# CSharpDllGraph

CSharpDllGraph is a .NET 10 toolset for static graph analysis of `.NET` solutions.

It has two entry points:

- `CSharpDllGraph.Cli` for building, updating, watching, and querying workspace graphs
- `CSharpDllGraph.Mcp` for exposing the query layer as an MCP server over stdio

The MCP server is read-only. It does not modify user code.

## What it does

The tool builds a graph from a `.sln` or `.slnx` file and exposes queries for:

- package API inspection
- dependency listing
- version conflict detection
- symbol usage lookup
- canonical usage suggestions
- HTTP call tracing across registered workspaces

## Requirements

- .NET 10 SDK
- Windows, macOS, or Linux
- a `.sln` or `.slnx` file in the target workspace

## Build

```bash
dotnet build CSharpDllGraph.slnx
```

## Run the CLI

Show CLI help:

```bash
dotnet run --project src/CSharpDllGraph.Cli -- help
```

Build graph for a workspace:

```bash
dotnet run --project src/CSharpDllGraph.Cli -- build . --name csharpdllgraph
```

Build graph for a specific solution:

```bash
dotnet run --project src/CSharpDllGraph.Cli -- build . --solution CSharpDllGraph.slnx --name csharpdllgraph
```

Update existing graph:

```bash
dotnet run --project src/CSharpDllGraph.Cli -- update . --name csharpdllgraph
```

Watch workspace and rebuild incrementally:

```bash
dotnet run --project src/CSharpDllGraph.Cli -- watch . --solution CSharpDllGraph.slnx --name csharpdllgraph
```

List registered workspaces:

```bash
dotnet run --project src/CSharpDllGraph.Cli -- workspace list
```

Run a query from CLI:

```bash
dotnet run --project src/CSharpDllGraph.Cli -- query list_dependencies --workspace csharpdllgraph
```

Registry location:

- Windows: `%APPDATA%\CSharpDllGraph\workspaces.json`
- Linux/macOS: `$XDG_CONFIG_HOME/csharpdllgraph/workspaces.json` or `~/.config/csharpdllgraph/workspaces.json`

Default graph output path:

- `<workspace-root>/.csharpdllgraph/graph`

## Run the MCP server

Start the MCP server over stdio:

```bash
dotnet run --project src/CSharpDllGraph.Mcp --
```

Important:

- register at least one workspace first with the CLI
- the MCP server reads workspace registrations from the registry file above
- the default log file is `src/CSharpDllGraph.Mcp/logs/mcp-actions.log` when started from that project folder

## Quick local smoke test

Use the fixture workspace:

```bash
dotnet run --project src/CSharpDllGraph.Cli -- build tests/Fixtures/SampleApi --name sample-api
dotnet run --project src/CSharpDllGraph.Cli -- query trace_http_call --method GET --path /users --workspace sample-api
```

Then start MCP:

```bash
dotnet run --project src/CSharpDllGraph.Mcp --
```

## MCP tools exposed by the server

- `ping`
- `describe_package_api`
- `list_dependencies`
- `find_version_conflicts`
- `find_usages`
- `suggest_usage`
- `trace_http_call`

## Configure as MCP for Kiro

Kiro loads MCP servers from workspace or user settings. Workspace config file:

- `.kiro/settings/mcp.json`

Example:

```json
{
  "mcpServers": {
    "csharpdllgraph": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:\\GIT\\CSharpDllGraph\\src\\CSharpDllGraph.Mcp",
        "--"
      ],
      "env": {}
    }
  }
}
```

Before using it in Kiro, build or update at least one workspace with the CLI.

## Configure as MCP for Codex

Add the server with Codex CLI:

```bash
codex mcp add csharpdllgraph -- dotnet run --project D:\GIT\CSharpDllGraph\src\CSharpDllGraph.Mcp --
```

Check registration:

```bash
codex mcp list
```

Equivalent `~/.codex/config.toml` entry:

```toml
[mcp_servers.csharpdllgraph]
command = "dotnet"
args = ["run", "--project", "D:\\GIT\\CSharpDllGraph\\src\\CSharpDllGraph.Mcp", "--"]
```

## Configure as MCP for Claude Code

Add the server with Claude Code CLI:

```bash
claude mcp add --scope project csharpdllgraph -- dotnet run --project D:\GIT\CSharpDllGraph\src\CSharpDllGraph.Mcp --
```

Claude Code can also load project MCP config from:

- `.mcp.json`

Example:

```json
{
  "mcpServers": {
    "csharpdllgraph": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:\\GIT\\CSharpDllGraph\\src\\CSharpDllGraph.Mcp",
        "--"
      ],
      "env": {}
    }
  }
}
```

## Notes

- Use absolute paths in MCP client config.
- `dotnet run` is easiest for development.
- For faster startup in repeated use, publish the MCP project and point clients to the published executable.
- The server uses stdio transport only.

## Publish optional standalone binaries

CLI:

```bash
dotnet publish src/CSharpDllGraph.Cli -c Release -o .artifacts/cli
```

MCP:

```bash
dotnet publish src/CSharpDllGraph.Mcp -c Release -o .artifacts/mcp
```

Then point MCP clients to the published executable instead of `dotnet run`.
