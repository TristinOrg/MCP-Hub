// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    BridgeBootstrap.cs
// ============================================================
// Bridge 启动入口：[InitializeOnLoad] 保证 Unity 域重载后自动启动
// ============================================================

#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Tristin.MCPBridge
{
    /// <summary>
    /// Unity Editor 域加载后自动启动 Bridge
    /// </summary>
    [InitializeOnLoad]
    public static class BridgeBootstrap
    {
        private static IDisposable? _bridgeHost;
        private static int          _started;

        static BridgeBootstrap()
        {
            // 延迟一帧启动，避免 Unity 启动阶段的竞态
            EditorApplication.delayCall += TryStartBridge;
        }

        private static void TryStartBridge()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                return;

            try
            {
                // 1. 构造 Bridge 信息
                var projectPath = Directory.GetParent(Application.dataPath)!.FullName;
                var projectName = Path.GetFileName(projectPath);
                var pid         = System.Diagnostics.Process.GetCurrentProcess().Id;

                // 2. 生成唯一 IPC 端点（NamedPipe 名称）
                var endpoint = $"TristinMCP_{pid}";

                // 3. 启动 IPC 客户端（连接 Runtime Manager）并发送注册消息
                //    当前 MVP：使用 NamedPipe + JSON 行协议
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
