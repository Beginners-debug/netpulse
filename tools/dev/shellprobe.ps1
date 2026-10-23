Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class ShellProbe {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    public static int Ping(string cls, int timeoutMs) {
        IntPtr h = FindWindow(cls, null);
        if (h == IntPtr.Zero) return -1;
        IntPtr r;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        IntPtr ok = SendMessageTimeout(h, 0, IntPtr.Zero, IntPtr.Zero, 0x2, (uint)timeoutMs, out r);
        return (int)sw.ElapsedMilliseconds;
    }
}
"@
$rows = @()
for ($i = 0; $i -lt 24; $i++) {
    $a = [ShellProbe]::Ping('Shell_TrayWnd', 2000)
    $b = [ShellProbe]::Ping('Progman', 2000)
    $rows += [pscustomobject]@{ t = [math]::Round($i * 0.25, 2); tray = $a; desktop = $b }
    Start-Sleep -Milliseconds 250
}
$rows | Format-Table -AutoSize
"MAX tray = {0}ms  desktop = {1}ms" -f ($rows | Measure-Object -Property tray -Maximum).Maximum, ($rows | Measure-Object -Property desktop -Maximum).Maximum
