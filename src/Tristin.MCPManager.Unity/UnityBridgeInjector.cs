using Tristin.MCPManager.Core.Interfaces;
using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Orchestrates the Unity Bridge lifecycle: backup → inject → cleanup.
/// Does NOT wait for Unity recompilation — the caller polls for Bridge
/// registration via IPC, which is more reliable and faster.
/// </summary>
public class UnityBridgeInjector : IBridgeInjector
{
    public string EditorType => "Unity";

    /// <summary>
    /// Local path to the Bridge package (shipped alongside the UI).
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
            // Step 1: backup manifest
            progress?.Report((10, "Backup Packages/manifest.json ..."));
            await UnityManifestManager.BackupAsync(instance.ProjectPath, ct);

            // Step 2: inject dependency — Unity will detect the change,
            // resolve packages, recompile, and domain-reload automatically.
            // The Bridge [InitializeOnLoad] fires after reload and registers
            // via IPC. The caller polls for that registration.
            progress?.Report((50, "Inject MCP Bridge package dependency ..."));
            await UnityManifestManager.InjectBridgeDependencyAsync(
                instance.ProjectPath, BridgePackagePath, ct);

            progress?.Report((100, "Manifest injected. Unity will reload and Bridge will auto-register."));
            return true;
        }
        catch (Exception ex)
        {
            instance.ErrorMessage = $"Inject failed: {ex.Message}";
            // Attempt rollback
            try { await RestoreAsync(instance.ProjectPath, ct); } catch { /* ignore */ }
            return false;
        }
    }

    public async Task<bool> CleanupAsync(EditorInstance instance, CancellationToken ct = default)
    {
        if (instance.EditorType != "Unity") return false;

        try
        {
            await RestoreAsync(instance.ProjectPath, ct);
            return true;
        }
        catch (Exception ex)
        {
            instance.ErrorMessage = $"Cleanup failed: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> IsInjectedAsync(EditorInstance instance, CancellationToken ct = default)
    {
        if (instance.EditorType != "Unity") return false;
        return await UnityManifestManager.IsBridgeInjectedAsync(instance.ProjectPath, ct);
    }

    private static Task RestoreAsync(string projectPath, CancellationToken ct)
        => UnityManifestManager.RestoreAsync(projectPath, ct);
}
