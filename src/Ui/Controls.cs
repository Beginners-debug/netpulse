using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NetPulse.App;

namespace NetPulse.Ui
{
    /// <summary>控件基类：双缓冲、回退色、主题变更钩子。</summary>
    internal abstract class CandyBase : Control
    {
        protected CandyBase()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Skin.T.Bg;
            ForeColor = Skin.T.Text;
            Font = Skin.Ui(13f);
        }

        protected Theme T { get { return Skin.T; } }

        /// <summary>容器换了底色，控件跟着换。</summary>
        public void Surface(Color color)
        {
            BackColor = color;
            Invalidate();
        }

        protected static float Ease(double t)
        {
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            return (float)(1 - Math.Pow(1 - t, 3));
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            Ticker.Kick();
            base.OnMouseEnter(e);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Ticker.Kick();
            base.OnMouseLeave(e);
            Cursor = Cursors.Default;
        }
    }

    /// <summary>动画帧驱动：需要 60fps 的控件用它，不用每个控件各开一个 Timer。</summary>
    internal static class Ticker
    {
        // 动画心跳。有控件在听就 60Hz，没人听就彻底停表（不占 CPU）。
        // 之前那套“交互后 1 秒降频到 8Hz”的策略有个致命 bug：
        // Kick() 里的判断写成了「最近 8 秒内活跃过才续命」，
        // 于是程序性触发的动画（点开关、切分段、翻页）根本拿不到 60Hz，
        // 用户看到的就是“动画非常缓慢、一卡一卡”。
        const int FastMs = 16;

        static readonly Timer Timer = new Timer { Interval = FastMs };
        static readonly List<Action> Listeners = new List<Action>();

        static Ticker()
        {
            Timer.Tick += (s, e) =>
            {
                System.Threading.Interlocked.Increment(ref Perf.TickerTicks);
                Action[] copy;
                lock (Listeners) copy = Listeners.ToArray();
                foreach (var a in copy) { try { a(); } catch { } }
            };
        }

        /// <summary>交互或属性变化时调用：保证接下来一小段时间是满帧。</summary>
        public static void Kick()
        {
            if (!Timer.Enabled) Timer.Start();
        }

        public static void Add(Action tick)
        {
            lock (Listeners) if (!Listeners.Contains(tick)) Listeners.Add(tick);
            if (!Timer.Enabled) Timer.Start();
        }

        public static void Remove(Action tick)
        {
            lock (Listeners) Listeners.Remove(tick);
            lock (Listeners)
            {
                if (Listeners.Count == 0) Timer.Stop();   // 没人听就停表
            }
        }
    }

    // ------------------------------------------------------------------ 开关

    internal sealed class CandySwitch : CandyBase
    {
        bool _on;
        float _anim;
        bool _hover;

        public event EventHandler Toggled;

        public CandySwitch()
        {
            Size = new Size(46, 26);
            Ticker.Add(Tick);
        }

        public bool Checked
        {
            get { return _on; }
            set
            {
                if (_on == value) return;
                _on = value;
                Invalidate();
            }
        }

        void Tick()
        {
            if (!IsHandleCreated) return;
            float target = _on ? 1f : 0f;
            if (Math.Abs(_anim - target) < 0.004f) { if (_anim != target) { _anim = target; Invalidate(); } return; }
            _anim += (target - _anim) * 0.28f;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); Invalidate(); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _on = !_on;
            Invalidate();
            var h = Toggled;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new RectangleF(1, 1, Width - 2, Height - 2);
            float radius = r.Height / 2f;

            Color off = T.Dark ? Skin.Blend(T.Card2, T.Line, 0.35) : Skin.Blend(T.Line, Color.White, 0.25);
            Color on = T.Accent;
            Color track = Skin.Blend(off, on, _anim);

            Round.Fill(g, r, radius, track);
            if (_hover) Round.Stroke(g, r, radius, Skin.Alpha(T.Accent, 70), 1.6f);

            float knob = r.Height - 6;
            float left = r.X + 3 + (r.Width - knob - 6) * Ease(_anim);
            var kr = new RectangleF(left, r.Y + 3, knob, knob);
            using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(b, kr.X, kr.Y + 1.2f, knob, knob);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kr);
        }
    }

    // ------------------------------------------------------------------ 滑块

    internal sealed class CandySlider : CandyBase
    {
        int _min, _max = 100, _value = 50;
        bool _drag;

        public event EventHandler ValueChanged;

        public int Min { get { return _min; } set { _min = value; if (_max <= _min) _max = _min + 1; Invalidate(); } }
        public int Max { get { return _max; } set { _max = Math.Max(value, _min + 1); Invalidate(); } }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(_min, Math.Min(_max, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
            }
        }

        public CandySlider()
        {
            Size = new Size(150, 28);
            Font = Skin.Ui(12.5f);
        }

        float Ratio { get { return (_value - _min) / (float)(_max - _min); } }

        int KnobX
        {
            get
            {
                int knob = 16;
                return (int)((Width - knob - 2) * Ratio) + 1;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int trackH = 6;
            float cy = Height / 2f;
            var track = new RectangleF(0, cy - trackH / 2f, Width, trackH);
            Round.Fill(g, track, trackH / 2f, T.Dark ? T.Card2 : T.Line);

            float fillW = KnobX + 8;
            if (fillW > 2)
                Round.Gradient(g, new RectangleF(0, cy - trackH / 2f, Math.Min(fillW, Width), trackH), trackH / 2f,
                    T.Accent2, T.Accent, 0);

            var knob = new RectangleF(KnobX, cy - 8, 16, 16);
            using (var b = new SolidBrush(Color.FromArgb(_drag ? 70 : 40, 0, 0, 0)))
                g.FillEllipse(b, knob.X - 1, knob.Y + 1.5f, 18, 18);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, knob);
            using (var p = new Pen(Skin.Blend(T.Accent, Color.White, 0.2), 2f)) g.DrawEllipse(p, knob);
        }

        void SetFromX(int x)
        {
            if (Width < 40) return;                       // 还没布局好，别用 0 宽度算比例
            if (_max <= _min) return;
            float ratio = (x - 8) / (float)Math.Max(1, Width - 16);
            ratio = Math.Max(0, Math.Min(1, ratio));
            int v = _min + (int)Math.Round(ratio * (_max - _min));
            if (v == _value) return;
            _value = v;
            Invalidate();
            var h = ValueChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Ticker.Kick();
            base.OnMouseDown(e);
            _drag = true;
            SetFromX(e.X);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = false;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Cursor = Cursors.Hand; }
    }

    // ------------------------------------------------------------------ 按钮

    internal enum BtnStyle { Primary, Soft, Ghost, Danger, Link }

    internal sealed class CandyButton : CandyBase
    {
        BtnStyle _style = BtnStyle.Soft;
        float _hover;
        bool _over, _down;
        public Glyph? IconKind;
        public int Radius = 12;
        public string SubText = "";

        public CandyButton()
        {
            Size = new Size(96, 36);
            Font = Skin.Ui(13.5f, FontStyle.Bold);
            Ticker.Add(Tick);
        }

        public BtnStyle Style
        {
            get { return _style; }
            set { _style = value; Invalidate(); }
        }

        void Tick()
        {
            if (!IsHandleCreated) return;
            float target = (_over || _down) ? 1f : 0f;
            if (Math.Abs(_hover - target) < 0.02f) return;
            _hover += (target - _hover) * 0.3f;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _over = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _over = false; _down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }

        Color BgColor()
        {
            switch (_style)
            {
                case BtnStyle.Primary: return Skin.Blend(T.Accent, Color.White, 0.10 * _hover);
                case BtnStyle.Soft: return Skin.Blend(T.Dark ? T.Card2 : T.Card2, T.Accent, 0.12 + 0.10 * _hover);
                case BtnStyle.Danger: return Skin.Blend(T.Warn, Color.White, 0.06 * _hover);
                case BtnStyle.Ghost: return Skin.Alpha(T.Text, (int)(_hover * (T.Dark ? 26 : 12)));
                default: return Color.Transparent;
            }
        }

        Color FgColor()
        {
            switch (_style)
            {
                case BtnStyle.Primary: return Color.White;
                case BtnStyle.Danger: return Color.White;
                case BtnStyle.Link: return Skin.Blend(T.Accent, T.Text, 0.15 * _hover);
                default: return T.Text;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float radius = Radius;

            if (_style == BtnStyle.Primary)
                Round.Gradient(g, r, radius, Skin.Blend(T.Accent, Color.White, 0.18), T.Accent, 60);
            else if (_style == BtnStyle.Danger)
                Round.Gradient(g, r, radius, Skin.Blend(T.Warn, Color.White, 0.20), T.Warn, 60);
            else
                Round.Fill(g, r, radius, BgColor());

            if (_style == BtnStyle.Soft || _style == BtnStyle.Ghost || _style == BtnStyle.Link)
                Round.Stroke(g, r, radius, Skin.Alpha(T.Line, T.Dark ? 90 : 200), 1f);

            if (_down) { using (var b = new SolidBrush(Color.FromArgb(18, 0, 0, 0))) { using (var p = Round.Make(r, radius)) g.FillPath(b, p); } }

            var fg = FgColor();
            bool hasIcon = IconKind.HasValue;
            SizeF textSize = Txt.Measure(g, Text, Font);

            float iconW = hasIcon ? Font.Height * 0.95f : 0;
            float gap = hasIcon ? 6 : 0;
            float totalW = textSize.Width + iconW + gap;
            float startX = (Width - totalW) / 2f;

            if (hasIcon)
            {
                var ir = new RectangleF(startX, (Height - iconW) / 2f, iconW, iconW);
                Ico.Draw(g, IconKind.Value, ir, fg, 1.8f);
            }

            Txt.Draw(g, Text, Font, fg, new RectangleF(startX + iconW + gap, 0, textSize.Width + 4, Height), Txt.Left);
        }
    }

    // ------------------------------------------------------------------ 分段选择

    internal sealed class CandySegmented : CandyBase
    {
        string[] _items = new string[0];
        int _selected;
        int _hoverIndex = -1;
        float _animIndex;

        public event EventHandler SelectedChanged;

        public CandySegmented()
        {
            Size = new Size(180, 32);
            Font = Skin.Ui(12.5f);
            Ticker.Add(Tick);
        }

        public string[] Items
        {
            get { return _items; }
            set { _items = value ?? new string[0]; Invalidate(); }
        }

        public int Selected
        {
            get { return _selected; }
            set
            {
                int v = Math.Max(0, Math.Min(_items.Length - 1, value));
                if (v == _selected) return;
                _selected = v;
                Invalidate();
                var h = SelectedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        void Tick()
        {
            if (!IsHandleCreated) return;
            if (Math.Abs(_animIndex - _selected) < 0.01f) return;
            _animIndex += (_selected - _animIndex) * 0.35f;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_items.Length == 0) return;
            int idx = (int)(e.X / (Width / (float)_items.Length));
            idx = Math.Max(0, Math.Min(_items.Length - 1, idx));
            if (idx != _hoverIndex) { _hoverIndex = idx; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hoverIndex = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_items.Length == 0) return;
            int idx = (int)(e.X / (Width / (float)_items.Length));
            Selected = Math.Max(0, Math.Min(_items.Length - 1, idx));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_items.Length == 0) return;

            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Round.Fill(g, r, Height / 2f, T.Dark ? T.Card2 : Skin.Blend(T.Line, Color.White, 0.45));

            float segW = Width / (float)_items.Length;
            float x = _animIndex * segW;
            if (Math.Abs(_animIndex - _selected) > 0.4f) x = _selected * segW;   // 首次直接就位

            var thumb = new RectangleF(x + 3, 3, segW - 6, Height - 6);
            Round.Fill(g, thumb, (Height - 6) / 2f, T.Dark ? Skin.Blend(T.Card, T.Accent, 0.22) : Color.White);
            Round.Stroke(g, thumb, (Height - 6) / 2f, Skin.Alpha(T.Accent, T.Dark ? 60 : 40), 1f);

            for (int i = 0; i < _items.Length; i++)
            {
                var cell = new RectangleF(i * segW, 0, segW, Height);
                bool sel = i == _selected;
                Color fg = sel ? T.Text : Skin.Alpha(T.Sub, _hoverIndex == i ? 255 : 200);
                Txt.Draw(g, _items[i], sel ? Skin.Ui(12.5f, FontStyle.Bold) : Font, fg, cell, Txt.Center);
            }
        }
    }

    // ------------------------------------------------------------------ 下拉选择

    internal sealed class SelectBox : CandyBase
    {
        string[] _items = new string[0];
        int _selected;
        bool _hover;
        DropPopup _popup;

        public event EventHandler SelectedChanged;

        public SelectBox()
        {
            Size = new Size(180, 32);
            Font = Skin.Ui(13f);
        }

        public string[] Items
        {
            get { return _items; }
            set { _items = value ?? new string[0]; Invalidate(); }
        }

        public int Selected
        {
            get { return _selected; }
            set
            {
                int v = Math.Max(0, Math.Min(Math.Max(0, _items.Length - 1), value));
                if (v == _selected) return;
                _selected = v;
                Invalidate();
                var h = SelectedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public string Text2 { get { return _selected >= 0 && _selected < _items.Length ? _items[_selected] : ""; } }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Open();
        }

        void Open()
        {
            if (_popup != null && !_popup.IsDisposed) _popup.Close();
            var screen = PointToScreen(new Point(0, Height + 4));
            _popup = new DropPopup(_items, _selected, Width);
            _popup.Picked += idx =>
            {
                Selected = idx;
            };
            _popup.ShowAt(screen, this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Round.Fill(g, r, 11, T.Dark ? Skin.Blend(T.Card2, T.Accent, _hover ? 0.14 : 0.02) : T.Card);
            Round.Stroke(g, r, 11, _hover ? Skin.Alpha(T.Accent, 150) : Skin.Alpha(T.Line, T.Dark ? 140 : 235), 1f);

            string text = Txt.Ellipsis(g, Text2, Font, Width - 34);
            Txt.Draw(g, text, Font, T.Text, new RectangleF(11, 0, Width - 34, Height), Txt.Left);
            Ico.Draw(g, Glyph.ChevronDown, new RectangleF(Width - 25, (Height - 12) / 2f, 12, 12), T.Sub, 1.7f);
        }
    }

    // ------------------------------------------------------------------ 卡片

    internal sealed class CandyCard : Panel
    {
        public string Title = "";
        public string Hint = "";
        public int Radius = 22;

        public CandyCard()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Skin.T.Card;
            DoubleBuffered = true;
        }

        void PaintSelf(Graphics g)
        {
            var t = Skin.T;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

            Round.Fill(g, r, Radius, t.Card);

            // 顶部一层极淡的品牌色雾，卡片不再是一块死白
            var washH = Math.Min(96f, Height - 8);
            using (var path = Round.Make(r, Radius))
            using (var b = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(1, Width), washH),
                Skin.Alpha(t.Accent, t.Dark ? 16 : 10), Skin.Alpha(t.Accent, 0), 90f))
                g.FillPath(b, path);

            // 细边 + 底缘加重，做出一点点厚度
            Round.Stroke(g, r, Radius, Skin.Alpha(t.Line, t.Dark ? 170 : 255), 1f);
            using (var pen = new Pen(Skin.Alpha(t.Sub, t.Dark ? 30 : 22), 1.4f))
            {
                float inset = Radius * 0.55f;
                g.DrawLine(pen, r.X + inset, r.Bottom - 0.8f, r.Right - inset, r.Bottom - 0.8f);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintSelf(e.Graphics);
            var t = Skin.T;
            float x = 22;
            float y = 17;
            if (!string.IsNullOrEmpty(Title))
            {
                // 标题前的小圆点，让排版有节奏
                var dot = new RectangleF(x, y + 6, 5, 5);
                Round.Fill(e.Graphics, dot, 2.5f, Skin.Alpha(t.Accent, 230));
                Txt.Draw(e.Graphics, Title, Skin.Ui(14.5f, FontStyle.Bold), t.Text,
                    new RectangleF(x + 11, y - 2, Width - 2 * x - 11, 22), Txt.Left);
                if (!string.IsNullOrEmpty(Hint))
                {
                    var size = Txt.Measure(e.Graphics, Hint, Skin.Ui(11.5f));
                    Txt.Draw(e.Graphics, Hint, Skin.Ui(11.5f), Skin.Alpha(t.Sub, 225),
                        new RectangleF(Width - x - size.Width - 2, y, size.Width + 4, 22), Txt.Right);
                }
            }
        }
    }

    /// <summary>卡片里的一行：左边标题/说明，右边控件。</summary>
    internal sealed class CandyRow : CandyBase
    {
        public string Label = "";
        public string Sub = "";
        public Glyph? Lead;
        public bool ShowSeparator = true;
        public bool LabelBold = true;
        public Control RightControl;
        public Color SurfaceColor = Color.White;

        bool _over;

        public CandyRow()
        {
            Height = 58;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Default;
        }

        public void Attach(Control right)
        {
            RightControl = right;
            Controls.Clear();
            if (right != null)
            {
                right.Anchor = AnchorStyles.Right;
                Controls.Add(right);
            }
            ApplyLayout();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            ApplyLayout();
        }

        void ApplyLayout()
        {
            if (RightControl == null) return;
            int rh = Math.Min(RightControl.Height, Height - 14);
            RightControl.SetBounds(Width - RightControl.Width - 22, (Height - rh) / 2, RightControl.Width, rh);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _over = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _over = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 悬停时整行轻微泛色：有反馈才不"廉价"
            if (_over)
            {
                var hr = new RectangleF(6, 3, Width - 12, Height - 6);
                Round.Fill(g, hr, 13, Skin.Alpha(T.Accent, T.Dark ? 22 : 12));
            }

            float x = 22;
            if (Lead.HasValue)
            {
                int s = 17;
                var plate = new RectangleF(x - 4, (Height - 26) / 2f, 26, 26);
                Round.Fill(g, plate, 9, Skin.Alpha(T.Accent, T.Dark ? 32 : 24));
                Ico.Draw(g, Lead.Value, new RectangleF(x + 1.5f, (Height - s) / 2f, s, s), T.Accent, 1.7f);
                x += 34;
            }

            float textW = Width - x - (RightControl != null ? RightControl.Width + 34 : 22);
            bool twoLine = !string.IsNullOrEmpty(Sub);
            var labelRect = new RectangleF(x, twoLine ? 9 : 0, textW, twoLine ? 22 : Height);
            Txt.Draw(g, Label, LabelBold ? Skin.Ui(13.5f, FontStyle.Bold) : Skin.Ui(13f), T.Text, labelRect, Txt.Left);
            if (twoLine)
                Txt.Draw(g, Txt.Ellipsis(g, Sub, Skin.Ui(11.5f), textW), Skin.Ui(11.5f), Skin.Alpha(T.Sub, 228),
                    new RectangleF(x, 30, textW, 18), Txt.Left);

            if (ShowSeparator)
            {
                using (var p = new Pen(Skin.Alpha(T.Line, T.Dark ? 120 : 235), 1f))
                    g.DrawLine(p, x, Height - 0.5f, Width - 22, Height - 0.5f);
            }
        }
    }

    // ------------------------------------------------------------------ 平滑滚动容器

    internal sealed class ScrollHost : Panel
    {
        readonly Panel _page = new Panel();
        int _offset;
        float _target;
        bool _draggingBar;
        int _dragStartY, _dragStartOffset;

        public ScrollHost()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Skin.T.Bg;
            _page.AutoSize = false;
            _page.BackColor = Skin.T.Bg;
            Controls.Add(_page);
            Ticker.Add(Tick);
        }

        public Panel Page { get { return _page; } }

        public int ContentHeight
        {
            get { return _page.Height; }
            set
            {
                _page.Height = Math.Max(value, 0);
                ClampOffset();
                Invalidate();
            }
        }

        int Viewport { get { return Height; } }
        int MaxOffset { get { return Math.Max(0, _page.Height - Viewport); } }

        void ClampOffset()
        {
            _target = Math.Max(0, Math.Min(MaxOffset, _target));
            _offset = Math.Max(0, Math.Min(MaxOffset, _offset));
            _page.Top = -_offset;
        }

        void Tick()
        {
            if (!IsHandleCreated) return;
            if (Math.Abs(_target - _offset) < 0.6f)
            {
                if (_offset != (int)_target) { _offset = (int)_target; _page.Top = -_offset; Invalidate(); }
                return;
            }
            _offset += (int)Math.Round((_target - _offset) * 0.35);
            _page.Top = -_offset;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _target -= e.Delta * 0.9f;
            ClampOffset();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _page.Width = Width;
            ClampOffset();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_page.Height <= Viewport) return;

            float trackX = Width - 7;
            float ratio = Viewport / (float)_page.Height;
            float thumbH = Math.Max(34, Viewport * ratio);
            float maxScroll = MaxOffset;
            float pos = maxScroll <= 0 ? 0 : (_offset / maxScroll) * (Viewport - thumbH);

            Round.Fill(g, new RectangleF(trackX, pos + 2, 5, thumbH - 4), 3, Skin.Alpha(Skin.T.Sub, 110));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.X >= Width - 12 && _page.Height > Viewport)
            {
                _draggingBar = true;
                _dragStartY = e.Y;
                _dragStartOffset = _offset;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_draggingBar) return;
            float ratio = Viewport / (float)_page.Height;
            float thumbH = Math.Max(34, Viewport * ratio);
            float range = Math.Max(1, Viewport - thumbH);
            float delta = (e.Y - _dragStartY) / range * MaxOffset;
            _target = _offset = (int)(_dragStartOffset + delta);
            ClampOffset();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _draggingBar = false;
            Capture = false;
        }
    }
}
