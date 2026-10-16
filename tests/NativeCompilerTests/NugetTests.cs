using NativeCompiler.NuGet;
using System;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;
using NativeCompiler;

namespace NativeCompilerTests;

public class NuGetTests
{
    [Fact]
    public void FindPackageDLLs_SelectsBestMatchingLibForTargetTfm()
    {
        var packagePath = NuGetPackage.FindPackagePath("Microsoft.CodeAnalysis.CSharp", "5.3.0");
        packagePath.ShouldNotBeEmpty();

        var dlls = NuGetPackage.FindPackageDLLs(packagePath, "net8.0");

        dlls.ShouldNotBeEmpty();
        dlls.ShouldAllBe(dll => dll.Replace('\\', '/').Contains("/lib/net8.0/",
            StringComparison.OrdinalIgnoreCase));
        dlls.Select(Path.GetFileName).ShouldContain("Microsoft.CodeAnalysis.CSharp.dll",
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindPackageDLLs_ScoringPrefersExactCoreTfmOverNetStandard()
    {
        NuGetTfm.Score("net8.0", "net10.0").ShouldBeGreaterThan(NuGetTfm.Score("netstandard2.0", "net10.0"));
        NuGetTfm.Score("net8.0", "net10.0").ShouldBeGreaterThan(NuGetTfm.Score("net472", "net10.0"));
        NuGetTfm.Score("net10.0", "net10.0").ShouldBeGreaterThan(NuGetTfm.Score("net8.0", "net10.0"));
    }

    // A platform-specific lib folder (net10.0-ios26.0) must not be chosen for a platform-agnostic
    // target: it would otherwise beat the compatible net8.0 folder and pull in the wrong variant.
    [Fact]
    public void Score_PlatformSpecificCandidate_IsNotCompatibleWithPlainTarget()
    {
        NuGetTfm.Score("net10.0-ios26.0", "net10.0").ShouldBe(0);
        NuGetTfm.Score("net10.0-tvos26.0", "net10.0").ShouldBe(0);
        NuGetTfm.Score("net8.0-windows7.0", "net10.0").ShouldBe(0);
        NuGetTfm.Score("net8.0", "net10.0").ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Score_PlatformSpecificCandidate_MatchesSamePlatformTarget()
    {
        NuGetTfm.Score("net10.0-ios26.0", "net10.0-ios26.0").ShouldBeGreaterThan(0);
        NuGetTfm.Score("net8.0", "net10.0-ios26.0").ShouldBeGreaterThan(0);
    }


    [Fact]
    public void FindPackageDLLs_MetaPackageWithoutLibReturnsEmpty()
    {
        var packagePath = NuGetPackage.FindPackagePath("Microsoft.CodeAnalysis", "5.0.0");
        packagePath.ShouldNotBeNull();

        var dlls = NuGetPackage.FindPackageDLLs(packagePath, "net10.0");
        dlls.ShouldBeEmpty();
    }

    [Fact]
    public void GetTransitivePackageDlls_IncludesNuSpecDependencies()
    {
        var packagePath = NuGetPackage.FindPackagePath("Testcontainers", "4.4.0");
        packagePath.ShouldNotBeEmpty();

        var dlls = NuGetPackage.GetTransitivePackageDlls(packagePath, "net10.0");

        dlls.ShouldNotBeEmpty();
        dlls.Select(Path.GetFileName)
            .ShouldContain(file => file.Equals("Renci.SshNet.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveCentralVersion_ReadsGlobalPackagesProps()
    {
        var root = Path.Combine(Path.GetTempPath(), "SlnFixturesCentralTest", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Directory.Packages.props"),
                "<Project><ItemGroup><PackageVersion Include=\"DemoPkg\" Version=\"1.2.3\" /></ItemGroup></Project>");

            NuGetPackage.ResolveCentralVersion("DemoPkg", root).ShouldBe("1.2.3");
            NuGetPackage.ResolveCentralVersion("Unknown", root).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ResolveCentralVersion_ReturnsEmptyWithoutGlobalPackagesProps()
    {
        var root = Path.GetTempPath();
        NuGetPackage.ResolveCentralVersion("DemoPkg", root).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------------------
    // Feature re-apply check: package buildTransitive <Using> global usings + Web SDK
    // implicit usings (see NativeCompilerUtils.GetProjectDetails / BuildPackageUsingStatements).
    // ---------------------------------------------------------------------------
    [Fact]
    public void ReadPackageUsingItems_Parlot_ReturnsStaticFluentParsersUsing()
    {
        var packagePath = NuGetPackage.FindPackagePath("Parlot", "1.5.8");
        packagePath.ShouldNotBeEmpty();

        var usings = NuGetPackage.ReadPackageUsingItems(packagePath);

        usings.ShouldContain(item => item.Namespace == "Parlot.Fluent.Parsers" && item.Static);
    }

    [Fact]
    public void GetProjectDetails_ImplicitUsingsWithParlotReference_EmitsPackageGlobalUsings()
    {
        var root = Path.Combine(Path.GetTempPath(), "GlobalUsingsFixture", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Fixture.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>" +
                "<ImplicitUsings>enable</ImplicitUsings></PropertyGroup>" +
                "<ItemGroup><PackageReference Include=\"Parlot\" Version=\"1.5.8\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Program.cs"), "// placeholder\n");

            var project = NativeCompilerUtils.GetProjectDetails(
                NativeCompilerUtils.ReadAllFilesInMemory(NativeCompilerUtils.ReadAllProjectFilesPath(root)), root);

            project.ImplicitUsings.ShouldBeTrue();
            var globalUsings = project.CSFiles.Single(file => file.FilePath.EndsWith("GlobalUsings.g.cs")).FileContent;
            globalUsings.ShouldContain("global using static global::Parlot.Fluent.Parsers;");
            globalUsings.ShouldContain("global using System.Linq;");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetProjectDetails_WebSdk_EmitsAspNetCoreGlobalUsings()
    {
        var root = Path.Combine(Path.GetTempPath(), "GlobalUsingsWebFixtures", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Fixture.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>" +
                "<ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Program.cs"), "// placeholder\n");

            var project = NativeCompilerUtils.GetProjectDetails(
                NativeCompilerUtils.ReadAllFilesInMemory(NativeCompilerUtils.ReadAllProjectFilesPath(root)), root);

            project.IsWebSdk.ShouldBeTrue();
            var globalUsings = project.CSFiles.Single(file => file.FilePath.EndsWith("GlobalUsings.g.cs")).FileContent;
            globalUsings.ShouldContain("global using Microsoft.AspNetCore.Builder;");
            globalUsings.ShouldContain("global using Microsoft.AspNetCore.Http;");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}