# Upgrading to a new .NET major

**How hard: mechanical, not architectural.** The embedded-payload pipeline is driven entirely by the
SDK that builds it, so the compiler rebuilds its payload from whatever SDK you install. Upgrading is
version literals and names, not a redesign of the mechanism.

## The coupling

Two things determine what the payload holds, and both come straight from the building SDK — there is
no hand-written version: the staged ref pack is `$(BundledNETCoreAppPackageVersion)` and the compiler
TFM is the project files' `net10.0`.

| Thing | Where | Example |
|---|---|---|
| Compiler's own TFM | every `.csproj` | `net10.0` |
| Ref pack the payload is staged from | `$(BundledNETCoreAppPackageVersion)` | `10.0.11` |

`EmbeddedSdkRefs.FrameworkName` is `"net" + Environment.Version.Major + ".0"` — the compiler's *own*
runtime major, not the target project's. A `net11.0` compiler therefore looks under
`packs/Microsoft.NETCore.App.Ref/<refpack>/ref/net11.0`. So a payload built from an SDK 11 ref pack
but a compiler still on `net10.0` (or vice versa) resolves to a folder that does not exist.

The resource name is deliberately **unversioned** (`embedded-refs.zip`): MSBuild derives it from the
SDK-only for the stager's *arguments*, not for the name, so the constant in `Payloads.cs` and the
resource MSBuild embeds always agree. It used to be `embedded-refs.<refpack>.zip`, and that versioned
name broke CI — `setup-dotnet` installs the newest `10.0.x` patch, so the embedded name drifted from
the constant and the compiler failed its run-time lookup.

## Checklist

### 1. Install the SDK
- `~/.config/mise/config.toml` has `dotnet = "latest"`; pin it (e.g. `dotnet = "11.0.100"`) or install
  .NET 11 directly. Only .NET 10 is present today.
- `README.md` → `## Requirements` names the SDK version.

### 2. Payload pipeline
- No version constants to touch: `Payloads.ZipResourceName` is the fixed `embedded-refs.zip`, and
  `Dotnet10Dependencies.csproj` derives the stager's ref-pack/TFM arguments from the SDK. The only
  thing to verify after installing the new SDK is that the payload builds: check
  `obj/.../embedded-refs.zip` exists after a build.
- `tools/PayloadStager` needs no code change; it already takes `--ref-pack-version` and
  `--sdk-version` as arguments.

### 3. Compiler + first-party TFMs
`<TargetFramework>net10.0</TargetFramework>` → `net11.0` in:
- `src/NativeCompiler/NativeCompiler.csproj`
- `src/NativeCompilerCLI/NativeCompilerCLI.csproj`
- `src/Dotnet10Dependencies/Dotnet10Dependencies.csproj`
- `tools/PayloadStager/PayloadStager.csproj`
- `tests/NativeCompilerTests/NativeCompilerTests.csproj`
- `DotnetRuntime10/`, `DotnetRuntimeHostExe/`, `SimplePlayground/`

### 4. Fallback TFM literals
12 `?? "net10.0"` / `= "net10.0"` defaults in `src/NativeCompiler/` (`Project.cs`,
`NativeCompiler.cs`, `NativeCompilerUtils.cs`, `NuGet/PackageRestorer.cs`) and
`src/NativeCompilerCLI/Program.cs`. These are the fragile ones: nothing fails if you miss one, the
wrong TFM is just silently assumed when the target does not state one.

### 5. Names carrying the major
Only two, and they are cosmetic — rename only if you want the major gone:
- `Dotnet10Dependencies` — project name, assembly name, `namespace Dotnet10Dependencies`, and the
  `using` in `EmbeddedSdkRefs.cs`. Referenced by `NativeCompiler.csproj` and the solution.
- `DotnetRuntime10` — test fixture project + `namespace DotnetRuntime10`, referenced by
  `NativeCompiler.sln` and asserted in `SolutionTests.cs`.

Note `Dotnet10Dependencies` now also appears in `SolutionTests.Parse_RootSolution_Snapshot.verified.txt`
and `SnapshotTests/Sample.Verify_GetdefaultReferences.verified.txt`, so a rename regenerates those.

### 6. Package versions
- `Microsoft.NET.ILLink.Tasks` `10.0.12` — in **both** `src/Dotnet10Dependencies/` and
  `src/NativeCompiler/` (must stay the same version).
- `System.Reflection.MetadataLoadContext` `10.0.1`.
- Roslyn `Microsoft.CodeAnalysis*` `5.9.0` — bump to whatever SDK 11 bundles (Roslyn's major is
  independent of the SDK major; verify against the installed SDK, do not assume `+1`).

### 7. Scripts
`publish.bat`, `sign-test-outputs.bat`, `install.sh` hardcode `net10.0` paths.

### 8. Fixtures and snapshots
- ~16 project files under `tests/TestExampleProjects/`, `tests/PackageCacheFixtures/`, `testProjects/`.
- 8 Verify snapshot files contain `net10.0`; the byte-parity snapshots (`CliComparisonTests`,
  `CliCommandParityTests`) may also shift if the SDK changes its terminal-logger output. Regenerate
  rather than hand-edit.

## Validation

```
dotnet test NativeCompiler.sln -c Release          # expect 100/100
bash publish.sh                                     # AOT CLI
# then compile a real project with the published binary and confirm the cache:
#   ~/.cache/sharp/<hash>/sdk/<new-refpack>/
```

The Docker tests (`DockerIntegrationTests`) early-return without a daemon, so they do **not** verify
this locally — that path only runs in CI.

## Make this cheaper

One change would shrink the checklist above:

1. **One TFM constant** for the 12 fallback literals, so a bump is one line instead of a grep.
