using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Providers;
using CSharpDllGraph.Engine.Store;
using CSharpDllGraph.Providers.Dotnet.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace CSharpDllGraph.Providers.Dotnet.Tests;

public sealed class IncrementalUpdateTests
{
    [Fact]
    public async Task Update_WhenSingleControllerFileChanges_OnlyOwnedGraphElementsChange()
    {
        var sourceFixtureRoot = GetSampleApiFixtureRoot();
        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"csharpdllgraph-incremental-{Guid.NewGuid():N}");
        CopyDirectory(sourceFixtureRoot, workspaceRoot);

        var solutionPath = Path.Combine(workspaceRoot, "SampleApi.slnx");
        var graphPath = Path.Combine(workspaceRoot, ".graph");
        var controllerPath = Path.Combine(workspaceRoot, "Controllers", "UsersController.cs");

        var initialSnapshot = await BuildSnapshotAsync(solutionPath, workspaceRoot, graphPath, isUpdate: false);
        var initialEndpoint = Assert.Single(
            initialSnapshot.Nodes,
            static node => node.Id == new NodeId(NodeKind.HttpEndpoint, "GET /api/users", "openapi"));

        Assert.Contains(initialEndpoint.SourceRefs, sourceRef => sourceRef.File.EndsWith("Controllers\\UsersController.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(initialEndpoint.SourceRefs, sourceRef => sourceRef.File.EndsWith("openapi.json", StringComparison.OrdinalIgnoreCase));

        var originalController = await File.ReadAllTextAsync(controllerPath);
        var updatedController = originalController.Replace("[HttpGet]", "[NonAction]", StringComparison.Ordinal);
        Assert.NotEqual(originalController, updatedController);
        await File.WriteAllTextAsync(controllerPath, updatedController);

        var updatedSnapshot = await BuildSnapshotAsync(solutionPath, workspaceRoot, graphPath, isUpdate: true);
        var updatedEndpoint = Assert.Single(
            updatedSnapshot.Nodes,
            static node => node.Id == new NodeId(NodeKind.HttpEndpoint, "GET /api/users", "openapi"));

        var nodeDiff = CalculateNodeDiff(initialSnapshot.Nodes, updatedSnapshot.Nodes);
        var edgeDiff = CalculateEdgeDiff(initialSnapshot.Edges, updatedSnapshot.Edges);

        Assert.Empty(nodeDiff.Added);
        Assert.Empty(nodeDiff.Removed);
        Assert.Single(nodeDiff.Changed);
        Assert.Equal(new NodeId(NodeKind.HttpEndpoint, "GET /api/users", "openapi"), nodeDiff.Changed[0].Before.Id);

        Assert.Empty(edgeDiff.Added);
        Assert.Empty(edgeDiff.Changed);
        var removedEdge = Assert.Single(edgeDiff.Removed);
        Assert.Equal(EdgeKind.HandlesRoute, removedEdge.Kind);
        Assert.Equal(
            new NodeId(NodeKind.Method, "SampleApi.Controllers.UsersController.GetAll()", "project-SampleApi"),
            removedEdge.FromId);
        Assert.Equal(new NodeId(NodeKind.HttpEndpoint, "GET /api/users", "openapi"), removedEdge.ToId);

        Assert.DoesNotContain(
            updatedEndpoint.SourceRefs,
            sourceRef => sourceRef.File.EndsWith("Controllers\\UsersController.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Single(updatedEndpoint.SourceRefs);
        Assert.EndsWith("openapi.json", updatedEndpoint.SourceRefs[0].File, StringComparison.OrdinalIgnoreCase);
    }

    private static (IReadOnlyList<Node> Added, IReadOnlyList<Node> Removed, IReadOnlyList<(Node Before, Node After)> Changed) CalculateNodeDiff(
        IReadOnlyList<Node> before,
        IReadOnlyList<Node> after)
    {
        var beforeById = before.ToDictionary(static node => node.Id);
        var afterById = after.ToDictionary(static node => node.Id);
        var added = new List<Node>();
        var removed = new List<Node>();
        var changed = new List<(Node Before, Node After)>();

        foreach (var node in after)
        {
            if (!beforeById.TryGetValue(node.Id, out var previous))
            {
                added.Add(node);
                continue;
            }

            if (!NodeContentEquals(previous, node))
            {
                changed.Add((previous, node));
            }
        }

        foreach (var node in before)
        {
            if (!afterById.ContainsKey(node.Id))
            {
                removed.Add(node);
            }
        }

        return (added, removed, changed);
    }

    private static (IReadOnlyList<Edge> Added, IReadOnlyList<Edge> Removed, IReadOnlyList<(Edge Before, Edge After)> Changed) CalculateEdgeDiff(
        IReadOnlyList<Edge> before,
        IReadOnlyList<Edge> after)
    {
        var beforeByKey = before.ToDictionary(static edge => new EdgeIdentity(edge));
        var afterByKey = after.ToDictionary(static edge => new EdgeIdentity(edge));

        var added = afterByKey
            .Where(pair => !beforeByKey.ContainsKey(pair.Key))
            .Select(static pair => pair.Value)
            .ToArray();

        var removed = beforeByKey
            .Where(pair => !afterByKey.ContainsKey(pair.Key))
            .Select(static pair => pair.Value)
            .ToArray();

        var changed = afterByKey
            .Where(pair => beforeByKey.TryGetValue(pair.Key, out var previous) && !EdgeContentEquals(previous, pair.Value))
            .Select(pair => (beforeByKey[pair.Key], pair.Value))
            .ToArray();

        return (added, removed, changed);
    }

    private static async Task<WorkspaceSnapshot> BuildSnapshotAsync(
        string solutionPath,
        string workspaceRoot,
        string graphPath,
        bool isUpdate)
    {
        var pipeline = new GraphBuildPipeline(
        [
            new ControllerEndpointProvider(),
            new MinimalApiEndpointProvider(),
            new HttpClientCallSiteProvider(),
            new OpenApiSpecProvider(),
        ], NullLogger<GraphBuildPipeline>.Instance);

        var store = new JsonWorkspaceStore(graphPath);
        var snapshot = await pipeline.BuildAndPersistAsync(
            GraphBuildContext.Create(solutionPath, workspaceRoot, isUpdate),
            store);
        var reconciled = HttpEndpointReconciler.Reconcile(snapshot.Nodes, snapshot.Edges);
        return new WorkspaceSnapshot(snapshot.Manifest, reconciled.Nodes, reconciled.Edges);
    }

    private static string GetSampleApiFixtureRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "SampleApi"));
    }

