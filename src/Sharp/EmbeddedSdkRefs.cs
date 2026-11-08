using System.IO.Compression;
using System.Security.Cryptography;
using Dotnet10Dependencies;

namespace Sharp;

/// <summary>
/// The compile-time payload sharp carries so it can build projects on a machine with no .NET SDK
/// installed. The payload is staged at build time by tools/PayloadStager and embedded as the
/// <c>embedded-refs.&lt;refpack&gt;.zip</c> manifest resource of the Dotnet10Dependencies assembly,
/// which is read here through <see cref="Payloads"/>.
///
/// It contains only what compiling actually needs:
/// <list type="bullet">
///   <item>reference (ref pack) assemblies for compiling, never the shared runtime implementations;</item>
///   <item>the targeting-pack source generators and the ILLink analyzers;</item>
///   <item>the apphost template, so output projects get a native launcher;</item>
///   <item>Microsoft.Extensions.DependencyModel.dll, used for reading .deps.json;</item>
///   <item>the shared-framework assembly *names* and the runtime version, which the compiler needs for
///         reference filtering and for stamping runtimeconfig.json.</item>
/// </list>
/// The shared framework itself (~106 MB) is deliberately not embedded: sharp never loads those
/// assemblies, it only needs to know which references are framework-provided so it does not copy them
/// into the output directory.
///
/// The zip is extracted once into a cache directory and reused afterwards, so steady-state builds pay
/// no extraction cost. The cache is keyed by a hash of the payload, so upgrading sharp can never
/// pick up a stale extraction from a previous version.
/// </summary>
public static class EmbeddedSdkRefs
{
    // The payload's name is versioned (embedded-refs.<refpack>.zip) and discovered at run time, never
    // hardcoded - the assembly that owns it derives the name from what was actually embedded.

    // A completed extraction is marked with a file containing the payload hash. Until that file
    // exists the directory is treated as incomplete, so an interrupted extraction is retried rather
    // than being mistaken for a usable cache.
    private const string ExtractionMarkerName = ".extracted";

    private static readonly Lock SyncRoot = new();

    private static string? _root;
    private static string? _runtimeVersion;
    private static string? _refPackVersion;
    private static string? _sdkVersion;

    /// <summary>
    /// Root of the extracted SDK-shaped tree, i.e. the directory that plays the role of the .NET SDK
    /// install directory (containing <c>packs/</c> and <c>sdk/</c>). Extraction happens on first use.
    /// </summary>
    public static string Root
    {
        get
        {
            if (_root is not null) return _root;

            lock (SyncRoot)
            {
                return _root ??= EnsureExtracted();
            }
        }
    }

    /// <summary>
    /// Version of the shared framework sharp targets, e.g. <c>10.0.11</c>. This is written verbatim
    /// into every produced runtimeconfig.json.
    /// </summary>
    public static string RuntimeVersion => _runtimeVersion ??= ReadVersion("runtime");

    /// <summary>
    /// Version of the reference packs that were embedded, e.g. <c>10.0.11</c>. This is a patch version
    /// shared by the ref packs and the runtime, not the SDK's own version (see <see cref="SdkVersion"/>).
    /// </summary>
    public static string RefPackVersion => _refPackVersion ??= ReadVersion("refpack");

    /// <summary>
    /// The .NET SDK version sharp was built against, e.g. <c>10.0.400</c>. Reported by
    /// <c>sharp --version</c>, matching what <c>dotnet --version</c> prints for that SDK. Stamped
    /// into the payload at build time so it is available with no SDK installed.
    /// </summary>
    public static string SdkVersion => _sdkVersion ??= ReadVersion("sdk");

    /// <summary>Directory holding the reference assemblies for compiling against .NET itself.</summary>
    public static string ReferenceAssemblies => Path.Combine(
        Root, "packs", "Microsoft.NETCore.App.Ref", RefPackVersion, "ref", FrameworkName);

    /// <summary>Directory holding the targeting-pack source generators shipped in the ref pack.</summary>
    public static string RefPackAnalyzers => Path.Combine(
        Root, "packs", "Microsoft.NETCore.App.Ref", RefPackVersion, "analyzers", "dotnet", "cs");

    /// <summary>Directory holding the ILLink analyzers, which sharp copies into trimmed output.</summary>
    public static string IllinkAnalyzers => Path.Combine(Root, "sdk", RefPackVersion, "illink-analyzers");

    /// <summary>
    /// Directory holding the ASP.NET Core targeting-pack generators (minimal-API public Program,
    /// [LoggerMessage], options/validation/config-binding, RequestDelegateGenerator).
    /// </summary>
    public static string AspNetRefPackAnalyzers => Path.Combine(
        Root, "packs", "Microsoft.AspNetCore.App.Ref", RefPackVersion, "analyzers", "dotnet", "cs");

