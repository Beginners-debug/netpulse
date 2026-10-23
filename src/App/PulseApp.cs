using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using NetPulse.App;
using NetPulse.Core;
using NetPulse.Interop;
using NetPulse.Ui;

namespace NetPulse
{
    /// <summary>
    /// 进程里唯一的应用上下文：把托盘、任务栏组件、面板、设置页和采样线程缝在一起。
    /// </summary>
    internal sealed class PulseApp : ApplicationContext
    {
        readonly Conf _conf;
        readonly NetMonitor _mon;
        readonly TrafficStore _store;
        readonly TrayIcon _tray;
        readonly TaskbarWidget _widget;

        PanelForm _panel;
        SettingsForm _settings;

        long _lastIn, _lastOut;
        bool _primed;
        int _saveTick;
        int _lastPanelTick;
        readonly EventWaitHandle _showEvent;
        readonly SynchronizationContext _ui;

        readonly int _startPage;
        readonly string _shot;
        readonly string _shotPanel;

        public PulseApp(EventWaitHandle showEvent, bool openSettings, int startPage, string shot = null, string shotPanel = null)
        {
            _showEvent = showEvent;
            _startPage = startPage;
            _shot = shot;
            _shotPanel = shotPanel;
            Paths.Log("NetPulse " + Application.ProductVersion + " 启动 · Windows " + OsName());

            _conf = new Conf(Paths.IniPath);
            bool firstRun = !System.IO.File.Exists(Paths.IniPath);
            Skin.Apply(Theme.Get(_conf.ThemeKey), _conf.FontScale);
            if ((firstRun || _conf.Migrated) && string.IsNullOrEmpty(shot) && string.IsNullOrEmpty(shotPanel))
            {
                try { _conf.Save(); } catch { }   // 首次运行 / 旧配置迁移：立刻落一份可读的 ini
            }

            _store = new TrafficStore(Paths.TrafficPath);
            _store.Load();

            _mon = new NetMonitor();
            ApplyNetworkConf();
            _mon.Tick += PostTick;

            _tray = new TrayIcon();
            _tray.LeftClick += () => OpenPanel();
            _tray.RightClick += () => OpenPanel();

            _widget = new TaskbarWidget(_conf, _mon);
            _widget.Clicked += () =>
            {
                if (_conf.ClickOpensPanel) OpenPanel();
            };
            _widget.RightClicked += () => OpenPanel();

            _conf.Changed += OnConfChanged;
            Skin.ThemeChanged += OnSkinChanged;

            _ui = SynchronizationContext.Current;
            _mon.Start();

            // 启动瞬间 shell 还在初始化，这时候去 SetWindowLongPtr 挂 owner 会拖住任务栏，
            // 让人误以为“任务栏假死”。等 1.2 秒再挂，期间先不显示组件。
            var warm = new System.Windows.Forms.Timer { Interval = 1200 };
            warm.Tick += (s, e) =>
            {
                warm.Stop(); warm.Dispose();
                _widget.Rebuild(_conf.WidgetOn);

                // 面板预先建好（隐藏）：第一次点托盘图标时就不用现场建窗口/载字体了，
                // 否则那段 40 毫秒左右的工作会落在 explorer 等我们返回的同步回调上。
                EnsurePanel().Prewarm();
            };
            warm.Start();

            // 第二个实例被点开时，把设置页叫到前面
            var t = new Thread(WatchShowEvent) { IsBackground = true, Name = "np-show" };
            t.Start();

            if (openSettings)
            {
                var st = new System.Windows.Forms.Timer { Interval = 200 };
                st.Tick += (s, e) =>
                {
                    st.Stop();
                    st.Dispose();
                    ShowSettings();
                    if (_settings != null) _settings.ShowPageAt(_startPage);
                    if (!string.IsNullOrEmpty(_shot)) CaptureAndQuit(_shot);
                };
                st.Start();
            }
            if (!string.IsNullOrEmpty(_shotPanel))
            {
                var st2 = new System.Windows.Forms.Timer { Interval = 1600 };
                st2.Tick += (s, e) =>
                {
                    st2.Stop(); st2.Dispose();
                    CapturePanelAndQuit(_shotPanel);
                };
                st2.Start();
            }
            else if (_conf.ShowPanelOnStart)
            {
                var timer = new System.Windows.Forms.Timer { Interval = 900 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    OpenPanel(true);
                };
                timer.Start();
            }
        }

