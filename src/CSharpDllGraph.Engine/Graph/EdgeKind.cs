namespace CSharpDllGraph.Engine.Graph;

public enum EdgeKind
{
    Contains,
    References,
    DependsOn,
    Implements,
    Inherits,
    Calls,
    Uses,
    HandlesRoute,
    CallsRoute,
    DescribedBy
}
