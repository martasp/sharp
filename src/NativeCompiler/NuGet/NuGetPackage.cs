using System.Xml.Linq;

namespace NativeCompiler.NuGet;

public static class NuGetPackage
{
    // ---------------------------------------------------------------------------
    // Feature: package buildTransitive/build <Using> items.
    // NuGet packages can ship .props files (e.g. Parlot's buildTransitive/Parlot.props) that
    // inject global usings into consuming projects (<Using Include="Parlot.Fluent.Parsers"
    // Static="true" />). The real SDK consumes these items when generating the implicit
    // GlobalUsings file; this compiler does not import package props, so without this feature
    // projects that rely on those namespaces (Literals/OneOf etc.) fail. DELETABLE FEATURE.
    // ---------------------------------------------------------------------------
    public readonly record struct PackageUsingItem(string Namespace, bool Static);

    public static List<PackageUsingItem> ReadPackageUsingItems(string packagePath)
    {
        var items = new List<PackageUsingItem>();

        foreach (var folder in new[] { Path.Combine(packagePath, "buildTransitive"), Path.Combine(packagePath, "build") })
        {
            if (!Directory.Exists(folder)) continue;

            foreach (var propsFile in Directory.EnumerateFiles(folder, "*.props", SearchOption.AllDirectories))
            {
                try
                {
                    var document = XDocument.Load(propsFile);
                    foreach (var element in document.Descendants())
                    {
                        if (!element.Name.LocalName.Equals("Using", StringComparison.OrdinalIgnoreCase)) continue;

                        var include = (string?)element.Attributes()
                            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Include", StringComparison.OrdinalIgnoreCase));
                        if (string.IsNullOrWhiteSpace(include)) continue;

                        var staticAttribute = (string?)element.Attributes()
                            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Static", StringComparison.OrdinalIgnoreCase));
                        var isStatic = !string.IsNullOrWhiteSpace(staticAttribute) &&
                                       (staticAttribute.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                        staticAttribute == "1");

                        items.Add(new PackageUsingItem(include, isStatic));
                    }
                }
                catch
                {
                    // Ignore malformed props files.
                }
            }
        }

        return items;
    }

    public static string FindPackagePath(string packageName, string packageVersion)
    {
        if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(packageVersion)) return string.Empty;

        var packageDirectory = new DirectoryInfo(GlobalPackagesFolderPath());
        if (!packageDirectory.Exists) return string.Empty;

        var exact = FindExactVersionDirectory(packageName, packageVersion);
        if (exact is not null) return exact;

