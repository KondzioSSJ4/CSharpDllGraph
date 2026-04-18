# Plan: Phase 09 — Static HTML Graph Visualizer

> Add a self-contained graph.html that the CLI injects with graph data and the user opens directly in a browser — no server needed.

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

## Task T1: Create graph.html template as embedded resource

```task
{
  "id": "T1",
  "title": "Create graph.html template as embedded resource",
  "status": "[ ]",
  "agent": "backend-csharp",
  "dependsOn": [],
  "paths": [
    "src/CSharpDllGraph.Cli/Assets/graph.html",
    "src/CSharpDllGraph.Cli/CSharpDllGraph.Cli.csproj"
  ],
  "goal": "Create a single self-contained HTML file with inline D3.js that renders the workspace graph from window.GRAPH_DATA injected by the CLI.",
  "acceptance": [
    "File exists at src/CSharpDllGraph.Cli/Assets/graph.html",
    "File is marked as EmbeddedResource in CSharpDllGraph.Cli.csproj",
    "HTML contains placeholder comment /*GRAPH_DATA_PLACEHOLDER*/ inside a <script> block",
    "When GRAPH_DATA is populated, force-directed graph renders in browser without a server",
    "Node colors differ per NodeKind (Workspace, Project, Package, Assembly, Namespace, Type, Method, HttpEndpoint, HttpCallSite, ExternalRef)",
    "Filter panel has checkboxes per NodeKind and per EdgeKind",
    "Clicking a node shows sidebar with DisplayName, Kind, Attributes key/value, SourceRefs file+line",
    "Search box highlights matching nodes",
    "Dark theme, legend visible",
    "No external network requests — all JS/CSS inline"
  ],
  "steps": [
    "Create directory src/CSharpDllGraph.Cli/Assets/",
    "Download minified D3.js v7 source and inline it into the HTML file",
    "Build the full HTML template: dark-themed layout with left filter panel, center SVG canvas, right detail sidebar",
    "Define color map for NodeKind values: Workspace=#6366f1, Project=#22c55e, Package=#f59e0b, Assembly=#94a3b8, Namespace=#38bdf8, Type=#e879f9, Method=#fb923c, HttpEndpoint=#34d399, HttpCallSite=#f472b6, ExternalRef=#a3a3a3",
    "Implement D3 force simulation: forceLink (edges), forceManyBody (repulsion), forceCenter",
    "Implement filter panel: two checkbox groups (NodeKind, EdgeKind); toggling rebuilds visible nodes/edges sets and restarts simulation",
    "Implement click handler: populate sidebar with node.displayName, node.kind, all node.attributes entries, all node.sourceRefs with file + spans",
    "Implement search input: on input, highlight matching nodes with a stroke ring, dim non-matching",
    "Add legend: colored squares with NodeKind labels",
    "Place data injection point: <script>const GRAPH_DATA = /*GRAPH_DATA_PLACEHOLDER*/;</script> near top of body scripts",
    "Add to CSharpDllGraph.Cli.csproj: <ItemGroup><EmbeddedResource Include=\"Assets\\graph.html\" /></ItemGroup>"
  ]
}
```

## Task T2: Implement GraphHtmlExporter

