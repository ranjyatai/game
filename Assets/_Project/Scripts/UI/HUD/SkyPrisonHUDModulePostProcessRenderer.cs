using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// V106: runtime-only module post-process renderer with post-process padding and HUD internal camera tag isolation.
    /// This component must not run inside the UI workbench / prefab editing stage, because the RT route
    /// creates generated RawImage/SourceRoot/Camera objects and can pollute or recursively render the PF.
    /// The UI workbench only stores runtime settings; runtime drivers may enable this component in Play Mode.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonHUDModulePostProcessRenderer_V87 : MonoBehaviour
    {
        private static bool CanMutateForPostProcess => Application.isPlaying;
        private const string SourceRootName = "__SkyPrisonHUDModuleRTSource_V87";
        private const string OutputName = "__ModulePostProcessOutput_V87";
        private const string CameraName = "__SkyPrisonHUDModuleRTCamera_V87";
        private const string QuickPromptOverlayName = "QuickItemPromptOverlay";
        private const string QuickPromptPrefix = "QuickItemPrompt_";
        // V103: this shader derives its alpha mask from RGB luminance, not the real alpha channel
        // (URP17 render-graph bakes alpha=1 into cleared RT background pixels, so real source alpha
        // is unusable inside the RT — see SkyPrisonHUDModulePostProcess.shader's frag() comment).
        // That means anything captured into the chromatic RT can never render as genuinely
        // translucent against the live scene — a light/bright flat panel always reads near-opaque
        // regardless of its Image alpha. "CleanBG_" objects are excluded from the RT capture (same
        // mechanism as QuickItemPrompt overlays) so they draw with normal, correct alpha blending;
        // they must be positioned as siblings under the module root, not nested inside sourceRoot.
        private const string CleanBackgroundPrefix = "CleanBG_";
        // V104: same exclusion as CleanBG_, but for overlays that must render ON TOP of the
        // chromatic output (e.g. a cooldown fill covering an icon) instead of behind it —
        // CleanBG_ is always forced to first-sibling (back); CleanFG_ is forced to last-sibling
        // (front), same placement rule as the QuickItemPrompt overlay.
        private const string CleanForegroundPrefix = "CleanFG_";
        private const string DisabledStalePrefix = "__DisabledStale_";
        private const int MinRTSize = 8;
        private const int MaxRTSize = 4096;
        private const int MinPostProcessPaddingPixels = 0;
        private const int MaxPostProcessPaddingPixels = 96;
        private static readonly Vector3 OffscreenWorldBasePosition = new Vector3(100000f, 100000f, 0f);

        [SerializeField] private RectTransform displayRoot;
        [SerializeField] private RectTransform sourceRoot;
        [SerializeField] private Canvas sourceCanvas;
        [SerializeField] private Camera sourceCamera;
        [SerializeField] private RawImage outputImage;
        [SerializeField] private RenderTexture moduleRT;
        [SerializeField] private Material outputMaterial;
        [SerializeField] private Material sourceMaterial;
        [SerializeField] private SkyPrisonHUDBlendModeV1 blendMode = SkyPrisonHUDBlendModeV1.Normal;
        [SerializeField] private Vector2Int rtSize;
        [SerializeField, Min(0)] private int postProcessPaddingPixels = 24;

        // V94 diagnostics: source UI must stay live after being moved into the hidden RT canvas.
        [SerializeField] private bool runtimeLiveBindingRefreshed;
        [SerializeField] private int runtimeLiveBindingViewCount;
        [SerializeField] private string runtimeLiveBindingStatus = "Not refreshed";
        [SerializeField] private SkyPrisonPlayerHUDView_V4_StyleDriven runtimeBridgePlayerHUDView;
        [SerializeField] private bool runtimeBridgePlayerHUDViewCreated;
        [SerializeField] private int disabledSourcePlayerHUDViewCount;
        [SerializeField] private string playerHUDViewAuthorityStatus = "Not resolved";

        public static bool ShouldUseModulePostProcess(Material material, SkyPrisonHUDBlendModeV1 mode)
        {
            if (mode == SkyPrisonHUDBlendModeV1.SoftLight || mode == SkyPrisonHUDBlendModeV1.HardLight)
                return true;

            if (material == null || material.shader == null)
                return false;

            string shaderName = material.shader.name;
            return !string.IsNullOrEmpty(shaderName) && shaderName.Contains("HUD Chromatic");
        }

        public static SkyPrisonHUDModulePostProcessRenderer_V87 Attach(GameObject moduleRoot, Material postProcessSourceMaterial, SkyPrisonHUDBlendModeV1 mode)
        {
            if (moduleRoot == null)
                return null;

            SkyPrisonHUDModulePostProcessRenderer_V87 renderer = moduleRoot.GetComponent<SkyPrisonHUDModulePostProcessRenderer_V87>();
            if (renderer == null)
                renderer = moduleRoot.AddComponent<SkyPrisonHUDModulePostProcessRenderer_V87>();

            renderer.Configure(postProcessSourceMaterial, mode);
            if (CanMutateForPostProcess)
                renderer.BuildOrRebuild();
            return renderer;
        }

        public void Configure(Material postProcessSourceMaterial, SkyPrisonHUDBlendModeV1 mode)
        {
            sourceMaterial = postProcessSourceMaterial;
            blendMode = mode;
        }

        private void OnEnable()
        {
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;

            if (!CanMutateForPostProcess)
                return;

            // Bail out early if no source material is configured — renderer was added to the PF
            // but never properly set up (e.g. workbench saved with shouldEnable=false).
            // Keep content in place instead of creating SourceRoot / grey output RawImage.
            if (sourceMaterial == null)
            {
                enabled = false;
                return;
            }

            BuildOrRebuild();
        }

        private void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            // Keep the source hierarchy intact in edit mode. Only release volatile render targets.
            DeactivateSourceCameraAndCanvas();
            ReleaseRTOnly();
        }

        private void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            ReleaseAllGeneratedObjects();
        }


        // Alpha 修复已移入 Output shader（SkyPrisonHUDModulePostProcess.shader）的 frag 函数。
        // URP 17 render-graph 模式下，在此回调中调用 Graphics.Blit 会损坏 RT 内容（竞态）。
        // OnEndCameraRendering 现在只做轻量状态检查，不做任何 Blit。
        private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            // no-op: alpha correction is now handled in the output shader frag
        }


        private void LateUpdate()
        {
            if (!CanMutateForPostProcess)
                return;

            // V93:
            // Keep the source hierarchy live. Children may be moved to sourceRoot after the component
            // is built; when that happens, PlayerHUDView references must be rebound to the source tree.
            bool movedChildren = MoveExistingVisualChildrenToSource();
            DisableMasksInSourceRoot();
            DisableSourceQuickPromptOverlays();
            BringDisplayQuickPromptOverlayToFront();
            if (movedChildren || !runtimeLiveBindingRefreshed)
                RefreshLiveRuntimeBindingsToSource();

            HideStrayDisplayGraphicsExceptOutput();

            if (sourceCamera == null || moduleRT == null)
                return;

            EnsureSize();
            EnsureOutputMaterial();
            DisableSourceQuickPromptOverlays();
            BringDisplayQuickPromptOverlayToFront();
            RefreshSourceCameraBinding();

            // The RT output is not a baked snapshot. Force UI layout before the enabled source camera
            // captures the live source canvas for this frame.
            Canvas.ForceUpdateCanvases();
        }

        private void BuildOrRebuild()
        {
            displayRoot = transform as RectTransform;
            if (displayRoot == null)
                return;

            EnsureOutputImage();
            EnsureSourceCanvasAndCamera();
            MoveExistingVisualChildrenToSource();
            DisableSourceQuickPromptOverlays();
            BringDisplayQuickPromptOverlayToFront();
            RefreshLiveRuntimeBindingsToSource();
            SyncSourceLayer();
            EnsureSize();
            EnsureOutputMaterial();
            RefreshSourceCameraBinding();
        }

        private void EnsureOutputImage()
        {
            if (outputImage != null)
                return;

            Transform existing = transform.Find(OutputName);
            GameObject outputObject = existing != null ? existing.gameObject : new GameObject(OutputName, typeof(RectTransform));
            outputObject.transform.SetParent(transform, false);
            outputObject.layer = gameObject.layer;

            RectTransform outputRect = outputObject.GetComponent<RectTransform>();
            outputRect.anchorMin = Vector2.zero;
            outputRect.anchorMax = Vector2.one;
            outputRect.pivot = new Vector2(0.5f, 0.5f);
            outputRect.anchoredPosition = Vector2.zero;
            outputRect.sizeDelta = Vector2.zero;
            outputRect.localScale = Vector3.one;

            outputImage = outputObject.GetComponent<RawImage>();
            if (outputImage == null)
                outputImage = outputObject.AddComponent<RawImage>();
            outputImage.raycastTarget = false;
            outputImage.color = Color.white;
            outputObject.transform.SetAsLastSibling();
        }

        private void EnsureSourceCanvasAndCamera()
        {
            if (sourceRoot != null && sourceCanvas != null && sourceCamera != null)
                return;

            GameObject sourceObject = new GameObject($"{SourceRootName}_{name}", typeof(RectTransform));
            sourceObject.hideFlags = HideFlags.DontSave;
            sourceObject.layer = gameObject.layer;

            RectTransform sourceRect = sourceObject.GetComponent<RectTransform>();
            sourceRect.anchorMin = Vector2.zero;
            sourceRect.anchorMax = Vector2.zero;
            sourceRect.pivot = new Vector2(0.5f, 0.5f);
            sourceRect.anchoredPosition = Vector2.zero;
            sourceRect.localScale = Vector3.one;
            sourceRoot = sourceRect;

            sourceCanvas = sourceObject.AddComponent<Canvas>();
            sourceCanvas.renderMode = RenderMode.WorldSpace;
            sourceCanvas.pixelPerfect = false;
            sourceCanvas.overrideSorting = true;
            sourceCanvas.sortingOrder = 0;

            CanvasScaler scaler = sourceObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            GameObject cameraObject = new GameObject($"{CameraName}_{name}", typeof(Camera));
            cameraObject.hideFlags = HideFlags.DontSave;
            if (cameraObject.GetComponent<SkyPrisonHUDInternalRenderCameraTag>() == null)
                cameraObject.AddComponent<SkyPrisonHUDInternalRenderCameraTag>();
            sourceCamera = cameraObject.GetComponent<Camera>();
            sourceCamera.enabled = true;
            sourceCamera.clearFlags = CameraClearFlags.SolidColor;
            sourceCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            sourceCamera.orthographic = true;
            sourceCamera.nearClipPlane = -100f;
            sourceCamera.farClipPlane = 100f;
            sourceCamera.cullingMask = 1 << gameObject.layer;
            sourceCamera.allowHDR = false;
            sourceCamera.allowMSAA = false;

            var urpData = sourceCamera.GetUniversalAdditionalCameraData();
            if (urpData != null)
            {
                urpData.renderType    = CameraRenderType.Base;
                urpData.renderShadows = false;
                urpData.requiresColorOption = CameraOverrideOption.On;
                urpData.requiresDepthOption = CameraOverrideOption.Off;
                urpData.antialiasing = AntialiasingMode.None;
            }

            sourceCanvas.worldCamera = sourceCamera;
            ResetOffscreenSourceTransform();
        }

        private bool MoveExistingVisualChildrenToSource()
        {
            if (sourceRoot == null)
                return false;

            bool movedAny = false;

            // V107: snapshot the child list before touching sibling order. SetAsFirstSibling/
            // SetAsLastSibling mutate the live hierarchy, and re-reading transform.GetChild(i)
            // on each loop turn after such a call can skip or double-visit children as indices
            // shift underneath the loop — with 2+ special-cased objects (CleanBG_, CleanFG_,
            // QuickItemPrompt) this reliably let some of them fall through unprocessed and get
            // swept into sourceRoot like ordinary content (re-entering the chromatic RT capture,
            // which is exactly the "background looks dark/opaque again" symptom this caused).
            var children = new List<Transform>(transform.childCount);
            for (int i = 0; i < transform.childCount; i++)
                children.Add(transform.GetChild(i));

            // Move every already-built visual child under the offscreen source canvas.
            // Keep the output RawImage under the displayed module root.
            // These are not copies. They are the live UI objects that PlayerHUDView updates.
            for (int i = children.Count - 1; i >= 0; i--)
            {
                Transform child = children[i];
                if (child == null || child == outputImage.transform)
                    continue;
                if (child.name == OutputName)
                    continue;

                // V102:
                // Quick item prompt icons are a clean, display-side overlay. They must share the
                // QuickSlotsArea module coordinate space, but they must not be moved into the RT source.
                // If they enter sourceRoot, they are captured by the chromatic shader and appear with
                // red/cyan fringes underneath the clean overlay.
                //
                // V103: CleanBG_ objects are the same kind of display-side exclusion, but they are a
                // *background* layer, not an overlay — they must stay BEHIND the chromatic output
                // (first sibling), not in front of it like the QuickItemPrompt overlay.
                if (child.name.StartsWith(CleanBackgroundPrefix, System.StringComparison.Ordinal))
                {
                    child.SetAsFirstSibling();
                    continue;
                }

                if (IsQuickPromptOverlayOrChild(child))
                {
                    child.SetAsLastSibling();
                    continue;
                }

                child.SetParent(sourceRoot, false);
                movedAny = true;
            }

            if (movedAny)
                DisableMasksInSourceRoot();

            return movedAny;
        }

        // Mask components rely on stencil buffer write order that breaks in the offscreen
        // WorldSpace canvas. Disable them so content renders without stencil clipping.
        // The RT dimensions and outputImage rect provide equivalent content cropping.
        private void DisableMasksInSourceRoot()
        {
            if (sourceRoot == null) return;
            foreach (var mask in sourceRoot.GetComponentsInChildren<UnityEngine.UI.Mask>(true))
                mask.enabled = false;
        }

        private void RefreshLiveRuntimeBindingsToSource()
        {
            runtimeLiveBindingRefreshed = false;
            runtimeLiveBindingViewCount = 0;
            runtimeLiveBindingStatus = "No source root";
            runtimeBridgePlayerHUDViewCreated = false;
            disabledSourcePlayerHUDViewCount = 0;
            playerHUDViewAuthorityStatus = "No source root";

            if (sourceRoot == null)
                return;

            // V96:
            // There must be exactly one active PlayerHUDView authority for this module.
            //
            // Before V96 both the displayed bridge and the moved source PlayerHUDView could tick the same
            // source graphics. The binder drives the bridge, but the old source view can still Update()
            // and restore/overwrite LP penalty colors, causing high-frequency flashing.
            //
            // Authority rule:
            // - The displayed PlayerStatusArea bridge is the only enabled PlayerHUDView.
            // - It is bound to sourceRoot and drives the live source UI.
            // - Any PlayerHUDView found under sourceRoot is disabled so it cannot compete.
            SkyPrisonPlayerHUDView_V4_StyleDriven bridgeView = EnsureDisplayedPlayerHUDViewBridge();

            if (bridgeView == null)
            {
                runtimeLiveBindingStatus = "Bridge PlayerHUDView missing";
                playerHUDViewAuthorityStatus = "No bridge authority";
                return;
            }

            bridgeView.enabled = true;
            bridgeView.EnsurePreviewBindingsFrom(sourceRoot);
            runtimeBridgePlayerHUDView = bridgeView;
            runtimeBridgePlayerHUDViewCreated = true;
            runtimeLiveBindingViewCount = 1;

            DisableCompetingSourcePlayerHUDViews(bridgeView);

            runtimeLiveBindingRefreshed = true;
            runtimeLiveBindingStatus = $"Bridge drives RT source; disabled source HUD views={disabledSourcePlayerHUDViewCount}";
            playerHUDViewAuthorityStatus = disabledSourcePlayerHUDViewCount > 0
                ? "Single authority: displayed bridge enabled, source views disabled"
                : "Single authority: displayed bridge enabled, no source competitors";
        }

        private void DisableCompetingSourcePlayerHUDViews(SkyPrisonPlayerHUDView_V4_StyleDriven bridgeView)
        {
            disabledSourcePlayerHUDViewCount = 0;

            if (sourceRoot == null)
                return;

            SkyPrisonPlayerHUDView_V4_StyleDriven[] sourceViews =
                sourceRoot.GetComponentsInChildren<SkyPrisonPlayerHUDView_V4_StyleDriven>(true);

            for (int i = 0; i < sourceViews.Length; i++)
            {
                SkyPrisonPlayerHUDView_V4_StyleDriven view = sourceViews[i];
                if (view == null || view == bridgeView)
                    continue;

                if (view.enabled)
                {
                    view.enabled = false;
                    disabledSourcePlayerHUDViewCount++;
                }
            }
        }



        private SkyPrisonPlayerHUDView_V4_StyleDriven EnsureDisplayedPlayerHUDViewBridge()
        {
            // If the displayed module root already has a PlayerHUDView, use it.
            SkyPrisonPlayerHUDView_V4_StyleDriven direct = GetComponent<SkyPrisonPlayerHUDView_V4_StyleDriven>();
            if (direct != null)
                return direct;

            // Do not use GetComponentInChildren here: child views may have already moved to sourceRoot.
            // The bridge must live on the displayed module root so external binders can find it under
            // SkyPrisonBattleHUD_Runtime.
            SkyPrisonPlayerHUDView_V4_StyleDriven bridge = gameObject.AddComponent<SkyPrisonPlayerHUDView_V4_StyleDriven>();
            return bridge;
        }



        private void HideStrayDisplayGraphicsExceptOutput()
        {
            if (displayRoot == null)
                displayRoot = transform as RectTransform;
            if (displayRoot == null || outputImage == null)
                return;

            Graphic[] graphics = displayRoot.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || graphic == outputImage)
                    continue;

                // V102: the quick prompt overlay is intentionally a display-side clean overlay.
                // Keep it visible above the post-process RawImage and do not cull it as a stray graphic.
                if (IsQuickPromptOverlayOrChild(graphic.transform))
                {
                    CanvasRenderer promptRenderer = graphic.canvasRenderer;
                    if (promptRenderer != null)
                        promptRenderer.cull = false;
                    BringDisplayQuickPromptOverlayToFront();
                    continue;
                }

                // OutputName is the only visible child allowed under the displayed module root,
                // except explicitly whitelisted clean overlays.
                if (graphic.transform == outputImage.transform || graphic.transform.IsChildOf(outputImage.transform))
                    continue;

                CanvasRenderer renderer = graphic.canvasRenderer;
                if (renderer != null)
                    renderer.cull = true;
            }
        }


        private static bool IsQuickPromptOverlayOrChild(Transform transformToCheck)
        {
            Transform cursor = transformToCheck;
            while (cursor != null)
            {
                if (IsQuickPromptObjectName(cursor.name))
                    return true;
                cursor = cursor.parent;
            }

            return false;
        }

        private static bool IsQuickPromptObjectName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return false;

            return objectName == QuickPromptOverlayName
                || objectName.Contains(QuickPromptOverlayName)
                || objectName.StartsWith(QuickPromptPrefix, System.StringComparison.Ordinal)
                || objectName.StartsWith(CleanBackgroundPrefix, System.StringComparison.Ordinal)
                || objectName.StartsWith(CleanForegroundPrefix, System.StringComparison.Ordinal)
                || objectName.StartsWith(DisabledStalePrefix + QuickPromptOverlayName, System.StringComparison.Ordinal)
                || objectName.StartsWith(DisabledStalePrefix + QuickPromptPrefix, System.StringComparison.Ordinal)
                || objectName.StartsWith(DisabledStalePrefix + CleanBackgroundPrefix, System.StringComparison.Ordinal)
                || objectName.StartsWith(DisabledStalePrefix + CleanForegroundPrefix, System.StringComparison.Ordinal);
        }

        private void BringDisplayQuickPromptOverlayToFront()
        {
            if (displayRoot == null)
                displayRoot = transform as RectTransform;
            if (displayRoot == null)
                return;

            Transform overlay = displayRoot.Find(QuickPromptOverlayName);
            if (overlay == null)
                return;

            GameObject overlayObject = overlay.gameObject;
            if (!overlayObject.activeSelf)
                overlayObject.SetActive(true);

            overlay.SetAsLastSibling();

            Graphic[] graphics = overlay.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null)
                    continue;

                CanvasRenderer renderer = graphic.canvasRenderer;
                if (renderer != null)
                    renderer.cull = false;
            }
        }

        private void DisableSourceQuickPromptOverlays()
        {
            // V106:
            // QuickItemPromptOverlay is a display-side clean overlay.  It must never be part of any
            // offscreen RT source.  Older workbench/runtime versions may leave detached hidden source
            // roots in the scene, and the serialized sourceRoot field does not necessarily point at
            // every stale root.  Therefore this cleanup scans all loaded Transform objects for
            // __SkyPrisonHUDModuleRTSource_V87 roots and suppresses any prompt overlay/prompt icons
            // found inside them.  Never Destroy here: this renderer may tick while Unity is rebuilding
            // canvases or serializing prefabs.
            List<Transform> roots = new List<Transform>();
            if (sourceRoot != null)
                roots.Add(sourceRoot);

            Transform[] allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform candidate = allTransforms[i];
                if (candidate == null)
                    continue;
                if (!candidate.name.Contains(SourceRootName))
                    continue;

                GameObject go = candidate.gameObject;
                if (go == null)
                    continue;

                // Ignore persistent project assets; only touch scene/runtime objects.
                if (!go.scene.IsValid())
                    continue;

                if (!roots.Contains(candidate))
                    roots.Add(candidate);
            }

            for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                Transform root = roots[rootIndex];
                if (root == null)
                    continue;

                Transform[] children = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < children.Length; i++)
                {
                    Transform child = children[i];
                    if (child == null || child == root)
                        continue;

                    if (!IsQuickPromptObjectName(child.name))
                        continue;

                    SuppressQuickPromptInSource(child);
                }
            }
        }

        private void SuppressQuickPromptInSource(Transform promptTransform)
        {
            if (promptTransform == null)
                return;

            GameObject promptObject = promptTransform.gameObject;
            promptObject.SetActive(false);
            if (!promptObject.name.StartsWith(DisabledStalePrefix, System.StringComparison.Ordinal))
                promptObject.name = DisabledStalePrefix + promptObject.name;

            // Put stale prompt objects on a layer the module RT camera will not render.
            // Screen-space display UI does not rely on this layer, but the WorldSpace RT source camera does.
            SetLayerRecursive(promptObject, 0);

            Canvas[] canvases = promptObject.GetComponentsInChildren<Canvas>(true);
            for (int c = 0; c < canvases.Length; c++)
            {
                if (canvases[c] != null)
                    canvases[c].enabled = false;
            }

            Graphic[] graphics = promptObject.GetComponentsInChildren<Graphic>(true);
            for (int g = 0; g < graphics.Length; g++)
            {
                Graphic graphic = graphics[g];
                if (graphic == null)
                    continue;

                graphic.enabled = false;
                graphic.material = null;
                CanvasRenderer renderer = graphic.canvasRenderer;
                if (renderer != null)
                    renderer.cull = true;
            }
        }

        private void SyncSourceLayer()
        {
            int layer = gameObject.layer;
            if (sourceRoot != null)
                SetLayerRecursive(sourceRoot.gameObject, layer);
            if (sourceCamera != null)
            {
                sourceCamera.cullingMask = 1 << layer;
                if (sourceCamera.GetComponent<SkyPrisonHUDInternalRenderCameraTag>() == null)
                    sourceCamera.gameObject.AddComponent<SkyPrisonHUDInternalRenderCameraTag>();
            }
        }

        private void RefreshSourceCameraBinding()
        {
            if (sourceRoot == null || sourceCanvas == null || sourceCamera == null || moduleRT == null)
                return;

            // V87: do NOT call Camera.Render() here.
            // In URP/Unity 6, manual Camera.Render from ExecuteAlways can repeatedly touch the URP Blitter
            // and throw "Blitter is already initialized". Let the enabled offscreen camera render normally.
            if (!sourceRoot.gameObject.activeSelf)
                sourceRoot.gameObject.SetActive(true);
            if (!sourceCamera.gameObject.activeSelf)
                sourceCamera.gameObject.SetActive(true);

            ResetOffscreenSourceTransform();
            sourceCamera.enabled = true;
            sourceCanvas.enabled = true;
            sourceCanvas.worldCamera = sourceCamera;
            sourceCamera.targetTexture = moduleRT;

            Canvas.ForceUpdateCanvases();
        }

        private void DeactivateSourceCameraAndCanvas()
        {
            if (sourceCamera != null)
            {
                sourceCamera.enabled = false;
                sourceCamera.targetTexture = null;
            }

            if (sourceCanvas != null)
                sourceCanvas.enabled = false;

            if (sourceRoot != null && sourceRoot.gameObject.activeSelf)
                sourceRoot.gameObject.SetActive(false);
        }

        private Vector3 GetOffscreenWorldPosition()
        {
            int id = Mathf.Abs(GetInstanceID());
            int column = id % 64;
            int row = (id / 64) % 64;
            return OffscreenWorldBasePosition + new Vector3(column * 10000f, row * 10000f, 0f);
        }

        private void ResetOffscreenSourceTransform()
        {
            Vector3 offscreenWorldPosition = GetOffscreenWorldPosition();

            if (sourceRoot != null)
            {
                sourceRoot.SetParent(null, true);
                sourceRoot.position = offscreenWorldPosition;
                sourceRoot.rotation = Quaternion.identity;
                sourceRoot.localScale = Vector3.one;
                sourceRoot.anchorMin = Vector2.zero;
                sourceRoot.anchorMax = Vector2.zero;
                sourceRoot.pivot = new Vector2(0.5f, 0.5f);
            }

            if (sourceCamera != null)
            {
                sourceCamera.transform.position = offscreenWorldPosition + new Vector3(0f, 0f, -1f);
                sourceCamera.transform.rotation = Quaternion.identity;
                sourceCamera.orthographic = true;
                sourceCamera.nearClipPlane = -100f;
                sourceCamera.farClipPlane = 100f;
                sourceCamera.clearFlags = CameraClearFlags.SolidColor;
                sourceCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null || layer < 0)
                return;
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i).gameObject, layer);
        }

        private void EnsureSize()
        {
            if (displayRoot == null)
                displayRoot = transform as RectTransform;
            if (displayRoot == null)
                return;

            Rect rect = displayRoot.rect;
            int contentWidth = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1f, rect.width)), MinRTSize, MaxRTSize);
            int contentHeight = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1f, rect.height)), MinRTSize, MaxRTSize);
            int padding = EvaluatePostProcessPaddingPixels();

            int width = Mathf.Clamp(contentWidth + padding * 2, MinRTSize, MaxRTSize);
            int height = Mathf.Clamp(contentHeight + padding * 2, MinRTSize, MaxRTSize);
            Vector2Int wanted = new Vector2Int(width, height);

            if (moduleRT == null || rtSize != wanted)
            {
                ReleaseRTOnly();
                rtSize = wanted;
                moduleRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = $"RT_HUDModule_{name}_V87",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                moduleRT.Create();
            }

            // Keep the offscreen source canvas at the real module size.
            // The padding is camera/RT bleed area only; changing the source canvas size would
            // change RectTransform stretch children and desync the runtime PF layout.
            if (sourceRoot != null)
            {
                sourceRoot.sizeDelta = new Vector2(contentWidth, contentHeight);
                sourceRoot.anchoredPosition = Vector2.zero;
            }

            if (sourceCamera != null)
            {
                sourceCamera.targetTexture = moduleRT;
                sourceCamera.pixelRect = new Rect(0f, 0f, width, height);
                sourceCamera.aspect = width / Mathf.Max(1f, (float)height);
                sourceCamera.orthographicSize = height * 0.5f;
            }

            if (outputImage != null)
            {
                RectTransform outputRect = outputImage.rectTransform;
                outputRect.anchorMin = Vector2.zero;
                outputRect.anchorMax = Vector2.one;
                outputRect.pivot = new Vector2(0.5f, 0.5f);
                outputRect.anchoredPosition = Vector2.zero;
                outputRect.sizeDelta = new Vector2(padding * 2f, padding * 2f);
                outputRect.localScale = Vector3.one;
                outputImage.texture = moduleRT;
                outputImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            }

            ResetOffscreenSourceTransform();

            if (outputMaterial != null && outputMaterial.HasProperty("_MainTex"))
                outputMaterial.SetTexture("_MainTex", moduleRT);
        }

        private int EvaluatePostProcessPaddingPixels()
        {
            int padding = Mathf.Clamp(postProcessPaddingPixels, MinPostProcessPaddingPixels, MaxPostProcessPaddingPixels);

            // Chromatic shaders shift samples outside the source bounds.  If the material exposes
            // a pixel-like amount, reserve at least that much bleed space plus a small safety margin.
            if (sourceMaterial != null && sourceMaterial.HasProperty("_ChromaticAmount"))
            {
                float amount = Mathf.Abs(sourceMaterial.GetFloat("_ChromaticAmount"));
                if (amount > 0.001f)
                    padding = Mathf.Max(padding, Mathf.CeilToInt(amount) + 8);
            }

            if (sourceMaterial != null && sourceMaterial.HasProperty("_GeometryFringeWidth"))
            {
                float fringeWidth = Mathf.Abs(sourceMaterial.GetFloat("_GeometryFringeWidth"));
                if (fringeWidth > 0.001f)
                    padding = Mathf.Max(padding, Mathf.CeilToInt(fringeWidth) + 4);
            }

            return Mathf.Clamp(padding, MinPostProcessPaddingPixels, MaxPostProcessPaddingPixels);
        }

        private void EnsureOutputMaterial()
        {
            if (outputImage == null)
                return;

            if (outputMaterial == null)
            {
                Shader shader = Shader.Find("Sky Prison/UI/HUD Module PostProcess Clean V32");
                if (shader == null)
                    shader = Shader.Find("Sky Prison/UI/HUD Module PostProcess V87");
                if (shader == null)
                    shader = Shader.Find("UI/Default");
                outputMaterial = shader != null ? new Material(shader) : null;
                if (outputMaterial != null)
                {
                    outputMaterial.name = $"M_HUDModulePostProcess_{name}_V87";
                    outputMaterial.hideFlags = HideFlags.HideAndDontSave;
                }
            }

            if (outputMaterial == null)
                return;

            CopyPostProcessProperties(sourceMaterial, outputMaterial);
            ConfigureBlendMaterial(outputMaterial, blendMode);

            if (moduleRT != null && outputMaterial.HasProperty("_MainTex"))
                outputMaterial.SetTexture("_MainTex", moduleRT);

            // Compute screen-pixel correction so _ChromaticAmount means "screen pixels"
            // regardless of RT resolution or canvas scale (important on 4K displays).
            if (outputMaterial.HasProperty("_ScreenPixelScale") && moduleRT != null && displayRoot != null)
            {
                var canvas = GetComponentInParent<Canvas>();
                float scaleFactor = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
                float displayPx = displayRoot.rect.width * scaleFactor;
                float scale = displayPx > 0f ? (float)moduleRT.width / displayPx : 1f;
                outputMaterial.SetFloat("_ScreenPixelScale", scale);
            }

            outputImage.material = outputMaterial;
        }

        private static void CopyPostProcessProperties(Material source, Material target)
        {
            if (source == null || target == null)
                return;

            CopyFloat(source, target, "_ChromaticAmount");
            CopyFloat(source, target, "_ChromaticAngle");
            CopyFloat(source, target, "_ChromaticSoftness");
            CopyFloat(source, target, "_ChromaticAlphaBoost");
            CopyFloat(source, target, "_GeometryFringeStrength");
            CopyFloat(source, target, "_GeometryFringeWidth");
            CopyFloat(source, target, "_GeometryFringeOutputBoost");
            CopyFloat(source, target, "_GeometryFringeDebug");
            CopyFloat(source, target, "_HUDBrightness");
            CopyFloat(source, target, "_HUDContrast");
            CopyFloat(source, target, "_HUDEmission");
            CopyFloat(source, target, "_HUDLightStrength");
            CopyFloat(source, target, "_HUDSourcePreserve");
            CopyFloat(source, target, "_HUDSourceFloor");
            CopyFloat(source, target, "_HUDSaturation");
            CopyFloat(source, target, "_UsePSBackdropRecipe");
            CopyFloat(source, target, "_BackdropSampleStrength");
            CopyFloat(source, target, "_BackdropExposure");
            CopyColor(source, target, "_BackdropFallbackColor");
        }

        private static void ConfigureBlendMaterial(Material material, SkyPrisonHUDBlendModeV1 mode)
        {
            if (material == null)
                return;

            if (material.HasProperty("_SkyPrisonBlendMode")) material.SetFloat("_SkyPrisonBlendMode", (float)mode);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", 5f); // SrcAlpha
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", 10f); // OneMinusSrcAlpha
            if (material.HasProperty("_BlendOp")) material.SetFloat("_BlendOp", 0f);
        }

        private static void CopyFloat(Material source, Material target, string property)
        {
            if (source.HasProperty(property) && target.HasProperty(property))
                target.SetFloat(property, source.GetFloat(property));
        }

        private static void CopyColor(Material source, Material target, string property)
        {
            if (source.HasProperty(property) && target.HasProperty(property))
                target.SetColor(property, source.GetColor(property));
        }

        public void RenderNowForWorkbenchPreview()
        {
            if (!CanMutateForPostProcess)
                return;

            if (sourceCamera == null || moduleRT == null)
                return;

            EnsureSize();
            EnsureOutputMaterial();
            RefreshSourceCameraBinding();

            try
            {
                Canvas.ForceUpdateCanvases();
                sourceCamera.Render();
            }
            catch (System.Exception e)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SkyPrisonHUDModulePostProcessRenderer] 工作台预览渲染模块 RT 失败：{e.Message}", this);
