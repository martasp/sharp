using System;
using System.IO;
using Shouldly;
using Xunit;

namespace Sharp.Tests;

// The Configuration Binding Generator is off-by-default in the SDK: it is turned on only for
// PublishTrimmed/PublishAot or an explicit <EnableConfigurationBindingGenerator>. Unlike the other
// targeting-pack generators it is interceptor-based (not attribute-driven), so running it for a
// normal build emits binder glue the SDK never produces. When the emitting project or a referenced
// one defines its own Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions,
// the generated `OptionsServiceCollectionExtensions.AddOptions` call becomes ambiguous and the build
// fails with CS0433.
//
// Nothing in the suite caught this before: the AOT canary only discovered it on a real multi-project
// solution, and the in-repo fixtures all compiled fine. These tests pin the gate at
// the seam that matters - a project whose referenced assembly carries the colliding type.
[Collection("Sequential Tests 4")]
public class ConfigurationBindingGeneratorTests
{
    [Fact]
    public void BinderGenerator_OffByDefault_Compiles()
    {
        var root = WriteFixture(enableBinder: false);

        try
        {
            var build = SharpCompiler.Build(Path.Combine(root, "App"));

            build.Success.ShouldBeTrue($"build failed: {build.Errors}");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void BinderGenerator_WhenOptedIn_ReproducesCs0433()
    {
        var root = WriteFixture(enableBinder: true);

        try
        {
            var build = SharpCompiler.Build(Path.Combine(root, "App"));

            build.Success.ShouldBeFalse("the opt-in build should trip the collision this gate avoids");
            build.Errors.ShouldContain("CS0433");
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string WriteFixture(bool enableBinder)
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode-cfg-binder-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "Lib"));
        Directory.CreateDirectory(Path.Combine(root, "App"));

        File.WriteAllText(Path.Combine(root, "Lib", "Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
            </Project>
            """);

        // The exact fully-qualified type the framework ships (Microsoft.Extensions.Options). Once
        // this library is referenced, the generated binder's unqualified reference to it is
        // ambiguous between the two assemblies.
        File.WriteAllText(Path.Combine(root, "Lib", "Collision.cs"), """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class OptionsServiceCollectionExtensions
                {
                }
            }
            """);

        var enable = enableBinder
            ? "<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>"
            : "";

        File.WriteAllText(Path.Combine(root, "App", "App.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                {enable}
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);

        // An interceptable Configure<TOptions>(IConfiguration) call site - what makes the generator
        // emit the Configure interceptor that references OptionsServiceCollectionExtensions.
        File.WriteAllText(Path.Combine(root, "App", "Program.cs"), """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            var builder = WebApplication.CreateBuilder();
            IServiceCollection services = builder.Services;
            IConfiguration configuration = builder.Configuration;
            services.Configure<MyOptions>(configuration);

            var app = builder.Build();
            app.Run();

            public sealed class MyOptions
            {
                public string? Url { get; set; }
            }
            """);

        return root;
    }

    private static void Cleanup(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { }
    }
}
