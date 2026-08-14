using System.Text.Json.Nodes;

namespace Tristin.MCPManager.Unity.Tests;

/// <summary>
/// Verifies reversible Unity package-state transactions.
/// </summary>
public sealed class UnityManifestManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"unity-mcp-hub-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task RestoreAsync_RestoresExistingManifestAndLockFile()
    {
        var projectPath = CreateProject("{\"dependencies\":{\"original\":\"1.0.0\"}}", "{\"dependencies\":{\"original\":{}}}");
        var coplayPath  = CreatePackage("coplay");

        await UnityManifestManager.BackupAsync(projectPath);
        await UnityManifestManager.InjectDependenciesAsync(projectPath, coplayPath);
        await File.WriteAllTextAsync(UnityManifestManager.GetLockPath(projectPath), "mutated");

        Assert.True(await UnityManifestManager.RestoreAsync(projectPath));
        Assert.Equal("{\"dependencies\":{\"original\":\"1.0.0\"}}", await File.ReadAllTextAsync(UnityManifestManager.GetManifestPath(projectPath)));
        Assert.Equal("{\"dependencies\":{\"original\":{}}}", await File.ReadAllTextAsync(UnityManifestManager.GetLockPath(projectPath)));
        Assert.False(UnityManifestManager.HasBackup(projectPath));
    }

    [Fact]
    public async Task RestoreAsync_RemovesLockFileWhenOriginallyMissing()
    {
        var projectPath = CreateProject("{\"dependencies\":{}}");
        var coplayPath  = CreatePackage("coplay");

        await UnityManifestManager.BackupAsync(projectPath);
        await UnityManifestManager.InjectDependenciesAsync(projectPath, coplayPath);
        await File.WriteAllTextAsync(UnityManifestManager.GetLockPath(projectPath), "generated");

        Assert.True(await UnityManifestManager.RestoreAsync(projectPath));
        Assert.False(File.Exists(UnityManifestManager.GetLockPath(projectPath)));
    }

    [Fact]
    public async Task RestoreAsync_PreservesManifestBytesExactly()
    {
        var projectPath  = CreateProject("{\"dependencies\":{}}");
        var manifestPath = UnityManifestManager.GetManifestPath(projectPath);
        var original     = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(System.Text.Encoding.UTF8.GetBytes("{\r\n  \"dependencies\": {}\r\n}\r\n"))
            .ToArray();
        var coplayPath = CreatePackage("coplay");
        await File.WriteAllBytesAsync(manifestPath, original);

        await UnityManifestManager.BackupAsync(projectPath);
        await UnityManifestManager.InjectDependenciesAsync(projectPath, coplayPath);

        Assert.True(await UnityManifestManager.RestoreAsync(projectPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(manifestPath));
    }

    [Fact]
    public async Task InjectDependenciesAsync_UsesSingleLocalCoplayReference()
    {
        var projectPath = CreateProject("{\"dependencies\":{}}");
        var coplayPath  = CreatePackage("coplay");

        await UnityManifestManager.InjectDependenciesAsync(projectPath, coplayPath);

        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(UnityManifestManager.GetManifestPath(projectPath)))!;
        Assert.StartsWith("file:", manifest["dependencies"]![UnityManifestManager.CoplayPackageName]!.GetValue<string>());
        Assert.Single(manifest["dependencies"]!.AsObject());
    }

    private string CreateProject(string manifest, string? lockContent = null)
    {
        var projectPath = Path.Combine(_root, $"project-{Guid.NewGuid():N}");
        var packagesPath = Path.Combine(projectPath, "Packages");
        Directory.CreateDirectory(packagesPath);
        File.WriteAllText(Path.Combine(packagesPath, "manifest.json"), manifest);
        if (lockContent != null)
            File.WriteAllText(Path.Combine(packagesPath, "packages-lock.json"), lockContent);
        return projectPath;
    }

    private string CreatePackage(string name)
    {
        var packagePath = Path.Combine(_root, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packagePath);
        return packagePath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
