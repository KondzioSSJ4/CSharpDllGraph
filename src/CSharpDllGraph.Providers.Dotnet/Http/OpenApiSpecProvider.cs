using System.Runtime.CompilerServices;
using System.Text.Json;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Providers;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace CSharpDllGraph.Providers.Dotnet.Http;

public sealed class OpenApiSpecProvider : IGraphProvider
{
    private static readonly HashSet<string> OpenApiExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".yaml", ".yml"
    };

    public string Id => "openapi-spec";

    public async IAsyncEnumerable<GraphFragment> CollectAsync(
        GraphBuildContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var workspaceRoot = context.WorkspaceRootPath;
        var nodes = new Dictionary<NodeId, Node>();
        var edges = new Dictionary<string, Edge>(StringComparer.Ordinal);

        foreach (var specPath in DiscoverSpecPaths(workspaceRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessSpecFileAsync(specPath, workspaceRoot, nodes, edges, cancellationToken);
        }

        await Task.Yield();
        yield return new GraphFragment(
            Id,
            nodes.Values.OrderBy(static n => n.Id.ToString(), StringComparer.Ordinal).ToArray(),
            edges.Values
                .OrderBy(static e => e.FromId.ToString(), StringComparer.Ordinal)
                .ThenBy(static e => e.ToId.ToString(), StringComparer.Ordinal)
                .ThenBy(static e => e.Kind.ToString(), StringComparer.Ordinal)
                .ToArray());
    }

    private static IEnumerable<string> DiscoverSpecPaths(string workspaceRoot)
    {
        return Directory
            .EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories)
            .Where(IsOpenApiCandidate)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsOpenApiCandidate(string path)
    {
        var extension = Path.GetExtension(path);
        if (!OpenApiExtensions.Contains(extension))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(path);
        return fileName.Contains("openapi", StringComparison.OrdinalIgnoreCase)
               || fileName.Contains("swagger", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ProcessSpecFileAsync(
        string specPath,
        string workspaceRoot,
        Dictionary<NodeId, Node> nodes,
        Dictionary<string, Edge> edges,
        CancellationToken cancellationToken)
    {
        OpenApiDocument document;
        try
        {
            await using var stream = File.OpenRead(specPath);
            var reader = new OpenApiStreamReader();
            var readResult = await reader.ReadAsync(stream, cancellationToken);
            document = readResult.OpenApiDocument;

            if (document is null || readResult.OpenApiDiagnostic.Errors.Count > 0)
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        var relativeSpecPath = Path.GetRelativePath(workspaceRoot, specPath).Replace('\\', '/');

        foreach (var (pathTemplate, pathItem) in document.Paths)
        {
            foreach (var (operationType, operation) in pathItem.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var httpMethod = operationType.ToString().ToUpperInvariant();
                var endpointFqn = $"{httpMethod} {pathTemplate}";
                var endpointId = new NodeId(NodeKind.HttpEndpoint, endpointFqn, "openapi");

                var attributes = BuildEndpointAttributes(httpMethod, pathTemplate, operation);
                var endpointNode = Node.Create(
                    endpointId,
                    NodeKind.HttpEndpoint,
                    $"{httpMethod} {pathTemplate}",
                    attributes,
                    [new SourceRef(relativeSpecPath, [])]);

                nodes[endpointId] = endpointNode;

                var operationPath = $"{pathTemplate} {httpMethod}";
                var externalRefFqn = $"{relativeSpecPath}#{operationPath}";
                var externalRefId = new NodeId(NodeKind.ExternalRef, externalRefFqn, "openapi");
                var externalRefNode = Node.Create(
                    externalRefId,
                    NodeKind.ExternalRef,
                    externalRefFqn,
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["specFile"] = JsonSerializer.SerializeToElement(relativeSpecPath),
                        ["operationPath"] = JsonSerializer.SerializeToElement(operationPath)
                    });

                nodes[externalRefId] = externalRefNode;

                var describedByEdge = Edge.Create(endpointId, externalRefId, EdgeKind.DescribedBy);
                edges[EdgeKey(describedByEdge)] = describedByEdge;
            }
        }
    }

    private static Dictionary<string, JsonElement> BuildEndpointAttributes(
        string httpMethod,
        string pathTemplate,
        OpenApiOperation operation)
    {
        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["httpMethod"] = JsonSerializer.SerializeToElement(httpMethod),
            ["routeTemplate"] = JsonSerializer.SerializeToElement(pathTemplate),
            ["source"] = JsonSerializer.SerializeToElement("openapi")
        };

        if (!string.IsNullOrWhiteSpace(operation.OperationId))
        {
            attributes["operationId"] = JsonSerializer.SerializeToElement(operation.OperationId);
        }

        if (operation.Tags is { Count: > 0 })
        {
            var tags = operation.Tags
                .Select(static t => t.Name)
                .Where(static n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(static n => n, StringComparer.Ordinal)
                .ToArray();

            if (tags.Length > 0)
            {
                attributes["tags"] = JsonSerializer.SerializeToElement(tags);
            }
        }

        var schemaRefs = CollectSchemaRefs(operation);
        if (schemaRefs.Count > 0)
        {
            attributes["schemaRefs"] = JsonSerializer.SerializeToElement(schemaRefs);
        }

        if (!string.IsNullOrWhiteSpace(operation.Summary))
        {
            attributes["summary"] = JsonSerializer.SerializeToElement(operation.Summary.Trim());
        }

        return attributes;
    }

    private static IReadOnlyList<string> CollectSchemaRefs(OpenApiOperation operation)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);

        if (operation.RequestBody?.Content is not null)
        {
            foreach (var mediaType in operation.RequestBody.Content.Values)
            {
                CollectSchemaRefNames(mediaType.Schema, refs);
            }
        }

        foreach (var response in operation.Responses.Values)
        {
            if (response.Content is null)
            {
                continue;
            }

            foreach (var mediaType in response.Content.Values)
            {
                CollectSchemaRefNames(mediaType.Schema, refs);
            }
        }

        return refs.OrderBy(static r => r, StringComparer.Ordinal).ToArray();
    }

    private static void CollectSchemaRefNames(OpenApiSchema? schema, HashSet<string> refs)
    {
        if (schema is null)
        {
            return;
        }

        if (schema.Reference is not null)
        {
            refs.Add(schema.Reference.Id);
            return;
        }

        if (schema.Items is not null)
        {
            CollectSchemaRefNames(schema.Items, refs);
        }

        foreach (var property in schema.Properties.Values)
        {
            CollectSchemaRefNames(property, refs);
        }
    }

    private static string EdgeKey(Edge edge)
    {
        return $"{edge.FromId}|{edge.ToId}|{edge.Kind}";
    }
}
