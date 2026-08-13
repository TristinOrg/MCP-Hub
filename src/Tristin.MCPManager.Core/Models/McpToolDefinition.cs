namespace Tristin.MCPManager.Core.Models;

/// <summary>
/// Simplified MCP tool definition used by the proxy layer for routing.
/// </summary>
public class McpToolDefinition
{
    /// <summary>
    /// Tool name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// JSON Schema for the tool's input parameters.
    /// </summary>
    public object? InputSchema { get; set; }

    /// <summary>
    /// Source editor PID (for routing).
    /// </summary>
    public int SourceEditorPid { get; set; }
}
