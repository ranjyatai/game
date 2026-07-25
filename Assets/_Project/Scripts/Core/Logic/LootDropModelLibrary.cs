using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 掉落物模型库。资产放在 Resources/ 下命名 LootDropModelLibrary。
/// 运行时通过 LootDropModelLibrary.Instance 访问，不要在每个组件里单独 Load。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/Drop/Loot Drop Model Library", fileName = "LootDropModelLibrary")]
public class LootDropModelLibrary : ScriptableObject
{
    public const string ResourcesAssetName = "LootDropModelLibrary";
    public const string DefaultAssetPath   = "Assets/_Project/Data/Settings/Resources/LootDropModelLibrary.asset";

    // ── 静态单例 ──────────────────────────────────────────────────────────
    private static LootDropModelLibrary _instance;

    public static LootDropModelLibrary Instance
    {
        get
        {
            if (_instance != null) return _instance;

#if UNITY_EDITOR
            _instance = FindEditorAsset();
#else
            _instance = Resources.Load<LootDropModelLibrary>(ResourcesAssetName);

            if (_instance == null)
            {
                var all = Resources.LoadAll<LootDropModelLibrary>("");
                if (all != null && all.Length > 0) _instance = all[0];
            }

            // Preloaded Assets 路径：资产在 Build 启动时已加载进内存
            if (_instance == null)
            {
                var found = Resources.FindObjectsOfTypeAll<LootDropModelLibrary>();
                if (found != null && found.Length > 0) _instance = found[0];
            }

            if (_instance == null)
                Debug.LogError("[LootDropModelLibrary] 仍找不到资产！请执行菜单 SkyPrison/Rendering/修复掉落物 Build 打包 ★ 然后重新 Build。");
#endif
            return _instance;
        }
    }

    public static void ClearCache() => _instance = null;

    /// <summary>由 LootDropLibraryLocator 在场景 Awake 时注入，Build 里免 Resources.Load。</summary>
    public static void RegisterInstance(LootDropModelLibrary lib)
    {
        if (lib != null) _instance = lib;
    }

#if UNITY_EDITOR
    private static LootDropModelLibrary FindEditorAsset()
    {
        // 先按约定路径找
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<LootDropModelLibrary>(DefaultAssetPath);
        if (asset != null) return asset;

        // 全项目搜索兜底
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:LootDropModelLibrary");
        if (guids.Length > 0)
            return UnityEditor.AssetDatabase.LoadAssetAtPath<LootDropModelLibrary>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));

        return null;
    }
