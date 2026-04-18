using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Http;
using CSharpDllGraph.Engine.Providers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpDllGraph.Providers.Dotnet.Http;

public sealed class HttpClientCallSiteProvider : IGraphProvider
{
    public string Id => "dotnet-http-callsite";

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
        var edges = new List<Edge>();

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
                var walker = new HttpCallSiteWalker(semanticModel, projectVersionToken, nodes, edges);
                walker.Visit(root);
            }
        }

        await Task.Yield();
        yield return new GraphFragment(
            Id,
            nodes.Values.OrderBy(static n => n.Id.ToString(), StringComparer.Ordinal).ToArray(),
            edges
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

    private sealed class HttpCallSiteWalker : CSharpSyntaxWalker
    {
        private static readonly HashSet<string> HttpAsyncMethods = new(StringComparer.Ordinal)
        {
            "GetAsync",
            "PostAsync",
            "PutAsync",
            "DeleteAsync",
            "PatchAsync",
            "SendAsync"
        };

        private readonly SemanticModel _semanticModel;
        private readonly string _projectVersionToken;
        private readonly Dictionary<NodeId, Node> _nodes;
        private readonly List<Edge> _edges;
        private readonly Stack<(NodeId NodeId, string Identifier)> _sourceNodes = new();

        public HttpCallSiteWalker(
            SemanticModel semanticModel,
            string projectVersionToken,
            Dictionary<NodeId, Node> nodes,
            List<Edge> edges)
        {
            _semanticModel = semanticModel;
            _projectVersionToken = projectVersionToken;
            _nodes = nodes;
            _edges = edges;
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            VisitTypeDeclaration(node, base.VisitClassDeclaration);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            VisitTypeDeclaration(node, base.VisitStructDeclaration);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            VisitTypeDeclaration(node, base.VisitInterfaceDeclaration);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            VisitTypeDeclaration(node, base.VisitRecordDeclaration);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            VisitMethodDeclarationCore(
                node,
                base.VisitMethodDeclaration);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            VisitConstructorDeclarationCore(
                node,
                base.VisitConstructorDeclaration);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            TryEmitHttpCallSite(node);
            base.VisitInvocationExpression(node);
        }

        private void VisitTypeDeclaration<TSyntax>(TSyntax node, Action<TSyntax> visitChildren)
            where TSyntax : TypeDeclarationSyntax
        {
            if (_semanticModel.GetDeclaredSymbol(node) is INamedTypeSymbol typeSymbol)
            {
                var identifier = GetTypeIdentifier(typeSymbol);
                var nodeId = new NodeId(NodeKind.Type, identifier, _projectVersionToken);
                _sourceNodes.Push((nodeId, identifier));
                try
                {
                    visitChildren(node);
                }
                finally
                {
                    _sourceNodes.Pop();
                }

                return;
            }

            visitChildren(node);
        }

        private void VisitMethodDeclarationCore(
            MethodDeclarationSyntax node,
            Action<MethodDeclarationSyntax> visitChildren)
        {
            if (_semanticModel.GetDeclaredSymbol(node) is IMethodSymbol methodSymbol)
            {
                var identifier = GetMethodIdentifier(methodSymbol);
                var nodeId = new NodeId(NodeKind.Method, identifier, _projectVersionToken);
                _sourceNodes.Push((nodeId, identifier));
                try
                {
                    visitChildren(node);
                }
                finally
                {
                    _sourceNodes.Pop();
                }

                return;
            }

            visitChildren(node);
        }

        private void VisitConstructorDeclarationCore(
            ConstructorDeclarationSyntax node,
            Action<ConstructorDeclarationSyntax> visitChildren)
        {
            if (_semanticModel.GetDeclaredSymbol(node) is IMethodSymbol methodSymbol)
            {
                var identifier = GetMethodIdentifier(methodSymbol);
                var nodeId = new NodeId(NodeKind.Method, identifier, _projectVersionToken);
                _sourceNodes.Push((nodeId, identifier));
                try
                {
                    visitChildren(node);
                }
                finally
                {
                    _sourceNodes.Pop();
                }

                return;
            }

            visitChildren(node);
        }

        private void TryEmitHttpCallSite(InvocationExpressionSyntax node)
        {
            if (node.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return;
            }

            var methodName = memberAccess.Name.Identifier.Text;
            if (!HttpAsyncMethods.Contains(methodName))
            {
                return;
            }

            var httpMethod = MapToHttpMethod(methodName);
            var args = node.ArgumentList.Arguments;

            string urlTemplate;
            string confidence;

            if (methodName == "SendAsync")
            {
                (urlTemplate, confidence) = ExtractUrlFromSendAsync(args);
            }
            else
            {
                (urlTemplate, confidence) = args.Count > 0
                    ? NormalizeUrlExpression(args[0].Expression)
                    : ("unknown", "low");
            }

            var clientName = TryResolveNamedClient(memberAccess.Expression);
            var callingMethodIdentifier = _sourceNodes.Count > 0 ? _sourceNodes.Peek().Identifier : null;
            var location = node.GetLocation();
            var sourceRef = CreateSourceRef(location);

            var sourceHash = ComputeShortHash(sourceRef.File + ":" + location.GetLineSpan().StartLinePosition.Line);
            var fqn = $"{httpMethod} {urlTemplate}@{sourceHash}";

            var nodeId = new NodeId(NodeKind.HttpCallSite, fqn, _projectVersionToken);

            var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["httpMethod"] = JsonSerializer.SerializeToElement(httpMethod),
                ["urlTemplate"] = JsonSerializer.SerializeToElement(urlTemplate),
                ["confidence"] = JsonSerializer.SerializeToElement(confidence)
            };

            if (callingMethodIdentifier is not null)
            {
                attributes["callingMethod"] = JsonSerializer.SerializeToElement(callingMethodIdentifier);
            }

            if (clientName is not null)
            {
                attributes["clientName"] = JsonSerializer.SerializeToElement(clientName);
            }

            var callSiteNode = Node.Create(
                nodeId,
                NodeKind.HttpCallSite,
                $"{httpMethod} {urlTemplate}",
                attributes,
                [sourceRef]);

            _nodes[nodeId] = callSiteNode;

            if (_sourceNodes.Count > 0)
            {
                var callerNodeId = _sourceNodes.Peek().NodeId;
                _edges.Add(Edge.Create(callerNodeId, nodeId, EdgeKind.CallsRoute, null, [sourceRef]));
            }
        }

        private (string urlTemplate, string confidence) ExtractUrlFromSendAsync(
            SeparatedSyntaxList<ArgumentSyntax> args)
        {
            if (args.Count == 0)
            {
                return ("unknown", "low");
            }

            var firstArg = args[0].Expression;

            if (firstArg is ObjectCreationExpressionSyntax creation)
            {
                return ExtractUrlFromHttpRequestMessageCreation(creation);
            }

            if (firstArg is IdentifierNameSyntax identifier)
            {
                var symbol = _semanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol is ILocalSymbol localSymbol)
                {
                    var declaringNode = localSymbol.DeclaringSyntaxReferences
                        .Select(static r => r.GetSyntax())
                        .OfType<VariableDeclaratorSyntax>()
                        .FirstOrDefault();

                    if (declaringNode?.Initializer?.Value is ObjectCreationExpressionSyntax initCreation)
                    {
                        return ExtractUrlFromHttpRequestMessageCreation(initCreation);
                    }
                }
            }

            return ("unknown", "low");
        }

        private (string urlTemplate, string confidence) ExtractUrlFromHttpRequestMessageCreation(
            ObjectCreationExpressionSyntax creation)
        {
            // new HttpRequestMessage(HttpMethod.Get, "/api/users") — URL is the second argument
            if (creation.ArgumentList is not null && creation.ArgumentList.Arguments.Count >= 2)
            {
                return NormalizeUrlExpression(creation.ArgumentList.Arguments[1].Expression);
            }

            return ("unknown", "low");
        }

        private (string urlTemplate, string confidence) NormalizeUrlExpression(ExpressionSyntax expression)
        {
            return expression switch
            {
                LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression)
                    => (literal.Token.ValueText, "high"),
                InterpolatedStringExpressionSyntax interpolated
                    => NormalizeInterpolatedString(interpolated),
                BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression)
                    => NormalizeConcatenation(binary),
                InvocationExpressionSyntax invocation
                    => TryNormalizeStringFormat(invocation),
                MemberAccessExpressionSyntax memberAccess
                    => TryEvaluateConstant(memberAccess),
                IdentifierNameSyntax identifier
                    => TryEvaluateIdentifier(identifier),
                _ => ("unknown", "low")
            };
        }

        private static (string urlTemplate, string confidence) NormalizeInterpolatedString(
            InterpolatedStringExpressionSyntax interpolated)
        {
            var builder = new StringBuilder();
            foreach (var content in interpolated.Contents)
            {
                switch (content)
                {
                    case InterpolatedStringTextSyntax text:
                        builder.Append(text.TextToken.ValueText);
                        break;
                    case InterpolationSyntax:
                        builder.Append("{var}");
                        break;
                }
            }

            return (builder.ToString(), "high");
        }

        private (string urlTemplate, string confidence) NormalizeConcatenation(BinaryExpressionSyntax binary)
        {
            var (leftTemplate, leftConfidence) = NormalizeUrlExpression(binary.Left);
            var (rightTemplate, rightConfidence) = NormalizeUrlExpression(binary.Right);

            var confidence = leftConfidence == "high" && rightConfidence == "high" ? "high" : "medium";
            return (leftTemplate + rightTemplate, confidence);
        }

        private static (string urlTemplate, string confidence) TryNormalizeStringFormat(
            InvocationExpressionSyntax invocation)
        {
            // Handle string.Format("/api/users/{0}", id) → "/api/users/{var}"
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.Text == "Format"
                && invocation.ArgumentList.Arguments.Count >= 2)
            {
                var formatArg = invocation.ArgumentList.Arguments[0].Expression;
                if (formatArg is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var template = Regex.Replace(
                        literal.Token.ValueText,
                        @"\{[0-9]+\}",
                        "{var}");
                    return (template, "high");
                }
            }

            return ("unknown", "low");
        }

        private (string urlTemplate, string confidence) TryEvaluateConstant(
            MemberAccessExpressionSyntax memberAccess)
        {
            var constantValue = _semanticModel.GetConstantValue(memberAccess);
            if (constantValue.HasValue && constantValue.Value is string stringValue)
            {
                return (stringValue, "high");
            }

            return ("unknown", "low");
        }

        private (string urlTemplate, string confidence) TryEvaluateIdentifier(IdentifierNameSyntax identifier)
        {
            var constantValue = _semanticModel.GetConstantValue(identifier);
            if (constantValue.HasValue && constantValue.Value is string stringValue)
            {
                return (stringValue, "high");
            }

            return ("unknown", "low");
        }

        private string? TryResolveNamedClient(ExpressionSyntax receiverExpression)
        {
            // Detect: _httpClientFactory.CreateClient("name").GetAsync(...)
            if (receiverExpression is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.Text == "CreateClient"
                && invocation.ArgumentList.Arguments.Count >= 1)
            {
                var clientNameArg = invocation.ArgumentList.Arguments[0].Expression;
                if (clientNameArg is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return literal.Token.ValueText;
                }
            }

            // Detect: var client = _factory.CreateClient("name"); client.GetAsync(...)
            if (receiverExpression is IdentifierNameSyntax identifier)
            {
                var symbol = _semanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol is ILocalSymbol localSymbol)
                {
                    var declaringNode = localSymbol.DeclaringSyntaxReferences
                        .Select(static r => r.GetSyntax())
                        .OfType<VariableDeclaratorSyntax>()
                        .FirstOrDefault();

                    if (declaringNode?.Initializer?.Value is InvocationExpressionSyntax initInvocation)
                    {
                        return TryResolveNamedClient(initInvocation);
                    }
                }
            }

            return null;
        }

        private static string MapToHttpMethod(string methodName)
        {
            return methodName switch
            {
                "GetAsync" => "GET",
                "PostAsync" => "POST",
                "PutAsync" => "PUT",
                "DeleteAsync" => "DELETE",
                "PatchAsync" => "PATCH",
                _ => "UNKNOWN"
            };
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

        private static string GetTypeIdentifier(ITypeSymbol type)
        {
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty, StringComparison.Ordinal);
        }

        private static string GetMethodIdentifier(IMethodSymbol method)
        {
            var typeIdentifier = method.ContainingType.OriginalDefinition
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty, StringComparison.Ordinal);
            var methodName = method.MethodKind == MethodKind.Constructor ? ".ctor" : method.Name;
            var parameters = method.Parameters
                .Select(static p => GetFriendlyTypeName(p.Type))
                .ToArray();
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

        private static string ComputeShortHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
        }
    }
}
