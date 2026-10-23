using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NetPulse.Interop;

namespace NetPulse.Core
{
    /// <summary>一块网卡的静态信息（只在设置界面刷新时读取）。</summary>
    internal sealed class IfaceInfo
    {
        public uint Index;
        public string Name = "";
        public string Description = "";
        public uint Type;
        public bool Up;
        public bool HasMac;
        public ulong LinkSpeed;

        public string TypeName
        {
            get
            {
                switch (Type)
                {
                    case 6: return "有线";
                    case 71: return "无线";
                    case 243: return "蜂窝";
                    case 131: return "隧道";
                    case 24: return "回环";
                    default: return "网络";
                }
            }
        }

        public string SpeedText
        {
            get
            {
                if (LinkSpeed >= 1000000000UL) return (LinkSpeed / 1000000000UL) + " Gbps";
                if (LinkSpeed >= 1000000UL) return (LinkSpeed / 1000000UL) + " Mbps";
                return "";
            }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Name) ? Description : Name;
        }
    }

    /// <summary>读取 GetIfTable2 原始表。全部偏移由 Marshal.OffsetOf 现算，跨系统版本不会错位。</summary>
    internal static class IfTable
    {
        public const int ROW_SIZE = 1352;

        static readonly int OffIndex, OffAlias, OffDesc, OffType, OffOper, OffIn, OffOut, OffRx, OffTx, OffMacLen;

        static IfTable()
        {
            try
            {
                OffIndex = (int)Marshal.OffsetOf(typeof(Native.MIB_IF_ROW2), "InterfaceIndex");
                OffAlias = (int)Marshal.OffsetOf(typeof(Native.MIB_IF_ROW2), "Alias");
                OffDesc = (int)Marshal.OffsetOf(typeof(Native.MIB_IF_ROW2), "Description");
                OffType = (int)Marshal.OffsetOf(typeof(Native.MIB_IF_ROW2), "Type");
                OffOper = (int)Marshal.OffsetOf(typeof(Native.MIB_IF_ROW2), "OperStatus");
                OffIn = (int)Marshal.OffsetOf(typeof(Native.MIB_IF_ROW2), "InOctets");
                OffOut = (int)Marshal.OffsetOf(typeof(Native.MIB_IF_ROW2), "OutOctets");
                OffRx = (int)Marshal.OffsetOf(typeof(Native.MIB_IF_ROW2), "ReceiveLinkSpeed");
                OffTx = (int)Marshal.OffsetOf(typeof(Native.MIB_IF_ROW2), "TransmitLinkSpeed");
                OffMacLen = (int)Marshal.OffsetOf(typeof(Native.MIB_IF_ROW2), "PhysicalAddressLength");
            }
            catch
            {
                // 兜底：与 Win10/11 实际布局一致
                OffIndex = 8; OffAlias = 28; OffDesc = 542; OffType = 1060; OffOper = 1072;
                OffIn = 1208; OffOut = 1280; OffRx = 1192; OffTx = 1200; OffMacLen = 1080;
            }
        }

        /// <summary>打开表。返回表首地址（+8 之后是行），用完 FreeMibTable。</summary>
        public static IntPtr Open(out int count)
        {
            IntPtr table;
            count = 0;
            if (Native.GetIfTable2(out table) != 0 || table == IntPtr.Zero) return IntPtr.Zero;
            count = Marshal.ReadInt32(table);
            return table;
        }

        public static void Close(IntPtr table)
        {
            if (table != IntPtr.Zero) Native.FreeMibTable(table);
        }

        public static IntPtr Row(IntPtr table, int i)
        {
            return IntPtr.Add(table, 8 + i * ROW_SIZE);
        }

        public static uint Index(IntPtr row) { return (uint)Marshal.ReadInt32(row, OffIndex); }
        public static ulong InOctets(IntPtr row) { return (ulong)Marshal.ReadInt64(row, OffIn); }
        public static ulong OutOctets(IntPtr row) { return (ulong)Marshal.ReadInt64(row, OffOut); }
        public static ulong LinkSpeed(IntPtr row) { return (ulong)Marshal.ReadInt64(row, OffRx); }
        public static uint IfType(IntPtr row) { return (uint)Marshal.ReadInt32(row, OffType); }
        public static uint OperStatus(IntPtr row) { return (uint)Marshal.ReadInt32(row, OffOper); }
        public static uint MacLength(IntPtr row) { return (uint)Marshal.ReadInt32(row, OffMacLen); }

        public static string Alias(IntPtr row)
        {
            try { return Marshal.PtrToStringUni(IntPtr.Add(row, OffAlias)) ?? ""; }
            catch { return ""; }
        }

        public static string Desc(IntPtr row)
        {
            try { return Marshal.PtrToStringUni(IntPtr.Add(row, OffDesc)) ?? ""; }
            catch { return ""; }
        }

        static readonly string[] NoiseWords =
        {
            "qos packet scheduler", "wfp", "filter driver", "filter-0000", "-0000",
            "wan miniport", "wi-fi direct", "bluetooth", "loopback", "teredo",
            "isatap", "6to4", "kernel debug", "npcap", "vpn tunnel", "packet scheduler"
        };

        /// <summary>系统给真实网卡自动挂的过滤小端口（QoS/WFP）会重复计数，必须剔除。</summary>
        public static bool IsNoise(string alias, string desc)
        {
            for (int i = 0; i < NoiseWords.Length; i++)
            {
                if (!string.IsNullOrEmpty(alias) && alias.ToLowerInvariant().Contains(NoiseWords[i])) return true;
                if (!string.IsNullOrEmpty(desc) && desc.ToLowerInvariant().Contains(NoiseWords[i])) return true;
            }
            return false;
        }

        /// <summary>枚举所有「有用」的网卡：已启用、有 MAC 或非回环。</summary>
        public static List<IfaceInfo> List()
        {
            var list = new List<IfaceInfo>();
            int count;
            IntPtr table = Open(out count);
            if (table == IntPtr.Zero) return list;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = Row(table, i);
                    uint type = IfType(row);
                    uint oper = OperStatus(row);
                    uint macLen = MacLength(row);

                    bool up = oper == 1;
                    bool hasMac = macLen >= 6;
                    if (type == 24 || type == 131) continue;    // 回环 / 隧道
                    if (!up && !hasMac) continue;               // 未启用又没 MAC 的虚拟接口不要
                    if (IsNoise(Alias(row), Desc(row))) continue;

                    list.Add(new IfaceInfo
                    {
                        Index = Index(row),
                        Name = Alias(row),
                        Description = Desc(row),
                        Type = type,
                        Up = up,
                        HasMac = hasMac,
                        LinkSpeed = LinkSpeed(row)
                    });
                }
            }
            finally { Close(table); }

            list.Sort((a, b) =>
            {
                if (a.Up != b.Up) return a.Up ? -1 : 1;
                if (a.HasMac != b.HasMac) return a.HasMac ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
            });
            return list;
        }
    }
}
