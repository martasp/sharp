using System;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace NativeCompilerTests;

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
        var result = NativeCompiler.NativeCompiler.Build(projectDir);

        result.Success.ShouldBeTrue(result.Errors);
        result.Errors.ShouldBeEmpty();
        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
        result.Outputs.ShouldNotBeEmpty();
        result.Outputs.ShouldContain(line => line.StartsWith("Merged default references count:"));
        result.Outputs.ShouldContain($"Compiled {result.Project.ProjectName} successfully");
        File.Exists(result.Project.DllPathName).ShouldBeTrue($"Expected dll at {result.Project.DllPathName}");
    }
}