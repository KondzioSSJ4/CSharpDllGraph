# Plan: Phase 06 — MCP tools + cross-workspace registry

> Expose the full v1 MCP tool surface over the query layer. Resolve cross-workspace HTTP edges at query time via a shared URL-template registry. No LLM in any tool path.

Prerequisite: phase 05 complete and signed off.

## [ ] Task 1: Workspace registry

Goal: Global list of known workspaces and their graph paths.

- [ ] 1.1 Config file at `%APPDATA%\CSharpDllGraph\workspaces.json` (Windows) / `~/.config/csharpdllgraph/workspaces.json` elsewhere
- [ ] 1.2 `WorkspaceRegistry` service with `List`, `Add`, `Remove`, `Resolve(name)`
- [ ] 1.3 Each entry: name, root path, graph path, last-built timestamp
- [ ] 1.4 Registry read is lock-free; writes use a file lock

## [ ] Task 2: Cross-workspace URL index

Goal: At query time, join `HttpEndpoint` from any workspace with `HttpCallSite` from any workspace by normalized template.

- [ ] 2.1 `CrossWorkspaceHttpIndex` built on demand from all registered workspaces
- [ ] 2.2 Index keyed on `(method, normalized-path)` → list of `(workspace, node-id, role)` where role ∈ `producer|consumer`
- [ ] 2.3 No persistent file — rebuild each invocation (cache can come later if perf demands)

## [ ] Task 3: Tool — `describe_package_api`

- [ ] 3.1 Params: `package`, `version`, optional `tfm`, optional `filter` (regex on type/method name)
- [ ] 3.2 Returns: types + public methods with signatures, grouped by namespace
- [ ] 3.3 Acceptance: for a sample package in the fixture, returns the expected public surface

## [ ] Task 4: Tool — `find_usages`

- [ ] 4.1 Params: `symbol_id` OR (`kind`, `full_name`, optional `version`), optional `workspaces` (list or `all`)
- [ ] 4.2 Returns: list of user call sites with file + span + enclosing symbol, grouped by workspace
- [ ] 4.3 Sort by workspace name, then file path, then position

## [ ] Task 5: Tool — `trace_http_call`

- [ ] 5.1 Params: `method`, `path` (template form), optional `workspaces`
- [ ] 5.2 Returns: producer endpoint node(s) + all call sites across workspaces, each tagged with source label and workspace
- [ ] 5.3 Backed by the cross-workspace index from task 2

## [ ] Task 6: Tool — `list_dependencies`

- [ ] 6.1 Params: `workspace`, optional `project`
- [ ] 6.2 Returns: packages with resolved versions, per-project breakdown, direct-vs-transitive flag

## [ ] Task 7: Tool — `find_version_conflicts`

- [ ] 7.1 Params: `workspace`
- [ ] 7.2 Returns: packages where two or more different versions appear across projects, with the consuming projects and diverging versions listed

## [ ] Task 8: Tool — `suggest_usage`

Goal: Produce canonical call-site examples extracted from user code. No LLM.

- [ ] 8.1 Params: `symbol_id` OR (`package`, `type`, `method`), optional `max_results` (default 5)
- [ ] 8.2 Collect all `Calls` edges pointing into the target
- [ ] 8.3 Rank by: call-site frequency, recency (file mtime), diversity (prefer one per calling namespace), brevity (shorter enclosing method wins ties)
- [ ] 8.4 Return top N with ±5 lines of surrounding source excerpt

## [ ] Task 9: MCP wiring

Goal: Register all 6 tools as `[McpServerTool]` methods on a single `CSharpDllGraphTools` class.

Context: Match KanbnMCP's `WithTools<T>` shape. Each tool receives DI-injected `IGraphQueryService` + `WorkspaceRegistry`.

- [ ] 9.1 `CSharpDllGraphTools` class with 6 `[McpServerTool]` methods
- [ ] 9.2 Consistent parameter naming and XML doc comments — those surface as MCP tool descriptions
- [ ] 9.3 Each tool returns structured JSON objects, not prose strings
- [ ] 9.4 End-to-end test: boot the MCP server, call each tool via the MCP protocol, assert response shape against a schema fixture

## [ ] Task 10: Sync product definition

- [ ] 10.1 Update docs
- [ ] 10.2 Status line

## VALIDATE-STOP CHECKLIST

- [ ] All 6 tools callable via MCP and return expected shapes
- [ ] Cross-workspace `trace_http_call` works against a 2-workspace fixture (API repo + UI repo)
- [ ] No LLM usage anywhere in the tool path (inspect imports / deps)
- [ ] Print signed summary
- [ ] **HALT. Do not open `phase-07-cli-watcher.md`. Wait for sign-off.**
