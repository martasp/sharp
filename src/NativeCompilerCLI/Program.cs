using NativeCompiler.NuGet;
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

// SDK command registry - the single source of truth for the `SDK commands:` section of help. Flip
// `Implemented` here when a command gains parity; the help text is derived from it (and from the
// hosting SDK version), so nothing in the output is hardcoded.
var SdkCommands = new (string Name, string Description, bool Implemented)[]
{
    ("build", "Build a .NET project.", true),
    ("build-server", "Interact with servers started by a build.", false),
    ("clean", "Clean build outputs of a .NET project.", true),
    ("format", "Apply style preferences to a project or solution.", false),
    ("help", "Opens the reference page in a browser for the specified command.", true),
    ("msbuild", "Run Microsoft Build Engine (MSBuild) commands.", false),
    ("new", "Create a new .NET project or file.", false),
    ("nuget", "Provides additional NuGet commands.", false),
    ("pack", "Create a NuGet package.", false),
    ("package", "Search for, add, remove, or list PackageReferences for a .NET project.", false),
    ("publish", "Publish a .NET project for deployment.", false),
    ("reference", "Add, remove, or list ProjectReferences for a .NET project.", false),
    ("restore", "Restore dependencies specified in a .NET project.", true),
    ("run", "Build and run a .NET project output.", true),
    ("sdk", "Manage .NET SDK installation.", false),
    ("solution", "Modify Visual Studio solution files.", false),
    ("store", "Store the specified assemblies in the runtime package store.", false),
    ("test", "Run unit tests using the test runner specified in a .NET project.", false),
    ("tool", "Install or manage tools that extend the .NET experience.", false),
    ("vstest", "Run Microsoft Test Engine (VSTest) commands.", false),
    ("workload", "Manage optional workloads.", false),
};

// SDK-independent spine of the help output, mirroring `dotnet help`. The version line (top) and the
// SDK command list are generated with live values.
const string HelpPreamble = """
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
""";

const string HelpEpilogue = """
Additional commands from bundled tools:
  dev-certs         Create and manage development certificates.
  fsi               Start F# Interactive / execute F# scripts.
  user-jwts         Manage JSON Web Tokens in development.
  user-secrets      Manage development user secrets.
  watch             Start a file watcher that runs a command when files change.

Run 'dotnet [command] --help' for more information on a command.
""";

// Column to the left of the `[implemented]`/`[not implemented]` markers (past the longest description).
const int SdkCommandAnnotationColumn = 94;

string HelpText()
{
    var text = new System.Text.StringBuilder();
    text.AppendLine($".NET SDK ({SdkVersion()})");
    text.Append(HelpPreamble);
    text.AppendLine();
    foreach (var (name, description, implemented) in SdkCommands)
        text.AppendLine($"  {name.PadRight(18)}{description}".PadRight(SdkCommandAnnotationColumn)
            + (implemented ? "[implemented]" : "[not implemented]"));
    text.AppendLine();
    text.Append(HelpEpilogue);
    return text.ToString();
}

