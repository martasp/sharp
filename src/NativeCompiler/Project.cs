using Microsoft.CodeAnalysis;
using XML;
using ProjectReference = XML.ProjectReference;
using FrameworkReference = XML.FrameworkReference;

namespace NativeCompiler;

public class Project : IProject
{
    public Project(ProjectFile csProjFile, List<ProjectFile> csFiles, string projectRootPath)
    {
        CsProjFile = csProjFile;
        //Appsettings = appsettings;
        CSFiles = csFiles;
        ProjectRootPath = projectRootPath;
    }

    public ProjectFile CsProjFile { get; set; }

    //public ProjectFile Appsettings { get; set; }
    public List<ProjectFile> CSFiles { get; set; }
    public string ProjectRootPath { get; set; }
    public string PDBPathName => Path.Combine(SharpBinFolderPath, PDBName);
    public string SharpBinFolderPath => GetSharpBinDirectory(ProjectRootPath, TargetFramework);
    private XML.Project? _xmlProject;
    private XML.Project XMLProject => _xmlProject ??= DeserializeObject(CsProjFile.FilePath); //Dont leak xml structure

    // Feature: Directory.Build.props (see BuildPropsEvaluator.cs). Lazily evaluated, csproj values win.
    private BuildProps? _buildProps;
    private BuildProps BuildProps => _buildProps ??= BuildPropsEvaluator.Evaluate(ProjectRootPath);

    public bool ImplicitUsings =>
        (XMLProject.PropertyGroup?.FirstOrDefault()?.ImplicitUsings?.Contains("enable") ?? false)
        || BuildProps.ImplicitUsings;

    public string? TargetFramework => XMLProject.PropertyGroup?.FirstOrDefault()?.TargetFramework
                                      ?? BuildProps.TargetFramework;

    public string? RuntimeIdentifier => XMLProject.PropertyGroup?.FirstOrDefault()?.RuntimeIdentifier;

    public bool AllowUnsafeBlocks =>
        (XMLProject.PropertyGroup?.FirstOrDefault()?.AllowUnsafeBlocks?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ?? false)
        || BuildProps.AllowUnsafeBlocks;

