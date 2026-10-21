using Microsoft.CodeAnalysis;

namespace NativeCompiler;

public static class References
{
    // Reference set handed to CSharpCompilation.Create: NuGet + local (project refs) merged with the
    // SDK/framework reference pack, deduped and version-unified.
    [Query]
    public static MetadataReference[] GetCompilingRefs(Project project, string sdkRefPath)
    {
        var sdk = NativeCompilerUtils.GetdefaultReferencesForCompiling(sdkRefPath);
        var framework = NativeCompilerUtils.GetFrameworkReferenceCompilingRefs(project);
        var packageAndLocal = NativeCompilerUtils.GetdefaultNugetReferences(project)
            .Concat(NativeCompilerUtils.GetdefaultLocalReferences(project));

        return FrameworkFix.Merge(packageAndLocal, sdk.Concat(framework));
    }

    // Copy-local candidate set for the output app: NuGet + local only (shared-framework assemblies come
    // from the runtime at run time, never copied next to the app), so they are the runtime graph.
    [Query]
    public static MetadataReference[] GetRuntimeRefs(Project project)
    {
        var packageAndLocal = NativeCompilerUtils.GetdefaultNugetReferences(project)
            .Concat(NativeCompilerUtils.GetdefaultLocalReferences(project));

        return FrameworkFix.Merge(packageAndLocal, Array.Empty<MetadataReference>());
    }
}
