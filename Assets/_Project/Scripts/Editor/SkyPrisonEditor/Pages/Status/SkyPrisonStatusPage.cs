using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonStatusPage : SkyPrisonEditorPageBase
{
    public const string DefaultCreateFolder = "Assets/_Project/Data/Definitions/Custom/Status";

    private readonly Color accentPurple = new Color(1.00f, 0.42f, 0.72f, 1f);
    private readonly Color selectedRowPurple = new Color(1.00f, 0.42f, 0.72f, 0.26f);
    private readonly Color leftPanelBg = new Color(0.18f, 0.18f, 0.19f, 1f);
    private readonly Color listContainerBg = new Color(0.11f, 0.11f, 0.14f, 1f);

    private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>
    {
        { "基础信息", true },
        { "多语言名称", true },
        { "多语言描述", true },
        { "赋予方式", true },
        { "持续时间", true },
        { "叠层规则", true },
        { "基础属性修正", true },
        { "DOT设定", true },
        { "触发器生命周期", true },
        { "特效与音效", true },
    };

    private const string ModifierClipboardPrefix = "SkyPrison.StatusAttributeModifiers:";
    private static string modifierClipboard = "";

    private readonly List<int> selectedModifierIndices = new List<int>();
    private Vector2 modifierScroll;
    private int lastModifierSelectionIndex = -1;

    private readonly Dictionary<string, RichTextSelectionState> richTextSelections = new Dictionary<string, RichTextSelectionState>();
    private Color richTextCustomColor = Color.white;

    private List<StatusDefinition> statusDefinitions = new List<StatusDefinition>();
    private StatusDefinition selectedStatusDefinition;
    private SerializedObject selectedSO;
    private Vector2 leftScroll;
    private string searchText = "";

    public SkyPrisonStatusPage(SkyPrisonEditorContext context) : base(context) { }

    public override string TabName => "状态";

    public override void OnEnable()
    {
        Refresh();
    }

    public override void Refresh()
    {
        string selectedPath = selectedStatusDefinition != null ? AssetDatabase.GetAssetPath(selectedStatusDefinition) : "";

        string[] guids = AssetDatabase.FindAssets("t:StatusDefinition");
        statusDefinitions = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<StatusDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(x => x != null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
            .ThenBy(x => x.name)
            .ToList();

        if (!string.IsNullOrEmpty(selectedPath))
        {
            StatusDefinition matched = statusDefinitions.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == selectedPath);
            if (matched != null)
                selectedStatusDefinition = matched;
        }

        if (selectedStatusDefinition == null && statusDefinitions.Count > 0)
            SelectStatus(statusDefinitions[0]);
    }

    public override void OnGUILeft()
    {
        Rect fullRect = GUILayoutUtility.GetRect(
            0f, 100000f, 0f, 100000f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        EditorGUI.DrawRect(fullRect, leftPanelBg);

        Rect inner = new Rect(fullRect.x + 8f, fullRect.y + 8f, fullRect.width - 16f, fullRect.height - 16f);
        float y = inner.y;

        Rect titleRect = new Rect(inner.x, y, inner.width, 20f);
        y += 24f;

        Rect toolbarRect = new Rect(inner.x, y, inner.width, 22f);
        y += 28f;

        Rect searchRect = new Rect(inner.x, y, inner.width, 20f);
        y += 28f;

        Rect listOuterRect = new Rect(inner.x, y, inner.width, Mathf.Max(80f, inner.yMax - y));
        Rect listViewRect = new Rect(listOuterRect.x + 6f, listOuterRect.y + 6f, listOuterRect.width - 12f, listOuterRect.height - 12f);

        GUI.Label(titleRect, "状态列表", EditorStyles.boldLabel);
        DrawLeftToolbar(toolbarRect);
        searchText = EditorGUI.TextField(searchRect, searchText ?? "");

        EditorGUI.DrawRect(listOuterRect, listContainerBg);
        DrawThinBorder(listOuterRect, new Color(1f, 1f, 1f, 0.08f));

        IEnumerable<StatusDefinition> filtered = statusDefinitions;
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string search = searchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(x => GetDisplayLabel(x).ToLowerInvariant().Contains(search));
        }

        List<StatusDefinition> filteredList = filtered.ToList();
        float contentHeight = Mathf.Max(listViewRect.height, filteredList.Count * 24f + 4f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, listViewRect.width - 14f), contentHeight);

        Vector2 localMouse = Event.current.mousePosition + leftScroll - new Vector2(listViewRect.x, listViewRect.y);
        leftScroll = GUI.BeginScrollView(listViewRect, leftScroll, contentRect, false, true);

        if (filteredList.Count == 0)
        {
            GUI.Label(new Rect(8f, 6f, contentRect.width - 16f, 20f), "没有匹配状态资源。", EditorStyles.miniLabel);
        }
        else
        {
            for (int i = 0; i < filteredList.Count; i++)
            {
                StatusDefinition status = filteredList[i];
                Rect rowRect = new Rect(0f, i * 24f, contentRect.width, 22f);
                DrawStatusRowInScroll(rowRect, status, selectedStatusDefinition == status, localMouse);
            }
        }

        GUI.EndScrollView();
    }

    public override void OnGUIRight()
    {
        if (selectedStatusDefinition == null)
        {
            EditorGUILayout.HelpBox("请先在左侧选择一个状态定义。", MessageType.Info);
            return;
        }

        EnsureSelectedSerializedObject();
        selectedSO.Update();

        DrawHeader();
        GUILayout.Space(6f);

        DrawFoldoutSection("基础信息", DrawBasicSection);
        DrawFoldoutSection("多语言名称", DrawLocalizedNamesSection);
        DrawFoldoutSection("多语言描述", DrawLocalizedDescriptionsSection);
        DrawFoldoutSection("赋予方式", DrawGrantSection);
        DrawFoldoutSection("持续时间", DrawDurationSection);
        DrawFoldoutSection("叠层规则", DrawStackSection);
        DrawFoldoutSection("基础属性修正", DrawAttributeModifierSection);
        DrawFoldoutSection("DOT设定", DrawDotSection);
        DrawFoldoutSection("触发器生命周期", DrawTriggerLifecycleSection);
        DrawFoldoutSection("特效与音效", DrawFxSection);
        DrawFoldoutSection("状态描边特效", DrawStatusOutlineSection);
        DrawFoldoutSection("状态效果响应闪烁", DrawStatusFlashSection);

        selectedSO.ApplyModifiedProperties();
        if (GUI.changed)
            EditorUtility.SetDirty(selectedStatusDefinition);
    }

    private void DrawLeftToolbar(Rect rect)
    {
        const float buttonWidth = 60f;
        const float gap = 4f;

        Rect refreshRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
        Rect createRect = new Rect(refreshRect.xMax + gap, rect.y, buttonWidth + 18f, rect.height);
        Rect deleteRect = new Rect(createRect.xMax + gap, rect.y, 60f, rect.height);

        if (GUI.Button(refreshRect, "刷新"))
            Refresh();

        if (GUI.Button(createRect, "新建状态"))
            CreateStatus();

        using (new EditorGUI.DisabledScope(selectedStatusDefinition == null))
        {
            if (GUI.Button(deleteRect, "删除"))
                DeleteSelectedStatus();
        }
    }

    private void DrawStatusRowInScroll(Rect rect, StatusDefinition status, bool selected, Vector2 localMouse)
    {
        bool hover = rect.Contains(localMouse);

        if (selected)
        {
            EditorGUI.DrawRect(rect, selectedRowPurple);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accentPurple);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            SelectStatus(status);

        const float iconSize = 16f;
        const float iconLeft = 8f;
        Rect iconRect = new Rect(rect.x + iconLeft, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize);

        Texture iconTexture = null;
        if (status != null && status.icon != null)
        {
            iconTexture = AssetPreview.GetAssetPreview(status.icon);
            if (iconTexture == null)
                iconTexture = AssetPreview.GetMiniThumbnail(status.icon);
        }

        if (iconTexture != null)
            GUI.DrawTexture(iconRect, iconTexture, ScaleMode.ScaleToFit, true);

        Rect labelRect = new Rect(iconRect.xMax + 8f, rect.y, rect.width - (iconRect.xMax + 12f), rect.height);
        GUIStyle style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = selected ? Color.white : new Color(0.90f, 0.90f, 0.92f, 1f) }
        };

        GUI.Label(labelRect, GetDisplayLabel(status), style);
    }

    private void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("状态工作台", EditorStyles.boldLabel);
        EditorGUILayout.Space(4f);
        DrawReadonlyRow("资源路径", AssetDatabase.GetAssetPath(selectedStatusDefinition));
        DrawReadonlyRow("状态 ID", string.IsNullOrWhiteSpace(selectedStatusDefinition.statusId) ? "-" : selectedStatusDefinition.statusId);
        DrawPropertyRow("显示名称", "displayName");
        EditorGUILayout.Space(4f);
        DrawPingButtons(selectedStatusDefinition);
        EditorGUILayout.EndVertical();
    }

    [Serializable]
    private class RichTextSelectionState
    {
        public int cursorIndex;
        public int selectIndex;
    }

    private const string DescriptionEditorControlName = "SkyPrison_StatusDescriptionEditor";
    private const string BoldIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_20.png";
    private const string ItalicIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_21.png";

    private void DrawBasicSection()
    {
        DrawPropertyRow("状态 ID", "statusId");
        DrawPropertyRow("图标", "icon");
        DrawPropertyRow("是否标准", "isStandard");
        DrawPropertyRow("是否 Buff", "isBuff");
        DrawPropertyRow("隐藏状态", "isHidden");
        DrawPropertyRow("显示在 HUD", "showInHud");
        DrawPropertyRow("备注", "note", true);
        DrawReadonlyMultiline("主语言描述", selectedStatusDefinition != null ? (selectedStatusDefinition.description ?? "") : "");
        EditorGUILayout.HelpBox("多语言开启时，请在“多语言描述”里打开专用富文本编辑器进行描述编辑与染色。默认说明会跟随默认语言同步。", MessageType.None);
    }
    private void DrawReadonlyMultiline(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel(
            string.IsNullOrWhiteSpace(value) ? "-" : value,
            GUILayout.MinHeight(54f));
        EditorGUILayout.EndHorizontal();
    }
    private void DrawLocalizedNamesSection()
    {
        SerializedProperty prop = selectedSO.FindProperty("localizedNames");
        DrawLocalizedTextList(prop, false, true);
    }

    private void DrawLocalizedDescriptionsSection()
    {
        SerializedProperty prop = selectedSO.FindProperty("localizedDescriptions");
        DrawLocalizedDescriptionRichTextList(prop);
    }

    private void DrawGrantSection()
    {
        DrawPropertyRow("赋予方式", "grantMode");

        SerializedProperty grantModeProp = selectedSO.FindProperty("grantMode");
        if (grantModeProp == null)
            return;

        StatusGrantMode grantMode = (StatusGrantMode)grantModeProp.enumValueIndex;
        if (grantMode == StatusGrantMode.ByAccumulationThreshold)
        {
            EnsureAccumulationSourceKey();
            DrawReadonlyRow("累计来源 Key", GetAccumulationSourceKey());
            DrawPropertyRow("累计阈值", "accumulationThreshold");
        }
    }

    private void DrawDurationSection()
    {
        SerializedProperty grantModeProp = selectedSO.FindProperty("grantMode");
        if (grantModeProp != null)
        {
            StatusGrantMode grantMode = (StatusGrantMode)grantModeProp.enumValueIndex;
            if (grantMode == StatusGrantMode.PersistentPassive || grantMode == StatusGrantMode.UnlockedByProgression)
            {
                EditorGUILayout.HelpBox("当前状态为常驻被动 / 解锁常驻型状态，不使用持续时间配置。", MessageType.Info);
                return;
            }
        }

        DrawPropertyRow("持续类型", "durationType");
        DrawPropertyRow("基础持续时间", "baseDuration");
        DrawPropertyRow("持续时间更新", "durationUpdateMode");
        DrawPropertyRow("最大持续时间", "maxDuration");
    }

    private void DrawStackSection()
    {
        DrawPropertyRow("允许叠层", "canStack");
        DrawPropertyRow("基础层数", "baseStack");
        DrawPropertyRow("最大层数", "maxStack");
        DrawPropertyRow("层数更新方式", "stackUpdateMode");
    }

    private void DrawAttributeModifierSection()
    {
        SerializedProperty listProp = selectedSO.FindProperty("attributeModifiers");
        if (listProp == null)
        {
            EditorGUILayout.HelpBox("当前状态资源中还没有 attributeModifiers 字段。", MessageType.Warning);
            return;
        }

        HandleModifierShortcuts(listProp);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ 添加属性修正", GUILayout.Height(24f)))
            {
                StatusAttributeModifierPickerWindow.Open(
                    GetCoreAttributeDefinitions(),
                    existing =>
                    {
                        AddAttributeModifierFromDefinition(listProp, existing);
                        selectedSO.ApplyModifiedProperties();
                        EditorUtility.SetDirty(selectedStatusDefinition);
                    },
                    GetUsedModifierKeys(listProp));
            }

            using (new EditorGUI.DisabledScope(selectedModifierIndices.Count == 0))
            {
                if (GUILayout.Button("- 删除选中", GUILayout.Width(88f), GUILayout.Height(24f)))
                    DeleteSelectedModifiers(listProp);

                if (GUILayout.Button("复制", GUILayout.Width(60f), GUILayout.Height(24f)))
                    CopySelectedModifiers(listProp);

                if (GUILayout.Button("粘贴", GUILayout.Width(60f), GUILayout.Height(24f)))
                    PasteModifiers(listProp);

                if (GUILayout.Button("复制行", GUILayout.Width(72f), GUILayout.Height(24f)))
                    DuplicateSelectedModifiers(listProp);
            }
        }

        EditorGUILayout.HelpBox(
            "这里用于配置状态存在期间对核心属性的基础修正。\n" +
            "支持 + / × / = 三种方式。层数不展开为多张表，而是通过“叠层规则”参与计算。",
            MessageType.Info);

        Rect outerRect = GUILayoutUtility.GetRect(0f, 10000f, 220f, 220f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(outerRect, listContainerBg);
        DrawThinBorder(outerRect, new Color(1f, 1f, 1f, 0.08f));

        Rect viewRect = new Rect(outerRect.x + 6f, outerRect.y + 6f, outerRect.width - 12f, outerRect.height - 12f);
        float rowHeight = 74f;
        float contentHeight = Mathf.Max(viewRect.height, listProp.arraySize * rowHeight + 4f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        modifierScroll = GUI.BeginScrollView(viewRect, modifierScroll, contentRect, false, true);

        if (listProp.arraySize == 0)
        {
            GUI.Label(new Rect(8f, 8f, contentRect.width - 16f, 20f), "当前没有基础属性修正。", EditorStyles.miniLabel);
        }
        else
        {
            for (int i = 0; i < listProp.arraySize; i++)
            {
                Rect rowRect = new Rect(0f, i * rowHeight, contentRect.width, rowHeight - 2f);
                DrawAttributeModifierRow(rowRect, listProp, i);
            }
        }

        GUI.EndScrollView();

        EditorGUILayout.LabelField("快捷键：Ctrl+C / Ctrl+V / Ctrl+D / Ctrl+A / Delete", EditorStyles.miniLabel);
    }


    private void DrawDotSection()
    {
        DrawPropertyRow("启用 DOT", "enableDot");

        SerializedProperty enableProp = selectedSO.FindProperty("enableDot");
        if (enableProp == null || !enableProp.boolValue)
        {
            EditorGUILayout.HelpBox("启用后可配置持续伤害的 Tick 间隔、扣除目标与数值来源。", MessageType.Info);
            return;
        }

        DrawPropertyRow("Tick 间隔(秒)", "dotTickInterval");
        DrawPropertyRow("扣除目标", "dotTargetResource");
        DrawPropertyRow("DOT 数值模式", "dotValueMode");

        SerializedProperty valueModeProp = selectedSO.FindProperty("dotValueMode");
        StatusDotValueMode mode = valueModeProp != null
            ? (StatusDotValueMode)valueModeProp.enumValueIndex
            : StatusDotValueMode.Fixed;

        switch (mode)
        {
            case StatusDotValueMode.Fixed:
                DrawPropertyRow("基础 DOT 数值", "dotBaseValue");
                break;

            case StatusDotValueMode.TargetMaxResourcePercent:
            case StatusDotValueMode.TargetCurrentResourcePercent:
                DrawPropertyRow("百分比数值", "dotPercentValue");
                break;

            case StatusDotValueMode.OwnerAttributeRatio:
            case StatusDotValueMode.TargetAttributeRatio:
                DrawDotAttributeReferenceRow();
                DrawPropertyRow("属性比例系数", "dotAttributeRatio");
                break;
        }

        DrawPropertyRow("叠层方式", "dotStackMode");

        SerializedProperty stackModeProp = selectedSO.FindProperty("dotStackMode");
        StatusDotStackMode stackMode = stackModeProp != null
            ? (StatusDotStackMode)stackModeProp.enumValueIndex
            : StatusDotStackMode.None;

        if (stackMode == StatusDotStackMode.LinearAdd)
            DrawPropertyRow("每层增量", "dotStackAddValue");

        DrawPropertyRow("吃抗性", "dotAffectedByResistance");
        DrawPropertyRow("可致死", "dotCanKill");
        DrawPropertyRow("伤害类型 Key", "dotDamageTypeKey");
        DrawPropertyRow("伤害类型显示名", "dotDamageTypeDisplayName");

        EditorGUILayout.HelpBox(GetDotPreviewText(), MessageType.None);
    }

    private void DrawDotAttributeReferenceRow()
    {
        SerializedProperty keyProp = selectedSO.FindProperty("dotReferenceAttributeKey");
        SerializedProperty nameProp = selectedSO.FindProperty("dotReferenceAttributeDisplayName");
        SerializedProperty typeProp = selectedSO.FindProperty("dotReferenceAttributeValueType");

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("参考属性", GUILayout.Width(140f));

        string label = string.IsNullOrWhiteSpace(nameProp != null ? nameProp.stringValue : "")
            ? (keyProp != null ? keyProp.stringValue : "")
            : $"{nameProp.stringValue} ({keyProp.stringValue})";

        EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(label) ? "未选择" : label, GUILayout.Height(EditorGUIUtility.singleLineHeight));

        if (GUILayout.Button("选择", GUILayout.Width(60f)))
        {
            StatusAttributeModifierPickerWindow.Open(
                GetCoreAttributeDefinitions(),
                picked =>
                {
                    if (picked == null)
                        return;

                    selectedSO.Update();
                    if (keyProp != null) keyProp.stringValue = picked.key ?? "";
                    if (nameProp != null) nameProp.stringValue = picked.displayName ?? "";
                    if (typeProp != null) typeProp.enumValueIndex = (int)picked.valueType;
                    selectedSO.ApplyModifiedProperties();
                    EditorUtility.SetDirty(selectedStatusDefinition);
                },
                new HashSet<string>());
        }

        if (GUILayout.Button("清空", GUILayout.Width(60f)))
        {
            if (keyProp != null) keyProp.stringValue = "";
            if (nameProp != null) nameProp.stringValue = "";
            if (typeProp != null) typeProp.enumValueIndex = (int)BattleValueType.Float;
        }

        EditorGUILayout.EndHorizontal();
    }

    private string GetDotPreviewText()
    {
        float interval = Mathf.Max(0.01f, selectedSO.FindProperty("dotTickInterval") != null ? selectedSO.FindProperty("dotTickInterval").floatValue : 1f);
        StatusDotTargetResource target = selectedSO.FindProperty("dotTargetResource") != null
            ? (StatusDotTargetResource)selectedSO.FindProperty("dotTargetResource").enumValueIndex
            : StatusDotTargetResource.HP;
        StatusDotValueMode mode = selectedSO.FindProperty("dotValueMode") != null
            ? (StatusDotValueMode)selectedSO.FindProperty("dotValueMode").enumValueIndex
            : StatusDotValueMode.Fixed;

        string targetText = target == StatusDotTargetResource.HP ? "HP" : "LP";
        string valueText = "";

        switch (mode)
        {
            case StatusDotValueMode.Fixed:
                valueText = $"固定 {selectedSO.FindProperty("dotBaseValue").floatValue:0.###}";
                break;
            case StatusDotValueMode.TargetMaxResourcePercent:
                valueText = $"目标最大{targetText}的 {selectedSO.FindProperty("dotPercentValue").floatValue:0.###}%";
                break;
            case StatusDotValueMode.TargetCurrentResourcePercent:
                valueText = $"目标当前{targetText}的 {selectedSO.FindProperty("dotPercentValue").floatValue:0.###}%";
                break;
            case StatusDotValueMode.OwnerAttributeRatio:
                valueText = $"施加者属性 {selectedSO.FindProperty("dotReferenceAttributeKey").stringValue} × {selectedSO.FindProperty("dotAttributeRatio").floatValue:0.###}";
                break;
            case StatusDotValueMode.TargetAttributeRatio:
                valueText = $"目标属性 {selectedSO.FindProperty("dotReferenceAttributeKey").stringValue} × {selectedSO.FindProperty("dotAttributeRatio").floatValue:0.###}";
                break;
        }

        StatusDotStackMode stackMode = selectedSO.FindProperty("dotStackMode") != null
            ? (StatusDotStackMode)selectedSO.FindProperty("dotStackMode").enumValueIndex
            : StatusDotStackMode.None;

        string stackText = stackMode == StatusDotStackMode.None ? "不随层数变化" :
            stackMode == StatusDotStackMode.LinearAdd ? $"每层追加 {selectedSO.FindProperty("dotStackAddValue").floatValue:0.###}" : "按层数整体乘算";

        return $"DOT 预览：每 {interval:0.###} 秒扣除目标 {targetText}。\n数值来源：{valueText}\n叠层规则：{stackText}";
    }


    private Texture2D LoadOptionalIcon(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return null;

        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private bool DrawToolbarIconButton(Texture2D icon, string fallbackText, string tooltip, float width = 28f)
    {
        GUIContent content = icon != null
            ? new GUIContent(icon, tooltip)
            : new GUIContent(fallbackText, tooltip);

        return GUILayout.Button(content, GUILayout.Width(width), GUILayout.Height(24f));
    }


    private bool DrawToolbarTextButton(string text, string tooltip, float width)
    {
        Rect rect = GUILayoutUtility.GetRect(width, 24f, GUILayout.Width(width), GUILayout.Height(24f));
        bool hover = rect.Contains(Event.current.mousePosition);

        EditorGUI.DrawRect(rect, hover ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.05f));
        DrawThinBorder(rect, new Color(1f, 1f, 1f, hover ? 0.14f : 0.08f));

        GUI.Label(
            rect,
            new GUIContent(text, tooltip),
            new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.90f, 0.90f, 0.92f, 1f) }
            });

        return GUI.Button(rect, new GUIContent("", tooltip), GUIStyle.none);
    }

    private void DrawRichTextToolbar(string controlName, SerializedProperty descriptionProp)
    {
        EditorGUILayout.BeginHorizontal();

        Texture2D boldIcon = LoadOptionalIcon(BoldIconPath);
        if (DrawToolbarIconButton(boldIcon, "B", "加粗"))
            ToggleWrapSelectedDescriptionText(controlName, descriptionProp, "<b>", "</b>");

        Texture2D italicIcon = LoadOptionalIcon(ItalicIconPath);
        if (DrawToolbarIconButton(italicIcon, "I", "斜体"))
            ToggleWrapSelectedDescriptionText(controlName, descriptionProp, "<i>", "</i>");

        if (GUILayout.Button("清除样式", GUILayout.Width(72f), GUILayout.Height(24f)))
            ClearDescriptionSelectionStyle(controlName, descriptionProp);

        GUILayout.Space(6f);
        GUILayout.Label("快捷色", GUILayout.Width(40f));

        DrawRichTextColorButton(controlName, descriptionProp, new Color(1.00f, 0.40f, 0.35f, 1f), "伤害");
        DrawRichTextColorButton(controlName, descriptionProp, new Color(0.35f, 1.00f, 0.50f, 1f), "回复");
        DrawRichTextColorButton(controlName, descriptionProp, new Color(1.00f, 0.55f, 0.25f, 1f), "灼热");
        DrawRichTextColorButton(controlName, descriptionProp, new Color(0.45f, 0.80f, 1.00f, 1f), "电磁");
        DrawRichTextColorButton(controlName, descriptionProp, new Color(0.50f, 1.00f, 0.60f, 1f), "腐蚀");
        DrawRichTextColorButton(controlName, descriptionProp, new Color(0.72f, 0.92f, 1.00f, 1f), "冻结");
        DrawRichTextColorButton(controlName, descriptionProp, new Color(1.00f, 0.92f, 0.35f, 1f), "高亮");

        GUILayout.Space(6f);

        richTextCustomColor = EditorGUILayout.ColorField(GUIContent.none, richTextCustomColor, false, true, false, GUILayout.Width(42f));
        if (GUILayout.Button("应用颜色", GUILayout.Width(72f), GUILayout.Height(22f)))
            ApplyColorToSelectedDescriptionText(controlName, descriptionProp, richTextCustomColor);

        EditorGUILayout.EndHorizontal();
    }

    private void DrawRichTextColorButton(string controlName, SerializedProperty descriptionProp, Color color, string tooltip)
    {
        Color previous = GUI.backgroundColor;
        GUI.backgroundColor = color;
        if (GUILayout.Button(new GUIContent(" ", tooltip), GUILayout.Width(22f), GUILayout.Height(18f)))
            ApplyColorToSelectedDescriptionText(controlName, descriptionProp, color);
        GUI.backgroundColor = previous;
    }


    private void ApplyColorToSelectedDescriptionText(string controlName, SerializedProperty descriptionProp, Color color)
    {
        string source = descriptionProp.stringValue ?? "";
        RichTextSelectionState state;
        int start;
        int end;
        if (!TryGetSelectionRange(controlName, source, out state, out start, out end))
            return;

        if (start == end)
            return;

        string selected = source.Substring(start, end - start);
        selected = StripColorTags(selected);

        string prefix = $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>";
        string suffix = "</color>";

        descriptionProp.stringValue = source.Substring(0, start) + prefix + selected + suffix + source.Substring(end);
        state.cursorIndex = start + prefix.Length + selected.Length + suffix.Length;
        state.selectIndex = start + prefix.Length;
        richTextSelections[controlName] = state;
        GUI.changed = true;
    }


    private void DrawDescriptionPreview(string text)
    {
        GUIStyle previewStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
        {
            richText = true
        };

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("描述预览", EditorStyles.miniBoldLabel);
        GUILayout.Space(2f);

        string previewText = string.IsNullOrWhiteSpace(text)
            ? "（暂无描述）"
            : text;

        float height = previewStyle.CalcHeight(new GUIContent(previewText), Mathf.Max(100f, EditorGUIUtility.currentViewWidth - 120f));
        GUILayout.Label(previewText, previewStyle, GUILayout.MinHeight(Mathf.Max(42f, height)));
        EditorGUILayout.EndVertical();
    }

    private bool TryGetSelectionRange(string controlName, string source, out RichTextSelectionState state, out int start, out int end)
    {
        state = null;
        start = 0;
        end = 0;

        if (!richTextSelections.TryGetValue(controlName, out state) || source == null)
            return false;

        start = Mathf.Min(state.cursorIndex, state.selectIndex);
        end = Mathf.Max(state.cursorIndex, state.selectIndex);
        start = Mathf.Clamp(start, 0, source.Length);
        end = Mathf.Clamp(end, 0, source.Length);
        return true;
    }

    private void ToggleWrapSelectedDescriptionText(string controlName, SerializedProperty descriptionProp, string prefix, string suffix)
    {
        string source = descriptionProp.stringValue ?? "";
        RichTextSelectionState state;
        int start;
        int end;
        if (!TryGetSelectionRange(controlName, source, out state, out start, out end))
        {
            descriptionProp.stringValue = prefix + source + suffix;
            GUI.changed = true;
            return;
        }

        if (start == end)
        {
            if (!string.IsNullOrEmpty(source) && source.StartsWith(prefix) && source.EndsWith(suffix))
            {
                descriptionProp.stringValue = source.Substring(prefix.Length, source.Length - prefix.Length - suffix.Length);
                state.cursorIndex = Mathf.Clamp(state.cursorIndex - prefix.Length, 0, descriptionProp.stringValue.Length);
                state.selectIndex = state.cursorIndex;
            }
            else
            {
                descriptionProp.stringValue = prefix + source + suffix;
                state.cursorIndex = descriptionProp.stringValue.Length;
                state.selectIndex = prefix.Length;
            }

            richTextSelections[controlName] = state;
            GUI.changed = true;
            return;
        }

        string selected = source.Substring(start, end - start);
        bool alreadyWrapped =
            start >= prefix.Length &&
            end + suffix.Length <= source.Length &&
            source.Substring(start - prefix.Length, prefix.Length) == prefix &&
            source.Substring(end, suffix.Length) == suffix;

        if (alreadyWrapped)
        {
            descriptionProp.stringValue =
                source.Substring(0, start - prefix.Length) +
                selected +
                source.Substring(end + suffix.Length);

            state.cursorIndex = start - prefix.Length + selected.Length;
            state.selectIndex = start - prefix.Length;
        }
        else
        {
            descriptionProp.stringValue =
                source.Substring(0, start) +
                prefix + selected + suffix +
                source.Substring(end);

            state.cursorIndex = start + prefix.Length + selected.Length + suffix.Length;
            state.selectIndex = start + prefix.Length;
        }

        richTextSelections[controlName] = state;
        GUI.changed = true;
    }

    private void WrapSelectedDescriptionText(string controlName, SerializedProperty descriptionProp, string prefix, string suffix)
    {
        string source = descriptionProp.stringValue ?? "";
        RichTextSelectionState state;
        int start;
        int end;
        if (!TryGetSelectionRange(controlName, source, out state, out start, out end))
        {
            descriptionProp.stringValue = prefix + source + suffix;
            GUI.changed = true;
            return;
        }

        if (start == end)
        {
            descriptionProp.stringValue = prefix + source + suffix;
            state.cursorIndex = descriptionProp.stringValue.Length;
            state.selectIndex = prefix.Length;
        }
        else
        {
            string selected = source.Substring(start, end - start);
            descriptionProp.stringValue = source.Substring(0, start) + prefix + selected + suffix + source.Substring(end);
            state.cursorIndex = start + prefix.Length + selected.Length + suffix.Length;
            state.selectIndex = start + prefix.Length;
        }

        richTextSelections[controlName] = state;
        GUI.changed = true;
    }

    private void ClearDescriptionSelectionStyle(string controlName, SerializedProperty descriptionProp)
    {
        string source = descriptionProp.stringValue ?? "";
        RichTextSelectionState state;
        int start;
        int end;
        if (!TryGetSelectionRange(controlName, source, out state, out start, out end))
        {
            descriptionProp.stringValue = StripRichTextTags(source);
            GUI.changed = true;
            return;
        }

        if (start == end)
        {
            descriptionProp.stringValue = StripRichTextTags(source);
            state.cursorIndex = Mathf.Clamp(state.cursorIndex, 0, descriptionProp.stringValue.Length);
            state.selectIndex = state.cursorIndex;
            richTextSelections[controlName] = state;
            GUI.changed = true;
            return;
        }

        string selected = source.Substring(start, end - start);
        selected = StripRichTextTags(selected);

        descriptionProp.stringValue = source.Substring(0, start) + selected + source.Substring(end);
        state.cursorIndex = start + selected.Length;
        state.selectIndex = start;
        richTextSelections[controlName] = state;
        GUI.changed = true;
    }

    private string StripColorTags(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return System.Text.RegularExpressions.Regex.Replace(value, "</?color.*?>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private string StripRichTextTags(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        value = StripColorTags(value);
        value = System.Text.RegularExpressions.Regex.Replace(value, "</?b>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        value = System.Text.RegularExpressions.Regex.Replace(value, "</?i>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return value;
    }


    private void DrawTriggerLifecycleSection()
    {
        DrawPropertyRow("赋予时触发器", "onApplyTriggerKey");
        DrawPropertyRow("周期触发器", "onTickTriggerKey");
        DrawPropertyRow("周期触发间隔", "tickInterval");
        DrawPropertyRow("被清除时触发器", "onRemoveTriggerKey");
        DrawPropertyRow("结束时触发器", "onExpireTriggerKey");
    }

    private void DrawFxSection()
    {
        DrawPropertyRow("赋予时特效", "onApplyVfxKey");
        DrawPropertyRow("常驻特效", "persistentVfxKey");
        DrawPropertyRow("周期特效", "tickVfxKey");
        DrawPropertyRow("周期特效间隔", "tickVfxInterval");
        DrawPropertyRow("被清除时特效", "onRemoveVfxKey");
        DrawPropertyRow("结束时特效", "onExpireVfxKey");
        DrawPropertyRow("赋予时音效", "onApplySfxKey");
        DrawPropertyRow("被清除时音效", "onRemoveSfxKey");
        DrawPropertyRow("结束时音效", "onExpireSfxKey");
    }

    private void DrawStatusOutlineSection()
    {
        SerializedProperty useOutline = selectedSO.FindProperty("useStatusOutline");
        DrawPropertyRow("状态存在期间持续描边", "useStatusOutline");

        using (new EditorGUI.DisabledScope(useOutline != null && !useOutline.boolValue))
        {
            DrawPropertyRow("描边发光颜色", "statusOutlineColor");
            DrawPropertyRow("描边宽度（像素）", "statusOutlineWidthPixels");
            DrawPropertyRow("粗细/明暗变化幅度", "statusOutlineWidthVariance");
            DrawPropertyRow("流动速度", "statusOutlineFlowSpeed");
            DrawPropertyRow("噪波密度", "statusOutlineNoiseScale");
            DrawPropertyRow("淡入时长（秒）", "statusOutlineFadeInSeconds");
            DrawPropertyRow("淡出时长（秒）", "statusOutlineFadeOutSeconds");
        }

        EditorGUILayout.HelpBox("描的是角色整体外轮廓（跟遮挡描边同一套屏幕空间轮廓蒙版算法），不是逐部件贴图边缘。配合Bloom后处理会有向外扩散的光晕感。", MessageType.None);
    }

    private void DrawStatusFlashSection()
    {
        SerializedProperty useFlash = selectedSO.FindProperty("useStatusFlash");
        DrawPropertyRow("DOT跳伤害时全身呼吸闪烁", "useStatusFlash");

        using (new EditorGUI.DisabledScope(useFlash != null && !useFlash.boolValue))
        {
            DrawPropertyRow("闪烁色调", "statusFlashColor");
            DrawPropertyRow("闪烁时长（秒）", "statusFlashDuration");
            DrawPropertyRow("峰值半透明程度", "statusFlashAlphaDip");
        }

        EditorGUILayout.HelpBox("只在这个状态真的触发DOT tick（跳一次伤害）那一帧起播，全身强度按sin曲线0→1→0起伏一次，不是持续常亮。颜色是乘色调（染色），会保留角色本身的明暗细节，不是拿纯色盖上去——白色(1,1,1)等于不生效，想偏红就把G/B通道调低。", MessageType.None);
    }


    private void DrawLocalizedDescriptionRichTextList(SerializedProperty listProp)
    {
        if (listProp == null)
        {
            EditorGUILayout.HelpBox("找不到多语言描述字段。", MessageType.Warning);
            return;
        }

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        if (settings == null)
        {
            EditorGUILayout.HelpBox("未找到 LocalizationProjectSettings。", MessageType.Warning);
            return;
        }

        EnsureLocalizedEntries(listProp, settings);

        List<LocalizationProjectSettings.LanguageEntry> orderedLanguages = GetOrderedLanguages(settings);
        string defaultLanguageCode = GetDefaultLanguageCode(settings);

        EditorGUILayout.HelpBox("点击“打开富文本编辑器”后，在弹窗里进行局部选取、染色、加粗和斜体编辑。默认语言会同步到“默认说明”。", MessageType.None);

        for (int i = 0; i < orderedLanguages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = orderedLanguages[i];
            SerializedProperty entryProp = FindLocalizedEntry(listProp, lang.languageCode);
            if (entryProp == null)
                continue;

            SerializedProperty textProp = entryProp.FindPropertyRelative("text");
            string label = string.IsNullOrWhiteSpace(lang.displayName) ? lang.languageCode : lang.displayName;
            if (lang.isDefault)
                label += "（默认）";

            DrawLocalizedDescriptionEditorRow(lang.languageCode, label, textProp);
        }

        SerializedProperty defaultEntry = FindLocalizedEntry(listProp, defaultLanguageCode);
        if (defaultEntry != null)
        {
            SerializedProperty defaultTextProp = defaultEntry.FindPropertyRelative("text");
            SerializedProperty descriptionProp = selectedSO.FindProperty("description");
            if (defaultTextProp != null && descriptionProp != null && descriptionProp.stringValue != (defaultTextProp.stringValue ?? ""))
                descriptionProp.stringValue = defaultTextProp.stringValue ?? "";
        }
    }

    private void DrawLocalizedDescriptionEditorRow(string languageCode, string label, SerializedProperty textProp)
    {
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        bool requestOpenRichText = GUILayout.Button("打开富文本编辑器", GUILayout.Width(140f), GUILayout.Height(24f));
        EditorGUILayout.EndHorizontal();

        string previewText = textProp != null && !string.IsNullOrWhiteSpace(textProp.stringValue)
            ? textProp.stringValue
            : "（暂无描述）";

        DrawRichTextPreview(previewText);

        EditorGUILayout.EndVertical();
        GUILayout.Space(4f);

        if (requestOpenRichText)
        {
            string openLabel = label;
            string openLang = languageCode;
            string openCurrent = textProp != null ? (textProp.stringValue ?? "") : "";
            EditorApplication.delayCall += () =>
            {
                SkyPrisonRichTextEditorWindow.Open(
                    openLabel,
                    openCurrent,
                    updated =>
                    {
                        if (selectedSO == null)
                            return;
                        selectedSO.Update();
                        SerializedProperty localizedList = selectedSO.FindProperty("localizedDescriptions");
                        SerializedProperty entry = FindLocalizedEntry(localizedList, openLang);
                        if (entry != null)
                        {
                            SerializedProperty text = entry.FindPropertyRelative("text");
                            if (text != null)
                                text.stringValue = updated ?? "";
                        }
                        selectedSO.ApplyModifiedProperties();
                        EditorUtility.SetDirty(selectedStatusDefinition);
                    },
                    "status");
            };
            GUIUtility.ExitGUI();
        }
    }

    private struct PreviewCharStyle
    {
        public char character;
        public bool bold;
        public bool italic;
        public bool underline;
        public bool hasColor;
        public Color color;
    }

    private float GetPreviewCompactCharWidth(char c, GUIStyle style)
    {
        float width = style.CalcSize(new GUIContent(c.ToString())).x;
        width *= 0.91f;
        if (c < 127)
        {
            width *= 0.80f;
            switch (c)
            {
                case 'i':
                case 'l':
                case 'I':
                case '1':
                    width *= 0.34f;
                    break;
                case '|':
                case '!':
                case '\'':
                case '`':
                    width *= 0.40f;
                    break;
                case '.':
                case ',':
                case ';':
                case ':':
                    width *= 0.40f;
                    break;
                case 'f':
                case 't':
                case 'r':
                case 'j':
                    width *= 0.56f;
                    break;
            }
        }
        return Mathf.Max(4f, Mathf.Round(width));
    }

    private float GetPreviewCharDrawOffset(char c)
    {
        switch (c)
        {
            case 'i':
            case 'l':
            case 'I':
            case '1':
                return 1.15f;
            case '|':
            case '!':
            case '\'':
            case '`':
                return 0.75f;
            case 'f':
            case 't':
            case 'r':
            case 'j':
                return 0.4f;
            default:
                return 0f;
        }
    }

    private void DrawRichTextPreview(string richText)
    {
        List<PreviewCharStyle> chars = ParseRichTextPreview(richText ?? string.Empty);
        Rect rect = GUILayoutUtility.GetRect(0f, 10000f, 44f, 56f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.02f));

        float x = rect.x + 8f;
        float y = rect.y + 8f;
        float maxX = rect.xMax - 8f;
        float lineHeight = 20f;

        bool underlineOpen = false;
        float underlineStartX = 0f;
        float underlineEndX = 0f;
        Color underlineColor = Color.white;
        float underlineY = 0f;

        for (int i = 0; i < chars.Count; i++)
        {
            PreviewCharStyle c = chars[i];
            if (c.character == '\n')
            {
                if (underlineOpen)
                {
                    EditorGUI.DrawRect(new Rect(underlineStartX, underlineY, Mathf.Max(1f, underlineEndX - underlineStartX), 1f), underlineColor);
                    underlineOpen = false;
                }
                x = rect.x + 8f;
                y += lineHeight;
                continue;
            }

            GUIStyle style = new GUIStyle(EditorStyles.label);
            style.fontStyle = c.bold && c.italic ? FontStyle.BoldAndItalic : c.bold ? FontStyle.Bold : c.italic ? FontStyle.Italic : FontStyle.Normal;
            style.normal.textColor = c.hasColor ? c.color : new Color(0.86f, 0.86f, 0.88f, 1f);

            float width = GetPreviewCompactCharWidth(c.character, style);
            if (x + width > maxX)
            {
                if (underlineOpen)
                {
                    EditorGUI.DrawRect(new Rect(underlineStartX, underlineY, Mathf.Max(1f, underlineEndX - underlineStartX), 1f), underlineColor);
                    underlineOpen = false;
                }
                x = rect.x + 8f;
                y += lineHeight;
            }

            Rect charRect = new Rect(x, y, width, lineHeight);
            float drawOffset = GetPreviewCharDrawOffset(c.character);
            Rect drawRect = new Rect(charRect.x + drawOffset, charRect.y, charRect.width - drawOffset + 1f, charRect.height);
            GUI.Label(drawRect, c.character.ToString(), style);

            if (c.underline)
            {
                EditorGUI.DrawRect(new Rect(charRect.x, y + lineHeight - 3f, Mathf.Max(1f, charRect.width + 0.25f), 1f), style.normal.textColor);
            }

            x += width;
        }

    }

    private List<PreviewCharStyle> ParseRichTextPreview(string input)
    {
        List<PreviewCharStyle> result = new List<PreviewCharStyle>();
        bool bold = false;
        bool italic = false;
        bool underline = false;
        bool hasColor = false;
        Color color = Color.white;

        for (int i = 0; i < input.Length;)
        {
            if (input[i] == '<')
            {
                int close = input.IndexOf('>', i);
                if (close > i)
                {
                    string tag = input.Substring(i, close - i + 1);
                    string lower = tag.ToLowerInvariant();
                    if (lower == "<b>") { bold = true; i = close + 1; continue; }
                    if (lower == "</b>") { bold = false; i = close + 1; continue; }
                    if (lower == "<i>") { italic = true; i = close + 1; continue; }
                    if (lower == "</i>") { italic = false; i = close + 1; continue; }
                    if (lower == "<u>") { underline = true; i = close + 1; continue; }
                    if (lower == "</u>") { underline = false; i = close + 1; continue; }
                    if (lower.StartsWith("<color=#") && lower.EndsWith(">"))
                    {
                        string hex = tag.Substring(8, tag.Length - 9);
                        Color parsed;
                        if (ColorUtility.TryParseHtmlString("#" + hex, out parsed))
                        {
                            hasColor = true;
                            color = parsed;
                        }
                        i = close + 1;
                        continue;
                    }
                    if (lower == "</color>") { hasColor = false; color = Color.white; i = close + 1; continue; }
                }
            }

            char c = input[i];
            if (c == '&')
            {
                if (input.IndexOf("&lt;", i, StringComparison.Ordinal) == i) { c = '<'; i += 4; }
                else if (input.IndexOf("&gt;", i, StringComparison.Ordinal) == i) { c = '>'; i += 4; }
                else if (input.IndexOf("&amp;", i, StringComparison.Ordinal) == i) { c = '&'; i += 5; }
                else { i++; }
            }
            else
            {
                i++;
            }

            PreviewCharStyle style;
            style.character = c;
            style.bold = bold;
            style.italic = italic;
            style.underline = underline;
            style.hasColor = hasColor;
            style.color = color;
            result.Add(style);
        }

        return result;
    }

    private bool ApproximatelySameColor(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.01f
               && Mathf.Abs(a.g - b.g) < 0.01f
               && Mathf.Abs(a.b - b.b) < 0.01f
               && Mathf.Abs(a.a - b.a) < 0.01f;
    }

    private void CacheRichTextSelection(string controlName)
    {
        if (GUI.GetNameOfFocusedControl() != controlName)
            return;

        TextEditor editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
        if (editor == null)
            return;

        RichTextSelectionState state;
        if (!richTextSelections.TryGetValue(controlName, out state))
        {
            state = new RichTextSelectionState();
            richTextSelections[controlName] = state;
        }

        state.cursorIndex = editor.cursorIndex;
        state.selectIndex = editor.selectIndex;
    }


    private void DrawLocalizedTextList(SerializedProperty listProp, bool multiline, bool syncToDisplayName)
    {
        if (listProp == null)
        {
            EditorGUILayout.HelpBox("找不到多语言字段。", MessageType.Warning);
            return;
        }

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        if (settings == null)
        {
            EditorGUILayout.HelpBox("未找到 LocalizationProjectSettings。", MessageType.Warning);
            return;
        }

        EnsureLocalizedEntries(listProp, settings);

        List<LocalizationProjectSettings.LanguageEntry> orderedLanguages = GetOrderedLanguages(settings);
        string defaultLanguageCode = GetDefaultLanguageCode(settings);

        for (int i = 0; i < orderedLanguages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = orderedLanguages[i];
            SerializedProperty entryProp = FindLocalizedEntry(listProp, lang.languageCode);
            if (entryProp == null)
                continue;

            SerializedProperty textProp = entryProp.FindPropertyRelative("text");
            string label = string.IsNullOrWhiteSpace(lang.displayName) ? lang.languageCode : lang.displayName;
            if (lang.isDefault)
                label += "（默认）";

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140f));

            if (multiline)
                textProp.stringValue = EditorGUILayout.TextArea(textProp.stringValue ?? "", GUILayout.MinHeight(54f));
            else
                textProp.stringValue = EditorGUILayout.TextField(textProp.stringValue ?? "");

            EditorGUILayout.EndHorizontal();
        }

        SerializedProperty defaultEntry = FindLocalizedEntry(listProp, defaultLanguageCode);
        if (defaultEntry != null)
        {
            SerializedProperty defaultTextProp = defaultEntry.FindPropertyRelative("text");
            string defaultText = defaultTextProp != null ? defaultTextProp.stringValue ?? "" : "";

            if (syncToDisplayName)
            {
                SerializedProperty displayNameProp = selectedSO.FindProperty("displayName");
                if (displayNameProp != null && displayNameProp.stringValue != defaultText)
                    displayNameProp.stringValue = defaultText;
            }
            else
            {
                SerializedProperty descriptionProp = selectedSO.FindProperty("description");
                if (descriptionProp != null && descriptionProp.stringValue != defaultText)
                    descriptionProp.stringValue = defaultText;
            }
        }
    }

    private void EnsureLocalizedEntries(SerializedProperty listProp, LocalizationProjectSettings settings)
    {
        HashSet<string> existing = new HashSet<string>();

        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty item = listProp.GetArrayElementAtIndex(i);
            SerializedProperty codeProp = item.FindPropertyRelative("languageCode");
            if (codeProp != null && !string.IsNullOrWhiteSpace(codeProp.stringValue))
                existing.Add(codeProp.stringValue);
        }

        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang == null || !lang.enabled)
                continue;

            if (existing.Contains(lang.languageCode))
                continue;

            int index = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(index);

            SerializedProperty newItem = listProp.GetArrayElementAtIndex(index);
            SerializedProperty codeProp = newItem.FindPropertyRelative("languageCode");
            SerializedProperty textProp = newItem.FindPropertyRelative("text");

            if (codeProp != null)
                codeProp.stringValue = lang.languageCode;

            if (textProp != null)
                textProp.stringValue = "";

            existing.Add(lang.languageCode);
        }
    }

    private SerializedProperty FindLocalizedEntry(SerializedProperty listProp, string languageCode)
    {
        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty item = listProp.GetArrayElementAtIndex(i);
            SerializedProperty codeProp = item.FindPropertyRelative("languageCode");
            if (codeProp != null && codeProp.stringValue == languageCode)
                return item;
        }

        return null;
    }

    private List<LocalizationProjectSettings.LanguageEntry> GetOrderedLanguages(LocalizationProjectSettings settings)
    {
        List<LocalizationProjectSettings.LanguageEntry> result = new List<LocalizationProjectSettings.LanguageEntry>();
        if (settings == null || settings.languages == null)
            return result;

        LocalizationProjectSettings.LanguageEntry defaultLang = null;

        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang == null || !lang.enabled)
                continue;

            if (lang.isDefault)
                defaultLang = lang;
        }

        if (defaultLang != null)
            result.Add(defaultLang);

        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang == null || !lang.enabled)
                continue;

            if (defaultLang != null && lang.languageCode == defaultLang.languageCode)
                continue;

            result.Add(lang);
        }

        return result;
    }

    private string GetDefaultLanguageCode(LocalizationProjectSettings settings)
    {
        if (settings == null || settings.languages == null || settings.languages.Count == 0)
            return "zh-CN";

        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang != null && lang.enabled && lang.isDefault)
                return lang.languageCode;
        }

        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang != null && lang.enabled)
                return lang.languageCode;
        }

        return "zh-CN";
    }

    private void EnsureAccumulationSourceKey()
    {
        if (selectedSO == null)
            return;

        SerializedProperty keyProp = selectedSO.FindProperty("accumulationSourceKey");
        if (keyProp == null)
            return;

        string desired = GetAccumulationSourceKey();
        if (keyProp.stringValue != desired)
            keyProp.stringValue = desired;
    }

    private string GetAccumulationSourceKey()
    {
        string statusId = selectedStatusDefinition != null ? selectedStatusDefinition.statusId : "";
        string safeId = SanitizeKey(statusId);
        if (string.IsNullOrWhiteSpace(safeId))
            safeId = "new_status";

        string baseKey = $"accum_{safeId}";
        HashSet<string> used = new HashSet<string>();
        string selectedPath = selectedStatusDefinition != null ? AssetDatabase.GetAssetPath(selectedStatusDefinition) : "";

        string[] guids = AssetDatabase.FindAssets("t:StatusDefinition");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!string.IsNullOrWhiteSpace(selectedPath) && path == selectedPath)
                continue;

            StatusDefinition other = AssetDatabase.LoadAssetAtPath<StatusDefinition>(path);
            if (other == null)
                continue;

            if (other.grantMode != StatusGrantMode.ByAccumulationThreshold)
                continue;

            string otherKey = SanitizeKey(other.accumulationSourceKey);
            if (!string.IsNullOrWhiteSpace(otherKey))
                used.Add(otherKey);
        }

        if (!used.Contains(baseKey))
            return baseKey;

        int suffix = 1;
        while (used.Contains($"{baseKey}_{suffix}"))
            suffix++;

        return $"{baseKey}_{suffix}";
    }

    private string SanitizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string lower = value.Trim().ToLowerInvariant();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < lower.Length; i++)
        {
            char c = lower[i];
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
                sb.Append(c);
            else if (c == ' ' || c == '-')
                sb.Append('_');
        }
        return sb.ToString();
    }

    private void DrawStatusRow(Rect rect, StatusDefinition status, bool selected)
    {
        bool hover = rect.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, selectedRowPurple);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accentPurple);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            SelectStatus(status);

        GUIStyle style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 6, 0, 0),
            normal = { textColor = selected ? Color.white : new Color(0.90f, 0.90f, 0.92f, 1f) }
        };

        GUI.Label(rect, GetDisplayLabel(status), style);
    }

    private string GetDisplayLabel(StatusDefinition status)
    {
        if (status == null)
            return "(空状态)";

        string localized = GetLocalizedText(status.localizedNames, GetDefaultLanguageCode(LocalizationSettingsUtility.GetOrCreateSettings()));
        if (!string.IsNullOrWhiteSpace(localized))
        {
            if (!string.IsNullOrWhiteSpace(status.statusId))
                return $"{localized}  ({status.statusId})";
            return localized;
        }

        if (!string.IsNullOrWhiteSpace(status.statusId))
            return string.IsNullOrWhiteSpace(status.displayName) ? status.statusId : $"{status.displayName}  ({status.statusId})";

        return string.IsNullOrWhiteSpace(status.displayName) ? status.name : status.displayName;
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

    private void SelectStatus(StatusDefinition status)
    {
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;

        if (selectedSO != null)
        {
            selectedSO.ApplyModifiedProperties();
            selectedSO = null;
        }

        selectedModifierIndices.Clear();
        lastModifierSelectionIndex = -1;

        selectedStatusDefinition = status;
        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    private void EnsureSelectedSerializedObject()
    {
        if (selectedStatusDefinition == null)
            return;

        if (selectedSO == null || selectedSO.targetObject != selectedStatusDefinition)
        {
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;
            selectedSO = new SerializedObject(selectedStatusDefinition);
        }
    }

    private void CreateStatus()
    {
        EnsureFolderExists(DefaultCreateFolder);
        StatusDefinition asset = ScriptableObject.CreateInstance<StatusDefinition>();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{DefaultCreateFolder}/StatusDefinition.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Refresh();
        SelectStatus(AssetDatabase.LoadAssetAtPath<StatusDefinition>(path));
    }

    private void DeleteSelectedStatus()
    {
        if (selectedStatusDefinition == null)
            return;

        string path = AssetDatabase.GetAssetPath(selectedStatusDefinition);
        if (string.IsNullOrEmpty(path))
            return;

        bool ok = EditorUtility.DisplayDialog(
            "删除状态",
            $"确定删除当前状态资源吗？\n{GetDisplayLabel(selectedStatusDefinition)}",
            "删除",
            "取消");

        if (!ok)
            return;

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        selectedStatusDefinition = null;
        selectedSO = null;
        Refresh();
    }

    private void DrawPropertyRow(string label, string propertyPath, bool multiline = false)
    {
        SerializedProperty prop = selectedSO.FindProperty(propertyPath);
        if (prop == null)
            return;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));

        if (multiline && prop.propertyType == SerializedPropertyType.String)
            prop.stringValue = EditorGUILayout.TextArea(prop.stringValue, GUILayout.MinHeight(48f));
        else
            EditorGUILayout.PropertyField(prop, GUIContent.none, true);

        EditorGUILayout.EndHorizontal();
    }

    private void DrawReadonlyRow(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(value) ? "-" : value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPingButtons(UnityEngine.Object target)
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("在 Project 中定位", GUILayout.Height(24f)))
        {
            EditorGUIUtility.PingObject(target);
            Selection.activeObject = target;
        }

        if (GUILayout.Button("打开原始 Inspector", GUILayout.Height(24f)))
        {
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawFoldoutSection(string title, Action drawer)
    {
        EditorGUILayout.BeginVertical("box");
        bool oldState = foldouts.TryGetValue(title, out bool value) ? value : true;
        bool newState = EditorGUILayout.Foldout(oldState, title, true, EditorStyles.foldoutHeader);
        foldouts[title] = newState;

        if (newState)
        {
            EditorGUILayout.Space(4f);
            drawer?.Invoke();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4f);
    }

    private static void EnsureFolderExists(string folderPath)
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

    private void DrawAttributeModifierRow(Rect rect, SerializedProperty listProp, int index)
    {
        SerializedProperty item = listProp.GetArrayElementAtIndex(index);
        if (item == null)
            return;

        bool selected = selectedModifierIndices.Contains(index);
        bool hover = rect.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, selectedRowPurple);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accentPurple);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        Rect inner = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, rect.height - 8f);
        float y = inner.y;

        SerializedProperty enabledProp = item.FindPropertyRelative("enabled");
        SerializedProperty keyProp = item.FindPropertyRelative("attributeKey");
        SerializedProperty displayNameProp = item.FindPropertyRelative("attributeDisplayName");
        SerializedProperty valueTypeProp = item.FindPropertyRelative("attributeValueType");
        SerializedProperty operatorProp = item.FindPropertyRelative("attributeOperator");
        SerializedProperty scalingProp = item.FindPropertyRelative("stackScaling");
        SerializedProperty valueProp = item.FindPropertyRelative("value");
        SerializedProperty boolValueProp = item.FindPropertyRelative("boolValue");

        Rect topRect = new Rect(inner.x, y, inner.width, 18f);
        Rect secondRect = new Rect(inner.x, y + 22f, inner.width, 18f);
        Rect thirdRect = new Rect(inner.x, y + 44f, inner.width, 18f);

        float enabledWidth = 18f;
        float keyWidth = 180f;
        float typeWidth = 76f;
        float opWidth = 70f;
        float scalingWidth = 86f;
        float valueWidth = 120f;
        float gap = 6f;

        Rect enabledRect = new Rect(topRect.x, topRect.y, enabledWidth, topRect.height);
        Rect nameRect = new Rect(enabledRect.xMax + gap, topRect.y, keyWidth, topRect.height);
        Rect typeRect = new Rect(nameRect.xMax + gap, topRect.y, typeWidth, topRect.height);
        Rect opRect = new Rect(typeRect.xMax + gap, topRect.y, opWidth, topRect.height);
        Rect scalingRect = new Rect(opRect.xMax + gap, topRect.y, scalingWidth, topRect.height);
        Rect valueRect = new Rect(scalingRect.xMax + gap, topRect.y, Mathf.Max(60f, topRect.xMax - (scalingRect.xMax + gap)), topRect.height);

        HandleModifierRowSelection(rect, index, new[] { enabledRect, opRect, scalingRect, valueRect, thirdRect });

        enabledProp.boolValue = EditorGUI.Toggle(enabledRect, enabledProp.boolValue);
        EditorGUI.LabelField(nameRect, string.IsNullOrWhiteSpace(displayNameProp.stringValue) ? keyProp.stringValue : $"{displayNameProp.stringValue}  ({keyProp.stringValue})");
        EditorGUI.LabelField(typeRect, GetValueTypeLabel((BattleValueType)valueTypeProp.enumValueIndex));
        EditorGUI.PropertyField(opRect, operatorProp, GUIContent.none);
        EditorGUI.PropertyField(scalingRect, scalingProp, GUIContent.none);

        BattleValueType vt = (BattleValueType)valueTypeProp.enumValueIndex;
        if (vt == BattleValueType.Boolean)
            boolValueProp.boolValue = EditorGUI.Toggle(valueRect, boolValueProp.boolValue);
        else
            valueProp.floatValue = EditorGUI.FloatField(valueRect, valueProp.floatValue);

        EditorGUI.LabelField(secondRect, string.IsNullOrWhiteSpace(displayNameProp.stringValue) ? "-" : displayNameProp.stringValue, EditorStyles.miniLabel);
        EditorGUI.LabelField(new Rect(secondRect.x + 240f, secondRect.y, secondRect.width - 240f, secondRect.height), GetModifierPreviewText(item), EditorStyles.miniLabel);

        SerializedProperty noteProp = item.FindPropertyRelative("note");
        noteProp.stringValue = EditorGUI.TextField(thirdRect, noteProp.stringValue);
    }

    private void HandleModifierRowSelection(Rect rect, int index, Rect[] interactiveRects)
    {
        Event e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition))
            return;

        if (interactiveRects != null)
        {
            for (int i = 0; i < interactiveRects.Length; i++)
            {
                if (interactiveRects[i].Contains(e.mousePosition))
                    return;
            }
        }

        if (e.shift && lastModifierSelectionIndex >= 0)
        {
            int start = Mathf.Min(lastModifierSelectionIndex, index);
            int end = Mathf.Max(lastModifierSelectionIndex, index);
            selectedModifierIndices.Clear();
            for (int i = start; i <= end; i++)
                selectedModifierIndices.Add(i);
        }
        else if (e.control || e.command)
        {
            if (selectedModifierIndices.Contains(index))
                selectedModifierIndices.Remove(index);
            else
                selectedModifierIndices.Add(index);

            lastModifierSelectionIndex = index;
        }
        else
        {
            selectedModifierIndices.Clear();
            selectedModifierIndices.Add(index);
            lastModifierSelectionIndex = index;
        }

        e.Use();
    }

    private void HandleModifierShortcuts(SerializedProperty listProp)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown)
            return;

        if ((e.control || e.command) && e.keyCode == KeyCode.A)
        {
            selectedModifierIndices.Clear();
            for (int i = 0; i < listProp.arraySize; i++)
                selectedModifierIndices.Add(i);
            e.Use();
            return;
        }

        if ((e.control || e.command) && e.keyCode == KeyCode.C)
        {
            CopySelectedModifiers(listProp);
            e.Use();
            return;
        }

        if ((e.control || e.command) && e.keyCode == KeyCode.V)
        {
            PasteModifiers(listProp);
            e.Use();
            return;
        }

        if ((e.control || e.command) && e.keyCode == KeyCode.D)
        {
            DuplicateSelectedModifiers(listProp);
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            DeleteSelectedModifiers(listProp);
            e.Use();
        }
    }

    private void AddAttributeModifierFromDefinition(SerializedProperty listProp, CoreAttributeDefinition definition)
    {
        if (definition == null || listProp == null)
            return;

        if (ContainsAttributeModifier(listProp, definition.key))
            return;

        int index = listProp.arraySize;
        listProp.InsertArrayElementAtIndex(index);
        SerializedProperty item = listProp.GetArrayElementAtIndex(index);

        item.FindPropertyRelative("enabled").boolValue = true;
        item.FindPropertyRelative("attributeKey").stringValue = definition.key ?? "";
        item.FindPropertyRelative("attributeDisplayName").stringValue = definition.displayName ?? "";
        item.FindPropertyRelative("attributeValueType").enumValueIndex = (int)definition.valueType;
        item.FindPropertyRelative("attributeOperator").enumValueIndex = (int)StatusAttributeOperator.Add;
        item.FindPropertyRelative("stackScaling").enumValueIndex = (int)StatusAttributeStackScalingMode.Linear;
        item.FindPropertyRelative("value").floatValue = definition.valueType == BattleValueType.Percentage ? 0f : 0f;
        item.FindPropertyRelative("boolValue").boolValue = false;
        item.FindPropertyRelative("note").stringValue = "";

        selectedModifierIndices.Clear();
        selectedModifierIndices.Add(index);
        lastModifierSelectionIndex = index;
    }

    private bool ContainsAttributeModifier(SerializedProperty listProp, string key)
    {
        if (listProp == null || string.IsNullOrWhiteSpace(key))
            return false;

        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty item = listProp.GetArrayElementAtIndex(i);
            if (item.FindPropertyRelative("attributeKey").stringValue == key)
                return true;
        }

        return false;
    }

    private HashSet<string> GetUsedModifierKeys(SerializedProperty listProp)
    {
        HashSet<string> result = new HashSet<string>();
        if (listProp == null)
            return result;

        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty item = listProp.GetArrayElementAtIndex(i);
            string key = item.FindPropertyRelative("attributeKey").stringValue;
            if (!string.IsNullOrWhiteSpace(key))
                result.Add(key);
        }

        return result;
    }

    private void DeleteSelectedModifiers(SerializedProperty listProp)
    {
        if (listProp == null || selectedModifierIndices.Count == 0)
            return;

        for (int i = selectedModifierIndices.Count - 1; i >= 0; i--)
        {
            int index = selectedModifierIndices[i];
            if (index >= 0 && index < listProp.arraySize)
                listProp.DeleteArrayElementAtIndex(index);
        }

        selectedModifierIndices.Clear();
        lastModifierSelectionIndex = -1;
        GUI.changed = true;
    }

    [Serializable]
    private class ModifierClipboardData
    {
        public List<StatusAttributeModifierDefinition> items = new List<StatusAttributeModifierDefinition>();
    }

    private void CopySelectedModifiers(SerializedProperty listProp)
    {
        ModifierClipboardData data = new ModifierClipboardData();

        foreach (int index in selectedModifierIndices.OrderBy(x => x))
        {
            if (index < 0 || index >= listProp.arraySize)
                continue;

            SerializedProperty item = listProp.GetArrayElementAtIndex(index);
            data.items.Add(ReadModifierFromProperty(item));
        }

        modifierClipboard = ModifierClipboardPrefix + JsonUtility.ToJson(data);
    }

    private void PasteModifiers(SerializedProperty listProp)
    {
        if (string.IsNullOrWhiteSpace(modifierClipboard) || !modifierClipboard.StartsWith(ModifierClipboardPrefix))
            return;

        string json = modifierClipboard.Substring(ModifierClipboardPrefix.Length);
        ModifierClipboardData data = JsonUtility.FromJson<ModifierClipboardData>(json);
        if (data == null || data.items == null || data.items.Count == 0)
            return;

        selectedModifierIndices.Clear();

        for (int i = 0; i < data.items.Count; i++)
        {
            StatusAttributeModifierDefinition entry = data.items[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.attributeKey))
                continue;

            string key = entry.attributeKey;
            if (ContainsAttributeModifier(listProp, key))
                key = GenerateUniqueModifierKey(listProp, key);

            int index = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(index);
            SerializedProperty item = listProp.GetArrayElementAtIndex(index);

            WriteModifierToProperty(item, entry, key);
            selectedModifierIndices.Add(index);
            lastModifierSelectionIndex = index;
        }

        GUI.changed = true;
    }

    private void DuplicateSelectedModifiers(SerializedProperty listProp)
    {
        CopySelectedModifiers(listProp);
        PasteModifiers(listProp);
    }

    private string GenerateUniqueModifierKey(SerializedProperty listProp, string baseKey)
    {
        int suffix = 1;
        string next = baseKey;
        while (ContainsAttributeModifier(listProp, next))
        {
            suffix++;
            next = $"{baseKey}_{suffix}";
        }
        return next;
    }

    private StatusAttributeModifierDefinition ReadModifierFromProperty(SerializedProperty item)
    {
        StatusAttributeModifierDefinition data = new StatusAttributeModifierDefinition();
        data.enabled = item.FindPropertyRelative("enabled").boolValue;
        data.attributeKey = item.FindPropertyRelative("attributeKey").stringValue;
        data.attributeDisplayName = item.FindPropertyRelative("attributeDisplayName").stringValue;
        data.attributeValueType = (BattleValueType)item.FindPropertyRelative("attributeValueType").enumValueIndex;
        data.attributeOperator = (StatusAttributeOperator)item.FindPropertyRelative("attributeOperator").enumValueIndex;
        data.stackScaling = (StatusAttributeStackScalingMode)item.FindPropertyRelative("stackScaling").enumValueIndex;
        data.value = item.FindPropertyRelative("value").floatValue;
        data.boolValue = item.FindPropertyRelative("boolValue").boolValue;
        data.note = item.FindPropertyRelative("note").stringValue;
        return data;
    }

    private void WriteModifierToProperty(SerializedProperty item, StatusAttributeModifierDefinition data, string keyOverride = null)
    {
        item.FindPropertyRelative("enabled").boolValue = data.enabled;
        item.FindPropertyRelative("attributeKey").stringValue = keyOverride ?? data.attributeKey;
        item.FindPropertyRelative("attributeDisplayName").stringValue = data.attributeDisplayName;
        item.FindPropertyRelative("attributeValueType").enumValueIndex = (int)data.attributeValueType;
        item.FindPropertyRelative("attributeOperator").enumValueIndex = (int)data.attributeOperator;
        item.FindPropertyRelative("stackScaling").enumValueIndex = (int)data.stackScaling;
        item.FindPropertyRelative("value").floatValue = data.value;
        item.FindPropertyRelative("boolValue").boolValue = data.boolValue;
        item.FindPropertyRelative("note").stringValue = data.note;
    }

    private string GetModifierPreviewText(SerializedProperty item)
    {
        string key = item.FindPropertyRelative("attributeKey").stringValue;
        BattleValueType type = (BattleValueType)item.FindPropertyRelative("attributeValueType").enumValueIndex;
        StatusAttributeOperator op = (StatusAttributeOperator)item.FindPropertyRelative("attributeOperator").enumValueIndex;
        StatusAttributeStackScalingMode scaling = (StatusAttributeStackScalingMode)item.FindPropertyRelative("stackScaling").enumValueIndex;

        string opText = op == StatusAttributeOperator.Add ? "+" : op == StatusAttributeOperator.Multiply ? "×" : "=";
        string valueText;

        if (type == BattleValueType.Boolean)
            valueText = item.FindPropertyRelative("boolValue").boolValue ? "True" : "False";
        else if (type == BattleValueType.Percentage)
            valueText = $"{item.FindPropertyRelative("value").floatValue:0.###}";
        else if (type == BattleValueType.Integer)
            valueText = Mathf.RoundToInt(item.FindPropertyRelative("value").floatValue).ToString();
        else
            valueText = item.FindPropertyRelative("value").floatValue.ToString("0.###");

        string scalingText = scaling == StatusAttributeStackScalingMode.None ? "固定" :
            scaling == StatusAttributeStackScalingMode.Linear ? "线性叠层" : "乘层";
        return $"{key}  {opText} {valueText}  /  {scalingText}";
    }

    private string GetValueTypeLabel(BattleValueType type)
    {
        switch (type)
        {
            case BattleValueType.Integer: return "Integer";
            case BattleValueType.Float: return "Float";
            case BattleValueType.Percentage: return "Percentage";
            case BattleValueType.Boolean: return "Boolean";
            default: return type.ToString();
        }
    }

    private List<CoreAttributeDefinition> GetCoreAttributeDefinitions()
    {
        List<CoreAttributeDefinition> result = new List<CoreAttributeDefinition>();
        HashSet<string> addedKeys = new HashSet<string>();

        string[] guids = AssetDatabase.FindAssets("t:BattleParameterDatabase");
        for (int i = 0; i < guids.Length; i++)
        {
            BattleParameterDatabase db = AssetDatabase.LoadAssetAtPath<BattleParameterDatabase>(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (db == null || db.coreAttributes == null)
                continue;

            for (int j = 0; j < db.coreAttributes.Count; j++)
            {
                CoreAttributeDefinition def = db.coreAttributes[j];
                if (def == null || string.IsNullOrWhiteSpace(def.key))
                    continue;

                if (!addedKeys.Add(def.key))
                    continue;

                result.Add(def);
            }
        }

        return result
            .OrderBy(x => x.isStandard ? 0 : 1)
            .ThenBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.key : x.displayName)
            .ToList();
    }
}
