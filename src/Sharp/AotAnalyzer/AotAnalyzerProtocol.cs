using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Sharp.AotAnalyzer;

// ABI shared by the AOT analyzer host and every generated plugin. Both sides are NativeAOT
// linux-x64 modules built together, so this is plain unmanaged memory: no managed object, JSON
// document, or file protocol crosses the boundary. Sharp compiles this file and embeds it verbatim
// for the generated plugin shim, so it must not depend on Sharp-only types; bump AbiVersion when
// the unmanaged layout changes.
public static unsafe class AotAnalyzerProtocol
{
    public const int AbiVersion = 1;

    public static AotAnalyzerRequest* AllocateRequest(AotAnalyzerRequestData data)
    {
        var arena = AotArena.Create(EstimateRequestSize(data));
        var request = (AotAnalyzerRequest*)arena.Allocate(sizeof(AotAnalyzerRequest), 8);

        request->AbiVersion = AbiVersion;
        request->Language = arena.Write(data.Language);
        request->Interceptors = arena.Write(data.Interceptors);
        request->Nullable = data.Nullable ? 1 : 0;
        request->AllowUnsafe = data.AllowUnsafe ? 1 : 0;
        request->Defines = WriteStrings(ref arena, data.Defines);
        request->Sources = WriteSources(ref arena, data.Sources);
        request->AdditionalFiles = WriteSources(ref arena, data.AdditionalFiles);
        request->References = WriteStrings(ref arena, data.References);
        request->Options = WriteOptions(ref arena, data.Options);

        return request;
    }

    public static AotAnalyzerResponse* AllocateResponse(AotAnalyzerResponseData data)
    {
        var arena = AotArena.Create(EstimateResponseSize(data));
        var response = (AotAnalyzerResponse*)arena.Allocate(sizeof(AotAnalyzerResponse), 8);

        response->AbiVersion = AbiVersion;
        response->Error = arena.Write(data.Error);
        response->Generated = WriteGenerated(ref arena, data.Generated);
        response->Diagnostics = WriteDiagnostics(ref arena, data.Diagnostics);

        return response;
    }

    public static AotAnalyzerRequestData ReadRequest(AotAnalyzerRequest* request)
    {
        if (request is null)
            throw new InvalidOperationException("AOT analyzer request pointer was null.");
        if (request->AbiVersion != AbiVersion)
            throw new InvalidOperationException($"Unsupported AOT analyzer ABI version {request->AbiVersion}.");

        var data = new AotAnalyzerRequestData
        {
            Language = request->Language.AsString() ?? "Latest",
            Interceptors = request->Interceptors.AsString() ?? "",
            Nullable = request->Nullable != 0,
            AllowUnsafe = request->AllowUnsafe != 0,
        };

        foreach (var value in request->Defines.AsSpan())
            data.Defines.Add(value.AsString() ?? "");

        foreach (var source in request->Sources.AsSpan())
            data.Sources.Add(new AotSourceData(source.Path.AsString() ?? "",
                source.HasText != 0 ? source.Text.AsString() ?? "" : null));

        foreach (var source in request->AdditionalFiles.AsSpan())
            data.AdditionalFiles.Add(new AotSourceData(source.Path.AsString() ?? "",
                source.HasText != 0 ? source.Text.AsString() ?? "" : null));

        foreach (var reference in request->References.AsSpan())
            data.References.Add(reference.AsString() ?? "");

        foreach (var option in request->Options.AsSpan())
            data.Options.Add(new AotOptionData(option.Key.AsString() ?? "", option.Value.AsString() ?? ""));

        return data;
    }

    public static AotAnalyzerResponseData ReadResponse(AotAnalyzerResponse* response)
    {
        if (response is null)
            throw new InvalidOperationException("AOT analyzer response pointer was null.");
        if (response->AbiVersion != AbiVersion)
            throw new InvalidOperationException($"Unsupported AOT analyzer response ABI version {response->AbiVersion}.");

        var data = new AotAnalyzerResponseData
        {
            Error = response->Error.AsString(),
        };

        foreach (var generated in response->Generated.AsSpan())
        {
            data.Generated.Add(new AotGeneratedSourceData
            {
                HintName = generated.HintName.AsString() ?? "",
                Text = generated.Text.AsString() ?? "",
            });
        }

        foreach (var diagnostic in response->Diagnostics.AsSpan())
        {
            data.Diagnostics.Add(new AotDiagnosticData
            {
                Id = diagnostic.Id.AsString() ?? "",
                Message = diagnostic.Message.AsString() ?? "",
                File = diagnostic.File.AsString() ?? "",
                Severity = diagnostic.Severity,
                Line = diagnostic.Line,
                Column = diagnostic.Column,
                EndLine = diagnostic.EndLine,
                EndColumn = diagnostic.EndColumn,
            });
        }

        return data;
    }

