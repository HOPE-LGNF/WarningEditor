using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WarningEditor
{
    /// <summary>
    /// 富文本颜色模型：纯文本 + 颜色区间。
    /// 区间格式 "0-5:FF0000;6-9:73B8FF"（可见字符下标，左闭右开）。
    /// 渲染时才合成 &lt;color&gt; 标签，避免编辑时切坏标签。
    /// </summary>
    internal static class RichText
    {
        public static string Build(string plain, string spec)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var cols = Parse(spec, plain.Length);
            var sb = new StringBuilder();
            Color? cur = null;
            for (int i = 0; i < plain.Length; i++)
            {
                if (!Same(cols[i], cur))
                {
                    if (cur.HasValue) sb.Append("</color>");
                    if (cols[i].HasValue)
                        sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(cols[i].Value)).Append('>');
                    cur = cols[i];
                }
                sb.Append(plain[i]);
            }
            if (cur.HasValue) sb.Append("</color>");
            return sb.ToString();
        }

        private static bool Same(Color? a, Color? b)
        {
            if (a.HasValue != b.HasValue) return false;
            if (!a.HasValue) return true;
            return a.Value == b.Value;
        }

        public static Color?[] Parse(string spec, int len)
        {
            var cols = new Color?[len];
            if (len <= 0 || string.IsNullOrEmpty(spec)) return cols;

            foreach (var part in spec.Split(';'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                var kv = part.Split(':');
                if (kv.Length != 2) continue;
                var range = kv[0].Split('-');
                if (range.Length != 2) continue;
                if (!int.TryParse(range[0], out int a)) continue;
                if (!int.TryParse(range[1], out int b)) continue;
                if (!ColorUtility.TryParseHtmlString("#" + kv[1], out var c)) continue;

                for (int i = Mathf.Max(0, a); i < Mathf.Min(len, b); i++) cols[i] = c;
            }
            return cols;
        }

        public static string Serialize(Color?[] cols)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < cols.Length)
            {
                if (!cols[i].HasValue) { i++; continue; }
                int j = i;
                while (j < cols.Length && Same(cols[j], cols[i])) j++;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(i).Append('-').Append(j).Append(':')
                  .Append(ColorUtility.ToHtmlStringRGB(cols[i].Value));
                i = j;
            }
            return sb.ToString();
        }

        public static string Apply(string plain, string spec, int start, int end, Color c)
        {
            var cols = Parse(spec, plain.Length);
            for (int i = Mathf.Max(0, start); i < Mathf.Min(end, plain.Length); i++) cols[i] = c;
            return Serialize(cols);
        }

        public static string Clamp(string spec, int len) => Serialize(Parse(spec, len));

        /// <summary>把旧的带标签文本拆成「纯文本 + 颜色区间」。</summary>
        public static void FromTagged(string raw, out string plain, out string spec)
        {
            var sb = new StringBuilder();
            var cols = new List<Color?>();
            var stack = new List<Color>();
            int i = 0;

            while (i < raw.Length)
            {
                if (raw[i] == '<')
                {
                    int close = raw.IndexOf('>', i);
                    if (close > i)
                    {
                        string tag = raw.Substring(i, close - i + 1);
                        if (tag.StartsWith("<color="))
                        {
                            string hex = tag.Substring(7).TrimEnd('>').Trim('"');
                            if (ColorUtility.TryParseHtmlString(hex, out var c)) stack.Add(c);
                            i = close + 1;
                            continue;
                        }
                        if (tag == "</color>")
                        {
                            if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                            i = close + 1;
                            continue;
                        }
                    }
                }

                sb.Append(raw[i]);
                cols.Add(stack.Count > 0 ? stack[stack.Count - 1] : (Color?)null);
                i++;
            }

            plain = sb.ToString();
            spec = Serialize(cols.ToArray());
        }
    }
}
