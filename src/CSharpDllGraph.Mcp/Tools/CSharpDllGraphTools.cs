using System.ComponentModel;
using CSharpDllGraph.Engine.Query;
using CSharpDllGraph.Engine.Statistics;
using ModelContextProtocol.Server;

namespace CSharpDllGraph.Mcp.Tools;

[McpServerToolType]
internal sealed class CSharpDllGraphTools(
    IGraphQueryService graphQueryService,
    ToolCallStatisticsService statistics)
{
    [McpServerTool(Name = "ping"), Description("Returns pong to verify the server is running.")]
    public string Ping()
    {
        statistics.RecordCall("ping");
        return "pong";
    }

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
        statistics.RecordCall("describe_package_api");

        return graphQueryService.DescribePackageApiAsync(
            new DescribePackageApiRequest(package, version, tfm, filter),
            cancellationToken);
    }

    /// <summary>
    /// Returns resolved package versions for the configured workspace.
    /// </summary>
    /// <param name="project">Optional project name or relative project path filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured dependency list grouped by project.</returns>
    [McpServerTool(Name = "list_dependencies"), Description("Returns resolved package versions for the workspace configured at server startup, grouped by project.")]
    public Task<ListDependenciesResult> ListDependencies(
        [Description("Optional project name or relative project path filter.")] string? project = null,
        CancellationToken cancellationToken = default)
    {
        statistics.RecordCall("list_dependencies");

        return graphQueryService.ListDependenciesAsync(
            new ListDependenciesRequest(string.Empty, project),
            cancellationToken);
    }

    /// <summary>
    /// Returns packages resolved to different versions across projects in the configured workspace.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured version conflict result.</returns>
    [McpServerTool(Name = "find_version_conflicts"), Description("Returns packages resolved to different versions across projects in the workspace configured at server startup.")]
    public Task<FindVersionConflictsResult> FindVersionConflicts(
        CancellationToken cancellationToken = default)
    {
        statistics.RecordCall("find_version_conflicts");

        return graphQueryService.FindVersionConflictsAsync(
            new FindVersionConflictsRequest(string.Empty),
            cancellationToken);
    }

    /// <summary>
    /// Returns user-code usages of a symbol.
    /// </summary>
    /// <param name="symbolId">Exact graph node id. Use this or provide kind and fullName.</param>
    /// <param name="kind">Node kind when resolving by structured name.</param>
    /// <param name="fullName">Fully qualified symbol name when resolving by structured name.</param>
    /// <param name="version">Optional version token when resolving by structured name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured usage result for the configured workspace.</returns>
    [McpServerTool(Name = "find_usages"), Description("Returns user-code usages of a symbol from the workspace configured at server startup, sorted by file and source position.")]
    public Task<FindUsagesResult> FindUsages(
        [Description("Exact graph node id. Use this or provide kind + fullName.")] string? symbolId = null,
        [Description("Node kind when resolving by structured name, for example Type or Method.")] string? kind = null,
        [Description("Fully qualified symbol name when resolving by structured name.")] string? fullName = null,
        [Description("Optional version token when resolving by structured name.")] string? version = null,
        CancellationToken cancellationToken = default)
    {
        statistics.RecordCall("find_usages");

        return graphQueryService.FindUsagesAsync(
            new FindUsagesRequest(symbolId, kind, fullName, version, null),
            cancellationToken);
    }

    /// <summary>
    /// Returns ranked canonical call sites for a symbol.
    /// </summary>
    /// <param name="symbolId">Exact graph node id. Use this or provide kind and fullName.</param>
    /// <param name="kind">Node kind when resolving by structured name.</param>
    /// <param name="fullName">Fully qualified symbol name when resolving by structured name.</param>
    /// <param name="version">Optional version token when resolving by structured name.</param>
    /// <param name="maxResults">Maximum number of ranked suggestions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured canonical usage result.</returns>
    [McpServerTool(Name = "suggest_usage"), Description("Returns ranked canonical call sites for a symbol from the workspace configured at server startup, using observed Calls edges and short source excerpts from user code.")]
    public Task<SuggestUsageResult> SuggestUsage(
        [Description("Exact graph node id. Use this or provide kind + fullName.")] string? symbolId = null,
        [Description("Node kind when resolving by structured name, for example Method.")] string? kind = null,
        [Description("Fully qualified symbol name when resolving by structured name.")] string? fullName = null,
        [Description("Optional version token when resolving by structured name.")] string? version = null,
        [Description("Maximum number of ranked suggestions to return.")] int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        statistics.RecordCall("suggest_usage");

        return graphQueryService.SuggestUsageAsync(
            new SuggestUsageRequest(symbolId, kind, fullName, version, null, maxResults),
            cancellationToken);
    }

    /// <summary>
    /// Returns HTTP producers and consumer call sites for one normalized method and path.
    /// </summary>
    /// <param name="method">HTTP method, for example GET or POST.</param>
    /// <param name="path">Route or URL template to normalize and match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured HTTP trace result.</returns>
    [McpServerTool(Name = "trace_http_call"), Description("Returns HTTP producers and consumer call sites for one normalized method and path in the workspace configured at server startup.")]
    public Task<TraceHttpCallResult> TraceHttpCall(
        [Description("HTTP method, for example GET or POST.")] string method,
        [Description("Route or URL template to normalize and match.")] string path,
        CancellationToken cancellationToken = default)
    {
        statistics.RecordCall("trace_http_call");

        return graphQueryService.TraceHttpCallAsync(
            new TraceHttpCallRequest(method, path, null),
            cancellationToken);
    }
}
