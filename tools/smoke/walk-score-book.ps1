# Setup > Score book on a clean data folder: one read of a main clan writes the book, the page counts it,
# says why a stopped source isn't recording, and diagnostics never copy book content.
# Needs a clan battle the source reports (activeClanBattle names one); an idle source writes nothing.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-import.ps1')
$ErrorActionPreference = 'Stop'
$clanFixture = Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json'
$backup = $null

try {
    $backup = Move-UrDataAside
    Note-RoRoRo 'before'
    Start-UrScore | Out-Null
    $setup = Complete-ClanImport $clanFixture @('Points') @()
    $setup = Wait-UrWindow '^Setup$' 30
    Select-SearchName $setup 'Your main clan' $Main
    Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null

    $board = Get-BoardWindow
    Invoke-Element (Find-ByAutomationId $board 'TestNowButton')
    # Wait for the read to land rather than twenty seconds: Test now is disabled for exactly as long as it runs,
    # and the state line then says what the read found ("Last read ..."). A fixed sleep raced a slow source and
    # then counted a book that had nothing in it yet (S1-L.3).
    Wait-Until { -not (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 10 | Out-Null
    $landed = Wait-Until { (Find-ByAutomationId (Get-BoardWindow) 'TestNowButton').Current.IsEnabled } 240
    $stateAfter = Wait-Line (Get-BoardWindow) 'StateLine' 'Last read' 30
    Check '0b The read lands and the state line says what it found' ($landed -and $stateAfter -match 'Last read') "enabled again=$landed; '$stateAfter'"
    # The book writes on its own thread a moment after the read; give the month file a bounded moment to appear.
    Wait-Until { [bool](Get-ChildItem (Join-Path $UrData 'scorebook') -Recurse -Filter *.jsonl -ErrorAction SilentlyContinue) } 15 | Out-Null

    $setup = Open-SetupPage 'Score book'
    $folder = Line $setup 'BookFolderLine'
    Check '1 The page names the book folder' ($folder -like '*626labs.ur-score\scorebook') $folder

    $recipes = @(Get-AllTexts (Find-ByAutomationId $setup 'BookRecipesList'))
    Check '2 Per recipe: readings, first reading, finals and size' (
        ($recipes -contains 'Pet Sim 99 clan battle points') -and (@($recipes -match '^\d[\d,]* readings? kept .+ (bytes|KB|MB)$').Count -eq 1)) ($recipes -join ' | ')

    $notRecording = @(Get-AllTexts (Find-ByAutomationId $setup 'NotRecordingList'))
    Check '3 A stopped source says it is stopped' (@($notRecording -like '*Stopped. Press Start on the board.*').Count -gt 0) ($notRecording -join ' | ')

    $slugDir = Join-Path $UrData 'scorebook\pet-sim-99-clan-battle-points'
    $months = @(Get-ChildItem $slugDir -Filter *.jsonl -ErrorAction SilentlyContinue)
    $recipeTexts = @(Get-ChildItem (Join-Path $slugDir 'recipes') -Filter *.json -ErrorAction SilentlyContinue)
    $lines = @($months | ForEach-Object { Get-Content $_.FullName } | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
    Check '4 A read wrote v1 lines to a UTC month file and kept the recipe text once' (
        ($months.Count -ge 1) -and ($months[0].Name -match '^\d{4}-\d{2}\.jsonl$') -and ($recipeTexts.Count -eq 1) -and
        ($lines.Count -ge 1) -and (@($lines | Where-Object { $_.v -ne 1 }).Count -eq 0)) "months=$($months.Name -join ',') recipes=$($recipeTexts.Count) lines=$($lines.Count)"

    # Export stats, then import the same file: one file made where asked, the line counting what went into it, and
    # an import of it adding nothing because every reading is already here (2026-09-22, the owner's second machine).
    $exportDir = Join-Path $env:TEMP "urscore-smoke-export-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    New-Item -ItemType Directory -Force $exportDir | Out-Null
    $exportFile = Join-Path $exportDir 'ur-score-stats-smoke.zip'
    try {
        $setup = Open-SetupPage 'Score book'
        Invoke-Element (Get-Button $setup 'Export stats to a file for another PC')
        Complete-FileDialog '^Export stats to a file$' $exportFile
        $exported = Wait-Line (Get-SetupWindow) 'StatsTransferLine' '^Exported ' 30
        Check '4b Export stats writes one file and says what went into it' ((Test-Path $exportFile) -and $exported -match '^Exported \d[\d,]* readings? and \d[\d,]* finished battles? to ur-score-stats-smoke\.zip') "exists=$(Test-Path $exportFile); '$exported'"

        $setup = Get-SetupWindow
        Invoke-Element (Get-Button $setup 'Import stats from another PC''s file')
        Complete-FileDialog '^Import stats from another PC$' $exportFile
        $imported = Wait-Line (Get-SetupWindow) 'StatsTransferLine' '^(Nothing new to import|Imported )' 30
        Check '4c Importing it back adds nothing: every reading is already here' ($imported -match '^Nothing new to import\. \d[\d,]* were already here\.') "'$imported'"
        & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'score-book-export-import.png') | Out-Null
    }
    finally {
        if (Test-Path $exportDir) { Remove-Item $exportDir -Recurse -Force }
    }

    $setup = Open-SetupPage 'Diagnostics'
    $saved = Get-Clipboard -Raw
    Invoke-Element (Find-ByAutomationId $setup 'CopyDiagnosticsButton')
    Start-Sleep -Seconds 1
    $diag = Get-Clipboard -Raw
    if ($null -ne $saved) { Set-Clipboard -Value $saved }
    Check '5 Copied diagnostics count the book but carry none of it' (($diag -match 'pending=\d+ dropped=\d+ \(no book content is included\)') -and ($diag -notmatch '"accounts":')) 'checked clipboard'

    # Exit 2 means there was nothing to check (RoRoRo never listed accounts); the live walk runs it with RoRoRo up.
    & (Join-Path $PSScriptRoot 'check-book-privacy.ps1') -DataFolder $UrData
    Check '6 Every account in the book is one of yours' ($LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq 2) "check-book-privacy exit $LASTEXITCODE"
}
finally {
    if ($null -ne $backup) { Restore-UrData $backup }
    Note-RoRoRo 'after'
    Show-Results
}
exit $LASTEXITCODE
