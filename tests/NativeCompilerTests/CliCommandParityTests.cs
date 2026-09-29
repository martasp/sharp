using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Shouldly;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace NativeCompilerTests;

// CLI command parity with the .NET SDK. Every command our CLI advertises (`build`, `run`, `help`,
// `--version`) is executed against the fixture and the captured output is compared byte-for-byte with
// the matching `dotnet` invocation after shared normalization (path tokens + restore/time noise).
// The captured outputs are also rendered side-by-side into a Verify snapshot, so any drift is visible
// in review. This is the contract: where our CLI says it behaves like `dotnet`, it must match.
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
        Path.Combine(RepoRoot, "src", "NativeCompilerCLI", "bin", "Debug", "net10.0", "NativeCompilerCLI.dll");

    private static async Task<(string Output, int ExitCode)> RunAsync(
        string fileName, string arguments, string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
        };

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
        => Compare("build_success", $"build \"{HelloParityFixtureDir}\"",
            $"build \"{HelloParityFixtureDir}\" --nologo --no-restore --no-incremental");

    [Fact]
    public Task Build_Failure_Output_Parity()
        => Compare("build_failure", $"build \"{FailingFixtureDir}\"",
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
    public Task Help_Output_Parity()
        => Compare("help", "help", "help");

    [Fact]
    public async Task Build_Sln_Path_Output_Parity()
    {
        await EnsureSdkRestore(SlnFixturesSolutionPath);
        await Compare("build_sln_path", $"build \"{SlnFixturesSolutionPath}\"",
            $"build \"{SlnFixturesSolutionPath}\" --nologo --no-restore --no-incremental");
    }

    [Fact]
    public async Task Build_Slnx_Path_Output_Parity()
    {
        await EnsureSdkRestore(SlnxFixturesSolutionPath);
        await Compare("build_slnx_path", $"build \"{SlnxFixturesSolutionPath}\"",
            $"build \"{SlnxFixturesSolutionPath}\" --nologo --no-restore --no-incremental");
    }

    // `dotnet build --no-restore` fails with NETSDK1004 when an asset file is missing, so restore
    // the solution once first. Restore output is discarded; assets live in gitignored obj/.
    private async Task EnsureSdkRestore(string solutionPath)
        => await RunAsync("dotnet", $"restore \"{solutionPath}\"");

    [Fact]
    public Task Build_Slnx_NoPath_Cwd_Parity()
        => CompareIn("build_slnx_no_path_cwd", TestExampleProjectsDir, "build",
            "build --nologo --no-restore --no-incremental");

    [Fact]
    public Task Build_Csproj_NoPath_Cwd_Parity()
        => CompareIn("build_csproj_no_path_cwd", HelloParityFixtureDir, "build",
            "build --nologo --no-restore --no-incremental");

    private async Task CompareIn(string name, string workingDirectory, string oursArgs, string dotnetArgs)
    {
        if (!File.Exists(SlnxFixturesSolutionPath) || !Directory.Exists(workingDirectory))
            throw new InvalidOperationException($"{name} fixture not found - check {workingDirectory}");

        var ours = await RunAsync("dotnet", $"\"{CliDll}\" {oursArgs}", workingDirectory);
        var dotnet = await RunAsync("dotnet", dotnetArgs, workingDirectory);
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

    private async Task Compare(string name, string oursArgs, string dotnetArgs)
    {
        if (!File.Exists(CliDll))
            throw new InvalidOperationException("NativeCompilerCLI is not built - run `dotnet build NativeCompiler.sln` first.");

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