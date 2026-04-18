using System.Text.RegularExpressions;

namespace CSharpDllGraph.Engine.Http;

public static partial class HttpRouteNormalizer
{
    [GeneratedRegex(@"(?<![{])\:([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex ColonParamRegex();

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)(?:[?=:].*?)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex ConstrainedParamRegex();

    [GeneratedRegex(@"\{\*\*?[A-Za-z_][A-Za-z0-9_]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex CatchAllParamRegex();

    public static string NormalizePath(string rawUrl)
    {
        if (string.IsNullOrEmpty(rawUrl))
        {
            return "/";
        }

        var path = rawUrl;

        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed))
        {
            path = Uri.UnescapeDataString(parsed.AbsolutePath);
        }

        path = path.ToLowerInvariant();
        path = CatchAllParamRegex().Replace(path, "{**}");
        path = ConstrainedParamRegex().Replace(path, "{$1}");
        path = ColonParamRegex().Replace(path, "{$1}");

        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path.TrimEnd('/');
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return path;
    }

    public static string NormalizeMethod(string httpMethod)
    {
        return string.IsNullOrWhiteSpace(httpMethod)
            ? string.Empty
            : httpMethod.Trim().ToUpperInvariant();
    }

    public static (string Method, string Path) Normalize(string httpMethod, string rawUrl)
    {
        return (NormalizeMethod(httpMethod), NormalizePath(rawUrl));
    }
}
