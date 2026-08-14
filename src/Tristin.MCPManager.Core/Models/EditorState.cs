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
    /// The local Coplay package is being prepared and injected.
    /// </summary>
    Connecting,

    /// <summary>
    /// Injection is complete and the Coplay connection is pending.
    /// </summary>
    WaitingForCoplay,

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
