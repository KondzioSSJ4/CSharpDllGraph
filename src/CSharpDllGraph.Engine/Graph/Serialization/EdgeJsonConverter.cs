using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpDllGraph.Engine.Graph.Serialization;

public sealed class EdgeJsonConverter : JsonConverter<Edge>
{
    public override Edge Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Edge must be an object.");
        }

        NodeId fromId = default;
        NodeId toId = default;
        EdgeKind kind = default;
        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var sourceRefs = new List<SourceRef>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected edge property.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "fromId":
                    fromId = JsonSerializer.Deserialize<NodeId>(ref reader, options);
                    break;

                case "toId":
                    toId = JsonSerializer.Deserialize<NodeId>(ref reader, options);
                    break;

                case "kind":
                    kind = Enum.Parse<EdgeKind>(reader.GetString() ?? string.Empty);
                    break;

                case "attributes":
                    attributes = ReadAttributes(ref reader);
                    break;

                case "sourceRefs":
                    sourceRefs = JsonSerializer.Deserialize<List<SourceRef>>(ref reader, options) ?? [];
                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        return new Edge(
            fromId,
            toId,
            kind,
            GraphOrdering.OrderAttributes(attributes),
            GraphOrdering.OrderSourceRefs(sourceRefs));
    }

    public override void Write(Utf8JsonWriter writer, Edge value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("attributes");
        writer.WriteStartObject();
        foreach (var attribute in value.Attributes.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(attribute.Key);
            JsonDeterministicWriter.WriteElement(writer, attribute.Value);
        }

        writer.WriteEndObject();

        writer.WritePropertyName("fromId");
        JsonSerializer.Serialize(writer, value.FromId, options);
        writer.WriteString("kind", value.Kind.ToString());

        writer.WritePropertyName("sourceRefs");
        JsonSerializer.Serialize(writer, GraphOrdering.OrderSourceRefs(value.SourceRefs), options);

        writer.WritePropertyName("toId");
        JsonSerializer.Serialize(writer, value.ToId, options);

        writer.WriteEndObject();
    }

    private static Dictionary<string, JsonElement> ReadAttributes(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("attributes must be an object.");
        }

        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return attributes;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected attribute property.");
            }

            var key = reader.GetString() ?? string.Empty;
            reader.Read();
            attributes[key] = JsonElement.ParseValue(ref reader).Clone();
        }

        throw new JsonException("Unterminated attributes object.");
    }
}
