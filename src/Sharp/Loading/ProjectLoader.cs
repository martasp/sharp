namespace Sharp;

public static class ProjectLoader
{
    // Reads the project's source files (paths), loads their contents, and produces the Project model.
    [Query]
    public static Project Load(string projectPath)
    {
        var projectFiles = SharpUtils.ReadAllProjectFilesPath(projectPath);
        var files = SharpUtils.ReadAllFilesInMemory(projectFiles); // slow part 1300ms => now 6ms
        return SharpUtils.GetProjectDetails(files, projectPath);
    }
}