    // SDK default is to emit an apphost for Exe projects; <UseAppHost>false</UseAppHost> (as set by
    // NativeCompilerCLI itself, which must run inside the signed dotnet host) opts out. Mirrors the
    // SDK semantics for those exemptions.
    public bool UseAppHost =>
        !(XMLProject.PropertyGroup?.FirstOrDefault()?.UseAppHost?.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) ?? false)
        && BuildProps.UseAppHost;

    public string? Nullable => XMLProject.PropertyGroup?.FirstOrDefault()?.Nullable ?? BuildProps.Nullable;

    // SDK gate for the Request Delegate Generator (EnableRequestDelegateGenerator). Off unless the
    // project opts in (the SDK also flips it for PublishTrimmed/AOT, not evaluated here).
    public bool EnableRequestDelegateGenerator =>
        (XMLProject.PropertyGroup?.FirstOrDefault()?.EnableRequestDelegateGenerator?.Trim()
            .Equals("true", StringComparison.OrdinalIgnoreCase) ?? false)
        || BuildProps.EnableRequestDelegateGenerator;

    // SDK gate for the Configuration Binding Generator. Like the Request Delegate Generator it is
    // off-by-default (the SDK flips it on only for PublishTrimmed/PublishAot, handled by
    // BuildProps) and, unlike the other targeting-pack generators, it is interceptor-based rather
    // than attribute-driven: it emits glue for every `services.Configure<T>(config)` /
    // `configuration.Get<T>()` call site. Running it for a plain build emits code the SDK never
    // produces, which can collide with a project that defines its own
    // Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.
    public bool EnableConfigurationBindingGenerator =>
        (XMLProject.PropertyGroup?.FirstOrDefault()?.EnableConfigurationBindingGenerator?.Trim()
            .Equals("true", StringComparison.OrdinalIgnoreCase) ?? false)
        || BuildProps.EnableConfigurationBindingGenerator;

    public string? LangVersion => XMLProject.PropertyGroup?.FirstOrDefault()?.LangVersion ?? BuildProps.LangVersion;

    public string? DefineConstants => XMLProject.PropertyGroup?.FirstOrDefault()?.DefineConstants
                                      ?? BuildProps.DefineConstants;

    // Interceptor namespaces (SDK <InterceptorsNamespaces>/<InterceptorsPreviewNamespaces>) that
    // opt generated [InterceptsLocation] code in for csc parity (OrchardCore's shape generator).
    // A csproj value that references $(InterceptorsNamespaces) means "append to the inherited
    // value", so the props-chain value is folded in and the token is expanded away.
    public string? InterceptorsNamespaces
    {
        get
        {
            var declared = XMLProject.PropertyGroup?.FirstOrDefault()?.InterceptorsNamespaces;
            var preview = XMLProject.PropertyGroup?.FirstOrDefault()?.InterceptorsPreviewNamespaces;
            var csprojValue = declared ?? preview;
            var inherited = BuildProps.InterceptorsNamespaces;

            if (string.IsNullOrEmpty(csprojValue)) return inherited;
            if (inherited is null) return csprojValue;

            var touchesInherited = csprojValue.Contains("$(InterceptorsNamespaces)", StringComparison.OrdinalIgnoreCase)
                || csprojValue.Contains("$(InterceptorsPreviewNamespaces)", StringComparison.OrdinalIgnoreCase);
            if (!touchesInherited) return csprojValue;

            var surplus = csprojValue
                .Replace("$(InterceptorsNamespaces)", "", StringComparison.OrdinalIgnoreCase)
                .Replace("$(InterceptorsPreviewNamespaces)", "", StringComparison.OrdinalIgnoreCase);
            return inherited + surplus;
        }
    }
    public string ProjectName => Path.GetFileNameWithoutExtension(CsProjFile.FilePath);
    // SDK $(Version): the project's <Version> (or the Directory.Build.props overlay), defaulted to
    // "1.0.0" like the SDK. Becomes the entry's "Name/Version" key in deps.json.
    public string Version => BuildPropsEvaluator.EvaluateIdentity(ProjectRootPath)?.Version
                             ?? "1.0.0";
    public string DepsJsonPathName => Path.Combine(SharpBinFolderPath, ProjectName + ".deps.json");
    public string ExtensionName
    {
        get
        {
            var extension = ResolvedProjectExtensionType();
            return string.IsNullOrEmpty(extension) ? ProjectName : ProjectName + "." + extension;
        }
    }
    public string ExePathName => Path.Combine(SharpBinFolderPath, ExtensionName);
    public string DllPathName => Path.Combine(SharpBinFolderPath, ProjectName + ".dll");
    public string PDBName => ProjectName + ".pdb";
    public string NugetPackagesRootPath => GetNuGetCacheDirectory();

    public List<ProjectReference> ProjectReferences =>
        XMLProject.ItemGroup.SelectMany(item => item.ProjectReferences).ToList();

    // Project references wired as analyzers (<ProjectReference OutputItemType="Analyzer" ... />)
    // feed the source-generator pipeline (GENERATOR_PIPELINE). Kept general. DELETABLE FEATURE.
    public List<ProjectReference> AnalyzerProjectReferences =>
        ProjectReferences
            .Where(reference => string.Equals(reference.OutputItemType, "Analyzer",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

    // Same surface but resolved from the evaluated Directory.Build.props chain imports
    // (e.g. OrchardCore.Commons.props injects OrchardCore.SourceGenerators into every project).
    // Absolute csproj paths, nothing matched by the ProjectReference items above.
    public IReadOnlyList<string> BuildPropsAnalyzerProjectReferences => BuildProps.AnalyzerProjectReferences;

    public List<PackageReference> PackageReferences =>
        XMLProject.ItemGroup.SelectMany(item => item.PackageReferences).ToList();

    public List<FrameworkReference> FrameworkReferences =>
        XMLProject.ItemGroup.SelectMany(item => item.FrameworkReferences).ToList();

    public List<AssemblyAttributeItem> AssemblyAttributes =>
        XMLProject.ItemGroup.SelectMany(item => item.AssemblyAttributes).ToList();

    public IEnumerable<string> InternalsVisibleToNames =>
        XMLProject.ItemGroup
            .SelectMany(item => item.InternalsVisibleTo)
            .Select(item => item.Include ?? "")
            .Where(name => !string.IsNullOrEmpty(name))
            .Concat(XMLProject.ItemGroup
                .SelectMany(item => item.AssemblyAttributes)
                .Where(attr => string.Equals(attr.Include,
                    "System.Runtime.CompilerServices.InternalsVisibleTo",
                    StringComparison.OrdinalIgnoreCase))
                .Select(attr => attr.Parameter1 ?? "")
                .Where(name => !string.IsNullOrEmpty(name)));

    public bool IsWebSdk => (XMLProject.Sdk ?? "").Split(';')
        .Select(sdk => sdk.Trim())
        .Contains("Microsoft.NET.Sdk.Web", StringComparer.OrdinalIgnoreCase);

    // Razor SDK projects (Microsoft.NET.Sdk.Razor / Microsoft.NET.Sdk.Web). Drives Razor view/component
    // compilation via the Razor source generator (see RazorSupport).
    public bool UsesRazorSdk => RazorSupport.UsesRazorSdk(XMLProject.Sdk);

    // <AddRazorSupportForMvc> (set by MVC view projects, e.g. OrchardCore modules). Determines whether
    // .cshtml files compile into MVC views.
    public bool AddRazorSupportForMvc =>
        XMLProject.PropertyGroup?.FirstOrDefault()?.AddRazorSupportForMvc
            ?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

    // The Razor language version the generator targets. Defaults to the TFM's version (e.g. net10.0 ->
    // "10.0"); a project may override it with <RazorLangVersion>.
    public string RazorLangVersion
    {
        get
        {
            var declared = XMLProject.PropertyGroup?.FirstOrDefault()?.RazorLangVersion;
            if (!string.IsNullOrWhiteSpace(declared)) return declared.Trim();

            var tfm = TargetFramework ?? "";
            var trimmed = tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) ? tfm[3..] : tfm;
            var dash = trimmed.IndexOf('-');
            if (dash >= 0) trimmed = trimmed[..dash];
            return string.IsNullOrEmpty(trimmed) ? "10.0" : trimmed;
        }
    }

    // The Razor inputs (.cshtml -> MVC views, .razor -> components) the source generator compiles.
    // Empty for projects that do not use the Razor SDK or have no Razor files.
    private IReadOnlyList<RazorInput>? _razorInputs;
    public IReadOnlyList<RazorInput> RazorInputs => _razorInputs ??= UsesRazorSdk
        ? RazorSupport.Collect(
            ProjectRootPath,
            IsWebSdk,
            defaultItemsEnabled: !IsPropertyFalse(XMLProject.PropertyGroup?.FirstOrDefault()?.EnableDefaultItems),
            generateEnabled: !IsPropertyFalse(XMLProject.PropertyGroup?.FirstOrDefault()?.EnableDefaultRazorGenerateItems),
            componentEnabled: !IsPropertyFalse(XMLProject.PropertyGroup?.FirstOrDefault()?.EnableDefaultRazorComponentItems))
        : Array.Empty<RazorInput>();

    private static bool IsPropertyFalse(string? value) =>
        value?.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) ?? false;

    // Aspire AppHost projects (Sdk="Aspire.AppHost.Sdk"). The SDK's build targets synthesize
    // "Projects.<ClassName>" metadata classes for every project resource reference (and the host
    // itself); those generated files are MSBuild artifacts, not source-generator output, so we
    // regenerate them here (see AspireHostMetadataSource in GetProjectDetails).
    public bool IsAspireHost =>
        (XMLProject.Sdk ?? "").Split(';')
            .Any(sdk => sdk.Trim().StartsWith("Aspire.AppHost.Sdk", StringComparison.OrdinalIgnoreCase))
        || ((XMLProject.PropertyGroup?.FirstOrDefault()?.IsAspireHost
            ?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)) ?? false);

    // True for projects that depend on the ASP.NET Core shared framework (Web SDK projects, or any
    // project declaring <FrameworkReference Include="Microsoft.AspNetCore.App" />). Drives both the
    // compiler references and the runtimeconfig "frameworks" entry.
    public bool UsesAspNetCoreApp =>
        IsWebSdk
        || FrameworkReferences.Any(reference =>
            reference.Include?.Equals("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase) ?? false);

    // Test projects (SDK <IsTestProject>true</IsTestProject> or a test-runner package) default to
    // OutputType=Exe under the test SDK but get their entry point injected by the test framework
    // (xunit.v3's XunitAutoGeneratedEntryPoint.cs). Detected so BuildCore can generate the same
    // shim; real console projects keep the genuine CS5001 behavior.
    public bool IsTestProject =>
        XMLProject.PropertyGroup?.FirstOrDefault()?.IsTestProject
            ?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ?? false
        || PackageReferences.Any(reference =>
        {
            var include = reference.Include ?? "";
            return include.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                   || include.StartsWith("nunit", StringComparison.OrdinalIgnoreCase)
                   || include.StartsWith("mstest", StringComparison.OrdinalIgnoreCase)
                   || include.Equals("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
                   || include.StartsWith("Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase);
        });

    public OutputKind ResolvedProjectOutputKind()
    {
        var ExeContains = XMLProject.PropertyGroup?.FirstOrDefault()?.OutputType?.Contains("Exe") ?? false;

        return (ExeContains || IsWebSdk) switch
        {
            true => OutputKind.ConsoleApplication,
            false => OutputKind.DynamicallyLinkedLibrary
        };
    }

    private XML.Project DeserializeObject(string path)
    {
        return XML.Project.Parse(path);
    }

    private static string GetNuGetCacheDirectory()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        return path;
    }

    public static string GetSharpBinDirectory(string projectRootPath, string? targetFramework)
    {
        // SDK-mirroring output layout ("myc\Debug\<TFM>"). Root stays "myc" - the single tiny
        // difference from real `dotnet build` output ("bin") so this compiler's output never
        // clobbers SDK build output during development; layout (Debug\<TFM>) and everything inside
        // the DLLs match. `NATIVECOMPILER_OUTPUT_ROOT` overrides the root (e.g. "bin") for exact
        // path-parity runs.
        var root = Environment.GetEnvironmentVariable("NATIVECOMPILER_OUTPUT_ROOT") ?? "myc";
        var path = Path.Combine(projectRootPath, root, "Debug", targetFramework ?? DefaultTargetFramework.Value);
        CreateFolderIfNotExists(path);
        return path;
    }

    public static void CreateFolderIfNotExists(string folderPath)
    {
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
    }

    private string ResolvedProjectExtensionType()
    {
        var ExeContains = XMLProject.PropertyGroup?.FirstOrDefault()?.OutputType?.Contains("Exe") ?? false;

        // SDK apphost naming: <AssemblyName>.exe on Windows, bare <AssemblyName> on Unix.
        return (ExeContains || IsWebSdk) && OperatingSystem.IsWindows() ? "exe" : "";
    }
}


////
//// Summary:
////     Specifies output assembly kinds generated by compiler.
//public enum OutputKind
//{
//    //
//    // Summary:
//    //     An .exe with an entry point and a console.
//    ConsoleApplication,
//    //
//    // Summary:
//    //     An .exe with an entry point but no console.
//    WindowsApplication,
//    //
//    // Summary:
//    //     A .dll file.
//    DynamicallyLinkedLibrary,
//    //
//    // Summary:
//    //     A .netmodule file.
//    NetModule,
//    //
//    // Summary:
//    //     A .winmdobj file.
//    WindowsRuntimeMetadata,
//    //
//    // Summary:
//    //     An .exe that can run in an app container.
//    //
//    // Remarks:
//    //     Equivalent to a WindowsApplication, but with an extra bit set in the Portable
//    //     Executable file so that the application can only be run in an app container.
//    //     Also known as a "Windows Store app".
//    WindowsRuntimeApplication
//}
//}