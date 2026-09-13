# Mnemosyne regression smoke test against the PUBLISHED package (PS 5.1, ASCII only).
# Covers automatable items from requirements.md section 4; modal interactions are excluded
# and left to manual verification.
# Usage: scripts/regression-smoke.ps1 [-ExePath artifacts\publish\Mnemosyne.exe]
param(
    [string]$ExePath = 'artifacts\publish\Mnemosyne.exe'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = $PSScriptRoot
if (-not (Test-Path (Join-Path $root 'Mnemosyne.slnx'))) { $root = Split-Path -Parent $PSScriptRoot }
$ExePath = Join-Path $root $ExePath
$exeDir = Split-Path -Parent $ExePath
$cacheDir = Join-Path $exeDir 'cache'
$testDir = Join-Path $env:TEMP 'mnemo-step12'
$projDir = Join-Path $testDir 'proj'

$script:pass = 0
$script:fail = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { $script:pass++; Write-Host "PASS  $name" }
    else { $script:fail++; Write-Host "FAIL  $name" }
}
function Kill-App {
    Get-Process -Name 'Mnemosyne' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300
}
function Get-AppWindow([int]$procId, [int]$timeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $cond = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)),
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
        $win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($null -ne $win -and -not $win.Current.IsOffscreen) { return $win }
        Start-Sleep -Milliseconds 100
    }
    return $null
}
function Find-ById($rootEl, [string]$autoId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $autoId)
    return $rootEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
function Find-ByName($rootEl, [string]$name, $controlType) {
    $nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $cond = $nameCond
    if ($null -ne $controlType) {
        $cond = New-Object System.Windows.Automation.AndCondition($nameCond,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)))
    }
    return $rootEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
function Get-TabItems($win) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
    return @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond))
}
function Invoke-Menu($win, [string]$topPrefix, [string]$itemPrefix) {
    $menuCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
    $tops = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuCond)
    foreach ($top in $tops) {
        if ($top.Current.Name -like "$topPrefix*") {
            $ecp = $top.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            $ecp.Expand()
            Start-Sleep -Milliseconds 300
            $items = $top.FindAll([System.Windows.Automation.TreeScope]::Descendants, $menuCond)
            foreach ($it in $items) {
                if ($it.Current.Name -like "$itemPrefix*") {
                    $inv = $it.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                    $inv.Invoke()
                    return $true
                }
            }
            $ecp.Collapse()
            return $false
        }
    }
    return $false
}
function Wait-TabCount($win, [int]$n, [int]$timeoutSec = 10) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ((Get-TabItems $win).Count -ge $n) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}
function Open-ViaSingleInstance([string]$path) {
    Start-Process -FilePath $ExePath -ArgumentList "`"$path`""
}

# ---------- prepare test assets ----------
New-Item -ItemType Directory -Force -Path $projDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $projDir 'sub') | Out-Null
[IO.File]::WriteAllText((Join-Path $testDir 'fmt.json'), '{"a":1,"b":[1,2,{"c":"x"}]}')
[IO.File]::WriteAllText((Join-Path $testDir 'fmt.xml'), '<root><a x="1"><b>text</b></a></root>')
[IO.File]::WriteAllText((Join-Path $testDir 'fmt.html'), '<html><body><p>Hello <b>x</b></p><hr></body></html>')
[IO.File]::WriteAllText((Join-Path $testDir 'note.md'), "# Smoke Title`n`nhello **bold** world`n")
[IO.File]::WriteAllText((Join-Path $testDir 'plain.txt'), 'alpha beta alpha')
[IO.File]::WriteAllText((Join-Path $projDir 'needle-a.txt'), 'SMOKENEEDLE here')
[IO.File]::WriteAllText((Join-Path $projDir 'sub\needle-b.txt'), 'another SMOKENEEDLE')
$gbk = [Text.Encoding]::GetEncoding('GBK')
[IO.File]::WriteAllBytes((Join-Path $testDir 'gbk.txt'), $gbk.GetBytes([char]0x4E2D + [string][char]0x6587 + [string][char]0x6587 + [string][char]0x4EF6))

Write-Host '== A. launch published package with folder + json file =='
Kill-App
if (Test-Path $cacheDir) { Remove-Item -Recurse -Force $cacheDir }
$proc = Start-Process -FilePath $ExePath -ArgumentList "`"$projDir`"", "`"$(Join-Path $testDir 'fmt.json')`"" -PassThru
$win = Get-AppWindow $proc.Id
Check 'A1 published package launches, main window visible' ($null -ne $win)
if ($null -eq $win) { Write-Host 'abort: no window'; exit 1 }
Start-Sleep -Seconds 1

Check 'A2 json file opened as tab' (Wait-TabCount $win 1)
$treeCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TreeItem)
$treeItems = @($win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $treeCond))
Check ('A3 folder opened, file tree populated ({0} nodes)' -f $treeItems.Count) ($treeItems.Count -ge 2)
$langBtn = Find-ByName $win 'JSON' ([System.Windows.Automation.ControlType]::Button)
Check 'A4 status bar language = JSON' ($null -ne $langBtn)
$encBtn = Find-ByName $win 'UTF-8' ([System.Windows.Automation.ControlType]::Button)
Check 'A5 status bar encoding = UTF-8' ($null -ne $encBtn)

