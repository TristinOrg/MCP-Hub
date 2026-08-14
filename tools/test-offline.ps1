param(
    [string] $PackageRoot = "$PSScriptRoot\..\artifacts\UnityMCPHub-0.1.0-win-x64",
    [int] $Port = 18080
)

$ErrorActionPreference = "Stop"
$pythonPath            = Join-Path ([IO.Path]::GetFullPath($PackageRoot)) "runtime\coplay\python\python.exe"
if (!(Test-Path -LiteralPath $pythonPath))
{
    throw "Bundled Python runtime was not found at $pythonPath."
}

$standardOutput = Join-Path $env:TEMP "unity-mcp-offline-stdout.log"
$standardError  = Join-Path $env:TEMP "unity-mcp-offline-stderr.log"
$env:PYTHONNOUSERSITE = "1"
$env:PYTHONPATH        = ""
$env:FASTMCP_CHECK_FOR_UPDATES = "off"
$process = Start-Process `
    -FilePath $pythonPath `
    -ArgumentList @("-m", "main", "--transport", "http", "--http-url", "http://127.0.0.1:$Port") `
    -WorkingDirectory (Split-Path -Parent $pythonPath) `
    -WindowStyle Hidden `
    -PassThru `
    -RedirectStandardOutput $standardOutput `
    -RedirectStandardError $standardError

try
{
    $health = $null
    for ($attempt = 0; $attempt -lt 60; $attempt++)
    {
        Start-Sleep -Milliseconds 500
        try
        {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 1
            if ($health.version -eq "10.1.0")
            {
                break
            }
        }
        catch
        {
            if ($process.HasExited)
            {
                break
            }
        }
    }

    if ($health.version -ne "10.1.0")
    {
        Get-Content -LiteralPath $standardError -Tail 40
        throw "Bundled Coplay Server failed its offline health check."
    }

    Write-Host "Bundled Coplay Server health check passed: version $($health.version)"
}
finally
{
    if (!$process.HasExited)
    {
        Stop-Process -Id $process.Id -Force
    }
    Remove-Item -LiteralPath $standardOutput, $standardError -Force -ErrorAction SilentlyContinue
}
