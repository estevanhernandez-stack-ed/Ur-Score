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
    $seen = "period line '$periodLine'; " + ($texts -join ' | ')
    if ($numbers.Count -eq 0 -and -not $battleRuns -and $noBattle) {
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
    Show-Results
}
exit $LASTEXITCODE
