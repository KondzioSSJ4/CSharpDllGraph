using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpDllGraph.Engine.Graph.Serialization;

public sealed class WorkspaceManifestJsonConverter : JsonConverter<WorkspaceManifest>
{
    public override WorkspaceManifest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("manifest must be an object.");
        }

        var schemaVersion = string.Empty;
        var engineVersion = string.Empty;
        var lastBuildUtc = DateTimeOffset.UnixEpoch;
        var contentHashes = new Dictionary<string, string>(StringComparer.Ordinal);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected manifest property.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "schemaVersion":
                    schemaVersion = reader.GetString() ?? string.Empty;
                    break;

                case "engineVersion":
                    engineVersion = reader.GetString() ?? string.Empty;
                    break;

                case "lastBuildUtc":
                    lastBuildUtc = reader.GetDateTimeOffset();
                    break;

                case "contentHashes":
                    contentHashes = ReadHashes(ref reader);
                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        return new WorkspaceManifest(
            schemaVersion,
            engineVersion,
            lastBuildUtc,
            contentHashes.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
    }

    public override void Write(Utf8JsonWriter writer, WorkspaceManifest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("contentHashes");
        writer.WriteStartObject();
        foreach (var contentHash in value.ContentHashes.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteString(contentHash.Key, contentHash.Value);
        }

        writer.WriteEndObject();

        writer.WriteString("engineVersion", value.EngineVersion);
        writer.WriteString("lastBuildUtc", value.LastBuildUtc);
        writer.WriteString("schemaVersion", value.SchemaVersion);
        writer.WriteEndObject();
    }

    private static Dictionary<string, string> ReadHashes(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("contentHashes must be an object.");
        }

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return hashes;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected content hash key.");
            }

            var key = reader.GetString() ?? string.Empty;
            reader.Read();
            hashes[key] = reader.GetString() ?? string.Empty;
        }

        throw new JsonException("Unterminated contentHashes object.");
    }
}
