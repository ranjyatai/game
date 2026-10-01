using System;
using System.Collections.Generic;
using UnityEngine;

public enum TerrainDecorationCategory
{
    Prop = 0,
    Box = 1,
    Wall = 2,
    Pillar = 3,
    FloorAttachment = 4,
    Moss = 5,
    Ruin = 6,
    Pipe = 7,
    Occluder = 8,
    Mechanism = 9,
    Custom = 100,
}

public enum TerrainDecorationStructureTemplate
{
    StandardContainer = 0,
    VisualOnly = 1,
    BoxOccluder = 2,
    WallOccluder = 3,
    MossAttachment = 4,
    Custom = 100,
}

public enum TerrainDecorationFogMode
{
    AlwaysVisible = 0,
    DarkenInFog = 1,
    HideInFog = 2,
    RevealOnlyWhenSeen = 3,
}

public enum TerrainDecorationShadowMode
{
    None = 0,
    MeshRenderer = 1,
    ShadowCasterProxy = 2,
    ShadowsOnlyProxy = 3,
}

public enum TerrainDecorationOcclusionMode
{
    None = 0,
    FrontBack = 1,
    FadeWhenBlockingPlayer = 2,
    FrontBackAndFade = 3,
}

public enum TerrainDecorationCollisionMode
{
    None = 0,
    Box = 1,
    Mesh = 2,
    CustomRoot = 3,
}

public enum TerrainDecorationFrontOccluderProxyMode
{
    None = 0,
    BoxProxy = 1,
    ModelProxy = 2,
    ManualPrefab = 3,
}

public enum TerrainDecorationFrontBackPlaneMode
{
    ManualAnchors = 0,
    CollisionBounds = 1,
    ContainerBounds = 2,
}

public enum TerrainDecorationPlacementCollisionMode
{
    None = 0,
    VisualOnly = 1,
    BlockPlacement = 2,
    BlockUnits = 3,
    BlockEverything = 4,
}

[Serializable]
public class TerrainDecorationMaterialSlot
{
    public string slotId = "main";
    public string displayName = "主体材质";
    public string rendererPath = "VisualRoot/Visual_01";
    public int materialIndex = 0;
    public Material defaultMaterial;
    public List<Material> allowedMaterials = new List<Material>();
}

[Serializable]
public class TerrainDecorationVariant
{
    public string variantId = "default";
    public string displayName = "默认版本";
    public GameObject prefab;
    public int weight = 1;
    public Sprite previewIcon;
    public List<TerrainDecorationMaterialSlot> materialSlots = new List<TerrainDecorationMaterialSlot>();
}

[CreateAssetMenu(
    fileName = "TerrainDecorationDefinition",
    menuName = "Sky Prison/Terrain Decoration Definition",
    order = 1310)]
public class TerrainDecorationDefinition : ScriptableObject
{
    [Header("Identity")]
    public string decorationId = "new_terrain_decoration";
    public string displayName = "新地形装饰物";
    public TerrainDecorationCategory category = TerrainDecorationCategory.Prop;
    public string subCategory = "Default";
    public List<string> tags = new List<string>();
    public Sprite icon;
    [TextArea(2, 4)] public string note = "";
    public bool isStandard = false;

    [Tooltip("白模（关卡体量占位几何体）。\n\n" +
             "勾上后它不出现在放置工具的「地形装饰物」模块，只出现在「白模」模块；" +
             "放置时固定进 WorldRoot/BackgroundRoot/GrayboxRoot，方便整体隐藏/停用，" +
             "不影响实物。")]
    public bool isGraybox = false;

    [Header("Visual Variants")]
    public List<TerrainDecorationVariant> variants = new List<TerrainDecorationVariant>();
    public bool randomVariantOnPlace = false;
    public bool randomVariantByWeight = true;
    public bool randomMaterialOnPlace = false;

