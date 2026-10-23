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
    /// 任务栏小组件。
    ///
    /// 关键：它不是靠 SetWindowPos(HWND_TOPMOST) 抢救置顶（那样会被任务栏盖住），
    /// 而是把窗口的 owner 设成 Shell_TrayWnd。被拥有的窗口在 z 序上恒在 owner 之上，
    /// 这是窗口管理器保证的关系，所以任务栏重绘、全屏切换、资源管理器重启都不会再压住它。
    /// </summary>
    internal sealed class TaskbarWidget : LayeredForm
    {
        readonly Conf _conf;
        readonly NetMonitor _mon;

        readonly float[] _spark = new float[64];
        int _sparkPos;

        string _line1 = "", _unit1 = "", _line2 = "", _unit2 = "", _ping = "";
        int _textW = 60;
        NetSample _last;
        DateTime _lastPaint = DateTime.MinValue;

        IntPtr _ownedBy;
        readonly Timer _watch = new Timer { Interval = 2000 };
        TaskbarInfo _taskbar;
        bool _hiddenByFullscreen;
        bool _inAdopt;

        public event Action Clicked;
        public event Action RightClicked;

        public TaskbarWidget(Conf conf, NetMonitor mon)
        {
            _conf = conf;
            _mon = mon;

            ShowInTaskbar = false;
            _watch.Tick += (s, e) => Watchdog();
            _watch.Start();

            // 不再挂全局 WinEvent 钩子：EVENT_OBJECT_SHOW 是系统级事件，
            // 桌面上一有窗口弹出就会回调过来，每次回调都要查一遍任务栏，
            // 会让整个 shell 的事件派发被拖慢（开始菜单、任务栏动画发涩）。
            // explorer 重启由 2 秒看门狗兜住。
        }

        protected override bool CloseOnOutsideClick { get { return false; } }

        // ---------------- 生命周期 ----------------

        public bool Shown { get; private set; }

        public void ShowWidget()
        {
            Shown = _conf.WidgetOn;
            if (!_conf.WidgetOn) { if (Visible) Hide(); return; }
            if (Native.IsFullscreenAppRunning()) { _hiddenByFullscreen = true; if (Visible) Hide(); return; }

            _hiddenByFullscreen = false;
            var tb = TaskbarInfo.Query();
            if (tb == null) { if (Visible) Hide(); return; }

            _taskbar = tb;
            if (!Visible) Show();

            AdoptTaskbar(tb.Handle);
            Rebuild(true);
            Relayout();
            TopMostSafe();
        }

        public void HideWidget()
        {
            Shown = false;
            if (Visible) Hide();
        }

        public bool WidgetVisible { get { return Visible; } }

        /// <summary>最近一次由看门狗/重建拿到的任务栏几何，点击路径直接用它，不再跨进程查询。</summary>
        public TaskbarInfo CachedTaskbar { get { return _taskbar; } }

        /// <summary>配置或主题变了，整个重排。</summary>
        public void Rebuild(bool force)
        {
            if (!Visible && !force) return;
            System.Threading.Interlocked.Increment(ref Perf.Relayouts);
            BuildText();
            Relayout();
        }

        void AdoptTaskbar(IntPtr taskbar)
        {
            if (taskbar == IntPtr.Zero || _inAdopt) return;

            // 先把窗口挂到任务栏名下（改 owner 要求窗口先隐藏，否则偶发不生效）
            if (_ownedBy == taskbar) { Position(); return; }

            System.Threading.Interlocked.Increment(ref Perf.Adopts);
            _inAdopt = true;
            try
            {
                bool wasVisible = Visible;
                if (wasVisible) Hide();
                Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, taskbar);
                _ownedBy = taskbar;
                if (wasVisible) Show();
                // 只在这里抬一次 z-order（被拥有的窗口本来就在 owner 之上，这里只是兜底）
                if (Visible)
                    Native.SetWindowPos(Handle, Native.HWND_TOP, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                Position();
            }
            finally { _inAdopt = false; }
        }

        void TopMostSafe()
        {
            if (!Visible) return;
            Native.SetWindowPos(Handle, Native.HWND_TOP, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // ---------------- 文案 ----------------

        void BuildText()
        {
            var s = _mon.Sample;
            _last = s;

            string v1, u1, v2, u2;
            Fmt.Speed(s.Down, _conf.UnitMode, out v1, out u1);
            Fmt.Speed(s.Up, _conf.UnitMode, out v2, out u2);
            _unit1 = u1;
            _unit2 = u2;

            if (_conf.SingleLine)
            {
                _line1 = v1;
                _line2 = v2;
            }
            else
            {
                _line1 = v1;
                _line2 = v2;
            }

            _ping = s.Rtt < 0 ? "—" : s.Rtt + "ms";
        }

        // ---------------- 布局 ----------------

        void Relayout()
        {
            var tb = _taskbar;
            if (tb == null) return;

            // 组件可用的“车道”：任务栏按钮右边缘 到 通知区域左边缘。
            // 超出这条道就会盖住别人的按钮，所以超了就等比缩回来。
            int lane = Math.Max(90, tb.Width / 3);
            if (tb.TrayLeft > 0 && tb.ButtonsRight > 0 && tb.ButtonsRight < tb.TrayLeft)
                lane = Math.Max(90, tb.TrayLeft - tb.ButtonsRight - 6);

            int w, h;
            _fit = 1f;
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                var m = MeasureAll(g);
                float s = Ws;
                w = (int)Math.Ceiling(m.W + 16 * s);
                h = (int)Math.Ceiling(m.H + (_conf.Pill ? 9 * s : 5 * s));

                if (w > lane)
                {
                    // 放不下就整体缩小（下限 55%），保证不越界去盖任务栏按钮
                    _fit = Math.Max(0.55f, lane / (float)w);
                    m = MeasureAll(g);
                    s = Ws;
                    w = (int)Math.Ceiling(m.W + 16 * s);
                    h = (int)Math.Ceiling(m.H + (_conf.Pill ? 9 * s : 5 * s));
                }
            }

            h = Math.Min(h, tb.Height - 2);
            w = Math.Min(w, Math.Max(56, lane));

            if (Width != w || Height != h)
            {
                System.Threading.Interlocked.Increment(ref Perf.Resizes);
                ClientSize = new Size(w, h);
                EnsureCanvas(w, h);
            }
            EnsureCanvas(Width, Height);
            Position();
        }

        void Position()
        {
            var tb = _taskbar;
            if (tb == null || !Visible) return;

            int x, y;
            if (tb.Vertical)
            {
                x = tb.Left + (tb.Width - Width) / 2;
                y = (tb.TrayLeft > 0 ? tb.TrayLeft - Height - 4 : tb.Bottom - Height - 4) - _conf.OffsetX;
            }
            else
            {
                int anchor = tb.TrayLeft > 0 ? tb.TrayLeft : tb.Right;
                x = anchor - Width - 4 + _conf.OffsetX;
                y = tb.Top + (tb.Height - Height) / 2;
            }

            // 兜底：无论如何都要夹进任务栏矩形（曾因 Edge 误判把窗口放到屏幕外 y=1379）
            if (x < tb.Left) x = tb.Left;
            if (tb.Vertical == false && x + Width > tb.Right) x = tb.Right - Width;
            if (y < tb.Top) y = tb.Top;
            if (y + Height > tb.Bottom) y = tb.Bottom - Height;

            if (Left != x || Top != y)
            {
                Left = x;
                Top = y;
            }
        }

        // ---------------- 数据 ----------------

        /// <summary>吃进一份新样本：更新文字用的数字 + 迷你曲线环形缓冲 + 峰值。</summary>
        void ApplySample(NetSample s)
        {
            _last = s;

            float peak = 1;
            for (int i = 0; i < _spark.Length; i++) if (_spark[i] > peak) peak = _spark[i];
            _spark[_sparkPos] = (float)s.Down;
            _sparkPos = (_sparkPos + 1) % _spark.Length;
            for (int i = 0; i < _spark.Length; i++) if (_spark[i] > peak) peak = _spark[i];
            _peak = peak;
        }

        int _sparkMoved = -1;
        float _peak = 1;
        float _numW = 32;

        /// <summary>界面线程调用：值变了才重绘。</summary>
        public void RefreshView()
        {
            RefreshView(_mon.Sample);
        }

        /// <summary>
        /// 界面上真正刷新的一步。以前这里读的是 _last，而 _last 只在 Rebuild 时更新，
        /// 去掉组件自己的 _mon.Tick 订阅之后就没有人再写它了 —— 于是数字在屏幕上定格，
        /// 只有改配置触发重排时才会跳一下。现在每 tick 都吃新样本（和面板用同一份）。
        /// </summary>
        public void RefreshView(NetSample s)
        {
            if (!Visible) return;
            if (_hiddenByFullscreen && !Native.IsFullscreenAppRunning()) ShowWidget();
            if (_conf.WidgetOn == false) return;

            ApplySample(s);

            string v1, u1, v2, u2;
            Fmt.Speed(s.Down, _conf.UnitMode, out v1, out u1);
            Fmt.Speed(s.Up, _conf.UnitMode, out v2, out u2);
            string ping = s.Rtt < 0 ? "超时" : s.Rtt + "ms";

            bool textChanged = v1 != _line1 || v2 != _line2 || u1 != _unit1 || u2 != _unit2 || ping != _ping;
            bool sparkChanged = _conf.ShowSpark && (_sparkMoved != _sparkPos);

            // 数值和曲线都没变化就什么都不做。
            // 之前的实现有个 480ms 的重画下限，也就是静置时也在以 2 次/秒往屏幕上推全窗口位图，
            // 长期挂着会一直占着 DWM 的合成带宽（看视频/玩游戏时尤其明显）。
            if (!textChanged && !sparkChanged) return;

            _line1 = v1; _unit1 = u1; _line2 = v2; _unit2 = u2; _ping = ping;
            _sparkMoved = _sparkPos;
            _lastPaint = DateTime.UtcNow;
            Repaint();
        }

        // ---------------- 度量 ----------------

        struct Metrics
        {
            public float W, H;          // 内容宽高
            public float NumCol;        // 数字列（右对齐，位数变化不抖）
            public float UnitW;
            public float ArrowW;
            public float EntryW;        // 一行「箭头+数字+单位」
            public float PingW;
            public float SparkW;
            public bool Single;
        }

        Metrics MeasureAll(Graphics g)
        {
            float s = Ws;
            var fNum = NumFont;
            var fUnit = UnitFont;
            var fPing = PingFont;

            var m = new Metrics();
            m.ArrowW = 10.5f * s;
            // 用固定样本度量：数值每秒都在变，如果拿真实文本量宽度，
            // 窗口就会每秒 Resize 一次（owned by 任务栏的窗口一 Resize，
            // shell 就得重排整条任务栏）——这就是之前「任务栏跟着卡」的元凶。
            m.NumCol = Txt.Measure(g, "888.8", fNum).Width;
            m.UnitW = Math.Max(Txt.Measure(g, "KB/s", fUnit).Width, Txt.Measure(g, "MB/s", fUnit).Width);
            m.EntryW = m.ArrowW + 3f * s + m.NumCol + 3f * s + m.UnitW;
            m.Single = _conf.SingleLine;
            m.PingW = _conf.ShowPing ? 8f * s + Txt.Measure(g, "888ms", fPing).Width : 0;
            m.SparkW = _conf.ShowSpark ? 26f * s + 8f * s : 0;

            if (m.Single)
            {
                m.W = m.EntryW * 2 + 10f * s;
                m.H = 21f * s;
            }
            else
            {
                m.W = m.EntryW;
                m.H = 30f * s;
            }

            m.W += m.PingW + m.SparkW;
            _numW = m.NumCol;
            return m;
        }

        /// <summary>组件自己的缩放（设置里的「组件大小」），跟界面字号无关。</summary>
        float _fit = 1f;   // 车道放不下时的等比缩减系数

        float Ws { get { return _conf.WidgetScale / 100f * _fit; } }

        Font NumFont { get { return Skin.NumRaw(15.5f * Ws); } }
        Font UnitFont { get { return Skin.UiRaw(9.5f * Ws, FontStyle.Bold); } }
        Font PingFont { get { return Skin.UiRaw(9.5f * Ws); } }

        // ---------------- 绘制 ----------------

        protected override void OnLayerPaint(Graphics g)
        {
            var t = Skin.T;
            float s = Ws;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            if (_conf.Pill)
            {
                var pill = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                int alpha = (int)(_conf.Alpha / 100f * 255);
                Round.Fill(g, pill, pill.Height / 2f, Skin.Alpha(t.Card, alpha));
                Round.Stroke(g, pill, pill.Height / 2f, Skin.Alpha(t.Line, (int)(alpha * 0.5f)), 1f);
            }

            var m = MeasureAll(g);
            float padX = 8 * s;
            float left = padX;

            // 右 → 左：曲线、延迟
            float right = Width - padX;
            if (m.SparkW > 0)
            {
                var sr = new RectangleF(right - 30 * s, Height / 2f - 8 * s, 30 * s, 16 * s);
                DrawSpark(g, sr, t);
                right -= 30 * s + 8 * s;
            }
            if (m.PingW > 0)
            {
                float pw = m.PingW - 8 * s;
                var pr = new RectangleF(right - pw, 0, pw, Height);
                bool bad = _last.Rtt < 0 || _last.Rtt > 200;
                Txt.Draw(g, _ping, PingFont, Skin.Alpha(bad ? t.Warn : t.Sub, 240), pr, Txt.Right);
                right -= pw;
            }

            if (m.PingW > 0 && m.SparkW > 0 && _peak >= 1.5f)
            {
                using (var pen = new Pen(Skin.Alpha(t.Line, 95), 1f))
                {
                    float dx = right - 4 * s;
                    g.DrawLine(pen, dx, Height * 0.26f, dx, Height * 0.74f);
                }
            }

            if (m.Single)
            {
                float y = (Height - 20 * s) / 2f;
                float x = left;
                x = DrawEntry(g, x, y, Glyph.Down, t.Down, _line1, _unit1, s);
                DrawEntry(g, x + 10 * s, y, Glyph.Up, t.Up, _line2, _unit2, s);
            }
            else
            {
                float lineH = Height / 2f;
                DrawEntry(g, left, (lineH - 15 * s) / 2f + 0.5f * s, Glyph.Down, t.Down, _line1, _unit1, s);
                DrawEntry(g, left, lineH + (lineH - 15 * s) / 2f - 0.5f * s, Glyph.Up, t.Up, _line2, _unit2, s);
            }
        }

        /// <summary>画一行：箭头 + 右对齐数字 + 紧跟单位，返回右边缘。</summary>
        float DrawEntry(Graphics g, float x, float y, Glyph kind, Color color, string num, string unit, float s)
        {
            var arrow = new RectangleF(x, y + 1.5f * s, 9.5f * s, 12 * s);
            Ico.Draw(g, kind, arrow, color, 1.9f);

            float nx = x + 10.5f * s + 3 * s;
            var numSize = Txt.Measure(g, num, NumFont);
            Txt.Draw(g, num, NumFont, color, new RectangleF(nx, y, Math.Max(numSize.Width + 2, 4), 16 * s), Txt.Left);

            float ux = nx + numSize.Width + 3f * s;
            Txt.Draw(g, unit, UnitFont, Skin.Alpha(color, 215), new RectangleF(ux, y + 2.5f * s, 40 * s, 13 * s), Txt.Left);

            return ux + 40 * s;
        }

        void DrawSpark(Graphics g, RectangleF r, Theme t)
        {
            var old = g.Save();
            bool idle = _peak < 4096f;   // 低于 4 KB/s 就画一条安静的淡线，免得几百字节的抖动看起来像杂讯
            using (var clip = Round.Make(r, 4))
            {
                g.SetClip(clip);
                var pts = new PointF[_spark.Length];
                float peak = Math.Max(1f, _peak);
                for (int i = 0; i < _spark.Length; i++)
                {
                    int idx = (_sparkPos + i) % _spark.Length;
                    float v = idle ? 0.12f : (float)(Math.Log10(1 + _spark[idx]) / Math.Log10(1 + peak));
                    pts[i] = new PointF(r.X + i * (r.Width / (_spark.Length - 1)), r.Bottom - v * r.Height);
                }

                using (var pen = new Pen(idle ? Skin.Alpha(t.Sub, 120) : Skin.Alpha(t.Down, 215), idle ? 1.1f : 1.4f)
                { LineJoin = LineJoin.Round })
                    g.DrawLines(pen, pts);

                if (!idle)
                {
                    using (var b = new LinearGradientBrush(r, Skin.Alpha(t.Down, 64), Skin.Alpha(t.Down, 0), 90f))
                    {
                        var poly = new GraphicsPath();
                        var fp = new PointF[_spark.Length + 2];
                        for (int i = 0; i < pts.Length; i++) fp[i] = pts[i];
                        fp[_spark.Length] = new PointF(r.Right, r.Bottom);
                        fp[_spark.Length + 1] = new PointF(r.X, r.Bottom);
                        poly.AddPolygon(fp);
                        g.FillPath(b, poly);
                        poly.Dispose();
                    }
                }
            }
            g.Restore(old);
        }

        // ---------------- 交互 ----------------

        protected override bool OnLayerMouseDown(Point p, MouseButtons button)
        {
            if (button == MouseButtons.Right)
            {
                var h = RightClicked;
                if (h != null) h();
                return true;
            }

            var c = Clicked;
            if (c != null) c();
            return true;
        }


        void Watchdog()
        {
            if (!_conf.WidgetOn) { if (Visible) Hide(); return; }

            var tb = TaskbarInfo.Query();
            if (tb == null)
            {
                if (Visible) Hide();
                return;
            }

            if (!Visible && !Native.IsFullscreenAppRunning()) ShowWidget();

            bool moved = _taskbar == null || !tb.SameAs(_taskbar);
            _taskbar = tb;

            // owner 关系已经保证在上面了，这里不要再周期性 SetWindowPos：
            // 每次重排都会让 shell 再发一轮重排事件，开始菜单/任务视图打开时尤其明显。
            if (_ownedBy != tb.Handle || moved) ShowWidget();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _watch.Stop();
                _watch.Dispose();
                    if (CanvasGraphics != null) CanvasGraphics.Dispose();
                if (Canvas != null) Canvas.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
