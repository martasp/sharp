using NativeCompiler;
using System;
using System.Diagnostics;
using static System.Console;

//Console.WriteLine(args);
//Console.WriteLine(args.FirstOrDefault());

// time the compilation and running

var timer = Stopwatch.StartNew();
timer.Start();
var csprojFolderPath = args.FirstOrDefault() ?? throw new Exception("add console argument");
var _ = NativeCompiler.NativeCompiler.Build(csprojFolderPath);
timer.Stop();

WriteLine($"Total time: {timer.ElapsedMilliseconds}ms");
//Todo !!!!!!!!!!!!!!!!!!!!!!
//Fixes to do 
//Use dlls from refs folder for nativecompiler
//Copy shared dlls to sharpbin
//Use dotnet to run nativeCompiler.exe
//Fix nuget resolutions
//Fix NativeCompiler.dll resolution 



//Native aot works for build but not run method, consider just running exe not without loading assembly
//Errors are In xml reading proj files
//       Unhandled Exception: System.InvalidOperationException: There is an error in XML document(0, 0).
//--->System.InvalidOperationException: XML.Project cannot be serialized because it does not have a parameterless constructor.
//  at System.Xml.Serialization.TypeDesc.CheckSupported() + 0x42
//  at System.Xml.Serialization.TypeScope.GetTypeDesc(Type, MemberInfo, Boolean, Boolean) + 0xbd
//  at System.Xml.Serialization.ModelScope.GetTypeModel(Type, Boolean) + 0x5a
//  at System.Xml.Serialization.XmlReflectionImporter.ImportTypeMapping(Type, XmlRootAttribute, String) + 0x50
//  at System.Xml.Serialization.XmlSerializer.GetMapping() + 0x4f
//  at System.Xml.Serialization.XmlSerializer.Deserialize(XmlReader xmlReader, String encodingStyle, XmlDeserializationEvents events) + 0x98
//  --- End of inner exception stack trace ---
//  at System.Xml.Serialization.XmlSerializer.Deserialize(XmlReader xmlReader, String encodingStyle, XmlDeserializationEvents events) + 0x209
//  at System.Xml.Serialization.XmlSerializer.Deserialize(TextReader textReader) + 0x99
//  at NativeCompiler.Project.DeserializeObject(String) + 0x94
//  at NativeCompiler.Project.ResolvedProjectOutputKind() + 0x11
//  at NativeCompiler.NativeCompilerUtils.GetCompilationOptions(Project) + 0x4a
//  at NativeCompiler.NativeCompiler.Build(String) + 0x77
//  at Program.<Main>$(String[] args) +0x59
//  at NativeCompilerCLI!<BaseAddress>+0x101e3c3


//if you want rewrite roslyn:
//Found that roslyn compilation emit method eventually just use Microsoft.Cci Microsoft.Cci
//    https://source.dot.net/#Microsoft.CodeAnalysis/Compilation/Compilation.cs,3379
//https://source.dot.net/#Microsoft.CodeAnalysis/PEWriter/PeWriter.cs,922442ffcf5c6289


























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