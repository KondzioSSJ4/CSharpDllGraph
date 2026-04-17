# Plan: Phase 07 — CLI, watcher, incremental rebuild

> User-facing build/update commands and efficient incremental graph updates via content hashing. Completes v1.

Prerequisite: phase 06 complete and signed off.

## [ ] Task 1: CLI commands

Goal: `CSharpDllGraph.Cli` exposes `build`, `update`, `query`, and `workspace add/list/remove`.

- [ ] 1.1 Use `System.CommandLine` for command parsing
- [ ] 1.2 `build <workspace-root>` — full rebuild
- [ ] 1.3 `update <workspace-root>` — incremental
- [ ] 1.4 `query <tool> <args>` — local access to the same tool surface exposed over MCP
- [ ] 1.5 `workspace add/list/remove` — manages the registry from phase 06

## [ ] Task 2: Content hashing

Goal: Per-file SHA-256 tracked in the workspace manifest. Skip unchanged files on rebuild.

- [ ] 2.1 Track hashes for: `.csproj`, `.cs`, `.ts`, `.tsx`, `.js`, `.jsx`, `.vue`, `.svelte`, `.http`, `.rest`, OpenAPI specs, Postman collections, `packages.lock.json`, `project.assets.json`
- [ ] 2.2 Compare on `update`, re-run extractors only for changed inputs
- [ ] 2.3 Persist the hash map in `manifest.json`

## [ ] Task 3: Edge-level incremental updates

Goal: When a file changes, remove every node/edge whose `SourceRefs` include that file, re-run the relevant extractors for it, merge results back.

- [ ] 3.1 Per-node-kind removal strategy — user `Type`/`Method` nodes are file-owned; `HttpEndpoint` nodes may have multiple source files, remove only when last-remaining source file gone
- [ ] 3.2 Reconciliation path must rerun for endpoints whose source set shrank (provenance list updates)
- [ ] 3.3 Regression test: edit one source file, run `update`, assert the delta exactly matches expectation

## [ ] Task 4: Watcher mode

Goal: `CSharpDllGraph.Cli watch <workspace>` keeps the graph live as files change.

- [ ] 4.1 `FileSystemWatcher` rooted at workspace root, debounced 500ms per file
- [ ] 4.2 Trigger incremental update on debounced batches
- [ ] 4.3 Graceful Ctrl+C shutdown
- [ ] 4.4 Watcher activity logged to `logs/watcher-actions.log`

## [ ] Task 5: MCP tool reuse

Goal: The CLI `query` subcommand and the MCP tool methods call the same code.

- [ ] 5.1 Extract tool bodies into `IGraphQueryService` methods
- [ ] 5.2 Both `[McpServerTool]` methods and the CLI `query` handler become thin wrappers around the service

## [ ] Task 6: Performance target

Goal: Edit → update → query round-trip under 2 seconds on the sample fixture.

- [ ] 6.1 Benchmark test under `tests/`, documented target
- [ ] 6.2 Record the actual measured value in `REQUIREMENT_DEFINITION.md`
- [ ] 6.3 If target missed, document the gap; do NOT silently lower the bar

## [ ] Task 7: Sync product definition

- [ ] 7.1 Mark v1 scope complete in `PROJECT_DEFINITION.md`
- [ ] 7.2 Flip all phase-0..7 statuses in `REQUIREMENT_DEFINITION.md` to their final state
- [ ] 7.3 Respond with status line

## VALIDATE-STOP CHECKLIST

- [ ] All CLI commands functional end-to-end
- [ ] Incremental rebuild demonstrated on the fixture
- [ ] Watcher edits propagate within the latency target
- [ ] v1 SCOPE COMPLETE — 6 MCP tools + watcher + multi-workspace cross-linking
- [ ] Print final signed v1-delivery summary
- [ ] **HALT. v1 delivered. Await user direction for v2 (runtime capture / non-C# providers / perf).**
