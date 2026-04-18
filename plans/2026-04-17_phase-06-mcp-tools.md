# Plan: Phase 06 — MCP Tools and Cross-Workspace Registry

> Build the v1 MCP surface on top of the graph pipeline from phases 01-05. Add workspace registration, cross-workspace URL lookup, six MCP tools, then stop at the gate.

## [ ] Task 1: Add workspace registry

Goal: Track built workspaces that MCP tools can query by name.

Context: MCP host entrypoint exists in #[[file:src/CSharpDllGraph.Mcp/Program.cs]] and tool shell exists in #[[file:src/CSharpDllGraph.Mcp/Tools/CSharpDllGraphTools.cs]].

Acceptance: The runtime can list, add, remove, and resolve registered workspaces through a stable config file.

- [ ] 1.1 Store workspace registrations under the platform-specific config path
- [ ] 1.2 Add a `WorkspaceRegistry` service with `List`, `Add`, `Remove`, and `Resolve`
- [ ] 1.3 Persist name, root path, graph path, and last-built timestamp per workspace
- [ ] 1.4 Keep reads lock-free and protect writes with a file lock

## [ ] Task 2: Build cross-workspace HTTP index

Goal: Support `trace_http_call` across multiple registered workspaces.

Context: HTTP nodes and normalized `(method, path)` identities come from phases 04 and 05.

Acceptance: The runtime can rebuild an in-memory cross-workspace index on demand and resolve both producer and consumer nodes by route identity.

- [ ] 2.1 Load all registered workspace graphs on demand
- [ ] 2.2 Index by normalized method plus path
- [ ] 2.3 Track workspace name, node id, and role for each match
- [ ] 2.4 Keep the index ephemeral for v1; no persisted cache yet

## [ ] Task 3: Implement package and dependency tools

Goal: Expose stable query tools for package surface and dependency resolution.

Acceptance: `describe_package_api`, `list_dependencies`, and `find_version_conflicts` return structured JSON backed by fixture graphs.

- [ ] 3.1 Implement `describe_package_api` with package, version, optional TFM, and optional filter inputs
- [ ] 3.2 Implement `list_dependencies` with workspace and optional project filters
- [ ] 3.3 Implement `find_version_conflicts` with per-project version details
- [ ] 3.4 Return structured JSON payloads, not prose strings

## [ ] Task 4: Implement usage and HTTP tracing tools

Goal: Expose stable query tools for symbol usage and route tracing.

Context: Semantic edges from phase 03 and HTTP indices from phases 04-06 feed these tools.

Acceptance: `find_usages` and `trace_http_call` return grouped, sorted results with enough file and workspace context for MCP clients.

- [ ] 4.1 Implement `find_usages` with symbol lookup by id or by structured name input
- [ ] 4.2 Group usage results by workspace and sort by workspace, file path, and position
- [ ] 4.3 Implement `trace_http_call` with normalized method plus path lookup across workspaces
- [ ] 4.4 Return producer endpoints and consumer call sites with workspace and source labels

## [ ] Task 5: Implement usage suggestion tool

Goal: Expose canonical example call sites for a symbol based on observed graph usage.

Acceptance: `suggest_usage` ranks candidate call sites and returns short source excerpts from real user code.

- [ ] 5.1 Collect inbound `Calls` edges for the requested target symbol
- [ ] 5.2 Rank candidates by frequency, recency, diversity, and brevity
- [ ] 5.3 Read source excerpts around each selected call site
- [ ] 5.4 Return top results with deterministic ordering for ties

## [ ] Task 6: Wire MCP host and end-to-end tests

Goal: Make all six tools callable through the stdio MCP server.

Context: MCP host scaffolding already exists in #[[file:src/CSharpDllGraph.Mcp/Program.cs]] and #[[file:src/CSharpDllGraph.Mcp/Tools/CSharpDllGraphTools.cs]].

Acceptance: The MCP server exposes all six tools through `[McpServerTool]` methods and end-to-end tests assert their response shapes.

- [ ] 6.1 Add six `[McpServerTool]` methods with consistent parameter names and XML docs
- [ ] 6.2 Move tool logic behind reusable query services instead of embedding logic in the host
- [ ] 6.3 Add end-to-end MCP protocol tests that call every tool against fixture graphs
- [ ] 6.4 Assert response shapes against deterministic schema fixtures

## [ ] Task 7: Pass the validate-stop gate

Goal: Close phase 06 with all v1 MCP tools verified and halt before phase 07.

Acceptance: Every tool is callable, cross-workspace tracing works, and the tool path contains no LLM usage.

- [ ] 7.1 Prove all six tools are callable through MCP and return expected shapes
- [ ] 7.2 Prove `trace_http_call` works across a two-workspace fixture
- [ ] 7.3 Verify the tool path contains no LLM or embedding dependencies
- [ ] 7.4 Print a signed summary and halt for human sign-off

## [ ] Task 8: Sync product definition

Goal: Keep product docs aligned with the locked v1 MCP tool surface.

Context: Update #[[file:docs/PROJECT_DEFINITION.md]] and #[[file:docs/REQUIREMENT_DEFINITION.md]] after validation.

- [ ] 8.1 Review `docs/PROJECT_DEFINITION.md` against delivered MCP tool behavior
- [ ] 8.2 Update touched phase-06 requirements in `docs/REQUIREMENT_DEFINITION.md`
- [ ] 8.3 Respond with `Product definition: updated` or `Product definition: no update required`
