using NativeCompiler.NuGet;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NativeCompiler;
using Shouldly;
using Xunit;

namespace NativeCompilerTests;

/// <summary>
/// Builds .nupkg files on disk so restore can be exercised end to end without a network.
/// A flat-container feed is just directories: &lt;id&gt;/&lt;version&gt;/&lt;id&gt;.&lt;version&gt;.nupkg.
/// </summary>
internal sealed class LocalFeed : IDisposable
{
    public string Root { get; }
    public string PackagesFolder { get; }

    public LocalFeed()
    {
        Root = Path.Combine(Path.GetTempPath(), "sharp-feed-" + Guid.NewGuid().ToString("N")[..10]);
        PackagesFolder = Path.Combine(Root, "cache");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(PackagesFolder);
    }

    // Adds a package. `dependencies` are nuspec dependency lines for the given group.
    public LocalFeed WithPackage(string id, string version, string targetFramework = "net10.0",
        string? libDllName = null, params (string Id, string Version)[] dependencies)
    {
        var directory = Path.Combine(Root, id.ToLowerInvariant(), version);
        Directory.CreateDirectory(directory);

        var nupkgPath = Path.Combine(directory, $"{id.ToLowerInvariant()}.{version}.nupkg");
        if (File.Exists(nupkgPath)) return this;

        var nuspec = new StringBuilder();
        nuspec.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        nuspec.Append("<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">");
        nuspec.Append($"<metadata><id>{id}</id><version>{version}</version>");
        nuspec.Append($"<authors>test</authors><description>test package</description></metadata>");
        if (dependencies.Length > 0)
        {
            nuspec.Append($"<dependencies><group targetFramework=\"{targetFramework}\">");
            foreach (var (dependencyId, dependencyVersion) in dependencies)
                nuspec.Append($"<dependency id=\"{dependencyId}\" version=\"{dependencyVersion}\" />");
            nuspec.Append("</group></dependencies>");
        }
        nuspec.Append("</package>");

        using (var file = File.Create(nupkgPath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            // The nuspec name keeps the package's ORIGINAL casing, exactly as a real feed does -
            // only the on-disk folder is lowercased. Anything that lowercases this lookup fails.
            WriteEntry(archive, $"{id}.nuspec", nuspec.ToString());

            if (libDllName is not null)
                WriteEntry(archive, $"lib/{targetFramework}/{libDllName}", "not-a-real-assembly");

            // The flat container's version index for this package.
            var sibling = Path.GetDirectoryName(nupkgPath)!;
        }

        WriteVersionIndex(id, version);
        return this;
    }

    // Writes a version index listing every version currently present for `id`, which is the
    // shape NuGetFeed.GetVersionsAsync parses.
    private void WriteVersionIndex(string id, string version)
    {
        var packageDirectory = Path.Combine(Root, id.ToLowerInvariant());

        // Each immediate subdirectory is one version; the package's index.json lives beside them.
        var versions = Directory.EnumerateDirectories(packageDirectory)
            .Select(Path.GetFileName)
            .Where(name => name is not null && name != "index.json")
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (!versions.Contains(version))
        {
            versions.Add(version);
            versions.Sort(StringComparer.Ordinal);
        }

        var index = "{\"version\":\"1.0.0\",\"versions\":[" +
                    string.Join(",", versions.Select(v => $"\"{v}\"")) + "]}";
        File.WriteAllText(Path.Combine(packageDirectory, "index.json"), index);
    }

    public string NuGetConfigText => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="local" value="{Root.Replace('\\', '/')}" />
          </packageSources>
        </configuration>
        """;

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch { /* best effort */ }
    }
}

/// <summary>
/// Exercises the pieces of restore that do not need a network: version resolution, nuspec
/// dependency-group selection, extraction safety, and nuget.config parsing.
/// </summary>
public class RestoreTests
{
    // --- VersionResolver ----------------------------------------------------

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("[1.2.3]", "1.2.3")]
    public void Resolve_ExactVersion_IsAccepted(string requested, string expected)
    {
        var result = VersionResolver.Resolve(requested, new[] { "1.0.0", "1.2.3", "2.0.0" });

        result.Success.ShouldBeTrue();
        result.Version.ShouldBe(expected);
    }

    [Fact]
    public void Resolve_TwoPartVersion_NormalisesToThreePart()
    {
        // "1.2" means 1.2.0, so it matches a feed publishing 1.2.0 and not one publishing 1.2.3.
        VersionResolver.Resolve("1.2", new[] { "1.2.0" }).Version.ShouldBe("1.2.0");
        VersionResolver.Resolve("1.2", new[] { "1.2.3" }).Success.ShouldBeFalse();
    }

    [Theory]
    [InlineData("[1.0.0, )", "1.0.0")]
    [InlineData("[1.0.0, 2.0.0)", "1.0.0")]
    [InlineData("[1.5.0, )", "1.5.0")]
    [InlineData("(1.0.0, )", "1.2.3")]
    [InlineData("(, 2.0.0]", "1.0.0")]
    public void Resolve_Range_PicksLowestSatisfying(string requested, string expected)
    {
        var result = VersionResolver.Resolve(requested, new[] { "1.0.0", "1.2.3", "1.5.0", "2.0.0" });

        result.Success.ShouldBeTrue();
        result.Version.ShouldBe(expected);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("1.2.*")]
    [InlineData("latest")]
    public void Resolve_FloatingVersion_IsRejectedWithGuidance(string requested)
    {
        var result = VersionResolver.Resolve(requested, new[] { "1.0.0", "1.2.3" });

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("floating version");
        result.Error.ShouldContain("not supported");
    }

    [Fact]
    public void Resolve_ExactVersionAbsentFromFeed_Fails()
    {
        var result = VersionResolver.Resolve("9.9.9", new[] { "1.0.0", "2.0.0" });

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("not found on the package source");
    }

    [Fact]
    public void Resolve_RangeWithoutFeed_Fails()
    {
        var result = VersionResolver.Resolve("[1.0.0, )", null);

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("without a package source");
    }

    // --- nuspec dependency groups -------------------------------------------

    [Fact]
    public void ReadDependencies_SelectsMatchingTargetFrameworkGroup()
    {
        using var workspace = new TempWorkspace();
        workspace.ExtractNuspec("sample.pkg", """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata><id>Sample</id><version>1.0.0</version></metadata>
              <dependencies>
                <group targetFramework="net10.0">
                  <dependency id="Modern.Dep" version="2.0.0" />
                </group>
                <group targetFramework="net472">
                  <dependency id="Legacy.Dep" version="1.0.0" />
                </group>
              </dependencies>
            </package>
            """);

        var dependencies = NuGetPackage.ReadDependencies(workspace.PackageDirectory, "net10.0");

        dependencies.ShouldHaveSingleItem();
        dependencies[0].Id.ShouldBe("Modern.Dep");
        dependencies[0].Version.ShouldBe("2.0.0");
    }

    [Fact]
    public void ReadDependencies_HonoursExcludeAll()
    {
        using var workspace = new TempWorkspace();
        workspace.ExtractNuspec("sample.pkg", """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata><id>Sample</id><version>1.0.0</version></metadata>
              <dependencies>
                <group targetFramework="net10.0">
                  <dependency id="Flows" version="1.0.0" />
                  <dependency id="Stops" version="1.0.0" exclude="All" />
                </group>
              </dependencies>
            </package>
            """);

        var dependencies = NuGetPackage.ReadDependencies(workspace.PackageDirectory, "net10.0");

        dependencies.ShouldContain(dependency => dependency.Id == "Flows");
        dependencies.ShouldContain(dependency => dependency.Id == "Stops" && dependency.Excluded);
    }

    [Fact]
    public void ReadDependencies_LowersBracketRangeToMinimum()
    {
        using var workspace = new TempWorkspace();
        workspace.ExtractNuspec("sample.pkg", """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata><id>Sample</id><version>1.0.0</version></metadata>
              <dependencies>
                <group targetFramework="net10.0">
                  <dependency id="Ranged" version="[1.2.3, )" />
                </group>
              </dependencies>
            </package>
            """);

        var dependencies = NuGetPackage.ReadDependencies(workspace.PackageDirectory, "net10.0");

        dependencies.ShouldHaveSingleItem();
        dependencies[0].Version.ShouldBe("1.2.3");
    }

    // --- PackageExtractor ---------------------------------------------------

    [Fact]
    public void Extract_CreatesExpectedLayoutAndMetadataMarker()
    {
        using var feed = new LocalFeed();
        using var stream = PackageStream(feed, "Simple.Pkg", "1.0.0");

        PackageExtractor.Extract(stream, feed.PackagesFolder, "Simple.Pkg", "1.0.0");

        var directory = PackageExtractor.PackageDirectory(feed.PackagesFolder, "Simple.Pkg", "1.0.0");
        directory.ShouldBe(Path.Combine(feed.PackagesFolder, "simple.pkg", "1.0.0"));
        Directory.Exists(directory).ShouldBeTrue();
        File.Exists(Path.Combine(directory, "Simple.Pkg.nuspec")).ShouldBeTrue();
        File.Exists(Path.Combine(directory, PackageExtractor.MetadataFileName)).ShouldBeTrue();
        PackageExtractor.IsExtracted(feed.PackagesFolder, "Simple.Pkg", "1.0.0").ShouldBeTrue();
    }

    [Fact]
    public void Extract_RejectsEntryEscapingDestination()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("../../escaped.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("owned");
        }

        stream.Position = 0;

        var cache = Path.Combine(Path.GetTempPath(), "sharp-zipslip-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            Should.Throw<InvalidOperationException>(() =>
                PackageExtractor.Extract(stream, Path.Combine(cache, "pkgs"), "Evil", "1.0.0"));
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void Extract_LeavesNoTemporaryDirectoryBehind()
    {
        using var feed = new LocalFeed();
        using var stream = PackageStream(feed, "Tidy.Pkg", "1.0.0");

        PackageExtractor.Extract(stream, feed.PackagesFolder, "Tidy.Pkg", "1.0.0");

        Directory.EnumerateDirectories(feed.PackagesFolder, ".tmp-*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void VerifyHash_AcceptsMatchingDigestAndRejectsMismatch()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
        var digest = PackageExtractor.ComputeSha512(stream);

        stream.Position = 0;
        PackageExtractor.VerifyHash(stream, digest).ShouldBeTrue();

        stream.Position = 0;
        PackageExtractor.VerifyHash(stream, new string('A', 88)).ShouldBeFalse();
    }

    // --- NuGetConfig --------------------------------------------------------

    [Fact]
    public void NuGetConfig_ReadsSourcesWithClearSemantics()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("nuget.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="https://example.test/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var config = NuGetConfig.Load(workspace.Root);

        config.Sources.ShouldHaveSingleItem();
        config.Sources[0].Key.ShouldBe("local");
        config.Sources[0].Value.ShouldBe("https://example.test/v3/index.json");
    }

    [Fact]
    public void NuGetConfig_HonoursGlobalPackagesFolderSetting()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("nuget.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="custom-packages" />
              </config>
            </configuration>
            """);

        var config = NuGetConfig.Load(workspace.Root);
        var resolved = NuGetConfig.ResolveGlobalPackagesFolder(config.GlobalPackagesFolder, workspace.Root);

        resolved.ShouldBe(Path.Combine(workspace.Root, "custom-packages"));
    }

