namespace CSharpDllGraph.Engine.Http;

public sealed class CrossWorkspaceHttpIndex
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<CrossWorkspaceHttpIndexEntry>> _entriesByRouteKey;

    public CrossWorkspaceHttpIndex(IReadOnlyList<CrossWorkspaceHttpIndexEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Entries = entries
            .OrderBy(static entry => entry.Method, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Path, StringComparer.Ordinal)
            .ThenBy(static entry => entry.WorkspaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.WorkspaceName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Role)
            .ThenBy(static entry => entry.NodeId.ToString(), StringComparer.Ordinal)
            .ToArray();

        _entriesByRouteKey = Entries
            .GroupBy(static entry => CreateRouteKey(entry.Method, entry.Path), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<CrossWorkspaceHttpIndexEntry>)group.ToArray(),
                StringComparer.Ordinal);
    }

    public IReadOnlyList<CrossWorkspaceHttpIndexEntry> Entries { get; }

    public IReadOnlyList<CrossWorkspaceHttpIndexEntry> Find(string httpMethod, string path)
    {
        var normalized = HttpRouteNormalizer.Normalize(httpMethod, path);
        return _entriesByRouteKey.GetValueOrDefault(CreateRouteKey(normalized.Method, normalized.Path), []);
    }

    public static string CreateRouteKey(string method, string path)
    {
        return $"{HttpRouteNormalizer.NormalizeMethod(method)} {HttpRouteNormalizer.NormalizePath(path)}";
    }
}
