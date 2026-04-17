# Plan: Phase 03 — Usage edges via Roslyn

> Walk user source code and emit `Uses` and `Calls` edges from user symbols to the versioned provider-produced nodes from phase 02.

Prerequisite: phase 02 complete and signed off.

## [ ] Task 1: Roslyn workspace loader

Goal: Load the target solution with the MSBuild workspace so semantic models resolve correctly.

- [ ] 1.1 Reference `Microsoft.CodeAnalysis.Workspaces.MSBuild`
- [ ] 1.2 Register MSBuild locator once at engine startup
- [ ] 1.3 Open the solution, compile each project, retain `Compilation` + `SemanticModel` per project
- [ ] 1.4 Surface compile errors as warnings; do not abort graph build on diagnostics

## [ ] Task 2: Symbol visitor

Goal: Visit every member and emit edges to external symbols tagged with the resolved package version.

Acceptance: For a user method calling `Newtonsoft.Json.JsonConvert.SerializeObject`, an edge of kind `Calls` targets `method:Newtonsoft.Json.JsonConvert.SerializeObject(System.Object)@<resolved version>`.

- [ ] 2.1 Walk invocation expressions, member accesses, object creations, attribute applications, type references
- [ ] 2.2 For each resolved symbol, determine containing assembly → look up package + version via the Dotnet provider's package map
- [ ] 2.3 Emit `Uses` (type reference) or `Calls` (method invocation) edges, carrying user source span in `SourceRefs`
- [ ] 2.4 Deduplicate edges per `(from, to, kind)`; aggregate call-count in attributes

## [ ] Task 3: User-code nodes

Goal: User types and methods exist in the graph as first-class nodes so edges have endpoints on both sides.

- [ ] 3.1 Emit `Type` / `Method` nodes for user symbols with `origin=user` attribute
- [ ] 3.2 Use project identifier as version token for user nodes (no semver available)
- [ ] 3.3 Skip generated code: respect `SyntaxTree.IsGenerated()` and common marker comments / suffixes

## [ ] Task 4: Integration test

Goal: Run end-to-end against the phase-02 fixture extended with real user code.

- [ ] 4.1 Extend the sample solution with user code calling each referenced nuget
- [ ] 4.2 Run the Dotnet provider end-to-end; persist graph to a temp workspace folder
- [ ] 4.3 Assert expected `Calls` edges exist with correct version tags
- [ ] 4.4 Assert no cross-version usage edges leak (user consuming v1 never produces an edge to a v2 symbol)

## [ ] Task 5: Sync product definition

- [ ] 5.1 Update docs
- [ ] 5.2 Status line

## VALIDATE-STOP CHECKLIST

- [ ] Usage edges exist for every referenced public symbol invoked in the fixture
- [ ] Version tags on targets are correct
- [ ] Integration test green
- [ ] Print signed summary
- [ ] **HALT. Do not open `phase-04-http-static.md`. Wait for sign-off.**
