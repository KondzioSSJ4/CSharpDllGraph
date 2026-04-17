# Plan: Phase 01 — Graph Schema + JSON Store

> Define a versioned node/edge schema and a diff-friendly per-workspace JSON store with byte-stable round-trip.

Prerequisite: phase 00 complete and signed off.

## [x] Task 1: Node and edge schema

Goal: Typed C# model for graph entities. Every versionable entity carries its version in the identifier.

Acceptance: Unit test constructs a node of each kind; identifiers are deterministic, filesystem-safe, and include version where applicable.

- [x] 1.1 Define `NodeKind` enum: `Workspace, Project, Package, Assembly, Namespace, Type, Method, HttpEndpoint, HttpCallSite, ExternalRef`
- [x] 1.2 Define `EdgeKind` enum: `Contains, References, DependsOn, Implements, Inherits, Calls, Uses, HandlesRoute, CallsRoute, DescribedBy`
- [x] 1.3 Define `NodeId` value object — composite `{kind}:{fqn}@{version-token}`; stable and filesystem-safe (no backslashes, colons only in fixed positions)
- [x] 1.4 Define `Node` record with `Id`, `Kind`, `DisplayName`, `Attributes` (string→JsonElement dict), `SourceRefs` (file + span list)
- [x] 1.5 Define `Edge` record with `FromId`, `ToId`, `Kind`, `Attributes`, `SourceRefs`
- [x] 1.6 System.Text.Json converters with stable ordering: alphabetical keys, deterministic sort of collections

## [x] Task 2: Workspace store layout

Goal: One workspace = one folder. Shard JSON per node/edge kind so per-file diffs stay small.

Context: Proposed layout — `<workspace>/manifest.json`, `<workspace>/nodes/<kind>.json`, `<workspace>/edges/<kind>.json`, `<workspace>/index/<field>.json`.

Acceptance: Writing a graph to disk and reading it back yields byte-identical JSON on re-serialize.

- [x] 2.1 `IWorkspaceStore` interface: `LoadAsync`, `SaveAsync`, `OpenWriter`, `OpenReader`, `GetManifest`
- [x] 2.2 `JsonWorkspaceStore` implementation with shard-per-kind
- [x] 2.3 `manifest.json` tracks schema version, engine version, last-build timestamp, reserved content-hash map (populated in phase 07)
- [x] 2.4 Atomic writes: write to `*.json.tmp`, flush, rename
- [x] 2.5 Deterministic serialization: sorted property names, sorted arrays where order is not semantic

## [x] Task 3: Query layer v0

Goal: Minimal read API that later MCP tools build on.

Acceptance: Unit tests cover every query method against synthetic fixtures.

- [x] 3.1 `IGraphQuery` with `GetNode(id)`, `FindNodes(kind, predicate)`, `GetEdges(fromId?, toId?, kind?)`, `Neighbors(id, direction, kind?)`
- [x] 3.2 In-memory implementation backed by a loaded store snapshot
- [x] 3.3 Secondary indexes: by kind, by display name prefix, by attribute key

## [x] Task 4: Unit tests — round-trip and query

Goal: Prove serialization stability and query correctness.

- [x] 4.1 Fixture builder for a synthetic multi-version graph (package `X@1.0.0` and `X@1.2.0`, both with types/methods)
- [x] 4.2 Round-trip test: serialize → deserialize → re-serialize byte-equal
- [x] 4.3 Query tests: find all versions of package `X`, neighbors of a specific type, edge-kind filtering
- [x] 4.4 Stability test: two independent runs produce identical bytes on disk

## [x] Task 5: Sync product definition

- [x] 5.1 Append graph model summary to `PROJECT_DEFINITION.md` (ogólny opis, bez szczegółów technicznych)
- [x] 5.2 Mark phase-1 items in `REQUIREMENT_DEFINITION.md`
- [x] 5.3 Respond with `Product definition: updated` or `Product definition: no update required`

## VALIDATE-STOP CHECKLIST

- [ ] All phase-1 tests green
- [ ] Round-trip byte-equality demonstrated
- [ ] Sample graph JSON visually inspected — readable, diff-friendly
- [x] `REQUIREMENT_DEFINITION.md` statuses updated
- [ ] Print signed summary
- [ ] **HALT. Do not open `phase-02-dotnet-provider.md`. Wait for sign-off.**
