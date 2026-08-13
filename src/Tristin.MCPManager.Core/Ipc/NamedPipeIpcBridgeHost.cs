// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    NamedPipeIpcBridgeHost.cs
// ============================================================
// Runtime Manager 端 NamedPipe IPC 主机
// - 监听来自 Unity Bridge 的连接
// - 接收 register 消息，维护 Bridge 注册表
// - 提供 InvokeToolAsync / ListToolsAsync 路由调用
// ============================================================

using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tristin.MCPManager.Core.Interfaces;
using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Core.Ipc;

public class NamedPipeIpcBridgeHost : IIpcBridgeHost, IAsyncDisposable
{
    public const string DefaultPipeName = "TristinMCP_RuntimeManager";

    private readonly ConcurrentDictionary<int, BridgeConnection> _bridges    = new();
    private readonly ConcurrentDictionary<int, PendingCall>       _pending    = new();
    private int                                                   _nextCallId = 1;
    private CancellationTokenSource?                              _cts;
    private Task?                                                 _acceptLoop;

    public IReadOnlyDictionary<int, BridgeRegistration> RegisteredBridges
    {
        get
        {
            var dict = new Dictionary<int, BridgeRegistration>();
            foreach (var (pid, conn) in _bridges)
                if (conn.Registration != null)
                    dict[pid] = conn.Registration;
            return dict;
        }
    }

    public event EventHandler<BridgeRegistration>? BridgeRegistered;
    public event EventHandler<int>?                BridgeDisconnected;

    public Task StartAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var pipeName = string.IsNullOrEmpty(endpoint) ? DefaultPipeName : endpoint;
        _cts         = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptLoop  = RunAcceptLoopAsync(pipeName, _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try
        {
            if (_acceptLoop != null)
                await _acceptLoop;
        }
        catch (OperationCanceledException) { /* ignore */ }

        foreach (var (_, conn) in _bridges)
            conn.Dispose();
        _bridges.Clear();
    }

    public async Task<string> InvokeToolAsync(int targetPid, string toolName, string arguments, CancellationToken cancellationToken = default)
    {
        if (!_bridges.TryGetValue(targetPid, out var conn) || conn.Writer == null)
            throw new InvalidOperationException($"Bridge PID={targetPid} not connected.");

        var callId = Interlocked.Increment(ref _nextCallId);
        // args 需要序列化为 JSON 字符串嵌入（反斜杠转义）
        var argsEscaped = JsonEncodedText.Encode(arguments ?? "{}").ToString().Trim('"');
        var line        = $"{{\"id\":{callId},\"type\":\"tool\",\"name\":\"{toolName}\",\"args\":\"{argsEscaped}\"}}";

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled());

        _pending[callId] = new PendingCall(tcs, targetPid);

        try
        {
            await conn.Writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            await conn.Writer.FlushAsync(cancellationToken);
        }
        catch
        {
            _pending.TryRemove(callId, out _);
            throw;
        }

