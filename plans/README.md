# CSharpDllGraph — Implementation Plans

Multi-phase plan. Each phase is a standalone file. The AI executor MUST halt at the end of every phase (VALIDATE-STOP gate) and wait for explicit human sign-off before opening the next phase file.

## Reference conventions

MCP stdio wiring follows the shape of `G:\GIT\KanbnMCP` (do not copy; match conventions):

- .NET 10, `ModelContextProtocol` v1.1.0, `WithStdioServerTransport`, `WithTools<T>`
- `.slnx` solution, `src/<Project>/` + `tests/<Project>.Tests/`
- `Directory.Build.targets` at repo root
- `docs/PROJECT_DEFINITION.md` + `docs/REQUIREMENT_DEFINITION.md` — Polish content, English identifiers
- `AGENTS.md` at repo root
- Status symbols in REQUIREMENT_DEFINITION: `[x]` done, `[~]` partial, `[ ]` todo
- Test quality: real behavior, no tautological mocks; do not add tests unless the user asked

## Phases

| # | File | Focus | Stop gate |
|---|------|-------|-----------|
| 0 | [phase-00-scaffold.md](phase-00-scaffold.md) | Solution, projects, docs skeleton, empty MCP host | MCP host boots on stdio, answers `initialize`, exits cleanly |
| 1 | [2026-04-17_phase-01-graph-store.md](2026-04-17_phase-01-graph-store.md) | Graph schema + JSON store + versioned node keys | Round-trip byte-equality proven for synthetic graph |
| 2 | [2026-04-17_phase-02-dotnet-provider.md](2026-04-17_phase-02-dotnet-provider.md) | C# provider: package / assembly / type / method nodes + structural edges | Provider ingests sample `.nupkg` + `.slnx` and emits expected structural graph |
| 3 | [2026-04-17_phase-03-usage-edges.md](2026-04-17_phase-03-usage-edges.md) | Usage edges via Roslyn semantic model | User-code → NuGet symbol edges with correct version tags |
| 4 | [2026-04-17_phase-04-http-static.md](2026-04-17_phase-04-http-static.md) | HTTP static analysis (ASP.NET + Minimal API + fetch/axios) | Producer + consumer nodes matched by normalized URL template |
| 5 | [2026-04-17_phase-05-http-specs.md](2026-04-17_phase-05-http-specs.md) | OpenAPI/Swagger + `.http` + Postman ingestion | Spec-sourced endpoints reconcile with static-analysis endpoints |
| 6 | [2026-04-17_phase-06-mcp-tools.md](2026-04-17_phase-06-mcp-tools.md) | All 6 MCP tools + cross-workspace URL registry | All tools return expected payloads against fixtures |
| 7 | [2026-04-17_phase-07-cli-watcher.md](2026-04-17_phase-07-cli-watcher.md) | CLI, file watcher, incremental rebuild | Edit → rebuild → query latency under 2s on sample |

**Active phase: `2026-04-17_phase-01-graph-store.md`.**

## v1 MCP tool surface (locked)

None of these tools use an LLM. All are pure static analysis over the graph.

- `describe_package_api` — public surface of a nuget at version X
- `find_usages` — where symbol X is used in user code
- `trace_http_call` — which callers hit endpoint X (cross-workspace)
- `list_dependencies` — resolved package versions per project
- `find_version_conflicts` — same package at different versions across solution
- `suggest_usage` — canonical call sites of a symbol extracted from existing user code

## Non-goals for v1

- No runtime HTTP capture
- No LLM or embeddings in any tool
- No automatic cross-workspace pre-merge (resolve at query time only)
- No non-C# providers shipped (seam reserved in phase 2)

## Execution protocol for every phase

1. Read the phase file in full before touching code.
2. Read `docs/PROJECT_DEFINITION.md` and `docs/REQUIREMENT_DEFINITION.md` at the start of every task (once those exist after phase 0).
3. Update `REQUIREMENT_DEFINITION.md` statuses as work progresses.
4. At end of phase: complete the VALIDATE-STOP CHECKLIST, print a signed summary, then HALT. Do NOT open the next phase file.
