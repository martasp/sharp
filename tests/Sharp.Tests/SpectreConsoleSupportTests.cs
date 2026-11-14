using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Sharp;
using Sharp.NuGet;
using Shouldly;
using Xunit;

namespace Sharp.Tests;

[Collection("Sequential Tests 4")]
public class SpectreConsoleSupportTests
{
    [Fact]
    public void AdditionalFiles_And_NumericLangVersion_ReachSourceGenerators()
    {
        var root = TempRoot(nameof(AdditionalFiles_And_NumericLangVersion_ReachSourceGenerators));

        try
        {
            var generatorDirectory = Path.Combine(root, "Generator");
            Directory.CreateDirectory(generatorDirectory);

            File.WriteAllText(Path.Combine(generatorDirectory, "Generator.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.12.0" />
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(generatorDirectory, "ProbeGenerator.cs"), """
                using System;
                using System.Linq;
                using Microsoft.CodeAnalysis;
                using Microsoft.CodeAnalysis.CSharp;

                [Generator]
                public sealed class ProbeGenerator : ISourceGenerator
                {
                    public void Initialize(GeneratorInitializationContext context) { }

                    public void Execute(GeneratorExecutionContext context)
                    {
                        var file = context.AdditionalFiles.FirstOrDefault(
                            candidate => candidate.Path.EndsWith("input.txt", StringComparison.OrdinalIgnoreCase));
                        if (file is null)
                        {
                            Report(context, "SHARP001", "input.txt was not passed as an AdditionalFile");
                            return;
                        }

                        var text = file.GetText(context.CancellationToken)?.ToString().Trim();
                        if (text != "from-additional-file")
                        {
                            Report(context, "SHARP002", "unexpected AdditionalFile content: " + text);
                            return;
                        }

                        if (context.ParseOptions is not CSharpParseOptions { LanguageVersion: LanguageVersion.CSharp13 })
                        {
                            var actual = (context.ParseOptions as CSharpParseOptions)?.LanguageVersion.ToString()
                                ?? context.ParseOptions.GetType().Name;
                            Report(context, "SHARP003", "unexpected generator language version: " + actual);
                            return;
                        }

                        context.AddSource("Probe.Generated.g.cs",
                            "internal static class ProbeGenerated { public const string Value = \"ok\"; }");
                    }

                    private static void Report(GeneratorExecutionContext context, string id, string message) =>
                        context.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(id, message, message, "Sharp.Tests",
                                DiagnosticSeverity.Error, isEnabledByDefault: true),
                            Location.None));
                }
                """);

            var consumerDirectory = Path.Combine(root, "Consumer");
            Directory.CreateDirectory(consumerDirectory);

            File.WriteAllText(Path.Combine(consumerDirectory, "Consumer.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <LangVersion>13</LangVersion>
                  </PropertyGroup>
                  <ItemGroup>
                    <AdditionalFiles Include="input.txt" />
                    <ProjectReference Include="../Generator/Generator.csproj"
                                      OutputItemType="Analyzer"
                                      ReferenceOutputAssembly="false" />
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(consumerDirectory, "input.txt"), "from-additional-file");
            File.WriteAllText(Path.Combine(consumerDirectory, "Consumer.cs"), """
                public static class Consumer
                {
                    public static string Value => ProbeGenerated.Value;
                }
                """);

            var build = SharpCompiler.Build(consumerDirectory);

            build.Success.ShouldBeTrue(build.Errors);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void NumericLangVersion_IsAppliedToTheCompiler()
    {
        var root = TempRoot(nameof(NumericLangVersion_IsAppliedToTheCompiler));

        try
        {
            File.WriteAllText(Path.Combine(root, "Versioned.cs"), """
                public static class Extensions
                {
                    extension(string value)
                    {
                        public int DoubledLength => value.Length * 2;
                    }
                }
                """);

            var projectPath = Path.Combine(root, "Versioned.csproj");
            WriteVersionedProject(projectPath, "14");

            var latest = SharpCompiler.Build(root);
            latest.Success.ShouldBeTrue(latest.Errors);

            WriteVersionedProject(projectPath, "13");

            var csharp13 = SharpCompiler.Build(root);
            csharp13.Success.ShouldBeFalse(
                "C# 14 extension members must not compile when sharp is told LangVersion=13");
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void CompilerGeneratedFilesOutputPath_IsExpandedInCompileRemove()
    {
        var root = TempRoot(nameof(CompilerGeneratedFilesOutputPath_IsExpandedInCompileRemove));

        try
        {
            File.WriteAllText(Path.Combine(root, "Fixture.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Remove="$(CompilerGeneratedFilesOutputPath)/**/*.cs" />
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(root, "Worker.cs"), """
                public static class Worker
                {
                    public static int Value => 1;
                }
                """);

            var generatedDirectory = Path.Combine(root, "Generated");
            var nestedGeneratedDirectory = Path.Combine(generatedDirectory, "Nested");
            Directory.CreateDirectory(nestedGeneratedDirectory);
            File.WriteAllText(Path.Combine(generatedDirectory, "CheckedIn.cs"),
                "this is deliberately not valid C#; it must not be compiled");
            File.WriteAllText(Path.Combine(nestedGeneratedDirectory, "CheckedIn.cs"),
                "this is deliberately not valid C#; it must not be compiled");

            var build = SharpCompiler.Build(root);

            build.Success.ShouldBeTrue(build.Errors);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void DefineConstants_InheritedToken_IsExpanded()
    {
        var root = TempRoot(nameof(DefineConstants_InheritedToken_IsExpanded));

        try
        {
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <DefineConstants>BASE</DefineConstants>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(root, "Fixture.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <DefineConstants>$(DefineConstants);EXTRA</DefineConstants>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(root, "Worker.cs"), """
                #if BASE && EXTRA
                public static class ConstantsPresent
                {
                }
                #else
                #error BASE and EXTRA must both be defined
                #endif
                """);

            var build = SharpCompiler.Build(root);

            build.Success.ShouldBeTrue(build.Errors);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void FindPackageSourceFiles_SelectsBestTfm()
    {
        var root = TempRoot(nameof(FindPackageSourceFiles_SelectsBestTfm));

        try
        {
            WriteContentFile(root, "net8.0", "Net8.cs");
            WriteContentFile(root, "net11.0", "Net11.cs");
            WriteContentFile(root, "netstandard2.0", "NetStandard.cs");

            var files = NuGetPackage.FindPackageSourceFiles(root, "net11.0");

            files.ShouldBe(new[]
            {
                Path.Combine(root, "contentFiles", "cs", "net11.0", "Net11.cs")
            });
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void FindPackageSourceFiles_FallsBackToAnyAny()
    {
        var root = TempRoot(nameof(FindPackageSourceFiles_FallsBackToAnyAny));

        try
        {
            var sharedDirectory = Path.Combine(root, "contentFiles", "any", "any");
            Directory.CreateDirectory(sharedDirectory);
            var source = Path.Combine(sharedDirectory, "Shared.cs");
            File.WriteAllText(source, "internal static class Shared { }");

            var files = NuGetPackage.FindPackageSourceFiles(root, "net11.0");

            files.ShouldBe(new[] { source });
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void PackageLooks_UnguardedEmptyPaths_AreSafe()
    {
        NuGetPackage.FindPackageSourceFiles("", "net11.0").ShouldBeEmpty();
        NuGetPackage.FindPackageSourceFiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "net11.0")
            .ShouldBeEmpty();
        NuGetPackage.ReadDependencies("", "net11.0").ShouldBeEmpty();
        NuGetPackage.ReadDependencies(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "net11.0")
            .ShouldBeEmpty();
    }

    private static void WriteVersionedProject(string projectPath, string langVersion) =>
        File.WriteAllText(projectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <LangVersion>{langVersion}</LangVersion>
              </PropertyGroup>
            </Project>
            """);

    private static void WriteContentFile(string root, string tfm, string fileName)
    {
        var directory = Path.Combine(root, "contentFiles", "cs", tfm);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), $"namespace Content.{tfm.Replace('.', '_')}; internal static class Marker {{ }}");
    }

    private static string TempRoot(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "opencode-spectre-support",
            name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
