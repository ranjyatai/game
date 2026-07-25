// SkyPrisonMainCameraHiddenMaskOutlineFeature_V23_MainCameraVisualShadowMask.cs
// 2026-06-06
// Clean mainline only:
//   Spine real alpha shape        -> CharacterMask RT
//   Occluder real/proxy shape     -> OccluderMask RT
//   CharacterMask * OccluderMask  -> HiddenMask RT
//   HiddenMask is bound back to Spine/SpineOcclusionComposite for body clip.
//   Final formal outline is drawn by the same proven HiddenEdge overlay path, for every enabled faction channel.
// Optional debug overlay is inside this same Feature and uses the composite shader's DebugMaskOverlay pass.
// V7 fix: official final outline uses one overlay material instance per faction channel.
// V8 fix: CharacterMask is UNIT-AUTHORIZED.
// V15 fix: CharacterMask is drawn from authorized OccludedProxy renderers, with authorized Spine body fallback.
// V17 fix: Build-safe public Gate state. Avoid private-field reflection for UnitOccludedOutlineProxyGate in Player builds / IL2CPP.
// V20 fix: formal final overlay uses the same live CharacterRT x unit-authorized OccluderRT inputs as Debug HiddenEdge.
//          Do not draw final overlay from hiddenRT, because hiddenRT can be empty when composite pass is skipped or stale.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using System.Reflection;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class SkyPrisonMainCameraHiddenMaskOutlineFeature_V18_BuildSafePublicGateState_CompileFix : ScriptableRendererFeature
{
    public enum DebugViewMode
    {
        Off = 0,
        CharacterMask = 1,
        OccluderMask = 2,
        HiddenMask = 3,
        HiddenEdge = 4,
    }

    public enum DebugChannel
    {
        Player = 0,
        Enemy = 1,
        Item = 2,
        Ally = 3,
    }

    [Serializable]
    public sealed class ChannelSettings
    {
        public bool enabled = true;
        public string channelName = "Player";
        public string characterMaskRTName = "RT_MainCameraCharacterMask_Player";
        public string hiddenMaskRTName = "RT_MainCameraHiddenMask_Player";
        public Color outlineColor = new Color(1f, 0.83f, 0f, 1f);
        public string pathKeywords = "Player;PlayerRuntime";
    }

    [Serializable]
    public sealed class Settings
    {
        [Header("Version")]
        public string scriptVersion = "V23 - 2026-06-06 - pixel shadow mask from main-camera visual geometry, not proxy shape.";

        [Header("Camera Filter")]
        public bool onlyMainCamera = true;
        public string requiredCameraName = "Main Camera";

        [Header("Pass Events")]
        public RenderPassEvent characterMaskEvent = RenderPassEvent.BeforeRenderingTransparents;
        public RenderPassEvent occluderMaskEvent = RenderPassEvent.BeforeRenderingTransparents;
        public RenderPassEvent hiddenCompositeEvent = RenderPassEvent.BeforeRenderingTransparents;
        public RenderPassEvent bodyBindEvent = RenderPassEvent.BeforeRenderingTransparents;
        public RenderPassEvent finalOutlineEvent = RenderPassEvent.AfterRenderingTransparents;
        public RenderPassEvent debugOverlayEvent = RenderPassEvent.AfterRenderingTransparents;

        [Header("Channels")]
        public ChannelSettings player = new ChannelSettings
        {
            enabled = true,
            channelName = "Player",
            characterMaskRTName = "RT_MainCameraCharacterMask_Player",
            hiddenMaskRTName = "RT_MainCameraHiddenMask_Player",
            outlineColor = new Color(1f, 0.83f, 0f, 1f),
            pathKeywords = "Player;PlayerRuntime"
        };
        public ChannelSettings enemy = new ChannelSettings
        {
            enabled = true,
            channelName = "Enemy",
            characterMaskRTName = "RT_MainCameraCharacterMask_Enemy",
            hiddenMaskRTName = "RT_MainCameraHiddenMask_Enemy",
            outlineColor = new Color(1f, 0.16f, 0.16f, 1f),
            pathKeywords = "Enemy;Mob;Monster"
        };
        public ChannelSettings item = new ChannelSettings
        {
            enabled = true,
            channelName = "Item",
            characterMaskRTName = "RT_MainCameraCharacterMask_Item",
            hiddenMaskRTName = "RT_MainCameraHiddenMask_Item",
            outlineColor = Color.white,
            pathKeywords = "Item;Drop;Loot;Pickup"
        };
        public ChannelSettings ally = new ChannelSettings
        {
            enabled = true,
            channelName = "Ally",
            characterMaskRTName = "RT_MainCameraCharacterMask_Ally",
            hiddenMaskRTName = "RT_MainCameraHiddenMask_Ally",
            outlineColor = new Color(0.56f, 0.86f, 1f, 1f),
            pathKeywords = "Ally;Friend"
        };

        [Header("Spine Source")]
        public string spineOcclusionShaderName = "Spine/SpineOcclusionComposite";
        [Tooltip("SpineOcclusionComposite pass index: 1 = FullAlphaMaskOnly.")]
        public int fullAlphaMaskPassIndex = 1;
        public bool autoConfigureMaterialsInPlayMode = true;
        public bool configureOnlyOncePerRenderer = false;
        public bool forceMaskYSettings = true;

        [Header("Unit-Level Character Mask Authorization")]
        [Tooltip("ON = CharacterMask is drawn only from active OutlineProxy_*_Occluded renderers. This prevents rear units from making front units participate in the same occluder mask.")]
        public bool characterMaskFromAuthorizedOccludedProxyOnly = true;
        [Tooltip("Required path keyword for authorized unit mask proxies.")]
        public string occludedProxyRootKeyword = "OutlineProxyRoot";
        [Tooltip("Required renderer path suffix/keyword for authorized unit mask proxies.")]
        public string occludedProxyKeyword = "_Occluded";
        [Tooltip("ON = collect OccludedProxy renderers directly from UnitOccludedOutlineProxyGate.currentOccluded + activeCamp. This is stricter and avoids path-only guesswork.")]
        public bool collectAuthorizedProxyFromGateState = true;
        [Tooltip("ON = draw the authorized proxy renderer into CharacterMask even if Renderer.enabled is false. Some old proxy controllers leave renderers disabled while the GameObject is active.")]
        public bool drawDisabledAuthorizedProxyRenderers = true;
        [Tooltip("ON = allow CharacterMask to draw proxy renderers even if the proxy GameObject is currently inactive but its Gate says currentOccluded. Normally this should not be needed; it is a safety net for old structures.")]
        public bool drawInactiveAuthorizedProxyRenderers = true;
        [Tooltip("ON = after a UnitOccludedOutlineProxyGate authorizes this unit, also draw that same unit's real Spine body renderer into CharacterMask. This keeps unit-level authorization while avoiding empty masks when old OccludedProxy renderers are inactive, disabled, or not renderable.")]
        public bool alsoDrawAuthorizedSpineBodyFromGate = true;

        [Header("Occluder Source")]
        public string occluderLayerName = "OcclusionMask";
        public bool requireRendererEnabled = true;
        public bool requireActiveInHierarchy = true;
        public string rejectPathKeywords = "outline;shadow;canvas;ui;character;player;enemy;ally;item";
        [Tooltip("ON = draw occluder renderers with their own material, so transparent cutout/alpha shape can be captured. OFF = draw fallback solid proxy shape.")]
        public bool drawOccludersWithOriginalMaterials = true;

        [Header("Clean Occluder Input")]
        [Tooltip("Commercial-stable default. ON = force occluder masks to be written by Hidden/SkyPrison/OccluderSolidMask instead of original materials. This keeps the proven old HiddenEdge overlay but removes material/texture/detail noise from the occluder input. Existing serialized assets keep their old drawOccludersWithOriginalMaterials value, so this override is the safe migration switch.")]
        public bool forceSolidOccluderMaskInput = true;
        [Tooltip("Fallback material pass index when the effective occluder input is solid-mask mode.")]
        public int fallbackOccluderPassIndex = 0;

        [Header("Shadow / Contact Shadow Occluder Mask")]
        [Tooltip("ON = render an always-on global foreground occluder mask for stage/contact shadows. This mask is independent from unit BackTrigger authorization, so shadows are never lifted onto foreground props when the unit leaves a trigger relationship.")]
        public bool enableShadowOccluderMask = true;
        [Tooltip("Global texture name bound for shadow projectors. UnitGroundShadowFollowerTerrain should read this first.")]
        public string shadowOccluderGlobalTextureName = "_SkyPrison_GlobalShadowOccluderMask";
        [Tooltip("Compatibility global name. Also published so older shadow followers can bind it by RT-like name.")]
        public string shadowOccluderLegacyGlobalTextureName = "RT_MainCameraShadowOccluderMask_All";
        [Tooltip("Legacy fallback. ON = if no visual roots are found, shadow mask collects renderers on Occluder Source layer. It does not require a unit receiver to be currently occluded.")]
        public bool shadowMaskCollectAllOcclusionLayerRenderers = true;
        [Tooltip("ON = shadow/contact shadow mask is drawn from each terrain decoration's real VisualRoot as seen by the Main Camera, instead of from FrontOccluderProxy / ShadowOccluder proxy geometry. This makes the shadow cut pixel-align with the actual rendered object silhouette.")]
        public bool shadowMaskUseMainCameraVisualGeometry = true;
        [Tooltip("Name used when a decoration trigger has no explicit occluderContentRoot. The nearest child named this will be treated as the real visual source for the shadow mask.")]
        public string shadowMaskVisualRootName = "VisualRoot";
        [Tooltip("ON = if visual geometry collection finds nothing, fall back to the old OcclusionMask layer collection. Keep ON as a safety net while migrating prefabs.")]
        public bool shadowMaskFallbackToOcclusionLayerWhenNoVisual = true;
        [Tooltip("Extra reject keywords for visual-geometry shadow mask collection. This prevents proxy/trigger/collider/shadow/outline renderers from entering the pixel mask.")]
        public string shadowMaskVisualRejectPathKeywords = "outline;shadow;canvas;ui;character;player;enemy;ally;item;trigger;collider;collision;frontoccluder;occluder;proxy;gizmo;debug";
        [Tooltip("ON = skip renderers whose path matches Reject Path Keywords. Keep this ON to avoid characters/UI/shadows entering the shadow occluder mask.")]
        public bool shadowMaskUseRejectPathKeywords = true;
        [Tooltip("Usually ON for visual geometry mode: draw only enabled real visual renderers. For proxy fallback this can stay OFF if needed.")]
        public bool shadowMaskRequireRendererEnabled = true;
        [Tooltip("Usually ON for visual geometry mode: draw only active real visual renderers. For proxy fallback this can stay OFF if needed.")]
        public bool shadowMaskRequireActiveInHierarchy = true;

        [Header("RT")]
        public string occluderMaskRTName = "RT_MainCameraShadowOccluderMask_All";
        public bool matchCameraSize = true;
        public int fallbackWidth = 1920;
        public int fallbackHeight = 1080;
        // 之前默认 Point（最近邻，硬边界）——这些遮罩降到0.5倍分辨率之后再用最近邻采样，
        // 描边边缘锯齿会很明显，看着粗糙。换成 Bilinear（双线性）：显存/渲染开销完全
        // 不变，只是采样时多做一次插值，边缘会被自然柔化，不用牺牲分辨率也能大幅缓解锯齿。
        public FilterMode filterMode = FilterMode.Bilinear;
        [Tooltip("每个通道(Player/Enemy/Ally/Item)要建3张RT+1张全局RT，跟摄像机分辨率(4K)"
            + "同尺寸的话单是这一套系统就是十几张接近4K的RenderTexture，实测关掉这个特性能让"
            + "3FPS的场景稳定回60FPS——这些遮罩本质是二值的\"挡住/没挡住\"判断，不需要满分辨率"
            + "精度，降到0.5（面积变1/4）在视觉上几乎看不出差别，但能把这一大块显存/渲染开销"
            + "砍掉大半。")]
        [Range(0.25f, 1f)]
        public float renderTargetScale = 0.5f;

        [Header("Shaders")]
        [Tooltip("Build-safe direct reference. Assign Hidden/SkyPrison/OccluderSolidMask here so Unity includes it in Player builds. If empty, the feature falls back to Shader.Find by name.")]
        public Shader occluderMaskShader;
        public string occluderMaskShaderName = "Hidden/SkyPrison/OccluderSolidMask";
        [Tooltip("Build-safe direct reference. Assign Hidden/SkyPrison/MaskMultiplyComposite here so Unity includes the DebugMaskOverlay / HiddenEdge pass in Player builds. If empty, the feature falls back to Shader.Find by name.")]
        public Shader compositeShader;
        public string compositeShaderName = "Hidden/SkyPrison/MaskMultiplyComposite";

        [Header("Composite / Body")]
        [Range(0, 1)] public float threshold = 0.01f;
        public bool sampleBothY = false;
        public bool enableBodyClip = true;
        public bool enableHiddenOutline = true;
        [Range(1, 12)] public int outlineWidthPixels = 2;
        [Range(0, 1)] public float outlineAlpha = 1f;

        [Header("Final Outline Overlay")]
        [Tooltip("ON = draw the official outline with the same HiddenEdge path proven by Debug View. This avoids relying on the Spine body shader outline pass.")]
        public bool drawFinalOutlineWithHiddenEdgeOverlay = true;
        [Tooltip("ON = also draw the actual hidden area as a faint faction-color silhouette before drawing the edge. This restores the visible occluded part while still using unit-authorized OccludedProxy masks.")]
        public bool drawFinalHiddenMaskFillOverlay = true;
        [Range(0f, 1f)] public float finalHiddenMaskFillAlpha = 0.22f;
        [Tooltip("When the final overlay is ON, disable the Spine material's internal outline to avoid double outlines. Body clipping still remains controlled by Enable Body Clip.")]
        public bool disableSpineInternalOutlineWhenUsingFinalOverlay = true;

        [Header("Debug Overlay")]
        public DebugViewMode debugViewMode = DebugViewMode.Off;
        public DebugChannel debugChannel = DebugChannel.Player;
        [Range(0.05f, 1f)] public float debugAlpha = 0.65f;
        [Range(1, 12)] public int debugEdgeWidthPixels = 2;
        public Color debugCharacterColor = new Color(0f, 1f, 1f, 0.65f);
        public Color debugOccluderColor = new Color(1f, 0f, 1f, 0.65f);
        public Color debugHiddenColor = new Color(1f, 1f, 0f, 0.75f);
        public Color debugEdgeColor = new Color(1f, 0.25f, 0f, 0.95f);

        [Header("Debug Status - read only")]
        [TextArea(2, 5)] public string lastCameraStatus = "-";
        [TextArea(2, 5)] public string lastCharacterStatus = "-";
        [TextArea(2, 5)] public string lastOccluderStatus = "-";
        [TextArea(2, 5)] public string lastCompositeStatus = "-";
        [TextArea(2, 6)] public string lastBodyBindStatus = "-";
        [TextArea(2, 5)] public string lastDebugStatus = "-";
    }

    private sealed class ChannelRuntime
    {
        public ChannelSettings settings;
        public RenderTexture characterRT;
        public RTHandle characterHandle;
        public RenderTexture hiddenRT;
        public RTHandle hiddenHandle;
        public RenderTexture occluderRT;
        public RTHandle occluderHandle;
        // When non-null, final overlay and HiddenComposite use this instead of occluderRT.
        // Used by Item channel which has no UnitOcclusionMaterialReceiver and thus an empty per-channel occluder.
        public RenderTexture globalOccluderOverride;
        public readonly List<Renderer> occluderRenderers = new List<Renderer>(32);
        public OccluderMaskPass occluderPass;
        // Active, unit-authorized OccludedProxy renderers used to build CharacterMask.
        public readonly List<Renderer> renderers = new List<Renderer>(16);
        // Real Spine body renderers used only for material binding / body clipping.
        public readonly List<Renderer> bodyRenderers = new List<Renderer>(16);
        public CharacterMaskPass characterPass;
        public HiddenCompositePass compositePass;
        public BodyBindPass bodyBindPass;

        // 跳过没内容的Pass是为了省渲染开销，但这几个Pass执行时都会顺带清空目标RT——
        // 直接不执行的话，"这一帧突然没人了"那一帧不会被清空，上一帧残留的遮罩画面
        // 会一直挂在贴图里（鬼影）。用"上一帧也是空的"才真正跳过，保证"从有变没有"
        // 的那一帧一定会正常跑一次、把RT清干净，下一帧起才真正省下这份开销。
        public int lastRendererCount = -1;
        public int lastOccluderRendererCount = -1;
    }

    public Settings settings = new Settings();

    private Material occluderMaskMaterial;
    private Material compositeMaterial;
    private readonly Material[] finalOverlayCompositeMaterials = new Material[4];
    private readonly bool[] _rendererEmptyThisFrameBuffer = new bool[4]; // 复用缓冲区，避免每帧 new
    private RenderTexture occluderRT;
    private RTHandle occluderHandle;
    private OccluderMaskPass occluderPass;
    private FinalOutlineOverlayPass finalOutlineOverlayPass;
    private DebugOverlayPass debugOverlayPass;
    private readonly ChannelRuntime[] channels = new ChannelRuntime[4];
    private readonly List<Renderer> occluderRenderers = new List<Renderer>(64);
    private readonly HashSet<int> configuredRenderers = new HashSet<int>();

    private static readonly int OcclusionTexId = Shader.PropertyToID("_OcclusionTex");
    private static readonly int CharacterMaskTexBodyId = Shader.PropertyToID("_SkyPrison_CharacterMaskTex");
    private static readonly int OccluderMaskTexBodyId = Shader.PropertyToID("_SkyPrison_OccluderMaskTex");
    private static readonly int HiddenOutlineColorBodyId = Shader.PropertyToID("_SkyPrison_HiddenOutlineColor");
    private static readonly int EnableHiddenOutlineBodyId = Shader.PropertyToID("_SkyPrison_EnableHiddenOutline");
    private static readonly int EnableBodyClipId = Shader.PropertyToID("_SkyPrison_EnableBodyClip");
    private static readonly int HiddenOutlineWidthBodyId = Shader.PropertyToID("_SkyPrison_HiddenOutlineWidthPixels");
    private static readonly int HiddenOutlineAlphaBodyId = Shader.PropertyToID("_SkyPrison_HiddenOutlineAlpha");
    private static readonly int DebugBodyMaskModeId = Shader.PropertyToID("_SkyPrison_DebugBodyMaskMode");
    private static readonly int FlipMaskYId = Shader.PropertyToID("_FlipMaskY");
    private static readonly int SampleBothYId = Shader.PropertyToID("_SampleBothY");
    private static readonly int MaskColorId = Shader.PropertyToID("_MaskColor");
    private static readonly int CharacterMaskTexId = Shader.PropertyToID("_CharacterMaskTex");
    private static readonly int OccluderMaskTexId = Shader.PropertyToID("_OccluderMaskTex");
    private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
    private static readonly int DebugViewModeId = Shader.PropertyToID("_DebugViewMode");
    private static readonly int DebugTintId = Shader.PropertyToID("_DebugTint");
    private static readonly int DebugAlphaId = Shader.PropertyToID("_DebugAlpha");
    private static readonly int DebugEdgeWidthPixelsId = Shader.PropertyToID("_DebugEdgeWidthPixels");
    private static readonly int DebugTexelSizeId = Shader.PropertyToID("_DebugTexelSize");
    private static readonly int GlobalShadowOccluderMaskId = Shader.PropertyToID("_SkyPrison_GlobalShadowOccluderMask");
    private static readonly int LegacyShadowOccluderMaskId = Shader.PropertyToID("RT_MainCameraShadowOccluderMask_All");

    // CollectSpineRenderersAndConfigureMaterials / CollectShadowOccluderRenderersForStageShadowMask /
    // CollectUnitAuthorizedOccluderRenderersForChannels 各自都调用 Resources.FindObjectsOfTypeAll<Renderer>()
    // ——这是 Unity 里最重的 API 之一，扫描所有已加载场景的全部 Renderer。这三个函数之前是
    // AddRenderPasses 的一部分，等于每帧、每个摄像机跑三次全场景扫描，是长时间卡顿排查中
    // 定位到的最大单一CPU开销来源。改成限频重扫，新增/消失单位最多延迟这么久才被感知到，
    // 肉眼完全不会察觉，但去掉了逐帧扫描的开销。
    private const float UnitRescanInterval = 0.5f;
    private float _nextUnitRescanTime;

    public override void Create()
    {
        EnsureMaterials();
        BuildChannels();
        // V19: occluder masks are per channel / per active receiver authorization for body clip and hidden outline.
        // V22: a separate global shadow occluder pass is used only by stage/contact shadows. It is never gated by unit authorization.
        occluderPass = new OccluderMaskPass(settings, () => occluderMaskMaterial, () => occluderHandle, () => occluderRenderers.ToArray());
        finalOutlineOverlayPass = new FinalOutlineOverlayPass(settings, channels, () => compositeMaterial);
        debugOverlayPass = new DebugOverlayPass(settings, channels, () => compositeMaterial, () => finalOverlayCompositeMaterials);
        ApplyPassEvents();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (!ShouldRunForCamera(camera, renderingData.cameraData.cameraType))
        {
            settings.lastCameraStatus = "SKIP camera=" + SafeName(camera) + ", type=" + renderingData.cameraData.cameraType;
            return;
        }

        EnsureMaterials();
        BuildChannels();
        EnsureRenderTargets(camera);
        ApplyItemChannelGlobalOccluderOverride();
        ApplyPassEvents();
        if (Time.realtimeSinceStartup >= _nextUnitRescanTime)
        {
            _nextUnitRescanTime = Time.realtimeSinceStartup + UnitRescanInterval;
            CollectSpineRenderersAndConfigureMaterials();
            CollectShadowOccluderRenderersForStageShadowMask();
            CollectUnitAuthorizedOccluderRenderersForChannels();
        }
        PublishShadowOccluderGlobalTexture();

        if (compositeMaterial == null || ((settings.forceSolidOccluderMaskInput || !settings.drawOccludersWithOriginalMaterials) && occluderMaskMaterial == null))
        {
            settings.lastCameraStatus = "SKIP missing material. composite=" + SafeName(compositeMaterial) + ", occluder=" + SafeName(occluderMaskMaterial);
            return;
        }
        if (occluderMaskMaterial != null)
            occluderMaskMaterial.SetColor(MaskColorId, Color.white);

        if (settings.enableShadowOccluderMask && occluderPass != null && occluderHandle != null)
            renderer.EnqueuePass(occluderPass);

        // 之前这三个通道（角色遮罩/遮挡物遮罩/合成）不管这个通道当下有没有真正的
        // 单位/遮挡物，每帧都会整套跑一遍——跟 bodyBindPass 早就有的"没东西就跳过"
        // 判断（ch.bodyRenderers.Count == 0）比，这三个反而漏了同样的判断。比如
        // 场上没有敌人/没有友军/附近没有道具掉落时，对应通道这几个更重的Pass其实
        // 完全没有东西可画，直接跳过不会有任何视觉差异，纯粹省掉这一份渲染开销。
        //
        // 注意：这三个循环里 characterPass 跟 compositePass 都要看"角色渲染列表是否
        // 为空"这同一份状态——必须先把所有通道这一帧的"是否为空"判断完、再统一
        // 写回 lastRendererCount，不能在 characterPass 那个循环里就提前把
        // lastRendererCount 更新掉，不然 compositePass 循环读到的就已经是"这一帧"
        // 的计数而不是"上一帧"的，判断会失效。
        bool[] rendererEmptyThisFrame = _rendererEmptyThisFrameBuffer;
        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null) continue;
            rendererEmptyThisFrame[i] = ch.renderers.Count == 0 && ch.lastRendererCount == 0;
        }

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null || ch.settings == null || !ch.settings.enabled || ch.characterHandle == null)
                continue;
            if (rendererEmptyThisFrame[i]) continue;
            renderer.EnqueuePass(ch.characterPass);
        }

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null || ch.settings == null || !ch.settings.enabled || ch.occluderHandle == null)
                continue;
            bool skip = ch.occluderRenderers.Count == 0 && ch.lastOccluderRendererCount == 0;
            ch.lastOccluderRendererCount = ch.occluderRenderers.Count;
            if (skip) continue;
            renderer.EnqueuePass(ch.occluderPass);
        }

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null || ch.settings == null || !ch.settings.enabled || ch.characterHandle == null || ch.hiddenHandle == null)
                continue;
            if (rendererEmptyThisFrame[i]) continue;
            renderer.EnqueuePass(ch.compositePass);
        }

        // 两个循环都读完这一帧的空/非空状态后，才统一写回，供下一帧使用。
        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null) continue;
            ch.lastRendererCount = ch.renderers.Count;
        }

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null || ch.settings == null || !ch.settings.enabled || ch.hiddenRT == null || ch.bodyRenderers.Count == 0)
                continue;
            renderer.EnqueuePass(ch.bodyBindPass);
        }

        // V8: final formal outline and debug HiddenEdge use the same overlay pass. CharacterMask is unit-authorized via active OccludedProxy renderers.
        bool needsOverlayPass = settings.debugViewMode != DebugViewMode.Off ||
                                (settings.drawFinalOutlineWithHiddenEdgeOverlay && settings.enableHiddenOutline);
        if (needsOverlayPass)
            renderer.EnqueuePass(debugOverlayPass);

        settings.lastCameraStatus = "OK queued V22 clean-occluder old HiddenEdge overlay + shadow occluder mask. camera=" + SafeName(camera) + ", unifiedEdgeOverlay=" + needsOverlayPass;
    }

    protected override void Dispose(bool disposing)
    {
        for (int i = 0; i < channels.Length; i++)
        {
            if (channels[i] == null) continue;
            ReleaseRTHandle(ref channels[i].characterHandle);
            ReleaseRT(ref channels[i].characterRT);
            ReleaseRTHandle(ref channels[i].hiddenHandle);
            ReleaseRT(ref channels[i].hiddenRT);
            ReleaseRTHandle(ref channels[i].occluderHandle);
            ReleaseRT(ref channels[i].occluderRT);
        }
        ReleaseRTHandle(ref occluderHandle);
        ReleaseRT(ref occluderRT);
        DestroyMaterial(ref occluderMaskMaterial);
        DestroyMaterial(ref compositeMaterial);
        for (int i = 0; i < finalOverlayCompositeMaterials.Length; i++)
            DestroyMaterial(ref finalOverlayCompositeMaterials[i]);
        configuredRenderers.Clear();
    }

    private void BuildChannels()
    {
        ChannelSettings[] source = { settings.player, settings.enemy, settings.item, settings.ally };
        for (int i = 0; i < channels.Length; i++)
        {
            if (channels[i] == null)
                channels[i] = new ChannelRuntime();

            channels[i].settings = source[i];
            ChannelRuntime runtime = channels[i];
            if (runtime.characterPass == null)
                runtime.characterPass = new CharacterMaskPass(settings, runtime);
            if (runtime.occluderPass == null)
                runtime.occluderPass = new OccluderMaskPass(settings, () => occluderMaskMaterial, () => runtime.occluderHandle, () => runtime.occluderRenderers.ToArray());
            if (runtime.compositePass == null)
                runtime.compositePass = new HiddenCompositePass(settings, runtime, () => compositeMaterial,
                    () => runtime.globalOccluderOverride != null ? occluderHandle : runtime.occluderHandle,
                    () => runtime.globalOccluderOverride ?? runtime.occluderRT);
            if (runtime.bodyBindPass == null)
                runtime.bodyBindPass = new BodyBindPass(settings, runtime, () => runtime.occluderRT);
        }
    }

    private void ApplyItemChannelGlobalOccluderOverride()
    {
        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null || ch.settings == null) continue;
            if (string.Equals(ch.settings.channelName, "Item", System.StringComparison.OrdinalIgnoreCase))
                ch.globalOccluderOverride = occluderRT; // global ShadowOccluderMask has all 3D containers
            else
                ch.globalOccluderOverride = null;
        }
    }

    private void ApplyPassEvents()
    {
        if (occluderPass != null) occluderPass.renderPassEvent = settings.occluderMaskEvent;
        if (finalOutlineOverlayPass != null) finalOutlineOverlayPass.renderPassEvent = settings.finalOutlineEvent;
        if (debugOverlayPass != null) debugOverlayPass.renderPassEvent = settings.debugOverlayEvent;
        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null) continue;
            if (ch.characterPass != null) ch.characterPass.renderPassEvent = settings.characterMaskEvent;
            if (ch.compositePass != null) ch.compositePass.renderPassEvent = settings.hiddenCompositeEvent;
            if (ch.bodyBindPass != null) ch.bodyBindPass.renderPassEvent = settings.bodyBindEvent;
        }
    }

    private bool ShouldRunForCamera(Camera camera, CameraType type)
    {
        if (camera == null || type != CameraType.Game)
            return false;

        // HUD module post-process cameras must never run main-camera occlusion passes.
        // Do not reject every targetTexture camera here: some builds/pipelines can legitimately
        // render the main camera to an RT.  Only skip cameras explicitly tagged/named as HUD internals.
        if (IsHUDInternalRenderCamera(camera))
            return false;

        if (!settings.onlyMainCamera)
            return true;

        // This project's actual gameplay camera is not the object tagged MainCamera (Camera.main
        // resolves to an unrelated narrow-culling-mask base camera in the stack; GamePlayCamera
        // is the overlay that actually renders world content). Name-match against
        // Required Camera Name is authoritative - do not additionally require camera == Camera.main.
        if (!string.IsNullOrEmpty(settings.requiredCameraName))
            return camera.name == settings.requiredCameraName;

        return true;
    }

    private static bool IsHUDInternalRenderCamera(Camera camera)
    {
        if (camera == null)
            return false;

        if (camera.GetComponent<SkyPrison.Runtime.UI.SkyPrisonHUDInternalRenderCameraTag>() != null)
            return true;

        string cameraName = camera.name;
        if (!string.IsNullOrEmpty(cameraName) && cameraName.StartsWith("__SkyPrisonHUDModuleRTCamera"))
            return true;

        RenderTexture rt = camera.targetTexture;
        if (rt != null && !string.IsNullOrEmpty(rt.name) && rt.name.StartsWith("RT_HUDModule_"))
            return true;

        return false;
    }

    private void EnsureMaterials()
    {
        EnsureMaterial(ref occluderMaskMaterial, settings.occluderMaskShader, settings.occluderMaskShaderName);
        EnsureMaterial(ref compositeMaterial, settings.compositeShader, settings.compositeShaderName);
        EnsureFinalOverlayMaterials();
    }

    private void EnsureFinalOverlayMaterials()
    {
        Shader shader = settings.compositeShader != null ? settings.compositeShader : Shader.Find(settings.compositeShaderName);
        if (shader == null)
            return;

        for (int i = 0; i < finalOverlayCompositeMaterials.Length; i++)
        {
            if (finalOverlayCompositeMaterials[i] != null)
                continue;

            finalOverlayCompositeMaterials[i] = CoreUtils.CreateEngineMaterial(shader);
            finalOverlayCompositeMaterials[i].name = "SkyPrison_FinalHiddenEdgeOverlay_Channel" + i;
        }
    }

    private static void EnsureMaterial(ref Material mat, Shader shaderRef, string shaderName)
    {
        if (mat != null)
            return;
        Shader shader = shaderRef != null ? shaderRef : Shader.Find(shaderName);
        if (shader != null)
            mat = CoreUtils.CreateEngineMaterial(shader);
    }

    private void EnsureRenderTargets(Camera camera)
    {
        int w = settings.fallbackWidth;
        int h = settings.fallbackHeight;
        if (settings.matchCameraSize && camera != null)
        {
            w = Mathf.Max(1, camera.pixelWidth);
            h = Mathf.Max(1, camera.pixelHeight);
        }

        float scale = Mathf.Clamp(settings.renderTargetScale, 0.25f, 1f);
        w = Mathf.Max(1, Mathf.RoundToInt(w * scale));
        h = Mathf.Max(1, Mathf.RoundToInt(h * scale));

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null || ch.settings == null)
                continue;
            EnsureRT(ref ch.characterRT, ref ch.characterHandle, ch.settings.characterMaskRTName, w, h, settings.filterMode);
            EnsureRT(ref ch.occluderRT, ref ch.occluderHandle, "RT_MainCameraOccluderMask_" + ch.settings.channelName, w, h, settings.filterMode);
            EnsureRT(ref ch.hiddenRT, ref ch.hiddenHandle, ch.settings.hiddenMaskRTName, w, h, settings.filterMode);
        }
        EnsureRT(ref occluderRT, ref occluderHandle, settings.occluderMaskRTName, w, h, settings.filterMode);
        PublishShadowOccluderGlobalTexture();
    }

    private void PublishShadowOccluderGlobalTexture()
    {
        if (occluderRT == null)
            return;

        Shader.SetGlobalTexture(GlobalShadowOccluderMaskId, occluderRT);
        Shader.SetGlobalTexture(LegacyShadowOccluderMaskId, occluderRT);

        if (!string.IsNullOrWhiteSpace(settings.shadowOccluderGlobalTextureName))
            Shader.SetGlobalTexture(settings.shadowOccluderGlobalTextureName, occluderRT);
        if (!string.IsNullOrWhiteSpace(settings.shadowOccluderLegacyGlobalTextureName))
            Shader.SetGlobalTexture(settings.shadowOccluderLegacyGlobalTextureName, occluderRT);
    }

    private static void EnsureRT(ref RenderTexture rt, ref RTHandle handle, string rtName, int w, int h, FilterMode filterMode)
    {
        if (rt != null && rt.width == w && rt.height == h)
            return;
        ReleaseRTHandle(ref handle);
        ReleaseRT(ref rt);
        rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
        {
            name = rtName,
            filterMode = filterMode,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false,
            depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None
        };
        rt.Create();
        handle = RTHandles.Alloc(rt);
    }

    private void CollectSpineRenderersAndConfigureMaterials()
    {
        for (int i = 0; i < channels.Length; i++)
        {
            if (channels[i] == null)
                continue;
            channels[i].renderers.Clear();
            channels[i].bodyRenderers.Clear();
        }

        int checkedRenderers = 0;
        int matchedBodyRenderers = 0;
        int matchedAuthorizedProxyRenderers = 0;
        int matchedAuthorizedProxyRenderersFromGates = 0;
        int matchedAuthorizedBodyRenderersFromGates = 0;
        int configuredSlots = 0;

        Renderer[] all = Resources.FindObjectsOfTypeAll<Renderer>();
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || r.gameObject == null || !r.gameObject.activeInHierarchy)
                continue;
            checkedRenderers++;

            string path = GetPath(r.transform);
            bool isAuthorizedOccludedProxy = IsAuthorizedOccludedProxyPath(path);

            // V11: Authorized OccludedProxy renderers are the unit-level CharacterMask source.
            // Do NOT require them to use the Spine body shader, because proxy materials may be
            // mask/outline/silhouette materials. The path + active renderer state is the authority.
            if (isAuthorizedOccludedProxy)
            {
                ChannelRuntime proxyRuntime = ResolveRuntimeForOccludedProxyPath(path);
                if (proxyRuntime != null && proxyRuntime.settings != null && proxyRuntime.settings.enabled)
                {
                    if (!proxyRuntime.renderers.Contains(r))
                        proxyRuntime.renderers.Add(r);
                    matchedAuthorizedProxyRenderers++;
                }

                // Do not bind body material state against proxy-only renderers.
                // The real Spine body is bound below through bodyRenderers.
                continue;
            }

            Material[] shared = r.sharedMaterials;
            bool hasTargetShader = false;
            for (int m = 0; m < shared.Length; m++)
            {
                Material sm = shared[m];
                if (sm != null && sm.shader != null && sm.shader.name == settings.spineOcclusionShaderName)
                {
                    hasTargetShader = true;
                    break;
                }
            }
            if (!hasTargetShader)
                continue;

            ChannelRuntime bodyRuntime = ResolveRuntimeForPath(path);
            if (bodyRuntime == null || bodyRuntime.settings == null || !bodyRuntime.settings.enabled)
                continue;

            if (!bodyRuntime.bodyRenderers.Contains(r))
                bodyRuntime.bodyRenderers.Add(r);
            matchedBodyRenderers++;

            if (Application.isPlaying && settings.autoConfigureMaterialsInPlayMode)
            {
                int id = r.GetInstanceID();
                bool skip = settings.configureOnlyOncePerRenderer && configuredRenderers.Contains(id);
                if (!skip)
                {
                    Material[] mats = r.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        Material mat = mats[m];
                        if (mat == null || mat.shader == null || mat.shader.name != settings.spineOcclusionShaderName)
                            continue;

                        mat.SetTexture(OcclusionTexId, bodyRuntime.hiddenRT);
                        mat.SetTexture(CharacterMaskTexBodyId, bodyRuntime.characterRT);
                        mat.SetTexture(OccluderMaskTexBodyId, bodyRuntime.occluderRT);
                        mat.SetColor(HiddenOutlineColorBodyId, bodyRuntime.settings.outlineColor);
                        mat.SetFloat(EnableBodyClipId, settings.enableBodyClip ? 1f : 0f);
                        mat.SetFloat(EnableHiddenOutlineBodyId, (settings.enableHiddenOutline && !(settings.drawFinalOutlineWithHiddenEdgeOverlay && settings.disableSpineInternalOutlineWhenUsingFinalOverlay)) ? 1f : 0f);
                        mat.SetFloat(HiddenOutlineWidthBodyId, Mathf.Max(1f, settings.outlineWidthPixels));
                        mat.SetFloat(HiddenOutlineAlphaBodyId, Mathf.Clamp01(settings.outlineAlpha * bodyRuntime.settings.outlineColor.a));
                        mat.SetFloat(DebugBodyMaskModeId, 0f);
                        if (settings.forceMaskYSettings)
                        {
                            mat.SetFloat(FlipMaskYId, 0f);
                            mat.SetFloat(SampleBothYId, settings.sampleBothY ? 1f : 0f);
                        }
                        configuredSlots++;
                    }
                    configuredRenderers.Add(id);
                }
            }
        }

        if (settings.collectAuthorizedProxyFromGateState)
            matchedAuthorizedProxyRenderersFromGates = CollectAuthorizedOccludedProxyRenderersFromGates(out matchedAuthorizedBodyRenderersFromGates);

        int matchedAuthorizedProxyRenderersFromActiveProxies = CollectActiveOccludedProxyRenderersFallback();

        settings.lastCharacterStatus = "COLLECT V14 authorized OccludedProxy masks. checked=" + checkedRenderers +
                                       ", pathAuthorizedProxyRenderers=" + matchedAuthorizedProxyRenderers +
                                       ", gateAuthorizedProxyRenderers=" + matchedAuthorizedProxyRenderersFromGates +
                                       ", gateAuthorizedBodyRenderers=" + matchedAuthorizedBodyRenderersFromGates +
                                       ", activeProxyFallbackRenderers=" + matchedAuthorizedProxyRenderersFromActiveProxies +
                                       ", bodyRenderers=" + matchedBodyRenderers +
                                       ", configuredSlots=" + configuredSlots;
    }

    private int CollectAuthorizedOccludedProxyRenderersFromGates(out int addedBodyRenderers)
    {
        int added = 0;
        addedBodyRenderers = 0;

        // V17: Build-safe path. Do not read private fields by reflection here.
        // In Player/IL2CPP builds private metadata can be stripped or unavailable,
        // which made Editor Play work while Build failed to collect the unit side mask.
#if UNITY_2023_1_OR_NEWER
        UnitOccludedOutlineProxyGate[] gates = UnityEngine.Object.FindObjectsByType<UnitOccludedOutlineProxyGate>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        UnitOccludedOutlineProxyGate[] gates = UnityEngine.Object.FindObjectsOfType<UnitOccludedOutlineProxyGate>(true);
#endif
        if (gates == null || gates.Length == 0)
            return 0;

        for (int i = 0; i < gates.Length; i++)
        {
            UnitOccludedOutlineProxyGate gate = gates[i];
            if (gate == null || gate.gameObject == null)
                continue;

            if (!settings.drawInactiveAuthorizedProxyRenderers && !gate.gameObject.activeInHierarchy)
                continue;

            if (!gate.CurrentOccluded && !gate.FinalOccludedVisible)
                continue;

            string campName = gate.ActiveCamp.ToString();
            ChannelRuntime runtime = ResolveRuntimeForCampName(campName);
            if (runtime == null || runtime.settings == null || !runtime.settings.enabled)
                continue;

            GameObject proxyRoot = gate.GetOccludedProxyForActiveCamp();
            if (proxyRoot == null)
                proxyRoot = ResolveOccludedProxyRootByNameFallback(gate.transform, campName);

            if (proxyRoot != null)
            {
                Renderer[] renderers = proxyRoot.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer renderer = renderers[r];
                    if (renderer == null)
                        continue;

                    if (!settings.drawInactiveAuthorizedProxyRenderers && renderer.gameObject != null && !renderer.gameObject.activeInHierarchy)
                        continue;

                    if (!runtime.renderers.Contains(renderer))
                    {
                        runtime.renderers.Add(renderer);
                        added++;
                    }
                }
            }

            if (settings.alsoDrawAuthorizedSpineBodyFromGate)
                addedBodyRenderers += CollectAuthorizedSpineBodyRenderersFromGate(gate, runtime);
        }

        return added;
    }

    private GameObject ResolveOccludedProxyRootByNameFallback(Transform unitRoot, string campName)
    {
        if (unitRoot == null || string.IsNullOrWhiteSpace(campName))
            return null;

        string wanted = "OutlineProxy_" + campName + "_Occluded";
        Transform[] all = unitRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t != null && string.Equals(t.name, wanted, StringComparison.OrdinalIgnoreCase))
                return t.gameObject;
        }

        return null;
    }

    private int CollectAuthorizedSpineBodyRenderersFromGate(UnitOccludedOutlineProxyGate gate, ChannelRuntime runtime)
    {
        if (gate == null || runtime == null)
            return 0;

        int added = 0;
        Renderer[] renderers = gate.GetComponentsInChildren<Renderer>(true);
        if (renderers == null)
            return 0;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.gameObject == null)
                continue;

            string path = GetPath(renderer.transform);
            if (IsAuthorizedOccludedProxyPath(path))
                continue;
            if (path.IndexOf("OutlineProxyRoot", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (ContainsAny(path, new[] { "Shadow", "Canvas", "UI", "Overhead", "Sight", "Collider", "Trigger", "Anchor", "HitRoot", "EffectRoot", "WeaponRoot" }))
                continue;

            Material[] mats = renderer.sharedMaterials;
            bool hasSpineMaskShader = false;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat != null && mat.shader != null && mat.shader.name == settings.spineOcclusionShaderName)
                {
                    hasSpineMaskShader = true;
                    break;
                }
            }

            if (!hasSpineMaskShader)
                continue;

            if (!settings.drawInactiveAuthorizedProxyRenderers && !renderer.gameObject.activeInHierarchy)
                continue;

            if (!runtime.renderers.Contains(renderer))
            {
                runtime.renderers.Add(renderer);
                added++;
            }
        }

        return added;
    }

    private GameObject ResolveOccludedProxyRootFromGate(UnitOccludedOutlineProxyGate gate, string campName, BindingFlags flags)
    {
        if (gate == null)
            return null;

        string fieldName = GetOccludedProxyFieldName(campName);
        if (!string.IsNullOrEmpty(fieldName))
        {
            FieldInfo proxyField = typeof(UnitOccludedOutlineProxyGate).GetField(fieldName, flags);
            if (proxyField != null)
            {
                GameObject fromField = proxyField.GetValue(gate) as GameObject;
                if (fromField != null)
                    return fromField;
            }
        }

        string exactName = GetOccludedProxyExactName(campName);
        if (string.IsNullOrEmpty(exactName))
            return null;

        Transform[] children = gate.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform t = children[i];
            if (t != null && t.name == exactName)
                return t.gameObject;
        }

        return null;
    }

    private int CollectActiveOccludedProxyRenderersFallback()
    {
        // V14 fallback: if Gate reflection is not populated but the proxy GameObject itself is active,
        // treat active *_Occluded proxies as authorized. This keeps the unit-level rule without falling
        // back to all Spine bodies.
        int added = 0;
        Renderer[] all = Resources.FindObjectsOfTypeAll<Renderer>();
        if (all == null)
            return 0;

        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || r.gameObject == null)
                continue;

            string path = GetPath(r.transform);
            if (!IsAuthorizedOccludedProxyPath(path))
                continue;

            if (!r.gameObject.activeInHierarchy)
                continue;

            ChannelRuntime runtime = ResolveRuntimeForOccludedProxyPath(path);
            if (runtime == null || runtime.settings == null || !runtime.settings.enabled)
                continue;

            if (!runtime.renderers.Contains(r))
            {
                runtime.renderers.Add(r);
                added++;
            }
        }

        return added;
    }

    private ChannelRuntime ResolveRuntimeForCampName(string campName)
    {
        if (string.IsNullOrWhiteSpace(campName))
            return null;

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime runtime = channels[i];
            if (runtime == null || runtime.settings == null || !runtime.settings.enabled)
                continue;

            if (string.Equals(runtime.settings.channelName, campName, StringComparison.OrdinalIgnoreCase))
                return runtime;
        }

        return null;
    }

    private static string GetOccludedProxyFieldName(string campName)
    {
        if (string.Equals(campName, "Player", StringComparison.OrdinalIgnoreCase))
            return "outlineProxyPlayerOccluded";
        if (string.Equals(campName, "Enemy", StringComparison.OrdinalIgnoreCase))
            return "outlineProxyEnemyOccluded";
        if (string.Equals(campName, "Ally", StringComparison.OrdinalIgnoreCase))
            return "outlineProxyAllyOccluded";
        if (string.Equals(campName, "Item", StringComparison.OrdinalIgnoreCase))
            return "outlineProxyItemOccluded";
        return null;
    }

    private static string GetOccludedProxyExactName(string campName)
    {
        if (string.IsNullOrWhiteSpace(campName))
            return "";

        if (string.Equals(campName, "Player", StringComparison.OrdinalIgnoreCase))
            return "OutlineProxy_Player_Occluded";
        if (string.Equals(campName, "Enemy", StringComparison.OrdinalIgnoreCase))
            return "OutlineProxy_Enemy_Occluded";
        if (string.Equals(campName, "Ally", StringComparison.OrdinalIgnoreCase))
            return "OutlineProxy_Ally_Occluded";
        if (string.Equals(campName, "Item", StringComparison.OrdinalIgnoreCase))
            return "OutlineProxy_Item_Occluded";

        return "";
    }

    private bool IsAuthorizedOccludedProxyPath(string path)
    {
        if (!settings.characterMaskFromAuthorizedOccludedProxyOnly)
            return false;
        if (string.IsNullOrEmpty(path))
            return false;

        bool hasRoot = string.IsNullOrWhiteSpace(settings.occludedProxyRootKeyword) ||
                       path.IndexOf(settings.occludedProxyRootKeyword, StringComparison.OrdinalIgnoreCase) >= 0;
        bool hasOccluded = string.IsNullOrWhiteSpace(settings.occludedProxyKeyword) ||
                           path.IndexOf(settings.occludedProxyKeyword, StringComparison.OrdinalIgnoreCase) >= 0;
        bool hasOutlineProxy = path.IndexOf("OutlineProxy_", StringComparison.OrdinalIgnoreCase) >= 0;
        return hasRoot && hasOccluded && hasOutlineProxy;
    }

    private ChannelRuntime ResolveRuntimeForOccludedProxyPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime runtime = channels[i];
            if (runtime == null || runtime.settings == null || !runtime.settings.enabled)
                continue;

            string exact = "OutlineProxy_" + runtime.settings.channelName + "_Occluded";
            if (path.IndexOf(exact, StringComparison.OrdinalIgnoreCase) >= 0)
                return runtime;
        }

        // Fallback for custom names: use existing faction keywords, but only after the path was confirmed as an OccludedProxy.
        return ResolveRuntimeForPath(path);
    }

    private ChannelRuntime ResolveRuntimeForPath(string path)
    {
        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime runtime = channels[i];
            if (runtime == null || runtime.settings == null || !runtime.settings.enabled)
                continue;
            if (ContainsAny(path, SplitKeywords(runtime.settings.pathKeywords)))
                return runtime;
        }
        return null;
    }

    private static string DebugChannelToString(DebugChannel debugChannel)
    {
        switch (debugChannel)
        {
            case DebugChannel.Player: return "Player";
            case DebugChannel.Enemy: return "Enemy";
            case DebugChannel.Item: return "Item";
            case DebugChannel.Ally: return "Ally";
            default: return "Player";
        }
    }

    private ChannelRuntime GetDebugRuntime()
    {
        int index = Mathf.Clamp((int)settings.debugChannel, 0, channels.Length - 1);
        return channels[index];
    }

    private void CollectShadowOccluderRenderersForStageShadowMask()
    {
        occluderRenderers.Clear();

        if (!settings.enableShadowOccluderMask)
            return;

        int visualRoots = 0;
        int visualRenderers = 0;
        int fallbackRenderers = 0;

        if (settings.shadowMaskUseMainCameraVisualGeometry)
            visualRenderers = CollectShadowOccluderRenderersFromDecorationVisualRoots(out visualRoots);

        if (visualRenderers <= 0 && settings.shadowMaskFallbackToOcclusionLayerWhenNoVisual && settings.shadowMaskCollectAllOcclusionLayerRenderers)
            fallbackRenderers = CollectShadowOccluderRenderersFromOcclusionLayerFallback();

        if (settings.debugViewMode != DebugViewMode.Off)
        {
            settings.lastOccluderStatus = "COLLECT V23 shadow occluders: visualRoots=" + visualRoots +
                                          ", visualRenderers=" + visualRenderers +
                                          ", fallbackRenderers=" + fallbackRenderers +
                                          ", source=" + (visualRenderers > 0 ? "MainCameraVisualGeometry" : "OcclusionLayerFallback") +
                                          ", rt=" + settings.occluderMaskRTName +
                                          ", global=" + settings.shadowOccluderGlobalTextureName;
        }
    }

    private int CollectShadowOccluderRenderersFromDecorationVisualRoots(out int visualRootCount)
    {
        visualRootCount = 0;
        int added = 0;

        string[] rejects = SplitKeywords(settings.shadowMaskVisualRejectPathKeywords + ";" + settings.rejectPathKeywords);

#if UNITY_2023_1_OR_NEWER
        SkyPrisonTerrainDecorationFrontOccluderTrigger[] triggers = UnityEngine.Object.FindObjectsByType<SkyPrisonTerrainDecorationFrontOccluderTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        SkyPrisonTerrainDecorationFrontOccluderTrigger[] triggers = Resources.FindObjectsOfTypeAll<SkyPrisonTerrainDecorationFrontOccluderTrigger>();
#endif
        if (triggers == null || triggers.Length == 0)
            return 0;

        HashSet<int> visitedRoots = new HashSet<int>();

        for (int i = 0; i < triggers.Length; i++)
        {
            SkyPrisonTerrainDecorationFrontOccluderTrigger trigger = triggers[i];
            if (trigger == null || trigger.gameObject == null)
                continue;

#if UNITY_EDITOR
            if (!Application.isPlaying && EditorUtility.IsPersistent(trigger.gameObject))
                continue;
#endif
            Transform visualRoot = trigger.occluderContentRoot;
            if (visualRoot == null && !string.IsNullOrWhiteSpace(settings.shadowMaskVisualRootName))
                visualRoot = FindNearestNamedChildOrSelf(trigger.transform, settings.shadowMaskVisualRootName);
            if (visualRoot == null)
                continue;

            int rootId = visualRoot.GetInstanceID();
            if (!visitedRoots.Add(rootId))
                continue;

            visualRootCount++;

            Renderer[] rs = visualRoot.GetComponentsInChildren<Renderer>(true);
            if (rs == null)
                continue;

            for (int rIndex = 0; rIndex < rs.Length; rIndex++)
            {
                Renderer renderer = rs[rIndex];
                if (renderer == null || renderer.gameObject == null)
                    continue;

                if (settings.shadowMaskRequireRendererEnabled && !renderer.enabled)
                    continue;
                if (settings.shadowMaskRequireActiveInHierarchy && !renderer.gameObject.activeInHierarchy)
                    continue;

                string path = GetPath(renderer.transform);
                if (ContainsAny(path, rejects))
                    continue;

                if (!occluderRenderers.Contains(renderer))
                {
                    occluderRenderers.Add(renderer);
                    added++;
                }
            }
        }

        return added;
    }

    private int CollectShadowOccluderRenderersFromOcclusionLayerFallback()
    {
        int layer = LayerMask.NameToLayer(settings.occluderLayerName);
        string[] rejects = SplitKeywords(settings.rejectPathKeywords);
        int checkedRenderers = 0;
        int addedRenderers = 0;

#if UNITY_2023_1_OR_NEWER
        Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        Renderer[] renderers = Resources.FindObjectsOfTypeAll<Renderer>();
#endif
        if (renderers == null)
            return 0;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.gameObject == null)
                continue;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (EditorUtility.IsPersistent(renderer.gameObject))
                    continue;
            }
