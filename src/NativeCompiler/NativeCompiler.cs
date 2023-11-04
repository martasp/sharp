using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static NativeCompiler.NativeCompilerUtils;

namespace NativeCompiler;

public static class NativeCompiler
{
    public static bool WriteFiles(string folderPath, List<string> filePaths)
    {
        try
        {
            // Ensure the folder exists or create it if it doesn't.
            Directory.CreateDirectory(folderPath);

            foreach (var filePath in filePaths)
            {
                // Extract the file name from the file path.
                var fileName = Path.GetFileName(filePath);

                // Combine the folder path and file name to create the full file path.
                var destinationFilePath = Path.Combine(folderPath, fileName);

                // Copy the file to the specified folder.
                File.Copy(filePath, destinationFilePath);
            }

            // All files were copied successfully.
            return true;
        }
        catch (Exception ex)
        {
            // Handle any exceptions that may occur during the file copy operation.
            Console.WriteLine($"Error copying files: {ex.Message}");
            return false;
        }
    }

    public static (bool success, string errors) Build(string projectPath)
    {
        _ = Directory.Exists(projectPath)
            ? ""
            : throw new ArgumentException($"Project path not exists, given path {projectPath}");
        var projectFiles = ReadAllProjectFilesPath(projectPath);
        var files = ReadAllFilesInMemory(projectFiles);
        var project = GetProjectDetails(files, projectPath);

        var dotNetAssemblyPath = AsemblyExtensions.ResolveSdkLocation;
        var compilationOptions = GetCompilationOptions(project);
        var defaultReferences = GetdefaultReferences(dotNetAssemblyPath);

        var parsedData = ParseProject(project);
        var syntaxTrees = parsedData.Select(item => item.syntaxTree);

        var defaultLocalReferences = GetdefaultLocalReferences(project);
        var defaultNugetReferences = GetdefaultNugetReferences(project);

        var mergedDefaultReferences = defaultNugetReferences.Concat(defaultLocalReferences).Concat(defaultReferences);

        var compilation = CSharpCompilation.Create(project.ExtensionName, syntaxTrees, mergedDefaultReferences,
            compilationOptions);
        //.WithOptions(compilationOptions)
        //.AddReferences(mergedDefaultReferences)
        //.AddSyntaxTrees(syntaxTrees);

        var produceDll = ProduceDll(compilation, project);
        var paths = mergedDefaultReferences.Select(item => item?.Display ?? "").ToList();
        WriteFiles(project.SharpBinFolderPath, paths);

        var runErrors = project.ResolvedProjectOutputKind() == OutputKind.ConsoleApplication
            ? RunAssembly(compilation, project, parsedData.Select(item => item.embeddedText).ToList())
            : (success: true, errors: "");

        return (runErrors.success && produceDll.success, runErrors.errors + produceDll.errors);
    }

    public static (bool success, string errors) ProduceDll(CSharpCompilation compilation, Project project)
    {
        try
        {
            //var emitOptions = new EmitOptions(
            //    debugInformationFormat: DebugInformationFormat.PortablePdb,
            //    pdbFilePath: project.ProjectName);

            //EmitResult result = compilation.Emit(
            //    peStream: assemblyStream,
            //    pdbStream: symbolsStream,
            //    embeddedTexts: embeddedTexts,
            //    options: emitOptions);

            //dotnet publish -r win-x64 -c release
            var result = compilation.Emit(project.DllPathName, project.PDBPathName);
            var buildErrors = result.PrintErrors();
            return (result.Success, buildErrors);
        }
        catch (Exception ex)
        {
            return (false, ex.ToString());
        }
    }

    public static (bool success, string errors) RunAssembly(CSharpCompilation compilation, Project project,
        List<EmbeddedText> embeddedTexts)
    {
        // Emit the byte result of the compilation
        var assembly = compilation.CreateAssembly(project.ProjectName, embeddedTexts); // Do this without reflection


        var programType = assembly.GetType("Program") ?? throw new Exception("assembly Prgoram do not have main");
        var sourceInstance = Activator.CreateInstance(programType);

        // Get the static Main() method info from the type
        var method = programType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
            .FirstOrDefault();


        Debug.WriteLine(Directory.GetCurrentDirectory());
        Directory.SetCurrentDirectory(project.SharpBinFolderPath);
        Debug.WriteLine(Directory.GetCurrentDirectory());
        try
        {
            var builder = new StringBuilder();
            TextWriter writer = new StringWriter(builder);
            Console.SetOut(writer);
            method?.Invoke(sourceInstance, new object[] { new[] { project.ProjectRootPath } });
            var result = builder.ToString();
            return (true, result); // Todo needs logging better
        }
        catch (Exception ex)
        {
            return (false,
                ex?.InnerException?.ToString() ?? "No errors"); // Todo : Implement better global error handling 
        }
    }

    public static (bool success, string errors) Run(string projectPath)
    {
        return Build(projectPath);
    }
}