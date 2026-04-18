using CSharpDllGraph.Engine.Http;

namespace CSharpDllGraph.Providers.Dotnet.Tests;

public sealed class RouteNormalizerTests
{
    // ── NormalizePath ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(":id",              "/{id}")]    // Express colon param
    [InlineData("/api/:id",         "/api/{id}")]
    [InlineData("/api/:userId/orders/:orderId", "/api/{userid}/orders/{orderid}")]
    public void ColonParams_AreNormalizedToCurlyBrace(string input, string expected)
    {
        Assert.Equal(expected, HttpRouteNormalizer.NormalizePath(input));
    }

    [Theory]
    [InlineData("{id:int}",             "/{id}")]    // ASP.NET constraint
    [InlineData("/api/{id:int}",        "/api/{id}")]
    [InlineData("/api/{name:minlength(2)}", "/api/{name}")]
    public void ConstraintParams_AreStripped(string input, string expected)
    {
        Assert.Equal(expected, HttpRouteNormalizer.NormalizePath(input));
    }

    [Theory]
    [InlineData("{id?}",            "/{id}")]    // optional parameter
    [InlineData("/api/{id?}",       "/api/{id}")]
    [InlineData("/api/{page?}",     "/api/{page}")]
    public void OptionalParams_AreNormalized(string input, string expected)
    {
        Assert.Equal(expected, HttpRouteNormalizer.NormalizePath(input));
    }

    [Theory]
    [InlineData("/api/users/",      "/api/users")]   // trailing slash stripped
    [InlineData("/api/users//",     "/api/users")]
    public void TrailingSlash_IsStripped(string input, string expected)
    {
        Assert.Equal(expected, HttpRouteNormalizer.NormalizePath(input));
    }

    [Fact]
    public void RootSlash_IsPreserved()
    {
        Assert.Equal("/", HttpRouteNormalizer.NormalizePath("/"));
    }

    [Theory]
    [InlineData("https://example.com/api/users",        "/api/users")]
    [InlineData("https://example.com/api/users/",       "/api/users")]
    [InlineData("http://EXAMPLE.COM/Api/Users",         "/api/users")]
    [InlineData("https://example.com/api/{id:int}",     "/api/{id}")]
    public void FullUrl_HostIsStripped_PathIsNormalized(string input, string expected)
    {
        Assert.Equal(expected, HttpRouteNormalizer.NormalizePath(input));
    }

    [Theory]
    [InlineData("/API/USERS",   "/api/users")]      // uppercase path lowercased
    [InlineData("/Api/Users",   "/api/users")]
    public void Path_IsLowercased(string input, string expected)
    {
        Assert.Equal(expected, HttpRouteNormalizer.NormalizePath(input));
    }

    /// <summary>
    /// Catch-all parameters ({**path} and {*path}) normalize to {**}.
    /// This preserves the intent of a wildcard match while collapsing
    /// different naming conventions into one canonical token.
    /// </summary>
    [Theory]
    [InlineData("{**path}",             "/{**}")]
    [InlineData("/files/{**path}",      "/files/{**}")]
    [InlineData("/files/{*remainder}",  "/files/{**}")]
    public void CatchAll_NormalizesToDoubleStar(string input, string expected)
    {
        Assert.Equal(expected, HttpRouteNormalizer.NormalizePath(input));
    }

    // ── Normalize (method + path) ──────────────────────────────────────────────

    [Theory]
    [InlineData("get",      "GET")]
    [InlineData("Get",      "GET")]
    [InlineData("POST",     "POST")]
    [InlineData("delete",   "DELETE")]
    public void HttpMethod_IsUppercased(string input, string expectedMethod)
    {
        var (method, _) = HttpRouteNormalizer.Normalize(input, "/api/users");
        Assert.Equal(expectedMethod, method);
    }

    [Fact]
    public void Normalize_ReturnsBothMethodAndPath()
    {
        var (method, path) = HttpRouteNormalizer.Normalize("get", "/api/users/{id:int}/");
        Assert.Equal("GET", method);
        Assert.Equal("/api/users/{id}", path);
    }

    [Fact]
    public void Normalize_SameLogicalRoute_ProducerAndConsumer_Match()
    {
        // Producer registers: GET /api/orders/{id:int}
        var producer = HttpRouteNormalizer.Normalize("GET", "/api/orders/{id:int}");

        // Consumer calls: get https://service/api/orders/:id/
        var consumer = HttpRouteNormalizer.Normalize("get", "https://service/api/orders/:id/");

        Assert.Equal(producer, consumer);
    }
}
