namespace NativeCompiler;

public class ProjectReferenceFinder
{
    public static string FindReferencedDLL(string projectFilePath, string projectPath)
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

        // Build owns the single-flight and the authoritative output location: the build result reports
        // exactly where it emitted (Project.DllPathName), so there is no retry loop and no separately
        // computed path that could miss a freshly emitted file.
        var buildResult = NativeCompiler.Build(referencedProjectDir);
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