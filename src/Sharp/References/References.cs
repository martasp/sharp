using Microsoft.CodeAnalysis;

namespace Sharp;

public sealed record ResolvedReferences(
    MetadataReference[] Compiling,
    MetadataReference[] Runtime);

public static class References
{
    // Resolve the NuGet + local closure once, then derive both reference sets from it. The compile set
    // adds the SDK/framework pack; the runtime set is copy-local only (shared-framework assemblies
    // come from the runtime at execution time).
    [Query]
    public static ResolvedReferences GetReferences(Project project, string sdkRefPath)
    {
        var packageAndLocal = SharpUtils.GetdefaultNugetReferences(project)
            .Concat(SharpUtils.GetdefaultLocalReferences(project))
            .ToArray();

        var framework = SharpUtils.GetdefaultReferencesForCompiling(sdkRefPath)
            .Concat(SharpUtils.GetFrameworkReferenceCompilingRefs(project));
        var runtime = FrameworkFix.Unify(packageAndLocal);

        return new ResolvedReferences(
            FrameworkFix.AddFrameworkReferences(runtime, framework),
            runtime);
    }
}
