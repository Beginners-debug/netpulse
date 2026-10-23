using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using NetPulse.App;
using NetPulse.Core;
using NetPulse.Interop;

namespace NetPulse.Ui
{
    /// <summary>
    /// 托盘弹出面板：整窗自绘的圆角卡片，替代原来那个又丑又看不清的 ContextMenuStrip。
    /// 实时速率、曲线、延迟、今日流量、网卡、快捷开关全在一张卡上。
    /// </summary>
    internal sealed class PanelForm : LayeredForm
    {
        const int CardW = 348;

        readonly Conf _conf;
        readonly NetMonitor _mon;
        readonly TrafficStore _store;
        readonly Action _onSettings;
        readonly Action _onExit;
        readonly Action _onConfChanged;

        readonly List<Hit> _hits = new List<Hit>();
        int _hover = -1;

        readonly float[] _down = new float[72];
        readonly float[] _up = new float[72];
        int _pos;
        bool _seeded;
        float _peak = 1;

        NetSample _sample;
        // 刷新节奏统一由 PulseApp 的 UI tick 驱动，面板自己不再开表
        int _contentH;

        struct Hit
        {
            public Rectangle Rect;
            public Action Action;
            public bool Button;
        }

        public PanelForm(Conf conf, NetMonitor mon, TrafficStore store, Action onSettings, Action onExit, Action onConfChanged)
        {
            _conf = conf;
            _mon = mon;
            _store = store;
            _onSettings = onSettings;
            _onExit = onExit;
            _onConfChanged = onConfChanged;

            Pad = 18;
            ClientSize = new Size(CardW + Pad * 2, 520 + Pad * 2);
            EnsureCanvas(Width, Height);
        }

        public void Rearm()
        {
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            _hover = -1;
        }

        string _sig = "";

        int _autoHideAt;      // 自动收起的时间点（0 = 不自动收）

        /// <summary>显示在托盘图标上方 / 鼠标附近。autoHide = 开机自动弹出时，几秒后自己收起来。</summary>
        public void ShowNear(Point anchor, bool autoHide = false)
        {
            _autoHideAt = autoHide ? Environment.TickCount + 7000 : 0;
            _sample = _mon.Sample;
            SeedHistory();
            Build();

            var wa = Screen.FromPoint(anchor).WorkingArea;
            int x = anchor.X - Width + 24;
            int y = anchor.Y - Height + 20;

            if (y < wa.Top + 8) y = anchor.Y + 30;
            if (x + Width > wa.Right) x = wa.Right - Width - 6;
            if (x < wa.Left + 6) x = wa.Left + 6;
            if (y + Height > wa.Bottom) y = wa.Bottom - Height - 6;

            Location = new Point(x, y);
            if (!Visible) Show();
            FadeIn();
            Repaint();
        }

        /// <summary>
        /// 每秒由 app 调一次：拉一份新样本、把曲线往前推一格、重画。
        /// 以前 OnTick 里只调 Repaint()，画的还是 ShowNear() 时存下的那份快照，
        /// 于是面板打开后数字就定住了，和任务栏组件对不上。
        /// </summary>
        public void Refresh()
        {
            Refresh(_mon.Sample);
        }

        /// <summary>用 app 这一 tick 的同一份样本刷新，保证和任务栏组件逐帧一致。</summary>
        public void Refresh(NetSample s)
        {
            _sample = s;
            PushPoint();
            Repaint();
        }

        /// <summary>把当前速率压进曲线环形缓冲。</summary>
        void PushPoint()
        {
            _down[_pos] = (float)_sample.Down;
            _up[_pos] = (float)_sample.Up;
            _pos = (_pos + 1) % _down.Length;

            float cur = (float)Math.Max(_sample.Down, _sample.Up);
            if (cur > _peak) _peak = cur;                       // 峰值爬到新高度
            else _peak = Math.Max(1f, _peak * 0.997f);           // 慢慢回落，不留一根永久高峰
        }

