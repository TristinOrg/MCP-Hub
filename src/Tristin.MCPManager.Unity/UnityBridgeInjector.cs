// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    UnityBridgeInjector.cs
// ============================================================

using Tristin.MCPManager.Core.Interfaces;
using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Unity Bridge 注入器：封装 manifest 备份 → 注入 → 等待 → 清理 全流程
/// </summary>
public class UnityBridgeInjector : IBridgeInjector
{
    public string EditorType => "Unity";

    /// <summary>
    /// Bridge Package 的本地路径（通常随 UI 一起分发）
    /// </summary>
    public required string BridgePackagePath { get; init; }

    /// <summary>
    /// 等待 Unity 完成 Domain Reload 的轮询间隔
    /// </summary>
    public int ReloadPollIntervalMs { get; init; } = 1000;

    /// <summary>
    /// 等待 Unity 重新编译的最大时长
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
            // Step 1: 备份 manifest
            progress?.Report((5, "Backup Packages/manifest.json ..."));
            await UnityManifestManager.BackupAsync(instance.ProjectPath, ct);

            // Step 2: 注入依赖
            progress?.Report((20, "Inject MCP Bridge package dependency ..."));
            await UnityManifestManager.InjectBridgeDependencyAsync(
                instance.ProjectPath, BridgePackagePath, ct);

            // Step 3: 等待 Unity 自动检测 manifest 变化并完成 reload
            progress?.Report((35, "Waiting Unity Editor to detect manifest change and resolve packages ..."));

            // Unity 对 manifest.json 的检测并不总是实时的，这里做一个启发式等待：
            // 如果注入后 5s 还没看到编译状态，说明 Unity 可能没自动刷新，
            // 但大多数情况下 Unity 2021+ 会自动检测文件变化。
            await WaitForReloadStableAsync(instance, progress, ct);

            progress?.Report((100, "Bridge injected. Waiting Bridge to register via IPC ..."));
            return true;
        }
        catch (Exception ex)
        {
            instance.ErrorMessage = $"Inject failed: {ex.Message}";
            // 尝试回滚
            try { await RestoreAsync(instance.ProjectPath, ct); } catch { /* 忽略 */ }
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
    /// 启发式等待 Unity 完成 package resolve + compile + reload
    /// 通过检查 Library/ScriptAssemblies/*.dll 最后修改时间的变化来判断。
    /// 这里采用简化版：等待一个固定的稳定期。
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

            // 检查是否重新加载完成：dll 文件修改时间是否变化后稳定
            bool seemsStable = false;
            if (File.Exists(markerFile))
            {
                var currentWrite = File.GetLastWriteTimeUtc(markerFile);
                var sinceChange  = DateTime.UtcNow - currentWrite;
                // 修改时间变化后且已经过了至少 3s 没有再次变化 → 认为稳定
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
