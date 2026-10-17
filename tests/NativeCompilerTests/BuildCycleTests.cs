using System.IO;
using System.Threading.Tasks;
using Shouldly;
using Xunit;
using NativeCompiler;

namespace NativeCompilerTests;

[CollectionDefinition("Sequential Tests 5", DisableParallelization = true)]
[Collection("Sequential Tests 5")]
public class BuildCycleTests
{
    public static string RepoRoot =
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));

    [Fact]
    public async Task Self_referencing_project_reports_a_cycle_instead_of_building_forever()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nc-cycle-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var csproj = Path.Combine(dir, "SelfRef.csproj");
        await File.WriteAllTextAsync(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="SelfRef.csproj" />
              </ItemGroup>
            </Project>
            """);

        try
        {
            var build = NativeCompiler.NativeCompiler.Build(dir);

            build.Success.ShouldBeFalse();
            string.Join("\n", build.Errors).ShouldContain("Circular", Case.Insensitive);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
