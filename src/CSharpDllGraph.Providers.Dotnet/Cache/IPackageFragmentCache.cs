using CSharpDllGraph.Engine.Providers;

namespace CSharpDllGraph.Providers.Dotnet.Cache;

public interface IPackageFragmentCache
{
    Task<GraphFragment?> TryGetAsync(string packageName, string version, CancellationToken ct);

    Task SetAsync(string packageName, string version, GraphFragment fragment, CancellationToken ct);
}
