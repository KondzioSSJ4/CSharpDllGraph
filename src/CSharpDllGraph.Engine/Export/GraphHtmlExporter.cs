using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CSharpDllGraph.Engine.Export;

public static class GraphHtmlExporter
{
    private const string TemplateResourceName = "CSharpDllGraph.Engine.Assets.graph.html";
    private const string GraphDataPlaceholder = "/*GRAPH_DATA_PLACEHOLDER*/";

    public static async Task ExportAsync(string graphPath, string workspaceRoot, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var manifestPath = Path.Combine(graphPath, "manifest.json");
        var nodesPath = Path.Combine(graphPath, "nodes");
        var edgesPath = Path.Combine(graphPath, "edges");

        var manifestJson = await File.ReadAllTextAsync(manifestPath, ct);
        var nodes = await ReadShardElementsAsync(nodesPath, ct);
        var edges = await ReadShardElementsAsync(edgesPath, ct);

        var payload = new JsonObject
        {
            ["manifest"] = JsonNode.Parse(manifestJson),
            ["nodes"] = ToJsonArray(nodes),
            ["edges"] = ToJsonArray(edges)
        };

        var serializedPayload = payload.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        });

        var template = await LoadTemplateAsync(ct);
        var output = template.Replace(GraphDataPlaceholder, serializedPayload, StringComparison.Ordinal);

        var outputDirectory = Path.Combine(workspaceRoot, ".csharpdllgraph");
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, "graph.html");
        await File.WriteAllTextAsync(outputPath, output, ct);
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadShardElementsAsync(string directoryPath, CancellationToken ct)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var shardPaths = Directory
            .EnumerateFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        var elements = new List<JsonElement>();
        foreach (var shardPath in shardPaths)
        {
            var shardJson = await File.ReadAllTextAsync(shardPath, ct);
            using var document = JsonDocument.Parse(shardJson);

            var root = document.RootElement;

            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // shards are wrapped: { "nodes": [...] } or { "edges": [...] }
                var inner = root.EnumerateObject().FirstOrDefault(p =>
                    p.Value.ValueKind == JsonValueKind.Array);
                if (inner.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                array = inner.Value;
            }
            else
            {
                continue;
            }

            foreach (var element in array.EnumerateArray())
            {
                elements.Add(element.Clone());
            }
        }

        return elements;
    }

    private static JsonArray ToJsonArray(IReadOnlyList<JsonElement> elements)
    {
        var array = new JsonArray();
        foreach (var element in elements)
        {
            array.Add(JsonNode.Parse(element.GetRawText()));
        }

        return array;
    }

    private static async Task<string> LoadTemplateAsync(CancellationToken ct)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{TemplateResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
