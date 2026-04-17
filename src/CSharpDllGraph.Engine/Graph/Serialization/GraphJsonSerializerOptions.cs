using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpDllGraph.Engine.Graph.Serialization;

public static class GraphJsonSerializerOptions
{
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new NodeIdJsonConverter());
        options.Converters.Add(new SourceRefJsonConverter());
        options.Converters.Add(new NodeJsonConverter());
        options.Converters.Add(new EdgeJsonConverter());
        options.Converters.Add(new WorkspaceManifestJsonConverter());
        return options;
    }
}