        void SeedHistory()
        {
            // 第一次打开时用当前值填满，曲线不会是空白
            if (_seeded) return;
            _seeded = true;
            for (int i = 0; i < _down.Length; i++)
            {
                _down[i] = (float)_sample.Down;
                _up[i] = (float)_sample.Up;
            }
            _peak = Math.Max(1f, (float)Math.Max(_sample.Down, _sample.Up));
        }

        // ---------------- 布局 ----------------

        int _hdrTop, _tilesTop, _chartTop, _statsTop, _todayTop, _adapterTop, _toggleTop, _footerTop;
        RectangleF _chartRect;

        const int GapX = 20;

        void Build()
        {
            _hits.Clear();
            float s = Skin.Scale;
            int innerW = CardW - GapX * 2;

            int y = Pad + 14;
            _hdrTop = y;
            y += (int)(44 * s);

            _tilesTop = y;
            y += (int)(88 * s) + (int)(10 * s);

            _chartTop = y;
            _chartRect = new RectangleF(Pad + GapX + 10 * s, y + (int)(28 * s),
                innerW - 20 * s, 72 * s - (int)(38 * s));
            y += (int)(72 * s) + (int)(10 * s);

            _statsTop = y;
            y += (int)(50 * s) + (int)(10 * s);

            _todayTop = y;
            y += (int)(54 * s) + (int)(10 * s);

            _adapterTop = y;
            y += (int)(28 * s) + (int)(8 * s);

            _toggleTop = y;
            y += (int)(38 * s) + (int)(12 * s);

            _footerTop = y;
            y += (int)(42 * s);

            _contentH = y + 8;

            int h = _contentH + Pad;
            if (h != Height) { ClientSize = new Size(Width, h); EnsureCanvas(Width, h); }
        }

        // ---------------- 绘制 ----------------

        protected override void OnLayerPaint(Graphics g)
        {
            var t = Skin.T;
            float s = Skin.Scale;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var card = new RectangleF(Pad, Pad, CardW, Height - Pad * 2);
            var rr = (float)(24 * s);

            Round.Shadow(g, card, rr, Color.Black, (int)(18 * s), 0.30f);
            Round.Fill(g, card, rr, t.Card);
            Round.Stroke(g, card, rr, Skin.Alpha(t.Line, t.Dark ? 150 : 235), 1f);

            var old = g.Save();
            using (var clip = Round.Make(card, rr)) g.SetClip(clip);

            // 顶部一层很淡的主题色雾，让卡片不显得平
            using (var b = new LinearGradientBrush(new RectangleF(card.X, card.Y, card.Width, 150 * s),
                Skin.Alpha(t.Accent, t.Dark ? 34 : 26), Skin.Alpha(t.Accent, 0), 90f))
                g.FillRectangle(b, card.X, card.Y, card.Width, 150 * s);

            DrawHeader(g, t, s);
            DrawTiles(g, t, s);
            DrawChart(g, t, s);
            DrawStats(g, t, s);
            DrawToday(g, t, s);
            DrawAdapter(g, t, s);
            DrawToggles(g, t, s);
            DrawFooter(g, t, s);

            g.Restore(old);
        }

