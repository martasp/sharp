namespace Sharp.NuGet;

// Resolves the NuGet version syntax a csproj can carry into a concrete version string.
//
// Supported:
//   1.2.3          exact
//   [1.2.3]         exact, pinned with bracket notation
//   [1.2.3, )       minimum inclusive
//   (1.2.3, )       minimum exclusive
//   (, 2.0.0]       maximum inclusive
//   [1.0, 2.0)      range
//
// Deliberately NOT supported: floating versions (1.2.*, *), which need prerelease-aware
// resolution rules. They produce an explicit error rather than a silent wrong answer.
//
// Ordering reuses the System.Version comparison in NuGetPackage.TryParseVersion, so a
// prerelease suffix ("1.0.0-beta.1") is compared by its numeric core - the same approximation
// the rest of the compiler already makes.
public static class VersionResolver
{
    public readonly record struct Result(bool Success, string? Version, string? Error)
    {
        public static Result Ok(string version) => new(true, version, null);
        public static Result Fail(string error) => new(false, null, error);
    }

    // Parses NuGet's version grammar without touching the network. `available` is the feed's
    // version list; null means "no feed consulted yet" (only exact versions are valid then).
    public static Result Resolve(string requested, IReadOnlyList<string>? available)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return Result.Fail("The version is empty.");

        var text = requested.Trim();
        var normalized = Normalize(text);
        if (normalized is null)
            return Result.Fail($"'{text}' is not a valid version.");

        // Floating first: "*" and "1.2.*" are neither exact nor a range, so without this they
        // would fall through and be treated as exact versions.
        if (IsFloating(text))
            return Result.Fail(
                $"The floating version '{text}' is not supported. Pin an exact version, " +
                "or a range such as [1.2.3, ).");

        if (!IsRange(text))
        {
            // With a feed to check against, return the version it actually publishes rather than
            // echoing the normalized spelling, so the download URL matches.
            if (available is null) return Result.Ok(normalized);

            var published = available.FirstOrDefault(candidate => CompareVersions(candidate, normalized) == 0);
            if (published is null)
                return Result.Fail($"Version '{text}' was not found on the package source.");

            return Result.Ok(published);
        }

        // A range can only be satisfied against a feed's version list.
        if (available is null)
            return Result.Fail(
                $"The version range '{text}' cannot be resolved without a package source. " +
                "Pin an exact version instead.");

        var bounds = ParseRange(text);
        var candidates = available.Where(candidate => Satisfies(candidate, bounds)).ToList();
        if (candidates.Count == 0)
            return Result.Fail($"No version on the package source satisfies the range '{text}'.");

        // Lowest satisfying version, which is what NuGet resolves a range to when walking a
        // dependency graph: the declared minimum wins unless something else pulls higher.
        var lowest = candidates
            .OrderBy(candidate => candidate, Comparer<string>.Create(CompareVersions))
            .First();

        return Result.Ok(Normalize(lowest) ?? lowest);
    }

    public static bool IsFloating(string version)
    {
        var text = version.Trim();
        if (text == "*" || text == "*.*" || text == "latest") return true;
        if (text.Contains('*', StringComparison.Ordinal)) return true;
        return false;
    }

    public static bool IsRange(string version)
    {
        var text = version.Trim();
        return text.Length > 0 && (text[0] == '[' || text[0] == '(');
    }

    private readonly record struct Bounds(
        string? Min, bool MinInclusive,
        string? Max, bool MaxInclusive);

    private static Bounds ParseRange(string text)
    {
        var body = text[1..^1].Trim();
        var parts = body.Split(',', 2);

        string? min = null;
        string? max = null;
        var minInclusive = true;
        var maxInclusive = true;

        if (parts.Length == 1)
        {
            // "[1.2.3]" pins one version; "(" or ")" alone is malformed, treat as min.
            var single = parts[0].Trim();
            min = string.IsNullOrEmpty(single) ? null : single;
            max = min;
        }
        else
        {
            var lower = parts[0].Trim();
            var upper = parts[1].Trim();
            minInclusive = text[0] == '[';
            maxInclusive = text[^1] == ']';
            min = string.IsNullOrEmpty(lower) ? null : lower;
            max = string.IsNullOrEmpty(upper) ? null : upper;
        }

        return new Bounds(Normalize(min), minInclusive, Normalize(max), maxInclusive);
    }

    private static bool Satisfies(string candidate, Bounds bounds)
    {
        if (bounds.Min is not null)
        {
            var comparison = CompareVersions(candidate, bounds.Min);
            if (comparison < 0) return false;
            if (comparison == 0 && !bounds.MinInclusive) return false;
        }

        if (bounds.Max is not null)
        {
            var comparison = CompareVersions(candidate, bounds.Max);
            if (comparison > 0) return false;
            if (comparison == 0 && !bounds.MaxInclusive) return false;
        }

        return true;
    }

    private static bool Contains(IReadOnlyList<string> versions, string version) =>
        versions.Any(candidate => CompareVersions(candidate, version) == 0);

    // Numeric-core comparison, matching NuGetPackage.TryParseVersion: a prerelease label is
    // ignored, so "1.0.0-beta" and "1.0.0" compare equal. Good enough for choosing between
    // feed versions; documented approximation, not a hidden one.
    public static int CompareVersions(string left, string right)
    {
        var leftVersion = Parse(left, out var leftParsed);
        var rightVersion = Parse(right, out var rightParsed);
        if (!leftVersion || !rightVersion) return string.CompareOrdinal(left, right);
        return leftParsed!.CompareTo(rightParsed);
    }

    private static bool Parse(string version, out System.Version? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(version)) return false;

        var core = version.Split('-')[0];
        return System.Version.TryParse(Normalize(core), out parsed) && parsed is not null;
    }

    // "1.2" -> "1.2.0" so System.Version parses it; NuGet accepts both spellings.
    private static string? Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;

        var text = version.Trim();
        var core = text.Split('-')[0];
        var parts = core.Split('.');
        if (parts.Length == 2) return $"{parts[0]}.{parts[1]}.0";
        return core;
    }
}