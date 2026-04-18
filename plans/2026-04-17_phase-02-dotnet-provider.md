# Plan: Phase 02 — .NET Provider

> Build the structural .NET provider on top of the graph store from phase 01. Emit package, assembly, namespace, type, and method graph data from deterministic fixture inputs, then stop at the gate.

## [x] Task 1: Resolve projects and package graphs

Goal: Discover solution projects and resolved NuGet dependencies per project and TFM.

Context: Graph primitives and store contracts already exist in #[[file:src/CSharpDllGraph.Engine/Graph/Node.cs]], #[[file:src/CSharpDllGraph.Engine/Graph/Edge.cs]], and #[[file:src/CSharpDllGraph.Engine/Store/JsonWorkspaceStore.cs]]. New provider code should land under `src/CSharpDllGraph.Providers.Dotnet/`.

Acceptance: Given a fixture `.slnx`, the provider emits `Project` and `Package` nodes plus `DependsOn` edges with enough metadata to distinguish TFMs.

- [x] 1.1 Parse the fixture `.sln` or `.slnx` file and enumerate every project path
- [x] 1.2 Read `packages.lock.json` or `obj/project.assets.json` per project
- [x] 1.3 Build the resolved dependency graph for direct and transitive packages per TFM
- [x] 1.4 Emit `Project` nodes, `Package@version` nodes, and `DependsOn` edges with TFM metadata when needed

## [x] Task 2: Walk public surface from extracted `.nupkg` assemblies

Goal: Turn resolved package binaries into structural graph nodes and edges.

Context: The provider will extend the phase-01 node kinds already locked in #[[file:src/CSharpDllGraph.Engine/Graph/NodeKind.cs]] and #[[file:src/CSharpDllGraph.Engine/Graph/EdgeKind.cs]].

Acceptance: For every public symbol in fixture packages, the provider emits `Assembly`, `Namespace`, `Type`, and `Method` nodes plus `Contains`, `Implements`, and `Inherits` edges where resolvable.

- [x] 2.1 Resolve extracted package folders from the assets data
- [x] 2.2 Scan `lib/<tfm>/*.dll` and emit versioned `Assembly` nodes
- [x] 2.3 Walk metadata and emit public `Namespace`, `Type`, and `Method` nodes while skipping hidden or compiler-generated members
- [x] 2.4 Emit `Contains` edges from package to assembly to namespace to type to method
- [x] 2.5 Record method signatures and type relationships; use `ExternalRef` when the target type is outside the known graph

## [x] Task 3: Enforce multi-version coexistence rules

Goal: Keep multiple package versions side by side without structural collisions.

Context: `NodeId` already embeds a version token in #[[file:src/CSharpDllGraph.Engine/Graph/NodeId.cs]].

Acceptance: Fixture data containing two versions of the same package yields isolated structural subgraphs with no illegal cross-version containment.

- [x] 3.1 Propagate package version tokens to every descendant node under that package
- [x] 3.2 Forbid cross-version `Contains` edges
- [x] 3.3 Add a fixture covering two versions of the same package
- [x] 3.4 Assert the provider keeps both versions present and separate

## [x] Task 4: Add provider seam and engine pipeline hook

Goal: Create the stable provider contract for later graph sources.

Context: Existing engine code lives under `src/CSharpDllGraph.Engine/`. The .NET provider project exists at #[[file:src/CSharpDllGraph.Providers.Dotnet/CSharpDllGraph.Providers.Dotnet.csproj]].

Acceptance: The engine can iterate registered providers and commit their graph fragments without hard-coding .NET-specific logic into later phases.

- [x] 4.1 Add `IGraphProvider` returning `IAsyncEnumerable<GraphFragment>`
- [x] 4.2 Implement `DotnetProvider` in `src/CSharpDllGraph.Providers.Dotnet/`
- [x] 4.3 Add engine orchestration that consumes provider fragments and persists them through the workspace store
- [x] 4.4 Document the reserved provider subprocess protocol shape on the interface without implementing it yet

## [x] Task 5: Create deterministic fixtures and integration tests

Goal: Prove the provider against checked-in offline inputs.

Context: Existing test project shell exists at #[[file:tests/CSharpDllGraph.Providers.Dotnet.Tests/CSharpDllGraph.Providers.Dotnet.Tests.csproj]].

Acceptance: A committed fixture solution with locked package inputs produces stable node counts and structural edges in an integration test.

- [x] 5.1 Add `tests/Fixtures/SampleSolution/` with two projects and deterministic package inputs
- [x] 5.2 Check in `packages.lock.json` and any needed sample package artifacts for offline execution
- [x] 5.3 Add an integration test project flow that runs `DotnetProvider` against the fixture
- [x] 5.4 Assert expected node kinds, edge kinds, and multi-version coexistence behavior

## [x] Task 6: Pass the validate-stop gate

Goal: Close phase 02 with proof and halt before phase 03.

Acceptance: Structural graph output is verified against the fixture and no usage edges exist yet.

- [x] 6.1 Prove the fixture integration test is green
- [x] 6.2 Prove the multi-version fixture keeps both package versions
- [x] 6.3 Verify usage edges are intentionally absent in this phase
- [x] 6.4 Print a signed summary and halt for human sign-off

## [x] Task 7: Sync product definition

Goal: Keep product docs aligned with the structural-provider phase outcome.

Context: Update #[[file:docs/PROJECT_DEFINITION.md]] and #[[file:docs/REQUIREMENT_DEFINITION.md]] after implementation and validation.

- [x] 7.1 Review `docs/PROJECT_DEFINITION.md` against new provider behavior
- [x] 7.2 Update touched phase-02 requirements in `docs/REQUIREMENT_DEFINITION.md`
- [x] 7.3 Respond with `Product definition: updated` or `Product definition: no update required`
