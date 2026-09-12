#requires -Version 7.0
<#
.SYNOPSIS
    Build the RoRoRo Ur Score plugin release artifacts:
    artifacts/manifest.json + artifacts/manifest.sha256 + artifacts/plugin.zip.

.DESCRIPTION
    Per RoRoRo's AUTHOR_GUIDE.md, the plugin-install URL contract is a directory containing:
        <release>/manifest.json
        <release>/manifest.sha256
        <release>/plugin.zip
    where manifest.sha256 is the lowercase SHA-256 of plugin.zip on a single line, and plugin.zip
    contains the runnable EXE + manifest.json + icon.png + all deps. RoRoRo's installer refuses to
    extract if the actual SHA does not match the .sha256 file.

    This script also refuses to produce a plugin.zip with no real icon inside it. Ur Score does
    not have one yet — icon.png is owed through the 626labs-design skill and has not been made —
    and RoRoRo's own Store build already treats a placeholder logo as a build-blocking defect, not
    a warning. Fabricating one here so this script "completes" would ship a made-up icon, which is
    worse than a build that stops and says why. See the icon check below.

.PARAMETER Configuration
    Build configuration. Default: Release.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot/.."
$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish"

Write-Host "[build-plugin] root: $root"
Write-Host "[build-plugin] configuration: $Configuration"

# Fail before spending time on a publish that would only fail later at the copy step. No
# placeholder is generated here on purpose — see the .DESCRIPTION above.
$iconPath = Join-Path $root "icon.png"
if (-not (Test-Path $iconPath)) {
    throw (
        "icon.png is missing at $iconPath. Ur Score has no icon yet: it is owed through the " +
        "626labs-design skill and has not been made. This script will NOT fabricate a " +
        "placeholder — RoRoRo's own build refuses placeholder logos, and a made-up icon that " +
        "ships is worse than a build that stops here. Produce the real icon.png via the " +
        "626labs-design skill, place it at the repo root ($root\icon.png), and re-run this script."
    )
}

if (Test-Path $artifacts) {
    Write-Host "[build-plugin] clearing $artifacts"
    Remove-Item $artifacts -Recurse -Force
}
New-Item $artifacts -ItemType Directory -Force | Out-Null

Write-Host "[build-plugin] publishing..."
dotnet publish "$root/Ur-Score.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=false `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

# Drop the manifest + icon inside the zip so RoRoRo's installer finds them at the install root
# after extraction.
Copy-Item "$root/manifest.json" "$publish/manifest.json" -Force
Copy-Item $iconPath "$publish/icon.png" -Force

Write-Host "[build-plugin] creating plugin.zip..."
$zip = Join-Path $artifacts "plugin.zip"
Compress-Archive -Path "$publish/*" -DestinationPath $zip -Force
$zipSize = (Get-Item $zip).Length / 1MB
Write-Host ("[build-plugin] plugin.zip: {0:N2} MB" -f $zipSize)

Write-Host "[build-plugin] computing SHA-256..."
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$sha = Join-Path $artifacts "manifest.sha256"
[System.IO.File]::WriteAllText($sha, $hash)
Write-Host "[build-plugin] plugin.zip sha256: $hash"

Copy-Item "$root/manifest.json" (Join-Path $artifacts "manifest.json") -Force

Write-Host ""
Write-Host "[build-plugin] artifacts ready:"
Get-ChildItem $artifacts -File | ForEach-Object {
    Write-Host ("  {0}  ({1:N2} KB)" -f $_.Name, ($_.Length / 1KB))
}
