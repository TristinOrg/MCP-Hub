using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Core.Interfaces;

/// <summary>
/// Discovers running editor instances (extensible to Unity, Figma, Blender, etc.).
/// </summary>
public interface IEditorDetector
{
    /// <summary>
    /// Editor type this detector handles (e.g. "Unity", "Figma").
    /// </summary>
    string EditorType { get; }

    /// <summary>
    /// Scan for currently running editor instances.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of discovered editor instances.</returns>
    Task<IReadOnlyList<EditorInstance>> DetectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Start a background polling watch.
    /// </summary>
    /// <param name="intervalMs">Polling interval in milliseconds.</param>
    /// <param name="onChanged">Callback invoked when the instance set changes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartWatchAsync(int intervalMs, Func<IReadOnlyList<EditorInstance>, Task> onChanged, CancellationToken cancellationToken = default);
}
