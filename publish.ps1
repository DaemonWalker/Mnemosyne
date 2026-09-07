# Mnemosyne publish script (PowerShell 5.1+, ASCII only).
# Produces a framework-dependent, ReadyToRun, folder-style portable package:
#   artifacts/publish/Mnemosyne.exe + plugins/*.dll + config/ (initial structure)
# Usage: scripts/publish.ps1 [-Configuration Release] [-Runtime win-x64] [-Output artifacts/publish]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Output = 'artifacts/publish'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not (Test-Path (Join-Path $root 'Mnemosyne.slnx'))) { $root = Split-Path -Parent $PSScriptRoot }
Push-Location $root
try {
    $outDir = Join-Path $root $Output
    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

    Write-Host "==> Building solution ($Configuration) so plugin build targets run..."
    dotnet build Mnemosyne.slnx -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

    Write-Host "==> Publishing main app ($Runtime, self-contained=false, ReadyToRun)..."
    dotnet publish src/Mnemosyne/Mnemosyne.csproj -c $Configuration -r $Runtime --self-contained false -p:PublishReadyToRun=true -o $outDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    Write-Host "==> Copying formatter plugins to publish plugins/ ..."
    # The main app does not reference plugin projects; dotnet publish never emits them.
    # Plugin csproj build targets copy their dlls into the main app bin dir - reuse that.
    $builtPlugins = Join-Path $root "src/Mnemosyne/bin/$Configuration/net10.0-windows/plugins"
    $publishPlugins = Join-Path $outDir 'plugins'
    New-Item -ItemType Directory -Force -Path $publishPlugins | Out-Null
    Copy-Item (Join-Path $builtPlugins 'Mnemosyne.Formatters.*.dll') $publishPlugins
    # Abstractions is already deployed beside the main exe; plugins/ must not duplicate it.
    Remove-Item (Join-Path $publishPlugins 'Mnemosyne.Plugin.Abstractions.dll') -ErrorAction SilentlyContinue

    Write-Host "==> Creating initial config/ and cache/ structure (settings.json is generated on first run)..."
    New-Item -ItemType Directory -Force -Path (Join-Path $outDir 'config') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $outDir 'cache') | Out-Null

    $pluginDlls = Get-ChildItem $publishPlugins -Filter *.dll | Select-Object -ExpandProperty Name
    Write-Host ("==> Done. Package at: " + $outDir)
    Write-Host ("    plugins: " + ($pluginDlls -join ', '))
    Write-Host ("    Mnemosyne.exe: " + (Test-Path (Join-Path $outDir 'Mnemosyne.exe')))
} finally {
    Pop-Location
}
