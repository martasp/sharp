using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Shouldly;
using Xunit;

namespace NativeCompilerTests;

// DELETABLE FEATURE: output parity with `dotnet build`.
//
// Proves that compiling a plain project with NativeCompiler produces an output that "looks the
// same" as the SDK's, with exactly one tiny difference: the output root folder. The SDK writes
// bin\Debug\<TFM>\<name>.dll, we write myc\Debug\<TFM>\<name>.dll - same relative layout, but the
// "myc" root is kept so our output never clobbers real `dotnet build` output during development.
// Everything else matches: identity attributes (Company/Product/Title default to the assembly name,
// versioning mirrors $(Version)/$(AssemblyVersion) defaults, informational version carries
// SourceRevisionId when the props provide one), referenced-assembly set and public type surface.
// A diffoscope report over gzipped tarballs of each build's app-artifact subset (the files the
// project itself produced: dll, pdb, runtimeconfig.json, plus the SDK-only apphost/.deps.json from
// the SDK side) is written for review when diffoscope is installed. The tarball form makes
// diffoscope recurse the whole subset - so pdb path/GUID, .rsrc VERSIONINFO, apphost, and
// .deps.json differences all show up next to the dll's - instead of just one assembly. Byte
// equality is still NOT expected (PE timestamps, embedded PDB path, .rsrc VERSIONINFO/manifest,
// MVID all differ by construction; our build also copies the shared framework into its tree and
// emits no apphost/.deps.json by design).
//
// To DELETE: remove this file and, if nothing else depends on them, the identity/version mapping in
// VersionInfo.g.cs (NativeCompilerUtils) and BuildPropsEvaluator.EvaluateIdentity. GetSharpBinDirectory's
// "myc" root is the long-standing default and stays.

