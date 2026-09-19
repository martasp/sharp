using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CustomCompiler
{
    // Statically constructs every reachable SDK analyzer assembly type. No reflection, no runtime
    // assembly loading: every type below is public with a public parameterless constructor, and the
    // generator classes are public IIncrementalGenerator implementations wrapped through the public
    // GeneratorExtensions.AsSourceGenerator. Works identically under JIT and NativeAOT (the analyzer
    // assemblies are direct references rooted into the native image by reachability).
    public static class AnalyzerRegistry
    {
        public static ImmutableArray<DiagnosticAnalyzer> GetAnalyzers()
            => ImmutableArray.Create<DiagnosticAnalyzer>(
                new ILLink.RoslynAnalyzer.COMAnalyzer(),
                new ILLink.RoslynAnalyzer.DynamicallyAccessedMembersAnalyzer(),
                new ILLink.RoslynAnalyzer.RequiresAssemblyFilesAnalyzer(),
                new ILLink.RoslynAnalyzer.RequiresDynamicCodeAnalyzer(),
                new ILLink.RoslynAnalyzer.RequiresUnreferencedCodeAnalyzer(),

                new Microsoft.Interop.Analyzers.ConvertToLibraryImportAnalyzer(),
                new Microsoft.Interop.Analyzers.CustomMarshallerAttributeAnalyzer(),
                new Microsoft.Interop.Analyzers.NativeMarshallingAttributeAnalyzer(),
                new Microsoft.Interop.Analyzers.ShapeBreakingDiagnosticSuppressor(),
                new Microsoft.Interop.Analyzers.AddGeneratedComClassAnalyzer(),
                new Microsoft.Interop.Analyzers.ComHostingDoesNotSupportGeneratedComInterfaceAnalyzer(),
                new Microsoft.Interop.Analyzers.ConvertComImportToGeneratedComInterfaceAnalyzer(),
                new Microsoft.Interop.Analyzers.GeneratedComInterfaceAttributeAnalyzer(),
                new Microsoft.Interop.Analyzers.RuntimeComApiUsageWithSourceGeneratedComAnalyzer(),

                new System.Text.RegularExpressions.Generator.UpgradeToGeneratedRegexAnalyzer()

                // --- 2. Interop & Source Generator Analyzers ---
                // SYSLIB1030-1045 (JSON/Regex) and SYSLIB1051-1090 (Interop) validity
                // diagnostics are emitted by the source generators themselves, which are
                // wrapped via GeneratorExtensions.AsSourceGenerator below.

                );

        public static ImmutableArray<CodeFixProvider> GetCodeFixes()
            => ImmutableArray.Create<CodeFixProvider>(
                new ILLink.CodeFix.DynamicallyAccessedMembersCodeFixProvider(),
                new ILLink.CodeFix.RequiresAssemblyFilesCodeFixProvider(),
                new ILLink.CodeFix.RequiresDynamicCodeCodeFixProvider(),
                new ILLink.CodeFix.RequiresUnreferencedCodeCodeFixProvider(),
                new ILLink.CodeFix.UnconditionalSuppressMessageCodeFixProvider(),
                
                // TODO implement code fixes later.
                //new ILLink.CodeFixProvider.RequiresUnreferencedCodeCodeFixProvider(),
                //new ILLink.CodeFixProvider.RequiresDynamicCodeCodeFixProvider(),
                //// Interop Code Fixes (e.g., converting [DllImport] to [LibraryImport])
                //new Microsoft.Interop.LibraryImportGeneratorCodeFixProvider(),


                new Microsoft.Interop.Analyzers.AddDisableRuntimeMarshallingAttributeFixer(),
                new Microsoft.Interop.Analyzers.ConvertToLibraryImportFixer(),
                new Microsoft.Interop.Analyzers.CustomMarshallerAttributeFixer(),
                new Microsoft.Interop.Analyzers.AddGeneratedComClassFixer(),
                new Microsoft.Interop.Analyzers.AddMarshalAsToElementFixer(),
                new Microsoft.Interop.Analyzers.ConvertComImportToGeneratedComInterfaceFixer(),

                new System.Text.RegularExpressions.Generator.UpgradeToGeneratedRegexCodeFixer());


        public static ImmutableArray<ISourceGenerator> GetSourceGenerators()
            => ImmutableArray.Create<ISourceGenerator>(
                // Converts [LibraryImport] methods into Native P/Invoke implementations
                new Microsoft.Interop.LibraryImportGenerator().AsSourceGenerator(),

                // Converts [JSImport]/[JSExport] attributes for WebAssembly
                new Microsoft.Interop.JavaScript.JSImportGenerator().AsSourceGenerator(),

                // Generates COM wrappers
                new Microsoft.Interop.ComInterfaceGenerator().AsSourceGenerator(),

                // Generates System.Text.Json Contexts ([JsonSourceGenerationOptions])
                new System.Text.Json.SourceGeneration.JsonSourceGenerator().AsSourceGenerator(),

                // Generates optimized regex matchers ([GeneratedRegex])
                new System.Text.RegularExpressions.Generator.RegexGenerator().AsSourceGenerator()
                );
    }
}
