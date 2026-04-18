#nullable enable

using Sample.Transitive;
using Sample.WidgetKit;

namespace Sample.AppV2;

public static class Usage
{
    public static string Run(string? id)
    {
        ModernWidgetService service = new();
        IWidgetService contract = service;
        var marker = new SharedContract();
        var first = contract.Get(id);
        var second = contract.Get(first);
        var normalized = service.GetNormalized(second);
        return normalized + marker.Value;
    }
}
