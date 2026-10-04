using NativeCompiler.NuGet;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CustomCompiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

// ---------------------------------------------------------------------------
// Feature: real Roslyn source-generator pipeline (GENERATOR_PIPELINE).
// Mirrors the SDK: every project gets the ref-pack generators (GeneratedRegex,
// GenerateShape/[JsonSourceGenerator], LibraryImport, JSImport, ComInterface) plus
// NuGet package analyzers/dotnet/cs assemblies and, for projects holding the matching
// <FrameworkReference>, targeting-pack analyzers (e.g. AspNetCore.App's source generator
// that makes the implicit Program public). Generators run through
// CSharpGeneratorDriver.RunGeneratorsAndUpdateTree exactly like csc does; the
// generated trees are merged into the compilation and generator diagnostics are
// relayed as build errors. No hand-rolled code synthesis lives here. DELETABLE.
//
// DISABLED for the AOT experiment: the reflective/dynamic discovery below
// (CollectGeneratorDlls + GeneratorAssemblyLoadContext + Activator) is JIT-only —
// NativeAOT cannot load/reflect assemblies at runtime. BuildCore now routes the
// whole pipeline through AnalyzerRunner, which feeds the statically linked
// AnalyzerRegistry (SourceGeneratorRegistry + AnalyzerRegistry) to RunGeneratorDriver.
// The JIT discovery helpers below are left in place as the reference implementation;
// delete or comment out once the AOT static path is proven.
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

        // <FrameworkReference> targeting packs can ship their own analyzers (e.g. the AspNetCore
        // pack's Microsoft.AspNetCore.App.SourceGenerators, which emits `public partial class
        // Program {}` so minimal-API apps get the public Program the SDK produces). Mirror csc:
        // these only run for projects that actually hold the framework reference - web projects
        // get Microsoft.AspNetCore.App implicitly. The base Microsoft.NETCore.App pack is skipped
        // (its generators are the statically linked SDK set, already in AnalyzerRegistry).
        var frameworkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        NativeCompilerUtils.CollectFrameworkReferenceNames(project.ProjectRootPath, visitedProjects, frameworkNames);
        if (project.IsWebSdk) frameworkNames.Add("Microsoft.AspNetCore.App");

        foreach (var frameworkName in frameworkNames)
        {
            if (string.IsNullOrEmpty(frameworkName)) continue;
            if (frameworkName.Equals("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var refLocation = AsemblyExtensions.ResolveFrameworkRefLocation(frameworkName, tfm);
                var packVersionDir = Path.GetDirectoryName(Path.GetDirectoryName(refLocation));
                if (packVersionDir is null) continue;
                foreach (var dll in Directory.EnumerateFiles(
                    Path.Combine(packVersionDir, "analyzers", "dotnet", "cs"), "*.dll", SearchOption.AllDirectories))
                {
                    // The Request Delegate Generator is off-by-default in the SDK (gated behind
                    // EnableRequestDelegateGenerator, or PublishTrimmed/AOT). Mirror that gate,
                    // otherwise minimal-API apps compile request delegates - and pull in a very
                    // different reference surface than `dotnet build` - for no opt-in.
                    if (Environment.GetEnvironmentVariable("NATIVECOMPILER_TRACE_GENERATORS") == "1" &&
                        Path.GetFileName(dll).Contains("RequestDelegateGenerator", StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine($"[gens] {project.ProjectName}: RDG candidate={Path.GetFileName(dll)} enabled={project.EnableRequestDelegateGenerator}");
                    if (!project.EnableRequestDelegateGenerator &&
                        Path.GetFileName(dll)
                            .Equals("Microsoft.AspNetCore.Http.RequestDelegateGenerator.dll", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!IsRoslynDistribution(dll)) dlls.Add(dll);
                }
            }
            catch (Exception)
            {
                // Pack (or its analyzers subfolder) not installed; harmless - same tolerance the
                // framework-ref resolution already has for missing packs.
            }
        }

        // Analyzer project references (OutputItemType="Analyzer") ship generators from source
        // projects, e.g. OrchardCore.SourceGenerators (referenced from csproj XML by test
        // projects and injected into every project via the evaluated Commons.props import).
        var analyzerCsprojs = new List<string>();
        foreach (var reference in project.AnalyzerProjectReferences)
        {
            var include = (reference.Include ?? "").Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(include)) continue;
            if (SkippedSelf(project, include)) continue;

            var csproj = ProjectReferenceFinder.FindReferencedCsproj(include, project.ProjectRootPath);
            if (Environment.GetEnvironmentVariable("NATIVECOMPILER_TRACE_GENERATORS") == "1")
                Console.WriteLine($"[gens] {project.ProjectName}: analyzer ref '{include}' -> '{csproj}'");
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

        return (dlls.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(), warnings);
    }

    // The OrchardCore generator is injected with a condition excluding itself; mirrors that here.
    private static bool SkippedSelf(Project project, string csprojPath) =>
        string.Equals(project.ProjectName, Path.GetFileNameWithoutExtension(csprojPath),
            StringComparison.OrdinalIgnoreCase);

    private static string ResolveAnalyzerProjectDll(string csprojPath)
    {
        var projectDir = Path.GetDirectoryName(csprojPath) ?? "";
        var sharpBinDir = Project.GetSharpBinDirectory(projectDir, Project.ResolveTargetFramework(csprojPath));

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
    //
    // Generator set = the statically linked SDK set (AnalyzerRegistry, always, so JIT and AOT
    // behave identically) PLUS, under JIT only (RuntimeFeature.IsDynamicCodeSupported), the
    // project's own analyzers discovered from package `analyzers/dotnet/cs` folders and Analyzer
    // project references (e.g. OrchardCore.SourceGenerators, injected into every project via
    // the evaluated Commons.props import). AOT cannot reflect/load assemblies at runtime, so
    // project-specific generators are skipped there; the SDK set covers the common cases.
    public static (CSharpCompilation compilation, string errors) RunGenerators(
        CSharpCompilation compilation, Project project)
    {
        var generators = new List<ISourceGenerator>(AnalyzerRegistry.GetSourceGenerators());

        // Web-only generators. Under JIT the dynamic discovery below finds these anyway; adding them
        // here is what makes the AOT binary match, since AOT cannot load assemblies at runtime. The
        // gate matters: applied unconditionally, PublicProgramSourceGenerator also makes Program public
        // in console apps, which diverges from the SDK.
        if (project.IsWebSdk)
        {
            generators.AddRange(AnalyzerRegistry.GetWebSourceGenerators());
        }

        if (Environment.GetEnvironmentVariable("NATIVECOMPILER_TRACE_GENERATORS") == "1")
            Console.WriteLine($"[gens] {project.ProjectName}: IsWebSdk={project.IsWebSdk}, IsDynamicCodeSupported={System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}");

        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            var (generatorDlls, collectorWarnings) = CollectGeneratorDlls(project);
            PrintWarnings(collectorWarnings);

            if (Environment.GetEnvironmentVariable("NATIVECOMPILER_TRACE_GENERATORS") == "1")
                Console.WriteLine($"[gens] {project.ProjectName}: dynamic supported, discovered {generatorDlls.Count} dlls: [{string.Join("; ", generatorDlls.Select(path => Path.GetFileName(path)))}]");

            if (generatorDlls.Count > 0)
            {
                try
                {
                    var loadContext = new GeneratorAssemblyLoadContext();
                    var warnings = new List<string>();
                    foreach (var dll in generatorDlls) loadContext.RegisterCopy(dll);
                    foreach (var dll in generatorDlls)
                    {
                        Assembly assembly;
                        try
                        {
                            assembly = loadContext.LoadGenerator(dll);
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Warning: cannot load generator '{Path.GetFileNameWithoutExtension(dll)}': {ex.Message}");
                            continue;
                        }

                        Type[] types;
                        try { types = assembly.GetTypes(); }
                        catch (ReflectionTypeLoadException typeLoad) { types = typeLoad.Types.Where(t => t is not null).ToArray()!; }
                        catch (Exception ex)
                        {
                            warnings.Add($"Warning: cannot inspect generator '{Path.GetFileNameWithoutExtension(dll)}': {ex.Message}");
                            continue;
                        }

                        if (Environment.GetEnvironmentVariable("NATIVECOMPILER_TRACE_GENERATORS") == "1")
                            Console.WriteLine($"[gens] {project.ProjectName}: loaded {assembly.GetName().Name}, {types.Length} types");

                        foreach (var type in types)
                        {
                            try
                            {
                                if (type.IsAbstract || type.IsInterface) continue;
                                if (type.GetCustomAttribute<GeneratorAttribute>() is null) continue;
                                if (type.GetConstructor(Type.EmptyTypes) is null) continue;
                                var instance = Activator.CreateInstance(type);
                                if (instance is ISourceGenerator sourceGenerator) generators.Add(sourceGenerator);
                                else if (instance is IIncrementalGenerator incrementalGenerator) generators.Add(incrementalGenerator.AsSourceGenerator());
                            }
                            catch (Exception ex)
                            {
                                warnings.Add($"Warning: cannot instantiate generator '{type.FullName}': {ex.Message}");
                            }
                        }
                    }

                    PrintWarnings(warnings);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: generator discovery failed: {ex.Message}");
                }
            }
        }

        return RunGeneratorDriver(compilation, project, generators.ToImmutableArray());
    }

    // Shared driver: builds CSharpGeneratorDriver with the project's parse options (implicit TFM
    // preprocessor constants, interceptor namespaces) and a build-property AnalyzerConfigOptions,
    // runs the given generator set, re-parses generated trees with the project's parse options,
    // and returns the merged compilation plus error-severity generator diagnostics. Used by the
    // AOT pipeline (AnalyzerRunner feeds it AnalyzerRegistry.GetSourceGenerators()).
    internal static (CSharpCompilation compilation, string errors) RunGeneratorDriver(
        CSharpCompilation compilation, Project project, ImmutableArray<ISourceGenerator> generators)
    {
        if (generators.IsEmpty)
            return (compilation, "");

        if (Environment.GetEnvironmentVariable("NATIVECOMPILER_TRACE_GENERATORS") == "1")
            Console.WriteLine($"[gens] running {generators.Length} generators for {project.ProjectName}");

        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(ResolveLanguageVersion(project.LangVersion))
            .WithPreprocessorSymbols(NativeCompilerUtils.GetSdkPreprocessorSymbols(project));
        parseOptions = NativeCompilerUtils.ApplyInterceptorsNamespaces(parseOptions, project.InterceptorsNamespaces);

        if (Environment.GetEnvironmentVariable("NATIVECOMPILER_TRACE_GENERATORS") == "1")
            Console.WriteLine($"[gens] {project.ProjectName}: interceptors after seed='{NativeCompilerUtils.GetInterceptorsNamespacesOrEmpty(parseOptions)}'");

        var optionsProvider = new ProjectConfigOptionsProvider(project);
        var driver = CSharpGeneratorDriver.Create(
            generators,
            additionalTexts: ImmutableArray<AdditionalText>.Empty,
            parseOptions: parseOptions,
            optionsProvider: optionsProvider);

        var runResult = driver.RunGenerators(compilation).GetRunResult();

        // Only error-severity generator diagnostics gate the build; load/init warnings are
        // informational only (they would otherwise fail projects that have no real error).
        var errors = runResult.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();

        if (runResult.GeneratedTrees.Length > 0)
        {
            // Re-parse generated trees with the project's parse options (TMF constants and
            // interceptor namespaces apply to generated [InterceptsLocation] code too). Sources of
            // interceptors also seed their own declared namespaces (e.g. the Configuration.Binder
            // generator emits [InterceptsLocation] inside Microsoft.Extensions.Configuration.Binder.
            // SourceGeneration, a namespace that the SDK injects through the package's build targets
            // rather than any project property). Fold those in so generated interceptor callbacks
            // are always opted in regardless of MSBuild wiring.
            var seeding = parseOptions;
            // Fold in namespaces that generated [InterceptsLocation] trees declare themselves on
            // top of the project's configured interceptor namespaces.
            var generatedNamespaces = new List<string>();
            foreach (var tree in runResult.GeneratedTrees)
            {
                if (tree.ToString().IndexOf("InterceptsLocation", StringComparison.Ordinal) < 0) continue;
                generatedNamespaces.AddRange(CollectDeclaredNamespaces(tree));
            }
            if (generatedNamespaces.Count > 0)
            {
                var combined = string.Join(";", generatedNamespaces.Distinct(StringComparer.Ordinal)
                    .Append(project.InterceptorsNamespaces ?? ""));
                seeding = NativeCompilerUtils.ApplyInterceptorsNamespaces(seeding, combined);
            }
            parseOptions = seeding;

            var generatedTrees = runResult.GeneratedTrees.Select(tree =>
                CSharpSyntaxTree.ParseText(tree.GetText(), parseOptions,
                    Path.Combine(NativeCompilerUtils.GetGeneratedFileRoot(project), tree.FilePath)));
            compilation = compilation.AddSyntaxTrees(generatedTrees);
        }

        return (compilation, string.Join("\n", errors));
    }

    // The namespace(s) a generated tree declares (e.g. from the file that holds [InterceptsLocation]).
    private static List<string> CollectDeclaredNamespaces(SyntaxTree tree)
    {
        var namespaces = new List<string>();
        foreach (var node in tree.GetRoot().DescendantNodesAndSelf())
        {
            if (node is BaseNamespaceDeclarationSyntax ns)
                namespaces.Add(ns.Name.ToString());
        }
        return namespaces.Distinct(StringComparer.Ordinal).ToList();
    }

    // Build-property AnalyzerConfigOptions for both the generator driver and CompilationWithAnalyzers.
    internal static AnalyzerConfigOptionsProvider CreateOptionsProvider(Project project)
        => new ProjectConfigOptionsProvider(project);

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