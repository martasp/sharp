# Copilot instructions for Sharp

This repository is a self-hosting .NET compiler and CLI built on Roslyn. The project is intentionally shaped to mimic the `dotnet` CLI contract for build/run/help/version behavior without depending on MSBuild to execute the build pipeline.

## Build, test, and validation commands

### Required SDK

- Use .NET SDK 10 (`10.0.400`), matching the repo's compatibility target.
- CI provisions `.NET 10` and builds the solution in Debug mode before running tests.

### Restore and build

```bash
dotnet restore Sharp.sln
dotnet build Sharp.sln --nologo
```

The repo also publishes the AOT-compiled CLI with:

```bash
bash publish.sh
```

This produces the self-contained CLI under `src/Sharp.Cli/bin/Release/net10.0/.../publish/`.

### Test suite

The main test project is:

```bash
dotnet test tests/Sharp.Tests/Sharp.Tests.csproj --nologo
```

A single test or subset can be filtered by name or category, for example:

```bash
dotnet test tests/Sharp.Tests/Sharp.Tests.csproj --filter "FullyQualifiedName~CliCommandParityTests" --nologo
dotnet test tests/Sharp.Tests/Sharp.Tests.csproj --filter "Category=Benchmark" --nologo
```

The repo has benchmark tests that are intended to be run explicitly with a filter, not as part of a casual smoke run.

### Restore test fixtures before parity tests

The fixture projects under `tests/TestExampleProjects/` and `testProjects/` are intentionally outside `Sharp.sln`, so restore them explicitly when you need parity or fixture-driven tests:

```bash
find testProjects tests/TestExampleProjects tests/PackageCacheFixtures -name '*.csproj' -print0 \
  | xargs -0 -n1 dotnet restore
```

There is no separate lint target in this repo; build + test validation is the standard path.

## High-level architecture

### CLI front-end

- `src/Sharp.Cli/Program.cs` handles the user-facing commands (`build`, `run`, `restore`, `clean`, `publish`, `help`, `version`).
- The CLI intentionally mirrors `dotnet` argument resolution, output format, exit codes, and current-directory project discovery.
- Legacy bare-path invocation (`sharp <folder-or-sln>`) is routed to `build`.

### Core compiler engine

- `src/Sharp/SharpCompiler.cs` exposes the public build surface.
- `src/Sharp/Build/ProjectBuild.cs` performs a single-project build, including project loading, Roslyn compilation, source generator/analyzer execution, and output writing.
- `src/Sharp/Build/SolutionBuild.cs` resolves dependency order, groups projects into build waves, and performs parallel solution builds while keeping a serial fallback for cycles.

### Project model and resolution

- `src/Sharp/Project.cs` is the main project abstraction: TFM, references, props evaluation, publish settings, and runtime output paths.
- `src/Sharp/Loading/ProjectLoader.cs` and related project graph utilities resolve project structure and references.
- `src/Sharp/BuildPropsEvaluator.cs` and `Directory.Build.props` support matter here because repo-specific analyzers and build settings are often injected through props rather than the raw `.csproj`.

### NuGet / restore / runtime behavior

- `src/Sharp/NuGet/` contains restore logic, package resolution, and package/runtime graph generation.
- `src/Sharp/Publish/` and `src/Sharp/Runtime/` handle publish and run semantics.
- Runtime and reference graph generation intentionally matches `.NET` SDK behavior, including dependency injection for runtime assets and generated deps files.

### Tests and fixtures

- `tests/Sharp.Tests/` contains the main xUnit suite and parity tests.
- The test suite compares `sharp` output to `dotnet` output for CLI commands and snapshots; this is core to the repo’s correctness contract.
- `tests/TestExampleProjects/` and `testProjects/` are fixture projects used to validate SDK parity and edge cases that are deliberately outside the main solution.

## Repo-specific conventions and practices

- Preserve `dotnet` parity. This repo’s core contract is that `sharp` behaves like the .NET SDK in the same scenarios, including command routing, help text, error codes, project selection, and output format.
- When touching build output semantics, verify the relevant parity tests in `tests/Sharp.Tests/` rather than assuming the change is isolated.
- `Directory.Build.props` and eval-driven project references are part of the dependency graph; don’t treat an analyzer or generator reference as “invisible” just because it is not in the raw `.csproj` file.
- Solution builds are purposely ordered into dependency waves; keep build-order assumptions consistent with the graph logic in `SolutionBuild`.
- Build output folders for projects are intentionally shaped to mimic the `dotnet` SDK output layout (`myc/Debug/<tfm>/...`) and not a custom arbitrary location.
- Use the test fixtures and parity snapshots as the ground truth for expected behavior; many tests are designed around exact CLI comparisons and Verify snapshots.
- Keep changes compatible with .NET 10 and the repo’s embedded SDK/runtime payload expectations; update docs or migration guidance when version bumps are involved.

## Practical guidance for future Copilot sessions

- Start with `README.md` for the user-facing contract and `src/Sharp.Cli/Program.cs` for CLI behavior.
- When debugging a compile or project-resolution issue, inspect `ProjectBuild`, `Project`, and `SolutionBuild` together because the code is intentionally split by concern but depends on each other.
- If adding a fixture or changing a runtime aspect, do not forget the restore steps for non-solution fixtures.
- Prefer the smallest relevant test command (`--filter`) over broad suite runs unless the change clearly affects the full build graph.
