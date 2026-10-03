using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
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
            // Never hard-crash the compiler on an IO failure, even under a Debug build.
            // Typical case: the CLI re-builds itself from its own output folder while the
            // already-loaded references are kept mapped by the running process.
            Console.WriteLine($"Warning: could not copy runtime references to '{folderPath}': {ex.Message}");
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
        var files = ReadAllFilesInMemory(projectFiles); //slow part 1300ms => now 6ms
        var project = GetProjectDetails(files, projectPath);

        var dotNetAssemblyRefPath = AsemblyExtensions.ResolveSdkRefLocation;

        var compilationOptions = GetCompilationOptions(project);

        var defaultReferencesForCompiling = GetdefaultReferencesForCompiling(dotNetAssemblyRefPath);

        var parsedData = ParseProject(project);
        var syntaxTrees = parsedData.Select(item => item.syntaxTree);
        var embeddedTexts = parsedData.Select(item => item.embeddedText);

        var defaultLocalReferences = GetdefaultLocalReferences(project);
        var defaultNugetReferences = GetdefaultNugetReferences(project);
        var frameworkReferenceRefs = GetFrameworkReferenceCompilingRefs(project);

        var mergedDefaultReferences = MergeFrameworkSuppression(
            defaultNugetReferences.Concat(defaultLocalReferences),
            defaultReferencesForCompiling.Concat(frameworkReferenceRefs));

