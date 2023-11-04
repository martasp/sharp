using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace NativeCompiler;

public static class AsemblyExtensions
{
    public static string ResolveSdkLocation => @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\6.0.18\";

    //public static string AOTLocation(this Assembly asembly)
    //{
    //    Debug.Assert(asembly != null);
    //    Debug.Assert(asembly.ManifestModule != null);
    //    Debug.Assert(asembly.ManifestModule != null);
    //    Debug.Assert(asembly.ManifestModule != null);

    //    var fullFilePath = @$"{ResolveSdkLocation}{asembly.ManifestModule}";

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

        var assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream, symbolsStream);
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