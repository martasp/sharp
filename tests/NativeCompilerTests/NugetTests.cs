using System;
using System.IO;
using NativeCompiler;
using Xunit;

namespace NativeCompilerTests;

public class NuGetPackageFinderTests
{
    [Fact]
    public void FindPackagePath_GivenExistingPackageInGlobalCache_ShouldReturnPackagePath()
    {
        // Arrange
        var packageName = "Microsoft.CodeAnalysis.CSharp".ToLower();
        var packageVersion = "5.0.0";
        var globalPackagesFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");
        var expectedPackagePath = Path.Combine(globalPackagesFolderPath, packageName, packageVersion);

        // Act
        var packagePath = NuGetPackage.FindPackagePath(packageName, packageVersion);

        // Assert
        Assert.Equal(expectedPackagePath, packagePath);
    }

    [Fact]
    public void FindPackagePath_GivenExistingPackageInNuGetSources_ShouldReturnPackagePath()
    {
        // Arrange
        var packageName = "Microsoft.CodeAnalysis.CSharp".ToLower();
        var packageVersion = "5.0.0";
        var globalPackagesFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");
        var expectedPackagePath = Path.Combine(globalPackagesFolderPath, packageName, packageVersion);

        // Act
        var packagePath = NuGetPackage.FindPackagePath(packageName, packageVersion);

        // Assert
        Assert.Equal(expectedPackagePath, packagePath);
    }
}