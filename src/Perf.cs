using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NetPulse
{
    /// <summary>
    /// 轻量性能探针：只在设置了环境变量 NP_PERF=1 时启动。
    /// 每 3 秒把「UI 心跳 / 绘制次数 / 各线程 CPU 增量」写进日志，用来定位空转。
    /// 发布版保留也无副作用（默认不启动），调试时非常有用。
    /// </summary>
    internal static class Perf
    {
        public static long Paints, Pushes, TickerTicks, UiBeats, Relayouts, Resizes, Adopts, Pings, Ticks;
        public static int UiMaxGap;       // UI 心跳最大间隔（ms）——超过 ~250 就是卡了一下
        public static int TrayMaxMs;      // 托盘图标/Tooltip 调用耗时峰值（ms）
        static int _lastBeat;
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        static readonly Dictionary<int, string> Names = new Dictionary<int, string>();

        /// <summary>给当前线程登记一个名字，方便把热点线程对上号。</summary>
        public static void Beat()
        {
            Interlocked.Increment(ref UiBeats);
            int now = Environment.TickCount;
            int prev = _lastBeat;
            _lastBeat = now;
            if (prev != 0)
            {
                int gap = now - prev;
                if (gap > UiMaxGap) UiMaxGap = gap;
            }
        }

        public static void NoteTray(int ms)
        {
            if (ms > TrayMaxMs) TrayMaxMs = ms;
        }

        public static void Name(string label)
        {
            lock (Names) Names[(int)GetCurrentThreadId()] = label;
        }

        static string Label(int id)
        {
            lock (Names)
            {
                string s;
                return Names.TryGetValue(id, out s) ? s : "?";
            }
        }

        static bool _on;
        static Thread _thread;

        public static bool Enabled
        {
            get { return Environment.GetEnvironmentVariable("NP_PERF") == "1"; }
        }

        public static void Start()
        {
            if (_on || !Enabled) return;
            _on = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "np-perf", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        static void Loop()
        {
            var proc = Process.GetCurrentProcess();
            var last = new Dictionary<int, TimeSpan>();
            foreach (ProcessThread t in proc.Threads) { try { last[t.Id] = t.TotalProcessorTime; } catch { } }
            TimeSpan lastTotal = proc.TotalProcessorTime;

            while (true)
            {
                Thread.Sleep(3000);
                try
                {
                    proc.Refresh();
                    TimeSpan total = proc.TotalProcessorTime;
                    double cpu = Math.Round((total - lastTotal).TotalMilliseconds / 30.0, 1); // 占单核百分比
                    lastTotal = total;

                    string hot = "";
                    double best = 0;
                    foreach (ProcessThread t in proc.Threads)
                    {
                        TimeSpan prev;
                        if (!last.TryGetValue(t.Id, out prev)) prev = TimeSpan.Zero;
                        double pct;
                        try { pct = (t.TotalProcessorTime - prev).TotalMilliseconds / 30.0; }
                        catch { continue; }
                        last[t.Id] = t.TotalProcessorTime;
                        if (pct > best) { best = pct; hot = t.Id.ToString(); }
                    }

                    long paints = Interlocked.Exchange(ref Paints, 0);
                    long pushes = Interlocked.Exchange(ref Pushes, 0);
                    long ticks = Interlocked.Exchange(ref TickerTicks, 0);
                    long beats = Interlocked.Exchange(ref UiBeats, 0);
                    long relayouts = Interlocked.Exchange(ref Relayouts, 0);
                    long resizes = Interlocked.Exchange(ref Resizes, 0);
                    long adopts = Interlocked.Exchange(ref Adopts, 0);
                    int gapMax = UiMaxGap; UiMaxGap = 0;
                    int trayMax = TrayMaxMs; TrayMaxMs = 0;

                    App.Paths.Log(string.Format(
                        "PERF cpu={0}% hot={1}#{2}({3}%) uiBeats={4}/3s paint={5} push={6} ticker={7} relayout={8} resize={9} adopt={10} uigap={11}ms trayms={12}",
                        cpu, Label(int.Parse(hot)), hot, Math.Round(best, 1), beats, paints, pushes, ticks,
                        relayouts, resizes, adopts, gapMax, trayMax));
                }
                catch { }
            }
        }
    }
}
