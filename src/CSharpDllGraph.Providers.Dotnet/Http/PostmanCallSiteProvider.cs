using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Providers;

namespace CSharpDllGraph.Providers.Dotnet.Http;

public sealed partial class PostmanCallSiteProvider : IGraphProvider
{
    public string Id => "postman-callsite";

    public async IAsyncEnumerable<GraphFragment> CollectAsync(
        GraphBuildContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var nodes = new List<Node>();

        foreach (var collectionPath in EnumerateCollectionFiles(context.WorkspaceRootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var json = await File.ReadAllTextAsync(collectionPath, cancellationToken);
            nodes.AddRange(ParseCollection(json, collectionPath, context.WorkspaceRootPath));
        }

        await Task.Yield();
        yield return new GraphFragment(
            Id,
            nodes.OrderBy(static n => n.Id.ToString(), StringComparer.Ordinal).ToArray(),
            []);
    }

    private static IEnumerable<string> EnumerateCollectionFiles(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(rootPath, "*.postman_collection.json", SearchOption.AllDirectories))
        {
            yield return file;
        }
    }

    public static IEnumerable<Node> ParseCollection(string json, string collectionPath, string workspaceRootPath)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var collectionName = root.TryGetProperty("info", out var info)
            && info.TryGetProperty("name", out var nameProp)
            ? nameProp.GetString() ?? Path.GetFileNameWithoutExtension(collectionPath)
            : Path.GetFileNameWithoutExtension(collectionPath);

        var variables = ParseVariables(root);

        var relativePath = Path.GetRelativePath(workspaceRootPath, collectionPath).Replace('\\', '/');
        var fileHash = ComputeShortHash(relativePath);

        if (!root.TryGetProperty("item", out var itemArray))
        {
            yield break;
        }

