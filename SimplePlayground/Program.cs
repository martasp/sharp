using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

var syntaxTree = CSharpSyntaxTree.ParseText(@"
using System;
public class C
{
    public static void Main()
    {
        System.Console.WriteLine(""Hello World!"");
        System.Console.ReadLine();
    }
}");

// Reference assemblies come from the running shared framework, resolved at runtime
// (TRUSTED_PLATFORM_ASSEMBLIES), so there is no hardcoded pack path or version.
var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
    .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
    .ToArray();

var options = new CSharpCompilationOptions(OutputKind.ConsoleApplication)
    .WithPlatform(Platform.AnyCpu)
    .WithOptimizationLevel(OptimizationLevel.Debug);

var compilation = CSharpCompilation.Create(
    assemblyName: "MyApp",
    syntaxTrees: [syntaxTree],
    references: references,
    options: options
);

using var peStream = new MemoryStream();
using var pdbStream = new MemoryStream();

var emitResult = compilation.Emit(peStream, pdbStream);
if (!emitResult.Success)
{
    foreach (var diagnostic in emitResult.Diagnostics)
        Console.WriteLine(diagnostic);

    return 1;
}

// The dotnet muxer resolves managed assemblies by .dll extension, so name the emit
// accordingly regardless of platform.
const string assemblyFileName = "MyApp.dll";

var outputDir = Path.Combine(AppContext.BaseDirectory, "MyApp.out");
Directory.CreateDirectory(outputDir);
var assemblyPath = Path.Combine(outputDir, assemblyFileName);

await File.WriteAllBytesAsync(assemblyPath, peStream.ToArray());
await File.WriteAllBytesAsync(Path.Combine(outputDir, "MyApp.pdb"), pdbStream.ToArray());

// A framework-dependent assembly needs a runtimeconfig.json beside it so the muxer
// resolves the shared framework version.
var runtimeConfig = $$"""
{
  "runtimeOptions": {
    "tfm": "net10.0",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "{{Environment.Version}}"
    }
  }
}
""";
await File.WriteAllTextAsync(Path.Combine(outputDir, "MyApp.runtimeconfig.json"), runtimeConfig);

Console.WriteLine($"Compilation succeeded: {references.Length} references -> {outputDir}");

// Roslyn emits managed output without an apphost, so run it through the dotnet muxer
// rather than executing the file directly (no exec bit, no embedded host).
var startInfo = new ProcessStartInfo
{
    FileName = "dotnet",
    ArgumentList = { assemblyPath },
    RedirectStandardOutput = true,
    UseShellExecute = false,
    CreateNoWindow = true
};

using (var process = Process.Start(startInfo))
{
    if (process is null)
    {
        Console.WriteLine("Failed to start the compiled assembly.");
        return 1;
    }

    // Read before WaitForExit: synchronous read after wait can deadlock once the pipe buffer fills.
    var result = process.StandardOutput.ReadToEnd();
    process.WaitForExit();

    Console.WriteLine("Output:");
    Console.WriteLine(result);
}

return 0;
