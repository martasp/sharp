using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NativeCompilerTests;

// Always runs with the suite and prints how our BuildSolution compares to `dotnet build` on the
// same solution (timings are informational only - no thresholds are asserted, machines differ).
// Set NATIVECOMPILER_SKIP_BENCHMARK=1 to skip it for a fast suite run.
// Filter it on its own:  dotnet test --filter "Category=Benchmark"
[CollectionDefinition("Sequential Tests 5", DisableParallelization = true)]
[Trait("Category", "Benchmark")]
[Collection("Sequential Tests 5")]
public class PerformanceComparisonTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceComparisonTests(ITestOutputHelper output) => _output = output;

    public static string RepoRoot =>
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));

    public static string RepoSolution => Path.Combine(RepoRoot, "NativeCompiler.sln");

    // Published compiler executable name is platform-specific (NativeCompilerCLI.exe on Windows,
    // NativeCompilerCLI on Unix). Default to the publish matching the current OS; the variable
    // override still wins for custom seeds.
    private static readonly string PublishedCompilerExe =
        Environment.GetEnvironmentVariable("NATIVECOMPILER_PUBLISHED_EXE")
        ?? Path.Combine(RepoRoot, "src", "NativeCompilerCLI", "bin", "Release", "net10.0",
            OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", "publish",
            OperatingSystem.IsWindows() ? "NativeCompilerCLI.exe" : "NativeCompilerCLI");

    [Fact]
    public async Task Compare_BuildSolution_Against_DotnetBuild()
    {
        if (Environment.GetEnvironmentVariable("NATIVECOMPILER_SKIP_BENCHMARK") == "1")
        {
            _output.WriteLine("Skipped: NATIVECOMPILER_SKIP_BENCHMARK=1");
            return;
        }

        // Work on a clean copy so obj/bin state of the repo is untouched and both tools build cold.
        var benchRoot = Path.Combine(Path.GetTempPath(), "opencode-bench", Guid.NewGuid().ToString("N"));
        CopyTree(RepoRoot, benchRoot);
        var benchSolution = Path.Combine(benchRoot, "NativeCompiler.sln");

        try
        {
            // Ours: in-process, always from scratch (no incremental cache).
            var oursFirst = Stopwatch.StartNew();
            var resultFirst = NativeCompiler.NativeCompiler.BuildSolution(benchSolution);
            oursFirst.Stop();

            var oursSecond = Stopwatch.StartNew();
            var resultSecond = NativeCompiler.NativeCompiler.BuildSolution(benchSolution);
            oursSecond.Stop();

            // dotnet build: cold (fresh restore) then warm (incremental).
            var (dotnetCold, _, dotnetColdTimedOut) = await RunDotnetBuild(benchSolution);
            var (dotnetWarm, _, dotnetWarmTimedOut) = await RunDotnetBuild(benchSolution);

            var oursTotal1 = resultFirst.Projects.Sum(project => project.Result.Duration.TotalSeconds);

            var dotnetColdText = dotnetColdTimedOut ? "TIMED OUT (killed after 180s)" : $"{dotnetCold.TotalSeconds:0.00}s (incl. restore)";
            var dotnetWarmText = dotnetWarmTimedOut ? "TIMED OUT (killed after 180s)" : $"{dotnetWarm.TotalSeconds:0.00}s (incremental)";

            var lines = new List<string>
            {
                "-------------- speed comparison (NativeCompiler.sln) --------------",
                $"  our BuildSolution  run1  wall {oursFirst.Elapsed.TotalSeconds,6:0.00}s   (sum of per-project {oursTotal1:0.00}s, {resultFirst.Projects.Count(p => p.Result.Success)}/{resultFirst.Projects.Count} OK)",
                $"  our BuildSolution  run2  wall {oursSecond.Elapsed.TotalSeconds,6:0.00}s   (no cache: always from scratch)",
                $"  dotnet build       cold  wall {dotnetColdText}",
                $"  dotnet build       warm  wall {dotnetWarmText}",
            };
            if (!dotnetColdTimedOut && !dotnetWarmTimedOut)
                lines.Add($"  speed-up vs dotnet build cold: {dotnetCold.TotalSeconds / Math.Max(0.001, oursFirst.Elapsed.TotalSeconds):0.00}x");
            lines.Add("---------------------------------------------------------------------");
            lines.Add("  notes:");
            lines.Add("  - ours has no incremental cache; dotnet build warm is incremental");
            lines.Add("  - ours reports per-project FAIL for unsupported features (see main.md)");

            var latestPath = SaveBenchmark("jit", lines);

            foreach (var line in lines)
                _output.WriteLine(line);
            _output.WriteLine($"  (committed results saved to {latestPath})");
        }
        finally
        {
            try { Directory.Delete(benchRoot, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Compare_PublishedAotCompiler_Against_DotnetBuild()
    {
        if (Environment.GetEnvironmentVariable("NATIVECOMPILER_SKIP_BENCHMARK") == "1")
        {
            _output.WriteLine("Skipped: NATIVECOMPILER_SKIP_BENCHMARK=1");
            return;
        }

        if (!File.Exists(PublishedCompilerExe))
        {
            throw new InvalidOperationException(
                $"Published compiler not found at {PublishedCompilerExe}. Run publish.bat first "
                + "(or point NATIVECOMPILER_PUBLISHED_EXE at a published NativeCompilerCLI.exe).");
        }

        // Work on a clean copy so obj/bin/myc state of the repo is untouched and both tools build cold.
        var benchRoot = Path.Combine(Path.GetTempPath(), "opencode-bench", Guid.NewGuid().ToString("N"));
        CopyTree(RepoRoot, benchRoot);
        var benchSolution = Path.Combine(benchRoot, "NativeCompiler.sln");

        try
        {
            // Ours: the actual signed native-AOT published compiler, from scratch (no incremental cache).
            var (oursFirst, oursFirstExit, oursFirstTimedOut, _) = await RunPublishedCompiler(benchSolution);
            var (oursSecond, oursSecondExit, oursSecondTimedOut, _) = await RunPublishedCompiler(benchSolution);

            // dotnet build: cold (fresh restore) then warm (incremental).
            var (dotnetCold, _, dotnetColdTimedOut) = await RunDotnetBuild(benchSolution);
            var (dotnetWarm, _, dotnetWarmTimedOut) = await RunDotnetBuild(benchSolution);

            var oursFirstText = oursFirstTimedOut ? "TIMED OUT (killed after 300s)" : $"{oursFirst.TotalSeconds:0.00}s (exit {oursFirstExit})";
            var oursSecondText = oursSecondTimedOut ? "TIMED OUT (killed after 300s)" : $"{oursSecond.TotalSeconds:0.00}s (exit {oursSecondExit})";
            var dotnetColdText = dotnetColdTimedOut ? "TIMED OUT (killed after 180s)" : $"{dotnetCold.TotalSeconds:0.00}s (incl. restore)";
            var dotnetWarmText = dotnetWarmTimedOut ? "TIMED OUT (killed after 180s)" : $"{dotnetWarm.TotalSeconds:0.00}s (incremental)";

            var lines = new List<string>
            {
                "-------------- published AOT compiler vs dotnet build (NativeCompiler.sln) --------------",
                $"  NativeCompilerCLI.exe  run1  wall {oursFirstText,6}",
                $"  NativeCompilerCLI.exe  run2  wall {oursSecondText,6}   (no cache: always from scratch)",
                $"  dotnet build           cold  wall {dotnetColdText}",
                $"  dotnet build           warm  wall {dotnetWarmText}",
            };
            if (!dotnetColdTimedOut && !oursFirstTimedOut)
                lines.Add($"  speed-up vs dotnet build cold: {dotnetCold.TotalSeconds / Math.Max(0.001, oursFirst.TotalSeconds):0.00}x");
            lines.Add("---------------------------------------------------------------------");
            lines.Add("  notes:");
            lines.Add("  - ours is measured as the published signed native-AOT compiler (subprocess, wall clock)");
            lines.Add("  - in-process JIT comparison lives in Compare_BuildSolution_Against_DotnetBuild");
            lines.Add("  - ours has no incremental cache; dotnet build warm is incremental");
            lines.Add("  - ours reports per-project FAIL for unsupported features (see main.md)");
            // Relative to the repo so committed captures never contain absolute machine paths.
            lines.Add($"  exe: {Path.GetRelativePath(RepoRoot, PublishedCompilerExe)}");

            var latestPath = SaveBenchmark("published", lines);

            foreach (var line in lines)
                _output.WriteLine(line);
            _output.WriteLine($"  (committed results saved to {latestPath})");
        }
        finally
        {
            try { Directory.Delete(benchRoot, true); } catch { /* best effort */ }
        }
    }

    private static async Task<(TimeSpan Wall, int ExitCode, bool TimedOut, string Output)> RunPublishedCompiler(
        string solutionPath)
    {
        var info = new ProcessStartInfo(PublishedCompilerExe, $"\"{solutionPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }
        sw.Stop();

        var exitCode = timedOut ? -1 : process.ExitCode;
        return (sw.Elapsed, exitCode, timedOut, stdout.Result + stderr.Result);
    }

    private static async Task<(TimeSpan Wall, int ExitCode, bool TimedOut)> RunDotnetBuild(string solutionPath)
    {
        var info = new ProcessStartInfo("dotnet", $"build \"{solutionPath}\" -v q -nologo -p:RestoreIgnoreFailedSources=true")
        {
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(info)!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }
        sw.Stop();

        var exitCode = timedOut ? -1 : process.ExitCode;
        return (sw.Elapsed, exitCode, timedOut);
    }

    private static string SaveBenchmark(string kind, List<string> lines)
    {
        // Written into the repo (benchmarks/) so results can be committed to git:
        // - a dated file keeps a per-run history
        // - a stable "latest-<kind>.txt" is overwritten every run so `git diff` shows the
        //   change between consecutive benchmark runs.
        var benchmarksDir = Path.Combine(RepoRoot, "benchmarks");
        Directory.CreateDirectory(benchmarksDir);

        var datedPath = Path.Combine(benchmarksDir, $"benchmark-{kind}-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
        File.WriteAllLines(datedPath, lines);

        var latestPath = Path.Combine(benchmarksDir, $"latest-{kind}.txt");
        File.WriteAllLines(latestPath, lines);
        return latestPath;
    }

    private static void CopyTree(string sourceRoot, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var segments = relative.Split(Path.DirectorySeparatorChar);
            if (segments.Any(segment => segment is "bin" or "obj" or "myc" or ".git" or ".vs" or "OpenVinoCS"))
                continue;

            var destination = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? targetRoot);
            File.Copy(file, destination);
        }
    }
}