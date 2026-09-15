# Setup > Alerts on a clean data folder, against a scratch rules file (plan A1): RoRoRo's own metric-rules.json is never
# written, and the last step fails if its bytes change. Seeds the rule 0.3.1 added, one you wrote and another plugin's,
# imports the profile recipe sending Diamonds and Player rank, then: the cards read as sentences with no JSON and the
# next step in RoRoRo; Change a stops-climbing alert, confirmed with Enter in the closed minutes box; add a
# crosses-a-number alert with a refused number first and Enter in the number box; Escape cancels, from the number box
# and from a closed direction box; a locked file is said on the card and changes nothing; Remove; a stat you stop
# sending keeps its card with Remove only. RoRoRo running is optional.
. (Join-Path $PSScriptRoot 'uia-board.ps1')
$ErrorActionPreference = 'Stop'
$profileFixture = Join-Path $UrFixtures 'petsim99-profile.recipe.json'
$realRules = Join-Path $env:LOCALAPPDATA 'ROROROblox\metric-rules.json'
$scratchDir = Join-Path $env:TEMP "ur-score-smoke-rules-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$scratch = Join-Path $scratchDir 'metric-rules.json'
$backup = $null
$lock = $null

function Get-RealRulesHash {
    if (Test-Path $realRules) { (Get-FileHash $realRules -Algorithm SHA256).Hash } else { 'absent' }
}

# The scratch file's rules, one object per rule (see Read-Sources for why this unrolls with foreach).
function Read-ScratchRules {
    $parsed = Get-Content $scratch -Raw | ConvertFrom-Json
    foreach ($r in $parsed) { $r }
}

# A control by type and accessible name that is on screen: a collapsed editor's twin is not.
function Get-Shown($root, $type, [string]$name) {
    Find-All $root $type | Where-Object { $_.Current.Name -eq $name -and -not $_.Current.IsOffscreen } | Select-Object -First 1
}

function Get-ComboValue($box) {
    $selection = $box.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selection.Count -gt 0) { $selection[0].Current.Name } else { '' }
}

function Get-FocusedName { $f = $AE::FocusedElement; if ($f) { $f.Current.Name } else { '(none)' } }

# Climbs from a control unique to one stat (an edit, combo box or button named for that stat) up to that stat's own
# card, so a per-card automation id (AlertResultLine, AlertEditorProblemLine) is read from the right copy. Those ids
# are declared once per card in AlertCardTemplate, and AlertCardList is a plain, non-virtualizing list: with two or
# more sent stats on screen there are that many live elements sharing each id, and a plain Find-ByAutomationId
# (FindFirst from the window) always returns the first card's copy in tree order, not the one you meant (review
# task-4-review.md Important 1). Stops at the cards list or a window and throws, rather than falling back to an
# unscoped search that would silently read the wrong card again.
function Get-CardRoot($from, [string]$id) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $node = $from
    while ($node) {
        if ($node.Current.AutomationId -eq 'AlertCardList') { throw "climbed to the cards list without finding a card containing '$id'" }
        if ($node.Current.ControlType.ProgrammaticName -eq 'ControlType.Window') { throw "climbed to a window without finding a card containing '$id'" }
        if (Find-ByAutomationId $node $id) { return $node }
        $node = $walker.GetParent($node)
    }
    throw "ran out of ancestors without finding a card containing '$id'"
}

# Waits for a control by type and accessible name to be on screen, then returns it freshly found. A write's redraw
# can replace the card's visuals, so a reference held from before the write is not safe to reuse; and a script
# block's own assignment does not escape it (controller ruling 4), so this re-queries after Wait-Until confirms it
# rather than trusting a variable set inside the test itself.
function Wait-Shown($root, $type, [string]$name, [int]$seconds = 5) {
    $ok = Wait-Until { [bool](Get-Shown $root $type $name) } $seconds
    if (-not $ok) { throw "never showed up: '$name'" }
    Get-Shown $root $type $name
}

