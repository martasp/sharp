using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sharp;
using Shouldly;
using Xunit;

namespace Sharp.Tests;

public class RazorSupportTests
{
    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "razor-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static Project Load(string csprojPath, string root)
    {
        var file = new ProjectFile(csprojPath, File.ReadAllText(csprojPath));
        return new Project(file, new List<ProjectFile>(), root);
    }

    [Fact]
    public void UsesRazorSdk_DetectsRazorAndWebSdk()
    {
        RazorSupport.UsesRazorSdk("Microsoft.NET.Sdk.Razor").ShouldBeTrue();
        RazorSupport.UsesRazorSdk("Microsoft.NET.Sdk.Web").ShouldBeTrue();
        RazorSupport.UsesRazorSdk("Some.Sdk;Microsoft.NET.Sdk.Web").ShouldBeTrue();
        RazorSupport.UsesRazorSdk("Microsoft.NET.Sdk").ShouldBeFalse();
        RazorSupport.UsesRazorSdk(null).ShouldBeFalse();
    }

    [Fact]
    public void EncodeTargetPath_IsBase64OfRelativePath()
    {
        var encoded = RazorSupport.EncodeTargetPath("Views/Home/Index.cshtml");
        encoded.ShouldBe(Convert.ToBase64String(Encoding.UTF8.GetBytes("Views/Home/Index.cshtml")));
        Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).ShouldBe("Views/Home/Index.cshtml");
    }

    [Fact]
    public void RazorInputs_DiscoversViewsAndComponents_WithTargetPaths()
    {
        var root = NewRoot();
        var csproj = Path.Combine(root, "WebApp.csproj");
        Write(csproj, "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        Write(Path.Combine(root, "Views", "Home", "Index.cshtml"), "<h1>hi</h1>");
        Write(Path.Combine(root, "Components", "Counter.razor"), "<p>c</p>");
        // Excluded: bin/obj always, wwwroot for the Web SDK (DefaultWebContentItemExcludes).
        Write(Path.Combine(root, "bin", "Ignored.cshtml"), "x");
        Write(Path.Combine(root, "obj", "Ignored.cshtml"), "x");
        Write(Path.Combine(root, "wwwroot", "Static.cshtml"), "x");

        var inputs = Load(csproj, root).RazorInputs;

        inputs.Count.ShouldBe(2);
        inputs.ShouldContain(input => input.TargetPath == "Views/Home/Index.cshtml" && !input.IsComponent);
        inputs.ShouldContain(input => input.TargetPath == "Components/Counter.razor" && input.IsComponent);
    }

    [Fact]
    public void RazorInputs_EmptyForNonRazorSdk()
    {
        var root = NewRoot();
        var csproj = Path.Combine(root, "Lib.csproj");
        Write(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        Write(Path.Combine(root, "Views", "Index.cshtml"), "x");

        Load(csproj, root).RazorInputs.ShouldBeEmpty();
    }

    // End to end: feeding .cshtml through the Razor source generator must produce the compiled view
    // class and the [assembly: ProvideApplicationPartFactory] MVC discovery attribute, exactly as the
    // SDK does. The generator DLLs come from the staged payload.
    [Fact]
    public void Build_RazorWebApp_CompilesViewsAndEmitsApplicationPartAttribute()
    {
        var root = NewRoot();
        Write(Path.Combine(root, "WebApp.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        Write(Path.Combine(root, "Program.cs"), """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllersWithViews();
            var app = builder.Build();
            app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
            app.Run();
            """);
        Write(Path.Combine(root, "HomeController.cs"), """
            using Microsoft.AspNetCore.Mvc;
            public class HomeController : Controller { public IActionResult Index() => View(); }
            """);
        Write(Path.Combine(root, "Views", "Home", "Index.cshtml"),
            "@{ Layout = null; }<html><body><h1>Hi</h1></body></html>");

        var result = SharpCompiler.Build(root);
        result.Success.ShouldBeTrue(result.Errors);
        File.Exists(result.Project.DllPathName).ShouldBeTrue($"Expected dll at {result.Project.DllPathName}");

        // A lossless byte->string view so metadata names survive binary inspection.
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(result.Project.DllPathName));
        text.ShouldContain("AspNetCoreGeneratedDocument");
        text.ShouldContain("ProvideApplicationPartFactoryAttribute");
        text.ShouldContain("ConsolidatedAssemblyApplicationPartFactory");
    }
}
