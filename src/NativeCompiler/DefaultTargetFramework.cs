namespace NativeCompiler;

/// <summary>
/// The target framework the compiler itself is built for. Used as the default whenever a project
/// declares no <c>TargetFramework</c> and for the compiler's own output layout. One definition, so a
/// .NET major bump is a single line here instead of a grep through ~14 fallback literals.
/// </summary>
public static class DefaultTargetFramework
{
    public const string Value = "net10.0";
}