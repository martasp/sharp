using Sharp.NuGet;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyModel;
using static Sharp.SharpUtils;

namespace Sharp;

// Port of the SDK's GenerateDepsFile + DependencyContextBuilder for framework-dependent builds.
// Reads obj/project.assets.json (the same inputs the SDK's DependencyContextTask consumes) and
// materializes the runtime dependency graph, then emits <App>.deps.json through the real
// Microsoft.Extensions.DependencyModel DependencyContextWriter so the JSON matches `dotnet build`
// byte for byte (same Text.Json settings, same node shapes, same ordering).
//
// Like the SDK, a deps.json is only produced when the project was restored (assets file present);
// otherwise the file is skipped entirely.
public static class DepsJsonWriter
{
    public static void Write(Project project, IReadOnlyList<string> runtimeFilePaths,
        IReadOnlyList<string> hintPathFiles)
    {
        try
        {
            var assetsPath = Path.Combine(project.ProjectRootPath, "obj", "project.assets.json");
            if (!File.Exists(assetsPath)) return;

            using var document = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
            var root = document.RootElement;

            if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
                return;

            // Framework-dependent builds only: use the non-RID target. Assets files carry one key
            // per restored TFM (a RID-specific key contains a '/' and is never used here).
            var tfm = targets.EnumerateObject().Select(element => element.Name)
                .FirstOrDefault(name => !name.Contains('/'));
            if (tfm is null) return;

            var builder = new DependencyContextBuilderPort(project, root, targets.GetProperty(tfm),
                runtimeFilePaths, hintPathFiles, DotNetFrameworkName(tfm), tfm);
            var dependencyContext = builder.Build();

            using var stream = File.Create(project.DepsJsonPathName);
            new DependencyContextWriter().Write(dependencyContext, stream);
        }
        catch (Exception ex)
        {
            // Never hard-crash a build on a deps.json problem; the file is an SDK-parity nicety.
            Console.WriteLine($"Warning: could not write deps.json for '{project.ProjectName}': {ex.Message}");
        }
    }

