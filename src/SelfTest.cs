using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using NetPulse.App;
using NetPulse.Core;

namespace NetPulse
{
    /// <summary>
    /// 自检：不需要人点界面，跑一遍核心逻辑（配置读写、流量存储、自启、格式化、网卡枚举、采样）。
    /// 结果写进 data/selftest.log，退出码 = 失败项数量。适合发布前跑一次，也可以挂到 CI。
    /// </summary>
    internal static class SelfTest
    {
        static readonly List<string> Lines = new List<string>();
        static int _failed;

        static void Ok(string name, string detail)
        {
            Lines.Add("PASS  " + name + (detail.Length > 0 ? "  [" + detail + "]" : ""));
        }

        static void Fail(string name, string detail)
        {
            _failed++;
            Lines.Add("FAIL  " + name + "  -> " + detail);
        }

        static void Check(string name, bool cond, string detail = "")
        {
            if (cond) Ok(name, detail); else Fail(name, detail.Length > 0 ? detail : "条件不成立");
        }

        public static int Run()
        {
            var sw = Stopwatch.StartNew();

            // 自检绝不能碰用户真正在用的 netpulse.ini —— 以前这里直接读写 Paths.IniPath，
            // 跑一次自检就把用户辛苦调好的配置覆盖成测试值（连默认探测目标都给改回了 223.5.5.5）。
            string ini = Path.Combine(Paths.DataDir, "selftest.ini");
            try { Directory.CreateDirectory(Paths.DataDir); if (File.Exists(ini)) File.Delete(ini); } catch { }

            // 1. 默认配置
            try
            {
                var c = new Conf(ini);
                Check("conf.default.theme", c.ThemeKey == "cream", c.ThemeKey);
                Check("conf.default.scale", c.FontScale == 100, c.FontScale.ToString());
                Check("conf.default.adapter", c.Adapter == -1, c.Adapter.ToString());
            }
            catch (Exception ex) { Fail("conf.default", ex.Message); }

            // 2. 配置往返
            try
            {
                var a = new Conf(ini)
                {
                    ThemeKey = "mint",
                    FontScale = 120,
                    OffsetX = 12,
                    SingleLine = true,
                    ShowSpark = false,
                    PingTarget = "1.1.1.1",
                    SampleMs = 2000,
                };
                a.Save();

                var b = new Conf(ini);
                b.Load();
                Check("conf.roundtrip.theme", b.ThemeKey == "mint", b.ThemeKey);
                Check("conf.roundtrip.scale", b.FontScale == 120, b.FontScale.ToString());
                Check("conf.roundtrip.offset", b.OffsetX == 12, b.OffsetX.ToString());
                Check("conf.roundtrip.singleLine", b.SingleLine, b.SingleLine.ToString());
                Check("conf.roundtrip.spark", !b.ShowSpark, b.ShowSpark.ToString());
                Check("conf.roundtrip.ping", b.PingTarget == "1.1.1.1", b.PingTarget);
                Check("conf.roundtrip.sample", b.SampleMs == 2000, b.SampleMs.ToString());

                var d = new Conf(ini);   // 还原默认，别把测试值留在机器上
                d.ThemeKey = "cream";
                d.FontScale = 100;
                d.OffsetX = 0;
                d.SingleLine = false;
                d.ShowSpark = true;
                d.PingTarget = "1.1.1.1";
                d.SampleMs = 1000;
                d.Save();
            }
            catch (Exception ex) { Fail("conf.roundtrip", ex.Message); }
            finally { try { if (File.Exists(ini)) File.Delete(ini); } catch { } }

            // 3. 格式化
            try
            {
                string v, u;
                Fmt.Speed(1048576, 0, out v, out u);
                Check("fmt.mb", v.StartsWith("1.0") && u == "MB/s", v + " " + u);

                Fmt.Speed(512, 0, out v, out u);
                Check("fmt.bytes", v == "512" && u == "B/s", v + " " + u);

                Fmt.Speed(2560, 0, out v, out u);
                Check("fmt.kb", v == "2.5" && u == "KB/s", v + " " + u);

                Check("fmt.tiny", Fmt.Tiny(1048576) == "1.0M", Fmt.Tiny(1048576));
                Check("fmt.loss", Fmt.Loss(12.34) == "12.3%", Fmt.Loss(12.34));
                Check("fmt.rtt", Fmt.Rtt(-1) == "超时", Fmt.Rtt(-1));
            }
            catch (Exception ex) { Fail("fmt", ex.Message); }

            // 4. 流量存储
            try
            {
                string temp = Path.Combine(Paths.DataDir, "selftest-traffic.json");
                try { File.Delete(temp); } catch { }

                var store = new TrafficStore(temp);
                long before = store.DayIn;
                store.Add(4096, 2048);
                store.Save();

                var again = new TrafficStore(temp);
                again.Load();
                Check("store.persist", again.DayIn >= before + 4096, "dayIn=" + again.DayIn + " before=" + before);
                Check("store.recent", again.Recent(30).Count >= 1, again.Recent(30).Count + " 天");

                try { File.Delete(temp); } catch { }
            }
            catch (Exception ex) { Fail("store", ex.Message); }

            // 5. 开机自启（写 HKCU，测完必须还原）
            try
            {
                bool was = Startup.IsEnabled();
                Startup.Set(true);
                Check("startup.on", Startup.IsEnabled());
                Startup.Set(false);
                Check("startup.off", !Startup.IsEnabled());
                if (was) Startup.Set(true);
            }
            catch (Exception ex) { Fail("startup", ex.Message); }

            // 6. 网卡枚举
            try
            {
                var list = IfTable.List();
                Check("iface.count", list.Count > 0, list.Count + " 个");
                bool noise = list.Exists(x => IfTable.IsNoise(x.Name, x.Description));
                Check("iface.noNoise", !noise, "无虚拟小端口");
                Check("iface.hasUp", list.Exists(x => x.Up), "存在已连接接口");
            }
            catch (Exception ex) { Fail("iface", ex.Message); }

            // 7. 采样 1.6 秒
            try
            {
                using (var mon = new NetMonitor())
                {
                    mon.IntervalMs = 500;
                    mon.PingEnabled = false;
                    mon.Start();
                    Thread.Sleep(1600);

                    var s = mon.Sample;
                    Check("monitor.alive", s.AdapterCount > 0 || s.LinkUp, "adapters=" + s.AdapterCount);
                    Check("monitor.name", mon.AdapterName().Length > 0, mon.AdapterName());
                    Check("monitor.dt", mon.LastDtSec > 0, mon.LastDtSec.ToString("0.00") + "s");
                    Check("monitor.rate", s.Down >= 0 && s.Up >= 0, "down=" + s.Down.ToString("0") + " up=" + s.Up.ToString("0"));
                }
            }
            catch (Exception ex) { Fail("monitor", ex.Message); }

            // 8. 主题表
            try
            {
                Check("theme.count", Theme.All.Length >= 5, Theme.All.Length + " 套");
                Check("theme.night", Theme.Get("night").Dark, "night 是深色");
                Check("theme.fallback", Theme.Get("no-such-theme") != null, "未知主题回落默认");
            }
            catch (Exception ex) { Fail("theme", ex.Message); }

            sw.Stop();
            var head = new StringBuilder();
            head.AppendLine("NetPulse 自检  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            head.AppendLine((_failed == 0 ? "全部通过" : _failed + " 项失败") + "，用时 " + sw.ElapsedMilliseconds + " ms");
            head.AppendLine(new string('-', 56));

            string text = head + string.Join(Environment.NewLine, Lines.ToArray()) + Environment.NewLine;
            try
            {
                Directory.CreateDirectory(Paths.DataDir);
                File.WriteAllText(Path.Combine(Paths.DataDir, "selftest.log"), text, new UTF8Encoding(false));
            }
            catch { }
            try { Console.WriteLine(text); } catch { }
            return _failed;
        }
    }
}
