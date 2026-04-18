using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Providers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpDllGraph.Providers.Dotnet;

internal static class RoslynUsageGraphBuilder
{
    public static async Task<GraphFragment> BuildAsync(
        string solutionPath,
        string solutionDirectory,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> packageVersionsByProjectPath,
        CancellationToken cancellationToken = default)
    {
        RoslynBootstrap.EnsureRegistered();

        var collector = new UsageGraphCollector();
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        foreach (var projectPath in DiscoverProjectPaths(solutionPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetPackageVersions(projectPath, packageVersionsByProjectPath, out _))
            {
                continue;
            }

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

        foreach (var project in workspace.CurrentSolution.Projects.Where(static project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                continue;
            }

            ReportDiagnostics(project, compilation);

            if (!TryGetPackageVersions(project.FilePath, packageVersionsByProjectPath, out var packageVersions))
            {
                continue;
            }

            foreach (var document in project.Documents.Where(static document => document.SourceCodeKind == SourceCodeKind.Regular))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await IsGeneratedAsync(document, cancellationToken))
                {
                    continue;
                }

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
                var projectVersionToken = GetProjectVersionToken(solutionDirectory, project.FilePath, project.Name);
                var walker = new UsageWalker(
                    semanticModel,
                    projectVersionToken,
                    packageVersions,
                    collector);
                walker.Visit(root);
            }
        }

