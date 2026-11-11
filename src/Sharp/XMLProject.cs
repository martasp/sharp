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

    [XmlElement(ElementName = "EmitCompilerGeneratedFiles")]
    public string? EmitCompilerGeneratedFiles { get; set; }

    [XmlElement(ElementName = "CompilerGeneratedFilesOutputPath")]
    public string? CompilerGeneratedFilesOutputPath { get; set; }

    [XmlElement(ElementName = "Nullable")]
    public string? Nullable { get; set; }

    [XmlElement(ElementName = "DefineConstants")]
    public string? DefineConstants { get; set; }

    [XmlElement(ElementName = "InterceptorsNamespaces")]
    public string? InterceptorsNamespaces { get; set; }

    [XmlElement(ElementName = "InterceptorsPreviewNamespaces")]
    public string? InterceptorsPreviewNamespaces { get; set; }

    [XmlElement(ElementName = "Configuration")]
    public string? Configuration { get; set; }

    // Publish/NativeAOT properties evaluated by Sharp.Publish. Unknown properties stay unsupported;
    // these are the switches that change the emitted native image or the ILC/link arguments.
    [XmlElement(ElementName = "PublishAot")]
    public string? PublishAot { get; set; }

    [XmlElement(ElementName = "PublishTrimmed")]
    public string? PublishTrimmed { get; set; }

    [XmlElement(ElementName = "SelfContained")]
    public string? SelfContained { get; set; }

    [XmlElement(ElementName = "DebugType")]
    public string? DebugType { get; set; }

    [XmlElement(ElementName = "DebugSymbols")]
    public string? DebugSymbols { get; set; }

    [XmlElement(ElementName = "StripSymbols")]
    public string? StripSymbols { get; set; }

    [XmlElement(ElementName = "OptimizationPreference")]
    public string? OptimizationPreference { get; set; }

    [XmlElement(ElementName = "ServerGarbageCollection")]
    public string? ServerGarbageCollection { get; set; }

    [XmlElement(ElementName = "EventSourceSupport")]
    public string? EventSourceSupport { get; set; }

    [XmlElement(ElementName = "InvariantGlobalization")]
    public string? InvariantGlobalization { get; set; }

    [XmlElement(ElementName = "UseSystemResourceKeys")]
    public string? UseSystemResourceKeys { get; set; }

    [XmlElement(ElementName = "UseSizeOptimizedLinq")]
    public string? UseSizeOptimizedLinq { get; set; }

    [XmlElement(ElementName = "UseSystemZlib")]
    public string? UseSystemZlib { get; set; }

    [XmlElement(ElementName = "UseSystemBrotli")]
    public string? UseSystemBrotli { get; set; }

    [XmlElement(ElementName = "StaticICULinking")]
    public string? StaticICULinking { get; set; }

    [XmlElement(ElementName = "StaticOpenSslLinking")]
    public string? StaticOpenSslLinking { get; set; }

    [XmlElement(ElementName = "UseSystemLibraries")]
    public string? UseSystemLibraries { get; set; }

    [XmlElement(ElementName = "NativeDebugSymbols")]
    public string? NativeDebugSymbols { get; set; }

    [XmlElement(ElementName = "StackTraceSupport")]
    public string? StackTraceSupport { get; set; }

    [XmlElement(ElementName = "DebuggerSupport")]
    public string? DebuggerSupport { get; set; }

    [XmlElement(ElementName = "ETWEventSourceSupport")]
    public string? ETWEventSourceSupport { get; set; }

    // Razor SDK (Microsoft.NET.Sdk.Razor / Microsoft.NET.Sdk.Web). AddRazorSupportForMvc opts .cshtml
    // files into the MVC view pipeline; RazorLangVersion overrides the language version the Razor
    // source generator targets (defaults to the TFM's version, e.g. "10.0" for net10.0).
    [XmlElement(ElementName = "AddRazorSupportForMvc")]
    public string? AddRazorSupportForMvc { get; set; }

    [XmlElement(ElementName = "RazorLangVersion")]
    public string? RazorLangVersion { get; set; }

    [XmlElement(ElementName = "EnableDefaultRazorGenerateItems")]
    public string? EnableDefaultRazorGenerateItems { get; set; }

    [XmlElement(ElementName = "EnableDefaultRazorComponentItems")]
    public string? EnableDefaultRazorComponentItems { get; set; }
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

[XmlRoot(ElementName = "AdditionalFiles")]
public class AdditionalFileItem
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

    [XmlElement(ElementName = "AdditionalFiles")]
    public List<AdditionalFileItem> AdditionalFiles { get; set; } = new();

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

    [XmlElement(ElementName = "IlcArg")]
    public List<IncludeItem> IlcArgs { get; set; } = new();

    [XmlElement(ElementName = "TrimmerRootDescriptor")]
    public List<IncludeItem> TrimmerRootDescriptors { get; set; } = new();
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

    // Positional constructor arguments in declaration order (_Parameter1, _Parameter2, ...), which is
    // how MSBuild's WriteCodeFragment receives them.
    public List<string> PositionalParameters { get; set; } = new();

    // Named property assignments: any child element that is not a positional "_ParameterN".
    public Dictionary<string, string> NamedParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // First positional argument (used by InternalsVisibleTo items and the module-name manifest).
    public string? Parameter1 => PositionalParameters.Count > 0 ? PositionalParameters[0] : null;
}

