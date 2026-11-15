# Embedding the SDK payload: options considered

## The problem

`sharp` has to compile C# **with no .NET SDK installed** — that is the case it exists to serve. To do
that without MSBuild or a ref-pack directory, it needs a copy of the SDK's compile-time data:
~7 MB of reference assemblies, source generators, ILLink analyzers, the apphost template and the
version stamp. That data must travel *inside* the published binary and survive the NativeAOT + trim
publisher.

The question this document answers: **what is the best way to carry ~7 MB of binary data in a .NET
assembly in 2026?** Every option below was prototyped or reasoned through; the one we chose is marked.

## Decision at a glance

| # | Option | Chosen | Why it won or lost |
|---|--------|:------:|--------------------|
| A | Read from the installed SDK at runtime | ✗ | The whole point is to work *without* an SDK |
| B | Source generator reads files and emits them | ✗ | Generators cannot add manifest resources; file I/O trips `RS1035` |
| C | One C# string literal (`\uXXXX`/escaped) | ✗ | `CS8103` at ~8.4 M encoded chars; the payload is ~16 M |
| D | `u8` UTF-8 literal, Base64-decoded | ✗ | Works, but a bigger artifact and a CRLF pitfall |
| E | `ulong[]` / `byte[]` literal | ✗ | Largest source, slowest compile, no upside over C/D |
| F | Big string literal + data-section feature flag | ✗ | Works, but a *preview* compiler feature |
| G | **MSBuild `EmbeddedResource` + staged zip** | **✓** | One build step, native resource, trimmed-safe, cacheable |

The chosen design then splits into sub-decisions, each marked the same way:

| Sub-decision | Chosen | Alternative rejected |
|---|---|---|
| Producer: **C# stager program** | ✓ | MSBuild `<Copy>` + `<Zip>` tasks |
| Host assembly: **dedicated `Dotnet11Dependencies`** | ✓ | Embed in `Sharp` itself |
| Resource name: **`embedded-refs.<refpack>.zip`, discovered at run time** | ✓ | A second, hand-written copy of the version |
| Runtime lookup: **`typeof(Payloads).Assembly`** | ✓ | `Assembly.Load("Dotnet11Dependencies")` |

## Options in detail

### A. Read from the installed SDK (the original behaviour)

The baseline before `5bceda2`: resolve ref assemblies, analyzers, apphost and `dotnet --version` from
an SDK on disk at runtime.

| Pros | Cons |
|---|---|
| Zero build complexity; always matches the installed SDK | **Fails on a machine with no SDK — the exact target scenario** |
| No binary bloat | Version drifts with whatever SDK happens to be present |

Rejected because it defeats the purpose of a self-contained compiler.

### B. Source generator that reads the SDK and emits the payload

A prototype `[EmbedPath("…")]` attribute drove a generator that read each file and emitted it as
generated C#. It compiled end to end.

| Pros | Cons |
|---|---|
| No MSBuild resource plumbing; "just works" with the compiler | **Generators have no API to add manifest resources** — the data can only become source |
| Fits the existing generator-linking pipeline | **`RS1035`**: analyzers/generators must not do file I/O, so the design is a rule violation by construction |
| | Reads the SDK at *build* time of the consumer, reintroducing an SDK dependency for consumers |

Rejected: the resource API does not exist, and the approach is disallowed by analyzer rules.

### C. One C# string literal

Emit the payload as a single escaped string constant.

| Pros | Cons |
|---|---|
| No build machinery at all | **`CS8103`**: a user string is capped at ~8,388,608 encoded chars; the payload is ~16 M |
| Trivially trimmed-safe (it is just code) | Splitting into ~5 chunks clears the limit but balloons to **~90 MB of generated source** |

Rejected: a hard compiler limit, and even when worked around it is an order of magnitude larger source.

### D. `u8` UTF-8 literal, Base64-encoded

Store the zip as a Base64 `u8` literal and decode at runtime.

