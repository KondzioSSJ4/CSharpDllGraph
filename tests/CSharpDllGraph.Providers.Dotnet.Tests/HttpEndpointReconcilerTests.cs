using System.Text.Json;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Providers.Dotnet.Http;

namespace CSharpDllGraph.Providers.Dotnet.Tests;

/// <summary>
/// Unit tests for <see cref="HttpEndpointReconciler"/>.
/// Covers merge, attribute union, disagreement surfacing, and edge rewriting.
/// </summary>
public sealed class HttpEndpointReconcilerTests
{
    // -------------------------------------------------------------------------
    // Helper factories
    // -------------------------------------------------------------------------

    private static Node MakeEndpoint(
        string method,
        string routeTemplate,
        string versionToken,
        string? sourceName = null,
        string? summary = null,
        string? sourceFile = null)
    {
        var fqn = $"{method} {routeTemplate}";
        var id = new NodeId(NodeKind.HttpEndpoint, fqn, versionToken);

        var attrs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["httpMethod"] = JsonSerializer.SerializeToElement(method),
            ["routeTemplate"] = JsonSerializer.SerializeToElement(routeTemplate)
        };

        if (!string.IsNullOrEmpty(sourceName))
        {
            attrs["source"] = JsonSerializer.SerializeToElement(sourceName);
        }

        if (!string.IsNullOrEmpty(summary))
        {
            attrs["summary"] = JsonSerializer.SerializeToElement(summary);
        }

        var refs = sourceFile is not null
            ? new[] { new SourceRef(sourceFile, [new SourceSpan(1, 0, 1, 0)]) }
            : Array.Empty<SourceRef>();