        void DrawHeader(Graphics g, Theme t, float s)
        {
            float x = Pad + GapX;
            float y = _hdrTop;

            // 品牌小方块
            var mark = new RectangleF(x, y + 4 * s, 28 * s, 28 * s);
            Round.Gradient(g, mark, 10 * s, Skin.Blend(t.Accent, Color.White, 0.35), t.Accent, 55);
            Ico.Draw(g, Glyph.Sparkle, new RectangleF(mark.X + 6.5f * s, mark.Y + 6.5f * s, 15 * s, 15 * s), Color.White, 1.7f);

            Txt.Draw(g, "NetPulse", Skin.Cute(17.5f), t.Text,
                new RectangleF(x + 35 * s, y, 108 * s, 22 * s), Txt.Left);
            Txt.Draw(g, "网速速报", Skin.Ui(9.5f), Skin.Alpha(t.Sub, 200),
                new RectangleF(x + 35 * s, y + 20 * s, 108 * s, 16 * s), Txt.Left);

            var verRect = new RectangleF(x + 35 * s + Txt.Measure(g, "NetPulse", Skin.Cute(17.5f)).Width + 6 * s, y + 3 * s, 36 * s, 17 * s);
            Round.Fill(g, verRect, 8.5f * s, Skin.Alpha(t.Accent2, t.Dark ? 60 : 42));
            Txt.Draw(g, "v3.0", Skin.Ui(9f, FontStyle.Bold), Skin.Blend(t.Text, t.Accent, 0.25), verRect, Txt.Center);

            float bw = 28 * s;
            var gear = new RectangleF(Pad + CardW - GapX - bw * 2 - 6 * s, y + 4 * s, bw, bw);
            var close = new RectangleF(Pad + CardW - GapX - bw, y + 4 * s, bw, bw);

            DrawRoundIconButton(g, gear, Glyph.Gear, _hover == 0);
            DrawRoundIconButton(g, close, Glyph.Close, _hover == 1);

            _hits.Add(new Hit { Rect = ToScreenless(gear), Action = _onSettings, Button = true });
            _hits.Add(new Hit { Rect = ToScreenless(close), Action = HidePanel, Button = true });
        }

        Rectangle ToScreenless(RectangleF r)
        {
            return new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        }

        void DrawRoundIconButton(Graphics g, RectangleF r, Glyph icon, bool hover)
        {
            var t = Skin.T;
            Round.Fill(g, r, r.Width / 2f, hover ? Skin.Alpha(t.Text, t.Dark ? 34 : 14) : Skin.Alpha(t.Text, t.Dark ? 18 : 7));
            Ico.Draw(g, icon, new RectangleF(r.X + r.Width * 0.30f, r.Y + r.Height * 0.30f, r.Width * 0.40f, r.Height * 0.40f),
                Skin.Alpha(t.Text, 190), 1.9f);
        }

        void DrawTiles(Graphics g, Theme t, float s)
        {
            float x = Pad + GapX;
            float y = _tilesTop;
            float w = (CardW - GapX * 2 - 9 * s) / 2f;
            float h = 88 * s;

            string v1, u1, v2, u2;
            Fmt.Speed(_sample.Down, _conf.UnitMode, out v1, out u1);
            Fmt.Speed(_sample.Up, _conf.UnitMode, out v2, out u2);

            DrawTile(g, new RectangleF(x, y, w, h), Glyph.Down, "下载", v1, u1, t.Down, t, s);
            DrawTile(g, new RectangleF(x + w + 9 * s, y, w, h), Glyph.Up, "上传", v2, u2, t.Up, t, s);
        }

        void DrawTile(Graphics g, RectangleF r, Glyph icon, string label, string value, string unit, Color color, Theme t, float s)
        {
            Round.Fill(g, r, 16 * s, Skin.Alpha(color, t.Dark ? 30 : 16));
            Round.Stroke(g, r, 16 * s, Skin.Alpha(color, t.Dark ? 54 : 30), 1f);

            var bubble = new RectangleF(r.X + 11 * s, r.Y + 11 * s, 22 * s, 22 * s);
            Round.Fill(g, bubble, 7 * s, Skin.Alpha(color, t.Dark ? 66 : 40));
            Ico.Draw(g, icon, new RectangleF(bubble.X + 5 * s, bubble.Y + 5 * s, 12 * s, 12 * s), color, 2.1f);

            Txt.Draw(g, label, Skin.Ui(10.5f, FontStyle.Bold), Skin.Alpha(t.Sub, 235),
                new RectangleF(r.X + 39 * s, r.Y + 10 * s, r.Width - 46 * s, 16 * s), Txt.Left);

            // 数字自适应：太长就从 27 缩到 15
            float avail = r.Width - 20 * s;
            Font fNum = null;
            SizeF nv = SizeF.Empty, uv = SizeF.Empty;
            var fUnit = Skin.Ui(10f, FontStyle.Bold);
            for (float px = 27f; px >= 15f; px -= 1f)
            {
                fNum = Skin.Num(px);
                nv = Txt.Measure(g, value, fNum);
                uv = Txt.Measure(g, unit, fUnit);
                if (nv.Width + 4 * s + uv.Width <= avail) break;
            }
            if (nv.Width + 4 * s + uv.Width > avail && fNum != null)
            {
                value = Txt.Ellipsis(g, value, fNum, (int)(avail - uv.Width - 6));
                nv = Txt.Measure(g, value, fNum);
            }

            float baseline = r.Y + 46 * s;
            Txt.Draw(g, value, fNum, color, new RectangleF(r.X + 11 * s, baseline, nv.Width + 4, 30 * s), Txt.Left);
            Txt.Draw(g, unit, fUnit, Skin.Alpha(color, 215),
                new RectangleF(r.X + 11 * s + nv.Width + 3 * s, baseline + nv.Height - uv.Height - 2.5f * s, uv.Width + 4, uv.Height + 2), Txt.Left);
        }