| Pros | Cons |
|---|---|
| **`u8` literals bypass `CS8103`** — no chunking needed | Artifact larger than the raw zip (Base64 + generation overhead) |
| No MSBuild resource step | **`Base64.DecodeFromUtf8` rejects CRLF-formatted Base64** (LF is tolerated) — a silent, tooling-dependent footgun |
| Compiles in a plain `dotnet build` | Every consumer compiles ~9 MB of source per build |

Rejected: viable, but strictly worse than a real embedded resource, and the line-ending pitfall is
exactly the kind of thing that breaks on a different platform.

### E. `ulong[]` / `byte[]` literal

Pack the bytes into numeric array literals.

| Pros | Cons |
|---|---|
| No string-encoding limits apply | Largest source of all options and the slowest to compile |
| | No benefit over C/D, which share the same "it's just source" shape |

Reasoned through, not pursued: dominated by C and D.

### F. Big string literal + `experimental-data-section-string-literals`

Enable the preview compiler feature so a string literal of this size is allowed to live in the data
section.

| Pros | Cons |
|---|---|
| Big literal works without chunking | **Preview / unsupported feature flag** |
| Reasonably compact binary | Couples the build to a non-final compiler feature |

Rejected: depending on a preview feature for a core build path is not a trade worth making.

### G. MSBuild `EmbeddedResource` + staged zip — **chosen**

A build step produces an SDK-shaped tree, zips it, and embeds the zip as a managed manifest resource.
The runtime extracts it once into a keyed cache.

| Pros | Cons |
|---|---|
| **A real manifest resource** — the canonical way to embed binary data | Needs a build step (the stager) before compile |
| **Trimmed-safe when read via `typeof(...)`** (see below) | ~7 MB in the assembly |
| Zip is built once and cached; runtime pays extraction only on first use | Two artifacts to explain: the tree and the zip |
| Not source — no `CS8103`, no `RS1035`, no preview flags | |
| Versionable: the resource name carries the ref-pack version | |

#### G1. Producer: C# stager vs MSBuild copy/zip — **chose the C# stager**

| | C# `tools/PayloadStager` (**✓**) | MSBuild `<Copy>` + `<Zip>` |
|---|---|---|
| Control (ZipSlip, ordering, version stamp) | Full, testable C# | Awkward across MSBuild task versions |
| Metadata fan-out | Explicit | MSBuild cartesian-metadata behaviour needs one `Copy` per group |
| Risk | — | Fragile task/version coupling |

#### G2. Host assembly: dedicated project vs `Sharp` itself — **chose dedicated**

| | Dedicated `Dotnet11Dependencies` (**✓**) | Embedded in `Sharp` |
|---|---|---|
| `Sharp.csproj` | One `ProjectReference`; no Exec/EmbeddedResource | Carries the Exec + EmbeddedResource + stager args |
| Resource lookup | Lives in one place (`Payloads`), cannot be re-implemented elsewhere | Spread through the compiler assembly |
| Downside | One extra project; when sharp compiles itself, `Output.WriteDll` must embed the payload resource into that project's assembly (it does) | — |

#### G3. Resource name: versioned vs not — **chose versioned, discovered**

| | `embedded-refs.<refpack>.zip` (**✓**) | Unversioned `embedded-refs.zip` |
|---|---|---|
| Tells at a glance which SDK the payload came from | Yes | No |
| Several SDK payloads side by side | No name collision | Collides |
| Number of places the version is written | **One file** — the pinned literal in `src/Dotnet11Dependencies/Dotnet11Dependencies.csproj`. The runtime *discovers* the embedded name (`Payloads.ZipResourceName`), so there is no second copy to drift | One (a fixed literal) |

A first attempt hardcoded `embedded-refs.<refpack>.zip` in `Payloads.cs` **and** derived the MSBuild
`LogicalName` from the SDK, and broke CI: `setup-dotnet` installs a newer `11.0.x` patch than the
developer's SDK, so MSBuild embedded the resource under its own refpack version while the constant
asked for the write-in one. The build stayed green; every payload-dependent test failed at run time
with "resource is missing".

The fix is **not** to drop the version - it is to drop the *second copy*. `Payloads` looks up the one
resource matching `embedded-refs.*.zip` in its own assembly, so whatever version the building SDK
embedded is, by construction, the version the runtime asks for. When sharp compiles itself there is no
MSBuild to embed the resource, so `Output.WriteDll` embeds the same versioned payload into the
`Dotnet11Dependencies` assembly it emits. Only the hand-written constant is gone.

