using System.Text.Json;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Graph.Serialization;
using CSharpDllGraph.Engine.Store;

namespace CSharpDllGraph.Engine.Providers;

public sealed class GraphBuildPipeline
{
    private const string ProviderCacheFolderName = "providers";
    private static readonly JsonSerializerOptions ProviderFragmentSerializerOptions = GraphJsonSerializerOptions.Create();
    private readonly IReadOnlyList<IGraphProvider> _providers;

    public GraphBuildPipeline(IEnumerable<IGraphProvider> providers)
    {
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
    }

    public async Task<WorkspaceSnapshot> BuildAndPersistAsync(
        GraphBuildContext context,
        IWorkspaceStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);

        var currentHashes = WorkspaceInputHashes.Collect(context);
        var previousManifest = store.GetManifest();
        var providersToRun = ResolveProvidersToRun(context, previousManifest.ContentHashes, currentHashes);
        var changedFilesByKind = WorkspaceInputHashes.GetChangedFiles(
            previousManifest.ContentHashes,
            currentHashes,
            context.WorkspaceRootPath);
        var cachedFragments = await LoadCachedFragmentsAsync(store, cancellationToken);

        if (context.IsUpdate
            && providersToRun.Count == 0
            && cachedFragments.Count > 0)
        {
            return await store.LoadAsync(cancellationToken);
        }

        var requiresFullRebuild = !context.IsUpdate
                                  || providersToRun.Count == _providers.Count
                                  || cachedFragments.Count == 0
                                  || _providers.Any(provider => !cachedFragments.ContainsKey(provider.Id) && !providersToRun.Contains(provider.Id));
        var fragmentsByProvider = requiresFullRebuild
            ? new Dictionary<string, GraphFragment>(StringComparer.Ordinal)
            : new Dictionary<string, GraphFragment>(cachedFragments, StringComparer.Ordinal);

        foreach (var provider in _providers)
        {
            if (!requiresFullRebuild && !providersToRun.Contains(provider.Id))
            {
                continue;
            }

            var mergedFragment = requiresFullRebuild || !cachedFragments.TryGetValue(provider.Id, out var cachedFragment)
                ? GraphFragment.Empty(provider.Id)
                : PrepareCachedFragmentForUpdate(provider.Id, cachedFragment, changedFilesByKind);
            await foreach (var fragment in provider.CollectAsync(context, cancellationToken).WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                mergedFragment = MergeFragments(mergedFragment, fragment);
            }

            fragmentsByProvider[provider.Id] = mergedFragment;
            await SaveProviderFragmentAsync(store, mergedFragment, cancellationToken);
        }

        var combinedFragment = GraphFragment.Empty("workspace");
        foreach (var provider in _providers)
        {
            if (fragmentsByProvider.TryGetValue(provider.Id, out var fragment))
            {
                combinedFragment = MergeFragments(combinedFragment, fragment);
            }
        }

        var manifest = new WorkspaceManifest(
            "phase-03",
            "phase-03",
            DateTimeOffset.UtcNow,
            currentHashes);

        var snapshot = new WorkspaceSnapshot(
            manifest,
            combinedFragment.Nodes,
            combinedFragment.Edges);

