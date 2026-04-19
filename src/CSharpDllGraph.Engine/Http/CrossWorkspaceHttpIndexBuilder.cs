using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Registry;
using CSharpDllGraph.Engine.Store;

namespace CSharpDllGraph.Engine.Http;

public sealed class CrossWorkspaceHttpIndexBuilder : ICrossWorkspaceHttpIndexBuilder
{
    private readonly IReadOnlyList<WorkspaceRegistration> _registrations;

    public CrossWorkspaceHttpIndexBuilder(IReadOnlyList<WorkspaceRegistration> registrations)
    {
        _registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
    }

    public async Task<CrossWorkspaceHttpIndex> BuildAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<CrossWorkspaceHttpIndexEntry>();

        foreach (var registration in _registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var store = new JsonWorkspaceStore(registration.GraphPath);
            var snapshot = await store.LoadAsync(cancellationToken);

            foreach (var node in snapshot.Nodes)
            {
                if (!TryCreateEntry(registration.Name, node, out var entry))
                {
                    continue;
                }

                entries.Add(entry);
            }
        }

        return new CrossWorkspaceHttpIndex(entries);
    }

    private static bool TryCreateEntry(
        string workspaceName,
        Node node,
        out CrossWorkspaceHttpIndexEntry entry)
    {
        entry = default!;

        if (!TryResolveRole(node.Kind, out var role))
        {
            return false;
        }

        if (!TryReadAttribute(node, "httpMethod", out var method)
            || !TryReadAttribute(node, "routeTemplate", out var path))
        {
            return false;
        }

        var normalized = HttpRouteNormalizer.Normalize(method, path);
        if (string.IsNullOrEmpty(normalized.Method))
        {
            return false;
        }

        entry = new CrossWorkspaceHttpIndexEntry(
            workspaceName,
            node.Id,
            role,
            normalized.Method,
            normalized.Path);
        return true;
    }

    private static bool TryResolveRole(NodeKind nodeKind, out CrossWorkspaceHttpMatchRole role)
    {
        switch (nodeKind)
        {
            case NodeKind.HttpEndpoint:
                role = CrossWorkspaceHttpMatchRole.Producer;
                return true;
            case NodeKind.HttpCallSite:
                role = CrossWorkspaceHttpMatchRole.Consumer;
                return true;
            default:
                role = default;
                return false;
        }
    }

    private static bool TryReadAttribute(Node node, string key, out string value)
    {
        value = string.Empty;

        if (!node.Attributes.TryGetValue(key, out var jsonValue))
        {
            return false;
        }

        value = jsonValue.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
