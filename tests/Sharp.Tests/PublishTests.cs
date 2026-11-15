using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Dotnet11Dependencies;
using Sharp;
using Shouldly;
using Xunit;

namespace Sharp.Tests;

public class PublishTests
{
    [Fact]
    public void ManagedPublish_CopiesRunnableFrameworkDependentOutput()
    {
        using var project = new TempProject();
        project.WriteProject(publishAot: false);

        var result = SharpCompiler.Publish(new PublishOptions(project.Root));

        result.Success.ShouldBeTrue(result.Errors);
        var publishDir = Path.Combine(project.Root, "myc", "Release", "net11.0", "publish");
        result.PublishDirectory.ShouldBe(publishDir);
        File.Exists(Path.Combine(publishDir, "app.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(publishDir, "app.runtimeconfig.json")).ShouldBeTrue();

        var appHost = Path.Combine(publishDir, "app");
        File.Exists(appHost).ShouldBeTrue();

        var run = Run(appHost);
        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("Hello from publish");
    }

    [Fact]
    public void PublishAotWithoutRuntimeIdentifier_ReportsSdkError()
    {
        using var project = new TempProject();
        project.WriteProject(publishAot: true);

        var result = SharpCompiler.Publish(new PublishOptions(project.Root));

        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain(
            "RuntimeIdentifier is required for native compilation. " +
            "Try running dotnet publish with the -r option value specified.");
    }

    [Fact]
    public void PublishWithRuntimeIdentifierWithoutAot_IsRejected()
    {
        using var project = new TempProject();
        project.WriteProject(publishAot: false);

        var result = SharpCompiler.Publish(new PublishOptions(project.Root, RuntimeIdentifier: "linux-x64"));

        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain("not supported yet");
    }

    [Fact]
    public void PublishWithRuntimeIdentifierInProjectWithoutAot_IsRejected()
    {
        using var project = new TempProject();
        project.WriteProject(publishAot: false, runtimeIdentifier: "linux-x64");

        var result = SharpCompiler.Publish(new PublishOptions(project.Root));

        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain("not supported yet");
    }

    [Fact]
    public void IlcArg_ExpandsProjectDirectoryAndNormalizesSeparators()
    {
        using var project = new TempProject();
        project.WriteProject(
            publishAot: true,
            ilcArg: "--descriptor=$(MSBuildProjectDirectory)\\AotAnalyzerResources.xml");

        var loaded = ProjectLoader.Load(
            project.Root,
            new BuildOptions("Release", "linux-x64"));

        loaded.IlcArgs.ShouldContain(
            $"--descriptor={project.Root}{Path.DirectorySeparatorChar}AotAnalyzerResources.xml");
    }

    [Fact]
    public void SelfHostedPayloadOwner_EmbedsPayloadResource()
    {
        using var project = new TempProject("Dotnet11Dependencies");
        project.WriteProject(publishAot: false);

        var result = SharpCompiler.Build(project.Root);

        result.Success.ShouldBeTrue(result.Errors);
        var dll = Path.Combine(
            project.Root, "myc", "Debug", "net11.0", "Dotnet11Dependencies.dll");
        File.Exists(dll).ShouldBeTrue();

        var context = new AssemblyLoadContext("payload-owner-test", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(dll);
            assembly.GetManifestResourceNames().ShouldContain(Payloads.ZipResourceName);

            using var payload = assembly.GetManifestResourceStream(Payloads.ZipResourceName);
            payload.ShouldNotBeNull();
            payload!.Length.ShouldBeGreaterThan(1_000_000);
        }
        finally
        {
            context.Unload();
        }

        Directory.EnumerateFiles(
                project.Root, "embedded-refs.*.zip", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void PublishAotNonLinuxRid_IsRejected()
    {
        using var project = new TempProject();
        project.WriteProject(publishAot: true);

        var result = SharpCompiler.Publish(new PublishOptions(project.Root, RuntimeIdentifier: "win-x64"));

        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain("only linux-x64");
    }

    [Fact]
    public void NativeAotPublish_ProducesRunnableBinary()
    {
        if (!OperatingSystem.IsLinux()
            || RuntimeInformation.ProcessArchitecture != Architecture.X64
            || !NativeAotToolchainIsCached())
            return;

        using var project = new TempProject();
        project.WriteProject(publishAot: true);

        // Simulate a previous default publish under the build output. Its binary/.dbg must never be
        // copied back into the next publish (that recursed into linux-x64/publish/linux-x64/...).
        var buildOutput = Path.Combine(project.Root, "myc", "Release", "net11.0");
        var stalePublish = Path.Combine(buildOutput, "linux-x64", "publish");
        Directory.CreateDirectory(stalePublish);
        File.WriteAllText(Path.Combine(stalePublish, "stale.txt"), "stale");

        var result = SharpCompiler.Publish(new PublishOptions(
            project.Root,
            RuntimeIdentifier: "linux-x64"));

        result.Success.ShouldBeTrue(result.Errors);
        var publishDir = Path.Combine(buildOutput, "linux-x64", "publish");
        var binary = Path.Combine(publishDir, "app");
        File.Exists(binary).ShouldBeTrue();
        File.Exists(Path.Combine(publishDir, "linux-x64", "publish", "stale.txt")).ShouldBeFalse();

        var responseFile = Path.Combine(
            project.Root, "obj", "Release", "net11.0", "linux-x64", "native", "app.ilc.rsp");
        var ilcArguments = File.ReadAllText(responseFile);
        ilcArguments.ShouldContain("--targetos:linux");
        ilcArguments.ShouldContain("--targetarch:x64");
        ilcArguments.ShouldContain("--dehydrate");

        var header = File.ReadAllBytes(binary).Take(4).ToArray();
        header.ShouldBe(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' });

        var run = Run(binary);
        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("Hello from publish");
    }

    private static bool NativeAotToolchainIsCached()
    {
        var packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var version = EmbeddedSdkRefs.RuntimeVersion;

        return Directory.Exists(Path.Combine(
                   packages, "runtime.linux-x64.microsoft.dotnet.ilcompiler", version))
               && Directory.Exists(Path.Combine(
                   packages, "microsoft.netcore.app.runtime.nativeaot.linux-x64", version))
               && ToolExists("clang")
               && (ToolExists("llvm-objcopy") || ToolExists("objcopy"));
    }

    private static bool ToolExists(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Any(directory => File.Exists(Path.Combine(directory, name)));

    private static (int ExitCode, string Output) Run(string fileName)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(fileName)!,
        };

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
    }

    private sealed class TempProject : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "sharp-publish-" + Guid.NewGuid().ToString("N")[..10]);

        private readonly string _projectName;

        public TempProject(string projectName = "app")
        {
            _projectName = projectName;
            Directory.CreateDirectory(Root);
        }

        public string ProjectPath => Path.Combine(Root, _projectName + ".csproj");

        public void WriteProject(
            bool publishAot,
            string? runtimeIdentifier = null,
            string? ilcArg = null)
        {
            var rid = runtimeIdentifier is null
                ? ""
                : $"<RuntimeIdentifier>{runtimeIdentifier}</RuntimeIdentifier>";
            var items = ilcArg is null
                ? ""
                : $"<ItemGroup><IlcArg Include=\"{ilcArg}\" /></ItemGroup>";
            File.WriteAllText(ProjectPath, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                    <PublishAot>{publishAot.ToString().ToLowerInvariant()}</PublishAot>
                    {rid}
                  </PropertyGroup>
                  {items}
                </Project>
                """);
            File.WriteAllText(Path.Combine(Root, "Program.cs"),
                "System.Console.WriteLine(\"Hello from publish\");\n");
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
