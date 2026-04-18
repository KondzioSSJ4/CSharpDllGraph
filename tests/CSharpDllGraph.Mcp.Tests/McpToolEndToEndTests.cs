using System.Text.Json;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Registry;
using CSharpDllGraph.Engine.Store;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CSharpDllGraph.Mcp.Tests;

public sealed class McpToolEndToEndTests
{
    [Fact]
    public async Task AllPhase06Tools_ReturnExpectedStructuredShapes_OverMcpProtocol()
    {
        await using var fixture = await McpProtocolFixture.CreateAsync();
        var schema = LoadSchemaFixture();

        var toolList = await fixture.Client.ListToolsAsync(new ListToolsRequestParams());
        var toolNames = toolList.Tools
            .Select(static tool => tool.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("describe_package_api", toolNames);
        Assert.Contains("find_usages", toolNames);
        Assert.Contains("find_version_conflicts", toolNames);
        Assert.Contains("list_dependencies", toolNames);
        Assert.Contains("suggest_usage", toolNames);
        Assert.Contains("trace_http_call", toolNames);

        var describePackageApi = await fixture.CallToolAsync(
            "describe_package_api",
            new Dictionary<string, object?>
            {
                ["package"] = "Sample.WidgetKit",
                ["version"] = "2.0.0"
            });
        AssertMatchesSchema(describePackageApi, schema.GetProperty("describe_package_api"));
        Assert.Equal("Sample.WidgetKit", describePackageApi.GetProperty("package").GetString());
        Assert.Equal("2.0.0", describePackageApi.GetProperty("version").GetString());
        Assert.Equal("sample-solution", describePackageApi.GetProperty("workspaces")[0].GetString());

        var namespaces = describePackageApi.GetProperty("namespaces");
        Assert.Equal(1, namespaces.GetArrayLength());
        Assert.Equal("Sample.WidgetKit", namespaces[0].GetProperty("namespace").GetString());

        var listDependencies = await fixture.CallToolAsync(
            "list_dependencies",
            new Dictionary<string, object?>
            {
                ["workspace"] = "sample-solution"
            });
        AssertMatchesSchema(listDependencies, schema.GetProperty("list_dependencies"));
        Assert.Equal("sample-solution", listDependencies.GetProperty("workspace").GetString());
        Assert.Equal(2, listDependencies.GetProperty("projects").GetArrayLength());

        var findVersionConflicts = await fixture.CallToolAsync(
            "find_version_conflicts",
            new Dictionary<string, object?>
            {
                ["workspace"] = "sample-solution"
            });
        AssertMatchesSchema(findVersionConflicts, schema.GetProperty("find_version_conflicts"));
        var conflicts = findVersionConflicts.GetProperty("conflicts");
        Assert.Equal(1, conflicts.GetArrayLength());
        Assert.Equal("Sample.WidgetKit", conflicts[0].GetProperty("package").GetString());
        Assert.Equal(
            ["1.0.0", "2.0.0"],
            conflicts[0].GetProperty("versions").EnumerateArray().Select(static item => item.GetString()!).ToArray());

        var findUsages = await fixture.CallToolAsync(
            "find_usages",
            new Dictionary<string, object?>
            {
                ["symbolId"] = new NodeId(NodeKind.Method, "Sample.WidgetKit.IWidgetService.Get(string)", "2.0.0").ToString()
            });
        AssertMatchesSchema(findUsages, schema.GetProperty("find_usages"));
        Assert.Equal(1, findUsages.GetProperty("matchedSymbols").GetArrayLength());
        Assert.Equal(1, findUsages.GetProperty("workspaces").GetArrayLength());
        Assert.Equal("sample-solution", findUsages.GetProperty("workspaces")[0].GetProperty("workspace").GetString());

        var usages = findUsages.GetProperty("workspaces")[0].GetProperty("usages");
        Assert.Equal(1, usages.GetArrayLength());
        Assert.Equal("Calls", usages[0].GetProperty("usageKind").GetString());
        Assert.Equal("src/AppV2/Usage.cs", usages[0].GetProperty("file").GetString());

        var suggestUsage = await fixture.CallToolAsync(
            "suggest_usage",
            new Dictionary<string, object?>
            {
                ["symbolId"] = new NodeId(NodeKind.Method, "Sample.WidgetKit.IWidgetService.Get(string)", "2.0.0").ToString(),
                ["maxResults"] = 1
            });
        AssertMatchesSchema(suggestUsage, schema.GetProperty("suggest_usage"));
        var suggestions = suggestUsage.GetProperty("suggestions");
        Assert.Equal(1, suggestions.GetArrayLength());
        Assert.Equal("sample-solution", suggestions[0].GetProperty("workspace").GetString());
        Assert.Contains("service.Get(value);", suggestions[0].GetProperty("excerpt").GetString(), StringComparison.Ordinal);

        var traceHttpCall = await fixture.CallToolAsync(
            "trace_http_call",
            new Dictionary<string, object?>
            {
                ["method"] = "GET",
                ["path"] = "/api/users"
            });
        AssertMatchesSchema(traceHttpCall, schema.GetProperty("trace_http_call"));
        Assert.Equal("GET", traceHttpCall.GetProperty("method").GetString());
        Assert.Equal("/api/users", traceHttpCall.GetProperty("path").GetString());
    }

    [Fact]
    public async Task TraceHttpCall_ResolvesAcrossTwoRegisteredWorkspaces()
    {
        await using var fixture = await McpProtocolFixture.CreateAsync();
        var result = await fixture.CallToolAsync(
            "trace_http_call",
            new Dictionary<string, object?>
            {
                ["method"] = "GET",
                ["path"] = "/api/users"
            });

        var producers = result.GetProperty("producers");
        var consumers = result.GetProperty("consumers");

        Assert.Equal(1, producers.GetArrayLength());
        Assert.Equal(1, consumers.GetArrayLength());

        Assert.Equal("sample-api", producers[0].GetProperty("workspace").GetString());
        Assert.Equal("GET", producers[0].GetProperty("method").GetString());
        Assert.Equal("/api/users", producers[0].GetProperty("path").GetString());
        Assert.Equal("src/SampleApi/Controllers/UsersController.cs", producers[0].GetProperty("sourceFile").GetString());

        Assert.Equal("sample-ui", consumers[0].GetProperty("workspace").GetString());
        Assert.Equal("GET", consumers[0].GetProperty("method").GetString());
        Assert.Equal("/api/users", consumers[0].GetProperty("path").GetString());
        Assert.Equal("src/sample-api-client.ts", consumers[0].GetProperty("sourceFile").GetString());
    }

    private static JsonElement LoadSchemaFixture()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(GetRepositoryRoot(), "tests", "Fixtures", "McpToolResponseSchema.json")));
        return document.RootElement.Clone();
    }

    private static void AssertMatchesSchema(JsonElement value, JsonElement schema)
    {
        Assert.Equal(JsonValueKind.Object, value.ValueKind);

        foreach (var propertyName in schema.GetProperty("required").EnumerateArray().Select(static item => item.GetString()))
        {
            Assert.False(string.IsNullOrWhiteSpace(propertyName));
            Assert.True(value.TryGetProperty(propertyName, out _), $"Missing property '{propertyName}'.");
        }

        if (!schema.TryGetProperty("children", out var children))
        {
            return;
        }

        foreach (var child in children.EnumerateObject())
        {
            Assert.True(value.TryGetProperty(child.Name, out var childValue), $"Missing child property '{child.Name}'.");
            Assert.Equal(JsonValueKind.Array, childValue.ValueKind);

            if (childValue.GetArrayLength() == 0)
            {
                continue;
            }

            AssertMatchesSchema(childValue[0], child.Value);
        }
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class McpProtocolFixture : IAsyncDisposable
    {
        private readonly string _rootDirectory;
        private readonly string _registryFilePath;
        private readonly string? _originalRegistryContent;
        private readonly StdioClientTransport _transport;
        private readonly McpClient _client;

        private McpProtocolFixture(
            string rootDirectory,
            string registryFilePath,
            string? originalRegistryContent,
            StdioClientTransport transport,
            McpClient client)
        {
            _rootDirectory = rootDirectory;
            _registryFilePath = registryFilePath;
            _originalRegistryContent = originalRegistryContent;
            _transport = transport;
            _client = client;
        }

        public McpClient Client => _client;

        public static async Task<McpProtocolFixture> CreateAsync()
        {
            var rootDirectory = Path.Combine(Path.GetTempPath(), $"csharpdllgraph-mcp-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootDirectory);

            await CreateWorkspaceAsync(
                rootDirectory,
                "sample-solution",
                BuildSampleSolutionSnapshot(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/AppV2/Usage.cs"] = SampleSolutionUsageSource
                },
                new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal)
                {
                    ["src/AppV2/Usage.cs"] = new(2026, 4, 18, 9, 0, 0, TimeSpan.Zero)
                });

            await CreateWorkspaceAsync(
                rootDirectory,
                "sample-api",
                BuildSampleApiSnapshot(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/SampleApi/Controllers/UsersController.cs"] = SampleApiControllerSource
                },
                new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal)
                {
                    ["src/SampleApi/Controllers/UsersController.cs"] = new(2026, 4, 18, 8, 0, 0, TimeSpan.Zero)
                });

            await CreateWorkspaceAsync(
                rootDirectory,
                "sample-ui",
                BuildSampleUiSnapshot(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["src/sample-api-client.ts"] = SampleUiSource
                },
                new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal)
                {
                    ["src/sample-api-client.ts"] = new(2026, 4, 18, 7, 0, 0, TimeSpan.Zero)
                });

            var registryFilePath = new WorkspaceRegistry().RegistryFilePath;
            var registryDirectory = Path.GetDirectoryName(registryFilePath);
            if (!string.IsNullOrWhiteSpace(registryDirectory))
            {
                Directory.CreateDirectory(registryDirectory);
            }

            var originalRegistryContent = File.Exists(registryFilePath)
                ? await File.ReadAllTextAsync(registryFilePath)
                : null;

            File.Delete(registryFilePath);
            File.Delete(registryFilePath + ".lock");

            var registry = new WorkspaceRegistry(registryFilePath);

            foreach (var workspaceName in new[] { "sample-solution", "sample-api", "sample-ui" })
            {
                await registry.AddAsync(new WorkspaceRegistration(
                    workspaceName,
                    Path.Combine(rootDirectory, "workspaces", workspaceName, "root"),
                    Path.Combine(rootDirectory, "workspaces", workspaceName, "graph"),
                    new DateTimeOffset(2026, 4, 18, 12, 0, 0, TimeSpan.Zero)));
            }

            var serverProjectPath = Path.Combine(GetRepositoryRoot(), "src", "CSharpDllGraph.Mcp", "CSharpDllGraph.Mcp.csproj");
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "CSharpDllGraph MCP test host",
                Command = "dotnet",
                Arguments =
                [
                    "run",
                    "--project",
                    serverProjectPath,
                    "--no-build"
                ],
                WorkingDirectory = GetRepositoryRoot(),
                ShutdownTimeout = TimeSpan.FromSeconds(5),
                EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Mcp__LogFilePath"] = Path.Combine(rootDirectory, "logs", "mcp-actions.log")
                }
            });

            var client = await McpClient.CreateAsync(transport);
            return new McpProtocolFixture(rootDirectory, registryFilePath, originalRegistryContent, transport, client);
        }

        public async Task<JsonElement> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?> arguments)
        {
            IReadOnlyDictionary<string, object?> normalizedArguments = arguments
                .Where(static pair => pair.Value is not null)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

            var result = await _client.CallToolAsync(toolName, normalizedArguments, null, null);

            Assert.NotEqual(true, result.IsError);
            var structuredContent = JsonSerializer.SerializeToElement(result.StructuredContent);
            if (structuredContent.ValueKind == JsonValueKind.Object)
            {
                return structuredContent;
            }

            var content = JsonSerializer.SerializeToElement(result.Content);
            if (TryExtractJsonObject(content, out var payload))
            {
                return payload;
            }

            throw new InvalidOperationException($"Tool '{toolName}' did not return a JSON object payload.");
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();

            try
            {
                if (_originalRegistryContent is null)
                {
                    File.Delete(_registryFilePath);
                }
                else
                {
                    await File.WriteAllTextAsync(_registryFilePath, _originalRegistryContent);
                }

                Directory.Delete(_rootDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static async Task CreateWorkspaceAsync(
            string rootDirectory,
            string workspaceName,
            WorkspaceSnapshot snapshot,
            IReadOnlyDictionary<string, string> sourceFiles,
            IReadOnlyDictionary<string, DateTimeOffset> timestamps)
        {
            var workspaceRoot = Path.Combine(rootDirectory, "workspaces", workspaceName, "root");
            var graphRoot = Path.Combine(rootDirectory, "workspaces", workspaceName, "graph");
            Directory.CreateDirectory(workspaceRoot);

            foreach (var sourceFile in sourceFiles)
            {
                var fullPath = Path.Combine(workspaceRoot, sourceFile.Key.Replace('/', Path.DirectorySeparatorChar));
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(fullPath, sourceFile.Value);
                if (timestamps.TryGetValue(sourceFile.Key, out var timestamp))
                {
                    File.SetLastWriteTimeUtc(fullPath, timestamp.UtcDateTime);
                }
            }

            var store = new JsonWorkspaceStore(graphRoot);
            await store.SaveAsync(snapshot);
        }

        private static WorkspaceSnapshot BuildSampleSolutionSnapshot()
        {
            var manifest = WorkspaceManifest.CreateDefault(schemaVersion: "phase-06", engineVersion: "phase-06");

            var projectV1 = Node.Create(
                new NodeId(NodeKind.Project, "AppV1", "project"),
                NodeKind.Project,
                "AppV1",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["projectPath"] = Json("\"AppV1/AppV1.csproj\""),
                    ["targetFrameworks"] = Json("[\"net10.0\"]")
                });

            var projectV2 = Node.Create(
                new NodeId(NodeKind.Project, "AppV2", "project"),
                NodeKind.Project,
                "AppV2",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["projectPath"] = Json("\"AppV2/AppV2.csproj\""),
                    ["targetFrameworks"] = Json("[\"net10.0\"]")
                });

            var packageV1 = Node.Create(
                new NodeId(NodeKind.Package, "Sample.WidgetKit", "1.0.0"),
                NodeKind.Package,
                "Sample.WidgetKit@1.0.0");

            var packageV2 = Node.Create(
                new NodeId(NodeKind.Package, "Sample.WidgetKit", "2.0.0"),
                NodeKind.Package,
                "Sample.WidgetKit@2.0.0");

            var assemblyV2 = Node.Create(
                new NodeId(NodeKind.Assembly, "Sample.WidgetKit", "2.0.0"),
                NodeKind.Assembly,
                "Sample.WidgetKit@2.0.0");

            var namespaceV2 = Node.Create(
                new NodeId(NodeKind.Namespace, "Sample.WidgetKit", "2.0.0"),
                NodeKind.Namespace,
                "Sample.WidgetKit@2.0.0");

            var interfaceType = Node.Create(
                new NodeId(NodeKind.Type, "Sample.WidgetKit.IWidgetService", "2.0.0"),
                NodeKind.Type,
                "Sample.WidgetKit.IWidgetService@2.0.0",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["kind"] = Json("\"interface\"")
                });

            var modernType = Node.Create(
                new NodeId(NodeKind.Type, "Sample.WidgetKit.ModernWidgetService", "2.0.0"),
                NodeKind.Type,
                "Sample.WidgetKit.ModernWidgetService@2.0.0",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["kind"] = Json("\"class\"")
                });

            var interfaceMethod = Node.Create(
                new NodeId(NodeKind.Method, "Sample.WidgetKit.IWidgetService.Get(string)", "2.0.0"),
                NodeKind.Method,
                "Sample.WidgetKit.IWidgetService.Get(string)@2.0.0",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["signature"] = Json("\"string Get(string value)\"")
                });

            var modernGetMethod = Node.Create(
                new NodeId(NodeKind.Method, "Sample.WidgetKit.ModernWidgetService.Get(string)", "2.0.0"),
                NodeKind.Method,
                "Sample.WidgetKit.ModernWidgetService.Get(string)@2.0.0",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["signature"] = Json("\"string Get(string value)\"")
                });

            var modernNormalizedMethod = Node.Create(
                new NodeId(NodeKind.Method, "Sample.WidgetKit.ModernWidgetService.GetNormalized(string)", "2.0.0"),
                NodeKind.Method,
                "Sample.WidgetKit.ModernWidgetService.GetNormalized(string)@2.0.0",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["signature"] = Json("\"string GetNormalized(string value)\"")
                });

            var usageMethod = Node.Create(
                new NodeId(NodeKind.Method, "Sample.AppV2.Usage.Run(string)", "project-AppV2/AppV2"),
                NodeKind.Method,
                "Sample.AppV2.Usage.Run(string)@project-AppV2/AppV2",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["origin"] = Json("\"user\""),
                    ["projectVersion"] = Json("\"project-AppV2/AppV2\"")
                },
                [
                    new SourceRef("src/AppV2/Usage.cs", [new SourceSpan(4, 5, 8, 6)])
                ]);

            var nodes = new Node[]
            {
                projectV1,
                projectV2,
                packageV1,
                packageV2,
                assemblyV2,
                namespaceV2,
                interfaceType,
                modernType,
                interfaceMethod,
                modernGetMethod,
                modernNormalizedMethod,
                usageMethod
            };

            var edges = new Edge[]
            {
                Edge.Create(projectV1.Id, packageV1.Id, EdgeKind.DependsOn, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["isDirect"] = Json("true"),
                    ["tfm"] = Json("\"net10.0\"")
                }),
                Edge.Create(projectV2.Id, packageV2.Id, EdgeKind.DependsOn, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["isDirect"] = Json("true"),
                    ["tfm"] = Json("\"net10.0\"")
                }),
                Edge.Create(packageV2.Id, assemblyV2.Id, EdgeKind.Contains),
                Edge.Create(assemblyV2.Id, namespaceV2.Id, EdgeKind.Contains),
                Edge.Create(namespaceV2.Id, interfaceType.Id, EdgeKind.Contains),
                Edge.Create(namespaceV2.Id, modernType.Id, EdgeKind.Contains),
                Edge.Create(interfaceType.Id, interfaceMethod.Id, EdgeKind.Contains),
                Edge.Create(modernType.Id, modernGetMethod.Id, EdgeKind.Contains),
                Edge.Create(modernType.Id, modernNormalizedMethod.Id, EdgeKind.Contains),
                Edge.Create(usageMethod.Id, interfaceMethod.Id, EdgeKind.Calls, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["callCount"] = Json("1")
                }, [
                    new SourceRef("src/AppV2/Usage.cs", [new SourceSpan(7, 16, 7, 33)])
                ])
            };

            return new WorkspaceSnapshot(manifest, nodes, edges);
        }

        private static WorkspaceSnapshot BuildSampleApiSnapshot()
        {
            var manifest = WorkspaceManifest.CreateDefault(schemaVersion: "phase-06", engineVersion: "phase-06");

            var handlerMethod = Node.Create(
                new NodeId(NodeKind.Method, "SampleApi.Controllers.UsersController.GetAll()", "project-SampleApi/SampleApi"),
                NodeKind.Method,
                "SampleApi.Controllers.UsersController.GetAll()@project-SampleApi/SampleApi");

            var endpoint = Node.Create(
                new NodeId(NodeKind.HttpEndpoint, "GET:/api/users", "project-SampleApi/SampleApi"),
                NodeKind.HttpEndpoint,
                "GET /api/users",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["httpMethod"] = Json("\"GET\""),
                    ["routeTemplate"] = Json("\"/api/users\"")
                },
                [
                    new SourceRef("src/SampleApi/Controllers/UsersController.cs", [new SourceSpan(6, 5, 6, 40)])
                ]);

            return new WorkspaceSnapshot(
                manifest,
                [handlerMethod, endpoint],
                [
                    Edge.Create(handlerMethod.Id, endpoint.Id, EdgeKind.HandlesRoute)
                ]);
        }

        private static WorkspaceSnapshot BuildSampleUiSnapshot()
        {
            var manifest = WorkspaceManifest.CreateDefault(schemaVersion: "phase-06", engineVersion: "phase-06");

            var caller = Node.Create(
                new NodeId(NodeKind.Method, "SampleUi.ApiClient.LoadUsers()", "project-SampleUi/SampleUi"),
                NodeKind.Method,
                "SampleUi.ApiClient.LoadUsers()@project-SampleUi/SampleUi");

            var callSite = Node.Create(
                new NodeId(NodeKind.HttpCallSite, "GET:/api/users", "project-SampleUi/SampleUi"),
                NodeKind.HttpCallSite,
                "GET /api/users",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["httpMethod"] = Json("\"GET\""),
                    ["routeTemplate"] = Json("\"/api/users\""),
                    ["urlTemplate"] = Json("\"/api/users\""),
                    ["callingMethod"] = Json("\"SampleUi.ApiClient.LoadUsers()\"")
                },
                [
                    new SourceRef("src/sample-api-client.ts", [new SourceSpan(2, 1, 2, 20)])
                ]);

            return new WorkspaceSnapshot(
                manifest,
                [caller, callSite],
                [
                    Edge.Create(caller.Id, callSite.Id, EdgeKind.CallsRoute)
                ]);
        }

        private const string SampleSolutionUsageSource =
            """
            namespace Sample.AppV2;

            public sealed class Usage
            {
                public string Run(string value)
                {
                    var service = new Sample.WidgetKit.ModernWidgetService();
                    return service.Get(value);
                }
            }
            """;

        private const string SampleApiControllerSource =
            """
            namespace SampleApi.Controllers;

            public sealed class UsersController
            {
                public string GetAll()
                {
                    return "ok";
                }
            }
            """;

        private const string SampleUiSource =
            """
            export async function loadUsers() {
              return fetch('/api/users');
            }
            """;

        private static bool TryExtractJsonObject(JsonElement content, out JsonElement payload)
        {
            payload = default;

            if (content.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("text", out var textProperty)
                    || textProperty.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = textProperty.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(text);
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    payload = document.RootElement.Clone();
                    return true;
                }
                catch (JsonException)
                {
                }
            }

            return false;
        }
    }
}
