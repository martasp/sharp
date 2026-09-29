# NativeCompiler

A self-hosting C# compiler built on Roslyn that compiles `.csproj` projects and `.sln` / `.slnx`
solutions **without MSBuild**, driven by a CLI whose commands, paths, flags, exit codes and output
are shaped to match the .NET SDK's `dotnet`.

## Requirements

- .NET SDK 10 (`10.0.400`), which supplies both Roslyn and the framework ref packs
- Linux or WSL for the AOT publish path; `dotnet` must be on `PATH`

## Build

```
dotnet build NativeCompiler.sln            # debug compiler + native libs + tests
bash publish.sh                            # AOT self-contained CLI at
                                           #   src/NativeCompilerCLI/bin/Release/net10.0/linux-x64/publish/NativeCompilerCLI
```

The test suite is `dotnet test tests/NativeCompilerTests --nologo` (58 tests, incl. byte-for-byte
CLI parity verification against `dotnet`).

## Commands

```
NativeCompilerCLI <command> [target] [-- app args]
```

| Command | Behaviour |
|---|---|
| `build` | compiles the target; identical output/exit code to `dotnet build` |
| `run` | builds then launches the app (apphost when present, else `dotnet <dll>`); forwards everything after `--`; propagates the app exit code |
| `version` / `--version` | prints the hosting SDK version (e.g. `10.0.400`) |
| `help` / `-h` / `--help` | prints the .NET SDK's general help text |
| `NativeCompilerCLI <target>` | legacy form, routed to `build` |

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

```
$ NativeCompilerCLI build <repo>/tests/TestExampleProjects/HelloParityFixture
  HelloParityFixture -> <repo>/tests/TestExampleProjects/HelloParityFixture/myc/Debug/net10.0/HelloParityFixture.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Build a classic `.sln`

A fully-successful `.sln` build prints no per-project lines — an SDK quirk reproduced exactly:

```
$ NativeCompilerCLI build <repo>/tests/TestExampleProjects/SlnFixtures/SlnFixtures.sln

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Build a `.slnx`

Per-project lines are always printed for `.slnx`:

```
$ NativeCompilerCLI build <repo>/tests/TestExampleProjects/SlnxFixtures.slnx
  LibA -> <repo>/tests/TestExampleProjects/SlnFixtures/LibA/myc/Debug/net10.0/LibA.dll
  LibB -> <repo>/tests/TestExampleProjects/SlnFixtures/LibB/myc/Debug/net10.0/LibB.dll
  SlnApp -> <repo>/tests/TestExampleProjects/SlnFixtures/SlnApp/myc/Debug/net10.0/SlnApp.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Build with no target (current-directory search)

```
$ cd <repo>/tests/TestExampleProjects/HelloParityFixture && NativeCompilerCLI build
  HelloParityFixture -> <repo>/tests/TestExampleProjects/HelloParityFixture/myc/Debug/net10.0/HelloParityFixture.dll
  ...

$ cd <repo>/tests/TestExampleProjects && NativeCompilerCLI build        # sole .slnx wins
  LibA -> ...
  ...
```

### Run an app (args after `--` are forwarded)

```
$ NativeCompilerCLI run <repo>/tests/TestExampleProjects/HelloParityFixture
Hello from NativeCompiler!
```

### A failing build (`error CS0103`, echo, exit code 1)

```
$ NativeCompilerCLI build <repo>/tests/TestExampleProjects/FailingFixture
<repo>/tests/TestExampleProjects/FailingFixture/Class1.cs(5,29): error CS0103: The name 'NonExistent' does not exist in the current context [<repo>/tests/TestExampleProjects/FailingFixture/FailingFixture.csproj]

Build FAILED.

<repo>/tests/TestExampleProjects/FailingFixture/Class1.cs(5,29): error CS0103: The name 'NonExistent' does not exist in the current context [<repo>/tests/TestExampleProjects/FailingFixture/FailingFixture.csproj]
    0 Warning(s)
    1 Error(s)     # $? = 1
```

### Version and help

```
$ NativeCompilerCLI --version
10.0.400                  # the hosting SDK version, matching `dotnet --version`
$ NativeCompilerCLI help
...                       # byte-identical to `dotnet help` (SDK version substituted)
```

## Parity contract

Every example above is, after stripping SDK timings (`Time Elapsed`) and restore banners,
**byte-identical** to the matching `dotnet` invocation — including exit codes and the solution
layout quirks (`.sln` success suppresses per-project lines; a failing `.slnx` lists compiled
projects before the errors, a failing `.sln` lists them after). The parity is enforced by
`CliCommandParityTests` (side-by-side Verify snapshots for `build`, `run`,
`--version`, `help`, and `build` over `.sln` / `.slnx` / `.csproj`, with and without a path).

See **`main.md`** for the feature/limitation matrix, the deps.json output-parity work, and the
OrchardCore stress-test results.