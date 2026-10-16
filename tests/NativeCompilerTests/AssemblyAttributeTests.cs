using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NativeCompiler;
using Shouldly;
using Xunit;

namespace NativeCompilerTests;

public class AssemblyAttributeTests
{
    private static string NewFixtureRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "assemblyattr-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteProject(string path, string body)
    {
        Write(path, "<Project Sdk=\"Microsoft.NET.Sdk\">\n" + body + "\n</Project>\n");
    }

    private static Project Load(string csprojPath)
    {
        var file = new ProjectFile(csprojPath, File.ReadAllText(csprojPath));
        return new Project(file, new List<ProjectFile>(), Path.GetDirectoryName(csprojPath)!);
    }

    // The manifest target reads its attribute type from the .targets file, so the fixture uses a
    // vendor-neutral type name: any product-specific string in the compiler would not match.
    private const string ManifestTarget = @"<Project>
  <Target Name=""CollectModuleNames"" AfterTargets=""AfterResolveReferences"">
    <MSBuild Targets=""GetModuleProjectName"" Projects=""@(_MSBuildProjectReferenceExistent)"" Condition=""'@(_MSBuildProjectReferenceExistent)' != ''"" SkipNonexistentTargets=""true"" ContinueOnError=""true"">
      <Output ItemName=""ModuleProjectNames"" TaskParameter=""TargetOutputs"" />
    </MSBuild>
    <ItemGroup>
      <ModuleNames Include=""@(ModulePackageNames);@(ModuleProjectNames)"" />
    </ItemGroup>
    <ItemGroup>
      <AssemblyAttribute Include=""Vendor.Manifest.ModuleNameAttribute"" Condition=""'@(ModuleNames)' != ''"">
        <_Parameter1>%(ModuleNames.Identity)</_Parameter1>
      </AssemblyAttribute>
    </ItemGroup>
  </Target>
</Project>";

    private const string ModuleTarget = @"<Project>
  <Target Name=""GetModuleProjectName"" Returns=""@(ModuleProjectName)"">
    <ItemGroup>
      <ModuleProjectName Include=""$(AssemblyName)"" />
    </ItemGroup>
  </Target>
</Project>";

    // The module target reaches module projects through a Directory.Build.targets in their folder,
    // so only module projects contribute a name.
    private static void WriteModuleTargetsFixture(string root)
    {
        Write(Path.Combine(root, "targets", "Application.Targets.targets"), ManifestTarget);
        Write(Path.Combine(root, "targets", "Module.Targets.targets"), ModuleTarget);
        Write(Path.Combine(root, "Modules", "Directory.Build.targets"),
            "<Project>\n  <Import Project=\"..\\targets\\Module.Targets.targets\" />\n</Project>\n");
    }

    [Fact]
    public void GetAttributeSources_EmitsStaticAssemblyAttributesWithPositionalAndNamedArguments()
    {
        var root = NewFixtureRoot();
        var csproj = Path.Combine(root, "App.csproj");
        WriteProject(csproj,
            "<ItemGroup>\n" +
            "  <AssemblyAttribute Include=\"System.Reflection.AssemblyMetadataAttribute\">\n" +
            "    <_Parameter1>Commit</_Parameter1>\n" +
            "    <_Parameter2>abc123</_Parameter2>\n" +
            "  </AssemblyAttribute>\n" +
            "  <AssemblyAttribute Include=\"System.Reflection.AssemblyTitleAttribute\">\n" +
            "    <_Parameter1>My App</_Parameter1>\n" +
            "  </AssemblyAttribute>\n" +
            "</ItemGroup>");

        var lines = AssemblyAttributeEvaluator.GetAttributeSources(Load(csproj));

        lines.ShouldBe(new[]
        {
            "[assembly: System.Reflection.AssemblyMetadataAttribute(\"Commit\", \"abc123\")]",
            "[assembly: System.Reflection.AssemblyTitleAttribute(\"My App\")]",
        });
    }