        void DrawChart(Graphics g, Theme t, float s)
        {
            float x = Pad + GapX;
            float y = _chartTop;
            float w = CardW - GapX * 2;

            var box = new RectangleF(x, y, w, 72 * s);
            Round.Fill(g, box, 16 * s, t.Dark ? t.Card2 : Skin.Blend(t.Bg, t.Card, 0.55f));

            Txt.Draw(g, "最近 72 秒", Skin.Ui(9.5f), Skin.Alpha(t.Sub, 200),
                new RectangleF(box.X + 11 * s, box.Y + 8 * s, 90 * s, 14 * s), Txt.Left);
            Txt.Draw(g, _peak <= 1.5f ? "峰值 --" : "峰值 " + Fmt.Tiny(_peak) + "/s", Skin.Ui(9.5f), Skin.Alpha(t.Sub, 200),
                new RectangleF(box.Right - 111 * s, box.Y + 8 * s, 100 * s, 14 * s), Txt.Right);

            var r = _chartRect;
            bool idle = true;
            for (int i = 0; i < _down.Length; i++)
                if (_down[i] > 1f || _up[i] > 1f) { idle = false; break; }

            if (idle)
            {
                using (var pen = new Pen(Skin.Alpha(t.Line, t.Dark ? 80 : 140), 1.4f) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
                Txt.Draw(g, "这一会儿没有流量", Skin.Ui(9.5f), Skin.Alpha(t.Sub, 165),
                    new RectangleF(r.X, r.Y, r.Width, r.Height - 2), Txt.Center);
                return;
            }

            using (var pen = new Pen(Skin.Alpha(t.Line, t.Dark ? 34 : 60), 1f))
                for (int i = 1; i < 3; i++)
                {
                    float yy = r.Y + r.Height * i / 3f;
                    g.DrawLine(pen, r.X, yy, r.Right, yy);
                }

            DrawLine(g, r, _down, t.Down, s, true);
            DrawLine(g, r, _up, t.Up, s, false);
        }

        void DrawLine(Graphics g, RectangleF r, float[] data, Color color, float s, bool fill)
        {
            var pts = new PointF[data.Length];
            float peak = Math.Max(1f, _peak);
            for (int i = 0; i < data.Length; i++)
            {
                int idx = (_pos + i) % data.Length;
                float v = (float)(Math.Log10(1 + data[idx]) / Math.Log10(1 + peak));
                // 值再小也留 3px 高度，避免整条线糊在底边上
                float hh = Math.Max(3f, v * r.Height);
                pts[i] = new PointF(r.X + i * (r.Width / (data.Length - 1)), r.Bottom - hh);
            }

            using (var path = new GraphicsPath())
            {
                path.AddLines(pts);
                using (var pen = new Pen(Skin.Alpha(color, 240), 2.1f)
                       { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawPath(pen, path);

                if (!fill) return;

                // 只给下行画一块很淡的面积，两条都填会糊成一片灰带
                var area = (GraphicsPath)path.Clone();
                area.AddLine(new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom));
                area.CloseFigure();
                var top = Skin.T.Dark ? 38 : 26;
                using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                           Skin.Alpha(color, top), Skin.Alpha(color, 0), 90f))
                    g.FillPath(b, area);
                area.Dispose();
            }
        }

