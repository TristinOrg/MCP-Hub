# MCP-Hub

> Universal MCP Multi-Server Runtime Manager — dynamically inject, route, and manage MCP Bridges across multiple editor instances without polluting project dependencies.

## Problem

Current Unity MCP solutions require **every Unity project** to permanently install an MCP package (`Packages/manifest.json`), which:

- Pollutes the Git repository with tool dependencies
- Requires per-project maintenance
- Only supports one active MCP server at a time (opening a second project kicks off the first)

## Solution

MCP-Hub runs as a **standalone desktop app** that:

1. **Detects** all running Unity Editor instances
2. **Dynamically injects** a minimal Bridge package into the selected project (temporary, fully reversible)
3. **Routes** all MCP tool calls from AI agents (Codex, etc.) through a single endpoint to the active editor
4. **Cleans up** on disconnect — restores the original `manifest.json`, leaving `git status` clean

Think of it as **"Attach Debugger"** for MCP — not "install plugin in every project."

## Architecture

```
                 AI Agent (Codex)
                      |
                 MCP Protocol
                      |
            MCP-Hub Runtime Manager
                      |
        -------------------------------
        |                             |
  Unity Bridge                 Future Adapters
  (Named Pipe IPC)             (Figma, Blender, ...)
        |
  UnityEditor API
```

### Core Principles

- **MCP-Hub** handles: connection, routing, lifecycle — **no** Unity business logic
- **Unity Bridge** stays minimal: IPC, command receive, UnityEditor API execute, result return
- All modifications are **fully reversible** — `git status` returns to clean after disconnect
- Stability over feature count

## Project Structure

```
MCP-Hub/
├── src/
│   ├── Tristin.MCPManager.Core/          # Abstractions, IPC host, MCP proxy
│   │   ├── Models/                        # EditorInstance, BridgeRegistration, etc.
│   │   ├── Interfaces/                    # IEditorDetector, IBridgeInjector, IIpcBridgeHost, IMcpServerProxy
│   │   ├── Ipc/                           # NamedPipeIpcBridgeHost
│   │   └── Mcp/                           # SimpleHttpMcpServerProxy
│   ├── Tristin.MCPManager.Unity/         # Unity-specific: process detection, manifest injection
│   │   ├── UnityProcessDetector.cs
│   │   ├── UnityManifestManager.cs
│   │   └── UnityBridgeInjector.cs
│   └── Tristin.MCPManager.UI/            # Avalonia UI desktop client
│       ├── ViewModels/                    # MainViewModel (MVVM orchestration)
│       └── Views/                         # MainWindow.axaml
├── unity-bridge-package/                 # Minimal UPM package injected into Unity projects
│   ├── Runtime/
│   │   ├── Bootstrap/                    # [InitializeOnLoad] auto-start
│   │   ├── Ipc/                          # NamedPipe client
│   │   └── Commands/                     # UnityEditor API command handlers
│   └── package.json
└── Tristin.MCPManager.sln
```

## Quick Start

### Prerequisites

- .NET 8 SDK
- Unity Editor 2021.3+ (for Bridge injection)

### Build

```bash
dotnet build Tristin.MCPManager.sln
```

### Run

```bash
dotnet run --project src/Tristin.MCPManager.UI
```

### Connect to Unity

1. Open one or more Unity projects
2. Launch MCP-Hub — it auto-detects running Unity instances
3. Select a Unity Editor in the list and click **Connect**
4. MCP-Hub injects the Bridge, waits for it to register via IPC
5. Your AI agent can now send MCP tool calls to `http://localhost:9000/`

### Disconnect

Click **Disconnect** — MCP-Hub restores the original `manifest.json` and cleans up all temporary files. `git status` will be clean.

## MCP Endpoint

MCP-Hub exposes a JSON-RPC 2.0 over HTTP endpoint:

```
http://localhost:9000/
```

### Available Tools (MVP)

| Tool | Description |
|------|-------------|
| `ping` | Health check |
| `unity.editor_info` | Get Unity version, project path, play mode state |
| `unity.list_scenes` | List scenes in build settings |
| `unity.create_gameobject` | Create a new GameObject in the current scene |
| `unity.save_project` | Save all assets and project |
| `unity.refresh_assets` | Force refresh AssetDatabase |

### REST Endpoints (for testing)

- `GET /health` — Runtime status and active editor info
- `GET /tools` — List available tools for the active editor
- `POST /` — JSON-RPC 2.0 request

## Tech Stack

- **C# / .NET 8**
- **Avalonia UI 12** — cross-platform desktop UI
- **CommunityToolkit.Mvvm** — MVVM pattern
- **NamedPipe IPC** — Windows; WebSocket reserved for cross-platform
- **Unity UPM** — dynamic package injection via `manifest.json`

## Roadmap

- [x] Phase 1: Unity process detection + Avalonia UI
- [x] Phase 2: Unity Bridge package (auto-start, IPC, commands)
- [x] Phase 3: Dynamic package injection (backup/inject/restore)
- [x] Phase 4: NamedPipe IPC host with multi-client routing
- [x] Phase 5: MCP Server Proxy (JSON-RPC over HTTP)
- [ ] MCP Flow recorder (record & replay MCP call sequences)
- [ ] Figma / Blender adapter bridges
- [ ] Cross-platform IPC (WebSocket for macOS/Linux)

## License

MIT — see [LICENSE](LICENSE).

## Author

Tristin Wen — [Tristin_Wen@outlook.com](mailto:Tristin_Wen@outlook.com)

> Not affiliated with Unity Technologies.
