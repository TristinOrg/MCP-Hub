namespace Tristin.MCPManager.Unity.Tests;

/// <summary>
/// Verifies persistent recovery registration for interrupted injections.
/// </summary>
public sealed class InjectionRecoveryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"unity-mcp-hub-recovery-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task RegisterAndComplete_PersistUniqueCanonicalPaths()
    {
        var statePath   = Path.Combine(_root, "state.json");
        var projectPath = Path.Combine(_root, "Project");
        var store       = new InjectionRecoveryStore(statePath);

        await store.RegisterAsync(projectPath);
        await store.RegisterAsync(projectPath);

        Assert.Equal([Path.GetFullPath(projectPath)], await store.ListAsync());

        await store.CompleteAsync(projectPath);
        Assert.Empty(await store.ListAsync());
        Assert.False(File.Exists(statePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
