// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    IMcpServerProxy.cs
// ============================================================

using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Core.Interfaces;

/// <summary>
/// MCP Server Proxy：作为 Codex 的唯一 MCP Endpoint，负责将请求路由到活动的 Bridge
/// </summary>
public interface IMcpServerProxy
{
    /// <summary>
    /// 当前活动的编辑器实例 PID（所有 MCP 调用都会路由到它）
    /// </summary>
    EditorInstance? ActiveEditor { get; set; }

    /// <summary>
    /// 活动编辑器变更事件
    /// </summary>
    event EventHandler<EditorInstance?>? ActiveEditorChanged;

    /// <summary>
    /// 启动 MCP Server（HTTP / SSE / stdio）
    /// </summary>
    Task StartAsync(string listenEndpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止 MCP Server
    /// </summary>
    Task StopAsync();
}