    private static bool NodeContentEquals(Node left, Node right)
    {
        return left.Id == right.Id
               && left.Kind == right.Kind
               && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
               && JsonAttributesEqual(left.Attributes, right.Attributes)
               && SourceRefsEqual(left.SourceRefs, right.SourceRefs);
    }

    private static bool EdgeContentEquals(Edge left, Edge right)
    {
        return left.FromId == right.FromId
               && left.ToId == right.ToId
               && left.Kind == right.Kind
               && JsonAttributesEqual(left.Attributes, right.Attributes)
               && SourceRefsEqual(left.SourceRefs, right.SourceRefs);
    }

    private static bool JsonAttributesEqual(
        IReadOnlyDictionary<string, JsonElement> left,
        IReadOnlyDictionary<string, JsonElement> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var rightValue))
            {
                return false;
            }

            if (!string.Equals(pair.Value.GetRawText(), rightValue.GetRawText(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SourceRefsEqual(IReadOnlyList<SourceRef> left, IReadOnlyList<SourceRef> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].File, right[index].File, StringComparison.Ordinal))
            {
                return false;
            }

            if (left[index].Spans.Count != right[index].Spans.Count)
            {
                return false;
            }

            for (var spanIndex = 0; spanIndex < left[index].Spans.Count; spanIndex++)
            {
                if (left[index].Spans[spanIndex] != right[index].Spans[spanIndex])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var targetPath = file.Replace(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private readonly record struct EdgeIdentity(NodeId FromId, NodeId ToId, EdgeKind Kind)
    {
        public EdgeIdentity(Edge edge) : this(edge.FromId, edge.ToId, edge.Kind)
        {
        }
    }
}
