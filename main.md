# NativeCompiler

Self-hosting C# compiler built on Roslyn. Compiles `.csproj` projects and `.sln` solutions without MSBuild.

## Usage

```
NativeCompilerCLI <command> [target] [arguments]
```

Commands mirror the .NET SDK: `build`, `run`, `help`, `version` (`--version`).
Legacy bare-path invocation still works: `NativeCompilerCLI <target>` routes to `build`.

`<target>` can be:

- a project **folder** (builds the single `.csproj` found in it, output to `<proj>\myc\<config>\<tfm>\`)
- a `.csproj` file path
- a `.sln` file path (builds every project in dependency order; all failures reported at the end)

When no target is given the current directory is searched for a single `.csproj`/`.sln(x)`,
exactly like `dotnet build`/`dotnet run` (missing -> MSB1003 / "Couldn't find a project to
run"; ambiguous -> MSB1011). `run` launches the freshly built app (apphost when present,
else `dotnet <dll>`), propagates the app exit code, and forwards everything after `--` as
application arguments. `--version` prints the hosting SDK version; `help` prints the SDK's
general help text, with the SDK command list annotated `[implemented]`/`[not implemented]`
(derived from the command registry, not hardcoded) and the version line taken from the hosting
SDK. Like `dotnet`, flags the compiler does not act on (`--no-restore`,
`--no-incremental`, `--nologo`, `-v`, ...) are accepted and ignored.

## Examples

Real, captured output (all commands exit `0` unless noted). `<repo>` =
`<abs path to this repo>`. Full, live examples incl. `run --` forwarding and
SDK side-by-side output live in **`README.md`**.

```
# build a single project by folder or .csproj file (terminal-logger summary w/ seconds, path relative to CWD)
cd <repo> && NativeCompilerCLI build tests/TestExampleProjects/HelloParityFixture
  HelloParityFixture net10.0 succeeded (0.4s) → tests/TestExampleProjects/HelloParityFixture/myc/Debug/net10.0/HelloParityFixture.dll

Build succeeded in 1.2s
```

```
# build a solution (.sln/.slnx use the terminal-logger grammar: per-project timing lines + summary)
NativeCompilerCLI build <repo>/tests/TestExampleProjects/SlnFixtures/SlnFixtures.sln
  LibA net10.0 succeeded (0.1s) → tests/TestExampleProjects/SlnFixtures/LibA/myc/Debug/net10.0/LibA.dll
  LibB net10.0 succeeded (0.1s) → tests/TestExampleProjects/SlnFixtures/LibB/myc/Debug/net10.0/LibB.dll
  SlnApp net10.0 succeeded (0.2s) → tests/TestExampleProjects/SlnFixtures/SlnApp/myc/Debug/net10.0/SlnApp.dll

Build succeeded in 0.4s
```

```
# build with no target: the current directory is searched for a single project/solution
cd <repo>/tests/TestExampleProjects/HelloParityFixture && NativeCompilerCLI build
cd <repo>/tests/TestExampleProjects              && NativeCompilerCLI build   # sole SlnxFixtures.slnx
```

```
# run a console app; arguments after `--` are forwarded to the app
NativeCompilerCLI run <repo>/tests/TestExampleProjects/HelloParityFixture
Hello from NativeCompiler!
```

```
# a failing build reports errors (one indented line per error) and exits 1
cd <repo> && NativeCompilerCLI build tests/TestExampleProjects/FailingFixture
  FailingFixture net10.0 failed with 1 error(s) (0.6s)
    <repo>/tests/TestExampleProjects/FailingFixture/Class1.cs(5,29): error CS0103: The name 'NonExistent' does not exist in the current context

Build failed with 1 error(s) in 1.2s
# $? = 1
```

```
# version and help mirror the host SDK
NativeCompilerCLI version    # prints the SDK version, e.g. 10.0.400
NativeCompilerCLI help       # full dotnet help text
```

## What we support

### Project compilation
- SDK-style `.csproj` parsing: `TargetFramework`, `OutputType` (Exe / Library), `AllowUnsafeBlocks`, `LangVersion`, `Nullable`, `DefineConstants`, `ImplicitUsings` (generated `GlobalUsings.g.cs`), embedded/`None` compile items
- C# compilation via Roslyn (syntax trees loaded with embedded `EmbeddedText`), any language version
- Compile-time diagnostics printed to stderr; successful emit writes the DLL to `<proj>\myc\<config>\<tfm>\`

### References
- **Project references** – recursive and transitive: a referenced project is built first and its output DLL referenced; deep chains resolve through every level
- **NuGet packages** – TFM-aware `lib/<tfm>` selection (exact match preferred, then older core, then netstandard; legacy `net4x` handled), best-match `.nuspec` dependency group chosen for the target TFM, transitive dependencies resolved from `.nuspec` recursively
- **Central package management** – versionless `<PackageReference>` resolved from `Directory.Packages.props` (`<PackageVersion Include=… Version=…>`), searched up the directory tree
- **Framework references** – from the installed SDK/ref pack + runtime dirs; merged with NuGet/local references, suppressing duplicates whose simple name collides with a framework assembly (NuGet-style suppression)
- **Shared frameworks (`FrameworkReference`)** – `Microsoft.AspNetCore.App` and any other targeting pack resolved from `<dotnet>\packs\<Name>.Ref\<ver>\ref\<tfm>`; only the base `Microsoft.NETCore.App` is assumed inbox
- Referenced DLLs copied into the output folder so the built app can run standalone

### Running
- `OutputType=Exe` console projects are compiled and executed with a runtime config (`*runtimeconfig.json`) and apphost
- Compile-time errors reported with file:line:col

### Solutions
- `.sln` parsing (classic VS format), only `.csproj` entries considered (`.exe`/environmental project stubs skipped)
- Dependency-order topological build (`dependencies first`), deterministic order
- **Build-all / report-all**: every project is attempted even if an earlier one fails; per-project OK/FAIL + duration listed; process exits 1 if any project failed
- Recursive build-in-progress guard that turns cyclic project-reference graphs into a clear error instead of infinite recursion

### Scripts
- `publish.bat` – `dotnet publish` win-x64 Release
- `publish.sh`  – `dotnet publish` linux-x64 Release (needs Linux/WSL for the AOT native toolchain)

## CLI command parity

Captured, byte-for-byte output comparison of our CLI vs the matching `dotnet` command
(`CliCommandParityTests`, side-by-side Verify snapshots). One-off detail lines that cannot be
deterministically reproduced (SDK wall-clock `Time Elapsed`, restore banners) are stripped by
the shared normalizer. `build` off the fixtures below is run with `--nologo --no-restore
--no-incremental` on the SDK side.

| Command | Ours | `dotnet` | Parity |
|---|---|---|---|
| `build` (success, single project) | `Name tfm succeeded (0.4s) → <cwd-rel>/...dll` + blank + `Build succeeded in 1.2s`, rc=0 | same terminal-logger summary | identical after normalize (seconds → `<secs>`) |
| `build` (failure, single project) | `Name tfm failed with N error(s) (0.6s)` + one indented error line per error + blank + `Build failed with N error(s) in 1.2s`, rc=1 | same | identical after normalize |
| `run` (success) | application output only, rc=0 | same (incl. `--no-restore`) | identical |
| `run` (failure) | `error CS0103 (...)` + `The build failed. Fix the build errors and run again.`, rc=1 | same | identical |
| `run -- <app args>` | args forwarded to the app | same | identical |
| `--version` / `version` | `10.0.400` (hosting SDK) | `10.0.400` | identical |
| `help` | full SDK command list, each annotated `[implemented]` / `[not implemented]`, version line from the hosting SDK | plain SDK command list | intentionally annotated (byte-identical otherwise) |
| `build` in dir without a project | `MSBUILD : error MSB1003: ...` | same | identical |
| `build` in dir with several projects | `MSBUILD : error MSB1011: ...` | same | identical |
| `run` with no runnable project | `Couldn't find a project to run. Ensure a project exists in <dir>, or pass the path to the project using --project.` | same | identical |
| `build <path.sln>` (path) | terminal-logger grammar: per-project timing lines, failures echo errors under the project, then `Build succeeded[ in Ns]` | same | verdict + exit code identical; layout snapshot-pinned (SDK's own solution rendering is build-state dependent) |
| `build <path.slnx>` (path) | same grammar as `.sln` | same | verdict + exit code identical; layout snapshot-pinned |
| `build <path.csproj>` (path) | terminal-logger summary | same | identical after normalize (seconds → `<secs>`) |
| `build` (cwd = single `.slnx`) | resolves the sole `SlnxFixtures.slnx`, maps + summary | same | identical |
| `build` (cwd = single `.csproj`) | resolves sole `HelloParityFixture.csproj`, terminal summary | same | identical after normalize |

Solution builds use the same terminal grammar as single-project builds. They cannot be byte-pinned
against the SDK: the SDK's own solution rendering is build-state dependent — it only leaves
per-project status lines when a project is visibly compiling, and prints a bare `Build succeeded.`
(no `in Ns`) when every project is up-to-date. So `CliCommandParityTests` asserts the deterministic
surface (exit code + `Build succeeded`/`Build failed` verdict via
`CliComparisonHelper.NormalizeSolutionInteractive`) and pins our full layout with Verify
snapshots (`build_sln_path`, `build_slnx_path`, `build_slnx_no_path_cwd`).

Known gap: NuGet restore warnings (e.g. `NU1510`) are not reproduced, so the warning count in the
summary matches the SDK only for diagnostics the compile pipeline itself produces.

Single-project `build` always prints the SDK terminal-logger summary (the format `dotnet build`
uses on a terminal): measured seconds included, error counts computed from the actual diagnostics,
the build path relative to the current directory. The SDK's terminal logger is forced on
(`MSBUILDTERMINALLOGGER=on`) in the tests, and run-varying seconds are normalized to a `<secs>`
token via `CliComparisonHelper.NormalizeInteractive` (also strips the SDK's ANSI render noise).
Solution builds print the same terminal-logger grammar (per-project timing lines, warnings indented
under their project, `Build succeeded[ in Ns]` / `Build failed with N error(s) in Ns`).
Info-severity diagnostics (e.g. `SYSLIB1045`) are dropped, matching the SDK at default verbosity;
warning-severity diagnostics travel through `BuildResult.Warnings` and are rendered by the CLI
rather than printed from the analyzer pipeline.

## Benchmarking

```
dotnet test tests\NativeCompilerTests --filter "Category=Benchmark" --nologo
```

Runs with the suite every time and prints how `BuildSolution` compares to `dotnet build`
(cold incl. restore, and warm/incremental) on `NativeCompiler.sln`. Both tools build a
clean temp copy so the repo is untouched. The table is also saved to
`%TEMP%\opencode-bench\latest-benchmark.txt`. Timings are informational — no thresholds are
asserted. Set `NATIVECOMPILER_SKIP_BENCHMARK=1` to skip it for a fast suite run.

## What we do NOT support (by design, for now)

`NativeCompilerTests` is part of `NativeCompiler.sln` and compiles via the test entry-point shim
(see "Next features", item 2). `DotnetRuntimeHostExe` is re-enabled and compiles. `dotnet build`
and our `BuildSolution` both pass on the solution.

- **Source generators** – implemented (GENERATOR_PIPELINE, see below); remaining edge cases: checksum-based `[InterceptsLocation]` matching, package `build/`-target-injected analyzer assets (`analyzers` via targets)
- **MSBuild-target–generated project shapes** – test projects whose `Main` is produced by package build targets now compile through a hand-rolled no-op entry-point shim (fixes CS5001); running the tests still uses `dotnet test` against the real SDK build, and the faithful xunit.v3/MTP runner entry point remains out of scope
- **Packages with no `lib` assemblies** – compile references injected purely through package `buildTransitive` targets are not reproduced. `xunit.v3` ships zero DLLs (its assemblies arrive via `.nuspec` dependencies + targets), so `Xunit.*` namespaces stay unresolved; transitive `.nuspec` resolution helps but cannot fully replace target-injected `<Reference>` items
- **Analyzers / generators / build targets** from packages (`build`, `buildTransitive`, `analyzers` folders)
- **Configuration matrix** – a single build configuration is compiled; `Debug`/`Release`-conditional csproj items are not evaluated (MSBuild condition evaluation)
- **AOT for other platforms** – NativeCompilerCLI has `PublishAot=true`; emitting linux-x64 native requires the host machine's AOT cross toolchain (see `publish.sh`)

## Future work

### OrchardCore canary
`OrchardCore.slnx` (239 projects, .NET 10, ASP.NET Core CMS) is the external stress test. Fresh clone +
`dotnet restore` (warms the NuGet cache), then `NativeCompilerCLI OrchardCore.slnx`. Baseline
measurement: **3 OK / 236 FAIL** (42.3s) vs `dotnet build OrchardCore.slnx --no-restore` (149.4s, exit 0).
Pilot led to real fixes (`.slnx` parsing, CLI routing) and this prioritized gap list:

| # | Status | Feature needed | Why | Unlocks |
|---|--------|----------------|-----|---------|
| 1 | **DONE** | `FrameworkReference` / shared-framework resolution (`Microsoft.AspNetCore.App`) | 209/245 csprojs declare it; compiler only resolved base `Microsoft.NETCore.App` | all modules + core + abstractions (~200) |
| 2 | **DONE** | Evaluate `Directory.Build.props` + `Import` (TFM, `ImplicitUsings`, nullable, common items) | csprojs are skeletal; `TargetFramework`/`ImplicitUsings` live in `src\OrchardCore.Build\TargetFrameworks.props` | same tower (pairs with #1) |
| 3 | **DONE** | Source generator pipeline (see below) | `OrchardCore.SourceGenerators` used by every project via `Directory.Build.props` analyzer ref (`OutputItemType="Analyzer"`); xunit.v3/MTP entry points remain | test projects |
| 4 | open | Razor SDK views + `buildTransitive` target assets | modules are `Microsoft.NET.Sdk.Razor`; some packages inject refs via targets | runtime parity, not strictly compile-blocking |

Measurement after #1: AspNetCore-wall gone (CS0234 122→20, zero `Microsoft.AspNetCore.*` namespace
errors); count still 3/239 because the ImplicitUsings wall (#2) now dominates (256 `'Task'/'IEnumerable'`
missing). Error trail: `harness\OrchardCore\ours2.err`.

Measurement after #2: **8 OK / 231 FAIL** (40.6s). ImplicitUsings wall gone (`'Task'/'IEnumerable'` = 0);
`error CS` collapsed 1644 → ~70; 229 = cascade `Cant find dll path` from a dotted-name output bug (see
below). Trail: `ours4.err`.

Further canary fixes found during the sharpbin audit (also regressions for any dotted project name):
- **Dotted-name output bug**: `Path.ChangeExtension("X.Y", ".dll")` drops the tail segment (`X.dll`), so
  `OrchardCore.Abstractions.dll` was emitted as `OrchardCore.dll` and every dependent failed
  `Cant find dll path`. Fixed in `Project.cs` (concatenate `ProjectName + ".dll"`, never `ChangeExtension`);
  regression test in `CompilerApiTests.Build_DottedProjectName_ProducesFullyNamedAssembly`.
- **Empty DLL leftovers**: an emit failure left a 0-byte `*.dll`/`*.pdb`; dependents then hit
  `CS0009 PE image doesn't contain managed metadata`. `ProduceDll` now deletes the files on failure.
- **Assembly-attribute duplicates**: injected `VersionInfo.g.cs` hardcoded `AssemblyCompany/Product/Title`
  attributes → `CS0579` on projects with their own `[assembly:]` (all OrchardCore `Properties\AssemblyInfo.cs`).
  Now only injected when the project declares none (MSCompiler/MyCompiler snapshot parity kept).
- **Central Package Management for transitive refs**: `GetTransitiveNugetPackages` skipped versionless
  `<PackageReference>` (OrchardCore centralizes versions in `Directory.Packages.props`); now falls back to
  `ResolveCentralVersion` like the direct path. Unlocks YesSql/Fluid-powered modules.

Measurement after fixes: **80 OK / 159 FAIL** (54.6s; `.slnx`). Remaining walls: `Microsoft.NET.Sdk.Razor`
SDK-injected references (modules), remaining `Microsoft.AspNetCore.App` type gaps, real long-tail compile
errors. Trail: `ours10.err`.

Measurement after GENERATOR_PIPELINE (+ TFM constants, shadow-copy, interceptor opt-in plumbing):
**120 OK / 116 FAIL** (~150s; `.slnx`). `OrchardCore.SourceGenerators`, `OrchardCore.DisplayManagement`,
`OrchardCore.ContentTypes.Abstractions`, `OrchardCore.Module.Targets` all compile. Tail: `OrchardCore.Navigation.Core`
(CS9137 interceptor namespace gap, see below) plus cascades. Trail: `canary-raw.txt`.

Canary command:
```
git clone --depth 1 https://github.com/OrchardCMS/OrchardCore.git
dotnet restore OrchardCore.slnx          # populate ~/.nuget/packages once
NativeCompilerCLI OrchardCore.slnx       # then: OK/FAIL counts + ours2.err analysis
```

### Source generator support (GENERATOR_PIPELINE)
Implemented in `SourceGeneratorRunner.cs` — real `CSharpGeneratorDriver` runs, no hand-rolled synthesis:

1. **Analyzer discovery** — ref-pack generators (`Microsoft.NETCore.App.Ref\…\analyzers\dotnet\cs\`: regex,
   System.Text.Json, LibraryImport, JSImport, ComInterface) + NuGet/SDK analyzers + **Analyzer project
   references** — the last resolved from BOTH the csproj and evaluated `Directory.Build.props`
   (`<ProjectReference OutputItemType="Analyzer">`; OrchardCore injects its own
   `OrchardCore.SourceGenerators` into every project this way). Analyzer project refs are built on demand
   (missing output → compiler-internal build) and skipped when the compiler is itself the generator.
2. **Execution parity** — `ISourceGenerator`/`IIncrementalGenerator` normalized via `AsSourceGenerator()`,
   M.CA packages matched to OrchardCore's central version (`Microsoft.CodeAnalysis*` 5.6.0 in the host app),
   Roslyn-distribution assemblies filtered out of the analyzer set so dev-version M.C.A doesn't clash with the
   loaded compiler, generator `ParseOptions` built with **implicit TFM preprocessor constants**
   (`NET9_0_OR_GREATER`, `NET10_0_OR_GREATER`, … from `NativeCompilerUtils.GetSdkPreprocessorSymbols`) so
   `#if`-guarded generated code compiles, and `InterceptorsNamespaces` opt-in plumbed from the SDK/csproj
   props (`ApplyInterceptorsNamespaces`). Only Error-severity generator diagnostics fail the build; load/init
   warnings are printed. Generated trees are re-parsed with the project's parse options before being merged
   via `AddSyntaxTrees`.
3. **Shadow-copy loading** — analyzer DLLs are copied to `%TEMP%\NativeCompilerGenerators` keyed by
   last-write time and loaded from there, so repeated builds can rewrite the original `sharpbin` DLL
   (otherwise the load lock raises IOException). Loads go through one dedicated `AssemblyLoadContext`
   per project run whose `Resolving` handler maps each analyzer's simple name back to its shadow copy:
   generators with co-located dependencies (LibraryImport/JSImport/ComInterface →
   `Microsoft.Interop.SourceGeneration`) keep working whatever the load order or hosting process.
   The whole set is registered before any assembly is loaded, because the dependency DLLs sort after
   the generators that reference them. `Microsoft.CodeAnalysis.{CSharp.,}Analyzers` are excluded from
   the set (analyzer-only packages, they clash with / can't resolve against the host Roslyn).

Milestones verified on the canary: `OrchardCore.SourceGenerators` (the generator itself) compiles,
`OrchardCore.DisplayManagement` no longer fails, shadow-copied generators load and emit shapes.

**Remaining gap**: `OrchardCore.Navigation.Core` fails `CS9137` — OrchardCore's shape generator emits
checksum-based `[InterceptsLocation(1, "<checksum>")]`; our parse-option interception opt-in makes the
namespace available, but the file-content checksum encoded in the generated data isn't matched against our
trees the way csc matches it (real `dotnet build` passes, ours reports "interceptors feature is not enabled
in this namespace"). One remaining OrchardCore cluster; `InterceptorsNamespaces` still keyed off the SDK
property so non-OrchardCore interceptors work.

Once green, `DotnetRuntimeHostExe` was un-commented in `NativeCompiler.sln` and now compiles under both
`dotnet build` and our `BuildSolution` (its `[LibraryImport]` partial methods are emitted by the
ref-pack `LibraryImportGenerator`). `NativeCompilerTests` stays commented pending the entry-point
shim listed under "Next features" below. Microsoft's docs: `Microsoft.CodeAnalysis.CSharp` ships
everything needed (`CSharpGeneratorDriver`, `ISourceGenerator`/`IIncrementalGenerator`).

**Execution note (Smart App Control)**: `NativeCompilerCLI` sets `<UseAppHost>false</UseAppHost>`, and our
executable emission now respects that property too (the CLI's `bin\sharpbin` output is DLL-only). This
machine's code-integrity policy enforces enterprise signing-level on image loads into **unsigned**
processes — exactly what `dotnet build` avoids by hosting everything in Microsoft-signed `dotnet`
processes. A standalone unsigned apphost that loads freshly built generator assemblies (`%TEMP%`
shadow-copies) got blocked (`ERROR_FILE_BLOCKED_BY_POLICY`), while the same workloads run clean under the
signed `dotnet` host (`dotnet run`).

## Next features (roadmap)

Priority-ordered work items, derived from the canary tail and the gaps above. Definition of done for each:
the canary OK-count rises with no regressions, plus a unit test in `CompilerApiTests` when the behavior is
not OrchardCore-specific.

| # | Feature | Why | Unlocks |
|---|---------|-----|---------|
| 1 | `InterceptsLocation` checksum parity (CS9137) | csc matches the checksum encoded in interceptor `data` against the file that triggered generation; we report "interceptors feature is not enabled in this namespace" instead | `OrchardCore.Navigation.Core` + ~40 cascades |
| 2 | **DONE** | Test-project entry-point shim | `OutputType=Exe` with no source `Main` failed CS5001; xunit.v3 injects `XunitAutoGeneratedEntryPoint.cs` via build targets that don't run here, so a dependency-free no-op `Main` is generated for `IsTestProject`/test-runner projects (unit test: `TestEntryPointShimTests`) | `NativeCompilerTests` un-commented, project compiles |
| 3 | `Microsoft.NET.Sdk.Razor` project support | module csprojs use the Razor SDK; implicit `FrameworkReference` + razor generators are SDK-injected | remaining modules behind the Razor wall |
| 4 | Package `build`/`buildTransitive`-injected compile references | some packages inject `<Reference>` items via target files we don't run; `xunit.v3` ships zero `lib/` DLLs | packages without `lib` assemblies |
| 5 | MSBuild condition evaluation (configuration matrix) | Debug/Release-conditional csproj items are currently skipped | correct multi-configuration output |
| 6 | **DONE** | Visual output-behavior parity | pin our CLI output side-by-side against real `dotnet build` in a Verify snapshot (`CliComparisonTests`), so output drift toward/away from the SDK is reviewable; exposes leftover debug noise (`Sucess!!` banner, hidden CS8019 relayed from generators, missing project->path lines) | ~ `dotnet build` parity review |

## Known behaviors

- `bin\sharpbin` is the output folder per project; no cache invalidation between runs yet
- Verifiers in the test suite snapshot the SDK/fixture output layout; transient test-runner artifacts (diagnostic logs, coverage markers) in the test bin dir must not be committed