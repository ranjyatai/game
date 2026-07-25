// SkyPrisonMainCameraStencilHiddenOutlineFeature_V26_ShaderAssetPathBinding.cs
// 2026-06-05
// Official hidden outline pass for four channels.
// V7: fixes the unstable stencil precompute by rendering FullAlphaMaskOnly directly into CharacterMask RT before transparent Spine rendering. HiddenMask is then precomputed and bound to Spine materials before the normal body pass.
// V2: also binds the new main-camera occluder mask RT back to Spine/SpineOcclusionComposite _OcclusionTex,
// so the visible body is clipped by the same-space occluder mask instead of the legacy oversized mask.
// - Character full-alpha stencil is written by Spine/SpineOcclusionComposite V21.
// - This feature extracts channel masks from stencil, renders one shared OcclusionMask RT,
//   then draws only CharacterMask x OccluderMask outlines to the main camera color.
// - Does not bind OutlineOverlay_Player.
// - Does not write legacy RT_Outline_*.
// - Does not use unit outline proxy meshes.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class SkyPrisonMainCameraStencilHiddenOutlineFeatureV23 : ScriptableRendererFeature
{
    [Serializable]
    public sealed class ChannelSettings
    {
        public bool enabled = true;
        public string channelName = "Player";
        [Range(0, 255)] public int stencilRef = 41;
        public string characterMaskRTName = "RT_MainCameraCharacterMask_Player";
        public string hiddenMaskRTName = "RT_MainCameraHiddenMask_Player";
        public string cleanOutlineRTName = "RT_MainCameraCleanCharacterOutline_Player";
        public Color outlineColor = new Color(1f, 0.83f, 0f, 1f);
        public string pathKeywords = "Player;PlayerRuntime";
    }

    [Serializable]
    public sealed class Settings
    {
        [Header("Version")]
        public string scriptVersion = "V26 - 2026-06-06 - shader asset paths bound to FrontOccluder/Outline folder";

        [Header("Camera Filter")]
        public bool onlyMainCamera = true;
        public string requiredCameraName = "Main Camera";

        [Header("Pass Events")]
        public RenderPassEvent clearStencilEvent = RenderPassEvent.BeforeRenderingTransparents;
        public RenderPassEvent preCharacterStencilEvent = RenderPassEvent.BeforeRenderingTransparents;
        public RenderPassEvent extractCharacterEvent = RenderPassEvent.BeforeRenderingTransparents;
        public RenderPassEvent occluderMaskEvent = RenderPassEvent.BeforeRenderingTransparents;
        public RenderPassEvent compositeEvent = RenderPassEvent.BeforeRenderingTransparents;
        public RenderPassEvent cleanCharacterOutlineEvent = RenderPassEvent.BeforeRenderingTransparents;
        public RenderPassEvent bodyClipApplyEvent = RenderPassEvent.BeforeRenderingTransparents;
        public RenderPassEvent outlineEvent = RenderPassEvent.AfterRenderingTransparents;

        [Header("Precomputed Character Mask")]
        [Tooltip("V25 Spine/SpineOcclusionComposite pass index: 0=FullAlphaStencilOnly, 1=FullAlphaMaskOnly, 2=NormalOutsideMask.")]
        public int fullAlphaMaskPassIndex = 1;

        [Header("Stencil")]
        [Range(0, 255)] public int stencilReadMask = 255;
        [Range(0, 255)] public int stencilWriteMask = 255;

        [Header("Channels")]
        public ChannelSettings player = new ChannelSettings
        {
            enabled = true,
            channelName = "Player",
            stencilRef = 41,
            characterMaskRTName = "RT_MainCameraCharacterMask_Player",
            hiddenMaskRTName = "RT_MainCameraHiddenMask_Player",
            cleanOutlineRTName = "RT_MainCameraCleanCharacterOutline_Player",
            outlineColor = new Color(1f, 0.83f, 0f, 1f),
            pathKeywords = "Player;PlayerRuntime"
        };
        public ChannelSettings enemy = new ChannelSettings
        {
            enabled = true,
            channelName = "Enemy",
            stencilRef = 42,
            characterMaskRTName = "RT_MainCameraCharacterMask_Enemy",
            hiddenMaskRTName = "RT_MainCameraHiddenMask_Enemy",
            cleanOutlineRTName = "RT_MainCameraCleanCharacterOutline_Enemy",
            outlineColor = new Color(1f, 0.16f, 0.16f, 1f),
            pathKeywords = "Enemy;Mob;Monster"
        };
        public ChannelSettings item = new ChannelSettings
        {
            enabled = true,
            channelName = "Item",
            stencilRef = 43,
            characterMaskRTName = "RT_MainCameraCharacterMask_Item",
            hiddenMaskRTName = "RT_MainCameraHiddenMask_Item",
            cleanOutlineRTName = "RT_MainCameraCleanCharacterOutline_Item",
            outlineColor = new Color(1f, 1f, 1f, 1f),
            pathKeywords = "Item;Drop;Loot;Pickup"
        };
        public ChannelSettings ally = new ChannelSettings
        {
            enabled = true,
            channelName = "Ally",
            stencilRef = 44,
            characterMaskRTName = "RT_MainCameraCharacterMask_Ally",
            hiddenMaskRTName = "RT_MainCameraHiddenMask_Ally",
            cleanOutlineRTName = "RT_MainCameraCleanCharacterOutline_Ally",
            outlineColor = new Color(0.56f, 0.86f, 1f, 1f),
            pathKeywords = "Ally;Friend"
        };

        [Header("Runtime Material Stencil Auto Config")]
        public bool autoConfigureMaterialsInPlayMode = true;
        public string spineOcclusionShaderName = "Spine/SpineOcclusionComposite";
        public bool configureOnlyOncePerRenderer = true;
        public bool bindNewOccluderMaskToSpineBody = true;
        [Tooltip("V6: do not bind body clip texture during AddRenderPasses. A dedicated BodyClipApplyPass runs after HiddenComposite and before normal transparent Spine rendering, binding the freshly generated per-channel HiddenMask to _OcclusionTex.")]
        public bool usePrecomputedHiddenMaskForBodyClip = true;
        public bool forceSpineMaskYSettings = true;

        [Header("Occluder Source")]
        public string occluderLayerName = "OcclusionMask";
        public bool requireRendererEnabled = true;
        public bool requireActiveInHierarchy = true;
        public string rejectPathKeywords = "outline;shadow;canvas;ui;character;player;enemy;ally;item";

        [Header("Target RTs - independent, do not use legacy RTs")]
        public string occluderMaskRTName = "RT_MainCameraOccluderMask_All";
        public bool matchCameraSize = true;
        public int fallbackWidth = 1920;
        public int fallbackHeight = 1080;
        public FilterMode filterMode = FilterMode.Point;

        [Header("Shader Asset Paths - Assets/_Project/Art/Shaders/Custom/FrontOccluder/Outline")]
        [Tooltip("Editor/Play Mode first loads this exact asset path. Runtime build still falls back to Shader.Find by shader name.")]
        public string stencilClearShaderAssetPath = "Assets/_Project/Art/Shaders/Custom/FrontOccluder/Outline/SkyPrisonStencilClearOnly.shader";
        public string stencilToMaskShaderAssetPath = "Assets/_Project/Art/Shaders/Custom/FrontOccluder/Outline/SkyPrisonStencilToMask.shader";
        public string occluderMaskShaderAssetPath = "Assets/_Project/Art/Shaders/Custom/FrontOccluder/Outline/SkyPrisonOccluderSolidMask.shader";
        public string compositeShaderAssetPath = "Assets/_Project/Art/Shaders/Custom/FrontOccluder/Outline/UnitHiddenMaskComposite.shader";
        public string cleanCharacterOutlineShaderAssetPath = "Assets/_Project/Art/Shaders/Custom/FrontOccluder/Outline/SkyPrisonCleanCharacterSilhouetteOutlineMask.shader";
        public string hiddenOutlineShaderAssetPath = "Assets/_Project/Art/Shaders/Custom/FrontOccluder/Outline/SkyPrisonCleanFinalHiddenOutlineOverlay.shader";

        [Header("Shader Names - fallback for build / if asset path fails")]
        public string stencilClearShaderName = "Hidden/SkyPrison/StencilClearOnly";
        public string stencilToMaskShaderName = "Hidden/SkyPrison/StencilToMask";
        public string occluderMaskShaderName = "Hidden/SkyPrison/OccluderSolidMask";
        public string compositeShaderName = "Hidden/SkyPrison/MaskMultiplyComposite";
        public string cleanCharacterOutlineShaderName = "Hidden/SkyPrison/CleanCharacterSilhouetteOutlineMask";
        public string hiddenOutlineShaderName = "Hidden/SkyPrison/CleanFinalHiddenOutlineOverlay";

        public enum OutlineDebugMode
        {
            OutlineOnly = 0,
            FillHiddenMask = 1,
            DilatedHiddenMask = 2,
            ErodedHiddenMask = 3,
            RawAlpha = 4,
            ForceFullscreenColor = 5,
        }

        [Header("Outline")]
        [Range(0, 1)] public float threshold = 0.01f;
        [Range(1, 12)] public int outlineWidthPixels = 2;
        public bool enableOfficialOverlay = true; // V25: final visible outline is drawn by clean fullscreen overlay.
        public bool enableMeshHiddenOutline = false;
        [Range(0f, 1f)] public float meshHiddenOutlineAlpha = 1f;
        public OutlineDebugMode outlineDebugMode = OutlineDebugMode.OutlineOnly;
        [Range(0.05f, 1f)] public float debugFillAlpha = 0.65f;

        [Header("Clean Character Outline Input")]
        public bool enableCleanCharacterOutlineInput = true;
        [Range(1.0f, 4.0f)] public float cleanOutlineFarSampleScale = 2.4f;
        [Range(1, 8)] public int cleanOutlineMinStableVotes = 2;
        [Range(0.01f, 0.95f)] public float cleanOutlineNoiseThreshold = 0.55f;

        [Header("Debug - read only")]
        [TextArea(2, 5)] public string lastCameraStatus = "-";
        [TextArea(2, 5)] public string lastMaterialStatus = "-";
        [TextArea(2, 5)] public string lastClearStatus = "-";
        [TextArea(2, 5)] public string lastCharacterStatus = "-";
        [TextArea(2, 6)] public string lastOccluderStatus = "-";
        [TextArea(2, 6)] public string lastOutlineStatus = "-";

        public void EnsureDefaultShaderAssetPaths()
        {
            const string folder = "Assets/_Project/Art/Shaders/Custom/FrontOccluder/Outline/";

            if (string.IsNullOrWhiteSpace(stencilClearShaderAssetPath))
                stencilClearShaderAssetPath = folder + "SkyPrisonStencilClearOnly.shader";
            if (string.IsNullOrWhiteSpace(stencilToMaskShaderAssetPath))
                stencilToMaskShaderAssetPath = folder + "SkyPrisonStencilToMask.shader";
            if (string.IsNullOrWhiteSpace(occluderMaskShaderAssetPath))
                occluderMaskShaderAssetPath = folder + "SkyPrisonOccluderSolidMask.shader";
            if (string.IsNullOrWhiteSpace(compositeShaderAssetPath))
                compositeShaderAssetPath = folder + "UnitHiddenMaskComposite.shader";
            if (string.IsNullOrWhiteSpace(cleanCharacterOutlineShaderAssetPath))
                cleanCharacterOutlineShaderAssetPath = folder + "SkyPrisonCleanCharacterSilhouetteOutlineMask.shader";
            if (string.IsNullOrWhiteSpace(hiddenOutlineShaderAssetPath))
                hiddenOutlineShaderAssetPath = folder + "SkyPrisonCleanFinalHiddenOutlineOverlay.shader";

            if (string.IsNullOrWhiteSpace(cleanCharacterOutlineShaderName))
                cleanCharacterOutlineShaderName = "Hidden/SkyPrison/CleanCharacterSilhouetteOutlineMask";
            if (string.IsNullOrWhiteSpace(hiddenOutlineShaderName) || hiddenOutlineShaderName.Contains("MainCameraHiddenOutline", StringComparison.OrdinalIgnoreCase))
                hiddenOutlineShaderName = "Hidden/SkyPrison/CleanFinalHiddenOutlineOverlay";
        }
    }

    private sealed class ChannelRuntime
    {
        public ChannelSettings settings;
        public RenderTexture maskRT;
        public RTHandle maskHandle;
        public RenderTexture hiddenRT;
        public RTHandle hiddenHandle;
        public RenderTexture cleanOutlineRT;
        public RTHandle cleanOutlineHandle;
        public readonly List<Renderer> renderers = new List<Renderer>(16);
        public FullAlphaStencilPrePass preStencilPass;
        public FullAlphaMaskColorPass alphaMaskPass;
        public StencilExtractPass extractPass;
        public HiddenCompositePass compositePass;
        public CleanCharacterOutlinePass cleanOutlinePass;
        public BodyClipApplyPass bodyClipApplyPass;
        public HiddenOutlinePass outlinePass;
    }

    public Settings settings = new Settings();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (settings != null)
            settings.EnsureDefaultShaderAssetPaths();
    }
