using System.Threading.Channels;

namespace CSharpDllGraph.Cli;

internal sealed class WorkspaceWatchSession : IDisposable
{
    private static readonly string[] TrackedSourceExtensions =
    [
        ".cs", ".js", ".ts", ".jsx", ".tsx"
    ];

    private static readonly string[] TrackedHttpExtensions =
    [
        ".http", ".rest"
    ];

    private static readonly string[] TrackedOpenApiExtensions =
    [
        ".json", ".yaml", ".yml"
    ];

    private static readonly string[] IgnoredDirectoryNames =
    [
        ".git", ".vs", ".csharpdllgraph", "bin", "obj", "node_modules", "dist", "build", ".next", "out", "coverage"
    ];

    private static readonly string[] TrackedProjectFileNames =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
        "NuGet.Config",
        "packages.lock.json",
        "project.assets.json"
    ];

    private readonly Action<string> _log;
    private readonly Channel<IReadOnlyList<string>> _batches;
    private readonly List<FileSystemWatcher> _watchers;
    private readonly string _solutionPath;
    private readonly string _workspaceRootPath;
    private readonly string _graphPath;
    private readonly int _debounceMs;
    private readonly object _gate = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _debounceCancellationSource;
    private bool _disposed;

    public WorkspaceWatchSession(
        string workspaceRootPath,
        string solutionPath,
        string graphPath,
        int debounceMs,
        Action<string> log)
    {
        _workspaceRootPath = Path.GetFullPath(workspaceRootPath);
        _solutionPath = Path.GetFullPath(solutionPath);
        _graphPath = Path.GetFullPath(graphPath);
        _debounceMs = debounceMs;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _batches = Channel.CreateUnbounded<IReadOnlyList<string>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _watchers = CreateWatchers();
    }

    public async ValueTask<IReadOnlyList<string>> ReadBatchAsync(CancellationToken cancellationToken)
    {
        return await _batches.Reader.ReadAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CancellationTokenSource? debounceCancellationSource;
        lock (_gate)
        {
            debounceCancellationSource = _debounceCancellationSource;
            _debounceCancellationSource = null;
        }

        if (debounceCancellationSource is not null)
        {
            debounceCancellationSource.Cancel();
            debounceCancellationSource.Dispose();
        }

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _batches.Writer.TryComplete();
    }

    private List<FileSystemWatcher> CreateWatchers()
    {
        var watchers = new List<FileSystemWatcher>();

        foreach (var rootPath in EnumerateWatcherRoots())
        {
            var watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName
                               | NotifyFilters.FileName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.CreationTime
            };

            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;

            watchers.Add(watcher);
            _log($"Watching path '{rootPath}'.");
        }

        return watchers;
    }

    private IEnumerable<string> EnumerateWatcherRoots()
    {
        yield return _workspaceRootPath;

        var solutionDirectory = Path.GetDirectoryName(_solutionPath);
        if (!string.IsNullOrWhiteSpace(solutionDirectory)
            && !IsDescendantOf(solutionDirectory, _workspaceRootPath)
            && !string.Equals(solutionDirectory, _workspaceRootPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return solutionDirectory;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        QueuePath(args.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        QueuePath(args.OldFullPath);
        QueuePath(args.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs args)
    {
        _log($"Watcher error: {args.GetException().Message}");
        QueuePath(_solutionPath);
    }

    private void QueuePath(string? path)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!ShouldTrackPath(fullPath))
        {
            return;
        }

        lock (_gate)
        {
            _pendingPaths.Add(fullPath);
            ScheduleFlush_NoLock();
        }
    }

    private void ScheduleFlush_NoLock()
    {
        _debounceCancellationSource?.Cancel();
        _debounceCancellationSource?.Dispose();
        _debounceCancellationSource = new CancellationTokenSource();
        _ = FlushAfterDelayAsync(_debounceCancellationSource);
    }

    private async Task FlushAfterDelayAsync(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await Task.Delay(_debounceMs, cancellationTokenSource.Token);

            IReadOnlyList<string> batch;
            lock (_gate)
            {
                if (!ReferenceEquals(_debounceCancellationSource, cancellationTokenSource) || _pendingPaths.Count == 0)
                {
                    return;
                }

                batch = _pendingPaths
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                _pendingPaths.Clear();
                _debounceCancellationSource = null;
            }

            _batches.Writer.TryWrite(batch);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellationTokenSource.Dispose();
        }
    }

    private bool ShouldTrackPath(string fullPath)
    {
        if (string.Equals(fullPath, _solutionPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IsDescendantOf(fullPath, _workspaceRootPath))
        {
            return false;
        }

        if (IsDescendantOf(fullPath, _graphPath) || HasIgnoredDirectory(fullPath))
        {
            return false;
        }

        var fileName = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(fullPath);
        var normalizedPath = fullPath.Replace('\\', '/');

        if (string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TrackedSourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TrackedHttpExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TrackedProjectFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.EndsWith(".postman_collection.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TrackedOpenApiExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
               && (fileName.Contains("openapi", StringComparison.OrdinalIgnoreCase)
                   || fileName.Contains("swagger", StringComparison.OrdinalIgnoreCase)
                   || normalizedPath.Contains("/openapi/", StringComparison.OrdinalIgnoreCase)
                   || normalizedPath.Contains("/swagger/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasIgnoredDirectory(string fullPath)
    {
        var directoryPath = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        var segments = directoryPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => IgnoredDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsDescendantOf(string path, string parentPath)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = Path.GetFullPath(parentPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = normalizedParent + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               || normalizedPath.StartsWith(normalizedParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
