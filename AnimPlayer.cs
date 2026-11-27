using UnityEngine;
using UnityEngine.UI;

namespace WarningEditor
{
    /// <summary>
    /// PNG 序列播放器（后续 WebP/GIF 也复用这套）。
    /// 帧时长 = 1 / Fps，再乘以 Speed；循环模式支持 循环 / 往返 / 单次。
    /// </summary>
    public class AnimPlayer : MonoBehaviour
    {
        public Sprite[] Frames;
        public float Fps = 12f;
        public float Speed = 1f;
        public string LoopMode = "loop";   // loop | pingpong | once
        public int LoopCount = 0;          // 0 = 无限
        public bool PingSkip = true;       // 往返时跳过重复端点

        private Image _img;
        private float _t;
        private int _step;
        private int _cycles;
        private int _last = -1;
        private bool _done;

        public int FrameCount => Frames != null ? Frames.Length : 0;
        public bool IsAnimated => Frames != null && Frames.Length > 1;

        /// <summary>一个完整循环的步数（往返时是往复一圈）。</summary>
        public int CycleSteps
        {
            get
            {
                int n = FrameCount;
                if (n < 2) return 0;
                if (LoopMode != "pingpong") return n;
                return PingSkip ? Mathf.Max(1, 2 * n - 2) : 2 * n;
            }
        }

        private void Awake()
        {
            _img = GetComponent<Image>();
            ResetPlayback();
        }

        private void OnEnable()
        {
            ResetPlayback();
        }

        public void ResetPlayback()
        {
            _t = 0f;
            _step = 0;
            _cycles = 0;
            _last = -1;
            _done = false;
            ApplySprite(0);
        }

        private void Update()
        {
            if (_img == null || !IsAnimated || Fps <= 0f || _done) return;

            float dt = Time.unscaledDeltaTime * Mathf.Max(0.01f, Speed);
            _t += dt;

            float frameDur = 1f / Fps;
            int guard = 0;
            while (_t >= frameDur && guard++ < 240)
            {
                _t -= frameDur;
                Step();
                if (_done) break;
            }

            ApplySprite(_step);
        }

        private void Step()
        {
            int L = CycleSteps;
            if (L <= 0) return;

            _step++;
            if (_step < L) return;

            _step = 0;
            _cycles++;

            if (LoopMode == "once") { _done = true; _step = L - 1; return; }
            if (LoopCount > 0 && _cycles >= LoopCount) { _done = true; _step = L - 1; }
        }

        private void ApplySprite(int step)
        {
            if (!IsAnimated) return;
            int idx = IndexAt(step);
            if (idx == _last) return;
            _last = idx;
            _img.sprite = Frames[idx];
        }

        private int IndexAt(int step)
        {
            int n = FrameCount;
            if (n < 2) return 0;

            if (LoopMode != "pingpong") return step % n;

            int L = CycleSteps;
            if (L <= 0) return 0;
            step %= L;

            if (!PingSkip)
            {
                // 0..n-1 然后 n-1..0：折返处与首帧都会重复一次
                if (step < n) return step;
                int back = 2 * n - step;   // step = n -> n, n+1 -> n-1 ... 2n-1 -> 1
                return Mathf.Clamp(back, 0, n - 1);
            }

            // 跳过重复端点：0..n-1 然后 n-2..1
            if (step < n) return step;
            int k = L - step;              // step = n -> n-2, ... L-1 -> 1
            return Mathf.Clamp(k, 1, n - 2);
        }
    }
}