        // 30s 超时兜底
        using var timeout = new CancellationTokenSource(30000);
        using var linked  = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await tcs.Task.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _pending.TryRemove(callId, out _);
            throw new TimeoutException($"Tool '{toolName}' on PID={targetPid} timed out.");
        }
    }

    public Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(int targetPid, CancellationToken cancellationToken = default)
    {
        // MVP：Unity 端预定义的工具列表
        IReadOnlyList<McpToolDefinition> list = new List<McpToolDefinition>
        {
            new() { Name = "ping", Description = "Ping the bridge", InputSchema = new {}, SourceEditorPid = targetPid },
            new() { Name = "unity.editor_info", Description = "Get Unity Editor info (version, projectPath, isPlaying)", SourceEditorPid = targetPid },
            new() { Name = "unity.list_scenes", Description = "List scenes in build settings", SourceEditorPid = targetPid },
            new()
            {
                Name        = "unity.create_gameobject",
                Description = "Create a new GameObject in the current scene",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "GameObject name" }
                    }
                },
                SourceEditorPid = targetPid
            },
            new() { Name = "unity.save_project", Description = "Save all assets and project", SourceEditorPid = targetPid },
            new() { Name = "unity.refresh_assets", Description = "Force refresh AssetDatabase", SourceEditorPid = targetPid }
        };
        return Task.FromResult(list);
    }

    // ========== 内部实现 ==========

    private async Task RunAcceptLoopAsync(string pipeName, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    pipeName:             pipeName,
                    direction:            PipeDirection.InOut,
                    maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
                    transmissionMode:     PipeTransmissionMode.Byte,
                    options:              PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                // 每个连接一个独立任务
                _ = Task.Run(() => HandleConnectionAsync(server, ct), ct);
                server = null; // 所有权转移给处理任务
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                try { server?.Dispose(); } catch { /* ignore */ }
                // 避免 100% CPU 死循环
                try { await Task.Delay(500, ct); } catch (OperationCanceledException) { throw; }
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        BridgeConnection? bridgeConn = null;
        try
        {
            using (pipe)
            using (var reader = new StreamReader(pipe, new UTF8Encoding(false)))
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true })
            {
                bridgeConn = new BridgeConnection(writer);

                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    await ProcessLineAsync(line, bridgeConn, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        catch
        {
            // 连接异常断开
        }
        finally
        {
            if (bridgeConn?.Registration != null)
            {
                var pid = bridgeConn.Registration.Pid;
                _bridges.TryRemove(pid, out _);
                // 清理该 PID 下所有 pending call
                foreach (var (callId, pc) in _pending.ToArray())
                {
                    if (pc.TargetPid == pid)
                    {
                        _pending.TryRemove(callId, out _);
                        pc.Tcs.TrySetException(new IOException($"Bridge PID={pid} disconnected."));
                    }
                }
                try { BridgeDisconnected?.Invoke(this, pid); } catch { /* ignore */ }
            }
            bridgeConn?.Dispose();
        }
    }

    private Task ProcessLineAsync(string line, BridgeConnection bridgeConn, CancellationToken ct)
    {
        // 1) register
        if (Regex.IsMatch(line, "\"type\"\\s*:\\s*\"register\""))
        {
            try
            {
                // 提取关键字段
                var pidMatch       = Regex.Match(line, "\"pid\"\\s*:\\s*(?<pid>\\d+)");
                var editorTypeMatch = Regex.Match(line, "\"editorType\"\\s*:\\s*\"(?<v>[^\"]+)\"");
                var projectNameMatch = Regex.Match(line, "\"projectName\"\\s*:\\s*\"(?<v>[^\"]+)\"");
                var projectPathMatch = Regex.Match(line, "\"projectPath\"\\s*:\\s*\"(?<v>[^\"]+)\"");
                var endpointMatch    = Regex.Match(line, "\"endpoint\"\\s*:\\s*\"(?<v>[^\"]+)\"");

                if (pidMatch.Success)
                {
                    var reg = new BridgeRegistration
                    {
                        EditorType  = editorTypeMatch.Success  ? editorTypeMatch.Groups["v"].Value  : "Unity",
                        ProjectName = projectNameMatch.Success ? projectNameMatch.Groups["v"].Value : "Unknown",
                        ProjectPath = projectPathMatch.Success ? Unescape(projectPathMatch.Groups["v"].Value) : "",
                        Pid         = int.Parse(pidMatch.Groups["pid"].Value),
                        Endpoint    = endpointMatch.Success    ? endpointMatch.Groups["v"].Value    : ""
                    };
                    bridgeConn.Registration = reg;
                    _bridges[reg.Pid]       = bridgeConn;
                    try { BridgeRegistered?.Invoke(this, reg); } catch { /* ignore */ }
                }
            }
            catch { /* 解析失败忽略 */ }
            return Task.CompletedTask;
        }

        // 2) ping / pong
        if (Regex.IsMatch(line, "\"type\"\\s*:\\s*\"ping\""))
        {
            // 客户端 -> 服务器 ping：我们回 pong（通过连接的 writer）
            return RespondPingAsync(bridgeConn.Writer);
        }
        if (Regex.IsMatch(line, "\"type\"\\s*:\\s*\"pong\""))
            return Task.CompletedTask;

        // 3) tool call response: {"id":N,"ok":true,"result":"..."}  or {"id":N,"ok":false,"error":"..."}
        var idMatch = Regex.Match(line, "\"id\"\\s*:\\s*(?<id>\\d+)");
        if (idMatch.Success && Regex.IsMatch(line, "\"ok\""))
        {
            if (int.TryParse(idMatch.Groups["id"].Value, out var callId)
                && _pending.TryRemove(callId, out var pending))
            {
                var okMatch = Regex.Match(line, "\"ok\"\\s*:\\s*(?<ok>true|false)");
                var ok = okMatch.Success && okMatch.Groups["ok"].Value == "true";

                if (ok)
                {
                    // 提取 result，里面可能是 JSON 也可能是转义字符串
                    var resultMatch = Regex.Match(line, "\"result\"\\s*:\\s*(?<v>.*)\\s*\\}$", RegexOptions.Singleline);
                    var result = resultMatch.Success ? resultMatch.Groups["v"].Value.TrimEnd(',', ' ') : "null";
                    pending.Tcs.TrySetResult(result);
                }
                else
                {
                    var errMatch = Regex.Match(line, "\"error\"\\s*:\\s*\"(?<v>([^\"\\\\]|\\\\.)*)\"");
                    var err = errMatch.Success ? Unescape(errMatch.Groups["v"].Value) : "Unknown error";
                    pending.Tcs.TrySetException(new InvalidOperationException(err));
                }
            }
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    private static string Unescape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return Regex.Unescape(s);
    }

    private static async Task RespondPingAsync(StreamWriter writer)
    {
        await writer.WriteLineAsync("{\"type\":\"pong\"}");
        await writer.FlushAsync();
    }

    private class BridgeConnection : IDisposable
    {
        public StreamWriter         Writer         { get; }
        public BridgeRegistration?  Registration   { get; set; }

        public BridgeConnection(StreamWriter writer) => Writer = writer;

        public void Dispose()
        {
            try { Writer.Dispose(); } catch { /* ignore */ }
        }
    }

    private class PendingCall
    {
        public TaskCompletionSource<string> Tcs       { get; }
        public int                          TargetPid { get; }
        public PendingCall(TaskCompletionSource<string> tcs, int targetPid)
        {
            Tcs       = tcs;
            TargetPid = targetPid;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}
