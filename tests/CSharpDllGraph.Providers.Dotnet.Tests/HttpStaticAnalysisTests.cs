using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Providers;
using CSharpDllGraph.Engine.Store;
using CSharpDllGraph.Providers.Dotnet.Http;

namespace CSharpDllGraph.Providers.Dotnet.Tests;

public sealed class HttpStaticAnalysisTests
{
    [Fact]
    public async Task SampleApi_ControllersAndMinimalApis_ProduceHttpEndpointNodes()
    {
        var snapshot = await BuildSampleApiSnapshotAsync();

        // Controller endpoints
        Assert.Contains(snapshot.Nodes, node =>
            node.Kind == NodeKind.HttpEndpoint
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "GET"
            && node.Attributes.TryGetValue("routeTemplate", out var r) && r.GetString() == "/api/users");

        Assert.Contains(snapshot.Nodes, node =>
            node.Kind == NodeKind.HttpEndpoint
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "GET"
            && node.Attributes.TryGetValue("routeTemplate", out var r) && r.GetString() == "/api/users/{id}");

        Assert.Contains(snapshot.Nodes, node =>
            node.Kind == NodeKind.HttpEndpoint
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "POST"
            && node.Attributes.TryGetValue("routeTemplate", out var r) && r.GetString() == "/api/users");

        Assert.Contains(snapshot.Nodes, node =>
            node.Kind == NodeKind.HttpEndpoint
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "DELETE"
            && node.Attributes.TryGetValue("routeTemplate", out var r) && r.GetString() == "/api/users/{id}");
    }

    [Fact]
    public async Task SampleApi_MinimalApis_ProduceHttpEndpointNodes()
    {
        var snapshot = await BuildSampleApiSnapshotAsync();

        // Minimal API endpoints from Program.cs
        Assert.Contains(snapshot.Nodes, node =>
            node.Kind == NodeKind.HttpEndpoint
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "GET"
            && node.Attributes.TryGetValue("routeTemplate", out var r) && r.GetString() == "/api/health");

        Assert.Contains(snapshot.Nodes, node =>
            node.Kind == NodeKind.HttpEndpoint
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "POST"
            && node.Attributes.TryGetValue("routeTemplate", out var r) && r.GetString() == "/api/ping");
    }

    [Fact]
    public async Task SampleApi_Controllers_ProduceHandlesRouteEdges()
    {
        var snapshot = await BuildSampleApiSnapshotAsync();

        var handlesRouteEdges = snapshot.Edges
            .Where(static edge => edge.Kind == EdgeKind.HandlesRoute)
            .ToArray();

        Assert.NotEmpty(handlesRouteEdges);

        // Every HandlesRoute edge must point from a Method/lambda node to an HttpEndpoint node
        foreach (var edge in handlesRouteEdges)
        {
            Assert.True(
                edge.FromId.Kind == NodeKind.Method,
                $"Expected HandlesRoute from a Method node, got {edge.FromId.Kind}");
            Assert.Equal(NodeKind.HttpEndpoint, edge.ToId.Kind);
        }
    }

