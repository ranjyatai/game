
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonMapEditorPage : SkyPrisonEditorPageBase
{
    public const string MapDefinitionRootFolder = "Assets/_Project/Maps";
    public const string DefaultMapCreateFolder = "Assets/_Project/Maps";
    public const string DefaultSceneFolderName = "Scenes";

    private readonly Color leftTopBg = new Color(0.13f, 0.13f, 0.14f, 1f);
    private readonly Color accentGreen = new Color(0.33f, 0.70f, 0.52f, 1f);
    private readonly Color selectedRowGreen = new Color(0.18f, 0.42f, 0.28f, 0.34f);
    private readonly Color selectedFolderGreen = new Color(0.21f, 0.38f, 0.25f, 0.28f);

    private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>
    {
        { "基础信息", true },
        { "多语言名称", true },
        { "多语言描述", true },
        { "地图边界", true },
        { "战争迷雾", true },
        { "天空与环境", true },
        { "天气", true },
        { "触发器", true },
        { "镜头表现", true },
        { "Scene 关联", true },
        { "基础节点", true },
    };

    private readonly SkyPrisonMapAssetListPanel assetListPanel;
    private readonly SkyPrisonMapInspectorPanel inspectorPanel;

    private List<MapDefinition> maps = new List<MapDefinition>();
    private MapDefinition selectedMap;
    private SerializedObject selectedMapSO;

    public SkyPrisonMapEditorPage(SkyPrisonEditorContext context) : base(context)
    {
        assetListPanel = new SkyPrisonMapAssetListPanel(this);
        inspectorPanel = new SkyPrisonMapInspectorPanel(this);
    }

    public override string TabName => "地图编辑器";
    public Color AccentGreen => accentGreen;
    public Color SelectedRowGreen => selectedRowGreen;
    public Color SelectedFolderGreen => selectedFolderGreen;
    public Color LeftTopBg => leftTopBg;
    public Dictionary<string, bool> Foldouts => foldouts;
    public List<MapDefinition> Maps => maps;
    public MapDefinition SelectedMap => selectedMap;
    public SerializedObject SelectedMapSO => selectedMapSO;

    public override void OnEnable() { Refresh(); }

    public override void Refresh()
    {
        string selectedPath = selectedMap != null ? AssetDatabase.GetAssetPath(selectedMap) : "";
        EnsureFolderExists(MapDefinitionRootFolder);

        string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { MapDefinitionRootFolder });
        maps = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<MapDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(x => x != null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
            .ThenBy(x => x.name)
            .ToList();

        selectedMap = null;
        if (!string.IsNullOrEmpty(selectedPath))
        {
            MapDefinition matched = maps.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == selectedPath);
            if (matched != null)
                selectedMap = matched;
        }

        if (selectedMap == null && maps.Count > 0)
            selectedMap = maps[0];

        selectedMapSO = selectedMap != null ? new SerializedObject(selectedMap) : null;
        assetListPanel.OnRefresh();
        Context.Repaint();
    }

    public override void OnGUILeft() { assetListPanel.Draw(); }

    public override void OnGUIRight()
    {
        if (selectedMap == null)
        {
            EditorGUILayout.HelpBox("请先在左侧选择一个地图定义。", MessageType.Info);
            return;
        }

        EnsureSelectedSerializedObject();
        selectedMapSO.Update();
        inspectorPanel.Draw();
        DrawGroundTerrainAudioResolverBootstrapSection();
        DrawAudioListenerAnchorBootstrapSection();
        selectedMapSO.ApplyModifiedProperties();

        if (GUI.changed)
            EditorUtility.SetDirty(selectedMap);
    }

    public override void HandlePostGUI() { assetListPanel.HandlePostGUI(); }

    public void SelectMap(MapDefinition map)
    {
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;

        if (selectedMapSO != null)
        {
            selectedMapSO.ApplyModifiedProperties();
            selectedMapSO = null;
        }

        selectedMap = map;
        if (selectedMap != null)
            selectedMapSO = new SerializedObject(selectedMap);

        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    public void ClearSelectedMapAndSO()
    {
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;

        if (selectedMapSO != null)
        {
            selectedMapSO.ApplyModifiedProperties();
            selectedMapSO = null;
        }

        selectedMap = null;
        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    public void EnsureSelectedSerializedObject()
    {
        if (selectedMap == null)
            return;

        if (selectedMapSO == null || selectedMapSO.targetObject != selectedMap)
            selectedMapSO = new SerializedObject(selectedMap);
    }

    public void DrawFoldoutSection(string title, System.Action drawContent)
    {
        bool expanded = Foldouts.ContainsKey(title) ? Foldouts[title] : true;
        EditorGUILayout.BeginVertical("box");
        Foldouts[title] = EditorGUILayout.Foldout(expanded, title, true);
        if (Foldouts[title])
        {
            EditorGUILayout.Space(4f);
            drawContent?.Invoke();
        }
        EditorGUILayout.EndVertical();
    }

    public void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    public void DrawReadonlyRow(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(value) ? "-" : value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    public void DrawRow(string label, SerializedProperty prop, bool multiline = false)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        if (prop == null)
            EditorGUILayout.LabelField("字段不存在");
        else if (multiline && prop.propertyType == SerializedPropertyType.String)
            prop.stringValue = EditorGUILayout.TextArea(prop.stringValue, GUILayout.MinHeight(42f));
        else
            EditorGUILayout.PropertyField(prop, GUIContent.none, true);
        EditorGUILayout.EndHorizontal();
    }

    public void CreateNewMap(string targetFolder)
    {
        string folder = string.IsNullOrWhiteSpace(targetFolder) ? DefaultMapCreateFolder : targetFolder;
        EnsureFolderExists(folder);

        SkyPrisonCreateMapWindow.Open(result =>
        {
            MapDefinition created = SkyPrisonMapEditorUtility.CreateMap(result, folder);
            string createdPath = created != null ? AssetDatabase.GetAssetPath(created).Replace("\\", "/") : "";

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            if (created != null)
            {
                // 关键：不要先 Refresh()。
                // Refresh() 依赖 FindAssets，而新建后的当前帧 FindAssets 可能还没把 MD_xxx.asset 纳入结果。
                // 先把返回的 created 直接塞进当前 maps 缓存，左侧树 BuildTree 就能立刻把它识别成地图包。
                if (!maps.Contains(created))
                    maps.Add(created);

                SelectMap(created);
                assetListPanel.FocusCreatedMap(created, createdPath, true);
                Context.Repaint();
            }
            else
            {
                Refresh();
                return;
            }

            // 下一帧再走一次真正的刷新按钮逻辑，用 AssetDatabase 的稳定结果替换临时缓存。
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.ImportAsset(createdPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                Refresh();

                MapDefinition matched = maps.FirstOrDefault(x => AssetDatabase.GetAssetPath(x).Replace("\\", "/") == createdPath);
                if (matched == null)
                    matched = AssetDatabase.LoadAssetAtPath<MapDefinition>(createdPath);

                if (matched != null)
                {
                    if (!maps.Contains(matched))
                        maps.Add(matched);

                    SelectMap(matched);
                    assetListPanel.FocusCreatedMap(matched, createdPath, true);
                }

                Context.Repaint();
            };
        });
    }

    public void CreateNewMap()
    {
        CreateNewMap(DefaultMapCreateFolder);
    }


    // V2 - 2026-05-28
    // 地表脚步声音声解析器挂接入口。
    // 放在地图编辑器右侧基础节点校对区域之后，专门把 SkyPrisonGroundAudioSurfaceResolver 挂到当前 Scene 的 GroundTerrain 上。
    private void DrawGroundTerrainAudioResolverBootstrapSection()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("地表音声解析", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "把脚步声/敌人听觉使用的地表音声解析器挂到当前 Scene 的 GroundTerrain 上。已有组件不会重复添加，只会补齐 Terrain 引用并自动收集 GroundSurfaceMaterialDefinition。",
            MessageType.Info);

        GameObject groundTerrain = FindGroundTerrainObject();
        Terrain terrain = groundTerrain != null ? groundTerrain.GetComponent<Terrain>() : null;
        SkyPrisonGroundAudioSurfaceResolver resolver = groundTerrain != null
            ? groundTerrain.GetComponent<SkyPrisonGroundAudioSurfaceResolver>()
            : null;

        DrawReadonlyRow("GroundTerrain", groundTerrain != null ? GetHierarchyPath(groundTerrain.transform) : "未找到");
        DrawReadonlyRow("Terrain", terrain != null ? terrain.name : "未找到 Terrain 组件");
        DrawReadonlyRow("解析器", resolver != null ? "已挂接" : "未挂接");

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);

        using (new EditorGUI.DisabledScope(groundTerrain == null || terrain == null))
        {
            if (GUILayout.Button("挂接/修复地表音声解析器", GUILayout.Width(210f), GUILayout.Height(24f)))
                AttachOrRepairGroundAudioSurfaceResolver(groundTerrain, terrain);
        }

        using (new EditorGUI.DisabledScope(groundTerrain == null))
        {
            if (GUILayout.Button("定位 GroundTerrain", GUILayout.Width(140f), GUILayout.Height(24f)))
            {
                Selection.activeGameObject = groundTerrain;
                EditorGUIUtility.PingObject(groundTerrain);
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (groundTerrain == null)
            EditorGUILayout.HelpBox("当前 Scene 没找到名为 GroundTerrain 的节点。请先执行基础节点校对，或确认地面 Terrain 节点名称。", MessageType.Warning);
        else if (terrain == null)
            EditorGUILayout.HelpBox("找到 GroundTerrain，但它身上没有 Terrain 组件。解析器需要绑定真实 Unity Terrain。", MessageType.Warning);

        EditorGUILayout.EndVertical();
    }

    private void AttachOrRepairGroundAudioSurfaceResolver(GameObject groundTerrain, Terrain terrain)
    {
        if (groundTerrain == null || terrain == null)
            return;

        SkyPrisonGroundAudioSurfaceResolver resolver = groundTerrain.GetComponent<SkyPrisonGroundAudioSurfaceResolver>();
        Undo.RecordObject(groundTerrain, "Attach Ground Audio Surface Resolver");

        if (resolver == null)
            resolver = Undo.AddComponent<SkyPrisonGroundAudioSurfaceResolver>(groundTerrain);

        SerializedObject resolverSO = new SerializedObject(resolver);
        SerializedProperty terrainProp = resolverSO.FindProperty("terrain");
        if (terrainProp != null)
            terrainProp.objectReferenceValue = terrain;

        SerializedProperty autoFindTerrainProp = resolverSO.FindProperty("autoFindTerrain");
        if (autoFindTerrainProp != null)
            autoFindTerrainProp.boolValue = true;

        resolverSO.ApplyModifiedPropertiesWithoutUndo();

#if UNITY_EDITOR
        resolver.EditorAutoCollectGroundSurfaceDefinitions();
#endif
        resolver.RefreshResolverCache();

        EditorUtility.SetDirty(resolver);
        EditorUtility.SetDirty(groundTerrain);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(groundTerrain.scene);

        Selection.activeGameObject = groundTerrain;
        EditorGUIUtility.PingObject(groundTerrain);

        Debug.Log($"[SkyPrisonMapEditorPage] GroundTerrain 已挂接/修复 SkyPrisonGroundAudioSurfaceResolver：{GetHierarchyPath(groundTerrain.transform)}", groundTerrain);
    }

    private static GameObject FindGroundTerrainObject()
    {
        GameObject exact = GameObject.Find("GroundTerrain");
        if (exact != null)
            return exact;

        Terrain activeTerrain = Terrain.activeTerrain;
        if (activeTerrain != null && activeTerrain.gameObject.name == "GroundTerrain")
            return activeTerrain.gameObject;

        Terrain[] terrains = Object.FindObjectsOfType<Terrain>();
        if (terrains != null)
        {
            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain t = terrains[i];
                if (t != null && t.gameObject.name == "GroundTerrain")
                    return t.gameObject;
            }
        }

        return null;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
            return "-";

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }



    // V3 - 2026-05-28
    // 2.5D 音声监听结构校对入口。
    // 摄像机继续负责画面；AudioListener 独立为 AudioListenerRoot，并跟随 Player 根坐标。
    private void DrawAudioListenerAnchorBootstrapSection()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("2.5D 听觉锚点", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "修复 2.5D 摄像机距离导致的 3D 脚步声衰减错误：禁用摄像机/其它节点上的 AudioListener，创建 AudioListenerRoot，并让它运行时跟随 Player 根坐标。",
            MessageType.Info);

        GameObject listenerRoot = FindAudioListenerRootObject();
        SkyPrisonPlayerAudioListenerAnchor anchor = listenerRoot != null ? listenerRoot.GetComponent<SkyPrisonPlayerAudioListenerAnchor>() : null;
        AudioListener rootListener = listenerRoot != null ? listenerRoot.GetComponent<AudioListener>() : null;
        Transform playerRoot = FindBestPlayerRootInOpenScene();
        AudioListener[] listeners = UnityEngine.Object.FindObjectsOfType<AudioListener>(true);
        int enabledCount = 0;
        string enabledSummary = "-";

        if (listeners != null && listeners.Length > 0)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener listener = listeners[i];
                if (listener == null || !listener.enabled)
                    continue;

                enabledCount++;
                if (sb.Length > 0)
                    sb.Append(" | ");
                sb.Append(GetHierarchyPath(listener.transform));
            }
            enabledSummary = sb.Length > 0 ? sb.ToString() : "无启用 AudioListener";
        }

        DrawReadonlyRow("Player 根", playerRoot != null ? GetHierarchyPath(playerRoot) : "未找到");
        DrawReadonlyRow("AudioListenerRoot", listenerRoot != null ? GetHierarchyPath(listenerRoot.transform) : "未创建");
        DrawReadonlyRow("锚点脚本", anchor != null ? "已挂接" : "未挂接");
        DrawReadonlyRow("Root Listener", rootListener != null ? (rootListener.enabled ? "已启用" : "存在但未启用") : "未挂接");
        DrawReadonlyRow("启用 Listener", enabledCount + " / " + (listeners != null ? listeners.Length : 0));

        EditorGUILayout.HelpBox("当前启用：" + enabledSummary, enabledCount == 1 && rootListener != null && rootListener.enabled ? MessageType.Info : MessageType.Warning);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);

        if (GUILayout.Button("校对/修复 AudioListener 结构", GUILayout.Width(230f), GUILayout.Height(24f)))
            RepairAudioListenerAnchorStructure();

        using (new EditorGUI.DisabledScope(listenerRoot == null))
        {
            if (GUILayout.Button("定位 AudioListenerRoot", GUILayout.Width(170f), GUILayout.Height(24f)))
            {
                Selection.activeGameObject = listenerRoot;
                EditorGUIUtility.PingObject(listenerRoot);
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (playerRoot == null)
            EditorGUILayout.HelpBox("当前 Scene 没找到 Player 根。按钮仍会创建 AudioListenerRoot；运行时锚点脚本会继续自动寻找 Player。", MessageType.Warning);

        EditorGUILayout.EndVertical();
    }

    private void RepairAudioListenerAnchorStructure()
    {
        Transform playerRoot = FindBestPlayerRootInOpenScene();
        GameObject listenerRoot = FindAudioListenerRootObject();

        if (listenerRoot == null)
        {
            listenerRoot = new GameObject("AudioListenerRoot");
            Undo.RegisterCreatedObjectUndo(listenerRoot, "Create AudioListenerRoot");
        }
        else
        {
            Undo.RecordObject(listenerRoot, "Repair AudioListenerRoot");
        }

        listenerRoot.transform.SetParent(null, true);

        AudioListener rootListener = listenerRoot.GetComponent<AudioListener>();
        if (rootListener == null)
            rootListener = Undo.AddComponent<AudioListener>(listenerRoot);

        SkyPrisonPlayerAudioListenerAnchor anchor = listenerRoot.GetComponent<SkyPrisonPlayerAudioListenerAnchor>();
        if (anchor == null)
            anchor = Undo.AddComponent<SkyPrisonPlayerAudioListenerAnchor>(listenerRoot);

        if (playerRoot != null)
            listenerRoot.transform.position = playerRoot.position + new Vector3(0f, 1.5f, 0f);

        anchor.Configure(playerRoot, 1.5f, true);

        AudioListener[] listeners = UnityEngine.Object.FindObjectsOfType<AudioListener>(true);
        if (listeners != null)
        {
            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener listener = listeners[i];
                if (listener == null)
                    continue;

                Undo.RecordObject(listener, "Repair AudioListener Structure");
                listener.enabled = listener == rootListener;
                EditorUtility.SetDirty(listener);
            }
        }

        rootListener.enabled = true;
        EditorUtility.SetDirty(rootListener);
        EditorUtility.SetDirty(anchor);
        EditorUtility.SetDirty(listenerRoot);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(listenerRoot.scene);

        Selection.activeGameObject = listenerRoot;
        EditorGUIUtility.PingObject(listenerRoot);

        Debug.Log("[SkyPrisonMapEditorPage] 已校对 AudioListener 结构：AudioListenerRoot 为唯一启用监听器，运行时跟随 Player 根。player=" + (playerRoot != null ? GetHierarchyPath(playerRoot) : "未找到"), listenerRoot);
    }

    private static GameObject FindAudioListenerRootObject()
    {
        GameObject exact = GameObject.Find("AudioListenerRoot");
        if (exact != null)
            return exact;

        SkyPrisonPlayerAudioListenerAnchor anchor = UnityEngine.Object.FindObjectOfType<SkyPrisonPlayerAudioListenerAnchor>(true);
        if (anchor != null)
            return anchor.gameObject;

        return null;
    }

    private static Transform FindBestPlayerRootInOpenScene()
    {
        GameObject exact = GameObject.Find("Player");
        if (exact != null)
            return exact.transform;

        Transform[] transforms = UnityEngine.Object.FindObjectsOfType<Transform>(true);
        Transform best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            if (t == null)
                continue;

            int score = ScorePlayerRootCandidate(t);
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return bestScore > 0 ? best : null;
    }

    private static int ScorePlayerRootCandidate(Transform t)
    {
        string path = GetHierarchyPath(t);
        string name = t.name;
        int score = 0;

        if (name == "Player") score += 1000;
        if (name.Contains("Player")) score += 200;
        if (path.Contains("PlayerRoot")) score += 300;
        if (path.Contains("PlayerRuntime")) score += 300;
        if (t.GetComponent("UnitMovementController") != null) score += 120;
        if (t.GetComponent("UnitDefinitionRuntimeBinder") != null) score += 120;
        if (t.GetComponentInChildren<Spine.Unity.SkeletonAnimation>(true) != null) score += 40;

        if (path.Contains("EnemyRoot") || path.Contains("EnemyRuntime")) score -= 1000;
        if (name.Contains("Enemy")) score -= 1000;
        if (name.Contains("Camera")) score -= 500;
        if (name.Contains("AudioListenerRoot")) score -= 500;

        return score;
    }

    public void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
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