#### G4. Runtime lookup: `typeof` vs `Assembly.Load` — **chose `typeof`**

| | `typeof(Payloads).Assembly` (**✓**) | `Assembly.Load("Dotnet11Dependencies")` |
|---|---|---|
| JIT (`dotnet build`) | Works | Works |
| **Published NativeAOT / single-file** | Works — the assembly is rooted | **`FileNotFoundException`, exit 134** — a by-name load is invisible to the trimmer, so the assembly is never rooted and its resources are trimmed away |

This is the single most important invariant in the design. The failure is invisible under
`dotnet build` and only appears in the shipped binary.

## What we chose

**Option G**, built as: a C# stager (`tools/PayloadStager`) invoked by a dedicated
`Dotnet11Dependencies` project, embedding a versioned `embedded-refs.<refpack>.zip` manifest resource,
read at runtime through `Payloads.LoadZip()`, which resolves the asset with
`typeof(Payloads).Assembly` and discover the resource name rather than comparing it against a
hand-written constant.

### Where the data comes from

The payload *contents* are copied from pinned Microsoft-published NuGet packages
(`src/Dotnet11Dependencies/Dotnet11Dependencies.csproj`), never from the installed SDK. The targeting,
runtime and host packs are obtained with `PackageDownload` — they are packageType `DotnetPlatform`,
which NuGet refuses as a `PackageReference` with `NU1213` — and DependencyModel / ILLink with
`PackageReference`. The stager reads them out of the NuGet global packages folder and lays them out in
the SDK shape. Only the reported SDK version is still taken from the building SDK, because
`sharp --version` must match that machine's `dotnet` (see `UPGRADING.md`).

## How the payload is embedded and read (JIT vs NativeAOT)

The payload is not a file next to the compiler. It is a manifest resource inside
`Dotnet11Dependencies.dll`, and both build paths and both runtime modes go through the same
`Payloads.LoadZip()` API.

### Production path 1: MSBuild (`dotnet build` / `dotnet publish`)

1. The `StageEmbeddedRefsPayload` target in `src/Dotnet11Dependencies/Dotnet11Dependencies.csproj`
   runs `tools/PayloadStager`, which writes `obj/payload/embedded-refs.<refpack>.zip`.
2. The same target adds
   `<EmbeddedResource Include="..." LogicalName="embedded-refs.<refpack>.zip" />`.
3. Roslyn writes the zip into the PE manifest-resource table of `Dotnet11Dependencies.dll`.

This is the path used by a normal `dotnet build`, by the release AOT seed, and by the test host.

### Production path 2: sharp compiling itself

Sharp does not execute MSBuild targets, so MSBuild cannot embed the resource when sharp rebuilds
`Dotnet11Dependencies.csproj`. `src/Sharp/Emit/Output.cs` performs the equivalent step:

1. It checks whether the project being emitted is the payload owner:
   `AssemblyName == typeof(Payloads).Assembly.GetName().Name`.
2. If so, it adds a `ResourceDescription` to the emit whose name is `Payloads.ZipResourceName` and
   whose stream factory is `Payloads.LoadZip()`.
3. `CSharpCompilation.Emit(..., manifestResources: ...)` writes that resource into the new
   `Dotnet11Dependencies.dll`.

The stream factory is evaluated during emit, so the running compiler copies its own already-embedded
payload into the assembly it is producing. The new DLL grows by the size of the zip (about 8.9 MB for
the current payload) and carries the same manifest resource as an MSBuild-built one.

This is why a self-built compiler is self-sufficient: neither the produced
`Dotnet11Dependencies.dll` nor the final AOT image needs a sibling `embedded-refs.*.zip` file.

### Consumption path 1: JIT

`Payloads.LoadZip()` calls
`typeof(Payloads).Assembly.GetManifestResourceStream(Payloads.ZipResourceName)`. The JIT runtime reads
the resource directly from the DLL's PE manifest. The name is discovered with
`GetManifestResourceNames()` (the single `embedded-refs.*` entry), so the producer and consumer cannot
disagree about the version. `EmbeddedSdkRefs.ReadEmbeddedPayload` copies the stream into memory, and
`EnsureExtracted` extracts it once into the content-addressed cache.