[XmlRoot(ElementName = "InternalsVisibleTo")]
public class InternalsVisibleToItem
{
    [XmlAttribute(AttributeName = "Include")]
    public string? Include { get; set; }
}

[XmlRoot(ElementName = "IncludeItem")]
public class IncludeItem
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
        EmitCompilerGeneratedFiles = ElementValue(element, "EmitCompilerGeneratedFiles"),
        CompilerGeneratedFilesOutputPath = ElementValue(element, "CompilerGeneratedFilesOutputPath"),
        Nullable = ElementValue(element, "Nullable"),
        DefineConstants = ElementValue(element, "DefineConstants"),
        InterceptorsNamespaces = ElementValue(element, "InterceptorsNamespaces"),
        InterceptorsPreviewNamespaces = ElementValue(element, "InterceptorsPreviewNamespaces"),
        Configuration = ElementValue(element, "Configuration"),
        PublishAot = ElementValue(element, "PublishAot"),
        PublishTrimmed = ElementValue(element, "PublishTrimmed"),
        SelfContained = ElementValue(element, "SelfContained"),
        DebugType = ElementValue(element, "DebugType"),
        DebugSymbols = ElementValue(element, "DebugSymbols"),
        StripSymbols = ElementValue(element, "StripSymbols"),
        OptimizationPreference = ElementValue(element, "OptimizationPreference"),
        ServerGarbageCollection = ElementValue(element, "ServerGarbageCollection"),
        EventSourceSupport = ElementValue(element, "EventSourceSupport"),
        InvariantGlobalization = ElementValue(element, "InvariantGlobalization"),
        UseSystemResourceKeys = ElementValue(element, "UseSystemResourceKeys"),
        UseSizeOptimizedLinq = ElementValue(element, "UseSizeOptimizedLinq"),
        UseSystemZlib = ElementValue(element, "UseSystemZlib"),
        UseSystemBrotli = ElementValue(element, "UseSystemBrotli"),
        StaticICULinking = ElementValue(element, "StaticICULinking"),
        StaticOpenSslLinking = ElementValue(element, "StaticOpenSslLinking"),
        UseSystemLibraries = ElementValue(element, "UseSystemLibraries"),
        NativeDebugSymbols = ElementValue(element, "NativeDebugSymbols"),
        StackTraceSupport = ElementValue(element, "StackTraceSupport"),
        DebuggerSupport = ElementValue(element, "DebuggerSupport"),
        ETWEventSourceSupport = ElementValue(element, "ETWEventSourceSupport"),
        AddRazorSupportForMvc = ElementValue(element, "AddRazorSupportForMvc"),
        RazorLangVersion = ElementValue(element, "RazorLangVersion"),
        EnableDefaultRazorGenerateItems = ElementValue(element, "EnableDefaultRazorGenerateItems"),
        EnableDefaultRazorComponentItems = ElementValue(element, "EnableDefaultRazorComponentItems"),
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
        AdditionalFiles = Children(element, "AdditionalFiles").Select(item => new AdditionalFileItem
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
        AssemblyAttributes = Children(element, "AssemblyAttribute").Select(ParseAssemblyAttribute).ToList(),
        InternalsVisibleTo = Children(element, "InternalsVisibleTo").Select(item => new InternalsVisibleToItem
        {
            Include = AttributeValue(item, "Include"),
        }).ToList(),
        BuildOutputInPackage = Children(element, "BuildOutputInPackage")
            .Select(item => new BuildOutputInPackage { Include = AttributeValue(item, "Include") })
            .FirstOrDefault(),
        IlcArgs = Children(element, "IlcArg").Select(item => new IncludeItem
        {
            Include = AttributeValue(item, "Include"),
        }).ToList(),
        TrimmerRootDescriptors = Children(element, "TrimmerRootDescriptor").Select(item => new IncludeItem
        {
            Include = AttributeValue(item, "Include"),
        }).ToList(),
    };

    private static AssemblyAttributeItem ParseAssemblyAttribute(XElement element)
    {
        var attribute = new AssemblyAttributeItem { Include = AttributeValue(element, "Include") };
        var positional = new List<(int Index, string Value)>();

        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            if (name.StartsWith("_Parameter", StringComparison.Ordinal)
                && int.TryParse(name.AsSpan("_Parameter".Length), out var index))
            {
                positional.Add((index, child.Value));
            }
            else
            {
                attribute.NamedParameters[name] = child.Value;
            }
        }

        attribute.PositionalParameters = positional.OrderBy(item => item.Index).Select(item => item.Value).ToList();
        return attribute;
    }

    private static Target ParseTarget(XElement element) => new()
    {
        Name = AttributeValue(element, "Name"),
        DependsOnTargets = AttributeValue(element, "DependsOnTargets"),
        ItemGroup = Children(element, "ItemGroup").Select(ParseItemGroup).FirstOrDefault(),
    };
}