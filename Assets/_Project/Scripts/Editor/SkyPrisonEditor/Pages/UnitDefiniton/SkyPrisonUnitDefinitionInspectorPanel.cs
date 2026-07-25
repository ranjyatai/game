using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// V25 - 2026-06-05: Sync runtime prefab visual after Spine asset change. Compatible with KeepOccludedProxyOnly unit baseline.
public class SkyPrisonUnitDefinitionInspectorPanel
{
    // V24 - 2026-06-05: Spine asset assignment now syncs the runtime prefab source Spine directly.

    private const string SpineAssetRootFolder = "Assets/_Project/Art/Spine";

    private readonly SkyPrisonUnitDefinitionPage page;

    private bool standardParametersFoldout = true;
    private bool customParametersFoldout = true;
    private string lastOverheadSyncUnitPath = "";

    public SkyPrisonUnitDefinitionInspectorPanel(SkyPrisonUnitDefinitionPage page)
    {
        this.page = page;
    }

    public void Draw()
    {
        UnitDefinition unit = page.SelectedUnitDefinition;
        SerializedObject so = page.SelectedUnitSO;

        if (unit == null || so == null)
        {
            EditorGUILayout.HelpBox("请先在左侧选择一个单位定义。", MessageType.Info);
            return;
        }

        so.Update();
        SyncLocalizedBackfields(so, unit);
        TryAutoPullOverheadOffsetOnSelection(unit, so);
        DrawWorkspaceHeader(unit);
        DrawBattleParameterPresetBinding(unit);

        GUILayout.Space(8f);

        page.DrawFoldoutSection("基础信息", DrawUnitBasicInfo);
        page.DrawFoldoutSection("多语言名称", DrawLocalizedNames);
        page.DrawFoldoutSection("多语言描述", DrawLocalizedDescriptions);
        page.DrawFoldoutSection("定义类型", DrawUnitDefineType);
        page.DrawFoldoutSection("控制方式", DrawUnitControlModeSection);
        page.DrawFoldoutSection("AI 行为", DrawUnitAISection);
        page.DrawFoldoutSection("视野范围", DrawUnitVision);
        page.DrawFoldoutSection("听觉感知", DrawUnitHearing);
        page.DrawFoldoutSection("视觉通道 / 预制体", DrawUnitPrefab);
        page.DrawFoldoutSection("属性数值", DrawUnitParameterValues);
        page.DrawFoldoutSection("遮挡与描边", DrawUnitOutline);
        page.DrawFoldoutSection("物理规范", DrawUnitPhysics);
        page.DrawFoldoutSection("单位UI", DrawUnitShadow);
        page.DrawFoldoutSection("单位音声", DrawUnitAudio);
        page.DrawFoldoutSection("单位碰撞盒", DrawUnitCollision);
        page.DrawFoldoutSection("移动规则", DrawUnitMovement);
        page.DrawFoldoutSection("动作动画 Key", DrawUnitAnimationKeys);
        page.DrawFoldoutSection("战斗模组", DrawUnitCombatModule);
        page.DrawFoldoutSection("出血特效", DrawUnitBloodVFX);
        page.DrawFoldoutSection("死亡规则", DrawUnitDeath);
        page.DrawFoldoutSection("掉落配置", DrawUnitDropProfiles);

        so.ApplyModifiedProperties();
    }

