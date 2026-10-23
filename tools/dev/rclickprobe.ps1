param([int]$Rounds = 6, [int]$X = 0, [int]$Y = 0)

$src = @'
using System;
using System.Runtime.InteropServices;
public class SP {
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr res);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    public const uint UP = 0x0004, DOWN = 0x0002, RUP = 0x0010, RDOWN = 0x0008;

    public static int Ping(string cls) {
        IntPtr h = FindWindow(cls, null);
        if (h == IntPtr.Zero) return -1;
        IntPtr r;
        uint t0 = (uint)Environment.TickCount;
        SendMessageTimeout(h, 0 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero, 2 /*SMTO_ABORTIFHUNG*/, 2000, out r);
        return (int)((uint)Environment.TickCount - t0);
    }
    public static void RightClick(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(RDOWN, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(RUP, 0, 0, 0, IntPtr.Zero);
    }
}
'@
Add-Type -TypeDefinition $src -Language CSharp

$max = 0
for ($i = 0; $i -lt $Rounds; $i++) {
    [SP]::RightClick($X, $Y)
    Start-Sleep -Milliseconds 120
    $t = [SP]::Ping('Shell_TrayWnd')
    if ($t -gt $max) { $max = $t }
    Start-Sleep -Milliseconds 600
}
"rounds=$Rounds  tray max=$max ms"
