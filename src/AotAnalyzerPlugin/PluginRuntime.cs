using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Sharp.AotAnalyzer;

#pragma warning disable RS1042

public static partial class SharpAotAnalyzerHost
{
    [UnmanagedCallersOnly(EntryPoint = "sharp_aot_analyzers_run")]
    public static unsafe IntPtr Run(AotAnalyzerRequest* request)
    {
        try
        {
            if (request is null)
                return Error("AOT analyzer request pointer was null.");
            if (request->AbiVersion != AotAnalyzerProtocol.AbiVersion)
                return Error($"AOT analyzer ABI mismatch: host {request->AbiVersion}, plugin {AotAnalyzerProtocol.AbiVersion}.");

            return (IntPtr)Execute(request);
        }
        catch (Exception ex)
        {
            return Error(ex.ToString());
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "sharp_aot_analyzers_free")]
    public static void Free(IntPtr response) => AotAnalyzerProtocol.Free(response);

    private static unsafe IntPtr Error(string message)
    {
        try
        {
            return (IntPtr)AotAnalyzerProtocol.AllocateResponse(
                new AotAnalyzerResponseData { Error = message });
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static unsafe AotAnalyzerResponse* Execute(AotAnalyzerRequest* rawRequest)
    {
        var request = AotAnalyzerProtocol.ReadRequest(rawRequest);
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(ParseLanguage(request.Language))
            .WithPreprocessorSymbols(request.Defines);
        parseOptions = ApplyInterceptors(parseOptions, request.Interceptors);

        var trees = new List<SyntaxTree>();
        foreach (var source in request.Sources)
        {
            if (source.Text is not null)
            {
                trees.Add(CSharpSyntaxTree.ParseText(
                    SourceText.From(source.Text, Encoding.UTF8), parseOptions, source.Path));
            }
            else if (File.Exists(source.Path))
            {
                var text = File.ReadAllText(source.Path);
                trees.Add(CSharpSyntaxTree.ParseText(
                    SourceText.From(text, Encoding.UTF8), parseOptions, source.Path));
            }
        }

        var references = new List<MetadataReference>();
        foreach (var path in request.References)
        {
            if (!File.Exists(path)) continue;
            try
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch
            {
            }
        }

        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: request.Nullable
                ? NullableContextOptions.Enable
                : NullableContextOptions.Disable,
            allowUnsafe: request.AllowUnsafe);
        var compilation = CSharpCompilation.Create(
            "SharpAotAnalyzerInput", trees, references, compilationOptions);

        var additionalTexts = ImmutableArray.CreateBuilder<AdditionalText>();
        foreach (var file in request.AdditionalFiles)
        {
            if (file.Text is not null)
                additionalTexts.Add(new InlineAdditionalText(file.Path, file.Text));
            else if (File.Exists(file.Path))
                additionalTexts.Add(new FileAdditionalText(file.Path));
        }

        var generators = CreateGenerators();
        if (generators.Length == 0)
            return AotAnalyzerProtocol.AllocateResponse(new AotAnalyzerResponseData());

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in request.Options)
            options[option.Key] = option.Value;

        var driver = CSharpGeneratorDriver.Create(
            generators,
            additionalTexts: additionalTexts.ToImmutable(),
            parseOptions: parseOptions,
            optionsProvider: new RequestOptionsProvider(options));
        var result = driver.RunGenerators(compilation).GetRunResult();

        var response = new AotAnalyzerResponseData();
        foreach (var generatorResult in result.Results)
        {
            foreach (var generated in generatorResult.GeneratedSources)
            {
                response.Generated.Add(new AotGeneratedSourceData
                {
                    HintName = generated.HintName,
                    Text = generated.SourceText.ToString(),
                });
            }
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            var mapped = diagnostic.Location.GetMappedLineSpan();
            var hasLocation = diagnostic.Location.IsInSource && !string.IsNullOrEmpty(mapped.Path);
            response.Diagnostics.Add(new AotDiagnosticData
            {
                Id = diagnostic.Id,
                Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
                File = hasLocation ? mapped.Path : "",
                Severity = ToAbiSeverity(diagnostic.Severity),
                Line = hasLocation ? mapped.StartLinePosition.Line : 0,
                Column = hasLocation ? mapped.StartLinePosition.Character : 0,
                EndLine = hasLocation ? mapped.EndLinePosition.Line : 0,
                EndColumn = hasLocation ? mapped.EndLinePosition.Character : 0,
            });
        }

        return AotAnalyzerProtocol.AllocateResponse(response);
    }

    private static int ToAbiSeverity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => 3,
        DiagnosticSeverity.Warning => 2,
        DiagnosticSeverity.Info => 1,
        _ => 0,
    };

    private static void Add(List<ISourceGenerator> generators, object instance)
    {
        if (instance is ISourceGenerator sourceGenerator)
        {
            generators.Add(sourceGenerator);
        }
        else if (instance is IIncrementalGenerator incrementalGenerator)
        {
            generators.Add(incrementalGenerator.AsSourceGenerator());
        }
    }

    private static LanguageVersion ParseLanguage(string value) =>
        LanguageVersionFacts.TryParse(value, out var version) ? version : LanguageVersion.Latest;

    private static CSharpParseOptions ApplyInterceptors(CSharpParseOptions parseOptions, string namespaces)
    {
        if (string.IsNullOrWhiteSpace(namespaces)) return parseOptions;

        var parts = namespaces
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (parts.Length == 0) return parseOptions;

        var field = typeof(CSharpParseOptions)
            .GetField("_interceptorsNamespaces", BindingFlags.NonPublic | BindingFlags.Instance);
        var segments = parts
            .Select(part => part.Split('.', StringSplitOptions.TrimEntries).ToImmutableArray())
            .ToImmutableArray();
        field?.SetValue(parseOptions, segments);
        return parseOptions;
    }

    private sealed class InlineAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InlineAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }

    private sealed class FileAdditionalText : AdditionalText
    {
        private readonly string _path;
        private SourceText? _text;

        public FileAdditionalText(string path) => _path = path;

        public override string Path => _path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            _text ??= SourceText.From(File.ReadAllText(_path), Encoding.UTF8);
    }

    private sealed class RequestOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly RequestOptions _options;

        public RequestOptionsProvider(Dictionary<string, string> options) =>
            _options = new RequestOptions(options);

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree? tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class RequestOptions : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> _values;

        public RequestOptions(Dictionary<string, string> values) => _values = values;

        public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) =>
            _values.TryGetValue(key, out value);
    }
}
