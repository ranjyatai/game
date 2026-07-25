using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonTechTreePage : SkyPrisonEditorPageBase
{
    private const string DefaultGraphFolder = "Assets/_Project/Data/Database/TechTree";

    private const float MinInspectorWidth = 430f;
    private const float MaxInspectorWidth = 680f;
    private const float InspectorSplitterWidth = 4f;

    private const float LeftTitleRowHeight = 22f;
    private const float LeftToolbarRowHeight = 24f;
    private const float LeftSearchRowHeight = 22f;
    private const float LeftRowGap = 6f;
    private const float LeftContainerPadding = 8f;
    private const float LeftListRowHeight = 24f;

    private const string UndoIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_16.png";
    private const string RedoIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_17.png";
    private const string RefreshIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_18.png";

    private readonly Color leftTopBg = new Color(0.13f, 0.13f, 0.14f, 1f);
    private readonly Color accentBlue = new Color(0.18f, 0.72f, 0.78f, 1f);

    private string search = "";
    private List<TechTreeGraphAsset> graphs = new List<TechTreeGraphAsset>();
    private TechTreeGraphAsset selectedGraph;
    private SerializedObject selectedSO;

    private int selectedNodeIndex = -1;
    private readonly HashSet<int> selectedNodeIndices = new HashSet<int>();
    private Vector2 nodeInspectorScroll;
    private Vector2 graphListScroll;

    private float inspectorWidth = 460f;
    private bool draggingInspectorSplitter = false;

    private bool hasPendingDeleteNode = false;
    private int pendingDeleteNodeIndex = -1;

    private bool hasPendingBatchDelete = false;
    private List<int> pendingBatchDeleteIndices = new List<int>();

    private bool hasLocalUndo = false;
    private bool hasLocalRedo = false;

    private bool batchEnabledValue = true;
    private int batchMaxLevelValue = 1;
    private bool batchUseCustomColorValue = false;
    private Color batchCustomColorValue = new Color(0.48f, 0.76f, 1f, 1f);
    private string batchDesignerNoteValue = "";

    private readonly SkyPrisonTechTreeCanvasPanel canvasPanel = new SkyPrisonTechTreeCanvasPanel();

    public SkyPrisonTechTreePage(SkyPrisonEditorContext context) : base(context) { }

    public override string TabName => "科技树";

    public override void OnEnable()
    {
        Refresh();
    }

    public override void Refresh()
    {
        string selectedPath = selectedGraph != null ? AssetDatabase.GetAssetPath(selectedGraph) : "";

        string[] guids = AssetDatabase.FindAssets("t:TechTreeGraphAsset");
        graphs = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<TechTreeGraphAsset>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(x => x != null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
            .ThenBy(x => x.name)
            .ToList();

        if (!string.IsNullOrEmpty(selectedPath))
        {
            TechTreeGraphAsset matched = graphs.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == selectedPath);
            if (matched != null)
                selectedGraph = matched;
        }

        if (selectedGraph == null && graphs.Count > 0)
            selectedGraph = graphs[0];
    }

    public override void OnGUILeft()
    {
        Rect fullRect = GUILayoutUtility.GetRect(
            0f, 100000f, 0f, 100000f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        float y = fullRect.y;

        Rect titleRect = new Rect(fullRect.x, y, fullRect.width, LeftTitleRowHeight);
        y += LeftTitleRowHeight + LeftRowGap;

        Rect toolbarRect = new Rect(fullRect.x, y, fullRect.width, LeftToolbarRowHeight);
        y += LeftToolbarRowHeight + LeftRowGap;

        Rect searchRect = new Rect(fullRect.x, y, fullRect.width, LeftSearchRowHeight);
        y += LeftSearchRowHeight + LeftRowGap;

        Rect containerRect = new Rect(
            fullRect.x,
            y,
            fullRect.width,
            Mathf.Max(40f, fullRect.yMax - y));

        DrawLeftLabelRow(titleRect, "科技树资源列表");
        DrawGraphToolbarRow(toolbarRect);
        DrawGraphSearchRow(searchRect);
        DrawGraphContainer(containerRect);
    }

    public override void OnGUIRight()
    {
        if (selectedGraph == null)
        {
            EditorGUILayout.HelpBox("请先在左侧选择一个科技树资源。", MessageType.Info);
            return;
        }

        if (selectedSO == null || selectedSO.targetObject != selectedGraph)
            selectedSO = new SerializedObject(selectedGraph);

        selectedSO.Update();

        DrawGraphHeader();
        GUILayout.Space(6f);
        DrawTopActions();
        GUILayout.Space(6f);

        Rect contentRect = GUILayoutUtility.GetRect(
            0f, 100000f, 0f, 100000f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true)
        );

        HandleInspectorSplitterEvents(contentRect);

        float currentInspectorWidth = Mathf.Clamp(inspectorWidth, MinInspectorWidth, MaxInspectorWidth);
        float canvasWidth = Mathf.Max(260f, contentRect.width - currentInspectorWidth - InspectorSplitterWidth - 8f);

        Rect canvasRect = new Rect(contentRect.x, contentRect.y, canvasWidth, contentRect.height);
        Rect splitterRect = new Rect(canvasRect.xMax, contentRect.y, InspectorSplitterWidth, contentRect.height);
        Rect inspectorRect = new Rect(splitterRect.xMax + 8f, contentRect.y, currentInspectorWidth, contentRect.height);

        canvasPanel.Draw(
            canvasRect,
            selectedSO,
            selectedNodeIndex,
            selectedNodeIndices,
            index => SelectSingleNode(index),
            index => AddMultiSelection(index),
            parentIndex =>
            {
                if (parentIndex < 0)
                    AddRootNode();
                else
                    AddChildNodeKeepParentSelected(parentIndex);
            },
            nodeIndex =>
            {
                hasPendingDeleteNode = true;
                pendingDeleteNodeIndex = nodeIndex;
            },
            () => ScheduleDeleteSelectedNodes()
        );

        DrawInspectorSplitter(splitterRect);
        DrawInspectorArea(inspectorRect);

        if (hasPendingBatchDelete)
        {
            hasPendingBatchDelete = false;
            DeleteNodes(pendingBatchDeleteIndices);
            pendingBatchDeleteIndices.Clear();
            GUIUtility.ExitGUI();
        }

        if (hasPendingDeleteNode)
        {
            hasPendingDeleteNode = false;

            if (pendingDeleteNodeIndex >= 0)
                DeleteNode(pendingDeleteNodeIndex);

            pendingDeleteNodeIndex = -1;
            GUIUtility.ExitGUI();
        }

        selectedSO.ApplyModifiedProperties();

        if (GUI.changed)
            EditorUtility.SetDirty(selectedGraph);
    }

    private void DrawLeftLabelRow(Rect rect, string label)
    {
        GUI.Label(rect, label, EditorStyles.boldLabel);
    }

    private void DrawGraphToolbarRow(Rect rect)
    {
        Texture2D refreshIcon = LoadOptionalIcon(RefreshIconPath);

        const float buttonSize = 20f;
        const float gap = 4f;

        float y = rect.y + (rect.height - buttonSize) * 0.5f;
        float right = rect.xMax;

        Rect refreshRect = new Rect(right - buttonSize, y, buttonSize, buttonSize);
        Rect minusRect = new Rect(refreshRect.x - gap - buttonSize, y, buttonSize, buttonSize);
        Rect plusRect = new Rect(minusRect.x - gap - buttonSize, y, buttonSize, buttonSize);

        if (DrawToolButton(plusRect, "+", "新建科技树"))
            CreateNewGraph();

        using (new EditorGUI.DisabledScope(selectedGraph == null))
        {
            if (DrawToolButton(minusRect, "-", "删除当前科技树"))
                DeleteSelectedGraph();
        }

        if (DrawToolButton(refreshRect, refreshIcon, "刷新"))
            Refresh();
    }

    private void DrawGraphSearchRow(Rect rect)
    {
        string newSearch = EditorGUI.TextField(rect, search);
        if (newSearch != search)
        {
            search = newSearch;
            graphListScroll = Vector2.zero;
        }
    }

    private void DrawGraphContainer(Rect rect)
    {
        EditorGUI.DrawRect(rect, leftTopBg);
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.06f));

        Rect viewRect = new Rect(
            rect.x + LeftContainerPadding,
            rect.y + LeftContainerPadding,
            rect.width - LeftContainerPadding * 2f,
            rect.height - LeftContainerPadding * 2f);

        List<TechTreeGraphAsset> filtered = GetFilteredGraphs();

        float contentHeight = Mathf.Max(viewRect.height, filtered.Count * LeftListRowHeight);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        graphListScroll = GUI.BeginScrollView(viewRect, graphListScroll, contentRect, false, true);

        for (int i = 0; i < filtered.Count; i++)
        {
            TechTreeGraphAsset graph = filtered[i];
            Rect rowRect = new Rect(0f, i * LeftListRowHeight, contentRect.width, LeftListRowHeight);
            bool active = selectedGraph == graph;

            string label = string.IsNullOrWhiteSpace(graph.displayName) ? graph.name : graph.displayName;
            DrawFlatSelectableRow(rowRect, active, label, accentBlue, () => SelectGraph(graph));
        }

        if (filtered.Count == 0)
        {
            GUI.Label(new Rect(4f, 2f, contentRect.width - 8f, 22f), "没有匹配的科技树", EditorStyles.miniLabel);
        }

        GUI.EndScrollView();
    }

    private List<TechTreeGraphAsset> GetFilteredGraphs()
    {
        IEnumerable<TechTreeGraphAsset> filtered = graphs;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string keyword = search.Trim().ToLowerInvariant();
            filtered = filtered.Where(x =>
                x != null &&
                (
                    (!string.IsNullOrEmpty(x.displayName) && x.displayName.ToLowerInvariant().Contains(keyword)) ||
                    (!string.IsNullOrEmpty(x.graphId) && x.graphId.ToLowerInvariant().Contains(keyword)) ||
                    x.name.ToLowerInvariant().Contains(keyword)
                ));
        }

        return filtered.ToList();
    }

    private void DrawGraphHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(
            string.IsNullOrWhiteSpace(selectedGraph.displayName) ? selectedGraph.name : selectedGraph.displayName,
            EditorStyles.boldLabel);
        EditorGUILayout.LabelField("科技图资源", EditorStyles.miniLabel);
        EditorGUILayout.Space(2f);

        DrawReadonlyRow("资源路径", AssetDatabase.GetAssetPath(selectedGraph));

        SerializedProperty graphIdProp = selectedSO.FindProperty("graphId");
        SerializedProperty displayNameProp = selectedSO.FindProperty("displayName");
        SerializedProperty noteProp = selectedSO.FindProperty("note");

        DrawPropertyRow("科技图 ID", graphIdProp);
        DrawPropertyRow("显示名称", displayNameProp);
        DrawPropertyRow("备注", noteProp, true);

        EditorGUILayout.EndVertical();
    }

    private void DrawTopActions()
    {
        SerializedProperty layoutProp = selectedSO.FindProperty("layoutMode");
        int selectedCount = selectedNodeIndices.Count;

        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("布局模式", GUILayout.Width(70f));

        DrawLayoutButton(layoutProp, TechTreeGraphAsset.LayoutMode.Vertical, "纵向");
        DrawLayoutButton(layoutProp, TechTreeGraphAsset.LayoutMode.Horizontal, "横向");
        DrawLayoutButton(layoutProp, TechTreeGraphAsset.LayoutMode.RadialOutward, "圆形外扩");
        DrawLayoutButton(layoutProp, TechTreeGraphAsset.LayoutMode.RadialInward, "圆形内收");

        GUILayout.Space(12f);
        canvasPanel.DrawSimulationToolbar();

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("添加根节点", GUILayout.Height(24f)))
            AddRootNode();

        using (new EditorGUI.DisabledScope(selectedCount <= 0))
        {
            if (GUILayout.Button("添加子节点", GUILayout.Height(24f)))
                AddChildNodesKeepParentsSelected(selectedNodeIndices);

            if (GUILayout.Button(selectedCount > 1 ? "删除选中节点" : "删除当前节点", GUILayout.Height(24f)))
                ScheduleDeleteSelectedNodes();
        }

        if (GUILayout.Button("同步等级配置", GUILayout.Height(24f)))
            SyncAllNodeLevels();

        using (new EditorGUI.DisabledScope(selectedSO == null || selectedSO.FindProperty("nodes") == null || selectedSO.FindProperty("nodes").arraySize == 0))
        {
            if (GUILayout.Button("清除全部节点", GUILayout.Height(24f)))
                ClearAllNodesWithConfirm();
        }

        Texture2D undoIcon = LoadOptionalIcon(UndoIconPath);
        Texture2D redoIcon = LoadOptionalIcon(RedoIconPath);

        using (new EditorGUI.DisabledScope(!hasLocalUndo))
        {
            if (GUILayout.Button(
                BuildToolbarContent(undoIcon, "撤销", "撤销"),
                GUILayout.Height(24f),
                GUILayout.Width(60f)))
            {
                Undo.PerformUndo();
                hasLocalUndo = false;
                hasLocalRedo = true;
                GUI.changed = true;
            }
        }

        using (new EditorGUI.DisabledScope(!hasLocalRedo))
        {
            if (GUILayout.Button(
                BuildToolbarContent(redoIcon, "重做", "重做"),
                GUILayout.Height(24f),
                GUILayout.Width(60f)))
            {
                Undo.PerformRedo();
                hasLocalUndo = true;
                hasLocalRedo = false;
                GUI.changed = true;
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawLayoutButton(SerializedProperty layoutProp, TechTreeGraphAsset.LayoutMode mode, string label)
    {
        bool selected = layoutProp.enumValueIndex == (int)mode;
        if (GUILayout.Toggle(selected, label, EditorStyles.toolbarButton, GUILayout.Height(24f), GUILayout.Width(90f)))
        {
            if (!selected)
            {
                Undo.RecordObject(selectedGraph, "Change Tech Tree Layout");
                hasLocalUndo = true;
                hasLocalRedo = false;

                layoutProp.enumValueIndex = (int)mode;
                EditorUtility.SetDirty(selectedGraph);
                canvasPanel.RequestFocus();
            }
        }
    }

    private void DrawInspectorArea(Rect rect)
    {
        GUI.Box(rect, GUIContent.none);

        GUI.BeginGroup(rect);

        Rect viewRect = new Rect(8f, 8f, rect.width - 16f, rect.height - 16f);
        Rect contentRect = new Rect(0f, 0f, viewRect.width - 16f, Mathf.Max(viewRect.height, 1400f));

        nodeInspectorScroll = GUI.BeginScrollView(viewRect, nodeInspectorScroll, contentRect, false, true);

        GUILayout.BeginArea(new Rect(0f, 0f, contentRect.width, contentRect.height));

        if (selectedNodeIndices.Count > 1)
            DrawMultiSelectionInspector();
        else
            SkyPrisonTechTreeInspectorPanel.Draw(
                selectedGraph,
                selectedSO,
                selectedNodeIndex,
                Vector2.zero,
                _ => { }
            );

        GUILayout.EndArea();

        GUI.EndScrollView();
        GUI.EndGroup();
    }

    private void DrawMultiSelectionInspector()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField($"已选中 {selectedNodeIndices.Count} 个节点", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            selectedNodeIndex >= 0 ? $"主选中节点 Index: {selectedNodeIndex}" : "主选中节点: -",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("批量添加子节点", GUILayout.Height(22f)))
            AddChildNodesKeepParentsSelected(selectedNodeIndices);

        if (GUILayout.Button("批量删除", GUILayout.Height(22f)))
            ScheduleDeleteSelectedNodes();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        GUILayout.Space(8f);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("批量编辑", EditorStyles.miniBoldLabel);

        GUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("启用状态", GUILayout.Width(88f));
        batchEnabledValue = EditorGUILayout.Toggle(batchEnabledValue);
        if (GUILayout.Button("应用", GUILayout.Width(60f)))
            ApplyBatchEnabled(batchEnabledValue);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("最大等级", GUILayout.Width(88f));
        batchMaxLevelValue = EditorGUILayout.IntField(batchMaxLevelValue);
        batchMaxLevelValue = Mathf.Max(1, batchMaxLevelValue);
        if (GUILayout.Button("应用", GUILayout.Width(60f)))
            ApplyBatchMaxLevel(batchMaxLevelValue);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("启用染色", GUILayout.Width(88f));
        batchUseCustomColorValue = EditorGUILayout.Toggle(batchUseCustomColorValue);
        if (GUILayout.Button("应用", GUILayout.Width(60f)))
            ApplyBatchUseCustomColor(batchUseCustomColorValue);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("节点色条", GUILayout.Width(88f));
        batchCustomColorValue = EditorGUILayout.ColorField(batchCustomColorValue);
        if (GUILayout.Button("应用", GUILayout.Width(60f)))
            ApplyBatchCustomColor(batchCustomColorValue);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("设计备注（覆盖）", EditorStyles.miniLabel);
        batchDesignerNoteValue = EditorGUILayout.TextArea(batchDesignerNoteValue, GUILayout.MinHeight(64f));

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("覆盖到全部选中节点", GUILayout.Width(140f)))
            ApplyBatchDesignerNote(batchDesignerNoteValue);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        GUILayout.Space(8f);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("选中节点列表", EditorStyles.miniBoldLabel);

        foreach (int index in selectedNodeIndices.OrderBy(x => x))
        {
            SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
            if (nodesProp == null || index < 0 || index >= nodesProp.arraySize)
                continue;

            SerializedProperty node = nodesProp.GetArrayElementAtIndex(index);
            string nodeName = node.FindPropertyRelative("nodeName").stringValue;
            string nodeId = node.FindPropertyRelative("nodeId").stringValue;

            if (string.IsNullOrWhiteSpace(nodeName))
                nodeName = "未命名节点";

            EditorGUILayout.LabelField($"{nodeName} ({nodeId})", EditorStyles.miniLabel);
        }

        EditorGUILayout.EndVertical();
    }

    private void ApplyBatchEnabled(bool value)
    {
        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        if (nodesProp == null || selectedNodeIndices.Count == 0)
            return;

        Undo.RecordObject(selectedGraph, "Batch Set Node Enabled");
        hasLocalUndo = true;
        hasLocalRedo = false;

        foreach (int index in selectedNodeIndices)
        {
            if (index < 0 || index >= nodesProp.arraySize)
                continue;

            SerializedProperty node = nodesProp.GetArrayElementAtIndex(index);
            node.FindPropertyRelative("enabled").boolValue = value;
        }

        selectedSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedGraph);
    }

    private void ApplyBatchMaxLevel(int value)
    {
        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        if (nodesProp == null || selectedNodeIndices.Count == 0)
            return;

        Undo.RecordObject(selectedGraph, "Batch Set Node Max Level");
        hasLocalUndo = true;
        hasLocalRedo = false;

        int maxLv = Mathf.Max(1, value);

        foreach (int index in selectedNodeIndices)
        {
            if (index < 0 || index >= nodesProp.arraySize)
                continue;

            SerializedProperty node = nodesProp.GetArrayElementAtIndex(index);
            node.FindPropertyRelative("maxLevel").intValue = maxLv;
            SyncLevels(node.FindPropertyRelative("levels"), maxLv);
        }

        selectedSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedGraph);
    }

    private void ApplyBatchUseCustomColor(bool value)
    {
        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        if (nodesProp == null || selectedNodeIndices.Count == 0)
            return;

        Undo.RecordObject(selectedGraph, "Batch Set Node Use Custom Color");
        hasLocalUndo = true;
        hasLocalRedo = false;

        foreach (int index in selectedNodeIndices)
        {
            if (index < 0 || index >= nodesProp.arraySize)
                continue;

            SerializedProperty node = nodesProp.GetArrayElementAtIndex(index);
            node.FindPropertyRelative("useCustomColor").boolValue = value;
        }

        selectedSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedGraph);
    }

    private void ApplyBatchCustomColor(Color value)
    {
        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        if (nodesProp == null || selectedNodeIndices.Count == 0)
            return;

        Undo.RecordObject(selectedGraph, "Batch Set Node Custom Color");
        hasLocalUndo = true;
        hasLocalRedo = false;

        foreach (int index in selectedNodeIndices)
        {
            if (index < 0 || index >= nodesProp.arraySize)
                continue;

            SerializedProperty node = nodesProp.GetArrayElementAtIndex(index);
            node.FindPropertyRelative("customColor").colorValue = value;
            node.FindPropertyRelative("useCustomColor").boolValue = true;
        }

        selectedSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedGraph);
    }

    private void ApplyBatchDesignerNote(string value)
    {
        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        if (nodesProp == null || selectedNodeIndices.Count == 0)
            return;

        Undo.RecordObject(selectedGraph, "Batch Set Node Designer Note");
        hasLocalUndo = true;
        hasLocalRedo = false;

        foreach (int index in selectedNodeIndices)
        {
            if (index < 0 || index >= nodesProp.arraySize)
                continue;

            SerializedProperty node = nodesProp.GetArrayElementAtIndex(index);
            node.FindPropertyRelative("designerNote").stringValue = value ?? "";
        }

        selectedSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedGraph);
    }

    private void HandleInspectorSplitterEvents(Rect totalRect)
    {
        float splitX = totalRect.xMax - inspectorWidth - InspectorSplitterWidth - 8f;
        Rect splitterRect = new Rect(splitX, totalRect.y, InspectorSplitterWidth, totalRect.height);

        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseDown && splitterRect.Contains(e.mousePosition))
        {
            draggingInspectorSplitter = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && draggingInspectorSplitter)
        {
            inspectorWidth = Mathf.Clamp(totalRect.xMax - e.mousePosition.x - 8f, MinInspectorWidth, MaxInspectorWidth);
            GUI.changed = true;
            e.Use();
        }
        else if (e.type == EventType.MouseUp && draggingInspectorSplitter)
        {
            draggingInspectorSplitter = false;
            e.Use();
        }
    }

    private void DrawInspectorSplitter(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
    }

    private void AddRootNode()
    {
        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        Undo.RecordObject(selectedGraph, "Add Tech Tree Root Node");
        hasLocalUndo = true;
        hasLocalRedo = false;

        int newIndex = nodesProp.arraySize;
        nodesProp.arraySize++;

        SerializedProperty node = nodesProp.GetArrayElementAtIndex(newIndex);
        InitializeNode(node, newIndex);
        node.FindPropertyRelative("primaryParentIndex").intValue = -1;

        SelectSingleNode(newIndex);
        canvasPanel.RequestFocus();

        selectedSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedGraph);
    }

    private void AddChildNodeKeepParentSelected(int parentIndex)
    {
        AddChildNodesKeepParentsSelected(new[] { parentIndex });
    }

    private void AddChildNodesKeepParentsSelected(IEnumerable<int> parentIndices)
    {
        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        if (nodesProp == null)
            return;

        List<int> parents = parentIndices
            .Where(i => i >= 0 && i < nodesProp.arraySize)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (parents.Count == 0)
            return;

        Undo.RecordObject(selectedGraph, "Batch Add Tech Tree Child Nodes");
        hasLocalUndo = true;
        hasLocalRedo = false;

        foreach (int parentIndex in parents)
        {
            int newIndex = nodesProp.arraySize;
            nodesProp.arraySize++;

            SerializedProperty node = nodesProp.GetArrayElementAtIndex(newIndex);
            InitializeNode(node, newIndex);
            node.FindPropertyRelative("primaryParentIndex").intValue = parentIndex;
        }

        selectedSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedGraph);

        selectedNodeIndices.Clear();
        foreach (int parent in parents)
            selectedNodeIndices.Add(parent);

        selectedNodeIndex = parents[0];
        canvasPanel.RequestFocus();
    }

    private void InitializeNode(SerializedProperty node, int index)
    {
        node.FindPropertyRelative("nodeId").stringValue = "node_" + (index + 1).ToString("000");
        node.FindPropertyRelative("nodeName").stringValue = "新节点";
        node.FindPropertyRelative("description").stringValue = "";
        node.FindPropertyRelative("enabled").boolValue = true;
        node.FindPropertyRelative("maxLevel").intValue = 1;
        node.FindPropertyRelative("designerNote").stringValue = "";
        node.FindPropertyRelative("useCustomColor").boolValue = false;
        node.FindPropertyRelative("customColor").colorValue = new Color(0.5f, 0.8f, 1f, 1f);

        SerializedProperty requirements = node.FindPropertyRelative("secondaryRequirements");
        if (requirements != null)
            requirements.arraySize = 0;

        SerializedProperty levels = node.FindPropertyRelative("levels");
        SyncLevels(levels, 1);
    }

    private void DeleteNode(int nodeIndex)
    {
        DeleteNodes(new[] { nodeIndex });
    }

    private void DeleteNodes(IEnumerable<int> nodeIndices)
    {
        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        if (nodesProp == null || nodesProp.arraySize == 0)
            return;

        List<int> deleteIndices = nodeIndices
            .Where(i => i >= 0 && i < nodesProp.arraySize)
            .Distinct()
            .OrderByDescending(i => i)
            .ToList();

        if (deleteIndices.Count == 0)
            return;

        HashSet<int> deleteIndexSet = new HashSet<int>(deleteIndices);
        HashSet<string> deletedNodeIds = new HashSet<string>();

        for (int i = 0; i < deleteIndices.Count; i++)
        {
            int idx = deleteIndices[i];
            SerializedProperty node = nodesProp.GetArrayElementAtIndex(idx);
            string nodeId = node.FindPropertyRelative("nodeId").stringValue;
            if (!string.IsNullOrWhiteSpace(nodeId))
                deletedNodeIds.Add(nodeId);
        }

        Undo.RecordObject(selectedGraph, deleteIndices.Count > 1 ? "Batch Delete Tech Tree Nodes" : "Delete Tech Tree Node");
        hasLocalUndo = true;
        hasLocalRedo = false;

        foreach (int idx in deleteIndices)
        {
            while (idx < nodesProp.arraySize)
            {
                int before = nodesProp.arraySize;
                nodesProp.DeleteArrayElementAtIndex(idx);
                if (nodesProp.arraySize < before)
                    break;
            }
        }

        List<int> deletedAscending = deleteIndices.OrderBy(i => i).ToList();

        for (int i = 0; i < nodesProp.arraySize; i++)
        {
            SerializedProperty node = nodesProp.GetArrayElementAtIndex(i);
            SerializedProperty parentProp = node.FindPropertyRelative("primaryParentIndex");

            int oldParent = parentProp.intValue;
            if (deleteIndexSet.Contains(oldParent))
            {
                parentProp.intValue = -1;
            }
            else if (oldParent >= 0)
            {
                int shift = deletedAscending.Count(d => d < oldParent);
                parentProp.intValue = oldParent - shift;
            }

            SerializedProperty secondaryReqs = node.FindPropertyRelative("secondaryRequirements");
            if (secondaryReqs != null && deletedNodeIds.Count > 0)
            {
                for (int r = secondaryReqs.arraySize - 1; r >= 0; r--)
                {
                    SerializedProperty req = secondaryReqs.GetArrayElementAtIndex(r);
                    SerializedProperty targetNodeIdProp = req.FindPropertyRelative("targetNodeId");
                    if (targetNodeIdProp != null && deletedNodeIds.Contains(targetNodeIdProp.stringValue))
                        secondaryReqs.DeleteArrayElementAtIndex(r);
                }
            }
        }

        selectedNodeIndices.Clear();
        selectedNodeIndex = -1;

        selectedSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedGraph);
        canvasPanel.RequestFocus();
    }

    private void ScheduleDeleteSelectedNodes()
    {
        if (selectedNodeIndices.Count <= 0)
            return;

        List<int> snapshot = selectedNodeIndices
            .Where(i => i >= 0)
            .Distinct()
            .OrderByDescending(i => i)
            .ToList();

        if (snapshot.Count == 0)
            return;

        if (snapshot.Count == 1)
        {
            hasPendingDeleteNode = true;
            pendingDeleteNodeIndex = snapshot[0];
            return;
        }

        hasPendingBatchDelete = true;
        pendingBatchDeleteIndices = new List<int>(snapshot);
    }

    private void ClearAllNodesWithConfirm()
    {
        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        if (nodesProp == null || nodesProp.arraySize == 0)
            return;

        bool ok = EditorUtility.DisplayDialog(
            "清除全部节点",
            "确定要清除当前科技图中的全部节点吗？\n此操作会删除整张图的节点结构。",
            "清除",
            "取消"
        );

        if (!ok)
            return;

        Undo.RecordObject(selectedGraph, "Clear All Tech Tree Nodes");
        hasLocalUndo = true;
        hasLocalRedo = false;

        nodesProp.ClearArray();
        selectedNodeIndex = -1;
        selectedNodeIndices.Clear();

        selectedSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedGraph);
    }

    private void SyncAllNodeLevels()
    {
        SerializedProperty nodesProp = selectedSO.FindProperty("nodes");
        if (nodesProp == null)
            return;

        Undo.RecordObject(selectedGraph, "Sync Tech Tree Levels");
        hasLocalUndo = true;
        hasLocalRedo = false;

        for (int i = 0; i < nodesProp.arraySize; i++)
        {
            SerializedProperty node = nodesProp.GetArrayElementAtIndex(i);
            SerializedProperty maxLevelProp = node.FindPropertyRelative("maxLevel");
            SerializedProperty levelsProp = node.FindPropertyRelative("levels");
            SyncLevels(levelsProp, Mathf.Max(1, maxLevelProp.intValue));
        }

        selectedSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedGraph);
    }

    private void SyncLevels(SerializedProperty levelsProp, int maxLevel)
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

    private void CreateNewGraph()
    {
        EnsureFolderExists(DefaultGraphFolder);

        TechTreeGraphAsset asset = ScriptableObject.CreateInstance<TechTreeGraphAsset>();
        asset.displayName = "新科技图";
        asset.graphId = "tech_tree_new";
        asset.layoutMode = TechTreeGraphAsset.LayoutMode.Vertical;

        string path = AssetDatabase.GenerateUniqueAssetPath(DefaultGraphFolder + "/TT_NewGraph.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Refresh();
        SelectGraph(asset);
    }

    private void DeleteSelectedGraph()
    {
        if (selectedGraph == null)
            return;

        string path = AssetDatabase.GetAssetPath(selectedGraph);
        if (string.IsNullOrEmpty(path))
            return;

        if (!EditorUtility.DisplayDialog("删除科技图", "确定删除当前科技图吗？", "删除", "取消"))
            return;

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        selectedGraph = null;
        selectedSO = null;
        selectedNodeIndex = -1;
        selectedNodeIndices.Clear();
        hasLocalUndo = false;
        hasLocalRedo = false;
        canvasPanel.ResetView();
        Refresh();
    }

    private void SelectGraph(TechTreeGraphAsset graph)
    {
        selectedGraph = graph;
        selectedSO = graph != null ? new SerializedObject(graph) : null;
        selectedNodeIndex = -1;
        selectedNodeIndices.Clear();
        nodeInspectorScroll = Vector2.zero;
        hasLocalUndo = false;
        hasLocalRedo = false;
        canvasPanel.ResetView();
    }

    private void SelectSingleNode(int index)
    {
        selectedNodeIndex = index;
        selectedNodeIndices.Clear();

        if (index >= 0)
            selectedNodeIndices.Add(index);
    }

    private void AddMultiSelection(int index)
    {
        if (index < 0)
            return;

        if (!selectedNodeIndices.Contains(index))
            selectedNodeIndices.Add(index);

        selectedNodeIndex = index;
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

    private GUIContent BuildToolbarContent(Texture2D icon, string fallbackText, string tooltip)
    {
        if (icon != null)
            return new GUIContent(icon, tooltip);

        return new GUIContent(fallbackText, tooltip);
    }

    private void DrawPropertyRow(string label, SerializedProperty property, bool multiline = false)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(88f));

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

    private void DrawReadonlyRow(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(88f));
        EditorGUILayout.SelectableLabel(
            string.IsNullOrWhiteSpace(value) ? "-" : value,
            GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private bool DrawToolButton(Rect rect, string text, string tooltip)
    {
        return DrawMiniToolbarButton(rect, text, tooltip, null);
    }

    private bool DrawToolButton(Rect rect, Texture2D icon, string tooltip)
    {
        return DrawMiniToolbarButton(rect, "", tooltip, icon);
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
            EditorGUI.LabelField(rect, new GUIContent("", tooltip));

        return false;
    }

    private void DrawFlatSelectableRow(Rect rect, bool selected, string label, Color accent, System.Action onClick)
    {
        Event e = Event.current;
        bool hover = rect.Contains(e.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.72f, 0.78f, 0.22f));
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

    private void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
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
}