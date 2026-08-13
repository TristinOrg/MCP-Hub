# Roadmap

## Current Status (v0.1.0 — MVP)

### Completed

- **Unity Editor detection** — scan running Unity processes via WMI, extract PID / project path / version
- **Dynamic Bridge injection** — backup `manifest.json`, inject local UPM package, wait for domain reload
- **Minimal Unity Bridge** — `[InitializeOnLoad]` auto-start, NamedPipe IPC client, sample command handlers
- **NamedPipe IPC host** — multi-client support, JSON-lines protocol, heartbeat, 30s call timeout
- **MCP Server Proxy** — JSON-RPC 2.0 over HTTP (`initialize` / `tools/list` / `tools/call`), REST endpoints (`/health`, `/tools`)
- **Avalonia UI** — dark-themed desktop client with editor list, connect/disconnect flow, progress tracking, log panel
- **Full cleanup** — restore `manifest.json`, remove backup directory, leave `git status` clean

### MVP Tools

| Tool | Description |
|------|-------------|
| `ping` | Health check |
| `unity.editor_info` | Get Unity version, project path, play mode state |
| `unity.list_scenes` | List scenes in build settings |
| `unity.create_gameobject` | Create a new GameObject in the current scene |
| `unity.save_project` | Save all assets and project |
| `unity.refresh_assets` | Force refresh AssetDatabase |

---

## Short Term (v0.2.0)

### Multi-Editor Adapter Registry

**Goal**: refactor `MainViewModel` from hardcoded Unity services to a registry-based factory pattern.

- Introduce `IEditorAdapter` aggregate interface combining `IEditorDetector` + `IBridgeInjector`
- Register adapters by `EditorType` string (e.g. `"Unity"`, `"Blender"`)
- `MainViewModel` resolves the correct adapter based on `SelectedEditor.EditorType`
- Multiple detectors run concurrently, merging results into a unified editor list

### Dynamic Tool Discovery

**Goal**: replace hardcoded Unity tool list in `NamedPipeIpcBridgeHost.ListToolsAsync`.

- Add `list_tools` request to the IPC protocol
- Bridge reports its supported tools at registration time
- `ListToolsAsync` queries the Bridge instead of returning a static list

### Robustness

- Auto-restore `manifest.json` on unexpected app exit (crash recovery)
- Detect Bridge disconnection and reflect state in UI immediately
- Add `--port` CLI argument to customize MCP endpoint

---

## Mid Term (v0.3.0)

### Blender Adapter

**Goal**: add Blender as a second editor type to validate the multi-editor architecture.

- `BlenderProcessDetector` — detect running Blender instances via process scan
- `BlenderBridgeInjector` — install Blender addon dynamically (or via Python script injection)
- `BlenderBridge` addon — minimal IPC client using the same JSON-lines protocol over NamedPipe
- Sample tools: `blender.scene_info`, `blender.list_objects`, `blender.create_mesh`

### Cross-Platform IPC

**Goal**: support macOS and Linux.

- Add WebSocket IPC transport as alternative to NamedPipe (Windows-only)
- Auto-detect OS and select transport at runtime
- Bridge package selects matching transport

### Figma Adapter (Research)

- Evaluate Figma plugin API feasibility for Bridge injection
- Design WebSocket-based Bridge for Figma (browser plugin sandbox)

---

## Long Term (v1.0.0)

### MCP Flow Recorder

**Goal**: record and replay MCP call sequences — like Postman Collections or GitHub Actions for MCP.

- Record mode: capture every `tools/call` invocation as a step
- Save flows as `.flow` JSON files (name, description, steps with tool + args)
- Replay mode: execute a flow without AI agent / LLM / token cost
- Flow editor UI: reorder steps, parameterize values, conditional branching

```
Codex: "Create Inventory UI"
  → unity.create_gameobject
  → unity.add_component
  → unity.set_property
  → unity.save_prefab

Saved as: CreateInventoryUI.flow
Next time: Run Flow → instant replay, no LLM needed
```

### MCP Flow Marketplace

- Share `.flow` files via community repository
- One-click import and run

### Advanced Multi-Editor Routing

- Route different tool calls to different editors simultaneously
- Example: `unity.*` → Unity, `blender.*` → Blender, `figma.*` → Figma
- Visual routing matrix in UI

### Plugin System

- Third-party editor adapters as loadable plugins
- Hot-reload adapters without restarting MCP-Hub
- SDK for adapter development

---

## Architecture Extensibility

The core interfaces are already editor-agnostic:

```
IEditorDetector   — detect running editor instances (any type)
IBridgeInjector   — inject/remove Bridge into editor (any mechanism)
IIpcBridgeHost    — route MCP calls by PID (transport-agnostic)
IMcpServerProxy   — single MCP endpoint for AI agents (protocol-agnostic)
```

Adding a new editor adapter requires:

1. Implement `IEditorDetector` — process detection logic
2. Implement `IBridgeInjector` — injection mechanism (addon, script, package)
3. Create a minimal Bridge — IPC client using the same JSON-lines protocol
4. Register adapter in `MainViewModel` — one line in the adapter registry

**No changes to core interfaces or MCP protocol needed.**
