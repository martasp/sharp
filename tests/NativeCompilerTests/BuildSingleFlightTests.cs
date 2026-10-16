using System;
using System.IO;
using System.Threading;
using Shouldly;
using Xunit;

namespace NativeCompilerTests;

// Build is single-flight per project: overlapping requests for the same project reuse one build
// instead of racing or failing, while a genuine self-reference is still reported as a cycle.
[Collection("Sequential Tests 4")]
public class BuildSingleFlightTests
{
    private static string NewProject(string csproj, string source)
    {
        var root = Path.Combine(Path.GetTempPath(), "sharp-singleflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "P.csproj"), csproj);
        File.WriteAllText(Path.Combine(root, "C.cs"), source);
        return root;
    }

    private const string Library = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup>
        </Project>
        """;

    [Fact]
    public void Concurrent_builds_of_the_same_project_share_one_build()
    {
        var root = NewProject(Library, "namespace P; public class T { }");
        try
        {
            var results = new NativeCompiler.BuildResult[7];
            using var barrier = new Barrier(7);
            var threads = new Thread[7];
            for (var i = 0; i < threads.Length; i++)
            {
                var idx = i;
                threads[idx] = new Thread(() =>
                {
                    barrier.SignalAndWait(); // start together so the builds actually overlap
                    results[idx] = NativeCompiler.NativeCompiler.Build(root);
                });
                threads[idx].Start();
            }
            foreach (var thread in threads) thread.Join();

            foreach (var result in results)
                result.Success.ShouldBeTrue(result.Errors);

            // Reused, not rebuilt: every overlapping caller observed the same result instance.
            for (var i = 1; i < results.Length; i++)
                ReferenceEquals(results[i], results[0]).ShouldBeTrue("build was not shared");
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Repeated_builds_of_the_same_project_are_reused()
    {
        var root = NewProject(Library, "namespace P; public class T { }");
        try
        {
            var first = NativeCompiler.NativeCompiler.Build(root);
            var second = NativeCompiler.NativeCompiler.Build(root);

            first.Success.ShouldBeTrue(first.Errors);
            ReferenceEquals(first, second).ShouldBeTrue("second build should reuse the first");
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void Self_referencing_project_reports_a_cycle_instead_of_deadlocking()
    {
        var root = NewProject(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup>
              <ItemGroup><ProjectReference Include="P.csproj" /></ItemGroup>
            </Project>
            """,
            "namespace P; public class T { }");
        try
        {
            var result = NativeCompiler.NativeCompiler.Build(root);

            result.Success.ShouldBeFalse();
            result.Errors.ShouldContain("Circular project reference detected");
        }
        finally { TryDelete(root); }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
