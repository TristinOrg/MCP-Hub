namespace Tristin.MCPManager.Core.Interfaces;

/// <summary>
/// Exposes a stable local endpoint that forwards MCP traffic to an upstream server.
/// </summary>
public interface IMcpServerProxy
{
    /// <summary>
    /// Starts forwarding requests from the public endpoint to the upstream endpoint.
    /// </summary>
    Task StartAsync(Uri listenEndpoint, Uri upstreamEndpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the MCP server.
    /// </summary>
    Task StopAsync();
}
