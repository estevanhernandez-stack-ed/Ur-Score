# Saves a screenshot of one Ur Score window. You pass the output path.
param(
    [Parameter(Mandatory = $true)][string]$OutPath,
    [string]$Title = 'RoRoRo Ur Score'
)

Add-Type -AssemblyName System.Drawing
if (-not ('UrShotWin' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UrShotWin {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
}
"@
}

[UrShotWin]::SetProcessDPIAware() | Out-Null
$h = [UrShotWin]::FindWindow([NullString]::Value, $Title)
if ($h -eq [IntPtr]::Zero) { "no window titled '$Title'"; exit 1 }

[UrShotWin]::ShowWindow($h, 9) | Out-Null
[UrShotWin]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600

$r = New-Object UrShotWin+RECT
[UrShotWin]::GetWindowRect($h, [ref]$r) | Out-Null
$width = $r.R - $r.L
$height = $r.B - $r.T
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($r.L, $r.T, 0, 0, $bitmap.Size)

$folder = Split-Path -Parent $OutPath
if ($folder) { New-Item -ItemType Directory -Force $folder | Out-Null }
$bitmap.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
"$OutPath ${width}x${height}"
