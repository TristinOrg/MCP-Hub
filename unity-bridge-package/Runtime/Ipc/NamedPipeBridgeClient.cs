// NamedPipe IPC client: Unity Bridge -> Runtime Manager.
// Protocol (JSON-lines over NamedPipe):
//   1. Send one register JSON line on connect
//   2. Then each line is a request or response:
//        REQ: {"id":1,"type":"tool","name":"unity.create_gameobject","args":"{...}"}
//        RSP: {"id":1,"ok":true,"result":"..."} or {"id":1,"ok":false,"error":"..."}
//   3. Heartbeat: PING -> PONG

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
    /// <summary>
    /// NamedPipe client connecting the Unity Bridge to the Runtime Manager.
    /// </summary>
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
            CancellationTokenSource cts = new();
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
            Channel<string> sendChannel = Channel.CreateUnbounded<string>();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Connect to Runtime Manager's NamedPipe server
                    using var pipe = new NamedPipeClientStream(
                        serverName: ".",
                        pipeName:   ServerPipeName,
                        direction:  PipeDirection.InOut,
                        options:    PipeOptions.Asynchronous);

                    await pipe.ConnectAsync(2000, ct);
                    pipe.ReadMode = PipeStreamMode.Byte;

                    using StreamReader  reader = new(pipe, new UTF8Encoding(false));
                    using StreamWriter  writer = new(pipe, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };

                    // 1) Send registration
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

                    // 2) Start send loop
                    var sendTask = Task.Run(async () =>
                    {
                        await foreach (var line in sendChannel.Reader.ReadAllAsync(ct))
                        {
                            await writer.WriteLineAsync(line);
                            await writer.FlushAsync();
                        }
                    }, ct);

                    // 3) Start heartbeat
                    var hbTask = Task.Run(async () =>
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { break; }
                            try { await sendChannel.Writer.WriteAsync("{\"type\":\"ping\"}", ct); }
                            catch (ChannelClosedException) { break; }
                            catch (OperationCanceledException) { break; }
                        }
                    }, ct);

                    // 4) Receive loop
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            // MVP: simple string parsing to avoid Newtonsoft dependency
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
                                // Unescape args
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
                    Debug.LogWarning($"[MCPBridge] IPC disconnected, retry in 1s: {ex.Message}");
                }

                // Reconnect delay
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { throw; }
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            StringBuilder sb = new(s.Length);
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
