using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NativeCompiler;

// FEATURE: Directory.Build.props evaluation (OrchardCore canary, see "Future work" in main.md).
// This is a self-contained, optional feature: it only activates when a Directory.Build.props is found
// walking up from a project folder, and projects without one behave exactly as before.
//
// To DELETE the feature: remove this file, revert the property getters in Project.cs (the ones marked
// "Feature: Directory.Build.props"), and drop DirectoryBuildPropsTests.cs. No other parts use it.
//
// Scope is intentionally minimal - only the compile-relevant properties already honored by
// NativeCompiler (TargetFramework(s), ImplicitUsings, Nullable, LangVersion, AllowUnsafeBlocks,
// DefineConstants). MSBuild conditions, targets execution and item injection are NOT evaluated.
public sealed record BuildProps(
    string? TargetFramework,
    bool ImplicitUsings,
    string? Nullable,
    string? LangVersion,
    bool AllowUnsafeBlocks,
    bool UseAppHost,
    bool EnableRequestDelegateGenerator,
    string? DefineConstants,
    IReadOnlyList<string> AnalyzerProjectReferences,
    string? InterceptorsNamespaces);

// Assembly-identity/versioning properties that the SDK's GenerateAssemblyInfo turns into
// attributes. Project-level <PropertyGroup> values win over Directory.Build.props (document order).
public sealed record BuildIdentity(
    string? Company,
    string? Product,
    string? Title,
    string? Configuration,
    string? Version,
    string? AssemblyVersion,
    string? FileVersion,
    string? InformationalVersion,
    string? SourceRevisionId);

public static class BuildPropsEvaluator
{
    public static BuildProps Evaluate(string projectRootPath)
    {
        try
        {
            var propsFile = FindNearestPropsFile(projectRootPath);
            if (propsFile is null) return Default;

            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var analyzerRefs = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Traverse(propsFile, props, analyzerRefs, visited);

            var targetFramework = Expand(props.GetValueOrDefault("TargetFramework"), props);
            if (string.IsNullOrEmpty(targetFramework))
            {
                var frameworks = Expand(props.GetValueOrDefault("TargetFrameworks"), props);
                if (string.IsNullOrEmpty(frameworks))
                {
                    // The project's own <TargetFrameworks>/<TargetFramework> declarations are read
                    // last (the SDK is not imported, so Directory.Build.props alone may not define
                    // the TFM). The outer property (e.g. $(CommonTargetFrameworks)) is expanded
                    // against the props table collected above.
                    frameworks = Expand(ReadProjectTfmElement(projectRootPath), props);
                }

                targetFramework = frameworks
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
            }

            var implicitUsings = Expand(props.GetValueOrDefault("ImplicitUsings"), props)
                .Contains("enable", StringComparison.OrdinalIgnoreCase);
            var allowUnsafe = Expand(props.GetValueOrDefault("AllowUnsafeBlocks"), props)
                .Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            var useAppHost = !Expand(props.GetValueOrDefault("UseAppHost"), props)
                .Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
            // Mirrors the SDK gate for the Request Delegate Generator (Microsoft.AspNetCore.Http
            // .RequestDelegateGenerator): off unless enabled, or the app is published trimmed/AOT
            // (not evaluated here - this pipeline does not publish).
            var enableRdg = Expand(props.GetValueOrDefault("EnableRequestDelegateGenerator"), props)
                .Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

            return new BuildProps(
                EmptyToNull(targetFramework),
                implicitUsings,
                EmptyToNull(Expand(props.GetValueOrDefault("Nullable"), props)),
                EmptyToNull(Expand(props.GetValueOrDefault("LangVersion"), props)),
                allowUnsafe,
                useAppHost,
                enableRdg,
                EmptyToNull(Expand(props.GetValueOrDefault("DefineConstants"), props)),
                analyzerRefs,
                EmptyToNull(Expand(props.GetValueOrDefault("InterceptorsNamespaces") ??
                       props.GetValueOrDefault("InterceptorsPreviewNamespaces"), props, "InterceptorsNamespaces")));
        }
        catch
        {
            // Fail-safe: any evaluation problem degrades to the props-free behavior.
            return Default;
        }
    }

    private static readonly BuildProps Default = new(null, false, null, null, false, true, false, null, Array.Empty<string>(), null);

