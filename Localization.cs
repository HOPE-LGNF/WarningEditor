using System;

namespace WarningEditor
{
    /// <summary>
    /// 极简本地化：跟随游戏当前语言自动切中/英。
    /// 通过 Locale.GetOther 取一条已知文本，看里面有没有 CJK 字符来判断。
    /// </summary>
    internal static class L
    {
        private static bool? _zh;

        internal static void Reset() { _zh = null; }

        internal static bool Zh
        {
            get
            {
                if (_zh == null)
                {
                    try
                    {
                        var s = Locale.GetOther("discretionwarning");
                        _zh = string.IsNullOrEmpty(s) || HasCjk(s);
                    }
                    catch
                    {
                        _zh = true;
                    }
                }
                return _zh.Value;
            }
        }

        private static bool HasCjk(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 0x4E00 && c <= 0x9FFF) return true;
            }
            return false;
        }

        internal static string T(string zh, string en) => Zh ? zh : en;
    }
}
