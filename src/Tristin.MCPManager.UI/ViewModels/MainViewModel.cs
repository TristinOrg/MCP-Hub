using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tristin.MCPManager.Core.Interfaces;
using Tristin.MCPManager.Core.Mcp;
using Tristin.MCPManager.Core.Models;
using Tristin.MCPManager.Unity;

namespace Tristin.MCPManager.UI.ViewModels;

/// <summary>
/// Coordinates Unity discovery, Coplay package injection, and the Hub endpoints.
/// </summary>
public partial class MainViewModel : ViewModelBase, IDisposable
{
    private static readonly Uri CoplayEndpoint = new("http://127.0.0.1:8080/");

    private readonly IEditorDetector      _detector;
    private readonly IBridgeInjector      _injector;
    private readonly CoplayMcpServer      _coplayServer;
    private readonly CoplayMcpClient      _coplayClient;
    private readonly HttpMcpReverseProxy  _mcpProxy;
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty]
    private ObservableCollection<EditorInstance> _editorInstances = [];

    [ObservableProperty]
    private EditorInstance? _selectedEditor;

    [ObservableProperty]
    private string _mcpEndpoint = "http://127.0.0.1:9000/mcp";

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

    public AsyncRelayCommand ScanEditorsCommand { get; }
    public AsyncRelayCommand ConnectCommand     { get; }
    public AsyncRelayCommand DisconnectCommand  { get; }

    public MainViewModel()
    {
        var packagePath = LocateBridgePackage();

        _detector     = new UnityProcessDetector();
        _injector     = new UnityBridgeInjector { BridgePackagePath = packagePath };
        _coplayServer = new CoplayMcpServer(CoplayEndpoint, line => AppendLog($"[Coplay] {line}"));
        _coplayClient = new CoplayMcpClient(CoplayEndpoint);
        _mcpProxy     = new HttpMcpReverseProxy();

        ScanEditorsCommand = new AsyncRelayCommand(ScanEditorsAsync, () => !IsScanning);
        ConnectCommand     = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand  = new AsyncRelayCommand(DisconnectAsync, CanDisconnect);
    }

    public async Task StartAsync()
    {
        try
        {
            AppendLog("[Info] Starting official Coplay MCP server ...");
            await _coplayServer.StartAsync(_cts.Token);
            await _mcpProxy.StartAsync(new Uri("http://127.0.0.1:9000/"), CoplayEndpoint, _cts.Token);
            AppendLog($"[Info] MCP Hub ready at {McpEndpoint}");
        }
        catch (Exception ex)
        {
            AppendLog($"[Error] Startup failed: {ex.Message}");
        }

        _ = _detector.StartWatchAsync(3000, OnEditorListChanged, _cts.Token);
        await ScanEditorsAsync();
    }

    partial void OnSelectedEditorChanged(EditorInstance? value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsConnectingChanged(bool value) => ConnectCommand.NotifyCanExecuteChanged();
    partial void OnIsDisconnectingChanged(bool value) => DisconnectCommand.NotifyCanExecuteChanged();
    partial void OnIsScanningChanged(bool value) => ScanEditorsCommand.NotifyCanExecuteChanged();

    private bool CanConnect()
        => !IsConnecting && SelectedEditor is { State: EditorState.Available or EditorState.Error };

    private bool CanDisconnect()
        => !IsDisconnecting && SelectedEditor is { State: EditorState.Connected or EditorState.Error };

    private async Task ScanEditorsAsync()
    {
        if (IsScanning)
            return;

        try
        {
            IsScanning = true;
            var detected = await _detector.DetectAsync(_cts.Token);
            var existing = EditorInstances.ToDictionary(editor => editor.ProcessId);

            EditorInstances = new ObservableCollection<EditorInstance>(
                detected.Select(editor => existing.GetValueOrDefault(editor.ProcessId) ?? editor));

            if (SelectedEditor == null || !EditorInstances.Contains(SelectedEditor))
                SelectedEditor = EditorInstances.FirstOrDefault();

            await UpdateConnectionStatesAsync();
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
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
            return;

        try
        {
            IsConnecting       = true;
            target.State       = EditorState.Injecting;
            target.ErrorMessage = null;

            var progress = new Progress<(int percent, string message)>(value =>
            {
                InjectProgress = value.percent;
                InjectStatus   = value.message;
                AppendLog($"[Inject] {value.percent}% {value.message}");
            });

            if (!await _injector.InjectAsync(target, progress, _cts.Token))
                throw new InvalidOperationException(target.ErrorMessage ?? "Package injection failed.");

            target.State = EditorState.WaitingForBridge;
            if (!await WaitForCoplayConnectionAsync(target, TimeSpan.FromMinutes(3), _cts.Token))
                throw new TimeoutException("Coplay bridge did not connect within 3 minutes. Check Unity Console and package resolution.");

            target.State   = EditorState.Connected;
            ActiveEditor   = target;
            InjectProgress = 100;
            InjectStatus   = "Connected through Coplay MCP";
            AppendLog($"[OK] {target.ProjectName} connected. Use set_active_instance from each MCP client session when multiple projects are connected.");
        }
        catch (Exception ex)
        {
            target.State        = EditorState.Error;
            target.ErrorMessage = ex.Message;
            InjectStatus        = ex.Message;
            AppendLog($"[Error] Connect failed: {ex.Message}");
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task DisconnectAsync()
    {
        if (SelectedEditor is not { } target)
            return;

        try
        {
            IsDisconnecting = true;
            target.State    = EditorState.Disconnecting;

            if (!await _injector.CleanupAsync(target, _cts.Token))
                throw new InvalidOperationException(target.ErrorMessage ?? "Manifest restore failed.");

            UnityWindowActivator.ActivateUnityWindow(target.ProcessId);
            target.State = EditorState.Available;
            if (ReferenceEquals(ActiveEditor, target))
                ActiveEditor = null;
            AppendLog($"[OK] Restored {target.ProjectName} package manifest.");
        }
        catch (Exception ex)
        {
            target.State        = EditorState.Error;
            target.ErrorMessage = ex.Message;
            AppendLog($"[Error] Disconnect failed: {ex.Message}");
        }
        finally
        {
            IsDisconnecting = false;
        }
    }

    private async Task UpdateConnectionStatesAsync()
    {
        IReadOnlyList<CoplayUnityInstance> connected;
        try { connected = await _coplayClient.ListInstancesAsync(_cts.Token); }
        catch { return; }

        foreach (var editor in EditorInstances)
        {
            if (connected.Any(instance => Matches(editor, instance)))
                editor.State = EditorState.Connected;
            else if (editor.State == EditorState.Connected)
                editor.State = EditorState.Available;
        }
    }

    private async Task<bool> WaitForCoplayConnectionAsync(
        EditorInstance target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var instances = await _coplayClient.ListInstancesAsync(cancellationToken);
                if (instances.Any(instance => Matches(target, instance)))
                    return true;
            }
            catch (HttpRequestException) { }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    private Task OnEditorListChanged(IReadOnlyList<EditorInstance> _)
        => ScanEditorsAsync();

    private static bool Matches(EditorInstance editor, CoplayUnityInstance instance)
        => string.Equals(editor.ProjectName, instance.Project, StringComparison.OrdinalIgnoreCase);

    private static string LocateBridgePackage()
    {
        string[] candidates =
        [
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "unity-bridge-package")),
            Path.Combine(Environment.CurrentDirectory, "unity-bridge-package"),
            Path.Combine(AppContext.BaseDirectory, "unity-bridge-package")
        ];

        return candidates.FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException("Cannot locate unity-bridge-package.");
    }

    private void AppendLog(string line)
    {
        LogText = $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}{LogText}";
        var lines = LogText.Split(Environment.NewLine);
        if (lines.Length > 300)
            LogText = string.Join(Environment.NewLine, lines.Take(300));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _coplayClient.Dispose();
        _ = Task.Run(async () =>
        {
            await _mcpProxy.DisposeAsync();
            await _coplayServer.DisposeAsync();
            _cts.Dispose();
        });
    }
}
