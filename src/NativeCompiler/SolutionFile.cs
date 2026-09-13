using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NativeCompiler;

public sealed record SolutionProject(
    string Name,
    string ProjectTypeGuid,
    string ProjectRelativePath,
    string ProjectGuid,
    string ProjectPath);

public sealed class SolutionFile
{
    private static readonly Regex ProjectLine = new(
        @"^\s*Project\(""(?<type>[^""]+)""\)\s*=\s*""(?<name>[^""]+)"",\s*""(?<path>[^""]+)"",\s*""(?<guid>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string SolutionPath { get; private init; } = "";
    public IReadOnlyList<SolutionProject> Projects { get; private init; } = Array.Empty<SolutionProject>();

    public static SolutionFile Parse(string solutionPath)
    {
        var fullPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Solution file not found", fullPath);

        var solutionDir = Path.GetDirectoryName(fullPath) ?? "";
        return fullPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnx(fullPath, solutionDir)
            : ParseSln(fullPath, solutionDir);
    }

    private static SolutionFile ParseSln(string fullPath, string solutionDir)
    {
        var projects = new List<SolutionProject>();

        foreach (var rawLine in File.ReadLines(fullPath))
        {
            var match = ProjectLine.Match(rawLine);
            if (!match.Success) continue;

            var relativePath = match.Groups["path"].Value;
            if (!relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) continue;

            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            projects.Add(new SolutionProject(
                match.Groups["name"].Value,
                match.Groups["type"].Value,
                relativePath,
                match.Groups["guid"].Value,
                Path.GetFullPath(Path.Combine(solutionDir, normalized))));
        }

        return new SolutionFile { SolutionPath = fullPath, Projects = projects };
    }

    private static SolutionFile ParseSlnx(string fullPath, string solutionDir)
    {
        var projects = new List<SolutionProject>();
        var document = XDocument.Load(fullPath);

        foreach (var element in document.Descendants("Project"))
        {
            var buildAttr = element.Element("Build")?.Attribute("Project");
            if (buildAttr is not null &&
                (buildAttr.Value?.Equals("false", StringComparison.OrdinalIgnoreCase) ?? false))
                continue;

            var relativePath = (string?)element.Attribute("Path") ?? "";
            if (!relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) continue;

            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            projects.Add(new SolutionProject(
                Path.GetFileNameWithoutExtension(relativePath),
                "",
                relativePath,
                "",
                Path.GetFullPath(Path.Combine(solutionDir, normalized))));
        }

        return new SolutionFile { SolutionPath = fullPath, Projects = projects };
    }
}