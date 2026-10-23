using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NetPulse.App;
using NetPulse.Core;
using NetPulse.Interop;

namespace NetPulse.Ui
{
    /// <summary>
    /// 托盘图标：糖果色圆角小方块 + 三根速度电平条，随实时速率长高。
    /// 只在显示值真的变了才重建图标，避免每秒刷新导致的托盘闪烁。
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        const int Size = 32;

        readonly NotifyIcon _ni;
        System.Drawing.Icon _icon;
        string _key = "";
        bool _dark;

        public event Action LeftClick;
        public event Action RightClick;

        /// <summary>把回调挪出 shell 的同步调用栈，避免 explorer 等我们一起卡住。</summary>
        void Raise(Action handler)
        {
            if (handler == null) return;
            var ui = _ui;
            if (ui == null) { handler(); return; }
            ui.Post(delegate { try { handler(); } catch (Exception ex) { App.Paths.Log("托盘回调异常: " + ex.Message); } }, null);
        }

        readonly System.Threading.SynchronizationContext _ui;

        public TrayIcon()
        {
            // 托盘图标的鼠标事件，是 shell(explorer) 在处理“你点了通知区域”时
            // 同步回调到我们这个进程里的。如果在这里直接打开面板（建窗口、载字体、
            // 甚至只是慢 80ms），explorer 的 UI 线程就得跟着一起等——表现就是
            // “任务栏卡死、图标点不动、整个桌面假死一下”。
            // 所以事件一律先 Post 回消息队列，让 shell 的这次回调立刻返回。
            _ui = System.Threading.SynchronizationContext.Current;

            _ni = new NotifyIcon
            {
                Visible = true,
                Text = "NetPulse 3.0",
                ContextMenuStrip = null
            };

            _ni.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) Raise(LeftClick);
                else if (e.Button == MouseButtons.Right) Raise(RightClick);
            };

            _ni.DoubleClick += (s, e) => Raise(LeftClick);

            _dark = !Native.IsTaskbarLight();
            _icon = Build(0, 0, true);
            _ni.Icon = _icon;
        }

        public System.Drawing.Icon Icon { get { return _icon; } }

        public void Update(NetSample s, bool online)
        {
            string key = Key(s, online);
            if (key == _key) return;
            _key = key;

            var old = _icon;
            _icon = Build(s.Down, s.Up, online);
            _ni.Icon = _icon;

            if (old != null)
            {
                IntPtr h = old.Handle;
                old.Dispose();
                if (h != IntPtr.Zero) Native.DestroyIcon(h);
            }
        }

        public void SetTooltip(string text)
        {
            if (text == null) text = "NetPulse 3.0";
            if (text.Length > 63) text = text.Substring(0, 63);
            try { if (_ni.Text != text) _ni.Text = text; } catch { }
        }

        static string Key(NetSample s, bool online)
        {
            double d = s.Down, u = s.Up;
            int dl = d <= 0 ? 0 : (int)Math.Min(9, Math.Log10(1 + d) * 2);
            int ul = u <= 0 ? 0 : (int)Math.Min(9, Math.Log10(1 + u) * 2);
            return (online ? "1" : "0") + dl + "-" + ul;
        }

        System.Drawing.Icon Build(double down, double up, bool online)
        {
            var t = Skin.T;
            using (var bmp = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                var plate = new RectangleF(1, 1, Size - 2, Size - 2);
                Color c1 = online ? t.Down : Color.FromArgb(180, 180, 180);
                Color c2 = online ? t.Up : Color.FromArgb(150, 150, 150);

                using (var path = Round.Make(plate, Size * 0.32f))
                using (var brush = new LinearGradientBrush(plate, c1, c2, 60f))
                {
                    g.FillPath(brush, path);
                    using (var glow = new Pen(Color.FromArgb(70, Color.White), 1.2f))
                        g.DrawPath(glow, path);
                }

                // 三根电平条：高度按下载速率对数增长
                float peak = 100f * 1024 * 1024;               // 100 Mbps 视作满格
                double v = Math.Max(down, up * 1.2);
                float t01 = (float)Math.Min(1.0, Math.Log10(1 + v) / Math.Log10(1 + peak));
                float act = 0.34f + 0.66f * Math.Max(0f, t01);

                float baseY = 24.5f;
                float[] full = { 8f, 14f, 20f };
                float[] xs = { 8.5f, 14f, 19.5f };
                using (var white = new SolidBrush(Color.FromArgb(242, Color.White)))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float h = Math.Max(2.6f, full[i] * act);
                        var bar = new RectangleF(xs[i], baseY - h, 4f, h);
                        using (var bp = Round.Make(bar, 2f)) g.FillPath(white, bp);
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (var tmp = System.Drawing.Icon.FromHandle(hIcon))
                        return (System.Drawing.Icon)tmp.Clone();
                }
                finally { Native.DestroyIcon(hIcon); }
            }
        }

        public void Dispose()
        {
            try { _ni.Visible = false; } catch { }
            _ni.Dispose();
            if (_icon != null) { try { _icon.Dispose(); } catch { } }
        }
    }
}
