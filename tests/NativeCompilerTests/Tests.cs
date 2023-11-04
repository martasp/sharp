using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace NativeCompilerTests;

public class ConsoleAppTests
{
    public static string TestProjectsFolder =
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "TestExampleProjects");

    public static string ConsoleAppNotWorkig = Path.Combine(TestProjectsFolder, nameof(ConsoleAppNotWorkig));

    public static string ConsoleAppWorking = Path.Combine(TestProjectsFolder, nameof(ConsoleAppWorking));

    [Fact]
    public void WhenSampleConsoleAppGivenItShouldCompileWithoutErrors()
    {
        var csprojFolderPath = ConsoleAppWorking;
        var result = NativeCompiler.NativeCompiler.Build(csprojFolderPath);
        result.success.ShouldBeTrue(result.errors);
    }

    [Fact]
    public void WhenSampleConsoleAppGivenItShouldCompileWithErrorAndShowErrorLineNumber()
    {
        var csprojFolderPath = ConsoleAppNotWorkig;
        var conpilation = NativeCompiler.NativeCompiler.Build(csprojFolderPath);
        conpilation.errors.ShouldContain(":line 2");
    }

    [Fact]
    public void WhenSampleConsoleAppGivenItShouldRunAppAndCaptureConsoleLogs()
    {
        var csprojFolderPath = ConsoleAppWorking;
        var result = NativeCompiler.NativeCompiler.Build(csprojFolderPath);
        result.errors.ShouldContain("--------captured-logs-test--------");
        result.errors.ShouldNotContain("----Error----captured-logs-test--------");
    }

    [Fact]
    public void WhenSampleConsoleAppGivenItShouldCompileWithoutErrorsAndProduceDll()
    {
        var csprojFolderPath = ConsoleAppWorking;
        var result = NativeCompiler.NativeCompiler.Build(csprojFolderPath);
        result.success.ShouldBeTrue(result.errors);
        Directory.EnumerateFiles(csprojFolderPath).Any(item => item.Contains(".dll")).ShouldBeTrue("dll not Exists");
    }

    [Fact]
    public void ITShouldCompileSelfProject()
    {
        var csprojFolderPath = @"C:\Mp\pocs\Pocs\NativeCompiler\src\NativeCompiler";
        var result = NativeCompiler.NativeCompiler.Build(csprojFolderPath);
        result.success.ShouldBeTrue(result.errors);
    }

    [Fact]
    public void ITShouldCompileSelfCliProject()
    {
        var csprojFolderPath = @"C:\Mp\pocs\Pocs\NativeCompiler\src\NativeCompilerCLI";
        var result = NativeCompiler.NativeCompiler.Build(csprojFolderPath);
        result.success.ShouldBeTrue(result.errors);
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