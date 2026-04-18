using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Providers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpDllGraph.Providers.Dotnet.Http;

public sealed class MinimalApiEndpointProvider : IGraphProvider
{
    private static readonly Dictionary<string, string> HttpMethodByMapName = new(StringComparer.Ordinal)
    {
        ["MapGet"] = "GET",
        ["MapPost"] = "POST",
        ["MapPut"] = "PUT",
        ["MapDelete"] = "DELETE",
        ["MapPatch"] = "PATCH"
    };

    public string Id => "dotnet-minimal-api";

    public async IAsyncEnumerable<GraphFragment> CollectAsync(
        GraphBuildContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        RoslynBootstrap.EnsureRegistered();

        var solutionPath = context.SolutionPath;
        var solutionDirectory = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException("Solution directory is required.");

        var nodes = new Dictionary<NodeId, Node>();
        var edges = new Dictionary<string, Edge>(StringComparer.Ordinal);

        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        foreach (var projectPath in DiscoverProjectPaths(solutionPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var projectId = ProjectId.CreateNewId(projectName);
            var metadataReferences = LoadMetadataReferences(projectPath);
            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                projectName,
                projectName,
                LanguageNames.CSharp,
                filePath: projectPath,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: CSharpParseOptions.Default,
                metadataReferences: metadataReferences);

            solution = solution.AddProject(projectInfo);

            foreach (var documentPath in DiscoverDocumentPaths(projectPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceText = SourceText.From(await File.ReadAllTextAsync(documentPath, cancellationToken));
                solution = solution.AddDocument(
                    DocumentId.CreateNewId(projectId, documentPath),
                    Path.GetFileName(documentPath),
                    sourceText,
                    filePath: documentPath);
            }
        }

        workspace.TryApplyChanges(solution);

        foreach (var project in workspace.CurrentSolution.Projects.Where(static p => p.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                continue;
            }

            var projectVersionToken = GetProjectVersionToken(solutionDirectory, project.FilePath, project.Name);

            foreach (var document in project.Documents.Where(static d => d.SourceCodeKind == SourceCodeKind.Regular))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
                if (syntaxTree is null)
                {
                    continue;
                }

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel is null)
                {
                    continue;
                }

                var root = await syntaxTree.GetRootAsync(cancellationToken);
                var walker = new MinimalApiWalker(semanticModel, projectVersionToken, solutionDirectory, nodes, edges);
                walker.Visit(root);
            }
        }

