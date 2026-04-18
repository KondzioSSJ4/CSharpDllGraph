using System.Reflection;

[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace Sample.WidgetKit;

public interface IWidgetService
{
    string Get(string id);
}

public class WidgetBase
{
}

public sealed class ModernWidgetService : WidgetBase, IWidgetService
{
    public string Get(string id) => id.Trim();

    public string GetNormalized(string id) => id.Trim().ToUpperInvariant();
}
