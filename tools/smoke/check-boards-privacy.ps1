# Stage 2: boards.json holds no other player. Every userId a panel keeps must be one of your own Roblox user ids
# from accounts.json, a panel carries only its own fields, and no stat key is all digits.
# Prints counts only, never an id. Exit 0 when clean, 1 when something isn't right, 2 when there is nothing to check.
param([string]$DataFolder = (Join-Path $env:LOCALAPPDATA '626labs.ur-score'))

$boardsFile = Join-Path $DataFolder 'boards.json'
$accountsFile = Join-Path $DataFolder 'accounts.json'
if (-not (Test-Path $boardsFile)) { "No boards.json in $DataFolder yet."; exit 2 }
if (-not (Test-Path $accountsFile)) { "No accounts.json in $DataFolder, so there is nothing to check. Start RoRoRo while Ur Score runs, then check again."; exit 2 }

# Windows PowerShell 5.1 writes a parsed JSON array down the pipeline as one object; foreach over the parenthesized
# read unrolls it (see Read-Sources in uia-import.ps1), so every id is its own string and not one joined string.
$mine = @(foreach ($account in (Get-Content $accountsFile -Raw | ConvertFrom-Json)) {
    if ($account -and $account.robloxUserId -and [string]$account.robloxUserId -ne '0') { [string]$account.robloxUserId }
})
# With no ids of yours, every userId would read as not yours, and a board with none would pass without comparing anything.
if ($mine.Count -eq 0) { "accounts.json lists no Roblox user ids, so there is nothing to check."; exit 2 }

$panelKeys = @('id', 'type', 'size', 'order', 'settings', 'popout')
$settingKeys = @('recipe', 'sourceId', 'sourceIds', 'toSourceId', 'stat', 'userId')
$boards = 0; $panels = 0; $notYours = 0; $unexpected = 0; $digitStats = 0

foreach ($board in (Get-Content $boardsFile -Raw | ConvertFrom-Json)) {
    if (-not $board) { continue }
    $boards++
    foreach ($key in $board.PSObject.Properties.Name) { if (@('id', 'name', 'panels', 'follows') -notcontains $key) { $unexpected++ } }
    foreach ($panel in @($board.panels)) {
        if (-not $panel) { continue }
        $panels++
        foreach ($key in $panel.PSObject.Properties.Name) { if ($panelKeys -notcontains $key) { $unexpected++ } }
        $settings = $panel.settings
        if (-not $settings) { continue }
        foreach ($key in $settings.PSObject.Properties.Name) { if ($settingKeys -notcontains $key) { $unexpected++ } }
        if ($null -ne $settings.userId -and ($mine -notcontains [string]$settings.userId)) { $notYours++ }
        if ($settings.stat -and ([string]$settings.stat -match '^(counter:)?[0-9]+$')) { $digitStats++ }
    }
}

$bad = $notYours + $unexpected + $digitStats
"Your ids: $($mine.Count). Boards: $boards. Panels: $panels."
"Problems: $bad (account ids not yours: $notYours, unexpected fields: $unexpected, all-digit stat keys: $digitStats)."
if ($bad -gt 0) { exit 1 }
exit 0
