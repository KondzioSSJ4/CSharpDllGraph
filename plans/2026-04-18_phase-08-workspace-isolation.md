# Plan: Phase 08 — Per-Project MCP Workspace Isolation

> Replace the global WorkspaceRegistry with a per-invocation workspace config so that one MCP server process serves exactly one project, configured via CLI arg or `.csharpdllgraph.json` file, and auto-builds + watches the graph without requiring a separate CLI step.

## [ ] Task 1: Define WorkspaceConfig and .csharpdllgraph.json schema

Goal: Introduce a `WorkspaceConfig` record and a JSON config file schema that the MCP can load at startup.

Context: `WorkspaceRegistration` (`src/CSharpDllGraph.Engine/Registry/WorkspaceRegistration.cs`) is the existing per-workspace data record — `WorkspaceConfig` replaces it for MCP use without touching the CLI registry path. The config file lives alongside the `.sln` file in the workspace root.

- [ ] 1.1 Create `src/CSharpDllGraph.Engine/Config/WorkspaceConfig.cs` — a record with: `string RootPath`, `string GraphPath` (default `<RootPath>/.csharpdllgraph/graph`), `string? SolutionPath` (optional, null = auto-discover)
- [ ] 1.2 Create `src/CSharpDllGraph.Engine/Config/WorkspaceConfigFile.cs` — a JSON-serializable class matching `.csharpdllgraph.json` schema with fields: `workspacePath` (string, required), `graphPath` (string, optional), `solutionPath` (string, optional)
- [ ] 1.3 Create `src/CSharpDllGraph.Engine/Config/WorkspaceConfigLoader.cs` — static class with method `WorkspaceConfig Load(string? cliWorkspacePath)`:
  - If `cliWorkspacePath` is not null/empty → use it as `RootPath` (highest priority)
  - Else → search current directory and up for `.csharpdllgraph.json`, parse it
  - Else → throw `InvalidOperationException` with clear message explaining both options
  - Resolve `GraphPath` and `SolutionPath` relative to `RootPath` if they are relative paths
  - Normalize all paths with `Path.GetFullPath`

## [ ] Task 2: Create IWorkspaceContext and single-workspace query service

Goal: Replace `IWorkspaceRegistry`-based queries in the engine with a new `IWorkspaceContext` abstraction that wraps a single `WorkspaceConfig`.

Context: `GraphQueryService` (`src/CSharpDllGraph.Engine/Query/GraphQueryService.cs`) currently depends on `IWorkspaceRegistry` to resolve workspaces by name and load `JsonWorkspaceStore`. `CrossWorkspaceHttpIndexBuilder` (`src/CSharpDllGraph.Engine/Http/CrossWorkspaceHttpIndexBuilder.cs`) also iterates registry workspaces — it needs to operate on one workspace instead. #[[src/CSharpDllGraph.Engine/Query/GraphQueryService.cs]]

- [ ] 2.1 Create `src/CSharpDllGraph.Engine/Config/IWorkspaceContext.cs` — interface with: `WorkspaceConfig Config { get; }`, `Task<InMemoryGraphQuery> GetQueryAsync(CancellationToken ct)`
- [ ] 2.2 Create `src/CSharpDllGraph.Engine/Config/WorkspaceContext.cs` — singleton implementation that holds `WorkspaceConfig`, lazily loads `JsonWorkspaceStore` from `Config.GraphPath`, caches the result; exposes `Invalidate()` method to drop the cached query (called by watch mode after each rebuild)
- [ ] 2.3 Add `IGraphQueryService` implementation `SingleWorkspaceQueryService` in `src/CSharpDllGraph.Engine/Query/SingleWorkspaceQueryService.cs` — delegates all existing `IGraphQueryService` methods to `IWorkspaceContext.GetQueryAsync()` without registry lookups; workspace-name parameters in tool calls are ignored (single workspace mode)
- [ ] 2.4 Update `CrossWorkspaceHttpIndexBuilder` (or create `SingleWorkspaceHttpIndexBuilder`) to build the HTTP index from a single `IWorkspaceContext` instead of iterating all registry workspaces

## [ ] Task 3: Implement auto-build + embedded watch mode in MCP startup

Goal: When the MCP server starts, it builds the graph if no cache exists, then starts an embedded file watcher that triggers incremental rebuilds, keeping the in-memory query fresh.

Context: `WorkspaceWatchSession` (`src/CSharpDllGraph.Cli/WorkspaceWatchSession.cs`) already implements debounced file watching — it can be moved to the Engine project or referenced directly. `GraphBuildPipeline` (`src/CSharpDllGraph.Engine/Providers/GraphBuildPipeline.cs`) performs full and incremental builds given a `GraphBuildContext` and `IWorkspaceStore`. The CLI's `BuildCommand` and `WatchCommand` show the full wiring pattern. #[[src/CSharpDllGraph.Cli/WorkspaceWatchSession.cs]]

