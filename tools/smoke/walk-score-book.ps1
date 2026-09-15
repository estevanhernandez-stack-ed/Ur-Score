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
    Start-UrScore | Out-Null
    $setup = Complete-ClanImport $clanFixture @('Points') @()
    $setup = Wait-UrWindow '^Setup$' 30
    Select-SearchName $setup 'Your main clan' $Main
    Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null

    $board = Get-BoardWindow
    Invoke-Element (Find-ByAutomationId $board 'TestNowButton')
    Start-Sleep -Seconds 20

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
    Show-Results
}
exit $LASTEXITCODE
