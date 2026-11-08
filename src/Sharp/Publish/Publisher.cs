using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sharp;

// `sharp publish`: managed framework-dependent output by default, NativeAOT when
// <PublishAot>true</PublishAot> (or -p:PublishAot=true) is requested. The command deliberately
// mirrors the SDK's most-used publish surface; unsupported switches are rejected by the CLI rather
// than silently approximated.
public static class Publisher
{
    [HasSideEffects]
    public static PublishResult Publish(PublishOptions options)
    {
        var timer = Stopwatch.StartNew();
        var fullPath = Path.GetFullPath(options.ProjectPath);
        var buildOptions = new BuildOptions(
            options.Configuration, options.RuntimeIdentifier, options.Properties);

        Project project;
        try
        {
            project = ProjectLoader.Load(fullPath, buildOptions);
        }
        catch (Exception exception)
        {
            return Failure(exception.Message, timer.Elapsed, null, null);
        }

        var publishAot = project.PublishAot;
        if (publishAot)
        {
            if (string.IsNullOrWhiteSpace(project.RuntimeIdentifier))
            {
                return Failure(
                    "RuntimeIdentifier is required for native compilation. " +
                    "Try running dotnet publish with the -r option value specified.",
                    timer.Elapsed, project, null);
            }

            if (!project.RuntimeIdentifier.Equals("linux-x64", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    $"NativeAOT publish currently supports only linux-x64 (requested '{project.RuntimeIdentifier}').",
                    timer.Elapsed, project, null);
            }

            if (!OperatingSystem.IsLinux() || RuntimeInformation.RuntimeIdentifier != "linux-x64")
            {
                return Failure(
                    "NativeAOT publish currently runs only on a linux-x64 host.",
                    timer.Elapsed, project, null);
            }
        }
        else if (!string.IsNullOrWhiteSpace(project.RuntimeIdentifier))
        {
            return Failure(
                "Publishing with a runtime identifier without PublishAot is not supported yet.",
                timer.Elapsed, project, null);
        }

        var build = ProjectBuild.Build(fullPath, buildOptions);
        if (!build.Success || build.Project is not { } builtProject)
            return Failure(build.Errors, timer.Elapsed, build.Project, null, build.Warnings);

        var publishDirectory = ResolvePublishDirectory(builtProject, options.OutputDirectory);
        try
        {
            var outputs = publishAot
                ? NativeAotPublisher.Publish(builtProject, publishDirectory, buildOptions)
                : CopyManaged(builtProject, publishDirectory);

            timer.Stop();
            return new PublishResult(
                true, "", outputs, timer.Elapsed, builtProject, publishDirectory, build.Warnings);
        }
        catch (Exception exception)
        {
            return Failure(exception.Message, timer.Elapsed, builtProject, publishDirectory, build.Warnings);
        }
    }

    private static string ResolvePublishDirectory(Project project, string? outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            return Path.GetFullPath(outputDirectory);

        var tfm = project.TargetFramework ?? DefaultTargetFramework.Value;
        var rid = project.RuntimeIdentifier;
        return string.IsNullOrWhiteSpace(rid)
            ? Path.Combine(project.SharpBinFolderPath, "publish")
            : Path.Combine(project.ProjectRootPath, OutputRoot(project.ProjectRootPath, project.Configuration, tfm), rid, "publish");
    }

    private static string OutputRoot(string projectRootPath, string configuration, string tfm)
    {
        var root = Environment.GetEnvironmentVariable("SHARP_OUTPUT_ROOT") ?? "myc";
        return Path.Combine(root, configuration, tfm);
    }

    private static List<string> CopyManaged(Project project, string publishDirectory)
    {
        var source = project.SharpBinFolderPath;
        var destination = Path.GetFullPath(publishDirectory);
        Directory.CreateDirectory(destination);

        var outputs = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFullPath(file).StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;

            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            outputs.Add(target);
        }

        return outputs;
    }

    private static PublishResult Failure(
        string errors,
        TimeSpan duration,
        Project? project,
        string? publishDirectory,
        string warnings = "") =>
        new(false, errors, Array.Empty<string>(), duration, project, publishDirectory, warnings);
}
