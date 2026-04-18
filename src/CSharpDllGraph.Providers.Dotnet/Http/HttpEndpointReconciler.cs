using System.Text.Json;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Http;

namespace CSharpDllGraph.Providers.Dotnet.Http;

/// <summary>
/// Merges duplicate <see cref="NodeKind.HttpEndpoint"/> nodes that represent the same
/// logical endpoint (same normalized HTTP method + normalized path) but originate from
/// different sources (e.g. static analysis vs OpenAPI spec).
/// <para>
/// Merge rules:
/// <list type="bullet">
///   <item>SourceRefs from all matching nodes are unioned.</item>
///   <item>Attributes that agree across all sources are stored under the plain key.</item>
///   <item>Attributes that disagree are stored with a source-name prefix,
///     e.g. <c>summary:openapi</c> and <c>summary:static</c>, so that no value is silently lost.</item>
/// </list>
/// </para>
/// </summary>
public static class HttpEndpointReconciler
{
    /// <summary>
    /// Reconciles <see cref="NodeKind.HttpEndpoint"/> nodes in <paramref name="nodes"/>.
    /// Non-HttpEndpoint nodes are returned unchanged.
    /// Edges that reference a node whose <see cref="NodeId"/> was merged away are
    /// rewritten to point at the canonical merged node.
    /// </summary>
    public static (IReadOnlyList<Node> Nodes, IReadOnlyList<Edge> Edges) Reconcile(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<Edge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        // Partition into endpoints and everything else.
        var endpointNodes = nodes
            .Where(static n => n.Kind == NodeKind.HttpEndpoint)
            .ToArray();

        var otherNodes = nodes
            .Where(static n => n.Kind != NodeKind.HttpEndpoint)
            .ToArray();

        // Group endpoints by route identity key (method, normalizedPath).
        var groups = endpointNodes
            .GroupBy(static n => RouteIdentityKey(n), StringComparer.Ordinal)
            .ToArray();

        // For each group: if one node, keep as-is; if many, merge them.
        var mergedEndpoints = new List<Node>(groups.Length);

        // Build a map from original NodeId → canonical merged NodeId for edge rewriting.
        var idRemap = new Dictionary<NodeId, NodeId>();

        foreach (var group in groups)
        {
            var members = group.ToArray();
            if (members.Length == 1)
            {
                mergedEndpoints.Add(members[0]);
                idRemap[members[0].Id] = members[0].Id;
                continue;
            }

            var merged = MergeGroup(members);
            mergedEndpoints.Add(merged);

            foreach (var member in members)
            {
                idRemap[member.Id] = merged.Id;
            }
        }

        // Rewrite edges that touch any remapped node id.
        var rewrittenEdges = RewriteEdges(edges, idRemap);

        var resultNodes = otherNodes
            .Concat(mergedEndpoints)
            .OrderBy(static n => n.Id.ToString(), StringComparer.Ordinal)
            .ToArray();

        return (resultNodes, rewrittenEdges);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// The merge key: normalized method + normalized path. Version token is intentionally
    /// excluded so that nodes from different sources (different version tokens) are
    /// correctly identified as the same logical endpoint.
    /// </summary>
    private static string RouteIdentityKey(Node node)
    {
        var method = node.Attributes.TryGetValue("httpMethod", out var m)
            ? (m.GetString() ?? string.Empty).ToUpperInvariant()
            : string.Empty;

        var rawPath = node.Attributes.TryGetValue("routeTemplate", out var r)
            ? (r.GetString() ?? string.Empty)
            : string.Empty;

        var normalizedPath = HttpRouteNormalizer.NormalizePath(rawPath);

        return $"{method} {normalizedPath}";
    }

    private static Node MergeGroup(Node[] members)
    {
        // Canonical node id: pick the one whose version token is "openapi" if present,
        // otherwise the lexicographically first one.  The id is reconstructed from the
        // normalized key so that it is stable and predictable.
        var canonical = members
            .OrderBy(static n => n.Id.VersionToken == "openapi" ? 0 : 1)
            .ThenBy(static n => n.Id.ToString(), StringComparer.Ordinal)
            .First();

        // Union all SourceRefs (deduplicate by file + spans).
        var allSourceRefs = members
            .SelectMany(static n => n.SourceRefs)
            .Distinct(SourceRefComparer.Instance)
            .OrderBy(static sr => sr.File, StringComparer.Ordinal)
            .ToArray();

        // Determine source names per node for conflict tagging.
        var sourcesPerNode = members
            .Select(static n =>
            {
                var sourceName = n.Attributes.TryGetValue("source", out var s)
                    ? (s.GetString() ?? string.Empty)
                    : string.Empty;

                if (string.IsNullOrEmpty(sourceName))
                {
                    // Fall back to versionToken prefix (e.g. "project-src/appv1" → "project")
                    var vt = n.Id.VersionToken;
                    sourceName = vt.StartsWith("project-", StringComparison.OrdinalIgnoreCase)
                        ? "static"
                        : vt;
                }

                return (node: n, sourceName);
            })
            .ToArray();

        // Collect all distinct attribute keys across the group.
        var allKeys = members
            .SelectMany(static n => n.Attributes.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static k => k, StringComparer.Ordinal)
            .ToArray();

        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var key in allKeys)
        {
            // Gather all (sourceName, value) pairs for this key.
            var contributions = sourcesPerNode
                .Where(x => x.node.Attributes.ContainsKey(key))
                .Select(x => (x.sourceName, value: x.node.Attributes[key]))
                .ToArray();

            if (contributions.Length == 0)
            {
                continue;
            }

            // Check if all values agree (compare raw JSON text).
            var distinctRaw = contributions
                .Select(static c => c.value.GetRawText())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (distinctRaw.Length == 1)
            {
                // Unanimous — store under the plain key.
                merged[key] = contributions[0].value;
            }
            else
            {
                // Disagreement — store each value under "key:sourceName".
                foreach (var (sourceName, value) in contributions)
                {
                    var taggedKey = string.IsNullOrEmpty(sourceName)
                        ? key
                        : $"{key}:{sourceName}";

                    // Last writer for the same tagged key wins (shouldn't normally happen,
                    // but guard against duplicate source names).
                    merged[taggedKey] = value;
                }
            }
        }

        return Node.Create(
            canonical.Id,
            NodeKind.HttpEndpoint,
            canonical.DisplayName,
            merged,
            allSourceRefs);
    }

