using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CSharpDllGraph.Mcp.Logging;

internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly string _filePath;
    private readonly LogLevel _minimumLevel;
    private readonly object _writeLock = new();
    private bool _disposed;

    public FileLoggerProvider(string filePath, LogLevel minimumLevel = LogLevel.Information)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Log file path is required.", nameof(filePath));
        }

        _filePath = ResolveLogFilePath(filePath);
        _minimumLevel = minimumLevel;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        ThrowIfDisposed();
        return _loggers.GetOrAdd(categoryName, static (name, provider) => new FileLogger(name, provider), this);
    }

    public void Dispose()
    {
        _disposed = true;
        _loggers.Clear();
    }

    public static string ResolveLogFilePath(string filePath)
    {
        if (Path.IsPathRooted(filePath))
        {
            return filePath;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, filePath));
    }

    internal bool IsEnabled(LogLevel logLevel)
    {
        return !_disposed && logLevel >= _minimumLevel;
    }

    internal void WriteLine(string categoryName, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        ThrowIfDisposed();

        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var eventLabel = eventId.Id == 0 && string.IsNullOrWhiteSpace(eventId.Name)
            ? "-"
            : $"{eventId.Id}:{eventId.Name}";

        var line = $"{timestamp} [{level}] {categoryName} [Event:{eventLabel}] {message}";
        if (exception is not null)
        {
            line = $"{line}{Environment.NewLine}{exception}";
        }

        lock (_writeLock)
        {
            File.AppendAllText(_filePath, line + Environment.NewLine);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