    /// <summary>Directory holding the SDK assemblies sharp loads directly (DependencyModel).</summary>
    public static string SdkDirectory => Path.Combine(Root, "sdk", RefPackVersion);

    /// <summary>Directory holding the apphost template used to make output projects executable.</summary>
    public static string AppHostTemplate => Path.Combine(SdkDirectory, "AppHostTemplate");

    /// <summary>
    /// Directory holding the Razor source generators (the MVC view / component compiler). Empty on a
    /// payload staged without a .NET SDK present, in which case Razor projects fall back to no view
    /// compilation. Staged from the installed SDK because the Razor compiler ships only as part of the
    /// Microsoft.NET.Sdk.Razor SDK component, not in any reference pack.
    /// </summary>
    public static string RazorSourceGenerators => Path.Combine(SdkDirectory, "razor-source-generators");

    private static string FrameworkName => "net" + Environment.Version.Major + ".0";

    /// <summary>
    /// Assembly names that belong to a shared framework, so they must not be copied into the output
    /// directory (they are provided by the runtime at execution time).
    /// </summary>
    public static IReadOnlySet<string> GetSharedFrameworkAssemblyNames(string frameworkName)
    {
        var path = Path.Combine(Root, "frameworks", frameworkName + ".txt");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The embedded payload is missing the '{frameworkName}' assembly name list. sharp cannot " +
                "determine which references are framework-provided without it.", path);
        }

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reference assemblies for a shared-framework reference such as <c>Microsoft.AspNetCore.App</c>,
    /// or <c>null</c> when that framework is not part of the embedded payload.
    /// </summary>
    public static string? ResolveFrameworkReferenceAssemblies(string frameworkName)
    {
        if (!frameworkName.EndsWith(".Ref", StringComparison.OrdinalIgnoreCase))
        {
            frameworkName += ".Ref";
        }

        var candidate = Path.Combine(Root, "packs", frameworkName, RefPackVersion, "ref", FrameworkName);

        return Directory.Exists(candidate) ? candidate : null;
    }

    private static string ReadVersion(string key)
    {
        var path = Path.Combine(Root, "sdk-version.txt");

        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (!line.StartsWith(key + "=", StringComparison.Ordinal)) continue;

                var value = line[(key.Length + 1)..].Trim();
                if (value.Length > 0) return value;
            }
        }

        throw new InvalidOperationException(
            $"The embedded payload does not declare a '{key}' version. sharp cannot stamp a runtimeconfig.json without it.");
    }

    private static string EnsureExtracted()
    {
        var payload = ReadEmbeddedPayload();

        // Key the cache on the payload content so a sharp upgrade always extracts to a fresh directory
        // and can never be served a partial or older extraction.
        var cacheKey = Convert.ToHexStringLower(SHA256.HashData(payload))[..16];
        var cacheDirectory = Path.Combine(CacheRoot, cacheKey);
        var marker = Path.Combine(cacheDirectory, ExtractionMarkerName);

        if (!File.Exists(marker))
        {
            lock (SyncRoot)
            {
                if (!File.Exists(marker))
                {
                    // Extract to a uniquely named sibling first, then move into place. Moving within a
                    // directory is atomic, so a crash or a concurrent sharp process can never leave a
                    // half-extracted tree visible under the cache key.
                    var staging = cacheDirectory + "." + Environment.ProcessId + ".tmp";
                    TryDeleteDirectory(staging);

                    try
                    {
                        Directory.CreateDirectory(staging);
                        Extract(payload, staging);
                        File.WriteAllText(Path.Combine(staging, ExtractionMarkerName), cacheKey);

                        Directory.CreateDirectory(Path.GetDirectoryName(cacheDirectory)!);
                        Directory.Move(staging, cacheDirectory);
                    }
                    catch
                    {
                        TryDeleteDirectory(staging);
                        throw;
                    }
                }
            }
        }

        return cacheDirectory;
    }

    private static byte[] ReadEmbeddedPayload()
    {
        using var stream = Payloads.LoadZip();

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void Extract(byte[] payload, string destination)
    {
        using var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read);

        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            // Entries are written by our own build, but treat the archive as untrusted anyway: a path
            // escaping the destination (../, absolute root, or a Windows drive-relative path) would let
            // a malicious payload write outside the cache.
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));

            if (!target.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Refusing to extract '{entry.FullName}': the entry resolves outside the cache directory.");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string CacheRoot
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("SHARP_CACHE_HOME");

            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.Combine(configured, "ref");
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetEnvironmentVariable("HOME");

                if (string.IsNullOrWhiteSpace(home))
                {
                    throw new InvalidOperationException(
                        "Could not determine a cache directory for the embedded SDK references. Set SHARP_CACHE_HOME.");
                }
            }

            return Path.Combine(home, ".cache", "sharp");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover staging directory is harmless; it is never read and is overwritten by the
            // next extraction attempt using the same process id.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}