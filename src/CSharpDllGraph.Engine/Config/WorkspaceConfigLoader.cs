using System.Text.Json;

namespace CSharpDllGraph.Engine.Config;

public static class WorkspaceConfigLoader
{
    private const string ConfigFileName = ".csharpdllgraph.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static WorkspaceConfig Load(string? cliWorkspacePath, string? cliSolutionPath = null)
    {
        if (!string.IsNullOrWhiteSpace(cliWorkspacePath))
        {
            var rootPath = Path.GetFullPath(cliWorkspacePath);
            var solutionPathOverride = string.IsNullOrWhiteSpace(cliSolutionPath)
                ? null
                : Path.GetFullPath(cliSolutionPath);
            return new WorkspaceConfig(rootPath, solutionPath: solutionPathOverride);
        }

        var configPath = FindConfigPath(Directory.GetCurrentDirectory());
        if (configPath is null)
        {
            throw new InvalidOperationException(
                $"Workspace config file '{ConfigFileName}' not found. Pass workspace path with CLI argument or add the config file.");
        }

        var config = LoadConfigFile(configPath);
        var configDirectory = Path.GetDirectoryName(configPath)
                              ?? throw new InvalidOperationException($"Config path '{configPath}' has no parent directory.");

        var workspaceRootPath = ResolvePath(config.WorkspacePath, configDirectory);
        var graphPath = string.IsNullOrWhiteSpace(config.GraphPath)
            ? Path.Combine(workspaceRootPath, ".csharpdllgraph", "graph")
            : ResolvePath(config.GraphPath, workspaceRootPath);
        var solutionPath = string.IsNullOrWhiteSpace(config.SolutionPath)
            ? null
            : ResolvePath(config.SolutionPath, workspaceRootPath);

        return new WorkspaceConfig(workspaceRootPath, graphPath, solutionPath);
    }

    private static WorkspaceConfigFile LoadConfigFile(string configPath)
    {
        WorkspaceConfigFile? config;

        try
        {
            var json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<WorkspaceConfigFile>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Failed to parse workspace config '{configPath}'.", exception);
        }

        if (config is null || string.IsNullOrWhiteSpace(config.WorkspacePath))
        {
            throw new InvalidOperationException(
                $"Workspace config '{configPath}' must define non-empty 'workspacePath'.");
        }

        return config;
    }

    private static string? FindConfigPath(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, ConfigFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string ResolvePath(string path, string basePath)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(basePath, path));
    }
}