### Consumption path 2: NativeAOT

`NativeAotPublisher` passes the self-built `Dotnet11Dependencies.dll` to `ilc` as a normal reference
alongside the other application DLLs. ILC reads the manifest resources from that DLL and writes them
into the native image. Two things make the lookup survive:

- `typeof(Payloads)` roots `Dotnet11Dependencies`; `Assembly.Load("Dotnet11Dependencies")` would be
  invisible to the trimmer, and the resource would be removed with the unreferenced assembly.
- `Payloads.LoadZip()` is a direct call, so the resource read is reachable from `Main`.

`Assembly.Location` is empty in a NativeAOT image, but that no longer matters: no code searches the
filesystem for the payload. `GetManifestResourceStream` is served from the native image and feeds the
same extraction cache as JIT.

There is no sidecar in either production path or either consumption path. The build output contains no
payload zip; the only payload is the manifest resource.

### Failure mode to avoid

An old AOT seed built before the self-host embedding path existed will compile `Dotnet11Dependencies`
without the resource. The next generation then fails at startup with:

```
No 'embedded-refs.*.zip' payload resource is embedded in Dotnet11Dependencies.
```

After changing `Output.WriteDll` or the payload owner, rebuild the seed
(`dotnet publish src/Sharp.Cli/Sharp.Cli.csproj -c Release -r linux-x64`) before running
`BootstrapPipelineTests`.

## Where it lives

| File | Role |
|---|---|
| `tools/PayloadStager/Program.cs` | Builds the SDK-shaped tree, writes version stamps, zips it |
| `src/Dotnet11Dependencies/Dotnet11Dependencies.csproj` | Runs the stager, embeds the zip |
| `src/Dotnet11Dependencies/Payloads.cs` | Resource-name discovery; the AOT-safe resource API |
| `src/Sharp/EmbeddedSdkRefs.cs` | Extracts to the cache; version resolution |
| `src/Sharp/Emit/Output.cs` | Embeds the payload when sharp compiles its payload-owning project |

## Invariants and traps

1. **Read the resource via `typeof`, never by name.** Otherwise the AOT trimmer drops it (G4).
2. **The stager locates its inputs and output by working directory alone.** The staging target sets
   `WorkingDirectory="$(MSBuildProjectDirectory)"` and the stager reads `obj/project.assets.json` and
   writes `obj/payload` relative to it — a fixed path, not `$(IntermediateOutputPath)`, which is empty
   at project scope and configuration-dependent. Without the working directory the child inherits the
   invocation directory (the repo root), finds no assets file, and stages nothing.
3. **The zip must sit beside the tree it archives, never inside it.** `ZipFile.CreateFromDirectory`
   would otherwise try to read the archive it is writing. The tree goes in `obj/payload/embedded-refs/`
   with the zip at `obj/payload/embedded-refs.<refpack>.zip`.
4. **Never write the resource name by hand — discover it.** The embedded name carries the ref pack
   version that came from whoever built the assembly; the runtime looks up `embedded-refs.*.zip` in
   its own resources instead of comparing against a constant. A hand-written copy is the drift that
   broke CI (G3).
5. **The compiler TFM and the staged ref pack must move together.** A mismatch is invisible to
   `dotnet build` and only fails at runtime. See `UPGRADING.md`.
6. **Self-hosting embeds the payload, never a sidecar.** When sharp compiles `Dotnet11Dependencies`
   itself, `Output.WriteDll` adds the payload as a manifest resource using the name discovered from
   the running compiler. The host assembly matters (G2): the resource must live in the assembly that
   `Payloads` resolves through `typeof`.

> Prototype figures above (Base64 source size, chunk counts, binary sizes) were measured in throwaway
> experiments, not reproduced in this repository; they are directional. The design itself is enforced
> by `EmbeddedPayloadTests`, `BootstrapPipelineTests` and `NoDotnetSdkTests`.
