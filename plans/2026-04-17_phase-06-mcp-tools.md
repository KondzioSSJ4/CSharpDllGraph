# Plan: Phase 06 - MCP Tools and Cross-Workspace Registry

> Build the phase 06 MCP tool surface in script-executable task units with explicit agents, dependencies, and validation defaults.

```plan-meta
{
  "version": 1,
  "provider": "codex",
  "model": "gpt-5.4",
  "maxParallel": 2,
  "validation": [
    "dotnet build CSharpDllGraph.slnx",
    "dotnet test CSharpDllGraph.slnx --no-build"
  ]
}
```

## Task T1: Add workspace registry

```task
{
  "id": "T1",
  "title": "Add workspace registry",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [],
  "paths": [
    "src/CSharpDllGraph.Mcp/",
    "src/CSharpDllGraph.Engine/"
  ],
  "goal": "Add a runtime workspace registry that MCP queries can use by stable workspace name.",
  "acceptance": [
    "The runtime can list, add, remove, and resolve registered workspaces through a stable config file.",
    "Registry entries persist workspace name, root path, graph path, and last-built timestamp."
  ],
  "steps": [
    "Store workspace registrations under the platform-specific config path.",
    "Add a WorkspaceRegistry service with List, Add, Remove, and Resolve operations.",
    "Persist name, root path, graph path, and last-built timestamp per workspace.",
    "Keep reads lock-free and protect writes with a file lock."
  ]
}
```

## Task T2: Build cross-workspace HTTP index

```task
{
  "id": "T2",
  "title": "Build cross-workspace HTTP index",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T1"
  ],
  "paths": [
    "src/CSharpDllGraph.Engine/",
    "src/CSharpDllGraph.Mcp/",
    "src/CSharpDllGraph.Providers.Dotnet/"
  ],
  "goal": "Build an ephemeral in-memory index that resolves producer and consumer HTTP nodes across registered workspaces.",
  "acceptance": [
    "The runtime can rebuild a cross-workspace HTTP index on demand from all registered workspace graphs.",
    "The index resolves matches by normalized method and path with workspace name, node id, and role."
  ],
  "steps": [
    "Load all registered workspace graphs on demand.",
    "Index entries by normalized HTTP method and normalized path.",
    "Track workspace name, node id, and producer or consumer role for each match.",
    "Keep the index ephemeral for v1 with no persisted cache."
  ]
}
```

## Task T3: Implement package and dependency tools

```task
{
  "id": "T3",
  "title": "Implement package and dependency tools",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T1"
  ],
  "paths": [
    "src/CSharpDllGraph.Mcp/",
    "src/CSharpDllGraph.Engine/"
  ],
  "goal": "Implement describe_package_api, list_dependencies, and find_version_conflicts as structured MCP query tools.",
  "acceptance": [
    "describe_package_api, list_dependencies, and find_version_conflicts return structured JSON payloads backed by fixture graphs.",
    "Tool results are query-service driven, not prose strings assembled in the host."
  ],
  "steps": [
    "Implement describe_package_api with package, version, optional TFM, and optional filter inputs.",
    "Implement list_dependencies with workspace and optional project filters.",
    "Implement find_version_conflicts with per-project version details.",
    "Return structured JSON payloads with deterministic field shapes."
  ]
}
```

## Task T4: Implement usage and HTTP tracing tools

```task
{
  "id": "T4",
  "title": "Implement usage and HTTP tracing tools",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T1",
    "T2"
  ],
  "paths": [
    "src/CSharpDllGraph.Mcp/",
    "src/CSharpDllGraph.Engine/",
    "src/CSharpDllGraph.Providers.Dotnet/"
  ],
  "goal": "Implement find_usages and trace_http_call with sorted, workspace-aware results.",
  "acceptance": [
    "find_usages returns grouped and sorted results with enough file and workspace context for MCP clients.",
    "trace_http_call returns producer endpoints and consumer call sites across registered workspaces."
  ],
  "steps": [
    "Implement find_usages with symbol lookup by id or structured name input.",
    "Group usage results by workspace and sort by workspace, file path, and source position.",
    "Implement trace_http_call with normalized method and path lookup across workspaces.",
    "Return producer endpoints and consumer call sites with workspace and source labels."
  ]
}
```

## Task T5: Implement usage suggestion tool

