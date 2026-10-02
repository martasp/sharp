using System.Text.Json;

namespace NativeCompiler.NuGet;

// Minimal V3 flat-container client. This is the first network code in the compiler, so it is
// deliberately thin: one shared HttpClient, no handler customisation, no retry policy beyond a
// bounded attempt, and every failure reported with the package id and the URL that was tried.
//
// JSON is read with JsonDocument rather than a source-generated serializer context, which is the
// idiom the rest of the codebase already uses (DepsJsonWriter.cs:27, NativeCompilerUtils.cs:564).
public static class NuGetFeed
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            // A hung feed must not hang restore forever; the SDK's own default is comparable.
            Timeout = TimeSpan.FromSeconds(100),
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "sharp-native-compiler");
        return client;
    }

    // nuget.config stores the *service* index (https://api.nuget.org/v3/index.json) but downloads
    // live under a sibling path
    // (https://api.nuget.org/v3-flatcontainer/...). Resolve the real endpoint from the service
    // index when it is reachable; fall back to the conventional layout for a known host so a
    // local/offline feed still works without an extra request.
    public static async Task<Uri> GetFlatContainerAsync(Uri serviceIndex, CancellationToken cancellationToken = default)
    {
        // A folder source is already the flat-container root; there is no service index to consult.
        if (IsLocal(serviceIndex)) return serviceIndex;

        try
        {
            using var response = await Client.GetAsync(serviceIndex, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(
                    await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
                if (document.RootElement.TryGetProperty("resources", out var resources))
                {
                    foreach (var resource in resources.EnumerateArray())
                    {
                        if (!resource.TryGetProperty("@type", out var type)) continue;
                        if (type.GetString() != "PackageBaseAddress/3.0.0") continue;
                        if (!resource.TryGetProperty("@id", out var id)) continue;

                        var value = id.GetString();
                        if (!string.IsNullOrEmpty(value) && Uri.TryCreate(value, UriKind.Absolute, out var flat))
                            return flat;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Fall through to the conventional layout.
        }

        return ConventionalFlatContainer(serviceIndex);
    }

    // .../v3/index.json -> .../v3-flatcontainer/
    public static Uri ConventionalFlatContainer(Uri serviceIndex)
    {
        var builder = new UriBuilder(serviceIndex);
        var path = builder.Path.TrimEnd('/');

        if (path.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
            path = path[..^"/index.json".Length];

        path = path.Replace("/v3/", "/v3-flatcontainer/", StringComparison.OrdinalIgnoreCase);

        builder.Path = path.TrimEnd('/') + "/";
        return builder.Uri;
    }

    // GET {flat}/{id-lower}/index.json -> the package's published versions, oldest first.
    public static async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId, Uri flatContainer, CancellationToken cancellationToken = default)
    {
        if (IsLocal(flatContainer))
            return ReadLocalVersions(packageId, flatContainer);

        var url = VersionIndexUri(packageId, flatContainer);
        using var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new NuGetFeedException($"Package '{packageId}' was not found at {url}.");

            throw new NuGetFeedException(
                $"Could not read versions for package '{packageId}' from {url} (HTTP {(int)response.StatusCode}).");
        }

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));

        if (!document.RootElement.TryGetProperty("versions", out var versions) ||
            versions.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return versions.EnumerateArray()
            .Select(element => element.GetString())
            .Where(version => !string.IsNullOrEmpty(version))
            .Select(version => version!)
            .ToList();
    }

    // GET {flat}/{id-lower}/{version}/{id-lower}.{version}.nupkg
    public static async Task<Stream> DownloadPackageAsync(
        string packageId, string version, Uri flatContainer, CancellationToken cancellationToken = default)
    {
        if (IsLocal(flatContainer))
            return OpenLocalPackage(packageId, version, flatContainer);

        var url = PackageUri(packageId, version, flatContainer);
        var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new NuGetFeedException($"Package '{packageId}' version '{version}' was not found at {url}.");

            throw new NuGetFeedException(
                $"Could not download package '{packageId}' version '{version}' from {url} " +
                $"(HTTP {(int)response.StatusCode}).");
        }

        // Buffer so the stream outlives the response and is seekable for hashing.
        var memory = new MemoryStream();
        await response.Content.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        memory.Position = 0;
        return memory;
    }

    // A folder source points at the flat-container layout on disk. Serving it over HTTP is the
    // usual approach, but restore must work offline, so read the same files directly.
    public static bool IsLocal(Uri source) => source.IsFile;

    private static string LocalPath(Uri uri) =>
        Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/').Replace('\\', '/');

    private static IReadOnlyList<string> ReadLocalVersions(string packageId, Uri flatContainer)
    {
        var index = Path.Combine(LocalPath(flatContainer), packageId.ToLowerInvariant(), "index.json");
        if (!File.Exists(index)) return Array.Empty<string>();

        using var document = JsonDocument.Parse(File.ReadAllText(index));
        if (!document.RootElement.TryGetProperty("versions", out var versions) ||
            versions.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return versions.EnumerateArray()
            .Select(element => element.GetString())
            .Where(version => !string.IsNullOrEmpty(version))
            .Select(version => version!)
            .ToList();
    }

    private static Stream OpenLocalPackage(string packageId, string version, Uri flatContainer)
    {
        var lower = packageId.ToLowerInvariant();
        var path = Path.Combine(LocalPath(flatContainer), lower, version, $"{lower}.{version}.nupkg");
        if (!File.Exists(path))
            throw new NuGetFeedException($"Package '{packageId}' version '{version}' was not found at {path}.");

        // Read into memory so the returned stream matches the buffered HTTP path and callers get a
        // seekable stream for hashing.
        var memory = new MemoryStream(File.ReadAllBytes(path));
        memory.Position = 0;
        return memory;
    }

    public static Uri VersionIndexUri(string packageId, Uri flatContainer) =>
        Combine(flatContainer, $"{packageId.ToLowerInvariant()}/index.json");

    public static Uri PackageUri(string packageId, string version, Uri flatContainer) =>
        Combine(flatContainer,
            $"{packageId.ToLowerInvariant()}/{version}/{packageId.ToLowerInvariant()}.{version}.nupkg");

    // Feed URLs frequently carry a query string or end without a slash; normalise so relative
    // combination never drops the last path segment.
    private static Uri Combine(Uri baseUri, string relative)
    {
        var text = baseUri.ToString();
        if (!text.EndsWith('/')) text += "/";
        return new Uri(new Uri(text), relative);
    }
}

public sealed class NuGetFeedException : Exception
{
    public NuGetFeedException(string message) : base(message) { }
    public NuGetFeedException(string message, Exception inner) : base(message, inner) { }
}