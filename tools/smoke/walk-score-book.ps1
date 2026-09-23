# Setup > Score book on a clean data folder: one read of a main clan writes the book, the page counts it,
# says why a stopped source isn't recording, and diagnostics never copy book content.
# Needs a clan battle the source reports (activeClanBattle names one); an idle source writes nothing.
param([string]$Main = 'CCGP')

. (Join-Path $PSScriptRoot 'uia-board.ps1')   # a superset of uia-import.ps1; the setup-import steps need Stop-UrScoreFromBoard
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
    # an import of it back into this same, unchanged PC. The file always carries the setup now, so the re-import
    # opens the preview too; every row is Same (nothing here differs from what was just exported), so nothing new
    # applies to the setup and every reading is already in the book (2026-09-22, the owner's second machine).
    $exportDir = Join-Path $env:TEMP "urscore-smoke-export-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    New-Item -ItemType Directory -Force $exportDir | Out-Null
    $exportFile = Join-Path $exportDir 'ur-score-stats-smoke.zip'
    try {
        $setup = Open-SetupPage 'Score book'
        Invoke-Element (Get-Button $setup 'Export stats to a file for another PC')
        Complete-FileDialog '^Export stats to a file$' $exportFile
        $exported = Wait-Line (Get-SetupWindow) 'StatsTransferLine' '^Exported ' 30
        Check '4b Export stats writes one file and says what went into it' ((Test-Path $exportFile) -and $exported -match '^Exported \d[\d,]* readings? and \d[\d,]* finished battles?, with \d+ recipes?, \d+ clans? and \d+ boards?, to ur-score-stats-smoke\.zip') "exists=$(Test-Path $exportFile); '$exported'"

        $setup = Get-SetupWindow
        Invoke-Element (Get-Button $setup 'Import stats from another PC''s file')
        Complete-FileDialog '^Import stats from another PC$' $exportFile
        $preview = Wait-UrWindow '^Import from another PC$' 20
        Check '4c The re-import of this PC''s own file opens the preview' ([bool]$preview) "preview=$([bool]$preview)"
        Invoke-Element (Get-Button $preview 'Import ticked')
        $imported = Wait-Line (Get-SetupWindow) 'StatsTransferLine' '^Nothing new in the setup' 60
        Check '4c2 Every row is Same, so nothing new in the setup, and every reading is already here' ($imported -match '^Nothing new in the setup\. Then nothing new to import\. \d[\d,]* were already here\. Your previous setup is in 626labs\.ur-score\.before-import-\d{8}-\d{4}(-\d+)?\.$') "'$imported'"
        & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'score-book-export-import.png') | Out-Null

        # 4d/4e/4f/4g/4h. The setup travels (V3-S.46): export from this folder (it has a recipe and one clan),
        # start over on a fresh folder, import the file, read the preview's rows by name, untick the clan, Import
        # ticked. The recipe arrives and the clan does not; the after-line says so; the aside folder exists;
        # nothing is set to send.
        $setup = Get-SetupWindow
        Invoke-Element (Get-Button $setup 'Export stats to a file for another PC')
        $setupFile = Join-Path $exportDir 'ur-score-everything-smoke.zip'
        Complete-FileDialog '^Export stats to a file$' $setupFile
        # 'with 1 recipe' alone would risk matching the FIRST export's line (4b), still on screen until this
        # second export's own line lands; wait for a line that names this export's own file instead.
        $exportedAll = Wait-Line (Get-SetupWindow) 'StatsTransferLine' 'ur-score-everything-smoke\.zip' 30
        Check '4d Export stats counts the setup in its line' ($exportedAll -match 'with 1 recipe, 1 clan and \d+ boards?, to ur-score-everything-smoke\.zip') "'$exportedAll'"

        Stop-UrScoreFromBoard
        # S1-16.1: Move-UrDataAside refuses a second aside while the first backup exists, so the second, fresh
        # folder is made by hand here and renamed back in the finally below, rather than reusing that helper.
        Stop-UrScore
        $second = "$UrData.smoke-second-$(Get-Date -Format 'HHmmss')"
        Rename-Item $UrData (Split-Path $second -Leaf)
        New-Item -ItemType Directory -Force $UrData | Out-Null
        try {
            Start-UrScore | Out-Null
            $setup = Open-SetupPage 'Score book'
            Invoke-Element (Get-Button $setup 'Import stats from another PC''s file')
            Complete-FileDialog '^Import stats from another PC$' $setupFile
            $preview = Wait-UrWindow '^Import from another PC$' 20
            Check '4e The preview opens and names the recipe and the clan' ([bool]$preview -and [bool](Get-Check $preview 'Import Pet Sim 99 clan battle points') -and [bool](Get-Check $preview "Import $Main")) "preview=$([bool]$preview)"
            Set-Tick (Get-Check $preview "Import $Main") $false
            Invoke-Element (Get-Button $preview 'Import ticked')
            $after = Wait-Line (Get-SetupWindow) 'StatsTransferLine' '^Imported 1 recipe' 60
            Check '4f The recipe arrives, the stats follow, and the line says where the old setup is' ($after -match '^Imported 1 recipe\. Then [a-z].*\. Your previous setup is in 626labs\.ur-score\.before-import-\d{8}-\d{4}(-\d+)?\.$') "'$after'"
            $sourcesPath = Join-Path $UrData 'sources.json'
            $sourcesOk = (-not (Test-Path $sourcesPath)) -or ((Get-Content $sourcesPath -Raw) -notmatch $Main)
            Check '4g The unticked clan never reaches sources.json' $sourcesOk "exists=$(Test-Path $sourcesPath)"
            $recipeState = Get-Content (Join-Path $UrData 'recipes\pet-sim-99-clan-battle-points.state.json') -Raw
            Check '4h Nothing arrived set to send' ($recipeState -notmatch '"send":\s*true') 'state file read'
            & (Join-Path $PSScriptRoot 'shot.ps1') -Title 'Setup' -OutPath (Join-Path $UrShots 'score-book-setup-import.png') | Out-Null
        }
        finally {
            Stop-UrScore
            if ($second) { Remove-Item $UrData -Recurse -Force -ErrorAction SilentlyContinue; Rename-Item $second (Split-Path $UrData -Leaf) }
            Get-ChildItem (Split-Path $UrData -Parent) -Directory -Filter '626labs.ur-score.before-import-*' | Remove-Item -Recurse -Force
        }
        # The walk's remaining steps (Diagnostics, the privacy check) run against the folder just restored above,
        # so Ur Score comes back up here rather than leaving the walk stopped mid-way.
        Start-UrScore | Out-Null
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
