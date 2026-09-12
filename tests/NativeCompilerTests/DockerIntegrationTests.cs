using System;
using System.IO;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Shouldly;
using Xunit;

namespace NativeCompilerTests;

[CollectionDefinition("Sequential Tests 4", DisableParallelization = true)]
[Collection("Sequential Tests 4")]
public class DockerIntegrationTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string SrcDir = Path.Combine(RepoRoot, "src");
    private static readonly string NugetConfig = Path.Combine(RepoRoot, "nuget.config");

    private const string HelloCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    private static string CreateHelloProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hello-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Program.cs"), "Console.WriteLine(\"Hello from NativeCompiler!\");");
        File.WriteAllText(Path.Combine(dir, "hello.csproj"), HelloCsproj);
        return dir;
    }

    [Fact]
    public async Task Linux_BuildAndCompileHelloWorld()
    {
        await using var container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/dotnet/sdk:10.0")
            .WithEntrypoint("/bin/bash", "-c")
            .WithCommand("while true; do sleep 1000; done")
            .WithResourceMapping(SrcDir, "/src/src")
            .WithResourceMapping(NugetConfig, "/src/nuget.config")
            .WithResourceMapping(CreateHelloProject(), "/app/hello")
            .Build();

        await container.StartAsync();

        await container.ExecAsync(
            new[] { "bash", "-c", "find /src/src -type d -name obj -exec rm -rf {} + 2>/dev/null; true" });

        var build = await container.ExecAsync(
            new[] { "dotnet", "build", "/src/src/NativeCompilerCLI", "-c", "Release" });
        build.ExitCode.ShouldBe(0, $"Build failed.\n{build.Stdout}\n{build.Stderr}");
        build.Stdout.ShouldContain("Build succeeded");

        var compile = await container.ExecAsync(
            new[] { "dotnet", "/src/src/NativeCompilerCLI/bin/Release/net10.0/NativeCompilerCLI.dll", "/app/hello" });
        compile.ExitCode.ShouldBe(0, $"Compilation failed.\n{compile.Stdout}\n{compile.Stderr}");
        compile.Stdout.ShouldContain("Compiled hello successfully");

        var run = await container.ExecAsync(
            new[] { "dotnet", "/app/hello/bin/sharpbin/hello.dll" });
        run.ExitCode.ShouldBe(0, $"Run failed.\n{run.Stderr}");
        run.Stdout.Trim().ShouldBe("Hello from NativeCompiler!");
    }

    [Fact]
    public async Task Windows_BuildAndCompileHelloWorld()
    {
        if (!await IsWindowsContainersMode())
        {
            // Skip: Windows containers require Docker in Windows containers mode.
            return;
        }

        await using var container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/dotnet/sdk:10.0-nanoserver-ltsc2022")
            .WithEntrypoint("cmd", "/c")
            .WithCommand("ping -t 127.0.0.1")
            .WithResourceMapping(SrcDir, "C:\\src\\src")
            .WithResourceMapping(NugetConfig, "C:\\src\\nuget.config")
            .WithResourceMapping(CreateHelloProject(), "C:\\app\\hello")
            .Build();

        await container.StartAsync();

        await container.ExecAsync(new[]
        {
            "cmd", "/c", "for /d /r C:\\src\\src %%d in (obj) do @if exist \"%%d\" rd /s /q \"%%d\""
        });

        var build = await container.ExecAsync(
            new[] { "dotnet", "build", "C:\\src\\src\\NativeCompilerCLI", "-c", "Release" });
        build.ExitCode.ShouldBe(0, $"Build failed.\n{build.Stdout}\n{build.Stderr}");
        build.Stdout.ShouldContain("Build succeeded");

        var compile = await container.ExecAsync(
            new[] { "dotnet", "C:\\src\\src\\NativeCompilerCLI\\bin\\Release\\net10.0\\NativeCompilerCLI.dll", "C:\\app\\hello" });
        compile.ExitCode.ShouldBe(0, $"Compilation failed.\n{compile.Stdout}\n{compile.Stderr}");
        compile.Stdout.ShouldContain("Compiled hello successfully");

        var run = await container.ExecAsync(
            new[] { "dotnet", "C:\\app\\hello\\bin\\sharpbin\\hello.dll" });
        run.ExitCode.ShouldBe(0, $"Run failed.\n{run.Stderr}");
        run.Stdout.Trim().ShouldBe("Hello from NativeCompiler!");
    }

    private static async Task<bool> IsWindowsContainersMode()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info --format '{{.OperatingSystem}}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return false;
            await process.WaitForExitAsync();
            var output = await process.StandardOutput.ReadToEndAsync();
            return output.Contains("Windows") || output.Contains("nanoserver");
        }
        catch
        {
            return false;
        }
    }
}