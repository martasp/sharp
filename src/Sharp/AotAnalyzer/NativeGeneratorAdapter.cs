using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Sharp.AotAnalyzer;

namespace Sharp;

// Sharp is the generator host, so it is allowed to implement the Roslyn host-side interfaces even
// though the meta-analyzer marks them as internal-implementation-only for analyzer packages.
#pragma warning disable RS1042
internal sealed unsafe class NativeGeneratorAdapter : ISourceGenerator
#pragma warning restore RS1042
{
    private readonly Project _project;
    private readonly string _analyzerName;
    private readonly string _pluginPath;

    public NativeGeneratorAdapter(Project project, string analyzerName, string pluginPath)
    {
        _project = project;
        _analyzerName = analyzerName;
        _pluginPath = pluginPath;
    }

    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        AotAnalyzerRequest* request = null;
        AotAnalyzerResponse* response = null;
        try
        {
            request = AotAnalyzerProtocol.AllocateRequest(CreateRequestData(context));
            response = AotAnalyzerHost.Invoke(_pluginPath, request);
            if (response is null)
            {
                context.ReportDiagnostic(Fatal("SGAOT001",
                    $"AOT analyzer plugin '{_analyzerName}' returned no response."));
                return;
            }

            var result = AotAnalyzerProtocol.ReadResponse(response);
            if (!string.IsNullOrEmpty(result.Error))
            {
                context.ReportDiagnostic(Fatal("SGAOT001",
                    $"AOT analyzer plugin '{_analyzerName}' failed: {result.Error}"));
                return;
            }

            foreach (var generated in result.Generated)
            {
                var hintName = SanitizeHintName(_analyzerName + "_" + generated.HintName);
                context.AddSource(hintName, SourceText.From(generated.Text, Encoding.UTF8));
            }

            foreach (var diagnostic in result.Diagnostics)
                context.ReportDiagnostic(ToDiagnostic(diagnostic));
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Fatal("SGAOT002",
                $"AOT analyzer plugin '{_analyzerName}' failed: {ex}"));
        }
        finally
        {
            if (response is not null)
                AotAnalyzerHost.FreeResponse(_pluginPath, response);
            if (request is not null)
                AotAnalyzerProtocol.Free((IntPtr)request);
        }
    }

    private AotAnalyzerRequestData CreateRequestData(GeneratorExecutionContext context)
    {
        var parseOptions = SourceGeneratorRunner.GetParseOptions(_project);
        var request = new AotAnalyzerRequestData
        {
            Language = parseOptions.LanguageVersion.ToString(),
            Interceptors = _project.InterceptorsNamespaces ?? "",
            Nullable = _project.Nullable?.Contains("enable", StringComparison.OrdinalIgnoreCase) == true,
            AllowUnsafe = _project.AllowUnsafeBlocks,
        };

        foreach (var symbol in parseOptions.PreprocessorSymbolNames)
            request.Defines.Add(symbol);

        var sourceIndex = 0;
        foreach (var tree in context.Compilation.SyntaxTrees)
        {
            var path = tree.FilePath;
            if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path))
                path = Path.GetFullPath(Path.Combine(_project.ProjectRootPath, path));
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                request.Sources.Add(new AotSourceData(path, null));
            }
            else
            {
                var syntheticPath = string.IsNullOrEmpty(path)
                    ? Path.Combine("aot", "source_" + sourceIndex.ToString("D4") + ".cs")
                    : path;
                request.Sources.Add(new AotSourceData(syntheticPath, tree.GetText().ToString()));
                sourceIndex++;
            }
        }

        var additionalIndex = 0;
        foreach (var item in context.AdditionalFiles)
        {
            var text = item.GetText();
            if (text is null) continue;

            var path = item.Path;
            if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path))
                path = Path.GetFullPath(Path.Combine(_project.ProjectRootPath, path));
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                request.AdditionalFiles.Add(new AotSourceData(path, null));
            }
            else
            {
                var syntheticPath = string.IsNullOrEmpty(path)
                    ? Path.Combine("aot", "additional_" + additionalIndex.ToString("D4") + ".txt")
                    : path;
                request.AdditionalFiles.Add(new AotSourceData(syntheticPath, text.ToString()));
                additionalIndex++;
            }
        }

        foreach (var reference in context.Compilation.References)
        {
            if (reference is PortableExecutableReference { FilePath: { } path } && File.Exists(path))
                request.References.Add(path);
        }

        foreach (var (key, value) in AotAnalyzerHost.BuildProperties(_project))
            request.Options.Add(new AotOptionData(key, value));

        return request;
    }

    private static Diagnostic ToDiagnostic(AotDiagnosticData diagnostic)
    {
        var id = string.IsNullOrEmpty(diagnostic.Id) ? "SGAOT000" : diagnostic.Id;
        var severity = diagnostic.Severity switch
        {
            3 => DiagnosticSeverity.Error,
            2 => DiagnosticSeverity.Warning,
            1 => DiagnosticSeverity.Info,
            _ => DiagnosticSeverity.Hidden,
        };
        var descriptor = new DiagnosticDescriptor(
            id, id, "{0}", "Sharp.AotAnalyzer", severity, isEnabledByDefault: true);

        Location location = Location.None;
        if (!string.IsNullOrEmpty(diagnostic.File))
        {
            var start = new LinePosition(Math.Max(0, diagnostic.Line), Math.Max(0, diagnostic.Column));
            var end = new LinePosition(Math.Max(0, diagnostic.EndLine), Math.Max(0, diagnostic.EndColumn));
            if (end.Line < start.Line || (end.Line == start.Line && end.Character < start.Character))
                end = start;
            location = Location.Create(diagnostic.File, new TextSpan(0, 0), new LinePositionSpan(start, end));
        }

        return Diagnostic.Create(descriptor, location, diagnostic.Message);
    }

    private static Diagnostic Fatal(string id, string message) =>
        Diagnostic.Create(
            new DiagnosticDescriptor(id, id, "{0}", "Sharp.AotAnalyzer", DiagnosticSeverity.Error, true),
            Location.None, message);

    private static string SanitizeHintName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '_');
        }

        return builder.ToString();
    }
}
