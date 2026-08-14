# Unity MCP Hub

Unity MCP Hub is a Unity-specific Windows desktop hub that gives MCP clients such as Codex one stable endpoint for controlling multiple Unity Editor instances through the official [Coplay MCP for Unity](https://github.com/CoplayDev/unity-mcp).

This project is dedicated to Unity and is not a universal MCP server manager. The Hub does not reimplement Unity commands: it manages one pinned Coplay Unity package in a shared local cache, injects one local UPM reference, starts the matching Coplay MCP server, and forwards `http://127.0.0.1:9000/mcp` to it. Coplay owns tool discovery, execution, and per-client multi-instance routing.

For MCP client configuration, multi-Unity routing, and a manual JSON-RPC/PowerShell example, see the [AI Agent Guide](MCP_AGENT_GUIDE.md).

## Responsibilities and endpoints

Unity MCP Hub manages the local Coplay integration lifecycle; Coplay provides the MCP protocol, Unity tools, and multi-instance routing.

The Hub is responsible for:

- discovering running Unity Editors;
- preparing one shared, pinned Coplay Unity package;
- reversibly injecting its local UPM reference into selected projects;
- restoring `Packages/manifest.json` and `Packages/packages-lock.json` on disconnect;
- recovering unfinished injections after an unclean shutdown;
- starting and version-checking the matching Coplay MCP Server;
- bundling the .NET, Python, Coplay server, and Unity package dependencies in the offline release;
- displaying connected Unity instance state; and
- exposing the stable public MCP endpoint at `http://127.0.0.1:9000/mcp`.

Coplay is responsible for:

- maintaining connections to multiple Unity Editors;
- exposing `mcpforunity://instances` and `set_active_instance`;
- keeping the active Unity selection isolated per MCP client session; and
- executing all scene, prefab, GameObject, asset, console, and test tools.

The Hub currently forwards `9000/mcp` transparently to Coplay at `8080/mcp`. A client can technically connect directly to `http://127.0.0.1:8080/mcp` and retain the same Unity tools and multi-instance behavior while the Coplay server is running. External clients should nevertheless use `9000/mcp`: it is the Hub's stable public contract, avoids coupling client configuration to Coplay's internal port, and leaves room for future authentication, diagnostics, policy, and upstream-port changes. Do not configure both endpoints in one client, because that exposes duplicate Unity tool sets.

Multi-Unity routing is provided by Coplay rather than reimplemented by the Hub. Every independent agent should create its own MCP session, read `mcpforunity://instances`, and call `set_active_instance` before performing mutations when more than one Editor is connected.

## Requirements

- .NET 8 SDK
- Unity 2021.3 or newer
- [uv](https://docs.astral.sh/uv/) with `uvx` on `PATH`

## Run

```powershell
dotnet run --project src/Tristin.MCPManager.UI
```

Then:

1. Open one or more Unity projects.
2. Select each project in the Hub and click **Connect**.
3. Configure Codex with the HTTP MCP URL `http://127.0.0.1:9000/mcp`.
4. When multiple editors are connected, call Coplay's `set_active_instance` in each MCP client session to select its Unity target.

Disconnect restores the exact original `Packages/manifest.json` and `Packages/packages-lock.json` state. A recovery journal restores unfinished injections the next time the Hub starts after a crash.

## Architecture

```text
Codex and other MCP clients
          |
 http://127.0.0.1:9000/mcp
          |
 Unity MCP Hub reverse proxy
          |
 Official Coplay MCP Server (127.0.0.1:8080)
          |
    WebSocket sessions
       /       \
 Unity A       Unity B
 Coplay pkg    Coplay pkg
```

Each Unity project loads only `com.coplaydev.unity-mcp` through a local `file:` reference. The cached package contains one Hub integration script that configures the local endpoint and starts Coplay's own bridge; there is no second package and no duplicated tool implementation.

## Build

```powershell
dotnet build Tristin.MCPManager.sln --configuration Release
```

## Complete offline package

The Windows x64 offline package includes the .NET runtime, Python runtime, Coplay MCP Server 10.1.0, and the integrated Coplay Unity package. Building the package requires `uvx` only on the build machine:

```powershell
pwsh ./tools/build-offline.ps1
pwsh ./tools/test-offline.ps1
```

Distribute the generated ZIP and SHA-256 file from `artifacts`. End users extract the ZIP, run `UnityMCPHub.exe`, connect their Unity projects, and configure MCP clients with `http://127.0.0.1:9000/mcp`. They do not need .NET, Python, `uv`, Git, or a GitHub connection.

## License

MIT. MCP for Unity is maintained by Coplay and licensed separately under its repository license.
