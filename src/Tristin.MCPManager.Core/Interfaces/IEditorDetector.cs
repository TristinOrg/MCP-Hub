// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    IEditorDetector.cs
// ============================================================

using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Core.Interfaces;

/// <summary>
/// 编辑器实例发现接口（通用，可扩展多种编辑器类型）
/// </summary>
public interface IEditorDetector
{
    /// <summary>
    /// 支持的编辑器类型（如 "Unity", "Figma"）
    /// </summary>
    string EditorType { get; }

    /// <summary>
    /// 扫描当前运行中的编辑器实例
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>编辑器实例列表</returns>
    Task<IReadOnlyList<EditorInstance>> DetectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动后台定时扫描
    /// </summary>
    /// <param name="intervalMs">扫描间隔（毫秒）</param>
    /// <param name="onChanged">变化回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task StartWatchAsync(int intervalMs, Func<IReadOnlyList<EditorInstance>, Task> onChanged, CancellationToken cancellationToken = default);
}
