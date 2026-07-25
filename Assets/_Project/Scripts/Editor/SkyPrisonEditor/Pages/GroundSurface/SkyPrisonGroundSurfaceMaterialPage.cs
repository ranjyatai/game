using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonGroundSurfaceMaterialPage : SkyPrisonEditorPageBase
{
    public const string PageName = "地表材质";
    public const string IconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_46.png";

    private const string StandardFolder = "Assets/_Project/Data/Definitions/Standard/GroundSurfaceMaterials";
    private const string CustomFolder = "Assets/_Project/Data/Definitions/Custom/GroundSurfaceMaterials";

    private readonly List<GroundSurfaceMaterialDefinition> definitions = new List<GroundSurfaceMaterialDefinition>();
    private readonly Dictionary<string, bool> categoryFoldouts = new Dictionary<string, bool>();
    private readonly Dictionary<string, bool> sectionFoldouts = new Dictionary<string, bool>
    {
        { "基础信息", true },
        { "素材用途 / 纹理", true },
        { "颜色合成", true },
        { "地面标签 / 规则", true },
        { "音声合成", true },
        { "特效", true },
        { "备注", false },
    };

    private GroundSurfaceMaterialDefinition selectedDefinition;
    private SerializedObject selectedSO;
    private Vector2 leftScroll;
    private Vector2 rightScroll;
    private string search = "";

    private bool pendingSurfaceDirtyMark;
    private GroundSurfaceMaterialDefinition pendingDirtyDefinition;
    private double lastSurfaceDefinitionChangeTime;
    private double suppressSurfaceDirtyMarkUntil;

    private const double SurfaceDirtyMarkDelaySeconds = 0.35d;
    private const double SurfaceDirtyMarkInteractiveHoldSeconds = 0.25d;

    private static GroundSurfaceMaterialDefinition clipboardDefinition;
    private static string clipboardDefinitionName = "";

    private readonly Color accentMagenta = new Color(1.00f, 0.16f, 0.82f, 1f);
    private readonly Color selectedBg = new Color(0.30f, 0.10f, 0.25f, 1f);
    private readonly Color leftBg = new Color(0.13f, 0.13f, 0.14f, 1f);

    private readonly struct RuntimeLayerOption
    {
        public readonly string key;
        public readonly string label;

        public RuntimeLayerOption(string key, string label)
        {
            this.key = key;
            this.label = label;
        }
    }

    private static readonly RuntimeLayerOption[] RuntimeLayerOptions =
    {
        new RuntimeLayerOption("", "未指定"),
        new RuntimeLayerOption("base_impact", "基础冲击 / base_impact"),
        new RuntimeLayerOption("shoe_soft", "软鞋底 / shoe_soft"),
        new RuntimeLayerOption("shoe_metal", "金属鞋跟 / shoe_metal"),
        new RuntimeLayerOption("surface_stone", "石质地面 / surface_stone"),
        new RuntimeLayerOption("surface_metal", "金属地面 / surface_metal"),
        new RuntimeLayerOption("surface_wood", "木质地面 / surface_wood"),
        new RuntimeLayerOption("surface_grass", "草地摩擦 / surface_grass"),
        new RuntimeLayerOption("surface_water", "浅水水花 / surface_water"),
        new RuntimeLayerOption("surface_sand", "沙地颗粒 / surface_sand"),
        new RuntimeLayerOption("surface_mud", "泥地黏滞 / surface_mud"),
        new RuntimeLayerOption("gear_jingle", "装备轻响 / gear_jingle"),
        new RuntimeLayerOption("heavy_low_end", "重物低频 / heavy_low_end"),
        new RuntimeLayerOption("mechanical_servo", "机械伺服 / mechanical_servo"),
        new RuntimeLayerOption("cloth_rustle", "布料摩擦 / cloth_rustle"),
        new RuntimeLayerOption("custom", "自定义 / custom"),
    };

    public SkyPrisonGroundSurfaceMaterialPage(SkyPrisonEditorContext context) : base(context) { }

    public override string TabName => PageName;

    public override void OnEnable()
    {
        Refresh();
    }

    public void OnDisable()
    {
        EditorApplication.update -= HandleDeferredSurfaceDirtyMark;
        pendingSurfaceDirtyMark = false;
        pendingDirtyDefinition = null;
    }

    public override void Refresh()
    {
        string selectedPath = selectedDefinition != null ? AssetDatabase.GetAssetPath(selectedDefinition) : "";
        definitions.Clear();

        string[] guids = AssetDatabase.FindAssets("t:GroundSurfaceMaterialDefinition");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GroundSurfaceMaterialDefinition def = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(path);
            if (def != null)
                definitions.Add(def);
        }

        definitions.Sort((a, b) =>
        {
            int c = string.Compare(GetCategoryLabel(a), GetCategoryLabel(b), System.StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return string.Compare(GetDisplayName(a), GetDisplayName(b), System.StringComparison.OrdinalIgnoreCase);
        });

        selectedDefinition = null;
        selectedSO = null;
        if (!string.IsNullOrEmpty(selectedPath))
        {
            GroundSurfaceMaterialDefinition matched = definitions.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == selectedPath);
            if (matched != null)
                SelectDefinition(matched);
        }

        if (selectedDefinition == null && definitions.Count > 0)
            SelectDefinition(definitions[0]);
    }

    public override void OnGUILeft()
    {
        EditorGUILayout.LabelField("地表材质", EditorStyles.boldLabel);
        GUILayout.Space(6f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+", GUILayout.Width(28f), GUILayout.Height(22f)))
            ShowCreateDefinitionMenu();

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
        HandleLeftListKeyboardShortcuts();
        EditorGUILayout.LabelField("Ctrl/Cmd+C 复制  V 粘贴  X 剪切  D 副本  Delete 删除", EditorStyles.miniLabel);
        GUILayout.Space(4f);

        Rect rect = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, leftBg);

        Rect viewRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f);
        List<GroundSurfaceMaterialDefinition> filtered = GetFilteredDefinitions();
        Dictionary<string, List<GroundSurfaceMaterialDefinition>> groups = filtered
            .GroupBy(GetCategoryLabel)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.ToList());

        float contentHeight = Mathf.Max(viewRect.height, groups.Sum(g => 24f + g.Value.Count * 48f) + 12f);
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

            foreach (GroundSurfaceMaterialDefinition def in group.Value)
            {
                Rect row = new Rect(0f, y, contentRect.width, 46f);
                DrawDefinitionRow(row, def);
                y += 48f;
            }
        }

        GUI.EndScrollView();
    }

    public override void OnGUIRight()
    {
        if (selectedDefinition == null)
        {
            EditorGUILayout.HelpBox("请先创建或选择一个地表材质定义。", MessageType.Info);
            return;
        }

        if (selectedSO == null || selectedSO.targetObject != selectedDefinition)
            selectedSO = new SerializedObject(selectedDefinition);

        selectedSO.Update();

        DrawHeader();
        GUILayout.Space(8f);

        EditorGUI.BeginChangeCheck();

        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);
        DrawSection("基础信息", DrawBasicInfo);
        DrawSection("素材用途 / 纹理", DrawVisualInfo);
        DrawSection("颜色合成", DrawColorComposition);
        DrawSection("地面标签 / 规则", DrawGroundRule);
        DrawSection("音声合成", DrawAudioSynth);
        DrawSection("特效", DrawEffects);
        DrawSection("备注", DrawNote);
        EditorGUILayout.EndScrollView();

        bool definitionChanged = EditorGUI.EndChangeCheck();
        bool serializedChanged = selectedSO.ApplyModifiedProperties();
        if (definitionChanged || serializedChanged)
        {
            EditorUtility.SetDirty(selectedDefinition);
            ScheduleSurfaceRuntimeDirtyMark(selectedDefinition);
            SkyPrisonGroundSurfaceMaterialSyncBridge.SyncAfterGroundSurfaceRefresh();
        }

        CaptureCurrentEditInteractionForRefreshThrottle();
    }

    private void CaptureCurrentEditInteractionForRefreshThrottle()
    {
        if (IsUserEditingDefinitionFieldNow())
            suppressSurfaceDirtyMarkUntil = EditorApplication.timeSinceStartup + SurfaceDirtyMarkInteractiveHoldSeconds;
    }

    private bool IsUserEditingDefinitionFieldNow()
    {
        return EditorGUIUtility.editingTextField
            || GUIUtility.hotControl != 0
            || GUIUtility.keyboardControl != 0
            || IsUnityColorPickerOpen();
    }

    private bool IsUnityColorPickerOpen()
    {
        EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
        for (int i = 0; i < windows.Length; i++)
        {
            EditorWindow window = windows[i];
            if (window == null)
                continue;

            string typeName = window.GetType().Name;
            if (!string.IsNullOrEmpty(typeName) && typeName.Contains("ColorPicker"))
                return true;
        }

        return false;
    }

    private void ScheduleSurfaceRuntimeDirtyMark(GroundSurfaceMaterialDefinition changedDefinition)
    {
        if (changedDefinition == null)
            return;

        pendingDirtyDefinition = changedDefinition;
        pendingSurfaceDirtyMark = true;
        lastSurfaceDefinitionChangeTime = EditorApplication.timeSinceStartup;

        EditorApplication.update -= HandleDeferredSurfaceDirtyMark;
        EditorApplication.update += HandleDeferredSurfaceDirtyMark;
    }

    private void HandleDeferredSurfaceDirtyMark()
    {
        if (!pendingSurfaceDirtyMark || pendingDirtyDefinition == null)
        {
            EditorApplication.update -= HandleDeferredSurfaceDirtyMark;
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        if (now - lastSurfaceDefinitionChangeTime < SurfaceDirtyMarkDelaySeconds)
            return;

        if (now < suppressSurfaceDirtyMarkUntil)
            return;

        if (IsUnityColorPickerOpen())
        {
            suppressSurfaceDirtyMarkUntil = now + SurfaceDirtyMarkInteractiveHoldSeconds;
            return;
        }

        GroundSurfaceMaterialDefinition definition = pendingDirtyDefinition;
        pendingSurfaceDirtyMark = false;
        pendingDirtyDefinition = null;
        EditorApplication.update -= HandleDeferredSurfaceDirtyMark;

        MarkGroundBlocksRuntimeBakeDirty(definition);
    }

    private void MarkGroundBlocksRuntimeBakeDirty(GroundSurfaceMaterialDefinition changedDefinition)
    {
        if (changedDefinition == null)
            return;

        BaseGroundBlock[] blocks = Resources.FindObjectsOfTypeAll<BaseGroundBlock>();
        for (int i = 0; i < blocks.Length; i++)
        {
            BaseGroundBlock block = blocks[i];
            if (block == null)
                continue;

            bool usesMaterial = block.defaultSurfaceMaterial == changedDefinition;
            if (!usesMaterial && block.surfaceMaterialPalette != null)
                usesMaterial = block.surfaceMaterialPalette.Contains(changedDefinition);

            if (!usesMaterial)
                continue;

            // 只标记运行贴图过期，不在属性页里重建整张地面贴图。
            // 编辑期保持当前模拟/预览显示，正式烘焙统一交给进入 Play Mode 前或手动“烘焙运行地面”。
            block.MarkGroundDataDirty(false);
            EditorUtility.SetDirty(block);
        }

        SceneView.RepaintAll();
    }

    private void HandleLeftListKeyboardShortcuts()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown)
            return;

        if (EditorGUIUtility.editingTextField)
            return;

        bool command = e.control || e.command;
        bool handled = false;

        if (command && e.keyCode == KeyCode.C)
        {
            CopyDefinitionToClipboard(selectedDefinition);
            handled = true;
        }
        else if (command && e.keyCode == KeyCode.X)
        {
            CutSelectedDefinitionToClipboard();
            handled = true;
        }
        else if (command && e.keyCode == KeyCode.V)
        {
            PasteDefinitionFromClipboard();
            handled = true;
        }
        else if (command && e.keyCode == KeyCode.D)
        {
            DuplicateSelectedDefinition();
            handled = true;
        }
        else if (!command && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
        {
            DeleteSelectedDefinition();
            handled = true;
        }

        if (handled)
        {
            GUI.changed = true;
            e.Use();
        }
    }

    private void ShowDefinitionContextMenu(GroundSurfaceMaterialDefinition def)
    {
        GenericMenu menu = new GenericMenu();

        if (def != null)
        {
            menu.AddItem(new GUIContent("复制"), false, () => CopyDefinitionToClipboard(def));
            menu.AddItem(new GUIContent("创建副本"), false, () => DuplicateDefinition(def));

            if (def.isStandard)
            {
                menu.AddDisabledItem(new GUIContent("剪切（标准资源不可剪切）"));
                menu.AddDisabledItem(new GUIContent("删除（标准资源不可删除）"));
            }
            else
            {
                menu.AddItem(new GUIContent("剪切"), false, () => CutDefinitionToClipboard(def));
                menu.AddItem(new GUIContent("删除"), false, () =>
                {
                    SelectDefinition(def);
                    DeleteSelectedDefinition();
                });
            }
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("复制"));
            menu.AddDisabledItem(new GUIContent("创建副本"));
            menu.AddDisabledItem(new GUIContent("剪切"));
            menu.AddDisabledItem(new GUIContent("删除"));
        }

        menu.AddSeparator("");
        if (clipboardDefinition != null)
            menu.AddItem(new GUIContent("粘贴"), false, PasteDefinitionFromClipboard);
        else
            menu.AddDisabledItem(new GUIContent("粘贴（剪贴板为空）"));

        menu.ShowAsContext();
    }

    private void CopyDefinitionToClipboard(GroundSurfaceMaterialDefinition source)
    {
        if (source == null)
            return;

        if (clipboardDefinition != null)
            Object.DestroyImmediate(clipboardDefinition);

        clipboardDefinition = ScriptableObject.CreateInstance<GroundSurfaceMaterialDefinition>();
        EditorUtility.CopySerialized(source, clipboardDefinition);
        clipboardDefinition.hideFlags = HideFlags.HideAndDontSave;
        clipboardDefinitionName = GetDisplayName(source);
    }

    private void CutSelectedDefinitionToClipboard()
    {
        CutDefinitionToClipboard(selectedDefinition);
    }

    private void CutDefinitionToClipboard(GroundSurfaceMaterialDefinition source)
    {
        if (source == null)
            return;

        CopyDefinitionToClipboard(source);

        if (source.isStandard)
        {
            Debug.LogWarning("标准地表材质不能剪切或删除；已复制到剪贴板，可粘贴为自定义副本。");
            return;
        }

        SelectDefinition(source);
        DeleteSelectedDefinition();
    }

    private void PasteDefinitionFromClipboard()
    {
        if (clipboardDefinition == null)
        {
            Debug.LogWarning("地表材质剪贴板为空，无法粘贴。");
            return;
        }

        CreateDefinitionFromTemplate(clipboardDefinition, clipboardDefinitionName);
    }

    private void DuplicateSelectedDefinition()
    {
        DuplicateDefinition(selectedDefinition);
    }

    private void DuplicateDefinition(GroundSurfaceMaterialDefinition source)
    {
        if (source == null)
            return;

        CreateDefinitionFromTemplate(source, GetDisplayName(source));
    }

    private void CreateDefinitionFromTemplate(GroundSurfaceMaterialDefinition source, string sourceDisplayName)
    {
        if (source == null)
            return;

        EnsureFolderExists(CustomFolder);

        GroundSurfaceMaterialDefinition asset = ScriptableObject.CreateInstance<GroundSurfaceMaterialDefinition>();
        EditorUtility.CopySerialized(source, asset);

        string baseId = SanitizeDefinitionId(string.IsNullOrWhiteSpace(source.surfaceId) ? source.name : source.surfaceId);
        asset.surfaceId = GenerateUniqueDefinitionId(baseId + "_copy");
        asset.displayName = MakeCopyDisplayName(sourceDisplayName);
        asset.isStandard = false;
        if (string.IsNullOrWhiteSpace(asset.category))
            asset.category = "自定义";
        asset.name = "GSM_" + asset.surfaceId;

        string path = AssetDatabase.GenerateUniqueAssetPath($"{CustomFolder}/GSM_{asset.surfaceId}.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Refresh();
        GroundSurfaceMaterialDefinition created = AssetDatabase.LoadAssetAtPath<GroundSurfaceMaterialDefinition>(path);
        if (created != null)
            SelectDefinition(created);
        else
            SelectDefinition(asset);
    }

    private string MakeCopyDisplayName(string sourceDisplayName)
    {
        string baseName = string.IsNullOrWhiteSpace(sourceDisplayName) ? "新地表材质" : sourceDisplayName.Trim();
        if (baseName.EndsWith(" 副本"))
            return baseName;
        return baseName + " 副本";
    }

    private string SanitizeDefinitionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "ground_surface";

        value = value.Trim().ToLowerInvariant();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                builder.Append(c);
            else if (c == '_' || c == '-' || c == ' ')
                builder.Append('_');
        }

        string result = builder.ToString().Trim('_');
        while (result.Contains("__"))
            result = result.Replace("__", "_");
        return string.IsNullOrWhiteSpace(result) ? "ground_surface" : result;
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("地表材质定义工作台（菜单简化版/真实路径已修复）", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(GetDisplayName(selectedDefinition), EditorStyles.miniBoldLabel);
        GUILayout.Space(4f);
        DrawReadonlyText("资源路径", AssetDatabase.GetAssetPath(selectedDefinition));
        DrawReadonlyText("材质 ID", selectedDefinition.surfaceId);
        DrawReadonlyText("分类", GetCategoryLabel(selectedDefinition));
        EditorGUILayout.EndVertical();
    }

    private void DrawBasicInfo()
    {
        using (new EditorGUI.DisabledScope(true))
            PropertyField("Surface ID", "surfaceId");
        PropertyField("显示名称", "displayName");
        PropertyField("分类", "category");
        PropertyField("标准资源", "isStandard");
    }

    private void DrawVisualInfo()
    {
        Rect previewRect = GUILayoutUtility.GetRect(0f, 108f, GUILayout.ExpandWidth(true));
        DrawLargePreview(previewRect, selectedDefinition);
        GUILayout.Space(4f);

        DrawTextureDistributionModePopup("素材用途", "textureDistributionMode");
        DrawTextureModeHint(selectedDefinition.textureDistributionMode);

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("通用纹理", EditorStyles.miniBoldLabel);
        PropertyField("基础纹理", "baseTexture");

        GroundSurfaceTextureDistributionMode mode = selectedDefinition.textureDistributionMode;

        if (IsTerrainSurfaceMode(mode))
            DrawTerrainTextureSettings(mode);
        else if (mode == GroundSurfaceTextureDistributionMode.StampDecal)
            DrawStampOverlaySettings();
        else if (mode == GroundSurfaceTextureDistributionMode.SplinePattern)
            DrawSplinePatternSettings();
        else
            DrawSingleLargeSettings();

        // 四档渲染模拟贴图已从主工作流移除。
        // 旧字段仍保留在 GroundSurfaceMaterialDefinition 中，仅用于兼容历史资源，不再在页面中显示或作为预览优先级。

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("高级材质模板（可选）", EditorStyles.miniBoldLabel);
        PropertyField("基础材质", "baseMaterial");
        EditorGUILayout.HelpBox("Terrain 已经接管基础地形，高级材质只作为特殊 Shader / TerrainLayer 模板入口，不再承担旧 GroundBlock 主系统。", MessageType.Info);
    }

    private bool IsTerrainSurfaceMode(GroundSurfaceTextureDistributionMode mode)
    {
        return mode == GroundSurfaceTextureDistributionMode.SeamlessTiling
            || mode == GroundSurfaceTextureDistributionMode.RandomScatter;
    }

    private void DrawTerrainTextureSettings(GroundSurfaceTextureDistributionMode mode)
    {
        GUILayout.Space(6f);
        EditorGUILayout.LabelField("Terrain 地表纹理", EditorStyles.miniBoldLabel);
        SyncUseAsTerrainSurfaceByCurrentMode();
        DrawReadonlyText("正式用途", "Terrain 地表");
        PropertyField("TerrainLayer", "terrainLayer");
        PropertyField("世界平铺尺寸", "textureWorldSize");
        EditorGUILayout.HelpBox("正式结构：TerrainLayer 是地形采样入口；音声运行层 Key 是脚步声 / 听觉系统的出口。旧版“是否作为 Terrain 地表”开关不再暴露，按素材用途自动决定。", MessageType.None);

        if (mode == GroundSurfaceTextureDistributionMode.RandomScatter)
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField("随机化", EditorStyles.miniBoldLabel);
            PropertyField("允许 90° 旋转", "allowRotate90");
            PropertyField("允许水平翻转", "allowFlipX");
            PropertyField("允许垂直翻转", "allowFlipY");
            PropertyField("随机缩放最小", "randomScaleMin");
            PropertyField("随机缩放最大", "randomScaleMax");
            PropertyField("随机偏移强度", "randomOffsetStrength");

            GUILayout.Space(6f);
            EditorGUILayout.LabelField("反重复采样", EditorStyles.miniBoldLabel);
            PropertyField("启用反重复采样", "antiRepeatEnabled");
            using (new EditorGUI.DisabledScope(!selectedDefinition.antiRepeatEnabled))
            {
                PropertyField("反重复强度", "antiRepeatStrength");
                PropertyField("反重复世界尺寸", "antiRepeatWorldSize");
                PropertyField("UV 偏移强度", "antiRepeatUvOffset");
                PropertyField("明暗随机强度", "antiRepeatToneJitter");
            }

            GUILayout.Space(6f);
            EditorGUILayout.LabelField("多纹理变体", EditorStyles.miniBoldLabel);
            PropertyField("纹理变体", "textureVariants");
            PropertyField("变体混合强度", "variantBlendStrength");
            PropertyField("随机散布混合", "stochasticBlendStrength");

            GUILayout.Space(6f);
            EditorGUILayout.LabelField("宏观变化", EditorStyles.miniBoldLabel);
            PropertyField("宏观变化纹理", "macroVariationTexture");
            PropertyField("宏观变化强度", "macroVariationStrength");
            PropertyField("宏观变化尺寸", "macroVariationWorldSize");
        }
        else
        {
            EditorGUILayout.HelpBox("循环散布纹理保持干净平铺；随机化、反重复和多变体只在“随机散布纹理”中显示。", MessageType.None);
        }
    }

    private void DrawSingleLargeSettings()
    {
        GUILayout.Space(6f);
        EditorGUILayout.LabelField("整张大图", EditorStyles.miniBoldLabel);
        SyncUseAsTerrainSurfaceByCurrentMode();
        DrawReadonlyText("正式用途", "特殊地表 / 大图入口");
        PropertyField("TerrainLayer", "terrainLayer");
        EditorGUILayout.HelpBox("整张大图保留为特殊地图地表或大面积 Overlay 的定义入口。Terrain 时代不建议把它作为常规平刷材质。", MessageType.None);
    }

    private void DrawStampOverlaySettings()
    {
        GUILayout.Space(6f);
        EditorGUILayout.LabelField("印章 / 贴花", EditorStyles.miniBoldLabel);
        SyncUseAsTerrainSurfaceByCurrentMode();
        DrawReadonlyText("正式用途", "印章 / 贴花 Overlay");
        PropertyField("印章纹理", "stampTexture");
        PropertyField("默认世界尺寸", "stampWorldSize");
        PropertyField("默认透明度", "stampOpacity");
        PropertyField("混合模式", "stampBlendMode");
        PropertyField("允许旋转", "stampCanRotate");
        PropertyField("允许缩放", "stampCanScale");
        PropertyField("覆盖地面标签", "stampOverridesSurfaceType");
        EditorGUILayout.HelpBox("印章用于裂缝、油污、血迹、地面文字等 Overlay。默认只改变视觉，不改变脚下地面标签；只有明确需要时才勾选“覆盖地面标签”。", MessageType.Info);
    }

    private void DrawSplinePatternSettings()
    {
        GUILayout.Space(6f);
        EditorGUILayout.LabelField("样条图案 / 方向画线", EditorStyles.miniBoldLabel);
        SyncUseAsTerrainSurfaceByCurrentMode();
        DrawReadonlyText("正式用途", "样条图案 / 画线 Overlay");
        PropertyField("样条纹理", "splineTexture");
        PropertyField("默认线宽", "splineWorldWidth");
        PropertyField("单段世界长度", "splineSegmentWorldLength");
        PropertyField("盖印间距", "splineStampSpacing");
        PropertyField("默认透明度", "splineOpacity");
        PropertyField("混合模式", "splineBlendMode");
        PropertyField("跟随笔刷方向", "splineFollowBrushDirection");
        PropertyField("连续绘制", "splineContinuous");
        PropertyField("角度平滑", "splineAngleSmoothing");
        PropertyField("覆盖地面标签", "stampOverridesSurfaceType");

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("样条图案蒙版 / 破损图层", EditorStyles.miniBoldLabel);
        PropertyField("启用蒙版", "splineMaskEnabled");
        using (new EditorGUI.DisabledScope(!selectedDefinition.splineMaskEnabled))
        {
            PropertyField("蒙版纹理", "splineMaskTexture");
            PropertyField("蒙版强度", "splineMaskStrength");
            PropertyField("蒙版阈值", "splineMaskThreshold");
            PropertyField("蒙版软边", "splineMaskSoftness");
            PropertyField("蒙版世界尺寸", "splineMaskWorldSize");
            PropertyField("反转蒙版", "splineMaskInvert");
            PropertyField("蒙版偏移", "splineMaskOffset");
        }

        EditorGUILayout.HelpBox("马路线这类素材放在这里。素材建议横向绘制：X 轴是线条前进方向，Y 轴是线宽方向；绘制工具会按鼠标路径方向旋转盖印。蒙版只对样条图案 / 画线显示，用于白线斑驳、掉漆、残缺等效果。", MessageType.Info);
    }

    private void DrawColorComposition()
    {
        PropertyField("基础颜色", "baseColor");
        DrawColorBlendModePopup("颜色合成方式", "baseColorBlendMode");
        PropertyField("颜色合成强度", "baseColorBlendStrength");
        PropertyField("明度", "brightness");
        PropertyField("对比度", "contrast");
        PropertyField("饱和度", "saturation");
        EditorGUILayout.HelpBox("有纹理时，基础颜色不再粗暴盖住图像，而是按合成方式参与调色。推荐默认用“乘算叠加”，用于统一地表色调；没有纹理时，基础颜色作为兜底显示。", MessageType.Info);
    }

    private void DrawTextureDistributionModePopup(string label, string propertyName)
    {
        SerializedProperty prop = selectedSO.FindProperty(propertyName);
        if (prop == null)
        {
            EditorGUILayout.LabelField(label, "字段不存在");
            return;
        }

        GroundSurfaceTextureDistributionMode current = (GroundSurfaceTextureDistributionMode)prop.enumValueIndex;
        GroundSurfaceTextureDistributionMode[] values =
        {
            GroundSurfaceTextureDistributionMode.SeamlessTiling,
            GroundSurfaceTextureDistributionMode.RandomScatter,
            GroundSurfaceTextureDistributionMode.SingleLarge,
            GroundSurfaceTextureDistributionMode.StampDecal,
            GroundSurfaceTextureDistributionMode.SplinePattern,
        };
        string[] labels = values.Select(GetTextureDistributionModeLabel).ToArray();
        int currentIndex = Mathf.Max(0, System.Array.IndexOf(values, current));

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(150f));
        int nextIndex = EditorGUILayout.Popup(currentIndex, labels);
        EditorGUILayout.EndHorizontal();

        if (nextIndex != currentIndex)
        {
            GroundSurfaceTextureDistributionMode nextMode = values[nextIndex];
            prop.enumValueIndex = (int)nextMode;
            ApplyUsageDefaultsAfterModeChange(nextMode);
        }
    }

    private void ApplyUsageDefaultsAfterModeChange(GroundSurfaceTextureDistributionMode mode)
    {
        if (selectedSO == null)
            return;

        SerializedProperty terrainProp = selectedSO.FindProperty("useAsTerrainSurface");
        SerializedProperty categoryProp = selectedSO.FindProperty("category");

        bool terrainSurface = mode == GroundSurfaceTextureDistributionMode.SeamlessTiling
            || mode == GroundSurfaceTextureDistributionMode.RandomScatter;

        if (terrainProp != null && terrainProp.propertyType == SerializedPropertyType.Boolean)
            terrainProp.boolValue = terrainSurface;

        if (categoryProp != null && categoryProp.propertyType == SerializedPropertyType.String)
        {
            string current = categoryProp.stringValue ?? "";
            bool canAutoCategory = string.IsNullOrWhiteSpace(current)
                || current == "自定义"
                || current == "Terrain 地表"
                || current == "印章 / 贴花"
                || current == "样条图案 / 画线"
                || current == "特殊地表";

            if (canAutoCategory)
            {
                switch (mode)
                {
                    case GroundSurfaceTextureDistributionMode.StampDecal:
                        categoryProp.stringValue = "印章 / 贴花";
                        break;
                    case GroundSurfaceTextureDistributionMode.SplinePattern:
                        categoryProp.stringValue = "样条图案 / 画线";
                        break;
                    case GroundSurfaceTextureDistributionMode.SingleLarge:
                        categoryProp.stringValue = "特殊地表";
                        break;
                    case GroundSurfaceTextureDistributionMode.SeamlessTiling:
                    case GroundSurfaceTextureDistributionMode.RandomScatter:
                    default:
                        categoryProp.stringValue = "Terrain 地表";
                        break;
                }
            }
        }
    }

    private void DrawColorBlendModePopup(string label, string propertyName)
    {
        SerializedProperty prop = selectedSO.FindProperty(propertyName);
        if (prop == null)
        {
            EditorGUILayout.LabelField(label, "字段不存在");
            return;
        }

        GroundSurfaceColorBlendMode current = (GroundSurfaceColorBlendMode)prop.enumValueIndex;
        GroundSurfaceColorBlendMode[] values =
        {
            GroundSurfaceColorBlendMode.None,
            GroundSurfaceColorBlendMode.Tint,
            GroundSurfaceColorBlendMode.Multiply,
            GroundSurfaceColorBlendMode.Overlay,
            GroundSurfaceColorBlendMode.Additive,
        };
        string[] labels = values.Select(GetColorBlendModeLabel).ToArray();
        int currentIndex = Mathf.Max(0, System.Array.IndexOf(values, current));

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(150f));
        int nextIndex = EditorGUILayout.Popup(currentIndex, labels);
        EditorGUILayout.EndHorizontal();

        if (nextIndex != currentIndex)
            prop.enumValueIndex = (int)values[nextIndex];
    }

    private void DrawGroundSurfaceTypePopup(string label, string propertyName)
    {
        SerializedProperty prop = selectedSO.FindProperty(propertyName);
        if (prop == null)
        {
            EditorGUILayout.LabelField(label, "字段不存在");
            return;
        }

        GroundSurfaceType current = (GroundSurfaceType)prop.enumValueIndex;
        GroundSurfaceType[] values =
        {
            GroundSurfaceType.Default,
            GroundSurfaceType.Concrete,
            GroundSurfaceType.Metal,
            GroundSurfaceType.Wood,
            GroundSurfaceType.Dirt,
            GroundSurfaceType.Grass,
            GroundSurfaceType.Sand,
            GroundSurfaceType.Water,
            GroundSurfaceType.Glass,
        };
        string[] labels = values.Select(GetGroundSurfaceTypeLabel).ToArray();
        int currentIndex = Mathf.Max(0, System.Array.IndexOf(values, current));

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(150f));
        int nextIndex = EditorGUILayout.Popup(currentIndex, labels);
        EditorGUILayout.EndHorizontal();

        if (nextIndex != currentIndex)
            prop.enumValueIndex = (int)values[nextIndex];
    }

    private string GetTextureDistributionModeLabel(GroundSurfaceTextureDistributionMode mode)
    {
        switch (mode)
        {
            case GroundSurfaceTextureDistributionMode.SeamlessTiling: return "循环散布纹理";
            case GroundSurfaceTextureDistributionMode.RandomScatter: return "随机散布纹理";
            case GroundSurfaceTextureDistributionMode.SingleLarge: return "整张大图";
            case GroundSurfaceTextureDistributionMode.StampDecal: return "印章 / 贴花";
            case GroundSurfaceTextureDistributionMode.SplinePattern: return "样条图案";
            default: return mode.ToString();
        }
    }

    private string GetColorBlendModeLabel(GroundSurfaceColorBlendMode mode)
    {
        switch (mode)
        {
            case GroundSurfaceColorBlendMode.None: return "不合成";
            case GroundSurfaceColorBlendMode.Tint: return "普通染色";
            case GroundSurfaceColorBlendMode.Multiply: return "乘算叠加";
            case GroundSurfaceColorBlendMode.Overlay: return "Overlay 叠加";
            case GroundSurfaceColorBlendMode.Additive: return "加算";
            default: return mode.ToString();
        }
    }

    private string GetGroundSurfaceTypeLabel(GroundSurfaceType type)
    {
        switch (type)
        {
            case GroundSurfaceType.Default: return "未指定";
            case GroundSurfaceType.Concrete: return "石质地面 / 水泥";
            case GroundSurfaceType.Metal: return "金属地面";
            case GroundSurfaceType.Wood: return "木质地面";
            case GroundSurfaceType.Dirt: return "泥土地面";
            case GroundSurfaceType.Grass: return "草地摩擦";
            case GroundSurfaceType.Sand: return "沙地颗粒";
            case GroundSurfaceType.Water: return "浅水水花";
            case GroundSurfaceType.Glass: return "玻璃 / 硬质";
            default: return type.ToString();
        }
    }

    private string GetRuntimeLayerKeyFromSurfaceType(GroundSurfaceType type)
    {
        switch (type)
        {
            case GroundSurfaceType.Concrete: return "surface_stone";
            case GroundSurfaceType.Metal: return "surface_metal";
            case GroundSurfaceType.Wood: return "surface_wood";
            case GroundSurfaceType.Dirt: return "surface_mud";
            case GroundSurfaceType.Grass: return "surface_grass";
            case GroundSurfaceType.Sand: return "surface_sand";
            case GroundSurfaceType.Water: return "surface_water";
            case GroundSurfaceType.Glass: return "surface_stone";
            default: return "";
        }
    }

    private void DrawSurfaceTypeAudioHint(GroundSurfaceType type)
    {
        string key = GetRuntimeLayerKeyFromSurfaceType(type);
        string label = GetRuntimeLayerLabel(key);
        EditorGUILayout.HelpBox(
            string.IsNullOrWhiteSpace(key)
                ? "当前地面标签未指定。建议从音声合成运行层 Key 中选择一个 surface_* 层。"
                : $"音声合成建议运行层：{label}。地面标签是脚步语义分类；运行层 Key 是音声合成模块检索入口。",
            MessageType.None);
    }

    private void ApplyAudioTagsFromSurfaceType()
    {
        if (selectedSO == null)
            return;

        GroundSurfaceType type = selectedDefinition != null ? selectedDefinition.surfaceType : GroundSurfaceType.Default;
        string key = GetRuntimeLayerKeyFromSurfaceType(type);
        SetFormalAudioRuntimeLayerKey(key);
        GUI.changed = true;
    }

    private void ApplyAudioTagsFromSurfaceId()
    {
        if (selectedDefinition == null || selectedSO == null)
            return;

        string stem = SanitizeAudioTagStem(selectedDefinition.surfaceId);
        string key = string.IsNullOrWhiteSpace(stem) ? "custom" : "surface_" + stem;
        SetFormalAudioRuntimeLayerKey(key);
        GUI.changed = true;
    }

    private string SanitizeAudioTagStem(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "default";

        value = value.Trim().ToLowerInvariant();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                builder.Append(c);
            else if (c == '_' || c == '-' || c == ' ')
                builder.Append('_');
        }

        string result = builder.ToString().Trim('_');
        while (result.Contains("__"))
            result = result.Replace("__", "_");
        return string.IsNullOrWhiteSpace(result) ? "default" : result;
    }


    private void SyncUseAsTerrainSurfaceByCurrentMode()
    {
        if (selectedSO == null || selectedDefinition == null)
            return;

        SerializedProperty terrainProp = selectedSO.FindProperty("useAsTerrainSurface");
        if (terrainProp == null || terrainProp.propertyType != SerializedPropertyType.Boolean)
            return;

        GroundSurfaceTextureDistributionMode mode = selectedDefinition.textureDistributionMode;
        bool terrainSurface = mode == GroundSurfaceTextureDistributionMode.SeamlessTiling
            || mode == GroundSurfaceTextureDistributionMode.RandomScatter;

        if (terrainProp.boolValue != terrainSurface)
            terrainProp.boolValue = terrainSurface;
    }

    private void SetFormalAudioRuntimeLayerKey(string key)
    {
        SetStringProperty("audioRuntimeLayerKey", key);

        // 同步旧字段仅用于兼容还没升级完的运行时脚本 / 历史资源，不再作为页面主入口。
        SetStringProperty("footstepAudioTag", key);
        SetStringProperty("landingAudioTag", key);
        SetStringProperty("slideAudioTag", key);
    }

    private void MigrateLegacyAudioTagsToFormalKeyIfNeeded()
    {
        if (selectedSO == null || selectedDefinition == null)
            return;

        SerializedProperty formal = selectedSO.FindProperty("audioRuntimeLayerKey");
        if (formal == null || formal.propertyType != SerializedPropertyType.String || !string.IsNullOrWhiteSpace(formal.stringValue))
            return;

        string legacy = selectedDefinition.EffectiveAudioRuntimeLayerKey;
        if (string.IsNullOrWhiteSpace(legacy))
            return;

        formal.stringValue = legacy;
    }

    private void DrawRuntimeLayerKeyPopup(string label, string propertyName)
    {
        SerializedProperty prop = selectedSO.FindProperty(propertyName);
        if (prop == null || prop.propertyType != SerializedPropertyType.String)
        {
            EditorGUILayout.LabelField(label, "字段不存在");
            return;
        }

        string currentKey = prop.stringValue ?? "";
        int currentIndex = System.Array.FindIndex(RuntimeLayerOptions, x => x.key == currentKey);
        bool customValue = currentIndex < 0;
        if (customValue)
            currentIndex = RuntimeLayerOptions.Length - 1;

        string[] labels = RuntimeLayerOptions.Select(x => x.label).ToArray();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(150f));
        int nextIndex = EditorGUILayout.Popup(currentIndex, labels);
        EditorGUILayout.EndHorizontal();

        if (nextIndex != currentIndex || customValue)
        {
            string nextKey = RuntimeLayerOptions[Mathf.Clamp(nextIndex, 0, RuntimeLayerOptions.Length - 1)].key;
            if (!customValue || nextIndex != RuntimeLayerOptions.Length - 1)
                prop.stringValue = nextKey;
        }

        if (prop.stringValue == "custom" || customValue)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("自定义 Key", GUILayout.Width(150f));
            prop.stringValue = EditorGUILayout.TextField(customValue ? currentKey : prop.stringValue);
            EditorGUILayout.EndHorizontal();
        }
    }

    private string GetRuntimeLayerLabel(string key)
    {
        for (int i = 0; i < RuntimeLayerOptions.Length; i++)
        {
            if (RuntimeLayerOptions[i].key == key)
                return RuntimeLayerOptions[i].label;
        }

        return string.IsNullOrWhiteSpace(key) ? "未指定" : key;
    }

    private void SetStringProperty(string propertyName, string value)
    {
        SerializedProperty prop = selectedSO.FindProperty(propertyName);
        if (prop != null && prop.propertyType == SerializedPropertyType.String)
            prop.stringValue = value;
    }

    private void DrawTextureModeHint(GroundSurfaceTextureDistributionMode mode)
    {
        string message;
        switch (mode)
        {
            case GroundSurfaceTextureDistributionMode.SeamlessTiling:
                message = "循环散布纹理：适合能上下左右无缝循环的基础地表。";
                break;
            case GroundSurfaceTextureDistributionMode.RandomScatter:
                message = "随机散布纹理：适合半可循环素材，会用于随机旋转、翻转、缩放、偏移与变体打散。";
                break;
            case GroundSurfaceTextureDistributionMode.SingleLarge:
                message = "整张大图：适合按 GroundBlock 0-1 范围铺满的独立大地面，不建议频繁平刷。";
                break;
            case GroundSurfaceTextureDistributionMode.StampDecal:
                message = "印章/贴花型：适合污渍、裂缝、水渍等局部图案，后续应进入贴花/印章层。";
                break;
            case GroundSurfaceTextureDistributionMode.SplinePattern:
                message = "样条图案型：适合马路线、铁轨、木枕等沿路径生成的图案，后续应进入路径刷。";
                break;
            default:
                message = "未知采样模式。";
                break;
        }

        EditorGUILayout.HelpBox(message, MessageType.None);
    }

    private void DrawGroundRule()
    {
        GroundSurfaceTextureDistributionMode mode = selectedDefinition != null
            ? selectedDefinition.textureDistributionMode
            : GroundSurfaceTextureDistributionMode.SeamlessTiling;

        bool overlayMode = mode == GroundSurfaceTextureDistributionMode.StampDecal
            || mode == GroundSurfaceTextureDistributionMode.SplinePattern;

        if (overlayMode)
        {
            PropertyField("覆盖地面标签", "stampOverridesSurfaceType");
            EditorGUILayout.HelpBox("印章 / 样条默认只是地表视觉覆盖层。没有勾选覆盖时，角色脚步声、摩擦和 AI 听觉仍读取底层 Terrain 地面标签。", MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(overlayMode && selectedDefinition != null && !selectedDefinition.stampOverridesSurfaceType))
        {
            DrawGroundSurfaceTypePopup("地面标签", "surfaceType");
            DrawSurfaceTypeAudioHint(selectedDefinition.surfaceType);
            PropertyField("摩擦系数", "friction");
            PropertyField("行走噪声倍率", "walkNoiseMultiplier");
            PropertyField("奔跑噪声倍率", "runNoiseMultiplier");
            PropertyField("潜行噪声倍率", "sneakNoiseMultiplier");
            PropertyField("落地噪声倍率", "landingNoiseMultiplier");
        }
    }

    private void DrawAudioSynth()
    {
        MigrateLegacyAudioTagsToFormalKeyIfNeeded();

        EditorGUILayout.HelpBox(
            "正式结构：地表材质直接绑定地表音声包，例如 AP_Surface_Grass；音声运行层 Key 只作为该包内部的检索入口，例如 surface_grass。脚步、落地、滑步差异交给音声合成器内部按 Spine 事件 / 音轨组合处理。",
            MessageType.Info);

        PropertyField("地表音声包", "surfaceAudioPackage");

        GUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("按地面标签对齐 Key", GUILayout.Height(22f)))
            ApplyAudioTagsFromSurfaceType();
        if (GUILayout.Button("按材质 ID 生成自定义 Key", GUILayout.Height(22f)))
            ApplyAudioTagsFromSurfaceId();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);
        DrawRuntimeLayerKeyPopup("包内音声运行层 Key", "audioRuntimeLayerKey");
    }

    private void DrawEffects()
    {
        PropertyField("默认脚步特效", "defaultFootstepFx");
        PropertyField("默认落地特效", "defaultLandingFx");
    }

    private void DrawNote()
    {
        PropertyField("备注", "note", true);
    }

    private void DrawSection(string title, System.Action drawer)
    {
        if (!sectionFoldouts.ContainsKey(title))
            sectionFoldouts[title] = true;

        sectionFoldouts[title] = EditorGUILayout.Foldout(sectionFoldouts[title], title, true);
        if (!sectionFoldouts[title])
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

    private void DrawDefinitionRow(Rect rect, GroundSurfaceMaterialDefinition def)
    {
        bool selected = selectedDefinition == def;
        bool hover = rect.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, selectedBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accentMagenta);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.05f));
        }

        Event e = Event.current;
        if (e != null && e.type == EventType.ContextClick && rect.Contains(e.mousePosition))
        {
            SelectDefinition(def);
            ShowDefinitionContextMenu(def);
            e.Use();
        }

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            SelectDefinition(def);

        Rect iconRect = new Rect(rect.x + 8f, rect.y + 6f, 34f, 34f);
        DrawPreviewIcon(iconRect, def, selected);

        GUIStyle titleStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            normal = { textColor = selected ? Color.white : new Color(0.92f, 0.92f, 0.94f, 1f) }
        };

        GUIStyle subStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = selected ? new Color(1f, 0.78f, 0.95f, 1f) : new Color(0.66f, 0.66f, 0.68f, 1f) }
        };

        Rect titleRect = new Rect(rect.x + 48f, rect.y + 5f, rect.width - 52f, 18f);
        Rect subRect = new Rect(rect.x + 48f, rect.y + 24f, rect.width - 52f, 16f);
        GUI.Label(titleRect, GetDisplayName(def), titleStyle);
        GUI.Label(subRect, GetSurfaceSummary(def), subStyle);
    }

    private void DrawPreviewIcon(Rect rect, GroundSurfaceMaterialDefinition def, bool selected)
    {
        Texture texture = GetPreviewTexture(def);
        Color fallbackColor = def != null ? def.baseColor : new Color(0.5f, 0.5f, 0.5f, 1f);

        EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.06f));
        Rect inner = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);

        if (texture != null)
        {
            GUI.DrawTexture(inner, texture, ScaleMode.ScaleAndCrop, true);
            if (def != null && def.baseColor.a > 0f)
                EditorGUI.DrawRect(new Rect(inner.xMax - 9f, inner.yMax - 9f, 8f, 8f), def.baseColor);
        }
        else
        {
            EditorGUI.DrawRect(inner, fallbackColor);
            Texture2D fallback = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (fallback != null)
                GUI.DrawTexture(new Rect(inner.x + 5f, inner.y + 5f, inner.width - 10f, inner.height - 10f), fallback, ScaleMode.ScaleToFit, true);
        }

        DrawThinBorder(rect, selected ? accentMagenta : new Color(1f, 1f, 1f, 0.12f));
    }

    private void DrawLargePreview(Rect rect, GroundSurfaceMaterialDefinition def)
    {
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.085f, 1f));
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.12f));

        Rect preview = new Rect(rect.x + 8f, rect.y + 8f, 92f, rect.height - 16f);
        DrawPreviewIcon(preview, def, false);

        Rect labelRect = new Rect(preview.xMax + 10f, rect.y + 10f, rect.width - preview.width - 26f, 22f);
        GUI.Label(labelRect, GetDisplayName(def), EditorStyles.boldLabel);
        Rect subRect = new Rect(preview.xMax + 10f, labelRect.yMax + 4f, rect.width - preview.width - 26f, 18f);
        GUI.Label(subRect, GetSurfaceSummary(def), EditorStyles.miniLabel);
        Rect hintRect = new Rect(preview.xMax + 10f, subRect.yMax + 6f, rect.width - preview.width - 26f, 44f);
        GUI.Label(hintRect, "预览直接来自基础纹理 / 变体 / 材质模板；不再需要单独维护预览图标。", EditorStyles.wordWrappedMiniLabel);
    }

    private Texture GetPreviewTextureForCurrentTier(GroundSurfaceMaterialDefinition def)
    {
        // 四档预览贴图已从主工作流移除。
        // 预览只使用当前定义的真实视觉纹理，避免旧 L0/L1/L2/L3 字段继续影响素材判断。
        return def != null ? def.baseTexture : null;
    }

    private Texture GetPreviewTexture(GroundSurfaceMaterialDefinition def)
    {
        if (def == null)
            return null;

        if (def.textureDistributionMode == GroundSurfaceTextureDistributionMode.SplinePattern)
        {
            if (def.splineTexture != null) return def.splineTexture;
            if (def.stampTexture != null) return def.stampTexture;
        }
        else if (def.textureDistributionMode == GroundSurfaceTextureDistributionMode.StampDecal)
        {
            if (def.stampTexture != null) return def.stampTexture;
        }

        Texture tierTexture = GetPreviewTextureForCurrentTier(def);
        if (tierTexture != null)
            return tierTexture;

        if (def.textureVariants != null && def.textureVariants.Count > 0)
        {
            for (int i = 0; i < def.textureVariants.Count; i++)
            {
                if (def.textureVariants[i] != null)
                    return def.textureVariants[i];
            }
        }

        if (def.baseMaterial != null)
        {
            Texture texture = null;
            if (def.baseMaterial.HasProperty("_BaseMap"))
                texture = def.baseMaterial.GetTexture("_BaseMap");
            if (texture == null && def.baseMaterial.HasProperty("_MainTex"))
                texture = def.baseMaterial.GetTexture("_MainTex");
            if (texture != null)
                return texture;

            Texture preview = AssetPreview.GetAssetPreview(def.baseMaterial);
            if (preview != null)
                return preview;
            return AssetPreview.GetMiniThumbnail(def.baseMaterial);
        }

        if (def.previewIcon != null)
            return def.previewIcon.texture;

        return null;
    }

    private string GetSurfaceSummary(GroundSurfaceMaterialDefinition def)
    {
        if (def == null)
            return "-";
        string type = GetGroundSurfaceTypeLabel(def.surfaceType);
        string mode = GetTextureDistributionModeLabel(def.textureDistributionMode);
        return $"{type} / {mode} / 摩擦 {def.friction:0.##}";
    }

    private List<GroundSurfaceMaterialDefinition> GetFilteredDefinitions()
    {
        if (string.IsNullOrWhiteSpace(search))
            return definitions;

        string s = search.ToLowerInvariant();
        return definitions.Where(d =>
            d != null &&
            ((d.surfaceId != null && d.surfaceId.ToLowerInvariant().Contains(s)) ||
             (d.displayName != null && d.displayName.ToLowerInvariant().Contains(s)) ||
             (d.category != null && d.category.ToLowerInvariant().Contains(s)) ||
             d.surfaceType.ToString().ToLowerInvariant().Contains(s) ||
             d.textureDistributionMode.ToString().ToLowerInvariant().Contains(s) ||
             GetTextureDistributionModeLabel(d.textureDistributionMode).ToLowerInvariant().Contains(s) ||
             GetGroundSurfaceTypeLabel(d.surfaceType).ToLowerInvariant().Contains(s))).ToList();
    }

    private void SelectDefinition(GroundSurfaceMaterialDefinition def)
    {
        // 切换左侧列表前，先把右侧当前正在编辑的 SerializedObject 写回真实资产。
        // 否则 TextField 的编辑缓存可能会把上一项的值带到下一项，看起来像“列表切换没有刷新”。
        CommitSelectedDefinitionEditsBeforeSelectionChange();

        if (selectedDefinition == def && selectedSO != null && selectedSO.targetObject == def)
        {
            selectedSO.Update();
            RepaintHostWindowIfPossible();
            return;
        }

        GUI.FocusControl(null);
        GUIUtility.keyboardControl = 0;

        selectedDefinition = def;
        selectedSO = def != null ? new SerializedObject(def) : null;
        if (selectedSO != null)
            selectedSO.Update();

        rightScroll = Vector2.zero;
        RepaintHostWindowIfPossible();
    }

    private void CommitSelectedDefinitionEditsBeforeSelectionChange()
    {
        if (selectedSO == null || selectedSO.targetObject == null)
            return;

        bool changed = selectedSO.ApplyModifiedProperties();
        if (!changed || selectedDefinition == null)
            return;

        EditorUtility.SetDirty(selectedDefinition);
        ScheduleSurfaceRuntimeDirtyMark(selectedDefinition);
        SkyPrisonGroundSurfaceMaterialSyncBridge.SyncAfterGroundSurfaceRefresh();
    }

    private void RepaintHostWindowIfPossible()
    {
        GUI.changed = true;
    }

    private void ShowCreateDefinitionMenu()
    {
        GenericMenu menu = new GenericMenu();

        menu.AddItem(new GUIContent("地面纹理"), false,
            () => CreateDefinition(GroundSurfaceTextureDistributionMode.SeamlessTiling));

        menu.AddItem(new GUIContent("随机纹理"), false,
            () => CreateDefinition(GroundSurfaceTextureDistributionMode.RandomScatter));

        menu.AddItem(new GUIContent("印章"), false,
            () => CreateDefinition(GroundSurfaceTextureDistributionMode.StampDecal));

        menu.AddItem(new GUIContent("样条图案"), false, () => CreateDefinition(GroundSurfaceTextureDistributionMode.SplinePattern));
        menu.AddItem(new GUIContent("大图"), false,
            () => CreateDefinition(GroundSurfaceTextureDistributionMode.SingleLarge));

        menu.ShowAsContext();
    }

    private void CreateDefinition()
    {
        CreateDefinition(GroundSurfaceTextureDistributionMode.SeamlessTiling);
    }

    private void CreateDefinition(GroundSurfaceTextureDistributionMode mode)
    {
        EnsureFolderExists(CustomFolder);
        GroundSurfaceMaterialDefinition asset = ScriptableObject.CreateInstance<GroundSurfaceMaterialDefinition>();
        ConfigureNewDefinitionDefaults(asset, mode);

        string path = AssetDatabase.GenerateUniqueAssetPath($"{CustomFolder}/GSM_{asset.surfaceId}.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Refresh();
        SelectDefinition(asset);
    }

    private void ConfigureNewDefinitionDefaults(GroundSurfaceMaterialDefinition asset, GroundSurfaceTextureDistributionMode mode)
    {
        if (asset == null)
            return;

        string baseId;
        string displayName;
        string category;

        switch (mode)
        {
            case GroundSurfaceTextureDistributionMode.StampDecal:
                baseId = "new_ground_stamp";
                displayName = "新地面印章";
                category = "印章 / 贴花";
                asset.useAsTerrainSurface = false;
                asset.stampWorldSize = new Vector2(1f, 1f);
                asset.stampOpacity = 1f;
                asset.stampBlendMode = GroundSurfaceOverlayBlendMode.AlphaBlend;
                break;

            case GroundSurfaceTextureDistributionMode.SplinePattern:
                baseId = "new_ground_spline";
                displayName = "新地面样条图案";
                category = "样条图案 / 画线";
                asset.useAsTerrainSurface = false;
                asset.splineWorldWidth = 0.25f;
                asset.splineSegmentWorldLength = 1f;
                asset.splineStampSpacing = 0.35f;
                asset.splineFollowBrushDirection = true;
                asset.splineContinuous = true;
                asset.splineMaskEnabled = false;
                asset.splineMaskStrength = 1f;
                asset.splineMaskThreshold = 0.45f;
                asset.splineMaskSoftness = 0.08f;
                asset.splineMaskWorldSize = 3f;
                asset.splineMaskInvert = false;
                asset.splineMaskOffset = Vector2.zero;
                break;

            case GroundSurfaceTextureDistributionMode.RandomScatter:
                baseId = "new_ground_scatter";
                displayName = "新随机散布地表";
                category = "Terrain 地表";
                asset.useAsTerrainSurface = true;
                break;

            case GroundSurfaceTextureDistributionMode.SingleLarge:
                baseId = "new_ground_large";
                displayName = "新整张大图地表";
                category = "特殊地表";
                asset.useAsTerrainSurface = false;
                break;

            case GroundSurfaceTextureDistributionMode.SeamlessTiling:
            default:
                baseId = "new_ground_surface";
                displayName = "新地表纹理";
                category = "Terrain 地表";
                asset.useAsTerrainSurface = true;
                break;
        }

        asset.surfaceId = GenerateUniqueDefinitionId(baseId);
        asset.displayName = displayName;
        asset.category = category;
        asset.surfaceType = GroundSurfaceType.Default;
        asset.baseColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        asset.textureDistributionMode = mode;
        asset.baseColorBlendMode = GroundSurfaceColorBlendMode.Multiply;
        asset.surfaceAudioPackage = null;
        asset.audioRuntimeLayerKey = "";
        asset.footstepAudioTag = "";
        asset.landingAudioTag = "";
        asset.slideAudioTag = "";
        asset.name = "GSM_" + asset.surfaceId;
    }

    private void DeleteSelectedDefinition()
    {
        if (selectedDefinition == null || selectedDefinition.isStandard)
            return;

        string path = AssetDatabase.GetAssetPath(selectedDefinition);
        if (string.IsNullOrEmpty(path))
            return;

        if (!EditorUtility.DisplayDialog("删除地表材质定义", "确定删除当前自定义地表材质定义吗？", "删除", "取消"))
            return;

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        selectedDefinition = null;
        selectedSO = null;
        Refresh();
    }

    private string GetDisplayName(GroundSurfaceMaterialDefinition def)
    {
        if (def == null)
            return "-";
        if (!string.IsNullOrWhiteSpace(def.displayName))
            return def.displayName;
        if (!string.IsNullOrWhiteSpace(def.surfaceId))
            return def.surfaceId;
        return def.name;
    }

    private string GetCategoryLabel(GroundSurfaceMaterialDefinition def)
    {
        if (def == null || string.IsNullOrWhiteSpace(def.category))
            return "未分类";
        return def.category;
    }

    private string GenerateUniqueDefinitionId(string baseId)
    {
        HashSet<string> used = new HashSet<string>();
        foreach (GroundSurfaceMaterialDefinition def in definitions)
        {
            if (def != null && !string.IsNullOrWhiteSpace(def.surfaceId))
                used.Add(def.surfaceId);
        }

        if (!used.Contains(baseId))
            return baseId;

        int index = 1;
        while (used.Contains(baseId + "_" + index))
            index++;
        return baseId + "_" + index;
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

    private void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }
}
