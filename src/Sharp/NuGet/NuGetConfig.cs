using System.Xml.Linq;

namespace Sharp.NuGet;

// Reads nuget.config so restore knows which feeds to talk to and where the global packages
// folder lives. Hand-rolled XDocument (AOT-safe, no XmlSerializer reflection), matching the
// style of XMLProject.cs.
//
// Merge rules, mirroring the SDK:
//   - <packageSources> is *replaced* by the nearest config that declares one, unless that
//     declaration starts with <clear/>, which wipes everything inherited from outer configs.
//   - <config> key/value pairs are additive across the chain; nearest wins per key.
// Walking up from the project directory, outermost first, so nearer files overwrite farther ones.
public static class NuGetConfig
{
    public const string DefaultSource = "https://api.nuget.org/v3/index.json";

    // nuget.config is hand-written and people write "NuGet.Config", "NuGet.config" and
    // "nuget.config" interchangeably. Directory enumeration is case-sensitive on Linux, so
    // FindConfigFiles enumerates and compares rather than probing fixed names.

    public readonly record struct PackageSource(string Key, string Value)
    {
        // A feed is either an absolute URI (https://api.nuget.org/v3/index.json) or a path to a
        // folder laid out as a flat container (../packages). Relative paths resolve against the
        // project directory, which the restorer supplies.
        public bool IsAbsolute => Uri.TryCreate(Value, UriKind.Absolute, out _);

        public bool IsRemote => Uri.TryCreate(Value, UriKind.Absolute, out var uri) && !uri.IsFile;

        public Uri? Uri => Uri.TryCreate(Value, UriKind.Absolute, out var uri) ? uri : null;
    }

    public sealed record Result(
        IReadOnlyList<PackageSource> Sources,
        string GlobalPackagesFolder,
        string? ConfigFilePath)
    {
        public bool HasSources => Sources.Count > 0;

        // Sources whose value is an absolute URI; anything else (relative path, empty) is a
        // local/missing feed we cannot serve over HTTP and is reported separately by the caller.
        public IEnumerable<PackageSource> RemoteSources =>
            Sources.Where(source => source.Uri is not null);
    }

    // `startDirectory` is the project directory. Returns the effective configuration for that
    // project. Never throws: a malformed config degrades to the defaults, matching the
    // best-effort posture of BuildPropsEvaluator.Evaluate.
    public static Result Load(string startDirectory)
    {
        var files = FindConfigFiles(startDirectory);
        var sources = new List<PackageSource>();
        var globalPackagesFolder = string.Empty;
        string? nearest = null;

        foreach (var file in files)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(file);
            }
            catch
            {
                continue;
            }

            var root = document.Root;
            if (root is null) continue;
            if (nearest is null) nearest = file;

            // <packageSources>: the nearest declaration replaces what we have; <clear/> additionally
            // wipes the inherited list.
            var sourcesElement = root.Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals("packageSources", StringComparison.OrdinalIgnoreCase));
            if (sourcesElement is not null)
            {
                // The nearest <packageSources> block wins outright, with or without <clear/>: the SDK
                // replaces rather than merges the inherited list. <clear/> therefore needs no
                // special handling here beyond being recognised as a valid (no-op) child element.
                sources.Clear();

                foreach (var add in sourcesElement.Elements()
                             .Where(element => element.Name.LocalName.Equals("add", StringComparison.OrdinalIgnoreCase)))
                {
                    var key = AttributeValue(add, "key");
                    var value = AttributeValue(add, "value");
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    // Last declaration of a key wins, as in the SDK. A missing key still gets an
                    // entry so the source can be addressed positionally in diagnostics.
                    sources.RemoveAll(existing =>
                        !string.IsNullOrEmpty(key)
                            ? existing.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                            : existing.Key.Length == 0);
                    sources.Add(new PackageSource(key ?? "", value));
                }
            }

            var configElement = root.Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals("config", StringComparison.OrdinalIgnoreCase));
            if (configElement is not null)
            {
                foreach (var add in configElement.Elements()
                             .Where(element => element.Name.LocalName.Equals("add", StringComparison.OrdinalIgnoreCase)))
                {
                    if (AttributeValue(add, "key")?.Equals("globalPackagesFolder", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var value = AttributeValue(add, "value");
                        if (!string.IsNullOrWhiteSpace(value)) globalPackagesFolder = value;
                    }
                }
            }
        }

        // The configured folder is returned RAW. Resolving it requires the project directory
        // (a relative globalPackagesFolder is relative to the project, not the process CWD), and
        // the caller knows the project; resolving here would bake in the wrong base.
        return new Result(
            sources.Count > 0 ? sources : [new PackageSource("nuget.org", DefaultSource)],
            globalPackagesFolder,
            nearest);
    }

    // NUGET_PACKAGES wins over the config file (the SDK honours the env var too), then the
    // configured folder, then ~/.nuget/packages. The env var/config value may be relative to the
    // project, which is why the project root is needed to absolutise it.
    public static string ResolveGlobalPackagesFolder(string configuredFolder, string? projectRootPath = null)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        var candidate = !string.IsNullOrWhiteSpace(fromEnvironment)
            ? fromEnvironment
            : configuredFolder;

        if (string.IsNullOrWhiteSpace(candidate))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        if (!Path.IsPathRooted(candidate) && projectRootPath is not null)
            candidate = Path.GetFullPath(Path.Combine(projectRootPath, candidate));

        return Path.GetFullPath(candidate);
    }

    // Every nuget.config from the outermost ancestor down to the nearest, so callers can apply
    // "nearest wins" by simply iterating the result in order.
    private static List<string> FindConfigFiles(string startDirectory)
    {
        var directories = new List<string>();
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            directories.Add(current.FullName);
            current = current.Parent;
        }
        directories.Reverse();

        var files = new List<string>();
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory)) continue;

            // Enumerate rather than probe "nuget.config" alone: the casing is not standardised and
            // Linux directory enumeration is case-sensitive. At most one config per directory.
            foreach (var candidate in Directory.EnumerateFiles(directory, "*.config"))
            {
                if (Path.GetFileName(candidate).Equals("nuget.config", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(candidate);
                    break;
                }
            }
        }

        return files;
    }

    private static string? AttributeValue(XElement element, string localName) =>
        (string?)element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
}