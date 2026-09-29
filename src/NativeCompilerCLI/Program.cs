using NativeCompiler;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using static System.Console;

// CLI command parity with the .NET SDK: `build`, `run`, `help`, `version`/`--version`.
// Paths, positional arguments and flags seek the same behaviors as `dotnet`
// (current-directory project search, exit codes, and the SDK's error/help texts).
// Legacy bare-path invocation (`NativeCompilerCLI <folder>`) routes to `build`.

// Byte-identical to `dotnet help` for the host SDK (the version line is substituted at run time so
// the text stays in step with whichever SDK hosts the CLI).
const string HelpTemplate = """
.NET SDK (10.0.400)
Usage: dotnet [runtime-options] [path-to-application] [arguments]

Execute a .NET application.

runtime-options:
  --additionalprobingpath <path>   Path containing probing policy and assemblies to probe for.
  --additional-deps <path>         Path to additional deps.json file.
  --depsfile                       Path to <application>.deps.json file.
  --fx-version <version>           Version of the installed Shared Framework to use to run the application.
  --roll-forward <setting>         Roll forward to framework version  (LatestPatch, Minor, LatestMinor, Major, LatestMajor, Disable).
  --runtimeconfig                  Path to <application>.runtimeconfig.json file.

path-to-application:
  The path to an application .dll file to execute.

Usage: dotnet [sdk-options] [command] [command-options] [arguments]

Execute a .NET SDK command.

sdk-options:
  -d|--diagnostics  Enable diagnostic output.
  -h|--help         Show command line help.
  --info            Display .NET information.
  --list-runtimes   Display the installed runtimes.
  --list-sdks       Display the installed SDKs.
  --version         Display .NET SDK version in use.

SDK commands:
  build             Build a .NET project.
  build-server      Interact with servers started by a build.
  clean             Clean build outputs of a .NET project.
  format            Apply style preferences to a project or solution.
  help              Opens the reference page in a browser for the specified command.
  msbuild           Run Microsoft Build Engine (MSBuild) commands.
  new               Create a new .NET project or file.
  nuget             Provides additional NuGet commands.
  pack              Create a NuGet package.
  package           Search for, add, remove, or list PackageReferences for a .NET project.
  publish           Publish a .NET project for deployment.
  reference         Add, remove, or list ProjectReferences for a .NET project.
  restore           Restore dependencies specified in a .NET project.
  run               Build and run a .NET project output.
  sdk               Manage .NET SDK installation.
  solution          Modify Visual Studio solution files.
  store             Store the specified assemblies in the runtime package store.
  test              Run unit tests using the test runner specified in a .NET project.
  tool              Install or manage tools that extend the .NET experience.
  vstest            Run Microsoft Test Engine (VSTest) commands.
  workload          Manage optional workloads.

Additional commands from bundled tools:
  dev-certs         Create and manage development certificates.
  fsi               Start F# Interactive / execute F# scripts.
  user-jwts         Manage JSON Web Tokens in development.
  user-secrets      Manage development user secrets.
  watch             Start a file watcher that runs a command when files change.

Run 'dotnet [command] --help' for more information on a command.
""";

var first = args.Length == 0 ? "help" : args[0];
bool explicitCommand = first is "build" or "run" or "help" or "version" or "--version" or "-h" or "--help";
if (!explicitCommand)
{
    // Legacy bare-path invocation: `NativeCompilerCLI <folder|sln>` IS a build; the path stays in
    // args[0] and must NOT be skipped like an explicit `build` subcommand would.
    first = "build";
}

switch (first)
{
    case "help" or "-h" or "--help":
        PrintHelp();
        return 0;
    case "version" or "--version":
        WriteLine(SdkVersion());
        return 0;
    case "build":
        return BuildCommand(explicitCommand ? args.Skip(1).ToArray() : args);
    case "run":
        return RunCommand(args.Skip(1).ToArray());
    default:
        throw new InvalidOperationException($"unreachable command {first}");
}

