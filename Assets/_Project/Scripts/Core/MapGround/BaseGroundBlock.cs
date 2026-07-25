using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 地图基础地面数据块。
/// GroundBlock 覆盖完整 MapBounds 数据域；真实地面区域由 GroundShapeMask 裁切。
/// SurfaceMaterialMap 用于记录每个位置是什么地表材质。
/// </summary>
[System.Serializable]
public struct SurfacePaintStamp
{
    public int paletteIndex;
    public Vector3 worldPosition;
    public float radius;
    public float hardness;
    public float strength;
    public int seed;
}

[ExecuteAlways]
public class BaseGroundBlock : MonoBehaviour
{
    // 地面刷拖动/松手刷新期间不要自动修改 TextureImporter。
    // SaveAndReimport 会触发资源重新导入，看起来就像 Unity 每画一笔都在编译。
    public static bool SuppressAutomaticTextureImporterChanges = false;

    [Header("Map Bounds Binding")]
    public Vector3 mapBoundsCenter = Vector3.zero;
    public Vector3 mapBoundsSize = new Vector3(64f, 6f, 64f);

    [Header("Ground Visual Stability")]
    [Tooltip("把 GroundVisual 从逻辑地面高度轻微抬起，避免和其它地面/旧平面/Scene 网格共面造成 z-fighting 闪烁。")]
    public bool enforceGroundVisualYOffset = true;
    [Tooltip("GroundVisual 相对地面逻辑高度的显示偏移。只影响视觉，不影响角色落地/查询。")]
    [Range(0f, 0.2f)] public float groundVisualYOffset = 0.035f;
    [Tooltip("自动关闭 GroundBlock_01 下不属于 GroundVisual 的额外 Renderer，避免旧地面面片和新 GroundVisual 叠在一起。")]
    public bool disableExtraGroundRenderersInBlock = true;

    [Header("Ground Shape")]
    public Texture2D groundShapeMask;
    public float defaultGroundHeight = 0f;
    [Range(0f, 1f)] public float groundMaskThreshold = 0.5f;

    [Header("Surface Material")]
    public GroundSurfaceType defaultSurfaceType = GroundSurfaceType.Concrete;
    public GroundSurfaceMaterialDefinition defaultSurfaceMaterial;
    public Texture2D surfaceMaterialIndexMap;
    public Texture2D surfaceMaterialPreviewTexture;
    public List<GroundSurfaceMaterialDefinition> surfaceMaterialPalette = new List<GroundSurfaceMaterialDefinition>();
    [Tooltip("真正的软边材质过渡。每个地表材质拥有独立权重通道，笔刷硬度会写入权重，而不是只写一个硬 ID。")]
    public bool enableSurfaceWeightBlend = true;
    [Tooltip("地表材质权重图。每张 RGBA 存 4 个材质权重。逻辑查询仍使用 SurfaceMaterialIndexMap 的主材质。")]
    public List<Texture2D> surfaceMaterialWeightMaps = new List<Texture2D>();
    public bool previewSurfaceMaterialOnGroundVisual = true;

    [Header("Surface Paint Data Resolution")]
    [Tooltip("地表控制图分辨率。影响圆形刷边缘、软边权重、材质覆盖精度。旧 512 图会在绘制时自动升采样。")]
    [Range(256, 4096)] public int groundDataTextureResolution = 1024;
    [Tooltip("绘制地表时自动把旧的低分辨率 Shape/Index/Weight/Preview 控制图升采样到 groundDataTextureResolution。")]
    public bool autoUpgradeGroundDataTextures = true;

    [Header("Surface Paint Stamp Randomization")]
    [Tooltip("记录画笔落点，用于后续地表细节随机场。只作为采样种子，不直接改变逻辑材质 ID。")]
    public bool enableSurfacePaintStampRandomization = true;
    [Tooltip("最多保留多少个地表画笔 Stamp。过多会影响烘焙速度。")]
    [Range(0, 4096)] public int maxSurfacePaintStamps = 512;
    [Tooltip("Stamp 影响半径倍率。")]
    [Range(0.2f, 4f)] public float surfacePaintStampInfluenceScale = 1.15f;
    [Tooltip("Stamp 随机强度。")]
    [Range(0f, 1f)] public float surfacePaintStampRandomness = 1f;
    [SerializeField] public List<SurfacePaintStamp> surfacePaintStamps = new List<SurfacePaintStamp>();

    [Header("Ground Shadow Receiver")]
    [Tooltip("自动确保 GroundVisual Renderer 开启 Receive Shadows。Game 视图投影依赖这个开关。")]
    public bool enforceGroundReceiveShadows = true;
    [Tooltip("GroundVisual 接收实时阴影的强度。0 = 不收影，1 = 完整主光阴影。")]
    [Range(0f, 1f)] public float groundReceiveShadowStrength = 1f;
    [Tooltip("GroundVisual 自身通常不需要投射阴影，只需要接收其它物体投影。默认关闭自身 Cast Shadows，避免地板向其它物体投影。")]
    public bool disableGroundVisualCastShadows = true;


    [Header("URP Lit Shadow Safe Output")]
    [Tooltip("使用真正的 URP/Lit 材质承载地面显示。它会把 ShapeMask/地表材质结果烘成一张 BaseMap，从而完全走 URP/Lit 的接收阴影链路。用于解决自定义 Shader 不接收投影的问题。")]
    public bool useUrpLitShadowSafeOutput = true;
    [Tooltip("烘焙给 URP/Lit 使用的地面 BaseMap 分辨率。越高越清晰，越低越省。")]
    [Range(128, 4096)] public int litBakedTextureResolution = 1024;
    [Tooltip("无地面区域的 Alpha。保持 0 可以配合 AlphaClip 裁掉无地面区域。")]
    [Range(0f, 1f)] public float litNoGroundAlpha = 0f;
    [Tooltip("URP/Lit AlphaClip Cutoff。通常和 Ground Mask Threshold 保持一致或略高。")]
    [Range(0f, 1f)] public float litAlphaCutoff = 0.5f;
    [Tooltip("编辑器下自动把被地表材质引用的 Texture2D 开启 Read/Write，以便 CPU 烘焙 Lit BaseMap。否则会退回 baseColor，看起来像贴图没显示。")]
    public bool autoMakeSurfaceTexturesReadableForLitBake = false;
    [Tooltip("编辑器下自动把被地表材质引用的 Texture2D 开启 MipMaps，用于降低大面积地面移动时的高频闪烁。")]
    public bool autoEnableMipMapsForLitBakeTextures = false;
    [Tooltip("使用 GroundShapeMask 裁切 URP/Lit 地面。关闭后会显示完整矩形地面，只用于排查投影。")]
    public bool litUseGroundShapeAlpha = true;
    [Tooltip("贴图视觉缩小时，自动提高 URP/Lit 烘焙贴图分辨率，避免细节被 1024 烘焙图压糊。例：Visual Scale=0.25 时，1024 会自动提升到 4096。")]
    public bool autoBoostLitBakeResolutionByTextureScale = true;
    [Tooltip("自动提高烘焙分辨率时的上限。质量优先可设 4096；机器吃紧可设 2048。")]
    [Range(512, 4096)] public int maxAutoLitBakedTextureResolution = 4096;

    [Header("Grass Wave Overlay")]
    [Tooltip("草浪作为透明 Overlay 叠在 URP/Lit 地面上。地面主体仍使用官方 URP/Lit 接收投影，避免自定义草浪 Shader 吃掉投影。")]
    public bool enableGrassWaveShader = false;
    [Tooltip("草浪视觉强度。Overlay 只影响草地区域的轻微明暗/颜色起伏，不扭曲地面 UV。")]
    [Range(0f, 0.05f)] public float grassWaveStrength = 0.019f;
    [Tooltip("草浪移动速度。")]
    [Range(0f, 8f)] public float grassWaveSpeed = 1.45f;
    [Tooltip("草浪空间频率。越高波纹越密。")]
    [Range(0.1f, 16f)] public float grassWaveFrequency = 1.05f;
    [Tooltip("草浪方向。X/Z 平面方向，通常让它跟主风向一致。")]
    public Vector2 grassWaveDirection = new Vector2(1f, 0.35f);
    [Tooltip("草浪条带密度倍率。越低越稀疏，避免一排一排的密线。")]
    [Range(0.2f, 3f)] public float grassWaveLineDensity = 0.38f;
    [Tooltip("草浪形状扰乱强度。越高越不规则，能打散整齐直线。")]
    [Range(0f, 2f)] public float grassWaveIrregularity = 1.45f;
    [Tooltip("草浪随机噪声强度，用来避免规则正弦波。")]
    [Range(0f, 1f)] public float grassWaveNoiseStrength = 0.56f;
    [Tooltip("草浪带来的轻微亮部变化。")]
    [Range(0f, 0.35f)] public float grassWaveColorStrength = 0.125f;
    [Tooltip("草浪暗部压低强度。")]
    [Range(0f, 0.35f)] public float grassWaveDarkenStrength = 0.070f;
    [Tooltip("草地边界遮罩软化。ID 图建议保持接近 0，避免泥土被草浪影响。")]
    [Range(0.0001f, 0.03f)] public float grassWaveMaskSoftness = 0.0001f;
    [Tooltip("编辑模式下也推进草浪时间，方便在 Scene 视图里确认效果。")]
    public bool previewGrassWaveInEditMode = true;
    [Tooltip("调试用：强制下次刷新时重新烘焙 Lit BaseMap。")]
    public bool forceRebakeLitGroundTexture = false;
    [SerializeField, HideInInspector] private Material runtimeLitGroundMaterial;
    [SerializeField, HideInInspector] private Material runtimeGrassWaveOverlayMaterial;
    [SerializeField, HideInInspector] private Texture2D runtimeLitGroundBaseMap;
    private int runtimeLitBakeHash = 0;

    [Header("Ground Bake Workflow")]
    [Tooltip("编辑刷地时使用内存预览贴图，保持画布式即时反馈；正式运行使用 Runtime Baked Ground Texture。")]
    public bool useEditorPreviewTexture = true;
    [Tooltip("当前 ShapeMask / SurfaceMaterialMap / 智能合成参数是否已经修改，运行贴图是否需要重新烘焙。")]
    public bool needsRuntimeBake = true;
    [Tooltip("进入 Play Mode 前如果发现运行贴图过期，自动烘焙一次。")]
    public bool autoBakeBeforePlay = true;
    [Tooltip("编辑器内按 Play 时优先复用当前内存预览贴图，不立刻保存/重烘焙运行贴图资产。这样刷地后测试手感更快；正式资产仍可通过手动 Bake 或 Build 前流程生成。")]
    public bool fastEditorPlayPreviewWithoutAssetBake = true;
    [Tooltip("正式运行使用的烘焙地面贴图资产。Game/Play 优先使用它，以获得稳定 URP/Lit 投影。")]
    public Texture2D runtimeBakedGroundTexture;
    [Tooltip("运行烘焙贴图保存目录。建议放在项目数据目录，不放 Editor 目录。")]
    public string runtimeBakeAssetFolder = "Assets/_Project/Data/Maps/GroundBakes";

    [Header("Surface Texture Stability")]
    [Tooltip("降低斜视角/移动时的高频摩尔纹。1 最稳定，0 不处理。")]
    [Range(0f, 1f)] public float textureAntiShimmer = 0.95f;
    [Tooltip("越小越早淡化高频纹理。地面出现闪烁时优先降低这个值。")]
    [Range(0.0005f, 0.08f)] public float detailFadeStart = 0.0035f;
    [Tooltip("越小越强力压制高频纹理。建议保持大于 Fade Start。")]
    [Range(0.001f, 0.12f)] public float detailFadeEnd = 0.018f;
    [Tooltip("旧版细节强度参数，保留兼容。现在正常显示主要由 Surface Texture Weight / Tint Strength 控制。")]
    [Range(0f, 1f)] public float surfaceTextureStrength = 0.35f;

    [Header("Surface Texture Display")]
    [Tooltip("有 baseTexture 时，贴图参与最终显示的权重。1 = 贴图主导；0 = 颜色主导。")]
    [Range(0f, 1f)] public float surfaceTextureWeight = 1.0f;
    [Tooltip("有 baseTexture 时，baseColor 对贴图的染色强度。建议 0.08~0.2，避免颜色压过图像。")]
    [Range(0f, 1f)] public float surfaceColorTintStrength = 0.15f;
    [Tooltip("没有 baseTexture 时，使用 baseColor 的强度。通常保持 1。")]
    [Range(0f, 1f)] public float fallbackColorStrength = 1.0f;

    [Header("Surface Texture Sampling")]
    [Tooltip("强制采样更低频 mip。数值越高越稳定但越糊。建议 2.5~5。")]
    [Range(0f, 8f)] public float surfaceMipBias = 3.0f;
    [Tooltip("不要继承 GroundVisual 材质自身的贴图 Tiling，而是按世界尺寸稳定计算地表纹理平铺。这样能避免贴图被意外拉伸。")]
    public bool useStableSurfaceTextureWorldTiling = true;
    [Tooltip("全局兜底：一张地表纹理覆盖多少世界单位。材质资源本身填写了 textureWorldSize 时，优先使用材质自己的数值。数值越大，纹理越大。")]
    [Range(0.25f, 32f)] public float surfaceTextureWorldSize = 4f;
    [Tooltip("额外整体平铺倍率。1 为正常；2 表示图案再缩小一半；4 表示图案缩到四分之一。")]
    [Range(0.1f, 16f)] public float surfaceTextureTilingMultiplier = 1f;
    [Tooltip("地表贴图整体视觉比例。0.5 = 贴图缩小一半；0.25 = 贴图缩小到四分之一。只影响视觉采样，不改变地块尺寸、碰撞、ShapeMask 或 SurfaceMaterialMap。")]
    [Range(0.125f, 2f)] public float surfaceTextureVisualScale = 0.25f;
    [Tooltip("启用地表贴图反重复采样。它不会改变地表 ID，只在烘焙颜色时给贴图 UV 做低频随机偏移混合，用来打散大面积平铺感。")]
    public bool enableSurfaceTextureAntiRepeat = true;
    [Tooltip("反重复采样强度。0 = 原始平铺；1 = 完全使用随机偏移混合。沥青/水泥建议 0.45~0.75。")]
    [Range(0f, 1f)] public float surfaceTextureAntiRepeatStrength = 0.62f;
    [Tooltip("反重复随机场的世界尺寸。数值越大，变化越慢；太小会变脏，太大看不出效果。")]
    [Range(2f, 64f)] public float surfaceTextureAntiRepeatWorldSize = 12f;
    [Tooltip("每个随机场格子对贴图 UV 的最大偏移量。0.5 表示最多偏移半张贴图。")]
    [Range(0f, 1f)] public float surfaceTextureAntiRepeatUvOffset = 0.72f;
    [Tooltip("反重复随机场带来的微弱明暗差。用于进一步破除重复，但过高会变成脏斑。")]
    [Range(0f, 0.2f)] public float surfaceTextureAntiRepeatToneJitter = 0.045f;
    public FilterMode runtimeSurfaceTextureFilter = FilterMode.Trilinear;
    [Range(0, 16)] public int runtimeSurfaceTextureAniso = 8;

