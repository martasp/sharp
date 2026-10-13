# Upgrading to a new .NET major

**How hard: mechanical, not architectural.** The embedded payload is sourced from pinned
Microsoft-published NuGet packages, so it no longer follows the SDK that builds it. Upgrading is
version literals and names, not a redesign of the mechanism.

## The coupling

Two things determine what the payload holds: the pinned version literals in
`src/Dotnet10Dependencies/Dotnet10Dependencies.csproj`, and the compiler TFM in the project files.

| Thing | Where | Example |
|---|---|---|
| Compiler's own TFM | every `.csproj` | `net10.0` |
| Ref pack, runtime, host pack and DependencyModel the payload is staged from | pinned literal in `src/Dotnet10Dependencies/Dotnet10Dependencies.csproj` | `10.0.11` |
| ILLink analyzers | pinned literal in `src/Dotnet10Dependencies/Dotnet10Dependencies.csproj` | `10.0.12` |

`EmbeddedSdkRefs.FrameworkName` is `"net" + Environment.Version.Major + ".0"` — the compiler's *own*
runtime major, not the target project's. A `net11.0` compiler therefore looks under
`packs/Microsoft.NETCore.App.Ref/<refpack>/ref/net11.0`. So a payload pinned to an 11 ref pack but a
compiler still on `net10.0` (or vice versa) resolves to a folder that does not exist.

The resource name is `embedded-refs.<refpack>.zip` and carries the pinned ref pack version. Nothing has
to keep it in step with the runtime: `Payloads.ZipResourceName` *discovers* the matching resource in
its own assembly instead of comparing against a constant, and a JIT-produced compiler (no embedded
resource) finds the same versioned payload as a `embedded-refs.*.zip` sidecar file. A first version of
the design hardcoded the name and derived the embedded name from whatever SDK happened to build it, so
any machine with a different SDK patch (CI's `setup-dotnet` installs the newest `10.0.x`) embedded the
resource under its own version while the constant asked for the write-in one — the build stayed green
and the compiler failed its run-time lookup.

## Checklist

### 1. Install the SDK
- `~/.config/mise/config.toml` has `dotnet = "latest"`; pin it (e.g. `dotnet = "11.0.100"`) or install
  .NET 11 directly. Only .NET 10 is present today.
- `README.md` → `## Requirements` names the SDK version.

### 2. Payload pipeline
- Bump the pinned version literals in `src/Dotnet10Dependencies/Dotnet10Dependencies.csproj` — the
  ref pack, runtime, host pack and DependencyModel `10.0.11` and the ILLink `10.0.12` — plus the
  compiler TFM in the step below. The runtime *discovers* `embedded-refs.<refpack>.zip` by name, so it
  needs no change of its own.
- `tools/PayloadStager` needs no code change: it takes no arguments, reads the ref-pack version and
  every pack folder back from `src/Dotnet10Dependencies/obj/project.assets.json`, and stamps
  `$(NETCoreSdkVersion)` from the SDK it runs under.
- After a build, verify `src/Dotnet10Dependencies/obj/payload/embedded-refs.<refpack>.zip` exists and
  carries the new version.

### 3. Compiler + first-party TFMs
`<TargetFramework>net10.0</TargetFramework>` → `net11.0` in:
- `src/NativeCompiler/NativeCompiler.csproj`
- `src/NativeCompilerCLI/NativeCompilerCLI.csproj`
- `src/Dotnet10Dependencies/Dotnet10Dependencies.csproj`
- `tools/PayloadStager/PayloadStager.csproj`
- `tests/NativeCompilerTests/NativeCompilerTests.csproj`
- `DotnetRuntime10/`, `DotnetRuntimeHostExe/`, `SimplePlayground/`

### 4. Fallback TFM literals
None to touch - the ~14 `?? "net10.0"` fallbacks and the restored pack default are all
`DefaultTargetFramework.Value` in `src/NativeCompiler/DefaultTargetFramework.cs`; bump that one line.
It is the fragile one: nothing fails if it is missed for a net11 build, the out-of-date TFM is just
silently assumed when the target does not state one.

### 5. Names carrying the major
Only two, and they are cosmetic — rename only if you want the major gone:
- `Dotnet10Dependencies` — project name, assembly name, `namespace Dotnet10Dependencies`, and the
  `using` in `EmbeddedSdkRefs.cs`. Referenced by `NativeCompiler.csproj` and the solution.
- `DotnetRuntime10` — test fixture project + `namespace DotnetRuntime10`, referenced by
  `NativeCompiler.sln` and asserted in `SolutionTests.cs`.

Note `Dotnet10Dependencies` now also appears in `SolutionTests.Parse_RootSolution_Snapshot.verified.txt`
and `SnapshotTests/Sample.Verify_GetdefaultReferences.verified.txt`, so a rename regenerates those.

### 6. Package versions
- `Microsoft.NET.ILLink.Tasks` — the pinned literal in `src/Dotnet10Dependencies/Dotnet10Dependencies.csproj`
  (the only reference; `NativeCompiler` gets ILLink through `Dotnet10Dependencies` and the SDK's own
  `PublishAot` bring-up).
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

Done on this branch: the ~14 hand-written `net10.0` fallbacks are now the single
`DefaultTargetFramework.Value` constant, so the checklist above no longer has a literal-grep step.