var first = args.Length == 0 ? "help" : args[0];
bool explicitCommand = first is "build" or "run" or "restore" or "clean" or "help" or "version"
    or "--version" or "-h" or "--help";
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
    case "restore":
        return await RestoreCommandAsync(args.Skip(1).ToArray());
    case "clean":
        return CleanCommand(args.Skip(1).ToArray());
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

        // Top-level resolution errors (missing projects, unresolved references, cycles) come first.
        foreach (var error in solutionResult.Errors) WriteLine(error);

        // SDK terminal-logger grammar for solution builds: one status line per project in build
        // order, each carrying the measured seconds and the output path relative to the CWD
        // (exactly like a single-project build). Warnings are listed indented under their project.
        var totalWarnings = 0;
        var totalErrors = 0;
        foreach (var project in solutionResult.Projects)
        {
            var result = project.Result;
            var tfm = result.Project?.TargetFramework ?? "net10.0";

            if (result.Success)
            {
                var outputPath = result.Project?.DllPathName ?? project.ProjectFile;
                var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), outputPath);
                var warnings = SplitLines(result.Warnings);
                totalWarnings += warnings.Length;
                if (warnings.Length > 0)
                {
                    WriteLine($"  {project.ProjectName} {tfm} succeeded with {warnings.Length} warning(s) ({result.Duration.TotalSeconds:0.0}s) → {relativePath}");
                    foreach (var warning in warnings) WriteLine($"    {warning}");
                }
                else
                {
                    WriteLine($"  {project.ProjectName} {tfm} succeeded ({result.Duration.TotalSeconds:0.0}s) → {relativePath}");
                }
            }
            else
            {
                var projectErrorCount = ErrorLineCount(result.Errors);
                totalErrors += projectErrorCount;
                WriteLine($"  {project.ProjectName} {tfm} failed with {projectErrorCount} error(s) ({result.Duration.TotalSeconds:0.0}s)");
                foreach (var line in SplitLines(WithProjectSuffix(result.Errors, project.ProjectFile)))
                    WriteLine($"    {line}");
            }
        }

        WriteLine();
        if (solutionResult.Success)
        {
            WriteLine(totalWarnings > 0
                ? $"Build succeeded with {totalWarnings} warning(s) in {solutionResult.Duration.TotalSeconds:0.0}s"
                : $"Build succeeded in {solutionResult.Duration.TotalSeconds:0.0}s");
            return 0;
        }

        WriteLine($"Build failed with {totalErrors} error(s) in {solutionResult.Duration.TotalSeconds:0.0}s");
        return 1;
    }

    var totalTimer = Stopwatch.StartNew();
    var buildResult = NativeCompiler.NativeCompiler.Build(target);
    totalTimer.Stop();

    // Always use the SDK terminal-logger summary (the format `dotnet build` prints to a terminal):
    // it carries the measured seconds and the build path relative to the current directory.
    if (buildResult.Success && buildResult.Project is { } builtProject)
    {
        var tfm = builtProject.TargetFramework ?? "net10.0";
        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), builtProject.DllPathName);
        var warnings = SplitLines(buildResult.Warnings);
        if (warnings.Length > 0)
        {
            // SDK terminal grammar: a project with warnings reports
            // "succeeded with N warning(s) (Xs)" and lists them indented underneath.
            WriteLine($"  {builtProject.ProjectName} {tfm} succeeded with {warnings.Length} warning(s) ({buildResult.Duration.TotalSeconds:0.0}s) → {relativePath}");
            foreach (var warning in warnings) WriteLine($"    {warning}");
            WriteLine();
            WriteLine($"Build succeeded with {warnings.Length} warning(s) in {totalTimer.Elapsed.TotalSeconds:0.0}s");
            return 0;
        }

        WriteLine($"  {builtProject.ProjectName} {tfm} succeeded ({buildResult.Duration.TotalSeconds:0.0}s) → {relativePath}");
        WriteLine();
        WriteLine($"Build succeeded in {totalTimer.Elapsed.TotalSeconds:0.0}s");
        return 0;
    }

    var errorLines = SplitLines(buildResult.Errors);
    var errorCount = ErrorLineCount(buildResult.Errors);
    var name = buildResult.Project?.ProjectName ?? Path.GetFileNameWithoutExtension(target);
    var failureTfm = buildResult.Project?.TargetFramework ?? "net10.0";
    WriteLine($"  {name} {failureTfm} failed with {errorCount} error(s) ({buildResult.Duration.TotalSeconds:0.0}s)");
    foreach (var line in errorLines) WriteLine($"    {line}");
    WriteLine();
    WriteLine($"Build failed with {errorCount} error(s) in {totalTimer.Elapsed.TotalSeconds:0.0}s");
    return 1;
}

int ErrorLineCount(string errors) => SplitLines(errors).Length;

// Diagnostics come back as one blob with embedded newlines; split into individual message lines.
static string[] SplitLines(string text) => text
    .Replace("\r\n", "\n")
    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

