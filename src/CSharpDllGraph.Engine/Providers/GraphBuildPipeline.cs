using System.Text.Json;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Store;

namespace CSharpDllGraph.Engine.Providers;

public sealed class GraphBuildPipeline
{
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

        var nodesById = new Dictionary<NodeId, Node>();
        var edgesByKey = new Dictionary<EdgeKey, Edge>();

        foreach (var provider in _providers)
        {
            await foreach (var fragment in provider.CollectAsync(context, cancellationToken).WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                MergeFragment(fragment, nodesById, edgesByKey);
            }
        }

        var existingManifest = store.GetManifest();
        var manifest = new WorkspaceManifest(
            "phase-02",
            "phase-02",
            DateTimeOffset.UtcNow,
            existingManifest.ContentHashes);

        var snapshot = new WorkspaceSnapshot(
            manifest,
            nodesById.Values.ToArray(),
            edgesByKey.Values.ToArray());

        await store.SaveAsync(snapshot, cancellationToken);
        return snapshot;
    }

    private static void MergeFragment(
        GraphFragment fragment,
        IDictionary<NodeId, Node> nodesById,
        IDictionary<EdgeKey, Edge> edgesByKey)
    {
        foreach (var node in fragment.Nodes)
        {
            nodesById[node.Id] = node;
        }

        foreach (var edge in fragment.Edges)
        {
            edgesByKey[EdgeKey.Create(edge)] = edge;
        }
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
