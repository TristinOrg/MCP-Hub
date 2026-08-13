# Project Context

- Last analyzed: 2026-08-13 at commit `4aa2539`.
- This repository is not a Unity project root. It is a .NET 8 Avalonia Hub plus an injectable UPM package.
- `Tristin.MCPManager.Core` owns Coplay server lifecycle, upstream status reads, and transparent HTTP proxying.
- `Tristin.MCPManager.Unity` owns Unity process discovery and reversible manifest injection.
- `Tristin.MCPManager.UI` coordinates connection state and user actions.
- `unity-bridge-package` is editor-only bootstrap code. It depends on Coplay MCP for Unity and must not implement duplicate Unity tools.
- Coplay package and Python server versions are pinned together at 10.1.0.
- Public MCP endpoint: `http://127.0.0.1:9000/mcp`.
- Upstream Coplay endpoint: `http://127.0.0.1:8080/mcp`.
- Validation: build the solution in Release. Full Unity package/runtime validation requires a running Unity project selected in the desktop app.
- Current limitation: Coplay's `/api/instances` response lacks project paths, so UI matching uses project name.
