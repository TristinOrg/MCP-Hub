// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    IIpcBridgeHost.cs
// ============================================================

using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Core.Interfaces;

/// <summary>
/// IPC Bridge 主机：负责接收 Bridge 注册、转发 MCP 调用
/// </summary>
public interface IIpcBridgeHost
{
    /// <summary>
    /// 当前已注册的 Bridge 列表
    /// </summary>
    IReadOnlyDictionary<int, BridgeRegistration> RegisteredBridges { get; }

    /// <summary>
    /// Bridge 注册事件
    /// </summary>
    event EventHandler<BridgeRegistration>? BridgeRegistered;

    /// <summary>
    /// Bridge 断开事件
    /// </summary>
    event EventHandler<int>? BridgeDisconnected;

    /// <summary>
    /// 启动 IPC 主机
    /// </summary>
    Task StartAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止 IPC 主机
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 向指定 Bridge 发送 MCP 工具调用请求
    /// </summary>
    /// <param name="targetPid">目标编辑器 PID</param>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">参数（JSON）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具调用结果（JSON 字符串）</returns>
    Task<string> InvokeToolAsync(int targetPid, string toolName, string arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列举指定 Bridge 支持的工具列表
    /// </summary>
    Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(int targetPid, CancellationToken cancellationToken = default);
}
