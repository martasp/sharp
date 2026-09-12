using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace NativeCompilerTests;

[CollectionDefinition("Sequential Tests 2", DisableParallelization = true)]
[Collection("Sequential Tests 2")]
public class ConsoleAppTests
{
    public static string TestProjectsFolder =
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "TestExampleProjects");

    public static string ConsoleAppNotWorkig = Path.Combine(TestProjectsFolder, nameof(ConsoleAppNotWorkig));

    public static string ConsoleAppWorking = Path.Combine(TestProjectsFolder, nameof(ConsoleAppWorking));

    [Fact]
    public void WhenSampleConsoleAppGivenItShouldCompileAndRunWithErrorAndShowErrorLineNumber()
    {
        var csprojFolderPath = ConsoleAppNotWorkig;
        var conpilation = NativeCompiler.NativeCompiler.Run(csprojFolderPath);
        conpilation.errors.ShouldContain(":line 2");
    }

    [Fact]
    public void ITShouldCompileSelfProject()
    {
        var csprojFolderPath = @"D:\main\Pocs\NativeCompiler\src\NativeCompiler";
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