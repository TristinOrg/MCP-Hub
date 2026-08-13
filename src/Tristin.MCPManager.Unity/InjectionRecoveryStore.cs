using System.Text.Json;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Persists projects with outstanding package mutations for crash recovery.
/// </summary>
public sealed class InjectionRecoveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public InjectionRecoveryStore(string? statePath = null)
    {
        StatePath = statePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tristin.MCPManager",
            "pending-restores.json");
    }

    public string StatePath { get; }

    public async Task RegisterAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var projects = await ReadAsync(cancellationToken);
            projects.Add(Path.GetFullPath(projectPath));
            await WriteAsync(projects, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task CompleteAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var projects = await ReadAsync(cancellationToken);
            projects.Remove(Path.GetFullPath(projectPath));
            await WriteAsync(projects, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return (await ReadAsync(cancellationToken)).Order().ToArray(); }
        finally { _gate.Release(); }
    }

    private async Task<HashSet<string>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(StatePath))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await using var stream = File.OpenRead(StatePath);
            var paths = await JsonSerializer.DeserializeAsync<string[]>(stream, cancellationToken: cancellationToken) ?? [];
            return new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    private async Task WriteAsync(HashSet<string> projects, CancellationToken cancellationToken)
    {
        if (projects.Count == 0)
        {
            if (File.Exists(StatePath))
                File.Delete(StatePath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        var temporaryPath = StatePath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(projects.Order(), JsonOptions),
            new System.Text.UTF8Encoding(false),
            cancellationToken);
        File.Move(temporaryPath, StatePath, overwrite: true);
    }
}