    // LockFileTarget.TargetFramework.DotNetFrameworkName: "net10.0" -> ".NETCoreApp,Version=v10.0".
    private static string DotNetFrameworkName(string tfm)
    {
        if (tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            return ".NETStandard,Version=v" + tfm["netstandard".Length..];
        if (tfm.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
            return ".NETCoreApp,Version=v" + tfm["netcoreapp".Length..];
        if (tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return ".NETCoreApp,Version=v" + tfm["net".Length..];
        return tfm;
    }

    // One restored library inside the assets file's target (a node under "targets[<tfm>]").
    private sealed class DependencyLibrary
    {
        public DependencyLibrary(string name, string version, string type)
        {
            Name = name;
            Version = version;
            Type = type;
            Dependency = new Dependency(name, version);
        }

        public string Name { get; }
        public string Version { get; }
        public string Type { get; }
        public Dependency Dependency { get; }
        public string Sha512 { get; set; } = "";
        public string Path { get; set; } = "";        // assets "path" (lower-case Id/Version for packages)
        public string MsBuildProject { get; set; } = "";
        public bool ExcludeFromRuntime { get; set; }
    }

    private readonly struct Edge
    {
        public readonly string Name;
        public readonly string MinVersion;

        public Edge(string name, string minVersion)
        {
            Name = name;
            MinVersion = minVersion;
        }
    }

    private sealed class DependencyContextBuilderPort
    {
        private const string NetCorePlatformLibrary = "Microsoft.NETCore.App";
        private readonly string _dotnetFrameworkName;
        private readonly string _tfm;
        private readonly Dictionary<string, DependencyLibrary> _dependencyLibraries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Edge>> _libraryDependencies = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _mainProjectDependencies = new();
        private readonly HashSet<string> _usedLibraryNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _packageRoots = new(StringComparer.OrdinalIgnoreCase);

        private readonly IReadOnlyList<string> _inputs;
        private readonly HashSet<string> _consumed = new(StringComparer.OrdinalIgnoreCase);

        // Copy-local HintPath <Reference> assemblies (the SDK generator/analyzer set the compiler
        // itself links). The SDK emits each as a standalone "reference" runtime library so the
        // produced app can bind the type at runtime; without these entries a JIT build of the
        // compiler cannot resolve its statically instantiated generators (AnalyzerRegistry).
        private readonly IReadOnlyList<string> _referenceFiles;

        private readonly string _mainProjectName;
        private readonly string _mainProjectVersion;
        private readonly string _outputName;

        public DependencyContextBuilderPort(Project project, JsonElement root, JsonElement target,
            IReadOnlyList<string> runtimeFilePaths, IReadOnlyList<string> hintPathFiles,
            string dotnetFrameworkName, string tfm)
        {
            _mainProjectName = project.ProjectName;
            _mainProjectVersion = project.Version;
            _outputName = project.ProjectName + ".dll";
            _dotnetFrameworkName = dotnetFrameworkName;
            _tfm = tfm;
            _inputs = runtimeFilePaths;
            _referenceFiles = hintPathFiles;

            var libraries = root.TryGetProperty("libraries", out var libs) ? libs : default;

            foreach (var node in target.EnumerateObject())
            {
                var (name, version) = SplitIdentity(node.Name);
                var type = "package";
                var sha512 = "";
                var path = "";
                var msbuildProject = "";

                if (libraries.ValueKind == JsonValueKind.Object &&
                    libraries.TryGetProperty(node.Name, out var libraryEntry) &&
                    libraryEntry.ValueKind == JsonValueKind.Object)
                {
                    type = PropertyString(libraryEntry, "type") ?? type;
                    sha512 = PropertyString(libraryEntry, "sha512") ?? "";
                    path = PropertyString(libraryEntry, "path") ?? "";
                    msbuildProject = PropertyString(libraryEntry, "msbuildProject") ?? "";
                }

                _dependencyLibraries[name] = new DependencyLibrary(name, version, type)
                {
                    Sha512 = sha512,
                    Path = path,
                    MsBuildProject = msbuildProject,
                };

                var edges = new List<Edge>();
                if (node.Value.TryGetProperty("dependencies", out var dependencies) &&
                    dependencies.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dependency in dependencies.EnumerateObject())
                        edges.Add(new Edge(dependency.Name, MinVersion(dependency.Value.GetString())));
                }
                _libraryDependencies[name] = edges;
            }

            foreach (var name in _dependencyLibraries.Keys)
                _usedLibraryNames.Add(name);

            // projectFileDependencyGroups: top-level (project/package) dependencies, in declaration
            // order -> the order of the app's "dependencies" node.
            if (root.TryGetProperty("projectFileDependencyGroups", out var groups) &&
                groups.ValueKind == JsonValueKind.Object)
            {
                foreach (var group in groups.EnumerateObject())
                {
                    foreach (var dependency in group.Value.EnumerateArray())
                    {
                        var text = dependency.GetString() ?? "";
                        var separator = text.IndexOf(' ');
                        var dependencyName = separator > 0 ? text[..separator] : text;
                        if (dependencyName.Length > 0 && _dependencyLibraries.ContainsKey(dependencyName))
                            _mainProjectDependencies.Add(dependencyName);
                    }
                }
            }

            // packagesToPrune is NOT consumed here: the SDK's GenerateDepsFile only populates
            // ProjectContext.PackagesToBeFiltered from runtime-store packages, so a framework-provided
            // NuGet library (e.g. Microsoft.Extensions.Configuration) stays in deps.json as long as it
            // has runtime assets. Framework-suppressed libraries are dropped by TrimLibrariesWithoutAssets
            // because their files were never resolved as runtime assets.
            if (root.TryGetProperty("project", out var projectJson) &&
                projectJson.TryGetProperty("frameworks", out var frameworks) &&
                frameworks.TryGetProperty(_tfm, out var frameworkJson) &&
                frameworkJson.TryGetProperty("packagesToPrune", out _))
            {
                // Intentionally empty: prune ranges drive restore-side suppression, not deps.json.
            }

            foreach (var library in _dependencyLibraries.Values.Where(l => l.Type == "package"))
            {
                var rootPath = NuGetPackage.FindPackagePath(library.Name, library.Version);
                if (rootPath is not null)
                    _packageRoots[library.Name + "/" + library.Version] = rootPath;
            }
        }

        public DependencyContext Build()
        {
            CalculateExcludedLibraries();

            var runtimeLibraries = new List<ModifiableRuntimeLibrary>();
            runtimeLibraries.Add(GetProjectRuntimeLibrary());

            foreach (var library in _dependencyLibraries.Values.Where(l => !l.ExcludeFromRuntime))
            {
                var runtimeLibrary = GetRuntimeLibrary(library);
                if (runtimeLibrary is not null) runtimeLibraries.Add(runtimeLibrary);
            }

            // Local files not claimed by any package are the referenced projects' produced dlls
            // (the SDK's UserRuntimeAssembly set), used to read versions for project runtime nodes.
            var localByBaseName = _inputs
                .Where(file => !_consumed.Contains(file))
                .ToDictionary(file => Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase);

            for (var i = 1; i < runtimeLibraries.Count; i++)
            {
                var runtimeLibrary = runtimeLibraries[i];
                if (runtimeLibrary.Library.Type != "project") continue;
                if (runtimeLibrary.Library.RuntimeAssemblyGroups.Count == 0) continue;
                var runtimeFile = runtimeLibrary.Library.RuntimeAssemblyGroups[0].RuntimeFiles.FirstOrDefault();
                if (runtimeFile is null || !string.IsNullOrEmpty(runtimeFile.AssemblyVersion)) continue;

                if (localByBaseName.TryGetValue(runtimeLibrary.Library.Name, out var localFile))
                {
                    runtimeLibrary.Library = new RuntimeLibrary(
                        runtimeLibrary.Library.Type,
                        runtimeLibrary.Library.Name,
                        runtimeLibrary.Library.Version,
                        runtimeLibrary.Library.Hash,
                        new[] { new RuntimeAssetGroup(string.Empty, new[] { CreateRuntimeFile(runtimeFile.Path, localFile) }) },
                        runtimeLibrary.Library.NativeLibraryGroups,
                        runtimeLibrary.Library.ResourceAssemblies,
                        runtimeLibrary.Library.Dependencies,
                        runtimeLibrary.Library.Serviceable,
                        runtimeLibrary.Library.Path,
                        runtimeLibrary.Library.HashPath,
                        runtimeLibrary.Library.RuntimeStoreManifestName);
                }
            }

            TrimLibrariesWithoutAssets(runtimeLibraries);

            foreach (var hintPath in _referenceFiles)
            {
                var fileName = Path.GetFileName(hintPath);
                var baseName = Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrEmpty(baseName)) continue;

                // A reference library is only emitted when nothing already claimed the name - a
                // package or project named identically wins (the SDK's user-assembly dedup).
                if (runtimeLibraries.Any(library => library.Library.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var runtimeFile = CreateRuntimeFile(fileName, hintPath);
                var version = runtimeFile.AssemblyVersion ?? "0.0.0";
                runtimeLibraries.Add(new ModifiableRuntimeLibrary(new RuntimeLibrary(
                    type: "reference",
                    name: baseName,
                    version: version,
                    hash: string.Empty,
                    runtimeAssemblyGroups: new[] { new RuntimeAssetGroup(string.Empty, new[] { runtimeFile }) },
                    nativeLibraryGroups: Array.Empty<RuntimeAssetGroup>(),
                    resourceAssemblies: Array.Empty<ResourceAssembly>(),
                    dependencies: Array.Empty<Dependency>(),
                    path: null,
                    hashPath: null,
                    runtimeStoreManifestName: null,
                    serviceable: false)));
            }

            var libraryNames = runtimeLibraries.Select(library => library.Library.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var targetInfo = new TargetInfo(_dotnetFrameworkName, null, "", isPortable: true);

            var final = runtimeLibraries.Select(library => new RuntimeLibrary(
                library.Library.Type,
                library.Library.Name,
                library.Library.Version,
                library.Library.Hash,
                library.Library.RuntimeAssemblyGroups,
                library.Library.NativeLibraryGroups,
                library.Library.ResourceAssemblies,
                library.Library.Dependencies.Where(dependency => libraryNames.Contains(dependency.Name)).ToList(),
                library.Library.Serviceable,
                library.Library.Path,
                library.Library.HashPath,
                library.Library.RuntimeStoreManifestName)).ToList();

            return new DependencyContext(
                targetInfo,
                CompilationOptions.Default,
                Array.Empty<CompilationLibrary>(),
                final,
                Array.Empty<RuntimeFallbacks>());
        }

        private void CalculateExcludedLibraries()
        {
            var runtimeExclusionList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Platform exclusion: walk the platform library's dependency graph from its resolved
            // version, excluding libraries whose version matches exactly. A framework-dependent
            // build only has a Microsoft.NETCore.App node when something pulled it in as a package;
            // otherwise this walk is skipped - exactly like the SDK, which derives its platform
            // library from the lock file and gets null here.
            if (_dependencyLibraries.TryGetValue(NetCorePlatformLibrary, out var platform))
            {
                runtimeExclusionList.Add(platform.Name);
                var toWalk = new Stack<Edge>(_libraryDependencies.GetValueOrDefault(platform.Name) ?? Enumerable.Empty<Edge>());

                while (toWalk.Count > 0)
                {
                    var edge = toWalk.Pop();
                    if (runtimeExclusionList.Contains(edge.Name)) continue;

                    if (_dependencyLibraries.TryGetValue(edge.Name, out var library) &&
                        string.Equals(library.Version, edge.MinVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        runtimeExclusionList.Add(edge.Name);
                        foreach (var deeper in _libraryDependencies.GetValueOrDefault(edge.Name) ?? Enumerable.Empty<Edge>())
                            toWalk.Push(deeper);
                    }
                }
            }

            // No package pruning for deps.json: the SDK never applies packagesToPrune ranges here
            // (see the note in the constructor); libraries without runtime assets are removed later
            // by TrimLibrariesWithoutAssets.

            foreach (var excluded in runtimeExclusionList)
            {
                if (_dependencyLibraries.TryGetValue(excluded, out var library))
                    library.ExcludeFromRuntime = true;
            }
        }

        private List<Dependency> GetProjectDependencies()
        {
            var dependencies = new List<Dependency>();
            foreach (var dependencyName in _mainProjectDependencies)
            {
                if (_dependencyLibraries.TryGetValue(dependencyName, out var library) &&
                    !library.ExcludeFromRuntime)
                {
                    dependencies.Add(library.Dependency);
                }
            }
            return dependencies;
        }

        private ModifiableRuntimeLibrary GetProjectRuntimeLibrary()
        {
            var runtimeAssemblyGroups = new[]
            {
                new RuntimeAssetGroup(string.Empty, _outputName),
            };

            // The app entry is emitted first and carries no assembly/file versions. Its name is
            // kept unless a lock library already claimed it (SDK GetUniqueLibraryName).
            var libraryName = _mainProjectName;
            if (_usedLibraryNames.Contains(libraryName))
            {
                libraryName = _mainProjectName + ".Project";
                var suffix = 1;
                while (_usedLibraryNames.Contains(libraryName))
                    libraryName = _mainProjectName + ".Project" + suffix++;
            }
            _usedLibraryNames.Add(libraryName);

            return new ModifiableRuntimeLibrary(new RuntimeLibrary(
                type: "project",
                name: libraryName,
                version: _mainProjectVersion,
                hash: string.Empty,
                runtimeAssemblyGroups: runtimeAssemblyGroups,
                nativeLibraryGroups: Array.Empty<RuntimeAssetGroup>(),
                resourceAssemblies: Array.Empty<ResourceAssembly>(),
                dependencies: GetProjectDependencies(),
                path: null,
                hashPath: null,
                runtimeStoreManifestName: null,
                serviceable: false));
        }

        private ModifiableRuntimeLibrary? GetRuntimeLibrary(DependencyLibrary library)
        {
            var hash = string.Empty;
            string? path = null;
            string? hashPath = null;
            var serviceable = true;
            var dependencies = new List<Dependency>();
            var runtimeAssemblyGroups = new List<RuntimeAssetGroup>();
            var nativeLibraryGroups = new List<RuntimeAssetGroup>();
            var resourceAssemblies = new List<ResourceAssembly>();

            if (_libraryDependencies.TryGetValue(library.Name, out var edges))
            {
                foreach (var edge in edges)
                {
                    if (_dependencyLibraries.TryGetValue(edge.Name, out var dependency) &&
                        !dependency.ExcludeFromRuntime)
                    {
                        dependencies.Add(dependency.Dependency);
                    }
                }
            }

            if (library.Type == "project")
            {
                serviceable = false;

                // Project runtime asset: the referenced project's produced dll under its bare output
                // name. Versions (assembly/file) are read from the actual file in Build() via the
                // local file set; when there is no local copy the entry carries empty versions -
                // mirroring the SDK's fallback for a project whose output was not co-located.
                runtimeAssemblyGroups.Add(new RuntimeAssetGroup(string.Empty,
                    new[] { new RuntimeFile(_outputNameFor(library.Name), string.Empty, string.Empty, null) }));
            }
            else if (library.Type == "package")
            {
                serviceable = true;
                if (!string.IsNullOrEmpty(library.Sha512))
                {
                    hash = "sha512-" + library.Sha512;
                    hashPath = library.Name.ToLowerInvariant() + "." + library.Version + ".nupkg.sha512";
                }
                path = library.Path;

                if (_packageRoots.TryGetValue(library.Name + "/" + library.Version, out var root))
                {
                    var files = RuntimeFilesOfPackage(root);
                    var runtimeFiles = files.Where(f => !f.IsResources)
                        .Select(f => CreateRuntimeFile(f.Relative, f.FullPath))
                        .ToList();
                    var resourceFiles = files.Where(f => f.IsResources).ToList();
                    if (runtimeFiles.Count > 0)
                        runtimeAssemblyGroups.Add(new RuntimeAssetGroup(string.Empty, runtimeFiles));
                    foreach (var resourceFile in resourceFiles)
                        resourceAssemblies.Add(new ResourceAssembly(resourceFile.Relative, CultureOf(resourceFile.Relative)));
                }
            }

            return new ModifiableRuntimeLibrary(new RuntimeLibrary(
                type: library.Type,
                name: library.Name,
                version: library.Version,
                hash: hash,
                runtimeAssemblyGroups: runtimeAssemblyGroups,
                nativeLibraryGroups: nativeLibraryGroups,
                resourceAssemblies: resourceAssemblies,
                dependencies: dependencies,
                path: path,
                hashPath: hashPath,
                runtimeStoreManifestName: null,
                serviceable: serviceable));
        }

        private static string _outputNameFor(string libraryName) => libraryName + ".dll";

        // All copied runtime files that live under the package's cache folder. Only the
        // nuget/local resolved set is considered (hint-path copies are never package assets).
        // Order matches the SDK's resolved-file ordering for a single-assembly package; multiples
        // are emitted sorted by package-relative path.
        private List<(string Relative, string FullPath, bool IsResources)> RuntimeFilesOfPackage(string root)
        {
            var result = new List<(string Relative, string FullPath, bool IsResources)>();
            foreach (var file in _inputs)
            {
                if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                var relative = file[(root.Length + 1)..].Replace('\\', '/');
                var isResources = relative.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);
                _consumed.Add(file);
                result.Add((relative, file, isResources));
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Relative, b.Relative));
            return result;
        }

        // Culture of a satellite resource asset, derived from its package-relative path:
        // "lib/net10.0/fr/Foo.resources.dll" -> "fr". NuGet stamps the same value as "locale"
        // on the lock file item, which is what the SDK writes into the resources node.
        private static string CultureOf(string relative)
        {
            var normalized = relative.Replace('\\', '/');
            var index = normalized.LastIndexOf('/');
            if (index < 0) return string.Empty;
            var start = normalized.LastIndexOf('/', index - 1);
            return normalized[(start + 1)..index];
        }

        private static RuntimeFile CreateRuntimeFile(string path, string fullPath)
        {
            var fileVersion = ReadFileVersion(fullPath);
            var assemblyVersion = ReadAssemblyVersion(fullPath);
            return new RuntimeFile(path, assemblyVersion, fileVersion, null);
        }

        private void TrimLibrariesWithoutAssets(List<ModifiableRuntimeLibrary> runtimeLibraries)
        {
            var libraries = runtimeLibraries.ToDictionary(library => library.Library.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var reference in runtimeLibraries)
            {
                foreach (var dependency in reference.Library.Dependencies)
                {
                    if (libraries.TryGetValue(dependency.Name, out var dependent))
                        dependent.Dependents.Add(reference.Library.Name);
                }
            }

            var unprocessed = runtimeLibraries.ToList();
            while (unprocessed.Count > 0)
            {
                var index = unprocessed.Count - 1;
                var lib = unprocessed[index];
                unprocessed.RemoveAt(index);
                if (!runtimeLibraries.Contains(lib)) continue;

                // xunit/xunit.core are kept even without assets: the xUnit v2 runner identifies
                // test projects by their presence in deps.json (dotnet/sdk#49248).
                if (lib.Library.Name.Equals("xunit", StringComparison.OrdinalIgnoreCase) ||
                    lib.Library.Name.Equals("xunit.core", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (lib.Library.RuntimeAssemblyGroups.Count == 0 &&
                    lib.Library.NativeLibraryGroups.Count == 0 &&
                    lib.Library.ResourceAssemblies.Count == 0)
                {
                    if (lib.Library.Dependencies.All(dependency =>
                            !libraries.TryGetValue(dependency.Name, out var value) || value.Dependents.Count > 1))
                    {
                        runtimeLibraries.Remove(lib);
                        libraries.Remove(lib.Library.Name);
                        foreach (var dependency in lib.Library.Dependencies)
                            if (libraries.TryGetValue(dependency.Name, out var value))
                                value.Dependents.Remove(lib.Library.Name);
                        foreach (var dependent in lib.Dependents)
                            if (libraries.TryGetValue(dependent, out var value))
                                unprocessed.Add(value);
                    }
                }
            }
        }

        private sealed class ModifiableRuntimeLibrary
        {
            public ModifiableRuntimeLibrary(RuntimeLibrary library)
            {
                Library = library;
                Dependents = new HashSet<string>();
            }

            public HashSet<string> Dependents { get; }
            public RuntimeLibrary Library { get; internal set; }
        }
    }

    // ---- Shared helpers ---------------------------------------------------------------

    private static (string Name, string Version) SplitIdentity(string identity)
    {
        var separator = identity.LastIndexOf('/');
        return separator < 0
            ? (identity, "0.0.0")
            : (identity[..separator], identity[(separator + 1)..]);
    }

    private static string? PropertyString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // The minimum version carried by a lock dependency. Values are either a bare resolved version
    // ("10.0.0") or a NuGet range ("[10.0.0, )") - the SDK uses VersionRange.MinVersion.
    private static string MinVersion(string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return "0.0.0";
        var trimmed = range.Trim().TrimStart('[', '(').TrimEnd(']', ')');
        var comma = trimmed.IndexOf(',');
        var min = comma >= 0 ? trimmed[..comma] : trimmed;
        return string.IsNullOrWhiteSpace(min) ? "0.0.0" : min.Trim();
    }

// FileUtilities.GetFileVersion(file).ToString(): the PE file version resource, exactly as the
    // SDK writes it. Null when the file carries no version resource.
    private static string? ReadFileVersion(string path)
    {
        try
        {
            var raw = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (string.IsNullOrEmpty(raw)) return null;
            return Version.TryParse(raw, out var parsed) ? parsed.ToString() : raw;
        }
        catch
        {
            return null;
        }
    }

    // FileUtilities.TryGetAssemblyVersion(file)?.ToString(): the managed assembly version.
    private static string? ReadAssemblyVersion(string path)
    {
        try
        {
            return AssemblyName.GetAssemblyName(path).Version?.ToString();
        }
        catch
        {
            return null;
        }
    }
}