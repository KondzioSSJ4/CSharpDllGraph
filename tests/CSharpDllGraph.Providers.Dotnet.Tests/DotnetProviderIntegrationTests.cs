using System.Text.Json;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Providers;
using CSharpDllGraph.Engine.Store;
using CSharpDllGraph.Providers.Dotnet;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSharpDllGraph.Providers.Dotnet.Tests;

public sealed class DotnetProviderIntegrationTests
{
    [Fact]
    public async Task FixtureSolution_ProducesExpectedStructuralAndUsageGraph()
    {
        var fixtureRoot = GetFixtureRoot();
        var solutionPath = Path.Combine(fixtureRoot, "SampleSolution.slnx");
        var workspacePath = Path.Combine(Path.GetTempPath(), $"csharpdllgraph-phase02-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspacePath);

        var orchestrator = new GraphBuildPipeline([new DotnetProvider(NullLogger<DotnetProvider>.Instance)], NullLogger<GraphBuildPipeline>.Instance);
        var store = new JsonWorkspaceStore(workspacePath);
        await orchestrator.BuildAndPersistAsync(
            GraphBuildContext.Create(solutionPath, workspacePath),
            store);
        var snapshot = await store.LoadAsync();

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
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Type, "Sample.WidgetKit.IWidgetService", "1.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Type, "Sample.WidgetKit.IWidgetService", "2.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Method, "Sample.WidgetKit.IWidgetService.Get(string)", "1.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Method, "Sample.WidgetKit.IWidgetService.Get(string)", "2.0.0"));
        Assert.Contains(snapshot.Nodes, static node => node.Id == new NodeId(NodeKind.Method, "Sample.WidgetKit.ModernWidgetService.GetNormalized(string)", "2.0.0"));

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

        var appV1Type = Assert.Single(snapshot.Nodes, node => MatchesUserNode(node, NodeKind.Type, "Sample.AppV1.Usage"));
        var appV1Method = Assert.Single(snapshot.Nodes, node => MatchesUserNode(node, NodeKind.Method, "Sample.AppV1.Usage.Run(string)"));
        var appV2Type = Assert.Single(snapshot.Nodes, node => MatchesUserNode(node, NodeKind.Type, "Sample.AppV2.Usage"));
        var appV2Method = Assert.Single(snapshot.Nodes, node => MatchesUserNode(node, NodeKind.Method, "Sample.AppV2.Usage.Run(string)"));

        Assert.True(IsUserNode(appV1Type));
        Assert.True(IsUserNode(appV1Method));
        Assert.True(IsUserNode(appV2Type));
        Assert.True(IsUserNode(appV2Method));
        Assert.Equal("project-AppV1/AppV1", appV1Type.Attributes["projectVersion"].GetString());
        Assert.Equal("project-AppV1/AppV1", appV1Method.Attributes["projectVersion"].GetString());
        Assert.Equal("project-AppV2/AppV2", appV2Type.Attributes["projectVersion"].GetString());
        Assert.Equal("project-AppV2/AppV2", appV2Method.Attributes["projectVersion"].GetString());

        Assert.Contains(
            snapshot.Edges,
            edge => edge.Kind == EdgeKind.Calls
                    && edge.FromId == appV1Method.Id
                    && edge.ToId == new NodeId(NodeKind.Method, "Sample.WidgetKit.IWidgetService.Get(string)", "1.0.0")
                    && GetInt32(edge.Attributes["callCount"]) == 2
                    && HasSourceRefs(edge));
        Assert.Contains(
            snapshot.Edges,
            edge => edge.Kind == EdgeKind.Uses
                    && edge.FromId == appV1Method.Id
                    && edge.ToId == new NodeId(NodeKind.Type, "Sample.WidgetKit.LegacyWidgetService", "1.0.0")
                    && HasSourceRefs(edge));
        Assert.Contains(
            snapshot.Edges,
            edge => edge.Kind == EdgeKind.Uses
                    && edge.FromId == appV1Method.Id
                    && edge.ToId == new NodeId(NodeKind.Type, "Sample.WidgetKit.IWidgetService", "1.0.0")
                    && HasSourceRefs(edge));
        Assert.Contains(
            snapshot.Edges,
            edge => edge.Kind == EdgeKind.Uses
                    && edge.FromId == appV1Method.Id
                    && edge.ToId == new NodeId(NodeKind.Type, "Sample.Transitive.SharedContract", "1.0.0")
                    && HasSourceRefs(edge));

        Assert.Contains(
            snapshot.Edges,
            edge => edge.Kind == EdgeKind.Calls
                    && edge.FromId == appV2Method.Id
                    && edge.ToId == new NodeId(NodeKind.Method, "Sample.WidgetKit.IWidgetService.Get(string)", "2.0.0")
                    && GetInt32(edge.Attributes["callCount"]) == 2
                    && HasSourceRefs(edge));
        Assert.Contains(
            snapshot.Edges,
            edge => edge.Kind == EdgeKind.Calls
                    && edge.FromId == appV2Method.Id
                    && edge.ToId == new NodeId(NodeKind.Method, "Sample.WidgetKit.ModernWidgetService.GetNormalized(string)", "2.0.0")
                    && GetInt32(edge.Attributes["callCount"]) == 1
                    && HasSourceRefs(edge));
        Assert.Contains(
            snapshot.Edges,
            edge => edge.Kind == EdgeKind.Uses
                    && edge.FromId == appV2Method.Id
                    && edge.ToId == new NodeId(NodeKind.Type, "Sample.WidgetKit.ModernWidgetService", "2.0.0")
                    && HasSourceRefs(edge));
        Assert.Contains(
            snapshot.Edges,
            edge => edge.Kind == EdgeKind.Uses
                    && edge.FromId == appV2Method.Id
                    && edge.ToId == new NodeId(NodeKind.Type, "Sample.WidgetKit.IWidgetService", "2.0.0")
                    && HasSourceRefs(edge));
        Assert.Contains(
            snapshot.Edges,
            edge => edge.Kind == EdgeKind.Uses
                    && edge.FromId == appV2Method.Id
                    && edge.ToId == new NodeId(NodeKind.Type, "Sample.Transitive.SharedContract", "2.0.0")
                    && HasSourceRefs(edge));

        Assert.DoesNotContain(
            snapshot.Edges,
            edge => edge.Kind is EdgeKind.Calls or EdgeKind.Uses
                    && edge.FromId.VersionToken == appV1Method.Id.VersionToken
                    && edge.ToId.VersionToken == "2.0.0");
        Assert.DoesNotContain(
            snapshot.Edges,
            edge => edge.Kind is EdgeKind.Calls or EdgeKind.Uses
                    && edge.FromId.VersionToken == appV2Method.Id.VersionToken
                    && edge.ToId.VersionToken == "1.0.0");
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

    private static int GetInt32(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : throw new InvalidOperationException("Expected numeric JsonElement.");
    }

    private static bool HasSourceRefs(Edge edge)
    {
        return edge.SourceRefs.Count > 0
               && edge.SourceRefs.All(static sourceRef => !string.IsNullOrWhiteSpace(sourceRef.File) && sourceRef.Spans.Count > 0);
    }

    private static bool IsUserNode(Node node)
    {
        return node.Attributes.TryGetValue("origin", out var origin) && origin.GetString() == "user";
    }

    private static bool MatchesUserNode(Node node, NodeKind kind, string fullyQualifiedName)
    {
        return node.Kind == kind
               && node.DisplayName.StartsWith(fullyQualifiedName + "@", StringComparison.Ordinal);
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
