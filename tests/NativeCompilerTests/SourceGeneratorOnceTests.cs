using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Shouldly;
using Xunit;

namespace NativeCompilerTests;

// A generator that runs twice is not a harmless duplicate. The ASP.NET Core targeting-pack
// generators are linked in statically (so the NativeAOT binary, which cannot load assemblies at
// runtime, matches the SDK) *and* found by the JIT discovery path from the same embedded payload.
// Before the discovery path learned to skip statically linked analyzers, [LoggerMessage] ran twice
// under JIT: two `__InformationCallback` declarations, and the build failed with CS0757 ("a partial
// method may not have multiple implementing declarations"), CS0102, CS0229 and CS0121.
//
// Nothing in the suite caught this because no fixture used [LoggerMessage] - the AOT build ran the
// generator once and passed, so only the JIT path broke, on real user code.
public class SourceGeneratorOnceTests
{
    [Fact]
    public void LoggerMessage_Generator_Runs_Exactly_Once_Under_Jit()
    {
        var root = TempRoot("opencode-generator-once");

        try
        {
            // Web SDK, so Microsoft.Extensions.Logging.Abstractions comes from the shared framework
            // and the fixture needs no package restore. LoggerMessageGenerator is the generator with
            // the loudest failure mode when duplicated, which is what makes it a good probe.
            File.WriteAllText(Path.Combine(root, "gen.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(root, "Program.cs"), """
                using Microsoft.Extensions.Logging;

                var builder = WebApplication.CreateBuilder();
                builder.Logging.AddProvider(NullLoggerProvider.Instance);
                var app = builder.Build();
                app.MapGet("/", (ILogger<Program> logger) => Log.Greet(logger, "world"));
                app.Run();

                static partial class Log
                {
                    [LoggerMessage(Level = LogLevel.Information, Message = "Hello {Name}")]
                    public static partial void Greet(ILogger logger, string name);
                }

                sealed class NullLoggerProvider : ILoggerProvider
                {
                    public static readonly NullLoggerProvider Instance = new();
                    public ILogger CreateLogger(string categoryName) => new NullLogger();
                    public void Dispose() { }

                    sealed class NullLogger : ILogger
                    {
                        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                        public bool IsEnabled(LogLevel logLevel) => false;
                        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
                    }
                }
                """);

            // In-process, i.e. the JIT pipeline where the two sources of generators overlap. The
            // published AOT binary takes a different path entirely and would not exercise this.
            var build = NativeCompiler.NativeCompiler.Build(root);

            build.Success.ShouldBeTrue($"build failed: {string.Join("; ", build.Errors)}");

            // Skip the reference assembly under myc/Debug/<tfm>/ref - it keeps only the public
            // surface, so the generated private callback is not in it.
            var dll = Directory
                .EnumerateFiles(root, "gen.dll", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains(Path.Combine("myc", "Debug"), StringComparison.Ordinal)
                                     && !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

            dll.ShouldNotBeNull("expected the build output under myc/Debug");

            // The generator emits one private static field `__<MethodName>Callback` per
            // [LoggerMessage] method, so a second one means it ran twice over the same input.
            CountFieldsContaining(dll!, "__GreetCallback")
                .ShouldBe(1, "the generator emitted its callback more than once, so it ran twice");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    // Counts field definitions whose name contains the given text, straight from metadata.
    private static int CountFieldsContaining(string dllPath, string nameFragment)
    {
        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var count = 0;
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            foreach (var fieldHandle in reader.GetTypeDefinition(typeHandle).GetFields())
            {
                if (reader.GetString(reader.GetFieldDefinition(fieldHandle).Name).Contains(nameFragment, StringComparison.Ordinal))
                    count++;
            }
        }

        return count;
    }

    private static string TempRoot(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }
}