// --- commands ---------------------------------------------------------------

int BuildCommand(string[] args)
{
    var (pathArg, _) = PositionalAndFlags(args);
    var target = ResolveTargetOrFail(pathArg, checkRunnable: false);
    if (target is null) return 1;

    if (target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        target.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
    {
        var solutionResult = NativeCompiler.NativeCompiler.BuildSolution(target);

        foreach (var error in solutionResult.Errors) WriteLine(error);

        var isSln = target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase);
        var failedErrorLines = new List<string>();
        var successLines = new List<string>();
        foreach (var project in solutionResult.Projects)
        {
            if (project.Result.Success)
            {
                // `dotnet build *.sln` suppresses per-project lines on an all-successful build,
                // but `.slnx` (and any failing solution build) prints them per compiled project.
                if (isSln && solutionResult.Success) continue;
                var outputPath = project.Result.Project?.DllPathName ?? project.ProjectFile;
                successLines.Add($"  {project.ProjectName} -> {outputPath}");
            }
            else
            {
                failedErrorLines.Add(WithProjectSuffix(project.Result.Errors, project.ProjectFile));
            }
        }

        // SDK ordering quirk: a failing `.slnx` lists compiled projects before the errors, while a
        // `.sln` lists the errors first. Both are stable across runs, so mirror them exactly.
        if (!isSln)
        {
            foreach (var line in successLines) WriteLine(line);
            foreach (var line in failedErrorLines) WriteLine(line);
        }
        else
        {
            foreach (var line in failedErrorLines) WriteLine(line);
            foreach (var line in successLines) WriteLine(line);
        }

        WriteLine();
        WriteLine(solutionResult.Success ? "Build succeeded." : "Build FAILED.");
        if (!solutionResult.Success)
        {
            WriteLine();
            foreach (var failedLine in failedErrorLines) WriteLine(failedLine);
        }

        var failedProjects = solutionResult.Projects.Count(project => !project.Result.Success);
        WriteLine("    0 Warning(s)");
        WriteLine($"    {failedProjects} Error(s)");
        WriteLine();

        return solutionResult.Success ? 0 : 1;
    }

    var buildResult = NativeCompiler.NativeCompiler.Build(target);

    if (buildResult.Success && buildResult.Project is { } builtProject)
    {
        WriteLine($"  {builtProject.ProjectName} -> {builtProject.DllPathName}");
        WriteLine();
        WriteLine("Build succeeded.");
        WriteLine("    0 Warning(s)");
        WriteLine("    0 Error(s)");
        WriteLine();
        return 0;
    }

    var errors = WithProjectSuffix(buildResult.Errors, buildResult.Project?.CsProjFile.FilePath);
    WriteLine(errors);
    WriteLine();
    WriteLine("Build FAILED.");
    WriteLine();
    WriteLine(errors);
    WriteLine("    0 Warning(s)");
    WriteLine("    1 Error(s)");
    WriteLine();
    return 1;
}

int RunCommand(string[] args)
{
    var (pathArg, appArgs) = PositionalAndFlags(args);
    var target = ResolveTargetOrFail(pathArg, checkRunnable: true);
    if (target is null) return 1;

    var buildResult = NativeCompiler.NativeCompiler.Build(target);
    if (!buildResult.Success || buildResult.Project is not { } project)
    {
        WriteLine(WithProjectSuffix(buildResult.Errors, buildResult.Project?.CsProjFile.FilePath));
        WriteLine();
        WriteLine("The build failed. Fix the build errors and run again.");
        return 1;
    }

    // Launch the freshly built app just like `dotnet run`: apphost when present, else `dotnet <dll>`.
    // Output streams and the exit code are inherited and propagated unmodified.
    var appHost = Path.Combine(Path.GetDirectoryName(project.DllPathName) ?? ".", project.ProjectName);
    var launcher = File.Exists(appHost) ? appHost : "dotnet";
    var launchArgs = File.Exists(appHost) ? appArgs : new[] { project.DllPathName }.Concat(appArgs).ToArray();

    var startInfo = new ProcessStartInfo
    {
        FileName = launcher,
        UseShellExecute = false,
    };
    foreach (var arg in launchArgs) startInfo.ArgumentList.Add(arg);
    using var process = Process.Start(startInfo);
    if (process is null) return 1;
    process.WaitForExit();
    return process.ExitCode;
}

