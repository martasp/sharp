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
    string? DefineConstants);

public static class BuildPropsEvaluator
{
    public static BuildProps Evaluate(string projectRootPath)
    {
        try
        {
            var propsFile = FindNearestPropsFile(projectRootPath);
            if (propsFile is null) return Default;

            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Traverse(propsFile, props, visited);

            var targetFramework = Expand(props.GetValueOrDefault("TargetFramework"), props);
            if (string.IsNullOrEmpty(targetFramework))
            {
                var frameworks = Expand(props.GetValueOrDefault("TargetFrameworks"), props);
                targetFramework = frameworks
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
            }

            var implicitUsings = Expand(props.GetValueOrDefault("ImplicitUsings"), props)
                .Contains("enable", StringComparison.OrdinalIgnoreCase);
            var allowUnsafe = Expand(props.GetValueOrDefault("AllowUnsafeBlocks"), props)
                .Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

            return new BuildProps(
                EmptyToNull(targetFramework),
                implicitUsings,
                EmptyToNull(Expand(props.GetValueOrDefault("Nullable"), props)),
                EmptyToNull(Expand(props.GetValueOrDefault("LangVersion"), props)),
                allowUnsafe,
                EmptyToNull(Expand(props.GetValueOrDefault("DefineConstants"), props)));
        }
        catch
        {
            // Fail-safe: any evaluation problem degrades to the props-free behavior.
            return Default;
        }
    }

    private static readonly BuildProps Default = new(null, false, null, null, false, null);

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

    private static void Traverse(string propsFilePath, Dictionary<string, string> props, HashSet<string> visited)
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
                Traverse(Path.GetFullPath(Path.Combine(directory, importPath)), props, visited);
            }
            else if (localName == "PropertyGroup")
            {
                foreach (var property in element.Elements())
                    props[property.Name.LocalName] = property.Value.Trim(); // last definition wins
            }
        }
    }

    private const int MaxExpansionDepth = 16;

    private static readonly Regex PropertyToken = new(
        @"\$\(([^)]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string Expand(string? value, IReadOnlyDictionary<string, string> props)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf("$(", StringComparison.Ordinal) < 0) return value ?? "";

        var current = value;
        for (var i = 0; i < MaxExpansionDepth; i++)
        {
            var next = PropertyToken.Replace(current, match => props.TryGetValue(match.Groups[1].Value, out var resolved)
                ? resolved
                : match.Value);
            if (next == current) break;
            current = next;
        }

        return current;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}