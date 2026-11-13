using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Sharp.AotAnalyzer;

namespace Sharp;

// NativeAOT cannot execute user analyzer assemblies in-process. Build each analyzer project into a
// NativeAOT shared library with a small generated shim, then run its generators through a versioned
// unmanaged ABI. No managed object, JSON document, or temporary request directory crosses the
// boundary.
public static class AotAnalyzerHost
{
    private const string PluginRuntimeVersion = "3";
    private const string EntryPoint = "sharp_aot_analyzers_run";
    private const string FreeEntryPoint = "sharp_aot_analyzers_free";
    private const string RuntimeResourceName = "Sharp.AotAnalyzer.PluginRuntime.cs";
    private const string ProtocolResourceName = "Sharp.AotAnalyzer.Protocol.cs";

    private static readonly ConcurrentDictionary<string, object> PluginLocks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, LoadedPlugin> LoadedPlugins = new(StringComparer.Ordinal);
    private static readonly Lazy<string> RuntimeSource = new(() => ReadResource(RuntimeResourceName));
    private static readonly Lazy<string> ProtocolSource = new(() => ReadResource(ProtocolResourceName));

    public static ISourceGenerator? CreateGenerator(Project project, string csproj, string dll)
    {
        if (!File.Exists(dll))
        {
            Console.WriteLine($"Warning: analyzer project output '{dll}' was not found.");
            return null;
        }

        var warnings = new List<string>();
        var generatorTypes = AotAnalyzerMetadata.ReadGeneratorTypes(dll, warnings);
        PrintWarnings(warnings);
        if (generatorTypes.Count == 0)
        {
            Console.WriteLine(
                $"Warning: analyzer project '{Path.GetFileNameWithoutExtension(csproj)}' has no public [Generator] types.");
            return null;
        }

        var pluginPath = EnsurePlugin(csproj, dll, generatorTypes);
        return new NativeGeneratorAdapter(project, Path.GetFileNameWithoutExtension(dll), pluginPath);
    }

    internal static unsafe AotAnalyzerResponse* Invoke(string pluginPath, AotAnalyzerRequest* request) =>
        LoadedPlugins.GetOrAdd(pluginPath, LoadPlugin).Run(request);

    internal static unsafe void FreeResponse(string pluginPath, AotAnalyzerResponse* response)
    {
        if (response is null) return;
        LoadedPlugins.GetOrAdd(pluginPath, LoadPlugin).Free((IntPtr)response);
    }

    internal static IEnumerable<(string Key, string Value)> BuildProperties(Project project)
    {
        yield return ("build_property.MSBuildProjectFullPath", project.CsProjFile.FilePath);
        yield return ("build_property.ProjectDir", project.ProjectRootPath.EndsWith(Path.DirectorySeparatorChar)
            ? project.ProjectRootPath
            : project.ProjectRootPath + Path.DirectorySeparatorChar);
        yield return ("build_property.RootNamespace", project.ProjectName);
        yield return ("build_property.LangVersion", project.LangVersion ?? "");
        yield return ("build_property.Nullable", project.Nullable ?? "");
        yield return ("build_property.TargetFramework", project.TargetFramework ?? "");
        yield return ("build_property.AllowUnsafeBlocks", project.AllowUnsafeBlocks ? "true" : "false");
        yield return ("build_property.EnableConfigurationBindingGenerator",
            project.EnableConfigurationBindingGenerator ? "true" : "false");
    }

    private static unsafe LoadedPlugin LoadPlugin(string path)
    {
        var handle = NativeLibrary.Load(path, typeof(AotAnalyzerHost).Assembly, null);
        if (!NativeLibrary.TryGetExport(handle, EntryPoint, out var run))
            throw new InvalidOperationException($"AOT analyzer plugin '{path}' does not export '{EntryPoint}'.");
        if (!NativeLibrary.TryGetExport(handle, FreeEntryPoint, out var free))
            throw new InvalidOperationException($"AOT analyzer plugin '{path}' does not export '{FreeEntryPoint}'.");

        return new LoadedPlugin
        {
            Handle = handle,
            Run = (delegate* unmanaged<AotAnalyzerRequest*, AotAnalyzerResponse*>)run,
            Free = (delegate* unmanaged<IntPtr, void>)free,
        };
    }

