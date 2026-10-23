using System.Collections.Generic;
using System.IO;
using Shouldly;
using Xunit;
using Sharp;

namespace Sharp.Tests;

[Collection("Sequential Tests 4")]
public class DirectoryBuildPropsTests
{
    private static string TempRoot(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Evaluate_WalksUpAndFollowsImports()
    {
        var root = TempRoot("opencode-dbp-eval");

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "props"));
            File.WriteAllText(Path.Combine(root, "props", "TargetFrameworks.props"), """
                <Project>
                  <PropertyGroup>
                    <DefaultTargetFramework>net10.0</DefaultTargetFramework>
                    <CommonTargetFrameworks>$(DefaultTargetFramework)</CommonTargetFrameworks>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project>
                  <Import Project="props\TargetFrameworks.props" />
                  <PropertyGroup>
                    <TargetFrameworks>$(CommonTargetFrameworks)</TargetFrameworks>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <LangVersion>12.0</LangVersion>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);

            var projectDir = Path.Combine(root, "A", "B"); // walks up from a nested project dir
            Directory.CreateDirectory(projectDir);

            var result = BuildPropsEvaluator.Evaluate(projectDir);

            result.TargetFramework.ShouldBe("net10.0");
            result.ImplicitUsings.ShouldBeTrue();
            result.Nullable.ShouldBe("enable");
            result.LangVersion.ShouldBe("12.0");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>", true)]
    [InlineData("<EnableConfigurationBindingGenerator>false</EnableConfigurationBindingGenerator>", false)]
    [InlineData("<PublishTrimmed>true</PublishTrimmed>", true)]
    [InlineData("<PublishAot>true</PublishAot>", true)]
    public void Evaluate_ConfigurationBindingGeneratorGate(string propertyXml, bool expected)
    {
        var root = TempRoot("opencode-dbp-binder");

        try
        {
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), $"""
                <Project>
                  <PropertyGroup>
                    {propertyXml}
                  </PropertyGroup>
                </Project>
                """);

            var projectDir = Path.Combine(root, "A");
            Directory.CreateDirectory(projectDir);

            var result = BuildPropsEvaluator.Evaluate(projectDir);

            result.EnableConfigurationBindingGenerator.ShouldBe(expected);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_PropsOnlyImplicitUsings_Compiles()
    {
        var root = TempRoot("opencode-dbp-build");

        try
        {
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(root, "PropsOnly.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Library</OutputType>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(root, "Worker.cs"), """
                namespace PropsOnly;

                public class Worker
                {
                    public int Produce()
                    {
                        var items = new List<int> { 1, 2, 3 };
                        return items.Count;
                    }

                    public async Task<int> DelayAsync()
                    {
                        await Task.Delay(1);
                        return 7;
                    }
                }
                """);

            var build = SharpCompiler.Build(root);

            build.Success.ShouldBeTrue(build.Errors);
            File.Exists(Path.Combine(root, "myc", "Debug", "net10.0", "PropsOnly.dll")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}