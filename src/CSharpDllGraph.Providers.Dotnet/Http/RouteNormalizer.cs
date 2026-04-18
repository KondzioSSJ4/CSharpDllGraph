using System.Text.RegularExpressions;

namespace CSharpDllGraph.Providers.Dotnet.Http;

/// <summary>
/// Normalizes route templates and URLs to a canonical (method, path) pair so that
/// producer and consumer routes can be compared across frameworks and syntax styles.
/// </summary>
public static partial class RouteNormalizer
{
    // Matches Express-style :param tokens that are not inside curly braces.
    [GeneratedRegex(@"(?<![{])\:([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex ColonParamRegex();

    // Matches ASP.NET route parameters with optional constraint or optional marker:
    //   {id:int}, {id?}, {id:int?}, {id=default}  →  {id}
    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)(?:[?=:].*?)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex ConstrainedParamRegex();

    // Matches catch-all parameters: {**path}, {*path}  →  {**}
    [GeneratedRegex(@"\{\*\*?[A-Za-z_][A-Za-z0-9_]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex CatchAllParamRegex();

    /// <summary>
    /// Normalizes a raw URL or route template to a canonical path string:
    /// <list type="bullet">
    ///   <item>Strips scheme and host when present (keeps path + query stripped).</item>
    ///   <item>Lowercases the entire path.</item>
    ///   <item>Strips trailing slash unless the path is exactly "/".</item>
    ///   <item>Normalizes <c>:param</c> (Express) → <c>{param}</c>.</item>
    ///   <item>Normalizes <c>{param:constraint}</c>, <c>{param?}</c>, <c>{param=default}</c> → <c>{param}</c>.</item>
    ///   <item>Normalizes catch-all <c>{**path}</c> / <c>{*path}</c> → <c>{**}</c>.</item>
    /// </list>
    /// </summary>
    public static string NormalizePath(string rawUrl)
    {
        if (string.IsNullOrEmpty(rawUrl))
            return "/";

        var path = rawUrl;

        // Strip scheme + host if a full URL was supplied.
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed))
        {
            path = parsed.AbsolutePath;
        }

        // Lowercase first so all subsequent regex operations are case-insensitive by default.
        path = path.ToLowerInvariant();

        // Normalize catch-alls before constrained params to avoid partial matches.
        path = CatchAllParamRegex().Replace(path, "{**}");

        // Normalize {param:constraint}, {param?}, {param=default} → {param}.
        path = ConstrainedParamRegex().Replace(path, "{$1}");

        // Normalize :param (Express / Fastify style) → {param}.
        path = ColonParamRegex().Replace(path, "{$1}");

        // Strip trailing slash unless the path is the root.
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');

        // Ensure path starts with '/'.
        if (!path.StartsWith('/'))
            path = "/" + path;

        return path;
    }

    /// <summary>
    /// Returns a canonical <c>(Method, Path)</c> pair for the given HTTP method and raw URL.
    /// The method is uppercased; the path is normalized via <see cref="NormalizePath"/>.
    /// </summary>
    public static (string Method, string Path) Normalize(string httpMethod, string rawUrl)
    {
        var method = string.IsNullOrEmpty(httpMethod)
            ? string.Empty
            : httpMethod.ToUpperInvariant();

        return (method, NormalizePath(rawUrl));
    }
}
