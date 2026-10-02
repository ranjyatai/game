using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 建立并配置 WalkableProbe 层——「能踩上去、但不挡任何人」的碰撞体专用层。
///
/// 为什么需要一个专门的层：
/// GroundQueryService 判断脚下是什么地表，用的是向下射线，而且明确排除 trigger
/// （QueryTriggerInteraction.Ignore 再加一道 col.isTrigger 过滤）。所以铁轨这类
/// 装饰物必须带实体碰撞体才查得到。但实体碰撞体默认会挡住角色。
///
/// 解法是把它单独放一层，然后利用 Unity 的一个事实：
///   射线检测只看 layerMask，完全不看物理碰撞矩阵。
/// 于是只要这一层不出现在移动用的 mask 里，它就同时做到「射线看得见、移动看不见」。
///
/// 这个工具负责三件事，缺一不可：
///   1. 建层（找第一个空槽）
///   2. 加进 GroundQueryService.groundRaycastLayers —— 不加就查不到
///   3. 从三个移动 mask 里剔掉 —— 不剔就会挡人
///
/// 第 3 步是重点：那三个 mask 默认值都是 ~0（包含所有层），新建的层会被自动包含。
/// 所以「建个新层」本身不解决任何问题，必须显式排除。
/// </summary>
public static class SkyPrisonWalkableProbeLayerSetup
{
    private const string LogPrefix = "[SkyPrison WalkableProbe]";
    private const int FirstUserLayerToSearch = 28;

    [MenuItem("天空囚笼/地面/接入可站立装饰物探测层", false, 144)]
    public static void Setup()
    {
        RunSetup(interactive: true);
    }

    /// <summary>
    /// 返回修正的 mask 字段数；-1 表示没有空闲层槽。
    /// interactive=false 时不弹窗，供自动同步调用。
    /// </summary>
    public static int RunSetup(bool interactive)
    {
        // 「挡单位、不挡子弹」的装饰物层。不需要改任何 mask：移动 mask 默认 ~0 已经
        // 包含它（正是我们要的"挡单位"），而子弹那边是层白名单、本来就不含它。
        // 只要保证层存在即可。
        EnsureLayer(TerrainDecorationDefinition.UnitOnlyObstacleLayerName);

        // 「能穿过去、但要挡住角色」的装饰物（草丛这类）用的探测层。
        // 遮挡判定是射线，只看 layerMask；移动是 CapsuleCast/穿透修正，看的是另外
        // 几个 mask。把这一层从移动 mask 里剔掉，两件事就互不干扰了。
        int occluderProbe = EnsureLayer(TerrainDecorationDefinition.OccluderProbeLayerName);

        int layer = EnsureLayer(GroundSurfaceMarker.WalkableProbeLayerName);
        if (layer < 0)
        {
            string message =
                $"没有空闲的 User Layer 了（{FirstUserLayerToSearch}~31 都被占用）。" +
                "请先腾出一个层槽，或者手动指定一个空层。";

            if (interactive)
                EditorUtility.DisplayDialog("WalkableProbe", message, "知道了");
            else
                Debug.LogWarning($"{LogPrefix} {message}");
            return -1;
        }

        int fixedMasks = 0;
        fixedMasks += ApplyToAllInstances<GroundQueryService>(
            layer, new[] { "groundRaycastLayers" }, include: true);

        fixedMasks += ApplyToAllInstances<UnitMovementController>(
            layer, new[] { "blockingLayers" }, include: false);

        // groundMask 也要排除：这一层只用来「判定踩到没踩到」，不该成为站立高度来源。
        // 含进去的话角色跨过铁轨会被抬高十几厘米，2.5D 正交视角下很明显。
        // 真正能站上去的平台/楼梯走的是它们自己的实体碰撞体，不经过这一层。
        fixedMasks += ApplyToAllInstances<TerrainGroundMotorV5>(
            layer, new[] { "bodyBlockMask", "groundMask" }, include: false);

        if (occluderProbe >= 0)
        {
            // 移动侧全部排除——这一层只给遮挡射线用。
            fixedMasks += ApplyToAllInstances<UnitMovementController>(
                occluderProbe, new[] { "blockingLayers" }, include: false);
            fixedMasks += ApplyToAllInstances<TerrainGroundMotorV5>(
                occluderProbe, new[] { "bodyBlockMask", "groundMask" }, include: false);

            // 遮挡射线侧必须包含，否则射线打不到探测碰撞体，等于没生成。
            //
            // 只加 raycastLayers，不要加 targetLayers——后者是「扫描候选单位」用的，
            // 把探测层加进去会让探测碰撞体自己被当成一个单位候选。
            // 两个字段默认都是 ~0，所以这一步对默认实例是空操作；它是给
            // mask 被收窄过的实例兜底的。
            fixedMasks += ApplyToAllInstances<SkyPrisonTerrainDecorationFrontOccluderTrigger>(
                occluderProbe, new[] { "raycastLayers" }, include: true);
        }

        AssetDatabase.SaveAssets();

        Debug.Log(
            $"{LogPrefix} 完成。层「{GroundSurfaceMarker.WalkableProbeLayerName}」= {layer}，" +
            $"修正了 {fixedMasks} 个 mask 字段。\n" +
            $"如果之后新增了单位预制体或场景，重跑一次这个菜单即可——它是幂等的。");

        return fixedMasks;
    }

