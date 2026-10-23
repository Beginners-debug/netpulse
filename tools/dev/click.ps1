# 鼠标点击小工具：powershell -File tools/click.ps1 -X 100 -Y 200
param([int]$X = 0, [int]$Y = 0, [int]$Button = 0)
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class M {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
}
"@
[M]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 120
if ($Button -eq 1) {
    [M]::mouse_event(0x0008, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [M]::mouse_event(0x0010, 0, 0, 0, [IntPtr]::Zero)
} else {
    [M]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [M]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
}
Start-Sleep -Milliseconds 250
Write-Output "clicked $X,$Y"