#endif
            checkedRenderers++;

            if (layer >= 0 && renderer.gameObject.layer != layer)
                continue;
            if (settings.shadowMaskRequireRendererEnabled && !renderer.enabled)
                continue;
            if (settings.shadowMaskRequireActiveInHierarchy && !renderer.gameObject.activeInHierarchy)
                continue;

            string path = GetPath(renderer.transform);
            if (settings.shadowMaskUseRejectPathKeywords && ContainsAny(path, rejects))
                continue;

            if (!occluderRenderers.Contains(renderer))
            {
                occluderRenderers.Add(renderer);
                addedRenderers++;
            }
        }

        return addedRenderers;
    }

    private void CollectUnitAuthorizedOccluderRenderersForChannels()
    {
        for (int i = 0; i < channels.Length; i++)
        {
            if (channels[i] != null)
                channels[i].occluderRenderers.Clear();
        }

        int checkedReceivers = 0;
        int activeReceivers = 0;
        int authorizedRoots = 0;
        int addedRenderers = 0;
        int layer = LayerMask.NameToLayer(settings.occluderLayerName);
        string[] rejects = SplitKeywords(settings.rejectPathKeywords);

#if UNITY_2023_1_OR_NEWER
        UnitOcclusionMaterialReceiver[] receivers = UnityEngine.Object.FindObjectsByType<UnitOcclusionMaterialReceiver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        UnitOcclusionMaterialReceiver[] receivers = UnityEngine.Object.FindObjectsOfType<UnitOcclusionMaterialReceiver>();
#endif
        if (receivers == null || receivers.Length == 0)
        {
            settings.lastOccluderStatus = "COLLECT V19 unit occluders: no receivers.";
            return;
        }

        List<GameObject> rootBuffer = new List<GameObject>(8);
        for (int i = 0; i < receivers.Length; i++)
        {
            UnitOcclusionMaterialReceiver receiver = receivers[i];
            if (receiver == null || receiver.gameObject == null || !receiver.gameObject.activeInHierarchy)
                continue;
            checkedReceivers++;
            if (!receiver.CurrentOccluded || receiver.ActiveOccluderCount <= 0)
                continue;

            ChannelRuntime runtime = ResolveRuntimeForPath(GetPath(receiver.transform));
            if (runtime == null || runtime.settings == null || !runtime.settings.enabled)
                continue;

            rootBuffer.Clear();
            receiver.CollectActiveFrontOccluderRoots(rootBuffer);
            if (rootBuffer.Count <= 0)
                continue;

            activeReceivers++;
            for (int r = 0; r < rootBuffer.Count; r++)
            {
                GameObject root = rootBuffer[r];
                if (root == null)
                    continue;
                authorizedRoots++;
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int k = 0; k < renderers.Length; k++)
                {
                    Renderer renderer = renderers[k];
                    if (renderer == null || renderer.gameObject == null)
                        continue;
                    if (layer >= 0 && renderer.gameObject.layer != layer)
                        continue;
                    if (settings.requireRendererEnabled && !renderer.enabled)
                        continue;
                    if (settings.requireActiveInHierarchy && !renderer.gameObject.activeInHierarchy)
                        continue;
                    string path = GetPath(renderer.transform);
                    if (ContainsAny(path, rejects))
                        continue;
                    if (!runtime.occluderRenderers.Contains(renderer))
                    {
                        runtime.occluderRenderers.Add(renderer);
                        addedRenderers++;
                    }
                }
            }
        }

        settings.lastOccluderStatus = "COLLECT V19 UNIT occluders: receivers=" + checkedReceivers +
                                      ", activeReceivers=" + activeReceivers +
                                      ", authorizedRoots=" + authorizedRoots +
                                      ", addedRenderers=" + addedRenderers +
                                      ", layer=" + settings.occluderLayerName +
                                      ", originalMaterials=" + settings.drawOccludersWithOriginalMaterials;
    }

    private sealed class CharacterMaskPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly ChannelRuntime channel;
        private sealed class PassData
        {
            public Settings settings;
            public ChannelSettings channel;
            public Camera camera;
            public RTHandle target;
            public Renderer[] renderers;
            public bool drawDisabledAuthorizedProxyRenderers;
            public bool drawInactiveAuthorizedProxyRenderers;
        }

        public CharacterMaskPass(Settings settings, ChannelRuntime channel)
        {
            this.settings = settings;
            this.channel = channel;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (channel == null || channel.settings == null || channel.characterHandle == null)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison V17 CharacterMask " + channel.settings.channelName, out PassData passData))
            {
                passData.settings = settings;
                passData.channel = channel.settings;
                passData.camera = cameraData.camera;
                passData.target = channel.characterHandle;
                passData.renderers = channel.renderers.ToArray();
                passData.drawDisabledAuthorizedProxyRenderers = settings.drawDisabledAuthorizedProxyRenderers;
                passData.drawInactiveAuthorizedProxyRenderers = settings.drawInactiveAuthorizedProxyRenderers;

                TextureHandle targetTexture = renderGraph.ImportTexture(channel.characterHandle);
                builder.SetRenderAttachment(targetTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(false, true, Color.clear);
                    int rendererCount = 0;
                    int drawCalls = 0;
                    int passIndex = Mathf.Max(0, data.settings.fullAlphaMaskPassIndex);

                    if (data.renderers != null)
                    {
                        for (int i = 0; i < data.renderers.Length; i++)
                        {
                            Renderer r = data.renderers[i];
                            if (r == null || r.gameObject == null)
                                continue;
                            if (!data.drawDisabledAuthorizedProxyRenderers && !r.enabled)
                                continue;
                            if (!data.drawInactiveAuthorizedProxyRenderers && !r.gameObject.activeInHierarchy)
                                continue;

                            Material[] mats = r.sharedMaterials;
                            bool drawn = false;
                            for (int sub = 0; sub < mats.Length; sub++)
                            {
                                Material mat = mats[sub];
                                if (mat == null || mat.shader == null)
                                    continue;

                                // V11: OccludedProxy materials are allowed to be mask/silhouette materials.
                                // If they use SpineOcclusionComposite, use the FullAlphaMask pass.
                                // Otherwise draw pass 0 so the active proxy still contributes to CharacterMask.
                                int materialPass = mat.shader.name == data.settings.spineOcclusionShaderName ? passIndex : 0;
                                context.cmd.DrawRenderer(r, mat, sub, materialPass);
                                drawCalls++;
                                drawn = true;
                            }
                            if (drawn)
                                rendererCount++;
                        }
                    }

                    if (string.Equals(data.channel.channelName, DebugChannelToString(data.settings.debugChannel), StringComparison.OrdinalIgnoreCase))
                    {
                        data.settings.lastCharacterStatus = "OK CharacterMask V17 UNIT_AUTHORIZED " + data.channel.channelName + " -> " + SafeName(data.target) + ", renderers=" + rendererCount + ", drawCalls=" + drawCalls + ", passIndex=" + passIndex + ", camera=" + SafeName(data.camera);
                    }
                });
            }
        }
    }

    private sealed class OccluderMaskPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly Func<Material> materialGetter;
        private readonly Func<RTHandle> targetGetter;
        private readonly Func<Renderer[]> renderersGetter;
        private sealed class PassData
        {
            public Settings settings;
            public Material fallbackMaterial;
            public Camera camera;
            public RTHandle target;
            public Renderer[] renderers;
            public bool drawDisabledAuthorizedProxyRenderers;
            public bool drawInactiveAuthorizedProxyRenderers;
        }

        public OccluderMaskPass(Settings settings, Func<Material> materialGetter, Func<RTHandle> targetGetter, Func<Renderer[]> renderersGetter)
        {
            this.settings = settings;
            this.materialGetter = materialGetter;
            this.targetGetter = targetGetter;
            this.renderersGetter = renderersGetter;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            RTHandle target = targetGetter();
            if (target == null)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Clean OccluderMask", out PassData passData))
            {
                passData.settings = settings;
                passData.fallbackMaterial = materialGetter();
                passData.camera = cameraData.camera;
                passData.target = target;
                passData.renderers = renderersGetter();
                passData.drawDisabledAuthorizedProxyRenderers = !settings.shadowMaskRequireRendererEnabled;
                passData.drawInactiveAuthorizedProxyRenderers = !settings.shadowMaskRequireActiveInHierarchy;

                TextureHandle targetTexture = renderGraph.ImportTexture(target);
                builder.SetRenderAttachment(targetTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(false, true, Color.clear);
                    int drawCount = 0;
                    int rendererCount = 0;

                    if (data.renderers != null)
                    {
                        for (int i = 0; i < data.renderers.Length; i++)
                        {
                            Renderer r = data.renderers[i];
                            if (r == null || r.gameObject == null)
                                continue;
                            if (!data.drawDisabledAuthorizedProxyRenderers && !r.enabled)
                                continue;
                            if (!data.drawInactiveAuthorizedProxyRenderers && !r.gameObject.activeInHierarchy)
                                continue;

                            bool effectiveDrawOriginal = data.settings.drawOccludersWithOriginalMaterials && !data.settings.forceSolidOccluderMaskInput;
                            if (effectiveDrawOriginal)
                            {
                                Material[] mats = r.sharedMaterials;
                                bool drawn = false;
                                for (int sub = 0; sub < mats.Length; sub++)
                                {
                                    Material mat = mats[sub];
                                    if (mat == null)
                                        continue;
                                    context.cmd.DrawRenderer(r, mat, sub, 0);
                                    drawCount++;
                                    drawn = true;
                                }
                                if (drawn)
                                    rendererCount++;
                            }
                            else if (data.fallbackMaterial != null)
                            {
                                context.cmd.DrawRenderer(r, data.fallbackMaterial, 0, Mathf.Max(0, data.settings.fallbackOccluderPassIndex));
                                drawCount++;
                                rendererCount++;
                            }
                        }
                    }

                    data.settings.lastOccluderStatus = "OK OccluderMask -> " + SafeName(data.target) + ", renderers=" + rendererCount + ", drawCalls=" + drawCount + ", originalMaterials=" + data.settings.drawOccludersWithOriginalMaterials + ", forceSolid=" + data.settings.forceSolidOccluderMaskInput + ", effectiveOriginal=" + (data.settings.drawOccludersWithOriginalMaterials && !data.settings.forceSolidOccluderMaskInput) + ", camera=" + SafeName(data.camera);
                });
            }
        }
    }

    private sealed class HiddenCompositePass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly ChannelRuntime channel;
        private readonly Func<Material> materialGetter;
        private readonly Func<RTHandle> occluderHandleGetter;
        private readonly Func<RenderTexture> occluderRTGetter;
        private sealed class PassData
        {
            public Material material;
            public Settings settings;
            public ChannelSettings channel;
            public Camera camera;
            public RTHandle target;
            public RenderTexture characterRT;
            public RenderTexture occluderRT;
        }

        public HiddenCompositePass(Settings settings, ChannelRuntime channel, Func<Material> materialGetter, Func<RTHandle> occluderHandleGetter, Func<RenderTexture> occluderRTGetter)
        {
            this.settings = settings;
            this.channel = channel;
            this.materialGetter = materialGetter;
            this.occluderHandleGetter = occluderHandleGetter;
            this.occluderRTGetter = occluderRTGetter;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Material mat = materialGetter();
            RTHandle occluderHandle = occluderHandleGetter();
            RenderTexture occluderRT = occluderRTGetter();
            if (mat == null || channel == null || channel.settings == null || channel.characterHandle == null || channel.characterRT == null || channel.hiddenHandle == null || channel.hiddenRT == null || occluderHandle == null || occluderRT == null)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Clean HiddenComposite " + channel.settings.channelName, out PassData passData))
            {
                passData.material = mat;
                passData.settings = settings;
                passData.channel = channel.settings;
                passData.camera = cameraData.camera;
                passData.target = channel.hiddenHandle;
                passData.characterRT = channel.characterRT;
                passData.occluderRT = occluderRT;

                TextureHandle characterTexture = renderGraph.ImportTexture(channel.characterHandle);
                TextureHandle occluderTexture = renderGraph.ImportTexture(occluderHandle);
                TextureHandle targetTexture = renderGraph.ImportTexture(channel.hiddenHandle);
                builder.UseTexture(characterTexture, AccessFlags.Read);
                builder.UseTexture(occluderTexture, AccessFlags.Read);
                builder.SetRenderAttachment(targetTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(false, true, Color.clear);
                    data.material.SetTexture(CharacterMaskTexId, data.characterRT);
                    data.material.SetTexture(OccluderMaskTexId, data.occluderRT);
                    data.material.SetFloat(ThresholdId, data.settings.threshold);
                    data.material.SetFloat(SampleBothYId, data.settings.sampleBothY ? 1f : 0f);
                    CoreUtils.DrawFullScreen(context.cmd, data.material, null, 0);
                    data.settings.lastCompositeStatus = "OK HiddenComposite " + data.channel.channelName + " -> " + SafeName(data.target) + ", character=" + SafeName(data.characterRT) + ", occluder=" + SafeName(data.occluderRT) + ", camera=" + SafeName(data.camera);
                });
            }
        }
    }

    private sealed class BodyBindPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly ChannelRuntime channel;
        private readonly Func<RenderTexture> occluderRTGetter;
        private sealed class PassData
        {
            public Settings settings;
            public ChannelSettings channel;
            public Renderer[] renderers;
            public RenderTexture hiddenRT;
            public RenderTexture characterRT;
            public RenderTexture occluderRT;
            public bool drawDisabledAuthorizedProxyRenderers;
            public bool drawInactiveAuthorizedProxyRenderers;
        }

        public BodyBindPass(Settings settings, ChannelRuntime channel, Func<RenderTexture> occluderRTGetter)
        {
            this.settings = settings;
            this.channel = channel;
            this.occluderRTGetter = occluderRTGetter;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (channel == null || channel.settings == null || channel.hiddenRT == null || channel.characterRT == null || channel.renderers.Count == 0)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Clean BodyBind " + channel.settings.channelName, out PassData passData))
            {
                passData.settings = settings;
                passData.channel = channel.settings;
                passData.renderers = channel.bodyRenderers.ToArray();
                passData.hiddenRT = channel.hiddenRT;
                passData.characterRT = channel.characterRT;
                passData.occluderRT = occluderRTGetter();
                passData.drawDisabledAuthorizedProxyRenderers = settings.drawDisabledAuthorizedProxyRenderers;
                passData.drawInactiveAuthorizedProxyRenderers = settings.drawInactiveAuthorizedProxyRenderers;

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    int rendererCount = 0;
                    int slotCount = 0;
                    if (data.renderers != null)
                    {
                        for (int i = 0; i < data.renderers.Length; i++)
                        {
                            Renderer r = data.renderers[i];
                            if (r == null || r.gameObject == null)
                                continue;
                            if (!data.drawDisabledAuthorizedProxyRenderers && !r.enabled)
                                continue;
                            if (!data.drawInactiveAuthorizedProxyRenderers && !r.gameObject.activeInHierarchy)
                                continue;

                            Material[] mats = r.sharedMaterials;
                            bool touched = false;
                            for (int m = 0; m < mats.Length; m++)
                            {
                                Material mat = mats[m];
                                if (mat == null || mat.shader == null || mat.shader.name != data.settings.spineOcclusionShaderName)
                                    continue;

                                mat.SetTexture(OcclusionTexId, data.hiddenRT);
                                mat.SetTexture(CharacterMaskTexBodyId, data.characterRT);
                                mat.SetTexture(OccluderMaskTexBodyId, data.occluderRT);
                                mat.SetColor(HiddenOutlineColorBodyId, data.channel.outlineColor);
                                mat.SetFloat(EnableBodyClipId, data.settings.enableBodyClip ? 1f : 0f);
                                mat.SetFloat(EnableHiddenOutlineBodyId, (data.settings.enableHiddenOutline && !(data.settings.drawFinalOutlineWithHiddenEdgeOverlay && data.settings.disableSpineInternalOutlineWhenUsingFinalOverlay)) ? 1f : 0f);
                                mat.SetFloat(HiddenOutlineWidthBodyId, Mathf.Max(1f, data.settings.outlineWidthPixels));
                                mat.SetFloat(HiddenOutlineAlphaBodyId, Mathf.Clamp01(data.settings.outlineAlpha * data.channel.outlineColor.a));
                                mat.SetFloat(DebugBodyMaskModeId, 0f);
                                if (data.settings.forceMaskYSettings)
                                {
                                    mat.SetFloat(FlipMaskYId, 0f);
                                    mat.SetFloat(SampleBothYId, data.settings.sampleBothY ? 1f : 0f);
                                }
                                slotCount++;
                                touched = true;
                            }
                            if (touched)
                                rendererCount++;
                        }
                    }
                    data.settings.lastBodyBindStatus = "OK BodyBind " + data.channel.channelName + ", hidden=" + SafeName(data.hiddenRT) + ", character=" + SafeName(data.characterRT) + ", occluder=" + SafeName(data.occluderRT) + ", renderers=" + rendererCount + ", slots=" + slotCount;
                });
            }
        }
    }

    private sealed class FinalOutlineOverlayPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly ChannelRuntime[] channels;
        private readonly Func<Material> materialGetter;
        private sealed class PassData
        {
            public Settings settings;
            public Material material;
            public Camera camera;
            public ChannelRuntime[] channels;
            public RenderTexture occluderRT;
        }

        public FinalOutlineOverlayPass(Settings settings, ChannelRuntime[] channels, Func<Material> materialGetter)
        {
            this.settings = settings;
            this.channels = channels;
            this.materialGetter = materialGetter;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!settings.drawFinalOutlineWithHiddenEdgeOverlay || !settings.enableHiddenOutline || settings.debugViewMode != DebugViewMode.Off)
                return;

            Material mat = materialGetter();
            if (mat == null || channels == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Final HiddenEdge Faction Outline", out PassData passData))
            {
                passData.settings = settings;
                passData.material = mat;
                passData.camera = cameraData.camera;
                passData.channels = channels;
                passData.occluderRT = null;

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    int drawnChannels = 0;
                    for (int i = 0; i < data.channels.Length; i++)
                    {
                        ChannelRuntime ch = data.channels[i];
                        if (ch == null || ch.settings == null || !ch.settings.enabled || ch.characterRT == null || ch.occluderRT == null)
                            continue;

                        Color c = ch.settings.outlineColor;
                        Color tint = new Color(c.r, c.g, c.b, Mathf.Clamp01(data.settings.outlineAlpha * c.a));

                        // V20: final overlay uses the same proven inputs as Debug HiddenEdge.
                        // CharacterRT is unit-authorized; OccluderRT is unit-authorized per channel.
                        // Do not depend on hiddenRT here, because an empty/stale hiddenRT makes normal mode invisible while Debug HiddenEdge works.
                        RenderTexture effectiveOccluder = ch.globalOccluderOverride ?? ch.occluderRT;
                        data.material.SetTexture(CharacterMaskTexId, ch.characterRT);
                        data.material.SetTexture(OccluderMaskTexId, effectiveOccluder);
                        data.material.SetFloat(ThresholdId, data.settings.threshold);
                        data.material.SetFloat(SampleBothYId, data.settings.sampleBothY ? 1f : 0f);
                        data.material.SetFloat(DebugViewModeId, 4f);
                        data.material.SetColor(DebugTintId, tint);
                        data.material.SetFloat(DebugAlphaId, Mathf.Clamp01(data.settings.outlineAlpha));
                        data.material.SetFloat(DebugEdgeWidthPixelsId, Mathf.Max(1f, data.settings.outlineWidthPixels));
                        float w = Mathf.Max(1, ch.characterRT.width);
                        float h = Mathf.Max(1, ch.characterRT.height);
                        data.material.SetVector(DebugTexelSizeId, new Vector4(1f / w, 1f / h, w, h));

                        CoreUtils.DrawFullScreen(context.cmd, data.material, null, 1);
                        drawnChannels++;
                    }

                    data.settings.lastDebugStatus = "OK FinalHiddenEdgeOverlay V20 live unit masks channels=" + drawnChannels + ", camera=" + SafeName(data.camera) + ", width=" + data.settings.outlineWidthPixels + ", alpha=" + data.settings.outlineAlpha;
                });
            }
        }
    }

    private sealed class DebugOverlayPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly ChannelRuntime[] channels;
        private readonly Func<Material> materialGetter;
        private readonly Func<Material[]> finalMaterialsGetter;
        private sealed class PassData
        {
            public Settings settings;
            public Material material;
            public Material[] finalMaterials;
            public Camera camera;
            public ChannelRuntime channel;
            public ChannelRuntime[] channels;
            public RenderTexture occluderRT;
            public bool finalOverlayMode;
        }

        public DebugOverlayPass(Settings settings, ChannelRuntime[] channels, Func<Material> materialGetter, Func<Material[]> finalMaterialsGetter)
        {
            this.settings = settings;
            this.channels = channels;
            this.materialGetter = materialGetter;
            this.finalMaterialsGetter = finalMaterialsGetter;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            bool finalOverlayMode = settings.debugViewMode == DebugViewMode.Off &&
                                    settings.drawFinalOutlineWithHiddenEdgeOverlay &&
                                    settings.enableHiddenOutline;
            if (settings.debugViewMode == DebugViewMode.Off && !finalOverlayMode)
                return;

            Material mat = materialGetter();
            int index = Mathf.Clamp((int)settings.debugChannel, 0, channels.Length - 1);
            ChannelRuntime ch = channels[index];
            if (mat == null)
                return;
            if (!finalOverlayMode && (ch == null || ch.characterRT == null || ch.hiddenRT == null))
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Clean DebugOverlay", out PassData passData))
            {
                passData.settings = settings;
                passData.material = mat;
                passData.finalMaterials = finalMaterialsGetter != null ? finalMaterialsGetter() : null;
                passData.camera = cameraData.camera;
                passData.channel = ch;
                passData.channels = channels;
                passData.occluderRT = null;
                passData.finalOverlayMode = finalOverlayMode;

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    if (data.finalOverlayMode)
                    {
                        int drawn = 0;
                        for (int i = 0; i < data.channels.Length; i++)
                        {
                            ChannelRuntime ch = data.channels[i];
                            if (ch == null || ch.settings == null || !ch.settings.enabled || ch.characterRT == null || ch.occluderRT == null)
                                continue;

                            Material drawMat = data.material;
                            if (data.finalMaterials != null && i >= 0 && i < data.finalMaterials.Length && data.finalMaterials[i] != null)
                                drawMat = data.finalMaterials[i];

                            Color c = ch.settings.outlineColor;
                            Color finalTint = new Color(c.r, c.g, c.b, Mathf.Clamp01(data.settings.outlineAlpha * c.a));
                            // V20: final overlay uses the same proven inputs as Debug HiddenEdge for every channel.
                            // CharacterRT is unit-authorized; OccluderRT is unit-authorized per channel.
                            // Do not use hiddenRT here; when hiddenRT is empty, Debug HiddenEdge works but normal display disappears.
                            RenderTexture effectiveOccluder = ch.globalOccluderOverride ?? ch.occluderRT;
                            drawMat.SetTexture(CharacterMaskTexId, ch.characterRT);
                            drawMat.SetTexture(OccluderMaskTexId, effectiveOccluder);
                            drawMat.SetFloat(ThresholdId, data.settings.threshold);
                            drawMat.SetFloat(SampleBothYId, data.settings.sampleBothY ? 1f : 0f);
                            float w = Mathf.Max(1, ch.characterRT.width);
                            float h = Mathf.Max(1, ch.characterRT.height);
                            drawMat.SetVector(DebugTexelSizeId, new Vector4(1f / w, 1f / h, w, h));

                            // V10: draw the authorized hidden area first, then draw the edge on top.
                            // This restores the visible occluded silhouette without allowing unauthorized front units into CharacterMask.
                            if (data.settings.drawFinalHiddenMaskFillOverlay && data.settings.finalHiddenMaskFillAlpha > 0.001f)
                            {
                                Color fillTint = new Color(c.r, c.g, c.b, Mathf.Clamp01(data.settings.finalHiddenMaskFillAlpha * c.a));
                                drawMat.SetFloat(DebugViewModeId, 3f); // HiddenMask fill
                                drawMat.SetColor(DebugTintId, fillTint);
                                drawMat.SetFloat(DebugAlphaId, Mathf.Clamp01(data.settings.finalHiddenMaskFillAlpha));
                                drawMat.SetFloat(DebugEdgeWidthPixelsId, Mathf.Max(1f, data.settings.outlineWidthPixels));
                                CoreUtils.DrawFullScreen(context.cmd, drawMat, null, 1);
                            }

                            drawMat.SetFloat(DebugViewModeId, 4f); // HiddenEdge outline
                            drawMat.SetColor(DebugTintId, finalTint);
                            drawMat.SetFloat(DebugAlphaId, Mathf.Clamp01(data.settings.outlineAlpha));
                            drawMat.SetFloat(DebugEdgeWidthPixelsId, Mathf.Max(1f, data.settings.outlineWidthPixels));
                            CoreUtils.DrawFullScreen(context.cmd, drawMat, null, 1);
                            drawn++;
                        }
                        data.settings.lastDebugStatus = "OK FinalOutline V20 live unit masks. channels=" + drawn + ", camera=" + SafeName(data.camera);
                        return;
                    }

                    RenderTexture character = data.channel.characterRT;
                    RenderTexture occluder = data.channel.occluderRT;
                    Color tint = data.settings.debugCharacterColor;
                    float mode = 1f;

                    if (data.settings.debugViewMode == DebugViewMode.CharacterMask)
                    {
                        character = data.channel.characterRT;
                        occluder = data.channel.occluderRT;
                        mode = 1f;
                        tint = data.settings.debugCharacterColor;
                    }
                    else if (data.settings.debugViewMode == DebugViewMode.OccluderMask)
                    {
                        character = data.channel.occluderRT;
                        occluder = data.channel.occluderRT;
                        mode = 1f;
                        tint = data.settings.debugOccluderColor;
                    }
                    else if (data.settings.debugViewMode == DebugViewMode.HiddenMask)
                    {
                        character = data.channel.characterRT;
                        occluder = data.channel.occluderRT;
                        mode = 3f;
                        tint = data.settings.debugHiddenColor;
                    }
                    else if (data.settings.debugViewMode == DebugViewMode.HiddenEdge)
                    {
                        character = data.channel.characterRT;
                        occluder = data.channel.occluderRT;
                        mode = 4f;
                        tint = new Color(data.channel.settings.outlineColor.r, data.channel.settings.outlineColor.g, data.channel.settings.outlineColor.b, data.settings.debugEdgeColor.a);
                    }

                    data.material.SetTexture(CharacterMaskTexId, character);
                    data.material.SetTexture(OccluderMaskTexId, occluder == null ? data.channel.occluderRT : occluder);
                    data.material.SetFloat(ThresholdId, data.settings.threshold);
                    data.material.SetFloat(SampleBothYId, data.settings.sampleBothY ? 1f : 0f);
                    data.material.SetFloat(DebugViewModeId, mode);
                    data.material.SetColor(DebugTintId, tint);
                    data.material.SetFloat(DebugAlphaId, Mathf.Clamp01(data.settings.debugAlpha));
                    data.material.SetFloat(DebugEdgeWidthPixelsId, Mathf.Max(1f, data.settings.debugEdgeWidthPixels));
                    float dw = Mathf.Max(1, data.channel.characterRT.width);
                    float dh = Mathf.Max(1, data.channel.characterRT.height);
                    data.material.SetVector(DebugTexelSizeId, new Vector4(1f / dw, 1f / dh, dw, dh));

                    CoreUtils.DrawFullScreen(context.cmd, data.material, null, 1);
                    data.settings.lastDebugStatus = "OK DebugOverlay mode=" + data.settings.debugViewMode + ", channel=" + data.settings.debugChannel + ", camera=" + SafeName(data.camera);
                });
            }
        }
    }

    private static Transform FindNearestNamedChildOrSelf(Transform start, string targetName)
    {
        if (start == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        Transform current = start;
        while (current != null)
        {
            if (string.Equals(current.name, targetName, StringComparison.OrdinalIgnoreCase))
                return current;

            Transform found = FindChildRecursiveByName(current, targetName);
            if (found != null)
                return found;

            current = current.parent;
        }

        return null;
    }

    private static Transform FindChildRecursiveByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;
            if (string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase))
                return child;

            Transform nested = FindChildRecursiveByName(child, targetName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static string[] SplitKeywords(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Array.Empty<string>();
        string[] raw = s.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < raw.Length; i++) raw[i] = raw[i].Trim();
        return raw;
    }

    private static bool ContainsAny(string value, string[] keywords)
    {
        if (string.IsNullOrEmpty(value) || keywords == null) return false;
        for (int i = 0; i < keywords.Length; i++)
        {
            string k = keywords[i];
            if (!string.IsNullOrEmpty(k) && value.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "null";
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private static string SafeName(UnityEngine.Object obj) { return obj == null ? "null" : obj.name; }
    private static string SafeName(RTHandle handle) { return handle == null ? "null" : handle.name; }

    private static void ReleaseRT(ref RenderTexture rt)
    {
        if (rt == null) return;
        if (rt.IsCreated()) rt.Release();
        if (Application.isPlaying) Destroy(rt); else DestroyImmediate(rt);
        rt = null;
    }

    private static void ReleaseRTHandle(ref RTHandle handle)
    {
        if (handle == null) return;
        handle.Release();
        handle = null;
    }

    private static void DestroyMaterial(ref Material mat)
    {
        if (mat == null) return;
        if (Application.isPlaying) Destroy(mat); else DestroyImmediate(mat);
        mat = null;
    }

#if UNITY_EDITOR
    [MenuItem("Tools/Sky Prison/Rendering/Install Hidden Mask Outline Feature V16 Build Safe Shader References")]
    private static void InstallFeatureMenu()
    {
        ScriptableRendererData[] renderers = Resources.FindObjectsOfTypeAll<ScriptableRendererData>();
        ScriptableRendererData target = null;
        for (int i = 0; i < renderers.Length; i++)
        {
            ScriptableRendererData r = renderers[i];
            if (r != null && r.name.IndexOf("UniversalRenderer3D", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                target = r;
                break;
            }
        }
        if (target == null)
        {
            EditorUtility.DisplayDialog("Sky Prison", "UniversalRenderer3D renderer data not found.", "OK");
            return;
        }

        SerializedObject so = new SerializedObject(target);
        SerializedProperty featuresProp = so.FindProperty("m_RendererFeatures");
        if (featuresProp == null || !featuresProp.isArray)
        {
            EditorUtility.DisplayDialog("Sky Prison", "Renderer feature list not found on UniversalRenderer3D.", "OK");
            return;
        }

        for (int i = 0; i < featuresProp.arraySize; i++)
        {
            UnityEngine.Object obj = featuresProp.GetArrayElementAtIndex(i).objectReferenceValue;
            if (obj is SkyPrisonMainCameraHiddenMaskOutlineFeature_V18_BuildSafePublicGateState_CompileFix)
            {
                Selection.activeObject = target;
                EditorUtility.DisplayDialog("Sky Prison", "Hidden Mask Outline Feature V16 Build Safe Shader References is already installed.", "OK");
                return;
            }
        }

        SkyPrisonMainCameraHiddenMaskOutlineFeature_V18_BuildSafePublicGateState_CompileFix feature = CreateInstance<SkyPrisonMainCameraHiddenMaskOutlineFeature_V18_BuildSafePublicGateState_CompileFix>();
        feature.name = "Sky Prison Hidden Mask Outline Feature V16 Build Safe Shader References";
        AssetDatabase.AddObjectToAsset(feature, target);
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(target));
        featuresProp.arraySize++;
        featuresProp.GetArrayElementAtIndex(featuresProp.arraySize - 1).objectReferenceValue = feature;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();
        Selection.activeObject = target;
        EditorUtility.DisplayDialog("Sky Prison", "Installed Hidden Mask Outline Feature V16 Build Safe Shader References.", "OK");
    }
#endif
}
