using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Detects running Unity Editor processes by scanning process list
/// and parsing command-line arguments (-projectPath) via WMI.
/// </summary>
public sealed class UnityProcessDetector
{
    public Task<IReadOnlyList<EditorInstance>> DetectAsync(CancellationToken cancellationToken = default)
    {
        List<EditorInstance> result     = new();
        var                  processes = Process.GetProcessesByName("Unity");
        var                  seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in processes)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var instance = ParseProcess(process);
                // Only include main editor processes with a real -projectPath,
                // deduplicate by project path.
                if (instance != null && seenPaths.Add(instance.ProjectPath))
                    result.Add(instance);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Process may have exited — skip
            }
            finally
            {
                process.Dispose();
            }
        }

        return Task.FromResult<IReadOnlyList<EditorInstance>>(result);
    }

    public async Task StartWatchAsync(
        int                                                 intervalMs,
        Func<IReadOnlyList<EditorInstance>, Task>           onChanged,
        CancellationToken                                   cancellationToken = default)
    {
        HashSet<int> previousSet = new();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var instances  = await DetectAsync(cancellationToken);
                var currentSet = new HashSet<int>(instances.Select(i => i.ProcessId));

                if (!currentSet.SetEquals(previousSet))
                {
                    previousSet = currentSet;
                    await onChanged(instances);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Ignore transient detection errors
            }

            try
            {
                await Task.Delay(intervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Parse a Unity process to extract project path, version, etc.
    /// Returns null for child processes (AssetImportWorker, ShaderCompiler, etc.).
    /// </summary>
    private static EditorInstance? ParseProcess(Process process)
    {
        // 1. Get executable path and version from the main module
        string? exePath = null;
        string? version = "Unknown";

        try
        {
            exePath = process.MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                var fileVersion = FileVersionInfo.GetVersionInfo(exePath);
                version = fileVersion.ProductVersion ?? fileVersion.FileVersion ?? "Unknown";
            }
        }
        catch
        {
            // No permission to access main module — skip
        }

        // 2. Read command line via WMI (System.Management — not wmic.exe which is deprecated)
        var commandLine = GetCommandLine(process.Id);
        if (string.IsNullOrEmpty(commandLine))
            return null;

        // 3. Filter out child processes:
        //    - AssetImportWorker: has -batchMode and -name "AssetImportWorker*"
        //    - ShaderCompiler: similar pattern
        if (commandLine.Contains("-batchMode", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("AssetImportWorker", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("ShaderCompiler", StringComparison.OrdinalIgnoreCase))
            return null;

        // 4. Extract -projectPath from command line
        var projectPath = ExtractProjectPath(commandLine);
        if (string.IsNullOrEmpty(projectPath))
            return null;

        // Normalize path separators
        projectPath = projectPath.Replace('/', '\\');

        var projectName = new DirectoryInfo(projectPath).Name;

        return new EditorInstance
        {
            ProcessId      = process.Id,
            ProjectName    = projectName,
            ProjectPath    = projectPath,
            Version        = version,
            State          = EditorState.Available
        };
    }

    /// <summary>
    /// Query WMI for a process command line using System.Management (not wmic.exe).
    /// </summary>
    private static string? GetCommandLine(int pid)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using ManagementObjectSearcher searcher = new(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");

            using var collection = searcher.Get();
            foreach (var obj in collection)
            {
                var cmd = obj["CommandLine"]?.ToString();
                if (!string.IsNullOrEmpty(cmd))
                    return cmd;
            }
        }
        catch
        {
            // WMI query failed
        }

        return null;
    }

    /// <summary>
    /// Extract -projectPath value from a Unity command line.
    /// Handles: -projectPath "path", -projectpath path, -projectPath path
    /// </summary>
    private static string? ExtractProjectPath(string commandLine)
    {
        // Match -projectPath followed by an optional quote, then the path
        var match = Regex.Match(
            commandLine,
            @"-projectPath\s+[""']?(?<path>[^""'\s]+)[""']?",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return null;

        var path = match.Groups["path"].Value.Trim().Trim('"', '\'');

        // Validate path exists
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return null;

        return path;
    }
}
