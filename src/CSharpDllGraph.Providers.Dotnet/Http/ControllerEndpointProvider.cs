using System.Runtime.CompilerServices;
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

public sealed class ControllerEndpointProvider : IGraphProvider
{
    public string Id => "dotnet-controller-endpoints";

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
                var walker = new ControllerWalker(semanticModel, projectVersionToken, nodes, edges);
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

    private sealed class ControllerWalker : CSharpSyntaxWalker
    {
        private static readonly HashSet<string> HttpVerbAttributes = new(StringComparer.Ordinal)
        {
            "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch",
            "HttpGetAttribute", "HttpPostAttribute", "HttpPutAttribute", "HttpDeleteAttribute", "HttpPatchAttribute"
        };

        private static readonly Dictionary<string, string> HttpVerbNames = new(StringComparer.Ordinal)
        {
            ["HttpGet"] = "GET",
            ["HttpPost"] = "POST",
            ["HttpPut"] = "PUT",
            ["HttpDelete"] = "DELETE",
            ["HttpPatch"] = "PATCH",
            ["HttpGetAttribute"] = "GET",
            ["HttpPostAttribute"] = "POST",
            ["HttpPutAttribute"] = "PUT",
            ["HttpDeleteAttribute"] = "DELETE",
            ["HttpPatchAttribute"] = "PATCH"
        };

        private readonly SemanticModel _semanticModel;
        private readonly string _projectVersionToken;
        private readonly Dictionary<NodeId, Node> _nodes;
        private readonly Dictionary<string, Edge> _edges;

        public ControllerWalker(
            SemanticModel semanticModel,
            string projectVersionToken,
            Dictionary<NodeId, Node> nodes,
            Dictionary<string, Edge> edges)
        {
            _semanticModel = semanticModel;
            _projectVersionToken = projectVersionToken;
            _nodes = nodes;
            _edges = edges;
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (!IsControllerClass(node))
            {
                base.VisitClassDeclaration(node);
                return;
            }

            if (_semanticModel.GetDeclaredSymbol(node) is not INamedTypeSymbol typeSymbol)
            {
                base.VisitClassDeclaration(node);
                return;
            }

            var controllerName = GetControllerName(typeSymbol.Name);
            var classRoutePrefix = GetRouteTemplate(node.AttributeLists);

            foreach (var member in node.Members.OfType<MethodDeclarationSyntax>())
            {
                if (!IsPublicMethod(member))
                {
                    continue;
                }

                var verbAttribute = GetHttpVerbAttribute(member.AttributeLists);
                if (verbAttribute is null)
                {
                    continue;
                }

                var httpMethod = ResolveHttpMethod(verbAttribute);
                if (httpMethod is null)
                {
                    continue;
                }

                var methodTemplate = GetRouteTemplate(member.AttributeLists);
                var normalizedTemplate = NormalizeRouteTemplate(classRoutePrefix, methodTemplate, controllerName, member.Identifier.Text);

                var responseTypes = CollectResponseTypes(member.AttributeLists);

                var endpointFqn = $"{httpMethod} {normalizedTemplate}";
                var endpointId = new NodeId(NodeKind.HttpEndpoint, endpointFqn, _projectVersionToken);

                var metaAttributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["httpMethod"] = JsonSerializer.SerializeToElement(httpMethod),
                    ["routeTemplate"] = JsonSerializer.SerializeToElement(normalizedTemplate),
                    ["controllerType"] = JsonSerializer.SerializeToElement(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal)),
                    ["actionName"] = JsonSerializer.SerializeToElement(member.Identifier.Text)
                };

                if (responseTypes.Count > 0)
                {
                    metaAttributes["responseTypes"] = JsonSerializer.SerializeToElement(responseTypes);
                }

                var endpointSourceRef = CreateSourceRef(member.GetLocation());
                var endpointNode = Node.Create(
                    endpointId,
                    NodeKind.HttpEndpoint,
                    $"{httpMethod} {normalizedTemplate}",
                    metaAttributes,
                    [endpointSourceRef]);

                _nodes[endpointId] = endpointNode;

                if (_semanticModel.GetDeclaredSymbol(member) is IMethodSymbol methodSymbol)
                {
                    var methodIdentifier = GetMethodIdentifier(methodSymbol);
                    var methodId = new NodeId(NodeKind.Method, methodIdentifier, _projectVersionToken);

                    var handlesEdge = Edge.Create(methodId, endpointId, EdgeKind.HandlesRoute);
                    _edges[AddEdgeKey(handlesEdge)] = handlesEdge;
                }
            }

