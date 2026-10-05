# The whole window in one pass, on a clean data folder: first run, Setup opening on the Pet Sim 99 page, both modes on,
# picking the main clan, the starter board, Test now, and copied diagnostics.
# 0.7.0 retired the import window, so the old steps for a refused file and the import screen are gone with it.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$rororo = [bool](Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue)
$backup = $null

try {
    $backup = Move-UrDataAside
    Note-RoRoRo 'before'
    $board = Start-UrScore

    # A fresh folder composes the three built-in readers by itself. Profile needs no clan, so its Alts tab shows; Battle
    # needs one and has no tab until a clan is picked.
    $tabs = @(Get-TabNames $board)
    Check '1 First run: Alts shows, Battle waits for a clan' (($tabs -contains 'Alts') -and ($tabs -notcontains 'Battle')) ($tabs -join ', ')

    # Replaces the old steps 2 and 3 (a refused file, the import screen): there is no file to import any more.
    Invoke-Element (Find-ByAutomationId $board 'SetupButton')
    $setup = Wait-UrWindow '^Setup$' 15
    $title = Get-SetupPageTitle $setup
    $picker = Get-UrWindows | Where-Object { $_.Current.Name -match 'Import|Open' -and $_.Current.Name -ne 'Setup' } | Select-Object -First 1
    Check '2 Setup opens on the Pet Sim 99 page, with no file picker' (($title -eq 'Pet Sim 99') -and -not $picker) "page='$title' picker=$([bool]$picker)"

    Check '3 Both modes are on' ((Test-Toggled (Find-ByAutomationId $setup 'ModeSwitch_battle')) -and (Test-Toggled (Find-ByAutomationId $setup 'ModeSwitch_profile'))) 'ModeSwitch_battle, ModeSwitch_profile'
    Check '3b The page offers start on open' ([bool](Find-ByAutomationId $setup 'StartOnOpenBox')) 'StartOnOpenBox'

    Select-SearchName $setup 'Your main clan' $Main
    $found = Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120
    Check '5 Picking the main clan reads it once and says who was found' ($found -match "^(Found .+ in $Main\.|None of your accounts are in $Main yet\.|Read $Main\.|Added $Main)") $found

    # Named $mainSources, not $main: PowerShell variable names are case-insensitive, so a local $main here
    # would be the exact same variable as the -Main parameter and clobber every use of $Main after it.
    $mainSources = @(Read-Sources | Where-Object { (Get-RoleText $_) -in @('main', '0') })
    Check '5b sources.json holds it as the main clan' (($mainSources.Count -eq 1) -and ($mainSources[0].inputs.clan -eq $Main)) (Get-Content (Join-Path $UrData 'sources.json') -Raw)

    Close-UrWindow $setup
    # The Battle tab arrives with the clan; Alts was the tab on screen, so select Battle before reading its panel.
    Wait-Until { (Get-TabNames (Get-BoardWindow)) -contains 'Battle' } 20 | Out-Null
    Select-Tab (Get-BoardWindow) 'Battle'
    $board = Get-BoardWindow
    # Wait for the panel's bound text, not just its presence in the tree, before reading it for the check.
    Wait-Until {
        $standing = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1'
        $standing -and (Line $standing 'PanelTitle') -eq 'Clan standing' -and (Line $standing 'PanelSubtitle') -eq $Main
    } 20 | Out-Null
    $standing = Find-ByAutomationId $board 'StandingPanel1'
    Check '6 The board shows the main clan standing' ((Line $standing 'PanelTitle') -eq 'Clan standing' -and (Line $standing 'PanelSubtitle') -eq $Main) "title='$(Line $standing 'PanelTitle')' subtitle='$(Line $standing 'PanelSubtitle')'"

    $finished = Wait-UrReadOnce 240
    $state = Line (Get-BoardWindow) 'StateLine'
    Check '7 Test now reads without a failure' ($finished -and $state -notmatch 'Something unexpected' -and $state -notmatch '^Reading every source') "state='$state' detail='$(Line $board 'DetailLine')'"

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
    Note-RoRoRo 'after'
    Show-Results
    "RoRoRo running: $rororo"
}
exit $LASTEXITCODE
