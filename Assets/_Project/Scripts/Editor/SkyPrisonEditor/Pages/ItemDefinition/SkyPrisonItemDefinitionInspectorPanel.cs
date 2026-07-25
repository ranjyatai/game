using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Spine.Unity;

public class SkyPrisonItemDefinitionInspectorPanel
{
    private readonly SkyPrisonItemDefinitionPage page;

    private int selectedCurrencyIndex = -1;
    private int _spritePickerControlId = -1;

    private bool editingCurrencyName = false;
    private bool editingCurrencyUnit = false;
    private int editingCurrencyRowIndex = -1;
    private string currencyNameEditBuffer = "";
    private string currencyUnitEditBuffer = "";

    public SkyPrisonItemDefinitionInspectorPanel(SkyPrisonItemDefinitionPage page)
    {
        this.page = page;
    }

    public void Draw()
    {
        ItemDefinition item = page.SelectedItemDefinition;
        SerializedObject so = page.SelectedItemSO;

        if (item == null || so == null)
        {
            EditorGUILayout.HelpBox("请先在左侧选择一个物品定义。", MessageType.Info);
            return;
        }

        DrawWorkspaceHeader(item);

        GUILayout.Space(8f);

        page.DrawFoldoutSection("基础信息", DrawBasicInfo);
        page.DrawFoldoutSection("多语言名称", DrawLocalizedNames);
        page.DrawFoldoutSection("多语言描述", DrawLocalizedDescriptions);
        page.DrawFoldoutSection("使用规则", DrawUsageRules);
        page.DrawFoldoutSection("分类与价值", DrawCategoryAndEconomy);
        DrawMajorCategoryExtensionContainer();
        page.DrawFoldoutSection("使用效果", DrawEffects);
        page.DrawFoldoutSection("价格与货币", DrawCurrencyAndPrices);
    }

    private void DrawWorkspaceHeader(ItemDefinition item)
    {
        EditorGUILayout.BeginVertical("box");

        string title = GetBestDisplayName(item);

        EditorGUILayout.LabelField("物品定义工作台", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);

        EditorGUILayout.Space(6f);

        page.DrawReadonlyRow("资源路径", AssetDatabase.GetAssetPath(item));
        page.DrawReadonlyRow("物品 ID", item.itemId.ToString());
        page.DrawReadonlyRow("ItemKey", string.IsNullOrWhiteSpace(item.itemKey) ? "-" : item.itemKey);

        SerializedProperty displayNameProp = page.SelectedItemSO.FindProperty("displayName");
        if (displayNameProp != null)
            page.DrawRow("显示名称", displayNameProp);

        EditorGUILayout.Space(4f);
        page.DrawPingButtons(item);

        EditorGUILayout.EndVertical();
    }

    private string GetBestDisplayName(ItemDefinition item)
    {
        if (item == null)
            return "未命名道具";

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        string defaultLanguageCode = GetDefaultLanguageCode(settings);

        string localized = GetLocalizedText(item.localizedNames, defaultLanguageCode);
        if (!string.IsNullOrWhiteSpace(localized))
            return localized;

        if (!string.IsNullOrWhiteSpace(item.displayName))
            return item.displayName;

        return item.name;
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

    private void DrawBasicInfo()
    {
        SyncDescriptionBackfieldFromDefaultLanguage();
        DrawIconField();
        DrawItemIdRow();
        DrawReadonlyKeyRow("ItemKey", page.SelectedItemSO.FindProperty("itemKey"));
        DrawReadonlyKeyRow("名字 Key", page.SelectedItemSO.FindProperty("nameKey"));
        DrawReadonlyKeyRow("描述 Key", page.SelectedItemSO.FindProperty("descKey"));
        DrawReadonlyKeyRow("图标 Key", page.SelectedItemSO.FindProperty("iconKey"));
        page.DrawRow("备注", page.SelectedItemSO.FindProperty("note"));
        DrawReadonlyMultiline("主语言描述", GetPrimaryDescription(page.SelectedItemDefinition));
        EditorGUILayout.HelpBox("主语言描述由多语言描述自动同步，这里不直接编辑。", MessageType.None);
    }

    private string GetPrimaryDescription(ItemDefinition item)
    {
        if (item == null)
            return string.Empty;

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        string defaultLanguageCode = GetDefaultLanguageCode(settings);
        string localized = GetLocalizedText(item.localizedDescriptions, defaultLanguageCode);
        if (!string.IsNullOrWhiteSpace(localized))
            return localized;
        return item.description ?? string.Empty;
    }

    private void DrawReadonlyMultiline(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(value) ? "-" : value, GUILayout.MinHeight(54f));
        EditorGUILayout.EndHorizontal();
    }

    private void SyncDescriptionBackfieldFromDefaultLanguage()
    {
        SerializedProperty descriptionsProp = page.SelectedItemSO.FindProperty("localizedDescriptions");
        if (descriptionsProp == null)
            return;

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        if (settings == null)
            return;

        EnsureLocalizedEntries(descriptionsProp, settings);
        string defaultLanguageCode = GetDefaultLanguageCode(settings);
        SerializedProperty defaultEntry = FindLocalizedEntry(descriptionsProp, defaultLanguageCode);
        if (defaultEntry == null)
            return;

        SerializedProperty defaultTextProp = defaultEntry.FindPropertyRelative("text");
        SerializedProperty descriptionProp = page.SelectedItemSO.FindProperty("description");
        if (defaultTextProp == null || descriptionProp == null)
            return;

        string value = defaultTextProp.stringValue ?? "";
        if (descriptionProp.stringValue != value)
            descriptionProp.stringValue = value;
    }

