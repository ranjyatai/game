using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonItemDefinitionPage : SkyPrisonEditorPageBase
{
    public const string ItemRootFolder = "Assets/_Project/Data/Definitions";
    public const string DefaultItemCreateFolder = "Assets/_Project/Data/Definitions/Custom/Items";

    private readonly Color leftTopBg = new Color(0.13f, 0.13f, 0.14f, 1f);
    private readonly Color accentYellow = new Color(0.95f, 0.78f, 0.22f, 1f);
    private readonly Color selectedRowYellow = new Color(0.72f, 0.56f, 0.12f, 0.25f);
    private readonly Color selectedFolderYellow = new Color(0.64f, 0.50f, 0.12f, 0.18f);

    private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>
    {
        { "基础信息", true },
        { "多语言名称", true },
        { "多语言描述", true },
        { "使用规则", true },
        { "分类与价值", true },
        { "价格与货币", true }
    };

    private readonly SkyPrisonItemDefinitionAssetListPanel assetListPanel;
    private readonly SkyPrisonItemDefinitionInspectorPanel inspectorPanel;

    private List<ItemDefinition> itemDefinitions = new List<ItemDefinition>();
    private ItemDefinition selectedItemDefinition;
    private SerializedObject selectedItemSO;

    public SkyPrisonItemDefinitionPage(SkyPrisonEditorContext context) : base(context)
    {
        assetListPanel = new SkyPrisonItemDefinitionAssetListPanel(this);
        inspectorPanel = new SkyPrisonItemDefinitionInspectorPanel(this);
    }

    public override string TabName => "物品库";

    public Color AccentYellow => accentYellow;
    public Color SelectedRowYellow => selectedRowYellow;
    public Color SelectedFolderYellow => selectedFolderYellow;
    public Color LeftTopBg => leftTopBg;

    public Dictionary<string, bool> Foldouts => foldouts;
    public List<ItemDefinition> ItemDefinitions => itemDefinitions;
    public ItemDefinition SelectedItemDefinition => selectedItemDefinition;
    public SerializedObject SelectedItemSO => selectedItemSO;

    public override void OnEnable()
    {
        Refresh();
    }

    public override void Refresh()
    {
        string selectedPath = selectedItemDefinition != null ? AssetDatabase.GetAssetPath(selectedItemDefinition) : "";

        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
        itemDefinitions = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(x => x != null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
            .ThenBy(x => x.name)
            .ToList();

        if (!string.IsNullOrEmpty(selectedPath))
        {
            ItemDefinition matched = itemDefinitions.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == selectedPath);
            if (matched != null)
                selectedItemDefinition = matched;
        }

        if (selectedItemDefinition == null && itemDefinitions.Count > 0)
            SelectItem(itemDefinitions[0]);

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

        if (ctrlOrCmd && e.keyCode == KeyCode.C && selectedItemDefinition != null)
        {
            assetListPanel.CopyItem(selectedItemDefinition);
            e.Use();
        }
        else if (ctrlOrCmd && e.keyCode == KeyCode.X && selectedItemDefinition != null)
        {
            assetListPanel.CutItem(selectedItemDefinition);
            e.Use();
        }
        else if (ctrlOrCmd && e.keyCode == KeyCode.V)
        {
            if (assetListPanel.TryPasteClipboardToCurrentFolder())
                e.Use();
        }
        else if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selectedItemDefinition != null)
        {
            if (assetListPanel.DeleteItemDefinition(selectedItemDefinition))
                e.Use();
        }
    }

    public override void OnGUILeft()
    {
        assetListPanel.Draw();
    }

    public override void OnGUIRight()
    {
        if (selectedItemDefinition == null)
        {
            EditorGUILayout.HelpBox("请先在左侧选择一个物品定义。", MessageType.Info);
            return;
        }

        EnsureSelectedSerializedObject();
        selectedItemSO.Update();

        inspectorPanel.Draw();

        selectedItemSO.ApplyModifiedProperties();
        NormalizeSelectedItemKeys();

        if (GUI.changed)
            EditorUtility.SetDirty(selectedItemDefinition);
    }

    public override void HandlePostGUI()
    {
        assetListPanel.HandlePostGUI();
    }

    public void SelectItem(ItemDefinition item)
    {
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;

        if (selectedItemSO != null)
        {
            selectedItemSO.ApplyModifiedProperties();
            selectedItemSO = null;
        }

        selectedItemDefinition = item;
        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    public void ClearSelectedItemAndSO()
    {
        GUI.FocusControl(null);
        EditorGUIUtility.editingTextField = false;

        if (selectedItemSO != null)
        {
            selectedItemSO.ApplyModifiedProperties();
            selectedItemSO = null;
        }

        selectedItemDefinition = null;
        Context.RightScroll = Vector2.zero;
        Context.Repaint();
    }

    public void EnsureSelectedSerializedObject()
    {
        if (selectedItemDefinition == null)
            return;

        if (selectedItemSO == null || selectedItemSO.targetObject != selectedItemDefinition)
        {
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;
            selectedItemSO = new SerializedObject(selectedItemDefinition);
        }
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

    public string SanitizeKey(string value)
    {
        string s = (value ?? "").Trim().ToLower().Replace(" ", "_");
        return new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
    }

    public string GenerateUniqueItemKey(string baseKey)
    {
        string safeBase = SanitizeKey(string.IsNullOrWhiteSpace(baseKey) ? "item" : baseKey);
        if (string.IsNullOrWhiteSpace(safeBase))
            safeBase = "item";

        HashSet<string> existing = new HashSet<string>(
            itemDefinitions.Where(x => x != null && !string.IsNullOrWhiteSpace(x.itemKey)).Select(x => x.itemKey)
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

    public int GenerateUniqueItemId(int fallback = 10000)
    {
        HashSet<int> existing = new HashSet<int>(
            itemDefinitions.Where(x => x != null).Select(x => x.itemId)
        );

        int id = Mathf.Max(1, fallback);
        while (existing.Contains(id))
            id += 10000;

        return id;
    }

    public void NormalizeSelectedItemKeys()
    {
        if (selectedItemDefinition == null)
            return;

        string oldItemKey = selectedItemDefinition.itemKey;
        string sanitizedItemKey = SanitizeKey(oldItemKey);

        if (string.IsNullOrWhiteSpace(sanitizedItemKey))
            sanitizedItemKey = GenerateUniqueItemKey("item");

        if (sanitizedItemKey != oldItemKey)
        {
            selectedItemDefinition.itemKey = sanitizedItemKey;
            EditorUtility.SetDirty(selectedItemDefinition);
        }

        if (string.IsNullOrWhiteSpace(selectedItemDefinition.nameKey))
            selectedItemDefinition.nameKey = $"item_name_{selectedItemDefinition.itemKey}";

        if (string.IsNullOrWhiteSpace(selectedItemDefinition.descKey))
            selectedItemDefinition.descKey = $"item_desc_{selectedItemDefinition.itemKey}";

        if (string.IsNullOrWhiteSpace(selectedItemDefinition.iconKey))
            selectedItemDefinition.iconKey = $"icon_{selectedItemDefinition.itemKey}";
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
