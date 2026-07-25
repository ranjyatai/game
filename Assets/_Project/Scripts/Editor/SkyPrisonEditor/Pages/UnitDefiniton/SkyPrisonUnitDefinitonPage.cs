using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonUnitDefinitionPage : SkyPrisonEditorPageBase
{
    public const string UnitRootFolder = "Assets/_Project/Data/Definitions";
    public const string DefaultUnitCreateFolder = "Assets/_Project/Data/Definitions/Custom/Units";
    public const string DefaultAICreateFolder = "Assets/_Project/Data/AI";

    private readonly Color leftTopBg = new Color(0.13f, 0.13f, 0.14f, 1f);
    private readonly Color accentBlue = new Color(0.35f, 0.55f, 0.90f, 1f);
    private readonly Color selectedRowBlue = new Color(0.20f, 0.42f, 0.78f, 0.34f);
    private readonly Color selectedFolderBlue = new Color(0.24f, 0.40f, 0.72f, 0.28f);

    private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>
    {
        { "基础信息", true },
        { "多语言名称", true },
        { "多语言描述", true },
        { "定义类型", true },
        { "控制方式", true },
        { "AI 行为", true },
        { "视觉通道 / 预制体", true },
        { "属性数值", true },
        { "遮挡与描边", false },
        { "物理规范", false },
        { "单位UI", false },
        { "单位碰撞盒", false },
        { "移动规则", false },
        { "死亡规则", true }
    };

    private readonly SkyPrisonUnitDefinitionAssetListPanel assetListPanel;
    private readonly SkyPrisonUnitDefinitionInspectorPanel inspectorPanel;

    private List<UnitDefinition> unitDefinitions = new List<UnitDefinition>();
    private UnitDefinition selectedUnitDefinition;
    private SerializedObject selectedUnitSO;

    public SkyPrisonUnitDefinitionPage(SkyPrisonEditorContext context) : base(context)
    {
        assetListPanel = new SkyPrisonUnitDefinitionAssetListPanel(this);
        inspectorPanel = new SkyPrisonUnitDefinitionInspectorPanel(this);
    }

    public override string TabName => "单位定义";

    public Color AccentBlue => accentBlue;
    public Color SelectedRowBlue => selectedRowBlue;
    public Color SelectedFolderBlue => selectedFolderBlue;
    public Color LeftTopBg => leftTopBg;

    public Dictionary<string, bool> Foldouts => foldouts;
    public List<UnitDefinition> UnitDefinitions => unitDefinitions;
    public UnitDefinition SelectedUnitDefinition => selectedUnitDefinition;
    public SerializedObject SelectedUnitSO => selectedUnitSO;

    public override void OnEnable()
    {
        Refresh();
    }

    public override void Refresh()
    {
        string selectedPath = selectedUnitDefinition != null ? AssetDatabase.GetAssetPath(selectedUnitDefinition) : "";

        string[] guids = AssetDatabase.FindAssets("t:UnitDefinition");
        unitDefinitions = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<UnitDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(x => x != null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
            .ThenBy(x => x.name)
            .ToList();

        if (!string.IsNullOrEmpty(selectedPath))
        {
            UnitDefinition matched = unitDefinitions.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == selectedPath);
            if (matched != null)
                selectedUnitDefinition = matched;
        }

        if (selectedUnitDefinition == null && unitDefinitions.Count > 0)
            SelectUnit(unitDefinitions[0]);

        assetListPanel.OnRefresh();
    }

    public override void HandleGlobalShortcuts()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown)
            return;

        if (EditorGUIUtility.editingTextField)
            return;

        string focused = GUI.GetNameOfFocusedControl();
        if (!string.IsNullOrEmpty(focused))
            return;

        bool ctrlOrCmd = e.control || e.command;

        if (ctrlOrCmd && e.keyCode == KeyCode.C && selectedUnitDefinition != null)
        {
            assetListPanel.CopyUnit(selectedUnitDefinition);
            e.Use();
        }
        else if (ctrlOrCmd && e.keyCode == KeyCode.X && selectedUnitDefinition != null)
        {
            assetListPanel.CutUnit(selectedUnitDefinition);
            e.Use();
        }
        else if (ctrlOrCmd && e.keyCode == KeyCode.V)
        {
            if (assetListPanel.TryPasteClipboardToCurrentFolder())
                e.Use();
        }
        else if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selectedUnitDefinition != null)
        {
            if (assetListPanel.DeleteUnitDefinition(selectedUnitDefinition))
                e.Use();
        }
    }

    public override void OnGUILeft()
    {
        assetListPanel.Draw();
    }

    public override void OnGUIRight()
    {
        if (selectedUnitDefinition == null)
        {
            EditorGUILayout.HelpBox("请先在左侧选择一个单位定义。", MessageType.Info);
            return;
        }

        EnsureSelectedSerializedObject();
        selectedUnitSO.Update();

        inspectorPanel.Draw();

        selectedUnitSO.ApplyModifiedProperties();
        NormalizeSelectedUnitId();

        if (GUI.changed)
            EditorUtility.SetDirty(selectedUnitDefinition);
    }

    public override void HandlePostGUI()
    {
        assetListPanel.HandlePostGUI();
    }

    public void SelectUnit(UnitDefinition unit)
    {
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;

        if (selectedUnitSO != null)
        {
            selectedUnitSO.ApplyModifiedProperties();
            selectedUnitSO = null;
        }

        selectedUnitDefinition = unit;
        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    public void ClearSelectedUnitAndSO()
    {
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;

        if (selectedUnitSO != null)
        {
            selectedUnitSO.ApplyModifiedProperties();
            selectedUnitSO = null;
        }

        selectedUnitDefinition = null;
        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    public void EnsureSelectedSerializedObject()
    {
        if (selectedUnitDefinition == null)
            return;

        if (selectedUnitSO == null || selectedUnitSO.targetObject != selectedUnitDefinition)
        {
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;
            selectedUnitSO = new SerializedObject(selectedUnitDefinition);
        }
    }

    public void DrawPingButtons(Object target)
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

    public void DrawUnitIdDuplicateWarning(UnitDefinition unit)
    {
        if (unit == null || string.IsNullOrWhiteSpace(unit.unitId))
            return;

        int duplicateCount = unitDefinitions.Count(x => x != null && x.unitId == unit.unitId);
        if (duplicateCount > 1)
        {
            EditorGUILayout.HelpBox(
                $"警告：当前 unitId \"{unit.unitId}\" 与其他单位重复。Key 必须唯一，请立即修改。",
                MessageType.Error
            );
        }
    }

    public void DrawFoldoutSection(string title, System.Action drawer)
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

    public void DrawReadonlyRow(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel(
            string.IsNullOrWhiteSpace(value) ? "-" : value,
            GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    public void DrawRow(string label, SerializedProperty property, bool multiline = false)
    {
        if (property == null)
        {
            EditorGUILayout.HelpBox($"字段 {label} 不存在。", MessageType.Warning);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));

        if (multiline && property.propertyType == SerializedPropertyType.String)
            property.stringValue = EditorGUILayout.TextArea(property.stringValue, GUILayout.MinHeight(54f));
        else
            EditorGUILayout.PropertyField(property, GUIContent.none, true);

        EditorGUILayout.EndHorizontal();
    }

    public void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    public GUIStyle GetCenteredToolbarTextStyle()
    {
        return new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11,
            normal = { textColor = new Color(0.92f, 0.92f, 0.94f) }
        };
    }

    public string SanitizeId(string value)
    {
        string s = (value ?? "").Trim().ToLower().Replace(" ", "_");
        return new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
    }

    public string GenerateUniqueUnitId(string baseId)
    {
        string safeBase = SanitizeId(string.IsNullOrWhiteSpace(baseId) ? "unit" : baseId);
        if (string.IsNullOrWhiteSpace(safeBase))
            safeBase = "unit";

        HashSet<string> existing = new HashSet<string>(
            unitDefinitions.Where(x => x != null && !string.IsNullOrWhiteSpace(x.unitId)).Select(x => x.unitId)
        );

        if (!existing.Contains(safeBase))
            return safeBase;

        int index = 1;
        while (true)
        {
            string candidate = $"{safeBase}_{index:000}";
            if (!existing.Contains(candidate))
                return candidate;
            index++;
        }
    }

    public string GenerateUniqueAIIdForUnitPage(string baseId)
    {
        string safeBase = SanitizeId(string.IsNullOrWhiteSpace(baseId) ? "ai" : baseId);
        if (string.IsNullOrWhiteSpace(safeBase))
            safeBase = "ai";

        string[] guids = AssetDatabase.FindAssets("t:AIBehaviorPackage");
        HashSet<string> existing = new HashSet<string>();

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AIBehaviorPackage pkg = AssetDatabase.LoadAssetAtPath<AIBehaviorPackage>(path);
            if (pkg != null && !string.IsNullOrWhiteSpace(pkg.aiId))
                existing.Add(pkg.aiId);
        }

        if (!existing.Contains(safeBase))
            return safeBase;

        int index = 1;
        while (true)
        {
            string candidate = $"{safeBase}_{index:000}";
            if (!existing.Contains(candidate))
                return candidate;
            index++;
        }
    }

    public void NormalizeSelectedUnitId()
    {
        if (selectedUnitDefinition == null)
            return;

        string oldId = selectedUnitDefinition.unitId;
        string sanitized = SanitizeId(oldId);

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = GenerateUniqueUnitId("unit");

        if (sanitized != oldId)
        {
            selectedUnitDefinition.unitId = sanitized;
            EditorUtility.SetDirty(selectedUnitDefinition);
        }
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
