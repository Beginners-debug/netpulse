param(
    [Parameter(Mandatory = $true)][string]$Name,
    [int]$ClickX = -1,
    [int]$ClickY = -1,
    [string]$Title = "NetPulse 设置",
    [switch]$KeepTop
)

Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class NPW {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    static readonly IntPtr TOPMOST = new IntPtr(-1);
    static readonly IntPtr NOTOPMOST = new IntPtr(-2);

    public static IntPtr Find(uint pid, string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            var sb = new StringBuilder(256); GetWindowTextW(h, sb, 256);
            if (sb.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static void Top(IntPtr h, bool on) {
        SetWindowPos(h, on ? TOPMOST : NOTOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(100);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(50);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
}
"@

$proc = Get-Process NetPulse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "NetPulse not running"; exit 1 }
$h = [NPW]::Find([uint32]$proc.Id, $Title)
if ($h -eq [IntPtr]::Zero) { Write-Output "window not found: $Title"; exit 2 }

[NPW]::Top($h, $true)
Start-Sleep -Milliseconds 250

$r = New-Object 'NPW+RECT'
[NPW]::GetWindowRect($h, [ref]$r) | Out-Null

if ($ClickX -ge 0) {
    [NPW]::Click($r.L + $ClickX, $r.T + $ClickY)
    Start-Sleep -Milliseconds 700
}

$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$g.Dispose()
$out = Join-Path (Get-Location) "shots\$Name.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

if (-not $KeepTop) { [NPW]::Top($h, $false) }
Write-Output "$out ($w x $ht)"
