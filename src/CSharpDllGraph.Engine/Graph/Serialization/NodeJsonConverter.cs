using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpDllGraph.Engine.Graph.Serialization;

public sealed class NodeJsonConverter : JsonConverter<Node>
{
    public override Node Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Node must be an object.");
        }

        NodeId id = default;
        NodeKind kind = default;
        var displayName = string.Empty;
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
                throw new JsonException("Expected node property.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "id":
                    id = JsonSerializer.Deserialize<NodeId>(ref reader, options);
                    break;

                case "kind":
                    kind = Enum.Parse<NodeKind>(reader.GetString() ?? string.Empty);
                    break;

                case "displayName":
                    displayName = reader.GetString() ?? string.Empty;
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

        return new Node(
            id,
            kind,
            displayName,
            GraphOrdering.OrderAttributes(attributes),
            GraphOrdering.OrderSourceRefs(sourceRefs));
    }

    public override void Write(Utf8JsonWriter writer, Node value, JsonSerializerOptions options)
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

        writer.WriteString("displayName", value.DisplayName);
        writer.WritePropertyName("id");
        JsonSerializer.Serialize(writer, value.Id, options);
        writer.WriteString("kind", value.Kind.ToString());

        writer.WritePropertyName("sourceRefs");
        JsonSerializer.Serialize(writer, GraphOrdering.OrderSourceRefs(value.SourceRefs), options);

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
