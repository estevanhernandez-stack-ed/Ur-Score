# UI Automation helpers for Ur Score's smoke scripts. Dot-source this file; it only defines things.
# ASCII only on purpose: Windows PowerShell 5.1 reads a BOM-less file as ANSI.

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$script:AE = [System.Windows.Automation.AutomationElement]
$script:TS = [System.Windows.Automation.TreeScope]
$script:Cond = [System.Windows.Automation.Condition]
$script:CT = [System.Windows.Automation.ControlType]

# Paths come from this file's own location, never from a machine.
$script:UrRepo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
# UR_SCORE_EXE walks another copy, such as the one RoRoRo installed (D22); else the Release build.
$script:UrExe = if ($env:UR_SCORE_EXE) { $env:UR_SCORE_EXE } else { Join-Path $UrRepo 'bin\Release\net10.0-windows\626labs.ur-score.exe' }
$script:UrFixtures = Join-Path $UrRepo 'tests\Fixtures'
$script:UrData = Join-Path $env:LOCALAPPDATA '626labs.ur-score'
$script:UrShots = Join-Path $UrRepo 'artifacts\smoke'
$script:Results = [System.Collections.Generic.List[object]]::new()
$script:SkipReasons = [System.Collections.Generic.List[string]]::new()

function Get-UrProcessId {
    (Get-Process | Where-Object { $_.ProcessName -match 'ur-score' } | Select-Object -First 1).Id
}

# Every top-level window of the Ur Score process: the board, Setup, the import screen, message boxes, the file picker.
# WPF nests owned windows several levels deep in the UIA tree (board -> Setup -> the file picker, or
# board -> Setup -> the import screen -> a message box that screen raises), not just one level under the
# root or one level under an owner. So this walks down from whatever was found so far, a few levels deep,
# instead of assuming a fixed depth of one. Runtime ids dedupe: a window found at one level is not walked
# into again if it also turns up elsewhere.
function Get-UrWindows {
    $procId = Get-UrProcessId
    if (-not $procId) { return @() }
    $byProcess = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $procId)
    $isWindow = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Window)

    $seen = @{}
    $list = @()
    $frontier = @($AE::RootElement.FindAll($TS::Children, $byProcess))
    foreach ($w in $frontier) { $seen[($w.GetRuntimeId() -join '-')] = $true }
    $list += $frontier

    # Five more levels reaches well past the deepest nesting seen so far (board -> Setup -> a screen it
    # opened -> a message box that screen raised is four); the loop stops early once a level finds nothing new.
    for ($i = 0; $i -lt 5; $i++) {
        $next = @()
        foreach ($w in $frontier) {
            foreach ($child in @($w.FindAll($TS::Children, $isWindow))) {
                $key = $child.GetRuntimeId() -join '-'
                if (-not $seen.ContainsKey($key)) {
                    $seen[$key] = $true
                    $next += $child
                }
            }
        }
        if ($next.Count -eq 0) { break }
        $list += $next
        $frontier = $next
    }

    return $list
}

function Wait-UrWindow([string]$titlePattern, [int]$seconds = 15) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $w = Get-UrWindows | Where-Object { $_.Current.Name -match $titlePattern } | Select-Object -First 1
        if ($w) { return $w }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Get-BoardWindow { Get-UrWindows | Where-Object { $_.Current.Name -eq 'RoRoRo Ur Score' } | Select-Object -First 1 }

function Get-SetupWindow { Get-UrWindows | Where-Object { $_.Current.Name -eq 'Setup' } | Select-Object -First 1 }

function Find-ByAutomationId($root, [string]$id) {
    if (-not $root) { return $null }
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $root.FindFirst($TS::Descendants, $c)
}

function Find-All($root, $controlType) {
    if (-not $root) { return @() }
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $controlType)
    @($root.FindAll($TS::Descendants, $c))
}

function Get-Button($root, [string]$name) { Find-All $root $CT::Button | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1 }

function Get-Edit($root, [string]$name) { Find-All $root $CT::Edit | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1 }

function Get-Check($root, [string]$name) { Find-All $root $CT::CheckBox | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1 }