    private void DrawWorkspaceHeader(UnitDefinition unit)
    {
        EditorGUILayout.BeginVertical("box");

        string unitName = GetBestDisplayName(unit);
        string defineTypeText = GetDefineTypeLabel();

        EditorGUILayout.LabelField("单位定义工作台", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(unitName, EditorStyles.miniBoldLabel);

        if (!string.IsNullOrWhiteSpace(defineTypeText))
            EditorGUILayout.LabelField($"当前类型：{defineTypeText}", EditorStyles.miniLabel);

        EditorGUILayout.Space(6f);

        page.DrawReadonlyRow("资源路径", AssetDatabase.GetAssetPath(unit));
        page.DrawReadonlyRow("单位 ID", string.IsNullOrWhiteSpace(unit.unitId) ? "-" : unit.unitId);
        page.DrawReadonlyRow("显示名称", GetBestDisplayName(unit));

        EditorGUILayout.Space(4f);

        page.DrawPingButtons(unit);
        page.DrawUnitIdDuplicateWarning(unit);

        EditorGUILayout.EndVertical();
    }

    private string GetBestDisplayName(UnitDefinition unit)
    {
        if (unit == null)
            return "未命名单位";

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        string defaultLanguageCode = GetDefaultLanguageCode(settings);

        string localized = GetLocalizedText(unit.localizedNames, defaultLanguageCode);
        if (!string.IsNullOrWhiteSpace(localized))
            return localized;

        if (!string.IsNullOrWhiteSpace(unit.displayName))
            return unit.displayName;

        return unit.name;
    }

    private string GetDefineTypeLabel()
    {
        SerializedProperty defineType = page.SelectedUnitSO.FindProperty("defineType");
        if (defineType == null)
            return "";

        UnitDefineType type = (UnitDefineType)defineType.enumValueIndex;
        return type.ToString();
    }

    private void DrawBattleParameterPresetBinding(UnitDefinition unit)
    {
        SerializedProperty dbProp = page.SelectedUnitSO.FindProperty("battleParameterDatabase");
        if (dbProp == null)
            return;

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("战斗参数预设包", EditorStyles.boldLabel);
        EditorGUILayout.Space(4f);

        page.DrawRow("参数预设包", dbProp);

        BattleParameterDatabase current = dbProp.objectReferenceValue as BattleParameterDatabase;
        if (current == null)
            current = FindRuntimeActiveBattleParameterDatabase();

        if (current != null)
        {
            page.DrawReadonlyRow("资源路径", AssetDatabase.GetAssetPath(current));
            page.DrawReadonlyRow("参数库 ID", string.IsNullOrWhiteSpace(current.databaseId) ? "-" : current.databaseId);
            page.DrawReadonlyRow("显示名称", string.IsNullOrWhiteSpace(current.displayName) ? current.name : current.displayName);
        }
        else
        {
            EditorGUILayout.HelpBox("当前未绑定战斗参数预设包，也没有找到运行时激活库。", MessageType.Warning);
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(140f);

        if (GUILayout.Button("使用运行时库", GUILayout.Width(110f)))
        {
            BattleParameterDatabase runtimeDb = FindRuntimeActiveBattleParameterDatabase();
            if (runtimeDb != null)
                dbProp.objectReferenceValue = runtimeDb;
        }

        using (new EditorGUI.DisabledScope(current == null))
        {
            if (GUILayout.Button("定位", GUILayout.Width(60f)) && current != null)
            {
                Selection.activeObject = current;
                EditorGUIUtility.PingObject(current);
            }

            if (GUILayout.Button("清空", GUILayout.Width(60f)))
                dbProp.objectReferenceValue = null;
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
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

    private string GetPrimaryDescription(UnitDefinition unit)
    {
        if (unit == null)
            return "";

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        string defaultLanguageCode = GetDefaultLanguageCode(settings);
        string localized = GetLocalizedText(unit.localizedDescriptions, defaultLanguageCode);
        if (!string.IsNullOrWhiteSpace(localized))
            return localized;
        return unit.description ?? "";
    }

    private void DrawReadonlyMultiline(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(value) ? "-" : value, GUILayout.MinHeight(54f));
        EditorGUILayout.EndHorizontal();
    }

    private void SyncLocalizedBackfields(SerializedObject so, UnitDefinition unit)
    {
        if (so == null || unit == null)
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
            string v = textProp != null ? textProp.stringValue ?? "" : "";
            if (displayNameProp.stringValue != v)
                displayNameProp.stringValue = v;
        }

        if (descriptionProp != null && defaultDescriptionEntry != null)
        {
            SerializedProperty textProp = defaultDescriptionEntry.FindPropertyRelative("text");
            string v = textProp != null ? textProp.stringValue ?? "" : "";
            if (descriptionProp.stringValue != v)
                descriptionProp.stringValue = v;
        }
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

    private void DrawUnitBasicInfo()
    {
        DrawIconField();
        DrawUnitIdRow();
        page.DrawReadonlyRow("显示名称", GetBestDisplayName(page.SelectedUnitDefinition));
        DrawReadonlyMultiline("主语言描述", GetPrimaryDescription(page.SelectedUnitDefinition));
        page.DrawRow("备注", page.SelectedUnitSO.FindProperty("note"));
        EditorGUILayout.HelpBox("显示名称与描述由主语言自动同步，这里不再直接编辑。", MessageType.None);
    }

    private void DrawUnitIdRow()
    {
        SerializedProperty unitIdProp = page.SelectedUnitSO.FindProperty("unitId");
        if (unitIdProp == null)
        {
            EditorGUILayout.HelpBox("字段 unitId 不存在。", MessageType.Warning);
            return;
        }

        page.DrawReadonlyRow("单位 ID", string.IsNullOrWhiteSpace(unitIdProp.stringValue) ? "-" : unitIdProp.stringValue);
        EditorGUILayout.HelpBox("单位 Key 由系统维护，这里不直接编辑。", MessageType.None);
    }

    private void DrawIconField()
    {
        SerializedProperty iconProp = page.SelectedUnitSO.FindProperty("icon");

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("图标", GUILayout.Width(140f));

        EditorGUILayout.BeginVertical();
        EditorGUILayout.PropertyField(iconProp, GUIContent.none, true);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("选择图标（预留）", GUILayout.Width(110f)))
        {
            EditorUtility.DisplayDialog(
                "预留接口",
                "图标浏览器下一步再接。\n当前先支持直接拖 Sprite。",
                "确定"
            );
        }

        if (GUILayout.Button("清空", GUILayout.Width(60f)))
            iconProp.objectReferenceValue = null;

        EditorGUILayout.EndHorizontal();

        if (iconProp.objectReferenceValue is Sprite sprite && sprite.texture != null)
        {
            Rect previewRect = GUILayoutUtility.GetRect(72f, 72f, GUILayout.Width(72f), GUILayout.Height(72f));
            GUI.DrawTexture(previewRect, sprite.texture, ScaleMode.ScaleToFit);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawLocalizedNames()
    {
        SerializedProperty prop = page.SelectedUnitSO.FindProperty("localizedNames");
        DrawLocalizedTextList(prop, false, true);
    }

    private void DrawLocalizedDescriptions()
    {
        SerializedProperty prop = page.SelectedUnitSO.FindProperty("localizedDescriptions");
        DrawLocalizedDescriptionRichTextList(prop);
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
        PruneLocalizedEntries(listProp, settings);

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
        if (defaultEntry == null)
            return;

        SerializedProperty defaultTextProp = defaultEntry.FindPropertyRelative("text");
        string defaultText = defaultTextProp != null ? defaultTextProp.stringValue ?? "" : "";

        if (syncToDisplayName)
        {
            SerializedProperty displayNameProp = page.SelectedUnitSO.FindProperty("displayName");
            if (displayNameProp != null && displayNameProp.stringValue != defaultText)
                displayNameProp.stringValue = defaultText;
        }
        else
        {
            SerializedProperty descriptionProp = page.SelectedUnitSO.FindProperty("description");
            if (descriptionProp != null && descriptionProp.stringValue != defaultText)
                descriptionProp.stringValue = defaultText;
        }
    }


    private void DrawLocalizedDescriptionRichTextList(SerializedProperty listProp)
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
        PruneLocalizedEntries(listProp, settings);

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
                string openLang = lang.languageCode;
                string openCurrent = textProp != null ? (textProp.stringValue ?? "") : "";
                EditorApplication.delayCall += () =>
                {
                    SkyPrisonRichTextEditorWindow.Open(
                        openLabel,
                        openCurrent,
                        updated =>
                        {
                            if (page.SelectedUnitSO == null)
                                return;

                            page.SelectedUnitSO.Update();
                            SerializedProperty localizedList = page.SelectedUnitSO.FindProperty("localizedDescriptions");
                            SerializedProperty entry = FindLocalizedEntry(localizedList, openLang);
                            if (entry != null)
                            {
                                SerializedProperty text = entry.FindPropertyRelative("text");
                                if (text != null)
                                    text.stringValue = updated ?? "";
                            }

                            SerializedProperty defaultEntry = FindLocalizedEntry(localizedList, defaultLanguageCode);
                            SerializedProperty descriptionProp = page.SelectedUnitSO.FindProperty("description");
                            if (defaultEntry != null && descriptionProp != null)
                            {
                                SerializedProperty defaultText = defaultEntry.FindPropertyRelative("text");
                                descriptionProp.stringValue = defaultText != null ? (defaultText.stringValue ?? "") : "";
                            }

                            page.SelectedUnitSO.ApplyModifiedProperties();
                            EditorUtility.SetDirty(page.SelectedUnitDefinition);
                        },
                        "unit");
                };
                GUIUtility.ExitGUI();
            }
        }

        SerializedProperty defaultEntryProp = FindLocalizedEntry(listProp, defaultLanguageCode);
        if (defaultEntryProp == null)
            return;

        SerializedProperty defaultTextProp = defaultEntryProp.FindPropertyRelative("text");
        SerializedProperty descriptionPropSync = page.SelectedUnitSO.FindProperty("description");
        string defaultTextValue = defaultTextProp != null ? defaultTextProp.stringValue ?? "" : "";
        if (descriptionPropSync != null && descriptionPropSync.stringValue != defaultTextValue)
            descriptionPropSync.stringValue = defaultTextValue;
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
        for (int i = 0; i < chars.Count; i++)
        {
            PreviewCharStyle c = chars[i];
            if (c.character == '\n')
            {
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
                x = rect.x + 8f;
                y += lineHeight;
            }

            Rect charRect = new Rect(x, y, width, lineHeight);
            float drawOffset = GetPreviewCharDrawOffset(c.character);
            Rect drawRect = new Rect(charRect.x + drawOffset, charRect.y, charRect.width - drawOffset + 1f, charRect.height);
            GUI.Label(drawRect, c.character.ToString(), style);

            if (c.underline)
                EditorGUI.DrawRect(new Rect(charRect.x, y + lineHeight - 2f, Mathf.Max(1f, charRect.width + 0.25f), 1f), style.normal.textColor);

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

            PreviewCharStyle s;
            s.character = c;
            s.bold = bold;
            s.italic = italic;
            s.underline = underline;
            s.hasColor = hasColor;
            s.color = color;
            result.Add(s);
        }

        return result;
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

    private void PruneLocalizedEntries(SerializedProperty listProp, LocalizationProjectSettings settings)
    {
        HashSet<string> validCodes = new HashSet<string>();
        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang != null && lang.enabled && !string.IsNullOrWhiteSpace(lang.languageCode))
                validCodes.Add(lang.languageCode);
        }

        for (int i = listProp.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty item = listProp.GetArrayElementAtIndex(i);
            SerializedProperty codeProp = item.FindPropertyRelative("languageCode");
            string code = codeProp != null ? codeProp.stringValue : "";
            if (!validCodes.Contains(code))
                listProp.DeleteArrayElementAtIndex(i);
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

    private void DrawUnitDefineType()
    {
        SerializedProperty defineType = page.SelectedUnitSO.FindProperty("defineType");
        page.DrawRow("定义类型", defineType);

        if (defineType != null && (UnitDefineType)defineType.enumValueIndex == UnitDefineType.Character)
            page.DrawRow("人物身份", page.SelectedUnitSO.FindProperty("characterIdentity"));

        SerializedProperty anomalyMultiplierProp = page.SelectedUnitSO.FindProperty("anomalyReceiveMultiplier");
        if (anomalyMultiplierProp != null)
        {
            page.DrawRow("个体异常倍率", anomalyMultiplierProp);
            EditorGUILayout.HelpBox(
                "这是单位个体异常承受倍率。\n最终异常累积 = 输入值 × 异常默认累计系数 × 类别倍率 × 个体倍率。",
                MessageType.None);
        }
    }

    private void DrawUnitControlModeSection()
    {
        SerializedProperty defineType = page.SelectedUnitSO.FindProperty("defineType");
        SerializedProperty controlMode = page.SelectedUnitSO.FindProperty("controlMode");

        if (controlMode == null)
        {
            EditorGUILayout.HelpBox("当前 UnitDefinition 里还没有 controlMode 字段。", MessageType.Warning);
            return;
        }

        bool isCharacter = true;
        if (defineType != null)
            isCharacter = (UnitDefineType)defineType.enumValueIndex == UnitDefineType.Character;

        if (!isCharacter)
        {
            EditorGUILayout.HelpBox(
                "当前定义类型不是 Character，因此控制方式模块默认隐藏。\n字段仍然保留，但此处不显示和不使用。",
                MessageType.Info
            );
            return;
        }

        page.DrawRow("控制方式", controlMode);
    }

    private void DrawUnitAISection()
    {
        SerializedProperty defineType = page.SelectedUnitSO.FindProperty("defineType");
        SerializedProperty controlMode = page.SelectedUnitSO.FindProperty("controlMode");
        SerializedProperty aiPackage = page.SelectedUnitSO.FindProperty("aiBehaviorPackage");

        if (aiPackage == null)
        {
            EditorGUILayout.HelpBox("当前 UnitDefinition 里还没有 aiBehaviorPackage 字段。", MessageType.Warning);
            return;
        }

        bool isCharacter = true;
        if (defineType != null)
            isCharacter = (UnitDefineType)defineType.enumValueIndex == UnitDefineType.Character;

        if (!isCharacter)
        {
            EditorGUILayout.HelpBox(
                "当前定义类型不是 Character，因此 AI 行为包模块默认隐藏。\n字段仍然保留，但此处不显示和不使用。",
                MessageType.Info
            );
            return;
        }

        bool isAIControlled = true;
        if (controlMode != null)
            isAIControlled = (UnitControlMode)controlMode.enumValueIndex == UnitControlMode.AIControlled;

        using (new EditorGUI.DisabledScope(!isAIControlled))
        {
            page.DrawRow("AI 行为包", aiPackage);

            string currentAIName = "未绑定";
            if (aiPackage.objectReferenceValue is AIBehaviorPackage currentPackage)
            {
                currentAIName = string.IsNullOrWhiteSpace(currentPackage.displayName)
                    ? currentPackage.name
                    : currentPackage.displayName;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("当前 AI", GUILayout.Width(140f));
            EditorGUILayout.SelectableLabel(currentAIName, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(140f);

            if (GUILayout.Button("新建 AI 包", GUILayout.Width(90f)))
                CreateNewAIPackageFromUnitPage(aiPackage);

            if (GUILayout.Button("打开 AI 页面", GUILayout.Width(100f)))
                SkyPrisonEditorWindow.OpenWindowWithTab("AI", aiPackage.objectReferenceValue);

            if (aiPackage.objectReferenceValue != null)
            {
                if (GUILayout.Button("打开当前 AI", GUILayout.Width(100f)))
                {
                    Selection.activeObject = aiPackage.objectReferenceValue;
                    EditorGUIUtility.PingObject(aiPackage.objectReferenceValue);
                    SkyPrisonEditorWindow.OpenWindowWithTab("AI");
                }

                if (GUILayout.Button("定位", GUILayout.Width(60f)))
                {
                    Selection.activeObject = aiPackage.objectReferenceValue;
                    EditorGUIUtility.PingObject(aiPackage.objectReferenceValue);
                }

                if (GUILayout.Button("清空", GUILayout.Width(60f)))
                    aiPackage.objectReferenceValue = null;
            }

            EditorGUILayout.EndHorizontal();
        }

        if (!isAIControlled)
        {
            EditorGUILayout.HelpBox(
                "当前控制方式不是 AIControlled，因此 AI 行为包会被隐藏或禁用，但字段仍然保留。",
                MessageType.Info
            );
        }
    }

    private void DrawUnitPrefab()
    {
        SerializedObject so = page.SelectedUnitSO;
        UnitDefinition unit = page.SelectedUnitDefinition;

        if (so == null || unit == null)
            return;

        SerializedProperty prefabProp = so.FindProperty("prefab");
        SerializedProperty visualChannelProp = so.FindProperty("visualChannel");
        SerializedProperty spinePrefabProp = so.FindProperty("spinePrefab");
        SerializedProperty model3DPrefabProp = so.FindProperty("model3DPrefab");

        EditorGUILayout.HelpBox(
            "单位运行时预制体是单位壳；视觉通道只保留 Spine / 3D 两条。",
            MessageType.Info
        );

        page.DrawRow("单位运行时预制体", prefabProp);

        if (visualChannelProp == null)
        {
            EditorGUILayout.HelpBox(
                "当前 UnitDefinition.cs 尚未包含 visualChannel / spinePrefab / model3DPrefab 字段。请先替换新版 UnitDefinition.cs。",
                MessageType.Warning
            );
            DrawVisualStructureButtons(unit);
            return;
        }

        if (visualChannelProp.enumValueIndex > 1)
            visualChannelProp.enumValueIndex = 0;

        GUILayout.Space(4f);
        DrawTwoChannelSelector(visualChannelProp);

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("当前通道设置", EditorStyles.boldLabel);

        // 这里不直接引用 UnitVisualChannel 枚举，避免旧工程字段刚迁移时因为类型差异导致编辑器面板编译失败。
        // 当前窗口只允许：0 = Spine, 1 = Model3D。
        switch (visualChannelProp.enumValueIndex)
        {
            case 0:
                DrawSpineAssetRow("Spine 通道", spinePrefabProp);
                break;

            case 1:
                DrawAssetGameObjectRow("3D 通道", model3DPrefabProp);
                break;

            default:
                visualChannelProp.enumValueIndex = 0;
                DrawSpineAssetRow("Spine 通道", spinePrefabProp);
                break;
        }

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("兼容字段", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "旧 prefab 字段现在保留为单位运行时壳，不再代表唯一视觉模型。地图生成器应实例化这个单位壳，再由 RuntimeApplier 按视觉通道启用对应 Root。",
            MessageType.None
        );

        DrawVisualStructureButtons(unit);
    }



    private void DrawSpineAssetRow(string label, SerializedProperty property)
    {
        if (property == null)
        {
            EditorGUILayout.HelpBox($"字段 {label} 不存在。", MessageType.Warning);
            return;
        }

        UnityEngine.Object current = property.objectReferenceValue;
        string currentPath = current != null ? AssetDatabase.GetAssetPath(current) : "";
        bool currentValid = current == null || IsAllowedSpineAssetPath(currentPath);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(current, typeof(ScriptableObject), false);
        }

        if (GUILayout.Button("选择 .asset", GUILayout.Width(86f)))
            ShowSpineAssetPickerMenu(property, current);

        using (new EditorGUI.DisabledScope(current == null))
        {
            if (GUILayout.Button("定位", GUILayout.Width(50f)) && current != null)
            {
                Selection.activeObject = current;
                EditorGUIUtility.PingObject(current);
            }

            if (GUILayout.Button("清空", GUILayout.Width(50f)))
                TryAssignSpineChannelAsset(property, current, null);
        }

        EditorGUILayout.EndHorizontal();

        if (current != null)
            page.DrawReadonlyRow("Spine资源路径", string.IsNullOrWhiteSpace(currentPath) ? "-" : currentPath);

        if (!AssetDatabase.IsValidFolder(SpineAssetRootFolder))
        {
            EditorGUILayout.HelpBox(
                $"Spine 资源根目录不存在：{SpineAssetRootFolder}\n请确认工程里已经创建这个目录。",
                MessageType.Warning
            );
        }
        else if (!currentValid)
        {
            EditorGUILayout.HelpBox(
                $"当前已绑定资源不在唯一允许目录下。请重新从 {SpineAssetRootFolder} 选择 .asset。",
                MessageType.Warning
            );
        }

        EditorGUILayout.HelpBox(
            $"Spine 通道只允许绑定 {SpineAssetRootFolder} 目录下的 ScriptableObject .asset 文件。这里不再接受 Prefab、场景物体或其他目录资源。",
            MessageType.None
        );
    }

    private void ShowSpineAssetPickerMenu(SerializedProperty property, UnityEngine.Object current)
    {
        List<ScriptableObject> assets = GetSpineAssetCandidates();

        if (assets.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "没有找到 Spine .asset",
                $"没有在 {SpineAssetRootFolder} 目录下找到 .asset 文件。\n请确认 Spine 导出的 SkeletonDataAsset 已经放在这个目录或它的子目录里。",
                "知道了"
            );
            return;
        }

        GenericMenu menu = new GenericMenu();

        for (int i = 0; i < assets.Count; i++)
        {
            ScriptableObject asset = assets[i];
            string assetPath = AssetDatabase.GetAssetPath(asset);
            string menuLabel = assetPath.Substring(SpineAssetRootFolder.Length).TrimStart('/');
            bool on = current == asset;

            menu.AddItem(
                new GUIContent(string.IsNullOrWhiteSpace(menuLabel) ? asset.name : menuLabel),
                on,
                () => TryAssignSpineChannelAsset(property, current, asset)
            );
        }

        menu.ShowAsContext();
    }

    private List<ScriptableObject> GetSpineAssetCandidates()
    {
        List<ScriptableObject> results = new List<ScriptableObject>();

        if (!AssetDatabase.IsValidFolder(SpineAssetRootFolder))
            return results;

        string[] guids = AssetDatabase.FindAssets("", new[] { SpineAssetRootFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!IsAllowedSpineAssetPath(path))
                continue;

            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset != null)
                results.Add(asset);
        }

        results.Sort((a, b) => string.Compare(
            AssetDatabase.GetAssetPath(a),
            AssetDatabase.GetAssetPath(b),
            StringComparison.OrdinalIgnoreCase));

        return results;
    }

    private void TryAssignSpineChannelAsset(SerializedProperty property, UnityEngine.Object current, UnityEngine.Object selected)
    {
        if (selected != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(selected);

            if (!IsAllowedSpineAssetPath(assetPath))
            {
                EditorUtility.DisplayDialog(
                    "Spine 资源路径不允许",
                    $"Spine 通道只能选择这个目录下的 .asset：\n{SpineAssetRootFolder}\n\n当前选择：\n{(string.IsNullOrWhiteSpace(assetPath) ? "场景物体或工程外资源" : assetPath)}",
                    "知道了"
                );
                selected = current;
            }
        }

        property.objectReferenceValue = selected;
        property.serializedObject.ApplyModifiedProperties();

        UnitDefinition changedUnit = property.serializedObject.targetObject as UnitDefinition;
        if (changedUnit != null)
        {
            EditorUtility.SetDirty(changedUnit);
            TrySyncRuntimePrefabVisualFromUnitDefinition(changedUnit);
            AssetDatabase.SaveAssets();
        }

        if (selected != null && property.objectReferenceValue != selected)
        {
            EditorUtility.DisplayDialog(
                "Spine 字段类型需要更新",
                "这个 .asset 已经通过面板校验，但 Unity 没能写入字段。请确认 UnitDefinition.cs 中 spinePrefab 字段类型是：public ScriptableObject spinePrefab;",
                "知道了"
            );
        }
    }

    private void TrySyncRuntimePrefabVisualFromUnitDefinition(UnitDefinition unit)
    {
        if (unit == null || unit.prefab == null)
            return;

        string prefabPath = AssetDatabase.GetAssetPath(unit.prefab);
        if (string.IsNullOrWhiteSpace(prefabPath))
            return;

        GameObject prefabRoot = null;
        try
        {
            prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
                return;

            UnitDefinitionRuntimeBinder binder = prefabRoot.GetComponent<UnitDefinitionRuntimeBinder>()
                ?? prefabRoot.GetComponentInChildren<UnitDefinitionRuntimeBinder>(true);

            if (binder != null)
            {
                binder.SetUnitDefinitionAsset(unit, true);
                EditorUtility.SetDirty(binder);
            }

            UnitDefinitionRuntimeApplier applier = prefabRoot.GetComponent<UnitDefinitionRuntimeApplier>()
                ?? prefabRoot.GetComponentInChildren<UnitDefinitionRuntimeApplier>(true);

            if (applier != null)
            {
                applier.ApplyDefinition(true);
                EditorUtility.SetDirty(applier);
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SkyPrisonUnitDefinitionInspectorPanel] Failed to sync runtime prefab visual from UnitDefinition '{unit.name}': {ex.Message}", unit);
        }
        finally
        {
            if (prefabRoot != null)
                PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool IsAllowedSpineAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        string normalized = assetPath.Replace("\\", "/");
        string root = SpineAssetRootFolder.TrimEnd('/');

        return normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
            && (normalized.Equals(root, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAssetFilePath(string assetPath)
    {
        return !string.IsNullOrWhiteSpace(assetPath)
            && assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpineSkeletonDataAsset(UnityEngine.Object asset)
    {
        if (asset == null)
            return false;

        Type type = asset.GetType();
        return type.Name == "SkeletonDataAsset" || type.FullName == "Spine.Unity.SkeletonDataAsset";
    }

    private void DrawAssetGameObjectRow(string label, SerializedProperty property)
    {
        if (property == null)
        {
            EditorGUILayout.HelpBox($"字段 {label} 不存在。", MessageType.Warning);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));

        GameObject current = property.objectReferenceValue as GameObject;
        GameObject selected = (GameObject)EditorGUILayout.ObjectField(
            current,
            typeof(GameObject),
            false);

        if (selected != current)
        {
            if (selected != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(selected);
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    EditorUtility.DisplayDialog(
                        "只能选择 Assets 里的预制体",
                        "视觉通道字段只接受 Project/Assets 中的 Prefab 资产，不能绑定场景里的 GameObject。",
                        "知道了"
                    );
                    selected = current;
                }
            }

            property.objectReferenceValue = selected;
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawTwoChannelSelector(SerializedProperty visualChannelProp)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("视觉通道", GUILayout.Width(96f));

        int current = Mathf.Clamp(visualChannelProp.enumValueIndex, 0, 1);
        int selected = GUILayout.Toolbar(
            current,
            new[] { "Spine", "3D" },
            GUILayout.Height(22f));

        if (selected != visualChannelProp.enumValueIndex)
            visualChannelProp.enumValueIndex = selected;

        EditorGUILayout.EndHorizontal();
    }

    private void DrawVisualStructureButtons(UnitDefinition unit)
    {
        if (unit == null)
            return;

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("结构修复", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("修复当前 UD 字段", GUILayout.Height(24f)))
            InvokeVisualStructureUtility("RepairUnitDefinition", unit);

        if (GUILayout.Button("修复 Prefab 结构", GUILayout.Height(24f)))
            InvokeVisualStructureUtility("RepairUnitPrefabStructure", unit);

        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("一键修复 UD + Prefab", GUILayout.Height(24f)))
            InvokeVisualStructureUtility("RepairAll", unit);

        EditorGUILayout.HelpBox(
            "结构修复只面向 VisualRoot / SpineRoot / Model3DRoot。不会创建自研包 Root，也不会显示自研通道入口。",
            MessageType.None
        );
    }

    private void InvokeVisualStructureUtility(string methodName, UnitDefinition unit)
    {
        if (unit == null)
            return;

        Type utilityType = Type.GetType("UnitDefinitionVisualStructureUtility");
        if (utilityType == null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                utilityType = assembly.GetType("UnitDefinitionVisualStructureUtility");
                if (utilityType != null)
                    break;
            }
        }

        if (utilityType == null)
        {
            EditorUtility.DisplayDialog(
                "缺少结构修复工具",
                "未找到 UnitDefinitionVisualStructureUtility。请确认该脚本已放入 Editor 目录并成功编译。",
                "知道了"
            );
            return;
        }

        var method = utilityType.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        );

        if (method == null)
        {
            EditorUtility.DisplayDialog(
                "缺少修复方法",
                $"UnitDefinitionVisualStructureUtility 中未找到方法：{methodName}",
                "知道了"
            );
            return;
        }

        try
        {
            method.Invoke(null, new object[] { unit });
            EditorUtility.SetDirty(unit);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog(
                "结构修复失败",
                ex.InnerException != null ? ex.InnerException.Message : ex.Message,
                "知道了"
            );
        }
    }

    private void DrawUnitParameterValues()
    {
        BattleParameterDatabase database = FindBattleParameterDatabase();
        if (database == null)
        {
            EditorGUILayout.HelpBox("未找到 BattleParameterDatabase，暂时无法填写单位属性。", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            $"当前属性填写基于参数库：{(string.IsNullOrWhiteSpace(database.displayName) ? database.name : database.displayName)}",
            MessageType.None);

        SerializedProperty parameterValuesProp = page.SelectedUnitSO.FindProperty("parameterValues");
        if (parameterValuesProp == null)
        {
            EditorGUILayout.HelpBox("当前 UnitDefinition 里还没有 parameterValues 字段。", MessageType.Warning);
            return;
        }

        EnsureParameterEntries(parameterValuesProp, database);

        List<CoreAttributeDefinition> standardDefs = database.coreAttributes
            .Where(x => x != null && IsStandardCoreAttribute(x.key))
            .ToList();

        List<CoreAttributeDefinition> customDefs = database.coreAttributes
            .Where(x => x != null && !IsStandardCoreAttribute(x.key))
            .ToList();

        DrawSubFoldoutHeader(ref standardParametersFoldout, "标准属性");
        if (standardParametersFoldout)
        {
            DrawParameterGroup(parameterValuesProp, standardDefs, true);
            GUILayout.Space(6f);
        }

        DrawSubFoldoutHeader(ref customParametersFoldout, "自定义属性");
        if (customParametersFoldout)
        {
            DrawParameterGroup(parameterValuesProp, customDefs, false);
        }
    }

    private bool IsStandardCoreAttribute(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        switch (key)
        {
            case "maxHp":
            case "maxLp":
            case "hp":
            case "lp":
            case "atk":
            case "atkSpeed":
            case "hpRecoveryRate":
            case "lpRecoveryRate":
            case "heatDamage":
            case "shockDamage":
            case "corrosionDamage":
            case "freezeDamage":
            case "dodgeLpCostRate":
            case "sprintLpCostRate":
            case "lightAtkLpCostRate":
            case "heavyAtkLpCostRate":
            case "chargeAtkLpCostRate":
            case "lpRecoveryDelayRate":
            case "dodgeInvulTimeRate":
            case "staggerRecoveryRate":
            case "poise":
            case "hitRecoveryRate":
            case "critRate":
            case "critDamageMultiplier":
            case "negativeCritRate":
            case "negativeCritDamageMultiplier":
            case "lpRecoveryBase":
            case "hpRecoveryBase":
            case "lpRecoveryDelayBase":
            case "heatBuildUp":
            case "shockBuildUp":
            case "corrosionBuildUp":
            case "freezeBuildUp":
            case "def":
            case "slashResist":
            case "strikeResist":
            case "impactResist":
            case "heatResist":
            case "shockResist":
            case "corrosionResist":
            case "freezeResist":
                return true;
            default:
                return false;
        }
    }

    private void DrawParameterGroup(
        SerializedProperty parameterValuesProp,
        List<CoreAttributeDefinition> definitions,
        bool isStandard)
    {
        if (definitions == null || definitions.Count == 0)
        {
            EditorGUILayout.LabelField(isStandard ? "暂无标准属性" : "暂无自定义属性", EditorStyles.miniLabel);
            return;
        }

        EditorGUILayout.BeginVertical("box");

        for (int i = 0; i < definitions.Count; i++)
        {
            CoreAttributeDefinition def = definitions[i];
            if (def == null || string.IsNullOrWhiteSpace(def.key))
                continue;

            SerializedProperty entryProp = FindParameterEntry(parameterValuesProp, def.key);
            if (entryProp == null)
                continue;

            SerializedProperty valueProp = entryProp.FindPropertyRelative("value");

            string label = string.IsNullOrWhiteSpace(def.displayName) ? def.key : def.displayName;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180f));

            if (valueProp != null)
            {
                switch (def.valueType)
                {
                    case BattleValueType.Integer:
                        valueProp.floatValue = EditorGUILayout.IntField(Mathf.RoundToInt(valueProp.floatValue));
                        break;

                    case BattleValueType.Boolean:
                        valueProp.floatValue = EditorGUILayout.Toggle(valueProp.floatValue > 0.5f) ? 1f : 0f;
                        break;

                    case BattleValueType.Percentage:
                        valueProp.floatValue = EditorGUILayout.FloatField(valueProp.floatValue);
                        GUILayout.Label("%", GUILayout.Width(18f));
                        break;

                    default:
                        valueProp.floatValue = EditorGUILayout.FloatField(valueProp.floatValue);
                        break;
                }
            }
            else
            {
                EditorGUILayout.LabelField("字段缺失");
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private void EnsureParameterEntries(SerializedProperty parameterValuesProp, BattleParameterDatabase database)
    {
        if (database == null || database.coreAttributes == null)
            return;

        HashSet<string> existing = new HashSet<string>();
        for (int i = 0; i < parameterValuesProp.arraySize; i++)
        {
            SerializedProperty item = parameterValuesProp.GetArrayElementAtIndex(i);
            SerializedProperty keyProp = item.FindPropertyRelative("parameterKey");
            if (keyProp != null && !string.IsNullOrWhiteSpace(keyProp.stringValue))
                existing.Add(keyProp.stringValue);
        }

        for (int i = 0; i < database.coreAttributes.Count; i++)
        {
            CoreAttributeDefinition def = database.coreAttributes[i];
            if (def == null || string.IsNullOrWhiteSpace(def.key))
                continue;

            if (existing.Contains(def.key))
                continue;

            int index = parameterValuesProp.arraySize;
            parameterValuesProp.InsertArrayElementAtIndex(index);

            SerializedProperty newItem = parameterValuesProp.GetArrayElementAtIndex(index);
            SerializedProperty keyProp = newItem.FindPropertyRelative("parameterKey");
            SerializedProperty valueProp = newItem.FindPropertyRelative("value");

            if (keyProp != null)
                keyProp.stringValue = def.key;

            if (valueProp != null)
                valueProp.floatValue = 0f;

            existing.Add(def.key);
        }
    }

    private SerializedProperty FindParameterEntry(SerializedProperty parameterValuesProp, string parameterKey)
    {
        for (int i = 0; i < parameterValuesProp.arraySize; i++)
        {
            SerializedProperty item = parameterValuesProp.GetArrayElementAtIndex(i);
            SerializedProperty keyProp = item.FindPropertyRelative("parameterKey");
            if (keyProp != null && keyProp.stringValue == parameterKey)
                return item;
        }

        return null;
    }

    private BattleParameterDatabase FindBattleParameterDatabase()
    {
        SerializedProperty dbProp = page.SelectedUnitSO.FindProperty("battleParameterDatabase");
        if (dbProp != null && dbProp.objectReferenceValue is BattleParameterDatabase explicitDb)
            return explicitDb;

        BattleParameterDatabase runtimeDb = FindRuntimeActiveBattleParameterDatabase();
        if (runtimeDb != null)
            return runtimeDb;

        string[] guids = AssetDatabase.FindAssets("t:BattleParameterDatabase");
        if (guids == null || guids.Length == 0)
            return null;

        List<BattleParameterDatabase> list = new List<BattleParameterDatabase>();
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            BattleParameterDatabase db = AssetDatabase.LoadAssetAtPath<BattleParameterDatabase>(path);
            if (db != null)
                list.Add(db);
        }

        if (list.Count == 0)
            return null;

        BattleParameterDatabase main = list.FirstOrDefault(x => x != null && x.databaseId == "battle_parameters_main");
        return main ?? list[0];
    }

    private void DrawSubFoldoutHeader(ref bool expanded, string title)
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 10000f, 20f, 20f, GUILayout.ExpandWidth(true));
        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            expanded = !expanded;

        string marker = expanded ? "-" : "+";
        Rect markerRect = new Rect(rect.x + 2f, rect.y, 16f, rect.height);
        Rect labelRect = new Rect(rect.x + 18f, rect.y, rect.width - 18f, rect.height);

        GUI.Label(markerRect, marker, EditorStyles.boldLabel);
        GUI.Label(labelRect, title, EditorStyles.miniBoldLabel);
    }

    private void DrawUnitOutline()
    {
        page.DrawRow("启用遮挡描边", page.SelectedUnitSO.FindProperty("useOcclusionOutline"));
        page.DrawRow("使用共享描边组", page.SelectedUnitSO.FindProperty("useSharedOutlineGroup"));
    }

    private void DrawUnitPhysics()
    {
        page.DrawRow("使用 Kinematic 刚体", page.SelectedUnitSO.FindProperty("useKinematicBody"));
        page.DrawRow("允许真实物理推挤", page.SelectedUnitSO.FindProperty("allowPhysicalPush"));
        page.DrawRow("冻结旋转 X", page.SelectedUnitSO.FindProperty("freezeRotationX"));
        page.DrawRow("冻结旋转 Y", page.SelectedUnitSO.FindProperty("freezeRotationY"));
        page.DrawRow("冻结旋转 Z", page.SelectedUnitSO.FindProperty("freezeRotationZ"));
    }

    private BattleParameterDatabase FindRuntimeActiveBattleParameterDatabase()
    {
        string[] guids = AssetDatabase.FindAssets("t:BattleParameterDatabase");
        if (guids == null || guids.Length == 0)
            return null;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            BattleParameterDatabase db = AssetDatabase.LoadAssetAtPath<BattleParameterDatabase>(path);
            if (db != null && db.isRuntimeActive)
                return db;
        }

        return null;
    }


