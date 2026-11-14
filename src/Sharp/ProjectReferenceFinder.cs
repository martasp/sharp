using System.Collections.Concurrent;

namespace Sharp;

public class ProjectReferenceFinder
{
    private static readonly ConcurrentDictionary<string, DateTime> LatestSourceTimestamp = new(
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> GeneratedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "myc", "sharpbin", "publish", "TestResults", ".git", ".vs", "node_modules",
    };

    public static string FindReferencedDLL(string projectFilePath, string projectPath, BuildOptions? options = null)
    {
        // ProjectReference Include values use Windows separators (backslashes) even on
        // Linux checkouts; normalize before any Path operation (Path.GetDirectoryName /
        // Path.GetFileName treat '\' as a regular char on Linux).
        projectFilePath = projectFilePath.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        // Extract the directory path of the project file.
        var projectDirectory = Path.GetDirectoryName(projectFilePath) ?? throw new Exception("Directory not found");

        var referencedProjectDir = Path.Combine(projectPath, projectDirectory);
        var referencedProjectCsproj = Path.Combine(referencedProjectDir, Path.GetFileName(projectFilePath));
        if (!File.Exists(referencedProjectCsproj))
            throw new Exception($"Cant find csproj {referencedProjectCsproj}");

        // The wave plan builds every referenced project before its dependents, so in a solution build
        // the output is present and we just locate it; only a standalone build reaches the missing
        // case. GetOutputDllPath resolves TargetFramework the same way compilation does, so the probe
        // cannot drift from the emitted path.
        var dllPath = Project.GetOutputDllPath(
            Path.GetFullPath(referencedProjectDir), referencedProjectCsproj, options?.Configuration);
        if (File.Exists(dllPath) && !IsOutputStale(referencedProjectDir, referencedProjectCsproj, dllPath))
            return dllPath;

        var buildResult = SharpCompiler.Build(referencedProjectDir, options);
        if (!buildResult.Success)
            throw new Exception($"Cant build {referencedProjectDir}: {buildResult.Errors}");

        return buildResult.Project?.DllPathName
            ?? throw new Exception($"Cant find dll path for {referencedProjectDir}");
    }

    // A prior standalone build can leave a referenced project's myc output behind. Reusing it just
    // because the file exists compiles the consumer against stale metadata (for example after a
    // reference was added to the referenced assembly). Check the project and its source inputs
    // before treating the DLL as current; a real solution build still hits the fast path.
    private static bool IsOutputStale(string projectDir, string csproj, string dllPath)
    {
        var outputTimestamp = File.GetLastWriteTimeUtc(dllPath);
        if (File.GetLastWriteTimeUtc(csproj) > outputTimestamp) return true;

        var latestSource = LatestSourceTimestamp.GetOrAdd(
            Path.GetFullPath(projectDir), FindLatestSourceTimestamp);
        return latestSource > outputTimestamp;
    }

    private static DateTime FindLatestSourceTimestamp(string projectDir)
    {
        var latest = DateTime.MinValue;
        var pending = new Stack<string>();
        pending.Push(projectDir);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var extension = Path.GetExtension(file);
                    if (extension is not (".cs" or ".razor" or ".cshtml" or ".resx" or ".props" or ".targets" or ".json"))
                        continue;

                    var timestamp = File.GetLastWriteTimeUtc(file);
                    if (timestamp > latest) latest = timestamp;
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    if (!GeneratedDirectoryNames.Contains(Path.GetFileName(child)))
                        pending.Push(child);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort: an unreadable directory cannot make a current output stale.
            }
            catch (IOException)
            {
                // Same for a directory removed while the build is inspecting it.
            }
        }

        return latest;
    }

    public static string? FindReferencedCsproj(string projectFilePath, string projectPath)
    {
        projectFilePath = projectFilePath.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var projectDirectory = Path.GetDirectoryName(projectFilePath) ?? throw new Exception("Directory not found");

        var referencedProjectDir = Path.Combine(projectPath, projectDirectory);
        var csprojFile = Path.Combine(referencedProjectDir, Path.GetFileName(projectFilePath));

        return File.Exists(csprojFile) ? csprojFile : null;
    }
}