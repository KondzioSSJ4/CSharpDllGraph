using System.Text.Json;

namespace CSharpDllGraph.Engine.Graph;

public sealed record Node(
    NodeId Id,
    NodeKind Kind,
    string DisplayName,
    IReadOnlyDictionary<string, JsonElement> Attributes,
    IReadOnlyList<SourceRef> SourceRefs)
{
    public static Node Create(
        NodeId id,
        NodeKind kind,
        string displayName,
        IReadOnlyDictionary<string, JsonElement>? attributes = null,
        IReadOnlyList<SourceRef>? sourceRefs = null)
    {
        return new Node(
            id,
            kind,
            displayName,
            attributes ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            sourceRefs ?? []);
    }
}
