namespace CSharpDllGraph.Engine.Graph;

public sealed record WorkspaceManifest(
    string SchemaVersion,
    string EngineVersion,
    DateTimeOffset LastBuildUtc,
    IReadOnlyDictionary<string, string> ContentHashes)
{
    public static WorkspaceManifest CreateDefault(
        string schemaVersion = "phase-01",
        string engineVersion = "phase-01",
        DateTimeOffset? lastBuildUtc = null)
    {
        return new WorkspaceManifest(
            schemaVersion,
            engineVersion,
            lastBuildUtc ?? DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(StringComparer.Ordinal));
    }
}
