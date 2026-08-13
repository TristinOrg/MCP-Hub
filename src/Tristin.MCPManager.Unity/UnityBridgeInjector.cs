using Tristin.MCPManager.Core.Interfaces;
using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Orchestrates the full Unity Bridge lifecycle: backup → inject → wait for reload → cleanup.
/// </summary>
public class UnityBridgeInjector : IBridgeInjector
{
    public string EditorType => "Unity";

    /// <summary>
    /// Local path to the Bridge package (shipped alongside the UI).
    /// </summary>
    public required string BridgePackagePath { get; init; }

    /// <summary>
    /// Polling interval (ms) when waiting for Unity domain reload.
    /// </summary>
    public int ReloadPollIntervalMs { get; init; } = 1000;

    /// <summary>
    /// Maximum time (seconds) to wait for Unity recompilation.
    /// </summary>
    public int MaxReloadWaitSeconds { get; init; } = 180;

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
            progress?.Report((5, "Backup Packages/manifest.json ..."));
            await UnityManifestManager.BackupAsync(instance.ProjectPath, ct);

            // Step 2: inject dependency
            progress?.Report((20, "Inject MCP Bridge package dependency ..."));
            await UnityManifestManager.InjectBridgeDependencyAsync(
                instance.ProjectPath, BridgePackagePath, ct);

            // Step 3: wait for Unity to detect manifest change and reload
            progress?.Report((35, "Waiting Unity Editor to detect manifest change and resolve packages ..."));

            await WaitForReloadStableAsync(instance, progress, ct);

            progress?.Report((100, "Bridge injected. Waiting Bridge to register via IPC ..."));
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

    /// <summary>
    /// Heuristically wait for Unity to finish package resolve + compile + reload
    /// by monitoring the last-write time of Library/ScriptAssemblies/Assembly-CSharp.dll.
    /// </summary>
    private async Task WaitForReloadStableAsync(
        EditorInstance                              instance,
        IProgress<(int percent, string message)>?  progress,
        CancellationToken                           ct)
    {
        var scriptAsmDir = Path.Combine(instance.ProjectPath, "Library", "ScriptAssemblies");
        var markerFile   = Path.Combine(scriptAsmDir, "Assembly-CSharp.dll");

        DateTime? initialWriteTime = null;
        if (File.Exists(markerFile))
            initialWriteTime = File.GetLastWriteTimeUtc(markerFile);

        var startTime = DateTime.UtcNow;
        var percent   = 35;

        while ((DateTime.UtcNow - startTime).TotalSeconds < MaxReloadWaitSeconds)
        {
            ct.ThrowIfCancellationRequested();

            // Check if reload completed: dll write time changed and then stabilized for 3s
            bool seemsStable = false;
            if (File.Exists(markerFile))
            {
                var currentWrite = File.GetLastWriteTimeUtc(markerFile);
                var sinceChange  = DateTime.UtcNow - currentWrite;
                if ((initialWriteTime == null || currentWrite != initialWriteTime)
                    && sinceChange.TotalSeconds >= 3)
                {
                    seemsStable = true;
                }
            }

            if (seemsStable)
                break;

            percent = Math.Min(90, percent + 1);
            progress?.Report((percent,
                $"Waiting Unity reload ... ({(int)(DateTime.UtcNow - startTime).TotalSeconds}s / {MaxReloadWaitSeconds}s)"));

            await Task.Delay(ReloadPollIntervalMs, ct);
        }
    }

    private static Task RestoreAsync(string projectPath, CancellationToken ct)
        => UnityManifestManager.RestoreAsync(projectPath, ct);
}
