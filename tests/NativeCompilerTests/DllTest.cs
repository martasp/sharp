using System.IO;
using NativeCompiler;
using Xunit;

namespace NativeCompilerTests;

public class ProjectReferenceFinderTests
{
    [Fact]
    public void FindReferencedDLL_ShouldReturnValidPath()
    {
        // Replace with the actual paths to your project and referenced project
        var projectFilePath = @"..\NativeCompiler\NativeCompiler.csproj";
        var projectFullPath = @"C:\Mp\pocs\Pocs\NativeCompiler\src\NativeCompilerCLI";

        var referencedDLLPath = ProjectReferenceFinder.FindReferencedDLL(projectFilePath, projectFullPath);

        Assert.NotNull(referencedDLLPath);
        Assert.True(File.Exists(referencedDLLPath));
    }
}