        void WatchShowEvent()
        {
            Perf.Name("show");
            try
            {
                while (true)
                {
                    if (!_showEvent.WaitOne()) return;
                    try
                    {
                        // 第二个实例启动时举手：把设置页叫到前面来（没有就打开）
                        var ui = _ui;
                        if (ui != null) ui.Post(delegate { ShowSettings(); }, null);
                        else ShowSettings();

                        if (_settings != null && _settings.Visible)
                        {
                            if (_settings.InvokeRequired) _settings.BeginInvoke(new Action(() => _settings.Activate()));
                            else _settings.Activate();
                        }
                    }
                    catch { }
                    Thread.Sleep(200);
                }
            }
            catch { }
        }

        /// <summary>调试用：等窗口稳定后把自己渲染成 PNG，然后退出。</summary>
        void CaptureAndQuit(string path)
        {
            var t = new System.Windows.Forms.Timer { Interval = 700 };
            t.Tick += (s, e) =>
            {
                t.Stop(); t.Dispose();
                if (_settings != null) _settings.CaptureTo(path);
                var w = _widget;
                Exit();
                Application.Exit();
            };
            t.Start();
        }

        /// <summary>调试用：把面板离屏画布存成 PNG，然后退出。</summary>
        void CapturePanelAndQuit(string path)
        {
            try
            {
                if (_panel == null) OpenPanel();
                _panel.SaveCanvas(path);
                Paths.Log("面板截图 → " + path);
            }
            catch (Exception ex) { Paths.Log("面板截图失败: " + ex.Message); }
            var t2 = new System.Windows.Forms.Timer { Interval = 120 };
            t2.Tick += (s, e) => { t2.Stop(); t2.Dispose(); Exit(); Application.Exit(); };
            t2.Start();
        }

        void ApplyNetworkConf()
        {
            _mon.SelectedIndex = _conf.Adapter;
            _mon.IntervalMs = _conf.SampleMs;
            _mon.PingEnabled = _conf.PingOn;
            _mon.PingIntervalMs = _conf.PingMs;
            _mon.PingTarget = _conf.PingTarget;
        }

        void OnConfChanged()
        {
            try
            {
                ApplyNetworkConf();
                Skin.Apply(Theme.Get(_conf.ThemeKey), _conf.FontScale);
                _widget.Rebuild(true);
                if (_panel != null && _panel.Visible) _panel.Repaint();
            }
            catch (Exception ex) { Paths.Log("应用配置失败: " + ex.Message); }
        }

        void OnSkinChanged()
        {
            try
            {
                if (_panel != null && _panel.Visible) _panel.Repaint();
                _widget.RefreshView();
            }
            catch { }
        }

