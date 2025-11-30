using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Xunit;
using VerifyTests;
using VerifyXunit;
using System.Linq;

namespace Tests;

[UsesVerify]
public class DllSnapshotTests
{
    private const string MSCompiler = @"D:\main\Pocs\NativeCompiler\testProjects\MSCompiler";
    private const string MyCompiler = @"D:\main\Pocs\NativeCompiler\testProjects\MyCompiler";

    [Fact]
    public async void CompareMSOriginalOutputWithMyCompilerOutput()
    {
        var MsCompilerDllMetadata = ExtractDllMetadata(MSCompiler + @"\bin\Debug\net10.0\MSCompiler.dll");

        var buildDetails = NativeCompiler.NativeCompiler.Build(MyCompiler);
        Assert.True(buildDetails.success, $"Build failed with errors: {buildDetails.errors}");

        var MyCompilerDllMetadata = ExtractDllMetadata(MyCompiler + @"\bin\sharpbin\MSCompiler.dll"); // MyCompiler produces an MSCompiler.exe

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

        Assert.Equal(MsCompilerDllMetadataJson, MyCompilerDllMetadataJson);
    }

    private static object ExtractDllMetadata(string dllPath)
    {
        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"DLL not found: {dllPath}");

        Assembly assembly = Assembly.LoadFile(Path.GetFullPath(dllPath));

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
            }).ToList(),
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
            }).ToList()
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