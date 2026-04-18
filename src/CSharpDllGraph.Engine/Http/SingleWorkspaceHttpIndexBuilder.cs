using CSharpDllGraph.Engine.Config;
using CSharpDllGraph.Engine.Graph;

namespace CSharpDllGraph.Engine.Http;

public sealed class SingleWorkspaceHttpIndexBuilder : ICrossWorkspaceHttpIndexBuilder
{
    private readonly IWorkspaceContext _workspaceContext;

    public SingleWorkspaceHttpIndexBuilder(IWorkspaceContext workspaceContext)
    {
        _workspaceContext = workspaceContext ?? throw new ArgumentNullException(nameof(workspaceContext));
    }

    public async Task<CrossWorkspaceHttpIndex> BuildAsync(CancellationToken cancellationToken = default)
    {
        var query = await _workspaceContext.GetQueryAsync(cancellationToken);
        var workspaceName = ResolveWorkspaceName(_workspaceContext.Config);
        var entries = new List<CrossWorkspaceHttpIndexEntry>();

        foreach (var nodeKind in new[] { NodeKind.HttpEndpoint, NodeKind.HttpCallSite })
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var node in query.FindNodes(nodeKind))
            {
                if (!TryCreateEntry(workspaceName, node, out var entry))
                {
                    continue;
                }

                entries.Add(entry);
            }
        }

        return new CrossWorkspaceHttpIndex(entries);
    }

    private static string ResolveWorkspaceName(WorkspaceConfig config)
    {
        var rootPath = config.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(rootPath);
        return string.IsNullOrWhiteSpace(name) ? config.RootPath : name;
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
