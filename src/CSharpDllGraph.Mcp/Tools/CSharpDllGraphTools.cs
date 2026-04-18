using System.ComponentModel;
using CSharpDllGraph.Engine.Query;
using ModelContextProtocol.Server;

namespace CSharpDllGraph.Mcp.Tools;

[McpServerToolType]
internal sealed class CSharpDllGraphTools(IGraphQueryService graphQueryService)
{
    [McpServerTool(Name = "ping"), Description("Returns pong to verify the server is running.")]
    public static string Ping() => "pong";

    /// <summary>
    /// Returns the public surface of a NuGet package version.
    /// </summary>
    /// <param name="package">NuGet package name.</param>
    /// <param name="version">Resolved package version.</param>
    /// <param name="tfm">Optional target framework filter, for example net10.0.</param>
    /// <param name="filter">Optional regex filter applied to type and method names.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured package API grouped by namespace.</returns>
    [McpServerTool(Name = "describe_package_api"), Description("Returns the public surface of a NuGet package version, grouped by namespace.")]
    public Task<DescribePackageApiResult> DescribePackageApi(
        [Description("NuGet package name.")] string package,
        [Description("Resolved package version.")] string version,
        [Description("Optional target framework filter, for example net10.0.")] string? tfm = null,
        [Description("Optional regex filter applied to type and method names.")] string? filter = null,
        CancellationToken cancellationToken = default)
    {
        return graphQueryService.DescribePackageApiAsync(package, version, tfm, filter, cancellationToken);
    }

    /// <summary>
    /// Returns resolved package versions for one workspace.
    /// </summary>
    /// <param name="workspace">Registered workspace name.</param>
    /// <param name="project">Optional project name or relative project path filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured dependency list grouped by project.</returns>
    [McpServerTool(Name = "list_dependencies"), Description("Returns resolved package versions for one workspace, grouped by project.")]
    public Task<ListDependenciesResult> ListDependencies(
        [Description("Registered workspace name.")] string workspace,
        [Description("Optional project name or relative project path filter.")] string? project = null,
        CancellationToken cancellationToken = default)
    {
        return graphQueryService.ListDependenciesAsync(workspace, project, cancellationToken);
    }

    /// <summary>
    /// Returns packages resolved to different versions across projects in one workspace.
    /// </summary>
    /// <param name="workspace">Registered workspace name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured version conflict result.</returns>
    [McpServerTool(Name = "find_version_conflicts"), Description("Returns packages resolved to different versions across projects in one workspace.")]
    public Task<FindVersionConflictsResult> FindVersionConflicts(
        [Description("Registered workspace name.")] string workspace,
        CancellationToken cancellationToken = default)
    {
        return graphQueryService.FindVersionConflictsAsync(workspace, cancellationToken);
    }

    /// <summary>
    /// Returns user-code usages of a symbol.
    /// </summary>
    /// <param name="symbolId">Exact graph node id. Use this or provide kind and fullName.</param>
    /// <param name="kind">Node kind when resolving by structured name.</param>
    /// <param name="fullName">Fully qualified symbol name when resolving by structured name.</param>
    /// <param name="version">Optional version token when resolving by structured name.</param>
    /// <param name="workspaces">Optional workspace filter. Omit or pass all to search every registered workspace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured usage result grouped by workspace.</returns>
    [McpServerTool(Name = "find_usages"), Description("Returns user-code usages of a symbol, grouped by workspace and sorted by file and source position.")]
    public Task<FindUsagesResult> FindUsages(
        [Description("Exact graph node id. Use this or provide kind + fullName.")] string? symbolId = null,
        [Description("Node kind when resolving by structured name, for example Type or Method.")] string? kind = null,
        [Description("Fully qualified symbol name when resolving by structured name.")] string? fullName = null,
        [Description("Optional version token when resolving by structured name.")] string? version = null,
        [Description("Optional workspace filter. Omit or pass all to search every registered workspace.")] string[]? workspaces = null,
        CancellationToken cancellationToken = default)
    {
        return graphQueryService.FindUsagesAsync(symbolId, kind, fullName, version, workspaces, cancellationToken);
    }

    /// <summary>
    /// Returns ranked canonical call sites for a symbol.
    /// </summary>
    /// <param name="symbolId">Exact graph node id. Use this or provide kind and fullName.</param>
    /// <param name="kind">Node kind when resolving by structured name.</param>
    /// <param name="fullName">Fully qualified symbol name when resolving by structured name.</param>
    /// <param name="version">Optional version token when resolving by structured name.</param>
    /// <param name="workspaces">Optional workspace filter. Omit or pass all to search every registered workspace.</param>
    /// <param name="maxResults">Maximum number of ranked suggestions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured canonical usage result.</returns>
    [McpServerTool(Name = "suggest_usage"), Description("Returns ranked canonical call sites for a symbol, using observed Calls edges and short source excerpts from user code.")]
    public Task<SuggestUsageResult> SuggestUsage(
        [Description("Exact graph node id. Use this or provide kind + fullName.")] string? symbolId = null,
        [Description("Node kind when resolving by structured name, for example Method.")] string? kind = null,
        [Description("Fully qualified symbol name when resolving by structured name.")] string? fullName = null,
        [Description("Optional version token when resolving by structured name.")] string? version = null,
        [Description("Optional workspace filter. Omit or pass all to search every registered workspace.")] string[]? workspaces = null,
        [Description("Maximum number of ranked suggestions to return.")] int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        return graphQueryService.SuggestUsageAsync(symbolId, kind, fullName, version, workspaces, maxResults, cancellationToken);
    }

    /// <summary>
    /// Returns HTTP producers and consumer call sites for one normalized method and path.
    /// </summary>
    /// <param name="method">HTTP method, for example GET or POST.</param>
    /// <param name="path">Route or URL template to normalize and match.</param>
    /// <param name="workspaces">Optional workspace filter. Omit or pass all to search every registered workspace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured HTTP trace result.</returns>
    [McpServerTool(Name = "trace_http_call"), Description("Returns HTTP producers and consumer call sites for one normalized method and path across registered workspaces.")]
    public Task<TraceHttpCallResult> TraceHttpCall(
        [Description("HTTP method, for example GET or POST.")] string method,
        [Description("Route or URL template to normalize and match.")] string path,
        [Description("Optional workspace filter. Omit or pass all to search every registered workspace.")] string[]? workspaces = null,
        CancellationToken cancellationToken = default)
    {
        return graphQueryService.TraceHttpCallAsync(method, path, workspaces, cancellationToken);
    }
}
