using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Dotnet10Dependencies;
using Shouldly;
using Xunit;

namespace NativeCompilerTests;

// The payload is the only reason a published sharp works on a machine with no .NET SDK: the
// targeting ref packs, the SDK source generators, the ILLink analyzers, DependencyModel, the apphost
// template, and the shared-framework assembly names all ship inside the binary. Every other test in
// this suite consumes the payload indirectly, by building something with it, so a payload that lost
// half its contents still passes them all - and the first real symptom is a user's build failing with
// CS0246 on a clean machine, or an app that will not start because runtimeconfig named a framework
// major from the wrong directory.
//
// tools/PayloadStager assembles it, so this asserts the layout the runtime actually probes rather
// than re-stating how the stager works: EmbeddedSdkRefs builds every path it resolves from
// packs/<framework>.Ref/<refpack>/..., sdk/<refpack>/..., and frameworks/<framework>.txt, and the
// zip root holds sdk-version.txt.
public class EmbeddedPayloadTests
{
    [Fact]
    public void Embedded_Payload_Contains_Every_Directory_EmbeddedSdkRefs_Probes()
    {
        using var payload = OpenPayload();
        var entries = FileEntries(payload).ToList();

        // Relative, forward-slash paths only. EmbeddedSdkRefs joins them onto the extraction root and
        // the cache is content-addressed by refpack version, so an absolute or backslash path here
        // would escape the cache on Windows and 404 everywhere else.
        entries.ShouldAllBe(entry => !entry.StartsWith('/') && !entry.StartsWith('\\'));
        entries.ShouldAllBe(entry => !entry.Contains("..", StringComparison.Ordinal));
        entries.ShouldAllBe(entry => !entry.Contains('\\'));

        // A partial payload is the failure that hurts: the ref packs are the compiler's entire input.
        entries.Count.ShouldBeGreaterThan(100, "the payload looks truncated");

        var versions = ReadVersions(payload);
        var refPack = versions["refpack"];
        var runtime = versions["runtime"];
        var sdk = versions["sdk"];

        // The ref pack version keys the extraction cache. It has to match the versions baked into the
        // staged paths, or the cache is written once under a version and never found again - and a
        // mismatch between refpack and runtime is what made net10.0 apps ask for a net9.0 framework
        // and die on a missing System.Runtime, Version=10.0.0.0.
        refPack.ShouldBe(runtime);
        SemVer(refPack).ShouldNotBeNullOrEmpty();
        SemVer(sdk).ShouldNotBeNullOrEmpty();

        var netPack = $"packs/Microsoft.NETCore.App.Ref/{refPack}";
        var aspPack = $"packs/Microsoft.AspNetCore.App.Ref/{refPack}";
        var sdkDir = $"sdk/{refPack}";

        var netRef = Names(Under(entries, $"{netPack}/ref/"));
        netRef.ShouldNotBeEmpty("no .NET reference assemblies: every compile would fail");
        netRef.ShouldContain("System.Runtime.dll");

        Under(entries, $"{netPack}/analyzers/dotnet/cs/").ShouldNotBeEmpty("no .NET SDK analyzers");

        var aspRef = Names(Under(entries, $"{aspPack}/ref/"));
        aspRef.ShouldNotBeEmpty("no ASP.NET Core reference assemblies: a web app would fail to compile");
        aspRef.ShouldContain("Microsoft.AspNetCore.Http.Abstractions.dll");

        // Load-bearing for output parity, and each one is a separate way a web app can silently
        // diverge from `dotnet build`: SourceGenerators emits the public partial Program minimal APIs
        // get, Logging emits the [LoggerMessage] callbacks, and the options/validation/config
        // generators shape the emitted surface.
        var aspGenerators = Names(Under(entries, $"{aspPack}/analyzers/dotnet/cs/"));
        foreach (var generator in new[]
                 {
                     "Microsoft.AspNetCore.App.SourceGenerators.dll",
                     "Microsoft.Extensions.Logging.Generators.dll",
                     "Microsoft.Extensions.Options.SourceGeneration.dll",
                     "Microsoft.Extensions.Validation.ValidationsGenerator.dll",
                     "Microsoft.Extensions.Configuration.Binder.SourceGeneration.dll",
                 })
        {
            aspGenerators.ShouldContain(generator);
        }

        Names(Under(entries, $"{sdkDir}/illink-analyzers/"))
            .ShouldContain("ILLink.RoslynAnalyzer.dll");

        entries.ShouldContain($"{sdkDir}/Microsoft.Extensions.DependencyModel.dll");

        // An SDK install ships only the apphost for its own OS, so either name is correct and both
        // are probed at runtime; neither appearing means the payload was staged somewhere else.
        var apphosts = new[] { $"{sdkDir}/AppHostTemplate/apphost", $"{sdkDir}/AppHostTemplate/apphost.exe" };
        apphosts.ShouldContain(entry => entries.Contains(entry), "no apphost template, so no apphost output");

        // Names only, never the 106 MB of framework DLLs. These drive copy-local suppression, and
        // they cannot be derived from the ref packs: the AspNetCore ref pack omits
        // System.Security.Cryptography.Pkcs.dll, which the shared framework does ship.
        var netCoreNames = ReadTextLines(payload, "frameworks/Microsoft.NETCore.App.txt");
        var aspNetNames = ReadTextLines(payload, "frameworks/Microsoft.AspNetCore.App.txt");

        netCoreNames.ShouldNotBeEmpty();
        netCoreNames.ShouldContain("System.Runtime.dll");
        aspNetNames.ShouldNotBeEmpty();
        aspNetNames.ShouldContain("Microsoft.AspNetCore.Http.Abstractions.dll");

        // The whole point of the asymmetry above: the name list is a *different* set from the ref
        // pack. This one assembly ships in the ASP.NET Core shared framework but not in the
        // ASP.NET Core ref pack, so copy-local suppression cannot be derived from the refs. A payload
        // that quietly substituted the ref pack file list would still pass every other assertion here.
        aspNetNames.ShouldContain("System.Security.Cryptography.Pkcs.dll");
        Names(aspRef).ShouldNotContain("System.Security.Cryptography.Pkcs.dll");

        // A staged directory rather than a staged file would extract as an empty file.
        foreach (var entry in payload.Entries.Where(entry => !entry.FullName.EndsWith('/')))
            entry.Length.ShouldBeGreaterThan(0, $"embedded payload entry is empty: {entry.FullName}");
    }

