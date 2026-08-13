using System.Diagnostics;
using Tristin.MCPManager.Core.Interfaces;

namespace Tristin.MCPManager.Core.Mcp;

/// <summary>
/// Starts and monitors the official Coplay MCP for Unity server through uvx.
/// </summary>
public sealed class CoplayMcpServer : IManagedMcpServer, IAsyncDisposable
{
    public const string PackageVersion = "10.1.0";

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private Process? _process;

    public CoplayMcpServer(Uri endpoint, Action<string>? log = null)
    {
        Endpoint = endpoint;
        Log      = log;
    }

    public Uri             Endpoint { get; }
    public Action<string>? Log      { get; }
    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (await IsHealthyAsync(cancellationToken))
        {
            Log?.Invoke($"Using existing Coplay MCP server at {Endpoint}");
            return;
        }

        if (IsRunning)
            throw new InvalidOperationException("Coplay MCP server is running but its health endpoint is unavailable.");

        ProcessStartInfo startInfo = new()
        {
            FileName               = "uvx",
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };
        startInfo.ArgumentList.Add("--from");
        startInfo.ArgumentList.Add($"mcpforunityserver=={PackageVersion}");
        startInfo.ArgumentList.Add("mcp-for-unity");
        startInfo.ArgumentList.Add("--transport");
        startInfo.ArgumentList.Add("http");
        startInfo.ArgumentList.Add("--http-url");
        startInfo.ArgumentList.Add(Endpoint.AbsoluteUri.TrimEnd('/'));

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data != null) Log?.Invoke(e.Data); };
        _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) Log?.Invoke(e.Data); };

        try
        {
            if (!_process.Start())
                throw new InvalidOperationException("Failed to start uvx.");
        }
        catch (Exception ex)
        {
            _process.Dispose();
            _process = null;
            throw new InvalidOperationException(
                "Unable to start the Coplay server. Install uv from https://docs.astral.sh/uv/ and ensure uvx is on PATH.", ex);
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process.HasExited)
                throw new InvalidOperationException($"Coplay MCP server exited with code {_process.ExitCode}.");
            if (await IsHealthyAsync(cancellationToken))
                return;
            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("Coplay MCP server did not become healthy within 90 seconds.");
    }

    public Task StopAsync()
    {
        if (_process == null)
            return Task.CompletedTask;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }

        return Task.CompletedTask;
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(new Uri(Endpoint, "health"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _httpClient.Dispose();
    }
}
