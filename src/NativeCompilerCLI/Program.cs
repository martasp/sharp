using NativeCompiler;
using System;
using System.Linq;
using static System.Console;

var targetPath = args.FirstOrDefault() ?? throw new Exception("add console argument (.sln, .csproj or project folder)");

// dotnet's safe sign-off: `dotnet build` prints the errors to stdout with the owning `[csproj]`
// suffix, then re-echoes them after `Build FAILED.`.
static string WithProjectSuffix(string errors, string? projectFile)
{
    if (string.IsNullOrWhiteSpace(errors) || projectFile is null) return errors;

    return string.Join("\n", errors
        .Replace("\r\n", "\n")
        .Split('\n')
        .Where(line => line.Length > 0)
        .Select(line => line + $" [{projectFile}]"));
}

if (targetPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
    targetPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
{
    var solutionResult = NativeCompiler.NativeCompiler.BuildSolution(targetPath);

    foreach (var error in solutionResult.Errors)
    {
        WriteLine(error);
    }

    // Mirror `dotnet build`: one "<project> -> <output>" mapping line per built project, and the
    // build summary at the end. Project errors are printed inline like the SDK does.
    foreach (var project in solutionResult.Projects)
    {
        if (project.Result.Success)
        {
            var outputPath = project.Result.Project?.DllPathName ?? project.ProjectFile;
            WriteLine($"  {project.ProjectName} -> {outputPath}");
        }
        else
        {
            WriteLine(WithProjectSuffix(project.Result.Errors, project.ProjectFile));
        }
    }

    var failedProjects = solutionResult.Projects.Count(project => !project.Result.Success);
    WriteLine(solutionResult.Success ? "Build succeeded." : "Build FAILED.");
    WriteLine("    0 Warning(s)");
    WriteLine($"    {failedProjects} Error(s)");

    if (!solutionResult.Success)
    {
        Environment.Exit(1);
    }
}
else
{
    var buildResult = NativeCompiler.NativeCompiler.Build(targetPath);

    if (buildResult.Success && buildResult.Project is { } builtProject)
    {
        WriteLine($"  {builtProject.ProjectName} -> {builtProject.DllPathName}");
        WriteLine();
        WriteLine("Build succeeded.");
        WriteLine("    0 Warning(s)");
        WriteLine("    0 Error(s)");
        WriteLine();
    }
    else
    {
        // Layout mirrors `dotnet build`: the errors are printed inline, then again after the
        // failure summary ("Build FAILED."), each line carrying its owning [csproj] suffix.
        var errors = WithProjectSuffix(buildResult.Errors, buildResult.Project?.CsProjFile.FilePath);
        WriteLine(errors);
        WriteLine();
        WriteLine("Build FAILED.");
        WriteLine();
        WriteLine(errors);
        WriteLine("    0 Warning(s)");
        WriteLine("    1 Error(s)");
        WriteLine();

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