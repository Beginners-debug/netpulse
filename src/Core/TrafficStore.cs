using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NetPulse.Core
{
    /// <summary>
    /// 流量账本：今日上下行、历史总计、最近 30 天曲线。
    /// 存 data\traffic.json，自己写自己读，不依赖任何 JSON 库。
    /// </summary>
    internal sealed class TrafficStore
    {
        const int KeepDays = 30;

        public long DayIn, DayOut;       // 今日
        public long TotalIn, TotalOut;   // 历史总计
        public long SessionIn, SessionOut;
        public string Today = DateTime.Now.ToString("yyyy-MM-dd");

        readonly SortedDictionary<string, long[]> _days = new SortedDictionary<string, long[]>(StringComparer.Ordinal);
        readonly string _path;
        bool _dirty;

        public TrafficStore(string path)
        {
            _path = path;
            Load();
        }

        public void Add(long inBytes, long outBytes)
        {
            if (inBytes <= 0 && outBytes <= 0) return;
            RollDay();
            DayIn += inBytes;
            DayOut += outBytes;
            TotalIn += inBytes;
            TotalOut += outBytes;
            SessionIn += inBytes;
            SessionOut += outBytes;
            _dirty = true;
        }

        /// <summary>今日流量清零（历史总计保留）。</summary>
        public void ResetToday()
        {
            DayIn = 0;
            DayOut = 0;
            SessionIn = 0;
            SessionOut = 0;
            _dirty = true;
        }

        /// <summary>清空最近 30 天的曲线数据。</summary>
        public void ClearHistory()
        {
            _days.Clear();
            _dirty = true;
        }

        void RollDay()
        {
            string now = DateTime.Now.ToString("yyyy-MM-dd");
            if (now == Today) return;

            _days[Today] = new[] { DayIn, DayOut };
            Today = now;
            DayIn = 0;
            DayOut = 0;
            Trim();
            _dirty = true;
            Save();
        }

        void Trim()
        {
            while (_days.Count > KeepDays)
            {
                foreach (var k in _days.Keys) { _days.Remove(k); break; }
            }
        }

        /// <summary>最近 N 天的上下行，从旧到新。</summary>
        public List<long[]> Recent(int n)
        {
            var list = new List<long[]>();
            var keys = new List<string>(_days.Keys);
            for (int i = Math.Max(0, keys.Count - n); i < keys.Count; i++)
                list.Add(_days[keys[i]]);
            list.Add(new[] { DayIn, DayOut });
            return list;
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                string text = File.ReadAllText(_path, Encoding.UTF8);
                Today = Str(text, "today", Today);
                DayIn = Num(text, "dayIn");
                DayOut = Num(text, "dayOut");
                TotalIn = Num(text, "totalIn");
                TotalOut = Num(text, "totalOut");

                int i = text.IndexOf("\"days\"", StringComparison.Ordinal);
                if (i < 0) return;
                int open = text.IndexOf('{', i), close = text.LastIndexOf('}');
                if (open < 0 || close <= open) return;

                foreach (string pair in text.Substring(open + 1, close - open - 1).Split(','))
                {
                    int q1 = pair.IndexOf('"');
                    int q2 = pair.IndexOf('"', q1 + 1);
                    int b = pair.IndexOf('[', q2);
                    int e = pair.IndexOf(']', b);
                    if (q1 < 0 || q2 < 0 || b < 0 || e < 0) continue;
                    string key = pair.Substring(q1 + 1, q2 - q1 - 1);
                    string[] parts = pair.Substring(b + 1, e - b - 1).Split(':');
                    if (parts.Length != 2) continue;
                    long a, c;
                    if (long.TryParse(parts[0], out a) && long.TryParse(parts[1], out c))
                        _days[key] = new[] { a, c };
                }
            }
            catch { }
        }

        public void Save()
        {
            if (!_dirty) return;
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder(1024);
                sb.Append("{\n");
                sb.Append("  \"v\": 2,\n");
                sb.Append("  \"today\": \"").Append(Today).Append("\",\n");
                sb.Append("  \"dayIn\": ").Append(DayIn).Append(",\n");
                sb.Append("  \"dayOut\": ").Append(DayOut).Append(",\n");
                sb.Append("  \"totalIn\": ").Append(TotalIn).Append(",\n");
                sb.Append("  \"totalOut\": ").Append(TotalOut).Append(",\n");
                sb.Append("  \"days\": {");
                bool first = true;
                foreach (var kv in _days)
                {
                    if (!first) sb.Append(',');
                    sb.Append("\n    \"").Append(kv.Key).Append("\": [").Append(kv.Value[0]).Append(':').Append(kv.Value[1]).Append(']');
                    first = false;
                }
                sb.Append("\n  }\n}\n");

                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
                _dirty = false;
            }
            catch { }
        }

        static long Num(string text, string key)
        {
            int i = text.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (i < 0) return 0;
            i = text.IndexOf(':', i);
            if (i < 0) return 0;
            long v = 0;
            bool any = false;
            for (int p = i + 1; p < text.Length; p++)
            {
                char c = text[p];
                if (c >= '0' && c <= '9') { v = v * 10 + (c - '0'); any = true; }
                else if (any) break;
                else if (c == '-' || c == ' ') continue;
            }
            return any ? v : 0;
        }

        static string Str(string text, string key, string fallback)
        {
            int i = text.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (i < 0) return fallback;
            int a = text.IndexOf('"', text.IndexOf(':', i) + 1);
            int b = text.IndexOf('"', a + 1);
            if (a < 0 || b < 0) return fallback;
            return text.Substring(a + 1, b - a - 1);
        }
    }
}
