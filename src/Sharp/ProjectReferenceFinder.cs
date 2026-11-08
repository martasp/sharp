namespace Sharp;

public class ProjectReferenceFinder
{
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
        if (File.Exists(dllPath)) return dllPath;

        var buildResult = SharpCompiler.Build(referencedProjectDir, options);
        if (!buildResult.Success)
            throw new Exception($"Cant build {referencedProjectDir}: {buildResult.Errors}");

        return buildResult.Project?.DllPathName
            ?? throw new Exception($"Cant find dll path for {referencedProjectDir}");
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