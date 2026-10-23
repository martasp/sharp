using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace Sharp.Tests;

// CLI command parity with the .NET SDK. Every command our CLI advertises (`build`, `run`, `help`,
// `--version`) is executed against the fixture and the captured output is compared byte-for-byte with
// the matching `dotnet` invocation after shared normalization (path tokens + restore/time noise).
// The captured outputs are also rendered side-by-side into a Verify snapshot, so any drift is visible
// in review. This is the contract: where our CLI says it behaves like `dotnet`, it must match.
//
// These build the checked-in HelloParityFixture/FailingFixture/SlnFixtures directories through the SDK
// and the CLI - the same fixtures CliComparisonTests and SolutionTests build. The sequential collection
// keeps them serialized so concurrent builders cannot race on the fixtures' obj/bin.
[Collection("Sequential Tests 4")]
public class CliCommandParityTests
{
    public static string RepoRoot =
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));

    public static string HelloParityFixtureDir =
        Path.Combine(RepoRoot, "tests", "TestExampleProjects", "HelloParityFixture");

    public static string FailingFixtureDir =
        Path.Combine(RepoRoot, "tests", "TestExampleProjects", "FailingFixture");

    public static string TestExampleProjectsDir =
        Path.Combine(RepoRoot, "tests", "TestExampleProjects");

    public static string SlnxFixturesSolutionPath =
        Path.Combine(TestExampleProjectsDir, "SlnxFixtures.slnx");

    public static string SlnFixturesSolutionPath =
        Path.Combine(TestExampleProjectsDir, "SlnFixtures", "SlnFixtures.sln");

    static string CliDll =>
        Path.Combine(RepoRoot, "src", "Sharp.Cli", "bin", "Debug", "net10.0", "Sharp.Cli.dll");

    private static async Task<(string Output, int ExitCode)> RunAsync(
        string fileName, string arguments, string? workingDirectory = null, params (string Key, string Value)[] env)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
        };
        foreach (var (key, value) in env) startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)!;
        using var stdout = process.StandardOutput;
        using var stderr = process.StandardError;
        var outputTask = stdout.ReadToEndAsync();
        var errorTask = stderr.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (await outputTask + await errorTask, process.ExitCode);
    }

    // Commands are executed on both compilers and every captured output must match: exit code and bytes.
    [Fact]
    public Task Build_Success_Output_Parity()
        => CompareTerminal("build_success", $"build \"{HelloParityFixtureDir}\"",
            $"build \"{HelloParityFixtureDir}\" --nologo --no-restore --no-incremental");

    [Fact]
    public Task Build_Failure_Output_Parity()
        => CompareTerminal("build_failure", $"build \"{FailingFixtureDir}\"",
            $"build \"{FailingFixtureDir}\" --nologo --no-restore --no-incremental");

    [Fact]
    public Task Run_Success_Output_Parity()
        => Compare("run_success", $"run \"{HelloParityFixtureDir}\"",
            $"run --project \"{HelloParityFixtureDir}\" --no-restore");

    [Fact]
    public Task Run_Failure_Output_Parity()
        => Compare("run_failure", $"run \"{FailingFixtureDir}\"",
            $"run --project \"{FailingFixtureDir}\" --no-restore");

    [Fact]
    public Task Version_Output_Parity()
        => Compare("version", "--version", "--version");

    [Fact]
    public async Task Help_Output_Parity()
    {
        var ours = await RunAsync("dotnet", $"\"{CliDll}\" help");
        var sdkVersion = (await RunAsync("dotnet", "--version")).Output.Trim();

        ours.ExitCode.ShouldBe(0);
        // Version line is derived from the hosting SDK, never hardcoded.
        ours.Output.ShouldStartWith($".NET SDK ({sdkVersion})");

        // Implemented commands are annotated from the command registry.
        ours.Output.Split('\n')
            .Single(line => line.Contains("Build a .NET project.")).ShouldEndWith("[implemented]");
        ours.Output.Split('\n')
            .Single(line => line.Contains("Build and run a .NET project output.")).ShouldEndWith("[implemented]");
        ours.Output.Split('\n')
            .Single(line => line.Contains("Clean build outputs of a .NET project.")).ShouldEndWith("[implemented]");
        ours.Output.Split('\n')
            .Single(line => line.Contains("Restore dependencies specified in a .NET project."))
            .ShouldEndWith("[implemented]");

        await Verify(CliComparisonHelper.Normalize(ours.Output)).UseTextForParameters("help");
    }

    [Fact]
    public async Task Build_Sln_Path_Output_Parity()
    {
        await EnsureSdkRestore(SlnFixturesSolutionPath);
        await CompareSolutionTerminal("build_sln_path", $"build \"{SlnFixturesSolutionPath}\"",
            $"build \"{SlnFixturesSolutionPath}\" --nologo --no-restore --no-incremental");
    }

    [Fact]
    public async Task Build_Slnx_Path_Output_Parity()
    {
        await EnsureSdkRestore(SlnxFixturesSolutionPath);
        await CompareSolutionTerminal("build_slnx_path", $"build \"{SlnxFixturesSolutionPath}\"",
            $"build \"{SlnxFixturesSolutionPath}\" --nologo --no-restore --no-incremental");
    }

    [Fact]
    public async Task Build_Directory_Holding_Solution_Parity()
    {
        // A directory named on the command line must resolve to the solution it contains, the same
        // way the working directory does. This regressed once: the folder was passed through to the
        // compiler untouched, which globbed every .cs beneath it, so building this directory compiled
        // the fixture's sources under an unrelated project's properties.
        var dir = Path.GetDirectoryName(SlnFixturesSolutionPath)!;
        await EnsureSdkRestore(SlnFixturesSolutionPath);

        var ours = await RunAsync("dotnet", $"\"{CliDll}\" build \"{dir}\"", RepoRoot);
        ours.ExitCode.ShouldBe(0, $"building a directory holding a solution failed.\n{ours.Output}");

        // Resolution evidence: all three projects in the solution were built, not just one of them.
        foreach (var project in new[] { "LibA", "LibB", "SlnApp" })
            ours.Output.ShouldContain(project);

        var dotnet = await RunAsync("dotnet", $"build \"{dir}\" --nologo --no-restore --no-incremental",
            RepoRoot, ("MSBUILDTERMINALLOGGER", "on"));
        ours.ExitCode.ShouldBe(dotnet.ExitCode, "exit code parity for build_dir_holding_solution");
    }

    [Fact]
    public async Task Build_Directory_Ambiguous_Parity()
    {
        // MSBuild distinguishes the two ambiguity diagnostics: MSB1011 for the working directory and
        // MSB1050 when the directory is named on the command line, echoing the path as given.
        // Unifying them was the easy mistake, so both spellings are pinned here.
        var dir = Path.Combine(Path.GetTempPath(), "sharp-ambiguous-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var name in new[] { "One.csproj", "Two.csproj" })
            {
                File.WriteAllText(Path.Combine(dir, name), """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                      </PropertyGroup>
                    </Project>
                    """);
            }

            var ours = await RunAsync("dotnet", $"\"{CliDll}\" build \"{dir}\"", RepoRoot);
            var dotnet = await RunAsync("dotnet", $"build \"{dir}\"", RepoRoot,
                ("MSBUILDTERMINALLOGGER", "on"));

            ours.ExitCode.ShouldNotBe(0);
            dotnet.ExitCode.ShouldNotBe(0);
            CliComparisonHelper.Normalize(ours.Output)
                .ShouldBe(CliComparisonHelper.Normalize(dotnet.Output), "MSB1050 parity for build_dir_ambiguous");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
        }
    }

    // Solution builds use the same terminal grammar for the per-project lines and the summary, but
    // the SDK's own solution output is build-state dependent (see NormalizeSolutionInteractive), so
    // parity is asserted on the deterministic verdict/exit-code surface, while a snapshot pins our
    // full terminal layout (per-project timing lines, relative paths, warning/summary grammar).
    private async Task CompareSolutionTerminal(string name, string oursArgs, string dotnetArgs)
    {
        if (!File.Exists(CliDll))
            throw new InvalidOperationException("Sharp.Cli is not built - run `dotnet build Sharp.sln` first.");

        var ours = await RunAsync("dotnet", $"\"{CliDll}\" {oursArgs}", RepoRoot);
        var dotnet = await RunAsync("dotnet", dotnetArgs, RepoRoot, ("MSBUILDTERMINALLOGGER", "on"));

        ours.ExitCode.ShouldBe(dotnet.ExitCode, $"exit code parity for {name}");
        CliComparisonHelper.NormalizeSolutionInteractive(ours.Output)
            .ShouldBe(CliComparisonHelper.NormalizeSolutionInteractive(dotnet.Output), $"verdict parity for {name}");

        await Verify(CliComparisonHelper.NormalizeInteractive(ours.Output)).UseTextForParameters(name);
    }

    private async Task CompareSolutionTerminalIn(string name, string workingDirectory, string oursArgs, string dotnetArgs)
    {
        if (!File.Exists(CliDll) || !Directory.Exists(workingDirectory))
            throw new InvalidOperationException($"{name} fixture not found - check {workingDirectory}");

        var ours = await RunAsync("dotnet", $"\"{CliDll}\" {oursArgs}", workingDirectory);
        var dotnet = await RunAsync("dotnet", dotnetArgs, workingDirectory, ("MSBUILDTERMINALLOGGER", "on"));

        ours.ExitCode.ShouldBe(dotnet.ExitCode, $"exit code parity for {name}");
        CliComparisonHelper.NormalizeSolutionInteractive(ours.Output)
            .ShouldBe(CliComparisonHelper.NormalizeSolutionInteractive(dotnet.Output), $"verdict parity for {name}");

        await Verify(CliComparisonHelper.NormalizeInteractive(ours.Output)).UseTextForParameters(name);
    }

    // `dotnet build --no-restore` fails with NETSDK1004 when an asset file is missing, so restore
    // the solution once first. Restore output is discarded; assets live in gitignored obj/.
    private async Task EnsureSdkRestore(string solutionPath)
        => await RunAsync("dotnet", $"restore \"{solutionPath}\"");

    [Fact]
    public Task Build_Slnx_NoPath_Cwd_Parity()
        => CompareSolutionTerminalIn("build_slnx_no_path_cwd", TestExampleProjectsDir, "build",
            "build --nologo --no-restore --no-incremental");

    [Fact]
    public Task Build_Csproj_NoPath_Cwd_Parity()
        => CompareTerminalIn("build_csproj_no_path_cwd", HelloParityFixtureDir, "build",
            "build --nologo --no-restore --no-incremental");

    private async Task CompareTerminalIn(string name, string workingDirectory, string oursArgs, string dotnetArgs)
    {
        if (!File.Exists(CliDll) || !Directory.Exists(workingDirectory))
            throw new InvalidOperationException($"{name} fixture not found - check {workingDirectory}");

        var ours = await RunAsync("dotnet", $"\"{CliDll}\" {oursArgs}", workingDirectory);
        var dotnet = await RunAsync("dotnet", dotnetArgs, workingDirectory, ("MSBUILDTERMINALLOGGER", "on"));
        var (nativeOutput, nativeExit) = (CliComparisonHelper.NormalizeInteractive(ours.Output), ours.ExitCode);
        var (sdkOutput, sdkExit) = (CliComparisonHelper.NormalizeInteractive(dotnet.Output), dotnet.ExitCode);

        nativeExit.ShouldBe(sdkExit, $"exit code parity for {name}");
        nativeOutput.ShouldBe(sdkOutput, $"output parity for {name}");

        string comparisonResult = CliComparisonHelper.GenerateSideBySide(
            nativeOutput,
            sdkOutput,
            $"NATIVE {oursArgs}",
            $"DOTNET {dotnetArgs}");

        await Verify(comparisonResult).UseTextForParameters(name);
    }

    // Our `build` always prints the SDK terminal-logger summary (build seconds included, project path
    // relative to CWD), exactly like `dotnet build` on a real terminal. Force the SDK's terminal
    // logger on (it works redirected) and compare against it, from the repo root so both render the
    // path identically relative to CWD. Run-varying seconds are normalized to a `<secs>` token.
    private async Task CompareTerminal(string name, string oursArgs, string dotnetArgs)
    {
        if (!File.Exists(CliDll))
            throw new InvalidOperationException("Sharp.Cli is not built - run `dotnet build Sharp.sln` first.");

        var ours = await RunAsync("dotnet", $"\"{CliDll}\" {oursArgs}", RepoRoot);
        var dotnet = await RunAsync("dotnet", dotnetArgs, RepoRoot, ("MSBUILDTERMINALLOGGER", "on"));
        var (nativeOutput, nativeExit) = (CliComparisonHelper.NormalizeInteractive(ours.Output), ours.ExitCode);
        var (sdkOutput, sdkExit) = (CliComparisonHelper.NormalizeInteractive(dotnet.Output), dotnet.ExitCode);

        nativeExit.ShouldBe(sdkExit, $"exit code parity for {name}");
        nativeOutput.ShouldBe(sdkOutput, $"output parity for {name}");

        string comparisonResult = CliComparisonHelper.GenerateSideBySide(
            nativeOutput,
            sdkOutput,
            $"NATIVE {oursArgs}",
            $"DOTNET {dotnetArgs}");

        await Verify(comparisonResult).UseTextForParameters(name);
    }

    private async Task Compare(string name, string oursArgs, string dotnetArgs)
    {
        if (!File.Exists(CliDll))
            throw new InvalidOperationException("Sharp.Cli is not built - run `dotnet build Sharp.sln` first.");

        var ours = await RunAsync("dotnet", $"\"{CliDll}\" {oursArgs}");
        var dotnet = await RunAsync("dotnet", dotnetArgs);
        var (nativeOutput, nativeExit) = (CliComparisonHelper.Normalize(ours.Output), ours.ExitCode);
        var (sdkOutput, sdkExit) = (CliComparisonHelper.Normalize(dotnet.Output), dotnet.ExitCode);

        nativeExit.ShouldBe(sdkExit, $"exit code parity for {name}");
        nativeOutput.ShouldBe(sdkOutput, $"output parity for {name}");

        string comparisonResult = CliComparisonHelper.GenerateSideBySide(
            nativeOutput,
            sdkOutput,
            $"NATIVE {oursArgs}",
            $"DOTNET {dotnetArgs}");

        await Verify(comparisonResult).UseTextForParameters(name);
    }
}