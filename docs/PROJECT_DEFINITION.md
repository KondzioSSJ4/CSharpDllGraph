# CSharpDllGraph — Product Definition

## What is CSharpDllGraph

CSharpDllGraph is an MCP (Model Context Protocol) server and CLI toolset for static graph analysis of .NET ecosystems.

It builds a dependency graph from a `.sln` or `.slnx` file and exposes precise query tools for language models working in code-generation mode and for developers navigating large codebases without opening an IDE.

No tool uses a language model or vector embeddings — all logic is based on static analysis of source code and NuGet package metadata.

---

## Who is it for

| User | Usage mode | Primary need |
|---|---|---|
| Language model (AI in code-gen mode) | MCP client (stdio), one server per project | Precise, context-minimal responses without hallucinations |
| Developer | CLI / MCP client | Fast navigation of large codebases without opening an IDE |

---

## Runtime model

One MCP process serves exactly one workspace. Configuration comes from the `--workspace-path` argument or a `.csharpdllgraph.json` file searched upward from the current directory.

`.csharpdllgraph.json` fields:

- `workspacePath` (required)
- `graphPath` (optional — defaults to `<workspacePath>/.csharpdllgraph/graph`)
- `solutionPath` (optional)

`--workspace-path` takes priority over the config file. If neither source is available, the server exits with an error.

On startup the server auto-builds the graph if `manifest.json` is missing, then keeps it fresh via an embedded file watcher with incremental rebuild (edit → rebuild → query latency under 2 s).

---

## CLI

- `build <path-to-sln>` — full graph build, output written to `.csharpdllgraph/`
- `update` — incremental update based on changed files
- `watch` — continuous mode: detects `.cs` and `.csproj` changes and rebuilds automatically

After every `build` or `update`, a `graph.html` file is written to `.csharpdllgraph/graph.html`.

---

## MCP tools

All tools are read-only and do not modify user code. The workspace is configured at server startup — tool signatures require no workspace-selection parameter.

| # | Tool | Description |
|---|---|---|
| 1 | `describe_package_api` | Public surface of a NuGet package at a given version |
| 2 | `find_usages` | Places where symbol X is used in user code |
| 3 | `trace_http_call` | Callers that hit endpoint X (cross-workspace) |
| 4 | `list_dependencies` | Resolved package versions per project |
| 5 | `find_version_conflicts` | Same package at different versions across the solution |
| 6 | `suggest_usage` | Canonical call sites of a symbol extracted from existing user code |

---

## Graph model

The graph represents the full structure of a .NET solution as a network of nodes and edges.

**Nodes:** `Workspace`, `Project`, `Package`, `Assembly`, `Namespace`, `Type`, `Method`, `HttpEndpoint`, `HttpCallSite`, `ExternalRef`

**Edges:** `Contains`, `References`, `DependsOn`, `Implements`, `Inherits`, `Calls`, `Uses`, `HandlesRoute`, `CallsRoute`, `DescribedBy`

Graph data is stored per project in type-sharded JSON files (`manifest.json`, `nodes/*.json`, `edges/*.json`). Node keys are deterministic (`{kind}:{fqn}@{version}`), enabling diff-based comparison between rebuilds.

---

## Source analysis

The tool detects and analyzes:

- NuGet dependencies from `project.assets.json` (types, methods, assemblies) — with per-package cache (`name@version`) to speed up subsequent builds
- Symbol usage edges between methods (Roslyn)
- HTTP endpoints: ASP.NET routing attributes (`[HttpGet]`, `[Route]`), Minimal API (`app.MapGet`)
- HTTP call sites in JS/TS files (`fetch`, `axios`)
- HTTP specifications: OpenAPI/Swagger (JSON and YAML), `.http` files, Postman collections
- Mismatches between specifications and source code

---

## Graph visualization

After every build the CLI generates `graph.html` — a self-contained HTML file with a D3.js force-directed graph. No server or network connection required.

UI features:
- Filter nodes and edges by type
- Click a node to see its attributes and source locations
- Search with node highlighting

---

## Non-goals for v1

- **No runtime capture** — the tool does not intercept live HTTP traffic.
- **No LLM in tools** — no tool calls a language model or embeddings.
- **No multi-workspace routing in one MCP process** — one process serves one workspace.
- **No non-.NET providers** — the provider seam is reserved for future use; no other provider ships in v1.

---

## Technical requirements

- Platform: .NET 10
- MCP library: `ModelContextProtocol` v1.1.0, transport `WithStdioServerTransport`
- Solution format: `.slnx`
