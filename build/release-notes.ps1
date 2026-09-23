#requires -Version 7.0
<#
.SYNOPSIS
    Write a release page's notes: that version's CHANGELOG section, then how to install it.

.DESCRIPTION
    The release workflow runs this for the tag it is publishing; docs/releasing.md runs it by hand to fix an
    existing release page. It stops, naming the version, when CHANGELOG.md has no "## <version> - <date>" section:
    a release whose page says nothing about what changed is the thing this exists to prevent.

.PARAMETER Version
    The version without the v: 0.5.9.

.PARAMETER OutPath
    Where to write the notes. Default: artifacts/release-notes.md.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$OutPath = (Join-Path $PSScriptRoot '..\artifacts\release-notes.md')
)

$ErrorActionPreference = 'Stop'
$changelog = Get-Content (Join-Path $PSScriptRoot '..\CHANGELOG.md') -Raw
$heading = '(?m)^## ' + [regex]::Escape($Version) + ' - \d{4}-\d{2}-\d{2}[^\S\r\n]*\r?$'
$start = [regex]::Match($changelog, $heading)
if (-not $start.Success) {
    throw "CHANGELOG.md has no '## $Version - <date>' section. Date the Unreleased section as $Version before tagging."
}

$rest = $changelog.Substring($start.Index + $start.Length)
$next = [regex]::Match($rest, '(?m)^## ')
$section = ($(if ($next.Success) { $rest.Substring(0, $next.Index) } else { $rest })).Trim()

$repo = 'https://github.com/estevanhernandez-stack-ed/Ur-Score'
$notes = @"
$section

### How to install

- **In RoRoRo:** Plugins, where the marketplace offers Ur Score $Version.
- **By hand:** download ``plugin.zip``, ``manifest.sha256`` and ``install.ps1`` below into one folder, quit RoRoRo and Ur Score, then right-click ``install.ps1`` › **Run with PowerShell**. Or unzip ``plugin.zip`` into ``%LOCALAPPDATA%\ROROROblox\plugins\626labs.ur-score\`` yourself. Details: [Install by hand]($repo#install-by-hand).

Your recipes, clans, boards and score book live in ``%LOCALAPPDATA%\626labs.ur-score\`` and are never touched by an install. The full history is in [CHANGELOG.md]($repo/blob/master/CHANGELOG.md).
"@

New-Item -ItemType Directory -Force (Split-Path $OutPath) | Out-Null
Set-Content -Path $OutPath -Value $notes -Encoding utf8NoBOM
Write-Host "[release-notes] $Version -> $OutPath"
