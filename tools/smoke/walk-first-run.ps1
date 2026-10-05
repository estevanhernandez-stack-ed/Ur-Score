# First run, from an empty data folder: Setup opens on the Pet Sim 99 page with no file picker in sight, the clan search
# there already has the keyboard (a mode that asks for a clan has none picked), and picking a clan fills the Battle board.
# This is the 0.7.0 first run: no recipe to import, the three Pet Sim 99 readers are built in and composed on start.
#
# "Empty" means a folder with nothing of yours in it. Move-UrDataAside also writes the settings file every walk starts from
# (reading on open off, settingsVersion 3, no modes key so every mode is at its default), so the walk cannot start reading
# by itself; a folder with no settings file would start reading on open (the 0.6 default).
#
# Cleanup rule (a past walk deleted a real before-import-* backup): the before-import-* folders beside the data folder are
# snapshotted first, and this walk makes none (nothing here imports), so it removes none; the last step proves the set is
# unchanged rather than trusting that.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$backup = $null
$ownersAsides = @(Get-UrBeforeImportFolders)

try {
    $backup = Move-UrDataAside
    Note-RoRoRo 'before'
    $empty = @(Get-ChildItem $UrData | Where-Object { $_.Name -ne 'settings.json' })
    Check '0b The data folder starts empty but for the walk''s own settings file' ($empty.Count -eq 0) (($empty | ForEach-Object { $_.Name }) -join ', ')

    $board = Start-UrScore

    # Profile needs no clan, so its Alts tab shows from the first start; Battle asks for a clan and waits for one.
    $tabs = @(Get-TabNames $board)
    Check '1 First run: the board offers Alts, and Battle waits for a clan' (($tabs -contains 'Alts') -and ($tabs -notcontains 'Battle')) "tabs: $($tabs -join ', ')"
    Check '1b The built-in readers were composed into sources.json by themselves' (@(Read-Sources).Count -ge 2) "sources: $(@(Read-Sources | ForEach-Object { $_.recipe }) -join ', ')"

    # 2. Setup, from the board's own button: the game page, and nothing asking for a file.
    Invoke-Element (Find-ByAutomationId $board 'SetupButton')
    $setup = Wait-UrWindow '^Setup$' 15
    $title = Get-SetupPageTitle $setup
    Check '2 Setup opens on the Pet Sim 99 page' ($title -eq 'Pet Sim 99') "page='$title'"
    $others = @(Get-UrWindows | Where-Object { $_.Current.Name -notmatch '^(Setup|RoRoRo Ur Score( \(Paused\))?)$' })
    Check '2b No file picker and no import window opened' ($others.Count -eq 0) (($others | ForEach-Object { $_.Current.Name }) -join ', ')
    $nav = Find-ByAutomationId $setup 'SetupNav'
    $isItem = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::ListItem)
    $names = @(@($nav.FindAll($TS::Children, $isItem)) | ForEach-Object { $_.Current.Name })
    Check '2c Setup lists no Recipes or Clans page' (($names -notcontains 'Recipes') -and ($names -notcontains 'Clans') -and ($names -contains 'Pet Sim 99')) ($names -join ', ')

    # 3. The keyboard is in Battle's clan search without a click (A11): the page focuses it once it is drawn.
    $search = Get-Edit $setup 'Your main clan'
    $focused = Wait-Until { Test-FocusWithin (Get-Edit (Get-SetupWindow) 'Your main clan') } 8
    Check '3 The clan search has focus' ([bool]$search -and $focused) "search found=$([bool]$search) focused=$focused"

    # 4. Pick a clan: Battle has what it needs, and its tab and panels arrive.
    Select-SearchName $setup 'Your main clan' $Main
    $found = Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120
    Check '4 Picking a clan reads it once and says who was found' ($found -match "^(Found .+ in $Main\.|None of your accounts are in $Main yet\.|Read $Main\.|Added $Main)") $found
    $mainSources = @(Read-Sources | Where-Object { (Get-RoleText $_) -in @('main', '0') })
    Check '4b sources.json holds it as the main clan' (($mainSources.Count -eq 1) -and ($mainSources[0].inputs.clan -eq $Main)) (Get-Content (Join-Path $UrData 'sources.json') -Raw)

    Close-UrWindow (Get-SetupWindow)
    Wait-Until { (Get-TabNames (Get-BoardWindow)) -contains 'Battle' } 20 | Out-Null
    Select-Tab (Get-BoardWindow) 'Battle'
    Wait-Until {
        $standing = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1'
        $standing -and (Line $standing 'PanelTitle') -eq 'Clan standing' -and (Line $standing 'PanelSubtitle') -eq $Main
    } 30 | Out-Null
    $board = Get-BoardWindow
    $standing = Find-ByAutomationId $board 'StandingPanel1'
    Check '5 The Battle board fills: a Battle tab, with the main clan standing on it' ((@(Get-TabNames $board) -contains 'Battle') -and $standing -and (Line $standing 'PanelSubtitle') -eq $Main) "tabs: $((Get-TabNames $board) -join ', '); panels: $(@(Get-PanelIds $board) -join ',')"
    Check '5b ...and its other starter panels came with it' ([bool](Find-ByAutomationId $board 'RacePanel1') -and [bool](Find-ByAutomationId $board 'RecordsPanel1')) (@(Get-PanelIds $board) -join ',')

    # 6. Nothing was imported, so no asides were made and none of the owner's were touched.
    $now = @(Get-UrBeforeImportFolders)
    $came = @($now | Where-Object { $ownersAsides -notcontains $_ })
    $went = @($ownersAsides | Where-Object { $now -notcontains $_ })
    Check '6 No before-import folder came or went' (($came.Count -eq 0) -and ($went.Count -eq 0)) "before=$($ownersAsides.Count) now=$($now.Count)"

    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'first-run-board.png') | Out-Null
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Note-RoRoRo 'after'
    Show-Results
}
exit $LASTEXITCODE
