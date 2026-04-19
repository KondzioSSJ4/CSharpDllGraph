using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Channels;
using System.Xml.Linq;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Providers;
using CSharpDllGraph.Providers.Dotnet.Cache;
using Microsoft.Extensions.Logging;

namespace CSharpDllGraph.Providers.Dotnet;

public sealed class DotnetProvider : IGraphProvider
{
    private const string ProviderId = "dotnet";
    private readonly ILogger<DotnetProvider> _logger;

    public DotnetProvider(ILogger<DotnetProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Id => ProviderId;

    public async IAsyncEnumerable<GraphFragment> CollectAsync(
        GraphBuildContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        RoslynBootstrap.EnsureRegistered();

        var solutionPath = context.SolutionPath;
        var solutionDirectory = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException("Solution directory is required.");

        var collector = new GraphCollector();
        var packageFragmentCache = new ParallelFilePackageFragmentCache(context.WorkspaceRootPath);
        var packages = new Dictionary<PackageIdentity, PackageWorkItem>(PackageIdentityComparer.Instance);
        var projectPackageVersionsByPath = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("DotnetProvider: discovering projects from '{SolutionPath}'.", solutionPath);

        foreach (var projectPath in DiscoverProjectPaths(solutionPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("DotnetProvider: loading assets for project '{ProjectPath}'.", projectPath);
            var assets = LoadProjectAssets(projectPath);
            projectPackageVersionsByPath[projectPath] = BuildPackageVersionMap(assets);
            var projectNode = CreateProjectNode(solutionDirectory, projectPath, assets.TargetFrameworks);
            collector.AddNode(projectNode);

            foreach (var target in assets.Targets)
            {
                foreach (var package in target.Packages)
                {
                    var packageNode = CreatePackageNode(package.Identity, package.RelativePath);
                    collector.AddNode(packageNode);
                    collector.AddEdge(
                        Edge.Create(
                            projectNode.Id,
                            packageNode.Id,
                            EdgeKind.DependsOn,
                            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                            {
                                ["isDirect"] = JsonSerializer.SerializeToElement(package.IsDirect),
                                ["tfm"] = JsonSerializer.SerializeToElement(target.FrameworkMoniker)
                            }));

                    if (!packages.TryGetValue(package.Identity, out var workItem))
                    {
                        workItem = new PackageWorkItem(package.Identity, package.RelativePath);
                        packages.Add(package.Identity, workItem);
                    }

                    foreach (var packageFolder in assets.PackageFolders)
                    {
                        workItem.PackageFolders.Add(packageFolder);
                    }

                    foreach (var dllPath in package.DllPaths)
                    {
                        workItem.DllPaths.Add(dllPath);
                    }
                }
            }
        }

        var packageList = packages.Values
            .OrderBy(static item => item.Identity.Name, StringComparer.Ordinal)
            .ThenBy(static item => item.Identity.Version, StringComparer.Ordinal)
            .ToArray();

        _logger.LogInformation("DotnetProvider: emitting structure for {PackageCount} packages.", packageList.Length);

        var prewarmTargets = packageList
            .Select(static p => (p.Identity.Name, p.Identity.Version))
            .ToArray();
        await packageFragmentCache.PreWarmAsync(prewarmTargets, _logger, cancellationToken);

        await EmitPackagesParallelAsync(packageList, collector, packageFragmentCache, cancellationToken);

        _logger.LogInformation("DotnetProvider: starting Roslyn usage analysis.");
        var roslynSw = Stopwatch.StartNew();
        var roslynFragment = await RoslynUsageGraphBuilder.BuildAsync(
            solutionPath,
            solutionDirectory,
            projectPackageVersionsByPath,
            cancellationToken);

        _logger.LogInformation(
            "DotnetProvider: Roslyn analysis finished in {Elapsed:0.0}s. Nodes: {NodeCount}, Edges: {EdgeCount}.",
            roslynSw.Elapsed.TotalSeconds,
            roslynFragment.Nodes.Count,
            roslynFragment.Edges.Count);

        foreach (var node in roslynFragment.Nodes)
        {
            collector.AddNode(node);
        }

        foreach (var edge in roslynFragment.Edges)
        {
            collector.AddEdge(edge);
        }

        await Task.Yield();
        yield return collector.ToFragment(Id);
    }

    private static IEnumerable<string> DiscoverProjectPaths(string solutionPath)
    {
        if (solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return DiscoverProjectsFromSlnx(solutionPath);
        }

        if (solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return DiscoverProjectsFromSln(solutionPath);
        }

        throw new NotSupportedException($"Unsupported solution format: '{solutionPath}'.");
    }

    private static IEnumerable<string> DiscoverProjectsFromSlnx(string solutionPath)
    {
        var document = XDocument.Load(solutionPath, LoadOptions.None);
        var baseDirectory = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException("Solution directory is required.");

        return document
            .Descendants("Project")
            .Select(static element => element.Attribute("Path")?.Value)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.Combine(baseDirectory, path!)))
            .Where(static path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> DiscoverProjectsFromSln(string solutionPath)
    {
        var baseDirectory = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException("Solution directory is required.");

        return File.ReadLines(solutionPath)
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("Project(", StringComparison.Ordinal))
            .Select(TryExtractProjectPath)
            .Where(static path => path is not null)
            .Select(path => Path.GetFullPath(Path.Combine(baseDirectory, path!)))
            .Where(static path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? TryExtractProjectPath(string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 2)
        {
            return null;
        }

        return parts[1].Trim().Trim('"');
    }

    private static ProjectAssets LoadProjectAssets(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException("Project directory is required.");
        var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");

        if (!File.Exists(assetsPath))
        {
            var lockPath = Path.Combine(projectDirectory, "packages.lock.json");
            if (File.Exists(lockPath))
            {
                throw new FileNotFoundException(
                    $"Lock file exists for '{projectPath}', but provider requires obj/project.assets.json to resolve package binaries.",
                    assetsPath);
            }

            throw new FileNotFoundException($"Could not find project.assets.json for '{projectPath}'.", assetsPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = document.RootElement;

        var packageFolders = root.TryGetProperty("packageFolders", out var packageFoldersElement)
            ? packageFoldersElement
                .EnumerateObject()
                .Select(property => ResolvePathRelativeTo(assetsPath, property.Name))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray()
            : [];

        var libraries = ParseLibraries(root);
        var directDependenciesByFramework = ParseDirectDependencies(root);
        var targets = ParseTargets(root, libraries, directDependenciesByFramework);

        return new ProjectAssets(
            packageFolders,
            directDependenciesByFramework.Keys.OrderBy(static framework => framework, StringComparer.Ordinal).ToArray(),
            targets);
    }

    private static IReadOnlyDictionary<string, string> BuildPackageVersionMap(ProjectAssets assets)
    {
        var packageVersions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var target in assets.Targets)
        {
            foreach (var package in target.Packages)
            {
                packageVersions[package.Identity.Name] = package.Identity.Version;
            }
        }

        return packageVersions;
    }

    private static IReadOnlyDictionary<PackageIdentity, PackageLibrary> ParseLibraries(JsonElement root)
    {
        if (!root.TryGetProperty("libraries", out var librariesElement))
        {
            return new Dictionary<PackageIdentity, PackageLibrary>(PackageIdentityComparer.Instance);
        }

        var libraries = new Dictionary<PackageIdentity, PackageLibrary>(PackageIdentityComparer.Instance);
        foreach (var property in librariesElement.EnumerateObject())
        {
            var identity = PackageIdentity.Parse(property.Name);
            var type = property.Value.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            if (!string.Equals(type, "package", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = property.Value.TryGetProperty("path", out var pathElement)
                ? pathElement.GetString() ?? string.Empty
                : string.Empty;

            libraries[identity] = new PackageLibrary(identity, path.TrimEnd('/', '\\'));
        }

        return libraries;
    }

    private static IReadOnlyDictionary<string, HashSet<string>> ParseDirectDependencies(JsonElement root)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (!root.TryGetProperty("project", out var projectElement)
            || !projectElement.TryGetProperty("frameworks", out var frameworksElement))
        {
            return result;
        }

        foreach (var frameworkProperty in frameworksElement.EnumerateObject())
        {
            var directDependencies = new HashSet<string>(StringComparer.Ordinal);
            if (frameworkProperty.Value.TryGetProperty("dependencies", out var dependenciesElement))
            {
                foreach (var dependencyProperty in dependenciesElement.EnumerateObject())
                {
                    directDependencies.Add(dependencyProperty.Name);
                }
            }

            result[frameworkProperty.Name] = directDependencies;
        }

        return result;
    }

    private static IReadOnlyList<TargetGraph> ParseTargets(
        JsonElement root,
        IReadOnlyDictionary<PackageIdentity, PackageLibrary> libraries,
        IReadOnlyDictionary<string, HashSet<string>> directDependenciesByFramework)
    {
        if (!root.TryGetProperty("targets", out var targetsElement))
        {
            return [];
        }

        var targets = new List<TargetGraph>();
        foreach (var targetProperty in targetsElement.EnumerateObject())
        {
            var frameworkMoniker = targetProperty.Name.Split('/')[0];
            var directDependencies = directDependenciesByFramework.GetValueOrDefault(
                frameworkMoniker,
                new HashSet<string>(StringComparer.Ordinal));
            var packages = new List<ResolvedPackage>();

            foreach (var packageProperty in targetProperty.Value.EnumerateObject())
            {
                var identity = PackageIdentity.Parse(packageProperty.Name);
                if (!libraries.TryGetValue(identity, out var library))
                {
                    continue;
                }

                packages.Add(
                    new ResolvedPackage(
                        identity,
                        library.RelativePath,
                        directDependencies.Contains(identity.Name),
                        ExtractDllPaths(packageProperty.Value)));
            }

            targets.Add(
                new TargetGraph(
                    frameworkMoniker,
                    packages
                        .OrderBy(static package => package.Identity.Name, StringComparer.Ordinal)
                        .ThenBy(static package => package.Identity.Version, StringComparer.Ordinal)
                        .ToArray()));
        }

        return targets.OrderBy(static target => target.FrameworkMoniker, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> ExtractDllPaths(JsonElement packageElement)
    {
        var dllPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sectionName in new[] { "runtime", "compile" })
        {
            if (!packageElement.TryGetProperty(sectionName, out var sectionElement))
            {
                continue;
            }

            foreach (var fileProperty in sectionElement.EnumerateObject())
            {
                if (fileProperty.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && fileProperty.Name.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
                {
                    dllPaths.Add(fileProperty.Name.Replace('/', Path.DirectorySeparatorChar));
                }
            }
        }

        return dllPaths.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
    }

    private static Node CreateProjectNode(string solutionDirectory, string projectPath, IReadOnlyList<string> targetFrameworks)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        return Node.Create(
            new NodeId(NodeKind.Project, projectName, "project"),
            NodeKind.Project,
            projectName,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["projectPath"] = JsonSerializer.SerializeToElement(
                    Path.GetRelativePath(solutionDirectory, projectPath).Replace('\\', '/')),
                ["targetFrameworks"] = JsonSerializer.SerializeToElement(targetFrameworks)
            },
            [
                new SourceRef(
                    Path.GetRelativePath(solutionDirectory, projectPath).Replace('\\', '/'),
                    [])
            ]);
    }

    private static Node CreatePackageNode(PackageIdentity identity, string relativePath)
    {
        return Node.Create(
            new NodeId(NodeKind.Package, identity.Name, identity.Version),
            NodeKind.Package,
            $"{identity.Name}@{identity.Version}",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["packageName"] = JsonSerializer.SerializeToElement(identity.Name),
                ["relativePath"] = JsonSerializer.SerializeToElement(relativePath.Replace('\\', '/')),
                ["version"] = JsonSerializer.SerializeToElement(identity.Version)
            });
    }

    private async Task EmitPackagesParallelAsync(
        IReadOnlyList<PackageWorkItem> packageList,
        GraphCollector collector,
        ParallelFilePackageFragmentCache cache,
        CancellationToken cancellationToken)
    {
        var parallelism = Math.Max(Environment.ProcessorCount * 2 - 1, 4);
        var total = packageList.Count;
        var processed = 0;

        var channel = Channel.CreateBounded<PackageWorkItem>(new BoundedChannelOptions(parallelism * 2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });

        var workers = Enumerable
            .Range(0, parallelism)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var package in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    var fragment = await BuildPackageFragmentAsync(package, cache, _logger, cancellationToken);
                    collector.AddFragmentThreadSafe(fragment);

                    var index = Interlocked.Increment(ref processed);
                    _logger.LogDebug(
                        "DotnetProvider: package {Index}/{Total} — {PackageName}@{PackageVersion}.",
                        index,
                        total,
                        package.Identity.Name,
                        package.Identity.Version);
                }
            }, cancellationToken))
            .ToArray();

