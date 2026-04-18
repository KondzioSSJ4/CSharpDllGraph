namespace CSharpDllGraph.Engine.Providers;

public sealed record GraphBuildContext(string SolutionPath, string WorkspaceRootPath, bool IsUpdate)
{
    public static GraphBuildContext Create(string solutionPath, string workspaceRootPath, bool isUpdate = false)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            throw new ArgumentException("Solution path is required.", nameof(solutionPath));
        }

        if (string.IsNullOrWhiteSpace(workspaceRootPath))
        {
            throw new ArgumentException("Workspace root path is required.", nameof(workspaceRootPath));
        }

        return new GraphBuildContext(
            Path.GetFullPath(solutionPath),
            Path.GetFullPath(workspaceRootPath),
            isUpdate);
    }
}
