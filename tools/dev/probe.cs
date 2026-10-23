using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

public class Probe
{
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr hProcess, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr res);

    public static string Handles(string proc)
    {
        var p = Process.GetProcessesByName(proc);
        if (p.Length == 0) return "none";
        var list = "";
        foreach (var q in p)
        {
            q.Refresh();
            list += string.Format("pid={0} gdi={1} user={2} handles={3} threads={4} ws={5:N1}MB cpu={6:N2}s ",
                q.Id, GetGuiResources(q.Handle, 0), GetGuiResources(q.Handle, 1),
                q.HandleCount, q.Threads.Count, q.WorkingSet64 / 1048576.0, q.TotalProcessorTime.TotalSeconds);
        }
        return list;
    }

    // DwmFlush 返回一次合成完成的耗时；桌面卡顿时会明显变长
    public static string Frames(int n)
    {
        var sw = Stopwatch.StartNew();
        double max = 0, sum = 0;
        int over = 0;
        for (int i = 0; i < n; i++)
        {
            var f = Stopwatch.StartNew();
            DwmFlush();
            double ms = f.Elapsed.TotalMilliseconds;
            sum += ms; if (ms > max) max = ms; if (ms > 24) over++;
        }
        return string.Format("frames={0} avg={1:F2}ms max={2:F2}ms slow(>24ms)={3} total={4:F0}ms",
            n, sum / n, max, over, sw.Elapsed.TotalMilliseconds);
    }

    public static string TaskbarPing(int n)
    {
        IntPtr tb = FindWindow("Shell_TrayWnd", null);
        if (tb == IntPtr.Zero) return "no taskbar";
        var sw = Stopwatch.StartNew();
        double max = 0;
        for (int i = 0; i < n; i++)
        {
            IntPtr r;
            var t = Stopwatch.StartNew();
            SendMessageTimeout(tb, 0, IntPtr.Zero, IntPtr.Zero, 0, 2000, out r);
            double ms = t.Elapsed.TotalMilliseconds;
            if (ms > max) max = ms;
        }
        return string.Format("taskbar WM_NULL x{0}: max={1:F1}ms total={2:F1}ms", n, max, sw.Elapsed.TotalMilliseconds);
    }
}
