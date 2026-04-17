using CSharpDllGraph.Engine.Graph;

namespace CSharpDllGraph.Engine.Tests;

public sealed class NodeSchemaTests
{
    [Fact]
    public void CanConstructNodeForEveryKind_WithDeterministicFileSystemSafeIds()
    {
        var nodes = Enum.GetValues<NodeKind>()
            .Select(CreateNodeForKind)
            .ToArray();

        Assert.Equal(Enum.GetValues<NodeKind>().Length, nodes.Length);

        foreach (var node in nodes)
        {
            var text = node.Id.ToString();
            Assert.Equal(1, text.Count(static character => character == ':'));
            Assert.DoesNotContain('\\', text);

            var prefix = $"{node.Kind}:";
            Assert.StartsWith(prefix, text, StringComparison.Ordinal);
            Assert.True(text.Contains('@'));

            var reCreated = new NodeId(node.Kind, $"domain::{node.Kind}", VersionFor(node.Kind));
            Assert.Equal(node.Id, reCreated);
            Assert.Equal(text, reCreated.ToString());
        }

        Assert.All(
            VersionedKinds(),
            kind =>
            {
                var node = nodes.Single(candidate => candidate.Kind == kind);
                Assert.True(node.Id.ToString().Contains(VersionFor(kind), StringComparison.Ordinal));
            });
    }

    private static Node CreateNodeForKind(NodeKind kind)
    {
        return Node.Create(
            new NodeId(kind, $"domain::{kind}", VersionFor(kind)),
            kind,
            $"{kind} node");
    }

    private static IReadOnlyList<NodeKind> VersionedKinds()
    {
        return
        [
            NodeKind.Package,
            NodeKind.Assembly,
            NodeKind.Namespace,
            NodeKind.Type,
            NodeKind.Method
        ];
    }

    private static string VersionFor(NodeKind kind)
    {
        return kind switch
        {
            NodeKind.Package => "1.2.3",
            NodeKind.Assembly => "1.2.3",
            NodeKind.Namespace => "1.2.3",
            NodeKind.Type => "1.2.3",
            NodeKind.Method => "1.2.3",
            _ => "v1"
        };
    }
}
