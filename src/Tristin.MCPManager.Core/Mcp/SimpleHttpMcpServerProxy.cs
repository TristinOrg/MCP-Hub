using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tristin.MCPManager.Core.Interfaces;
using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Core.Mcp;

/// <summary>
/// Simplified MCP Server Proxy: exposes a JSON-RPC 2.0 over HTTP endpoint
/// and routes tool calls to the currently active Bridge.
/// </summary>
public class SimpleHttpMcpServerProxy : IMcpServerProxy, IAsyncDisposable
{
    private readonly IIpcBridgeHost  _bridgeHost;
    private HttpListener?           _listener;
    private CancellationTokenSource? _cts;
    private Task?                   _runLoop;
    private EditorInstance?         _activeEditor;

    public SimpleHttpMcpServerProxy(IIpcBridgeHost bridgeHost)
    {
        _bridgeHost = bridgeHost;
    }

    public EditorInstance? ActiveEditor
    {
        get => _activeEditor;
        set
        {
            var old = _activeEditor;
            _activeEditor = value;
            if (!ReferenceEquals(old, value))
                ActiveEditorChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<EditorInstance?>? ActiveEditorChanged;

    public Task StartAsync(string listenEndpoint, CancellationToken cancellationToken = default)
    {
        var prefix = listenEndpoint.EndsWith('/') ? listenEndpoint : listenEndpoint + "/";

        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();

        _cts     = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runLoop = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _listener?.Stop();   } catch { /* ignore */ }
        try
        {
            if (_runLoop != null) await _runLoop;
        }
        catch (OperationCanceledException) { /* ignore */ }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // GetContext failed (e.g. listener stopped)
                try { await Task.Delay(50, ct); } catch (OperationCanceledException) { throw; }
                continue;
            }

            _ = Task.Run(async () =>
            {
                try { await HandleRequestAsync(ctx, ct); }
                catch (Exception ex)
                {
                    try
                    {
                        ctx.Response.StatusCode = 500;
                        var buf = Encoding.UTF8.GetBytes($"{{\"error\":\"{Escape(ex.Message)}\"}}");
                        await ctx.Response.OutputStream.WriteAsync(buf, 0, buf.Length, ct);
                    }
                    catch { /* ignore */ }
                    finally { try { ctx.Response.Close(); } catch { /* ignore */ } }
                }
            }, ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req  = ctx.Request;
        var resp = ctx.Response;
        resp.ContentType = "application/json; charset=utf-8";
        resp.AddHeader("Access-Control-Allow-Origin", "*");
        resp.AddHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
        resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        if (req.HttpMethod == "OPTIONS")
        {
            resp.StatusCode = 204;
            resp.Close();
            return;
        }

        try
        {
            string body;
            using (StreamReader sr = new(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                body = await sr.ReadToEndAsync(ct);

            JsonNode? rpcDoc = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try { rpcDoc = JsonNode.Parse(body); }
                catch { /* parse failed — fall through to REST routing */ }
            }

            // 1) JSON-RPC handling (MCP tools/call etc.)
            if (rpcDoc != null)
            {
                var method  = rpcDoc["method"]?.GetValue<string>();
                var id      = rpcDoc["id"]?.ToJsonString();
                var @params = rpcDoc["params"];

                JsonNode? result;
                try
                {
                    result = method switch
                    {
                        "initialize" => HandleInitialize(),
                        "tools/list" => await HandleToolsListAsync(ct),
                        "tools/call" => await HandleToolsCallAsync(@params, ct),
                        "ping"       => JsonValue.Create("pong"),
                        _            => throw new NotSupportedException($"Method '{method}' not implemented")
                    };
                }
                catch (Exception ex)
                {
                    JsonObject errDoc = new()
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"]      = string.IsNullOrEmpty(id) ? null : JsonNode.Parse(id),
                        ["error"]   = new JsonObject
                        {
                            ["code"]    = -32603,
                            ["message"] = ex.Message
                        }
                    };
                    await WriteJsonAndClose(resp, errDoc, ct);
                    return;
                }

                JsonObject okDoc = new()
                {
                    ["jsonrpc"] = "2.0",
                    ["id"]      = string.IsNullOrEmpty(id) ? null : JsonNode.Parse(id),
                    ["result"]  = result
                };
                await WriteJsonAndClose(resp, okDoc, ct);
                return;
            }

            // 2) REST-style routes (for manual testing)
            var path = req.Url?.AbsolutePath ?? "/";
            switch (path)
            {
                case "/health":
                    await WriteJsonAndClose(resp, new JsonObject
                    {
                        ["status"]      = "ok",
                        ["activePid"]   = ActiveEditor?.ProcessId ?? 0,
                        ["activeName"]  = ActiveEditor?.ProjectName,
                        ["activeState"] = ActiveEditor?.State.ToString()
                    }, ct);
                    return;
                case "/tools":
                {
                    var list = ActiveEditor != null
                        ? await _bridgeHost.ListToolsAsync(ActiveEditor.ProcessId, ct)
                        : Array.Empty<McpToolDefinition>();
                    await WriteJsonAndClose(resp, JsonSerializer.SerializeToNode(list), ct);
                    return;
                }
                default:
                    resp.StatusCode = 404;
                    var buf = Encoding.UTF8.GetBytes("{\"error\":\"Not found. Try POST / with JSON-RPC or GET /health /tools\"}");
                    await resp.OutputStream.WriteAsync(buf, 0, buf.Length, ct);
                    resp.Close();
                    return;
            }
        }
        finally
        {
            try { resp.Close(); } catch { /* ignore */ }
        }
    }

    private static JsonNode HandleInitialize()
    {
        return new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"]    = "Tristin MCP Runtime Manager Proxy",
                ["version"] = "0.1.0"
            }
        };
    }

    private async Task<JsonNode> HandleToolsListAsync(CancellationToken ct)
    {
        if (ActiveEditor == null)
            return new JsonArray();

        var tools = await _bridgeHost.ListToolsAsync(ActiveEditor.ProcessId, ct);
        JsonArray arr = new();
        foreach (var t in tools)
        {
            arr.Add(new JsonObject
            {
                ["name"]        = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = JsonSerializer.SerializeToNode(t.InputSchema ?? new { })
            });
        }
        return arr;
    }

    private async Task<JsonNode> HandleToolsCallAsync(JsonNode? @params, CancellationToken ct)
    {
        if (ActiveEditor == null)
            throw new InvalidOperationException("No active Unity Editor. Select one in Runtime Manager first.");

        var name = @params?["name"]?.GetValue<string>()
                   ?? throw new ArgumentException("Missing 'name' in params.");

        var argsNode = @params?["arguments"];
        var argsJson = argsNode?.ToJsonString() ?? "{}";

        var resultStr = await _bridgeHost.InvokeToolAsync(
            ActiveEditor.ProcessId, name, argsJson, ct);

        // resultStr is already JSON — try to parse, fall back to raw string
        try
        {
            var parsed = JsonNode.Parse(resultStr);
            return new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = parsed?.ToJsonString() ?? resultStr
                    }
                }
            };
        }
        catch
        {
            return new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = resultStr }
                }
            };
        }
    }

    private static async Task WriteJsonAndClose(HttpListenerResponse resp, JsonNode? doc, CancellationToken ct)
    {
        var json = doc?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
        var buf  = Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = buf.Length;
        await resp.OutputStream.WriteAsync(buf, 0, buf.Length, ct);
        resp.Close();
    }

    private static string Escape(string s)
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

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
        _listener?.Close();
    }
}