    [Fact]
    public void GetAttributeSources_TargetDeclaredAttribute_EmitsNamesForTransitiveModuleProjectsOnly()
    {
        var root = NewFixtureRoot();
        WriteModuleTargetsFixture(root);

        WriteProject(Path.Combine(root, "App", "App.csproj"),
            "<Import Project=\"..\\targets\\Application.Targets.targets\" />\n" +
            "<ItemGroup>\n" +
            "  <ProjectReference Include=\"..\\Bundle\\Bundle.csproj\" />\n" +
            "  <ProjectReference Include=\"..\\Lib\\Lib.csproj\" />\n" +
            "</ItemGroup>");
        WriteProject(Path.Combine(root, "Bundle", "Bundle.csproj"),
            "<ItemGroup>\n" +
            "  <ProjectReference Include=\"..\\Modules\\Mod1\\Mod1.csproj\" />\n" +
            "  <ProjectReference Include=\"..\\Modules\\Mod2\\Mod2.csproj\" />\n" +
            "  <ProjectReference Include=\"..\\Lib\\Lib.csproj\" />\n" +
            "</ItemGroup>");
        WriteProject(Path.Combine(root, "Modules", "Mod1", "Mod1.csproj"), "");
        WriteProject(Path.Combine(root, "Modules", "Mod2", "Mod2.csproj"), "");
        WriteProject(Path.Combine(root, "Lib", "Lib.csproj"), "");

        var lines = AssemblyAttributeEvaluator.GetAttributeSources(Load(Path.Combine(root, "App", "App.csproj")));

        lines.ShouldContain("[assembly: Vendor.Manifest.ModuleNameAttribute(\"Mod1\")]");
        lines.ShouldContain("[assembly: Vendor.Manifest.ModuleNameAttribute(\"Mod2\")]");
        // Lib is referenced but defines no GetModuleProjectName target, so it is not a module.
        lines.ShouldNotContain(line => line.Contains("Lib", StringComparison.Ordinal));
        lines.Count.ShouldBe(2);
    }

    [Fact]
    public void GetAttributeSources_ModuleTargetPresentButManifestTargetMissing_EmitsNothing()
    {
        var root = NewFixtureRoot();
        WriteModuleTargetsFixture(root);

        WriteProject(Path.Combine(root, "App", "App.csproj"),
            "<Import Project=\"..\\targets\\Module.Targets.targets\" />\n" +
            "<ItemGroup>\n" +
            "  <ProjectReference Include=\"..\\Modules\\Mod1\\Mod1.csproj\" />\n" +
            "</ItemGroup>");
        WriteProject(Path.Combine(root, "Modules", "Mod1", "Mod1.csproj"), "");

        AssemblyAttributeEvaluator.GetAttributeSources(Load(Path.Combine(root, "App", "App.csproj")))
            .ShouldBeEmpty();
    }

    [Fact]
    public void GetAttributeSources_ProjectWithoutAssemblyAttributeTarget_EmitsNothing()
    {
        var root = NewFixtureRoot();
        WriteProject(Path.Combine(root, "App.csproj"), "");
        WriteProject(Path.Combine(root, "Mod", "Mod.csproj"), "");

        AssemblyAttributeEvaluator.GetAttributeSources(Load(Path.Combine(root, "App.csproj")))
            .ShouldBeEmpty();
    }

    [Fact]
    public void GetAttributeSources_DeduplicatesIdenticalAttributes()
    {
        var root = NewFixtureRoot();
        var csproj = Path.Combine(root, "App.csproj");
        WriteProject(csproj,
            "<ItemGroup>\n" +
            "  <AssemblyAttribute Include=\"System.Reflection.AssemblyMetadataAttribute\">\n" +
            "    <_Parameter1>Path</_Parameter1>\n" +
            "    <_Parameter2>C:\\temp\\x</_Parameter2>\n" +
            "  </AssemblyAttribute>\n" +
            "  <AssemblyAttribute Include=\"System.Reflection.AssemblyMetadataAttribute\">\n" +
            "    <_Parameter1>Path</_Parameter1>\n" +
            "    <_Parameter2>C:\\temp\\x</_Parameter2>\n" +
            "  </AssemblyAttribute>\n" +
            "</ItemGroup>");

        var lines = AssemblyAttributeEvaluator.GetAttributeSources(Load(csproj));

        lines.Count.ShouldBe(1);
        lines[0].ShouldBe("[assembly: System.Reflection.AssemblyMetadataAttribute(\"Path\", \"C:\\\\temp\\\\x\")]");
    }

    // End-to-end for the module-asset style target: a Directory.Build.props globs ** into
    // EmbeddedResource, a Directory.Build.targets batches it into a manifest + LogicalName update,
    // and the emitted attribute type/name are read from the targets (no product strings in source).
    private const string AssetProps = @"<Project>
  <PropertyGroup>
    <DefaultLanguageSourceExtension>.cs</DefaultLanguageSourceExtension>
    <ModuleDefaultAssetExcludes>Properties\**;**\*$(DefaultLanguageSourceExtension);**\*.resx</ModuleDefaultAssetExcludes>
  </PropertyGroup>
  <ItemGroup>
    <EmbeddedResource Include=""**\*"" Exclude=""$(ModuleDefaultAssetExcludes);$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)"" />
  </ItemGroup>
