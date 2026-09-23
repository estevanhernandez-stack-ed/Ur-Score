<#
.SYNOPSIS
    Install a RoRoRo plugin by hand: right-click this file, "Run with PowerShell".

.DESCRIPTION
    The same script ships with every RoRoRo plugin release (Ur Score, Ur Task, Ur MCP, Ur OCR, Ur AFK); keep the
    copies identical. Put it beside the release's plugin.zip and manifest.sha256, then run it. It:

      1. checks plugin.zip against manifest.sha256 (the same check RoRoRo's own installer makes)
      2. reads the plugin's id, name, version and exe from the manifest.json inside the zip
      3. refuses while RoRoRo or the plugin is running
      4. moves an existing install aside to %TEMP%\<id>-<old version>-backup-<time>
      5. extracts into %LOCALAPPDATA%\ROROROblox\plugins\<id>\, which is where RoRoRo looks
      6. unblocks the files, so Windows doesn't question every exe as "downloaded from the internet"

    It never starts RoRoRo or the plugin, and never touches RoRoRo's consent record: RoRoRo asks for the plugin's
    permissions the first time you launch it.

    Runs under Windows PowerShell 5.1 (what "Run with PowerShell" uses) and PowerShell 7. If Windows refuses to run
    scripts: powershell -ExecutionPolicy Bypass -File install.ps1

.PARAMETER PluginsRoot
    Where RoRoRo looks for plugins. Only the tests change it.

.PARAMETER BackupRoot
    Where an existing install is moved aside. Only the tests change it.

.PARAMETER NoPause
    Don't wait for Enter at the end (the tests, and anyone running it from a console).
#>
[CmdletBinding()]
param(
    [string]$PluginsRoot = (Join-Path $env:LOCALAPPDATA 'ROROROblox\plugins'),
    [string]$BackupRoot = $env:TEMP,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Finish([int]$code, [string]$message) {
    if ($code -eq 0) { Write-Host $message -ForegroundColor Green } else { Write-Host $message -ForegroundColor Yellow }
    if (-not $NoPause) { [void](Read-Host 'Press Enter to close') }
    exit $code
}

$here = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$zip = Join-Path $here 'plugin.zip'
$shaFile = Join-Path $here 'manifest.sha256'

if (-not (Test-Path $zip) -or -not (Test-Path $shaFile)) {
    Finish 1 "Put plugin.zip and manifest.sha256 from the same release beside this script, then run it again. Looked in: $here"
}

# 1. The same check RoRoRo's installer makes: the zip is exactly the one the release published.
$expected = ([string]@((Get-Content $shaFile -Raw) -split '\s+' | Where-Object { $_ })[0]).ToLowerInvariant()
$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expected -ne $actual) {
    Finish 1 "plugin.zip doesn't match manifest.sha256, so it isn't the file that release published. Download both again from the same release. Nothing was installed."
}

# 2. What this plugin is, from its own manifest.
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entry = $archive.Entries | Where-Object { $_.FullName -eq 'manifest.json' } | Select-Object -First 1
    if (-not $entry) { Finish 1 "plugin.zip has no manifest.json at its root, so it isn't a RoRoRo plugin. Nothing was installed." }
    $reader = New-Object System.IO.StreamReader($entry.Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
}
finally { $archive.Dispose() }

$id = [string]$manifest.id
$name = if ($manifest.name) { [string]$manifest.name } else { $id }
$version = [string]$manifest.version
$exe = [string]$manifest.entrypoint
# The id becomes a folder name: only the characters plugin ids use, so it can't point anywhere else.
if ($id -notmatch '^[a-z0-9][a-z0-9.\-]*$' -or $id -match '\.\.') {
    Finish 1 "The plugin's id ('$id') isn't one RoRoRo would accept. Nothing was installed."
}

# 3. Files in use can't be replaced, and a half-replaced plugin is worse than an old one.
$running = @('ROROROblox.App')
if ($exe) { $running += [System.IO.Path]::GetFileNameWithoutExtension($exe) }
$up = @(Get-Process -Name $running -ErrorAction SilentlyContinue | Select-Object -ExpandProperty ProcessName -Unique)
if ($up.Count -gt 0) {
    Finish 1 "Quit $(($up | ForEach-Object { if ($_ -eq 'ROROROblox.App') { 'RoRoRo' } else { $name } }) -join ' and ') first (RoRoRo lives in the tray: right-click its icon, Quit), then run this again. Nothing was installed."
}

$target = Join-Path $PluginsRoot $id
$backup = $null

# 4. An existing install goes aside, named for its version, so it can be put back by hand.
if (Test-Path $target) {
    $old = 'unknown'
    $oldManifest = Join-Path $target 'manifest.json'
    if (Test-Path $oldManifest) {
        try { $old = [string](Get-Content $oldManifest -Raw | ConvertFrom-Json).version } catch { }
    }
    New-Item -ItemType Directory -Force $BackupRoot | Out-Null
    $backup = Join-Path $BackupRoot "$id-$old-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Move-Item $target $backup
}

# 5. Extract to a staging folder first, then one move: a failed extract leaves the old install where it was.
New-Item -ItemType Directory -Force $PluginsRoot | Out-Null
$staging = Join-Path $PluginsRoot ".$id.installing"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
try {
    # ExtractToDirectory refuses an entry that would land outside the folder.
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $staging)
    Move-Item $staging $target
}
catch {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue }
    if ($backup -and -not (Test-Path $target)) { Move-Item $backup $target }
    Finish 1 "Couldn't extract plugin.zip: $($_.Exception.Message) Your previous install is back where it was. Nothing was changed."
}

# 6. Files that came from a download carry Windows' "from the internet" mark; clear it on what was installed.
Get-ChildItem $target -Recurse -File | Unblock-File

$line = "Installed $name $version into $target."
if ($backup) { $line += " The previous install is in $backup." }
Finish 0 "$line Start RoRoRo; the first launch asks for its permissions."
