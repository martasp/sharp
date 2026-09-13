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
    public string SharpBinFolderPath => GetSharpBinDirectory(ProjectRootPath);
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

    public bool AllowUnsafeBlocks =>
        (XMLProject.PropertyGroup?.FirstOrDefault()?.AllowUnsafeBlocks?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ?? false)
        || BuildProps.AllowUnsafeBlocks;

    public string? Nullable => XMLProject.PropertyGroup?.FirstOrDefault()?.Nullable ?? BuildProps.Nullable;

    public string? LangVersion => XMLProject.PropertyGroup?.FirstOrDefault()?.LangVersion ?? BuildProps.LangVersion;

    public string? DefineConstants => XMLProject.PropertyGroup?.FirstOrDefault()?.DefineConstants
                                      ?? BuildProps.DefineConstants;

    // Interceptor namespaces (SDK <InterceptorsNamespaces>/<InterceptorsPreviewNamespaces>) that
    // opt generated [InterceptsLocation] code in for csc parity (OrchardCore's shape generator).
    public string? InterceptorsNamespaces =>
        XMLProject.PropertyGroup?.FirstOrDefault()?.InterceptorsNamespaces
        ?? XMLProject.PropertyGroup?.FirstOrDefault()?.InterceptorsPreviewNamespaces
        ?? BuildProps.InterceptorsNamespaces;
    public string ProjectName => Path.GetFileNameWithoutExtension(CsProjFile.FilePath);
    public string ExtensionName => ProjectName + "." + ResolvedProjectExtensionType();
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

    private static string GetSharpBinDirectory(string projectRootPath)
    {
        var path = Path.Combine(projectRootPath, "bin", "sharpbin");
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

        return (ExeContains || IsWebSdk) switch
        {
            true => "exe",
            false => "dll"
        };
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