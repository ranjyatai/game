using System.Collections.Generic;
using UnityEngine;

public enum GroundSurfaceTextureDistributionMode
{
    [InspectorName("循环散布纹理")]
    SeamlessTiling = 0,

    [InspectorName("随机散布纹理")]
    RandomScatter = 1,

    [InspectorName("整张大图")]
    SingleLarge = 2,

    [InspectorName("印章 / 贴花")]
    StampDecal = 3,

    [InspectorName("样条图案")]
    SplinePattern = 4
}

public enum GroundSurfaceColorBlendMode
{
    [InspectorName("不合成")]
    None = 0,

    [InspectorName("普通染色")]
    Tint = 1,

    [InspectorName("乘算叠加")]
    Multiply = 2,

    [InspectorName("Overlay 叠加")]
    Overlay = 3,

    [InspectorName("加算")]
    Additive = 4
}

public enum GroundSurfaceOverlaySizeMode
{
    [InspectorName("跟随笔刷")]
    FollowBrush = 0,

    [InspectorName("固定素材尺寸")]
    FixedMaterialSize = 1
}

public enum GroundSurfaceOverlayLayerSlot
{
    [InspectorName("底层覆盖 / Underlay")] Underlay = 0,
    [InspectorName("地表细节 / Surface")] Surface = 1,
    [InspectorName("标线文字 / Marking")] Marking = 2,
    [InspectorName("顶部侵蚀 / Top")] Top = 3,
}

public enum GroundSurfaceOverlayBlendMode
{
    [InspectorName("普通覆盖")] AlphaBlend = 0,
    [InspectorName("加算")] Additive = 1,
    [InspectorName("乘算")] Multiply = 2,
    [InspectorName("擦除")] Erase = 3,
}

[CreateAssetMenu(
    fileName = "GroundSurfaceMaterialDefinition",
    menuName = "Sky Prison/Ground Surface Material Definition",
    order = 1310)]
public class GroundSurfaceMaterialDefinition : ScriptableObject
{
    [Header("基础信息")]
    public string surfaceId = "new_ground_surface";
    public string displayName = "新地表材质";
    public string category = "基础地表";
    public bool isStandard = false;
    [Tooltip("仅作地面标签：不是用来刷地面的纹理，只给装饰物的「可站立表面」/ 地面标记提供\n" +
             "脚步声、摩擦、噪声这些规则（例如「金属」只给铁轨用）。\n" +
             "勾上后不出现在放置工具的地表笔刷列表里；编辑页里归到最底部默认折叠的分组。")]
    public bool surfaceTagOnly = false;
    [TextArea(2, 5)] public string note = "";

    [Header("视觉")]
    public Color baseColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    public Texture2D baseTexture;

    [Header("兼容旧字段：四档渲染模拟贴图（已从主页面移除）")]
    [HideInInspector] public Texture2D safePreviewTexture;
    [HideInInspector] public Texture2D editPreviewTexture;
    [HideInInspector] public Texture2D runtimePreviewTexture;
    [HideInInspector] public Texture2D finalBakeTexture;
    public Material baseMaterial;

    [Header("Terrain 兼容字段")]
    public bool useAsTerrainSurface = true;
    public TerrainLayer terrainLayer;
    public TerrainLayer terrainLayerTemplate;
    public Material advancedMaterialTemplate;
    public bool useAsOverlayStamp = false;
    public bool useAsSplinePattern = false;
    public bool useAsGroundOverlay = false;

    [Header("兼容 / 可选预览")]
    public Sprite previewIcon;

    [Header("纹理散布")]
    public GroundSurfaceTextureDistributionMode textureDistributionMode = GroundSurfaceTextureDistributionMode.SeamlessTiling;
    [Min(0.01f)] public float textureWorldSize = 4f;
    [Min(0.01f)] public float randomScaleMin = 0.9f;
    [Min(0.01f)] public float randomScaleMax = 1.15f;
    [Range(0f, 1f)] public float randomOffsetStrength = 1f;
    public bool allowRotate90 = true;
    public bool allowFlipX = true;
    public bool allowFlipY = false;

    [Header("印章 / 贴花定义")]
    public Texture2D stampTexture;
    public Vector2 stampWorldSize = new Vector2(1f, 1f);
    [Range(0f, 1f)] public float stampOpacity = 1f;
    [HideInInspector] public GroundSurfaceOverlayBlendMode stampBlendMode = GroundSurfaceOverlayBlendMode.AlphaBlend;
    public bool stampCanRotate = true;
    public bool stampCanScale = true;
    public bool stampOverridesSurfaceType = false;