    [Header("Structure")]
    public TerrainDecorationStructureTemplate structureTemplate = TerrainDecorationStructureTemplate.StandardContainer;
    public bool autoEnsureStandardStructure = true;
    public bool repairMissingNodesOnly = true;

    [Header("Placement")]
    public bool allowMove = true;
    public bool allowRotate = true;
    public bool allowScale = true;
    public bool snapToGrid = true;
    public TerrainDecorationPlacementCollisionMode placementCollisionMode = TerrainDecorationPlacementCollisionMode.BlockPlacement;
    public bool allowVisualOverlap = true;
    public bool allowCollisionOverlap = false;
    public Vector3 defaultPlacementRotation = Vector3.zero;
    public Vector3 defaultScale = Vector3.one;
    public Vector2 footprintSize = Vector2.one;

    [Header("Random Scale")]
    public bool enableRandomScale = false;
    public bool uniformRandomScale = true;
    public Vector3 randomScaleMin = Vector3.one;
    public Vector3 randomScaleMax = Vector3.one;

    [Header("Visual Random Rotation")]
    public bool enableVisualRandomRotation = false;
    public Vector3 visualRandomRotationMin = Vector3.zero;
    public Vector3 visualRandomRotationMax = Vector3.zero;
    public bool visualRandomRotationAffectsRules = false;

    [Tooltip("列表排序优先级。数字越小越靠前，相同则按显示名字母序。\n" +
             "空气墙这类高频摆放的工具类物件设成负数，正常美术资产保持 0。")]
    public int sortPriority = 0;

    [Tooltip("视觉只在编辑器里可见——空气墙用。\n\n" +
             "勾上之后 VisualRoot 会留在 Default(0) 层而不是 World3D。本项目四台相机的 " +
             "cullingMask 都不含第 0 位（Main=World3D+Character2D，GamePlay=UI+FogOfWar，" +
             "OcclusionMask=OcclusionMask，OverheadUI=OverheadUI），所以游戏里一帧都不会渲染，" +
             "而 Scene 视图有自己的层显示开关、照常可见。\n\n" +
             "同时碰撞盒改为贴合视觉的包围盒——视觉存在的意义就是把体积画出来，" +
             "两者必须一致，所以这里不再读碰撞 Size / Offset。")]
    public bool editorOnlyVisual = false;

    [Header("Collision")]
    public TerrainDecorationCollisionMode collisionMode = TerrainDecorationCollisionMode.Box;
    public Vector3 collisionSize = new Vector3(1f, 1f, 1f);
    public Vector3 collisionOffset = new Vector3(0f, 0.5f, 0f);
    // blockPlayer / blockEnemy 现在始终同步，由「阻挡单位」一个开关同时写入。
    //
    // 拆成两个字段是历史设计，但全部 35 个定义里这两个值从来没有不同过——
    // 没有一处需要「挡玩家不挡敌人」。而阻挡是按 LayerMask 判定的，真要区分就得给
    // 「只挡玩家」「只挡敌人」各开一个层，还要给玩家和敌人预制体配不同的 mask，
    // 为一个零需求的能力占掉两个层槽，不划算。
    //
    // 字段保留不动（改序列化要迁移 35 个资产，风险大于收益），读的时候一律走
    // BlocksUnits。将来真出现需求时数据结构还在，直接拆开即可。
    public bool blockPlayer = true;
    public bool blockEnemy = true;
    public bool blockVision = false;
    public bool blockProjectile = false;

    /// <summary>是否阻挡单位移动。读这个，不要单独读 blockPlayer / blockEnemy。</summary>
    public bool BlocksUnits => blockPlayer || blockEnemy;

    /// <summary>
    /// 「挡单位、但不挡子弹」的碰撞体所在层。
    ///
    /// 挡两者的留在 World3D，谁都不挡的走 WalkableProbe，只差这一档需要单独的层：
    /// 子弹的阻挡判定是按层白名单做的，这一层不在名单里，所以子弹穿过去、角色被挡住。
    /// </summary>
    public const string UnitOnlyObstacleLayerName = "Obstacle_UnitOnly";

