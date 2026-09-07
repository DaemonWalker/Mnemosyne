# Mnemosyne dev build script (PowerShell 5.1+, ASCII only).
# Builds the whole solution (plugin csproj copy targets run as part of it) and
# enforces the project baseline: 0 errors, 0 warnings (warnings are errors).
# Usage: scripts/build.ps1 [-Configuration Debug] [-Run]
#   -Run  after a successful build, kill any running instance and start the exe.
param(
    [string]$Configuration = 'Debug',
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
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
