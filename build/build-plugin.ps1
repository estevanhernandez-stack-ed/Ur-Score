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

    The .NET runtime is bundled by default, the same as Ur Task: clan members can't be assumed to
    have the .NET 10 desktop runtime installed, and a plugin that won't start without it is a
    support message in Discord, not a working install.

    The script refuses to build without icon.png at the repo root rather than packaging a
    placeholder; RoRoRo's own Store build treats a placeholder logo as a build-blocking defect.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER Runtime
    Target runtime identifier. Default: win-x64 (runs under emulation on Windows on Arm).

.PARAMETER SelfContained
    Bundle the .NET runtime into the plugin. Default: true.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot/.."
$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish"

Write-Host "[build-plugin] root: $root"
Write-Host "[build-plugin] configuration: $Configuration | runtime: $Runtime | self-contained: $SelfContained"

$iconPath = Join-Path $root "icon.png"
if (-not (Test-Path $iconPath)) {
    throw "icon.png is missing at $iconPath. Restore it (build/make-icon.py) rather than packaging a placeholder."
}

if (Test-Path $artifacts) {
    Write-Host "[build-plugin] clearing $artifacts"
    Remove-Item $artifacts -Recurse -Force
}
New-Item $artifacts -ItemType Directory -Force | Out-Null

Write-Host "[build-plugin] publishing..."
dotnet publish "$root/Ur-Score.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained $SelfContained `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

# A bundled runtime must actually be in the folder, or the zip only works on PCs that already have .NET.
if ($SelfContained -and -not (Test-Path (Join-Path $publish "hostfxr.dll"))) {
    throw "Self-contained publish produced no hostfxr.dll in $publish; the runtime was not bundled."
}

# Drop the manifest + icon inside the zip so RoRoRo's installer finds them at the install root
# after extraction.
Copy-Item "$root/manifest.json" "$publish/manifest.json" -Force
Copy-Item $iconPath "$publish/icon.png" -Force

# Debug symbols aren't needed at runtime.
Get-ChildItem -Path $publish -Filter "*.pdb" -Recurse | Remove-Item -Force

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
