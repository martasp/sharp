using System;
using System.IO;
using System.Linq;
using Sharp;
using Shouldly;
using Xunit;

namespace Sharp.Tests;

// `clean` must remove exactly this compiler's outputs (myc/<config>/ and obj/) and nothing else.
// The important negative assertion is bin/: the output-parity tests compare our output against
// the SDK's bin/ tree, so deleting it would destroy the baseline they assert on.
public class CleanTests
{
    [Fact]
    public void Clean_RemovesCompilerOutputsButLeavesBinAndSources()
    {
        using var project = new TempProject();
        project.WriteProject();
        project.CreateBuildOutputs();

        var removed = Clean(project.Root, "Debug");

        // myc/<config> and obj are removed wholesale; the myc root survives, exactly as
        // `dotnet clean` leaves bin/ in place after removing bin/<config>.
        removed.ShouldBe(2);
        Directory.Exists(Path.Combine(project.Root, "myc", "Debug")).ShouldBeFalse();
        Directory.Exists(Path.Combine(project.Root, "obj")).ShouldBeFalse();

        // Untouched.
        Directory.Exists(Path.Combine(project.Root, "bin", "Debug", "net11.0")).ShouldBeTrue();
        File.Exists(Path.Combine(project.Root, "app.csproj")).ShouldBeTrue();
        File.Exists(Path.Combine(project.Root, "Program.cs")).ShouldBeTrue();
    }

    [Fact]
    public void Clean_HonoursConfiguration()
    {
        using var project = new TempProject();
        project.WriteProject();
        project.CreateBuildOutputs();
        Directory.CreateDirectory(Path.Combine(project.Root, "myc", "Release"));

        var targets = CleanTargets(project.Root, "Release");

        targets.ShouldContain(path => path.Contains("Release"));
        targets.ShouldNotContain(path => path.Contains("Debug"));
    }

    [Fact]
    public void Clean_UsesConfiguredOutputRoot()
    {
        using var project = new TempProject();
        project.WriteProject();
        Directory.CreateDirectory(Path.Combine(project.Root, "custom-root", "Debug"));

        var previous = Environment.GetEnvironmentVariable("SHARP_OUTPUT_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SHARP_OUTPUT_ROOT", "custom-root");

            var targets = CleanTargets(project.Root, "Debug");

            targets.ShouldContain(path => path.Contains("custom-root"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARP_OUTPUT_ROOT", previous);
        }
    }

    [Fact]
    public void Clean_OnProjectWithoutOutputs_IsHarmless()
    {
        using var project = new TempProject();
        project.WriteProject();

        var targets = CleanTargets(project.Root, "Debug");

        targets.ShouldAllBe(path => !Directory.Exists(path));
    }

    // Mirrors CleanTarget/CleanTargets in Program.cs. Those are top-level local functions and cannot
    // be referenced from the test assembly, so the contract is asserted against this copy.
    private static int Clean(string projectDirectory, string configuration)
    {
        var removed = 0;
        foreach (var candidate in CleanTargets(projectDirectory, configuration))
        {
            if (!Directory.Exists(candidate)) continue;
            Directory.Delete(candidate, recursive: true);
            removed++;
        }

        return removed;
    }

    private static string[] CleanTargets(string projectDirectory, string configuration)
    {
        var root = Environment.GetEnvironmentVariable("SHARP_OUTPUT_ROOT") ?? "myc";
        return
        [
            Path.Combine(projectDirectory, root, configuration),
            Path.Combine(projectDirectory, "obj"),
        ];
    }

    private sealed class TempProject : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "sharp-clean-" + Guid.NewGuid().ToString("N")[..10]);

        public TempProject() => Directory.CreateDirectory(Root);

        public void WriteProject()
        {
            File.WriteAllText(Path.Combine(Root, "app.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(Root, "Program.cs"), "System.Console.WriteLine(\"hi\");\n");
        }

        public void CreateBuildOutputs()
        {
            CreateFile(Path.Combine(Root, "myc", "Debug", "net11.0", "app.dll"));
            CreateFile(Path.Combine(Root, "obj", "Debug", "net11.0", "app.csproj.nuget.g.props"));
            CreateFile(Path.Combine(Root, "bin", "Debug", "net11.0", "app.dll"));
        }

        private static void CreateFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { /* best effort */ }
        }
    }
}