- [ ] 3.1 Move `WorkspaceWatchSession` from `CSharpDllGraph.Cli` to `CSharpDllGraph.Engine` (namespace `CSharpDllGraph.Engine.Watch`) so MCP can reference it without a CLI dependency
- [ ] 3.2 Create `src/CSharpDllGraph.Engine/Watch/WorkspaceAutoManager.cs` — `IHostedService` implementation:
  - On `StartAsync`: instantiate `JsonWorkspaceStore(config.GraphPath)`, check if `manifest.json` exists in graph path
  - If no cache → run full `GraphBuildPipeline.BuildAndPersistAsync(...)` with all providers
  - After build (or if cache existed) → start `WorkspaceWatchSession`
  - On each batch of changes from the session → run incremental build → call `IWorkspaceContext.Invalidate()` to drop cached query
- [ ] 3.3 Register all graph providers in MCP DI (same set as CLI: `DotnetProvider`, `ControllerEndpointProvider`, `MinimalApiEndpointProvider`, `HttpClientCallSiteProvider`, `HttpFileCallSiteProvider`, `JsFetchCallSiteProvider`, `OpenApiSpecProvider`, `PostmanCallSiteProvider`) — check CLI's `ServiceRegistration` or `Program.cs` for the exact registration pattern
- [ ] 3.4 Register `WorkspaceAutoManager` as `IHostedService` in MCP DI

## [ ] Task 4: Rewire MCP Program.cs

Goal: Replace registry-based DI wiring in MCP host with `WorkspaceConfig`-driven wiring using the new types.

Context: Current `Program.cs` (`src/CSharpDllGraph.Mcp/Program.cs`) registers `WorkspaceRegistry`, `IWorkspaceRegistry`, `IGraphQueryService`, `ICrossWorkspaceHttpIndexBuilder`. All of these need replacement. CLI arg `--workspace-path` must be parsed before the host builder. #[[src/CSharpDllGraph.Mcp/Program.cs]]

- [ ] 4.1 Parse `args` before host builder: extract `--workspace-path <value>` if present (simple manual parse, no System.CommandLine needed here)
- [ ] 4.2 Call `WorkspaceConfigLoader.Load(cliWorkspacePath)` — fail fast with a clear error message to stderr if config cannot be resolved, then `Environment.Exit(1)`
- [ ] 4.3 Register `WorkspaceConfig` as singleton in DI (the resolved instance)
- [ ] 4.4 Register `WorkspaceContext` as `IWorkspaceContext` singleton
- [ ] 4.5 Register `SingleWorkspaceQueryService` as `IGraphQueryService` singleton
- [ ] 4.6 Register `SingleWorkspaceHttpIndexBuilder` as `ICrossWorkspaceHttpIndexBuilder` singleton
- [ ] 4.7 Remove `WorkspaceRegistry` and `IWorkspaceRegistry` registrations
- [ ] 4.8 Add `AddHostedService<WorkspaceAutoManager>()` registration
- [ ] 4.9 Verify `CSharpDllGraphTools` constructor still compiles (it only takes `IGraphQueryService` — no changes needed)

## [ ] Task 5: Update CSharpDllGraphTools for single-workspace mode

Goal: Remove workspace-name parameters from MCP tools that are meaningless in single-workspace mode, or make them silently ignored, so Claude gets a cleaner tool API.

Context: `CSharpDllGraphTools.cs` (`src/CSharpDllGraph.Mcp/Tools/CSharpDllGraphTools.cs`) exposes workspace name as a parameter to most tools — in single-workspace mode this is noise. #[[src/CSharpDllGraph.Mcp/Tools/CSharpDllGraphTools.cs]]

- [ ] 5.1 Review each tool's parameter list — identify `workspaceName` / `workspaces` parameters
- [ ] 5.2 Remove or mark as optional (with null default) any workspace-selection parameters; update tool descriptions to reflect that the workspace is pre-configured
- [ ] 5.3 Ensure `SingleWorkspaceQueryService` ignores any passed workspace names gracefully (returns data from the single loaded workspace)

## [ ] Task 6: Document .csharpdllgraph.json format

Goal: Add a concise schema example so users know how to configure a project.

- [ ] 6.1 Add `.csharpdllgraph.json` example to the root `README.md` under a new "Per-Project Configuration" section with the three fields: `workspacePath`, `graphPath` (optional), `solutionPath` (optional)
- [ ] 6.2 Document the two ways to pass workspace to MCP: (a) `--workspace-path <absolute-path>` CLI arg, (b) `.csharpdllgraph.json` in workspace root or any ancestor directory
- [ ] 6.3 Add a minimal Claude Desktop `claude_desktop_config.json` snippet showing MCP server config with `--workspace-path`

## [ ] Task 7: Sync product definition

Goal: Update `PRODUCT_DEFINITION.md` if the plan introduces new scope, users, flows, rules, constraints, or success metrics.

- [ ] 7.1 Review `PRODUCT_DEFINITION.md` against the changes introduced by this plan
- [ ] 7.2 Update `PRODUCT_DEFINITION.md` where applicable, preserving internal consistency. Skip if no changes apply.
- [ ] 7.3 Respond with `Product definition: updated` or `Product definition: no update required`
