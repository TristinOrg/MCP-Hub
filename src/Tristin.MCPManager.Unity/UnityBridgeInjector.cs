using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Injects the Coplay bootstrap package through Packages/manifest.json.
/// </summary>
public sealed class UnityBridgeInjector
{
    private readonly CoplayPackageCache    _packageCache;
    private readonly InjectionRecoveryStore _recoveryStore;

    public UnityBridgeInjector(CoplayPackageCache packageCache, InjectionRecoveryStore recoveryStore)
    {
        _packageCache  = packageCache;
        _recoveryStore = recoveryStore;
    }

    /// <summary>
    /// Local path to the bootstrap package directory.
    /// </summary>
    public required string BridgePackagePath { get; init; }

    public async Task<bool> InjectAsync(
        EditorInstance                              instance,
        IProgress<(int percent, string message)>?  progress   = null,
        CancellationToken                           ct         = default)
    {
        if (instance.EditorType != "Unity")
            throw new ArgumentException("Only Unity editor supported", nameof(instance));

        if (!Directory.Exists(instance.ProjectPath))
        {
            instance.ErrorMessage = $"Project path not found: {instance.ProjectPath}";
            return false;
        }

        try
        {
            var coplayPackagePath = await _packageCache.PrepareAsync(progress, ct);

            progress?.Report((40, "Backing up Unity package state ..."));
            await UnityManifestManager.BackupAsync(instance.ProjectPath, ct);
            await _recoveryStore.RegisterAsync(instance.ProjectPath, ct);

            progress?.Report((60, "Injecting cached Coplay packages ..."));
            await UnityManifestManager.InjectDependenciesAsync(
                instance.ProjectPath,
                BridgePackagePath,
                coplayPackagePath,
                ct);

            UnityWindowActivator.ActivateUnityWindow(instance.ProcessId);
            progress?.Report((100, "Cached Coplay package injected. Waiting for Unity package reload ..."));
            return true;
        }
        catch (Exception ex)
        {
            instance.ErrorMessage = $"Inject failed: {ex.Message}";
            try
            {
                if (await UnityManifestManager.RestoreAsync(instance.ProjectPath, CancellationToken.None))
                    await _recoveryStore.CompleteAsync(instance.ProjectPath, CancellationToken.None);
            }
            catch { }
            return false;
        }
    }

    public async Task<bool> CleanupAsync(EditorInstance instance, CancellationToken ct = default)
    {
        if (instance.EditorType != "Unity") return false;
        try
        {
            if (!UnityManifestManager.HasBackup(instance.ProjectPath))
                return !await UnityManifestManager.IsBridgeInjectedAsync(instance.ProjectPath, ct);

            var restored = await UnityManifestManager.RestoreAsync(instance.ProjectPath, ct);
            if (restored)
                await _recoveryStore.CompleteAsync(instance.ProjectPath, ct);
            return restored;
        }
        catch (Exception ex)
        {
            instance.ErrorMessage = $"Cleanup failed: {ex.Message}";
            return false;
        }
    }

    public Task<bool> IsInjectedAsync(EditorInstance instance, CancellationToken ct = default)
    {
        if (instance.EditorType != "Unity") return Task.FromResult(false);
        return UnityManifestManager.IsBridgeInjectedAsync(instance.ProjectPath, ct);
    }

    /// <summary>
    /// Restores projects left mutated by an unclean Hub shutdown.
    /// </summary>
    public async Task<IReadOnlyList<string>> RecoverPendingAsync(CancellationToken ct = default)
    {
        List<string> restored = [];
        foreach (var projectPath in await _recoveryStore.ListAsync(ct))
        {
            if (!UnityManifestManager.HasBackup(projectPath))
            {
                await _recoveryStore.CompleteAsync(projectPath, ct);
                continue;
            }

            if (await UnityManifestManager.RestoreAsync(projectPath, ct))
            {
                await _recoveryStore.CompleteAsync(projectPath, ct);
                restored.Add(projectPath);
            }
        }
        return restored;
    }
}