    private void DrawIconField()
    {
        SerializedProperty iconProp = page.SelectedItemSO.FindProperty("icon");

        // 接收 ObjectPicker 回调
        Event e = Event.current;
        if (e.commandName == "ObjectSelectorUpdated"
            && EditorGUIUtility.GetObjectPickerControlID() == _spritePickerControlId)
        {
            UnityEngine.Object picked = EditorGUIUtility.GetObjectPickerObject();
            if (picked is Sprite || picked == null)
            {
                iconProp.objectReferenceValue = picked;
                page.SelectedItemSO.ApplyModifiedProperties();
            }
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("图标", GUILayout.Width(140f));

        EditorGUILayout.BeginVertical();
        EditorGUILayout.PropertyField(iconProp, GUIContent.none, true);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("选择图标", GUILayout.Width(80f)))
        {
            _spritePickerControlId = EditorGUIUtility.GetControlID(FocusType.Passive);
            EditorGUIUtility.ShowObjectPicker<Sprite>(
                iconProp.objectReferenceValue as Sprite,
                false, null, _spritePickerControlId);
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

    private void DrawItemIdRow()
    {
        SerializedProperty itemIdProp = page.SelectedItemSO.FindProperty("itemId");
        if (itemIdProp == null)
            return;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("道具 ID", GUILayout.Width(140f));
        itemIdProp.intValue = EditorGUILayout.IntField(itemIdProp.intValue);
        EditorGUILayout.EndHorizontal();

        if (itemIdProp.intValue <= 0)
            itemIdProp.intValue = page.GenerateUniqueItemId(10000);
    }

    private void DrawReadonlyKeyRow(string label, SerializedProperty property)
    {
        string value = property != null ? property.stringValue : "";
        page.DrawReadonlyRow(label, string.IsNullOrWhiteSpace(value) ? "-" : value);
    }

    private void DrawLocalizedNames()
    {
        SerializedProperty prop = page.SelectedItemSO.FindProperty("localizedNames");
        DrawLocalizedTextList(prop, false, true);
    }

    private void DrawLocalizedDescriptions()
    {
        SerializedProperty prop = page.SelectedItemSO.FindProperty("localizedDescriptions");
        DrawLocalizedDescriptionRichTextList(prop);
    }

    private void DrawLocalizedTextList(SerializedProperty listProp, bool multiline, bool syncToDisplayName)
    {
        if (listProp == null)
        {
            EditorGUILayout.HelpBox("找不到本地化字段。", MessageType.Warning);
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
                SerializedProperty displayNameProp = page.SelectedItemSO.FindProperty("displayName");
                if (displayNameProp != null && displayNameProp.stringValue != defaultText)
                    displayNameProp.stringValue = defaultText;
            }
            else
            {
                SerializedProperty descriptionProp = page.SelectedItemSO.FindProperty("description");
                if (descriptionProp != null && descriptionProp.stringValue != defaultText)
                    descriptionProp.stringValue = defaultText;
            }
        }
    }


    private void DrawLocalizedDescriptionRichTextList(SerializedProperty listProp)
    {
        if (listProp == null)
        {
            EditorGUILayout.HelpBox("找不到本地化字段。", MessageType.Warning);
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

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140f));
            bool requestOpenRichText = GUILayout.Button("打开富文本编辑器", GUILayout.Width(140f), GUILayout.Height(24f));
            EditorGUILayout.EndHorizontal();

            string previewText = textProp != null && !string.IsNullOrWhiteSpace(textProp.stringValue) ? textProp.stringValue : "（暂无描述）";
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
                            if (page.SelectedItemSO == null)
                                return;

                            page.SelectedItemSO.Update();
                            SerializedProperty localizedList = page.SelectedItemSO.FindProperty("localizedDescriptions");
                            SerializedProperty entry = FindLocalizedEntry(localizedList, openLang);
                            if (entry != null)
                            {
                                SerializedProperty text = entry.FindPropertyRelative("text");
                                if (text != null)
                                    text.stringValue = updated ?? "";
                            }

                            SerializedProperty defaultEntry = FindLocalizedEntry(localizedList, defaultLanguageCode);
                            SerializedProperty descriptionProp = page.SelectedItemSO.FindProperty("description");
                            if (defaultEntry != null && descriptionProp != null)
                            {
                                SerializedProperty defaultText = defaultEntry.FindPropertyRelative("text");
                                descriptionProp.stringValue = defaultText != null ? (defaultText.stringValue ?? "") : "";
                            }

                            page.SelectedItemSO.ApplyModifiedProperties();
                            EditorUtility.SetDirty(page.SelectedItemDefinition);
                        },
                        "item");
                };
            }
        }

        SerializedProperty defaultEntryProp = FindLocalizedEntry(listProp, defaultLanguageCode);
        if (defaultEntryProp == null)
            return;

        SerializedProperty defaultTextProp = defaultEntryProp.FindPropertyRelative("text");
        SerializedProperty descriptionPropSync = page.SelectedItemSO.FindProperty("description");
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
                if (y + lineHeight > rect.yMax) break;
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

            if (y + lineHeight > rect.yMax) break; // 超出框高，停止绘制

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

    private void DrawUsageRules()
    {
        ItemDefinition item = page.SelectedItemDefinition;
        SerializedProperty usabilityProp = page.SelectedItemSO.FindProperty("usability");
        SerializedProperty cooldownProp = page.SelectedItemSO.FindProperty("cooldown");
        SerializedProperty statusTypeProp = page.SelectedItemSO.FindProperty("statusType");

        bool lockUseRule = item != null && item.majorCategory != ItemMajorCategory.General;
        if (lockUseRule)
        {
            if (usabilityProp != null)
                usabilityProp.enumValueIndex = (int)ItemUsability.NotUsable;
            if (cooldownProp != null)
                cooldownProp.floatValue = 0f;
            if (statusTypeProp != null)
                statusTypeProp.enumValueIndex = (int)ItemStatusType.None;

            EditorGUI.BeginDisabledGroup(true);
            page.DrawRow("可使用性", usabilityProp);
            page.DrawRow("冷却", cooldownProp);
            page.DrawRow("状态类型", statusTypeProp);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.HelpBox("装备和外观仍然具备物品基础信息，但不是药品/消耗道具，因此可使用性被锁定为不可使用。", MessageType.None);
        }
        else
        {
            page.DrawRow("可使用性", usabilityProp);
            page.DrawRow("冷却", cooldownProp);
            page.DrawRow("状态类型", statusTypeProp);
        }

        page.DrawRow("组数量上限", page.SelectedItemSO.FindProperty("maxStackCount"));
        page.DrawRow("是否可丢弃", page.SelectedItemSO.FindProperty("canDiscard"));

        if (item != null && item.IsUsable)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("使用 VFX / SFX", EditorStyles.boldLabel);
            page.DrawRow("使用特效", page.SelectedItemSO.FindProperty("useVFX"));
            page.DrawRow("使用音效", page.SelectedItemSO.FindProperty("useSE"));
            page.DrawRow("使用音效音量倍率", page.SelectedItemSO.FindProperty("useSEVolume"));
            EditorGUILayout.HelpBox(
                "背包窗口和快捷道具栏两个使用入口都会触发，不用分别配置。",
                MessageType.None);
        }
    }

    private void DrawCategoryAndEconomy()
    {
        ItemDefinition item = page.SelectedItemDefinition;

        page.DrawRow("物品大类", page.SelectedItemSO.FindProperty("majorCategory"));

        if (item != null && item.majorCategory == ItemMajorCategory.General)
            DrawFilteredGeneralCategoryRow("一般道具子类", page.SelectedItemSO.FindProperty("category"));
        else if (item != null && item.majorCategory == ItemMajorCategory.Equipment)
            page.DrawReadonlyRow("旧分类 / 子类", "已移入装备信息：装备槽位 / 武器分类");
        else
            DrawFilteredGeneralCategoryRow("一般道具子类", page.SelectedItemSO.FindProperty("category"));

        // 仅 Material 时显示材料子类
        if (item != null && item.category == ItemCategory.Material)
        {
            page.DrawRow("材料子类", page.SelectedItemSO.FindProperty("materialSubCategory"));

            // 弹药是材料子类里的一种，只额外多问一句"口径"——跟热武器
            // ItemEquipmentExtension.ammoCaliber 要对得上，攻击时按口径汇总扣减。
            if (item.materialSubCategory == MaterialSubCategory.Ammunition)
                page.DrawRow("弹药口径", page.SelectedItemSO.FindProperty("ammo.caliber"));
        }

        page.DrawRow("物品等级", page.SelectedItemSO.FindProperty("itemLevel"));
page.DrawRow("产地", page.SelectedItemSO.FindProperty("origin"));
        page.DrawRow("负重 / 石英（旧字段）", page.SelectedItemSO.FindProperty("weightOrQuartz"));

        EditorGUILayout.HelpBox("旧分类 / 子类现在只保留一般道具分类。武器、护甲、饰品已经移入装备信息；发型、InnerSkin 等移入外观信息。", MessageType.None);
    }

    private void DrawFilteredGeneralCategoryRow(string label, SerializedProperty categoryProp)
    {
        if (categoryProp == null)
        {
            EditorGUILayout.HelpBox($"字段 {label} 不存在。", MessageType.Warning);
            return;
        }

        ItemCategory current = (ItemCategory)categoryProp.enumValueIndex;
        if (current == ItemCategory.Weapon || current == ItemCategory.Armor || current == ItemCategory.Accessory)
        {
            current = ItemCategory.Consumable;
            categoryProp.enumValueIndex = (int)current;
        }

        ItemCategory[] values =
        {
            ItemCategory.Consumable,
            ItemCategory.Material,
            ItemCategory.Quest,
            ItemCategory.Currency,
            ItemCategory.Special
        };

        GUIContent[] labels =
        {
            new GUIContent("消耗品"),
            new GUIContent("材料"),
            new GUIContent("任务物品"),
            new GUIContent("凭证 / 货币"),
            new GUIContent("特殊")
        };

        int selected = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == current)
            {
                selected = i;
                break;
            }
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        int newSelected = EditorGUILayout.Popup(selected, labels);
        EditorGUILayout.EndHorizontal();

        categoryProp.enumValueIndex = (int)values[Mathf.Clamp(newSelected, 0, values.Length - 1)];
    }

    private void DrawMajorCategoryExtensionContainer()
    {
        ItemDefinition item = page.SelectedItemDefinition;
        if (item == null)
            return;

        switch (item.majorCategory)
        {
            case ItemMajorCategory.Equipment:
                DrawTypedContainer("装备信息（仅装备类物品生效）", new Color(0.70f, 0.92f, 1.00f, 0.78f), DrawEquipmentExtension);
                break;
            default:
                DrawTypedContainer("一般道具信息（仅一般道具生效）", new Color(0.92f, 0.92f, 0.94f, 0.72f), DrawGeneralItemExtension);
                break;
        }
    }

    private void DrawTypedContainer(string title, Color accent, System.Action drawer)
    {
        EditorGUILayout.BeginVertical("box");

        Rect headerRect = EditorGUILayout.GetControlRect(false, 24f);
        EditorGUI.DrawRect(headerRect, new Color(1f, 1f, 1f, 0.025f));
        EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y + 2f, 3f, headerRect.height - 4f), accent);

        bool oldState = page.Foldouts.TryGetValue(title, out bool value) ? value : true;
        Rect foldoutRect = new Rect(headerRect.x + 8f, headerRect.y, headerRect.width - 8f, headerRect.height);
        bool newState = EditorGUI.Foldout(foldoutRect, oldState, title, true, EditorStyles.foldoutHeader);
        page.Foldouts[title] = newState;

        if (newState)
        {
            GUILayout.Space(4f);
            drawer?.Invoke();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4f);
    }

    private void DrawEquipmentExtension()
    {
        page.DrawRow("装备槽位", page.SelectedItemSO.FindProperty("equipment.slot"));
        page.DrawRow("移动速度倍率", page.SelectedItemSO.FindProperty("equipment.moveSpeedMultiplier"));
        page.DrawRow("LP 消耗倍率", page.SelectedItemSO.FindProperty("equipment.lpCostMultiplier"));
        page.DrawRow("脚步噪声倍率", page.SelectedItemSO.FindProperty("equipment.footstepNoiseMultiplier"));
        page.DrawRow("潜行噪声倍率", page.SelectedItemSO.FindProperty("equipment.sneakNoiseMultiplier"));
        page.DrawRow("奔跑噪声倍率", page.SelectedItemSO.FindProperty("equipment.sprintNoiseMultiplier"));
        page.DrawRow("落地噪声倍率", page.SelectedItemSO.FindProperty("equipment.landingNoiseMultiplier"));
        // 之前这个字段只在 DrawGeneralItemExtension() 里画，装备/武器类物品完全没有
        // 这一行——但实际拾取/放下播放音效的代码(SkyPrisonInventoryInteraction等)
        // 从来都是不分类型统一读 general.soundMaterial，数据字段本身在装备物品上
        // 也存在，只是编辑器没画出来给人填，导致武器类物品的拾取音效永远是默认值。
        page.DrawRow("拾取/放下音效材质", page.SelectedItemSO.FindProperty("general.soundMaterial"));

        ItemDefinition item = page.SelectedItemDefinition;
        if (item != null && item.equipment != null && item.equipment.slot == EquipmentSlotType.Weapon)
        {
            GUILayout.Space(6f);
            DrawTypedSubBox("武器专属信息", () =>
            {
                page.DrawRow("武器分类", page.SelectedItemSO.FindProperty("equipment.weaponCategory"));
                SyncWeaponKeyFromCategoryIfNeeded(item);
                page.DrawRow("武器类型 Key", page.SelectedItemSO.FindProperty("equipment.weaponTypeKey"));

                // 战斗模组直接引用（优先）
                SerializedProperty moduleProp = page.SelectedItemSO.FindProperty("equipment.combatModule");
                if (moduleProp != null)
                {
                    WeaponCombatModule curMod = moduleProp.objectReferenceValue as WeaponCombatModule;
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("战斗模组", GUILayout.Width(140));
                    EditorGUI.BeginChangeCheck();
                    var newMod = (WeaponCombatModule)EditorGUILayout.ObjectField(
                        curMod, typeof(WeaponCombatModule), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        moduleProp.objectReferenceValue = newMod;
                        // 同步 moduleKey 字段
                        SerializedProperty keyProp = page.SelectedItemSO.FindProperty("equipment.weaponModuleKey");
                        if (keyProp != null)
                            keyProp.stringValue = newMod != null ? newMod.moduleKey : "";
                        page.SelectedItemSO.ApplyModifiedProperties();
                    }
                    EditorGUILayout.EndHorizontal();

                    if (curMod != null)
                    {
                        var info = new GUIStyle(EditorStyles.miniLabel)
                            { normal = { textColor = new Color(0.92f, 0.88f, 0.55f) } };
                        EditorGUILayout.LabelField($"  Key: {curMod.moduleKey}  |  {curMod.displayName}", info);
                    }
                }

                page.DrawRow("武器模组 Key", page.SelectedItemSO.FindProperty("equipment.weaponModuleKey"));
                EditorGUILayout.HelpBox("直接拖入战斗模组资产（推荐）。模组 Key 会自动同步，也可手填 Key 走运行时查库。", MessageType.None);

                GUILayout.Space(6f);
                page.DrawRow("武器皮肤名字", page.SelectedItemSO.FindProperty("equipment.weaponSkinName"));
                EditorGUILayout.HelpBox("对应角色自己Spine骨架里的Skin名字（比如\"weapon_sword_heavySpade\"）——武器现在直接画在角色骨架本身里，不再是独立的Spine文件/预制体。装备时直接给角色骨架切皮肤，留空=不切皮肤。", MessageType.None);
                page.DrawRow("武器判定插槽名字", page.SelectedItemSO.FindProperty("equipment.weaponJudgmentSlotName"));
                EditorGUILayout.HelpBox("对应武器皮肤里那个判定形状(Boundingbox附件)插槽的名字——装备时攻击判定从角色徒手切到这个插槽的形状。留空=不切换判定，沿用角色徒手判定。", MessageType.None);

                GUILayout.Space(6f);
                page.DrawRow("武器剪影图（HUD切换条用）", page.SelectedItemSO.FindProperty("equipment.weaponSilhouette"));
                EditorGUILayout.HelpBox("战斗HUD右下角武器切换条显示用，白色剪影+透明背景，素材放Assets/_Project/Icon/Equipment/WeaponSilhouette文件夹。", MessageType.None);

                GUILayout.Space(6f);
                page.DrawRow("消耗弹药", page.SelectedItemSO.FindProperty("equipment.usesAmmo"));
                if (page.SelectedItemDefinition.equipment.usesAmmo)
                {
                    page.DrawRow("弹药口径", page.SelectedItemSO.FindProperty("equipment.ammoCaliber"));
                    page.DrawRow("单次消耗数量", page.SelectedItemSO.FindProperty("equipment.ammoPerShot"));
                    page.DrawRow("弹匣容量", page.SelectedItemSO.FindProperty("equipment.magazineSize"));
                    page.DrawRow("换弹耗时(秒)", page.SelectedItemSO.FindProperty("equipment.reloadDurationSeconds"));
                }
                EditorGUILayout.HelpBox("勾上后，轻/重攻击消耗的是弹匣里的弹药（不是直接扣背包），弹匣打空后按换弹键（默认R）从背包按口径补充，补充量=min(弹匣空位,背包备用弹药)。近战武器（剑/链锯）不用勾，HUD固定显示无穷符号。", MessageType.None);
            });
        }

        EditorGUILayout.HelpBox("饰品物品只使用装备槽位 Accessory；饰品槽 1 / 饰品槽 2 属于单位装备栏位置，不属于物品类型。", MessageType.None);

        GUILayout.Space(6f);
        DrawTypedSubBox("属性加成（装备后对角色核心属性的加成）", DrawEquipmentStatBonuses);

        GUILayout.Space(6f);
        DrawTypedSubBox("强化熔炉（偏好标签）", () =>
            DrawTagWeightList(page.SelectedItemSO.FindProperty("equipment.alchemyPreferredTags")));

        GUILayout.Space(6f);
        DrawTypedSubBox("染色（默认配色方案）", DrawDyeColorSchemes);
    }

    // 装备属性加成——跟 UnitDefinition 那边的"标准属性/自定义属性"是同一套
    // BattleParameterDatabase.coreAttributes 字典、同一个 UnitParameterValue 类型，
    // 只是这里不分标准/自定义两组，直接列出全部核心属性（大多数装备只会填其中一两项，
    // 分组意义不大，不用照搬 Unit 那边的折叠分组）。
    private void DrawEquipmentStatBonuses()
    {
        BattleParameterDatabase database = FindBattleParameterDatabaseForItem();
        if (database == null)
        {
            EditorGUILayout.HelpBox("未找到 BattleParameterDatabase，暂时无法填写属性加成。", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox(
            $"当前属性加成填写基于参数库：{(string.IsNullOrWhiteSpace(database.displayName) ? database.name : database.displayName)}",
            MessageType.None);

        SerializedProperty statBonusesProp = page.SelectedItemSO.FindProperty("equipment.statBonuses");
        if (statBonusesProp == null)
        {
            EditorGUILayout.HelpBox("当前 ItemEquipmentExtension 里还没有 statBonuses 字段。", MessageType.Warning);
            return;
        }

        EnsureStatBonusEntries(statBonusesProp, database);

        EditorGUILayout.BeginVertical("box");
        foreach (var def in database.coreAttributes)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.key))
                continue;

            SerializedProperty entryProp = FindStatBonusEntry(statBonusesProp, def.key);
            if (entryProp == null)
                continue;

            SerializedProperty valueProp = entryProp.FindPropertyRelative("value");
            string label = string.IsNullOrWhiteSpace(def.displayName) ? def.key : def.displayName;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180f));
            if (valueProp != null)
            {
                valueProp.floatValue = EditorGUILayout.FloatField(valueProp.floatValue);
                if (def.valueType == BattleValueType.Percentage)
                    GUILayout.Label("%", GUILayout.Width(18f));
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
    }

    private void EnsureStatBonusEntries(SerializedProperty statBonusesProp, BattleParameterDatabase database)
    {
        if (database == null || database.coreAttributes == null)
            return;

        var existing = new HashSet<string>();
        for (int i = 0; i < statBonusesProp.arraySize; i++)
        {
            SerializedProperty item = statBonusesProp.GetArrayElementAtIndex(i);
            SerializedProperty keyProp = item.FindPropertyRelative("parameterKey");
            if (keyProp != null && !string.IsNullOrWhiteSpace(keyProp.stringValue))
                existing.Add(keyProp.stringValue);
        }

        foreach (var def in database.coreAttributes)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.key) || existing.Contains(def.key))
                continue;

            int index = statBonusesProp.arraySize;
            statBonusesProp.InsertArrayElementAtIndex(index);
            SerializedProperty newItem = statBonusesProp.GetArrayElementAtIndex(index);
            SerializedProperty keyProp = newItem.FindPropertyRelative("parameterKey");
            SerializedProperty valueProp = newItem.FindPropertyRelative("value");
            if (keyProp != null) keyProp.stringValue = def.key;
            if (valueProp != null) valueProp.floatValue = 0f;
            existing.Add(def.key);
        }
    }

    private SerializedProperty FindStatBonusEntry(SerializedProperty statBonusesProp, string parameterKey)
    {
        for (int i = 0; i < statBonusesProp.arraySize; i++)
        {
            SerializedProperty item = statBonusesProp.GetArrayElementAtIndex(i);
            SerializedProperty keyProp = item.FindPropertyRelative("parameterKey");
            if (keyProp != null && keyProp.stringValue == parameterKey)
                return item;
        }
        return null;
    }

    private BattleParameterDatabase FindBattleParameterDatabaseForItem()
    {
        string[] guids = AssetDatabase.FindAssets("t:BattleParameterDatabase");
        if (guids == null || guids.Length == 0)
            return null;

        List<BattleParameterDatabase> list = new List<BattleParameterDatabase>();
        foreach (var guid in guids)
        {
            var db = AssetDatabase.LoadAssetAtPath<BattleParameterDatabase>(AssetDatabase.GUIDToAssetPath(guid));
            if (db != null) list.Add(db);
        }
        if (list.Count == 0)
            return null;

        BattleParameterDatabase main = list.FirstOrDefault(x => x != null && x.databaseId == "battle_parameters_main");
        return main ?? list[0];
    }

    private void DrawDyeColorSchemes()
    {
        SerializedProperty schemesProp = page.SelectedItemSO.FindProperty("equipment.defaultDyeColorSchemes");
        if (schemesProp == null)
            return;

        EditorGUILayout.HelpBox("每套方案设定3个染色区域（对应武器/装备Spine上 {xxx_dye_01}/02/03 标签的颜色）。填1套=固定用这套；填多套=掉落时随机抽1套。留空=不染色（中性白）。", MessageType.None);

        for (int i = 0; i < schemesProp.arraySize; i++)
        {
            SerializedProperty schemeProp  = schemesProp.GetArrayElementAtIndex(i);
            SerializedProperty colorsProp  = schemeProp.FindPropertyRelative("colors");

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"方案 {i + 1}", GUILayout.Width(60f));

            if (colorsProp != null)
            {
                if (colorsProp.arraySize != 3)
                    colorsProp.arraySize = 3;

                for (int c = 0; c < 3; c++)
                {
                    SerializedProperty colorProp = colorsProp.GetArrayElementAtIndex(c);
                    colorProp.colorValue = EditorGUILayout.ColorField(GUIContent.none, colorProp.colorValue, false, false, false, GUILayout.Width(60f));
                }
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("删除", GUILayout.Width(50f)))
            {
                schemesProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        GUILayout.Space(4f);
        if (GUILayout.Button("+ 添加配色方案"))
        {
            int idx = schemesProp.arraySize;
            schemesProp.arraySize++;
            SerializedProperty newScheme = schemesProp.GetArrayElementAtIndex(idx);
            SerializedProperty newColors = newScheme.FindPropertyRelative("colors");
            if (newColors != null)
            {
                newColors.arraySize = 3;
                for (int c = 0; c < 3; c++)
                    newColors.GetArrayElementAtIndex(c).colorValue = Color.white;
            }
        }
    }


    private void SyncWeaponKeyFromCategoryIfNeeded(ItemDefinition item)
    {
        if (item == null || item.equipment == null)
            return;

        string suggestedKey = GetSuggestedWeaponKey(item.equipment.weaponCategory);
        if (string.IsNullOrEmpty(suggestedKey))
            return;

        SerializedProperty typeKeyProp = page.SelectedItemSO.FindProperty("equipment.weaponTypeKey");
        SerializedProperty moduleKeyProp = page.SelectedItemSO.FindProperty("equipment.weaponModuleKey");

        if (typeKeyProp != null && string.IsNullOrWhiteSpace(typeKeyProp.stringValue))
            typeKeyProp.stringValue = suggestedKey;

        if (moduleKeyProp != null && string.IsNullOrWhiteSpace(moduleKeyProp.stringValue))
            moduleKeyProp.stringValue = suggestedKey;
    }

    private string GetSuggestedWeaponKey(WeaponCategoryType category)
    {
        switch (category)
        {
            case WeaponCategoryType.Sword:
                return "sword";
            case WeaponCategoryType.Chainsaw:
                return "chainsaw";
            case WeaponCategoryType.DualGun:
                return "dual_gun";
            default:
                return "";
        }
    }

    private void DrawAppearanceExtension()
    {
        page.DrawRow("外观槽位", page.SelectedItemSO.FindProperty("appearance.slot"));
        page.DrawRow("Appearance Key", page.SelectedItemSO.FindProperty("appearance.appearanceKey"));
        page.DrawRow("Spine Skin Key", page.SelectedItemSO.FindProperty("appearance.spineSkinKey"));
        page.DrawRow("覆盖装备外观", page.SelectedItemSO.FindProperty("appearance.overrideEquipmentAppearance"));
        EditorGUILayout.HelpBox("外观是物品，但不是战斗装备。发型、InnerSkin 等只影响外观层，不参与装备槽位。", MessageType.None);
    }

    private void DrawGeneralItemExtension()
    {
        page.DrawRow("一般道具类型", page.SelectedItemSO.FindProperty("general.type"));
        page.DrawRow("使用效果 Key", page.SelectedItemSO.FindProperty("general.useEffectKey"));
        page.DrawRow("可放入快捷栏", page.SelectedItemSO.FindProperty("general.canPutInQuickSlot"));
        page.DrawRow("使用后消耗", page.SelectedItemSO.FindProperty("general.consumeOnUse"));
        page.DrawRow("复活道具", page.SelectedItemSO.FindProperty("general.isReviveItem"));
        SerializedProperty isReviveProp = page.SelectedItemSO.FindProperty("general.isReviveItem");
        if (isReviveProp != null && isReviveProp.boolValue)
            page.DrawRow("复活无敌时长（秒）", page.SelectedItemSO.FindProperty("general.reviveProtectionDuration"));
        page.DrawRow("拾取/放下音效材质", page.SelectedItemSO.FindProperty("general.soundMaterial"));
        EditorGUILayout.HelpBox("一般道具只显示道具层信息，不显示装备信息或外观信息。", MessageType.None);

        ItemDefinition item = page.SelectedItemDefinition;
        if (item != null && item.category == ItemCategory.Material)
        {
            GUILayout.Space(6f);
            DrawTypedSubBox("强化熔炉相性（仅材料物品生效）", () =>
            {
                page.DrawRow("基础效力", page.SelectedItemSO.FindProperty("materialAlchemy.basePotency"));
                EditorGUILayout.HelpBox("不考虑相性时，这个素材本身对成功率的基础贡献。", MessageType.None);
                GUILayout.Space(4f);
                DrawTagWeightList(page.SelectedItemSO.FindProperty("materialAlchemy.affinityTags"));
            });
        }
    }

    // 素材的"携带标签"和装备的"偏好标签"共用同一个 MaterialAffinityTagWeight 类型，
    // 编辑器画法也共用这一份——避免两处各写一套列表增删逻辑。
    private void DrawTagWeightList(SerializedProperty listProp)
    {
        if (listProp == null)
        {
            EditorGUILayout.HelpBox("字段不存在。", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox("标签key要跟 MaterialAffinityDatabase 里注册的一致，权重0~1表示这个标签在这件物品身上有多强。", MessageType.None);

        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty entry = listProp.GetArrayElementAtIndex(i);
            SerializedProperty keyProp = entry.FindPropertyRelative("tagKey");
            SerializedProperty weightProp = entry.FindPropertyRelative("weight");

            EditorGUILayout.BeginHorizontal();
            if (keyProp != null)
                keyProp.stringValue = EditorGUILayout.TextField(keyProp.stringValue, GUILayout.Width(140f));
            if (weightProp != null)
                weightProp.floatValue = EditorGUILayout.Slider(weightProp.floatValue, 0f, 1f);
            if (GUILayout.Button("删除", GUILayout.Width(50f)))
            {
                listProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("+ 添加标签"))
        {
            int index = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(index);
            SerializedProperty newEntry = listProp.GetArrayElementAtIndex(index);
            SerializedProperty newKey = newEntry.FindPropertyRelative("tagKey");
            SerializedProperty newWeight = newEntry.FindPropertyRelative("weight");
            if (newKey != null) newKey.stringValue = "";
            if (newWeight != null) newWeight.floatValue = 1f;
        }
    }

    private void DrawTypedSubBox(string title, System.Action drawer)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        GUILayout.Space(2f);
        drawer?.Invoke();
        EditorGUILayout.EndVertical();
    }

    private void DrawCurrencyAndPrices()
    {
        EnsureDefaultCurrencyExists();

        EditorGUILayout.LabelField("货币", EditorStyles.miniBoldLabel);
        GUILayout.Space(2f);
        DrawCurrencyDefinitionContainer();

        GUILayout.Space(6f);
        DrawSelectedCurrencyMetaInfo();

        GUILayout.Space(12f);

        EditorGUILayout.LabelField("价格", EditorStyles.miniBoldLabel);
        GUILayout.Space(2f);
        DrawPriceInputs();
    }

    private void DrawCurrencyDefinitionContainer()
    {
        List<CurrencyDefinition> currencies = GetAllCurrenciesOrdered();
        EnsureSelectedCurrencyIndex(currencies);

        Rect outerRect = GUILayoutUtility.GetRect(0f, 10000f, 168f, 168f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(outerRect, new Color(0.13f, 0.13f, 0.14f, 1f));
        page.DrawThinBorder(outerRect, new Color(1f, 1f, 1f, 0.06f));

        Rect topLine = new Rect(outerRect.x + 6f, outerRect.y + 28f, outerRect.width - 12f, 1f);
        Rect bottomLine = new Rect(outerRect.x + 6f, outerRect.yMax - 8f, outerRect.width - 12f, 1f);
        EditorGUI.DrawRect(topLine, new Color(1f, 1f, 1f, 0.08f));
        EditorGUI.DrawRect(bottomLine, new Color(1f, 1f, 1f, 0.08f));

        Rect plusRect = new Rect(outerRect.xMax - 56f, outerRect.y + 4f, 24f, 20f);
        Rect minusRect = new Rect(outerRect.xMax - 28f, outerRect.y + 4f, 24f, 20f);

        if (DrawMiniHeaderButton(plusRect, "+"))
            CreateNewCurrencyDefinition();

        using (new EditorGUI.DisabledScope(!CanDeleteSelectedCurrency(currencies)))
        {
            if (DrawMiniHeaderButton(minusRect, "-"))
                DeleteSelectedCurrency(currencies);
        }

        float rowY = outerRect.y + 34f;
        float rowHeight = 22f;

        for (int i = 0; i < currencies.Count; i++)
        {
            CurrencyDefinition currency = currencies[i];
            Rect rowRect = new Rect(outerRect.x + 6f, rowY, outerRect.width - 12f, rowHeight);

            bool selected = i == selectedCurrencyIndex;
            bool hover = rowRect.Contains(Event.current.mousePosition);

            if (selected)
            {
                EditorGUI.DrawRect(rowRect, new Color(0.72f, 0.56f, 0.12f, 0.18f));
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), page.AccentYellow);
            }
            else if (hover)
            {
                EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.04f));
            }

            Rect nameRect = new Rect(rowRect.x + 8f, rowRect.y + 2f, rowRect.width - 112f, rowRect.height - 4f);
            Rect unitRect = new Rect(rowRect.xMax - 52f, rowRect.y + 2f, 44f, rowRect.height - 4f);

            HandleCurrencyRowInput(i, currency, rowRect, nameRect, unitRect);

            bool editingNameThisRow = editingCurrencyName && editingCurrencyRowIndex == i;
            bool editingUnitThisRow = editingCurrencyUnit && editingCurrencyRowIndex == i;

            if (editingNameThisRow)
            {
                GUI.SetNextControlName("CurrencyNameInlineEditor");
                currencyNameEditBuffer = EditorGUI.TextField(nameRect, currencyNameEditBuffer);

                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                {
                    CommitCurrencyNameEdit(currency);
                    Event.current.Use();
                }
            }
            else
            {
                GUIStyle nameStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = selected ? Color.white : new Color(0.90f, 0.90f, 0.92f, 1f) }
                };
                EditorGUI.LabelField(nameRect, string.IsNullOrWhiteSpace(currency.displayName) ? currency.name : currency.displayName, nameStyle);
            }

            if (editingUnitThisRow)
            {
                GUI.SetNextControlName("CurrencyUnitInlineEditor");
                currencyUnitEditBuffer = EditorGUI.TextField(unitRect, currencyUnitEditBuffer);

                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                {
                    CommitCurrencyUnitEdit(currency);
                    Event.current.Use();
                }
            }
            else
            {
                GUIStyle unitStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 10,
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = selected ? Color.white : new Color(0.82f, 0.78f, 0.64f, 1f) }
                };
                EditorGUI.LabelField(unitRect, string.IsNullOrWhiteSpace(currency.unitSymbol) ? "-" : currency.unitSymbol, unitStyle);
            }

            rowY += rowHeight + 2f;
        }

        if (editingCurrencyName)
            EditorGUI.FocusTextInControl("CurrencyNameInlineEditor");
        else if (editingCurrencyUnit)
            EditorGUI.FocusTextInControl("CurrencyUnitInlineEditor");
    }

    private void DrawSelectedCurrencyMetaInfo()
    {
        List<CurrencyDefinition> currencies = GetAllCurrenciesOrdered();
        CurrencyDefinition selectedCurrency = GetSelectedCurrency(currencies);
        if (selectedCurrency == null)
            return;

        // 图标编辑（与货币身份同处管理；不放字典表）
        SerializedObject currencySO = new SerializedObject(selectedCurrency);
        currencySO.Update();
        SerializedProperty iconProp = currencySO.FindProperty("icon");
        if (iconProp != null)
        {
            EditorGUILayout.BeginHorizontal();

            Sprite spr = iconProp.objectReferenceValue as Sprite;
            Rect preview = GUILayoutUtility.GetRect(48f, 48f, GUILayout.Width(48f), GUILayout.Height(48f));
            if (spr != null && spr.texture != null)
                GUI.DrawTexture(preview, spr.texture, ScaleMode.ScaleToFit, true);
            else
                EditorGUI.DrawRect(preview, new Color(1f, 1f, 1f, 0.06f));

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("图标", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(iconProp, GUIContent.none, true);
            using (new EditorGUI.DisabledScope(iconProp.objectReferenceValue == null))
            {
                if (GUILayout.Button("清空图标", GUILayout.Height(20f)))
                    iconProp.objectReferenceValue = null;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
            currencySO.ApplyModifiedProperties();
        }

        EditorGUILayout.Space(4f);

        Color oldColor = GUI.color;
        GUI.color = new Color(0.70f, 0.70f, 0.70f, 1f);

        GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.68f, 0.68f, 0.70f, 1f) }
        };

        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField($"ID：{selectedCurrency.currencyId}", labelStyle);
        EditorGUILayout.LabelField($"名字 Key：{selectedCurrency.nameKey}", labelStyle);
        EditorGUILayout.LabelField($"描述 Key：{selectedCurrency.descKey}", labelStyle);
        EditorGUILayout.EndVertical();

        GUI.color = oldColor;
    }

    private CurrencyDefinition GetSelectedCurrency(List<CurrencyDefinition> currencies)
    {
        if (selectedCurrencyIndex < 0 || selectedCurrencyIndex >= currencies.Count)
            return null;

        return currencies[selectedCurrencyIndex];
    }

    private void HandleCurrencyRowInput(int rowIndex, CurrencyDefinition currency, Rect rowRect, Rect nameRect, Rect unitRect)
    {
        Event e = Event.current;
        if (e.type != EventType.MouseDown || !rowRect.Contains(e.mousePosition))
            return;

        selectedCurrencyIndex = rowIndex;

        if (e.clickCount >= 2)
        {
            if (nameRect.Contains(e.mousePosition))
            {
                editingCurrencyName = true;
                editingCurrencyUnit = false;
                editingCurrencyRowIndex = rowIndex;
                currencyNameEditBuffer = currency.displayName ?? "";
                e.Use();
                return;
            }

            if (unitRect.Contains(e.mousePosition))
            {
                editingCurrencyUnit = true;
                editingCurrencyName = false;
                editingCurrencyRowIndex = rowIndex;
                currencyUnitEditBuffer = currency.unitSymbol ?? "";
                e.Use();
                return;
            }
        }
        else
        {
            if (editingCurrencyName && editingCurrencyRowIndex != rowIndex)
                TryCommitCurrentCurrencyEdit();
            if (editingCurrencyUnit && editingCurrencyRowIndex != rowIndex)
                TryCommitCurrentCurrencyEdit();
        }
    }

    private void TryCommitCurrentCurrencyEdit()
    {
        List<CurrencyDefinition> currencies = GetAllCurrenciesOrdered();
        if (editingCurrencyRowIndex < 0 || editingCurrencyRowIndex >= currencies.Count)
        {
            editingCurrencyName = false;
            editingCurrencyUnit = false;
            editingCurrencyRowIndex = -1;
            return;
        }

        CurrencyDefinition currency = currencies[editingCurrencyRowIndex];
        if (editingCurrencyName)
            CommitCurrencyNameEdit(currency);
        else if (editingCurrencyUnit)
            CommitCurrencyUnitEdit(currency);
    }

    private void CommitCurrencyNameEdit(CurrencyDefinition currency)
    {
        string value = (currencyNameEditBuffer ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            value = GenerateNextCurrencyDisplayName();

        if (currency.displayName != value)
        {
            currency.displayName = value;
            EditorUtility.SetDirty(currency);
            AssetDatabase.SaveAssets();
        }

        editingCurrencyName = false;
        editingCurrencyRowIndex = -1;
        currencyNameEditBuffer = "";
    }

    private void CommitCurrencyUnitEdit(CurrencyDefinition currency)
    {
        string value = (currencyUnitEditBuffer ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(value))
            value = GenerateNextCurrencyUnitSymbol();

        if (currency.unitSymbol != value)
        {
            currency.unitSymbol = value;
            EditorUtility.SetDirty(currency);
            AssetDatabase.SaveAssets();
        }

        editingCurrencyUnit = false;
        editingCurrencyRowIndex = -1;
        currencyUnitEditBuffer = "";
    }

    private void EnsureSelectedCurrencyIndex(List<CurrencyDefinition> currencies)
    {
        if (currencies.Count == 0)
        {
            selectedCurrencyIndex = -1;
            return;
        }

        if (selectedCurrencyIndex < 0 || selectedCurrencyIndex >= currencies.Count)
            selectedCurrencyIndex = 0;
    }

    private bool DrawMiniHeaderButton(Rect rect, string text)
    {
        Event e = Event.current;
        bool hover = rect.Contains(e.mousePosition);
        bool clicked = e.type == EventType.MouseDown && e.button == 0 && hover;

        EditorGUI.DrawRect(rect, hover ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.04f));
        page.DrawThinBorder(rect, new Color(1f, 1f, 1f, hover ? 0.12f : 0.05f));

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.92f, 0.92f, 0.94f, 1f) }
        };
        GUI.Label(rect, text, style);

        if (clicked)
        {
            e.Use();
            return true;
        }

        return false;
    }

    private void DrawPriceInputs()
    {
        List<CurrencyDefinition> currencies = GetAllCurrenciesOrdered();
        if (currencies.Count == 0)
        {
            EditorGUILayout.HelpBox("当前没有可用货币定义。", MessageType.Warning);
            return;
        }

        SerializedProperty pricesProp = page.SelectedItemSO.FindProperty("currencyPrices");
        if (pricesProp == null)
        {
            EditorGUILayout.HelpBox("找不到 currencyPrices 字段。", MessageType.Warning);
            return;
        }

        EnsurePriceEntriesForCurrencies(pricesProp, currencies);

        EditorGUILayout.BeginVertical("box");

        for (int i = 0; i < currencies.Count; i++)
        {
            CurrencyDefinition currency = currencies[i];
            SerializedProperty priceEntry = FindPriceEntry(pricesProp, currency.currencyId);
            if (priceEntry == null)
                continue;

            SerializedProperty priceProp = priceEntry.FindPropertyRelative("price");

            string label = $"{currency.displayName}（{currency.unitSymbol}）";
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(160f));
            priceProp.intValue = EditorGUILayout.IntField(priceProp.intValue);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private void EnsureDefaultCurrencyExists()
    {
        CurrencyDefinition currency = FindDefaultCurrency();
        if (currency != null)
            return;

        SkyPrisonCurrencySeeder.EnsureDefaultCurrency();
    }

    private CurrencyDefinition FindDefaultCurrency()
    {
        return GetAllCurrenciesOrdered().FirstOrDefault(x => x != null && x.currencyId == "token");
    }

    private List<CurrencyDefinition> GetAllCurrenciesOrdered()
    {
        string[] guids = AssetDatabase.FindAssets("t:CurrencyDefinition");
        List<CurrencyDefinition> currencies = new List<CurrencyDefinition>();

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            CurrencyDefinition currency = AssetDatabase.LoadAssetAtPath<CurrencyDefinition>(path);
            if (currency != null)
                currencies.Add(currency);
        }

        return currencies
            .OrderBy(x => x.isStandard ? 0 : 1)
            .ThenBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
            .ToList();
    }

    private void CreateNewCurrencyDefinition()
    {
        const string folder = "Assets/_Project/Data/Definitions/Custom/Currencies";
        page.EnsureFolderExists(folder);

        CurrencyDefinition asset = ScriptableObject.CreateInstance<CurrencyDefinition>();
        asset.isStandard = false;
        asset.displayName = GenerateNextCurrencyDisplayName();
        asset.unitSymbol = GenerateNextCurrencyUnitSymbol();

        string safeId = GenerateNextSequentialCurrencyId();
        asset.currencyId = safeId;
        asset.nameKey = $"currency_{safeId}_name";
        asset.descKey = $"currency_{safeId}_desc";

        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/CD_NewCurrency.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        List<CurrencyDefinition> currencies = GetAllCurrenciesOrdered();
        selectedCurrencyIndex = currencies.FindIndex(x => x == asset);
        if (selectedCurrencyIndex < 0)
            selectedCurrencyIndex = currencies.Count - 1;

        editingCurrencyName = true;
        editingCurrencyUnit = false;
        editingCurrencyRowIndex = selectedCurrencyIndex;
        currencyNameEditBuffer = asset.displayName;
    }

    private bool CanDeleteSelectedCurrency(List<CurrencyDefinition> currencies)
    {
        if (selectedCurrencyIndex < 0 || selectedCurrencyIndex >= currencies.Count)
            return false;

        CurrencyDefinition selected = currencies[selectedCurrencyIndex];
        if (selected == null)
            return false;

        if (selected.isStandard)
            return false;

        return currencies.Count > 1;
    }

    private void DeleteSelectedCurrency(List<CurrencyDefinition> currencies)
    {
        if (!CanDeleteSelectedCurrency(currencies))
            return;

        CurrencyDefinition selected = currencies[selectedCurrencyIndex];
        string path = AssetDatabase.GetAssetPath(selected);

        bool ok = EditorUtility.DisplayDialog(
            "删除货币定义",
            $"确定删除货币：{selected.displayName}？",
            "删除",
            "取消"
        );

        if (!ok)
            return;

        RemoveCurrencyPriceById(selected.currencyId);

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        selectedCurrencyIndex = Mathf.Clamp(selectedCurrencyIndex - 1, 0, GetAllCurrenciesOrdered().Count - 1);
        editingCurrencyName = false;
        editingCurrencyUnit = false;
        editingCurrencyRowIndex = -1;
    }

    private void RemoveCurrencyPriceById(string currencyId)
    {
        SerializedProperty pricesProp = page.SelectedItemSO.FindProperty("currencyPrices");
        if (pricesProp == null)
            return;

        for (int i = pricesProp.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty entry = pricesProp.GetArrayElementAtIndex(i);
            SerializedProperty idProp = entry.FindPropertyRelative("currencyId");
            if (idProp != null && idProp.stringValue == currencyId)
                pricesProp.DeleteArrayElementAtIndex(i);
        }
    }

    private string GenerateNextSequentialCurrencyId()
    {
        List<CurrencyDefinition> currencies = GetAllCurrenciesOrdered();
        int maxIndex = 0;

        for (int i = 0; i < currencies.Count; i++)
        {
            CurrencyDefinition c = currencies[i];
            if (c == null || string.IsNullOrWhiteSpace(c.currencyId))
                continue;

            if (c.currencyId.StartsWith("currency_"))
            {
                string suffix = c.currencyId.Substring("currency_".Length);
                if (int.TryParse(suffix, out int number))
                    maxIndex = Mathf.Max(maxIndex, number);
            }
        }

        return $"currency_{(maxIndex + 1):000}";
    }

    private string GenerateNextCurrencyDisplayName()
    {
        List<CurrencyDefinition> currencies = GetAllCurrenciesOrdered();
        int maxIndex = 0;

        for (int i = 0; i < currencies.Count; i++)
        {
            CurrencyDefinition c = currencies[i];
            if (c == null || string.IsNullOrWhiteSpace(c.displayName))
                continue;

            const string prefix = "新添加货币";
            if (c.displayName.StartsWith(prefix))
            {
                string suffix = c.displayName.Substring(prefix.Length);
                if (int.TryParse(suffix, out int number))
                    maxIndex = Mathf.Max(maxIndex, number);
            }
        }

        return $"新添加货币{(maxIndex + 1):00}";
    }

    private string GenerateNextCurrencyUnitSymbol()
    {
        List<CurrencyDefinition> currencies = GetAllCurrenciesOrdered();
        int maxIndex = 0;

        for (int i = 0; i < currencies.Count; i++)
        {
            CurrencyDefinition c = currencies[i];
            if (c == null || string.IsNullOrWhiteSpace(c.unitSymbol))
                continue;

            if (c.unitSymbol.StartsWith("U"))
            {
                string suffix = c.unitSymbol.Substring(1);
                if (int.TryParse(suffix, out int number))
                    maxIndex = Mathf.Max(maxIndex, number);
            }
        }

        return $"U{(maxIndex + 1):00}";
    }

    private void EnsurePriceEntriesForCurrencies(SerializedProperty pricesProp, List<CurrencyDefinition> currencies)
    {
        HashSet<string> existing = new HashSet<string>();

        for (int i = 0; i < pricesProp.arraySize; i++)
        {
            SerializedProperty entry = pricesProp.GetArrayElementAtIndex(i);
            SerializedProperty idProp = entry.FindPropertyRelative("currencyId");
            if (idProp != null && !string.IsNullOrWhiteSpace(idProp.stringValue))
                existing.Add(idProp.stringValue);
        }

        for (int i = 0; i < currencies.Count; i++)
        {
            CurrencyDefinition currency = currencies[i];
            if (currency == null || string.IsNullOrWhiteSpace(currency.currencyId))
                continue;

            if (existing.Contains(currency.currencyId))
                continue;

            int index = pricesProp.arraySize;
            pricesProp.InsertArrayElementAtIndex(index);

            SerializedProperty newEntry = pricesProp.GetArrayElementAtIndex(index);
            SerializedProperty idProp = newEntry.FindPropertyRelative("currencyId");
            SerializedProperty priceProp = newEntry.FindPropertyRelative("price");

            if (idProp != null)
                idProp.stringValue = currency.currencyId;

            if (priceProp != null)
                priceProp.intValue = 0;

            existing.Add(currency.currencyId);
        }

        for (int i = pricesProp.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty entry = pricesProp.GetArrayElementAtIndex(i);
            SerializedProperty idProp = entry.FindPropertyRelative("currencyId");
            if (idProp == null)
                continue;

            bool stillExists = currencies.Any(x => x != null && x.currencyId == idProp.stringValue);
            if (!stillExists)
                pricesProp.DeleteArrayElementAtIndex(i);
        }
    }

    private SerializedProperty FindPriceEntry(SerializedProperty pricesProp, string currencyId)
    {
        for (int i = 0; i < pricesProp.arraySize; i++)
        {
            SerializedProperty entry = pricesProp.GetArrayElementAtIndex(i);
            SerializedProperty idProp = entry.FindPropertyRelative("currencyId");
            if (idProp != null && idProp.stringValue == currencyId)
                return entry;
        }

        return null;
    }

    // ── 使用效果面板 ─────────────────────────────────────────────────────────

    private static readonly string[] _effectTypeNames = new[]
    {
        "资源-立即", "资源-持续（DoT/HoT）", "施加状态", "移除状态", "执行触发器", "掉落率加成"
    };

    private static readonly string[] _resourceTargetNames = new[] { "HP", "LP", "货币" };
    private static readonly string[] _valueModeNames      = new[] { "固定值", "百分比（%最大值）" };

    private void DrawEffects()
    {
        SerializedObject so = page.SelectedItemSO;
        SerializedProperty effectsProp = so.FindProperty("effects");
        if (effectsProp == null) return;

        // 不在此处调 so.Update()——由 page 统一在 Draw() 前后管理，避免覆盖同帧其它字段的未提交改动。

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField($"效果列表（{effectsProp.arraySize} 条）", EditorStyles.miniBoldLabel);
        GUILayout.Space(2f);

        for (int i = 0; i < effectsProp.arraySize; i++)
        {
            SerializedProperty ep = effectsProp.GetArrayElementAtIndex(i);
            DrawEffectEntry(ep, i, effectsProp);
            GUILayout.Space(4f);
        }

        if (GUILayout.Button("＋  添加效果", GUILayout.Height(22f)))
        {
            effectsProp.arraySize++;
            // 新条目默认 ResourceImmediate / HP / 固定值 / 0
            SerializedProperty newEp = effectsProp.GetArrayElementAtIndex(effectsProp.arraySize - 1);
            newEp.FindPropertyRelative("effectType").enumValueIndex      = 0;
            newEp.FindPropertyRelative("resourceTarget").enumValueIndex  = 0;
            newEp.FindPropertyRelative("valueMode").enumValueIndex       = 0;
            newEp.FindPropertyRelative("value").floatValue               = 0f;
            newEp.FindPropertyRelative("statusStacks").intValue          = 1;
            newEp.FindPropertyRelative("statusDurationOverride").floatValue = -1f;
            newEp.FindPropertyRelative("dotInterval").floatValue         = 1f;
            newEp.FindPropertyRelative("dotDuration").floatValue         = 5f;
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawEffectEntry(SerializedProperty ep, int index, SerializedProperty listProp)
    {
        SerializedProperty typeProp = ep.FindPropertyRelative("effectType");
        int typeIdx = typeProp.enumValueIndex;

        EditorGUILayout.BeginVertical("helpbox");

        // ── 标题行（类型选择 + 删除） ──
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"效果 {index}", EditorStyles.boldLabel, GUILayout.Width(50f));
        int newTypeIdx = EditorGUILayout.Popup(typeIdx, _effectTypeNames);
        if (newTypeIdx != typeIdx) typeProp.enumValueIndex = newTypeIdx;
        if (GUILayout.Button("✕", GUILayout.Width(24f)))
        {
            listProp.DeleteArrayElementAtIndex(index);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return;
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(2f);

        // ── 类型特有字段 ──
        switch ((ItemEffectType)newTypeIdx)
        {
            case ItemEffectType.ResourceImmediate:
                DrawResourceFields(ep, showDot: false);
                break;

            case ItemEffectType.ResourceOverTime:
                DrawResourceFields(ep, showDot: true);
                break;

            case ItemEffectType.ApplyStatus:
                DrawPropertyRow("状态",    ep.FindPropertyRelative("statusToApply"));
                DrawPropertyRow("层数",    ep.FindPropertyRelative("statusStacks"));
                DrawPropertyRow("持续时间覆盖（-1=默认）", ep.FindPropertyRelative("statusDurationOverride"));
                break;

            case ItemEffectType.RemoveStatus:
                DrawPropertyRow("指定状态（空=批量）", ep.FindPropertyRelative("statusToRemove"));
                DrawPropertyRow("移除所有负面状态",    ep.FindPropertyRelative("removeAllDebuffs"));
                DrawPropertyRow("移除所有正面状态",    ep.FindPropertyRelative("removeAllBuffs"));
                break;

            case ItemEffectType.ExecuteTrigger:
                DrawPropertyRow("触发器包", ep.FindPropertyRelative("triggerPackage"));
                break;

            case ItemEffectType.DropRateBoost:
                DrawPropertyRow("掉落率倍率", ep.FindPropertyRelative("dropRateMultiplier"));
                DrawPropertyRow("持续时间（秒）", ep.FindPropertyRelative("dropRateBoostDuration"));
                break;
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawResourceFields(SerializedProperty ep, bool showDot)
    {
        SerializedProperty resProp    = ep.FindPropertyRelative("resourceTarget");
        SerializedProperty modeProp   = ep.FindPropertyRelative("valueMode");
        SerializedProperty valueProp  = ep.FindPropertyRelative("value");
        SerializedProperty currProp   = ep.FindPropertyRelative("currencyId");

        resProp.enumValueIndex  = EditorGUILayout.Popup("目标资源",  resProp.enumValueIndex,  _resourceTargetNames);
        modeProp.enumValueIndex = EditorGUILayout.Popup("值类型",    modeProp.enumValueIndex, _valueModeNames);
        EditorGUILayout.PropertyField(valueProp, new GUIContent("数值（正=恢复，负=扣）"));

        if (resProp.enumValueIndex == (int)ItemEffectResourceTarget.Currency)
            EditorGUILayout.PropertyField(currProp, new GUIContent("货币 ID"));

        if (showDot)
        {
            EditorGUILayout.PropertyField(ep.FindPropertyRelative("dotInterval"), new GUIContent("触发间隔（秒）"));
            EditorGUILayout.PropertyField(ep.FindPropertyRelative("dotDuration"),  new GUIContent("持续时长（秒）"));
        }
    }

    private static void DrawPropertyRow(string label, SerializedProperty prop)
    {
        if (prop != null)
            EditorGUILayout.PropertyField(prop, new GUIContent(label));
    }
}
