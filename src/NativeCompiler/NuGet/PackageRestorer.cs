using System.Diagnostics;

namespace NativeCompiler.NuGet;

public sealed record RestoreResult(
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ProjectsRestored,
    IReadOnlyList<string> PackagesDownloaded,
    int PackagesCached,
    TimeSpan Duration);

public sealed record RestoreProjectResult(
    string ProjectFile,
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    int PackagesDownloaded,
    TimeSpan Duration);

// Walks the package graph for a project (or solution) and materialises it into the global
// packages folder so the build-time lookup in NuGetPackage.FindPackagePath finds real files
// instead of silently skipping.
//
// Deliberate scope limits, each a reported error rather than a silent approximation:
//   - nuget.config feeds must be reachable over HTTP; no local folder or UNC feeds.
//   - No credentials, packageSourceMapping, or lock files.
//   - No project.assets.json writer yet (DepsJsonWriter still reads the SDK's, if present).
public static class PackageRestorer
{
    public static async Task<RestoreResult> RestoreProject(string projectDirectory, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        var errors = new List<string>();
        var warnings = new List<string>();
        var downloaded = new List<string>();
        var cached = 0;

        try
        {
            var csproj = FindProjectFile(projectDirectory);
            if (csproj is null)
            {
                errors.Add($"MSB1003: Specify a project or solution file. The folder '{projectDirectory}' does not contain a project file.");
                return new RestoreResult(false, errors, warnings, Array.Empty<string>(), downloaded, cached, timer.Elapsed);
            }

            var config = NuGetConfig.Load(projectDirectory);
            var globalPackagesFolder = NuGetConfig.ResolveGlobalPackagesFolder(config.GlobalPackagesFolder, projectDirectory);

            var state = new RestoreState(config, globalPackagesFolder, projectDirectory,
                downloaded, warnings, cancellationToken);

            var references = CollectPackageReferences(projectDirectory, out var targetFramework);
            state.TargetFramework = targetFramework;

            // Nothing to restore is a success, not a warning: `dotnet restore` on a project with
            // no PackageReference reports success without comment.
            if (references.Count == 0)
                return new RestoreResult(true, errors, warnings, [csproj], downloaded, cached, timer.Elapsed);

            foreach (var (id, version) in references)
            {
                var requested = version;
                if (string.IsNullOrWhiteSpace(requested))
                    requested = NuGetPackage.ResolveCentralVersion(id, projectDirectory);

                if (string.IsNullOrWhiteSpace(requested))
                {
                    errors.Add(
                        $"NU1002: Unable to find package '{id}'. No version was specified on the PackageReference " +
                        "and no central PackageVersion entry was found in a Directory.Packages.props file.");
                    continue;
                }

                await EnsurePackageAsync(id, requested, state).ConfigureAwait(false);
            }

            errors.AddRange(state.GraphErrors);
            cached = state.CachedCount;

            var success = errors.Count == 0;
            return new RestoreResult(success, errors, warnings, [csproj], downloaded, cached, timer.Elapsed);
        }
        catch (Exception exception)
        {
            errors.Add($"Restore failed: {exception.Message}");
            return new RestoreResult(false, errors, warnings, Array.Empty<string>(), downloaded, cached, timer.Elapsed);
        }
    }

    // Solution restore: restore every project in the solution and report them individually,
    // mirroring how the CLI reports a solution build.
    public static async Task<(bool success, IReadOnlyList<RestoreProjectResult> projects, TimeSpan duration)>
        RestoreSolution(string solutionPath, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        var solution = SolutionFile.Parse(solutionPath);

        var results = new List<RestoreProjectResult>();
        foreach (var project in solution.Projects)
        {
            var directory = Path.GetDirectoryName(project.ProjectPath);
            if (directory is null)
            {
                results.Add(new RestoreProjectResult(
                    project.ProjectPath, false,
                    [$"Could not resolve the folder for '{project.ProjectPath}'."],
                    Array.Empty<string>(), 0, TimeSpan.Zero));
                continue;
            }

            var result = await RestoreProject(directory, cancellationToken).ConfigureAwait(false);
            results.Add(new RestoreProjectResult(
                project.ProjectPath, result.Success, result.Errors, result.Warnings,
                result.PackagesDownloaded.Count, result.Duration));
        }

        return (results.All(result => result.Success), results, timer.Elapsed);
    }