#endif
            }
        }


        /// <summary>
        /// V37: Before saving a prefab from the workbench, restore any children that were temporarily moved
        /// to the offscreen RT source. This prevents HP/LP/quick-slot details from disappearing after save.
        /// </summary>
        public void RestoreSourceChildrenToDisplayRootForPrefabSave()
        {
            RectTransform display = transform as RectTransform;
            if (display != null && sourceRoot != null)
            {
                for (int i = sourceRoot.childCount - 1; i >= 0; i--)
                {
                    Transform child = sourceRoot.GetChild(i);
                    if (child != null)
                        child.SetParent(display, false);
                }
            }

            if (outputImage != null)
            {
                DestroySafe(outputImage.gameObject);
                outputImage = null;
            }

            ReleaseAllGeneratedObjects();
        }

        private void ReleaseRTOnly()
        {
            if (sourceCamera != null)
                sourceCamera.targetTexture = null;
            if (outputImage != null)
                outputImage.texture = null;
            if (moduleRT != null)
            {
                if (moduleRT.IsCreated())
                    moduleRT.Release();
                DestroySafe(moduleRT);
                moduleRT = null;
            }
        }

        private void ReleaseAllGeneratedObjects()
        {
            ReleaseRTOnly();
            if (outputMaterial != null)
            {
                DestroySafe(outputMaterial);
                outputMaterial = null;
            }
            if (sourceRoot != null)
            {
                DestroySafe(sourceRoot.gameObject);
                sourceRoot = null;
            }
            if (sourceCamera != null)
            {
                DestroySafe(sourceCamera.gameObject);
                sourceCamera = null;
            }
        }

        private static void DestroySafe(Object obj)
        {
            if (obj == null)
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(obj);
            else
#endif
                Object.Destroy(obj);
        }
    }
}
