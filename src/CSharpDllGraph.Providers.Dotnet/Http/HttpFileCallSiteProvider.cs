using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Providers;

namespace CSharpDllGraph.Providers.Dotnet.Http;

public sealed partial class HttpFileCallSiteProvider : IGraphProvider
{
    private static readonly string[] SkippedDirectories =
    [
        "node_modules", "dist", "build", ".next", "out", "coverage", "obj", "bin"
    ];

    public string Id => "http-file-callsite";

    public async IAsyncEnumerable<GraphFragment> CollectAsync(
        GraphBuildContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var nodes = new List<Node>();

        foreach (var file in EnumerateHttpFiles(context.WorkspaceRootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(file, cancellationToken);
            var relativePath = Path.GetRelativePath(context.WorkspaceRootPath, file).Replace('\\', '/');
            var fileHash = ComputeFileHash(relativePath);

            nodes.AddRange(ExtractCallSiteNodes(content, relativePath, fileHash));
        }

        await Task.Yield();
        yield return new GraphFragment(Id, nodes, []);
    }

    private static IEnumerable<string> EnumerateHttpFiles(string rootPath)
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
                if (string.Equals(extension, ".http", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".rest", StringComparison.OrdinalIgnoreCase))
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
        string content,
        string relativePath,
        string fileHash)
    {
        // Split into blocks separated by lines starting with ###
        var blocks = SplitIntoBlocks(content);

        var blockIndex = 0;
        foreach (var (separatorLine, blockLines, startLineNumber) in blocks)
        {
            var requestName = ExtractRequestName(separatorLine, blockLines);
            var requestLine = FindRequestLine(blockLines);

            if (requestLine is null)
            {
                blockIndex++;
                continue;
            }

            var (httpMethod, urlTemplate) = ParseRequestLine(requestLine.Value.line);
            if (string.IsNullOrEmpty(httpMethod) || string.IsNullOrEmpty(urlTemplate))
            {
                blockIndex++;
                continue;
            }

            var normalizedUrl = NormalizeVariables(urlTemplate);
            var headers = ExtractHeaders(blockLines);

            var lineNumber = startLineNumber + requestLine.Value.relativeLineIndex;
            var fqn = $"HTTP.{httpMethod}.{normalizedUrl}.{fileHash}.{blockIndex}";

            var nodeId = new NodeId(NodeKind.HttpCallSite, fqn, fileHash);

            var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["httpMethod"] = JsonSerializer.SerializeToElement(httpMethod),
                ["urlTemplate"] = JsonSerializer.SerializeToElement(normalizedUrl),
                ["confidence"] = JsonSerializer.SerializeToElement("high"),
                ["sourceFile"] = JsonSerializer.SerializeToElement(relativePath),
                ["sourceLine"] = JsonSerializer.SerializeToElement(lineNumber),
                ["scanner"] = JsonSerializer.SerializeToElement("http-file")
            };

            if (!string.IsNullOrEmpty(requestName))
            {
                attributes["requestName"] = JsonSerializer.SerializeToElement(requestName);
            }

            if (headers.Count > 0)
            {
                attributes["headers"] = JsonSerializer.SerializeToElement(headers);
            }

            var sourceRef = new SourceRef(
                relativePath,
                [new SourceSpan(lineNumber, 0, lineNumber, 0)]);

            var displayName = string.IsNullOrEmpty(requestName)
                ? $"{httpMethod} {normalizedUrl}"
                : $"{httpMethod} {normalizedUrl} ({requestName})";

            yield return Node.Create(
                nodeId,
                NodeKind.HttpCallSite,
                displayName,
                attributes,
                [sourceRef]);

            blockIndex++;
        }
    }

    /// <summary>
    /// Splits file content into request blocks.
    /// Each block is separated by a line starting with "###".
    /// Returns tuples of (separatorLine, blockLines, 1-based start line number of the block content).
    /// </summary>
    private static IEnumerable<(string separatorLine, string[] blockLines, int startLineNumber)> SplitIntoBlocks(string content)
    {
        var allLines = content.Split('\n');
        var currentSeparator = string.Empty;
        var currentBlock = new List<string>();
        var currentStartLine = 1;
        var lineNumber = 0;

        foreach (var rawLine in allLines)
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("###", StringComparison.Ordinal))
            {
                if (currentBlock.Count > 0 || lineNumber > 1)
                {
                    yield return (currentSeparator, currentBlock.ToArray(), currentStartLine);
                }

                currentSeparator = line;
                currentBlock = [];
                currentStartLine = lineNumber + 1;
            }
            else
            {
                currentBlock.Add(line);
            }
        }

        // Yield the last block
        if (currentBlock.Count > 0)
        {
            yield return (currentSeparator, currentBlock.ToArray(), currentStartLine);
        }
    }

    /// <summary>
    /// Extracts the request name from the separator line (### RequestName)
    /// or from an inline comment # @name requestName within the block.
    /// </summary>
    private static string? ExtractRequestName(string separatorLine, string[] blockLines)
    {
        // Check separator line: ### RequestName
        if (separatorLine.StartsWith("###", StringComparison.Ordinal))
        {
            var afterSeparator = separatorLine[3..].Trim();
            if (!string.IsNullOrEmpty(afterSeparator))
            {
                return afterSeparator;
            }
        }

        // Check for # @name requestName comment in block
        foreach (var line in blockLines)
        {
            var trimmed = line.Trim();
            var nameMatch = NameCommentRegex().Match(trimmed);
            if (nameMatch.Success)
            {
                return nameMatch.Groups["name"].Value.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first non-comment, non-empty line that looks like an HTTP request line.
    /// Returns (line, relativeLineIndex) where relativeLineIndex is 0-based within blockLines.
    /// </summary>
    private static (string line, int relativeLineIndex)? FindRequestLine(string[] blockLines)
    {
        for (var i = 0; i < blockLines.Length; i++)
        {
            var line = blockLines[i].Trim();

            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            if (line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            // Must start with an HTTP method keyword
            if (HttpMethodRegex().IsMatch(line))
            {
                return (line, i);
            }

            // If we encounter a non-comment non-empty line that isn't a request line, stop looking
            break;
        }

        return null;
    }

    /// <summary>
    /// Parses "METHOD URL" or "METHOD URL HTTP/version" into (method, url).
    /// </summary>
    private static (string httpMethod, string urlTemplate) ParseRequestLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return (string.Empty, string.Empty);
        }

        var method = parts[0].ToUpperInvariant();
        // URL is the second token; strip trailing HTTP version if present (e.g. HTTP/1.1)
        var url = parts[1];

        return (method, url);
    }

    /// <summary>
    /// Normalizes {{variable}} placeholders used in REST Client files to {var} markers.
    /// </summary>
    private static string NormalizeVariables(string url)
    {
        return VariablePlaceholderRegex().Replace(url, static match =>
        {
            var varName = match.Groups["name"].Value;
            return $"{{{varName}}}";
        });
    }

    /// <summary>
    /// Extracts headers from block lines (lines of the form "HeaderName: value"
    /// that appear after the request line and before an empty line).
    /// </summary>
    private static IReadOnlyDictionary<string, string> ExtractHeaders(string[] blockLines)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pastRequestLine = false;
        var inHeaders = false;

        foreach (var rawLine in blockLines)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrEmpty(line))
            {
                if (inHeaders)
                {
                    // Blank line ends the header section
                    break;
                }

                continue;
            }

            if (line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (!pastRequestLine)
            {
                if (HttpMethodRegex().IsMatch(line))
                {
                    pastRequestLine = true;
                    inHeaders = true;
                }

                continue;
            }

            if (inHeaders)
            {
                var colonIndex = line.IndexOf(':', StringComparison.Ordinal);
                if (colonIndex > 0)
                {
                    var headerName = line[..colonIndex].Trim();
                    var headerValue = line[(colonIndex + 1)..].Trim();
                    headers[headerName] = headerValue;
                }
                else
                {
                    // Not a header line — end of headers
                    break;
                }
            }
        }

        return headers;
    }

    private static string ComputeFileHash(string relativePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath));
        return Convert.ToHexStringLower(bytes)[..8];
    }

    [GeneratedRegex(@"^#\s*@name\s+(?<name>\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex NameCommentRegex();

    [GeneratedRegex(@"^(?:GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS|CONNECT|TRACE)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HttpMethodRegex();

    [GeneratedRegex(@"\{\{(?<name>[^}]+)\}\}", RegexOptions.Compiled)]
    private static partial Regex VariablePlaceholderRegex();
}
