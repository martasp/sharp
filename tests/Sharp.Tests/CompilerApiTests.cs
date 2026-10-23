using System;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace Sharp.Tests;

public class CompilerApiTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string MyCompilerSrc = Path.Combine(RepoRoot, "testProjects", "MyCompiler");

    private static string StageHelloProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "compilerapi-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(MyCompilerSrc, "Program.cs"), Path.Combine(dir, "Program.cs"));
        File.Copy(Path.Combine(MyCompilerSrc, "MSCompiler.csproj"), Path.Combine(dir, "MSCompiler.csproj"));
        return dir;
    }

    [Fact]
    public void Build_ReturnsStructuredResultWithCompiledArtifact()
    {
        var projectDir = StageHelloProject();
        var result = SharpCompiler.Build(projectDir);

        result.Success.ShouldBeTrue(result.Errors);
        result.Errors.ShouldBeEmpty();
        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
        result.Outputs.ShouldNotBeEmpty();
        result.Outputs.ShouldContain(line => line.StartsWith("Merged default references count:"));
        result.Outputs.ShouldContain($"Compiled {result.Project.ProjectName} successfully");
        File.Exists(result.Project.DllPathName).ShouldBeTrue($"Expected dll at {result.Project.DllPathName}");
    }

    [Fact]
    public void Build_DottedProjectName_ProducesFullyNamedAssembly()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "dottedname-" + Path.GetRandomFileName());
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "Dotted.Name.csproj"),
            File.ReadAllText(Path.Combine(MyCompilerSrc, "MSCompiler.csproj"))
                .Replace("OutputType>Exe", "OutputType>Library"));
        File.WriteAllText(Path.Combine(projectDir, "DottedThing.cs"),
            "namespace Dotted.Name; public class DottedThing { public int Value => 42; }");

        var result = SharpCompiler.Build(projectDir);

        result.Success.ShouldBeTrue(result.Errors);
        StringComparer.Ordinal.Equals(Path.GetFileName(result.Project.DllPathName), "Dotted.Name.dll")
            .ShouldBeTrue($"Expected dotted assembly name, got: {result.Project.DllPathName}");
        File.Exists(result.Project.DllPathName).ShouldBeTrue($"Expected dll at {result.Project.DllPathName}");
        var dotless = Path.Combine(Path.GetDirectoryName(result.Project.DllPathName), "Dotted.dll");
        File.Exists(dotless).ShouldBeFalse("Dotted project name must not be mangled into Dotted.dll");
    }
}