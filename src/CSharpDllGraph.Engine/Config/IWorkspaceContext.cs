using CSharpDllGraph.Engine.Query;

namespace CSharpDllGraph.Engine.Config;

public interface IWorkspaceContext
{
    WorkspaceConfig Config { get; }

    Task<InMemoryGraphQuery> GetQueryAsync(CancellationToken cancellationToken = default);

    void Invalidate();
}
