using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Core.Interfaces;

/// <summary>
/// IPC host that accepts Bridge registrations and routes MCP tool calls.
/// </summary>
public interface IIpcBridgeHost
{
    /// <summary>
    /// Currently registered bridges keyed by editor PID.
    /// </summary>
    IReadOnlyDictionary<int, BridgeRegistration> RegisteredBridges { get; }

    /// <summary>
    /// Raised when a Bridge registers.
    /// </summary>
    event EventHandler<BridgeRegistration>? BridgeRegistered;

    /// <summary>
    /// Raised when a Bridge disconnects.
    /// </summary>
    event EventHandler<int>? BridgeDisconnected;

    /// <summary>
    /// Start the IPC host.
    /// </summary>
    Task StartAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the IPC host.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Send a tool call to a specific Bridge.
    /// </summary>
    /// <param name="targetPid">Target editor PID.</param>
    /// <param name="toolName">Tool name.</param>
    /// <param name="arguments">Arguments as JSON string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result as JSON string.</returns>
    Task<string> InvokeToolAsync(int targetPid, string toolName, string arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// List tools supported by a specific Bridge.
    /// </summary>
    Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(int targetPid, CancellationToken cancellationToken = default);
}
