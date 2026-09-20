# Spec 5.6: another player's id never reaches disk. Loads your own Roblox user ids from accounts.json and
# checks every score book line: its account keys, its unavailable ids, and that no line carries response text.
# Since 2026-09-19 a clans list keeps the field's numbers (FieldSummary), and since 2026-09-20 the clans a chart
# draws, by name and capped (GroupRows: the top 25, your own and their neighbours). Clan names are public game
# standings and allowed; a PLAYER never is. This checks the headline keys, that no account rides a field line, and
# that the kept clans stay within the cap, so the book cannot quietly grow into the whole board.
# Prints only pass/fail with counts, never an account id or another player's id.
# Exit 0 when clean, 1 when anything is not yours, 2 when there is nothing to check.
param([string]$DataFolder = (Join-Path $env:LOCALAPPDATA '626labs.ur-score'))

$accountsFile = Join-Path $DataFolder 'accounts.json'
$bookRoot = Join-Path $DataFolder 'scorebook'
if (-not (Test-Path $accountsFile)) { "No accounts.json in $DataFolder. Start RoRoRo while Ur Score runs, then check again."; exit 2 }
if (-not (Test-Path $bookRoot)) { "No score book in $DataFolder yet."; exit 2 }

# Windows PowerShell 5.1 passes a parsed JSON array down the pipeline as one object, so unroll it first;
# otherwise every id is joined into one string and every account reads as not yours.
$parsed = Get-Content $accountsFile -Raw | ConvertFrom-Json
$mine = @(@($parsed) | ForEach-Object { [string]$_.robloxUserId } | Where-Object { $_ -and $_ -ne '0' })
if ($mine.Count -eq 0) { "accounts.json lists no Roblox user ids."; exit 2 }

$lines = 0; $reads = 0; $finals = 0; $broken = 0
$notYoursAccountKeys = 0; $notYoursUnavail = 0; $watchWithAccounts = 0; $responseTextLines = 0
$fieldLines = 0; $fieldWithAccounts = 0; $fieldStrayKeys = 0
# GroupRows.Top (25) plus your own clans and the place either side of each: a handful more, never a whole board.
$groupCap = 40; $overCap = 0; $mostKept = 0
$fieldKeys = @('field-leader', 'field-top10', 'field-avg', 'field-bottom10', 'field-clans',
               'field-mine', 'field-mine-rank', 'field-above', 'field-gap-above',
               'field-mine-members', 'field-mine-capacity', 'field-mine-contributors')

foreach ($file in Get-ChildItem $bookRoot -Recurse -Filter *.jsonl) {
    foreach ($text in [System.IO.File]::ReadLines($file.FullName)) {
        if (-not $text.Trim()) { continue }
        try { $line = $text | ConvertFrom-Json } catch { $broken++; continue }
        $lines++
        if ($line.kind -eq 'final') { $finals++ } else { $reads++ }

        if ($line.accounts) {
            foreach ($key in $line.accounts.PSObject.Properties.Name) {
                if ($mine -notcontains $key) { $notYoursAccountKeys++ }
            }
        }
        foreach ($id in @($line.unavail)) {
            if ($id -and ($mine -notcontains [string]$id)) { $notYoursUnavail++ }
        }
        if ($line.role -eq 'watch' -and $line.accounts -and @($line.accounts.PSObject.Properties).Count -gt 0) {
            $watchWithAccounts++
        }
        if ($text -match 'PointContributions|UserID|DisplayName|displayName') {
            $responseTextLines++
        }

        # A clans-list line: every headline key must be one of the field's own, and it may hold no account.
        $headlineKeys = @()
        if ($line.headline) { $headlineKeys = @($line.headline.PSObject.Properties.Name) }
        if (@($headlineKeys | Where-Object { $fieldKeys -contains $_ }).Count -gt 0) {
            $fieldLines++
            $fieldStrayKeys += @($headlineKeys | Where-Object { $fieldKeys -notcontains $_ }).Count
            if ($line.accounts -and @($line.accounts.PSObject.Properties).Count -gt 0) { $fieldWithAccounts++ }
            $kept = 0
            if ($line.groups) { $kept = @($line.groups.PSObject.Properties).Count }
            if ($kept -gt $groupCap) { $overCap++ }
            if ($kept -gt $mostKept) { $mostKept = $kept }
        }
    }
}

$bad = $notYoursAccountKeys + $notYoursUnavail + $watchWithAccounts + $responseTextLines + $fieldWithAccounts + $fieldStrayKeys + $overCap
"Your ids: $($mine.Count). Lines: $lines ($reads read, $finals final, $fieldLines field). Unparseable lines skipped: $broken."
"Problems: $bad (not-yours account keys: $notYoursAccountKeys, not-yours unavail ids: $notYoursUnavail, watch lines with accounts: $watchWithAccounts, field lines with accounts: $fieldWithAccounts, stray keys on field lines: $fieldStrayKeys, field lines over the clan cap: $overCap (most kept on one line: $mostKept), response-text lines: $responseTextLines)."
if ($bad -gt 0) { exit 1 }
exit 0
