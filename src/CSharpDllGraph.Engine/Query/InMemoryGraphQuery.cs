using CSharpDllGraph.Engine.Graph;

namespace CSharpDllGraph.Engine.Query;

public sealed class InMemoryGraphQuery : IGraphQuery
{
    private readonly IReadOnlyDictionary<NodeId, Node> _nodesById;
    private readonly IReadOnlyDictionary<NodeKind, IReadOnlyList<Node>> _nodesByKind;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Node>> _nodesByDisplayNamePrefix;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Node>> _nodesByAttributeKey;
    private readonly IReadOnlyList<Edge> _edges;

    public InMemoryGraphQuery(WorkspaceSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var orderedNodes = snapshot.Nodes.OrderBy(static node => node, GraphOrdering.NodeComparer).ToArray();
        _edges = snapshot.Edges.OrderBy(static edge => edge, GraphOrdering.EdgeComparer).ToArray();

        _nodesById = orderedNodes.ToDictionary(static node => node.Id, static node => node);
        _nodesByKind = orderedNodes
            .GroupBy(static node => node.Kind)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<Node>)group
                    .OrderBy(static node => node, GraphOrdering.NodeComparer)
                    .ToArray());

        _nodesByDisplayNamePrefix = BuildDisplayNamePrefixIndex(orderedNodes);
        _nodesByAttributeKey = BuildAttributeKeyIndex(orderedNodes);
    }

    public Node? GetNode(NodeId id)
    {
        return _nodesById.GetValueOrDefault(id);
    }

    public IReadOnlyList<Node> FindNodes(NodeKind kind, Func<Node, bool>? predicate = null)
    {
        if (!_nodesByKind.TryGetValue(kind, out var nodes))
        {
            return [];
        }

        if (predicate is null)
        {
            return nodes;
        }

        return nodes.Where(predicate).OrderBy(static node => node, GraphOrdering.NodeComparer).ToArray();
    }

    public IReadOnlyList<Edge> GetEdges(NodeId? fromId = null, NodeId? toId = null, EdgeKind? kind = null)
    {
        var query = _edges.AsEnumerable();

        if (fromId.HasValue)
        {
            query = query.Where(edge => edge.FromId == fromId.Value);
        }

        if (toId.HasValue)
        {
            query = query.Where(edge => edge.ToId == toId.Value);
        }

        if (kind.HasValue)
        {
            query = query.Where(edge => edge.Kind == kind.Value);
        }

        return query.OrderBy(static edge => edge, GraphOrdering.EdgeComparer).ToArray();
    }

    public IReadOnlyList<Node> Neighbors(NodeId id, EdgeDirection direction, EdgeKind? kind = null)
    {
        var edgeQuery = _edges.AsEnumerable();
        if (kind.HasValue)
        {
            edgeQuery = edgeQuery.Where(edge => edge.Kind == kind.Value);
        }

        IEnumerable<NodeId> neighborIds = direction switch
        {
            EdgeDirection.Outgoing => edgeQuery
                .Where(edge => edge.FromId == id)
                .Select(static edge => edge.ToId),
            EdgeDirection.Incoming => edgeQuery
                .Where(edge => edge.ToId == id)
                .Select(static edge => edge.FromId),
            EdgeDirection.Both => edgeQuery
                .Where(edge => edge.FromId == id || edge.ToId == id)
                .Select(edge => edge.FromId == id ? edge.ToId : edge.FromId),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction.")
        };

        return neighborIds
            .Distinct()
            .Select(neighborId => _nodesById.GetValueOrDefault(neighborId))
            .Where(static node => node is not null)
            .Select(static node => node!)
            .OrderBy(static node => node, GraphOrdering.NodeComparer)
            .ToArray();
    }

    public IReadOnlyList<Node> FindByDisplayNamePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return [];
        }

        var key = prefix.Trim().ToLowerInvariant();
        return _nodesByDisplayNamePrefix.GetValueOrDefault(key, []);
    }

    public IReadOnlyList<Node> FindByAttributeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return [];
        }

        return _nodesByAttributeKey.GetValueOrDefault(key.Trim(), []);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<Node>> BuildDisplayNamePrefixIndex(IReadOnlyList<Node> nodes)
    {
        var index = new Dictionary<string, List<Node>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var normalizedDisplayName = node.DisplayName.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalizedDisplayName))
            {
                continue;
            }

            for (var length = 1; length <= normalizedDisplayName.Length; length++)
            {
                var prefix = normalizedDisplayName[..length];
                if (!index.TryGetValue(prefix, out var list))
                {
                    list = [];
                    index[prefix] = list;
                }

                list.Add(node);
            }
        }

        return index.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<Node>)pair.Value
                .DistinctBy(static node => node.Id)
                .OrderBy(static node => node, GraphOrdering.NodeComparer)
                .ToArray(),
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<Node>> BuildAttributeKeyIndex(IReadOnlyList<Node> nodes)
    {
        var index = new Dictionary<string, List<Node>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            foreach (var attributeKey in node.Attributes.Keys)
            {
                if (!index.TryGetValue(attributeKey, out var list))
                {
                    list = [];
                    index[attributeKey] = list;
                }

                list.Add(node);
            }
        }

        return index.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<Node>)pair.Value
                .DistinctBy(static node => node.Id)
                .OrderBy(static node => node, GraphOrdering.NodeComparer)
                .ToArray(),
            StringComparer.Ordinal);
    }
}
