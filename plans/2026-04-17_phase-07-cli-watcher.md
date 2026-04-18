# Plan: Phase 07 — CLI, Watcher, and Incremental Rebuild

> Finish v1 delivery with a local CLI, workspace management, incremental rebuilds, and file watching. Prove the latency target on fixtures, close docs, then stop at final delivery.

## [ ] Task 1: Add CLI command surface

Goal: Expose local build, update, query, and workspace-management commands.

Context: CLI project shell exists in #[[file:src/CSharpDllGraph.Cli/Program.cs]]. Workspace registry arrives from phase 06.

Acceptance: The CLI supports build, update, query, and workspace registry flows through deterministic command parsing.

- [ ] 1.1 Add command parsing, preferably with `System.CommandLine`
- [ ] 1.2 Implement `build <workspace-root>` for full rebuild
- [ ] 1.3 Implement `update <workspace-root>` for incremental rebuild
- [ ] 1.4 Implement `query <tool> <args>` and `workspace add/list/remove`

## [ ] Task 2: Track content hashes for incremental rebuild

Goal: Detect changed graph inputs precisely enough to avoid full rebuilds on every update.

Context: Workspace manifest already exists in #[[file:src/CSharpDllGraph.Engine/Graph/WorkspaceManifest.cs]].

Acceptance: The manifest persists hashes for supported inputs and the update flow reruns only the required extractors.

- [ ] 2.1 Hash supported source, project, dependency, and HTTP-spec inputs
- [ ] 2.2 Persist the hash map in `manifest.json`
- [ ] 2.3 Compare previous and current hashes during `update`
- [ ] 2.4 Route changed inputs to the minimal required extractor set

## [ ] Task 3: Apply edge-level incremental updates

Goal: Update only graph fragments affected by changed files.

Context: File ownership rules for user symbols and HTTP endpoints must preserve provenance and avoid stale edges.

Acceptance: Editing one source file updates only the owned nodes and edges, while shared endpoint provenance remains correct.

- [ ] 3.1 Define per-kind removal and replacement rules for user nodes and HTTP nodes
- [ ] 3.2 Re-run reconciliation when endpoint source sets shrink
- [ ] 3.3 Add regression tests for one-file deltas
- [ ] 3.4 Assert the delta matches the expected graph change exactly

## [ ] Task 4: Add watcher mode

Goal: Keep graph data fresh during local development with debounced filesystem watching.

Acceptance: File edits trigger incremental updates, logs capture watcher activity, and shutdown is clean.

- [ ] 4.1 Add `FileSystemWatcher` rooted at the workspace
- [ ] 4.2 Debounce changes by file and batch updates sensibly
- [ ] 4.3 Trigger incremental update runs from debounced batches
- [ ] 4.4 Support graceful Ctrl+C shutdown and watcher logging

## [ ] Task 5: Reuse the MCP query layer from CLI

Goal: Avoid duplicate tool logic across CLI and MCP host.

Context: MCP tool implementations from phase 06 should already call reusable services.

Acceptance: CLI query commands and MCP tool methods both wrap the same query service layer.

- [ ] 5.1 Extract or finalize `IGraphQueryService` style abstractions behind MCP tools
- [ ] 5.2 Keep MCP tool methods thin wrappers
- [ ] 5.3 Make CLI `query` call the same service implementations
- [ ] 5.4 Verify response shape consistency across CLI and MCP entrypoints

## [ ] Task 6: Prove the performance target

Goal: Measure and record edit-to-query latency against the fixture workspace.

Acceptance: The documented latency result is measured, recorded, and compared directly against the target instead of being hand-waved.

- [ ] 6.1 Add a benchmark or integration measurement under `tests/`
- [ ] 6.2 Measure edit → rebuild → query latency on the sample fixture
- [ ] 6.3 Record the measured value in `docs/REQUIREMENT_DEFINITION.md`
- [ ] 6.4 If the target is missed, document the gap without lowering the bar

## [ ] Task 7: Pass the final validate-stop gate

Goal: Close v1 delivery with verified CLI, watcher, and incremental rebuild behavior.

Acceptance: End-to-end flows work, latency is measured, and the final delivery summary is signed.

- [ ] 7.1 Prove CLI commands work end to end
- [ ] 7.2 Prove incremental rebuild works on the fixture
- [ ] 7.3 Prove watcher-driven updates stay within the target or document the shortfall
- [ ] 7.4 Print the signed final v1 delivery summary and halt for human direction

## [ ] Task 8: Sync product definition

Goal: Mark v1 complete in product docs with final requirement statuses.

Context: Update #[[file:docs/PROJECT_DEFINITION.md]] and #[[file:docs/REQUIREMENT_DEFINITION.md]] after final validation.

- [ ] 8.1 Review `docs/PROJECT_DEFINITION.md` against final v1 delivery
- [ ] 8.2 Update all touched requirement statuses to their final verified state in `docs/REQUIREMENT_DEFINITION.md`
- [ ] 8.3 Respond with `Product definition: updated` or `Product definition: no update required`
