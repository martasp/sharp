using NativeCompiler;
using System;
using System.Linq;
using static System.Console;

var targetPath = args.FirstOrDefault() ?? throw new Exception("add console argument (.sln, .csproj or project folder)");

if (targetPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
{
    var solutionResult = NativeCompiler.NativeCompiler.BuildSolution(targetPath);

    WriteLine($"Solution total time: {solutionResult.Duration.TotalMilliseconds:0}ms");
    foreach (var error in solutionResult.Errors)
    {
        Error.WriteLine(error);
    }

    foreach (var project in solutionResult.Projects)
    {
        WriteLine($"{(project.Result.Success ? "OK   " : "FAIL ")} {project.ProjectName}\t{project.Result.Duration.TotalMilliseconds:0}ms");
        foreach (var output in project.Result.Outputs)
        {
            WriteLine($"      {output}");
        }
        if (!project.Result.Success)
        {
            Error.WriteLine($"      {project.Result.Errors}");
        }
    }

    if (!solutionResult.Success)
    {
        Environment.Exit(1);
    }
}
else
{
    var buildResult = NativeCompiler.NativeCompiler.Build(targetPath);

    WriteLine($"Total time: {buildResult.Duration.TotalMilliseconds:0}ms");
    foreach (var line in buildResult.Outputs)
    {
        WriteLine(line);
    }

    if (!buildResult.Success)
    {
        Error.WriteLine($"Build failed: {buildResult.Errors}");
        Environment.Exit(1);
    }
}

//if you want rewrite roslyn:
//Found that roslyn compilation emit method eventually just use Microsoft.Cci Microsoft.Cci
//    https://source.dot.net/#Microsoft.CodeAnalysis/Compilation/Compilation.cs,3379
//https://source.dot.net/#Microsoft.CodeAnalysis/PEWriter/PeWriter.cs,922442ffcf5c6289

























//In case if want to use Cocona
////var timer = new Stopwatch();
////timer.Start();
//var builder = CoconaApp.CreateBuilder();
//var app = builder.Build();

//app.AddCommand("build",(string? csprojFolderPath) => 
//{
//    //app.Logger.LogInformation("Compilation Started");
//    NativeCompiler.NativeCompiler.Build(csprojFolderPath ?? "./");
//    //timer.Stop();
//    //app.Logger.LogInformation("Compilation Took: " + timer.ElapsedMilliseconds.ToString() + "ms");
//});


//app.AddCommand("run", (string csprojFolderPath) => { NativeCompiler.NativeCompiler.Run(csprojFolderPath); });


////app.Logger..Log<String>(Microsoft.Extensions.Logging.LogLevel.Information, new EventId(5), timer.ElapsedMilliseconds.ToString(),null,null);
//app.Run();


//app.Logger.LogInformation(timer.ElapsedMilliseconds.ToString());

//add               Add a package or reference to a .NET project.
//build             Build a .NET project.
//build-server      Interact with servers started by a build.
//clean             Clean build outputs of a .NET project.
//format            Apply style preferences to a project or solution.
//help              Show command line help.
//list              List project references of a .NET project.
//msbuild           Run Microsoft Build Engine (MSBuild) commands.
//new               Create a new .NET project or file.
//nuget             Provides additional NuGet commands.
//pack              Create a NuGet package.
//publish           Publish a .NET project for deployment.
//remove            Remove a package or reference from a .NET project.
//restore           Restore dependencies specified in a .NET project.
//run               Build and run a .NET project output.
//sdk               Manage .NET SDK installation.
//sln               Modify Visual Studio solution files.
//store             Store the specified assemblies in the runtime package store.
//test              Run unit tests using the test runner specified in a .NET project.
//tool              Install or manage tools that extend the .NET experience.
//vstest            Run Microsoft Test Engine (VSTest) commands.
//workload          Manage optional workloads.

//TODO
//Use dlls from refs folder for nativecompiler
//Copy shared dlls to sharpbin
//Use dotnet to run nativeCompiler.exe
//Fix nuget resolutions
//Fix NativeCompiler.dll resolution
//Native AOT works for build but not run method, consider just running exe not without loading assembly