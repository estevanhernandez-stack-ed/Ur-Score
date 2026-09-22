# The 2026-09-17 visible fixes on a clean data folder, against a scratch rules file (RoRoRo's own metric-rules.json is
# never written, and the last step fails if its bytes change). Sends no clan Points. Walks:
#   S2-F.8   Duplicate is off for the empty first-run starter and on once the starter has panels.
#   S2-P.18  the drag grip is drawn, named, not a keyboard stop (S2-P.3), and dragging by it still moves a panel.
#   V3-S.13  "Drag to move" and "Return to the board" show Ur Score's own tooltip (screenshots for eyes).
#   S2-8.2   the pop-out's own close returns its panel; an outside close (WM_CLOSE, taskkill) keeps it popped out.
#   S2-8.3   a score book that can't be read shows Try again with no pop-out windows or slots; Try again recovers.
#   S2-FR.1  Past battles offers "Don't show your best account", and it survives reopening.
#   AC-6.11  Setup > Your accounts: the two notes sit 4 px apart.
#   AC-2.2   a hand-edited 1e300 threshold reads short, and Change opens with the refusal showing.
#   AC-3.4   a result for an alert that has gone is drawn in magenta.
#   S2-FR.2  Profile stat settings say a switched-off source isn't read.
# Needs RoRoRo running (your accounts) and the mouse left alone: the grip, tooltips and drag use the real pointer.
# Screenshots are taken only while an Ur Score window is in front.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'

