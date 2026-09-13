using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

// ---------------------------------------------------------------------------
// Feature: real Roslyn source-generator pipeline (GENERATOR_PIPELINE).
// Mirrors the SDK: every project gets the ref-pack generators (GeneratedRegex,
// GenerateShape/[JsonSourceGenerator], LibraryImport, JSImport, ComInterface) plus
// NuGet package analyzers/dotnet/cs assemblies. Generators run through
// CSharpGeneratorDriver.RunGeneratorsAndUpdateTree exactly like csc does; the
// generated trees are merged into the compilation and generator diagnostics are
// relayed as build errors. No hand-rolled code synthesis lives here. DELETABLE.
// ---------------------------------------------------------------------------
namespace NativeCompiler;

public static class SourceGeneratorRunner
{
    // Package analyzer folders are enumerated once per package dir (shared across projects).
    private static readonly ConcurrentDictionary<string, List<string>> AnalyzerDllCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static (List<string> Dlls, List<string> Warnings) CollectGeneratorDlls(Project project)
    {
        var dlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var tfm = project.TargetFramework ?? "net" + Environment.Version.Major + ".0";

        foreach (var item in project.PackageReferences)
        {
            var packageName = item.Include ?? "";
            var packageVersion = item.Version ??
                NuGetPackage.ResolveCentralVersion(packageName, project.ProjectRootPath);
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(packageVersion)) continue;

            var packagePath = NuGetPackage.FindPackagePath(packageName, packageVersion);
            if (string.IsNullOrEmpty(packagePath)) continue;

            AddAnalyzerDlls(dlls, packagePath);
            foreach (var transitive in NuGetPackage.GetTransitivePackagePaths(packagePath, tfm))
                AddAnalyzerDlls(dlls, transitive);
        }

        // Analyzer project references (OutputItemType="Analyzer") ship generators from source
        // projects, e.g. OrchardCore.SourceGenerators (referenced from csproj XML by test
        // projects and injected into every project via the evaluated Commons.props import).
        var analyzerCsprojs = new List<string>();
        foreach (var reference in project.AnalyzerProjectReferences)
        {
            var include = reference.Include ?? "";
            if (string.IsNullOrEmpty(include)) continue;
            if (SkippedSelf(project, include)) continue;

            var csproj = ProjectReferenceFinder.FindReferencedCsproj(include, project.ProjectRootPath);
            if (csproj is not null) analyzerCsprojs.Add(csproj);
        }

        foreach (var csproj in project.BuildPropsAnalyzerProjectReferences)
        {
            if (SkippedSelf(project, csproj)) continue;
            analyzerCsprojs.Add(csproj);
        }