        await store.SaveAsync(snapshot, cancellationToken);
        return snapshot;
    }

    private static GraphFragment MergeFragments(GraphFragment current, GraphFragment next)
    {
        var nodesById = current.Nodes.ToDictionary(static node => node.Id);
        var edgesByKey = current.Edges.ToDictionary(static edge => EdgeKey.Create(edge));

        foreach (var node in next.Nodes)
        {
            nodesById[node.Id] = node;
        }

        foreach (var edge in next.Edges)
        {
            edgesByKey[EdgeKey.Create(edge)] = edge;
        }

        return new GraphFragment(
            next.ProviderId,
            nodesById.Values
                .OrderBy(static node => node, GraphOrdering.NodeComparer)
                .ToArray(),
            edgesByKey.Values
                .OrderBy(static edge => edge, GraphOrdering.EdgeComparer)
                .ToArray());
    }

    private static GraphFragment PrepareCachedFragmentForUpdate(
        string providerId,
        GraphFragment cachedFragment,
        IReadOnlyDictionary<WorkspaceInputKind, IReadOnlySet<string>> changedFilesByKind)
    {
        var ownedFiles = ResolveOwnedFiles(providerId, changedFilesByKind);
        if (ownedFiles.Count == 0)
        {
            return cachedFragment;
        }

        var removedNodeIds = cachedFragment.Nodes
            .Where(node => IsOwnedByChangedFile(node.SourceRefs, ownedFiles))
            .Select(static node => node.Id)
            .ToHashSet();

        var nodes = cachedFragment.Nodes
            .Where(node => !removedNodeIds.Contains(node.Id))
            .OrderBy(static node => node, GraphOrdering.NodeComparer)
            .ToArray();

        var edges = cachedFragment.Edges
            .Where(edge => !removedNodeIds.Contains(edge.FromId)
                           && !removedNodeIds.Contains(edge.ToId)
                           && !IsOwnedByChangedFile(edge.SourceRefs, ownedFiles))
            .OrderBy(static edge => edge, GraphOrdering.EdgeComparer)
            .ToArray();

        return new GraphFragment(providerId, nodes, edges);
    }

    private static IReadOnlySet<string> ResolveOwnedFiles(
        string providerId,
        IReadOnlyDictionary<WorkspaceInputKind, IReadOnlySet<string>> changedFilesByKind)
    {
        return providerId switch
        {
            "dotnet" => GetChangedFiles(changedFilesByKind, WorkspaceInputKind.Source, WorkspaceInputKind.Project),
            "dotnet-controller-endpoints" => GetChangedFiles(changedFilesByKind, WorkspaceInputKind.Source, WorkspaceInputKind.Project),
            "dotnet-minimal-api" => GetChangedFiles(changedFilesByKind, WorkspaceInputKind.Source, WorkspaceInputKind.Project),
            "dotnet-http-callsite" => GetChangedFiles(changedFilesByKind, WorkspaceInputKind.Source, WorkspaceInputKind.Project),
            "js-fetch-callsite" => GetChangedFiles(changedFilesByKind, WorkspaceInputKind.Source),
            "http-file-callsite" => GetChangedFiles(changedFilesByKind, WorkspaceInputKind.HttpSpec),
            "openapi-spec" => GetChangedFiles(changedFilesByKind, WorkspaceInputKind.HttpSpec),
            "postman-callsite" => GetChangedFiles(changedFilesByKind, WorkspaceInputKind.HttpSpec),
            _ => GetChangedFiles(changedFilesByKind)
        };
    }

    private static IReadOnlySet<string> GetChangedFiles(
        IReadOnlyDictionary<WorkspaceInputKind, IReadOnlySet<string>> changedFilesByKind,
        params WorkspaceInputKind[] kinds)
    {
        if (kinds.Length == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in kinds)
        {
            if (!changedFilesByKind.TryGetValue(kind, out var changedFiles))
            {
                continue;
            }

            files.UnionWith(changedFiles);
        }

        return files;
    }

    private static bool IsOwnedByChangedFile(
        IReadOnlyList<SourceRef> sourceRefs,
        IReadOnlySet<string> changedFiles)
    {
        return sourceRefs.Any(sourceRef =>
            !string.IsNullOrWhiteSpace(sourceRef.File)
            && changedFiles.Contains(Path.GetFullPath(sourceRef.File)));
    }

    private async Task<Dictionary<string, GraphFragment>> LoadCachedFragmentsAsync(
        IWorkspaceStore store,
        CancellationToken cancellationToken)
    {
        var fragments = new Dictionary<string, GraphFragment>(StringComparer.Ordinal);

        foreach (var provider in _providers)
        {
            var relativePath = GetProviderCachePath(provider.Id);

            try
            {
                await using var stream = store.OpenReader(relativePath);
                var fragment = await JsonSerializer.DeserializeAsync<GraphFragment>(
                    stream,
                    ProviderFragmentSerializerOptions,
                    cancellationToken);
                if (fragment is not null)
                {
                    fragments[provider.Id] = fragment;
                }
            }
            catch (FileNotFoundException)
            {
                return new Dictionary<string, GraphFragment>(StringComparer.Ordinal);
            }
            catch (DirectoryNotFoundException)
            {
                return new Dictionary<string, GraphFragment>(StringComparer.Ordinal);
            }
        }

        return fragments;
    }

    private static async Task SaveProviderFragmentAsync(
        IWorkspaceStore store,
        GraphFragment fragment,
        CancellationToken cancellationToken)
    {
        var relativePath = GetProviderCachePath(fragment.ProviderId);
        await using var stream = store.OpenWriter(relativePath);
        await JsonSerializer.SerializeAsync(
            stream,
            fragment,
            ProviderFragmentSerializerOptions,
            cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private HashSet<string> ResolveProvidersToRun(
        GraphBuildContext context,
        IReadOnlyDictionary<string, string> previousHashes,
        IReadOnlyDictionary<string, string> currentHashes)
    {
        if (!context.IsUpdate || previousHashes.Count == 0)
        {
            return _providers.Select(static provider => provider.Id).ToHashSet(StringComparer.Ordinal);
        }

        var changedKinds = WorkspaceInputHashes.GetChangedKinds(previousHashes, currentHashes);
        if (changedKinds.Count == 0)
        {
            return [];
        }

        var providerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in changedKinds)
        {
            foreach (var providerId in GetProviderIdsForKind(kind))
            {
                providerIds.Add(providerId);
            }
        }

        return providerIds;
    }

    private static IReadOnlyList<string> GetProviderIdsForKind(WorkspaceInputKind kind)
    {
        return kind switch
        {
            WorkspaceInputKind.Source =>
            [
                "dotnet",
                "dotnet-controller-endpoints",
                "dotnet-minimal-api",
                "dotnet-http-callsite",
                "js-fetch-callsite"
            ],
            WorkspaceInputKind.Project =>
            [
                "dotnet",
                "dotnet-controller-endpoints",
                "dotnet-minimal-api",
                "dotnet-http-callsite"
            ],
            WorkspaceInputKind.Dependency =>
            [
                "dotnet",
                "dotnet-controller-endpoints",
                "dotnet-minimal-api",
                "dotnet-http-callsite"
            ],
            WorkspaceInputKind.HttpSpec =>
            [
                "http-file-callsite",
                "openapi-spec",
                "postman-callsite"
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static string GetProviderCachePath(string providerId)
    {
        return Path.Combine(ProviderCacheFolderName, $"{providerId}.json");
    }

    private readonly record struct EdgeKey(
        NodeId FromId,
        NodeId ToId,
        EdgeKind Kind,
        string AttributeKey)
    {
        public static EdgeKey Create(Edge edge)
        {
            return new EdgeKey(
                edge.FromId,
                edge.ToId,
                edge.Kind,
                CreateAttributeKey(edge.Attributes));
        }

        private static string CreateAttributeKey(IReadOnlyDictionary<string, JsonElement> attributes)
        {
            if (attributes.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(
                "|",
                attributes
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => $"{pair.Key}={pair.Value.GetRawText()}"));
        }
    }
}
