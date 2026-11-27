using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace WarningEditor
{
    /// <summary>
    /// Windows 原生「打开 / 保存」对话框。
    /// 只在原生 Windows 上启用；Wine/Proton 下判定为不可用，调用方回退到游戏内列表。
    /// 对话框会阻塞主线程直到关闭，这与游戏自身的做法一致。
    /// </summary>
    internal static class NativeDialogs
    {
        private const int OFN_OVERWRITEPROMPT = 0x00000002;
        private const int OFN_NOCHANGEDIR = 0x00000008;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_EXPLORER = 0x00080000;

        private const int BufferChars = 2048;

        private static bool? _available;

        internal static bool Available
        {
            get
            {
                if (_available == null)
                {
                    bool win = Application.platform == RuntimePlatform.WindowsPlayer
                            || Application.platform == RuntimePlatform.WindowsEditor;
                    _available = win && !LooksLikeWine();
                }
                return _available.Value;
            }
        }

        /// <summary>Wine/Proton 会伪装成 WindowsPlayer，用文件系统与环境变量识别。</summary>
        private static bool LooksLikeWine()
        {
            try
            {
                var p = Application.persistentDataPath ?? "";
                if (p.StartsWith("Z:", StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINEPREFIX"))) return true;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH"))) return true;
                if (File.Exists("Z:\\etc\\os-release")) return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 返回 false 表示对话框没能显示（调用方应回退）；
        /// 返回 true 且 path 为 null 表示玩家取消了。
        /// </summary>
        internal static bool TryPickFile(string title, string startDir, string filter, out string path)
        {
            path = null;
            if (!Available) return false;
            try { return ShowOpen(title, startDir, filter, out path); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("原生打开对话框失败: " + e.Message);
                return false;
            }
        }

        /// <summary>同上；成功时 path 是玩家选择的完整路径。</summary>
        internal static bool TryPickSave(string title, string startDir, string defaultName, out string path)
        {
            path = null;
            if (!Available) return false;
            try { return ShowSave(title, startDir, defaultName, out path); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("原生保存对话框失败: " + e.Message);
                return false;
            }
        }

        private static bool ShowOpen(string title, string startDir, string filter, out string path)
        {
            path = null;
            IntPtr buffer = Marshal.AllocHGlobal(BufferChars * 2);
            try
            {
                WriteString(buffer, null);

                var d = NewDialog(title, startDir, filter, buffer);
                d.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER;
                d.lStructSize = Marshal.SizeOf(typeof(OpenFileName));

                if (GetOpenFileNameW(ref d))
                {
                    path = ReadString(buffer);
                    return true;
                }

                // 没有错误码 = 玩家点了取消
                return CommDlgExtendedError() == 0;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static bool ShowSave(string title, string startDir, string defaultName, out string path)
        {
            path = null;
            IntPtr buffer = Marshal.AllocHGlobal(BufferChars * 2);
            try
            {
                WriteString(buffer, defaultName);

                var d = NewDialog(title, startDir, "JSON\0*.json\0All files\0*.*\0\0", buffer);
                d.Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER;
                d.lpstrDefExt = "json";
                d.lStructSize = Marshal.SizeOf(typeof(OpenFileName));

                if (GetSaveFileNameW(ref d))
                {
                    path = ReadString(buffer);
                    return true;
                }

                return CommDlgExtendedError() == 0;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static OpenFileName NewDialog(string title, string startDir, string filter, IntPtr buffer)
        {
            return new OpenFileName
            {
                hwndOwner = GetActiveWindow(),
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = BufferChars,
                lpstrInitialDir = startDir,
                lpstrTitle = title
            };
        }

        private static void WriteString(IntPtr buffer, string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                Marshal.WriteInt16(buffer, 0, 0);
                return;
            }
            var chars = s.ToCharArray();
            Marshal.Copy(chars, 0, buffer, chars.Length);
            Marshal.WriteInt16(buffer, chars.Length * 2, 0);
        }

        private static string ReadString(IntPtr buffer) => Marshal.PtrToStringUni(buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetOpenFileNameW(ref OpenFileName dialog);

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetSaveFileNameW(ref OpenFileName dialog);

        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
    }
}
