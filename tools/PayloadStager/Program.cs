// Stages the compiler payload that Sharp embeds, so a published sharp works on a machine
// with no .NET SDK installed.
//
// sharp needs compile-time metadata (the targeting reference packs), the SDK source generators,
// the ILLink analyzers, the apphost template, and the *names* of the shared-framework assemblies.
// It used to read all of that from the installed SDK, which made the published binary useless
// exactly where it is meant to be useful, and made the payload differ from machine to machine. This
// tool instead copies those files out of pinned Microsoft-published NuGet packages, writes them into
// an SDK-shaped tree under obj/payload, adds the two name lists plus a version stamp, and zips the
// result. EmbeddedSdkRefs.cs extracts that zip to a version-keyed cache on first use.
//
// The output layout still mirrors an SDK install - AsemblyExtensions probes those paths and only its
// "where is the root" answer changes - but the *sources* are packages, so the same `dotnet build`
// produces the same payload anywhere.
//
// It takes no arguments. MSBuild runs it with the working directory set to Dotnet10Dependencies, so
// it reads that project's own restore output, obj/project.assets.json, to learn the NuGet
// global-packages folder and which packs were downloaded, and derives everything else - the
// reference-pack version, the RID-qualified pack folders, the SDK version, the target framework -
// from that file and the environment. Keeping the decisions here rather than in the csproj is what
// lets Dotnet10Dependencies.csproj stay a list of packages plus one Exec, with none of the path
// plumbing that used to mirror all of this as a dozen quoted command-line arguments.
//
// This is deliberately C# rather than an MSBuild target: the staging is real logic with real
// failure modes (an empty glob, a directory that silently holds the wrong version), and MSBuild
// expresses both badly - item-metadata batching produced a cartesian product of paths once, and a
// '*' over a versioned directory picked whichever version sorted first on a machine with several
// installed. Here both are ordinary code with ordinary error messages.

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

var projectDirectory = Environment.CurrentDirectory;
var payloadRoot = Path.Combine(projectDirectory, "obj", "payload");

var assets = NuGetAssets.Load(Path.Combine(projectDirectory, "obj", "project.assets.json"));

var refPackVersion = assets.DownloadVersion("Microsoft.NETCore.App.Ref");
var targetFramework = "net" + Environment.Version.Major + ".0";

// The zip must not sit inside the tree it archives, so stage the tree in a subdirectory and write the
// zip beside it. MSBuild embeds the zip by glob and never needs the tree.
var payloadDir = Path.Combine(payloadRoot, "embedded-refs");
var zipPath = Path.Combine(payloadRoot, $"embedded-refs.{refPackVersion}.zip");

if (Directory.Exists(payloadRoot))
    Directory.Delete(payloadRoot, recursive: true);

Directory.CreateDirectory(payloadDir);

// The layout mirrors the SDK's own, because AsemblyExtensions probes those paths and only its
// "where is the root" answer changes. Keep in step with the path properties in EmbeddedSdkRefs.
var netPack = Path.Combine("packs", "Microsoft.NETCore.App.Ref", refPackVersion);
var aspPack = Path.Combine("packs", "Microsoft.AspNetCore.App.Ref", refPackVersion);
var sdkDir = Path.Combine("sdk", refPackVersion);

CopyAssemblies(
    Path.Combine(assets.Download("Microsoft.NETCore.App.Ref"), "ref", targetFramework),
    Path.Combine(payloadDir, netPack, "ref", targetFramework),
    required: true,
    what: "the .NET reference assemblies");

CopyAssemblies(
    Path.Combine(assets.Download("Microsoft.NETCore.App.Ref"), "analyzers", "dotnet", "cs"),
    Path.Combine(payloadDir, netPack, "analyzers", "dotnet", "cs"),
    required: true,
    what: "the .NET SDK analyzers");

CopyAssemblies(
    Path.Combine(assets.Download("Microsoft.AspNetCore.App.Ref"), "ref", targetFramework),
    Path.Combine(payloadDir, aspPack, "ref", targetFramework),
    required: true,
    what: "the ASP.NET Core reference assemblies");