        foreach (var node in WalkItems(itemArray, collectionName, [], variables, relativePath, fileHash))
        {
            yield return node;
        }
    }

    private static IReadOnlyDictionary<string, string> ParseVariables(JsonElement root)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!root.TryGetProperty("variable", out var variableArray))
        {
            return variables;
        }

        foreach (var variable in variableArray.EnumerateArray())
        {
            if (variable.TryGetProperty("key", out var key)
                && variable.TryGetProperty("value", out var value)
                && key.GetString() is { Length: > 0 } keyStr
                && value.GetString() is { } valueStr)
            {
                variables[keyStr] = valueStr;
            }
        }

        return variables;
    }

    private static IEnumerable<Node> WalkItems(
        JsonElement itemArray,
        string collectionName,
        IReadOnlyList<string> folderPath,
        IReadOnlyDictionary<string, string> variables,
        string collectionRelativePath,
        string fileHash)
    {
        foreach (var item in itemArray.EnumerateArray())
        {
            var itemName = item.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? string.Empty
                : string.Empty;

            // Folders have an "item" array; requests have a "request" property
            if (item.TryGetProperty("item", out var nestedItems))
            {
                var childFolderPath = new List<string>(folderPath) { itemName };
                foreach (var node in WalkItems(
                    nestedItems,
                    collectionName,
                    childFolderPath,
                    variables,
                    collectionRelativePath,
                    fileHash))
                {
                    yield return node;
                }
            }
            else if (item.TryGetProperty("request", out var request))
            {
                var node = BuildCallSiteNode(
                    request,
                    itemName,
                    collectionName,
                    folderPath,
                    variables,
                    collectionRelativePath,
                    fileHash);

                if (node is not null)
                {
                    yield return node;
                }
            }
        }
    }

    private static Node? BuildCallSiteNode(
        JsonElement request,
        string requestName,
        string collectionName,
        IReadOnlyList<string> folderPath,
        IReadOnlyDictionary<string, string> variables,
        string collectionRelativePath,
        string fileHash)
    {
        var httpMethod = request.TryGetProperty("method", out var methodProp)
            ? (methodProp.GetString() ?? "GET").ToUpperInvariant()
            : "GET";

        if (!request.TryGetProperty("url", out var urlElement))
        {
            return null;
        }

        var rawUrl = ExtractRawUrl(urlElement);
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var urlTemplate = NormalizeUrl(rawUrl, variables);

        var folderPathStr = folderPath.Count > 0
            ? string.Join("/", folderPath)
            : string.Empty;

        var fqn = $"Postman.{httpMethod}.{urlTemplate}.{fileHash}.{requestName}";

        var nodeId = new NodeId(NodeKind.HttpCallSite, fqn, fileHash);

        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["httpMethod"] = JsonSerializer.SerializeToElement(httpMethod),
            ["urlTemplate"] = JsonSerializer.SerializeToElement(urlTemplate),
            ["confidence"] = JsonSerializer.SerializeToElement("high"),
            ["collectionName"] = JsonSerializer.SerializeToElement(collectionName),
            ["scanner"] = JsonSerializer.SerializeToElement("postman")
        };

        if (!string.IsNullOrEmpty(requestName))
        {
            attributes["requestName"] = JsonSerializer.SerializeToElement(requestName);
        }

        if (!string.IsNullOrEmpty(folderPathStr))
        {
            attributes["folderPath"] = JsonSerializer.SerializeToElement(folderPathStr);
        }

        var sourceRef = new SourceRef(collectionRelativePath, []);

        return Node.Create(
            nodeId,
            NodeKind.HttpCallSite,
            $"{httpMethod} {urlTemplate}",
            attributes,
            [sourceRef]);
    }

    private static string ExtractRawUrl(JsonElement urlElement)
    {
        // String form: "url": "https://..."
        if (urlElement.ValueKind == JsonValueKind.String)
        {
            return urlElement.GetString() ?? string.Empty;
        }

        // Object form: "url": { "raw": "...", "path": [...] }
        if (urlElement.ValueKind == JsonValueKind.Object)
        {
            if (urlElement.TryGetProperty("raw", out var rawProp)
                && rawProp.GetString() is { Length: > 0 } rawUrl)
            {
                return rawUrl;
            }

            // Fallback: reconstruct from path array
            if (urlElement.TryGetProperty("path", out var pathArray)
                && pathArray.ValueKind == JsonValueKind.Array)
            {
                var segments = pathArray.EnumerateArray()
                    .Select(static segment => segment.GetString() ?? string.Empty)
                    .Where(static segment => segment.Length > 0)
                    .ToArray();

                return "/" + string.Join("/", segments);
            }
        }

        return string.Empty;
    }

    private static string NormalizeUrl(string rawUrl, IReadOnlyDictionary<string, string> variables)
    {
        // First resolve any variables so that host-like variables (e.g. {{baseUrl}}) can be
        // stripped together with the scheme+host portion of the URL.
        var resolved = PostmanVariableRegex().Replace(rawUrl, match =>
        {
            var varName = match.Groups["name"].Value;
            // Known variables are substituted so that scheme+host stripping works correctly.
            // Unknown variables are normalized to the single-brace route-template style.
            return variables.TryGetValue(varName, out var value)
                ? value
                : $"{{{varName}}}";
        });

        // Strip scheme+host: "https://localhost:5001/api/users" → "/api/users"
        // After substitution, path-segment variables remain as {varName} route markers.
        return StripSchemeAndHost(resolved);
    }

    private static string StripSchemeAndHost(string rawUrl)
    {
        // Handle protocol-relative or absolute URLs by finding the path component
        var schemeEnd = rawUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            // No scheme — treat as relative path already
            return EnsureLeadingSlash(rawUrl);
        }

        // Find the first slash after the scheme separator
        var hostStart = schemeEnd + 3;
        var pathStart = rawUrl.IndexOf('/', hostStart);
        if (pathStart < 0)
        {
            // URL has no path component (e.g., "https://{{baseUrl}}")
            return "/";
        }

        return rawUrl[pathStart..];
    }

    private static string EnsureLeadingSlash(string path)
    {
        return path.StartsWith('/') ? path : "/" + path;
    }

    private static string ComputeShortHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes)[..8];
    }

    [GeneratedRegex(@"\{\{(?<name>[^}]+)\}\}", RegexOptions.Compiled)]
    private static partial Regex PostmanVariableRegex();
}
