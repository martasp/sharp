namespace Sharp;

public sealed record PublishOptions(
    string ProjectPath,
    string Configuration = "Release",
    string? RuntimeIdentifier = null,
    string? OutputDirectory = null,
    IReadOnlyDictionary<string, string>? Properties = null);

public sealed record PublishResult(
    bool Success,
    string Errors,
    IReadOnlyList<string> Outputs,
    TimeSpan Duration,
    Project? Project,
    string? PublishDirectory,
    string Warnings);
