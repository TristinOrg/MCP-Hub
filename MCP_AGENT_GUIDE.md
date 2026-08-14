# AI Agent Guide

This document explains how any AI agent or automation client can control one or more Unity Editors through Unity MCP Hub.

## Endpoint and lifecycle

- Public MCP endpoint: `http://127.0.0.1:9000/mcp`
- Transport: MCP Streamable HTTP using JSON-RPC 2.0
- Authentication: none; the endpoint is intentionally bound to the local machine
- Requirement: start Unity MCP Hub and connect each target Unity Editor before invoking Unity tools

Do not configure the upstream Coplay endpoint at `127.0.0.1:8080`. Clients should use the stable Hub endpoint at port `9000`.

## Preferred: configure an MCP client

Clients that use JSON MCP configuration can add:

```json
{
  "mcpServers": {
    "unity_hub": {
      "url": "http://127.0.0.1:9000/mcp"
    }
  }
}
```

Codex clients can add this to `~/.codex/config.toml`:

```toml
[mcp_servers.unity_hub]
url = "http://127.0.0.1:9000/mcp"
enabled = true
required = false
startup_timeout_sec = 30
tool_timeout_sec = 300
```

Restart the MCP client after changing persistent configuration. It should discover Coplay's Unity tools automatically.

## Multi-Unity routing

Coplay owns instance discovery and routing. The Hub transparently forwards the MCP session and does not duplicate Unity tools.

1. Read the MCP resource `mcpforunity://instances` to discover connected Unity Editors.
2. If more than one instance is connected, call `set_active_instance` with the exact `Name@hash` value.
3. Invoke Unity tools such as `manage_asset`, `manage_gameobject`, or `manage_prefabs`.

Example tool call arguments:

```json
{
  "name": "set_active_instance",
  "arguments": {
    "instance": "MyUnityProject@6fa1436890f43744"
  }
}
```

The active Unity selection belongs to the MCP client session. Every agent should initialize its own MCP session and select its own target. Do not share an `mcp-session-id` between independent agents.

## Manual JSON-RPC over HTTP

An agent without native MCP integration can call the endpoint with any HTTP client. The required sequence is:

```text
initialize
notifications/initialized
resources/read or tools/list
tools/call set_active_instance (when multiple Unity Editors are connected)
tools/call <Unity tool>
```

Every request after `initialize` must send the returned `mcp-session-id` header. Clients must also accept both JSON and Server-Sent Events responses.

### Minimal PowerShell example

```powershell
$endpoint = "http://127.0.0.1:9000/mcp"
$accept   = "application/json, text/event-stream"

$initializeBody = @{
    jsonrpc = "2.0"
    id      = 1
    method  = "initialize"
    params  = @{
        protocolVersion = "2025-06-18"
        capabilities    = @{}
        clientInfo      = @{
            name    = "manual-unity-agent"
            version = "1.0.0"
        }
    }
} | ConvertTo-Json -Depth 10

$initialize = Invoke-WebRequest `
    -UseBasicParsing `
    -Uri $endpoint `
    -Method Post `
    -ContentType "application/json" `
    -Headers @{ Accept = $accept } `
    -Body $initializeBody

$sessionId = $initialize.Headers["mcp-session-id"]
if (!$sessionId)
{
    throw "The MCP server did not return mcp-session-id."
}

$headers = @{
    Accept            = $accept
    "mcp-session-id" = $sessionId
}

$initializedBody = @{
    jsonrpc = "2.0"
    method  = "notifications/initialized"
} | ConvertTo-Json -Depth 5

Invoke-WebRequest `
    -UseBasicParsing `
    -Uri $endpoint `
    -Method Post `
    -ContentType "application/json" `
    -Headers $headers `
    -Body $initializedBody | Out-Null

$toolsListBody = @{
    jsonrpc = "2.0"
    id      = 2
    method  = "tools/list"
    params  = @{}
} | ConvertTo-Json -Depth 5

$tools = Invoke-WebRequest `
    -UseBasicParsing `
    -Uri $endpoint `
    -Method Post `
    -ContentType "application/json" `
    -Headers $headers `
    -Body $toolsListBody

$tools.Content
```

Windows PowerShell 5.1 can report an `underlying connection was closed` receive error for the empty successful response to `notifications/initialized`. If that occurs, continue with `tools/list`; treat the session as failed only if the subsequent request fails. PowerShell 7 does not normally exhibit this legacy `Invoke-WebRequest` behavior.

To invoke a tool, send:

```powershell
$callBody = @{
    jsonrpc = "2.0"
    id      = 3
    method  = "tools/call"
    params  = @{
        name      = "manage_asset"
        arguments = @{
            action           = "get_info"
            path             = "Assets"
            generate_preview = $false
        }
    }
} | ConvertTo-Json -Depth 10

$result = Invoke-WebRequest `
    -UseBasicParsing `
    -Uri $endpoint `
    -Method Post `
    -ContentType "application/json" `
    -Headers $headers `
    -Body $callBody

$result.Content
```

If the response content type is `text/event-stream`, parse the JSON value from the SSE `data:` line instead of treating the complete response body as JSON.

## Operational rules for agents

- Prefer native MCP configuration for routine work; use manual HTTP primarily for diagnostics and integration tests.
- Cache tool schemas within one session instead of repeatedly calling `tools/list`.
- Always inspect `mcpforunity://instances` and select the intended instance before mutating a project when multiple Editors are connected.
- Use exact asset paths beginning with `Assets/`.
- Check tool errors and the Unity Console after mutations.
- Avoid concurrent writes to the same Unity instance unless the workflow explicitly coordinates them.
- Reinitialize if the Hub restarts or returns an invalid/expired session error.
- Never expose port `9000` to a network without adding authentication, authorization, and transport security.

## Architecture

```text
AI agent or MCP client
        |
        | Streamable HTTP / JSON-RPC 2.0
        v
http://127.0.0.1:9000/mcp
        |
        v
Unity MCP Hub transparent proxy
        |
        v
Coplay MCP Server
        |
        +-- Unity Editor A
        +-- Unity Editor B
        +-- Unity Editor C
```
