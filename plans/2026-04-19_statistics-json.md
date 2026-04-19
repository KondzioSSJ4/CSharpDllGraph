# Plan: Tool Call Statistics JSON

> Add thread-safe `statistics.json` to `.csharpdllgraph/` tracking per-tool call counts, first/last call timestamps, accumulated across sessions.

```plan-meta
{
  "version": 1,
  "provider": "claude-code",
  "model": "claude-sonnet-4-6",
  "maxParallel": 1,
  "validation": [
    "dotnet build d:/GIT/CSharpDllGraph/CSharpDllGraph.slnx"
  ]
}
```

## Task T1: Add ToolCallStatisticsService to Engine

```task
{
  "id": "T1",
  "title": "Add ToolCallStatisticsService to Engine",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [],
  "paths": [
    "src/CSharpDllGraph.Engine/Statistics/"
  ],
  "goal": "Create a thread-safe Channel-based service that records tool call events and flushes to statistics.json after each event.",
  "acceptance": [
    "File src/CSharpDllGraph.Engine/Statistics/ToolCallStatisticsService.cs exists",
    "File src/CSharpDllGraph.Engine/Statistics/ToolCallEvent.cs exists",
    "File src/CSharpDllGraph.Engine/Statistics/ToolStatisticsData.cs exists (model for JSON serialization)",
    "Service uses System.Threading.Channels.Channel<ToolCallEvent> with SingleReader=true, SingleWriter=false",
    "Constructor accepts string statisticsFilePath, loads existing statistics.json on startup (merge/accumulate)",
    "RecordCall(string toolName) writes to channel without blocking",
    "Background consumer loop updates in-memory dictionary and writes statistics.json after each event",
    "statistics.json shape: { total_calls: int, tools: { [name]: { calls: int, first_called_utc: string, last_called_utc: string } } }",
    "DisposeAsync() completes the channel writer and awaits the consumer task for graceful shutdown",
    "If statistics.json does not exist on startup, starts with empty state"
  ],
  "steps": [
    "Create directory src/CSharpDllGraph.Engine/Statistics/",
    "Create record ToolCallEvent(string ToolName, DateTimeOffset CalledAt) in ToolCallEvent.cs",
    "Create record ToolEntryData(long Calls, DateTimeOffset? FirstCalledUtc, DateTimeOffset? LastCalledUtc) in ToolStatisticsData.cs, plus StatisticsData root class with long TotalCalls and Dictionary<string, ToolEntryData> Tools",
    "Create class ToolCallStatisticsService : IAsyncDisposable in ToolCallStatisticsService.cs",
    "Constructor: accept string statisticsFilePath; load existing JSON into _data dict or start empty; create Channel<ToolCallEvent>(SingleReader=true, SingleWriter=false); start background consumer Task",
    "Implement void RecordCall(string toolName) — write ToolCallEvent to channel via TryWrite (fire-and-forget, never blocks)",
    "Consumer loop (private async Task ConsumeAsync): read events from channel, update in-memory _data, call FlushAsync() after each event",
    "FlushAsync(): serialize _data to JSON (WriteIndented=true, camelCase) and write atomically (write to .tmp then File.Move with overwrite)",
    "DisposeAsync(): complete channel writer, await consumer task with timeout, suppress OperationCanceledException",
    "Use System.Text.Json with JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }"
  ]
}
```

## Task T2: Wire ToolCallStatisticsService into MCP server

