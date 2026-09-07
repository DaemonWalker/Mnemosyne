# Mnemosyne build script (PowerShell 5.1+, ASCII only).
# Default: dev build of the whole solution (plugin csproj copy targets run as part of it),
# enforcing the project baseline: 0 errors, 0 warnings (warnings are errors).
# Usage:
#   build.ps1 [-Configuration Debug] [-Run]
#       Dev build. -Run: after a successful build, kill any running instance and start the exe.
#   build.ps1 -Publish [-Runtime win-x64] [-Output artifacts/publish]
#       Release publish: framework-dependent, ReadyToRun, folder-style portable package:
#       artifacts/publish/Mnemosyne.exe + plugins/*.dll + config/ (initial structure).
param(
    [string]$Configuration = 'Debug',
    [switch]$Run,
    [switch]$Publish,
    [string]$Runtime = 'win-x64',
    [string]$Output = 'artifacts/publish'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Push-Location $root
try {
    if ($Publish) {
        $Configuration = 'Release'
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
        return
    }

    Write-Host "==> Building solution ($Configuration, warnings as errors)..."
    dotnet build Mnemosyne.slnx -c $Configuration --nologo -warnaserror
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
    Write-Host "==> Build OK: 0 errors, 0 warnings."

    if ($Run) {
        $exe = Join-Path $root "src/Mnemosyne/bin/$Configuration/net10.0-windows/Mnemosyne.exe"
        if (-not (Test-Path $exe)) { throw "exe not found: $exe" }
        # Single-instance app: a stale process would just swallow the new launch.
        Get-Process -Name 'Mnemosyne' -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 300
        Write-Host "==> Starting $exe ..."
        Start-Process -FilePath $exe
    }
} finally {
    Pop-Location
}
