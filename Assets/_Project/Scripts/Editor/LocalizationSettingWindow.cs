using UnityEditor;
using UnityEngine;

public class LocalizationSettingsWindow : EditorWindow
{
    private const float ToolbarHeight = 22f;
    private const float SplitterWidth = 4f;
    private const float MinLeftWidth = 220f;
    private const float MaxLeftWidth = 420f;

    private LocalizationProjectSettings settings;
    private SerializedObject serializedSettings;

    private Vector2 leftScroll;
    private Vector2 rightScroll;

    private float leftPanelWidth = 280f;
    private bool draggingSplitter = false;

    private int selectedLanguageIndex = -1;

    [MenuItem("Tools/语言与字体设置")]
    public static void OpenWindow()
    {
        LocalizationSettingsWindow window = GetWindow<LocalizationSettingsWindow>("语言与字体设置");
        window.minSize = new Vector2(900f, 560f);
        window.Show();
    }

    private void OnEnable()
    {
        ReloadSettings();
    }

    private void OnGUI()
    {
        if (settings == null)
            ReloadSettings();

        if (settings == null)
        {
            EditorGUILayout.HelpBox("无法加载本地化设置资产。", MessageType.Error);
            return;
        }

        serializedSettings.Update();

        DrawToolbar();

        Rect bodyRect = new Rect(0f, ToolbarHeight, position.width, position.height - ToolbarHeight);

        HandleSplitterEvents(bodyRect);

        Rect leftRect = new Rect(bodyRect.x, bodyRect.y, leftPanelWidth, bodyRect.height);
        Rect splitterRect = new Rect(leftRect.xMax, bodyRect.y, SplitterWidth, bodyRect.height);
        Rect rightRect = new Rect(splitterRect.xMax, bodyRect.y, bodyRect.width - leftPanelWidth - SplitterWidth, bodyRect.height);

        DrawLeftPanel(leftRect);
        DrawSplitter(splitterRect);
        DrawRightPanel(rightRect);

        serializedSettings.ApplyModifiedProperties();

        if (settings != null)
        {
            settings.EnsureSingleDefault();

            if (GUI.changed)
                EditorUtility.SetDirty(settings);
        }
    }

    private void ReloadSettings()
    {
        settings = LocalizationSettingsUtility.GetOrCreateSettings();
        serializedSettings = settings != null ? new SerializedObject(settings) : null;

        if (settings != null)
        {
            if (settings.languages.Count > 0)
                selectedLanguageIndex = Mathf.Clamp(selectedLanguageIndex, 0, settings.languages.Count - 1);
            else
                selectedLanguageIndex = -1;
        }

        ForceRefreshSelectionView();
    }

    private void ForceRefreshSelectionView()
    {
        GUIUtility.keyboardControl = 0;
        EditorGUIUtility.editingTextField = false;
        GUI.FocusControl(null);
        Repaint();
    }

    private void SelectLanguage(int index)
    {
        if (settings == null)
            return;

        if (index < 0 || index >= settings.languages.Count)
            return;

        if (selectedLanguageIndex == index)
            return;

        selectedLanguageIndex = index;
        ForceRefreshSelectionView();
    }

