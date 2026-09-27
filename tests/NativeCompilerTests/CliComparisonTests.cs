using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace NativeCompilerTests;

// Visual behavior-parity between our compiler CLI and the real `dotnet build`. Renders both
// outputs side-by-side with CliComparisonHelper and pins them in a Verify snapshot, so compiler
// improvements that bring the output closer to the SDK (or drift it apart) become reviewable diffs.
// Success covers a real multi-project solution (the compiler repo itself); failure covers a
// deliberately broken fixture so the error layout can be compared too.
public class CliComparisonTests
{
    public static string RepoRoot =
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));

    public static string HelloParityFixtureDir =
        Path.Combine(RepoRoot, "tests", "TestExampleProjects", "HelloParityFixture");

    public static string FailingFixtureDir =
        Path.Combine(RepoRoot, "tests", "TestExampleProjects", "FailingFixture");

    static string CliDll =>
        Path.Combine(RepoRoot, "src", "NativeCompilerCLI", "bin", "Debug", "net10.0", "NativeCompilerCLI.dll");

    // The parity must exercise the current compiler CLI. It is NOT rebuilt from inside the test:
    // the test host keeps src\NativeCompiler\bin assemblies mapped, so a subprocess `dotnet build`
    // of the CLI (which references that library) deadlocks on the file lock. Rebuild the CLI with
    // `dotnet build NativeCompiler.sln` before running the suite.
    private static void EnsureCliExists()
    {
        if (!File.Exists(CliDll))
        {
            throw new InvalidOperationException(
                "NativeCompilerCLI is not built - run `dotnet build NativeCompiler.sln` first.");
        }
    }

    private static async Task<string> RunCliAsync(string fileName, string arguments)
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
        return await outputTask + await errorTask;
    }

    // Token used in place of the repo root so snapshots stay machine-independent. Must be applied
    // here BEFORE rendering: Verify would otherwise swap the real path for {SolutionDirectory}
    // after the side-by-side was laid out, shortening only the affected lines and misaligning the
    // pipes. Normalizing first keeps wrap points and column widths consistent with the saved text.
    private const string SolutionToken = "{SolutionDirectory}";

    // Wall-clock, restore banners add noise per run; drop them so the snapshot is deterministic.
    private static string Normalize(string raw)
    {
        string normalized = raw
            .Replace("\r\n", "\n")
            // Canonicalize separators FIRST: every downstream lookup then matches a single
            // dialect, so a Windows-authored snapshot (\ separators) and a Linux run (/)
            // land on byte-identical text. Path.Combine can't do this - it emits the host's
            // native separator, which is exactly the variance we are removing.
            .Replace('\\', '/')
            // Cull the path holder so snapshots stay machine-independent
            // (windows: {SolutionDirectory}\... linux: {SolutionDirectory}/...).
            .Replace(RepoRoot.Replace('\\', '/') + '/', SolutionToken + '/')
            // Our compiler emits its artifacts under a dedicated "myc" tree (myc\Debug\<TFM>) so it
            // never overwrites real SDK output in bin\; the physical location is irrelevant to
            // behavior, so map it to the canonical SDK layout for byte-parity with `dotnet build`.
            .Replace("/myc/Debug/net10.0/", "/bin/Debug/net10.0/");

        return string.Join("\n", normalized.Split('\n')
            .Where(line =>
            {
                string trimmed = line.Trim();
                return !trimmed.StartsWith("Total time:")
                       && !trimmed.StartsWith("Solution total time:")
                       && !trimmed.StartsWith("Time Elapsed")
                       && !trimmed.StartsWith("Determining projects to restore")
                       && !trimmed.StartsWith("All projects are up-to-date for restore")
                       && !trimmed.StartsWith("Restored ");
            })
            .Select(line => line.TrimEnd()));
    }

[Fact]
    public Task Build_HelloWorld_VisualParity_WithDotnetBuild()
    {
        EnsureCliExists();

        string nativeOutput = Normalize(
            RunCliAsync("dotnet", $"\"{CliDll}\" \"{HelloParityFixtureDir}\"").Result);
        // --no-restore/--no-incremental keep the SDK output focused on the compile itself (assets
        // already exist for the fixture) and force the mapping line + summary to reprint every run.
        string dotnetOutput = Normalize(RunCliAsync("dotnet",
            $"build \"{HelloParityFixtureDir}\" --nologo --no-restore --no-incremental").Result);

        Assert.Contains("Build succeeded.", nativeOutput);
        Assert.Contains("Build succeeded.", dotnetOutput);
        nativeOutput.ShouldBe(dotnetOutput);

        string comparisonResult = CliComparisonHelper.GenerateSideBySide(
            nativeOutput,
            dotnetOutput,
            "NATIVE COMPILER",
            "DOTNET BUILD");

        return Verify(comparisonResult);
    }

    [Fact]
    public Task Build_Failure_VisualParity_WithDotnetBuild()
    {
        EnsureCliExists();

        string nativeOutput = Normalize(
            RunCliAsync("dotnet", $"\"{CliDll}\" \"{FailingFixtureDir}\"").Result);
        string dotnetOutput = Normalize(RunCliAsync("dotnet",
            $"build \"{FailingFixtureDir}\" --nologo").Result);

        Assert.Contains("CS0103", nativeOutput);
        Assert.Contains("CS0103", dotnetOutput);
        Assert.Contains("Build FAILED.", nativeOutput);
        Assert.Contains("Build FAILED.", dotnetOutput);
        nativeOutput.ShouldBe(dotnetOutput);

        string comparisonResult = CliComparisonHelper.GenerateSideBySide(
            nativeOutput,
            dotnetOutput,
            "NATIVE COMPILER",
            "DOTNET BUILD");

        return Verify(comparisonResult);
    }
}