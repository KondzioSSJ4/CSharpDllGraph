using System.Text;
using System.Text.Json;
using System.Threading;

namespace CSharpDllGraph.Engine.Registry;

public sealed class WorkspaceRegistry : IWorkspaceRegistry
{
    private const string RegistryFileName = "workspaces.json";
    private const string RegistryVersion = "1";

    private readonly JsonSerializerOptions _serializerOptions;
    private WorkspaceRegistryState _state;

    public WorkspaceRegistry(string? registryFilePath = null, JsonSerializerOptions? serializerOptions = null)
    {
        _serializerOptions = serializerOptions ?? CreateSerializerOptions();
        RegistryFilePath = registryFilePath is null
            ? GetDefaultRegistryFilePath()
            : Path.GetFullPath(registryFilePath);
        _state = LoadStateFromDisk();
    }

    public string RegistryFilePath { get; }

    public IReadOnlyList<WorkspaceRegistration> List()
    {
        return Volatile.Read(ref _state).Entries;
    }

    public async Task<WorkspaceRegistration> AddAsync(
        WorkspaceRegistration registration,
        CancellationToken cancellationToken = default)
    {
        var normalizedRegistration = NormalizeRegistration(registration);

        await using var writeLock = await AcquireWriteLockAsync(cancellationToken);
        var state = await LoadStateFromDiskAsync(cancellationToken);

        var entriesByName = state.Entries.ToDictionary(static entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        entriesByName[normalizedRegistration.Name] = normalizedRegistration;

        var updatedState = CreateState(entriesByName.Values);
        await PersistStateAsync(updatedState, cancellationToken);
        PublishState(updatedState);
        return normalizedRegistration;
    }

    public async Task<bool> RemoveAsync(
        string workspaceName,
        CancellationToken cancellationToken = default)
    {
        var normalizedWorkspaceName = NormalizeWorkspaceName(workspaceName);

        await using var writeLock = await AcquireWriteLockAsync(cancellationToken);
        var state = await LoadStateFromDiskAsync(cancellationToken);
        var entriesByName = state.Entries.ToDictionary(static entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        if (!entriesByName.Remove(normalizedWorkspaceName))
        {
            return false;
        }

        var updatedState = CreateState(entriesByName.Values);
        await PersistStateAsync(updatedState, cancellationToken);
        PublishState(updatedState);
        return true;
    }

    public WorkspaceRegistration Resolve(string workspaceName)
    {
        if (!TryResolve(workspaceName, out var registration))
        {
            throw new KeyNotFoundException($"Workspace '{workspaceName}' is not registered.");
        }

        return registration;
    }

    public bool TryResolve(string workspaceName, out WorkspaceRegistration registration)
    {
        var normalizedWorkspaceName = NormalizeWorkspaceName(workspaceName);
        return Volatile.Read(ref _state).EntriesByName.TryGetValue(normalizedWorkspaceName, out registration!);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
    }

    private static string GetDefaultRegistryFilePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appDataPath))
            {
                throw new InvalidOperationException("APPDATA path is not available.");
            }

            return Path.Combine(appDataPath, "CSharpDllGraph", RegistryFileName);
        }

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                throw new InvalidOperationException("User profile path is not available.");
            }

            configHome = Path.Combine(userProfile, ".config");
        }

        return Path.Combine(configHome, "csharpdllgraph", RegistryFileName);
    }

    private static WorkspaceRegistryState CreateState(IEnumerable<WorkspaceRegistration> entries)
    {
        var orderedEntries = entries
            .Select(NormalizeRegistration)
            .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Name, StringComparer.Ordinal)
            .ToArray();

        var entriesByName = orderedEntries.ToDictionary(static entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        return new WorkspaceRegistryState(orderedEntries, entriesByName);
    }

    private static WorkspaceRegistration NormalizeRegistration(WorkspaceRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return new WorkspaceRegistration(
            NormalizeWorkspaceName(registration.Name),
            NormalizePath(registration.RootPath, nameof(registration.RootPath)),
            NormalizePath(registration.GraphPath, nameof(registration.GraphPath)),
            registration.LastBuiltUtc?.ToUniversalTime());
    }

    private static string NormalizeWorkspaceName(string workspaceName)
    {
        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            throw new ArgumentException("Workspace name is required.", nameof(workspaceName));
        }

        return workspaceName.Trim();
    }

    private static string NormalizePath(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", paramName);
        }

        return Path.GetFullPath(path.Trim());
    }

    private async Task<FileStream> AcquireWriteLockAsync(CancellationToken cancellationToken)
    {
        var lockFilePath = RegistryFilePath + ".lock";
        var directoryPath = Path.GetDirectoryName(lockFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }
    }

    private WorkspaceRegistryState LoadStateFromDisk()
    {
        var document = ReadDocument();
        return CreateState(document.Workspaces);
    }

    private async Task<WorkspaceRegistryState> LoadStateFromDiskAsync(CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(cancellationToken);
        return CreateState(document.Workspaces);
    }

    private WorkspaceRegistryDocument ReadDocument()
    {
        if (!File.Exists(RegistryFilePath))
        {
            return WorkspaceRegistryDocument.Empty;
        }

        var json = File.ReadAllText(RegistryFilePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<WorkspaceRegistryDocument>(json, _serializerOptions)
               ?? WorkspaceRegistryDocument.Empty;
    }

    private async Task<WorkspaceRegistryDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(RegistryFilePath))
        {
            return WorkspaceRegistryDocument.Empty;
        }

        await using var stream = new FileStream(
            RegistryFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        return await JsonSerializer.DeserializeAsync<WorkspaceRegistryDocument>(stream, _serializerOptions, cancellationToken)
               ?? WorkspaceRegistryDocument.Empty;
    }

    private async Task PersistStateAsync(WorkspaceRegistryState state, CancellationToken cancellationToken)
    {
        var document = new WorkspaceRegistryDocument(RegistryVersion, state.Entries);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, _serializerOptions);
        var directoryPath = Path.GetDirectoryName(RegistryFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var temporaryPath = RegistryFilePath + ".tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         options: FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, RegistryFilePath, overwrite: true);
    }

    private void PublishState(WorkspaceRegistryState state)
    {
        Interlocked.Exchange(ref _state, state);
    }

    private sealed record WorkspaceRegistryDocument(string Version, IReadOnlyList<WorkspaceRegistration> Workspaces)
    {
        public static WorkspaceRegistryDocument Empty { get; } = new(RegistryVersion, []);
    }

    private sealed record WorkspaceRegistryState(
        WorkspaceRegistration[] Entries,
        IReadOnlyDictionary<string, WorkspaceRegistration> EntriesByName);
}
