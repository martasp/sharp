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
    /// Logical name of the embedded payload, e.g. <c>embedded-refs.10.0.11.zip</c>. Carries the ref
    /// pack version so a zip is self-describing - it can be told at a glance which SDK it came from -
    /// and several SDK payloads could sit side by side without name collisions. The runtime currently
    /// ships exactly one and refuses to guess among several (see <see cref="FindZipResourceName"/>).
    ///
    /// The name is <i>discovered</i>, never compared against a constant: MSBuild derives the
    /// resource's <c>LogicalName</c> from the SDK that builds it, so the only name that is ever
    /// correct is the one actually embedded. A second, hand-written copy made CI fail:
    /// <c>setup-dotnet</c> installs the newest <c>10.0.x</c> while the developer's SDK bundles an
    /// older ref pack, so MSBuild embedded the resource under its own version while the constant
    /// asked for the write-in one. The build stayed green; every payload-dependent test then failed
    /// at run time with "resource is missing". Discovery removes the copy, so it cannot drift.
    /// </summary>
    public static string ZipResourceName => _zipResourceName ??= FindZipResourceName();

    /// <summary>Prefix of the payload resource name; the version is whatever MSBuild embedded.</summary>
    private const string ResourcePrefix = "embedded-refs.";

    private static string? _zipResourceName;

    private static string FindZipResourceName()
    {
        var names = Owner.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToArray();

        return names switch
        {
            [var name] => name,
            [] => throw new InvalidOperationException(
                $"No '{ResourcePrefix}*.zip' payload resource is embedded in {Owner.GetName().Name}. The payload " +
                "was either not embedded at build time or was trimmed away."),
            _ => throw new InvalidOperationException(
                $"More than one '{ResourcePrefix}*.zip' payload resource is embedded in {Owner.GetName().Name}. " +
                "Ship exactly one."),
        };
    }

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
}
