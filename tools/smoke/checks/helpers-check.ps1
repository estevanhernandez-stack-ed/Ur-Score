# Checks the data-folder helpers in uia.ps1 without launching anything (S1-16.1). Points $UrData at a scratch
# folder under %TEMP%, so the real data folder is never touched, and prints PASS/FAIL per step like a walk.
# Run: powershell -File tools\smoke\checks\helpers-check.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\uia.ps1')

$scratch = Join-Path $env:TEMP "urscore-helpers-check-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
New-Item -ItemType Directory -Force $scratch | Out-Null
$script:UrData = Join-Path $scratch '626labs.ur-score'

try {
    # 1. Aside and back round-trips your data, and the walk gets an empty folder in between.
    New-Item -ItemType Directory -Force $UrData | Out-Null
    Set-Content (Join-Path $UrData 'marker.txt') 'yours'
    $backup = Move-UrDataAside
    $emptyDuring = (Test-Path $UrData) -and (@(Get-ChildItem $UrData).Count -eq 0)
    Set-Content (Join-Path $UrData 'scratch.txt') 'the walk''s'
    Restore-UrData $backup | Out-Null
    $marker = Get-Content (Join-Path $UrData 'marker.txt') -ErrorAction SilentlyContinue
    Check '1 Aside then back: your data returns and the walk''s scratch is gone' (
        $emptyDuring -and $marker -eq 'yours' -and -not (Test-Path (Join-Path $UrData 'scratch.txt')) -and -not (Test-Path $backup)) "empty during=$emptyDuring marker='$marker'"

    # 2. A leftover backup - a walk killed mid-run - stops the next Move-UrDataAside by name, and moves nothing.
    $leftover = "$UrData.smoke-backup-20260101-000000"
    New-Item -ItemType Directory -Force $leftover | Out-Null
    Set-Content (Join-Path $leftover 'marker.txt') 'yours, from the killed walk'
    $threw = $null
    try { Move-UrDataAside | Out-Null } catch { $threw = $_.Exception.Message }
    $named = $threw -and $threw.Contains($leftover) -and $threw -match 'Rename the newest'
    $untouched = (Get-Content (Join-Path $UrData 'marker.txt')) -eq 'yours' -and (Test-Path $leftover) -and @(Get-UrLeftoverBackups).Count -eq 1
    Check '2 A leftover backup stops the next walk, names the folder, and touches nothing' ($named -and $untouched) "threw='$threw'"

    # 3. With the leftover dealt with, the walk runs again.
    Remove-Item $leftover -Recurse -Force
    $backup = Move-UrDataAside
    Restore-UrData $backup | Out-Null
    Check '3 Once the leftover is gone the helpers work as before' ((Get-Content (Join-Path $UrData 'marker.txt')) -eq 'yours') 'round trip'

    # 4. The RoRoRo record: a process check, and the after-step is a failure only when RoRoRo appeared mid-walk.
    $script:RoRoRoUpBefore = $false
    $up = Test-RoRoRoUp
    Note-RoRoRo 'after'
    $last = $script:Results[$script:Results.Count - 1]
    Check '4 RoRoRo after the walk is a FAIL only when it came up during it' (($up -and $last.Result -eq 'FAIL') -or (-not $up -and $last.Result -eq 'PASS')) "RoRoRo up now=$up; recorded $($last.Result): $($last.Seen)"
}
finally {
    if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force }
    Show-Results
}
exit $LASTEXITCODE
