using System.IO.Compression;
using System.Reflection;

namespace Dotnet10Dependencies;

/// <summary>
/// The .NET 10 compile-time payload that <c>sharp</c> ships, staged at build time by
/// tools/PayloadStager and embedded as the <c>embedded-refs.&lt;refpack&gt;.zip</c> manifest resource
/// of *this* assembly.
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
    /// Version of the .NET reference pack this payload was staged from, e.g. <c>10.0.11</c>. Also the
    /// ref pack version recorded in the zip root, which keys the extraction cache.
    /// </summary>
    public const string PackVersion = "10.0.11";

    /// <summary>
    /// Logical name of the embedded payload. The pack version is part of the name so payloads for
    /// several SDK versions can be committed side by side and referenced unambiguously: each zip holds
    /// the ref pack, the source generators and the ILLink analyzers of one specific SDK, and the
    /// versions are not interchangeable - serving a stale one gives CS0246 on the assemblies that
    /// moved. Keep this in step with the <c>LogicalName</c> in Dotnet10Dependencies.csproj: MSBuild
    /// derives that name from the SDK in use, so if an SDK bump moves it without this constant moving
    /// too, the payload still gets built and embedded - under a name nothing asks for. The build stays
    /// green and the mismatch surfaces later as "resource is missing" at run time, which
    /// <c>EmbeddedPayloadTests</c> catches by looking this name up in the built assembly.
    /// </summary>
    public const string ZipResourceName = "embedded-refs." + PackVersion + ".zip";

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