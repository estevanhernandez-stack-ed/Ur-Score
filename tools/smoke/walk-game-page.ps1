# Setup > Pet Sim 99 on a clean data folder: the game page shows both modes with their reads lines and the start-on-open
# tick; Battle's clans (main, mine, watched, Make main, Remove, a repeat pick, the request line, the Top switch) are
# picked inside Battle's card; turning Battle off dims the clans and takes its panels off the board ("Battle is off"
# on a panel you edited, the tab itself on the starter), and turning it back on restores them. The clan steps are the
# old walk-setup-clans.ps1's, moved here when 0.7.0 folded the Clans page into the game page.
param(
    [string]$Main = 'CCGP',
    [string]$Alt = 'K0i2'
)

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$settingsFile = Join-Path $UrData 'settings.json'
$backup = $null

function Get-SourceRoles { Read-Sources | Where-Object { $_.inputs.clan } | ForEach-Object { "$($_.inputs.clan)=$(Get-RoleText $_)" } }

# settings.json's modes map as 'key=on|off' pairs; empty while nothing has been switched (defaults stay implicit).
function Get-ModesSaved {
    if (-not (Test-Path $settingsFile)) { return @() }
    $settings = Get-Content $settingsFile -Raw | ConvertFrom-Json
    if (-not $settings.modes) { return @() }
    @($settings.modes.PSObject.Properties | ForEach-Object { "$($_.Name)=$(if ($_.Value) { 'on' } else { 'off' })" })
}

function Test-MainIs([string]$clan) {
    $roles = @(Get-SourceRoles)
    return ($roles -contains "$clan=main") -or ($roles -contains "$clan=0")
}