Write-Host '== B. format JSON via menu, save via menu, verify disk =='
$fmtOk = Invoke-Menu $win '编辑' '格式化文档'
Check 'B1 edit menu -> format document invoked' $fmtOk
Start-Sleep -Milliseconds 800
$saveOk = Invoke-Menu $win '文件' '保存'
Check 'B2 file menu -> save invoked' $saveOk
Start-Sleep -Milliseconds 800
$jsonNow = [IO.File]::ReadAllText((Join-Path $testDir 'fmt.json'))
Check 'B3 json formatted on disk (multi-line indented)' ($jsonNow -match "`n" -and $jsonNow -match '"a": 1')

Write-Host '== C. format XML via single-instance forward =='
Open-ViaSingleInstance (Join-Path $testDir 'fmt.xml')
Check 'C1 xml tab opened via single-instance forward' (Wait-TabCount $win 2)
Start-Sleep -Milliseconds 500
Invoke-Menu $win '编辑' '格式化文档' | Out-Null
Start-Sleep -Milliseconds 800
Invoke-Menu $win '文件' '保存' | Out-Null
Start-Sleep -Milliseconds 800
$xmlNow = [IO.File]::ReadAllText((Join-Path $testDir 'fmt.xml'))
Check 'C2 xml formatted on disk' ($xmlNow -match "`n" -and $xmlNow -match '<a x="1">')

Write-Host '== D. format HTML =='
Open-ViaSingleInstance (Join-Path $testDir 'fmt.html')
Check 'D1 html tab opened' (Wait-TabCount $win 3)
Start-Sleep -Milliseconds 500
Invoke-Menu $win '编辑' '格式化文档' | Out-Null
Start-Sleep -Milliseconds 800
Invoke-Menu $win '文件' '保存' | Out-Null
Start-Sleep -Milliseconds 800
$htmlNow = [IO.File]::ReadAllText((Join-Path $testDir 'fmt.html'))
Check 'D2 html formatted on disk' ($htmlNow -match "`n" -and $htmlNow -match '<body>')

Write-Host '== E. markdown preview tab =='
Open-ViaSingleInstance (Join-Path $testDir 'note.md')
Check 'E1 md tab opened' (Wait-TabCount $win 4)
Start-Sleep -Milliseconds 500
$prevOk = Invoke-Menu $win '视图' 'Markdown 预览'
Check 'E2 view menu -> markdown preview invoked' $prevOk
Start-Sleep -Milliseconds 800
$prevTitle = Find-ByName $win '预览：note.md' ([System.Windows.Automation.ControlType]::Text)
Check 'E3 preview tab opened with title' ($null -ne $prevTitle)
$rendered = Find-ByName $win 'Smoke Title' $null
Check 'E4 markdown rendered (H1 text present)' ($null -ne $rendered)

