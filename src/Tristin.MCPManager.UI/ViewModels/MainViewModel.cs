// ============================================================
// Author:  Tristin Wen
// Email:   Tristin_Wen@outlook.com
// File:    MainViewModel.cs
// ============================================================
// MVVM 主视图模型：编排 UI 与所有核心模块
// ============================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tristin.MCPManager.Core.Interfaces;
using Tristin.MCPManager.Core.Ipc;
using Tristin.MCPManager.Core.Mcp;
using Tristin.MCPManager.Core.Models;
using Tristin.MCPManager.Unity;

namespace Tristin.MCPManager.UI.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    // ========== 注入的核心服务 ==========
    private readonly IEditorDetector        _detector;
    private readonly IBridgeInjector        _injector;
    private readonly NamedPipeIpcBridgeHost _ipcHost;
    private readonly SimpleHttpMcpServerProxy _mcpProxy;
    private readonly string                 _bridgePackagePath;
    private CancellationTokenSource?        _cts;

    // ========== UI 可观察属性 ==========

    [ObservableProperty]
    private ObservableCollection<EditorInstance> _editorInstances = new();

    [ObservableProperty]
    private EditorInstance? _selectedEditor;

    [ObservableProperty]
    private string _mcpEndpoint = "http://localhost:9000/";

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private int _injectProgress;

    [ObservableProperty]
    private string _injectStatus = string.Empty;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isDisconnecting;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private EditorInstance? _activeEditor;

    // ========== 命令（显式实现，避免 MVVMTK0007 源生成器版本冲突） ==========
    public ICommand ScanEditorsCommand  { get; }
    public ICommand ConnectCommand      { get; }
    public ICommand DisconnectCommand   { get; }

    partial void OnActiveEditorChanged(EditorInstance? value)
    {
        _mcpProxy.ActiveEditor = value;
    }

    // ========== 构造与初始化 ==========

    public MainViewModel()
    {
        // 定位 Unity Bridge Package 路径（发布模式：与 UI 同级的 unity-bridge-package 目录）
        // 优先用 [AppDomain.BaseDir]/../../../../unity-bridge-package，其次用 当前Dir/unity-bridge-package
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "unity-bridge-package")),
            Path.Combine(Environment.CurrentDirectory, "unity-bridge-package"),
            Path.Combine(AppContext.BaseDirectory, "unity-bridge-package")
        };
        _bridgePackagePath = candidates.FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException(
                "Can not locate unity-bridge-package. Tried:\n" + string.Join("\n", candidates));

        _detector = new UnityProcessDetector();
        _injector = new UnityBridgeInjector { BridgePackagePath = _bridgePackagePath };
        _ipcHost  = new NamedPipeIpcBridgeHost();
        _mcpProxy = new SimpleHttpMcpServerProxy(_ipcHost);

        _mcpProxy.ActiveEditorChanged += (_, e) => ActiveEditor = e;
        _ipcHost.BridgeRegistered     += OnBridgeRegistered;
        _ipcHost.BridgeDisconnected   += OnBridgeDisconnected;

        // 显式初始化命令（避免不同版本 MVVMTK 源生成器兼容性问题）
        ScanEditorsCommand = new AsyncRelayCommand(ScanEditorsAsync, () => !IsScanning);
        ConnectCommand     = new AsyncRelayCommand(ConnectAsync,     () => !IsConnecting && SelectedEditor != null);
        DisconnectCommand  = new AsyncRelayCommand(DisconnectAsync,  () => !IsDisconnecting && SelectedEditor != null);
    }

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();

        AppendLog($"[Info] Bridge package located: {_bridgePackagePath}");

        // 启动 IPC Host
        await _ipcHost.StartAsync(NamedPipeIpcBridgeHost.DefaultPipeName, _cts.Token);
        AppendLog($"[Info] IPC Host listening on NamedPipe '{NamedPipeIpcBridgeHost.DefaultPipeName}'");

        // 启动 MCP Proxy
        try
        {
            await _mcpProxy.StartAsync(McpEndpoint, _cts.Token);
            AppendLog($"[Info] MCP Proxy listening on {McpEndpoint}");
        }
        catch (Exception ex)
        {
            AppendLog($"[Warn] MCP Proxy start failed: {ex.Message} (port in use? check firewall)");
        }

        // 启动后台扫描
        _ = _detector.StartWatchAsync(3000, OnEditorListChanged, _cts.Token);
        _ = ScanEditorsAsync(); // 立即扫一次
    }

    // ========== 命令 ==========

    private async Task ScanEditorsAsync()
    {
        if (IsScanning) return;
        try
        {
            IsScanning = true;
            var before = EditorInstances.Count;
            var list   = await _detector.DetectAsync(_cts?.Token ?? default);

            // 增量更新：保留已有对象的状态引用（如果 PID 相同）
            var existingByPid = EditorInstances.ToDictionary(e => e.ProcessId);
            var merged        = new List<EditorInstance>(list.Count);

            foreach (var inst in list)
            {
                if (existingByPid.TryGetValue(inst.ProcessId, out var oldOne))
                {
                    // 仅刷新不敏感字段，保留 State / ErrorMessage
                    merged.Add(oldOne);
                }
                else
                {
                    merged.Add(inst);
                }
            }

            EditorInstances = new ObservableCollection<EditorInstance>(merged);
            if (SelectedEditor == null || !EditorInstances.Contains(SelectedEditor))
                SelectedEditor = EditorInstances.FirstOrDefault();

            AppendLog($"[Scan] Found {EditorInstances.Count} Unity Editor(s) ({(EditorInstances.Count - before)} delta)");
        }
        catch (Exception ex)
        {
            AppendLog($"[Error] Scan failed: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task ConnectAsync()
    {
        if (SelectedEditor is not { } target)
        {
            AppendLog("[Warn] No Unity Editor selected.");
            return;
        }
        if (IsConnecting) return;
        try
        {
            IsConnecting   = true;
            InjectStatus   = "Starting injection ...";
            InjectProgress = 0;

            target.State        = EditorState.Injecting;
            target.ErrorMessage = null;

            var progress = new Progress<(int p, string m)>(t =>
            {
                InjectProgress = t.p;
                InjectStatus   = t.m;
                AppendLog($"[Inject {target.ProjectName}] {t.p}% - {t.m}");
            });

            var ok = await _injector.InjectAsync(target, progress, _cts?.Token ?? default);
            if (!ok)
            {
                target.State = EditorState.Error;
                AppendLog($"[Error] Inject failed for PID={target.ProcessId}: {target.ErrorMessage}");
                return;
            }

            target.State = EditorState.WaitingForBridge;
            InjectStatus = "Bridge package injected. Waiting Unity to load Bridge and register via IPC ...";
            AppendLog($"[Inject] Done for {target.ProjectName}. Waiting Bridge registration ...");

            // 等待最多 60s Bridge 注册
            for (int i = 0; i < 60; i++)
            {
                if (_ipcHost.RegisteredBridges.ContainsKey(target.ProcessId))
                    break;
                await Task.Delay(1000);
            }

            if (_ipcHost.RegisteredBridges.ContainsKey(target.ProcessId))
            {
                target.State    = EditorState.Connected;
                ActiveEditor    = target;
                InjectStatus    = "Connected ✓";
                InjectProgress  = 100;
                AppendLog($"[OK] {target.ProjectName} connected. All MCP calls will route to it.");
            }
            else
            {
                target.State        = EditorState.Error;
                target.ErrorMessage = "Bridge did not register within 60s. Unity may still be compiling, or project path detection is wrong.";
                InjectStatus        = "Bridge not registered.";
                AppendLog($"[Warn] Bridge for PID={target.ProcessId} not registered in time.");
            }
        }
        catch (Exception ex)
        {
            if (SelectedEditor != null)
            {
                SelectedEditor.State = EditorState.Error;
                SelectedEditor.ErrorMessage = ex.Message;
            }
            AppendLog($"[Error] Connect failed: {ex}");
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task DisconnectAsync()
    {
        if (SelectedEditor is not { } target)
        {
            AppendLog("[Warn] No Unity Editor selected.");
            return;
        }
        if (IsDisconnecting) return;
        try
        {
            IsDisconnecting = true;
            target.State    = EditorState.Disconnecting;
            AppendLog($"[Cleanup] Restoring manifest for {target.ProjectName} ...");

            var ok = await _injector.CleanupAsync(target, _cts?.Token ?? default);
            if (ok)
            {
                target.State = EditorState.Available;
                if (ReferenceEquals(ActiveEditor, target))
                    ActiveEditor = null;
                AppendLog($"[Cleanup] {target.ProjectName} restored. Unity will reload domain in a moment.");
            }
            else
            {
                target.State = EditorState.Error;
                AppendLog($"[Error] Cleanup failed: {target.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[Error] Disconnect failed: {ex.Message}");
        }
        finally
        {
            IsDisconnecting = false;
        }
    }

    // ========== 事件回调 ==========

    private Task OnEditorListChanged(IReadOnlyList<EditorInstance> newList)
    {
        _ = ScanEditorsAsync();
        return Task.CompletedTask;
    }

    private void OnBridgeRegistered(object? sender, BridgeRegistration reg)
    {
        AppendLog($"[Bridge] Registered: {reg.ProjectName} PID={reg.Pid} endpoint={reg.Endpoint}");
        var match = EditorInstances.FirstOrDefault(e => e.ProcessId == reg.Pid);
        if (match != null && match.State != EditorState.Connected)
        {
            match.State    = EditorState.Connected;
            match.BridgePort = reg.Endpoint;
            if (ActiveEditor == null)
            {
                ActiveEditor = match;
                AppendLog($"[Auto] Set Active Editor to {match.ProjectName}");
            }
        }
    }

    private void OnBridgeDisconnected(object? sender, int pid)
    {
        AppendLog($"[Bridge] Disconnected PID={pid}");
        var match = EditorInstances.FirstOrDefault(e => e.ProcessId == pid);
        if (match != null)
        {
            match.State = EditorState.Available;
            if (ReferenceEquals(ActiveEditor, match))
                ActiveEditor = null;
        }
    }

    // ========== 日志帮助 ==========

    private void AppendLog(string line)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        LogText = $"[{ts}] {line}{Environment.NewLine}{LogText}";
        // 保留最多 300 行
        var nl = 0;
        var cut = 0;
        for (int i = 0; i < LogText.Length; i++)
        {
            if (LogText[i] == '\n')
            {
                nl++;
                if (nl >= 300) { cut = i; break; }
            }
        }
        if (cut > 0) LogText = LogText.Substring(0, cut);
    }

    // ========== Dispose ==========

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _ = Task.Run(async () =>
        {
            try { await _ipcHost.StopAsync(); } catch { /* ignore */ }
            try { await _mcpProxy.StopAsync(); } catch { /* ignore */ }
            try { await _ipcHost.DisposeAsync(); } catch { /* ignore */ }
            try { await _mcpProxy.DisposeAsync(); } catch { /* ignore */ }
        });
    }
}
