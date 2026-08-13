// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    BridgeRegistration.cs
// ============================================================

namespace Tristin.MCPManager.Core.Models;

/// <summary>
/// Bridge 向 Runtime Manager 注册的消息体
/// </summary>
public class BridgeRegistration
{
    /// <summary>
    /// 编辑器类型
    /// </summary>
    public required string EditorType { get; set; }

    /// <summary>
    /// 项目名称
    /// </summary>
    public required string ProjectName { get; set; }

    /// <summary>
    /// 项目路径
    /// </summary>
    public required string ProjectPath { get; set; }

    /// <summary>
    /// 编辑器进程 ID
    /// </summary>
    public required int Pid { get; set; }

    /// <summary>
    /// Bridge IPC 监听端口（WebSocket / Named Pipe 名称）
    /// </summary>
    public required string Endpoint { get; set; }

    /// <summary>
    /// 支持的工具名称列表
    /// </summary>
    public string[]? SupportedTools { get; set; }

    /// <summary>
    /// 心跳间隔（毫秒）
    /// </summary>
    public int HeartbeatIntervalMs { get; set; } = 5000;
}
