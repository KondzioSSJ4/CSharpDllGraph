using System.Security.Cryptography;

namespace CSharpDllGraph.Engine.Providers;

internal enum WorkspaceInputKind
{
    Source,
    Project,
    Dependency,
    HttpSpec
}

internal static class WorkspaceInputHashes
{
    private static readonly string[] SourceExtensions =
    [
        ".cs", ".js", ".ts", ".jsx", ".tsx"
    ];

    private static readonly string[] OpenApiExtensions =
    [
        ".json", ".yaml", ".yml"
    ];

    private static readonly string[] SkippedDirectories =
    [
        ".git", ".vs", "bin", "obj", "node_modules", "dist", "build", ".next", "out", "coverage"
    ];

    public static IReadOnlyDictionary<string, string> Collect(GraphBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var solutionPath = context.SolutionPath;
        var workspaceRootPath = context.WorkspaceRootPath;

        AddFileHash(hashes, WorkspaceInputKind.Project, workspaceRootPath, solutionPath);

        foreach (var filePath in EnumerateWorkspaceFiles(workspaceRootPath))
        {
            if (TryClassifyWorkspaceFile(filePath, out var kind))
            {
                AddFileHash(hashes, kind, workspaceRootPath, filePath);
            }
        }

        return hashes;
    }

    public static IReadOnlySet<WorkspaceInputKind> GetChangedKinds(
        IReadOnlyDictionary<string, string> previousHashes,
        IReadOnlyDictionary<string, string> currentHashes)
    {
        var changedKinds = new HashSet<WorkspaceInputKind>();

        foreach (var entry in currentHashes)
        {
            if (!previousHashes.TryGetValue(entry.Key, out var previousHash)
                || !string.Equals(previousHash, entry.Value, StringComparison.Ordinal))
            {
                changedKinds.Add(ParseKind(entry.Key));
            }
        }

        foreach (var entry in previousHashes)
        {
            if (!currentHashes.ContainsKey(entry.Key))
            {
                changedKinds.Add(ParseKind(entry.Key));
            }
        }

        return changedKinds;
    }

    public static IReadOnlyDictionary<WorkspaceInputKind, IReadOnlySet<string>> GetChangedFiles(
        IReadOnlyDictionary<string, string> previousHashes,
        IReadOnlyDictionary<string, string> currentHashes,
        string workspaceRootPath)
    {
        ArgumentNullException.ThrowIfNull(previousHashes);
        ArgumentNullException.ThrowIfNull(currentHashes);

        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            throw new ArgumentException("Workspace root path is required.", nameof(workspaceRootPath));
        }

        var changedFiles = new Dictionary<WorkspaceInputKind, HashSet<string>>();

        foreach (var key in currentHashes.Keys.Concat(previousHashes.Keys).Distinct(StringComparer.Ordinal))
        {
            var kind = ParseKind(key);
            var changed = !previousHashes.TryGetValue(key, out var previousHash)
                          || !currentHashes.TryGetValue(key, out var currentHash)
                          || !string.Equals(previousHash, currentHash, StringComparison.Ordinal);

            if (!changed)
            {
                continue;
            }

            var relativePath = ParseRelativePath(key);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            if (!changedFiles.TryGetValue(kind, out var files))
            {
                files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                changedFiles[kind] = files;
            }

            files.Add(Path.GetFullPath(Path.Combine(workspaceRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar))));
        }

        return changedFiles.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlySet<string>)pair.Value,
            EqualityComparer<WorkspaceInputKind>.Default);
    }

    private static IEnumerable<string> EnumerateWorkspaceFiles(string workspaceRootPath)
    {
        if (!Directory.Exists(workspaceRootPath))
        {
            yield break;
        }

        var queue = new Queue<string>();
        queue.Enqueue(workspaceRootPath);

        while (queue.Count > 0)
        {
            var directory = queue.Dequeue();
            var directoryName = Path.GetFileName(directory);

            if (!string.Equals(directory, workspaceRootPath, StringComparison.OrdinalIgnoreCase)
                && SkippedDirectories.Contains(directoryName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                yield return file;
            }

            foreach (var subdirectory in Directory.EnumerateDirectories(directory))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(subdirectory), StringComparer.OrdinalIgnoreCase))
                {
                    queue.Enqueue(subdirectory);
                }
            }
        }

        foreach (var assetsPath in Directory.EnumerateFiles(workspaceRootPath, "project.assets.json", SearchOption.AllDirectories))
        {
            yield return assetsPath;
        }
    }

    private static bool TryClassifyWorkspaceFile(string filePath, out WorkspaceInputKind kind)
    {
        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath);
        var normalizedPath = filePath.Replace('\\', '/');

        if (string.Equals(fileName, "project.assets.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "packages.lock.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "Directory.Packages.props", StringComparison.OrdinalIgnoreCase))
        {
            kind = WorkspaceInputKind.Dependency;
            return true;
        }

        if (string.Equals(fileName, "Directory.Build.props", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "Directory.Build.targets", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "global.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "NuGet.Config", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            kind = WorkspaceInputKind.Project;
            return true;
        }

        if (string.Equals(extension, ".http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".rest", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".postman_collection.json", StringComparison.OrdinalIgnoreCase)
            || IsOpenApiCandidate(filePath, normalizedPath))
        {
            kind = WorkspaceInputKind.HttpSpec;
            return true;
        }

        if (SourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            kind = WorkspaceInputKind.Source;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool IsOpenApiCandidate(string filePath, string normalizedPath)
    {
        if (!OpenApiExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalizedPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return fileName.Contains("openapi", StringComparison.OrdinalIgnoreCase)
               || fileName.Contains("swagger", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddFileHash(
        IDictionary<string, string> hashes,
        WorkspaceInputKind kind,
        string workspaceRootPath,
        string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var relativePath = Path.GetRelativePath(workspaceRootPath, filePath).Replace('\\', '/');
        var key = $"{ToKey(kind)}:{relativePath}";
        hashes[key] = ComputeFileHash(filePath);
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hashBytes);
    }

    private static WorkspaceInputKind ParseKind(string key)
    {
        var separatorIndex = key.IndexOf(':');
        var prefix = separatorIndex >= 0 ? key[..separatorIndex] : key;

        return prefix switch
        {
            "source" => WorkspaceInputKind.Source,
            "project" => WorkspaceInputKind.Project,
            "dependency" => WorkspaceInputKind.Dependency,
            "http-spec" => WorkspaceInputKind.HttpSpec,
            _ => throw new InvalidOperationException($"Unsupported hash key '{key}'.")
        };
    }

    private static string ParseRelativePath(string key)
    {
        var separatorIndex = key.IndexOf(':');
        return separatorIndex >= 0 && separatorIndex < key.Length - 1
            ? key[(separatorIndex + 1)..]
            : string.Empty;
    }

    private static string ToKey(WorkspaceInputKind kind)
    {
        return kind switch
        {
            WorkspaceInputKind.Source => "source",
            WorkspaceInputKind.Project => "project",
            WorkspaceInputKind.Dependency => "dependency",
            WorkspaceInputKind.HttpSpec => "http-spec",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
}
