namespace CSharpDllGraph.Engine.Graph;

public readonly record struct SourceSpan(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