    [Header("样条图案定义")]
    public Texture2D splineTexture;
    [Min(0.01f)] public float splineWorldWidth = 0.55f;
    [Min(0.01f)] public float splineSegmentWorldLength = 1f;
    [Min(0.01f)] public float splineStampSpacing = 0.1f;
    [Range(0f, 1f)] public float splineOpacity = 1f;
    public bool splineFollowBrushDirection = true;
    public bool splineContinuous = true;
    [Range(0f, 1f)] public float splineAngleSmoothing = 0.45f;

    [Header("样条图案蒙版 / 破损")]
    public bool splineMaskEnabled = false;
    public Texture2D splineMaskTexture;
    [Range(0f, 1f)] public float splineMaskStrength = 1f;
    [Range(0f, 1f)] public float splineMaskThreshold = 0.45f;
    [Range(0.001f, 0.5f)] public float splineMaskSoftness = 0.08f;
    [Min(0.01f)] public float splineMaskWorldSize = 3f;
    public bool splineMaskInvert = false;
    public Vector2 splineMaskOffset = Vector2.zero;

    [Header("Overlay / 样条尺寸规则")]
    public GroundSurfaceOverlaySizeMode overlaySizeMode = GroundSurfaceOverlaySizeMode.FollowBrush;
    public bool lockOverlaySize = false;
    public Vector2 fixedOverlayWorldSize = new Vector2(1f, 1f);
    [Min(0.01f)] public float fixedSplineWorldWidth = 0.55f;

    [Header("兼容旧字段：不要在新逻辑里主动使用")]
    [HideInInspector] public bool lockOverlayWorldSize = false;
    [HideInInspector] public Vector2 lockedOverlayWorldSize = new Vector2(1f, 1f);
    [HideInInspector] public bool lockSplineWorldWidth = false;
    [HideInInspector] public float lockedSplineWorldWidth = 0.55f;

    [Header("反重复采样 / 随机散布")]
    public bool antiRepeatEnabled = true;
    [Range(0f, 1f)] public float antiRepeatStrength = 0.62f;
    [Min(0.25f)] public float antiRepeatWorldSize = 12f;
    [Range(0f, 1f)] public float antiRepeatUvOffset = 0.72f;
    [Range(0f, 0.2f)] public float antiRepeatToneJitter = 0.045f;

    [Header("纹理变体")]
    public List<Texture2D> textureVariants = new List<Texture2D>();
    [Range(0f, 1f)] public float variantBlendStrength = 0.65f;
    [Range(0f, 1f)] public float stochasticBlendStrength = 0.45f;

    [Header("宏观变化")]
    public Texture2D macroVariationTexture;
    [Range(0f, 1f)] public float macroVariationStrength = 0.18f;
    [Min(0.01f)] public float macroVariationWorldSize = 18f;

    [Header("颜色合成")]
    public GroundSurfaceColorBlendMode baseColorBlendMode = GroundSurfaceColorBlendMode.Multiply;
    [Range(0f, 1f)] public float baseColorBlendStrength = 0.35f;
    [Range(0f, 2f)] public float brightness = 1f;
    [Range(0f, 2f)] public float contrast = 1f;
    [Range(0f, 2f)] public float saturation = 1f;

    [Header("虚空")]
    [Tooltip("虚空地表：刷到地形上的地方地面整块消失（逐像素裁剪，边缘是平滑曲线，不像 Terrain 挖洞那样有锯齿台阶），\n" +
             "角色不能走上去（移动系统把它当墙）。没有脚步声——不挂音效包、地面标签保持「未指定」。\n\n" +
             "只去掉画面，Terrain 碰撞还在：需要物体真正掉下去的地方仍用挖洞工具。\n" +
             "首次用放置工具刷时自动给地形挂 SkyPrisonTerrainGrateBinder 并换格栅版地形着色器。\n" +
             "和「格栅镂空」互斥，一张地形同一时间只支持一种虚空材质。")]
    public bool isVoid = false;

    [Header("格栅镂空")]
    [Tooltip("刷到地形上的地方变成金属格栅：按格栅图案镂空，透出地形下方的建筑群。\n\n" +
             "只镂空视觉，碰撞不变——角色照常站在上面走。\n" +
             "首次用放置工具刷这个材质时，会自动给地形挂 SkyPrisonTerrainGrateBinder 并换成格栅版地形着色器。\n" +
             "一张地形同一时间只支持一种格栅材质。")]
    public bool seeThroughGrate = false;
    [Tooltip("格子间距（米）：相邻两根栅条中心之间的距离。")]
    [Min(0.05f)] public float grateCellSize = 0.5f;
    [Tooltip("栅条宽度（米）。等于格子间距时就是实心地面。")]
    [Min(0.005f)] public float grateBarWidth = 0.08f;
    [Tooltip("镂空遮罩：白 = 实心栅条，黑 = 镂空。\n" +
             "要和「基础纹理」是同一套像素布局（同尺寸、栅条在同一位置）——遮罩按这个地形层自己的\n" +
             "平铺尺寸采样，镂空位置和颜色贴图上画的栅条逐像素对齐。\n" +
             "不能直接用颜色贴图的 Alpha：Textures/Ground/Surface/ 下的地表贴图导入时会被强制去掉\n" +
             "Alpha（URP 地形把 diffuse 的 Alpha 当光滑度），所以遮罩单独一张黑白图，放 Textures/Ground/Mask/。\n" +
             "留空则用上面的格子间距 / 栅条宽度程序化生成（白模阶段用）。")]
    public Texture2D grateMaskTexture;
    [Tooltip("不勾（默认）读 R 通道——黑白遮罩；勾选读 Alpha 通道——遮罩贴图本身带透明孔洞时用。")]
    public bool grateMaskUseAlpha = false;
    [Tooltip("遮罩值低于这个就镂空。")]
    [Range(0.01f, 0.99f)] public float grateMaskThreshold = 0.5f;