    /// <summary>
    /// 找第一个空的 User Layer 槽建层。已存在则直接返回索引。
    /// 走 SerializedObject 而不是改 TagManager.asset 文本：Unity 开着的时候
    /// 直接改那个文件不会被重新读取，保存时还会被覆盖回去。
    /// </summary>
    private static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0)
            return existing;

        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
            return -1;

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        if (layers == null)
            return -1;

        for (int i = FirstUserLayerToSearch; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (slot == null || !string.IsNullOrEmpty(slot.stringValue))
                continue;

            slot.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} 新建层「{layerName}」在 {i}。");
            return i;
        }

        return -1;
    }

    /// <summary>
    /// 把某个层加进 / 剔出指定组件所有实例的 LayerMask 字段。
    /// 场景里的实例和预制体资产都要处理——单位一般是预制体，但地图上也常有场景实例。
    /// </summary>
    private static int ApplyToAllInstances<T>(int layer, string[] maskFieldNames, bool include)
        where T : Component
    {
        int changed = 0;
        var visited = new HashSet<Object>();

        foreach (T component in CollectSceneInstances<T>())
        {
            if (component == null || !visited.Add(component))
                continue;
            changed += ApplyToComponent(component, layer, maskFieldNames, include, markSceneDirty: true);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            foreach (T component in prefab.GetComponentsInChildren<T>(true))
            {
                if (component == null || !visited.Add(component))
                    continue;
                changed += ApplyToComponent(component, layer, maskFieldNames, include, markSceneDirty: false);
            }
        }

        return changed;
    }

    private static List<T> CollectSceneInstances<T>() where T : Component
    {
        var found = new List<T>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return found;
    }

    private static int ApplyToComponent(Component component, int layer, string[] maskFieldNames, bool include, bool markSceneDirty)
    {
        SerializedObject so = new SerializedObject(component);
        int bit = 1 << layer;
        int changed = 0;

        foreach (string fieldName in maskFieldNames)
        {
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
                continue;

            int current = prop.intValue;
            int updated = include ? (current | bit) : (current & ~bit);
            if (updated == current)
                continue;

            prop.intValue = updated;
            changed++;
        }

        if (changed == 0)
            return 0;

        so.ApplyModifiedPropertiesWithoutUndo();

        if (markSceneDirty)
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        else
            EditorUtility.SetDirty(component);

        Debug.Log($"{LogPrefix} {component.GetType().Name} on {component.name}：修正 {changed} 个 mask。", component);
        return changed;
    }
}
