using NativeCompiler;
using System;
using System.Linq;
using static System.Console;

var csprojFolderPath = args.FirstOrDefault() ?? throw new Exception("add console argument");
var buildResult = NativeCompiler.NativeCompiler.Build(csprojFolderPath);

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

//TODO
//Use dlls from refs folder for nativecompiler
//Copy shared dlls to sharpbin
//Use dotnet to run nativeCompiler.exe
//Fix nuget resolutions
//Fix NativeCompiler.dll resolution
//Native AOT works for build but not run method, consider just running exe not without loading assembly