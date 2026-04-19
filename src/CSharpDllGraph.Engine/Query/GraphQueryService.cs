using System.Text.Json;
using System.Text.RegularExpressions;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Http;
using CSharpDllGraph.Engine.Registry;
using CSharpDllGraph.Engine.Store;

namespace CSharpDllGraph.Engine.Query;

public sealed class GraphQueryService(
    IReadOnlyList<WorkspaceRegistration> registrations,
    ICrossWorkspaceHttpIndexBuilder crossWorkspaceHttpIndexBuilder) : IGraphQueryService
{
    public async Task<DescribePackageApiResult> DescribePackageApiAsync(
        DescribePackageApiRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var package = request.Package;
        var version = request.Version;
        var tfm = request.TargetFramework;
        var filter = request.Filter;

        ValidateRequired(package, nameof(package));
        ValidateRequired(version, nameof(version));

        var normalizedPackage = package.Trim();
        var normalizedVersion = version.Trim();
        var normalizedTfm = NormalizeOptional(tfm);
        var normalizedFilter = NormalizeOptional(filter);
        var matcher = CreateFilterMatcher(normalizedFilter);

        var namespaces = new SortedDictionary<string, NamespaceAccumulator>(StringComparer.Ordinal);
        var matchingWorkspaces = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations
                     .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static item => item.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var query = await LoadWorkspaceQueryAsync(registration, cancellationToken);
            var packageId = new NodeId(NodeKind.Package, normalizedPackage, normalizedVersion);
            if (query.GetNode(packageId) is null)
            {
                continue;
            }

            if (!PackageMatchesFramework(query, packageId, normalizedTfm))
            {
                continue;
            }

            matchingWorkspaces.Add(registration.Name);
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

        var workspace = request.Workspace;
        var project = request.Project;
        var registration = ResolveWorkspace(workspace);
        var normalizedProject = NormalizeOptional(project);
        var query = await LoadWorkspaceQueryAsync(registration, cancellationToken);

        var projectResults = query.FindNodes(NodeKind.Project, node =>
                normalizedProject is null || MatchesProjectFilter(node, normalizedProject))
            .OrderBy(static node => node.DisplayName, StringComparer.Ordinal)
            .ThenBy(static node => node.Id.FullyQualifiedName, StringComparer.Ordinal)
            .Select(projectNode => BuildProjectDependencies(query, projectNode))
            .ToArray();

        return new ListDependenciesResult(
            registration.Name,
            normalizedProject,
            projectResults);
    }

    public async Task<FindVersionConflictsResult> FindVersionConflictsAsync(
        FindVersionConflictsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workspace = request.Workspace;
        var registration = ResolveWorkspace(workspace);
        var query = await LoadWorkspaceQueryAsync(registration, cancellationToken);

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

        return new FindVersionConflictsResult(registration.Name, conflicts);
    }

    public async Task<FindUsagesResult> FindUsagesAsync(
        FindUsagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var symbolId = request.SymbolId;
        var kind = request.Kind;
        var fullName = request.FullName;
        var version = request.Version;
        var workspaces = request.Workspaces;
        var normalizedSymbolId = NormalizeOptional(symbolId);
        var normalizedKind = NormalizeOptional(kind);
        var normalizedFullName = NormalizeOptional(fullName);
        var normalizedVersion = NormalizeOptional(version);
        ValidateUsageLookup(normalizedSymbolId, normalizedKind, normalizedFullName);

        var workspaceRegistrations = ResolveWorkspaceScope(workspaces);
        var workspaceContexts = await LoadWorkspaceContextsAsync(workspaceRegistrations, cancellationToken);
        var matchedSymbols = ResolveMatchedSymbols(
            workspaceContexts,
            normalizedSymbolId,
            normalizedKind,
            normalizedFullName,
            normalizedVersion);

        var usageGroups = workspaceContexts
            .Select(context => BuildUsageWorkspaceResult(context, matchedSymbols))
            .Where(static result => result.Usages.Count > 0)
            .OrderBy(static result => result.Workspace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static result => result.Workspace, StringComparer.Ordinal)
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

        var symbolId = request.SymbolId;
        var kind = request.Kind;
        var fullName = request.FullName;
        var version = request.Version;
        var workspaces = request.Workspaces;
        var maxResults = request.MaxResults;
        var normalizedSymbolId = NormalizeOptional(symbolId);
        var normalizedKind = NormalizeOptional(kind);
        var normalizedFullName = NormalizeOptional(fullName);
        var normalizedVersion = NormalizeOptional(version);
        ValidateUsageLookup(normalizedSymbolId, normalizedKind, normalizedFullName);

        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "Value must be greater than zero.");
        }

        var workspaceRegistrations = ResolveWorkspaceScope(workspaces);
        var workspaceContexts = await LoadWorkspaceContextsAsync(workspaceRegistrations, cancellationToken);
        var matchedSymbols = ResolveMatchedSymbols(
            workspaceContexts,
            normalizedSymbolId,
            normalizedKind,
            normalizedFullName,
            normalizedVersion);

        var suggestions = workspaceContexts
            .SelectMany(context => BuildUsageSuggestionCandidates(context, matchedSymbols, cancellationToken))
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
            .Take(maxResults)
            .Select(static candidate => candidate.ToResult())
            .ToArray();

        return new SuggestUsageResult(
            normalizedSymbolId,
            normalizedKind,
            normalizedFullName,
            normalizedVersion,
            maxResults,
            matchedSymbols,
            suggestions);
    }

    private async Task<TraceHttpCallAsyncResult> TraceHttpCallInternalAsync(
        string method,
        string path,
        IReadOnlyList<string>? workspaces = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequired(method, nameof(method));
        ValidateRequired(path, nameof(path));

        var workspaceRegistrations = ResolveWorkspaceScope(workspaces);
        var workspaceNames = workspaceRegistrations
            .Select(static item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var index = await crossWorkspaceHttpIndexBuilder.BuildAsync(cancellationToken);
        var normalized = HttpRouteNormalizer.Normalize(method, path);
        var matchingEntries = index.Find(normalized.Method, normalized.Path)
            .Where(entry => workspaceNames.Contains(entry.WorkspaceName))
            .OrderBy(static entry => entry.WorkspaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.WorkspaceName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Role)
            .ThenBy(static entry => entry.NodeId.ToString(), StringComparer.Ordinal)
            .ToArray();

        var workspaceContexts = await LoadWorkspaceContextsAsync(
            workspaceRegistrations.Where(registration => matchingEntries.Any(entry => string.Equals(entry.WorkspaceName, registration.Name, StringComparison.OrdinalIgnoreCase))).ToArray(),
            cancellationToken);

        var contextsByWorkspace = workspaceContexts.ToDictionary(static item => item.Registration.Name, StringComparer.OrdinalIgnoreCase);
        var producers = new List<HttpTraceProducerResult>();
        var consumers = new List<HttpTraceConsumerResult>();

        foreach (var entry in matchingEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!contextsByWorkspace.TryGetValue(entry.WorkspaceName, out var context))
            {
                continue;
            }

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

        return new TraceHttpCallAsyncResult(
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

    public async Task<TraceHttpCallResult> TraceHttpCallAsync(
        TraceHttpCallRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var method = request.Method;
        var path = request.Path;
        var workspaces = request.Workspaces;
        var result = await TraceHttpCallInternalAsync(method, path, workspaces, cancellationToken);
        return new TraceHttpCallResult(result.Method, result.Path, result.Producers, result.Consumers);
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

    private static async Task<IGraphQuery> LoadWorkspaceQueryAsync(
        WorkspaceRegistration registration,
        CancellationToken cancellationToken)
    {
        var snapshot = await new JsonWorkspaceStore(registration.GraphPath).LoadAsync(cancellationToken);
        return new InMemoryGraphQuery(snapshot);
    }

    private static async Task<IReadOnlyList<WorkspaceQueryContext>> LoadWorkspaceContextsAsync(
        IReadOnlyList<WorkspaceRegistration> registrations,
        CancellationToken cancellationToken)
    {
        var contexts = new List<WorkspaceQueryContext>(registrations.Count);
        foreach (var registration in registrations
                     .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static item => item.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await new JsonWorkspaceStore(registration.GraphPath).LoadAsync(cancellationToken);
            contexts.Add(new WorkspaceQueryContext(registration, new InMemoryGraphQuery(snapshot)));
        }

        return contexts;
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

    private WorkspaceRegistration ResolveWorkspace(string name)
    {
        var registration = registrations.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
        if (registration is null)
        {
            throw new KeyNotFoundException($"Workspace '{name}' not found.");
        }

        return registration;
    }

    private IReadOnlyList<WorkspaceRegistration> ResolveWorkspaceScope(IReadOnlyList<string>? workspaceNames)
    {
        if (workspaceNames is null || workspaceNames.Count == 0 || workspaceNames.Any(static item => string.Equals(item, "all", StringComparison.OrdinalIgnoreCase)))
        {
            return registrations
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Name, StringComparer.Ordinal)
                .ToArray();
        }

        return workspaceNames
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(ResolveWorkspace)
            .DistinctBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Name, StringComparer.Ordinal)
            .ToArray();
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

        return new UsageWorkspaceResult(context.Registration.Name, usages);
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
        var sourcePath = ResolveSourceFilePath(context.Registration.RootPath, sourceRef?.File);
        var normalizedFile = NormalizeSourceFile(context.Registration.RootPath, sourceRef?.File);
        var excerpt = ReadSourceExcerpt(sourcePath, span);
        var recencyUtc = GetFileRecencyUtc(sourcePath);
        var diversityCount = GetDistinctSourceFileCount(sourceRefs);
        var brevityScore = excerpt?.Length
            ?? (span is null ? int.MaxValue - 1 : Math.Max(1, (span.Value.EndColumn - span.Value.StartColumn) + 1));

        return new SuggestUsageCandidate(
            context.Registration.Name,
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
        var normalizedFile = NormalizeSourceFile(context.Registration.RootPath, sourceRef?.File);
        return new UsageResult(
            symbolNodeId.ToString(),
            usageKind.ToString(),
            normalizedFile,
            span?.StartLine,
            span?.StartColumn,
            span?.EndLine,
            span?.EndColumn,
            BuildSourceLabel(normalizedFile, span),
            sourceNode.Id.ToString(),
            sourceNode.Kind.ToString(),
            sourceNode.Id.FullyQualifiedName,
            sourceNode.DisplayName);
    }

    private static HttpTraceProducerResult BuildProducerResult(WorkspaceQueryContext context, Node node)
    {
        var firstSource = node.SourceRefs.Count > 0 ? node.SourceRefs[0] : null;
        var firstSpan = firstSource is not null && firstSource.Spans.Count > 0 ? (SourceSpan?)firstSource.Spans[0] : null;
        var handlerNode = context.Query.GetEdges(toId: node.Id, kind: EdgeKind.HandlesRoute)
            .Select(edge => context.Query.GetNode(edge.FromId))
            .FirstOrDefault(static candidate => candidate is not null);

        var normalizedFile = NormalizeSourceFile(context.Registration.RootPath, firstSource?.File);
        return new HttpTraceProducerResult(
            context.Registration.Name,
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

        var normalizedFile = NormalizeSourceFile(context.Registration.RootPath, firstSource?.File);
        return new HttpTraceConsumerResult(
            context.Registration.Name,
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
        WorkspaceRegistration Registration,
        IGraphQuery Query);
}

public sealed record DescribePackageApiResult(
    string Package,
    string Version,
    string? TargetFramework,
    string? Filter,
    IReadOnlyList<string> Workspaces,
    IReadOnlyList<NamespaceApiResult> Namespaces);

public sealed record NamespaceApiResult(
    string Namespace,
    IReadOnlyList<TypeApiResult> Types);

public sealed record TypeApiResult(
    string Name,
    string? Kind,
    IReadOnlyList<MethodApiResult> Methods);

public sealed record MethodApiResult(
    string Name,
    string? Signature);

public sealed record ListDependenciesResult(
    string Workspace,
    string? ProjectFilter,
    IReadOnlyList<ProjectDependenciesResult> Projects);

public sealed record ProjectDependenciesResult(
    string Project,
    string? ProjectPath,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<DependencyEntryResult> Packages);

public sealed record DependencyEntryResult(
    string Package,
    string Version,
    bool IsDirect,
    string? TargetFramework,
    string PackageNodeId,
    string Project);

public sealed record FindVersionConflictsResult(
    string Workspace,
    IReadOnlyList<VersionConflictResult> Conflicts);

public sealed record VersionConflictResult(
    string Package,
    IReadOnlyList<string> Versions,
    IReadOnlyList<ConflictProjectResult> Projects);

public sealed record ConflictProjectResult(
    string Project,
    string Version,
    string? TargetFramework,
    bool IsDirect);

public sealed record FindUsagesResult(
    string? RequestedSymbolId,
    string? RequestedKind,
    string? RequestedFullName,
    string? RequestedVersion,
    IReadOnlyList<MatchedSymbolResult> MatchedSymbols,
    IReadOnlyList<UsageWorkspaceResult> Workspaces);

public sealed record MatchedSymbolResult(
    string SymbolId,
    string Kind,
    string FullName,
    string Version,
    string DisplayName);

public sealed record UsageWorkspaceResult(
    string Workspace,
    IReadOnlyList<UsageResult> Usages);

public sealed record UsageResult(
    string SymbolId,
    string UsageKind,
    string? File,
    int? StartLine,
    int? StartColumn,
    int? EndLine,
    int? EndColumn,
    string? SourceLabel,
    string EnclosingSymbolId,
    string EnclosingSymbolKind,
    string EnclosingSymbol,
    string EnclosingDisplayName);

public sealed record TraceHttpCallResult(
    string Method,
    string Path,
    IReadOnlyList<HttpTraceProducerResult> Producers,
    IReadOnlyList<HttpTraceConsumerResult> Consumers);

public sealed record HttpTraceProducerResult(
    string Workspace,
    string EndpointId,
    string DisplayName,
    string? Method,
    string? Path,
    string? SourceFile,
    int? StartLine,
    int? StartColumn,
    string? SourceLabel,
    string? HandlerSymbolId,
    string? HandlerSymbol,
    string? HandlerDisplayName);

public sealed record HttpTraceConsumerResult(
    string Workspace,
    string CallSiteId,
    string DisplayName,
    string? Method,
    string? Path,
    string? SourceFile,
    int? StartLine,
    int? StartColumn,
    string? SourceLabel,
    string? CallerSymbolId,
    string? CallerSymbol,
    string? CallerDisplayName);

internal sealed record TraceHttpCallAsyncResult(
    string Method,
    string Path,
    IReadOnlyList<HttpTraceProducerResult> Producers,
    IReadOnlyList<HttpTraceConsumerResult> Consumers);

public sealed record SuggestUsageResult(
    string? RequestedSymbolId,
    string? RequestedKind,
    string? RequestedFullName,
    string? RequestedVersion,
    int MaxResults,
    IReadOnlyList<MatchedSymbolResult> MatchedSymbols,
    IReadOnlyList<UsageSuggestionResult> Suggestions);

public sealed record UsageSuggestionResult(
    string Workspace,
    string SymbolId,
    string CallSiteSymbolId,
    string CallSiteSymbolKind,
    string CallSiteSymbol,
    string CallSiteDisplayName,
    string? File,
    int? StartLine,
    int? StartColumn,
    int? EndLine,
    int? EndColumn,
    string? SourceLabel,
    string? Excerpt,
    int Frequency,
    int Diversity,
    long RecencyUtcTicks,
    string RankReason);

internal sealed record SuggestUsageCandidate(
    string Workspace,
    string SymbolId,
    string CallSiteSymbolId,
    string CallSiteSymbolKind,
    string CallSiteSymbol,
    string CallSiteDisplayName,
    string? File,
    int? StartLine,
    int? StartColumn,
    int? EndLine,
    int? EndColumn,
    string? SourceLabel,
    string? Excerpt,
    int OccurrenceCount,
    int DiversityCount,
    long RecencyUtc,
    int BrevityScore,
    string RankReason)
{
    public UsageSuggestionResult ToResult()
    {
        return new UsageSuggestionResult(
            Workspace,
            SymbolId,
            CallSiteSymbolId,
            CallSiteSymbolKind,
            CallSiteSymbol,
            CallSiteDisplayName,
            File,
            StartLine,
            StartColumn,
            EndLine,
            EndColumn,
            SourceLabel,
            Excerpt,
            OccurrenceCount,
            DiversityCount,
            RecencyUtc,
            RankReason);
    }
}

internal readonly record struct SuggestUsageGroupKey(
    string Workspace,
    string CallSiteSymbolId,
    string? File,
    int? StartLine,
    int? StartColumn,
    int? EndLine,
    int? EndColumn,
    string? Excerpt)
{
    public static IEqualityComparer<SuggestUsageGroupKey> Comparer { get; } = new SuggestUsageGroupKeyComparer();

    private sealed class SuggestUsageGroupKeyComparer : IEqualityComparer<SuggestUsageGroupKey>
    {
        public bool Equals(SuggestUsageGroupKey x, SuggestUsageGroupKey y)
        {
            return string.Equals(x.Workspace, y.Workspace, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.CallSiteSymbolId, y.CallSiteSymbolId, StringComparison.Ordinal)
                   && string.Equals(x.File, y.File, StringComparison.OrdinalIgnoreCase)
                   && x.StartLine == y.StartLine
                   && x.StartColumn == y.StartColumn
                   && x.EndLine == y.EndLine
                   && x.EndColumn == y.EndColumn
                   && string.Equals(x.Excerpt, y.Excerpt, StringComparison.Ordinal);
        }

        public int GetHashCode(SuggestUsageGroupKey obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Workspace, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.CallSiteSymbolId, StringComparer.Ordinal);
            hash.Add(obj.File, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.StartLine);
            hash.Add(obj.StartColumn);
            hash.Add(obj.EndLine);
            hash.Add(obj.EndColumn);
            hash.Add(obj.Excerpt, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
