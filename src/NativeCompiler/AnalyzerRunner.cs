using System.Collections.Immutable;
using CustomCompiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NativeCompiler;

// ---------------------------------------------------------------------------
// AOT-safe analyzer + generator pipeline. Everything comes from the statically
// linked AnalyzerRegistry: no reflection, no AssemblyLoadContext, no dynamic
// package discovery. Mirrors what the SDK feeds csc — generators merge their
// trees first, then the analyzers run over the resulting compilation. Errors
// (Error-severity analyzer/generator diagnostics) gate the build exactly like
// compile errors; warnings are printed (the CLI reports "0 Warning(s)").
// ---------------------------------------------------------------------------
public static class AnalyzerRunner
{
    public static (CSharpCompilation compilation, string errors) Run(
        CSharpCompilation compilation, Project project)
    {
        // 1. Source generators (statically linked, rooted into the native image).
        var (generated, generatorErrors) =
            SourceGeneratorRunner.RunGeneratorDriver(compilation, project,
                AnalyzerRegistry.GetSourceGenerators());
        compilation = generated;

        // 2. Analyzers (statically linked DiagnosticAnalyzer instances).
        var analyzers = AnalyzerRegistry.GetAnalyzers();
        if (analyzers.Length == 0)
            return (compilation, generatorErrors);

        var analyzerOptions = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            SourceGeneratorRunner.CreateOptionsProvider(project));
        var withAnalyzers = compilation.WithAnalyzers(
            analyzers,
            new CompilationWithAnalyzersOptions(
                analyzerOptions,
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: true,
                reportSuppressedDiagnostics: false));

        var diagnostics = withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();

        var analyzerErrors = new List<string>();
        var analyzerWarnings = new List<string>();
        foreach (var diagnostic in diagnostics)
        {
            switch (diagnostic.Severity)
            {
                case DiagnosticSeverity.Error:
                    analyzerErrors.Add(diagnostic.ToString());
                    break;
                case DiagnosticSeverity.Warning:
                case DiagnosticSeverity.Info:
                    analyzerWarnings.Add(diagnostic.ToString());
                    break;
            }
        }

        PrintWarnings(analyzerWarnings);

        var errors = new List<string>();
        if (!string.IsNullOrEmpty(generatorErrors)) errors.Add(generatorErrors);
        errors.AddRange(analyzerErrors);

        return (compilation, string.Join("\n", errors));
    }

    private static void PrintWarnings(List<string> warnings)
    {
        foreach (var warning in warnings)
            Console.WriteLine(warning);
    }
}