    [Fact]
    public void NuGetConfig_NearestConfigWinsOverOuterOne()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("nuget.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="outer" value="https://outer.test/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var nested = Path.Combine(workspace.Root, "src", "App");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="inner" value="https://inner.test/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var config = NuGetConfig.Load(nested);

        config.Sources.ShouldHaveSingleItem();
        config.Sources[0].Key.ShouldBe("inner");
    }

    [Fact]
    public void NuGetConfig_DefaultsToNugetOrgWhenNoneConfigured()
    {
        using var workspace = new TempWorkspace();

        var config = NuGetConfig.Load(workspace.Root);

        config.Sources.ShouldNotBeEmpty();
        config.Sources[0].Value.ShouldBe(NuGetConfig.DefaultSource);
    }

    // --- PackageRestorer ----------------------------------------------------

    [Fact]
    public async Task RestoreProject_ReportsNamedErrorForUnresolvableVersion()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("app.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Definitely.Missing" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        workspace.WriteFile("nuget.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="empty" value="https://localhost:1/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        var result = await PackageRestorer.RestoreProject(workspace.Root);

        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Contains("Definitely.Missing"));
    }

    [Fact]
    public async Task RestoreProject_ReportsClearErrorForFloatingVersion()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("app.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Floating.Pkg" Version="1.0.*" />
              </ItemGroup>
            </Project>
            """);

        var result = await PackageRestorer.RestoreProject(workspace.Root);

        result.Success.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Contains("floating version"));
    }

    [Fact]
    public async Task RestoreProject_RestoresTransitiveDependencyFromLocalFolderFeed()
    {
        using var feed = new LocalFeed();
        feed.WithPackage("Leaf.Pkg", "1.0.0", libDllName: "Leaf.dll");
        feed.WithPackage("Root.Pkg", "2.0.0", dependencies: [("Leaf.Pkg", "1.0.0")], libDllName: "Root.dll");

        using var workspace = new TempWorkspace();
        workspace.WriteFile("nuget.config", $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="cache" />
              </config>
              <packageSources>
                <clear />
                <add key="local" value="{{feed.Root}}" />
              </packageSources>
            </configuration>
            """);
        workspace.WriteFile("app.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Root.Pkg" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """);

        var result = await PackageRestorer.RestoreProject(workspace.Root);

        result.Success.ShouldBeTrue(string.Join("\n", result.Errors));
        result.PackagesDownloaded.ShouldContain("Root.Pkg/2.0.0");
        result.PackagesDownloaded.ShouldContain("Leaf.Pkg/1.0.0");

        // Both packages are materialised in the global cache under the layout the compiler later
        // reads (id/version, lowercase folder, ORIGINAL-cased nuspec inside).
        var expected = new Dictionary<string, string> { ["Root.Pkg"] = "2.0.0", ["Leaf.Pkg"] = "1.0.0" };
        foreach (var (id, version) in expected)
        {
            var cache = Path.Combine(workspace.Root, "cache", id.ToLowerInvariant(), version);

            Directory.GetFiles(cache, "*.nuspec").ShouldHaveSingleItem();
            Directory.GetFiles(cache, "*.dll", SearchOption.AllDirectories).ShouldNotBeEmpty();
            File.Exists(Path.Combine(cache, ".nupkg.metadata")).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task RestoreProject_ResolvesRangeAgainstFolderFeedVersions()
    {
        using var feed = new LocalFeed();
        feed.WithPackage("Ranged.Pkg", "1.0.0", libDllName: "Ranged.dll");
        feed.WithPackage("Ranged.Pkg", "1.5.0");

        using var workspace = new TempWorkspace();
        workspace.WriteFile("nuget.config", $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="cache" />
              </config>
              <packageSources>
                <clear />
                <add key="local" value="{{feed.Root}}" />
              </packageSources>
            </configuration>
            """);
        workspace.WriteFile("app.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Ranged.Pkg" Version="[1.0.0, )" />
              </ItemGroup>
            </Project>
            """);

        var result = await PackageRestorer.RestoreProject(workspace.Root);

        result.Success.ShouldBeTrue(string.Join("\n", result.Errors));

        // Lowest satisfying version wins, matching NuGet's range resolution.
        result.PackagesDownloaded.ShouldContain("Ranged.Pkg/1.0.0");
        result.PackagesDownloaded.ShouldNotContain("Ranged.Pkg/1.5.0");
    }

    [Fact]
    public async Task RestoreProject_ReportsMissingPackageWithoutNetworkAccess()
    {
        using var feed = new LocalFeed();

        using var workspace = new TempWorkspace();
        workspace.WriteFile("nuget.config", $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="cache" />
              </config>
              <packageSources>
                <clear />
                <add key="local" value="{{feed.Root}}" />
              </packageSources>
            </configuration>
            """);
        workspace.WriteFile("app.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Absent.Pkg" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var result = await PackageRestorer.RestoreProject(workspace.Root);

        result.Success.ShouldBeFalse();
        result.Warnings.ShouldContain(warning => warning.Contains("Absent.Pkg"));
    }

