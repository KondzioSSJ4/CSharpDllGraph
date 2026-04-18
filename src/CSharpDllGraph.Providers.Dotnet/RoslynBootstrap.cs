using Microsoft.Build.Locator;

namespace CSharpDllGraph.Providers.Dotnet;

public static class RoslynBootstrap
{
    private static readonly object Sync = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered || MSBuildLocator.IsRegistered)
        {
            _registered = true;
            return;
        }

        lock (Sync)
        {
            if (_registered || MSBuildLocator.IsRegistered)
            {
                _registered = true;
                return;
            }

            MSBuildLocator.RegisterDefaults();
            _registered = true;
        }
    }
}