        return Node.Create(id, NodeKind.HttpEndpoint, fqn, attrs, refs);
    }

    private static Node MakeOtherNode(string kind = "Project")
    {
        var id = new NodeId(NodeKind.Project, $"proj-{Guid.NewGuid():N}", "v1");
        return Node.Create(id, NodeKind.Project, "SomeProject");
    }

    // -------------------------------------------------------------------------
    // 4.1 — Merge by (method, normalizedPath)
    // -------------------------------------------------------------------------

    [Fact]
    public void Reconcile_SingleEndpoint_ReturnedUnchanged()
    {
        var node = MakeEndpoint("GET", "/api/users", "project-src/app");
        var (nodes, _) = HttpEndpointReconciler.Reconcile([node], []);

        var endpoint = Assert.Single(nodes, static n => n.Kind == NodeKind.HttpEndpoint);
        Assert.Equal(node.Id, endpoint.Id);
    }

    [Fact]
    public void Reconcile_TwoEndpointsWithSameMethodAndNormalizedPath_AreMergedIntoOne()
    {
        // Static analysis yields /api/users/{id} with project versionToken
        var staticNode = MakeEndpoint("GET", "/api/users/{id}", "project-src/app", "static");
        // OpenAPI yields /api/users/{id} with versionToken "openapi"
        var openApiNode = MakeEndpoint("GET", "/api/users/{id}", "openapi", "openapi");

        var (nodes, _) = HttpEndpointReconciler.Reconcile([staticNode, openApiNode], []);

        var endpoints = nodes.Where(static n => n.Kind == NodeKind.HttpEndpoint).ToArray();
        Assert.Single(endpoints);
    }

    [Fact]
    public void Reconcile_ConstrainedAndPlainRouteTemplate_AreRecognizedAsSameEndpoint()
    {
        // Static analysis stores the template after constraint stripping: /api/users/{id}
        // OpenAPI may store the raw template before normalization: /api/users/{id:int}
        // Both should normalize to the same path via RouteNormalizer.
        var staticNode = MakeEndpoint("GET", "/api/users/{id}", "project-src/app", "static");
        var openApiNode = MakeEndpoint("GET", "/api/users/{id:int}", "openapi", "openapi");

        var (nodes, _) = HttpEndpointReconciler.Reconcile([staticNode, openApiNode], []);

        var endpoints = nodes.Where(static n => n.Kind == NodeKind.HttpEndpoint).ToArray();
        Assert.Single(endpoints);
    }

    [Fact]
    public void Reconcile_DifferentMethods_ProduceSeparateNodes()
    {
        var getNode = MakeEndpoint("GET", "/api/users", "project-src/app");
        var postNode = MakeEndpoint("POST", "/api/users", "openapi");

        var (nodes, _) = HttpEndpointReconciler.Reconcile([getNode, postNode], []);

        var endpoints = nodes.Where(static n => n.Kind == NodeKind.HttpEndpoint).ToArray();
        Assert.Equal(2, endpoints.Length);
    }

    [Fact]
    public void Reconcile_DifferentPaths_ProduceSeparateNodes()
    {
        var nodeA = MakeEndpoint("GET", "/api/users", "project-src/app");
        var nodeB = MakeEndpoint("GET", "/api/orders", "openapi");

        var (nodes, _) = HttpEndpointReconciler.Reconcile([nodeA, nodeB], []);

        var endpoints = nodes.Where(static n => n.Kind == NodeKind.HttpEndpoint).ToArray();
        Assert.Equal(2, endpoints.Length);
    }

    // -------------------------------------------------------------------------
    // 4.2 — SourceRefs from both sources appear on merged node
    // -------------------------------------------------------------------------

    [Fact]
    public void Reconcile_MergedNode_ContainsSourceRefsFromBothSources()
    {
        var staticNode = MakeEndpoint("GET", "/api/users", "project-src/app", "static", sourceFile: "Controllers/UsersController.cs");
        var openApiNode = MakeEndpoint("GET", "/api/users", "openapi", "openapi", sourceFile: "openapi.yaml");

        var (nodes, _) = HttpEndpointReconciler.Reconcile([staticNode, openApiNode], []);

        var merged = nodes.Single(static n => n.Kind == NodeKind.HttpEndpoint);
        var files = merged.SourceRefs.Select(static sr => sr.File).ToArray();

        Assert.Contains("Controllers/UsersController.cs", files);
        Assert.Contains("openapi.yaml", files);
    }

    [Fact]
    public void Reconcile_MergedNode_DeduplicatesDuplicateSourceRefs()
    {
        var ref1 = new SourceRef("openapi.yaml", []);
        var ref2 = new SourceRef("openapi.yaml", []);

        var id1 = new NodeId(NodeKind.HttpEndpoint, "GET /api/users", "openapi");
        var id2 = new NodeId(NodeKind.HttpEndpoint, "GET /api/users", "project-src/app");

        var node1 = Node.Create(id1, NodeKind.HttpEndpoint, "GET /api/users",
            new Dictionary<string, JsonElement>
            {
                ["httpMethod"] = JsonSerializer.SerializeToElement("GET"),
                ["routeTemplate"] = JsonSerializer.SerializeToElement("/api/users"),
                ["source"] = JsonSerializer.SerializeToElement("openapi")
            },
            [ref1]);

        var node2 = Node.Create(id2, NodeKind.HttpEndpoint, "GET /api/users",
            new Dictionary<string, JsonElement>
            {
                ["httpMethod"] = JsonSerializer.SerializeToElement("GET"),
                ["routeTemplate"] = JsonSerializer.SerializeToElement("/api/users"),
                ["source"] = JsonSerializer.SerializeToElement("static")
            },
            [ref2]);

        var (nodes, _) = HttpEndpointReconciler.Reconcile([node1, node2], []);

        var merged = nodes.Single(static n => n.Kind == NodeKind.HttpEndpoint);
        // Both refs are the same file with no spans → deduplicated to one
        Assert.Single(merged.SourceRefs);
    }

    // -------------------------------------------------------------------------
    // 4.3 — Non-conflicting attributes are unioned; conflicts are tagged
    // -------------------------------------------------------------------------

    [Fact]
    public void Reconcile_NonConflictingAttributes_StoredUnderPlainKey()
    {
        // Both nodes share httpMethod and routeTemplate; only static has controllerType
        var staticNode = MakeEndpoint("GET", "/api/users", "project-src/app", "static");
        var openApiNode = MakeEndpoint("GET", "/api/users", "openapi", "openapi");

        // Add a unique attribute to staticNode only
        var staticAttrs = new Dictionary<string, JsonElement>(staticNode.Attributes)
        {
            ["controllerType"] = JsonSerializer.SerializeToElement("UsersController")
        };
        var enrichedStatic = Node.Create(staticNode.Id, staticNode.Kind, staticNode.DisplayName, staticAttrs, staticNode.SourceRefs);

        var (nodes, _) = HttpEndpointReconciler.Reconcile([enrichedStatic, openApiNode], []);

        var merged = nodes.Single(static n => n.Kind == NodeKind.HttpEndpoint);

        // controllerType exists only in one source → no conflict → stored as-is
        Assert.True(merged.Attributes.ContainsKey("controllerType"));
        Assert.False(merged.Attributes.ContainsKey("controllerType:static"));
    }

    [Fact]
    public void Reconcile_ConflictingSummary_StoredAsTaggedAttributes()
    {
        // Two nodes for the same endpoint, each with a different summary
        var staticNode = MakeEndpoint("POST", "/api/orders", "project-src/app", "static", summary: "Create order (static)");
        var openApiNode = MakeEndpoint("POST", "/api/orders", "openapi", "openapi", summary: "Create order (openapi)");

        var (nodes, _) = HttpEndpointReconciler.Reconcile([staticNode, openApiNode], []);

        var merged = nodes.Single(static n => n.Kind == NodeKind.HttpEndpoint);

        // Plain key must NOT be present (values disagree)
        Assert.False(merged.Attributes.ContainsKey("summary"),
            "Conflicting 'summary' should not appear under the plain key.");

        // Both source-tagged keys must be present
        Assert.True(merged.Attributes.ContainsKey("summary:static"),
            "Expected 'summary:static' for the static source value.");
        Assert.True(merged.Attributes.ContainsKey("summary:openapi"),
            "Expected 'summary:openapi' for the openapi source value.");

        Assert.Equal("Create order (static)", merged.Attributes["summary:static"].GetString());
        Assert.Equal("Create order (openapi)", merged.Attributes["summary:openapi"].GetString());
    }

    [Fact]
    public void Reconcile_AgreingSummary_StoredUnderPlainKey()
    {
        var staticNode = MakeEndpoint("GET", "/api/health", "project-src/app", "static", summary: "Health check");
        var openApiNode = MakeEndpoint("GET", "/api/health", "openapi", "openapi", summary: "Health check");

        var (nodes, _) = HttpEndpointReconciler.Reconcile([staticNode, openApiNode], []);

        var merged = nodes.Single(static n => n.Kind == NodeKind.HttpEndpoint);

        Assert.True(merged.Attributes.ContainsKey("summary"));
        Assert.Equal("Health check", merged.Attributes["summary"].GetString());

        Assert.False(merged.Attributes.ContainsKey("summary:static"));
        Assert.False(merged.Attributes.ContainsKey("summary:openapi"));
    }

    // -------------------------------------------------------------------------
    // 4.4 — Non-HttpEndpoint nodes pass through unchanged
    // -------------------------------------------------------------------------

    [Fact]
    public void Reconcile_NonEndpointNodes_PassThroughUnchanged()
    {
        var project = MakeOtherNode();
        var endpoint = MakeEndpoint("GET", "/api/users", "project-src/app");

        var (nodes, _) = HttpEndpointReconciler.Reconcile([project, endpoint], []);

        Assert.Contains(nodes, n => n.Id == project.Id);
        Assert.Contains(nodes, static n => n.Kind == NodeKind.HttpEndpoint);
    }

    // -------------------------------------------------------------------------
    // Edge rewriting
    // -------------------------------------------------------------------------

    [Fact]
    public void Reconcile_EdgesPointingAtMergedAwayId_AreRewrittenToCanonicalId()
    {
        var staticNode = MakeEndpoint("DELETE", "/api/users/{id}", "project-src/app", "static");
        var openApiNode = MakeEndpoint("DELETE", "/api/users/{id}", "openapi", "openapi");

        // An edge that points FROM some method TO the static node's id
        var methodId = new NodeId(NodeKind.Method, "MyNamespace.UsersController.Delete(int)", "project-src/app");
        var edge = Edge.Create(methodId, staticNode.Id, EdgeKind.HandlesRoute);

        var (nodes, edges) = HttpEndpointReconciler.Reconcile([staticNode, openApiNode], [edge]);

        // After merge only one endpoint node exists
        var mergedEndpoint = nodes.Single(static n => n.Kind == NodeKind.HttpEndpoint);

        // The edge must now point to the canonical merged node
        var rewrittenEdge = Assert.Single(edges);
        Assert.Equal(mergedEndpoint.Id, rewrittenEdge.ToId);
    }

    [Fact]
    public void Reconcile_DuplicateEdgesAfterRewrite_AreDeduplicatedToOne()
    {
        var staticNode = MakeEndpoint("GET", "/api/items", "project-src/app", "static");
        var openApiNode = MakeEndpoint("GET", "/api/items", "openapi", "openapi");

        var methodId = new NodeId(NodeKind.Method, "MyNs.ItemsController.GetAll()", "project-src/app");

        // Two edges that both point to one of the two pre-merge node ids
        var edge1 = Edge.Create(methodId, staticNode.Id, EdgeKind.HandlesRoute);
        var edge2 = Edge.Create(methodId, openApiNode.Id, EdgeKind.HandlesRoute);

        var (_, edges) = HttpEndpointReconciler.Reconcile([staticNode, openApiNode], [edge1, edge2]);

        // After rewrite both edges have the same (from, to, kind) → deduplicated
        Assert.Single(edges);
    }

    // -------------------------------------------------------------------------
    // Three-way merge
    // -------------------------------------------------------------------------

    [Fact]
    public void Reconcile_ThreeSourcesForSameEndpoint_AllSourceRefsPresent()
    {
        var n1 = MakeEndpoint("PUT", "/api/products/{id}", "project-src/app", "static", sourceFile: "Controllers/ProductsController.cs");
        var n2 = MakeEndpoint("PUT", "/api/products/{id}", "openapi", "openapi", sourceFile: "openapi.yaml");
        var n3 = MakeEndpoint("PUT", "/api/products/{id}", "postman", "postman", sourceFile: "SampleApi.postman_collection.json");

        var (nodes, _) = HttpEndpointReconciler.Reconcile([n1, n2, n3], []);

        var merged = nodes.Single(static n => n.Kind == NodeKind.HttpEndpoint);

        var files = merged.SourceRefs.Select(static sr => sr.File).ToArray();
        Assert.Contains("Controllers/ProductsController.cs", files);
        Assert.Contains("openapi.yaml", files);
        Assert.Contains("SampleApi.postman_collection.json", files);
    }
}
