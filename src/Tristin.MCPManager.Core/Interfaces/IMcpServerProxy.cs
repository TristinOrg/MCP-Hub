using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Core.Interfaces;

/// <summary>
/// Single MCP endpoint that routes tool calls to the active Bridge.
/// </summary>
public interface IMcpServerProxy
{
    /// <summary>
    /// The currently active editor. All MCP calls route to it.
    /// </summary>
    EditorInstance? ActiveEditor { get; set; }

    /// <summary>
    /// Raised when the active editor changes.
    /// </summary>
    event EventHandler<EditorInstance?>? ActiveEditorChanged;

    /// <summary>
    /// Start the MCP server (HTTP / SSE / stdio).
    /// </summary>
    Task StartAsync(string listenEndpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the MCP server.
    /// </summary>
    Task StopAsync();
}
