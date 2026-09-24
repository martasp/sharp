using System.Threading;

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
        var fileName = Path.GetFileNameWithoutExtension(projectFilePath) ?? throw new Exception("File not found");

        // Extract the directory path of the project file.
        var projectDirectory = Path.GetDirectoryName(projectFilePath) ?? throw new Exception("Directory not found");

        var referencedProjectDir = Path.Combine(projectPath, projectDirectory);
        var referencedProjectCsproj = Path.Combine(referencedProjectDir, Path.GetFileName(projectFilePath));
        if (!File.Exists(referencedProjectCsproj))
            throw new Exception($"Cant find csproj {referencedProjectCsproj}");

        var referencedProjectSharpBinDir = Project.GetSharpBinDirectory(
            referencedProjectDir, Project.ResolveTargetFramework(referencedProjectCsproj));
        var dllPath = Path.Combine(referencedProjectSharpBinDir, $"{fileName}.dll");

        // Build the referenced project on demand when its output is missing. The compiler reports
        // the authoritative location via the build result; the final existence check retries briefly
        // so a freshly emitted file is always visible to this (possibly `..`-normalized) path probe.
        BuildResult buildResult = default!;
        if (!File.Exists(dllPath))
        {
            buildResult = NativeCompiler.Build(referencedProjectDir);
            if (!buildResult.Success)
                throw new Exception($"Cant build {referencedProjectDir}: {buildResult.Errors}");

            if (buildResult.Project?.DllPathName is { } reportedPath && File.Exists(reportedPath))
                return reportedPath;
        }

        for (var attempt = 0; attempt < 5 && !File.Exists(dllPath); attempt++)
            Thread.Sleep(50);

        if (File.Exists(dllPath)) return dllPath;

        var detail = buildResult.Project is null
            ? (success: true, dll: (string?)null, errors: "")
            : (success: buildResult.Success, dll: buildResult.Project.DllPathName, errors: buildResult.Errors);
        throw new Exception($"Cant find dll path {dllPath} (build success={detail.success}, "
            + $"dll={detail.dll}, errors=[{detail.errors}])");
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