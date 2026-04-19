using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSharpDllGraph.Engine.Graph.Serialization;
using CSharpDllGraph.Engine.Providers;
using Microsoft.Extensions.Logging;

namespace CSharpDllGraph.Providers.Dotnet.Cache;

public sealed class ParallelFilePackageFragmentCache : IPackageFragmentCache
{
    private static readonly JsonSerializerOptions SerializerOptions = GraphJsonSerializerOptions.Create();
    private static readonly Regex UnsafeFileNameCharacterPattern = new("[^a-zA-Z0-9._-]", RegexOptions.Compiled);

    private readonly string _cacheRootPath;
    private readonly ConcurrentDictionary<string, GraphFragment> _memoryCache = new(StringComparer.Ordinal);

    public ParallelFilePackageFragmentCache(string workspaceRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);
        _cacheRootPath = Path.Combine(Path.GetFullPath(workspaceRootPath), ".csharpdllgraph", "cache", "packages");
    }

    /// <summary>
    /// Pre-loads all cache files for the given packages in parallel into memory.
    /// Call once before the parallel worker loop to maximize throughput.
    /// </summary>
    public async Task PreWarmAsync(
        IReadOnlyList<(string Name, string Version)> packages,
        ILogger logger,
        CancellationToken ct)
    {
        var parallelism = Math.Max(Environment.ProcessorCount, 4);
        var hits = 0;

        await Parallel.ForEachAsync(
            packages,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
            async (package, innerCt) =>
            {
                var key = MakeKey(package.Name, package.Version);
                var path = GetCacheFilePath(package.Name, package.Version);

                try
                {
                    await using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 65536,
                        options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                    var fragment = await JsonSerializer.DeserializeAsync<GraphFragment>(stream, SerializerOptions, innerCt);
                    if (fragment is not null)
                    {
                        _memoryCache[key] = fragment;
                        Interlocked.Increment(ref hits);
                    }
                }
                catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
                {
                    // cache miss — will be built by worker
                }
            });

        logger.LogInformation(
            "ParallelFilePackageFragmentCache: pre-warmed {Hits}/{Total} packages into memory.",
            hits,
            packages.Count);
    }

    public Task<GraphFragment?> TryGetAsync(string packageName, string version, CancellationToken ct)
    {
        var key = MakeKey(packageName, version);
        return Task.FromResult(_memoryCache.TryGetValue(key, out var fragment) ? fragment : null);
    }

    public async Task SetAsync(string packageName, string version, GraphFragment fragment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        var key = MakeKey(packageName, version);
        _memoryCache[key] = fragment;

        var path = GetCacheFilePath(packageName, version);
        var directoryPath = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Cache directory path is required.");
        var temporaryPath = path + ".tmp";

        Directory.CreateDirectory(directoryPath);

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 65536,
                         options: FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, fragment, SerializerOptions, ct);
            await stream.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string MakeKey(string packageName, string version) =>
        $"{packageName}@{version}";

    private string GetCacheFilePath(string packageName, string version)
    {
        var fileName = $"{Sanitize(packageName)}@{Sanitize(version)}.json";
        return Path.Combine(_cacheRootPath, fileName);
    }

    private static string Sanitize(string value) =>
        UnsafeFileNameCharacterPattern.Replace(value, "_");
}