// --- resolution -------------------------------------------------------------

static string[] DirectChildProjectFiles(string dir) =>
    new[] { "*.sln", "*.slnx", "*.csproj" }
        .SelectMany(g => Directory.GetFiles(dir, g))
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        .ToArray();

// Mirrors `dotnet build`/`dotnet run` project resolution: an explicit path wins, otherwise the
// current directory is searched. Missing targets get the SDK-style messages; ambiguous folders
// (more than one .sln/.slnx/.csproj) get MSB1011. A solution is fine to *build* but is not
// runnable. A .csproj FILE is unwrapped to its directory (Build() takes a folder). Returns null
// after printing the failure message.
static string? ResolveTargetOrFail(string? pathArg, bool checkRunnable)
{
    if (pathArg is not null)
    {
        var fullPath = Path.GetFullPath(pathArg);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            WriteLine($"The provided file path does not exist: {pathArg}.");
            return null;
        }
        if (File.Exists(fullPath) && pathArg.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(fullPath)!;
        }
        return fullPath;
    }

    var cwd = Directory.GetCurrentDirectory();
    var candidates = DirectChildProjectFiles(cwd);
    if (candidates.Length == 0)
    {
        if (checkRunnable)
            WriteLine($"Couldn't find a project to run. Ensure a project exists in {cwd}, or pass the path to the project using --project.");
        else
            WriteLine("MSBUILD : error MSB1003: Specify a project or solution file. The current working directory does not contain a project or solution file.");
        return null;
    }
    if (candidates.Length > 1)
    {
        if (checkRunnable)
            WriteLine($"Specify which project file to use because {cwd} contains more than one project file.");
        else
            WriteLine("MSBUILD : error MSB1011: Specify which project or solution file to use because this folder contains more than one project or solution file.");
        return null;
    }

    var resolved = candidates[0];
    if (checkRunnable &&
        (resolved.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
         resolved.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
    {
        var dir = Path.GetDirectoryName(resolved) ?? ".";
        WriteLine($"Couldn't find a project to run. Ensure a project exists in {dir}, or pass the path to the project using --project.");
        return null;
    }
    if (resolved.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        return Path.GetDirectoryName(resolved)!;
    return resolved;
}

// --- helpers ----------------------------------------------------------------

// First non-flag argument is the project/solution path (`--project <path>` works too, matching
// `dotnet run`); everything after the `--` separator is handed to the running application.
static (string? Path, string[] AppArgs) PositionalAndFlags(string[] args)
{
    string? path = null;
    var appArgs = new System.Collections.Generic.List<string>();
    bool afterSeparator = false;
    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (afterSeparator) { appArgs.Add(arg); continue; }
        if (arg == "--") { afterSeparator = true; continue; }
        if (arg is "--project" or "-p")
        {
            if (i + 1 < args.Length) path = args[++i];
            continue;
        }
        if (path is null && !arg.StartsWith("-")) path = arg;
    }
    return (path, appArgs.ToArray());
}

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

// The version of the SDK hosting this compiler, byte-identical to `dotnet --version`.
static string SdkVersion()
{
    using var process = Process.Start(new ProcessStartInfo("dotnet", "--version")
    {
        RedirectStandardOutput = true,
        UseShellExecute = false,
    }) ?? throw new InvalidOperationException("Unable to start `dotnet --version`.");
    var version = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit();
    return version;
}

static void PrintHelp()
{
    WriteLine(HelpTemplate.Replace(".NET SDK (10.0.400)", $".NET SDK ({SdkVersion()})"));
}