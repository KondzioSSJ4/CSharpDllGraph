using CSharpDllGraph.Engine.Query;
using CSharpDllGraph.Engine.Store;

namespace CSharpDllGraph.Engine.Config;

public sealed class WorkspaceContext : IWorkspaceContext
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile InMemoryGraphQuery? _query;

    public WorkspaceContext(WorkspaceConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public WorkspaceConfig Config { get; }

    public async Task<InMemoryGraphQuery> GetQueryAsync(CancellationToken cancellationToken = default)
    {
        var cached = _query;
        if (cached is not null)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            cached = _query;
            if (cached is not null)
            {
                return cached;
            }

            var snapshot = await new JsonWorkspaceStore(Config.GraphPath).LoadAsync(cancellationToken);
            cached = new InMemoryGraphQuery(snapshot);
            _query = cached;
            return cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _query = null;
    }
}
