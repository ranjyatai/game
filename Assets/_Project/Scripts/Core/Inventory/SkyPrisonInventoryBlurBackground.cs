using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 动态磨砂背景：辅助相机实时渲染 → LateUpdate 用 CommandBuffer 做降采样 + Gaussian + 升采样。
    /// Screen Space Overlay Canvas 不会被辅助相机捕获，无需隐藏面板，无闪烁。
    ///
    /// 摄像机/RT/降采样-模糊处理全部是全局共享的一份（见 SkyPrisonSharedUIBlurRunner），
    /// 不是每个窗口各建一台——之前每个窗口各自建一台摄像机，两个窗口同时开着时，第二台
    /// 摄像机创建的那一帧会让第一台已经在正常渲染的摄像机丢帧，表现为"开新窗口的瞬间，
    /// 另一个窗口的磨砂背景整个消失一两帧、露出清晰彩色场景"。改成全局只有一台摄像机、
    /// 引用计数管理生命周期，就不存在"创建第二台摄像机"这个会撞车的时间点了。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonInventoryBlurBackground : MonoBehaviour
    {
        [SerializeField] private RawImage      blurImage;
        [SerializeField] private RectTransform panelRect;

        // ── 可调参数（Inspector / Workbench 暴露）────────────────────────
        // 这几个参数现在实际控制的是全局共享的那一份处理链，不再是"各窗口各自一份"——
        // 多个窗口同时开着时用的是同一套模糊强度/饱和度/亮度，属性读写转发到共享处。
        [Header("Blur")]
        [SerializeField, Range(1, 8), Tooltip("降采样级数：越大越模糊（6=推荐）")]
        private int blurHalfSteps = 6;

        [Header("Color")]
        [SerializeField, Range(0f, 1f), Tooltip("0=完全黑白，1=原色")]
        private float saturation = 0f;

        [SerializeField, Range(0f, 1f), Tooltip("磨砂背景亮度。1=原始亮度，越小越暗（场景太亮时压暗用）")]
        private float brightness = 0.5f;

        // Workbench 直接读写
        public int   BlurHalfSteps { get => blurHalfSteps; set { blurHalfSteps = value; SkyPrisonSharedUIBlurRunner.BlurHalfSteps = value; } }
        public float Saturation    { get => saturation;    set { saturation = value; SkyPrisonSharedUIBlurRunner.Saturation = value; } }
        public float Brightness    { get => brightness;    set { brightness = value; ApplyBrightness(); } }

        private void ApplyBrightness()
        {
            if (blurImage == null) return;
            float b = Mathf.Clamp01(brightness);
            blurImage.color = new Color(b, b, b, 1f);
        }

        // 灰度材质：赋给 blurImage，让模糊背景以黑白显示。全局共享同一份材质实例，不要
        // 每个窗口各自 new 一份——之前每个窗口各建一份自己的 Desaturate 材质实例，背包
        // 窗口开出来的那一刻要为它自己那份材质第一次编译 shader 变体，编译期间同一个
        // shader 的所有材质（包括角色面板已经在用的那份）都可能短暂回退成默认渲染。
        private static Material _desatMat;
        internal static Material GetDesatMat()
        {
            if (_desatMat != null) return _desatMat;
            var s = Shader.Find("UI/SkyPrison/Desaturate");
            if (s == null) { Debug.LogWarning("[UIBlur] Desaturate shader not found"); return null; }
            _desatMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            _desatMat.SetFloat("_Saturation", 0f); // 完全黑白
            return _desatMat;
        }

        public void SetReferences(RawImage img, RectTransform panel)
        {
            blurImage = img;
            panelRect = panel;
        }

        private Coroutine _delayedSetupRoutine;
        private bool _acquired;

        private void OnEnable()
        {
            // RawImage 在贴图还没赋值之前用 Unity 内置纯白贴图渲染，表现成"开窗瞬间白屏
            // 闪一下"——先禁用，等共享摄像机真正有内容了再显示。
            if (blurImage != null) blurImage.enabled = false;
            _delayedSetupRoutine = StartCoroutine(AcquireNextFrame());
        }

        private IEnumerator AcquireNextFrame()
        {
            yield return null;
            if (blurImage == null) yield break;

            SkyPrisonSharedUIBlurRunner.Acquire();
            _acquired = true;

            var dm = GetDesatMat();
            if (dm != null) blurImage.material = dm;
            blurImage.texture = SkyPrisonSharedUIBlurRunner.OutputTexture;
            blurImage.uvRect  = new Rect(0f, 0f, 1f, 1f);
            ApplyBrightness();
            blurImage.enabled = true;
        }

        private void OnDisable()
        {
            if (_delayedSetupRoutine != null) { StopCoroutine(_delayedSetupRoutine); _delayedSetupRoutine = null; }
            if (_acquired) { SkyPrisonSharedUIBlurRunner.Release(); _acquired = false; }
            if (blurImage != null) blurImage.material = null; // 恢复默认 UI 材质
        }

        private void OnDestroy() => OnDisable();
    }

    /// <summary>
    /// 全局共享的磨砂摄像机/RT/降采样-高斯模糊处理链，引用计数管理生命周期。
    /// 挂在一个常驻的隐藏 GameObject 上，不随任何具体窗口的开关而创建/销毁——只有当
    /// 「当前没有任何窗口在用」时才真正销毁摄像机和 RT，避免出现"第一个窗口关闭时把
    /// 还在用的共享摄像机也拆了"的问题。
    /// </summary>
    internal static class SkyPrisonSharedUIBlurRunner
    {
        private static SkyPrisonSharedUIBlurRunnerBehaviour _runner;
        private static int _refCount;

        public static int   BlurHalfSteps = 6;
        public static float Saturation    = 0f;

        public static Texture OutputTexture => _runner != null ? (Texture)_runner.OutputRT : null;

        public static void Acquire()
        {
            _refCount++;
            if (_runner == null)
            {
                var go = new GameObject("[UIBlur] Shared") { hideFlags = HideFlags.HideAndDontSave };
                _runner = go.AddComponent<SkyPrisonSharedUIBlurRunnerBehaviour>();
                _runner.SetupCamera();
            }
        }

        public static void Release()
        {
            _refCount = Mathf.Max(0, _refCount - 1);
            if (_refCount == 0 && _runner != null)
            {
                Object.Destroy(_runner.gameObject);
                _runner = null;
            }
        }
    }

    [AddComponentMenu("")]
    internal sealed class SkyPrisonSharedUIBlurRunnerBehaviour : MonoBehaviour
    {
        private Camera        _blurCam;
        private RenderTexture _sourceRT;
        private RenderTexture _outputRT;
        public  RenderTexture OutputRT => _outputRT;

        private int _srcW, _srcH, _outW, _outH;
        private const int SrcLongEdge = 960;
        private const int MinBlurDim = 64;
        private const RenderTextureFormat Fmt = RenderTextureFormat.DefaultHDR;

        private float _lastSaturation = -1f;
        private readonly List<RenderTexture> _temps = new List<RenderTexture>();

        private static Material _blurMat;
        private static Material GetBlurMat()
        {
            if (_blurMat != null) return _blurMat;
            var s = Shader.Find("Hidden/SkyPrison/UIFreezeBlur");
            if (s == null) return null;
            _blurMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            return _blurMat;
        }

        private void ComputeResolutions()
        {
            float aspect = (float)Mathf.Max(1, Screen.width) / Mathf.Max(1, Screen.height);
            if (aspect >= 1f) { _srcW = SrcLongEdge; _srcH = Mathf.Max(64, Mathf.RoundToInt(SrcLongEdge / aspect)); }
            else              { _srcH = SrcLongEdge; _srcW = Mathf.Max(64, Mathf.RoundToInt(SrcLongEdge * aspect)); }
            _outW = Mathf.Max(64, _srcW / 2);
            _outH = Mathf.Max(64, _srcH / 2);
        }

        private static RenderTexture MakeRT(int w, int h, string n) =>
            new RenderTexture(w, h, 0, Fmt)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
                name       = n,
                hideFlags  = HideFlags.HideAndDontSave
            };

        public void SetupCamera()
        {
            var mainCam = Camera.main;
            if (mainCam == null) { Debug.LogWarning("[UIBlur] 未找到 Main Camera"); return; }

            ComputeResolutions();
            _sourceRT = MakeRT(_srcW, _srcH, "SkyPrison_UIBlur_Source");
            _outputRT = MakeRT(_outW, _outH, "SkyPrison_UIBlur_Output");

            var camGo = new GameObject("[UIBlur] Camera") { hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.SetParent(transform, false);
            _blurCam = camGo.AddComponent<Camera>();
            _blurCam.CopyFrom(mainCam);
            _blurCam.depth         = mainCam.depth - 1;
            _blurCam.targetTexture = _sourceRT;
            // 显式钉死取景比例 = 屏幕比例，避免 CopyFrom/targetTexture 下 aspect 不确定导致取景框
            // 与主相机不一致（横向拉伸/缩放），那正是磨砂图和真实场景对不上的根源。
            _blurCam.aspect  = (float)_srcW / _srcH;
            _blurCam.enabled = true;

            var urp = _blurCam.GetUniversalAdditionalCameraData();
            if (urp != null) { urp.renderType = CameraRenderType.Base; urp.renderShadows = false; }

            StartCoroutine(SyncLoop(mainCam));
        }

        private IEnumerator SyncLoop(Camera mainCam)
        {
            while (_blurCam != null && mainCam != null)
            {
                var t = mainCam.transform;
                _blurCam.transform.SetPositionAndRotation(t.position, t.rotation);
                _blurCam.orthographicSize = mainCam.orthographicSize;
                _blurCam.fieldOfView      = mainCam.fieldOfView;
                yield return null;
            }
        }

        // ── LateUpdate：完整模糊链 ────────────────────────────────────────
        // 辅助相机 depth 更低，渲染先于主相机完成，此处读取上帧（1帧延迟，不可感知）。
        private void LateUpdate()
        {
            if (_sourceRT == null || _outputRT == null) return;

            float saturation = SkyPrisonSharedUIBlurRunner.Saturation;
            if (!Mathf.Approximately(saturation, _lastSaturation))
            {
                var dm = SkyPrisonInventoryBlurBackground.GetDesatMat();
                if (dm != null) dm.SetFloat("_Saturation", saturation);
                _lastSaturation = saturation;
            }

            int blurHalfSteps = Mathf.Clamp(SkyPrisonSharedUIBlurRunner.BlurHalfSteps, 1, 8);

            _temps.Clear();
            var cmd = CommandBufferPool.Get("UIBlur");

            try
            {
                RenderTexture cur = _sourceRT;
                int cw = _srcW, ch = _srcH;

                int downSteps = 0;
                for (int i = 0; i < blurHalfSteps; i++)
                {
                    int nw = cw / 2;
                    int nh = ch / 2;
                    if (nw < MinBlurDim || nh < MinBlurDim) break;
                    var rt = RenderTexture.GetTemporary(nw, nh, 0, Fmt);
                    rt.filterMode = FilterMode.Bilinear;
                    _temps.Add(rt);
                    cmd.Blit(cur, rt);
                    cur = rt; cw = nw; ch = nh; downSteps++;
                }

                var blurMat = GetBlurMat();
                if (blurMat != null)
                {
                    blurMat.SetFloat("_UIBlur_TexelSizeX", 1f / cw);
                    blurMat.SetFloat("_UIBlur_TexelSizeY", 1f / ch);
                    blurMat.SetFloat("_UIBlur_Radius", 3.0f);

                    var a = RenderTexture.GetTemporary(cw, ch, 0, Fmt); a.filterMode = FilterMode.Bilinear; _temps.Add(a);
                    var b = RenderTexture.GetTemporary(cw, ch, 0, Fmt); b.filterMode = FilterMode.Bilinear; _temps.Add(b);

                    int iterations = Mathf.Max(1, blurHalfSteps);
                    RenderTexture src = cur;
                    for (int it = 0; it < iterations; it++)
                    {
                        cmd.Blit(src, a, blurMat, 1);
                        cmd.Blit(a,   b, blurMat, 2);
                        src = b;
                    }
                    cur = src;
                }

                for (int i = 0; i < downSteps; i++)
                {
                    var rt = RenderTexture.GetTemporary(cw * 2, ch * 2, 0, Fmt);
                    rt.filterMode = FilterMode.Bilinear;
                    _temps.Add(rt);
                    cmd.Blit(cur, rt);
                    cur = rt; cw *= 2; ch *= 2;
                }

                cmd.Blit(cur, _outputRT);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
                foreach (var t in _temps) RenderTexture.ReleaseTemporary(t);
            }
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            if (_sourceRT != null) { _sourceRT.Release(); _sourceRT = null; }
            if (_outputRT != null) { _outputRT.Release(); _outputRT = null; }
        }
    }
}
