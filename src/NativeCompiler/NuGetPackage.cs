using System.Xml.Linq;

namespace NativeCompiler;

public static class NuGetPackage
{
    public static string FindPackagePath(string packageName, string packageVersion)
    {
        if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(packageVersion)) return string.Empty;

        // Specify the path to the global package cache folder
        var globalPackagesFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");

        var packageDirectory = new DirectoryInfo(globalPackagesFolderPath);
        if (!packageDirectory.Exists) return string.Empty;

        foreach (var packageDir in packageDirectory.GetDirectories($"{packageName}*"))
        {
            var matchingVersionDir = packageDir.GetDirectories()
                .FirstOrDefault(dir => dir.Name.Equals(packageVersion, StringComparison.OrdinalIgnoreCase));

            if (matchingVersionDir != null) return matchingVersionDir.FullName;
        }

        return string.Empty;
    }

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