namespace CSharpDllGraph.Engine.Graph;

public sealed record SourceRef(
    string File,
    IReadOnlyList<SourceSpan> Spans);
