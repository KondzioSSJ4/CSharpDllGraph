# Plan: Phase 08 — Per-Project MCP Workspace Isolation

> Replace the global WorkspaceRegistry with a per-invocation workspace config so one MCP server process serves exactly one project, auto-builds, and watches the graph without a separate CLI step.

```plan-meta
{
  "version": 1,
  "provider": "claude-code",
  "model": "claude-sonnet-4-6",
  "maxParallel": 1,
  "validation": [
    "dotnet build CSharpDllGraph.slnx"
  ]
}
```

## Task T1: Define WorkspaceConfig and config file schema

```task
{
  "id": "T1",
  "title": "Define WorkspaceConfig and config file schema",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [],
  "paths": [
    "src/CSharpDllGraph.Engine/Config/"
  ],
  "goal": "Introduce WorkspaceConfig record and WorkspaceConfigLoader that resolves config from CLI arg (priority) or .csharpdllgraph.json file.",
  "acceptance": [
    "WorkspaceConfigLoader.Load(null) discovers .csharpdllgraph.json by walking up from CWD",
    "WorkspaceConfigLoader.Load('/some/path') uses that path directly without file lookup",
    "All resolved paths are absolute (Path.GetFullPath applied)",
    "Missing config throws InvalidOperationException with helpful message"
  ],
  "steps": [
    "Create src/CSharpDllGraph.Engine/Config/WorkspaceConfig.cs — record with: string RootPath, string GraphPath (defaults to <RootPath>/.csharpdllgraph/graph), string? SolutionPath",
    "Create src/CSharpDllGraph.Engine/Config/WorkspaceConfigFile.cs — JSON-serializable class for .csharpdllgraph.json with fields: workspacePath (required), graphPath (optional), solutionPath (optional)",
    "Create src/CSharpDllGraph.Engine/Config/WorkspaceConfigLoader.cs — static class, method WorkspaceConfig Load(string? cliWorkspacePath): if cliWorkspacePath non-empty use it as RootPath; else walk CWD upward searching for .csharpdllgraph.json and parse it; else throw",
    "In Load(): resolve GraphPath and SolutionPath relative to RootPath when they are relative; normalize all paths with Path.GetFullPath"
  ]
}
```

## Task T2: Create IWorkspaceContext and SingleWorkspaceQueryService

```task
{
  "id": "T2",
  "title": "Create IWorkspaceContext and SingleWorkspaceQueryService",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T1"
  ],
  "paths": [
    "src/CSharpDllGraph.Engine/Config/",
    "src/CSharpDllGraph.Engine/Query/",
    "src/CSharpDllGraph.Engine/Http/"
  ],
  "goal": "Replace IWorkspaceRegistry-based query lookup with IWorkspaceContext that wraps a single WorkspaceConfig and caches the loaded graph.",
  "acceptance": [
    "IWorkspaceContext.GetQueryAsync() returns InMemoryGraphQuery loaded from Config.GraphPath",
    "Invalidate() drops the cached query so next call reloads from disk",
    "SingleWorkspaceQueryService implements IGraphQueryService using IWorkspaceContext — no registry calls",
    "SingleWorkspaceHttpIndexBuilder implements ICrossWorkspaceHttpIndexBuilder using IWorkspaceContext"
  ],
  "steps": [
    "Create src/CSharpDllGraph.Engine/Config/IWorkspaceContext.cs — interface: WorkspaceConfig Config { get; }, Task<InMemoryGraphQuery> GetQueryAsync(CancellationToken ct), void Invalidate()",
    "Create src/CSharpDllGraph.Engine/Config/WorkspaceContext.cs — singleton implementing IWorkspaceContext: holds WorkspaceConfig, lazily loads JsonWorkspaceStore(Config.GraphPath).LoadAsync(), caches result in a volatile field reset by Invalidate()",
    "Create src/CSharpDllGraph.Engine/Query/SingleWorkspaceQueryService.cs — implements IGraphQueryService: delegate all query methods to IWorkspaceContext.GetQueryAsync(); silently ignore any workspace-name filter parameters (single workspace mode)",
    "Create src/CSharpDllGraph.Engine/Http/SingleWorkspaceHttpIndexBuilder.cs — implements ICrossWorkspaceHttpIndexBuilder: build HTTP index from the single IWorkspaceContext graph snapshot; mirror the logic in CrossWorkspaceHttpIndexBuilder but without iterating a registry"
  ]
}
```

