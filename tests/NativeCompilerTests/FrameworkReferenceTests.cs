using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;
using NativeCompiler;

namespace NativeCompilerTests;

[Collection("Sequential Tests 4")]
public class FrameworkReferenceTests
{
    public static string RepoRoot =
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));

    public static string FixtureDir =
        Path.Combine(RepoRoot, "tests", "TestExampleProjects", "FrameworkRefFixture");

    [Fact]
    public void Parse_Csproj_ExtractsFrameworkReferences()
    {
        var path = Path.Combine(Path.GetTempPath(), "opencode-fr-test", "sample.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <FrameworkReference Include="Microsoft.NETCore.App" />
              </ItemGroup>
            </Project>
            """);

        try
        {
            var project = XML.Project.Parse(path);
            var names = project.ItemGroup
                .SelectMany(itemGroup => itemGroup.FrameworkReferences)
                .Select(reference => reference.Include)
                .ToList();
            names.ShouldBe(new[] { "Microsoft.AspNetCore.App", "Microsoft.NETCore.App" });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Build_FrameworkRefFixture_ResolvesAspNetCoreApp()
    {
        var build = NativeCompiler.NativeCompiler.Build(FixtureDir);

        build.Success.ShouldBeTrue(build.Errors);
        File.Exists(Path.Combine(FixtureDir, "bin", "sharpbin", "FrameworkRefFixture.dll")).ShouldBeTrue();
    }
}