using System.Security.Cryptography;
using CSharpDllGraph.Engine.Graph;
using CSharpDllGraph.Engine.Store;

namespace CSharpDllGraph.Engine.Tests;

public sealed class WorkspaceStoreRoundTripTests
{
    [Fact]
    public async Task SerializeDeserializeReserialize_IsByteIdentical()
    {
        var workspacePath = CreateWorkspacePath();
        var store = new JsonWorkspaceStore(workspacePath);
        var snapshot = GraphFixtureBuilder.BuildSnapshot();

        await store.SaveAsync(snapshot);
        var initialBytes = ReadWorkspaceBytes(workspacePath);

        var loaded = await store.LoadAsync();
        await store.SaveAsync(loaded);
        var secondBytes = ReadWorkspaceBytes(workspacePath);

        AssertByteMapsEqual(initialBytes, secondBytes);
    }

    [Fact]
    public async Task TwoIndependentRuns_ProduceIdenticalBytes()
    {
        var firstWorkspacePath = CreateWorkspacePath();
        var secondWorkspacePath = CreateWorkspacePath();
        var snapshot = GraphFixtureBuilder.BuildSnapshot();

        await new JsonWorkspaceStore(firstWorkspacePath).SaveAsync(snapshot);
        await new JsonWorkspaceStore(secondWorkspacePath).SaveAsync(snapshot);

        var first = ReadWorkspaceBytes(firstWorkspacePath);
        var second = ReadWorkspaceBytes(secondWorkspacePath);
        AssertByteMapsEqual(first, second);
    }

    private static string CreateWorkspacePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"csharpdllgraph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static IReadOnlyDictionary<string, byte[]> ReadWorkspaceBytes(string workspacePath)
    {
        return Directory
            .GetFiles(workspacePath, "*.json", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(workspacePath, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);
    }

    private static void AssertByteMapsEqual(
        IReadOnlyDictionary<string, byte[]> first,
        IReadOnlyDictionary<string, byte[]> second)
    {
        Assert.Equal(first.Keys.OrderBy(static key => key), second.Keys.OrderBy(static key => key));

        foreach (var key in first.Keys)
        {
            var leftHash = SHA256.HashData(first[key]);
            var rightHash = SHA256.HashData(second[key]);
            Assert.Equal(Convert.ToHexString(leftHash), Convert.ToHexString(rightHash));
            Assert.Equal(first[key], second[key]);
        }
    }
}