        foreach (var csproj in analyzerCsprojs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var dll = ResolveAnalyzerProjectDll(csproj);
                dlls.Add(dll);
            }
            catch (Exception ex)
            {
                warnings.Add($"Warning: cannot use analyzer project '{Path.GetFileNameWithoutExtension(csproj)}': {ex.Message}");
            }
        }

        // Ref-pack generators are implicit for every net project (what the SDK feeds csc).
        AddAnalyzerDlls(dlls, AsemblyExtensions.ResolveRefPackAnalyzersLocation);

        return (dlls.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), warnings);
    }

    // The OrchardCore generator is injected with a condition excluding itself; mirrors that here.
    private static bool SkippedSelf(Project project, string csprojPath) =>
        string.Equals(project.ProjectName, Path.GetFileNameWithoutExtension(csprojPath),
            StringComparison.OrdinalIgnoreCase);

    private static string ResolveAnalyzerProjectDll(string csprojPath)
    {
        var projectDir = Path.GetDirectoryName(csprojPath) ?? "";
        var sharpBinDir = Path.Combine(projectDir, "bin", "sharpbin");

        if (!Directory.Exists(sharpBinDir)) NativeCompiler.Build(projectDir);

        var dll = Path.Combine(sharpBinDir, Path.GetFileNameWithoutExtension(csprojPath) + ".dll");
        if (!File.Exists(dll)) throw new Exception($"Cant find dll path {dll}");
        return dll;
    }

    private static void AddAnalyzerDlls(HashSet<string> dlls, string packagePath)
    {
        if (!Directory.Exists(packagePath)) return;

        var cached = AnalyzerDllCache.GetOrAdd(packagePath, path => Directory
            .EnumerateFiles(path, "*.dll", SearchOption.AllDirectories)
            .Where(dll => dll.Replace('\\', '/')
                .Contains("/analyzers/dotnet/cs/", StringComparison.OrdinalIgnoreCase))
            .Where(dll => !IsRoslynDistribution(dll))
            .OrderBy(dll => dll, StringComparer.OrdinalIgnoreCase)
            .ToList());

        foreach (var dll in cached) dlls.Add(dll);
    }

    // The compiler's own assemblies shipped in analyzer folders (e.g. the
    // Microsoft.CodeAnalysis.Analyzers package carries M.CA.dll/M.CA.CSharp.dll at a dev
    // version that clashes with the compiler process). csc never runs them as generators.
    private static bool IsRoslynDistribution(string dllPath)
    {
        var name = Path.GetFileNameWithoutExtension(dllPath);
        return name.Equals("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Microsoft.CodeAnalysis.CSharp", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Microsoft.CodeAnalysis.Analyzers", StringComparison.OrdinalIgnoreCase)
            // Analyzer (not generator) package: depends on M.CA.Analyzers, which is excluded above,
            // so it can never resolve; loading it only produces misleading warnings.
            || name.Equals("Microsoft.CodeAnalysis.CSharp.Analyzers", StringComparison.OrdinalIgnoreCase);
    }

    // Runs the project's generators and returns the compilation with generated trees merged
    // plus any generator-reported error diagnostics.
    public static (CSharpCompilation compilation, string errors) RunGenerators(
        CSharpCompilation compilation, Project project)
    {
        var (generatorDlls, collectorWarnings) = CollectGeneratorDlls(project);
        if (generatorDlls.Count == 0)
        {
            PrintWarnings(collectorWarnings);
            return (compilation, "");
        }

        try
        {
            var loadContext = new GeneratorAssemblyLoadContext();
            var generators = new List<ISourceGenerator>();
            var warnings = new List<string>(collectorWarnings);

            // Resolving interop-generator references (LibraryImportGenerator ->
            // Microsoft.Interop.SourceGeneration) needs the whole set mapped before any assembly
            // is loaded, since the dependency dlls sort after the generators themselves.
            foreach (var dll in generatorDlls)
                loadContext.RegisterCopy(dll);

            foreach (var dll in generatorDlls)
            {
                List<Type>? types;
                try
                {
                    // Dependencies are visible inside the dedicated context through its Resolving
                    // handler (e.g. LibraryImportGenerator -> Microsoft.Interop.SourceGeneration,
                    // both shadow-copied from the ref-pack analyzers folder). Loading into the
                    // default ALC fails for such co-located deps because the shadow copies are
                    // renamed (Assembly.LoadFrom on an interop generator -> FileNotFoundException,
                    // leaving [LibraryImport] methods without an implementation => CS8795).
                    var assembly = loadContext.LoadGenerator(dll);
                    types = assembly.GetTypes().ToList();
                }
                catch (ReflectionTypeLoadException typeLoad)
                {
                    var missing = typeLoad.LoaderExceptions
                        .Where(ex => ex is not null)
                        .Select(ex => ex!.Message)
                        .Distinct();
                    warnings.Add($"Warning: failed to load generator '{Path.GetFileName(dll)}': " +
                        string.Join(" | ", missing));
                    continue;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Warning: failed to load generator '{Path.GetFileName(dll)}': {ex.Message}");
                    continue;
                }

                foreach (var type in types)
                {
                    try
                    {
                        if (type.IsAbstract || type.IsInterface) continue;
                        if (type.GetCustomAttribute<GeneratorAttribute>() is null) continue;
                        if (type.GetConstructor(Type.EmptyTypes) is null) continue;

                        var instance = Activator.CreateInstance(type);
                        if (instance is ISourceGenerator sourceGenerator)
                            generators.Add(sourceGenerator);
                        else if (instance is IIncrementalGenerator incrementalGenerator)
                            generators.Add(incrementalGenerator.AsSourceGenerator());
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Warning: failed to instantiate generator '{type.FullName}': {ex.Message}");
                    }
                }
            }

            if (generators.Count == 0)
            {
                PrintWarnings(warnings);
                return (compilation, "");
            }

            var parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(ResolveLanguageVersion(project.LangVersion))
                .WithPreprocessorSymbols(NativeCompilerUtils.GetSdkPreprocessorSymbols(project));
            parseOptions = NativeCompilerUtils.ApplyInterceptorsNamespaces(parseOptions, project.InterceptorsNamespaces);

            var optionsProvider = new ProjectConfigOptionsProvider(project);
            var driver = CSharpGeneratorDriver.Create(
                generators.ToImmutableArray(),
                additionalTexts: ImmutableArray<AdditionalText>.Empty,
                parseOptions: parseOptions,
                optionsProvider: optionsProvider);

            var runResult = driver.RunGenerators(compilation).GetRunResult();

            // Only error-severity generator diagnostics gate the build; load/init warnings are
            // informational only (they would otherwise fail projects that have no real error).
            PrintWarnings(warnings);
            var errors = runResult.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())
                .ToList();

            if (runResult.GeneratedTrees.Length > 0)
            {
                // Re-parse generated trees with the project's parse options (TMF constants and
                // interceptor namespaces apply to generated [InterceptsLocation] code too).
                var generatedTrees = runResult.GeneratedTrees.Select(tree =>
                    CSharpSyntaxTree.ParseText(tree.GetText(), parseOptions, tree.FilePath));
                compilation = compilation.AddSyntaxTrees(generatedTrees);
            }

            return (compilation, string.Join("\n", errors));
        }
        catch (Exception ex)
        {
            return (compilation, ex.ToString());
        }
    }

    private static void PrintWarnings(List<string> warnings)
    {
        foreach (var warning in warnings)
            Console.WriteLine(warning);
    }

    // Assembly.LoadFrom keeps the file mapped for the lifetime of the default ALC, which would
    // prevent a later rebuild from overwriting the analyzer dll (SDK projects are recompiled into
    // the same sharpbin path). Load a private copy keyed by last-write so the original stays free.
    private static string ShadowCopy(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "NativeCompilerGenerators");
        Directory.CreateDirectory(dir);

        var ticks = File.GetLastWriteTimeUtc(source).Ticks;
        var target = Path.Combine(dir,
            $"{Path.GetFileNameWithoutExtension(source)}.{ticks}.{source.GetHashCode():x}.dll");

        if (!File.Exists(target))
            File.Copy(source, target);

        return target;
    }

    // ---------------------------------------------------------------------------
    // Per-run load context for generator assemblies. Every analyzer dll from this project's
    // set is shadow-copied (renamed), and the handler below maps a requested simple name back
    // to its copy, so generators with co-located dependencies in the same analyzers folder
    // (LibraryImport/JSImport/ComInterface -> Microsoft.Interop.SourceGeneration) load no
    // matter the load order or hosting process. Microsoft.CodeAnalysis* is deliberately not
    // registered here: leaving the handler null for it falls back to the host's Roslyn, which
    // is what the driver + the generator interfaces are bound against.
    // ---------------------------------------------------------------------------
    private sealed class GeneratorAssemblyLoadContext : AssemblyLoadContext
    {
        // lowercase simple name -> shadow copy, newest build wins when a package set supplies
        // multiple versions of the same assembly.
        private readonly Dictionary<string, string> _copies =
            new(StringComparer.OrdinalIgnoreCase);

        public GeneratorAssemblyLoadContext()
            : base("NativeCompilerGenerators", isCollectible: true)
        {
            Resolving += (_, name) => LoadDependency(name);
        }

        public Assembly LoadGenerator(string sourcePath) => LoadFromAssemblyPath(Register(sourcePath));

        // Shadow-copies the source dll into the shared temp dir and maps its simple name to the
        // copy so the Resolving handler can satisfy cross-references between co-located analyzer
        // dlls. Pre-register the whole set before loading any assembly: dependency dlls like
        // Microsoft.Interop.SourceGeneration sort AFTER the generators that reference them and must
        // already be mapped before the first generator's GetTypes() triggers dependency resolution.
        public void RegisterCopy(string sourcePath) => Register(sourcePath);

        private string Register(string sourcePath)
        {
            var copy = ShadowCopy(sourcePath);
            var simpleName = Path.GetFileNameWithoutExtension(sourcePath);
            if (!_copies.TryGetValue(simpleName, out var existing) ||
                LastWriteTick(copy) > LastWriteTick(existing))
                _copies[simpleName] = copy;
            return copy;
        }

        private Assembly? LoadDependency(AssemblyName name)
        {
            if (!_copies.TryGetValue(name.Name ?? "", out var copy)) return null;
            return LoadFromAssemblyPath(copy);
        }

        private static long LastWriteTick(string shadowPath)
        {
            var name = Path.GetFileNameWithoutExtension(shadowPath);
            var parts = name.Split('.');
            return parts.Length >= 3 && long.TryParse(parts[^2], out var ticks) ? ticks : 0;
        }
    }

    private static LanguageVersion ResolveLanguageVersion(string? langVersion)
    {
        if (!string.IsNullOrWhiteSpace(langVersion) &&
            Enum.TryParse(langVersion.Trim(), ignoreCase: true, out LanguageVersion resolved))
            return resolved;
        return LanguageVersion.Latest;
    }

    // ---------------------------------------------------------------------------
    // Standard BuildProperties the SDK feeds analyzers/generators (ProjectSystem-provided keys
    // like build_property.MSBuildProjectFullPath). Kept general. DELETABLE FEATURE.
    // ---------------------------------------------------------------------------
    private sealed class ProjectConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly Project _project;
        private readonly AnalyzerConfigOptions _options;

        public ProjectConfigOptionsProvider(Project project)
        {
            _project = project;
            _options = new ProjectAnalyzerConfigOptions(project);
        }

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree? tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class ProjectAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly Project _project;

        public ProjectAnalyzerConfigOptions(Project project) => _project = project;

        public override bool TryGetValue([NotNullWhen(true)] string key, [NotNullWhen(true)] out string? value)
        {
            value = key switch
            {
                "build_property.MSBuildProjectFullPath" => _project.CsProjFile.FilePath,
                "build_property.ProjectDir" => _project.ProjectRootPath.EndsWith(Path.DirectorySeparatorChar)
                    ? _project.ProjectRootPath
                    : _project.ProjectRootPath + Path.DirectorySeparatorChar,
                "build_property.RootNamespace" => _project.ProjectName,
                "build_property.LangVersion" => _project.LangVersion,
                "build_property.Nullable" => _project.Nullable,
                "build_property.TargetFramework" => _project.TargetFramework,
                "build_property.AllowUnsafeBlocks" => _project.AllowUnsafeBlocks ? "true" : "false",
                _ => null
            };
            return value is not null;
        }
    }
}