    // Resolves the assembly-identity properties for a project folder. Collects Directory.Build.props
    // (with its imports) then overlays the project's own <PropertyGroup> declarations, which override
    // because the SDK imports Directory.Build.props before the project body.
    public static BuildIdentity? EvaluateIdentity(string projectRootPath)
    {
        try
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var analyzerRefs = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var propsFile = FindNearestPropsFile(projectRootPath);
            if (propsFile is not null)
                Traverse(propsFile, props, analyzerRefs, visited);

            var csprojFile = Directory.EnumerateFiles(projectRootPath, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (csprojFile is not null)
            {
                var document = XDocument.Load(csprojFile);
                var root = document.Root;
                if (root is not null)
                {
                    var directory = Path.GetDirectoryName(Path.GetFullPath(csprojFile)) + Path.DirectorySeparatorChar;
                    props["MSBuildThisFileDirectory"] = directory;
                    // Last PropertyGroup wins, mirroring MSBuild document order.
                    foreach (var group in root.Elements().Where(e => e.Name.LocalName == "PropertyGroup"))
                        foreach (var property in group.Elements())
                            props[property.Name.LocalName] = property.Value.Trim();
                }
            }

            if (props.Count == 0) return null;

            string? Value(string name) => EmptyToNull(Expand(props.GetValueOrDefault(name), props));

            return new BuildIdentity(
                Value("Company"),
                Value("Product"),
                Value("Title"),
                Value("Configuration"),
                Value("Version"),
                Value("AssemblyVersion"),
                Value("AssemblyFileVersion"),
                Value("InformationalVersion"),
                Value("SourceRevisionId"));
        }
        catch
        {
            return null;
        }
    }

    private static string? FindNearestPropsFile(string projectRootPath)
    {
        if (string.IsNullOrWhiteSpace(projectRootPath) || !Directory.Exists(projectRootPath)) return null;

        var directory = new DirectoryInfo(Path.GetFullPath(projectRootPath));
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Directory.Build.props");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        return null;
    }

    // Reads the project's own first <PropertyGroup> TFM declarations (<TargetFramework> then
    // <TargetFrameworks>), so projects that only express the TFM via an outer property reference
    // (e.g. <TargetFrameworks>$(CommonTargetFrameworks)</TargetFrameworks>) still resolve it.
    private static string ReadProjectTfmElement(string projectRootPath)
    {
        try
        {
            var csprojFile = Directory.EnumerateFiles(projectRootPath, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (csprojFile is null) return string.Empty;

            var document = XDocument.Load(csprojFile);
            var firstPropertyGroup = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "PropertyGroup");
            if (firstPropertyGroup is null) return string.Empty;

            var targetFramework = firstPropertyGroup.Elements().FirstOrDefault(element => element.Name.LocalName == "TargetFramework")?.Value.Trim();
            if (!string.IsNullOrEmpty(targetFramework)) return targetFramework;

            return firstPropertyGroup.Elements().FirstOrDefault(element => element.Name.LocalName == "TargetFrameworks")?.Value.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void Traverse(string propsFilePath, Dictionary<string, string> props, List<string> analyzerRefs, HashSet<string> visited)
    {
        var fullPath = Path.GetFullPath(propsFilePath);
        if (!visited.Add(fullPath)) return;
        if (!File.Exists(fullPath)) return;

        XDocument document;
        try { document = XDocument.Load(fullPath); }
        catch { return; }

        var root = document.Root;
        if (root is null) return;

        var directory = Path.GetDirectoryName(fullPath) ?? "";
        props["MSBuildThisFileDirectory"] = directory + Path.DirectorySeparatorChar;

        foreach (var element in root.Elements())
        {
            var localName = element.Name.LocalName;

            if (localName == "Import")
            {
                var project = (string?)element.Attribute("Project") ?? "";
                if (project.Length == 0) continue;

                var expanded = Expand(project, props);
                var importPath = expanded
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                Traverse(Path.GetFullPath(Path.Combine(directory, importPath)), props, analyzerRefs, visited);
            }
            else if (localName == "PropertyGroup")
            {
                foreach (var property in element.Elements())
                    props[property.Name.LocalName] = property.Value.Trim(); // last definition wins
            }
            else if (localName == "ItemGroup")
            {
                // Analyzer project references injected from evaluated props (e.g. OrchardCore's
                // OrchardCore.Commons.props adds OrchardCore.SourceGenerators to every project).
                foreach (var item in element.Elements())
                {
                    if (item.Name.LocalName != "ProjectReference") continue;
                    var outputType = (string?)item.Attribute("OutputItemType") ?? "";
                    if (!outputType.Contains("Analyzer", StringComparison.OrdinalIgnoreCase)) continue;

                    var include = Expand((string?)item.Attribute("Include") ?? "", props);
                    if (string.IsNullOrWhiteSpace(include)) continue;

                    include = include
                        .Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar);
                    var full = Path.GetFullPath(Path.Combine(directory, include));
                    if (!analyzerRefs.Contains(full, StringComparer.OrdinalIgnoreCase))
                        analyzerRefs.Add(full);
                }
            }
        }
    }

    private const int MaxExpansionDepth = 16;

    private static readonly Regex PropertyToken = new(
        @"\$\(([^)]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string Expand(string? value, IReadOnlyDictionary<string, string> props, string? selfReference = null)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf("$(", StringComparison.Ordinal) < 0) return value ?? "";

        var current = value;
        for (var i = 0; i < MaxExpansionDepth; i++)
        {
            var next = PropertyToken.Replace(current, match =>
            {
                var name = match.Groups[1].Value;
                if (props.TryGetValue(name, out var resolved))
                    return string.Equals(name, selfReference, StringComparison.Ordinal) ? "" : resolved;
                return match.Value;
            });
            if (next == current) break;
            current = next;
        }

        return current;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}