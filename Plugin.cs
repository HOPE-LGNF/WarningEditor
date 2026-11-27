using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WarningEditor
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.hope.cu.warningeditor";
        public const string PluginName = "Warning Editor";
        public const string PluginVersion = "1.10.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<int> CfgCount;
        internal static ConfigEntry<int> CfgTextCount;
        internal static ConfigEntry<string> CfgTextOverride;
        internal static ConfigEntry<string> CfgHotkeyKey;
        internal static ConfigEntry<bool> CfgHotkeyCtrl;
        internal static ConfigEntry<bool> CfgHotkeyShift;
        internal static ConfigEntry<bool> CfgHotkeyAlt;

        private static readonly Dictionary<int, ConfigEntry<string>> _txtText =
            new Dictionary<int, ConfigEntry<string>>();
        private static readonly Dictionary<int, ConfigEntry<string>> _txtColor =
            new Dictionary<int, ConfigEntry<string>>();
        private static readonly Dictionary<int, ConfigEntry<string>> _txtVAlign =
            new Dictionary<int, ConfigEntry<string>>();
        private static readonly Dictionary<int, ConfigEntry<string>> _txtColors =
            new Dictionary<int, ConfigEntry<string>>();
        private static readonly Dictionary<int, ConfigEntry<string>> _txtAlign =
            new Dictionary<int, ConfigEntry<string>>();
        private static readonly Dictionary<int, Dictionary<string, ConfigEntry<float>>> _txtRects =
            new Dictionary<int, Dictionary<string, ConfigEntry<float>>>();

        internal static readonly Dictionary<string, ConfigEntry<float>> Fixed =
            new Dictionary<string, ConfigEntry<float>>();

        private static readonly Dictionary<int, ConfigEntry<string>> _imgNames =
            new Dictionary<int, ConfigEntry<string>>();
        private static readonly Dictionary<int, Dictionary<string, ConfigEntry<float>>> _imgRects =
            new Dictionary<int, Dictionary<string, ConfigEntry<float>>>();

        // 出厂值：用户导出并确认的那份 JSON
        internal static readonly Dictionary<string, Vector4> Defaults = new Dictionary<string, Vector4>
        {
            { "Text", new Vector4(0f,   -313f, 1782f,  616f) },
            { "Logo", new Vector4(0f,   883.9f, 453.6f, 272.2f) },
        };
        internal static readonly Vector4 DefaultImg0 = new Vector4(0f, -390f, 407.4f, 300f);

        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private static Sprite _embedded;

        /// <summary>从旧插件（com.hope.cu.warningexperiment）迁移配置。</summary>
        static Plugin()
        {
            try
            {
                string dir = BepInEx.Paths.ConfigPath;
                string newPath = System.IO.Path.Combine(dir, PluginGuid + ".cfg");
                string oldPath = System.IO.Path.Combine(dir, "com.hope.cu.warningexperiment.cfg");
                if (!System.IO.File.Exists(newPath) && System.IO.File.Exists(oldPath))
                {
                    System.IO.File.Copy(oldPath, newPath, false);
                }
            }
            catch { }
        }

        internal static string ImagesDir
        {
            get
            {
                string dll = Assembly.GetExecutingAssembly().Location;
                return Path.Combine(Path.GetDirectoryName(dll) ?? ".", "images");
            }
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            CfgEnabled = Config.Bind("General", "Enabled", true, "启用警告页布局调整");
            CfgCount = Config.Bind("Elements", "Count", 1, "图片元素数量");
            if (CfgCount.Value < 0) CfgCount.Value = 0;

            foreach (var id in new[] { "Text", "Logo" })
            {
                var d = Defaults[id];
                Fixed[id + ".X"] = Config.Bind(id, "X", d.x, "anchoredPosition.x");
                Fixed[id + ".Y"] = Config.Bind(id, "Y", d.y, "anchoredPosition.y");
                Fixed[id + ".W"] = Config.Bind(id, "W", d.z, "sizeDelta.x");
                Fixed[id + ".H"] = Config.Bind(id, "H", d.w, "sizeDelta.y");
            }

            CfgHotkeyKey = Config.Bind("Hotkey", "Key", "F8",
                "打开编辑器的按键（KeyCode 名称，例如 F8 / F10 / None 禁用）");
            CfgHotkeyCtrl = Config.Bind("Hotkey", "Ctrl", true, "是否要求按住 Ctrl");
            CfgHotkeyShift = Config.Bind("Hotkey", "Shift", true, "是否要求按住 Shift");
            CfgHotkeyAlt = Config.Bind("Hotkey", "Alt", false, "是否要求按住 Alt");

            CfgTextOverride = Config.Bind("Text", "Override", "", "覆盖警告文本内容（留空则用游戏原文）");
            CfgTextCount = Config.Bind("TextBoxes", "Count", 0, "文本框数量");
            if (CfgTextCount.Value < 0) CfgTextCount.Value = 0;

            GetImgName(0);
            GetImgRect(0);

            Harmony.CreateAndPatchAll(typeof(PreRunPatch), PluginGuid);
            Harmony.CreateAndPatchAll(typeof(SettingsRowPatch), PluginGuid);
            Log.LogInfo("WarningEditor 1.10.0 loaded. images=" + ImagesDir);
            Log.LogInfo($"热键: {(CfgHotkeyCtrl.Value ? "Ctrl+" : "")}{(CfgHotkeyAlt.Value ? "Alt+" : "")}{(CfgHotkeyShift.Value ? "Shift+" : "")}{CfgHotkeyKey.Value}");
        }

        internal static Vector4 GetFixed(string id)
        {
            return new Vector4(Fixed[id + ".X"].Value, Fixed[id + ".Y"].Value,
                               Fixed[id + ".W"].Value, Fixed[id + ".H"].Value);
        }

        internal static void SetFixed(string id, Vector4 v)
        {
            Fixed[id + ".X"].Value = v.x;
            Fixed[id + ".Y"].Value = v.y;
            Fixed[id + ".W"].Value = v.z;
            Fixed[id + ".H"].Value = v.w;
        }

        internal static ConfigEntry<string> GetImgName(int i)
        {
            if (!_imgNames.TryGetValue(i, out var e))
            {
                e = Instance.Config.Bind("Element" + i, "Image", "warning_experiment.png",
                                         "图片文件名（images 文件夹内）");
                _imgNames[i] = e;
            }
            return e;
        }

        internal static Vector4 GetImgRect(int i)
        {
            if (!_imgRects.TryGetValue(i, out var d))
            {
                d = new Dictionary<string, ConfigEntry<float>>();
                var def = i == 0 ? DefaultImg0 : new Vector4(0f, -120f - i * 40f, 400f, 300f);
                d["X"] = Instance.Config.Bind("Element" + i, "X", def.x, "anchoredPosition.x");
                d["Y"] = Instance.Config.Bind("Element" + i, "Y", def.y, "anchoredPosition.y");
                d["W"] = Instance.Config.Bind("Element" + i, "W", def.z, "sizeDelta.x");
                d["H"] = Instance.Config.Bind("Element" + i, "H", def.w, "sizeDelta.y");
                _imgRects[i] = d;
            }
            return new Vector4(d["X"].Value, d["Y"].Value, d["W"].Value, d["H"].Value);
        }

        internal static void SetImgRect(int i, Vector4 v)
        {
            GetImgRect(i);
            var d = _imgRects[i];
            d["X"].Value = v.x; d["Y"].Value = v.y; d["W"].Value = v.z; d["H"].Value = v.w;
        }

        internal static void SaveConfig() => Instance.Config.Save();

        // ---------------- 布局导入 / 导出 ----------------
        internal static string LayoutDir => Path.Combine(BepInEx.Paths.ConfigPath, "WarningEditor");
        internal static string LayoutPath => Path.Combine(LayoutDir, "layout.json");

        internal static string ExportLayout()
        {
            var d = new LayoutDto();
            d.images = CfgCount.Value;
            for (int i = 0; i < d.images; i++)
            {
                var r = GetImgRect(i);
                d.img.Add(new ImgDto
                {
                    name = GetImgName(i).Value,
                    x = r.x, y = r.y, w = r.z, h = r.w,
                    fps = EF(i, "Fps", 12f, "").Value,
                    speed = EF(i, "Speed", 1f, "").Value,
                    loop = ES(i, "Loop", "loop", "").Value,
                    loopCount = EI(i, "LoopCount", 0, "").Value,
                    pingSkip = EB(i, "PingSkip", true, "").Value
                });
            }
            d.texts = CfgTextCount.Value;
            for (int i = 0; i < d.texts; i++)
            {
                var r = GetTxtRect(i);
                d.txt.Add(new TxtDto
                {
                    text = GetTxtText(i).Value,
                    colors = GetTxtColors(i).Value,
                    align = GetTxtAlign(i).Value,
                    valign = GetTxtVAlign(i).Value,
                    font = GetTxtRectDict(i)["Font"].Value,
                    x = r.x, y = r.y, w = r.z, h = r.w
                });
            }
            var tr = GetFixed("Text");
            d.textRect = new RectDto { x = tr.x, y = tr.y, w = tr.z, h = tr.w };
            var lr = GetFixed("Logo");
            d.logoRect = new RectDto { x = lr.x, y = lr.y, w = lr.z, h = lr.w };
            d.textOverride = CfgTextOverride.Value;

            Directory.CreateDirectory(LayoutDir);
            File.WriteAllText(LayoutPath, JsonUtility.ToJson(d, true));
            Log.LogInfo("布局已导出: " + LayoutPath);
            return LayoutPath;
        }

        internal static bool ImportLayout(Transform warning, out string error)
        {
            error = null;
            if (!File.Exists(LayoutPath)) { error = "找不到 " + LayoutPath; return false; }

            LayoutDto d;
            try { d = JsonUtility.FromJson<LayoutDto>(File.ReadAllText(LayoutPath)); }
            catch (Exception e) { error = "JSON 解析失败: " + e.Message; return false; }
            if (d == null) { error = "JSON 解析失败"; return false; }

            CfgCount.Value = Mathf.Max(0, d.images);
            for (int i = 0; i < CfgCount.Value; i++)
            {
                ImgDto it = i < d.img.Count ? d.img[i] : null;
                if (it == null) continue;
                GetImgName(i).Value = it.name ?? "";
                SetImgRect(i, new Vector4(it.x, it.y, it.w, it.h));
                EF(i, "Fps", 12f, "").Value = it.fps > 0f ? it.fps : 12f;
                EF(i, "Speed", 1f, "").Value = it.speed > 0f ? it.speed : 1f;
                ES(i, "Loop", "loop", "").Value = string.IsNullOrEmpty(it.loop) ? "loop" : it.loop;
                EI(i, "LoopCount", 0, "").Value = it.loopCount;
                EB(i, "PingSkip", true, "").Value = it.pingSkip;
            }

            CfgTextCount.Value = Mathf.Max(0, d.texts);
            for (int i = 0; i < CfgTextCount.Value; i++)
            {
                TxtDto it = i < d.txt.Count ? d.txt[i] : null;
                if (it == null) continue;
                GetTxtText(i).Value = it.text ?? "";
                GetTxtColors(i).Value = it.colors ?? "";
                GetTxtAlign(i).Value = string.IsNullOrEmpty(it.align) ? "center" : it.align;
                GetTxtVAlign(i).Value = string.IsNullOrEmpty(it.valign) ? "middle" : it.valign;
                GetTxtRectDict(i)["Font"].Value = it.font > 0f ? it.font : 24f;
                SetTxtRect(i, new Vector4(it.x, it.y, it.w, it.h));
            }

            if (d.textRect != null) SetFixed("Text", new Vector4(d.textRect.x, d.textRect.y, d.textRect.w, d.textRect.h));
            if (d.logoRect != null) SetFixed("Logo", new Vector4(d.logoRect.x, d.logoRect.y, d.logoRect.w, d.logoRect.h));
            CfgTextOverride.Value = d.textOverride ?? "";

            SyncImages(warning);
            SyncTexts(warning);
            ApplyTextOverride(warning);
            Log.LogInfo("布局已导入: " + LayoutPath);
            return true;
        }

        // ---------------- 元素扩展配置 ----------------
        private static readonly Dictionary<string, ConfigEntry<float>> _ef = new Dictionary<string, ConfigEntry<float>>();
        private static readonly Dictionary<string, ConfigEntry<int>> _ei = new Dictionary<string, ConfigEntry<int>>();
        private static readonly Dictionary<string, ConfigEntry<bool>> _eb = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly Dictionary<string, ConfigEntry<string>> _es = new Dictionary<string, ConfigEntry<string>>();

        internal static ConfigEntry<float> EF(int i, string key, float def, string desc)
        {
            string k = i + "/" + key;
            if (!_ef.TryGetValue(k, out var e)) { e = Instance.Config.Bind("Element" + i, key, def, desc); _ef[k] = e; }
            return e;
        }

        internal static ConfigEntry<int> EI(int i, string key, int def, string desc)
        {
            string k = i + "/" + key;
            if (!_ei.TryGetValue(k, out var e)) { e = Instance.Config.Bind("Element" + i, key, def, desc); _ei[k] = e; }
            return e;
        }

        internal static ConfigEntry<bool> EB(int i, string key, bool def, string desc)
        {
            string k = i + "/" + key;
            if (!_eb.TryGetValue(k, out var e)) { e = Instance.Config.Bind("Element" + i, key, def, desc); _eb[k] = e; }
            return e;
        }

        internal static ConfigEntry<string> ES(int i, string key, string def, string desc)
        {
            string k = i + "/" + key;
            if (!_es.TryGetValue(k, out var e)) { e = Instance.Config.Bind("Element" + i, key, def, desc); _es[k] = e; }
            return e;
        }

        /// <summary>把 name_0.png / name_1.png … 识别成序列，按编号排序加载。</summary>
        internal static Sprite[] LoadSequence(string firstName, out string[] files)
        {
            files = null;
            if (string.IsNullOrEmpty(firstName)) return null;

            var baseName = RelBase(firstName);
            var m = Regex.Match(baseName, @"^(.*)[ _\-.](\d+)$");
            if (!m.Success) return null;
            string prefix = m.Groups[1].Value;
            if (string.IsNullOrEmpty(prefix)) return null;

            var hits = new List<KeyValuePair<int, string>>();
            foreach (var f in ListImages())
            {
                var bn = RelBase(f);
                var mm = Regex.Match(bn, @"^(.*)[ _\-.](\d+)$");
                if (!mm.Success) continue;
                if (mm.Groups[1].Value != prefix) continue;
                if (!int.TryParse(mm.Groups[2].Value, out int n)) continue;
                hits.Add(new KeyValuePair<int, string>(n, f));
            }
            if (hits.Count < 2) return null;

            hits.Sort((a, b) => a.Key.CompareTo(b.Key));
            files = new string[hits.Count];
            var sprites = new Sprite[hits.Count];
            for (int i = 0; i < hits.Count; i++)
            {
                files[i] = hits[i].Value;
                sprites[i] = LoadSprite(hits[i].Value);
                if (sprites[i] == null) return null;
            }
            Log.LogInfo($"序列: {prefix} 共 {hits.Count} 帧");
            return sprites;
        }

        // ---------------- 文本框 ----------------
        internal static ConfigEntry<string> GetTxtText(int i)
        {
            if (!_txtText.TryGetValue(i, out var e))
            {
                e = Instance.Config.Bind("TextBox" + i, "Text", "文本", "文本框内容（纯文本）");
                _txtText[i] = e;
            }

            var raw = e.Value ?? "";
            if (raw.IndexOf("<color", System.StringComparison.Ordinal) >= 0)
            {
                RichText.FromTagged(raw, out var plain, out var spec);
                e.Value = plain;
                GetTxtColors(i).Value = spec;
                Log.LogInfo($"文本框{i} 已从富文本迁移：'{plain}' 颜色={spec}");
            }
            return e;
        }

        internal static ConfigEntry<string> GetTxtColor(int i)
        {
            if (!_txtColor.TryGetValue(i, out var e))
            {
                e = Instance.Config.Bind("TextBox" + i, "Color", "#FFFFFF", "文字颜色");
                _txtColor[i] = e;
            }
            return e;
        }

        internal static ConfigEntry<string> GetTxtColors(int i)
        {
            if (!_txtColors.TryGetValue(i, out var e))
            {
                e = Instance.Config.Bind("TextBox" + i, "Colors", "", "颜色区间 0-5:FF0000;6-9:73B8FF");
                _txtColors[i] = e;
            }
            return e;
        }

        internal static ConfigEntry<string> GetTxtVAlign(int i)
        {
            if (!_txtVAlign.TryGetValue(i, out var e))
            {
                e = Instance.Config.Bind("TextBox" + i, "VAlign", "middle", "纵向对齐 top/middle/bottom");
                _txtVAlign[i] = e;
            }
            return e;
        }

        internal static ConfigEntry<string> GetTxtAlign(int i)
        {
            if (!_txtAlign.TryGetValue(i, out var e))
            {
                e = Instance.Config.Bind("TextBox" + i, "Align", "center", "对齐 left/center/right");
                _txtAlign[i] = e;
            }
            return e;
        }

        internal static Dictionary<string, ConfigEntry<float>> GetTxtRectDict(int i)
        {
            if (!_txtRects.TryGetValue(i, out var d))
            {
                d = new Dictionary<string, ConfigEntry<float>>();
                var def = new Vector4(0f, -460f - i * 40f, 800f, 120f);
                d["X"] = Instance.Config.Bind("TextBox" + i, "X", def.x, "anchoredPosition.x");
                d["Y"] = Instance.Config.Bind("TextBox" + i, "Y", def.y, "anchoredPosition.y");
                if (d["Y"].Value > 0)
                {
                    Log.LogInfo($"文本框{i} Y={d["Y"].Value} 为旧版正值，已转为 -{d["Y"].Value}");
                    d["Y"].Value = -d["Y"].Value;
                }
                d["W"] = Instance.Config.Bind("TextBox" + i, "W", def.z, "sizeDelta.x");
                d["H"] = Instance.Config.Bind("TextBox" + i, "H", def.w, "sizeDelta.y");
                d["Font"] = Instance.Config.Bind("TextBox" + i, "FontSize", 24f, "字号");
                _txtRects[i] = d;
            }
            return d;
        }

        internal static Vector4 GetTxtRect(int i)
        {
            var d = GetTxtRectDict(i);
            return new Vector4(d["X"].Value, d["Y"].Value, d["W"].Value, d["H"].Value);
        }

        internal static void SetTxtRect(int i, Vector4 v)
        {
            var d = GetTxtRectDict(i);
            d["X"].Value = v.x; d["Y"].Value = v.y; d["W"].Value = v.z; d["H"].Value = v.w;
        }

        internal static void SyncTexts(Transform warning)
        {
            int n = Mathf.Max(0, CfgTextCount.Value);
            var src = warning.Find("Text (TMP)");

            for (int i = 0; i < n; i++)
            {
                var name = "WarningTxt" + i;
                var t = warning.Find(name);
                GameObject go;
                if (t == null)
                {
                    if (src == null) break;
                    go = UnityEngine.Object.Instantiate(src.gameObject, warning);
                    go.name = name;
                    var loc = go.GetComponent<UILocalizer>();
                    if (loc != null) UnityEngine.Object.Destroy(loc);
                    go.transform.SetAsLastSibling();
                }
                else go = t.gameObject;

                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.localScale = Vector3.one;

                var tmp = go.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.text = RichText.Build(GetTxtText(i).Value, GetTxtColors(i).Value);
                    tmp.fontSize = GetTxtRectDict(i)["Font"].Value;
                    if (ColorUtility.TryParseHtmlString(GetTxtColor(i).Value, out var c)) tmp.color = c;
                    tmp.alignment = ComposeAlign(GetTxtAlign(i).Value, GetTxtVAlign(i).Value);
                    tmp.raycastTarget = false;
                    tmp.enableWordWrapping = true;
                }

                var r = GetTxtRect(i);
                rt.anchoredPosition = new Vector2(r.x, r.y);
                rt.sizeDelta = new Vector2(r.z, r.w);

                if (tmp != null)
                {
                    go.SetActive(true);
                    tmp.enabled = true;
                    Log.LogInfo($"文本框{i} text='{tmp.text}' pos=({r.x:0},{r.y:0}) size=({r.z:0}x{r.w:0}) color={tmp.color} font={(tmp.font != null ? tmp.font.name : "null")} active={go.activeInHierarchy}");
                }

                go.transform.SetAsLastSibling();
            }

            for (int i = n; ; i++)
            {
                var t = warning.Find("WarningTxt" + i);
                if (t == null) break;
                UnityEngine.Object.Destroy(t.gameObject);
            }
        }

        internal static TextAlignmentOptions ComposeAlign(string h, string v)
        {
            bool top = v == "top", bottom = v == "bottom";
            if (h == "left") return top ? TextAlignmentOptions.TopLeft : bottom ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.Left;
            if (h == "right") return top ? TextAlignmentOptions.TopRight : bottom ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.Right;
            return top ? TextAlignmentOptions.Top : bottom ? TextAlignmentOptions.Bottom : TextAlignmentOptions.Center;
        }

        internal static TextAlignmentOptions ParseAlign(string s)
        {
            switch (s)
            {
                case "left": return TextAlignmentOptions.Left;
                case "right": return TextAlignmentOptions.Right;
                default: return TextAlignmentOptions.Center;
            }
        }

        // ---------------- 图片 ----------------
        internal static string[] ListImages()
        {
            try
            {
                if (!Directory.Exists(ImagesDir)) return new[] { "warning_experiment.png" };
                var files = Directory.GetFiles(ImagesDir, "*.*", SearchOption.AllDirectories);
                var list = new List<string>();
                foreach (var f in files)
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;

                    // 保留相对路径，支持 images/子文件夹/xxx.png
                    var rel = f.Substring(ImagesDir.Length)
                               .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                               .Replace('\\', '/');
                    list.Add(rel);
                }
                list.Sort(StringComparer.OrdinalIgnoreCase);
                if (list.Count == 0) list.Add("warning_experiment.png");
                return list.ToArray();
            }
            catch (Exception e)
            {
                Log.LogError("扫描图片失败: " + e.Message);
                return new[] { "warning_experiment.png" };
            }
        }

        private static string RelBase(string rel)
        {
            int dot = rel.LastIndexOf('.');
            return dot > 0 ? rel.Substring(0, dot) : rel;
        }

        internal static void ClearImageCache()
        {
            _cache.Clear();
        }

        internal static Sprite LoadSprite(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "warning_experiment.png";
            if (_cache.TryGetValue(name, out var cached) && cached != null) return cached;

            Sprite sp = null;
            try
            {
                var path = Path.Combine(ImagesDir, name);
                if (File.Exists(path))
                {
                    var bytes = File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes))
                    {
                        tex.filterMode = FilterMode.Point;
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.hideFlags = HideFlags.DontSave;
                        sp = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                           new Vector2(0.5f, 0.5f), 100f);
                        sp.hideFlags = HideFlags.DontSave;
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogError($"加载图片 {name} 失败: {e.Message}");
            }

            if (sp == null) sp = GetEmbedded();
            _cache[name] = sp;
            return sp;
        }

        internal static Sprite GetEmbedded()
        {
            if (_embedded != null) return _embedded;

            var asm = Assembly.GetExecutingAssembly();
            string res = null;
            foreach (var n in asm.GetManifestResourceNames())
                if (n.EndsWith("warning_experiment.png")) { res = n; break; }
            if (res == null) return null;

            using (var s = asm.GetManifestResourceStream(res))
            {
                if (s == null) return null;
                var bytes = new byte[s.Length];
                s.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes)) return null;
                tex.filterMode = FilterMode.Point;
                tex.hideFlags = HideFlags.DontSave;
                _embedded = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                          new Vector2(0.5f, 0.5f), 100f);
                _embedded.hideFlags = HideFlags.DontSave;
                return _embedded;
            }
        }

        // ---------------- 应用 ----------------
        internal static void ApplyLayout(Transform warning)
        {
            if (warning == null) return;
            var textT = warning.Find("Text (TMP)");
            ApplyFixed(textT, "Text");
            ApplyTextOverride(warning);
            ApplyFixed(warning.Find("Cas"), "Logo");
            SyncImages(warning);
            SyncTexts(warning);
        }

        /// <summary>当前语言下的游戏原文（不缓存，跟随语言切换）。</summary>
        internal static string LocalizedWarningText()
        {
            try
            {
                var s = Locale.GetOther("discretionwarning");
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch { }
            return null;
        }

        internal static bool HasTextOverride => !string.IsNullOrEmpty(CfgTextOverride.Value);

        internal static void ApplyTextOverride(Transform warning)
        {
            if (warning == null) return;
            var t = warning.Find("Text (TMP)");
            if (t == null) return;
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp == null) return;

            if (HasTextOverride)
            {
                tmp.text = CfgTextOverride.Value;
                return;
            }

            // 没有自定义时，按当前语言重新取一次游戏原文，避免被上一次的语言钉死
            var loc = LocalizedWarningText();
            if (loc != null) tmp.text = loc;
        }

        private static void ApplyFixed(Transform t, string id)
        {
            if (!(t is RectTransform rt)) return;
            var v = GetFixed(id);
            rt.anchoredPosition = new Vector2(v.x, v.y);
            rt.sizeDelta = new Vector2(v.z, v.w);
        }

        /// <summary>按当前图片名配置动画播放器（序列帧）或退回静态图。</summary>
        internal static void ConfigureAnim(int i, GameObject go)
        {
            if (go == null) return;
            var img = go.GetComponent<Image>();
            var ap = go.GetComponent<AnimPlayer>();
            var seq = LoadSequence(GetImgName(i).Value, out var _);

            if (seq != null && seq.Length > 1)
            {
                if (ap == null) ap = go.AddComponent<AnimPlayer>();
                ap.Frames = seq;
                ap.Fps = EF(i, "Fps", 12f, "序列播放帧率").Value;
                ap.Speed = EF(i, "Speed", 1f, "速度倍率").Value;
                ap.LoopMode = ES(i, "Loop", "loop", "循环模式 loop/pingpong/once").Value;
                ap.LoopCount = EI(i, "LoopCount", 0, "循环次数 0=无限").Value;
                ap.PingSkip = EB(i, "PingSkip", true, "往返时跳过重复端点").Value;
                ap.ResetPlayback();
                if (img != null) img.sprite = seq[0];
            }
            else
            {
                if (ap != null) ap.Frames = null;
                if (img != null) img.sprite = LoadSprite(GetImgName(i).Value);
            }
        }

        internal static void SyncImages(Transform warning)
        {
            int n = Mathf.Max(0, CfgCount.Value);

            for (int i = 0; i < n; i++)
            {
                var name = "WarningImg" + i;
                var t = warning.Find(name);
                GameObject go;
                if (t == null)
                {
                    go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    go.transform.SetParent(warning, false);
                    go.transform.SetAsLastSibling();
                }
                else go = t.gameObject;

                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.localScale = Vector3.one;

                var img = go.GetComponent<Image>();
                var sp = LoadSprite(GetImgName(i).Value);
                if (sp != null) img.sprite = sp;
                img.preserveAspect = true;
                img.raycastTarget = false;

                var r = GetImgRect(i);
                rt.anchoredPosition = new Vector2(r.x, r.y);
                rt.sizeDelta = new Vector2(r.z, r.w);

                ConfigureAnim(i, go);
            }

            for (int i = n; ; i++)
            {
                var t = warning.Find("WarningImg" + i);
                if (t == null) break;
                UnityEngine.Object.Destroy(t.gameObject);
            }
        }
    }

    // ================= 热键兜底 =================
    public class WarningEditorHotkey : MonoBehaviour
    {
        private void Update()
        {
            var keyStr = Plugin.CfgHotkeyKey.Value;
            if (string.IsNullOrWhiteSpace(keyStr) ||
                keyStr.Equals("none", System.StringComparison.OrdinalIgnoreCase)) return;
            if (!System.Enum.TryParse<KeyCode>(keyStr, true, out var key)) return;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            // 精确匹配修饰键，避免"按住 Ctrl 时误触"或与别的 mod 抢同一个裸键
            if (ctrl != Plugin.CfgHotkeyCtrl.Value) return;
            if (shift != Plugin.CfgHotkeyShift.Value) return;
            if (alt != Plugin.CfgHotkeyAlt.Value) return;

            if (!Input.GetKeyDown(key)) return;
            if (SettingsMenu.instance != null) return;
            var pre = PreRunScript.instance;
            if (pre == null || pre.contentWarning == null) return;
            if (LayoutEditor.IsOpen) return;
            Plugin.Log.LogInfo("热键: 打开布局编辑器");
            LayoutEditor.Open(pre.contentWarning.transform);
        }
    }

    [System.Serializable]
    public class LayoutDto
    {
        public int version = 1;
        public int images;
        public List<ImgDto> img = new List<ImgDto>();
        public int texts;
        public List<TxtDto> txt = new List<TxtDto>();
        public RectDto textRect;
        public RectDto logoRect;
        public string textOverride = "";
    }

    [System.Serializable]
    public class ImgDto
    {
        public string name = "";
        public float x, y, w, h;
        public float fps = 12f;
        public float speed = 1f;
        public string loop = "loop";
        public int loopCount = 0;
        public bool pingSkip = true;
    }

    [System.Serializable]
    public class TxtDto
    {
        public string text = "", colors = "", align = "center", valign = "middle";
        public float x, y, w, h, font = 24f;
    }

    [System.Serializable]
    public class RectDto
    {
        public float x, y, w, h;
    }

    [HarmonyPatch(typeof(PreRunScript), "Start")]
    internal static class PreRunPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PreRunScript __instance)
        {
            if (!Plugin.CfgEnabled.Value) return;
            var w = __instance.contentWarning;
            if (w == null) { Plugin.Log.LogWarning("contentWarning 为空"); return; }

            if (__instance.gameObject.GetComponent<WarningEditorHotkey>() == null)
                __instance.gameObject.AddComponent<WarningEditorHotkey>();

            Plugin.ApplyLayout(w.transform);
        }
    }

    [HarmonyPatch(typeof(SettingsMenu), "SelectTab", new Type[] { typeof(Setting.SettingCategory) })]
    internal static class SettingsRowPatch
    {
        [HarmonyPostfix]
        private static void Postfix(SettingsMenu __instance, Setting.SettingCategory category)
        {
            if (category != Setting.SettingCategory.Game) return;
            try
            {
                if (!Plugin.CfgEnabled.Value) return;

                var row = Utils.Create("Special/GameSettingInput", __instance.content);
                if (row == null) return;
                row.name = "WarningLayoutRow";

                var label = row.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
                if (label != null) label.text = L.T("警告页布局", "Warning Layout");

                var btn = row.transform.GetChild(1).GetComponent<Button>();
                if (btn != null)
                {
                    var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
                    if (txt != null) txt.text = L.T("打开编辑器", "Open Editor");
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() =>
                    {
                        var pre = PreRunScript.instance;
                        if (pre == null || pre.contentWarning == null) return;
                        var warning = pre.contentWarning;
                        if (SettingsMenu.instance != null) SettingsMenu.instance.Close();
                        LayoutEditor.Open(warning.transform);
                    });
                }

                var rt = row.GetComponent<RectTransform>();
                var list = AccessTools.FieldRefAccess<SettingsMenu, List<GameObject>>("spawnedSettings")(__instance);

                float top = 0f;
                if (list != null)
                {
                    foreach (var g in list)
                    {
                        if (g == null) continue;
                        var r = g.GetComponent<RectTransform>();
                        if (r == null) continue;
                        top = Mathf.Min(top, r.anchoredPosition.y - r.sizeDelta.y * 0.5f);
                    }
                    list.Add(row);
                }

                // 原版在 SelectTab 末尾把 content 高度设成 num；我们是在之后追加的，
                // 不补高度的话这一行会落在内容矩形外，被列表裁掉。
                float used = -top;
                rt.anchoredPosition = new Vector2(0f, top - rt.sizeDelta.y * 0.5f);

                var content = __instance.content;
                if (content != null)
                {
                    content.sizeDelta = new Vector2(content.sizeDelta.x, used + rt.sizeDelta.y);
                    Plugin.Log.LogInfo($"设置行已插入: y={rt.anchoredPosition.y:0} contentH={content.sizeDelta.y:0} (原 {used:0})");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("插入设置行失败: " + e);
            }
        }
    }
}
