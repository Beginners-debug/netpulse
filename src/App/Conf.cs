using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace NetPulse.App
{
    /// <summary>运行时路径：优先 exe 同目录（便携），不可写时回落 %APPDATA%\NetPulse。</summary>
    internal static class Paths
    {
        public static readonly string BaseDir;
        public static readonly string IniPath;
        public static readonly string DataDir;
        public static readonly string TrafficPath;
        public static readonly string LogPath;

        static Paths()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            exeDir = exeDir.TrimEnd('\\');

            string dir = exeDir;
            try
            {
                string probe = Path.Combine(exeDir, ".np_write_test");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
            }
            catch
            {
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetPulse");
            }

            BaseDir = dir;
            try { Directory.CreateDirectory(dir); } catch { }
            DataDir = Path.Combine(dir, "data");
            try { Directory.CreateDirectory(DataDir); } catch { }

            IniPath = Path.Combine(dir, "netpulse.ini");
            TrafficPath = Path.Combine(DataDir, "traffic.json");
            LogPath = Path.Combine(DataDir, "netpulse.log");
        }

        public static void Log(string message)
        {
            try
            {
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 256 * 1024)
                {
                    File.Delete(LogPath);
                }
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    /// <summary>配置：单文件 ini，键值平铺，人可以直接改。</summary>
    internal sealed class Conf
    {
        readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly string _path;
        public event Action Changed;

        // ---- 常规 ----
        public string ThemeKey = "cream";
        public int FontScale = 100;          // 80 - 140
        public int UnitMode = Fmt.UnitAuto;  // 0 自动 1 B/s 2 KB/s 3 MB/s

        // ---- 任务栏组件 ----
        public bool WidgetOn = true;
        public bool SingleLine;
        public bool ShowSpark = true;
        public bool ShowPing = true;
        public bool Pill = true;             // 半透明圆角卡片背景
        public int Alpha = 92;               // 卡片不透明度
        public int WidgetScale = 100;        // 任务栏组件自身大小（%）——和界面字号解耦
        public int Adapter = -1;             // -1 全部网卡
        public int SampleMs = 1000;
        public bool ClickOpensPanel = true;
        public int OffsetX;                  // 相对托盘左边的横向微调

        // ---- 网络探测 ----
        /// <summary>加载时做过旧配置迁移，调用方应该立即回写一次。</summary>
        public bool Migrated;

        public string PingTarget = "1.1.1.1";
        public int PingMs = 1000;
        public bool PingOn = true;

        // ---- 行为 ----
        public bool AutoStart;
        public bool ShowPanelOnStart = false;   // 默认不弹：开机就弹一张卡片会让人觉得"这软件在抢屏幕"

        public Conf(string path)
        {
            _path = path;
            Load();
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                foreach (string raw in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    _map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { Paths.Log("读取配置失败: " + ex.Message); }

            ThemeKey = S("theme", ThemeKey);
            FontScale = I("fontScale", FontScale).Clamp(80, 150);
            UnitMode = I("unit", UnitMode).Clamp(0, 3);

            WidgetOn = B("widget", WidgetOn);
            SingleLine = B("singleLine", SingleLine);
            ShowSpark = B("spark", ShowSpark);
            ShowPing = B("showPing", ShowPing);
            Pill = B("pill", Pill);
            Alpha = I("alpha", Alpha).Clamp(35, 100);
            WidgetScale = I("widgetScale", WidgetScale).Clamp(70, 140);
            Adapter = I("adapter", Adapter);
            SampleMs = I("sampleMs", SampleMs).Clamp(200, 10000);
            ClickOpensPanel = B("clickPanel", ClickOpensPanel);
            OffsetX = I("offsetX", OffsetX).Clamp(-40, 40);

            PingTarget = S("pingTarget", PingTarget);
            if (PingTarget == "223.5.5.5" && !_map.ContainsKey("ver"))
            {
                PingTarget = "1.1.1.1";   // 3.0 早期默认值指向国内 DNS，海外延迟很难看
                Migrated = true;
            }
            PingMs = I("pingMs", PingMs).Clamp(300, 10000);
            PingOn = B("pingOn", PingOn);

            AutoStart = B("autoStart", AutoStart);
            ShowPanelOnStart = B("panelOnStart", ShowPanelOnStart);
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder(1024);
                sb.Append("# NetPulse 配置文件（改完重启软件生效，也可以在设置界面里改）\r\n");
                sb.Append("[appearance]\r\n");
                sb.Append("theme=").Append(ThemeKey).Append("\r\n");
                sb.Append("fontScale=").Append(FontScale).Append("\r\n");
                sb.Append("unit=").Append(UnitMode).Append("\r\n");
                sb.Append("\r\n[widget]\r\n");
                sb.Append("widget=").Append(WidgetOn ? 1 : 0).Append("\r\n");
                sb.Append("singleLine=").Append(SingleLine ? 1 : 0).Append("\r\n");
                sb.Append("spark=").Append(ShowSpark ? 1 : 0).Append("\r\n");
                sb.Append("showPing=").Append(ShowPing ? 1 : 0).Append("\r\n");
                sb.Append("pill=").Append(Pill ? 1 : 0).Append("\r\n");
                sb.Append("alpha=").Append(Alpha).Append("\r\n");
            sb.Append("widgetScale=").Append(WidgetScale).Append("\r\n");
                sb.Append("adapter=").Append(Adapter).Append("\r\n");
                sb.Append("sampleMs=").Append(SampleMs).Append("\r\n");
                sb.Append("clickPanel=").Append(ClickOpensPanel ? 1 : 0).Append("\r\n");
                sb.Append("offsetX=").Append(OffsetX).Append("\r\n");
                sb.Append("\r\n[network]\r\n");
                sb.Append("pingTarget=").Append(PingTarget).Append("\r\n");
            sb.Append("ver=3").Append("\r\n");
                sb.Append("pingMs=").Append(PingMs).Append("\r\n");
                sb.Append("pingOn=").Append(PingOn ? 1 : 0).Append("\r\n");
                sb.Append("\r\n[behavior]\r\n");
                sb.Append("autoStart=").Append(AutoStart ? 1 : 0).Append("\r\n");
                sb.Append("panelOnStart=").Append(ShowPanelOnStart ? 1 : 0).Append("\r\n");

                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
            }
            catch (Exception ex) { Paths.Log("保存配置失败: " + ex.Message); }
        }

        public void Notify()
        {
            Save();
            var h = Changed;
            if (h != null) { try { h(); } catch { } }
        }

        // ---- 小工具 ----
        string S(string key, string dflt)
        {
            string v;
            return _map.TryGetValue(key, out v) && v.Length > 0 ? v : dflt;
        }

        int I(string key, int dflt)
        {
            string v;
            int n;
            return _map.TryGetValue(key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : dflt;
        }

        bool B(string key, bool dflt)
        {
            string v;
            if (!_map.TryGetValue(key, out v)) return dflt;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class IntExtensions
    {
        public static int Clamp(this int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        public static double Clamp(this double v, double min, double max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }

    /// <summary>开机自启：写 HKCU\...\Run，不需要管理员。</summary>
    internal static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "NetPulse";

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return key != null && key.GetValue(Name) != null;
                }
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;
                    if (on) key.SetValue(Name, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                    else if (key.GetValue(Name) != null) key.DeleteValue(Name, false);
                }
            }
            catch (Exception ex) { Paths.Log("设置开机自启失败: " + ex.Message); }
        }
    }
}
