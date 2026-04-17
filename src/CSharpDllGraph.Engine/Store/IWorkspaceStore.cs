using CSharpDllGraph.Engine.Graph;

namespace CSharpDllGraph.Engine.Store;

public interface IWorkspaceStore
{
    Task<WorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default);

    Stream OpenWriter(string relativePath);

    Stream OpenReader(string relativePath);

    WorkspaceManifest GetManifest();
}