            base.VisitClassDeclaration(node);
        }

        private bool IsControllerClass(ClassDeclarationSyntax node)
        {
            if (_semanticModel.GetDeclaredSymbol(node) is not INamedTypeSymbol typeSymbol)
            {
                return false;
            }

            if (HasAttribute(node.AttributeLists, "ApiController") || HasAttribute(node.AttributeLists, "ApiControllerAttribute"))
            {
                return true;
            }

            var baseType = typeSymbol.BaseType;
            while (baseType is not null)
            {
                var baseTypeName = baseType.Name;
                if (string.Equals(baseTypeName, "ControllerBase", StringComparison.Ordinal)
                    || string.Equals(baseTypeName, "Controller", StringComparison.Ordinal))
                {
                    return true;
                }

                baseType = baseType.BaseType;
            }

            return false;
        }

        private static bool IsPublicMethod(MethodDeclarationSyntax node)
        {
            return node.Modifiers.Any(static m => m.IsKind(SyntaxKind.PublicKeyword));
        }

        private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
        {
            return attributeLists
                .SelectMany(static list => list.Attributes)
                .Any(attr => GetAttributeShortName(attr) == attributeName);
        }

        private static string GetAttributeShortName(AttributeSyntax attribute)
        {
            var name = attribute.Name.ToString();
            var dotIndex = name.LastIndexOf('.');
            return dotIndex >= 0 ? name[(dotIndex + 1)..] : name;
        }

        private static AttributeSyntax? GetHttpVerbAttribute(SyntaxList<AttributeListSyntax> attributeLists)
        {
            return attributeLists
                .SelectMany(static list => list.Attributes)
                .FirstOrDefault(attr => HttpVerbAttributes.Contains(GetAttributeShortName(attr)));
        }

        private static string? ResolveHttpMethod(AttributeSyntax verbAttribute)
        {
            var shortName = GetAttributeShortName(verbAttribute);
            return HttpVerbNames.GetValueOrDefault(shortName);
        }

        private static string? GetRouteTemplate(SyntaxList<AttributeListSyntax> attributeLists)
        {
            var routeAttr = attributeLists
                .SelectMany(static list => list.Attributes)
                .FirstOrDefault(static attr =>
                {
                    var name = GetAttributeShortName(attr);
                    return name is "Route" or "RouteAttribute";
                });

            if (routeAttr is not null)
            {
                return ExtractFirstStringArgument(routeAttr);
            }

            var verbAttr = attributeLists
                .SelectMany(static list => list.Attributes)
                .FirstOrDefault(static attr => HttpVerbAttributes.Contains(GetAttributeShortName(attr)));

            return verbAttr is not null ? ExtractFirstStringArgument(verbAttr) : null;
        }

        private static string? ExtractFirstStringArgument(AttributeSyntax attribute)
        {
            if (attribute.ArgumentList is null || attribute.ArgumentList.Arguments.Count == 0)
            {
                return null;
            }

            var firstArg = attribute.ArgumentList.Arguments[0];
            if (firstArg.Expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }

            return null;
        }

        private static IReadOnlyList<int> CollectResponseTypes(SyntaxList<AttributeListSyntax> attributeLists)
        {
            var statusCodes = new List<int>();

            foreach (var attribute in attributeLists.SelectMany(static list => list.Attributes))
            {
                var shortName = GetAttributeShortName(attribute);
                if (shortName is not ("ProducesResponseType" or "ProducesResponseTypeAttribute"))
                {
                    continue;
                }

                if (attribute.ArgumentList is null)
                {
                    continue;
                }

                foreach (var argument in attribute.ArgumentList.Arguments)
                {
                    if (argument.Expression is LiteralExpressionSyntax literal
                        && literal.IsKind(SyntaxKind.NumericLiteralExpression)
                        && literal.Token.Value is int statusCode)
                    {
                        statusCodes.Add(statusCode);
                        break;
                    }

                    if (argument.Expression is MemberAccessExpressionSyntax memberAccess)
                    {
                        var memberName = memberAccess.Name.Identifier.Text;
                        if (TryResolveStatusCode(memberName, out var resolvedCode))
                        {
                            statusCodes.Add(resolvedCode);
                            break;
                        }
                    }
                }
            }

            return statusCodes.Distinct().OrderBy(static c => c).ToArray();
        }

        private static bool TryResolveStatusCode(string memberName, out int statusCode)
        {
            statusCode = memberName switch
            {
                "Status200OK" => 200,
                "Status201Created" => 201,
                "Status204NoContent" => 204,
                "Status400BadRequest" => 400,
                "Status401Unauthorized" => 401,
                "Status403Forbidden" => 403,
                "Status404NotFound" => 404,
                "Status409Conflict" => 409,
                "Status422UnprocessableEntity" => 422,
                "Status500InternalServerError" => 500,
                _ => -1
            };

            return statusCode >= 0;
        }

        private static string NormalizeRouteTemplate(
            string? classPrefix,
            string? methodTemplate,
            string controllerName,
            string actionName)
        {
            var segments = new List<string>();

            if (!string.IsNullOrWhiteSpace(classPrefix))
            {
                segments.Add(classPrefix.Trim('/'));
            }

            if (!string.IsNullOrWhiteSpace(methodTemplate))
            {
                segments.Add(methodTemplate.Trim('/'));
            }

            var combined = segments.Count > 0
                ? "/" + string.Join("/", segments.Where(static s => s.Length > 0))
                : "/";

            combined = ReplaceToken(combined, "controller", controllerName.ToLowerInvariant());
            combined = ReplaceToken(combined, "action", actionName.ToLowerInvariant());
            combined = NormalizeRouteConstraints(combined);

            return combined.Length == 0 ? "/" : combined;
        }

        private static string ReplaceToken(string template, string token, string replacement)
        {
            return Regex.Replace(
                template,
                $@"\[{Regex.Escape(token)}\]",
                replacement,
                RegexOptions.IgnoreCase);
        }

        private static string NormalizeRouteConstraints(string template)
        {
            return Regex.Replace(template, @"\{(\w+):[^}]+\}", "{$1}");
        }

        private static string GetControllerName(string className)
        {
            const string suffix = "Controller";
            if (className.EndsWith(suffix, StringComparison.Ordinal) && className.Length > suffix.Length)
            {
                return className[..^suffix.Length];
            }

            return className;
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

        private static string AddEdgeKey(Edge edge)
        {
            return $"{edge.FromId}|{edge.ToId}|{edge.Kind}";
        }
    }
}
