using Microsoft.CodeAnalysis;

namespace Sharp;

public static class References
{
    // Reference set handed to CSharpCompilation.Create: NuGet + local (project refs) merged with the
    // SDK/framework reference pack, deduped and version-unified.
    [Query]
    public static MetadataReference[] GetCompilingRefs(Project project, string sdkRefPath)
    {
        var sdk = SharpUtils.GetdefaultReferencesForCompiling(sdkRefPath);
        var framework = SharpUtils.GetFrameworkReferenceCompilingRefs(project);
        var packageAndLocal = SharpUtils.GetdefaultNugetReferences(project)
            .Concat(SharpUtils.GetdefaultLocalReferences(project));

        return FrameworkFix.Merge(packageAndLocal, sdk.Concat(framework));
    }

    // Copy-local candidate set for the output app: NuGet + local only (shared-framework assemblies come
    // from the runtime at run time, never copied next to the app), so they are the runtime graph.
    [Query]
    public static MetadataReference[] GetRuntimeRefs(Project project)
    {
        var packageAndLocal = SharpUtils.GetdefaultNugetReferences(project)
            .Concat(SharpUtils.GetdefaultLocalReferences(project));

        return FrameworkFix.Merge(packageAndLocal, Array.Empty<MetadataReference>());
    }
}