    private void DrawUnitAudio()
    {
        SerializedProperty defaultFootstepPackageProp = page.SelectedUnitSO.FindProperty("defaultFootstepAudioPackage");
        if (defaultFootstepPackageProp == null)
        {
            EditorGUILayout.HelpBox("当前 UnitDefinition 尚未包含 defaultFootstepAudioPackage 字段。请确认已替换最新 UnitDefinition.cs。", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            "单位默认脚步声包用于：没有鞋子装备、怪物/机械不走鞋子逻辑、或装备脚步声包缺失时的默认声音。\n" +
            "Player 可选择裸足包；敌人/怪物请按单位类型选择爪、机械、重型生物等专属包，不要全部默认裸足。",
            MessageType.Info);

        page.DrawRow("默认脚步声包", defaultFootstepPackageProp);

        SkyPrisonAudioPackage current = defaultFootstepPackageProp.objectReferenceValue as SkyPrisonAudioPackage;
        if (current != null)
        {
            page.DrawReadonlyRow("包 Key", string.IsNullOrWhiteSpace(current.packageKey) ? current.name : current.packageKey);
            page.DrawReadonlyRow("显示名", string.IsNullOrWhiteSpace(current.displayName) ? current.name : current.displayName);
            page.DrawReadonlyRow("类型", current.packageType.ToString());
            page.DrawReadonlyRow("资源路径", AssetDatabase.GetAssetPath(current));
        }
        else
        {
            page.DrawReadonlyRow("当前脚步声", "未指定");
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(140f);

            if (GUILayout.Button("从脚步声包选择", GUILayout.Width(140f)))
                ShowDefaultFootstepPackageMenu(current);

            using (new EditorGUI.DisabledScope(current == null))
            {
                if (GUILayout.Button("定位", GUILayout.Width(60f)) && current != null)
                {
                    Selection.activeObject = current;
                    EditorGUIUtility.PingObject(current);
                }

                if (GUILayout.Button("清空", GUILayout.Width(60f)))
                {
                    defaultFootstepPackageProp.objectReferenceValue = null;
                    page.SelectedUnitSO.ApplyModifiedProperties();
                    EditorUtility.SetDirty(page.SelectedUnitSO.targetObject);
                }
            }
        }
    }

    private void ShowDefaultFootstepPackageMenu(SkyPrisonAudioPackage current)
    {
        GenericMenu menu = new GenericMenu();
        List<SkyPrisonAudioPackage> packages = FindFootstepAudioPackages();

        if (packages.Count == 0)
        {
            menu.AddDisabledItem(new GUIContent("未找到 SkyPrisonAudioPackage 类型为 Footstep 的音声包"));
        }
        else
        {
            for (int i = 0; i < packages.Count; i++)
            {
                SkyPrisonAudioPackage package = packages[i];
                if (package == null)
                    continue;

                string key = string.IsNullOrWhiteSpace(package.packageKey) ? package.name : package.packageKey;
                string label = string.IsNullOrWhiteSpace(package.displayName) ? key : package.displayName;
                string menuPath = $"{label}  ({key})";
                SkyPrisonAudioPackage captured = package;

                menu.AddItem(new GUIContent(menuPath), current == package, () =>
                {
                    SerializedObject so = page.SelectedUnitSO;
                    so.Update();
                    SerializedProperty prop = so.FindProperty("defaultFootstepAudioPackage");
                    if (prop != null)
                    {
                        prop.objectReferenceValue = captured;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(so.targetObject);
                    }
                });
            }
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("清空"), current == null, () =>
        {
            SerializedObject so = page.SelectedUnitSO;
            so.Update();
            SerializedProperty prop = so.FindProperty("defaultFootstepAudioPackage");
            if (prop != null)
            {
                prop.objectReferenceValue = null;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(so.targetObject);
            }
        });

        menu.ShowAsContext();
    }

    private List<SkyPrisonAudioPackage> FindFootstepAudioPackages()
    {
        List<SkyPrisonAudioPackage> result = new List<SkyPrisonAudioPackage>();
        string[] guids = AssetDatabase.FindAssets("t:SkyPrisonAudioPackage");
        if (guids == null)
            return result;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            SkyPrisonAudioPackage package = AssetDatabase.LoadAssetAtPath<SkyPrisonAudioPackage>(path);
            if (package == null)
                continue;

            if (package.packageType != SkyPrisonAudioPackageType.Footstep)
                continue;

            result.Add(package);
        }

        result.Sort((a, b) => string.Compare(
            string.IsNullOrWhiteSpace(a.displayName) ? a.name : a.displayName,
            string.IsNullOrWhiteSpace(b.displayName) ? b.name : b.displayName,
            StringComparison.OrdinalIgnoreCase));

        return result;
    }

