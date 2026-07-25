using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// Window-level chromatic aberration via RT pipeline.
    /// Switches the root Canvas to Screen Space - Camera, renders it into a RenderTexture,
    /// then composites the result through the chromatic shader in a top-level SS Overlay canvas.
    /// Compatible with SkyPrisonInventoryBlurBackground (the blur camera still runs normally;
    /// this driver's canvas camera captures the canvas AFTER the blur RawImage has been populated).
    /// Place this component on the window root GameObject that has the Canvas.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class SkyPrisonWindowChromaticRTDriver : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] public float chromaticIntensity = 0.2f;
        [SerializeField, Range(0f, 1f)] public float chromaticSoftness = 1f;
        [SerializeField] public Material sourceMaterial;

        private Canvas _targetCanvas;
        private Camera _rtCamera;
        private GameObject _rtCameraGo;
        private RenderTexture _rt;
        private Canvas _outputCanvas;
        private RawImage _outputImage;
        private Material _outputMaterial;

        private RenderMode _originalRenderMode;
        private Camera _originalWorldCamera;
        private float _originalPlaneDistance;

        private bool _setup;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            SetupRTPipeline();
        }

        private void OnDisable()
        {
            TearDown();
        }

        private void OnDestroy()
        {
            TearDown();
        }

        private void LateUpdate()
        {
            if (!_setup) return;

            if (_outputMaterial != null)
            {
                float pixels = Mathf.Clamp01(chromaticIntensity) * 10f;
                float soft = Mathf.Clamp01(chromaticSoftness);
                if (_outputMaterial.HasProperty("_ChromaticAmount"))
                    _outputMaterial.SetFloat("_ChromaticAmount", pixels);
                if (_outputMaterial.HasProperty("_ChromaticSoftness"))
                    _outputMaterial.SetFloat("_ChromaticSoftness", soft);
            }

            // The window manager fades via CanvasGroup.alpha on the root.
            // In SS Camera mode the canvas renders into the RT — a CanvasGroup alpha=0
            // makes the canvas transparent in the RT, so the output overlay shows nothing
            // during the fade-in, then pops in abruptly.
            // Fix: intercept the CanvasGroup alpha every frame, keep the canvas at full
            // opacity so the RT always has valid content, and apply the fade to the output
            // image instead so the visual result is identical.
            CanvasGroup cg = GetComponent<CanvasGroup>();
            if (cg != null && _outputImage != null)
            {
                float a = cg.alpha;
                cg.alpha = 1f;
                _outputImage.color = new Color(1f, 1f, 1f, a);
            }
        }

        public void Configure(Material material, float intensity, float softness)
        {
            sourceMaterial = material;
            chromaticIntensity = intensity;
            chromaticSoftness = softness;
        }

        private void SetupRTPipeline()
        {
            if (_setup) return;
            _targetCanvas = GetComponent<Canvas>();
            if (_targetCanvas == null) return;

            _originalRenderMode = _targetCanvas.renderMode;
            _originalWorldCamera = _targetCanvas.worldCamera;
            _originalPlaneDistance = _targetCanvas.planeDistance;

            int w = Mathf.Max(8, Screen.width);
            int h = Mathf.Max(8, Screen.height);
            _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = $"RT_WindowChromatic_{name}",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            _rtCameraGo = new GameObject($"[WindowChromatic] Cam_{name}") { hideFlags = HideFlags.HideAndDontSave };
            _rtCamera = _rtCameraGo.AddComponent<Camera>();
            _rtCamera.clearFlags = CameraClearFlags.SolidColor;
            _rtCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _rtCamera.cullingMask = 0;
            _rtCamera.targetTexture = _rt;
            _rtCamera.orthographic = true;
            _rtCamera.orthographicSize = h * 0.5f;
            _rtCamera.nearClipPlane = -100f;
            _rtCamera.farClipPlane = 100f;
            _rtCamera.depth = -100f;
            _rtCamera.allowHDR = false;
            _rtCamera.allowMSAA = false;

            var urp = _rtCamera.GetUniversalAdditionalCameraData();
            if (urp != null)
            {
                urp.renderType = CameraRenderType.Base;
                urp.renderShadows = false;
                urp.requiresColorOption = CameraOverrideOption.Off;
                urp.requiresDepthOption = CameraOverrideOption.Off;
            }

            _targetCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _targetCanvas.worldCamera = _rtCamera;
            _targetCanvas.planeDistance = 1f;

            int originalSortOrder = _targetCanvas.sortingOrder;

            var outputGo = new GameObject($"[WindowChromatic] Output_{name}") { hideFlags = HideFlags.HideAndDontSave };
            _outputCanvas = outputGo.AddComponent<Canvas>();
            _outputCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _outputCanvas.sortingOrder = originalSortOrder;
            outputGo.AddComponent<CanvasScaler>();

            var imgGo = new GameObject("ChromaticOutput");
            imgGo.transform.SetParent(outputGo.transform, false);
            var rect = imgGo.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            _outputImage = imgGo.AddComponent<RawImage>();
            _outputImage.texture = _rt;
            _outputImage.raycastTarget = false;
            _outputImage.color = Color.white;

            if (sourceMaterial != null)
            {
                _outputMaterial = new Material(sourceMaterial) { hideFlags = HideFlags.HideAndDontSave };
                _outputMaterial.SetFloat("_ChromaticAmount", Mathf.Clamp01(chromaticIntensity) * 10f);
                _outputMaterial.SetFloat("_ChromaticSoftness", Mathf.Clamp01(chromaticSoftness));
                _outputImage.material = _outputMaterial;
            }

            _setup = true;
        }

        private void TearDown()
        {
            if (!_setup) return;
            _setup = false;

            if (_targetCanvas != null)
            {
                _targetCanvas.renderMode = _originalRenderMode;
                _targetCanvas.worldCamera = _originalWorldCamera;
                _targetCanvas.planeDistance = _originalPlaneDistance;
            }
            if (_rtCameraGo != null) { Destroy(_rtCameraGo); _rtCameraGo = null; _rtCamera = null; }
            if (_outputCanvas != null) { Destroy(_outputCanvas.gameObject); _outputCanvas = null; _outputImage = null; }
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
            if (_outputMaterial != null) { Destroy(_outputMaterial); _outputMaterial = null; }
        }
    }
}
