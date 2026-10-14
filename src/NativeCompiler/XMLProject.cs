using System.Xml.Linq;
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

    [XmlElement(ElementName = "ImplicitUsings")]
    public string? ImplicitUsings { get; set; }

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

    [XmlElement(ElementName = "EnableDefaultItems")]
    public string? EnableDefaultItems { get; set; }

    [XmlElement(ElementName = "IsTestProject")]
    public string? IsTestProject { get; set; }

    [XmlElement(ElementName = "IsAspireHost")]
    public string? IsAspireHost { get; set; }

    [XmlElement(ElementName = "AllowUnsafeBlocks")]
    public string? AllowUnsafeBlocks { get; set; }

    [XmlElement(ElementName = "UseAppHost")]
    public string? UseAppHost { get; set; }

    [XmlElement(ElementName = "EnableRequestDelegateGenerator")]
    public string? EnableRequestDelegateGenerator { get; set; }

    [XmlElement(ElementName = "EnableConfigurationBindingGenerator")]
    public string? EnableConfigurationBindingGenerator { get; set; }

    [XmlElement(ElementName = "LangVersion")]
    public string? LangVersion { get; set; }

    [XmlElement(ElementName = "Nullable")]
    public string? Nullable { get; set; }

    [XmlElement(ElementName = "DefineConstants")]
    public string? DefineConstants { get; set; }

    [XmlElement(ElementName = "InterceptorsNamespaces")]
    public string? InterceptorsNamespaces { get; set; }

    [XmlElement(ElementName = "InterceptorsPreviewNamespaces")]
    public string? InterceptorsPreviewNamespaces { get; set; }
}

[XmlRoot(ElementName = "Content")]
public class Content
{
    [XmlElement(ElementName = "CopyToOutputDirectory")]
    public string? CopyToOutputDirectory { get; set; }

    [XmlAttribute(AttributeName = "Update")]
    public string? Update { get; set; }
}

[XmlRoot(ElementName = "Compile")]
public class CompileItem
{
    [XmlAttribute(AttributeName = "Include")]
    public string? Include { get; set; }

    [XmlAttribute(AttributeName = "Remove")]
    public string? Remove { get; set; }
}

[XmlRoot(ElementName = "None")]
public class NoneItem
{
    [XmlAttribute(AttributeName = "Include")]
    public string? Include { get; set; }

    [XmlAttribute(AttributeName = "Remove")]
    public string? Remove { get; set; }
}

[XmlRoot(ElementName = "ItemGroup")]
public class ItemGroup
{
    [XmlElement(ElementName = "Content")] public List<Content>? Content { get; set; }

    [XmlElement(ElementName = "Compile")]
    public List<CompileItem> CompileItems { get; set; } = new();

    [XmlElement(ElementName = "None")]
    public List<NoneItem> NoneItems { get; set; } = new();

    [XmlElement(ElementName = "PackageReference")]
    public List<PackageReference> PackageReferences { get; set; } = new();

    [XmlElement(ElementName = "Reference")]
    public List<Reference> References { get; set; } = new();

    [XmlElement(ElementName = "ProjectReference")]
    public List<ProjectReference> ProjectReferences { get; set; } = new();

    [XmlElement(ElementName = "FrameworkReference")]
    public List<FrameworkReference> FrameworkReferences { get; set; } = new();

    [XmlElement(ElementName = "AssemblyAttribute")]
    public List<AssemblyAttributeItem> AssemblyAttributes { get; set; } = new();

    [XmlElement(ElementName = "InternalsVisibleTo")]
    public List<InternalsVisibleToItem> InternalsVisibleTo { get; set; } = new();

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

    [XmlAttribute(AttributeName = "OutputItemType")]
    public string? OutputItemType { get; set; }
}

[XmlRoot(ElementName = "FrameworkReference")]
public class FrameworkReference
{
    [XmlAttribute(AttributeName = "Include")]
    public string? Include { get; set; }
}

[XmlRoot(ElementName = "Reference")]
public class Reference
{
    [XmlAttribute(AttributeName = "Include")]
    public string? Include { get; set; }

