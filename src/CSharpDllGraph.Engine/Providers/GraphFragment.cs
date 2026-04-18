using CSharpDllGraph.Engine.Graph;

namespace CSharpDllGraph.Engine.Providers;

public sealed record GraphFragment(
    string ProviderId,
    IReadOnlyList<Node> Nodes,
    IReadOnlyList<Edge> Edges)
{
    public static GraphFragment Empty(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        }

        return new GraphFragment(providerId, [], []);
    }
}