// Load-bearing for output parity, not merely nice to have: Microsoft.AspNetCore.App.SourceGenerators
// emits the `public partial class Program` minimal-API apps get from the SDK, and the
// Microsoft.Extensions.* generators shape the emitted surface. Without them a sharp-built web app
// diverges from `dotnet build` and [LoggerMessage] fails with CS8795.
CopyAssemblies(
    Path.Combine(assets.Download("Microsoft.AspNetCore.App.Ref"), "analyzers", "dotnet", "cs"),
    Path.Combine(payloadDir, aspPack, "analyzers", "dotnet", "cs"),
    required: true,
    what: "the ASP.NET Core SDK generators");

CopyAssemblies(
    Path.Combine(assets.Library("Microsoft.NET.ILLink.Tasks"), "analyzers", "dotnet", "cs"),
    Path.Combine(payloadDir, sdkDir, "illink-analyzers"),
    required: true,
    what: "the ILLink analyzers");

// The Razor source generators (Microsoft.CodeAnalysis.Razor.Compiler.dll and its shared utilities)
// compile .cshtml/.razor files. They ship only inside the SDK's Microsoft.NET.Sdk.Razor component,
// not in any reference pack or NuGet package, so they are copied from the installed SDK that is
// running this staging step. Not required: a payload staged without an SDK present simply has no
// Razor support, and sharp degrades to leaving .cshtml/.razor files uncompiled.
var razorSourceGenerators = FindRazorSourceGenerators();
if (razorSourceGenerators is not null)
    CopyAssemblies(
        razorSourceGenerators,
        Path.Combine(payloadDir, sdkDir, "razor-source-generators"),
        required: false,
        what: "the Razor source generators");

// DependencyModel ships only in the SDK, not in a ref pack or the shared framework. sharp loads it to
// read .deps.json, so a payload without it cannot run.
CopyFile(
    Path.Combine(assets.Library("Microsoft.Extensions.DependencyModel"), "lib", targetFramework, "Microsoft.Extensions.DependencyModel.dll"),
    Path.Combine(payloadDir, sdkDir, "Microsoft.Extensions.DependencyModel.dll"),
    what: "Microsoft.Extensions.DependencyModel.dll");

// The host pack carries the apphost for one RID under runtimes/<rid>/native/. Not required: the
// runtime degrades to a dll-only layout.
foreach (var apphost in new[] { "apphost", "apphost.exe" })
{
    var source = FindAppHost(assets.Download("Microsoft.NETCore.App.Host"), apphost);
    if (source is not null)
        CopyFile(source, Path.Combine(payloadDir, sdkDir, "AppHostTemplate", apphost), what: $"the {apphost} template");
}

// Shared-framework assembly NAMES, never their bytes: the 106 MB of framework DLLs are never
// embedded, only the names, which sharp uses to decide which references belong to the framework and
// must not be copied into bin/. Derived from the runtime packs, not the ref packs - the AspNetCore
// ref pack omits System.Security.Cryptography.Pkcs.dll, which the shared framework does ship.
WriteFrameworkNames(
    assets.Download("Microsoft.NETCore.App.Runtime"),
    Path.Combine(payloadDir, "frameworks", "Microsoft.NETCore.App.txt"),
    what: "the Microsoft.NETCore.App assembly names");

WriteFrameworkNames(
    assets.Download("Microsoft.AspNetCore.App.Runtime"),
    Path.Combine(payloadDir, "frameworks", "Microsoft.AspNetCore.App.txt"),
    what: "the Microsoft.AspNetCore.App assembly names");

// The versions, as key=value lines the runtime reads after extraction. refpack keys the extraction
// cache so it can never serve reference assemblies from a different version; sdk is what
// `sharp --version` reports. The runtime tracks the ref pack: they ship together (same Microsoft
// patch), and a mismatch is a build-environment problem worth surfacing rather than papering over.
File.WriteAllLines(
    Path.Combine(payloadDir, "sdk-version.txt"),
    [
        $"refpack={refPackVersion}",
        $"runtime={refPackVersion}",
        $"sdk={ResolveSdkVersion()}",
    ]);

if (File.Exists(zipPath))
    File.Delete(zipPath);

