using Shouldly;
using Sharp;
using System;
using System;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VerifyTests;
using VerifyXunit;
using Xunit;

namespace Tests;


[CollectionDefinition("Sequential Tests 3", DisableParallelization = true)]
[Collection("Sequential Tests 3")]
public class DllSnapshotTests
{
    private static readonly string MSCompiler = TestPaths.RootProjects("MSCompiler");
    private static readonly string MyCompiler = TestPaths.RootProjects("MyCompiler");

    [Fact]
    public async Task TypeDefinitionUsage()
    {
        var msCompilerDll = Path.Combine(MSCompiler, "myc", "Debug", "net11.0", "MSCompiler.dll");
        // The golden MSCompiler.dll used to be a checked-out artifact (Windows machine). Make the
        // test self-contained: build it on first run so a fresh checkout has a reference to compare.
        if (!File.Exists(msCompilerDll))
        {
            var msBuild = SharpCompiler.Build(MSCompiler);
            Assert.True(msBuild.Success, $"Building MSCompiler failed: {msBuild.Errors}");
        }

        var MsCompilerDllMetadata = ExtractAll(msCompilerDll);

        var buildDetails = SharpCompiler.Build(MyCompiler);
        Assert.True(buildDetails.Success, $"Build failed with errors: {buildDetails.Errors}");

        var MyCompilerDllMetadata = ExtractAll(Path.Combine(MyCompiler, "myc", "Debug", "net11.0", "MSCompiler.dll")); // MyCompiler produces an MSCompiler.exe

        var MsCompilerDllMetadataJson = JsonSerializer.Serialize(MsCompilerDllMetadata, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var MyCompilerDllMetadataJson = JsonSerializer.Serialize(MyCompilerDllMetadata, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        // trim from both AssemblyFileVersionAttribute both. 
        MsCompilerDllMetadataJson = TrimVersionAttributes(MsCompilerDllMetadataJson);
        MyCompilerDllMetadataJson = TrimVersionAttributes(MyCompilerDllMetadataJson);

        var both =
            $"""
            ------------------- MSCompiler Output -------------------
            {MsCompilerDllMetadataJson}


            ------------------- MyCompiler Output -------------------
            {MyCompilerDllMetadataJson}
            """;

        Assert.Equal(MsCompilerDllMetadataJson, MyCompilerDllMetadataJson);
        await Verifier.Verify(both);
    }

    private static string TrimVersionAttributes(string json) =>
        string.Join('\n', json.Split('\n').Where(line =>
            !line.Contains("AssemblyFileVersionAttribute") &&
            !line.Contains("AssemblyInformationalVersionAttribute")));

    public static FullDllMetadata ExtractAll(string dllPath)
    {
        var context = new AssemblyLoadContext("OneTimeContext" + dllPath, isCollectible: true);

        var assembly = context.LoadFromAssemblyPath(dllPath);
        var name = assembly.GetName();
        var metadata = new FullDllMetadata
        {
            AssemblyName = name.Name,
            Version = name.Version.ToString(),
            RuntimeVersion = assembly.ImageRuntimeVersion,
            Architecture = name.ProcessorArchitecture.ToString(),
            EntryPoint = assembly.EntryPoint?.Name ?? "None",
            Resources = new List<string>(assembly.GetManifestResourceNames()),
            AssemblyMetadata = new SortedDictionary<string, string>(),
            DllMetadata = ExtractDllMetadata(assembly)
        };

        // Assembly metadata attributes
        foreach (var attr in assembly.GetCustomAttributesData())
        {
            metadata.AssemblyMetadata[attr.AttributeType.Name] = attr.ToString();
        }

        // All types and their members
        foreach (var type in assembly.GetTypes())
        {
            metadata.Types.Add(new TypeInfo(type));
        }

        // PE headers (simplified; for full details use dnlib or PE parsing)
        using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);
        metadata.PEHeaders["Magic"] = br.ReadUInt16();
        metadata.PEHeaders["Machine"] = br.ReadUInt16();
        metadata.PEHeaders["NumberOfSections"] = br.ReadUInt16();
        metadata.PEHeaders["TimeDateStamp"] = br.ReadUInt32();
        metadata.PEHeaders["PointerToSymbolTable"] = br.ReadUInt32();
        metadata.PEHeaders["NumberOfSymbols"] = br.ReadUInt32();
        metadata.PEHeaders["SizeOfOptionalHeader"] = br.ReadUInt16();
        metadata.PEHeaders["Characteristics"] = br.ReadUInt16();

        metadata.Types = metadata.Types.OrderByDescending(type => type.Name).ToList();
        metadata.Resources = metadata.Resources.OrderByDescending(r=>r).ToList();

        //sort assembly metadata
        return metadata;
    }

    private static object ExtractDllMetadata(Assembly assembly)
    {
        return new
        {
            AssemblyName = assembly.GetName().Name,
            Version = assembly.GetName().Version?.ToString(),
            Culture = assembly.GetName().CultureName,
            PublicKeyToken = BitConverter.ToString(assembly.GetName().GetPublicKeyToken()).Replace("-", "").ToLower(),
            ReferencedAssemblies = assembly.GetReferencedAssemblies().Select(a => new
            {
                Name = a.Name,
                Version = a.Version?.ToString(),
                Culture = a.CultureName,
                PublicKeyToken = BitConverter.ToString(a.GetPublicKeyToken()).Replace("-", "").ToLower()
            }).OrderByDescending(item=>item.Name).ToList(),
            Types = assembly.GetTypes().Select(type => new
            {
                Type = type.FullName,
                Namespace = type.Namespace,
                IsClass = type.IsClass,
                IsInterface = type.IsInterface,
                IsEnum = type.IsEnum,
                IsPublic = type.IsPublic,
                IsAbstract = type.IsAbstract,
                BaseType = type.BaseType?.FullName,
                Interfaces = type.GetInterfaces().Select(i => i.FullName).ToList(),
                Attributes = type.GetCustomAttributes().Select(attr => attr.GetType().FullName).ToList(),
                Methods = GetMethods(type),
                Properties = GetProperties(type),
                Fields = GetFields(type)
            }).OrderByDescending(item => item.Type).ToList(),
        };
    }
    private static List<object> GetMethods(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                   .Select(method => new
                   {
                       Name = method.Name,
                       ReturnType = method.ReturnType.FullName,
                       IsPublic = method.IsPublic,
                       IsPrivate = method.IsPrivate,
                       IsStatic = method.IsStatic,
                       IsAbstract = method.IsAbstract,
                       Attributes = method.GetCustomAttributes().Select(attr => attr.GetType().FullName).ToList(),
                       Parameters = method.GetParameters().Select(param => new
                       {
                           Name = param.Name,
                           Type = param.ParameterType.FullName
                       }).ToList()
                   })
                   .Cast<object>() // Convert anonymous type list to List<object>
                   .ToList();
    }

    private static List<object> GetProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                   .Select(prop => new
                   {
                       Name = prop.Name,
                       Type = prop.PropertyType.FullName,
                       CanRead = prop.CanRead,
                       CanWrite = prop.CanWrite,
                       Attributes = prop.GetCustomAttributes().Select(attr => attr.GetType().FullName).ToList()
                   })
                   .Cast<object>() // Convert anonymous type list to List<object>
                   .ToList();
    }