    [XmlAttribute(AttributeName = "Private")]
    public string? Private { get; set; }

    [XmlElement(ElementName = "HintPath")]
    public string? HintPath { get; set; }
}

[XmlRoot(ElementName = "AssemblyAttribute")]
public class AssemblyAttributeItem
{
    [XmlAttribute(AttributeName = "Include")]
    public string? Include { get; set; }

    [XmlElement(ElementName = "_Parameter1")]
    public string? Parameter1 { get; set; }
}

[XmlRoot(ElementName = "InternalsVisibleTo")]
public class InternalsVisibleToItem
{
    [XmlAttribute(AttributeName = "Include")]
    public string? Include { get; set; }
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
    public Project() { }

    [XmlElement(ElementName = "PropertyGroup")]
    public List<PropertyGroup>? PropertyGroup { get; set; }

    [XmlElement(ElementName = "ItemGroup")]
    public List<ItemGroup> ItemGroup { get; set; } = new();

    [XmlElement(ElementName = "Target")] public Target? Target { get; set; }

    [XmlAttribute(AttributeName = "Sdk")] public string? Sdk { get; set; }

    //XmlSerializer relies on runtime code generation (Reflection.Emit) which is not
    //available under NativeAOT, so csproj files are parsed with System.Xml.Linq instead.
    public static Project Parse(string path)
    {
        var root = XElement.Load(path);
        if (root.Name.LocalName != "Project")
            throw new InvalidOperationException($"\"{path}\" is not a .csproj file (<Project> root element expected).");

        return new Project
        {
            Sdk = AttributeValue(root, "Sdk"),
            PropertyGroup = Children(root, "PropertyGroup").Select(ParsePropertyGroup).ToList(),
            ItemGroup = Children(root, "ItemGroup").Select(ParseItemGroup).ToList(),
            Target = Children(root, "Target").Select(ParseTarget).FirstOrDefault(),
        };
    }

    private static IEnumerable<XElement> Children(XElement parent, string localName) =>
        parent.Elements().Where(e => e.Name.LocalName == localName);

    private static string? AttributeValue(XElement parent, string localName) =>
        (string?)parent.Attributes().FirstOrDefault(a => a.Name.LocalName == localName);

    private static string? ElementValue(XElement? parent, string localName) =>
        parent is null ? null : (string?)Children(parent, localName).FirstOrDefault();

    private static PropertyGroup ParsePropertyGroup(XElement element) => new()
    {
        TargetFramework = ElementValue(element, "TargetFramework"),
        RuntimeIdentifier = ElementValue(element, "RuntimeIdentifier"),
        RuntimeFrameworkVersion = ElementValue(element, "RuntimeFrameworkVersion"),
        ImplicitUsings = ElementValue(element, "ImplicitUsings"),
        TargetsForTfmSpecificBuildOutput = ElementValue(element, "TargetsForTfmSpecificBuildOutput"),
        PreserveCompilationContext = ElementValue(element, "PreserveCompilationContext"),
        AssemblyName = ElementValue(element, "AssemblyName"),
        OutputType = ElementValue(element, "OutputType"),
        PackageId = ElementValue(element, "PackageId"),
        GenerateAssemblyTitleAttribute = ElementValue(element, "GenerateAssemblyTitleAttribute"),
        GenerateAssemblyDescriptionAttribute = ElementValue(element, "GenerateAssemblyDescriptionAttribute"),
        GenerateAssemblyConfigurationAttribute = ElementValue(element, "GenerateAssemblyConfigurationAttribute"),
        GenerateAssemblyCompanyAttribute = ElementValue(element, "GenerateAssemblyCompanyAttribute"),
        GenerateAssemblyProductAttribute = ElementValue(element, "GenerateAssemblyProductAttribute"),
        GenerateAssemblyCopyrightAttribute = ElementValue(element, "GenerateAssemblyCopyrightAttribute"),
        GenerateAssemblyVersionAttribute = ElementValue(element, "GenerateAssemblyVersionAttribute"),
        GenerateAssemblyFileVersionAttribute = ElementValue(element, "GenerateAssemblyFileVersionAttribute"),
        DocumentationFile = ElementValue(element, "DocumentationFile"),
        Authors = ElementValue(element, "Authors"),
        PublishWithAspNetCoreTargetManifest = ElementValue(element, "PublishWithAspNetCoreTargetManifest"),
        VersionPrefix = ElementValue(element, "VersionPrefix"),
        VersionSuffix = ElementValue(element, "VersionSuffix"),
        IsPackable = ElementValue(element, "IsPackable"),
        EnableDefaultItems = ElementValue(element, "EnableDefaultItems"),
        IsTestProject = ElementValue(element, "IsTestProject"),
        IsAspireHost = ElementValue(element, "IsAspireHost"),
        AllowUnsafeBlocks = ElementValue(element, "AllowUnsafeBlocks"),
        UseAppHost = ElementValue(element, "UseAppHost"),
        EnableRequestDelegateGenerator = ElementValue(element, "EnableRequestDelegateGenerator"),
        EnableConfigurationBindingGenerator = ElementValue(element, "EnableConfigurationBindingGenerator"),
        LangVersion = ElementValue(element, "LangVersion"),
        Nullable = ElementValue(element, "Nullable"),
        DefineConstants = ElementValue(element, "DefineConstants"),
        InterceptorsNamespaces = ElementValue(element, "InterceptorsNamespaces"),
        InterceptorsPreviewNamespaces = ElementValue(element, "InterceptorsPreviewNamespaces"),
    };