        return collector.ToFragment();
    }

    private static IEnumerable<string> DiscoverProjectPaths(string solutionPath)
    {
        var baseDirectory = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException("Solution directory is required.");

        if (solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            var document = XDocument.Load(solutionPath, LoadOptions.None);
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
            return File.ReadLines(solutionPath)
                .Select(static line => line.Trim())
                .Where(static line => line.StartsWith("Project(", StringComparison.Ordinal))
                .Select(static line =>
                {
                    var parts = line.Split(',');
                    return parts.Length >= 2 ? parts[1].Trim().Trim('"') : null;
                })
                .Where(static path => path is not null)
                .Select(path => Path.GetFullPath(Path.Combine(baseDirectory, path!)))
                .Where(static path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
        }

        throw new NotSupportedException($"Unsupported solution format: '{solutionPath}'.");
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

        foreach (var runtimeAssembly in GetTrustedPlatformAssemblies())
        {
            references[runtimeAssembly] = MetadataReference.CreateFromFile(runtimeAssembly);
        }

        foreach (var packageAssembly in GetPackageAssemblies(projectPath))
        {
            references[packageAssembly] = MetadataReference.CreateFromFile(packageAssembly);
        }

        return references.Values.ToArray();
    }

    private static IEnumerable<string> GetTrustedPlatformAssemblies()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            return [];
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetPackageAssemblies(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException("Project directory is required.");
        var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = document.RootElement;

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
            yield break;
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
                                yield return resolvedPath;
                            }
                        }
                    }
                }
            }
        }
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

    private static bool TryGetPackageVersions(
        string? projectPath,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> packageVersionsByProjectPath,
        out IReadOnlyDictionary<string, string> packageVersions)
    {
        packageVersions = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return false;
        }

        if (packageVersionsByProjectPath.TryGetValue(Path.GetFullPath(projectPath), out var resolvedPackageVersions))
        {
            packageVersions = resolvedPackageVersions;
            return true;
        }

        return false;
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

    private static async Task<bool> IsGeneratedAsync(Document document, CancellationToken cancellationToken)
    {
        var filePath = document.FilePath ?? string.Empty;
        if (IsGeneratedFileName(filePath))
        {
            return true;
        }

        var text = await document.GetTextAsync(cancellationToken);
        var start = Math.Min(2048, text.Length);
        if (start == 0)
        {
            return false;
        }

        var prefix = text.ToString(TextSpan.FromBounds(0, start));
        return prefix.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase)
               || prefix.Contains("<autogenerated", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedFileName(string filePath)
    {
        return filePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
               || filePath.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
               || filePath.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
               || filePath.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
               || filePath.Contains(".generated.", StringComparison.OrdinalIgnoreCase)
               || filePath.Contains(".g.", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReportDiagnostics(Project project, Compilation compilation)
    {
        foreach (var diagnostic in compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error))
        {
            Trace.TraceWarning(
                "{0}: {1} {2} {3}",
                project.Name,
                diagnostic.Severity,
                diagnostic.Id,
                diagnostic.GetMessage());
        }
    }

    private sealed class UsageWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly string _projectVersionToken;
        private readonly IReadOnlyDictionary<string, string> _packageVersions;
        private readonly UsageGraphCollector _collector;
        private readonly Stack<NodeId> _sourceNodes = new();

        public UsageWalker(
            SemanticModel semanticModel,
            string projectVersionToken,
            IReadOnlyDictionary<string, string> packageVersions,
            UsageGraphCollector collector)
        {
            _semanticModel = semanticModel;
            _projectVersionToken = projectVersionToken;
            _packageVersions = packageVersions;
            _collector = collector;
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
            VisitMethodLikeDeclaration(node, base.VisitMethodDeclaration);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            VisitMethodLikeDeclaration(node, base.VisitConstructorDeclaration);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (TryResolveTarget(node, out var targetId, out var sourceSpan, out var kind) && kind == EdgeKind.Calls)
            {
                AddUsageEdge(kind, targetId, sourceSpan);
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
            if (node.Parent is not InvocationExpressionSyntax
                && symbol is INamedTypeSymbol namedType
                && TryResolvePackageTarget(namedType, out var targetId))
            {
                AddUsageEdge(EdgeKind.Uses, targetId, CreateSourceRef(node.GetLocation()));
            }

            base.VisitMemberAccessExpression(node);
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            VisitTypeLikeName(node, base.VisitIdentifierName);
        }

        public override void VisitGenericName(GenericNameSyntax node)
        {
            VisitTypeLikeName(node, base.VisitGenericName);
        }

        public override void VisitQualifiedName(QualifiedNameSyntax node)
        {
            VisitTypeLikeName(node, base.VisitQualifiedName);
        }

        public override void VisitAliasQualifiedName(AliasQualifiedNameSyntax node)
        {
            VisitTypeLikeName(node, base.VisitAliasQualifiedName);
        }

        public override void VisitPredefinedType(PredefinedTypeSyntax node)
        {
            VisitTypeLikeName(node, base.VisitPredefinedType);
        }

        public override void VisitArrayType(ArrayTypeSyntax node)
        {
            VisitTypeLikeName(node, base.VisitArrayType);
        }

        public override void VisitNullableType(NullableTypeSyntax node)
        {
            VisitTypeLikeName(node, base.VisitNullableType);
        }

        public override void VisitPointerType(PointerTypeSyntax node)
        {
            VisitTypeLikeName(node, base.VisitPointerType);
        }

        private void VisitTypeDeclaration<TSyntax>(TSyntax node, Action<TSyntax> visitChildren)
            where TSyntax : TypeDeclarationSyntax
        {
            if (TryEmitUserTypeNode(node, out var nodeId))
            {
                _sourceNodes.Push(nodeId);
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

        private void VisitMethodLikeDeclaration<TSyntax>(TSyntax node, Action<TSyntax> visitChildren)
            where TSyntax : SyntaxNode
        {
            if (TryEmitUserMethodNode(node, out var nodeId))
            {
                _sourceNodes.Push(nodeId);
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

        private void VisitTypeLikeName<TSyntax>(TSyntax node, Action<TSyntax> visitChildren)
            where TSyntax : TypeSyntax
        {
            if (IsTypeUsageContext(node)
                && TryResolveTypeReference(node, out var targetId, out var sourceSpan))
            {
                AddUsageEdge(EdgeKind.Uses, targetId, sourceSpan);
            }

            visitChildren(node);
        }

        private static bool IsTypeUsageContext(SyntaxNode node)
        {
            return node.Parent is VariableDeclarationSyntax
                or MethodDeclarationSyntax
                or PropertyDeclarationSyntax
                or FieldDeclarationSyntax
                or EventDeclarationSyntax
                or IndexerDeclarationSyntax
                or ConversionOperatorDeclarationSyntax
                or OperatorDeclarationSyntax
                or ParameterSyntax
                or CastExpressionSyntax
                or IsPatternExpressionSyntax
                or TypeOfExpressionSyntax
                or AttributeSyntax
                or ObjectCreationExpressionSyntax
                or ImplicitObjectCreationExpressionSyntax
                or UsingDirectiveSyntax;
        }

        private bool TryResolveTypeReference(TypeSyntax node, out NodeId targetId, out SourceRef sourceRef)
        {
            var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is INamedTypeSymbol typeSymbol && TryResolvePackageTarget(typeSymbol, out targetId))
            {
                sourceRef = CreateSourceRef(node.GetLocation());
                return true;
            }

            if (symbol is IAliasSymbol alias && alias.Target is INamedTypeSymbol aliasType && TryResolvePackageTarget(aliasType, out targetId))
            {
                sourceRef = CreateSourceRef(node.GetLocation());
                return true;
            }

            if (_semanticModel.GetTypeInfo(node).Type is INamedTypeSymbol typeInfo && TryResolvePackageTarget(typeInfo, out targetId))
            {
                sourceRef = CreateSourceRef(node.GetLocation());
                return true;
            }

            targetId = default;
            sourceRef = new SourceRef(string.Empty, []);
            return false;
        }

        private bool TryEmitUserTypeNode(TypeDeclarationSyntax node, out NodeId nodeId)
        {
            if (_semanticModel.GetDeclaredSymbol(node) is not INamedTypeSymbol typeSymbol)
            {
                nodeId = default;
                return false;
            }

            var typeIdentifier = GetTypeIdentifier(typeSymbol);
            nodeId = new NodeId(NodeKind.Type, typeIdentifier, _projectVersionToken);
            _collector.AddNode(Node.Create(
                nodeId,
                NodeKind.Type,
                $"{typeIdentifier}@{_projectVersionToken}",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["origin"] = JsonSerializer.SerializeToElement("user"),
                    ["projectVersion"] = JsonSerializer.SerializeToElement(_projectVersionToken)
                },
                BuildSourceRefs(typeSymbol.Locations)));
            return true;
        }

        private bool TryEmitUserMethodNode(SyntaxNode node, out NodeId nodeId)
        {
            if (_semanticModel.GetDeclaredSymbol(node) is not IMethodSymbol methodSymbol)
            {
                nodeId = default;
                return false;
            }

            var methodIdentifier = GetMethodIdentifier(methodSymbol);
            nodeId = new NodeId(NodeKind.Method, methodIdentifier, _projectVersionToken);
            _collector.AddNode(Node.Create(
                nodeId,
                NodeKind.Method,
                $"{methodIdentifier}@{_projectVersionToken}",
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["origin"] = JsonSerializer.SerializeToElement("user"),
                    ["projectVersion"] = JsonSerializer.SerializeToElement(_projectVersionToken)
                },
                BuildSourceRefs(methodSymbol.Locations)));
            return true;
        }

        private bool TryResolveTarget(ExpressionSyntax expression, out NodeId targetId, out SourceRef sourceRef, out EdgeKind kind)
        {
            var symbol = _semanticModel.GetSymbolInfo(expression).Symbol;
            if (symbol is IMethodSymbol methodSymbol && TryResolvePackageTarget(methodSymbol, out targetId))
            {
                sourceRef = CreateSourceRef(expression.GetLocation());
                kind = EdgeKind.Calls;
                return true;
            }

            if (symbol is INamedTypeSymbol typeSymbol && TryResolvePackageTarget(typeSymbol, out targetId))
            {
                sourceRef = CreateSourceRef(expression.GetLocation());
                kind = EdgeKind.Uses;
                return true;
            }

            targetId = default;
            sourceRef = new SourceRef(string.Empty, []);
            kind = default;
            return false;
        }

        private bool TryResolvePackageTarget(INamedTypeSymbol typeSymbol, out NodeId targetId)
        {
            var definition = typeSymbol.OriginalDefinition;
            if (definition.ContainingAssembly is null
                || !_packageVersions.TryGetValue(definition.ContainingAssembly.Identity.Name, out var versionToken))
            {
                targetId = default;
                return false;
            }

            targetId = new NodeId(NodeKind.Type, GetTypeIdentifier(definition), versionToken);
            return true;
        }

        private bool TryResolvePackageTarget(IMethodSymbol methodSymbol, out NodeId targetId)
        {
            var definition = methodSymbol.OriginalDefinition;
            if (definition.ContainingAssembly is null
                || !_packageVersions.TryGetValue(definition.ContainingAssembly.Identity.Name, out var versionToken))
            {
                targetId = default;
                return false;
            }

            targetId = new NodeId(NodeKind.Method, GetMethodIdentifier(definition), versionToken);
            return true;
        }

        private void AddUsageEdge(EdgeKind kind, NodeId targetId, SourceRef sourceRef)
        {
            if (_sourceNodes.Count == 0)
            {
                return;
            }

            var fromId = _sourceNodes.Peek();
            _collector.AddUsage(fromId, targetId, kind, sourceRef);
        }

        private static IReadOnlyList<SourceRef> BuildSourceRefs(IReadOnlyList<Location> locations)
        {
            var sourceRefs = new Dictionary<string, List<SourceSpan>>(StringComparer.Ordinal);

            foreach (var location in locations.Where(static location => location.IsInSource))
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

        private static string GetTypeIdentifier(ITypeSymbol type)
        {
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
        }

        private static string GetMethodIdentifier(IMethodSymbol method)
        {
            var typeIdentifier = GetTypeIdentifier(method.ContainingType.OriginalDefinition);
            var methodName = method.MethodKind == MethodKind.Constructor ? ".ctor" : method.Name;
            var parameters = method.Parameters.Select(static parameter => GetFriendlyTypeName(parameter.Type)).ToArray();
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
                    $"{TrimGenericArity(GetTypeIdentifier(namedType.ConstructedFrom))}<{string.Join(", ", namedType.TypeArguments.Select(GetFriendlyTypeName))}>",
                IArrayTypeSymbol arrayType => $"{GetFriendlyTypeName(arrayType.ElementType)}[]",
                _ => GetTypeIdentifier(type)
            };
        }

        private static string TrimGenericArity(string value)
        {
            var index = value.IndexOf('`');
            return index < 0 ? value : value[..index];
        }
    }

    private sealed class UsageGraphCollector
    {
        private readonly Dictionary<NodeId, Node> _nodes = new();
        private readonly Dictionary<UsageEdgeKey, UsageEdgeAccumulator> _edges = new();

        public void AddNode(Node node)
        {
            _nodes[node.Id] = node;
        }

        public void AddUsage(NodeId fromId, NodeId toId, EdgeKind kind, SourceRef sourceRef)
        {
            var key = new UsageEdgeKey(fromId, toId, kind);
            if (!_edges.TryGetValue(key, out var accumulator))
            {
                accumulator = new UsageEdgeAccumulator();
                _edges[key] = accumulator;
            }

            accumulator.Add(sourceRef);
        }

        public GraphFragment ToFragment()
        {
            return new GraphFragment(
                "dotnet",
                _nodes.Values.OrderBy(static node => node.Id.ToString(), StringComparer.Ordinal).ToArray(),
                _edges
                    .OrderBy(static pair => pair.Key.FromId.ToString(), StringComparer.Ordinal)
                    .ThenBy(static pair => pair.Key.ToId.ToString(), StringComparer.Ordinal)
                    .ThenBy(static pair => pair.Key.Kind.ToString(), StringComparer.Ordinal)
                    .Select(static pair => pair.Value.ToEdge(pair.Key))
                    .ToArray());
        }
    }

    private readonly record struct UsageEdgeKey(NodeId FromId, NodeId ToId, EdgeKind Kind);

    private sealed class UsageEdgeAccumulator
    {
        private readonly Dictionary<string, HashSet<SourceSpan>> _spansByFile = new(StringComparer.Ordinal);
        private int _count;

        public void Add(SourceRef sourceRef)
        {
            if (!_spansByFile.TryGetValue(sourceRef.File, out var spans))
            {
                spans = new HashSet<SourceSpan>();
                _spansByFile[sourceRef.File] = spans;
            }

            foreach (var span in sourceRef.Spans)
            {
                spans.Add(span);
            }

            _count++;
        }

        public Edge ToEdge(UsageEdgeKey key)
        {
            var sourceRefs = _spansByFile
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new SourceRef(
                    pair.Key,
                    pair.Value
                        .OrderBy(static span => span.StartLine)
                        .ThenBy(static span => span.StartColumn)
                        .ThenBy(static span => span.EndLine)
                        .ThenBy(static span => span.EndColumn)
                        .ToArray()))
                .ToArray();

            var attributeName = key.Kind == EdgeKind.Calls ? "callCount" : "referenceCount";
            return Edge.Create(
                key.FromId,
                key.ToId,
                key.Kind,
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [attributeName] = JsonSerializer.SerializeToElement(_count)
                },
                sourceRefs);
        }
    }
}
