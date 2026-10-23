using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Sharp;

public static class Runtime
{
    [HasSideEffects]
    public static (bool success, string errors) Run(string projectPath)
    {
        var build = ProjectBuild.Build(projectPath);
        if (build.Compilation is not { } compilation || build.Project is not { } project)
            return (false, build.Errors);

        var embeddedTexts = build.EmbeddedTexts?.ToList() ?? new List<EmbeddedText>();

        var runErrors = project.ResolvedProjectOutputKind() == OutputKind.ConsoleApplication
            ? Output.RunAssembly(compilation, project, embeddedTexts)
            : (success: true, errors: "");

        return (runErrors.success && build.Success, runErrors.errors + build.Errors);
    }
}
