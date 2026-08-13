// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    NamedPipeBridgeClient.cs
// ============================================================
// Unity Bridge -> Runtime Manager 的 NamedPipe IPC 客户端
// 协议：
//   - 先发送 1 行注册 JSON（BridgeRegistration）
//   - 之后每行一个请求/响应：
//       REQ:  {"id":1,"type":"tool","name":"unity.create_gameobject","args":"{...}"}
//       RSP:  {"id":1,"ok":true,"result":"..."} 或 {"id":1,"ok":false,"error":"..."}
//   - 心跳：PING -> PONG
// ============================================================

#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using UnityEngine;

namespace Tristin.MCPBridge
{
    public static class NamedPipeBridgeClient
    {
        public const string ServerPipeName = "TristinMCP_RuntimeManager";

        public static IDisposable Start(
            string                          endpoint,
            string                          projectName,
            string                          projectPath,
            int                             pid,
            Func<string, string, string>    onCommand)
        {
            var cts = new CancellationTokenSource();
            _ = Task.Run(() => RunLoopAsync(endpoint, projectName, projectPath, pid, onCommand, cts.Token), cts.Token);
            return new DisposableAction(() =>
            {
                try { cts.Cancel(); } catch { /* ignore */ }
            });
        }

        private static async Task RunLoopAsync(
            string                          endpoint,
            string                          projectName,
            string                          projectPath,
            int                             pid,
            Func<string, string, string>    onCommand,
            CancellationToken               ct)
        {
            var sendChannel = Channel.CreateUnbounded<string>();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 连接 Runtime Manager 的 NamedPipe Server
                    using var pipe = new NamedPipeClientStream(
                        serverName:         ".",
                        pipeName:           ServerPipeName,
                        direction:          PipeDirection.InOut,
                        options:            PipeOptions.Asynchronous);

                    await pipe.ConnectAsync(3000, ct);
                    pipe.ReadMode = PipeStreamMode.Byte;

                    using var reader = new StreamReader(pipe, new UTF8Encoding(false));
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };

                    // 1) 发送注册消息
                    var registerJson = JsonUtility.ToJson(new BridgeRegistrationMsg
                    {
                        type        = "register",
                        editorType  = "Unity",
                        projectName = projectName,
                        projectPath = projectPath,
                        pid         = pid,
                        endpoint    = endpoint
                    });
                    await writer.WriteLineAsync(registerJson);
                    await writer.FlushAsync();

                    // 2) 启动发送循环
                    var sendTask = Task.Run(async () =>
                    {
                        await foreach (var line in sendChannel.Reader.ReadAllAsync(ct))
                        {
                            await writer.WriteLineAsync(line);
                            await writer.FlushAsync();
                        }
                    }, ct);

                    // 3) 启动心跳
                    var hbTask = Task.Run(async () =>
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            await Task.Delay(5000, ct);
                            await sendChannel.Writer.WriteAsync("{\"type\":\"ping\"}", ct);
                        }
                    }, ct);

                    // 4) 接收循环
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            // MVP：用简易 JSON 字符串解析避免引入 Newtonsoft
                            // 实际可使用 System.Text.Json
                            if (line.Contains("\"type\":\"ping\"") || line.Contains("\"type\":\"pong\""))
                            {
                                if (line.Contains("\"type\":\"ping\""))
                                    await sendChannel.Writer.WriteAsync("{\"type\":\"pong\"}", ct);
                                continue;
                            }

                            var toolMatch = System.Text.RegularExpressions.Regex.Match(
                                line,
                                "\"id\"\\s*:\\s*(?<id>\\d+).*?\"name\"\\s*:\\s*\"(?<name>[^\"]+)\".*?\"args\"\\s*:\\s*\"(?<args>.*?)\"\\s*\\}");

                            if (toolMatch.Success)
                            {
                                var id   = toolMatch.Groups["id"].Value;
                                var name = toolMatch.Groups["name"].Value;
                                var args = toolMatch.Groups["args"].Value;
                                // 还原 args 转义
                                args = args.Replace("\\\"", "\"").Replace("\\\\", "\\");

                                string result;
                                try
                                {
                                    var cmdResult = onCommand(name, args);
                                    result = $"{{\"id\":{id},\"ok\":true,\"result\":{EscapeJson(cmdResult)}}}";
                                }
                                catch (Exception ex)
                                {
                                    result = $"{{\"id\":{id},\"ok\":false,\"error\":\"{EscapeJson(ex.Message)}\"}}";
                                }

                                await sendChannel.Writer.WriteAsync(result, ct);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[MCPBridge] Dispatch error: {ex}");
                        }
                    }

                    try { sendChannel.Writer.Complete(); } catch { /* ignore */ }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MCPBridge] IPC disconnected, retry in 3s: {ex.Message}");
                }

                // 重连间隔
                try { await Task.Delay(3000, ct); } catch (OperationCanceledException) { throw; }
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < ' ') sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        [Serializable]
        private class BridgeRegistrationMsg
        {
            // 字段顺序小写匹配 JSON 原生
            public string type        = null!;
            public string editorType  = null!;
            public string projectName = null!;
            public string projectPath = null!;
            public int    pid;
            public string endpoint    = null!;
        }

        private class DisposableAction : IDisposable
        {
            private readonly Action _action;
            private int _disposed;
            public DisposableAction(Action action) { _action = action; }
            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0) _action();
            }
        }
    }
}
#endif