    private static ItemGroup ParseItemGroup(XElement element) => new()
    {
        Content = Children(element, "Content").Select(content => new Content
        {
            Update = AttributeValue(content, "Update"),
            CopyToOutputDirectory = ElementValue(content, "CopyToOutputDirectory"),
        }).ToList(),
        CompileItems = Children(element, "Compile").Select(item => new CompileItem
        {
            Include = AttributeValue(item, "Include"),
            Remove = AttributeValue(item, "Remove"),
        }).ToList(),
        NoneItems = Children(element, "None").Select(item => new NoneItem
        {
            Include = AttributeValue(item, "Include"),
            Remove = AttributeValue(item, "Remove"),
        }).ToList(),
        PackageReferences = Children(element, "PackageReference").Select(reference => new PackageReference
        {
            Include = AttributeValue(reference, "Include"),
            Version = AttributeValue(reference, "Version"),
        }).ToList(),
        References = Children(element, "Reference").Select(reference => new Reference
        {
            Include = AttributeValue(reference, "Include"),
            Private = AttributeValue(reference, "Private"),
            HintPath = ElementValue(reference, "HintPath"),
        }).ToList(),
        ProjectReferences = Children(element, "ProjectReference").Select(reference => new ProjectReference
        {
            Include = AttributeValue(reference, "Include"),
            PrivateAssets = AttributeValue(reference, "PrivateAssets"),
            OutputItemType = AttributeValue(reference, "OutputItemType"),
        }).ToList(),
        FrameworkReferences = Children(element, "FrameworkReference").Select(reference => new FrameworkReference
        {
            Include = AttributeValue(reference, "Include"),
        }).ToList(),
        AssemblyAttributes = Children(element, "AssemblyAttribute").Select(item => new AssemblyAttributeItem
        {
            Include = AttributeValue(item, "Include"),
            Parameter1 = ElementValue(item, "_Parameter1"),
        }).ToList(),
        InternalsVisibleTo = Children(element, "InternalsVisibleTo").Select(item => new InternalsVisibleToItem
        {
            Include = AttributeValue(item, "Include"),
        }).ToList(),
        BuildOutputInPackage = Children(element, "BuildOutputInPackage")
            .Select(item => new BuildOutputInPackage { Include = AttributeValue(item, "Include") })
            .FirstOrDefault(),
    };

    private static Target ParseTarget(XElement element) => new()
    {
        Name = AttributeValue(element, "Name"),
        DependsOnTargets = AttributeValue(element, "DependsOnTargets"),
        ItemGroup = Children(element, "ItemGroup").Select(ParseItemGroup).FirstOrDefault(),
    };
}