    /// <summary>
    /// 把「挡不挡单位 / 挡不挡子弹」映射到碰撞体应该在的层。
    ///
    ///   挡单位 + 挡子弹   -> World3D（默认，和一切既有几何体一致）
    ///   挡单位 + 不挡子弹 -> Obstacle_UnitOnly
    ///   都不挡            -> WalkableProbe（仍能被地表查询射线打中，用来出脚步声）
    ///
    /// 「不挡单位但挡子弹」是第四种组合，全项目 0 个定义用到，没有为它单独开层，
    /// 落到 World3D。真需要时再加，那时候也有具体场景验证设计。
    ///
    /// 放在定义上而不是 Builder 里：Builder 在 Editor 程序集、RuntimeApplier 在
    /// Assembly-CSharp，两边都要用这套映射。各写一份必然漂移——Applier 那边本来就
    /// 要在 ApplyLayerStructure 之后把层重新按住，判定条件一旦和 Builder 不一致
    /// 就会互相打架。
    /// </summary>
    public string ResolveCollisionLayerName()
    {
        if (BlocksUnits)
            return blockProjectile ? "World3D" : UnitOnlyObstacleLayerName;

        // 不挡单位却要挡子弹：没有对应的层，按两者都挡处理。
        if (blockProjectile)
            return "World3D";

        return GroundSurfaceMarker.WalkableProbeLayerName;
    }

    [Tooltip("可站立表面的地表材质。填了才会产生地表脚步声层；留空只有基础鞋声。\n\n" +
             "注意「挡住」和「能站上去」不是一回事——树、栏杆、墙 blockPlayer 也是 true，" +
             "但站不上去，那些保持留空。只给站台、楼梯、平台、桥这类真正能踩的填。\n\n" +
             "填在定义上，所有实例自动生效，不用逐个去场景里挂组件。")]
    public GroundSurfaceMaterialDefinition walkableSurface;

    [Header("Rule Space / Front Back")]
    public bool lockRuleSpaceFromVisualRandomRotation = true;
    public TerrainDecorationFrontBackPlaneMode frontBackPlaneMode = TerrainDecorationFrontBackPlaneMode.CollisionBounds;
    public Vector3 ruleForwardLocal = Vector3.forward;
    public Vector3 rulePlaneOriginLocal = Vector3.zero;
    public float planePushOutDistance = 0.05f;

    [Header("Occlusion")]
    public TerrainDecorationOcclusionMode occlusionMode = TerrainDecorationOcclusionMode.None;
    public bool fadeWhenBlockingPlayer = false;
    [Range(0f, 1f)] public float fadeAlpha = 0.45f;
    public float fadeDuration = 0.12f;

    [Header("Height Fade（高层建筑物）")]
    [Tooltip("勾上后，放置这个装饰物时自动挂 SkyPrisonHeightFadeController——建筑自己底部\n" +
             "往上超过 heightFadeThreshold 的部分，在接下来 heightFadeDistance 这段距离内\n" +
             "逐渐淡出到全透明，避免高层建筑把镜头和地图背景之间的视野挡得太死。\n" +
             "注意：这只挂组件，真正要有可见效果，物体的材质Shader还得支持高度淡出\n" +
             "（SkyPrison/Lit With Height Fade）且 Surface Type 是 Transparent，这两步\n" +
             "美术/关卡那边单独处理，不是这个勾选框自动做的。")]
    public bool enableHeightFade = false;
    [Tooltip("从建筑自己底部往上算，超过这个高度（米）才开始淡出。")]
    public float heightFadeThreshold = 15f;
    [Tooltip("淡出经过的距离（米）——超过 threshold 之后，再经过这段距离完全淡到透明。")]
    public float heightFadeDistance = 2f;

