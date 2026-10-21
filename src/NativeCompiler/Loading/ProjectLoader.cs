namespace NativeCompiler;

public static class ProjectLoader
{
    // Reads the project's source files (paths), loads their contents, and produces the Project model.
    [Query]
    public static Project Load(string projectPath)
    {
        var projectFiles = NativeCompilerUtils.ReadAllProjectFilesPath(projectPath);
        var files = NativeCompilerUtils.ReadAllFilesInMemory(projectFiles); // slow part 1300ms => now 6ms
        return NativeCompilerUtils.GetProjectDetails(files, projectPath);
    }
}
