using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace NativeCompilerTests;

[CollectionDefinition("Sequential Tests", DisableParallelization = true)]
[Collection("Sequential Tests")]
public class ConsoleAppTestsSelf
{
    public static string TestProjectsFolder =
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "TestExampleProjects");

    public static string ConsoleAppNotWorkig = Path.Combine(TestProjectsFolder, nameof(ConsoleAppNotWorkig));

    public static string ConsoleAppWorking = Path.Combine(TestProjectsFolder, nameof(ConsoleAppWorking));

    [Fact]
    public async Task WhenSampleConsoleAppGivenItShouldRunAppAndCaptureConsoleLogs()
    {
        var csprojFolderPath = ConsoleAppWorking;
        
        var result = NativeCompiler.NativeCompiler.Run(csprojFolderPath);

        result.errors.ShouldContain("--------captured-logs-test--------");
        result.errors.ShouldNotContain("----Error----captured-logs-test--------");
    }

    [Fact]
    public void WhenSampleConsoleAppGivenItShouldCompileWithoutErrors()
    {
        var csprojFolderPath = ConsoleAppWorking;
        var result = NativeCompiler.NativeCompiler.Build(csprojFolderPath);
        result.Success.ShouldBeTrue(result.Errors);
    }

    [Fact]
    public void WhenSampleConsoleAppGivenItShouldCompileWithoutErrorsAndProduceDll()
    {
        var csprojFolderPath = ConsoleAppWorking;
        var result = NativeCompiler.NativeCompiler.Build(csprojFolderPath);
        result.Success.ShouldBeTrue(result.Errors);
        Directory.EnumerateFiles(csprojFolderPath).Any(item => item.Contains(".dll")).ShouldBeTrue("dll not Exists");
    }

    [Fact]
    public void ITShouldCompileSelfCliProject()
    {
        var csprojFolderPath = @"D:\main\Pocs\NativeCompiler\src\NativeCompilerCLI";
        var result = NativeCompiler.NativeCompiler.Build(csprojFolderPath);
        result.Success.ShouldBeTrue(result.Errors);
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