// NamedPipe IPC client: Unity Bridge -> Runtime Manager.
// Minimal implementation using only Unity-compatible APIs (no Channels, no Pipelines).
// Protocol (JSON-lines over NamedPipe):
//   1. Send register JSON line on connect
//   2. Each subsequent line is a request or response
//   3. Heartbeat: PING -> PONG

#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Tristin.MCPBridge
{
    /// <summary>
    /// NamedPipe client connecting the Unity Bridge to the Runtime Manager.
    /// Uses simple StreamReader/StreamWriter for maximum Unity compatibility.
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
            var cts = new CancellationTokenSource();
            Task.Run(() => RunLoopAsync(endpoint, projectName, projectPath, pid, onCommand, cts.Token), cts.Token);
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
            // Thread-safe queue for outgoing messages (replaces Channel)
            var sendQueue = new ConcurrentQueue<string>();
            var sendSignal = new AutoResetEvent(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(
                        serverName: ".",
                        pipeName:   ServerPipeName,
                        direction:  PipeDirection.InOut,
                        options:    PipeOptions.Asynchronous);

                    await pipe.ConnectAsync(3000, ct);

                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    using var writer = new StreamWriter(pipe, Encoding.UTF8) { NewLine = "\n", AutoFlush = true };

                    // 1) Send registration
                    var registerJson = $"{{\"type\":\"register\",\"editorType\":\"Unity\",\"projectName\":\"{Escape(projectName)}\",\"projectPath\":\"{Escape(projectPath)}\",\"pid\":{pid},\"endpoint\":\"{Escape(endpoint)}\"}}";
                    writer.WriteLine(registerJson);
                    writer.Flush();
                    Debug.Log($"[MCPBridge] Sent registration: {projectName} PID={pid}");

                    // 2) Start heartbeat + send loop on separate tasks
                    var hbTask = Task.Run(async () =>
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { break; }
                            sendQueue.Enqueue("{\"type\":\"ping\"}");
                            sendSignal.Set();
                        }
                    }, ct);

                    var sendTask = Task.Run(async () =>
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            try
                            {
                                sendSignal.WaitOne(500);
                                while (sendQueue.TryDequeue(out var msg))
                                {
                                    writer.WriteLine(msg);
                                    writer.Flush();
                                }
                            }
                            catch (OperationCanceledException) { break; }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[MCPBridge] Send error: {ex.Message}");
                                break;
                            }
                        }
                    }, ct);

                    // 3) Receive loop
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            // ping/pong
                            if (line.Contains("\"type\":\"ping\""))
                            {
                                sendQueue.Enqueue("{\"type\":\"pong\"}");
                                sendSignal.Set();
                                continue;
                            }

                            if (line.Contains("\"type\":\"pong\""))
                                continue;

                            // tool call: {"id":N,"type":"tool","name":"...","args":{...}}
                            var idMatch = System.Text.RegularExpressions.Regex.Match(line, "\"id\"\\s*:\\s*(?<id>\\d+)");
                            var nameMatch = System.Text.RegularExpressions.Regex.Match(line, "\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"");

                            if (idMatch.Success && nameMatch.Success)
                            {
                                var id = idMatch.Groups["id"].Value;
                                var name = nameMatch.Groups["name"].Value;

                                // Extract args: everything after "args": until the closing }
                                // args can be a raw JSON object {...} or a string "..."
                                var argsIdx = line.IndexOf("\"args\"");
                                string args = "{}";
                                if (argsIdx >= 0)
                                {
                                    var afterArgs = line.Substring(argsIdx + 6).TrimStart();
                                    if (afterArgs.StartsWith("{"))
                                    {
                                        // Raw JSON object — find matching closing brace
                                        int depth = 0;
                                        int end = -1;
                                        for (int i = 0; i < afterArgs.Length; i++)
                                        {
                                            if (afterArgs[i] == '{') depth++;
                                            else if (afterArgs[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
                                        }
                                        if (end > 0) args = afterArgs.Substring(0, end + 1);
                                    }
                                    else if (afterArgs.StartsWith("\""))
                                    {
                                        // String args — extract until closing quote
                                        var strEnd = afterArgs.IndexOf('"', 1);
                                        if (strEnd > 0) args = afterArgs.Substring(1, strEnd - 1);
                                    }
                                }

                                string result;
                                try
                                {
                                    var cmdResult = onCommand(name, args);
                                    result = $"{{\"id\":{id},\"ok\":true,\"result\":\"{Escape(cmdResult)}\"}}";
                                }
                                catch (Exception ex)
                                {
                                    result = $"{{\"id\":{id},\"ok\":false,\"error\":\"{Escape(ex.Message)}\"}}";
                                }

                                sendQueue.Enqueue(result);
                                sendSignal.Set();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[MCPBridge] Dispatch error: {ex}");
                        }
                    }

                    // Connection closed
                    Debug.LogWarning("[MCPBridge] NamedPipe connection closed, retrying ...");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MCPBridge] IPC error: {ex.Message}");
                }

                // Reconnect delay
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { throw; }
            }
        }

        private static string Escape(string s)
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
                    default:   sb.Append(c);     break;
                }
            }
            return sb.ToString();
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
