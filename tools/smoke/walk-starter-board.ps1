# The starter boards on a clean data folder: a main clan, a clan your accounts are in, a watched clan, (when
# the fixture exists) the top list and the profile recipe with its suggestions, then two tabs, Battle first, and
# every Battle panel with its title and no account card or table on it, Alts as its own tab, Start, Test now and
# Pause through the status card, and a change to Alts that leaves Battle following.
param(
    [string]$Main = 'CCGP',
    [string]$Alt = 'K0i2'
)

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$clanFixture = Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json'
$profileFixture = Join-Path $UrFixtures 'petsim99-profile.recipe.json'
$boardsFile = Join-Path $UrData 'boards.json'
$topFixture = Get-ChildItem $UrFixtures -Filter *.recipe.json | Where-Object { (Get-Content $_.FullName -Raw) -match '"groupName"' } | Select-Object -First 1
$rororo = [bool](Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue)
$backup = $null

try {
    $backup = Move-UrDataAside
    Note-RoRoRo 'before'
    Start-UrScore | Out-Null

    $board = Get-BoardWindow
    Invoke-Element (Find-ByAutomationId $board 'SetupButton')
    Wait-UrWindow '^Setup$' 15 | Out-Null
    $setup = Complete-ClanImport $clanFixture @('Points') @('Points')
    $setup = Wait-UrWindow '^Setup$' 30
    Select-SearchName $setup 'Your main clan' $Main
    Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null
    Invoke-WhenReady $setup 'AddMineButton'
    Select-SearchName $setup 'Add a clan your accounts are in' $Alt
    Wait-Line $setup 'MineFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null
    Invoke-WhenReady $setup 'WatchClanButton'
    Select-FirstSearchMatch $setup 'Watch a clan' 'an' @($Main, $Alt) | Out-Null

    if ($topFixture) {
        Start-Import $topFixture.FullName
        $screen = Wait-UrWindow '^Import recipe$' 30
        Invoke-WhenReady $screen 'ImportButton'
        Start-Sleep -Seconds 2
    }

    Start-Import $profileFixture
    $screen = Wait-UrWindow '^Import recipe$' 30
    Invoke-WhenReady $screen 'ImportButton'
    Start-Sleep -Seconds 2

    Close-UrWindow (Get-SetupWindow)
    $board = Get-BoardWindow

    $expected = [ordered]@{
        'StandingPanel1'       = 'Clan standing'
        'StandingPanel2'       = 'Clan standing'
        'RacePanel1'           = 'Battle race'
        'PromotionCheckPanel1' = 'Promotion check'
        'MyAccountsPanel1'     = 'My accounts'
        'PastPeriodsPanel1'    = 'Past battles'
        'RecordsPanel1'        = 'Records'
    }
    if ($topFixture) { $expected['TopPanel1'] = 'Top of the battle' }

    # Records is the last panel the Battle starter adds, so waiting for its bound title is a proxy for the
    # whole board having finished rendering -- reading a panel's text right after it merely appears in the
    # tree can race its data binding.
    Wait-Until {
        $records = Find-ByAutomationId (Get-BoardWindow) 'RecordsPanel1'
        $records -and (Line $records 'PanelTitle') -eq 'Records'
    } 20 | Out-Null
    $board = Get-BoardWindow
    foreach ($id in $expected.Keys) {
        $panel = Find-ByAutomationId $board $id
        Check "1 $id is on the board" ($panel -and (Line $panel 'PanelTitle') -eq $expected[$id]) "title='$(Line $panel 'PanelTitle')'"
    }
    $tabs = @(Get-TabNames $board)
    Check '1b Two tabs, Battle first' ($tabs.Count -eq 2 -and $tabs[0] -eq 'Battle' -and $tabs[1] -eq 'Alts') ($tabs -join ', ')
    Check '1d Battle has no account card or table' (-not (Find-ByAutomationId $board 'AccountCardPanel1') -and -not (Find-ByAutomationId $board 'AccountsTablePanel1')) (@(Get-PanelIds $board) -join ',')
    Check '1c The promotion check names both clans' ((Line (Find-ByAutomationId $board 'PromotionCheckPanel1') 'PanelSubtitle') -match "^$Alt .+ $Main$") (Line (Find-ByAutomationId $board 'PromotionCheckPanel1') 'PanelSubtitle')

    Invoke-Element (Find-ByAutomationId $board 'StartStopButton')
    $period = Wait-Line $board 'PeriodLine' 'next read' 240
    Check '2 Start reads, and the period line gains the next read' ($period -match 'next read') $period
    $state = Wait-Line $board 'StateLine' '^(Reading \d+ sources?\.|.+: )' 60
    Check '2b The state line counts the sources or names one in trouble' ($state -match '^(Reading \d+ sources?\.|.+: )') $state

    Invoke-Element (Find-ByAutomationId $board 'TestNowButton')
    # Test now is disabled for exactly as long as its read runs (several sources, 2 s apart per host), so wait on
    # the button rather than a fixed sleep. The press lands on the dispatcher after Invoke returns, so first let
    # it go disabled. Whether it DID is its own step: the walk used to throw that away, so a button that never
    # started a read at all still passed the next line (S1-L.1). Two sources on one host are two seconds of read
    # at least, and the poll is every 400 ms, so the disabled moment is there to be seen.
    $wentDisabled = Wait-Until { -not (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 10
    Check '2c Test now goes disabled while its read runs' $wentDisabled "TestNowButton went disabled=$wentDisabled"
    $tested = Wait-Until { (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 240
    Check '2d Test now finishes and takes a press again' $tested "TestNowButton enabled=$tested"
    $accounts = @(Get-AllTexts (Find-ByAutomationId $board 'MyAccountsPanel1'))
    # Every heading My accounts can file an account under. Only once every source has a reading from this session may it
    # say 'Not in a watched clan'; before that it says how much has been read (PanelText.NotFound), and a main clan with no
    # live battle is not 'read now'. The step proves the panel groups your accounts, so any true heading counts.
    # A clan read between battles WAS read, so its accounts are headed by that fact rather than "No clans read yet"
    # (V3-S.18) - and the owner's main clan is between battles nearly always, so these are the common case.
    $headings = @('Not in a watched clan', 'Not found in the clans read so far', 'No clans read yet',
                  "Only in clans you're watching", 'Not matched by RoRoRo yet',
                  'No clan is in a battle right now', 'No clan read so far is in a battle')
    $grouped = @($accounts | Where-Object { $_ -like "*$Main" -or $_ -eq $Alt -or $headings -contains $_ }).Count -gt 0
    # With RoRoRo quit - the rule for an owner-watched launch since 2026-09-22 - there are no accounts to group and the
    # panel rightly shows none, so this step can only be judged with RoRoRo up. Seen first on that day's launch.
    if (-not $script:RoRoRoUpBefore -and -not $grouped) {
        Skip '3 My accounts groups your accounts by clan' 'needs RoRoRo running' "no accounts listed: $($accounts -join ' | ')"
    }
    else {
        Check '3 My accounts groups your accounts by clan' $grouped ($accounts -join ' | ')
    }

    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'starter-board.png') | Out-Null

    # 5. Alts is a tab of its own.
    Select-Tab (Get-BoardWindow) 'Alts'
    Check '5 Alts shows the accounts table' ([bool](Find-ByAutomationId (Get-BoardWindow) 'AccountsTablePanel1')) (@(Get-PanelIds (Get-BoardWindow)) -join ',')
    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'alts-tab.png') | Out-Null
    Check '5b No boards.json while both tabs follow' (-not (Test-Path $boardsFile)) "exists=$(Test-Path $boardsFile)"

    # 6. Changing Alts writes Alts; Battle keeps following.
    Enter-EditMode (Get-BoardWindow)
    Invoke-PanelTool (Get-BoardWindow) 'RecordsPanel1' 'RemovePanelButton'
    Complete-EditMode (Get-BoardWindow)
    $saved = @(Read-Boards)
    $battleEntry = $saved | Where-Object { $_.id -eq 'b-starter-battle' } | Select-Object -First 1
    $altsEntry = $saved | Where-Object { $_.id -eq 'b-starter-alts' } | Select-Object -First 1
    Check '6 boards.json keeps Battle following, with no panels' ($battleEntry -and $battleEntry.follows -eq 'battle' -and @($battleEntry.panels).Count -eq 0) ($saved | ConvertTo-Json -Depth 2 -Compress)
    Check '6b ...and Alts as you left it' ($altsEntry -and -not $altsEntry.follows -and @($altsEntry.panels).Count -eq 2) "alts panels=$(@($altsEntry.panels).Count)"
    Select-Tab (Get-BoardWindow) 'Battle'
    Check '6c Battle still shows its panels' ([bool](Find-ByAutomationId (Get-BoardWindow) 'RacePanel1')) (@(Get-PanelIds (Get-BoardWindow)) -join ',')

    Invoke-PauseResume | Out-Null
    $stopped = Wait-Line (Get-BoardWindow) 'StateLine' '^Paused\.' 20
    Check '4 Pause pauses' ($stopped -match '^Paused\.') $stopped
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Note-RoRoRo 'after'
    Show-Results
    "RoRoRo running: $rororo"
}
exit $LASTEXITCODE
