using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Applies and restores transactional Unity package changes made by the Hub.
/// </summary>
public static class UnityManifestManager
{
    public const string BackupFolderName  = ".tristin_mcp_backup";
    public const string CoplayPackageName = CoplayPackageCache.PackageName;

    private const string ManifestFileName    = "manifest.json";
    private const string LockFileName        = "packages-lock.json";
    private const string MissingLockFileName = "packages-lock.missing";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Get the manifest.json path for a project.
    /// </summary>
    public static string GetManifestPath(string projectPath)
        => Path.Combine(projectPath, "Packages", ManifestFileName);

    /// <summary>
    /// Gets the packages-lock.json path for a project.
    /// </summary>
    public static string GetLockPath(string projectPath)
        => Path.Combine(projectPath, "Packages", LockFileName);

    /// <summary>
    /// Get the backup directory path.
    /// </summary>
    public static string GetBackupDir(string projectPath)
        => Path.Combine(projectPath, BackupFolderName);

    /// <summary>
    /// Backs up package state exactly once, including whether packages-lock.json existed.
    /// </summary>
    public static async Task BackupAsync(string projectPath, CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        var backupDir    = GetBackupDir(projectPath);
        var backupPath   = Path.Combine(backupDir, ManifestFileName);

        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"manifest.json not found: {manifestPath}");

        // An existing backup is the original pre-injection manifest. Never overwrite it
        // with an already modified manifest when Connect is invoked repeatedly.
        if (File.Exists(backupPath))
            return;

        Directory.CreateDirectory(backupDir);
        await CopyAsync(manifestPath, backupPath, ct);

        var lockPath       = GetLockPath(projectPath);
        var backupLockPath = Path.Combine(backupDir, LockFileName);
        var missingMarker  = Path.Combine(backupDir, MissingLockFileName);
        if (File.Exists(lockPath))
            await CopyAsync(lockPath, backupLockPath, ct);
        else
            await File.WriteAllTextAsync(missingMarker, string.Empty, new System.Text.UTF8Encoding(false), ct);

        var gitignorePath = Path.Combine(backupDir, ".gitignore");
        await File.WriteAllTextAsync(gitignorePath, "*\n", new System.Text.UTF8Encoding(false), ct);
    }

    /// <summary>
    /// Injects the Hub-integrated local Coplay dependency into manifest.json.
    /// </summary>
    public static async Task InjectDependenciesAsync(
        string projectPath,
        string coplayPackagePath,
        CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"manifest.json not found: {manifestPath}");

        var json = await File.ReadAllTextAsync(manifestPath, System.Text.Encoding.UTF8, ct);
        var doc  = JsonNode.Parse(json) ?? new JsonObject();
        var deps = doc["dependencies"] as JsonObject ?? new JsonObject();

        deps[CoplayPackageName] = ToFileDependency(coplayPackagePath);
        doc["dependencies"]    = deps;

        var newContent = JsonSerializer.Serialize(doc, JsonOptions);
        await WriteAtomicallyAsync(manifestPath, newContent, ct);
    }

    /// <summary>
    /// Restore manifest.json from backup and clean up the backup directory.
    /// </summary>
    public static async Task<bool> RestoreAsync(string projectPath, CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        var backupDir    = GetBackupDir(projectPath);
        var backupPath   = Path.Combine(backupDir, ManifestFileName);

        if (!File.Exists(backupPath))
            return false;

        var backupContent = await File.ReadAllTextAsync(backupPath, System.Text.Encoding.UTF8, ct);
        await WriteAtomicallyAsync(manifestPath, backupContent, ct);

        var lockPath       = GetLockPath(projectPath);
        var backupLockPath = Path.Combine(backupDir, LockFileName);
        var missingMarker  = Path.Combine(backupDir, MissingLockFileName);
        if (File.Exists(backupLockPath))
            await CopyAsync(backupLockPath, lockPath, ct);
        else if (File.Exists(missingMarker) && File.Exists(lockPath))
            File.Delete(lockPath);

        Directory.Delete(backupDir, recursive: true);

        return true;
    }

    /// <summary>
    /// Checks whether the Hub-managed Coplay dependency is currently injected.
    /// </summary>
    public static async Task<bool> IsInjectedAsync(string projectPath, CancellationToken ct = default)
    {
        var manifestPath = GetManifestPath(projectPath);
        if (!File.Exists(manifestPath)) return false;

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, System.Text.Encoding.UTF8, ct);
            var doc  = JsonNode.Parse(json);
            return doc?["dependencies"]?[CoplayPackageName] != null;
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
        => File.Exists(Path.Combine(GetBackupDir(projectPath), ManifestFileName));

    private static string ToFileDependency(string packagePath)
        => "file:" + Path.GetFullPath(packagePath).Replace("\\", "/");

    private static async Task CopyAsync(string sourcePath, string destinationPath, CancellationToken ct)
    {
        await using var source      = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, ct);
    }

    private static async Task WriteAtomicallyAsync(string path, string content, CancellationToken ct)
    {
        var temporaryPath = path + ".tristin.tmp";
        await File.WriteAllTextAsync(temporaryPath, content, new System.Text.UTF8Encoding(false), ct);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