Write-Host '== F. in-page find =='
Open-ViaSingleInstance (Join-Path $testDir 'plain.txt')
Check 'F1 plain.txt tab opened' (Wait-TabCount $win 6)
Start-Sleep -Milliseconds 500
$findOk = Invoke-Menu $win '编辑' '查找'
Check 'F2 edit menu -> find invoked' $findOk
Start-Sleep -Milliseconds 500
$searchBox = Find-ById $win 'FindSearchBox'
if ($null -ne $searchBox) {
    $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('alpha')
}
Start-Sleep -Milliseconds 800
$countEl = Find-ById $win 'FindCountText'
$countOk = ($null -ne $countEl -and $countEl.Current.Name -match '^\d+/2$')
Check ('F3 find count = x/2 (actual: {0})' -f $(if ($countEl) { $countEl.Current.Name } else { 'n/a' })) $countOk

Write-Host '== G. folder search =='
$gOk = Invoke-Menu $win '编辑' '在文件夹中查找'
Check 'G1 edit menu -> search in folder invoked' $gOk
Start-Sleep -Milliseconds 500
$fsBox = Find-ById $win 'FolderSearchBox'
if ($null -ne $fsBox) {
    $fsBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('SMOKENEEDLE')
}
$statusEl = $null
$deadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $deadline) {
    $statusEl = Find-ById $win 'FolderSearchStatus'
    if ($null -ne $statusEl -and $statusEl.Current.Name -match '个结果' -and $statusEl.Current.Name -notmatch '正在搜索') { break }
    Start-Sleep -Milliseconds 300
}
$fsOk = ($null -ne $statusEl -and $statusEl.Current.Name -like '2 个结果*')
Check ('G2 folder search stats = 2 results (actual: {0})' -f $(if ($statusEl) { $statusEl.Current.Name } else { 'n/a' })) $fsOk
$fsTree = Find-ById $win 'FolderSearchResults'
$fsItems = 0
if ($null -ne $fsTree) { $fsItems = @($fsTree.FindAll([System.Windows.Automation.TreeScope]::Descendants, $treeCond)).Count }
Check ('G3 folder search results tree populated ({0} items)' -f $fsItems) ($fsItems -ge 2)

Write-Host '== H. GBK encoding detection =='
Open-ViaSingleInstance (Join-Path $testDir 'gbk.txt')
Check 'H1 gbk tab opened' (Wait-TabCount $win 7)
Start-Sleep -Milliseconds 800
$gbkBtn = Find-ByName $win 'GB18030' ([System.Windows.Automation.ControlType]::Button)
if ($null -eq $gbkBtn) { $gbkBtn = Find-ByName $win 'GBK' ([System.Windows.Automation.ControlType]::Button) }
Check 'H2 gbk.txt detected as GB18030/GBK' ($null -ne $gbkBtn)

Write-Host '== I. session persistence across kill/restart =='
Kill-App
$sessionJson = Join-Path $cacheDir 'session.json'
Check 'I1 session.json written' (Test-Path $sessionJson)
$proc2 = Start-Process -FilePath $ExePath -PassThru
$win2 = Get-AppWindow $proc2.Id
Check 'I2 restart after kill' ($null -ne $win2)
if ($null -ne $win2) {
    # preview tabs are excluded from the session by design (step 11.3), so 7 visible tabs -> 6 restored
    $restored = Wait-TabCount $win2 6 15
    Check ('I3 session restored 6 file tabs, preview excluded (actual: {0})' -f (Get-TabItems $win2).Count) $restored
}
Kill-App

Write-Host ''
Write-Host ("SMOKE RESULT: pass={0} fail={1}" -f $script:pass, $script:fail)
if ($script:fail -gt 0) { exit 1 }
