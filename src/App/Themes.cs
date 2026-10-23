using System;
using System.Drawing;
using System.Globalization;

namespace NetPulse.App
{
    /// <summary>所有主题。深色只有一个，其余都是奶油系浅色。</summary>
    internal sealed class Theme
    {
        public string Key = "";
        public string Name = "";
        public bool Dark;

        public Color Bg;        // 窗口底
        public Color Card;      // 卡片
        public Color Card2;     // 卡片里更深一层的块
        public Color Line;      // 描边 / 分隔
        public Color Text;      // 主文字
        public Color Sub;       // 次文字
        public Color Down;      // 下行主色
        public Color Up;        // 上行主色
        public Color Accent;    // 强调色
        public Color Accent2;   // 次强调色
        public Color Hover;
        public Color Warn = Color.FromArgb(255, 138, 120);

        public Color Tint(Color c, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c);
        }

        public static readonly Theme[] All =
        {
            new Theme
            {
                Key = "cream", Name = "奶油布丁", Dark = false,
                Bg = Color.FromArgb(0xFF, 0xF8, 0xF0), Card = Color.White, Card2 = Color.FromArgb(0xFF, 0xF1, 0xE3),
                Line = Color.FromArgb(0xF4, 0xE3, 0xD3), Text = Color.FromArgb(0x4B, 0x3A, 0x31),
                Sub = Color.FromArgb(0xA7, 0x91, 0x84), Down = Color.FromArgb(0xFF, 0x7E, 0x9D),
                Up = Color.FromArgb(0x35, 0xC4, 0xAE), Accent = Color.FromArgb(0xFF, 0xB2, 0x5E),
                Accent2 = Color.FromArgb(0x8E, 0xC5, 0xFF), Hover = Color.FromArgb(0xFF, 0xF3, 0xE4)
            },
            new Theme
            {
                Key = "sakura", Name = "樱花牛奶", Dark = false,
                Bg = Color.FromArgb(0xFF, 0xF3, 0xF7), Card = Color.White, Card2 = Color.FromArgb(0xFF, 0xE8, 0xEF),
                Line = Color.FromArgb(0xFA, 0xDD, 0xE6), Text = Color.FromArgb(0x5A, 0x34, 0x41),
                Sub = Color.FromArgb(0xB7, 0x8D, 0x9A), Down = Color.FromArgb(0xFF, 0x6F, 0x91),
                Up = Color.FromArgb(0x57, 0xC7, 0xB8), Accent = Color.FromArgb(0xFF, 0x9E, 0xB5),
                Accent2 = Color.FromArgb(0xC3, 0xA6, 0xFF), Hover = Color.FromArgb(0xFF, 0xEC, 0xF2)
            },
            new Theme
            {
                Key = "mint", Name = "薄荷苏打", Dark = false,
                Bg = Color.FromArgb(0xF2, 0xFC, 0xF9), Card = Color.White, Card2 = Color.FromArgb(0xE8, 0xF8, 0xF4),
                Line = Color.FromArgb(0xD8, 0xF0, 0xEA), Text = Color.FromArgb(0x2F, 0x4B, 0x47),
                Sub = Color.FromArgb(0x85, 0xA8, 0xA1), Down = Color.FromArgb(0x38, 0xB6, 0xA6),
                Up = Color.FromArgb(0x6C, 0xC0, 0x72), Accent = Color.FromArgb(0x52, 0xC7, 0xB8),
                Accent2 = Color.FromArgb(0x9A, 0xD9, 0xFF), Hover = Color.FromArgb(0xE8, 0xF8, 0xF4)
            },
            new Theme
            {
                Key = "grape", Name = "葡萄汽水", Dark = false,
                Bg = Color.FromArgb(0xF7, 0xF3, 0xFF), Card = Color.White, Card2 = Color.FromArgb(0xF0, 0xEA, 0xFD),
                Line = Color.FromArgb(0xE4, 0xDA, 0xFA), Text = Color.FromArgb(0x3F, 0x33, 0x55),
                Sub = Color.FromArgb(0x99, 0x8A, 0xB0), Down = Color.FromArgb(0x8B, 0x6B, 0xF0),
                Up = Color.FromArgb(0x35, 0xC4, 0xAE), Accent = Color.FromArgb(0xA9, 0x8B, 0xFF),
                Accent2 = Color.FromArgb(0xFF, 0xA8, 0xC5), Hover = Color.FromArgb(0xF2, 0xEC, 0xFF)
            },
            new Theme
            {
                Key = "night", Name = "夜莓布丁", Dark = true,
                Bg = Color.FromArgb(0x20, 0x1A, 0x26), Card = Color.FromArgb(0x2C, 0x25, 0x34),
                Card2 = Color.FromArgb(0x36, 0x2D, 0x41), Line = Color.FromArgb(0x3B, 0x31, 0x48),
                Text = Color.FromArgb(0xF7, 0xEF, 0xF7), Sub = Color.FromArgb(0xA7, 0x95, 0xB3),
                Down = Color.FromArgb(0xFF, 0x9D, 0xB4), Up = Color.FromArgb(0x74, 0xE0, 0xCE),
                Accent = Color.FromArgb(0xC7, 0x92, 0xEA), Accent2 = Color.FromArgb(0x8A, 0xB4, 0xFF),
                Hover = Color.FromArgb(0x3A, 0x31, 0x47)
            }
        };

