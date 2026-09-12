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
}