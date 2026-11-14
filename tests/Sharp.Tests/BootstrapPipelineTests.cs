using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Sharp.Tests;

// Bootstrap (self-hosting) pipeline validation:
//
//   AOT seed (the published, signed native compiler)
//     -- compiles Sharp.sln --> produces the compiler itself as JIT output (myc)
//   that JIT compiler (its apphost exe, run from a temp copy)
//     -- compiles Sharp.sln again --> 0 errors (fixed point, generation 2)
//
// Each generation recompiles the whole solution from scratch; the seed of every generation is
// the Sharp.Cli myc output written by the previous generation, so the test proves the
// compiler rebuilds itself without relying on MSBuild at any stage after the (AOT) seed.
//
// How to run (one command, shows what you see here on the console):
//   bootstrap.bat
//
//   That builds the test host, signs the test outputs (required on this machine's
//   code-integrity policy), runs the test with detailed logging so every stage is visible,
//   then prints the full transcript file.
[Trait("Category", "Bootstrap")]
public class BootstrapPipelineTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    // The AOT seed is `dotnet publish` output and its file name is platform-specific: the Windows
    // RID produces Sharp.Cli.exe, the Linux RID Sharp.Cli. Pick the publish that
    // matches the current OS (produced by publish.bat / `dotnet publish -r <rid>`).
    private static readonly string PublishedSeedExe = Path.Combine(
        RepoRoot, "src", "Sharp.Cli", "bin", "Release", "net11.0",
        OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", "publish",
        OperatingSystem.IsWindows() ? "Sharp.Cli.exe" : "Sharp.Cli");

    private const string SolutionFile = "Sharp.sln";

    // The JIT compiler's apphost name mirrors the SDK: Sharp.Cli.exe on Windows,
    // Sharp.Cli on Unix.
    private static string HostName() => "Sharp.Cli" + (OperatingSystem.IsWindows() ? ".exe" : "");

    private static readonly string BootstrapReportPath = Path.Combine(
        Path.GetTempPath(), "sharp-bootstrap-last-run.txt");

    private readonly ITestOutputHelper _output;

    public BootstrapPipelineTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AotSeed_ProducesJitCompiler_ThatRebuildsSolutionClean()
    {
        if (!File.Exists(PublishedSeedExe))
            throw new InvalidOperationException(
                "Missing AOT seed - run `publish.bat` (builds + signs Sharp.Cli.exe) before this test.");

        PruneStaleSandboxes();

        var sandbox = Path.Combine(Path.GetTempPath(), "sharp-bootstrap-" + Guid.NewGuid().ToString("N"));
        CopyTree(RepoRoot, sandbox);
        var sln = Path.Combine(sandbox, SolutionFile);
        var jitOutput = Path.Combine(sandbox, "src", "Sharp.Cli", "myc", "Debug", "net11.0");

        File.WriteAllText(BootstrapReportPath, $"bootstrap report {DateTime.Now:O}{Environment.NewLine}{Environment.NewLine}");
        Log($"report file: {BootstrapReportPath}");
        Log($"sandbox:     {sandbox}");
        Log($"seed (AOT):  {PublishedSeedExe}");
        Log($"solution:    {sln}");
        Log($"jit output:  {jitOutput}");

        // Generation 0: the AOT seed compiles the solution, producing the first JIT compiler.
        // The seed is a self-contained native exe, so it runs directly (not via `dotnet`).
        var gen0 = await Compile($"\"{sln}\"", PublishedSeedExe, "AOT seed");
        AssertClean("AOT seed", gen0);
        var jitCompilerDll = Path.Combine(jitOutput, "Sharp.Cli.dll");
        Assert.True(File.Exists(jitCompilerDll),
            $"AOT seed did not produce the JIT compiler at '{jitCompilerDll}'.\n{gen0.Output}");

        // Generation 1: the AOT-produced JIT compiler rebuilds the solution (from a temp copy).
        var gen1Compiler = StageCompilerCopy(jitOutput);
        var gen1 = await Compile(
            $"\"{sln}\"",
            Path.Combine(gen1Compiler, HostName()),
            "generation 1");
        AssertClean("generation 1", gen1);

        // Generation 2: the gen-1 run rewrote the sandbox compiler output; stage that as the
        // new seed and rebuild once more - reaching the fixed point.
        var gen2Compiler = StageCompilerCopy(jitOutput);
        var gen2 = await Compile(
            $"\"{sln}\"",
            Path.Combine(gen2Compiler, HostName()),
            "generation 2");
        AssertClean("generation 2", gen2);

        // The sandbox and per-generation compiler copies are intentionally kept (paths are
        // printed above) so each stage can be inspected or re-run manually after the test.
    }

    // The sandbox (a full repo copy, ~300 MB) and the per-generation compiler copies are left in
    // place after a run for manual inspection, but each run leaks a fresh set. On a tmpfs that fills
    // the volume after a few runs and then fails unrelated tests for lack of space. Drop artifacts
    // from previous runs at the start of each run; the tree this run produces stays inspectable.
    private static void PruneStaleSandboxes()
    {
        foreach (var pattern in new[] { "sharp-bootstrap-*", "sharp-gen-*" })
        {
            foreach (var directory in Directory.EnumerateDirectories(Path.GetTempPath(), pattern))
            {
                try { Directory.Delete(directory, recursive: true); }
                catch { /* best effort: a concurrent run may hold a file */ }
            }
        }
    }

    private readonly record struct CompilationResult(int ExitCode, string Output);

    private static async Task<CompilationResult> RunAsync(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)!;
        using var stdout = process.StandardOutput;
        using var stderr = process.StandardError;
        var outputTask = stdout.ReadToEndAsync();
        var errorTask = stderr.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask + await errorTask;
        return new CompilationResult(process.ExitCode, output);
    }

    // Runs a compile stage, logs the exact command and the compiler's "project -> output" lines
    // plus the final summary, then returns the result for assertion.
    private async Task<CompilationResult> Compile(string arguments, string? fileName, string stage)
    {
        fileName ??= "dotnet";

        var result = await RunAsync(fileName, arguments);

        Log($"{Environment.NewLine}[{stage}] {fileName} {arguments}");
        foreach (var line in result.Output.Split('\n')
                     .Where(line => line.Contains(" -> ") || line.Contains("succeeded (")
                                 || line.Contains("Error(s)") || line.Contains("Warning(s)")
                                 || line.StartsWith("Build succeeded") || line.StartsWith("Build failed"))
                     .Select(line => line.TrimEnd()))
            Log("  " + line);
        Log($"  exit={result.ExitCode}");

        return result;
    }

    // A clean solution build exits 0 and reports the success verdict (the SDK terminal-logger
    // summary "Build succeeded[ in Ns]" / "Build succeeded with N warning(s) in Ns"); anything else
    // (a compiler crash, a blocked binary, a compile error) must surface as a test failure with
    // the full output attached.
    private static void AssertClean(string stage, CompilationResult result) =>
        Assert.True(
            result.ExitCode == 0
            && (result.Output.Contains("Build succeeded in")
                || result.Output.Contains("Build succeeded with"))
            && !result.Output.Contains("error CS")
            && !result.Output.Contains("Build failed"),
            $"[{stage}] expected a clean solution build. exit={result.ExitCode}\n{result.Output}");

    // Written to the on-screen test output (visible with a detailed console logger) AND to the
    // persistent report file, so a full transcript survives xunit's stdout capture.
    private void Log(string message)
    {
        _output.WriteLine(message);
        File.AppendAllText(BootstrapReportPath, message + Environment.NewLine);
    }

    private static string StageCompilerCopy(string compilerDir)
    {
        var dest = Path.Combine(Path.GetTempPath(), "sharp-gen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(compilerDir))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        return dest;
    }

    private static void CopyTree(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var file in Directory.EnumerateFiles(sourceRoot))
            File.Copy(file, Path.Combine(destinationRoot, Path.GetFileName(file)));

        foreach (var dir in Directory.EnumerateDirectories(sourceRoot))
        {
            var name = Path.GetFileName(dir)!;
            if (name is ".git" or ".vs" or "bin" or "obj" or "myc" or "publish" or "TestResults")
                continue;
            CopyTree(dir, Path.Combine(destinationRoot, name));
        }
    }
}