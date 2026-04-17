using CSharpDllGraph.Engine.Graph;

namespace CSharpDllGraph.Engine.Query;

public interface IGraphQuery
{
    Node? GetNode(NodeId id);

    IReadOnlyList<Node> FindNodes(NodeKind kind, Func<Node, bool>? predicate = null);

    IReadOnlyList<Edge> GetEdges(NodeId? fromId = null, NodeId? toId = null, EdgeKind? kind = null);

    IReadOnlyList<Node> Neighbors(NodeId id, EdgeDirection direction, EdgeKind? kind = null);
}
