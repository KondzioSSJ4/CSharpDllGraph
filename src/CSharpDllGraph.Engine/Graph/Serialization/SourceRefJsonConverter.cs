using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpDllGraph.Engine.Graph.Serialization;

public sealed class SourceRefJsonConverter : JsonConverter<SourceRef>
{
    public override SourceRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("SourceRef must be an object.");
        }

        var file = string.Empty;
        var spans = new List<SourceSpan>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property in SourceRef.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "file":
                    file = reader.GetString() ?? string.Empty;
                    break;

                case "spans":
                    spans = ReadSpans(ref reader);
                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        return new SourceRef(file, GraphOrdering.OrderSourceRefs([new SourceRef(file, spans)]).Single().Spans);
    }

    public override void Write(Utf8JsonWriter writer, SourceRef value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("file", value.File);

        writer.WritePropertyName("spans");
        writer.WriteStartArray();
        foreach (var span in value.Spans
                     .OrderBy(static span => span.StartLine)
                     .ThenBy(static span => span.StartColumn)
                     .ThenBy(static span => span.EndLine)
                     .ThenBy(static span => span.EndColumn))
        {
            writer.WriteStartObject();
            writer.WriteNumber("endColumn", span.EndColumn);
            writer.WriteNumber("endLine", span.EndLine);
            writer.WriteNumber("startColumn", span.StartColumn);
            writer.WriteNumber("startLine", span.StartLine);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static List<SourceSpan> ReadSpans(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("spans must be an array.");
        }

        var spans = new List<SourceSpan>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            spans.Add(ReadSpan(ref reader));
        }

        return spans;
    }

    private static SourceSpan ReadSpan(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("span must be an object.");
        }

        var startLine = 0;
        var startColumn = 0;
        var endLine = 0;
        var endColumn = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new SourceSpan(startLine, startColumn, endLine, endColumn);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected span property.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "startLine":
                    startLine = reader.GetInt32();
                    break;
                case "startColumn":
                    startColumn = reader.GetInt32();
                    break;
                case "endLine":
                    endLine = reader.GetInt32();
                    break;
                case "endColumn":
                    endColumn = reader.GetInt32();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unterminated span object.");
    }
}
