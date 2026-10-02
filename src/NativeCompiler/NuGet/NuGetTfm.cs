namespace NativeCompiler.NuGet;

public static class NuGetTfm
{
    private enum Kind
    {
        Unknown,
        Framework,
        Standard,
        Core,
    }

    private readonly record struct Tfm(Kind Kind, int Major, int Minor, int Build)
    {
        public int Magnitude => Major * 1000 + Minor * 10 + Build;
    }

    // Higher score = more compatible with the target TFM. Zero = not usable.
    // Used to pick among a package's lib/<tfm> folders and its nuspec dependency groups.
    public static int Score(string candidateTfm, string targetTfm)
    {
        var target = Parse(targetTfm);
        if (target.Kind == Kind.Unknown) return 0;

        var candidate = Parse(candidateTfm);
        if (candidate.Kind == Kind.Unknown) return 0;

        if (candidate.Kind == target.Kind && candidate.Major == target.Major && candidate.Minor == target.Minor)
            return 1_000_000;

        switch (target.Kind)
        {
            case Kind.Core:
                if (candidate.Kind == Kind.Core && IsSameOrOlder(candidate, target))
                    return 500_000 + candidate.Magnitude;
                if (candidate.Kind == Kind.Standard)
                    return 300_000 + candidate.Magnitude;
                return 0;

            case Kind.Standard:
                if (candidate.Kind == Kind.Standard && IsSameOrOlder(target, candidate))
                    return 300_000 + candidate.Magnitude;
                return 0;

            case Kind.Framework:
                if (candidate.Kind == Kind.Framework && IsSameOrOlder(target, candidate))
                    return 200_000 + candidate.Magnitude;
                if (candidate.Kind == Kind.Standard)
                    return 100_000 + candidate.Magnitude;
                return 0;

            default:
                return 0;
        }
    }

    private static bool IsSameOrOlder(Tfm older, Tfm newer)
    {
        if (older.Major != newer.Major) return older.Major < newer.Major;
        if (older.Minor != newer.Minor) return older.Minor < newer.Minor;
        return older.Build <= newer.Build;
    }

    private static Tfm Parse(string tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm)) return default;

        // Drop platform suffixes: "net10.0-windows7.0" -> "net10.0".
        var value = tfm.Split('-', 2)[0].Trim().ToLowerInvariant();

        if (value.StartsWith("netstandard", StringComparison.Ordinal))
            return ParseVersion(value, "netstandard", Kind.Standard);

        if (value.StartsWith("netcoreapp", StringComparison.Ordinal))
            return ParseVersion(value, "netcoreapp", Kind.Core);

        if (value.StartsWith("net", StringComparison.Ordinal))
        {
            var rest = value.Substring(3);

            // "net6.0"/"net10.0" (.NET Core+, first segment >= 5) vs "net472"/"net48" (legacy .NET Framework).
            if (rest.Contains('.') && ParseFirstSegment(rest) >= 5)
                return ParseVersion(rest, "", Kind.Core);

            return ParseFramework(rest);
        }

        return default;
    }

    private static Tfm ParseVersion(string value, string prefix, Kind kind)
    {
        var rest = value.Substring(prefix.Length).TrimStart('.');
        var (major, minor, build) = ParseSegments(rest);
        return new Tfm(kind, major, minor, build);
    }

    private static Tfm ParseFramework(string digits)
    {
        // "35" -> 3.5   "45" -> 4.5   "472" -> 4.7.2   "48" -> 4.8   "481" -> 4.8.1
        var major = digits.Length > 0 && int.TryParse(digits[0].ToString(), out var m) ? m : 0;
        var minor = digits.Length > 1 && int.TryParse(digits[1].ToString(), out var mi) ? mi : 0;
        var build = digits.Length > 2 && int.TryParse(digits.Substring(2), out var b) ? b : 0;
        return new Tfm(Kind.Framework, major, minor, build);
    }

    private static int ParseFirstSegment(string value)
    {
        var end = value.IndexOfAny(new[] { '.', '-' });
        var segment = end < 0 ? value : value.Substring(0, end);
        return int.TryParse(segment, out var number) ? number : 0;
    }

    private static (int Major, int Minor, int Build) ParseSegments(string value)
    {
        var parts = value.Split('.');
        return (
            parts.Length > 0 && int.TryParse(parts[0], out var major) ? major : 0,
            parts.Length > 1 && int.TryParse(parts[1], out var minor) ? minor : 0,
            parts.Length > 2 && int.TryParse(parts[2], out var build) ? build : 0);
    }
}