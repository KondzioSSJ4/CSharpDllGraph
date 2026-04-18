# Plan: Phase 07 - CLI, Watcher, and Incremental Rebuild

> Finish v1 with CLI flows, incremental rebuild, watcher, validation, doc sync.

```plan-meta
{
  "version": 1,
  "provider": "codex",
  "model": "gpt-5.4",
  "maxParallel": 2,
  "validation": [
    "dotnet build CSharpDllGraph.slnx"
  ]
}
```

## Task T1: Add CLI command surface

```task
{
  "id": "T1",
  "title": "Add CLI command surface",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [],
  "paths": [
    "src/CSharpDllGraph.Cli/",
    "src/CSharpDllGraph.Engine/"
  ],
  "goal": "Expose deterministic CLI commands for build, update, query, and workspace registry flows.",
  "acceptance": [
    "CLI supports build, update, query, workspace add, workspace list, and workspace remove commands through deterministic parsing."
  ],
  "steps": [
    "Inspect current CLI shell and workspace registry entrypoints.",
    "Add command parsing with System.CommandLine or equivalent deterministic parser.",
    "Implement build and update commands wired to existing engine flows.",
    "Implement query and workspace registry commands with stable argument mapping."
  ]
}
```

## Task T2: Track content hashes for incremental rebuild

```task
{
  "id": "T2",
  "title": "Track content hashes for incremental rebuild",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T1"
  ],
  "paths": [
    "src/CSharpDllGraph.Engine/Graph/",
    "src/CSharpDllGraph.Engine/"
  ],
  "goal": "Persist supported input hashes and use them to detect minimal rebuild scope during update.",
  "acceptance": [
    "manifest.json stores hashes for supported inputs and update compares current state with previous state before choosing extractors."
  ],
  "steps": [
    "Extend workspace manifest structures with hash data for source, project, dependency, and HTTP-spec inputs.",
    "Persist hash maps during full rebuild.",
    "Load previous hashes during update and compute deltas.",
    "Map changed input kinds to minimal extractor execution."
  ]
}
```

## Task T3: Apply edge-level incremental updates

```task
{
  "id": "T3",
  "title": "Apply edge-level incremental updates",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T2"
  ],
  "paths": [
    "src/CSharpDllGraph.Engine/",
    "tests/"
  ],
  "goal": "Replace only graph fragments owned by changed files without leaving stale nodes or edges.",
  "acceptance": [
    "Changing one source file updates only owned nodes and edges, and endpoint provenance remains correct after reconciliation."
  ],
  "steps": [
    "Define removal and replacement rules per graph node and edge kind.",
    "Implement selective cleanup for user symbols and HTTP artifacts.",
    "Re-run reconciliation when endpoint source sets shrink or move.",
    "Add regression coverage for one-file deltas and exact graph diff assertions."
  ]
}
```

## Task T4: Add watcher mode

```task
{
  "id": "T4",
  "title": "Add watcher mode",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T3"
  ],
  "paths": [
    "src/CSharpDllGraph.Cli/",
    "src/CSharpDllGraph.Engine/"
  ],
  "goal": "Keep graph data current during local development through debounced filesystem watching.",
  "acceptance": [
    "Watcher batches file changes, triggers incremental updates, logs activity, and shuts down cleanly on Ctrl+C."
  ],
  "steps": [
    "Add FileSystemWatcher rooted at workspace inputs.",
    "Debounce and batch file events by path and time window.",
    "Trigger incremental update runs from debounced batches.",
    "Handle graceful cancellation and watcher lifecycle logging."
  ]
}
```

## Task T5: Reuse MCP query layer from CLI

```task
{
  "id": "T5",
  "title": "Reuse MCP query layer from CLI",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T1"
  ],
  "paths": [
    "src/CSharpDllGraph.Mcp/",
    "src/CSharpDllGraph.Cli/",
    "src/CSharpDllGraph.Engine/"
  ],
  "goal": "Make CLI query commands and MCP tools call the same graph query services.",
  "acceptance": [
    "CLI query output and MCP tool responses are produced by shared service implementations with thin entrypoint wrappers."
  ],
  "steps": [
    "Inspect current MCP tool service boundaries.",
    "Extract or finalize shared graph query abstractions.",
    "Wire CLI query command to the shared services.",
    "Keep MCP methods as thin wrappers and verify response shape consistency."
  ]
}
```

## Task T6: Prove performance target

```task
{
  "id": "T6",
  "title": "Prove performance target",
  "status": "[x]",
  "agent": "backend-testing",
  "dependsOn": [
    "T3",
    "T4",
    "T5"
  ],
  "paths": [
    "tests/",
    "docs/REQUIREMENT_DEFINITION.md"
  ],
  "goal": "Measure edit-to-query latency on the fixture workspace and record the observed result.",
  "acceptance": [
    "A repeatable measurement exists under tests, the measured latency is recorded in docs/REQUIREMENT_DEFINITION.md, and any miss is documented without lowering the target."
  ],
  "steps": [
    "Add benchmark or integration measurement for edit, rebuild, and query latency.",
    "Run the measurement on the sample fixture workspace.",
    "Record the measured value in docs/REQUIREMENT_DEFINITION.md.",
    "Document the gap clearly if the target is missed."
  ]
}
```

## Task T7: Pass final validate-stop gate

```task
{
  "id": "T7",
  "title": "Pass final validate-stop gate",
  "status": "[x]",
  "agent": "backend-testing",
  "dependsOn": [
    "T4",
    "T5",
    "T6"
  ],
  "paths": [
    "plans/",
    "src/CSharpDllGraph.Cli/",
    "tests/",
    "docs/REQUIREMENT_DEFINITION.md"
  ],
  "goal": "Verify final CLI, watcher, and incremental rebuild behavior and produce the signed delivery summary without editing plan state.",
  "acceptance": [
    "CLI flows work end to end, incremental rebuild works on fixture data, watcher-driven updates are validated against the target or the shortfall is documented, and the signed final summary is printed."
  ],
  "steps": [
    "Run the phase VALIDATE-STOP checklist from the phase plan and capture outcomes without editing the phase plan file.",
    "Verify CLI build, update, query, and workspace flows end to end.",
    "Verify fixture incremental rebuild and watcher-driven updates.",
    "Print the signed final v1 delivery summary and stop for human direction."
  ]
}
```

## Task T8: Sync product definition

```task
{
  "id": "T8",
  "title": "Sync product definition",
  "status": "[x]",
  "agent": "docs-product",
  "dependsOn": [
    "T7"
  ],
  "paths": [
    "docs/PROJECT_DEFINITION.md",
    "docs/REQUIREMENT_DEFINITION.md"
  ],
  "goal": "Align final product and requirement documents with verified phase 07 delivery state.",
  "acceptance": [
    "docs/PROJECT_DEFINITION.md reflects final v1 delivery state when needed, docs/REQUIREMENT_DEFINITION.md statuses are updated to verified values, and the final response states whether product definition changed."
  ],
  "steps": [
    "Review docs/PROJECT_DEFINITION.md against final v1 delivery state.",
    "Update touched requirement statuses in docs/REQUIREMENT_DEFINITION.md to verified values.",
    "Report Product definition: updated or Product definition: no update required."
  ]
}
```
