using System.Xml.Serialization;

namespace XML;

[XmlRoot(ElementName = "PropertyGroup")]
public class PropertyGroup
{
    [XmlElement(ElementName = "TargetFramework")]
    public string? TargetFramework { get; set; }

    [XmlElement(ElementName = "RuntimeIdentifier")]
    public string? RuntimeIdentifier { get; set; }

    [XmlElement(ElementName = "RuntimeFrameworkVersion")]
    public string? RuntimeFrameworkVersion { get; set; }

    [XmlElement(ElementName = "TargetsForTfmSpecificBuildOutput")]
    public string? TargetsForTfmSpecificBuildOutput { get; set; }

    [XmlElement(ElementName = "PreserveCompilationContext")]
    public string? PreserveCompilationContext { get; set; }

    [XmlElement(ElementName = "AssemblyName")]
    public string? AssemblyName { get; set; }

    [XmlElement(ElementName = "OutputType")]
    public string? OutputType { get; set; }

    [XmlElement(ElementName = "PackageId")]
    public string? PackageId { get; set; }

    [XmlElement(ElementName = "GenerateAssemblyTitleAttribute")]
    public string? GenerateAssemblyTitleAttribute { get; set; }

    [XmlElement(ElementName = "GenerateAssemblyDescriptionAttribute")]
    public string? GenerateAssemblyDescriptionAttribute { get; set; }

    [XmlElement(ElementName = "GenerateAssemblyConfigurationAttribute")]
    public string? GenerateAssemblyConfigurationAttribute { get; set; }

    [XmlElement(ElementName = "GenerateAssemblyCompanyAttribute")]
    public string? GenerateAssemblyCompanyAttribute { get; set; }

    [XmlElement(ElementName = "GenerateAssemblyProductAttribute")]
    public string? GenerateAssemblyProductAttribute { get; set; }

    [XmlElement(ElementName = "GenerateAssemblyCopyrightAttribute")]
    public string? GenerateAssemblyCopyrightAttribute { get; set; }

    [XmlElement(ElementName = "GenerateAssemblyVersionAttribute")]
    public string? GenerateAssemblyVersionAttribute { get; set; }

    [XmlElement(ElementName = "GenerateAssemblyFileVersionAttribute")]
    public string? GenerateAssemblyFileVersionAttribute { get; set; }

    [XmlElement(ElementName = "DocumentationFile")]
    public string? DocumentationFile { get; set; }

    [XmlElement(ElementName = "Authors")] public string? Authors { get; set; }

    [XmlElement(ElementName = "PublishWithAspNetCoreTargetManifest")]
    public string? PublishWithAspNetCoreTargetManifest { get; set; }

    [XmlElement(ElementName = "VersionPrefix")]
    public string? VersionPrefix { get; set; }

    [XmlElement(ElementName = "VersionSuffix")]
    public string? VersionSuffix { get; set; }

    [XmlElement(ElementName = "IsPackable")]
    public string? IsPackable { get; set; }
}

[XmlRoot(ElementName = "Content")]
public class Content
{
    [XmlElement(ElementName = "CopyToOutputDirectory")]
    public string? CopyToOutputDirectory { get; set; }

    [XmlAttribute(AttributeName = "Update")]
    public string? Update { get; set; }
}

[XmlRoot(ElementName = "ItemGroup")]
public class ItemGroup
{
    [XmlElement(ElementName = "Content")] public List<Content>? Content { get; set; }

    [XmlElement(ElementName = "PackageReference")]
    public List<PackageReference> PackageReferences { get; set; } = new();

    [XmlElement(ElementName = "ProjectReference")]
    public List<ProjectReference> ProjectReferences { get; set; } = new();

    [XmlElement(ElementName = "BuildOutputInPackage")]
    public BuildOutputInPackage? BuildOutputInPackage { get; set; }
}

[XmlRoot(ElementName = "PackageReference")]
public class PackageReference
{
    [XmlAttribute(AttributeName = "Include")]
    public string? Include { get; set; }

    [XmlAttribute(AttributeName = "Version")]
    public string? Version { get; set; }
}

[XmlRoot(ElementName = "ProjectReference")]
public class ProjectReference
{
    [XmlAttribute(AttributeName = "Include")]
    public string? Include { get; set; }

    [XmlAttribute(AttributeName = "PrivateAssets")]
    public string? PrivateAssets { get; set; }
}

[XmlRoot(ElementName = "BuildOutputInPackage")]
public class BuildOutputInPackage
{
    [XmlAttribute(AttributeName = "Include")]
    public string? Include { get; set; }
}

[XmlRoot(ElementName = "Target")]
public class Target
{
    [XmlElement(ElementName = "ItemGroup")]
    public ItemGroup? ItemGroup { get; set; }

    [XmlAttribute(AttributeName = "Name")] public string? Name { get; set; }

    [XmlAttribute(AttributeName = "DependsOnTargets")]
    public string? DependsOnTargets { get; set; }
}

[XmlRoot(ElementName = "Project")]
public class Project
{
    [XmlElement(ElementName = "PropertyGroup")]
    public List<PropertyGroup>? PropertyGroup { get; set; }

    [XmlElement(ElementName = "ItemGroup")]
    public List<ItemGroup> ItemGroup { get; set; } = new();

    [XmlElement(ElementName = "Target")] public Target? Target { get; set; }

    [XmlAttribute(AttributeName = "Sdk")] public string? Sdk { get; set; }
}