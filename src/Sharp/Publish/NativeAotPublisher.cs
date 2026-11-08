using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Sharp.NuGet;

namespace Sharp;

// Direct NativeAOT publish: acquire the ILCompiler host + NativeAOT runtime pack from NuGet, run
// ilc against the managed build output, then link the object with the native runtime libraries.
// The argument set mirrors the SDK's Microsoft.NETCore.Native.targets for linux-x64.
public static class NativeAotPublisher
{
    private const string Rid = "linux-x64";

    [HasSideEffects]
    public static List<string> Publish(Project project, string publishDirectory, BuildOptions options)
    {
        if (project.ResolvedProjectOutputKind() != OutputKind.ConsoleApplication)
            throw new NotSupportedException("NativeAOT publish currently supports executable projects only.");
        if (project.GetBoolProperty("StaticICULinking") || project.GetBoolProperty("StaticOpenSslLinking"))
            throw new NotSupportedException("Static NativeAOT linking is not supported yet.");

        var version = EmbeddedSdkRefs.RuntimeVersion;
        var ensure = PackageRestorer.EnsurePackagesAsync(project.ProjectRootPath, new[]
        {
            ($"runtime.{Rid}.Microsoft.DotNet.ILCompiler", version),
            ($"Microsoft.NETCore.App.Runtime.NativeAOT.{Rid}", version),
        }).GetAwaiter().GetResult();

        if (!ensure.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ensure.Errors));

        var ilcPackage = PackageExtractor.PackageDirectory(
            ensure.GlobalPackagesFolder, $"runtime.{Rid}.Microsoft.DotNet.ILCompiler", version);
        var ilcTools = Path.Combine(ilcPackage, "tools");
        var ilc = Path.Combine(ilcTools, "ilc");
        if (!File.Exists(ilc))
            throw new InvalidOperationException($"The NativeAOT compiler was not found at '{ilc}'.");
        EnsureExecutable(ilc);

        var nativePackage = PackageExtractor.PackageDirectory(
            ensure.GlobalPackagesFolder, $"Microsoft.NETCore.App.Runtime.NativeAOT.{Rid}", version);
        var ridRoot = Path.Combine(nativePackage, "runtimes", Rid);
        var tfm = project.TargetFramework ?? DefaultTargetFramework.Value;
        var frameworkLib = ResolveFrameworkLib(ridRoot, tfm);
        var nativeDir = Path.Combine(ridRoot, "native");

        var configuration = project.Configuration;
        var intermediate = Path.Combine(
            project.ProjectRootPath, "obj", configuration, tfm, Rid, "native");
        Directory.CreateDirectory(intermediate);

        var appName = project.ProjectName;
        var nativeObject = Path.Combine(intermediate, appName + ".o");
        var nativeBinary = Path.Combine(intermediate, appName);
        var exportsFile = Path.Combine(intermediate, appName + ".exports");
        var responseFile = Path.Combine(intermediate, appName + ".ilc.rsp");

