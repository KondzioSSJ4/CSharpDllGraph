using System.Globalization;
using System.Text;

namespace CSharpDllGraph.Engine.Graph;

public readonly record struct NodeId
{
    public NodeId(NodeKind kind, string fullyQualifiedName, string versionToken)
        : this(
            kind,
            NormalizeSegment(fullyQualifiedName, nameof(fullyQualifiedName)),
            NormalizeSegment(versionToken, nameof(versionToken)),
            alreadyNormalized: true)
    {
    }

    private NodeId(NodeKind kind, string fullyQualifiedName, string versionToken, bool alreadyNormalized)
    {
        Kind = kind;
        FullyQualifiedName = alreadyNormalized
            ? ValidateNormalizedSegment(fullyQualifiedName, nameof(fullyQualifiedName))
            : NormalizeSegment(fullyQualifiedName, nameof(fullyQualifiedName));
        VersionToken = alreadyNormalized
            ? ValidateNormalizedSegment(versionToken, nameof(versionToken))
            : NormalizeSegment(versionToken, nameof(versionToken));
    }

    public NodeKind Kind { get; }

    public string FullyQualifiedName { get; }

    public string VersionToken { get; }

    public override string ToString() => $"{Kind}:{FullyQualifiedName}@{VersionToken}";

    public static NodeId Parse(string value)
    {
        if (!TryParse(value, out var nodeId))
        {
            throw new FormatException($"Invalid node id format: '{value}'.");
        }

        return nodeId;
    }

    public static bool TryParse(string? value, out NodeId nodeId)
    {
        nodeId = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var colonIndex = value.IndexOf(':');
        var atIndex = value.LastIndexOf('@');

        if (colonIndex <= 0 || atIndex <= colonIndex + 1 || atIndex == value.Length - 1)
        {
            return false;
        }

        if (value.IndexOf(':', colonIndex + 1) >= 0)
        {
            return false;
        }

        var kindToken = value[..colonIndex];
        if (!Enum.TryParse<NodeKind>(kindToken, ignoreCase: false, out var kind))
        {
            return false;
        }

        var fqn = value[(colonIndex + 1)..atIndex];
        var version = value[(atIndex + 1)..];

        if (!IsNormalized(fqn) || !IsNormalized(version))
        {
            return false;
        }

        nodeId = new NodeId(kind, fqn, version, alreadyNormalized: true);
        return true;
    }

    private static bool IsNormalized(string value)
    {
        return value.Length > 0 && value.All(static c => IsSafeChar(c) || c == '~');
    }

    private static string NormalizeSegment(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Segment value is required.", paramName);
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (IsSafeChar(character))
            {
                builder.Append(character);
                continue;
            }

            builder.Append('~');
            builder.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }

    private static string ValidateNormalizedSegment(string value, string paramName)
    {
        if (!IsNormalized(value))
        {
            throw new ArgumentException("Segment must be pre-normalized.", paramName);
        }

        return value;
    }

    private static bool IsSafeChar(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '+';
    }
}
