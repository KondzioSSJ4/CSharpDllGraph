using System.Text.Json;
using CSharpDllGraph.Engine.Export;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Http;
using CSharpDllGraph.Engine.Providers;
using CSharpDllGraph.Engine.Query;
using CSharpDllGraph.Engine.Registry;
using CSharpDllGraph.Engine.Statistics;
using CSharpDllGraph.Engine.Store;
using CSharpDllGraph.Engine.Watch;
using CSharpDllGraph.Providers.Dotnet;
using CSharpDllGraph.Providers.Dotnet.Http;

return await CliApplication.RunAsync(args, CancellationToken.None);

internal static class CliApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            RoslynBootstrap.EnsureRegistered();

            if (args.Length == 0)
            {
                throw new CliException(GetUsage());
            }

            return args[0] switch
            {
                "build" => await RunBuildAsync(args[1..], isUpdate: false, cancellationToken),
                "update" => await RunBuildAsync(args[1..], isUpdate: true, cancellationToken),
                "watch" => await RunWatchAsync(args[1..], cancellationToken),
                "query" => await RunQueryAsync(args[1..], cancellationToken),
                "help" or "--help" or "-h" => throw new CliException(GetUsage()),
                _ => throw new CliException($"Unknown command '{args[0]}'.{Environment.NewLine}{GetUsage()}")
            };
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunBuildAsync(
        IReadOnlyList<string> args,
        bool isUpdate,
        CancellationToken cancellationToken)
    {
        var parser = ArgumentParser.Parse(args);
        var workspaceRoot = RequireValue(parser.Positionals, 0, "workspace-root");
        var resolvedWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        var graphPath = ResolveGraphPath(parser.GetSingleOption("--graph-path"), resolvedWorkspaceRoot);
        var solutionPath = ResolveSolutionPath(parser.GetSingleOption("--solution"), resolvedWorkspaceRoot);

        var snapshot = await BuildWorkspaceAsync(solutionPath, resolvedWorkspaceRoot, graphPath, isUpdate, cancellationToken);

        WriteJson(new BuildCommandResult(
            isUpdate ? "update" : "build",
            resolvedWorkspaceRoot,
            graphPath,
            solutionPath,
            snapshot.Manifest.LastBuildUtc,
            snapshot.Nodes.Count,
            snapshot.Edges.Count));
        await GraphHtmlExporter.ExportAsync(graphPath, resolvedWorkspaceRoot, cancellationToken);
        Console.WriteLine("Graph visualization: .csharpdllgraph/graph.html");

        return 0;
    }

    private static async Task<int> RunWatchAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var parser = ArgumentParser.Parse(args);
        var workspaceRoot = RequireValue(parser.Positionals, 0, "workspace-root");
        var resolvedWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        var graphPath = ResolveGraphPath(parser.GetSingleOption("--graph-path"), resolvedWorkspaceRoot);
        var solutionPath = ResolveSolutionPath(parser.GetSingleOption("--solution"), resolvedWorkspaceRoot);
        var debounceMs = parser.GetIntOption("--debounce-ms", 750);
        if (debounceMs < 100)
        {
            throw new CliException("Option '--debounce-ms' must be at least 100.");
        }

        using var watchCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var cancelHandler = new ConsoleCancelHandler(watchCancellationSource, static message => Console.Error.WriteLine(message));

        LogWatch($"Watching '{resolvedWorkspaceRoot}'.");
        LogWatch($"Solution: {solutionPath}");
        LogWatch($"Graph path: {graphPath}");
        LogWatch($"Debounce: {debounceMs}ms");

        await RunWatchUpdateAsync(resolvedWorkspaceRoot, solutionPath, graphPath, batch: null, watchCancellationSource.Token);

        using var watcher = new WorkspaceWatchSession(
            resolvedWorkspaceRoot,
            solutionPath,
            graphPath,
            debounceMs,
            static message => Console.Error.WriteLine(message));

        try
        {
            while (!watchCancellationSource.IsCancellationRequested)
            {
                IReadOnlyList<string> batch;
                try
                {
                    batch = await watcher.ReadBatchAsync(watchCancellationSource.Token);
                }
                catch (OperationCanceledException) when (watchCancellationSource.IsCancellationRequested)
                {
                    break;
                }

                await RunWatchUpdateAsync(resolvedWorkspaceRoot, solutionPath, graphPath, batch, watchCancellationSource.Token);
            }
        }
        finally
        {
            LogWatch($"Watcher stopped for '{resolvedWorkspaceRoot}'.");
        }

        return 0;
    }

    private static async Task<WorkspaceSnapshot> BuildWorkspaceAsync(
        string solutionPath,
        string workspaceRootPath,
        string graphPath,
        bool isUpdate,
        CancellationToken cancellationToken)
    {
        var store = new JsonWorkspaceStore(graphPath);
        var pipeline = new GraphBuildPipeline(CreateProviders(), Microsoft.Extensions.Logging.Abstractions.NullLogger<GraphBuildPipeline>.Instance);
        var snapshot = await pipeline.BuildAndPersistAsync(
            GraphBuildContext.Create(solutionPath, workspaceRootPath, isUpdate),
            store,
            cancellationToken);

        var reconciled = HttpEndpointReconciler.Reconcile(snapshot.Nodes, snapshot.Edges);
        var reconciledSnapshot = new WorkspaceSnapshot(snapshot.Manifest, reconciled.Nodes, reconciled.Edges);
        await store.SaveAsync(reconciledSnapshot, cancellationToken);
        return reconciledSnapshot;
    }

    private static async Task RunWatchUpdateAsync(
        string workspaceRootPath,
        string solutionPath,
        string graphPath,
        IReadOnlyList<string>? batch,
        CancellationToken cancellationToken)
    {
        try
        {
            if (batch is null)
            {
                LogWatch("Running initial update.");
            }
            else
            {
                LogWatch($"Detected {batch.Count} change(s): {string.Join(", ", batch.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase))}");
                LogWatch("Running incremental update.");
            }

            var snapshot = await BuildWorkspaceAsync(solutionPath, workspaceRootPath, graphPath, isUpdate: true, cancellationToken);
            LogWatch($"Update complete. Nodes: {snapshot.Nodes.Count}. Edges: {snapshot.Edges.Count}. Last build: {snapshot.Manifest.LastBuildUtc:O}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogWatch($"Update failed: {ex.Message}");
        }
    }

    private static IReadOnlyList<IGraphProvider> CreateProviders()
    {
        return
        [
            new DotnetProvider(Microsoft.Extensions.Logging.Abstractions.NullLogger<DotnetProvider>.Instance),
            new ControllerEndpointProvider(),
            new MinimalApiEndpointProvider(),
            new HttpClientCallSiteProvider(),
            new HttpFileCallSiteProvider(),
            new JsFetchCallSiteProvider(),
            new OpenApiSpecProvider(),
            new PostmanCallSiteProvider()
        ];
    }

    private static async Task<int> RunQueryAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count == 0)
        {
            throw new CliException("Missing query tool name.");
        }

        var parser = ArgumentParser.Parse(args.Skip(1).ToArray());
        var workspacePath = parser.GetSingleOption("--workspace");
        WorkspaceRegistration? workspace = null;
        if (!string.IsNullOrWhiteSpace(workspacePath))
        {
            var resolvedPath = Path.GetFullPath(workspacePath);
            var name = Path.GetFileName(resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var graphPath = ResolveGraphPath(null, resolvedPath);
            workspace = new WorkspaceRegistration(name, resolvedPath, graphPath);
        }

        IReadOnlyList<WorkspaceRegistration> registrations = workspace is not null
            ? [workspace]
            : [];
        var queryService = new GraphQueryService(registrations, new CrossWorkspaceHttpIndexBuilder(registrations));

        var statisticsFilePath = workspace is not null
            ? Path.Combine(workspace.RootPath, ".csharpdllgraph", "statistics.json")
            : null;

        await using var statistics = statisticsFilePath is not null ? new ToolCallStatisticsService(statisticsFilePath) : null;
        statistics?.RecordCall(args[0]);

        object result = args[0] switch
        {
            "describe_package_api" => await queryService.DescribePackageApiAsync(
                new DescribePackageApiRequest(
                    parser.RequireOption("--package"),
                    parser.RequireOption("--version"),
                    parser.GetSingleOption("--tfm"),
                    parser.GetSingleOption("--filter")),
                cancellationToken),
            "list_dependencies" => await queryService.ListDependenciesAsync(
                new ListDependenciesRequest(
                    RequireWorkspaceName(workspace),
                    parser.GetSingleOption("--project")),
                cancellationToken),
            "find_version_conflicts" => await queryService.FindVersionConflictsAsync(
                new FindVersionConflictsRequest(
                    RequireWorkspaceName(workspace)),
                cancellationToken),
            "find_usages" => await queryService.FindUsagesAsync(
                new FindUsagesRequest(
                    parser.GetSingleOption("--symbol-id"),
                    parser.GetSingleOption("--kind"),
                    parser.GetSingleOption("--full-name"),
                    parser.GetSingleOption("--version"),
                    parser.GetMultiOption("--workspace-filter")),
                cancellationToken),
            "suggest_usage" => await queryService.SuggestUsageAsync(
                new SuggestUsageRequest(
                    parser.GetSingleOption("--symbol-id"),
                    parser.GetSingleOption("--kind"),
                    parser.GetSingleOption("--full-name"),
                    parser.GetSingleOption("--version"),
                    parser.GetMultiOption("--workspace-filter"),
                    parser.GetIntOption("--max-results", 5)),
                cancellationToken),
            "trace_http_call" => await queryService.TraceHttpCallAsync(
                new TraceHttpCallRequest(
                    parser.RequireOption("--method"),
                    parser.RequireOption("--path"),
                    null),
                cancellationToken),
            _ => throw new CliException($"Unknown query tool '{args[0]}'.")
        };

        WriteJson(result);
        return 0;
    }

    private static string RequireWorkspaceName(WorkspaceRegistration? workspace)
    {
        if (workspace is null)
        {
            throw new CliException("Missing required option '--workspace'.");
        }

        return workspace.Name;
    }

    private static string ResolveGraphPath(string? explicitGraphPath, string workspaceRootPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitGraphPath))
        {
            return Path.GetFullPath(explicitGraphPath);
        }

        return Path.GetFullPath(Path.Combine(workspaceRootPath, ".csharpdllgraph", "graph"));
    }

    private static string ResolveSolutionPath(string? explicitSolutionPath, string workspaceRootPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitSolutionPath))
        {
            var fullPath = Path.GetFullPath(explicitSolutionPath);
            if (!File.Exists(fullPath))
            {
                throw new CliException($"Solution file '{fullPath}' does not exist.");
            }

            return fullPath;
        }

        var solutions = Directory
            .EnumerateFiles(workspaceRootPath, "*.sln*", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        return solutions.Length switch
        {
            1 => Path.GetFullPath(solutions[0]),
            0 => throw new CliException($"No .sln or .slnx file found under '{workspaceRootPath}'."),
            _ => throw new CliException($"Multiple solution files found under '{workspaceRootPath}'. Pass --solution explicitly.")
        };
    }

    private static string RequireValue(IReadOnlyList<string> values, int index, string name)
    {
        if (index >= values.Count || string.IsNullOrWhiteSpace(values[index]))
        {
            throw new CliException($"Missing required argument '{name}'.");
        }

        return values[index];
    }

    private static void WriteJson<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    private static string GetUsage()
    {
        return """
               Usage:
                 build <workspace-root> [--solution <path>] [--graph-path <path>]
                 update <workspace-root> [--solution <path>] [--graph-path <path>]
                 watch <workspace-root> [--solution <path>] [--graph-path <path>] [--debounce-ms <ms>]
                 query <tool> [--workspace <path>] [options]

               Query tools:
                 describe_package_api --package <name> --version <version> [--tfm <tfm>] [--filter <regex>] [--workspace <path>]
                 list_dependencies --workspace <path> [--project <name-or-path>]
                 find_version_conflicts --workspace <path>
                 find_usages [--symbol-id <id>] [--kind <kind>] [--full-name <name>] [--version <token>] [--workspace <path>]
                 suggest_usage [--symbol-id <id>] [--kind <kind>] [--full-name <name>] [--version <token>] [--workspace <path>] [--max-results <count>]
                 trace_http_call --method <http-method> --path <route-or-url> [--workspace <path>]
               """;
    }

    private sealed record BuildCommandResult(
        string Command,
        string RootPath,
        string GraphPath,
        string SolutionPath,
        DateTimeOffset LastBuildUtc,
        int NodeCount,
        int EdgeCount);

    private static void LogWatch(string message)
    {
        Console.Error.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
    }
}