    [Header("Front / Back Occlusion Projection")]
    [Min(0.01f)] public float frontBackOcclusionWidthMultiplier = 1f;
    [Min(0.01f)] public float frontBackOcclusionHeightMultiplier = 1f;
    [Min(0.01f)] public float frontBackOcclusionDepthMultiplier = 1f;
    [Range(0f, 1f)] public float frontOcclusionDepthRatio = 0.18f;
    [Range(0f, 1f)] public float backOcclusionDepthRatio = 0.82f;
    public float frontBackOcclusionCenterOffset = 0f;
    public float frontBackOcclusionHorizontalOffset = 0f;
    public float frontBackOcclusionHeightOffset = 0f;
    public float frontBackOcclusionDepthOffset = 0f;

    [Header("Front Occluder Proxy")]
    /// <summary>
    /// 「能穿过去、但能挡住角色」的装饰物（草丛、灌木、纱帘）专用层。
    /// 遮挡判定是射线，射线只看 layerMask 不看物理碰撞矩阵，所以这一层只要不出现在
    /// 移动用的 mask 里，就能同时做到「遮挡判定看得见、角色穿得过」。
    /// </summary>
    public const string OccluderProbeLayerName = "OccluderProbe";

    [Tooltip("生成一个只给遮挡判定用的碰撞体，不挡移动。\n\n" +
             "碰撞模式=无 的装饰物（草丛这类）身上一个碰撞体都没有，而前后遮挡判定是" +
             "「从相机向角色射线、看有没有先打到遮挡物自己的碰撞体」——没有靶子就永远" +
             "判定成不遮挡，角色会画在草的前面。\n\n" +
             "开启后会在 VisualRoot 下生成一个贴合视觉包围盒的 Box，放在 OccluderProbe 层。" +
             "那一层被排除在移动碰撞之外，所以照样能穿过去。\n\n" +
             "碰撞模式不是「无」的装饰物不需要开——它们本来就有碰撞体可以当靶子。")]
    public bool generateOccluderProbeCollider = false;

    public TerrainDecorationFrontOccluderProxyMode frontOccluderProxyMode = TerrainDecorationFrontOccluderProxyMode.ModelProxy;
    public Material frontOccluderProxyMaterial;
    [Range(0f, 1f)] public float frontOccluderAlphaCutoff = 0.35f;
    public GameObject manualFrontOccluderProxyPrefab;
    [Min(0.01f)] public float frontOccluderProxyWidthMultiplier = 1f;
    [Min(0.01f)] public float frontOccluderProxyHeightMultiplier = 1f;
    [Min(0.01f)] public float frontOccluderProxyDepthMultiplier = 1f;
    public Vector3 frontOccluderProxyOffset = Vector3.zero;

    [Header("Shadow")]
    public TerrainDecorationShadowMode shadowMode = TerrainDecorationShadowMode.MeshRenderer;
    public bool castShadow = true;
    public bool receiveShadow = true;
    public GameObject shadowCasterPrefab;
    public Material shadowCasterMaterial;

    [Header("Fog")]
    public TerrainDecorationFogMode fogMode = TerrainDecorationFogMode.AlwaysVisible;

    [Header("Environment Audio")]
    public bool enableEnvironmentAudio = false;
    public SkyPrisonAudioPackage environmentAudioPackage;
    [Min(0f)] public float environmentAudioMinDistance = 1f;
    [Min(0.1f)] public float environmentAudioMaxDistance = 12f;
    [Range(0f, 2f)] public float environmentAudioVolume = 1f;
    public bool environmentAudioLoop = true;

    [Header("Editor Display")]
    public bool showBoundsGizmo = true;
    public bool showCollisionGizmo = true;
    public bool showFrontBackPlaneGizmo = true;
    public Color gizmoColor = new Color(1f, 0.55f, 0.12f, 1f);

    public TerrainDecorationVariant GetFirstVariant()
    {
        return variants != null && variants.Count > 0 ? variants[0] : null;
    }
}
