namespace Tristin.MCPManager.Core.Models;

/// <summary>
/// Registration message sent by a Bridge to the Runtime Manager.
/// </summary>
public class BridgeRegistration
{
    /// <summary>
    /// Editor type (Unity, Figma, etc.).
    /// </summary>
    public required string EditorType { get; set; }

    /// <summary>
    /// Project display name.
    /// </summary>
    public required string ProjectName { get; set; }

    /// <summary>
    /// Absolute project path.
    /// </summary>
    public required string ProjectPath { get; set; }

    /// <summary>
    /// Editor process ID.
    /// </summary>
    public required int Pid { get; set; }

    /// <summary>
    /// Bridge IPC endpoint (WebSocket URL or Named Pipe name).
    /// </summary>
    public required string Endpoint { get; set; }

    /// <summary>
    /// List of supported tool names.
    /// </summary>
    public string[]? SupportedTools { get; set; }

    /// <summary>
    /// Heartbeat interval in milliseconds.
    /// </summary>
    public int HeartbeatIntervalMs { get; set; } = 5000;
}
