// Quick test: multi-instance routing via MCP proxy
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

using var client = new HttpClient();

// Step 1: list_instances
Console.WriteLine("\n=== Step 1: unity.list_instances ===");
var resp1 = await CallMcp(client, "unity.list_instances", new { });
Console.WriteLine(resp1);

// Step 2: set_active_instance
var firstPid = ipcHost.RegisteredBridges.Keys.First();
Console.WriteLine($"\n=== Step 2: unity.set_active_instance (PID={firstPid}) ===");
var resp2 = await CallMcp(client, "unity.set_active_instance", new { pid = firstPid });
Console.WriteLine(resp2);

// Step 3: create_prefab
Console.WriteLine("\n=== Step 3: unity.create_prefab ===");
var resp3 = await CallMcp(client, "unity.create_prefab", new
{
    path = "Assets/Prefabs/test.prefab",
    children = new object[]
    {
        new { name = "UI", components = new[] { "RectTransform" } },
        new { name = "Text", components = new[] { "TextMeshPro", "RectTransform" } }
    }
});
Console.WriteLine(resp3);

// Step 4: create_text
Console.WriteLine("\n=== Step 4: unity.create_text ===");
var resp4 = await CallMcp(client, "unity.create_text", new
{
    path = "Assets/Scripts/Test.cs",
    content = "public class Test { public void Hello() { Debug.Log(\"Hello from MCP!\"); } }"
});
Console.WriteLine(resp4);

Console.WriteLine("\n=== Done ===");

static async Task<string> CallMcp(HttpClient client, string toolName, object args)
{
    var body = new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "tools/call",
        @params = new { name = toolName, arguments = args }
    };
    var json = System.Text.Json.JsonSerializer.Serialize(body);
    var resp = await client.PostAsync("http://localhost:9001/",
        new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
    return $"({resp.StatusCode}) {await resp.Content.ReadAsStringAsync()}";
}
