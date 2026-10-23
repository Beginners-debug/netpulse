using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using NetPulse.Interop;

namespace NetPulse.Core
{
    /// <summary>一次采样的完整快照。结构体，读的时候整体拷贝，不加锁。</summary>
    internal struct NetSample
    {
        public double Down;         // 字节/秒
        public double Up;
        public long DownTotal;      // 今日累计（由 TrafficStore 维护）
        public long UpTotal;
        public int Rtt;             // 毫秒，-1 表示超时
        public double Jitter;       // 毫秒
        public double Loss;         // 0-100
        public bool LinkUp;
        public int AdapterCount;
        public ulong LinkSpeed;
    }

    /// <summary>
    /// 采样引擎：一个线程读网卡计数器，一个线程打 ICMP。
    /// 全程无字符串分配，速率用 Stopwatch 差值计算，网卡消失/重建后自动重新对齐。
    /// </summary>
    internal sealed class NetMonitor : IDisposable
    {
        readonly object _sync = new object();
        NetSample _sample;

        Thread _speedThread;
        Thread _pingThread;
        readonly ManualResetEvent _stop = new ManualResetEvent(false);
        readonly ManualResetEvent _wake = new ManualResetEvent(false);
        readonly ManualResetEvent _pingWake = new ManualResetEvent(false);

        volatile int _intervalMs = 1000;
        volatile int _pingMs = 1000;
        volatile int _selectedIndex = -1;   // -1 = 全部网卡
        volatile bool _pingEnabled = true;

        string _pingTarget = "1.1.1.1";
        uint _pingAddr;

        // 滑动窗口
        const int PingWindow = 60;
        readonly int[] _rttRing = new int[PingWindow];
        int _rttPos, _rttCount;
        readonly List<int> _recentRtt = new List<int>(PingWindow);

        long _lastIn, _lastOut;
        uint _lastIndex = uint.MaxValue;
        bool _primed;
        readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>上一次速率采样实际经过的秒数（用于把 bps 积分成流量）。</summary>
        public double LastDtSec { get; private set; }

        public event Action Tick;      // 每秒一次（采样线程上），UI 自己切线程
        public IntPtr IcmpHandle = IntPtr.Zero;

        public int IntervalMs
        {
            get { return _intervalMs; }
            set { _intervalMs = Math.Max(200, Math.Min(10000, value)); _wake.Set(); }
        }

        public int PingIntervalMs
        {
            get { return _pingMs; }
            set { _pingMs = Math.Max(300, Math.Min(10000, value)); _pingWake.Set(); }
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set { _selectedIndex = value; _primed = false; _wake.Set(); }
        }

        public bool PingEnabled
        {
            get { return _pingEnabled; }
            set { _pingEnabled = value; _pingWake.Set(); }
        }

        public string PingTarget
        {
            get { return _pingTarget; }
            set
            {
                _pingTarget = string.IsNullOrWhiteSpace(value) ? "1.1.1.1" : value.Trim();
                _pingAddr = Resolve(_pingTarget);
                ClearRtt();
            }
        }

        public NetSample Sample
        {
            get { lock (_sync) return _sample; }
        }

        public void Start()
        {
            _pingAddr = Resolve(_pingTarget);

            _speedThread = new Thread(SpeedLoop) { IsBackground = true, Name = "np-speed", Priority = ThreadPriority.BelowNormal };
            _speedThread.Start();

            _pingThread = new Thread(PingLoop) { IsBackground = true, Name = "np-ping", Priority = ThreadPriority.Lowest };
            _pingThread.Start();
        }

        public void Dispose()
        {
            _stop.Set();
            _wake.Set();
            _pingWake.Set();
            try { if (_speedThread != null) _speedThread.Join(600); } catch { }
            try { if (_pingThread != null) _pingThread.Join(600); } catch { }
        }

        // ---------------- 速率 ----------------

        string _nameCache = "";
        int _nameIdx = -2;
        int _nameStamp;

        /// <summary>当前统计范围的显示名（未指定网卡时返回「全部网卡」）。</summary>
        public string AdapterName()
        {
            int want = _selectedIndex;
            int now = Environment.TickCount;
            if (want == _nameIdx && unchecked(now - _nameStamp) < 3000) return _nameCache;

            string name = "";
            IntPtr table = IntPtr.Zero;
            try
            {
                int count;
                table = IfTable.Open(out count);
                if (table != IntPtr.Zero)
                {
                    if (want < 0) name = "全部网卡";
                    else
                    {
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr row = IfTable.Row(table, i);
                            if ((int)IfTable.Index(row) != want) continue;
                            name = IfTable.Alias(row);
                            if (string.IsNullOrEmpty(name)) name = IfTable.Desc(row);
                            break;
                        }
                    }
                }
            }
            catch { }
            finally { IfTable.Close(table); }

            _nameIdx = want;
            _nameCache = name ?? "";
            _nameStamp = now;
            return _nameCache;
        }

