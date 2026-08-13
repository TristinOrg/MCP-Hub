// Injects the Bridge into a Unity Editor by modifying Packages/manifest.json.
// Unity's Package Manager resolves the local Bridge package on startup.
// The Bridge [InitializeOnLoad] fires after domain reload and registers via IPC.
//
// Author: Tristin Wen
// Email:  Tristin_Wen@outlook.com

using Tristin.MCPManager.Core.Interfaces;
using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Injects Bridge by adding a local package dependency to manifest.json.
/// Unity resolves it on next startup / domain reload.
/// </summary>
public class UnityBridgeInjector : IBridgeInjector
{
    public string EditorType => "Unity";

    /// <summary>
    /// Local path to the Bridge package directory (contains package.json + Runtime/).
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
            progress?.Report((10, "Backup Packages/manifest.json ..."));
            await UnityManifestManager.BackupAsync(instance.ProjectPath, ct);

            progress?.Report((50, "Inject MCP Bridge package dependency ..."));
            await UnityManifestManager.InjectBridgeDependencyAsync(
                instance.ProjectPath, BridgePackagePath, ct);

            progress?.Report((100, "Manifest injected. Restart Unity Editor to load Bridge."));
            return true;
        }
        catch (Exception ex)
        {
            instance.ErrorMessage = $"Inject failed: {ex.Message}";
            try { await UnityManifestManager.RestoreAsync(instance.ProjectPath, ct); } catch { /* ignore */ }
            return false;
        }
    }

    public async Task<bool> CleanupAsync(EditorInstance instance, CancellationToken ct = default)
    {
        if (instance.EditorType != "Unity") return false;
        try
        {
            await UnityManifestManager.RestoreAsync(instance.ProjectPath, ct);
            return true;
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
}