## Task T3: Embed auto-build and watch mode in Engine as IHostedService

```task
{
  "id": "T3",
  "title": "Embed auto-build and watch mode in Engine as IHostedService",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T2"
  ],
  "paths": [
    "src/CSharpDllGraph.Engine/Watch/",
    "src/CSharpDllGraph.Cli/WorkspaceWatchSession.cs"
  ],
  "goal": "Move WorkspaceWatchSession to the Engine project and implement WorkspaceAutoManager IHostedService that auto-builds on startup and keeps the graph fresh via embedded watch.",
  "acceptance": [
    "WorkspaceWatchSession compiles in CSharpDllGraph.Engine namespace with no CLI dependency",
    "WorkspaceAutoManager.StartAsync() builds the graph if manifest.json is absent, then starts file watching",
    "Each batch of file changes triggers an incremental build followed by IWorkspaceContext.Invalidate()",
    "dotnet build CSharpDllGraph.slnx passes"
  ],
  "steps": [
    "Move src/CSharpDllGraph.Cli/WorkspaceWatchSession.cs to src/CSharpDllGraph.Engine/Watch/WorkspaceWatchSession.cs; update namespace to CSharpDllGraph.Engine.Watch; remove from Cli project, add to Engine project file",
    "Create src/CSharpDllGraph.Engine/Watch/WorkspaceAutoManager.cs implementing IHostedService: constructor takes WorkspaceConfig, IWorkspaceContext, GraphBuildPipeline, IEnumerable<IGraphProvider>, ILogger",
    "In StartAsync: create JsonWorkspaceStore(config.GraphPath); if manifest.json missing run GraphBuildPipeline.BuildAndPersistAsync with all providers and a GraphBuildContext built from WorkspaceConfig; call IWorkspaceContext.Invalidate() after build",
    "After initial build (or if cache existed): instantiate WorkspaceWatchSession(config.RootPath, resolvedSolutionPath, config.GraphPath, debounceMs: 750, log: logger.LogInformation); start consuming batches in a background Task",
    "For each batch: run incremental GraphBuildPipeline.BuildAndPersistAsync with changed file paths; then call IWorkspaceContext.Invalidate()",
    "In StopAsync: dispose WorkspaceWatchSession and cancel the background task"
  ]
}
```

## Task T4: Rewire MCP Program.cs for single-workspace mode

```task
{
  "id": "T4",
  "title": "Rewire MCP Program.cs for single-workspace mode",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T3"
  ],
  "paths": [
    "src/CSharpDllGraph.Mcp/Program.cs",
    "src/CSharpDllGraph.Mcp/CSharpDllGraph.Mcp.csproj"
  ],
  "goal": "Replace registry-based DI with WorkspaceConfig-driven wiring; parse --workspace-path CLI arg before host builder and fail fast if config is unresolvable.",
  "acceptance": [
    "MCP starts without --workspace-path when .csharpdllgraph.json exists in CWD",
    "MCP starts with --workspace-path /abs/path ignoring any config file",
    "MCP exits with non-zero code and stderr message when neither source is available",
    "No WorkspaceRegistry or IWorkspaceRegistry in DI",
    "dotnet build CSharpDllGraph.slnx passes"
  ],
  "steps": [
    "Before host builder: scan args for --workspace-path <value>; extract value or null",
    "Call WorkspaceConfigLoader.Load(cliWorkspacePath); on exception write to Console.Error and Environment.Exit(1)",
    "In services: remove WorkspaceRegistry and IWorkspaceRegistry registrations",
    "Register resolved WorkspaceConfig instance as singleton",
    "Register WorkspaceContext as IWorkspaceContext singleton",
    "Register SingleWorkspaceQueryService as IGraphQueryService singleton",
    "Register SingleWorkspaceHttpIndexBuilder as ICrossWorkspaceHttpIndexBuilder singleton",
    "Register all graph providers (DotnetProvider, ControllerEndpointProvider, MinimalApiEndpointProvider, HttpClientCallSiteProvider, HttpFileCallSiteProvider, JsFetchCallSiteProvider, OpenApiSpecProvider, PostmanCallSiteProvider) — copy exact registration pattern from CLI Program.cs or ServiceRegistration",
    "Add AddHostedService<WorkspaceAutoManager>()",
    "Add project reference to CSharpDllGraph.Engine in Mcp .csproj if not already present"
  ]
}
```