        await Task.Yield();
        yield return new GraphFragment(
            Id,
            nodes.Values.OrderBy(static n => n.Id.ToString(), StringComparer.Ordinal).ToArray(),
            edges.Values
                .OrderBy(static e => e.FromId.ToString(), StringComparer.Ordinal)
                .ThenBy(static e => e.ToId.ToString(), StringComparer.Ordinal)
                .ThenBy(static e => e.Kind.ToString(), StringComparer.Ordinal)
                .ToArray());
    }

    private static IEnumerable<string> DiscoverProjectPaths(string solutionPath)
    {
        if (solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
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

        if (solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
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

        throw new NotSupportedException($"Unsupported solution format: '{solutionPath}'.");
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

    private static IEnumerable<string> DiscoverDocumentPaths(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException("Project directory is required.");

        return Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsExcludedDirectory(path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsExcludedDirectory(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MetadataReference> LoadMetadataReferences(string projectPath)
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            foreach (var assembly in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                references[assembly] = MetadataReference.CreateFromFile(assembly);
            }
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException("Project directory is required.");
        var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");

        if (!File.Exists(assetsPath))
        {
            return references.Values.ToArray();
        }

        using var assetsDocument = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = assetsDocument.RootElement;

        var packageFolders = root.TryGetProperty("packageFolders", out var packageFoldersElement)
            ? packageFoldersElement.EnumerateObject()
                .Select(property => ResolvePathRelativeTo(assetsPath, property.Name))
                .ToArray()
            : [];

        var libraries = root.TryGetProperty("libraries", out var librariesElement)
            ? librariesElement.EnumerateObject()
                .ToDictionary(
                    static property => property.Name,
                    static property => property.Value.GetProperty("path").GetString() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty("targets", out var targetsElement))
        {
            return references.Values.ToArray();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targetsElement.EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!libraries.TryGetValue(library.Name, out var libraryPath))
                {
                    continue;
                }

                foreach (var sectionName in new[] { "compile", "runtime" })
                {
                    if (!library.Value.TryGetProperty(sectionName, out var section))
                    {
                        continue;
                    }

                    foreach (var file in section.EnumerateObject())
                    {
                        if (!file.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        foreach (var packageFolder in packageFolders)
                        {
                            var resolvedPath = Path.GetFullPath(Path.Combine(packageFolder, libraryPath, file.Name.Replace('/', Path.DirectorySeparatorChar)));
                            if (File.Exists(resolvedPath) && seen.Add(resolvedPath))
                            {
                                references[resolvedPath] = MetadataReference.CreateFromFile(resolvedPath);
                            }
                        }
                    }
                }
            }
        }

        return references.Values.ToArray();
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

    private static string GetProjectVersionToken(string solutionDirectory, string? projectPath, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return $"project-{projectName}";
        }

        var relativePath = Path.GetRelativePath(solutionDirectory, projectPath).Replace('\\', '/');
        var versionPath = relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? relativePath[..^7]
            : relativePath;

        return $"project-{versionPath}";
    }

    private sealed class MinimalApiWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly string _projectVersionToken;
        private readonly string _solutionDirectory;
        private readonly Dictionary<NodeId, Node> _nodes;
        private readonly Dictionary<string, Edge> _edges;

        public MinimalApiWalker(
            SemanticModel semanticModel,
            string projectVersionToken,
            string solutionDirectory,
            Dictionary<NodeId, Node> nodes,
            Dictionary<string, Edge> edges)
        {
            _semanticModel = semanticModel;
            _projectVersionToken = projectVersionToken;
            _solutionDirectory = solutionDirectory;
            _nodes = nodes;
            _edges = edges;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (TryExtractMapCall(node, out var httpMethod, out var routeTemplate, out var confidence, out var handlerArg))
            {
                var normalizedTemplate = NormalizeRouteTemplate(routeTemplate);
                var endpointFqn = $"{httpMethod} {normalizedTemplate}";

                var endpointAttributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["httpMethod"] = JsonSerializer.SerializeToElement(httpMethod),
                    ["routeTemplate"] = JsonSerializer.SerializeToElement(normalizedTemplate),
                    ["confidence"] = JsonSerializer.SerializeToElement(confidence),
                    ["isMinimalApi"] = JsonSerializer.SerializeToElement(true)
                };

                var endpointSourceRef = CreateSourceRef(node.GetLocation());
                var endpointNodeId = new NodeId(NodeKind.HttpEndpoint, endpointFqn, _projectVersionToken);
                var endpointNode = Node.Create(
                    endpointNodeId,
                    NodeKind.HttpEndpoint,
                    $"{httpMethod} {normalizedTemplate}",
                    endpointAttributes,
                    [endpointSourceRef]);

                _nodes[endpointNodeId] = endpointNode;

                var handlerNodeId = ResolveOrSynthesizeHandlerNode(handlerArg);
                if (handlerNodeId.HasValue)
                {
                    var handlerEdge = Edge.Create(handlerNodeId.Value, endpointNodeId, EdgeKind.HandlesRoute);
                    _edges[EdgeKey(handlerEdge)] = handlerEdge;
                }
            }

            base.VisitInvocationExpression(node);
        }

        private bool TryExtractMapCall(
            InvocationExpressionSyntax node,
            out string httpMethod,
            out string routeTemplate,
            out string confidence,
            out ArgumentSyntax? handlerArg)
        {
            httpMethod = string.Empty;
            routeTemplate = string.Empty;
            confidence = "high";
            handlerArg = null;

            if (node.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var methodName = memberAccess.Name.Identifier.Text;
            if (!HttpMethodByMapName.TryGetValue(methodName, out var resolvedHttpMethod))
            {
                return false;
            }

            var args = node.ArgumentList.Arguments;
            if (args.Count < 2)
            {
                return false;
            }

            httpMethod = resolvedHttpMethod;
            handlerArg = args[1];

            var routeArg = args[0].Expression;
            var constantValue = _semanticModel.GetConstantValue(routeArg);
            if (constantValue.HasValue && constantValue.Value is string literalRoute)
            {
                routeTemplate = literalRoute;
                confidence = "high";
            }
            else
            {
                routeTemplate = "unknown";
                confidence = "low";
            }

            return true;
        }

        private NodeId? ResolveOrSynthesizeHandlerNode(ArgumentSyntax? handlerArg)
        {
            if (handlerArg is null)
            {
                return null;
            }

            var expression = handlerArg.Expression;

            // Lambda expressions: synthesize a Method node keyed by file + line
            if (expression is LambdaExpressionSyntax lambda)
            {
                return SynthesizeLambdaNode(lambda);
            }

            // Method group: resolve via semantic model
            var symbolInfo = _semanticModel.GetSymbolInfo(expression);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
            {
                return EmitMethodNode(methodSymbol);
            }

            // Candidate symbols (method group with multiple overloads — take first)
            if (symbolInfo.CandidateSymbols.Length > 0 && symbolInfo.CandidateSymbols[0] is IMethodSymbol candidateMethod)
            {
                return EmitMethodNode(candidateMethod);
            }

            return null;
        }

        private NodeId SynthesizeLambdaNode(LambdaExpressionSyntax lambda)
        {
            var location = lambda.GetLocation();
            var lineSpan = location.GetLineSpan();
            var filePath = location.SourceTree?.FilePath ?? string.Empty;
            var fileKey = BuildFileKey(filePath);
            var line = lineSpan.StartLinePosition.Line + 1;

            var lambdaFqn = $"lambda@{fileKey}:{line}";
            var nodeId = new NodeId(NodeKind.Method, lambdaFqn, _projectVersionToken);

            var sourceRef = CreateSourceRef(location);
            var lambdaNode = Node.Create(
                nodeId,
                NodeKind.Method,
                $"{lambdaFqn}@{_projectVersionToken}",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["origin"] = JsonSerializer.SerializeToElement("lambda"),
                    ["projectVersion"] = JsonSerializer.SerializeToElement(_projectVersionToken)
                },
                [sourceRef]);

            _nodes[nodeId] = lambdaNode;
            return nodeId;
        }

        private NodeId EmitMethodNode(IMethodSymbol methodSymbol)
        {
            var definition = methodSymbol.OriginalDefinition;
            var methodIdentifier = GetMethodIdentifier(definition);
            var nodeId = new NodeId(NodeKind.Method, methodIdentifier, _projectVersionToken);

            var sourceRefs = BuildSourceRefs(definition.Locations);
            var methodNode = Node.Create(
                nodeId,
                NodeKind.Method,
                $"{methodIdentifier}@{_projectVersionToken}",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["origin"] = JsonSerializer.SerializeToElement("user"),
                    ["projectVersion"] = JsonSerializer.SerializeToElement(_projectVersionToken)
                },
                sourceRefs);

            _nodes[nodeId] = methodNode;
            return nodeId;
        }

        private static string GetMethodIdentifier(IMethodSymbol method)
        {
            var typeIdentifier = method.ContainingType.OriginalDefinition
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty, StringComparison.Ordinal);
            var methodName = method.MethodKind == MethodKind.Constructor ? ".ctor" : method.Name;
            var parameters = method.Parameters.Select(static p => GetFriendlyTypeName(p.Type)).ToArray();
            return $"{typeIdentifier}.{methodName}({string.Join(",", parameters)})";
        }

        private static string GetFriendlyTypeName(ITypeSymbol type)
        {
            return type switch
            {
                { SpecialType: SpecialType.System_Void } => "void",
                { SpecialType: SpecialType.System_String } => "string",
                { SpecialType: SpecialType.System_Int32 } => "int",
                { SpecialType: SpecialType.System_Boolean } => "bool",
                INamedTypeSymbol namedType when namedType.IsGenericType =>
                    $"{TrimGenericArity(namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal))}<{string.Join(", ", namedType.TypeArguments.Select(GetFriendlyTypeName))}>",
                IArrayTypeSymbol arrayType => $"{GetFriendlyTypeName(arrayType.ElementType)}[]",
                _ => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal)
            };
        }

        private static string TrimGenericArity(string value)
        {
            var index = value.IndexOf('`');
            return index < 0 ? value : value[..index];
        }

        private static IReadOnlyList<SourceRef> BuildSourceRefs(IReadOnlyList<Location> locations)
        {
            var sourceRefs = new Dictionary<string, List<SourceSpan>>(StringComparer.Ordinal);

            foreach (var location in locations.Where(static l => l.IsInSource))
            {
                var sourceRef = CreateSourceRef(location);
                if (!sourceRefs.TryGetValue(sourceRef.File, out var spans))
                {
                    spans = new List<SourceSpan>();
                    sourceRefs[sourceRef.File] = spans;
                }

                spans.AddRange(sourceRef.Spans);
            }

            return sourceRefs
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new SourceRef(
                    pair.Key,
                    pair.Value
                        .Distinct()
                        .OrderBy(static span => span.StartLine)
                        .ThenBy(static span => span.StartColumn)
                        .ThenBy(static span => span.EndLine)
                        .ThenBy(static span => span.EndColumn)
                        .ToArray()))
                .ToArray();
        }

        private static SourceRef CreateSourceRef(Location location)
        {
            var span = location.GetLineSpan();
            var file = location.SourceTree?.FilePath ?? string.Empty;
            return new SourceRef(
                file,
                [
                    new SourceSpan(
                        span.StartLinePosition.Line + 1,
                        span.StartLinePosition.Character + 1,
                        span.EndLinePosition.Line + 1,
                        span.EndLinePosition.Character + 1)
                ]);
        }

        private string BuildFileKey(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return "unknown";
            }

            var relative = Path.GetRelativePath(_solutionDirectory, filePath).Replace('\\', '/');
            var bytes = Encoding.UTF8.GetBytes(relative);
            var hashBytes = SHA256.HashData(bytes);
            var hash = BitConverter.ToUInt32(hashBytes, 0);
            return $"{Path.GetFileNameWithoutExtension(filePath)}-{hash:x8}";
        }

        private static string NormalizeRouteTemplate(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return "/";
            }

            // Ensure leading slash
            if (!template.StartsWith('/'))
            {
                template = "/" + template;
            }

            // Strip route constraints: {id:int} -> {id}, {name:maxlength(50)} -> {name}
            template = Regex.Replace(template, @"\{(\w+):[^}]+\}", "{$1}");

            return template;
        }

        private static string EdgeKey(Edge edge)
        {
            return $"{edge.FromId}|{edge.ToId}|{edge.Kind}";
        }
    }
}
