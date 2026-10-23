using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using NetPulse.App;
using NetPulse.Interop;

namespace NetPulse.Ui
{
    /// <summary>
    /// 分层窗口基类：整窗自绘（含阴影），逐像素 alpha，不抢焦点。
    /// 托盘面板、下拉菜单、任务栏小组件都由它派生。
    /// </summary>
    internal abstract class LayeredForm : Form
    {
        protected Bitmap Canvas;

        /// <summary>调试用：把当前离屏画布原样存成 PNG。</summary>
        internal void SaveCanvas(string path)
        {
            if (Canvas == null) return;
            try { Canvas.Save(path, System.Drawing.Imaging.ImageFormat.Png); } catch { }
        }
        protected Graphics CanvasGraphics;
        protected int Pad = 16;

        float _alpha;
        float _alphaTarget = 1f;
        bool _closing;
        readonly Timer _fade;

        // 重入保护：Push() 里 GetHbitmap 会临时锁住位图，此时任何 EnsureCanvas/Repaint
        // 都会抛「对象当前正在其他地方使用」。用标志位挡住，事后再补一次。
        bool _busy;
        bool _dirtyPaint;
        Size _pendingSize = Size.Empty;

        protected LayeredForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Magenta;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);

            _fade = new Timer { Interval = 15 };
            _fade.Tick += (s, e) => StepFade();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        // ---------------- 画布 ----------------

        protected void EnsureCanvas(int w, int h)
        {
            w = Math.Max(1, w);
            h = Math.Max(1, h);
            if (Canvas != null && Canvas.Width == w && Canvas.Height == h) return;

            if (_busy) { _pendingSize = new Size(w, h); return; }

            if (CanvasGraphics != null) { CanvasGraphics.Dispose(); CanvasGraphics = null; }
            if (Canvas != null) { Canvas.Dispose(); Canvas = null; }

            Bitmap bmp = null;
            Graphics gfx = null;
            try
            {
                bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                gfx = Graphics.FromImage(bmp);
                gfx.SmoothingMode = SmoothingMode.AntiAlias;
                gfx.TextRenderingHint = TextRenderingHint.AntiAlias;
                gfx.PixelOffsetMode = PixelOffsetMode.HighQuality;
                gfx.CompositingQuality = CompositingQuality.HighQuality;
            }
            catch (Exception ex)
            {
                if (gfx != null) gfx.Dispose();
                if (bmp != null) bmp.Dispose();
                Paths.Log("创建画布失败: " + ex.Message);
                return;
            }
            Canvas = bmp;
            CanvasGraphics = gfx;
        }

        /// <summary>重画并推送到屏幕。</summary>
        public void Repaint()
        {
            if (!IsHandleCreated || Canvas == null) return;
            if (_busy) { _dirtyPaint = true; return; }

            _busy = true;
            try
            {
                CanvasGraphics.Clear(Color.Transparent);
                CanvasGraphics.SetClip(new Rectangle(0, 0, Canvas.Width, Canvas.Height));
                System.Threading.Interlocked.Increment(ref Perf.Paints);
                OnLayerPaint(CanvasGraphics);
                CanvasGraphics.ResetClip();
                Push();
            }
            catch (Exception ex) { Paths.Log("重绘失败: " + ex.Message); }
            finally
            {
                _busy = false;
                if (_pendingSize != Size.Empty)
                {
                    var want = _pendingSize;
                    _pendingSize = Size.Empty;
                    EnsureCanvas(want.Width, want.Height);
                    _dirtyPaint = true;
                }
                if (_dirtyPaint)
                {
                    _dirtyPaint = false;
                    Repaint();
                }
            }
        }

        protected virtual void OnLayerPaint(Graphics g) { }