    [SerializeField, HideInInspector] private float cachedSurfaceTextureWorldSize = -1f;
    [SerializeField, HideInInspector] private float cachedSurfaceTextureTilingMultiplier = -1f;
    [SerializeField, HideInInspector] private bool cachedUseStableSurfaceTextureWorldTiling = false;
    [SerializeField, HideInInspector] private float cachedSurfaceTextureVisualScale = -1f;
    [SerializeField, HideInInspector] private bool cachedEnableSurfaceTextureAntiRepeat = false;
    [SerializeField, HideInInspector] private float cachedSurfaceTextureAntiRepeatStrength = -1f;
    [SerializeField, HideInInspector] private float cachedSurfaceTextureAntiRepeatWorldSize = -1f;
    [SerializeField, HideInInspector] private float cachedSurfaceTextureAntiRepeatUvOffset = -1f;
    [SerializeField, HideInInspector] private float cachedSurfaceTextureAntiRepeatToneJitter = -1f;


    [Header("Smart Surface Composition")]
    [Tooltip("在烘焙给 URP/Lit 的地面贴图时，自动对不同地表材质边界做过渡、噪声打散和污渍叠加。只影响视觉，不改变 SurfaceMaterialMap 的逻辑数据。")]
    public bool enableSmartSurfaceComposition = true;
    [Tooltip("材质边界混合半径，单位是烘焙贴图像素。0 = 硬边。注意：当前企业默认不做邻域模糊，只用于后续权重图/调试。")]
    [Range(0, 24)] public int smartEdgeBlendPixels = 4;
    [Tooltip("是否启用旧版邻域材质模糊。默认关闭；它会把相邻材质颜色互相平均，容易产生油污状脏边。企业默认应使用硬材质 ID + 贴花/权重图，而不是连续模糊。")]
    public bool enableMaterialNeighborBlur = false;
    [Tooltip("旧版邻域模糊强度。只有启用邻域模糊时才生效。")]
    [Range(0f, 1f)] public float materialNeighborBlurStrength = 0.18f;
    [Tooltip("边缘混合时的噪声打散强度。越高边缘越不规则。")]
    [Range(0f, 1f)] public float smartEdgeNoiseStrength = 0.30f;
    [Tooltip("边缘噪声尺度。越大噪声块越大。")]
    [Range(0.5f, 64f)] public float smartEdgeNoiseScale = 16f;
    [Tooltip("在材质边界附近叠加轻微暗污，使拼接不那么机械。")]
    [Range(0f, 1f)] public float smartEdgeDirtStrength = 0.0f;
    [Tooltip("整体地表噪声变化强度，用来打散大面积平铺重复感。")]
    [Range(0f, 1f)] public float smartSurfaceVariationStrength = 0.10f;
    [Tooltip("整体地表噪声变化尺度。越大变化越慢。")]
    [Range(0.5f, 96f)] public float smartSurfaceVariationScale = 28f;
    [Tooltip("合成叠加层强度。用于轻微脏污、磨损和色差。")]
    [Range(0f, 1f)] public float smartOverlayStrength = 0.035f;
    [Tooltip("合成叠加层颜色。默认偏暗灰，用来压出工业地表的磨损感。")]
    public Color smartOverlayColor = new Color(0.18f, 0.17f, 0.15f, 1f);
    [Tooltip("合成叠加噪声尺度。越大污渍块越大。")]
    [Range(0.5f, 128f)] public float smartOverlayNoiseScale = 36f;

    [Header("Enterprise Edge Bake")]
    [Tooltip("正式 Lit 烘焙时保持地面存在性为硬 Alpha。软边只用于颜色/材质融合，不把半透明边缘写进运行贴图，避免黑边和角色站在半透明地面上。")]
    public bool forceHardRuntimeGroundAlpha = true;
    [Tooltip("给透明/无地面像素向外扩张最近的地面颜色。这样 AlphaClip / Mip / 双线性采样时不会从透明区采到黑色或默认色，能消掉软边轮廓。")]
    public bool enableEdgeColorDilation = true;
    [Tooltip("颜色扩张半径，单位是烘焙贴图像素。建议 8~16。")]
    [Range(0, 32)] public int edgeColorDilationPixels = 12;
    [Tooltip("ShapeMask 边缘内部的轻微脏污/压暗。它只影响有地面的内部边缘，不改变 Alpha。")]
    [Range(0f, 1f)] public float shapeInnerEdgeDirtStrength = 0.0f;

    [Header("Generated Children")]
    public Transform groundVisualRoot;
    public Transform groundColliderRoot;
    public Transform groundDebugRoot;

    private void Awake()
    {
        RefreshGroundVisualRuntime();
    }

    private void OnEnable()
    {
        RefreshGroundVisualRuntime();
    }

    private void Start()
    {
        RefreshGroundVisualRuntime();
    }

    /// <summary>
    /// 让 Game 视图 / Play 模式也能看到编辑器刷出的地表结果。
    /// 注意：编辑器 Scene Overlay 只存在于 SceneView，真正进入 Game 的必须是 GroundVisual Renderer。
    /// </summary>
    public void RefreshGroundVisualRuntime()
    {
        StabilizeGroundVisualTransformAndRenderers();
        EnsureGroundVisualRenderersEnabled();

        if (useUrpLitShadowSafeOutput)
        {
            ApplyShadowSafeLitGroundToGroundVisual();
            return;
        }

        if (previewSurfaceMaterialOnGroundVisual && surfaceMaterialIndexMap != null && surfaceMaterialPalette != null && surfaceMaterialPalette.Count > 0)
            ApplySurfaceMaterialsToGroundVisual();
        else
            ApplyNormalDisplayToGroundVisual();
    }

    private void StabilizeGroundVisualTransformAndRenderers()
    {
        if (groundVisualRoot != null && enforceGroundVisualYOffset)
        {
            Vector3 local = groundVisualRoot.localPosition;
            local.y = defaultGroundHeight + Mathf.Max(0f, groundVisualYOffset);
            groundVisualRoot.localPosition = local;
        }

        if (!disableExtraGroundRenderersInBlock || groundVisualRoot == null)
            return;

        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in allRenderers)
        {
            if (r == null)
                continue;

            Transform rt = r.transform;
            bool isGroundVisual = rt == groundVisualRoot || rt.IsChildOf(groundVisualRoot);
            if (isGroundVisual)
                continue;

            // 只处理 GroundBlock 内疑似旧地面显示的 Renderer。Structure / Prop 不应该挂在 GroundBlock_01 下。
            string n = rt.name.ToLowerInvariant();
            if (n.Contains("ground") || n.Contains("visual") || n.Contains("plane") || n.Contains("mesh"))
                r.enabled = false;
        }
    }

    private void EnsureGroundVisualRenderersEnabled()
    {
        if (groundVisualRoot == null)
            return;

        Renderer[] renderers = groundVisualRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r != null)
            {
                r.enabled = true;
                ConfigureGroundVisualRenderer(r);
            }
        }
    }

    private void ConfigureGroundVisualRenderer(Renderer renderer)
    {
        if (renderer == null)
            return;

        if (enforceGroundReceiveShadows)
            renderer.receiveShadows = true;

        if (disableGroundVisualCastShadows)
            renderer.shadowCastingMode = ShadowCastingMode.Off;

        // URP/Lit compatible shadow receiving state.
        // When a material has previously used another shader or inspector, these hidden render-state
        // properties/keywords can remain in a bad state and silently disable shadow receiving.
        Material[] materials = renderer.sharedMaterials;
        if (materials == null)
            return;

        foreach (Material material in materials)
        {
            if (material == null || material.shader == null)
                continue;

            if (material.shader.name != "SkyPrison/Map/BaseGroundBlockMasked")
                continue;

            material.SetFloat("_Surface", 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_ReceiveShadows", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_AlphaToMask", 1f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetFloat("_Cutoff", Mathf.Clamp01(groundMaskThreshold));

            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.DisableKeyword("_RECEIVE_SHADOWS_OFF");

            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }
    }

    public Bounds WorldBounds
    {
        get
        {
            Vector3 size = mapBoundsSize;
            size.x = Mathf.Max(0.01f, Mathf.Abs(size.x));
            size.y = Mathf.Max(0.01f, Mathf.Abs(size.y));
            size.z = Mathf.Max(0.01f, Mathf.Abs(size.z));
            return new Bounds(mapBoundsCenter, size);
        }
    }

    public bool TryWorldToUV(Vector3 worldPosition, out Vector2 uv)
    {
        Bounds bounds = WorldBounds;
        Vector3 min = bounds.min;
        Vector3 size = bounds.size;

        uv = Vector2.zero;
        if (size.x <= Mathf.Epsilon || size.z <= Mathf.Epsilon)
            return false;

        uv.x = Mathf.InverseLerp(min.x, min.x + size.x, worldPosition.x);
        uv.y = Mathf.InverseLerp(min.z, min.z + size.z, worldPosition.z);
        return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
    }

    public float SampleShapeMaskWorld(Vector3 worldPosition)
    {
        if (!TryWorldToUV(worldPosition, out Vector2 uv))
            return 0f;

        if (groundShapeMask == null)
            return 1f;

        return groundShapeMask.GetPixelBilinear(uv.x, uv.y).a;
    }

    public bool HasGroundAtWorld(Vector3 worldPosition)
    {
        return SampleShapeMaskWorld(worldPosition) >= groundMaskThreshold;
    }

    /// <summary>
    /// 返回相对 GroundBlock 基准面的地面高度偏移。
    /// 兼容旧调用：需要世界 Y 时请使用 GetGroundWorldYAtWorld。
    /// </summary>
    public float GetGroundYAtWorld(Vector3 worldPosition)
    {
        return defaultGroundHeight;
    }

    /// <summary>
    /// 返回世界坐标下的地面 Y。
    /// 当前第一版只有默认高度；后面接 GroundHeightMap 时会在这里叠加高度图。
    /// </summary>
    public float GetGroundWorldYAtWorld(Vector3 worldPosition)
    {
        return mapBoundsCenter.y + defaultGroundHeight;
    }

    public GroundSurfaceMaterialDefinition GetSurfaceMaterialAtWorld(Vector3 worldPosition)
    {
        if (!HasGroundAtWorld(worldPosition))
            return null;

        if (!TryWorldToUV(worldPosition, out Vector2 uv))
            return null;

        if (surfaceMaterialIndexMap == null || surfaceMaterialPalette == null || surfaceMaterialPalette.Count == 0)
            return defaultSurfaceMaterial;

        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (surfaceMaterialIndexMap.width - 1)), 0, surfaceMaterialIndexMap.width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (surfaceMaterialIndexMap.height - 1)), 0, surfaceMaterialIndexMap.height - 1);
        Color c = surfaceMaterialIndexMap.GetPixel(x, y);
        int index = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);

        if (index >= 0 && index < surfaceMaterialPalette.Count)
        {
            GroundSurfaceMaterialDefinition material = surfaceMaterialPalette[index];
            if (material != null)
                return material;
        }

        return defaultSurfaceMaterial;
    }

    public GroundSurfaceType GetSurfaceTypeAtWorld(Vector3 worldPosition)
    {
        GroundSurfaceMaterialDefinition material = GetSurfaceMaterialAtWorld(worldPosition);
        if (material != null)
            return material.surfaceType;

        return defaultSurfaceType;
    }

    public bool IsFallDeathAreaAtWorld(Vector3 worldPosition)
    {
        return !HasGroundAtWorld(worldPosition);
    }

    public int RegisterSurfaceMaterial(GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return 0;

        if (surfaceMaterialPalette == null)
            surfaceMaterialPalette = new List<GroundSurfaceMaterialDefinition>();

        int existing = surfaceMaterialPalette.IndexOf(material);
        if (existing >= 0)
            return Mathf.Clamp(existing, 0, 255);

        if (surfaceMaterialPalette.Count >= 256)
        {
            Debug.LogWarning($"[BaseGroundBlock] Surface material palette is full on {name}. Max 256 materials per block.", this);
            return 0;
        }

        surfaceMaterialPalette.Add(material);
        return surfaceMaterialPalette.Count - 1;
    }


    public void AddSurfacePaintStamp(int paletteIndex, Vector3 worldPosition, float radius, float hardness, float strength = 1f)
    {
        GroundSurfaceMaterialDefinition material = null;
        if (surfaceMaterialPalette != null && paletteIndex >= 0 && paletteIndex < surfaceMaterialPalette.Count)
            material = surfaceMaterialPalette[paletteIndex];

        AddSurfacePaintStamp(material, worldPosition, radius, hardness, strength);
    }

    public void AddSurfacePaintStamp(GroundSurfaceMaterialDefinition material, Vector3 worldPosition, float radius, float hardness, float strength = 1f)
    {
        if (!enableSurfacePaintStampRandomization)
            return;

        if (surfacePaintStamps == null)
            surfacePaintStamps = new List<SurfacePaintStamp>();

        int paletteIndex = RegisterSurfaceMaterial(material);
        SurfacePaintStamp stamp = new SurfacePaintStamp
        {
            paletteIndex = Mathf.Clamp(paletteIndex, 0, 255),
            worldPosition = worldPosition,
            radius = Mathf.Max(0.01f, radius),
            hardness = Mathf.Clamp01(hardness),
            strength = Mathf.Clamp01(strength),
            seed = unchecked(worldPosition.GetHashCode() ^ (paletteIndex * 73856093) ^ (surfacePaintStamps.Count * 19349663))
        };

        surfacePaintStamps.Add(stamp);

        int maxCount = Mathf.Max(0, maxSurfacePaintStamps);
        if (maxCount > 0 && surfacePaintStamps.Count > maxCount)
            surfacePaintStamps.RemoveRange(0, surfacePaintStamps.Count - maxCount);

        needsRuntimeBake = true;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorUtility.SetDirty(this);
#endif
    }

    public void ApplyShadowSafeLitGroundToGroundVisual()
    {
        ApplyGroundShapeMaskToGroundVisual();

        if (groundVisualRoot == null)
            return;

        Material litMaterial = GetOrCreateRuntimeLitGroundMaterial();
        if (litMaterial == null)
            return;

        RepairEditorPreviewGroundTextureIfLooksCorrupted();

        Texture2D displayTexture = GetLitGroundTextureForCurrentContext();
        if (displayTexture != null)
        {
            litMaterial.SetTexture("_BaseMap", displayTexture);
            litMaterial.SetTexture("_MainTex", displayTexture);
        }

        litMaterial.SetColor("_BaseColor", Color.white);
        litMaterial.SetFloat("_Surface", 0f);
        litMaterial.SetFloat("_Blend", 0f);
        litMaterial.SetFloat("_AlphaClip", litUseGroundShapeAlpha ? 1f : 0f);
        litMaterial.SetFloat("_Cutoff", Mathf.Clamp01(litAlphaCutoff));
        litMaterial.SetFloat("_ReceiveShadows", 1f);
        litMaterial.SetFloat("_SrcBlend", (float)BlendMode.One);
        litMaterial.SetFloat("_DstBlend", (float)BlendMode.Zero);
        litMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        litMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
        litMaterial.SetFloat("_ZWrite", 1f);
        litMaterial.SetFloat("_Cull", (float)CullMode.Back);
        litMaterial.renderQueue = litUseGroundShapeAlpha ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
        litMaterial.SetOverrideTag("RenderType", litUseGroundShapeAlpha ? "TransparentCutout" : "Opaque");
        if (litUseGroundShapeAlpha)
            litMaterial.EnableKeyword("_ALPHATEST_ON");
        else
            litMaterial.DisableKeyword("_ALPHATEST_ON");
        litMaterial.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        litMaterial.DisableKeyword("_RECEIVE_SHADOWS_OFF");

        Renderer[] renderers = groundVisualRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;

            r.enabled = true;
            if (enforceGroundReceiveShadows)
                r.receiveShadows = true;
            if (disableGroundVisualCastShadows)
                r.shadowCastingMode = ShadowCastingMode.Off;

            // 安全恢复版：地面主体只挂官方 URP/Lit。
            // 草浪 / 额外 Overlay 暂时不参与 GroundVisual 材质栈，避免自定义 Overlay Shader 编译失败或旧材质缓存把整块地面染成 Unity 粉色。
            r.sharedMaterials = new Material[] { litMaterial };
            r.SetPropertyBlock(null);
        }
    }