    private void DrawUnitShadow()
    {
        SerializedProperty lockShadow = page.SelectedUnitSO.FindProperty("lockShadowProjectorTransform");
        page.DrawRow("锁定投影位姿", lockShadow);

        using (new EditorGUI.DisabledScope(lockShadow != null && !lockShadow.boolValue))
        {
            page.DrawRow("投影本地位置", page.SelectedUnitSO.FindProperty("shadowLocalPosition"));
            page.DrawRow("投影本地旋转", page.SelectedUnitSO.FindProperty("shadowLocalEuler"));
            page.DrawRow("投影本地缩放", page.SelectedUnitSO.FindProperty("shadowLocalScale"));
        }

        EditorGUILayout.Space(4f);

        SerializedProperty overheadOffsetXProp = page.SelectedUnitSO.FindProperty("overheadUiOffsetX");
        SerializedProperty overheadOffsetYProp = page.SelectedUnitSO.FindProperty("overheadUiOffsetY");
        page.DrawRow("头顶UI偏移X", overheadOffsetXProp);
        page.DrawRow("头顶UI偏移Y", overheadOffsetYProp);
        page.DrawRow("自动判断名字显示", page.SelectedUnitSO.FindProperty("autoOverheadNameVisibility"));

        SerializedProperty manualName = page.SelectedUnitSO.FindProperty("manualShowOverheadName");
        SerializedProperty autoName = page.SelectedUnitSO.FindProperty("autoOverheadNameVisibility");
        using (new EditorGUI.DisabledScope(autoName != null && autoName.boolValue))
        {
            page.DrawRow("手动显示名字", manualName);
        }

        SerializedProperty hpBarStyleProp = page.SelectedUnitSO.FindProperty("overheadHpBarStyle");
        page.DrawRow("HP条样式", hpBarStyleProp);

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("打开单位UI样式编辑器", GUILayout.Height(22f)))
            {
                OverheadBarStyleAsset style = hpBarStyleProp != null ? hpBarStyleProp.objectReferenceValue as OverheadBarStyleAsset : null;
                UnitUIStyleEditorWindow.Open(style);
            }

