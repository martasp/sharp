using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
//See https://aka.ms/new-console-template for more information

namespace NativeCompiler;

public static class NativeCompilerUtils
{
    public static (SyntaxTree syntaxTree, EmbeddedText embeddedText) Parse(ProjectFile projectFile,
        CSharpParseOptions options)
    {
        var stringText = SourceText.From(File.ReadAllText(projectFile.FilePath), Encoding.UTF8);
        var embeddedText = EmbeddedText.FromSource(projectFile.FilePath, stringText);
        var SyntaxTree = SyntaxFactory.ParseSyntaxTree(stringText, options, projectFile.FilePath);
        return (SyntaxTree, embeddedText);
    }

    public static (SyntaxTree syntaxTree, EmbeddedText embeddedText) ParseFile(ProjectFile file)
    {
        var parsedSyntaxTree = Parse(file, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        return parsedSyntaxTree;
    }

    public static (SyntaxTree syntaxTree, EmbeddedText embeddedText)[] ParseProject(Project project)
    {
        return project.CSFiles.Select(file => ParseFile(file)).ToArray();
    }

    public static MetadataReference[] GetdefaultReferences(string dotNetAssemblyPath)
    {
        var exclude = new List<string>
        {
            @"ucrtbase.dll",
            @"System.IO.Compression.Native.dll",
            @"msquic.dll",
            @"mscorrc.dll",
            @"mscordbi.dll",
            @"mscordaccore_amd64_amd64_6.0.121.56705.dll",
            @"mscordaccore_amd64_amd64_6.0.1823.26907.dll",
            @"mscordaccore.dll",
            @"Microsoft.DiaSymReader.Native.amd64.dll",
            @"hostpolicy.dll",
            @"dbgshim.dll",
            @"coreclr.dll",
            @"clrjit.dll",
            @"clretwrc.dll",
            @"api-ms-win-crt-utility-l1-1-0.dll",
            @"api-ms-win-crt-time-l1-1-0.dll",
            @"api-ms-win-crt-string-l1-1-0.dll",
            @"api-ms-win-crt-stdio-l1-1-0.dll",
            @"api-ms-win-crt-runtime-l1-1-0.dll",
            @"api-ms-win-crt-process-l1-1-0.dll",
            @"api-ms-win-crt-private -l1 - 1 - 0.dll",
            @"api-ms-win-crt-multibyte-l1-1-0.dll",
            @"api-ms-win-crt-math-l1-1-0.dll",
            @"api-ms-win-crt-locale-l1-1-0.dll",
            @"api-ms-win-crt-heap-l1-1-0.dll",
            @"api-ms-win-crt-filesystem-l1-1-0.dll",
            @"api-ms-win-crt-environment-l1-1-0.dll",
            @"api-ms-win-crt-convert-l1-1-0.dll",
            @"api-ms-win-crt-conio-l1-1-0.dll",
            @"api-ms-win-core-util-l1-1-0.dll",
            @"api-ms-win-core-timezone-l1-1-0.dll",
            @"api-ms-win-core-sysinfo-l1-1-0.dll",
            @"api-ms-win-core-synch-l1-2-0.dll",
            @"api-ms-win-core-synch-l1-1-0.dll",
            @"api-ms-win-core-string-l1-1-0.dll",
            @"api-ms-win-core-rtlsupport-l1-1-0.dll",
            @"api-ms-win-core-profile-l1-1-0.dll",
            @"api-ms-win-core-processthreads-l1-1-1.dll",
            @"api-ms-win-core-processthreads-l1-1-0.dll",
            @"api-ms-win-core-processenvironment-l1-1-0.dll",
            @"api-ms-win-core-namedpipe-l1-1-0.dll",
            @"api-ms-win-core-memory-l1-1-0.dll",
            @"api-ms-win-core-localization-l1-2-0.dll",
            @"api-ms-win-core-libraryloader-l1-1-0.dll",
            @"api-ms-win-core-interlocked-l1-1-0.dll",
            @"api-ms-win-core-heap-l1-1-0.dll",
            @"api-ms-win-core-handle-l1-1-0.dll",
            @"api-ms-win-core-file-l2-1-0.dll",
            @"api-ms-win-core-file-l1-2-0.dll",
            @"api-ms-win-core-file-l1-1-0.dll",
            @"api-ms-win-core-errorhandling-l1-1-0.dll",
            @"api-ms-win-core-debug-l1-1-0.dll",
            @"api-ms-win-core-datetime-l1-1-0.dll",
            @"api-ms-win-core-console-l1-2-0.dll",
            @"api-ms-win-core-console-l1-1-0.dll",
            @"api-ms-win-crt-private-l1-1-0.dll",
            @"hostfxr.dll",
            @"api-ms-win-core-fibers-l1-1-0.dll",
            @"mscordaccore_amd64_amd64_8.0.724.31311.dll",
            @"clrgc.dll",
           // @"System.Private.CoreLib.dll"
        };

        var directory = new DirectoryInfo(dotNetAssemblyPath);
        var files = directory
            .GetFiles("*.dll", SearchOption.TopDirectoryOnly)
            //.Where(f => exclude.Contains(f.Name) == false)
            .ToArray();

        // Explicitly add runtime assemblies for .NET 8
        //var references = new List<MetadataReference>
        //{
        //    MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, "System.Private.CoreLib.dll")),
        //    MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, "System.Runtime.dll")),
        //    MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, "System.Console.dll"))
        //};

        var defaultReferences = files.Select(f =>
            (MetadataReference)MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, f.Name))).ToArray();

