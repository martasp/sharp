using System;
using System.IO;
using NativeCompiler;
using Xunit;

public class NuGetPackageFinderTests
{
    [Fact]
    public void FindPackagePath_GivenExistingPackageInGlobalCache_ShouldReturnPackagePath()
    {
        // Arrange
        var packageName = "Microsoft.CodeAnalysis.CSharp".ToLower();
        var packageVersion = "4.7.0";
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
    public void FindPackagePath_GivenNonExistentPackageInGlobalCache_ShouldThrowException()
    {
        // Arrange
        var packageName = "NonExistentPackage";
        var packageVersion = "1.0.0";

        // Act and Assert
        Assert.Throws<Exception>(() => NuGetPackage.FindPackagePath(packageName, packageVersion));
    }

    [Fact]
    public void FindPackagePath_GivenExistingPackageInNuGetSources_ShouldReturnPackagePath()
    {
        // Arrange
        var packageName = "Microsoft.CodeAnalysis.CSharp".ToLower();
        var packageVersion = "4.7.0";
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