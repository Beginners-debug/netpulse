using System;
using System.Threading;
using System.Windows.Forms;
using NetPulse.App;
using NetPulse.Interop;

namespace NetPulse
{
    internal static class Program
    {
        const string MutexName = "NetPulse_SingleInstance_v3";
        const string ShowEventName = "NetPulse_ShowSettings_v3";

        [STAThread]
        static void Main(string[] args)
        {
            // 命令行
            var opt = CommandLine.Parse(args);

            // 自检：不显示任何界面，跑完写日志并退出（退出码 = 失败项数）
            if (opt.SelfTest)
            {
                Environment.Exit(SelfTest.Run());
                return;
            }

            // 按系统 DPI 感知，避免任务栏几何被虚拟化
            try { Native.SetProcessDpiAwareness(1); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // 已经有一个在跑了：叫它把设置页弹出来，然后自己退出
                    try
                    {
                        EventWaitHandle ev;
                        if (EventWaitHandle.TryOpenExisting(ShowEventName, out ev))
                        {
                            ev.Set();
                            ev.Dispose();
                        }
                    }
                    catch { }
                    return;
                }

                Application.ThreadException += (s, e) => Crash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);

                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    var app = new PulseApp(showEvent, opt.OpenSettings, opt.Page, opt.Shot, opt.ShotPanel);

                    Perf.Name("ui");
                    Perf.Start();
                    if (Perf.Enabled)
                    {
                        var beat = new System.Windows.Forms.Timer { Interval = 100 };
                        beat.Tick += (s, e) => Perf.Beat();
                        beat.Start();
                    }

                    Application.Run(app);
                }

                GC.KeepAlive(mutex);
            }
        }

        static void Crash(Exception ex)
        {
            try
            {
                if (ex == null) return;
                Paths.Log("未处理异常: " + ex.GetType().Name + " · " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
            catch { }
        }

        /// <summary>命令行参数。</summary>
        sealed class CommandLine
        {
            public bool OpenSettings;
            public int Page;
            public string Shot;
            public string ShotPanel;
            public bool SelfTest;

            public static CommandLine Parse(string[] args)
            {
                var o = new CommandLine();
                if (args == null) return o;

                foreach (string a in args)
                {
                    if (string.IsNullOrEmpty(a)) continue;

                    if (Eq(a, "--settings") || Eq(a, "-s")) o.OpenSettings = true;
                    else if (Eq(a, "--selftest")) o.SelfTest = true;
                    else if (Prefix(a, "--page="))
                    {
                        int v;
                        if (int.TryParse(Value(a, 7), out v)) { o.Page = v; o.OpenSettings = true; }
                    }
                    else if (Prefix(a, "--shotpanel="))
                    {
                        o.ShotPanel = Value(a, 12).Trim('"');
                        o.OpenSettings = true;
                    }
                    else if (Prefix(a, "--shot="))
                    {
                        o.Shot = Value(a, 7).Trim('"');
                        o.OpenSettings = true;
                    }
                }
                return o;
            }

            static bool Eq(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
            static bool Prefix(string a, string p) { return a.StartsWith(p, StringComparison.OrdinalIgnoreCase); }
            static string Value(string a, int skip) { return a.Length > skip ? a.Substring(skip) : ""; }
        }
    }
}
