using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    // 悬停用的"真"色收差——跟 SkyPrisonInventoryChromatic（整窗口用，持续刷新截屏）
    // 同一套原理：截屏→喂进色收差 shader→叠加显示裁切出来的那一小块。区别是这里只在
    // 鼠标"刚悬停上"那一刻截一次全屏（不持续刷新，元素本身不会在悬停期间移动/变化），
    // 离开就隐藏，成本远低于窗口级快照。全项目共用一份 RT/Canvas/材质，同一时刻只会
    // 有一个东西在被悬停，不需要每个用到的地方各自建一份。
    public static class SkyPrisonHoverChromaticSnapshot
    {
        private static SkyPrisonHoverChromaticSnapshotRunner _runner;

        public static void Show(RectTransform target, Sprite maskSprite = null)
        {
            EnsureRunner();
            _runner.Show(target, maskSprite);
        }

        public static void Hide()
        {
            if (_runner != null) _runner.HideOverlay();
        }

        private static void EnsureRunner()
        {
            if (_runner != null) return;
            var go = new GameObject("[HoverChromatic] Shared") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<SkyPrisonHoverChromaticSnapshotRunner>();
        }
    }

    internal sealed class SkyPrisonHoverChromaticSnapshotRunner : MonoBehaviour
    {
        private Canvas _overlayCanvas;
        private RectTransform _maskRt;
        private Image _maskImage;
        private RawImage _overlayImage;
        private Material _outputMaterial;
        private RenderTexture _captureRT;
        private RectTransform _target;
        private Coroutine _captureRoutine;
        private readonly Vector3[] _corners = new Vector3[4];

        private static readonly int PropMainTex         = Shader.PropertyToID("_MainTex");
        private static readonly int PropChromaticAmount = Shader.PropertyToID("_ChromaticAmount");
        private static readonly int PropChromaticSoft   = Shader.PropertyToID("_ChromaticSoftness");

        // 直接给固定像素量——这是个小图标级别的悬停反馈，不需要再配一层0-1强度换算。
        private const float ChromaticPixelAmount = 3.5f;
        private const float ChromaticSoftness = 1f;

        private void EnsureBuilt()
        {
            if (_overlayCanvas != null) return;

            int w = Mathf.Max(8, Screen.width);
            int h = Mathf.Max(8, Screen.height);
            _captureRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name       = "RT_HoverChromatic_Cap",
                hideFlags  = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp
            };
            _captureRT.Create();

            var go = new GameObject("Overlay", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            _overlayCanvas = go.AddComponent<Canvas>();
            _overlayCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = 32750; // 盖过窗口内容，即时悬停反馈要在最上层
            go.AddComponent<CanvasScaler>();

            // 截出来的是目标RectTransform的整个矩形包围盒，但像菱形边框这种图标只在
            // 这个矩形里占了一个旋转菱形的范围，四角是透明的——直接铺一个不透明矩形
            // RawImage会把四角本该透出背景的地方也糊成一块实心方块（"露馅"）。加一层
            // UI Mask，拿跟原图标同一张贴图（自带菱形alpha）当模板，只让RawImage在
            // 菱形轮廓内可见，四角依然透明。
            var maskGo = new GameObject("Mask", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            maskGo.transform.SetParent(go.transform, false);
            _maskRt = maskGo.GetComponent<RectTransform>();
            _maskRt.anchorMin = Vector2.zero;
            _maskRt.anchorMax = Vector2.zero;
            _maskRt.pivot     = Vector2.zero;
            _maskImage = maskGo.AddComponent<Image>();
            _maskImage.raycastTarget = false;
            var mask = maskGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var imgGo = new GameObject("ChromaticOutput", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            imgGo.transform.SetParent(maskGo.transform, false);
            var rt = imgGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _overlayImage = imgGo.AddComponent<RawImage>();
            _overlayImage.texture       = _captureRT;
            _overlayImage.raycastTarget = false;
            _overlayImage.color         = Color.white;

            var shader = Shader.Find("Sky Prison/UI/HUD Module PostProcess Clean V32");
            if (shader != null)
            {
                _outputMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                // URP 写屏幕 alpha=0，截出来的RT alpha 全是0——强制 One/Zero 全量替换，
                // 不然截图整个不显示（跟 SkyPrisonInventoryChromatic 里同样的坑）。
                if (_outputMaterial.HasProperty("_SrcBlend"))
                    _outputMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                if (_outputMaterial.HasProperty("_DstBlend"))
                    _outputMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                // 这个材质是战斗 HUD 后处理公用的资产，自带浓烈对比度/饱和度调值——
                // 悬停小图标只要色收差本身，归中性直通值。
                if (_outputMaterial.HasProperty("_HUDContrast")) _outputMaterial.SetFloat("_HUDContrast", 1f);
                if (_outputMaterial.HasProperty("_HUDSaturation")) _outputMaterial.SetFloat("_HUDSaturation", 1f);
                if (_outputMaterial.HasProperty("_HUDBrightness")) _outputMaterial.SetFloat("_HUDBrightness", 1f);
                if (_outputMaterial.HasProperty("_HUDEmission")) _outputMaterial.SetFloat("_HUDEmission", 0f);
                if (_outputMaterial.HasProperty("_HUDSourceFloor")) _outputMaterial.SetFloat("_HUDSourceFloor", 0f);
                if (_outputMaterial.HasProperty(PropChromaticAmount)) _outputMaterial.SetFloat(PropChromaticAmount, ChromaticPixelAmount);
                if (_outputMaterial.HasProperty(PropChromaticSoft)) _outputMaterial.SetFloat(PropChromaticSoft, ChromaticSoftness);
                _outputMaterial.SetTexture(PropMainTex, _captureRT);
                _overlayImage.material = _outputMaterial;
            }

            _overlayImage.enabled = false;
        }

        public void Show(RectTransform target, Sprite maskSprite)
        {
            EnsureBuilt();
            _target = target;
            if (_maskImage != null) _maskImage.sprite = maskSprite; // null=不裁剪，铺满整个矩形
            if (_captureRoutine != null) StopCoroutine(_captureRoutine);
            _captureRoutine = StartCoroutine(CaptureAndShow());
        }

        public void HideOverlay()
        {
            if (_captureRoutine != null) { StopCoroutine(_captureRoutine); _captureRoutine = null; }
            if (_overlayImage != null) _overlayImage.enabled = false;
            _target = null;
        }

        // 截屏含"这一帧屏幕上已经画好的一切"，包括这个overlay自己——先关掉overlay
        // 再等一帧结尾截屏，避免截到自己形成反馈残影（跟主系统同样的处理）。
        private IEnumerator CaptureAndShow()
        {
            if (_overlayImage != null) _overlayImage.enabled = false;
            yield return new WaitForEndOfFrame();
            if (_captureRT == null || _target == null) yield break;

            ScreenCapture.CaptureScreenshotIntoRenderTexture(_captureRT);

            _target.GetWorldCorners(_corners);
            var bl = new Vector2(_corners[0].x, _corners[0].y);
            var tr = new Vector2(_corners[2].x, _corners[2].y);
            float sw = Screen.width, sh = Screen.height;
            if (sw <= 0f || sh <= 0f) yield break;

            float u0 = bl.x / sw, du = (tr.x - bl.x) / sw;
            float v0 = bl.y / sh, dv = (tr.y - bl.y) / sh;
            Rect uv = new Rect(u0, 1f - v0, du, -dv); // D3D 截屏上下翻转，跟主系统一致处理

            if (_overlayImage != null && _maskRt != null)
            {
                _overlayImage.uvRect = uv;
                _maskRt.anchoredPosition = bl;
                _maskRt.sizeDelta        = tr - bl;
                _overlayImage.enabled = true;
            }
            _captureRoutine = null;
        }

        private void OnDestroy()
        {
            if (_outputMaterial != null) Object.Destroy(_outputMaterial);
            if (_captureRT != null) { _captureRT.Release(); Object.Destroy(_captureRT); }
        }
    }
}
