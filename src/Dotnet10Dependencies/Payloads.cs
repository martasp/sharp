using System.IO.Compression;
using System.Reflection;

namespace Dotnet10Dependencies;

/// <summary>
/// The .NET 10 compile-time payload that <c>sharp</c> ships, staged at build time by
/// tools/PayloadStager and embedded as the <c>embedded-refs.zip</c> manifest resource of *this*
/// assembly.
/// </summary>
/// <remarks>
/// <para>
/// NativeCompiler used to embed the payload in its own assembly. It now references this project
/// instead, so NativeCompiler.csproj carries a single ProjectReference rather than an Exec plus an
/// EmbeddedResource, and the resource lookup that NativeAOT depends on lives in exactly one place.
/// </para>
/// <para>
/// That lookup must stay <c>typeof(Payloads).Assembly</c>. An earlier prototype resolved it with
/// <c>Assembly.Load("Dotnet10Dependencies")</c>, which builds cleanly and passes every test run under
/// <c>dotnet build</c> - and then throws <see cref="FileNotFoundException"/> in the published binary,
/// because a by-name load is invisible to the AOT compiler, so this assembly is never rooted and its
/// resources are trimmed away. Always reach the payload through the methods below.
/// </para>
/// </remarks>
public static class Payloads
{
    /// <summary>
    /// Logical name of the embedded payload. Deliberately *not* versioned: MSBuild derives the
    /// resource name from the SDK in use, so baking the ref pack version into the name makes the
    /// constant and the embedded resource disagree whenever CI or a developer builds against a
    /// different SDK patch. The payload gets built and embedded under the SDK's name while the
    /// constant asks for its own, the build stays green, and the mismatch surfaces later as "resource
    /// is missing" at run time - which is the one environment sharp exists to support. A fixed name
    /// has no such coupling. Payload freshness is already keyed off a content hash of the zip (see
    /// EmbeddedSdkRefs), never off the name. Keep this in step with the <c>LogicalName</c> in
    /// Dotnet10Dependencies.csproj; <c>EmbeddedPayloadTests</c> looks this name up in the built
    /// assembly.
    /// </summary>
    public const string ZipResourceName = "embedded-refs.zip";

    /// <summary>
    /// The assembly carrying the payload. Resolved through <c>typeof</c> so the compiler roots this
    /// assembly; see the remarks on <see cref="Payloads"/>.
    /// </summary>
    private static Assembly Owner => typeof(Payloads).Assembly;

    /// <summary>
    /// Opens the embedded payload zip for reading. The caller owns the returned stream.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The payload resource is missing, which means this assembly was built without it (a plain
    /// <c>dotnet build</c> of the project alone, or a trimmed publish that dropped the resource).
    /// </exception>
    public static Stream LoadZip() => Owner.GetManifestResourceStream(ZipResourceName)
        ?? throw new InvalidOperationException(
            $"The '{ZipResourceName}' resource is missing from {Owner.GetName().Name}. The payload was " +
            "either not embedded at build time or was trimmed away.");

    /// <summary>
    /// Opens the embedded payload as a read-only zip archive. Disposing the archive disposes the
    /// underlying stream.
    /// </summary>
    public static ZipArchive OpenZip() => new(LoadZip(), ZipArchiveMode.Read, leaveOpen: false);
}