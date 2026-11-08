using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Sharp.NuGet;

namespace Sharp;

// Public entry points. The implementation lives in the intent-based modules:
//   ProjectBuild (single project), SolutionBuild (wave scheduler), Runtime (run),
//   Output (emit/run assembly), References/FrameworkFix (reference set), ProjectLoader,
//   ProjectGraph, and the BuildProps/analyzer pipeline.
public static class SharpCompiler
{
    public static BuildResult Build(string projectPath, BuildOptions? options = null)
        => ProjectBuild.Build(projectPath, options);

    public static BuildSolutionResult BuildSolution(string solutionPath)
        => SolutionBuild.Build(solutionPath);

    public static (bool success, string errors) Run(string projectPath)
        => Runtime.Run(projectPath);

    [HasSideEffects]
    public static PublishResult Publish(PublishOptions options)
        => Publisher.Publish(options);

    // Library command surface for restore/clean (mirrors CLI commands so the core is usable as a
    // library without coupling to the CLI front-end).
    [Query]
    public static Task<RestoreResult> RestoreProject(string projectDirectory, CancellationToken cancellationToken = default)
        => PackageRestorer.RestoreProject(projectDirectory, cancellationToken);

    [Query]
    public static Task<(bool success, IReadOnlyList<RestoreProjectResult> projects, TimeSpan duration)> RestoreSolution(string solutionPath, CancellationToken cancellationToken = default)
        => PackageRestorer.RestoreSolution(solutionPath, cancellationToken);

    public static CleanResult Clean(string target, string configuration = "Debug")
        => Cleaner.Clean(target, configuration);
}

public sealed record BuildResult(
    bool Success,
    string Errors,
    IReadOnlyList<string> Outputs,
    TimeSpan Duration,
    CSharpCompilation? Compilation,
    Project? Project,
    IEnumerable<EmbeddedText>? EmbeddedTexts,
    string Warnings);

public sealed record SolutionProjectResult(
    string ProjectName,
    string ProjectFile,
    BuildResult Result);

public sealed record BuildSolutionResult(
    bool Success,
    IReadOnlyList<SolutionProjectResult> Projects,
    IReadOnlyList<string> Errors,
    TimeSpan Duration);