        foreach (var package in packageList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await channel.Writer.WriteAsync(package, cancellationToken);
        }

        channel.Writer.Complete();
        await Task.WhenAll(workers);
    }

    private static async Task<GraphFragment> BuildPackageFragmentAsync(
        PackageWorkItem package,
        IPackageFragmentCache cache,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var cachedFragment = await cache.TryGetAsync(package.Identity.Name, package.Identity.Version, cancellationToken);
        if (cachedFragment is not null)
        {
            logger.LogDebug(
                "DotnetProvider: cache hit for {PackageName}@{PackageVersion}.",
                package.Identity.Name,
                package.Identity.Version);
            return cachedFragment;
        }

        logger.LogInformation(
            "DotnetProvider: cache miss — reflecting {PackageName}@{PackageVersion}.",
            package.Identity.Name,
            package.Identity.Version);

        var packageNodeId = new NodeId(NodeKind.Package, package.Identity.Name, package.Identity.Version);
        var packageCollector = new GraphCollector();

        foreach (var dllPath in ResolveAssemblyPaths(package))
        {
            var assemblyName = AssemblyName.GetAssemblyName(dllPath);
            var assemblyNode = Node.Create(
                new NodeId(NodeKind.Assembly, assemblyName.Name ?? Path.GetFileNameWithoutExtension(dllPath), package.Identity.Version),
                NodeKind.Assembly,
                $"{assemblyName.Name}@{package.Identity.Version}",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["assemblyVersion"] = JsonSerializer.SerializeToElement(assemblyName.Version?.ToString() ?? "0.0.0.0"),
                    ["fileName"] = JsonSerializer.SerializeToElement(Path.GetFileName(dllPath))
                });

            packageCollector.AddNode(assemblyNode);
            packageCollector.AddContainsEdge(packageNodeId, assemblyNode.Id);

            var loadContext = new NuGetAssemblyLoadContext(package.PackageFolders);
            try
            {
                var assembly = loadContext.LoadFromAssemblyPath(dllPath);
                EmitAssemblyTypes(package.Identity.Version, assemblyName.Name ?? string.Empty, assemblyNode.Id, assembly, packageCollector);
            }
            finally
            {
                loadContext.Unload();
            }
        }

        var packageFragment = packageCollector.ToFragment(ProviderId);
        await cache.SetAsync(package.Identity.Name, package.Identity.Version, packageFragment, cancellationToken);
        logger.LogDebug(
            "DotnetProvider: reflected and cached {PackageName}@{PackageVersion}. Nodes: {NodeCount}.",
            package.Identity.Name,
            package.Identity.Version,
            packageFragment.Nodes.Count);

        return packageFragment;
    }

    private static IEnumerable<string> ResolveAssemblyPaths(PackageWorkItem package)
    {
        foreach (var packageFolder in package.PackageFolders.OrderBy(static path => path, StringComparer.Ordinal))
        {
            var packageRoot = Path.Combine(packageFolder, package.RelativePath);
            foreach (var dllPath in package.DllPaths.OrderBy(static path => path, StringComparer.Ordinal))
            {
                var resolvedPath = Path.GetFullPath(Path.Combine(packageRoot, dllPath));
                if (File.Exists(resolvedPath))
                {
                    yield return resolvedPath;
                }
            }
        }
    }

    private static void EmitAssemblyTypes(
        string packageVersion,
        string assemblyName,
        NodeId assemblyNodeId,
        Assembly assembly,
        GraphCollector collector)
    {
        Type[] rawExportedTypes;
        try
        {
            rawExportedTypes = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            rawExportedTypes = ex.Types.Where(static t => t is not null).ToArray()!;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException)
        {
            rawExportedTypes = [];
        }

        var exportedTypes = rawExportedTypes
            .Where(static type => !HasCompilerGeneratedMarker(type))
            .OrderBy(static type => GetTypeIdentifier(type), StringComparer.Ordinal)
            .ToArray();

        var knownTypeIds = new Dictionary<string, NodeId>(StringComparer.Ordinal);
        var namespaceIds = new Dictionary<string, NodeId>(StringComparer.Ordinal);

        foreach (var type in exportedTypes)
        {
            var namespaceName = type.Namespace ?? "<global>";
            if (!namespaceIds.TryGetValue(namespaceName, out var namespaceId))
            {
                namespaceId = new NodeId(NodeKind.Namespace, namespaceName, packageVersion);
                namespaceIds[namespaceName] = namespaceId;
                collector.AddNode(
                    Node.Create(
                        namespaceId,
                        NodeKind.Namespace,
                        $"{namespaceName}@{packageVersion}",
                        new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                        {
                            ["assemblyName"] = JsonSerializer.SerializeToElement(assemblyName)
                        }));
                collector.AddContainsEdge(assemblyNodeId, namespaceId);
            }

            var typeIdentifier = GetTypeIdentifier(type);
            var typeNode = Node.Create(
                new NodeId(NodeKind.Type, typeIdentifier, packageVersion),
                NodeKind.Type,
                $"{typeIdentifier}@{packageVersion}",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["assemblyName"] = JsonSerializer.SerializeToElement(assemblyName),
                    ["kind"] = JsonSerializer.SerializeToElement(GetTypeKind(type)),
                    ["namespace"] = JsonSerializer.SerializeToElement(namespaceName)
                });

            knownTypeIds[typeIdentifier] = typeNode.Id;
            collector.AddNode(typeNode);
            collector.AddContainsEdge(namespaceId, typeNode.Id);
        }

        foreach (var type in exportedTypes)
        {
            var typeIdentifier = GetTypeIdentifier(type);
            var typeNodeId = knownTypeIds[typeIdentifier];

            if (type.BaseType is not null && type.BaseType != typeof(object))
            {
                EmitTypeRelationship(typeNodeId, type.BaseType, EdgeKind.Inherits, packageVersion, knownTypeIds, collector);
            }

            var inheritedInterfaces = type.BaseType?.GetInterfaces() ?? [];
            foreach (var implementedInterface in type.GetInterfaces().Except(inheritedInterfaces))
            {
                EmitTypeRelationship(typeNodeId, implementedInterface, EdgeKind.Implements, packageVersion, knownTypeIds, collector);
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName || HasCompilerGeneratedMarker(method))
                {
                    continue;
                }

                string methodIdentifier;
                string methodSignature;
                try
                {
                    methodIdentifier = GetMethodIdentifier(typeIdentifier, method);
                    methodSignature = GetMethodSignature(typeIdentifier, method);
                }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException)
                {
                    continue;
                }

                collector.AddNode(
                    Node.Create(
                        new NodeId(NodeKind.Method, methodIdentifier, packageVersion),
                        NodeKind.Method,
                        $"{methodIdentifier}@{packageVersion}",
                        new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                        {
                            ["declaringType"] = JsonSerializer.SerializeToElement(typeIdentifier),
                            ["signature"] = JsonSerializer.SerializeToElement(methodSignature)
                        }));
                collector.AddContainsEdge(typeNodeId, new NodeId(NodeKind.Method, methodIdentifier, packageVersion));
            }
        }
    }

    private static void EmitTypeRelationship(
        NodeId sourceTypeId,
        Type targetType,
        EdgeKind kind,
        string packageVersion,
        IReadOnlyDictionary<string, NodeId> knownTypeIds,
        GraphCollector collector)
    {
        var targetIdentifier = GetTypeIdentifier(targetType);
        if (knownTypeIds.TryGetValue(targetIdentifier, out var knownTargetId))
        {
            collector.AddEdge(Edge.Create(sourceTypeId, knownTargetId, kind));
            return;
        }

        var externalNode = Node.Create(
            new NodeId(NodeKind.ExternalRef, targetIdentifier, "external"),
            NodeKind.ExternalRef,
            targetIdentifier,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["packageVersion"] = JsonSerializer.SerializeToElement(packageVersion)
            });

        collector.AddNode(externalNode);
        collector.AddEdge(Edge.Create(sourceTypeId, externalNode.Id, kind));
    }

    private static string GetTypeIdentifier(Type type)
    {
        return (type.FullName ?? type.Name).Replace('+', '.');
    }

    private static string GetTypeKind(Type type)
    {
        if (type.IsInterface)
        {
            return "interface";
        }

        if (type.IsEnum)
        {
            return "enum";
        }

        if (typeof(MulticastDelegate).IsAssignableFrom(type.BaseType))
        {
            return "delegate";
        }

        if (type.IsValueType)
        {
            return "struct";
        }

        return "class";
    }

    private static bool HasCompilerGeneratedMarker(MemberInfo member)
    {
        try
        {
            return member.GetCustomAttributesData()
                .Any(static attribute => string.Equals(
                    attribute.AttributeType.FullName,
                    typeof(CompilerGeneratedAttribute).FullName,
                    StringComparison.Ordinal));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string GetMethodIdentifier(string typeIdentifier, MethodInfo method)
    {
        var parameters = method.GetParameters()
            .Select(static parameter => GetFriendlyTypeName(parameter.ParameterType))
            .ToArray();
        return $"{typeIdentifier}.{method.Name}({string.Join(",", parameters)})";
    }

    private static string GetMethodSignature(string typeIdentifier, MethodInfo method)
    {
        var parameters = method.GetParameters()
            .Select(static parameter => $"{GetFriendlyTypeName(parameter.ParameterType)} {parameter.Name}")
            .ToArray();
        return $"{GetFriendlyTypeName(method.ReturnType)} {typeIdentifier}.{method.Name}({string.Join(", ", parameters)})";
    }

    private static string GetFriendlyTypeName(Type type)
    {
        if (type == typeof(void))
        {
            return "void";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(int))
        {
            return "int";
        }

        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type.IsGenericType)
        {
            var genericTypeName = type.GetGenericTypeDefinition().FullName ?? type.Name;
            genericTypeName = genericTypeName[..genericTypeName.IndexOf('`')];
            var arguments = type.GetGenericArguments().Select(GetFriendlyTypeName);
            return $"{genericTypeName}<{string.Join(", ", arguments)}>";
        }

        return GetTypeIdentifier(type);
    }

    private static string ResolvePathRelativeTo(string anchorPath, string candidatePath)
    {
        if (Path.IsPathRooted(candidatePath))
        {
            return Path.GetFullPath(candidatePath);
        }

        var baseDirectory = Path.GetDirectoryName(anchorPath)
            ?? throw new InvalidOperationException("Anchor directory is required.");
        return Path.GetFullPath(Path.Combine(baseDirectory, candidatePath));
    }

    private sealed class GraphCollector
    {
        private readonly Dictionary<NodeId, Node> _nodes = new();
        private readonly Dictionary<string, Edge> _edges = new(StringComparer.Ordinal);
        private readonly Lock _lock = new();

        public void AddNode(Node node)
        {
            _nodes[node.Id] = node;
        }

        public void AddContainsEdge(NodeId fromId, NodeId toId)
        {
            if (!string.Equals(fromId.VersionToken, toId.VersionToken, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Contains edge crosses version boundary: {fromId} -> {toId}.");
            }

            AddEdge(Edge.Create(fromId, toId, EdgeKind.Contains));
        }

        public void AddEdge(Edge edge)
        {
            _edges[CreateEdgeKey(edge)] = edge;
        }

        public void AddFragment(GraphFragment fragment)
        {
            foreach (var node in fragment.Nodes)
            {
                AddNode(node);
            }

            foreach (var edge in fragment.Edges)
            {
                AddEdge(edge);
            }
        }

        public void AddFragmentThreadSafe(GraphFragment fragment)
        {
            lock (_lock)
            {
                AddFragment(fragment);
            }
        }

        public GraphFragment ToFragment(string providerId)
        {
            return new GraphFragment(
                providerId,
                _nodes.Values.OrderBy(static node => node.Id.ToString(), StringComparer.Ordinal).ToArray(),
                _edges.Values
                    .OrderBy(static edge => edge.FromId.ToString(), StringComparer.Ordinal)
                    .ThenBy(static edge => edge.ToId.ToString(), StringComparer.Ordinal)
                    .ThenBy(static edge => edge.Kind.ToString(), StringComparer.Ordinal)
                    .ToArray());
        }

        private static string CreateEdgeKey(Edge edge)
        {
            return $"{edge.FromId}|{edge.ToId}|{edge.Kind}|{string.Join(";", edge.Attributes.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => pair.Key + '=' + pair.Value.GetRawText()))}";
        }
    }

    private sealed record ProjectAssets(
        IReadOnlyList<string> PackageFolders,
        IReadOnlyList<string> TargetFrameworks,
        IReadOnlyList<TargetGraph> Targets);

    private sealed record TargetGraph(string FrameworkMoniker, IReadOnlyList<ResolvedPackage> Packages);

    private sealed record ResolvedPackage(
        PackageIdentity Identity,
        string RelativePath,
        bool IsDirect,
        IReadOnlyList<string> DllPaths);

    private sealed record PackageLibrary(PackageIdentity Identity, string RelativePath);

    private sealed class PackageWorkItem
    {
        public PackageWorkItem(PackageIdentity identity, string relativePath)
        {
            Identity = identity;
            RelativePath = relativePath;
        }

        public PackageIdentity Identity { get; }

        public string RelativePath { get; }

        public HashSet<string> PackageFolders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> DllPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct PackageIdentity(string Name, string Version)
    {
        public static PackageIdentity Parse(string value)
        {
            var separator = value.LastIndexOf('/');
            if (separator <= 0 || separator == value.Length - 1)
            {
                throw new FormatException($"Invalid package identity '{value}'.");
            }

            return new PackageIdentity(value[..separator], value[(separator + 1)..]);
        }
    }

    private sealed class NuGetAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly IReadOnlyCollection<string> _packageFolders;

        public NuGetAssemblyLoadContext(IReadOnlyCollection<string> packageFolders)
            : base($"dotnet-provider-{Guid.NewGuid():N}", isCollectible: true)
        {
            _packageFolders = packageFolders;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is null)
            {
                return null;
            }

            foreach (var folder in _packageFolders)
            {
                foreach (var dll in Directory.EnumerateFiles(folder, $"{assemblyName.Name}.dll", SearchOption.AllDirectories))
                {
                    try
                    {
                        var candidate = AssemblyName.GetAssemblyName(dll);
                        if (string.Equals(candidate.Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            return LoadFromAssemblyPath(dll);
                        }
                    }
                    catch (BadImageFormatException)
                    {
                        // not a managed assembly
                    }
                }
            }

            return null;
        }
    }

    private sealed class PackageIdentityComparer : IEqualityComparer<PackageIdentity>
    {
        public static PackageIdentityComparer Instance { get; } = new();

        public bool Equals(PackageIdentity x, PackageIdentity y)
        {
            return string.Equals(x.Name, y.Name, StringComparison.Ordinal)
                   && string.Equals(x.Version, y.Version, StringComparison.Ordinal);
        }

        public int GetHashCode(PackageIdentity obj)
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Name),
                StringComparer.Ordinal.GetHashCode(obj.Version));
        }
    }
}