// The shared framework's own assemblies are never copied next to the output app - they come from the
        // runtime at execution time - and they are no longer even available as files, since only their
        // names are embedded. So the copy-local candidates are just the project's own dependencies.
        var mergedDefaultReferencesForRunning = MergeFrameworkSuppression(
            defaultNugetReferences.Concat(defaultLocalReferences), Array.Empty<MetadataReference>());

        outputs.Add($"Merged default references count: {mergedDefaultReferences.Length}");

        var compilation = CSharpCompilation.Create(project.ProjectName, syntaxTrees, mergedDefaultReferences,compilationOptions);

        // Feature: source-generator + analyzer pipeline. Generators always run the statically
        // linked SDK set; under JIT (not NativeAOT) they additionally discover the project's own
        // analyzer assemblies (package analyzers + Analyzer project references like
        // OrchardCore.SourceGenerators) via a shadow-copied load context. AnalyzerRunner merges
        // their trees, then the static analyzers run over the merged compilation.
        var (generatedCompilation, analyzerErrors, analyzerWarnings) = AnalyzerRunner.Run(compilation, project);
        compilation = generatedCompilation;
        //string? analyzerErrors = "";    

        // Test projects (xunit.v3/vstest/MTP) get their entry point injected by the framework's
        // MSBuild targets (XunitAutoGeneratedEntryPoint.cs) which don't run here; mirror it so
        // OutputType=Exe test projects compile (CS5001 otherwise). Run() then works against the
        // shim's entry point like it would against the real generated one.
        if (NeedsTestEntryPointShim(compilation, project))
        {
            compilation = compilation.AddSyntaxTrees(BuildTestEntryPointTree(project.ProjectName));
        }

        // NU1001/NU1002: a PackageReference that never resolved. Reported as warnings alongside the
        // analyzer diagnostics so the user learns which package is missing before the CS0246s
        // that it will cause.
        var packageWarnings = GetUnresolvedPackageWarnings(project);
        if (packageWarnings.Count > 0)
        {
            analyzerWarnings = string.Join(
                Environment.NewLine,
                analyzerWarnings.Split('\n', StringSplitOptions.RemoveEmptyEntries).Concat(packageWarnings));
        }

        if (!string.IsNullOrEmpty(analyzerErrors))
        {
            return new BuildResult(
                false,
                analyzerErrors,
                outputs,
                timer.Elapsed,
                compilation,
                project,
                embeddedTexts,
                analyzerWarnings);
        }

        var produceDll = ProduceDll(compilation, project); // slowest part 1sec, (TODO dont need to save dll if running on compile time)

        // SDK mirror: only the app's own artifacts and non-framework NuGet/local assemblies are
        // co-located in bin. Shared-framework DLLs (Microsoft.NETCore.App & Microsoft.AspNetCore.App)
        // are resolved from the installed runtime at run time - never copied by `dotnet build`.
        // HintPath <Reference> assemblies are CopyLocal in MSBuild (the compiler's own graph carries
        // the SDK generator/analyzer DLLs so the self-hosted JIT stays bindable), so those ride along.
        var paths = GetOutputRuntimeFiles(mergedDefaultReferencesForRunning, project.ProjectRootPath, project.TargetFramework ?? "net10.0");
        // SDK mirror: restore-derived runtime graph (*.deps.json). Only the NuGet/local runtime set
        // feeds it - HintPath copies are reference/analyzer assemblies, never package assets. They
        // are still registered in deps.json as "reference" libraries (DepsJsonWriter) so a JIT
        // build of the compiler can bind its statically linked generators at runtime.
        var hintPathCopies = GetHintPathCopyLocalFiles(projectPath);
        DepsJsonWriter.Write(project, paths, hintPathCopies);
        paths.AddRange(hintPathCopies);
        WriteFiles(project.SharpBinFolderPath, paths);

        // Self-hosting: when the project being compiled consumes sharp itself (NativeCompilerCLI via
        // its ProjectReference), the emitted assembly needs the embedded SDK payload. MSBuild embeds
        // it when it publishes sharp, but there is no MSBuild in this loop, so sharp hands its own
        // payload to the compiler it is producing - the same mechanism the generator/analyzer
        // assemblies use via HintPath copy-local.
        if (mergedDefaultReferences.Any(IsCompilerReference))
        {
            EmbeddedSdkRefs.WritePayloadTo(project.SharpBinFolderPath);
        }

        if (produceDll.success)
            outputs.Add($"Compiled {project.ProjectName} successfully");

        return new BuildResult(
            produceDll.success,
            produceDll.errors,
            outputs,
            timer.Elapsed,
            compilation,
            project,
            embeddedTexts,
            analyzerWarnings);
    }

    // True when a merged reference is sharp's own compiler library, i.e. the project being built
    // consumes the compiler and therefore needs the embedded SDK payload propagated to its output.
    private static bool IsCompilerReference(MetadataReference reference)
    {
        var display = reference.Display;
        return !string.IsNullOrEmpty(display)
               && Path.GetFileNameWithoutExtension(display)
                   .Equals("NativeCompiler", StringComparison.OrdinalIgnoreCase);
    }

    public static BuildSolutionResult BuildSolution(string solutionPath)
    {
        var timer = Stopwatch.StartNew();
        var solution = SolutionFile.Parse(solutionPath);

        var errors = new List<string>();
        var (buildOrder, buildWaves) = ResolveBuildOrder(
            solution.Projects.Select(project => project.ProjectPath), errors);

        var results = new List<SolutionProjectResult>();
        var cache = new System.Collections.Concurrent.ConcurrentDictionary<string, BuildResult>(StringComparer.OrdinalIgnoreCase);

        BuildResult BuildOne(string csproj)
        {
            try
            {
                var folder = Path.GetDirectoryName(csproj)
                             ?? throw new InvalidOperationException($"Cannot resolve folder for '{csproj}'");
                return Build(folder);
            }
            catch (Exception ex)
            {
                return new BuildResult(false, ex.ToString(),
                    new[] { $"{Path.GetFileName(csproj)} failed: {ex.Message}" },
                    TimeSpan.Zero, null, null, null, string.Empty);
            }
        }

        // Build each independent wave's projects concurrently. Members of one wave share no reference
        // edges (all their references live in earlier waves), so they can build in parallel safely.
        // Shared state (BuildsInProgress, AnalyzerDllCache) is ConcurrentDictionary; BuildPropsEvaluator
        // holds no shared mutable statics. Results land in a thread-safe cache keyed by csproj.
        foreach (var wave in buildWaves)
        {
            // Parallel.ForEach is the right tool for blocking, CPU-bound work: it partitions the
            // wave among the thread pool without the Task.Run + WhenAll(...).GetResult() anti-pattern
            // (which squats a blocked worker per item and risks pool starvation under heavy waves).
            Parallel.ForEach(
                wave.Where(csproj => !cache.ContainsKey(csproj)),
                csproj => cache[csproj] = BuildOne(csproj));
        }

        // If a cyclic reference prevented a project from reaching any wave, still build it serially
        // so every project is reported (mirrors the serial build-all/report-all contract).
        foreach (var csproj in buildOrder.Where(csproj => !cache.ContainsKey(csproj)))
        {
            cache[csproj] = BuildOne(csproj);
        }

        results.AddRange(buildOrder.Select(csproj => new SolutionProjectResult(
            Path.GetFileNameWithoutExtension(csproj), csproj, cache[csproj])));

        var success = errors.Count == 0 && results.All(projectResult => projectResult.Result.Success);
        return new BuildSolutionResult(success, results, errors, timer.Elapsed);
    }

    private static (List<string> order, List<List<string>> waves) ResolveBuildOrder(
        IEnumerable<string> initialProjects, List<string> errors)
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
    private static (List<string> order, List<List<string>> waves) TopologicalSort(
        SortedSet<string> projects, Dictionary<string, HashSet<string>> references, List<string> errors)
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
        var waves = new List<List<string>>();

        // Kahn's algorithm grouped into "waves": snapshot the whole current ready set as one wave,
        // emit all its members, then promote newly-zero-indegree projects into the next wave. No
        // member of a wave references another member (all its references live in earlier waves), so
        // every member of a wave can be built concurrently. A cycle is still emitted (below) so every
        // project gets reported.
        while (ready.Count > 0)
        {
            var wave = new List<string>(ready);
            waves.Add(wave);
            ready.Clear();

            foreach (var project in wave)
            {
                order.Add(project);
                foreach (var dependent in dependents[project])
                {
                    if (--indegree[dependent] == 0) ready.Add(dependent);
                }
            }
        }

        if (order.Count < projects.Count)
        {
            var cyclic = projects.Where(project => !order.Contains(project)).ToArray();
            errors.Add("Project reference cycle detected between: " +
                       string.Join(", ", cyclic.Select(Path.GetFileName)));

            // Still attempt every cyclic project so each one is reported.
            foreach (var project in cyclic) order.Add(project);
            if (cyclic.Length > 0) waves.Add(cyclic.ToList());
        }

        return (order, waves);
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
            //highEntropyVirtualAddressSpace: true mirrors the SDK's AnyCPU PE characteristics
            //(0x8560, HIGH_ENTROPY_VA bit set), matching `dotnet build` output. Deterministic
            //emission is enabled on the compilation options (this Roslyn fork keeps the flag there).
            var emitOptions = new EmitOptions(
                debugInformationFormat: DebugInformationFormat.PortablePdb,
                pdbFilePath: project.PDBPathName,
                highEntropyVirtualAddressSpace: true);

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

            if (project.UsesAspNetCoreApp)
                WriteRuntimeConfigWeb(project);
            else
                WriteRuntimeConfig(project);
            if (project.UseAppHost && project.ResolvedProjectOutputKind() == OutputKind.ConsoleApplication)
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
        var originalDirectory = Directory.GetCurrentDirectory();
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
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
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
    IEnumerable<EmbeddedText>? EmbeddedTexts,
    string Warnings);

public sealed record SolutionProjectResult(
    string ProjectName,
    string ProjectFile,
    BuildResult Result);

public sealed record BuildSolutionResult(
    bool Success,
    IReadOnlyList<SolutionProjectResult> Projects,
    IReadOnlyList<string> Errors,
    TimeSpan Duration);