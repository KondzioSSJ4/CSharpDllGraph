# Plan: NuGet Package Cache + DLL Load Bug Fix

> Fix crash when transitive DLL dependencies are missing during reflection, and add per-package cache to avoid re-processing unchanged NuGet packages on every build.

```plan-meta
{
  "version": 1,
  "provider": "claude-code",
  "model": "claude-sonnet-4-6",
  "maxParallel": 1,
  "validation": [
    "dotnet build /d/GIT/CSharpDllGraph/CSharpDllGraph.slnx"
  ]
}
```

## Task T1: Fix HasCompilerGeneratedMarker crash on missing transitive DLL

```task
{
  "id": "T1",
  "title": "Fix HasCompilerGeneratedMarker crash on missing transitive DLL",
  "status": "[ ]",
  "agent": "backend-csharp",
  "dependsOn": [],
  "paths": [
    "src/CSharpDllGraph.Providers.Dotnet/DotnetProvider.cs"
  ],
  "goal": "Prevent FileNotFoundException from crashing the entire DotnetProvider when a transitive assembly dependency is missing during reflection.",
  "acceptance": [
    "HasCompilerGeneratedMarker returns false instead of throwing when GetCustomAttributesData() fails",
    "EmitAssemblyTypes does not crash when iterating types with missing transitive deps",
    "dotnet build succeeds"
  ],
  "steps": [
    "Open src/CSharpDllGraph.Providers.Dotnet/DotnetProvider.cs",
    "Find HasCompilerGeneratedMarker (line ~597): wrap the body in try/catch(Exception), return false on any exception",
    "Find the LINQ Where() call in EmitAssemblyTypes (line ~446) that calls HasCompilerGeneratedMarker on each type — verify it is now safe because HasCompilerGeneratedMarker itself catches",
    "Find the loop over type.GetMethods() (line ~505) where HasCompilerGeneratedMarker is called on method — verify same try/catch covers it",
    "Run dotnet build to confirm no compile errors"
  ]
}
```

## Task T2: Add IPackageFragmentCache interface and file-based implementation

```task
{
  "id": "T2",
  "title": "Add IPackageFragmentCache interface and file-based implementation",
  "status": "[ ]",
  "agent": "backend-csharp",
  "dependsOn": ["T1"],
  "paths": [
    "src/CSharpDllGraph.Providers.Dotnet/",
    "src/CSharpDllGraph.Engine/Store/"
  ],
  "goal": "Create a cache abstraction and implementation that stores GraphFragment per NuGet package (keyed by name@version) in .csharpdllgraph/cache/packages/ inside the workspace.",
  "acceptance": [
    "IPackageFragmentCache interface exists with TryGetAsync and SetAsync methods",
    "FilePackageFragmentCache implementation stores files as {name}@{version}.json with special chars sanitized",
    "Cache directory is .csharpdllgraph/cache/packages/ relative to workspace root (passed via GraphBuildContext.WorkspaceRootPath)",
    "Uses same JsonSerializerOptions as GraphBuildPipeline (GraphJsonSerializerOptions.Create())",
    "dotnet build succeeds"
  ],
  "steps": [
    "Create src/CSharpDllGraph.Providers.Dotnet/Cache/IPackageFragmentCache.cs with interface: Task<GraphFragment?> TryGetAsync(string packageName, string version, CancellationToken ct); Task SetAsync(string packageName, string version, GraphFragment fragment, CancellationToken ct)",
    "Create src/CSharpDllGraph.Providers.Dotnet/Cache/FilePackageFragmentCache.cs implementing IPackageFragmentCache",
    "Constructor takes (string workspaceRootPath) — cache root is Path.Combine(workspaceRootPath, 'cache', 'packages')",
    "Sanitize cache key: replace chars not in [a-zA-Z0-9._-] with '_' to form safe filename {name}@{version}.json",
    "TryGetAsync: if file exists deserialize and return GraphFragment, else return null; catch FileNotFoundException silently",
    "SetAsync: serialize GraphFragment to file using atomic write (write .tmp then move), create directory if needed",
    "Use JsonSerializer with GraphJsonSerializerOptions.Create() — add using for CSharpDllGraph.Engine.Graph.Serialization",
    "Run dotnet build to confirm no compile errors"
  ]
}
```

## Task T3: Wire cache into DotnetProvider.EmitPackageStructure

```task
{
  "id": "T3",
  "title": "Wire cache into DotnetProvider.EmitPackageStructure",
  "status": "[ ]",
  "agent": "backend-csharp",
  "dependsOn": ["T2"],
  "paths": [
    "src/CSharpDllGraph.Providers.Dotnet/DotnetProvider.cs"
  ],
  "goal": "Make DotnetProvider use FilePackageFragmentCache so reflection is skipped for NuGet packages that haven't changed version.",
  "acceptance": [
    "EmitPackageStructure checks cache before calling EmitAssemblyTypes",
    "On cache hit: nodes and edges from cached fragment are added to collector, reflection skipped",
    "On cache miss: reflection runs as before, result is saved to cache",
    "Cache is instantiated using GraphBuildContext.WorkspaceRootPath in CollectAsync",
    "dotnet build succeeds"
  ],
  "steps": [
    "In DotnetProvider.CollectAsync (line ~15), instantiate FilePackageFragmentCache using context.WorkspaceRootPath and store as local variable",
    "Change EmitPackageStructure signature to async and add IPackageFragmentCache and CancellationToken parameters",
    "At start of EmitPackageStructure: call cache.TryGetAsync(package.Identity.Name, package.Identity.Version, ct)",
    "On cache hit: iterate cached fragment Nodes and Edges and add them to collector via collector.AddNode/AddEdge, then return",
    "On cache miss: run existing reflection logic, then collect all newly added nodes/edges into a GraphFragment and call cache.SetAsync",
    "To collect nodes/edges for cache: create a secondary GraphCollector for the package scope, pass to EmitAssemblyTypes, then merge into main collector and save secondary to cache",
    "Update the call site in CollectAsync (line ~79) to await EmitPackageStructure",
    "Run dotnet build to confirm no compile errors"
  ]
}
```

## Task T4: Add .gitignore for cache directory in workspace output

```task
{
  "id": "T4",
  "title": "Add .gitignore for cache directory",
  "status": "[ ]",
  "agent": "backend-csharp",
  "dependsOn": ["T2"],
  "paths": [
    "src/CSharpDllGraph.Engine/",
    "src/CSharpDllGraph.Cli/"
  ],
  "goal": "Ensure the cache/ directory inside .csharpdllgraph/ is automatically gitignored when a workspace is built.",
  "acceptance": [
    "A .gitignore file containing '/cache/' is written to the workspace's .csharpdllgraph/ directory on first build",
    "The .gitignore write is idempotent (does not overwrite if already exists with correct content)",
    "Existing gitignore for providers/ or the graph folder (if any) is preserved"
  ],
  "steps": [
    "Find where the .csharpdllgraph/ directory is created — likely in JsonWorkspaceStore.SaveAsync or GraphBuildPipeline.BuildAndPersistAsync",
    "After the directory is created, check if .csharpdllgraph/.gitignore exists",
    "If it does not exist, write a .gitignore with content: '/cache/\\n'",
    "If it exists but does not contain '/cache/', append the line",
    "Prefer placing this logic in JsonWorkspaceStore.SaveAsync where Directory.CreateDirectory is already called",
    "Run dotnet build to confirm no compile errors"
  ]
}
```