function Invoke-Element($el) {
    if (-not $el) { throw 'element not found' }
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# Waits for an element to exist under $root, then invokes it. A window that just appeared (a new page, a
# screen an import opened) isn't always fully laid out for UI Automation to see its children on the very
# first poll; invoking straight off Find-ByAutomationId in that window races it, and Invoke-Element's own
# 'element not found' doesn't say which element was missing. This names it instead.
function Invoke-WhenReady($root, [string]$id, [int]$seconds = 15) {
    $el = $null
    $ok = Wait-Until { $el = Find-ByAutomationId $root $id; [bool]$el } $seconds
    if (-not $ok) { throw "$id never showed up" }
    Invoke-Element $el
}

function Set-ElementValue($el, [string]$value) {
    if (-not $el) { throw 'element not found' }
    $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Set-Tick($el, [bool]$on) {
    if (-not $el) { throw 'checkbox not found' }
    $p = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($p.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) -ne $on) {
        $p.Toggle()
        Start-Sleep -Milliseconds 400
    }
}

# The text of a TextBlock found by automation id; '(absent)' when it isn't shown (a collapsed line is not in the tree).
function Line($root, [string]$id) {
    $e = Find-ByAutomationId $root $id
    if ($e) { $e.Current.Name } else { '(absent)' }
}

function Wait-Line($root, [string]$id, [string]$pattern, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $text = Line $root $id
        if ($text -match $pattern) { return $text }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $text
}

function Wait-Until([scriptblock]$test, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        try { if (& $test) { return $true } } catch { }
        Start-Sleep -Milliseconds 400
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Get-AllTexts($root) {
    Find-All $root $CT::Text | ForEach-Object { $_.Current.Name } | Where-Object { $_ }
}

# True when UI Automation's focused element is $root itself or somewhere under it (a cell, a row, ...).
function Test-FocusWithin($root) {
    if (-not $root) { return $false }
    $focused = $AE::FocusedElement
    if (-not $focused) { return $false }
    $targetId = $root.GetRuntimeId() -join '-'
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $node = $focused
    for ($i = 0; $i -lt 25 -and $node; $i++) {
        if (($node.GetRuntimeId() -join '-') -eq $targetId) { return $true }
        $node = $walker.GetParent($node)
    }
    return $false
}

function Stop-UrScore {
    Get-Process | Where-Object { $_.ProcessName -match 'ur-score' } | ForEach-Object {
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(10000)) { $_.Kill(); $_.WaitForExit(5000) | Out-Null }
    }
}

# Starts the Release build and waits until the board has read the score book.
function Start-UrScore([int]$seconds = 60) {
    if (-not (Test-Path $UrExe)) {
        if ($env:UR_SCORE_EXE) { throw "UR_SCORE_EXE points at a missing file: $UrExe" }
        throw "No build at $UrExe. Run: dotnet build Ur-Score.csproj -c Release"
    }
    Start-Process -FilePath $UrExe -WorkingDirectory (Split-Path $UrExe) | Out-Null
    $board = Wait-UrWindow '^RoRoRo Ur Score$' $seconds
    if (-not $board) { throw 'the board window never appeared' }
    # Start is enabled once the score book has been read.
    Wait-Until { $start = Find-ByAutomationId (Get-BoardWindow) 'StartStopButton'; $start -and $start.Current.IsEnabled } $seconds | Out-Null
    # Every walk's first move from here is Setup, directly or through Open-SetupPage / Complete-ClanImport /
    # Initialize-ClanBoard: wait until it is actually in the tree before handing the window back, so an early
    # caller can't race a window UI Automation can't see the children of yet (the bug behind
    # walk-starter-board.ps1's one-off "element not found" before its first check).
    $hasSetup = Wait-Until { [bool](Find-ByAutomationId (Get-BoardWindow) 'SetupButton') } $seconds
    if (-not $hasSetup) { throw 'the board window appeared but SetupButton never showed up' }
    Start-Sleep -Seconds 1
    return Get-BoardWindow
}

function Close-UrWindow($w) {
    if (-not $w) { return }
    try { $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
    Start-Sleep -Milliseconds 600
}

# Opens Setup from the board when it isn't open, then selects a page by its title in the left list.
function Open-SetupPage([string]$title) {
    $setup = Get-SetupWindow
    if (-not $setup) {
        Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'SetupButton')
        $setup = Wait-UrWindow '^Setup$' 15
    }
    if (-not $setup) { throw 'Setup did not open' }
    $nav = Find-ByAutomationId $setup 'SetupNav'
    $isItem = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::ListItem)
    $item = @($nav.FindAll($TS::Children, $isItem)) | Where-Object { $_.Current.Name -eq $title } | Select-Object -First 1
    if (-not $item) { throw "Setup has no page named '$title'" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
    return $setup
}

# Types a name into a ClanSearchBox (found by its accessible name) and invokes its "Pick <name>" match.
# The list is read once per session, so this retries until it has arrived.
function Select-SearchName($root, [string]$searchLabel, [string]$name, [int]$seconds = 90) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $edit = Get-Edit $root $searchLabel
        if ($edit) {
            Set-ElementValue $edit ''
            Set-ElementValue $edit $name
            Start-Sleep -Milliseconds 700
            $pick = Get-Button $root "Pick $name"
            if ($pick) { Invoke-Element $pick; Start-Sleep -Milliseconds 600; return }
        }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    throw "no search match named '$name' under '$searchLabel'"
}

