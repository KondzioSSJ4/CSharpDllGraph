using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpDllGraph.Engine.Graph.Serialization;

public sealed class NodeIdJsonConverter : JsonConverter<NodeId>
{
    public override NodeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (!NodeId.TryParse(value, out var nodeId))
        {
            throw new JsonException("Invalid node id.");
        }

        return nodeId;
    }

    public override void Write(Utf8JsonWriter writer, NodeId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
