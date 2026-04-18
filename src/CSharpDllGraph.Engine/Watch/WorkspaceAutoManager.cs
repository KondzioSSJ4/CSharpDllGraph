using CSharpDllGraph.Engine.Config;
using CSharpDllGraph.Engine.Export;
using CSharpDllGraph.Engine.Providers;
using CSharpDllGraph.Engine.Store;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CSharpDllGraph.Engine.Watch;

public sealed class WorkspaceAutoManager : IHostedService, IDisposable
{
    private const string ManifestFileName = "manifest.json";
    private readonly WorkspaceConfig _config;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly GraphBuildPipeline _pipeline;
    private readonly IReadOnlyList<IGraphProvider> _providers;
    private readonly ILogger<WorkspaceAutoManager> _logger;

    private CancellationTokenSource? _stopCancellationSource;
    private Task? _backgroundTask;
    private WorkspaceWatchSession? _watchSession;

    public WorkspaceAutoManager(
        WorkspaceConfig config,
        IWorkspaceContext workspaceContext,
        GraphBuildPipeline pipeline,
        IEnumerable<IGraphProvider> providers,
        ILogger<WorkspaceAutoManager> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _workspaceContext = workspaceContext ?? throw new ArgumentNullException(nameof(workspaceContext));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var resolvedSolutionPath = ResolveSolutionPath(_config);
        var store = new JsonWorkspaceStore(_config.GraphPath);
        var manifestPath = Path.Combine(_config.GraphPath, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            _logger.LogInformation(
                "Graph manifest missing at '{GraphPath}'. Running initial build with {ProviderCount} provider(s).",
                manifestPath,
                _providers.Count);

            await BuildAsync(
                store,
                GraphBuildContext.Create(resolvedSolutionPath, _config.RootPath, isUpdate: false),
                changedPaths: null,
                cancellationToken);
        }
        else
        {
            _logger.LogInformation("Using existing graph manifest at '{GraphPath}'.", manifestPath);
            await TryExportHtmlAsync(cancellationToken);
        }

        _stopCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _watchSession = new WorkspaceWatchSession(
            _config.RootPath,
            resolvedSolutionPath,
            _config.GraphPath,
            debounceMs: 750,
            log: message => _logger.LogInformation("{Message}", message));

        _backgroundTask = Task.Run(
            () => RunWatchLoopAsync(store, resolvedSolutionPath, _stopCancellationSource.Token),
            CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopCancellationSource = _stopCancellationSource;
        var backgroundTask = _backgroundTask;

        _stopCancellationSource = null;
        _backgroundTask = null;

        if (stopCancellationSource is not null)
        {
            stopCancellationSource.Cancel();
        }

        _watchSession?.Dispose();
        _watchSession = null;

        if (backgroundTask is not null)
        {
            await Task.WhenAny(backgroundTask, Task.Delay(Timeout.Infinite, cancellationToken));
        }

        stopCancellationSource?.Dispose();
    }

    public void Dispose()
    {
        _watchSession?.Dispose();
        _stopCancellationSource?.Cancel();
        _stopCancellationSource?.Dispose();
    }

    private async Task RunWatchLoopAsync(
        JsonWorkspaceStore store,
        string solutionPath,
        CancellationToken cancellationToken)
    {
        var session = _watchSession;
        if (session is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<string> batch;
            try
            {
                batch = await session.ReadBatchAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ChannelClosedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await BuildAsync(
                    store,
                    GraphBuildContext.Create(solutionPath, _config.RootPath, isUpdate: true),
                    batch,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Incremental build failed.");
            }
        }
    }

    private async Task BuildAsync(
        JsonWorkspaceStore store,
        GraphBuildContext context,
        IReadOnlyList<string>? changedPaths,
        CancellationToken cancellationToken)
    {
        if (changedPaths is { Count: > 0 })
        {
            _logger.LogInformation(
                "Detected {ChangeCount} change(s): {Changes}",
                changedPaths.Count,
                string.Join(", ", changedPaths.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        await _pipeline.BuildAndPersistAsync(context, store, cancellationToken);
        await TryExportHtmlAsync(cancellationToken);
        _workspaceContext.Invalidate();
        _logger.LogInformation("Workspace graph invalidated after {BuildMode} build.", context.IsUpdate ? "incremental" : "initial");
    }

    private async Task TryExportHtmlAsync(CancellationToken cancellationToken)
    {
        try
        {
            await GraphHtmlExporter.ExportAsync(_config.GraphPath, _config.RootPath, cancellationToken);
            _logger.LogInformation("HTML visualization exported to '{RootPath}/.csharpdllgraph/graph.html'.", _config.RootPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to export HTML visualization.");
        }
    }

    private static string ResolveSolutionPath(WorkspaceConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.SolutionPath))
        {
            var explicitPath = Path.GetFullPath(config.SolutionPath);
            if (!File.Exists(explicitPath))
            {
                throw new FileNotFoundException($"Solution file '{explicitPath}' does not exist.", explicitPath);
            }

            return explicitPath;
        }

        var solutions = Directory
            .EnumerateFiles(config.RootPath, "*.sln*", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        return solutions.Length switch
        {
            1 => Path.GetFullPath(solutions[0]),
            0 => throw new InvalidOperationException($"No .sln or .slnx file found under '{config.RootPath}'."),
            _ => throw new InvalidOperationException($"Multiple solution files found under '{config.RootPath}'. Set WorkspaceConfig.SolutionPath.")
        };
    }
}