        void DrawStats(Graphics g, Theme t, float s)
        {
            float x = Pad + GapX;
            float w = (CardW - GapX * 2 - 16 * s) / 3f;
            float y = _statsTop;
            float h = 50 * s;

            bool timeout = _sample.Rtt < 0;
            string rttV = timeout ? "超时" : _sample.Rtt.ToString();
            Color rttC = timeout ? t.Warn : GradeColor(_sample.Rtt, 60, 150);

            DrawStatPill(g, new RectangleF(x, y, w, h), "延迟", rttV, timeout ? "" : "ms", rttC, t, s);
            DrawStatPill(g, new RectangleF(x + w + 8 * s, y, w, h), "抖动", _sample.Jitter.ToString("0.0"), "ms",
                GradeColor((int)_sample.Jitter, 12, 40), t, s);
            DrawStatPill(g, new RectangleF(x + (w + 8 * s) * 2, y, w, h), "丢包", Fmt.Loss(_sample.Loss).Replace("%", ""), "%",
                GradeColor((int)(_sample.Loss * 4), 4, 20), t, s);
        }

        void DrawStatPill(Graphics g, RectangleF r, string label, string value, string unit, Color color, Theme t, float s)
        {
            Round.Fill(g, r, 14 * s, t.Dark ? t.Card2 : Skin.Blend(t.Bg, t.Card, 0.55f));

            Txt.Draw(g, label, Skin.Ui(9.5f), Skin.Alpha(t.Sub, 215),
                new RectangleF(r.X, r.Y + 6 * s, r.Width, 13 * s), Txt.Center);

            var fv = Skin.Num(16f);
            var fu = Skin.Ui(8.5f, FontStyle.Bold);
            SizeF vs = Txt.Measure(g, value, fv);
            SizeF us = Txt.Measure(g, unit, fu);
            float total = vs.Width + 2 * s + us.Width;
            float sx = r.X + (r.Width - total) / 2f;
            float by = r.Y + 20 * s;

            Txt.Draw(g, value, fv, color, new RectangleF(sx, by, vs.Width + 3, 22 * s), Txt.Left);
            Txt.Draw(g, unit, fu, Skin.Alpha(color, 200),
                new RectangleF(sx + vs.Width + 2 * s, by + vs.Height - us.Height - 2 * s, us.Width + 3, us.Height + 2), Txt.Left);
        }

        Color GradeColor(int value, int good, int bad)
        {
            var t = Skin.T;
            if (value <= good) return t.Up;
            if (value <= bad) return t.Accent;
            return t.Warn;
        }

        void DrawToday(Graphics g, Theme t, float s)
        {
            float x = Pad + GapX;
            float y = _todayTop;
            float w = (CardW - GapX * 2 - 16 * s) / 3f;
            float h = 54 * s;

            DrawTodayCell(g, new RectangleF(x, y, w, h), "今日下行", _store.DayIn, t.Down, t, s);
            DrawTodayCell(g, new RectangleF(x + w + 8 * s, y, w, h), "今日上行", _store.DayOut, t.Up, t, s);
            DrawTodayCell(g, new RectangleF(x + (w + 8 * s) * 2, y, w, h), "本次会话", _store.SessionIn + _store.SessionOut, t.Accent, t, s);
        }

