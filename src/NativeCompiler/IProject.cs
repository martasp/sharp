using Microsoft.CodeAnalysis;
using XML;
using ProjectReference = XML.ProjectReference;

namespace NativeCompiler;

public interface IProject
{
    public string ProjectName { get; }
    public string ExtensionName { get; }
    public string DllPathName { get; }
    public string PDBName { get; }
    public string NugetPackagesRootPath { get; }
    public List<ProjectReference> ProjectReferences { get; }
    public List<PackageReference> PackageReferences { get; }
    public OutputKind ResolvedProjectOutputKind();
}