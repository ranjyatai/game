using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class SkyPrisonTechTreeInspectorPanel
{
    private static readonly (string name, Color color)[] PresetColors =
    {
        ("青", new Color(0.30f, 0.80f, 1.00f, 1f)),
        ("绿", new Color(0.40f, 0.86f, 0.50f, 1f)),
        ("黄", new Color(0.95f, 0.82f, 0.28f, 1f)),
        ("橙", new Color(1.00f, 0.60f, 0.20f, 1f)),
        ("红", new Color(0.93f, 0.34f, 0.34f, 1f)),
        ("紫", new Color(0.70f, 0.46f, 0.96f, 1f)),
        ("粉", new Color(0.94f, 0.39f, 0.64f, 1f)),
        ("灰", new Color(0.72f, 0.76f, 0.82f, 1f)),
    };

    private static readonly Dictionary<string, bool> LevelFoldouts = new Dictionary<string, bool>();
    private static readonly Dictionary<string, bool> SectionFoldouts = new Dictionary<string, bool>();

    public static void Draw(
        TechTreeGraphAsset selectedGraph,
        SerializedObject selectedSO,
        int selectedNodeIndex,
        Vector2 scroll,
        Action<Vector2> setScroll)
    {
        if (selectedGraph == null || selectedSO == null)
        {
            EditorGUILayout.HelpBox("没有可编辑的科技图。", MessageType.Info);
            return;
        }

        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        if (selectedNodeIndex < 0 || nodesProp == null || selectedNodeIndex >= nodesProp.arraySize)
        {
            EditorGUILayout.HelpBox("请选择一个科技节点。", MessageType.Info);
            return;
        }

        SerializedProperty nodeProp = nodesProp.GetArrayElementAtIndex(selectedNodeIndex);

        DrawNodeHeader(nodeProp, selectedNodeIndex);
        GUILayout.Space(8f);

        DrawBasicInfoSection(nodeProp);
        GUILayout.Space(8f);

        DrawVisualSection(nodeProp);
        GUILayout.Space(8f);

        DrawParentSection(nodeProp, nodesProp, selectedNodeIndex);
        GUILayout.Space(8f);

        DrawRequirementsSection(nodeProp, nodesProp, selectedNodeIndex);
        GUILayout.Space(8f);

        DrawLevelsSection(nodeProp);
    }

    private static void DrawNodeHeader(SerializedProperty nodeProp, int nodeIndex)
    {
        EditorGUILayout.BeginVertical("box");

        Rect accentRect = GUILayoutUtility.GetRect(1f, 4f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(accentRect, new Color(0.18f, 0.72f, 0.78f, 1f));

        EditorGUILayout.BeginHorizontal();

        SerializedProperty iconProp = nodeProp.FindPropertyRelative("icon");
        Sprite sprite = iconProp != null ? iconProp.objectReferenceValue as Sprite : null;
        Texture2D tex = sprite != null ? sprite.texture : null;

        Rect iconRect = GUILayoutUtility.GetRect(46f, 46f, GUILayout.Width(46f), GUILayout.Height(46f));
        if (tex != null)
            GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(1f, 1f, 1f, 0.06f));

        EditorGUILayout.BeginVertical();

        string nodeName = GetNodeName(nodeProp, nodeIndex);
        string nodeId = GetNodeId(nodeProp);

        SerializedProperty parentProp = nodeProp.FindPropertyRelative("primaryParentIndex");
        bool isRoot = parentProp != null && parentProp.intValue < 0;

        EditorGUILayout.LabelField(nodeName, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("ID: " + (string.IsNullOrWhiteSpace(nodeId) ? "-" : nodeId), EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"节点 #{nodeIndex} · {(isRoot ? "根节点" : "普通节点")}", EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private static void DrawReadonlyRow(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel(
            string.IsNullOrWhiteSpace(value) ? "-" : value,
            GUILayout.Height(EditorGUIUtility.singleLineHeight)
        );
        EditorGUILayout.EndHorizontal();
    }



    private static void DrawBasicInfoSection(SerializedProperty nodeProp)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("基础信息", EditorStyles.miniBoldLabel);

        SerializedProperty nodeIdProp = nodeProp.FindPropertyRelative("nodeId");
        DrawReadonlyRow("节点 ID", nodeIdProp != null ? nodeIdProp.stringValue : "-");

        DrawPropertyRow("节点名称", nodeProp.FindPropertyRelative("nodeName"));
        DrawPropertyRow("图标", nodeProp.FindPropertyRelative("icon"));
        DrawPropertyRow("启用", nodeProp.FindPropertyRelative("enabled"));
        DrawPropertyRow("最大等级", nodeProp.FindPropertyRelative("maxLevel"));
        DrawLocalizedDescriptionSection(nodeProp);
        DrawPropertyRow("设计备注", nodeProp.FindPropertyRelative("designerNote"), true);

        EditorGUILayout.EndVertical();
    }


    private static void DrawLocalizedDescriptionSection(SerializedProperty nodeProp)
    {
        SerializedProperty descriptionsProp = nodeProp.FindPropertyRelative("localizedDescriptions");
        SerializedProperty descriptionProp = nodeProp.FindPropertyRelative("description");
        if (descriptionsProp == null)
        {
            DrawPropertyRow("描述", descriptionProp, true);
            return;
        }

        LocalizationProjectSettings settings = LocalizationSettingsUtility.GetOrCreateSettings();
        if (settings == null)
        {
            DrawPropertyRow("描述", descriptionProp, true);
            return;
        }

        EnsureLocalizedEntries(descriptionsProp, settings);
        string defaultLanguageCode = GetDefaultLanguageCode(settings);
        List<LocalizationProjectSettings.LanguageEntry> orderedLanguages = GetOrderedLanguages(settings);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("多语言描述", EditorStyles.miniBoldLabel);

        for (int i = 0; i < orderedLanguages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = orderedLanguages[i];
            SerializedProperty entryProp = FindLocalizedEntry(descriptionsProp, lang.languageCode);
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
                            if (descriptionsProp.serializedObject == null)
                                return;
                            descriptionsProp.serializedObject.Update();
                            SerializedProperty localizedList = nodeProp.FindPropertyRelative("localizedDescriptions");
                            SerializedProperty entry = FindLocalizedEntry(localizedList, openLang);
                            if (entry != null)
                            {
                                SerializedProperty text = entry.FindPropertyRelative("text");
                                if (text != null)
                                    text.stringValue = updated ?? "";
                            }
                            SerializedProperty defaultEntry = FindLocalizedEntry(localizedList, defaultLanguageCode);
                            if (defaultEntry != null && descriptionProp != null)
                            {
                                SerializedProperty defaultText = defaultEntry.FindPropertyRelative("text");
                                descriptionProp.stringValue = defaultText != null ? (defaultText.stringValue ?? "") : "";
                            }
                            descriptionsProp.serializedObject.ApplyModifiedProperties();
                            EditorUtility.SetDirty(descriptionsProp.serializedObject.targetObject);
                        },
                        "node");
                };
            }
        }

        EditorGUILayout.EndVertical();

        SerializedProperty defaultEntryProp = FindLocalizedEntry(descriptionsProp, defaultLanguageCode);
        if (defaultEntryProp != null && descriptionProp != null)
        {
            SerializedProperty defaultTextProp = defaultEntryProp.FindPropertyRelative("text");
            descriptionProp.stringValue = defaultTextProp != null ? (defaultTextProp.stringValue ?? "") : "";
        }
    }

    private static void EnsureLocalizedEntries(SerializedProperty listProp, LocalizationProjectSettings settings)
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
            if (lang == null || !lang.enabled || existing.Contains(lang.languageCode))
                continue;
            int index = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(index);
            SerializedProperty item = listProp.GetArrayElementAtIndex(index);
            item.FindPropertyRelative("languageCode").stringValue = lang.languageCode;
            item.FindPropertyRelative("text").stringValue = string.Empty;
        }
    }

    private static List<LocalizationProjectSettings.LanguageEntry> GetOrderedLanguages(LocalizationProjectSettings settings)
    {
        List<LocalizationProjectSettings.LanguageEntry> result = new List<LocalizationProjectSettings.LanguageEntry>();
        LocalizationProjectSettings.LanguageEntry defaultLang = null;
        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang != null && lang.enabled && lang.isDefault)
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

    private static string GetDefaultLanguageCode(LocalizationProjectSettings settings)
    {
        if (settings == null || settings.languages == null || settings.languages.Count == 0)
            return "zh-CN";
        for (int i = 0; i < settings.languages.Count; i++)
        {
            LocalizationProjectSettings.LanguageEntry lang = settings.languages[i];
            if (lang != null && lang.enabled && lang.isDefault)
                return lang.languageCode;
        }
        return settings.languages[0].languageCode;
    }

    private static SerializedProperty FindLocalizedEntry(SerializedProperty listProp, string languageCode)
    {
        if (listProp == null)
            return null;
        for (int i = 0; i < listProp.arraySize; i++)
        {
            SerializedProperty item = listProp.GetArrayElementAtIndex(i);
            SerializedProperty codeProp = item.FindPropertyRelative("languageCode");
            if (codeProp != null && codeProp.stringValue == languageCode)
                return item;
        }
        return null;
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

    private static float GetPreviewCompactCharWidth(char c, GUIStyle style)
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

    private static float GetPreviewCharDrawOffset(char c)
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

    private static void DrawRichTextPreview(string richText)
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

    private static List<PreviewCharStyle> ParseRichTextPreview(string input)
    {
        List<PreviewCharStyle> result = new List<PreviewCharStyle>();
        bool bold = false, italic = false, underline = false, hasColor = false;
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
                            hasColor = true; color = parsed;
                        }
                        i = close + 1; continue;
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
            else i++;
            PreviewCharStyle s; s.character = c; s.bold = bold; s.italic = italic; s.underline = underline; s.hasColor = hasColor; s.color = color; result.Add(s);
        }
        return result;
    }

    private static void DrawVisualSection(SerializedProperty nodeProp)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("节点视觉", EditorStyles.miniBoldLabel);

        SerializedProperty useCustomColorProp = nodeProp.FindPropertyRelative("useCustomColor");
        SerializedProperty customColorProp = nodeProp.FindPropertyRelative("customColor");

        DrawPropertyRow("启用染色", useCustomColorProp);

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("快速配色", EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < PresetColors.Length; i++)
        {
            Rect colorRect = GUILayoutUtility.GetRect(22f, 22f, GUILayout.Width(22f), GUILayout.Height(22f));
            EditorGUI.DrawRect(colorRect, PresetColors[i].color);

            if (Event.current.type == EventType.MouseDown && colorRect.Contains(Event.current.mousePosition))
            {
                useCustomColorProp.boolValue = true;
                customColorProp.colorValue = PresetColors[i].color;
                GUI.changed = true;
                Event.current.Use();
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6f);

        EditorGUI.BeginDisabledGroup(useCustomColorProp == null || !useCustomColorProp.boolValue);
        DrawPropertyRow("节点色条", customColorProp);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("清除颜色", GUILayout.Width(90f)))
        {
            if (useCustomColorProp != null)
                useCustomColorProp.boolValue = false;
            if (customColorProp != null)
                customColorProp.colorValue = new Color(0.48f, 0.76f, 1f, 1f);
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private static void DrawParentSection(SerializedProperty nodeProp, SerializedProperty nodesProp, int selfIndex)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("主前置", EditorStyles.miniBoldLabel);

        SerializedProperty parentProp = nodeProp.FindPropertyRelative("primaryParentIndex");

        List<string> options = new List<string> { "无（根节点）" };
        List<int> optionValues = new List<int> { -1 };

        for (int i = 0; i < nodesProp.arraySize; i++)
        {
            if (i == selfIndex)
                continue;

            SerializedProperty otherNode = nodesProp.GetArrayElementAtIndex(i);
            options.Add(GetNodeDisplayLabel(otherNode, i));
            optionValues.Add(i);
        }

        int currentPopupIndex = 0;
        for (int i = 0; i < optionValues.Count; i++)
        {
            if (optionValues[i] == parentProp.intValue)
            {
                currentPopupIndex = i;
                break;
            }
        }

        int newPopupIndex = EditorGUILayout.Popup("主前置节点", currentPopupIndex, options.ToArray());
        if (newPopupIndex >= 0 && newPopupIndex < optionValues.Count)
            parentProp.intValue = optionValues[newPopupIndex];

        EditorGUILayout.EndVertical();
    }

    private static void DrawRequirementsSection(SerializedProperty nodeProp, SerializedProperty nodesProp, int selfIndex)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("附加条件", EditorStyles.miniBoldLabel);

        SerializedProperty requirementsProp = nodeProp.FindPropertyRelative("secondaryRequirements");
        if (requirementsProp == null)
        {
            EditorGUILayout.HelpBox("未找到附加条件字段。", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        if (requirementsProp.arraySize == 0)
            EditorGUILayout.LabelField("当前没有附加条件。", EditorStyles.miniLabel);

        for (int i = 0; i < requirementsProp.arraySize; i++)
        {
            SerializedProperty req = requirementsProp.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginVertical("box");

            DrawRequirementTargetNodeSelector(req, nodesProp, selfIndex);
            DrawPropertyRow("条件类型", req.FindPropertyRelative("requirementType"));
            DrawPropertyRow("所需等级", req.FindPropertyRelative("requiredLevel"));

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("-", GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                requirementsProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("添加附加条件", GUILayout.Height(22f)))
            requirementsProp.arraySize++;

        EditorGUILayout.EndVertical();
    }

    private static void DrawRequirementTargetNodeSelector(SerializedProperty req, SerializedProperty nodesProp, int selfIndex)
    {
        SerializedProperty targetNodeIdProp = req.FindPropertyRelative("targetNodeId");
        if (targetNodeIdProp == null)
        {
            EditorGUILayout.LabelField("目标节点", "字段不存在");
            return;
        }

        List<string> options = new List<string> { "未选择" };
        List<string> optionNodeIds = new List<string> { "" };

        for (int i = 0; i < nodesProp.arraySize; i++)
        {
            if (i == selfIndex)
                continue;

            SerializedProperty node = nodesProp.GetArrayElementAtIndex(i);
            string nodeId = GetNodeId(node);
            options.Add(GetNodeDisplayLabel(node, i));
            optionNodeIds.Add(nodeId);
        }

        int currentPopupIndex = 0;
        for (int i = 0; i < optionNodeIds.Count; i++)
        {
            if (optionNodeIds[i] == targetNodeIdProp.stringValue)
            {
                currentPopupIndex = i;
                break;
            }
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("目标节点", GUILayout.Width(88f));
        int newPopupIndex = EditorGUILayout.Popup(currentPopupIndex, options.ToArray());
        EditorGUILayout.EndHorizontal();

        if (newPopupIndex >= 0 && newPopupIndex < optionNodeIds.Count)
            targetNodeIdProp.stringValue = optionNodeIds[newPopupIndex];
    }

    private static void DrawLevelsSection(SerializedProperty nodeProp)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("等级配置", EditorStyles.miniBoldLabel);

        SerializedProperty levelsProp = nodeProp.FindPropertyRelative("levels");
        SerializedProperty maxLevelProp = nodeProp.FindPropertyRelative("maxLevel");
        string nodeId = GetNodeId(nodeProp);

        if (GUILayout.Button("同步当前节点等级列表", GUILayout.Height(22f)))
            SyncLevels(levelsProp, Mathf.Max(1, maxLevelProp.intValue));

        if (levelsProp == null)
        {
            EditorGUILayout.HelpBox("未找到 levels 字段。", MessageType.Warning);
            EditorGUILayout.EndVertical();
            return;
        }

        for (int i = 0; i < levelsProp.arraySize; i++)
        {
            SerializedProperty level = levelsProp.GetArrayElementAtIndex(i);
            SerializedProperty levelNumberProp = level.FindPropertyRelative("level");
            SerializedProperty costsProp = level.FindPropertyRelative("costs");
            SerializedProperty rewardsProp = level.FindPropertyRelative("rewards");
            SerializedProperty noteProp = level.FindPropertyRelative("note");

            int levelNumber = levelNumberProp != null ? levelNumberProp.intValue : (i + 1);
            string foldKey = $"{nodeId}_level_{levelNumber}";
            if (!LevelFoldouts.ContainsKey(foldKey))
                LevelFoldouts[foldKey] = true;

            LevelFoldouts[foldKey] = EditorGUILayout.Foldout(LevelFoldouts[foldKey], $"等级 {levelNumber}", true);

            if (!LevelFoldouts[foldKey])
                continue;

            EditorGUILayout.BeginVertical("box");

            DrawCostRows(costsProp, $"{foldKey}_costs");
            GUILayout.Space(8f);
            DrawRewardRows(rewardsProp, $"{foldKey}_rewards");
            GUILayout.Space(8f);
            DrawPropertyRow("备注", noteProp, true);

            EditorGUILayout.EndVertical();
            GUILayout.Space(6f);
        }

        EditorGUILayout.EndVertical();
    }

    private static void DrawCostRows(SerializedProperty costsProp, string foldKey)
    {
        if (!SectionFoldouts.ContainsKey(foldKey))
            SectionFoldouts[foldKey] = true;

        SectionFoldouts[foldKey] = EditorGUILayout.Foldout(SectionFoldouts[foldKey], "所需道具", true);
        if (!SectionFoldouts[foldKey])
            return;

        if (costsProp == null)
        {
            EditorGUILayout.LabelField("未找到 costs 字段。", EditorStyles.miniLabel);
            return;
        }

        if (costsProp.arraySize == 0)
            EditorGUILayout.LabelField("当前无道具需求。", EditorStyles.miniLabel);

        for (int i = 0; i < costsProp.arraySize; i++)
        {
            SerializedProperty cost = costsProp.GetArrayElementAtIndex(i);
            SerializedProperty itemProp = cost.FindPropertyRelative("item");
            SerializedProperty amountProp = cost.FindPropertyRelative("amount");

            EditorGUILayout.BeginHorizontal("box");

            Texture2D icon = GetObjectIcon(itemProp.objectReferenceValue);
            Rect iconRect = GUILayoutUtility.GetRect(28f, 28f, GUILayout.Width(28f), GUILayout.Height(28f));
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            else
                EditorGUI.DrawRect(iconRect, new Color(1f, 1f, 1f, 0.06f));

            string label = itemProp.objectReferenceValue != null ? itemProp.objectReferenceValue.name : "未选择道具";
            GUILayout.Label(label, GUILayout.Width(150f));

            GUILayout.Label("×", GUILayout.Width(14f));
            amountProp.intValue = EditorGUILayout.IntField(amountProp.intValue, GUILayout.Width(60f));

            if (GUILayout.Button("选择", GUILayout.Width(48f)))
            {
                SkyPrisonItemPickerPopup.Open(
                    itemProp.objectReferenceValue,
                    obj => { itemProp.objectReferenceValue = obj; },
                    "Item",
                    "ItemDefinition",
                    "ItemData",
                    "SkyPrisonItemDefinition"
                );
            }

            if (GUILayout.Button("-", GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                costsProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                break;
            }

            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("+ 添加道具", GUILayout.Height(22f)))
            costsProp.arraySize++;
    }

    private static void DrawRewardRows(SerializedProperty rewardsProp, string foldKey)
    {
        if (!SectionFoldouts.ContainsKey(foldKey))
            SectionFoldouts[foldKey] = true;

        SectionFoldouts[foldKey] = EditorGUILayout.Foldout(SectionFoldouts[foldKey], "等级收益", true);
        if (!SectionFoldouts[foldKey])
            return;

        if (rewardsProp == null)
        {
            EditorGUILayout.LabelField("未找到 rewards 字段。", EditorStyles.miniLabel);
            return;
        }

        if (rewardsProp.arraySize == 0)
            EditorGUILayout.LabelField("当前无等级收益。", EditorStyles.miniLabel);

        for (int i = 0; i < rewardsProp.arraySize; i++)
        {
            SerializedProperty reward = rewardsProp.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginVertical("box");
            DrawPropertyRow("Key", reward.FindPropertyRelative("key"));
            DrawPropertyRow("显示名", reward.FindPropertyRelative("displayName"));
            DrawPropertyRow("数值", reward.FindPropertyRelative("value"));
            DrawPropertyRow("百分比", reward.FindPropertyRelative("isPercent"));
            DrawPropertyRow("备注", reward.FindPropertyRelative("note"), true);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("-", GUILayout.Width(24f), GUILayout.Height(20f)))
            {
                rewardsProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("添加收益", GUILayout.Height(22f)))
            rewardsProp.arraySize++;
    }

    private static void SyncLevels(SerializedProperty levelsProp, int maxLevel)
    {
        if (levelsProp == null)
            return;

        levelsProp.arraySize = Mathf.Max(1, maxLevel);

        for (int i = 0; i < levelsProp.arraySize; i++)
        {
            SerializedProperty level = levelsProp.GetArrayElementAtIndex(i);
            level.FindPropertyRelative("level").intValue = i + 1;
        }
    }

    private static Texture2D GetObjectIcon(UnityEngine.Object obj)
    {
        if (obj == null)
            return null;

        Texture2D preview = AssetPreview.GetAssetPreview(obj);
        if (preview != null)
            return preview;

        GUIContent iconContent = EditorGUIUtility.ObjectContent(obj, obj.GetType());
        return iconContent?.image as Texture2D;
    }

    private static string GetNodeName(SerializedProperty nodeProp, int fallbackIndex)
    {
        SerializedProperty nodeNameProp = nodeProp.FindPropertyRelative("nodeName");
        string nodeName = nodeNameProp != null ? nodeNameProp.stringValue : "";
        if (string.IsNullOrWhiteSpace(nodeName))
            nodeName = $"节点 {fallbackIndex}";
        return nodeName;
    }

    private static string GetNodeId(SerializedProperty nodeProp)
    {
        SerializedProperty nodeIdProp = nodeProp.FindPropertyRelative("nodeId");
        return nodeIdProp != null ? nodeIdProp.stringValue : "";
    }

    private static string GetNodeDisplayLabel(SerializedProperty nodeProp, int fallbackIndex)
    {
        string nodeName = GetNodeName(nodeProp, fallbackIndex);
        string nodeId = GetNodeId(nodeProp);

        if (string.IsNullOrWhiteSpace(nodeId))
            return nodeName;

        return $"{nodeName}  ({nodeId})";
    }

    private static void DrawPropertyRow(string label, SerializedProperty property, bool multiline = false)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));

        if (property == null)
        {
            EditorGUILayout.LabelField("字段不存在");
        }
        else if (multiline && property.propertyType == SerializedPropertyType.String)
        {
            property.stringValue = EditorGUILayout.TextArea(property.stringValue, GUILayout.MinHeight(54f));
        }
        else
        {
            EditorGUILayout.PropertyField(property, GUIContent.none, true);
        }

        EditorGUILayout.EndHorizontal();
    }
}
