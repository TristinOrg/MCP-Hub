using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Tristin.MCPManager.Core.Models;

/// <summary>
/// Represents a discovered editor instance (Unity, Figma, Blender, etc.).
/// </summary>
public class EditorInstance : INotifyPropertyChanged
{
    private EditorState _state;
    private string?     _bridgePort;
    private string?     _errorMessage;

    /// <summary>
    /// Editor type: Unity / Figma / Blender, etc.
    /// </summary>
    public required string EditorType { get; init; }

    /// <summary>
    /// OS process ID.
    /// </summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// Project display name.
    /// </summary>
    public required string ProjectName { get; init; }

    /// <summary>
    /// Absolute project path on disk.
    /// </summary>
    public required string ProjectPath { get; init; }

    /// <summary>
    /// Editor version string.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Path to the editor executable (if available).
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// Current connection state.
    /// </summary>
    public EditorState State
    {
        get => _state;
        set => SetField(ref _state, value);
    }

    /// <summary>
    /// Bridge endpoint (filled after registration).
    /// </summary>
    public string? BridgePort
    {
        get => _bridgePort;
        set => SetField(ref _bridgePort, value);
    }

    /// <summary>
    /// Error message (populated when State == Error).
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    /// <summary>
    /// Human-readable display string for UI.
    /// </summary>
    public string DisplayName => $"{ProjectName} ({Version}) [PID:{ProcessId}]";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