        /// <summary>Environment.OSVersion 在没吃到 manifest 时只肯报 6.2，这里直接读注册表的真实版本。</summary>
        static string OsName()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        object major = k.GetValue("CurrentMajorVersionNumber");
                        object build = k.GetValue("CurrentBuild");
                        if (major != null && build != null)
                            return major + "." + k.GetValue("CurrentMinorVersionNumber", "0") + " build " + build;
                    }
                }
            }
            catch { }
            return Environment.OSVersion.Version.ToString();
        }

        void OnTick()
        {
            var s = _mon.Sample;

            // 速率是「字节差/实际间隔」，把它积分回去得到真实流量（累加而不是取累计计数器，网卡重建也不会跳变）
            double dt = _mon.LastDtSec;
            if (_primed && dt > 0.05)
            {
                long din = (long)(s.Down * dt);
                long dout = (long)(s.Up * dt);
                if (din > 0 || dout > 0) _store.Add(din, dout);
            }
            _primed = true;

            int nowMs = Environment.TickCount;
            if (nowMs - _iconAt > 2000 || nowMs - _iconAt < 0)
            {
                _iconAt = nowMs;
                var swTray = System.Diagnostics.Stopwatch.StartNew();
                _tray.Update(s, s.LinkUp);
                Perf.NoteTray((int)swTray.ElapsedMilliseconds);
            }

            // 托盘提示只在「连上/断开」这种状态变化时才改：
            // 每次改文本都是一次跨进程 NIM_MODIFY，explorer 忙的时候会把我们卡住。
            string tip = s.LinkUp ? "NetPulse 3.0 · 运行中" : "NetPulse 3.0 · 网络断开";
            if (tip != _tipText)
            {
                _tipText = tip;
                var swTip = System.Diagnostics.Stopwatch.StartNew();
                _tray.SetTooltip(tip);
                Perf.NoteTray((int)swTip.ElapsedMilliseconds);
            }

            _widget.RefreshView(s);
            if (_panel != null && _panel.Visible) _panel.Refresh(s);   // 同一份样本，面板与组件逐帧一致

            if (_conf.WidgetOn != _widget.Shown) _widget.Rebuild(true);

            _saveTick++;
            if (_saveTick >= 30)
            {
                _saveTick = 0;
                _store.Save();
            }
        }

        int _uiBusy;          // 1 = 已经有一条 UI 更新在排队，避免积压
        string _tipText = "";
        int _tipAt;
        int _iconAt;

        /// <summary>
        /// 采样线程回调：绝不能直接碰界面控件（NotifyIcon.Text 会 SendMessage 到 shell，
        /// 从后台线程调用会把 UI 线程和资源管理器一起卡死）。统一切回 UI 线程，并且只排一条。
        /// </summary>
        int _uiBusyAt;

        void PostTick()
        {
            int now = Environment.TickCount;
            if (System.Threading.Interlocked.CompareExchange(ref _uiBusy, 1, 0) != 0)
            {
                // 上一次投递迟迟没回来（消息循环被别的模态循环占着），
                // 超过 3 秒就强制放开闸门，免得网速从此定格。
                if (unchecked(now - _uiBusyAt) > 3000)
                    System.Threading.Interlocked.Exchange(ref _uiBusy, 0);
                return;
            }
            _uiBusyAt = now;

            var ui = _ui;
            if (ui != null) ui.Post(delegate { System.Threading.Interlocked.Exchange(ref _uiBusy, 0); OnTick(); }, null);
            else { System.Threading.Interlocked.Exchange(ref _uiBusy, 0); OnTick(); }
        }

        string Tooltip(NetSample s)
        {
            string v1, u1, v2, u2;
            Fmt.Speed(s.Down, _conf.UnitMode, out v1, out u1);
            Fmt.Speed(s.Up, _conf.UnitMode, out v2, out u2);
            string rtt = s.Rtt < 0 ? "--" : s.Rtt + "ms";
            return "NetPulse 3.0  ↓" + v1 + u1 + "  ↑" + v2 + u2 + "  延迟 " + rtt;
        }

        // ---------------- 面板 / 设置 ----------------

        void OpenPanel(bool autoHide = false)
        {
            try
            {
                int now = Environment.TickCount;
                if (_panel != null && _panel.Visible)
                {
                    _panel.HidePanel();
                    return;
                }
                if (unchecked(now - _lastPanelTick) < 120) return;
                _lastPanelTick = now;

                _panel = EnsurePanel();
                _panel.ShowNear(TrayAnchor(), autoHide);
            }
            catch (Exception ex) { Paths.Log("打开面板失败: " + ex.Message); }
        }

        /// <summary>面板只建一次（隐藏着），点击时只做显示和淡入。</summary>
        PanelForm EnsurePanel()
        {
            if (_panel == null)
                _panel = new PanelForm(_conf, _mon, _store, ShowSettings, Exit, OnConfChanged);
            return _panel;
        }

        Point TrayAnchor()
        {
            // 注意：这里绝对不能调 TaskbarInfo.Query()。
            // 它内部是 FindWindow/GetWindowRect 去问 explorer，而这个方法会在
            // “用户点了托盘图标” 的同步回调里被调用——一旦 explorer 正等着我们返回，
            // 就成了双向等待：任务栏卡死、图标点不动、我们自己的定时器也全停。
            var info = _widget.CachedTaskbar;
            if (info != null && !info.Vertical && info.Width > 0)
                return new Point(info.Right - 30, info.Top + info.Height / 2);

            var wa = Screen.PrimaryScreen.WorkingArea;
            return new Point(wa.Right - 30, wa.Bottom + 24);
        }

        void ShowSettings()
        {
            try
            {
                if (_settings == null || _settings.IsDisposed)
                {
                    var sw0 = System.Diagnostics.Stopwatch.StartNew();
                    _settings = new SettingsForm(_conf, _mon, _store, Exit);
                    Paths.Log("设置窗口构建 " + sw0.ElapsedMilliseconds + "ms");
                    _settings.FormClosed += (s, e) => { _settings = null; };
                }
                var sw1 = System.Diagnostics.Stopwatch.StartNew();
                if (!_settings.Visible) _settings.Show();
                Paths.Log("设置窗口 Show " + sw1.ElapsedMilliseconds + "ms");
                _settings.Activate();
                _settings.BringToFront();
            }
            catch (Exception ex) { Paths.Log("打开设置失败: " + ex.Message + Environment.NewLine + ex.StackTrace); }
        }

        void Exit()
        {
            try
            {
                _store.Save();
                _mon.Tick -= PostTick;
                _mon.Dispose();
                _widget.HideWidget();
                _widget.Dispose();
                _tray.Dispose();
                if (_settings != null) _settings.Close();
                if (_panel != null) _panel.Close();
            }
            catch { }
            ExitThread();
        }
    }
}
