using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Sharp;

internal static class AotAnalyzerMetadata
{
    public static List<string> ReadGeneratorTypes(string dll, List<string> warnings)
    {
        using var stream = File.OpenRead(dll);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata) return new List<string>();

        var reader = peReader.GetMetadataReader();
        var result = new List<string>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (!IsAccessible(reader, handle)) continue;
            if ((type.Attributes & TypeAttributes.Abstract) != 0) continue;
            if ((type.Attributes & TypeAttributes.Interface) != 0) continue;
            if (type.GetGenericParameters().Count > 0) continue;
            if (!HasGeneratorAttribute(reader, type)) continue;
            if (!HasPublicParameterlessConstructor(reader, type))
            {
                warnings.Add($"Warning: generator '{GetFullName(reader, handle)}' has no public parameterless constructor.");
                continue;
            }

            var name = GetFullName(reader, handle);
            if (name is not null) result.Add(name);
        }

        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    public static string EscapeIdentifier(string value) =>
        string.Join(".", value.Split('.').Select(part => "@" + part));

    private static bool HasGeneratorAttribute(MetadataReader reader, TypeDefinition type)
    {
        foreach (var attributeHandle in type.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            EntityHandle parent;
            switch (attribute.Constructor.Kind)
            {
                case HandleKind.MemberReference:
                    parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                    break;
                case HandleKind.MethodDefinition:
                    parent = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
                    break;
                default:
                    continue;
            }

            if (parent.Kind == HandleKind.TypeReference)
            {
                var reference = reader.GetTypeReference((TypeReferenceHandle)parent);
                if (reader.StringComparer.Equals(reference.Name, "GeneratorAttribute") &&
                    reader.StringComparer.Equals(reference.Namespace, "Microsoft.CodeAnalysis"))
                    return true;
            }
            else if (parent.Kind == HandleKind.TypeDefinition)
            {
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)parent);
                if (reader.StringComparer.Equals(definition.Name, "GeneratorAttribute") &&
                    reader.StringComparer.Equals(definition.Namespace, "Microsoft.CodeAnalysis"))
                    return true;
            }
        }

        return false;
    }

    private static bool HasPublicParameterlessConstructor(MetadataReader reader, TypeDefinition type)
    {
        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (!reader.StringComparer.Equals(method.Name, ".ctor")) continue;
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public) continue;
            if (method.GetParameters().Count == 0) return true;
        }

        return false;
    }

    private static bool IsAccessible(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        if (visibility == TypeAttributes.Public) return true;
        if (visibility != TypeAttributes.NestedPublic) return false;
        var declaring = type.GetDeclaringType();
        return !declaring.IsNil && IsAccessible(reader, declaring);
    }

    private static string? GetFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        if (type.GetGenericParameters().Count > 0) return null;

        var name = reader.GetString(type.Name);
        var tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];

        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            var parent = GetFullName(reader, declaring);
            return parent is null ? null : parent + "." + name;
        }

        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