if (-not ('UrPointer' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UrPointer {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
}
"@
}
# Physical pixels everywhere: UI Automation rectangles, the pointer and screen captures then agree at any scale.
[UrPointer]::SetProcessDPIAware() | Out-Null
Add-Type -AssemblyName System.Drawing

$profileFixture = Join-Path $UrFixtures 'petsim99-profile.recipe.json'
$realRules = Join-Path $env:LOCALAPPDATA 'ROROROblox\metric-rules.json'
$scratchDir = Join-Path $env:TEMP "ur-score-smoke-visible-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$scratch = Join-Path $scratchDir 'metric-rules.json'
$shots = Join-Path $UrShots 'visible-fixes'
$backup = $null
$denied = $null
$lock = $null

function Get-RealRulesHash {
    if (Test-Path $realRules) { (Get-FileHash $realRules -Algorithm SHA256).Hash } else { 'absent' }
}

function Get-Shown($root, $type, [string]$name) {
    Find-All $root $type | Where-Object { $_.Current.Name -eq $name -and -not $_.Current.IsOffscreen } | Select-Object -First 1
}

function Get-Center($el) {
    $r = $el.Current.BoundingRectangle
    return @([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
}

function Move-Pointer([int]$x, [int]$y) { [UrPointer]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 60 }

# Parks the pointer in a corner of the board, so a tooltip from before can't be the one found next.
function Clear-Pointer {
    $r = (Get-BoardWindow).Current.BoundingRectangle
    Move-Pointer ([int]$r.X + 12) ([int]$r.Y + 12)
    Start-Sleep -Milliseconds 700
}

function Show-Front($window) {
    $h = [IntPtr]$window.Current.NativeWindowHandle
    [UrPointer]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 500
}

function Test-UrInFront {
    $h = [UrPointer]::GetForegroundWindow()
    $procId = [uint32]0
    [UrPointer]::GetWindowThreadProcessId($h, [ref]$procId) | Out-Null
    return ($procId -eq [uint32](Get-UrProcessId))
}

# A screen area around a rectangle, saved only while Ur Score is in front, so nothing else on the screen is captured.
function Save-Area($rect, [string]$name, [int]$pad = 24) {
    if (-not (Test-UrInFront)) { return "not saved: Ur Score was not in front" }
    New-Item -ItemType Directory -Force $shots | Out-Null
    $x = [int]$rect.X - $pad; $y = [int]$rect.Y - $pad
    $w = [int]$rect.Width + 2 * $pad; $h = [int]$rect.Height + 2 * $pad
    $bitmap = New-Object System.Drawing.Bitmap $w, $h
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($x, $y, 0, 0, $bitmap.Size)
    $path = Join-Path $shots "$name.png"
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
    return $path
}

# Two rectangles as one, for a shot that holds both a control and its tooltip.
function Join-Rect($a, $b) {
    $left = [Math]::Min($a.X, $b.X); $top = [Math]::Min($a.Y, $b.Y)
    $right = [Math]::Max($a.X + $a.Width, $b.X + $b.Width); $bottom = [Math]::Max($a.Y + $a.Height, $b.Y + $b.Height)
    return New-Object System.Windows.Rect($left, $top, ($right - $left), ($bottom - $top))
}

# The tooltip Ur Score is showing, if any: a top-level element of its process whose type is ToolTip.
function Get-UrToolTip {
    $procId = Get-UrProcessId
    if (-not $procId) { return $null }
    $byProcess = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $procId)
    foreach ($top in @($AE::RootElement.FindAll($TS::Children, $byProcess))) {
        if ($top.Current.ControlType -eq $CT::ToolTip) { return $top }
        $inner = @(Find-All $top $CT::ToolTip) | Select-Object -First 1
        if ($inner) { return $inner }
    }
    return $null
}

function Wait-ToolTip([int]$seconds = 4) {
    Wait-Until { [bool](Get-UrToolTip) } $seconds | Out-Null
    return Get-UrToolTip
}

# The tab menu's item, whether it takes a press, closing the menu without pressing it.
function Get-TabMenuItemEnabled($board, [string]$itemId) {
    $selected = Get-TabItems $board |
        Where-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } |
        Select-Object -First 1
    if (-not $selected) { return $null }
    $selected.SetFocus()
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('+{F10}')
    $item = $null
    $deadline = (Get-Date).AddSeconds(6)
    do {
        $item = Find-InUrWindows $itemId
        if ($item) { break }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    $enabled = if ($item) { $item.Current.IsEnabled } else { $null }
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 400
    return $enabled
}

function Get-SavedPanel([string]$type) {
    foreach ($b in @(Read-Boards)) {
        foreach ($p in @($b.panels)) { if ($p -and $p.type -eq $type) { return $p } }
    }
    return $null
}

function Open-StandingPopOut {
    Invoke-PanelTool (Get-BoardWindow) 'StandingPanel1' 'PopOutButton'
    Wait-Until { Get-PopOutFor 'StandingPanel1' } 15 | Out-Null
    return Get-PopOutFor 'StandingPanel1'
}

# The share of a rectangle's pixels that read as magenta (high red, low green, some blue).
function Get-MagentaShare($rect) {
    $bitmap = New-Object System.Drawing.Bitmap ([int]$rect.Width), ([int]$rect.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bitmap.Size)
    $magenta = 0; $ink = 0
    for ($x = 0; $x -lt $bitmap.Width; $x += 1) {
        for ($y = 0; $y -lt $bitmap.Height; $y += 1) {
            $c = $bitmap.GetPixel($x, $y)
            if ([Math]::Max($c.R, [Math]::Max($c.G, $c.B)) -lt 110) { continue }
            $ink++
            if ($c.R -gt 170 -and $c.G -lt 120 -and $c.B -gt 70) { $magenta++ }
        }
    }
    $graphics.Dispose(); $bitmap.Dispose()
    if ($ink -eq 0) { return 0 }
    return [Math]::Round($magenta / $ink, 2)
}

function Write-Rules([string]$json) { Set-Content -Path $scratch -Encoding ASCII -Value $json }

$realBefore = Get-RealRulesHash
try {
    New-Item -ItemType Directory -Force $scratchDir | Out-Null
    # A 1e300 threshold typed by hand on a sent stat, and an alert Ur Score owns for a stat this walk never sends.
    Write-Rules @'
[
  { "metricId": "ps99.diamonds", "kind": "Rate", "threshold": 1e300, "windowMinutes": 10, "alertWhenBelow": true, "owner": "626labs.ur-score" },
  { "metricId": "ps99.rank", "kind": "Rate", "threshold": 3, "windowMinutes": 30, "alertWhenBelow": true, "owner": "626labs.ur-score" }
]
'@
    $env:UR_SCORE_RULES_FILE = $scratch
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null

    # 1. S2-F.8: the empty first-run starter can't be duplicated.
    $emptyDuplicate = Get-TabMenuItemEnabled (Get-BoardWindow) 'DuplicateBoardItem'
    if ($null -eq $emptyDuplicate) {
        Skip '1 Duplicate is off for the empty starter' 'no tab or no menu at first run' ((Get-TabNames (Get-BoardWindow)) -join ', ')
    }
    else {
        Check '1 Duplicate is off for the empty starter' ($emptyDuplicate -eq $false) "DuplicateBoardItem enabled=$emptyDuplicate; tabs: $((Get-TabNames (Get-BoardWindow)) -join ', ')"
    }

    # The clan fixture with Points shown and nothing sent, and the main clan.
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'SetupButton')
    Wait-UrWindow '^Setup$' 15 | Out-Null
    Complete-ClanImport (Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json') @('Points') @() | Out-Null
    $setup = Wait-UrWindow '^Setup$' 30
    Select-SearchName $setup 'Your main clan' $Main
    Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null
    Close-UrWindow (Get-SetupWindow)
    Wait-Until {
        $p = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1'
        $p -and (Line $p 'PanelTitle') -eq 'Clan standing'
    } 30 | Out-Null

    $fullDuplicate = Get-TabMenuItemEnabled (Get-BoardWindow) 'DuplicateBoardItem'
    Check '1b ...and on once the starter has panels' ($fullDuplicate -eq $true) "DuplicateBoardItem enabled=$fullDuplicate; tabs: $((Get-TabNames (Get-BoardWindow)) -join ', ')"

    # 2. S2-P.18, S2-P.3, V3-S.13: the grip in edit mode.
    $board = Get-BoardWindow
    Show-Front $board
    Enter-EditMode $board
    $board = Get-BoardWindow
    $panelsBefore = @(Get-PanelIds $board)
    $grips = @($board.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'DragHandle'))))
    Check '2 Every panel has a named grip' (($grips.Count -eq $panelsBefore.Count) -and ($grips.Count -gt 0) -and -not @($grips | Where-Object { $_.Current.Name -ne 'Drag to move' })) "grips=$($grips.Count) panels=$($panelsBefore.Count) names=$((@($grips | ForEach-Object { $_.Current.Name }) | Sort-Object -Unique) -join '/')"
    Check '2b The grip is not a keyboard stop' (-not @($grips | Where-Object { $_.Current.IsKeyboardFocusable })) "focusable=$(@($grips | Where-Object { $_.Current.IsKeyboardFocusable }).Count)"

    $grip = $grips[0]
    Clear-Pointer
    $c = Get-Center $grip
    Move-Pointer ($c[0] - 2) $c[1]
    Move-Pointer $c[0] $c[1]
    $tip = Wait-ToolTip
    $tipName = if ($tip) { (@($tip.Current.Name) + @(Get-AllTexts $tip)) -join ' ' } else { '' }
    Check '2c Hovering the grip shows "Drag to move"' ($tipName -match 'Drag to move') "tooltip='$tipName'"
    if ($tip) { $shot = Save-Area (Join-Rect $grip.Current.BoundingRectangle $tip.Current.BoundingRectangle) 'tooltip-drag-to-move'; "  shot: $shot" | Out-Host }
    $gripShot = Save-Area (Find-ByAutomationId $board $panelsBefore[0]).Current.BoundingRectangle 'grip-in-edit-mode' 8
    "  shot: $gripShot" | Out-Host

    # Drag the first panel by its grip onto the last other panel whose middle is inside the window: a drop below the
    # window's edge lands on nothing, and the order stays as it was.
    $win = $board.Current.BoundingRectangle
    $target = @($panelsBefore | Select-Object -Skip 1 | ForEach-Object { Find-ByAutomationId $board $_ } | Where-Object {
        $m = Get-Center $_
        -not $_.Current.IsOffscreen -and $m[1] -gt $win.Y -and $m[1] -lt ($win.Y + $win.Height - 40) }) | Select-Object -Last 1
    if ($target) {
        $t = Get-Center $target
        Move-Pointer $c[0] $c[1]
        [UrPointer]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 150
        for ($i = 1; $i -le 12; $i++) {
            Move-Pointer ([int]($c[0] + ($t[0] - $c[0]) * $i / 12)) ([int]($c[1] + ($t[1] - $c[1]) * $i / 12))
            Start-Sleep -Milliseconds 40
        }
        Start-Sleep -Milliseconds 300
        [UrPointer]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 1200
        $panelsAfter = @(Get-PanelIds (Get-BoardWindow))
        Check '2d Dragging by the grip moves the panel' (($panelsAfter -join ',') -ne ($panelsBefore -join ',')) "before: $($panelsBefore -join ', ') | after: $($panelsAfter -join ', ')"
    }
    else {
        Skip '2d Dragging by the grip moves the panel' 'no other panel in view to drop on' ($panelsBefore -join ', ')
    }
    Clear-Pointer
    Complete-EditMode (Get-BoardWindow)

    # 3. S2-8.2 and V3-S.13: pop-outs.
    $out = Open-StandingPopOut
    Show-Front $out
    $return = Find-ByAutomationId $out 'ReturnPanelButton'
    Clear-Pointer
    Show-Front $out
    $rc = Get-Center $return
    Move-Pointer ($rc[0] - 2) $rc[1]
    Move-Pointer $rc[0] $rc[1]
    $tip = Wait-ToolTip
    $tipName = if ($tip) { (@($tip.Current.Name) + @(Get-AllTexts $tip)) -join ' ' } else { '' }
    Check '3 Hovering the pop-out close shows "Return to the board"' ($tipName -match 'Return to the board') "tooltip='$tipName'"
    if ($tip) { $shot = Save-Area (Join-Rect $return.Current.BoundingRectangle $tip.Current.BoundingRectangle) 'tooltip-return-to-board'; "  shot: $shot" | Out-Host }
    Clear-Pointer

    # The window's own close command (the X, Alt+F4) returns the panel.
    [UrPointer]::PostMessage([IntPtr]$out.Current.NativeWindowHandle, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Seconds 2
    Check '3b The pop-out close command returns the panel' (-not (Get-PopOutFor 'StandingPanel1') -and [bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1') -and $null -eq (Get-SavedPanel 'standing').popout) "window=$([bool](Get-PopOutFor 'StandingPanel1')) popout=$((Get-SavedPanel 'standing').popout)"

    # A close from outside (WM_CLOSE alone) keeps it popped out and doesn't reopen it.
    $out = Open-StandingPopOut
    [UrPointer]::PostMessage([IntPtr]$out.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Seconds 4
    Check '3c An outside close keeps the slot popped out' ([bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1PoppedOut') -and $null -ne (Get-SavedPanel 'standing').popout) "slot=$([bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1PoppedOut')) popout saved=$($null -ne (Get-SavedPanel 'standing').popout)"
    Check '3d ...and does not reopen the window' (-not (Get-PopOutFor 'StandingPanel1')) "window=$([bool](Get-PopOutFor 'StandingPanel1'))"
    Invoke-Element (Get-Button (Get-BoardWindow) 'Bring back Clan standing')
    Start-Sleep -Seconds 1
    Check '3e Bring back returns it' ([bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1') -and $null -eq (Get-SavedPanel 'standing').popout) "popout=$((Get-SavedPanel 'standing').popout)"

    # Not pass or fail: what a UI Automation Close (a screen reader's, or a tool's) does to a pop-out.
    $out = Open-StandingPopOut
    try { $out.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
    Start-Sleep -Seconds 2
    $uiaReturned = [bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1') -and -not (Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1PoppedOut')
    "  NOTE a UI Automation Close on a pop-out $(if ($uiaReturned) { 'returned the panel, like its X' } else { 'kept the panel popped out, like an outside close' })" | Out-Host
    if (-not $uiaReturned) {
        $bring = Get-Button (Get-BoardWindow) 'Bring back Clan standing'
        if ($bring) { Invoke-Element $bring; Start-Sleep -Seconds 1 }
    }

    # taskkill without /F closes every window from outside: the pop-out is kept and reopens at the next start.
    $out = Open-StandingPopOut
    $procId = Get-UrProcessId
    & taskkill.exe /PID $procId | Out-Null
    Wait-Until { -not (Get-UrProcessId) } 20 | Out-Null
    $closed = -not (Get-UrProcessId)
    Check '3f taskkill keeps the pop-out saved' ($closed -and $null -ne (Get-SavedPanel 'standing').popout) "exited=$closed popout saved=$($null -ne (Get-SavedPanel 'standing').popout)"
    if (-not $closed) { Stop-UrScore }
    Start-UrScore | Out-Null
    Wait-Until { Get-PopOutFor 'StandingPanel1' } 30 | Out-Null
    Check '3g ...and it reopens at the next start' ([bool](Get-PopOutFor 'StandingPanel1')) "count=$((Get-PopOutWindows).Count)"

    # 4. S2-8.3: a score book that can't be read. The pop-out stays saved from step 3g.
    Stop-UrScoreFromBoard
    $bookRoot = Join-Path $UrData 'scorebook'
    New-Item -ItemType Directory -Force $bookRoot | Out-Null
    $who = "$env:USERDOMAIN\$env:USERNAME"
    & icacls.exe $bookRoot /deny "${who}:(RD)" | Out-Null
    $denied = $bookRoot
    $unreadable = $false
    try { [System.IO.Directory]::GetDirectories($bookRoot) | Out-Null } catch { $unreadable = $true }
    if (-not $unreadable) {
        Skip '4 An unreadable score book shows Try again' 'the deny rule did not stop listing the folder' $bookRoot
    }
    else {
        Start-Process -FilePath $UrExe -WorkingDirectory (Split-Path $UrExe) | Out-Null
        $failedBoard = Wait-UrWindow '^RoRoRo Ur Score$' 30
        Wait-Until { $b = Find-ByAutomationId (Get-BoardWindow) 'EmptyStateButton'; $b -and $b.Current.Name -match 'Try again' } 30 | Out-Null
        Start-Sleep -Seconds 3
        $tryAgain = Find-ByAutomationId (Get-BoardWindow) 'EmptyStateButton'
        $slots = @(Get-PanelIds (Get-BoardWindow))
        Check '4 An unreadable score book shows Try again' ([bool]$tryAgain -and $tryAgain.Current.Name -match 'Try again') "button='$(if ($tryAgain) { $tryAgain.Current.Name })' line='$(Line (Get-BoardWindow) 'EmptyStateLine')'"
        Check '4b ...with no pop-out window' ((Get-PopOutWindows).Count -eq 0) "count=$((Get-PopOutWindows).Count)"
        Check '4c ...and no panels or popped-out slots' ($slots.Count -eq 0) "found: $($slots -join ', ')"
        Show-Front (Get-BoardWindow)
        $shot = Save-Area (Get-BoardWindow).Current.BoundingRectangle 'book-unreadable' 0
        "  shot: $shot" | Out-Host

        & icacls.exe $bookRoot /remove:d $who | Out-Null
        $denied = $null
        Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'EmptyStateButton')
        Wait-Until { Get-PopOutFor 'StandingPanel1' } 30 | Out-Null
        Check '4d Try again loads the book and reopens the pop-out' ([bool](Get-PopOutFor 'StandingPanel1')) "count=$((Get-PopOutWindows).Count)"
        $bring = Get-Button (Get-BoardWindow) 'Bring back Clan standing'
        if ($bring) { Invoke-Element $bring; Start-Sleep -Seconds 1 }
    }

    # 5. S2-FR.1: Past battles without a best-account line.
    $pastId = @(Get-PanelIds (Get-BoardWindow) | Where-Object { $_ -like 'Past*' }) | Select-Object -First 1
    if (-not $pastId) {
        Skip '5 Past battles offers no best-account line' 'no Past panel on the board' ((Get-PanelIds (Get-BoardWindow)) -join ', ')
    }
    else {
        Invoke-PanelTool (Get-BoardWindow) $pastId 'PanelSettingsButton'
        $form = Wait-UrWindow '^Panel settings$' 15
        $stat = Find-ByAutomationId $form 'StatBox'
        $statItems = @()
        if ($stat) {
            $stat.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            Start-Sleep -Milliseconds 500
            $statItems = @(Find-All $stat $CT::ListItem | ForEach-Object { $_.Current.Name })
            $stat.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
            Start-Sleep -Milliseconds 300
        }
        $noStat = "Don't show your best account"
        Check '5 Past battles offers "Don''t show your best account"' ($statItems -contains $noStat) "items: $($statItems -join ' | ')"
        if ($statItems -contains $noStat) {
            Select-ComboItem (Find-ByAutomationId $form 'StatBox') $noStat
            Invoke-Element (Find-ByAutomationId (Wait-UrWindow '^Panel settings$' 5) 'SaveSettingsButton')
            Start-Sleep -Seconds 1
            Invoke-PanelTool (Get-BoardWindow) $pastId 'PanelSettingsButton'
            $form = Wait-UrWindow '^Panel settings$' 15
            $selection = (Find-ByAutomationId $form 'StatBox').GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
            $chosen = if ($selection.Count -gt 0) { $selection[0].Current.Name } else { '' }
            Check '5b ...and it is still chosen when reopened' ($chosen -eq $noStat) "chosen='$chosen'"
        }
        Close-UrWindow (Wait-UrWindow '^Panel settings$' 3)
    }

    # 6. AC-6.11: Setup > Your accounts notes.
    $setup = Open-SetupPage 'Your accounts'
    $texts = @(Find-All $setup $CT::Text)
    $send = $texts | Where-Object { $_.Current.Name -like 'Send reports an account*' } | Select-Object -First 1
    $picture = $texts | Where-Object { $_.Current.Name -like 'Each picture comes from Roblox*' } | Select-Object -First 1
    if ($send -and $picture) {
        $dpi = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero).DpiY / 96
        $gap = [int]($picture.Current.BoundingRectangle.Y - ($send.Current.BoundingRectangle.Y + $send.Current.BoundingRectangle.Height))
        $expected = [int][Math]::Round(4 * $dpi)
        Check '6 The two notes sit 4 px apart' ([Math]::Abs($gap - $expected) -le 2) "gap=$gap px at scale $dpi (4 px = $expected)"
        Show-Front $setup
        $area = Join-Rect $send.Current.BoundingRectangle $picture.Current.BoundingRectangle
        $shot = Save-Area $area 'accounts-notes' 40
        "  shot: $shot" | Out-Host
    }
    else {
        Check '6 The two notes sit 4 px apart' $false "notes found: send=$([bool]$send) picture=$([bool]$picture)"
    }

    # 7. AC-2.2 and AC-3.4: Setup > Alerts, with Diamonds sent.
    Complete-ClanImport $profileFixture @('Diamonds') @('Diamonds') | Out-Null
    $setup = Open-SetupPage 'Alerts'
    Wait-Until { @(Get-AllTexts (Get-SetupWindow)) -match '1E\+300' } 15 | Out-Null
    $sentence = @(Get-AllTexts (Get-SetupWindow)) | Where-Object { $_ -match '1E\+300' } | Select-Object -First 1
    Check '7 A 1e300 threshold reads short' ([bool]$sentence -and $sentence.Length -le 120) "length=$(if ($sentence) { $sentence.Length }) '$sentence'"
    $change = Get-Shown (Get-SetupWindow) $CT::Button 'Change the stops climbing alert for Diamonds'
    if ($change) {
        Invoke-Element $change
        $problem = Wait-Line (Get-SetupWindow) 'AlertEditorProblemLine' '.' 5
        Check '7b Change opens with the refusal showing' ($problem -and $problem -ne '(absent)') "problem='$problem'"
        Show-Front (Get-SetupWindow)
        $card = Find-ByAutomationId (Get-SetupWindow) 'AlertEditorProblemLine'
        if ($card) { $shot = Save-Area $card.Current.BoundingRectangle 'alert-1e300-change' 120; "  shot: $shot" | Out-Host }
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Milliseconds 600
    }
    else {
        Check '7b Change opens with the refusal showing' $false 'no Change button for Diamonds'
    }
    $scratchAfterChange = Get-Content $scratch -Raw
    Check '7c Nothing was written' ($scratchAfterChange -match '1e300') 'the 1e300 rule is still in the scratch file as typed'

    # The Player rank alert goes from the file behind the page's back; Remove on its stale card has nothing to remove,
    # and that result has no card of its own to sit on.
    $remove = Get-Shown (Get-SetupWindow) $CT::Button 'Remove the stops climbing alert for Player rank'
    if ($remove) {
        Write-Rules @'
[
  { "metricId": "ps99.diamonds", "kind": "Rate", "threshold": 1e300, "windowMinutes": 10, "alertWhenBelow": true, "owner": "626labs.ur-score" }
]
'@
        Invoke-Element $remove
        $orphan = Wait-Line (Get-SetupWindow) 'AlertsResultLine' '.' 5
        Show-Front (Get-SetupWindow)
        $line = Find-ByAutomationId (Get-SetupWindow) 'AlertsResultLine'
        $share = if ($line) { Get-MagentaShare $line.Current.BoundingRectangle } else { 0 }
        Check '7d A result for an alert that has gone is magenta' ([bool]$line -and $share -ge 0.5) "line='$orphan' magenta share of ink=$share"
        if ($line) { $shot = Save-Area $line.Current.BoundingRectangle 'alert-orphan-result' 16; "  shot: $shot" | Out-Host }
    }
    else {
        Skip '7d A result for an alert that has gone is magenta' 'no card for the Player rank alert' ((@(Get-AllTexts (Get-SetupWindow)) | Select-Object -First 12) -join ' | ')
    }
    Close-UrWindow (Get-SetupWindow)

    # 8. S2-FR.2: Profile stat on a switched-off source.
    Stop-UrScoreFromBoard
    $sourcesFile = Join-Path $UrData 'sources.json'
    $all = @(Read-Sources)
    $profileSource = $all | Where-Object { "$($_.recipe)" -like '*profile*' } | Select-Object -First 1
    if (-not $profileSource) {
        Skip '8 Profile stat settings say a switched-off source is not read' 'the profile import made no source' ((@($all | ForEach-Object { "$($_.recipe)" })) -join ', ')
        Start-UrScore | Out-Null
    }
    else {
        foreach ($s in $all) { if ($s.id -eq $profileSource.id) { $s.enabled = $false } }
        ConvertTo-Json -InputObject @($all) -Depth 8 | Set-Content -Path $sourcesFile -Encoding UTF8
        Start-UrScore | Out-Null
        Enter-EditMode (Get-BoardWindow)
        Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'AddPanelButton')
        $gallery = Wait-UrWindow '^Add a panel$' 15
        $add = if ($gallery) { Get-Button $gallery 'Add Profile stat' } else { $null }
        if (-not $add) {
            Skip '8 Profile stat settings say a switched-off source is not read' 'the gallery has no Profile stat card with its source off' "gallery=$([bool]$gallery)"
            if ($gallery) { Close-UrWindow $gallery }
        }
        else {
            Invoke-Element $add
            $form = Wait-UrWindow '^Add Profile stat$' 15
            $warning = Wait-Line $form 'SettingsWarningLine' '.' 5
            Check '8 Profile stat settings say a switched-off source is not read' ($warning -and $warning -ne '(absent)' -and $warning -match 'off') "warning='$warning'"
            $save = Find-ByAutomationId $form 'SaveSettingsButton'
            Check '8b ...without blocking Save' ([bool]$save -and $save.Current.IsEnabled) "save enabled=$(if ($save) { $save.Current.IsEnabled })"
            Show-Front $form
            $shot = Save-Area $form.Current.BoundingRectangle 'profile-stat-source-off' 0
            "  shot: $shot" | Out-Host
            Close-UrWindow $form
        }
        Complete-EditMode (Get-BoardWindow)
    }

    # 9. Not walkable without a live battle: say so rather than pass them.
    Skip '9 AC-B2.18 the SENT dot is named with its account' 'needs a battle read that sends Points' 'covered by MyAccountsAccessibilityTests; this walk sends nothing'
    Skip '9b S1-F.1 Diagnostics names the source holding a claim' 'needs two clans reading one of your accounts in a live battle' 'covered by DiagnosticsModelTests and RecipeWatchBookTests'

    & (Join-Path $PSScriptRoot 'check-boards-privacy.ps1') | Out-Host
    $privacy = $LASTEXITCODE
    Check '10 boards.json holds no other player' ($privacy -eq 0) "exit=$privacy"
}
finally {
    [UrPointer]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    if ($lock) { $lock.Dispose() }
    $restoreSafe = $true
    if ($denied) {
        & icacls.exe $denied /remove:d "$env:USERDOMAIN\$env:USERNAME" | Out-Null
        try { [System.IO.Directory]::GetDirectories($denied) | Out-Null } catch { $restoreSafe = $false }
    }
    Remove-Item Env:UR_SCORE_RULES_FILE -ErrorAction SilentlyContinue
    if ($restoreSafe) {
        if ($null -ne $backup) { Restore-UrData $backup }
    }
    else {
        Stop-UrScore
        "DATA NOT RESTORED: the deny rule on $denied could not be removed. Your data is in $backup. Remove the rule with: icacls `"$denied`" /remove:d $env:USERDOMAIN\$env:USERNAME, then delete $UrData and rename the backup back." | Out-Host
    }
    Remove-Item $scratchDir -Recurse -Force -ErrorAction SilentlyContinue
    Check '11 RoRoRo''s own rules file is unchanged' ((Get-RealRulesHash) -eq $realBefore) 'hash compared'
    Show-Results
}
exit $LASTEXITCODE
