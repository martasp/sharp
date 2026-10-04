// Stages the compiler payload that NativeCompiler embeds, so a published sharp works on a machine
// with no .NET SDK installed.
//
// `sharp` needs compile-time metadata (the targeting reference packs), the SDK source generators,
// the ILLink analyzers, the apphost template, and the *names* of the shared-framework assemblies.
// It used to read all of that from the installed SDK, which made the published binary useless
// exactly where it is meant to be useful. This tool copies those files into an SDK-shaped tree under
// obj/, writes the two name lists plus a version stamp, and zips the result. EmbeddedSdkRefs.cs
// extracts that zip to a version-keyed cache on first use.
//
// This is deliberately C# rather than an MSBuild target: the staging is real logic with real
// failure modes (an empty glob, a directory that silently holds the wrong version), and MSBuild
// expresses both badly - item-metadata batching produced a cartesian product of paths once, and a
// '*' over the shared-framework directory picked whichever version sorted first on a machine with
// several installed. Here both are ordinary code with ordinary error messages.

using System.IO.Compression;

var options = Options.Parse(args);

var refPackVersion = options.RefPackVersion;
var targetFramework = options.TargetFramework;
var payloadDir = options.OutDir;
var sdkDir = Path.Combine(payloadDir, "sdk", refPackVersion);

if (Directory.Exists(payloadDir))
    Directory.Delete(payloadDir, recursive: true);

Directory.CreateDirectory(payloadDir);

// The layout mirrors the SDK's own, because AsemblyExtensions probes those paths and only its
// "where is the root" answer changes. Keep in step with the path properties in EmbeddedSdkRefs.
var netPack = Path.Combine("packs", "Microsoft.NETCore.App.Ref", refPackVersion);
var aspPack = Path.Combine("packs", "Microsoft.AspNetCore.App.Ref", refPackVersion);

CopyAssemblies(
    Path.Combine(options.TargetingPackRoot, "Microsoft.NETCore.App.Ref", refPackVersion, "ref", targetFramework),
    Path.Combine(payloadDir, netPack, "ref", targetFramework),
    required: true,
    what: "the .NET reference assemblies");

CopyAssemblies(
    Path.Combine(options.TargetingPackRoot, "Microsoft.NETCore.App.Ref", refPackVersion, "analyzers", "dotnet", "cs"),
    Path.Combine(payloadDir, netPack, "analyzers", "dotnet", "cs"),
    required: true,
    what: "the .NET SDK analyzers");

CopyAssemblies(
    Path.Combine(options.TargetingPackRoot, "Microsoft.AspNetCore.App.Ref", refPackVersion, "ref", targetFramework),
    Path.Combine(payloadDir, aspPack, "ref", targetFramework),
    required: true,
    what: "the ASP.NET Core reference assemblies");

// Load-bearing for output parity, not merely nice to have: Microsoft.AspNetCore.App.SourceGenerators
// emits the `public partial class Program` minimal-API apps get from the SDK, and the
// Microsoft.Extensions.* generators shape the emitted surface. Without them a sharp-built web app
// diverges from `dotnet build` and [LoggerMessage] fails with CS8795.
CopyAssemblies(
    Path.Combine(options.TargetingPackRoot, "Microsoft.AspNetCore.App.Ref", refPackVersion, "analyzers", "dotnet", "cs"),
    Path.Combine(payloadDir, aspPack, "analyzers", "dotnet", "cs"),
    required: true,
    what: "the ASP.NET Core SDK generators");

CopyAssemblies(
    options.IllinkAnalyzersDirectory,
    Path.Combine(sdkDir, "illink-analyzers"),
    required: true,
    what: "the ILLink analyzers");

// DependencyModel ships only in the SDK, not in a ref pack or the shared framework. sharp loads it to
// read .deps.json, so a payload without it cannot run.
CopyFile(
    Path.Combine(options.SdkRoot, "Microsoft.Extensions.DependencyModel.dll"),
    Path.Combine(sdkDir, "Microsoft.Extensions.DependencyModel.dll"),
    what: "Microsoft.Extensions.DependencyModel.dll");

// An SDK install ships only the apphost for its own OS, so stage whichever exists. The runtime probes
// both names for the same reason. Not required: the runtime degrades to a dll-only layout.
foreach (var apphost in new[] { "apphost", "apphost.exe" })
{
    var source = Path.Combine(options.SdkRoot, "AppHostTemplate", apphost);
    if (File.Exists(source))
        CopyFile(source, Path.Combine(sdkDir, "AppHostTemplate", apphost), what: $"the {apphost} template");
}

