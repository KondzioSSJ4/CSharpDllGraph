namespace CSharpDllGraph.Engine.Query;

public interface IGraphQueryService
{
    /// <summary>
    /// Returns the public API surface of one package version.
    /// </summary>
    /// <param name="package">NuGet package name.</param>
    /// <param name="version">Resolved package version.</param>
    /// <param name="tfm">Optional target framework filter.</param>
    /// <param name="filter">Optional regex filter for type and method names.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured package API result.</returns>
    Task<DescribePackageApiResult> DescribePackageApiAsync(
        string package,
        string version,
        string? tfm = null,
        string? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns resolved package dependencies for one workspace.
    /// </summary>
    /// <param name="workspace">Registered workspace name.</param>
    /// <param name="project">Optional project filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured dependency result.</returns>
    Task<ListDependenciesResult> ListDependenciesAsync(
        string workspace,
        string? project = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns package version conflicts for one workspace.
    /// </summary>
    /// <param name="workspace">Registered workspace name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured conflict result.</returns>
    Task<FindVersionConflictsResult> FindVersionConflictsAsync(
        string workspace,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns user-code usages for one resolved symbol.
    /// </summary>
    /// <param name="symbolId">Optional exact symbol node id.</param>
    /// <param name="kind">Optional symbol kind when resolving by structured name.</param>
    /// <param name="fullName">Optional fully qualified symbol name.</param>
    /// <param name="version">Optional version token.</param>
    /// <param name="workspaces">Optional workspace scope filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured usage result.</returns>
    Task<FindUsagesResult> FindUsagesAsync(
        string? symbolId = null,
        string? kind = null,
        string? fullName = null,
        string? version = null,
        IReadOnlyList<string>? workspaces = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns ranked canonical usage examples for one resolved symbol.
    /// </summary>
    /// <param name="symbolId">Optional exact symbol node id.</param>
    /// <param name="kind">Optional symbol kind when resolving by structured name.</param>
    /// <param name="fullName">Optional fully qualified symbol name.</param>
    /// <param name="version">Optional version token.</param>
    /// <param name="workspaces">Optional workspace scope filter.</param>
    /// <param name="maxResults">Maximum number of suggestions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured suggestion result.</returns>
    Task<SuggestUsageResult> SuggestUsageAsync(
        string? symbolId = null,
        string? kind = null,
        string? fullName = null,
        string? version = null,
        IReadOnlyList<string>? workspaces = null,
        int maxResults = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns HTTP producers and consumers for one normalized route.
    /// </summary>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Route or URL template.</param>
    /// <param name="workspaces">Optional workspace scope filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured HTTP trace result.</returns>
    Task<TraceHttpCallResult> TraceHttpCallAsync(
        string method,
        string path,
        IReadOnlyList<string>? workspaces = null,
        CancellationToken cancellationToken = default);
}
