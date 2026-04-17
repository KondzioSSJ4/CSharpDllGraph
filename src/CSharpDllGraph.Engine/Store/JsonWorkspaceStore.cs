using System.Text;
using System.Text.Json;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Graph.Serialization;

namespace CSharpDllGraph.Engine.Store;

public sealed class JsonWorkspaceStore : IWorkspaceStore
{
    private const string ManifestFileName = "manifest.json";
    private const string NodesFolderName = "nodes";
    private const string EdgesFolderName = "edges";

    private readonly string _workspaceRootPath;
    private readonly JsonSerializerOptions _serializerOptions;
    private WorkspaceManifest? _manifest;

    public JsonWorkspaceStore(string workspaceRootPath, JsonSerializerOptions? serializerOptions = null)
    {
        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            throw new ArgumentException("Workspace path is required.", nameof(workspaceRootPath));
        }

        _workspaceRootPath = Path.GetFullPath(workspaceRootPath);
        _serializerOptions = serializerOptions ?? GraphJsonSerializerOptions.Create();
    }

    public async Task<WorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var manifest = GetManifestOrDefault();
        var nodes = new List<Node>();
        var edges = new List<Edge>();

        foreach (var nodeKind in Enum.GetValues<NodeKind>())
        {
            var relativePath = Path.Combine(NodesFolderName, $"{ToFileName(nodeKind)}.json");
            var path = ResolveWorkspacePath(relativePath);
            if (!File.Exists(path))
            {
                continue;
            }

            await using var stream = OpenReader(relativePath);
            var shard = await JsonSerializer.DeserializeAsync<NodeShard>(stream, _serializerOptions, cancellationToken)
                        ?? new NodeShard([]);

            nodes.AddRange(shard.Nodes);
        }

        foreach (var edgeKind in Enum.GetValues<EdgeKind>())
        {
            var relativePath = Path.Combine(EdgesFolderName, $"{ToFileName(edgeKind)}.json");
            var path = ResolveWorkspacePath(relativePath);
            if (!File.Exists(path))
            {
                continue;
            }

            await using var stream = OpenReader(relativePath);
            var shard = await JsonSerializer.DeserializeAsync<EdgeShard>(stream, _serializerOptions, cancellationToken)
                        ?? new EdgeShard([]);

            edges.AddRange(shard.Edges);
        }

        var orderedNodes = nodes.OrderBy(static node => node, GraphOrdering.NodeComparer).ToArray();
        var orderedEdges = edges.OrderBy(static edge => edge, GraphOrdering.EdgeComparer).ToArray();
        return new WorkspaceSnapshot(manifest, orderedNodes, orderedEdges);
    }

    public async Task SaveAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_workspaceRootPath);
        Directory.CreateDirectory(ResolveWorkspacePath(NodesFolderName));
        Directory.CreateDirectory(ResolveWorkspacePath(EdgesFolderName));

        _manifest = NormalizeManifest(snapshot.Manifest);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(_manifest, _serializerOptions);
        await WriteAtomicFileAsync(ManifestFileName, manifestBytes, cancellationToken);

        var orderedNodes = snapshot.Nodes.OrderBy(static node => node, GraphOrdering.NodeComparer).ToArray();
        var orderedEdges = snapshot.Edges.OrderBy(static edge => edge, GraphOrdering.EdgeComparer).ToArray();

        foreach (var nodeKind in Enum.GetValues<NodeKind>())
        {
            var shard = new NodeShard(
                orderedNodes
                    .Where(node => node.Kind == nodeKind)
                    .Select(static node => new Node(
                        node.Id,
                        node.Kind,
                        node.DisplayName,
                        GraphOrdering.OrderAttributes(node.Attributes),
                        GraphOrdering.OrderSourceRefs(node.SourceRefs)))
                    .ToArray());

            var bytes = JsonSerializer.SerializeToUtf8Bytes(shard, _serializerOptions);
            await WriteAtomicFileAsync(Path.Combine(NodesFolderName, $"{ToFileName(nodeKind)}.json"), bytes, cancellationToken);
        }

        foreach (var edgeKind in Enum.GetValues<EdgeKind>())
        {
            var shard = new EdgeShard(
                orderedEdges
                    .Where(edge => edge.Kind == edgeKind)
                    .Select(static edge => new Edge(
                        edge.FromId,
                        edge.ToId,
                        edge.Kind,
                        GraphOrdering.OrderAttributes(edge.Attributes),
                        GraphOrdering.OrderSourceRefs(edge.SourceRefs)))
                    .ToArray());

            var bytes = JsonSerializer.SerializeToUtf8Bytes(shard, _serializerOptions);
            await WriteAtomicFileAsync(Path.Combine(EdgesFolderName, $"{ToFileName(edgeKind)}.json"), bytes, cancellationToken);
        }
    }

    public Stream OpenWriter(string relativePath)
    {
        var path = ResolveWorkspacePath(relativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    }

    public Stream OpenReader(string relativePath)
    {
        var path = ResolveWorkspacePath(relativePath);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public WorkspaceManifest GetManifest()
    {
        return GetManifestOrDefault();
    }

    private static WorkspaceManifest NormalizeManifest(WorkspaceManifest manifest)
    {
        return new WorkspaceManifest(
            manifest.SchemaVersion,
            manifest.EngineVersion,
            manifest.LastBuildUtc,
            manifest.ContentHashes
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
    }

    private static string ToFileName<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return value.ToString().ToLowerInvariant();
    }

    private async Task WriteAtomicFileAsync(string relativePath, byte[] bytes, CancellationToken cancellationToken)
    {
        var destinationPath = ResolveWorkspacePath(relativePath);
        var temporaryPath = destinationPath + ".tmp";
        var destinationDirectory = Path.GetDirectoryName(destinationPath);

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         options: FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, destinationPath, overwrite: true);
    }

    private WorkspaceManifest GetManifestOrDefault()
    {
        if (_manifest is not null)
        {
            return _manifest;
        }

        var path = ResolveWorkspacePath(ManifestFileName);
        if (!File.Exists(path))
        {
            _manifest = WorkspaceManifest.CreateDefault();
            return _manifest;
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        _manifest = JsonSerializer.Deserialize<WorkspaceManifest>(json, _serializerOptions)
                    ?? WorkspaceManifest.CreateDefault();
        return _manifest;
    }

    private string ResolveWorkspacePath(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(_workspaceRootPath, relativePath));
        var rootWithSeparator = _workspaceRootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _workspaceRootPath
            : _workspaceRootPath + Path.DirectorySeparatorChar;

        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, _workspaceRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path escapes workspace boundary.");
        }

        return path;
    }

    private sealed record NodeShard(IReadOnlyList<Node> Nodes);

    private sealed record EdgeShard(IReadOnlyList<Edge> Edges);
}
