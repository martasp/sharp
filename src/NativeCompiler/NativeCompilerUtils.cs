using System.Diagnostics;
//See https://aka.ms/new-console-template for more information
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using System;
using System.Reflection;
using Microsoft.CodeAnalysis.Emit;

namespace NativeCompiler
{
    public static class NativeCompilerUtils
    {
        public static (SyntaxTree syntaxTree, EmbeddedText embeddedText) Parse(ProjectFile projectFile, CSharpParseOptions options)
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
            var exclude = new List<string>()
    {
        @"ucrtbase.dll",
        @"System.IO.Compression.Native.dll",
        @"msquic.dll",
        @"mscorrc.dll",
        @"mscordbi.dll",
        @"mscordaccore_amd64_amd64_6.0.121.56705.dll",
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

    };

            DirectoryInfo directory = new DirectoryInfo(dotNetAssemblyPath);
            FileInfo[] files = directory
                .GetFiles("*.dll", SearchOption.TopDirectoryOnly)
                .Where(f => exclude.Contains(f.Name) == false).ToArray();

            var defaultReferences = files.Select(f => (MetadataReference)MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, f.Name))).ToArray();
            return defaultReferences;
        }

        public static MetadataReference[] GetdefaultLocalReferences(Project project)
        {
            var projectReferences = project.ProjectReferences;

            //var files = packages.Select(f => Path.Combine(project.NugetPackagesRootPath, f.Include ?? "", "lib", "netstandard2.0", f.Include + ".dll"));
            string hardcodedPath = @"C:\Users\m.pocius\Desktop\NativeCompiler\src\NativeCompilerCLI\sharpbin\NativeCompiler.dll";
            
            var dllfiles = projectReferences.Select(p => hardcodedPath); // todo unhardcode when we have sharp_bin
            
            var defaultReferences = dllfiles.Select(f => (MetadataReference)MetadataReference.CreateFromFile(f)).ToArray();
            return defaultReferences;
        }

        public static MetadataReference[] GetdefaultNugetReferences(Project project)
        {
            var packages = project.PackageReferences;

            var files = packages.Select(f => Path.Combine(project.NugetPackagesRootPath, f.Include ?? "",f?.Version ?? "", "lib","netstandard2.0", f?.Include+".dll")).ToList();
            string hardcodedPath = @"C:\Users\m.pocius\.nuget\packages\microsoft.codeanalysis.common\4.1.0\lib\netstandard2.0\Microsoft.CodeAnalysis.dll";
            string hardcodedPatha = @"C:\Users\m.pocius\.nuget\packages\cocona.core\2.0.3\lib\netstandard2.0\Cocona.Core.dll";
            files.Add(hardcodedPath);
            files.Add(hardcodedPatha);

            var defaultReferences = files.Select(f => (MetadataReference)MetadataReference.CreateFromFile(f)).ToArray();
            return defaultReferences;
        }
        public static CSharpCompilationOptions GetCompilationOptions(Project project)
        {
            IEnumerable<string> DefaultNamespaces = new[]
            {
                "System",
                "System.IO",
                "System.Net",
                "System.Linq",
                "System.Text",
                "System.Text.RegularExpressions",
                "System.Collections.Generic",
    };

            var OutputKind = project.ResolvedProjectOutputKind();

            CSharpCompilationOptions DefaultCompilationOptions =
                new CSharpCompilationOptions(OutputKind)
                    .WithOverflowChecks(true).WithOptimizationLevel(OptimizationLevel.Debug)
                        .WithUsings(DefaultNamespaces).WithDeterministic(true);

            return DefaultCompilationOptions;
        }
        public static void ThrowErrors(EmitResult result)
        {
            var errors = new List<string>();

            IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                diagnostic.IsWarningAsError ||
                diagnostic.Severity == DiagnosticSeverity.Error);

            foreach (Diagnostic diagnostic in failures)
                errors.Add($"{diagnostic.Location} {diagnostic.Id}: {diagnostic.GetMessage()}");

            throw new Exception(String.Join("\n", errors));
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

            var traversedFiles = foldersExceptObjAndBin.Select(folder => Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories));
            var files = traversedFiles.SelectMany(folder => folder).Concat(rootFiles);

            return files.ToArray(); //todo make everything enumerable maybe faster
        }
        public static List<ProjectFile> ReadAllFilesInMemory(string[] projectFiles)
        {
            return projectFiles.Select(item => new ProjectFile(item, File.ReadAllText(item))).ToList();
        }
        public static Project GetProjectDetails(List<ProjectFile> projectFiles,string projectRootPath)
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
}
