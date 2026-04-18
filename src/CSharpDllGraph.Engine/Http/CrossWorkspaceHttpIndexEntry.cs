using CSharpDllGraph.Engine.Graph;

namespace CSharpDllGraph.Engine.Http;

public sealed record CrossWorkspaceHttpIndexEntry(
    string WorkspaceName,
    NodeId NodeId,
    CrossWorkspaceHttpMatchRole Role,
    string Method,
    string Path);