    [Header("地面规则")]
    public GroundSurfaceType surfaceType = GroundSurfaceType.Default;
    [Min(0f)] public float friction = 1f;
    [Min(0f)] public float walkNoiseMultiplier = 1f;
    [Min(0f)] public float runNoiseMultiplier = 1.25f;
    [Min(0f)] public float sneakNoiseMultiplier = 0.55f;
    [Min(0f)] public float landingNoiseMultiplier = 1.35f;

    [Header("音声合成")]
    [Tooltip("正式结构：地表材质直接绑定自己的地表音声包，例如 AP_Surface_Grass。角色脚步声系统采到该地表后直接使用这个包，不再在角色身上维护 地表->音声包 映射表。")]
    public SkyPrisonAudioPackage surfaceAudioPackage;

    [Tooltip("音声包内部运行层 Key，例如 surface_grass。脚步 / 落地 / 滑步由音声合成器内部按 Spine 事件 / 音轨组合处理。")]
    public string audioRuntimeLayerKey = "";

    [Header("兼容旧字段：旧版脚步 / 落地 / 滑步分离 Key（新逻辑不主动显示）")]
    [HideInInspector] public string footstepAudioTag = "";
    [HideInInspector] public string landingAudioTag = "";
    [HideInInspector] public string slideAudioTag = "";

    public string EffectiveAudioRuntimeLayerKey
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(audioRuntimeLayerKey)) return audioRuntimeLayerKey;
            if (!string.IsNullOrWhiteSpace(footstepAudioTag)) return footstepAudioTag;
            if (!string.IsNullOrWhiteSpace(landingAudioTag)) return landingAudioTag;
            if (!string.IsNullOrWhiteSpace(slideAudioTag)) return slideAudioTag;
            return "";
        }
    }

    [Header("特效")]
    public GameObject defaultFootstepFx;
    public GameObject defaultLandingFx;

    // Deprecated compatibility only. Four-layer GroundOverlay has been retired from the main workflow.
    public bool IsOverlayMode => textureDistributionMode == GroundSurfaceTextureDistributionMode.StampDecal || textureDistributionMode == GroundSurfaceTextureDistributionMode.SplinePattern;
    public GroundSurfaceOverlayLayerSlot EffectiveOverlayLayerSlot => GroundSurfaceOverlayLayerSlot.Marking;


    public float EffectiveFixedSplineWorldWidth
    {
        get
        {
            if (fixedSplineWorldWidth > 0.001f) return fixedSplineWorldWidth;
            if (lockedSplineWorldWidth > 0.001f) return lockedSplineWorldWidth;
            if (splineWorldWidth > 0.001f) return splineWorldWidth;
            return 0.55f;
        }
    }

    public Vector2 EffectiveFixedOverlayWorldSize
    {
        get
        {
            if (fixedOverlayWorldSize.x > 0.001f && fixedOverlayWorldSize.y > 0.001f) return fixedOverlayWorldSize;
            if (lockedOverlayWorldSize.x > 0.001f && lockedOverlayWorldSize.y > 0.001f) return lockedOverlayWorldSize;
            if (stampWorldSize.x > 0.001f && stampWorldSize.y > 0.001f) return stampWorldSize;
            return Vector2.one;
        }
    }

    public bool UsesFixedOverlaySize
    {
        get
        {
            return overlaySizeMode == GroundSurfaceOverlaySizeMode.FixedMaterialSize
                   || lockOverlaySize
                   || lockOverlayWorldSize
                   || lockSplineWorldWidth;
        }
    }

    public static readonly int RoadLineStandardTextureWidth = 2048;
    public static readonly int RoadLineStandardTextureHeight = 1024;
    public static readonly int RoadLineStandardWidth = 2048;
    public static readonly int RoadLineStandardHeight = 1024;
}
