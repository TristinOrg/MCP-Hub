namespace Tristin.MCPManager.Core.Models;

/// <summary>
/// Connection lifecycle state for an editor instance.
/// </summary>
public enum EditorState
{
    /// <summary>
    /// Detected but not connected.
    /// </summary>
    Available,

    /// <summary>
    /// Bridge package is being injected.
    /// </summary>
    Injecting,

    /// <summary>
    /// Injection done, waiting for Bridge to register via IPC.
    /// </summary>
    WaitingForBridge,

    /// <summary>
    /// Bridge registered and ready to accept commands.
    /// </summary>
    Connected,

    /// <summary>
    /// Cleanup / restore in progress.
    /// </summary>
    Disconnecting,

    /// <summary>
    /// An error occurred.
    /// </summary>
    Error
}