#if UNITY_EDITOR
    /// <summary>
    /// 编辑器恢复用：清掉临时预览/Overlay 状态，强制回到单通道 URP/Lit 地面显示。
    /// 只恢复显示链，不修改 ShapeMask / SurfaceIndex / WeightMap 数据。
    /// </summary>
    public void ForceRestoreNormalLitGroundDisplay(bool rebuildPreviewTexture = true)
    {
        useUrpLitShadowSafeOutput = true;
        enableGrassWaveShader = false;
        useEditorPreviewTexture = true;
        runtimeLitGroundBaseMap = null;
        runtimeLitBakeHash = 0;
        forceRebakeLitGroundTexture = true;

        if (groundVisualRoot != null)
        {
            Renderer[] renderers = groundVisualRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (r == null)
                    continue;
                r.SetPropertyBlock(null);
                r.sharedMaterials = new Material[0];
                r.enabled = true;
            }
        }

        if (rebuildPreviewTexture)
            RebuildEditorPreviewGroundTexture();
        else
            ApplyShadowSafeLitGroundToGroundVisual();

        EditorUtility.SetDirty(this);
    }
#endif

    private Material GetOrCreateRuntimeLitGroundMaterial()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
            return null;

        if (runtimeLitGroundMaterial == null || runtimeLitGroundMaterial.shader != lit)
        {
            runtimeLitGroundMaterial = new Material(lit);
            runtimeLitGroundMaterial.name = $"{name}_Ground_URP_Lit_ShadowSafe";
            runtimeLitGroundMaterial.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        return runtimeLitGroundMaterial;
    }

    private Material GetOrCreateGrassWaveOverlayMaterial()
    {
        Shader targetShader = Shader.Find("SkyPrison/Map/GroundGrassWaveOverlay");
        if (targetShader == null)
            return null;

        if (runtimeGrassWaveOverlayMaterial == null || runtimeGrassWaveOverlayMaterial.shader != targetShader)
        {
            runtimeGrassWaveOverlayMaterial = new Material(targetShader);
            runtimeGrassWaveOverlayMaterial.name = $"{name}_Ground_GrassWave_Overlay";
            runtimeGrassWaveOverlayMaterial.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        return runtimeGrassWaveOverlayMaterial;
    }

    private void ApplyGrassWaveOverlayProperties(Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty("_GroundShapeMask"))
            material.SetTexture("_GroundShapeMask", groundShapeMask != null ? groundShapeMask : Texture2D.whiteTexture);

        if (material.HasProperty("_SurfaceIndexMap"))
            material.SetTexture("_SurfaceIndexMap", surfaceMaterialIndexMap != null ? surfaceMaterialIndexMap : Texture2D.blackTexture);

        if (material.HasProperty("_UseSurfaceIndexMap"))
            material.SetFloat("_UseSurfaceIndexMap", surfaceMaterialIndexMap != null ? 1f : 0f);

        if (material.HasProperty("_GrassSurfaceIndex"))
            material.SetFloat("_GrassSurfaceIndex", ResolveGrassSurfacePaletteIndex());

        if (material.HasProperty("_GrassMaskSoftness"))
            material.SetFloat("_GrassMaskSoftness", grassWaveMaskSoftness);

        if (material.HasProperty("_GrassWaveStrength"))
            material.SetFloat("_GrassWaveStrength", grassWaveStrength);

        if (material.HasProperty("_GrassWaveSpeed"))
            material.SetFloat("_GrassWaveSpeed", grassWaveSpeed);

        if (material.HasProperty("_GrassWaveFrequency"))
            material.SetFloat("_GrassWaveFrequency", grassWaveFrequency);

        if (material.HasProperty("_GrassWaveDirection"))
            material.SetVector("_GrassWaveDirection", new Vector4(grassWaveDirection.x, grassWaveDirection.y, 0f, 0f));

        if (material.HasProperty("_GrassWaveLineDensity"))
            material.SetFloat("_GrassWaveLineDensity", grassWaveLineDensity);

        if (material.HasProperty("_GrassWaveIrregularity"))
            material.SetFloat("_GrassWaveIrregularity", grassWaveIrregularity);

        if (material.HasProperty("_GrassWaveNoiseStrength"))
            material.SetFloat("_GrassWaveNoiseStrength", grassWaveNoiseStrength);

        if (material.HasProperty("_GrassWaveColorStrength"))
            material.SetFloat("_GrassWaveColorStrength", grassWaveColorStrength);

        if (material.HasProperty("_GrassWaveDarkenStrength"))
            material.SetFloat("_GrassWaveDarkenStrength", grassWaveDarkenStrength);

        if (material.HasProperty("_SkyPrisonCustomTime"))
        {
            float t = Application.isPlaying || previewGrassWaveInEditMode ? Time.realtimeSinceStartup : 0f;
            material.SetFloat("_SkyPrisonCustomTime", t);
        }
    }

    private float ResolveGrassSurfacePaletteIndex()
    {
        if (surfaceMaterialPalette == null)
            return -1f;

        for (int i = 0; i < surfaceMaterialPalette.Count; i++)
        {
            GroundSurfaceMaterialDefinition material = surfaceMaterialPalette[i];
            if (material != null && material.surfaceType == GroundSurfaceType.Grass)
                return i;
        }

        return -1f;
    }

    private Texture2D GetLitGroundTextureForCurrentContext()
    {
#if UNITY_EDITOR
        // L0/L1/L2 的核心规矩：编辑显示和运行调试都走“模拟预览画布”。
        // 不能在退出 Play 回到 Scene 时弹回旧的 runtimeBakedGroundTexture，
        // 否则就会出现“编辑模拟一套、烘焙显示一套”的割裂。
        if (!SkyPrisonRenderQualityContext.IsFinal)
        {
            useEditorPreviewTexture = true;

            int previewSize = GetFastEditorPlayPreviewTextureResolution();
            if (runtimeLitGroundBaseMap == null || runtimeLitGroundBaseMap.width != previewSize || runtimeLitGroundBaseMap.height != previewSize)
                EnsureEditorPreviewLitGroundBaseMapWithoutBake(previewSize);

            if (runtimeLitGroundBaseMap != null)
                return runtimeLitGroundBaseMap;

            // 非 Final 档宁可暂时不显示预览图，也不回退到旧烘焙图。
            // 旧烘焙图只属于 L3 正式发布链。
            return null;
        }
#endif

        // L3 正式发布档：才允许使用/生成正式运行烘焙贴图。
        if (Application.isPlaying)
        {
            if (runtimeBakedGroundTexture != null)
                return runtimeBakedGroundTexture;

            return GetOrBakeLitGroundBaseMap();
        }

        if (useEditorPreviewTexture && runtimeLitGroundBaseMap != null)
            return runtimeLitGroundBaseMap;

        if (runtimeBakedGroundTexture != null)
            return runtimeBakedGroundTexture;

        return GetOrBakeLitGroundBaseMap();
    }

#if UNITY_EDITOR
    public bool CanUseFastEditorPlayPreviewWithoutAssetBake()
    {
        // 开发期快速 Play 的判定不要再要求“已有预览图 + 分辨率等于正式烘焙分辨率”。
        // 否则编辑预览是 1024、正式烘焙自动提升到 4096 时，会直接回退到完整烘焙，卡在 ExitingEditMode。
        return fastEditorPlayPreviewWithoutAssetBake || !SkyPrisonRenderQualityContext.AllowRuntimeBake;
    }

    public void PrepareFastEditorPlayPreviewWithoutAssetBake()
    {
        if (!fastEditorPlayPreviewWithoutAssetBake && SkyPrisonRenderQualityContext.AllowRuntimeBake)
            return;

        // 快速 Play 只准备一张“开发预览级”的 URP/Lit BaseMap。
        // 有现成 runtimeLitGroundBaseMap 就直接复用；没有就创建一张低成本临时图。
        // 这里绝不调用 GetOrBakeLitGroundBaseMap()，也不保存资产。
        useEditorPreviewTexture = true;

        int previewSize = GetFastEditorPlayPreviewTextureResolution();
        EnsureEditorPreviewLitGroundBaseMapWithoutBake(previewSize);
        ApplyShadowSafeLitGroundToGroundVisual();
    }
#endif

    public void MarkGroundDataDirty(bool invalidateEditorPreview = true)
    {
        needsRuntimeBake = true;
        if (invalidateEditorPreview)
        {
            runtimeLitBakeHash = 0;
            forceRebakeLitGroundTexture = true;
        }
    }

    public void RebuildEditorPreviewGroundTexture()
    {
        bool oldSuppress = SuppressAutomaticTextureImporterChanges;
        SuppressAutomaticTextureImporterChanges = true;
        try
        {
            // 编辑预览必须从当前数据通道重新生成，不能继续复用旧的 runtimeLitGroundBaseMap。
            // 之前的粉色/调试色污染就会残留在这个 HideInInspector 的内存贴图里。
            runtimeLitGroundBaseMap = null;
            runtimeLitBakeHash = 0;
            forceRebakeLitGroundTexture = true;
            useEditorPreviewTexture = true;
            GetOrBakeLitGroundBaseMap();
            ApplyShadowSafeLitGroundToGroundVisual();
        }
        finally
        {
            SuppressAutomaticTextureImporterChanges = oldSuppress;
        }
    }

    /// <summary>
    /// 清理编辑期临时显示贴图。不会改数据通道，不会保存资产。
    /// 用于从调试色/旧预览污染中恢复 GroundVisual。
    /// </summary>
    public void ClearEditorPreviewGroundTexture()
    {
        runtimeLitGroundBaseMap = null;
        runtimeLitBakeHash = 0;
        forceRebakeLitGroundTexture = true;
        useEditorPreviewTexture = false;
    }

    /// <summary>
    /// 检查当前编辑预览图是否被调试色污染。典型症状是整块品红/粉色。
    /// 检出后自动从当前数据通道重建一次正常 URP/Lit BaseMap。
    /// </summary>
    public void RepairEditorPreviewGroundTextureIfLooksCorrupted()
    {
        if (!useUrpLitShadowSafeOutput)
            return;

        if (runtimeLitGroundBaseMap == null)
            return;

        if (!DoesTextureLookLikeDebugMagenta(runtimeLitGroundBaseMap))
            return;

        RebuildEditorPreviewGroundTexture();
    }

    private bool DoesTextureLookLikeDebugMagenta(Texture2D texture)
    {
        if (texture == null || texture.width <= 0 || texture.height <= 0)
            return false;

        int w = texture.width;
        int h = texture.height;
        Color[] samples =
        {
            texture.GetPixel(w / 2, h / 2),
            texture.GetPixel(Mathf.Clamp(w / 4, 0, w - 1), Mathf.Clamp(h / 4, 0, h - 1)),
            texture.GetPixel(Mathf.Clamp(w * 3 / 4, 0, w - 1), Mathf.Clamp(h / 4, 0, h - 1)),
            texture.GetPixel(Mathf.Clamp(w / 4, 0, w - 1), Mathf.Clamp(h * 3 / 4, 0, h - 1)),
            texture.GetPixel(Mathf.Clamp(w * 3 / 4, 0, w - 1), Mathf.Clamp(h * 3 / 4, 0, h - 1)),
        };

        int magentaCount = 0;
        foreach (Color c in samples)
        {
            if (c.r > 0.72f && c.b > 0.72f && c.g < 0.30f && c.a > 0.40f)
                magentaCount++;
        }

        return magentaCount >= 3;
    }

