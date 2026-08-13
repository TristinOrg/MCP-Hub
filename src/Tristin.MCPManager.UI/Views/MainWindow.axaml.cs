using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Tristin.MCPManager.UI.Views;

/// <summary>
/// Main application window.
/// Uses manual AXAML loading to avoid Avalonia source generator version conflicts.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
