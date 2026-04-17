using System.Text.Json;

namespace CSharpDllGraph.Engine.Graph;

public sealed record Edge(
    NodeId FromId,
    NodeId ToId,
    EdgeKind Kind,
    IReadOnlyDictionary<string, JsonElement> Attributes,
    IReadOnlyList<SourceRef> SourceRefs)
{
    public static Edge Create(
        NodeId fromId,
        NodeId toId,
        EdgeKind kind,
        IReadOnlyDictionary<string, JsonElement>? attributes = null,
        IReadOnlyList<SourceRef>? sourceRefs = null)
    {
        return new Edge(
            fromId,
            toId,
            kind,
            attributes ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            sourceRefs ?? []);
    }
}
