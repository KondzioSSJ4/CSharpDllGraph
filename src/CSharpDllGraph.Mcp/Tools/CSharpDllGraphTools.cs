using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CSharpDllGraph.Mcp.Tools;

[McpServerToolType]
internal sealed class CSharpDllGraphTools
{
    [McpServerTool(Name = "ping"), Description("Returns pong to verify the server is running.")]
    public static string Ping() => "pong";
}
