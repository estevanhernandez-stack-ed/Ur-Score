# Import helpers: the Windows file picker, the import screen, message boxes, and the data files an import writes.
. (Join-Path $PSScriptRoot 'uia.ps1')

if (-not ('UrWin32Msg' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UrWin32Msg {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
}
"@
}

# Presses Import recipe... on Setup > Recipes, types the path into the file picker and presses Open.
function Start-Import([string]$path) {
    $setup = Open-SetupPage 'Recipes'
    Invoke-Element (Find-ByAutomationId $setup 'ImportRecipeButton')
    $dlg = Wait-UrWindow '^Import a recipe$' 20
    if (-not $dlg) { throw 'the file picker did not open' }
    Start-Sleep -Milliseconds 800
    # The common dialog's classic controls surface only as panes to this UIA client, so drive them by handle:
    # set the file name text (WM_SETTEXT), then click Open (BM_CLICK).
    $all = $dlg.FindAll($TS::Descendants, $Cond::TrueCondition)
    $edit = $all | Where-Object { $_.Current.AutomationId -eq '1148' -and $_.Current.ClassName -eq 'Edit' } | Select-Object -First 1
    $open = $all | Where-Object { $_.Current.AutomationId -eq '1' -and $_.Current.ClassName -eq 'Button' } | Select-Object -First 1
    if (-not $edit -or -not $open) { throw 'file picker controls not found' }
    [UrWin32Msg]::SendMessage([IntPtr]$edit.Current.NativeWindowHandle, 0x000C, [IntPtr]::Zero, $path) | Out-Null
    Start-Sleep -Milliseconds 300
    [UrWin32Msg]::PostMessage([IntPtr]$open.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Seconds 2
}

# Whatever an import put up: a message box, the import screen, or nothing.
function Get-AfterImport([int]$seconds = 20) {
    Wait-UrWindow '^(Ur Score|Import recipe|Update recipe)$' $seconds
}

function Close-MessageBox($w) {
    $ok = $w.FindAll($TS::Descendants, $Cond::TrueCondition) |
        Where-Object { $_.Current.ClassName -eq 'Button' -and $_.Current.Name -eq 'OK' } | Select-Object -First 1
    [UrWin32Msg]::PostMessage([IntPtr]$ok.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 800
}

# A message box's text lives in a Static control this UIA client names but types as a pane.
function Get-MessageBoxText($w) {
    $w.FindAll($TS::Descendants, $Cond::TrueCondition) |
        Where-Object { $_.Current.ClassName -eq 'Static' -and $_.Current.Name } | ForEach-Object { $_.Current.Name }
}

# ConvertFrom-Json's return value is not reliably enumerable as an array on every PowerShell version when
# the JSON is an array of exactly one element -- foreach always yields one object per element either way,
# in Windows PowerShell 5.1 and in PowerShell 7, so this never depends on that.
function Read-Sources {
    $file = Join-Path $UrData 'sources.json'
    if (-not (Test-Path $file)) { return @() }
    $text = Get-Content $file -Raw
    if (-not $text.Trim()) { return @() }
    $parsed = $text | ConvertFrom-Json
    foreach ($s in $parsed) { $s }
}

function Get-RoleText($source) { "$($source.role)".ToLowerInvariant() }

# Imports a fixture through the import screen with the given stats ticked. Returns the Setup window.
function Complete-ClanImport([string]$fixture, [string[]]$show, [string[]]$send) {
    Start-Import $fixture
    $screen = Wait-UrWindow '^(Import recipe|Update recipe)$' 30
    if (-not $screen) {
        $box = Get-AfterImport 2
        throw "no import screen; saw '$($box.Current.Name)': $((Get-MessageBoxText $box) -join ' ')"
    }
    foreach ($label in $show) { Set-Tick (Get-Check $screen "Show $label") $true }
    foreach ($label in $send) { Set-Tick (Get-Check $screen "Send $label") $true }
    Invoke-WhenReady $screen 'ImportButton'
    Start-Sleep -Seconds 2
    return Get-SetupWindow
}
