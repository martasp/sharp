using System.Diagnostics;

namespace NativeCompiler;

public sealed record CleanResult(
    int Removed,
    IReadOnlyList<string> Warnings,
    TimeSpan Duration);

// Removes this compiler's own output trees for a project or solution. Independent of the build
// pipeline: it only enumerates project roots (ProjectGraph) and deletes folders, so it works even
// for projects that no longer compile.
public static class Cleaner
{
    [HasSideEffects]
    public static CleanResult Clean(string target, string configuration)
    {
        var timer = Stopwatch.StartNew();
        var warnings = new List<string>();
        var removed = 0;

        foreach (var root in ProjectGraph.GetProjectDirectories(target))
        {
            foreach (var candidate in Targets(root, configuration))
            {
                if (!Directory.Exists(candidate)) continue;
                try
                {
                    Directory.Delete(candidate, recursive: true);
                    removed++;
                }
                catch (Exception exception)
                {
                    warnings.Add($"could not delete '{candidate}': {exception.Message}");
                }
            }
        }

        return new CleanResult(removed, warnings, timer.Elapsed);
    }

    // Only this compiler's own trees. bin/ belongs to the SDK and is what the parity tests compare
    // against, so deleting it would destroy the comparison baseline.
    [Pure]
    private static IEnumerable<string> Targets(string projectDirectory, string configuration)
    {
        var root = Environment.GetEnvironmentVariable("NATIVECOMPILER_OUTPUT_ROOT") ?? "myc";
        yield return Path.Combine(projectDirectory, root, configuration);
        yield return Path.Combine(projectDirectory, "obj");
    }
}
