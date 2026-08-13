using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Core.Interfaces;

/// <summary>
/// Dynamically injects and removes a Bridge into an editor project.
/// </summary>
public interface IBridgeInjector
{
    /// <summary>
    /// Editor type this injector handles.
    /// </summary>
    string EditorType { get; }

    /// <summary>
    /// Inject the Bridge into the target editor instance.
    /// </summary>
    /// <param name="instance">Target editor instance.</param>
    /// <param name="progress">Progress callback (0-100, message).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if injection succeeded.</returns>
    Task<bool> InjectAsync(EditorInstance instance, IProgress<(int percent, string message)>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up and restore the editor to its original state.
    /// </summary>
    /// <param name="instance">Target editor instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if cleanup succeeded.</returns>
    Task<bool> CleanupAsync(EditorInstance instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check whether the Bridge is currently injected.
    /// </summary>
    Task<bool> IsInjectedAsync(EditorInstance instance, CancellationToken cancellationToken = default);
}
