using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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


        // Returns the generator set for EVERY project. Generators that the SDK applies conditionally
    // (minimal-API public Program, RequestDelegateGenerator) therefore cannot be listed here without
    // diverging from `dotnet build`; this takes no project context, so it can only hold generators
    // that are no-ops when their attributes are absent.
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
                new System.Text.RegularExpressions.Generator.RegexGenerator().AsSourceGenerator(),

                // --- ASP.NET Core targeting-pack generators ---
                // Linked from the embedded payload like the six above. The ones registered here are
                // attribute-driven, so they no-op on a project that uses none of their attributes and
                // leave output identical to the SDK.

                // Implements [LoggerMessage] partial methods. Missing this, every one of them is
                // CS8795 "method must have a body because it is partially defined".
                new Microsoft.Extensions.Logging.Generators.LoggerMessageGenerator().AsSourceGenerator(),

                new Microsoft.Extensions.Options.Generators.OptionsValidatorGenerator().AsSourceGenerator(),
                new Microsoft.Extensions.Validation.ValidationsGenerator().AsSourceGenerator()

                );

        // The Configuration Binding Generator is off-by-default in the SDK (enabled only for
        // PublishTrimmed/PublishAot or an explicit <EnableConfigurationBindingGenerator>). Unlike
        // the attribute-driven generators above it is interceptor-based: it emits a
        // Configure<TOptions> interceptor for every `services.Configure<T>(config)` call site, so
        // running it unconditionally diverges from `dotnet build` and can collide with a project's
        // own options-extension type (a CS0433 duplicate). Kept out of GetSourceGenerators(); the
        // project-aware gate in SourceGeneratorRunner adds it back only when the SDK would.
        public static ISourceGenerator GetConfigurationBindingGenerator()
            => new Microsoft.Extensions.Configuration.Binder.SourceGeneration.ConfigurationBindingGenerator().AsSourceGenerator();

        // Generators the SDK applies only to web projects. Kept out of GetSourceGenerators() because
        // that set is unconditional, and running these for every project changes console output:
        // PublicProgramSourceGenerator makes `Program` public in a plain console app, which
        // OutputParityTests reports as a public-surface difference.
        //
        // Under JIT these generators are picked up regardless, by the runtime discovery in
        // SourceGeneratorRunner. These entries exist so the NativeAOT binary - which can neither
        // discover nor load assemblies - produces the same output as the SDK.
        public static ImmutableArray<ISourceGenerator> GetWebSourceGenerators()
            => ImmutableArray.Create<ISourceGenerator>(
                // Emits the `public partial class Program` the SDK gives top-level-statement
                // minimal-API apps. Without it a sharp-built web app exposes an internal Program, so
                // anything referencing the entry point (WebApplicationFactory<Program>, and most
                // integration tests) fails to compile against the produced assembly.
                new Microsoft.AspNetCore.SourceGenerators.PublicProgramSourceGenerator().AsSourceGenerator()
                );

        // RequestDelegateGenerator is deliberately absent from both sets.
        //
        // The SDK keeps it OFF unless a project opts in with <EnableRequestDelegateGenerator>true</...>,
        // so not registering it is correct for every default project - verified by building a
        // net10.0 Microsoft.NET.Sdk.Web project with the SDK, which emits no RDG output. Registering it
        // unconditionally instead made sharp emit route-handler glue the SDK does not, adding twelve
        // assemblies to the produced app's reference set.
        //
        // Supporting the opt-in path needs two things that do not exist yet: the
        // `build_property.EnableRequestDelegateGenerator` key in ProjectAnalyzerConfigOptions
        // (SourceGeneratorRunner), which RDG reads to decide whether to generate at all, and
        // EnableRequestDelegateGenerator on the project model. Until both land, a project that opts
        // in silently gets no RDG output under AOT while JIT still produces it via discovery.
}
}
