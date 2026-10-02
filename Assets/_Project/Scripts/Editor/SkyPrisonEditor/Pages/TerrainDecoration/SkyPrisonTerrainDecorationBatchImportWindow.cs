using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 批量把模型资产变成地形装饰物定义。
///
/// 手工流程是：定义页点「新建」→ 改名 → 绑模型 → 填碰撞盒尺寸和偏移 → 重复 N 次。
/// 素材一多这个循环就是纯体力活，而且碰撞盒靠肉眼估数字很难准
/// （AutoFitCollisionBoxToVisualBase 已经废弃了，没有现成的自动贴合）。
///
/// 这里直接从模型的渲染器包围盒算碰撞盒，一次处理一整批。
/// 变体绑的是 GameObject，FBX 资产本身就是 GameObject，不用先手工做预制体
/// （标准容器 Prefab 那套也废弃了，实例结构由 Builder 在放置时生成）。
///
/// 可重复运行：已经被某个定义绑过的模型会自动跳过，不会重复生成。
/// </summary>
public class SkyPrisonTerrainDecorationBatchImportWindow : EditorWindow
{
    private const string CustomFolder = "Assets/_Project/Data/Definitions/Custom/TerrainDecorations";
    private const string DefaultIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_34.png";

    private enum CollisionFit
    {
        [InspectorName("完整包围盒")] FullBounds,
        [InspectorName("底部贴合（高度取 30%）")] BaseOnly,
        [InspectorName("不自动算（留默认值）")] None,
    }

    private TerrainDecorationCategory category = TerrainDecorationCategory.Prop;
    private string subCategory = "Custom";
    private CollisionFit collisionFit = CollisionFit.FullBounds;
    private GroundSurfaceMaterialDefinition walkableSurface;
    private bool blockPlayer = true;
    private Vector2 scroll;

    [MenuItem("天空囚笼/地图/批量从模型生成地形装饰物定义", false, 115)]
    public static void Open()
    {
        // 普通可停靠窗口，不用 utility——utility 窗口浮在最前、抢焦点，
        // 而这个窗口的整个用法就是"一边在 Project 里点选、一边看这里的计数"。
        var w = GetWindow<SkyPrisonTerrainDecorationBatchImportWindow>("批量生成装饰物定义");
        w.minSize = new Vector2(460f, 380f);
        w.Show();
    }

    private void OnSelectionChange() => Repaint();

    /// <summary>
    /// 收集场景里选中的、可以转成地形装饰物的对象。
    ///
    /// 只接受"预制体实例"——必须能反查到源资产，因为定义的变体绑的是 GameObject 资产。
    /// 场景里手捏的散装物件没有源资产可绑，跳过并在结果里报出来，让你先存成预制体。
    /// 已经是装饰物的（带 Binder）也跳过，避免重复套娃。
    /// </summary>
    private static List<GameObject> CollectSelectedSceneObjects(out List<string> unconvertible)
    {
        var result = new List<GameObject>();
        unconvertible = new List<string>();

        GameObject[] sel = Selection.gameObjects;
        for (int i = 0; i < sel.Length; i++)
        {
            GameObject go = sel[i];
            if (go == null || AssetDatabase.Contains(go)) continue;   // 资产不走这条路
            if (go.GetComponentInParent<TerrainDecorationRuntimeBinder>() != null) continue;

            if (ResolveSourcePrefab(go) == null)
            {
                unconvertible.Add(go.name);
                continue;
            }
            result.Add(go);
        }
        return result;
    }

    private static GameObject ResolveSourcePrefab(GameObject sceneObject)
    {
        GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(sceneObject);
        if (source != null) return source;

        // 嵌套/变体预制体的情况，退回最外层实例的源
        GameObject outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(sceneObject);
        if (outermost != null)
            return PrefabUtility.GetCorrespondingObjectFromOriginalSource(outermost);

        return null;
    }

    private void OnGUI()
    {
        List<GameObject> models = CollectSelectedModels();

        EditorGUILayout.HelpBox(
            "在 Project 窗口里选中一批模型（FBX 或预制体均可），然后点下面的按钮。\n" +
            "已经被现有定义绑过的模型会自动跳过，可以反复运行。",
            MessageType.Info);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField($"当前选中的模型：{models.Count} 个", EditorStyles.boldLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(110f));
        for (int i = 0; i < models.Count && i < 200; i++)
            EditorGUILayout.LabelField("　" + models[i].name);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6f);
        category = (TerrainDecorationCategory)EditorGUILayout.EnumPopup("分类", category);
        subCategory = EditorGUILayout.TextField("子分类", subCategory);
        collisionFit = (CollisionFit)EditorGUILayout.EnumPopup("碰撞盒", collisionFit);
        blockPlayer = EditorGUILayout.Toggle("阻挡玩家", blockPlayer);

