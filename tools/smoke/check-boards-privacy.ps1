# Stage 2: boards.json holds no other player. Every userId a panel keeps must be one of your own Roblox user ids
# from accounts.json, a panel carries only its own fields, and no stat key is all digits.
# Prints counts only, never an id. Exit 0 when clean, 1 when something isn't right, 2 when there is nothing to check.
param([string]$DataFolder = (Join-Path $env:LOCALAPPDATA '626labs.ur-score'))

$boardsFile = Join-Path $DataFolder 'boards.json'
$accountsFile = Join-Path $DataFolder 'accounts.json'
if (-not (Test-Path $boardsFile)) { "No boards.json in $DataFolder yet."; exit 2 }

# Windows PowerShell 5.1 passes a parsed JSON array down the pipeline as one object, so unroll it first.
$mine = @()
if (Test-Path $accountsFile) {
    $parsedAccounts = Get-Content $accountsFile -Raw | ConvertFrom-Json
    $mine = @(@($parsedAccounts) | ForEach-Object { [string]$_.robloxUserId } | Where-Object { $_ -and $_ -ne '0' })
}

$panelKeys = @('id', 'type', 'size', 'order', 'settings', 'popout')
$settingKeys = @('recipe', 'sourceId', 'sourceIds', 'toSourceId', 'stat', 'userId')
$boards = 0; $panels = 0; $notYours = 0; $unexpected = 0; $digitStats = 0

$parsedBoards = Get-Content $boardsFile -Raw | ConvertFrom-Json
foreach ($board in @($parsedBoards)) {
    if (-not $board) { continue }
    $boards++
    foreach ($key in $board.PSObject.Properties.Name) { if (@('id', 'name', 'panels') -notcontains $key) { $unexpected++ } }
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
