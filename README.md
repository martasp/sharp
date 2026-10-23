# Sharp

A self-hosting C# compiler built on Roslyn that compiles `.csproj` projects and `.sln` / `.slnx`
solutions **without MSBuild**, driven by a CLI whose commands, paths, flags, exit codes and output
are shaped to match the .NET SDK's `dotnet`.

## Requirements

- .NET SDK 10 (`10.0.400`), which supplies both Roslyn and the framework ref packs
- Linux or WSL for the AOT publish path; `dotnet` must be on `PATH`

## Build

```
dotnet build Sharp.sln            # debug compiler + native libs + tests
bash publish.sh                            # AOT self-contained CLI at
                                           #   src/Sharp.Cli/bin/Release/net10.0/linux-x64/publish/Sharp.Cli
```

The test suite is `dotnet test tests/Sharp.Tests --nologo` (58 tests, incl. byte-for-byte
CLI parity verification against `dotnet`).

## Commands

```
sharp <command> [target] [-- app args]
```

| Command | Behaviour |
|---|---|
| `build` | compiles the target; identical output/exit code to `dotnet build` |
| `run` | builds then launches the app (apphost when present, else `dotnet <dll>`); forwards app args like `dotnet run` (everything after the target, and everything after `--`); propagates the app exit code |
| `version` / `--version` | prints the hosting SDK version (e.g. `10.0.400`) |
| `help` / `-h` / `--help` | prints the .NET SDK's general help text; the SDK command list is annotated `[implemented]` / `[not implemented]` (derived from the command registry) and the version line comes from the hosting SDK |
| `sharp <target>` | legacy form, routed to `build` |

`<target>` may be:

- a project **folder** — builds the single `.csproj` inside it,
  output to `<proj>/myc/<config>/<tfm>/`
- a `.csproj` **file** path
- a `.sln` / `.slnx` **solution** path — every project built in dependency order, all failures
  reported at the end, exit code `1` if any failed
- **omitted** — the current directory is searched (exactly like `dotnet`):
  * no project/solution → `MSBUILD : error MSB1003: Specify a project or solution file. ...`
  * more than one → `MSBUILD : error MSB1011: Specify which project or solution file to use ...`
  * `run` additionally rejects `.sln`/`.slnx` with `Couldn't find a project to run. ... --project.`

Flags the compiler does not act on (`--no-restore`, `--no-incremental`, `--nologo`, `-c`,
`-v`, ...) are accepted and ignored, mirroring `dotnet`.

## Examples

Captured output, `<repo>` = absolute path to this repo.

### Build a single project (by folder or `.csproj` file)

Uses the SDK terminal-logger summary, seconds included, path relative to the current directory:

```
$ cd <repo> && sharp build tests/TestExampleProjects/HelloParityFixture
  HelloParityFixture net10.0 succeeded (0.4s) → tests/TestExampleProjects/HelloParityFixture/myc/Debug/net10.0/HelloParityFixture.dll

Build succeeded in 1.2s
```

### Build a solution (`.sln` / `.slnx`)

Solution builds use the same terminal-logger grammar as a single-project build: one status line per
project in dependency order, each with the measured seconds and the output path relative to the
current directory, then the summary. Warning-severity diagnostics are listed indented under their
project; info-severity diagnostics stay hidden, exactly like the SDK at default verbosity.

```
$ sharp build <repo>/tests/TestExampleProjects/SlnFixtures/SlnFixtures.sln
  LibA net10.0 succeeded (0.1s) → tests/TestExampleProjects/SlnFixtures/LibA/myc/Debug/net10.0/LibA.dll
  LibB net10.0 succeeded (0.1s) → tests/TestExampleProjects/SlnFixtures/LibB/myc/Debug/net10.0/LibB.dll
  SlnApp net10.0 succeeded (0.2s) → tests/TestExampleProjects/SlnFixtures/SlnApp/myc/Debug/net10.0/SlnApp.dll

Build succeeded in 0.4s
```

A failing project reports `failed with N error(s) (Xs)` with its errors underneath, and the
summary becomes `Build failed with N error(s) in Xs` (exit code 1).

Known gap: the SDK also counts NuGet **restore** warnings (e.g. `NU1510`) in that summary; our
restore is silent, so a project whose only warnings come from NuGet reports `Build succeeded in
Xs` where the SDK would say `Build succeeded with N warning(s) in Xs`.

### Build with no target (current-directory search)

```
$ cd <repo>/tests/TestExampleProjects/HelloParityFixture && sharp build
  HelloParityFixture net10.0 succeeded (0.4s) → myc/Debug/net10.0/HelloParityFixture.dll

Build succeeded in 1.2s

$ cd <repo>/tests/TestExampleProjects && sharp build        # sole .slnx wins
  LibA -> ...
  ...
```

### Run an app (args after `--` are forwarded)

```
$ sharp run <repo>/tests/TestExampleProjects/HelloParityFixture
Hello from Sharp!
```

### A failing build (`error CS0103`, exit code 1)

```
$ sharp build <repo>/tests/TestExampleProjects/FailingFixture
  FailingFixture net10.0 failed with 1 error(s) (0.6s)
    <repo>/tests/TestExampleProjects/FailingFixture/Class1.cs(5,29): error CS0103: The name 'NonExistent' does not exist in the current context

Build failed with 1 error(s) in 1.2s     # $? = 1
```

### Version and help

```
$ sharp --version
10.0.400                  # the hosting SDK version, matching `dotnet --version`
$ sharp help
.NET SDK (10.0.400)       # correct SDK version, never hardcoded
...
SDK commands:
  build             Build a .NET project.                                                     [implemented]
  clean             Clean build outputs of a .NET project.                                    [not implemented]
  ...               # every command marked against the command registry
```

## Parity contract

Every `build`/`run` example above is verified against the matching `dotnet` invocation. `build` uses
the SDK's terminal-logger summary: the SDK's own terminal logger is forced on
(`MSBUILDTERMINALLOGGER=on`) in the tests and the run-varying seconds are normalized to a `<secs>`
token. Single-project output is compared byte-for-byte; solution output is compared on the
deterministic surface only (exit code plus the `Build succeeded`/`Build failed` verdict), because
the SDK's own solution rendering is build-state dependent — it drops per-project lines and the
summary's timing clause when every project is up-to-date. Our full solution layout is pinned by
Verify snapshots. `help` and `--version`
deliberately add value: `--version` echoes the hosting SDK version and `help` annotates the
command list with `[implemented]`/`[not implemented]`, both derived at runtime rather than
hardcoded. The parity is enforced by `CliCommandParityTests` and `CliComparisonTests`
(side-by-side Verify snapshots for `build`, `run`, `--version`, `help`, and `build` over
`.sln` / `.slnx` / `.csproj`, with and without a path).

See **`main.md`** for the feature/limitation matrix, the deps.json output-parity work, and the
OrchardCore stress-test results. See **`PAYLOAD.md`** for how the embedded SDK payload is packaged
and the options that were considered, and **`UPGRADING.md`** for moving to a new .NET major.