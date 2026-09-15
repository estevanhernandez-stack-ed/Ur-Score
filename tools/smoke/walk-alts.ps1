# The Alts tab on a clean data folder: a first import of the profile recipe starts with its suggested stats ticked to
# show and none to send; the board opens on Alts alone; the accounts table has a column per shown stat, sorted by the
# first with its change; a heading click sorts and a second flips; picking a row fills the account card; the total
# sums what adds up; and none of it writes boards.json. Steps 4 to 7 need RoRoRo running and listing your accounts.
. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$profileFixture = Join-Path $UrFixtures 'petsim99-profile.recipe.json'
$rororo = [bool](Get-Process -Name 'ROROROblox.App' -ErrorAction SilentlyContinue)
$boardsFile = Join-Path $UrData 'boards.json'
$down = [string][char]0x2193
$up = [string][char]0x2191
$dash = [string][char]0x2014
$backup = $null

function Get-Toggle($root, [string]$name) {
    $box = Get-Check $root $name
    $box -and ($box.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On)
}

try {
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null

    # 1. A first import ticks what the recipe suggests, to show only, and says so.
    Start-Import $profileFixture
    $screen = Wait-UrWindow '^Import recipe$' 30
    $suggested = @('Diamonds', 'Eggs hatched', 'Player rank', 'Rebirths', 'Different pets hatched', 'Goals completed', 'Playtime')
    $shown = @($suggested | Where-Object { Get-Toggle $screen "Show $_" })
    $sent = @($suggested | Where-Object { Get-Toggle $screen "Send $_" })
    Check '1 The suggested stats start ticked to show' ($shown.Count -eq $suggested.Count) ($shown -join ', ')
    Check '1b ...and none to send' ($sent.Count -eq 0) ($sent -join ', ')
    $line = Line $screen 'SuggestedLine'
    Check '1c The screen says why, and that nothing is sent' ($line -like 'The recipe suggests showing *Nothing is sent to RoRoRo unless you tick Send.') $line
    Invoke-Element (Find-ByAutomationId $screen 'ImportButton')
    Start-Sleep -Seconds 2
    Close-UrWindow (Get-SetupWindow)

    # 2. Only Alts: there is no battle recipe to build Battle from.
    Wait-Until { (Get-TabNames (Get-BoardWindow)) -contains 'Alts' } 20 | Out-Null
    $board = Get-BoardWindow
    $tabs = @(Get-TabNames $board)
    Check '2 The board opens on the Alts tab alone' ($tabs.Count -eq 1 -and $tabs[0] -eq 'Alts') ($tabs -join ', ')
    $ids = @(Get-PanelIds $board)
    Check '2b The accounts table, then records and the account card' (($ids -join ',') -eq 'AccountsTablePanel1,RecordsPanel1,AccountCardPanel1') ($ids -join ',')
    Check '2c The table is titled' ((Line (Find-ByAutomationId $board 'AccountsTablePanel1') 'PanelTitle') -eq 'Accounts table') (Line (Find-ByAutomationId $board 'AccountsTablePanel1') 'PanelTitle')

    # 3. A column per shown stat, sorted by the first, its change beside it.
    $headers = @(Get-GridHeaders (Get-AccountsGrid))
    $expected = @('Account', "Diamonds $down", 'Today', '7 days', 'Eggs hatched', 'Player rank', 'Rebirths', 'Different pets hatched', 'Goals completed', 'Playtime')
    Check '3 A column per shown stat, the first sorted with its change' (($headers -join '|') -eq ($expected -join '|')) ($headers -join ' | ')

    if (-not $rororo) {
        Skip '4 Test now fills a row per account and a total' 'needs RoRoRo' 'RoRoRo is not running'
    }
    else {
        # 4. A row per account, then the total; playtime reads as a duration.
        Invoke-Element (Find-ByAutomationId $board 'TestNowButton')
        Wait-Until { -not (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 10 | Out-Null
        Wait-Until { (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 240 | Out-Null
        $rows = @(Get-GridRows (Get-AccountsGrid))
        $names = @($rows | ForEach-Object { $_.Current.Name })
        Check '4 A row per account, then the total' ($rows.Count -ge 2 -and $names[-1] -eq 'Total') ($names -join ', ')
        $playtimes = @($rows | Select-Object -SkipLast 1 | ForEach-Object { @(Get-AllTexts $_)[-1] })
        $timeLike = @($playtimes | Where-Object { $_ -match '^(\d[\d,]*d \d+h|\d+h \d+m|\d+m)$' -or $_ -eq $dash })
        Check '4b Playtime reads as a duration' ($playtimes.Count -gt 0 -and $timeLike.Count -eq $playtimes.Count) ($playtimes -join ' | ')

        # 5. A heading click sorts by it and its change follows it; a second click flips it.
        Invoke-GridHeader (Get-AccountsGrid) 'Player rank'
        $headers = @(Get-GridHeaders (Get-AccountsGrid))
        Check '5 Player rank sorts highest first, its change beside it' ((($headers -join '|') -like "*|Player rank $down|Today|7 days|*") -and $headers[1] -eq 'Diamonds') ($headers -join ' | ')
        Invoke-GridHeader (Get-AccountsGrid) 'Player rank'
        $headers = @(Get-GridHeaders (Get-AccountsGrid))
        Check '5b A second click flips it' ($headers -contains "Player rank $up") ($headers -join ' | ')

        # 6. Picking an account fills the account card.
        $pick = @(Get-GridRows (Get-AccountsGrid)) | Where-Object { $_.Current.Name -ne 'Total' } | Select-Object -Last 1
        $pickName = $pick.Current.Name
        Select-GridRow $pick
        $card = Find-ByAutomationId (Get-BoardWindow) 'AccountCardPanel1'
        $subtitle = Line $card 'PanelSubtitle'
        Check '6 The card shows the picked account' ($subtitle.StartsWith($pickName)) "picked '$pickName'; card '$subtitle'; note '$(Line $card 'PanelNote')'"

        # 6b. The pick's own redraw puts keyboard focus back in the table (Task 5's fix; AccountsTableFocusTests
        # covers the logic, this proves the real DataGrid lands focus somewhere under AccountsGrid, not just off it).
        $focusedName = try { $AE::FocusedElement.Current.Name } catch { '(none)' }
        $focusedId = try { $AE::FocusedElement.Current.AutomationId } catch { '' }
        Check '6b Keyboard focus stays in the table after the pick' (Test-FocusWithin (Get-AccountsGrid)) "focused name='$focusedName' id='$focusedId'"

        # 7. The total sums Diamonds (the first stat column); the rank column stays blank.
        $total = @(Get-GridRows (Get-AccountsGrid)) | Where-Object { $_.Current.Name -eq 'Total' } | Select-Object -First 1
        $totalTexts = @(Get-AllTexts $total)
        Check '7 The total row sums what adds up' ($totalTexts.Count -ge 2 -and $totalTexts[0] -eq 'Total' -and ($totalTexts[1] -match '^[\d,]+$' -or $totalTexts[1] -eq $dash)) ($totalTexts -join ' | ')

        # 7b. A refresh (Test now) doesn't scroll the board back to wherever the table is (Task 5's fix: a data
        # refresh leaves focus and scrolling alone, only a sort or pick this table raised may restore them).
        $scrollable = Get-BoardScrollPercent (Get-BoardWindow)
        if ($scrollable -eq -1) {
            Skip '7b A refresh does not scroll the board' 'board is not tall enough to scroll' 'BoardScroll.VerticallyScrollable=false'
        }
        else {
            Set-BoardScrollPercent (Get-BoardWindow) 80
            $before = Get-BoardScrollPercent (Get-BoardWindow)
            Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton')
            Wait-Until { -not (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 10 | Out-Null
            Wait-Until { (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 240 | Out-Null
            $after = Get-BoardScrollPercent (Get-BoardWindow)
            Check '7b A refresh does not scroll the board' ([Math]::Abs($after - $before) -le 2) "scrolled to $before before Test now, $after after"
        }
    }

    # 8. Sorting and picking are this session's only.
    Check '8 Sorting and picking write no boards.json' (-not (Test-Path $boardsFile)) "exists=$(Test-Path $boardsFile)"

    & (Join-Path $PSScriptRoot 'shot.ps1') -OutPath (Join-Path $UrShots 'alts-board.png') | Out-Null
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Show-Results
    "RoRoRo running: $rororo"
}
exit $LASTEXITCODE
