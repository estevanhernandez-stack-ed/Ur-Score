# Spec 5.6: another player's id never reaches disk. Loads your own Roblox user ids from accounts.json and
# checks every score book line: its account keys, its unavailable ids, and that no line carries response text.
# Prints only pass/fail with counts, never an account id or another player's id.
# Exit 0 when clean, 1 when anything is not yours, 2 when there is nothing to check.
param([string]$DataFolder = (Join-Path $env:LOCALAPPDATA '626labs.ur-score'))

$accountsFile = Join-Path $DataFolder 'accounts.json'
$bookRoot = Join-Path $DataFolder 'scorebook'
if (-not (Test-Path $accountsFile)) { "No accounts.json in $DataFolder. Start RoRoRo while Ur Score runs, then check again."; exit 2 }
if (-not (Test-Path $bookRoot)) { "No score book in $DataFolder yet."; exit 2 }

$mine = @(Get-Content $accountsFile -Raw | ConvertFrom-Json | ForEach-Object { [string]$_.robloxUserId } | Where-Object { $_ -and $_ -ne '0' })
if ($mine.Count -eq 0) { "accounts.json lists no Roblox user ids."; exit 2 }

$lines = 0; $reads = 0; $finals = 0; $broken = 0
$notYoursAccountKeys = 0; $notYoursUnavail = 0; $watchWithAccounts = 0; $responseTextLines = 0

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
    }
}

$bad = $notYoursAccountKeys + $notYoursUnavail + $watchWithAccounts + $responseTextLines
"Your ids: $($mine.Count). Lines: $lines ($reads read, $finals final). Unparseable lines skipped: $broken."
"Problems: $bad (not-yours account keys: $notYoursAccountKeys, not-yours unavail ids: $notYoursUnavail, watch lines with accounts: $watchWithAccounts, response-text lines: $responseTextLines)."
if ($bad -gt 0) { exit 1 }
exit 0
