using CSharpDllGraph.Engine.Config;
using CSharpDllGraph.Engine.Http;
using CSharpDllGraph.Engine.Providers;
using CSharpDllGraph.Engine.Query;
using CSharpDllGraph.Engine.Watch;
using CSharpDllGraph.Mcp.Logging;
using CSharpDllGraph.Mcp.Tools;
using CSharpDllGraph.Providers.Dotnet;
using CSharpDllGraph.Providers.Dotnet.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

RoslynBootstrap.EnsureRegistered();

var cliWorkspacePath = TryGetArg(args, "--workspace-path");
var cliSolutionPath = TryGetArg(args, "--solution-path");
WorkspaceConfig workspaceConfig;

try
{
    workspaceConfig = WorkspaceConfigLoader.Load(cliWorkspacePath, cliSolutionPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.Exit(1);
    return;
}

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

var logFilePath = builder.Configuration["Mcp:LogFilePath"] ?? "logs/mcp-actions.log";

using var fileLoggerProvider = new FileLoggerProvider(logFilePath);

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddProvider(fileLoggerProvider);

builder.Services
    .AddSingleton(workspaceConfig)
    .AddSingleton<IWorkspaceContext, WorkspaceContext>()
    .AddSingleton<IGraphQueryService, SingleWorkspaceQueryService>()
    .AddSingleton<ICrossWorkspaceHttpIndexBuilder, SingleWorkspaceHttpIndexBuilder>()
    .AddSingleton<GraphBuildPipeline>()
    .AddSingleton<IGraphProvider, DotnetProvider>()
    .AddSingleton<IGraphProvider, ControllerEndpointProvider>()
    .AddSingleton<IGraphProvider, MinimalApiEndpointProvider>()
    .AddSingleton<IGraphProvider, HttpClientCallSiteProvider>()
    .AddSingleton<IGraphProvider, HttpFileCallSiteProvider>()
    .AddSingleton<IGraphProvider, JsFetchCallSiteProvider>()
    .AddSingleton<IGraphProvider, OpenApiSpecProvider>()
    .AddSingleton<IGraphProvider, PostmanCallSiteProvider>()
    .AddHostedService<WorkspaceAutoManager>()
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CSharpDllGraphTools>();

var host = builder.Build();
await host.RunAsync();

static string? TryGetArg(string[] args, string option)
{
    for (var index = 0; index < args.Length; index++)
    {
        if (!string.Equals(args[index], option, StringComparison.Ordinal))
        {
            continue;
        }

        if (index == args.Length - 1 || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new InvalidOperationException($"Missing value for option '{option}'.");
        }

        return args[index + 1];
    }

    return null;
}