async Task<int> RestoreCommandAsync(string[] args)
{
    // `dotnet restore` accepts -p/--project as the path (not an app-args forwarding flag), so the
    // shared positional parser is not reused here: the first non-flag argument is the path and
    // everything else is an error, matching the SDK's strictness.
    string? pathArg = null;
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg is "--project" or "-p")
        {
            if (i + 1 < args.Length) pathArg = args[++i];
            continue;
        }
        if (!arg.StartsWith("-") && pathArg is null) { pathArg = arg; continue; }

        WriteLine($"Unknown option: {arg}");
        WriteLine("Run 'dotnet restore --help' for more information.");
        return 1;
    }

    var target = ResolveTargetOrFail(pathArg, checkRunnable: false);
    if (target is null) return 1;

    WriteLine("Determining projects to restore...");

    var isSolution = target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                     target.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    var errors = new List<string>();
    var warnings = new List<string>();
    var restored = 0;

    if (isSolution)
    {
        var (success, projects, duration) =
            await PackageRestorer.RestoreSolution(target).ConfigureAwait(false);

        foreach (var project in projects)
        {
            foreach (var warning in project.Warnings) warnings.Add(warning);
            foreach (var error in project.Errors) errors.Add($"  {error}");

            if (project.Success)
            {
                restored++;
                WriteLine($"Restored {RelativePath(project.ProjectFile)} ({project.Duration.TotalMilliseconds:0} ms).");
            }
            else
            {
                var detail = Path.GetRelativePath(Directory.GetCurrentDirectory(), project.ProjectFile);
                WriteLine($"  {detail} failed with {project.Errors.Count} error(s) ({project.Duration.TotalSeconds:0.0}s)");
                foreach (var error in project.Errors) WriteLine($"    {error}");
            }
        }

        WriteLine();
        if (success)
        {
            WriteLine($"Restore succeeded in {duration.TotalSeconds:0.0}s");
            return 0;
        }

        WriteLine($"Restore failed with {errors.Count} error(s) in {duration.TotalSeconds:0.0}s");
        return 1;
    }

    var result = await PackageRestorer.RestoreProject(target).ConfigureAwait(false);
    foreach (var warning in result.Warnings) WriteLine(warning);

    if (result.Success)
    {
        WriteLine($"Restored {RelativePath(result.ProjectsRestored.FirstOrDefault() ?? target)} ({result.Duration.TotalMilliseconds:0} ms).");
        WriteLine();
        WriteLine($"Restore succeeded in {result.Duration.TotalSeconds:0.0}s");
        return 0;
    }

    foreach (var error in result.Errors) WriteLine($"  {error}");
    WriteLine();
    WriteLine($"Restore failed with {result.Errors.Count} error(s) in {result.Duration.TotalSeconds:0.0}s");
    return 1;
}

int CleanCommand(string[] args)
{
    // `dotnet clean -c|--configuration <name>` selects which configuration's outputs to delete.
    var configuration = "Debug";
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg is "-c" or "--configuration")
        {
            if (i + 1 < args.Length) configuration = args[++i];
            continue;
        }
        if (!arg.StartsWith("-"))
        {
            var (pathArg, _) = PositionalAndFlags(args);
            var target = ResolveTargetOrFail(pathArg, checkRunnable: false);
            if (target is null) return 1;
            return CleanTarget(target, configuration);
        }
    }

    var (implicitPath, _) = PositionalAndFlags(args);
    var implicitTarget = ResolveTargetOrFail(implicitPath, checkRunnable: false);
    if (implicitTarget is null) return 1;
    return CleanTarget(implicitTarget, configuration);
}

int CleanTarget(string target, string configuration)
{
    var cleanTimer = Stopwatch.StartNew();
    var roots = target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                target.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
        ? SolutionFile.Parse(target).Projects
            .Select(project => Path.GetDirectoryName(project.ProjectPath))
            .Where(directory => !string.IsNullOrEmpty(directory))
            .Select(directory => directory!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        : [target];

    var removed = 0;
    foreach (var root in roots)
    {
        // Only this compiler's own trees. bin/ belongs to the SDK and is what the parity tests
        // compare against, so deleting it would destroy the comparison baseline.
        foreach (var candidate in CleanTargets(root, configuration))
        {
            if (!Directory.Exists(candidate)) continue;
            try
            {
                Directory.Delete(candidate, recursive: true);
                removed++;
            }
            catch (Exception exception)
            {
                WriteLine($"Warning: could not delete '{RelativePath(candidate)}': {exception.Message}");
            }
        }
    }

    // SDK grammar: a clean that removed something still reports a (no-op) build line; one that
    // found nothing says so, which is the only useful distinction here.
    if (removed == 0)
    {
        WriteLine($"Nothing to clean for configuration '{configuration}'.");
        return 0;
    }

    WriteLine();
    WriteLine($"Clean succeeded in {cleanTimer.Elapsed.TotalSeconds:0.0}s");
    return 0;
}

static IEnumerable<string> CleanTargets(string projectDirectory, string configuration)
{
    var root = Environment.GetEnvironmentVariable("NATIVECOMPILER_OUTPUT_ROOT") ?? "myc";
    yield return Path.Combine(projectDirectory, root, configuration);
    yield return Path.Combine(projectDirectory, "obj");
}

// --- resolution -------------------------------------------------------------

static string[] DirectChildProjectFiles(string dir) =>
    new[] { "*.sln", "*.slnx", "*.csproj" }
        .SelectMany(g => Directory.GetFiles(dir, g))
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        .ToArray();

// Mirrors `dotnet build`/`dotnet run` project resolution: an explicit path wins, otherwise the
// current directory is searched. A directory is resolved to the single project or solution file it
// contains, for both the explicit-path and working-directory cases. Returns null after printing the
// failure message.
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

        // An explicit directory is resolved the same way the working directory is. Handing the folder
        // straight through used to make the compiler glob every .cs beneath it, so `build .` in a
        // repository root compiled sibling projects' sources - under whichever project's properties
        // happened to be picked - instead of building the solution it holds. A path that is a file
        // (a .sln/.slnx, or any other) is already the target and is passed straight on.
        if (!Directory.Exists(fullPath))
            return fullPath;

        return ResolveDirectory(fullPath, pathArg, checkRunnable, explicitPath: true);
    }

    return ResolveDirectory(Directory.GetCurrentDirectory(), null, checkRunnable, explicitPath: false);
}

