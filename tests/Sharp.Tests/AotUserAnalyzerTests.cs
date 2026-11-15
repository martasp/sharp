using System;
using System.Diagnostics;
using System.IO;
using Shouldly;
using Xunit;

namespace Sharp.Tests;

[Collection("Sequential Tests 4")]
public class AotUserAnalyzerTests
{
    private static readonly string Sharp = Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
        "src", "Sharp.Cli", "bin", "Release", "net11.0",
        OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", "publish",
        OperatingSystem.IsWindows() ? "Sharp.Cli.exe" : "Sharp.Cli");

    [Fact]
    public void AotBuild_RunsUserSpaceSourceGenerator()
    {
        if (!OperatingSystem.IsLinux())
            return;

        if (!File.Exists(Sharp))
            throw new InvalidOperationException(
                $"Missing AOT seed at '{Sharp}'. Run publish.sh first.");

        var root = Path.Combine(Path.GetTempPath(), "sharp-aot-user-analyzer", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Generator"));
            Directory.CreateDirectory(Path.Combine(root, "Consumer"));

            File.WriteAllText(Path.Combine(root, "Generator", "Generator.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.12.0" />
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(root, "Generator", "ProbeGenerator.cs"), """
                using System.Linq;
                using Microsoft.CodeAnalysis;

                [Generator]
                public sealed class ProbeGenerator : ISourceGenerator
                {
                    public void Initialize(GeneratorInitializationContext context) { }

                    public void Execute(GeneratorExecutionContext context)
                    {
                        var file = context.AdditionalFiles.FirstOrDefault()?.GetText()?.ToString() ?? "missing";
                        context.AnalyzerConfigOptions.GlobalOptions.TryGetValue(
                            "build_property.RootNamespace", out var rootNamespace);
                        if (file.Trim() != "ok" || rootNamespace != "Consumer")
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "PROBE001", "Probe", "Probe additional file={0}, root={1}",
                                    "Probe", DiagnosticSeverity.Error, true),
                                Location.None, file.Trim(), rootNamespace ?? "missing"));
                        }

                        context.AddSource("Probe.Generated.g.cs",
                            "internal static class ProbeGenerated { public const string Value = \"ok\"; }");
                    }
                }
                """);

            File.WriteAllText(Path.Combine(root, "Consumer", "Consumer.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <AdditionalFiles Include="probe.txt" />
                    <ProjectReference Include="../Generator/Generator.csproj"
                                      OutputItemType="Analyzer"
                                      ReferenceOutputAssembly="false" />
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(root, "Consumer", "probe.txt"), "ok");
            File.WriteAllText(Path.Combine(root, "Consumer", "Consumer.cs"), """
                public static class Consumer
                {
                    public static string Value => ProbeGenerated.Value;
                }
                """);

            var restore = Run(Sharp, "restore Consumer", root);
            restore.Exit.ShouldBe(0, restore.Output);

            var build = Run(Sharp, "build Consumer", root);
            build.Exit.ShouldBe(0, build.Output);

            File.WriteAllText(Path.Combine(root, "Consumer", "probe.txt"), "bad");
            var failingBuild = Run(Sharp, "build Consumer", root);
            failingBuild.Exit.ShouldNotBe(0, failingBuild.Output);
            failingBuild.Output.ShouldContain("PROBE001");
            failingBuild.Output.ShouldContain("Probe additional file=bad, root=Consumer");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static (int Exit, string Output) Run(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