```task
{
  "id": "T5",
  "title": "Implement usage suggestion tool",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T3",
    "T4"
  ],
  "paths": [
    "src/CSharpDllGraph.Mcp/",
    "src/CSharpDllGraph.Engine/"
  ],
  "goal": "Implement suggest_usage to return ranked canonical call sites from observed graph usage.",
  "acceptance": [
    "suggest_usage ranks candidate call sites and returns short source excerpts from real user code.",
    "Ranking and tie-breaking are deterministic."
  ],
  "steps": [
    "Collect inbound Calls edges for the requested target symbol.",
    "Rank candidates by frequency, recency, diversity, and brevity.",
    "Read source excerpts around each selected call site.",
    "Return top results with deterministic ordering for ties."
  ]
}
```

## Task T6: Wire MCP host methods

```task
{
  "id": "T6",
  "title": "Wire MCP host methods",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T3",
    "T4",
    "T5"
  ],
  "paths": [
    "src/CSharpDllGraph.Mcp/",
    "src/CSharpDllGraph.Engine/"
  ],
  "goal": "Expose all six MCP tools through the stdio host with reusable query services behind the host layer.",
  "acceptance": [
    "The MCP server exposes all six tools through [McpServerTool] methods.",
    "Host methods delegate to reusable query services instead of embedding tool logic inline."
  ],
  "steps": [
    "Add six [McpServerTool] methods with consistent parameter names and XML docs.",
    "Move tool logic behind reusable query services instead of embedding logic in the host.",
    "Keep tool contracts aligned with the locked v1 MCP surface.",
    "Verify no tool path introduces LLM or embedding dependencies."
  ]
}
```

## Task T7: Add phase 06 end-to-end tests

```task
{
  "id": "T7",
  "title": "Add phase 06 end-to-end tests",
  "status": "[x]",
  "agent": "backend-testing",
  "dependsOn": [
    "T6"
  ],
  "paths": [
    "tests/CSharpDllGraph.Mcp.Tests/",
    "tests/CSharpDllGraph.Providers.Dotnet.Tests/",
    "tests/Fixtures/"
  ],
  "goal": "Add end-to-end tests that call every MCP tool against fixture graphs and assert deterministic response shapes.",
  "acceptance": [
    "End-to-end MCP protocol tests call every tool against fixture graphs.",
    "Tests assert deterministic response shapes for all six tools.",
    "Tests prove trace_http_call works across a two-workspace fixture."
  ],
  "steps": [
    "Add end-to-end MCP protocol tests that call every tool against fixture graphs.",
    "Assert response shapes against deterministic schema fixtures.",
    "Cover a two-workspace fixture for trace_http_call.",
    "Keep tests focused on observable payload behavior."
  ]
}
```

## Task T8: Pass validate-stop gate

```task
{
  "id": "T8",
  "title": "Pass validate-stop gate",
  "status": "[x]",
  "agent": "backend-testing",
  "dependsOn": [
    "T7"
  ],
  "paths": [
    "plans/2026-04-17_phase-06-mcp-tools.md",
    "tests/",
    "src/"
  ],
  "goal": "Verify phase 06 end-to-end and stop before phase 07.",
  "acceptance": [
    "All six tools are callable through MCP and return expected shapes.",
    "Cross-workspace trace_http_call works on fixtures.",
    "A signed validate-stop summary is ready and execution halts before phase 07."
  ],
  "steps": [
    "Run the phase 06 validation commands from plan-meta.",
    "Confirm all six tools are callable through MCP and return expected shapes.",
    "Confirm trace_http_call works across a two-workspace fixture.",
    "Print a signed summary and halt for human sign-off."
  ]
}
```

## Task T9: Sync product definition

```task
{
  "id": "T9",
  "title": "Sync product definition",
  "status": "[~]",
  "agent": "docs-product",
  "dependsOn": [
    "T8"
  ],
  "paths": [
    "docs/PROJECT_DEFINITION.md",
    "docs/REQUIREMENT_DEFINITION.md"
  ],
  "goal": "Align product and requirement docs with delivered phase 06 MCP behavior after validation.",
  "acceptance": [
    "Touched phase 06 requirements are updated in docs/REQUIREMENT_DEFINITION.md.",
    "Final response states Product definition: updated or Product definition: no update required."
  ],
  "steps": [
    "Review docs/PROJECT_DEFINITION.md against delivered MCP tool behavior.",
    "Update touched phase 06 requirements in docs/REQUIREMENT_DEFINITION.md.",
    "Update docs/PROJECT_DEFINITION.md only if scope, users, flows, rules, constraints, or success metrics changed.",
    "Respond with Product definition: updated or Product definition: no update required."
  ]
}
```
