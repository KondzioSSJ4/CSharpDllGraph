namespace CSharpDllGraph.Engine.Registry;

public sealed record WorkspaceRegistration(
    string Name,
    string RootPath,
    string GraphPath);