        EditorGUILayout.Space(2f);
        walkableSurface = (GroundSurfaceMaterialDefinition)EditorGUILayout.ObjectField(
            new GUIContent("可行走面", "填了才会有地表脚步声。站台、楼梯、平台这类才填；" +
                                     "树、栏杆、墙留空——「挡住」和「能站上去」不是一回事。"),
            walkableSurface, typeof(GroundSurfaceMaterialDefinition), false);

        EditorGUILayout.Space(10f);
        using (new EditorGUI.DisabledScope(models.Count == 0))
        {
            if (GUILayout.Button($"批量生成 {models.Count} 个定义", GUILayout.Height(30f)))
                Generate(models);
        }

        // ── 场景对象转换 ────────────────────────────────────────────
        EditorGUILayout.Space(14f);
        EditorGUILayout.LabelField("场景对象转换", EditorStyles.boldLabel);

        List<GameObject> sceneObjects = CollectSelectedSceneObjects(out List<string> unconvertible);

        EditorGUILayout.HelpBox(
            "在 Hierarchy 里选中已经摆好的预制体实例，可以原地换成结构化的地形装饰物。\n" +
            "位置、旋转、缩放和父节点都保留，同一个源预制体只会建一个定义。Ctrl+Z 可整体撤销。",
            MessageType.None);

        EditorGUILayout.LabelField($"可转换的场景对象：{sceneObjects.Count} 个");

        if (unconvertible.Count > 0)
        {
            EditorGUILayout.HelpBox(
                $"有 {unconvertible.Count} 个选中对象无法转换——它们不是预制体实例，反查不到源资产，" +
                $"而定义的变体必须绑一个 GameObject 资产。先把它们存成预制体再来：\n　" +
                string.Join("、", unconvertible.GetRange(0, Mathf.Min(6, unconvertible.Count))) +
                (unconvertible.Count > 6 ? " …" : ""),
                MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(sceneObjects.Count == 0))
        {
            if (GUILayout.Button($"把 {sceneObjects.Count} 个场景对象转成装饰物", GUILayout.Height(30f)))
                ConvertSceneObjects(sceneObjects);
        }
    }

