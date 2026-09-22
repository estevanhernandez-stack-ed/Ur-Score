# Stage 2 pop-outs on a clean data folder: pop out a panel, it sits on top with no tools of its own and its
# slot on the board says so, it updates after a read, a second one opens, a moved window's place is saved,
# a restart reopens both, and closing one or Bring back returns each panel.
# Step 3 needs a clan battle the source reports (activeClanBattle keeps the last one); an idle source shows dashes, and
# the step is then listed as needing a live battle, which doesn't fail the walk.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$backup = $null


function Get-SavedPanel([string]$type) {
    foreach ($b in @(Read-Boards)) {
        foreach ($p in @($b.panels)) { if ($p -and $p.type -eq $type) { return $p } }
    }
    return $null
}

try {
    $backup = Move-UrDataAside
    Note-RoRoRo 'before'
    $board = Initialize-ClanBoard $Main ''

    # 1. Pop out the clan standing.
    Invoke-PanelTool $board 'StandingPanel1' 'PopOutButton'
    Wait-Until { Get-PopOutFor 'StandingPanel1' } 15 | Out-Null
    $out = Get-PopOutFor 'StandingPanel1'
    Check '1 Clan standing opens in its own window' ([bool]$out -and $out.Current.Name -eq 'Clan standing') "window='$(if ($out) { $out.Current.Name })'"
    Check '1b It stays on top' ([bool]$out -and $out.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Current.IsTopmost) 'IsTopmost'
    Check '1c Its slot on the board says it is out' ([bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1PoppedOut')) 'StandingPanel1PoppedOut'
    Check '1d boards.json keeps where it is' ($null -ne (Get-SavedPanel 'standing').popout) 'popout saved'
    Check '1e A pop-out has no tools of its own' (-not (Find-ByAutomationId $out 'PopOutButton') -and -not (Find-ByAutomationId $out 'PanelSettingsButton')) 'no pop out or settings button inside'

    # 2. A second one.
    Invoke-PanelTool (Get-BoardWindow) 'RecordsPanel1' 'PopOutButton'
    Wait-Until { Get-PopOutFor 'RecordsPanel1' } 15 | Out-Null
    Check '2 Two pop-outs at once' ((Get-PopOutWindows).Count -eq 2) "count=$((Get-PopOutWindows).Count)"

    # 3. It updates live. Only a running battle has numbers to show: with none, the step needs a live battle and is
    # skipped, not failed. A running battle with no number still fails.
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton')
    Start-Sleep -Seconds 20
    $texts = @(Get-AllTexts (Find-PopOutPanel (Get-PopOutFor 'StandingPanel1') 'StandingPanel1'))
    # A number read from the source: not the clan's own name, and not the battle line's "ends in 3d".
    $numbers = @($texts | Where-Object { $_ -match '[0-9]' -and $_ -ne $Main -and $_ -notmatch 'ends in|ended|next read|Reads every' })
    $dash = [string][char]0x2014
    $periodLine = Line (Get-BoardWindow) 'PeriodLine'
    # The board's top line names the battle while one runs: "Reads every ..." means no battle is known, "ended" one that is over.
    $battleRuns = $periodLine -and $periodLine -ne '(absent)' -and $periodLine -notlike 'Reads every*' -and $periodLine -notmatch '\bended\b'
    $noBattle = ($periodLine -like 'Reads every*') -or ($periodLine -match '\bended\b') -or
        ($numbers.Count -eq 0 -and @($texts | Where-Object { $_ -eq $dash }).Count -gt 0)
    # The board's state line, which after a Test now on a stopped board says what the read found: a source in
    # trouble is named first ("Last read of CCGP: Could not reach the data."), and an idle clan is not trouble
    # ("Nothing to read right now"). That is the one thing the panel's own text cannot say - a broken read and
    # no battle both draw dashes - and it closes the last hole in this step without leaving the board (S2-FR.3).
    $stateLine = Line (Get-BoardWindow) 'StateLine'
    $readBroken = $stateLine -match 'Last read of .+: (Could not reach|Nothing matched|The response was not|The source asked|The source wants|A key|The source rejected|Waiting for)'
    $seen = "period line '$periodLine'; state '$stateLine'; " + ($texts -join ' | ')
    if ($readBroken) {
        Check '3 The pop-out shows numbers after a read' $false "the read is broken, whatever the battle: $seen"
    }
    elseif ($numbers.Count -eq 0 -and -not $battleRuns -and $noBattle) {
        Skip '3 The pop-out shows numbers after a read' 'needs a live battle' "no battle is running: $seen"
    }
    else {
        Check '3 The pop-out shows numbers after a read' ($numbers.Count -gt 0) $seen
    }
    & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Clan standing' -OutPath (Join-Path $UrShots 'pop-out.png') | Out-Null

    # 4. A moved window's place is saved.
    $beforeX = (Get-SavedPanel 'standing').popout.x
    (Get-PopOutFor 'StandingPanel1').GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Move(120, 140)
    Start-Sleep -Seconds 2
    $afterX = (Get-SavedPanel 'standing').popout.x
    Check '4 Moving it saves the new place' ($afterX -ne $beforeX) "x: $beforeX -> $afterX"

    # 5. A restart reopens both where they were.
    Stop-UrScoreFromBoard
    Start-UrScore | Out-Null
    Wait-Until { (Get-PopOutWindows).Count -eq 2 } 30 | Out-Null
    Check '5 Both pop-outs reopen' ((Get-PopOutWindows).Count -eq 2) "count=$((Get-PopOutWindows).Count)"
    Check '5b ...where they were' ((Get-SavedPanel 'standing').popout.x -eq $afterX) "x=$((Get-SavedPanel 'standing').popout.x)"

    # 5c/5d (S2-8.6). A pop-out moved to the second screen reopens on it, at the same place, after a restart. The app
    # is system-DPI aware, not per-monitor, so on a second screen with a different scale Windows stretches the window;
    # what this pins is that the saved place still comes back as the same pixels. Each screen's DPI is recorded, so the
    # step says which case it verified: two scales, or one.
    $screens = @(Get-UrScreens)
    $other = $screens | Where-Object { -not $_.Primary } | Select-Object -First 1
    $dpis = ($screens | ForEach-Object { "$($_.Width)x$($_.Height)@$($_.Dpi)dpi" }) -join ', '
    if ($null -eq $other) {
        Skip '5c A pop-out moved to the second screen reopens there' 'needs a second screen' "screens: $dpis"
    }
    else {
        $window = Get-PopOutFor 'StandingPanel1'
        $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Move($other.X + 80, $other.Y + 80)
        Start-Sleep -Seconds 2
        $moved = (Get-PopOutFor 'StandingPanel1').Current.BoundingRectangle
        Stop-UrScoreFromBoard
        Start-UrScore | Out-Null
        Wait-Until { (Get-PopOutWindows).Count -eq 2 } 30 | Out-Null
        $back = (Get-PopOutFor 'StandingPanel1').Current.BoundingRectangle
        $centreX = $back.X + $back.Width / 2
        $centreY = $back.Y + $back.Height / 2
        $onOther = $centreX -ge $other.X -and $centreX -lt ($other.X + $other.Width) -and $centreY -ge $other.Y -and $centreY -lt ($other.Y + $other.Height)
        $mixed = @($screens | Select-Object -ExpandProperty Dpi -Unique).Count -gt 1
        $case = if ($mixed) { 'two scales' } else { 'one scale, so the mixed-DPI half stays unverified' }
        Check '5c A pop-out moved to the second screen reopens on it' $onOther "after restart: $($back.X),$($back.Y) $($back.Width)x$($back.Height); screens: $dpis ($case)"
        Check '5d ...at the same place, within 8 px' ([math]::Abs($back.X - $moved.X) -le 8 -and [math]::Abs($back.Y - $moved.Y) -le 8) "moved to $($moved.X),$($moved.Y); back at $($back.X),$($back.Y)"
        # Back to the primary, so the steps that follow see what they always saw.
        (Get-PopOutFor 'StandingPanel1').GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Move(120, 140)
        Start-Sleep -Seconds 2
    }

    # 6. Closing a pop-out returns its panel.
    Invoke-Element (Find-ByAutomationId (Get-PopOutFor 'StandingPanel1') 'ReturnPanelButton')
    Start-Sleep -Seconds 1
    Check '6 Closing it returns the panel' (-not (Get-PopOutFor 'StandingPanel1') -and [bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1')) 'StandingPanel1 back on the board'
    Check '6b ...and boards.json forgets its place' ($null -eq (Get-SavedPanel 'standing').popout) 'no popout'

    # 7. Bring back returns the other.
    Invoke-Element (Get-Button (Get-BoardWindow) 'Bring back Records')
    Start-Sleep -Seconds 1
    Check '7 Bring back closes its window' ((Get-PopOutWindows).Count -eq 0) "count=$((Get-PopOutWindows).Count)"

    & (Join-Path $PSScriptRoot 'check-boards-privacy.ps1') | Out-Host
    $privacy = $LASTEXITCODE
    Check '8 boards.json holds no other player' ($privacy -eq 0) "exit=$privacy"
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Note-RoRoRo 'after'
    Show-Results
}
exit $LASTEXITCODE
