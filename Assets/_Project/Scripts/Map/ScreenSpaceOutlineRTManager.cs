using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// V55 - 2026-05-22 - respect disabled proxy renderers / mask source authority
///
/// Runtime phase rule:
/// - FrontOccluderTrigger evaluates the receiver ledger in LateUpdate.
/// - This manager has a late execution order and now produces masks in LateUpdate after that ledger.
/// - Camera callback rendering is disabled by default, so body masks do not consume a one-phase-old ledger.
///
/// Rule locked for this version:
/// - Occlusion authorization is UNIT level.
/// - Final outline display is still four categories: Player / Enemy / Ally / Item.
/// - This script does NOT touch RawImage.texture.
/// - This script does NOT bind Silhouette RT / MainTex / OutlineRT.
/// - This script only writes category MaskRTs and GateRTs to the existing UI materials.
///
/// Important difference from V12:
/// V12 rendered category MaskRT from the union of authorized occluder proxy roots.
/// V13 renders per receiver first:
///     UnitHidden = UnitOccludedSilhouette ∩ UnitAuthorizedOccluderMask
/// then adds UnitHidden into the final Player/Enemy/Ally/Item MaskRT.
///
/// Therefore, before the final four-channel integration, every unit is isolated.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32000)]
public sealed class ScreenSpaceOutlineRTManager : MonoBehaviour
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V64 - 2026-06-06 - unit-level outline mask binding";
    [SerializeField] private int compileTouchVersion = 2026060604;

    [Header("Rule")]
    [SerializeField] private bool bindFixedFourChannels = true;
    [SerializeField] private bool renderPerUnitHiddenMaskBeforeCategoryOutput = true;
    [SerializeField] private bool doNotTouchSilhouetteOrRawImageTexture = true;

    [Header("Render Resources - explicit asset dependency")]
    [Tooltip("Formal render resource asset. This is the preferred path: scene -> ScriptableObject -> Material -> Shader, so Build keeps the shader without global forcing.")]
    [SerializeField] private SkyPrisonOcclusionRenderResources renderResources;
    [Tooltip("Optional direct material override. Use this only when the resources asset is not ready yet.")]
    [SerializeField] private Material unitHiddenMaskCompositeMaterial;
    [Tooltip("Optional direct shader override. Prefer Material or RenderResources so shader variants are asset-referenced.")]
    [SerializeField] private Shader unitHiddenMaskCompositeShader;
    [Tooltip("Editor-only fallback for diagnosis. Runtime builds should rely on serialized assets, not blind shader lookup.")]
    [SerializeField] private bool allowEditorShaderFindFallback = true;
    [SerializeField] private string unitHiddenMaskCompositeShaderName = "Hidden/SkyPrison/UnitHiddenMaskComposite";
    [Tooltip("Optional formal fallback. This is not a blind Shader.Find: it loads the official resource asset created by the installer under Assets/_Project/Resources/Occlusion.")]
    [SerializeField] private bool allowResourcesAssetFallback = true;
    [SerializeField] private string renderResourcesLoadPath = "Occlusion/SkyPrisonOcclusionRenderResources";


    [Header("Camera Template")]
    [Tooltip("Template camera used only for projection settings. It should match the old OcclusionMaskCamera view.")]
    [SerializeField] private Camera occlusionMaskCameraTemplate;
    [SerializeField] private string maskCameraTemplateName = "OcclusionMaskCamera";

    [Header("Camera Sync")]
    [Tooltip("Important for camera-follow games: runtime mask cameras must copy the current template camera pose/projection before every manual Render(). Otherwise the mask can drift from the visible map while the player/camera moves.")]
    [SerializeField] private bool syncRuntimeMaskCameraFromTemplateEveryRender = true;
    [SerializeField] private bool syncCameraProjectionMatrix = true;
    [SerializeField] private bool syncCameraViewMatrix = true;

    [Header("Build Stable Visible View Camera")]
    [Tooltip("Use the final visible Main Camera as the view/projection source for runtime mask rendering. This keeps Editor Play and Build aligned.")]
    [SerializeField] private bool preferVisibleViewCameraForViewSync = true;
    [Tooltip("Use the visible camera pixel size instead of only Screen.width/height when rebuilding runtime RTs.")]
    [SerializeField] private bool useVisibleCameraPixelSizeForRT = true;
    [SerializeField] private Camera visibleViewCamera;
    [SerializeField] private string visibleViewCameraName = "Main Camera";
    [Tooltip("Diagnostic only. Do not let Character2D consumer camera override the world/view authority for mask production; Main Camera remains the spatial authority for occluder logic.")]
    [SerializeField] private bool preferCharacter2DConsumerCameraForViewSync = false;
    [SerializeField] private string character2DConsumerCameraName = "GamePlayCamera";
    [SerializeField] private string character2DLayerName = "Character2D";
    [Tooltip("When the serialized visibleViewCamera points to a camera that does not render Character2D, automatically override it with the Character2D consumer camera. This prevents body shader screen UV from sampling a mask rendered in another camera space.")]
    [SerializeField] private bool overrideNonConsumerVisibleCamera = false;
    [Tooltip("Refresh mask RTs immediately before the visible camera renders.")]
    [SerializeField] private bool renderBeforeVisibleCamera = false;
    [Tooltip("Fallback refresh in LateUpdate. Usually off once beginCameraRendering is stable.")]
    [SerializeField] private bool renderInLateUpdateFallback = true;

    [Header("Fixed Overlay RawImages - MANUAL DRAG FIRST; auto-find only when empty")]
    [SerializeField] private RawImage playerOverlayImage;
    [SerializeField] private RawImage enemyOverlayImage;
    [SerializeField] private RawImage allyOverlayImage;
    [SerializeField] private RawImage itemOverlayImage;

    [Header("Overlay Rect Authority")]
    [Tooltip("The outline overlay is a full-screen post layer. Keep the overlay container and four RawImages stretched to the full visible canvas so their RT pixels match the camera pixels.")]
    [SerializeField] private bool forceOverlayRawImagesToFullScreen = true;
    [Tooltip("Also stretch the OutlineOverlay container chain. This is the important fix when RawImage rect reports 100x100 even after its own anchors are full-screen.")]
    [SerializeField] private bool forceOverlayParentRectsToFullScreen = true;
    [SerializeField] private bool forceOverlayRawImageUvFullRect = true;
    [SerializeField] private bool debugLogOverlayRectFix = false;

    [Header("Fixed Overlay Materials - optional fallback; usually leave empty if RawImage has material")]
    [SerializeField] private Material playerOverlayMaterial;
    [SerializeField] private Material enemyOverlayMaterial;
    [SerializeField] private Material allyOverlayMaterial;
    [SerializeField] private Material itemOverlayMaterial;

    [Header("Channels")]
    [SerializeField] private bool bindPlayer = true;
    [SerializeField] private bool bindEnemy = true;
    [SerializeField] private bool bindAlly = true;
    [SerializeField] private bool bindItem = true;

    [Header("Render Texture Options")]
    [SerializeField] private bool matchScreenExactly = true;
    [Tooltip("Runtime RT scale. Lowered from 1.0 default: profiling found ~9 full-screen-resolution occlusion RTs (Player/Enemy/Ally/Item mask+raw + temp buffers) eating ~660MB VRAM combined, a likely contributor to severe frame stalls. Occlusion masks are binary hit-tests, not fine detail, so 0.5 (1/4 area) should be visually safe — bump back toward 1.0 if mask edges look chunky.")]
    [SerializeField, Range(0.25f, 1.0f)] private float runtimeRTScale = 0.5f;
    [SerializeField] private int fallbackWidth = 1024;
    [SerializeField] private int fallbackHeight = 1024;
    [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;
    [SerializeField] private int maskDepthBufferBits = 24;

    [Header("Performance Cleanup")]
    [Tooltip("Do not run scene/component lookup on every camera render. References are resolved on Awake/Start/OnEnable/Rebuild and when a missing reference is detected.")]
    [SerializeField] private bool resolveReferencesBeforeEveryRender = false;
    [Tooltip("Do not recreate/check composite material every render once it exists.")]
    [SerializeField] private bool ensureCompositeBeforeEveryRender = false;
    [Tooltip("Overlay rect authority is applied once and when size changes, not every render.")]
    [SerializeField] private bool enforceOverlayRectEveryRender = false;
    [Tooltip("Build the long human-readable channel summary only at a throttled interval. The runtime path should not allocate long strings every frame.")]
    [SerializeField] private bool buildDebugSummaryEveryRender = false;
    [SerializeField, Min(0.1f)] private float debugSummaryRefreshInterval = 0.5f;
    [Tooltip("When off, this manager does not build long inspector/debug strings during normal play. Enable only while diagnosing the outline/mask pipeline.")]
    [SerializeField] private bool updateInspectorDebugStrings = false;
    [Tooltip("Even when inspector debug strings are enabled, refresh them at a low rate instead of every visual frame.")]
    [SerializeField, Min(0.25f)] private float inspectorDebugStringRefreshInterval = 1.0f;
    [Tooltip("When off, per-channel renderStatus strings are not rebuilt every frame. Numeric fields still update.")]
    [SerializeField] private bool updatePerFrameRenderStatusStrings = false;
    [Tooltip("Build the detailed per-receiver ledger only when diagnosing. Keep off for normal play/build.")]
    [SerializeField] private bool buildDetailedMaskLedgerEveryRender = false;
    [Tooltip("Disable the overlay RawImage for channels with no rendered hidden units, avoiding empty full-screen UI draws.")]
    [SerializeField] private bool disableOverlayImageWhenChannelEmpty = true;
    [Tooltip("Avoid Canvas rebuilds caused by setting the same UI material/textures every frame. The RT object stays the same while its contents update, so rebinding is only needed when the references actually change.")]
    [SerializeField] private bool onlyDirtyOverlayWhenBindingChanges = true;
    [Tooltip("Sleep Outline producer cameras for inactive channels. Active channels are still rendered manually at full resolution, so this does not lower visual quality.")]
    [SerializeField] private bool sleepInactiveProducerCameras = true;
    [Tooltip("How often to enforce inactive producer camera sleep. This catches cameras that another script or serialized scene state re-enabled.")]
    [SerializeField, Min(0.05f)] private float inactiveProducerCameraSleepInterval = 0.5f;
    [Tooltip("Skip binding work for a channel when both overlay and runtime material are missing. Safe guard for stripped/unused channels.")]
    [SerializeField] private bool skipMissingOverlayChannel = true;
    [Tooltip("Guarantee this manager renders at most once per frame, even if multiple camera callbacks or fallback paths fire.")]
    [SerializeField] private bool renderAtMostOncePerFrame = true;
    [Tooltip("Runtime rule: active occlusion masks are visual-frame products, not state-change events. Produce them once in LateUpdate after Trigger/Receiver ledgers and before the next camera consumes them.")]
    [SerializeField] private bool useAuthoritativeLateUpdateVisualClock = true;
    [Tooltip("When using the authoritative visual clock, camera callbacks never produce masks. This avoids one early/old producer consuming the frame budget and blocking the LateUpdate producer.")]
    [SerializeField] private bool ignoreCameraCallbacksWhenUsingVisualClock = true;

    [Header("Deterministic Producer Sync")]
    [Tooltip("When a channel actually has hidden units, render its Outline source camera and Occluded Gate camera once immediately after the unit-hidden mask is updated. This keeps producer RTs and the UI composite on the same frame without restoring full brute-force refresh.")]
    [SerializeField] private bool renderOutlineProducerCamerasForActiveChannels = true;
    [SerializeField] private bool syncProducerCamerasFromVisibleViewCamera = true;
    [SerializeField] private string outlineCameraNamePattern = "OutlineCamera_{0}";
    [SerializeField] private string occludedOutlineCameraNamePattern = "OutlineCamera_{0}_Occluded";
    [Tooltip("Keep producer cameras disabled after manual rendering so they do not render again later in the same frame.")]
    [SerializeField] private bool disableProducerCamerasAfterManualRender = true;

    [Header("Manual Per Unit Render")]
    [SerializeField] private int temporaryMaskLayer = 31;
    [SerializeField] private bool disableTemplateCamera = false;
    [SerializeField] private bool forceRootsActiveDuringRender = true;
    [SerializeField] private bool forceRenderersEnabledDuringRender = true;
    [Tooltip("V55: Renderer.enabled is now treated as mask-source authority. A disabled Box/AutoClone renderer must stay disabled during manual mask rendering; otherwise old fallback proxies are silently reintroduced into the RawMask.")]
    [SerializeField] private bool respectDisabledProxyRenderersDuringRender = true;
    [SerializeField] private bool preferExactChannelOccludedProxy = true;

    [Header("Render Root Traversal Cache")]
    [Tooltip("Cache Transform/Renderer arrays for occluder proxy roots. This removes the per-frame GetComponentsInChildren allocation spike while characters stay behind the same object.")]
    [SerializeField] private bool cacheRenderRootTraversal = true;
    [Tooltip("Seconds before refreshing a root traversal cache entry. Keep small enough to catch runtime proxy changes, but not every frame.")]
    [SerializeField, Min(0.05f)] private float renderRootTraversalCacheRefreshInterval = 1.0f;
    [Tooltip("Drop cached root traversal entries that were not used recently. Prevents long-session cache growth after scene activity.")]
    [SerializeField, Min(30)] private int renderRootTraversalCacheUnusedFrameLimit = 600;

    [Header("Thresholds")]
    [SerializeField] private float unitSilhouetteThreshold = 0.5f;
    [SerializeField] private float occluderMaskThreshold = 0.5f;


    [Header("Raw Mask Stability")]
    [Tooltip("When a receiver ledger briefly drops for a frame during state/material ordering, keep the last valid raw occluder mask for a tiny grace window instead of clearing it immediately.")]
    [SerializeField] private bool holdLastValidRawMaskOnBriefLedgerDrop = true;
    [Tooltip("How many frames a previously valid raw mask may be held after the ledger briefly reports no authorized roots. Keep this small; it is only a visual-phase guard, not a logic override.")]
    [SerializeField, Min(0)] private int rawMaskReleaseGraceFrames = 2;

    [Header("Receiver Runtime Cache")]
    [Tooltip("Cache UnitOcclusionMaterialReceiver lookups. This avoids FindObjectsByType every render while still refreshing periodically for spawned/despawned units.")]
    [SerializeField] private bool cacheReceiversForRuntime = true;
    [Tooltip("Seconds between receiver-cache refreshes when cacheReceiversForRuntime is enabled.")]
    [SerializeField, Min(0.05f)] private float receiverCacheRefreshInterval = 0.5f;

    [Header("Performance - Stable Raw Mask Reuse")]
    [Tooltip("When the authorized occluder root set and mask camera are unchanged, reuse the previous raw mask instead of rendering proxy roots again. The raw occluder mask depends on map occluders/camera, not on Spine animation.")]
    [SerializeField] private bool reuseStableRawOccluderMask = true;

    [Tooltip("Safety refresh for a reused raw mask. 0 disables periodic refresh. Keep small enough for moving map objects, large enough to avoid constant RT renders.")]
    [SerializeField, Min(0)] private int forceRawMaskRefreshEveryFrames = 30;

    [Header("Outline Overlay Mask Source")]
    [Tooltip("Body occlusion already uses the raw authorized occluder mask. For hidden outline, also use the raw authorized occluder mask instead of the pre-intersected UnitHiddenMask, so animated Spine silhouettes are not clipped by stale proxy silhouettes.")]
    [SerializeField] private bool overlayUsesRawOccluderMaskForHiddenOutline = false;
    [Tooltip("When overlayUsesRawOccluderMaskForHiddenOutline is enabled, disable the occluded-gate multiplier on UIExpandMasked. The active channel/receiver ledger is already the gate, and old gate RTs can clip the current animation pose.")]
    [SerializeField] private bool disableOverlayGateWhenUsingRawOccluderMask = false;
    [Tooltip("Fast path after V33/V46 split: body and hidden outline both consume the raw authorized occluder mask. The old UnitHiddenMask = silhouette ∩ occluder composite is no longer needed in normal play, so skip silhouette rendering and composite blits.")]
    [SerializeField] private bool useRawOccluderMaskOnlyFastPath = false;

    [Header("Unit Level Outline")]
    [Tooltip("V64: force the visible outline overlay to use per-receiver UnitHidden results. This prevents a receiver that has entered occlusion from sampling unrelated same-channel raw occluder masks.")]
    [SerializeField] private bool forceUnitLevelOutlineMask = true;
    [Tooltip("V64: once _MaskTex is already the per-receiver hidden result, old occluded gate RT can clip or mix unrelated silhouettes. Disable gate for unit-level outline.")]
    [SerializeField] private bool disableOverlayGateForUnitLevelOutline = true;
    [Tooltip("V64: also bind each receiver-owned hidden mask RT back to its UnitOcclusionMaterialReceiver so the body and outline share the same unit authority.")]
    [SerializeField] private bool bindReceiverOwnedMaskToReceiver = true;

    [Header("Naming")]
    [SerializeField] private string overlayRootName = "OutlineOverlay";
    [SerializeField] private string playerOverlayName = "OutlineOverlay_Player";
    [SerializeField] private string enemyOverlayName = "OutlineOverlay_Enemy";
    [SerializeField] private string allyOverlayName = "OutlineOverlay_Ally";
    [SerializeField] private string itemOverlayName = "OutlineOverlay_Item";
    [SerializeField] private string gateRTNamePattern = "RT_Outline_{0}_Occluded";
    [SerializeField] private string maskRTNamePattern = "RT_OcclusionMask_{0}_Runtime";
    [SerializeField] private string rawOccluderMaskRTNamePattern = "RT_OcclusionRaw_{0}_Runtime";
    [SerializeField] private string maskCameraNamePattern = "OcclusionMaskCamera_{0}_Runtime";

    [Header("Material Property Binding")]
    [SerializeField] private bool bindAliasProperties = true;
    [SerializeField] private bool enforceEveryLateUpdate = true;
    [Tooltip("Force each Overlay RawImage to use a runtime material instance, then overwrite every mask/gate alias every LateUpdate. This prevents old shared RT_OcclusionMask_Runtime from coming back.")]
    [SerializeField] private bool forceRuntimeMaterialInstance = true;
    [SerializeField] private bool forceUseGateOnBoundMaterials = false;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
    [TextArea(5, 18)]
    [SerializeField] private string debugBoundChannelSummary = "";
    [SerializeField] private string debugWarning = "";
    [SerializeField] private string debugCompositeResourceStatus = "";
    [TextArea(4, 12)]
    [SerializeField] private string debugLastPlayerMaskRenderLedger = "";
    [TextArea(4, 12)]
    [SerializeField] private string debugLastEnemyMaskRenderLedger = "";
    [TextArea(4, 12)]
    [SerializeField] private string debugLastAllyMaskRenderLedger = "";
    [TextArea(4, 12)]
    [SerializeField] private string debugLastItemMaskRenderLedger = "";

    private readonly Dictionary<string, ChannelRuntime> runtimeByChannel = new Dictionary<string, ChannelRuntime>(StringComparer.OrdinalIgnoreCase);

    // FindGateTexture 之前每帧、每个 channel 都无条件调一次 Resources.FindObjectsOfTypeAll
    // <RenderTexture>()——这个不是扫场景，是扫整个已加载工程里所有 RenderTexture，代价
    // 随项目里 RT 总数线性增长，而且是 LateUpdate 里每帧都跑、乘上 channel 数量。正式游戏
    // 场景变大、遮挡物变多之后这里会是最先"跑崩"的地方之一。缓存下来，找到过的 gate
    // texture 只要还活着就不再重新扫，销毁了才重新找一次。
    private readonly Dictionary<string, Texture> gateTextureCache = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Camera> outlineProducerCameraByChannel = new Dictionary<string, Camera>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Camera> occludedProducerCameraByChannel = new Dictionary<string, Camera>(StringComparer.OrdinalIgnoreCase);
    private readonly List<UnitOcclusionMaterialReceiver> receiverBuffer = new List<UnitOcclusionMaterialReceiver>();
    private readonly List<UnitOcclusionMaterialReceiver> receiverCache = new List<UnitOcclusionMaterialReceiver>();
    private readonly Dictionary<UnitOcclusionMaterialReceiver, RenderTexture> perReceiverHiddenMaskRT = new Dictionary<UnitOcclusionMaterialReceiver, RenderTexture>();
    private readonly HashSet<UnitOcclusionMaterialReceiver> perReceiverMaskTouchedThisFrame = new HashSet<UnitOcclusionMaterialReceiver>();
    private readonly List<UnitOcclusionMaterialReceiver> receiverReleaseBuffer = new List<UnitOcclusionMaterialReceiver>();
    private float nextReceiverCacheRefreshTime = -999f;
    private readonly List<GameObject> occluderRootBuffer = new List<GameObject>();
    private readonly List<GameObject> silhouetteRootBuffer = new List<GameObject>();
    private readonly List<GameObjectLayerState> layerStates = new List<GameObjectLayerState>();
    private readonly List<GameObjectActiveState> activeStates = new List<GameObjectActiveState>();
    private readonly List<RendererState> rendererStates = new List<RendererState>();
    private readonly Dictionary<int, RootTraversalCacheEntry> rootTraversalCache = new Dictionary<int, RootTraversalCacheEntry>();

    private RenderTexture tempUnitSilhouetteRT;
    private RenderTexture tempUnitOccluderMaskRT;
    private RenderTexture tempUnitHiddenRT;
    private Material compositeMaterial;
    private int lastWidth = -1;
    private int lastHeight = -1;
    private int lastRenderFrame = -1;
    private float lastDebugSummaryTime = -999f;
    private float nextInactiveProducerCameraSleepTime = -999f;
    private bool overlayRectAuthorityApplied;

    private static readonly string[] MaskTexturePropertyNames =
    {
        // Current UIExpandMasked_V4 property.
        "_MaskTex",

        // Explicit unit-hidden names, kept for future shader variants.
        "_UnitHiddenMaskRT",
        "_UnitHiddenMaskTex",
        "_UnitHiddenMask",
        "_UnitHiddenTex",
        "_HiddenMaskTex",

        // Legacy aliases. These must also be overwritten so old shared RT_OcclusionMask_Runtime cannot survive.
        "_OcclusionMaskRT",
        "_MaskRT",
        "_OcclusionMask",
        "_OcclusionMaskTex"
    };

    private static readonly string[] GateTexturePropertyNames =
    {
        // Current UIExpandMasked_V4 property.
        "_GateTex",

        // Legacy / explicit aliases.
        "_OccludedGateRT",
        "_GateRT",
        "_OcclusionGateRT",
        "_OccludedGateTex"
    };

    [Serializable]
    private sealed class ChannelRuntime
    {
        public string channel;
        public RawImage overlayImage;
        public Material sourceMaterial;
        public Material runtimeMaterial;
        public Camera runtimeMaskCamera;
        public RenderTexture maskRT;
        public RenderTexture rawOccluderMaskRT;
        public Texture gateRT;
        public string warning;
        public string renderStatus;
        public int lastReceiverCount;
        public int lastRenderedUnits;
        public int lastAuthorizedRootCount;
        public bool hasValidRawMask;
        public bool rawMaskAlreadyClearedWhenEmpty;
        public int lastRawRootSignature;
        public int lastRawCameraSignature;
        public int lastRawMaskRenderFrame = -999999;
        public int lastRawMaskReuseFrame = -999999;
        public int rawMaskReuseCount;
        public int lastValidRawMaskFrame = -999999;
        public int lastHeldRawMaskFrame = -999999;
        public Material lastForcedOverlayMaterial;
        public Texture lastBoundMaskTexture;
        public Texture lastBoundGateTexture;
        public bool lastOverlayEnabledInitialized;
        public bool lastOverlayEnabled;
    }

    private struct GameObjectLayerState
    {
        public GameObject go;
        public int layer;
    }

    private struct GameObjectActiveState
    {
        public GameObject go;
        public bool activeSelf;
    }

    private struct RendererState
    {
        public Renderer renderer;
        public bool enabled;
    }

    private sealed class RootTraversalCacheEntry
    {
        public Transform[] transforms;
        public Renderer[] renderers;
        public float nextRefreshTime;
        public int lastUsedFrame;
    }

    private void NormalizeRuntimePhaseSettings()
    {
        if (!useAuthoritativeLateUpdateVisualClock)
            return;

        // Runtime phase rule, not an Inspector preference:
        // the occlusion relation can stay unchanged while the character silhouette keeps moving.
        // The mask must therefore be produced from the current visual frame, once, after receiver ledgers.
        renderBeforeVisibleCamera = false;
        renderInLateUpdateFallback = true;
        enforceEveryLateUpdate = true;

        if (forceUnitLevelOutlineMask)
        {
            overlayUsesRawOccluderMaskForHiddenOutline = false;
            disableOverlayGateWhenUsingRawOccluderMask = false;
            useRawOccluderMaskOnlyFastPath = false;
        }
    }

    private void OnValidate()
    {
        scriptVersion = "V64 - 2026-06-06 - unit-level outline mask binding";
        compileTouchVersion = 2026052222;
        NormalizeRuntimePhaseSettings();
    }

    private void Awake()
    {
        NormalizeRuntimePhaseSettings();
        ResolveReferences();
        RefreshReceiverCacheIfNeeded(true);
        EnsureCompositeMaterial();
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

        NormalizeRuntimePhaseSettings();
        ResolveReferences();
        RefreshReceiverCacheIfNeeded(true);
        EnsureCompositeMaterial();
        RebuildIfNeeded(true);
        lastRenderFrame = -1;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        rootTraversalCache.Clear();
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (useAuthoritativeLateUpdateVisualClock && ignoreCameraCallbacksWhenUsingVisualClock)
            return;

        if (!renderBeforeVisibleCamera || camera == null || camera.targetTexture != null)
            return;

        Camera view = ResolveVisibleViewCamera();
        if (view != null && camera != view)
            return;

        RenderAndBindAllChannels();
    }

    private void Start()
    {
        NormalizeRuntimePhaseSettings();
        ResolveReferences();
        RefreshReceiverCacheIfNeeded(true);
        EnsureCompositeMaterial();
        RebuildIfNeeded(true);

        // V42 rule:
        // Do not publish an early Start-phase mask when the authoritative visual ledger
        // is produced in LateUpdate after FrontOccluderTrigger has evaluated.
        // Publishing here can make the first gameplay camera frame consume stale masks.
        if (!renderInLateUpdateFallback)
            RenderAndBindAllChannels();
    }

    private void Update()
    {
        RebuildIfNeeded(false);
    }

    private void LateUpdate()
    {
        if (useAuthoritativeLateUpdateVisualClock)
        {
            RenderAndBindAllChannels();
            return;
        }

        if (enforceEveryLateUpdate && renderInLateUpdateFallback)
            RenderAndBindAllChannels();
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

        foreach (KeyValuePair<string, ChannelRuntime> pair in runtimeByChannel)
        {
            ChannelRuntime rt = pair.Value;
            if (rt == null)
                continue;

            if (rt.runtimeMaskCamera != null)
                Destroy(rt.runtimeMaskCamera.gameObject);

            ReleaseRT(ref rt.maskRT);
            ReleaseRT(ref rt.rawOccluderMaskRT);

            if (rt.runtimeMaterial != null)
                Destroy(rt.runtimeMaterial);
        }

        runtimeByChannel.Clear();
        rootTraversalCache.Clear();
        ReleaseRT(ref tempUnitSilhouetteRT);
        ReleaseRT(ref tempUnitOccluderMaskRT);
        ReleaseRT(ref tempUnitHiddenRT);

        ReleaseAllPerReceiverHiddenMaskRTs();

        if (compositeMaterial != null)
            Destroy(compositeMaterial);
    }

    [ContextMenu("Resolve References")]
    public void ResolveReferences()
    {
        if (occlusionMaskCameraTemplate == null)
            occlusionMaskCameraTemplate = FindCameraByName(maskCameraTemplateName);

        if (visibleViewCamera == null)
            visibleViewCamera = FindCameraByName(visibleViewCameraName);

        if (playerOverlayImage == null)
            playerOverlayImage = FindRawImageByName(playerOverlayName);
        if (enemyOverlayImage == null)
            enemyOverlayImage = FindRawImageByName(enemyOverlayName);
        if (allyOverlayImage == null)
            allyOverlayImage = FindRawImageByName(allyOverlayName);
        if (itemOverlayImage == null)
            itemOverlayImage = FindRawImageByName(itemOverlayName);

        ResolveOutlineProducerCameras();

        EnsureOverlayRawImageRectAuthority(playerOverlayImage, "Player");
        EnsureOverlayRawImageRectAuthority(enemyOverlayImage, "Enemy");
        EnsureOverlayRawImageRectAuthority(allyOverlayImage, "Ally");
        EnsureOverlayRawImageRectAuthority(itemOverlayImage, "Item");
    }

    [ContextMenu("Force Rebuild Mask RTs")]
    public void ForceRebuildMaskRTs()
    {
        ResolveReferences();
        RefreshReceiverCacheIfNeeded(true);
        EnsureCompositeMaterial();
        RebuildIfNeeded(true);
        RenderAndBindAllChannels();
    }

    [ContextMenu("Render Per Unit Hidden Masks + Bind Now")]
    public void RenderAndBindAllChannels()
    {
        if (!bindFixedFourChannels)
            return;

        if (renderAtMostOncePerFrame && lastRenderFrame == Time.frameCount)
            return;
        lastRenderFrame = Time.frameCount;

        if (resolveReferencesBeforeEveryRender || ReferencesMissing())
            ResolveReferences();

        if (ensureCompositeBeforeEveryRender || compositeMaterial == null)
            EnsureCompositeMaterial();

        RebuildIfNeeded(false);

        bool buildSummary = ShouldBuildDebugSummary();
        StringBuilder sb = buildSummary ? new StringBuilder(1024) : null;
        if (buildSummary || debugLogs || updatePerFrameRenderStatusStrings)
            debugWarning = "";

        perReceiverMaskTouchedThisFrame.Clear();

        if (bindPlayer)
            RenderAndBindChannel("Player", playerOverlayImage, playerOverlayMaterial, sb);
        if (bindEnemy)
            RenderAndBindChannel("Enemy", enemyOverlayImage, enemyOverlayMaterial, sb);
        if (bindAlly)
            RenderAndBindChannel("Ally", allyOverlayImage, allyOverlayMaterial, sb);
        if (bindItem)
            RenderAndBindChannel("Item", itemOverlayImage, itemOverlayMaterial, sb);

        ReleaseUntouchedPerReceiverMasks();

        overlayRectAuthorityApplied = true;

        if (buildSummary)
            debugBoundChannelSummary = sb != null ? sb.ToString() : "";

        if (disableTemplateCamera && occlusionMaskCameraTemplate != null)
            occlusionMaskCameraTemplate.enabled = false;

        SleepInactiveProducerCamerasIfNeeded(false);
        PruneRootTraversalCacheIfNeeded();

        if (debugLogs && buildSummary && !string.IsNullOrEmpty(debugBoundChannelSummary))
            Debug.Log("[ScreenSpaceOutlineRTManager] Per unit hidden mask summary:\n" + debugBoundChannelSummary, this);
    }

    private bool ReferencesMissing()
    {
        if (occlusionMaskCameraTemplate == null)
            return true;
        if (preferVisibleViewCameraForViewSync && visibleViewCamera == null)
            return true;
        if (bindPlayer && playerOverlayImage == null)
            return true;
        if (bindEnemy && enemyOverlayImage == null)
            return true;
        if (bindAlly && allyOverlayImage == null)
            return true;
        if (bindItem && itemOverlayImage == null)
            return true;
        return false;
    }

    private bool ShouldBuildDebugSummary()
    {
        if (debugLogs || buildDebugSummaryEveryRender)
            return true;

        if (!updateInspectorDebugStrings)
            return false;

        float interval = Mathf.Max(0.25f, inspectorDebugStringRefreshInterval);
        if (Time.unscaledTime - lastDebugSummaryTime < interval)
            return false;

        lastDebugSummaryTime = Time.unscaledTime;
        return true;
    }

    private bool ShouldBuildRuntimeStatusStrings(StringBuilder summaryBuilder)
    {
        return updatePerFrameRenderStatusStrings || debugLogs || summaryBuilder != null;
    }

    private void RebuildIfNeeded(bool force)
    {
        int width;
        int height;
        GetTargetPixelSize(out width, out height);

        if (!force && width == lastWidth && height == lastHeight)
            return;

        lastWidth = width;
        lastHeight = height;

        if (bindPlayer)
            EnsureChannelRuntime("Player", width, height);
        if (bindEnemy)
            EnsureChannelRuntime("Enemy", width, height);
        if (bindAlly)
            EnsureChannelRuntime("Ally", width, height);
        if (bindItem)
            EnsureChannelRuntime("Item", width, height);

        EnsureTempRTs(width, height);
    }

    private int lastLoggedTargetWidth = -1;
    private int lastLoggedTargetHeight = -1;

    private void GetTargetPixelSize(out int width, out int height)
    {
        width = Mathf.Max(1, fallbackWidth);
        height = Mathf.Max(1, fallbackHeight);

        Camera view = ResolveVisibleViewCamera();
        if (matchScreenExactly)
        {
            float scale = Mathf.Clamp(runtimeRTScale, 0.25f, 1.0f);
            if (useVisibleCameraPixelSizeForRT && view != null && view.pixelWidth > 0 && view.pixelHeight > 0)
            {
                width = Mathf.Max(1, Mathf.RoundToInt(view.pixelWidth * scale));
                height = Mathf.Max(1, Mathf.RoundToInt(view.pixelHeight * scale));
                LogTargetSizeIfChanged(width, height, view.name, "cameraPixelSize");
                return;
            }

            width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * scale));
            LogTargetSizeIfChanged(width, height, view != null ? view.name : "null", "screenSize");
        }
    }

    // 2026-07-14：诊断 RT_OcclusionMask_* 实测显存占用对不上 runtimeRTScale=0.5 的问题——
    // 只在这个值真的变化时打一行日志（不是每帧都打），用来确认运行时算出来的宽高、走的哪条
    // 分支、绑定的是哪台摄像机，到底跟配置对不对得上。确认问题后可以删掉。
    private void LogTargetSizeIfChanged(int width, int height, string cameraName, string branch)
    {
        if (width == lastLoggedTargetWidth && height == lastLoggedTargetHeight)
            return;

        lastLoggedTargetWidth = width;
        lastLoggedTargetHeight = height;
        Debug.Log($"[ScreenSpaceOutlineRTManager] GetTargetPixelSize -> {width}x{height}, branch={branch}, camera={cameraName}, matchScreenExactly={matchScreenExactly}, runtimeRTScale={runtimeRTScale}, frame={Time.frameCount}");
    }

    private Camera ResolveVisibleViewCamera()
    {
        // V44 rule:
        // The occlusion switch / proxy geometry is a world-view rule, so the spatial authority
        // must remain the visible world Main Camera. The Character2D consumer camera may render
        // the Spine body, but it must not silently replace the world camera for mask/proxy
        // production. In this project these cameras share the same rect/projection; if they ever
        // diverge, fix the camera rig, not the occlusion switch.
        if (visibleViewCamera != null)
            return visibleViewCamera;

        if (!string.IsNullOrWhiteSpace(visibleViewCameraName))
            visibleViewCamera = FindCameraByName(visibleViewCameraName);

        if (visibleViewCamera == null)
            visibleViewCamera = Camera.main;

        // Last-resort diagnostic fallback only. Do not prefer this by default.
        if (visibleViewCamera == null && preferCharacter2DConsumerCameraForViewSync)
            visibleViewCamera = ResolveCharacter2DConsumerCamera();

        return visibleViewCamera;
    }

    private Camera ResolveCharacter2DConsumerCamera()
    {
        Camera cam = null;

        if (!string.IsNullOrWhiteSpace(character2DConsumerCameraName))
            cam = FindCameraByName(character2DConsumerCameraName);

        if (cam != null)
            return cam;

        Camera[] cameras = FindObjectsOfType<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate == null)
                continue;

            if (CameraRendersLayer(candidate, character2DLayerName))
                return candidate;
        }

        return null;
    }

    private static bool CameraRendersLayer(Camera camera, string layerName)
    {
        if (camera == null || string.IsNullOrWhiteSpace(layerName))
            return false;

        int layer = LayerMask.NameToLayer(layerName);
        if (layer < 0 || layer > 31)
            return false;

        return (camera.cullingMask & (1 << layer)) != 0;
    }

    private ChannelRuntime EnsureChannelRuntime(string channel, int width, int height)
    {
        ChannelRuntime runtime;
        if (!runtimeByChannel.TryGetValue(channel, out runtime) || runtime == null)
        {
            runtime = new ChannelRuntime { channel = channel };
            runtimeByChannel[channel] = runtime;
        }

        if (runtime.maskRT == null || runtime.maskRT.width != width || runtime.maskRT.height != height)
        {
            ReleaseRT(ref runtime.maskRT);
            runtime.maskRT = CreateRT(string.Format(maskRTNamePattern, channel), width, height, maskDepthBufferBits);
            Debug.Log($"[ScreenSpaceOutlineRTManager] Created maskRT '{runtime.maskRT.name}' actual={runtime.maskRT.width}x{runtime.maskRT.height} requested={width}x{height} depthBits={maskDepthBufferBits} frame={Time.frameCount}");
        }

        if (runtime.rawOccluderMaskRT == null || runtime.rawOccluderMaskRT.width != width || runtime.rawOccluderMaskRT.height != height)
        {
            ReleaseRT(ref runtime.rawOccluderMaskRT);
            runtime.rawOccluderMaskRT = CreateRT(string.Format(rawOccluderMaskRTNamePattern, channel), width, height, maskDepthBufferBits);
        }

        if (runtime.runtimeMaskCamera == null && occlusionMaskCameraTemplate != null)
        {
            GameObject clone = Instantiate(occlusionMaskCameraTemplate.gameObject, occlusionMaskCameraTemplate.transform.parent);
            clone.name = string.Format(maskCameraNamePattern, channel);
            runtime.runtimeMaskCamera = clone.GetComponent<Camera>();
        }

        if (runtime.runtimeMaskCamera != null)
        {
            runtime.runtimeMaskCamera.enabled = false;
            SyncRuntimeMaskCameraFromTemplate(runtime.runtimeMaskCamera);
            runtime.runtimeMaskCamera.targetTexture = runtime.maskRT;
        }

        return runtime;
    }

    private void EnsureTempRTs(int width, int height)
    {
        EnsureRT(ref tempUnitSilhouetteRT, "RT_UnitHidden_Temp_Silhouette", width, height, maskDepthBufferBits);
        EnsureRT(ref tempUnitOccluderMaskRT, "RT_UnitHidden_Temp_OccluderMask", width, height, maskDepthBufferBits);
        EnsureRT(ref tempUnitHiddenRT, "RT_UnitHidden_Temp_Intersection", width, height, 0);
    }

    private void EnsureRT(ref RenderTexture rt, string rtName, int width, int height, int depth)
    {
        if (rt != null && rt.width == width && rt.height == height)
            return;

        ReleaseRT(ref rt);
        rt = CreateRT(rtName, width, height, depth);
    }

    private void RenderAndBindChannel(string channel, RawImage overlayImage, Material fallbackMaterial, StringBuilder sb)
    {
        int width;
        int height;
        GetTargetPixelSize(out width, out height);
        ChannelRuntime runtime = EnsureChannelRuntime(channel, lastWidth > 0 ? lastWidth : width, lastHeight > 0 ? lastHeight : height);
        runtime.overlayImage = overlayImage;
        if (enforceOverlayRectEveryRender || !overlayRectAuthorityApplied)
            EnsureOverlayRawImageRectAuthority(runtime.overlayImage, channel);
        runtime.sourceMaterial = fallbackMaterial != null ? fallbackMaterial : (overlayImage != null ? overlayImage.material : null);
        bool buildStatusStrings = ShouldBuildRuntimeStatusStrings(sb);
        if (buildStatusStrings)
            runtime.warning = "";

        if (buildStatusStrings && runtime.overlayImage == null)
            runtime.warning += " overlay image missing;";
        if (buildStatusStrings && runtime.sourceMaterial == null)
            runtime.warning += " overlay material missing;";

        if (skipMissingOverlayChannel && runtime.overlayImage == null && runtime.sourceMaterial == null)
        {
            if (buildStatusStrings)
                runtime.renderStatus = "skipped: overlay/material missing";
            AppendChannelSummary(sb, channel, runtime);
            return;
        }

        runtime.gateRT = FindGateTexture(channel);
        if (runtime.gateRT == null)
            runtime.warning += " gate RT missing;";

        runtime.renderStatus = renderPerUnitHiddenMaskBeforeCategoryOutput
            ? RenderPerUnitHiddenMaskForChannel(channel, runtime, buildStatusStrings)
            : (buildStatusStrings ? "manual render disabled" : "");

        if (runtime.lastRenderedUnits > 0)
            RenderOutlineProducerCamerasForChannel(channel);

        // 2026-07-19：全屏 RawImage 描边方案两轮尝试均失败，确认现场调试已经无法收敛，
        // 彻底放弃，不再尝试第三次。真正在用的描边显示路径是 UnitOcclusionMaterialReceiver
        // 材质切换 + SpineOcclusionComposite（角色）和掉落物自己的描边环
        // （LootDropVisual），这四个 RawImage 覆盖层保持常态关闭。
        // RenderPerUnitHiddenMaskForChannel 本身不能删——它是给 SetRuntimeAuthorityMaskTexture
        // 供数据的核心计算，SpineOcclusionComposite 的 _OcclusionTex 就靠这个。
        if (runtime.overlayImage != null && runtime.overlayImage.enabled)
            runtime.overlayImage.enabled = false;

        if (buildStatusStrings && !string.IsNullOrEmpty(runtime.warning))
            debugWarning += channel + ":" + runtime.warning + "\n";

        AppendChannelSummary(sb, channel, runtime);
    }

    private void AppendChannelSummary(StringBuilder sb, string channel, ChannelRuntime runtime)
    {
        if (sb == null || runtime == null)
            return;

        sb.Append(channel)
            .Append(": unitHiddenMask=").Append(runtime.maskRT != null ? runtime.maskRT.name : "NULL")
            .Append(", rawOccluderMask=").Append(runtime.rawOccluderMaskRT != null ? runtime.rawOccluderMaskRT.name : "NULL")
            .Append(", gate=").Append(runtime.gateRT != null ? runtime.gateRT.name : "NULL")
            .Append(", overlayMaskSource=").Append(overlayUsesRawOccluderMaskForHiddenOutline && runtime.rawOccluderMaskRT != null ? runtime.rawOccluderMaskRT.name : (runtime.maskRT != null ? runtime.maskRT.name : "NULL"))
            .Append(", maskCamera=").Append(runtime.runtimeMaskCamera != null ? GetPath(runtime.runtimeMaskCamera.transform) : "NULL")
            .Append(", image=").Append(runtime.overlayImage != null ? GetPath(runtime.overlayImage.transform) : "NULL")
            .Append(", overlayEnabled=").Append(runtime.overlayImage != null ? runtime.overlayImage.enabled.ToString() : "NULL")
            .Append(", producer=").Append(DescribeProducerCameras(channel))
            .Append(", overlayRect=").Append(runtime.overlayImage != null ? DescribeOverlayRect(runtime.overlayImage) : "NULL")
            .Append(", mat=").Append(runtime.runtimeMaterial != null ? runtime.runtimeMaterial.name : "NULL")
            .Append(", render=").Append(string.IsNullOrEmpty(runtime.renderStatus) ? "none" : runtime.renderStatus)
            .Append(", warn=").Append(string.IsNullOrEmpty(runtime.warning) ? "none" : runtime.warning)
            .Append(", composite=").Append(string.IsNullOrEmpty(debugCompositeResourceStatus) ? "unknown" : debugCompositeResourceStatus)
            .AppendLine();
    }

    private string RenderPerUnitHiddenMaskForChannel(string channel, ChannelRuntime runtime, bool buildStatusStrings)
    {
        if (runtime == null || runtime.runtimeMaskCamera == null || runtime.maskRT == null)
            return "camera/rt missing";
        runtime.lastReceiverCount = 0;
        runtime.lastRenderedUnits = 0;
        runtime.lastAuthorizedRootCount = 0;

        if (useRawOccluderMaskOnlyFastPath && overlayUsesRawOccluderMaskForHiddenOutline)
            return RenderRawOccluderMaskOnlyForChannel(channel, runtime, buildStatusStrings);

        if (compositeMaterial == null)
            return buildStatusStrings
                ? "composite material missing" + (string.IsNullOrEmpty(debugCompositeResourceStatus) ? "" : " | " + debugCompositeResourceStatus)
                : "";

        ClearRT(runtime.maskRT);
        ClearRT(runtime.rawOccluderMaskRT);

        receiverBuffer.Clear();
        CollectOccludedReceiversForChannel(channel, receiverBuffer);

        int unitsRendered = 0;
        int skippedNoOccluder = 0;
        int skippedNoSilhouette = 0;
        int authorizedRootCount = 0;
        bool buildLedger = buildDetailedMaskLedgerEveryRender || debugLogs;
        StringBuilder maskLedger = buildLedger ? new StringBuilder(1024) : null;
        if (buildLedger)
            maskLedger.Append("channel=").Append(channel).Append(", receiverCount=").Append(receiverBuffer.Count).AppendLine();

        for (int i = 0; i < receiverBuffer.Count; i++)
        {
            UnitOcclusionMaterialReceiver receiver = receiverBuffer[i];
            if (receiver == null)
                continue;

            occluderRootBuffer.Clear();
            receiver.CollectActiveFrontOccluderRoots(occluderRootBuffer);
            RemoveNullAndDuplicateRoots(occluderRootBuffer);

            if (buildLedger)
            {
                maskLedger.Append("receiver=").Append(GetPath(receiver.transform))
                    .Append(", active=").Append(receiver.CurrentOccluded)
                    .Append(", activeCount=").Append(receiver.ActiveOccluderCount)
                    .AppendLine();
                maskLedger.Append("  occluderRoots[").Append(occluderRootBuffer.Count).Append("]=").AppendLine();
                AppendRootPaths(maskLedger, occluderRootBuffer, "    ");
            }

            if (occluderRootBuffer.Count == 0)
            {
                skippedNoOccluder++;
                if (buildLedger)
                    maskLedger.AppendLine("  SKIP: no authorized FrontOccluderRoot");
                continue;
            }

            silhouetteRootBuffer.Clear();
            CollectReceiverSilhouetteRoots(receiver, channel, silhouetteRootBuffer);
            RemoveNullAndDuplicateRoots(silhouetteRootBuffer);
            if (buildLedger)
            {
                maskLedger.Append("  silhouetteRoots[").Append(silhouetteRootBuffer.Count).Append("]=").AppendLine();
                AppendRootPaths(maskLedger, silhouetteRootBuffer, "    ");
            }

            if (silhouetteRootBuffer.Count == 0)
            {
                skippedNoSilhouette++;
                if (buildLedger)
                    maskLedger.AppendLine("  SKIP: no silhouette / occluded outline proxy root");
                continue;
            }

            Camera cam = runtime.runtimeMaskCamera;
            int tempLayer = Mathf.Clamp(temporaryMaskLayer, 0, 31);
            cam.cullingMask = 1 << tempLayer;
            cam.enabled = false;

            RenderRootsIntoRT(cam, silhouetteRootBuffer, tempUnitSilhouetteRT, tempLayer);
            RenderRootsIntoRT(cam, occluderRootBuffer, tempUnitOccluderMaskRT, tempLayer);

            if (runtime.rawOccluderMaskRT != null)
                Graphics.Blit(tempUnitOccluderMaskRT, runtime.rawOccluderMaskRT, compositeMaterial, 1);

            compositeMaterial.SetTexture("_SilhouetteTex", tempUnitSilhouetteRT);
            compositeMaterial.SetTexture("_OccluderMaskTex", tempUnitOccluderMaskRT);
            compositeMaterial.SetFloat("_SilhouetteThreshold", unitSilhouetteThreshold);
            compositeMaterial.SetFloat("_MaskThreshold", occluderMaskThreshold);
            Graphics.Blit(null, tempUnitHiddenRT, compositeMaterial, 0);

            RenderTexture receiverHiddenRT = EnsurePerReceiverHiddenMaskRT(receiver, channel, runtime.maskRT.width, runtime.maskRT.height);
            if (receiverHiddenRT != null)
            {
                Graphics.Blit(tempUnitHiddenRT, receiverHiddenRT);
                perReceiverMaskTouchedThisFrame.Add(receiver);

                if (bindReceiverOwnedMaskToReceiver)
                    receiver.SetRuntimeAuthorityMaskTexture(receiverHiddenRT);
            }

            // The four channel overlay still receives the UNION of already-isolated unit hidden masks.
            // This is safe because each tempUnitHiddenRT has already been clipped by its own receiver silhouette
            // and its own authorized FrontOccluderRoots. It is NOT a raw same-channel occluder union.
            Graphics.Blit(tempUnitHiddenRT, runtime.maskRT, compositeMaterial, 1);

            unitsRendered++;
            authorizedRootCount += occluderRootBuffer.Count;
        }

        runtime.lastReceiverCount = receiverBuffer.Count;
        runtime.lastRenderedUnits = unitsRendered;
        runtime.lastAuthorizedRootCount = authorizedRootCount;

        string status = buildStatusStrings
            ? "units=" + receiverBuffer.Count + ", renderedUnits=" + unitsRendered + ", authorizedRoots=" + authorizedRootCount + ", noRoots=" + skippedNoOccluder + ", noSilhouette=" + skippedNoSilhouette
            : "";
        if (buildLedger)
        {
            maskLedger.Append("status=").Append(status).AppendLine();
            StoreMaskRenderLedger(channel, maskLedger.ToString());
        }
        return status;
    }

    private string RenderRawOccluderMaskOnlyForChannel(string channel, ChannelRuntime runtime, bool buildStatusStrings)
    {
        if (runtime == null || runtime.runtimeMaskCamera == null || runtime.rawOccluderMaskRT == null)
            return "raw fast path camera/rt missing";

        runtime.lastReceiverCount = 0;
        runtime.lastRenderedUnits = 0;
        runtime.lastAuthorizedRootCount = 0;

        // Collect first, clear second.
        // This is important: if the ledger briefly drops for a single frame during high-speed movement,
        // clearing the raw mask immediately makes the visible body pop back in. The mask is a visual-frame
        // product, so it is allowed to hold the previous valid frame for a tiny release window.
        receiverBuffer.Clear();
        CollectOccludedReceiversForChannel(channel, receiverBuffer);

        occluderRootBuffer.Clear();
        int receiversWithRoots = 0;
        for (int i = 0; i < receiverBuffer.Count; i++)
        {
            UnitOcclusionMaterialReceiver receiver = receiverBuffer[i];
            if (receiver == null)
                continue;

            int before = occluderRootBuffer.Count;
            receiver.CollectActiveFrontOccluderRoots(occluderRootBuffer);
            if (occluderRootBuffer.Count > before)
                receiversWithRoots++;
        }

        RemoveNullAndDuplicateRoots(occluderRootBuffer);

        runtime.lastReceiverCount = receiverBuffer.Count;
        runtime.lastAuthorizedRootCount = occluderRootBuffer.Count;

        if (occluderRootBuffer.Count > 0)
        {
            Camera cam = runtime.runtimeMaskCamera;
            SyncRuntimeMaskCameraFromTemplate(cam);

            int rootSignature = ComputeRawRootSignature(occluderRootBuffer);
            int cameraSignature = ComputeMaskCameraSignature(cam, runtime.rawOccluderMaskRT);
            bool periodicRefreshDue = forceRawMaskRefreshEveryFrames > 0 && Time.frameCount - runtime.lastRawMaskRenderFrame >= forceRawMaskRefreshEveryFrames;
            bool canReuseRawMask =
                reuseStableRawOccluderMask &&
                runtime.hasValidRawMask &&
                runtime.lastRawRootSignature == rootSignature &&
                runtime.lastRawCameraSignature == cameraSignature &&
                !periodicRefreshDue;

            runtime.lastRenderedUnits = Mathf.Max(1, receiversWithRoots);
            runtime.hasValidRawMask = true;
            runtime.lastValidRawMaskFrame = Time.frameCount;
            runtime.rawMaskAlreadyClearedWhenEmpty = false;

            if (canReuseRawMask)
            {
                runtime.lastRawMaskReuseFrame = Time.frameCount;
                runtime.rawMaskReuseCount++;
                return buildStatusStrings
                    ? "rawFastPath reused stable mask, receivers=" + receiverBuffer.Count + ", renderedUnits=" + runtime.lastRenderedUnits + ", authorizedRoots=" + occluderRootBuffer.Count + ", reuse=" + runtime.rawMaskReuseCount
                    : "";
            }

            // RenderRootsIntoRT clears rawOccluderMaskRT immediately before rendering.
            // Only clear legacy maskRT here so old UnitHidden consumers cannot display stale content.
            if (runtime.maskRT != null)
                ClearRT(runtime.maskRT);

            int tempLayer = Mathf.Clamp(temporaryMaskLayer, 0, 31);
            cam.cullingMask = 1 << tempLayer;
            cam.enabled = false;
            RenderRootsIntoRT(cam, occluderRootBuffer, runtime.rawOccluderMaskRT, tempLayer);

            runtime.lastRawRootSignature = rootSignature;
            runtime.lastRawCameraSignature = cameraSignature;
            runtime.lastRawMaskRenderFrame = Time.frameCount;
            runtime.rawMaskReuseCount = 0;

            return buildStatusStrings
                ? "rawFastPath rendered, receivers=" + receiverBuffer.Count + ", renderedUnits=" + runtime.lastRenderedUnits + ", authorizedRoots=" + occluderRootBuffer.Count
                : "";
        }

        bool mayHoldPreviousMask =
            holdLastValidRawMaskOnBriefLedgerDrop &&
            runtime.hasValidRawMask &&
            rawMaskReleaseGraceFrames > 0 &&
            Time.frameCount - runtime.lastValidRawMaskFrame <= rawMaskReleaseGraceFrames;

        if (mayHoldPreviousMask)
        {
            // Do not clear the raw mask. This is a fail-closed release grace, not a refresh.
            // It prevents one-frame reveal when Trigger/Receiver briefly reports empty during a continuous run.
            runtime.lastRenderedUnits = 1;
            runtime.lastHeldRawMaskFrame = Time.frameCount;
            return buildStatusStrings
                ? "rawFastPath held previous valid mask, receivers=" + receiverBuffer.Count + ", authorizedRoots=0, grace=" + (Time.frameCount - runtime.lastValidRawMaskFrame) + "/" + rawMaskReleaseGraceFrames
                : "";
        }

        if (!runtime.rawMaskAlreadyClearedWhenEmpty)
        {
            ClearRT(runtime.rawOccluderMaskRT);
            if (runtime.maskRT != null)
                ClearRT(runtime.maskRT);
            runtime.rawMaskAlreadyClearedWhenEmpty = true;
        }

        runtime.hasValidRawMask = false;
        runtime.lastRenderedUnits = 0;
        return buildStatusStrings
            ? "rawFastPath empty, receivers=" + receiverBuffer.Count + ", authorizedRoots=0"
            : "";
    }


    private int ComputeRawRootSignature(List<GameObject> roots)
    {
        unchecked
        {
            int hash = 17;
            if (roots != null)
            {
                for (int i = 0; i < roots.Count; i++)
                {
                    GameObject root = roots[i];
                    if (root == null)
                        continue;

                    Transform t = root.transform;
                    hash = hash * 31 + root.GetInstanceID();
                    hash = hash * 31 + (root.activeInHierarchy ? 1 : 0);
                    hash = hash * 31 + Quantize(t.position.x);
                    hash = hash * 31 + Quantize(t.position.y);
                    hash = hash * 31 + Quantize(t.position.z);
                    hash = hash * 31 + Quantize(t.rotation.eulerAngles.x);
                    hash = hash * 31 + Quantize(t.rotation.eulerAngles.y);
                    hash = hash * 31 + Quantize(t.rotation.eulerAngles.z);
                    hash = hash * 31 + Quantize(t.lossyScale.x);
                    hash = hash * 31 + Quantize(t.lossyScale.y);
                    hash = hash * 31 + Quantize(t.lossyScale.z);

                    // V55: the raw mask depends on which child renderers are actually enabled.
                    // Previously the signature only tracked root transform, so disabling Box/AutoClone
                    // could still reuse a stale mask that had been rendered with those fallback sources.
                    Renderer[] renderers = GetCachedRenderersForRoot(root);
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        Renderer renderer = renderers[r];
                        if (renderer == null)
                            continue;

                        hash = hash * 31 + renderer.GetInstanceID();
                        hash = hash * 31 + (renderer.enabled ? 1 : 0);
                        hash = hash * 31 + (renderer.gameObject.activeInHierarchy ? 1 : 0);
                        hash = hash * 31 + renderer.gameObject.layer;

                        Bounds b = renderer.bounds;
                        hash = hash * 31 + Quantize(b.center.x);
                        hash = hash * 31 + Quantize(b.center.y);
                        hash = hash * 31 + Quantize(b.center.z);
                        hash = hash * 31 + Quantize(b.size.x);
                        hash = hash * 31 + Quantize(b.size.y);
                        hash = hash * 31 + Quantize(b.size.z);

                        Material[] mats = renderer.sharedMaterials;
                        if (mats != null)
                        {
                            hash = hash * 31 + mats.Length;
                            for (int m = 0; m < mats.Length; m++)
                                hash = hash * 31 + (mats[m] != null ? mats[m].GetInstanceID() : 0);
                        }
                    }
                }
            }
            return hash;
        }
    }

    private static int ComputeMaskCameraSignature(Camera cam, RenderTexture target)
    {
        unchecked
        {
            int hash = 23;
            if (cam != null)
            {
                Transform t = cam.transform;
                hash = hash * 31 + Quantize(t.position.x);
                hash = hash * 31 + Quantize(t.position.y);
                hash = hash * 31 + Quantize(t.position.z);
                hash = hash * 31 + Quantize(t.rotation.eulerAngles.x);
                hash = hash * 31 + Quantize(t.rotation.eulerAngles.y);
                hash = hash * 31 + Quantize(t.rotation.eulerAngles.z);
                hash = hash * 31 + Quantize(cam.orthographicSize);
                hash = hash * 31 + Quantize(cam.aspect);
                hash = hash * 31 + Quantize(cam.nearClipPlane);
                hash = hash * 31 + Quantize(cam.farClipPlane);
                Rect r = cam.pixelRect;
                hash = hash * 31 + Quantize(r.x);
                hash = hash * 31 + Quantize(r.y);
                hash = hash * 31 + Quantize(r.width);
                hash = hash * 31 + Quantize(r.height);
            }

            if (target != null)
            {
                hash = hash * 31 + target.width;
                hash = hash * 31 + target.height;
            }

            return hash;
        }
    }

    private static int Quantize(float value)
    {
        return Mathf.RoundToInt(value * 1000f);
    }

    private void StoreMaskRenderLedger(string channel, string ledger)
    {
        if (string.Equals(channel, "Player", StringComparison.OrdinalIgnoreCase)) debugLastPlayerMaskRenderLedger = ledger;
        else if (string.Equals(channel, "Enemy", StringComparison.OrdinalIgnoreCase)) debugLastEnemyMaskRenderLedger = ledger;
        else if (string.Equals(channel, "Ally", StringComparison.OrdinalIgnoreCase)) debugLastAllyMaskRenderLedger = ledger;
        else if (string.Equals(channel, "Item", StringComparison.OrdinalIgnoreCase)) debugLastItemMaskRenderLedger = ledger;
    }

    private static void AppendRootPaths(StringBuilder sb, List<GameObject> roots, string indent)
    {
        if (sb == null || roots == null)
            return;
        for (int i = 0; i < roots.Count; i++)
        {
            GameObject root = roots[i];
            sb.Append(indent).Append(i).Append(": ").Append(root != null ? GetPath(root.transform) : "NULL").AppendLine();
        }
    }

    private void SyncRuntimeMaskCameraFromTemplate(Camera dst)
    {
        if (!syncRuntimeMaskCameraFromTemplateEveryRender || dst == null)
            return;

        Camera viewSrc = preferVisibleViewCameraForViewSync ? ResolveVisibleViewCamera() : null;
        if (viewSrc == null)
            viewSrc = occlusionMaskCameraTemplate;

        Camera settingSrc = occlusionMaskCameraTemplate != null ? occlusionMaskCameraTemplate : viewSrc;
        if (viewSrc == null && settingSrc == null)
            return;

        Camera src = viewSrc != null ? viewSrc : settingSrc;

        dst.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
        dst.transform.localScale = src.transform.localScale;

        dst.orthographic = src.orthographic;
        dst.orthographicSize = src.orthographicSize;
        dst.fieldOfView = src.fieldOfView;
        dst.nearClipPlane = src.nearClipPlane;
        dst.farClipPlane = src.farClipPlane;
        dst.rect = src.rect;
        dst.aspect = src.aspect;

        if (syncCameraProjectionMatrix)
            dst.projectionMatrix = src.projectionMatrix;
        else
            dst.ResetProjectionMatrix();

        if (syncCameraViewMatrix)
            dst.worldToCameraMatrix = src.worldToCameraMatrix;
        else
            dst.ResetWorldToCameraMatrix();

        if (settingSrc != null)
        {
            dst.clearFlags = settingSrc.clearFlags;
            dst.backgroundColor = settingSrc.backgroundColor;
            dst.allowHDR = settingSrc.allowHDR;
            dst.allowMSAA = settingSrc.allowMSAA;
            dst.useOcclusionCulling = settingSrc.useOcclusionCulling;
        }
    }

    private void RenderRootsIntoRT(Camera cam, List<GameObject> roots, RenderTexture target, int tempLayer)
    {
        if (cam == null || target == null)
            return;

        // V18: keep the manually rendered mask view locked to the real occlusion-mask camera.
        // The visible game camera can move after the runtime clones were created; if the clones
        // keep an old transform/projection, the white proxy mask appears slightly offset from the
        // actual map object during player movement.
        SyncRuntimeMaskCameraFromTemplate(cam);

        ClearRT(target);

        layerStates.Clear();
        activeStates.Clear();
        rendererStates.Clear();

        for (int i = 0; i < roots.Count; i++)
        {
            GameObject root = roots[i];
            if (root == null)
                continue;

            if (forceRootsActiveDuringRender)
            {
                activeStates.Add(new GameObjectActiveState { go = root, activeSelf = root.activeSelf });
                if (!root.activeSelf)
                    root.SetActive(true);
            }

            if (forceRenderersEnabledDuringRender && !respectDisabledProxyRenderersDuringRender)
                ForceRenderersEnabled(root, rendererStates);

            SetLayerRecursive(root, tempLayer, layerStates);
        }

        RenderTexture oldTarget = cam.targetTexture;
        int oldMask = cam.cullingMask;
        bool oldEnabled = cam.enabled;

        cam.enabled = false;
        cam.targetTexture = target;
        cam.cullingMask = 1 << tempLayer;
        cam.Render();

        cam.targetTexture = oldTarget;
        cam.cullingMask = oldMask;
        cam.enabled = oldEnabled;

        RestoreLayers(layerStates);
        RestoreRenderers(rendererStates);
        RestoreActiveStates(activeStates);
    }

    private void CollectOccludedReceiversForChannel(string channelName, List<UnitOcclusionMaterialReceiver> results)
    {
        if (results == null)
            return;

        RefreshReceiverCacheIfNeeded(false);

        for (int i = 0; i < receiverCache.Count; i++)
        {
            UnitOcclusionMaterialReceiver receiver = receiverCache[i];
            if (receiver == null || !receiver.isActiveAndEnabled)
                continue;

            if (!receiver.CurrentOccluded || receiver.ActiveOccluderCount <= 0)
                continue;

            if (!ReceiverBelongsToChannel(receiver, channelName))
                continue;

            results.Add(receiver);
        }
    }

    private void RefreshReceiverCacheIfNeeded(bool force)
    {
        if (!cacheReceiversForRuntime)
        {
            receiverCache.Clear();
            UnitOcclusionMaterialReceiver[] direct = UnityEngine.Object.FindObjectsByType<UnitOcclusionMaterialReceiver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < direct.Length; i++)
            {
                if (direct[i] != null)
                    receiverCache.Add(direct[i]);
            }
            return;
        }

        if (!force && Time.unscaledTime < nextReceiverCacheRefreshTime)
            return;

        receiverCache.Clear();
        UnitOcclusionMaterialReceiver[] receivers = UnityEngine.Object.FindObjectsByType<UnitOcclusionMaterialReceiver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < receivers.Length; i++)
        {
            UnitOcclusionMaterialReceiver receiver = receivers[i];
            if (receiver != null)
                receiverCache.Add(receiver);
        }

        nextReceiverCacheRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, receiverCacheRefreshInterval);
    }

    private void CollectReceiverSilhouetteRoots(UnitOcclusionMaterialReceiver receiver, string channelName, List<GameObject> results)
    {
        if (receiver == null || results == null)
            return;

        Transform root = receiver.transform;
        string exactName = "OutlineProxy_" + channelName + "_Occluded";

        if (preferExactChannelOccludedProxy)
        {
            Transform exact = FindDeepChildExact(root, exactName);
            if (exact != null)
            {
                results.Add(exact.gameObject);
                return;
            }
        }

        Transform outlineProxyRoot = FindDeepChildExact(root, "OutlineProxyRoot");
        if (outlineProxyRoot != null)
        {
            Transform exactUnderRoot = FindDeepChildExact(outlineProxyRoot, exactName);
            if (exactUnderRoot != null)
            {
                results.Add(exactUnderRoot.gameObject);
                return;
            }

            Transform[] children = outlineProxyRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null)
                    continue;
                if (child.name.IndexOf("_Occluded", StringComparison.OrdinalIgnoreCase) >= 0 && child.GetComponentInChildren<Renderer>(true) != null)
                    results.Add(child.gameObject);
            }
        }
    }

    private static bool ReceiverBelongsToChannel(UnitOcclusionMaterialReceiver receiver, string channelName)
    {
        if (receiver == null || string.IsNullOrEmpty(channelName))
            return false;

        string path = GetPath(receiver.transform);
        string rootedChannel = TryReadChannelFromPath(path);
        if (!string.IsNullOrEmpty(rootedChannel))
            return string.Equals(rootedChannel, channelName, StringComparison.OrdinalIgnoreCase);

        string tag = receiver.gameObject.tag;
        if (!string.IsNullOrEmpty(tag) && string.Equals(tag, channelName, StringComparison.OrdinalIgnoreCase))
            return true;

        string camp = TryReadCampFromGate(receiver);
        if (!string.IsNullOrEmpty(camp))
            return string.Equals(camp, channelName, StringComparison.OrdinalIgnoreCase);

        return path.IndexOf("/" + channelName + "/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               path.IndexOf("Unit_" + channelName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string TryReadChannelFromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (path.IndexOf("/PlayerRoot/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("UnitRoot/PlayerRoot/", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Player";
        if (path.IndexOf("/EnemyRoot/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("UnitRoot/EnemyRoot/", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Enemy";
        if (path.IndexOf("/AllyRoot/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("UnitRoot/AllyRoot/", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Ally";
        if (path.IndexOf("/ItemRoot/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("UnitRoot/ItemRoot/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("/DestructibleRoot/", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Item";

        return null;
    }

    private static string TryReadCampFromGate(UnitOcclusionMaterialReceiver receiver)
    {
        UnitOccludedOutlineProxyGate gate = receiver.GetComponent<UnitOccludedOutlineProxyGate>();
        if (gate == null)
            gate = receiver.GetComponentInParent<UnitOccludedOutlineProxyGate>();
        if (gate == null)
            return null;

        System.Reflection.FieldInfo field = typeof(UnitOccludedOutlineProxyGate).GetField("activeCamp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
            return null;

        object value = field.GetValue(gate);
        return value != null ? value.ToString() : null;
    }

    private void EnsureCompositeMaterial()
    {
        if (compositeMaterial != null)
            return;

        Material sourceMaterial = null;
        Shader sourceShader = null;
        string source = null;

        // 1. Formal serialized scene dependency.
        if (renderResources != null)
        {
            sourceMaterial = renderResources.UnitHiddenMaskCompositeMaterial;
            sourceShader = renderResources.UnitHiddenMaskCompositeShader;
            if (sourceMaterial != null)
                source = "RenderResources.Material:" + sourceMaterial.name;
            else if (sourceShader != null)
                source = "RenderResources.Shader:" + sourceShader.name;
            else
                debugCompositeResourceStatus = "renderResources assigned but composite material/shader is null";
        }
        else
        {
            debugCompositeResourceStatus = "renderResources field is null";
        }

        // 2. Formal Resources fallback. This is still an explicit asset, not global shader forcing.
        // Expected path: Assets/_Project/Resources/Occlusion/SkyPrisonOcclusionRenderResources.asset
        if (sourceMaterial == null && sourceShader == null && allowResourcesAssetFallback && !string.IsNullOrWhiteSpace(renderResourcesLoadPath))
        {
            SkyPrisonOcclusionRenderResources loaded = Resources.Load<SkyPrisonOcclusionRenderResources>(renderResourcesLoadPath);
            if (loaded != null)
            {
                if (renderResources == null)
                    renderResources = loaded;

                sourceMaterial = loaded.UnitHiddenMaskCompositeMaterial;
                sourceShader = loaded.UnitHiddenMaskCompositeShader;

                if (sourceMaterial != null)
                    source = "Resources.Material:" + sourceMaterial.name + " @ " + renderResourcesLoadPath;
                else if (sourceShader != null)
                    source = "Resources.Shader:" + sourceShader.name + " @ " + renderResourcesLoadPath;
                else
                    debugCompositeResourceStatus = "Resources asset loaded but composite material/shader is null @ " + renderResourcesLoadPath;
            }
            else
            {
                debugCompositeResourceStatus = "Resources asset not found @ " + renderResourcesLoadPath;
            }
        }

        // 3. Direct serialized overrides.
        if (sourceMaterial == null && unitHiddenMaskCompositeMaterial != null)
        {
            sourceMaterial = unitHiddenMaskCompositeMaterial;
            source = "DirectMaterial:" + sourceMaterial.name;
        }

        if (sourceMaterial == null && sourceShader == null && unitHiddenMaskCompositeShader != null)
        {
            sourceShader = unitHiddenMaskCompositeShader;
            source = "DirectShader:" + sourceShader.name;
        }

#if UNITY_EDITOR
        // 4. Editor-only diagnosis fallback. Never rely on this as the production path.
        if (sourceMaterial == null && sourceShader == null && allowEditorShaderFindFallback && !Application.isPlaying)
        {
            sourceShader = Shader.Find(unitHiddenMaskCompositeShaderName);
            if (sourceShader != null)
                source = "EditorShaderFind:" + sourceShader.name;
        }
#endif

        if (sourceMaterial != null)
        {
            compositeMaterial = new Material(sourceMaterial)
            {
                name = "M_UnitHiddenMaskComposite_Runtime"
            };
            debugCompositeResourceStatus = "ok from " + source;
            debugWarning = "";
            if (debugLogs)
                Debug.Log("[ScreenSpaceOutlineRTManager] UnitHiddenMaskComposite material created from " + source, this);
            return;
        }

        if (sourceShader != null)
        {
            compositeMaterial = new Material(sourceShader)
            {
                name = "M_UnitHiddenMaskComposite_Runtime"
            };
            debugCompositeResourceStatus = "ok from " + source;
            debugWarning = "";
            if (debugLogs)
                Debug.Log("[ScreenSpaceOutlineRTManager] UnitHiddenMaskComposite material created from " + source, this);
            return;
        }

        if (string.IsNullOrEmpty(debugCompositeResourceStatus))
            debugCompositeResourceStatus = "missing render resources and direct composite material/shader";

        debugWarning = "Missing UnitHiddenMaskComposite material. " + debugCompositeResourceStatus;
        if (debugLogs)
            Debug.LogWarning("[ScreenSpaceOutlineRTManager] " + debugWarning, this);
    }


    private static bool IsRuntimeInstanceFor(Material runtimeMaterial, Material sourceMaterial, string channel)
    {
        if (runtimeMaterial == null || sourceMaterial == null)
            return false;

        return runtimeMaterial.name.StartsWith(sourceMaterial.name, StringComparison.Ordinal) && runtimeMaterial.name.Contains(channel + "_UnitHiddenMask_Runtime");
    }

    private void EnsureOverlayRawImageRectAuthority(RawImage image, string channel)
    {
        if (!forceOverlayRawImagesToFullScreen || image == null)
            return;

        if (forceOverlayParentRectsToFullScreen)
            EnsureOverlayParentRectAuthority(image, channel);

        bool changed = EnsureRectTransformFullStretch(image.rectTransform);

        if (forceOverlayRawImageUvFullRect && image.uvRect != new Rect(0f, 0f, 1f, 1f))
        {
            image.uvRect = new Rect(0f, 0f, 1f, 1f);
            changed = true;
        }

        if (changed)
        {
            image.SetVerticesDirty();
            image.SetMaterialDirty();

            Canvas canvas = image.canvas;
            if (canvas != null)
                Canvas.ForceUpdateCanvases();

            if (debugLogOverlayRectFix)
                Debug.Log("[ScreenSpaceOutlineRTManager V36] Fullscreen overlay rect fixed for " + channel + ": " + GetPath(image.transform), image);
        }
    }

    private void EnsureOverlayParentRectAuthority(RawImage image, string channel)
    {
        if (image == null)
            return;

        Canvas canvas = image.canvas;
        Transform canvasTransform = canvas != null ? canvas.transform : null;
        Transform cursor = image.transform.parent;
        bool changed = false;

        while (cursor != null && cursor != canvasTransform)
        {
            RectTransform parentRect = cursor as RectTransform;
            if (parentRect != null && IsOverlayRectAuthorityTarget(cursor, image.transform))
                changed |= EnsureRectTransformFullStretch(parentRect);

            cursor = cursor.parent;
        }

        // If the Canvas itself is nested under a RectTransform and is not a root screen-space canvas,
        // stretch it too. Root screen-space canvases are driven by Unity, so we leave them alone.
        if (canvas != null && !canvas.isRootCanvas)
        {
            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect != null)
                changed |= EnsureRectTransformFullStretch(canvasRect);
        }

        if (changed)
        {
            Canvas.ForceUpdateCanvases();

            if (debugLogOverlayRectFix)
                Debug.Log("[ScreenSpaceOutlineRTManager V36] Fullscreen overlay parent rect fixed for " + channel + ": " + GetPath(image.transform), image);
        }
    }

    private bool IsOverlayRectAuthorityTarget(Transform candidate, Transform imageTransform)
    {
        if (candidate == null)
            return false;

        string name = candidate.name;
        if (!string.IsNullOrEmpty(overlayRootName) && name.IndexOf(overlayRootName, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // The direct parent of the four RawImages is part of the overlay display surface.
        // This catches renamed containers, while avoiding unrelated health bars or other UI branches.
        if (imageTransform != null && candidate == imageTransform.parent)
            return true;

        string path = GetPath(candidate);
        return !string.IsNullOrEmpty(overlayRootName) && path.IndexOf("/" + overlayRootName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool EnsureRectTransformFullStretch(RectTransform rt)
    {
        if (rt == null)
            return false;

        bool changed = false;

        if (rt.anchorMin != Vector2.zero)
        {
            rt.anchorMin = Vector2.zero;
            changed = true;
        }

        if (rt.anchorMax != Vector2.one)
        {
            rt.anchorMax = Vector2.one;
            changed = true;
        }

        Vector2 centerPivot = new Vector2(0.5f, 0.5f);
        if (rt.pivot != centerPivot)
        {
            rt.pivot = centerPivot;
            changed = true;
        }

        if (rt.anchoredPosition != Vector2.zero)
        {
            rt.anchoredPosition = Vector2.zero;
            changed = true;
        }

        if (rt.sizeDelta != Vector2.zero)
        {
            rt.sizeDelta = Vector2.zero;
            changed = true;
        }

        if (rt.offsetMin != Vector2.zero)
        {
            rt.offsetMin = Vector2.zero;
            changed = true;
        }

        if (rt.offsetMax != Vector2.zero)
        {
            rt.offsetMax = Vector2.zero;
            changed = true;
        }

        if (rt.localScale != Vector3.one)
        {
            rt.localScale = Vector3.one;
            changed = true;
        }

        return changed;
    }


    private string DescribeProducerCameras(string channel)
    {
        Camera src = ResolveOutlineProducerCamera(channel);
        Camera gate = ResolveOccludedProducerCamera(channel);
        return "outline=" + DescribeProducerCamera(src) + " gate=" + DescribeProducerCamera(gate);
    }

    private static string DescribeProducerCamera(Camera cam)
    {
        if (cam == null)
            return "NULL";
        Texture target = cam.targetTexture;
        return cam.name + " enabled=" + cam.enabled + " target=" + (target != null ? target.name : "NULL");
    }

    private static string DescribeOverlayRect(RawImage image)
    {
        if (image == null || image.rectTransform == null)
            return "NULL";

        RectTransform rt = image.rectTransform;
        Rect rect = rt.rect;
        RectTransform parentRt = rt.parent as RectTransform;
        Rect parentRect = parentRt != null ? parentRt.rect : default(Rect);
        Canvas canvas = image.canvas;
        RectTransform canvasRt = canvas != null ? canvas.transform as RectTransform : null;
        Rect canvasRect = canvasRt != null ? canvasRt.rect : default(Rect);
        return "anchorMin=" + rt.anchorMin.ToString("F3")
               + " anchorMax=" + rt.anchorMax.ToString("F3")
               + " pos=" + rt.anchoredPosition.ToString("F1")
               + " sizeDelta=" + rt.sizeDelta.ToString("F1")
               + " rect=" + rect.width.ToString("F1") + "x" + rect.height.ToString("F1")
               + " parent=" + (parentRt != null ? GetPath(parentRt) + " " + parentRect.width.ToString("F1") + "x" + parentRect.height.ToString("F1") : "NULL")
               + " canvas=" + (canvasRt != null ? GetPath(canvasRt) + " " + canvasRect.width.ToString("F1") + "x" + canvasRect.height.ToString("F1") + " mode=" + canvas.renderMode : "NULL")
               + " uv=" + image.uvRect;
    }

    private Material EnsureOverlayRuntimeMaterial(ChannelRuntime runtime, string channel)
    {
        if (runtime == null || runtime.overlayImage == null || runtime.sourceMaterial == null)
            return null;

        if (!forceRuntimeMaterialInstance)
        {
            runtime.runtimeMaterial = runtime.overlayImage.material != null ? runtime.overlayImage.material : runtime.sourceMaterial;
            if (runtime.overlayImage.material != runtime.runtimeMaterial)
                runtime.overlayImage.material = runtime.runtimeMaterial;
            return runtime.runtimeMaterial;
        }

        bool needNew = runtime.runtimeMaterial == null || !IsRuntimeInstanceFor(runtime.runtimeMaterial, runtime.sourceMaterial, channel);

        // If the RawImage already has the correct runtime instance, reuse it.
        Material current = runtime.overlayImage.material;
        if (needNew && current != null && IsRuntimeInstanceFor(current, runtime.sourceMaterial, channel))
        {
            runtime.runtimeMaterial = current;
            needNew = false;
        }

        if (needNew)
        {
            if (runtime.runtimeMaterial != null)
                Destroy(runtime.runtimeMaterial);

            runtime.runtimeMaterial = new Material(runtime.sourceMaterial);
            runtime.runtimeMaterial.name = runtime.sourceMaterial.name + "_" + channel + "_UnitHiddenMask_Runtime";
        }

        if (runtime.overlayImage.material != runtime.runtimeMaterial)
            runtime.overlayImage.material = runtime.runtimeMaterial;

        return runtime.runtimeMaterial;
    }

    private static void ForceOverlayGraphicMaterial(RawImage image, Material material)
    {
        if (image == null || material == null)
            return;

        if (image.material != material)
            image.material = material;

        // Force the UI renderer to rebuild from this material instance this frame.
        image.SetMaterialDirty();
        image.SetVerticesDirty();

        CanvasRenderer canvasRenderer = image.canvasRenderer;
        if (canvasRenderer != null)
            canvasRenderer.SetMaterial(material, image.mainTexture);
    }

    private void BindTextureAliases(Material mat, string[] propertyNames, Texture tex)
    {
        if (mat == null)
            return;

        for (int i = 0; i < propertyNames.Length; i++)
        {
            if (!bindAliasProperties && i > 0)
                continue;

            string propertyName = propertyNames[i];
            if (mat.HasProperty(propertyName))
                mat.SetTexture(propertyName, tex);
        }
    }


    private void ResolveOutlineProducerCameras()
    {
        if (!renderOutlineProducerCamerasForActiveChannels)
            return;

        ResolveOutlineProducerCamera("Player");
        ResolveOutlineProducerCamera("Enemy");
        ResolveOutlineProducerCamera("Ally");
        ResolveOutlineProducerCamera("Item");
    }

    private Camera ResolveOutlineProducerCamera(string channel)
    {
        if (string.IsNullOrEmpty(channel))
            return null;

        Camera cam;
        if (outlineProducerCameraByChannel.TryGetValue(channel, out cam) && cam != null)
            return cam;

        string cameraName = string.Format(outlineCameraNamePattern, channel);
        cam = FindCameraByName(cameraName);
        if (cam != null)
            outlineProducerCameraByChannel[channel] = cam;
        return cam;
    }

    private Camera ResolveOccludedProducerCamera(string channel)
    {
        if (string.IsNullOrEmpty(channel))
            return null;

        Camera cam;
        if (occludedProducerCameraByChannel.TryGetValue(channel, out cam) && cam != null)
            return cam;

        string cameraName = string.Format(occludedOutlineCameraNamePattern, channel);
        cam = FindCameraByName(cameraName);
        if (cam != null)
            occludedProducerCameraByChannel[channel] = cam;
        return cam;
    }

    private void SleepInactiveProducerCamerasIfNeeded(bool force)
    {
        if (!sleepInactiveProducerCameras)
            return;

        if (!force && Time.unscaledTime < nextInactiveProducerCameraSleepTime)
            return;

        nextInactiveProducerCameraSleepTime = Time.unscaledTime + Mathf.Max(0.05f, inactiveProducerCameraSleepInterval);

        SleepProducerCamerasForInactiveChannel("Player", bindPlayer);
        SleepProducerCamerasForInactiveChannel("Enemy", bindEnemy);
        SleepProducerCamerasForInactiveChannel("Ally", bindAlly);
        SleepProducerCamerasForInactiveChannel("Item", bindItem);
    }

    private void SleepProducerCamerasForInactiveChannel(string channel, bool channelBound)
    {
        ChannelRuntime runtime;
        bool active = channelBound && runtimeByChannel.TryGetValue(channel, out runtime) && runtime != null && runtime.lastRenderedUnits > 0;
        if (active)
            return;

        Camera outline = ResolveOutlineProducerCamera(channel);
        if (outline != null && outline.enabled)
            outline.enabled = false;

        Camera occluded = ResolveOccludedProducerCamera(channel);
        if (occluded != null && occluded.enabled)
            occluded.enabled = false;
    }

    private void RenderOutlineProducerCamerasForChannel(string channel)
    {
        if (!renderOutlineProducerCamerasForActiveChannels)
            return;

        // Rule: producer RTs must be generated before the UI composite consumes them.
        // This is a deterministic dependency sync for the active channel only; it is not a global brute-force refresh.
        RenderProducerCamera(ResolveOutlineProducerCamera(channel));
        RenderProducerCamera(ResolveOccludedProducerCamera(channel));
    }

    private void RenderProducerCamera(Camera cam)
    {
        if (cam == null || cam.targetTexture == null)
            return;

        if (syncProducerCamerasFromVisibleViewCamera)
            SyncProducerCameraFromVisibleView(cam);

        bool oldEnabled = cam.enabled;
        cam.enabled = false;
        cam.Render();

        if (!disableProducerCamerasAfterManualRender)
            cam.enabled = oldEnabled;
    }

    private void SyncProducerCameraFromVisibleView(Camera cam)
    {
        if (cam == null)
            return;

        Camera view = ResolveVisibleViewCamera();
        if (view == null || view == cam)
            return;

        cam.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
        cam.transform.localScale = view.transform.localScale;
        cam.orthographic = view.orthographic;
        cam.orthographicSize = view.orthographicSize;
        cam.fieldOfView = view.fieldOfView;
        cam.nearClipPlane = view.nearClipPlane;
        cam.farClipPlane = view.farClipPlane;
        cam.rect = view.rect;
        cam.aspect = view.aspect;
        cam.projectionMatrix = view.projectionMatrix;
        cam.worldToCameraMatrix = view.worldToCameraMatrix;
    }

    private Texture FindGateTexture(string channel)
    {
        if (gateTextureCache.TryGetValue(channel, out Texture cached) && cached != null)
            return cached;

        string gateName = string.Format(gateRTNamePattern, channel);
        RenderTexture[] allRTs = Resources.FindObjectsOfTypeAll<RenderTexture>();
        for (int i = 0; i < allRTs.Length; i++)
        {
            RenderTexture rt = allRTs[i];
            if (rt != null && rt.name == gateName)
            {
                gateTextureCache[channel] = rt;
                return rt;
            }
        }

        Camera cam = FindCameraByName("OutlineCamera_" + channel + "_Occluded");
        if (cam != null && cam.targetTexture != null)
        {
            gateTextureCache[channel] = cam.targetTexture;
            return cam.targetTexture;
        }

        return null;
    }


    private RenderTexture EnsurePerReceiverHiddenMaskRT(UnitOcclusionMaterialReceiver receiver, string channel, int width, int height)
    {
        if (receiver == null || width <= 0 || height <= 0)
            return null;

        RenderTexture rt;
        if (perReceiverHiddenMaskRT.TryGetValue(receiver, out rt) && rt != null && rt.width == width && rt.height == height)
            return rt;

        if (rt != null)
        {
            if (rt.IsCreated())
                rt.Release();
            Destroy(rt);
        }

        string safeChannel = string.IsNullOrWhiteSpace(channel) ? "Unit" : channel.Trim();
        string safeName = receiver.name;
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "Receiver";
        safeName = safeName.Replace("/", "_").Replace("\\", "_").Replace(" ", "_");

        rt = CreateRT("RT_UnitHiddenMask_" + safeChannel + "_" + safeName + "_" + receiver.GetInstanceID(), width, height, 0);
        perReceiverHiddenMaskRT[receiver] = rt;
        return rt;
    }


    private void ReleaseUntouchedPerReceiverMasks()
    {
        if (perReceiverHiddenMaskRT.Count == 0)
            return;

        receiverReleaseBuffer.Clear();
        foreach (KeyValuePair<UnitOcclusionMaterialReceiver, RenderTexture> pair in perReceiverHiddenMaskRT)
        {
            UnitOcclusionMaterialReceiver receiver = pair.Key;
            if (receiver == null || !perReceiverMaskTouchedThisFrame.Contains(receiver))
                receiverReleaseBuffer.Add(receiver);
        }

        for (int i = 0; i < receiverReleaseBuffer.Count; i++)
        {
            UnitOcclusionMaterialReceiver receiver = receiverReleaseBuffer[i];
            RenderTexture rt;
            if (receiver != null)
                receiver.SetRuntimeAuthorityMaskTexture(null);

            if (perReceiverHiddenMaskRT.TryGetValue(receiver, out rt) && rt != null)
            {
                if (rt.IsCreated())
                    rt.Release();
                Destroy(rt);
            }

            perReceiverHiddenMaskRT.Remove(receiver);
        }

        receiverReleaseBuffer.Clear();
    }

    private void ReleaseAllPerReceiverHiddenMaskRTs()
    {
        foreach (KeyValuePair<UnitOcclusionMaterialReceiver, RenderTexture> pair in perReceiverHiddenMaskRT)
        {
            if (pair.Key != null)
                pair.Key.SetRuntimeAuthorityMaskTexture(null);

            RenderTexture rt = pair.Value;
            if (rt == null)
                continue;

            if (rt.IsCreated())
                rt.Release();
            Destroy(rt);
        }

        perReceiverHiddenMaskRT.Clear();
        perReceiverMaskTouchedThisFrame.Clear();
        receiverReleaseBuffer.Clear();
    }

    private RenderTexture CreateRT(string rtName, int width, int height, int depth)
    {
        RenderTexture rt = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32);
        rt.name = rtName;
        rt.filterMode = filterMode;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.useMipMap = false;
        rt.autoGenerateMips = false;

        // 2026-07-19：Unity 6 的 RenderGraph 校验相机 targetTexture 时看的是新版
        // depthStencilFormat 字段，不是这里传的旧版 depth 位深参数——旧版构造函数不会
        // 自动把新字段填上，结果就是 depth>0 但 depthStencilFormat 仍是 None，
        // RenderGraph 把这次 Camera.Render() 判定为非法输出直接拒绝/产出空白，
        // C# 这边的计数逻辑完全不知道 GPU 那边没画出东西，一直误报"渲染成功"。
        // 控制台刷屏的 "output Render Texture must have a depth buffer" 警告就是这个。
        if (depth > 0)
            rt.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt;

        rt.Create();
        return rt;
    }

    private static void ClearRT(RenderTexture rt)
    {
        if (rt == null)
            return;

        RenderTexture old = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = old;
    }

    private static void ReleaseRT(ref RenderTexture rt)
    {
        if (rt == null)
            return;

        if (rt.IsCreated())
            rt.Release();

        Destroy(rt);
        rt = null;
    }

    private static Camera FindCameraByName(string cameraName)
    {
        if (string.IsNullOrEmpty(cameraName))
            return null;

        Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam != null && cam.name == cameraName && IsSceneObject(cam.gameObject))
                return cam;
        }

        return null;
    }

    private RawImage FindRawImageByName(string imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName))
            return null;

        string target = imageName.Trim();

        // 1) Most robust path: search every loaded RawImage, including inactive UI.
        // Do NOT reject by scene here. In Play Mode, UI objects can be in DontDestroyOnLoad
        // or prefab-stage-like loaded contexts; the previous IsSceneObject guard caused
        // all four Overlay images to resolve as NULL even though they were visible in Hierarchy.
        RawImage[] images = Resources.FindObjectsOfTypeAll<RawImage>();
        RawImage exactFallback = null;
        RawImage looseFallback = null;

        for (int i = 0; i < images.Length; i++)
        {
            RawImage image = images[i];
            if (image == null)
                continue;

            string name = image.name != null ? image.name.Trim() : "";
            bool exact = string.Equals(name, target, StringComparison.Ordinal);
            bool loose = !exact && name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!exact && !loose)
                continue;

            string path = GetPath(image.transform);

            // Prefer the real battle HUD overlay path.
            if (exact && !string.IsNullOrEmpty(overlayRootName) &&
                path.IndexOf("/" + overlayRootName + "/", StringComparison.OrdinalIgnoreCase) >= 0)
                return image;

            // Prefer Canvas objects over project/prefab remnants.
            if (exact && path.IndexOf("Canvas/", StringComparison.OrdinalIgnoreCase) >= 0)
                exactFallback = image;
            else if (exact && exactFallback == null)
                exactFallback = image;
            else if (loose && looseFallback == null)
                looseFallback = image;
        }

        if (exactFallback != null)
            return exactFallback;
        if (looseFallback != null)
            return looseFallback;

        // 2) Last fallback: search loaded Canvas transforms manually.
        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null)
                continue;

            Transform t = FindDeepChildExact(canvas.transform, target);
            if (t == null)
                continue;

            RawImage raw = t.GetComponent<RawImage>();
            if (raw != null)
                return raw;
        }

        return null;
    }

    private void SetLayerRecursive(GameObject root, int layer, List<GameObjectLayerState> states)
    {
        if (root == null)
            return;

        Transform[] transforms = GetCachedTransformsForRoot(root);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            if (t == null)
                continue;

            GameObject go = t.gameObject;
            if (go == null)
                continue;

            states.Add(new GameObjectLayerState { go = go, layer = go.layer });
            go.layer = layer;
        }
    }

    private void ForceRenderersEnabled(GameObject root, List<RendererState> states)
    {
        if (root == null || states == null)
            return;

        Renderer[] renderers = GetCachedRenderersForRoot(root);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;
            states.Add(new RendererState { renderer = r, enabled = r.enabled });
            r.enabled = true;
        }
    }

    private Transform[] GetCachedTransformsForRoot(GameObject root)
    {
        RootTraversalCacheEntry entry = GetOrRefreshRootTraversalCache(root);
        return entry != null && entry.transforms != null ? entry.transforms : Array.Empty<Transform>();
    }

    private Renderer[] GetCachedRenderersForRoot(GameObject root)
    {
        RootTraversalCacheEntry entry = GetOrRefreshRootTraversalCache(root);
        return entry != null && entry.renderers != null ? entry.renderers : Array.Empty<Renderer>();
    }

    private RootTraversalCacheEntry GetOrRefreshRootTraversalCache(GameObject root)
    {
        if (root == null)
            return null;

        if (!cacheRenderRootTraversal)
        {
            return new RootTraversalCacheEntry
            {
                transforms = root.GetComponentsInChildren<Transform>(true),
                renderers = root.GetComponentsInChildren<Renderer>(true),
                nextRefreshTime = Time.unscaledTime + renderRootTraversalCacheRefreshInterval,
                lastUsedFrame = Time.frameCount
            };
        }

        int id = root.GetInstanceID();
        float now = Time.unscaledTime;

        RootTraversalCacheEntry entry;
        if (!rootTraversalCache.TryGetValue(id, out entry) || entry == null)
        {
            entry = new RootTraversalCacheEntry();
            rootTraversalCache[id] = entry;
        }

        if (entry.transforms == null || entry.renderers == null || now >= entry.nextRefreshTime)
        {
            entry.transforms = root.GetComponentsInChildren<Transform>(true);
            entry.renderers = root.GetComponentsInChildren<Renderer>(true);
            entry.nextRefreshTime = now + Mathf.Max(0.05f, renderRootTraversalCacheRefreshInterval);
        }

        entry.lastUsedFrame = Time.frameCount;
        return entry;
    }

    private void PruneRootTraversalCacheIfNeeded()
    {
        if (!cacheRenderRootTraversal || rootTraversalCache.Count == 0)
            return;

        // Cheap long-session cleanup. It runs only occasionally and prevents destroyed/unused roots from growing the cache.
        if ((Time.frameCount & 127) != 0)
            return;

        int limit = Mathf.Max(30, renderRootTraversalCacheUnusedFrameLimit);
        List<int> removeKeys = null;
        foreach (KeyValuePair<int, RootTraversalCacheEntry> pair in rootTraversalCache)
        {
            RootTraversalCacheEntry entry = pair.Value;
            if (entry == null || Time.frameCount - entry.lastUsedFrame > limit)
            {
                if (removeKeys == null)
                    removeKeys = new List<int>();
                removeKeys.Add(pair.Key);
            }
        }

        if (removeKeys == null)
            return;

        for (int i = 0; i < removeKeys.Count; i++)
            rootTraversalCache.Remove(removeKeys[i]);
    }

    private static void RestoreLayers(List<GameObjectLayerState> states)
    {
        for (int i = states.Count - 1; i >= 0; i--)
        {
            GameObjectLayerState state = states[i];
            if (state.go != null)
                state.go.layer = state.layer;
        }
        states.Clear();
    }

    private static void RestoreActiveStates(List<GameObjectActiveState> states)
    {
        for (int i = states.Count - 1; i >= 0; i--)
        {
            GameObjectActiveState state = states[i];
            if (state.go != null)
                state.go.SetActive(state.activeSelf);
        }
        states.Clear();
    }

    private static void RestoreRenderers(List<RendererState> states)
    {
        for (int i = states.Count - 1; i >= 0; i--)
        {
            RendererState state = states[i];
            if (state.renderer != null)
                state.renderer.enabled = state.enabled;
        }
        states.Clear();
    }

    private static Transform FindDeepChildExact(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child != null && child.name == name)
                return child;
        }

        return null;
    }

    private static void RemoveNullAndDuplicateRoots(List<GameObject> roots)
    {
        if (roots == null)
            return;

        for (int i = roots.Count - 1; i >= 0; i--)
        {
            if (roots[i] == null)
                roots.RemoveAt(i);
        }

        for (int i = roots.Count - 1; i >= 0; i--)
        {
            GameObject root = roots[i];
            for (int j = 0; j < i; j++)
            {
                if (roots[j] == root)
                {
                    roots.RemoveAt(i);
                    break;
                }
            }
        }
    }

    private static bool IsSceneObject(GameObject go)
    {
        return go != null && go.scene.IsValid();
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return "";

        string path = t.name;
        Transform p = t.parent;
        while (p != null)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }
}
