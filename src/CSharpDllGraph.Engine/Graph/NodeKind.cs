namespace CSharpDllGraph.Engine.Graph;

public enum NodeKind
{
    Workspace,
    Project,
    Package,
    Assembly,
    Namespace,
    Type,
    Method,
    HttpEndpoint,
    HttpCallSite,
    ExternalRef
}
