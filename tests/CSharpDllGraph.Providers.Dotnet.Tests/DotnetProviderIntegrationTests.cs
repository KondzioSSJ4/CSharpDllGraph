using System.Text.Json;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Providers;
using CSharpDllGraph.Engine.Store;
using CSharpDllGraph.Providers.Dotnet;

namespace CSharpDllGraph.Providers.Dotnet.Tests;

public sealed class DotnetProviderIntegrationTests
{
    [Fact]
    public async Task FixtureSolution_ProducesExpectedStructuralGraph()
    {
        var fixtureRoot = GetFixtureRoot();
        var solutionPath = Path.Combine(fixtureRoot, "SampleSolution.slnx");
        var workspacePath = Path.Combine(Path.GetTempPath(), $"csharpdllgraph-phase02-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspacePath);

        var orchestrator = new GraphBuildPipeline([new DotnetProvider()]);
        var snapshot = await orchestrator.BuildAndPersistAsync(
            GraphBuildContext.Create(solutionPath, workspacePath),
            new JsonWorkspaceStore(workspacePath));

        Assert.Equal(2, snapshot.Nodes.Count(static node => node.Kind == NodeKind.Project));

        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Package, "Sample.WidgetKit", "1.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Package, "Sample.WidgetKit", "2.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Package, "Sample.Transitive", "1.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Package, "Sample.Transitive", "2.0.0"));

        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Assembly, "Sample.WidgetKit", "1.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Assembly, "Sample.WidgetKit", "2.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Type, "Sample.WidgetKit.LegacyWidgetService", "1.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Type, "Sample.WidgetKit.ModernWidgetService", "2.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Method, "Sample.WidgetKit.LegacyWidgetService.Get(string)", "1.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Method, "Sample.WidgetKit.ModernWidgetService.Get(string)", "2.0.0"));

        var dependsOnEdges = snapshot.Edges.Where(static edge => edge.Kind == EdgeKind.DependsOn).ToArray();
        Assert.Contains(dependsOnEdges, edge => MatchesDependency(edge, "AppV1", "Sample.WidgetKit", "1.0.0", isDirect: true));
        Assert.Contains(dependsOnEdges, edge => MatchesDependency(edge, "AppV1", "Sample.Transitive", "1.0.0", isDirect: false));
        Assert.Contains(dependsOnEdges, edge => MatchesDependency(edge, "AppV2", "Sample.WidgetKit", "2.0.0", isDirect: true));
        Assert.Contains(dependsOnEdges, edge => MatchesDependency(edge, "AppV2", "Sample.Transitive", "2.0.0", isDirect: false));

        var containsEdges = snapshot.Edges.Where(static edge => edge.Kind == EdgeKind.Contains).ToArray();
        Assert.DoesNotContain(
            containsEdges,
            static edge => IsVersioned(edge.FromId.Kind)
                           && IsVersioned(edge.ToId.Kind)
                           && !string.Equals(edge.FromId.VersionToken, edge.ToId.VersionToken, StringComparison.Ordinal));

        Assert.Contains(
            snapshot.Edges,
            static edge => edge.Kind == EdgeKind.Implements
                           && edge.FromId == new NodeId(NodeKind.Type, "Sample.WidgetKit.LegacyWidgetService", "1.0.0")
                           && edge.ToId == new NodeId(NodeKind.Type, "Sample.WidgetKit.IWidgetService", "1.0.0"));
        Assert.Contains(
            snapshot.Edges,
            static edge => edge.Kind == EdgeKind.Inherits
                           && edge.FromId == new NodeId(NodeKind.Type, "Sample.WidgetKit.ModernWidgetService", "2.0.0")
                           && edge.ToId == new NodeId(NodeKind.Type, "Sample.WidgetKit.WidgetBase", "2.0.0"));
        Assert.Contains(
            snapshot.Nodes,
            static node => node.Kind == NodeKind.ExternalRef
                           && node.Id == new NodeId(NodeKind.ExternalRef, "System.IDisposable", "external"));

        Assert.DoesNotContain(snapshot.Edges, static edge => edge.Kind is EdgeKind.Calls or EdgeKind.Uses);
    }

    private static bool MatchesDependency(Edge edge, string projectName, string packageName, string version, bool isDirect)
    {
        return edge.FromId == new NodeId(NodeKind.Project, projectName, "project")
               && edge.ToId == new NodeId(NodeKind.Package, packageName, version)
               && edge.Attributes["tfm"].GetString() == "net10.0"
               && GetBoolean(edge.Attributes["isDirect"]) == isDirect;
    }

    private static bool GetBoolean(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException("Expected boolean JsonElement.")
        };
    }

    private static bool IsVersioned(NodeKind kind)
    {
        return kind is NodeKind.Package or NodeKind.Assembly or NodeKind.Namespace or NodeKind.Type or NodeKind.Method;
    }

    private static string GetFixtureRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "SampleSolution"));
    }
}
