using XML;

namespace Sharp;

public static class ProjectGraph
{
    [Pure]
    public static bool IsSolution(string path)
        => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    [Query]
    public static IReadOnlyList<string> GetProjectPathsFromSolution(string solutionPath)
        => SolutionFile.Parse(solutionPath).Projects
            .Select(project => project.ProjectPath)
            .ToList();

    // Distinct project directories a target covers: one per project for a solution, the target itself
    // for a single project. Lets clean inspect/act on a target without importing the build pipeline.
    [Query]
    public static string[] GetProjectDirectories(string target)
        => IsSolution(target)
            ? GetProjectPathsFromSolution(target)
                .Select(Path.GetDirectoryName)
                .Where(directory => !string.IsNullOrEmpty(directory))
                .Select(directory => directory!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : new[] { target };
}
