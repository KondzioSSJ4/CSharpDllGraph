using System.Text.Json;
using System.Threading.Channels;

namespace CSharpDllGraph.Engine.Statistics;

public sealed class ToolCallStatisticsService : IAsyncDisposable
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);

    private readonly string _statisticsFilePath;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly Channel<ToolCallEvent> _channel;
    private readonly Task _consumerTask;
    private readonly StatisticsData _statistics;

    public ToolCallStatisticsService(string statisticsFilePath)
    {
        if (string.IsNullOrWhiteSpace(statisticsFilePath))
        {
            throw new ArgumentException("Statistics file path is required.", nameof(statisticsFilePath));
        }

        _statisticsFilePath = Path.GetFullPath(statisticsFilePath);
        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        _statistics = LoadExistingStatistics(_statisticsFilePath, _serializerOptions);
        _channel = Channel.CreateUnbounded<ToolCallEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = ConsumeAsync();
    }

    public void RecordCall(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name is required.", nameof(toolName));
        }

        _channel.Writer.TryWrite(new ToolCallEvent(toolName, DateTimeOffset.UtcNow));
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(DisposeTimeout);
            await _consumerTask.WaitAsync(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ConsumeAsync()
    {
        await foreach (var toolCall in _channel.Reader.ReadAllAsync())
        {
            Apply(toolCall);
            await FlushAsync();
        }
    }

    private void Apply(ToolCallEvent toolCall)
    {
        _statistics.TotalCalls++;

        if (_statistics.Tools.TryGetValue(toolCall.ToolName, out var existing))
        {
            _statistics.Tools[toolCall.ToolName] = existing with
            {
                Calls = existing.Calls + 1,
                FirstCalledUtc = Min(existing.FirstCalledUtc, toolCall.CalledAt),
                LastCalledUtc = Max(existing.LastCalledUtc, toolCall.CalledAt)
            };

            return;
        }

        _statistics.Tools[toolCall.ToolName] = new ToolEntryData(1, toolCall.CalledAt, toolCall.CalledAt);
    }

    private async Task FlushAsync()
    {
        var directoryPath = Path.GetDirectoryName(_statisticsFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var temporaryPath = _statisticsFilePath + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_statistics, _serializerOptions);

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         options: FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, _statisticsFilePath, overwrite: true);
    }

    private static StatisticsData LoadExistingStatistics(string statisticsFilePath, JsonSerializerOptions serializerOptions)
    {
        if (!File.Exists(statisticsFilePath))
        {
            return new StatisticsData();
        }

        var loaded = JsonSerializer.Deserialize<StatisticsData>(File.ReadAllText(statisticsFilePath), serializerOptions);
        if (loaded is null)
        {
            return new StatisticsData();
        }

        var loadedTools = loaded.Tools ?? new Dictionary<string, ToolEntryData>(StringComparer.Ordinal);
        var tools = new Dictionary<string, ToolEntryData>(StringComparer.Ordinal);
        foreach (var (toolName, entry) in loadedTools)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                continue;
            }

            tools[toolName] = new ToolEntryData(
                Math.Max(0, entry.Calls),
                entry.FirstCalledUtc,
                entry.LastCalledUtc);
        }

        var totalCalls = loaded.TotalCalls > 0
            ? loaded.TotalCalls
            : tools.Values.Sum(static entry => entry.Calls);

        return new StatisticsData
        {
            TotalCalls = totalCalls,
            Tools = tools
        };
    }

    private static DateTimeOffset? Min(DateTimeOffset? left, DateTimeOffset right)
    {
        if (left is null)
        {
            return right;
        }

        return left.Value <= right ? left : right;
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset right)
    {
        if (left is null)
        {
            return right;
        }

        return left.Value >= right ? left : right;
    }
}
