namespace Tristin.MCPManager.Core.Interfaces;

/// <summary>
/// Controls the lifecycle of the upstream MCP server used by the hub.
/// </summary>
public interface IManagedMcpServer
{
    /// <summary>
    /// Gets whether the managed server process is running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the server and waits until its health endpoint is ready.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops only the process started by this instance.
    /// </summary>
    Task StopAsync();
}
