# 按窗口句柄/title 截图（PrintWindow，不怕被别的窗口挡住）
# powershell -File tools/winshot.ps1 -Title "NetPulse 设置" -Name set
param(
    [string]$Title = "",
    [long]$Hwnd = 0,
    [string]$Name = "win",
    [string]$Dir = "shots"
)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WS {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
"@

if ($Hwnd -eq 0) {
    $target = $Title
    $found = [IntPtr]::Zero
    $cb = [WS+EnumProc]{ param($h,$l)
        if ([WS]::IsWindowVisible($h)) {
            $sb = New-Object System.Text.StringBuilder 256
            [WS]::GetWindowText($h,$sb,256) | Out-Null
            if ($sb.ToString() -like "*$target*") { $script:found = $h; return $false }
        }
        return $true
    }
    [WS]::EnumWindows($cb,[IntPtr]::Zero) | Out-Null
    $Hwnd = $script:found.ToInt64()
}
if ($Hwnd -eq 0) { Write-Output "window not found"; exit 1 }

$h2 = [IntPtr]$Hwnd
$r = New-Object WS+RECT
[WS]::GetWindowRect($h2, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hh = $r.B - $r.T
if ($w -le 0 -or $hh -le 0) { Write-Output "bad rect"; exit 1 }

$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[WS]::PrintWindow($h2, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)
$g.Dispose()

if (-not (Test-Path $Dir)) { New-Item -ItemType Directory -Path $Dir | Out-Null }
$path = Join-Path $Dir "$Name.png"
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "$path  ($w x $hh)"
