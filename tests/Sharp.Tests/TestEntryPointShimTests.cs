using System.IO;
using Shouldly;
using Xunit;
using Sharp;

namespace Sharp.Tests;

[Collection("Sequential Tests 4")]
public class TestEntryPointShimTests
{
    [Fact]
    public void Build_ExeTestProjectWithoutMain_CompilesWithGeneratedEntryPoint()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "opencode-shim-test");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "ShimTest.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projectDir, "Class1.cs"), """
            public class Class1
            {
                public static int Value = 42;
            }
            """);

        try
        {
            var build = SharpCompiler.Build(projectDir);

            build.Success.ShouldBeTrue(build.Errors);
            File.Exists(Path.Combine(projectDir, "myc", "Debug", "net10.0", "ShimTest.dll")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }
}