using System.IO;
using Sharp;
using Xunit;

namespace Sharp.Tests;

public class ProjectReferenceFinderTests
{
    [Fact]
    public void FindReferencedDLL_ShouldReturnValidPath()
    {
        // Replace with the actual paths to your project and referenced project
        var projectFilePath = @"..\Sharp\Sharp.csproj";
        var projectFullPath = TestPaths.Src("Sharp.Cli");

        var referencedDLLPath = ProjectReferenceFinder.FindReferencedDLL(projectFilePath, projectFullPath);

        Assert.NotNull(referencedDLLPath);
        Assert.True(File.Exists(referencedDLLPath));
    }
}