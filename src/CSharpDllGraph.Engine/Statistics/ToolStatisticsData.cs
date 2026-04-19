using System.Text.Json.Serialization;

namespace CSharpDllGraph.Engine.Statistics;

public sealed record ToolEntryData(
    [property: JsonPropertyName("calls")] long Calls,
    [property: JsonPropertyName("first_called_utc")] DateTimeOffset? FirstCalledUtc,
    [property: JsonPropertyName("last_called_utc")] DateTimeOffset? LastCalledUtc);

public sealed class StatisticsData
{
    [JsonPropertyName("total_calls")]
    public long TotalCalls { get; set; }

    [JsonPropertyName("tools")]
    public Dictionary<string, ToolEntryData> Tools { get; set; } = new(StringComparer.Ordinal);
}