#endif

    private Material clearMaterial;
    private Material stencilToMaskMaterial;
    private Material occluderMaskMaterial;
    private Material compositeMaterial;
    private Material cleanCharacterOutlineMaterial;
    private Material hiddenOutlineMaterial;

    private RenderTexture occluderMaskRT;
    private RTHandle occluderMaskHandle;

    private StencilClearPass clearPass;
    private OccluderMaskPass occluderPass;
    private readonly ChannelRuntime[] channels = new ChannelRuntime[4];
    private readonly List<Renderer> occluderRenderers = new List<Renderer>(64);
    private readonly HashSet<int> configuredRenderers = new HashSet<int>();

    // ConfigureUnitMaterials()/CollectOccluderRenderers() 各自都调用一次
    // Resources.FindObjectsOfTypeAll<Renderer>()——这是 Unity 里出了名的重量级API，
    // 会扫描所有已加载场景里的全部 Renderer。之前这两个函数是 AddRenderPasses 的
    // 一部分，等于每帧、每个摄像机都要做两次全场景扫描，是长时间卡顿排查中定位到
    // 的最大单一CPU开销来源。改成限频重扫（默认每0.5秒一次），场景里新增/消失单位
    // 最多延迟半秒被感知到，肉眼完全不会察觉，但去掉了逐帧的扫描开销。
    private const float UnitRescanInterval = 0.5f;
    private float _nextUnitRescanTime;

    private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
    private static readonly int StencilReadMaskId = Shader.PropertyToID("_StencilReadMask");
    private static readonly int StencilWriteMaskId = Shader.PropertyToID("_StencilWriteMask");
    private static readonly int CharacterStencilRefId = Shader.PropertyToID("_SkyPrison_CharacterStencilRef");
    private static readonly int CharacterStencilReadMaskId = Shader.PropertyToID("_SkyPrison_CharacterStencilReadMask");
    private static readonly int CharacterStencilWriteMaskId = Shader.PropertyToID("_SkyPrison_CharacterStencilWriteMask");
    private static readonly int OcclusionTexId = Shader.PropertyToID("_OcclusionTex");
    private static readonly int CharacterMaskTexBodyId = Shader.PropertyToID("_SkyPrison_CharacterMaskTex");
    private static readonly int CleanCharacterOutlineTexBodyId = Shader.PropertyToID("_SkyPrison_CleanCharacterOutlineTex");
    private static readonly int UseCleanCharacterOutlineTexBodyId = Shader.PropertyToID("_SkyPrison_UseCleanCharacterOutlineTex");
    private static readonly int HiddenOutlineColorBodyId = Shader.PropertyToID("_SkyPrison_HiddenOutlineColor");
    private static readonly int EnableHiddenOutlineBodyId = Shader.PropertyToID("_SkyPrison_EnableHiddenOutline");
    private static readonly int HiddenOutlineWidthBodyId = Shader.PropertyToID("_SkyPrison_HiddenOutlineWidthPixels");
    private static readonly int HiddenOutlineAlphaBodyId = Shader.PropertyToID("_SkyPrison_HiddenOutlineAlpha");
    private static readonly int FlipMaskYId = Shader.PropertyToID("_FlipMaskY");
    private static readonly int SampleBothYId = Shader.PropertyToID("_SampleBothY");
    private static readonly int MaskColorId = Shader.PropertyToID("_MaskColor");
    private static readonly int CharacterMaskTexId = Shader.PropertyToID("_CharacterMaskTex");
    private static readonly int SkyPrisonHiddenMaskTexId = Shader.PropertyToID("_SkyPrisonHiddenMaskTex");
    private static readonly int OccluderMaskTexId = Shader.PropertyToID("_OccluderMaskTex");
    private static readonly int MaskTexelSizeId = Shader.PropertyToID("_MaskTexelSize");
    private static readonly int DebugModeId = Shader.PropertyToID("_DebugMode");
    private static readonly int DebugFillAlphaId = Shader.PropertyToID("_DebugFillAlpha");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthPixelsId = Shader.PropertyToID("_OutlineWidthPixels");
    private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
    private static readonly int CleanOutlineFarSampleScaleId = Shader.PropertyToID("_CleanOutlineFarSampleScale");
    private static readonly int CleanOutlineMinStableVotesId = Shader.PropertyToID("_CleanOutlineMinStableVotes");
    private static readonly int CleanOutlineNoiseThresholdId = Shader.PropertyToID("_CleanOutlineNoiseThreshold");

    public override void Create()
    {
        if (settings != null)
            settings.EnsureDefaultShaderAssetPaths();
        EnsureMaterials();
        BuildChannels();
        clearPass = new StencilClearPass(settings, () => clearMaterial);
        occluderPass = new OccluderMaskPass(settings, () => occluderMaskMaterial, () => occluderMaskHandle, () => occluderRenderers.ToArray());
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
        ApplyPassEvents();
        if (Time.realtimeSinceStartup >= _nextUnitRescanTime)
        {
            _nextUnitRescanTime = Time.realtimeSinceStartup + UnitRescanInterval;
            ConfigureUnitMaterials();
            CollectOccluderRenderers();
        }

        bool missingRequiredMaterial = clearMaterial == null || stencilToMaskMaterial == null || occluderMaskMaterial == null || compositeMaterial == null || (settings.enableCleanCharacterOutlineInput && cleanCharacterOutlineMaterial == null);
        bool missingOverlayMaterial = settings.enableOfficialOverlay && hiddenOutlineMaterial == null;
        if (missingRequiredMaterial || missingOverlayMaterial)
        {
            settings.lastCameraStatus = "SKIP missing material. clear=" + SafeName(clearMaterial) + ", stencil=" + SafeName(stencilToMaskMaterial) + ", occluder=" + SafeName(occluderMaskMaterial) + ", composite=" + SafeName(compositeMaterial) + ", outline=" + SafeName(hiddenOutlineMaterial) + ", overlay=" + settings.enableOfficialOverlay;
            return;
        }

        if (occluderMaskHandle == null)
        {
            settings.lastCameraStatus = "SKIP missing occluder RT handle.";
            return;
        }

        clearMaterial.SetFloat(StencilWriteMaskId, settings.stencilWriteMask);
        occluderMaskMaterial.SetColor(MaskColorId, Color.white);

        renderer.EnqueuePass(clearPass);

        // V7: do not rely on stencil extraction for the pre-body CharacterMask.
        // Draw the Spine full-alpha color mask directly into each channel RT before transparent body rendering.
        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null || ch.settings == null || !ch.settings.enabled || ch.maskHandle == null)
                continue;
            renderer.EnqueuePass(ch.alphaMaskPass);
        }

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null || ch.settings == null || !ch.settings.enabled || ch.maskHandle == null || ch.cleanOutlineHandle == null)
                continue;
            if (settings.enableCleanCharacterOutlineInput)
                renderer.EnqueuePass(ch.cleanOutlinePass);
        }

        renderer.EnqueuePass(occluderPass);
        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null || ch.settings == null || !ch.settings.enabled || ch.maskHandle == null || ch.hiddenHandle == null)
                continue;
            renderer.EnqueuePass(ch.compositePass);
        }
        if (settings.bindNewOccluderMaskToSpineBody && settings.usePrecomputedHiddenMaskForBodyClip)
        {
            for (int i = 0; i < channels.Length; i++)
            {
                ChannelRuntime ch = channels[i];
                if (ch == null || ch.settings == null || !ch.settings.enabled || ch.hiddenHandle == null || ch.hiddenRT == null || ch.renderers.Count == 0)
                    continue;
                renderer.EnqueuePass(ch.bodyClipApplyPass);
            }
        }

        if (settings.enableOfficialOverlay)
        {
            for (int i = 0; i < channels.Length; i++)
            {
                ChannelRuntime ch = channels[i];
                if (ch == null || ch.settings == null || !ch.settings.enabled || ch.maskHandle == null)
                    continue;
                renderer.EnqueuePass(ch.outlinePass);
            }
        }

        settings.lastCameraStatus = "OK queued V25 clean final overlay. camera=" + camera.name + ", occluder=" + SafeName(occluderMaskRT);
    }

    protected override void Dispose(bool disposing)
    {
        for (int i = 0; i < channels.Length; i++)
        {
            if (channels[i] != null)
            {
                ReleaseRTHandle(ref channels[i].maskHandle);
                ReleaseRT(ref channels[i].maskRT);
                ReleaseRTHandle(ref channels[i].hiddenHandle);
                ReleaseRT(ref channels[i].hiddenRT);
                ReleaseRTHandle(ref channels[i].cleanOutlineHandle);
                ReleaseRT(ref channels[i].cleanOutlineRT);
            }
        }
        ReleaseRTHandle(ref occluderMaskHandle);
        ReleaseRT(ref occluderMaskRT);
        DestroyMaterial(ref clearMaterial);
        DestroyMaterial(ref stencilToMaskMaterial);
        DestroyMaterial(ref occluderMaskMaterial);
        DestroyMaterial(ref compositeMaterial);
        DestroyMaterial(ref cleanCharacterOutlineMaterial);
        DestroyMaterial(ref hiddenOutlineMaterial);
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
            if (channels[i].preStencilPass == null)
                channels[i].preStencilPass = new FullAlphaStencilPrePass(settings, channels[i]);
            if (channels[i].alphaMaskPass == null)
                channels[i].alphaMaskPass = new FullAlphaMaskColorPass(settings, channels[i]);
            if (channels[i].extractPass == null)
                channels[i].extractPass = new StencilExtractPass(settings, channels[i], () => stencilToMaskMaterial);
            if (channels[i].compositePass == null)
                channels[i].compositePass = new HiddenCompositePass(settings, channels[i], () => compositeMaterial, () => occluderMaskHandle, () => occluderMaskRT);
            if (channels[i].cleanOutlinePass == null)
                channels[i].cleanOutlinePass = new CleanCharacterOutlinePass(settings, channels[i], () => cleanCharacterOutlineMaterial);
            if (channels[i].bodyClipApplyPass == null)
                channels[i].bodyClipApplyPass = new BodyClipApplyPass(settings, channels[i]);
            if (channels[i].outlinePass == null)
                channels[i].outlinePass = new HiddenOutlinePass(settings, channels[i], () => hiddenOutlineMaterial, () => occluderMaskHandle, () => occluderMaskRT);
        }
    }

    private void ApplyPassEvents()
    {
        if (clearPass != null) clearPass.renderPassEvent = settings.clearStencilEvent;
        if (occluderPass != null) occluderPass.renderPassEvent = settings.occluderMaskEvent;
        for (int i = 0; i < channels.Length; i++)
        {
            if (channels[i] == null) continue;
            if (channels[i].preStencilPass != null) channels[i].preStencilPass.renderPassEvent = settings.preCharacterStencilEvent;
            if (channels[i].alphaMaskPass != null) channels[i].alphaMaskPass.renderPassEvent = settings.preCharacterStencilEvent;
            if (channels[i].extractPass != null) channels[i].extractPass.renderPassEvent = settings.extractCharacterEvent;
            if (channels[i].compositePass != null) channels[i].compositePass.renderPassEvent = settings.compositeEvent;
            if (channels[i].cleanOutlinePass != null) channels[i].cleanOutlinePass.renderPassEvent = settings.cleanCharacterOutlineEvent;
            if (channels[i].bodyClipApplyPass != null) channels[i].bodyClipApplyPass.renderPassEvent = settings.bodyClipApplyEvent;
            if (channels[i].outlinePass != null) channels[i].outlinePass.renderPassEvent = settings.outlineEvent;
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
        if (!string.IsNullOrEmpty(settings.requiredCameraName) && camera.name != settings.requiredCameraName)
            return false;
        if (Camera.main != null && camera != Camera.main)
            return false;
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
        EnsureMaterial(ref clearMaterial, settings.stencilClearShaderAssetPath, settings.stencilClearShaderName);
        EnsureMaterial(ref stencilToMaskMaterial, settings.stencilToMaskShaderAssetPath, settings.stencilToMaskShaderName);
        EnsureMaterial(ref occluderMaskMaterial, settings.occluderMaskShaderAssetPath, settings.occluderMaskShaderName);
        EnsureMaterial(ref compositeMaterial, settings.compositeShaderAssetPath, settings.compositeShaderName);
        EnsureMaterial(ref cleanCharacterOutlineMaterial, settings.cleanCharacterOutlineShaderAssetPath, settings.cleanCharacterOutlineShaderName);
        if (settings.enableOfficialOverlay)
            EnsureMaterial(ref hiddenOutlineMaterial, settings.hiddenOutlineShaderAssetPath, settings.hiddenOutlineShaderName);
    }

    private void EnsureMaterial(ref Material mat, string shaderAssetPath, string shaderName)
    {
        if (mat != null)
            return;

        Shader shader = null;

#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(shaderAssetPath))
        {
            shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderAssetPath);
        }
#endif

        if (shader == null && !string.IsNullOrWhiteSpace(shaderName))
            shader = Shader.Find(shaderName);

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

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime ch = channels[i];
            if (ch == null || ch.settings == null)
                continue;
            EnsureRT(ref ch.maskRT, ref ch.maskHandle, ch.settings.characterMaskRTName, w, h, settings.filterMode);
            EnsureRT(ref ch.hiddenRT, ref ch.hiddenHandle, ch.settings.hiddenMaskRTName, w, h, settings.filterMode);
            string cleanName = string.IsNullOrWhiteSpace(ch.settings.cleanOutlineRTName) ? ("RT_MainCameraCleanCharacterOutline_" + ch.settings.channelName) : ch.settings.cleanOutlineRTName;
            EnsureRT(ref ch.cleanOutlineRT, ref ch.cleanOutlineHandle, cleanName, w, h, settings.filterMode);

            // V23: publish the exact runtime RT instances created by this Feature.
            // Canvas outline must bind these, not stale Resources search results with the same name.
            SkyPrisonRuntimeMaskRegistry_V1.PublishChannel(ch.settings.channelName, ch.maskRT, ch.hiddenRT);
        }
        EnsureRT(ref occluderMaskRT, ref occluderMaskHandle, settings.occluderMaskRTName, w, h, settings.filterMode);
        SkyPrisonRuntimeMaskRegistry_V1.PublishOccluderMask(occluderMaskRT);
    }

    private void EnsureRT(ref RenderTexture rt, ref RTHandle handle, string rtName, int w, int h, FilterMode filterMode)
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

    private void ConfigureUnitMaterials()
    {
        if (!settings.autoConfigureMaterialsInPlayMode)
        {
            settings.lastMaterialStatus = "SKIP auto material config disabled.";
            return;
        }
        if (!Application.isPlaying)
        {
            settings.lastMaterialStatus = "SKIP auto material config outside Play Mode.";
            return;
        }

        for (int c = 0; c < channels.Length; c++)
        {
            if (channels[c] != null)
                channels[c].renderers.Clear();
        }

        int checkedRenderers = 0;
        int matchedRenderers = 0;
        int configuredSlots = 0;
        int bodyClipTextureSlots = 0;
        int sharedBodyClipSlots = 0;
        Renderer[] all = Resources.FindObjectsOfTypeAll<Renderer>();
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || r.gameObject == null || !r.gameObject.activeInHierarchy)
                continue;
            checkedRenderers++;

            string path = GetPath(r.transform);
            ChannelSettings channel = ResolveChannelForPath(path);
            if (channel == null || !channel.enabled)
                continue;

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

            matchedRenderers++;
            ChannelRuntime runtimeChannel = ResolveRuntimeForChannel(channel);
            if (runtimeChannel != null && !runtimeChannel.renderers.Contains(r))
                runtimeChannel.renderers.Add(r);

            int id = r.GetInstanceID();
            bool stencilAlreadyConfigured = settings.configureOnlyOncePerRenderer && configuredRenderers.Contains(id);

            // r.materials is intentionally Play Mode only here: the runtime material instance is what the unit receiver also drives.
            Material[] mats = r.materials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null || mat.shader == null || mat.shader.name != settings.spineOcclusionShaderName)
                    continue;

                if (!stencilAlreadyConfigured)
                {
                    mat.SetFloat(CharacterStencilRefId, channel.stencilRef);
                    mat.SetFloat(CharacterStencilReadMaskId, settings.stencilReadMask);
                    mat.SetFloat(CharacterStencilWriteMaskId, settings.stencilWriteMask);
                    configuredSlots++;
                }

                // V6 rule: do NOT bind _OcclusionTex here.
                // HiddenRT is generated later by the render passes; binding is done by BodyClipApplyPass
                // after HiddenComposite and before the normal transparent Spine body pass.
                if (settings.bindNewOccluderMaskToSpineBody && settings.usePrecomputedHiddenMaskForBodyClip && runtimeChannel != null && runtimeChannel.hiddenRT != null)
                {
                    bodyClipTextureSlots++;
                    if (runtimeChannel.hiddenRT.name.StartsWith("RT_MainCameraHiddenMask", StringComparison.Ordinal))
                        sharedBodyClipSlots++;
                }

                if (settings.forceSpineMaskYSettings)
                {
                    mat.SetFloat(FlipMaskYId, 0f);
                    mat.SetFloat(SampleBothYId, 0f);
                }
            }
            configuredRenderers.Add(id);
        }
        settings.lastMaterialStatus = "OK material config. checked=" + checkedRenderers
            + ", matchedRenderers=" + matchedRenderers
            + ", stencilSlots=" + configuredSlots
            + ", bodyClipTextureSlots=" + bodyClipTextureSlots
            + ", sharedBodyClipSlots=" + sharedBodyClipSlots
            + ", bodyClipMode=PrecomputedHiddenMask_AppliedByRenderPass"
            + ", cachedRenderers=" + configuredRenderers.Count;
    }


    private ChannelRuntime ResolveRuntimeForChannel(ChannelSettings channel)
    {
        if (channel == null)
            return null;
        for (int i = 0; i < channels.Length; i++)
        {
            ChannelRuntime runtime = channels[i];
            if (runtime != null && ReferenceEquals(runtime.settings, channel))
                return runtime;
        }
        return null;
    }

    private ChannelSettings ResolveChannelForPath(string path)
    {
        ChannelSettings[] all = { settings.player, settings.enemy, settings.item, settings.ally };
        for (int i = 0; i < all.Length; i++)
        {
            ChannelSettings ch = all[i];
            if (ch == null || !ch.enabled)
                continue;
            if (ContainsAny(path, SplitKeywords(ch.pathKeywords)))
                return ch;
        }
        return null;
    }

    private void CollectOccluderRenderers()
    {
        occluderRenderers.Clear();
        int layer = LayerMask.NameToLayer(settings.occluderLayerName);
        if (layer < 0)
        {
            settings.lastOccluderStatus = "SKIP occluder layer not found: " + settings.occluderLayerName;
            return;
        }

        string[] rejects = SplitKeywords(settings.rejectPathKeywords);
        Renderer[] all = Resources.FindObjectsOfTypeAll<Renderer>();
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null) continue;
            GameObject go = r.gameObject;
            if (go == null) continue;
            if (go.layer != layer) continue;
            if (settings.requireRendererEnabled && !r.enabled) continue;
            if (settings.requireActiveInHierarchy && !go.activeInHierarchy) continue;
            string path = GetPath(r.transform);
            if (ContainsAny(path, rejects)) continue;
            occluderRenderers.Add(r);
        }
        settings.lastOccluderStatus = "COLLECT occluder renderers=" + occluderRenderers.Count + ", layer=" + settings.occluderLayerName;
    }

    private sealed class StencilClearPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly Func<Material> materialGetter;
        private sealed class PassData { public Material material; public Settings settings; public Camera camera; }
        public StencilClearPass(Settings settings, Func<Material> materialGetter) { this.settings = settings; this.materialGetter = materialGetter; }
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Material mat = materialGetter();
            if (mat == null) return;
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Official Clear Stencil", out PassData passData))
            {
                passData.material = mat;
                passData.settings = settings;
                passData.camera = cameraData.camera;
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    CoreUtils.DrawFullScreen(context.cmd, data.material);
                    data.settings.lastClearStatus = "OK clear stencil. camera=" + SafeName(data.camera);
                });
            }
        }
    }


    private sealed class FullAlphaMaskColorPass : ScriptableRenderPass
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
        }

        public FullAlphaMaskColorPass(Settings settings, ChannelRuntime channel)
        {
            this.settings = settings;
            this.channel = channel;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (channel == null || channel.settings == null || channel.maskHandle == null)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison V7 Full Alpha Color Mask " + channel.settings.channelName, out PassData passData))
            {
                passData.settings = settings;
                passData.channel = channel.settings;
                passData.camera = cameraData.camera;
                passData.target = channel.maskHandle;
                passData.renderers = channel.renderers.ToArray();

                TextureHandle targetTexture = renderGraph.ImportTexture(channel.maskHandle);
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
                            if (r == null || !r.enabled || r.gameObject == null || !r.gameObject.activeInHierarchy)
                                continue;

                            Material[] mats = r.materials;
                            bool rendererDrawn = false;
                            if (mats == null)
                                continue;

                            for (int sub = 0; sub < mats.Length; sub++)
                            {
                                Material mat = mats[sub];
                                if (mat == null || mat.shader == null || mat.shader.name != data.settings.spineOcclusionShaderName)
                                    continue;

                                // V25 pass index 1 = FullAlphaMaskOnly.
                                // This uses the real Spine material and atlas, but outputs white alpha mask only.
                                context.cmd.DrawRenderer(r, mat, sub, passIndex);
                                drawCalls++;
                                rendererDrawn = true;
                            }

                            if (rendererDrawn)
                                rendererCount++;
                        }
                    }

                    data.settings.lastCharacterStatus = "OK V7 full-alpha color mask " + data.channel.channelName
                        + " -> " + SafeName(data.target)
                        + ", renderers=" + rendererCount
                        + ", drawCalls=" + drawCalls
                        + ", passIndex=" + passIndex
                        + ", camera=" + SafeName(data.camera);
                });
            }
        }
    }

    private sealed class FullAlphaStencilPrePass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly ChannelRuntime channel;
        private sealed class PassData { public Settings settings; public ChannelSettings channel; public Camera camera; public Renderer[] renderers; }
        public FullAlphaStencilPrePass(Settings settings, ChannelRuntime channel)
        {
            this.settings = settings;
            this.channel = channel;
        }
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (channel == null || channel.settings == null || !channel.settings.enabled || channel.renderers.Count == 0)
                return;
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Official Full Alpha Stencil Prepass " + channel.settings.channelName, out PassData passData))
            {
                passData.settings = settings;
                passData.channel = channel.settings;
                passData.camera = cameraData.camera;
                passData.renderers = channel.renderers.ToArray();
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    int drawn = 0;
                    if (data.renderers != null)
                    {
                        for (int i = 0; i < data.renderers.Length; i++)
                        {
                            Renderer r = data.renderers[i];
                            if (r == null || !r.enabled || r.gameObject == null || !r.gameObject.activeInHierarchy)
                                continue;
                            // Use runtime material instances here because ConfigureUnitMaterials writes the channel stencil ref to r.materials.
                            // This pass is only meaningful in Play Mode; it must not use sharedMaterials for multi-channel stencil refs.
                            Material[] mats = r.materials;
                            if (mats == null || mats.Length == 0)
                                continue;
                            for (int sub = 0; sub < mats.Length; sub++)
                            {
                                Material mat = mats[sub];
                                if (mat == null || mat.shader == null || mat.shader.name != data.settings.spineOcclusionShaderName)
                                    continue;
                                // Pass 0 in SpineOcclusionComposite_V24 is FullAlphaStencilOnly:
                                // ColorMask 0, alpha-tested full character silhouette, writes only stencil.
                                context.cmd.DrawRenderer(r, mat, sub, 0);
                                drawn++;
                            }
                        }
                    }
                    data.settings.lastCharacterStatus = "OK pre-stencil " + data.channel.channelName + " renderers=" + (data.renderers == null ? 0 : data.renderers.Length) + ", drawCalls=" + drawn + ", camera=" + SafeName(data.camera);
                });
            }
        }
    }

    private sealed class StencilExtractPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly ChannelRuntime channel;
        private readonly Func<Material> materialGetter;
        private sealed class PassData { public Material material; public Settings settings; public ChannelSettings channel; public Camera camera; public RTHandle target; }
        public StencilExtractPass(Settings settings, ChannelRuntime channel, Func<Material> materialGetter) { this.settings = settings; this.channel = channel; this.materialGetter = materialGetter; }
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Material mat = materialGetter();
            if (mat == null || channel == null || channel.settings == null || channel.maskHandle == null) return;
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Official Stencil To Mask " + channel.settings.channelName, out PassData passData))
            {
                passData.material = mat;
                passData.settings = settings;
                passData.channel = channel.settings;
                passData.camera = cameraData.camera;
                passData.target = channel.maskHandle;
                TextureHandle targetTexture = renderGraph.ImportTexture(channel.maskHandle);
                builder.SetRenderAttachment(targetTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(false, true, Color.clear);
                    data.material.SetFloat(StencilRefId, data.channel.stencilRef);
                    data.material.SetFloat(StencilReadMaskId, data.settings.stencilReadMask);
                    CoreUtils.DrawFullScreen(context.cmd, data.material);
                    data.settings.lastCharacterStatus = "OK extracted " + data.channel.channelName + " stencil=" + data.channel.stencilRef + " -> " + SafeName(data.target) + ", camera=" + SafeName(data.camera);
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
        private sealed class PassData { public Material material; public Settings settings; public Camera camera; public RTHandle target; public Renderer[] renderers; }
        public OccluderMaskPass(Settings settings, Func<Material> materialGetter, Func<RTHandle> targetGetter, Func<Renderer[]> renderersGetter) { this.settings = settings; this.materialGetter = materialGetter; this.targetGetter = targetGetter; this.renderersGetter = renderersGetter; }
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Material mat = materialGetter();
            RTHandle target = targetGetter();
            Renderer[] renderers = renderersGetter();
            if (mat == null || target == null) return;
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Official Occluder Mask RT", out PassData passData))
            {
                passData.material = mat;
                passData.settings = settings;
                passData.camera = cameraData.camera;
                passData.target = target;
                passData.renderers = renderers;
                TextureHandle targetTexture = renderGraph.ImportTexture(target);
                builder.SetRenderAttachment(targetTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(false, true, Color.clear);
                    int drawCount = 0;
                    if (data.renderers != null)
                    {
                        for (int i = 0; i < data.renderers.Length; i++)
                        {
                            Renderer r = data.renderers[i];
                            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                            context.cmd.DrawRenderer(r, data.material, 0, 0);
                            drawCount++;
                        }
                    }
                    data.settings.lastOccluderStatus = "OK occluder mask: collected=" + (data.renderers == null ? 0 : data.renderers.Length) + ", drawn=" + drawCount + ", target=" + SafeName(data.target) + ", camera=" + SafeName(data.camera);
                });
            }
        }
    }



    private sealed class CleanCharacterOutlinePass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly ChannelRuntime channel;
        private readonly Func<Material> materialGetter;

        private sealed class PassData
        {
            public Material material;
            public Settings settings;
            public ChannelSettings channel;
            public Camera camera;
            public RTHandle target;
            public RenderTexture characterRT;
            public int width;
            public int height;
        }

        public CleanCharacterOutlinePass(Settings settings, ChannelRuntime channel, Func<Material> materialGetter)
        {
            this.settings = settings;
            this.channel = channel;
            this.materialGetter = materialGetter;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Material mat = materialGetter();
            if (mat == null || settings == null || channel == null || channel.settings == null || channel.maskHandle == null || channel.maskRT == null || channel.cleanOutlineHandle == null || channel.cleanOutlineRT == null)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Clean Character Outline Input " + channel.settings.channelName, out PassData passData))
            {
                passData.material = mat;
                passData.settings = settings;
                passData.channel = channel.settings;
                passData.camera = cameraData.camera;
                passData.target = channel.cleanOutlineHandle;
                passData.characterRT = channel.maskRT;
                passData.width = Mathf.Max(1, channel.maskRT.width);
                passData.height = Mathf.Max(1, channel.maskRT.height);

                TextureHandle characterTexture = renderGraph.ImportTexture(channel.maskHandle);
                TextureHandle targetTexture = renderGraph.ImportTexture(channel.cleanOutlineHandle);
                builder.UseTexture(characterTexture, AccessFlags.Read);
                builder.SetRenderAttachment(targetTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(false, true, Color.clear);
                    data.material.SetTexture(CharacterMaskTexId, data.characterRT);
                    data.material.SetVector(MaskTexelSizeId, new Vector4(1f / Mathf.Max(1, data.width), 1f / Mathf.Max(1, data.height), data.width, data.height));
                    data.material.SetFloat(ThresholdId, data.settings.threshold);
                    data.material.SetFloat(OutlineWidthPixelsId, Mathf.Max(1f, data.settings.outlineWidthPixels));
                    data.material.SetFloat(CleanOutlineFarSampleScaleId, Mathf.Max(1f, data.settings.cleanOutlineFarSampleScale));
                    data.material.SetFloat(CleanOutlineMinStableVotesId, Mathf.Clamp(data.settings.cleanOutlineMinStableVotes, 1, 8));
                    data.material.SetFloat(CleanOutlineNoiseThresholdId, Mathf.Clamp01(data.settings.cleanOutlineNoiseThreshold));
                    CoreUtils.DrawFullScreen(context.cmd, data.material);
                    data.settings.lastCharacterStatus = data.settings.lastCharacterStatus
                        + "\nCleanCharacterOutline " + data.channel.channelName
                        + " -> " + SafeName(data.target)
                        + ", source=" + SafeName(data.characterRT)
                        + ", camera=" + SafeName(data.camera);
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
            if (mat == null || channel == null || channel.settings == null || channel.maskHandle == null || channel.maskRT == null || channel.hiddenHandle == null || channel.hiddenRT == null || occluderHandle == null || occluderRT == null)
                return;
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Official Hidden Composite " + channel.settings.channelName, out PassData passData))
            {
                passData.material = mat;
                passData.settings = settings;
                passData.channel = channel.settings;
                passData.camera = cameraData.camera;
                passData.target = channel.hiddenHandle;
                passData.characterRT = channel.maskRT;
                passData.occluderRT = occluderRT;
                TextureHandle characterTexture = renderGraph.ImportTexture(channel.maskHandle);
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
                    CoreUtils.DrawFullScreen(context.cmd, data.material);
                    data.settings.lastMaterialStatus = data.settings.lastMaterialStatus + "\nHiddenComposite " + data.channel.channelName + " -> " + SafeName(data.target);
                });
            }
        }
    }


    private sealed class BodyClipApplyPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly ChannelRuntime channel;
        private sealed class PassData
        {
            public Settings settings;
            public ChannelSettings channel;
            public Renderer[] renderers;
            public RenderTexture hiddenRT;
            public RenderTexture characterMaskRT;
            public RenderTexture cleanOutlineRT;
        }

        public BodyClipApplyPass(Settings settings, ChannelRuntime channel)
        {
            this.settings = settings;
            this.channel = channel;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || channel == null || channel.settings == null || !channel.settings.enabled || channel.hiddenRT == null || channel.maskRT == null || channel.renderers.Count == 0)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison Official Apply Body Clip " + channel.settings.channelName, out PassData passData))
            {
                passData.settings = settings;
                passData.channel = channel.settings;
                passData.renderers = channel.renderers.ToArray();
                passData.hiddenRT = channel.hiddenRT;
                passData.characterMaskRT = channel.maskRT;
                passData.cleanOutlineRT = channel.cleanOutlineRT;
                // Attach the active color texture so RenderGraph keeps this pass in the correct place.
                // The pass does not draw; it only updates runtime material texture bindings immediately before transparents.
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    int rendererCount = 0;
                    int materialSlots = 0;
                    if (data.renderers != null)
                    {
                        for (int i = 0; i < data.renderers.Length; i++)
                        {
                            Renderer r = data.renderers[i];
                            if (r == null || !r.enabled || r.gameObject == null || !r.gameObject.activeInHierarchy)
                                continue;
                            Material[] mats = r.materials;
                            bool touchedRenderer = false;
                            for (int m = 0; m < mats.Length; m++)
                            {
                                Material mat = mats[m];
                                if (mat == null || mat.shader == null || mat.shader.name != data.settings.spineOcclusionShaderName)
                                    continue;
                                mat.SetTexture(OcclusionTexId, data.hiddenRT);
                                mat.SetTexture(CharacterMaskTexBodyId, data.characterMaskRT);
                                if (data.settings.enableCleanCharacterOutlineInput && data.cleanOutlineRT != null)
                                {
                                    mat.SetTexture(CleanCharacterOutlineTexBodyId, data.cleanOutlineRT);
                                    mat.SetFloat(UseCleanCharacterOutlineTexBodyId, 1f);
                                }
                                else
                                {
                                    mat.SetFloat(UseCleanCharacterOutlineTexBodyId, 0f);
                                }

                                // V20: draw the hidden outline inside the same Spine body shader pass.
                                // This avoids the RenderGraph fullscreen-overlay texture binding problem.
                                mat.SetColor(HiddenOutlineColorBodyId, data.channel.outlineColor);
                                mat.SetFloat(EnableHiddenOutlineBodyId, data.settings.enableMeshHiddenOutline ? 1f : 0f);
                                mat.SetFloat(HiddenOutlineWidthBodyId, Mathf.Max(1f, data.settings.outlineWidthPixels));
                                mat.SetFloat(HiddenOutlineAlphaBodyId, Mathf.Clamp01(data.settings.meshHiddenOutlineAlpha * data.channel.outlineColor.a));

                                if (data.settings.forceSpineMaskYSettings)
                                {
                                    mat.SetFloat(FlipMaskYId, 0f);
                                    mat.SetFloat(SampleBothYId, 0f);
                                }
                                materialSlots++;
                                touchedRenderer = true;
                            }
                            if (touchedRenderer)
                                rendererCount++;
                        }
                    }
                    data.settings.lastMaterialStatus = data.settings.lastMaterialStatus
                        + "\nBodyClipApply/MeshOutline " + data.channel.channelName
                        + " -> hidden=" + SafeName(data.hiddenRT)
                        + ", character=" + SafeName(data.characterMaskRT)
                        + ", cleanOutline=" + SafeName(data.cleanOutlineRT)
                        + ", renderers=" + rendererCount
                        + ", slots=" + materialSlots;
                });
            }
        }
    }

    private sealed class HiddenOutlinePass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly ChannelRuntime channel;
        private readonly Func<Material> materialGetter;

        private sealed class PassData
        {
            public Material material;
            public Settings settings;
            public ChannelSettings channel;
            public Camera camera;
            public RenderTexture hiddenRT;
            public RenderTexture cleanOutlineRT;
            public int width;
            public int height;
        }

        public HiddenOutlinePass(
            Settings settings,
            ChannelRuntime channel,
            Func<Material> materialGetter,
            Func<RTHandle> occluderHandleGetter,
            Func<RenderTexture> occluderRTGetter)
        {
            this.settings = settings;
            this.channel = channel;
            this.materialGetter = materialGetter;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            Material mat = materialGetter();
            if (mat == null
                || channel == null
                || channel.settings == null
                || !channel.settings.enabled
                || channel.hiddenHandle == null
                || channel.hiddenRT == null
                || channel.cleanOutlineHandle == null
                || channel.cleanOutlineRT == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SkyPrison V25 Clean Final Hidden Outline " + channel.settings.channelName, out PassData passData))
            {
                passData.material = mat;
                passData.settings = settings;
                passData.channel = channel.settings;
                passData.camera = cameraData.camera;
                passData.hiddenRT = channel.hiddenRT;
                passData.cleanOutlineRT = channel.cleanOutlineRT;
                passData.width = Mathf.Max(1, channel.hiddenRT.width);
                passData.height = Mathf.Max(1, channel.hiddenRT.height);

                TextureHandle hiddenTexture = renderGraph.ImportTexture(channel.hiddenHandle);
                TextureHandle cleanTexture = renderGraph.ImportTexture(channel.cleanOutlineHandle);
                builder.UseTexture(hiddenTexture, AccessFlags.Read);
                builder.UseTexture(cleanTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    float w = Mathf.Max(1, data.width);
                    float h = Mathf.Max(1, data.height);
                    data.material.SetTexture("_CleanOutlineTex", data.cleanOutlineRT);
                    data.material.SetTexture("_HiddenMaskTex", data.hiddenRT);
                    data.material.SetVector(MaskTexelSizeId, new Vector4(1f / w, 1f / h, w, h));
                    data.material.SetFloat(DebugModeId, (float)data.settings.outlineDebugMode);
                    data.material.SetFloat(DebugFillAlphaId, Mathf.Clamp01(data.settings.debugFillAlpha));
                    data.material.SetColor(OutlineColorId, data.channel.outlineColor);
                    data.material.SetFloat(OutlineWidthPixelsId, Mathf.Max(1, data.settings.outlineWidthPixels));
                    data.material.SetFloat(ThresholdId, data.settings.threshold);

                    CoreUtils.DrawFullScreen(context.cmd, data.material, null, 0);

                    data.settings.lastOutlineStatus =
                        "OK V25 clean final overlay "
                        + data.channel.channelName
                        + ", clean=" + SafeName(data.cleanOutlineRT)
                        + ", hidden=" + SafeName(data.hiddenRT)
                        + ", camera=" + SafeName(data.camera);
                });
            }
        }
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
    [MenuItem("Tools/Sky Prison/Rendering/Install Main Camera Stencil Hidden Outline Feature V23")]
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
            if (obj is SkyPrisonMainCameraStencilHiddenOutlineFeatureV23)
            {
                Selection.activeObject = target;
                EditorUtility.DisplayDialog("Sky Prison", "Mesh Hidden Outline V23 is already installed.", "OK");
                return;
            }
        }

        SkyPrisonMainCameraStencilHiddenOutlineFeatureV23 feature = CreateInstance<SkyPrisonMainCameraStencilHiddenOutlineFeatureV23>();
        feature.name = "Sky Prison Main Camera Stencil Hidden Outline Feature V23";
        AssetDatabase.AddObjectToAsset(feature, target);
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(target));
        featuresProp.arraySize++;
        featuresProp.GetArrayElementAtIndex(featuresProp.arraySize - 1).objectReferenceValue = feature;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();
        Selection.activeObject = target;
        EditorUtility.DisplayDialog("Sky Prison", "Installed Mesh Hidden Outline V23. HiddenMask is generated first, then bound to Spine body by BodyClipApplyPass before normal transparent rendering.", "OK");
    }
#endif
}