internal sealed class ArgumentParser
{
    private readonly Dictionary<string, List<string>> _options = new(StringComparer.Ordinal);

    private ArgumentParser(IReadOnlyList<string> positionals)
    {
        Positionals = positionals;
    }

    public IReadOnlyList<string> Positionals { get; }

    public static ArgumentParser Parse(IReadOnlyList<string> args)
    {
        var positionals = new List<string>();
        var parser = new ArgumentParser(positionals);

        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(token);
                continue;
            }

            if (index == args.Count - 1 || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new CliException($"Missing value for option '{token}'.");
            }

            if (!parser._options.TryGetValue(token, out var values))
            {
                values = [];
                parser._options[token] = values;
            }

            values.Add(args[index + 1]);
            index++;
        }

        return parser;
    }

    public string RequireOption(string name)
    {
        return GetSingleOption(name) ?? throw new CliException($"Missing required option '{name}'.");
    }

    public string? GetSingleOption(string name)
    {
        if (!_options.TryGetValue(name, out var values) || values.Count == 0)
        {
            return null;
        }

        if (values.Count > 1)
        {
            throw new CliException($"Option '{name}' can only be provided once.");
        }

        return values[0];
    }

    public string[]? GetMultiOption(string name)
    {
        return !_options.TryGetValue(name, out var values) || values.Count == 0
            ? null
            : values.ToArray();
    }

    public int GetIntOption(string name, int defaultValue)
    {
        var value = GetSingleOption(name);
        if (value is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed))
        {
            throw new CliException($"Option '{name}' requires an integer value.");
        }

        return parsed;
    }
}

internal sealed class CliException(string message) : Exception(message);

internal sealed class ConsoleCancelHandler : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Action<string> _log;

    public ConsoleCancelHandler(CancellationTokenSource cancellationTokenSource, Action<string> log)
    {
        _cancellationTokenSource = cancellationTokenSource ?? throw new ArgumentNullException(nameof(cancellationTokenSource));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
    {
        if (_cancellationTokenSource.IsCancellationRequested)
        {
            args.Cancel = true;
            return;
        }

        args.Cancel = true;
        _log("Ctrl+C received. Stopping watcher.");
        _cancellationTokenSource.Cancel();
    }
}
