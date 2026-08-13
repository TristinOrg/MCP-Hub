// Bridge bootstrap: [InitializeOnLoad] ensures the Bridge auto-starts after any domain reload.

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
    /// </summary>
    [InitializeOnLoad]
    public static class BridgeBootstrap
    {
        private static IDisposable? _bridgeHost;
        private static int          _started;

        static BridgeBootstrap()
        {
            // Delay one frame to avoid startup race conditions
            EditorApplication.delayCall += TryStartBridge;
        }

        private static void TryStartBridge()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                return;

            try
            {
                // 1. Build Bridge info
                var projectPath = Directory.GetParent(Application.dataPath)!.FullName;
                var projectName = Path.GetFileName(projectPath);
                var pid         = System.Diagnostics.Process.GetCurrentProcess().Id;

                // 2. Generate unique IPC endpoint (NamedPipe name)
                var endpoint = $"TristinMCP_{pid}";

                // 3. Start IPC client (connects to Runtime Manager) and send registration
                _bridgeHost = NamedPipeBridgeClient.Start(
                    endpoint:     endpoint,
                    projectName:  projectName,
                    projectPath:  projectPath,
                    pid:          pid,
                    onCommand:    CommandDispatcher.Dispatch);

                Debug.Log($"[MCPBridge] Registered: {projectName} PID={pid} Pipe={endpoint}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCPBridge] Failed to start: {ex}");
                Interlocked.Exchange(ref _started, 0);
            }
        }
    }
}
#endif