    /// <summary>
    /// 收集 Project 窗口里选中的模型。
    ///
    /// 用 Selection.assetGUIDs 而不是 Selection.GetFiltered(typeof(GameObject),
    /// SelectionMode.Assets)——后者在 Project 窗口里拿不到东西，而且完全不认文件夹。
    /// 素材是成批放在目录里的，直接选目录才是常态。
    /// </summary>
    private static List<GameObject> CollectSelectedModels()
    {
        var result = new List<GameObject>();
        var seen = new HashSet<string>();

        string[] guids = Selection.assetGUIDs;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path)) continue;

            if (AssetDatabase.IsValidFolder(path))
            {
                // 选中目录：递归扫下面所有的模型/预制体
                string[] inner = AssetDatabase.FindAssets("t:GameObject", new[] { path });
                for (int k = 0; k < inner.Length; k++)
                    AddByPath(AssetDatabase.GUIDToAssetPath(inner[k]), result, seen);
                continue;
            }

            AddByPath(path, result, seen);
        }

        result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return result;
    }

    private static void AddByPath(string path, List<GameObject> result, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(path) || !seen.Add(path))
            return;

        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go != null)
            result.Add(go);
    }

    private void Generate(List<GameObject> models)
    {
        EnsureFolder(CustomFolder);

        HashSet<GameObject> alreadyBound = CollectBoundPrefabs();
        HashSet<string> usedIds = CollectUsedIds();
        var icon = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultIconPath);

        int created = 0;
        int skipped = 0;
        var log = new StringBuilder();

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < models.Count; i++)
            {
                GameObject model = models[i];
                if (alreadyBound.Contains(model))
                {
                    skipped++;
                    continue;
                }

                TerrainDecorationDefinition def = BuildDefinitionAsset(model, usedIds, icon);
                created++;
                log.Append("\n  ").Append(model.name).Append("  →  ").Append(def.name);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[地形装饰物] 批量生成完成：新建 {created} 个，跳过 {skipped} 个（已被现有定义绑过）。{log}");
        EditorUtility.DisplayDialog("批量生成",
            $"新建 {created} 个定义。\n跳过 {skipped} 个（模型已被现有定义绑过）。\n\n" +
            "碰撞盒是按模型包围盒估算的，高的或形状特殊的记得在定义页复核。", "确定");
    }

    /// <summary>按当前窗口设置从一个模型建出定义资产。资产路径和碰撞盒都在这里算。</summary>
    private TerrainDecorationDefinition BuildDefinitionAsset(
        GameObject model, HashSet<string> usedIds, Sprite icon)
    {
        var def = CreateInstance<TerrainDecorationDefinition>();
        def.decorationId = MakeUniqueId(model.name, usedIds);
        def.displayName = model.name;
        def.category = category;
        def.subCategory = string.IsNullOrWhiteSpace(subCategory) ? "Custom" : subCategory;
        def.icon = icon;
        def.blockPlayer = blockPlayer;
        def.walkableSurface = walkableSurface;

        def.variants.Add(new TerrainDecorationVariant
        {
            variantId = "default",
            displayName = "默认版本",
            prefab = model,
            weight = 1,
        });

        if (collisionFit != CollisionFit.None && TryMeasureBounds(model, out Bounds b))
        {
            Vector3 size = b.size;
            Vector3 center = b.center;

            if (collisionFit == CollisionFit.BaseOnly)
            {
                // 高的东西（树、灯柱）用完整包围盒当碰撞会挡出一大片，
                // 底部贴合只取下部，走上去的手感更接近视觉。
                float h = Mathf.Max(0.05f, size.y * 0.3f);
                center.y = b.min.y + h * 0.5f;
                size.y = h;
            }

            def.collisionSize = Sanitize(size);
            def.collisionOffset = center;
        }

        string path = AssetDatabase.GenerateUniqueAssetPath($"{CustomFolder}/TD_{def.decorationId}.asset");
        AssetDatabase.CreateAsset(def, path);
        return def;
    }

    /// <summary>
    /// 把场景里选中的对象原地换成结构化的地形装饰物实例。
    ///
    /// 走的是放置工具那条链路里两边共有的骨架：
    ///   Binder → VisualRoot → InstantiatePrefab → Applier.ApplyDefinition()
    ///   → InstanceBuilder.BuildStructureFromDefinition
    /// 放置工具还会做变体随机抽取、材质随机选择、贴花 sortingOrder 压制——那些是
    /// "从定义随机摆一个"才需要的，1:1 转换只有一个变体、没有随机，所以不复制过来。
    /// 需要多变体/材质随机的物件，走放置工具重新摆。
    ///
    /// 原对象用 Undo.DestroyObjectImmediate 删除，Ctrl+Z 能整体撤销。
    /// </summary>
    private void ConvertSceneObjects(List<GameObject> sceneObjects)
    {
        EnsureFolder(CustomFolder);

        var boundToDefinition = new Dictionary<GameObject, TerrainDecorationDefinition>();
        string[] guids = AssetDatabase.FindAssets("t:TerrainDecorationDefinition");
        for (int i = 0; i < guids.Length; i++)
        {
            var def = AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
            if (def == null || def.variants == null) continue;
            for (int v = 0; v < def.variants.Count; v++)
                if (def.variants[v] != null && def.variants[v].prefab != null)
                    boundToDefinition[def.variants[v].prefab] = def;
        }

        HashSet<string> usedIds = CollectUsedIds();
        var icon = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultIconPath);
        int worldLayer = LayerMask.NameToLayer("World3D");

        int converted = 0;
        int newDefs = 0;
        var log = new StringBuilder();

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Convert To Terrain Decorations");
        int undoGroup = Undo.GetCurrentGroup();

        for (int i = 0; i < sceneObjects.Count; i++)
        {
            GameObject src = sceneObjects[i];
            if (src == null) continue;

            GameObject sourcePrefab = ResolveSourcePrefab(src);
            if (sourcePrefab == null) continue;

            // 同一个源预制体只建一个定义，第二个开始复用
            if (!boundToDefinition.TryGetValue(sourcePrefab, out TerrainDecorationDefinition def))
            {
                def = BuildDefinitionAsset(sourcePrefab, usedIds, icon);
                boundToDefinition[sourcePrefab] = def;
                newDefs++;
            }

            Transform t = src.transform;
            var root = new GameObject(string.IsNullOrEmpty(def.displayName) ? sourcePrefab.name : def.displayName);
            Undo.RegisterCreatedObjectUndo(root, "Create Terrain Decoration");

            root.transform.SetParent(t.parent, false);
            root.transform.SetSiblingIndex(t.GetSiblingIndex());
            root.transform.localPosition = t.localPosition;
            root.transform.localRotation = t.localRotation;
            root.transform.localScale = Vector3.one;
            if (worldLayer >= 0) SetLayerRecursively(root, worldLayer);

            var binder = root.AddComponent<TerrainDecorationRuntimeBinder>();
            binder.definition = def;
            binder.instanceId = def.decorationId + "_" + System.DateTime.Now.ToString("yyyyMMddHHmmssfff") + "_" + i;
            binder.selectedVariantId = def.variants.Count > 0 ? def.variants[0].variantId : "default";
            binder.finalScale = t.localScale;

            var visualRoot = new GameObject("VisualRoot").transform;
            visualRoot.SetParent(root.transform, false);
            visualRoot.localPosition = Vector3.zero;
            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localScale = t.localScale;

            var visual = PrefabUtility.InstantiatePrefab(sourcePrefab, visualRoot) as GameObject;
            if (visual != null)
            {
                visual.name = sourcePrefab.name;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                StripDecorationComponents(visual);
                if (worldLayer >= 0) SetLayerRecursively(visual, worldLayer);
            }

            var applier = root.AddComponent<TerrainDecorationRuntimeApplier>();
            DisableApplierAutoApply(applier);
            applier.ApplyDefinition();
            SkyPrisonTerrainDecorationInstanceBuilder.BuildStructureFromDefinition(root, def, false);
            DisableApplierAutoApply(applier);

            Undo.DestroyObjectImmediate(src);
            converted++;
            log.Append("\n  ").Append(sourcePrefab.name).Append("  →  ").Append(def.name);
        }

        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssets();

        Debug.Log($"[地形装饰物] 场景转换完成：转换 {converted} 个实例，新建 {newDefs} 个定义。{log}");
        EditorUtility.DisplayDialog("场景转换",
            $"转换了 {converted} 个场景对象。\n新建 {newDefs} 个定义（同一源预制体复用同一个定义）。\n\n" +
            "碰撞盒按包围盒估算，形状特殊的记得在定义页复核。Ctrl+Z 可整体撤销。", "确定");
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    /// <summary>视觉子树里不能留装饰物自己的组件，否则会被当成嵌套装饰物。</summary>
    private static void StripDecorationComponents(GameObject visual)
    {
        foreach (var c in visual.GetComponentsInChildren<TerrainDecorationRuntimeApplier>(true))
            DestroyImmediate(c);
        foreach (var c in visual.GetComponentsInChildren<TerrainDecorationRuntimeBinder>(true))
            DestroyImmediate(c);
    }

    /// <summary>applyOnEnable / applyInEditMode 是私有字段，只能走 SerializedObject。</summary>
    private static void DisableApplierAutoApply(TerrainDecorationRuntimeApplier applier)
    {
        if (applier == null) return;
        var so = new SerializedObject(applier);
        SerializedProperty a = so.FindProperty("applyOnEnable");
        SerializedProperty b = so.FindProperty("applyInEditMode");
        if (a != null) a.boolValue = false;
        if (b != null) b.boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>实例化一份临时对象来量包围盒——预制体资产上的 Renderer.bounds 不可靠。</summary>
    private static bool TryMeasureBounds(GameObject model, out Bounds bounds)
    {
        bounds = default;
        GameObject temp = null;
        try
        {
            temp = Instantiate(model);
            temp.hideFlags = HideFlags.HideAndDontSave;
            temp.transform.position = Vector3.zero;
            temp.transform.rotation = Quaternion.identity;

            Renderer[] renderers = temp.GetComponentsInChildren<Renderer>();
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || !renderers[i].enabled) continue;
                if (any) bounds.Encapsulate(renderers[i].bounds);
                else { bounds = renderers[i].bounds; any = true; }
            }
            return any;
        }
        finally
        {
            if (temp != null) DestroyImmediate(temp);
        }
    }

    private static HashSet<GameObject> CollectBoundPrefabs()
    {
        var set = new HashSet<GameObject>();
        string[] guids = AssetDatabase.FindAssets("t:TerrainDecorationDefinition");
        for (int i = 0; i < guids.Length; i++)
        {
            var def = AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
            if (def == null || def.variants == null) continue;
            for (int v = 0; v < def.variants.Count; v++)
                if (def.variants[v] != null && def.variants[v].prefab != null)
                    set.Add(def.variants[v].prefab);
        }
        return set;
    }

    private static HashSet<string> CollectUsedIds()
    {
        var set = new HashSet<string>();
        string[] guids = AssetDatabase.FindAssets("t:TerrainDecorationDefinition");
        for (int i = 0; i < guids.Length; i++)
        {
            var def = AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
            if (def != null && !string.IsNullOrWhiteSpace(def.decorationId))
                set.Add(def.decorationId);
        }
        return set;
    }

    private static string MakeUniqueId(string rawName, HashSet<string> used)
    {
        var sb = new StringBuilder();
        foreach (char c in rawName.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');

        string baseId = sb.ToString().Trim('_');
        if (string.IsNullOrEmpty(baseId))
            baseId = "terrain_decoration";

        string id = baseId;
        int n = 2;
        while (used.Contains(id))
            id = baseId + "_" + n++;

        used.Add(id);
        return id;
    }

    private static Vector3 Sanitize(Vector3 size) => new Vector3(
        Mathf.Max(0.05f, Mathf.Abs(size.x)),
        Mathf.Max(0.05f, Mathf.Abs(size.y)),
        Mathf.Max(0.05f, Mathf.Abs(size.z)));

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
