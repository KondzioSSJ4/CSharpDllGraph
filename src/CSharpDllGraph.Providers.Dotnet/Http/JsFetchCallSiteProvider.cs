using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Http;
using CSharpDllGraph.Engine.Providers;

namespace CSharpDllGraph.Providers.Dotnet.Http;

public sealed partial class JsFetchCallSiteProvider : IGraphProvider
{
    private static readonly string[] SkippedDirectories =
    [
        "node_modules", "dist", "build", ".next", "out", "coverage"
    ];

    private static readonly string[] ScannedExtensions =
    [
        ".js", ".ts", ".jsx", ".tsx"
    ];

    public string Id => "js-fetch-callsite";

    public async IAsyncEnumerable<GraphFragment> CollectAsync(
        GraphBuildContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var nodes = new List<Node>();

        foreach (var file in EnumerateSourceFiles(context.WorkspaceRootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lines = await File.ReadAllLinesAsync(file, cancellationToken);
            var relativePath = Path.GetRelativePath(context.WorkspaceRootPath, file).Replace('\\', '/');
            var fileHash = ComputeFileHash(relativePath);

            nodes.AddRange(ExtractCallSiteNodes(lines, relativePath, fileHash));
        }

        await Task.Yield();
        yield return new GraphFragment(Id, nodes, []);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        var queue = new Queue<string>();
        queue.Enqueue(rootPath);

        while (queue.Count > 0)
        {
            var directory = queue.Dequeue();
            var directoryName = Path.GetFileName(directory);

            if (IsSkippedDirectory(directoryName) && !string.Equals(directory, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var extension = Path.GetExtension(file);
                if (ScannedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }

            foreach (var subdirectory in Directory.EnumerateDirectories(directory))
            {
                var subdirectoryName = Path.GetFileName(subdirectory);
                if (!IsSkippedDirectory(subdirectoryName))
                {
                    queue.Enqueue(subdirectory);
                }
            }
        }
    }

    private static bool IsSkippedDirectory(string name)
    {
        return SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<Node> ExtractCallSiteNodes(
        string[] lines,
        string relativePath,
        string fileHash)
    {
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var lineNumber = lineIndex + 1;

            foreach (var node in ExtractFetchNodes(line, relativePath, fileHash, lineNumber))
            {
                yield return node;
            }

            foreach (var node in ExtractAxiosNodes(line, relativePath, fileHash, lineNumber))
            {
                yield return node;
            }
        }
    }

    private static IEnumerable<Node> ExtractFetchNodes(
        string line,
        string relativePath,
        string fileHash,
        int lineNumber)
    {
        foreach (Match match in FetchStringLiteralRegex().Matches(line))
        {
            var url = match.Groups["url"].Value;
            var method = ExtractFetchMethod(line);
            yield return BuildCallSiteNode(url, method, "high", relativePath, fileHash, lineNumber);
        }

        foreach (Match match in FetchTemplateLiteralRegex().Matches(line))
        {
            var rawUrl = match.Groups["url"].Value;
            var url = NormalizeTemplateLiteral(rawUrl);
            var method = ExtractFetchMethod(line);
            yield return BuildCallSiteNode(url, method, "medium", relativePath, fileHash, lineNumber);
        }
    }

    private static IEnumerable<Node> ExtractAxiosNodes(
        string line,
        string relativePath,
        string fileHash,
        int lineNumber)
    {
        foreach (Match match in AxiosStringLiteralRegex().Matches(line))
        {
            var verb = match.Groups["verb"].Value.ToUpperInvariant();
            var url = match.Groups["url"].Value;
            yield return BuildCallSiteNode(url, verb, "high", relativePath, fileHash, lineNumber);
        }

        foreach (Match match in AxiosTemplateLiteralRegex().Matches(line))
        {
            var verb = match.Groups["verb"].Value.ToUpperInvariant();
            var rawUrl = match.Groups["url"].Value;
            var url = NormalizeTemplateLiteral(rawUrl);
            yield return BuildCallSiteNode(url, verb, "medium", relativePath, fileHash, lineNumber);
        }
    }

    private static string ExtractFetchMethod(string line)
    {
        var methodMatch = FetchMethodOptionRegex().Match(line);
        return methodMatch.Success
            ? methodMatch.Groups["method"].Value.ToUpperInvariant()
            : "GET";
    }

    private static string NormalizeTemplateLiteral(string raw)
    {
        return TemplateExpressionRegex().Replace(raw, "{var}");
    }

    private static Node BuildCallSiteNode(
        string urlTemplate,
        string httpMethod,
        string confidence,
        string relativePath,
        string fileHash,
        int lineNumber)
    {
        var fqn = $"JS.{httpMethod}.{urlTemplate}.{fileHash}.{lineNumber}";
        var versionToken = fileHash;

        var nodeId = new NodeId(NodeKind.HttpCallSite, fqn, versionToken);

        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["httpMethod"] = JsonSerializer.SerializeToElement(httpMethod),
            ["urlTemplate"] = JsonSerializer.SerializeToElement(urlTemplate),
            ["confidence"] = JsonSerializer.SerializeToElement(confidence),
            ["sourceFile"] = JsonSerializer.SerializeToElement(relativePath),
            ["sourceLine"] = JsonSerializer.SerializeToElement(lineNumber),
            ["scanner"] = JsonSerializer.SerializeToElement("regex")
        };

        var sourceRef = new SourceRef(
            relativePath,
            [new SourceSpan(lineNumber, 0, lineNumber, 0)]);

        return Node.Create(
            nodeId,
            NodeKind.HttpCallSite,
            $"{httpMethod} {urlTemplate}",
            attributes,
            [sourceRef]);
    }

    private static string ComputeFileHash(string relativePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath));
        return Convert.ToHexStringLower(bytes)[..8];
    }

    [GeneratedRegex(@"fetch\(\s*(?<q>['""])(?<url>[^'""]+)\k<q>", RegexOptions.Compiled)]
    private static partial Regex FetchStringLiteralRegex();

    [GeneratedRegex(@"fetch\(\s*`(?<url>[^`]+)`", RegexOptions.Compiled)]
    private static partial Regex FetchTemplateLiteralRegex();

    [GeneratedRegex(@"axios\.(?<verb>get|post|put|delete|patch)\(\s*(?<q>['""])(?<url>[^'""]+)\k<q>", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex AxiosStringLiteralRegex();

    [GeneratedRegex(@"axios\.(?<verb>get|post|put|delete|patch)\(\s*`(?<url>[^`]+)`", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex AxiosTemplateLiteralRegex();

    [GeneratedRegex(@"method\s*:\s*['""](?<method>[A-Za-z]+)['""]", RegexOptions.Compiled)]
    private static partial Regex FetchMethodOptionRegex();

    [GeneratedRegex(@"\$\{[^}]+\}", RegexOptions.Compiled)]
    private static partial Regex TemplateExpressionRegex();
}
