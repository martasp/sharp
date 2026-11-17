using System.Collections.Immutable;
using CustomCompiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Sharp;

// ---------------------------------------------------------------------------
// AOT-safe analyzer + generator pipeline. Everything comes from the statically
// linked AnalyzerRegistry: no reflection, no AssemblyLoadContext, no dynamic
// package discovery. Mirrors what the SDK feeds csc — generators merge their
// trees first, then the analyzers run over the resulting compilation. Errors
// (Error-severity analyzer/generator diagnostics) gate the build exactly like
// compile errors. Warning-severity diagnostics are returned (not printed) so
// the CLI renders them per-project in the SDK terminal-logger grammar; Info
// diagnostics are dropped (the SDK hides them at default verbosity).
// ---------------------------------------------------------------------------
public static class AnalyzerRunner
{
    public static (CSharpCompilation compilation, string errors, string warnings) Run(
        CSharpCompilation compilation, Project project)
    {
        // 1. Source generators (statically linked SDK set + JIT-only discovery of analyzer
        //    project/package generators; see SourceGeneratorRunner.RunGenerators).
        var (generated, generatorErrors) = SourceGeneratorRunner.RunGenerators(compilation, project); //~1s
        compilation = generated;

        // 2. Project-relevant analyzers (statically linked DiagnosticAnalyzer instances).
        var analyzers = SelectAnalyzers(compilation);
        if (analyzers.Length == 0)
            return (compilation, generatorErrors, string.Empty);

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

        var diagnostics = withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult(); //1.3s

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
                    analyzerWarnings.Add(diagnostic.ToString());
                    break;
                case DiagnosticSeverity.Info:
                    // SDK parity: info-severity diagnostics (e.g. SYSLIB1045) never surface at
                    // default verbosity, so they are not reported to the CLI either.
                    break;
            }
        }

        var errors = new List<string>();
        if (!string.IsNullOrEmpty(generatorErrors)) errors.Add(generatorErrors);
        errors.AddRange(analyzerErrors);

        return (compilation, string.Join("\n", errors), string.Join("\n", analyzerWarnings));
    }

    private static readonly string[] InteropMarkers =
    [
        "InteropServices", "DllImport", "LibraryImport", "ComImport", "ComVisible",
        "GeneratedCom", "NativeMarshalling", "CustomMarshaller", "MarshalAs",
        "UnmanagedCallersOnly", "JSImport", "JSExport",
    ];

    private static readonly string[] RegexMarkers = ["Regex", "RegularExpressions"];

    private static ImmutableArray<DiagnosticAnalyzer> SelectAnalyzers(CSharpCompilation compilation)
    {
        var analyzers = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();

        var interop = false;
        var regex = false;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var text = tree.GetText().ToString();
            interop |= ContainsAny(text, InteropMarkers);
            regex |= ContainsAny(text, RegexMarkers);
            if (interop && regex) break;
        }

        if (interop)
            analyzers.AddRange(AnalyzerRegistry.GetInteropAnalyzers());

        if (regex)
            analyzers.AddRange(AnalyzerRegistry.GetRegexAnalyzers());

        return analyzers.ToImmutable();
    }

    private static bool ContainsAny(string text, string[] markers)
    {
        foreach (var marker in markers)
            if (text.Contains(marker, StringComparison.Ordinal))
                return true;

        return false;
    }
}