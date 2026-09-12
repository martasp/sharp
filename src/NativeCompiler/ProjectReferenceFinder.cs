namespace NativeCompiler;

public class ProjectReferenceFinder
{
    public static string FindReferencedDLL(string projectFilePath, string projectPath)
    {
        // Extract the directory path of the project file.
        var fileName = Path.GetFileNameWithoutExtension(projectFilePath) ?? throw new Exception("File not found");

        // Extract the directory path of the project file.
        var projectDirectory = Path.GetDirectoryName(projectFilePath) ?? throw new Exception("Directory not found");

        var referencedProjectDir = Path.Combine(projectPath, projectDirectory);
        var referencedProjectSharpBinDir = Path.Combine(referencedProjectDir, "bin", "sharpbin");

        if (!Directory.Exists(referencedProjectSharpBinDir)) NativeCompiler.Build(referencedProjectDir);

        if (Directory.Exists(referencedProjectSharpBinDir))
        {
            var file = Path.Combine(referencedProjectSharpBinDir, $"{fileName}.dll");
            if (!File.Exists(file)) throw new Exception($"Cant find dll path {file}");
            return file;
        }

        throw new Exception("Cant find dll path"); // Package not found in NuGet package cache.
    }

    public static string? FindReferencedCsproj(string projectFilePath, string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectFilePath) ?? throw new Exception("Directory not found");

        var referencedProjectDir = Path.Combine(projectPath, projectDirectory);
        var csprojFile = Path.Combine(referencedProjectDir, Path.GetFileName(projectFilePath));

        return File.Exists(csprojFile) ? csprojFile : null;
    }
}