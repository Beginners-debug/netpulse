# 截图工具：把指定窗口切到前台，再按屏幕坐标拷贝它的区域。
# 比 PrintWindow 可靠：分层窗口 / DWM 圆角 / 被遮挡都能截到，也不会因为目标线程繁忙而卡死。
#   .\wincap.ps1 -Title "NetPulse 设置" -Name settings
#   .\wincap.ps1 -MinW 300 -MaxW 520 -MinH 400 -MaxH 760 -Name panel
param(
    [string]$ProcessName = 'NetPulse',
    [string]$Title = '',
    [int]$MinW = 1, [int]$MaxW = 4000,
    [int]$MinH = 1, [int]$MaxH = 4000,
    [string]$Name = 'cap',
    [double]$Zoom = 1,
    [switch]$NoForeground
)

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WC2 {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$proc = Get-Process $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { throw "进程 $ProcessName 没在运行" }
$targetPid = $proc.Id
$script:hits = @()

$cb = [WC2+EnumProc]{
    param($h, $p)
    $wpid = 0
    [void][WC2]::GetWindowThreadProcessId($h, [ref]$wpid)
    if ($wpid -eq $targetPid -and [WC2]::IsWindowVisible($h)) {
        $sb = New-Object System.Text.StringBuilder 512
        [void][WC2]::GetWindowText($h, $sb, 512)
        $text = $sb.ToString()
        $r = New-Object WC2+RECT
        if ([WC2]::GetWindowRect($h, [ref]$r)) {
            $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
            $okTitle = ($Title -eq '') -or $text.Contains($Title)
            $okSize = $w -ge $MinW -and $w -le $MaxW -and $hh -ge $MinH -and $hh -le $MaxH
            if ($okTitle -and $okSize) {
                $script:hits += [pscustomobject]@{ H = $h; Text = $text; X = $r.Left; Y = $r.Top; W = $w; Hh = $hh }
            }
        }
    }
    return $true
}
[void][WC2]::EnumWindows($cb, [IntPtr]::Zero)

if ($script:hits.Count -eq 0) { throw '没找到匹配的窗口' }
$win = $script:hits | Sort-Object -Property Hh -Descending | Select-Object -First 1

if (-not $NoForeground) {
    [void][WC2]::ShowWindow($win.H, 9)          # SW_RESTORE
    [void][WC2]::SetWindowPos($win.H, [IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0040)  # TOPMOST, NOSIZE|NOMOVE|SHOWWINDOW
    [void][WC2]::SetForegroundWindow($win.H)
    Start-Sleep -Milliseconds 320
    # 重新取一次矩形（可能被挪动）
    $r2 = New-Object WC2+RECT
    [void][WC2]::GetWindowRect($win.H, [ref]$r2)
    $win.X = $r2.Left; $win.Y = $r2.Top; $win.W = $r2.Right - $r2.Left; $win.Hh = $r2.Bottom - $r2.Top
}

$bmp = New-Object System.Drawing.Bitmap $win.W, $win.Hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($win.X, $win.Y, 0, 0, $bmp.Size)
$g.Dispose()

if (-not (Test-Path 'shots')) { New-Item -ItemType Directory 'shots' | Out-Null }
$dst = Join-Path (Get-Location) ("shots\" + $Name + ".png")
if ($Zoom -ne 1) {
    $zw = [int]($win.W * $Zoom); $zh = [int]($win.Hh * $Zoom)
    $z = New-Object System.Drawing.Bitmap $zw, $zh
    $gz = [System.Drawing.Graphics]::FromImage($z)
    $gz.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $gz.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $gz.DrawImage($bmp, 0, 0, $zw, $zh)
    $gz.Dispose()
    $z.Save($dst, [System.Drawing.Imaging.ImageFormat]::Png)
    $z.Dispose()
}
else { $bmp.Save($dst, [System.Drawing.Imaging.ImageFormat]::Png) }
$bmp.Dispose()
"$dst  ($($win.W) x $($win.Hh))  title='$($win.Text)'  at $($win.X),$($win.Y)"
