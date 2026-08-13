// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    IBridgeInjector.cs
// ============================================================

using Tristin.MCPManager.Core.Models;

namespace Tristin.MCPManager.Core.Interfaces;

/// <summary>
/// Bridge 动态注入接口（负责修改编辑器项目配置以加载 Bridge）
/// </summary>
public interface IBridgeInjector
{
    /// <summary>
    /// 支持的编辑器类型
    /// </summary>
    string EditorType { get; }

    /// <summary>
    /// 向指定编辑器实例注入 Bridge
    /// </summary>
    /// <param name="instance">目标编辑器实例</param>
    /// <param name="progress">进度回调（0-100，描述）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>注入是否成功</returns>
    Task<bool> InjectAsync(EditorInstance instance, IProgress<(int percent, string message)>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理并恢复编辑器原始状态
    /// </summary>
    /// <param name="instance">目标编辑器实例</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理是否成功</returns>
    Task<bool> CleanupAsync(EditorInstance instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查目标实例是否已注入 Bridge
    /// </summary>
    Task<bool> IsInjectedAsync(EditorInstance instance, CancellationToken cancellationToken = default);
}
