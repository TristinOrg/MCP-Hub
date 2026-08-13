// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    UnityManifestManager.cs
// ============================================================

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Unity Packages/manifest.json 管理器
/// 负责备份、注入 MCP Bridge 依赖、恢复原始状态
/// </summary>
public class UnityManifestManager
{
    public const string BackupFolderName = ".tristin_mcp_backup";
    public const string BridgePackageName = "com.tristin.unity-mcp-bridge";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 获取 manifest.json 路径
    /// </summary>
    public static string GetManifestPath(string projectPath)
        => Path.Combine(projectPath, "Packages", "manifest.json");

    /// <summary>
    /// 获取备份目录
    /// </summary>
    public static string GetBackupDir(string projectPath)
        => Path.Combine(projectPath, BackupFolderName);

    /// <summary>
    /// 备份当前 manifest.json
    /// </summary>
    public static Task BackupAsync(string projectPath, CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        var backupDir    = GetBackupDir(projectPath);
        var backupPath   = Path.Combine(backupDir, "manifest.json");

        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"manifest.json not found: {manifestPath}");

        Directory.CreateDirectory(backupDir);

        // 如果已有备份，说明上次未正常清理，直接覆盖（但先做时间戳副本以防万一）
        if (File.Exists(backupPath))
        {
            var tsBackup = Path.Combine(backupDir, $"manifest_{DateTime.Now:yyyyMMdd_HHmmss}.json.bak");
            File.Copy(backupPath, tsBackup, true);
        }

        File.Copy(manifestPath, backupPath, true);

        // 写入 .gitignore 防止备份目录被提交
        var gitignorePath = Path.Combine(backupDir, ".gitignore");
        File.WriteAllText(gitignorePath, "*\n");

        return Task.CompletedTask;
    }

    /// <summary>
    /// 向 manifest.json 注入本地 Bridge 包依赖
    /// </summary>
    /// <param name="projectPath">Unity 项目路径</param>
    /// <param name="bridgePackagePath">Bridge package 磁盘路径</param>
    public static async Task InjectBridgeDependencyAsync(
        string projectPath,
        string bridgePackagePath,
        CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"manifest.json not found: {manifestPath}");

        // 规范化 package 路径（Unity 支持 "file:/" 前缀的本地依赖）
        var normalizedPath = bridgePackagePath.Replace("\\", "/");
        if (!normalizedPath.StartsWith("file:"))
            normalizedPath = "file:" + normalizedPath;

        var json = await File.ReadAllTextAsync(manifestPath, ct);
        var doc  = JsonNode.Parse(json) ?? new JsonObject();
        var deps = doc["dependencies"] as JsonObject ?? new JsonObject();

        deps[BridgePackageName] = normalizedPath;
        doc["dependencies"]     = deps;

        // 保留原始缩进风格：2 空格
        await using var fs = new FileStream(manifestPath, FileMode.Truncate, FileAccess.Write);
        await JsonSerializer.SerializeAsync(fs, doc, JsonOptions, ct);
    }

    /// <summary>
    /// 恢复 manifest.json 为备份状态，并清理备份目录
    /// </summary>
    public static Task<bool> RestoreAsync(string projectPath, CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        var backupDir    = GetBackupDir(projectPath);
        var backupPath   = Path.Combine(backupDir, "manifest.json");

        if (!File.Exists(backupPath))
            return Task.FromResult(false);

        // 恢复 manifest
        File.Copy(backupPath, manifestPath, true);

        // 清理备份目录（保留目录空壳也可以，这里直接全部删）
        try
        {
            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, true);
        }
        catch
        {
            // 删除失败不影响主要结果
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// 检查是否已注入 Bridge
    /// </summary>
    public static async Task<bool> IsBridgeInjectedAsync(string projectPath, CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        if (!File.Exists(manifestPath)) return false;

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, ct);
            var doc  = JsonNode.Parse(json);
            return doc?["dependencies"]?[BridgePackageName] != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查备份是否存在（用于判断是否需要恢复）
    /// </summary>
    public static bool HasBackup(string projectPath)
        => File.Exists(Path.Combine(GetBackupDir(projectPath), "manifest.json"));
}
