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

# Types a path into the Windows file picker that is open under this title (Open or Save alike) and presses its
# main button. The common dialog's classic controls surface only as panes to this UIA client, so it is driven by
# handle: the file name text (WM_SETTEXT into the Edit - 1148 in an Open dialog, 1001 in a Save dialog, probed
# 2026-09-22), then the button (BM_CLICK on button 1, Open or Save).
function Complete-FileDialog([string]$titlePattern, [string]$path) {
    $dlg = Wait-UrWindow $titlePattern 20
    if (-not $dlg) { throw "the file picker '$titlePattern' did not open" }
    Start-Sleep -Milliseconds 800
    $all = $dlg.FindAll($TS::Descendants, $Cond::TrueCondition)
    $edit = $all | Where-Object { $_.Current.AutomationId -in @('1148', '1001') -and $_.Current.ClassName -eq 'Edit' } | Select-Object -First 1
    $open = $all | Where-Object { $_.Current.AutomationId -eq '1' -and $_.Current.ClassName -eq 'Button' } | Select-Object -First 1
    if (-not $edit -or -not $open) { throw 'file picker controls not found' }
    [UrWin32Msg]::SendMessage([IntPtr]$edit.Current.NativeWindowHandle, 0x000C, [IntPtr]::Zero, $path) | Out-Null
    Start-Sleep -Milliseconds 300
    [UrWin32Msg]::PostMessage([IntPtr]$open.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Seconds 2
}

# Presses Import recipe... on Setup > Recipes, types the path into the file picker and presses Open.
function Start-Import([string]$path) {
    $setup = Open-SetupPage 'Recipes'
    Invoke-Element (Find-ByAutomationId $setup 'ImportRecipeButton')
    Complete-FileDialog '^Import a recipe$' $path
}

# Why an import was refused, in the words the page says it in. A refused import no longer raises a message box
# to dismiss (owner rule, backlog V3-S.10): Setup > Recipes says it on ImportProblemLine, under the button that
# started it, and it stays there. '(absent)' when nothing was refused.
function Get-ImportRefusal([int]$seconds = 15) {
    # Re-find Setup on each look rather than holding one reference: Wait-Until's block has its own scope, and
    # the import may have opened and closed windows over it.
    Wait-Until { (Line (Get-SetupWindow) 'ImportProblemLine') -ne '(absent)' } $seconds | Out-Null
    Line (Get-SetupWindow) 'ImportProblemLine'
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
    if (-not $screen) { throw "no import screen; Setup says: $(Get-ImportRefusal 3)" }
    foreach ($label in $show) { Set-Tick (Get-Check $screen "Show $label") $true }
    foreach ($label in $send) { Set-Tick (Get-Check $screen "Send $label") $true }
    Invoke-WhenReady $screen 'ImportButton'
    Start-Sleep -Seconds 2
    return Get-SetupWindow
}
