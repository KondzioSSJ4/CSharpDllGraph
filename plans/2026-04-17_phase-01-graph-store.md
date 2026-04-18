# Plan: Phase 01 — Graph Schema and JSON Store

> Close phase 01 with verified evidence for deterministic graph storage. Preserve completed schema, store, and query work. Finish blocked validation steps, then halt at the stop gate.

## [x] Task 1: Lock node and edge schema

Goal: Keep the graph primitives stable for every later phase.

Context: Implemented graph types already live in #[[file:src/CSharpDllGraph.Engine/Graph/NodeKind.cs]], #[[file:src/CSharpDllGraph.Engine/Graph/EdgeKind.cs]], #[[file:src/CSharpDllGraph.Engine/Graph/Node.cs]], #[[file:src/CSharpDllGraph.Engine/Graph/Edge.cs]], and #[[file:src/CSharpDllGraph.Engine/Graph/NodeId.cs]].

Acceptance: Node kinds, edge kinds, identifiers, attributes, and source references stay deterministic and version-aware.

- [x] 1.1 Keep `NodeKind` aligned with the phase-01 contract: `Workspace`, `Project`, `Package`, `Assembly`, `Namespace`, `Type`, `Method`, `HttpEndpoint`, `HttpCallSite`, `ExternalRef`
- [x] 1.2 Keep `EdgeKind` aligned with the phase-01 contract: `Contains`, `References`, `DependsOn`, `Implements`, `Inherits`, `Calls`, `Uses`, `HandlesRoute`, `CallsRoute`, `DescribedBy`
- [x] 1.3 Keep `NodeId` stable, versioned, and filesystem-safe in `{kind}:{fqn}@{version-token}` form
- [x] 1.4 Keep `Node`, `Edge`, `SourceRef`, and `SourceSpan` as the base records for graph persistence

## [x] Task 2: Keep deterministic JSON store layout

Goal: Preserve the shard-per-kind workspace store and deterministic serialization.

Context: Store and serializer code already exist in #[[file:src/CSharpDllGraph.Engine/Store/IWorkspaceStore.cs]], #[[file:src/CSharpDllGraph.Engine/Store/JsonWorkspaceStore.cs]], and #[[file:src/CSharpDllGraph.Engine/Graph/Serialization/GraphJsonSerializerOptions.cs]].

Acceptance: Saving the same snapshot twice can produce byte-identical JSON when the environment allows validation.

- [x] 2.1 Keep `IWorkspaceStore` and `JsonWorkspaceStore` as the phase-01 persistence seam
- [x] 2.2 Keep `manifest.json`, `nodes/*.json`, and `edges/*.json` as the persisted workspace layout
- [x] 2.3 Keep atomic write flow based on temp files and rename
- [x] 2.4 Keep deterministic ordering in converters and JSON writer helpers

## [x] Task 3: Keep query layer v0 usable

Goal: Preserve read-side graph queries needed by later providers and MCP tools.

Context: Query code already lives in #[[file:src/CSharpDllGraph.Engine/Query/IGraphQuery.cs]] and #[[file:src/CSharpDllGraph.Engine/Query/InMemoryGraphQuery.cs]].

Acceptance: `GetNode`, `FindNodes`, `GetEdges`, and `Neighbors` remain available over a loaded snapshot with secondary indexes.

- [x] 3.1 Keep `IGraphQuery` contract for node lookup, edge lookup, and neighborhood traversal
- [x] 3.2 Keep the in-memory implementation backed by a loaded workspace snapshot
- [x] 3.3 Keep indexes by kind, display-name prefix, and attribute key
- [x] 3.4 Keep graph ordering helpers deterministic for repeated queries and saves

## [x] Task 4: Finish test evidence for round-trip and stability

Goal: Prove the existing implementation with runnable evidence or a recorded blocker.

Context: Existing tests live in #[[file:tests/CSharpDllGraph.Engine.Tests/GraphFixtureBuilder.cs]], #[[file:tests/CSharpDllGraph.Engine.Tests/NodeSchemaTests.cs]], #[[file:tests/CSharpDllGraph.Engine.Tests/GraphQueryTests.cs]], and #[[file:tests/CSharpDllGraph.Engine.Tests/WorkspaceStoreRoundTripTests.cs]].

Acceptance: Round-trip byte equality and repeat-run stability are both demonstrated, or the exact local blocker is captured with the failing command.

- [x] 4.1 Keep the synthetic multi-version fixture builder under `tests/CSharpDllGraph.Engine.Tests`
- [x] 4.2 Keep the round-trip serialization test for byte equality
- [x] 4.3 Keep the query and schema tests for graph lookup behavior
- [x] 4.4 Ran validation commands. Exact blockers captured: `dotnet test` hits NuGet scratch/config lock errors, local `obj/*.cache` write denial in MSBuild, and `dotnet vstest` against the stale built DLL reports no discoverable tests

## [~] Task 5: Pass the validate-stop gate

Goal: Close phase 01 with signed evidence and then halt.

Acceptance: Validation output proves the phase goal, requirement statuses are aligned, and execution stops before phase 02 starts.

- [ ] 5.1 Prove all phase-01 tests green
- [~] 5.2 Inspected serializer and shard layout. Saved sample JSON inspection still blocked by local execution limits
- [ ] 5.3 Print a signed summary covering shipped work, verification result, and remaining blocker if any
- [ ] 5.4 Halt and wait for explicit human sign-off before opening phase 02

## [x] Task 6: Sync product definition

Goal: Keep product docs aligned with the real phase-01 outcome.

Context: Product docs live in #[[file:docs/PROJECT_DEFINITION.md]] and #[[file:docs/REQUIREMENT_DEFINITION.md]].

- [x] 6.1 Keep the graph-model summary in `docs/PROJECT_DEFINITION.md`
- [x] 6.2 Keep phase-01 requirement statuses updated in `docs/REQUIREMENT_DEFINITION.md`
- [x] 6.3 Re-checked both docs after validation. Product definition unchanged
