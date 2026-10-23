using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;

namespace NetPulse.Ui
{
    /// <summary>
    /// 两套内嵌字体：
    ///   digitfont.ttf → Fredoka（ASCII 子集，圆润的数字字体）—— 用于速率、延迟等所有数字
    ///   cjkfont.ttf  → 站酷快乐体（只留界面用到的汉字）—— 用于中文标题
    /// 正文仍用微软雅黑，保证小字可读。
    /// 字体只创建一次并缓存 —— 每秒 new Font 会让 GDI+ 字形缓存一路涨到几十 MB。
    /// </summary>
    internal static class UiFont
    {
        static readonly Dictionary<string, Font> Cache = new Dictionary<string, Font>(48);
        static readonly object Sync = new object();

        static FontFamily _numFamily, _titleFamily;
        static string _numName = "", _titleName = "";
        static bool _loaded;

        public static string CuteName { get { Ensure(); return _numName; } }
        public static string TitleName { get { Ensure(); return _titleName; } }

        static string FontDir
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetPulse", "fonts");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        static void Ensure()
        {
            if (_loaded) return;
            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;
                _numFamily = Load("NetPulse.digitfont.ttf", "digit", out _numName);
                _titleFamily = Load("NetPulse.cjkfont.ttf", "cjk", out _titleName);
                if (_numFamily == null) _numName = PickFallback("Fredoka", "Segoe UI Variable Display");
                if (_titleFamily == null) _titleName = PickFallback("站酷快乐体", "ZCOOL KuaiLe");
            }
        }

        /// <summary>解出内嵌字体并注册成私有字体族（用内容哈希命名，避免撞上被 GDI+ 锁住的旧文件）。</summary>
        static FontFamily Load(string resource, string prefix, out string familyName)
        {
            familyName = "";
            try
            {
                byte[] bytes = ReadResource(resource);
                if (bytes == null || bytes.Length == 0) return null;

                string dir = FontDir;
                string file = Path.Combine(dir, prefix + "-" + Hash(bytes).ToString("x8") + ".ttf");
                if (!File.Exists(file))
                {
                    string tmp = file + ".tmp";
                    File.WriteAllBytes(tmp, bytes);
                    try { File.Move(tmp, file); }
                    catch { try { File.Delete(tmp); } catch { } }
                }
                if (!File.Exists(file)) return null;

                Cleanup(dir, prefix + "-", Path.GetFileName(file));

                var pfc = new PrivateFontCollection();
                pfc.AddFontFile(file);
                if (pfc.Families.Length == 0) return null;
                familyName = pfc.Families[0].Name;
                return pfc.Families[0];
            }
            catch (Exception ex)
            {
                App.Paths.Log("加载字体失败(" + resource + "): " + ex.Message);
                return null;
            }
        }

        static byte[] ReadResource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        static uint Hash(byte[] data)
        {
            uint h = 2166136261;
            for (int i = 0; i < data.Length; i++) { h ^= data[i]; h *= 16777619; }
            return h;
        }

        static void Cleanup(string dir, string prefix, string keep)
        {
            try
            {
                foreach (string old in Directory.GetFiles(dir, prefix + "*.ttf"))
                {
                    if (string.Equals(Path.GetFileName(old), keep, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(old); } catch { }
                }
            }
            catch { }
        }

        static string PickFallback(params string[] candidates)
        {
            using (var installed = new InstalledFontCollection())
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in installed.Families) names.Add(f.Name);
                foreach (string c in candidates) if (names.Contains(c)) return c;
            }
            return "Segoe UI";
        }

        static Font Make(FontFamily family, string fallbackName, float px, FontStyle style)
        {
            string key = (family != null ? family.Name : fallbackName) + "|" + px.ToString("0.0") + "|" + (int)style;
            lock (Sync)
            {
                Font f;
                if (Cache.TryGetValue(key, out f) && f != null) return f;
                try
                {
                    // 私有字体必须用 FontFamily 对象创建，用字符串名会落到系统字体上
                    f = family != null
                        ? new Font(family, px, style, GraphicsUnit.Pixel)
                        : new Font(fallbackName, px, style, GraphicsUnit.Pixel);
                }
                catch
                {
                    f = new Font("Microsoft YaHei UI", px, style, GraphicsUnit.Pixel);
                }
                Cache[key] = f;
                return f;
            }
        }

        /// <summary>中文标题用（手写感的快乐体）。</summary>
        public static Font Cute(float px, FontStyle style = FontStyle.Regular)
        {
            Ensure();
            return Make(_titleFamily, _titleName, px, style);
        }

        /// <summary>数字 / 速率 / 英文标签用（Fredoka，圆润且数字不会变成方块）。</summary>
        public static Font Num(float px, FontStyle style = FontStyle.Regular)
        {
            Ensure();
            return Make(_numFamily, _numName, px, style);
        }

        /// <summary>正文字体（说明文字、按钮）。</summary>
        public static Font Ui(float px, FontStyle style = FontStyle.Regular)
        {
            return Get("Microsoft YaHei UI", px, style);
        }

        static Font Get(string family, float px, FontStyle style)
        {
            string key = family + "|" + px.ToString("0.0") + "|" + (int)style;
            lock (Sync)
            {
                Font f;
                if (Cache.TryGetValue(key, out f) && f != null) return f;
                try
                {
                    f = new Font(family, px, style, GraphicsUnit.Pixel);
                    if (f.Name != family) { f.Dispose(); f = new Font("Microsoft YaHei UI", px, style, GraphicsUnit.Pixel); }
                }
                catch
                {
                    f = new Font("Microsoft YaHei UI", px, style, GraphicsUnit.Pixel);
                }
                Cache[key] = f;
                return f;
            }
        }

        /// <summary>按界面缩放比例取字号。</summary>
        public static float Scale(int percent)
        {
            return Math.Max(60, Math.Min(180, percent)) / 100f;
        }
    }
}
