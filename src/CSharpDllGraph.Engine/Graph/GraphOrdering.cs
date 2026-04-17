using System.Text.Json;

namespace CSharpDllGraph.Engine.Graph;

internal static class GraphOrdering
{
    public static readonly IComparer<Node> NodeComparer =
        Comparer<Node>.Create(static (left, right) => StringComparer.Ordinal.Compare(left.Id.ToString(), right.Id.ToString()));

    public static readonly IComparer<Edge> EdgeComparer =
        Comparer<Edge>.Create(static (left, right) =>
        {
            var fromResult = StringComparer.Ordinal.Compare(left.FromId.ToString(), right.FromId.ToString());
            if (fromResult != 0)
            {
                return fromResult;
            }

            var toResult = StringComparer.Ordinal.Compare(left.ToId.ToString(), right.ToId.ToString());
            if (toResult != 0)
            {
                return toResult;
            }

            var kindResult = StringComparer.Ordinal.Compare(left.Kind.ToString(), right.Kind.ToString());
            if (kindResult != 0)
            {
                return kindResult;
            }

            return CompareSourceRefs(left.SourceRefs, right.SourceRefs);
        });

    public static IReadOnlyList<SourceRef> OrderSourceRefs(IReadOnlyList<SourceRef> sourceRefs)
    {
        return sourceRefs
            .OrderBy(static sourceRef => sourceRef.File, StringComparer.Ordinal)
            .ThenBy(static sourceRef => sourceRef.Spans.Count)
            .ThenBy(static sourceRef => string.Join("|", sourceRef.Spans.Select(static span => $"{span.StartLine}:{span.StartColumn}:{span.EndLine}:{span.EndColumn}")), StringComparer.Ordinal)
            .Select(static sourceRef => new SourceRef(
                sourceRef.File,
                sourceRef.Spans
                    .OrderBy(static span => span.StartLine)
                    .ThenBy(static span => span.StartColumn)
                    .ThenBy(static span => span.EndLine)
                    .ThenBy(static span => span.EndColumn)
                    .ToArray()))
            .ToArray();
    }

    public static IReadOnlyDictionary<string, JsonElement> OrderAttributes(IReadOnlyDictionary<string, JsonElement> attributes)
    {
        return attributes
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
    }

    private static int CompareSourceRefs(IReadOnlyList<SourceRef> left, IReadOnlyList<SourceRef> right)
    {
        var leftOrdered = OrderSourceRefs(left);
        var rightOrdered = OrderSourceRefs(right);

        var countResult = leftOrdered.Count.CompareTo(rightOrdered.Count);
        if (countResult != 0)
        {
            return countResult;
        }

        for (var index = 0; index < leftOrdered.Count; index++)
        {
            var fileResult = StringComparer.Ordinal.Compare(leftOrdered[index].File, rightOrdered[index].File);
            if (fileResult != 0)
            {
                return fileResult;
            }

            var spansCountResult = leftOrdered[index].Spans.Count.CompareTo(rightOrdered[index].Spans.Count);
            if (spansCountResult != 0)
            {
                return spansCountResult;
            }

            for (var spanIndex = 0; spanIndex < leftOrdered[index].Spans.Count; spanIndex++)
            {
                var leftSpan = leftOrdered[index].Spans[spanIndex];
                var rightSpan = rightOrdered[index].Spans[spanIndex];

                var startLineResult = leftSpan.StartLine.CompareTo(rightSpan.StartLine);
                if (startLineResult != 0)
                {
                    return startLineResult;
                }

                var startColumnResult = leftSpan.StartColumn.CompareTo(rightSpan.StartColumn);
                if (startColumnResult != 0)
                {
                    return startColumnResult;
                }

                var endLineResult = leftSpan.EndLine.CompareTo(rightSpan.EndLine);
                if (endLineResult != 0)
                {
                    return endLineResult;
                }

                var endColumnResult = leftSpan.EndColumn.CompareTo(rightSpan.EndColumn);
                if (endColumnResult != 0)
                {
                    return endColumnResult;
                }
            }
        }

        return 0;
    }
}