#endif

    // ── 一般道具：消耗品 / 材料 / 任务 / 凭证 / 特殊 ────────────────────────
    [Serializable]
    public class GeneralEntry
    {
        [InspectorName("类别")]
        public ItemCategory category;
        [Tooltip("FBX GameObject（优先）。")]
        public GameObject model;
        [Tooltip("或直接拖入 Mesh asset（当 model 为空时使用）。")]
        public Mesh mesh;
        [Tooltip("相对于全局尺寸倍率的额外缩放，1 = 不调整。")]
        public float scaleOverride = 1f;
        [Tooltip("模型实例化后的额外旋转偏移（欧拉角），用于修正 FBX 导入朝向。")]
        public Vector3 rotationOffset = Vector3.zero;
    }

    // ── 材料子类：零件 / 金属块 / 液体 / 杂物 ───────────────────────────────
    [Serializable]
    public class MaterialEntry
    {
        [InspectorName("材料子类")]
        public MaterialSubCategory subCategory;
        [Tooltip("FBX GameObject（优先）。")]
        public GameObject model;
        [Tooltip("或直接拖入 Mesh asset（当 model 为空时使用）。")]
        public Mesh mesh;
        [Tooltip("相对于全局尺寸倍率的额外缩放，1 = 不调整。")]
        public float scaleOverride = 1f;
        [Tooltip("模型实例化后的额外旋转偏移（欧拉角），用于修正 FBX 导入朝向。")]
        public Vector3 rotationOffset = Vector3.zero;
    }

    // ── 装备：武器 / 头部 / 上装 / 下装 / 手部 / 鞋子 ──────────────────────
    [Serializable]
    public class EquipmentEntry
    {
        [InspectorName("装备槽")]
        public EquipmentSlotType slot;
        [Tooltip("FBX GameObject（优先）。")]
        public GameObject model;
        [Tooltip("或直接拖入 Mesh asset（当 model 为空时使用）。")]
        public Mesh mesh;
        [Tooltip("相对于全局尺寸倍率的额外缩放，1 = 不调整。")]
        public float scaleOverride = 1f;
        [Tooltip("模型实例化后的额外旋转偏移（欧拉角），用于修正 FBX 导入朝向。")]
        public Vector3 rotationOffset = Vector3.zero;
    }

    [Header("全局模型尺寸")]
    [Tooltip("以默认尺寸为 1.0 基准，填写倍率。例如 1.3 = 放大 30%。")]
    public float modelSizeMultiplier = 1.0f;

    [Header("全息材质")]
    [Tooltip("指定使用 Sky Prison/LootDrop Hologram shader 的材质。留空则保持原始 FBX 材质。")]
    public Material hologramMaterial;

    [Header("VFX 染色 Shader（构建包含用）")]
    [Tooltip("把 VFXHueShift.shader 拖进来。Hidden/ 前缀 shader 不会自动打包，必须显式引用才能在 Build 中使用。")]
    public Shader vfxHueShiftShader;

    [Header("光柱 VFX 材质（自搓粒子用）")]
    [Tooltip("URP Particles/Unlit，Additive。beaconVFXPrefab 有值时此项无效。")]
    public Material beaconMaterial;

    [Header("光柱 VFX Prefab（商店资产）")]
    [Tooltip("把商店买来的掉落 VFX Prefab 拖进来，代码自动按品级染色。有值时替代自搓粒子。")]
    public GameObject beaconVFXPrefab;

    [Tooltip("VFX 整体缩放。X/Z 控制光柱粗细，Y 控制高度。")]
    public Vector3 beaconVFXScale = Vector3.one;

    [Header("一般道具 Mesh（消耗品 / 任务 / 凭证 / 特殊；材料请用下方子类区块）")]
    public List<GeneralEntry> generalEntries = new List<GeneralEntry>();

    [Header("材料子类 Mesh（零件 / 金属块 / 液体 / 杂物）")]
    public List<MaterialEntry> materialEntries = new List<MaterialEntry>();

    [Header("装备 Mesh（武器 / 头部 / 上装 / 下装 / 手部 / 鞋子）")]
    public List<EquipmentEntry> equipmentEntries = new List<EquipmentEntry>();

    // ── 查询 API ──────────────────────────────────────────────────────────

    public GameObject GetModelForGeneral(ItemCategory category, out float scaleOverride, out Vector3 rotationOffset, out Mesh meshOverride)
    {
        scaleOverride = 1f; rotationOffset = Vector3.zero; meshOverride = null;
        if (category == ItemCategory.Material) return null;
        foreach (var e in generalEntries)
            if (e.category == category && (e.model != null || e.mesh != null))
            { scaleOverride = e.scaleOverride; rotationOffset = e.rotationOffset; meshOverride = e.mesh; return e.model; }
        return null;
    }

    public GameObject GetModelForMaterial(MaterialSubCategory sub, out float scaleOverride, out Vector3 rotationOffset, out Mesh meshOverride)
    {
        foreach (var e in materialEntries)
            if (e.subCategory == sub && (e.model != null || e.mesh != null))
            { scaleOverride = e.scaleOverride; rotationOffset = e.rotationOffset; meshOverride = e.mesh; return e.model; }
        return GetModelForGeneral(ItemCategory.Material, out scaleOverride, out rotationOffset, out meshOverride);
    }

    public GameObject GetModelForEquipment(EquipmentSlotType slot, out float scaleOverride, out Vector3 rotationOffset, out Mesh meshOverride)
    {
        scaleOverride = 1f; rotationOffset = Vector3.zero; meshOverride = null;
        foreach (var e in equipmentEntries)
            if (e.slot == slot && (e.model != null || e.mesh != null))
            { scaleOverride = e.scaleOverride; rotationOffset = e.rotationOffset; meshOverride = e.mesh; return e.model; }
        return null;
    }

    // ── 品级颜色（与物品库 QualityHex 保持一致）─────────────────────────────
    public static Color GetLevelColor(int itemLevel) => itemLevel switch
    {
        1 => HexColor("B4B2A9"),
        2 => HexColor("97C459"),
        3 => HexColor("ED93B1"),
        4 => HexColor("85B7EB"),
        5 => HexColor("AFA9EC"),
        6 => HexColor("1D9E75"),
        7 => HexColor("D85A30"),
        8 => HexColor("E24B4A"),
        _ => HexColor("B4B2A9"),
    };

    private static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }
}
