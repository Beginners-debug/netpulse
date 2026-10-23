param([string]$Prefix = "page", [string]$Title = "NetPulse 设置")
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  public struct RECT { public int L, T, R, B; }
}
"@
$h = [W]::FindWindow([NullString]::Value, $Title)
if ($h -eq [IntPtr]::Zero) { Write-Output "window not found"; exit 1 }
[W]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300
$r = New-Object W+RECT
[W]::GetWindowRect($h, [ref]$r) | Out-Null
$ox = $r.L; $oy = $r.T
Write-Output "origin=$ox,$oy"

$ys = @(118, 172, 226, 280, 334)
for ($i = 0; $i -lt $ys.Count; $i++) {
    [W]::SetCursorPos($ox + 110, $oy + $ys[$i]) | Out-Null
    [W]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [W]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 550
    $p = (& powershell -NoProfile -ExecutionPolicy Bypass -File tools\winshot.ps1 -Title $Title -Name ("$Prefix" + ($i + 1)))
    Write-Output $p
}
