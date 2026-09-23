# Checks build/install.ps1, the by-hand plugin installer every RoRoRo plugin ships, without touching RoRoRo's plugins
# folder: every case installs a made-up plugin into a scratch folder under %TEMP%, under Windows PowerShell 5.1 (what
# "Run with PowerShell" uses). Prints PASS/FAIL per step like a walk; exits 1 on any failure.
# Run: powershell -File tools\smoke\checks\install-check.ps1   (-Script <path> checks another copy)
param([string]$Script = (Join-Path $PSScriptRoot '..\..\..\build\install.ps1'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Script = (Resolve-Path $Script).Path

$scratch = Join-Path $env:TEMP "urscore-install-check-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$results = New-Object System.Collections.Generic.List[object]
function Check([string]$step, [bool]$ok, [string]$seen) { $results.Add([pscustomobject]@{ Step = $step; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Seen = $seen }) }

# A release folder: plugin.zip (flat, with a manifest), its sha256, and a copy of the script beside them.
function New-Release([string]$name, [string]$version, [string]$entrypoint = 'fake-plugin.exe', [switch]$BadSha, [switch]$Escape) {
    $dir = Join-Path $scratch $name
    $content = Join-Path $dir 'content'
    New-Item -ItemType Directory -Force $content | Out-Null
    @{ schemaVersion = 1; id = 'test.fake-plugin'; name = 'Fake Plugin'; version = $version; entrypoint = $entrypoint } |
        ConvertTo-Json | Set-Content (Join-Path $content 'manifest.json') -Encoding UTF8
    Set-Content (Join-Path $content 'fake-plugin.exe') "not really an exe, version $version"
    $zip = Join-Path $dir 'plugin.zip'
    [System.IO.Compression.ZipFile]::CreateFromDirectory($content, $zip)
    if ($Escape) {
        $archive = [System.IO.Compression.ZipFile]::Open($zip, 'Update')
        $writer = New-Object System.IO.StreamWriter($archive.CreateEntry('../escaped.txt').Open())
        $writer.Write('should never land'); $writer.Dispose(); $archive.Dispose()
    }
    Remove-Item $content -Recurse -Force
    $sha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($BadSha) { $sha = ('0' * 64) }
    Set-Content (Join-Path $dir 'manifest.sha256') $sha
    Copy-Item $Script (Join-Path $dir 'install.ps1')
    $dir
}

function Install([string]$release, [string]$plugins) {
    # Its output (a refusal, or an error it did not expect) is recorded, not thrown: the check judges it.
    $ErrorActionPreference = 'Continue'
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $release 'install.ps1') -PluginsRoot $plugins -BackupRoot (Join-Path $scratch 'backups') -NoPause *>&1 | Out-String
    [pscustomobject]@{ Code = $LASTEXITCODE; Out = $out.Trim() }
}

try {
    New-Item -ItemType Directory -Force $scratch | Out-Null
    $plugins = Join-Path $scratch 'plugins'
    $target = Join-Path $plugins 'test.fake-plugin'

    # 1. A fresh install lands in plugins\<id>, says what it installed, and leaves no staging folder.
    $r = Install (New-Release 'v1' '1.0.0') $plugins
    $landed = (Test-Path (Join-Path $target 'fake-plugin.exe')) -and ((Get-Content (Join-Path $target 'fake-plugin.exe')) -match '1\.0\.0')
    Check '1 A fresh install lands in plugins\<id> and says so' ($r.Code -eq 0 -and $landed -and $r.Out -match 'Installed Fake Plugin 1\.0\.0' -and -not (Test-Path (Join-Path $plugins '.test.fake-plugin.installing'))) "exit $($r.Code): $($r.Out)"

    # 2. Installing over it moves the old one aside, named for its version, and the new one replaces it whole.
    $r = Install (New-Release 'v2' '2.0.0') $plugins
    $aside = @(Get-ChildItem (Join-Path $scratch 'backups') -Directory -Filter 'test.fake-plugin-1.0.0-backup-*')
    $new = (Get-Content (Join-Path $target 'fake-plugin.exe')) -match '2\.0\.0'
    Check '2 Over an old install: the old one goes aside by version, the new one replaces it' ($r.Code -eq 0 -and $aside.Count -eq 1 -and $new -and $r.Out -match 'previous install is in') "exit $($r.Code); asides=$($aside.Count)"

    # 3. A zip that doesn't match its sha256 is refused and nothing moves.
    $r = Install (New-Release 'bad-sha' '3.0.0' -BadSha) $plugins
    $still = (Get-Content (Join-Path $target 'fake-plugin.exe')) -match '2\.0\.0'
    Check '3 A zip that does not match manifest.sha256 is refused, nothing moves' ($r.Code -eq 1 -and $still -and $r.Out -match "doesn't match manifest\.sha256") "exit $($r.Code): $($r.Out)"

    # 4. While the plugin is running it is refused. The made-up plugin's exe is named after a process that is
    #    certainly running now: this check's own PowerShell host.
    $self = (Get-Process -Id $PID).ProcessName + '.exe'
    $r = Install (New-Release 'running' '4.0.0' $self) $plugins
    $still = (Get-Content (Join-Path $target 'fake-plugin.exe')) -match '2\.0\.0'
    Check '4 While the plugin is running it is refused by name, nothing moves' ($r.Code -eq 1 -and $still -and $r.Out -match 'Quit Fake Plugin first') "exit $($r.Code): $($r.Out)"

    # 5. An entry that would land outside the folder fails the extract, and the previous install is put back.
    $r = Install (New-Release 'escape' '5.0.0' -Escape) $plugins
    $restored = (Get-Content (Join-Path $target 'fake-plugin.exe')) -match '2\.0\.0'
    Check '5 An escaping entry is refused and the previous install is put back' ($r.Code -eq 1 -and $restored -and -not (Test-Path (Join-Path $scratch 'escaped.txt')) -and -not (Test-Path (Join-Path $plugins 'escaped.txt')) -and $r.Out -match 'previous install is back') "exit $($r.Code): $($r.Out)"

    # 6. Nothing installed carries Windows' "from the internet" mark, even from a zip that had it. .NET's extract never
    #    copies the mark today, so this guards a future extract method that would, rather than the unblock line.
    $marked = New-Release 'marked' '6.0.0'
    Set-Content -Path (Join-Path $marked 'plugin.zip') -Stream 'Zone.Identifier' -Value "[ZoneTransfer]`r`nZoneId=3"
    $r = Install $marked $plugins
    $flagged = @(Get-ChildItem $target -Recurse -File | Where-Object { Get-Item $_.FullName -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue })
    Check '6 Installed files carry no "from the internet" mark' ($r.Code -eq 0 -and $flagged.Count -eq 0) "exit $($r.Code); marked files=$($flagged.Count)"
}
catch {
    # A check that crashes is a failure, not a quieter run: say where it stopped.
    Check "X The check stopped: $($_.Exception.Message)" $false ($_.InvocationInfo.PositionMessage -split "`n" | Select-Object -First 1)
}
finally {
    if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue }
    $results | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
    $failed = @($results | Where-Object Result -eq 'FAIL').Count
    Write-Host "$($results.Count - $failed) passed, $failed failed"
}
if ($failed -gt 0) { exit 1 }