            using (new EditorGUI.DisabledScope(hpBarStyleProp == null || hpBarStyleProp.objectReferenceValue == null))
            {
                if (GUILayout.Button("定位样式", GUILayout.Height(22f)))
                {
                    Selection.activeObject = hpBarStyleProp.objectReferenceValue;
                    EditorGUIUtility.PingObject(hpBarStyleProp.objectReferenceValue);
                }
            }
        }

        if (GUI.changed)
            PushOverheadOffsetToPrefab(overheadOffsetXProp, overheadOffsetYProp);
    }

    private void TryAutoPullOverheadOffsetOnSelection(UnitDefinition unit, SerializedObject so)
    {
        if (unit == null || so == null || unit.prefab == null)
            return;

        string unitPath = AssetDatabase.GetAssetPath(unit);
        if (unitPath == lastOverheadSyncUnitPath)
            return;

        SerializedProperty overheadOffsetXProp = so.FindProperty("overheadUiOffsetX");
        SerializedProperty overheadOffsetYProp = so.FindProperty("overheadUiOffsetY");
        if (overheadOffsetXProp == null || overheadOffsetYProp == null)
            return;

        PullOverheadOffsetFromPrefab(overheadOffsetXProp, overheadOffsetYProp);
        so.ApplyModifiedPropertiesWithoutUndo();
        lastOverheadSyncUnitPath = unitPath;
    }

    private void PullOverheadOffsetFromPrefab(SerializedProperty overheadOffsetXProp, SerializedProperty overheadOffsetYProp)
    {
        UnitDefinition unit = page.SelectedUnitDefinition;
        if (unit == null || unit.prefab == null || overheadOffsetXProp == null || overheadOffsetYProp == null)
            return;

        string prefabPath = AssetDatabase.GetAssetPath(unit.prefab);
        if (string.IsNullOrWhiteSpace(prefabPath))
            return;

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform anchor = FindDeepChild(prefabRoot.transform, "OverheadAnchor");
            if (anchor == null)
                return;

            overheadOffsetXProp.floatValue = anchor.localPosition.x;
            overheadOffsetYProp.floatValue = anchor.localPosition.y;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private void PushOverheadOffsetToPrefab(SerializedProperty overheadOffsetXProp, SerializedProperty overheadOffsetYProp)
    {
        UnitDefinition unit = page.SelectedUnitDefinition;
        if (unit == null || unit.prefab == null || overheadOffsetXProp == null || overheadOffsetYProp == null)
            return;

        string prefabPath = AssetDatabase.GetAssetPath(unit.prefab);
        if (string.IsNullOrWhiteSpace(prefabPath))
            return;

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform anchor = FindDeepChild(prefabRoot.transform, "OverheadAnchor");
            if (anchor == null)
                return;

            Vector3 pos = anchor.localPosition;
            pos.x = overheadOffsetXProp.floatValue;
            pos.y = overheadOffsetYProp.floatValue;
            anchor.localPosition = pos;

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private Transform FindDeepChild(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == targetName)
                return child;

            Transform nested = FindDeepChild(child, targetName);
            if (nested != null)
                return nested;
        }

        return null;
    }


    private void DrawUnitVision()
    {
        UnitDefinition unit = page.SelectedUnitDefinition;
        if (unit == null)
            return;

        SerializedProperty defineTypeProp = page.SelectedUnitSO.FindProperty("defineType");
        if (defineTypeProp == null || (UnitDefineType)defineTypeProp.enumValueIndex != UnitDefineType.Character)
        {
            EditorGUILayout.HelpBox("只有 defineType == Character 的单位才显示视野范围模块。", MessageType.None);
            return;
        }

        SerializedProperty identityProp = page.SelectedUnitSO.FindProperty("characterIdentity");
        SerializedProperty enableVisionProp = page.SelectedUnitSO.FindProperty("enableVision");
        SerializedProperty visionRadiusProp = page.SelectedUnitSO.FindProperty("visionRadius");
        SerializedProperty visionHeightProp = page.SelectedUnitSO.FindProperty("visionHeight");
        SerializedProperty visionShapeProp = page.SelectedUnitSO.FindProperty("visionShape");
        SerializedProperty visionAngleProp = page.SelectedUnitSO.FindProperty("visionAngle");
        SerializedProperty useFacingDirectionProp = page.SelectedUnitSO.FindProperty("useFacingDirection");
        SerializedProperty ignoreObstacleOcclusionProp = page.SelectedUnitSO.FindProperty("ignoreObstacleOcclusion");
        SerializedProperty shareVisionToFactionProp = page.SelectedUnitSO.FindProperty("shareVisionToFaction");
        SerializedProperty ignoreFogOfWarProp = page.SelectedUnitSO.FindProperty("ignoreFogOfWar");

        if (enableVisionProp == null)
        {
            EditorGUILayout.HelpBox("UnitDefinition 当前缺少视野字段。请同时确认 UnitDefinition.cs 已替换为带视野字段的版本。", MessageType.Warning);
            return;
        }

        CharacterIdentity identity = identityProp != null
            ? (CharacterIdentity)identityProp.enumValueIndex
            : CharacterIdentity.Player;

        bool isPlayer = identity == CharacterIdentity.Player;
        bool isHostile = identity == CharacterIdentity.Enemy ||
                         identity == CharacterIdentity.Elite ||
                         identity == CharacterIdentity.Boss ||
                         identity == CharacterIdentity.NeutralHostile;

        page.DrawRow("启用视野", enableVisionProp);

        using (new EditorGUI.DisabledScope(enableVisionProp != null && !enableVisionProp.boolValue))
        {
            page.DrawRow("视野半径", visionRadiusProp);
            page.DrawRow("视野高度", visionHeightProp);

            if (isPlayer)
            {
                if (visionShapeProp != null)
                    visionShapeProp.enumValueIndex = (int)UnitVisionShape.Circle;
                if (visionAngleProp != null)
                    visionAngleProp.floatValue = 360f;
                if (useFacingDirectionProp != null)
                    useFacingDirectionProp.boolValue = false;

                page.DrawReadonlyRow("视野形状", "圆形");
                page.DrawReadonlyRow("视野角度", "360°");
                page.DrawReadonlyRow("受朝向影响", "否");
                page.DrawRow("无视遮挡", ignoreObstacleOcclusionProp);
                page.DrawRow("共享给阵营", shareVisionToFactionProp);
                page.DrawRow("免疫战争迷雾（全开视野）", ignoreFogOfWarProp);

                EditorGUILayout.HelpBox(
                    "玩家单位第一阶段默认使用圆形全向视野。\n运行时强制：visionShape = Circle，visionAngle = 360，useFacingDirection = false。",
                    MessageType.None);
            }
            else if (isHostile)
            {
                if (visionShapeProp != null)
                    visionShapeProp.enumValueIndex = (int)UnitVisionShape.Sector;

                page.DrawReadonlyRow("视野形状", "扇区");
                page.DrawRow("视野角度", visionAngleProp);
                page.DrawRow("受朝向影响", useFacingDirectionProp);
                page.DrawRow("无视遮挡", ignoreObstacleOcclusionProp);
                page.DrawRow("共享给阵营", shareVisionToFactionProp);
                page.DrawRow("免疫战争迷雾（全开视野）", ignoreFogOfWarProp);

                EditorGUILayout.HelpBox(
                    "敌对角色第一阶段默认使用扇区视野。\n可填写角度，并决定是否受朝向影响。",
                    MessageType.None);
            }
            else
            {
                if (visionShapeProp != null)
                    visionShapeProp.enumValueIndex = (int)UnitVisionShape.Circle;
                if (visionAngleProp != null)
                    visionAngleProp.floatValue = 360f;
                if (useFacingDirectionProp != null)
                    useFacingDirectionProp.boolValue = false;

                page.DrawReadonlyRow("视野形状", "圆形");
                page.DrawReadonlyRow("视野角度", "360°");
                page.DrawReadonlyRow("受朝向影响", "否");
                page.DrawRow("无视遮挡", ignoreObstacleOcclusionProp);
                page.DrawRow("共享给阵营", shareVisionToFactionProp);
                page.DrawRow("免疫战争迷雾（全开视野）", ignoreFogOfWarProp);

                EditorGUILayout.HelpBox(
                    "其他 Character 第一阶段先按圆形全向视野处理，后续再扩例外。",
                    MessageType.None);
            }
        }
    }


    private void DrawUnitHearing()
    {
        SerializedObject so = page.SelectedUnitSO;
        if (so == null)
            return;

        SerializedProperty defineTypeProp = so.FindProperty("defineType");
        if (defineTypeProp == null || (UnitDefineType)defineTypeProp.enumValueIndex != UnitDefineType.Character)
        {
            EditorGUILayout.HelpBox("只有 defineType == Character 的单位才显示听觉感知模块。", MessageType.None);
            return;
        }

        SerializedProperty canUseHearingProp = so.FindProperty("canUseHearing");
        SerializedProperty canBePerceivedProp = so.FindProperty("canBePerceived");
        SerializedProperty hearingMultiplierProp = so.FindProperty("hearingMultiplier");
        SerializedProperty hearingBaseRangeProp = so.FindProperty("hearingBaseRange");
        SerializedProperty hearingMaxRangeProp = so.FindProperty("hearingMaxRange");
        SerializedProperty hearingSuspicionThresholdProp = so.FindProperty("hearingSuspicionThreshold");
        SerializedProperty hearingAlertThresholdProp = so.FindProperty("hearingAlertThreshold");
        SerializedProperty hearingDetectThresholdProp = so.FindProperty("hearingDetectThreshold");
        SerializedProperty hearingMemorySecondsProp = so.FindProperty("hearingMemorySeconds");

        if (canUseHearingProp == null || hearingMultiplierProp == null)
        {
            EditorGUILayout.HelpBox(
                "当前 UnitDefinition.cs 尚未包含听觉感知字段。请先替换为带 canUseHearing / hearingMultiplier 的 UnitDefinition.cs。",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            "听觉感知用于 AI 判断声音事件，不等同于玩家实际听到的音量。\n" +
            "听力倍率：1.0 = 普通听力，20.0 = 顺风耳，0.0 = 完全听不见。",
            MessageType.None);

        page.DrawRow("启用听觉感知", canUseHearingProp);
        page.DrawRow("可被感知", canBePerceivedProp);

        using (new EditorGUI.DisabledScope(canUseHearingProp != null && !canUseHearingProp.boolValue))
        {
            DrawNonNegativeFloatRow("听力倍率", hearingMultiplierProp);
            DrawNonNegativeFloatRow("听觉基础范围", hearingBaseRangeProp);
            DrawNonNegativeFloatRow("听觉最大范围", hearingMaxRangeProp);

            if (hearingBaseRangeProp != null && hearingMaxRangeProp != null && hearingMaxRangeProp.floatValue < hearingBaseRangeProp.floatValue)
                hearingMaxRangeProp.floatValue = hearingBaseRangeProp.floatValue;

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("听觉阈值", EditorStyles.boldLabel);
            DrawNonNegativeFloatRow("怀疑阈值", hearingSuspicionThresholdProp);
            DrawNonNegativeFloatRow("警戒阈值", hearingAlertThresholdProp);
            DrawNonNegativeFloatRow("发现阈值", hearingDetectThresholdProp);

            if (hearingSuspicionThresholdProp != null && hearingAlertThresholdProp != null && hearingAlertThresholdProp.floatValue < hearingSuspicionThresholdProp.floatValue)
                hearingAlertThresholdProp.floatValue = hearingSuspicionThresholdProp.floatValue;

            if (hearingAlertThresholdProp != null && hearingDetectThresholdProp != null && hearingDetectThresholdProp.floatValue < hearingAlertThresholdProp.floatValue)
                hearingDetectThresholdProp.floatValue = hearingAlertThresholdProp.floatValue;

            DrawNonNegativeFloatRow("听觉记忆秒数", hearingMemorySecondsProp);
        }

        EditorGUILayout.HelpBox(
            "推荐起点：普通敌人听力倍率 1.0；精英 2~4；特殊顺风耳 10~20。\n" +
            "听觉最大范围控制接收声音事件的上限，阈值控制怀疑 / 警戒 / 发现分层。",
            MessageType.Info);
    }

    private void DrawNonNegativeFloatRow(string label, SerializedProperty prop)
    {
        if (prop == null)
            return;

        page.DrawRow(label, prop);
        if (prop.floatValue < 0f)
            prop.floatValue = 0f;
    }

    private void DrawUnitCollision()
    {
        SerializedProperty overrideCollision = page.SelectedUnitSO.FindProperty("overrideCollisionShape");
        page.DrawRow("覆盖默认碰撞形状", overrideCollision);

        using (new EditorGUI.DisabledScope(overrideCollision != null && !overrideCollision.boolValue))
        {
            page.DrawRow("碰撞中心", page.SelectedUnitSO.FindProperty("collisionLocalCenter"));
            page.DrawRow("碰撞半径", page.SelectedUnitSO.FindProperty("collisionRadius"));
            page.DrawRow("碰撞高度", page.SelectedUnitSO.FindProperty("collisionHeight"));
        }
    }

    private void DrawUnitMovement()
    {
        SerializedProperty movementType = page.SelectedUnitSO.FindProperty("movementType");
        page.DrawRow("移动类型", movementType);

        bool immobile = movementType != null &&
                        (UnitMovementType)movementType.enumValueIndex == UnitMovementType.Immobile;

        using (new EditorGUI.DisabledScope(immobile))
        {
            page.DrawRow("理想步行速度", page.SelectedUnitSO.FindProperty("idealWalkSpeed"));
            page.DrawRow("理想潜行速度", page.SelectedUnitSO.FindProperty("idealSneakSpeed"));
            page.DrawRow("理想奔跑速度", page.SelectedUnitSO.FindProperty("idealRunSpeed"));
            page.DrawRow("跳跃高度", page.SelectedUnitSO.FindProperty("idealJumpHeight"));
            page.DrawRow("轻负重跳跃倍率", page.SelectedUnitSO.FindProperty("lightBurdenJumpHeightMultiplier"));
            page.DrawRow("中负重跳跃倍率", page.SelectedUnitSO.FindProperty("mediumBurdenJumpHeightMultiplier"));
            page.DrawRow("重负重跳跃倍率", page.SelectedUnitSO.FindProperty("heavyBurdenJumpHeightMultiplier"));
            page.DrawRow("超重禁止跳跃", page.SelectedUnitSO.FindProperty("overweightDisablesJump"));
            page.DrawRow("移动惯性", page.SelectedUnitSO.FindProperty("movementInertia"));
            page.DrawRow("最低步行速度", page.SelectedUnitSO.FindProperty("minWalkSpeed"));
            page.DrawRow("持续移动倍率", page.SelectedUnitSO.FindProperty("sustainedMoveSpeedMultiplier"));
            page.DrawRow("持续移动延迟", page.SelectedUnitSO.FindProperty("sustainedMoveDelay"));
        }
    }

    private void DrawUnitAnimationKeys()
    {
        SerializedProperty defineType = page.SelectedUnitSO.FindProperty("defineType");
        bool isCharacter = defineType != null && (UnitDefineType)defineType.enumValueIndex == UnitDefineType.Character;

        using (new EditorGUI.DisabledScope(!isCharacter))
        {
            SerializedProperty keys = page.SelectedUnitSO.FindProperty("animationKeys");
            if (keys == null)
            {
                EditorGUILayout.HelpBox("UnitDefinition 缺少 animationKeys 字段。请确认 UnitDefinition.cs 已更新。", MessageType.Warning);
                return;
            }

            page.DrawRow("驱动移动动画", keys.FindPropertyRelative("driveMovementAnimation"));
            page.DrawRow("动画淡入时间", keys.FindPropertyRelative("movementAnimationFade"));
            page.DrawRow("单次动作锁定秒数", keys.FindPropertyRelative("oneShotLockSeconds"));

            GUILayout.Space(6f);
            EditorGUILayout.LabelField("移动状态 Key", EditorStyles.miniBoldLabel);
            DrawAnimationKeyRow(keys, "待机 Key", "idleKey");
            DrawAnimationKeyRow(keys, "行走 Key", "walkKey");
            DrawAnimationKeyRow(keys, "奔跑 Key", "runKey");
            DrawAnimationKeyRow(keys, "潜行 Key", "sneakKey");

            GUILayout.Space(6f);
            EditorGUILayout.LabelField("跳跃 Key", EditorStyles.miniBoldLabel);
            DrawAnimationKeyRow(keys, "起跳 Key", "jumpStartKey", "jumpKey");
            DrawAnimationKeyRow(keys, "滞空 Key", "jumpAirKey");
            DrawAnimationKeyRow(keys, "落地 Key", "jumpLandKey");

            GUILayout.Space(6f);
            EditorGUILayout.LabelField("闪避 Key", EditorStyles.miniBoldLabel);
            DrawAnimationKeyRow(keys, "向前闪避 Key", "dodgeForwardKey", "dodgeKey");
            DrawAnimationKeyRow(keys, "向后闪避 Key", "dodgeBackKey");
            DrawAnimationKeyRow(keys, "通用闪避 Key", "dodgeKey");

            GUILayout.Space(6f);
            EditorGUILayout.LabelField("战斗 / 受击 / 死亡 Key", EditorStyles.miniBoldLabel);
            DrawAnimationKeyRow(keys, "攻击 Key", "attackKey");
            DrawAnimationKeyRow(keys, "受击 Key", "hitKey");
            DrawAnimationKeyRow(keys, "死亡 Key", "deathKey");

            if (keys.FindPropertyRelative("jumpStartKey") == null ||
                keys.FindPropertyRelative("jumpAirKey") == null ||
                keys.FindPropertyRelative("jumpLandKey") == null ||
                keys.FindPropertyRelative("dodgeForwardKey") == null ||
                keys.FindPropertyRelative("dodgeBackKey") == null)
            {
                EditorGUILayout.HelpBox(
                    "当前 UnitAnimationKeySet 还没有完整的新动作字段。窗口会先兼容旧字段 jumpKey / dodgeKey，但要完整保存起跳/滞空/落地、前闪/后闪，请在 UnitDefinition.cs 的 UnitAnimationKeySet 中补上：\n" +
                    "public string jumpStartKey = \"Jump_Start\";\n" +
                    "public string jumpAirKey = \"Jump_Air\";\n" +
                    "public string jumpLandKey = \"Jump_Land\";\n" +
                    "public string dodgeForwardKey = \"dodge_forward\";\n" +
                    "public string dodgeBackKey = \"dodge_back\";",
                    MessageType.Warning);
            }
        }

        if (!isCharacter)
            EditorGUILayout.HelpBox("只有人物类型单位需要填写动作动画 Key。", MessageType.Info);

        EditorGUILayout.HelpBox(
            "这些 Key 会由 RuntimeApplier 写入 UnitMovementController。移动脚本会自动播放待机 / 行走 / 奔跑 / 潜行；跳跃已经拆成起跳 / 滞空 / 落地；闪避已经拆成向前 / 向后，通用闪避 Key 只作为旧资源兜底。",
            MessageType.None);
    }

    private void DrawAnimationKeyRow(SerializedProperty keys, string label, string primaryFieldName, string fallbackFieldName = null)
    {
        if (keys == null)
            return;

        SerializedProperty prop = keys.FindPropertyRelative(primaryFieldName);
        if (prop != null)
        {
            page.DrawRow(label, prop);
            return;
        }

        if (!string.IsNullOrWhiteSpace(fallbackFieldName))
        {
            SerializedProperty fallback = keys.FindPropertyRelative(fallbackFieldName);
            if (fallback != null)
            {
                page.DrawRow($"{label}（兼容旧 {fallbackFieldName}）", fallback);
                return;
            }
        }

        EditorGUILayout.HelpBox($"字段 {primaryFieldName} 不存在。", MessageType.Warning);
    }

    private void DrawUnitCombatModule()
    {
        SerializedObject so = page.SelectedUnitSO;
        UnitDefinition   ud = page.SelectedUnitDefinition;
        if (so == null) return;

        bool isCharacter = ud != null && ud.defineType == UnitDefineType.Character;
        using (new EditorGUI.DisabledScope(!isCharacter))
        {
            SerializedProperty moduleProp = so.FindProperty("defaultCombatModule");
            if (moduleProp == null)
            {
                EditorGUILayout.HelpBox("UnitDefinition 缺少 defaultCombatModule 字段。", MessageType.Warning);
                return;
            }

            WeaponCombatModule current = moduleProp.objectReferenceValue as WeaponCombatModule;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("默认战斗模组", GUILayout.Width(140));
            EditorGUI.BeginChangeCheck();
            var newMod = (WeaponCombatModule)EditorGUILayout.ObjectField(
                current, typeof(WeaponCombatModule), false);
            if (EditorGUI.EndChangeCheck())
                moduleProp.objectReferenceValue = newMod;
            EditorGUILayout.EndHorizontal();

            if (current != null)
            {
                EditorGUILayout.BeginVertical("box");

                // 模组信息摘要
                var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = new Color(0.92f, 0.88f, 0.55f) } };
                EditorGUILayout.LabelField($"Key: {current.moduleKey}    {current.displayName}", labelStyle);

                // 连段预览
                var combo = current.lightAttackCombo;
                if (combo != null && combo.Count > 0)
                {
                    string chain = string.Join("  →  ", combo.ConvertAll(
                        s => s != null ? (string.IsNullOrWhiteSpace(s.displayName) ? s.skillKey : s.displayName) : "?"));
                    EditorGUILayout.LabelField($"轻攻击: {chain}", EditorStyles.wordWrappedMiniLabel);
                }
                else
                    EditorGUILayout.LabelField("轻攻击: 未配置", EditorStyles.miniLabel);

                if (current.heavyAttack != null)
                {
                    string hName = string.IsNullOrWhiteSpace(current.heavyAttack.displayName)
                        ? current.heavyAttack.skillKey : current.heavyAttack.displayName;
                    EditorGUILayout.LabelField($"重攻击: {hName}", EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
            }
            else if (isCharacter)
                EditorGUILayout.HelpBox("未设置战斗模组，角色将无法使用攻击技能。", MessageType.Warning);
        }

        if (!isCharacter)
            EditorGUILayout.HelpBox("战斗模组仅适用于 Character 类型单位。", MessageType.None);
    }

    private void DrawUnitBloodVFX()
    {
        SerializedProperty bloodVFXType = page.SelectedUnitSO.FindProperty("bloodVFXType");
        SerializedProperty bloodColor   = page.SelectedUnitSO.FindProperty("bloodColor");

        page.DrawRow("出血演出类型", bloodVFXType);

        UnitBloodVFXType currentType = bloodVFXType != null
            ? (UnitBloodVFXType)bloodVFXType.enumValueIndex
            : UnitBloodVFXType.Normal;

        bool showColor = currentType == UnitBloodVFXType.Normal || currentType == UnitBloodVFXType.DarkBlood;
        using (new EditorGUI.DisabledScope(!showColor))
            page.DrawRow("血液颜色", bloodColor);

        if (currentType == UnitBloodVFXType.None)
            EditorGUILayout.HelpBox("不显示任何出血特效（机械、魂体等无血单位）。", MessageType.None);
        else if (currentType == UnitBloodVFXType.MetalSpark)
            EditorGUILayout.HelpBox("金属火花。预制体在 BloodVFXSettings.metalSparkPrefabs，为空时回退到普通飞溅。", MessageType.None);
        else if (currentType == UnitBloodVFXType.EnergyBurst)
            EditorGUILayout.HelpBox("能量爆散。预制体在 BloodVFXSettings.energyBurstPrefabs，为空时回退到普通飞溅。", MessageType.None);
    }

    private void DrawUnitDeath()
    {
        SerializedProperty playDeathAnimation = page.SelectedUnitSO.FindProperty("playDeathAnimation");
        SerializedProperty useCorpseDecay = page.SelectedUnitSO.FindProperty("useCorpseDecay");

        page.DrawRow("播放死亡动画", playDeathAnimation);

        using (new EditorGUI.DisabledScope(playDeathAnimation != null && !playDeathAnimation.boolValue))
        {
            page.DrawRow("死亡动画 Trigger", page.SelectedUnitSO.FindProperty("deathTriggerName"));
        }

        page.DrawRow("启用尸体消亡", useCorpseDecay);

        using (new EditorGUI.DisabledScope(useCorpseDecay != null && !useCorpseDecay.boolValue))
        {
            page.DrawRow("尸体保留秒数", page.SelectedUnitSO.FindProperty("corpseDecaySeconds"));
            page.DrawRow("尸体清理方式", page.SelectedUnitSO.FindProperty("corpseCleanupMode"));

            SerializedProperty useDeathDissolve = page.SelectedUnitSO.FindProperty("useDeathDissolve");
            page.DrawRow("死亡溶解特效", useDeathDissolve);

            using (new EditorGUI.DisabledScope(useDeathDissolve != null && !useDeathDissolve.boolValue))
            {
                page.DrawRow("溶解时长（秒）", page.SelectedUnitSO.FindProperty("deathDissolveSeconds"));
                page.DrawRow("压黑阶段占比", page.SelectedUnitSO.FindProperty("deathDissolveDarkenFraction"));
                page.DrawRow("溶解边缘颜色", page.SelectedUnitSO.FindProperty("deathDissolveEdgeColor"));
                page.DrawRow("溶解边缘宽度", page.SelectedUnitSO.FindProperty("deathDissolveEdgeWidth"));
                page.DrawRow("溶解噪波贴图", page.SelectedUnitSO.FindProperty("deathDissolveNoiseTexture"));
                page.DrawRow("溶解噪波密度", page.SelectedUnitSO.FindProperty("deathDissolveNoiseScale"));
            }
        }

        page.DrawRow("作为可复活玩家处理", page.SelectedUnitSO.FindProperty("treatAsRespawnablePlayer"));
    }

    private readonly Dictionary<int, bool> _dropProfileFoldouts = new Dictionary<int, bool>();

    private void DrawUnitDropProfiles()
    {
        SerializedProperty dropProfilesProp = page.SelectedUnitSO.FindProperty("dropProfiles");
        if (dropProfilesProp == null) return;

        bool deleted = false;
        for (int i = 0; i < dropProfilesProp.arraySize; i++)
        {
            SerializedProperty elem = dropProfilesProp.GetArrayElementAtIndex(i);
            DropProfile profile = elem.objectReferenceValue as DropProfile;

            // 标题行
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();

            if (!_dropProfileFoldouts.ContainsKey(i)) _dropProfileFoldouts[i] = false;
            string label = profile != null
                ? (!string.IsNullOrWhiteSpace(profile.displayName) ? profile.displayName : profile.name)
                : $"（空）掉落配置 {i + 1}";
            string summary = profile != null ? $"  {profile.pools.Count} 池" : "";
            _dropProfileFoldouts[i] = EditorGUILayout.Foldout(_dropProfileFoldouts[i], label + summary, true, EditorStyles.foldoutHeader);

            string btnLabel = profile != null
                ? (!string.IsNullOrWhiteSpace(profile.displayName) ? profile.displayName : profile.name)
                : "点击选择…";
            if (GUILayout.Button(btnLabel, EditorStyles.miniButton, GUILayout.Width(180f)))
            {
                int captured = i;
                DropProfilePickerWindow.Open(profile, picked =>
                {
                    dropProfilesProp.GetArrayElementAtIndex(captured).objectReferenceValue = picked;
                    page.SelectedUnitSO.ApplyModifiedProperties();
                });
            }
            if (GUILayout.Button("✕", GUILayout.Width(22f)))
            {
                dropProfilesProp.DeleteArrayElementAtIndex(i);
                page.SelectedUnitSO.ApplyModifiedProperties();
                deleted = true;
            }
            EditorGUILayout.EndHorizontal();

            // 展开内容
            if (!deleted && _dropProfileFoldouts.ContainsKey(i) && _dropProfileFoldouts[i] && profile != null)
            {
                EditorGUI.indentLevel++;
                for (int p = 0; p < profile.pools.Count; p++)
                {
                    DropProfile.DropPool pool = profile.pools[p];
                    if (pool == null) continue;

                    string poolMode = pool.poolMode == DropPoolMode.IndependentRolls ? "独立" : "权重";
                    string poolHeader = $"[{poolMode}] {pool.poolName}  ({pool.entries.Count} 项)";
                    EditorGUILayout.LabelField(poolHeader, EditorStyles.miniBoldLabel);

                    EditorGUI.indentLevel++;
                    for (int e = 0; e < pool.entries.Count; e++)
                    {
                        DropProfile.DropEntry entry = pool.entries[e];
                        if (entry == null) continue;
                        string rawName = entry.itemDefinition != null ? entry.itemDefinition.name : "（空）";
                        string itemName = entry.itemDefinition is ItemDefinition idef && !string.IsNullOrWhiteSpace(idef.displayName)
                            ? idef.displayName : rawName;
                        string chanceStr = pool.poolMode == DropPoolMode.IndependentRolls
                            ? $"{entry.dropChance:0.#}%"
                            : $"权重 {entry.weight:0.#}";
                        string countStr = entry.minCount == entry.maxCount ? $"x{entry.minCount}" : $"x{entry.minCount}~{entry.maxCount}";
                        EditorGUILayout.LabelField($"• {itemName}  {chanceStr}  {countStr}", EditorStyles.miniLabel);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            if (deleted) break;
            GUILayout.Space(2f);
        }

        GUILayout.Space(4f);
        if (GUILayout.Button("+ 添加掉落池", GUILayout.Height(24f)))
        {
            dropProfilesProp.InsertArrayElementAtIndex(dropProfilesProp.arraySize);
            dropProfilesProp.GetArrayElementAtIndex(dropProfilesProp.arraySize - 1).objectReferenceValue = null;
            page.SelectedUnitSO.ApplyModifiedProperties();
        }
    }

    private void CreateNewAIPackageFromUnitPage(SerializedProperty aiPackageProp)
    {
        page.EnsureFolderExists(SkyPrisonUnitDefinitionPage.DefaultAICreateFolder);

        string uniqueId = page.GenerateUniqueAIIdForUnitPage("new_ai");
        AIBehaviorPackage asset = ScriptableObject.CreateInstance<AIBehaviorPackage>();
        asset.aiId = uniqueId;
        asset.displayName = "新AI行为包";

        string path = AssetDatabase.GenerateUniqueAssetPath(
            SkyPrisonUnitDefinitionPage.DefaultAICreateFolder + "/AI_NewBehaviorPackage.asset");

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        aiPackageProp.objectReferenceValue = asset;
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }
}
