# Plan: Phase 03 — Usage Edges via Roslyn

> Add semantic usage edges on top of the structural graph from phase 02. Resolve user-code symbols with Roslyn, emit version-correct `Calls` and `Uses` edges, then stop at the gate.

## [ ] Task 1: Load Roslyn workspace and compilations

Goal: Open the fixture solution through Roslyn and retain compilations needed for symbol resolution.

Context: Structural graph output comes from phase 02. New Roslyn-facing code will extend `src/CSharpDllGraph.Providers.Dotnet/`.

Acceptance: The provider can open the sample solution, compile every project, and continue even when diagnostics exist.

- [ ] 1.1 Add `Microsoft.CodeAnalysis.Workspaces.MSBuild`
- [ ] 1.2 Register MSBuild locator once during engine startup
- [ ] 1.3 Open the solution and cache `Compilation` plus `SemanticModel` per relevant document
- [ ] 1.4 Surface compile diagnostics as warnings instead of aborting the graph build

## [ ] Task 2: Resolve symbol usages into graph edges

Goal: Convert user code references into graph edges that target the versioned symbols emitted in phase 02.

Context: `NodeId` versioning rules from phase 01 and package maps from phase 02 must stay consistent.

Acceptance: Invocations and references in the fixture produce deduplicated `Calls` or `Uses` edges with source spans and correct target versions.

- [ ] 2.1 Walk invocation expressions, member access, object creation, attributes, and type references
- [ ] 2.2 Resolve each symbol to its containing assembly, package, and package version using the phase-02 provider outputs
- [ ] 2.3 Emit `Calls` for method invocations and `Uses` for non-call type references
- [ ] 2.4 Deduplicate edges per `(from, to, kind)` and aggregate call count metadata

## [ ] Task 3: Emit user-code nodes safely

Goal: Add graph nodes for user symbols without mixing them with package-owned symbols.

Context: Existing graph records live in #[[file:src/CSharpDllGraph.Engine/Graph/Node.cs]] and #[[file:src/CSharpDllGraph.Engine/Graph/SourceRef.cs]].

Acceptance: User `Type` and `Method` nodes are versioned by project identity, marked `origin=user`, and exclude generated code.

- [ ] 3.1 Emit user `Type` and `Method` nodes with `origin=user`
- [ ] 3.2 Use a stable project-specific version token for user nodes
- [ ] 3.3 Skip generated files through Roslyn and common generated-code markers
- [ ] 3.4 Reuse source span tracking so returned call sites can later power MCP tools

## [ ] Task 4: Prove usage edges with integration fixtures

Goal: Validate semantic edges against deterministic sample code.

Context: Extend the fixture solution introduced in phase 02. Existing engine tests live in `tests/`.

Acceptance: The fixture contains explicit user calls into referenced packages and the resulting graph shows only the expected versioned targets.

- [ ] 4.1 Extend the sample solution with user code that calls fixture package APIs
- [ ] 4.2 Run the provider end to end and persist the graph to a temp workspace
- [ ] 4.3 Assert expected `Calls` and `Uses` edges with correct target version tags
- [ ] 4.4 Assert no cross-version leakage when user code references only one package version

## [ ] Task 5: Pass the validate-stop gate

Goal: Close phase 03 with verified usage edges and halt before phase 04.

Acceptance: Every fixture usage is represented, target versions are correct, and summary output is signed.

- [ ] 5.1 Prove usage edges exist for every relevant referenced public symbol in the fixture
- [ ] 5.2 Prove target version tags are correct
- [ ] 5.3 Prove integration tests are green
- [ ] 5.4 Print a signed summary and halt for human sign-off

## [ ] Task 6: Sync product definition

Goal: Keep product docs aligned with semantic-edge support.

Context: Update #[[file:docs/PROJECT_DEFINITION.md]] and #[[file:docs/REQUIREMENT_DEFINITION.md]] after validation.

- [ ] 6.1 Review `docs/PROJECT_DEFINITION.md` against new usage-edge behavior
- [ ] 6.2 Update touched phase-03 requirements in `docs/REQUIREMENT_DEFINITION.md`
- [ ] 6.3 Respond with `Product definition: updated` or `Product definition: no update required`