    public static void Free(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
            NativeMemory.Free((void*)pointer);
    }

    private static ArrayView<Utf8String> WriteStrings(ref AotArena arena, List<string> values)
    {
        if (values.Count == 0) return default;

        var items = arena.Allocate<Utf8String>(values.Count);
        for (var index = 0; index < values.Count; index++)
            items[index] = arena.Write(values[index]);

        return new ArrayView<Utf8String> { Items = items, Length = values.Count };
    }

    private static ArrayView<AotSourceInput> WriteSources(ref AotArena arena, List<AotSourceData> values)
    {
        if (values.Count == 0) return default;

        var items = arena.Allocate<AotSourceInput>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            items[index] = new AotSourceInput
            {
                Path = arena.Write(value.Path),
                Text = arena.Write(value.Text),
                HasText = value.Text is null ? 0 : 1,
            };
        }

        return new ArrayView<AotSourceInput> { Items = items, Length = values.Count };
    }

    private static ArrayView<AotOption> WriteOptions(ref AotArena arena, List<AotOptionData> values)
    {
        if (values.Count == 0) return default;

        var items = arena.Allocate<AotOption>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            items[index] = new AotOption
            {
                Key = arena.Write(values[index].Key),
                Value = arena.Write(values[index].Value),
            };
        }

        return new ArrayView<AotOption> { Items = items, Length = values.Count };
    }

    private static ArrayView<AotGeneratedSource> WriteGenerated(
        ref AotArena arena, List<AotGeneratedSourceData> values)
    {
        if (values.Count == 0) return default;

        var items = arena.Allocate<AotGeneratedSource>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            items[index] = new AotGeneratedSource
            {
                HintName = arena.Write(values[index].HintName),
                Text = arena.Write(values[index].Text),
            };
        }

        return new ArrayView<AotGeneratedSource> { Items = items, Length = values.Count };
    }

    private static ArrayView<AotDiagnostic> WriteDiagnostics(
        ref AotArena arena, List<AotDiagnosticData> values)
    {
        if (values.Count == 0) return default;

        var items = arena.Allocate<AotDiagnostic>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            items[index] = new AotDiagnostic
            {
                Id = arena.Write(value.Id),
                Message = arena.Write(value.Message),
                File = arena.Write(value.File),
                Severity = value.Severity,
                Line = value.Line,
                Column = value.Column,
                EndLine = value.EndLine,
                EndColumn = value.EndColumn,
            };
        }

        return new ArrayView<AotDiagnostic> { Items = items, Length = values.Count };
    }

    private static long EstimateRequestSize(AotAnalyzerRequestData data)
    {
        var size = 1024L + sizeof(AotAnalyzerRequest);
        size += Utf8Size(data.Language) + Utf8Size(data.Interceptors);
        size += StringsSize(data.Defines);
        size += SourcesSize(data.Sources);
        size += SourcesSize(data.AdditionalFiles);
        size += StringsSize(data.References);
        size += (long)data.Options.Count * (sizeof(AotOption) + 16);
        foreach (var option in data.Options)
            size += Utf8Size(option.Key) + Utf8Size(option.Value);

        return size;
    }

    private static long EstimateResponseSize(AotAnalyzerResponseData data)
    {
        var size = 1024L + sizeof(AotAnalyzerResponse);
        size += Utf8Size(data.Error);
        size += (long)data.Generated.Count * (sizeof(AotGeneratedSource) + 16);
        foreach (var generated in data.Generated)
            size += Utf8Size(generated.HintName) + Utf8Size(generated.Text);

        size += (long)data.Diagnostics.Count * (sizeof(AotDiagnostic) + 16);
        foreach (var diagnostic in data.Diagnostics)
        {
            size += Utf8Size(diagnostic.Id) + Utf8Size(diagnostic.Message) +
                Utf8Size(diagnostic.File);
        }

        return size;
    }

    private static long StringsSize(List<string> values)
    {
        var size = (long)values.Count * (sizeof(Utf8String) + 16);
        foreach (var value in values) size += Utf8Size(value);
        return size;
    }

    private static long SourcesSize(List<AotSourceData> values)
    {
        var size = (long)values.Count * (sizeof(AotSourceInput) + 16);
        foreach (var value in values)
            size += Utf8Size(value.Path) + Utf8Size(value.Text);
        return size;
    }

    private static long Utf8Size(string? value) =>
        value is null ? 0 : Encoding.UTF8.GetByteCount(value);
}

