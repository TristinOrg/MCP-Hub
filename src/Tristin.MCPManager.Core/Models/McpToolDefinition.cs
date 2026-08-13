// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    McpToolDefinition.cs
// ============================================================

namespace Tristin.MCPManager.Core.Models;

/// <summary>
/// MCP 工具定义（简化版，用于 Proxy 层转发）
/// </summary>
public class McpToolDefinition
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// 工具描述
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// JSON Schema 参数
    /// </summary>
    public object? InputSchema { get; set; }

    /// <summary>
    /// 来源编辑器实例 PID（路由用）
    /// </summary>
    public int SourceEditorPid { get; set; }
}
