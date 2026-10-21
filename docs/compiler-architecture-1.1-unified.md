# Sharp Compiler Architecture — v1.1 (unified)

One diagram showing the hierarchy: CLI is a thin wrapper over the `NativeCompiler` core library. The core library exposes the same command surface (`Build`, `BuildSolution`, `Run`, `RestoreProject`/`RestoreSolution`, `Clean`) so it can be consumed as a library without depending on the CLI. Implementation lives in intent-based vertical slices.

```mermaid
flowchart TB
    subgraph CLI["NativeCompilerCLI (thin wrapper)"]
        Program["Program.cs<br/>command registry, arg parsing"]
        CmdBuild["build"]
        CmdRun["run"]
        CmdRestore["restore"]
        CmdClean["clean"]
    end

    subgraph CORE["NativeCompiler (core library)"]
        Facade["NativeCompiler facade<br/>Build / BuildSolution / Run<br/>RestoreProject/RestoreSolution / Clean"]
        PB["ProjectBuild<br/>Build / BuildCore / cycle guard"]
        SB["SolutionBuild<br/>BuildSolution + ResolveBuildOrder<br/>AddAnalyzerProjectRefs + TopologicalSort<br/>wave scheduler"]
        PL["ProjectLoader<br/>Load (read/parse)"]
        PG["ProjectGraph<br/>IsSolution/GetProjectPathsFromSolution<br/>GetProjectDirectories"]
        R["References<br/>GetCompilingRefs / GetRuntimeRefs"]
        FF["FrameworkFix<br/>MergeFrameworkSuppression<br/>(keep HIGHEST version)"]
        OUT["Output<br/>WriteDll / RunAssembly"]
        RT["Runtime.Run"]
        Cleaner["Cleaner<br/>Clean(target, config)"]
        PR["PackageRestorer<br/>RestoreProject/RestoreSolution"]
        DEPS["DepsJsonWriter"]
        PROJ["Project model<br/>BuildPropsEvaluator / ProjectReferenceFinder"]
    end

    subgraph ANALYZERS["Generators/analyzers"]
        AR["AnalyzerRunner"]
        SG["SourceGeneratorRunner"]
        AZREG["AnalyzerRegistry"]
        RZ["RazorSupport"]
    end

    subgraph NUGET["NuGet"]
        NP["NuGetPackage / VersionResolver"]
    end

    Program --> CmdBuild & CmdRun & CmdRestore & CmdClean
    CmdBuild -->|project| Facade
    CmdBuild -->|sln/slnx| Facade
    CmdRun --> Facade
    CmdRestore --> Facade
    CmdClean --> Facade

    Facade --> PB
    Facade --> SB
    Facade --> RT
    Facade --> Cleaner
    Facade --> PR

    SB --> PG
    SB --> PB
    PB --> PL
    PB --> R --> FF
    PB --> OUT --> DEPS
    PB --> PROJ
    RT --> PB
    RT --> OUT
    Cleaner --> PG

    PR --> NP
    PB --> AR
    AR --> SG
    SG --> AZREG
    SG --> RZ
    AR --> AZREG
```

Notes:
- `Clean` moved into the core (`Cleaner.Clean`) so both CLI and library consumers can clean project outputs. CLI only formats/parses; the logic lives under `NativeCompiler.Cleaner`.
- Restore is part of the core (`PackageRestorer`) and exposed via the `NativeCompiler` facade; CLI delegates. Diagram shows restore under the core, not a separate detached box.
- Solution builds use wave scheduling (Kahn) with analyzer project references (self-edges skipped); single-project builds have a cycle guard and return a failed `BuildResult` on resolution errors.
- Reference dedup keeps the **highest** assembly version on simple-name clashes (`FrameworkFix`).
