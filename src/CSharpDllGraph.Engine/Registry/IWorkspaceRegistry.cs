namespace CSharpDllGraph.Engine.Registry;

public interface IWorkspaceRegistry
{
    string RegistryFilePath { get; }

    IReadOnlyList<WorkspaceRegistration> List();

    Task<WorkspaceRegistration> AddAsync(
        WorkspaceRegistration registration,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        string workspaceName,
        CancellationToken cancellationToken = default);

    WorkspaceRegistration Resolve(string workspaceName);

    bool TryResolve(string workspaceName, out WorkspaceRegistration registration);
}