```task
{
  "id": "T2",
  "title": "Implement GraphHtmlExporter",
  "status": "[ ]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T1"
  ],
  "paths": [
    "src/CSharpDllGraph.Cli/Export/GraphHtmlExporter.cs"
  ],
  "goal": "Create GraphHtmlExporter that reads graph JSON shards, serializes them into the HTML template, and writes graph.html to .csharpdllgraph/.",
  "acceptance": [
    "GraphHtmlExporter.ExportAsync(graphPath, workspaceRoot, ct) reads nodes/*.json and edges/*.json shards and manifest.json from graphPath",
    "Loads graph.html template from Assembly.GetManifestResourceStream (embedded resource)",
    "Replaces /*GRAPH_DATA_PLACEHOLDER*/ with JSON object: { manifest, nodes, edges }",
    "Writes result to {workspaceRoot}/.csharpdllgraph/graph.html, overwriting if exists",
    "Creates .csharpdllgraph/ directory if missing",
    "dotnet build CSharpDllGraph.slnx passes"
  ],
  "steps": [
    "Create src/CSharpDllGraph.Cli/Export/GraphHtmlExporter.cs with static class GraphHtmlExporter",
    "Add method: public static async Task ExportAsync(string graphPath, string workspaceRoot, CancellationToken ct)",
    "In ExportAsync: read manifest.json via File.ReadAllTextAsync; read all *.json files under graphPath/nodes/ and graphPath/edges/ into JsonDocument lists",
    "Merge nodes from all shard files into a single JsonElement array; same for edges",
    "Build combined payload: JsonObject with manifest (raw JsonElement), nodes (merged array), edges (merged array)",
    "Serialize payload to string with JsonSerializerOptions (no indentation for smaller file)",
    "Load template: Assembly.GetExecutingAssembly().GetManifestResourceStream(\"CSharpDllGraph.Cli.Assets.graph.html\") → StreamReader → ReadToEnd",
    "Replace the literal string /*GRAPH_DATA_PLACEHOLDER*/ in template with serialized payload",
    "Ensure output dir exists: Directory.CreateDirectory(Path.Combine(workspaceRoot, \".csharpdllgraph\"))",
    "Write result to {workspaceRoot}/.csharpdllgraph/graph.html via File.WriteAllTextAsync"
  ]
}
```

## Task T3: Wire GraphHtmlExporter into build and update commands

```task
{
  "id": "T3",
  "title": "Wire GraphHtmlExporter into build and update commands",
  "status": "[ ]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T2"
  ],
  "paths": [
    "src/CSharpDllGraph.Cli/Program.cs"
  ],
  "goal": "Call GraphHtmlExporter.ExportAsync after every successful build and update command and print the output path.",
  "acceptance": [
    "After `build` completes successfully, graph.html is written and console prints: Graph visualization: .csharpdllgraph/graph.html",
    "After `update` completes successfully, same behavior",
    "dotnet build CSharpDllGraph.slnx passes"
  ],
  "steps": [
    "In RunBuildAsync in src/CSharpDllGraph.Cli/Program.cs, after WriteJson(new BuildCommandResult(...)), add: await GraphHtmlExporter.ExportAsync(graphPath, resolvedWorkspaceRoot, cancellationToken)",
    "Print to console: Console.WriteLine(\"Graph visualization: .csharpdllgraph/graph.html\")",
    "Verify build and update both flow through RunBuildAsync (they do — isUpdate flag differentiates them)"
  ]
}
```

## Task T4: Update .gitignore

```task
{
  "id": "T4",
  "title": "Update .gitignore for generated HTML",
  "status": "[ ]",
  "agent": "docs-product",
  "dependsOn": [
    "T3"
  ],
  "paths": [
    ".gitignore"
  ],
  "goal": "Ensure generated graph.html and any tmp files under .csharpdllgraph/ are excluded from git.",
  "acceptance": [
    ".gitignore contains .csharpdllgraph/graph.html",
    ".gitignore contains .csharpdllgraph/tmp/"
  ],
  "steps": [
    "Open .gitignore and locate the CSharpDllGraph section (already has .csharpdllgraph/ entry)",
    "Add specific entries: .csharpdllgraph/graph.html and .csharpdllgraph/tmp/ below the existing .csharpdllgraph/ line"
  ]
}
```

## Task T5: Document graph visualization in README.md

```task
{
  "id": "T5",
  "title": "Document graph visualization in README.md",
  "status": "[ ]",
  "agent": "docs-product",
  "dependsOn": [
    "T4"
  ],
  "paths": [
    "README.md"
  ],
  "goal": "Add a Graph Visualization section to README.md explaining how to open and use graph.html.",
  "acceptance": [
    "README.md has a 'Graph Visualization' section",
    "Section explains that graph.html is auto-generated after build/update",
    "Section explains to open .csharpdllgraph/graph.html in any browser — no server needed",
    "Section mentions filter panel, node click for details, and search"
  ],
  "steps": [
    "Add 'Graph Visualization' section after the 'Run the CLI' section",
    "Explain: after every build or update command, graph.html is written to .csharpdllgraph/graph.html",
    "Explain how to open: open .csharpdllgraph/graph.html in any browser",
    "List UI features: force-directed graph, filter by node/edge type, click node for attributes and source locations, search to highlight nodes"
  ]
}
```
