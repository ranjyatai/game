
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class SkyPrisonMapInspectorPanel
{
    private readonly SkyPrisonMapEditorPage page;
    private string editingFileName = "";
    private string editingFileNameAssetPath = "";
    private const int TriggerPackagePickerControlId = 24042501;
    private const float TriggerPackageListContainerHeight = 260f;
    private int triggerPackagePickerControlId = -1;
    private bool triggerPackagePickerAutoAdd = false;
    private TriggerPackage triggerPackagePickerSelected = null;
    private TriggerPackage triggerPackageToAdd = null;
    private Vector2 triggerPackageListScroll = Vector2.zero;
    private bool showMapBoundsAdvancedTools = false;

    public SkyPrisonMapInspectorPanel(SkyPrisonMapEditorPage page)
    {
        this.page = page;
    }

    private void ApplySelectedMapPropertiesNow()
    {
        if (page.SelectedMapSO == null || page.SelectedMap == null)
            return;

        page.SelectedMapSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(page.SelectedMap);
        page.SelectedMapSO.Update();
    }

    public void Draw()
    {
        MapDefinition map = page.SelectedMap;
        if (map == null || page.SelectedMapSO == null)
        {
            EditorGUILayout.HelpBox("未选择地图定义。", MessageType.Info);
            return;
        }

        SyncLocalizedBackfields(page.SelectedMapSO, map);
        DrawOverviewTwoColumn(map);

        page.DrawFoldoutSection("多语言名称", DrawLocalizedNames);
        page.DrawFoldoutSection("多语言描述", DrawLocalizedDescriptions);
        page.DrawFoldoutSection("地图边界", DrawMapBoundsSection);
        page.DrawFoldoutSection("战争迷雾", DrawFogSection);
        page.DrawFoldoutSection("天空与环境", DrawEnvironmentSection);
        page.DrawFoldoutSection("天气", DrawWeatherSection);
        page.DrawFoldoutSection("触发器", DrawTriggerPackagesSection);
        page.DrawFoldoutSection("地图BGM", DrawBGMSection);
        page.DrawFoldoutSection("镜头表现", DrawCameraSection);
        page.DrawFoldoutSection("Scene 关联", DrawSceneBindingSection);
        page.DrawFoldoutSection("基础节点", DrawBootstrapSection);
    }

    private const float OverviewLeftColumnWidth = 640f;
    private const float OverviewRightColumnWidth = 360f;
    private const float OverviewColumnGap = 10f;
    private const float MapBoundsPreviewBoxWidth = 300f;
    private const float MapBoundsPreviewBoxHeight = 260f;

    private void DrawOverviewTwoColumn(MapDefinition map)
    {
        // 顶部总览区必须始终保持左右双栏：
        // 左侧基础信息随窗口伸缩，右侧地图边界预览固定宽度并保持在同一行。
        // 这里不再在窄窗口下切换为上下布局，避免地图边界预览换行。
        float availableWidth = Mathf.Max(
            OverviewRightColumnWidth + OverviewColumnGap + 280f,
            EditorGUIUtility.currentViewWidth - 330f);
        float leftWidth = Mathf.Max(280f, availableWidth - OverviewRightColumnWidth - OverviewColumnGap - 16f);

        EditorGUILayout.BeginHorizontal("box", GUILayout.Width(availableWidth));

        EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth), GUILayout.MinWidth(280f));
        DrawHeaderContent(map);
        EditorGUILayout.Space(8f);
        DrawBasicInfoContent();
        EditorGUILayout.EndVertical();

        GUILayout.Space(OverviewColumnGap);

        EditorGUILayout.BeginVertical("box", GUILayout.Width(OverviewRightColumnWidth));
        DrawMapBoundsPreviewContent();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawHeaderContent(MapDefinition map)
    {
        EditorGUILayout.LabelField("地图编辑器工作台", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(GetBestDisplayName(map), EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(6f);
        DrawClippedReadonlyRow("资源路径", AssetDatabase.GetAssetPath(map));
        DrawClippedReadonlyRow("地图 Key", map.mapKey);
        DrawClippedReadonlyRow("文件名称", GetPackageFileName(map));
        DrawClippedReadonlyRow("关联 Scene", string.IsNullOrWhiteSpace(map.scenePath) ? "-" : map.scenePath);
        EditorGUILayout.Space(6f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("定位地图包", GUILayout.Height(28f), GUILayout.Width(120f)))
            SkyPrisonMapEditorUtility.PingMapPackage(map);
        if (GUILayout.Button("定位 Scene", GUILayout.Height(28f), GUILayout.Width(120f)))
            SkyPrisonMapEditorUtility.PingMapScene(map);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBasicInfoContent()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("基础信息", EditorStyles.boldLabel);
        EditorGUILayout.Space(4f);
        DrawClippedReadonlyRow("地图 Key", page.SelectedMap.mapKey);
        DrawEditableFileNameRow();
        DrawClippedReadonlyRow("主语言名称", GetBestDisplayName(page.SelectedMap));
        DrawReadonlyMultiline("主语言描述", GetPrimaryDescription(page.SelectedMap));
        DrawConstrainedMultilineProperty("备注描述", page.SelectedMapSO.FindProperty("description"), 58f);
        DrawConstrainedHelpBox("主语言名称与主语言描述由多语言字段自动同步。这里保留 description 作为运行时主语言回写字段。", MessageType.None);
    }

    private void DrawMapBoundsPreviewContent()
    {
        EditorGUILayout.LabelField("地图边界预览", EditorStyles.boldLabel);
        DrawMapBoundsPreviewSection();
    }

    private void DrawHeader(MapDefinition map)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("地图编辑器工作台", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(GetBestDisplayName(map), EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(6f);
        DrawClippedReadonlyRow("资源路径", AssetDatabase.GetAssetPath(map));
        DrawClippedReadonlyRow("地图 Key", map.mapKey);
        DrawClippedReadonlyRow("关联 Scene", string.IsNullOrWhiteSpace(map.scenePath) ? "-" : map.scenePath);
        EditorGUILayout.EndVertical();
    }

    private void DrawBasicInfo()
    {
        DrawClippedReadonlyRow("地图 Key", page.SelectedMap.mapKey);
        DrawEditableFileNameRow();
        DrawClippedReadonlyRow("主语言名称", GetBestDisplayName(page.SelectedMap));
        DrawReadonlyMultiline("主语言描述", GetPrimaryDescription(page.SelectedMap));
        DrawConstrainedMultilineProperty("备注描述", page.SelectedMapSO.FindProperty("description"), 58f);
        DrawConstrainedHelpBox("主语言名称与主语言描述由多语言字段自动同步。这里保留 description 作为运行时主语言回写字段。", MessageType.None);
    }

    private void DrawMapBoundsPreviewSection()
    {
        MapDefinition map = page.SelectedMap;
        if (map == null)
            return;

        EditorGUILayout.HelpBox("当前阶段这里只预览 MapBounds 的 XZ 边界比例。后续真正小地图会使用专用美术素材生成。", MessageType.None);

        Vector3 center = map.mapBoundsCenter;
        Vector3 size = map.mapBoundsSize;
        if (page.SelectedMapSO != null)
        {
            SerializedProperty centerProp = page.SelectedMapSO.FindProperty("mapBoundsCenter");
            SerializedProperty sizeProp = page.SelectedMapSO.FindProperty("mapBoundsSize");
            if (centerProp != null)
                center = centerProp.vector3Value;
            if (sizeProp != null)
                size = sizeProp.vector3Value;
        }
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        Rect rect = GUILayoutUtility.GetRect(
            MapBoundsPreviewBoxWidth,
            MapBoundsPreviewBoxWidth,
            MapBoundsPreviewBoxHeight,
            MapBoundsPreviewBoxHeight,
            GUILayout.Width(MapBoundsPreviewBoxWidth),
            GUILayout.Height(MapBoundsPreviewBoxHeight));
        DrawBoundaryPreview(rect, size);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        page.DrawReadonlyRow("边界中心", $"X {center.x:0.##} / Y {center.y:0.##} / Z {center.z:0.##}");
        page.DrawReadonlyRow("边界尺寸", $"X {size.x:0.##} / Y {size.y:0.##} / Z {size.z:0.##}");

        const float buttonGap = 8f;
        const float smallButtonWidth = 150f;
        const float openButtonWidth = smallButtonWidth * 2f + buttonGap;

        // 第一行：打开地图，宽度 = 下面两个按钮总宽
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("打开地图", GUILayout.Width(openButtonWidth), GUILayout.Height(26f)))
            SkyPrisonMapEditorUtility.OpenMapScene(page.SelectedMap);

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);

        // 第二行：边界操作
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("从 Scene 回读边界", GUILayout.Width(smallButtonWidth), GUILayout.Height(24f)))
        {
            SkyPrisonMapEditorUtility.PullSceneBoundsToMap(page.SelectedMap);
            page.EnsureSelectedSerializedObject();
            page.SelectedMapSO?.Update();
        }

        GUILayout.Space(buttonGap);

        if (GUILayout.Button("同步边界到 Scene", GUILayout.Width(smallButtonWidth), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapEditorUtility.SyncMapBoundsToScene(page.SelectedMap);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBoundaryPreview(Rect rect, Vector3 size)
    {
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.13f, 1f));
        DrawRectBorder(rect, new Color(1f, 1f, 1f, 0.12f));

        float safeX = Mathf.Max(1f, size.x);
        float safeZ = Mathf.Max(1f, size.z);
        float maxW = Mathf.Max(10f, rect.width - 44f);
        float maxH = Mathf.Max(10f, rect.height - 32f);
        float scale = Mathf.Min(maxW / safeX, maxH / safeZ);
        Rect mapRect = new Rect(rect.center.x - safeX * scale * 0.5f, rect.center.y - safeZ * scale * 0.5f, safeX * scale, safeZ * scale);

        EditorGUI.DrawRect(mapRect, new Color(0.33f, 0.70f, 0.52f, 0.18f));
        DrawRectBorder(mapRect, new Color(0.33f, 0.90f, 0.68f, 0.95f));

        EditorGUI.DrawRect(new Rect(mapRect.center.x - 12f, mapRect.center.y, 24f, 1f), new Color(1f, 1f, 1f, 0.28f));
        EditorGUI.DrawRect(new Rect(mapRect.center.x, mapRect.center.y - 12f, 1f, 24f), new Color(1f, 1f, 1f, 0.28f));

        GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.88f, 0.92f, 0.90f, 1f) }
        };
        GUI.Label(rect, $"MapBounds  X {safeX:0.#} / Z {safeZ:0.#}", style);
    }

    private void DrawRectBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private void DrawLocalizedNames()
    {
        SerializedProperty listProp = page.SelectedMapSO.FindProperty("localizedNames");
        if (listProp == null)
        {
            EditorGUILayout.HelpBox("找不到 localizedNames。", MessageType.Warning);
            return;
        }

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        if (settings == null)
        {
            EditorGUILayout.HelpBox("未找到 LocalizationProjectSettings。", MessageType.Warning);
            return;
        }

        EnsureLocalizedEntries(listProp, settings);
        PruneLocalizedEntries(listProp, settings);

        List<LocalizationProjectSettings.LanguageEntry> orderedLanguages = GetOrderedLanguages(settings);
        string defaultLanguageCode = GetDefaultLanguageCode(settings);

        for (int i = 0; i < orderedLanguages.Count; i++)
        {
            var lang = orderedLanguages[i];
            SerializedProperty entry = FindLocalizedEntry(listProp, lang.languageCode);
            if (entry == null) continue;
            SerializedProperty textProp = entry.FindPropertyRelative("text");

            EditorGUILayout.BeginHorizontal();
            string label = string.IsNullOrWhiteSpace(lang.displayName) ? lang.languageCode : lang.displayName;
            if (lang.isDefault) label += "（默认）";
            GUILayout.Label(label, GUILayout.Width(140f));
            string newText = EditorGUILayout.TextField(textProp != null ? textProp.stringValue : "");
            if (textProp != null && textProp.stringValue != newText)
                textProp.stringValue = newText;
            EditorGUILayout.EndHorizontal();
        }

        SerializedProperty defaultEntry = FindLocalizedEntry(listProp, defaultLanguageCode);
        SerializedProperty displayNameProp = page.SelectedMapSO.FindProperty("displayName");
        if (defaultEntry != null && displayNameProp != null)
        {
            SerializedProperty text = defaultEntry.FindPropertyRelative("text");
            displayNameProp.stringValue = text != null ? (text.stringValue ?? "") : "";
        }
    }

    private void DrawLocalizedDescriptions()
    {
        SerializedProperty listProp = page.SelectedMapSO.FindProperty("localizedDescriptions");
        if (listProp == null)
        {
            EditorGUILayout.HelpBox("找不到 localizedDescriptions。", MessageType.Warning);
            return;
        }

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        if (settings == null)
        {
            EditorGUILayout.HelpBox("未找到 LocalizationProjectSettings。", MessageType.Warning);
            return;
        }

        EnsureLocalizedEntries(listProp, settings);
        PruneLocalizedEntries(listProp, settings);

        List<LocalizationProjectSettings.LanguageEntry> orderedLanguages = GetOrderedLanguages(settings);
        string defaultLanguageCode = GetDefaultLanguageCode(settings);

        for (int i = 0; i < orderedLanguages.Count; i++)
        {
            var lang = orderedLanguages[i];
            SerializedProperty entry = FindLocalizedEntry(listProp, lang.languageCode);
            if (entry == null) continue;

            SerializedProperty textProp = entry.FindPropertyRelative("text");
            string label = string.IsNullOrWhiteSpace(lang.displayName) ? lang.languageCode : lang.displayName;
            if (lang.isDefault) label += "（默认）";

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140f));
            bool requestOpen = GUILayout.Button("打开富文本编辑器", GUILayout.Width(140f), GUILayout.Height(24f));
            EditorGUILayout.EndHorizontal();

            string previewText = textProp != null && !string.IsNullOrWhiteSpace(textProp.stringValue) ? textProp.stringValue : "（暂无描述）";
            DrawRichTextPreview(previewText);
            EditorGUILayout.EndVertical();
            GUILayout.Space(4f);

            if (requestOpen)
            {
                string openLabel = label;
                string openLang = lang.languageCode;
                string openCurrent = textProp != null ? (textProp.stringValue ?? "") : "";
                EditorApplication.delayCall += () =>
                {
                    SkyPrisonRichTextEditorWindow.Open(
                        openLabel,
                        openCurrent,
                        updated =>
                        {
                            if (page.SelectedMapSO == null)
                                return;

                            page.SelectedMapSO.Update();
                            SerializedProperty localizedList = page.SelectedMapSO.FindProperty("localizedDescriptions");
                            SerializedProperty openEntry = FindLocalizedEntry(localizedList, openLang);
                            if (openEntry != null)
                            {
                                SerializedProperty text = openEntry.FindPropertyRelative("text");
                                if (text != null) text.stringValue = updated ?? "";
                            }

                            SerializedProperty defaultEntry = FindLocalizedEntry(localizedList, defaultLanguageCode);
                            SerializedProperty descriptionProp = page.SelectedMapSO.FindProperty("description");
                            if (defaultEntry != null && descriptionProp != null)
                            {
                                SerializedProperty defaultText = defaultEntry.FindPropertyRelative("text");
                                descriptionProp.stringValue = defaultText != null ? (defaultText.stringValue ?? "") : "";
                            }

                            page.SelectedMapSO.ApplyModifiedProperties();
                            EditorUtility.SetDirty(page.SelectedMap);
                        },
                        "map");
                };
                GUIUtility.ExitGUI();
            }
        }

        SerializedProperty defaultEntryProp = FindLocalizedEntry(listProp, defaultLanguageCode);
        if (defaultEntryProp != null)
        {
            SerializedProperty defaultTextProp = defaultEntryProp.FindPropertyRelative("text");
            SerializedProperty descriptionPropSync = page.SelectedMapSO.FindProperty("description");
            string defaultTextValue = defaultTextProp != null ? defaultTextProp.stringValue ?? "" : "";
            if (descriptionPropSync != null && descriptionPropSync.stringValue != defaultTextValue)
                descriptionPropSync.stringValue = defaultTextValue;
        }
    }

    private void DrawMapBoundsSection()
    {
        EditorGUI.BeginChangeCheck();
        page.DrawRow("中心点", page.SelectedMapSO.FindProperty("mapBoundsCenter"));
        page.DrawRow("尺寸", page.SelectedMapSO.FindProperty("mapBoundsSize"));
        EditorGUILayout.Space(4f);
        page.DrawRow("启用物理边界", page.SelectedMapSO.FindProperty("enablePhysicalMapBounds"));
        page.DrawRow("边界墙厚度", page.SelectedMapSO.FindProperty("mapBoundsWallThickness"));
        page.DrawRow("边界墙高度", page.SelectedMapSO.FindProperty("mapBoundsWallHeight"));
        page.DrawRow("启用顶部封顶", page.SelectedMapSO.FindProperty("mapBoundsUseCeiling"));
        page.DrawRow("顶部厚度", page.SelectedMapSO.FindProperty("mapBoundsCeilingThickness"));
        if (EditorGUI.EndChangeCheck())
            ApplySelectedMapPropertiesNow();

        EditorGUILayout.Space(6f);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);
        if (GUILayout.Button("同步到 Scene", GUILayout.Width(170f), GUILayout.Height(24f)))
            SyncMapDefinitionToScene();

        if (GUILayout.Button("从 Scene 回读", GUILayout.Width(130f), GUILayout.Height(24f)))
        {
            SkyPrisonMapEditorUtility.PullSceneBoundsToMap(page.SelectedMap);
            page.EnsureSelectedSerializedObject();
            page.SelectedMapSO?.Update();
        }

        if (GUILayout.Button("生成 / 矫正", GUILayout.Width(170f), GUILayout.Height(24f)))
            GenerateAndCorrectCurrentScene();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);
        showMapBoundsAdvancedTools = EditorGUILayout.Foldout(showMapBoundsAdvancedTools, "高级工具", true);
        if (showMapBoundsAdvancedTools)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(140f);
                if (GUILayout.Button("删除物理边界", GUILayout.Width(160f)))
                    SkyPrisonMapPhysicalBoundsEditorUtility.ClearPhysicalBoundsInCurrentScene();

                if (GUILayout.Button("重置 Terrain 地表层为干净底色", GUILayout.Width(240f)))
                {
                    ApplySelectedMapPropertiesNow();
                    SkyPrisonMapEditorUtility.ResetGroundTerrainSurfaceToCleanDebugLayer(page.SelectedMap);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(140f);
                if (GUILayout.Button("将 WorldRoot 居中到 MapBounds", GUILayout.Width(240f)))
                    SkyPrisonMapEditorUtility.MoveWorldRootToMapBoundsCenter();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(140f);
                if (GUILayout.Button("将 WorldRoot + UnitRoot 居中到 MapBounds", GUILayout.Width(300f)))
                    SkyPrisonMapEditorUtility.MoveWorldRootAndUnitRootToMapBoundsCenter();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(140f);
                if (GUILayout.Button("清理废弃 GroundOverlay", GUILayout.Width(240f)))
                    CleanupDeprecatedGroundOverlays(false);
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.HelpBox(
            "同步：把当前地图定义写入 Scene，包括 MapBounds、战争迷雾与 Terrain 对齐。\n" +
            "生成 / 矫正：执行一次完整 Scene 修复：同步 MapBounds、矫正 Terrain、按启用状态生成/矫正物理边界，并清理已经废弃的四层 GroundOverlay / RoadLine Overlay。不会移动 WorldRoot、UnitRoot、System、相机或 Canvas，也不会重算已有摆放坐标。",
            MessageType.None);
    }

    private void SyncMapDefinitionToScene()
    {
        ApplySelectedMapPropertiesNow();
        SkyPrisonMapEditorUtility.SyncMapBoundsToScene(page.SelectedMap);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    private void GenerateAndCorrectCurrentScene()
    {
        ApplySelectedMapPropertiesNow();

        // 只做地图结构修复，不再创建 / 修复 GroundOverlay_RoadLine 或四层 Overlay。
        SkyPrisonMapEditorUtility.SyncMapBoundsToScene(page.SelectedMap);
        SkyPrisonMapEditorUtility.SyncGroundTerrainToMapBounds(page.SelectedMap);

        SerializedProperty enablePhysicalBoundsProp = page.SelectedMapSO.FindProperty("enablePhysicalMapBounds");
        bool enablePhysicalBounds = enablePhysicalBoundsProp == null || enablePhysicalBoundsProp.boolValue;
        if (enablePhysicalBounds)
            SkyPrisonMapPhysicalBoundsEditorUtility.SyncPhysicalBoundsToCurrentScene(page.SelectedMap);
        else
            SkyPrisonMapPhysicalBoundsEditorUtility.ClearPhysicalBoundsInCurrentScene();

        CleanupDeprecatedGroundOverlays(true);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[SkyPrison Map] 生成 / 矫正完成：MapBounds、Terrain、物理边界已同步；废弃 GroundOverlay 已清理。");
    }

    private void CleanupDeprecatedGroundOverlays(bool silent)
    {
        System.Type cleanupType = System.Type.GetType("SkyPrisonGroundOverlayFourLayerCleanup");
        if (cleanupType == null)
        {
            // 兜底：即使清理工具脚本没放入工程，也直接清理场景对象，避免生成 / 矫正继续留下旧 Overlay。
            int deleted = CleanupDeprecatedGroundOverlayObjectsDirectly();
            if (!silent)
                Debug.Log($"[SkyPrison Map] 已直接清理废弃 GroundOverlay 对象：{deleted}");
            return;
        }

        System.Reflection.MethodInfo method = cleanupType.GetMethod(
            "CleanupFourLayerOverlaySilently",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        if (method != null)
        {
            method.Invoke(null, null);
            return;
        }

        int fallbackDeleted = CleanupDeprecatedGroundOverlayObjectsDirectly();
        if (!silent)
            Debug.Log($"[SkyPrison Map] 已直接清理废弃 GroundOverlay 对象：{fallbackDeleted}");
    }

    private int CleanupDeprecatedGroundOverlayObjectsDirectly()
    {
        string[] names =
        {
            "GroundOverlay_Underlay",
            "GroundOverlay_Surface",
            "GroundOverlay_Marking",
            "GroundOverlay_Top",
            "GroundOverlay_RoadLine"
        };

        int deleted = 0;
        foreach (string objectName in names)
        {
            GameObject go = GameObject.Find("WorldRoot/GroundRoot/" + objectName);
            if (go == null)
                go = GameObject.Find(objectName);

            if (go == null)
                continue;

            Undo.DestroyObjectImmediate(go);
            deleted++;
        }

        return deleted;
    }

    private void DrawFogSection()
    {
        page.DrawRow("开启战争迷雾", page.SelectedMapSO.FindProperty("enableFogOfWar"));
        page.DrawRow("迷雾强度", page.SelectedMapSO.FindProperty("fogStrength"));
        page.DrawRow("边缘柔和度", page.SelectedMapSO.FindProperty("fogSoftEdgeWidth"));
    }

    private void DrawEnvironmentSection()
    {
        page.DrawRow("环境预设", page.SelectedMapSO.FindProperty("environmentPreset"));
        page.DrawRow("天空渲染模型", page.SelectedMapSO.FindProperty("skyRenderModel"));
        page.DrawRow("Skybox 材质", page.SelectedMapSO.FindProperty("skyboxMaterial"));

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("环境补光（Ambient + Area Light）", EditorStyles.miniBoldLabel);
        page.DrawRow("Ambient 颜色", page.SelectedMapSO.FindProperty("ambientColor"));
        page.DrawRow("Area 光颜色", page.SelectedMapSO.FindProperty("environmentAreaLightColor"));
        page.DrawRow("Area 光强度", page.SelectedMapSO.FindProperty("environmentAreaLightIntensity"));
        page.DrawRow("Area 光尺寸", page.SelectedMapSO.FindProperty("environmentAreaLightSize"));
        page.DrawRow("Area 光位置", page.SelectedMapSO.FindProperty("environmentAreaLightPosition"));
        page.DrawRow("Area 光角度", page.SelectedMapSO.FindProperty("environmentAreaLightEuler"));

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("主方向光", EditorStyles.miniBoldLabel);
        page.DrawRow("主光颜色", page.SelectedMapSO.FindProperty("mainLightColor"));
        page.DrawRow("主光强度", page.SelectedMapSO.FindProperty("mainLightIntensity"));
        page.DrawRow("主光角度", page.SelectedMapSO.FindProperty("mainLightEuler"));

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("场景雾 / 后处理", EditorStyles.miniBoldLabel);
        page.DrawRow("场景雾", page.SelectedMapSO.FindProperty("enableSceneFog"));
        page.DrawRow("雾颜色", page.SelectedMapSO.FindProperty("sceneFogColor"));
        page.DrawRow("雾开始距离", page.SelectedMapSO.FindProperty("fogStartDistance"));
        page.DrawRow("雾结束距离", page.SelectedMapSO.FindProperty("fogEndDistance"));
        page.DrawRow("后处理 Profile", page.SelectedMapSO.FindProperty("postProcessProfile"));
        page.DrawRow("环境特效 Prefab", page.SelectedMapSO.FindProperty("environmentFxPrefab"));

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("时间", EditorStyles.miniBoldLabel);
        page.DrawRow("昼夜交替", page.SelectedMapSO.FindProperty("enableDayNightCycle"));
        page.DrawRow("初始时间", page.SelectedMapSO.FindProperty("startTimeOfDay"));

        EditorGUILayout.Space(6f);
        DrawConstrainedHelpBox(
            "地图环境只同步默认舞台气氛：Skybox、Ambient、Area Light、主方向光、场景雾、CameraPostProcessVolume 与环境特效。不会改地形、遮挡、相机栈、单位、地图物件坐标。Point Light 不再作为环境光默认生成。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);

        if (GUILayout.Button("套用预设到表单", GUILayout.Width(130f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapEnvironmentEditorUtility.ApplyPresetDefaultValues(page.SelectedMap);
            page.EnsureSelectedSerializedObject();
            page.SelectedMapSO?.Update();
        }

        if (GUILayout.Button("检查环境结构", GUILayout.Width(130f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapEnvironmentEditorUtility.InspectEnvironmentStructureCurrentScene(page.SelectedMap);
        }

        if (GUILayout.Button("自动补齐/矫正", GUILayout.Width(130f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapEnvironmentEditorUtility.AutoFixEnvironmentStructureCurrentScene(page.SelectedMap);
        }

        if (GUILayout.Button("同步到当前 Scene", GUILayout.Width(130f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapEnvironmentEditorUtility.ApplyEnvironmentToCurrentScene(page.SelectedMap);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);

        if (GUILayout.Button("从当前 Scene 回读到页面", GUILayout.Width(190f), GUILayout.Height(24f)))
        {
            SkyPrisonMapEnvironmentEditorUtility.PullEnvironmentFromCurrentScene(page.SelectedMap);
            page.EnsureSelectedSerializedObject();
            page.SelectedMapSO?.Update();
        }

        if (GUILayout.Button("同步环境到地图 Scene", GUILayout.Width(190f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapEnvironmentEditorUtility.ApplyEnvironmentToMapScene(page.SelectedMap);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);

        if (GUILayout.Button("补齐并渲染草 Color Map", GUILayout.Width(210f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapEnvironmentEditorUtility.EnsureAndRenderGrassColorMapCurrentScene(page.SelectedMap);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawWeatherSection()
    {
        page.DrawRow("开启天气", page.SelectedMapSO.FindProperty("enableWeather"));

        SerializedProperty weatherTypeProp = page.SelectedMapSO.FindProperty("weatherType");
        page.DrawRow("天气类型", weatherTypeProp);

        // 每种天气类型的参数各自独立（DustWeatherParams/RainWeatherParams/...），
        // 只显示当前选中天气类型对应的那一组，不相关的参数不显示——避免选了扬尘还
        // 看到一个跟扬尘无关的"镜头湿润强度"。
        MapWeatherType weatherType = (MapWeatherType)weatherTypeProp.enumValueIndex;
        switch (weatherType)
        {
            case MapWeatherType.Dust:
                page.DrawRow("扬尘强度", page.SelectedMapSO.FindProperty("dustWeather.intensity"));
                break;

            case MapWeatherType.Rain:
            case MapWeatherType.HeavyRain:
                page.DrawRow("降雨强度", page.SelectedMapSO.FindProperty("rainWeather.intensity"));
                page.DrawRow("镜头湿润强度", page.SelectedMapSO.FindProperty("rainWeather.lensWetnessIntensity"));
                break;

            case MapWeatherType.Snow:
                page.DrawRow("降雪强度", page.SelectedMapSO.FindProperty("snowWeather.intensity"));
                break;

            case MapWeatherType.Fog:
                page.DrawRow("雾强度", page.SelectedMapSO.FindProperty("weatherFog.intensity"));
                break;
        }
    }


    private void DrawTriggerPackagesSection()
    {
        SerializedProperty listProp = page.SelectedMapSO.FindProperty("triggerPackages");
        if (listProp == null)
        {
            EditorGUILayout.HelpBox("MapDefinition 中没有 triggerPackages 字段。请确认 MapDefinition.cs 已更新。", MessageType.Warning);
            return;
        }

        HandleTriggerPackageObjectPicker(listProp);

        EditorGUILayout.HelpBox("这里绑定当前地图要使用的触发器包。地图运行时会从 MapDefinition 读取这些触发器包。当前阶段只做引用配置，不会自动生成触发器运行时。", MessageType.None);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("触发器包绑定表单", EditorStyles.boldLabel);
        EditorGUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ 添加触发器包", GUILayout.Width(130f), GUILayout.Height(24f)))
        {
            triggerPackagePickerControlId = TriggerPackagePickerControlId;
            triggerPackagePickerAutoAdd = true;
            triggerPackagePickerSelected = null;
            EditorGUIUtility.ShowObjectPicker<TriggerPackage>(null, false, "t:TriggerPackage", triggerPackagePickerControlId);
        }

        using (new EditorGUI.DisabledScope(listProp.arraySize == 0))
        {
            if (GUILayout.Button("清理空引用", GUILayout.Width(100f), GUILayout.Height(24f)))
                RemoveNullTriggerPackageReferences(listProp);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"已绑定 {CountValidTriggerPackages(listProp)} 个", EditorStyles.miniLabel, GUILayout.Width(90f));
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(8f);

        DrawTriggerPackageListContainer(listProp);

        EditorGUILayout.EndVertical();
    }

    private void DrawTriggerPackageListContainer(SerializedProperty listProp)
    {
        Rect outerRect = EditorGUILayout.BeginVertical("box", GUILayout.MinHeight(TriggerPackageListContainerHeight + 28f));
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(outerRect, new Color(0.09f, 0.09f, 0.10f, 0.72f));

        Rect headerRect = EditorGUILayout.GetControlRect(false, 20f);
        EditorGUI.LabelField(headerRect, "已绑定触发器包", EditorStyles.boldLabel);

        triggerPackageListScroll = EditorGUILayout.BeginScrollView(
            triggerPackageListScroll,
            GUILayout.Height(TriggerPackageListContainerHeight));

        if (listProp.arraySize == 0)
        {
            Rect emptyRect = EditorGUILayout.GetControlRect(false, TriggerPackageListContainerHeight - 8f);
            EditorGUI.HelpBox(emptyRect, "当前地图还没有绑定触发器包。点击 + 从触发器包资源中选择，或把 TriggerPackage 拖到上方表单。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            return;
        }

        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty item = listProp.GetArrayElementAtIndex(i);
            TriggerPackage package = item.objectReferenceValue as TriggerPackage;

            Rect rowRect = EditorGUILayout.BeginVertical("box");
            if (Event.current.type == EventType.Repaint)
            {
                Color rowColor = i % 2 == 0
                    ? new Color(0.13f, 0.13f, 0.14f, 0.72f)
                    : new Color(0.11f, 0.11f, 0.12f, 0.72f);
                EditorGUI.DrawRect(rowRect, rowColor);
            }

            EditorGUILayout.BeginHorizontal();

            GUILayout.Label((i + 1).ToString("00"), GUILayout.Width(24f));

            EditorGUI.BeginChangeCheck();
            UnityEngine.Object newObject = EditorGUILayout.ObjectField(
                package,
                typeof(TriggerPackage),
                false,
                GUILayout.MinWidth(120f),
                GUILayout.MaxWidth(520f),
                GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
            {
                TriggerPackage newPackage = newObject as TriggerPackage;
                if (newPackage == null || !ContainsTriggerPackage(listProp, newPackage, i))
                {
                    item.objectReferenceValue = newPackage;
                    page.SelectedMapSO.ApplyModifiedProperties();
                    EditorUtility.SetDirty(page.SelectedMap);
                }
                else
                {
                    EditorUtility.DisplayDialog("触发器包重复", "这个触发器包已经绑定在当前地图上。", "确定");
                }
            }

            using (new EditorGUI.DisabledScope(package == null))
            {
                if (GUILayout.Button("定位", GUILayout.Width(44f), GUILayout.Height(22f)))
                    FocusTriggerPackage(package);
            }

            if (GUILayout.Button("-", GUILayout.Width(22f), GUILayout.Height(22f)))
            {
                listProp.DeleteArrayElementAtIndex(i);
                page.SelectedMapSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(page.SelectedMap);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                GUI.changed = true;
                break;
            }

            GUILayout.Space(8f);
            EditorGUILayout.EndHorizontal();

            if (package != null)
            {
                EditorGUILayout.LabelField("名称", string.IsNullOrWhiteSpace(package.displayName) ? package.name : package.displayName);
                EditorGUILayout.LabelField("ID", string.IsNullOrWhiteSpace(package.triggerId) ? "-" : package.triggerId);
                string path = AssetDatabase.GetAssetPath(package);
                EditorGUILayout.LabelField("路径", ShortPath(path), EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("空引用。可以重新指定，或点击“清理空引用”。", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ── 地图BGM ───────────────────────────────────────────────────────────────
    // 跟触发器包同一套"编辑器配置→Resources注册表→运行时按场景自动生成"的模式，
    // 只是BGM不需要ScriptableObject资产，直接在这里配两份AudioClip清单——不走
    // SkyPrisonAudioPackage那套调音台，太重了。保存MapDefinition时
    // MapBGMRegistryBuilder会自动同步，不用像触发器包那样额外点"重建"。

    private void DrawBGMSection()
    {
        SerializedProperty exploreProp = page.SelectedMapSO.FindProperty("exploreBgmClips");
        SerializedProperty combatProp = page.SelectedMapSO.FindProperty("combatBgmClips");
        if (exploreProp == null || combatProp == null)
        {
            EditorGUILayout.HelpBox("MapDefinition 中没有 exploreBgmClips/combatBgmClips 字段。请确认 MapDefinition.cs 已更新。", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            "配置这张地图的BGM——探索/战斗各一份曲目清单，可以放多首随机或顺序轮播。" +
            "任意敌人看见玩家时自动切到战斗曲目，脱战后切回探索。改完之后点下面「同步到运行时" +
            "注册表」，运行时才会读到最新配置——光改完字段、不点同步的话，Unity不一定会自动" +
            "触发这一步（取决于资产是否真的走了完整的重新导入流程），保险起见改完直接点一下。",
            MessageType.None);

        EditorGUI.BeginChangeCheck();

        DrawAudioClipListBox(exploreProp, "探索曲目");
        GUILayout.Space(6f);
        DrawAudioClipListBox(combatProp, "战斗曲目（留空=进战斗维持探索曲目不切）");

        GUILayout.Space(8f);
        SerializedProperty playModeProp = page.SelectedMapSO.FindProperty("bgmPlayMode");
        SerializedProperty crossfadeProp = page.SelectedMapSO.FindProperty("bgmCrossfadeDuration");
        SerializedProperty volumeProp = page.SelectedMapSO.FindProperty("bgmVolume");
        if (playModeProp != null) EditorGUILayout.PropertyField(playModeProp, new GUIContent("播放模式"));
        if (crossfadeProp != null) EditorGUILayout.PropertyField(crossfadeProp, new GUIContent("切歌淡入淡出时长（秒）"));
        if (volumeProp != null) EditorGUILayout.PropertyField(volumeProp, new GUIContent("音量"));

        if (EditorGUI.EndChangeCheck())
            ApplySelectedMapPropertiesNow();

        GUILayout.Space(8f);
        if (GUILayout.Button("同步到运行时注册表（MapBGMRegistry）", GUILayout.Height(26f)))
        {
            AssetDatabase.SaveAssets();
            MapBGMRegistryBuilder.RebuildMenu();
        }
    }

    private const float AudioClipListMinHeight = 120f;

    private void DrawAudioClipListBox(SerializedProperty listProp, string title)
    {
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+", GUILayout.Width(24f), GUILayout.Height(20f)))
        {
            int index = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(index);
            listProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);

        // 跟触发器包列表同一套深色底框+行底色交替样式，视觉上保持一致。
        Rect outerRect = EditorGUILayout.BeginVertical("box", GUILayout.MinHeight(AudioClipListMinHeight));
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(outerRect, new Color(0.09f, 0.09f, 0.10f, 0.72f));

        if (listProp.arraySize == 0)
        {
            Rect emptyRect = EditorGUILayout.GetControlRect(false, AudioClipListMinHeight - 12f);
            EditorGUI.HelpBox(emptyRect, "还没有配曲目，点右上角「+」添加。", MessageType.Info);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
            return;
        }

        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty item = listProp.GetArrayElementAtIndex(i);

            Rect rowRect = EditorGUILayout.BeginHorizontal("box");
            if (Event.current.type == EventType.Repaint)
            {
                Color rowColor = i % 2 == 0
                    ? new Color(0.13f, 0.13f, 0.14f, 0.72f)
                    : new Color(0.11f, 0.11f, 0.12f, 0.72f);
                EditorGUI.DrawRect(rowRect, rowColor);
            }

            GUILayout.Label((i + 1).ToString("00"), GUILayout.Width(24f));
            EditorGUILayout.PropertyField(item, GUIContent.none);
            if (GUILayout.Button("-", GUILayout.Width(22f), GUILayout.Height(18f)))
            {
                listProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndVertical();
    }

    private void FocusTriggerPackage(TriggerPackage package)
    {
        if (package == null)
            return;

        EditorGUIUtility.PingObject(package);
        Selection.activeObject = package;
        SkyPrisonEditorWindow.OpenWindowWithTab("触发器", package);
    }

    private static string ShortPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "-";

        const int maxLength = 86;
        if (path.Length <= maxLength)
            return path;

        return "..." + path.Substring(path.Length - maxLength);
    }

    private void HandleTriggerPackageObjectPicker(SerializedProperty listProp)
    {
        Event e = Event.current;
        if (e == null || triggerPackagePickerControlId < 0)
            return;

        if (e.type != EventType.ExecuteCommand)
            return;

        if (EditorGUIUtility.GetObjectPickerControlID() != triggerPackagePickerControlId)
            return;

        if (e.commandName == "ObjectSelectorUpdated")
        {
            TriggerPackage selected = EditorGUIUtility.GetObjectPickerObject() as TriggerPackage;
            if (selected != null)
            {
                triggerPackagePickerSelected = selected;
                triggerPackageToAdd = selected;
                GUI.changed = true;
            }

            e.Use();
            return;
        }

        if (e.commandName == "ObjectSelectorClosed")
        {
            TriggerPackage selected = EditorGUIUtility.GetObjectPickerObject() as TriggerPackage;
            if (selected == null)
                selected = triggerPackagePickerSelected;

            if (selected != null)
            {
                triggerPackageToAdd = selected;

                if (triggerPackagePickerAutoAdd)
                {
                    AddTriggerPackageReference(listProp, selected);
                    triggerPackageToAdd = null;
                }
            }

            triggerPackagePickerControlId = -1;
            triggerPackagePickerAutoAdd = false;
            triggerPackagePickerSelected = null;
            e.Use();
        }
    }

    private void AddTriggerPackageReference(SerializedProperty listProp, TriggerPackage package)
    {
        if (package == null || page.SelectedMapSO == null || page.SelectedMap == null)
            return;

        page.SelectedMapSO.Update();
        listProp = page.SelectedMapSO.FindProperty("triggerPackages");
        if (listProp == null)
            return;

        if (ContainsTriggerPackage(listProp, package, -1))
        {
            EditorUtility.DisplayDialog("触发器包重复", "这个触发器包已经绑定在当前地图上。", "确定");
            return;
        }

        int index = listProp.arraySize;
        listProp.InsertArrayElementAtIndex(index);
        SerializedProperty item = listProp.GetArrayElementAtIndex(index);
        item.objectReferenceValue = package;

        page.SelectedMapSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(page.SelectedMap);
        AssetDatabase.SaveAssets();
        GUI.changed = true;
    }

    private bool ContainsTriggerPackage(SerializedProperty listProp, TriggerPackage package, int ignoreIndex)
    {
        if (listProp == null || package == null)
            return false;

        for (int i = 0; i < listProp.arraySize; i++)
        {
            if (i == ignoreIndex)
                continue;

            SerializedProperty item = listProp.GetArrayElementAtIndex(i);
            if (item != null && item.objectReferenceValue == package)
                return true;
        }

        return false;
    }

    private int CountValidTriggerPackages(SerializedProperty listProp)
    {
        if (listProp == null)
            return 0;

        int count = 0;
        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty item = listProp.GetArrayElementAtIndex(i);
            if (item != null && item.objectReferenceValue != null)
                count++;
        }
        return count;
    }

    private void RemoveNullTriggerPackageReferences(SerializedProperty listProp)
    {
        if (listProp == null)
            return;

        for (int i = listProp.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty item = listProp.GetArrayElementAtIndex(i);
            if (item == null || item.objectReferenceValue == null)
                listProp.DeleteArrayElementAtIndex(i);
        }

        page.SelectedMapSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(page.SelectedMap);
        GUI.changed = true;
    }

    private void DrawCameraSection()
    {
        page.DrawRow("开启景深", page.SelectedMapSO.FindProperty("enableDepthOfField"));
        page.DrawRow("焦点距离", page.SelectedMapSO.FindProperty("focusDistance"));
        page.DrawRow("模糊强度", page.SelectedMapSO.FindProperty("blurStrength"));

        EditorGUILayout.Space(6f);
        DrawConstrainedHelpBox(
            "景深结构采用解耦规则：Main Camera / Base 世界相机负责地图后处理，GamePlayCamera / OverheadUICamera 等 Overlay 相机只负责角色、迷雾、UI叠加。自动补齐不会重写原相机栈，只会矫正 Base 相机的 Post Processing、Depth Texture、CameraPostProcessVolume 与景深控制器。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);

        if (GUILayout.Button("检查景深结构", GUILayout.Width(130f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapDepthOfFieldEditorUtility.InspectDepthOfFieldStructureCurrentScene(page.SelectedMap);
        }

        if (GUILayout.Button("自动补齐/矫正", GUILayout.Width(130f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapDepthOfFieldEditorUtility.AutoFixDepthOfFieldStructureCurrentScene(page.SelectedMap);
        }

        if (GUILayout.Button("同步景深", GUILayout.Width(110f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapDepthOfFieldEditorUtility.ApplyDepthOfFieldToCurrentScene(page.SelectedMap);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);

        if (GUILayout.Button("同步景深到地图 Scene", GUILayout.Width(190f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapDepthOfFieldEditorUtility.ApplyDepthOfFieldToMapScene(page.SelectedMap);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);
        DrawConstrainedHelpBox(
            "如果 URP 原生景深在正交相机下不明显，请使用 2.5D 远景虚化。它会作为 UniversalRenderer3D 的 Renderer Feature 运行，只模糊 Base 世界相机结果，不会模糊 Overlay 角色/UI。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);

        if (GUILayout.Button("检查2.5D远景虚化", GUILayout.Width(150f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonOrthographicDistanceBlurEditorUtility.InspectCurrentScene(page.SelectedMap);
        }

        if (GUILayout.Button("自动补齐2.5D远景虚化", GUILayout.Width(180f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonOrthographicDistanceBlurEditorUtility.AutoFixCurrentScene(page.SelectedMap);
        }

        if (GUILayout.Button("同步2.5D远景虚化", GUILayout.Width(150f), GUILayout.Height(24f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonOrthographicDistanceBlurEditorUtility.SyncCurrentScene(page.SelectedMap);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSceneBindingSection()
    {
        SerializedProperty pathProp = page.SelectedMapSO.FindProperty("scenePath");
        SerializedProperty guidProp = page.SelectedMapSO.FindProperty("sceneGuid");

        string resolvedScenePath = SkyPrisonMapEditorUtility.ResolveMapScenePath(page.SelectedMap, true);
        if (!string.IsNullOrWhiteSpace(resolvedScenePath))
        {
            if (pathProp != null) pathProp.stringValue = resolvedScenePath;
            if (guidProp != null) guidProp.stringValue = AssetDatabase.AssetPathToGUID(resolvedScenePath);
        }

        SceneAsset currentSceneAsset = null;
        if (pathProp != null && !string.IsNullOrWhiteSpace(pathProp.stringValue))
            currentSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(pathProp.stringValue);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("主 Scene", GUILayout.Width(140f));
        SceneAsset newSceneAsset = (SceneAsset)EditorGUILayout.ObjectField(currentSceneAsset, typeof(SceneAsset), false);
        EditorGUILayout.EndHorizontal();

        if (newSceneAsset != currentSceneAsset)
        {
            if (newSceneAsset == null)
            {
                if (pathProp != null) pathProp.stringValue = "";
                if (guidProp != null) guidProp.stringValue = "";
            }
            else
            {
                string newPath = AssetDatabase.GetAssetPath(newSceneAsset);
                if (pathProp != null) pathProp.stringValue = newPath;
                if (guidProp != null) guidProp.stringValue = AssetDatabase.AssetPathToGUID(newPath);
            }
        }

        page.DrawReadonlyRow("Scene 路径", pathProp != null ? pathProp.stringValue : "");
        page.DrawReadonlyRow("Scene GUID", guidProp != null ? guidProp.stringValue : "");

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);

        if (GUILayout.Button("绑定当前打开 Scene", GUILayout.Width(140f)))
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid() && !string.IsNullOrWhiteSpace(scene.path))
            {
                if (pathProp != null) pathProp.stringValue = scene.path;
                if (guidProp != null) guidProp.stringValue = AssetDatabase.AssetPathToGUID(scene.path);
            }
        }

        using (new EditorGUI.DisabledScope(currentSceneAsset == null))
        {
            if (GUILayout.Button("在 Project 中定位", GUILayout.Width(120f)))
            {
                if (currentSceneAsset != null)
                {
                    Selection.activeObject = currentSceneAsset;
                    EditorGUIUtility.PingObject(currentSceneAsset);
                }
            }
        }

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(pathProp != null ? pathProp.stringValue : "")))
        {
            if (GUILayout.Button("打开地图", GUILayout.Width(100f)))
                SkyPrisonMapEditorUtility.OpenMapScene(page.SelectedMap);
        }

        if (GUILayout.Button("清空绑定", GUILayout.Width(90f)))
        {
            if (pathProp != null) pathProp.stringValue = "";
            if (guidProp != null) guidProp.stringValue = "";
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawBootstrapSection()
    {
        EditorGUILayout.HelpBox("校对当前 Scene 的基础节点骨架；已有节点不会重建，只会补齐缺失节点、组件、战争迷雾相机层设置，并让 GroundRoot/GroundTerrain 对齐覆盖 MapBounds，同时同步地图定义里的景深、环境设置与草地 Color Map Renderer 节点。", MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);
        if (GUILayout.Button("校对并补齐基础节点（当前 Scene）", GUILayout.Width(240f)))
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid())
                SkyPrisonMapEditorUtility.EnsureDefaultSceneHierarchy(page.SelectedMap, scene);
        }

        if (GUILayout.Button("补齐并渲染草 Color Map", GUILayout.Width(210f)))
        {
            ApplySelectedMapPropertiesNow();
            SkyPrisonMapEnvironmentEditorUtility.EnsureAndRenderGrassColorMapCurrentScene(page.SelectedMap);
        }
        EditorGUILayout.EndHorizontal();
    }

    private string GetBestDisplayName(MapDefinition map)
    {
        if (map == null) return "未命名地图";
        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        string defaultLanguageCode = GetDefaultLanguageCode(settings);
        string localized = GetLocalizedText(map.localizedNames, defaultLanguageCode);
        if (!string.IsNullOrWhiteSpace(localized))
            return localized;
        if (!string.IsNullOrWhiteSpace(map.displayName))
            return map.displayName;
        return map.name;
    }

    private string GetPrimaryDescription(MapDefinition map)
    {
        if (map == null) return "";
        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        string defaultLanguageCode = GetDefaultLanguageCode(settings);
        string localized = GetLocalizedText(map.localizedDescriptions, defaultLanguageCode);
        if (!string.IsNullOrWhiteSpace(localized))
            return localized;
        return map.description ?? "";
    }

    private string GetLocalizedText(List<LocalizedTextEntry> list, string languageCode)
    {
        if (list == null || string.IsNullOrWhiteSpace(languageCode))
            return "";
        for (int i = 0; i < list.Count; i++)
        {
            LocalizedTextEntry entry = list[i];
            if (entry != null && entry.languageCode == languageCode)
                return entry.text ?? "";
        }
        return "";
    }

    private string GetDefaultLanguageCode(LocalizationProjectSettings settings)
    {
        if (settings == null || settings.languages == null || settings.languages.Count == 0)
            return "zh-CN";

        for (int i = 0; i < settings.languages.Count; i++)
        {
            var lang = settings.languages[i];
            if (lang != null && lang.enabled && lang.isDefault)
                return lang.languageCode;
        }

        for (int i = 0; i < settings.languages.Count; i++)
        {
            var lang = settings.languages[i];
            if (lang != null && lang.enabled)
                return lang.languageCode;
        }

        return "zh-CN";
    }

    private List<LocalizationProjectSettings.LanguageEntry> GetOrderedLanguages(LocalizationProjectSettings settings)
    {
        List<LocalizationProjectSettings.LanguageEntry> result = new List<LocalizationProjectSettings.LanguageEntry>();
        if (settings == null || settings.languages == null)
            return result;

        LocalizationProjectSettings.LanguageEntry defaultLang = null;
        for (int i = 0; i < settings.languages.Count; i++)
        {
            var lang = settings.languages[i];
            if (lang == null || !lang.enabled) continue;
            if (lang.isDefault) defaultLang = lang;
        }

        if (defaultLang != null)
            result.Add(defaultLang);

        for (int i = 0; i < settings.languages.Count; i++)
        {
            var lang = settings.languages[i];
            if (lang == null || !lang.enabled) continue;
            if (defaultLang != null && lang.languageCode == defaultLang.languageCode) continue;
            result.Add(lang);
        }

        return result;
    }

    private void EnsureLocalizedEntries(SerializedProperty listProp, LocalizationProjectSettings settings)
    {
        List<LocalizationProjectSettings.LanguageEntry> ordered = GetOrderedLanguages(settings);
        for (int i = 0; i < ordered.Count; i++)
        {
            string code = ordered[i].languageCode;
            if (FindLocalizedEntry(listProp, code) != null)
                continue;

            int index = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(index);
            SerializedProperty entry = listProp.GetArrayElementAtIndex(index);
            SerializedProperty codeProp = entry.FindPropertyRelative("languageCode");
            SerializedProperty textProp = entry.FindPropertyRelative("text");
            if (codeProp != null) codeProp.stringValue = code;
            if (textProp != null) textProp.stringValue = "";
        }
    }

    private void PruneLocalizedEntries(SerializedProperty listProp, LocalizationProjectSettings settings)
    {
        HashSet<string> valid = new HashSet<string>(GetOrderedLanguages(settings).ConvertAll(x => x.languageCode));
        for (int i = listProp.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty entry = listProp.GetArrayElementAtIndex(i);
            SerializedProperty codeProp = entry.FindPropertyRelative("languageCode");
            string code = codeProp != null ? codeProp.stringValue : "";
            if (!valid.Contains(code))
                listProp.DeleteArrayElementAtIndex(i);
        }
    }

    private SerializedProperty FindLocalizedEntry(SerializedProperty listProp, string languageCode)
    {
        if (listProp == null) return null;
        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty entry = listProp.GetArrayElementAtIndex(i);
            SerializedProperty codeProp = entry.FindPropertyRelative("languageCode");
            if (codeProp != null && codeProp.stringValue == languageCode)
                return entry;
        }
        return null;
    }

    private void SyncLocalizedBackfields(SerializedObject so, MapDefinition map)
    {
        if (so == null || map == null)
            return;

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        if (settings == null)
            return;

        SerializedProperty namesProp = so.FindProperty("localizedNames");
        SerializedProperty descriptionsProp = so.FindProperty("localizedDescriptions");
        if (namesProp == null || descriptionsProp == null)
            return;

        EnsureLocalizedEntries(namesProp, settings);
        EnsureLocalizedEntries(descriptionsProp, settings);
        PruneLocalizedEntries(namesProp, settings);
        PruneLocalizedEntries(descriptionsProp, settings);

        string defaultLanguageCode = GetDefaultLanguageCode(settings);
        SerializedProperty defaultNameEntry = FindLocalizedEntry(namesProp, defaultLanguageCode);
        SerializedProperty defaultDescriptionEntry = FindLocalizedEntry(descriptionsProp, defaultLanguageCode);
        SerializedProperty displayNameProp = so.FindProperty("displayName");
        SerializedProperty descriptionProp = so.FindProperty("description");

        if (displayNameProp != null && defaultNameEntry != null)
        {
            SerializedProperty textProp = defaultNameEntry.FindPropertyRelative("text");
            displayNameProp.stringValue = textProp != null ? (textProp.stringValue ?? "") : "";
        }

        if (descriptionProp != null && defaultDescriptionEntry != null)
        {
            SerializedProperty textProp = defaultDescriptionEntry.FindPropertyRelative("text");
            descriptionProp.stringValue = textProp != null ? (textProp.stringValue ?? "") : "";
        }
    }

    private string GetPackageFileName(MapDefinition map)
    {
        if (map == null)
            return "";

        if (!string.IsNullOrWhiteSpace(map.fileName))
            return map.fileName;

        string assetPath = AssetDatabase.GetAssetPath(map).Replace("\\", "/");
        if (!string.IsNullOrWhiteSpace(assetPath))
        {
            string folder = System.IO.Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(folder))
                return System.IO.Path.GetFileName(folder);
        }

        return map.name != null && map.name.StartsWith("MD_") ? map.name.Substring(3) : map.name;
    }

    private void DrawEditableFileNameRow()
    {
        MapDefinition map = page.SelectedMap;
        if (map == null || page.SelectedMapSO == null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(map).Replace("\\", "/");
        string currentFileName = GetPackageFileName(map);
        if (editingFileNameAssetPath != assetPath)
        {
            editingFileNameAssetPath = assetPath;
            editingFileName = currentFileName;
        }

        Rect row = EditorGUILayout.GetControlRect(false, 24f);
        Rect labelRect = new Rect(row.x, row.y + 2f, 140f, EditorGUIUtility.singleLineHeight);
        Rect buttonRect = new Rect(row.xMax - 92f, row.y, 92f, 22f);
        Rect valueRect = new Rect(labelRect.xMax + 2f, row.y, Mathf.Max(10f, row.width - 142f - 98f), 22f);

        EditorGUI.LabelField(labelRect, "文件名称");
        editingFileName = EditorGUI.TextField(valueRect, editingFileName ?? "");

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(editingFileName) || editingFileName == currentFileName))
        {
            if (GUI.Button(buttonRect, "应用重命名"))
            {
                page.SelectedMapSO.ApplyModifiedProperties();
                bool ok = SkyPrisonMapEditorUtility.RenameMapPackage(map, editingFileName);
                if (ok)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    string newPath = AssetDatabase.GetAssetPath(map);
                    page.Refresh();
                    MapDefinition reloaded = !string.IsNullOrWhiteSpace(newPath)
                        ? AssetDatabase.LoadAssetAtPath<MapDefinition>(newPath)
                        : null;
                    if (reloaded != null)
                        page.SelectMap(reloaded);
                    editingFileNameAssetPath = AssetDatabase.GetAssetPath(page.SelectedMap).Replace("\\", "/");
                    editingFileName = GetPackageFileName(page.SelectedMap);
                }
            }
        }
    }

    private void DrawReadonlyMultiline(string label, string value)
    {
        Rect row = EditorGUILayout.GetControlRect(false, 54f);
        Rect labelRect = new Rect(row.x, row.y, 140f, row.height);
        Rect valueRect = new Rect(labelRect.xMax + 2f, row.y, Mathf.Max(10f, row.width - 142f), row.height);

        EditorGUI.LabelField(labelRect, label);
        EditorGUI.SelectableLabel(valueRect, string.IsNullOrWhiteSpace(value) ? "-" : value, EditorStyles.label);
    }

    private void DrawClippedReadonlyRow(string label, string value)
    {
        Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        Rect labelRect = new Rect(row.x, row.y, 140f, row.height);
        Rect valueRect = new Rect(labelRect.xMax + 2f, row.y, Mathf.Max(10f, row.width - 142f), row.height);

        EditorGUI.LabelField(labelRect, label);
        GUIStyle clipped = new GUIStyle(EditorStyles.label)
        {
            clipping = TextClipping.Clip,
            wordWrap = false
        };
        EditorGUI.SelectableLabel(valueRect, string.IsNullOrWhiteSpace(value) ? "-" : value, clipped);
    }

    private void DrawConstrainedMultilineProperty(string label, SerializedProperty property, float height)
    {
        Rect row = EditorGUILayout.GetControlRect(false, height);
        Rect labelRect = new Rect(row.x, row.y + 2f, 140f, EditorGUIUtility.singleLineHeight);
        Rect valueRect = new Rect(labelRect.xMax + 2f, row.y, Mathf.Max(10f, row.width - 142f), row.height);

        EditorGUI.LabelField(labelRect, label);
        if (property == null)
        {
            EditorGUI.LabelField(valueRect, "字段不存在");
        }
        else
        {
            EditorGUI.PropertyField(valueRect, property, GUIContent.none, true);
        }
    }

    private void DrawConstrainedHelpBox(string message, MessageType type)
    {
        Rect row = EditorGUILayout.GetControlRect(false, 22f);
        EditorGUI.HelpBox(row, message, type);
    }

    private void DrawRichTextPreview(string text)
    {
        GUIStyle style = new GUIStyle(EditorStyles.helpBox);
        style.richText = true;
        style.wordWrap = true;
        EditorGUILayout.LabelField(text, style, GUILayout.MinHeight(54f));
    }
    private const int MapPageGroundPhysicsLayerIndex = 21;
    private const string MapPageGroundPhysicsLayerName = "GroundPhysics";
    private const string MapPageGroundPhysicsForceVersion = "2026-05-11-MapPage-GroundPhysics-BIND-HARD-01";

    private int ForceGroundPhysicsLayer21FromMapPage()
    {
        EnsureMapPageGroundPhysicsLayer21();

        HashSet<GameObject> targets = new HashSet<GameObject>();

        GameObject worldRoot = GameObject.Find("WorldRoot");
        if (worldRoot != null)
        {
            Transform known = worldRoot.transform.Find("GroundRoot/GroundBlock_01/GroundPhysics");
            if (known != null && known.gameObject != null)
                targets.Add(known.gameObject);
        }

        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject obj = allObjects[i];
            if (!IsMapPageEditableLoadedSceneObject(obj))
                continue;

            if (!IsMapPageGroundPhysicsName(obj.name))
                continue;

            targets.Add(obj);
        }

        int changedCount = 0;
        foreach (GameObject target in targets)
            changedCount += ForceMapPageLayerRecursive(target);

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid())
            EditorSceneManager.MarkSceneDirty(scene);

        Debug.LogError(
            $"[SkyPrisonMapInspectorPanel] GroundPhysics 页面按钮硬校正完成：Layer 21 = {LayerMask.LayerToName(MapPageGroundPhysicsLayerIndex)}，命中节点数={targets.Count}，实际修改对象数={changedCount}，Version={MapPageGroundPhysicsForceVersion}");

        return changedCount;
    }

    private void EnsureMapPageGroundPhysicsLayer21()
    {
        UnityEngine.Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagerAssets == null || tagManagerAssets.Length == 0 || tagManagerAssets[0] == null)
        {
            Debug.LogError($"[SkyPrisonMapInspectorPanel] 找不到 ProjectSettings/TagManager.asset，无法强制 Layer 21 = GroundPhysics。Version={MapPageGroundPhysicsForceVersion}");
            return;
        }

        SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        if (layers == null || layers.arraySize <= MapPageGroundPhysicsLayerIndex)
        {
            Debug.LogError($"[SkyPrisonMapInspectorPanel] TagManager layers 不存在或 Layer 21 不可用。Version={MapPageGroundPhysicsForceVersion}");
            return;
        }

        for (int i = 0; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (layer == null)
                continue;

            if (i != MapPageGroundPhysicsLayerIndex && layer.stringValue == MapPageGroundPhysicsLayerName)
                layer.stringValue = string.Empty;
        }

        SerializedProperty targetLayer = layers.GetArrayElementAtIndex(MapPageGroundPhysicsLayerIndex);
        if (targetLayer != null)
            targetLayer.stringValue = MapPageGroundPhysicsLayerName;

        tagManager.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }

    private int ForceMapPageLayerRecursive(GameObject root)
    {
        if (root == null)
            return 0;

        int changedCount = 0;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.gameObject == null)
                continue;

            Undo.RecordObject(t.gameObject, "Force GroundPhysics Layer 21 From Map Page");

            if (t.gameObject.layer != MapPageGroundPhysicsLayerIndex)
            {
                t.gameObject.layer = MapPageGroundPhysicsLayerIndex;
                changedCount++;
            }

            EditorUtility.SetDirty(t.gameObject);
            if (t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded)
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }

        return changedCount;
    }

    private bool IsMapPageEditableLoadedSceneObject(GameObject obj)
    {
        if (obj == null)
            return false;

        if (EditorUtility.IsPersistent(obj))
            return false;

        if (!obj.scene.IsValid() || !obj.scene.isLoaded)
            return false;

        return true;
    }

    private bool IsMapPageGroundPhysicsName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string normalized = name.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        return normalized.Contains("groundphysics") || normalized.Contains("groundphyscis");
    }


}
