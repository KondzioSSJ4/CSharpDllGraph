using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Providers;
using CSharpDllGraph.Engine.Store;
using CSharpDllGraph.Providers.Dotnet.Http;

namespace CSharpDllGraph.Providers.Dotnet.Tests;

/// <summary>
/// Phase 05 — Task 5 &amp; 6: Integration tests proving spec ingestion via
/// OpenApiSpecProvider, HttpFileCallSiteProvider, and PostmanCallSiteProvider,
/// plus reconciliation provenance and disagreement visibility.
/// </summary>
public sealed class SpecIngestionTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string GetSampleApiFixtureRoot()
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "SampleApi"));
    }

    private static async Task<IReadOnlyList<Node>> CollectAllNodesAsync(IGraphProvider provider, string workspaceRoot)
    {
        var context = GraphBuildContext.Create(
            Path.Combine(workspaceRoot, "dummy.slnx"),
            workspaceRoot);

        var nodes = new List<Node>();
        await foreach (var fragment in provider.CollectAsync(context))
        {
            nodes.AddRange(fragment.Nodes);
        }

        return nodes;
    }

    private static async Task<IReadOnlyList<Edge>> CollectAllEdgesAsync(IGraphProvider provider, string workspaceRoot)
    {
        var context = GraphBuildContext.Create(
            Path.Combine(workspaceRoot, "dummy.slnx"),
            workspaceRoot);

        var edges = new List<Edge>();
        await foreach (var fragment in provider.CollectAsync(context))
        {
            edges.AddRange(fragment.Edges);
        }

        return edges;
    }

    // -------------------------------------------------------------------------
    // Task 5.2 — OpenAPI ingestion
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OpenApiSpecProvider_LoadsFixture_EmitsHttpEndpointNodesWithSourceOpenapi()
    {
        var fixtureRoot = GetSampleApiFixtureRoot();
        Assert.True(File.Exists(Path.Combine(fixtureRoot, "openapi.json")),
            "openapi.json fixture must exist in SampleApi.");

        var provider = new OpenApiSpecProvider();
        var nodes = await CollectAllNodesAsync(provider, fixtureRoot);

        var endpoints = nodes.Where(static n => n.Kind == NodeKind.HttpEndpoint).ToList();
        Assert.True(endpoints.Count >= 2,
            $"Expected at least 2 HttpEndpoint nodes from openapi.json, got {endpoints.Count}.");

        Assert.All(endpoints, node =>
        {
            Assert.True(
                node.Attributes.TryGetValue("source", out var src) && src.GetString() == "openapi",
                $"Endpoint {node.Id} must have source=openapi.");
        });
    }

    [Fact]
    public async Task OpenApiSpecProvider_LoadsFixture_EmitsOperationIdAttribute()
    {
        var fixtureRoot = GetSampleApiFixtureRoot();
        var provider = new OpenApiSpecProvider();
        var nodes = await CollectAllNodesAsync(provider, fixtureRoot);

        var endpoints = nodes.Where(static n => n.Kind == NodeKind.HttpEndpoint).ToList();

        // The fixture defines operationId on every operation; at least one must be visible
        Assert.Contains(endpoints, node =>
            node.Attributes.TryGetValue("operationId", out var op)
            && !string.IsNullOrEmpty(op.GetString()));
    }

    [Fact]
    public async Task OpenApiSpecProvider_LoadsFixture_EmitsDescribedByEdges()
    {
        var fixtureRoot = GetSampleApiFixtureRoot();
        var provider = new OpenApiSpecProvider();
        var edges = await CollectAllEdgesAsync(provider, fixtureRoot);

        var describedByEdges = edges.Where(static e => e.Kind == EdgeKind.DescribedBy).ToList();
        Assert.True(describedByEdges.Count >= 2,
            $"Expected at least 2 DescribedBy edges, got {describedByEdges.Count}.");

        // Every DescribedBy edge must go FROM an HttpEndpoint TO an ExternalRef
        foreach (var edge in describedByEdges)
        {
            Assert.Equal(NodeKind.HttpEndpoint, edge.FromId.Kind);
            Assert.Equal(NodeKind.ExternalRef, edge.ToId.Kind);
        }
    }

    [Fact]
    public async Task OpenApiSpecProvider_LoadsFixture_GetAllUsersEndpointPresent()
    {
        var fixtureRoot = GetSampleApiFixtureRoot();
        var provider = new OpenApiSpecProvider();
        var nodes = await CollectAllNodesAsync(provider, fixtureRoot);

        Assert.Contains(nodes, node =>
            node.Kind == NodeKind.HttpEndpoint
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "GET"
            && node.Attributes.TryGetValue("routeTemplate", out var r) && r.GetString() == "/api/users"
            && node.Attributes.TryGetValue("operationId", out var op) && op.GetString() == "GetAllUsers");
    }

    // -------------------------------------------------------------------------
    // Task 5.2 — .http file ingestion
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HttpFileCallSiteProvider_LoadsFixture_EmitsHttpCallSiteNodes()
    {
        var fixtureRoot = GetSampleApiFixtureRoot();
        Assert.True(File.Exists(Path.Combine(fixtureRoot, "requests.http")),
            "requests.http fixture must exist in SampleApi.");

        var provider = new HttpFileCallSiteProvider();
        var nodes = await CollectAllNodesAsync(provider, fixtureRoot);

        var callSites = nodes.Where(static n => n.Kind == NodeKind.HttpCallSite).ToList();
        Assert.True(callSites.Count >= 2,
            $"Expected at least 2 HttpCallSite nodes from requests.http, got {callSites.Count}.");

        Assert.All(callSites, static node => Assert.Equal(NodeKind.HttpCallSite, node.Kind));
    }

    [Fact]
    public async Task HttpFileCallSiteProvider_LoadsFixture_CorrectMethodAndPath()
    {
        var fixtureRoot = GetSampleApiFixtureRoot();
        var provider = new HttpFileCallSiteProvider();
        var nodes = await CollectAllNodesAsync(provider, fixtureRoot);

        // GET .../api/users (first block) — urlTemplate may include scheme+host
        Assert.Contains(nodes, node =>
            node.Kind == NodeKind.HttpCallSite
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "GET"
            && node.Attributes.TryGetValue("urlTemplate", out var u)
            && u.GetString() is { } url && url.EndsWith("/api/users", StringComparison.Ordinal));

        // POST .../api/users (second block)
        Assert.Contains(nodes, node =>
            node.Kind == NodeKind.HttpCallSite
            && node.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "POST"
            && node.Attributes.TryGetValue("urlTemplate", out var u)
            && u.GetString() is { } url && url.EndsWith("/api/users", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HttpFileCallSiteProvider_LoadsFixture_RequestNamePresentWhenAnnotated()
    {
        var fixtureRoot = GetSampleApiFixtureRoot();
        var provider = new HttpFileCallSiteProvider();
        var nodes = await CollectAllNodesAsync(provider, fixtureRoot);

        // The first block has "# @name listUsers"
        Assert.Contains(nodes, node =>
            node.Kind == NodeKind.HttpCallSite
            && node.Attributes.TryGetValue("requestName", out var rn)
            && rn.GetString() == "listUsers");
    }

    // -------------------------------------------------------------------------
    // Task 5.2 — Postman ingestion (fixture already exists from Task 3)
    // -------------------------------------------------------------------------

    [Fact]
    public void PostmanCallSiteProvider_SampleApiFixture_EmitsHttpCallSiteNodes()
    {
        var fixtureRoot = GetSampleApiFixtureRoot();
        var collectionPath = Path.Combine(fixtureRoot, "SampleApi.postman_collection.json");
        Assert.True(File.Exists(collectionPath), $"Postman fixture not found: {collectionPath}");

        var json = File.ReadAllText(collectionPath);
        var nodes = PostmanCallSiteProvider.ParseCollection(json, collectionPath, fixtureRoot).ToList();

        Assert.True(nodes.Count >= 3,
            $"Expected at least 3 HttpCallSite nodes from Postman fixture, got {nodes.Count}.");
        Assert.All(nodes, static node => Assert.Equal(NodeKind.HttpCallSite, node.Kind));
    }

    // -------------------------------------------------------------------------
    // Task 5.3 — Merged endpoints keep full provenance (SourceRefs from both)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Reconciler_StaticAndOpenApi_MergedNodeHasSourceRefsFromBothSources()
    {
        var fixtureRoot = GetSampleApiFixtureRoot();
        var solutionPath = Path.Combine(fixtureRoot, "SampleApi.slnx");
        var workspacePath = Path.Combine(Path.GetTempPath(), $"csharpdllgraph-phase05-provenance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspacePath);

        // Build static analysis snapshot (ControllerEndpointProvider + MinimalApiEndpointProvider)
        var staticPipeline = new GraphBuildPipeline([
            new ControllerEndpointProvider(),
            new MinimalApiEndpointProvider()
        ]);
        var store = new JsonWorkspaceStore(workspacePath);
        await staticPipeline.BuildAndPersistAsync(
            GraphBuildContext.Create(solutionPath, workspacePath),
            store);
        var staticSnapshot = await store.LoadAsync();

        // Collect OpenAPI nodes and edges from the fixture
        var openApiProvider = new OpenApiSpecProvider();
        var openApiContext = GraphBuildContext.Create(solutionPath, fixtureRoot);
        var openApiNodes = new List<Node>();
        var openApiEdges = new List<Edge>();
        await foreach (var fragment in openApiProvider.CollectAsync(openApiContext))
        {
            openApiNodes.AddRange(fragment.Nodes);
            openApiEdges.AddRange(fragment.Edges);
        }

        // Merge everything through the reconciler
        var allNodes = staticSnapshot.Nodes.Concat(openApiNodes).ToList();
        var allEdges = staticSnapshot.Edges.Concat(openApiEdges).ToList();
        var (reconciledNodes, _) = HttpEndpointReconciler.Reconcile(allNodes, allEdges);

        // GET /api/users exists in both static (UsersController) and openapi.json
        var mergedGetUsers = reconciledNodes.FirstOrDefault(n =>
            n.Kind == NodeKind.HttpEndpoint
            && n.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "GET"
            && n.Attributes.TryGetValue("routeTemplate", out var r) && r.GetString() == "/api/users");

        Assert.NotNull(mergedGetUsers);

        // SourceRefs must come from both the static provider (controller file) and the openapi spec
        var sourceFiles = mergedGetUsers.SourceRefs.Select(static sr => sr.File).ToArray();
        Assert.True(sourceFiles.Length >= 2,
            $"Merged GET /api/users should have SourceRefs from at least 2 sources, got {sourceFiles.Length}: [{string.Join(", ", sourceFiles)}]");

        Assert.Contains(sourceFiles, f => f.Contains("openapi", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // Task 5.4 — Disagreement cases remain visible (both tagged attributes kept)
    // -------------------------------------------------------------------------

    [Fact]
    public void Reconciler_DisagreementOnSummary_BothTaggedAttributesPresent()
    {
        // We synthesise two nodes with differing summaries to force a disagreement.
        // The openapi.json fixture has GET /api/health summary = "Health check — openapi flavour"
        // which differs from the static value "Health check" we inject here.
        var staticNode = CreateHttpEndpointNode(
            "GET", "/api/health", "project-src/SampleApi", "static", "Health check");

        var openApiNode = CreateHttpEndpointNode(
            "GET", "/api/health", "openapi", "openapi", "Health check — openapi flavour");

        var (reconciledNodes, _) = HttpEndpointReconciler.Reconcile([staticNode, openApiNode], []);

        var merged = reconciledNodes.Single(static n =>
            n.Kind == NodeKind.HttpEndpoint
            && n.Attributes.TryGetValue("routeTemplate", out var r) && r.GetString() == "/api/health");

        // Summaries disagree → plain key must be absent; tagged keys must be present
        Assert.False(merged.Attributes.ContainsKey("summary"),
            "Conflicting summary must not appear under the plain 'summary' key.");

        Assert.True(merged.Attributes.ContainsKey("summary:static"),
            "Expected 'summary:static' for the static value.");
        Assert.True(merged.Attributes.ContainsKey("summary:openapi"),
            "Expected 'summary:openapi' for the openapi value.");

        Assert.Equal("Health check", merged.Attributes["summary:static"].GetString());
        Assert.Equal("Health check — openapi flavour", merged.Attributes["summary:openapi"].GetString());
    }

    [Fact]
    public void Reconciler_AgreementOnSummary_PlainKeyRetained()
    {
        // When both sources agree on summary the plain key survives reconciliation.
        var staticNode = CreateHttpEndpointNode(
            "POST", "/api/users", "project-src/SampleApi", "static", "Create a new user");
        var openApiNode = CreateHttpEndpointNode(
            "POST", "/api/users", "openapi", "openapi", "Create a new user");

        var (reconciledNodes, _) = HttpEndpointReconciler.Reconcile([staticNode, openApiNode], []);

        var merged = reconciledNodes.Single(static n =>
            n.Kind == NodeKind.HttpEndpoint
            && n.Attributes.TryGetValue("routeTemplate", out var r) && r.GetString() == "/api/users"
            && n.Attributes.TryGetValue("httpMethod", out var m) && m.GetString() == "POST");

        Assert.True(merged.Attributes.ContainsKey("summary"),
            "Agreeing summary must be kept under the plain 'summary' key.");
        Assert.Equal("Create a new user", merged.Attributes["summary"].GetString());
        Assert.False(merged.Attributes.ContainsKey("summary:static"));
        Assert.False(merged.Attributes.ContainsKey("summary:openapi"));
    }

    // -------------------------------------------------------------------------
    // Private factory
    // -------------------------------------------------------------------------

    private static Node CreateHttpEndpointNode(
        string method,
        string routeTemplate,
        string versionToken,
        string sourceName,
        string? summary = null)
    {
        var fqn = $"{method} {routeTemplate}";
        var id = new NodeId(NodeKind.HttpEndpoint, fqn, versionToken);

        var attrs = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>(
            System.StringComparer.Ordinal)
        {
            ["httpMethod"] = System.Text.Json.JsonSerializer.SerializeToElement(method),
            ["routeTemplate"] = System.Text.Json.JsonSerializer.SerializeToElement(routeTemplate),
            ["source"] = System.Text.Json.JsonSerializer.SerializeToElement(sourceName)
        };

        if (!string.IsNullOrEmpty(summary))
        {
            attrs["summary"] = System.Text.Json.JsonSerializer.SerializeToElement(summary);
        }

        return Node.Create(id, NodeKind.HttpEndpoint, fqn, attrs, []);
    }
}
