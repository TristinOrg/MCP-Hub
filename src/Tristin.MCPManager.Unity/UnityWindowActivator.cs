// Force Unity Editor main window to gain focus.
// Unity only re-scans Packages/manifest.json when its main window gains focus
// (or on a ~30s idle timer). Activating the window triggers the package
// refresh immediately, which is the fastest way to get Bridge loaded.
//
// Author: Tristin Wen
// Email:  Tristin_Wen@outlook.com

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Win32 interop helpers to activate (bring to foreground) a Unity Editor's
/// main window by PID. This is the single most reliable trigger for Unity to
/// detect Packages/manifest.json changes and start recompiling the Bridge.
/// </summary>
public static class UnityWindowActivator
{
    // Win32 APIs
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW    = 5;

    /// <summary>
    /// Find a Unity Editor's main window handle by its PID.
    /// Enumerates all top-level windows, matches PID and checks title for Unity-ish patterns.
    /// </summary>
    public static IntPtr FindUnityMainWindow(int pid)
    {
        IntPtr result   = IntPtr.Zero;
        int    targetPid = pid;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            GetWindowThreadProcessId(hWnd, out var windowPid);
            if (windowPid != targetPid)
                return true;

            var len = GetWindowTextLength(hWnd);
            if (len <= 0)
                return true;

            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            // Unity main window typically contains project name + "Unity" suffix.
            // It does NOT contain: *Compiling*, *Progress*, Inspector, Hierarchy, Game, Scene
            // (those are child utility windows that happen to be top-level on Windows).
            if (title.Contains("Unity", StringComparison.OrdinalIgnoreCase)
                || (title.Length > 0 && !IsUtilityWindowTitle(title)))
            {
                // Prefer the longest window title (usually the main one carrying the project name)
                if (result == IntPtr.Zero || title.Length > GetWindowTitleLength(result))
                    result = hWnd;
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static int GetWindowTitleLength(IntPtr hWnd)
    {
        var len = GetWindowTextLength(hWnd);
        if (len > 0) return len;
        var sb = new StringBuilder(512);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.Length;
    }

    private static bool IsUtilityWindowTitle(string title)
    {
        string[] utilityKeywords = { "Inspector", "Hierarchy", "Game", "Scene", "Project",
                                     "Console", "Animator", "Animation", "Profiler",
                                     "Package Manager", "Build Settings", "Preferences",
                                     "Shader", "Debug", "Testing" };
        foreach (var kw in utilityKeywords)
        {
            if (title.Equals(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Activate the Unity Editor main window and bring it to foreground.
    /// Handles the "foreground permission" problem by attaching to the
    /// current foreground thread first (AttachThreadInput trick).
    /// </summary>
    public static bool ActivateUnityWindow(int pid)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var hWnd = FindUnityMainWindow(pid);
        if (hWnd == IntPtr.Zero)
            return false;

        try
        {
            // Restore if minimized
            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);
            else
                ShowWindow(hWnd, SW_SHOW);

            // Attach to the current foreground window thread so SetForegroundWindow
            // succeeds even when the user is currently focused on another app.
            var foregroundWnd    = GetForegroundWindow();
            var currentThreadId  = GetWindowThreadProcessId(foregroundWnd, out _);
            var targetThreadId   = GetWindowThreadProcessId(hWnd, out _);

            if (currentThreadId != targetThreadId && currentThreadId != 0)
            {
                AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            // Allow the target process to set foreground (works if we're the foreground caller)
            try { AllowSetForegroundWindow(pid); }
            catch { /* ignore */ }

            var ok = SetForegroundWindow(hWnd);

            if (currentThreadId != targetThreadId && currentThreadId != 0)
            {
                try { AttachThreadInput(currentThreadId, targetThreadId, false); }
                catch { /* ignore */ }
            }

            return ok;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Try to activate Unity's window, then give it 1 second of focus before
    /// returning. This gives Unity enough time to process its package FSW.
    /// </summary>
    public static async Task<bool> ActivateAndYieldAsync(int pid, int yieldMs = 1200, CancellationToken ct = default)
    {
        bool ok = ActivateUnityWindow(pid);
        try { await Task.Delay(yieldMs, ct); }
        catch (OperationCanceledException) { /* ignore */ }
        return ok;
    }
}