    [Fact]
    public async Task RestoreProject_UsesCentralVersionFromDirectoryPackagesProps()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("Directory.Packages.props", """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Central.Pkg" Version="4.5.6" />
              </ItemGroup>
            </Project>
            """);
        workspace.WriteFile("app.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Central.Pkg" />
              </ItemGroup>
            </Project>
            """);

        // With no version anywhere, restore must say "no version specified" rather than probing.
        var result = await PackageRestorer.RestoreProject(workspace.Root);

        // 4.5.6 was found centrally, so this must NOT be the NU1002 "no version" error.
        result.Errors.ShouldNotContain(error => error.Contains("NU1002"));
    }

    [Fact]
    public async Task RestoreProject_SucceedsWithNoPackageReferences()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("app.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        var result = await PackageRestorer.RestoreProject(workspace.Root);

        result.Success.ShouldBeTrue();
        result.PackagesDownloaded.ShouldBeEmpty();
    }

    // --- helpers ------------------------------------------------------------

    private static MemoryStream PackageStream(LocalFeed feed, string id, string version)
    {
        feed.WithPackage(id, version);
        var path = Path.Combine(feed.Root, id.ToLowerInvariant(), version, $"{id.ToLowerInvariant()}.{version}.nupkg");
        var bytes = File.ReadAllBytes(path);
        return new MemoryStream(bytes);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "sharp-restore-" + Guid.NewGuid().ToString("N")[..10]);

        public TempWorkspace() => Directory.CreateDirectory(Root);

        public void WriteFile(string name, string content) =>
            File.WriteAllText(Path.Combine(Root, name), content);

        public string PackageDirectory => Path.Combine(Root, "package");

        // FindDependencies globs *.nuspec in the package folder, so the file must carry that extension.
        public void ExtractNuspec(string packageId, string content)
        {
            Directory.CreateDirectory(PackageDirectory);
            File.WriteAllText(Path.Combine(PackageDirectory, $"{packageId}.nuspec"), content);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { /* best effort */ }
        }
    }
}