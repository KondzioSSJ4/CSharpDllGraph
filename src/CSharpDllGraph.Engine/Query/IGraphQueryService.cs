namespace CSharpDllGraph.Engine.Query;

public interface IGraphQueryService
{
    Task<DescribePackageApiResult> DescribePackageApiAsync(
        DescribePackageApiRequest request,
        CancellationToken cancellationToken = default);

    Task<ListDependenciesResult> ListDependenciesAsync(
        ListDependenciesRequest request,
        CancellationToken cancellationToken = default);

    Task<FindVersionConflictsResult> FindVersionConflictsAsync(
        FindVersionConflictsRequest request,
        CancellationToken cancellationToken = default);

    Task<FindUsagesResult> FindUsagesAsync(
        FindUsagesRequest request,
        CancellationToken cancellationToken = default);

    Task<SuggestUsageResult> SuggestUsageAsync(
        SuggestUsageRequest request,
        CancellationToken cancellationToken = default);

    Task<TraceHttpCallResult> TraceHttpCallAsync(
        TraceHttpCallRequest request,
        CancellationToken cancellationToken = default);
}
