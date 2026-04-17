using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Query;

namespace CSharpDllGraph.Engine.Tests;

public sealed class GraphQueryTests
{
    [Fact]
    public void GetNode_ReturnsNodeById()
    {
        var snapshot = GraphFixtureBuilder.BuildSnapshot();
        var query = new InMemoryGraphQuery(snapshot);
        var expectedId = new NodeId(NodeKind.Type, "X.Core.Services.WidgetService", "1.2.0");

        var node = query.GetNode(expectedId);

        Assert.NotNull(node);
        Assert.Equal(expectedId, node!.Id);
        Assert.Equal(NodeKind.Type, node.Kind);
    }

    [Fact]
    public void FindNodes_ReturnsAllVersionsOfPackageX()
    {
        var snapshot = GraphFixtureBuilder.BuildSnapshot();
        var query = new InMemoryGraphQuery(snapshot);

        var packages = query.FindNodes(
            NodeKind.Package,
            node => node.DisplayName.StartsWith("X@", StringComparison.Ordinal));

        Assert.Equal(2, packages.Count);
        Assert.Contains(packages, node => node.Id == new NodeId(NodeKind.Package, "X", "1.0.0"));
        Assert.Contains(packages, node => node.Id == new NodeId(NodeKind.Package, "X", "1.2.0"));
    }

    [Fact]
    public void GetEdges_CanFilterByKindAndEndpoints()
    {
        var snapshot = GraphFixtureBuilder.BuildSnapshot();
        var query = new InMemoryGraphQuery(snapshot);
        var projectId = new NodeId(NodeKind.Project, "SampleWorkspace.App", "project-v1");
        var packageId = new NodeId(NodeKind.Package, "X", "1.2.0");

        var dependsOnEdges = query.GetEdges(kind: EdgeKind.DependsOn);
        var exactEdge = query.GetEdges(projectId, packageId, EdgeKind.DependsOn);

        Assert.Equal(2, dependsOnEdges.Count);
        Assert.Single(exactEdge);
        Assert.Equal(projectId, exactEdge[0].FromId);
        Assert.Equal(packageId, exactEdge[0].ToId);
    }

    [Fact]
    public void Neighbors_ReturnsTypeNeighborsAndSupportsKindFiltering()
    {
        var snapshot = GraphFixtureBuilder.BuildSnapshot();
        var query = new InMemoryGraphQuery(snapshot);
        var typeId = new NodeId(NodeKind.Type, "X.Core.Services.WidgetService", "1.2.0");

        var outgoingContains = query.Neighbors(typeId, EdgeDirection.Outgoing, EdgeKind.Contains);
        var allOutgoing = query.Neighbors(typeId, EdgeDirection.Outgoing);
        var incomingContains = query.Neighbors(typeId, EdgeDirection.Incoming, EdgeKind.Contains);

        Assert.Single(outgoingContains);
        Assert.Equal(NodeKind.Method, outgoingContains[0].Kind);
        Assert.Equal(new NodeId(NodeKind.Method, "X.Core.Services.WidgetService.Get", "1.2.0"), outgoingContains[0].Id);

        Assert.Equal(outgoingContains, allOutgoing);

        Assert.Single(incomingContains);
        Assert.Equal(NodeKind.Namespace, incomingContains[0].Kind);
    }

    [Fact]
    public void SecondaryIndexes_AreAvailableForPrefixAndAttributeLookups()
    {
        var snapshot = GraphFixtureBuilder.BuildSnapshot();
        var query = new InMemoryGraphQuery(snapshot);

        var prefixMatches = query.FindByDisplayNamePrefix("x.core.services.widgetservice@1.");
        var attributeMatches = query.FindByAttributeKey("signature");

        Assert.Equal(2, prefixMatches.Count);
        Assert.Equal(2, attributeMatches.Count);
        Assert.All(attributeMatches, node => Assert.Equal(NodeKind.Method, node.Kind));
    }
}