public sealed class AotAnalyzerRequestData
{
    public string Language { get; set; } = "Latest";
    public string Interceptors { get; set; } = "";
    public bool Nullable { get; set; }
    public bool AllowUnsafe { get; set; }
    public List<string> Defines { get; } = new();
    public List<AotSourceData> Sources { get; } = new();
    public List<AotSourceData> AdditionalFiles { get; } = new();
    public List<string> References { get; } = new();
    public List<AotOptionData> Options { get; } = new();
}

public sealed class AotSourceData
{
    public AotSourceData(string path, string? text)
    {
        Path = path;
        Text = text;
    }

    public string Path { get; }
    public string? Text { get; }
}

public sealed class AotOptionData
{
    public AotOptionData(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }
    public string Value { get; }
}

public sealed class AotAnalyzerResponseData
{
    public string? Error { get; set; }
    public List<AotGeneratedSourceData> Generated { get; } = new();
    public List<AotDiagnosticData> Diagnostics { get; } = new();
}

public sealed class AotGeneratedSourceData
{
    public string HintName { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class AotDiagnosticData
{
    public string Id { get; set; } = "";
    public string Message { get; set; } = "";
    public string File { get; set; } = "";
    public int Severity { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct AotAnalyzerRequest
{
    public int AbiVersion;
    public Utf8String Language;
    public Utf8String Interceptors;
    public int Nullable;
    public int AllowUnsafe;
    public ArrayView<Utf8String> Defines;
    public ArrayView<AotSourceInput> Sources;
    public ArrayView<AotSourceInput> AdditionalFiles;
    public ArrayView<Utf8String> References;
    public ArrayView<AotOption> Options;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct AotAnalyzerResponse
{
    public int AbiVersion;
    public Utf8String Error;
    public ArrayView<AotGeneratedSource> Generated;
    public ArrayView<AotDiagnostic> Diagnostics;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct AotSourceInput
{
    public Utf8String Path;
    public Utf8String Text;
    public int HasText;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct AotOption
{
    public Utf8String Key;
    public Utf8String Value;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct AotGeneratedSource
{
    public Utf8String HintName;
    public Utf8String Text;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct AotDiagnostic
{
    public Utf8String Id;
    public Utf8String Message;
    public Utf8String File;
    public int Severity;
    public int Line;
    public int Column;
    public int EndLine;
    public int EndColumn;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Utf8String
{
    public byte* Data;
    public int Length;

    public readonly string? AsString() =>
        Data is null ? null : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(Data, Length));
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArrayView<T> where T : unmanaged
{
    public T* Items;
    public int Length;

    public readonly ReadOnlySpan<T> AsSpan() =>
        Items is null || Length == 0 ? ReadOnlySpan<T>.Empty : new ReadOnlySpan<T>(Items, Length);
}

public unsafe struct AotArena
{
    public byte* Root;
    public long Capacity;
    public long Offset;

    public static AotArena Create(long capacity)
    {
        if (capacity < 1) capacity = 1;
        return new AotArena
        {
            Root = (byte*)NativeMemory.Alloc((nuint)capacity),
            Capacity = capacity,
        };
    }

    public void* Allocate(long size, int alignment = 8)
    {
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (alignment < 1) alignment = 1;

        var aligned = (Offset + alignment - 1) & ~(long)(alignment - 1);
        var end = aligned + size;
        if (end > Capacity)
            throw new InvalidOperationException(
                $"AOT analyzer ABI buffer is too small (need {end} bytes, have {Capacity}).");

        var result = Root + aligned;
        Offset = end;
        return result;
    }

    public T* Allocate<T>(int count) where T : unmanaged
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return (T*)Allocate((long)count * sizeof(T), 8);
    }

    public byte* AllocateBytes(int length, int alignment = 1) =>
        (byte*)Allocate(length, alignment);

    public Utf8String Write(string? value)
    {
        if (value is null) return default;

        var length = Encoding.UTF8.GetByteCount(value);
        var data = AllocateBytes(length);
        if (length > 0)
            Encoding.UTF8.GetBytes(value, new Span<byte>(data, length));

        return new Utf8String { Data = data, Length = length };
    }
}
