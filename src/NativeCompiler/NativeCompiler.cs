using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using XML;
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

                // overwrite files
                File.Copy(filePath, destinationFilePath, true);
            }

            // All files were copied successfully.
            return true;
        }
        catch (Exception ex)
        {
            Debug.Assert(false, "Failes to write files "+ ex.Message);
            return false;
        }
    }

    public static (bool success, string errors, CSharpCompilation Compilation, Project project, IEnumerable<EmbeddedText> EmbeddedTexts) Build(string projectPath)
    {
        _ = Directory.Exists(projectPath)
            ? ""
            : throw new ArgumentException($"Project path not exists, given path {projectPath}");
        var projectFiles = ReadAllProjectFilesPath(projectPath);
        var files = ReadAllFilesInMemory(projectFiles); //slow part 300ms;
        var project = GetProjectDetails(files, projectPath);

        var dotNetAssemblyRefPath = AsemblyExtensions.ResolveSdkRefLocation;
        var dotNetAssemblyRuntimePath = AsemblyExtensions.ResolveSdkRuntimeAssemblyPath;

        var compilationOptions = GetCompilationOptions(project);

        var defaultReferencesForCompiling = GetdefaultReferencesForCompiling(dotNetAssemblyRefPath);
        var defaultReferencesForRunning   = GetdefaultReferencesForRunning(dotNetAssemblyRuntimePath);

        var parsedData = ParseProject(project);
        var syntaxTrees = parsedData.Select(item => item.syntaxTree);
        var embeddedTexts = parsedData.Select(item => item.embeddedText);

        var defaultLocalReferences = GetdefaultLocalReferences(project);
        var defaultNugetReferences = GetdefaultNugetReferences(project);

        var mergedDefaultReferences = defaultNugetReferences.Concat(defaultLocalReferences).Concat(defaultReferencesForCompiling);
        var mergedDefaultReferencesForRunning = defaultNugetReferences.Concat(defaultLocalReferences).Concat(defaultReferencesForRunning);

        // Write count of mergedDefaultReferences use nice format:
        Console.WriteLine($"Merged default references count: {mergedDefaultReferences.Count()}");
        
        var compilation = CSharpCompilation.Create(project.ProjectName, syntaxTrees, mergedDefaultReferences,compilationOptions);
        
        var produceDll = ProduceDll(compilation, project); // slowest part 1sec, (TODO dont need to save dll if running on compile time)
        
        var paths = mergedDefaultReferencesForRunning.Select(item => item?.Display ?? "").ToList();
        WriteFiles(project.SharpBinFolderPath, paths);

        Console.WriteLine($"Compiled {project.ProjectName} successfully");
        
        return (produceDll.success, produceDll.errors, compilation, project, embeddedTexts);
    }

    public static (bool success, string errors) Run(string projectPath)
    {
        var buildDetails = Build(projectPath);

        var project = buildDetails.project;
        var compilation = buildDetails.Compilation;
        var embeddedTexts = buildDetails.EmbeddedTexts.ToList();

        var runErrors = project.ResolvedProjectOutputKind() == OutputKind.ConsoleApplication
            ? RunAssembly(compilation, project, embeddedTexts)
            : (success: true, errors: "");

        Console.WriteLine($"Run {project.ProjectName} successfully");

        return (runErrors.success && buildDetails.success, runErrors.errors + buildDetails.errors);
    }

    public static (bool success, string errors) ProduceDll(CSharpCompilation compilation, Project project)
    {
        try
        {
            //EmitOptions must use PortablePdb: the string Emit overload forces the native Windows PDB
            //format which relies on COM (DiaSymReader) and is not supported under NativeAOT.
            var emitOptions = new EmitOptions(
                debugInformationFormat: DebugInformationFormat.PortablePdb,
                pdbFilePath: project.PDBPathName);

            using (var dllStream = File.Create(project.DllPathName))
            using (var pdbStream = File.Create(project.PDBPathName))
            {
                var dllResult = compilation.Emit(dllStream, pdbStream, options: emitOptions);
                if (!dllResult.Success)
                {
                    return (false, dllResult.PrintErrors());
                }
            }

            using (var exeStream = File.Create(project.ExePathName))
            using (var pdbStream = File.Create(project.PDBPathName))
            {
                var result = compilation.Emit(exeStream, pdbStream, options: emitOptions);
                var buildErrors = result.PrintErrors();
                if (!result.Success)
                {
                    return (false, buildErrors);
                }
            }

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.ToString());
        }
    }

    public static (bool success, string errors) BuildNative(CSharpCompilation compilation, Project project,
    List<EmbeddedText> embeddedTexts)
    {
        //Todo just use exes and process spwaning, cant figure out how to build programatically without clis external process. !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

        //string inputAssembly = "MyLibrary.dll";
        //string outputObjectFile = "MyLibrary.obj";

        //// 1. Setup the Type System Context (Target Architecture: x64)
        //// This loads the metadata from your IL assembly.
        //TargetDetails target = new TargetDetails(TargetArchitecture.X64, TargetOS.Windows, TargetAbi.NativeAot);
        //var context = new CompilerTypeSystemContext(target, SharedGenericsMode.CanonicalReferenceTypes, DelegateFeature.All);

        //// 2. Load the Input Assembly
        //context.InputFilePaths.Add(inputAssembly);
        //context.ReferenceFilePaths.Add(typeof(object).Assembly.Location); // Reference CoreLib

        //EcmaModule module = context.GetModuleFromPath(inputAssembly);

        //// 3. Define Compilation Roots
        //// You must tell the compiler "start here". For a DLL, this is usually all exported methods.
        //var compilationRoots = new List<ICompilationRootProvider>();

        //// (Simplified) Add a root for a specific method "DoWork" in "MyClass"
        //var type = module.GetType("MyNamespace", "MyClass");
        //var method = type.GetMethod("DoWork", null);

        //compilationRoots.Add(new SingleMethodRootProvider(method));

        //// 4. Configure the Dependency Analyzer
        //// This is the engine that crawls the IL to find everything else that needs compiling.
        //var nodeFactory = new NodeFactory(
        //    context,
        //    new CompilerOptions() { TargetOS = TargetOS.Windows },
        //    new NameMangler(true),
        //    "MyNativeLib"
        //);

        //// 5. Instantiate RyuJIT Compilation
        //// RyuJitCompilation is the specific class that connects the ILCompiler to the RyuJIT backend.
        //var compilation = new RyuJitCompilation(
        //    nodeFactory,
        //    compilationRoots,
        //    null, // logger
        //    new RyuJitCompilationOptions()
        //);

        //// 6. Run Compilation
        //Console.WriteLine("Compiling methods...");
        //DependencyAnalyzerBase<NodeFactory> analyzer = compilation.GetDependencyAnalyzer();

        //// This triggers the crawl. It generates the dependency graph of all methods/types needed.
        //analyzer.ComputeMarkedNodes();

        //// 7. Emit Native Object File (ObjectWriter)
        //// The compiler produces an .obj/.o file, which must then be linked by a platform linker (link.exe/ld).
        //Console.WriteLine($"Emitting object file: {outputObjectFile}");

        //using (var fileStream = File.Create(outputObjectFile))
        //{
        //    // Internal API to write the object file containing machine code
        //    compilation.WriteObjectFile(fileStream);
        //}

        //Console.WriteLine("Done. You must now link this object file using link.exe (MSVC) or clang.");

        return (true, ""); // Todo needs logging better
    }

    public static (bool success, string errors) RunAssembly(CSharpCompilation compilation, Project project,
        List<EmbeddedText> embeddedTexts)
    {
        // Emit the byte result of the compilation
        var assembly = compilation.CreateAssembly(project.ProjectName, embeddedTexts); // Do this without reflection

        //-----------------
        //var entryPoint = assembly.EntryPoint;

        //if (entryPoint != null)
        //{
        //    entryPoint.Invoke(null, new object[] { project.ProjectRootPath });
        //}

        // you  can also use entry point insted of assuming Program class 

        //-----------------------

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
            Console.WriteLine($"Running {project.ProjectName} with args {project.ProjectRootPath}");
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

    public static (bool success, string errors) RunAssemblyAot(CSharpCompilation compilation, Project project,
    List<EmbeddedText> embeddedTexts)
    {
        throw new NotImplementedException();    
    }
}

record BuildResult(bool Success, string Errors, CSharpCompilation Compilation, Project Project, IEnumerable<EmbeddedText> EmbeddedTexts);