// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    EditorInstance.cs
// ============================================================

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Tristin.MCPManager.Core.Models;

/// <summary>
/// 编辑器实例（通用模型，未来可扩展 Figma、Blender 等）
/// </summary>
public class EditorInstance : INotifyPropertyChanged
{
    private EditorState _state;
    private string?     _bridgePort;
    private string?     _errorMessage;

    /// <summary>
    /// 编辑器类型：Unity / Figma / Blender 等
    /// </summary>
    public required string EditorType { get; init; }

    /// <summary>
    /// 进程 ID
    /// </summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// 项目名称
    /// </summary>
    public required string ProjectName { get; init; }

    /// <summary>
    /// 项目路径
    /// </summary>
    public required string ProjectPath { get; init; }

    /// <summary>
    /// 编辑器版本
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// 可执行文件路径
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// 当前连接状态
    /// </summary>
    public EditorState State
    {
        get => _state;
        set => SetField(ref _state, value);
    }

    /// <summary>
    /// Bridge 监听端口（注册后填充）
    /// </summary>
    public string? BridgePort
    {
        get => _bridgePort;
        set => SetField(ref _bridgePort, value);
    }

    /// <summary>
    /// 错误信息（State=Error 时填充）
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    /// <summary>
    /// 显示名称（UI 用）
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
