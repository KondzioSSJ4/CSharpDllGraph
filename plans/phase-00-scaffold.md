# Plan: Phase 00 — Scaffold

> Create the .NET solution, project layout, shared build targets, mandatory product docs, and an empty MCP host that boots on stdio. No graph logic yet.

Reference: `G:\GIT\KanbnMCP` for MCP stdio wiring shape. Match conventions, do not copy files.

## [ ] Task 1: Solution and project skeleton

Goal: Empty but buildable .NET solution with every v1 project wired.

Acceptance: `dotnet build` succeeds from repo root; `dotnet test` runs (0 tests is acceptable).

- [ ] 1.1 Create `CSharpDllGraph.slnx` at repo root listing `src/` and `tests/` folders
- [ ] 1.2 Create `src/CSharpDllGraph.Engine/CSharpDllGraph.Engine.csproj` (classlib)
- [ ] 1.3 Create `src/CSharpDllGraph.Providers.Dotnet/CSharpDllGraph.Providers.Dotnet.csproj` (classlib) referencing Engine
- [ ] 1.4 Create `src/CSharpDllGraph.Mcp/CSharpDllGraph.Mcp.csproj` (exe) referencing Engine + Providers.Dotnet + `ModelContextProtocol` 1.1.0
- [ ] 1.5 Create `src/CSharpDllGraph.Cli/CSharpDllGraph.Cli.csproj` (exe) referencing Engine + Providers.Dotnet
- [ ] 1.6 Create xUnit test project per src project under `tests/<Project>.Tests/`
- [ ] 1.7 Register every project in `.slnx`

## [ ] Task 2: Shared build configuration

Goal: Central properties via `Directory.Build.targets` to avoid repetition across csprojs.

- [ ] 2.1 Create `Directory.Build.targets` at repo root with `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`
- [ ] 2.2 Remove redundant properties from individual csprojs
- [ ] 2.3 Verify `dotnet build` succeeds with zero warnings

## [ ] Task 3: Product and requirement definitions

Goal: Create `docs/PROJECT_DEFINITION.md` and `docs/REQUIREMENT_DEFINITION.md` with initial v1 scope.

Context: Follow KanbnMCP convention — Polish prose in these docs, English elsewhere. `REQUIREMENT_DEFINITION.md` uses `[x] [~] [ ]` status symbols.

- [ ] 3.1 `docs/PROJECT_DEFINITION.md` — cel produktu, użytkownicy (AI w trybie code-gen, developerzy), scope v1 (6 narzędzi MCP), non-goals (brak runtime capture, brak LLM w narzędziach, brak merge workspace-ów, brak providerów innych niż .NET)
- [ ] 3.2 `docs/REQUIREMENT_DEFINITION.md` — wymagania pogrupowane po fazach 0–7, każde ze statusem `[ ]`
- [ ] 3.3 Nagłówek `PROJECT_DEFINITION` wskazuje `plans/README.md` jako źródło faz i porządku

## [ ] Task 4: AGENTS.md workflow

Goal: Root-level `AGENTS.md` describing execution rules binding for every future agent.

- [ ] 4.1 Wymóg czytania `PROJECT_DEFINITION` + `REQUIREMENT_DEFINITION` na początku każdego promptu
- [ ] 4.2 Definicja symboli statusu `[x] [~] [ ]`
- [ ] 4.3 Standard jakości testów: realne zachowania, bez tautologicznych mocków; testy dodajemy wyłącznie na żądanie
- [ ] 4.4 Język: kod i identyfikatory po angielsku; dokumenty produktowe po polsku
- [ ] 4.5 Reguła STOP po każdej fazie — link do `plans/README.md`; zakaz auto-otwierania kolejnej fazy

## [ ] Task 5: Empty MCP host on stdio

Goal: Runnable MCP server with a single `ping` tool, confirms process wiring.

Context: Mirror shape from `G:\GIT\KanbnMCP\src\KanbnMcpGateway\Program.cs` — `Host.CreateEmptyApplicationBuilder`, file logging under `logs/`, `AddMcpServer().WithStdioServerTransport().WithTools<T>()`. Do not copy files; reproduce the structure with names specific to this project.

Acceptance: Running the Mcp project and sending a JSON-RPC `initialize` over stdin yields a valid `initialize` response; sending a `ping` tool call returns `"pong"`.

- [ ] 5.1 `src/CSharpDllGraph.Mcp/Program.cs` — host builder with configuration layering (`appsettings.json`, `appsettings.Local.json`, env vars)
- [ ] 5.2 File logging to `logs/mcp-actions.log` via a minimal `FileLoggerProvider` (reimplement; do not import)
- [ ] 5.3 `CSharpDllGraphTools` class with a single `[McpServerTool]` method `ping` returning `"pong"`
- [ ] 5.4 `appsettings.json` with defaults, copied to output via `PreserveNewest`
- [ ] 5.5 Smoke-test manually: start the process, feed an `initialize` request over stdin, confirm response, terminate cleanly

## [ ] Task 6: Sync product definition

Goal: Confirm `PROJECT_DEFINITION` reflects exactly what phase 0 delivered.

- [ ] 6.1 Review `docs/PROJECT_DEFINITION.md` against the created scaffold
- [ ] 6.2 Update where applicable; preserve internal consistency
- [ ] 6.3 Respond with `Product definition: updated` or `Product definition: no update required`

## VALIDATE-STOP CHECKLIST

Before ending this phase the executor MUST:

- [ ] `dotnet build` succeeds with zero warnings
- [ ] `dotnet test` runs (0 tests is acceptable)
- [ ] `dotnet run --project src/CSharpDllGraph.Mcp` accepts MCP `initialize` over stdin and exits cleanly on stdin close
- [ ] `docs/PROJECT_DEFINITION.md` and `docs/REQUIREMENT_DEFINITION.md` exist and are non-empty
- [ ] `AGENTS.md` at repo root references both docs and links `plans/README.md`
- [ ] Every phase-0 item in `REQUIREMENT_DEFINITION.md` marked `[x]` or `[~]` with reason
- [ ] Print signed summary: what shipped, what remains for phase 01
- [ ] **HALT. Do not open `phase-01-graph-store.md`. Wait for human sign-off.**
