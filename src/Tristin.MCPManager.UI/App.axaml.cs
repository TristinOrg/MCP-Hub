using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Tristin.MCPManager.UI.ViewModels;
using Tristin.MCPManager.UI.Views;

namespace Tristin.MCPManager.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };
            desktop.MainWindow.Opened += OnWindowOpened;
            desktop.Exit += (_, _) =>
            {
                try { vm.Dispose(); } catch { /* ignore */ }
            };

            async void OnWindowOpened(object? sender, EventArgs args)
            {
                if (desktop.MainWindow is Window w)
                    w.Opened -= OnWindowOpened;
                try { await vm.StartAsync(); }
                catch (Exception ex)
                {
                    // 启动失败：至少让窗口保留（MainViewModel 的 LogText 在构造时即已创建，无法直接访问）
                    Console.Error.WriteLine("[App] Start failed: " + ex);
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}