        var frameworkDlls = Directory.EnumerateFiles(frameworkLib, "*.dll")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // The NativeAOT runtime pack keeps its private implementation assemblies (CoreLib,
        // reflection/type-loader, stack-trace metadata) beside the native libraries rather than in
        // lib/<tfm>, so both locations are ILC references.
        var privateDlls = Directory.EnumerateFiles(nativeDir, "*.dll")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var frameworkNames = frameworkDlls.Concat(privateDlls)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var appReferences = Directory.EnumerateFiles(project.SharpBinFolderPath, "*.dll")
            .Where(path => !frameworkNames.Contains(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var optimize = configuration.Equals("Release", StringComparison.OrdinalIgnoreCase);
        var stackTraceSupport = !IsFalse(project.GetProperty("StackTraceSupport"));
        var serverGc = project.GetBoolProperty("ServerGarbageCollection");
        var eventSourceSupport = project.GetBoolProperty("EventSourceSupport");
        var invariantGlobalization = project.GetBoolProperty("InvariantGlobalization");
        var useSystemResourceKeys = project.GetBoolProperty("UseSystemResourceKeys");
        var useSizeOptimizedLinq = !IsFalse(project.GetProperty("UseSizeOptimizedLinq"));
        var optimizationPreference = project.GetProperty("OptimizationPreference");
        var debugType = project.GetProperty("DebugType") ?? "portable";
        var nativeDebugSymbols = !debugType.Equals("none", StringComparison.OrdinalIgnoreCase);
        var debuggerSupport = project.GetBoolProperty("DebuggerSupport");
        var stripSymbols = !IsFalse(project.GetProperty("StripSymbols"));

        var ilcArgs = new List<string>
        {
            project.DllPathName,
            $"-o:{nativeObject}",
        };

        foreach (var reference in appReferences)
            ilcArgs.Add($"-r:{reference}");
        foreach (var reference in frameworkDlls)
            ilcArgs.Add($"-r:{reference}");
        foreach (var reference in privateDlls)
            ilcArgs.Add($"-r:{reference}");

        foreach (var descriptor in project.TrimmerRootDescriptors)
        {
            var path = Path.IsPathRooted(descriptor)
                ? descriptor
                : Path.GetFullPath(Path.Combine(project.ProjectRootPath, descriptor));
            if (!File.Exists(path))
                throw new InvalidOperationException($"TrimmerRootDescriptor '{descriptor}' was not found.");
            ilcArgs.Add($"--descriptor:{path}");
        }

        ilcArgs.Add("--targetos:linux");
        ilcArgs.Add("--targetarch:x64");
        ilcArgs.Add("--dehydrate");
        ilcArgs.Add($"--exportsfile:{exportsFile}");
        ilcArgs.Add(debuggerSupport
            ? "--export-dynamic-symbol:DotNetRuntimeDebugHeader"
            : "--feature:System.Diagnostics.Debugger.IsSupported=false");

        ilcArgs.Add("--initassembly:System.Private.CoreLib");
        if (stackTraceSupport)
            ilcArgs.Add("--initassembly:System.Private.StackTraceMetadata");
        ilcArgs.Add("--initassembly:System.Private.TypeLoader");
        ilcArgs.Add("--initassembly:System.Private.Reflection.Execution");
        ilcArgs.Add("--directpinvoke:System.Native");
        if (!invariantGlobalization)
            ilcArgs.Add("--directpinvoke:System.Globalization.Native");
        ilcArgs.Add("--directpinvoke:System.IO.Compression.Native");
        ilcArgs.Add("--directpinvoke:System.Net.Security.Native");
        ilcArgs.Add("--directpinvoke:System.Security.Cryptography.Native.OpenSsl");

        var features = new List<(string Name, string Value)>
        {
            ("Microsoft.Extensions.DependencyInjection.VerifyOpenGenericServiceTrimmability", "true"),
            ("System.ComponentModel.DefaultValueAttribute.IsSupported", "false"),
            ("System.ComponentModel.Design.IDesignerHost.IsSupported", "false"),
            ("System.ComponentModel.TypeConverter.EnableUnsafeBinaryFormatterInDesigntimeLicenseContextSerialization", "false"),
            ("System.ComponentModel.TypeDescriptor.IsComObjectDescriptorSupported", "false"),
            ("System.Data.DataSet.XmlSerializationIsSupported", "false"),
            ("System.Diagnostics.Tracing.EventSource.IsSupported", eventSourceSupport ? "true" : "false"),
            ("System.Linq.Enumerable.IsSizeOptimized", useSizeOptimizedLinq ? "true" : "false"),
            ("System.Net.Security.UseManagedNtlm", "false"),
            ("System.Net.SocketsHttpHandler.Http3Support", "false"),
            ("System.Reflection.Metadata.MetadataUpdater.IsSupported", "false"),
            ("System.Resources.ResourceManager.AllowCustomResourceTypes", "false"),
            ("System.Resources.UseSystemResourceKeys", useSystemResourceKeys ? "true" : "false"),
            ("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", "false"),
            ("System.Runtime.InteropServices.BuiltInComInterop.IsSupported", "false"),
            ("System.Runtime.InteropServices.EnableConsumingManagedCodeFromNativeHosting", "false"),
            ("System.Runtime.InteropServices.EnableCppCLIHostActivation", "false"),
            ("System.Runtime.InteropServices.Marshalling.EnableGeneratedComInterfaceComImportInterop", "false"),
            ("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization", "false"),
            ("System.StartupHookProvider.IsSupported", "false"),
            ("System.Text.Encoding.EnableUnsafeUTF7Encoding", "false"),
            ("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", "false"),
            ("System.Threading.Thread.EnableAutoreleasePool", "false"),
            ("System.Linq.Expressions.CanEmitObjectArrayDelegate", "false"),
        };

        if (invariantGlobalization)
            features.Add(("System.Globalization.Invariant", "true"));

        foreach (var (name, value) in features)
        {
            ilcArgs.Add($"--feature:{name}={value}");
            ilcArgs.Add($"--runtimeknob:{name}={value}");
        }

        ilcArgs.Add($"--runtimeknob:RUNTIME_IDENTIFIER={Rid}");
        if (serverGc)
            ilcArgs.Add("--runtimeopt:gcServer=1");
        if (stackTraceSupport)
            ilcArgs.Add("--stacktracedata");
        ilcArgs.Add("--scanreflection");

        var methodBodyFolding = !optimize
            ? "none"
            : stackTraceSupport ? "generic" : "all";
        ilcArgs.Add($"--methodbodyfolding:{methodBodyFolding}");

        if (optimize)
            ilcArgs.Add("-O");
        if (optimizationPreference?.Equals("Size", StringComparison.OrdinalIgnoreCase) == true)
            ilcArgs.Add("--Os");
        else if (optimizationPreference?.Equals("Speed", StringComparison.OrdinalIgnoreCase) == true)
            ilcArgs.Add("--Ot");
        if (nativeDebugSymbols)
            ilcArgs.Add("-g");

        ilcArgs.Add("--singlewarn");
        ilcArgs.Add("--resilient");
        ilcArgs.Add("--generateunmanagedentrypoints:System.Private.CoreLib,HIDDEN");
        ilcArgs.AddRange(project.IlcArgs);

        File.WriteAllLines(responseFile, ilcArgs);
        RunTool(ilc, new[] { "@" + responseFile }, ilcTools, "NativeAOT compilation");

        if (!File.Exists(nativeObject))
            throw new InvalidOperationException(
                $"NativeAOT compilation did not produce '{nativeObject}'.");

        var useSystemZlib = project.GetBoolProperty("UseSystemZlib") || !File.Exists(NativeFile("libz.a"));
        var useSystemBrotli = project.GetBoolProperty("UseSystemBrotli")
                              || !File.Exists(NativeFile("libbrotlicommon.a"));
        var vxSort = optimizationPreference?.Equals("Speed", StringComparison.OrdinalIgnoreCase) == true
            ? "libRuntime.VxsortEnabled.a"
            : "libRuntime.VxsortDisabled.a";

        var nativeLibraries = new List<string>
        {
            NativeFile("libSystem.Native.a"),
        };
        if (!invariantGlobalization)
            nativeLibraries.Add(NativeFile("libSystem.Globalization.Native.a"));
        nativeLibraries.Add(NativeFile("libSystem.IO.Compression.Native.a"));
        nativeLibraries.Add(NativeFile("libSystem.Net.Security.Native.a"));
        nativeLibraries.Add(NativeFile("libSystem.Security.Cryptography.Native.OpenSsl.a"));
        nativeLibraries.Add(NativeFile("libbootstrapper.o"));
        nativeLibraries.Add(NativeFile(serverGc ? "libRuntime.ServerGC.a" : "libRuntime.WorkstationGC.a"));
        nativeLibraries.Add(NativeFile(eventSourceSupport ? "libeventpipe-enabled.a" : "libeventpipe-disabled.a"));
        nativeLibraries.Add(NativeFile(vxSort));
        nativeLibraries.Add(NativeFile("libstandalonegc-disabled.a"));
        nativeLibraries.Add(NativeFile("libaotminipal.a"));
        nativeLibraries.Add(NativeFile("libstdc++compat.a"));
        if (!useSystemZlib)
            nativeLibraries.Add(NativeFile("libz.a"));
        if (!useSystemBrotli)
        {
            nativeLibraries.Add(NativeFile("libbrotlienc.a"));
            nativeLibraries.Add(NativeFile("libbrotlidec.a"));
            nativeLibraries.Add(NativeFile("libbrotlicommon.a"));
        }

        var linkArgs = new List<string>
        {
            nativeObject,
            "-o",
            nativeBinary,
        };

        if (File.Exists(exportsFile))
        {
            linkArgs.Add($"-Wl,--version-script={exportsFile}");
            linkArgs.Add("-Wl,--export-dynamic");
        }

        linkArgs.AddRange(nativeLibraries);
        linkArgs.Add("-fuse-ld=bfd");
        linkArgs.Add("-gz=zlib");
        linkArgs.Add(nativeDebugSymbols ? "-g" : "-Wl,--strip-debug");
        linkArgs.Add("-Wl,--build-id=sha1");
        linkArgs.Add("-Wl,--as-needed");
        linkArgs.Add("-pthread");
        linkArgs.Add("-lrt");
        linkArgs.Add("-ldl");
        linkArgs.Add("-lm");
        if (useSystemZlib)
            linkArgs.Add("-lz");
        if (useSystemBrotli)
        {
            linkArgs.Add("-lbrotlienc");
            linkArgs.Add("-lbrotlidec");
            linkArgs.Add("-lbrotlicommon");
        }
        linkArgs.Add("-pie");
        linkArgs.Add("-Wl,-pie");
        linkArgs.Add("-Wl,-z,relro");
        linkArgs.Add("-Wl,-z,now");
        linkArgs.Add("-Wl,--eh-frame-hdr");
        linkArgs.Add("-Wl,--discard-all");
        linkArgs.Add("-Wl,--gc-sections");

        RunTool("clang", linkArgs, intermediate, "NativeAOT link");
        if (!File.Exists(nativeBinary))
            throw new InvalidOperationException($"NativeAOT link did not produce '{nativeBinary}'.");

        if (stripSymbols)
        {
            var objcopy = FindTool("llvm-objcopy", "objcopy");
            var debugFile = nativeBinary + ".dbg";
            if (nativeDebugSymbols)
                RunTool(objcopy, new[] { "--only-keep-debug", nativeBinary, debugFile }, intermediate, "NativeAOT debug split");
            RunTool(objcopy, new[] { "--strip-debug", "--strip-unneeded", nativeBinary }, intermediate, "NativeAOT strip");
            if (nativeDebugSymbols)
                RunTool(objcopy, new[] { $"--add-gnu-debuglink={debugFile}", nativeBinary }, intermediate, "NativeAOT debug link");
        }

        var destination = Path.GetFullPath(publishDirectory);
        Directory.CreateDirectory(destination);

        var outputBinary = Path.Combine(destination, appName);
        File.Copy(nativeBinary, outputBinary, overwrite: true);
        EnsureExecutable(outputBinary);

        var outputs = new List<string> { outputBinary };
        var debugOutput = nativeBinary + ".dbg";
        if (nativeDebugSymbols && File.Exists(debugOutput))
        {
            var destinationDebug = outputBinary + ".dbg";
            File.Copy(debugOutput, destinationDebug, overwrite: true);
            outputs.Add(destinationDebug);
        }

        outputs.AddRange(CopyContentFiles(project, destination));
        return outputs;

        string NativeFile(string name)
        {
            var path = Path.Combine(nativeDir, name);
            if (!File.Exists(path))
                throw new InvalidOperationException($"NativeAOT runtime file '{name}' was not found in '{nativeDir}'.");
            return path;
        }
    }

    private static string ResolveFrameworkLib(string ridRoot, string tfm)
    {
        var libRoot = Path.Combine(ridRoot, "lib");
        var exact = Path.Combine(libRoot, tfm.Split('-')[0]);
        if (Directory.Exists(exact)) return exact;

        return Directory.EnumerateDirectories(libRoot)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static IEnumerable<string> CopyContentFiles(Project project, string destination)
    {
        var source = project.SharpBinFolderPath;
        var appName = project.ProjectName;
        var outputs = new List<string>();

        var destinationPrefix = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var fullFile = Path.GetFullPath(file);
            var relative = Path.GetRelativePath(source, file);
            var fileName = Path.GetFileName(file);

            // The default publish directory lives under the build output, and a previous publish
            // leaves <rid>/publish (binary, .dbg) there. Never feed those back into the content set:
            // doing so recursively copies "linux-x64/publish/linux-x64/publish/...".
            if (fullFile.StartsWith(destinationPrefix, StringComparison.Ordinal)
                || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment.Equals("publish", StringComparison.OrdinalIgnoreCase)))
                continue;

            if (fileName.Equals(appName, StringComparison.Ordinal)
                || fileName.Equals(appName + ".exe", StringComparison.Ordinal)
                || fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".dbg", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            outputs.Add(target);
        }

        return outputs;
    }

    private static bool IsFalse(string? value) =>
        value?.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) ?? false;

    private static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;

        var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path,
            mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private static string FindTool(params string[] names)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new InvalidOperationException($"Required tool '{string.Join("' or '", names)}' was not found in PATH.");
    }

    private static void RunTool(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string description)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {description}.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);

        if (process.ExitCode == 0) return;

        throw new InvalidOperationException(
            $"{description} failed with exit code {process.ExitCode}." +
            Environment.NewLine + stdout.Result + Environment.NewLine + stderr.Result);
    }
}
