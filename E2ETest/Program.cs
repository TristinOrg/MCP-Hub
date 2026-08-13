// End-to-end test harness:
// 1. Scan Unity processes via UnityProcessDetector
// 2. Pick first instance, inject Bridge via UnityBridgeInjector
// 3. Start NamedPipe IPC host
// 4. Wait for Bridge to register (or time out and show diagnostic log paths)
// 5. Call MCP HTTP create_prefab + create_text

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tristin.MCPManager.Core.Ipc;
using Tristin.MCPManager.Core.Mcp;
using Tristin.MCPManager.Core.Models;
using Tristin.MCPManager.Unity;

var cts = new CancellationTokenSource();

// Locate Bridge package
var pkgCandidates = new[]
{
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "unity-bridge-package")),
    Path.Combine(Environment.CurrentDirectory, "unity-bridge-package"),
    Path.Combine(AppContext.BaseDirectory, "unity-bridge-package")
};
var bridgePkgPath = pkgCandidates.FirstOrDefault(Directory.Exists)
    ?? throw new Exception("Could not find unity-bridge-package directory");

// IPC log sink
List<string> allLogs = new();
void Log(string msg)
{
    var ts = DateTime.Now.ToString("HH:mm:ss");
    var line = $"[{ts}] {msg}";
    allLogs.Add(line);
    Console.WriteLine(line);
}
NamedPipeIpcBridgeHost.LogSink = msg => Log(msg.Replace("[IPC] ", "[IPC] "));

// Step 1: detect Unity processes
Log("Step 1: Scanning Unity processes ...");
var detector = new UnityProcessDetector();
var instances = await detector.DetectAsync(cts.Token);
Log($"Found {instances.Count} Unity editor(s)");

if (instances.Count == 0)
{
    Log("FAIL: No Unity editors detected. Open Unity and run again.");
    return;
}

var target = instances[0];
Log($"Target: {target.ProjectName} PID={target.ProcessId} path={target.ProjectPath} version={target.Version}");

// Step 2: clean up any previous injection leftovers
Log("Step 2: Cleaning up previous leftovers ...");
var backupDir = Path.Combine(target.ProjectPath, ".tristin_mcp_backup");
if (Directory.Exists(backupDir))
{
    await UnityManifestManager.RestoreAsync(target.ProjectPath, cts.Token);
    Log("  Restored previous manifest backup");
}

// Also clean up any Assets/Editor/MCPHubBridge/ left from previous attempts
var leftoverDir = Path.Combine(target.ProjectPath, "Assets", "Editor", "MCPHubBridge");
if (Directory.Exists(leftoverDir))
{
    try
    {
        Directory.Delete(leftoverDir, true);
        Log("  Cleaned up leftover Assets/Editor/MCPHubBridge/");
        Log("  Waiting for Unity to complete domain reload (old Bridge unload) ...");
        // Wait for Unity to finish recompiling after deletion
        await Task.Delay(3000, cts.Token);
    }
    catch { Log("  Could not clean up leftover Assets/Editor/MCPHubBridge/"); }
}

// Clean Unity-side Bridge log so we can tell if new writes happened
var unityLog = Path.Combine(target.ProjectPath, "Library", "TristinMCPBridge.log");
if (File.Exists(unityLog))
{
    File.WriteAllText(unityLog, "");
    Log($"  Cleared {unityLog}");
}

// Step 3: Start IPC host + MCP proxy BEFORE injection so the pipe is ready
Log("Step 3: Starting IPC host and MCP proxy ...");
var ipcHost = new NamedPipeIpcBridgeHost();
var mcpProxy = new SimpleHttpMcpServerProxy(ipcHost);
await ipcHost.StartAsync("", cts.Token);
_ = Task.Run(() => mcpProxy.StartAsync("http://localhost:9001/", cts.Token), cts.Token);
Log("  IPC listening + MCP proxy on http://localhost:9001/");

// Step 4: Inject Bridge
Log($"Step 4: Injecting Bridge into {target.ProjectName} ...");
var injector = new UnityBridgeInjector { BridgePackagePath = bridgePkgPath };
void Prog(int p, string m) => Log($"  Inject [{p}%] {m}");
var injectOk = await injector.InjectAsync(target, new Progress<(int, string)>(t => Prog(t.Item1, t.Item2)), cts.Token);
if (!injectOk)
{
    Log("FAIL: InjectAsync returned false");
    return;
}
Log("  Bridge scripts copied to Assets/Editor/MCPHubBridge/. Unity should detect, recompile, reload, and Bridge auto-connect via NamedPipe.");

