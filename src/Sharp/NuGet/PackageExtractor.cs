using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Sharp.NuGet;

// Unpacks a downloaded .nupkg into the global packages folder.
//
// Two safety properties matter more than anything else here:
//   - Never leave a half-written package directory behind. Extraction goes to a temp folder and
//     is moved into place only once it is complete, because the compiler treats the presence of
//     <id>/<version>/ as "already restored" and would happily build against a partial tree.
//   - Reject entries whose path escapes the destination ("zip slip"). A malicious or corrupt feed
//     must not be able to write outside the packages folder.
public static class PackageExtractor
{
    // Marker file the SDK writes into every extracted package; its presence distinguishes a
    // package extracted by a real restore from a hand-placed directory.
    public const string MetadataFileName = ".nupkg.metadata";

    public static string PackageDirectory(string globalPackagesFolder, string packageId, string version) =>
        Path.Combine(globalPackagesFolder, packageId.ToLowerInvariant(), version);

    public static bool IsExtracted(string globalPackagesFolder, string packageId, string version) =>
        Directory.Exists(PackageDirectory(globalPackagesFolder, packageId, version));

    // `nupkgStream` is read but not disposed; ownership stays with the caller.
    public static void Extract(Stream nupkgStream, string globalPackagesFolder, string packageId, string version)
    {
        var destination = PackageDirectory(globalPackagesFolder, packageId, version);

        // A pre-existing directory without our marker is not trusted (it may be partial). Move it
        // aside rather than merging into it, so a stale tree never leaks into a restore.
        if (Directory.Exists(destination))
        {
            var stale = destination + ".stale-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                Directory.Move(destination, stale);
                Directory.Delete(stale, recursive: true);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Could not replace the existing package directory '{destination}'.", exception);
            }
        }

        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parent)) throw new InvalidOperationException($"Invalid package path '{destination}'.");
        Directory.CreateDirectory(parent);

        var staging = Path.Combine(parent, ".tmp-" + Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(staging);
            ExtractTo(nupkgStream, staging);

            File.WriteAllText(
                Path.Combine(staging, MetadataFileName),
                $"version: {version}\ncontentHash: \nsource: \n");

            Directory.Move(staging, destination);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }
    }

    private static void ExtractTo(Stream nupkgStream, string stagingDirectory)
    {
        using var archive = new ZipArchive(nupkgStream, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var entry in archive.Entries)
        {
            // Directory entries carry no payload; creating the directory is implicit below.
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var target = ResolveEntryPath(stagingDirectory, entry.FullName);

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            PreserveExecutableBit(entry.FullName, target);
        }
    }

    // Zip entry attributes are not surfaced by ZipArchive, and NuGet's Linux tools (notably the
    // NativeAOT compiler at tools/ilc) must stay executable after extraction.
    private static void PreserveExecutableBit(string entryName, string target)
    {
        if (OperatingSystem.IsWindows()) return;

        var normalized = entryName.Replace('\\', '/');
        var executable = normalized.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)
            || (normalized.Contains("/tools/", StringComparison.OrdinalIgnoreCase)
                && !Path.HasExtension(normalized));
        if (!executable) return;

        var mode = File.GetUnixFileMode(target);
        File.SetUnixFileMode(target,
            mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    // Guards against "../" traversal in an entry name. The full path is compared against the
    // destination prefix rather than pattern-matching the entry, so obfuscated forms
    // ("a/../../b", absolute paths, backslash separators) are all caught.
    private static string ResolveEntryPath(string destinationDirectory, string entryName)
    {
        var normalizedName = entryName.Replace('\\', '/');
        if (Path.IsPathRooted(normalizedName))
            throw new InvalidOperationException(
                $"The package contains an absolute path entry ('{entryName}') and cannot be extracted.");

        var destination = Path.GetFullPath(destinationDirectory);
        var target = Path.GetFullPath(Path.Combine(destination, normalizedName));

        var prefix = destination.EndsWith(Path.DirectorySeparatorChar)
            ? destination
            : destination + Path.DirectorySeparatorChar;

        if (!target.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The package contains an entry ('{entryName}') that would be written outside the packages folder.");

        return target;
    }

    // SHA-512 base64, the digest the SDK stores alongside the .nupkg and puts in project.assets.json.
    public static string ComputeSha512(Stream stream)
    {
        if (stream.CanSeek) stream.Position = 0;
        using var sha = SHA512.Create();
        return Convert.ToBase64String(sha.ComputeHash(stream));
    }

    // The SDK writes <id>.<version>.nupkg.sha512 next to the package; when it is present the
    // download is verified against it. Constant-time compare keeps this free of a timing signal.
    public static bool VerifyHash(Stream nupkgStream, string expectedBase64)
    {
        if (string.IsNullOrWhiteSpace(expectedBase64)) return true;

        var actual = ComputeSha512(nupkgStream);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual),
            Encoding.ASCII.GetBytes(expectedBase64.Trim()));
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // A leftover temp directory is not worth failing the restore over.
        }
    }
}