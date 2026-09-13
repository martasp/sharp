using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Threading.Tasks;
using Shouldly;
using VerifyXunit;
using Xunit;
using NativeCompiler;
using static VerifyXunit.Verifier;

namespace NativeCompilerTests;

[CollectionDefinition("Sequential Tests 4", DisableParallelization = true)]
[Collection("Sequential Tests 4")]
public class SolutionTests
{
    public static string RepoRoot =
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));

    public static string RootSln = Path.Combine(RepoRoot, "NativeCompiler.sln");

    public static string MyCompilerSln =
        Path.Combine(RepoRoot, "testProjects", "MyCompiler", "MyCompiler.sln");

    public static string SlnFixturesDir =
        Path.Combine(RepoRoot, "tests", "TestExampleProjects", "SlnFixtures");

    public static string SlnFixturesSln = Path.Combine(SlnFixturesDir, "SlnFixtures.sln");

    public static string SlnxFixturesSln = Path.Combine(RepoRoot, "tests", "TestExampleProjects", "SlnxFixtures.slnx");

    [Fact]
    public void Parse_RootSolution_ShouldListOnlyCsprojProjects()
    {
        var solution = SolutionFile.Parse(RootSln);
        solution.Projects.Count.ShouldBe(4);
        solution.Projects.ShouldAllBe(project => project.ProjectPath.EndsWith(".csproj"));
        solution.Projects.ShouldContain(project => project.Name == "NativeCompiler");
        solution.Projects.ShouldContain(project => project.Name == "NativeCompilerCLI");
        solution.Projects.ShouldContain(project => project.Name == "SimplePlayground");
        solution.Projects.ShouldContain(project => project.Name == "DotnetRuntime10");
        solution.Projects.ShouldNotContain(project => project.Name == "NativeCompilerTests");
        solution.Projects.ShouldNotContain(project => project.Name == "DotnetRuntimeHostExe");
    }

    [Fact]
    public Task Parse_RootSolution_Snapshot()
    {
        var solution = SolutionFile.Parse(RootSln);
        var view = solution.Projects
            .Select(project => new
            {
                project.Name,
                project.ProjectTypeGuid,
                project.ProjectRelativePath,
                FlatPath = project.ProjectPath.Replace('\\', '/')
            })
            .OrderBy(entry => entry.Name);

        return Verify(view);
    }

    [Fact]
    public void Parse_Slnx_ShouldListOnlyCsprojProjects()
    {
        var solution = SolutionFile.Parse(SlnxFixturesSln);
        solution.Projects.Count.ShouldBe(3);
        solution.Projects.Select(project => project.Name).ShouldBe(new[] { "LibA", "LibB", "SlnApp" });
        solution.Projects[0].ProjectPath.ShouldEndWith("LibA.csproj");
        solution.Projects.ShouldNotContain(project => project.Name == "SkipMe");
    }

    [Fact]
    public void Parse_Slnx_ShouldSkipBuildFalseProjects()
    {
        var document = XDocument.Load(SlnxFixturesSln);
        document.Descendants("Project")
            .Any(element => element.Element("Build")?.Attribute("Project")?.Value == "false")
            .ShouldBeTrue("fixture contains a Build=false project to exercise the skip");

        var solution = SolutionFile.Parse(SlnxFixturesSln);
        solution.Projects.ShouldNotContain(project => project.Name == "SkipMe");
    }

    [Fact]
    public void Parse_MyCompilerSolution_Snapshot()
    {
        var solution = SolutionFile.Parse(MyCompilerSln);
        solution.Projects.Count.ShouldBe(1);
        solution.Projects[0].Name.ShouldBe("MSCompiler");
        solution.Projects[0].ProjectPath.ShouldEndWith("MSCompiler.csproj");
    }

    [Fact]
    public void BuildSolution_SlnFixtures_BuildsDependenciesFirstAndSucceeds()
    {
        var result = NativeCompiler.NativeCompiler.BuildSolution(SlnFixturesSln);

        result.Errors.ShouldBeEmpty();
        result.Success.ShouldBeTrue(string.Join("\n", result.Errors));

        var names = result.Projects.Select(project => project.ProjectName).ToList();
        names.ShouldBe(new[] { "LibA", "LibB", "SlnApp" });

        result.Projects.All(project => project.Result.Success).ShouldBeTrue();

        var sharpBin = Path.Combine(SlnFixturesDir, "SlnApp", "bin", "sharpbin");
        Directory.Exists(sharpBin).ShouldBeTrue();
        File.Exists(Path.Combine(sharpBin, "SlnApp.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(sharpBin, "LibB.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(sharpBin, "LibA.dll")).ShouldBeTrue();
    }

    [Fact]
    public void Build_SlnAppFolder_ResolvesTransitiveReferences()
    {
        var build = NativeCompiler.NativeCompiler.Build(Path.Combine(SlnFixturesDir, "SlnApp"));

        build.Success.ShouldBeTrue(build.Errors);

        var sharpBin = Path.Combine(SlnFixturesDir, "SlnApp", "bin", "sharpbin");
        File.Exists(Path.Combine(sharpBin, "SlnApp.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(sharpBin, "LibB.dll")).ShouldBeTrue();
        File.Exists(Path.Combine(sharpBin, "LibA.dll")).ShouldBeTrue();
    }
}