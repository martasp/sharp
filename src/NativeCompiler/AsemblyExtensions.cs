using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace NativeCompiler;

public static class AsemblyExtensions
{
    // Every path below is served from the payload embedded in this assembly (see EmbeddedSdkRefs), so
    // sharp resolves SDK layout without an installed .NET SDK.
    public static string ResolveSdkRefLocation => EmbeddedSdkRefs.ReferenceAssemblies;

    // The targeting pack ships real Roslyn source generators (System.Text.RegularExpressions.Generator,
    // System.Text.Json.SourceGeneration, Microsoft.Interop.*, ...) under analyzers/dotnet/cs. The same
    // assemblies .NET SDK feeds to csc for every project. Used by the GENERATOR_PIPELINE feature.
    public static string ResolveRefPackAnalyzersLocation => EmbeddedSdkRefs.RefPackAnalyzers;

    // The ASP.NET Core targeting pack's own generators sit beside the NETCore ones. sharp links them
    // too, so the self-host build resolves them out of the embedded payload like the rest.
    public static string ResolveAspNetRefPackAnalyzersLocation => EmbeddedSdkRefs.AspNetRefPackAnalyzers;

    // Resolves the ref folder for a shared-framework <FrameworkReference> item, e.g.
    // "Microsoft.AspNetCore.App" -> <payload>/packs/Microsoft.AspNetCore.App.Ref/<ver>/ref/<tfm>.
    public static string ResolveFrameworkRefLocation(string frameworkName, string? targetTfm)
    {
        if (!frameworkName.EndsWith(".Ref", StringComparison.OrdinalIgnoreCase))
        {
            frameworkName += ".Ref";
        }

        var versionDir = Path.Combine(EmbeddedSdkRefs.Root, "packs", frameworkName, EmbeddedSdkRefs.RefPackVersion);
        var refDir = Path.Combine(versionDir, "ref");
        var tfm = string.IsNullOrWhiteSpace(targetTfm) ? "net" + Environment.Version.Major + ".0" : targetTfm;

        var exactTfmDir = Path.Combine(refDir, tfm);
        if (Directory.Exists(exactTfmDir)) return exactTfmDir;

        return SelectBestVersionDirectory(refDir);
    }

    public static string ResolveSdkRoot => EmbeddedSdkRefs.Root;

    // The "Sdks" folder of the active SDK version, used by HintPath references into the SDK directory
    // (e.g. Microsoft.Extensions.DependencyModel.dll lives one level up).
    public static string ResolveSdkMsBuildSdksPath => Path.Combine(EmbeddedSdkRefs.SdkDirectory, "Sdks");

    private static string SelectBestVersionDirectory(string parent, int? preferredMajor = null)
    {
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException($"Could not find a part of the path '{parent}'.");

        var paths = Directory.EnumerateDirectories(parent)
            .Select(directory => new { Path = directory, Version = ParseVersion(Path.GetFileName(directory)) })
            .Where(item => item.Version is not null)
            .OrderByDescending(item => item.Version)
            .Select(item => item.Path)
            .ToArray();

        string? best = null;

        if (preferredMajor is not null)
        {
            best = paths.FirstOrDefault(path => ParseVersion(Path.GetFileName(path))?.Major == preferredMajor);
        }

        best ??= paths.FirstOrDefault();

        return best ?? throw new DirectoryNotFoundException($"Could not find a part of the path '{parent}'.");
    }

    private static Version? ParseVersion(string? value)
    {
        if (value is null) return null;

        var digits = new string(value.SkipWhile(character => !char.IsDigit(character)).ToArray());
        return Version.TryParse(digits, out var version) ? version : null;
    }


    //public static string AOTLocation(this Assembly asembly)
    //{
    //    Debug.Assert(asembly != null);
    //    Debug.Assert(asembly.ManifestModule != null);
    //    Debug.Assert(asembly.ManifestModule != null);
    //    Debug.Assert(asembly.ManifestModule != null);

    //    var fullFilePath = @$"{ResolveSdkRefLocation}{asembly.ManifestModule}";

    //    Debug.Assert(fullFilePath != null);
    //    Debug.Assert(fullFilePath.Length > 0);
    //    Debug.Assert(File.Exists(fullFilePath));

    //    return fullFilePath;
    //}

    public static string PrintErrors(this EmitResult result)
    {
        // Only surface real errors (emit result success is derived from them) and never write to
        // the console from the library — hidden diagnostics like "unnecessary using" (CS8019) are
        // not user-facing and must not leak into CLI output. Errors are formatted msbuild-style
        // ("<path>(<line>,<col>): error <code>: <message>") so the CLI output matches dotnet build.
        return string.Join(Environment.NewLine,
            result.Diagnostics
                .Where(item => item.Severity == DiagnosticSeverity.Error)
                .Select(item =>
                {
                    if (item.Location.IsInSource)
                    {
                        var line = item.Location.GetMappedLineSpan();
                        var position = line.StartLinePosition;
                        return $"{line.Path}({position.Line + 1},{position.Character + 1}): error {item.Id}: {item.GetMessage()}";
                    }

                    return item.ToString();
                }));
    }

    // emit the compilation result into a byte array.
    // throw an exception with corresponding message
    // if there are errors
    public static byte[] EmitToArray(this CSharpCompilation compilation)
    {
        using (var stream = new MemoryStream())
        {
            // emit result into a stream
            var emitResult = compilation.Emit(stream);

            emitResult.PrintErrors();

            // get the byte array from a stream
            return stream.ToArray();
        }
    }

    public static Assembly CreateAssembly(this CSharpCompilation compilation, string symbolsName,
        List<EmbeddedText> embeddedTexts)
    {
        using var assemblyStream = new MemoryStream();
        using var symbolsStream = new MemoryStream();

        var emitOptions = new EmitOptions(
            debugInformationFormat: DebugInformationFormat.PortablePdb,
            pdbFilePath: symbolsName);

        var result = compilation.Emit(
            assemblyStream,
            symbolsStream,
            embeddedTexts: embeddedTexts,
            options: emitOptions);

        if (!result.Success)
        {
            var errors = new List<string>();

            var failures = result.Diagnostics.Where(diagnostic =>
                diagnostic.IsWarningAsError ||
                diagnostic.Severity == DiagnosticSeverity.Error);

            foreach (var diagnostic in failures)
                errors.Add(diagnostic.ToString());

            throw new Exception(string.Join("\n", errors));
        }

        assemblyStream.Seek(0, SeekOrigin.Begin);
        symbolsStream?.Seek(0, SeekOrigin.Begin);

        var context = new AssemblyLoadContext("OneTimeContext", isCollectible: true);
        var assembly = context.LoadFromStream(assemblyStream, symbolsStream);

        return assembly;
    }
}


//var emitOptions = new EmitOptions(
//        debugInformationFormat: DebugInformationFormat.PortablePdb,
//          : symbolsName);

//var embeddedTexts = new List<EmbeddedText>
//        {
//            EmbeddedText.FromSource(sourceCodePath, sourceText),
//        };

//EmitResult result = compilation.Emit(
//    peStream: assemblyStream,
//    pdbStream: symbolsStream,
//    embeddedTexts: embeddedTexts,
//    options: emitOptions);

//Things to reaserch
//https://stackoverflow.com/questions/50649795/how-to-debug-dll-generated-from-roslyn-compilation
//https://www.codeproject.com/Articles/1215168/Using-Roslyn-for-Compiling-Code-into-Separate-Net