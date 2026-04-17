namespace CSharpDllGraph.Engine.Graph;

public sealed record WorkspaceSnapshot(
    WorkspaceManifest Manifest,
    IReadOnlyList<Node> Nodes,
    IReadOnlyList<Edge> Edges)
{
    public static WorkspaceSnapshot Empty { get; } = new(
        WorkspaceManifest.CreateDefault(),
        [],
        []);
}
