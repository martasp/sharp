using System.Runtime.CompilerServices;
using VerifyTests;

namespace NativeCompilerTests;

public static class VerifyConfig
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyDiffPlex.Initialize();
        VerifierSettings.ScrubLinesContaining("AssemblyInformationalVersion");
    }
}