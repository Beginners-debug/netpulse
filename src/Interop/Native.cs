using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NetPulse.Interop
{
    /// <summary>Win32 互操作。只放真正用到的入口，全部集中在这里。</summary>
    internal static class Native
    {
        // ---------------- 网卡计数器 ----------------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MIB_IF_ROW2
        {
            public ulong InterfaceLuid;
            public uint InterfaceIndex;
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
            public uint PhysicalAddressLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PhysicalAddress;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentPhysicalAddress;
            public uint Mtu;
            public uint Type;
            public uint TunnelType;
            public uint MediaType;
            public uint PhysicalMediumType;
            public uint AccessType;
            public uint DirectionType;
            public byte InterfaceAndOperStatusFlags;
            public uint OperStatus;
            public uint AdminStatus;
            public uint MediaConnectState;
            public Guid NetworkGuid;
            public uint ConnectionType;
            public ulong TransmitLinkSpeed;
            public ulong ReceiveLinkSpeed;
            public ulong InOctets;
            public ulong InUcastPkts;
            public ulong InNUcastPkts;
            public ulong InDiscards;
            public ulong InErrors;
            public ulong InUnknownProtos;
            public ulong InUcastOctets;
            public ulong InMulticastOctets;
            public ulong InBroadcastOctets;
            public ulong OutOctets;
            public ulong OutUcastPkts;
            public ulong OutNUcastPkts;
            public ulong OutDiscards;
            public ulong OutErrors;
            public ulong OutUcastOctets;
            public ulong OutMulticastOctets;
            public ulong OutBroadcastOctets;
            public ulong OutQLen;
        }

        [DllImport("iphlpapi.dll")]
        public static extern uint GetIfTable2(out IntPtr table);

        [DllImport("iphlpapi.dll")]
        public static extern void FreeMibTable(IntPtr table);

        // ---------------- ICMP ----------------

        [StructLayout(LayoutKind.Sequential)]
        public struct IP_OPTION_INFORMATION
        {
            public byte Ttl, Tos, Flags, OptionsSize;
            public IntPtr OptionsData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICMP_ECHO_REPLY
        {
            public uint Address;
            public uint Status;
            public uint RoundTripTime;
            public ushort DataSize;
            public ushort Reserved;
            public IntPtr Data;
            public IP_OPTION_INFORMATION Options;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern IntPtr IcmpCreateFile();

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern bool IcmpCloseHandle(IntPtr handle);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint IcmpSendEcho(IntPtr handle, uint address, byte[] requestData,
            ushort requestSize, IntPtr requestOptions, byte[] replyBuffer, uint replySize, uint timeout);

        // ---------------- 基础类型 ----------------

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int Cx, Cy; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS { public int Left, Right, Top, Bottom; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_APPWINDOW = 0x00040000;

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int GWLP_HWNDPARENT = -8;

        public const int ULW_ALPHA = 0x00000002;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const int SW_SHOW = 5;
        public const uint SWP_NOOWNERZORDER = 0x0200;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public static readonly IntPtr HWND_TOP = IntPtr.Zero;

        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        public static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

        public static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value)
                                    : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
        }

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

        public const uint GW_HWNDPREV = 3;

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dstPos, ref SIZE size,
            IntPtr srcDc, ref POINT srcPos, int colorKey, ref BLENDFUNCTION blend, int flags);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        /// <summary>
        /// 强行把窗口提到最前并抢到焦点。单纯 SetForegroundWindow 在调用方不是前台进程时会被系统忽略，
        /// 先临时挂到当前前台线程的输入队列再设置，成功率最高。
        /// </summary>
        public static void ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            // 只做“抬到最上层”，不抢焦点：跨进程抢焦点会被系统忽略，AttachThreadInput 还会把双方锁死。
            ShowWindow(hwnd, SW_SHOW);
            SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SetCapture(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr icon);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("shcore.dll")]
        public static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        // ---------------- WinEvent ----------------

        public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
            WinEventProc proc, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hook);

        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint EVENT_OBJECT_REORDER = 0x8004;
        public const uint EVENT_OBJECT_SHOW = 0x8002;
        public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
        public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;

        // ---------------- 主题色判断 ----------------

        /// <summary>任务栏是不是浅色（Win11 的"系统模式"）。</summary>
        public static bool IsTaskbarLight()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key == null ? null : key.GetValue("SystemUsesLightTheme");
                    if (v is int) return (int)v != 0;
                }
            }
            catch { }
            return false;
        }

        /// <summary>当前前台窗口是否全屏（含全屏游戏 / 视频），用于自动收起任务栏组件。</summary>
        public static bool IsFullscreenAppRunning()
        {
            // 不用 SHQueryUserNotificationState：开始菜单、通知中心打开时它也会报 BUSY，
            // 而且 shell 忙的时候这个 COM 调用会把调用方一起卡住。
            // 直接看前台窗口：铺满整块屏幕、且不是 shell 自己的窗口，才算全屏。
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                if (!IsWindowVisible(fg)) return false;
                if (fg == FindWindow("Shell_TrayWnd", null)) return false;
                if (fg == GetShellWindow() || fg == GetDesktopWindow()) return false;

                string cls = ClassNameOf(fg);
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" ||
                    cls == "Progman" || cls == "WorkerW" ||
                    cls == "ForegroundStaging" || cls == "MultitaskingViewFrame") return false;

                RECT r;
                if (!GetWindowRect(fg, out r)) return false;

                int sw = GetSystemMetrics(SM_CXSCREEN);
                int sh = GetSystemMetrics(SM_CYSCREEN);
                return r.Left <= 0 && r.Top <= 0 && r.Right >= sw && r.Bottom >= sh;
            }
            catch { return false; }
        }

        /// <summary>取窗口类名（拿不到就返回空串）。</summary>
        public static string ClassNameOf(IntPtr hwnd)
        {
            try
            {
                var sb = new StringBuilder(256);
                int n = GetClassName(hwnd, sb, sb.Capacity);
                return n > 0 ? sb.ToString(0, n) : "";
            }
            catch { return ""; }
        }
    }

    /// <summary>任务栏几何信息。位置变了就重排，内容变了不动。</summary>
    internal sealed class TaskbarInfo
    {
        public IntPtr Handle;
        public int Left, Top, Width, Height;
        public int TrayLeft = -1;      // 通知区域左边界
        public int ButtonsRight = -1;  // 任务按钮右边界
        public int Edge;               // 0=下 1=上 2=左 3=右

        public int Right { get { return Left + Width; } }
        public int Bottom { get { return Top + Height; } }
        public bool Vertical { get { return Edge == 2 || Edge == 3; } }

        public bool SameAs(TaskbarInfo o)
        {
            return o != null && Handle == o.Handle && Left == o.Left && Top == o.Top
                && Width == o.Width && Height == o.Height && TrayLeft == o.TrayLeft
                && ButtonsRight == o.ButtonsRight && Edge == o.Edge;
        }

        public static TaskbarInfo Query()
        {
            IntPtr tb = Native.FindWindow("Shell_TrayWnd", null);
            if (tb == IntPtr.Zero || !Native.IsWindowVisible(tb)) return null;

            Native.RECT r;
            if (!Native.GetWindowRect(tb, out r)) return null;

            var info = new TaskbarInfo
            {
                Handle = tb,
                Left = r.Left,
                Top = r.Top,
                Width = r.Right - r.Left,
                Height = r.Bottom - r.Top,
                TrayLeft = r.Right,
                ButtonsRight = r.Left,
                Edge = 0
            };

            int screenW = Native.GetSystemMetrics(Native.SM_CXSCREEN);
            int screenH = Native.GetSystemMetrics(Native.SM_CYSCREEN);

            // 先看长边方向：横向任务栏（宽 >= 高）只会贴上下边，纵向任务栏只会贴左右边。
            // 早期版本用「Left <= 2 就当中立」判断，导致底部任务栏（Left 恒为 0）被误判成左侧竖栏。
            if (info.Width >= info.Height)
                info.Edge = r.Top <= (screenH - info.Height) / 2 ? 1 : 0;
            else
                info.Edge = r.Left <= (screenW - info.Width) / 2 ? 2 : 3;

            IntPtr tray = Native.FindWindowEx(tb, IntPtr.Zero, "TrayNotifyWnd", null);
            Native.RECT tr;
            if (tray != IntPtr.Zero && Native.GetWindowRect(tray, out tr)) info.TrayLeft = tr.Left;

            foreach (string cls in new[] { "ReBarWindow32", "MSTaskSwWClass" })
            {
                IntPtr h = Native.FindWindowEx(tb, IntPtr.Zero, cls, null);
                Native.RECT br;
                if (h != IntPtr.Zero && Native.GetWindowRect(h, out br))
                    if (br.Right > info.ButtonsRight) info.ButtonsRight = br.Right;
            }

            // 自动隐藏 / 收起时整条掉到屏幕外（只剩 2px 触发条），此时视为没有任务栏
            if (info.Bottom <= 2 || info.Top >= screenH - 2 || info.Right <= 2 || info.Left >= screenW - 2) return null;
            if (info.Height <= 2 || info.Width <= 2) return null;
            return info;
        }
    }
}
