using System;
using System.Runtime.CompilerServices;
using VerifyTests;

namespace Sharp.Tests;

public static class VerifyConfig
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyDiffPlex.Initialize();
        VerifierSettings.ScrubLinesContaining("AssemblyInformationalVersion");

        // The parity tests spawn many `dotnet build`/`restore` subprocesses in parallel (xunit runs
        // the collection-less classes concurrently). The SDK's build subprocesses ride the single
        // shared MSBuild build-server connection, and concurrent clients wedge it on Linux, stalling
        // the whole suite for minutes. Run each build as its own process - the suite then finishes
        // in tens of seconds. Verified: without this the same tests hang indefinitely (killed by
        // timeout), with it they pass in under a minute.
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
        Environment.SetEnvironmentVariable("DOTNET_CLI_USE_MSBUILD_SERVER", "0");
    }
}