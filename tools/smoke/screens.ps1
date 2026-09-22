# Each screen's physical bounds and effective DPI, one per line: device|primary|x|y|width|height|dpi. Run as its own
# process by Get-UrScreens: DPI awareness is set once per process and before any window, and a DPI-unaware process
# is told 96 for every monitor whatever the scaling is (S2-8.6).
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class Dpi {
  [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
  [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint x, out uint y);
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; public POINT(int x, int y) { X = x; Y = y; } }
}
"@
[Dpi]::SetProcessDpiAwareness(2) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
  $b = $s.Bounds
  $pt = New-Object Dpi+POINT -ArgumentList ([int]($b.X + 10)), ([int]($b.Y + 10))
  $h = [Dpi]::MonitorFromPoint($pt, [uint32]2)
  $x = [uint32]0; $y = [uint32]0
  [Dpi]::GetDpiForMonitor($h, 0, [ref]$x, [ref]$y) | Out-Null
  "$($s.DeviceName)|$($s.Primary)|$($b.X)|$($b.Y)|$($b.Width)|$($b.Height)|$x"
}
