using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Sharp.Tests;

[CollectionDefinition("Sequential Tests", DisableParallelization = true)]
[Collection("Sequential Tests")]
public class ConsoleAppTestsSelf
{
    public static string TestProjectsFolder =
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "TestExampleProjects");

    public static string ConsoleAppNotWorkig = Path.Combine(TestProjectsFolder, nameof(ConsoleAppNotWorkig));

    public static string ConsoleAppWorking = Path.Combine(TestProjectsFolder, nameof(ConsoleAppWorking));

    public static string WebApiWorking = Path.Combine(TestProjectsFolder, nameof(WebApiWorking));

    [Fact]
    public async Task WhenSampleConsoleAppGivenItShouldRunAppAndCaptureConsoleLogs()
    {
        var csprojFolderPath = ConsoleAppWorking;
        
        var result = SharpCompiler.Run(csprojFolderPath);

        result.errors.ShouldContain("--------captured-logs-test--------");
        result.errors.ShouldNotContain("----Error----captured-logs-test--------");
    }

    [Fact]
    public void WhenSampleConsoleAppGivenItShouldCompileWithoutErrors()
    {
        var csprojFolderPath = ConsoleAppWorking;
        var result = SharpCompiler.Build(csprojFolderPath);
        result.Success.ShouldBeTrue(result.Errors);
    }

    [Fact]
    public void WhenSampleConsoleAppGivenItShouldCompileWithoutErrorsAndProduceDll()
    {
        var csprojFolderPath = ConsoleAppWorking;
        var result = SharpCompiler.Build(csprojFolderPath);
        result.Success.ShouldBeTrue(result.Errors);
        Directory.EnumerateFiles(csprojFolderPath).Any(item => item.Contains(".dll")).ShouldBeTrue("dll not Exists");
    }

    [Fact]
    public void ITShouldCompileSelfCliProject()
    {
        var csprojFolderPath = TestPaths.Src("Sharp.Cli");
        var result = SharpCompiler.Build(csprojFolderPath);
        result.Success.ShouldBeTrue(result.Errors);
    }

    [Fact]
    public void WhenSampleWebApiGivenItShouldCompileWithoutErrors()
    {
        var csprojFolderPath = WebApiWorking;
        var result = SharpCompiler.Build(csprojFolderPath);
        result.Success.ShouldBeTrue(result.Errors);
    }

    [Fact]
    public void WhenSampleWebApiGivenItShouldCompileWithoutErrorsAndProduceDll()
    {
        var csprojFolderPath = WebApiWorking;
        var result = SharpCompiler.Build(csprojFolderPath);
        result.Success.ShouldBeTrue(result.Errors);
        var outputDir = Path.Combine(csprojFolderPath, "myc", "Debug", "net11.0");
        File.Exists(Path.Combine(outputDir, "WebApiWorking.dll")).ShouldBeTrue("dll not Exists");
        var appHost = "WebApiWorking" + (OperatingSystem.IsWindows() ? ".exe" : "");
        File.Exists(Path.Combine(outputDir, appHost)).ShouldBeTrue("apphost not Exists");
    }
}


//Console.WriteLine("1  : " + typeof(object));
//Console.WriteLine("2  : " + typeof(object).Assembly);
//Console.WriteLine("3  : " + typeof(object).FullName);
//Console.WriteLine("4  : " + typeof(object).Name);
//Console.WriteLine("5  : " + typeof(object).AssemblyQualifiedName);
//Console.WriteLine("6  : " + typeof(object).Assembly.Location);
//Console.WriteLine("7  : " + typeof(object).Assembly.HostContext);
//Console.WriteLine("8  : " + typeof(object).Assembly.FullName);
//Console.WriteLine("9  : " + typeof(object).Assembly.HostContext);
//Console.WriteLine("10 : " + typeof(int).Assembly.Location);
//Console.WriteLine("11 : " + AppDomain.CurrentDomain.BaseDirectory);
//Console.WriteLine("12 : " + AppContext.BaseDirectory);
//Console.WriteLine("13 : " + typeof(object).Assembly.HostContext);
//Console.WriteLine("14 : " + typeof(object).Assembly.ImageRuntimeVersion);
//Console.WriteLine("15 : " + typeof(object).Assembly.ManifestModule);
//Console.WriteLine("16 : " + typeof(object).Assembly.AOTLocation());