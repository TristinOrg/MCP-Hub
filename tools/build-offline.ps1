param(
    [string] $Version = "0.1.0",
    [string] $RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot  = Join-Path $repositoryRoot "artifacts"
$packageName    = "UnityMCPHub-$Version-$RuntimeIdentifier"
$stagingRoot    = Join-Path $artifactsRoot ".offline-staging"
$publishRoot    = Join-Path $stagingRoot $packageName
$archivePath    = Join-Path $artifactsRoot "$packageName.zip"
$checksumPath   = "$archivePath.sha256"
$coplayVersion  = "10.1.0"
$coplayPackage  = Join-Path $env:LOCALAPPDATA "Tristin.MCPManager\packages\com.coplaydev.unity-mcp\$coplayVersion"

if (!(Test-Path -LiteralPath (Join-Path $coplayPackage "package.json")))
{
    throw "Coplay Unity package $coplayVersion is not cached. Connect one Unity project through the Hub, then rebuild."
}

$resolvedArtifacts = [IO.Path]::GetFullPath($artifactsRoot)
foreach ($path in @($stagingRoot, $archivePath, $checksumPath))
{
    $resolvedPath = [IO.Path]::GetFullPath($path)
    if (!$resolvedPath.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to clean a path outside the artifacts directory: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath)
    {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

dotnet publish (Join-Path $repositoryRoot "src\Tristin.MCPManager.UI\Tristin.MCPManager.UI.csproj") `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishRoot `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$bundledPackage = Join-Path $publishRoot "packages\com.coplaydev.unity-mcp\$coplayVersion"
New-Item -ItemType Directory -Path (Split-Path -Parent $bundledPackage) -Force | Out-Null
Copy-Item -LiteralPath $coplayPackage -Destination $bundledPackage -Recurse

$serverSitePackages = (& uvx --from "mcpforunityserver==$coplayVersion" python -c "import sysconfig; print(sysconfig.get_paths()['purelib'])" | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $serverSitePackages))
{
    throw "Unable to resolve the Coplay Server environment through uvx."
}

$serverPython = Join-Path (Split-Path -Parent $serverSitePackages) "..\Scripts\python.exe"
$pythonHome   = (& $serverPython -c "import sys; print(sys.base_prefix)" | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath (Join-Path $pythonHome "python.exe")))
{
    throw "Unable to resolve the Python runtime used by Coplay Server."
}

$bundledPython = Join-Path $publishRoot "runtime\coplay\python"
New-Item -ItemType Directory -Path $bundledPython -Force | Out-Null
Copy-Item -Path (Join-Path $pythonHome "*") -Destination $bundledPython -Recurse -Force

# Development-only CPython content is not required to run the bundled server.
foreach ($directory in @("Doc", "include", "libs", "Scripts", "share"))
{
    $developmentPath = Join-Path $bundledPython $directory
    if (Test-Path -LiteralPath $developmentPath)
    {
        Remove-Item -LiteralPath $developmentPath -Recurse -Force
    }
}

$bundledSitePackages = Join-Path $bundledPython "Lib\site-packages"
if (Test-Path -LiteralPath $bundledSitePackages)
{
    Remove-Item -LiteralPath $bundledSitePackages -Recurse -Force
}
New-Item -ItemType Directory -Path $bundledSitePackages -Force | Out-Null
Copy-Item -Path (Join-Path $serverSitePackages "*") -Destination $bundledSitePackages -Recurse -Force


# Package tests and bytecode caches add substantial size but are never imported at runtime.
Get-ChildItem -LiteralPath $bundledSitePackages -Recurse -Directory -Force |
    Where-Object { $_.Name -in @("__pycache__", "tests") } |
    Sort-Object { $_.FullName.Length } -Descending |
    Remove-Item -Recurse -Force

Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination (Join-Path $publishRoot "LICENSE.txt")
Copy-Item -LiteralPath (Join-Path $repositoryRoot "README.md") -Destination (Join-Path $publishRoot "README.md")

Compress-Archive -LiteralPath $publishRoot -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($checksumPath, "$hash  $packageName.zip`n", [Text.UTF8Encoding]::new($false))

Write-Host "Offline package: $archivePath"
Write-Host "SHA-256:        $hash"
