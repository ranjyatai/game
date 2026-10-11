using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把一个静态网格（比如买来的木箱 FBX）包装成"可破坏场景物"预制体：
/// 血量1、被打就碎。血量/死亡/碎裂这些逻辑都不需要完整的 UnitDefinition——
/// 相关组件对它的引用全是判空的。但出血表现是例外：
/// UnitActionModuleRuntime.ResolveSkillHit 只认
/// UnitDefinitionRuntimeBinder.UnitDefinitionAsset.bloodVFXType，没绑定就固定用
/// 红色 Normal。要让场景物跟 AXIA 一样不出血，就必须挂一份 bloodVFXType=None 的
/// UnitDefinition——所有可破坏场景物共用同一份最小定义资产，见
/// GetOrCreateNoBloodDestructibleDefinition。
///
/// 结构：
///   根节点：UnitBody层 + UnitHealthController（覆盖成maxHealth=1）+
///           UnitDeathController + SkyPrisonBreakableCrateEffect（碎块+爆炸冲量）+
///           UnitOcclusionMaterialReceiver（被挡住出白色全息）+
///           UnitDefinitionRuntimeBinder（引用共享的无出血定义）
///   子节点：Model3DRoot（空占位——真正的网格由 UnitDefinition.model3DPrefab +
///           RuntimeBinder.RefreshModel3DBindingsIfPossible 在运行时/放置时实例化
///           进去，缩放也作用在这个节点上，所以视觉不直接内嵌在预制体里；命中判定
///           用的 MeshCollider 也是在这一步顺带挂在网格实例的子节点上，贴合真实
///           形状、天然跟着缩放走，不用单独维护一份 BoxCollider）
///
/// 幂等：目标路径已存在同名预制体就跳过，不覆盖。
/// </summary>
public static class SkyPrisonBreakableCratePrefabCreator
{
    private const string LogPrefix = "[SkyPrison 可破坏场景物]";

    [MenuItem("Assets/天空囚笼/包装成可破坏场景物预制体", true)]
    private static bool ValidateCreate()
    {
        return Selection.activeObject is GameObject go && PrefabUtility.IsPartOfModelPrefab(go);
    }

    [MenuItem("Assets/天空囚笼/包装成可破坏场景物预制体")]
    public static void CreateFromSelectedMesh()
    {
        GameObject sourceMesh = Selection.activeObject as GameObject;
        if (sourceMesh == null)
        {
            Debug.LogError($"{LogPrefix} 请先在 Project 窗口选中一个网格资产（FBX 里的模型）。");
            return;
        }

        CreateFromMesh(sourceMesh);
    }

