# Plan: Phase 02 — .NET Provider (structural)

> Produce package, assembly, type, and method nodes plus structural edges from a .sln + resolved .nupkg set. No user-code usage edges yet.

Prerequisite: phase 01 complete and signed off.

## [ ] Task 1: Package resolution

Goal: Resolve exact package versions per project from NuGet lock or assets file.

Context: `packages.lock.json` is authoritative when present; otherwise fall back to `obj/project.assets.json` produced by `dotnet restore`.

- [ ] 1.1 Locate `packages.lock.json` or `obj/project.assets.json` per project
- [ ] 1.2 Parse the resolved dependency graph (direct + transitive, per TFM)
- [ ] 1.3 Emit `Project` node (versioned by project name + commit or timestamp), `Package@version` nodes, `DependsOn` edges
- [ ] 1.4 For multi-TFM projects tag edges with TFM in attributes

## [ ] Task 2: .nupkg DLL public surface walker

Goal: For every resolved package, open the extracted .nupkg from the NuGet cache and emit public types/methods.

Context: Use `System.Reflection.Metadata` + `PEReader` (fast, no loader-context pollution). Packages live at `%USERPROFILE%\.nuget\packages\<id>\<version>\`.

- [ ] 2.1 Resolve extracted-package folder path from the asset file
- [ ] 2.2 For every `lib/<tfm>/*.dll`: emit `Assembly@version` node with TFM in attributes
- [ ] 2.3 Walk metadata tables, emit public `Namespace`, `Type`, `Method` nodes; skip `internal`, `private`, compiler-generated, `<Module>`
- [ ] 2.4 Emit `Contains` edges down the tree: Package → Assembly → Namespace → Type → Method
- [ ] 2.5 Capture method signatures (param types, return type) as attributes — symbol IDs where resolvable, else raw metadata strings
- [ ] 2.6 Emit `Implements` / `Inherits` edges; target may reference a type outside this package (use `ExternalRef` node if not resolvable inside the graph)

## [ ] Task 3: Multi-version coexistence

Goal: Two versions of the same package produce two distinct, non-colliding subtrees.

Acceptance: Fixture with a solution referencing `Newtonsoft.Json 12.x` and `Newtonsoft.Json 13.x` in different projects yields two separate subtrees, both independently queryable.

- [ ] 3.1 Version token is part of every `NodeId` under a package — propagates to assembly, namespace, type, method
- [ ] 3.2 Forbid cross-version `Contains` edges; `Uses` edges across versions deferred to phase 03 where they are explicitly allowed
- [ ] 3.3 Integration test with the two-version fixture

## [ ] Task 4: Provider protocol seam

Goal: Internal contract positioned so a future non-C# provider can plug in later (not shipped in v1).

- [ ] 4.1 `IGraphProvider` with `DiscoverAsync(workspaceRoot, ct)` returning `IAsyncEnumerable<GraphFragment>` (fragments = batched nodes + edges)
- [ ] 4.2 `DotnetProvider` implements `IGraphProvider`
- [ ] 4.3 Engine pipeline iterates registered providers and commits fragments to the store incrementally
- [ ] 4.4 Document the expected subprocess JSON protocol in an `<seealso>` comment on `IGraphProvider` — shape only, no implementation

## [ ] Task 5: Fixtures and tests

Goal: A small, self-contained fixture solution used by this and later phases.

- [ ] 5.1 Create `tests/Fixtures/SampleSolution/` with 2 projects, 2 nugets (one shared across both projects, one unique)
- [ ] 5.2 Check in `packages.lock.json` so tests are deterministic offline
- [ ] 5.3 Integration test: run `DotnetProvider` against the fixture, assert expected node counts and edge kinds

## [ ] Task 6: Sync product definition

- [ ] 6.1 Update `PROJECT_DEFINITION.md` / `REQUIREMENT_DEFINITION.md`
- [ ] 6.2 Respond with status line

## VALIDATE-STOP CHECKLIST

- [ ] Fixture integration test green; expected structural graph produced
- [ ] Multi-version fixture proves coexistence
- [ ] No usage edges yet (verify intentionally absent)
- [ ] Print signed summary
- [ ] **HALT. Do not open `phase-03-usage-edges.md`. Wait for sign-off.**
