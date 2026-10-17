using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NativeCompiler;

// FEATURE: Razor source-generator support.

public sealed record RazorInput(string FullPath, string TargetPath, bool IsComponent);

public static class RazorSupport
{
    // Directory names MSBuild's default item globs never descend into (plus VCS/tool folders that
    // would otherwise contribute stray templates).
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "myc", "sharpbin", "TestResults", ".vs", ".idea", ".git", "node_modules"
    };

    public static bool UsesRazorSdk(string? sdk) =>
        (sdk ?? "").Split(';')
            .Select(part => part.Trim())
            .Any(part => part.Equals("Microsoft.NET.Sdk.Razor", StringComparison.OrdinalIgnoreCase)
                         || part.Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<RazorInput> Collect(
        string projectRootPath, bool isWebSdk, bool defaultItemsEnabled,
        bool generateEnabled, bool componentEnabled)
    {
        // Mirror the SDK's default-item gates. EnableDefaultItems=false removes the Content glob the
        // Razor items are derived from; EnableDefaultRazorGenerateItems/ComponentItems=false removes
        // the per-kind defaults (the explicit <RazorGenerate>/<RazorComponent> items are not handled
        // here - no known consumer relies on them).
        if (!defaultItemsEnabled) return Array.Empty<RazorInput>();
        if (!generateEnabled && !componentEnabled) return Array.Empty<RazorInput>();

        var root = Path.GetFullPath(projectRootPath);
        var inputs = new List<RazorInput>();

        // wwwroot is excluded from the web Content glob (DefaultWebContentItemExcludes).
        var excludeWwwRoot = isWebSdk;

        foreach (var file in Enumerate(root, root, excludeWwwRoot))
        {
            var extension = Path.GetExtension(file);
            bool isCshtml = extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase);
            bool isRazor = extension.Equals(".razor", StringComparison.OrdinalIgnoreCase);
            if (isCshtml ? !generateEnabled : isRazor ? !componentEnabled : true) continue;
            if (!isCshtml && !isRazor) continue;

            var targetPath = Path.GetRelativePath(root, file).Replace('\\', '/');
            inputs.Add(new RazorInput(file, targetPath, IsComponent: isRazor));
        }

        return inputs;
    }

    // The Razor source generator reads build_metadata.AdditionalFiles.TargetPath as base64 (the SDK's
    // EncodeRazorInputItem round-trips the path through a workaround for MSBuild item-spec escaping).
    public static string EncodeTargetPath(string targetPath) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(targetPath));

    // .cshtml/.razor inputs are handed to the compiler as AdditionalFiles.
    public static ImmutableArray<AdditionalText> BuildAdditionalTexts(Project project)
    {
        var inputs = project.RazorInputs;
        if (inputs.Count == 0) return ImmutableArray<AdditionalText>.Empty;

        var builder = ImmutableArray.CreateBuilder<AdditionalText>(inputs.Count);
        foreach (var input in inputs) builder.Add(new RazorAdditionalText(input.FullPath));
        return builder.MoveToImmutable();
    }

    // The Razor generator reads each additional file's logical path from TargetPath (base64) plus
    // its CSS scope. Non-Razor additional files get none.
    public static IReadOnlyDictionary<string, AnalyzerConfigOptions> BuildFileOptions(Project project)
    {
        var inputs = project.RazorInputs;
        if (inputs.Count == 0) return EmptyFileOptions;

        var map = new Dictionary<string, AnalyzerConfigOptions>(StringComparer.Ordinal);
        foreach (var input in inputs)
            map[input.FullPath] = new RazorFileOptions(EncodeTargetPath(input.TargetPath));
        return map;
    }

    private static readonly IReadOnlyDictionary<string, AnalyzerConfigOptions> EmptyFileOptions =
        new Dictionary<string, AnalyzerConfigOptions>(StringComparer.Ordinal);

    // CompilerVisibleProperties the Razor generator reads as global build properties.
    public static bool TryGetBuildProperty(Project project, string key, out string? value)
    {
        switch (key)
        {
            case "build_property.RazorLangVersion":
                value = project.RazorLangVersion;
                return value is not null;
            case "build_property.MSBuildProjectDirectory":
                value = project.ProjectRootPath.TrimEnd(Path.DirectorySeparatorChar);
                return true;
            case "build_property.SupportLocalizedComponentNames":
            case "build_property.GenerateRazorMetadataSourceChecksumAttributes":
            case "build_property._RazorSourceGeneratorDebug":
                value = "";
                return true;
            default:
                value = null;
                return false;
        }
    }

    private sealed class RazorAdditionalText : AdditionalText
    {
        private readonly string _path;
        private SourceText? _text;

        public RazorAdditionalText(string path) => _path = path;

        public override string Path => _path;

        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) =>
            _text ??= SourceText.From(File.ReadAllText(_path), Encoding.UTF8);
    }

    private sealed class RazorFileOptions : AnalyzerConfigOptions
    {
        private readonly string _targetPath;

        public RazorFileOptions(string targetPath) => _targetPath = targetPath;

        public override bool TryGetValue([NotNullWhen(true)] string key, [NotNullWhen(true)] out string? value)
        {
            switch (key)
            {
                case "build_metadata.AdditionalFiles.TargetPath":
                    value = _targetPath;
                    return true;
                case "build_metadata.AdditionalFiles.CssScope":
                    value = "";
                    return true;
                default:
                    value = null;
                    return false;
            }
        }
    }

    private static IEnumerable<string> Enumerate(string root, string directory, bool excludeWwwRoot)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly))
        {
            var extension = Path.GetExtension(file);
            if (extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".razor", StringComparison.OrdinalIgnoreCase))
                yield return file;
        }

        foreach (var subDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(subDirectory);
            if (ExcludedDirectories.Contains(name)) continue;
            if (excludeWwwRoot && name.Equals("wwwroot", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var file in Enumerate(root, subDirectory, excludeWwwRoot))
                yield return file;
        }
    }

    // Razor projects pull their generator from the SDK's Microsoft.NET.Sdk.Razor component (no
    // reference pack or NuGet package carries Microsoft.CodeAnalysis.Razor.Compiler.dll). The
    // payload stages those DLLs on a machine with the SDK installed; when absent, .cshtml/.razor
    // files simply are not compiled (the same degradation as a razor-less build).
    public static void AddGeneratorDlls(Project project, HashSet<string> dlls)
    {
        if (project.RazorInputs.Count == 0) return;
        var razorDir = EmbeddedSdkRefs.RazorSourceGenerators;
        if (Directory.Exists(razorDir))
        {
            foreach (var dll in Directory.EnumerateFiles(razorDir, "*.dll", SearchOption.TopDirectoryOnly))
                dlls.Add(dll);
        }
        else if (Environment.GetEnvironmentVariable("NATIVECOMPILER_TRACE_GENERATORS") == "1")
        {
            Console.WriteLine($"[gens] {project.ProjectName}: {project.RazorInputs.Count} Razor files but no Razor source generators staged at '{razorDir}'");
        }
    }
}
