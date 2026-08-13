// Injects a thin bootstrap package that loads and connects the official Coplay package.
//
// Author: Tristin Wen
// Email:  Tristin_Wen@outlook.com

using Tristin.MCPManager.Core.Interfaces;
using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Injects the Coplay bootstrap package through Packages/manifest.json.
/// </summary>
public class UnityBridgeInjector : IBridgeInjector
{
    public string EditorType => "Unity";

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
            progress?.Report((10, "Backup Packages/manifest.json ..."));
            await UnityManifestManager.BackupAsync(instance.ProjectPath, ct);

            progress?.Report((50, "Inject Coplay MCP package ..."));
            await UnityManifestManager.InjectBridgeDependencyAsync(
                instance.ProjectPath, BridgePackagePath, ct);

            UnityWindowActivator.ActivateUnityWindow(instance.ProcessId);
            progress?.Report((100, "Coplay package injected. Waiting for Unity package reload ..."));
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
