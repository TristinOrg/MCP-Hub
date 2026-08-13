// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    UnityProcessDetector.cs
// ============================================================

using System.Diagnostics;
using System.Text.RegularExpressions;
using Tristin.MCPManager.Core.Interfaces;
using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Unity Editor 进程检测器
/// 通过扫描名为 "Unity" 的进程并解析其命令行参数（-projectPath）来获取项目信息
/// </summary>
public class UnityProcessDetector : IEditorDetector
{
    public string EditorType => "Unity";

    private readonly TimeSpan _watchExitCheckInterval = TimeSpan.FromMilliseconds(500);

    public async Task<IReadOnlyList<EditorInstance>> DetectAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<EditorInstance>();
        var processes = Process.GetProcessesByName("Unity");

        foreach (var process in processes)
        {
            try
            {
                var instance = await ParseProcessAsync(process, cancellationToken);
                if (instance != null)
                    result.Add(instance);
            }
            catch
            {
                // 进程可能已经退出，忽略
            }
        }

        return result;
    }

    public async Task StartWatchAsync(
        int                                                 intervalMs,
        Func<IReadOnlyList<EditorInstance>, Task>           onChanged,
        CancellationToken                                   cancellationToken = default)
    {
        var previousSet = new HashSet<int>();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var instances = await DetectAsync(cancellationToken);
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
                // 忽略检测中的临时错误
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
    /// 解析 Unity 进程，提取项目路径、版本等信息
    /// </summary>
    private static async Task<EditorInstance?> ParseProcessAsync(Process process, CancellationToken ct)
    {
        // 1. 从进程主模块获取可执行文件路径和版本
        string? exePath      = null;
        string? version      = "Unknown";
        string? projectPath  = null;
        string? projectName  = null;

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
            // 无权限访问主模块时跳过
        }

        // 2. 尝试通过 WMI 读取命令行（Windows）获取 -projectPath
        projectPath = await GetProjectPathFromCommandLineAsync(process.Id, ct);

        // 3. 如果 WMI 失败，尝试通过 Editor.log 路径推断
        if (string.IsNullOrEmpty(projectPath))
        {
            projectPath = TryGetProjectPathFromLog(process.Id);
        }

        // 4. 还是没有，则标记为未知
        if (string.IsNullOrEmpty(projectPath))
        {
            projectName = $"Unity_{process.Id}";
            projectPath = "[Unknown]";
        }
        else
        {
            projectName = new DirectoryInfo(projectPath).Name;
        }

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
    /// 通过 WMI（Windows）读取进程命令行参数提取 -projectPath
    /// </summary>
    private static async Task<string?> GetProjectPathFromCommandLineAsync(int pid, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var startInfo = new ProcessStartInfo
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

            // 匹配 -projectPath "xxx" 或 -projectPath xxx
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
            // WMI 失败就放弃
        }

        return null;
    }

    /// <summary>
    /// 备选方案：通过 Unity Editor.log 定位项目路径
    /// Windows: %LOCALAPPDATA%\Unity\Editor\Editor.log
    /// </summary>
    private static string? TryGetProjectPathFromLog(int pid)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Unity", "Editor", "Editor.log");

            if (!File.Exists(logPath)) return null;

            // 尝试读取文件（Unity 可能正在占用，用只读共享模式）
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);

            // 从末尾向前找 "Initialize engine version" 后面的 "Project file" 行
            // 简单策略：倒序读最近的 100 行
            var lines = new List<string>();
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                lines.Add(line);
                if (lines.Count > 500) lines.RemoveAt(0);
            }

            // 匹配 PID 对应的 Project path
            // 格式示例：
            // "Built-in GUIDs are exported to 'D:/xxx/ProjectSettings/...'"
            // 或者直接找 "[PID]" 标记后面的 Project
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var l = lines[i];
                // 尝试匹配 ProjectSettings/ 路径反推
                var m = Regex.Match(l, @"['""](?<path>.+?)[\\/]ProjectSettings[\\/]");
                if (m.Success)
                {
                    var p = m.Groups["path"].Value;
                    if (Directory.Exists(p)) return p;
                }
            }
        }
        catch
        {
            // 忽略
        }
        return null;
    }
}