    private void DrawToolbar()
    {
        Rect rect = new Rect(0f, 0f, position.width, ToolbarHeight);
        GUILayout.BeginArea(rect, EditorStyles.toolbar);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            ReloadSettings();

        if (GUILayout.Button("定位设置资产", EditorStyles.toolbarButton, GUILayout.Width(100f)))
        {
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        GUILayout.Space(8f);
        GUILayout.Label("项目级语言与字体设置", EditorStyles.miniLabel);

        GUILayout.FlexibleSpace();

        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void HandleSplitterEvents(Rect bodyRect)
    {
        Rect splitterRect = new Rect(leftPanelWidth, bodyRect.y, SplitterWidth, bodyRect.height);
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseDown && splitterRect.Contains(e.mousePosition))
        {
            draggingSplitter = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && draggingSplitter)
        {
            leftPanelWidth = Mathf.Clamp(e.mousePosition.x, MinLeftWidth, MaxLeftWidth);
            Repaint();
            e.Use();
        }
        else if (e.type == EventType.MouseUp && draggingSplitter)
        {
            draggingSplitter = false;
            e.Use();
        }
    }

    private void DrawLeftPanel(Rect rect)
    {
        GUILayout.BeginArea(rect, EditorStyles.helpBox);

        DrawLeftHeader();

        leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

        SerializedProperty languagesProp = serializedSettings.FindProperty("languages");

        for (int i = 0; i < languagesProp.arraySize; i++)
        {
            SerializedProperty entry = languagesProp.GetArrayElementAtIndex(i);

            SerializedProperty enabledProp = entry.FindPropertyRelative("enabled");
            SerializedProperty isDefaultProp = entry.FindPropertyRelative("isDefault");
            SerializedProperty displayNameProp = entry.FindPropertyRelative("displayName");
            SerializedProperty codeProp = entry.FindPropertyRelative("languageCode");

            Rect rowRect = EditorGUILayout.GetControlRect(false, 40f);

            bool isSelected = selectedLanguageIndex == i;
            if (isSelected)
                EditorGUI.DrawRect(rowRect, new Color(0.35f, 0.55f, 0.90f, 0.65f));

            string defaultMark = isDefaultProp.boolValue ? "★ " : "";
            string enabledMark = enabledProp.boolValue ? "" : "[停用] ";
            string display = string.IsNullOrWhiteSpace(displayNameProp.stringValue) ? "(未命名语种)" : displayNameProp.stringValue;
            string code = string.IsNullOrWhiteSpace(codeProp.stringValue) ? "-" : codeProp.stringValue;

            Rect line1 = new Rect(rowRect.x + 6f, rowRect.y + 2f, rowRect.width - 12f, 18f);
            Rect line2 = new Rect(rowRect.x + 6f, rowRect.y + 20f, rowRect.width - 12f, 16f);

            EditorGUI.LabelField(line1, $"{defaultMark}{enabledMark}{display}");
            EditorGUI.LabelField(line2, code, EditorStyles.miniLabel);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition) && e.button == 0)
            {
                SelectLanguage(i);
                e.Use();
            }
        }

        EditorGUILayout.EndScrollView();

