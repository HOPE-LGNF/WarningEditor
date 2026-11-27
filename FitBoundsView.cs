using UnityEngine;
using UnityEngine.UI;

namespace WarningEditor
{
    /// <summary>
    /// 显示图片在 RectTransform 里“真实画出来”的区域。
    /// preserveAspect 时图片会在框内居中留黑边，这个框把真实占位画出来。
    /// </summary>
    public class FitBoundsView : MonoBehaviour
    {
        private Image _target;
        private Image[] _lines;

        public void Init(Image target)
        {
            _target = target;
            _lines = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("FitLine", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                var img = go.GetComponent<Image>();
                img.color = new Color(1f, 0.85f, 0.2f, 0.8f);
                img.raycastTarget = false;
                _lines[i] = img;

                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
            }
            UpdateView();
        }

        private void LateUpdate() => UpdateView();

        public void UpdateView()
        {
            if (_target == null || _lines == null) return;

            var host = transform as RectTransform;
            if (host == null) return;

            float W = host.rect.width;
            float H = host.rect.height;
            float w = W, h = H;

            var sp = _target.sprite;
            if (sp != null && _target.preserveAspect)
            {
                var s = sp.rect.size;
                if (s.x > 0f && s.y > 0f)
                {
                    float k = Mathf.Min(W / s.x, H / s.y);
                    w = s.x * k;
                    h = s.y * k;
                }
            }

            SetLine(0, new Vector2(0f, h / 2f), new Vector2(w, 1f));
            SetLine(1, new Vector2(0f, -h / 2f), new Vector2(w, 1f));
            SetLine(2, new Vector2(-w / 2f, 0f), new Vector2(1f, h));
            SetLine(3, new Vector2(w / 2f, 0f), new Vector2(1f, h));
        }

        private void SetLine(int i, Vector2 pos, Vector2 size)
        {
            var rt = _lines[i].rectTransform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
