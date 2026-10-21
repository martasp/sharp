using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using static NativeCompiler.NativeCompilerUtils;

namespace NativeCompiler;

public static class Output
{
    [HasSideEffects]
    public static (bool success, string errors) WriteDll(CSharpCompilation compilation, Project project)
    {
        try
        {
            //EmitOptions must use PortablePdb: the string Emit overload forces the native Windows PDB
            //format which relies on COM (DiaSymReader) and is not supported under NativeAOT.
            //highEntropyVirtualAddressSpace: true mirrors the SDK's AnyCPU PE characteristics
            //(0x8560, HIGH_ENTROPY_VA bit set), matching `dotnet build` output. Deterministic
            //emission is enabled on the compilation options (this Roslyn fork keeps the flag there).
            var emitOptions = new EmitOptions(
                debugInformationFormat: DebugInformationFormat.PortablePdb,
                pdbFilePath: project.PDBPathName,
                highEntropyVirtualAddressSpace: true);

            // Embedded resources (e.g. OrchardCore module assets) are embedded verbatim under the
            // name a build target computed, mirroring `dotnet build`'s manifest resources.
            var manifestResources = AssemblyAttributeEvaluator.GetEmbeddedResources(project)
                .Select(resource => new ResourceDescription(
                    resource.Name,
                    () => File.OpenRead(resource.FilePath),
                    isPublic: true))
                .ToList();

            (bool success, string errors) result;
            using (var dllStream = File.Create(project.DllPathName))
            using (var pdbStream = File.Create(project.PDBPathName))
            {
                var emitResult = compilation.Emit(dllStream, pdbStream,
                    manifestResources: manifestResources, options: emitOptions);
                result = (emitResult.Success, emitResult.PrintErrors());
            }

            if (!result.success)
            {
                File.Delete(project.DllPathName);
                File.Delete(project.PDBPathName);
                return result;
            }

            WriteRuntimeConfig(project);
            if (project.UseAppHost && project.ResolvedProjectOutputKind() == OutputKind.ConsoleApplication)
            {
                WriteAppHost(project);
            }

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.ToString());
        }
    }

    [HasSideEffects]
    public static (bool success, string errors) RunAssembly(CSharpCompilation compilation, Project project,
        List<EmbeddedText> embeddedTexts)
    {
        // Emit the byte result of the compilation
        var assembly = compilation.CreateAssembly(project.ProjectName, embeddedTexts); // Do this without reflection

        var programType = assembly.GetType("Program") ?? throw new Exception("assembly Prgoram do not have main");
        var sourceInstance = Activator.CreateInstance(programType);

        // Get the static Main() method info from the type
        var method = programType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
            .FirstOrDefault();

        Debug.WriteLine(Directory.GetCurrentDirectory());
        var originalDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(project.SharpBinFolderPath);
        Debug.WriteLine(Directory.GetCurrentDirectory());
        try
        {
            var builder = new StringBuilder();
            TextWriter writer = new StringWriter(builder);
            Console.SetOut(writer);
            Console.WriteLine($"Running {project.ProjectName} with args {project.ProjectRootPath}");
            method?.Invoke(sourceInstance, new object[] { new[] { project.ProjectRootPath } });
            var result = builder.ToString();
            return (true, result); // Todo needs logging better
        }
        catch (Exception ex)
        {
            return (false,
                ex?.InnerException?.ToString() ?? "No errors"); // Todo : Implement better global error handling
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }
}