    private static readonly string[] RequiredVersions = ["refpack", "runtime", "sdk"];

    private static Dictionary<string, string> ReadVersions(ZipArchive payload)
    {
        var lines = ReadTextLines(payload, "sdk-version.txt");
        var versions = lines
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

        foreach (var key in RequiredVersions)
            versions.ShouldContainKey(key, $"sdk-version.txt has no '{key}=' line");

        return versions;
    }

    private static List<string> Under(IEnumerable<string> entries, string prefix)
        => entries.Where(entry => entry.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    // Payload paths are absolute within the zip, so assertions about contents ask about file names.
    private static List<string> Names(IEnumerable<string> entries)
        => entries.Select(entry => entry[(entry.LastIndexOf('/') + 1)..]).ToList();

    private static IEnumerable<string> FileEntries(ZipArchive payload)
        => payload.Entries.Select(entry => entry.FullName).Where(name => !name.EndsWith('/'));

    private static List<string> ReadTextLines(ZipArchive payload, string entryName)
    {
        var entry = payload.GetEntry(entryName);
        entry.ShouldNotBeNull($"the payload is missing {entryName}");

        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string SemVer(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static ZipArchive OpenPayload()
    {
        // Read the resource from the assembly that embeds it rather than the staged obj/ tree: the
        // resource is the artifact that ships, and its LogicalName is the contract the runtime looks
        // the zip up by, so a rename would break first use on a clean machine.
        using var stream = Payloads.LoadZip();

        // Copied into memory rather than handed to the archive directly: a manifest stream belongs to
        // the caller, and ZipArchive only reads its central directory lazily, so a stream disposed on
        // return fails on the first Entries access instead of here.
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    [Fact]
    public void Payload_Resource_Exists_Under_The_Name_Payloads_Declares()
    {
        // Payloads.ZipResourceName and the LogicalName in Dotnet10Dependencies.csproj are two
        // hand-written strings that must agree; nothing but this test connects them. A mismatch does
        // not fail the build - it compiles, passes every JIT test, and then throws at runtime on a
        // clean machine with no SDK, which is the one environment sharp exists to support.
        var assembly = typeof(Payloads).Assembly;

        assembly.GetManifestResourceNames().ShouldContain(Payloads.ZipResourceName);
    }
}