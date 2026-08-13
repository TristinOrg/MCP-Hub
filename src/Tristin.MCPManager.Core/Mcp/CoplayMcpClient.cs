using System.Text.Json;

namespace Tristin.MCPManager.Core.Mcp;

/// <summary>
/// Reads connection state from the Coplay server's local management API.
/// </summary>
public sealed class CoplayMcpClient : IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public CoplayMcpClient(Uri serverEndpoint) => ServerEndpoint = serverEndpoint;

    public Uri ServerEndpoint { get; }

    public async Task<IReadOnlyList<CoplayUnityInstance>> ListInstancesAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(new Uri(ServerEndpoint, "api/instances"), cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<InstancesResponse>(stream, cancellationToken: cancellationToken);
        return payload?.Instances ?? [];
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class InstancesResponse
    {
        public List<CoplayUnityInstance> Instances { get; init; } = [];
    }
}

/// <summary>
/// Describes a Unity Editor session registered with the Coplay server.
/// </summary>
public sealed class CoplayUnityInstance
{
    [System.Text.Json.Serialization.JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("project")]
    public string Project { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("hash")]
    public string Hash { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("unity_version")]
    public string UnityVersion { get; init; } = string.Empty;
}
