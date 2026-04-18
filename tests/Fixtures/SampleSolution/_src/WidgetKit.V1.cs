using System.Reflection;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Sample.WidgetKit;

public interface IWidgetService
{
    string Get(string id);
}

public class WidgetBase
{
}

public sealed class LegacyWidgetService : WidgetBase, IWidgetService, System.IDisposable
{
    public string Get(string id) => id;

    public void Dispose()
    {
    }
}