[Collection("Sequential Tests 4")]
public class OutputParityTests
{
    private static string TempRoot(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string[] RefResolverPaths()
    {
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var paths = new List<string>();
        // MetadataLoadContext needs the real runtime assemblies (System.Private.CoreLib) and the
        // AspNetCore shared framework for web apps' references.
        var sharedRoot = Path.GetFullPath(Path.Combine(runtimeDir, "..", ".."));
        if (Directory.Exists(sharedRoot))
            foreach (var framework in Directory.EnumerateDirectories(sharedRoot))
                foreach (var version in Directory.EnumerateDirectories(framework))
                    paths.AddRange(Directory.EnumerateFiles(version, "*.dll"));

        var dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
        foreach (var root in new[]
        {
            Path.Combine(dotnetRoot, "packs"),
            "/usr/share/dotnet/packs",
            "/usr/lib/dotnet/packs",
            Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files\dotnet", "packs")
        })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var pack in Directory.EnumerateDirectories(root))
                foreach (var version in Directory.EnumerateDirectories(pack))
                {
                    var refDir = Path.Combine(version, "ref", "net10.0");
                    if (Directory.Exists(refDir))
                        paths.AddRange(Directory.EnumerateFiles(refDir, "*.dll"));
                }
        }
        return paths.ToArray();
    }

    public static IEnumerable<object[]> Projects()
    {
        yield return new object[] { "console", "ParityConsole.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>\n",
            "Program.cs",
            "Console.WriteLine(\"Hello from ParityConsole!\");\nConsole.WriteLine($\"two plus two is {2 + 2}\");\n" };
        yield return new object[] { "webapi", "ParityWebApi.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>\n",
            "Program.cs",
            "var builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();\napp.MapGet(\"/\", () => \"Hello from ParityWebApi\");\napp.MapGet(\"/sum/{a:int}/{b:int}\", (int a, int b) => a + b);\napp.Run();\n" };
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Output_Path_And_Assembly_Identity_Parity(string kind, string csprojName, string csprojBody, string programName, string programBody)
    {
        var root = TempRoot($"opencode-parity-{kind}");
        try
        {
            File.WriteAllText(Path.Combine(root, csprojName), csprojBody);
            File.WriteAllText(Path.Combine(root, programName), programBody);

            // 1. Reference: real SDK build; snapshot its assembly before we overwrite bin.
            var sdk = Run("dotnet", $"build \"{root}\" -c Debug");
            sdk.exit.ShouldBe(0, $"dotnet build failed:\n{sdk.output}");
            var assemblyName = Path.GetFileNameWithoutExtension(csprojName);
            var outputDir = Path.Combine(root, "bin", "Debug", "net10.0");
            var sdkDll = Path.Combine(outputDir, assemblyName + ".dll");
            File.Exists(sdkDll).ShouldBeTrue("SDK build did not produce bin/Debug/net10.0/<name>.dll");

            var snapshotDir = Path.Combine(root, ".snapshot");
            Directory.CreateDirectory(snapshotDir);
            var snapshotDll = Path.Combine(snapshotDir, assemblyName + ".dll");
            var snapshotPdb = Path.Combine(snapshotDir, assemblyName + ".pdb");
            File.Copy(sdkDll, snapshotDll);
            File.Copy(Path.Combine(outputDir, assemblyName + ".pdb"), snapshotPdb);
            var sdkSize = new FileInfo(sdkDll).Length;

            // 2. Ours: same project, same folder layout. The one tiny, deliberate difference is the output
            // root: "myc" instead of "bin", so we never overwrite SDK output. The relative layout
            // (Debug\net10.0\<name>.dll) must mirror the SDK's exactly.
            var result = NativeCompiler.NativeCompiler.Build(root);
            result.Success.ShouldBeTrue($"compile failed: {result.Errors}");
            var relLayout = Path.Combine("myc", "Debug", "net10.0", assemblyName + ".dll");
            var ourDll = Path.Combine(root, relLayout);
            File.Exists(ourDll).ShouldBeTrue($"our output missing at {ourDll}");
            var sdkRelLayout = Path.Combine("bin", "Debug", "net10.0", assemblyName + ".dll");
            var sdkSegments = sdkRelLayout.Split(Path.DirectorySeparatorChar);
            var ourSegments = relLayout.Split(Path.DirectorySeparatorChar);
            ourSegments.Length.ShouldBe(sdkSegments.Length);
            for (var i = 1; i < ourSegments.Length; i++)
                ourSegments[i].ShouldBe(sdkSegments[i], $"layout must mirror the SDK below the output root (segment {i})");
            ourSegments[0].ShouldBe("myc");
            sdkSegments[0].ShouldBe("bin");

            // 3. Structural parity via metadata-only reflection.
            var resolverPaths = RefResolverPaths();
            resolverPaths.Length.ShouldBeGreaterThan(0, "no shared framework ref assemblies found");
            var ourMeta = LoadMetadata(ourDll, resolverPaths);
            var sdkMeta = LoadMetadata(snapshotDll, resolverPaths);

            ourMeta.IdentityName.ShouldBe(sdkMeta.IdentityName);
            ourMeta.Version.ShouldBe(sdkMeta.Version);

            // AssemblyVersion is carried as the assembly's metadata version, not as a custom
            // attribute, so treat it via ourMeta.Version above.
            foreach (var attribute in new[] { "AssemblyCompanyAttribute", "AssemblyProductAttribute", "AssemblyTitleAttribute",
                                              "AssemblyConfigurationAttribute",
                                              "AssemblyFileVersionAttribute", "AssemblyInformationalVersionAttribute" })
            {
                sdkMeta.Attributes.TryGetValue(attribute, out var expected).ShouldBeTrue($"SDK assembly missing {attribute}");
                ourMeta.Attributes.TryGetValue(attribute, out var actual).ShouldBeTrue($"ours missing {attribute}");
                actual.ShouldBe(expected, $"{attribute} parity");
            }

            ourMeta.References.ShouldBe(sdkMeta.References, "referenced assembly set parity");
            ourMeta.PublicTypes.ShouldBe(sdkMeta.PublicTypes, "public type surface parity");

            // 3b. PDB document parity: the document set (path, hash algorithm, content checksum)
            // must be identical to the SDK's. Only the PdbId (a content-derived GUID) differs by
            // construction, so a full doc-set equality assert is the right gate.
            var ourPdb = Path.Combine(root, "myc", "Debug", "net10.0", assemblyName + ".pdb");
            PdbDocumentSummaries(ourPdb).ShouldBe(
                PdbDocumentSummaries(snapshotPdb), "PDB document set parity (paths + SHA-256 checksums)");
            PdbTextDump(ourPdb).ShouldBe(PdbTextDump(snapshotPdb)
                .Replace(PdbId(snapshotPdb), PdbId(ourPdb)), "PDB debug-info text parity (docs, sequence points, scopes)");

            // 4. diffoscope over gzipped tarballs of each tree's app-artifact subset + summary
            // table (informational; byte parity is not expected by design).
            var reportDir = Path.Combine(Path.GetTempPath(), "opencode-parity-reports");
            Directory.CreateDirectory(reportDir);
            var report = Path.Combine(reportDir, kind + ".diff.txt");
            var verdict = "diffoscope not available";
            var diffoscope = FindDiffoscope();
            if (diffoscope is not null)
            {
                var sdkOutDir = Path.GetDirectoryName(sdkDll)!;
                var ourOutDir = Path.GetDirectoryName(ourDll)!;
                var sdkTar = TarAppArtifacts(sdkOutDir, assemblyName);
                var ourTar = TarAppArtifacts(ourOutDir, assemblyName);

                // Files the SDK produced but our tree does not (apphost, .deps.json) are expected
                // by design; diffoscope still lists them, so surface the names here for the table.
                var sdkOnly = AppArtifactNames(sdkOutDir, assemblyName)
                    .Except(AppArtifactNames(ourOutDir, assemblyName), StringComparer.Ordinal)
                    .OrderBy(name => name)
                    .ToList();

                var diff = Run(diffoscope, $"\"{sdkTar}\" \"{ourTar}\" --text \"{report}\"");
                verdict = File.Exists(report)
                    ? $"report at {report} ({new FileInfo(report).Length} bytes; exit {diff.exit})"
                    : $"diffoscope produced no report (exit {diff.exit})";

                // Readable PDB comparison: dump the portable PDB to text on each side and let
                // diffoscope render the delta as HTML. (The two text blobs are already asserted
                // equal above modulo the PdbId; the report is for review.)
                var pdbHtml = Path.Combine(reportDir, kind + ".pdb.html");
                var oursPdbText = PdbTextDump(ourPdb);
                var sdkPdbText = PdbTextDump(snapshotPdb).Replace(PdbId(snapshotPdb), PdbId(ourPdb));
                File.WriteAllText(Path.Combine(reportDir, kind + "_sdk.pdb.txt"), sdkPdbText);
                File.WriteAllText(Path.Combine(reportDir, kind + "_ours.pdb.txt"), oursPdbText);
                Run(diffoscope, $"\"{Path.Combine(reportDir, kind + "_sdk.pdb.txt")}\" \"{Path.Combine(reportDir, kind + "_ours.pdb.txt")}\" --html \"{pdbHtml}\"");
                var pdbReport = File.Exists(pdbHtml)
                    ? $" {kind}.pdb.html ({new FileInfo(pdbHtml).Length} bytes)"
                    : " (PDB text identical - diffoscope writes no diff report)";

                Console.WriteLine("tarball diffoscope (app-artifact subset):");
                Console.WriteLine($"  sdk tree : {string.Join(", ", AppArtifactNames(sdkOutDir, assemblyName))}");
                Console.WriteLine($"  our tree : {string.Join(", ", AppArtifactNames(ourOutDir, assemblyName))}");
                if (sdkOnly.Count > 0)
                    Console.WriteLine($"  sdk-only (by design): {string.Join(", ", sdkOnly)}");
                if (File.Exists(report))
                {
                    Console.WriteLine($"  report excerpt ({report}):");
                    foreach (var line in File.ReadAllLines(report).Take(40))
                        Console.WriteLine("    " + line);
                }
                Console.WriteLine($"PDB ok (report:{pdbReport})");
            }

            var table = new StringBuilder();
            table.AppendLine($"| {assemblyName} (Debug, net10.0) | SDK build | our build |");
            table.AppendLine("|---|-:|-:|");
            table.AppendLine($"| output (root differs by design) | bin\\\\Debug\\\\net10.0\\\\{assemblyName}.dll | myc\\\\Debug\\\\net10.0\\\\{assemblyName}.dll |");
            table.AppendLine($"| size (bytes) | {sdkSize} | {new FileInfo(ourDll).Length} |");
            table.AppendLine($"| sha256 | {Sha256(snapshotDll)} | {Sha256(ourDll)} |");
            table.AppendLine($"| diffoscope | - | {verdict} |");
            table.AppendLine($"| identity attributes | match | match |");
            table.AppendLine($"| PDB documents (paths + checksums) | match | match |");
            table.AppendLine($"| referenced assemblies ({ourMeta.References.Count}) | match | match |");
            table.AppendLine($"| public types ({ourMeta.PublicTypes.Count}) | match | match |");

            Console.WriteLine(table.ToString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Whole_Tree_DeepScan_Parity(string kind, string csprojName, string csprojBody, string programName, string programBody)
    {
        // Deep scan over the ENTIRE output tree, not just the app-artifact subset - the subset
        // masked a real divergence where our "myc" tree carried ~250 shared-framework + analyzer
        // assemblies that `dotnet build` never copies into bin/. Gate: our whole tree must contain
        // nothing beyond the app's own artifacts, or the output is not SDK-parity.
        var root = TempRoot($"opencode-parity-fulltree-{kind}");
        try
        {
            File.WriteAllText(Path.Combine(root, csprojName), csprojBody);
            File.WriteAllText(Path.Combine(root, programName), programBody);
            var assemblyName = Path.GetFileNameWithoutExtension(csprojName);

            Run("dotnet", $"build \"{root}\" -c Debug").exit.ShouldBe(0, "SDK build failed");
            var ours = NativeCompiler.NativeCompiler.Build(root);
            ours.Success.ShouldBeTrue($"our build failed: {ours.Errors}");

            var sdkDir = Path.Combine(root, "bin", "Debug", "net10.0");
            var ourDir = Path.Combine(root, "myc", "Debug", "net10.0");
            var sdkFiles = RelativeFiles(sdkDir);
            var ourFiles = RelativeFiles(ourDir);

            var oursOnly = ourFiles.Except(sdkFiles, StringComparer.Ordinal)
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
            var sdkOnly = sdkFiles.Except(ourFiles, StringComparer.Ordinal)
                .OrderBy(f => f, StringComparer.Ordinal).ToList();

            // Apphost name mirrors the SDK: <name>.exe on Windows, bare <name> on Unix.
            var appHostName = assemblyName + (OperatingSystem.IsWindows() ? ".exe" : "");
            var unexpected = oursOnly.Where(f => f != appHostName).ToList();
            unexpected.ShouldBeEmpty(
                $"our output tree ships files the SDK never produces (shared-framework or analyzer assemblies?): {string.Join(", ", unexpected)}");

            // SDK-only files are the documented ones: the dependency graph we deliberately do not
            // emit, and the Web SDK's assets manifest.
            foreach (var file in sdkOnly)
            {
                var allowed = file == assemblyName + ".deps.json"
                              || (kind == "webapi" && file == assemblyName + ".staticwebassets.endpoints.json");
                allowed.ShouldBeTrue($"SDK-only file '{file}' has no equivalent in our tree: {string.Join(", ", sdkOnly)}");
            }

            var reportDir = Path.Combine(Path.GetTempPath(), "opencode-parity-reports");
            Directory.CreateDirectory(reportDir);
            Console.WriteLine($"whole-tree file sets ({assemblyName}):");
            Console.WriteLine($"  ours only : {string.Join(", ", oursOnly.Count == 0 ? new[] { "(none)" } : oursOnly.ToArray())}");
            Console.WriteLine($"  sdk only  : {string.Join(", ", sdkOnly.Count == 0 ? new[] { "(none)" } : sdkOnly.ToArray())}");

            // Full-tree tarballs through diffoscope when installed (report for review only; the
            // file-set gate above is the hard assertion).
            if (FindDiffoscope() is { } diffoscope)
            {
                var report = Path.Combine(reportDir, kind + ".fulltree.diff.txt");
                var diff = Run(diffoscope, $"\"{TarTree(sdkDir)}\" \"{TarTree(ourDir)}\" --text \"{report}\"");
                if (File.Exists(report))
                {
                    Console.WriteLine($"whole-tree diffoscope report: {report} ({new FileInfo(report).Length} bytes; exit {diff.exit})");
                    foreach (var line in File.ReadAllLines(report).Take(30))
                        Console.WriteLine("    " + line);
                }
                else
                {
                    Console.WriteLine($"whole-tree diffoscope: no report (exit {diff.exit}) - outputs identical");
                }
            }
            else
            {
                Console.WriteLine("whole-tree diffoscope: not available (skipping report)");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Console_Output_Runs_From_Sdk_Relative_Path()
    {
        // The produced console DLL is runnable straight from our myc\Debug\net10.0 output layout.
        var root = TempRoot("opencode-parity-run");
        try
        {
            File.WriteAllText(Path.Combine(root, "ParityConsole.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
            File.WriteAllText(Path.Combine(root, "Program.cs"),
                "Console.WriteLine(\"PARITY-RUN-OK\");\n");

            var result = NativeCompiler.NativeCompiler.Build(root);
            result.Success.ShouldBeTrue(result.Errors);

            var dll = Path.Combine(root, "myc", "Debug", "net10.0", "ParityConsole.dll");
            File.Exists(dll).ShouldBeTrue();
            var run = Run("dotnet", $"\"{dll}\"");
            run.exit.ShouldBe(0, $"our console dll failed to run:\n{run.output}");
            run.output.ShouldContain("PARITY-RUN-OK");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // Metadata comparison.
    // ------------------------------------------------------------------
    private sealed record MetadataSummary(
        string IdentityName,
        string? Version,
        SortedDictionary<string, string> Attributes,
        List<string> References,
        List<string> PublicTypes);

    private static MetadataSummary LoadMetadata(string dllPath, string[] resolverPaths)
    {
        using var peReader = new PEReader(File.OpenRead(dllPath));
        var reader = peReader.GetMetadataReader();
        using var context = new MetadataLoadContext(
            new SinglePathResolver(resolverPaths.Append(dllPath)),
            coreAssemblyName: "System.Private.CoreLib");

        var assembly = context.LoadFromAssemblyPath(dllPath);
        var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in assembly.GetCustomAttributesData())
            attributes[data.AttributeType.Name] = string.Join(",", data.ConstructorArguments.Select(a => a.Value?.ToString() ?? "null"));

        var references = assembly.GetReferencedAssemblies()
            .Select(r => $"{r.Name}@{r.Version}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var publicTypes = assembly.GetTypes()
            .Where(t => t.IsPublic)
            .Select(t => t.FullName!)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        return new MetadataSummary(assembly.GetName().Name!, assembly.GetName().Version?.ToString(), attributes, references, publicTypes);
    }

    /// <summary>MetadataLoadContext resolver: one path per assembly simple name (first wins).
    /// A plain PathAssemblyResolver fails because the ref and shared-framework dirs contain
    /// multiple copies of the same identity (e.g. mscorlib in both).</summary>
    private sealed class SinglePathResolver : MetadataAssemblyResolver
    {
        private readonly Dictionary<string, string> _byName;

        public SinglePathResolver(IEnumerable<string> paths)
        {
            _byName = paths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => (name: Path.GetFileNameWithoutExtension(p), path: p))
                .GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().path, StringComparer.OrdinalIgnoreCase);
        }

        public override Assembly? Resolve(MetadataLoadContext context, AssemblyName assemblyName)
        {
            if (assemblyName.Name is not null && _byName.TryGetValue(assemblyName.Name, out var path))
                return context.LoadFromAssemblyPath(path);
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Portable PDB comparison.
    // ------------------------------------------------------------------

    // Well-known portable-PDB doc hash algorithm GUIDs. NOTE: the GUIDs are easy to mistake for
    // each other (Roslyn's WellKnownChecksumAlgorithms) - do not re-order without re-checking.
    private static readonly Guid PdbHashSha1 = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid PdbHashSha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");

    private static string PdbId(string pdbPath)
    {
        using var fs = File.OpenRead(pdbPath);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(fs, MetadataStreamOptions.LeaveOpen);
        return Convert.ToHexString(provider.GetMetadataReader().DebugMetadataHeader!.Id.AsSpan());
    }

    // The parity gate on PDB debug info: full document list (path + hash algorithm + checksum).
    // This is byte-comparable with the SDK's since csc records the same document set with the
    // same SHA-256 checksums.
    private static List<string> PdbDocumentSummaries(string pdbPath)
    {
        using var fs = File.OpenRead(pdbPath);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(fs, MetadataStreamOptions.LeaveOpen);
        var md = provider.GetMetadataReader();
        var summaries = new List<string>();
        foreach (var docHandle in md.Documents)
        {
            var doc = md.GetDocument(docHandle);
            summaries.Add($"{md.GetString(doc.Name)}|{md.GetGuid(doc.HashAlgorithm)}|{Convert.ToHexString(md.GetBlobBytes(doc.Hash))}");
        }
        return summaries;
    }

    // Human-readable rendering of a portable PDB (docs + methods + sequence points + scopes) so
    // diffoscope can compare debug info as text. PdbId is excluded (it is a content-derived GUID.)
    private static string PdbTextDump(string pdbPath)
    {
        var sb = new StringBuilder();
        using var fs = File.OpenRead(pdbPath);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(fs, MetadataStreamOptions.LeaveOpen);
        var md = provider.GetMetadataReader();
        sb.AppendLine($"Documents ({md.Documents.Count}):");
        foreach (var docHandle in md.Documents)
        {
            var doc = md.GetDocument(docHandle);
            var alg = md.GetGuid(doc.HashAlgorithm);
            sb.AppendLine($"  {md.GetString(doc.Name)}");
            sb.AppendLine($"    hashAlg = {(alg == PdbHashSha1 ? "SHA-1" : alg == PdbHashSha256 ? "SHA-256" : alg.ToString())}");
            sb.AppendLine($"    hash    = {Convert.ToHexString(md.GetBlobBytes(doc.Hash))}");
        }
        sb.AppendLine("Methods with debug info:");
        foreach (var mdi in md.MethodDebugInformation)
        {
            if (mdi.IsNil) continue;
            var body = md.GetMethodDebugInformation(mdi);
            sb.AppendLine($"  {MetadataTokens.GetToken(mdi):x8}");
            if (!body.Document.IsNil)
                sb.AppendLine($"    document = {md.GetString(md.GetDocument(body.Document).Name)}");
            try
            {
                foreach (var sp in body.GetSequencePoints())
                    sb.AppendLine($"    il 0x{sp.Offset:x4} {sp.StartLine}:{sp.StartColumn}->{sp.EndLine}:{sp.EndColumn}{(sp.IsHidden ? " hidden" : "")}");
            }
            catch { }
            try
            {
                foreach (var scopeHandle in md.GetLocalScopes(mdi))
                {
                    var scope = md.GetLocalScope(scopeHandle);
                    var vars = string.Join(" ", scope.GetLocalVariables().Select(v => md.GetLocalVariable(v)).Select(v => md.GetString(v.Name)));
                    sb.AppendLine($"    scope 0x{scope.StartOffset:x4}+0x{scope.Length:x4} vars: {vars}");
                }
            }
            catch { }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Process + tooling helpers.
    // ------------------------------------------------------------------
    private static (int exit, string output) Run(string file, string arguments)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // diffoscope shells out to decoder tools (pedump/xxd...) which live in the user's
        // ~/.local/bin on the parity box; make sure a spawned process can find them.
        var localBin = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".local", "bin");
        if (Directory.Exists(localBin))
            psi.Environment["PATH"] = localBin + Path.PathSeparator + psi.Environment["PATH"];
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);
        return (process.ExitCode, output);
    }

    private static string? FindDiffoscope()
    {
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("PATH") is { } path)
            candidates.AddRange(path.Split(Path.PathSeparator).Select(p => Path.Combine(p, "diffoscope")));
        candidates.Add(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetEnvironmentVariable("HOME") ?? "", ".local", "bin", "diffoscope"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string Sha256(string file)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    // ------------------------------------------------------------------
    // Tarball subsetting for diffoscope.
    // ------------------------------------------------------------------

    // The files a project itself produced: <name>.dll/.pdb/.runtimeconfig.json, plus the SDK-only
    // apphost (<name> with no extension) and <name>.deps.json. Everything else in our output tree
    // (the copied shared-framework assemblies) and the SDK's obj/ tree is excluded on purpose so
    // the diffoscope report stays on the app-artifact surface.
    private static IEnumerable<string> AppArtifactNames(string dir, string assemblyName)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(file);
            if (IsAppArtifact(name, assemblyName)) yield return name;
        }
    }

    private static bool IsAppArtifact(string name, string assemblyName)
    {
        if (name == assemblyName) return true; // apphost
        return name == assemblyName + ".dll"
            || name == assemblyName + ".pdb"
            || name == assemblyName + ".runtimeconfig.json"
            || name == assemblyName + ".deps.json";
    }

    private static string TarAppArtifacts(string dir, string assemblyName)
    {
        var staging = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir)
                         .Where(f => IsAppArtifact(Path.GetFileName(f), assemblyName)))
                File.Copy(file, Path.Combine(staging, Path.GetFileName(file)));

            var tarPath = staging + ".tar.gz";
            using var fs = File.Create(tarPath);
            using var gzip = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal);
            TarFile.CreateFromDirectory(staging, gzip, includeBaseDirectory: false);
            return tarPath;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    // Recursive file list of an output tree, relative + canonicalized (forward slashes) so the
    // comparison is separator-independent across the SDK/bin and our/myc trees.
    private static List<string> RelativeFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    // Gzipped tarball of a whole directory tree (for full-output deep scans via diffoscope).
    private static string TarTree(string dir)
    {
        var tarPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".tar.gz");
        using var fs = File.Create(tarPath);
        using var gzip = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal);
        TarFile.CreateFromDirectory(dir, gzip, includeBaseDirectory: false);
        return tarPath;
    }
}