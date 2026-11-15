using System;
using System.IO;
using Shouldly;
using Xunit;
using Sharp;

namespace Sharp.Tests;

// A props-injected analyzer project reference (the OrchardCore.Commons.props pattern) is NOT part of
// the csproj <ProjectReference> graph, so GetdefaultLocalReferences never builds it - the only code
// that can produce its DLL is the analyzer build-on-demand in SourceGeneratorRunner. This guards that
// path: GetSharpBinDirectory creates the output folder, so probing the folder (not the DLL) silently
// skipped the build and left the analyzer unusable.
[Collection("Sequential Tests 4")]
public class AnalyzerBuildOnDemandTests
{
    private static string TempRoot(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Build_PropsInjectedAnalyzerProject_IsBuiltOnDemand()
    {
        var root = TempRoot("opencode-analyzer-ondemand");

        try
        {
            // Injects the analyzer into every project, exactly like the evaluated Commons.props chain.
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project>
                  <ItemGroup>
                    <ProjectReference Include="Analyzer\Analyzer.csproj"
                                      OutputItemType="Analyzer"
                                      ReferenceOutputAssembly="false" />
                  </ItemGroup>
                </Project>
                """);

            Directory.CreateDirectory(Path.Combine(root, "Analyzer"));
            File.WriteAllText(Path.Combine(root, "Analyzer", "Analyzer.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <OutputType>Library</OutputType>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Analyzer", "Analyzer.cs"),
                "namespace Analyzer; public class Noop { }");

            var consumerDir = Path.Combine(root, "Consumer");
            Directory.CreateDirectory(consumerDir);
            File.WriteAllText(Path.Combine(consumerDir, "Consumer.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <OutputType>Library</OutputType>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(consumerDir, "Consumer.cs"),
                "namespace Consumer; public class Widget { public int Value => 1; }");

            var build = SharpCompiler.Build(consumerDir);
            build.Success.ShouldBeTrue(build.Errors);

            var analyzerDll = Path.Combine(root, "Analyzer", "myc", "Debug", "net11.0", "Analyzer.dll");
            File.Exists(analyzerDll).ShouldBeTrue(
                $"Analyzer project output should be produced on demand at {analyzerDll}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