```task
{
  "id": "T2",
  "title": "Wire ToolCallStatisticsService into MCP server",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T1"
  ],
  "paths": [
    "src/CSharpDllGraph.Mcp/Program.cs",
    "src/CSharpDllGraph.Mcp/Tools/CSharpDllGraphTools.cs"
  ],
  "goal": "Register ToolCallStatisticsService as a singleton in MCP DI and inject it into CSharpDllGraphTools so every tool method records its call.",
  "acceptance": [
    "ToolCallStatisticsService registered as singleton in MCP Program.cs DI",
    "Statistics file path resolved from workspaceConfig: Path.Combine(workspaceConfig.WorkspaceRootPath, \".csharpdllgraph\", \"statistics.json\")",
    "CSharpDllGraphTools constructor accepts IToolCallStatistics (or ToolCallStatisticsService) parameter",
    "Every tool method (ping, describe_package_api, list_dependencies, find_version_conflicts, find_usages, suggest_usage, trace_http_call) calls _statistics.RecordCall(\"<tool_name>\") before delegating",
    "ToolCallStatisticsService.DisposeAsync called on host shutdown (IHostedService or IAsyncDisposable registration)",
    "statistics.json written to {workspace-root}/.csharpdllgraph/statistics.json"
  ],
  "steps": [
    "In Program.cs, resolve statisticsFilePath = Path.Combine(workspaceConfig.WorkspaceRootPath, \".csharpdllgraph\", \"statistics.json\")",
    "Register: builder.Services.AddSingleton(_ => new ToolCallStatisticsService(statisticsFilePath))",
    "Register IAsyncDisposable cleanup on host stop, OR add ApplicationStopping lifetime hook that calls DisposeAsync()",
    "In CSharpDllGraphTools.cs, add ToolCallStatisticsService parameter to primary constructor",
    "Add _statistics.RecordCall(\"ping\") to Ping() — note: Ping is static so either make it non-static or record separately via a wrapper",
    "Add _statistics.RecordCall(\"describe_package_api\") as first line of DescribePackageApi()",
    "Add _statistics.RecordCall(\"list_dependencies\") as first line of ListDependencies()",
    "Add _statistics.RecordCall(\"find_version_conflicts\") as first line of FindVersionConflicts()",
    "Add _statistics.RecordCall(\"find_usages\") as first line of FindUsages()",
    "Add _statistics.RecordCall(\"suggest_usage\") as first line of SuggestUsage()",
    "Add _statistics.RecordCall(\"trace_http_call\") as first line of TraceHttpCall()",
    "For Ping: remove static modifier so it can access _statistics, or record call in a non-static wrapper"
  ]
}
```

## Task T3: Wire ToolCallStatisticsService into CLI query command

```task
{
  "id": "T3",
  "title": "Wire ToolCallStatisticsService into CLI query command",
  "status": "[x]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T1"
  ],
  "paths": [
    "src/CSharpDllGraph.Cli/Program.cs"
  ],
  "goal": "Record tool call statistics in RunQueryAsync using the workspace path from --workspace-path or registry, writing to that workspace's .csharpdllgraph/statistics.json.",
  "acceptance": [
    "RunQueryAsync resolves workspace root path from --workspace-path arg or from registry lookup by workspace name",
    "ToolCallStatisticsService instantiated with {workspaceRoot}/.csharpdllgraph/statistics.json",
    "RecordCall called with the tool name (args[0]) before executing the query",
    "DisposeAsync awaited after the query result is written (using await using or explicit try/finally)",
    "statistics.json created/updated in the correct workspace .csharpdllgraph/ directory"
  ],
  "steps": [
    "In RunQueryAsync, parse --workspace-path from args using ArgumentParser; if absent, try to infer from registry (e.g. first registered workspace or --workspace arg)",
    "Resolve workspaceRoot; if cannot determine, skip statistics (log nothing, fail silently)",
    "Instantiate: await using var stats = new ToolCallStatisticsService(Path.Combine(workspaceRoot, \".csharpdllgraph\", \"statistics.json\"))",
    "Call stats.RecordCall(args[0]) immediately after instantiation, before the switch expression",
    "The await using ensures DisposeAsync (and final flush) is called after WriteJson(result)"
  ]
}
```

## Task T4: Verify Engine project reference in CLI csproj

```task
{
  "id": "T4",
  "title": "Verify Engine project reference in CLI csproj",
  "status": "[~]",
  "agent": "backend-csharp",
  "dependsOn": [
    "T1"
  ],
  "paths": [
    "src/CSharpDllGraph.Cli/CSharpDllGraph.Cli.csproj",
    "src/CSharpDllGraph.Mcp/CSharpDllGraph.Mcp.csproj"
  ],
  "goal": "Ensure both CLI and MCP projects have a project reference to CSharpDllGraph.Engine (where ToolCallStatisticsService lives).",
  "acceptance": [
    "CSharpDllGraph.Cli.csproj already references CSharpDllGraph.Engine — verify, no change needed",
    "CSharpDllGraph.Mcp.csproj already references CSharpDllGraph.Engine — verify, no change needed",
    "dotnet build CSharpDllGraph.slnx completes with no errors"
  ],
  "steps": [
    "Open src/CSharpDllGraph.Cli/CSharpDllGraph.Cli.csproj and confirm ProjectReference to CSharpDllGraph.Engine exists",
    "Open src/CSharpDllGraph.Mcp/CSharpDllGraph.Mcp.csproj and confirm ProjectReference to CSharpDllGraph.Engine exists",
    "Add missing references if absent",
    "Run dotnet build CSharpDllGraph.slnx to verify compilation"
  ]
}
```
