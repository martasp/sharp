using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace NativeCompiler;

public static class AsemblyExtensions
{
    public static string ResolveSdkRuntimeAssemblyPath
    {
        get
        {
            var runtimeRoot = Path.Combine(ResolveDotnetRoot(), "shared", "Microsoft.NETCore.App"); // shared dlls are for running, have to be copied near exe.
            return SelectBestVersionDirectory(runtimeRoot, preferredMajor: Environment.Version.Major);
        }
    }

    public static string ResolveSdkRefLocation
    {
        get
        {
            var refPackRoot = Path.Combine(ResolveDotnetRoot(), "packs", "Microsoft.NETCore.App.Ref"); // refs are for compiling
            var versionDir = SelectBestVersionDirectory(refPackRoot, preferredMajor: Environment.Version.Major);
            return SelectBestVersionDirectory(Path.Combine(versionDir, "ref"));
        }
    }

    public static string ResolveSdkHostLocation => Path.Combine(ResolveDotnetRoot(), "host", "fxr");

    public static string ResolveSdkRoot => ResolveDotnetRoot();

    private static string ResolveDotnetRoot()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot) && Directory.Exists(dotnetRoot))
            return dotnetRoot;

        foreach (var candidate in new[]
                 {
                     @"C:\Program Files\dotnet",
                     @"C:\Program Files (x86)\dotnet",
                     @"/usr/share/dotnet",
                 })
        {
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the .NET SDK install directory. Set the DOTNET_ROOT environment variable.");
    }

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
        Console.WriteLine(result.Success ? "Sucess!!" : "Failed");
        var errors = result.Diagnostics.Select(item => item.GetMessage() + item).ToArray();
        var allErrors = string.Join(Environment.NewLine, errors);
        Console.WriteLine(allErrors);
        return allErrors;
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