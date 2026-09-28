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

    static string CliDll =>
        Path.Combine(RepoRoot, "src", "NativeCompilerCLI", "bin", "Debug", "net10.0", "NativeCompilerCLI.dll");

    private static async Task<(string Output, int ExitCode)> RunAsync(string fileName, string arguments)
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