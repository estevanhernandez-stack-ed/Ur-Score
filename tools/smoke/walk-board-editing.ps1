# Stage 2 board editing on a clean data folder: the starter still follows your sources with no boards.json,
# edit mode moves, sizes and removes panels and Done saves them, the gallery adds one, a removed clan's panel
# says so and Choose another fixes it, + Board with Add panel, rename, duplicate and delete, and a restart.
param(
    [string]$Main = 'CCGP',
    [string]$Alt = 'K0i2'
)

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$boardsFile = Join-Path $UrData 'boards.json'
$backup = $null

function Get-SavedPanels([int]$index = 0) { @(@(Read-Boards)[$index].panels) }

try {
    $backup = Move-UrDataAside
    Note-RoRoRo 'before'
    $board = Initialize-ClanBoard $Main $Alt

    # 1. The starter still follows your sources, and nothing is written for it (R1).
    $tabs = @(Get-TabNames $board)
    Check '1 One tab, the starter' ($tabs.Count -eq 1 -and $tabs[0] -eq 'Battle') ($tabs -join ', ')
    Check '1b No boards.json yet' (-not (Test-Path $boardsFile)) "exists=$(Test-Path $boardsFile)"
    Check '1c Each panel shows panel settings' ([bool](Find-ByAutomationId (Find-ByAutomationId $board 'StandingPanel1') 'PanelSettingsButton')) 'PanelSettingsButton'

    # 2. Edit mode with no change writes nothing (R8).
    Enter-EditMode $board
    Check '2 Edit mode shows the panel tools' ([bool](Find-ByAutomationId (Find-ByAutomationId $board 'RacePanel1') 'DragHandle')) 'DragHandle'
    Check '2b The tabs are off while editing' (-not (Find-ByAutomationId $board 'BoardTabs').Current.IsEnabled) 'BoardTabs disabled'
    Complete-EditMode $board
    Check '2c Done with no change writes nothing' (-not (Test-Path $boardsFile)) "exists=$(Test-Path $boardsFile)"

    # 3. Move earlier; Done saves the order.
    $before = @(Get-PanelIds $board)
    Enter-EditMode $board
    Move-PanelEarlier $board 'RacePanel1'
    Complete-EditMode $board
    $after = @(Get-PanelIds (Get-BoardWindow))
    $was = [array]::IndexOf($before, 'RacePanel1')
    $now = [array]::IndexOf($after, 'RacePanel1')
    Check '3 The race moved one place earlier' ($was -gt 0 -and $now -eq $was - 1) "before: $($before -join ','); after: $($after -join ',')"
    $race = Get-SavedPanels | Where-Object { $_.type -eq 'race' } | Select-Object -First 1
    Check '3b boards.json holds the order' ((Test-Path $boardsFile) -and $race.order -eq $now) "race order=$($race.order)"

    # 4. Wide and tall.
    Enter-EditMode (Get-BoardWindow)
    Set-PanelSize (Get-BoardWindow) 'RacePanel1' 'Wide'
    Set-PanelTall (Get-BoardWindow) 'StandingPanel1' $true
    Start-Sleep -Milliseconds 800
    Complete-EditMode (Get-BoardWindow)
    $race = Get-SavedPanels | Where-Object { $_.type -eq 'race' } | Select-Object -First 1
    $standing = Get-SavedPanels | Where-Object { $_.type -eq 'standing' } | Select-Object -First 1
    Check '4 The race is wide' ($race.size.span -eq 12) "span=$($race.size.span)"
    Check '4b The first standing panel is tall' ($standing.size.tall -eq $true) "tall=$($standing.size.tall)"

    # 5. Remove.
    Enter-EditMode (Get-BoardWindow)
    Invoke-PanelTool (Get-BoardWindow) 'PromotionCheckPanel1' 'RemovePanelButton'
    Complete-EditMode (Get-BoardWindow)
    Check '5 The promotion check is off the board' (-not (Find-ByAutomationId (Get-BoardWindow) 'PromotionCheckPanel1')) 'PromotionCheckPanel1 absent'
    Check '5b ...and out of boards.json' (-not (Get-SavedPanels | Where-Object { $_.type -eq 'promotionCheck' })) 'no promotionCheck'

    # 6. The gallery adds a panel.
    Enter-EditMode (Get-BoardWindow)
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'AddPanelButton')
    Add-PanelFromGallery 'Live leaderboard'
    Complete-EditMode (Get-BoardWindow)
    Check '6 The gallery added a live leaderboard' ([bool](Find-ByAutomationId (Get-BoardWindow) 'LiveLeaderboardPanel1')) 'LiveLeaderboardPanel1'

    # 7. A removed clan's panel says so; Choose another fixes it (spec 9.4).
    $setup = Open-SetupPage 'Clans'
    # Removing a clan has never asked, so nothing opens here; the wait for a box that never came is gone.
    Invoke-Element (Get-Button $setup "Remove $Alt")
    Start-Sleep -Seconds 1
    Close-UrWindow (Get-SetupWindow)
    $altPanel = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel2'
    Check '7 The panel says its clan was removed' ((Line $altPanel 'PanelStale') -eq "This panel's clan was removed.") (Line $altPanel 'PanelStale')
    Invoke-Element (Find-ByAutomationId $altPanel 'ChooseAnotherButton')
    $form = Wait-UrWindow '^Panel settings$' 15
    Check '7b Choose another opens its settings' ([bool]$form) "form=$([bool]$form)"
    Select-ComboItem (Find-ByAutomationId $form 'SourceBox') "*$Main"
    Invoke-Element (Find-ByAutomationId $form 'SaveSettingsButton')
    Start-Sleep -Seconds 1
    $altPanel = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel2'
    Check '7c It now shows the main clan' ((Line $altPanel 'PanelSubtitle') -eq $Main -and (Line $altPanel 'PanelStale') -eq '(absent)') "subtitle=$(Line $altPanel 'PanelSubtitle')"

    # 8. + Board, an empty board, and its Add panel.
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'AddBoardButton')
    $add = Wait-UrWindow '^Add a board$' 15
    Invoke-Element (Find-ByAutomationId $add 'EmptyBoardButton')
    Start-Sleep -Seconds 1
    $board = Get-BoardWindow
    Check '8 A new empty board is selected' ((Get-SelectedTabName $board) -eq 'Board 2' -and (Line $board 'EmptyStateLine') -eq 'This board has no panels yet') "tab=$(Get-SelectedTabName $board) line=$(Line $board 'EmptyStateLine')"
    Invoke-Element (Find-ByAutomationId $board 'EmptyStateButton')
    Add-PanelFromGallery 'Clan standing'
    $first = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1'
    Check '8b Add panel put a clan standing on it' ((Line $first 'PanelSubtitle') -eq $Main) "subtitle=$(Line $first 'PanelSubtitle')"

    # 9. Rename, duplicate, delete.
    Invoke-TabMenu (Get-BoardWindow) 'RenameBoardItem' | Out-Null
    $rename = Wait-UrWindow '^Rename board$' 10
    Set-ElementValue (Find-ByAutomationId $rename 'BoardNameBox') 'Rivals'
    Invoke-Element (Find-ByAutomationId $rename 'SaveNameButton')
    Start-Sleep -Seconds 1
    $names = @(Get-TabNames (Get-BoardWindow))
    Check '9 Rename' ($names -contains 'Rivals' -and $names -notcontains 'Board 2') ($names -join ', ')
    Invoke-TabMenu (Get-BoardWindow) 'DuplicateBoardItem' | Out-Null
    Start-Sleep -Seconds 1
    Check '9b Duplicate adds the copy and selects it' ((Get-SelectedTabName (Get-BoardWindow)) -eq 'Rivals copy') ((Get-TabNames (Get-BoardWindow)) -join ', ')
    # Delete asks in Ur Score's own themed window (backlog V3-S.10), naming the board and what goes with it.
    Invoke-TabMenu (Get-BoardWindow) 'DeleteBoardItem' | Out-Null
    $confirm = Wait-UrConfirm '^Delete board$' 10
    $asked = Get-UrConfirmText $confirm
    Check "9c Delete asks first, in Ur Score's own window" ($asked -eq "Delete the Rivals copy board? Its panels go with it. Your score book isn't touched.") $asked
    if ($confirm) { Invoke-UrConfirm $confirm 'Delete the Rivals copy board' }
    Start-Sleep -Seconds 1
    $names = @(Get-TabNames (Get-BoardWindow))
    Check '9d ...and the board goes' ($names.Count -eq 2 -and $names -notcontains 'Rivals copy') ($names -join ', ')

    # 10. A restart keeps it all.
    Stop-UrScoreFromBoard
    $board = Start-UrScore
    $names = @(Get-TabNames $board)
    Check '10 The tabs come back, the first selected' ($names.Count -eq 2 -and $names[0] -eq 'Battle' -and $names[1] -eq 'Rivals' -and (Get-SelectedTabName $board) -eq 'Battle') ($names -join ', ')
    $ids = @(Get-PanelIds $board)
    Check '10b The order comes back' ($ids.Count -gt 1 -and $ids[1] -eq 'RacePanel1') ($ids -join ',')

    # 11. boards.json holds only your own account ids.
    & (Join-Path $PSScriptRoot 'check-boards-privacy.ps1') | Out-Host
    $privacy = $LASTEXITCODE
    Check '11 boards.json holds no other player' ($privacy -eq 0) "exit=$privacy"

    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'board-editing.png') | Out-Null
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Note-RoRoRo 'after'
    Show-Results
}
exit $LASTEXITCODE
