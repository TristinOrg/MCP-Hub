using System.Diagnostics;
using System.Text.RegularExpressions;
using Tristin.MCPManager.Core.Interfaces;
using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Detects running Unity Editor processes by scanning process list
/// and parsing command-line arguments (-projectPath).
/// </summary>
public class UnityProcessDetector : IEditorDetector
{
    public string EditorType => "Unity";

    private readonly TimeSpan _watchExitCheckInterval = TimeSpan.FromMilliseconds(500);

    public async Task<IReadOnlyList<EditorInstance>> DetectAsync(CancellationToken cancellationToken = default)
    {
        List<EditorInstance> result     = new();
        var                  processes = Process.GetProcessesByName("Unity");
        var                  seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in processes)
        {
            try
            {
                var instance = await ParseProcessAsync(process, cancellationToken);
                // Only include processes with a real -projectPath (filters out
                // child processes like shader compilers / asset import workers)
                // and deduplicate by project path.
                if (instance != null
                    && instance.ProjectPath != "[Unknown]"
                    && seenPaths.Add(instance.ProjectPath))
                {
                    result.Add(instance);
                }
            }
            catch
            {
                // Process may have exited — skip
            }
        }

        return result;
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
    /// Returns null for child processes (shader compiler, asset import worker, etc.)
    /// that don't carry -projectPath in their command line.
    /// </summary>
    private static async Task<EditorInstance?> ParseProcessAsync(Process process, CancellationToken ct)
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

        // 2. Read -projectPath from command line via WMI (Windows).
        //    Only the main Editor process has -projectPath;
        //    child processes (shader compiler, etc.) do not.
        var projectPath = await GetProjectPathFromCommandLineAsync(process.Id, ct);

        // 3. No -projectPath → this is a child process, not a real editor instance
        if (string.IsNullOrEmpty(projectPath))
            return null;

        var projectName = new DirectoryInfo(projectPath).Name;

        return new EditorInstance
        {
            EditorType     = "Unity",
            ProcessId      = process.Id,
            ProjectName    = projectName,
            ProjectPath    = projectPath,
            Version        = version,
            ExecutablePath = exePath,
            State          = EditorState.Available
        };
    }

    /// <summary>
    /// Read process command line via WMI (Windows) to extract -projectPath.
    /// </summary>
    private static async Task<string?> GetProjectPathFromCommandLineAsync(int pid, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName               = "wmic.exe",
                Arguments              = $"process where ProcessId={pid} get CommandLine /value",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true
            };

            using var proc = Process.Start(startInfo);
            if (proc == null) return null;

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            // Match -projectPath "xxx" or -projectPath xxx
            var match = Regex.Match(
                output,
                @"-projectPath\s+[""']?(?<path>[^""'\r\n]+)[""']?",
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var path = match.Groups["path"].Value.Trim();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    return path;
            }
        }
        catch
        {
            // WMI failed — give up
        }

        return null;
    }
}