    [Fact]
    public async Task SampleApi_HttpClientCalls_ProduceHttpCallSiteNodes()
    {
        var snapshot = await BuildSampleApiSnapshotAsync();

        // GET /api/orders
        Assert.Contains(snapshot.Nodes, node =>
            node.Kind == NodeKind.HttpCallSite
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "GET"
            && node.Attributes.TryGetValue("urlTemplate", out var u) && u.GetString() == "/api/orders");

        // POST /api/orders/{var}/submit (interpolated string)
        Assert.Contains(snapshot.Nodes, node =>
            node.Kind == NodeKind.HttpCallSite
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "POST"
            && node.Attributes.TryGetValue("urlTemplate", out var u) && u.GetString() == "/api/orders/{var}/submit");

        // DELETE /api/orders/{var} (string concatenation)
        Assert.Contains(snapshot.Nodes, node =>
            node.Kind == NodeKind.HttpCallSite
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "DELETE"
            && node.Attributes.TryGetValue("urlTemplate", out var u)
            && u.GetString()!.StartsWith("/api/orders/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SampleApi_HttpClientCalls_ProduceCallsRouteEdges()
    {
        var snapshot = await BuildSampleApiSnapshotAsync();

        var callsRouteEdges = snapshot.Edges
            .Where(static edge => edge.Kind == EdgeKind.CallsRoute)
            .ToArray();

        Assert.NotEmpty(callsRouteEdges);

        foreach (var edge in callsRouteEdges)
        {
            Assert.Equal(NodeKind.HttpCallSite, edge.ToId.Kind);
        }
    }

    [Fact]
    public async Task SampleApi_RouteNormalization_ProducerAndConsumerTemplatesMatch()
    {
        var snapshot = await BuildSampleApiSnapshotAsync();

        // Producer side: GET /api/users/{id} (controller with {id} parameter)
        // Consumer side: GET /api/orders normalized via RouteNormalizer
        // Validate that RouteNormalizer.NormalizePath strips constraints:
        // Producer: /api/users/{id:int} → /api/users/{id}
        // Consumer: /api/users/{id}     → /api/users/{id}
        var producerNormalized = RouteNormalizer.NormalizePath("/api/users/{id:int}");
        var consumerNormalized = RouteNormalizer.NormalizePath("/api/users/{id}");

        Assert.Equal(producerNormalized, consumerNormalized);
        Assert.Equal("/api/users/{id}", producerNormalized);

        // The graph must contain an HttpEndpoint whose routeTemplate matches the normalized form
        Assert.Contains(snapshot.Nodes, node =>
            node.Kind == NodeKind.HttpEndpoint
            && node.Attributes.TryGetValue("routeTemplate", out var r)
            && r.GetString() == producerNormalized);
    }

    [Fact]
    public void PostmanCallSiteProvider_ExtractsCallSiteNodes_FromFixtureCollection()
    {
        var fixtureRoot = GetSampleApiFixtureRoot();
        var collectionPath = Path.Combine(fixtureRoot, "SampleApi.postman_collection.json");
        Assert.True(File.Exists(collectionPath), $"Postman fixture not found: {collectionPath}");

        var json = File.ReadAllText(collectionPath);
        var nodes = PostmanCallSiteProvider.ParseCollection(json, collectionPath, fixtureRoot).ToList();

        // All extracted nodes must be HttpCallSite
        Assert.All(nodes, static node => Assert.Equal(NodeKind.HttpCallSite, node.Kind));

        // Must contain at least one node per top-level item that has a request
        Assert.True(nodes.Count >= 3, $"Expected at least 3 HttpCallSite nodes, got {nodes.Count}.");

        // Collection name attribute is preserved
        Assert.All(nodes, node =>
        {
            Assert.True(
                node.Attributes.TryGetValue("collectionName", out var col) && col.GetString() == "SampleApi",
                $"Node {node.Id} missing collectionName=SampleApi.");
        });

        // Folder path is set for nested items
        Assert.Contains(nodes, node =>
            node.Attributes.TryGetValue("folderPath", out var fp)
            && fp.GetString() == "Users");

        Assert.Contains(nodes, node =>
            node.Attributes.TryGetValue("folderPath", out var fp)
            && fp.GetString() == "Orders");

        // Postman variables should be substituted: {{baseUrl}} resolved and URL path extracted
        Assert.Contains(nodes, node =>
            node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "GET"
            && node.Attributes.TryGetValue("urlTemplate", out var u) && u.GetString() == "/api/users");

        // Unresolved variables become {varName} markers
        Assert.Contains(nodes, node =>
            node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "GET"
            && node.Attributes.TryGetValue("urlTemplate", out var u)
            && u.GetString() is { } url && url.Contains("{userId}", StringComparison.Ordinal));
    }

    [Fact]
    public void PostmanCallSiteProvider_ParsesBothUrlForms_StringAndObject()
    {
        const string json = """
            {
              "info": { "name": "TestCollection", "schema": "" },
              "item": [
                {
                  "name": "String URL request",
                  "request": {
                    "method": "GET",
                    "url": "https://example.com/api/string"
                  }
                },
                {
                  "name": "Object URL request",
                  "request": {
                    "method": "POST",
                    "url": {
                      "raw": "https://example.com/api/object",
                      "path": ["api", "object"]
                    }
                  }
                },
                {
                  "name": "Object URL with path fallback",
                  "request": {
                    "method": "DELETE",
                    "url": {
                      "path": ["api", "fallback"]
                    }
                  }
                }
              ]
            }
            """;

        var nodes = PostmanCallSiteProvider.ParseCollection(json, "/tmp/test.postman_collection.json", "/tmp").ToList();

        Assert.Equal(3, nodes.Count);

        Assert.Contains(nodes, n =>
            n.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "GET"
            && n.Attributes.TryGetValue("urlTemplate", out var u) && u.GetString() == "/api/string");

        Assert.Contains(nodes, n =>
            n.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "POST"
            && n.Attributes.TryGetValue("urlTemplate", out var u) && u.GetString() == "/api/object");

        Assert.Contains(nodes, n =>
            n.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "DELETE"
            && n.Attributes.TryGetValue("urlTemplate", out var u) && u.GetString() == "/api/fallback");
    }

    [Fact]
    public void PostmanCallSiteProvider_NormalizesVariables_KnownAndUnknown()
    {
        const string json = """
            {
              "info": { "name": "VarTest", "schema": "" },
              "item": [
                {
                  "name": "Known var substituted",
                  "request": {
                    "method": "GET",
                    "url": "{{baseUrl}}/api/items"
                  }
                },
                {
                  "name": "Unknown var becomes marker",
                  "request": {
                    "method": "GET",
                    "url": "{{baseUrl}}/api/items/{{unknownId}}"
                  }
                }
              ],
              "variable": [
                { "key": "baseUrl", "value": "https://localhost:5001" }
              ]
            }
            """;

        var nodes = PostmanCallSiteProvider.ParseCollection(json, "/tmp/test.postman_collection.json", "/tmp").ToList();

        Assert.Equal(2, nodes.Count);

        // {{baseUrl}} is resolved and scheme+host stripped — result is /api/items
        Assert.Contains(nodes, n =>
            n.Attributes.TryGetValue("urlTemplate", out var u) && u.GetString() == "/api/items");

        // {{unknownId}} becomes {unknownId} marker
        Assert.Contains(nodes, n =>
            n.Attributes.TryGetValue("urlTemplate", out var u)
            && u.GetString() == "/api/items/{unknownId}");
    }

    [Fact]
    public void JsFetchCallSiteProvider_ExtractsAtLeastThreeCallSiteNodes_FromFrontendFixture()
    {
        var frontendFixtureRoot = GetFrontendFixtureRoot();
        Assert.True(Directory.Exists(frontendFixtureRoot), $"Frontend fixture directory not found: {frontendFixtureRoot}");

        var provider = new JsFetchCallSiteProvider();
        // SolutionPath is required by the record contract but JsFetchCallSiteProvider only reads WorkspaceRootPath.
        var context = GraphBuildContext.Create(
            Path.Combine(frontendFixtureRoot, "dummy.slnx"),
            frontendFixtureRoot);

        var fragmentsTask = Task.Run(async () =>
        {
            var callSiteNodes = new List<Node>();
            await foreach (var fragment in provider.CollectAsync(context))
            {
                callSiteNodes.AddRange(fragment.Nodes.Where(static n => n.Kind == NodeKind.HttpCallSite));
            }

            return callSiteNodes;
        });

        var callSiteNodes = fragmentsTask.GetAwaiter().GetResult();

        Assert.True(
            callSiteNodes.Count >= 3,
            $"Expected at least 3 HttpCallSite nodes from the frontend fixture, but got {callSiteNodes.Count}.");

        // All extracted nodes must be HttpCallSite
        Assert.All(callSiteNodes, static node => Assert.Equal(NodeKind.HttpCallSite, node.Kind));
    }

    private static async Task<WorkspaceSnapshot> BuildSampleApiSnapshotAsync()
    {
        var fixtureRoot = GetSampleApiFixtureRoot();
        var solutionPath = Path.Combine(fixtureRoot, "SampleApi.slnx");
        var workspacePath = Path.Combine(Path.GetTempPath(), $"csharpdllgraph-phase05-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspacePath);

        var pipeline = new GraphBuildPipeline([
            new ControllerEndpointProvider(),
            new MinimalApiEndpointProvider(),
            new HttpClientCallSiteProvider()
        ]);

        var store = new JsonWorkspaceStore(workspacePath);
        await pipeline.BuildAndPersistAsync(
            GraphBuildContext.Create(solutionPath, workspacePath),
            store);

        return await store.LoadAsync();
    }

    private static string GetSampleApiFixtureRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "SampleApi"));
    }

    private static string GetFrontendFixtureRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "Frontend"));
    }
}
