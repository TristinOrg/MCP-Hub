using System.Net;
namespace Tristin.MCPManager.Core.Mcp;

/// <summary>
/// Transparently forwards MCP HTTP traffic to the official Coplay server.
/// </summary>
public sealed class HttpMcpReverseProxy : IAsyncDisposable
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host"
    };

    private readonly HttpClient _client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies         = false
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private HttpListener?            _listener;
    private CancellationTokenSource? _cts;
    private Task?                    _runLoop;
    private Uri?                     _upstreamEndpoint;

    public Task StartAsync(Uri listenEndpoint, Uri upstreamEndpoint, CancellationToken cancellationToken = default)
    {
        if (_listener != null)
            throw new InvalidOperationException("The MCP proxy is already running.");

        var prefix        = EnsureTrailingSlash(listenEndpoint).AbsoluteUri;
        _upstreamEndpoint = EnsureTrailingSlash(upstreamEndpoint);
        _cts              = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener         = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();
        _runLoop = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts == null)
            return;

        _cts.Cancel();
        _listener?.Close();

        if (_runLoop != null)
        {
            try { await _runLoop; }
            catch (OperationCanceledException) { }
            catch (HttpListenerException) when (_cts.IsCancellationRequested) { }
        }

        _listener = null;
        _runLoop  = null;
        _cts.Dispose();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener?.IsListening == true)
        {
            HttpListenerContext context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            _ = HandleAsync(context, cancellationToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage upstreamRequest = new(
                new HttpMethod(context.Request.HttpMethod),
                BuildUpstreamUri(context.Request.Url));

            if (context.Request.HasEntityBody)
                upstreamRequest.Content = new StreamContent(context.Request.InputStream);

            CopyRequestHeaders(context.Request, upstreamRequest);

            using var upstreamResponse = await _client.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            context.Response.StatusCode = (int)upstreamResponse.StatusCode;
            if (upstreamResponse.ReasonPhrase != null)
                context.Response.StatusDescription = upstreamResponse.ReasonPhrase;
            CopyResponseHeaders(upstreamResponse, context.Response);

            await using var responseStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
            await responseStream.CopyToAsync(context.Response.OutputStream, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (context.Response.OutputStream.CanWrite)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                await context.Response.OutputStream.WriteAsync(
                    System.Text.Encoding.UTF8.GetBytes($"Upstream MCP server unavailable: {ex.Message}"),
                    CancellationToken.None);
            }
        }
        finally
        {
            context.Response.Close();
        }
    }

    private Uri BuildUpstreamUri(Uri? requestUri)
    {
        if (_upstreamEndpoint == null)
            throw new InvalidOperationException("The upstream endpoint is not configured.");

        var relative = requestUri?.PathAndQuery.TrimStart('/') ?? string.Empty;
        return new Uri(_upstreamEndpoint, relative);
    }

    private static void CopyRequestHeaders(HttpListenerRequest source, HttpRequestMessage destination)
    {
        foreach (var name in source.Headers.AllKeys)
        {
            if (name == null || HopByHopHeaders.Contains(name))
                continue;

            var values = source.Headers.GetValues(name);
            if (values == null)
                continue;

            if (!destination.Headers.TryAddWithoutValidation(name, values))
                destination.Content?.Headers.TryAddWithoutValidation(name, values);
        }
    }

    private static void CopyResponseHeaders(HttpResponseMessage source, HttpListenerResponse destination)
    {
        foreach (var header in source.Headers.Concat(source.Content.Headers))
        {
            if (HopByHopHeaders.Contains(header.Key))
                continue;

            try { destination.Headers[header.Key] = string.Join(",", header.Value); }
            catch (ArgumentException) { }
        }
    }

    private static Uri EnsureTrailingSlash(Uri uri)
        => uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _client.Dispose();
    }
}
