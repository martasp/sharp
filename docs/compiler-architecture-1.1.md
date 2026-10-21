# Sharp Compiler Architecture — v1.1

Intent-based vertical-slice layout of the `NativeCompiler` engine, the `NativeCompilerCLI`
command layer, and the reference/NuGet support code.

**v1.1** reflects the refactor that turned the single `NativeCompiler.cs` god class into
one class per responsibility, with the code physically moved (not wrapped).

---

## 1. Top level — command layer and engine entry points

```mermaid
flowchart TB
    subgraph CLI["NativeCompilerCLI"]
        Program["Program.cs<br/>command registry"]
        CmdBuild["build"]
        CmdRun["run"]
        CmdRestore["restore"]
        CmdClean["clean"]
    end

    subgraph ENGINE["NativeCompiler (thin facade)"]
        NC["NativeCompiler.Build<br/>(→ ProjectBuild)"]
        NCSol["NativeCompiler.BuildSolution<br/>(→ SolutionBuild)"]
        NCRun["NativeCompiler.Run<br/>(→ Runtime)"]
    end

    Program --> CmdBuild & CmdRun & CmdRestore & CmdClean
    CmdBuild -->|".sln/.slnx"| NCSol
    CmdBuild -->|"project"| NC
    CmdRun --> NCRun
    CmdRestore --> Restorer["PackageRestorer"]
```

`NativeCompiler.cs` is now only 42 lines: the three public entry points plus the
`BuildResult` / `SolutionProjectResult` / `BuildSolutionResult` records. All implementation
lives in the slice modules below.

---

## 2. Engine slices — one class per responsibility

```mermaid
flowchart TB
    subgraph BUILD["Build slice"]
        PB["ProjectBuild<br/>single project + cycle guard"]
        SB["SolutionBuild<br/>wave scheduler"]
    end

    subgraph MODEL["Model + graph"]
        PL["ProjectLoader<br/>read + parse"]
        PG["ProjectGraph<br/>enumerate projects"]
        BO["BuildOrder<br/>(inside SolutionBuild)"]
    end

    subgraph REFS["Reference slice"]
        R["References<br/>GetCompilingRefs / GetRuntimeRefs"]
        FF["FrameworkFix<br/>Merge (keep HIGHEST version)"]
    end

    subgraph EMIT["Output slice"]
        OUT["Output<br/>WriteDll / RunAssembly"]
        Deps["DepsJsonWriter"]
    end

    subgraph RUNTIME["Runtime slice"]
        RT["Runtime.Run"]
    end

    SB --> BO --> PB
    PB --> PL
    PB --> R --> FF
    PB --> OUT --> Deps
    RT --> PB
    RT --> OUT
```

- `Build/ProjectBuild.cs` — full `Build`/`BuildCore` body, `BuildsInProgress`, `WriteFiles`.
- `Build/SolutionBuild.cs` — full `BuildSolution` body, `ResolveBuildOrder`,
  `AddAnalyzerProjectReferences`, `TopologicalSort` (private).
- `Emit/Output.cs` — real Roslyn `WriteDll` (`ProduceDll`) and `RunAssembly` bodies.
- `References/FrameworkFix.cs` — real `MergeFrameworkSuppression` (moved out of
  `NativeCompilerUtils.cs`).
- `References/References.cs` — `GetCompilingRefs` / `GetRuntimeRefs`.
- `Loading/ProjectLoader.cs` — file-read + model parse.
- `Graph/ProjectGraph.cs` — solution/project enumeration (used by CLI `clean`).
- `Runtime/Runtime.cs` — build + run orchestration.

---

## 3. Single-project build pipeline (`ProjectBuild.Core`)

```mermaid
flowchart LR
    Load["ProjectLoader.Load"]
    Parse["ParseProject<br/>syntax trees"]
    CompRefs["References.GetCompilingRefs"]
    Compile["CSharpCompilation.Create"]
    Gen["AnalyzerRunner.Run<br/>generators + analyzers"]
    Shim["NeedsTestEntryPointShim"]
    Emit["Output.WriteDll"]
    Deps["DepsJsonWriter.Write"]
    Copy["WriteFiles<br/>sharp-bin + native assets"]

    Load --> Parse --> Compile
    CompRefs --> Compile --> Gen --> Shim --> Emit --> Deps --> Copy
```

`Build` wraps `Core` in a try/catch so a resolution failure is reported as a failed
`BuildResult` instead of an unhandled exception (SIGABRT). The `BuildsInProgress` dictionary is
the cycle guard for re-entrant builds.

---

## 4. Solution build — dependency waves

```mermaid
flowchart LR
    Sol["SolutionFile.Parse"]
    Resolve["ResolveBuildOrder"]
    Props["BuildPropsEvaluator<br/>analyzer project refs"]
    Kahn["TopologicalSort (Kahn)"]
    Wave["Parallel.ForEach per wave"]
    Cache["results cache"]

    Sol --> Resolve --> Kahn
    Resolve --> Props --> Resolve
    Kahn --> Wave --> Cache
```

`ResolveBuildOrder` walks raw `<ProjectReference>` items plus `BuildPropsEvaluator` analyzer
references (self-edges skipped — the evaluator ignores `Condition`). Kahn's algorithm groups
ready projects into waves; every member of a wave can build concurrently because no wave member
references another. Cycles are still emitted so every project gets reported.

---

## 5. Clean — independent of the build pipeline

```mermaid
flowchart LR
    Clean["CLI clean"]
    PG["ProjectGraph.GetProjectDirectories"]
    Targets["CleanTargets<br/>sharp-bin / generated"]

    Clean --> PG --> Targets
```

`clean` resolves project roots through `ProjectGraph` (solution enumeration only). It never
invokes `Build`, so cleaning works even for projects that no longer compile.

---

### Build semantics recap

- `ProjectBuild.Build(path)` recursively resolves and builds referenced projects on demand.
- `SolutionBuild.Build(solution)` parses the `.sln/.slnx`, plans a dependency-ordered wave
  schedule, and builds each wave's projects concurrently via a thread-safe per-solution cache.
- Reference dedup keeps the **highest** assembly version on a simple-name clash (`FrameworkFix`).