    private static IReadOnlyList<Edge> RewriteEdges(
        IReadOnlyList<Edge> edges,
        IReadOnlyDictionary<NodeId, NodeId> idRemap)
    {
        if (idRemap.Count == 0)
        {
            return edges;
        }

        // Deduplicate after rewriting using a simple string key.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Edge>(edges.Count);

        foreach (var edge in edges)
        {
            var newFrom = idRemap.TryGetValue(edge.FromId, out var remappedFrom) ? remappedFrom : edge.FromId;
            var newTo = idRemap.TryGetValue(edge.ToId, out var remappedTo) ? remappedTo : edge.ToId;

            var rewritten = newFrom == edge.FromId && newTo == edge.ToId
                ? edge
                : Edge.Create(newFrom, newTo, edge.Kind, edge.Attributes, edge.SourceRefs);

            var key = $"{rewritten.FromId}|{rewritten.ToId}|{rewritten.Kind}";
            if (seen.Add(key))
            {
                result.Add(rewritten);
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // SourceRef equality comparer
    // -------------------------------------------------------------------------

    private sealed class SourceRefComparer : IEqualityComparer<SourceRef>
    {
        public static readonly SourceRefComparer Instance = new();

        public bool Equals(SourceRef? x, SourceRef? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (!string.Equals(x.File, y.File, StringComparison.Ordinal)) return false;
            if (x.Spans.Count != y.Spans.Count) return false;

            for (var i = 0; i < x.Spans.Count; i++)
            {
                if (x.Spans[i] != y.Spans[i]) return false;
            }

            return true;
        }

        public int GetHashCode(SourceRef obj)
        {
            var hash = new HashCode();
            hash.Add(obj.File, StringComparer.Ordinal);
            foreach (var span in obj.Spans)
            {
                hash.Add(span);
            }

            return hash.ToHashCode();
        }
    }
}
