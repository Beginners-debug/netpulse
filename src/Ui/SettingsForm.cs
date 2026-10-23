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
    /// <summary>导航条目。</summary>
    internal struct NavItem
    {
        public string Text;
        public Glyph Icon;
        public NavItem(string text, Glyph icon) { Text = text; Icon = icon; }
    }

    /// <summary>左侧导航栏：自绘的胶囊选中效果。</summary>
    internal sealed class NavBar : Control
    {
        public NavItem[] Items = new NavItem[0];
        public int Selected;
        int _hover = -1;
        public event EventHandler SelectedChanged;

        public NavBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = 232;
        }

        Rectangle ItemRect(int i) { return new Rectangle(18, 4 + i * 48, Width - 36, 42); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = -1;
            for (int i = 0; i < Items.Length; i++) if (ItemRect(i).Contains(e.Location)) { hit = i; break; }
            if (hit != _hover) { _hover = hit; Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Cursor = Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            for (int i = 0; i < Items.Length; i++)
            {
                if (!ItemRect(i).Contains(e.Location)) continue;
                if (i == Selected) return;
                Selected = i;
                Invalidate();
                var h = SelectedChanged;
                if (h != null) h(this, EventArgs.Empty);
                return;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var t = Skin.T;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            for (int i = 0; i < Items.Length; i++)
            {
                var r = ItemRect(i);
                bool sel = i == Selected;
                bool hov = i == _hover;

                if (sel)
                {
                    var rr = new Rectangle(r.X, r.Y, r.Width, r.Height);
                    Round.Gradient(g, rr, 14, Skin.Blend(t.Accent, Color.White, t.Dark ? 0.10 : 0.28), t.Accent, 25);
                    Round.Shadow(g, rr, 14, Skin.Alpha(t.Accent, 70), 5, 0.45f);
                }
                else if (hov)
                {
                    Round.Fill(g, r, 14, Skin.Alpha(t.Text, t.Dark ? 22 : 9));
                }

                var ir = new RectangleF(r.X + 15, r.Y + 12, 18, 18);
                Ico.Draw(g, Items[i].Icon, ir, sel ? Color.White : Skin.Alpha(t.Text, 185), 1.8f);
                Txt.Draw(g, Items[i].Text, Skin.Ui(13f, sel ? FontStyle.Bold : FontStyle.Regular),
                    sel ? Color.White : Skin.Alpha(t.Text, 225),
                    new RectangleF(r.X + 44, r.Y, r.Width - 48, r.Height), Txt.Left);

                if (sel)
                    Ico.Draw(g, Glyph.ChevronRight, new RectangleF(r.Right - 24, r.Y + 14, 12, 12), Skin.Alpha(Color.White, 190), 1.7f);
            }
        }
    }

    /// <summary>主题色卡选择器。</summary>
    internal sealed class ThemePicker : Control
    {
        int _hover = -1;

        public ThemePicker()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 92;
        }

        int CellW { get { return Math.Max(1, (Width - 4) / Theme.All.Length); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = e.X / Math.Max(1, CellW);
            if (hit >= Theme.All.Length) hit = -1;
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            if (hit != _hover) { _hover = hit; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int hit = e.X / Math.Max(1, CellW);
            if (hit < 0 || hit >= Theme.All.Length) return;
            var form = FindForm() as SettingsForm;
            if (form != null) form.ApplyTheme(Theme.All[hit].Key);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var cur = Skin.T;

            for (int i = 0; i < Theme.All.Length; i++)
            {
                var th = Theme.All[i];
                bool sel = th.Key == cur.Key;
                bool hov = i == _hover;
                // 色卡高度跟着容器走（界面字号变大时也跟着长），留 34px 给下面的名字
                float cellH = Math.Max(44f, Height - 34f);
                var cell = new RectangleF(2 + i * CellW, 0, CellW - 12, cellH);

                Round.Fill(g, new RectangleF(cell.X, cell.Y + 3, cell.Width, cell.Height), 15, Skin.Alpha(th.Down, 26));
                Round.Fill(g, cell, 15, th.Bg);
                Round.Stroke(g, cell, 15, sel ? th.Down : Skin.Alpha(th.Down, hov ? 170 : 90), sel ? 2.4f : 1f);

                // 两个小色点代表下载/上传
                float cy = cell.Y + cell.Height / 2f - 9;
                Round.Fill(g, new RectangleF(cell.X + 14, cy, 18, 18), 9, th.Down);
                Round.Fill(g, new RectangleF(cell.X + 36, cy, 18, 18), 9, th.Up);

                if (sel)
                {
                    var badge = new RectangleF(cell.Right - 19, cell.Y + 7, 13, 13);
                    Round.Fill(g, badge, 6.5f, th.Accent);
                    Ico.Draw(g, Glyph.Check, new RectangleF(badge.X + 2.5f, badge.Y + 2.5f, 8, 8), Color.White, 1.7f);
                }

                Txt.Draw(g, th.Name, Skin.Ui(10.5f, sel ? FontStyle.Bold : FontStyle.Regular),
                    Skin.Alpha(cur.Text, sel ? 245 : 165),
                    new RectangleF(cell.X, cell.Bottom + 5, cell.Width, 20), Txt.Center);
            }
        }
    }

    /// <summary>圆角输入框。</summary>
    internal sealed class InputBox : Control
    {
        public readonly TextBox Box;
        public event EventHandler TextChanged2;
        bool _focus;

        public InputBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = Skin.Ui(12f),
                BackColor = Skin.T.Card,
                ForeColor = Skin.T.Text,
                TextAlign = HorizontalAlignment.Left
            };
            Box.Enter += (s, e) => { _focus = true; Invalidate(); };
            Box.Leave += (s, e) => { _focus = false; Invalidate(); };
            Box.TextChanged += (s, e) => { var h = TextChanged2; if (h != null) h(this, EventArgs.Empty); };
            Controls.Add(Box);
            Height = 34;   // 必须在 Box 建好之后再设，否则 OnLayout 里会踩到 null
        }

        public string Value { get { return Box.Text; } set { Box.Text = value ?? ""; } }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Box == null) return;
            Box.SetBounds(12, (Height - Box.PreferredHeight) / 2 + 1, Math.Max(10, Width - 24), Box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var t = Skin.T;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Round.Fill(g, r, 11, t.Dark ? t.Card2 : t.Card);
            Round.Stroke(g, r, 11, _focus ? Skin.Alpha(t.Accent, 200) : Skin.Alpha(t.Line, t.Dark ? 140 : 235), _focus ? 1.6f : 1f);
        }
    }

    /// <summary>行右端的纯文本值。</summary>
    internal sealed class ValueText : Control
    {
        public string Value = "";
        public float Size2 = 12.5f;
        public Color PaintColor = Color.Empty;

        public ValueText(string value)
        {
            Value = value ?? "";
            Width = 150;
            Height = 26;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var t = Skin.T;
            var c = PaintColor.IsEmpty ? Skin.Alpha(t.Text, 210) : PaintColor;
            Txt.Draw(g, Txt.Ellipsis(g, Value, Skin.Ui(Size2, FontStyle.Bold), Width), Skin.Ui(Size2, FontStyle.Bold),
                c, new RectangleF(0, 0, Width, Height), Txt.Right);
        }
    }

    /// <summary>设置窗口：无边框圆角卡片 + 左侧导航 + 右侧分页。</summary>
    internal sealed class SettingsForm : Form
    {
        const int WinW = 1000;
        const int WinH = 704;
        const int NavW = 232;
        const int PageTop = 96;

        readonly Conf _conf;
        readonly NetMonitor _mon;
        readonly TrafficStore _store;
        readonly Action _onExit;

        NavBar _nav;
        readonly List<ScrollHost> _pages = new List<ScrollHost>();
        readonly string[] PageTitles = { "外观", "任务栏", "网络", "行为", "关于" };
        readonly string[] PageSubs =
        {
            "主题配色、字号与显示单位",
            "任务栏组件的形态与位置",
            "网卡、延迟检测与采样频率",
            "开机启动、数据与维护",
            "版本信息与开源许可"
        };

        int _hoverBtn = -1;

        public SettingsForm(Conf conf, NetMonitor mon, TrafficStore store, Action onExit)
        {
            _conf = conf;
            _mon = mon;
            _store = store;
            _onExit = onExit;

            Text = "NetPulse 设置";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(WinW, WinH);
            DoubleBuffered = true;
            KeyPreview = true;
            BackColor = Skin.T.Bg;
            Font = Skin.Ui(12f);
            if (Environment.OSVersion.Version.Build < 22000) RoundRegion();

            var _swc = System.Diagnostics.Stopwatch.StartNew();
        BuildNav();
        App.Paths.Log("SettingsForm.BuildNav " + _swc.ElapsedMilliseconds + "ms");
            _swc.Restart();
        BuildPages();
        App.Paths.Log("SettingsForm.BuildPages " + _swc.ElapsedMilliseconds + "ms");

            Skin.ThemeChanged += OnThemeChanged;
        }

        void RoundRegion()
        {
            using (var p = Round.Make(new RectangleF(0, 0, Width, Height), 22))
                Region = new Region(p);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;    // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2;   // DWMWCP_ROUND
                Native.DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            }
            catch { }
        }

        void BuildNav()
        {
            if (_nav != null) { Controls.Remove(_nav); _nav.Dispose(); _nav = null; }
            _nav = new NavBar
            {
                Items = new[]
                {
                    new NavItem("外观", Glyph.Palette),
                    new NavItem("任务栏", Glyph.Pin),
                    new NavItem("网络", Glyph.Wifi),
                    new NavItem("行为", Glyph.Gear),
                    new NavItem("关于", Glyph.Info)
                },
                Bounds = new Rectangle(0, PageTop - 4, NavW, 5 * 48 + 8)
            };
            _nav.SelectedChanged += (s, e) => ShowPage(_nav.Selected);
            Controls.Add(_nav);
        }

        void BuildPages()
        {
            foreach (var p in _pages) { Controls.Remove(p); p.Dispose(); }
            _pages.Clear();

            _pages.Add(BuildAppearance());
            _pages.Add(BuildTaskbar());
            _pages.Add(BuildNetwork());
            _pages.Add(BuildBehavior());
            _pages.Add(BuildAbout());

            foreach (var p in _pages) { p.Visible = false; Controls.Add(p); }

            int sel = _nav != null ? Math.Max(0, Math.Min(_pages.Count - 1, _nav.Selected)) : 0;
            ShowPage(sel);
        }

        /// <summary>外部（命令行/调试）指定打开哪一页。</summary>
        /// <summary>调试用：把窗口自身渲染成 PNG（不需要桌面截图）。</summary>
        public void CaptureTo(string path)
        {
            try
            {
                var bmp = new Bitmap(Width, Height);
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                App.Paths.Log("截图 → " + path);
            }
            catch (Exception ex) { App.Paths.Log("截图失败: " + ex.Message); }
        }

        public void ShowPageAt(int index)
        {
            if (_pages.Count == 0) return;
            index = Math.Max(0, Math.Min(_pages.Count - 1, index));
            if (_nav != null) _nav.Selected = index;
            ShowPage(index);
        }

        void ShowPage(int index)
        {
            for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == index;
            Invalidate(new Rectangle(NavW, 0, WinW - NavW, PageTop));
        }

        // ---------------- 分页构建工具 ----------------

        ScrollHost NewPage()
        {
            var host = new ScrollHost
            {
                Bounds = new Rectangle(NavW + 12, PageTop, WinW - NavW - 12, WinH - PageTop - 6)
            };
            host.Page.Width = host.Width - 16;
            return host;
        }

        sealed class Flow
        {
            public readonly ScrollHost Host;
            public int Y;

            public Flow(ScrollHost host) { Host = host; }

            /// <summary>按界面字号缩放的行高/间距：字号变了，行也要跟着变，否则大字会溢出。</summary>
            public static int S(int v) { return (int)Math.Round(v * Math.Max(0.8f, Skin.Scale)); }

            public Section Add(string title, string hint)
            {
                return new Section(this, title, hint);
            }
        }

        sealed class Section
        {
            const int PadX = 22;
            readonly Flow _flow;
            readonly int _top;
            readonly CandyCard _card;
            int _cursor;

            public Section(Flow flow, string title, string hint)
            {
                _flow = flow;
                _top = flow.Y;
                _card = new CandyCard
                {
                    Title = title,
                    Hint = hint,
                    Radius = 22,
                    Bounds = new Rectangle(0, _top, flow.Host.Page.Width, 88)
                };
                flow.Host.Page.Controls.Add(_card);
                _cursor = Flow.S(56);
                Sync();
            }

            void Sync()
            {
                _card.Height = _cursor + Flow.S(12);
                _flow.Y = _top + _card.Height + Flow.S(16);
                _flow.Host.ContentHeight = _flow.Y + Flow.S(16);
            }

            public Section Custom(Control c, int height)
            {
                int hh = Flow.S(height);
                c.SetBounds(PadX, _cursor, _card.Width - PadX * 2, hh);
                _card.Controls.Add(c);
                _cursor += hh;
                Sync();
                return this;
            }

            public Section Row(string label, string sub, Control right, bool sep = true)
            {
                var r = new CandyRow
                {
                    Label = label,
                    Sub = sub,
                    ShowSeparator = sep,
                    Bounds = new Rectangle(0, _cursor, _card.Width, Flow.S(58))
                };
                if (right != null) r.Attach(right);
                _card.Controls.Add(r);
                _cursor += Flow.S(58);
                Sync();
                return this;
            }

            public Section Text(string label, string sub, string value)
            {
                return Row(label, sub, new ValueText(value), true);
            }

            public Section Note(string text)
            {
                var r = new CandyRow
                {
                    Label = text,
                    LabelBold = false,
                    ShowSeparator = false,
                    Bounds = new Rectangle(0, _cursor, _card.Width, 44)
                };
                _card.Controls.Add(r);
                _cursor += 44;
                Sync();
                return this;
            }
        }

        CandySwitch Sw(bool value, EventHandler changed)
        {
            var s = new CandySwitch { Size = new Size(46, 26), Checked = value };
            if (changed != null) s.Toggled += changed;
            return s;
        }

        CandySlider Slider(int min, int max, int value, EventHandler changed, int width = 200)
        {
            var s = new CandySlider { Min = min, Max = max, Value = value, Size = new Size(width, 30) };
            if (changed != null) s.ValueChanged += changed;
            return s;
        }

        CandySegmented Seg(string[] items, int sel, EventHandler changed, int width = 260)
        {
            var s = new CandySegmented { Items = items, Selected = sel, Size = new Size(width, 32) };
            if (changed != null) s.SelectedChanged += changed;
            return s;
        }

        SelectBox Sel(string[] items, int sel, EventHandler changed, int width = 230)
        {
            var s = new SelectBox { Items = items, Selected = sel, Size = new Size(width, 34) };
            if (changed != null) s.SelectedChanged += changed;
            return s;
        }

        CandyButton Btn(string text, BtnStyle style, EventHandler click, int width = 96)
        {
            var b = new CandyButton { Text = text, Style = style, Size = new Size(width, 34) };
            if (click != null) b.Click += click;
            return b;
        }

        // ---------------- 外观 ----------------

        ScrollHost BuildAppearance()
        {
            var host = NewPage();
            var f = new Flow(host);

            f.Add("主题", "点一下换肤，整只软件跟着变")
                .Custom(new ThemePicker(), 88)
                .Row("界面字号", "整体缩放 80% – 140%", Slider(80, 140, _conf.FontScale, (s, e) =>
                {
                    // 只调字号，不重建窗口。
                    // 之前这里每动一像素就 RebuildAll()：正在拖的那个滑块被销毁重建，
                    // 新滑块拿到 Width=0，首次 MouseMove 算出比例 1.0 → 直接跳到 140%，
                    // 之后鼠标事件落在新控件上却又不是拖拽态，于是“卡在 140 动不了”。
                    _conf.FontScale = ((CandySlider)s).Value;
                    Skin.Apply(Theme.Get(_conf.ThemeKey), _conf.FontScale);
                    RepaintSkin();
                    DebounceNotify();
                }), false);

            f.Add("任务栏内容", "组件上出现哪些信息")
                .Row("实时曲线", "在组件里画一小段流量曲线",
                    Sw(_conf.ShowSpark, (s, e) => { _conf.ShowSpark = ((CandySwitch)s).Checked; _conf.Notify(); }))
                .Row("延迟显示", "组件右侧显示 ping 毫秒数",
                    Sw(_conf.ShowPing, (s, e) => { _conf.ShowPing = ((CandySwitch)s).Checked; _conf.Notify(); }))
                .Row("背景胶囊", "文字后面垫一层半透明小卡片",
                    Sw(_conf.Pill, (s, e) => { _conf.Pill = ((CandySwitch)s).Checked; _conf.Notify(); }))
                .Row("速率单位", "自动会按大小切换 KB / MB",
                    Seg(new[] { "自动", "B/s", "KB/s", "MB/s" }, _conf.UnitMode, (s, e) =>
                    {
                        _conf.UnitMode = ((CandySegmented)s).Selected;
                        _conf.Notify();
                    }), false);

            f.Add("细节", "胶囊的浓淡")
                .Row("胶囊透明度", "数值越大越不透明", Slider(35, 100, _conf.Alpha, (s, e) =>
                {
                    _conf.Alpha = ((CandySlider)s).Value;
                    _conf.Notify();
                }), false);

            return host;
        }

        // ---------------- 任务栏 ----------------

        ScrollHost BuildTaskbar()
        {
            var host = NewPage();
            var f = new Flow(host);

            f.Add("组件", "像一个小挂件，安静地待在托盘左边")
                .Row("组件大小", "只影响任务栏那一小块，和界面字号无关", Slider(70, 140, _conf.WidgetScale, (s, e) =>
                {
                    _conf.WidgetScale = ((CandySlider)s).Value;
                    DebounceNotify();
                }))
                .Row("显示组件", "关掉后只留托盘图标",
                    Sw(_conf.WidgetOn, (s, e) => { _conf.WidgetOn = ((CandySwitch)s).Checked; _conf.Notify(); }))
                .Row("单行显示", "把上下行压成一行，更省地方",
                    Sw(_conf.SingleLine, (s, e) => { _conf.SingleLine = ((CandySwitch)s).Checked; _conf.Notify(); }))
                .Row("点击打开面板", "点一下弹出详情卡片",
                    Sw(_conf.ClickOpensPanel, (s, e) => { _conf.ClickOpensPanel = ((CandySwitch)s).Checked; _conf.Notify(); }))
                .Row("横向偏移", "往左或往右挪一点点", Slider(-24, 24, _conf.OffsetX, (s, e) =>
                {
                    _conf.OffsetX = ((CandySlider)s).Value;
                    _conf.Notify();
                }), false);

            f.Add("采样", "越短越灵敏，也越费一点电")
                .Row("采样间隔", "流量统计的刷新频率",
                    Seg(new[] { "0.5 秒", "1 秒", "2 秒" }, SegIndex(_conf.SampleMs), (s, e) =>
                    {
                        int i = ((CandySegmented)s).Selected;
                        _conf.SampleMs = i == 0 ? 500 : i == 1 ? 1000 : 2000;
                        _conf.Notify();
                    }), false);

            string where = "托盘左侧";
            string size = "未知";
            try
            {
                var tb = TaskbarInfo.Query();
                if (tb != null)
                {
                    where = tb.Edge == 0 ? "屏幕底部" : tb.Edge == 1 ? "屏幕顶部" : tb.Edge == 2 ? "屏幕左侧" : "屏幕右侧";
                    size = tb.Height + " × " + tb.Width + " px";
                }
            }
            catch { }

            f.Add("位置", "自动跟随任务栏，不需要手动摆")
                .Text("停靠位置", "任务栏在哪，它就跟到哪", where)
                .Text("任务栏尺寸", "读取系统实时数值", size)
                .Note("任务栏移动、自动隐藏、全屏应用时都会自动收起再回来。");

            return host;
        }

        static int SegIndex(int ms)
        {
            return ms <= 600 ? 0 : ms <= 1400 ? 1 : 2;
        }

        static int PingSegIndex(int ms)
        {
            return ms <= 600 ? 0 : ms <= 1400 ? 1 : ms <= 2600 ? 2 : 3;
        }

        // ---------------- 网络 ----------------

        ScrollHost BuildNetwork()
        {
            var host = NewPage();
            var f = new Flow(host);

            var list = IfTable.List();
            var names = new List<string> { "全部网卡（合计）" };
            for (int i = 0; i < list.Count; i++)
                names.Add(list[i].Name + (list[i].Up ? "" : "（未连接）"));

            int sel = 0;
            for (int i = 0; i < list.Count; i++) if ((int)list[i].Index == _conf.Adapter) sel = i + 1;

            var sample = _mon.Sample;
            string state = sample.LinkUp ? "已连接" : "未连接";
            string speed = sample.LinkSpeed > 0 ? (sample.LinkSpeed / 1000000) + " Mbps" : "—";

            f.Add("网卡", "只统计指定网卡的流量")
                .Row("统计范围", "默认把所有网卡加在一起", Sel(names.ToArray(), sel, (s, e) =>
                {
                    int i = ((SelectBox)s).Selected;
                    _conf.Adapter = i <= 0 ? -1 : (int)list[i - 1].Index;
                    _conf.Notify();
                }, 290))
                .Text("当前状态", "网卡是否处于连接状态", state)
                .Text("链路速率", "网卡协商出来的带宽", speed);

            // 顺序必须和 hosts 一一对应，否则「选了阿里 DNS 实际测得是 Cloudflare」。
            var targets = new[] { "1.1.1.1  Cloudflare", "8.8.8.8  Google", "9.9.9.9  Quad9",
                                  "223.5.5.5  阿里 DNS", "119.29.29.29  腾讯 DNS", "baidu.com  百度" };
            var hosts = new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9", "223.5.5.5", "119.29.29.29", "baidu.com" };

            int ti = 0;
            for (int i = 0; i < hosts.Length; i++) if (hosts[i] == _conf.PingTarget) ti = i;

            var custom = new InputBox { Size = new Size(230, 34), Value = _conf.PingTarget };
            custom.TextChanged2 += (s, e) =>
            {
                _conf.PingTarget = custom.Value.Trim();
                _conf.Notify();
            };

            f.Add("延迟检测", "用 ICMP 探测，判断线路质量")
                .Row("开启检测", "关闭后不再发探测包",
                    Sw(_conf.PingOn, (s, e) => { _conf.PingOn = ((CandySwitch)s).Checked; _conf.Notify(); }))
                .Row("探测目标", "也可以自己填一个域名或 IP",
                    Sel(targets, ti, (s, e) =>
                    {
                        int i = ((SelectBox)s).Selected;
                        _conf.PingTarget = hosts[i];
                        custom.Value = hosts[i];
                        _conf.Notify();
                    }, 250))
                .Row("自定义目标", "改完自动生效", custom)
                .Row("探测间隔", "延迟检测的发送频率",
                    Seg(new[] { "0.5 秒", "1 秒", "2 秒", "5 秒" }, PingSegIndex(_conf.PingMs), (s, e) =>
                    {
                        int i = ((CandySegmented)s).Selected;
                        _conf.PingMs = i == 0 ? 500 : i == 1 ? 1000 : i == 2 ? 2000 : 5000;
                        _conf.Notify();
                    }, 320), false);

            return host;
        }

        // ---------------- 行为 ----------------

        ScrollHost BuildBehavior()
        {
            var host = NewPage();
            var f = new Flow(host);

            f.Add("启动与运行", "让它安静地跟着系统一起醒来")
                .Row("开机自启", "写入当前用户的启动项",
                    Sw(Startup.IsEnabled(), (s, e) =>
                    {
                        bool on = ((CandySwitch)s).Checked;
                        Startup.Set(on);
                        _conf.AutoStart = on;
                        _conf.Notify();
                    }))
                .Row("启动时弹出面板", "开机先看一眼今天用了多少",
                    Sw(_conf.ShowPanelOnStart, (s, e) => { _conf.ShowPanelOnStart = ((CandySwitch)s).Checked; _conf.Notify(); }), false);

            f.Add("数据与维护", "全部数据都存在本地，不上传任何东西")
                .Row("数据目录", Paths.BaseDir, Btn("打开", BtnStyle.Soft, (s, e) =>
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Paths.BaseDir + "\""); } catch { }
                }, 88))
                .Row("今日流量", "把今天累计的数字清零", Btn("重置", BtnStyle.Soft, (s, e) =>
                {
                    _store.ResetToday();
                    _store.Save();
                    RebuildPagesKeepIndex();
                }, 88))
                .Row("历史记录", "删除最近 30 天的记录", Btn("清空", BtnStyle.Danger, (s, e) =>
                {
                    _store.ClearHistory();
                    _store.Save();
                    RebuildPagesKeepIndex();
                }, 88), false);

            f.Add("退出", "任务栏组件和托盘图标一起消失")
                .Row("退出 NetPulse", "下次开机还会自动启动（如果开着自启）",
                    Btn("退出", BtnStyle.Danger, (s, e) => { if (_onExit != null) _onExit(); }, 88), false);

            return host;
        }

        // ---------------- 关于 ----------------

        ScrollHost BuildAbout()
        {
            var host = NewPage();
            var f = new Flow(host);

            f.Add("NetPulse", "v3.0.1")
                .Text("版本", "当前构建", "3.0.1")
                .Text("运行环境", "无需安装运行时", ".NET Framework 4.8")
                .Text("开源协议", "可自由使用与修改", "MIT License")
                .Text("内置字体", "Fredoka（数字）+ 站酷快乐体（中文）", "SIL OFL 1.1")
                .Row("项目主页", "欢迎 star 与提 issue",
                    Btn("GitHub", BtnStyle.Soft, (s, e) =>
                    {
                        try { System.Diagnostics.Process.Start("https://github.com"); } catch { }
                    }, 96), false);

            f.Add("小技巧", "几个不太显眼但很顺手的操作")
                .Row("左键托盘图标", "打开详情面板", null)
                .Row("右键托盘图标", "同样是详情面板（旧的右键菜单已经退休）", null)
                .Row("点击任务栏组件", _conf.ClickOpensPanel ? "当前：打开详情面板" : "当前：不响应", null)
                .Row("滚轮", "面板和设置页都支持滚动", null, false);

            f.Add("关于这个项目", null)
                .Note("把网速放在看得见的地方，又不打扰你。")
                .Note("单文件、零依赖、离线可用，所有数据只留在本机。");

            return host;
        }

        // ---------------- 主题切换 ----------------

        void OnThemeChanged()
        {
            if (InvokeRequired) { BeginInvoke(new Action(OnThemeChanged)); return; }
            BackColor = Skin.T.Bg;
            Invalidate(true);
        }

        public void ApplyTheme(string key)
        {
            if (_conf.ThemeKey == key) return;
            _conf.ThemeKey = key;
            Skin.Set(key, _conf.FontScale);
            RepaintSkin();
            DebounceNotify();
        }

        /// <summary>换肤/换字号后原地刷新：重设容器底色 + 整窗重绘，不销毁任何控件。</summary>
        void RepaintSkin()
        {
            if (_lastScale < 0) _lastScale = _conf.FontScale;
            BackColor = Skin.T.Bg;
            Font = Skin.Ui(12f);
            Retint(this);
            Invalidate(true);
        }

        static void Retint(Control root)
        {
            foreach (Control c in root.Controls)
            {
                var card = c as CandyCard;
                if (card != null) c.BackColor = Skin.T.Card;
                else if (c is CandyRow || c is ScrollHost) c.BackColor = Color.Transparent;
                else if (c is TextBox)
                {
                    c.BackColor = Skin.T.Card;
                    c.ForeColor = Skin.T.Text;
                }
                if (c.Controls.Count > 0) Retint(c);
            }
        }

        Timer _confTimer;
        int _lastScale = -1;

        /// <summary>拖滑块时不要每一像素都写盘；停手 350ms 再落配置。</summary>
        void DebounceNotify()
        {
            if (_confTimer == null)
            {
                _confTimer = new Timer { Interval = 350 };
                _confTimer.Tick += (s, e) =>
                {
                    _confTimer.Stop();
                    _conf.Notify();
                    // 行高是按字号算的，松手后重排一次页面（此时没有拖拽，重建是安全的）
                    if (_lastScale > 0 && _lastScale != _conf.FontScale)
                    {
                        _lastScale = _conf.FontScale;
                        RebuildPagesKeepIndex();
                    }
                };
            }
            _confTimer.Stop();
            _confTimer.Start();
        }

        void RebuildAll()
        {
            int keep = _nav != null ? _nav.Selected : 0;
            SuspendLayout();
            Controls.Clear();
            BackColor = Skin.T.Bg;
            Font = Skin.Ui(12f);
            _pages.Clear();
            BuildNav();
            BuildPages();
            ResumeLayout(true);
            _nav.Selected = keep;
            ShowPageAt(keep);
            Invalidate(true);
        }

        void RebuildPagesKeepIndex()
        {
            int idx = _nav != null ? _nav.Selected : 0;
            BuildPages();
            if (_nav != null) _nav.Selected = idx;
            ShowPage(idx);
            if (_nav != null) _nav.Invalidate();
        }

        // ---------------- 窗口外观与交互 ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var t = Skin.T;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            g.Clear(t.Bg);

            // 左侧导航底：淡渐变
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, NavW + 40, Height),
                t.Dark ? t.Card : Skin.Blend(t.Accent, Color.White, 0.90),
                t.Bg, 0f))
                g.FillRectangle(b, 0, 0, NavW, Height);

            using (var pen = new Pen(Skin.Alpha(t.Line, t.Dark ? 120 : 190), 1f))
                g.DrawLine(pen, NavW, 28, NavW, Height - 24);

            // 品牌区
            var logo = new RectangleF(22, 26, 30, 30);
            Round.Gradient(g, new Rectangle(22, 26, 30, 30), 11, t.Down, t.Up, 35);
            Round.Shadow(g, new Rectangle(22, 26, 30, 30), 11, Skin.Alpha(t.Down, 70), 4, 0.5f);
            Ico.Draw(g, Glyph.Sparkle, new RectangleF(29, 33, 16, 16), Color.White, 1.9f);

            Txt.Draw(g, "NetPulse", Skin.Cute(20f), t.Text, new RectangleF(62, 24, 160, 30), Txt.Left);
            var verChip = new RectangleF(62, 52, 44, 16);
            Round.Fill(g, verChip, 8, Skin.Alpha(t.Accent, t.Dark ? 60 : 42));
            Txt.Draw(g, "v3.0", Skin.Ui(9f, FontStyle.Bold), Skin.Blend(t.Text, t.Accent, 0.40), verChip, Txt.Center);

            // 右侧标题
            int idx = _nav != null ? _nav.Selected : 0;
            Txt.Draw(g, PageTitles[idx], Skin.Cute(24f), t.Text,
                new RectangleF(NavW + 26, 26, 400, 34), Txt.Left);
            Txt.Draw(g, PageSubs[idx], Skin.Ui(11.5f), Skin.Alpha(t.Sub, 225),
                new RectangleF(NavW + 26, 62, 560, 20), Txt.Left);

            DrawWindowButtons(g, t);

            // 底部署名
            Txt.Draw(g, "本地运行 · 数据不出门", Skin.Ui(10f), Skin.Alpha(t.Sub, 160),
                new RectangleF(26, Height - 58, NavW - 46, 16), Txt.Left);
            Txt.Draw(g, "用心做的 · for GitHub", Skin.Ui(9.5f), Skin.Alpha(t.Sub, 130),
                new RectangleF(26, Height - 40, NavW - 46, 16), Txt.Left);

            // 圆角描边
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Round.Stroke(g, r, 22, Skin.Alpha(t.Line, t.Dark ? 160 : 215), 1f);
        }

        Rectangle BtnMin { get { return new Rectangle(Width - 98, 22, 34, 34); } }
        Rectangle BtnClose { get { return new Rectangle(Width - 56, 22, 34, 34); } }

        void DrawWindowButtons(Graphics g, Theme t)
        {
            DrawWinBtn(g, BtnMin, Glyph.Minus, _hoverBtn == 0, t);
            DrawWinBtn(g, BtnClose, Glyph.Close, _hoverBtn == 1, t);
        }

        void DrawWinBtn(Graphics g, Rectangle r, Glyph icon, bool hover, Theme t)
        {
            var rect = new RectangleF(r.X, r.Y, r.Width, r.Height);
            if (hover) Round.Fill(g, rect, 12, Skin.Alpha(icon == Glyph.Close ? t.Warn : t.Text, icon == Glyph.Close ? 40 : 20));
            Ico.Draw(g, icon, new RectangleF(r.X + r.Width / 2f - 7, r.Y + r.Height / 2f - 7, 14, 14),
                Skin.Alpha(icon == Glyph.Close && hover ? Color.White : t.Text, hover ? 240 : 190), 1.9f);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = BtnMin.Contains(e.Location) ? 0 : BtnClose.Contains(e.Location) ? 1 : -1;
            if (h != _hoverBtn) { _hoverBtn = h; Invalidate(new Rectangle(Width - 110, 16, 110, 50)); }
            Cursor = (h >= 0 || (e.Y < PageTop && e.X > NavW)) ? Cursors.Hand : e.Y < PageTop ? Cursors.SizeAll : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (BtnClose.Contains(e.Location)) { Close(); return; }
            if (BtnMin.Contains(e.Location)) { WindowState = FormWindowState.Minimized; return; }
            if (e.Y < PageTop && e.Button == MouseButtons.Left) DragWindow();
        }

        void DragWindow()
        {
            Native.ReleaseCapture();
            Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);   // WM_NCLBUTTONDOWN / HTCAPTION
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _conf.Save();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) Hide();   // 只收进托盘，不占任务栏
        }

        protected override void Dispose(bool disposing)
        {
            Skin.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }
}