        //var executingEsemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.IsDynamic == false).ToArray();
        //var defaultReferences =  executingEsemblies.Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)).ToArray();

        // fix System.Exception: './Program.cs(2,1): error CS0103: The name 'Console' does not exist in the current context'
        //var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        //defaultReferences = defaultReferences.Append(mscorlib).ToArray();


        return defaultReferences.ToArray();
    }

    public static MetadataReference[] GetdefaultLocalReferences(Project project)
    {
        var projectReferences = project.ProjectReferences;

        var files = projectReferences.Select(item =>
            ProjectReferenceFinder.FindReferencedDLL(item.Include ?? "", project.ProjectRootPath));

        var defaultReferences = files.Select(f => (MetadataReference)MetadataReference.CreateFromFile(f)).ToArray();
        return defaultReferences;
    }

    public static MetadataReference[] GetdefaultNugetReferences(Project project)
    {
        var packages = project.PackageReferences;

        var files = packages
            .Select(item =>
                NuGetPackage.FindPackageDLL(NuGetPackage.FindPackagePath(item.Include ?? "", item.Version ?? "")))
            .Where(item => item != string.Empty);

        var defaultReferences = files.Select(f => (MetadataReference)MetadataReference.CreateFromFile(f)).ToArray();
        return defaultReferences;
    }

    public static CSharpCompilationOptions GetCompilationOptions(Project project)
    {
        IEnumerable<string> DefaultNamespaces = new[]
        {
            "System",
            //"System.IO",
            //"System.Net",
            //"System.Linq",
            //"System.Text",
            "System.Console",
            //"System.Text.RegularExpressions",
            //"System.Collections.Generic"
        };

        var OutputKind = project.ResolvedProjectOutputKind();

        var DefaultCompilationOptions =
            new CSharpCompilationOptions(OutputKind)
                .WithOptimizationLevel(OptimizationLevel.Debug)
                .WithUsings(DefaultNamespaces);

        return DefaultCompilationOptions;
    }

    public static void ThrowErrors(EmitResult result)
    {
        var errors = new List<string>();

        var failures = result.Diagnostics.Where(diagnostic =>
            diagnostic.IsWarningAsError ||
            diagnostic.Severity == DiagnosticSeverity.Error);

        foreach (var diagnostic in failures)
            errors.Add($"{diagnostic.Location} {diagnostic.Id}: {diagnostic.GetMessage()}");

        throw new Exception(string.Join("\n", errors));
    }

    public static string[] ReadAllProjectFilesPath(string projectPath)
    {
        var binFolder = Directory.GetDirectories(projectPath, "bin");
        var objFolder = Directory.GetDirectories(projectPath, "obj");

        var rootFiles = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.TopDirectoryOnly);
        var rootFolders = Directory.EnumerateDirectories(projectPath, "*.*", SearchOption.TopDirectoryOnly);

        var foldersExceptObjAndBin = rootFolders
            .Except(objFolder)
            .Except(binFolder);

        var traversedFiles = foldersExceptObjAndBin.Select(folder =>
            Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories));
        var files = traversedFiles.SelectMany(folder => folder).Concat(rootFiles);

        return files.ToArray(); //todo make everything enumerable maybe faster
    }

    public static List<ProjectFile> ReadAllFilesInMemory(string[] projectFiles)
    {
        return projectFiles.Select(item => new ProjectFile(item, File.ReadAllText(item))).ToList();
    }

    public static Project GetProjectDetails(List<ProjectFile> projectFiles, string projectRootPath)
    {
        var CsProjFile = projectFiles.FirstOrDefault(item => Path.GetExtension(item.FilePath) == ".csproj")
                         ?? throw new ArgumentException("Project does not have csproject file");

        var CSFiles = projectFiles.Where(item => Path.GetExtension(item.FilePath) == ".cs").ToList()
                      ?? throw new ArgumentException("Project does not have c sharp files");

        //var Appsettings = projectFiles.FirstOrDefault(item => Path.GetExtension(item.FilePath) == ".json") // Todo latter will need ConsoleProject, WebApi proeject
        //    ?? throw new ArgumentException("Project does not have c sharp files");

        return new Project(CsProjFile, CSFiles, projectRootPath);
    }
}