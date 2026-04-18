using System.Text.Json;
using System.Text.RegularExpressions;
using CSharpDllGraph.Engine.Config;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Http;

namespace CSharpDllGraph.Engine.Query;

public sealed class SingleWorkspaceQueryService(
    IWorkspaceContext workspaceContext,
    ICrossWorkspaceHttpIndexBuilder crossWorkspaceHttpIndexBuilder) : IGraphQueryService
{
    public async Task<DescribePackageApiResult> DescribePackageApiAsync(
        DescribePackageApiRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateRequired(request.Package, nameof(request.Package));
        ValidateRequired(request.Version, nameof(request.Version));

        var normalizedPackage = request.Package.Trim();
        var normalizedVersion = request.Version.Trim();
        var normalizedTfm = NormalizeOptional(request.TargetFramework);
        var normalizedFilter = NormalizeOptional(request.Filter);
        var matcher = CreateFilterMatcher(normalizedFilter);
        var query = await workspaceContext.GetQueryAsync(cancellationToken);
        var packageId = new NodeId(NodeKind.Package, normalizedPackage, normalizedVersion);

        var namespaces = new SortedDictionary<string, NamespaceAccumulator>(StringComparer.Ordinal);
        var matchingWorkspaces = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (query.GetNode(packageId) is not null && PackageMatchesFramework(query, packageId, normalizedTfm))
        {
            matchingWorkspaces.Add(GetWorkspaceName());
            AccumulatePackageSurface(query, packageId, matcher, namespaces);
        }

        return new DescribePackageApiResult(
            normalizedPackage,
            normalizedVersion,
            normalizedTfm,
            normalizedFilter,
            matchingWorkspaces.ToArray(),
            namespaces.Values
                .OrderBy(static item => item.Namespace, StringComparer.Ordinal)
                .Select(static item => item.ToResult())
                .ToArray());
    }

    public async Task<ListDependenciesResult> ListDependenciesAsync(
        ListDependenciesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedProject = NormalizeOptional(request.Project);
        var query = await workspaceContext.GetQueryAsync(cancellationToken);

        var projectResults = query.FindNodes(NodeKind.Project, node =>
                normalizedProject is null || MatchesProjectFilter(node, normalizedProject))
            .OrderBy(static node => node.DisplayName, StringComparer.Ordinal)
            .ThenBy(static node => node.Id.FullyQualifiedName, StringComparer.Ordinal)
            .Select(projectNode => BuildProjectDependencies(query, projectNode))
            .ToArray();

        return new ListDependenciesResult(GetWorkspaceName(), normalizedProject, projectResults);
    }

    public async Task<FindVersionConflictsResult> FindVersionConflictsAsync(
        FindVersionConflictsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = await workspaceContext.GetQueryAsync(cancellationToken);
        var dependencyEntries = query.FindNodes(NodeKind.Project)
            .SelectMany(projectNode => query.GetEdges(fromId: projectNode.Id, kind: EdgeKind.DependsOn)
                .Select(edge => CreateDependencyEntry(query, projectNode, edge)))
            .OrderBy(static item => item.Package, StringComparer.Ordinal)
            .ThenBy(static item => item.Version, StringComparer.Ordinal)
            .ThenBy(static item => item.Project, StringComparer.Ordinal)
            .ThenBy(static item => item.TargetFramework ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        var conflicts = dependencyEntries
            .GroupBy(static item => item.Package, StringComparer.Ordinal)
            .Select(static group => new
            {
                Package = group.Key,
                Entries = group
                    .OrderBy(static item => item.Version, StringComparer.Ordinal)
                    .ThenBy(static item => item.Project, StringComparer.Ordinal)
                    .ThenBy(static item => item.TargetFramework ?? string.Empty, StringComparer.Ordinal)
                    .ToArray()
            })
            .Where(group => group.Entries
                .Select(static item => item.Version)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any())
            .OrderBy(static group => group.Package, StringComparer.Ordinal)
            .Select(static group => new VersionConflictResult(
                group.Package,
                group.Entries
                    .Select(static item => item.Version)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static item => item, StringComparer.Ordinal)
                    .ToArray(),
                group.Entries
                    .Select(static item => new ConflictProjectResult(
                        item.Project,
                        item.Version,
                        item.TargetFramework,
                        item.IsDirect))
                    .ToArray()))
            .ToArray();

        return new FindVersionConflictsResult(GetWorkspaceName(), conflicts);
    }

    public async Task<FindUsagesResult> FindUsagesAsync(
        FindUsagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedSymbolId = NormalizeOptional(request.SymbolId);
        var normalizedKind = NormalizeOptional(request.Kind);
        var normalizedFullName = NormalizeOptional(request.FullName);
        var normalizedVersion = NormalizeOptional(request.Version);
        ValidateUsageLookup(normalizedSymbolId, normalizedKind, normalizedFullName);

        var context = await LoadWorkspaceContextAsync(cancellationToken);
        var matchedSymbols = ResolveMatchedSymbols(
            [context],
            normalizedSymbolId,
            normalizedKind,
            normalizedFullName,
            normalizedVersion);

        var usageGroups = new[] { BuildUsageWorkspaceResult(context, matchedSymbols) }
            .Where(static result => result.Usages.Count > 0)
            .ToArray();

        return new FindUsagesResult(
            normalizedSymbolId,
            normalizedKind,
            normalizedFullName,
            normalizedVersion,
            matchedSymbols,
            usageGroups);
    }

    public async Task<SuggestUsageResult> SuggestUsageAsync(
        SuggestUsageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedSymbolId = NormalizeOptional(request.SymbolId);
        var normalizedKind = NormalizeOptional(request.Kind);
        var normalizedFullName = NormalizeOptional(request.FullName);
        var normalizedVersion = NormalizeOptional(request.Version);
        ValidateUsageLookup(normalizedSymbolId, normalizedKind, normalizedFullName);

        if (request.MaxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MaxResults), "Value must be greater than zero.");
        }

        var context = await LoadWorkspaceContextAsync(cancellationToken);
        var matchedSymbols = ResolveMatchedSymbols(
            [context],
            normalizedSymbolId,
            normalizedKind,
            normalizedFullName,
            normalizedVersion);

        var suggestions = BuildUsageSuggestionCandidates(context, matchedSymbols, cancellationToken)
            .OrderByDescending(static candidate => candidate.OccurrenceCount)
            .ThenByDescending(static candidate => candidate.RecencyUtc)
            .ThenByDescending(static candidate => candidate.DiversityCount)
            .ThenBy(static candidate => candidate.BrevityScore)
            .ThenBy(static candidate => candidate.Workspace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static candidate => candidate.Workspace, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.File ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.StartLine ?? int.MaxValue)
            .ThenBy(static candidate => candidate.StartColumn ?? int.MaxValue)
            .ThenBy(static candidate => candidate.CallSiteSymbolId, StringComparer.Ordinal)
            .Take(request.MaxResults)
            .Select(static candidate => candidate.ToResult())
            .ToArray();

        return new SuggestUsageResult(
            normalizedSymbolId,
            normalizedKind,
            normalizedFullName,
            normalizedVersion,
            request.MaxResults,
            matchedSymbols,
            suggestions);
    }

    public async Task<TraceHttpCallResult> TraceHttpCallAsync(
        TraceHttpCallRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateRequired(request.Method, nameof(request.Method));
        ValidateRequired(request.Path, nameof(request.Path));

        var index = await crossWorkspaceHttpIndexBuilder.BuildAsync(cancellationToken);
        var normalized = HttpRouteNormalizer.Normalize(request.Method, request.Path);
        var workspaceName = GetWorkspaceName();
        var matchingEntries = index.Find(normalized.Method, normalized.Path)
            .Where(entry => string.Equals(entry.WorkspaceName, workspaceName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static entry => entry.WorkspaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.WorkspaceName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Role)
            .ThenBy(static entry => entry.NodeId.ToString(), StringComparer.Ordinal)
            .ToArray();

        var context = await LoadWorkspaceContextAsync(cancellationToken);
        var producers = new List<HttpTraceProducerResult>();
        var consumers = new List<HttpTraceConsumerResult>();

        foreach (var entry in matchingEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var node = context.Query.GetNode(entry.NodeId);
            if (node is null)
            {
                continue;
            }

            switch (entry.Role)
            {
                case CrossWorkspaceHttpMatchRole.Producer:
                    producers.Add(BuildProducerResult(context, node));
                    break;
                case CrossWorkspaceHttpMatchRole.Consumer:
                    consumers.Add(BuildConsumerResult(context, node));
                    break;
            }
        }

        return new TraceHttpCallResult(
            normalized.Method,
            normalized.Path,
            producers
                .OrderBy(static item => item.Workspace, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Workspace, StringComparer.Ordinal)
                .ThenBy(static item => item.SourceFile ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static item => item.StartLine ?? int.MaxValue)
                .ThenBy(static item => item.StartColumn ?? int.MaxValue)
                .ThenBy(static item => item.EndpointId, StringComparer.Ordinal)
                .ToArray(),
            consumers
                .OrderBy(static item => item.Workspace, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Workspace, StringComparer.Ordinal)
                .ThenBy(static item => item.SourceFile ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static item => item.StartLine ?? int.MaxValue)
                .ThenBy(static item => item.StartColumn ?? int.MaxValue)
                .ThenBy(static item => item.CallSiteId, StringComparer.Ordinal)
                .ToArray());
    }

    private async Task<WorkspaceQueryContext> LoadWorkspaceContextAsync(CancellationToken cancellationToken)
    {
        return new WorkspaceQueryContext(
            workspaceContext.Config,
            GetWorkspaceName(),
            await workspaceContext.GetQueryAsync(cancellationToken));
    }

    private string GetWorkspaceName()
    {
        var rootPath = workspaceContext.Config.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(rootPath);
        return string.IsNullOrWhiteSpace(name) ? workspaceContext.Config.RootPath : name;
    }

    private static ProjectDependenciesResult BuildProjectDependencies(IGraphQuery query, Node projectNode)
    {
        return new ProjectDependenciesResult(
            projectNode.DisplayName,
            GetString(projectNode.Attributes, "projectPath"),
            GetStringArray(projectNode.Attributes, "targetFrameworks"),
            query.GetEdges(fromId: projectNode.Id, kind: EdgeKind.DependsOn)
                .Select(edge => CreateDependencyEntry(query, projectNode, edge))
                .OrderBy(static item => item.Package, StringComparer.Ordinal)
                .ThenBy(static item => item.Version, StringComparer.Ordinal)
                .ThenBy(static item => item.TargetFramework ?? string.Empty, StringComparer.Ordinal)
                .ToArray());
    }

    private static DependencyEntryResult CreateDependencyEntry(IGraphQuery query, Node projectNode, Edge edge)
    {
        var packageNode = query.GetNode(edge.ToId)
            ?? throw new InvalidOperationException($"Missing dependency target node '{edge.ToId}'.");

        return new DependencyEntryResult(
            packageNode.Id.FullyQualifiedName,
            packageNode.Id.VersionToken,
            GetBoolean(edge.Attributes, "isDirect"),
            GetString(edge.Attributes, "tfm"),
            packageNode.Id.ToString(),
            projectNode.DisplayName);
    }

    private static void AccumulatePackageSurface(
        IGraphQuery query,
        NodeId packageId,
        Regex? matcher,
        IDictionary<string, NamespaceAccumulator> namespaces)
    {
        foreach (var assemblyNode in query.Neighbors(packageId, EdgeDirection.Outgoing, EdgeKind.Contains)
                     .Where(static node => node.Kind == NodeKind.Assembly)
                     .OrderBy(static node => node.Id.FullyQualifiedName, StringComparer.Ordinal))
        {
            foreach (var namespaceNode in query.Neighbors(assemblyNode.Id, EdgeDirection.Outgoing, EdgeKind.Contains)
                         .Where(static node => node.Kind == NodeKind.Namespace)
                         .OrderBy(static node => node.Id.FullyQualifiedName, StringComparer.Ordinal))
            {
                if (!namespaces.TryGetValue(namespaceNode.Id.FullyQualifiedName, out var namespaceAccumulator))
                {
                    namespaceAccumulator = new NamespaceAccumulator(namespaceNode.Id.FullyQualifiedName);
                    namespaces[namespaceNode.Id.FullyQualifiedName] = namespaceAccumulator;
                }

                foreach (var typeNode in query.Neighbors(namespaceNode.Id, EdgeDirection.Outgoing, EdgeKind.Contains)
                             .Where(static node => node.Kind == NodeKind.Type)
                             .OrderBy(static node => node.Id.FullyQualifiedName, StringComparer.Ordinal))
                {
                    var methods = query.Neighbors(typeNode.Id, EdgeDirection.Outgoing, EdgeKind.Contains)
                        .Where(static node => node.Kind == NodeKind.Method)
                        .Select(static methodNode => new MethodApiResult(
                            methodNode.Id.FullyQualifiedName,
                            GetString(methodNode.Attributes, "signature")))
                        .Where(method => matcher is null || matcher.IsMatch(method.Name))
                        .OrderBy(static method => method.Name, StringComparer.Ordinal)
                        .ThenBy(static method => method.Signature, StringComparer.Ordinal)
                        .Distinct()
                        .ToArray();

                    var matchesType = matcher is null || matcher.IsMatch(typeNode.Id.FullyQualifiedName);
                    if (!matchesType && methods.Length == 0)
                    {
                        continue;
                    }

                    namespaceAccumulator.UpsertType(new TypeApiResult(
                        typeNode.Id.FullyQualifiedName,
                        GetString(typeNode.Attributes, "kind"),
                        methods));
                }
            }
        }
    }

    private static bool MatchesProjectFilter(Node projectNode, string project)
    {
        return string.Equals(projectNode.DisplayName, project, StringComparison.OrdinalIgnoreCase)
               || string.Equals(projectNode.Id.FullyQualifiedName, project, StringComparison.OrdinalIgnoreCase)
               || string.Equals(GetString(projectNode.Attributes, "projectPath"), project, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PackageMatchesFramework(IGraphQuery query, NodeId packageId, string? tfm)
    {
        if (tfm is null)
        {
            return true;
        }

        return query.GetEdges(toId: packageId, kind: EdgeKind.DependsOn)
            .Any(edge => string.Equals(GetString(edge.Attributes, "tfm"), tfm, StringComparison.Ordinal));
    }

    private static Regex? CreateFilterMatcher(string? filter)
    {
        return filter is null
            ? null
            : new Regex(filter, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ValidateRequired(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }
    }

    private static void ValidateUsageLookup(string? symbolId, string? kind, string? fullName)
    {
        if (symbolId is not null)
        {
            _ = NodeId.Parse(symbolId);
            return;
        }

        if (kind is null || fullName is null)
        {
            throw new ArgumentException("Provide symbolId or both kind and fullName.");
        }

        if (!Enum.TryParse<NodeKind>(kind, ignoreCase: true, out _))
        {
            throw new ArgumentException($"Unknown node kind '{kind}'.", nameof(kind));
        }
    }

    private static IReadOnlyList<MatchedSymbolResult> ResolveMatchedSymbols(
        IReadOnlyList<WorkspaceQueryContext> contexts,
        string? symbolId,
        string? kind,
        string? fullName,
        string? version)
    {
        if (symbolId is not null)
        {
            var parsed = NodeId.Parse(symbolId);
            return contexts
                .Where(context => context.Query.GetNode(parsed) is not null)
                .Select(context => context.Query.GetNode(parsed)!)
                .DistinctBy(static node => node.Id)
                .OrderBy(static node => node.Id.ToString(), StringComparer.Ordinal)
                .Select(static node => CreateMatchedSymbol(node))
                .ToArray();
        }

        var parsedKind = Enum.Parse<NodeKind>(kind!, ignoreCase: true);
        return contexts
            .SelectMany(context => context.Query.FindNodes(parsedKind, node =>
                string.Equals(node.Id.FullyQualifiedName, fullName, StringComparison.Ordinal)
                && (version is null || string.Equals(node.Id.VersionToken, version, StringComparison.Ordinal))))
            .DistinctBy(static node => node.Id)
            .OrderBy(static node => node.Id.ToString(), StringComparer.Ordinal)
            .Select(static node => CreateMatchedSymbol(node))
            .ToArray();
    }

    private static MatchedSymbolResult CreateMatchedSymbol(Node node)
    {
        return new MatchedSymbolResult(
            node.Id.ToString(),
            node.Kind.ToString(),
            node.Id.FullyQualifiedName,
            node.Id.VersionToken,
            node.DisplayName);
    }

    private static UsageWorkspaceResult BuildUsageWorkspaceResult(
        WorkspaceQueryContext context,
        IReadOnlyList<MatchedSymbolResult> matchedSymbols)
    {
        var usages = matchedSymbols
            .Select(symbol => NodeId.Parse(symbol.SymbolId))
            .SelectMany(symbolNodeId => context.Query.GetEdges(toId: symbolNodeId)
                .Where(static edge => edge.Kind is EdgeKind.Calls or EdgeKind.Uses or EdgeKind.Implements)
                .SelectMany(edge => BuildUsageItems(context, edge, symbolNodeId)))
            .OrderBy(static item => item.File ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static item => item.StartLine ?? int.MaxValue)
            .ThenBy(static item => item.StartColumn ?? int.MaxValue)
            .ThenBy(static item => item.EnclosingSymbolId, StringComparer.Ordinal)
            .ThenBy(static item => item.SymbolId, StringComparer.Ordinal)
            .ToArray();

        return new UsageWorkspaceResult(context.WorkspaceName, usages);
    }

    private static IReadOnlyList<SuggestUsageCandidate> BuildUsageSuggestionCandidates(
        WorkspaceQueryContext context,
        IReadOnlyList<MatchedSymbolResult> matchedSymbols,
        CancellationToken cancellationToken)
    {
        var rawCandidates = matchedSymbols
            .Select(symbol => NodeId.Parse(symbol.SymbolId))
            .SelectMany(symbolNodeId => context.Query.GetEdges(toId: symbolNodeId, kind: EdgeKind.Calls)
                .SelectMany(edge => BuildUsageSuggestionCandidates(context, symbolNodeId, edge)))
            .ToArray();

        return rawCandidates
            .GroupBy(
                static candidate => new SuggestUsageGroupKey(
                    candidate.Workspace,
                    candidate.CallSiteSymbolId,
                    candidate.File,
                    candidate.StartLine,
                    candidate.StartColumn,
                    candidate.EndLine,
                    candidate.EndColumn,
                    candidate.Excerpt),
                SuggestUsageGroupKey.Comparer)
            .Select(group =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var exemplar = group
                    .OrderByDescending(static item => item.RecencyUtc)
                    .ThenByDescending(static item => item.DiversityCount)
                    .ThenBy(static item => item.BrevityScore)
                    .ThenBy(static item => item.File ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(static item => item.StartLine ?? int.MaxValue)
                    .ThenBy(static item => item.StartColumn ?? int.MaxValue)
                    .ThenBy(static item => item.CallSiteSymbolId, StringComparer.Ordinal)
                    .First();

                return exemplar with
                {
                    OccurrenceCount = group.Sum(static item => item.OccurrenceCount),
                    DiversityCount = group.Max(static item => item.DiversityCount),
                    RecencyUtc = group.Max(static item => item.RecencyUtc),
                    RankReason = BuildRankReason(
                        group.Sum(static item => item.OccurrenceCount),
                        group.Max(static item => item.DiversityCount),
                        group.Max(static item => item.RecencyUtc),
                        exemplar.BrevityScore)
                };
            })
            .ToArray();
    }

    private static IEnumerable<SuggestUsageCandidate> BuildUsageSuggestionCandidates(
        WorkspaceQueryContext context,
        NodeId symbolNodeId,
        Edge edge)
    {
        var sourceNode = context.Query.GetNode(edge.FromId);
        if (sourceNode is null)
        {
            yield break;
        }

        var sourceRefs = edge.SourceRefs.Count > 0 ? edge.SourceRefs : sourceNode.SourceRefs;
        if (sourceRefs.Count == 0)
        {
            yield return CreateUsageSuggestionCandidate(context, symbolNodeId, sourceNode, null, null, sourceRefs);
            yield break;
        }

        foreach (var sourceRef in sourceRefs)
        {
            if (sourceRef.Spans.Count == 0)
            {
                yield return CreateUsageSuggestionCandidate(context, symbolNodeId, sourceNode, sourceRef, null, sourceRefs);
                continue;
            }

            foreach (var span in sourceRef.Spans)
            {
                yield return CreateUsageSuggestionCandidate(context, symbolNodeId, sourceNode, sourceRef, span, sourceRefs);
            }
        }
    }

    private static SuggestUsageCandidate CreateUsageSuggestionCandidate(
        WorkspaceQueryContext context,
        NodeId symbolNodeId,
        Node sourceNode,
        SourceRef? sourceRef,
        SourceSpan? span,
        IReadOnlyList<SourceRef> sourceRefs)
    {
        var sourcePath = ResolveSourceFilePath(context.Config.RootPath, sourceRef?.File);
        var normalizedFile = NormalizeSourceFile(context.Config.RootPath, sourceRef?.File);
        var excerpt = ReadSourceExcerpt(sourcePath, span);
        var recencyUtc = GetFileRecencyUtc(sourcePath);
        var diversityCount = GetDistinctSourceFileCount(sourceRefs);
        var brevityScore = excerpt?.Length
            ?? (span is null ? int.MaxValue - 1 : Math.Max(1, (span.Value.EndColumn - span.Value.StartColumn) + 1));

        return new SuggestUsageCandidate(
            context.WorkspaceName,
            symbolNodeId.ToString(),
            sourceNode.Id.ToString(),
            sourceNode.Kind.ToString(),
            sourceNode.Id.FullyQualifiedName,
            sourceNode.DisplayName,
            normalizedFile,
            span?.StartLine,
            span?.StartColumn,
            span?.EndLine,
            span?.EndColumn,
            BuildSourceLabel(normalizedFile, span),
            excerpt,
            1,
            diversityCount,
            recencyUtc,
            brevityScore,
            BuildRankReason(1, diversityCount, recencyUtc, brevityScore));
    }

    private static IEnumerable<UsageResult> BuildUsageItems(
        WorkspaceQueryContext context,
        Edge edge,
        NodeId symbolNodeId)
    {
        var sourceNode = context.Query.GetNode(edge.FromId);
        if (sourceNode is null)
        {
            yield break;
        }

        var sourceRefs = edge.SourceRefs.Count > 0 ? edge.SourceRefs : sourceNode.SourceRefs;
        if (sourceRefs.Count == 0)
        {
            yield return CreateUsageResult(context, sourceNode, symbolNodeId, edge.Kind, null, null);
            yield break;
        }

        foreach (var sourceRef in sourceRefs)
        {
            var spans = sourceRef.Spans.Count == 0 ? [default(SourceSpan?)] : sourceRef.Spans.Select(static span => (SourceSpan?)span);
            foreach (var span in spans)
            {
                yield return CreateUsageResult(context, sourceNode, symbolNodeId, edge.Kind, sourceRef, span);
            }
        }
    }

    private static UsageResult CreateUsageResult(
        WorkspaceQueryContext context,
        Node sourceNode,
        NodeId symbolNodeId,
        EdgeKind usageKind,
        SourceRef? sourceRef,
        SourceSpan? span)
    {
        var normalizedFile = NormalizeSourceFile(context.Config.RootPath, sourceRef?.File);
        return new UsageResult(
            SymbolId: symbolNodeId.ToString(),
            UsageKind: usageKind.ToString(),
            File: normalizedFile,
            StartLine: span?.StartLine,
            StartColumn: span?.StartColumn,
            EndLine: span?.EndLine,
            EndColumn: span?.EndColumn,
            SourceLabel: BuildSourceLabel(normalizedFile, span),
            EnclosingSymbolId: sourceNode.Id.ToString(),
            EnclosingSymbolKind: sourceNode.Kind.ToString(),
            EnclosingSymbol: sourceNode.Id.FullyQualifiedName,
            EnclosingDisplayName: sourceNode.DisplayName);
    }

    private static HttpTraceProducerResult BuildProducerResult(WorkspaceQueryContext context, Node node)
    {
        var firstSource = node.SourceRefs.Count > 0 ? node.SourceRefs[0] : null;
        var firstSpan = firstSource is not null && firstSource.Spans.Count > 0 ? (SourceSpan?)firstSource.Spans[0] : null;
        var handlerNode = context.Query.GetEdges(toId: node.Id, kind: EdgeKind.HandlesRoute)
            .Select(edge => context.Query.GetNode(edge.FromId))
            .FirstOrDefault(static candidate => candidate is not null);

        var normalizedFile = NormalizeSourceFile(context.Config.RootPath, firstSource?.File);
        return new HttpTraceProducerResult(
            context.WorkspaceName,
            node.Id.ToString(),
            node.DisplayName,
            GetString(node.Attributes, "httpMethod"),
            GetString(node.Attributes, "routeTemplate"),
            normalizedFile,
            firstSpan?.StartLine,
            firstSpan?.StartColumn,
            BuildSourceLabel(normalizedFile, firstSpan),
            handlerNode?.Id.ToString(),
            handlerNode?.Id.FullyQualifiedName,
            handlerNode?.DisplayName);
    }

    private static HttpTraceConsumerResult BuildConsumerResult(WorkspaceQueryContext context, Node node)
    {
        var firstSource = node.SourceRefs.Count > 0 ? node.SourceRefs[0] : null;
        var firstSpan = firstSource is not null && firstSource.Spans.Count > 0 ? (SourceSpan?)firstSource.Spans[0] : null;
        var callerNode = context.Query.GetEdges(toId: node.Id, kind: EdgeKind.CallsRoute)
            .Select(edge => context.Query.GetNode(edge.FromId))
            .FirstOrDefault(static candidate => candidate is not null);

        var normalizedFile = NormalizeSourceFile(context.Config.RootPath, firstSource?.File);
        return new HttpTraceConsumerResult(
            context.WorkspaceName,
            node.Id.ToString(),
            node.DisplayName,
            GetString(node.Attributes, "httpMethod"),
            GetString(node.Attributes, "urlTemplate") ?? GetString(node.Attributes, "routeTemplate"),
            normalizedFile,
            firstSpan?.StartLine,
            firstSpan?.StartColumn,
            BuildSourceLabel(normalizedFile, firstSpan),
            callerNode?.Id.ToString() ?? GetString(node.Attributes, "callingMethod"),
            callerNode?.Id.FullyQualifiedName ?? GetString(node.Attributes, "callingMethod"),
            callerNode?.DisplayName);
    }

    private static string? NormalizeSourceFile(string workspaceRootPath, string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        var normalized = file.Trim().Replace('\\', '/');
        if (!Path.IsPathRooted(file))
        {
            return normalized;
        }

        var rootPath = Path.GetFullPath(workspaceRootPath);
        var filePath = Path.GetFullPath(file);
        if (filePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
        }

        return normalized;
    }

    private static string? ResolveSourceFilePath(string workspaceRootPath, string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        if (Path.IsPathRooted(file))
        {
            return Path.GetFullPath(file);
        }

        return Path.GetFullPath(Path.Combine(workspaceRootPath, file));
    }

    private static string? ReadSourceExcerpt(string? filePath, SourceSpan? span)
    {
        if (filePath is null || span is null || !File.Exists(filePath))
        {
            return null;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(filePath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (lines.Length == 0)
        {
            return null;
        }

        var startLine = Math.Max(1, span.Value.StartLine - 1);
        var endLine = Math.Min(lines.Length, Math.Max(span.Value.EndLine, span.Value.StartLine + 1));
        var excerptLines = Enumerable.Range(startLine, endLine - startLine + 1)
            .Select(lineNumber => $"{lineNumber}: {lines[lineNumber - 1].Trim()}")
            .ToArray();

        if (excerptLines.Length == 0)
        {
            return null;
        }

        var excerpt = string.Join(Environment.NewLine, excerptLines);
        return excerpt.Length <= 280 ? excerpt : excerpt[..280];
    }

    private static long GetFileRecencyUtc(string? filePath)
    {
        if (filePath is null || !File.Exists(filePath))
        {
            return long.MinValue;
        }

        try
        {
            return File.GetLastWriteTimeUtc(filePath).Ticks;
        }
        catch (IOException)
        {
            return long.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return long.MinValue;
        }
    }

    private static int GetDistinctSourceFileCount(IReadOnlyList<SourceRef> sourceRefs)
    {
        return sourceRefs
            .Select(static sourceRef => sourceRef.File)
            .Where(static file => !string.IsNullOrWhiteSpace(file))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static string BuildRankReason(int occurrenceCount, int diversityCount, long recencyUtc, int brevityScore)
    {
        return $"frequency={occurrenceCount}; recencyTicks={recencyUtc}; diversity={diversityCount}; brevity={brevityScore}";
    }

    private static string? BuildSourceLabel(string? file, SourceSpan? span)
    {
        if (file is null)
        {
            return null;
        }

        return span is null
            ? file
            : $"{file}:{span.Value.StartLine}:{span.Value.StartColumn}";
    }

    private static bool GetBoolean(IReadOnlyDictionary<string, JsonElement> attributes, string key)
    {
        return attributes.TryGetValue(key, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : false;
    }

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> attributes, string key)
    {
        return attributes.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static IReadOnlyList<string> GetStringArray(IReadOnlyDictionary<string, JsonElement> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class NamespaceAccumulator(string @namespace)
    {
        private readonly SortedDictionary<string, TypeApiResult> _types = new(StringComparer.Ordinal);

        public string Namespace { get; } = @namespace;

        public void UpsertType(TypeApiResult type)
        {
            _types[type.Name] = type;
        }

        public NamespaceApiResult ToResult()
        {
            return new NamespaceApiResult(
                Namespace,
                _types.Values
                    .OrderBy(static item => item.Name, StringComparer.Ordinal)
                    .ToArray());
        }
    }

    private sealed record WorkspaceQueryContext(
        WorkspaceConfig Config,
        string WorkspaceName,
        IGraphQuery Query);
}
