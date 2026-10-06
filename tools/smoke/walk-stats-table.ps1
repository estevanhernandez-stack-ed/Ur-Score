# The Stats table on a clean data folder: the suggested ticks a fresh install starts with, search, ticked rows staying
# visible, the name column, Setup > Stats reading names once, saving, and "tick at least one stat".
# 0.7.0 retired the import screen this walk used to start on: the same table lives on Setup > Stats, so every step here
# runs there, on the Profile reader (the Mode box picks which mode's stats the table shows).
. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$rororo = [bool](Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue)
$backup = $null

function Get-ProfileState { Get-Content (Join-Path $UrData 'recipes\pet-sim-99-profile.state.json') -Raw | ConvertFrom-Json }

# The Mode box picks which mode's stats the table shows; with one mode listed it may not be there, and then there is nothing to pick.
function Select-StatsMode($setup, [string]$mode) {
    $box = Find-All $setup $CT::ComboBox | Where-Object { $_.Current.Name -eq 'Mode' } | Select-Object -First 1
    if ($box) { Select-ComboItem $box $mode }
    return Get-SetupWindow
}

try {
    $backup = Move-UrDataAside
    Note-RoRoRo 'before'
    Start-UrScore | Out-Null
    $setup = Open-SetupPage 'Stats'
    $setup = Select-StatsMode $setup 'Profile'

    # 1b. A fresh install seeds what the reader suggests, to show only (not written until you change something); untick
    # them so the rest of this walk starts from a clean table, as it did before D11.
    $suggested = @('Diamonds', 'Eggs hatched', 'Player rank', 'Rebirths', 'Different pets hatched', 'Goals completed', 'Playtime')
    $ticked = @($suggested | Where-Object { Test-Toggled (Get-Check $setup "Show $_") })
    $sent = @($suggested | Where-Object { Test-Toggled (Get-Check $setup "Send $_") })
    Check '1b A fresh install starts with the suggested stats shown, none sent' (($ticked.Count -eq $suggested.Count) -and ($sent.Count -eq 0)) "shown: $($ticked -join ', '); sent: $($sent -join ', ')"
    foreach ($label in $suggested) { Set-Tick (Get-Check $setup "Show $label") $false }

    $read = Find-ByAutomationId $setup 'ReadNamesButton'
    Check '1 A counters reader offers to read every game statistic again (the Stats page''s own button since 0.7.0)' ($read -and $read.Current.Name -like 'Read stat names again*') "button='$($read.Current.Name)'"

    Set-ElementValue (Find-ByAutomationId $setup 'StatsSearchBox') 'EGGS'
    $showing = Wait-Line $setup 'ShowingLine' '^Showing ' 5
    Check '2 Search filters by label ignoring case' (($showing -match '^Showing 1 of 16 ') -and [bool](Get-Check $setup 'Show Eggs hatched') -and -not (Get-Check $setup 'Show Diamonds')) $showing

    Set-Tick (Get-Check $setup 'Show Eggs hatched') $true
    Set-ElementValue (Find-ByAutomationId $setup 'StatsSearchBox') 'dia'
    $showing = Wait-Line $setup 'ShowingLine' '^Showing 3 of 16 ' 5
    Check '3 A ticked row stays visible under another search' (($showing -match '^Showing 3 of 16 ') -and [bool](Get-Check $setup 'Show Eggs hatched')) $showing

    Check '4 No name column before Send' (-not (Get-Edit $setup 'Name RoRoRo uses for Eggs hatched')) 'absent'
    Set-Tick (Get-Check $setup 'Send Eggs hatched') $true
    $name = Get-Edit $setup 'Name RoRoRo uses for Eggs hatched'
    Check '4b Send shows the pinned name' ($name -and $name.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'ps99.eggs-hatched') 'ps99.eggs-hatched'
    Check '4c The slot line counts history slots' ((Line $setup 'SlotLine') -match "of RoRoRo's 256 history slots") (Line $setup 'SlotLine')

    Set-ElementValue (Find-ByAutomationId $setup 'StatsSearchBox') ''
    # 5. Was "Import saved the ticks"; the table's own Save stats is the same write now.
    Invoke-Element (Find-ByAutomationId $setup 'SaveStatsButton')
    $saved = Wait-Line $setup 'StatsSavedLine' '^Saved\.' 5
    $state = Get-ProfileState
    Check '5 Save stats wrote the ticks to the mode''s state file' (($saved -like 'Saved.*') -and ($state.stats.eggs.show -eq $true) -and ($state.stats.eggs.send -eq $true)) ($state.stats | ConvertTo-Json -Compress)

    # Re-open the page: the Mode box starts on its own default again, so pick Profile once more.
    $setup = Open-SetupPage 'Stats'
    $setup = Select-StatsMode $setup 'Profile'
    if ($rororo) {
        $names = Wait-Line $setup 'NamesLine' '^(Found \d|No statistic names came back|RoRoRo hasn|Could not|The source answered|[\d,]+ statistic names from)' 120
        Check '6 Setup > Stats reads counter names once when none are saved' ($names -notmatch '^Reading stat names once') $names
    }

    Set-Tick (Get-Check $setup 'Show Diamonds') $true
    Set-Tick (Get-Check $setup 'Show Eggs hatched') $false
    Set-Tick (Get-Check $setup 'Send Eggs hatched') $false
    Invoke-Element (Find-ByAutomationId $setup 'SaveStatsButton')
    $saved = Wait-Line $setup 'StatsSavedLine' '^Saved\.' 5
    $state = Get-ProfileState
    Check '7 Saving applies the new ticks' (($saved -like 'Saved.*') -and ($state.stats.diamonds.show -eq $true) -and ($state.stats.eggs.show -eq $false)) "$saved / $($state.stats | ConvertTo-Json -Compress)"

    Set-Tick (Get-Check $setup 'Show Diamonds') $false
    $refusal = Line $setup 'RefusalLine'
    $save = Find-ByAutomationId $setup 'SaveStatsButton'
    Check '8 Nothing ticked asks for one stat and Save is off' (($refusal -match 'Tick at least one stat') -and -not $save.Current.IsEnabled) $refusal

    & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'setup-stats.png') | Out-Null
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Note-RoRoRo 'after'
    Show-Results
    "RoRoRo running: $rororo"
}
exit $LASTEXITCODE
