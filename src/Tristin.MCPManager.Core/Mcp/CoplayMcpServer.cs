using System.Diagnostics;
using System.Text.Json;

namespace Tristin.MCPManager.Core.Mcp;

/// <summary>
/// Starts and monitors the official Coplay MCP for Unity server through uvx.
/// </summary>
public sealed class CoplayMcpServer : IAsyncDisposable
{
    public const string PackageVersion = "10.1.0";

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly Uri _endpoint;
    private readonly Action<string>? _log;
    private Process? _process;

    public CoplayMcpServer(Uri endpoint, Action<string>? log = null)
    {
        _endpoint = endpoint;
        _log      = log;
    }

    private bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var runningVersion = await GetHealthyVersionAsync(cancellationToken);
        if (runningVersion == PackageVersion)
        {
            _log?.Invoke($"Using existing Coplay MCP server at {_endpoint}");
            return;
        }
        if (runningVersion != null)
            throw new InvalidOperationException(
                $"Coplay MCP server {_endpoint} is version {runningVersion}; Hub requires {PackageVersion}. Stop the existing server and retry.");

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
        startInfo.ArgumentList.Add(_endpoint.AbsoluteUri.TrimEnd('/'));

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data != null) _log?.Invoke(e.Data); };
        _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) _log?.Invoke(e.Data); };

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
            if (await GetHealthyVersionAsync(cancellationToken) == PackageVersion)
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

    private async Task<string?> GetHealthyVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(new Uri(_endpoint, "health"), cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("version", out var version)
                ? version.GetString()
                : null;
        }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _httpClient.Dispose();
    }
}
