using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Diagnostics;
using System.Text;
//See https://aka.ms/new-console-template for more information

namespace NativeCompiler;

public static class NativeCompilerUtils
{
    public static (SyntaxTree syntaxTree, EmbeddedText embeddedText) Parse(ProjectFile projectFile,
        CSharpParseOptions options)
    {
        var fileContent = projectFile.FileContent;
        var stringText = SourceText.From(fileContent, Encoding.UTF8);
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

    public static MetadataReference[] GetdefaultReferencesForRunning(string dotNetAssemblyPath)
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
            //@"System.Private.CoreLib.dll"
        };

        var directory = new DirectoryInfo(dotNetAssemblyPath);
        var files = directory
            .GetFiles("*.dll", SearchOption.TopDirectoryOnly)
            .Where(f => exclude.Contains(f.Name) == false)
            .ToArray();

        var defaultReferences = files.Select(f =>
            (MetadataReference)MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, f.Name))).ToArray();

        return defaultReferences.ToArray();
    }

    public static MetadataReference[] GetdefaultReferencesForCompiling(string dotNetAssemblyRefPath)
    {
        var directory = new DirectoryInfo(dotNetAssemblyRefPath);
        var files = directory
            .GetFiles("*.dll", SearchOption.TopDirectoryOnly)
            .ToArray();

        var defaultReferences = files.Select(f =>
            (MetadataReference)MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyRefPath, f.Name))).ToArray();
        
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
        IEnumerable<string> Namespaces = new[]
        {
            "System",
        };

        IEnumerable<string> DefaultNamespacesWithGloBalUsingsForTopLevelExe = new[]
        {
            "System",
            //"global using global::System;",
            //"global using global::System.Collections.Generic;",
            //"global using global::System.IO;",
            //"global using global::System.Linq;",
            //"global using global::System.Net.Http;",
            //"global using global::System.Threading;",
            //"global using global::System.Threading.Tasks;",
        };

        var OutputKind = project.ResolvedProjectOutputKind();

        var DefaultNamespaces = OutputKind == OutputKind.ConsoleApplication
            ? DefaultNamespacesWithGloBalUsingsForTopLevelExe
            : Namespaces;

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
        var files = projectFiles.Select(item => new ProjectFile(item, File.ReadAllText(item))).ToList();
        return files;
    }

    public static Project GetProjectDetails(List<ProjectFile> projectFiles, string projectRootPath)
    {
        var CsProjFile = projectFiles.FirstOrDefault(item => Path.GetExtension(item.FilePath) == ".csproj")
                         ?? throw new ArgumentException("Project does not have csproject file");

        var CSFiles = projectFiles.Where(item => Path.GetExtension(item.FilePath) == ".cs").ToList()
                      ?? throw new ArgumentException("Project does not have c sharp files");

        var project = new Project(CsProjFile, CSFiles, projectRootPath);

        if (project.ImplicitUsings)
        {
            var globalUsingFile =
            """
            global using global::System;
            global using global::System.Collections.Generic;
            global using global::System.IO;
            global using global::System.Linq;
            global using global::System.Net.Http;
            global using global::System.Threading;
            global using global::System.Threading.Tasks;
            """;
            project.CSFiles.Add(new ProjectFile("GlobalUsings.g.cs", globalUsingFile));
        }

        //Todo read from csproj and add this file currently will be ignored
        //TODO What is this number 936242573a0bbf456eb83abb29521666de5e44d5 checked git git rev-parse HEAD its not the commit hash.
        var versionInfoSource =
        """
        using System;
        using System.Reflection;

        [assembly: global::System.Runtime.Versioning.TargetFrameworkAttribute(".NETCoreApp,Version=v10.0", FrameworkDisplayName = ".NET 10.0")]
        
        [assembly: System.Reflection.AssemblyCompanyAttribute("MSCompiler")]
        [assembly: System.Reflection.AssemblyConfigurationAttribute("Debug")]
        [assembly: System.Reflection.AssemblyFileVersionAttribute("1.0.0.0")]
        [assembly: System.Reflection.AssemblyInformationalVersionAttribute("1.0.0+936242573a0bbf456eb83abb29521666de5e44d5")] 
        [assembly: System.Reflection.AssemblyProductAttribute("MSCompiler")]
        [assembly: System.Reflection.AssemblyTitleAttribute("MSCompiler")]
        [assembly: System.Reflection.AssemblyVersionAttribute("1.0.0.0")]
        """;

        project.CSFiles.Add(new ProjectFile("VersionInfo.g.cs", versionInfoSource));

        //var Appsettings = projectFiles.FirstOrDefault(item => Path.GetExtension(item.FilePath) == ".json") // Todo latter will need ConsoleProject, WebApi proeject
        //    ?? throw new ArgumentException("Project does not have c sharp files");

        return project;
    }

    public static void WriteRuntimeConfig(Project project)
    {
        var runtimeVersion = new DirectoryInfo(AsemblyExtensions.ResolveSdkRuntimeAssemblyPath).Name;
        var versionParts = runtimeVersion.Split('.');
        var tfm = $"net{versionParts[0]}.{versionParts[1]}";

        var runtimeConfig =
        $$"""
        {
          "runtimeOptions": {
            "tfm": "{{tfm}}",
            "framework": {
              "name": "Microsoft.NETCore.App",
              "version": "{{runtimeVersion}}"
            },
            "configProperties": {
              "System.Reflection.Metadata.MetadataUpdater.IsSupported": false
            }
          }
        }
        """;

        File.WriteAllText(Path.Combine(project.SharpBinFolderPath, $"{project.ProjectName}.runtimeconfig.json"), runtimeConfig);
    }

    public static void WriteAppHost(Project project)
    {
        var appHostTemplate = FindAppHostTemplate();
        if (appHostTemplate is null)
        {
            Console.WriteLine(
                $"Skipping apphost: no AppHostTemplate found under '{Path.Combine(AsemblyExtensions.ResolveSdkRoot, "sdk")}'.");
            return;
        }

        //The SDK template is read-only and File.Copy preserves that attribute, which would make the
        //destination unwritable for the patch below (and block the next overwrite). Clear it first.
        MakeWritableIfExists(project.ExePathName);
        File.Copy(appHostTemplate, project.ExePathName, overwrite: true);
        File.SetAttributes(project.ExePathName, FileAttributes.Normal);

        var appDllName = Path.GetFileName(project.DllPathName);
        BindAppHost(project.ExePathName, appDllName);
        Console.WriteLine($"Apphost '{project.ExePathName}' bound to '{appDllName}'.");
    }

    private static void MakeWritableIfExists(string path)
    {
        if (!File.Exists(path)) return;
        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
    }

    private static string? FindAppHostTemplate()
    {
        var sdkDir = Path.Combine(AsemblyExtensions.ResolveSdkRoot, "sdk");
        if (!Directory.Exists(sdkDir)) return null;

        return Directory.EnumerateDirectories(sdkDir)
            .OrderByDescending(ParseSdkVersion)
            .Select(sdkVersionDir => Path.Combine(sdkVersionDir, "AppHostTemplate", "apphost.exe"))
            .FirstOrDefault(File.Exists);
    }

    private static Version? ParseSdkVersion(string sdkDirectoryPath)
    {
        var name = Path.GetFileName(sdkDirectoryPath) ?? "";
        var digits = new string(name.SkipWhile(character => !char.IsDigit(character)).ToArray());
        return Version.TryParse(digits, out var version) ? version : null;
    }

    private static void BindAppHost(string appHostPath, string appDllName)
    {
        var placeholder = Encoding.ASCII.GetBytes(AppBinaryPathPlaceholder);
        var appName = Encoding.UTF8.GetBytes(appDllName);

        if (appName.Length > placeholder.Length)
            throw new InvalidOperationException($"App DLL name '{appDllName}' is too long for the apphost.");

        var bytes = File.ReadAllBytes(appHostPath);
        var position = IndexOf(bytes, placeholder);
        if (position < 0)
            throw new InvalidOperationException("AppHost template is missing the placeholder hash; cannot bind it.");

        for (var i = 0; i < placeholder.Length; i++)
            bytes[position + i] = i < appName.Length ? appName[i] : (byte)0;

        File.WriteAllBytes(appHostPath, bytes);

        //Verify the placeholder is gone so a silent failure cannot leave an unbound apphost behind.
        var verify = File.ReadAllBytes(appHostPath);
        if (IndexOf(verify, placeholder) >= 0)
            throw new InvalidOperationException("Failed to bind apphost: the placeholder hash is still present.");
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    found = false;
                    break;
                }
            }

            if (found) return i;
        }

        return -1;
    }

    //The apphost template embeds a placeholder hash (SHA-256 of "foobar") where the managed DLL
    //name is stored. 'dotnet build' replaces it via the SDK's HostWriter, which hardcodes the same
    //constant (AppBinaryPathPlaceholder) in https://github.com/dotnet/runtime/blob/main/src/installer/
    //managed/Microsoft.NET.HostModel/AppHost/HostWriter.cs. The value never changes between runtime
    //versions or machines, so embedding it here mirrors the SDK exactly.
    private const string AppBinaryPathPlaceholder = "c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2";
}
