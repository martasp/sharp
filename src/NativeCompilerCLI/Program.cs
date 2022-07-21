global using NativeCompiler;
//global using Cocona;
using Microsoft.CodeAnalysis;
using System.Text;
using System.Diagnostics;
using System;
using System.Linq;

Console.WriteLine(args);
Console.WriteLine(args.FirstOrDefault());

var csprojFolderPath = args.FirstOrDefault() ?? throw new Exception ("add console argument");
var _ = NativeCompiler.NativeCompiler.Run(csprojFolderPath);


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