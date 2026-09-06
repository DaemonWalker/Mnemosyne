# Mnemosyne cold-start measurement (PowerShell 5.1+, ASCII only).
# Measures elapsed time from process start until the main window becomes visible via UIA.
# Usage: measure-coldstart.ps1 -ExePath <path> [-WithSession] [-Runs 5]
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [switch]$WithSession,
    [int]$Runs = 5
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exeDir = Split-Path -Parent $ExePath
$cacheDir = Join-Path $exeDir 'cache'
$testDir = Join-Path $env:TEMP 'mnemo-step12'
$sessionFile1 = Join-Path $testDir 'session-file-a.txt'
$sessionFile2 = Join-Path $testDir 'session-file-b.txt'

function Kill-App {
    Get-Process -Name 'Mnemosyne' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300
}

function Measure-OneRun {
    param([string[]]$Arguments)
    Kill-App
    $start = Get-Date
    if ($Arguments.Count -gt 0) {
        $proc = Start-Process -FilePath $ExePath -ArgumentList $Arguments -PassThru
    } else {
        $proc = Start-Process -FilePath $ExePath -PassThru
    }
    $elapsedMs = -1
    $deadline = $start.AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        $proc.Refresh()
        if ($proc.HasExited) { break }
        try {
            $cond = New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)),
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))
            )
            $win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
            if ($null -ne $win -and -not $win.Current.IsOffscreen) {
                $elapsedMs = [int]((Get-Date) - $start).TotalMilliseconds
                break
            }
        } catch { }
        Start-Sleep -Milliseconds 10
    }
    return $elapsedMs
}

New-Item -ItemType Directory -Force -Path $testDir | Out-Null
'session test file A' | Out-File -Encoding utf8 $sessionFile1
'session test file B' | Out-File -Encoding utf8 $sessionFile2

if ($WithSession) {
    Write-Host 'Preparing session: open two files, wait for session.json, then kill.'
    Kill-App
    Start-Process -FilePath $ExePath -ArgumentList "`"$sessionFile1`"", "`"$sessionFile2`""
    $sessionJson = Join-Path $cacheDir 'session.json'
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and -not (Test-Path $sessionJson)) { Start-Sleep -Milliseconds 100 }
    Start-Sleep -Seconds 2
    if (Test-Path $sessionJson) { Write-Host 'session.json ready.' } else { Write-Host 'WARNING: session.json not found.' }
} else {
    Write-Host 'Preparing clean state: removing cache dir (no session to restore).'
    Kill-App
    if (Test-Path $cacheDir) { Remove-Item -Recurse -Force $cacheDir }
}

$results = @()
for ($i = 1; $i -le $Runs; $i++) {
    $ms = Measure-OneRun -Arguments @()
    $results += $ms
    Write-Host ("run {0}: {1} ms" -f $i, $ms)
    Start-Sleep -Milliseconds 500
}
Kill-App

$valid = $results | Where-Object { $_ -ge 0 }
if ($valid.Count -gt 0) {
    $avg = [int](($valid | Measure-Object -Average).Average)
    $min = ($valid | Measure-Object -Minimum).Minimum
    $max = ($valid | Measure-Object -Maximum).Maximum
    Write-Host ("RESULT runs={0} avg={1}ms min={2}ms max={3}ms" -f $valid.Count, $avg, $min, $max)
} else {
    Write-Host 'RESULT all runs failed (window not found).'
    exit 1
}
