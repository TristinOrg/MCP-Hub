using System.Net;
using System.Net.Sockets;
using System.Text;
using Tristin.MCPManager.Core.Mcp;

namespace Tristin.MCPManager.Unity.Tests;

/// <summary>
/// Verifies Coplay management API response handling.
/// </summary>
public sealed class CoplayMcpClientTests
{
    [Fact]
    public async Task ListInstancesAsync_DeserializesLowercaseInstancesProperty()
    {
        var port = ReservePort();
        var endpoint = new Uri($"http://127.0.0.1:{port}/");
        using HttpListener listener = new();
        listener.Prefixes.Add(endpoint.AbsoluteUri);
        listener.Start();

        var responseTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var payload = Encoding.UTF8.GetBytes(
                "{\"success\":true,\"instances\":[{\"project\":\"ChatRoom.Unity\"}]}");
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload);
            context.Response.Close();
        });

        using CoplayMcpClient client = new(endpoint);
        var instances = await client.ListInstancesAsync();
        await responseTask;

        var instance = Assert.Single(instances);
        Assert.Equal("ChatRoom.Unity", instance.Project);
    }

    private static int ReservePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