        void SpeedLoop()
        {
            Perf.Name("speed");
            IntPtr table = IntPtr.Zero;
            var waits = new WaitHandle[] { _stop, _wake };

            while (!_stop.WaitOne(0))
            {
                double elapsed = 0;
                try
                {
                    int count;
                    table = IfTable.Open(out count);
                    if (table == IntPtr.Zero)
                    {
                        Publish(0, 0, false, 0, 0);
                    }
                    else
                    {
                        long inB = 0, outB = 0;
                        ulong link = 0;
                        bool found = false;
                        int active = 0;

                        int want = _selectedIndex;
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr row = IfTable.Row(table, i);
                            if (IfTable.OperStatus(row) != 1) continue;
                            if (IfTable.IfType(row) == 24) continue;
                            if (want < 0 && IfTable.IsNoise(IfTable.Alias(row), null)) continue;
                            active++;

                            uint idx = IfTable.Index(row);
                            if (want >= 0 && idx != (uint)want) continue;

                            inB += (long)IfTable.InOctets(row);
                            outB += (long)IfTable.OutOctets(row);
                            link = IfTable.LinkSpeed(row);
                            found = true;
                            if (want >= 0) break;
                        }

                        if (found)
                        {
                            double dt = (double)_clock.ElapsedTicks / Stopwatch.Frequency;
                            _clock.Restart();

                            double down = 0, up = 0;
                            if (_primed && dt > 0.05)
                            {
                                long dIn = inB - _lastIn;
                                long dOut = outB - _lastOut;
                                if (dIn < 0) dIn = 0;     // 计数器回绕/网卡重建
                                if (dOut < 0) dOut = 0;
                                down = dIn / dt;
                                up = dOut / dt;
                            }

                            _lastIn = inB;
                            _lastOut = outB;
                            _primed = true;
                            LastDtSec = dt;
                            Publish(down, up, true, active, link);
                        }
                        else
                        {
                            Publish(0, 0, false, active, 0);
                        }
                    }
                }
                catch
                {
                    Publish(0, 0, false, 0, 0);
                }
                finally
                {
                    IfTable.Close(table);
                }

                if (elapsed < 0) elapsed = 0;
                // WaitAny 返回 1 = 被 _wake 叫醒（改间隔/换网卡）。必须 Reset，
                // 否则事件一直处于已触发状态，WaitAny 立刻返回 → 采样线程空转把 CPU 吃满。
                if (WaitHandle.WaitAny(waits, _intervalMs) == 1) _wake.Reset();
            }
        }

        void Publish(double down, double up, bool linkUp, int active, ulong link)
        {
            NetSample s;
            lock (_sync)
            {
                s = _sample;
                s.Down = down;
                s.Up = up;
                s.LinkUp = linkUp;
                s.AdapterCount = active;
                s.LinkSpeed = link;
                _sample = s;
            }

            var h = Tick;
            if (h != null)
            {
                try { h(); } catch { }
            }
        }

        // ---------------- 延迟 ----------------

        void PingLoop()
        {
            Perf.Name("ping");
            var waits = new WaitHandle[] { _stop, _pingWake };
            byte[] payload = new byte[32];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(0x42 + i);
            byte[] reply = new byte[256];

            while (!_stop.WaitOne(0))
            {
                if (!_pingEnabled || _pingAddr == 0)
                {
                    if (WaitHandle.WaitAny(waits, 500) == 1) _pingWake.Reset();
                    continue;
                }

                int rtt = -1;
                if (IcmpHandle == IntPtr.Zero) IcmpHandle = Native.IcmpCreateFile();
                if (IcmpHandle != IntPtr.Zero && IcmpHandle != new IntPtr(-1))
                {
                    uint n = Native.IcmpSendEcho(IcmpHandle, _pingAddr, payload, (ushort)payload.Length,
                        IntPtr.Zero, reply, (uint)reply.Length, 600);
                    if (n > 0)
                    {
                        var r = (Native.ICMP_ECHO_REPLY)Marshal.PtrToStructure(
                            Marshal.UnsafeAddrOfPinnedArrayElement(reply, 0), typeof(Native.ICMP_ECHO_REPLY));
                        if (r.Status == 0) rtt = (int)r.RoundTripTime;
                    }
                }

                PushRtt(rtt);
                if (WaitHandle.WaitAny(waits, _pingMs) == 1) _pingWake.Reset();
            }

            if (IcmpHandle != IntPtr.Zero && IcmpHandle != new IntPtr(-1))
                Native.IcmpCloseHandle(IcmpHandle);
            IcmpHandle = IntPtr.Zero;
        }

        void PushRtt(int rtt)
        {
            int drops = 0, sum = 0, valid = 0;
            double jitter = 0;

            lock (_rttRing)
            {
                _rttRing[_rttPos] = rtt;
                _rttPos = (_rttPos + 1) % PingWindow;
                if (_rttCount < PingWindow) _rttCount++;

                int prev = -1;
                double jsum = 0;
                int jcount = 0;

                for (int k = 0; k < _rttCount; k++)
                {
                    int v = _rttRing[k];
                    if (v < 0) { drops++; prev = -1; continue; }
                    sum += v; valid++;
                    if (prev >= 0) { jsum += Math.Abs(v - prev); jcount++; }
                    prev = v;
                }

                if (jcount > 0) jitter = jsum / jcount;
            }

            lock (_sync)
            {
                var s = _sample;
                s.Rtt = rtt;
                s.Jitter = jitter;
                s.Loss = _rttCount > 0 ? drops * 100.0 / _rttCount : 0;
                _sample = s;
            }
        }

        void ClearRtt()
        {
            lock (_rttRing) { _rttCount = 0; _rttPos = 0; Array.Clear(_rttRing, 0, PingWindow); }
            lock (_sync) { var s = _sample; s.Rtt = -1; s.Jitter = 0; s.Loss = 0; _sample = s; }
        }

        static uint Resolve(string host)
        {
            try
            {
                IPAddress ip;
                if (!IPAddress.TryParse(host, out ip))
                {
                    var addrs = Dns.GetHostAddresses(host);
                    foreach (var a in addrs)
                        if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { ip = a; break; }
                }
                if (ip == null) return 0;
                byte[] b = ip.GetAddressBytes();
                if (b.Length != 4) return 0;
                return BitConverter.ToUInt32(b, 0);
            }
            catch { return 0; }
        }
    }
}
