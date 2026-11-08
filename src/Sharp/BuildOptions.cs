namespace Sharp;

// Build-time overrides that the CLI/publish surface can set without mutating project files.
// Configuration falls back to Directory.Build.props/<Configuration> and then "Debug"; the
// RuntimeIdentifier override is only meaningful to publish (build already honors the csproj value).
public sealed record BuildOptions(
    string? Configuration = null,
    string? RuntimeIdentifier = null,
    IReadOnlyDictionary<string, string>? Properties = null)
{
    public static BuildOptions Default { get; } = new();

    public string? GetProperty(string name) =>
        Properties is not null && Properties.TryGetValue(name, out var value) ? value : null;
}
