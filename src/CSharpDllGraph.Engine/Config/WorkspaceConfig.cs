namespace CSharpDllGraph.Engine.Config;

public sealed record WorkspaceConfig
{
    public WorkspaceConfig(string rootPath, string? graphPath = null, string? solutionPath = null)
    {
        RootPath = rootPath;
        GraphPath = graphPath ?? Path.Combine(rootPath, ".csharpdllgraph", "graph");
        SolutionPath = solutionPath;
    }

    public string RootPath { get; init; }

    public string GraphPath { get; init; }

    public string? SolutionPath { get; init; }
}
