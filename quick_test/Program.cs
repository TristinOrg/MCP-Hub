// Quick test: start IPC host + MCP proxy, call create_prefab via HTTP
using Tristin.MCPManager.Core.Ipc;
using Tristin.MCPManager.Core.Mcp;

var ipcHost = new NamedPipeIpcBridgeHost();
var mcpProxy = new SimpleHttpMcpServerProxy(ipcHost);

using var cts = new CancellationTokenSource();
await ipcHost.StartAsync("", cts.Token);
_ = Task.Run(() => mcpProxy.StartAsync("http://localhost:9001/", cts.Token));

Console.WriteLine("IPC + MCP proxy started on http://localhost:9001/");
Console.WriteLine("Waiting for Bridge registration...");

// Wait for bridge
for (int i = 0; i < 60; i++)
{
    if (ipcHost.RegisteredBridges.Count > 0)
    {
        Console.WriteLine($"Bridge registered! Count={ipcHost.RegisteredBridges.Count}");
        break;
    }
    await Task.Delay(1000);
}

if (ipcHost.RegisteredBridges.Count == 0)
{
    Console.WriteLine("ERROR: No bridge registered after 60s");
    return;
}

// Set active editor
var firstBridge = ipcHost.RegisteredBridges.Values.First();
mcpProxy.ActiveEditor = new Tristin.MCPManager.Core.Models.EditorInstance
{
    EditorType  = firstBridge.EditorType,
    ProcessId   = firstBridge.Pid,
    ProjectName = firstBridge.ProjectName,
    ProjectPath = firstBridge.ProjectPath,
    Version     = ""
};
Console.WriteLine($"Active editor set: PID={firstBridge.Pid} project={firstBridge.ProjectName}");

// Call create_prefab
var body = new
{
    jsonrpc = "2.0",
    id = 1,
    method = "tools/call",
    @params = new
    {
        name = "unity.create_prefab",
        arguments = new
        {
            path = "Assets/Prefabs/test.prefab",
            children = new object[]
            {
                new { name = "UI", components = new[] { "RectTransform" } },
                new { name = "Text", components = new[] { "TextMeshPro", "RectTransform" } }
            }
        }
    }
};

var json = System.Text.Json.JsonSerializer.Serialize(body);
Console.WriteLine($"Sending: {json}");

using var client = new HttpClient();
var resp = await client.PostAsync("http://localhost:9001/",
    new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
var result = await resp.Content.ReadAsStringAsync();
Console.WriteLine($"Response ({resp.StatusCode}): {result}");

// Also call create_text
var body2 = new
{
    jsonrpc = "2.0",
    id = 2,
    method = "tools/call",
    @params = new
    {
        name = "unity.create_text",
        arguments = new
        {
            path = "Assets/Scripts/Test.cs",
            content = "public class Test { public void Hello() { Debug.Log(\"Hello from MCP!\"); } }"
        }
    }
};
var json2 = System.Text.Json.JsonSerializer.Serialize(body2);
var resp2 = await client.PostAsync("http://localhost:9001/",
    new StringContent(json2, System.Text.Encoding.UTF8, "application/json"));
var result2 = await resp2.Content.ReadAsStringAsync();
Console.WriteLine($"Response ({resp2.StatusCode}): {result2}");