ZipFile.CreateFromDirectory(payloadDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

Console.WriteLine(
    $"Staged compiler payload: {CountFiles(payloadDir)} files, " +
    $"{new FileInfo(zipPath).Length / 1024} KB zipped -> {zipPath}");

return 0;

// The SDK version is stamped so `sharp --version` can report it with no SDK installed, and it must
// match the machine's installed dotnet. MSBuild exports the SDK's Sdks folder path to every Exec
// child, and its parent directory is named after the SDK version; running the tool by hand, with no
// MSBuild in the loop, falls back to the CLI.
string ResolveSdkVersion()
{
    var sdksPath = Environment.GetEnvironmentVariable("MSBuildSDKsPath");

    if (!string.IsNullOrEmpty(sdksPath))
    {
        var version = Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sdksPath)));
        if (!string.IsNullOrEmpty(version)) return version;
    }

    using var dotnet = Process.Start(new ProcessStartInfo("dotnet", "--version")
    {
        RedirectStandardOutput = true,
    });

    if (dotnet is null)
        throw new InvalidOperationException("Could not determine the .NET SDK version: 'dotnet' was not found.");

    var cliVersion = dotnet.StandardOutput.ReadToEnd().Trim();
    dotnet.WaitForExit();

    if (dotnet.ExitCode != 0 || cliVersion.Length == 0)
        throw new InvalidOperationException("Could not determine the .NET SDK version: 'dotnet --version' failed.");

    return cliVersion;
}

// Verifies a directory holds something before copying, because an empty glob used to stage silently:
// the compiler then reported a missing reference assembly at build time, with no hint that the
// payload had been built empty.
void CopyAssemblies(string sourceDir, string destinationDir, bool required, string what)
{
    if (!Directory.Exists(sourceDir))
    {
        if (!required)
            return;

        throw new InvalidOperationException(
            $"Cannot stage {what}: {sourceDir} does not exist. sharp cannot build projects without it.");
    }

    var sources = Directory.GetFiles(sourceDir, "*.dll");
    if (sources.Length == 0)
    {
        if (!required)
            return;

        throw new InvalidOperationException($"Cannot stage {what}: {sourceDir} contains no assemblies.");
    }

    Directory.CreateDirectory(destinationDir);
    foreach (var source in sources)
        File.Copy(source, Path.Combine(destinationDir, Path.GetFileName(source)), overwrite: true);
}

void CopyFile(string source, string destination, string what)
{
    if (!File.Exists(source))
        throw new InvalidOperationException($"Cannot stage {what}: {source} does not exist.");

    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.Copy(source, destination, overwrite: true);
}

// The host pack lays the apphost out as runtimes/<rid>/native/<name>; a host pack pins a single RID,
// so there is at most one match.
string? FindAppHost(string hostPack, string apphost)
{
    if (!Directory.Exists(hostPack))
        return null;

    return Directory.EnumerateFiles(hostPack, apphost, SearchOption.AllDirectories).FirstOrDefault();
}

// The Razor SDK component lives beside the SDK's other MSBuild SDKs (MSBuildSDKsPath). Its
// source-generators folder holds Microsoft.CodeAnalysis.Razor.Compiler.dll. Returns null when no SDK
// is present (running the stager by hand) so the payload can still be produced without Razor support.
string? FindRazorSourceGenerators()
{
    var sdksPath = Environment.GetEnvironmentVariable("MSBuildSDKsPath");
    if (string.IsNullOrEmpty(sdksPath)) return null;

    var candidate = Path.Combine(sdksPath, "Microsoft.NET.Sdk.Razor", "source-generators");
    return Directory.Exists(candidate) ? candidate : null;
}