        // ---------------------------------------------------------------------------
        // Feature: transitive version unification (NuGet nearest-wins approximation).
        // A dependency's nuspec declares a minimum version (e.g. Parlot 1.5.2), but the
        // resolved graph on this machine uses a newer version (e.g. 1.5.8) that is the
        // actual package cached. Falling back to the highest available version mirrors
        // the versions the real restore resolved, so compile references match. DELETABLE.
        // ---------------------------------------------------------------------------
        return FindHighestVersionDirectory(packageName, packageVersion) ?? string.Empty;
    }

    // Resolves the highest restored version of a package, without needing to know the version in
    // advance. Used when a file from the package must be copied/matched but only its simple package
    // name is known (e.g. the ILLink analyzers the compiler bundles into produced app folders); the
    // highest cached version is the one restore resolved for the current SDK, and its analyzer
    // assembly version always satisfies earlier references (.NET binds the found file if its version
    // is >= the requested one).
    public static string FindHighestPackagePath(string packageName)
    {
        if (string.IsNullOrEmpty(packageName)) return string.Empty;
        return FindHighestVersionDirectory(packageName, "0.0.0") ?? string.Empty;
    }

    // Candidate package directories: an exact (case-insensitive) name match wins; prefix
    // matches are only a fallback. The global packages folder lowercases names ("anyascii")
    // while csproj references use original case ("AnyAscii"), and Directory.GetDirectories(pattern)
    // is case-sensitive on Linux — plus a bare prefix DRAW ("GraphQL" matching "graphql.analyzers",
    // "YesSql" matching "yessql.abstractions") misresolves to the wrong package entirely.
    private static IEnumerable<DirectoryInfo> CandidatePackageDirs(string packageName)
    {
        var packageDirectory = new DirectoryInfo(GlobalPackagesFolderPath());
        if (!packageDirectory.Exists) yield break;

        var all = packageDirectory.GetDirectories();
        foreach (var dir in all.Where(dir => dir.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase)))
            yield return dir;
        foreach (var dir in all.Where(dir => !dir.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase) &&
            dir.Name.StartsWith(packageName, StringComparison.OrdinalIgnoreCase)))
            yield return dir;
    }

    private static string? FindExactVersionDirectory(string packageName, string packageVersion)
    {
        foreach (var packageDir in CandidatePackageDirs(packageName))
        {
            var matchingVersionDir = packageDir.GetDirectories()
                .FirstOrDefault(dir => dir.Name.Equals(packageVersion, StringComparison.OrdinalIgnoreCase));
            if (matchingVersionDir != null) return matchingVersionDir.FullName;
        }

        return null;
    }

    private static string? FindHighestVersionDirectory(string packageName, string packageVersion)
    {
        var hasRequested = TryParseVersion(packageVersion, out var requested);

        (System.Version version, string path)? best = null;
        var bestIsExact = false;
        foreach (var packageDir in CandidatePackageDirs(packageName))
        {
            var isExact = packageDir.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase);
            foreach (var versionDir in packageDir.GetDirectories())
            {
                if (!TryParseVersion(versionDir.Name, out var parsed)) continue;
                if (hasRequested && requested.CompareTo(parsed) > 0) continue;
                if (best is null || (bestIsExact == isExact && parsed.CompareTo(best.Value.version) > 0) ||
                    (isExact && !bestIsExact))
                    (best, bestIsExact) = ((parsed, versionDir.FullName), isExact);
            }
        }

        return best?.path;
    }

    private static bool TryParseVersion(string version, out System.Version parsed)
    {
        parsed = new System.Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(version)) return false;

        var parts = version.Split('-')[0].Split('.');
        if (parts.Length < 2) return false;

        var majorMinor = $"{parts[0]}.{parts[1]}";
        var build = parts.Length >= 3 ? parts[2] : "0";
        if (System.Version.TryParse($"{majorMinor}.{build}", out var result) && result is not null)
        {
            parsed = result;
            return true;
        }

        return false;
    }

    private static string GlobalPackagesFolderPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    // Returns the compile-time DLLs of the package whose lib/<tfm> folder best matches the target TFM.
    // Falls back to DLLs at the package root when the package ships no lib folder.
    public static string[] FindPackageDLLs(string packagePath, string targetTfm)
    {
        if (string.IsNullOrEmpty(packagePath) || !Directory.Exists(packagePath)) return Array.Empty<string>();

        var libRoot = Path.Combine(packagePath, "lib");
        var bestLibDir = BestLibDirectory(libRoot, targetTfm);
        var candidates = bestLibDir is not null
            ? EnumerateCompileDlls(bestLibDir)
            : EnumerateCompileDlls(packagePath);

        return candidates.ToArray();
    }

    // A package's direct dependencies for one target framework, read from its .nuspec. Picks the
    // best-matching <group targetFramework> (or the ungrouped form), lowers bracket ranges to
    // their minimum, and reports exclude="All" so callers can keep the dependency out of the
    // output graph. Shared by the transitive DLL walk and by restore's graph traversal.
    public static IReadOnlyList<(string Id, string Version, bool Excluded)> ReadDependencies(
        string packagePath, string targetTfm) =>
        FindDependencies(packagePath, targetTfm);

    // Resolves a package's own dependencies (from its .nuspec) transitively. Used to add the packages
    // that a referenced project depends on, which are not listed by the referencing project itself.
    public static List<string> GetTransitivePackageDlls(string packagePath, string targetTfm,
        HashSet<string>? visitedPackages = null)
    {
        var results = new List<string>();
        visitedPackages ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dependency in FindDependencies(packagePath, targetTfm))
        {
            if (!visitedPackages.Add(dependency.Id)) continue;
            if (dependency.Excluded) continue;

            var dependencyPath = FindPackagePath(dependency.Id, dependency.Version);
            if (string.IsNullOrEmpty(dependencyPath)) continue;

            results.AddRange(FindPackageDLLs(dependencyPath, targetTfm));
            results.AddRange(GetTransitivePackageDlls(dependencyPath, targetTfm, visitedPackages));
        }

        return results;
    }

    // Resolves the dependency package paths of a package (from its .nuspec) transitively.
    public static List<string> GetTransitivePackagePaths(string packagePath, string targetTfm,
        HashSet<string>? visitedPackages = null)
    {
        var results = new List<string>();
        visitedPackages ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dependency in FindDependencies(packagePath, targetTfm))
        {
            if (!visitedPackages.Add(dependency.Id)) continue;
            if (dependency.Excluded) continue;

            var dependencyPath = FindPackagePath(dependency.Id, dependency.Version);
            if (string.IsNullOrEmpty(dependencyPath)) continue;

            results.Add(dependencyPath);
            results.AddRange(GetTransitivePackagePaths(dependencyPath, targetTfm, visitedPackages));
        }

        return results;
    }

    // Resolves the version for a versionless <PackageReference> using central package management
    // (Directory.Packages.props). Returns an empty string when there is no central entry.
    public static string ResolveCentralVersion(string packageName, string projectRootPath)
    {
        var propsFile = FindDirectoryPackagesProps(projectRootPath);
        if (propsFile is null) return string.Empty;

        var doc = XDocument.Load(propsFile);
        var packageVersion = doc.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "PackageVersion" &&
                (element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Include")?.Value
                    .Equals(packageName, StringComparison.OrdinalIgnoreCase) ?? false));

        return packageVersion?.Attributes().FirstOrDefault(a => a.Name.LocalName == "Version")?.Value ?? string.Empty;
    }

    private static IReadOnlyList<(string Id, string Version, bool Excluded)> FindDependencies(
        string packagePath, string targetTfm)
    {
        var nuspec = Directory.EnumerateFiles(packagePath, "*.nuspec", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (nuspec is null) return Array.Empty<(string, string, bool)>();

        var dependencies = XDocument.Load(nuspec).Root?.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "dependencies");
        if (dependencies is null) return Array.Empty<(string, string, bool)>();

        var groups = dependencies.Elements().Where(e => e.Name.LocalName == "group").ToArray();

        IEnumerable<XElement> selected;
        if (groups.Length > 0)
        {
            var bestGroup = groups
                .OrderByDescending(group => NuGetTfm.Score(
                    AttributeValue(group, "targetFramework") ?? "", targetTfm))
                .FirstOrDefault()
                ?? groups.First();

            selected = bestGroup.Elements().Where(e => e.Name.LocalName == "dependency");
        }
        else
        {
            selected = dependencies.Elements().Where(e => e.Name.LocalName == "dependency");
        }

        return selected
            .Select(dependency => (
                Id: AttributeValue(dependency, "id") ?? "",
                Version: MinimumVersion(AttributeValue(dependency, "version") ?? ""),
                Excluded: AttributeValue(dependency, "exclude")?.Contains("All", StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(dependency => !string.IsNullOrEmpty(dependency.Id))
            .ToArray();
    }

    private static string? BestLibDirectory(string libRoot, string targetTfm)
    {
        if (!Directory.Exists(libRoot)) return null;

        return Directory.EnumerateDirectories(libRoot)
            .Where(directory => EnumerateCompileDlls(directory).Any())
            .OrderByDescending(directory => NuGetTfm.Score(Path.GetFileName(directory), targetTfm))
            .ThenBy(directory => Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)
            .Cast<string?>()
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateCompileDlls(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            : Enumerable.Empty<string>();

    // "1.2.3", "[1.2.3,)" and "(,1.2.3]" range forms all resolve to the lowest concrete version.
    private static string MinimumVersion(string version)
    {
        if (version.Length < 3 || (version[0] != '[' && version[0] != '(')) return version;

        var end = version.IndexOfAny(new[] { ',', ')' });
        var segment = end < 0 ? version.Substring(1) : version.Substring(1, end - 1);
        return segment.Trim();
    }

    private static string? FindDirectoryPackagesProps(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Directory.Packages.props");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        return null;
    }

    private static string? AttributeValue(XElement element, string localName) =>
        (string?)element.Attributes().FirstOrDefault(a => a.Name.LocalName == localName);
}