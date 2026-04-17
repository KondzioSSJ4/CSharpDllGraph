using System.Text.Json;
using CSharpDllGraph.Engine.Graph;

namespace CSharpDllGraph.Engine.Tests;

internal static class GraphFixtureBuilder
{
    public static WorkspaceSnapshot BuildSnapshot()
    {
        var workspace = Node.Create(
            new NodeId(NodeKind.Workspace, "SampleWorkspace", "workspace-v1"),
            NodeKind.Workspace,
            "SampleWorkspace",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["rootPath"] = ParseJsonElement("\"D:/repo\"")
            });

        var project = Node.Create(
            new NodeId(NodeKind.Project, "SampleWorkspace.App", "project-v1"),
            NodeKind.Project,
            "SampleWorkspace.App",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["targetFramework"] = ParseJsonElement("\"net10.0\"")
            });

        var package10 = CreatePackageNode("X", "1.0.0");
        var package12 = CreatePackageNode("X", "1.2.0");

        var assembly10 = Node.Create(
            new NodeId(NodeKind.Assembly, "X.Core", "1.0.0"),
            NodeKind.Assembly,
            "X.Core@1.0.0");
        var assembly12 = Node.Create(
            new NodeId(NodeKind.Assembly, "X.Core", "1.2.0"),
            NodeKind.Assembly,
            "X.Core@1.2.0");

        var namespace10 = Node.Create(
            new NodeId(NodeKind.Namespace, "X.Core.Services", "1.0.0"),
            NodeKind.Namespace,
            "X.Core.Services@1.0.0");
        var namespace12 = Node.Create(
            new NodeId(NodeKind.Namespace, "X.Core.Services", "1.2.0"),
            NodeKind.Namespace,
            "X.Core.Services@1.2.0");

        var type10 = CreateTypeNode("X.Core.Services.WidgetService", "1.0.0");
        var type12 = CreateTypeNode("X.Core.Services.WidgetService", "1.2.0");

        var method10 = CreateMethodNode("X.Core.Services.WidgetService.Get", "1.0.0");
        var method12 = CreateMethodNode("X.Core.Services.WidgetService.Get", "1.2.0");

        var endpoint = Node.Create(
            new NodeId(NodeKind.HttpEndpoint, "GET:/widgets/{id}", "http-v1"),
            NodeKind.HttpEndpoint,
            "GET /widgets/{id}");

        var callSite = Node.Create(
            new NodeId(NodeKind.HttpCallSite, "SampleWorkspace.App.HttpClient.GetWidget", "callsite-v1"),
            NodeKind.HttpCallSite,
            "HttpClient.GetWidget");

        var external = Node.Create(
            new NodeId(NodeKind.ExternalRef, "System.Console.WriteLine", "external-v1"),
            NodeKind.ExternalRef,
            "System.Console.WriteLine");

        var nodes = new[]
        {
            workspace, project, package10, package12, assembly10, assembly12, namespace10, namespace12,
            type10, type12, method10, method12, endpoint, callSite, external
        };

        var edges = new[]
        {
            Edge.Create(workspace.Id, project.Id, EdgeKind.Contains),
            Edge.Create(project.Id, package10.Id, EdgeKind.DependsOn),
            Edge.Create(project.Id, package12.Id, EdgeKind.DependsOn),
            Edge.Create(package10.Id, assembly10.Id, EdgeKind.Contains),
            Edge.Create(package12.Id, assembly12.Id, EdgeKind.Contains),
            Edge.Create(assembly10.Id, namespace10.Id, EdgeKind.Contains),
            Edge.Create(assembly12.Id, namespace12.Id, EdgeKind.Contains),
            Edge.Create(namespace10.Id, type10.Id, EdgeKind.Contains),
            Edge.Create(namespace12.Id, type12.Id, EdgeKind.Contains),
            Edge.Create(type10.Id, method10.Id, EdgeKind.Contains),
            Edge.Create(type12.Id, method12.Id, EdgeKind.Contains),
            Edge.Create(method12.Id, method10.Id, EdgeKind.Calls),
            Edge.Create(method12.Id, endpoint.Id, EdgeKind.HandlesRoute),
            Edge.Create(callSite.Id, endpoint.Id, EdgeKind.CallsRoute),
            Edge.Create(method10.Id, external.Id, EdgeKind.References)
        };

        var manifest = new WorkspaceManifest(
            "phase-01",
            "phase-01-tests",
            new DateTimeOffset(2026, 4, 17, 0, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>(StringComparer.Ordinal));

        return new WorkspaceSnapshot(manifest, nodes, edges);
    }

    public static Node CreatePackageNode(string name, string version)
    {
        return Node.Create(
            new NodeId(NodeKind.Package, name, version),
            NodeKind.Package,
            $"{name}@{version}",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["packageName"] = ParseJsonElement($"\"{name}\""),
                ["version"] = ParseJsonElement($"\"{version}\"")
            });
    }

    public static Node CreateTypeNode(string fullyQualifiedTypeName, string version)
    {
        return Node.Create(
            new NodeId(NodeKind.Type, fullyQualifiedTypeName, version),
            NodeKind.Type,
            $"{fullyQualifiedTypeName}@{version}",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["kind"] = ParseJsonElement("\"class\"")
            },
            [
                new SourceRef(
                    "src/SampleWorkspace.App/Services/WidgetService.cs",
                    [new SourceSpan(1, 1, 20, 1)])
            ]);
    }

    public static Node CreateMethodNode(string fullyQualifiedMethodName, string version)
    {
        return Node.Create(
            new NodeId(NodeKind.Method, fullyQualifiedMethodName, version),
            NodeKind.Method,
            $"{fullyQualifiedMethodName}@{version}",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["signature"] = ParseJsonElement($"\"{fullyQualifiedMethodName}(string id)\"")
            },
            [
                new SourceRef(
                    "src/SampleWorkspace.App/Services/WidgetService.cs",
                    [new SourceSpan(5, 5, 12, 6)])
            ]);
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
