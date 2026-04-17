using CSharpDllGraph.Mcp.Logging;
using CSharpDllGraph.Mcp.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CSharpDllGraphTools>();

var host = builder.Build();
await host.RunAsync();
