using System.Reflection;

[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace Sample.Transitive;

public sealed class SharedContract
{
    public string Value { get; init; } = string.Empty;
}
