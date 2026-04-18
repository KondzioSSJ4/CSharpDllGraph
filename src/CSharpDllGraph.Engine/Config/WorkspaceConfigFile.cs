using System.Text.Json.Serialization;

namespace CSharpDllGraph.Engine.Config;

public sealed class WorkspaceConfigFile
{
    [JsonPropertyName("workspacePath")]
    public required string WorkspacePath { get; init; }

    [JsonPropertyName("graphPath")]
    public string? GraphPath { get; init; }

    [JsonPropertyName("solutionPath")]
    public string? SolutionPath { get; init; }
}
