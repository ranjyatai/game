using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 变量表——之前挂成大工具窗口里的一个主Tab，但变量表体量太小(就是个名字+类型
/// 的列表)，跟单位定义/触发器这些真正的大系统并排占一个Tab位置不合适，改成
/// WC3 WE那种独立小弹窗，从"打开变量表"按钮弹出来，不占主窗口的Tab栏。
/// </summary>
public class SkyPrisonVariableTableWindow : EditorWindow
{
    private const string DatabaseFolder = "Assets/_Project/Data/Resources";
    private const string DatabasePath = DatabaseFolder + "/VariableDatabase.asset";

    private VariableDatabase database;
    private SerializedObject serializedDatabase;
    private SerializedProperty variablesProperty;
    private int selectedIndex = -1;
    private readonly HashSet<int> multiSelectedIndices = new HashSet<int>();
    private Vector2 leftScroll;
    private string search = "";

    [MenuItem("Tools/Sky Prison/变量表")]
    public static void Open()
    {
        SkyPrisonVariableTableWindow window = GetWindow<SkyPrisonVariableTableWindow>(true, "变量表", true);
        window.minSize = new Vector2(480f, 360f);
        window.LoadDatabase();
        window.Show();
    }

    private void OnEnable()
    {
        LoadDatabase();
    }

    private void LoadDatabase()
    {
        database = LoadOrCreateDatabase();
        serializedDatabase = database != null ? new SerializedObject(database) : null;
        variablesProperty = serializedDatabase?.FindProperty("variables");
    }

    public static VariableDatabase LoadOrCreateDatabase()
    {
        VariableDatabase existing = AssetDatabase.LoadAssetAtPath<VariableDatabase>(DatabasePath);
        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder(DatabaseFolder))
        {
            string[] parts = DatabaseFolder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        VariableDatabase created = ScriptableObject.CreateInstance<VariableDatabase>();
        AssetDatabase.CreateAsset(created, DatabasePath);
        AssetDatabase.SaveAssets();
        return created;
    }

    private const float ListWidth = 260f;
    private const float RowHeight = 28f;
    private static readonly Color PanelBg = new Color(0.16f, 0.16f, 0.17f, 1f);
    private static readonly Color RowBg = new Color(0.12f, 0.12f, 0.13f, 1f);
    private static readonly Color RowSelectedBg = new Color(0.20f, 0.32f, 0.48f, 1f);
    private static readonly Color RowHoverBg = new Color(1f, 1f, 1f, 0.05f);
    private static readonly Color DividerColor = new Color(0f, 0f, 0f, 0.5f);
    private const float IndexColumnWidth = 28f;

    private void OnGUI()
    {
        if (serializedDatabase == null || variablesProperty == null)
        {
            EditorGUILayout.HelpBox("变量数据库加载失败。", MessageType.Error);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        DrawList();

        // 左右分割线——之前左右两块紧挨着完全没有分隔，看起来像一整块。
        GUILayout.Box(GUIContent.none, GUILayout.Width(1f), GUILayout.ExpandHeight(true));

        DrawDetail();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawList()
    {
        serializedDatabase.Update();

        EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));
        EditorGUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("变量列表", EditorStyles.boldLabel);
        if (GUILayout.Button("+ 新建", GUILayout.Width(60f)))
        {
            variablesProperty.arraySize++;
            SerializedProperty newEntry = variablesProperty.GetArrayElementAtIndex(variablesProperty.arraySize - 1);
            newEntry.FindPropertyRelative("variableName").stringValue = $"NewVariable{variablesProperty.arraySize}";
            newEntry.FindPropertyRelative("type").enumValueIndex = 0;
            selectedIndex = variablesProperty.arraySize - 1;
        }
        EditorGUILayout.EndHorizontal();

        search = EditorGUILayout.TextField("搜索", search);

        // 手动Rect+滚动视图，不用EditorGUILayout.BeginScrollView——那个不给固定高度
        // 的话会跟着内容一直长高，永远不会真正出现滚轮，变量一多反而把详情区挤没了。
        Rect outerRect = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(outerRect, PanelBg);
        Rect viewRect = new Rect(outerRect.x + 4f, outerRect.y + 4f, outerRect.width - 8f, outerRect.height - 8f);

        List<int> visibleIndices = new List<int>();
        for (int i = 0; i < variablesProperty.arraySize; i++)
        {
            SerializedProperty entry = variablesProperty.GetArrayElementAtIndex(i);
            string displayName = entry.FindPropertyRelative("variableName").stringValue;
            if (string.IsNullOrWhiteSpace(search) ||
                (!string.IsNullOrWhiteSpace(displayName) && displayName.ToLower().Contains(search.ToLower())))
                visibleIndices.Add(i);
        }

        float contentHeight = Mathf.Max(viewRect.height, visibleIndices.Count * RowHeight + 4f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        leftScroll = GUI.BeginScrollView(viewRect, leftScroll, contentRect, false, true);
        float y = 0f;
        foreach (int i in visibleIndices)
        {
            SerializedProperty entry = variablesProperty.GetArrayElementAtIndex(i);
            SerializedProperty nameProp = entry.FindPropertyRelative("variableName");
            SerializedProperty typeProp = entry.FindPropertyRelative("type");

            string displayName = string.IsNullOrWhiteSpace(nameProp.stringValue) ? "<未命名>" : nameProp.stringValue;
            LogicSlotValueType type = (LogicSlotValueType)typeProp.enumValueIndex;

            Rect row = new Rect(0f, y, contentRect.width, RowHeight - 2f);
            Rect indexRect = new Rect(row.x, row.y, IndexColumnWidth, row.height);
            Rect nameRect = new Rect(row.x + IndexColumnWidth, row.y, row.width - IndexColumnWidth, row.height);

            bool selected = selectedIndex == i;
            bool multiSelected = multiSelectedIndices.Contains(i);
            bool hover = row.Contains(Event.current.mousePosition);

            EditorGUI.DrawRect(row, selected ? RowSelectedBg : RowBg);
            if (!selected && hover)
                EditorGUI.DrawRect(row, RowHoverBg);
            if (multiSelected)
                EditorGUI.DrawRect(new Rect(indexRect.x, indexRect.y, indexRect.width, indexRect.height), new Color(0.85f, 0.55f, 0.15f, 0.55f));

            // 编号单独一列可以点——点编号是"批量勾选"(跟名字那部分的单选互不干扰)，
            // 用来配合下面的"删除已选"做批量删除，不用一个个点开详情再单独删。
            GUI.Label(indexRect, (i + 1).ToString(), EditorStyles.centeredGreyMiniLabel);
            if (GUI.Button(indexRect, GUIContent.none, GUIStyle.none))
            {
                if (!multiSelectedIndices.Remove(i))
                    multiSelectedIndices.Add(i);
            }

            GUI.Label(new Rect(nameRect.x + 4f, nameRect.y, nameRect.width - 8f, nameRect.height),
                $"{displayName}  ({VariableDatabase.GetTypeLabel(type)})");

            if (GUI.Button(nameRect, GUIContent.none, GUIStyle.none))
                selectedIndex = i;

            y += RowHeight;
        }
        GUI.EndScrollView();

        using (new EditorGUI.DisabledScope(multiSelectedIndices.Count == 0))
        {
            if (GUILayout.Button($"删除已选({multiSelectedIndices.Count})", GUILayout.Height(22f)))
            {
                bool ok = EditorUtility.DisplayDialog("删除变量", $"确定删除选中的 {multiSelectedIndices.Count} 个变量？", "删除", "取消");
                if (ok)
                {
                    List<int> toDelete = new List<int>(multiSelectedIndices);
                    toDelete.Sort();
                    toDelete.Reverse(); // 从后往前删，不然前面删了后面的下标全部往前挪，会删错。
                    foreach (int idx in toDelete)
                        variablesProperty.DeleteArrayElementAtIndex(idx);
                    multiSelectedIndices.Clear();
                    selectedIndex = -1;
                }
            }
        }

        serializedDatabase.ApplyModifiedProperties();
        EditorGUILayout.EndVertical();
    }

    private void DrawDetail()
    {
        EditorGUILayout.BeginVertical();

        if (variablesProperty == null || selectedIndex < 0 || selectedIndex >= variablesProperty.arraySize)
        {
            EditorGUILayout.HelpBox("从左边选一个变量，或者点\"+ 新建\"新建一个。", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        serializedDatabase.Update();

        SerializedProperty entry = variablesProperty.GetArrayElementAtIndex(selectedIndex);
        SerializedProperty nameProp = entry.FindPropertyRelative("variableName");
        SerializedProperty typeProp = entry.FindPropertyRelative("type");

        EditorGUILayout.LabelField("变量详情", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(nameProp, new GUIContent("名字"));

        LogicSlotValueType currentType = (LogicSlotValueType)typeProp.enumValueIndex;
        List<LogicSlotValueType> supported = new List<LogicSlotValueType>(VariableDatabase.SupportedVariableTypes);
        string[] typeLabels = supported.ConvertAll(VariableDatabase.GetTypeLabel).ToArray();
        int currentSupportedIndex = supported.IndexOf(currentType);
        if (currentSupportedIndex < 0) currentSupportedIndex = 0;

        int newSupportedIndex = EditorGUILayout.Popup("类型", currentSupportedIndex, typeLabels);
        typeProp.enumValueIndex = (int)supported[newSupportedIndex];

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "改类型不会自动转换现有触发器/任务里已经选中这个变量的槽位——" +
            "如果有槽位类型跟改完后的变量类型对不上，那些槽位会显示为空引用，" +
            "需要手动去重新选一遍。",
            MessageType.Warning);

        EditorGUILayout.Space(12f);
        GUI.backgroundColor = new Color(0.85f, 0.22f, 0.12f, 1f);
        if (GUILayout.Button("删除这个变量", GUILayout.Height(24f)))
        {
            bool ok = EditorUtility.DisplayDialog("删除变量", $"确定删除变量：{nameProp.stringValue}？", "删除", "取消");
            if (ok)
            {
                variablesProperty.DeleteArrayElementAtIndex(selectedIndex);
                selectedIndex = -1;
            }
        }
        GUI.backgroundColor = Color.white;

        serializedDatabase.ApplyModifiedProperties();
        EditorGUILayout.EndVertical();
    }
}