// Step 5: Wait for Bridge registration with detailed diagnostics
Log("Step 5: Waiting for Bridge to register (180s timeout) ...");
var regDeadline = DateTime.UtcNow.AddSeconds(180);
bool registered = false;
EditorInstance? liveTarget = target;
int lastLogged = -1;
while (DateTime.UtcNow < regDeadline)
{
    if (ipcHost.RegisteredBridges.ContainsKey(target.ProcessId))
    {
        registered = true;
        liveTarget.State = EditorState.Connected;
        mcpProxy.ActiveEditor = liveTarget;
        break;
    }

    var elapsed = (int)(DateTime.UtcNow - regDeadline.AddSeconds(-180)).TotalSeconds;
    if (elapsed != lastLogged && elapsed % 10 == 0)
    {
        lastLogged = elapsed;
        Log($"  Waiting ... {elapsed}s elapsed");

        // Diagnostic: check if Unity-side log file has content
        if (File.Exists(unityLog))
        {
            var logContent = File.ReadAllText(unityLog);
            if (!string.IsNullOrWhiteSpace(logContent))
            {
                var lines = logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(5);
                Log($"  ** Unity Library/TristinMCPBridge.log tail:");
                foreach (var l in lines) Log($"     {l.Trim()}");
            }
            else
            {
                // Check that the bridge dependency is actually in manifest.json
                var manifest = Path.Combine(target.ProjectPath, "Packages", "manifest.json");
                if (File.Exists(manifest))
                {
                    var manifestText = File.ReadAllText(manifest);
                    var injected = manifestText.Contains("com.tristin.unity-mcp-bridge");
                    Log($"  manifest.json contains bridge dep: {injected}");
                }
                Log($"  (Bridge log file exists but is empty — Unity Bridge code has not yet run)");
                Log($"  HINT: Click the Unity Editor window NOW to force package refresh / domain reload.");
            }
        }
        else
        {
            Log($"  (Bridge log file does not exist yet — Unity has not started compiling Bridge)");
        }
    }

    await Task.Delay(500, cts.Token);
}

if (!registered)
{
    Log("============================================================");
    Log("FAIL: Bridge did not register within 180s.");
    Log("Diagnostic data:");
    Log("  1. Check Unity Console for compile errors in package 'com.tristin.unity-mcp-bridge'");
    Log($"  2. Check {unityLog} for Bridge startup logs");
    Log("  3. Verify Assets/Packages/manifest.json has the bridge dependency injected");
    Log("  4. Verify [Unity -> Window -> Package Manager] shows 'Tristin MCP Runtime Bridge' under In Project");
    Log("  5. If package shows compile errors, paste them to debug the Bridge code");

    if (File.Exists(unityLog))
    {
        var fullLog = File.ReadAllText(unityLog);
        if (!string.IsNullOrWhiteSpace(fullLog))
        {
            Log("Full Unity-side Bridge log:");
            Log(fullLog);
        }
    }
    return;
}

var reg = ipcHost.RegisteredBridges[target.ProcessId];
Log($"Step 5 OK: Bridge registered! project={reg.ProjectName} pid={reg.Pid} endpoint={reg.Endpoint}");

// Step 6: test tools/list via HTTP
Log("Step 6: Testing MCP HTTP endpoints ...");
var http = new HttpClient();
var rpcReq = new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } };
var content = new StringContent(JsonSerializer.Serialize(rpcReq), Encoding.UTF8, "application/json");
var resp = await http.PostAsync("http://localhost:9001/", content, cts.Token);
var respText = await resp.Content.ReadAsStringAsync();
Log($"  tools/list HTTP {(int)resp.StatusCode}: {Truncate(respText, 400)}");

// Step 7: Call create_prefab via MCP HTTP
Log("Step 7: Calling unity.create_prefab ...");
var prefabArgs = new
{
    path = "Assets/Prefabs/MCPTest/Test.prefab",
    children = new[]
    {
        new { name = "ChildText", components = new[] { "RectTransform" } }
    }
};
var callPrefab = new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "unity.create_prefab", arguments = prefabArgs } };
content = new StringContent(JsonSerializer.Serialize(callPrefab), Encoding.UTF8, "application/json");
resp = await http.PostAsync("http://localhost:9001/", content, cts.Token);
respText = await resp.Content.ReadAsStringAsync();
Log($"  create_prefab HTTP {(int)resp.StatusCode}: {Truncate(respText, 400)}");
var prefabCreated = respText.Contains("\"ok\"") || respText.Contains("Test.prefab");

// Step 8: Call create_text via MCP HTTP
Log("Step 8: Calling unity.create_text ...");
var textArgs = new { path = "Assets/MCPTest/Readme.txt", content = "MCP Hub Test - created at " + DateTime.Now.ToString("o") };
var callText = new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "unity.create_text", arguments = textArgs } };
content = new StringContent(JsonSerializer.Serialize(callText), Encoding.UTF8, "application/json");
resp = await http.PostAsync("http://localhost:9001/", content, cts.Token);
respText = await resp.Content.ReadAsStringAsync();
Log($"  create_text HTTP {(int)resp.StatusCode}: {Truncate(respText, 400)}");
var textCreated = respText.Contains("\"ok\"") || respText.Contains("Readme.txt");

Log("============================================================");
Log("Summary:");
Log($"  Bridge registered: {registered}");
Log($"  Prefab created:    {prefabCreated}");
Log($"  Text file created: {textCreated}");
if (registered && prefabCreated && textCreated)
    Log("ALL OK — end-to-end flow is working");
else
    Log("Some steps failed — check logs above");

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...(+" + (s.Length - max) + ")";