#if UNITY_EDITOR
    public void BakeRuntimeGroundTextureAsset(bool saveAssets = true)
    {
        if (!SkyPrisonRenderQualityContext.AllowRuntimeBake)
        {
            Debug.LogWarning($"[BaseGroundBlock] 已阻止 {name} 的正式运行地面烘焙：当前不是 L3 正式发布档。");
            return;
        }

        int size = GetEffectiveLitBakedTextureResolution();

        bool oldSuppress = SuppressAutomaticTextureImporterChanges;
        SuppressAutomaticTextureImporterChanges = false;
        try
        {
            runtimeLitBakeHash = 0;
            forceRebakeLitGroundTexture = true;
            Texture2D source = GetOrBakeLitGroundBaseMap();
            if (source == null)
                return;

            EnsureEditorFolderExists(runtimeBakeAssetFolder);
            string path = runtimeBakedGroundTexture != null ? AssetDatabase.GetAssetPath(runtimeBakedGroundTexture) : null;
            if (string.IsNullOrEmpty(path))
            {
                string safeScene = gameObject.scene.IsValid() && !string.IsNullOrWhiteSpace(gameObject.scene.name) ? gameObject.scene.name : "Scene";
                string safeName = MakeSafeFileName($"{safeScene}_{name}_RuntimeGroundTexture");
                path = AssetDatabase.GenerateUniqueAssetPath($"{runtimeBakeAssetFolder}/{safeName}.asset");
                Texture2D assetTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
                assetTexture.name = $"{safeName}";
                assetTexture.wrapMode = TextureWrapMode.Clamp;
                assetTexture.filterMode = runtimeSurfaceTextureFilter;
                assetTexture.anisoLevel = Mathf.Clamp(runtimeSurfaceTextureAniso, 0, 16);
                AssetDatabase.CreateAsset(assetTexture, path);
                runtimeBakedGroundTexture = assetTexture;
            }

            if (runtimeBakedGroundTexture == null)
                runtimeBakedGroundTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            if (runtimeBakedGroundTexture == null)
                return;

            if (runtimeBakedGroundTexture.width != size || runtimeBakedGroundTexture.height != size)
                runtimeBakedGroundTexture.Reinitialize(size, size, TextureFormat.RGBA32, false);

            runtimeBakedGroundTexture.name = System.IO.Path.GetFileNameWithoutExtension(path);
            runtimeBakedGroundTexture.wrapMode = TextureWrapMode.Clamp;
            runtimeBakedGroundTexture.filterMode = runtimeSurfaceTextureFilter;
            runtimeBakedGroundTexture.anisoLevel = Mathf.Clamp(runtimeSurfaceTextureAniso, 0, 16);
            runtimeBakedGroundTexture.SetPixels(source.GetPixels());
            runtimeBakedGroundTexture.Apply(false, false);

            needsRuntimeBake = false;
            forceRebakeLitGroundTexture = false;
            EditorUtility.SetDirty(runtimeBakedGroundTexture);
            EditorUtility.SetDirty(this);
            if (saveAssets)
                AssetDatabase.SaveAssets();

            ApplyShadowSafeLitGroundToGroundVisual();
        }
        finally
        {
            SuppressAutomaticTextureImporterChanges = oldSuppress;
        }
    }

    private static void EnsureEditorFolderExists(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        if (parts.Length == 0)
            return;

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string MakeSafeFileName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "RuntimeGroundTexture";

        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
            raw = raw.Replace(c, '_');
        raw = raw.Replace(' ', '_');
        return raw;
    }
