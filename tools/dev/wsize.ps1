param([string]$Name = 'NetPulse')
$src = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class WRect {
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static string Dump(int pid) {
        var sb = new StringBuilder();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            var cn = new StringBuilder(256); GetClassName(h, cn, 256);
            RECT r; GetWindowRect(h, out r);
            sb.AppendFormat("vis={0} {1}x{2} @{3},{4} cls={5}\n",
                IsWindowVisible(h) ? 1 : 0, r.R - r.L, r.B - r.T, r.L, r.T, cn.ToString());
            return true;
        }, IntPtr.Zero);
        return sb.ToString();
    }
}
'@
Add-Type -TypeDefinition $src -Language CSharp
$p = Get-Process $Name -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { "no process"; exit 1 }
[WRect]::Dump($p.Id)
