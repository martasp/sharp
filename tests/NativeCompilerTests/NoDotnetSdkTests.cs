using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Shouldly;
using Xunit;

// Proves sharp is self-contained: it must compile a real project when there is no .NET SDK and no
// .NET runtime anywhere it can reach.
//
// bwrap does this with no privileges at all - no daemon, no sudo, no container. It runs as the
// invoking user via unprivileged user namespaces, and it hides the .NET install roots behind empty
// tmpfs mounts, so a passing build cannot be explained by an SDK that happens to be installed.
//
// The project under test is not hello-world: three projects joined by a ProjectReference, records,
// pattern matching, top-level statements, minimal-API routing, and two SDK source generators. The
// generator use is deliberate - it makes a *missing* generator a compile error rather than a silent
// behaviour change:
//   [GeneratedRegex] synthesises SlugRegex.Pattern(), without which `new SlugRegex()` cannot compile.
//   [JsonSerializable] synthesises PayloadJson.Default, which the code references directly, so a
//                      missing generator fails with CS0117.
// So a green build is evidence the embedded reference pack *and* the embedded analyzers were both
// used - not merely that a binary was copied somewhere.
public class NoDotnetSdkTests
{
    // Matches BootstrapPipelineTests / PerformanceComparisonTests: the AOT publish for this OS.
    private static readonly string Sharp = Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
        "src", "NativeCompilerCLI", "bin", "Release", "net10.0",
        OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", "publish",
        OperatingSystem.IsWindows() ? "NativeCompilerCLI.exe" : "NativeCompilerCLI");

    // The compiler is bind-mounted here; its host path does not exist inside the sandbox.
    private static readonly string SharpInside = "/sharp/" + Path.GetFileName(Sharp);

    private readonly ITestOutputHelper _output;

    public NoDotnetSdkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void BuildsComplexProjectWithNoDotnetSdk()
    {
        if (!OperatingSystem.IsLinux())
            return;

        if (!File.Exists(Sharp))
            throw new InvalidOperationException(
                $"Missing AOT seed at '{Sharp}'. Run publish.bat (or `dotnet publish " +
                "src/NativeCompilerCLI/NativeCompilerCLI.csproj -c Release -r linux-x64`) first.");

        if (!BwrapWorks())
        {
            _output.WriteLine("SKIP: bwrap is unavailable or unprivileged user namespaces are disabled.");
            return;
        }

        var root = WriteSampleSolution();
        try
        {
            // Precondition: there really is no SDK in there. Checked once, up front.
            var probe = Sandbox(root, "command -v dotnet || echo ON_PATH_NONE");
            probe.Exit.ShouldBe(0, probe.Output);
            probe.Output.ShouldContain("ON_PATH_NONE");
            _output.WriteLine("sandbox probe: " + probe.Output.Trim());

            // Precondition: the SDK must genuinely be unable to build inside the sandbox. This is
            // the negative control for the whole test - if `dotnet build` worked here, the sharp
            // build below would prove nothing. Same project, same sandbox, opposite outcome.
            var sdks = Sandbox(root, "dotnet build /src/Domain");
            sdks.Exit.ShouldNotBe(0, $"dotnet build unexpectedly succeeded:\n{sdks.Output}");
            sdks.Output.ShouldContain("not found");
            _output.WriteLine($"negative control: `dotnet build /src/Domain` exited {sdks.Exit}: " +
                sdks.Output.Trim());

            // Build all three projects. The generators either ran or these do not compile.
            var sharp = SharpInside;
            var build = Sandbox(root,
                $"{sharp} build /src/Domain && {sharp} build /src/Cli && {sharp} build /src/Api");
            build.Exit.ShouldBe(0, build.Output);
            build.Output.ShouldContain("Build succeeded");

            // /src is a bind mount of `root`, so the output is right here on the host. No need to
            // re-enter the sandbox to look at it.
            string bin(string project) => Path.Combine(root, project, "myc", "Debug", "net10.0");
            File.Exists(Path.Combine(bin("Domain"), "Domain.dll")).ShouldBeTrue();

            foreach (var app in new[] { "Cli", "Api" })
            {
                // The ProjectReference output rides along next to the app.
                File.Exists(Path.Combine(bin(app), "Domain.dll")).ShouldBeTrue();
                File.Exists(Path.Combine(bin(app), $"{app}.dll")).ShouldBeTrue();

                // The embedded apphost template is used and the result is made executable.
                var apphost = Path.Combine(bin(app), app);
                File.Exists(apphost).ShouldBeTrue();
                (File.GetUnixFileMode(apphost) & UnixFileMode.UserExecute).ShouldNotBe(UnixFileMode.None);

                // The runtime version comes from the embedded SDK manifest, not from a probe.
                var runtimeConfig = File.ReadAllText(Path.Combine(bin(app), $"{app}.runtimeconfig.json"));
                runtimeConfig.ShouldContain("\"version\": \"10.0");
            }

            // The web app must declare the ASP.NET Core shared framework or it cannot serve.
            File.ReadAllText(Path.Combine(bin("Api"), "Api.runtimeconfig.json"))
                .ShouldContain("Microsoft.AspNetCore.App");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    // Runs a shell script in a sandbox where no .NET is reachable. `projectRoot` is bind-mounted
    // writable at /src, which is also how the compiler's output stays visible to the host.
    private static (int Exit, string Output) Sandbox(string projectRoot, string script)
    {
        var args = new List<string>();
        void Add(string flag, params string[] values) { args.Add(flag); args.AddRange(values); }

        // Read-only system bits so coreutils and the ELF loader work. On usrmerge distributions
        // /bin and /lib are symlinks into /usr, and bwrap cannot --ro-bind a symlink, so /usr is
        // bound and the symlinks recreated.
        Add("--ro-bind", "/usr", "/usr");
        Add("--ro-bind", "/etc", "/etc");
        foreach (var link in new[] { "/bin", "/sbin" }) Add("--symlink", "usr/bin", link);
        foreach (var link in new[] { "/lib", "/lib64" }) Add("--symlink", "usr/lib", link);

        // An empty /tmp and an empty HOME keep the run cold: no NuGet package cache, and no
        // previously extracted payload in ~/.cache/sharp, so the reference pack is re-extracted
        // from the binary on every run.
        Add("--tmpfs", "/tmp");
        Add("--tmpfs", "/isolated-home");

        // Mask the SDK install root, but only if it exists: bwrap must create a mount point before
        // mounting on it and cannot inside the read-only /usr bind. A directory that isn't there
        // hides nothing anyway.
        if (Directory.Exists("/usr/share/dotnet"))
            Add("--tmpfs", "/usr/share/dotnet");

        Add("--bind", projectRoot, "/src");
        Add("--ro-bind", Path.GetDirectoryName(Sharp)!, "/sharp");
        Add("--dev", "/dev");
        Add("--proc", "/proc");

        // --setenv takes the name and the value as two separate arguments.
        Add("--setenv", "HOME", "/isolated-home");
        Add("--setenv", "TMPDIR", "/tmp");
        // Excludes ~/.local/bin and the mise shims, where an SDK would otherwise be found.
        Add("--setenv", "PATH", "/usr/bin:/bin");
        Add("--setenv", "DOTNET_MULTILEVEL_LOOKUP", "0");
        Add("--unsetenv", "DOTNET_ROOT");
        Add("--die-with-parent");

        args.AddRange(new[] { "/bin/sh", "-c", script });

        var psi = new ProcessStartInfo("bwrap") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    // bwrap is on most modern Linux, but unprivileged user namespaces can be disabled by a hardened
    // kernel or sysctl. Probe it so the test skips instead of failing for an unrelated reason.
    private static bool BwrapWorks()
    {
        var psi = new ProcessStartInfo("bwrap") { RedirectStandardError = true };
        foreach (var arg in new[] { "--ro-bind", "/", "/", "/bin/true" })
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return false;
            process.WaitForExit(30_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Three projects joined by a ProjectReference. No PackageReference anywhere, so the run cannot
    // silently start depending on network access.
    private static string WriteSampleSolution()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharp-nosdk-" + Path.GetRandomFileName());

        void Write(string project, string file, string content)
        {
            var dir = Path.Combine(root, project);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, file), content);
        }

        Write("Domain", "Domain.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);

        Write("Domain", "Models.cs", """
            namespace Domain;

            public abstract record Shape(string Name)
            {
                public abstract double Area { get; }
            }

            public sealed record Circle(double Radius) : Shape("circle")
            {
                public override double Area => Math.PI * Radius * Radius;
            }

            public sealed record Rect(double W, double H) : Shape("rect")
            {
                public override double Area => W * H;
            }

            public static class Geometry
            {
                public static double Total(IEnumerable<Shape> shapes) =>
                    shapes.Sum(static s => s.Area is var a && a > 0 ? a : 0);

                public static string Describe(Shape shape) => shape switch
                {
                    Circle { Radius: > 100 } => "huge circle",
                    Circle => "circle",
                    Rect => "rect",
                    _ => throw new ArgumentOutOfRangeException(nameof(shape)),
                };
            }
            """);

        Write("Cli", "Cli.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Domain/Domain.csproj" />
              </ItemGroup>
            </Project>
            """);

        Write("Cli", "Program.cs", """
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.RegularExpressions;
            using Domain;

            var shapes = new Shape[] { new Circle(2), new Rect(3, 4) };
            Console.WriteLine($"total={Geometry.Total(shapes):F2}");
            Console.WriteLine(string.Join(",", shapes.Select(Geometry.Describe)));
            Console.WriteLine("slug=" + new SlugRegex().Slugify("Hello Sharp World"));
            Console.WriteLine(JsonSerializer.Serialize(
                new Payload(42, "sharp", [1, 2, 3]), PayloadJson.Default.Payload));

            public partial class SlugRegex : Regex
            {
                [GeneratedRegex("[^a-z0-9]+", RegexOptions.IgnoreCase)]
                private static partial Regex Pattern();

                public string Slugify(string s) =>
                    Pattern().Replace(s, "-").Trim('-').ToLowerInvariant();
            }

            [JsonSerializable(typeof(Payload))]
            public partial class PayloadJson : JsonSerializerContext { }

            public record Payload(int Id, string Name, int[] Values);
            """);

        Write("Api", "Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Domain/Domain.csproj" />
              </ItemGroup>
            </Project>
            """);

        Write("Api", "Program.cs", """
            using Domain;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/shapes", () => Results.Ok(new[]
            {
                new { name = "circle", area = new Circle(2).Area },
                new { name = "rect", area = new Rect(3, 4).Area },
            }));

            app.MapGet("/describe/{name}", (string name) => name switch
            {
                "circle" => Geometry.Describe(new Circle(2)),
                "rect" => Geometry.Describe(new Rect(3, 4)),
                _ => "unknown",
            });

            app.Run();
            """);

        return root;
    }
}