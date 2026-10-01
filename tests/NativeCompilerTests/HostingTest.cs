using Shouldly;
using System;
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VerifyTests;
using VerifyXunit;
using Xunit;

namespace Tests;


[CollectionDefinition("Sequential Tests 3", DisableParallelization = true)]
[Collection("Sequential Tests 3")]
public class HostingTest
{
    private static readonly string MyCompiler = Path.Combine(TestPaths.RepoRoot, "testProjects", "MyCompiler");

    // [Fact] Delete obsolete, we already have docker tests.
    public async Task TypeDefinitionUsage()
    {
        var hostExeName = OperatingSystem.IsWindows() ? "DotnetRuntimeHostExe.exe" : "DotnetRuntimeHostExe";

        var buildDetails = NativeCompiler.NativeCompiler.Build(MyCompiler);
        var sharbin = Path.Combine(MyCompiler, "myc", "Debug", "net10.0");
        var dll = Path.Combine(sharbin, "MSCompiler.dll");

        // copy tool to myCompiler output folder
        File.Copy(
            Path.Combine(TestPaths.RepoRoot, "DotnetRuntimeHostExe", "bin", "Release", "net10.0", "win-x64", "publish", hostExeName),
            Path.Combine(sharbin, hostExeName),
            true);

        //remove exe if exists
        var appExe = Path.Combine(sharbin, hostExeName);
        if (File.Exists(appExe))
        {
            File.Delete(appExe);
        }

        var runtimeconfigjson =
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "10.0.0"
                },
                "configProperties": {
                  "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
                }
              }
            }
            """;

        var MSCompilerdepsFile =
            """
            {
              "runtimeTarget": {
                "name": ".NETCoreApp,Version=v10.0",
                "signature": ""
              },
              "compilationOptions": {},
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "MSCompiler/1.0.0": {
                    "runtime": {
                      "MSCompiler.dll": {}
                    }
                  }
                }
              },
              "libraries": {
                "MSCompiler/1.0.0": {
                  "type": "project",
                  "serviceable": false,
                  "sha512": ""
                }
              }
            }
            """;

        // write runtimeconfig.json and deps.json , always overwrite
        File.WriteAllText(Path.Combine(sharbin, "MSCompiler.runtimeconfig.json"), runtimeconfigjson);
        File.WriteAllText(Path.Combine(sharbin, "MSCompiler.deps.json"), MSCompilerdepsFile);

        await Task.Delay(1000);

        // The executable is DotnetRuntimeHostExe.exe, and its current directory
        // should be set to the folder containing the DLLs and config files (sharbin).
        var startInfo = new ProcessStartInfo
        {
            FileName = appExe,
            // Argument should be the simple filename if your host executable is looking for 
            // a file name only, and relying on the CWD.
            Arguments = "MSCompiler.runtimeconfig.json",
            // Set the Current Working Directory (CWD) to the 'sharbin' folder.
            WorkingDirectory = sharbin,
            RedirectStandardOutput = true,
            RedirectStandardError = true, // Good practice for diagnostics
            UseShellExecute = false,      // Must be false if redirecting output/error
            CreateNoWindow = true
        };

        using (var process = Process.Start(startInfo))
        {
            // 3. Wait for the process to exit
            process.WaitForExit(10000); // Wait up to 10 seconds

            // Optional: read and print output if it fails for better debugging
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();

            if (process.ExitCode != 0)
            {
                // Fail the test if the host shell returns a failure code
                throw new Exception($"Host failed with code {process.ExitCode}. Output: {output}. Errors: {errors}");
            }
        }

    }
}