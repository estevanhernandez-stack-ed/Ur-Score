# The whole window in one pass, on a clean data folder: first run, a refused file, the import screen,
# Setup opening on Clans, picking the main clan, the starter board, Test now, and copied diagnostics.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-import.ps1')
$ErrorActionPreference = 'Stop'
$clanFixture = Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json'
$invalid = Join-Path $PSScriptRoot 'checks\invalid-no-step2-url.recipe.json'
$rororo = [bool](Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue)
$backup = $null

try {
    $backup = Move-UrDataAside
    $board = Start-UrScore

    Check '1 First run asks for a recipe' ((Line $board 'EmptyStateLine') -eq 'Import a recipe to start') (Line $board 'EmptyStateLine')

    Start-Import $invalid
    $box = Get-AfterImport 10
    $text = if ($box -and $box.Current.Name -eq 'Ur Score') { (Get-MessageBoxText $box) -join ' ' } else { "(no message box: '$($box.Current.Name)')" }
    if ($box -and $box.Current.Name -eq 'Ur Score') { Close-MessageBox $box }
    Check '2 An invalid file is refused and nothing is installed' (($text -match "Step 2 has no 'url'\.") -and -not (Test-Path (Join-Path $UrData 'recipes\*.recipe.json'))) $text

    Start-Import $clanFixture
    $screen = Wait-UrWindow '^Import recipe$' 30
    $texts = @(Get-AllTexts $screen)
    Check '3 The import screen names the host, the poll and what the book keeps' (
        ($texts -contains 'ps99.biggamesapi.io') -and (@($texts -match '^Asks every \d+ seconds\.$').Count -gt 0) -and
        ($texts -contains 'KEPT IN YOUR SCORE BOOK') -and (@($texts -like '*Clan place').Count -gt 0)) ($texts -join ' | ')
    Check '3b With nothing ticked, Import is refused' ((Line $screen 'RefusalLine') -match 'Tick at least one stat') (Line $screen 'RefusalLine')

    Set-Tick (Get-Check $screen 'Show Points') $true
    Set-Tick (Get-Check $screen 'Send Points') $true
    Check '3c A Send tick shows the name RoRoRo uses' ([bool](Get-Edit $screen 'Name RoRoRo uses for Points')) 'Name RoRoRo uses for Points'
    Invoke-Element (Find-ByAutomationId $screen 'ImportButton')

    $setup = Wait-UrWindow '^Setup$' 30
    $title = Wait-Line $setup 'ClansPageTitle' '^Clans$' 30
    Check '4 A recipe with inputs opens Setup on its Clans page' ($title -eq 'Clans') "title='$title'"

    Select-SearchName $setup 'Your main clan' $Main
    $found = Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120
    Check '5 Picking the main clan reads it once and says who was found' ($found -match "^(Found .+ in $Main\.|None of your accounts are in $Main yet\.|Read $Main\.|Added $Main)") $found

    # Named $mainSources, not $main: PowerShell variable names are case-insensitive, so a local $main here
    # would be the exact same variable as the -Main parameter and clobber every use of $Main after it.
    $mainSources = @(Read-Sources | Where-Object { (Get-RoleText $_) -in @('main', '0') })
    Check '5b sources.json holds it as the main clan' (($mainSources.Count -eq 1) -and ($mainSources[0].inputs.clan -eq $Main)) (Get-Content (Join-Path $UrData 'sources.json') -Raw)

    Close-UrWindow $setup
    $board = Get-BoardWindow
    # Wait for the panel's bound text, not just its presence in the tree, before reading it for the check.
    Wait-Until {
        $standing = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1'
        $standing -and (Line $standing 'PanelTitle') -eq 'Clan standing' -and (Line $standing 'PanelSubtitle') -eq $Main
    } 20 | Out-Null
    $standing = Find-ByAutomationId $board 'StandingPanel1'
    Check '6 The board shows the main clan standing' ((Line $standing 'PanelTitle') -eq 'Clan standing' -and (Line $standing 'PanelSubtitle') -eq $Main) "title='$(Line $standing 'PanelTitle')' subtitle='$(Line $standing 'PanelSubtitle')'"

    Invoke-Element (Find-ByAutomationId $board 'TestNowButton')
    $state = Wait-Line $board 'StateLine' '^(Not started\.|Stopped\.|Reading|.+: )' 120
    Check '7 Test now reads without a failure' ($state -notmatch 'Something unexpected') "state='$state' detail='$(Line $board 'DetailLine')'"

    $setup = Open-SetupPage 'Diagnostics'
    $saved = Get-Clipboard -Raw
    Invoke-Element (Find-ByAutomationId $setup 'CopyDiagnosticsButton')
    Start-Sleep -Seconds 1
    $diag = Get-Clipboard -Raw
    if ($null -ne $saved) { Set-Clipboard -Value $saved }
    Check '8 Copied diagnostics carry no book content' (($diag -match 'no book content is included') -and ($diag -notmatch '"kind":')) (($diag -split "`n" | Select-Object -First 6) -join ' | ')
    if ($rororo) {
        Check '8b With RoRoRo running, the theme is followed' ($diag -match "THEME: following RoRoRo's theme") (($diag -split "`n" | Where-Object { $_ -match 'THEME' }) -join ' ')
    }

    Close-UrWindow $setup
    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'window-smoke-board.png') | Out-Null
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
    "RoRoRo running: $rororo"
}
exit $LASTEXITCODE
