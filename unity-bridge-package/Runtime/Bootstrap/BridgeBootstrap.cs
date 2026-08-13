// Bridge bootstrap: auto-starts after Unity domain reload.
// Uses multiple startup mechanisms to maximize reliability.

#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Tristin.MCPBridge
{
    /// <summary>
    /// Auto-starts the Bridge after Unity domain reload.
    /// Uses 3 startup triggers to maximize reliability:
    ///   1. [InitializeOnLoad] static constructor
    ///   2. EditorApplication.delayCall
    ///   3. EditorApplication.update (backup poll loop — catches late initialization)
    /// </summary>
    [InitializeOnLoad]
    public static class BridgeBootstrap
    {
        private static IDisposable? _bridgeHost;
        private static int          _started;
        private static int          _startupAttempts;

        static BridgeBootstrap()
        {
            LogFile("BridgeBootstrap static ctor called");

            // Trigger 1: delayCall (after InitializeOnLoad finishes)
            EditorApplication.delayCall += () => TryStartBridge("delayCall");

            // Trigger 2: EditorApplication.update fallback — keeps retrying for
            // up to 30 seconds. This catches edge cases where static ctor /
            // delayCall run before Unity is fully initialized.
            EditorApplication.CallbackFunction? updateCb = null;
            updateCb = () =>
            {
                if (Volatile.Read(ref _started) == 1)
                {
                    EditorApplication.update -= updateCb;
                    return;
                }

                if (Interlocked.Increment(ref _startupAttempts) > 600) // 600 * 16ms ~= 10s
                {
                    EditorApplication.update -= updateCb;
                    LogFile("BridgeBootstrap: gave up after 10s of update loop attempts");
                    return;
                }

                if (_startupAttempts % 60 == 0) // Every ~1s
                    TryStartBridge($"update-loop attempt={_startupAttempts}");
            };
            EditorApplication.update += updateCb;
        }

        private static void TryStartBridge(string trigger)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                return;

            try
            {
                LogFile($"TryStartBridge({trigger}) — attempting");

                var dataPath = Application.dataPath;
                if (string.IsNullOrEmpty(dataPath))
                {
                    LogFile($"TryStartBridge: Application.dataPath is empty, deferring");
                    Interlocked.Exchange(ref _started, 0);
                    return;
                }

                var projectPath = Directory.GetParent(dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectPath))
                {
                    LogFile($"TryStartBridge: projectPath is null, deferring");
                    Interlocked.Exchange(ref _started, 0);
                    return;
                }

                var projectName = Path.GetFileName(projectPath);
                int pid;
                try { pid = System.Diagnostics.Process.GetCurrentProcess().Id; }
                catch { pid = -1; }

                var endpoint = $"TristinMCP_{pid}";

                LogFile($"[MCPBridge] project={projectName} path={projectPath} pid={pid}");

                _bridgeHost = NamedPipeBridgeClient.Start(
                    endpoint:     endpoint,
                    projectName:  projectName,
                    projectPath:  projectPath,
                    pid:          pid,
                    onCommand:    CommandDispatcher.Dispatch);

                Debug.Log($"[MCPBridge] Bootstrap OK via {trigger}: {projectName} PID={pid}");
                LogFile($"[MCPBridge] Bootstrap OK via {trigger}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCPBridge] Bootstrap failed (trigger={trigger}): {ex}");
                LogFile($"[MCPBridge] Bootstrap FAILED: {ex}");
                // Allow retry
                Interlocked.Exchange(ref _started, 0);
            }
        }

        /// <summary>
        /// File-based logging fallback. Unity Console does not always capture
        /// logs from [InitializeOnLoad] static constructors. Writing to a file
        /// under project Library/ folder guarantees we can see what happened.
        /// </summary>
        private static void LogFile(string message)
        {
            try
            {
                var logDir = Path.Combine(Application.dataPath, "..", "Library");
                if (!Directory.Exists(logDir)) return;

                var logPath = Path.Combine(logDir, "TristinMCPBridge.log");
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                File.AppendAllText(logPath, $"[{timestamp}] {message}\n");
            }
            catch
            {
                // Silent — we're a logging fallback itself
            }
        }
    }
}
#endif
