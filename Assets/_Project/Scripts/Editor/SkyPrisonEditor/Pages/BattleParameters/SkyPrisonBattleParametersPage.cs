using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonBattleParametersPage : SkyPrisonEditorPageBase
{
    private const string DefaultAssetFolder = "Assets/_Project/Resources/BattleParameters";

    private const float LeftPanelMinTopHeight = 126f;
    private const float LeftPanelDefaultTopHeight = 190f;
    private const float LeftPanelMaxTopHeight = 520f;
    private const float LeftPanelInnerSplitterHeight = 4f;

    private const float LeftTitleRowHeight = 22f;
    private const float LeftToolbarRowHeight = 24f;
    private const float LeftRowGap = 6f;
    private const float LeftContainerPadding = 8f;
    private const float LeftListRowHeight = 24f;
    private const float LeftSectionRowHeight = 28f;

    private const string RefreshIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_18.png";

    private readonly Color leftTopBg = new Color(0.13f, 0.13f, 0.14f, 1f);
    private readonly Color accentOrange = new Color(1.00f, 0.60f, 0.18f, 1f);

    private BattleParameterDatabase selectedDatabase;
    private SerializedObject selectedSO;
    private BattleParameterSection selectedSection = BattleParameterSection.CoreAttributes;

    private Vector2 databaseListScroll;
    private Vector2 leftSectionScroll;
    private float leftTopPanelHeight = LeftPanelDefaultTopHeight;
    private bool draggingLeftInnerSplitter = false;

    private readonly List<BattleParameterDatabase> databases = new List<BattleParameterDatabase>();
    private readonly SkyPrisonBattleDefinitionsWorkspace definitionsWorkspace;
    private readonly SkyPrisonBattleSimulationWorkspace simulationWorkspace;

    public SkyPrisonBattleParametersPage(SkyPrisonEditorContext context) : base(context)
    {
        definitionsWorkspace = new SkyPrisonBattleDefinitionsWorkspace(this);
        simulationWorkspace = new SkyPrisonBattleSimulationWorkspace(this);
    }

    public override string TabName => "战斗参数";

    public Color AccentOrange => accentOrange;
    public BattleParameterDatabase SelectedDatabase => selectedDatabase;
    public SerializedObject SelectedSO => selectedSO;
    public BattleParameterSection SelectedSection => selectedSection;

    public override void OnEnable()
    {
        Refresh();
    }

    public override void Refresh()
    {
        databases.Clear();

        string[] guids = AssetDatabase.FindAssets("t:BattleParameterDatabase");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            BattleParameterDatabase db = AssetDatabase.LoadAssetAtPath<BattleParameterDatabase>(path);
            if (db != null)
                databases.Add(db);
        }

        if (selectedDatabase == null)
        {
            SelectDatabase(databases.FirstOrDefault());
        }
        else
        {
            string selectedPath = AssetDatabase.GetAssetPath(selectedDatabase);
            BattleParameterDatabase matched = databases.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == selectedPath);
            SelectDatabase(matched ?? databases.FirstOrDefault());
        }
    }

    public override void OnGUILeft()
    {
        Rect fullRect = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        HandleLeftInnerSplitter(fullRect);

        float maxTopHeight = Mathf.Min(LeftPanelMaxTopHeight, Mathf.Max(LeftPanelMinTopHeight, fullRect.height - 120f));
        float topHeight = Mathf.Clamp(leftTopPanelHeight, LeftPanelMinTopHeight, maxTopHeight);

        Rect topRegionRect = new Rect(fullRect.x, fullRect.y, fullRect.width, topHeight);
        Rect innerSplitterRect = new Rect(fullRect.x, topRegionRect.yMax, fullRect.width, LeftPanelInnerSplitterHeight);
        Rect bottomRegionRect = new Rect(fullRect.x, innerSplitterRect.yMax + 6f, fullRect.width, Mathf.Max(100f, fullRect.yMax - (innerSplitterRect.yMax + 6f)));

        DrawLeftTopRegion(topRegionRect);
        DrawLeftInnerSplitter(innerSplitterRect);
        DrawLeftBottomRegion(bottomRegionRect);
    }

    public override void OnGUIRight()
    {
        if (selectedDatabase == null)
        {
            EditorGUILayout.HelpBox("请先创建或选择一个战斗参数库。", MessageType.Info);
            return;
        }

        if (selectedSO == null || selectedSO.targetObject != selectedDatabase)
            selectedSO = new SerializedObject(selectedDatabase);

        selectedSO.Update();
        definitionsWorkspace.EnsureMigration();

        bool isSimulationSection = selectedSection == BattleParameterSection.BuildEvaluation;
        if (isSimulationSection)
            simulationWorkspace.Draw();
        else
            definitionsWorkspace.Draw();

        selectedSO.ApplyModifiedProperties();
        if (GUI.changed)
            EditorUtility.SetDirty(selectedDatabase);
    }

    public void SetRuntimeActiveDatabase(BattleParameterDatabase database)
    {
        if (database == null)
            return;

        for (int i = 0; i < databases.Count; i++)
        {
            BattleParameterDatabase db = databases[i];
            if (db == null)
                continue;

            bool shouldBeActive = db == database;
            if (db.isRuntimeActive == shouldBeActive)
                continue;

            db.isRuntimeActive = shouldBeActive;
            EditorUtility.SetDirty(db);
        }

        AssetDatabase.SaveAssets();
        Refresh();
    }

    private void DrawLeftTopRegion(Rect rect)
    {
        float y = rect.y;

        Rect titleRect = new Rect(rect.x, y, rect.width, LeftTitleRowHeight);
        y += LeftTitleRowHeight + LeftRowGap;

        Rect toolbarRect = new Rect(rect.x, y, rect.width, LeftToolbarRowHeight);
        y += LeftToolbarRowHeight + LeftRowGap;

        Rect containerRect = new Rect(rect.x, y, rect.width, Mathf.Max(40f, rect.yMax - y));

        DrawLeftLabelRow(titleRect, "参数库");
        DrawDatabaseToolbarRow(toolbarRect);
        DrawDatabaseContainer(containerRect);
    }

    private void DrawLeftBottomRegion(Rect rect)
    {
        float y = rect.y;

        Rect titleRect = new Rect(rect.x, y, rect.width, LeftTitleRowHeight);
        y += LeftTitleRowHeight + 4f;

        Rect lineRect = new Rect(rect.x, y, rect.width, 1f);
        y += 7f;

        Rect listRect = new Rect(rect.x, y, rect.width, Mathf.Max(40f, rect.yMax - y));

        DrawLeftLabelRow(titleRect, "模块索引");
        EditorGUI.DrawRect(lineRect, new Color(1f, 1f, 1f, 0.10f));
        DrawLeftSectionList(listRect);
    }

    private void DrawLeftLabelRow(Rect rect, string label)
    {
        GUI.Label(rect, label, EditorStyles.boldLabel);
    }

    private void DrawDatabaseToolbarRow(Rect rect)
    {
        Texture2D refreshIcon = LoadOptionalIcon(RefreshIconPath);

        const float buttonSize = 20f;
        const float gap = 4f;

        float y = rect.y + (rect.height - buttonSize) * 0.5f;
        float right = rect.xMax;

        Rect refreshRect = new Rect(right - buttonSize, y, buttonSize, buttonSize);
        Rect minusRect = new Rect(refreshRect.x - gap - buttonSize, y, buttonSize, buttonSize);
        Rect plusRect = new Rect(minusRect.x - gap - buttonSize, y, buttonSize, buttonSize);

        if (DrawToolButton(plusRect, "+", "新建战斗参数库"))
            CreateDatabase();

        using (new EditorGUI.DisabledScope(selectedDatabase == null))
        {
            if (DrawToolButton(minusRect, "-", "删除当前战斗参数库"))
                DeleteSelectedDatabase();
        }

        if (DrawToolButton(refreshRect, refreshIcon, "刷新"))
            Refresh();
    }

    private void DrawDatabaseContainer(Rect rect)
    {
        EditorGUI.DrawRect(rect, leftTopBg);
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.06f));

        Rect viewRect = new Rect(rect.x + LeftContainerPadding, rect.y + LeftContainerPadding, rect.width - LeftContainerPadding * 2f, rect.height - LeftContainerPadding * 2f);
        float contentHeight = Mathf.Max(viewRect.height, databases.Count * LeftListRowHeight);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        databaseListScroll = GUI.BeginScrollView(viewRect, databaseListScroll, contentRect, false, true);

        for (int i = 0; i < databases.Count; i++)
        {
            BattleParameterDatabase db = databases[i];
            if (db == null)
                continue;

            Rect rowRect = new Rect(0f, i * LeftListRowHeight, contentRect.width, LeftListRowHeight);
            bool active = selectedDatabase == db;
            string label = string.IsNullOrWhiteSpace(db.displayName) ? db.name : db.displayName;
            if (db.isRuntimeActive)
                label += "   [运行时]";

            DrawFlatSelectableRow(rowRect, active, label, accentOrange, () => SelectDatabase(db));
        }

        GUI.EndScrollView();
    }

    private void DrawLeftSectionList(Rect rect)
    {
        string[] labels =
        {
            "核心属性",
            "动作模组",
            "伤害类型",
            "属性 / 异常",
            "状态类型 / 显示",
            "公式规则",
            "Build 评估"
        };

        BattleParameterSection[] sections =
        {
            BattleParameterSection.CoreAttributes,
            BattleParameterSection.ActionModules,
            BattleParameterSection.DamageTypes,
            BattleParameterSection.ElementAnomalies,
            BattleParameterSection.StatusDisplays,
            BattleParameterSection.FormulaRules,
            BattleParameterSection.BuildEvaluation
        };

        float contentHeight = Mathf.Max(rect.height, labels.Length * LeftSectionRowHeight);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, rect.width - 12f), contentHeight);

        leftSectionScroll = GUI.BeginScrollView(rect, leftSectionScroll, contentRect, false, true);
        for (int i = 0; i < labels.Length; i++)
        {
            Rect rowRect = new Rect(0f, i * LeftSectionRowHeight, contentRect.width, LeftSectionRowHeight);
            DrawSectionNavItem(rowRect, sections[i], labels[i]);
        }
        GUI.EndScrollView();
    }

    private void DrawSectionNavItem(Rect rect, BattleParameterSection section, string label)
    {
        bool selected = selectedSection == section;
        bool hover = rect.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.30f, 0.20f, 0.10f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accentOrange);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.04f));
        }

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            selectedSection = section;

        GUIStyle style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(12, 6, 0, 0),
            fontSize = 12,
            normal = { textColor = selected ? Color.white : new Color(0.90f, 0.90f, 0.92f, 1f) }
        };

        GUI.Label(rect, label, style);
    }

    private void HandleLeftInnerSplitter(Rect fullRect)
    {
        float splitY = fullRect.y + leftTopPanelHeight;
        Rect splitterRect = new Rect(fullRect.x, splitY, fullRect.width, LeftPanelInnerSplitterHeight);
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);

        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseDown && splitterRect.Contains(e.mousePosition))
        {
            draggingLeftInnerSplitter = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && draggingLeftInnerSplitter)
        {
            float localHeight = e.mousePosition.y - fullRect.y;
            float maxHeight = Mathf.Min(LeftPanelMaxTopHeight, Mathf.Max(LeftPanelMinTopHeight, fullRect.height - 120f));
            leftTopPanelHeight = Mathf.Clamp(localHeight, LeftPanelMinTopHeight, maxHeight);
            GUI.changed = true;
            e.Use();
        }
        else if (e.type == EventType.MouseUp && draggingLeftInnerSplitter)
        {
            draggingLeftInnerSplitter = false;
            e.Use();
        }
    }

    private void DrawLeftInnerSplitter(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.10f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
    }

    private bool DrawToolButton(Rect rect, string text, string tooltip)
    {
        return DrawMiniToolbarButton(rect, text, tooltip, null);
    }

    private bool DrawToolButton(Rect rect, Texture2D icon, string tooltip)
    {
        return DrawMiniToolbarButton(rect, string.Empty, tooltip, icon);
    }

    private bool DrawMiniToolbarButton(Rect rect, string text, string tooltip, Texture2D icon)
    {
        Event e = Event.current;
        bool hover = rect.Contains(e.mousePosition);
        bool clicked = e.type == EventType.MouseDown && e.button == 0 && hover;

        Color bg = hover ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.04f);
        EditorGUI.DrawRect(rect, bg);
        DrawThinBorder(rect, new Color(1f, 1f, 1f, hover ? 0.12f : 0.05f));

        if (icon != null)
            GUI.DrawTexture(new Rect(rect.x + 3f, rect.y + 3f, 14f, 14f), icon, ScaleMode.ScaleToFit, true);
        else
            GUI.Label(rect, new GUIContent(text, tooltip), GetCenteredToolbarTextStyle());

        if (clicked)
        {
            e.Use();
            GUI.changed = true;
            return true;
        }

        if (!string.IsNullOrEmpty(tooltip))
            EditorGUI.LabelField(rect, new GUIContent(string.Empty, tooltip));

        return false;
    }

    private void DrawFlatSelectableRow(Rect rect, bool selected, string label, Color accent, System.Action onClick)
    {
        Event e = Event.current;
        bool hover = rect.Contains(e.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.28f, 0.20f, 0.12f, 1f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.05f));
        }

        GUIStyle style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 6, 0, 0),
            normal = { textColor = selected ? Color.white : new Color(0.88f, 0.88f, 0.90f, 1f) }
        };

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            onClick?.Invoke();

        GUI.Label(rect, label, style);
    }

    private void CreateDatabase()
    {
        EnsureFolderExists(DefaultAssetFolder);

        BattleParameterDatabase asset = ScriptableObject.CreateInstance<BattleParameterDatabase>();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{DefaultAssetFolder}/BattleParameterDatabase.asset");

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Refresh();
        SelectDatabase(asset);
    }

    private void DeleteSelectedDatabase()
    {
        if (selectedDatabase == null)
            return;

        string path = AssetDatabase.GetAssetPath(selectedDatabase);
        if (string.IsNullOrEmpty(path))
            return;

        bool ok = EditorUtility.DisplayDialog("删除战斗参数库", "确定删除当前战斗参数库吗？", "删除", "取消");
        if (!ok)
            return;

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        selectedDatabase = null;
        selectedSO = null;
        Refresh();
    }

    private void SelectDatabase(BattleParameterDatabase db)
    {
        selectedDatabase = db;
        selectedSO = db != null ? new SerializedObject(db) : null;
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

    private Texture2D LoadOptionalIcon(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return null;

        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private GUIStyle GetCenteredToolbarTextStyle()
    {
        return new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11,
            normal = { textColor = new Color(0.92f, 0.92f, 0.94f) }
        };
    }

    public void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    public void DrawSimpleDivider()
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 10000f, 1f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.08f));
        GUILayout.Space(2f);
    }

    public void DrawPropertyRow(string label, SerializedProperty property, bool multiline = false)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(96f));

        if (property == null)
        {
            EditorGUILayout.LabelField("字段不存在");
        }
        else if (multiline && property.propertyType == SerializedPropertyType.String)
        {
            property.stringValue = EditorGUILayout.TextArea(property.stringValue, GUILayout.MinHeight(42f));
        }
        else
        {
            EditorGUILayout.PropertyField(property, GUIContent.none, true);
        }

        EditorGUILayout.EndHorizontal();
    }

    public void DrawReadonlyPropertyRow(string label, SerializedProperty property)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(96f));

        using (new EditorGUI.DisabledScope(true))
        {
            if (property == null)
                EditorGUILayout.LabelField("字段不存在");
            else
                EditorGUILayout.PropertyField(property, GUIContent.none, true);
        }

        EditorGUILayout.EndHorizontal();
    }

    public void DrawReadonlyRow(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(96f));
        EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(value) ? "-" : value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }
}