</Project>";

    private const string AssetTargets = @"<Project>
  <Target Name=""EmbedAssets"">
    <RemoveDuplicates Inputs=""@(EmbeddedResource)"">
      <Output TaskParameter=""Filtered"" ItemName=""AssetFiles"" />
    </RemoveDuplicates>
    <ItemGroup>
      <AssetManifest Include=""Areas\$(AssemblyName)\%(AssetFiles.RelativeDir)%(Filename)%(Extension)|%(AssetFiles.FullPath)""
                     Condition=""'%(AssetFiles.Link)' == '' and '%(AssetFiles.Filename)' != ''"" />
    </ItemGroup>
    <ItemGroup>
      <EmbeddedResource Remove=""@(EmbeddedResource)"" />
      <EmbeddedResource Include=""@(AssetFiles)"" />
      <EmbeddedResource Update=""@(EmbeddedResource)"" Condition=""'%(EmbeddedResource.Link)' == '' and '%(Extension)' != '.resx'"">
        <LogicalName>$([System.String]::new('$(AssemblyName).%(RelativeDir)%(FileName)%(Extension)').Replace('\', '>').Replace('/', '>'))</LogicalName>
      </EmbeddedResource>
      <EmbeddedResource Update=""@(EmbeddedResource)"" Condition=""!('%(EmbeddedResource.Link)' == '' or '%(Extension)' == '.resx')"">
        <LogicalName>$([System.String]::new('$(AssemblyName).%(EmbeddedResource.Link)').Replace('\', '>').Replace('/', '>'))</LogicalName>
      </EmbeddedResource>
    </ItemGroup>
    <ItemGroup>
      <AssemblyAttribute Include=""Vendor.Manifest.AssetAttribute"">
        <_Parameter1>%(AssetManifest.Identity)</_Parameter1>
      </AssemblyAttribute>
    </ItemGroup>
  </Target>
</Project>";

    [Fact]
    public void GetEmbeddedResources_ModuleAssetTarget_UsesLogicalNameAndExcludesNonAssetFiles()
    {
        var root = NewFixtureRoot();
        Write(Path.Combine(root, "Directory.Build.props"), AssetProps);
        Write(Path.Combine(root, "Directory.Build.targets"), AssetTargets);
        var csproj = Path.Combine(root, "App", "App.csproj");
        WriteProject(csproj, "");
        Write(Path.Combine(root, "App", "wwwroot", "css", "app.css"), "body{}");
        Write(Path.Combine(root, "App", "Views", "Index.cshtml"), "<h1>hi</h1>");
        Write(Path.Combine(root, "App", "Services.cs"), "class Services {}");
        Write(Path.Combine(root, "App", "data.resx"), "<root/>");

        var resources = AssemblyAttributeEvaluator.GetEmbeddedResources(Load(csproj));

        resources.Select(resource => resource.Name).OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(new[] { "App.Views>Index.cshtml", "App.wwwroot>css>app.css" });
        resources.ShouldContain(resource => resource.FilePath.EndsWith(Path.Combine("wwwroot", "css", "app.css"), StringComparison.Ordinal));
    }

    [Fact]
    public void GetAttributeSources_ModuleAssetTarget_BatchesAndSkipsExcludedFiles()
    {
        var root = NewFixtureRoot();
        Write(Path.Combine(root, "Directory.Build.props"), AssetProps);
        Write(Path.Combine(root, "Directory.Build.targets"), AssetTargets);
        var csproj = Path.Combine(root, "App", "App.csproj");
        WriteProject(csproj, "");
        Write(Path.Combine(root, "App", "wwwroot", "css", "app.css"), "body{}");
        Write(Path.Combine(root, "App", "Views", "Index.cshtml"), "<h1>hi</h1>");
        Write(Path.Combine(root, "App", "Services.cs"), "class Services {}");
        Write(Path.Combine(root, "App", "data.resx"), "<root/>");

        var lines = AssemblyAttributeEvaluator.GetAttributeSources(Load(csproj));

        lines.Count(line => line.StartsWith("[assembly: Vendor.Manifest.AssetAttribute(", StringComparison.Ordinal)).ShouldBe(2);
        lines.ShouldContain(line => line.Contains("wwwroot/css/app.css", StringComparison.Ordinal));
        lines.ShouldContain(line => line.Contains("Views/Index.cshtml", StringComparison.Ordinal));
        lines.ShouldNotContain(line => line.Contains(".csproj", StringComparison.Ordinal) || line.Contains(".resx", StringComparison.Ordinal) || line.Contains("Services", StringComparison.Ordinal));
    }
}
