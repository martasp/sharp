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

    public static BuildResult Build(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        if (!BuildsInProgress.TryAdd(fullPath, 0))
            throw new InvalidOperationException(
                $"Circular project reference detected: '{projectPath}' is already being built.");

        try
        {
            return BuildCore(projectPath);
        }
        finally
        {
            BuildsInProgress.TryRemove(fullPath, out _);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> BuildsInProgress =
        new(StringComparer.OrdinalIgnoreCase);

    private static BuildResult BuildCore(string projectPath)
    {
        _ = Directory.Exists(projectPath)
            ? ""
            : throw new ArgumentException($"Project path not exists, given path {projectPath}");

        var timer = Stopwatch.StartNew();
        var outputs = new List<string>();

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
        var frameworkReferenceRefs = GetFrameworkReferenceCompilingRefs(project);

        var mergedDefaultReferences = MergeFrameworkSuppression(
            defaultNugetReferences.Concat(defaultLocalReferences),
            defaultReferencesForCompiling.Concat(frameworkReferenceRefs));
        var mergedDefaultReferencesForRunning = MergeFrameworkSuppression(
            defaultNugetReferences.Concat(defaultLocalReferences), defaultReferencesForRunning);

        outputs.Add($"Merged default references count: {mergedDefaultReferences.Length}");

        var compilation = CSharpCompilation.Create(project.ProjectName, syntaxTrees, mergedDefaultReferences,compilationOptions);

        var produceDll = ProduceDll(compilation, project); // slowest part 1sec, (TODO dont need to save dll if running on compile time)

        var paths = mergedDefaultReferencesForRunning.Select(item => item?.Display ?? "").ToList();
        WriteFiles(project.SharpBinFolderPath, paths);

        if (produceDll.success)
            outputs.Add($"Compiled {project.ProjectName} successfully");

        return new BuildResult(
            produceDll.success,
            produceDll.errors,
            outputs,
            timer.Elapsed,
            compilation,
            project,
            embeddedTexts);
    }

    public static BuildSolutionResult BuildSolution(string solutionPath)
    {
        var timer = Stopwatch.StartNew();
        var solution = SolutionFile.Parse(solutionPath);

        var errors = new List<string>();
        var buildOrder = ResolveBuildOrder(solution.Projects.Select(project => project.ProjectPath), errors);

        var results = new List<SolutionProjectResult>();
        var cache = new Dictionary<string, BuildResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var csproj in buildOrder)
        {
            if (cache.ContainsKey(csproj)) continue;

            BuildResult result;
            try
            {
                var folder = Path.GetDirectoryName(csproj)
                             ?? throw new InvalidOperationException($"Cannot resolve folder for '{csproj}'");
                result = Build(folder);
            }
            catch (Exception ex)
            {
                result = new BuildResult(false, ex.ToString(),
                    new[] { $"{Path.GetFileName(csproj)} failed: {ex.Message}" },
                    TimeSpan.Zero, null, null, null);
            }

            cache[csproj] = result;
            results.Add(new SolutionProjectResult(Path.GetFileNameWithoutExtension(csproj), csproj, result));
        }

        var success = errors.Count == 0 && results.All(projectResult => projectResult.Result.Success);
        return new BuildSolutionResult(success, results, errors, timer.Elapsed);
    }

    private static List<string> ResolveBuildOrder(IEnumerable<string> initialProjects, List<string> errors)
    {
        var projects = new SortedSet<string>(initialProjects, StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(initialProjects);

        while (queue.Count > 0)
        {
            var csproj = queue.Dequeue();
            if (references.ContainsKey(csproj)) continue;
            if (!File.Exists(csproj))
            {
                errors.Add($"Project not found: {csproj}");
                references[csproj] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            var directory = Path.GetDirectoryName(csproj) ?? "";
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var xml = XML.Project.Parse(csproj);
                foreach (var projectReference in xml.ItemGroup.SelectMany(itemGroup => itemGroup.ProjectReferences))
                {
                    var include = projectReference.Include ?? "";
                    if (string.IsNullOrEmpty(include)) continue;

                    var resolved = ProjectReferenceFinder.FindReferencedCsproj(include, directory);
                    if (resolved is null)
                    {
                        errors.Add($"{csproj}: cannot resolve project reference '{include}'");
                        continue;
                    }

                    var fullPath = Path.GetFullPath(resolved);
                    refs.Add(fullPath);
                    if (projects.Add(fullPath)) queue.Enqueue(fullPath);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{csproj}: {ex.Message}");
            }

            references[csproj] = refs;
        }

        return TopologicalSort(projects, references, errors);
    }

    // Dependencies build first; anything that cannot be ordered (a cycle) is still built, in stable
    // order, so every project gets reported (build-all/report-all).
    private static List<string> TopologicalSort(SortedSet<string> projects,
        Dictionary<string, HashSet<string>> references, List<string> errors)
    {
        var indegree = projects.ToDictionary(project => project, _ => 0, StringComparer.OrdinalIgnoreCase);
        var dependents = projects.ToDictionary(project => project, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var pair in references)
        {
            foreach (var referenced in pair.Value)
            {
                if (!indegree.ContainsKey(referenced)) continue;
                indegree[pair.Key]++;
                dependents[referenced].Add(pair.Key);
            }
        }

        var ready = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in indegree.Where(entry => entry.Value == 0).Select(entry => entry.Key))
            ready.Add(project);

        var order = new List<string>();
        while (ready.Count > 0)
        {
            var project = ready.Min ?? "";
            ready.Remove(project);
            order.Add(project);

            foreach (var dependent in dependents[project])
            {
                if (--indegree[dependent] == 0) ready.Add(dependent);
            }
        }

        if (order.Count < projects.Count)
        {
            var cyclic = projects.Where(project => !order.Contains(project)).ToArray();
            errors.Add("Project reference cycle detected between: " +
                       string.Join(", ", cyclic.Select(Path.GetFileName)));

            // Still attempt every cyclic project so each one is reported.
            foreach (var project in cyclic) order.Add(project);
        }

        return order;
    }

    public static (bool success, string errors) Run(string projectPath)
    {
        var build = Build(projectPath);
        if (build.Compilation is not { } compilation || build.Project is not { } project)
            return (false, build.Errors);

        var embeddedTexts = build.EmbeddedTexts?.ToList() ?? new List<EmbeddedText>();

        var runErrors = project.ResolvedProjectOutputKind() == OutputKind.ConsoleApplication
            ? RunAssembly(compilation, project, embeddedTexts)
            : (success: true, errors: "");

        return (runErrors.success && build.Success, runErrors.errors + build.Errors);
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

            (bool success, string errors) result;
            using (var dllStream = File.Create(project.DllPathName))
            using (var pdbStream = File.Create(project.PDBPathName))
            {
                var emitResult = compilation.Emit(dllStream, pdbStream, options: emitOptions);
                result = (emitResult.Success, emitResult.PrintErrors());
            }

            if (!result.success)
            {
                File.Delete(project.DllPathName);
                File.Delete(project.PDBPathName);
                return result;
            }

            WriteRuntimeConfig(project);
            if (project.ResolvedProjectOutputKind() == OutputKind.ConsoleApplication)
            {
                WriteAppHost(project);
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

public sealed record BuildResult(
    bool Success,
    string Errors,
    IReadOnlyList<string> Outputs,
    TimeSpan Duration,
    CSharpCompilation? Compilation,
    Project? Project,
    IEnumerable<EmbeddedText>? EmbeddedTexts);

public sealed record SolutionProjectResult(
    string ProjectName,
    string ProjectFile,
    BuildResult Result);

public sealed record BuildSolutionResult(
    bool Success,
    IReadOnlyList<SolutionProjectResult> Projects,
    IReadOnlyList<string> Errors,
    TimeSpan Duration);