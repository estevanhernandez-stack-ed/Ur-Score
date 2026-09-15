# Setup > Clans on a clean data folder: the main clan, a clan your accounts are in, a watched clan,
# Make main, Remove, a repeat pick, the request line, and the Top switch when a group-list fixture exists.
param(
    [string]$Main = 'CCGP',
    [string]$Alt = 'K0i2'
)

. (Join-Path $PSScriptRoot 'uia-import.ps1')
$ErrorActionPreference = 'Stop'
$clanFixture = Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json'
$topFixture = Get-ChildItem $UrFixtures -Filter *.recipe.json | Where-Object { (Get-Content $_.FullName -Raw) -match '"groupName"' } | Select-Object -First 1
$backup = $null

function Get-SourceRoles { Read-Sources | ForEach-Object { "$($_.inputs.clan)=$(Get-RoleText $_)" } }

try {
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null
    $setup = Complete-ClanImport $clanFixture @('Points') @()
    $setup = Wait-UrWindow '^Setup$' 30

    Select-SearchName $setup 'Your main clan' $Main
    $mainLine = Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120
    Check '1 Main clan picked and read once' ($mainLine -match $Main) $mainLine

    Invoke-Element (Find-ByAutomationId $setup 'AddMineButton')
    Select-SearchName $setup 'Add a clan your accounts are in' $Alt
    $altLine = Wait-Line $setup 'MineFoundLine' '^(Found |None of your accounts|Read |Added )' 120
    Check '2 A clan your accounts are in is added and read' ($altLine -match $Alt) $altLine

    Invoke-Element (Find-ByAutomationId $setup 'WatchClanButton')
    $rival = Select-FirstSearchMatch $setup 'Watch a clan' 'an' @($Main, $Alt)
    $watchLine = Wait-Line $setup 'WatchFoundLine' '^Watching ' 20
    Check '3 A watched clan is added and says it is clan-level only' ($watchLine -match "^Watching $([regex]::Escape($rival))\.") $watchLine

    $roles = @(Get-SourceRoles)
    Check '4 sources.json holds one main, one mine and one watch' (
        ($roles -contains "$Main=main" -or $roles -contains "$Main=0") -and
        ($roles -contains "$Alt=mine" -or $roles -contains "$Alt=1") -and
        ($roles -contains "$rival=watch" -or $roles -contains "$rival=2")) ($roles -join ', ')

    Invoke-Element (Get-Button $setup "Make $Alt main")
    Start-Sleep -Seconds 1
    $roles = @(Get-SourceRoles)
    Check '5 Make main moves the star' (($roles -contains "$Alt=main" -or $roles -contains "$Alt=0") -and ($roles -contains "$Main=mine" -or $roles -contains "$Main=1")) ($roles -join ', ')
    Invoke-Element (Get-Button $setup "Make $Main main")
    Start-Sleep -Seconds 1

    Invoke-Element (Get-Button $setup "Remove $rival")
    Start-Sleep -Seconds 1
    $roles = @(Get-SourceRoles)
    Check '6 Remove takes the watched clan out' (@($roles | Where-Object { $_ -like "$rival=*" }).Count -eq 0) ($roles -join ', ')

    Select-SearchName $setup 'Your main clan' $Main
    $again = Wait-Line $setup 'MainFoundLine' 'already your main' 10
    Check '7 Picking the main clan again says so and adds nothing' ($again -eq "$Main is already your main clan.") $again

    $requests = Line $setup 'RequestsLine'
    Check '8 The request line names the host and a count per hour' ($requests -match '^Your PC asks ps99\.biggamesapi\.io about \d+ times an hour\.') $requests

    & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'setup-clans.png') | Out-Null

    if ($topFixture) {
        Close-UrWindow $setup
        Start-Import $topFixture.FullName
        $screen = Wait-UrWindow '^Import recipe$' 30
        Check '9 A group-list import shows no Stats table' (-not (Find-ByAutomationId $screen 'StatsRows')) 'StatsRows absent'
        Invoke-Element (Find-ByAutomationId $screen 'ImportButton')
        Start-Sleep -Seconds 2
        $setup = Open-SetupPage 'Clans'
        $switch = Find-ByAutomationId $setup 'TopSwitch'
        Check '9b The Top switch appears, named in the recipe''s words' ($switch -and $switch.Current.Name -eq 'Top of the battle') "switch='$($switch.Current.Name)'"
        Set-Tick $switch $false
        $top = @(Read-Sources | Where-Object { -not $_.inputs.clan })
        Check '9c Switching it off is saved at once' (($top.Count -eq 1) -and ($top[0].enabled -eq $false)) (Get-Content (Join-Path $UrData 'sources.json') -Raw)
    }
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
}
exit $LASTEXITCODE
