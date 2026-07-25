using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// True inventory chromatic aberration via on-demand screen-capture snapshot.
    ///
    /// The inventory stays a normal Screen Space - Overlay canvas — fully interactive and untouched,
    /// so closing / dragging / clicking always work. A separate raycast-transparent overlay shows a
    /// captured + chromatic snapshot of the panel region on top of it.
    ///
    /// True chromatic: the snapshot is the fully-composited panel, run through the chromatic shader
    /// ("HUD Module PostProcess Clean V32") which shifts the whole image's R/B channels, so flat
    /// areas get genuine red/cyan fringing at real edges.
    ///
    /// Why "on-demand": ScreenCapture grabs the final screen, which would include this very overlay
    /// and feed back into itself (smear) if done every frame. Instead the snapshot is refreshed on a
    /// throttle; for that single capture frame the overlay is hidden so the capture stays clean. At a
    /// low chromatic amount the missing fringe on that one frame is imperceptible. Between refreshes
    /// the snapshot simply follows the panel's screen rect, so window dragging stays smooth.
    ///
    /// Place this on InventoryPanel (the direct child of the window root).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonInventoryChromatic : MonoBehaviour
    {
        [SerializeField] public Material sourceMaterial; // Clean V32 chromatic material
        [SerializeField, Range(0f, 1f)] public float chromaticIntensity = 0.2f;
        [SerializeField, Range(0f, 1f)] public float chromaticSoftness  = 1f;

        [Tooltip("静止时快照刷新间隔（秒）。背包基本静止，间隔大些更省、闪动更少。")]
        [SerializeField] public float refreshInterval = 0.4f;

        [Tooltip("运动时（相机移动/拖窗）快照刷新间隔（秒）。0=每帧重抓（最跟手，拖窗无偏移）。")]
        [SerializeField] public float motionRefreshInterval = 0f;

        [Tooltip("D3D/Windows 下 ScreenCapture 通常上下翻转，保持勾选。如果画面上下颠倒就取消。")]
        [SerializeField] public bool flipY = true;


        private Canvas        _overlayCanvas;
        private RawImage      _overlayImage;
        private Material      _outputMaterial;
        private RenderTexture _captureRT;
        private RectTransform _panelRect;
        private CanvasGroup   _canvasGroup; // root fade group; chromatic only shows when fully open
        private float         _nextCaptureTime;
        private Rect          _capturedUv; // panel region in the capture, fixed until next capture

        // ── Motion tracking ───────────────────────────────────────────────────
        // 快照只在「完全静止」时才有意义；画面一动（相机移动 / 拖窗）就会慢半拍。
        // 运动期间隐藏快照、露出底下即时刷新的真面板，停下后立刻重抓恢复色收差。
        private Camera        _trackedCam;
        private Vector3       _lastCamPos;
        private Quaternion    _lastCamRot;
        private Rect          _lastPanelScreenRect;
        private bool          _motionInited;
        private bool          _isMoving;

        // 缓存 GetWorldCorners 用的数组，避免 LateUpdate 每帧 new Vector3[4]
        private readonly Vector3[] _corners = new Vector3[4];

        // 外部交互（如拖拽物品）期间临时挂起静止快照，避免残影。
        public bool SuspendForInteraction { get; set; }

        // 全局挂起：UI 动画（开关窗口、货币滚动等）期间停止周期性 ScreenCapture，
        // 改走实时模糊层，避免抓屏卡顿打断动画。引用计数，支持多处叠加。
        private static int _globalSuspend;
        public static void PushGlobalSuspend() => _globalSuspend++;
        public static void PopGlobalSuspend() => _globalSuspend = Mathf.Max(0, _globalSuspend - 1);

        private const float FullyVisibleAlpha = 0.99f;
        // 之前这两个阈值严到 0.0001 单位 / 0.01 度——任何跟随相机的呼吸感摆动、
        // 平滑跟随算法逼近目标点时残留的浮点噪声，随随便便就能每帧超过这个门槛，
        // 让 DetectMotion() 一直判定"在动"，即使玩家完全站着不动、镜头看起来也很
        // 稳定。这就是"窗口一直忽明忽暗"的真正原因：不是 GC 抖动（上一轮猜的），
        // 是这个运动检测本身对相机的正常静止抖动太敏感，一直在快照层/实时磨砂层
        // 之间来回切。放宽到能容忍正常静止抖动、又能真正抓到有意义相机移动的量级。
        private const float CamPosEpsilon     = 0.01f;
        private const float CamRotEpsilon     = 0.3f;   // degrees
        private const float PanelMoveEpsilon  = 0.5f;     // screen pixels

        private static readonly int PropMainTex         = Shader.PropertyToID("_MainTex");
        private static readonly int PropChromaticAmount = Shader.PropertyToID("_ChromaticAmount");
        private static readonly int PropChromaticSoft   = Shader.PropertyToID("_ChromaticSoftness");

        public void Configure(Material mat, float intensity, float softness)
        {
            sourceMaterial     = mat;
            chromaticIntensity = intensity;
            chromaticSoftness  = softness;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            _panelRect = GetComponent<RectTransform>();
            Canvas rootCanvas = GetComponentInParent<Canvas>();
            _canvasGroup = rootCanvas != null
                ? rootCanvas.rootCanvas.GetComponent<CanvasGroup>()
                : GetComponentInParent<CanvasGroup>();

            BuildOverlay();
            _nextCaptureTime = 0f;
            _motionInited    = false; // 重新建立运动基准，避免开窗瞬间误判
            _isMoving        = false;
            _trackedCam      = null;
            StartCoroutine(CapturePump());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            TearDown();
        }

        private void OnDestroy() => TearDown();

        // Move the snapshot with the window every frame (uvRect stays fixed from capture time, so
        // the captured panel image translates rigidly instead of resampling the stale background).
        private void LateUpdate()
        {
            if (_overlayImage == null) return;

            // 面板盖住底部提示条时，把提示条移到顶部躲开（拖开移回底部）。
            // 判定基于「固定底部矩形」，不随提示条当前位置变，避免来回抖。
            DriveHintBarEdge();

            // Hide the chromatic overlay whenever the window is mid fade (open/close), so the smooth
            // CanvasGroup fade is done by the real inventory alone. The opaque snapshot only shows
            // in the steady, fully-visible state — otherwise it pops/stutters against the fade.
            if (!IsFullyVisible())
            {
                if (_overlayImage.enabled) _overlayImage.enabled = false;
                return;
            }

            // 运动期间藏起 chromatic 快照，露出底下实时磨砂层（已对齐、跟手、无延迟、零抓屏开销），
            // 停下后立刻重抓恢复色收差。比运动时每帧 ScreenCapture 省得多，消除拖动卡顿。
            // 拖拽物品等面板内交互期间也按「运动」处理：藏起静止快照、露出实时磨砂层，
            // 否则快照只按 refreshInterval 刷新，会把移动中的物品留在旧位置形成残影。
            _isMoving = DetectMotion() || SuspendForInteraction || _globalSuspend > 0;
            if (_isMoving)
            {
                if (_overlayImage.enabled) _overlayImage.enabled = false;
                _nextCaptureTime = 0f;
                return;
            }

            if (!_overlayImage.enabled) return;
            FollowPanel();
            ApplyChromaticParams();
        }

        private bool IsFullyVisible() => _canvasGroup == null || _canvasGroup.alpha >= FullyVisibleAlpha;

        // 比较本帧与上帧的主相机位姿和面板屏幕矩形，任一变化即判定为运动。
        // 首帧只记录基准、不判运动，避免开窗瞬间误判。
        private bool DetectMotion()
        {
            if (_trackedCam == null) _trackedCam = Camera.main;

            Vector3    camPos = _trackedCam != null ? _trackedCam.transform.position : Vector3.zero;
            Quaternion camRot = _trackedCam != null ? _trackedCam.transform.rotation : Quaternion.identity;

            Rect panelRect = default;
            if (_panelRect != null)
            {
                _panelRect.GetWorldCorners(_corners);
                panelRect = new Rect(_corners[0].x, _corners[0].y, _corners[2].x - _corners[0].x, _corners[2].y - _corners[0].y);
            }

            if (!_motionInited)
            {
                _lastCamPos = camPos; _lastCamRot = camRot; _lastPanelScreenRect = panelRect;
                _motionInited = true;
                return false;
            }

            bool byCamPos = (camPos - _lastCamPos).sqrMagnitude > CamPosEpsilon * CamPosEpsilon;
            bool byCamRot = Quaternion.Angle(camRot, _lastCamRot) > CamRotEpsilon;
            bool byRectX  = Mathf.Abs(panelRect.x - _lastPanelScreenRect.x) > PanelMoveEpsilon;
            bool byRectY  = Mathf.Abs(panelRect.y - _lastPanelScreenRect.y) > PanelMoveEpsilon;
            bool byRectW  = Mathf.Abs(panelRect.width  - _lastPanelScreenRect.width)  > PanelMoveEpsilon;
            bool byRectH  = Mathf.Abs(panelRect.height - _lastPanelScreenRect.height) > PanelMoveEpsilon;
            bool moved = byCamPos || byCamRot || byRectX || byRectY || byRectW || byRectH;

            _lastCamPos = camPos; _lastCamRot = camRot; _lastPanelScreenRect = panelRect;
            return moved;
        }

        // ── Build ────────────────────────────────────────────────────────────

        private void BuildOverlay()
        {
            int w = Mathf.Max(8, Screen.width);
            int h = Mathf.Max(8, Screen.height);
            _captureRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name       = "RT_InvChromatic_Cap",
                hideFlags  = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp
            };
            _captureRT.Create();
            // 清空 RT，避免显示上一次窗口关闭时残留的旧截图（如死亡特效冷绿色）
            var prev = RenderTexture.active;
            RenderTexture.active = _captureRT;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;

            var go = new GameObject("[InvChromatic] Overlay", typeof(RectTransform))
                { hideFlags = HideFlags.HideAndDontSave };
            _overlayCanvas              = go.AddComponent<Canvas>();
            _overlayCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = 1101; // just above the inventory (1100)
            go.AddComponent<CanvasScaler>();

            var imgGo = new GameObject("ChromaticOutput", typeof(RectTransform))
                { hideFlags = HideFlags.HideAndDontSave };
            imgGo.transform.SetParent(go.transform, false);
            var rt = imgGo.GetComponent<RectTransform>();
            rt.anchorMin        = Vector2.zero;
            rt.anchorMax        = Vector2.zero;
            rt.pivot            = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = Vector2.zero;

            _overlayImage               = imgGo.AddComponent<RawImage>();
            _overlayImage.texture       = _captureRT;
            _overlayImage.raycastTarget = false;
            _overlayImage.color         = Color.white;

            if (sourceMaterial != null)
            {
                _outputMaterial = new Material(sourceMaterial) { hideFlags = HideFlags.HideAndDontSave };
                // URP writes alpha=0 to the screen, so the captured RT has alpha 0 everywhere.
                // Force One/Zero (full replace) so the snapshot shows regardless of alpha.
                if (_outputMaterial.HasProperty("_SrcBlend"))
                    _outputMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                if (_outputMaterial.HasProperty("_DstBlend"))
                    _outputMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                // sourceMaterial（M_Window_ModulePostProcess_Auto）是战斗 HUD 那套后处理
                // 共用的资产，上面 _HUDContrast=1.37 / _HUDSaturation=1.49 是给战斗 HUD
                // 调的浓烈参数——这里只克隆了材质，没有重置这几个值，背包窗口的快照
                // 因此被连带套上了战斗 HUD 的对比度/饱和度，看起来又暗又"烧"。这里
                // 强制归中性直通值，背包窗口只要色收差本身的效果，不要战斗 HUD 那套调色。
                if (_outputMaterial.HasProperty("_HUDContrast"))
                    _outputMaterial.SetFloat("_HUDContrast", 1f);
                if (_outputMaterial.HasProperty("_HUDSaturation"))
                    _outputMaterial.SetFloat("_HUDSaturation", 1f);
                if (_outputMaterial.HasProperty("_HUDBrightness"))
                    _outputMaterial.SetFloat("_HUDBrightness", 1f);
                if (_outputMaterial.HasProperty("_HUDEmission"))
                    _outputMaterial.SetFloat("_HUDEmission", 0f);
                if (_outputMaterial.HasProperty("_HUDSourceFloor"))
                    _outputMaterial.SetFloat("_HUDSourceFloor", 0f);
                _outputMaterial.SetTexture(PropMainTex, _captureRT);
                _overlayImage.material = _outputMaterial;
            }

            ApplyChromaticParams();
            _overlayImage.enabled = false; // hidden until first capture
        }

        // ── Capture pump ─────────────────────────────────────────────────────

        private IEnumerator CapturePump()
        {
            // 等待若干帧，让死亡特效（DeathGlitch/vignette）完全消失后再截屏，
            // 避免复活后立刻开背包时把冷绿色特效固化进 chromatic 快照。
            for (int i = 0; i < 6; i++) yield return null;

            while (true)
            {
                // Don't capture/show while the window is fading (open/close): the capture would grab a
                // half-faded panel and the opaque overlay would fight the fade. Wait until fully open.
                if (Time.unscaledTime >= _nextCaptureTime && IsFullyVisible() && !_isMoving)
                {
                    // Hide the overlay BEFORE this frame renders, so the capture taken at the end of
                    // the frame does not include the overlay (no feedback smear).
                    if (_overlayImage != null) _overlayImage.enabled = false;
                    // 自定义鼠标图标同理隐藏——不然截图那一刻的鼠标位置会被烤进快照，
                    // 之后跟实时移动的真实光标叠在一起变成残影。
                    SkyPrison.Runtime.UI.SkyPrisonCustomCursor.SetSuspendedForCapture(true);

                    yield return new WaitForEndOfFrame();
                    if (_captureRT == null)
                    {
                        SkyPrison.Runtime.UI.SkyPrisonCustomCursor.SetSuspendedForCapture(false);
                        yield break;
                    }

                    ScreenCapture.CaptureScreenshotIntoRenderTexture(_captureRT);
                    ApplyChromaticParams();
                    SkyPrison.Runtime.UI.SkyPrisonCustomCursor.SetSuspendedForCapture(false);

                    // Lock the uvRect to where the panel was at capture time.
                    ComputeCapturedUv();

                    if (_overlayImage != null)
                    {
                        FollowPanel();
                        _overlayImage.enabled = true;
                    }
                    // 运动时用更短的间隔重抓，让快照跟手；静止时回到正常节流。
                    float interval = _isMoving ? motionRefreshInterval : refreshInterval;
                    _nextCaptureTime = Time.unscaledTime + Mathf.Max(0f, interval);
                }
                else
                {
                    yield return null;
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // 之前这里只查背包自己这一个面板盖没盖住提示条——角色信息面板那边是另一套独立
        // 判断，两边各查各的、谁后调用就覆盖谁的结果，导致提示条卡在错误位置切不过去。
        // 现在统一走 SkyPrisonWindowOverlapGuard 那个"查所有已注册窗口"的入口，不管
        // 哪个窗口的 Update 调用，结果都一致。
        private void DriveHintBarEdge()
        {
            SkyPrisonWindowOverlapGuard.RefreshHintBarEdgeForAllWindows();
        }

        private void ApplyChromaticParams()
        {
            if (_outputMaterial == null) return;
            if (_outputMaterial.HasProperty(PropChromaticAmount))
                _outputMaterial.SetFloat(PropChromaticAmount, Mathf.Clamp01(chromaticIntensity) * 10f);
            if (_outputMaterial.HasProperty(PropChromaticSoft))
                _outputMaterial.SetFloat(PropChromaticSoft, Mathf.Clamp01(chromaticSoftness));
        }

        // Compute (at capture time) which region of the full-screen capture holds the panel,
        // flipping V for D3D's upside-down capture. Kept fixed until the next capture.
        private void ComputeCapturedUv()
        {
            if (_panelRect == null) return;

            _panelRect.GetWorldCorners(_corners);
            var bl = new Vector2(_corners[0].x, _corners[0].y);
            var tr = new Vector2(_corners[2].x, _corners[2].y);

            float sw = Screen.width, sh = Screen.height;
            if (sw <= 0f || sh <= 0f) return;

            float u0 = bl.x / sw, du = (tr.x - bl.x) / sw;
            float v0 = bl.y / sh, dv = (tr.y - bl.y) / sh;

            _capturedUv = flipY
                ? new Rect(u0, 1f - v0, du, -dv)
                : new Rect(u0, v0, du, dv);
        }

        // Move/size the overlay to the panel's CURRENT screen rect, keeping the fixed capture uvRect
        // so the snapshot moves as a rigid block with the window (no half-beat resampling lag).
        private void FollowPanel()
        {
            if (_overlayImage == null || _panelRect == null) return;

            _panelRect.GetWorldCorners(_corners);
            var bl = new Vector2(_corners[0].x, _corners[0].y);
            var tr = new Vector2(_corners[2].x, _corners[2].y);

            RectTransform ort = _overlayImage.rectTransform;
            ort.anchoredPosition = bl;
            ort.sizeDelta        = tr - bl;
            _overlayImage.uvRect = _capturedUv;
        }

        // ── Teardown ─────────────────────────────────────────────────────────

        private void TearDown()
        {
            SkyPrisonWindowHintBar.SetEdgeTop(false); // 复位提示条到底部
            if (_overlayCanvas != null) { Destroy(_overlayCanvas.gameObject); _overlayCanvas = null; _overlayImage = null; }
            if (_outputMaterial != null) { Destroy(_outputMaterial); _outputMaterial = null; }
            if (_captureRT != null) { _captureRT.Release(); Destroy(_captureRT); _captureRT = null; }
        }
    }
}
