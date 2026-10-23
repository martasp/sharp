using Microsoft.CodeAnalysis;
using System.Reflection;

namespace Sharp;

public static class FrameworkFix
{
    // Mirrors NuGet's framework-assembly suppression: assemblies that are inbox in the runtime/ref pack
    // are dropped from package/project references so duplicate simple names (e.g. System.Collections.Immutable)
    // do not produce conflicting references.
    //
    // When one simple name appears at two versions - the entry project's own graph resolves one (e.g.
    // Azure.Core 1.54.0 via Azure.Search.Documents) while a referenced project's graph resolves a higher
    // one (Azure.Core 1.61.0 via Microsoft.Extensions.Azure) - a compilation can only reference one of
    // them. Keep the highest: csc accepts a referenced assembly whose version is >= the version each
    // dependent was compiled against, so the highest satisfies every dependency, whereas keeping the
    // first-seen lower version triggers CS1705. This mirrors NuGet's graph-wide version unification,
    // which the per-project resolution upstream cannot see across the project-reference closure.
    [Pure]
    public static MetadataReference[] Merge(
        IEnumerable<MetadataReference> nugetAndLocal,
        IEnumerable<MetadataReference> frameworkReferences)
    {
        var frameworkNames = new HashSet<string>(
            frameworkReferences.Select(reference =>
                Path.GetFileNameWithoutExtension(reference.Display) ?? ""),
            StringComparer.OrdinalIgnoreCase);

        // First-seen order is preserved so the emitted reference set stays deterministic.
        var order = new List<string>();
        var chosen = new Dictionary<string, (MetadataReference Reference, Version? Version)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var reference in nugetAndLocal)
        {
            var name = Path.GetFileNameWithoutExtension(reference.Display) ?? "";
            if (name.Length == 0) continue;
            if (frameworkNames.Contains(name)) continue;

            if (!chosen.TryGetValue(name, out var current))
            {
                chosen[name] = (reference, TryGetVersion(reference.Display));
                order.Add(name);
                continue;
            }

            var candidate = TryGetVersion(reference.Display);
            if (candidate is null) continue;
            if (current.Version is not null && candidate.CompareTo(current.Version) <= 0) continue;

            chosen[name] = (reference, candidate);
        }

        var merged = new List<MetadataReference>(order.Count + frameworkReferences.Count());
        foreach (var name in order) merged.Add(chosen[name].Reference);
        merged.AddRange(frameworkReferences);
        return merged.ToArray();
    }

    // Reads an assembly's identity version from its file, tolerating paths that are not managed
    // assemblies (returns null so the caller keeps the reference it already has).
    [Pure]
    private static Version? TryGetVersion(string? displayPath)
    {
        if (string.IsNullOrEmpty(displayPath) || !File.Exists(displayPath)) return null;
        try { return AssemblyName.GetAssemblyName(displayPath).Version; }
        catch { return null; }
    }
}
