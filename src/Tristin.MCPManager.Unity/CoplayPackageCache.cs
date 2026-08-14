using System.IO.Compression;
using System.Text.Json;

namespace Tristin.MCPManager.Unity;

/// <summary>
/// Downloads, integrates, and validates one local Coplay Unity package for the Hub.
/// </summary>
public sealed class CoplayPackageCache : IDisposable
{
    public const string PackageName    = "com.coplaydev.unity-mcp";
    public const string PackageVersion = "10.1.0";

    private const string IntegrationRelativePath = "Editor/HubIntegration/HubAutoConnect.cs";
    private const string IntegrationSource = """
        using MCPForUnity.Editor.Services;
        using UnityEditor;

        namespace MCPForUnity.Editor.HubIntegration
        {
            /// <summary>
            /// Connects this Unity Editor to the MCP Hub-managed Coplay server.
            /// </summary>
            [InitializeOnLoad]
            internal static class HubAutoConnect
            {
                static HubAutoConnect()
                {
                    EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", true);
                    EditorPrefs.SetString("MCPForUnity.HttpTransportScope", "local");
                    EditorPrefs.SetString("MCPForUnity.HttpUrl", "http://127.0.0.1:8080");
                    EditorApplication.delayCall += Connect;
                }

                private static async void Connect()
                {
                    var bridge = new BridgeControlService();
                    if (!bridge.IsRunning)
                        await bridge.StartAsync();
                }
            }
        }
        """;

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly SemaphoreSlim _prepareLock = new(1, 1);
    private readonly string _applicationRoot;

    public CoplayPackageCache(string? cacheRoot = null, string? applicationRoot = null)
    {
        _applicationRoot = applicationRoot ?? AppContext.BaseDirectory;
        CacheRoot = cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tristin.MCPManager",
            "packages");
    }

    public string CacheRoot { get; }

    public string PackagePath => Path.Combine(CacheRoot, PackageName, PackageVersion);

    public string BundledPackagePath => Path.Combine(
        _applicationRoot,
        "packages",
        PackageName,
        PackageVersion);

    /// <summary>
    /// Returns a validated cache path, downloading the pinned release only when necessary.
    /// </summary>
    public async Task<string> PrepareAsync(
        IProgress<(int percent, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _prepareLock.WaitAsync(cancellationToken);
        try
        {
            if (await IsValidAsync(BundledPackagePath, cancellationToken))
            {
                progress?.Report((35, $"Using bundled Coplay package {PackageVersion}"));
                return BundledPackagePath;
            }

            if (await IsValidAsync(PackagePath, cancellationToken))
            {
                progress?.Report((35, $"Using cached Coplay package {PackageVersion}"));
                return PackagePath;
            }

            progress?.Report((15, $"Downloading Coplay package {PackageVersion} once ..."));
            Directory.CreateDirectory(CacheRoot);

            var operationRoot = Path.Combine(CacheRoot, $".prepare-{Guid.NewGuid():N}");
            var archivePath   = Path.Combine(operationRoot, "coplay.zip");
            var extractPath   = Path.Combine(operationRoot, "extract");
            var stagedPath    = Path.Combine(operationRoot, "package");

            Directory.CreateDirectory(operationRoot);
            try
            {
                var archiveUri = new Uri($"https://github.com/CoplayDev/unity-mcp/archive/refs/tags/v{PackageVersion}.zip");
                using var response = await _httpClient.GetAsync(
                    archiveUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var destination = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await source.CopyToAsync(destination, cancellationToken);

                progress?.Report((25, "Extracting cached Coplay package ..."));
                ZipFile.ExtractToDirectory(archivePath, extractPath);

                var packageSource = Directory.EnumerateDirectories(extractPath, "MCPForUnity", SearchOption.AllDirectories)
                    .SingleOrDefault(path => File.Exists(Path.Combine(path, "package.json")))
                    ?? throw new InvalidDataException("Downloaded Coplay archive does not contain MCPForUnity/package.json.");

                Directory.Move(packageSource, stagedPath);
                await InstallIntegrationAsync(stagedPath, cancellationToken);
                if (!await IsValidAsync(stagedPath, cancellationToken))
                    throw new InvalidDataException("Downloaded Coplay package identity or version does not match the pinned release.");

                var packageParent = Path.GetDirectoryName(PackagePath)!;
                Directory.CreateDirectory(packageParent);
                if (Directory.Exists(PackagePath))
                    Directory.Delete(PackagePath, recursive: true);
                Directory.Move(stagedPath, PackagePath);
            }
            finally
            {
                if (Directory.Exists(operationRoot))
                    Directory.Delete(operationRoot, recursive: true);
            }

            progress?.Report((35, $"Cached Coplay package {PackageVersion}"));
            return PackagePath;
        }
        finally
        {
            _prepareLock.Release();
        }
    }

    private static async Task<bool> IsValidAsync(string packagePath, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(packagePath, "package.json");
        if (!File.Exists(manifestPath))
            return false;

        try
        {
            await using var stream = File.OpenRead(manifestPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            return root.TryGetProperty("name", out var name)
                   && root.TryGetProperty("version", out var version)
                   && name.GetString() == PackageName
                   && version.GetString() == PackageVersion
                   && await HasValidIntegrationAsync(packagePath, cancellationToken);
        }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
    }

    private static async Task InstallIntegrationAsync(string packagePath, CancellationToken cancellationToken)
    {
        var integrationPath = Path.Combine(packagePath, IntegrationRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(integrationPath)!);
        await File.WriteAllTextAsync(
            integrationPath,
            IntegrationSource,
            new System.Text.UTF8Encoding(false),
            cancellationToken);
    }

    private static async Task<bool> HasValidIntegrationAsync(string packagePath, CancellationToken cancellationToken)
    {
        var integrationPath = Path.Combine(packagePath, IntegrationRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(integrationPath))
            return false;

        return await File.ReadAllTextAsync(integrationPath, System.Text.Encoding.UTF8, cancellationToken)
               == IntegrationSource;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _prepareLock.Dispose();
    }
}
