using System.Diagnostics;
using XML;

namespace Sharp;

public static class SolutionBuild
{
    [HasSideEffects]
    public static BuildSolutionResult Build(string solutionPath)
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
                return ProjectBuild.Build(folder);
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

    [Query]
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

            // Analyzer project references injected through Directory.Build.props (e.g. OrchardCore's
            // Commons.props adds OrchardCore.SourceGenerators to every project) are build dependencies
            // too, but they live in the evaluated props chain rather than the csproj, so the
            // ProjectReference items above never see them. Left out of the plan, a shared generator
            // lands in the same wave as its consumers, and each consumer builds it on demand -
            // concurrently and redundantly. Adding the edge puts it in its own earlier wave instead.
            foreach (var analyzerRef in AnalyzerProjectReferences(csproj, directory))
            {
                refs.Add(analyzerRef);
                if (projects.Add(analyzerRef)) queue.Enqueue(analyzerRef);
            }

            references[csproj] = refs;
        }

        return TopologicalSort(projects, references, errors);
    }

    // Analyzer project references injected through Directory.Build.props, excluding the self-edge (see
    // comment at the call site). Returned rather than accumulated so the caller owns the graph state.
    [Query]
    private static IEnumerable<string> AnalyzerProjectReferences(string csproj, string directory)
    {
        var self = Path.GetFullPath(csproj);
        foreach (var analyzerRef in BuildPropsEvaluator.Evaluate(directory).AnalyzerProjectReferences)
        {
            // The props evaluator does not honor MSBuild conditions, so a reference carrying
            // Condition="$(MSBuildProjectName) != 'ThisProject'" is read as a self-reference. Those
            // generator projects skip themselves at run time (SourceGeneratorRunner.SkippedSelf), so
            // drop the self-edge here - otherwise it is an unorderable cycle and the whole solution
            // falls back to a serial build.
            if (string.Equals(analyzerRef, self, StringComparison.OrdinalIgnoreCase)) continue;

            yield return analyzerRef;
        }
    }

    // Dependencies build first; anything that cannot be ordered (a cycle) is still built, in stable
    // order, so every project gets reported (build-all/report-all).
    [Pure]
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
}