        protected void Push()
        {
            if (Canvas == null || !IsHandleCreated) return;
            System.Threading.Interlocked.Increment(ref Perf.Pushes);

            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = Canvas.GetHbitmap(Color.FromArgb(0));
                oldBitmap = Native.SelectObject(memDc, hBitmap);

                var size = new Native.SIZE { Cx = Canvas.Width, Cy = Canvas.Height };
                var src = new Native.POINT { X = 0, Y = 0 };
                var dst = new Native.POINT { X = Left, Y = Top };
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, (int)(_alpha * 255))),
                    AlphaFormat = Native.AC_SRC_ALPHA
                };

                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) { Native.SelectObject(memDc, oldBitmap); Native.DeleteObject(hBitmap); }
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        // ---------------- 淡入淡出 ----------------

        protected void FadeIn()
        {
            _closing = false;
            _alpha = 0f;
            _alphaTarget = 1f;
            _fade.Start();
        }

        public void FadeOut()
        {
            _closing = true;
            _alphaTarget = 0f;
            _fade.Start();
        }

        void StepFade()
        {
            if (_busy) return;
            _alpha += (_alphaTarget - _alpha) * 0.30f;
            if (Math.Abs(_alpha - _alphaTarget) < 0.02f)
            {
                _alpha = _alphaTarget;
                _fade.Stop();
                if (_closing)
                {
                    Hide();
                    _alpha = 0f;
                    return;
                }
            }
            Push();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && _alpha <= 0.01f) _alpha = 1f;
        }

        // ---------------- 鼠标 ----------------

        protected virtual void OnLayerMouseMove(Point p) { }
        protected virtual bool OnLayerMouseDown(Point p, MouseButtons button) { return false; }
        protected virtual void OnLayerMouseUp(Point p, MouseButtons button) { }
        protected virtual void OnLayerWheel(int delta) { }
        protected virtual bool CloseOnOutsideClick { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_MOUSEMOVE = 0x0200;
            const int WM_MOUSEWHEEL = 0x020A, WM_RBUTTONUP = 0x0205, WM_CAPTURECHANGED = 0x0215;

            if (m.Msg == WM_MOUSEMOVE || m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONUP)
            {
                int x = (short)(m.LParam.ToInt64() & 0xFFFF);
                int y = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                var p = new Point(x, y);

                if (m.Msg == WM_MOUSEMOVE) OnLayerMouseMove(p);
                else if (m.Msg == WM_LBUTTONDOWN)
                {
                    if (!ClientRectangle.Contains(p) && CloseOnOutsideClick) { Hide(); return; }
                    OnLayerMouseDown(p, MouseButtons.Left);
                }
                else OnLayerMouseUp(p, MouseButtons.Left);

                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == WM_RBUTTONUP)
            {
                int x = (short)(m.LParam.ToInt64() & 0xFFFF);
                int y = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                OnLayerMouseDown(new Point(x, y), MouseButtons.Right);
                return;
            }

            if (m.Msg == WM_MOUSEWHEEL)
            {
                OnLayerWheel((short)((m.WParam.ToInt64() >> 16) & 0xFFFF));
                return;
            }

            base.WndProc(ref m);
        }

        Timer _dismissPoll;

        /// <summary>
        /// 点击外部关闭。
        /// 以前用 SetCapture 独占全系统的鼠标，窗口一旦隐藏/失焦没释放，
        /// 整个桌面就会出现“鼠标一直转圈、点什么都没反应”，
        /// 必须切一下别的窗口才恢复 —— 用户遇到的假死就是它。
        /// 现在改成低频轮询：只在自己的窗口可见时看一眼左键位置。
        /// </summary>
        protected void CaptureForDismiss()
        {
            if (_dismissPoll == null)
            {
                _dismissPoll = new Timer { Interval = 120 };
                _dismissPoll.Tick += (s, e) => PollDismiss();
            }
            _dismissPoll.Start();
        }

        void PollDismiss()
        {
            if (!Visible || !CloseOnOutsideClick) { _dismissPoll.Stop(); return; }

            // 鼠标不在自己身上，且左键刚被按下 → 关掉
            if (Bounds.Contains(Cursor.Position)) return;
            bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (down) Hide();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (CloseOnOutsideClick && Visible && !ContainsFocus) { /* 由点击外部逻辑处理 */ }
        }
    }

    /// <summary>自定义下拉列表（替代 ComboBox，圆角、带阴影、能跟着主题变）。</summary>
    internal sealed class DropPopup : LayeredForm
    {
        readonly string[] _items;
        int _hover = -1;
        int _selected;
        int _itemHeight = 34;

        public event Action<int> Picked;

        public DropPopup(string[] items, int selected, int width)
        {
            _items = items ?? new string[0];
            _selected = selected;
            Pad = 12;
            int w = Pad * 2 + Math.Max(140, width);
            int h = Pad * 2 + Math.Max(_itemHeight, _items.Length * _itemHeight);
            ClientSize = new Size(w, h);
            EnsureCanvas(w, h);
            SetStyle(ControlStyles.Selectable, false);
        }

        public void ShowAt(Point screen, Control owner)
        {
            var wa = Screen.FromControl(owner).WorkingArea;
            int x = Math.Min(screen.X, wa.Right - Width);
            int y = screen.Y;
            if (y + Height > wa.Bottom) y = screen.Y - owner.Height - Height - 6;
            Location = new Point(Math.Max(wa.Left, x - 0), Math.Max(wa.Top, y));
            Show();
            FadeIn();
            Repaint();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CaptureForDismiss();
        }

        protected override void OnLayerPaint(Graphics g)
        {
            var t = Skin.T;
            var card = new RectangleF(Pad, Pad, Width - Pad * 2, Height - Pad * 2);
            Round.Shadow(g, card, 14, Color.FromArgb(90, 0, 0, 0), 10, 0.5f);
            Round.Fill(g, card, 14, t.Card);
            Round.Stroke(g, card, 14, Skin.Alpha(t.Line, t.Dark ? 140 : 220), 1f);

            for (int i = 0; i < _items.Length; i++)
            {
                var r = new RectangleF(Pad + 5, Pad + 3 + i * _itemHeight, card.Width - 10, _itemHeight - 2);
                if (i == _hover) Round.Fill(g, r, 9, t.Hover);
                if (i == _selected) Round.Fill(g, r, 9, Skin.Alpha(t.Accent, t.Dark ? 46 : 30));

                var fg = i == _selected ? t.Text : Skin.Alpha(t.Text, 225);
                Txt.Draw(g, _items[i], i == _selected ? Skin.Ui(13f, FontStyle.Bold) : Skin.Ui(13f), fg,
                    new RectangleF(r.X + 10, r.Y, r.Width - 34, r.Height), Txt.Left);
                if (i == _selected)
                    Ico.Draw(g, Glyph.Check, new RectangleF(r.Right - 24, r.Y + (r.Height - 13) / 2f, 13, 13), t.Accent, 2f);
            }
        }

        protected override void OnLayerMouseMove(Point p)
        {
            int idx = (p.Y - Pad - 3) / _itemHeight;
            if (p.Y < Pad || p.X < Pad || idx < 0 || idx >= _items.Length) idx = -1;
            if (idx != _hover) { _hover = idx; Repaint(); }
        }

        protected override bool OnLayerMouseDown(Point p, MouseButtons button)
        {
            if (button == MouseButtons.Right) { FadeOut(); return true; }
            int idx = (p.Y - Pad - 3) / _itemHeight;
            if (p.Y >= Pad && p.X >= Pad && idx >= 0 && idx < _items.Length)
            {
                var h = Picked;
                var picked = idx;
                Hide();
                if (h != null) h(picked);
            }
            else FadeOut();
            return true;
        }

        protected override void OnLayerWheel(int delta)
        {
            FadeOut();
        }
    }
}