    // Materialises one package and, recursively, everything it depends on.
    private static async Task EnsurePackageAsync(string id, string requestedVersion, RestoreState state)
    {
        var key = id.ToLowerInvariant() + "/" + requestedVersion;
        if (!state.Visited.Add(key)) return;

        if (PackageExtractor.IsExtracted(state.GlobalPackagesFolder, id, requestedVersion))
        {
            state.CachedCount++;
            await WalkDependenciesAsync(id, requestedVersion, state).ConfigureAwait(false);
            return;
        }

        if (VersionResolver.IsFloating(requestedVersion))
        {
            state.GraphErrors.Add(
                $"NU1010: The floating version '{requestedVersion}' requested for package '{id}' is not supported. " +
                "Pin an exact version, or a range such as [1.2.3, ).");
            return;
        }

        var sources = state.Config.Sources;
        if (sources.Count == 0)
        {
            state.GraphErrors.Add($"NU1001: No package source is configured for package '{id}'.");
            return;
        }

        var searched = new List<string>();
        var downloaded = false;

foreach (var configured in sources)
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            searched.Add($"{configured.Key} ({configured.Value})");

// A feed is an absolute URI or a folder path; a relative path resolves against the project,
            // which is how nuget.config relative sources behave.
            var source = configured;
            if (!source.IsAbsolute && !Path.IsPathRooted(source.Value))
                source = configured with
                {
                    Value = Path.GetFullPath(Path.Combine(state.ProjectDirectory, source.Value)),
                };

            if (source.Uri is not { } sourceUri)
            {
                state.Warnings.Add(
                    $"Package source '{source.Key}' ('{source.Value}') is not an absolute path or URI.");
                continue;
            }

            try
            {
                var flat = await NuGetFeed.GetFlatContainerAsync(sourceUri, state.CancellationToken).ConfigureAwait(false);
                var versions = await NuGetFeed.GetVersionsAsync(id, flat, state.CancellationToken).ConfigureAwait(false);

                var resolved = VersionResolver.Resolve(requestedVersion, versions);
                if (!resolved.Success)
                {
                    state.Warnings.Add(
                        $"NU1101: Could not resolve package '{id}' version '{requestedVersion}' from " +
                        $"'{source.Key}': {resolved.Error}");
                    continue;
                }

                var version = resolved.Version!;

                await using var packageStream =
                    await NuGetFeed.DownloadPackageAsync(id, version, flat, state.CancellationToken)
                        .ConfigureAwait(false);

                PackageExtractor.Extract(packageStream, state.GlobalPackagesFolder, id, version);
                state.Downloaded.Add($"{id}/{version}");
                downloaded = true;

                await WalkDependenciesAsync(id, version, state).ConfigureAwait(false);
                break;
            }
            catch (NuGetFeedException exception)
            {
                state.Warnings.Add(exception.Message);
            }
            catch (HttpRequestException exception)
            {
                state.Warnings.Add($"Could not reach package source '{source.Key}': {exception.Message}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                state.Warnings.Add($"Could not restore package '{id}' from '{source.Key}': {exception.Message}");
            }
        }

        if (!downloaded)
        {
            state.GraphErrors.Add(
                $"NU1101: Unable to find package '{id}' with version '{requestedVersion}'. " +
                $"Searched: {string.Join(", ", searched)}.");
        }
    }

    // Reads the dependencies of a now-materialised package and restores them too. Uses the
    // existing nuspec parser, which already honours <group targetFramework>, version ranges and
    // exclude="All" - so the graph matches what the build-time lookup expects.
    private static async Task WalkDependenciesAsync(string id, string version, RestoreState state)
    {
        var packagePath = PackageExtractor.PackageDirectory(state.GlobalPackagesFolder, id, version);
        if (!Directory.Exists(packagePath)) return;

        foreach (var dependency in NuGetPackage.ReadDependencies(packagePath, state.TargetFramework))
        {
            if (dependency.Excluded) continue;
            if (string.IsNullOrEmpty(dependency.Id)) continue;

            await EnsurePackageAsync(dependency.Id, dependency.Version, state).ConfigureAwait(false);
        }
    }

    

    // A project's own PackageReferences plus those of every project it references, because the
    // build resolves the whole closure into one output folder.
    private static List<(string Id, string Version)> CollectPackageReferences(
        string projectDirectory, out string targetFramework)
    {
        var references = new List<(string, string)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(projectDirectory);

        targetFramework = DefaultTargetFramework.Value;

        while (queue.Count > 0)
        {
            var directory = queue.Dequeue();
            if (!visited.Add(directory)) continue;

            var csproj = FindProjectFile(directory);
            if (csproj is null) continue;

            var files = NativeCompilerUtils.ReadAllProjectFilesPath(directory);
            var details = NativeCompilerUtils.GetProjectDetails(
                NativeCompilerUtils.ReadAllFilesInMemory(files), directory);

            // The root project's TFM decides which nuspec dependency group is selected for the
            // whole graph; referenced projects normally agree.
            if (directory == projectDirectory && details.TargetFramework is { Length: > 0 } rootTfm)
                targetFramework = rootTfm;

            foreach (var item in details.PackageReferences)
            {
                var id = item.Include ?? "";
                if (string.IsNullOrEmpty(id)) continue;
                references.Add((id, item.Version ?? ""));
            }

            foreach (var referenced in details.ProjectReferences)
            {
                var include = referenced.Include ?? "";
                if (string.IsNullOrEmpty(include)) continue;

                var referencedCsproj = ProjectReferenceFinder.FindReferencedCsproj(include, directory);
                var referencedDirectory = referencedCsproj is null
                    ? null
                    : Path.GetDirectoryName(referencedCsproj);

                if (!string.IsNullOrEmpty(referencedDirectory)) queue.Enqueue(referencedDirectory!);
            }
        }

        return references;
    }

    private static string? FindProjectFile(string directory)
    {
        var csproj = Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return csproj;
    }

    private sealed class RestoreState
    {
        public RestoreState(NuGetConfig.Result config, string globalPackagesFolder, string projectDirectory,
            List<string> downloaded, List<string> warnings, CancellationToken cancellationToken)
        {
            Config = config;
            GlobalPackagesFolder = globalPackagesFolder;
            ProjectDirectory = projectDirectory;
            Downloaded = downloaded;
            Warnings = warnings;
            CancellationToken = cancellationToken;
        }

        public NuGetConfig.Result Config { get; }
        public string GlobalPackagesFolder { get; }
        public string ProjectDirectory { get; }
        public List<string> Downloaded { get; }
        public List<string> Warnings { get; }
        public CancellationToken CancellationToken { get; }
        public HashSet<string> Visited { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> GraphErrors { get; } = new();
        public int CachedCount { get; set; }

        // The TFM whose <group> is selected from each nuspec. Taken from the project being
        // restored; a package's dependency group is per-framework, so this decides which one wins.
        public string TargetFramework { get; set; } = DefaultTargetFramework.Value;
    }
}