    private static string EnsurePlugin(string csproj, string dll, IEnumerable<string> generatorTypes)
    {
        var key = CacheKey(csproj, dll);
        var cacheRoot = Path.Combine(Path.GetTempPath(), "SharpAotAnalyzerPlugins", key);
        var pluginPath = Path.Combine(cacheRoot, "publish", "SharpAotAnalyzerShim.so");
        var waitTimer = Stopwatch.StartNew();
        lock (PluginLocks.GetOrAdd(cacheRoot, _ => new object()))
        {
            if (File.Exists(pluginPath))
            {
                if (Environment.GetEnvironmentVariable("SHARP_TRACE_GENERATORS") == "1")
                    Console.WriteLine(
                        $"[gens] plugin cache hit after {waitTimer.Elapsed.TotalSeconds:0.0}s: {Path.GetFileNameWithoutExtension(csproj)}");
                return pluginPath;
            }

            if (Environment.GetEnvironmentVariable("SHARP_TRACE_GENERATORS") == "1")
                Console.WriteLine(
                    $"[gens] plugin publish start after {waitTimer.Elapsed.TotalSeconds:0.0}s: {Path.GetFileNameWithoutExtension(csproj)}");
            var publishTimer = Stopwatch.StartNew();
            Directory.CreateDirectory(cacheRoot);
            var shimCsproj = Path.Combine(cacheRoot, "SharpAotAnalyzerShim.csproj");
            File.WriteAllText(Path.Combine(cacheRoot, "Protocol.cs"), ProtocolSource.Value, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(cacheRoot, "Runtime.cs"), RuntimeSource.Value, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(cacheRoot, "Generators.cs"),
                BuildGeneratorsSource(generatorTypes), new UTF8Encoding(false));
            File.WriteAllText(shimCsproj, BuildShimProject(csproj), new UTF8Encoding(false));

            var publishDirectory = Path.Combine(cacheRoot, "publish");
            var result = SharpCompiler.Publish(new PublishOptions(
                cacheRoot, "Release", "linux-x64", publishDirectory));
            if (!result.Success)
                throw new InvalidOperationException(result.Errors);

            if (!File.Exists(pluginPath))
                throw new InvalidOperationException($"The AOT analyzer plugin was not produced at '{pluginPath}'.");

            if (Environment.GetEnvironmentVariable("SHARP_TRACE_GENERATORS") == "1")
                Console.WriteLine(
                    $"[gens] plugin publish finished in {publishTimer.Elapsed.TotalSeconds:0.0}s: {Path.GetFileNameWithoutExtension(csproj)}");
            return pluginPath;
        }
    }

    private static string CacheKey(string csproj, string dll)
    {
        var payload = new StringBuilder()
            .Append(PluginRuntimeVersion).Append('\n')
            .Append(Path.GetFullPath(csproj)).Append('\n')
            .Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)))).Append('\n')
            .Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RuntimeSource.Value)))).Append('\n')
            .Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ProtocolSource.Value))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))[..24]
            .ToLowerInvariant();
    }

    private static string BuildShimProject(string analyzerCsproj) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <OutputType>Library</OutputType>
            <PublishAot>true</PublishAot>
            <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
            <NativeLib>Shared</NativeLib>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
            <AssemblyName>SharpAotAnalyzerShim</AssemblyName>
            <StackTraceSupport>false</StackTraceSupport>
            <DebugType>none</DebugType>
            <UseSystemResourceKeys>true</UseSystemResourceKeys>
            <InvariantGlobalization>true</InvariantGlobalization>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.9.0" />
            <PackageReference Include="Microsoft.CodeAnalysis.Common" Version="5.9.0" />
            <ProjectReference Include="{{XmlEscape(Path.GetFullPath(analyzerCsproj))}}" />
          </ItemGroup>
        </Project>
        """;

    private static string XmlEscape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    private static string BuildGeneratorsSource(IEnumerable<string> typeNames)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using Microsoft.CodeAnalysis;");
        builder.AppendLine("public static partial class SharpAotAnalyzerHost");
        builder.AppendLine("{");
        builder.AppendLine("    private static ISourceGenerator[] CreateGenerators()");
        builder.AppendLine("    {");
        builder.AppendLine("        var generators = new List<ISourceGenerator>();");
        foreach (var typeName in typeNames)
            builder.AppendLine($"        Add(generators, new global::{AotAnalyzerMetadata.EscapeIdentifier(typeName)}());");
        builder.AppendLine("        return generators.ToArray();");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(AotAnalyzerHost).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"The AOT analyzer resource '{name}' is missing. Sharp was built without its analyzer runtime payload.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void PrintWarnings(List<string> warnings)
    {
        foreach (var warning in warnings)
            Console.WriteLine(warning);
    }

    private sealed unsafe class LoadedPlugin
    {
        public IntPtr Handle;
        public delegate* unmanaged<AotAnalyzerRequest*, AotAnalyzerResponse*> Run;
        public delegate* unmanaged<IntPtr, void> Free;
    }
}
