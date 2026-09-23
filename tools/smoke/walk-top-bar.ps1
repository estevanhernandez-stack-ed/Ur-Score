# The top bar after testing (spec 3, BC1-BC3, BC8): the chip starts a board that never read, opens a card without
# changing anything, pauses from inside the card, shows Paused in the title and the line; the read-now button and
# F5 read; Esc closes the card; a paused board closes without asking; start on open reads by itself. Also grabs the
# 1024 DIP, three-tab screenshots spec 7 wants of the bar in each chip state.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$backup = $null

function Chip { (Find-ByAutomationId (Get-BoardWindow) 'StartStopButton').Current.Name }

try {
    $backup = Move-UrDataAside
    Note-RoRoRo 'before'
    $board = Initialize-ClanBoard $Main

    Check '1 A board that never read offers Start reading' ((Chip) -eq 'Start reading') (Chip)

    # A third tab and a resize to 1024 DIP, done once and kept for the rest of the walk: spec 7 wants the bar at
    # 1024 wide with three tabs, one shot per chip state (not started, reading, paused).
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'AddBoardButton')
    $add = Wait-UrWindow '^Add a board$' 15
    Invoke-Element (Find-ByAutomationId $add 'EmptyBoardButton')
    Start-Sleep -Seconds 1
    Select-Tab (Get-BoardWindow) 'Battle'
    (Get-BoardWindow).GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Resize(1024, 800)
    Start-Sleep -Milliseconds 500
    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'top-bar-1024-start-reading.png') | Out-Null

    Invoke-Element (Find-ByAutomationId $board 'StartStopButton')
    $live = Wait-Until { (Chip) -match '^Reading (is on|has a problem)' } 90
    Check '2 The chip starts it, and says it is reading' $live (Chip)
    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'top-bar-1024-reading.png') | Out-Null

    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'StartStopButton')
    Start-Sleep -Milliseconds 600
    $card = Find-InUrWindows 'PauseResumeButton'
    Check '3 A click opens the card' ([bool]$card) 'PauseResumeButton present'
    Check '3b ...and pauses nothing' ((Chip) -match '^Reading (is on|has a problem)') (Chip)

    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 500
    Check '4 Esc closes the card' (-not (Find-InUrWindows 'PauseResumeButton')) 'card gone'

    $name = Invoke-PauseResume
    Check '5 Pause is in the card and pauses' ((Chip) -eq 'Reading is paused. Press for status.') "chip='$(Chip)' button='$name'"
    Check '5b The title says Paused' ((Get-BoardWindow).Current.Name -eq 'RoRoRo Ur Score (Paused)') (Get-BoardWindow).Current.Name
    $line = Line (Get-BoardWindow) 'StateLine'
    Check '5c The line says what pausing costs' ($line -match '^Paused\. Nothing is read or sent') $line
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')

    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton')
    $went = Wait-Until { -not (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 10
    $back = Wait-Until { (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 240
    Check '6 The read-now button reads while paused' ($went -and $back) "went=$went back=$back"

    (Get-BoardWindow).SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{F5}')
    $f5 = Wait-Until { -not (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 10
    Wait-Until { (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 240 | Out-Null
    Check '7 F5 reads too' $f5 "f5 went=$f5"

    # Still resized from the block above, so this is the 1024/three-tab shot for the paused state too. The board
    # is paused here, so its title carries " (Paused)" (BC2) -- shot.ps1's FindWindow is an exact match, not a
    # pattern like Get-BoardWindow's, so the title has to be given exactly or the shot silently finds nothing
    # (2026-09-23 review).
    & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'RoRoRo Ur Score (Paused)' -OutPath (Join-Path $UrShots 'top-bar-paused.png') | Out-Null

    # 8. Paused closes without asking (BC8).
    Close-UrWindow (Get-BoardWindow)
    $asked = Wait-UrConfirm '^Close Ur Score$' 4
    Check '8 A paused board closes without asking' (-not $asked) "confirm=$([bool]$asked)"
    Stop-UrScore

    # 9. Start on open reads by itself (BC1): tick it the way a player would, then open again.
    Start-UrScore | Out-Null
    $setup = Open-SetupPage 'Recipes'
    Set-Tick (Get-Check $setup 'Start reading when Ur Score opens') $true
    Close-UrWindow (Get-SetupWindow)
    Stop-UrScoreFromBoard
    Start-UrScore | Out-Null
    $auto = Wait-Until { (Chip) -match '^Reading (is on|has a problem)' } 90
    Check '9 Start on open reads without a press' $auto (Chip)

    # This board is now actively reading and sending, so closing it raises the close question (constraints.md:
    # "while something is being read and sent ... a paused board closes silencing nothing" -- this one is not
    # paused). Stop-UrScoreFromBoard does not answer that question itself; left alone it would just wait out its
    # own timeout and fall back to a hard kill, which is not "closes cleanly". So the walk answers it here.
    $closingBoard = Get-BoardWindow
    if ($closingBoard) { Close-UrWindow $closingBoard }
    $closeAsked = Wait-UrConfirm '^Close Ur Score$' 5
    if ($closeAsked) { Invoke-UrConfirm $closeAsked 'Close Ur Score' }
    Stop-UrScore
    Note-RoRoRo 'after'
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
}
exit $LASTEXITCODE
