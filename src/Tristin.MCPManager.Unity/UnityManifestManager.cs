using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Manages Unity Packages/manifest.json: backup, inject Bridge dependency, restore.
/// </summary>
public class UnityManifestManager
{
    public const string BackupFolderName   = ".tristin_mcp_backup";
    public const string BridgePackageName  = "com.tristin.unity-mcp-bridge";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Get the manifest.json path for a project.
    /// </summary>
    public static string GetManifestPath(string projectPath)
        => Path.Combine(projectPath, "Packages", "manifest.json");

    /// <summary>
    /// Get the backup directory path.
    /// </summary>
    public static string GetBackupDir(string projectPath)
        => Path.Combine(projectPath, BackupFolderName);

    /// <summary>
    /// Back up the current manifest.json.
    /// </summary>
    public static Task BackupAsync(string projectPath, CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        var backupDir    = GetBackupDir(projectPath);
        var backupPath   = Path.Combine(backupDir, "manifest.json");

        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"manifest.json not found: {manifestPath}");

        Directory.CreateDirectory(backupDir);

        // If a previous backup exists, archive it with a timestamp
        if (File.Exists(backupPath))
        {
            var tsBackup = Path.Combine(backupDir, $"manifest_{DateTime.Now:yyyyMMdd_HHmmss}.json.bak");
            File.Copy(backupPath, tsBackup, true);
        }

        File.Copy(manifestPath, backupPath, true);

        // Write .gitignore to prevent backup from being committed
        var gitignorePath = Path.Combine(backupDir, ".gitignore");
        File.WriteAllText(gitignorePath, "*\n");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Inject the local Bridge package dependency into manifest.json.
    /// </summary>
    /// <param name="projectPath">Unity project path.</param>
    /// <param name="bridgePackagePath">On-disk path to the Bridge package.</param>
    public static async Task InjectBridgeDependencyAsync(
        string projectPath,
        string bridgePackagePath,
        CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"manifest.json not found: {manifestPath}");

        // Normalize package path (Unity supports "file:/" prefix for local deps)
        var normalizedPath = bridgePackagePath.Replace("\\", "/");
        if (!normalizedPath.StartsWith("file:"))
            normalizedPath = "file:" + normalizedPath;

        var json = await File.ReadAllTextAsync(manifestPath, ct);
        var doc  = JsonNode.Parse(json) ?? new JsonObject();
        var deps = doc["dependencies"] as JsonObject ?? new JsonObject();

        deps[BridgePackageName] = normalizedPath;
        doc["dependencies"]     = deps;

        await using FileStream fs = new(manifestPath, FileMode.Truncate, FileAccess.Write);
        await JsonSerializer.SerializeAsync(fs, doc, JsonOptions, ct);
    }

    /// <summary>
    /// Restore manifest.json from backup and clean up the backup directory.
    /// </summary>
    public static Task<bool> RestoreAsync(string projectPath, CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        var backupDir    = GetBackupDir(projectPath);
        var backupPath   = Path.Combine(backupDir, "manifest.json");

        if (!File.Exists(backupPath))
            return Task.FromResult(false);

        // Restore manifest
        File.Copy(backupPath, manifestPath, true);

        // Clean up backup directory
        try
        {
            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, true);
        }
        catch
        {
            // Deletion failure does not affect the main result
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Check whether the Bridge is currently injected.
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
    /// Check whether a backup exists (used to determine if restore is needed).
    /// </summary>
    public static bool HasBackup(string projectPath)
        => File.Exists(Path.Combine(GetBackupDir(projectPath), "manifest.json"));
}