    private static List<object> GetFields(Type type)
    {
        return type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                   .Select(field => new
                   {
                       Name = field.Name,
                       Type = field.FieldType.FullName,
                       IsPublic = field.IsPublic,
                       IsPrivate = field.IsPrivate,
                       IsStatic = field.IsStatic,
                       Attributes = field.GetCustomAttributes().Select(attr => attr.GetType().FullName).ToList()
                   })
                   .Cast<object>() // Convert anonymous type list to List<object>
                   .ToList();
    }



}


public class FullDllMetadata
{
    public string AssemblyName { get; set; }
    public string Version { get; set; }
    public string RuntimeVersion { get; set; }
    public string Architecture { get; set; }
    public string EntryPoint { get; set; }
    public List<TypeInfo> Types { get; set; } = new List<TypeInfo>();
    public List<string> Resources { get; set; } = new List<string>();
    public SortedDictionary<string, string> AssemblyMetadata { get; set; } = new SortedDictionary<string, string>();
    public SortedDictionary<string, object> PEHeaders { get; set; } = new SortedDictionary<string, object>();
    public object DllMetadata { get; set; }
}


public class TypeInfo
{
    public string Name { get; set; }
    public List<string> Methods { get; set; } = new List<string>();
    public List<string> Fields { get; set; } = new List<string>();
    public List<string> Properties { get; set; } = new List<string>();

    public TypeInfo(Type type)
    {
        Name = type.FullName;
        Methods.AddRange(type.GetMethods().Select(m => m.ToString()));
        Fields.AddRange(type.GetFields().Select(f => f.ToString()));
        Properties.AddRange(type.GetProperties().Select(p => p.ToString()));
    }
}
