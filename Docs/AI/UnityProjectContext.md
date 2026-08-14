# Project Context

- Last analyzed: 2026-08-14 at commit `b083587`.
- This repository is not a Unity project root. It is a .NET 8 Avalonia Hub that integrates a cached Coplay UPM package.
- `Tristin.MCPManager.Core` owns Coplay server lifecycle, upstream status reads, and transparent HTTP proxying.
- `Tristin.MCPManager.Unity` owns Unity process discovery, the shared Coplay package cache, reversible package-state injection, and crash recovery.
- `Tristin.MCPManager.UI` coordinates connection state and user actions.
- The cached Coplay package receives one generated editor-only auto-connect script. No second UPM package or duplicate Unity tools exist.
- Coplay package and Python server versions are pinned together at 10.1.0.
- Each Unity project receives one local Coplay `file:` reference. Coplay is downloaded once under the user's local application-data cache.
- Both `manifest.json` and the original presence/content of `packages-lock.json` are restored.
- Public MCP endpoint: `http://127.0.0.1:9000/mcp`.
- Upstream Coplay endpoint: `http://127.0.0.1:8080/mcp`.
- Validation: build the solution in Release. Full Unity package/runtime validation requires a running Unity project selected in the desktop app.
- Current limitation: Coplay's `/api/instances` response lacks project paths, so UI matching uses project name.
