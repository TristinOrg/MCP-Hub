// Manages Unity Packages/manifest.json: backup, inject Bridge dependency, restore.
// The key insight: Unity's Package Manager FileSystemWatcher only triggers on
// actual content changes to manifest.json — touching timestamps or deleting
// packages-lock.json is NOT reliable. We must write new content to the file.
//
// Author: Tristin Wen
// Email:  Tristin_Wen@outlook.com

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Manages Unity Packages/manifest.json for Bridge injection and cleanup.
/// </summary>
public static class UnityManifestManager
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

        // An existing backup is the original pre-injection manifest. Never overwrite it
        // with an already modified manifest when Connect is invoked repeatedly.
        if (File.Exists(backupPath))
            return Task.CompletedTask;

        Directory.CreateDirectory(backupDir);
        File.Copy(manifestPath, backupPath, true);

        // Write .gitignore to prevent backup from being committed
        var gitignorePath = Path.Combine(backupDir, ".gitignore");
        File.WriteAllText(gitignorePath, "*\n");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Inject the local Bridge package dependency into manifest.json.
    /// CRITICAL: We must WRITE NEW CONTENT to trigger Unity's FileSystemWatcher.
    /// Touching timestamps or deleting packages-lock.json does NOT reliably
    /// trigger Unity's Package Manager re-resolution.
    /// </summary>
    public static async Task InjectBridgeDependencyAsync(
        string projectPath,
        string bridgePackagePath,
        CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"manifest.json not found: {manifestPath}");

        // The wrapper package contains only connection bootstrap code and depends on Coplay.
        var normalizedPath = bridgePackagePath.Replace("\\", "/");
        if (!normalizedPath.StartsWith("file:"))
            normalizedPath = "file:" + normalizedPath;

        // Read current manifest
        var json = await File.ReadAllTextAsync(manifestPath, ct);
        var doc  = JsonNode.Parse(json) ?? new JsonObject();
        var deps = doc["dependencies"] as JsonObject ?? new JsonObject();

        // Add/update the bridge dependency
        deps[BridgePackageName] = normalizedPath;
        doc["dependencies"]     = deps;

        // Serialize to new JSON string
        var newContent = JsonSerializer.Serialize(doc, JsonOptions);

        // CRITICAL: Write new content to trigger Unity's FileSystemWatcher.
        // This MUST be a genuine content change — Unity watches for Changed events,
        // not just timestamp modifications.
        await File.WriteAllTextAsync(manifestPath, newContent, new System.Text.UTF8Encoding(false), ct);

        // Small delay to ensure Unity's FSW has time to observe the change
        try { await Task.Delay(200, ct); }
        catch (OperationCanceledException) { /* ignore */ }
    }

    /// <summary>
    /// Restore manifest.json from backup and clean up the backup directory.
    /// </summary>
    public static async Task<bool> RestoreAsync(string projectPath, CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        var backupDir    = GetBackupDir(projectPath);
        var backupPath   = Path.Combine(backupDir, "manifest.json");

        if (!File.Exists(backupPath))
            return false;

        // Restore manifest by writing the backed-up content
        var backupContent = await File.ReadAllTextAsync(backupPath, System.Text.Encoding.UTF8, ct);
        await File.WriteAllTextAsync(manifestPath, backupContent, new System.Text.UTF8Encoding(false), ct);

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

        return true;
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
