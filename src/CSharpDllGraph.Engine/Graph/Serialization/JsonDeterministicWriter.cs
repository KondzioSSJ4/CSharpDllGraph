using System.Text.Json;

namespace CSharpDllGraph.Engine.Graph.Serialization;

internal static class JsonDeterministicWriter
{
    public static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value);
                }

                writer.WriteEndObject();
                return;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
                }

                writer.WriteEndArray();
                return;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                return;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                return;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                return;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                return;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                return;

            default:
                writer.WriteRawValue(element.GetRawText());
                return;
        }
    }
}