        public static Theme Get(string key)
        {
            foreach (var t in All) if (t.Key == key) return t;
            return All[0];
        }

        public static int IndexOf(string key)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Key == key) return i;
            return 0;
        }
    }

    /// <summary>数字 + 单位的格式化。全静态、无分配（除返回值）。</summary>
    internal static class Fmt
    {
        public const int UnitAuto = 0, UnitB = 1, UnitKB = 2, UnitMB = 3;

        /// <summary>速度拆成「数值」「单位」两段，方便排版。</summary>
        public static void Speed(double bytesPerSec, int unitMode, out string value, out string unit)
        {
            double v = bytesPerSec < 0 ? 0 : bytesPerSec;
            switch (unitMode)
            {
                case UnitB:
                    value = ((long)v).ToString(CultureInfo.InvariantCulture);
                    unit = "B/s";
                    return;
                case UnitKB:
                    value = (v / 1024.0).ToString(v < 1024 * 100 ? "0.0" : "0", CultureInfo.InvariantCulture);
                    unit = "KB/s";
                    return;
                case UnitMB:
                    value = (v / 1048576.0).ToString("0.00", CultureInfo.InvariantCulture);
                    unit = "MB/s";
                    return;
            }

            if (v < 1024) { value = ((long)v).ToString(CultureInfo.InvariantCulture); unit = "B/s"; }
            else if (v < 1048576) { value = (v / 1024.0).ToString(v < 102400 ? "0.0" : "0", CultureInfo.InvariantCulture); unit = "KB/s"; }
            else if (v < 1073741824) { value = (v / 1048576.0).ToString(v < 104857600 ? "0.00" : "0.0", CultureInfo.InvariantCulture); unit = "MB/s"; }
            else { value = (v / 1073741824.0).ToString("0.00", CultureInfo.InvariantCulture); unit = "GB/s"; }
        }

        /// <summary>任务栏用的极简写法：1.2M / 340K / 12B。</summary>
        public static string Tiny(double bytesPerSec)
        {
            double v = bytesPerSec < 0 ? 0 : bytesPerSec;
            if (v < 1000) return ((long)v).ToString(CultureInfo.InvariantCulture);
            if (v < 1024 * 1000) return (v / 1024.0).ToString(v < 102400 ? "0.0" : "0", CultureInfo.InvariantCulture) + "K";
            if (v < 1024.0 * 1024 * 1000) return (v / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + "M";
            return (v / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + "G";
        }

        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1073741824L) return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            if (bytes < 1099511627776L) return (bytes / 1073741824.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            return (bytes / 1099511627776.0).ToString("0.00", CultureInfo.InvariantCulture) + " TB";
        }

        /// <summary>拆成数值 + 单位，用于大小不一的排版。</summary>
        public static void Bytes(long bytes, out string value, out string unit)
        {
            string s = Bytes(bytes);
            int sp = s.LastIndexOf(' ');
            if (sp < 0) { value = s; unit = ""; }
            else { value = s.Substring(0, sp); unit = s.Substring(sp + 1); }
        }

        public static string Rtt(int ms)
        {
            return ms < 0 ? "超时" : ms + " ms";
        }

        public static string Loss(double pct)
        {
            return pct <= 0.01 ? "0%" : pct.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        public static string SpeedFull(double bps, int unitMode)
        {
            string v, u;
            Speed(bps, unitMode, out v, out u);
            return v + " " + u;
        }
    }
}