        GUILayout.EndArea();
    }

    private void DrawLeftHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("语种列表", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "左边管理语种，右边编辑当前语种的基础信息与字体设置。",
            MessageType.Info
        );

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("新增语种"))
            AddLanguage();

        if (GUILayout.Button("删除语种"))
            RemoveSelectedLanguage();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("上移"))
            MoveSelectedLanguage(-1);

        if (GUILayout.Button("下移"))
            MoveSelectedLanguage(1);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawSplitter(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
    }

    private void DrawRightPanel(Rect rect)
    {
        GUILayout.BeginArea(rect);

        rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

        SerializedProperty languagesProp = serializedSettings.FindProperty("languages");

        if (languagesProp.arraySize == 0)
        {
            EditorGUILayout.HelpBox("请先在左边新增一个语种。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
            return;
        }

        if (selectedLanguageIndex < 0 || selectedLanguageIndex >= languagesProp.arraySize)
        {
            EditorGUILayout.HelpBox("请先在左边选择一个语种。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
            return;
        }

        SerializedProperty entry = languagesProp.GetArrayElementAtIndex(selectedLanguageIndex);
        DrawLanguageEntry(entry, selectedLanguageIndex);

        EditorGUILayout.EndScrollView();

        GUILayout.EndArea();
    }

    private void DrawLanguageEntry(SerializedProperty entry, int index)
    {
        SerializedProperty enabledProp = entry.FindPropertyRelative("enabled");
        SerializedProperty isDefaultProp = entry.FindPropertyRelative("isDefault");
        SerializedProperty codeProp = entry.FindPropertyRelative("languageCode");
        SerializedProperty displayNameProp = entry.FindPropertyRelative("displayName");
        SerializedProperty noteProp = entry.FindPropertyRelative("note");
        SerializedProperty primaryFontProp = entry.FindPropertyRelative("primaryFont");
        SerializedProperty fallbackFontsProp = entry.FindPropertyRelative("fallbackFonts");

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("基础信息", EditorStyles.boldLabel);

        DrawRow("启用", enabledProp);
        DrawRow("默认语言", isDefaultProp);
        DrawRow("语言代码", codeProp);
        DrawRow("显示名称", displayNameProp);
        DrawRow("备注", noteProp, true);

        string currentCode = codeProp.stringValue?.Trim();
        if (!string.IsNullOrWhiteSpace(currentCode) && settings.HasLanguageCode(currentCode, index))
        {
            EditorGUILayout.HelpBox(
                $"语言代码 \"{currentCode}\" 已与其他语种重复。建议保持唯一。",
                MessageType.Warning
            );
        }

        if (isDefaultProp.boolValue)
        {
            for (int i = 0; i < settings.languages.Count; i++)
            {
                if (i == index)
                    continue;

                if (settings.languages[i] != null)
                    settings.languages[i].isDefault = false;
            }
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6f);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("字体设置", EditorStyles.boldLabel);

        DrawRow("主字体文件", primaryFontProp);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("备用字体列表", GUILayout.Width(140f));
        EditorGUILayout.BeginVertical();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("添加备用字体", GUILayout.Width(100f)))
            fallbackFontsProp.arraySize++;

        if (GUILayout.Button("清空", GUILayout.Width(60f)))
            fallbackFontsProp.ClearArray();
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < fallbackFontsProp.arraySize; i++)
        {
            SerializedProperty fontProp = fallbackFontsProp.GetArrayElementAtIndex(i);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(fontProp, GUIContent.none);
            if (GUILayout.Button("删除", GUILayout.Width(50f)))
            {
                fallbackFontsProp.DeleteArrayElementAtIndex(i);
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawRow(string label, SerializedProperty property, bool multiline = false)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));

        if (multiline && property.propertyType == SerializedPropertyType.String)
            property.stringValue = EditorGUILayout.TextArea(property.stringValue, GUILayout.MinHeight(54f));
        else
            EditorGUILayout.PropertyField(property, GUIContent.none, true);

        EditorGUILayout.EndHorizontal();
    }

    private void AddLanguage()
    {
        Undo.RecordObject(settings, "Add Language");

        LocalizationProjectSettings.LanguageEntry entry = new LocalizationProjectSettings.LanguageEntry
        {
            enabled = true,
            isDefault = settings.languages.Count == 0,
            languageCode = settings.GenerateUniqueLanguageCode("new-language"),
            displayName = "新语种"
        };

        settings.languages.Add(entry);
        settings.EnsureSingleDefault();

        selectedLanguageIndex = settings.languages.Count - 1;

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        ForceRefreshSelectionView();
    }

    private void RemoveSelectedLanguage()
    {
        if (selectedLanguageIndex < 0 || selectedLanguageIndex >= settings.languages.Count)
            return;

        bool ok = EditorUtility.DisplayDialog(
            "删除语种",
            $"确定删除语种：\n{settings.languages[selectedLanguageIndex].displayName}",
            "删除",
            "取消"
        );

        if (!ok)
            return;

        Undo.RecordObject(settings, "Remove Language");
        settings.languages.RemoveAt(selectedLanguageIndex);
        settings.EnsureSingleDefault();

        if (settings.languages.Count == 0)
            selectedLanguageIndex = -1;
        else
            selectedLanguageIndex = Mathf.Clamp(selectedLanguageIndex, 0, settings.languages.Count - 1);

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        ForceRefreshSelectionView();
    }

    private void MoveSelectedLanguage(int direction)
    {
        if (selectedLanguageIndex < 0 || selectedLanguageIndex >= settings.languages.Count)
            return;

        int newIndex = selectedLanguageIndex + direction;
        if (newIndex < 0 || newIndex >= settings.languages.Count)
            return;

        Undo.RecordObject(settings, "Move Language");

        LocalizationProjectSettings.LanguageEntry temp = settings.languages[selectedLanguageIndex];
        settings.languages[selectedLanguageIndex] = settings.languages[newIndex];
        settings.languages[newIndex] = temp;

        selectedLanguageIndex = newIndex;

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        ForceRefreshSelectionView();
    }
}