// Picks the one project/solution file in `dir`, mirroring MSBuild: a single candidate wins, several
// is ambiguous, none is MSB1003. The two ambiguity diagnostics are deliberately not unified -
// MSBuild emits MSB1011 for the working directory and MSB1050 when the directory is named on the
// command line, and `asGiven` is echoed verbatim in the latter.
static string? ResolveDirectory(string dir, string? asGiven, bool checkRunnable, bool explicitPath)
{
    var candidates = DirectChildProjectFiles(dir);
    if (candidates.Length == 0)
    {
        if (checkRunnable)
            WriteLine($"Couldn't find a project to run. Ensure a project exists in {dir}, or pass the path to the project using --project.");
        else
            WriteLine("MSBUILD : error MSB1003: Specify a project or solution file. The current working directory does not contain a project or solution file.");
        return null;
    }
    if (candidates.Length > 1)
    {
        if (checkRunnable)
            WriteLine($"Specify which project file to use because {dir} contains more than one project file.");
        else if (explicitPath)
            WriteLine($"MSBUILD : error MSB1050: Specify which project or solution file to use because the folder \"{asGiven}\" contains more than one project or solution file.");
        else
            WriteLine("MSBUILD : error MSB1011: Specify which project or solution file to use because this folder contains more than one project or solution file.");
        return null;
    }

    var resolved = candidates[0];
    if (checkRunnable &&
        (resolved.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
         resolved.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)))
    {
        var parent = Path.GetDirectoryName(resolved) ?? ".";
        WriteLine($"Couldn't find a project to run. Ensure a project exists in {parent}, or pass the path to the project using --project.");
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
    // Mirrors `dotnet run` forwarding: only --project/-p are consumed as options, everything else
    // is forwarded to the app (positionals and unknown flags alike), and the first positional is
    // the project path - exactly like `dotnet run --project <proj> <args>` (the path is not
    // re-forwarded; dotnet only echoes a path given as a bare positional, a legacy quirk).
    string? path = null;
    var appArgs = new System.Collections.Generic.List<string>();
    bool afterSeparator = false;
    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg == "--") { afterSeparator = true; continue; }
        if (!afterSeparator && arg is "--project" or "-p")
        {
            if (i + 1 < args.Length) path = args[++i];
            continue;
        }
        if (!afterSeparator && path is null && !arg.StartsWith("-")) { path = arg; continue; }
        appArgs.Add(arg);
    }
    return (path, appArgs.ToArray());
}

// Restore and clean report paths the way the SDK does: relative to the current directory.
static string RelativePath(string path) =>
    string.IsNullOrEmpty(path)
        ? path
        : Path.GetRelativePath(Directory.GetCurrentDirectory(), path);

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

// The version of the SDK this compiler was built against, matching `dotnet --version` for that SDK.
// It comes from the payload embedded in the binary rather than by running `dotnet --version`, so it
// still works on a machine with no .NET SDK installed.
static string SdkVersion() => EmbeddedSdkRefs.SdkVersion;

void PrintHelp()
{
    WriteLine(HelpText());
}