// Shared-framework assembly NAMES, never their bytes: the 106 MB of framework DLLs are never
// embedded, only the names, which sharp uses to decide which references belong to the framework and
// must not be copied into bin/. Deriving the names from the ref packs is not equivalent - the
// AspNetCore ref pack omits System.Security.Cryptography.Pkcs.dll, which the shared framework does
// ship - so the real lists are shipped.
//
// Pinned to the pack version rather than globbed: a machine can have several framework majors side
// by side, and matching any of them picked whichever came first - a net9.0 framework for a net10.0
// build, which produced apps that could not start.
WriteFrameworkNames(
    Path.Combine(options.SharedFrameworkRoot, "Microsoft.NETCore.App", refPackVersion),
    Path.Combine(payloadDir, "frameworks", "Microsoft.NETCore.App.txt"),
    what: "the Microsoft.NETCore.App assembly names");

WriteFrameworkNames(
    Path.Combine(options.SharedFrameworkRoot, "Microsoft.AspNetCore.App", refPackVersion),
    Path.Combine(payloadDir, "frameworks", "Microsoft.AspNetCore.App.txt"),
    what: "the Microsoft.AspNetCore.App assembly names");

// The versions, as key=value lines the runtime reads after extraction. refpack keys the extraction
// cache so it can never serve reference assemblies from a different SDK; sdk is what `sharp --version`
// reports, recorded here rather than read from the assembly so it stays the SDK that built sharp.
File.WriteAllLines(
    Path.Combine(payloadDir, "sdk-version.txt"),
    [
        $"refpack={refPackVersion}",
        $"runtime={options.RuntimeVersion}",
        $"sdk={options.SdkVersion}",
    ]);

if (File.Exists(options.ZipPath))
    File.Delete(options.ZipPath);

ZipFile.CreateFromDirectory(payloadDir, options.ZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

Console.WriteLine(
    $"Staged compiler payload: {CountFiles(payloadDir)} files, " +
    $"{new FileInfo(options.ZipPath).Length / 1024} KB zipped -> {options.ZipPath}");

return 0;

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

void WriteFrameworkNames(string sourceDir, string destinationPath, string what)
{
    if (!Directory.Exists(sourceDir))
        throw new InvalidOperationException(
            $"Cannot stage {what}: {sourceDir} does not exist. If this SDK's shared framework is a " +
            $"different version than its bundled pack, pass --runtime-version explicitly.");

    var names = Directory.GetFiles(sourceDir, "*.dll")
        .Select(Path.GetFileName)
        .OfType<string>()
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();

    if (names.Count == 0)
        throw new InvalidOperationException($"Cannot stage {what}: {sourceDir} contains no assemblies.");

    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
    File.WriteAllLines(destinationPath, names);
}

int CountFiles(string directory)
    => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length;

file sealed class Options
{
    public required string TargetingPackRoot { get; init; }
    public required string SdkRoot { get; init; }
    public required string SharedFrameworkRoot { get; init; }
    public required string IllinkAnalyzersDirectory { get; init; }
    public required string RefPackVersion { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string SdkVersion { get; init; }
    public required string TargetFramework { get; init; }
    public required string OutDir { get; init; }
    public required string ZipPath { get; init; }

    public static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{args[i]}'. Expected --name <value>.");

            if (i + 1 >= args.Length)
                throw new ArgumentException($"Option '{args[i]}' has no value.");

            values[args[i][2..]] = args[++i];
        }

        string Required(string name) =>
            values.TryGetValue(name, out var value) && value.Length > 0
                ? value
                : throw new ArgumentException($"Missing required option --{name}.");

        var refPackVersion = Required("ref-pack-version");

        return new Options
        {
            TargetingPackRoot = Required("targeting-pack-root"),
            SdkRoot = Required("sdk-root"),
            SharedFrameworkRoot = Required("shared-framework-root"),
            IllinkAnalyzersDirectory = Required("illink-analyzers"),
            RefPackVersion = refPackVersion,

            // The runtime version tracks the bundled pack unless told otherwise: they ship together,
            // and a mismatch is a build-environment problem worth surfacing rather than papering over.
            RuntimeVersion = values.TryGetValue("runtime-version", out var runtime) && runtime.Length > 0
                ? runtime
                : refPackVersion,

            SdkVersion = Required("sdk-version"),
            TargetFramework = Required("target-framework"),
            OutDir = Path.GetFullPath(Required("out")),
            ZipPath = Path.GetFullPath(Required("zip")),
        };
    }
}