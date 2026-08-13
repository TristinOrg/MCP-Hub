// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    MainWindow.axaml.cs
// ============================================================
// 手动加载 AXAML（绕过 Avalonia 源生成器与 Roslyn 版本不兼容问题）
// ============================================================

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Tristin.MCPManager.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