        void DrawTodayCell(Graphics g, RectangleF r, string label, long bytes, Color color, Theme t, float s)
        {
            Round.Fill(g, r, 14 * s, t.Dark ? t.Card2 : Skin.Blend(t.Bg, t.Card, 0.55f));

            var dot = new RectangleF(r.X + 9 * s, r.Y + 11 * s, 5.5f * s, 5.5f * s);
            Round.Fill(g, dot, 3 * s, color);
            Txt.Draw(g, label, Skin.Ui(9.5f), Skin.Alpha(t.Sub, 215),
                new RectangleF(r.X + 18 * s, r.Y + 6 * s, r.Width - 24 * s, 13 * s), Txt.Left);

            string v, u;
            Fmt.Bytes(bytes, out v, out u);
            var fv = Skin.Num(15f);
            var fu = Skin.Ui(8.5f, FontStyle.Bold);
            SizeF vs = Txt.Measure(g, v, fv);
            Txt.Draw(g, v, fv, t.Text, new RectangleF(r.X + 9 * s, r.Y + 22 * s, vs.Width + 4, 22 * s), Txt.Left);
            Txt.Draw(g, u, fu, Skin.Alpha(t.Sub, 230),
                new RectangleF(r.X + 9 * s + vs.Width + 2 * s, r.Y + 22 * s + vs.Height - 11 * s, 22 * s, 14 * s), Txt.Left);
        }

        void DrawAdapter(Graphics g, Theme t, float s)
        {
            float x = Pad + GapX;
            float y = _adapterTop;

            Ico.Draw(g, Glyph.Wifi, new RectangleF(x, y + 6 * s, 14 * s, 14 * s), Skin.Alpha(t.Accent, 225), 1.7f);

            string name = _mon.AdapterName();
            if (string.IsNullOrEmpty(name)) name = "全部网卡";
            var fName = Skin.Ui(10.5f);
            Txt.Draw(g, Txt.Ellipsis(g, name, fName, CardW - GapX * 2 - 90), fName, Skin.Alpha(t.Sub, 240),
                new RectangleF(x + 20 * s, y + 3 * s, CardW - GapX * 2 - 90, 20 * s), Txt.Left);

            var dot = new RectangleF(Pad + CardW - GapX - 58 * s, y + 10 * s, 6 * s, 6 * s);
            Round.Fill(g, dot, 3 * s, _sample.LinkUp ? t.Up : t.Warn);
            Txt.Draw(g, _sample.LinkUp ? "已连接" : "未连接", Skin.Ui(9.5f),
                Skin.Alpha(_sample.LinkUp ? t.Up : t.Warn, 235),
                new RectangleF(Pad + CardW - GapX - 48 * s, y + 4 * s, 48 * s, 18 * s), Txt.Right);
        }

        void DrawToggles(Graphics g, Theme t, float s)
        {
            float x = Pad + GapX;
            float y = _toggleTop;
            float w = (CardW - GapX * 2 - 16 * s) / 3f;

            DrawToggle(g, new RectangleF(x, y, w, 38 * s), "任务栏", _conf.WidgetOn, 0, t, s);
            DrawToggle(g, new RectangleF(x + w + 8 * s, y, w, 38 * s), "单行", _conf.SingleLine, 1, t, s);
            DrawToggle(g, new RectangleF(x + (w + 8 * s) * 2, y, w, 38 * s), "自启", _conf.AutoStart, 2, t, s);
        }

        void DrawToggle(Graphics g, RectangleF r, string label, bool on, int index, Theme t, float s)
        {
            bool hover = _hover == 10 + index;
            Round.Fill(g, r, 13 * s,
                on ? Skin.Alpha(t.Accent, t.Dark ? 76 : 46) : (t.Dark ? t.Card2 : Skin.Blend(t.Bg, t.Card, 0.5f)));
            if (hover) Round.Stroke(g, r, 13 * s, Skin.Alpha(t.Accent, 95), 1.2f);

            float dotR = 7 * s;
            var dotRect = new RectangleF(r.X + 10 * s, r.Y + r.Height / 2f - dotR / 2f, dotR, dotR);
            if (on) Round.Fill(g, dotRect, dotR / 2f, t.Accent);
            else Round.Stroke(g, dotRect, dotR / 2f, Skin.Alpha(t.Sub, 165), 1.3f);

            Txt.Draw(g, label, Skin.Ui(11f, on ? FontStyle.Bold : FontStyle.Regular),
                Skin.Alpha(on ? t.Text : t.Sub, 240), new RectangleF(r.X + 22 * s, r.Y, r.Width - 24 * s, r.Height), Txt.Left);

            _hits.Add(new Hit { Rect = ToScreenless(r), Action = () => ToggleOption(index) });
        }

