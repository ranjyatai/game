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

    [Header("Collision")]
    public TerrainDecorationCollisionMode collisionMode = TerrainDecorationCollisionMode.Box;
    public Vector3 collisionSize = new Vector3(1f, 1f, 1f);
    public Vector3 collisionOffset = new Vector3(0f, 0.5f, 0f);
    public bool blockPlayer = true;
    public bool blockEnemy = true;
    public bool blockVision = false;
    public bool blockProjectile = false;

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