void WriteFrameworkNames(string runtimePack, string destinationPath, string what)
{
    if (!Directory.Exists(runtimePack))
        throw new InvalidOperationException(
            $"Cannot stage {what}: {runtimePack} does not exist. sharp cannot build projects without it.");

    // A runtime pack lays the shared-framework assemblies out as runtimes/<rid>/lib/<tfm>. Union across
    // any RIDs present (a pack normally pins one) so the list is never empty.
    var names = Directory.EnumerateDirectories(Path.Combine(runtimePack, "runtimes"), "*", SearchOption.TopDirectoryOnly)
        .Select(rid => Path.Combine(rid, "lib", targetFramework))
        .Where(Directory.Exists)
        .SelectMany(lib => Directory.GetFiles(lib, "*.dll"))
        .Select(Path.GetFileName)
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();

    if (names.Count == 0)
        throw new InvalidOperationException(
            $"Cannot stage {what}: {runtimePack} contains no {targetFramework} assemblies under runtimes/*/lib.");

    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
    File.WriteAllLines(destinationPath, names);
}

int CountFiles(string directory)
    => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length;

// What the project restored, read back from obj/project.assets.json. The DotnetPlatform packs are
// PackageDownload items, so restore records them as "downloadDependencies" (name plus a [min, max]
// version range) rather than in "libraries", which holds the ordinary packages (DependencyModel,
// ILLink) keyed as "Id/Version" with a lower-cased relative path.
file sealed class NuGetAssets
{
    private readonly string _packageRoot;
    private readonly Dictionary<string, string> _downloads;   // package id -> version
    private readonly Dictionary<string, string> _libraries;   // "Id/Version" -> relative path

    private NuGetAssets(string packageRoot, Dictionary<string, string> downloads, Dictionary<string, string> libraries)
    {
        _packageRoot = packageRoot;
        _downloads = downloads;
        _libraries = libraries;
    }

    public static NuGetAssets Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Cannot stage the compiler payload: {path} does not exist. Restore the project before building it.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var packageRoot = root.GetProperty("packageFolders").EnumerateObject().First().Name;
        var downloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var libraries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in FindDownloadDependencies(root))
            downloads[entry.GetProperty("name").GetString()!] =
                LowerBound(entry.GetProperty("version").GetString()!);

        foreach (var library in root.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.TryGetProperty("type", out var type) && type.GetString() == "package")
                libraries[library.Name] = library.Value.GetProperty("path").GetString()!;
        }

        return new NuGetAssets(packageRoot, downloads, libraries);
    }

    /// <summary>Folder of a downloaded DotnetPlatform pack, matched by id or by id prefix (the RID packs).</summary>
    public string Download(string id)
    {
        var name = MatchDownload(id);
        return Path.Combine(_packageRoot, name.ToLowerInvariant(), _downloads[name]);
    }

    /// <summary>Version of a downloaded DotnetPlatform pack.</summary>
    public string DownloadVersion(string id) => _downloads[MatchDownload(id)];

    /// <summary>Folder of an ordinary package, e.g. Microsoft.Extensions.DependencyModel or ILLink.</summary>
    public string Library(string id)
    {
        foreach (var name in _libraries.Keys)
            if (name.StartsWith(id + "/", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(_packageRoot, _libraries[name]);

        throw new InvalidOperationException(
            $"The restore output lists no package named '{id}'. Expected one of: " +
            string.Join(", ", _libraries.Keys.Order()));
    }

    private string MatchDownload(string id)
    {
        foreach (var name in _downloads.Keys)
            if (name.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase))
                return name;

        throw new InvalidOperationException(
            $"The restore output lists no downloaded package named '{id}'. Expected one of: " +
            string.Join(", ", _downloads.Keys.Order()));
    }

    // downloadDependencies record a version range like "[10.0.11, 10.0.11]"; the lower bound is the
    // version restore actually used.
    private static string LowerBound(string range) => range.Trim('[', ']').Split(',')[0].Trim();

    // The packs land under project.frameworks.<tfm>.downloadDependencies in the SDK's restore output;
    // earlier layouts put them under targets.<tfm>. Search instead of hardcoding a path so neither
    // layout can silently stage an empty payload.
    private static IEnumerable<JsonElement> FindDownloadDependencies(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "downloadDependencies")
                    {
                        foreach (var entry in property.Value.EnumerateArray())
                            yield return entry;
                    }
                    else
                    {
                        foreach (var nested in FindDownloadDependencies(property.Value))
                            yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var nested in FindDownloadDependencies(item))
                        yield return nested;

                break;
        }
    }
}
