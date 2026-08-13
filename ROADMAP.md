# Roadmap

## Implemented

- Detect multiple running Unity Editor processes.
- Temporarily inject and cleanly restore a UPM package dependency.
- Load the official Coplay MCP for Unity package instead of duplicating Unity commands.
- Start a version-matched official Coplay MCP server through `uvx`.
- Expose a stable Hub MCP endpoint through a transparent HTTP reverse proxy.
- Display Coplay connection state for discovered Unity projects.
- Delegate tool discovery, tool execution, and MCP-session instance selection to Coplay.

## Next

- Persist and recover all outstanding manifest restorations after an unclean Hub shutdown.
- Make public and upstream ports configurable from the UI and command line.
- Match Coplay sessions by canonical project path when the upstream API exposes it, avoiding ambiguity between projects with the same folder name.
- Add automated proxy protocol tests and a Unity package compilation fixture.
- Package the desktop app and bootstrap package for release.