try {
    $backup = Move-UrDataAside
    Note-RoRoRo 'before'
    Start-UrScore | Out-Null
    $setup = Open-GamePage

    # 1-3. The page: the game, its two modes, what each reads, and the tick that starts reading on open.
    $gameSwitch = Find-ByAutomationId $setup 'GameSwitch'
    Check '1 The game switch is the page heading and is on' ((Test-Toggled $gameSwitch) -and $gameSwitch.Current.Name -eq 'Pet Sim 99') "name='$($gameSwitch.Current.Name)'"
    $battle = Find-ByAutomationId $setup 'ModeSwitch_battle'
    $profile = Find-ByAutomationId $setup 'ModeSwitch_profile'
    Check '2 Both modes are listed and on' ((Test-Toggled $battle) -and (Test-Toggled $profile) -and $battle.Current.Name -eq 'Battle' -and $profile.Current.Name -eq 'Profile') "battle='$($battle.Current.Name)' profile='$($profile.Current.Name)'"
    $reads = @(Get-AllTexts $setup | Where-Object { $_ -match '^Reads .+ every \d+ min' })
    Check '2b Each mode says what it reads, and how often' ($reads.Count -ge 2) ($reads -join ' | ')
    $startOnOpen = Find-ByAutomationId $setup 'StartOnOpenBox'
    Check '3 The page offers start on open (off in a walk folder)' ([bool]$startOnOpen -and -not (Test-Toggled $startOnOpen)) "present=$([bool]$startOnOpen)"
    Check '3b The clans sit inside Battle''s card: its search is on the page' ([bool](Get-Edit $setup 'Your main clan')) 'Your main clan'

    # 4-12. Battle's clans, as walk-setup-clans.ps1 walked them. (Its import step and its Recipes list are not ported:
    # neither exists any more.)
    Select-SearchName $setup 'Your main clan' $Main
    $mainLine = Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120
    Check '4 Main clan picked and read once' ($mainLine -match $Main) $mainLine

    Invoke-Element (Find-ByAutomationId $setup 'AddMineButton')
    Select-SearchName $setup 'Add a clan your accounts are in' $Alt
    $altLine = Wait-Line $setup 'MineFoundLine' '^(Found |None of your accounts|Read |Added )' 120
    Check '5 A clan your accounts are in is added and read' ($altLine -match $Alt) $altLine

    Invoke-Element (Find-ByAutomationId $setup 'WatchClanButton')
    $rival = Select-FirstSearchMatch $setup 'Watch a clan' 'an' @($Main, $Alt)
    $watchLine = Wait-Line $setup 'WatchFoundLine' '^Watching ' 20
    Check '6 A watched clan is added and says it is clan-level only' ($watchLine -match "^Watching $([regex]::Escape($rival))\.") $watchLine

    $roles = @(Get-SourceRoles)
    Check '7 sources.json holds one main, one mine and one watch' (
        ($roles -contains "$Main=main" -or $roles -contains "$Main=0") -and
        ($roles -contains "$Alt=mine" -or $roles -contains "$Alt=1") -and
        ($roles -contains "$rival=watch" -or $roles -contains "$rival=2")) ($roles -join ', ')

    Invoke-Element (Get-Button $setup "Make $Alt main")
    Start-Sleep -Seconds 1
    $roles = @(Get-SourceRoles)
    Check '8 Make main moves the star' ((Test-MainIs $Alt) -and ($roles -contains "$Main=mine" -or $roles -contains "$Main=1")) ($roles -join ', ')
    Invoke-Element (Get-Button $setup "Make $Main main")
    Start-Sleep -Seconds 1

    Invoke-Element (Get-Button $setup "Remove $rival")
    Start-Sleep -Seconds 1
    $roles = @(Get-SourceRoles)
    Check '9 Remove takes the watched clan out' (@($roles | Where-Object { $_ -like "$rival=*" }).Count -eq 0) ($roles -join ', ')

    Select-SearchName $setup 'Your main clan' $Main
    $again = Wait-Line $setup 'MainFoundLine' 'already your main' 10
    Check '10 Picking the main clan again says so and adds nothing' ($again -eq "$Main is already your main clan.") $again

    $requests = Line $setup 'RequestsLine'
    Check '11 The request line names the host and a count per hour' ($requests -match '^Your PC asks ps99\.biggamesapi\.io about \d+ times an hour\.') $requests

    # The Top switch belongs to the built-in top-clans list, which a fresh folder composes a watch source for. The spec's
    # open issue says it may be unreachable (the list has no inputs); when the page does not show it, the step says so.
    $switch = Find-ByAutomationId $setup 'TopSwitch'
    if ($switch) {
        Check '12 The Top switch is named in the reader''s words' ($switch.Current.Name -eq 'Top of the battle') "switch='$($switch.Current.Name)'"
        Set-Tick $switch $false
        $top = @(Read-Sources | Where-Object { -not $_.inputs.clan -and "$($_.recipe)" -like '*top-clans*' })
        Check '12b Switching it off is saved at once' (($top.Count -eq 1) -and ($top[0].enabled -eq $false)) (Get-Content (Join-Path $UrData 'sources.json') -Raw)
        Set-Tick (Find-ByAutomationId $setup 'TopSwitch') $true
    }
    else {
        Skip '12 The Top switch is named in the reader''s words' 'the page does not show the Top switch' 'TopSwitch absent (spec open issue)'
    }

    & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'game-page.png') | Out-Null

    # 13-16. Battle off, with the starter tab (unedited, following).
    Set-Tick (Find-ByAutomationId $setup 'ModeSwitch_battle') $false
    Start-Sleep -Seconds 1
    $mainBox = Get-Edit (Get-SetupWindow) 'Your main clan'
    $mineButton = Find-ByAutomationId (Get-SetupWindow) 'AddMineButton'
    $dimmed = ($mainBox -and -not $mainBox.Current.IsEnabled) -and ($mineButton -and -not $mineButton.Current.IsEnabled)
    Check '13 Battle off dims the clans: nothing in them takes a click or a key' $dimmed "search enabled=$(if ($mainBox) { $mainBox.Current.IsEnabled }) add enabled=$(if ($mineButton) { $mineButton.Current.IsEnabled })"
    Check '13b ...and Profile is untouched' (Test-Toggled (Find-ByAutomationId (Get-SetupWindow) 'ModeSwitch_profile')) 'ModeSwitch_profile on'
    $saved = @(Get-ModesSaved)
    Check '13c The switch is saved in settings.json, and only the changed key' (($saved -contains 'pet-sim-99/battle=off') -and ($saved.Count -eq 1)) ($saved -join ', ')

    Close-UrWindow (Get-SetupWindow)
    Start-Sleep -Seconds 1
    $board = Get-BoardWindow
    $tabs = @(Get-TabNames $board)
    $emptyLine = Line $board 'EmptyStateLine'
    Check '14 With Battle off the starter shows no Battle panels' (-not (Find-ByAutomationId $board 'StandingPanel1') -and (($tabs -notcontains 'Battle') -or ($emptyLine -eq 'Battle is off'))) "tabs=$($tabs -join ', ') empty-state='$emptyLine'"

    $setup = Open-GamePage
    Set-Tick (Find-ByAutomationId $setup 'ModeSwitch_battle') $true
    Start-Sleep -Seconds 1
    $mainBox = Get-Edit (Get-SetupWindow) 'Your main clan'
    Check '15 Turning it back on un-dims the clans, with what was picked still there' ($mainBox -and $mainBox.Current.IsEnabled -and (Test-MainIs $Main)) "roles: $(@(Get-SourceRoles) -join ', ')"
    Close-UrWindow (Get-SetupWindow)
    Wait-Until { (Get-TabNames (Get-BoardWindow)) -contains 'Battle' } 20 | Out-Null
    Select-Tab (Get-BoardWindow) 'Battle'
    Wait-Until { [bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1') } 20 | Out-Null
    Check '16 The Battle tab and its panels are back' ([bool](Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1')) ((Get-TabNames (Get-BoardWindow)) -join ', ')

    # 17-18. Battle off, with a board you edited: it stays on screen and each panel says why it is empty.
    Enter-EditMode (Get-BoardWindow)
    Invoke-PanelTool (Get-BoardWindow) 'RecordsPanel1' 'RemovePanelButton'
    Complete-EditMode (Get-BoardWindow)
    $setup = Open-GamePage
    Set-Tick (Find-ByAutomationId $setup 'ModeSwitch_battle') $false
    Start-Sleep -Seconds 1
    Close-UrWindow (Get-SetupWindow)
    Start-Sleep -Seconds 1
    Select-Tab (Get-BoardWindow) 'Battle'
    $standing = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1'
    $turnOn = if ($standing) { Find-ByAutomationId $standing 'TurnOnModeButton' } else { $null }
    $texts = if ($standing) { @(Get-AllTexts $standing) } else { @() }
    Check '17 An edited Battle board stays; its panel says "Battle is off." and offers Turn on' ([bool]$turnOn -and ($texts -contains 'Battle is off.')) "texts: $($texts -join ' | ')"
    if ($turnOn) { Invoke-Element $turnOn; Start-Sleep -Seconds 1 }
    $standing = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1'
    $restored = $standing -and ((Line $standing 'PanelTitle') -eq 'Clan standing') -and -not (Find-ByAutomationId $standing 'TurnOnModeButton')
    Check '18 Turn on, on the panel, restores it' $restored "title='$(if ($standing) { Line $standing 'PanelTitle' })'"
    $setup = Open-GamePage
    Check '18b ...and the switch on the page follows' (Test-Toggled (Find-ByAutomationId $setup 'ModeSwitch_battle')) 'ModeSwitch_battle'
    Close-UrWindow (Get-SetupWindow)
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Note-RoRoRo 'after'
    Show-Results
}
exit $LASTEXITCODE
