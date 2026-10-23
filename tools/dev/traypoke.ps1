param([int]$Times = 5)

$src = @'
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
public class TrayProbe {
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr l);

    public static IntPtr[] Find(int pid) {
        var list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || IsWindowVisible(h)) return true;
            var cn = new StringBuilder(256); GetClassName(h, cn, 256);
            string c = cn.ToString();
            if (c.StartsWith("WindowsForms10.Window.0.app")) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    /// <summary>模拟 explorer 同步回调托盘窗口，返回本次调用占用目标 UI 线程的毫秒数。</summary>
    public static int Poke(IntPtr hwnd) {
        uint t0 = (uint)Environment.TickCount;
        SendMessage(hwnd, 0x0400 + 1024, new IntPtr(1), new IntPtr(0x0205)); // WM_TRAYMOUSEMESSAGE + WM_RBUTTONUP
        return (int)((uint)Environment.TickCount - t0);
    }
}
'@
Add-Type -TypeDefinition $src -Language CSharp

$p = Get-Process NetPulse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { "no process"; exit 1 }
$hs = [TrayProbe]::Find($p.Id)
"托盘隐藏窗口数 = " + $hs.Count
$max = 0
foreach ($h in $hs) {
    for ($i = 0; $i -lt $Times; $i++) {
        $ms = [TrayProbe]::Poke($h)
        if ($ms -gt $max) { $max = $ms }
    }
}
"同步回调最长占用 UI 线程 = $max ms"
