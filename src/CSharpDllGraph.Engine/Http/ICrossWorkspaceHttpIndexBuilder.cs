namespace CSharpDllGraph.Engine.Http;

public interface ICrossWorkspaceHttpIndexBuilder
{
    Task<CrossWorkspaceHttpIndex> BuildAsync(CancellationToken cancellationToken = default);
}