# Types a query and picks the first match whose name is not in $except. Returns the name picked.
function Select-FirstSearchMatch($root, [string]$searchLabel, [string]$query, [string[]]$except, [int]$seconds = 90) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $edit = Get-Edit $root $searchLabel
        if ($edit) {
            Set-ElementValue $edit ''
            Set-ElementValue $edit $query
            Start-Sleep -Milliseconds 700
            $pick = Find-All $root $CT::Button |
                Where-Object { $_.Current.Name -like 'Pick *' -and ($except -notcontains $_.Current.Name.Substring(5)) } |
                Select-Object -First 1
            if ($pick) {
                $name = $pick.Current.Name.Substring(5)
                Invoke-Element $pick
                Start-Sleep -Milliseconds 600
                return $name
            }
        }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    throw "no search match for '$query' under '$searchLabel'"
}

# Moves your data folder aside so a walk starts clean. Returns the backup path, or $null when you had none.
function Move-UrDataAside {
    Stop-UrScore
    $backup = "$UrData.smoke-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    if (Test-Path $UrData) {
        Rename-Item $UrData (Split-Path $backup -Leaf)
    } else {
        $backup = ''
    }
    try {
        New-Item -ItemType Directory -Force $UrData | Out-Null
    } catch {
        if ($backup) { Rename-Item $backup (Split-Path $UrData -Leaf) }
        throw
    }
    return $backup
}

# Puts your data folder back. Always call it from a finally.
function Restore-UrData([string]$backup) {
    Stop-UrScore
    if ($null -eq $backup) { return }
    if (Test-Path $UrData) { Remove-Item $UrData -Recurse -Force }
    if ($backup -and (Test-Path $backup)) { Rename-Item $backup (Split-Path $UrData -Leaf) }
    "Your data folder is back: $(Test-Path $UrData)"
}

function Check([string]$step, [bool]$ok, [string]$seen) {
    $script:Results.Add([pscustomobject]@{ Step = $step; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Seen = $seen })
}

# A step this run couldn't judge, and why ('needs a live battle'). It is neither a pass nor a failure: Seen leads with
# the reason, the summary counts it by reason, and it doesn't change the exit code.
function Skip([string]$step, [string]$why, [string]$seen) {
    $script:Results.Add([pscustomobject]@{ Step = $step; Result = 'SKIP'; Seen = "[$why] $seen" })
    $script:SkipReasons.Add($why)
}

function Show-Results {
    $script:Results | Format-Table -AutoSize -Wrap | Out-String -Width 240
    $failed = @($script:Results | Where-Object { $_.Result -eq 'FAIL' }).Count
    $skipped = @($script:Results | Where-Object { $_.Result -eq 'SKIP' }).Count
    $summary = "$($script:Results.Count - $failed - $skipped) passed, $failed failed"
    foreach ($reason in @($script:SkipReasons | Group-Object | Sort-Object Name)) { $summary += ", $($reason.Count) $($reason.Name)" }
    $summary
    if ($failed -gt 0) { $global:LASTEXITCODE = 1 } else { $global:LASTEXITCODE = 0 }
}

function Show-Tree($root, [int]$depth = 0, [int]$max = 8) {
    if ($depth -gt $max) { return }
    foreach ($k in $root.FindAll($TS::Children, $Cond::TrueCondition)) {
        $c = $k.Current
        ('  ' * $depth) + "[$($c.ControlType.ProgrammaticName -replace 'ControlType\.','')] id='$($c.AutomationId)' name='$($c.Name)'"
        Show-Tree $k ($depth + 1) $max
    }
}
