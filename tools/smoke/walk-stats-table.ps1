# The Stats table on a clean data folder: the import screen's table and search, ticked rows staying visible,
# the name column, Setup > Stats reading names once, saving, and "tick at least one stat".
. (Join-Path $PSScriptRoot 'uia-import.ps1')
$ErrorActionPreference = 'Stop'
$profileFixture = Join-Path $UrFixtures 'petsim99-profile.recipe.json'
$rororo = [bool](Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue)
$backup = $null

try {
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null
    Start-Import $profileFixture
    $screen = Wait-UrWindow '^Import recipe$' 30

    # 1b. A first import ticks what the recipe suggests, to show only; untick them so the rest of this
    # walk starts from a clean table, as it did before D11.
    $suggested = @('Diamonds', 'Eggs hatched', 'Player rank', 'Rebirths', 'Different pets hatched', 'Goals completed', 'Playtime')
    $ticked = @($suggested | Where-Object { $c = Get-Check $screen "Show $_"; $c -and $c.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On })
    Check '1b A first import starts with the suggested stats shown' ($ticked.Count -eq $suggested.Count) ($ticked -join ', ')
    foreach ($label in $suggested) { Set-Tick (Get-Check $screen "Show $label") $false }

    $read = Find-ByAutomationId $screen 'ReadNamesButton'
    Check '1 A counters recipe offers one read of every game statistic' ($read -and $read.Current.Name -like 'Show every game statistic*') "button='$($read.Current.Name)'"

    Set-ElementValue (Find-ByAutomationId $screen 'StatsSearchBox') 'EGGS'
    $showing = Wait-Line $screen 'ShowingLine' '^Showing ' 5
    Check '2 Search filters by label ignoring case' (($showing -match '^Showing 1 of 16 ') -and [bool](Get-Check $screen 'Show Eggs hatched') -and -not (Get-Check $screen 'Show Diamonds')) $showing

    Set-Tick (Get-Check $screen 'Show Eggs hatched') $true
    Set-ElementValue (Find-ByAutomationId $screen 'StatsSearchBox') 'dia'
    $showing = Wait-Line $screen 'ShowingLine' '^Showing 3 of 16 ' 5
    Check '3 A ticked row stays visible under another search' (($showing -match '^Showing 3 of 16 ') -and [bool](Get-Check $screen 'Show Eggs hatched')) $showing

    Check '4 No name column before Send' (-not (Get-Edit $screen 'Name RoRoRo uses for Eggs hatched')) 'absent'
    Set-Tick (Get-Check $screen 'Send Eggs hatched') $true
    $name = Get-Edit $screen 'Name RoRoRo uses for Eggs hatched'
    Check '4b Send shows the pinned name' ($name -and $name.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'ps99.eggs-hatched') 'ps99.eggs-hatched'
    Check '4c The slot line counts history slots' ((Line $screen 'SlotLine') -match "of RoRoRo's 256 history slots") (Line $screen 'SlotLine')

    Set-ElementValue (Find-ByAutomationId $screen 'StatsSearchBox') ''
    Invoke-Element (Find-ByAutomationId $screen 'ImportButton')
    Start-Sleep -Seconds 2

    $state = Get-Content (Join-Path $UrData 'recipes\pet-sim-99-profile.state.json') -Raw | ConvertFrom-Json
    Check '5 Import saved the ticks' (($state.stats.eggs.show -eq $true) -and ($state.stats.eggs.send -eq $true)) ($state.stats | ConvertTo-Json -Compress)

    $setup = Open-SetupPage 'Stats'
    if ($rororo) {
        $names = Wait-Line $setup 'NamesLine' '^(Found \d|No statistic names came back|RoRoRo hasn|Could not|The source answered|[\d,]+ statistic names from)' 120
        Check '6 Setup > Stats reads counter names once when none are saved' ($names -notmatch '^Reading stat names once') $names
    }

    Set-Tick (Get-Check $setup 'Show Diamonds') $true
    Set-Tick (Get-Check $setup 'Show Eggs hatched') $false
    Set-Tick (Get-Check $setup 'Send Eggs hatched') $false
    Invoke-Element (Find-ByAutomationId $setup 'SaveStatsButton')
    $saved = Wait-Line $setup 'StatsSavedLine' '^Saved\.' 5
    $state = Get-Content (Join-Path $UrData 'recipes\pet-sim-99-profile.state.json') -Raw | ConvertFrom-Json
    Check '7 Saving applies the new ticks' (($saved -like 'Saved.*') -and ($state.stats.diamonds.show -eq $true) -and ($state.stats.eggs.show -eq $false)) "$saved / $($state.stats | ConvertTo-Json -Compress)"

    Set-Tick (Get-Check $setup 'Show Diamonds') $false
    $refusal = Line $setup 'RefusalLine'
    $save = Find-ByAutomationId $setup 'SaveStatsButton'
    Check '8 Nothing ticked asks for one stat and Save is off' (($refusal -match 'Tick at least one stat') -and -not $save.Current.IsEnabled) $refusal

    & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'setup-stats.png') | Out-Null
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
    "RoRoRo running: $rororo"
}
exit $LASTEXITCODE
