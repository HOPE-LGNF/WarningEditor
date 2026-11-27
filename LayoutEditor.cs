using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WarningEditor
{
    /// <summary>
    /// 警告页编辑器。顶部 = 元素管理，左侧 = 属性处理，中间 = 画布。
    /// 支持撤回、保存确认、对齐、文本框横/纵对齐、游戏文本覆盖。
    /// </summary>
    public class LayoutEditor : MonoBehaviour
    {
        private static LayoutEditor _instance;

        private Transform _warning;
        private Canvas _canvas;
        private Transform _root;
        private readonly Dictionary<string, RectTransform> _elements = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, Image> _hits = new Dictionary<string, Image>();
        private readonly List<string> _order = new List<string>();

        private string _selected = "Text";
        private RectTransform _handle;
        private RectTransform _overlay;
        private GameObject _grid;
        private GameObject _bar;
        private GameObject _panel;
        private GameObject _tab;
        private GameObject _previewExit;
        private GameObject _modal;
        private Image _guideV, _guideH;
        private TMP_InputField _textInput;
        private bool _preview;
        private bool _modalOpen;
        private bool _warningWasActive;
        private Button _warningButton;
        private string _status = "";
        private bool _closing;

        private Vector2 _sizeClip;
        private bool _hasSizeClip;

        private class Snap
        {
            public int ImgCount, TxtCount;
            public readonly List<string> ImgNames = new List<string>();
            public readonly List<Vector4> ImgRects = new List<Vector4>();
            public readonly List<float> ImgFps = new List<float>();
            public readonly List<float> ImgSpeed = new List<float>();
            public readonly List<string> ImgLoop = new List<string>();
            public readonly List<int> ImgLoopCount = new List<int>();
            public readonly List<bool> ImgPingSkip = new List<bool>();
            public readonly List<string> TxtText = new List<string>();
            public readonly List<string> TxtColors = new List<string>();
            public readonly List<string> TxtAlign = new List<string>();
            public readonly List<string> TxtVAlign = new List<string>();
            public readonly List<Vector4> TxtRects = new List<Vector4>();
            public readonly List<float> TxtFont = new List<float>();
            public Vector4 TextRect, LogoRect;
            public string Override;
        }

        private readonly List<Snap> _undo = new List<Snap>();
        private Snap _initial;

        private class BtnEntry
        {
            public RectTransform rt;
            public Action act;
            public string name;
        }

        private readonly List<BtnEntry> _buttons = new List<BtnEntry>();
        private readonly List<BtnEntry> _modalButtons = new List<BtnEntry>();
        private readonly List<RectTransform> _inputs = new List<RectTransform>();
        private BtnEntry _pressed;

        internal static bool IsOpen => _instance != null;

        internal float ScaleFactor => _canvas != null ? _canvas.scaleFactor : 1f;
        internal int ImageCount => Mathf.Max(0, Plugin.CfgCount.Value);
        internal int TextCount => Mathf.Max(0, Plugin.CfgTextCount.Value);

        private const float W = 1920f, H = 1080f;
        private const float BarH = 64f;
        private const float PanelW = 300f;

        private static bool IsImg(string id) => id.StartsWith("Img");
        private static bool IsTxt(string id) => id.StartsWith("Txt");
        private static int IdxOf(string id) => int.Parse(id.Substring(3));

        // ---------------- 打开 / 关闭 ----------------
        public static void Open(Transform warning)
        {
            if (_instance != null || warning == null) return;
            var canvas = warning.GetComponentInParent<Canvas>();
            if (canvas == null) { Plugin.Log.LogError("找不到 Canvas"); return; }

            var edCanvasGo = new GameObject("WarningEditorCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            edCanvasGo.transform.SetParent(null, false);

            var ec = edCanvasGo.GetComponent<Canvas>();
            ec.renderMode = RenderMode.ScreenSpaceOverlay;
            ec.overrideSorting = true;
            ec.sortingOrder = 1000;

            var ecrt = edCanvasGo.GetComponent<RectTransform>();
            ecrt.anchorMin = Vector2.zero; ecrt.anchorMax = Vector2.one;
            ecrt.offsetMin = Vector2.zero; ecrt.offsetMax = Vector2.zero;
            ecrt.localScale = Vector3.one; ecrt.localPosition = Vector3.zero;

            var sc = edCanvasGo.GetComponent<CanvasScaler>();
            var srcScaler = canvas.GetComponent<CanvasScaler>();
            if (srcScaler != null)
            {
                sc.uiScaleMode = srcScaler.uiScaleMode;
                sc.referenceResolution = srcScaler.referenceResolution;
                sc.screenMatchMode = srcScaler.screenMatchMode;
                sc.matchWidthOrHeight = srcScaler.matchWidthOrHeight;
                sc.scaleFactor = srcScaler.scaleFactor;
                sc.referencePixelsPerUnit = srcScaler.referencePixelsPerUnit;
            }

            var root = new GameObject("WarningLayoutEditor", typeof(RectTransform));
            root.transform.SetParent(edCanvasGo.transform, false);
            root.transform.SetAsLastSibling();
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            L.Reset();
            var ed = root.AddComponent<LayoutEditor>();
            _instance = ed;
            ed._warning = warning;
            ed._canvas = canvas;
            ed._root = root.transform;
            ed._warningWasActive = warning.gameObject.activeSelf;

            if (!warning.gameObject.activeSelf) warning.gameObject.SetActive(true);
            ed._warningButton = warning.GetComponent<Button>();
            if (ed._warningButton != null) ed._warningButton.enabled = false;

            ed.Build();
            ed._initial = ed.Capture();
            Plugin.Log.LogInfo("布局编辑器已打开");
        }

        private void Close(bool apply)
        {
            _closing = true;
            if (!apply && _initial != null) ApplySnapshot(_initial, false);

            CleanupHits();
            if (_warningButton != null) _warningButton.enabled = true;
            if (!_warningWasActive && _warning != null) _warning.gameObject.SetActive(false);
            _instance = null;
            if (_root != null && _root.parent != null && _root.parent.GetComponent<Canvas>() != null)
                Destroy(_root.parent.gameObject);
            else if (_root != null) Destroy(_root.gameObject);
            Plugin.Log.LogInfo("布局编辑器已关闭");
        }

        // ---------------- 快照 / 撤回 ----------------
        private Snap Capture()
        {
            var s = new Snap();
            s.ImgCount = ImageCount;
            for (int i = 0; i < s.ImgCount; i++)
            {
                s.ImgNames.Add(Plugin.GetImgName(i).Value);
                s.ImgRects.Add(Plugin.GetImgRect(i));
                s.ImgFps.Add(Plugin.EF(i, "Fps", 12f, "序列播放帧率").Value);
                s.ImgSpeed.Add(Plugin.EF(i, "Speed", 1f, "速度倍率").Value);
                s.ImgLoop.Add(Plugin.ES(i, "Loop", "loop", "循环模式").Value);
                s.ImgLoopCount.Add(Plugin.EI(i, "LoopCount", 0, "循环次数").Value);
                s.ImgPingSkip.Add(Plugin.EB(i, "PingSkip", true, "跳过重复端点").Value);
            }
            s.TxtCount = TextCount;
            for (int i = 0; i < s.TxtCount; i++)
            {
                s.TxtText.Add(Plugin.GetTxtText(i).Value);
                s.TxtColors.Add(Plugin.GetTxtColors(i).Value);
                s.TxtAlign.Add(Plugin.GetTxtAlign(i).Value);
                s.TxtVAlign.Add(Plugin.GetTxtVAlign(i).Value);
                s.TxtRects.Add(Plugin.GetTxtRect(i));
                s.TxtFont.Add(Plugin.GetTxtRectDict(i)["Font"].Value);
            }
            s.TextRect = Plugin.GetFixed("Text");
            s.LogoRect = Plugin.GetFixed("Logo");
            s.Override = Plugin.CfgTextOverride.Value;
            return s;
        }

        private void ApplySnapshot(Snap s, bool rebuildUi)
        {
            if (s == null) return;
            Plugin.CfgCount.Value = s.ImgCount;
            for (int i = 0; i < s.ImgCount; i++)
            {
                Plugin.GetImgName(i).Value = s.ImgNames[i];
                Plugin.SetImgRect(i, s.ImgRects[i]);
                Plugin.EF(i, "Fps", 12f, "序列播放帧率").Value = s.ImgFps[i];
                Plugin.EF(i, "Speed", 1f, "速度倍率").Value = s.ImgSpeed[i];
                Plugin.ES(i, "Loop", "loop", "循环模式").Value = s.ImgLoop[i];
                Plugin.EI(i, "LoopCount", 0, "循环次数").Value = s.ImgLoopCount[i];
                Plugin.EB(i, "PingSkip", true, "跳过重复端点").Value = s.ImgPingSkip[i];
            }
            Plugin.CfgTextCount.Value = s.TxtCount;
            for (int i = 0; i < s.TxtCount; i++)
            {
                Plugin.GetTxtText(i).Value = s.TxtText[i];
                Plugin.GetTxtColors(i).Value = s.TxtColors[i];
                Plugin.GetTxtAlign(i).Value = s.TxtAlign[i];
                Plugin.GetTxtVAlign(i).Value = s.TxtVAlign[i];
                Plugin.GetTxtRectDict(i)["Font"].Value = s.TxtFont[i];
                Plugin.SetTxtRect(i, s.TxtRects[i]);
            }
            Plugin.SetFixed("Text", s.TextRect);
            Plugin.SetFixed("Logo", s.LogoRect);
            Plugin.CfgTextOverride.Value = s.Override;

            Plugin.SyncImages(_warning);
            Plugin.SyncTexts(_warning);
            Plugin.ApplyTextOverride(_warning);

            if (rebuildUi)
            {
                _selected = "Text";
                CollectElements();
                CleanupHits();
                for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
                _buttons.Clear(); _inputs.Clear();
                _elements.Clear(); _order.Clear();
                Build();
            }
        }

        private void PushUndo()
        {
            CommitToMemory();
            _undo.Add(Capture());
            if (_undo.Count > 40) _undo.RemoveAt(0);
        }

        private void Undo()
        {
            if (_undo.Count == 0) { _status = L.T("没有可撤回的操作", "Nothing to undo"); RefreshPanel(); return; }
            var s = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            ApplySnapshot(s, true);
            _status = L.T("已撤回", "Undone");
            RefreshPanel();
        }

        // ---------------- 构建 ----------------
        private void Build()
        {
            CollectElements();
            BuildGrid();
            BuildBar();
            BuildOverlay();
            BuildTab();
            foreach (var id in _order) BuildHit(id);
            Select(_order.Count > 0 ? _order[0] : "Text");
        }

        private void Rebuild()
        {
            CleanupHits();
            for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
            _elements.Clear(); _order.Clear();
            _buttons.Clear(); _inputs.Clear(); _pressed = null;
            _handle = null; _bar = null; _panel = null; _tab = null; _overlay = null;
            _previewExit = null; _guideV = null; _guideH = null; _textInput = null; _modal = null;
            _preview = false; _modalOpen = false;
            if (_warningButton != null) _warningButton.enabled = false;
            Build();
        }

        private void CollectElements()
        {
            _elements.Clear(); _order.Clear();

            var text = _warning.Find("Text (TMP)") as RectTransform;
            if (text != null) { _elements["Text"] = text; _order.Add("Text"); }

            var logo = _warning.Find("Cas") as RectTransform;
            if (logo != null) { _elements["Logo"] = logo; _order.Add("Logo"); }

            for (int i = 0; i < ImageCount; i++)
            {
                var t = _warning.Find("WarningImg" + i) as RectTransform;
                if (t == null) continue;
                _elements["Img" + i] = t; _order.Add("Img" + i);
            }

            for (int i = 0; i < TextCount; i++)
            {
                var t = _warning.Find("WarningTxt" + i) as RectTransform;
                if (t == null) continue;
                _elements["Txt" + i] = t; _order.Add("Txt" + i);
            }
        }

        private RectTransform FindElement(string id)
        {
            _elements.TryGetValue(id, out var rt);
            return rt;
        }

        private string LabelOf(string id)
        {
            if (id == "Text") return L.T("文本", "Text");
            if (id == "Logo") return "Logo";
            if (IsImg(id)) return L.T("图", "Img") + IdxOf(id);
            return L.T("字", "Txt") + IdxOf(id);
        }

        private void BuildGrid()
        {
            _grid = new GameObject("Grid", typeof(RectTransform));
            _grid.transform.SetParent(_root, false);
            var rt = _grid.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            Color c = new Color(1f, 1f, 1f, 0.05f);
            for (float x = 0; x <= W; x += 120f) AddLine(_grid.transform, new Vector2(x, 0), new Vector2(1f, H), c);
            for (float y = 0; y <= H; y += 120f) AddLine(_grid.transform, new Vector2(0, y), new Vector2(W, 1f), c);
            AddLine(_grid.transform, new Vector2(W / 2f, 0), new Vector2(1f, H), new Color(0.3f, 0.8f, 1f, 0.14f));
            AddLine(_grid.transform, new Vector2(0, H / 2f), new Vector2(W, 1f), new Color(0.3f, 0.8f, 1f, 0.14f));
        }

        private static void AddLine(Transform parent, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject("L", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(pos.x, -pos.y);
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = c; img.raycastTarget = false;
        }

        private void BuildBar()
        {
            _bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            _bar.transform.SetParent(_root, false);
            var rt = _bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, BarH);
            _bar.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.06f, 1f);

            MakeLabel(_bar.transform, L.T("警告页编辑器", "Warning Editor"), 20,
                      new Vector2(16f, -8f), new Vector2(190f, 26f));

            float x = 210f;
            foreach (var id in _order)
            {
                var sid = id;
                MakeButton(_bar.transform, LabelOf(id), new Vector2(x, -6f), new Vector2(74f, 30f), () => Select(sid));
                x += 80f;
            }

            MakeButton(_bar.transform, L.T("+ 图片", "+ Img"), new Vector2(1470f, -6f), new Vector2(96f, 30f), AddImage);
            MakeButton(_bar.transform, L.T("+ 文本", "+ Text"), new Vector2(1572f, -6f), new Vector2(96f, 30f), AddText);
            MakeButton(_bar.transform, L.T("预览", "Preview"), new Vector2(1674f, -6f), new Vector2(88f, 30f), () => SetPreview(true));
            MakeButton(_bar.transform, L.T("保存", "Save"), new Vector2(1768f, -6f), new Vector2(70f, 30f), () => Save());
            MakeButton(_bar.transform, L.T("取消", "Cancel"), new Vector2(1844f, -6f), new Vector2(66f, 30f), CancelRequest);
        }

        private void BuildTab()
        {
            _tab = MakeButton(_root, L.T("▲ 编辑器", "▲ Editor"), new Vector2(16f, 16f), new Vector2(120f, 34f),
                              () => SetPreview(false));
            var rt = _tab.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            _tab.SetActive(false);
        }

        // ---------------- 左侧属性面板 ----------------
        private void BuildPanel()
        {
            if (_panel != null)
            {
                var old = _panel.transform;
                _buttons.RemoveAll(b => b.rt == null || b.rt.IsChildOf(old));
                _inputs.RemoveAll(r => r == null || r.IsChildOf(old));
                Destroy(_panel);
            }

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(_root, false);
            var rt = _panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(PanelW, -BarH);
            _panel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 1f);

            var p = _panel.transform;
            MakeLabel(p, L.T("属性", "Properties") + " · " + LabelOf(_selected), 18,
                      new Vector2(14f, -10f), new Vector2(270f, 24f));

            var e = FindElement(_selected);
            if (e == null) return;

            float y = 44f;
            void Row(string label, string value, Action<string> onCommit, float labelW = 14f)
            {
                MakeLabel(p, label, 14, new Vector2(14f, -y - 3f), new Vector2(labelW, 22f));
                float ix = 14f + labelW + 4f;
                MakeInput(p, new Vector2(ix, -y), new Vector2(PanelW - 14f - ix, 30f), value, onCommit);
                y += 38f;
            }

            MakeButton(p, L.T("撤回", "Undo"), new Vector2(14f, -y), new Vector2(84f, 28f), Undo);
            MakeButton(p, L.T("置顶", "Front"), new Vector2(104f, -y), new Vector2(84f, 28f), BringToFront);
            MakeButton(p, L.T("居中", "Center"), new Vector2(194f, -y), new Vector2(84f, 28f), () => Align("cxcy"));
            y += 34f;

            MakeButton(p, L.T("导出布局", "Export"), new Vector2(14f, -y), new Vector2(126f, 28f), DoExport);
            MakeButton(p, L.T("导入布局", "Import"), new Vector2(146f, -y), new Vector2(126f, 28f), DoImport);
            y += 36f;

            var tl0 = GetTopLeft(e, _selected);
            Row("X", tl0.x.ToString("0.0"), s =>
            {
                if (float.TryParse(s, out var v))
                {
                    var cur = GetTopLeft(e, _selected); cur.x = v; SetTopLeft(e, _selected, cur);
                    LogRect("输入X", _selected); UpdateHandle();
                }
            });
            Row("Y", tl0.y.ToString("0.0"), s =>
            {
                if (float.TryParse(s, out var v))
                {
                    var cur = GetTopLeft(e, _selected); cur.y = v; SetTopLeft(e, _selected, cur);
                    LogRect("输入Y", _selected); UpdateHandle();
                }
            });
            Row("W", e.sizeDelta.x.ToString("0.0"), s =>
            {
                if (float.TryParse(s, out var v)) { PushUndo(); e.sizeDelta = new Vector2(Mathf.Max(1f, v), e.sizeDelta.y); LogRect("输入W", _selected); UpdateHandle(); }
            });
            Row("H", e.sizeDelta.y.ToString("0.0"), s =>
            {
                if (float.TryParse(s, out var v)) { PushUndo(); e.sizeDelta = new Vector2(e.sizeDelta.x, Mathf.Max(1f, v)); LogRect("输入H", _selected); UpdateHandle(); }
            });

            MakeButton(p, L.T("复制大小", "Copy Size"), new Vector2(14f, -y), new Vector2(126f, 28f), CopySize);
            MakeButton(p, L.T("粘贴大小", "Paste Size"), new Vector2(146f, -y), new Vector2(126f, 28f), PasteSize);
            y += 36f;

            MakeButton(p, L.T("贴顶", "Top"), new Vector2(14f, -y), new Vector2(84f, 28f), () => Align("top"));
            MakeButton(p, L.T("垂直居中", "V.Center"), new Vector2(104f, -y), new Vector2(84f, 28f), () => Align("cy"));
            MakeButton(p, L.T("贴底", "Bottom"), new Vector2(194f, -y), new Vector2(84f, 28f), () => Align("bottom"));
            y += 34f;
            MakeButton(p, L.T("贴左", "Left"), new Vector2(14f, -y), new Vector2(84f, 28f), () => Align("left"));
            MakeButton(p, L.T("水平居中", "H.Center"), new Vector2(104f, -y), new Vector2(84f, 28f), () => Align("cx"));
            MakeButton(p, L.T("贴右", "Right"), new Vector2(194f, -y), new Vector2(84f, 28f), () => Align("right"));
            y += 36f;

            if (IsImg(_selected))
            {
                MakeButton(p, L.T("换图", "Swap"), new Vector2(14f, -y), new Vector2(84f, 28f), CycleImage);
                MakeButton(p, L.T("刷新图片", "Refresh"), new Vector2(104f, -y), new Vector2(174f, 28f), () =>
                {
                    Plugin.ClearImageCache();
                    Rebuild();
                });
                y += 34f;
            }

            if (IsImg(_selected))
            {
                var ap = e.GetComponent<AnimPlayer>();
                if (ap != null && ap.IsAnimated)
                {
                    int ai = IdxOf(_selected);
                    float eff = ap.Fps > 0f ? ap.FrameCount / ap.Fps / Mathf.Max(0.01f, ap.Speed) : 0f;
                    var sum = MakeLabel(p, $"▶ {ap.FrameCount} 帧 · {ap.Fps:0.#} fps · {eff:0.##}s", 13,
                                        new Vector2(14f, -y), new Vector2(272f, 20f));
                    sum.color = new Color(0.5f, 0.9f, 1f, 1f);
                    y += 24f;

                    Row(L.T("FPS", "FPS"), ap.Fps.ToString("0.#"), str =>
                    {
                        if (float.TryParse(str, out var v) && v > 0f)
                        {
                            PushUndo();
                            Plugin.EF(ai, "Fps", 12f, "序列播放帧率").Value = v;
                            ap.Fps = v; ap.ResetPlayback();
                            _status = "FPS " + v;
                            RefreshPanel();
                        }
                    }, 34f);

                    Row(L.T("速度", "Speed"), ap.Speed.ToString("0.##"), str =>
                    {
                        if (float.TryParse(str, out var v) && v > 0f)
                        {
                            PushUndo();
                            Plugin.EF(ai, "Speed", 1f, "速度倍率").Value = v;
                            ap.Speed = v; ap.ResetPlayback();
                            _status = L.T("速度 x", "Speed x") + v;
                            RefreshPanel();
                        }
                    }, 34f);

                    MakeButton(p, L.T("循环模式: ", "Loop: ") + LoopName(ap.LoopMode),
                               new Vector2(14f, -y), new Vector2(272f, 28f), () =>
                    {
                        PushUndo();
                        string next = ap.LoopMode == "loop" ? "pingpong" : ap.LoopMode == "pingpong" ? "once" : "loop";
                        ap.LoopMode = next; ap.ResetPlayback();
                        Plugin.ES(ai, "Loop", "loop", "循环模式").Value = next;
                    });
                    y += 32f;

                    MakeButton(p, L.T("循环次数: ", "Count: ") + (ap.LoopCount == 0 ? "∞" : ap.LoopCount.ToString()),
                               new Vector2(14f, -y), new Vector2(272f, 28f), () =>
                    {
                        PushUndo();
                        int[] opts = { 0, 1, 3, 5, 10 };
                        int idx = System.Array.IndexOf(opts, ap.LoopCount);
                        int next = opts[(idx + 1 + opts.Length) % opts.Length];
                        ap.LoopCount = next; ap.ResetPlayback();
                        Plugin.EI(ai, "LoopCount", 0, "循环次数").Value = next;
                    });
                    y += 32f;

                    if (ap.LoopMode == "pingpong")
                    {
                        MakeButton(p, L.T("跳过重复端点: ", "Skip ends: ") + (ap.PingSkip ? L.T("开", "On") : L.T("关", "Off")),
                                   new Vector2(14f, -y), new Vector2(272f, 28f), () =>
                        {
                            PushUndo();
                            ap.PingSkip = !ap.PingSkip; ap.ResetPlayback();
                            Plugin.EB(ai, "PingSkip", true, "跳过重复端点").Value = ap.PingSkip;
                        });
                        y += 32f;
                    }
                }
            }

            if (IsImg(_selected) || IsTxt(_selected))
            {
                MakeButton(p, L.T("删除此元素", "Delete"), new Vector2(14f, -y), new Vector2(126f, 28f), DeleteSelected);
                y += 34f;
            }

            MakeButton(p, L.T("恢复默认布局", "Reset Layout"), new Vector2(14f, -y), new Vector2(272f, 30f), ResetRequest);
            var rb = _buttons[_buttons.Count - 1].rt.GetComponent<Image>();
            if (rb != null) rb.color = new Color(0.42f, 0.12f, 0.14f, 1f);
            y += 40f;

            if (_selected == "Text")
            {
                MakeLabel(p, L.T("内容（留空用原文）", "Content (empty = original)"), 13,
                          new Vector2(14f, -y), new Vector2(272f, 20f));
                y += 24f;
                var tmp = e.GetComponent<TextMeshProUGUI>();
                string effective = Plugin.HasTextOverride
                    ? Plugin.CfgTextOverride.Value
                    : (Plugin.LocalizedWarningText() ?? (tmp != null ? tmp.text : ""));
                _textInput = MakeInput(p, new Vector2(14f, -y), new Vector2(272f, 92f), effective,
                                       s =>
                                       {
                                           // 没改就不写 override，避免把语言钉死
                                           if (s == effective) return;
                                           PushUndo();
                                           Plugin.CfgTextOverride.Value = s;
                                           Plugin.ApplyTextOverride(_warning);
                                       }, true);
                y += 100f;
                MakeButton(p, L.T("清除自定义文本", "Clear Override"), new Vector2(14f, -y), new Vector2(272f, 28f), () =>
                {
                    PushUndo();
                    Plugin.CfgTextOverride.Value = "";
                    Plugin.ApplyTextOverride(_warning);
                    BuildPanel();
                });
                y += 34f;
            }

            if (IsTxt(_selected))
            {
                var tmp = e.GetComponent<TextMeshProUGUI>();
                int tid = IdxOf(_selected);

                MakeLabel(p, L.T("内容", "Content"), 13, new Vector2(14f, -y), new Vector2(100f, 20f));
                y += 24f;
                _textInput = MakeInput(p, new Vector2(14f, -y), new Vector2(272f, 92f),
                                       Plugin.GetTxtText(tid).Value, s =>
                {
                    PushUndo();
                    Plugin.GetTxtText(tid).Value = s;
                    string spec = RichText.Clamp(Plugin.GetTxtColors(tid).Value, s.Length);
                    Plugin.GetTxtColors(tid).Value = spec;
                    if (tmp != null) tmp.text = RichText.Build(s, spec);
                }, true);
                y += 100f;

                var fontEntry = Plugin.GetTxtRectDict(tid)["Font"];
                Row(L.T("字号", "Size"), fontEntry.Value.ToString("0"), s =>
                {
                    if (float.TryParse(s, out var v))
                    {
                        PushUndo();
                        fontEntry.Value = Mathf.Max(6f, v);
                        if (tmp != null) tmp.fontSize = fontEntry.Value;
                    }
                }, 30f);

                MakeLabel(p, L.T("颜色（选中文本只染选段）", "Color (selection only)"), 12,
                          new Vector2(14f, -y), new Vector2(272f, 20f));
                y += 22f;
                AddColorSwatches(p, y, tmp);
                y += 32f;

                MakeLabel(p, L.T("横向", "Horizontal"), 13, new Vector2(14f, -y), new Vector2(60f, 20f));
                var align = Plugin.GetTxtAlign(tid);
                MakeButton(p, L.T("左", "L"), new Vector2(78f, -y), new Vector2(58f, 26f), () => SetAlign(align, tmp, "left"));
                MakeButton(p, L.T("中", "C"), new Vector2(140f, -y), new Vector2(58f, 26f), () => SetAlign(align, tmp, "center"));
                MakeButton(p, L.T("右", "R"), new Vector2(202f, -y), new Vector2(58f, 26f), () => SetAlign(align, tmp, "right"));
                y += 32f;

                MakeLabel(p, L.T("纵向", "Vertical"), 13, new Vector2(14f, -y), new Vector2(60f, 20f));
                var valign = Plugin.GetTxtVAlign(tid);
                MakeButton(p, L.T("上", "Top"), new Vector2(78f, -y), new Vector2(58f, 26f), () => SetVAlign(valign, tmp, "top"));
                MakeButton(p, L.T("中", "Mid"), new Vector2(140f, -y), new Vector2(58f, 26f), () => SetVAlign(valign, tmp, "middle"));
                MakeButton(p, L.T("下", "Bot"), new Vector2(202f, -y), new Vector2(58f, 26f), () => SetVAlign(valign, tmp, "bottom"));
                y += 32f;
            }

            if (!string.IsNullOrEmpty(_status))
            {
                var st = MakeLabel(p, _status, 13, new Vector2(14f, -y - 2f), new Vector2(272f, 20f));
                st.color = new Color(0.45f, 0.85f, 1f, 1f);
            }
        }

        private static string LoopName(string m)
        {
            if (m == "pingpong") return L.T("往返", "Ping-pong");
            if (m == "once") return L.T("单次", "Once");
            return L.T("循环", "Loop");
        }

        private void AddColorSwatches(Transform p, float y, TextMeshProUGUI tmp)
        {
            var colors = new[]
            {
                Color.white, new Color(1f, 0.42f, 0.42f), new Color(1f, 0.74f, 0.36f),
                new Color(0.45f, 0.9f, 0.55f), new Color(0.45f, 0.72f, 1f), new Color(0.65f, 0.65f, 0.7f)
            };
            for (int i = 0; i < colors.Length; i++)
            {
                var c = colors[i];
                var go = MakeButton(p, "", new Vector2(14f + i * 46f, -y), new Vector2(40f, 26f), () => ApplyColor(tmp, c));
                go.GetComponent<Image>().color = c;
            }
        }

        private void ApplyColor(TextMeshProUGUI tmp, Color c)
        {
            if (tmp == null || !IsTxt(_selected)) return;
            int id = IdxOf(_selected);
            string plain = Plugin.GetTxtText(id).Value ?? "";
            if (plain.Length == 0) return;

            int a = _textInput != null ? _textInput.selectionAnchorPosition : 0;
            int b = _textInput != null ? _textInput.selectionFocusPosition : 0;
            if (a == b) { a = 0; b = plain.Length; }
            a = Mathf.Clamp(a, 0, plain.Length);
            b = Mathf.Clamp(b, 0, plain.Length);
            if (a > b) { int t = a; a = b; b = t; }

            PushUndo();
            string spec = RichText.Apply(plain, Plugin.GetTxtColors(id).Value, a, b, c);
            Plugin.GetTxtColors(id).Value = spec;
            tmp.text = RichText.Build(plain, spec);
            _status = L.T("已染色 ", "Colored ") + $"{a}–{b}";
            RefreshPanel();
        }

        private void SetAlign(BepInEx.Configuration.ConfigEntry<string> entry, TextMeshProUGUI tmp, string v)
        {
            PushUndo();
            entry.Value = v;
            if (tmp != null) tmp.alignment = Plugin.ComposeAlign(v, Plugin.GetTxtVAlign(IdxOf(_selected)).Value);
            _status = L.T("横向对齐 ", "H align ") + v;
        }

        private void SetVAlign(BepInEx.Configuration.ConfigEntry<string> entry, TextMeshProUGUI tmp, string v)
        {
            PushUndo();
            entry.Value = v;
            if (tmp != null) tmp.alignment = Plugin.ComposeAlign(Plugin.GetTxtAlign(IdxOf(_selected)).Value, v);
            _status = L.T("纵向对齐 ", "V align ") + v;
        }

        // ---------------- Overlay / 手柄 / 参考线 ----------------
        private void BuildOverlay()
        {
            var go = new GameObject("Overlay", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            _overlay = go.GetComponent<RectTransform>();
            _overlay.anchorMin = Vector2.zero; _overlay.anchorMax = Vector2.one;
            _overlay.offsetMin = Vector2.zero; _overlay.offsetMax = Vector2.zero;

            _guideV = MakeGuide(_overlay, new Vector2(2f, H));
            _guideH = MakeGuide(_overlay, new Vector2(W, 2f));
            _guideV.gameObject.SetActive(false);
            _guideH.gameObject.SetActive(false);

            _previewExit = MakeButton(_root, L.T("退出预览 (Esc)", "Exit Preview (Esc)"),
                                      new Vector2(16f, -16f), new Vector2(200f, 36f), () => SetPreview(false));
            var prt = _previewExit.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            prt.anchoredPosition = new Vector2(16f, -16f);
            _previewExit.SetActive(false);
        }

        private Image MakeGuide(Transform parent, Vector2 size)
        {
            var go = new GameObject("Guide", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 0.35f, 0.75f, 0.9f);
            img.raycastTarget = false;
            return img;
        }

        private void BuildHit(string id)
        {
            var target = FindElement(id);
            if (target == null) return;
            var old = target.Find("EditHit");
            if (old != null) Destroy(old.gameObject);

            var go = new GameObject("EditHit", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(target, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.raycastTarget = true;
            img.color = new Color(0.3f, 0.65f, 1f, 0.05f);

            var drag = go.AddComponent<DragMove>();
            drag.Editor = this; drag.Id = id;
            _hits[id] = img;
        }

        private void CleanupHits()
        {
            foreach (var id in _order)
            {
                var e = FindElement(id);
                if (e == null) continue;
                for (int i = e.childCount - 1; i >= 0; i--)
                {
                    var c = e.GetChild(i);
                    if (c.name == "EditHit" || c.name == "ResizeHandle") Destroy(c.gameObject);
                }
            }
            _hits.Clear(); _handle = null;
        }

        internal void Select(string id)
        {
            if (_selected != id) Plugin.Log.LogInfo("选中: " + id);
            _selected = id;
            foreach (var kv in _hits)
                if (kv.Value != null)
                    kv.Value.color = kv.Key == id ? new Color(0.3f, 0.65f, 1f, 0.18f) : new Color(0.3f, 0.65f, 1f, 0.05f);
            BuildHandle();
            if (!_preview) BuildPanel();
            UpdateHandle();
        }

        private void BuildHandle()
        {
            if (_handle != null) { Destroy(_handle.gameObject); _handle = null; }
            if (_overlay == null) return;
            var e = FindElement(_selected);
            if (e == null) return;

            var go = new GameObject("ResizeHandle", typeof(RectTransform), typeof(Image), typeof(DragResize));
            go.transform.SetParent(_overlay, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(24f, 24f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.36f, 0.78f, 1f, 1f);
            img.raycastTarget = true;

            var drag = go.GetComponent<DragResize>();
            drag.Editor = this; drag.Target = e;
            _handle = rt;
            UpdateHandle();
        }

        private void UpdateHandle()
        {
            if (_handle == null || _overlay == null) return;
            var e = FindElement(_selected);
            if (e == null) return;
            var corners = new Vector3[4];
            e.GetWorldCorners(corners);
            Vector3 local = _overlay.InverseTransformPoint(corners[3]);
            _handle.anchoredPosition = new Vector2(local.x - 10f, local.y + 10f);
        }

        // ---------------- 拖动 + 吸附 ----------------
        internal void BeginDrag() => PushUndo();

        internal void DragBy(string id, Vector2 delta)
        {
            var e = FindElement(id);
            if (e == null || _preview || _modalOpen) return;
            e.anchoredPosition += delta;
            ApplySnap(id, e);
        }

        private void ApplySnap(string id, RectTransform e)
        {
            var tl = GetTopLeft(e, id);
            float w = e.sizeDelta.x, h = e.sizeDelta.y;

            var xs = new List<float> { 960f, 0f, W };
            var ys = new List<float> { 540f, 0f, H };
            foreach (var other in _order)
            {
                if (other == id) continue;
                var o = FindElement(other);
                if (o == null) continue;
                var otl = GetTopLeft(o, other);
                xs.Add(otl.x); xs.Add(otl.x + o.sizeDelta.x / 2f); xs.Add(otl.x + o.sizeDelta.x);
                ys.Add(otl.y); ys.Add(otl.y + o.sizeDelta.y / 2f); ys.Add(otl.y + o.sizeDelta.y);
            }

            float bestDx = 6f, lineX = 0f; bool hitX = false;
            foreach (var cand in xs)
                foreach (var edge in new[] { tl.x, tl.x + w / 2f, tl.x + w })
                {
                    float d = Mathf.Abs(edge - cand);
                    if (d < bestDx) { bestDx = d; lineX = cand; hitX = true; }
                }
            if (hitX)
                foreach (var edge in new[] { tl.x, tl.x + w / 2f, tl.x + w })
                    if (Mathf.Abs(edge - lineX) < 0.01f) { tl.x += lineX - edge; break; }

            float bestDy = 6f, lineY = 0f; bool hitY = false;
            foreach (var cand in ys)
                foreach (var edge in new[] { tl.y, tl.y + h / 2f, tl.y + h })
                {
                    float d = Mathf.Abs(edge - cand);
                    if (d < bestDy) { bestDy = d; lineY = cand; hitY = true; }
                }
            if (hitY)
                foreach (var edge in new[] { tl.y, tl.y + h / 2f, tl.y + h })
                    if (Mathf.Abs(edge - lineY) < 0.01f) { tl.y += lineY - edge; break; }

            SetTopLeft(e, id, tl);

            if (_guideV != null) { _guideV.gameObject.SetActive(hitX); _guideV.rectTransform.anchoredPosition = new Vector2(lineX - 960f, 0f); }
            if (_guideH != null) { _guideH.gameObject.SetActive(hitY); _guideH.rectTransform.anchoredPosition = new Vector2(0f, 540f - lineY); }
        }

        private void HideGuides()
        {
            if (_guideV != null) _guideV.gameObject.SetActive(false);
            if (_guideH != null) _guideH.gameObject.SetActive(false);
        }

        // ---------------- 每帧 ----------------
        private void Update()
        {
            UpdateHandle();
            UpdatePointerGuard();
            HandleManualClicks();

            if (_modalOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CloseDialog();
                return;
            }

            if (_preview)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) SetPreview(false);
                return;
            }

            if (!Input.GetMouseButton(0)) HideGuides();

            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                Input.GetKeyDown(KeyCode.Z))
            {
                Undo();
                return;
            }

            var e = FindElement(_selected);
            if (e == null) return;

            float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 10f : 1f;
            Vector2 d = Vector2.zero;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) d.x -= step;
            if (Input.GetKeyDown(KeyCode.RightArrow)) d.x += step;
            if (Input.GetKeyDown(KeyCode.UpArrow)) d.y += step;
            if (Input.GetKeyDown(KeyCode.DownArrow)) d.y -= step;
            if (d != Vector2.zero) e.anchoredPosition += d;

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                int i = _order.IndexOf(_selected);
                int dir = Input.GetKey(KeyCode.LeftShift) ? -1 : 1;
                Select(_order[(i + dir + _order.Count) % _order.Count]);
            }

            if (Input.GetKeyDown(KeyCode.Escape) && !IsInputFocused()) CancelRequest();
        }

        private bool PointerOver(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null);
        }

        private bool PointerOverInput()
        {
            for (int i = 0; i < _inputs.Count; i++) if (PointerOver(_inputs[i])) return true;
            return false;
        }

        internal bool IsPointerOverPanel() => PoPanel();

        private bool PoPanel()
        {
            if (_panel != null && PointerOver(_panel.GetComponent<RectTransform>())) return true;
            if (_bar != null && PointerOver(_bar.GetComponent<RectTransform>())) return true;
            return false;
        }

        private void UpdatePointerGuard()
        {
            bool over = _modalOpen || (!_preview && PoPanel());
            foreach (var kv in _hits)
            {
                if (kv.Value == null) continue;
                kv.Value.raycastTarget = !over;
            }
            if (_handle != null)
            {
                var img = _handle.GetComponent<Image>();
                if (img != null) img.raycastTarget = !over;
            }
        }

        private void HandleManualClicks()
        {
            var list = _modalOpen ? _modalButtons : _buttons;

            if (Input.GetMouseButtonDown(0))
            {
                _pressed = null;
                if (!PointerOverInput())
                {
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        var b = list[i];
                        if (b.rt == null || !b.rt.gameObject.activeInHierarchy) continue;
                        if (RectTransformUtility.RectangleContainsScreenPoint(b.rt, Input.mousePosition, null))
                        {
                            _pressed = b;
                            var bi = b.rt.GetComponent<Image>();
                            if (bi != null) bi.color = new Color(0.24f, 0.36f, 0.5f, 1f);
                            break;
                        }
                    }
                }
            }

            if (Input.GetMouseButtonUp(0) && _pressed != null)
            {
                var b = _pressed;
                _pressed = null;
                if (b.rt != null)
                {
                    var bi = b.rt.GetComponent<Image>();
                    if (bi != null) bi.color = new Color(0.13f, 0.16f, 0.21f, 1f);
                }
                if (b.rt != null && b.rt.gameObject.activeInHierarchy &&
                    RectTransformUtility.RectangleContainsScreenPoint(b.rt, Input.mousePosition, null))
                {
                    Plugin.Log.LogInfo("点击: " + b.name + "  选中=" + _selected);
                    try { b.act(); }
                    catch (Exception e) { Plugin.Log.LogError("按钮异常 " + b.name + ": " + e); }
                    RefreshPanel();
                }
            }
        }

        private bool IsInputFocused()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var go = es.currentSelectedGameObject;
            return go != null && go.GetComponent<TMP_InputField>() != null;
        }

        private void RefreshPanel()
        {
            if (_closing || _root == null || _preview || IsInputFocused()) return;
            BuildPanel();
        }

        // ---------------- 对话框 ----------------
        private class DlgBtn
        {
            public string Label;
            public Action Act;
            public DlgBtn(string label, Action act) { Label = label; Act = act; }
        }

        private void ShowDialog(string msg, params DlgBtn[] buttons)
        {
            CloseDialog();
            _modalOpen = true;

            _modal = new GameObject("Modal", typeof(RectTransform), typeof(Image));
            _modal.transform.SetParent(_root, false);
            var rt = _modal.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(660f, 240f);
            _modal.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 1f);

            var m = MakeLabel(_modal.transform, msg, 20, new Vector2(24f, -24f), new Vector2(612f, 110f));
            m.alignment = TextAlignmentOptions.Center;
            m.enableWordWrapping = true;

            float bw = 180f, gap = 16f;
            float total = buttons.Length * bw + (buttons.Length - 1) * gap;
            float bx = (660f - total) / 2f;
            foreach (var b in buttons)
            {
                var a = b.Act;
                MakeButton(_modal.transform, b.Label, new Vector2(bx, -160f), new Vector2(bw, 36f), a, true);
                bx += bw + gap;
            }
        }

        private void CloseDialog()
        {
            if (_modal != null) Destroy(_modal);
            _modal = null;
            _modalOpen = false;
            _modalButtons.Clear();
        }

        private void CancelRequest()
        {
            ShowDialog(L.T("有未保存的更改，要保存吗？", "You have unsaved changes. Save them?"),
                new DlgBtn(L.T("保存并退出", "Save & Exit"), () => { CloseDialog(); Save(); }),
                new DlgBtn(L.T("不保存退出", "Exit without saving"), () => { CloseDialog(); Close(false); }),
                new DlgBtn(L.T("取消", "Cancel"), CloseDialog));
        }

        private void DoExport()
        {
            try
            {
                string path = Plugin.ExportLayout();
                _status = L.T("已导出: ", "Exported: ") + path;
                Plugin.Log.LogInfo(_status);
            }
            catch (Exception e)
            {
                _status = L.T("导出失败: ", "Export failed: ") + e.Message;
                Plugin.Log.LogError(_status);
            }
        }

        private void DoImport()
        {
            ShowDialog(L.T("导入会覆盖当前布局，继续？", "Import will overwrite the current layout. Continue?"),
                new DlgBtn(L.T("导入", "Import"), () =>
                {
                    CloseDialog();
                    PushUndo();
                    if (Plugin.ImportLayout(_warning, out var err))
                    {
                        _status = L.T("已导入", "Imported");
                        Rebuild();
                        Select("Text");
                    }
                    else
                    {
                        _status = L.T("导入失败: ", "Import failed: ") + err;
                        Plugin.Log.LogError(_status);
                        RefreshPanel();
                    }
                }),
                new DlgBtn(L.T("取消", "Cancel"), CloseDialog));
        }

        private void ResetRequest()
        {
            ShowDialog(L.T("恢复默认布局？当前改动可以通过「撤回」找回。", "Reset layout to defaults? You can undo this."),
                new DlgBtn(L.T("恢复", "Reset"), () => { CloseDialog(); ResetAll(); }),
                new DlgBtn(L.T("取消", "Cancel"), CloseDialog));
        }

        // ---------------- 坐标 ----------------
        private static Vector2 GetTopLeft(RectTransform rt, string id)
        {
            float w = rt.sizeDelta.x, h = rt.sizeDelta.y;
            float apx = rt.anchoredPosition.x, apy = rt.anchoredPosition.y;
            if (id == "Text") return new Vector2(960f + apx - w / 2f, 540f - apy - h / 2f);
            if (id == "Logo") return new Vector2(960f + apx - w / 2f, 1080f - apy - h / 2f);
            return new Vector2(960f + apx - w / 2f, -apy);
        }

        private static void SetTopLeft(RectTransform rt, string id, Vector2 tl)
        {
            float w = rt.sizeDelta.x, h = rt.sizeDelta.y;
            if (id == "Text") rt.anchoredPosition = new Vector2(tl.x + w / 2f - 960f, 540f - (tl.y + h / 2f));
            else if (id == "Logo") rt.anchoredPosition = new Vector2(tl.x + w / 2f - 960f, 1080f - tl.y - h / 2f);
            else rt.anchoredPosition = new Vector2(tl.x + w / 2f - 960f, -tl.y);
        }

        private void Align(string mode)
        {
            var e = FindElement(_selected);
            if (e == null) return;
            PushUndo();
            LogRect("对齐-前", _selected);
            var tl = GetTopLeft(e, _selected);
            float w = e.sizeDelta.x, h = e.sizeDelta.y;
            switch (mode)
            {
                case "cx": tl.x = (W - w) / 2f; break;
                case "cy": tl.y = (H - h) / 2f; break;
                case "cxcy": tl.x = (W - w) / 2f; tl.y = (H - h) / 2f; break;
                case "top": tl.y = 0f; break;
                case "bottom": tl.y = H - h; break;
                case "left": tl.x = 0f; break;
                case "right": tl.x = W - w; break;
            }
            SetTopLeft(e, _selected, tl);
            LogRect("对齐-后", _selected);
            _status = L.T("已对齐 ", "Aligned ") + _selected;
            UpdateHandle();
        }

        private void BringToFront()
        {
            var e = FindElement(_selected);
            if (e == null) return;
            e.SetAsLastSibling();
            LogRect("置顶", _selected);
            _status = L.T("已置顶 ", "Front: ") + _selected;
        }

        private void CopySize()
        {
            var e = FindElement(_selected);
            if (e == null) return;
            _sizeClip = e.sizeDelta;
            _hasSizeClip = true;
            _status = (L.Zh ? "已复制尺寸 " : "Copied ") + $"{_sizeClip.x:0.0} × {_sizeClip.y:0.0}";
        }

        private void PasteSize()
        {
            if (!_hasSizeClip) return;
            var e = FindElement(_selected);
            if (e == null) return;
            PushUndo();
            e.sizeDelta = _sizeClip;
            _status = (L.Zh ? "已粘贴尺寸 " : "Pasted ") + $"{_sizeClip.x:0.0} × {_sizeClip.y:0.0}";
            UpdateHandle();
        }

        // ---------------- 配置同步 ----------------
        private void CommitToMemory()
        {
            foreach (var id in _order)
            {
                var e = FindElement(id);
                if (e == null) continue;
                var v = new Vector4(e.anchoredPosition.x, e.anchoredPosition.y, e.sizeDelta.x, e.sizeDelta.y);
                if (id == "Text" || id == "Logo") Plugin.SetFixed(id, v);
                else if (IsImg(id)) Plugin.SetImgRect(IdxOf(id), v);
                else Plugin.SetTxtRect(IdxOf(id), v);
            }
        }

        internal void LogRect(string tag, string id)
        {
            var e = FindElement(id);
            if (e == null) { Plugin.Log.LogInfo($"{tag} 目标={id} <未找到>"); return; }
            Plugin.Log.LogInfo($"{tag} 目标={id} ap=({e.anchoredPosition.x:0.0}, {e.anchoredPosition.y:0.0}) size=({e.sizeDelta.x:0.0}, {e.sizeDelta.y:0.0})");
        }

        // ---------------- 增删 ----------------
        private void AddImage()
        {
            PushUndo();
            int n = ImageCount;
            Plugin.CfgCount.Value = n + 1;
            Plugin.GetImgName(n); Plugin.GetImgRect(n);
            Plugin.SyncImages(_warning);
            Rebuild();
            Select("Img" + n);
        }

        private void AddText()
        {
            PushUndo();
            int n = TextCount;
            Plugin.CfgTextCount.Value = n + 1;
            Plugin.GetTxtText(n); Plugin.GetTxtColors(n); Plugin.GetTxtAlign(n); Plugin.GetTxtVAlign(n);
            Plugin.GetTxtRectDict(n);
            Plugin.SyncTexts(_warning);
            Rebuild();
            Select("Txt" + n);
        }

        private void DeleteSelected()
        {
            if (IsImg(_selected)) DeleteImage();
            else if (IsTxt(_selected)) DeleteText();
        }

        private void DeleteImage()
        {
            int n = ImageCount;
            if (n <= 0) return;
            PushUndo();
            int idx = IdxOf(_selected), last = n - 1;
            if (idx != last)
            {
                Plugin.GetImgName(idx).Value = Plugin.GetImgName(last).Value;
                Plugin.SetImgRect(idx, Plugin.GetImgRect(last));
            }
            Plugin.CfgCount.Value = n - 1;
            Plugin.SyncImages(_warning);
            Rebuild(); Select("Text");
        }

        private void DeleteText()
        {
            int n = TextCount;
            if (n <= 0) return;
            PushUndo();
            int idx = IdxOf(_selected), last = n - 1;
            if (idx != last)
            {
                Plugin.GetTxtText(idx).Value = Plugin.GetTxtText(last).Value;
                Plugin.GetTxtColors(idx).Value = Plugin.GetTxtColors(last).Value;
                Plugin.GetTxtAlign(idx).Value = Plugin.GetTxtAlign(last).Value;
                Plugin.GetTxtVAlign(idx).Value = Plugin.GetTxtVAlign(last).Value;
                Plugin.SetTxtRect(idx, Plugin.GetTxtRect(last));
                Plugin.GetTxtRectDict(idx)["Font"].Value = Plugin.GetTxtRectDict(last)["Font"].Value;
            }
            Plugin.CfgTextCount.Value = n - 1;
            Plugin.SyncTexts(_warning);
            Rebuild(); Select("Text");
        }

        private void CycleImage()
        {
            if (!IsImg(_selected)) return;
            int i = IdxOf(_selected);
            var all = Plugin.ListImages();
            if (all.Length == 0) return;

            PushUndo();
            string cur = Plugin.GetImgName(i).Value;
            int pos = Array.IndexOf(all, cur);
            string curGroup = Plugin.SequencePrefix(cur);
            string next = all[(pos + 1 + all.Length) % all.Length];
            for (int step = 1; step <= all.Length; step++)
            {
                var cand = all[(pos + step) % all.Length];
                if (Plugin.SequencePrefix(cand) != curGroup || step == all.Length) { next = cand; break; }
            }
            Plugin.GetImgName(i).Value = next;

            var e = FindElement(_selected);
            if (e != null) Plugin.ConfigureAnim(i, e.gameObject);
            _status = L.T("换图: ", "Image: ") + next;
            Plugin.Log.LogInfo($"换图 目标={_selected} -> {next}");
        }

        // ---------------- 保存 / 重置 ----------------
        private void Save()
        {
            CommitToMemory();
            Plugin.CfgCount.Value = ImageCount;
            Plugin.CfgTextCount.Value = TextCount;
            Plugin.SaveConfig();
            Plugin.Log.LogInfo("布局已保存");
            Close(true);
        }

        private void ResetAll()
        {
            PushUndo();
            Plugin.SetFixed("Text", Plugin.Defaults["Text"]);
            Plugin.SetFixed("Logo", Plugin.Defaults["Logo"]);
            Plugin.CfgCount.Value = 1;
            Plugin.GetImgName(0).Value = "warning_experiment.png";
            Plugin.SetImgRect(0, Plugin.DefaultImg0);
            Plugin.CfgTextCount.Value = 0;
            Plugin.CfgTextOverride.Value = "";

            Plugin.SyncImages(_warning);
            Plugin.SyncTexts(_warning);
            Plugin.ApplyTextOverride(_warning);
            Rebuild();
            Select("Img0");
            _status = L.T("已恢复默认（未保存）", "Reset (not saved)");
        }

        // ---------------- 预览 ----------------
        private void SetPreview(bool on)
        {
            _preview = on;
            if (_bar != null) _bar.SetActive(!on);
            if (_panel != null) _panel.SetActive(!on);
            if (_grid != null) _grid.SetActive(!on);
            if (_tab != null) _tab.SetActive(false);
            if (_previewExit != null) _previewExit.SetActive(on);
            if (_handle != null) _handle.gameObject.SetActive(!on);
            HideGuides();
            foreach (var kv in _hits)
                if (kv.Value != null) kv.Value.enabled = !on;
        }

        // ---------------- UI 小工具 ----------------
        private TextMeshProUGUI MakeLabel(Transform parent, string text, float size, Vector2 pos, Vector2 sizeDelta)
        {
            GameObject go;
            var src = _warning != null ? _warning.Find("Text (TMP)") : null;
            if (src != null)
            {
                go = Instantiate(src.gameObject, parent, false);
                var loc = go.GetComponent<UILocalizer>();
                if (loc != null) { loc.enabled = false; Destroy(loc); }
            }
            else
            {
                go = new GameObject("Label", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                go.AddComponent<TextMeshProUGUI>();
            }

            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Left;
            t.enableWordWrapping = false;
            t.raycastTarget = false;

            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;
            return t;
        }

        private GameObject MakeButton(Transform parent, string text, Vector2 pos, Vector2 size, Action onClick, bool modal = false)
        {
            var go = new GameObject("Btn_" + text, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.13f, 0.16f, 0.21f, 1f);
            img.raycastTarget = false;

            var entry = new BtnEntry { rt = rt, act = onClick, name = string.IsNullOrEmpty(text) ? L.T("颜色", "Color") : text };
            if (modal) _modalButtons.Add(entry); else _buttons.Add(entry);

            if (!string.IsNullOrEmpty(text))
            {
                var label = MakeLabel(go.transform, text, 15, Vector2.zero, size);
                label.alignment = TextAlignmentOptions.Center;
                label.enableAutoSizing = true;
                label.fontSizeMin = 9;
                label.fontSizeMax = 15;
                label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                label.rectTransform.anchoredPosition = Vector2.zero;
                label.rectTransform.sizeDelta = new Vector2(size.x - 8f, size.y - 4f);
            }
            return go;
        }

        private TMP_InputField MakeInput(Transform parent, Vector2 pos, Vector2 size, string value, Action<string> onSubmit, bool multiline = false)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.12f, 1f);

            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(go.transform, false);
            var art = area.GetComponent<RectTransform>();
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(6f, 3f); art.offsetMax = new Vector2(-6f, -3f);

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(area.transform, false);
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            var src = _warning != null ? _warning.Find("Text (TMP)") : null;
            if (src != null)
            {
                var st = src.GetComponent<TextMeshProUGUI>();
                if (st != null && st.font != null) tmp.font = st.font;
            }
            tmp.fontSize = 15;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            var trt = tmp.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

            var input = go.GetComponent<TMP_InputField>();
            input.textViewport = art;
            input.textComponent = tmp;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            input.text = value;
            input.caretColor = Color.white;
            input.customCaretColor = true;
            input.caretWidth = 2;
            input.caretBlinkRate = 1f;
            input.selectionColor = new Color(0.3f, 0.65f, 1f, 0.35f);
            input.onSelect.AddListener(_ => PushUndo());
            input.onEndEdit.AddListener(s =>
            {
                Plugin.Log.LogInfo("输入提交: " + s);
                onSubmit(s);
            });

            go.SetActive(false);
            go.SetActive(true);

            _inputs.Add(rt);
            return input;
        }
    }

    public class DragMove : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler
    {
        public LayoutEditor Editor;
        public string Id;

        public void OnPointerDown(PointerEventData e)
        {
            if (Editor == null || Editor.IsPointerOverPanel()) return;
            Editor.BeginDrag();
            Editor.Select(Id);
        }

        public void OnDrag(PointerEventData e) => Editor.DragBy(Id, e.delta / Editor.ScaleFactor);
        public void OnEndDrag(PointerEventData e) => Editor.LogRect("拖动结束", Id);
    }

    public class DragResize : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler
    {
        public LayoutEditor Editor;
        public RectTransform Target;

        public void OnPointerDown(PointerEventData e) => Editor.BeginDrag();

        public void OnDrag(PointerEventData e)
        {
            if (Target == null) return;
            Vector2 d = e.delta / Editor.ScaleFactor;
            float dw = d.x, dh = -d.y;
            Vector2 p = Target.pivot;
            float w = Mathf.Max(10f, Target.sizeDelta.x + dw);
            float h = Mathf.Max(10f, Target.sizeDelta.y + dh);
            Target.sizeDelta = new Vector2(w, h);
            Target.anchoredPosition += new Vector2(p.x * dw, -(1f - p.y) * dh);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (Target != null)
                Plugin.Log.LogInfo($"缩放结束 目标={Target.name} size=({Target.sizeDelta.x:0.0}, {Target.sizeDelta.y:0.0})");
        }
    }
}
