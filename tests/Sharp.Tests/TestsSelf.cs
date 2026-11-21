using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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

    [Fact]
    public async Task Run_WebApi_UsesProjectDirectoryAsContentRoot()
    {
        var output = await RunWebApi();
        var projectDir = Path.GetFullPath(TestPaths.TestProjects("WebApiWorking"));
        output.Contains($"Content root path: {projectDir}")
            .ShouldBeTrue($"unexpected content root:\n{output}");
    }

    [Fact]
    public async Task Run_WebApi_UsesDefaultLaunchProfileEnvironment()
    {
        var output = await RunWebApi();
        output.Contains("Hosting environment: Development").ShouldBeTrue(output);
    }

    [Fact]
    public async Task Run_WebApi_HonorsNoLaunchProfile()
    {
        var output = await RunWebApi("--no-launch-profile");
        output.Contains("Hosting environment: Production").ShouldBeTrue(output);
    }

    [Fact]
    public async Task Run_WebApi_HonorsExplicitLaunchProfile()
    {
        var output = await RunWebApi("--launch-profile", "SecondProfile");
        output.Contains("Hosting environment: SecondDev").ShouldBeTrue(output);
    }

    private static async Task<string> RunWebApi(params string[] extraArgs)
    {
        var projectDir = Path.GetFullPath(TestPaths.TestProjects("WebApiWorking"));
        var cliDll = Path.Combine(TestPaths.RepoRoot, "src", "Sharp.Cli", "bin", "Debug", "net11.0",
            "Sharp.Cli.dll");
        File.Exists(cliDll).ShouldBeTrue("Sharp.Cli is not built");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = TestPaths.RepoRoot,
        };
        startInfo.ArgumentList.Add(cliDll);
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(projectDir);
        foreach (var extraArg in extraArgs) startInfo.ArgumentList.Add(extraArg);
        startInfo.Environment.Remove("ASPNETCORE_ENVIRONMENT");
        startInfo.Environment.Remove("DOTNET_ENVIRONMENT");
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var started = false;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            for (var attempt = 0; attempt < 80 && !started; attempt++)
            {
                try
                {
                    var response = await client.GetAsync($"http://127.0.0.1:{port}/");
                    started = response.IsSuccessStatusCode;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                if (!started) await Task.Delay(100);
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        var output = await stdout + await stderr;
        started.ShouldBeTrue($"Web API did not start:\n{output}");
        return output;
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