# Plan: Phase 00 — Scaffold

> Create the .NET solution, project layout, shared build targets, mandatory product docs, and an empty MCP host that boots on stdio. No graph logic yet.

Reference: `G:\GIT\KanbnMCP` for MCP stdio wiring shape. Match conventions, do not copy files.

## [x] Task 1: Solution and project skeleton

Goal: Empty but buildable .NET solution with every v1 project wired.

Acceptance: `dotnet build` succeeds from repo root; `dotnet test` runs (0 tests is acceptable).

- [x] 1.1 Create `CSharpDllGraph.slnx` at repo root listing `src/` and `tests/` folders
- [x] 1.2 Create `src/CSharpDllGraph.Engine/CSharpDllGraph.Engine.csproj` (classlib)
- [x] 1.3 Create `src/CSharpDllGraph.Providers.Dotnet/CSharpDllGraph.Providers.Dotnet.csproj` (classlib) referencing Engine
- [x] 1.4 Create `src/CSharpDllGraph.Mcp/CSharpDllGraph.Mcp.csproj` (exe) referencing Engine + Providers.Dotnet + `ModelContextProtocol` 1.1.0
- [x] 1.5 Create `src/CSharpDllGraph.Cli/CSharpDllGraph.Cli.csproj` (exe) referencing Engine + Providers.Dotnet
- [x] 1.6 Create xUnit test project per src project under `tests/<Project>.Tests/`
- [x] 1.7 Register every project in `.slnx`

## [x] Task 2: Shared build configuration

Goal: Central properties via `Directory.Build.targets` to avoid repetition across csprojs.

- [x] 2.1 Create `Directory.Build.targets` at repo root with `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`
- [x] 2.2 Remove redundant properties from individual csprojs
- [x] 2.3 Verify `dotnet build` succeeds with zero warnings

## [x] Task 3: Product and requirement definitions

Goal: Create `docs/PROJECT_DEFINITION.md` and `docs/REQUIREMENT_DEFINITION.md` with initial v1 scope.

Context: `REQUIREMENT_DEFINITION.md` uses `[x] [~] [ ]` status symbols.

- [x] 3.1 `docs/PROJECT_DEFINITION.md` — product goal, users (AI in code-gen mode, developers), scope v1 (6 MCP tools), non-goals (no runtime capture, no LLM in tools, no workspace merges, no providers other than .NET)
- [x] 3.2 `docs/REQUIREMENT_DEFINITION.md` — requirements grouped by phases 0–7, each with status `[ ]`
- [x] 3.3 `PROJECT_DEFINITION` header points to `plans/README.md` as the source of phases and order

## [x] Task 4: AGENTS.md workflow

Goal: Root-level `AGENTS.md` describing execution rules binding for every future agent.

- [x] 4.1 Requirement to read `PROJECT_DEFINITION` + `REQUIREMENT_DEFINITION` at the start of each prompt
- [x] 4.2 Definition of status symbols `[x] [~] [ ]`
- [x] 4.3 Test quality standard: real behaviors, no tautological mocks; tests added only on request
- [x] 4.4 Language: code and identifiers in English; product documents in English
- [x] 4.5 STOP rule after each phase — link to `plans/README.md`; no auto-opening of the next phase

## [x] Task 5: Empty MCP host on stdio

Goal: Runnable MCP server with a single `ping` tool, confirms process wiring.

Context: Mirror shape from `G:\GIT\KanbnMCP\src\KanbnMcpGateway\Program.cs` — `Host.CreateEmptyApplicationBuilder`, file logging under `logs/`, `AddMcpServer().WithStdioServerTransport().WithTools<T>()`. Do not copy files; reproduce the structure with names specific to this project.

Acceptance: Running the Mcp project and sending a JSON-RPC `initialize` over stdin yields a valid `initialize` response; sending a `ping` tool call returns `"pong"`.

- [x] 5.1 `src/CSharpDllGraph.Mcp/Program.cs` — host builder with configuration layering (`appsettings.json`, `appsettings.Local.json`, env vars)
- [x] 5.2 File logging to `logs/mcp-actions.log` via a minimal `FileLoggerProvider` (reimplement; do not import)
- [x] 5.3 `CSharpDllGraphTools` class with a single `[McpServerTool]` method `ping` returning `"pong"`
- [x] 5.4 `appsettings.json` with defaults, copied to output via `PreserveNewest`
- [x] 5.5 Smoke-test manually: start the process, feed an `initialize` request over stdin, confirm response, terminate cleanly

## [x] Task 6: Sync product definition

Goal: Confirm `PROJECT_DEFINITION` reflects exactly what phase 0 delivered.

- [x] 6.1 Review `docs/PROJECT_DEFINITION.md` against the created scaffold
- [x] 6.2 Update where applicable; preserve internal consistency
- [x] 6.3 Respond with `Product definition: updated` or `Product definition: no update required`

## VALIDATE-STOP CHECKLIST

Before ending this phase the executor MUST:

- [x] `dotnet build` succeeds with zero warnings
- [x] `dotnet test` runs (0 tests is acceptable)
- [x] `dotnet run --project src/CSharpDllGraph.Mcp` accepts MCP `initialize` over stdin and exits cleanly on stdin close
- [x] `docs/PROJECT_DEFINITION.md` and `docs/REQUIREMENT_DEFINITION.md` exist and are non-empty
- [x] `AGENTS.md` at repo root references both docs and links `plans/README.md`
- [x] Every phase-0 item in `REQUIREMENT_DEFINITION.md` marked `[x]` or `[~]` with reason
- [x] Print signed summary: what shipped, what remains for phase 01
- [x] **HALT. Do not open `phase-01-graph-store.md`. Wait for human sign-off.**