#endif

    private int GetFastEditorPlayPreviewTextureResolution()
    {
        // 开发期 Play 预览不要跟随 autoBoostLitBakeResolutionByTextureScale 升到 4096。
        // 1024 足够用于测试移动、AI、遮挡和战斗，不应该因为正式质量设置阻塞 Play。
        int baseSize = Mathf.Clamp(litBakedTextureResolution, 128, 1024);
        if (runtimeLitGroundBaseMap != null)
            baseSize = Mathf.Clamp(Mathf.Max(runtimeLitGroundBaseMap.width, runtimeLitGroundBaseMap.height), 128, 1024);
        return baseSize;
    }

    private int GetEffectiveLitBakedTextureResolution()
    {
#if UNITY_EDITOR
        if (!SkyPrisonRenderQualityContext.IsFinal)
            return Mathf.Clamp(SkyPrisonRenderQualityContext.GetFallbackGroundPreviewResolution(), 128, 2048);
#endif

        int baseSize = Mathf.Clamp(litBakedTextureResolution, 128, 4096);
        if (!autoBoostLitBakeResolutionByTextureScale)
            return baseSize;

        float visualScale = Mathf.Clamp(surfaceTextureVisualScale, 0.125f, 2f);
        float boost = visualScale < 0.999f ? 1f / visualScale : 1f;
        int boostedSize = Mathf.CeilToInt(baseSize * boost);
        int maxSize = Mathf.Clamp(maxAutoLitBakedTextureResolution, 512, 4096);
        return Mathf.Clamp(boostedSize, baseSize, maxSize);
    }

    private Texture2D GetOrBakeLitGroundBaseMap()
    {
        int size = GetEffectiveLitBakedTextureResolution();
        int hash = ComputeLitBakeHash(size);
        if (!forceRebakeLitGroundTexture && runtimeLitGroundBaseMap != null && runtimeLitGroundBaseMap.width == size && runtimeLitGroundBaseMap.height == size && runtimeLitBakeHash == hash)
            return runtimeLitGroundBaseMap;

        forceRebakeLitGroundTexture = false;
        runtimeLitBakeHash = hash;

        if (runtimeLitGroundBaseMap == null || runtimeLitGroundBaseMap.width != size || runtimeLitGroundBaseMap.height != size)
        {
            runtimeLitGroundBaseMap = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            runtimeLitGroundBaseMap.name = $"{name}_Ground_Lit_BakedBaseMap";
            runtimeLitGroundBaseMap.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            runtimeLitGroundBaseMap.wrapMode = TextureWrapMode.Clamp;
            runtimeLitGroundBaseMap.filterMode = runtimeSurfaceTextureFilter;
            runtimeLitGroundBaseMap.anisoLevel = Mathf.Clamp(runtimeSurfaceTextureAniso, 0, 16);
        }

        EnsurePaletteHasDefaultMaterial();

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = size <= 1 ? 0f : y / (float)(size - 1);
            for (int x = 0; x < size; x++)
            {
                float u = size <= 1 ? 0f : x / (float)(size - 1);
                Color c = SampleBakedSurfaceColor(u, v);
                float mask = litUseGroundShapeAlpha ? SampleMask01(u, v) : 1f;
                float runtimeAlpha = forceHardRuntimeGroundAlpha ? (mask >= groundMaskThreshold ? 1f : litNoGroundAlpha) : Mathf.Clamp01(mask);
                c.a = runtimeAlpha;
                pixels[y * size + x] = c;
            }
        }

        ApplyEnterpriseEdgeBakePostProcess(pixels, size);

        runtimeLitGroundBaseMap.SetPixels(pixels);
        runtimeLitGroundBaseMap.Apply(false, false);
        return runtimeLitGroundBaseMap;
    }


    /// <summary>
    /// 编辑器刷地面时使用的轻量实时预览：只重烘焙本次笔刷影响的小区域。
    /// 不改 TextureImporter，不 SaveAssets，不整图重建。
    /// </summary>
    public void PreviewRebakeLitGroundTextureRegion(Bounds worldBounds)
    {
        if (!useUrpLitShadowSafeOutput)
            return;

        useEditorPreviewTexture = true;
        needsRuntimeBake = true;

        int size = GetEffectiveLitBakedTextureResolution();
        if (!EnsureEditorPreviewLitGroundBaseMapWithoutBake(size))
            return;

        Bounds bounds = WorldBounds;
        if (bounds.size.x <= Mathf.Epsilon || bounds.size.z <= Mathf.Epsilon)
            return;

        float minU = Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldBounds.min.x);
        float maxU = Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldBounds.max.x);
        float minV = Mathf.InverseLerp(bounds.min.z, bounds.max.z, worldBounds.min.z);
        float maxV = Mathf.InverseLerp(bounds.min.z, bounds.max.z, worldBounds.max.z);

        if (maxU < minU)
        {
            float t = minU;
            minU = maxU;
            maxU = t;
        }
        if (maxV < minV)
        {
            float t = minV;
            minV = maxV;
            maxV = t;
        }

        int edgePad = enableSmartSurfaceComposition ? Mathf.Clamp(smartEdgeBlendPixels + 3, 3, 32) : 3;
        int minX = Mathf.Clamp(Mathf.FloorToInt(minU * (size - 1)) - edgePad, 0, size - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(maxU * (size - 1)) + edgePad, 0, size - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(minV * (size - 1)) - edgePad, 0, size - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(maxV * (size - 1)) + edgePad, 0, size - 1);

        if (maxX < minX || maxY < minY)
            return;

        EnsurePaletteHasDefaultMaterial();

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        Color[] regionPixels = new Color[width * height];

        for (int localY = 0; localY < height; localY++)
        {
            int y = minY + localY;
            float v = size <= 1 ? 0f : y / (float)(size - 1);
            for (int localX = 0; localX < width; localX++)
            {
                int x = minX + localX;
                float u = size <= 1 ? 0f : x / (float)(size - 1);
                Color c = SampleEditorRealtimeSurfaceColor(u, v);
                float mask = litUseGroundShapeAlpha ? SampleMask01(u, v) : 1f;
                c.a = ResolveSimulationPreviewAlpha(mask);
                regionPixels[localY * width + localX] = c;
            }
        }

        runtimeLitGroundBaseMap.SetPixels(minX, minY, width, height, regionPixels);
        runtimeLitGroundBaseMap.Apply(false, false);
        runtimeLitBakeHash = 0;
        forceRebakeLitGroundTexture = false;

        ApplyEditorPreviewTextureToLitMaterialsOnly();
    }


    private float ResolveSimulationPreviewAlpha(float mask)
    {
        // L0/L1/L2 是“模拟显示”，不能用半透明 mask 造成未修改区域发暗。
        // 只有 L3 正式发布档才允许保留完整软 alpha / 正式烘焙语义。
        if (!SkyPrisonRenderQualityContext.IsFinal)
            return mask >= groundMaskThreshold ? 1f : 0f;

        return forceHardRuntimeGroundAlpha
            ? (mask >= groundMaskThreshold ? 1f : litNoGroundAlpha)
            : Mathf.Clamp01(mask);
    }


    private Color SampleEditorRealtimeSurfaceColor(float u, float v)
    {
        // 拖刷实时预览必须轻：不走 TextureImporter，不强制 readable，不跑完整智能合成。
        // 正式效果仍由手动/Play 前运行烘焙负责；这里负责“画下去马上看见”。
        if (enableSurfaceWeightBlend && HasAnySurfaceWeightMap())
        {
            EnsurePaletteHasDefaultMaterial();
            int count = surfaceMaterialPalette != null ? Mathf.Min(surfaceMaterialPalette.Count, 256) : 0;
            Color accum = Color.clear;
            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                float w = SampleSurfaceMaterialWeight01(i, u, v);
                if (w <= 0.0001f)
                    continue;
                accum += SampleEditorRealtimeSingleSurfaceColor(u, v, i) * w;
                total += w;
            }

            if (total > 0.0001f)
            {
                Color weighted = accum / total;
                weighted.a = 1f;
                return weighted;
            }
        }

        return SampleEditorRealtimeSingleSurfaceColor(u, v, SampleSurfaceIndex01(u, v));
    }

    private Color SampleEditorRealtimeSingleSurfaceColor(float u, float v, int index)
    {
        GroundSurfaceMaterialDefinition material = ResolvePaletteMaterial(index);
        Color tint = ResolveSurfaceColor(material);
        tint.a = 1f;

        Texture2D tex2D = ResolveSurfaceTexture(material) as Texture2D;
        if (tex2D != null && tex2D.isReadable)
        {
            try
            {
                Vector2 uv = new Vector2(u, v);
                if (useStableSurfaceTextureWorldTiling)
                {
                    float effectiveWorldSize = ResolveEffectiveSurfaceTextureWorldSize(material);
                    float sx = Mathf.Max(0.001f, mapBoundsSize.x / effectiveWorldSize);
                    float sz = Mathf.Max(0.001f, mapBoundsSize.z / effectiveWorldSize);
                    float effectiveTilingMultiplier = Mathf.Max(0.01f, surfaceTextureTilingMultiplier);
                    uv = new Vector2(u * sx * effectiveTilingMultiplier, v * sz * effectiveTilingMultiplier);
                }

                Color tex;
                GroundSurfaceTextureDistributionMode mode = material != null
                    ? material.textureDistributionMode
                    : GroundSurfaceTextureDistributionMode.SeamlessTiling;

                if (mode == GroundSurfaceTextureDistributionMode.SingleLarge)
                    tex = tex2D.GetPixelBilinear(Mathf.Clamp01(u), Mathf.Clamp01(v));
                else
                    tex = tex2D.GetPixelBilinear(Repeat01(uv.x), Repeat01(uv.y));

                tex.a = 1f;
                Color tintedTexture = tex * Color.Lerp(Color.white, tint, Mathf.Clamp01(surfaceColorTintStrength));
                Color fallback = tint * Mathf.Clamp01(fallbackColorStrength);
                fallback.a = 1f;
                Color result = Color.Lerp(fallback, tintedTexture, Mathf.Clamp01(surfaceTextureWeight));
                result.a = 1f;
                return result;
            }
            catch
            {
                // 贴图不可读或编辑器临时状态异常时，实时预览退回 baseColor，不触发 Importer 读条。
            }
        }

        Color c = tint * Mathf.Clamp01(Mathf.Max(fallbackColorStrength, 0.85f));
        c.a = 1f;
        return c;
    }

    private bool EnsureEditorPreviewLitGroundBaseMapWithoutBake(int size)
    {
        if (runtimeLitGroundBaseMap != null && runtimeLitGroundBaseMap.width == size && runtimeLitGroundBaseMap.height == size)
            return true;

        runtimeLitGroundBaseMap = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        runtimeLitGroundBaseMap.name = $"{name}_Ground_EditorPreview_BaseMap";
        runtimeLitGroundBaseMap.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        runtimeLitGroundBaseMap.wrapMode = TextureWrapMode.Clamp;
        runtimeLitGroundBaseMap.filterMode = runtimeSurfaceTextureFilter;
        runtimeLitGroundBaseMap.anisoLevel = Mathf.Clamp(runtimeSurfaceTextureAniso, 0, 16);

        // 运行调试 / 编辑预览不再从当前 Renderer 反拷贝画面，也不混用旧的正式烘焙贴图。
        // 统一从 SurfaceIndex / WeightMap / SurfaceMaterialDefinition 的“四档模拟贴图”生成。
        // 这样水泥、沥青、草地等每种地表都清楚知道：低档怎么显示，高档怎么显示；
        // 没有被本次笔刷修改的区域也不会被旧截图、阴影或暗色调试遮罩污染。
        FillRuntimeLitPreviewFromCurrentSurfaceData(size);
        ApplyEditorPreviewTextureToLitMaterialsOnly();
        return true;
    }

    private bool TryCopyTextureIntoRuntimeLitPreview(Texture source, int size)
    {
        if (source == null || runtimeLitGroundBaseMap == null)
            return false;

        // 第一选择：GPU 拷贝当前可见画面。
        // 这条路径不要求 Texture2D Read/Write Enabled，不改 Importer，不走 AssetDatabase。
        if (TryGpuCopyTextureIntoRuntimeLitPreview(source, size))
            return true;

        // 兜底：如果源本身是 readable Texture2D，再走 CPU 采样。
        Texture2D source2D = source as Texture2D;
        if (source2D == null)
            return false;

        try
        {
            if (source2D.width == size && source2D.height == size)
            {
                runtimeLitGroundBaseMap.SetPixels(source2D.GetPixels());
                runtimeLitGroundBaseMap.Apply(false, false);
                return true;
            }

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = size <= 1 ? 0f : y / (float)(size - 1);
                for (int x = 0; x < size; x++)
                {
                    float u = size <= 1 ? 0f : x / (float)(size - 1);
                    pixels[y * size + x] = source2D.GetPixelBilinear(u, v);
                }
            }

            runtimeLitGroundBaseMap.SetPixels(pixels);
            runtimeLitGroundBaseMap.Apply(false, false);
            return true;
        }
        catch
        {
            // 不为了复制旧画面去改 TextureImporter；不可读时 GPU 路径失败才会走到这里。
            return false;
        }
    }

    private bool TryGpuCopyTextureIntoRuntimeLitPreview(Texture source, int size)
    {
        if (source == null || runtimeLitGroundBaseMap == null || size <= 0)
            return false;

        RenderTexture previous = RenderTexture.active;
        RenderTexture rt = null;
        try
        {
            rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.filterMode = runtimeSurfaceTextureFilter;

            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            runtimeLitGroundBaseMap.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
            runtimeLitGroundBaseMap.Apply(false, false);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null)
                RenderTexture.ReleaseTemporary(rt);
        }
    }

    private void FillRuntimeLitPreviewFromCurrentSurfaceData(int size)
    {
        if (runtimeLitGroundBaseMap == null)
            return;

        EnsurePaletteHasDefaultMaterial();

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = size <= 1 ? 0f : y / (float)(size - 1);
            for (int x = 0; x < size; x++)
            {
                float u = size <= 1 ? 0f : x / (float)(size - 1);
                Color c = SampleEditorRealtimeSurfaceColor(u, v);
                float mask = litUseGroundShapeAlpha ? SampleMask01(u, v) : 1f;
                c.a = ResolveSimulationPreviewAlpha(mask);
                pixels[y * size + x] = c;
            }
        }

        runtimeLitGroundBaseMap.SetPixels(pixels);
        runtimeLitGroundBaseMap.Apply(false, false);
        runtimeLitBakeHash = 0;
        forceRebakeLitGroundTexture = false;
    }

    private Texture FindCurrentGroundVisualTexture()
    {
        if (groundVisualRoot == null)
            return null;

        Renderer[] renderers = groundVisualRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;

            Material m = r.sharedMaterial;
            if (m == null)
                continue;

            Texture tex = null;
            if (m.HasProperty("_BaseMap"))
                tex = m.GetTexture("_BaseMap");
            if (tex == null && m.HasProperty("_MainTex"))
                tex = m.GetTexture("_MainTex");

            if (tex != null)
                return tex;
        }

        return null;
    }

    private void ApplyEditorPreviewTextureToLitMaterialsOnly()
    {
        Material litMaterial = GetOrCreateRuntimeLitGroundMaterial();
        if (litMaterial != null && runtimeLitGroundBaseMap != null)
        {
            litMaterial.SetTexture("_BaseMap", runtimeLitGroundBaseMap);
            litMaterial.SetTexture("_MainTex", runtimeLitGroundBaseMap);
            if (litMaterial.HasProperty("_BaseMap"))
                litMaterial.SetTextureScale("_BaseMap", Vector2.one);
            if (litMaterial.HasProperty("_MainTex"))
                litMaterial.SetTextureScale("_MainTex", Vector2.one);
        }

        Material overlayMaterial = GetOrCreateGrassWaveOverlayMaterial();
        if (overlayMaterial != null && runtimeLitGroundBaseMap != null)
        {
            overlayMaterial.SetTexture("_BaseMap", runtimeLitGroundBaseMap);
            overlayMaterial.SetTexture("_MainTex", runtimeLitGroundBaseMap);
            ApplyGrassWaveOverlayProperties(overlayMaterial);
        }
    }

    private int ComputeLitBakeHash(int size)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + size;
            h = h * 31 + Mathf.RoundToInt(groundMaskThreshold * 10000f);
            h = h * 31 + Mathf.RoundToInt(litAlphaCutoff * 10000f);
            h = h * 31 + (litUseGroundShapeAlpha ? 1 : 0);
            h = h * 31 + (groundShapeMask != null ? groundShapeMask.GetInstanceID() : 0);
            h = h * 31 + (surfaceMaterialIndexMap != null ? surfaceMaterialIndexMap.GetInstanceID() : 0);
            h = h * 31 + Mathf.RoundToInt(surfaceTextureWeight * 10000f);
            h = h * 31 + Mathf.RoundToInt(surfaceColorTintStrength * 10000f);
            h = h * 31 + Mathf.RoundToInt(fallbackColorStrength * 10000f);
            h = h * 31 + Mathf.RoundToInt(surfaceTextureWorldSize * 1000f);
            h = h * 31 + Mathf.RoundToInt(surfaceTextureTilingMultiplier * 1000f);
            h = h * 31 + Mathf.RoundToInt(surfaceTextureVisualScale * 10000f);
            h = h * 31 + (enableSurfaceTextureAntiRepeat ? 1 : 0);
            h = h * 31 + Mathf.RoundToInt(surfaceTextureAntiRepeatStrength * 10000f);
            h = h * 31 + Mathf.RoundToInt(surfaceTextureAntiRepeatWorldSize * 1000f);
            h = h * 31 + Mathf.RoundToInt(surfaceTextureAntiRepeatUvOffset * 10000f);
            h = h * 31 + Mathf.RoundToInt(surfaceTextureAntiRepeatToneJitter * 10000f);
            h = h * 31 + (autoBoostLitBakeResolutionByTextureScale ? 1 : 0);
            h = h * 31 + maxAutoLitBakedTextureResolution;
            h = h * 31 + (int)runtimeSurfaceTextureFilter;
            h = h * 31 + runtimeSurfaceTextureAniso;
            h = h * 31 + (useStableSurfaceTextureWorldTiling ? 1 : 0);
            h = h * 31 + (enableSmartSurfaceComposition ? 1 : 0);
            h = h * 31 + smartEdgeBlendPixels;
            h = h * 31 + (enableMaterialNeighborBlur ? 1 : 0);
            h = h * 31 + Mathf.RoundToInt(materialNeighborBlurStrength * 10000f);
            h = h * 31 + Mathf.RoundToInt(smartEdgeNoiseStrength * 10000f);
            h = h * 31 + Mathf.RoundToInt(smartEdgeNoiseScale * 1000f);
            h = h * 31 + Mathf.RoundToInt(smartEdgeDirtStrength * 10000f);
            h = h * 31 + Mathf.RoundToInt(smartSurfaceVariationStrength * 10000f);
            h = h * 31 + Mathf.RoundToInt(smartSurfaceVariationScale * 1000f);
            h = h * 31 + Mathf.RoundToInt(smartOverlayStrength * 10000f);
            h = h * 31 + Mathf.RoundToInt(smartOverlayNoiseScale * 1000f);
            h = h * 31 + ColorToHash(smartOverlayColor);
            h = h * 31 + (forceHardRuntimeGroundAlpha ? 1 : 0);
            h = h * 31 + (enableEdgeColorDilation ? 1 : 0);
            h = h * 31 + edgeColorDilationPixels;
            h = h * 31 + Mathf.RoundToInt(shapeInnerEdgeDirtStrength * 10000f);
            h = h * 31 + (enableSurfaceWeightBlend ? 1 : 0);
            h = h * 31 + (surfaceMaterialPalette != null ? surfaceMaterialPalette.Count : 0);
            if (surfaceMaterialWeightMaps != null)
            {
                h = h * 31 + surfaceMaterialWeightMaps.Count;
                for (int i = 0; i < surfaceMaterialWeightMaps.Count; i++)
                    h = h * 31 + (surfaceMaterialWeightMaps[i] != null ? surfaceMaterialWeightMaps[i].GetInstanceID() : 0);
            }
            if (surfaceMaterialPalette != null)
            {
                for (int i = 0; i < surfaceMaterialPalette.Count; i++)
                {
                    GroundSurfaceMaterialDefinition mat = surfaceMaterialPalette[i];
                    h = h * 31 + (mat != null ? mat.GetInstanceID() : 0);
                    if (mat != null)
                    {
                        h = h * 31 + ColorToHash(mat.baseColor);
                        h = h * 31 + (int)mat.textureDistributionMode;
                        h = h * 31 + Mathf.RoundToInt(mat.textureWorldSize * 1000f);
                        h = h * 31 + Mathf.RoundToInt(mat.randomScaleMin * 1000f);
                        h = h * 31 + Mathf.RoundToInt(mat.randomScaleMax * 1000f);
                        h = h * 31 + Mathf.RoundToInt(mat.randomOffsetStrength * 10000f);
                        h = h * 31 + (mat.allowRotate90 ? 1 : 0);
                        h = h * 31 + (mat.allowFlipX ? 1 : 0);
                        h = h * 31 + (mat.allowFlipY ? 1 : 0);
                        h = h * 31 + (mat.antiRepeatEnabled ? 1 : 0);
                        h = h * 31 + Mathf.RoundToInt(mat.antiRepeatStrength * 10000f);
                        h = h * 31 + Mathf.RoundToInt(mat.antiRepeatWorldSize * 1000f);
                        h = h * 31 + Mathf.RoundToInt(mat.antiRepeatUvOffset * 10000f);
                        h = h * 31 + Mathf.RoundToInt(mat.antiRepeatToneJitter * 10000f);
                        h = h * 31 + Mathf.RoundToInt(mat.variantBlendStrength * 10000f);
                        h = h * 31 + Mathf.RoundToInt(mat.stochasticBlendStrength * 10000f);
                        h = h * 31 + Mathf.RoundToInt(mat.macroVariationStrength * 10000f);
                        h = h * 31 + Mathf.RoundToInt(mat.macroVariationWorldSize * 1000f);
                        h = h * 31 + (mat.macroVariationTexture != null ? mat.macroVariationTexture.GetInstanceID() : 0);
                        h = h * 31 + (int)mat.baseColorBlendMode;
                        h = h * 31 + Mathf.RoundToInt(mat.baseColorBlendStrength * 10000f);
                        h = h * 31 + Mathf.RoundToInt(mat.brightness * 10000f);
                        h = h * 31 + Mathf.RoundToInt(mat.contrast * 10000f);
                        h = h * 31 + Mathf.RoundToInt(mat.saturation * 10000f);
                        if (mat.textureVariants != null)
                        {
                            h = h * 31 + mat.textureVariants.Count;
                            for (int j = 0; j < mat.textureVariants.Count; j++)
                                h = h * 31 + (mat.textureVariants[j] != null ? mat.textureVariants[j].GetInstanceID() : 0);
                        }
                        Texture tex = ResolveSurfaceTextureForHashOnly(mat);
                        h = h * 31 + (tex != null ? tex.GetInstanceID() : 0);
                    }
                }
            }
            return h;
        }
    }

    private float SampleMask01(float u, float v)
    {
        if (groundShapeMask == null)
            return 1f;
        return groundShapeMask.GetPixelBilinear(Mathf.Clamp01(u), Mathf.Clamp01(v)).a;
    }

    private int SampleSurfaceIndex01(float u, float v)
    {
        if (surfaceMaterialIndexMap == null)
            return 0;
        Color c = surfaceMaterialIndexMap.GetPixelBilinear(Mathf.Clamp01(u), Mathf.Clamp01(v));
        return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f), 0, 255);
    }

    private Color SampleBakedSurfaceColor(float u, float v)
    {
        if (enableSurfaceWeightBlend && HasAnySurfaceWeightMap())
        {
            Color weighted = SampleWeightedSurfaceLayerColor(u, v, out float mixedEdge);
            return ApplySmartSurfaceOverlay(weighted, u, v, mixedEdge);
        }

        if (!enableSmartSurfaceComposition || surfaceMaterialIndexMap == null)
        {
            Color baseColor = SampleSingleSurfaceLayerColor(u, v);
            return ApplySmartSurfaceOverlay(baseColor, u, v, 0f);
        }

        // 企业默认：不要用邻域平均去“糊开”不同材质。
        // 单通道 SurfaceMaterialIndexMap 只能表达一个主材质，强行取邻居平均会形成油污状软边。
        // 真正的自然过渡后续应使用权重图/贴花层/路径压痕层。这里先保持材质本身清晰。
        if (!enableMaterialNeighborBlur || smartEdgeBlendPixels <= 0)
        {
            Color baseColor = SampleSingleSurfaceLayerColor(u, v);
            return ApplySmartSurfaceOverlay(baseColor, u, v, 0f);
        }

        int centerIndex = SampleSurfaceIndex01(u, v);
        Color accum = SampleSingleSurfaceLayerColor(u, v, centerIndex) * 1.0f;
        float total = 1.0f;
        float edgeAmount = 0f;

        int texW = surfaceMaterialIndexMap != null ? Mathf.Max(1, surfaceMaterialIndexMap.width) : Mathf.Max(1, GetEffectiveLitBakedTextureResolution());
        int texH = surfaceMaterialIndexMap != null ? Mathf.Max(1, surfaceMaterialIndexMap.height) : Mathf.Max(1, GetEffectiveLitBakedTextureResolution());

        int radius = Mathf.Clamp(smartEdgeBlendPixels, 1, 24);
        // 两圈八方向采样。比完整圆盘轻很多，但足够打散刷子硬边。
        for (int ring = 1; ring <= 2; ring++)
        {
            float ringRadius = radius * (ring / 2f);
            float du = ringRadius / texW;
            float dv = ringRadius / texH;

            AccumulateNeighborSurface(u + du, v, centerIndex, ring, ref accum, ref total, ref edgeAmount);
            AccumulateNeighborSurface(u - du, v, centerIndex, ring, ref accum, ref total, ref edgeAmount);
            AccumulateNeighborSurface(u, v + dv, centerIndex, ring, ref accum, ref total, ref edgeAmount);
            AccumulateNeighborSurface(u, v - dv, centerIndex, ring, ref accum, ref total, ref edgeAmount);
            AccumulateNeighborSurface(u + du * 0.7071f, v + dv * 0.7071f, centerIndex, ring, ref accum, ref total, ref edgeAmount);
            AccumulateNeighborSurface(u - du * 0.7071f, v + dv * 0.7071f, centerIndex, ring, ref accum, ref total, ref edgeAmount);
            AccumulateNeighborSurface(u + du * 0.7071f, v - dv * 0.7071f, centerIndex, ring, ref accum, ref total, ref edgeAmount);
            AccumulateNeighborSurface(u - du * 0.7071f, v - dv * 0.7071f, centerIndex, ring, ref accum, ref total, ref edgeAmount);
        }

        Color blended = total > 0.0001f ? accum / total : SampleSingleSurfaceLayerColor(u, v, centerIndex);
        Color centerColor = SampleSingleSurfaceLayerColor(u, v, centerIndex);
        Color result = Color.Lerp(centerColor, blended, Mathf.Clamp01(materialNeighborBlurStrength));
        result.a = 1f;
        edgeAmount = Mathf.Clamp01(edgeAmount);

        // 边界脏污不能做成连续暗环，否则软刷会出现“脏辫/描边”。
        // 企业做法是把边界脏污噪声门控成断续污渍，只在少量边缘像素上出现。
        if (smartEdgeDirtStrength > 0f && edgeAmount > 0f)
        {
            float n = Fbm01(u * smartEdgeNoiseScale + 13.17f, v * smartEdgeNoiseScale + 91.73f);
            float dirtMask = Mathf.SmoothStep(0.58f, 0.92f, n);
            float dirt = edgeAmount * Mathf.Clamp01(smartEdgeDirtStrength) * dirtMask;
            result = Color.Lerp(result, result * 0.82f, Mathf.Clamp01(dirt));
            result.a = 1f;
        }

        if (shapeInnerEdgeDirtStrength > 0f && litUseGroundShapeAlpha && groundShapeMask != null)
        {
            float shapeEdge = ComputeInnerShapeEdgeAmount(u, v);
            if (shapeEdge > 0f)
            {
                float n = Fbm01(u * smartEdgeNoiseScale + 37.1f, v * smartEdgeNoiseScale + 81.9f);
                float dirtMask = Mathf.SmoothStep(0.62f, 0.94f, n);
                float dirt = shapeEdge * Mathf.Clamp01(shapeInnerEdgeDirtStrength) * dirtMask;
                result = Color.Lerp(result, result * 0.86f, Mathf.Clamp01(dirt));
                result.a = 1f;
            }
        }

        return ApplySmartSurfaceOverlay(result, u, v, edgeAmount);
    }

    private void ApplyEnterpriseEdgeBakePostProcess(Color[] pixels, int size)
    {
        if (pixels == null || size <= 0)
            return;

        if (enableEdgeColorDilation && edgeColorDilationPixels > 0)
            DilateTransparentPixelColors(pixels, size, Mathf.Clamp(edgeColorDilationPixels, 0, 32));
    }

    private void DilateTransparentPixelColors(Color[] pixels, int size, int radius)
    {
        int count = pixels.Length;
        bool[] hasColor = new bool[count];
        Color[] work = new Color[count];

        for (int i = 0; i < count; i++)
        {
            work[i] = pixels[i];
            hasColor[i] = pixels[i].a > 0.5f;
        }

        int[] ox = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] oy = { -1, -1, -1, 0, 0, 1, 1, 1 };

        for (int step = 0; step < radius; step++)
        {
            bool changed = false;
            bool[] nextHas = (bool[])hasColor.Clone();
            Color[] next = (Color[])work.Clone();

            for (int y = 0; y < size; y++)
            {
                int row = y * size;
                for (int x = 0; x < size; x++)
                {
                    int idx = row + x;
                    if (hasColor[idx])
                        continue;

                    Color accum = Color.clear;
                    int total = 0;
                    for (int n = 0; n < 8; n++)
                    {
                        int nx = x + ox[n];
                        int ny = y + oy[n];
                        if (nx < 0 || nx >= size || ny < 0 || ny >= size)
                            continue;

                        int ni = ny * size + nx;
                        if (!hasColor[ni])
                            continue;

                        Color c = work[ni];
                        c.a = 1f;
                        accum += c;
                        total++;
                    }

                    if (total > 0)
                    {
                        Color c = accum / total;
                        c.a = pixels[idx].a;
                        next[idx] = c;
                        nextHas[idx] = true;
                        changed = true;
                    }
                }
            }

            work = next;
            hasColor = nextHas;
            if (!changed)
                break;
        }

        for (int i = 0; i < count; i++)
        {
            if (pixels[i].a <= 0.5f)
            {
                float a = pixels[i].a;
                pixels[i] = work[i];
                pixels[i].a = a;
            }
        }
    }

    private float ComputeInnerShapeEdgeAmount(float u, float v)
    {
        if (groundShapeMask == null || !litUseGroundShapeAlpha)
            return 0f;

        float center = SampleMask01(u, v);
        if (center < groundMaskThreshold)
            return 0f;

        int radius = Mathf.Clamp(smartEdgeBlendPixels, 1, 24);
        int texW = Mathf.Max(1, groundShapeMask.width);
        int texH = Mathf.Max(1, groundShapeMask.height);
        float du = radius / (float)texW;
        float dv = radius / (float)texH;

        float minNeighbor = 1f;
        minNeighbor = Mathf.Min(minNeighbor, SampleMask01(u + du, v));
        minNeighbor = Mathf.Min(minNeighbor, SampleMask01(u - du, v));
        minNeighbor = Mathf.Min(minNeighbor, SampleMask01(u, v + dv));
        minNeighbor = Mathf.Min(minNeighbor, SampleMask01(u, v - dv));
        minNeighbor = Mathf.Min(minNeighbor, SampleMask01(u + du * 0.7071f, v + dv * 0.7071f));
        minNeighbor = Mathf.Min(minNeighbor, SampleMask01(u - du * 0.7071f, v + dv * 0.7071f));
        minNeighbor = Mathf.Min(minNeighbor, SampleMask01(u + du * 0.7071f, v - dv * 0.7071f));
        minNeighbor = Mathf.Min(minNeighbor, SampleMask01(u - du * 0.7071f, v - dv * 0.7071f));

        return Mathf.Clamp01((center - minNeighbor) / Mathf.Max(0.0001f, 1f - groundMaskThreshold));
    }

    private void AccumulateNeighborSurface(float u, float v, int centerIndex, int ring, ref Color accum, ref float total, ref float edgeAmount)
    {
        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);

        int neighborIndex = SampleSurfaceIndex01(u, v);
        if (neighborIndex == centerIndex)
            return;

        float baseWeight = ring == 1 ? 0.34f : 0.18f;
        if (smartEdgeNoiseStrength > 0f)
        {
            float n = ValueNoise01(u * smartEdgeNoiseScale + neighborIndex * 7.31f, v * smartEdgeNoiseScale + ring * 19.87f);
            baseWeight *= Mathf.Lerp(1f - smartEdgeNoiseStrength, 1f + smartEdgeNoiseStrength, n);
        }

        baseWeight = Mathf.Max(0f, baseWeight);
        if (baseWeight <= 0f)
            return;

        accum += SampleSingleSurfaceLayerColor(u, v, neighborIndex) * baseWeight;
        total += baseWeight;
        edgeAmount += baseWeight * 0.65f;
    }

    private bool HasAnySurfaceWeightMap()
    {
        if (!enableSurfaceWeightBlend || surfaceMaterialWeightMaps == null)
            return false;

        for (int i = 0; i < surfaceMaterialWeightMaps.Count; i++)
        {
            if (surfaceMaterialWeightMaps[i] != null)
                return true;
        }

        return false;
    }

    private Color SampleWeightedSurfaceLayerColor(float u, float v, out float mixedEdge)
    {
        mixedEdge = 0f;
        EnsurePaletteHasDefaultMaterial();

        int count = surfaceMaterialPalette != null ? Mathf.Min(surfaceMaterialPalette.Count, 256) : 0;
        if (count <= 0)
            return SampleSingleSurfaceLayerColor(u, v);

        Color accum = Color.clear;
        float total = 0f;
        float maxWeight = 0f;
        int contributing = 0;

        for (int i = 0; i < count; i++)
        {
            float w = SampleSurfaceMaterialWeight01(i, u, v);
            if (w <= 0.0001f)
                continue;

            accum += SampleSingleSurfaceLayerColor(u, v, i) * w;
            total += w;
            maxWeight = Mathf.Max(maxWeight, w);
            contributing++;
        }

        if (total <= 0.0001f)
            return SampleSingleSurfaceLayerColor(u, v);

        Color result = accum / total;
        result.a = 1f;

        // 多材质权重同时存在时，给智能叠加一个温和的“混合边缘量”。
        mixedEdge = contributing > 1 ? Mathf.Clamp01(1f - maxWeight / total) : 0f;
        return result;
    }

    public float SampleSurfaceMaterialWeight01(int paletteIndex, float u, float v)
    {
        if (paletteIndex < 0 || surfaceMaterialWeightMaps == null)
            return 0f;

        int group = paletteIndex / 4;
        int channel = paletteIndex % 4;
        if (group < 0 || group >= surfaceMaterialWeightMaps.Count)
            return 0f;

        Texture2D map = surfaceMaterialWeightMaps[group];
        if (map == null)
            return 0f;

        Color c = map.GetPixelBilinear(Mathf.Clamp01(u), Mathf.Clamp01(v));
        switch (channel)
        {
            case 0: return Mathf.Clamp01(c.r);
            case 1: return Mathf.Clamp01(c.g);
            case 2: return Mathf.Clamp01(c.b);
            case 3: return Mathf.Clamp01(c.a);
            default: return 0f;
        }
    }

    private Color SampleSingleSurfaceLayerColor(float u, float v)
    {
        return SampleSingleSurfaceLayerColor(u, v, SampleSurfaceIndex01(u, v));
    }

    private Color SampleSingleSurfaceLayerColor(float u, float v, int index)
    {
        GroundSurfaceMaterialDefinition material = ResolvePaletteMaterial(index);
        Color tint = ResolveSurfaceColor(material);
        Texture texture = ResolveSurfaceTexture(material);
        Texture2D tex2D = texture as Texture2D;

        if (tex2D != null)
        {
            if (TryPrepareReadableTextureForLitBake(tex2D))
            {
                try
                {
                    Vector2 uv = new Vector2(u, v);
                    if (useStableSurfaceTextureWorldTiling)
                    {
                        float effectiveWorldSize = ResolveEffectiveSurfaceTextureWorldSize(material);
                        float sx = Mathf.Max(0.001f, mapBoundsSize.x / effectiveWorldSize);
                        float sz = Mathf.Max(0.001f, mapBoundsSize.z / effectiveWorldSize);
                        float effectiveTilingMultiplier = Mathf.Max(0.01f, surfaceTextureTilingMultiplier);
                        uv = new Vector2(u * sx * effectiveTilingMultiplier, v * sz * effectiveTilingMultiplier);
                    }

                    Color tex = SampleSurfaceTextureByDistribution(tex2D, material, uv, u, v);
                    tex.a = 1f;

                    Color tintedTexture = tex * Color.Lerp(Color.white, tint, Mathf.Clamp01(surfaceColorTintStrength));
                    Color fallback = tint * Mathf.Clamp01(fallbackColorStrength);
                    fallback.a = 1f;

                    Color result = Color.Lerp(fallback, tintedTexture, Mathf.Clamp01(surfaceTextureWeight));
                    result.a = 1f;
                    return result;
                }
                catch
                {
                }
            }
        }

        tint *= Mathf.Clamp01(fallbackColorStrength);
        tint.a = 1f;
        return tint;
    }

    private Color SampleSurfaceTextureByDistribution(Texture2D tex2D, GroundSurfaceMaterialDefinition material, Vector2 tiledUv, float mapU, float mapV)
    {
        if (tex2D == null)
            return Color.white;

        GroundSurfaceTextureDistributionMode mode = material != null
            ? material.textureDistributionMode
            : GroundSurfaceTextureDistributionMode.SeamlessTiling;

        Color result;
        switch (mode)
        {
            case GroundSurfaceTextureDistributionMode.SingleLarge:
                result = tex2D.GetPixelBilinear(Mathf.Clamp01(mapU), Mathf.Clamp01(mapV));
                break;

            case GroundSurfaceTextureDistributionMode.RandomScatter:
                result = SampleSurfaceTextureRandomScatter(tex2D, material, tiledUv, mapU, mapV);
                break;

            case GroundSurfaceTextureDistributionMode.SeamlessTiling:
            case GroundSurfaceTextureDistributionMode.StampDecal:
            case GroundSurfaceTextureDistributionMode.SplinePattern:
            default:
                result = tex2D.GetPixelBilinear(Repeat01(tiledUv.x), Repeat01(tiledUv.y));
                break;
        }

        result.a = 1f;
        result = ApplySurfaceMaterialMacroVariation(result, material, mapU, mapV);
        result = ApplySurfaceMaterialColorControls(result, material);
        result.a = 1f;
        return result;
    }

    private Color SampleSurfaceTextureRandomScatter(Texture2D tex2D, GroundSurfaceMaterialDefinition material, Vector2 tiledUv, float mapU, float mapV)
    {
        Color original = tex2D.GetPixelBilinear(Repeat01(tiledUv.x), Repeat01(tiledUv.y));
        original.a = 1f;

        if (material == null)
            return original;

        bool enabled = material.antiRepeatEnabled && enableSurfaceTextureAntiRepeat;
        float strength = Mathf.Clamp01(Mathf.Max(material.antiRepeatStrength * surfaceTextureAntiRepeatStrength, material.stochasticBlendStrength));
        if (!enabled || strength <= 0.0001f)
            return original;

        Bounds bounds = WorldBounds;
        float worldX = Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01(mapU));
        float worldZ = Mathf.Lerp(bounds.min.z, bounds.max.z, Mathf.Clamp01(mapV));
        float cellSize = Mathf.Max(0.25f, material.antiRepeatWorldSize);

        float gx = worldX / cellSize;
        float gz = worldZ / cellSize;
        int ix = Mathf.FloorToInt(gx);
        int iz = Mathf.FloorToInt(gz);
        float fx = Smooth01(gx - ix);
        float fz = Smooth01(gz - iz);

        Color c00 = SampleSurfaceTextureScatterCell(tex2D, material, tiledUv, ix, iz);
        Color c10 = SampleSurfaceTextureScatterCell(tex2D, material, tiledUv, ix + 1, iz);
        Color c01 = SampleSurfaceTextureScatterCell(tex2D, material, tiledUv, ix, iz + 1);
        Color c11 = SampleSurfaceTextureScatterCell(tex2D, material, tiledUv, ix + 1, iz + 1);

        Color cx0 = Color.Lerp(c00, c10, fx);
        Color cx1 = Color.Lerp(c01, c11, fx);
        Color blended = Color.Lerp(cx0, cx1, fz);
        blended.a = 1f;

        return Color.Lerp(original, blended, strength);
    }

    private Color SampleSurfaceTextureScatterCell(Texture2D baseTex, GroundSurfaceMaterialDefinition material, Vector2 tiledUv, int cellX, int cellZ)
    {
        Texture2D tex = ResolveScatterVariantTexture(baseTex, material, cellX, cellZ);
        if (tex == null)
            tex = baseTex;

        float offsetScale = Mathf.Clamp01(material != null ? material.antiRepeatUvOffset : surfaceTextureAntiRepeatUvOffset);
        offsetScale *= Mathf.Clamp01(material != null ? material.randomOffsetStrength : 1f);

        float ox = (Hash01(cellX * 17 + 3, cellZ * 31 + 11) - 0.5f) * 2f * offsetScale;
        float oz = (Hash01(cellX * 47 + 19, cellZ * 13 + 5) - 0.5f) * 2f * offsetScale;

        float scaleMin = material != null ? Mathf.Max(0.01f, material.randomScaleMin) : 1f;
        float scaleMax = material != null ? Mathf.Max(scaleMin, material.randomScaleMax) : 1f;
        float scale = Mathf.Lerp(scaleMin, scaleMax, Hash01(cellX * 67 + 7, cellZ * 83 + 17));

        Vector2 uv = new Vector2(tiledUv.x + ox, tiledUv.y + oz);
        Vector2 whole = new Vector2(Mathf.Floor(uv.x), Mathf.Floor(uv.y));
        Vector2 local = new Vector2(Repeat01(uv.x), Repeat01(uv.y));

        local = (local - new Vector2(0.5f, 0.5f)) / Mathf.Max(0.01f, scale) + new Vector2(0.5f, 0.5f);

        if (material != null && material.allowFlipX && Hash01(cellX * 101 + 5, cellZ * 109 + 13) > 0.5f)
            local.x = 1f - local.x;
        if (material != null && material.allowFlipY && Hash01(cellX * 131 + 23, cellZ * 137 + 29) > 0.5f)
            local.y = 1f - local.y;

        if (material != null && material.allowRotate90)
        {
            int rot = Mathf.FloorToInt(Hash01(cellX * 151 + 31, cellZ * 157 + 37) * 4f) & 3;
            if (rot == 1)
                local = new Vector2(local.y, 1f - local.x);
            else if (rot == 2)
                local = new Vector2(1f - local.x, 1f - local.y);
            else if (rot == 3)
                local = new Vector2(1f - local.y, local.x);
        }

        Vector2 finalUv = whole + local;
        Color c = tex.GetPixelBilinear(Repeat01(finalUv.x), Repeat01(finalUv.y));
        c.a = 1f;

        float toneStrength = Mathf.Clamp(material != null ? material.antiRepeatToneJitter : surfaceTextureAntiRepeatToneJitter, 0f, 0.2f);
        if (toneStrength > 0.0001f)
        {
            float tone = 1f + (Hash01(cellX * 71 + 23, cellZ * 97 + 29) - 0.5f) * 2f * toneStrength;
            c.r = Mathf.Clamp01(c.r * tone);
            c.g = Mathf.Clamp01(c.g * tone);
            c.b = Mathf.Clamp01(c.b * tone);
        }

        if (material != null && material.textureVariants != null && material.textureVariants.Count > 0 && material.variantBlendStrength > 0.0001f)
        {
            Color baseC = baseTex.GetPixelBilinear(Repeat01(finalUv.x), Repeat01(finalUv.y));
            baseC.a = 1f;
            c = Color.Lerp(baseC, c, Mathf.Clamp01(material.variantBlendStrength));
            c.a = 1f;
        }

        return c;
    }

    private Texture2D ResolveScatterVariantTexture(Texture2D baseTex, GroundSurfaceMaterialDefinition material, int cellX, int cellZ)
    {
        if (material == null || material.textureVariants == null || material.textureVariants.Count == 0)
            return baseTex;

        int validCount = 1;
        for (int i = 0; i < material.textureVariants.Count; i++)
        {
            if (material.textureVariants[i] != null)
                validCount++;
        }

        if (validCount <= 1)
            return baseTex;

        int pick = Mathf.FloorToInt(Hash01(cellX * 173 + 41, cellZ * 181 + 43) * validCount);
        if (pick <= 0)
            return baseTex;

        int seen = 1;
        for (int i = 0; i < material.textureVariants.Count; i++)
        {
            Texture2D variant = material.textureVariants[i];
            if (variant == null)
                continue;

            if (seen == pick)
            {
                StabilizeSurfaceTexture(variant);
                if (TryPrepareReadableTextureForLitBake(variant))
                    return variant;
                return baseTex;
            }
            seen++;
        }

        return baseTex;
    }

    private Color ApplySurfaceMaterialMacroVariation(Color color, GroundSurfaceMaterialDefinition material, float mapU, float mapV)
    {
        if (material == null || material.textureDistributionMode != GroundSurfaceTextureDistributionMode.RandomScatter)
            return color;

        float strength = Mathf.Clamp01(material.macroVariationStrength);
        if (strength <= 0.0001f)
            return color;

        Bounds bounds = WorldBounds;
        float worldX = Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01(mapU));
        float worldZ = Mathf.Lerp(bounds.min.z, bounds.max.z, Mathf.Clamp01(mapV));
        float size = Mathf.Max(0.01f, material.macroVariationWorldSize);

        float n = 0.5f;
        Texture2D macro = material.macroVariationTexture;
        if (macro != null)
        {
            StabilizeSurfaceTexture(macro);
            if (TryPrepareReadableTextureForLitBake(macro))
            {
                Color mc = macro.GetPixelBilinear(Repeat01(worldX / size), Repeat01(worldZ / size));
                n = (mc.r + mc.g + mc.b) / 3f;
            }
            else
            {
                n = Fbm01(worldX / size + 19.7f, worldZ / size + 73.1f);
            }
        }
        else
        {
            n = Fbm01(worldX / size + 19.7f, worldZ / size + 73.1f);
        }

        float variation = (n - 0.5f) * 2f * strength;
        color.r = Mathf.Clamp01(color.r + variation);
        color.g = Mathf.Clamp01(color.g + variation);
        color.b = Mathf.Clamp01(color.b + variation);
        color.a = 1f;
        return color;
    }

    private Color ApplySurfaceMaterialColorControls(Color color, GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return color;

        Color baseColor = material.baseColor;
        baseColor.a = 1f;
        float blendStrength = Mathf.Clamp01(material.baseColorBlendStrength);

        if (blendStrength > 0.0001f)
        {
            Color blended = color;
            switch (material.baseColorBlendMode)
            {
                case GroundSurfaceColorBlendMode.None:
                    blended = color;
                    break;
                case GroundSurfaceColorBlendMode.Tint:
                    color = Color.Lerp(color, color * baseColor, blendStrength);
                    break;
                case GroundSurfaceColorBlendMode.Multiply:
                    blended = color * baseColor;
                    blended.a = 1f;
                    color = Color.Lerp(color, blended, blendStrength);
                    break;
                case GroundSurfaceColorBlendMode.Overlay:
                    blended = ApplyOverlayBlend(color, baseColor);
                    color = Color.Lerp(color, blended, blendStrength);
                    break;
                case GroundSurfaceColorBlendMode.Additive:
                    blended = new Color(
                        Mathf.Clamp01(color.r + baseColor.r * blendStrength),
                        Mathf.Clamp01(color.g + baseColor.g * blendStrength),
                        Mathf.Clamp01(color.b + baseColor.b * blendStrength),
                        1f);
                    color = blended;
                    break;
            }
        }

        color = AdjustSaturation(color, Mathf.Max(0f, material.saturation));
        color.r = Mathf.Clamp01((color.r - 0.5f) * Mathf.Max(0f, material.contrast) + 0.5f);
        color.g = Mathf.Clamp01((color.g - 0.5f) * Mathf.Max(0f, material.contrast) + 0.5f);
        color.b = Mathf.Clamp01((color.b - 0.5f) * Mathf.Max(0f, material.contrast) + 0.5f);
        color.r = Mathf.Clamp01(color.r * Mathf.Max(0f, material.brightness));
        color.g = Mathf.Clamp01(color.g * Mathf.Max(0f, material.brightness));
        color.b = Mathf.Clamp01(color.b * Mathf.Max(0f, material.brightness));
        color.a = 1f;
        return color;
    }

    private Color ApplyOverlayBlend(Color baseColor, Color blendColor)
    {
        return new Color(
            OverlayChannel(baseColor.r, blendColor.r),
            OverlayChannel(baseColor.g, blendColor.g),
            OverlayChannel(baseColor.b, blendColor.b),
            1f);
    }

    private float OverlayChannel(float baseValue, float blendValue)
    {
        return baseValue < 0.5f
            ? 2f * baseValue * blendValue
            : 1f - 2f * (1f - baseValue) * (1f - blendValue);
    }

    private Color AdjustSaturation(Color color, float saturation)
    {
        float luma = color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        color.r = Mathf.Clamp01(Mathf.Lerp(luma, color.r, saturation));
        color.g = Mathf.Clamp01(Mathf.Lerp(luma, color.g, saturation));
        color.b = Mathf.Clamp01(Mathf.Lerp(luma, color.b, saturation));
        color.a = 1f;
        return color;
    }

    private float Repeat01(float value)
    {
        return value - Mathf.Floor(value);
    }

    private float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private Color ApplySmartSurfaceOverlay(Color color, float u, float v, float edgeAmount)
    {
        color.a = 1f;

        if (!enableSmartSurfaceComposition)
            return color;

        if (smartSurfaceVariationStrength > 0f)
        {
            float n = Fbm01(u * smartSurfaceVariationScale + 5.23f, v * smartSurfaceVariationScale + 41.91f);
            float variation = (n - 0.5f) * 2f * smartSurfaceVariationStrength;
            color.r = Mathf.Clamp01(color.r + variation);
            color.g = Mathf.Clamp01(color.g + variation);
            color.b = Mathf.Clamp01(color.b + variation);
        }

        if (smartOverlayStrength > 0f)
        {
            float n = Fbm01(u * smartOverlayNoiseScale + 103.7f, v * smartOverlayNoiseScale + 9.11f);
            float mask = Mathf.SmoothStep(0.35f, 0.92f, n);
            float strength = Mathf.Clamp01(smartOverlayStrength) * mask;
            // 边缘叠加也要噪声门控，避免沿笔刷边界形成连续暗圈。
            float edgeMask = Mathf.SmoothStep(0.60f, 0.95f, Fbm01(u * smartOverlayNoiseScale + 217.3f, v * smartOverlayNoiseScale + 71.4f));
            strength = Mathf.Clamp01(strength + edgeAmount * smartOverlayStrength * 0.12f * edgeMask);
            Color overlay = smartOverlayColor;
            overlay.a = 1f;
            color = Color.Lerp(color, Color.Lerp(color * overlay, overlay, 0.18f), strength);
            color.a = 1f;
        }

        return color;
    }

    private float Fbm01(float x, float y)
    {
        float v = 0f;
        float amp = 0.5f;
        float freq = 1f;
        float total = 0f;
        for (int i = 0; i < 3; i++)
        {
            v += ValueNoise01(x * freq, y * freq) * amp;
            total += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return total > 0f ? v / total : 0.5f;
    }

    private float ValueNoise01(float x, float y)
    {
        int xi = Mathf.FloorToInt(x);
        int yi = Mathf.FloorToInt(y);
        float xf = x - xi;
        float yf = y - yi;
        xf = xf * xf * (3f - 2f * xf);
        yf = yf * yf * (3f - 2f * yf);

        float a = Hash01(xi, yi);
        float b = Hash01(xi + 1, yi);
        float c = Hash01(xi, yi + 1);
        float d = Hash01(xi + 1, yi + 1);
        float x1 = Mathf.Lerp(a, b, xf);
        float x2 = Mathf.Lerp(c, d, xf);
        return Mathf.Lerp(x1, x2, yf);
    }

    private float Hash01(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0x00FFFFFF) / 16777215f;
        }
    }

    private int ColorToHash(Color c)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + Mathf.RoundToInt(c.r * 10000f);
            h = h * 31 + Mathf.RoundToInt(c.g * 10000f);
            h = h * 31 + Mathf.RoundToInt(c.b * 10000f);
            h = h * 31 + Mathf.RoundToInt(c.a * 10000f);
            return h;
        }
    }

    private Texture ResolveSurfaceTextureForHashOnly(GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return null;

        Texture tierTexture = ResolveSurfaceTextureForCurrentRenderTier(material);
        if (tierTexture != null)
            return tierTexture;

        Material mat = material.baseMaterial;
        if (mat == null)
            return null;

        if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null)
            return mat.GetTexture("_BaseMap");

        if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null)
            return mat.GetTexture("_MainTex");

        return null;
    }

    private bool TryPrepareReadableTextureForLitBake(Texture2D texture)
    {
        if (texture == null)
            return false;

        if (texture == Texture2D.whiteTexture || texture == Texture2D.blackTexture || texture == Texture2D.grayTexture || texture == Texture2D.normalTexture)
            return true;

#if UNITY_EDITOR
        if (!SuppressAutomaticTextureImporterChanges && (autoMakeSurfaceTexturesReadableForLitBake || autoEnableMipMapsForLitBakeTextures) && !Application.isPlaying)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(path))
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    bool changed = false;
                    if (autoMakeSurfaceTexturesReadableForLitBake && !importer.isReadable)
                    {
                        importer.isReadable = true;
                        changed = true;
                    }

                    if (autoEnableMipMapsForLitBakeTextures && !importer.mipmapEnabled)
                    {
                        importer.mipmapEnabled = true;
                        changed = true;
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                        runtimeLitBakeHash = 0;
                        forceRebakeLitGroundTexture = true;
                    }
                }
            }
        }
