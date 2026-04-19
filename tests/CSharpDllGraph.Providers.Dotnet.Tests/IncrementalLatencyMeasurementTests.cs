using System.Diagnostics;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Http;
using CSharpDllGraph.Engine.Providers;
using CSharpDllGraph.Engine.Query;
using CSharpDllGraph.Engine.Registry;
using CSharpDllGraph.Engine.Store;
using CSharpDllGraph.Providers.Dotnet;
using CSharpDllGraph.Providers.Dotnet.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace CSharpDllGraph.Providers.Dotnet.Tests;

public sealed class IncrementalLatencyMeasurementTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EditToQueryLatency_OnSampleApiFixture_IsMeasured()
    {
        const int iterationCount = 3;
        var measurements = new List<long>(iterationCount);

        for (var iteration = 1; iteration <= iterationCount; iteration++)
        {
            var workspaceRoot = Path.Combine(Path.GetTempPath(), $"csharpdllgraph-latency-{Guid.NewGuid():N}");

            try
            {
                CopyDirectory(GetSampleApiFixtureRoot(), workspaceRoot);
                await RestoreFixtureAsync(workspaceRoot);

                var solutionPath = Path.Combine(workspaceRoot, "SampleApi.slnx");
                var graphPath = Path.Combine(workspaceRoot, ".csharpdllgraph", "graph");
                var programPath = Path.Combine(workspaceRoot, "Program.cs");
                var workspaceName = "sample-api";

                IReadOnlyList<WorkspaceRegistration> registrations = [new WorkspaceRegistration(workspaceName, workspaceRoot, graphPath)];
                var queryService = new GraphQueryService(registrations, new CrossWorkspaceHttpIndexBuilder(registrations));

                await BuildWorkspaceAsync(solutionPath, workspaceRoot, graphPath, isUpdate: false);

                var baseline = await queryService.TraceHttpCallAsync(new TraceHttpCallRequest("POST", "/api/ping", [workspaceName]));
                Assert.Contains(
                    baseline.Producers,
                    producer => string.Equals(producer.SourceFile, "Program.cs", StringComparison.OrdinalIgnoreCase));

                var originalProgram = await File.ReadAllTextAsync(programPath);
                var updatedProgram = originalProgram.Replace(
                    "app.MapPost(\"/api/ping\", () => \"pong\");",
                    "app.MapPost(\"/api/pong\", () => \"pong\");",
                    StringComparison.Ordinal);
                Assert.NotEqual(originalProgram, updatedProgram);

                var stopwatch = Stopwatch.StartNew();
                await File.WriteAllTextAsync(programPath, updatedProgram);

                await BuildWorkspaceAsync(solutionPath, workspaceRoot, graphPath, isUpdate: true);

                var updated = await queryService.TraceHttpCallAsync(new TraceHttpCallRequest("POST", "/api/ping", [workspaceName]));
                stopwatch.Stop();

                Assert.Empty(updated.Producers);

                measurements.Add(stopwatch.ElapsedMilliseconds);
                output.WriteLine($"Iteration {iteration}: {stopwatch.ElapsedMilliseconds} ms");
            }
            finally
            {
                TryDeleteDirectory(workspaceRoot);
            }
        }

        var ordered = measurements.OrderBy(static value => value).ToArray();
        var median = ordered[ordered.Length / 2];
        output.WriteLine($"Median edit-to-query latency: {median} ms");
    }

    private static async Task BuildWorkspaceAsync(
        string solutionPath,
        string workspaceRoot,
        string graphPath,
        bool isUpdate)
    {
        var pipeline = new GraphBuildPipeline(
        [
            new DotnetProvider(NullLogger<DotnetProvider>.Instance),
            new ControllerEndpointProvider(),
            new MinimalApiEndpointProvider(),
            new HttpClientCallSiteProvider(),
            new HttpFileCallSiteProvider(),
            new JsFetchCallSiteProvider(),
            new OpenApiSpecProvider(),
            new PostmanCallSiteProvider()
        ], NullLogger<GraphBuildPipeline>.Instance);

        var store = new JsonWorkspaceStore(graphPath);
        await pipeline.BuildAndPersistAsync(
            GraphBuildContext.Create(solutionPath, workspaceRoot, isUpdate),
            store);
    }

    private static async Task RestoreFixtureAsync(string workspaceRoot)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "restore SampleApi.csproj",
                WorkingDirectory = workspaceRoot,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.Start();
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet restore failed with exit code {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
    }

    private static string GetSampleApiFixtureRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fixtures", "SampleApi"));
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var targetPath = file.Replace(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
