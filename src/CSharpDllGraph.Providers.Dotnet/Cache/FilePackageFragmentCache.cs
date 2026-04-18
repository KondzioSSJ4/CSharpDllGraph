using System.Text.Json;
using System.Text.RegularExpressions;
using CSharpDllGraph.Engine.Graph.Serialization;
using CSharpDllGraph.Engine.Providers;

namespace CSharpDllGraph.Providers.Dotnet.Cache;

public sealed class FilePackageFragmentCache : IPackageFragmentCache
{
    private static readonly JsonSerializerOptions SerializerOptions = GraphJsonSerializerOptions.Create();
    private static readonly Regex UnsafeFileNameCharacterPattern = new("[^a-zA-Z0-9._-]", RegexOptions.Compiled);

    private readonly string _cacheRootPath;

    public FilePackageFragmentCache(string workspaceRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);
        _cacheRootPath = Path.Combine(Path.GetFullPath(workspaceRootPath), ".csharpdllgraph", "cache", "packages");
    }

    public async Task<GraphFragment?> TryGetAsync(string packageName, string version, CancellationToken ct)
    {
        var path = GetCacheFilePath(packageName, version);

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.Asynchronous);

            return await JsonSerializer.DeserializeAsync<GraphFragment>(stream, SerializerOptions, ct);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public async Task SetAsync(string packageName, string version, GraphFragment fragment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fragment);

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
                         bufferSize: 4096,
                         options: FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, fragment, SerializerOptions, ct);
            await stream.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private string GetCacheFilePath(string packageName, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var fileName = $"{Sanitize(packageName)}@{Sanitize(version)}.json";
        return Path.Combine(_cacheRootPath, fileName);
    }

    private static string Sanitize(string value)
    {
        return UnsafeFileNameCharacterPattern.Replace(value, "_");
    }
}