        void DrawFooter(Graphics g, Theme t, float s)
        {
            float x = Pad + GapX;
            float y = _footerTop;
            float w = CardW - GapX * 2;

            var primary = new RectangleF(x, y, w - 70 * s, 42 * s);
            bool hover = _hover == 20;
            Round.Gradient(g, primary, 14 * s, Skin.Blend(t.Accent, Color.White, 0.22), t.Accent, 60);
            Ico.Draw(g, Glyph.Gear, new RectangleF(primary.X + 16 * s, primary.Y + 12 * s, 18 * s, 18 * s), Color.White, 1.8f);
            Txt.Draw(g, "打开设置", Skin.Ui(13.5f, FontStyle.Bold), Color.White,
                new RectangleF(primary.X + 40 * s, primary.Y, primary.Width - 50 * s, primary.Height), Txt.Left);
            if (hover) Round.Stroke(g, primary, 14 * s, Skin.Alpha(Color.White, 130), 1.4f);

            var quit = new RectangleF(x + w - 62 * s, y, 62 * s, 42 * s);
            Round.Fill(g, quit, 14 * s, t.Dark ? t.Card2 : Skin.Blend(t.Bg, t.Card, 0.5f));
            Ico.Draw(g, Glyph.Power, new RectangleF(quit.X + 11 * s, quit.Y + 13 * s, 16 * s, 16 * s), Skin.Alpha(t.Text, 205), 1.7f);
            Txt.Draw(g, "退出", Skin.Ui(10.5f), Skin.Alpha(t.Text, 220),
                new RectangleF(quit.X + 30 * s, quit.Y, 28 * s, quit.Height), Txt.Left);
            if (_hover == 21) Round.Stroke(g, quit, 14 * s, Skin.Alpha(t.Warn, 130), 1.4f);

            _hits.Add(new Hit { Rect = ToScreenless(primary), Action = _onSettings, Button = true });
            _hits.Add(new Hit { Rect = ToScreenless(quit), Action = _onExit, Button = true });
        }

        // ---------------- 交互 ----------------

        /// <summary>预热：把离屏画布和第一帧先画出来，第一次真正打开时只剩一次 Push。</summary>
        public void Prewarm()
        {
            try { Repaint(); } catch (Exception ex) { App.Paths.Log("面板预热失败: " + ex.Message); }
        }

        public void HidePanel()
        {
            FadeOut();
        }

        void ToggleOption(int index)
        {
            if (index == 0) _conf.WidgetOn = !_conf.WidgetOn;
            else if (index == 1) _conf.SingleLine = !_conf.SingleLine;
            else _conf.AutoStart = !_conf.AutoStart;

            if (index == 2) Startup.Set(_conf.AutoStart);
            _conf.Notify();
            Repaint();
        }

        protected override void OnLayerMouseMove(Point p)
        {
            _autoHideAt = 0;
            int hit = -1;
            for (int i = 0; i < _hits.Count; i++)
                if (_hits[i].Rect.Contains(p)) { hit = i; break; }

            // 开关的编号沿用绘制时的约定
            if (hit >= 0)
            {
                var a = _hits[hit].Action;
                hit = IndexOfAction(a);
            }

            if (hit != _hover) { _hover = hit; Repaint(); }
        }

        int IndexOfAction(Action a)
        {
            if (a == null) return -1;
            for (int i = 0; i < _hits.Count; i++) if (_hits[i].Action.Method == a.Method) return i;
            return -1;
        }

        protected override bool OnLayerMouseDown(Point p, MouseButtons button)
        {
            if (button == MouseButtons.Right) { HidePanel(); return true; }

            for (int i = 0; i < _hits.Count; i++)
            {
                if (!_hits[i].Rect.Contains(p)) continue;
                var action = _hits[i].Action;
                if (action == null) continue;

                if (action == _onSettings) { HidePanel(); }
                try { action(); } catch (Exception ex) { Paths.Log("面板点击失败: " + ex.Message); }
                return true;
            }

            return true;
        }
    }
}