#endif

        try
        {
            return texture.isReadable;
        }
        catch
        {
            return false;
        }
    }

    public void ApplyNormalDisplayToGroundVisual()
    {
        // 正常游戏显示现在以 URP/Lit 烘焙输出为唯一正式通道。
        // 旧的自定义 SurfaceIndex/Preview 写材质路径只保留给关闭 shadow-safe 输出时排查。
        if (useUrpLitShadowSafeOutput)
        {
            ApplyShadowSafeLitGroundToGroundVisual();
            return;
        }

        ApplyGroundShapeMaskToGroundVisual();

        if (groundVisualRoot == null)
            return;

        // 正常游戏显示优先走真实地表材质纹理；没有材质图时退回默认地表材质。
        if (previewSurfaceMaterialOnGroundVisual && surfaceMaterialIndexMap != null && surfaceMaterialPalette != null && surfaceMaterialPalette.Count > 0)
        {
            ApplySurfaceMaterialsToGroundVisual();
            return;
        }

        Texture normalTexture = ResolveNormalBaseTexture();
        Color normalColor = defaultSurfaceMaterial != null ? defaultSurfaceMaterial.baseColor : new Color(0.34f, 0.34f, 0.34f, 1f);
        normalColor.a = 1f;

        Renderer[] renderers = groundVisualRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;

            r.enabled = true;
            ConfigureGroundVisualRenderer(r);

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            if (normalTexture != null)
            {
                mpb.SetTexture("_BaseMap", normalTexture);
                mpb.SetTexture("_MainTex", normalTexture);
            }
            mpb.SetFloat("_UseSurfaceIndexMap", 0f);
            mpb.SetVector("_BaseMap_ST", ResolveRendererBaseMapST(r));
            mpb.SetColor("_BaseColor", normalColor);
            ApplyTextureStabilityProperties(mpb);
            r.SetPropertyBlock(mpb);
        }
    }

    public void ApplySurfaceMaterialsToGroundVisual()
    {
        // 重要：正常显示不能再回退到旧的 SurfaceIndexMap 自定义 Shader 路径，
        // 否则进入编辑/点击地表材质时会把 GroundVisual 写成另一套显示状态，
        // 造成贴图不同步、左下角旧缓存条纹、甚至看起来像两套地面在切换。
        if (useUrpLitShadowSafeOutput)
        {
            ApplyShadowSafeLitGroundToGroundVisual();
            return;
        }

        ApplyGroundShapeMaskToGroundVisual();

        if (groundVisualRoot == null)
            return;

        if (surfaceMaterialIndexMap == null)
        {
            ApplyNormalDisplayToGroundVisual();
            return;
        }

        EnsurePaletteHasDefaultMaterial();

        Renderer[] renderers = groundVisualRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;

            r.enabled = true;
            ConfigureGroundVisualRenderer(r);

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);

            mpb.SetTexture("_SurfaceIndexMap", surfaceMaterialIndexMap);
            mpb.SetFloat("_UseSurfaceIndexMap", 1f);
            mpb.SetVector("_BaseMap_ST", ResolveRendererBaseMapST(r));
            mpb.SetColor("_BaseColor", Color.white);
            ApplyTextureStabilityProperties(mpb);

            for (int i = 0; i < 8; i++)
            {
                GroundSurfaceMaterialDefinition material = ResolvePaletteMaterial(i);
                bool hasTexture = HasSurfaceTexture(material);
                Texture texture = ResolveSurfaceTexture(material);
                Color color = ResolveSurfaceColor(material);

                mpb.SetTexture($"_SurfaceTex{i}", texture != null ? texture : Texture2D.whiteTexture);
                mpb.SetColor($"_SurfaceColor{i}", color);
                mpb.SetFloat($"_SurfaceHasTexture{i}", hasTexture ? 1f : 0f);
            }

            r.SetPropertyBlock(mpb);
        }
    }

    public void ApplySurfacePreviewToGroundVisual()
    {
        if (useUrpLitShadowSafeOutput)
        {
            ApplyShadowSafeLitGroundToGroundVisual();
            return;
        }

        ApplyGroundShapeMaskToGroundVisual();

        if (!previewSurfaceMaterialOnGroundVisual || surfaceMaterialPreviewTexture == null || groundVisualRoot == null)
            return;

        Renderer[] renderers = groundVisualRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;

            r.enabled = true;
            ConfigureGroundVisualRenderer(r);

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetTexture("_BaseMap", surfaceMaterialPreviewTexture);
            mpb.SetTexture("_MainTex", surfaceMaterialPreviewTexture);
            // SurfaceMaterialPreview 是整张 GroundBlock 的数据图，必须用原始 0-1 UV，不能继承地表贴图 tiling。
            mpb.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            mpb.SetColor("_BaseColor", Color.white);
            r.SetPropertyBlock(mpb);
        }
    }


    private void EnsurePaletteHasDefaultMaterial()
    {
        if (surfaceMaterialPalette == null)
            surfaceMaterialPalette = new List<GroundSurfaceMaterialDefinition>();

        if (surfaceMaterialPalette.Count == 0)
            surfaceMaterialPalette.Add(defaultSurfaceMaterial);
        else if (surfaceMaterialPalette[0] == null && defaultSurfaceMaterial != null)
            surfaceMaterialPalette[0] = defaultSurfaceMaterial;
    }

    private GroundSurfaceMaterialDefinition ResolvePaletteMaterial(int index)
    {
        if (surfaceMaterialPalette != null && index >= 0 && index < surfaceMaterialPalette.Count)
        {
            GroundSurfaceMaterialDefinition material = surfaceMaterialPalette[index];
            if (material != null)
                return material;
        }

        return defaultSurfaceMaterial;
    }

    private void ApplyTextureStabilityProperties(MaterialPropertyBlock mpb)
    {
        if (mpb == null)
            return;

        float start = Mathf.Max(0.001f, detailFadeStart);
        float end = Mathf.Max(start + 0.001f, detailFadeEnd);
        mpb.SetFloat("_TextureAntiShimmer", Mathf.Clamp01(textureAntiShimmer));
        mpb.SetFloat("_DetailFadeStart", start);
        mpb.SetFloat("_DetailFadeEnd", end);
        mpb.SetFloat("_SurfaceTextureStrength", Mathf.Clamp01(surfaceTextureStrength));
        mpb.SetFloat("_SurfaceTextureWeight", Mathf.Clamp01(surfaceTextureWeight));
        mpb.SetFloat("_SurfaceColorTintStrength", Mathf.Clamp01(surfaceColorTintStrength));
        mpb.SetFloat("_FallbackColorStrength", Mathf.Clamp01(fallbackColorStrength));
        mpb.SetFloat("_SurfaceMipBias", Mathf.Clamp(surfaceMipBias, 0f, 8f));
        mpb.SetFloat("_ReceiveShadowStrength", Mathf.Clamp01(groundReceiveShadowStrength));
    }


    private float ResolveEffectiveSurfaceTextureWorldSize(GroundSurfaceMaterialDefinition material)
    {
        float baseWorldSize = surfaceTextureWorldSize;
        if (material != null && material.textureWorldSize > 0.001f)
            baseWorldSize = material.textureWorldSize;

        float visualScale = Mathf.Clamp(surfaceTextureVisualScale, 0.125f, 2f);
        return Mathf.Max(0.05f, baseWorldSize * visualScale);
    }

    private float GetEffectiveSurfaceTextureTilingMultiplier()
    {
        return Mathf.Max(0.01f, surfaceTextureTilingMultiplier);
    }

    private Texture StabilizeSurfaceTexture(Texture texture)
    {
        if (texture == null)
            return null;

        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = runtimeSurfaceTextureFilter;
        texture.anisoLevel = Mathf.Clamp(runtimeSurfaceTextureAniso, 0, 16);
        return texture;
    }

    private bool HasSurfaceTexture(GroundSurfaceMaterialDefinition material)
    {
        return ResolveSurfaceTexture(material) != null && ResolveSurfaceTexture(material) != Texture2D.whiteTexture;
    }

    private Texture ResolveSurfaceTexture(GroundSurfaceMaterialDefinition material)
    {
        if (material != null)
        {
            Texture tierTexture = ResolveSurfaceTextureForCurrentRenderTier(material);
            if (tierTexture != null)
                return StabilizeSurfaceTexture(tierTexture);

            Material mat = material.baseMaterial;
            if (mat != null)
            {
                if (mat.HasProperty("_BaseMap"))
                {
                    Texture t = mat.GetTexture("_BaseMap");
                    if (t != null)
                        return StabilizeSurfaceTexture(t);
                }

                if (mat.HasProperty("_MainTex"))
                {
                    Texture t = mat.GetTexture("_MainTex");
                    if (t != null)
                        return StabilizeSurfaceTexture(t);
                }
            }
        }

        return Texture2D.whiteTexture;
    }

    private Texture ResolveSurfaceTextureForCurrentRenderTier(GroundSurfaceMaterialDefinition material)
    {
        if (material == null)
            return null;

        switch (SkyPrisonRenderQualityContext.CurrentTier)
        {
            case SkyPrisonRenderQualityTier.Safe:
                if (material.safePreviewTexture != null) return material.safePreviewTexture;
                if (material.editPreviewTexture != null) return material.editPreviewTexture;
                if (material.runtimePreviewTexture != null) return material.runtimePreviewTexture;
                if (material.baseTexture != null) return material.baseTexture;
                if (material.finalBakeTexture != null) return material.finalBakeTexture;
                break;

            case SkyPrisonRenderQualityTier.EditPreview:
                if (material.editPreviewTexture != null) return material.editPreviewTexture;
                if (material.runtimePreviewTexture != null) return material.runtimePreviewTexture;
                if (material.baseTexture != null) return material.baseTexture;
                if (material.safePreviewTexture != null) return material.safePreviewTexture;
                if (material.finalBakeTexture != null) return material.finalBakeTexture;
                break;

            case SkyPrisonRenderQualityTier.RuntimePreview:
                if (material.runtimePreviewTexture != null) return material.runtimePreviewTexture;
                if (material.editPreviewTexture != null) return material.editPreviewTexture;
                if (material.baseTexture != null) return material.baseTexture;
                if (material.finalBakeTexture != null) return material.finalBakeTexture;
                if (material.safePreviewTexture != null) return material.safePreviewTexture;
                break;

            case SkyPrisonRenderQualityTier.Final:
                if (material.finalBakeTexture != null) return material.finalBakeTexture;
                if (material.baseTexture != null) return material.baseTexture;
                if (material.runtimePreviewTexture != null) return material.runtimePreviewTexture;
                if (material.editPreviewTexture != null) return material.editPreviewTexture;
                if (material.safePreviewTexture != null) return material.safePreviewTexture;
                break;
        }

        return material.baseTexture;
    }

    private Color ResolveSurfaceColor(GroundSurfaceMaterialDefinition material)
    {
        Color color = material != null ? material.baseColor : new Color(0.45f, 0.45f, 0.45f, 1f);
        color.a = 1f;
        return color;
    }

    private Vector4 ResolveRendererBaseMapST(Renderer renderer)
    {
        if (renderer == null || renderer.sharedMaterial == null)
            return new Vector4(1f, 1f, 0f, 0f);

        Material mat = renderer.sharedMaterial;
        string prop = mat.HasProperty("_BaseMap") ? "_BaseMap" : (mat.HasProperty("_MainTex") ? "_MainTex" : null);
        if (string.IsNullOrEmpty(prop))
            return new Vector4(1f, 1f, 0f, 0f);

        Vector2 offset = mat.GetTextureOffset(prop);

        if (useStableSurfaceTextureWorldTiling)
        {
            float worldSize = Mathf.Max(0.05f, surfaceTextureWorldSize * Mathf.Clamp(surfaceTextureVisualScale, 0.125f, 2f));
            float multiplier = GetEffectiveSurfaceTextureTilingMultiplier();
            float repeatX = Mathf.Max(0.01f, Mathf.Abs(mapBoundsSize.x) / worldSize * multiplier);
            float repeatZ = Mathf.Max(0.01f, Mathf.Abs(mapBoundsSize.z) / worldSize * multiplier);
            return new Vector4(repeatX, repeatZ, offset.x, offset.y);
        }

        Vector2 scale = mat.GetTextureScale(prop);
        return new Vector4(scale.x, scale.y, offset.x, offset.y);
    }

    private Texture ResolveNormalBaseTexture()
    {
        if (defaultSurfaceMaterial != null)
        {
            if (defaultSurfaceMaterial.baseTexture != null)
                return defaultSurfaceMaterial.baseTexture;

            Material mat = defaultSurfaceMaterial.baseMaterial;
            if (mat != null)
            {
                if (mat.HasProperty("_BaseMap"))
                {
                    Texture t = mat.GetTexture("_BaseMap");
                    if (t != null)
                        return StabilizeSurfaceTexture(t);
                }

                if (mat.HasProperty("_MainTex"))
                {
                    Texture t = mat.GetTexture("_MainTex");
                    if (t != null)
                        return StabilizeSurfaceTexture(t);
                }
            }
        }

        return Texture2D.whiteTexture;
    }

    public void ApplyGroundShapeMaskToGroundVisual()
    {
        if (groundShapeMask == null || groundVisualRoot == null)
            return;

        Renderer[] renderers = groundVisualRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;

            r.enabled = true;
            ConfigureGroundVisualRenderer(r);

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            // 兼容不同 Shader 命名。正式 Mask Shader 后面可以只保留标准名.
            mpb.SetTexture("_GroundShapeMask", groundShapeMask);
            mpb.SetTexture("_GroundMask", groundShapeMask);
            mpb.SetTexture("_MaskTex", groundShapeMask);
            mpb.SetTexture("_AlphaMask", groundShapeMask);
            mpb.SetFloat("_MaskThreshold", groundMaskThreshold);
            r.SetPropertyBlock(mpb);
        }
    }


    private void ValidateSurfaceTextureScaleDirtyFlag()
    {
        bool changed =
            !Mathf.Approximately(cachedSurfaceTextureWorldSize, surfaceTextureWorldSize) ||
            !Mathf.Approximately(cachedSurfaceTextureTilingMultiplier, surfaceTextureTilingMultiplier) ||
            cachedUseStableSurfaceTextureWorldTiling != useStableSurfaceTextureWorldTiling ||
            !Mathf.Approximately(cachedSurfaceTextureVisualScale, surfaceTextureVisualScale) ||
            cachedEnableSurfaceTextureAntiRepeat != enableSurfaceTextureAntiRepeat ||
            !Mathf.Approximately(cachedSurfaceTextureAntiRepeatStrength, surfaceTextureAntiRepeatStrength) ||
            !Mathf.Approximately(cachedSurfaceTextureAntiRepeatWorldSize, surfaceTextureAntiRepeatWorldSize) ||
            !Mathf.Approximately(cachedSurfaceTextureAntiRepeatUvOffset, surfaceTextureAntiRepeatUvOffset) ||
            !Mathf.Approximately(cachedSurfaceTextureAntiRepeatToneJitter, surfaceTextureAntiRepeatToneJitter);

        cachedSurfaceTextureWorldSize = surfaceTextureWorldSize;
        cachedSurfaceTextureTilingMultiplier = surfaceTextureTilingMultiplier;
        cachedUseStableSurfaceTextureWorldTiling = useStableSurfaceTextureWorldTiling;
        cachedSurfaceTextureVisualScale = surfaceTextureVisualScale;
        cachedEnableSurfaceTextureAntiRepeat = enableSurfaceTextureAntiRepeat;
        cachedSurfaceTextureAntiRepeatStrength = surfaceTextureAntiRepeatStrength;
        cachedSurfaceTextureAntiRepeatWorldSize = surfaceTextureAntiRepeatWorldSize;
        cachedSurfaceTextureAntiRepeatUvOffset = surfaceTextureAntiRepeatUvOffset;
        cachedSurfaceTextureAntiRepeatToneJitter = surfaceTextureAntiRepeatToneJitter;

        if (!changed)
            return;

        needsRuntimeBake = true;
        runtimeLitBakeHash = 0;
        forceRebakeLitGroundTexture = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (surfaceMaterialPalette == null)
            surfaceMaterialPalette = new List<GroundSurfaceMaterialDefinition>();

        if (defaultSurfaceMaterial != null && surfaceMaterialPalette.Count == 0)
            surfaceMaterialPalette.Add(defaultSurfaceMaterial);

        litBakedTextureResolution = Mathf.Clamp(litBakedTextureResolution, 128, 4096);
        maxAutoLitBakedTextureResolution = Mathf.Clamp(maxAutoLitBakedTextureResolution, 512, 4096);

        ValidateSurfaceTextureScaleDirtyFlag();
        RefreshGroundVisualRuntime();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireCube(mapBoundsCenter, mapBoundsSize);
    }
#endif
}
