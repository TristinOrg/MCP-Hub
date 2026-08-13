// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    EditorState.cs
// ============================================================

namespace Tristin.MCPManager.Core.Models;

/// <summary>
/// 编辑器实例连接状态
/// </summary>
public enum EditorState
{
    /// <summary>
    /// 可用，未连接
    /// </summary>
    Available,

    /// <summary>
    /// 正在注入 Bridge
    /// </summary>
    Injecting,

    /// <summary>
    /// 等待 Bridge 注册
    /// </summary>
    WaitingForBridge,

    /// <summary>
    /// 已连接
    /// </summary>
    Connected,

    /// <summary>
    /// 正在断开连接/清理
    /// </summary>
    Disconnecting,

    /// <summary>
    /// 错误状态
    /// </summary>
    Error
}
