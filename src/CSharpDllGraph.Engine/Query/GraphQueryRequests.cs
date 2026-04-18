namespace CSharpDllGraph.Engine.Query;

public sealed record DescribePackageApiRequest(
    string Package,
    string Version,
    string? TargetFramework = null,
    string? Filter = null);

public sealed record ListDependenciesRequest(
    string Workspace,
    string? Project = null);

public sealed record FindVersionConflictsRequest(
    string Workspace);

public sealed record FindUsagesRequest(
    string? SymbolId = null,
    string? Kind = null,
    string? FullName = null,
    string? Version = null,
    IReadOnlyList<string>? Workspaces = null);

public sealed record SuggestUsageRequest(
    string? SymbolId = null,
    string? Kind = null,
    string? FullName = null,
    string? Version = null,
    IReadOnlyList<string>? Workspaces = null,
    int MaxResults = 5);

public sealed record TraceHttpCallRequest(
    string Method,
    string Path,
    IReadOnlyList<string>? Workspaces = null);
