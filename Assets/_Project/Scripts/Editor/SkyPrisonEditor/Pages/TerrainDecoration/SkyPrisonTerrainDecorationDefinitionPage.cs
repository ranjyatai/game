using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonTerrainDecorationDefinitionPage : SkyPrisonEditorPageBase
{
    private const string StandardFolder = "Assets/_Project/Data/Definitions/Standard/TerrainDecorations";
    private const string CustomFolder = "Assets/_Project/Data/Definitions/Custom/TerrainDecorations";
    private const string DefaultPrefabFolder = "Assets/_Project/Prefabs/TerrainDecorations/Custom";
    private const string DefaultVisualPrefabFolder = "Assets/_Project/Art/Prefabs/TerrainDecoration";
    private const string DefaultIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_34.png";
    private const string PhysicsSettingsFolder = "Assets/_Project/Data/Definitions/Physics/TerrainDecorations";

    private readonly List<TerrainDecorationDefinition> definitions = new List<TerrainDecorationDefinition>();
    private readonly Dictionary<string, bool> categoryFoldouts = new Dictionary<string, bool>();
    private readonly Dictionary<string, Vector2> materialSlotScrolls = new Dictionary<string, Vector2>();
    private readonly Dictionary<string, bool> sectionFoldouts = new Dictionary<string, bool>
    {
        { "基础信息", true },
        { "视觉方案", true },
        { "容器结构", true },
        { "放置规则", true },
        { "随机化", true },
        { "碰撞", true },
        { "物理结构", false },
        { "规则空间 / 正背面", true },
        { "遮挡 / 阴影 / 迷雾", true },
        { "前后遮挡投影基准", true },
        { "环境音", true },
        { "编辑器显示", false },
    };

    private TerrainDecorationDefinition selectedDefinition;
    private SerializedObject selectedSO;
    private Vector2 leftScroll;
    private string search = "";

    private static TerrainDecorationDefinition clipboardSnapshot;
    private static bool clipboardFromCut;
    private static string clipboardSourceDecorationId = "";

    private readonly Color accent = new Color(1.00f, 0.24f, 0.08f, 1f);
    private readonly Color leftBg = new Color(0.13f, 0.13f, 0.14f, 1f);

    public SkyPrisonTerrainDecorationDefinitionPage(SkyPrisonEditorContext context) : base(context) { }

    public override string TabName => "地形装饰物";

    public override void OnEnable()
    {
        Refresh();
    }

    public override void Refresh()
    {
        string selectedPath = selectedDefinition != null ? AssetDatabase.GetAssetPath(selectedDefinition) : "";
        definitions.Clear();

        string[] guids = AssetDatabase.FindAssets("t:TerrainDecorationDefinition");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            TerrainDecorationDefinition def = AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(path);
            if (def != null)
                definitions.Add(def);
        }

        definitions.Sort((a, b) =>
        {
            int c = string.Compare(GetCategoryLabel(a), GetCategoryLabel(b), System.StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return string.Compare(GetDisplayName(a), GetDisplayName(b), System.StringComparison.OrdinalIgnoreCase);
        });

        if (!string.IsNullOrEmpty(selectedPath))
        {
            TerrainDecorationDefinition matched = definitions.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == selectedPath);
            if (matched != null)
                SelectDefinition(matched);
        }

        if (selectedDefinition == null && definitions.Count > 0)
            SelectDefinition(definitions[0]);
    }

    public void TrySelectObject(UnityEngine.Object obj)
    {
        TerrainDecorationDefinition definition = obj as TerrainDecorationDefinition;
        if (definition != null)
            SelectDefinition(definition);
    }

    public override void OnGUILeft()
    {
        EditorGUILayout.LabelField("地形装饰物", EditorStyles.boldLabel);
        GUILayout.Space(6f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+", GUILayout.Width(28f), GUILayout.Height(22f)))
            CreateDefinition(false);
        using (new EditorGUI.DisabledScope(selectedDefinition == null || selectedDefinition.isStandard))
        {
            if (GUILayout.Button("-", GUILayout.Width(28f), GUILayout.Height(22f)))
                DeleteSelectedDefinition();
        }
        if (GUILayout.Button("刷新", GUILayout.Height(22f)))
            Refresh();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6f);
        search = EditorGUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.textField);
        GUILayout.Space(6f);

        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, leftBg);

        Rect viewRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
        List<TerrainDecorationDefinition> filtered = GetFilteredDefinitions();
        Dictionary<string, List<TerrainDecorationDefinition>> groups = filtered
            .GroupBy(GetCategoryLabel)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.ToList());

        float contentHeight = Mathf.Max(viewRect.height, groups.Sum(g => 24f + g.Value.Count * 46f) + 12f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        leftScroll = GUI.BeginScrollView(viewRect, leftScroll, contentRect, false, true);
        float y = 0f;
        foreach (var group in groups)
        {
            Rect header = new Rect(0f, y, contentRect.width, 24f);
            if (!categoryFoldouts.ContainsKey(group.Key))
                categoryFoldouts[group.Key] = true;

            categoryFoldouts[group.Key] = EditorGUI.Foldout(header, categoryFoldouts[group.Key], group.Key, true);
            y += 24f;

            if (!categoryFoldouts[group.Key])
                continue;

            foreach (TerrainDecorationDefinition def in group.Value)
            {
                Rect row = new Rect(0f, y, contentRect.width, 44f);
                DrawDefinitionRow(row, def);
                y += 46f;
            }
        }
        GUI.EndScrollView();

        HandleLeftListKeyboardShortcuts(rect);
    }

    public override void OnGUIRight()
    {
        if (selectedDefinition == null)
        {
            EditorGUILayout.HelpBox("请先创建或选择一个地形装饰物定义。", MessageType.Info);
            return;
        }

        if (selectedSO == null || selectedSO.targetObject != selectedDefinition)
            selectedSO = new SerializedObject(selectedDefinition);

        selectedSO.Update();

        DrawHeader();
        GUILayout.Space(8f);
        DrawAutomationBar();
        GUILayout.Space(8f);

        DrawSection("基础信息", DrawBasicInfo);
        DrawSection("视觉方案", DrawVisualVariants);
        DrawSection("容器结构", DrawStructure);
        DrawSection("放置规则", DrawPlacement);
        DrawSection("随机化", DrawRandomization);
        DrawSection("碰撞", DrawCollision);
        DrawSection("物理结构", DrawPhysicsStructure);
        DrawSection("规则空间 / 正背面", DrawRuleSpace);
        DrawSection("遮挡 / 阴影 / 迷雾", DrawOcclusionShadowFog);
        DrawSection("高层建筑物 / 高度淡出", DrawHeightFade);
        DrawSection("前后遮挡投影基准", DrawFrontBackOcclusionProjectionSettings);
        DrawSection("环境音", DrawEnvironmentAudio);
        DrawSection("编辑器显示", DrawEditorDisplay);

        selectedSO.ApplyModifiedProperties();
        if (GUI.changed)
            EditorUtility.SetDirty(selectedDefinition);
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("地形装饰物定义工作台", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(GetDisplayName(selectedDefinition), EditorStyles.miniBoldLabel);
        GUILayout.Space(4f);
        DrawReadonlyText("资源路径", AssetDatabase.GetAssetPath(selectedDefinition));
        DrawReadonlyText("装饰物 ID", selectedDefinition.decorationId);
        DrawReadonlyText("分类", GetCategoryLabel(selectedDefinition));
        EditorGUILayout.EndVertical();
    }

    private void DrawAutomationBar()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("定义页边界", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "当前页面只负责编辑 TerrainDecorationDefinition。\n" +
            "这里不再提供矫正、修复 Prefab、修复已摆放实例、生成 RuntimeTemplate、批量扫描场景等暗箱操作。\n" +
            "地形装饰物结构必须在放置瞬间由正式 Builder 按定义生成。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("验证当前定义", GUILayout.Height(24f)))
            ShowDefinitionValidationReport(selectedDefinition);

        if (GUILayout.Button("在 Project 中定位", GUILayout.Width(120f), GUILayout.Height(24f)))
        {
            EditorGUIUtility.PingObject(selectedDefinition);
            Selection.activeObject = selectedDefinition;
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void ShowDefinitionValidationReport(TerrainDecorationDefinition def)
    {
        if (def == null)
            return;

        SerializedObject so = new SerializedObject(def);
        SerializedProperty collisionModeProp = so.FindProperty("collisionMode");
        SerializedProperty collisionSizeProp = so.FindProperty("collisionSize");
        SerializedProperty collisionOffsetProp = so.FindProperty("collisionOffset");
        SerializedProperty occlusionModeProp = so.FindProperty("occlusionMode");

        string collisionMode = GetEnumDisplayNameSafe(collisionModeProp, "字段不存在");
        Vector3 collisionSize = collisionSizeProp != null ? collisionSizeProp.vector3Value : Vector3.zero;
        Vector3 collisionOffset = collisionOffsetProp != null ? collisionOffsetProp.vector3Value : Vector3.zero;
        string occlusionMode = GetEnumDisplayNameSafe(occlusionModeProp, "字段不存在");

        string message =
            "当前定义生成结果预览：\n" +
            $"- 碰撞模式：{collisionMode}\n" +
            $"- 碰撞 Size：{collisionSize}\n" +
            $"- 碰撞 Offset：{collisionOffset}\n" +
            $"- 遮挡模式：{occlusionMode}\n\n" +
            "注意：验证只读，不会修改 Prefab、Scene 实例、Collider、遮挡代理或 RuntimeTemplate。";

        EditorUtility.DisplayDialog("地形装饰物定义验证", message, "知道了");
    }

    private string GetEnumDisplayNameSafe(SerializedProperty prop, string fallback)
    {
        if (prop == null || prop.propertyType != SerializedPropertyType.Enum || prop.enumDisplayNames == null || prop.enumDisplayNames.Length == 0)
            return fallback;
        int index = Mathf.Clamp(prop.enumValueIndex, 0, prop.enumDisplayNames.Length - 1);
        return prop.enumDisplayNames[index];
    }

    private void DrawBasicInfo()
    {
        using (new EditorGUI.DisabledScope(true))
            PropertyField("Decoration ID", "decorationId");
        PropertyField("显示名称", "displayName");
        DrawCategoryPopup();
        PropertyField("子分类", "subCategory");
        PropertyField("图标", "icon");
        PropertyField("标准资源", "isStandard");
        PropertyField("备注", "note", true);
    }

    private void DrawVisualVariants()
    {
        PropertyField("随机 PF 版本", "randomVariantOnPlace");
        PropertyField("按权重随机", "randomVariantByWeight");
        PropertyField("随机材质方案", "randomMaterialOnPlace");

        GUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("PF 搜索目录", GUILayout.Width(150f));
        EditorGUILayout.SelectableLabel(DefaultVisualPrefabFolder, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        if (GUILayout.Button("定位", GUILayout.Width(56f)))
            PingOrCreateVisualPrefabFolder();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "定义页只维护显式选择的视觉 Prefab 和变体数据。\n" +
            "这里不再自动绑定 PF、不批量补 PF，也不自动扫描所有材质槽，避免定义页在背后改资源。",
            MessageType.None);

        GUILayout.Space(4f);
        DrawVariantListWithPrefabPreview();
    }

    private void DrawVariantListWithPrefabPreview()
    {
        SerializedProperty variants = selectedSO.FindProperty("variants");
        if (variants == null)
        {
            EditorGUILayout.LabelField("PF 变体 / 可替换 MAT", "字段不存在");
            return;
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("PF 变体 / 可替换 MAT", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(variants.arraySize.ToString(), GUILayout.Width(28f));
        EditorGUILayout.EndHorizontal();

        if (variants.arraySize == 0)
            EditorGUILayout.HelpBox("当前没有 PF 变体。点击 + 添加一个默认变体。", MessageType.Info);

        for (int i = 0; i < variants.arraySize; i++)
        {
            SerializedProperty item = variants.GetArrayElementAtIndex(i);
            SerializedProperty variantId = item.FindPropertyRelative("variantId");
            SerializedProperty displayName = item.FindPropertyRelative("displayName");
            SerializedProperty prefab = item.FindPropertyRelative("prefab");
            SerializedProperty weight = item.FindPropertyRelative("weight");
            SerializedProperty previewIcon = item.FindPropertyRelative("previewIcon");
            SerializedProperty materialSlots = item.FindPropertyRelative("materialSlots");

            string title = string.IsNullOrWhiteSpace(displayName.stringValue) ? variantId.stringValue : displayName.stringValue;
            if (string.IsNullOrWhiteSpace(title))
                title = "Variant " + i;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();

            item.isExpanded = EditorGUILayout.Foldout(item.isExpanded, title, true);
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(i <= 0))
            {
                if (GUILayout.Button("↑", GUILayout.Width(24f)))
                {
                    variants.MoveArrayElement(i, i - 1);
                    break;
                }
            }

            using (new EditorGUI.DisabledScope(i >= variants.arraySize - 1))
            {
                if (GUILayout.Button("↓", GUILayout.Width(24f)))
                {
                    variants.MoveArrayElement(i, i + 1);
                    break;
                }
            }

            if (GUILayout.Button("-", GUILayout.Width(24f)))
            {
                variants.DeleteArrayElementAtIndex(i);
                break;
            }

            EditorGUILayout.EndHorizontal();

            if (item.isExpanded)
            {
                EditorGUILayout.BeginHorizontal();

                Rect previewRect = GUILayoutUtility.GetRect(92f, 92f, GUILayout.Width(92f), GUILayout.Height(92f));
                DrawVariantPreviewBox(previewRect, prefab.objectReferenceValue as GameObject, previewIcon.objectReferenceValue as Sprite);

                EditorGUILayout.BeginVertical();
                EditorGUILayout.PropertyField(variantId, new GUIContent("Variant Id"));
                EditorGUILayout.PropertyField(displayName, new GUIContent("Display Name"));
                DrawRestrictedPrefabRow(i, prefab);
                EditorGUILayout.PropertyField(weight, new GUIContent("Weight"));
                EditorGUILayout.PropertyField(previewIcon, new GUIContent("Preview Icon"));
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();

                GUILayout.Space(4f);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(98f);
                if (GUILayout.Button("扫描当前 PF 的 MAT 槽", GUILayout.Height(22f)))
                {
                    ScanVariantMaterialSlots(i, false);
                    break;
                }
                if (GUILayout.Button("清空并重扫", GUILayout.Width(92f), GUILayout.Height(22f)))
                {
                    ScanVariantMaterialSlots(i, true);
                    break;
                }
                EditorGUILayout.EndHorizontal();

                DrawMaterialSlots(materialSlots, prefab.objectReferenceValue as GameObject);
            }

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+", GUILayout.Width(42f), GUILayout.Height(22f)))
        {
            int index = variants.arraySize;
            variants.InsertArrayElementAtIndex(index);
            SerializedProperty item = variants.GetArrayElementAtIndex(index);
            item.FindPropertyRelative("variantId").stringValue = GenerateUniqueVariantId(variants, "variant");
            item.FindPropertyRelative("displayName").stringValue = "新版本";
            item.FindPropertyRelative("prefab").objectReferenceValue = null;
            item.FindPropertyRelative("weight").intValue = 1;
            item.FindPropertyRelative("previewIcon").objectReferenceValue = null;
            SerializedProperty slots = item.FindPropertyRelative("materialSlots");
            if (slots != null)
                slots.ClearArray();
            item.isExpanded = true;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }



    private void DrawRestrictedPrefabRow(int variantIndex, SerializedProperty prefabProp)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Prefab", GUILayout.Width(150f));

        GameObject current = prefabProp != null ? prefabProp.objectReferenceValue as GameObject : null;
        Rect fieldRect = GUILayoutUtility.GetRect(120f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));

        bool hover = fieldRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(fieldRect, hover ? new Color(1f, 1f, 1f, 0.08f) : new Color(0.08f, 0.08f, 0.085f, 1f));
        DrawThinBorder(fieldRect, new Color(1f, 1f, 1f, hover ? 0.18f : 0.10f));

        Texture icon = current != null ? AssetPreview.GetMiniThumbnail(current) : AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultIconPath);
        Rect iconRect = new Rect(fieldRect.x + 4f, fieldRect.y + 2f, 16f, 16f);
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);

        string label = current != null ? current.name : "未绑定 PF（限定目录：Art/Prefabs/TerrainDecoration）";
        GUI.Label(new Rect(fieldRect.x + 24f, fieldRect.y, fieldRect.width - 28f, fieldRect.height), label, EditorStyles.label);

        HandlePrefabDragAndDrop(fieldRect, variantIndex, prefabProp);

        if (GUI.Button(fieldRect, GUIContent.none, GUIStyle.none) && current != null)
        {
            Selection.activeObject = current;
            EditorGUIUtility.PingObject(current);
        }

        if (GUILayout.Button("选择PF", GUILayout.Width(66f), GUILayout.Height(EditorGUIUtility.singleLineHeight + 2f)))
            ShowVisualPrefabMenu(variantIndex);

        if (GUILayout.Button("清空", GUILayout.Width(46f), GUILayout.Height(EditorGUIUtility.singleLineHeight + 2f)))
        {
            prefabProp.objectReferenceValue = null;
            if (selectedDefinition != null && selectedDefinition.variants != null && variantIndex >= 0 && variantIndex < selectedDefinition.variants.Count)
            {
                Undo.RecordObject(selectedDefinition, "Clear terrain decoration visual prefab");
                selectedDefinition.variants[variantIndex].prefab = null;
                if (selectedDefinition.variants[variantIndex].materialSlots != null)
                    selectedDefinition.variants[variantIndex].materialSlots.Clear();
                EditorUtility.SetDirty(selectedDefinition);
                AssetDatabase.SaveAssets();
                selectedSO?.Update();
            }
        }

        if (GUILayout.Button("目录", GUILayout.Width(46f), GUILayout.Height(EditorGUIUtility.singleLineHeight + 2f)))
            PingOrCreateVisualPrefabFolder();

        EditorGUILayout.EndHorizontal();
    }

    private void HandlePrefabDragAndDrop(Rect rect, int variantIndex, SerializedProperty prefabProp)
    {
        Event e = Event.current;
        if (e == null || !rect.Contains(e.mousePosition))
            return;

        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
            return;

        GameObject draggedPrefab = null;
        foreach (Object obj in DragAndDrop.objectReferences)
        {
            GameObject go = obj as GameObject;
            if (go != null && IsVisualPrefabInDefaultFolder(go))
            {
                draggedPrefab = go;
                break;
            }
        }

        DragAndDrop.visualMode = draggedPrefab != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            if (draggedPrefab != null)
            {
                prefabProp.objectReferenceValue = draggedPrefab;
                if (selectedDefinition != null && selectedDefinition.variants != null && variantIndex >= 0 && variantIndex < selectedDefinition.variants.Count)
                {
                    selectedDefinition.variants[variantIndex].prefab = draggedPrefab;
                    // 拖拽 PF 后也直接自动扫描 MAT 槽。
                    ScanVariantMaterialSlotsDirect(selectedDefinition, variantIndex, true, false);
                    EditorUtility.SetDirty(selectedDefinition);
                    AssetDatabase.SaveAssets();
                    selectedSO?.Update();
                }
            }
        }

        e.Use();
    }

    private void ShowVisualPrefabMenu(int variantIndex)
    {
        EnsureFolderExists(DefaultVisualPrefabFolder);
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { DefaultVisualPrefabFolder });
        List<GameObject> prefabs = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null)
            .OrderBy(p => p.name)
            .ToList();

        GenericMenu menu = new GenericMenu();

        if (prefabs.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("目录内没有 Prefab"));
        }
        else
        {
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                string menuPath = BuildVisualPrefabMenuPath(prefab);
                bool on = selectedDefinition != null && selectedDefinition.variants != null &&
                          variantIndex >= 0 && variantIndex < selectedDefinition.variants.Count &&
                          selectedDefinition.variants[variantIndex].prefab == prefab;

                menu.AddItem(new GUIContent(menuPath), on, () => AssignVisualPrefabToVariant(variantIndex, prefab));
            }
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("打开 PF 目录"), false, PingOrCreateVisualPrefabFolder);
        menu.ShowAsContext();
    }

    private string BuildVisualPrefabMenuPath(GameObject prefab)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrEmpty(path))
            return prefab != null ? prefab.name : "-";

        string relative = path.StartsWith(DefaultVisualPrefabFolder) ? path.Substring(DefaultVisualPrefabFolder.Length).Trim('/') : prefab.name;
        if (relative.EndsWith(".prefab"))
            relative = relative.Substring(0, relative.Length - ".prefab".Length);

        return string.IsNullOrWhiteSpace(relative) ? prefab.name : relative;
    }

    private void AssignVisualPrefabToVariant(int variantIndex, GameObject prefab)
    {
        if (selectedDefinition == null || selectedDefinition.variants == null || variantIndex < 0 || variantIndex >= selectedDefinition.variants.Count)
            return;

        Undo.RecordObject(selectedDefinition, "Assign terrain decoration visual prefab");
        selectedDefinition.variants[variantIndex].prefab = prefab;
        EditorUtility.SetDirty(selectedDefinition);

        // PF 变体绑定后，直接根据该 PF 自动扫描 MAT 槽。
        // 这里使用 clearExisting = true，避免更换 PF 后保留旧 Renderer Path / MAT 槽造成误绑。
        ScanVariantMaterialSlotsDirect(selectedDefinition, variantIndex, true, false);

        EditorUtility.SetDirty(selectedDefinition);
        AssetDatabase.SaveAssets();
        selectedSO?.Update();
    }

    private bool IsVisualPrefabInDefaultFolder(GameObject prefab)
    {
        if (prefab == null)
            return false;

        string path = AssetDatabase.GetAssetPath(prefab);
        return !string.IsNullOrEmpty(path) && path.StartsWith(DefaultVisualPrefabFolder + "/");
    }

    private void DrawMaterialSlots(SerializedProperty materialSlots, GameObject prefab)
    {
        if (materialSlots == null)
        {
            EditorGUILayout.LabelField("Material Slots", "字段不存在");
            return;
        }

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("MAT 槽 / 可替换材质", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(materialSlots.arraySize.ToString(), GUILayout.Width(28f));
        EditorGUILayout.EndHorizontal();

        if (prefab == null)
        {
            EditorGUILayout.HelpBox("先绑定 PF。绑定或拖拽 PF 后会自动扫描 Renderer / Material 槽。", MessageType.Info);
        }
        else if (materialSlots.arraySize == 0)
        {
            EditorGUILayout.HelpBox("当前没有 MAT 槽。PF 绑定时会自动扫描；如果 PF 结构刚刚修改，也可以点击当前变体上的重扫按钮。", MessageType.Info);
        }

        // 固定高度的深色容器：MAT 槽可能很多，但不要把整个页面撑长。
        // 内容超出时在容器内部滚动。
        const float containerHeight = 138f; // 大约 2~3 行紧凑槽位高度
        float contentHeight = 8f;
        for (int i = 0; i < materialSlots.arraySize; i++)
        {
            SerializedProperty slot = materialSlots.GetArrayElementAtIndex(i);
            SerializedProperty allowedMaterials = slot.FindPropertyRelative("allowedMaterials");
            float allowedHeight = allowedMaterials != null && slot.isExpanded
                ? EditorGUI.GetPropertyHeight(allowedMaterials, true)
                : 0f;
            contentHeight += slot.isExpanded ? 152f + allowedHeight : 54f;
        }
        contentHeight = Mathf.Max(containerHeight, contentHeight + 6f);

        Rect outerRect = GUILayoutUtility.GetRect(0f, containerHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(outerRect, new Color(0.075f, 0.075f, 0.08f, 1f));
        DrawThinBorder(outerRect, new Color(1f, 1f, 1f, 0.08f));

        string scrollKey = (selectedDefinition != null ? selectedDefinition.GetInstanceID().ToString() : "none") + ":" + materialSlots.propertyPath;
        if (!materialSlotScrolls.ContainsKey(scrollKey))
            materialSlotScrolls[scrollKey] = Vector2.zero;

        Rect viewRect = new Rect(outerRect.x + 6f, outerRect.y + 6f, outerRect.width - 12f, outerRect.height - 12f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 16f), contentHeight);

        Vector2 scroll = materialSlotScrolls[scrollKey];
        scroll = GUI.BeginScrollView(viewRect, scroll, contentRect, false, true);

        float y = 0f;
        for (int i = 0; i < materialSlots.arraySize; i++)
        {
            SerializedProperty slot = materialSlots.GetArrayElementAtIndex(i);
            SerializedProperty slotId = slot.FindPropertyRelative("slotId");
            SerializedProperty displayName = slot.FindPropertyRelative("displayName");
            SerializedProperty rendererPath = slot.FindPropertyRelative("rendererPath");
            SerializedProperty materialIndex = slot.FindPropertyRelative("materialIndex");
            SerializedProperty defaultMaterial = slot.FindPropertyRelative("defaultMaterial");
            SerializedProperty allowedMaterials = slot.FindPropertyRelative("allowedMaterials");

            string title = string.IsNullOrWhiteSpace(displayName.stringValue) ? slotId.stringValue : displayName.stringValue;
            if (string.IsNullOrWhiteSpace(title))
                title = "Material Slot " + i;

            float allowedHeight = allowedMaterials != null && slot.isExpanded
                ? EditorGUI.GetPropertyHeight(allowedMaterials, true)
                : 0f;
            float rowHeight = slot.isExpanded ? 144f + allowedHeight : 48f;
            Rect rowRect = new Rect(0f, y, contentRect.width, rowHeight);

            EditorGUI.DrawRect(rowRect, i % 2 == 0 ? new Color(1f, 1f, 1f, 0.035f) : new Color(1f, 1f, 1f, 0.02f));
            DrawThinBorder(rowRect, new Color(1f, 1f, 1f, 0.045f));

            Rect headerRect = new Rect(rowRect.x + 6f, rowRect.y + 4f, rowRect.width - 12f, 20f);
            slot.isExpanded = EditorGUI.Foldout(new Rect(headerRect.x, headerRect.y, headerRect.width - 82f, headerRect.height), slot.isExpanded, title, true);

            using (new EditorGUI.DisabledScope(i <= 0))
            {
                if (GUI.Button(new Rect(headerRect.xMax - 76f, headerRect.y, 22f, 20f), "↑"))
                {
                    materialSlots.MoveArrayElement(i, i - 1);
                    GUI.EndScrollView();
                    materialSlotScrolls[scrollKey] = scroll;
                    EditorGUILayout.EndVertical();
                    return;
                }
            }
            using (new EditorGUI.DisabledScope(i >= materialSlots.arraySize - 1))
            {
                if (GUI.Button(new Rect(headerRect.xMax - 50f, headerRect.y, 22f, 20f), "↓"))
                {
                    materialSlots.MoveArrayElement(i, i + 1);
                    GUI.EndScrollView();
                    materialSlotScrolls[scrollKey] = scroll;
                    EditorGUILayout.EndVertical();
                    return;
                }
            }
            if (GUI.Button(new Rect(headerRect.xMax - 24f, headerRect.y, 22f, 20f), "-"))
            {
                materialSlots.DeleteArrayElementAtIndex(i);
                GUI.EndScrollView();
                materialSlotScrolls[scrollKey] = scroll;
                EditorGUILayout.EndVertical();
                return;
            }

            Rect summaryRect = new Rect(rowRect.x + 24f, rowRect.y + 26f, rowRect.width - 32f, 18f);
            string summary = $"{rendererPath.stringValue}   /   MAT {materialIndex.intValue}";
            EditorGUI.LabelField(summaryRect, summary, EditorStyles.miniLabel);

            if (slot.isExpanded)
            {
                float x = rowRect.x + 18f;
                float w = rowRect.width - 26f;
                float yy = rowRect.y + 50f;
                float lh = EditorGUIUtility.singleLineHeight;
                float gap = 3f;

                EditorGUI.PropertyField(new Rect(x, yy, w, lh), slotId, new GUIContent("Slot Id")); yy += lh + gap;
                EditorGUI.PropertyField(new Rect(x, yy, w, lh), displayName, new GUIContent("显示名")); yy += lh + gap;
                EditorGUI.PropertyField(new Rect(x, yy, w, lh), rendererPath, new GUIContent("Renderer Path")); yy += lh + gap;
                EditorGUI.PropertyField(new Rect(x, yy, w, lh), materialIndex, new GUIContent("Material Index")); yy += lh + gap;
                EditorGUI.PropertyField(new Rect(x, yy, w, lh), defaultMaterial, new GUIContent("默认 MAT")); yy += lh + gap;

                Rect buttonRow = new Rect(x, yy, w, 20f);
                if (GUI.Button(new Rect(buttonRow.x, buttonRow.y, 150f, buttonRow.height), "默认 MAT 加入允许列表"))
                    AddDefaultMaterialToAllowedList(defaultMaterial, allowedMaterials);
                if (GUI.Button(new Rect(buttonRow.x + 156f, buttonRow.y, 88f, buttonRow.height), "定位默认 MAT"))
                {
                    if (defaultMaterial.objectReferenceValue != null)
                    {
                        Selection.activeObject = defaultMaterial.objectReferenceValue;
                        EditorGUIUtility.PingObject(defaultMaterial.objectReferenceValue);
                    }
                }
                yy += 23f;

                if (allowedMaterials != null)
                {
                    EditorGUI.PropertyField(new Rect(x, yy, w, allowedHeight), allowedMaterials, new GUIContent("允许替换 MAT"), true);
                }
            }

            y += rowHeight + 6f;
        }

        GUI.EndScrollView();
        materialSlotScrolls[scrollKey] = scroll;

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+ 添加 MAT 槽", GUILayout.Width(110f), GUILayout.Height(22f)))
        {
            int index = materialSlots.arraySize;
            materialSlots.InsertArrayElementAtIndex(index);
            SerializedProperty slot = materialSlots.GetArrayElementAtIndex(index);
            slot.FindPropertyRelative("slotId").stringValue = GenerateUniqueMaterialSlotId(materialSlots, "mat_slot");
            slot.FindPropertyRelative("displayName").stringValue = "新材质槽";
            slot.FindPropertyRelative("rendererPath").stringValue = "VisualRoot/Visual_01";
            slot.FindPropertyRelative("materialIndex").intValue = 0;
            slot.FindPropertyRelative("defaultMaterial").objectReferenceValue = null;
            SerializedProperty allowed = slot.FindPropertyRelative("allowedMaterials");
            if (allowed != null)
                allowed.ClearArray();
            slot.isExpanded = true;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void AddDefaultMaterialToAllowedList(SerializedProperty defaultMaterial, SerializedProperty allowedMaterials)
    {
        if (defaultMaterial == null || allowedMaterials == null || defaultMaterial.objectReferenceValue == null)
            return;

        for (int i = 0; i < allowedMaterials.arraySize; i++)
        {
            SerializedProperty item = allowedMaterials.GetArrayElementAtIndex(i);
            if (item.objectReferenceValue == defaultMaterial.objectReferenceValue)
                return;
        }

        int index = allowedMaterials.arraySize;
        allowedMaterials.InsertArrayElementAtIndex(index);
        allowedMaterials.GetArrayElementAtIndex(index).objectReferenceValue = defaultMaterial.objectReferenceValue;
    }

    private string GenerateUniqueMaterialSlotId(SerializedProperty materialSlots, string baseId)
    {
        HashSet<string> used = new HashSet<string>();
        if (materialSlots != null)
        {
            for (int i = 0; i < materialSlots.arraySize; i++)
            {
                SerializedProperty item = materialSlots.GetArrayElementAtIndex(i);
                SerializedProperty idProp = item.FindPropertyRelative("slotId");
                if (idProp != null && !string.IsNullOrWhiteSpace(idProp.stringValue))
                    used.Add(idProp.stringValue);
            }
        }

        if (!used.Contains(baseId))
            return baseId;

        int index = 1;
        while (used.Contains(baseId + "_" + index))
            index++;
        return baseId + "_" + index;
    }

    private void DrawVariantPreviewBox(Rect rect, GameObject prefab, Sprite previewIcon)
    {
        EditorGUI.DrawRect(rect, new Color(0.10f, 0.10f, 0.105f, 1f));
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.16f));

        Texture texture = null;
        if (previewIcon != null)
            texture = previewIcon.texture;

        if (texture == null && prefab != null)
        {
            texture = AssetPreview.GetAssetPreview(prefab);
            if (texture == null)
                texture = AssetPreview.GetMiniThumbnail(prefab);
        }

        if (texture == null)
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultIconPath);

        Rect imageRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 24f);
        if (texture != null)
            GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleToFit, true);

        Rect labelRect = new Rect(rect.x + 4f, rect.yMax - 18f, rect.width - 8f, 16f);
        GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.78f, 0.78f, 0.80f, 1f) }
        };
        GUI.Label(labelRect, prefab != null ? prefab.name : "未绑定 PF", style);
    }


    private void ScanAllVariantMaterialSlots(bool clearExisting)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再批量扫描所有 PF 的材质槽。请在单个变体中显式维护材质槽。");
    }

    private void ScanVariantMaterialSlots(int variantIndex, bool clearExisting)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再自动扫描 PF 材质槽。请显式维护当前变体材质槽。");
    }

    private bool ScanVariantMaterialSlotsDirect(TerrainDecorationDefinition def, int variantIndex, bool clearExisting, bool showDialog)
    {
        if (def == null || def.variants == null || variantIndex < 0 || variantIndex >= def.variants.Count)
            return false;

        TerrainDecorationVariant variant = def.variants[variantIndex];
        if (variant == null || variant.prefab == null)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("没有 PF", "这个变体还没有绑定 PF，无法扫描 MAT 槽。", "知道了");
            return false;
        }

        List<TerrainDecorationMaterialSlot> scanned = BuildMaterialSlotsFromPrefab(variant.prefab);
        if (scanned.Count == 0)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("没有找到 MAT", "当前 PF 中没有找到 Renderer / Material。", "知道了");
            return false;
        }

        Undo.RecordObject(def, "Scan terrain decoration material slots");

        if (variant.materialSlots == null || clearExisting)
            variant.materialSlots = new List<TerrainDecorationMaterialSlot>();

        if (clearExisting || variant.materialSlots.Count == 0)
        {
            variant.materialSlots = scanned;
            return true;
        }

        bool changed = false;
        for (int i = 0; i < scanned.Count; i++)
        {
            TerrainDecorationMaterialSlot newSlot = scanned[i];
            TerrainDecorationMaterialSlot existing = variant.materialSlots.FirstOrDefault(x =>
                x != null && x.rendererPath == newSlot.rendererPath && x.materialIndex == newSlot.materialIndex);

            if (existing == null)
            {
                variant.materialSlots.Add(newSlot);
                changed = true;
            }
            else if (existing.defaultMaterial == null && newSlot.defaultMaterial != null)
            {
                existing.defaultMaterial = newSlot.defaultMaterial;
                AddAllowedMaterial(existing, newSlot.defaultMaterial);
                changed = true;
            }
        }

        return changed;
    }

    private List<TerrainDecorationMaterialSlot> BuildMaterialSlotsFromPrefab(GameObject prefab)
    {
        List<TerrainDecorationMaterialSlot> result = new List<TerrainDecorationMaterialSlot>();
        if (prefab == null)
            return result;

        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null)
                continue;

            string rawPath = GetTransformPath(prefab.transform, renderer.transform);
            if (ShouldIgnoreRendererForMaterialSlot(rawPath, renderer.transform))
                continue;

            bool usePrefabRootSlot = IsPrefabRootMaterialRenderer(prefab, renderer, rawPath);
            string path = usePrefabRootSlot ? prefab.name : rawPath;
            string displayRendererName = usePrefabRootSlot ? prefab.name : renderer.name;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                continue;

            for (int m = 0; m < materials.Length; m++)
            {
                Material mat = materials[m];
                TerrainDecorationMaterialSlot slot = new TerrainDecorationMaterialSlot
                {
                    slotId = MakeSafeId(path + "_mat_" + m),
                    displayName = displayRendererName + " / MAT " + m,
                    rendererPath = path,
                    materialIndex = m,
                    defaultMaterial = mat,
                    allowedMaterials = new List<Material>()
                };

                if (mat != null)
                    slot.allowedMaterials.Add(mat);

                result.Add(slot);
            }
        }

        return result;
    }

    private bool IsPrefabRootMaterialRenderer(GameObject prefab, Renderer renderer, string rawPath)
    {
        if (prefab == null || renderer == null)
            return false;

        // 正常情况：PF 根节点自己挂 MeshRenderer / SkinnedMeshRenderer。
        // MAT 槽里直接显示 PF 名称，而不是空路径。
        if (renderer.transform == prefab.transform || string.IsNullOrWhiteSpace(rawPath))
            return true;

        string p = rawPath.Replace('\\', '/');

        // 兼容旧版自动矫正造成的结构：VisualRoot/Visual_01 是从 PF 根节点迁移出来的唯一主视觉。
        // 对用户和材质变体来说，它仍然应被视为“PF 自身的 MAT 槽”。
        if (p == "VisualRoot/Visual_01")
        {
            Renderer rootRenderer = prefab.GetComponent<Renderer>();
            if (rootRenderer != null)
                return false;

            Transform visualRoot = prefab.transform.Find("VisualRoot");
            if (visualRoot == null || visualRoot.childCount != 1)
                return false;

            Renderer[] allRenderers = prefab.GetComponentsInChildren<Renderer>(true);
            int realVisualCount = 0;
            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer r = allRenderers[i];
                if (r == null)
                    continue;

                string rp = GetTransformPath(prefab.transform, r.transform);
                if (!ShouldIgnoreRendererForMaterialSlot(rp, r.transform))
                    realVisualCount++;
            }

            return realVisualCount == 1;
        }

        return false;
    }

    private bool ShouldIgnoreRendererForMaterialSlot(string path, Transform rendererTransform)
    {
        // MAT 槽只允许记录真正的视觉模型。
        // RuleRoot / ShadowCasterRoot / StencilWriterRoot / OutlineMaskProxyRoot 等自动生成节点
        // 只是 2.5D 遮挡、投影或编辑器辅助结构，不能参与变体材质替换。
        // 空路径代表 Prefab 根节点 Renderer。它是合法主视觉，不应被过滤。
        string p = string.IsNullOrWhiteSpace(path) ? "" : path.Replace('\\', '/');

        if (p.StartsWith("RuleRoot/") || p == "RuleRoot") return true;
        if (p.StartsWith("ShadowCasterRoot/") || p == "ShadowCasterRoot") return true;
        if (p.StartsWith("StencilWriterRoot/") || p == "StencilWriterRoot") return true;
        if (p.StartsWith("OutlineMaskProxyRoot/") || p == "OutlineMaskProxyRoot") return true;
        if (p.StartsWith("FrontOccluderRoot/") || p == "FrontOccluderRoot") return true;
        if (p.StartsWith("CollisionRoot/") || p == "CollisionRoot") return true;
        if (p.StartsWith("VisionBlockerRoot/") || p == "VisionBlockerRoot") return true;
        if (p.StartsWith("EditorGizmoRoot/") || p == "EditorGizmoRoot") return true;

        if (p.Contains("__AutoStencilClone") ||
            p.Contains("__AutoOutlineMaskShapeClone") ||
            p.Contains("_Stencil") ||
            p.Contains("_OutlineMask"))
            return true;

        if (rendererTransform != null)
        {
            Transform t = rendererTransform;
            while (t != null)
            {
                string n = t.name;
                if (n == "RuleRoot" ||
                    n == "ShadowCasterRoot" ||
                    n == "StencilWriterRoot" ||
                    n == "OutlineMaskProxyRoot" ||
                    n == "FrontOccluderRoot" ||
                    n == "CollisionRoot" ||
                    n == "VisionBlockerRoot" ||
                    n == "EditorGizmoRoot" ||
                    n == "__AutoStencilClone" ||
                    n == "__AutoOutlineMaskShapeClone")
                    return true;

                t = t.parent;
            }
        }

        return false;
    }

    private void AddAllowedMaterial(TerrainDecorationMaterialSlot slot, Material material)
    {
        if (slot == null || material == null)
            return;
        if (slot.allowedMaterials == null)
            slot.allowedMaterials = new List<Material>();
        if (!slot.allowedMaterials.Contains(material))
            slot.allowedMaterials.Add(material);
    }

    private string GetTransformPath(Transform root, Transform target)
    {
        if (root == null || target == null)
            return "";
        if (root == target)
            return "";

        List<string> parts = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            parts.Add(current.name);
            current = current.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private string MakeSafeId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "slot";

        string lower = raw.ToLowerInvariant();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(lower.Length);
        bool lastUnderscore = false;
        for (int i = 0; i < lower.Length; i++)
        {
            char c = lower[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (ok)
            {
                builder.Append(c);
                lastUnderscore = false;
            }
            else if (!lastUnderscore)
            {
                builder.Append('_');
                lastUnderscore = true;
            }
        }

        string result = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "slot" : result;
    }

    private string GenerateUniqueVariantId(SerializedProperty variants, string baseId)
    {
        HashSet<string> used = new HashSet<string>();
        if (variants != null)
        {
            for (int i = 0; i < variants.arraySize; i++)
            {
                SerializedProperty item = variants.GetArrayElementAtIndex(i);
                SerializedProperty idProp = item.FindPropertyRelative("variantId");
                if (idProp != null && !string.IsNullOrWhiteSpace(idProp.stringValue))
                    used.Add(idProp.stringValue);
            }
        }

        if (!used.Contains(baseId))
            return baseId;

        int index = 1;
        while (used.Contains(baseId + "_" + index))
            index++;
        return baseId + "_" + index;
    }

    private void DrawStructure()
    {
        EditorGUILayout.HelpBox(
            "容器结构不再由定义页矫正或自动补齐。\n" +
            "新放置实例会由 SkyPrisonTerrainDecorationInstanceBuilder 按碰撞、遮挡、物理定义生成标准结构。\n" +
            "旧字段 structureTemplate / autoEnsureStandardStructure / repairMissingNodesOnly 仅作为历史数据保留，不再作为日常编辑入口。",
            MessageType.Info);

        SerializedProperty structureTemplate = selectedSO.FindProperty("structureTemplate");
        SerializedProperty autoEnsure = selectedSO.FindProperty("autoEnsureStandardStructure");
        SerializedProperty repairMissing = selectedSO.FindProperty("repairMissingNodesOnly");

        using (new EditorGUI.DisabledScope(true))
        {
            if (structureTemplate != null)
                EditorGUILayout.PropertyField(structureTemplate, new GUIContent("历史结构模板"), true);
            if (autoEnsure != null)
                EditorGUILayout.PropertyField(autoEnsure, new GUIContent("历史：自动补齐标准结构"), true);
            if (repairMissing != null)
                EditorGUILayout.PropertyField(repairMissing, new GUIContent("历史：只补缺失节点"), true);
        }
    }

    private void DrawPlacement()
    {
        PropertyField("允许移动", "allowMove");
        PropertyField("允许旋转", "allowRotate");
        PropertyField("允许缩放", "allowScale");
        PropertyField("吸附网格", "snapToGrid");
        DrawPlacementCollisionModePopup();
        PropertyField("允许视觉穿插", "allowVisualOverlap");
        PropertyField("允许碰撞重叠", "allowCollisionOverlap");
        PropertyField("默认放置旋转", "defaultPlacementRotation");
        PropertyField("默认缩放", "defaultScale");
        PropertyField("占地尺寸", "footprintSize");
    }

    private void DrawRandomization()
    {
        PropertyField("启用随机缩放", "enableRandomScale");
        bool randomScale = selectedDefinition != null && selectedDefinition.enableRandomScale;
        using (new EditorGUI.DisabledScope(!randomScale))
        {
            PropertyField("等比随机缩放", "uniformRandomScale");
            PropertyField("随机缩放 Min", "randomScaleMin");
            PropertyField("随机缩放 Max", "randomScaleMax");
        }
        GUILayout.Space(4f);
        PropertyField("启用视觉随机旋转", "enableVisualRandomRotation");
        bool randomRot = selectedDefinition != null && selectedDefinition.enableVisualRandomRotation;
        using (new EditorGUI.DisabledScope(!randomRot))
        {
            PropertyField("视觉随机角度 Min", "visualRandomRotationMin");
            PropertyField("视觉随机角度 Max", "visualRandomRotationMax");
            PropertyField("视觉随机影响规则", "visualRandomRotationAffectsRules");
        }
    }

    private void DrawCollision()
    {
        DrawCollisionModePopup();
        TerrainDecorationCollisionMode collisionMode = GetEnumValue("collisionMode", TerrainDecorationCollisionMode.None);
        bool hasCollision = collisionMode != TerrainDecorationCollisionMode.None;
        bool blockVision = selectedDefinition != null && selectedDefinition.blockVision;
        using (new EditorGUI.DisabledScope(!hasCollision && !blockVision))
        {
            if (!hasCollision && !blockVision)
                EditorGUILayout.HelpBox("碰撞模式为“无”且未阻挡视线时，不需要修改碰撞体尺寸。", MessageType.Info);
            PropertyField("碰撞 Size", "collisionSize");
            PropertyField("碰撞 Offset", "collisionOffset");
        }
        using (new EditorGUI.DisabledScope(!hasCollision))
        {
            PropertyField("阻挡玩家", "blockPlayer");
            PropertyField("阻挡敌人", "blockEnemy");
            PropertyField("阻挡子弹", "blockProjectile");
        }
        PropertyField("阻挡视线", "blockVision");
    }


    private void DrawPhysicsStructure()
    {
        if (selectedDefinition == null)
            return;

        SkyPrisonTerrainDecorationPhysicsSettings settings = GetOrCreatePhysicsSettings(selectedDefinition, false);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("物理结构设置", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "这里只编辑物理结构定义。\n" +
            "是否生成 Rigidbody / PushableRuntime / MeshCollider 代理，由放置 Builder 在摆放瞬间按定义执行。\n" +
            "定义页不再直接修模板 Prefab 或 Scene 已摆放实例。",
            MessageType.Info);

        if (settings == null)
        {
            EditorGUILayout.HelpBox("当前装饰物还没有物理设置资产。未创建时视为：不启用物理结构。", MessageType.None);
            if (GUILayout.Button("创建物理设置资产", GUILayout.Height(24f)))
            {
                settings = GetOrCreatePhysicsSettings(selectedDefinition, true);
                if (settings != null)
                {
                    Selection.activeObject = settings;
                    EditorGUIUtility.PingObject(settings);
                }
            }
            EditorGUILayout.EndVertical();
            return;
        }

        SerializedObject physicsSO = new SerializedObject(settings);
        physicsSO.Update();

        PhysicsPropertyField(physicsSO, "enablePhysicsStructure", "启用物理结构");
        bool enablePhysics = GetPhysicsBool(physicsSO, "enablePhysicsStructure", false);

        using (new EditorGUI.DisabledScope(!enablePhysics))
        {
            GUILayout.Space(4f);
            EditorGUILayout.LabelField("碰撞响应", EditorStyles.miniBoldLabel);
            PhysicsPropertyField(physicsSO, "receiveVolumeCollision", "受到体积碰撞");
            PhysicsPropertyField(physicsSO, "receiveAttackImpulse", "受到攻击冲量");
            PhysicsPropertyField(physicsSO, "receiveExplosionImpulse", "受到爆炸冲量");
            PhysicsPropertyField(physicsSO, "receiveScriptedImpulse", "受到脚本冲量");

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("碰撞体生成", EditorStyles.miniBoldLabel);
            PhysicsPropertyField(physicsSO, "customPhysicsMesh", "自定义物理 Mesh");
            PhysicsPropertyField(physicsSO, "autoPickLargestVisibleMesh", "自动选择最大可见 Mesh");
            PhysicsPropertyField(physicsSO, "forceConvexMeshCollider", "MeshCollider Convex");
            PhysicsPropertyField(physicsSO, "pushableLayerName", "物理 Layer");

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Rigidbody 默认值", EditorStyles.miniBoldLabel);
            PhysicsPropertyField(physicsSO, "mass", "质量");
            PhysicsPropertyField(physicsSO, "linearDamping", "线性阻尼");
            PhysicsPropertyField(physicsSO, "angularDamping", "角阻尼");
            PhysicsPropertyField(physicsSO, "maxPlanarSpeed", "最大平面速度");

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Pushable 默认值", EditorStyles.miniBoldLabel);
            PhysicsPropertyField(physicsSO, "externalPushMultiplier", "外部推力倍率");
            PhysicsPropertyField(physicsSO, "applyForceAtTop", "高点受力");
            PhysicsPropertyField(physicsSO, "topForceHeight", "高点受力高度");
            PhysicsPropertyField(physicsSO, "topForceMultiplier", "高点受力倍率");

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("倒地 / 地面保护", EditorStyles.miniBoldLabel);
            PhysicsPropertyField(physicsSO, "enableKnockdown", "允许推倒");
            PhysicsPropertyField(physicsSO, "protectAfterPivotRelease", "释放后防穿地");
            PhysicsPropertyField(physicsSO, "useLastKnownGroundWhenRayMisses", "射线失败使用上次地面");
            PhysicsPropertyField(physicsSO, "useFallbackGroundPlaneWhenRayMisses", "射线失败使用备用地面");
            PhysicsPropertyField(physicsSO, "fallbackGroundY", "备用地面 Y");
        }

        physicsSO.ApplyModifiedProperties();

        GUILayout.Space(6f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("定位物理设置资产", GUILayout.Width(130f), GUILayout.Height(24f)))
        {
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "物理设置只作为定义数据保存。新放置实例会由 SkyPrisonTerrainDecorationInstanceBuilder 按这些设置生成。\n" +
            "旧实例迁移请走 Migration 工具，不再从定义页执行矫正。",
            MessageType.None);

        EditorGUILayout.EndVertical();
    }

    private void PhysicsPropertyField(SerializedObject so, string propertyName, string label)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null)
        {
            EditorGUILayout.LabelField(label, "字段不存在");
            return;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(150f));
        EditorGUILayout.PropertyField(prop, GUIContent.none, true);
        EditorGUILayout.EndHorizontal();
    }

    private bool GetPhysicsBool(SerializedObject so, string propertyName, bool fallback)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        return prop != null && prop.propertyType == SerializedPropertyType.Boolean ? prop.boolValue : fallback;
    }

    private void DrawRuleSpace()
    {
        PropertyField("规则空间锁定视觉随机", "lockRuleSpaceFromVisualRandomRotation");
        DrawFrontBackPlaneModePopup();
        PropertyField("规则 Forward", "ruleForwardLocal");
        PropertyField("手动平面 Origin", "rulePlaneOriginLocal");
        PropertyField("平面外推距离", "planePushOutDistance");
    }

    private void DrawOcclusionShadowFog()
    {
        DrawOcclusionModePopup();

        TerrainDecorationOcclusionMode occlusionMode = GetEnumValue("occlusionMode", TerrainDecorationOcclusionMode.None);
        bool hasOcclusion = occlusionMode != TerrainDecorationOcclusionMode.None;
        bool hasFade = occlusionMode == TerrainDecorationOcclusionMode.FadeWhenBlockingPlayer ||
                       occlusionMode == TerrainDecorationOcclusionMode.FrontBackAndFade;

        using (new EditorGUI.DisabledScope(!hasOcclusion))
        {
            if (!hasOcclusion)
                EditorGUILayout.HelpBox("遮挡模式为“无”时，不需要修改半透明或前后遮挡参数。", MessageType.Info);

            using (new EditorGUI.DisabledScope(!hasFade))
            {
                PropertyField("挡住玩家时半透明", "fadeWhenBlockingPlayer");
                PropertyField("半透明 Alpha", "fadeAlpha");
                PropertyField("过渡时间", "fadeDuration");
            }
        }

        GUILayout.Space(4f);
        DrawShadowModePopup();
        TerrainDecorationShadowMode shadowMode = GetEnumValue("shadowMode", TerrainDecorationShadowMode.None);
        using (new EditorGUI.DisabledScope(shadowMode == TerrainDecorationShadowMode.None))
        {
            if (shadowMode == TerrainDecorationShadowMode.None)
                EditorGUILayout.HelpBox("阴影模式为“无”时，不需要修改投影参数。", MessageType.Info);

            PropertyField("投射阴影", "castShadow");
            PropertyField("接收阴影", "receiveShadow");

            using (new EditorGUI.DisabledScope(shadowMode == TerrainDecorationShadowMode.MeshRenderer))
            {
                PropertyField("投影代理 Prefab", "shadowCasterPrefab");
                PropertyField("投影代理材质", "shadowCasterMaterial");
            }
        }

        GUILayout.Space(4f);
        DrawFogModePopup();
    }


    private void DrawHeightFade()
    {
        PropertyField("勾选＝高层建筑物", "enableHeightFade");

        bool enableHeightFade = selectedSO.FindProperty("enableHeightFade")?.boolValue ?? false;

        using (new EditorGUI.DisabledScope(!enableHeightFade))
        {
            if (!enableHeightFade)
                EditorGUILayout.HelpBox("不勾选时，放置这个装饰物不会自动挂高度淡出组件。", MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "放置时会自动挂 SkyPrisonHeightFadeController，超过下面这个高度的部分会\n" +
                    "逐渐淡出到透明。但只挂组件还不够——物体材质的Shader得支持高度淡出\n" +
                    "（SkyPrison/Lit With Height Fade）且 Surface Type 是 Transparent，这两步\n" +
                    "需要美术/关卡单独处理。", MessageType.Info);

            PropertyField("可显示高度（米，从建筑自己底部往上算）", "heightFadeThreshold");
            PropertyField("淡出距离（米）", "heightFadeDistance");
        }
    }


    private void DrawFrontBackOcclusionProjectionSettings()
    {
        EditorGUILayout.HelpBox(
            "45° 镜头投影自动计算出的判定盒作为 1.0 基准。\n" +
            "这里保存的是生成规则：最终 BackTrigger / FrontTrigger / FrontOccluderProxy 会在摆放确认、物体最终旋转确定后由 Builder 生成。",
            MessageType.Info);

        EditorGUILayout.LabelField("投影基准倍率", EditorStyles.boldLabel);
        PropertyField("总宽度倍率", "frontBackOcclusionWidthMultiplier");
        PropertyField("总高度倍率", "frontBackOcclusionHeightMultiplier");
        PropertyField("总深度倍率", "frontBackOcclusionDepthMultiplier");

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("前后比例", EditorStyles.boldLabel);
        PropertyField("前方比例", "frontOcclusionDepthRatio");
        PropertyField("后方比例", "backOcclusionDepthRatio");
        PropertyField("前后中心偏移", "frontBackOcclusionCenterOffset");

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("基准盒偏移", EditorStyles.boldLabel);
        PropertyField("画面横向偏移", "frontBackOcclusionHorizontalOffset");
        PropertyField("高度偏移", "frontBackOcclusionHeightOffset");
        PropertyField("前后深度偏移", "frontBackOcclusionDepthOffset");

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("FrontOccluder 代理模式", EditorStyles.boldLabel);
        DrawFrontOccluderProxyModePopup();
        PropertyField("代理体材质", "frontOccluderProxyMaterial");
        PropertyField("Alpha Cutoff", "frontOccluderAlphaCutoff");
        PropertyField("手动代理 Prefab", "manualFrontOccluderProxyPrefab");

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("FrontOccluder 盒体代理倍率", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("参考模型代理会优先复制 VisualRoot 下的 Mesh / UV / 形状，并替换成代理体材质；盒体代理才使用下面的盒体倍率。", MessageType.Info);
        PropertyField("代理宽度倍率", "frontOccluderProxyWidthMultiplier");
        PropertyField("代理高度倍率", "frontOccluderProxyHeightMultiplier");
        PropertyField("代理深度倍率", "frontOccluderProxyDepthMultiplier");
        PropertyField("代理 Offset", "frontOccluderProxyOffset");
    }

    private void DrawFrontOccluderProxyModePopup()
    {
        SerializedProperty prop = selectedSO.FindProperty("frontOccluderProxyMode");
        if (prop == null)
        {
            EditorGUILayout.LabelField("代理模式", "字段不存在");
            return;
        }

        string[] labels =
        {
            "无",
            "盒体代理",
            "参考模型代理",
            "手动代理 Prefab"
        };

        DrawEnumPopupByIndex("代理模式", prop, labels);
    }

    private void DrawOcclusionModePopup()
    {
        SerializedProperty prop = selectedSO.FindProperty("occlusionMode");
        if (prop == null)
        {
            EditorGUILayout.LabelField("遮挡模式", "字段不存在");
            return;
        }

        string[] labels =
        {
            "无",
            "前后遮挡",
            "挡住玩家时半透明",
            "前后遮挡 + 半透明"
        };

        DrawEnumPopupByIndex("遮挡模式", prop, labels);
    }

    private void DrawShadowModePopup()
    {
        SerializedProperty prop = selectedSO.FindProperty("shadowMode");
        if (prop == null)
        {
            EditorGUILayout.LabelField("阴影模式", "字段不存在");
            return;
        }

        string[] labels =
        {
            "无",
            "使用模型 Renderer",
            "使用投影代理",
            "仅投影代理"
        };

        DrawEnumPopupByIndex("阴影模式", prop, labels);
    }

    private void DrawFogModePopup()
    {
        SerializedProperty prop = selectedSO.FindProperty("fogMode");
        if (prop == null)
        {
            EditorGUILayout.LabelField("战争迷雾模式", "字段不存在");
            return;
        }

        string[] labels =
        {
            "始终可见",
            "迷雾中暗化",
            "迷雾中隐藏",
            "看见后才显示"
        };

        DrawEnumPopupByIndex("战争迷雾模式", prop, labels);
    }

    private void DrawStructureTemplatePopup()
    {
        SerializedProperty prop = selectedSO.FindProperty("structureTemplate");
        if (prop == null)
        {
            EditorGUILayout.LabelField("结构模板", "字段不存在");
            return;
        }

        string[] labels = { "标准容器", "仅视觉", "盒体遮挡", "墙体遮挡", "苔藓挂件", "自定义" };
        DrawEnumPopupByIndex("结构模板", prop, labels);
    }

    private void DrawPlacementCollisionModePopup()
    {
        SerializedProperty prop = selectedSO.FindProperty("placementCollisionMode");
        if (prop == null)
        {
            EditorGUILayout.LabelField("放置碰撞模式", "字段不存在");
            return;
        }

        string[] labels = { "无", "仅视觉", "阻挡放置", "阻挡单位", "阻挡全部" };
        DrawEnumPopupByIndex("放置碰撞模式", prop, labels);
    }

    private void DrawCollisionModePopup()
    {
        SerializedProperty prop = selectedSO.FindProperty("collisionMode");
        if (prop == null)
        {
            EditorGUILayout.LabelField("碰撞模式", "字段不存在");
            return;
        }

        string[] labels = { "无", "盒体碰撞", "网格碰撞", "自定义碰撞根节点" };
        DrawEnumPopupByIndex("碰撞模式", prop, labels);
    }

    private void DrawFrontBackPlaneModePopup()
    {
        SerializedProperty prop = selectedSO.FindProperty("frontBackPlaneMode");
        if (prop == null)
        {
            EditorGUILayout.LabelField("正背面平面模式", "字段不存在");
            return;
        }

        string[] labels = { "手动锚点", "按碰撞盒自动", "按容器包围盒自动" };
        DrawEnumPopupByIndex("正背面平面模式", prop, labels);
    }


    private void DrawEnumPopupByIndex(string label, SerializedProperty prop, string[] labels)
    {
        int currentIndex = Mathf.Clamp(prop.enumValueIndex, 0, labels.Length - 1);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(150f));
        int newIndex = EditorGUILayout.Popup(currentIndex, labels);
        prop.enumValueIndex = newIndex;
        EditorGUILayout.EndHorizontal();
    }

    private TerrainDecorationOcclusionMode GetEnumValue(string propertyName, TerrainDecorationOcclusionMode fallback)
    {
        SerializedProperty prop = selectedSO.FindProperty(propertyName);
        if (prop == null)
            return fallback;

        TerrainDecorationOcclusionMode[] values = (TerrainDecorationOcclusionMode[])System.Enum.GetValues(typeof(TerrainDecorationOcclusionMode));
        int index = prop.enumValueIndex;
        if (index < 0 || index >= values.Length)
            return fallback;

        return values[index];
    }

    private TerrainDecorationShadowMode GetEnumValue(string propertyName, TerrainDecorationShadowMode fallback)
    {
        SerializedProperty prop = selectedSO.FindProperty(propertyName);
        if (prop == null)
            return fallback;

        TerrainDecorationShadowMode[] values = (TerrainDecorationShadowMode[])System.Enum.GetValues(typeof(TerrainDecorationShadowMode));
        int index = prop.enumValueIndex;
        if (index < 0 || index >= values.Length)
            return fallback;

        return values[index];
    }


    private TerrainDecorationCollisionMode GetEnumValue(string propertyName, TerrainDecorationCollisionMode fallback)
    {
        SerializedProperty prop = selectedSO.FindProperty(propertyName);
        if (prop == null)
            return fallback;

        TerrainDecorationCollisionMode[] values = (TerrainDecorationCollisionMode[])System.Enum.GetValues(typeof(TerrainDecorationCollisionMode));
        int index = prop.enumValueIndex;
        if (index < 0 || index >= values.Length)
            return fallback;

        return values[index];
    }


    private void DrawEnvironmentAudio()
    {
        SerializedProperty enableProp = selectedSO.FindProperty("enableEnvironmentAudio");
        SerializedProperty packageProp = selectedSO.FindProperty("environmentAudioPackage");
        SerializedProperty minProp = selectedSO.FindProperty("environmentAudioMinDistance");
        SerializedProperty maxProp = selectedSO.FindProperty("environmentAudioMaxDistance");
        SerializedProperty volumeProp = selectedSO.FindProperty("environmentAudioVolume");
        SerializedProperty loopProp = selectedSO.FindProperty("environmentAudioLoop");

        if (enableProp == null)
        {
            EditorGUILayout.HelpBox("当前 TerrainDecorationDefinition.cs 还没有环境音字段。请先替换最新定义文件。", MessageType.Warning);
            return;
        }

        EditorGUILayout.PropertyField(enableProp, new GUIContent("启用环境音"));

        using (new EditorGUI.DisabledScope(!enableProp.boolValue))
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(packageProp, new GUIContent("环境音包"));
            if (EditorGUI.EndChangeCheck())
            {
                SkyPrisonAudioPackage pkg = packageProp.objectReferenceValue as SkyPrisonAudioPackage;
                if (pkg != null && pkg.packageType != SkyPrisonAudioPackageType.Ambience)
                {
                    EditorUtility.DisplayDialog("音声包类型不匹配", "地形装饰物的环境音槽只允许绑定包标签为“环境音”的音声包。", "知道了");
                    packageProp.objectReferenceValue = null;
                }
            }

            SkyPrisonAudioPackage current = packageProp.objectReferenceValue as SkyPrisonAudioPackage;
            if (current != null && current.packageType != SkyPrisonAudioPackageType.Ambience)
                EditorGUILayout.HelpBox("当前绑定的音声包不是“环境音”类型，请到音声合成器里把包标签改为环境音。", MessageType.Error);
            else if (current == null)
                EditorGUILayout.HelpBox("不是每个地形装饰物都需要环境音。需要时绑定 SkyPrisonAudioPackage，且包标签必须为“环境音”。", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(150f);
            if (GUILayout.Button("定位音声包目录", GUILayout.Height(22f)))
            {
                Object folder = AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Audio/Packages");
                if (folder != null)
                {
                    Selection.activeObject = folder;
                    EditorGUIUtility.PingObject(folder);
                }
            }
            using (new EditorGUI.DisabledScope(current == null))
            {
                if (GUILayout.Button("在音声合成器中打开", GUILayout.Height(22f)))
                    SkyPrisonEditorWindow.OpenWindowWithTab("音声合成", current);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(minProp, new GUIContent("最小距离"));
            EditorGUILayout.PropertyField(maxProp, new GUIContent("最大距离"));
            if (maxProp.floatValue < minProp.floatValue + 0.01f)
                maxProp.floatValue = minProp.floatValue + 0.01f;

            EditorGUILayout.PropertyField(volumeProp, new GUIContent("音量倍率"));
            EditorGUILayout.PropertyField(loopProp, new GUIContent("循环播放"));
        }
    }

    private void DrawEditorDisplay()
    {
        PropertyField("显示容器 Gizmo", "showBoundsGizmo");
        PropertyField("显示碰撞 Gizmo", "showCollisionGizmo");
        PropertyField("显示正背面 Gizmo", "showFrontBackPlaneGizmo");
        PropertyField("Gizmo 颜色", "gizmoColor");
    }

    private void DrawSection(string label, System.Action drawer)
    {
        if (!sectionFoldouts.ContainsKey(label))
            sectionFoldouts[label] = true;
        sectionFoldouts[label] = EditorGUILayout.Foldout(sectionFoldouts[label], label, true);
        if (!sectionFoldouts[label])
            return;

        EditorGUILayout.BeginVertical("box");
        drawer?.Invoke();
        EditorGUILayout.EndVertical();
        GUILayout.Space(4f);
    }

    private void PropertyField(string label, string propertyName, bool multiline = false)
    {
        SerializedProperty prop = selectedSO.FindProperty(propertyName);
        if (prop == null)
        {
            EditorGUILayout.LabelField(label, "字段不存在");
            return;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(150f));
        if (multiline && prop.propertyType == SerializedPropertyType.String)
            prop.stringValue = EditorGUILayout.TextArea(prop.stringValue, GUILayout.MinHeight(44f));
        else
            EditorGUILayout.PropertyField(prop, GUIContent.none, true);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawReadonlyText(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(90f));
        EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(value) ? "-" : value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawDefinitionRow(Rect rect, TerrainDecorationDefinition def)
    {
        bool selected = selectedDefinition == def;
        bool hover = rect.Contains(Event.current.mousePosition);
        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.30f, 0.13f, 0.08f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.05f));
        }

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            SelectDefinition(def);

        Rect iconRect = new Rect(rect.x + 8f, rect.y + 5f, 34f, 34f);
        DrawPreviewIcon(iconRect, def);

        GUIStyle titleStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            normal = { textColor = selected ? Color.white : new Color(0.92f, 0.92f, 0.94f, 1f) }
        };

        GUIStyle subStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = selected ? new Color(1f, 0.82f, 0.76f, 1f) : new Color(0.66f, 0.66f, 0.68f, 1f) }
        };

        Rect titleRect = new Rect(rect.x + 48f, rect.y + 4f, rect.width - 52f, 18f);
        Rect subRect = new Rect(rect.x + 48f, rect.y + 23f, rect.width - 52f, 16f);
        GUI.Label(titleRect, GetDisplayName(def), titleStyle);
        GUI.Label(subRect, GetPrimaryPrefabName(def), subStyle);
    }

    private void DrawPreviewIcon(Rect rect, TerrainDecorationDefinition def)
    {
        Texture texture = null;
        if (def != null && def.icon != null)
            texture = def.icon.texture;

        TerrainDecorationVariant variant = def != null ? def.GetFirstVariant() : null;
        if (texture == null && variant != null && variant.previewIcon != null)
            texture = variant.previewIcon.texture;

        GameObject prefab = GetPrimaryPrefab(def);
        if (texture == null && prefab != null)
        {
            texture = AssetPreview.GetAssetPreview(prefab);
            if (texture == null)
                texture = AssetPreview.GetMiniThumbnail(prefab);
        }

        if (texture == null)
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultIconPath);

        EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.06f));
        if (texture != null)
            GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), texture, ScaleMode.ScaleToFit, true);
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.12f));
    }

    private string GetPrimaryPrefabName(TerrainDecorationDefinition def)
    {
        GameObject prefab = GetPrimaryPrefab(def);
        if (prefab == null)
            return "未绑定 PF";
        return prefab.name;
    }

    private List<TerrainDecorationDefinition> GetFilteredDefinitions()
    {
        if (string.IsNullOrWhiteSpace(search))
            return definitions;

        string s = search.ToLowerInvariant();
        return definitions.Where(d =>
            d != null &&
            ((d.decorationId != null && d.decorationId.ToLowerInvariant().Contains(s)) ||
             (d.displayName != null && d.displayName.ToLowerInvariant().Contains(s)) ||
             (d.subCategory != null && d.subCategory.ToLowerInvariant().Contains(s)) ||
             GetCategoryLabel(d).ToLowerInvariant().Contains(s))).ToList();
    }

    private void SelectDefinition(TerrainDecorationDefinition def)
    {
        if (selectedDefinition == def && selectedSO != null && selectedSO.targetObject == def)
            return;

        // 左侧列表切换时，必须先结束右侧正在编辑的 TextField/TextArea。
        // Unity IMGUI 会缓存当前聚焦文本框的编辑字符串；如果不主动清焦点，
        // 新 SerializedObject 已经切换了，但右侧文本框仍可能继续显示上一个对象的输入缓存。
        if (selectedSO != null)
            selectedSO.ApplyModifiedProperties();

        GUI.FocusControl(null);
        GUIUtility.keyboardControl = 0;
        GUIUtility.hotControl = 0;
        EditorGUIUtility.editingTextField = false;

        selectedDefinition = def;
        selectedSO = def != null ? new SerializedObject(def) : null;
        selectedSO?.Update();

        GUI.changed = true;
    }

    private void CreateDefinition(bool createPrefab)
    {
        EnsureFolderExists(CustomFolder);
        TerrainDecorationDefinition asset = ScriptableObject.CreateInstance<TerrainDecorationDefinition>();
        asset.decorationId = GenerateUniqueDefinitionId("new_terrain_decoration");
        asset.displayName = "新地形装饰物";
        asset.category = TerrainDecorationCategory.Prop;
        asset.subCategory = "Custom";
        asset.icon = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultIconPath);
        asset.variants.Add(new TerrainDecorationVariant
        {
            variantId = "default",
            displayName = "默认版本",
            weight = 1,
        });

        string path = AssetDatabase.GenerateUniqueAssetPath($"{CustomFolder}/TD_{asset.decorationId}.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (createPrefab)
            CreateOrBindStandardPrefab(asset);

        Refresh();
        SelectDefinition(asset);
    }

    private void DeleteSelectedDefinition()
    {
        DeleteSelectedDefinitionWithConfirm(
            "删除地形装饰物定义",
            "确定删除当前自定义地形装饰物定义吗？",
            "删除");
    }

    private bool DeleteSelectedDefinitionWithConfirm(string title, string message, string okLabel)
    {
        if (selectedDefinition == null || selectedDefinition.isStandard)
            return false;

        string path = AssetDatabase.GetAssetPath(selectedDefinition);
        if (string.IsNullOrEmpty(path))
            return false;

        if (!EditorUtility.DisplayDialog(title, message, okLabel, "取消"))
            return false;

        bool deleted = AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        selectedDefinition = null;
        selectedSO = null;
        Refresh();
        return deleted;
    }

    private void HandleLeftListKeyboardShortcuts(Rect leftListRect)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown)
            return;

        if (!leftListRect.Contains(e.mousePosition))
            return;

        if (EditorGUIUtility.editingTextField)
            return;

        bool action = e.control || e.command;

        if (action && e.keyCode == KeyCode.C)
        {
            CopySelectedDefinitionToClipboard(false);
            e.Use();
            return;
        }

        if (action && e.keyCode == KeyCode.X)
        {
            CutSelectedDefinitionToClipboard();
            e.Use();
            return;
        }

        if (action && e.keyCode == KeyCode.V)
        {
            PasteDefinitionFromClipboard();
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            DeleteSelectedDefinition();
            e.Use();
        }
    }

    private bool CopySelectedDefinitionToClipboard(bool fromCut)
    {
        if (selectedDefinition == null)
            return false;

        TerrainDecorationDefinition snapshot = ScriptableObject.CreateInstance<TerrainDecorationDefinition>();
        EditorUtility.CopySerialized(selectedDefinition, snapshot);

        clipboardSnapshot = snapshot;
        clipboardFromCut = fromCut;
        clipboardSourceDecorationId = selectedDefinition.decorationId;
        return true;
    }

    private void CutSelectedDefinitionToClipboard()
    {
        if (selectedDefinition == null || selectedDefinition.isStandard)
            return;

        if (!CopySelectedDefinitionToClipboard(true))
            return;

        bool deleted = DeleteSelectedDefinitionWithConfirm(
            "剪切地形装饰物定义",
            "剪切会先把当前自定义地形装饰物放入剪贴板，并从列表中移除。之后可用 Ctrl+V 粘贴为新的定义。",
            "剪切");

        if (!deleted)
            clipboardFromCut = false;
    }

    private void PasteDefinitionFromClipboard()
    {
        if (clipboardSnapshot == null)
            return;

        EnsureFolderExists(CustomFolder);

        TerrainDecorationDefinition pasted = ScriptableObject.CreateInstance<TerrainDecorationDefinition>();
        EditorUtility.CopySerialized(clipboardSnapshot, pasted);

        string baseId = string.IsNullOrWhiteSpace(clipboardSourceDecorationId)
            ? "terrain_decoration"
            : clipboardSourceDecorationId;
        if (!clipboardFromCut)
            baseId += "_copy";

        pasted.decorationId = GenerateUniqueDefinitionId(baseId);
        pasted.isStandard = false;

        if (!clipboardFromCut)
            pasted.displayName = MakeCopyDisplayName(pasted.displayName);

        string path = AssetDatabase.GenerateUniqueAssetPath($"{CustomFolder}/TD_{pasted.decorationId}.asset");
        AssetDatabase.CreateAsset(pasted, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        clipboardFromCut = false;

        Refresh();
        SelectDefinition(AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(path));
    }

    private string MakeCopyDisplayName(string original)
    {
        if (string.IsNullOrWhiteSpace(original))
            return "地形装饰物 副本";

        if (original.EndsWith(" 副本"))
            return original;

        return original + " 副本";
    }

    private void CreateOrBindStandardPrefab(TerrainDecorationDefinition def)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再创建或绑定标准容器 Prefab。新实例结构由 Builder 在放置时生成。");
    }

    private void RepairDefinitionPrefabAndPlacedInstances(TerrainDecorationDefinition def)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再矫正 Prefab 或已摆放实例。旧实例请走 Migration 工具。");
    }

    private void RepairDefinitionPhysicsTemplate(TerrainDecorationDefinition def)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再修复物理模板。物理结构由 Builder / Migration 处理。");
    }

    private GameObject GetOrCreateRuntimeTemplatePrefab(TerrainDecorationDefinition def)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：新流程不再创建 PF_TD RuntimeTemplate。");
        return null;
    }

    private GameObject FindRuntimeTemplatePrefab(TerrainDecorationDefinition def)
    {
        return null;
    }

    private string GetFirstVariantId(TerrainDecorationDefinition def)
    {
        if (def != null && def.variants != null && def.variants.Count > 0 && def.variants[0] != null && !string.IsNullOrWhiteSpace(def.variants[0].variantId))
            return def.variants[0].variantId;

        return "default";
    }

    private void RepairSelectedRuntimeInstancePhysicsOnly(TerrainDecorationDefinition def)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再修复当前选中实例。旧实例请走 Migration 工具。");
    }

    private void ForceInstallPhysicsComponentsOnRuntimeRoot(GameObject runtimeRoot, SkyPrisonTerrainDecorationPhysicsSettings settings)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再直接安装 Rigidbody / MeshCollider。");
    }

    private void ForceCleanPhysicsComponentsOnChildrenOnly(GameObject runtimeRoot)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再清理 Scene 实例物理组件。");
    }

    private void ForceCleanPhysicsComponentsOnRuntimeRootAndChildren(GameObject runtimeRoot)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再清理 Scene 实例物理组件。");
    }

    private void RepairPrimaryPrefab(TerrainDecorationDefinition def)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再修复主 Prefab。");
    }

    private bool RepairRuntimeTemplatePrefabAndReturnResult(GameObject prefab, TerrainDecorationDefinition def, SkyPrisonTerrainDecorationPhysicsSettings physicsSettings)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再修复 RuntimeTemplate Prefab。");
        return false;
    }

    private bool RepairPrefabAssetAndReturnResult(GameObject prefab, TerrainDecorationDefinition def, SkyPrisonTerrainDecorationPhysicsSettings physicsSettings)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再修复 Prefab Asset。");
        return false;
    }


    private SkyPrisonTerrainDecorationPhysicsSettings GetOrCreatePhysicsSettings(TerrainDecorationDefinition def, bool create)
    {
        if (def == null)
            return null;

        string id = string.IsNullOrWhiteSpace(def.decorationId) ? def.name : def.decorationId;
        string filter = $"t:SkyPrisonTerrainDecorationPhysicsSettings {id}";
        string[] guids = AssetDatabase.FindAssets(filter, new[] { PhysicsSettingsFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            SkyPrisonTerrainDecorationPhysicsSettings found = AssetDatabase.LoadAssetAtPath<SkyPrisonTerrainDecorationPhysicsSettings>(path);
            if (found != null && found.decorationId == id)
                return found;
        }

        if (!create)
            return null;

        EnsureFolderExists(PhysicsSettingsFolder);
        SkyPrisonTerrainDecorationPhysicsSettings settings = ScriptableObject.CreateInstance<SkyPrisonTerrainDecorationPhysicsSettings>();
        settings.decorationId = id;
        settings.displayName = GetDisplayName(def);
        string safeName = MakeSafeId(id);
        string pathNew = AssetDatabase.GenerateUniqueAssetPath($"{PhysicsSettingsFolder}/{safeName}_PhysicsSettings.asset");
        AssetDatabase.CreateAsset(settings, pathNew);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return settings;
    }


    private void DisableRuntimeApplierAutoApply(GameObject root)
    {
        if (root == null)
            return;

        TerrainDecorationRuntimeApplier applier = root.GetComponent<TerrainDecorationRuntimeApplier>();
        if (applier == null)
            return;

        SerializedObject so = new SerializedObject(applier);
        SerializedProperty applyOnEnableProp = so.FindProperty("applyOnEnable");
        SerializedProperty applyInEditModeProp = so.FindProperty("applyInEditMode");

        if (applyOnEnableProp != null)
            applyOnEnableProp.boolValue = false;

        if (applyInEditModeProp != null)
            applyInEditModeProp.boolValue = false;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(applier);
    }
    private void ApplyPhysicsStructureBySettings(GameObject root, TerrainDecorationDefinition def, SkyPrisonTerrainDecorationPhysicsSettings settings)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再向实例写入物理结构。");
    }

    private GameObject ResolveTerrainDecorationRuntimeRoot(GameObject any)
    {
        if (any == null)
            return null;

        Transform t = any.transform;
        while (t != null)
        {
            GameObject go = t.gameObject;
            if (go.GetComponent<TerrainDecorationRuntimeBinder>() != null || go.GetComponent<TerrainDecorationRuntimeApplier>() != null)
                return go;
            t = t.parent;
        }

        return null;
    }

    private bool IsSameTerrainDecorationDefinition(TerrainDecorationDefinition a, TerrainDecorationDefinition b)
    {
        if (a == null || b == null)
            return false;

        if (ReferenceEquals(a, b))
            return true;

        string pathA = AssetDatabase.GetAssetPath(a);
        string pathB = AssetDatabase.GetAssetPath(b);
        if (!string.IsNullOrEmpty(pathA) && !string.IsNullOrEmpty(pathB) && pathA == pathB)
            return true;

        if (!string.IsNullOrWhiteSpace(a.decorationId) && !string.IsNullOrWhiteSpace(b.decorationId) && a.decorationId == b.decorationId)
            return true;

        return false;
    }

    private void EnsureStaticCollisionOnlyStructure(GameObject root, SkyPrisonTerrainDecorationPhysicsSettings settings)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：静态碰撞结构由 Builder / Migration 处理。");
    }

    private void RemoveLegacyPushableColliderRoot(GameObject root, bool useUndo)
    {
        if (root == null)
            return;

        Transform pushableColliderRoot = root.transform.Find("PushableColliderRoot");
        if (pushableColliderRoot != null)
            DestroyGameObjectImmediateSafe(pushableColliderRoot.gameObject, useUndo);
    }

    private void ForceStaticCollisionProxyState(GameObject root, SkyPrisonTerrainDecorationPhysicsSettings settings)
    {
        if (root == null)
            return;

        Transform meshRoot = root.transform.Find("RuleRoot/CollisionRoot/Main_Collision_MeshRoot");
        if (meshRoot == null)
            meshRoot = root.transform.Find("CollisionRoot/Main_Collision_MeshRoot");
        if (meshRoot == null)
            return;

        int worldLayer = LayerMask.NameToLayer("World3D");
        Transform[] all = meshRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null)
                continue;

            if (worldLayer >= 0)
                t.gameObject.layer = worldLayer;

            MeshCollider collider = t.GetComponent<MeshCollider>();
            if (collider == null)
                continue;

            collider.isTrigger = false;
            // 静态遮挡/静态阻挡允许非 Convex，避免空心支架被凸包封成实心块。
            collider.convex = false;
            EditorUtility.SetDirty(collider);
        }
    }

    private void SetRuleMeshPhysicsLayer(GameObject root, int layer)
    {
        if (root == null || layer < 0)
            return;

        Transform meshRoot = root.transform.Find("RuleRoot/CollisionRoot/Main_Collision_MeshRoot");
        if (meshRoot == null)
            meshRoot = root.transform.Find("CollisionRoot/Main_Collision_MeshRoot");
        if (meshRoot == null)
            return;

        Transform[] all = meshRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null)
                all[i].gameObject.layer = layer;
        }
    }

    private void RestoreLegacyDecorationPhysics(GameObject root)
    {
        if (root == null)
            return;

        DisableTerrainDecorationPhysics(root);
    }

    private void DisableTerrainDecorationPhysics(GameObject root)
    {
        if (root == null)
            return;

        // 先清旧命名污染节点与根/视觉模型上的物理组件。
        RemoveNamedPhysicsPollutionNodes(root.transform);
        RemovePushablePhysicsComponentsRecursive(root, includeRoot: true);

        // 再清真正会参与体积碰撞的生成结构。
        // 注意：不碰 BackTrigger / FrontTrigger / FrontOccluderProxy_Box，
        // 它们属于前后遮挡规则，不是物理阻挡。
        RemoveGeneratedPhysicalCollisionProxies(root, useUndo: false);

        int legacyLayer = LayerMask.NameToLayer("World3D");
        if (legacyLayer >= 0)
            SetLayerRecursivelyForPhysicsDisabled(root.transform, legacyLayer);

        EditorUtility.SetDirty(root);
    }

    private void SetLayerRecursivelyForPhysicsDisabled(Transform root, int layer)
    {
        if (root == null)
            return;

        // 只把运行时物理根和已失效的物理代理区域收回 World3D。
        // 遮挡规则盒自身的 Layer 由 RuntimeApplier 管理，不在这里乱改。
        root.gameObject.layer = layer;
    }

    private void RemoveGeneratedPhysicalCollisionProxies(GameObject root, bool useUndo)
    {
        if (root == null)
            return;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = all.Length - 1; i >= 0; i--)
        {
            Transform t = all[i];
            if (t == null)
                continue;

            string n = t.name;
            bool deleteWholeNode =
                n == "PushableColliderRoot" ||
                n == "PhysicsMeshColliderRoot" ||
                n == "Main_Collision_MeshRoot" ||
                n.StartsWith("__PhysicsMeshCollider_", System.StringComparison.Ordinal);

            if (deleteWholeNode)
            {
                DestroyGameObjectImmediateSafe(t.gameObject, useUndo);
                continue;
            }

            // CollisionRoot 下的 Main/Sub 物理盒是体积阻挡来源；关闭物理结构时必须去掉 Collider。
            // GameObject 可以保留，避免 RuntimeApplier 依赖节点名时断链。
            if (IsInsideCollisionRoot(t, root.transform))
                RemoveAllCollidersOnTransform(t, useUndo);
        }
    }

    private bool IsInsideCollisionRoot(Transform t, Transform runtimeRoot)
    {
        if (t == null || runtimeRoot == null)
            return false;

        Transform current = t;
        while (current != null && current != runtimeRoot)
        {
            if (current.name == "CollisionRoot")
                return true;
            current = current.parent;
        }

        return false;
    }

    private void RemoveAllCollidersOnTransform(Transform t, bool useUndo)
    {
        if (t == null)
            return;

        Collider[] colliders = t.GetComponents<Collider>();
        for (int i = colliders.Length - 1; i >= 0; i--)
        {
            if (colliders[i] == null)
                continue;

            if (useUndo)
                Undo.DestroyObjectImmediate(colliders[i]);
            else
                Object.DestroyImmediate(colliders[i], true);
        }
    }

    private void DestroyGameObjectImmediateSafe(GameObject go, bool useUndo)
    {
        if (go == null)
            return;

        if (useUndo)
            Undo.DestroyObjectImmediate(go);
        else
            Object.DestroyImmediate(go, true);
    }

    private void EnsurePushablePhysicsStructure(GameObject root, SkyPrisonTerrainDecorationPhysicsSettings settings)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：可推动物理结构由 Builder / Migration 处理。");
    }

    private void ApplyPushableRuntimeSettings(SkyPrisonPushablePropRuntime runtime, SkyPrisonTerrainDecorationPhysicsSettings settings)
    {
        if (runtime == null || settings == null)
            return;

        runtime.receiveVolumeCollision = settings.receiveVolumeCollision;
        runtime.receiveAttackImpulse = settings.receiveAttackImpulse;
        runtime.receiveExplosionImpulse = settings.receiveExplosionImpulse;
        runtime.receiveScriptedImpulse = settings.receiveScriptedImpulse;

        runtime.stayKinematicUntilPushed = true;
        runtime.useGravityAfterActivated = false;
        runtime.returnToKinematicWhenStable = true;
        runtime.mass = Mathf.Max(0.01f, settings.mass);
        runtime.linearDamping = Mathf.Max(0f, settings.linearDamping);
        runtime.angularDamping = Mathf.Max(0f, settings.angularDamping);
        runtime.maxPlanarSpeed = Mathf.Max(0.01f, settings.maxPlanarSpeed);
        runtime.externalPushMultiplier = Mathf.Max(0f, settings.externalPushMultiplier);
        runtime.applyForceAtTop = settings.applyForceAtTop;
        runtime.topForceHeight = settings.topForceHeight;
        runtime.topForceMultiplier = settings.topForceMultiplier;
        runtime.enableKnockdown = settings.enableKnockdown;
        runtime.protectAfterPivotRelease = settings.protectAfterPivotRelease;
        runtime.useLastKnownGroundWhenRayMisses = settings.useLastKnownGroundWhenRayMisses;
        runtime.useFallbackGroundPlaneWhenRayMisses = settings.useFallbackGroundPlaneWhenRayMisses;
        runtime.fallbackGroundY = settings.fallbackGroundY;
        runtime.pushableLayerName = settings.pushableLayerName;
    }

    private void RemoveRootColliders(GameObject root, bool useUndo)
    {
        if (root == null)
            return;

        Collider[] rootColliders = root.GetComponents<Collider>();
        for (int i = rootColliders.Length - 1; i >= 0; i--)
        {
            if (rootColliders[i] == null)
                continue;

            if (useUndo)
                Undo.DestroyObjectImmediate(rootColliders[i]);
            else
                Object.DestroyImmediate(rootColliders[i], true);
        }
    }

    private void RemoveVisualRootColliders(GameObject runtimeRoot, bool useUndo)
    {
        if (runtimeRoot == null)
            return;

        Transform visualRoot = runtimeRoot.transform.Find("VisualRoot");
        if (visualRoot == null)
            return;

        Collider[] colliders = visualRoot.GetComponentsInChildren<Collider>(true);
        for (int i = colliders.Length - 1; i >= 0; i--)
        {
            Collider col = colliders[i];
            if (col == null)
                continue;
            if (IsInsideProtectedManualOrRuleRoot(col.transform, runtimeRoot.transform))
                continue;

            if (useUndo)
                Undo.DestroyObjectImmediate(col);
            else
                Object.DestroyImmediate(col, true);
        }
    }

    private void RebuildMeshColliderProxiesFromVisualRoot(GameObject runtimeRoot, SkyPrisonTerrainDecorationPhysicsSettings settings, bool useUndo)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再重建 MeshCollider 代理。");
    }

    private Vector3 GetRuntimePhysicsProxyScale(Transform runtimeRoot)
    {
        if (runtimeRoot == null)
            return Vector3.one;

        TerrainDecorationRuntimeBinder binder = runtimeRoot.GetComponent<TerrainDecorationRuntimeBinder>();
        if (binder != null)
        {
            SerializedObject binderSO = new SerializedObject(binder);
            SerializedProperty finalScaleProp = binderSO.FindProperty("finalScale");
            if (finalScaleProp != null && finalScaleProp.propertyType == SerializedPropertyType.Vector3)
            {
                Vector3 finalScale = finalScaleProp.vector3Value;
                if (IsUsablePhysicsScale(finalScale))
                    return finalScale;
            }
        }

        Transform visualRoot = runtimeRoot.Find("VisualRoot");
        if (visualRoot != null && IsUsablePhysicsScale(visualRoot.localScale))
            return visualRoot.localScale;

        return Vector3.one;
    }

    private bool IsUsablePhysicsScale(Vector3 scale)
    {
        return Mathf.Abs(scale.x) > 0.0001f && Mathf.Abs(scale.y) > 0.0001f && Mathf.Abs(scale.z) > 0.0001f;
    }

    private GameObject CreatePhysicsMeshProxy(Transform parent, Transform runtimeRoot, string name, Mesh mesh, bool convex, int layer, bool useUndo)
    {
        if (parent == null)
            return null;

        GameObject proxy = new GameObject(name);
        if (useUndo)
            Undo.RegisterCreatedObjectUndo(proxy, "创建 MeshCollider 物理代理");
        proxy.transform.SetParent(parent, false);
        if (layer >= 0)
            proxy.layer = layer;

        ForceEnsureMeshCollider(proxy, mesh, convex, useUndo);

        EditorUtility.SetDirty(proxy);
        return proxy;
    }

    private void ForceEnsureMeshCollider(GameObject proxy, Mesh mesh, bool convex, bool useUndo)
    {
        if (proxy == null)
            return;

        MeshCollider collider = proxy.GetComponent<MeshCollider>();
        if (collider == null)
        {
            collider = useUndo
                ? Undo.AddComponent<MeshCollider>(proxy)
                : proxy.AddComponent<MeshCollider>();
        }

        if (collider == null)
        {
            Debug.LogError($"[TerrainDecoration] 无法给 {proxy.name} 添加 MeshCollider。请检查 Unity 物理模块是否正常。", proxy);
            return;
        }

        collider.hideFlags = HideFlags.None;
        collider.convex = convex;
        collider.isTrigger = false;

        // 先保证组件一定存在。Mesh 为空时也保留 MeshCollider 组件，
        // 这样不会再出现“__PhysicsMeshCollider_Custom 节点有了但组件没了”的假成功状态。
        if (mesh != null)
        {
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
        }

        EditorUtility.SetDirty(collider);
        EditorUtility.SetDirty(proxy);
    }

    private void VerifyMeshColliderProxyRoot(Transform meshRoot, bool useUndo)
    {
        if (meshRoot == null)
            return;

        MeshCollider[] colliders = meshRoot.GetComponentsInChildren<MeshCollider>(true);
        if (colliders != null && colliders.Length > 0)
            return;

        Debug.LogWarning($"[TerrainDecoration] {meshRoot.name} 已创建，但下面没有任何 MeshCollider。通常是 Custom Physics Mesh 为空/不可用，或 VisualRoot 下没有可读取的 MeshFilter。", meshRoot.gameObject);
    }

    private void FinalVerifyAndForceAttachMeshColliders(GameObject runtimeRoot, SkyPrisonTerrainDecorationPhysicsSettings settings, bool useUndo)
    {
        if (runtimeRoot == null || settings == null)
            return;

        Transform meshRoot = runtimeRoot.transform.Find("RuleRoot/CollisionRoot/Main_Collision_MeshRoot");
        if (meshRoot == null)
            meshRoot = runtimeRoot.transform.Find("CollisionRoot/Main_Collision_MeshRoot");
        if (meshRoot == null)
            return;

        int pushableLayer = LayerMask.NameToLayer(string.IsNullOrWhiteSpace(settings.pushableLayerName) ? "PushableProp" : settings.pushableLayerName);

        Mesh fallbackMesh = settings.customPhysicsMesh != null ? settings.customPhysicsMesh : FindBestVisibleMeshForPhysics(runtimeRoot);

        Transform[] children = meshRoot.GetComponentsInChildren<Transform>(true);
        bool hasAnyCollider = false;
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == null || child == meshRoot)
                continue;

            if (!child.name.StartsWith("__PhysicsMeshCollider_"))
                continue;

            if (pushableLayer >= 0)
                child.gameObject.layer = pushableLayer;

            if (child.name == "__PhysicsMeshCollider_Custom")
                child.localScale = GetRuntimePhysicsProxyScale(runtimeRoot.transform);

            Mesh meshForChild = fallbackMesh;

            MeshCollider collider = child.GetComponent<MeshCollider>();
            if (collider == null)
            {
                collider = useUndo
                    ? Undo.AddComponent<MeshCollider>(child.gameObject)
                    : child.gameObject.AddComponent<MeshCollider>();
            }

            if (collider == null)
            {
                Debug.LogError($"[TerrainDecoration] 兜底添加 MeshCollider 失败：{child.name}", child.gameObject);
                continue;
            }

            collider.hideFlags = HideFlags.None;
            collider.isTrigger = false;
            collider.convex = settings.forceConvexMeshCollider;
            if (meshForChild != null && collider.sharedMesh != meshForChild)
            {
                collider.sharedMesh = null;
                collider.sharedMesh = meshForChild;
            }

            hasAnyCollider = true;
            EditorUtility.SetDirty(collider);
            EditorUtility.SetDirty(child.gameObject);
        }

        if (!hasAnyCollider)
        {
            Debug.LogWarning($"[TerrainDecoration] {runtimeRoot.name} 的 Main_Collision_MeshRoot 下仍然没有 MeshCollider。请检查物理设置的 Custom Physics Mesh 或 VisualRoot 下是否存在 MeshFilter。", meshRoot.gameObject);
        }

        EditorUtility.SetDirty(meshRoot.gameObject);
    }


    private Transform GetOrCreateCollisionRootForPhysics(GameObject runtimeRoot, bool useUndo)
    {
        if (runtimeRoot == null)
            return null;

        Transform ruleRoot = runtimeRoot.transform.Find("RuleRoot");
        if (ruleRoot != null)
        {
            Transform ruleCollisionRoot = ruleRoot.Find("CollisionRoot");
            if (ruleCollisionRoot != null)
                return ruleCollisionRoot;

            GameObject collisionRootGo = new GameObject("CollisionRoot");
            if (useUndo)
                Undo.RegisterCreatedObjectUndo(collisionRootGo, "创建 RuleRoot/CollisionRoot");
            collisionRootGo.transform.SetParent(ruleRoot, false);
            return collisionRootGo.transform;
        }

        Transform directCollisionRoot = runtimeRoot.transform.Find("CollisionRoot");
        if (directCollisionRoot != null)
            return directCollisionRoot;

        GameObject fallback = new GameObject("CollisionRoot");
        if (useUndo)
            Undo.RegisterCreatedObjectUndo(fallback, "创建 CollisionRoot");
        fallback.transform.SetParent(runtimeRoot.transform, false);
        return fallback.transform;
    }

    private void ClearAllAutoCollisionRootsForMeshPhysics(Transform runtimeRoot, Transform preferredCollisionRoot, bool useUndo)
    {
        if (runtimeRoot == null)
            return;

        List<Transform> collisionRoots = new List<Transform>();
        Transform[] all = runtimeRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t != null && t.name == "CollisionRoot")
                collisionRoots.Add(t);
        }

        if (preferredCollisionRoot != null && !collisionRoots.Contains(preferredCollisionRoot))
            collisionRoots.Add(preferredCollisionRoot);

        for (int i = 0; i < collisionRoots.Count; i++)
            ClearAutoCollisionRootChildrenForMeshPhysics(collisionRoots[i], useUndo);

        // 如果旧版本在根节点下创建了一个空的直连 CollisionRoot，而真正结构是 RuleRoot/CollisionRoot，
        // 则把空直连 CollisionRoot 清掉，避免层级里出现两个同名根节点让人误判。
        Transform directCollisionRoot = runtimeRoot.Find("CollisionRoot");
        Transform ruleCollisionRoot = runtimeRoot.Find("RuleRoot/CollisionRoot");
        if (directCollisionRoot != null && ruleCollisionRoot != null && directCollisionRoot != ruleCollisionRoot && directCollisionRoot.childCount == 0)
        {
            if (useUndo)
                Undo.DestroyObjectImmediate(directCollisionRoot.gameObject);
            else
                Object.DestroyImmediate(directCollisionRoot.gameObject, true);
        }
    }

    private void ClearAutoCollisionRootChildrenForMeshPhysics(Transform collisionRoot, bool useUndo)
    {
        if (collisionRoot == null)
            return;

        for (int i = collisionRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = collisionRoot.GetChild(i);
            if (child == null)
                continue;

            string n = child.name;
            bool isAutoOldBox = n == "Main_Collision_Box" || n.StartsWith("Main_Collision_Box");
            bool isAutoMeshRoot = n == "Main_Collision_MeshRoot" || n == "PhysicsMeshColliderRoot" || n.StartsWith("__PhysicsMeshCollider_");

            if (!isAutoOldBox && !isAutoMeshRoot)
                continue;

            if (useUndo)
                Undo.DestroyObjectImmediate(child.gameObject);
            else
                Object.DestroyImmediate(child.gameObject, true);
        }
    }

    private void CopyWorldTransformToDirectChildProxy(Transform proxy, Transform source, Transform parentForScale)
    {
        if (proxy == null || source == null || parentForScale == null)
            return;

        proxy.position = source.position;
        proxy.rotation = source.rotation;
        proxy.localScale = DivideVector3Safe(source.lossyScale, parentForScale.lossyScale);
    }

    private Vector3 DivideVector3Safe(Vector3 a, Vector3 b)
    {
        return new Vector3(
            Mathf.Abs(b.x) < 0.0001f ? a.x : a.x / b.x,
            Mathf.Abs(b.y) < 0.0001f ? a.y : a.y / b.y,
            Mathf.Abs(b.z) < 0.0001f ? a.z : a.z / b.z
        );
    }

    private List<MeshSourceInfo> CollectVisibleMeshSources(Transform runtimeRoot)
    {
        List<MeshSourceInfo> result = new List<MeshSourceInfo>();
        if (runtimeRoot == null)
            return result;

        Transform visualRoot = runtimeRoot.Find("VisualRoot");
        Transform searchRoot = visualRoot != null ? visualRoot : runtimeRoot;

        MeshFilter[] filters = searchRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;
            if (ShouldSkipPhysicsMeshCandidate(filter.transform))
                continue;

            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled)
                continue;

            result.Add(new MeshSourceInfo(filter.transform, filter.sharedMesh, renderer.bounds));
        }

        SkinnedMeshRenderer[] skinnedRenderers = searchRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinnedRenderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = skinnedRenderers[i];
            if (renderer == null || renderer.sharedMesh == null || !renderer.enabled)
                continue;
            if (ShouldSkipPhysicsMeshCandidate(renderer.transform))
                continue;

            result.Add(new MeshSourceInfo(renderer.transform, renderer.sharedMesh, renderer.bounds));
        }

        return result;
    }

    private bool IsUnderVisualRoot(Transform t, Transform runtimeRoot)
    {
        if (t == null || runtimeRoot == null)
            return false;

        Transform current = t;
        while (current != null && current != runtimeRoot)
        {
            if (current.name == "VisualRoot")
                return true;
            current = current.parent;
        }

        return false;
    }


    private bool IsPhysicsMeshColliderProxyNode(Transform t)
    {
        while (t != null)
        {
            string n = t.name;
            if (n == "Main_Collision_MeshRoot" || n.StartsWith("__PhysicsMeshCollider_"))
                return true;
            t = t.parent;
        }
        return false;
    }
    private bool IsInsideProtectedManualOrRuleRoot(Transform t, Transform runtimeRoot)
    {
        if (t == null || runtimeRoot == null)
            return false;

        Transform current = t;
        while (current != null && current != runtimeRoot)
        {
            string n = current.name;
            if (n == "ManualProxies" || n == "RuleRoot" || n == "CollisionRoot" || n == "VisionBlockerRoot" ||
                n == "FrontOccluderRoot" || n == "OutlineMaskProxyRoot" || n == "ShadowCasterRoot" ||
                n == "StencilWriterRoot" || n == "EditorGizmoRoot")
                return true;
            current = current.parent;
        }

        return false;
    }

    private sealed class MeshSourceInfo
    {
        public readonly Transform source;
        public readonly Mesh mesh;
        public readonly Bounds bounds;

        public MeshSourceInfo(Transform source, Mesh mesh, Bounds bounds)
        {
            this.source = source;
            this.mesh = mesh;
            this.bounds = bounds;
        }
    }

    private void RemovePushablePhysicsComponentsRecursive(GameObject root, bool includeRoot)
    {
        if (root == null)
            return;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = all.Length - 1; i >= 0; i--)
        {
            Transform t = all[i];
            if (t == null)
                continue;

            bool isRoot = t.gameObject == root;
            if (isRoot && !includeRoot)
                continue;

            // RuleRoot / CollisionRoot / Main_Collision_MeshRoot / __PhysicsMeshCollider_* 是规则与新版物理代理区。
            // 非根节点处在这些区域时，不允许任何“清污染”逻辑删除它们的组件。
            if (!isRoot && (IsInsideProtectedManualOrRuleRoot(t, root.transform) || IsPhysicsMeshColliderProxyNode(t)))
                continue;

            // 旧系统节点下的 BoxCollider / Trigger 等不能删；这里只清物理系统专属组件。
            SkyPrisonPushablePropRuntime runtime = t.GetComponent<SkyPrisonPushablePropRuntime>();
            if (runtime != null)
                Object.DestroyImmediate(runtime, true);

            Rigidbody rb = t.GetComponent<Rigidbody>();
            if (rb != null)
                Object.DestroyImmediate(rb, true);

            MeshCollider meshCollider = t.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                // 2026-05-16 修正：__PhysicsMeshCollider_* 是新版合法物理代理，不再当污染清理。
                // 只允许清理根节点旧 MeshCollider，或 VisualRoot/视觉模型上误挂的 MeshCollider。
                bool removeMeshCollider = isRoot || IsUnderVisualRoot(t, root.transform);
                if (removeMeshCollider && !IsInsideProtectedManualOrRuleRoot(t, root.transform))
                    Object.DestroyImmediate(meshCollider, true);
            }

            if (isRoot)
            {
                Collider[] colliders = t.GetComponents<Collider>();
                for (int c = colliders.Length - 1; c >= 0; c--)
                {
                    if (colliders[c] != null)
                        Object.DestroyImmediate(colliders[c], true);
                }
            }
        }
    }

    private Mesh FindBestVisibleMeshForPhysics(GameObject root)
    {
        if (root == null)
            return null;

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        Mesh bestMesh = null;
        float bestVolume = -1f;

        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;

            if (ShouldSkipPhysicsMeshCandidate(filter.transform))
                continue;

            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled)
                continue;

            Bounds b = renderer.bounds;
            float volume = Mathf.Abs(b.size.x * b.size.y * b.size.z);
            if (volume > bestVolume)
            {
                bestVolume = volume;
                bestMesh = filter.sharedMesh;
            }
        }

        if (bestMesh != null)
            return bestMesh;

        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i] != null && filters[i].sharedMesh != null && !ShouldSkipPhysicsMeshCandidate(filters[i].transform))
                return filters[i].sharedMesh;
        }

        return null;
    }

    private bool ShouldSkipPhysicsMeshCandidate(Transform t)
    {
        while (t != null)
        {
            string n = t.name;
            if (n.Contains("RuleRoot") || n.Contains("CollisionRoot") || n.Contains("Main_Collision_Box") ||
                n.Contains("FrontTrigger") || n.Contains("BackTrigger") || n.Contains("VisionBlockerRoot") ||
                n.Contains("FrontOccluderRoot") || n.Contains("OutlineMaskProxyRoot") || n.Contains("ShadowCasterRoot") ||
                n.Contains("StencilWriterRoot") || n.Contains("EditorGizmoRoot") || n.Contains("__Auto") ||
                n.Contains("PhysicsProxy") || n.Contains("PhysicsMeshColliderRoot") || n.Contains("__PhysicsMeshCollider_") || n.Contains("PushableBody") || n.Contains("PhysicsBody"))
                return true;
            t = t.parent;
        }
        return false;
    }

    private void RemoveNamedPhysicsPollutionNodes(Transform root)
    {
        if (root == null)
            return;

        List<GameObject> delete = new List<GameObject>();
        CollectNamedPhysicsPollutionNodes(root, delete);
        for (int i = 0; i < delete.Count; i++)
        {
            if (delete[i] != null)
                Object.DestroyImmediate(delete[i], true);
        }
    }

    private void CollectNamedPhysicsPollutionNodes(Transform t, List<GameObject> delete)
    {
        if (t == null)
            return;

        string n = t.name;
        if (n == "PhysicsProxyRoot" || n == "PhysicsMeshColliderRoot" || n == "PushableBody" || n == "PhysicsBody" || n == "SingleBodyPhysicsRoot" ||
            n.StartsWith("__PhysicsProxy") || n.StartsWith("__PushablePhysics"))
        {
            delete.Add(t.gameObject);
            return;
        }

        // 2026-05-16 修正：Main_Collision_MeshRoot / __PhysicsMeshCollider_* 是新版合法节点。
        // 不能在“清污染”流程里删除，否则手动或自动添加的 MeshCollider 会被下一次刷新/矫正吃掉。

        for (int i = 0; i < t.childCount; i++)
            CollectNamedPhysicsPollutionNodes(t.GetChild(i), delete);
    }

    private void RemoveMissingScriptsRecursive(GameObject root)
    {
        if (root == null)
            return;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null)
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(children[i].gameObject);
        }
    }

    private GameObject GetPrimaryPrefab(TerrainDecorationDefinition def)
    {
        if (def == null || def.variants == null || def.variants.Count == 0 || def.variants[0] == null)
            return null;
        return def.variants[0].prefab;
    }

    private void DrawCategoryPopup()
    {
        SerializedProperty prop = selectedSO.FindProperty("category");
        if (prop == null)
        {
            EditorGUILayout.LabelField("主分类", "字段不存在");
            return;
        }

        TerrainDecorationCategory[] values = (TerrainDecorationCategory[])System.Enum.GetValues(typeof(TerrainDecorationCategory));
        string[] labels = values.Select(GetCategoryMainLabel).ToArray();
        int currentIndex = Mathf.Clamp(prop.enumValueIndex, 0, values.Length - 1);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("主分类", GUILayout.Width(150f));
        int newIndex = EditorGUILayout.Popup(currentIndex, labels);
        prop.enumValueIndex = newIndex;
        EditorGUILayout.EndHorizontal();
    }

    private void PingOrCreateVisualPrefabFolder()
    {
        EnsureFolderExists(DefaultVisualPrefabFolder);
        Object folder = AssetDatabase.LoadAssetAtPath<Object>(DefaultVisualPrefabFolder);
        if (folder != null)
        {
            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }
    }

    private void AutoBindFirstVisualPrefabFromFolder(TerrainDecorationDefinition def)
    {
        if (def == null)
            return;

        EnsureFolderExists(DefaultVisualPrefabFolder);
        GameObject prefab = FindBestVisualPrefab(def, null);
        if (prefab == null)
        {
            PingOrCreateVisualPrefabFolder();
            EditorUtility.DisplayDialog("没有找到 PF", "默认目录下没有找到可绑定的 Prefab。\n请把 PF 放入：\n" + DefaultVisualPrefabFolder, "知道了");
            return;
        }

        EnsureFirstVariant(def);
        def.variants[0].prefab = prefab;
        if (string.IsNullOrWhiteSpace(def.variants[0].variantId))
            def.variants[0].variantId = "default";
        if (string.IsNullOrWhiteSpace(def.variants[0].displayName))
            def.variants[0].displayName = "默认版本";

        EditorUtility.SetDirty(def);
        AssetDatabase.SaveAssets();
        selectedSO?.Update();
        EditorGUIUtility.PingObject(prefab);
    }

    private void AutoBindMissingVisualPrefabsFromFolder(TerrainDecorationDefinition def)
    {
        if (def == null)
            return;

        EnsureFolderExists(DefaultVisualPrefabFolder);
        EnsureFirstVariant(def);

        bool changed = false;
        for (int i = 0; i < def.variants.Count; i++)
        {
            TerrainDecorationVariant variant = def.variants[i];
            if (variant == null || variant.prefab != null)
                continue;

            GameObject prefab = FindBestVisualPrefab(def, variant);
            if (prefab == null)
                continue;

            variant.prefab = prefab;
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            selectedSO?.Update();
        }
        else
        {
            PingOrCreateVisualPrefabFolder();
            EditorUtility.DisplayDialog("没有可补齐的 PF", "没有找到匹配的 Prefab，或者所有变体已经绑定。", "知道了");
        }
    }

    private GameObject FindBestVisualPrefab(TerrainDecorationDefinition def, TerrainDecorationVariant variant)
    {
        EnsureFolderExists(DefaultVisualPrefabFolder);
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { DefaultVisualPrefabFolder });
        List<GameObject> prefabs = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null)
            .OrderBy(p => p.name)
            .ToList();

        if (prefabs.Count == 0)
            return null;

        List<string> keys = new List<string>();
        if (variant != null)
        {
            keys.Add(variant.variantId);
            keys.Add(variant.displayName);
        }
        if (def != null)
        {
            keys.Add(def.decorationId);
            keys.Add(def.displayName);
        }

        foreach (string rawKey in keys)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
                continue;
            string key = rawKey.ToLowerInvariant();
            GameObject matched = prefabs.FirstOrDefault(p => p.name.ToLowerInvariant().Contains(key));
            if (matched != null)
                return matched;
        }

        return prefabs[0];
    }

    private void EnsureFirstVariant(TerrainDecorationDefinition def)
    {
        if (def.variants == null)
            def.variants = new List<TerrainDecorationVariant>();
        if (def.variants.Count == 0)
        {
            def.variants.Add(new TerrainDecorationVariant
            {
                variantId = "default",
                displayName = "默认版本",
                weight = 1,
            });
        }
    }

    private string GenerateUniqueDefinitionId(string baseId)
    {
        HashSet<string> used = new HashSet<string>(definitions.Where(x => x != null).Select(x => x.decorationId));
        if (!used.Contains(baseId))
            return baseId;
        int i = 1;
        while (used.Contains(baseId + "_" + i))
            i++;
        return baseId + "_" + i;
    }

    private string GetDisplayName(TerrainDecorationDefinition def)
    {
        if (def == null)
            return "-";
        return string.IsNullOrWhiteSpace(def.displayName) ? def.decorationId : def.displayName;
    }

    private string GetCategoryLabel(TerrainDecorationDefinition def)
    {
        if (def == null)
            return "未分类";
        string main = GetCategoryMainLabel(def.category);
        if (string.IsNullOrWhiteSpace(def.subCategory))
            return main;
        return main + " / " + def.subCategory;
    }

    private string GetCategoryMainLabel(TerrainDecorationCategory category)
    {
        switch (category)
        {
            case TerrainDecorationCategory.Prop: return "通用道具";
            case TerrainDecorationCategory.Box: return "箱体";
            case TerrainDecorationCategory.Wall: return "墙体";
            case TerrainDecorationCategory.Pillar: return "柱体";
            case TerrainDecorationCategory.FloorAttachment: return "地面装饰";
            case TerrainDecorationCategory.Moss: return "苔藓";
            case TerrainDecorationCategory.Ruin: return "残骸";
            case TerrainDecorationCategory.Pipe: return "管线";
            case TerrainDecorationCategory.Occluder: return "遮挡体";
            case TerrainDecorationCategory.Mechanism: return "机关";
            case TerrainDecorationCategory.Custom: return "自定义";
            default: return "未分类";
        }
    }

    private void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private void EnsureFolderExists(string folderPath)
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


    // 2026-05-16：自动矫正最后一步。
    // 物理碰撞按 MeshCollider；前后遮挡盒按 VisualRoot 的完整视觉 Bounds + 摄像机高度投影重新包围。
    // 规则：45 度镜头下，模型高度会投影到地面深度；投影厚度主要向 RuleRoot 的后方（当前约定 +Z）分配。
    private bool RepairProjectedOcclusionBoxesFromVisualRoot(GameObject root, bool useUndo)
    {
        Debug.LogWarning("[TD_DEFINITION_PAGE] 已废弃：定义页不再修复投影遮挡盒。遮挡盒由 Builder 按定义生成。");
        return false;
    }

    private bool ApplyProjectedOcclusionBox(Transform target, Renderer[] visualRenderers, float depthMultiplier, float minimumDepth, bool useUndo)
    {
        return false;
    }

    private ProjectedOcclusionBounds CalculateProjectedOcclusionBounds(Transform targetSpace, Renderer[] visualRenderers, float depthMultiplier, float minimumDepth)
    {
        bool initialized = false;
        Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);

        for (int i = 0; i < visualRenderers.Length; i++)
        {
            Renderer r = visualRenderers[i];
            if (r == null)
                continue;

            Bounds wb = r.bounds;
            Vector3 min = wb.min;
            Vector3 max = wb.max;

            Vector3[] corners = new Vector3[8]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z)
            };

            for (int c = 0; c < corners.Length; c++)
            {
                Vector3 local = targetSpace.InverseTransformPoint(corners[c]);
                if (!initialized)
                {
                    localBounds = new Bounds(local, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(local);
                }
            }
        }

        if (!initialized)
            localBounds = new Bounds(Vector3.zero, Vector3.one);

        Vector3 center = localBounds.center;
        Vector3 size = localBounds.size;

        const float cameraElevationDegrees = 45f;
        const float frontReserveRatio = 0.18f;
        const float backReserveRatio = 0.82f;
        const float horizontalPadding = 0.08f;
        const float verticalPadding = 0.08f;
        const float depthPadding = 0.12f;

        float modelHeight = Mathf.Max(0.01f, size.y);
        float elevationRad = Mathf.Clamp(cameraElevationDegrees, 5f, 85f) * Mathf.Deg2Rad;
        float projectedDepth = modelHeight / Mathf.Tan(elevationRad);

        float baseDepth = Mathf.Max(0.01f, size.z);
        float totalDepth = Mathf.Max(minimumDepth, baseDepth + projectedDepth * Mathf.Max(0f, depthMultiplier) + depthPadding);
        float extraDepth = Mathf.Max(0f, totalDepth - baseDepth);
        float backwardShift = extraDepth * (backReserveRatio - frontReserveRatio) * 0.5f;

        // 当前 RuleRoot 约定：+Z 是后方。之前独立工具已经验证过，向后挪必须加 Z。
        center.z += backwardShift;

        size.x = Mathf.Max(0.05f, size.x + horizontalPadding * 2f);
        size.y = Mathf.Max(0.05f, size.y + verticalPadding * 2f);
        size.z = totalDepth;

        return new ProjectedOcclusionBounds(center, size);
    }

    private Renderer[] CollectOcclusionRepairVisualRenderers(Transform visualRoot)
    {
        List<Renderer> result = new List<Renderer>();
        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;
            if (!IsValidOcclusionRepairVisualRenderer(r))
                continue;
            result.Add(r);
        }
        return result.ToArray();
    }

    private bool IsValidOcclusionRepairVisualRenderer(Renderer renderer)
    {
        if (renderer == null)
            return false;

        Transform t = renderer.transform;
        while (t != null)
        {
            string n = t.name;
            if (n.StartsWith("__Auto", System.StringComparison.Ordinal)) return false;
            if (n.StartsWith("__PhysicsMeshCollider", System.StringComparison.Ordinal)) return false;
            if (n.IndexOf("Collision", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Collider", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Proxy", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Trigger", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Mask", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("Occluder", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            t = t.parent;
        }

        return true;
    }

    private readonly struct ProjectedOcclusionBounds
    {
        public readonly Vector3 center;
        public readonly Vector3 size;

        public ProjectedOcclusionBounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
        }
    }


    private bool TDMeshPhysicsGuard_IsProtectedProxyPath(Transform t)
    {
        if (t == null)
            return false;

        Transform current = t;
        while (current != null)
        {
            string n = current.name;
            if (n == "Main_Collision_MeshRoot" ||
                n.StartsWith("__PhysicsMeshCollider_", System.StringComparison.Ordinal) ||
                n == "ManualProxies" ||
                n == "CollisionRoot" ||
                n == "RuleRoot")
                return true;

            current = current.parent;
        }

        return false;
    }

}
