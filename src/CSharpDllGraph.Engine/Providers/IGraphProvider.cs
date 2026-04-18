namespace CSharpDllGraph.Engine.Providers;

public interface IGraphProvider
{
    string Id { get; }

    /// <summary>
    /// Emits graph fragments for the requested workspace build.
    /// Reserved subprocess protocol shape (not implemented in phase 02):
    /// request: { "protocolVersion": "1", "command": "build", "solutionPath": "...", "workspaceRootPath": "..." }
    /// stream:  { "type": "fragment", "nodes": [...], "edges": [...] }* then { "type": "done" }
    /// </summary>
    IAsyncEnumerable<GraphFragment> CollectAsync(
        GraphBuildContext context,
        CancellationToken cancellationToken = default);
}