## Task T5: Remove workspace-selection noise from MCP tool signatures

```task
{
  "id": "T5",
  "title": "Remove workspace-selection noise from MCP tool signatures",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T4"
  ],
  "paths": [
    "src/CSharpDllGraph.Mcp/Tools/CSharpDllGraphTools.cs"
  ],
  "goal": "Remove or make optional the workspaceName/workspaces parameters from all MCP tool methods so Claude sees a clean single-workspace API.",
  "acceptance": [
    "No required workspace-name parameter in any tool",
    "Tool descriptions updated to say workspace is pre-configured at server startup",
    "All tools still return correct results via SingleWorkspaceQueryService"
  ],
  "steps": [
    "Open CSharpDllGraphTools.cs; for each [McpServerTool] method identify workspaceName or workspaces parameters",
    "Remove those parameters (or default them to null/empty if removing breaks interface — confirm SingleWorkspaceQueryService ignores them)",
    "Update each tool's Description attribute to remove workspace selection language; add a note like 'workspace is configured at server startup'",
    "Verify the file compiles; check that no tool method passes a non-null workspace name to a registry call"
  ]
}
```

## Task T6: Document .csharpdllgraph.json and MCP setup

```task
{
  "id": "T6",
  "title": "Document .csharpdllgraph.json and MCP setup",
  "status": "[x]",
  "agent": "docs-product",
  "dependsOn": [
    "T5"
  ],
  "paths": [
    "README.md"
  ],
  "goal": "Add a Per-Project Configuration section to README.md covering the config file schema and Claude Desktop MCP snippet.",
  "acceptance": [
    "README.md has a 'Per-Project Configuration' section with .csharpdllgraph.json example",
    "Both config sources documented: CLI arg --workspace-path and .csharpdllgraph.json",
    "claude_desktop_config.json snippet shows --workspace-path usage"
  ],
  "steps": [
    "Add 'Per-Project Configuration' section to README.md with .csharpdllgraph.json JSON example showing all three fields (workspacePath required, graphPath and solutionPath optional)",
    "Document priority: CLI arg --workspace-path overrides config file",
    "Add claude_desktop_config.json snippet: mcpServers entry for CSharpDllGraph with args: ['--workspace-path', '/absolute/path/to/your/project']",
    "Add note that MCP auto-builds the graph on first run and keeps it fresh via embedded file watching"
  ]
}
```

## Task T7: Sync product definition

```task
{
  "id": "T7",
  "title": "Sync product definition",
  "status": "[x]",
  "agent": "docs-product",
  "dependsOn": [
    "T6"
  ],
  "paths": [
    "PRODUCT_DEFINITION.md"
  ],
  "goal": "Update PRODUCT_DEFINITION.md to reflect the new per-project isolated MCP model.",
  "acceptance": [
    "PRODUCT_DEFINITION.md updated or confirmed no update required"
  ],
  "steps": [
    "Review PRODUCT_DEFINITION.md against changes introduced by this plan (no WorkspaceRegistry in MCP, per-project config, auto-build+watch)",
    "Update sections describing MCP architecture, configuration, and workspace management where applicable",
    "Respond with 'Product definition: updated' or 'Product definition: no update required'"
  ]
}
```