$realBefore = Get-RealRulesHash
try {
    New-Item -ItemType Directory -Force $scratchDir | Out-Null
    Set-Content -Path $scratch -Encoding ASCII -Value @'
[
  { "metricId": "ps99.diamonds", "kind": "Rate", "threshold": 100, "windowMinutes": 10, "alertWhenBelow": true, "owner": "626labs.ur-score" },
  { "metricId": "ps99.rank", "kind": "Rate", "threshold": 3, "windowMinutes": 30 },
  { "metricId": "memory.warning", "kind": "Event", "owner": "someone.else" }
]
'@
    # Set before Ur Score starts, so the process inherits it.
    $env:UR_SCORE_RULES_FILE = $scratch
    $backup = Move-UrDataAside
    Start-UrScore | Out-Null

    Complete-ClanImport $profileFixture @() @('Diamonds', 'Player rank') | Out-Null
    Open-SetupPage 'Alerts' | Out-Null
    $rate100 = "Alert me when an account's Diamonds gains fewer than 100 a minute for 10 minutes."
    # Controller ruling 2: throw before any click on this page if Ur Score isn't reading the scratch file. Without this,
    # a run against the real file would fall through into Section 2's Change click on a card that was never drawn.
    if (-not (Wait-Until { @(Get-AllTexts (Get-SetupWindow)) -contains $rate100 } 15)) {
        throw "Ur Score is not reading the scratch rules file: expected to see '$rate100' on Setup > Alerts within 15s and never did. Check that UR_SCORE_RULES_FILE was set before Start-UrScore, and that this isn't reading RoRoRo's real metric-rules.json."
    }

    # 1. The cards read as sentences: the rule 0.3.1 added is Ur Score's, yours is marked, no JSON, and the next step shows.
    $texts = @(Get-AllTexts (Get-SetupWindow))
    Check '1 The rule 0.3.1 added reads as a sentence in your words' ($texts -contains $rate100) ($texts -join ' | ')
    Check '1b ...with Change and Remove' ([bool](Get-Shown (Get-SetupWindow) $CT::Button 'Change the stops climbing alert for Diamonds') -and [bool](Get-Shown (Get-SetupWindow) $CT::Button 'Remove the stops climbing alert for Diamonds')) 'buttons'
    $yours = "Alert me when an account's Player rank gains fewer than 3 a minute for 30 minutes."
    Check '1c A rule you wrote is marked yours, with no Change' (($texts -contains $yours) -and ($texts -contains 'yours') -and -not (Get-Shown (Get-SetupWindow) $CT::Button 'Change the stops climbing alert for Player rank')) ($texts -join ' | ')
    $json = @($texts | Where-Object { $_ -match 'metricId|windowMinutes|alertWhenBelow|626labs|[{}]' })
    Check '1d No JSON on screen' ($json.Count -eq 0) ($json -join ' | ')
    $next = Line (Get-SetupWindow) 'AlertsNextLine'
    Check '1e The next step in RoRoRo is said' ($next -like 'Next, in RoRoRo: Settings*turn on Metric alerts and choose where they go (desktop, Discord, phone).') $next
    Check '1f Another plugin''s rule for a stat you do not send has no card' (-not ($texts -like '*memory.warning*')) 'absent'

    # 2. Change: focus in the number box, the minutes open on the rule's own, Enter in the closed minutes box saves it in
    # place with its label (controller ruling 3: a closed combo box does not handle Enter itself, so this proves
    # OnEditorKeyDown runs from there too, not only from the number box).
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Change the stops climbing alert for Diamonds')
    Start-Sleep -Milliseconds 800
    $number = Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds'
    Check '2 Change puts focus in the number box' (Test-FocusWithin $number) "focused='$(Get-FocusedName)'"
    $minutes = Get-Shown (Get-SetupWindow) $CT::ComboBox 'Minutes for Diamonds'
    Check '2b The minutes open on the rule''s own' ((Get-ComboValue $minutes) -eq '10') (Get-ComboValue $minutes)
    Set-ElementValue $number '250'
    Select-ComboItem $minutes '15'
    $minutes.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $card = Wait-Shown (Get-SetupWindow) $CT::Button 'Change the stops climbing alert for Diamonds' 5
    $said = Wait-Line (Get-CardRoot $card 'AlertResultLine') 'AlertResultLine' '^Changed\.' 5
    Check '2c The card says what changed' ($said -eq "Changed. RoRoRo will now alert you when an account's Diamonds gains fewer than 250 a minute for 15 minutes.") $said
    $rules = @(Read-ScratchRules)
    Check '2d Rewritten in place with its label; the others are kept' ($rules.Count -eq 3 -and $rules[0].threshold -eq 250 -and $rules[0].windowMinutes -eq 15 -and $rules[0].label -eq 'Diamonds' -and $rules[0].owner -eq '626labs.ur-score' -and $rules[1].threshold -eq 3 -and -not $rules[1].owner -and $rules[2].owner -eq 'someone.else') ($rules | ConvertTo-Json -Compress)
    Check '2e The file was backed up first' ((Get-Content "$scratch.ur-score-backup" -Raw) -match '"threshold": 100,') 'backup'
    Check '2f Focus returns to Change' (Test-FocusWithin (Get-Shown (Get-SetupWindow) $CT::Button 'Change the stops climbing alert for Diamonds')) "focused='$(Get-FocusedName)'"

    # 3. + Add an alert asks which kind; a comma decimal is refused with nothing written; Enter in the number box turns it on.
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Player rank')
    Start-Sleep -Milliseconds 800
    $stops = Get-Shown (Get-SetupWindow) $CT::Button 'Stops climbing, for Player rank'
    $crosses = Get-Shown (Get-SetupWindow) $CT::Button 'Crosses a number, for Player rank'
    Check '3 + Add an alert asks which kind, focus on the first' ([bool]$stops -and [bool]$crosses -and (Test-FocusWithin $stops)) "focused='$(Get-FocusedName)'"
    Invoke-Element $crosses
    Start-Sleep -Milliseconds 800
    $before = Get-Content $scratch -Raw
    Select-ComboItem (Get-Shown (Get-SetupWindow) $CT::ComboBox 'Above or below for Player rank') 'above'
    Set-ElementValue (Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Player rank') '1,5'
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Turn on the alert for Player rank')
    $editBox = Wait-Shown (Get-SetupWindow) $CT::Edit 'Number for Player rank' 5
    $problem = Wait-Line (Get-CardRoot $editBox 'AlertEditorProblemLine') 'AlertEditorProblemLine' '^Use a dot' 5
    Check '3b A comma decimal is refused and nothing is written' (($problem -eq 'Use a dot for decimals, like 1.5.') -and ((Get-Content $scratch -Raw) -eq $before)) $problem
    $number = Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Player rank'
    Set-ElementValue $number '40'
    $number.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $card = Wait-Shown (Get-SetupWindow) $CT::Button 'Change the crosses a number alert for Player rank' 5
    $said = Wait-Line (Get-CardRoot $card 'AlertResultLine') 'AlertResultLine' '^On\.' 5
    Check '3c Enter turns it on, and the card says so' ($said -eq "On. RoRoRo will alert you when an account's Player rank goes above 40.") $said
    $level = @(Read-ScratchRules) | Where-Object { $_.metricId -eq 'ps99.rank' -and $_.kind -eq 'Level' } | Select-Object -First 1
    Check '3d The rule has its direction, label and owner, and no window' ($level -and $level.threshold -eq 40 -and $level.alertWhenBelow -eq $false -and $level.label -eq 'Player rank' -and $level.owner -eq '626labs.ur-score' -and -not ($level.PSObject.Properties.Name -contains 'windowMinutes')) ($level | ConvertTo-Json -Compress)

    # 4. Escape closes an editor without writing, and focus goes back to + Add an alert: from the number box, and from a
    # closed direction box (controller ruling 3: a closed combo box does not handle Escape itself either).
    $before = Get-Content $scratch -Raw
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Diamonds')
    Start-Sleep -Milliseconds 800
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Crosses a number, for Diamonds')
    Start-Sleep -Milliseconds 800
    Check '4 Choosing a kind puts focus in the number box' (Test-FocusWithin (Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds')) "focused='$(Get-FocusedName)'"
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 800
    $add = Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Diamonds'
    Check '4b Escape closes it, writes nothing, and focus is on + Add an alert' ((-not (Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds')) -and ((Get-Content $scratch -Raw) -eq $before) -and (Test-FocusWithin $add)) "focused='$(Get-FocusedName)'"

    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Diamonds')
    Start-Sleep -Milliseconds 800
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Crosses a number, for Diamonds')
    Start-Sleep -Milliseconds 800
    $direction = Get-Shown (Get-SetupWindow) $CT::ComboBox 'Above or below for Diamonds'
    $direction.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 800
    $add = Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Diamonds'
    Check '4c Escape in a closed direction box cancels too, writing nothing' ((-not (Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds')) -and ((Get-Content $scratch -Raw) -eq $before) -and (Test-FocusWithin $add)) "focused='$(Get-FocusedName)'"

    # 5. A file another program holds open: Turn on says so on the card, keeps the editor, changes nothing; it works once let go.
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Diamonds')
    Start-Sleep -Milliseconds 800
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Crosses a number, for Diamonds')
    Start-Sleep -Milliseconds 800
    Set-ElementValue (Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds') '5000000'
    $before = Get-Content $scratch -Raw
    $lock = [System.IO.File]::Open($scratch, 'Open', 'ReadWrite', 'None')
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Turn on the alert for Diamonds')
    $editBox = Wait-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds' 5
    $problem = Wait-Line (Get-CardRoot $editBox 'AlertEditorProblemLine') 'AlertEditorProblemLine' 'locked' 5
    Check '5 A locked file is said on the card and the editor stays open' (($problem -like "RoRoRo's rules file is locked*") -and [bool](Get-Shown (Get-SetupWindow) $CT::Edit 'Number for Diamonds')) $problem
    $lock.Dispose()
    $lock = $null
    Check '5b ...and nothing changed' ((Get-Content $scratch -Raw) -eq $before) 'compared'
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Turn on the alert for Diamonds')
    $card = Wait-Shown (Get-SetupWindow) $CT::Button 'Change the crosses a number alert for Diamonds' 5
    $said = Wait-Line (Get-CardRoot $card 'AlertResultLine') 'AlertResultLine' '^On\.' 5
    Check '5c Once let go, Turn on works' ($said -eq "On. RoRoRo will alert you when an account's Diamonds goes below 5,000,000.") $said

    # 6. Remove deletes exactly Ur Score's alert of that kind; your rule stays; focus goes to + Add an alert.
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Remove the stops climbing alert for Diamonds')
    $card = Wait-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Diamonds' 5
    $said = Wait-Line (Get-CardRoot $card 'AlertResultLine') 'AlertResultLine' '^Removed\.' 5
    Check '6 The card says what was removed' ($said -eq "Removed. RoRoRo won't alert you when an account's Diamonds gains fewer than 250 a minute for 15 minutes any more.") $said
    $rules = @(Read-ScratchRules)
    $diamondRates = @($rules | Where-Object { $_.metricId -eq 'ps99.diamonds' -and $_.kind -eq 'Rate' })
    Check '6b Only that rule is gone' ($diamondRates.Count -eq 0 -and $rules.Count -eq 4) ($rules | ConvertTo-Json -Compress)
    Check '6c Focus goes to + Add an alert' (Test-FocusWithin (Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Diamonds')) "focused='$(Get-FocusedName)'"
    & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'setup-alerts.png') | Out-Null

    # 7. A stat you stop sending keeps its card while Ur Score has an alert for it, with Remove only.
    $setup = Open-SetupPage 'Stats'
    Set-Tick (Get-Check $setup 'Send Player rank') $false
    Invoke-Element (Find-ByAutomationId $setup 'SaveStatsButton')
    $saved = Wait-Line $setup 'StatsSavedLine' '^Saved\.' 5
    Open-SetupPage 'Alerts' | Out-Null
    Start-Sleep -Milliseconds 800
    $texts = @(Get-AllTexts (Get-SetupWindow))
    Check '7 The card says you no longer send it' (($saved -like 'Saved.*') -and (@($texts -like "You don't send Player rank any more*").Count -eq 1)) "$saved / $($texts -join ' | ')"
    Check '7b Remove only: no Change and no + Add an alert' ([bool](Get-Shown (Get-SetupWindow) $CT::Button 'Remove the crosses a number alert for Player rank') -and -not (Get-Shown (Get-SetupWindow) $CT::Button 'Change the crosses a number alert for Player rank') -and -not (Get-Shown (Get-SetupWindow) $CT::Button 'Add an alert for Player rank')) 'buttons'
    Check '7c The next step still shows for Diamonds' ((Line (Get-SetupWindow) 'AlertsNextLine') -like 'Next, in RoRoRo*') (Line (Get-SetupWindow) 'AlertsNextLine')
    Invoke-Element (Get-Shown (Get-SetupWindow) $CT::Button 'Remove the crosses a number alert for Player rank')
    $said = Wait-Line (Get-SetupWindow) 'AlertsResultLine' '^Removed\.' 5
    $gone = -not (@(Get-AllTexts (Get-SetupWindow)) -like "You don't send Player rank*")
    Check '7d Removing its last alert takes the card away and says so under the cards' (($said -like "Removed.*Player rank goes above 40 any more.") -and $gone) $said
}
finally {
    if ($null -ne $lock) { $lock.Dispose() }
    if ($null -ne $backup) { Restore-UrData $backup }
    Remove-Item Env:UR_SCORE_RULES_FILE -ErrorAction SilentlyContinue
    if (Test-Path $scratchDir) { Remove-Item $scratchDir -Recurse -Force }
    $realAfter = Get-RealRulesHash
    Check '8 RoRoRo''s own rules file is exactly as it was' ($realAfter -eq $realBefore) "same=$($realAfter -eq $realBefore)"
    Show-Results
}
exit $LASTEXITCODE
