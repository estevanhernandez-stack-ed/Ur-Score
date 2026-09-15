# The starter board on a clean data folder: a main clan, a clan your accounts are in, a watched clan and (when
# the fixture exists) the top list, then every Battle panel with its title, Start, Test now and Stop.
param(
    [string]$Main = 'CCGP',
    [string]$Alt = 'K0i2'
)

. (Join-Path $PSScriptRoot 'uia-import.ps1')
$ErrorActionPreference = 'Stop'
$clanFixture = Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json'
$topFixture = Get-ChildItem $UrFixtures -Filter *.recipe.json | Where-Object { (Get-Content $_.FullName -Raw) -match '"groupName"' } | Select-Object -First 1
$rororo = [bool](Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue)
$backup = $null

try {
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null

    $board = Get-BoardWindow
    Invoke-Element (Find-ByAutomationId $board 'SetupButton')
    Wait-UrWindow '^Setup$' 15 | Out-Null
    $setup = Complete-ClanImport $clanFixture @('Points') @('Points')
    $setup = Wait-UrWindow '^Setup$' 30
    Select-SearchName $setup 'Your main clan' $Main
    Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null
    Invoke-Element (Find-ByAutomationId $setup 'AddMineButton')
    Select-SearchName $setup 'Add a clan your accounts are in' $Alt
    Wait-Line $setup 'MineFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null
    Invoke-Element (Find-ByAutomationId $setup 'WatchClanButton')
    Select-FirstSearchMatch $setup 'Watch a clan' 'an' @($Main, $Alt) | Out-Null

    if ($topFixture) {
        Start-Import $topFixture.FullName
        $screen = Wait-UrWindow '^Import recipe$' 30
        Invoke-Element (Find-ByAutomationId $screen 'ImportButton')
        Start-Sleep -Seconds 2
    }

    Close-UrWindow (Get-SetupWindow)
    $board = Get-BoardWindow

    $expected = [ordered]@{
        'StandingPanel1'       = 'Clan standing'
        'StandingPanel2'       = 'Clan standing'
        'RacePanel1'           = 'Battle race'
        'MyAccountsPanel1'     = 'My accounts'
        'PromotionCheckPanel1' = 'Promotion check'
        'AccountCardPanel1'    = 'Account card'
        'PastPeriodsPanel1'    = 'Past battles'
    }
    if ($topFixture) { $expected['TopPanel1'] = 'Top of the battle' }

    Wait-Until { [bool](Find-ByAutomationId (Get-BoardWindow) 'PastPeriodsPanel1') } 20 | Out-Null
    foreach ($id in $expected.Keys) {
        $panel = Find-ByAutomationId $board $id
        Check "1 $id is on the board" ($panel -and (Line $panel 'PanelTitle') -eq $expected[$id]) "title='$(Line $panel 'PanelTitle')'"
    }
    Check '1b The board has no Grind panels' (-not (Find-ByAutomationId $board 'ProfileStatPanel1')) 'ProfileStatPanel1 absent'
    Check '1c The promotion check names both clans' ((Line (Find-ByAutomationId $board 'PromotionCheckPanel1') 'PanelSubtitle') -match "^$Alt .+ $Main$") (Line (Find-ByAutomationId $board 'PromotionCheckPanel1') 'PanelSubtitle')

    Invoke-Element (Find-ByAutomationId $board 'StartStopButton')
    $period = Wait-Line $board 'PeriodLine' 'next read' 240
    Check '2 Start reads, and the period line gains the next read' ($period -match 'next read') $period
    $state = Wait-Line $board 'StateLine' '^(Reading \d+ sources?\.|.+: )' 60
    Check '2b The state line counts the sources or names one in trouble' ($state -match '^(Reading \d+ sources?\.|.+: )') $state

    Invoke-Element (Find-ByAutomationId $board 'TestNowButton')
    Start-Sleep -Seconds 20
    $accounts = @(Get-AllTexts (Find-ByAutomationId $board 'MyAccountsPanel1'))
    $grouped = @($accounts | Where-Object { $_ -like "*$Main" -or $_ -eq $Alt -or $_ -eq 'Not in a watched clan' }).Count -gt 0
    Check '3 My accounts groups your accounts by clan' $grouped ($accounts -join ' | ')

    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'starter-board.png') | Out-Null

    Invoke-Element (Find-ByAutomationId $board 'StartStopButton')
    $stopped = Wait-Line $board 'StateLine' '^Stopped\.$' 20
    Check '4 Stop stops' ($stopped -eq 'Stopped.') $stopped
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
    "RoRoRo running: $rororo"
}