    /// <summary>
    /// 供一次性脚本/其它编辑器代码直接调用的入口——不依赖 Selection，可以脱离
    /// 菜单点击流程执行。返回生成（或已存在的）预制体资产，失败返回 null。
    /// </summary>
    public static GameObject CreateFromMesh(GameObject sourceMesh)
    {
        if (sourceMesh == null)
        {
            Debug.LogError($"{LogPrefix} 传入的网格资产为空。");
            return null;
        }

        string sourcePath = AssetDatabase.GetAssetPath(sourceMesh);
        string sourceFolder = Path.GetDirectoryName(sourcePath).Replace('\\', '/');
        // 输出到网格资产同级的一个 Prefabs 子文件夹，不跟原始美术资源混在一起，
        // 方便以后美术更新原始FBX时不用担心覆盖到这份包装好的预制体。
        string outputFolder = sourceFolder + "/Breakable";
        string prefabName = "PF_" + sourceMesh.name + "_Breakable";
        string prefabPath = $"{outputFolder}/{prefabName}.prefab";

        GameObject alreadyExisting = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (alreadyExisting != null)
        {
            Debug.LogWarning($"{LogPrefix} {prefabPath} 已存在，跳过（幂等，不覆盖）。");
            Selection.activeObject = alreadyExisting;
            return alreadyExisting;
        }

        if (!AssetDatabase.IsValidFolder(outputFolder))
            AssetDatabase.CreateFolder(sourceFolder, "Breakable");

        int unitBodyLayer = LayerMask.NameToLayer("UnitBody");
        if (unitBodyLayer < 0)
        {
            Debug.LogWarning($"{LogPrefix} 找不到 UnitBody 层，退回 Default——" +
                              "攻击命中检测大概率找不到这个物体，记得手动改层。");
            unitBodyLayer = 0;
        }

        GameObject root = new GameObject(prefabName);
        root.layer = unitBodyLayer;

        // 视觉走"3D 通道"：预制体里只留一个空的 Model3DRoot 占位节点，网格本体不
        // 直接内嵌，改成 UnitDefinition.model3DPrefab 引用 + 运行时由
        // UnitDefinitionRuntimeBinder.RefreshModel3DBindingsIfPossible 实例化进去。
        // 这样"模型缩放"字段（作用在 Model3DRoot 上）才对这个预制体生效——直接内嵌
        // 网格的旧写法完全绕开了这套缩放机制，改缩放字段没有任何反应。
        GameObject model3DRoot = new GameObject("Model3DRoot");
        model3DRoot.transform.SetParent(root.transform, false);

        // 命中判定不在这里手动量包围盒定 BoxCollider 了——那种写法的碰撞体大小是
        // 缩放前量的，跟 model3DVisualScale 不同步（改缩放没跟着变大/变小，打偏）。
        // 改成 UnitDefinitionRuntimeBinder.RefreshModel3DBindingsIfPossible 在实例化
        // 网格的同时顺手挂 MeshCollider（凸包，贴合真实网格形状，天然跟着父节点缩放），
        // 下面 binder.SetUnitDefinitionAsset(...) 一调用就会把这一步跑掉。

        UnitHealthController health = root.AddComponent<UnitHealthController>();
        ConfigureHealthForOneHitKill(health);

        root.AddComponent<UnitDeathController>();
        root.AddComponent<SkyPrisonBreakableCrateEffect>();

        // 被别的地形装饰物挡住时要出"白色全息"——这是 UnitOcclusionMaterialReceiver
        // 负责的效果，不加这个组件的话别的遮挡物的触发器找到箱子也没有接收器能响应，
        // 挡住了也不会有任何表现。渲染器缓存要等 Model3DRoot 下的网格实例化完之后才刷
        // 新一次，见 UnitDefinitionRuntimeBinder.RefreshModel3DBindingsIfPossible。
        root.AddComponent<UnitOcclusionMaterialReceiver>();

        UnitDefinition definition = GetOrCreateNoBloodDestructibleDefinition(outputFolder, prefabName);
        if (definition.model3DPrefab == null)
        {
            // model3DPrefab 是共享定义上的单一字段——第一个用这份共享定义的箱子网格
            // 顺带写进去；换别的网格再复用这份定义会覆盖不掉旧网格，需要额外拆分成
            // 各自专属的 UnitDefinition，这里先不做，等真的出现第二种网格再处理。
            definition.visualChannel = UnitVisualChannel.Model3D;
            definition.model3DPrefab = sourceMesh;
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
        }

        UnitDefinitionRuntimeBinder binder = root.AddComponent<UnitDefinitionRuntimeBinder>();
        binder.SetUnitDefinitionAsset(definition, applyNow: false);

        GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        Debug.Log($"{LogPrefix} 已生成 {prefabPath}（血量1，UnitBody层，命中即碎）。", prefabAsset);
        Selection.activeObject = prefabAsset;
        EditorGUIUtility.PingObject(prefabAsset);
        return prefabAsset;
    }

    /// <summary>
    /// UnitHealthController 的字段都是 private，只能通过 SerializedObject 改——
    /// 这是场景物唯一需要碰"属性系统"的地方：把默认血量覆盖成1，不接入
    /// BattleParameterDatabase 那套（那是给会被buff/装备影响血量上限的角色用的，
    /// 一次性场景物用不上，maxHp的默认值100会被这个覆盖盖掉）。
    /// </summary>
    private static void ConfigureHealthForOneHitKill(UnitHealthController health)
    {
        var so = new SerializedObject(health);
        so.FindProperty("overrideDefaultHealth").boolValue = true;
        so.FindProperty("maxHealth").floatValue = 1f;
        so.FindProperty("currentHealth").floatValue = 1f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 出血效果（UnitActionModuleRuntime.ResolveSkillHit）只认
    // UnitDefinitionRuntimeBinder.UnitDefinitionAsset.bloodVFXType，没有绑定就固定
    // 用红色 Normal——AXIA不出血走的也是这条路（bloodVFXType=None），场景物要跟
    // 它保持同一套机制而不是另开一个开关。所有"可破坏场景物"共用同一份最小
    // UnitDefinition（类型=可破坏物品，无出血），不用每个箱子各生成一份。
    private const string SharedNoBloodDefinitionPath =
        "Assets/_Project/Data/Definitions/Custom/Units/Destructibles/UD_SceneProp_NoBlood.asset";

    private static UnitDefinition GetOrCreateNoBloodDestructibleDefinition(string fallbackFolder, string fallbackName)
    {
        UnitDefinition existing = AssetDatabase.LoadAssetAtPath<UnitDefinition>(SharedNoBloodDefinitionPath);
        if (existing != null)
            return existing;

        string folder = Path.GetDirectoryName(SharedNoBloodDefinitionPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder))
        {
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        UnitDefinition definition = ScriptableObject.CreateInstance<UnitDefinition>();
        definition.defineType = UnitDefineType.Destructible;
        definition.bloodVFXType = UnitBloodVFXType.None;

        AssetDatabase.CreateAsset(definition, SharedNoBloodDefinitionPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"{LogPrefix} 已生成共享的无出血场景物定义 {SharedNoBloodDefinitionPath}（供所有可破坏场景物复用）。", definition);
        return definition;
    }
}
