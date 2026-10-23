param(
    [string]$Name = "probe",
    [string]$Title = "NetPulse 设置",
    [int]$ClickX = -1,
    [int]$ClickY = -1
)

Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class NetPulseProbe {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

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

    public static int Ping(IntPtr h, int timeout) {
        IntPtr r;
        var t0 = Environment.TickCount;
        SendMessageTimeoutW(h, 0, IntPtr.Zero, IntPtr.Zero, 2, (uint)timeout, out r);
        return Environment.TickCount - t0;
    }

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(80);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
}
"@

$proc = Get-Process NetPulse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { "NetPulse 未运行"; exit 1 }

$h = [NetPulseProbe]::Find([uint32]$proc.Id, $Title)
if ($h -eq [IntPtr]::Zero) { "找不到窗口：$Title"; exit 2 }

$r = New-Object 'NetPulseProbe+RECT'
[NetPulseProbe]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $ht = $r.B - $r.T

"hwnd=$h rect=$($r.L),$($r.T),$w,$ht ping=$([NetPulseProbe]::Ping($h, 1500))ms"

if ($ClickX -ge 0) {
    [NetPulseProbe]::Click($r.L + $ClickX, $r.T + $ClickY)
    Start-Sleep -Milliseconds 900
}

$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$g.Dispose()
$out = Join-Path (Get-Location) "shots\$Name.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$out
