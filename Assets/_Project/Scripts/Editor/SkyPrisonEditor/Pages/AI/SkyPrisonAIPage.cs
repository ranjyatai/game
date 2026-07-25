using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonAIPage : SkyPrisonEditorPageBase
{
    private class TreeNode
    {
        public string displayName;
        public string fullKey;
        public string assetFolderPath;
        public int depth;
        public bool isFolder;
        public AIBehaviorPackage aiPackage;
        public Rect lastRect;
    }

    private enum EntryState
    {
        Valid,
        Warning,
        Error
    }

    private enum RuleTreeDropMode
    {
        None,
        RootAppend,
        IntoFolder,
        InsertBefore,
        InsertAfter
    }

    private struct RuleTreeFlatItem
    {
        public string path;
        public int depth;
        public bool isFolder;
        public string title;
        public bool enabled;
        public bool expanded;
        public EntryState state;
        public AIBehaviorPackage.AIRuleTreeNode node;
    }

    private struct RuleTreeDropTarget
    {
        public RuleTreeDropMode mode;
        public string targetPath;
        public Rect rect;
    }

    private const string AIRootFolder = "Assets/_Project/Data/AI";
    private const string DefaultAICreateFolder = "Assets/_Project/Data/AI";

    private const float RulesColumnWidth = 250f;
    private const float ColumnSpacing = 8f;
    private const float TopInfoLeftMinWidth = 300f;
    private const float TopInfoHeight = 150f;
    private const float NoteScrollViewHeight = 104f;

    private const float LeftTitleRowHeight = 22f;
    private const float LeftToolbarRowHeight = 24f;
    private const float LeftSearchRowHeight = 22f;
    private const float LeftRowGap = 6f;
    private const float LeftContainerPadding = 8f;
    private const float LeftListRowHeight = 24f;

    private const string RefreshIconPath = "Assets/_Project/Icon/Editor/SkyPrisonEditor_18.png";

    private static readonly Color RuleTreeDropLineColor = new Color(0.65f, 0.55f, 0.95f, 0.95f);
    private static readonly Color RuleTreeDropFillColor = new Color(0.65f, 0.55f, 0.95f, 0.18f);
    private static readonly Color RuleTreeMultiSelectedColor = new Color(0.38f, 0.28f, 0.55f, 0.48f);

    private const bool EnableRuleTreeDragDebug = false;

    private readonly Dictionary<string, bool> folderExpanded = new Dictionary<string, bool>();
    private readonly List<TreeNode> visibleNodes = new List<TreeNode>();
    private readonly SkyPrisonAILogicPanel logicPanel = new SkyPrisonAILogicPanel();

    private string search = "";
    private List<AIBehaviorPackage> aiPackages = new List<AIBehaviorPackage>();
    private AIBehaviorPackage selectedPackage;
    private SerializedObject selectedSO;
    private string selectedFolderPath = DefaultAICreateFolder;
    private string selectedFolderKey = "";
    private Vector2 leftPackageScroll;
    private AIBehaviorPackage editingPackageNameTarget;
    private string editingPackageNameBuffer = "";

    private readonly Color leftTopBg = new Color(0.13f, 0.13f, 0.14f, 1f);
    private readonly Color accentPurple = new Color(0.65f, 0.55f, 0.95f, 1f);

    private readonly List<RuleTreeFlatItem> ruleTreeFlatCache = new List<RuleTreeFlatItem>();
    private bool ruleTreeFlatCacheDirty = true;
    private bool rightPanelDirty = true;

    private string selectedRuleNodePath = "";
    private readonly HashSet<string> selectedRuleNodePaths = new HashSet<string>();
    private string rangeAnchorRuleNodePath = "";

    private Vector2 rulesTreeScroll;
    private Vector2 noteScroll;

    private string lastDrawnRuleNodePath = "";

    private bool isDraggingRuleTreeNode = false;
    private string draggingRuleTreeNodePath = "";
    private RuleTreeDropTarget hoveredDropTarget;

    private RuleTreeDropMode lastHoveredDropMode = RuleTreeDropMode.None;
    private string lastHoveredDropPath = "";

    private readonly List<AIBehaviorPackage.AIRuleTreeNode> ruleTreeClipboardNodes = new List<AIBehaviorPackage.AIRuleTreeNode>();
    private bool ruleTreeClipboardIsCut = false;
    private readonly List<string> cutPendingRuleTreeNodePaths = new List<string>();

    public SkyPrisonAIPage(SkyPrisonEditorContext context) : base(context) { }

    public override string TabName => "AI";

    public override void OnEnable()
    {
        Refresh();
    }

    public override void Refresh()
    {
        string selectedPath = GetPackagePath(selectedPackage);

        string[] guids = AssetDatabase.FindAssets("t:AIBehaviorPackage");
        aiPackages = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<AIBehaviorPackage>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(x => x != null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
            .ThenBy(x => x.name)
            .ToList();

        if (!string.IsNullOrEmpty(selectedPath))
        {
            AIBehaviorPackage matched = aiPackages.FirstOrDefault(x => GetPackagePath(x) == selectedPath);
            selectedPackage = matched;
        }
        else if (selectedPackage == null && aiPackages.Count > 0)
        {
            selectedPackage = aiPackages[0];
        }

        if (selectedPackage != null && (selectedSO == null || selectedSO.targetObject != selectedPackage))
            selectedSO = new SerializedObject(selectedPackage);

        ruleTreeFlatCacheDirty = true;
        rightPanelDirty = true;
        ClampRuleTreeSelection();
    }

    public override bool TrySelectObject(Object obj)
    {
        if (obj is not AIBehaviorPackage pkg)
            return false;

        Refresh();

        AIBehaviorPackage found = aiPackages.FirstOrDefault(x => x == pkg);
        if (found != null)
        {
            SelectPackage(found);
            return true;
        }

        string path = AssetDatabase.GetAssetPath(pkg);
        if (!string.IsNullOrEmpty(path))
        {
            AIBehaviorPackage loaded = AssetDatabase.LoadAssetAtPath<AIBehaviorPackage>(path);
            if (loaded != null)
            {
                Refresh();
                SelectPackage(loaded);
                return true;
            }
        }

        return false;
    }

    public override void HandleGlobalShortcuts()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown)
            return;

        if (EditorGUIUtility.editingTextField)
            return;

        SerializedProperty selectedRuleProp = GetSelectedRuleDataProperty();
        if (selectedRuleProp != null && logicPanel.HandleKeyboardShortcuts(selectedRuleProp, e))
        {
            selectedSO.ApplyModifiedProperties();
            if (selectedPackage != null)
                EditorUtility.SetDirty(selectedPackage);

            ruleTreeFlatCacheDirty = true;
            rightPanelDirty = true;
            e.Use();
            return;
        }

        bool ctrlOrCmd = e.control || e.command;

        if (HandleRuleTreeShortcuts(e, ctrlOrCmd))
        {
            e.Use();
            return;
        }

        if (ctrlOrCmd && e.keyCode == KeyCode.N)
        {
            if (selectedPackage != null)
                AddRuleNodeToBestParent();
            else
                CreateNewAIPackage(GetCurrentCreateFolder());

            e.Use();
        }
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

        DrawLeftLabelRow(titleRect, "AI 行为包列表");
        DrawPackageToolbarRow(toolbarRect);
        DrawPackageSearchRow(searchRect);

        BuildLeftPackageTree();
        DrawPackageContainer(containerRect);
    }

    public override void OnGUIRight()
    {
        if (selectedPackage == null)
        {
            EditorGUILayout.HelpBox("请先在左侧选择一个 AI 行为包。", MessageType.Info);
            return;
        }

        if (selectedSO == null || selectedSO.targetObject != selectedPackage)
            selectedSO = new SerializedObject(selectedPackage);

        // SerializedObject 必须在每次 OnGUI 绘制开始时刷新。
        // 否则切换规则节点后，右侧规则基础区可能继续沿用上一条规则的编辑缓存。
        selectedSO.Update();

        if (rightPanelDirty)
        {
            logicPanel.MarkCacheDirty();
            rightPanelDirty = false;
        }

        DrawAssetHeader(
            string.IsNullOrWhiteSpace(selectedPackage.displayName) ? selectedPackage.name : selectedPackage.displayName,
            "AI 行为包资源",
            AssetDatabase.GetAssetPath(selectedPackage),
            selectedPackage.aiId
        );

        DrawPingButtons(selectedPackage);
        DrawUniqueIdWarning(selectedPackage);

        GUILayout.Space(8f);

        EditorGUILayout.BeginHorizontal();
        DrawRuleTreeColumn();
        GUILayout.Space(ColumnSpacing);
        DrawRuleEditorColumn();
        EditorGUILayout.EndHorizontal();

        selectedSO.ApplyModifiedProperties();

        if (GUI.changed)
            EditorUtility.SetDirty(selectedPackage);
    }

    private void LogRuleTreeDrag(string message)
    {
        if (!EnableRuleTreeDragDebug)
            return;

        Debug.Log("[AI RuleTree Drag] " + message);
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

    private Texture2D LoadOptionalIcon(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return null;

        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private string GetPackagePath(AIBehaviorPackage pkg)
    {
        return pkg != null ? AssetDatabase.GetAssetPath(pkg) : "";
    }

    private void RecordUndo(string actionName)
    {
        if (selectedPackage == null)
            return;

        Undo.RecordObject(selectedPackage, actionName);
    }

    private void RecordGroupedUndo(string actionName)
    {
        if (selectedPackage == null)
            return;

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(actionName);
        Undo.RecordObject(selectedPackage, actionName);
        Undo.CollapseUndoOperations(group);
    }

    private void DrawLeftLabelRow(Rect rect, string label)
    {
        GUI.Label(rect, label, EditorStyles.boldLabel);
    }

    private void DrawPackageToolbarRow(Rect rect)
    {
        Texture2D refreshIcon = LoadOptionalIcon(RefreshIconPath);

        const float buttonSize = 20f;
        const float gap = 4f;

        float y = rect.y + (rect.height - buttonSize) * 0.5f;
        float right = rect.xMax;

        Rect refreshRect = new Rect(right - buttonSize, y, buttonSize, buttonSize);
        Rect minusRect = new Rect(refreshRect.x - gap - buttonSize, y, buttonSize, buttonSize);
        Rect plusRect = new Rect(minusRect.x - gap - buttonSize, y, buttonSize, buttonSize);

        if (DrawToolButton(plusRect, "+", "新建 AI 包"))
            CreateNewAIPackage(GetCurrentCreateFolder());

        using (new EditorGUI.DisabledScope(selectedPackage == null))
        {
            if (DrawToolButton(minusRect, "-", "删除当前 AI 包"))
                DeletePackage(selectedPackage);
        }

        if (DrawToolButton(refreshRect, refreshIcon, "刷新"))
            Refresh();
    }

    private void DrawPackageSearchRow(Rect rect)
    {
        string newSearch = EditorGUI.TextField(rect, search);
        if (newSearch != search)
        {
            search = newSearch;
            leftPackageScroll = Vector2.zero;
        }
    }

    private void DrawPackageContainer(Rect rect)
    {
        EditorGUI.DrawRect(rect, leftTopBg);
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.06f));

        Rect viewRect = new Rect(
            rect.x + LeftContainerPadding,
            rect.y + LeftContainerPadding,
            rect.width - LeftContainerPadding * 2f,
            rect.height - LeftContainerPadding * 2f);

        float contentHeight = Mathf.Max(viewRect.height, visibleNodes.Count * LeftListRowHeight);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        leftPackageScroll = GUI.BeginScrollView(viewRect, leftPackageScroll, contentRect, false, true);

        float y = 0f;
        for (int i = 0; i < visibleNodes.Count; i++)
        {
            TreeNode node = visibleNodes[i];
            Rect rowRect = new Rect(0f, y, contentRect.width, LeftListRowHeight);

            if (node.isFolder)
                DrawFolderNode(node, rowRect);
            else
                DrawPackageNode(node, rowRect);

            y += LeftListRowHeight;
        }

        if (visibleNodes.Count == 0)
            GUI.Label(new Rect(4f, 2f, contentRect.width - 8f, 22f), "没有匹配的 AI 行为包", EditorStyles.miniLabel);

        GUI.EndScrollView();
    }

    private void BuildLeftPackageTree()
    {
        visibleNodes.Clear();
        Dictionary<string, List<AIBehaviorPackage>> folderToPackages = new Dictionary<string, List<AIBehaviorPackage>>();

        foreach (AIBehaviorPackage pkg in aiPackages)
        {
            if (pkg == null)
                continue;

            string compareName = string.IsNullOrWhiteSpace(pkg.displayName) ? pkg.name : pkg.displayName;
            string compareId = string.IsNullOrWhiteSpace(pkg.aiId) ? "" : pkg.aiId;

            if (!string.IsNullOrWhiteSpace(search))
            {
                string keyword = search.Trim().ToLower();
                bool match =
                    compareName.ToLower().Contains(keyword) ||
                    compareId.ToLower().Contains(keyword) ||
                    pkg.name.ToLower().Contains(keyword);

                if (!match)
                    continue;
            }

            string path = AssetDatabase.GetAssetPath(pkg).Replace("\\", "/");
            string relativeFolder = GetRelativeFolder(path);

            if (!folderToPackages.ContainsKey(relativeFolder))
                folderToPackages.Add(relativeFolder, new List<AIBehaviorPackage>());

            folderToPackages[relativeFolder].Add(pkg);
        }

        List<string> allFolders = folderToPackages.Keys.OrderBy(x => x).ToList();
        HashSet<string> addedFolders = new HashSet<string>();

        foreach (string folder in allFolders)
        {
            string[] parts = folder.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
            string current = "";
            string currentAssetPath = AIRootFolder;

            for (int i = 0; i < parts.Length; i++)
            {
                current = string.IsNullOrEmpty(current) ? parts[i] : current + "/" + parts[i];
                currentAssetPath = currentAssetPath.TrimEnd('/') + "/" + parts[i];

                if (!addedFolders.Contains(current))
                {
                    addedFolders.Add(current);
                    visibleNodes.Add(new TreeNode
                    {
                        displayName = parts[i],
                        fullKey = current,
                        assetFolderPath = currentAssetPath,
                        depth = i,
                        isFolder = true
                    });
                }
            }

            if (!IsFolderChainVisible(folder))
                continue;

            foreach (AIBehaviorPackage pkg in folderToPackages[folder].OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName))
            {
                visibleNodes.Add(new TreeNode
                {
                    displayName = string.IsNullOrWhiteSpace(pkg.displayName) ? pkg.name : pkg.displayName,
                    fullKey = folder + "/" + pkg.name,
                    assetFolderPath = AIRootFolder.TrimEnd('/') + "/" + folder,
                    depth = parts.Length,
                    isFolder = false,
                    aiPackage = pkg
                });
            }
        }
    }

    private void DrawFolderNode(TreeNode node, Rect rowRect)
    {
        node.lastRect = rowRect;

        bool isSelected = selectedFolderKey == node.fullKey && selectedPackage == null;
        bool hover = rowRect.Contains(Event.current.mousePosition);

        if (isSelected)
        {
            EditorGUI.DrawRect(rowRect, new Color(0.24f, 0.20f, 0.34f, 1f));
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), accentPurple);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.04f));
        }

        float indent = node.depth * 14f;
        Rect foldoutRect = new Rect(rowRect.x + indent + 4f, rowRect.y + 2f, rowRect.width - indent - 8f, rowRect.height - 4f);

        bool expanded = GetFolderExpanded(node.fullKey);
        bool newExpanded = EditorGUI.Foldout(foldoutRect, expanded, node.displayName, true);
        if (newExpanded != expanded)
            folderExpanded[node.fullKey] = newExpanded;

        Event e = Event.current;
        if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
        {
            if (e.button == 0)
            {
                SelectFolder(node.fullKey, node.assetFolderPath);
                e.Use();
            }
            else if (e.button == 1)
            {
                SelectFolder(node.fullKey, node.assetFolderPath);
                ShowFolderContextMenu(node);
                e.Use();
            }
        }
    }

    private void DrawPackageNode(TreeNode node, Rect rowRect)
    {
        node.lastRect = rowRect;

        bool isSelected = selectedPackage == node.aiPackage;
        bool hover = rowRect.Contains(Event.current.mousePosition);

        if (isSelected)
        {
            EditorGUI.DrawRect(rowRect, new Color(0.24f, 0.20f, 0.34f, 1f));
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), accentPurple);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.05f));
        }

        float indent = node.depth * 14f + 18f;
        Rect labelRect = new Rect(rowRect.x + indent, rowRect.y + 2f, rowRect.width - indent - 6f, rowRect.height - 4f);

        if (editingPackageNameTarget == node.aiPackage)
        {
            GUI.SetNextControlName("AIPackageRenameField");
            editingPackageNameBuffer = EditorGUI.TextField(labelRect, editingPackageNameBuffer);

            Event editEvent = Event.current;
            if (editEvent.type == EventType.KeyDown && (editEvent.keyCode == KeyCode.Return || editEvent.keyCode == KeyCode.KeypadEnter))
            {
                CommitAIPackageRename();
                editEvent.Use();
            }
            else if (editEvent.type == EventType.KeyDown && editEvent.keyCode == KeyCode.Escape)
            {
                CancelAIPackageRename();
                editEvent.Use();
            }
            else if (editEvent.type == EventType.MouseDown && !rowRect.Contains(editEvent.mousePosition))
            {
                CommitAIPackageRename();
            }

            return;
        }

        string label = string.IsNullOrWhiteSpace(node.aiPackage.displayName) ? node.aiPackage.name : node.aiPackage.displayName;
        GUIStyle style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(0, 6, 0, 0),
            normal = { textColor = isSelected ? Color.white : new Color(0.88f, 0.88f, 0.90f, 1f) }
        };
        GUI.Label(labelRect, label, style);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
        {
            if (e.button == 0)
            {
                SelectPackage(node.aiPackage);
                selectedFolderPath = node.assetFolderPath;

                if (e.clickCount == 2)
                    BeginAIPackageRename(node.aiPackage);

                e.Use();
            }
            else if (e.button == 1)
            {
                SelectPackage(node.aiPackage);
                selectedFolderPath = node.assetFolderPath;
                ShowPackageContextMenu(node.aiPackage);
                e.Use();
            }
        }
    }

    private void BeginAIPackageRename(AIBehaviorPackage pkg)
    {
        if (pkg == null)
            return;

        editingPackageNameTarget = pkg;
        editingPackageNameBuffer = string.IsNullOrWhiteSpace(pkg.displayName) ? pkg.name : pkg.displayName;
        EditorGUI.FocusTextInControl("AIPackageRenameField");
    }

    private void CommitAIPackageRename()
    {
        if (editingPackageNameTarget == null)
            return;

        string newName = (editingPackageNameBuffer ?? "").Trim();
        if (string.IsNullOrWhiteSpace(newName))
            newName = editingPackageNameTarget.name;

        Undo.RecordObject(editingPackageNameTarget, "Rename AI Package Display Name");
        editingPackageNameTarget.displayName = newName;
        EditorUtility.SetDirty(editingPackageNameTarget);
        AssetDatabase.SaveAssets();

        editingPackageNameTarget = null;
        editingPackageNameBuffer = "";
        Refresh();
    }

    private void CancelAIPackageRename()
    {
        editingPackageNameTarget = null;
        editingPackageNameBuffer = "";
    }

    private void DrawRuleTreeColumn()
    {
        RebuildRuleTreeFlatCacheIfNeeded();

        hoveredDropTarget = new RuleTreeDropTarget
        {
            mode = RuleTreeDropMode.None,
            targetPath = "",
            rect = Rect.zero
        };

        EditorGUILayout.BeginVertical(GUILayout.Width(RulesColumnWidth), GUILayout.ExpandHeight(true));

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("规则树", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+规则", GUILayout.Height(22f)))
            AddRuleNodeToBestParent();
        if (GUILayout.Button("+文件夹", GUILayout.Height(22f)))
            AddFolderNodeToBestParent();

        using (new EditorGUI.DisabledScope(selectedRuleNodePaths.Count == 0))
        {
            if (GUILayout.Button("-", GUILayout.Width(28f), GUILayout.Height(22f)))
                DeleteSelectedRuleTreeNodes();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
        rulesTreeScroll = EditorGUILayout.BeginScrollView(rulesTreeScroll, GUILayout.ExpandHeight(true));

        DrawRuleTreeRootDropBar();

        for (int i = 0; i < ruleTreeFlatCache.Count; i++)
            DrawRuleTreeFlatRow(ruleTreeFlatCache[i]);

        HandleRuleTreeEmptyAreaContext();

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndVertical();

        HandleRuleTreeDrop(Event.current);

        if (lastHoveredDropMode != hoveredDropTarget.mode || lastHoveredDropPath != hoveredDropTarget.targetPath)
        {
            lastHoveredDropMode = hoveredDropTarget.mode;
            lastHoveredDropPath = hoveredDropTarget.targetPath;
            GUI.changed = true;
        }
    }

    private void RebuildRuleTreeFlatCacheIfNeeded()
    {
        if (selectedPackage == null)
            return;

        if (!ruleTreeFlatCacheDirty)
            return;

        ruleTreeFlatCache.Clear();
        BuildRuleTreeFlatRecursive(selectedPackage.ruleTree, 0, "");
        ruleTreeFlatCacheDirty = false;
    }

    private void BuildRuleTreeFlatRecursive(List<AIBehaviorPackage.AIRuleTreeNode> nodes, int depth, string prefix)
    {
        if (nodes == null)
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            AIBehaviorPackage.AIRuleTreeNode node = nodes[i];
            if (node == null)
                continue;

            string path = string.IsNullOrWhiteSpace(prefix) ? i.ToString() : prefix + "/children/" + i;

            RuleTreeFlatItem item = new RuleTreeFlatItem
            {
                path = path,
                depth = depth,
                isFolder = node.nodeType == AIBehaviorPackage.RuleTreeNodeType.Folder,
                title = GetRuntimeNodeTitle(node),
                enabled = GetRuntimeNodeEnabled(node),
                expanded = node.expanded,
                state = GetRuntimeRuleState(node),
                node = node
            };

            ruleTreeFlatCache.Add(item);

            if (item.isFolder && node.expanded && node.children != null && node.children.Count > 0)
                BuildRuleTreeFlatRecursive(node.children, depth + 1, path);
        }
    }

    private void DrawRuleTreeRootDropBar()
    {
        Rect rowRect = EditorGUILayout.GetControlRect(false, 22f);

        bool hover = false;
        if (isDraggingRuleTreeNode && CanMoveSelectionToRoot())
        {
            hover = rowRect.Contains(Event.current.mousePosition);
            if (hover)
            {
                hoveredDropTarget = new RuleTreeDropTarget
                {
                    mode = RuleTreeDropMode.RootAppend,
                    targetPath = "",
                    rect = rowRect
                };

                LogRuleTreeDrag("Hover RootAppend");

                EditorGUI.DrawRect(rowRect, RuleTreeDropFillColor);
                DrawRuleTreeDropLine(rowRect, false);
            }
        }

        EditorGUI.LabelField(
            new Rect(rowRect.x + 6f, rowRect.y + 2f, rowRect.width - 12f, 18f),
            "根目录",
            EditorStyles.miniBoldLabel
        );

        Event e = Event.current;
        if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition) && e.button == 0)
        {
            ClearRuleTreeSelection();
            e.Use();
        }

        if (e.type == EventType.ContextClick && rowRect.Contains(e.mousePosition))
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("新建规则"), false, AddRuleNodeAtRoot);
            menu.AddItem(new GUIContent("新建文件夹"), false, AddFolderNodeAtRoot);

            if (ruleTreeClipboardNodes.Count > 0)
                menu.AddItem(new GUIContent("粘贴到根目录"), false, PasteRuleTreeNodesAtRoot);
            else
                menu.AddDisabledItem(new GUIContent("粘贴到根目录"));

            menu.ShowAsContext();
            e.Use();
        }
    }

    private void DrawRuleTreeFlatRow(RuleTreeFlatItem item)
    {
        Rect rowRect = EditorGUILayout.GetControlRect(false, 22f);

        bool isPrimarySelected = selectedRuleNodePath == item.path;
        bool isSelected = selectedRuleNodePaths.Contains(item.path);
        bool isCutPending = cutPendingRuleTreeNodePaths.Contains(item.path);

        Rect topLineRect = new Rect(rowRect.x + 2f, rowRect.y, rowRect.width - 4f, 4f);
        Rect bottomLineRect = new Rect(rowRect.x + 2f, rowRect.yMax - 4f, rowRect.width - 4f, 4f);
        Rect centerRect = new Rect(rowRect.x + 2f, rowRect.y + 4f, rowRect.width - 4f, rowRect.height - 8f);

        RuleTreeDropTarget localHover = new RuleTreeDropTarget
        {
            mode = RuleTreeDropMode.None,
            targetPath = "",
            rect = Rect.zero
        };

        if (isDraggingRuleTreeNode)
        {
            if (topLineRect.Contains(Event.current.mousePosition) && CanDropInsertBefore(item.path))
            {
                localHover.mode = RuleTreeDropMode.InsertBefore;
                localHover.targetPath = item.path;
                localHover.rect = rowRect;
                LogRuleTreeDrag("Hover InsertBefore | target=" + item.path);
            }
            else if (bottomLineRect.Contains(Event.current.mousePosition) && CanDropInsertAfter(item.path))
            {
                localHover.mode = RuleTreeDropMode.InsertAfter;
                localHover.targetPath = item.path;
                localHover.rect = rowRect;
                LogRuleTreeDrag("Hover InsertAfter | target=" + item.path);
            }
            else if (item.isFolder && centerRect.Contains(Event.current.mousePosition) && CanDropIntoFolder(item.path))
            {
                localHover.mode = RuleTreeDropMode.IntoFolder;
                localHover.targetPath = item.path;
                localHover.rect = rowRect;
                LogRuleTreeDrag("Hover IntoFolder | target=" + item.path);
            }

            if (localHover.mode != RuleTreeDropMode.None)
            {
                hoveredDropTarget = localHover;

                if (localHover.mode == RuleTreeDropMode.IntoFolder)
                {
                    EditorGUI.DrawRect(rowRect, RuleTreeDropFillColor);
                    DrawRuleTreeDropLine(rowRect, false);
                }
                else if (localHover.mode == RuleTreeDropMode.InsertBefore)
                {
                    DrawRuleTreeDropLine(rowRect, true);
                }
                else if (localHover.mode == RuleTreeDropMode.InsertAfter)
                {
                    DrawRuleTreeDropLine(rowRect, false);
                }
            }
        }

        if (isSelected)
            EditorGUI.DrawRect(rowRect, RuleTreeMultiSelectedColor);

        if (isPrimarySelected)
            EditorGUI.DrawRect(rowRect, new Color(0.42f, 0.32f, 0.62f, 0.72f));

        float indent = item.depth * 16f;
        float x = rowRect.x + indent;

        if (item.isFolder)
        {
            Rect foldRect = new Rect(x, rowRect.y, 18f, rowRect.height);
            bool newExpanded = EditorGUI.Foldout(foldRect, item.expanded, GUIContent.none, false);
            if (newExpanded != item.node.expanded)
            {
                item.node.expanded = newExpanded;
                ruleTreeFlatCacheDirty = true;
                GUI.changed = true;
            }
            x += 18f;
        }
        else
        {
            x += 18f;
        }

        Color oldColor = GUI.color;
        if (isCutPending)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
        }
        else if (!item.enabled)
        {
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        }
        else if (!item.isFolder)
        {
            if (item.state == EntryState.Error)
                GUI.color = new Color(1f, 0.4f, 0.4f, 1f);
            else if (item.state == EntryState.Warning)
                GUI.color = new Color(1f, 0.8f, 0.3f, 1f);
        }

        Rect labelRect = new Rect(x, rowRect.y + 2f, rowRect.width - (x - rowRect.x), 18f);
        EditorGUI.LabelField(labelRect, item.isFolder ? $"📁 {item.title}" : $"• {item.title}");
        GUI.color = oldColor;

        Event e = Event.current;
        if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
        {
            if (e.button == 0)
            {
                HandleRuleTreeSelectionClick(item.path, e);
                e.Use();
            }
            else if (e.button == 1)
            {
                HandleRuleTreeRightClickSelection(item.path);
                ShowRuleTreeNodeContextMenu(item.path, item.isFolder);
                e.Use();
            }
        }

        if (e.type == EventType.MouseDrag &&
            e.button == 0 &&
            rowRect.Contains(e.mousePosition) &&
            selectedRuleNodePaths.Contains(item.path))
        {
            isDraggingRuleTreeNode = true;
            draggingRuleTreeNodePath = item.path;

            LogRuleTreeDrag(
                "Begin Drag | path=" + item.path +
                " | selectedCount=" + selectedRuleNodePaths.Count +
                " | selected=" + string.Join(", ", selectedRuleNodePaths)
            );

            e.Use();
        }
    }

    private void DrawRuleTreeDropLine(Rect rowRect, bool atTop)
    {
        Rect lineRect = new Rect(
            rowRect.x + 2f,
            atTop ? rowRect.y : rowRect.yMax - 2f,
            rowRect.width - 4f,
            2f
        );

        EditorGUI.DrawRect(lineRect, RuleTreeDropLineColor);
    }

    private void HandleRuleTreeEmptyAreaContext()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.ContextClick)
            return;

        Rect lastRect = GUILayoutUtility.GetLastRect();
        if (!lastRect.Contains(e.mousePosition))
            return;

        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("新建规则"), false, AddRuleNodeAtRoot);
        menu.AddItem(new GUIContent("新建文件夹"), false, AddFolderNodeAtRoot);

        if (ruleTreeClipboardNodes.Count > 0)
            menu.AddItem(new GUIContent("粘贴"), false, PasteRuleTreeNodesAtRoot);
        else
            menu.AddDisabledItem(new GUIContent("粘贴"));

        menu.ShowAsContext();
        e.Use();
    }

    private void HandleRuleTreeDrop(Event e)
    {
        if (e == null)
            return;

        if (e.type != EventType.MouseUp || !isDraggingRuleTreeNode)
            return;

        LogRuleTreeDrag(
            "MouseUp | mode=" + hoveredDropTarget.mode +
            " | target=" + hoveredDropTarget.targetPath +
            " | draggingSource=" + draggingRuleTreeNodePath
        );

        bool moved = false;
        List<string> movingPaths = GetOrderedSelectedPathsForMove();

        if (movingPaths.Count == 0 && !string.IsNullOrWhiteSpace(draggingRuleTreeNodePath))
            movingPaths.Add(draggingRuleTreeNodePath);

        LogRuleTreeDrag("Moving Paths | " + string.Join(", ", movingPaths));

        if (hoveredDropTarget.mode == RuleTreeDropMode.RootAppend)
            moved = MoveRuleTreeNodesToRoot(movingPaths);
        else if (hoveredDropTarget.mode == RuleTreeDropMode.IntoFolder)
            moved = MoveRuleTreeNodesIntoFolder(movingPaths, hoveredDropTarget.targetPath);
        else if (hoveredDropTarget.mode == RuleTreeDropMode.InsertBefore)
            moved = ReorderRuleTreeNodes(movingPaths, hoveredDropTarget.targetPath, true);
        else if (hoveredDropTarget.mode == RuleTreeDropMode.InsertAfter)
            moved = ReorderRuleTreeNodes(movingPaths, hoveredDropTarget.targetPath, false);

        isDraggingRuleTreeNode = false;
        draggingRuleTreeNodePath = "";
        hoveredDropTarget = new RuleTreeDropTarget
        {
            mode = RuleTreeDropMode.None,
            targetPath = "",
            rect = Rect.zero
        };

        if (moved)
        {
            logicPanel.ResetSelection();
            if (rightPanelDirty)
                selectedSO.Update();
            RepaintHost();
        }

        e.Use();
    }

    private bool HandleRuleTreeShortcuts(Event e, bool ctrlOrCmd)
    {
        if (selectedPackage == null)
            return false;

        if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            if (selectedRuleNodePaths.Count > 0)
            {
                DeleteSelectedRuleTreeNodes();
                return true;
            }
        }

        if (ctrlOrCmd && e.keyCode == KeyCode.C)
        {
            if (selectedRuleNodePaths.Count > 0)
            {
                CopySelectedRuleTreeNodes();
                return true;
            }
        }

        if (ctrlOrCmd && e.keyCode == KeyCode.X)
        {
            if (selectedRuleNodePaths.Count > 0)
            {
                CutSelectedRuleTreeNodes();
                return true;
            }
        }

        if (ctrlOrCmd && e.keyCode == KeyCode.V)
        {
            if (ruleTreeClipboardNodes.Count > 0)
            {
                PasteRuleTreeNodesToBestParent();
                return true;
            }
        }

        return false;
    }

    private bool CanMoveSelectionToRoot()
    {
        List<string> moving = GetOrderedSelectedPathsForMove();
        if (moving.Count == 0)
            return false;

        for (int i = 0; i < moving.Count; i++)
        {
            if (TryGetRuntimeNode(moving[i], out _, out List<AIBehaviorPackage.AIRuleTreeNode> ownerList, out _))
            {
                if (!ReferenceEquals(ownerList, selectedPackage.ruleTree))
                    return true;
            }
        }

        return false;
    }

    private bool CanDropIntoFolder(string folderPath)
    {
        bool result = true;

        if (!TryGetRuntimeNode(folderPath, out AIBehaviorPackage.AIRuleTreeNode folderNode, out _, out _))
            result = false;
        else if (folderNode == null || folderNode.nodeType != AIBehaviorPackage.RuleTreeNodeType.Folder)
            result = false;
        else
        {
            List<string> moving = GetOrderedSelectedPathsForMove();
            if (moving.Count == 0)
                result = false;
            else
            {
                for (int i = 0; i < moving.Count; i++)
                {
                    string source = moving[i];
                    if (source == folderPath || folderPath.StartsWith(source + "/"))
                    {
                        result = false;
                        break;
                    }
                }
            }
        }

        LogRuleTreeDrag("CanDropIntoFolder | folder=" + folderPath + " | result=" + result);
        return result;
    }

    private bool CanDropInsertBefore(string targetPath)
    {
        bool result = CanDropInsertRelative(targetPath, true);
        LogRuleTreeDrag("CanDropInsertBefore | target=" + targetPath + " | result=" + result);
        return result;
    }

    private bool CanDropInsertAfter(string targetPath)
    {
        bool result = CanDropInsertRelative(targetPath, false);
        LogRuleTreeDrag("CanDropInsertAfter | target=" + targetPath + " | result=" + result);
        return result;
    }

    private bool CanDropInsertRelative(string targetPath, bool before)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        List<string> moving = GetOrderedSelectedPathsForMove();
        if (moving.Count == 0)
            return false;

        if (moving.Contains(targetPath))
            return false;

        if (!TryGetRuntimeNode(targetPath, out _, out List<AIBehaviorPackage.AIRuleTreeNode> targetOwner, out _))
            return false;

        if (targetOwner == null)
            return false;

        for (int i = 0; i < moving.Count; i++)
        {
            if (!TryGetRuntimeNode(moving[i], out _, out List<AIBehaviorPackage.AIRuleTreeNode> srcOwner, out _))
                return false;

            if (!ReferenceEquals(srcOwner, targetOwner))
                return true;
        }

        return true;
    }

    private void ShowRuleTreeNodeContextMenu(string nodePath, bool isFolder)
    {
        GenericMenu menu = new GenericMenu();

        if (isFolder)
        {
            menu.AddItem(new GUIContent("在此文件夹下新建规则"), false, () => AddRuleNodeUnderPath(nodePath));
            menu.AddItem(new GUIContent("在此文件夹下新建文件夹"), false, () => AddFolderNodeUnderPath(nodePath));

            if (ruleTreeClipboardNodes.Count > 0)
                menu.AddItem(new GUIContent("粘贴到此文件夹"), false, () => PasteRuleTreeNodesToFolder(nodePath));
            else
                menu.AddDisabledItem(new GUIContent("粘贴到此文件夹"));
        }
        else
        {
            menu.AddItem(new GUIContent("在根目录新建规则"), false, AddRuleNodeAtRoot);
            menu.AddItem(new GUIContent("在根目录新建文件夹"), false, AddFolderNodeAtRoot);

            if (ruleTreeClipboardNodes.Count > 0)
                menu.AddItem(new GUIContent("粘贴到根目录"), false, PasteRuleTreeNodesAtRoot);
            else
                menu.AddDisabledItem(new GUIContent("粘贴到根目录"));
        }

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("复制"), false, CopySelectedRuleTreeNodes);
        menu.AddItem(new GUIContent("剪切"), false, CutSelectedRuleTreeNodes);
        menu.AddItem(new GUIContent("删除"), false, DeleteSelectedRuleTreeNodes);
        menu.ShowAsContext();
    }

    private void ForceRefreshRuleEditorPanel(bool resetLogicSelection = true)
    {
        // TextField / TextArea 在 Unity IMGUI 里会保留当前控件的编辑缓存。
        // 切换规则节点时如果不主动清掉焦点，规则名称、冷却、备注等字段可能显示上一条规则的内容。
        GUIUtility.keyboardControl = 0;
        GUIUtility.hotControl = 0;
        EditorGUIUtility.editingTextField = false;

        noteScroll = Vector2.zero;

        if (resetLogicSelection)
            logicPanel.ResetSelection();

        logicPanel.MarkCacheDirty();

        if (selectedSO != null)
            selectedSO.Update();

        rightPanelDirty = true;
    }

    private void DrawRuleEditorColumn()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        if (lastDrawnRuleNodePath != selectedRuleNodePath)
        {
            lastDrawnRuleNodePath = selectedRuleNodePath;
            ForceRefreshRuleEditorPanel(false);
        }

        SerializedProperty selectedRuleProp = GetSelectedRuleDataProperty();

        if (selectedRuleProp == null)
        {
            if (selectedRuleNodePaths.Count > 1)
                EditorGUILayout.HelpBox($"当前选中了 {selectedRuleNodePaths.Count} 个节点。多选状态下不显示单条规则编辑。", MessageType.Info);
            else
                EditorGUILayout.HelpBox("请在中间规则树中选择一条规则节点。", MessageType.Info);

            EditorGUILayout.EndVertical();
            return;
        }

        DrawRuleTopInfoArea(selectedRuleProp);
        GUILayout.Space(8f);
        logicPanel.Draw(selectedRuleProp);

        EditorGUILayout.EndVertical();
    }

    private void DrawRuleTopInfoArea(SerializedProperty ruleProp)
    {
        EditorGUILayout.BeginHorizontal(GUILayout.Height(TopInfoHeight));

        EditorGUILayout.BeginVertical("box", GUILayout.MinWidth(TopInfoLeftMinWidth), GUILayout.Height(TopInfoHeight));
        EditorGUILayout.LabelField("规则基础", EditorStyles.boldLabel);
        DrawRow("启用", ruleProp.FindPropertyRelative("enabled"));
        DrawRow("规则名称", ruleProp.FindPropertyRelative("ruleName"));
        DrawRow("优先级", ruleProp.FindPropertyRelative("priority"));
        DrawRow("规则冷却秒数", ruleProp.FindPropertyRelative("cooldownSeconds"));
        DrawRow("条件全部满足", ruleProp.FindPropertyRelative("requireAllConditions"));
        EditorGUILayout.EndVertical();

        GUILayout.Space(ColumnSpacing);

        EditorGUILayout.BeginVertical("box", GUILayout.Height(TopInfoHeight), GUILayout.ExpandWidth(true));
        EditorGUILayout.LabelField("备注", EditorStyles.boldLabel);

        SerializedProperty noteProp = ruleProp.FindPropertyRelative("note");

        noteScroll = EditorGUILayout.BeginScrollView(
            noteScroll,
            GUILayout.Height(NoteScrollViewHeight),
            GUILayout.ExpandWidth(true)
        );

        EditorGUI.BeginChangeCheck();
        string newNote = EditorGUILayout.TextArea(
            noteProp.stringValue,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true)
        );
        if (EditorGUI.EndChangeCheck())
        {
            RecordUndo("Edit AI 备注");
            noteProp.stringValue = newNote;
            selectedSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(selectedPackage);
            ruleTreeFlatCacheDirty = true;
            rightPanelDirty = true;
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private void AddRuleNodeToBestParent()
    {
        if (string.IsNullOrWhiteSpace(selectedRuleNodePath))
        {
            AddRuleNodeAtRoot();
            return;
        }

        if (TryGetRuntimeNode(selectedRuleNodePath, out AIBehaviorPackage.AIRuleTreeNode selectedNode, out _, out _))
        {
            if (selectedNode != null && selectedNode.nodeType == AIBehaviorPackage.RuleTreeNodeType.Folder)
                AddRuleNodeUnderPath(selectedRuleNodePath);
            else
                AddRuleNodeAtRoot();
        }
        else
        {
            AddRuleNodeAtRoot();
        }
    }

    private void AddFolderNodeToBestParent()
    {
        if (string.IsNullOrWhiteSpace(selectedRuleNodePath))
        {
            AddFolderNodeAtRoot();
            return;
        }

        if (TryGetRuntimeNode(selectedRuleNodePath, out AIBehaviorPackage.AIRuleTreeNode selectedNode, out _, out _))
        {
            if (selectedNode != null && selectedNode.nodeType == AIBehaviorPackage.RuleTreeNodeType.Folder)
                AddFolderNodeUnderPath(selectedRuleNodePath);
            else
                AddFolderNodeAtRoot();
        }
        else
        {
            AddFolderNodeAtRoot();
        }
    }

    private void AddRuleNodeAtRoot()
    {
        if (selectedPackage == null)
            return;

        RecordGroupedUndo("Add AI Rule Node");

        string name = GetNextRuleNodeName();
        selectedPackage.ruleTree.Add(CreateRuntimeRuleNode(name));

        SetSingleRuleTreeSelection((selectedPackage.ruleTree.Count - 1).ToString());
        logicPanel.ResetSelection();

        ruleTreeFlatCacheDirty = true;
        MarkPackageDirtyAndRefreshSO(true);
    }

    private void AddFolderNodeAtRoot()
    {
        if (selectedPackage == null)
            return;

        RecordGroupedUndo("Add AI Folder Node");

        string name = GetNextFolderNodeName();
        selectedPackage.ruleTree.Add(CreateRuntimeFolderNode(name));

        SetSingleRuleTreeSelection((selectedPackage.ruleTree.Count - 1).ToString());
        logicPanel.ResetSelection();

        ruleTreeFlatCacheDirty = true;
        MarkPackageDirtyAndRefreshSO(true);
    }

    private void AddRuleNodeUnderPath(string folderNodePath)
    {
        if (!TryGetRuntimeNode(folderNodePath, out AIBehaviorPackage.AIRuleTreeNode folderNode, out _, out _))
            return;

        if (folderNode == null || folderNode.nodeType != AIBehaviorPackage.RuleTreeNodeType.Folder)
            return;

        RecordGroupedUndo("Add AI Rule Node");

        string name = GetNextRuleNodeName();
        folderNode.children.Add(CreateRuntimeRuleNode(name));
        folderNode.expanded = true;

        SetSingleRuleTreeSelection(folderNodePath + "/children/" + (folderNode.children.Count - 1));
        logicPanel.ResetSelection();

        ruleTreeFlatCacheDirty = true;
        MarkPackageDirtyAndRefreshSO(true);
    }

    private void AddFolderNodeUnderPath(string folderNodePath)
    {
        if (!TryGetRuntimeNode(folderNodePath, out AIBehaviorPackage.AIRuleTreeNode folderNode, out _, out _))
            return;

        if (folderNode == null || folderNode.nodeType != AIBehaviorPackage.RuleTreeNodeType.Folder)
            return;

        RecordGroupedUndo("Add AI Folder Node");

        string name = GetNextFolderNodeName();
        folderNode.children.Add(CreateRuntimeFolderNode(name));
        folderNode.expanded = true;

        SetSingleRuleTreeSelection(folderNodePath + "/children/" + (folderNode.children.Count - 1));
        logicPanel.ResetSelection();

        ruleTreeFlatCacheDirty = true;
        MarkPackageDirtyAndRefreshSO(true);
    }

    private AIBehaviorPackage.AIRuleTreeNode CreateRuntimeRuleNode(string name)
    {
        return new AIBehaviorPackage.AIRuleTreeNode
        {
            nodeType = AIBehaviorPackage.RuleTreeNodeType.Rule,
            displayName = name,
            expanded = true,
            enabled = true,
            note = "",
            ruleData = new AIBehaviorPackage.AIRule
            {
                enabled = true,
                ruleName = name,
                note = "",
                priority = 100,
                cooldownSeconds = 0f,
                requireAllConditions = true,
                conditions = new List<AIBehaviorPackage.AIConditionUnit>(),
                actions = new List<AIBehaviorPackage.AIActionUnit>(),
                sentenceRoot = new AIBehaviorPackage.LogicSentenceRoot()
            },
            children = new List<AIBehaviorPackage.AIRuleTreeNode>()
        };
    }

    private AIBehaviorPackage.AIRuleTreeNode CreateRuntimeFolderNode(string name)
    {
        return new AIBehaviorPackage.AIRuleTreeNode
        {
            nodeType = AIBehaviorPackage.RuleTreeNodeType.Folder,
            displayName = name,
            expanded = true,
            enabled = true,
            note = "",
            ruleData = null,
            children = new List<AIBehaviorPackage.AIRuleTreeNode>()
        };
    }

    private string GetNextRuleNodeName()
    {
        int count = CountRuleTreeNodesByType(selectedPackage.ruleTree, AIBehaviorPackage.RuleTreeNodeType.Rule);
        return $"新规则 {count + 1}";
    }

    private string GetNextFolderNodeName()
    {
        int count = CountRuleTreeNodesByType(selectedPackage.ruleTree, AIBehaviorPackage.RuleTreeNodeType.Folder);
        return $"新文件夹 {count + 1}";
    }

    private int CountRuleTreeNodesByType(List<AIBehaviorPackage.AIRuleTreeNode> nodes, AIBehaviorPackage.RuleTreeNodeType type)
    {
        if (nodes == null)
            return 0;

        int count = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            AIBehaviorPackage.AIRuleTreeNode node = nodes[i];
            if (node == null)
                continue;

            if (node.nodeType == type)
                count++;

            if (node.children != null && node.children.Count > 0)
                count += CountRuleTreeNodesByType(node.children, type);
        }

        return count;
    }

    private void DeleteSelectedRuleTreeNodes()
    {
        List<string> paths = GetOrderedSelectedPathsForDelete();
        if (paths.Count == 0)
            return;

        RecordGroupedUndo("Delete AI Tree Nodes");

        for (int i = 0; i < paths.Count; i++)
            RemoveRuntimeNodeByPath(paths[i]);

        cutPendingRuleTreeNodePaths.RemoveAll(x => paths.Contains(x));

        ClearRuleTreeSelection();
        logicPanel.ResetSelection();
        ruleTreeFlatCacheDirty = true;
        MarkPackageDirtyAndRefreshSO(true);
    }

    private void CopySelectedRuleTreeNodes()
    {
        List<string> paths = GetOrderedSelectedPathsForCopyPaste();
        if (paths.Count == 0)
            return;

        ruleTreeClipboardNodes.Clear();

        for (int i = 0; i < paths.Count; i++)
        {
            if (!TryGetRuntimeNode(paths[i], out AIBehaviorPackage.AIRuleTreeNode node, out _, out _))
                continue;

            ruleTreeClipboardNodes.Add(CloneRuleTreeNode(node));
        }

        ruleTreeClipboardIsCut = false;
        cutPendingRuleTreeNodePaths.Clear();
    }

    private void CutSelectedRuleTreeNodes()
    {
        List<string> paths = GetOrderedSelectedPathsForCopyPaste();
        if (paths.Count == 0)
            return;

        ruleTreeClipboardNodes.Clear();

        for (int i = 0; i < paths.Count; i++)
        {
            if (!TryGetRuntimeNode(paths[i], out AIBehaviorPackage.AIRuleTreeNode node, out _, out _))
                continue;

            ruleTreeClipboardNodes.Add(CloneRuleTreeNode(node));
        }

        ruleTreeClipboardIsCut = true;
        cutPendingRuleTreeNodePaths.Clear();
        cutPendingRuleTreeNodePaths.AddRange(paths);

        GUI.changed = true;
    }

    private void PasteRuleTreeNodesToBestParent()
    {
        if (ruleTreeClipboardNodes.Count == 0 || selectedPackage == null)
            return;

        if (!string.IsNullOrWhiteSpace(selectedRuleNodePath) &&
            TryGetRuntimeNode(selectedRuleNodePath, out AIBehaviorPackage.AIRuleTreeNode selectedNode, out _, out _) &&
            selectedNode != null &&
            selectedNode.nodeType == AIBehaviorPackage.RuleTreeNodeType.Folder)
        {
            PasteRuleTreeNodesToFolder(selectedRuleNodePath);
            return;
        }

        PasteRuleTreeNodesAtRoot();
    }

    private void PasteRuleTreeNodesAtRoot()
    {
        if (ruleTreeClipboardNodes.Count == 0 || selectedPackage == null)
            return;

        RecordGroupedUndo(ruleTreeClipboardIsCut ? "Cut Paste AI Tree Nodes" : "Paste AI Tree Nodes");

        if (ruleTreeClipboardIsCut)
            RemoveRuntimeNodesByPaths(cutPendingRuleTreeNodePaths);

        int startIndex = selectedPackage.ruleTree.Count;
        for (int i = 0; i < ruleTreeClipboardNodes.Count; i++)
            selectedPackage.ruleTree.Add(CloneRuleTreeNode(ruleTreeClipboardNodes[i]));

        ClearRuleTreeSelection();
        for (int i = 0; i < ruleTreeClipboardNodes.Count; i++)
            selectedRuleNodePaths.Add((startIndex + i).ToString());

        selectedRuleNodePath = selectedRuleNodePaths.LastOrDefault();
        rangeAnchorRuleNodePath = selectedRuleNodePath;

        if (ruleTreeClipboardIsCut)
        {
            ruleTreeClipboardNodes.Clear();
            cutPendingRuleTreeNodePaths.Clear();
            ruleTreeClipboardIsCut = false;
        }

        ruleTreeFlatCacheDirty = true;
        MarkPackageDirtyAndRefreshSO(true);
    }

    private void PasteRuleTreeNodesToFolder(string folderPath)
    {
        if (ruleTreeClipboardNodes.Count == 0 || selectedPackage == null)
            return;

        if (!TryGetRuntimeNode(folderPath, out AIBehaviorPackage.AIRuleTreeNode folderNode, out _, out _))
            return;

        if (folderNode == null || folderNode.nodeType != AIBehaviorPackage.RuleTreeNodeType.Folder)
            return;

        if (ruleTreeClipboardIsCut)
        {
            for (int i = 0; i < cutPendingRuleTreeNodePaths.Count; i++)
            {
                string cutPath = cutPendingRuleTreeNodePaths[i];
                if (folderPath == cutPath || folderPath.StartsWith(cutPath + "/"))
                    return;
            }
        }

        RecordGroupedUndo(ruleTreeClipboardIsCut ? "Cut Paste AI Tree Nodes" : "Paste AI Tree Nodes");

        if (ruleTreeClipboardIsCut)
            RemoveRuntimeNodesByPaths(cutPendingRuleTreeNodePaths);

        folderNode.expanded = true;
        int startIndex = folderNode.children.Count;

        for (int i = 0; i < ruleTreeClipboardNodes.Count; i++)
            folderNode.children.Add(CloneRuleTreeNode(ruleTreeClipboardNodes[i]));

        ClearRuleTreeSelection();
        for (int i = 0; i < ruleTreeClipboardNodes.Count; i++)
            selectedRuleNodePaths.Add(folderPath + "/children/" + (startIndex + i));

        selectedRuleNodePath = selectedRuleNodePaths.LastOrDefault();
        rangeAnchorRuleNodePath = selectedRuleNodePath;

        if (ruleTreeClipboardIsCut)
        {
            ruleTreeClipboardNodes.Clear();
            cutPendingRuleTreeNodePaths.Clear();
            ruleTreeClipboardIsCut = false;
        }

        ruleTreeFlatCacheDirty = true;
        MarkPackageDirtyAndRefreshSO(true);
    }

    private bool MoveRuleTreeNodesToRoot(List<string> movingPaths)
    {
        LogRuleTreeDrag("Enter MoveToRoot | count=" + (movingPaths == null ? 0 : movingPaths.Count));

        if (movingPaths == null || movingPaths.Count == 0)
        {
            LogRuleTreeDrag("MoveToRoot FAILED | empty moving paths");
            return false;
        }

        List<AIBehaviorPackage.AIRuleTreeNode> movingNodes = CaptureMovingNodesByFlatOrder(movingPaths);
        if (movingNodes.Count == 0)
        {
            LogRuleTreeDrag("MoveToRoot FAILED | no moving nodes captured");
            return false;
        }

        bool needsMove = false;
        for (int i = 0; i < movingPaths.Count; i++)
        {
            if (TryGetRuntimeNode(movingPaths[i], out _, out List<AIBehaviorPackage.AIRuleTreeNode> ownerList, out _))
            {
                if (!ReferenceEquals(ownerList, selectedPackage.ruleTree))
                {
                    needsMove = true;
                    break;
                }
            }
        }

        if (!needsMove)
        {
            LogRuleTreeDrag("MoveToRoot FAILED | already in root");
            return false;
        }

        RecordGroupedUndo("Move AI Tree Nodes To Root");

        RemoveRuntimeNodesByPaths(movingPaths);

        int insertIndex = selectedPackage.ruleTree.Count;
        for (int i = 0; i < movingNodes.Count; i++)
            selectedPackage.ruleTree.Insert(insertIndex + i, movingNodes[i]);

        RestoreSelectionByRuntimeNodes(movingNodes);
        cutPendingRuleTreeNodePaths.RemoveAll(x => movingPaths.Contains(x));

        ruleTreeFlatCacheDirty = true;
        bool refreshRightPanel = IsSelectedRuleAffectedByPaths(movingPaths);
        MarkPackageDirtyAndRefreshSO(refreshRightPanel);

        LogRuleTreeDrag("MoveToRoot SUCCESS");
        return true;
    }

    private bool MoveRuleTreeNodesIntoFolder(List<string> movingPaths, string folderPath)
    {
        LogRuleTreeDrag(
            "Enter MoveIntoFolder | targetFolder=" + folderPath +
            " | count=" + (movingPaths == null ? 0 : movingPaths.Count)
        );

        if (movingPaths == null || movingPaths.Count == 0)
        {
            LogRuleTreeDrag("MoveIntoFolder FAILED | empty moving paths");
            return false;
        }

        if (!CanDropIntoFolder(folderPath))
        {
            LogRuleTreeDrag("MoveIntoFolder FAILED | CanDropIntoFolder=false");
            return false;
        }

        if (!TryGetRuntimeNode(folderPath, out AIBehaviorPackage.AIRuleTreeNode folderNode, out _, out _))
        {
            LogRuleTreeDrag("MoveIntoFolder FAILED | target folder not found");
            return false;
        }

        if (folderNode == null || folderNode.nodeType != AIBehaviorPackage.RuleTreeNodeType.Folder)
        {
            LogRuleTreeDrag("MoveIntoFolder FAILED | target is not folder");
            return false;
        }

        List<AIBehaviorPackage.AIRuleTreeNode> movingNodes = CaptureMovingNodesByFlatOrder(movingPaths);
        if (movingNodes.Count == 0)
        {
            LogRuleTreeDrag("MoveIntoFolder FAILED | no moving nodes captured");
            return false;
        }

        RecordGroupedUndo("Move AI Tree Nodes Into Folder");

        RemoveRuntimeNodesByPaths(movingPaths);

        folderNode.expanded = true;
        int insertIndex = folderNode.children.Count;
        for (int i = 0; i < movingNodes.Count; i++)
            folderNode.children.Insert(insertIndex + i, movingNodes[i]);

        RestoreSelectionByRuntimeNodes(movingNodes);
        cutPendingRuleTreeNodePaths.RemoveAll(x => movingPaths.Contains(x));

        ruleTreeFlatCacheDirty = true;
        bool refreshRightPanel = IsSelectedRuleAffectedByPaths(movingPaths);
        MarkPackageDirtyAndRefreshSO(refreshRightPanel);

        LogRuleTreeDrag("MoveIntoFolder SUCCESS | targetFolder=" + folderPath);
        return true;
    }

    private bool ReorderRuleTreeNodes(List<string> movingPaths, string targetPath, bool insertBefore)
    {
        LogRuleTreeDrag(
            "Enter Reorder | target=" + targetPath +
            " | insertBefore=" + insertBefore +
            " | count=" + (movingPaths == null ? 0 : movingPaths.Count)
        );

        if (movingPaths == null || movingPaths.Count == 0 || string.IsNullOrWhiteSpace(targetPath))
        {
            LogRuleTreeDrag("Reorder FAILED | invalid args");
            return false;
        }

        if (!TryGetRuntimeNode(targetPath, out _, out List<AIBehaviorPackage.AIRuleTreeNode> targetOwner, out _))
        {
            LogRuleTreeDrag("Reorder FAILED | target path not found");
            return false;
        }

        List<AIBehaviorPackage.AIRuleTreeNode> movingNodes = CaptureMovingNodesByFlatOrder(movingPaths);
        if (movingNodes.Count == 0)
        {
            LogRuleTreeDrag("Reorder FAILED | no moving nodes captured");
            return false;
        }

        RecordGroupedUndo("Reorder AI Tree Nodes");

        RemoveRuntimeNodesByPaths(movingPaths);

        if (!TryGetRuntimeNode(targetPath, out _, out targetOwner, out int targetIndex))
        {
            LogRuleTreeDrag("Reorder FAILED | target disappeared after remove");
            return false;
        }

        int insertIndex = insertBefore ? targetIndex : targetIndex + 1;
        if (insertIndex < 0)
            insertIndex = 0;
        if (insertIndex > targetOwner.Count)
            insertIndex = targetOwner.Count;

        for (int i = 0; i < movingNodes.Count; i++)
            targetOwner.Insert(insertIndex + i, movingNodes[i]);

        RestoreSelectionByRuntimeNodes(movingNodes);
        cutPendingRuleTreeNodePaths.RemoveAll(x => movingPaths.Contains(x));

        ruleTreeFlatCacheDirty = true;
        bool refreshRightPanel = IsSelectedRuleAffectedByPaths(movingPaths);
        MarkPackageDirtyAndRefreshSO(refreshRightPanel);

        LogRuleTreeDrag(
            "Reorder SUCCESS | target=" + targetPath +
            " | insertBefore=" + insertBefore
        );
        return true;
    }

    private List<AIBehaviorPackage.AIRuleTreeNode> CaptureMovingNodesByFlatOrder(List<string> movingPaths)
    {
        HashSet<string> movingSet = new HashSet<string>(movingPaths);
        List<AIBehaviorPackage.AIRuleTreeNode> result = new List<AIBehaviorPackage.AIRuleTreeNode>();

        for (int i = 0; i < ruleTreeFlatCache.Count; i++)
        {
            if (!movingSet.Contains(ruleTreeFlatCache[i].path))
                continue;

            AIBehaviorPackage.AIRuleTreeNode node = ruleTreeFlatCache[i].node;
            if (node != null)
                result.Add(node);
        }

        return result;
    }

    private void RestoreSelectionByRuntimeNodes(List<AIBehaviorPackage.AIRuleTreeNode> nodes)
    {
        ClearRuleTreeSelection();
        ruleTreeFlatCacheDirty = true;
        RebuildRuleTreeFlatCacheIfNeeded();

        for (int i = 0; i < nodes.Count; i++)
        {
            string foundPath = FindPathByRuntimeNode(nodes[i]);
            if (!string.IsNullOrWhiteSpace(foundPath))
                selectedRuleNodePaths.Add(foundPath);
        }

        selectedRuleNodePath = selectedRuleNodePaths.LastOrDefault();
        rangeAnchorRuleNodePath = selectedRuleNodePath;
    }

    private string FindPathByRuntimeNode(AIBehaviorPackage.AIRuleTreeNode node)
    {
        if (node == null || selectedPackage == null)
            return "";

        return FindPathByRuntimeNodeRecursive(selectedPackage.ruleTree, node, "");
    }

    private string FindPathByRuntimeNodeRecursive(List<AIBehaviorPackage.AIRuleTreeNode> nodes, AIBehaviorPackage.AIRuleTreeNode target, string prefix)
    {
        if (nodes == null)
            return "";

        for (int i = 0; i < nodes.Count; i++)
        {
            string path = string.IsNullOrWhiteSpace(prefix) ? i.ToString() : prefix + "/children/" + i;
            if (ReferenceEquals(nodes[i], target))
                return path;

            if (nodes[i] != null && nodes[i].children != null && nodes[i].children.Count > 0)
            {
                string child = FindPathByRuntimeNodeRecursive(nodes[i].children, target, path);
                if (!string.IsNullOrWhiteSpace(child))
                    return child;
            }
        }

        return "";
    }

    private AIBehaviorPackage.AIRuleTreeNode CloneRuleTreeNode(AIBehaviorPackage.AIRuleTreeNode src)
    {
        if (src == null)
            return null;

        AIBehaviorPackage.AIRuleTreeNode dst = new AIBehaviorPackage.AIRuleTreeNode
        {
            nodeType = src.nodeType,
            displayName = src.displayName,
            expanded = src.expanded,
            enabled = src.enabled,
            note = src.note,
            ruleData = CloneAIRule(src.ruleData),
            children = new List<AIBehaviorPackage.AIRuleTreeNode>()
        };

        if (src.children != null)
        {
            for (int i = 0; i < src.children.Count; i++)
            {
                AIBehaviorPackage.AIRuleTreeNode childClone = CloneRuleTreeNode(src.children[i]);
                if (childClone != null)
                    dst.children.Add(childClone);
            }
        }

        return dst;
    }

    private AIBehaviorPackage.AIRule CloneAIRule(AIBehaviorPackage.AIRule src)
    {
        if (src == null)
            return null;

        AIBehaviorPackage.AIRule dst = new AIBehaviorPackage.AIRule
        {
            enabled = src.enabled,
            ruleName = src.ruleName,
            note = src.note,
            priority = src.priority,
            cooldownSeconds = src.cooldownSeconds,
            requireAllConditions = src.requireAllConditions,
            motive = src.motive,
            conditions = new List<AIBehaviorPackage.AIConditionUnit>(),
            actions = new List<AIBehaviorPackage.AIActionUnit>(),
            sentenceRoot = new AIBehaviorPackage.LogicSentenceRoot()
        };

        if (src.conditions != null)
        {
            for (int i = 0; i < src.conditions.Count; i++)
            {
                var c = src.conditions[i];
                if (c == null) continue;

                dst.conditions.Add(new AIBehaviorPackage.AIConditionUnit
                {
                    enabled = c.enabled,
                    conditionType = c.conditionType,
                    valueA = c.valueA,
                    note = c.note
                });
            }
        }

        if (src.actions != null)
        {
            for (int i = 0; i < src.actions.Count; i++)
            {
                var a = src.actions[i];
                if (a == null) continue;

                dst.actions.Add(new AIBehaviorPackage.AIActionUnit
                {
                    enabled = a.enabled,
                    actionType = a.actionType,
                    valueA = a.valueA,
                    nextAIPackage = a.nextAIPackage,
                    note = a.note
                });
            }
        }

        if (src.sentenceRoot != null)
        {
            dst.sentenceRoot.conditionGroup.items = CloneSentenceList(src.sentenceRoot.conditionGroup.items);
            dst.sentenceRoot.motiveGroup.items = CloneSentenceList(src.sentenceRoot.motiveGroup.items);
            dst.sentenceRoot.actionGroup.items = CloneSentenceList(src.sentenceRoot.actionGroup.items);
        }

        return dst;
    }

    private List<LogicSentenceInstance> CloneSentenceList(List<LogicSentenceInstance> src)
    {
        List<LogicSentenceInstance> dst = new List<LogicSentenceInstance>();
        if (src == null)
            return dst;

        for (int i = 0; i < src.Count; i++)
        {
            LogicSentenceInstance cloned = CloneSentenceInstance(src[i]);
            if (cloned != null)
                dst.Add(cloned);
        }

        return dst;
    }

    private LogicSentenceInstance CloneSentenceInstance(LogicSentenceInstance src)
    {
        if (src == null)
            return null;

        LogicSentenceInstance dst = new LogicSentenceInstance
        {
            templateId = src.templateId,
            enabled = src.enabled,
            slotAssignments = new List<LogicSlotAssignment>(),
            conditionChildren = new List<LogicSentenceInstance>(),
            thenChildren = new List<LogicSentenceInstance>(),
            elseChildren = new List<LogicSentenceInstance>(),
            bodyChildren = new List<LogicSentenceInstance>()
        };

        if (src.slotAssignments != null)
        {
            for (int i = 0; i < src.slotAssignments.Count; i++)
            {
                LogicSlotAssignment a = src.slotAssignments[i];
                if (a == null) continue;

                dst.slotAssignments.Add(new LogicSlotAssignment
                {
                    slotId = a.slotId,
                    value = CloneLogicSlotValue(a.value)
                });
            }
        }

        if (src.conditionChildren != null)
        {
            for (int i = 0; i < src.conditionChildren.Count; i++)
            {
                LogicSentenceInstance child = CloneSentenceInstance(src.conditionChildren[i]);
                if (child != null) dst.conditionChildren.Add(child);
            }
        }

        if (src.thenChildren != null)
        {
            for (int i = 0; i < src.thenChildren.Count; i++)
            {
                LogicSentenceInstance child = CloneSentenceInstance(src.thenChildren[i]);
                if (child != null) dst.thenChildren.Add(child);
            }
        }

        if (src.elseChildren != null)
        {
            for (int i = 0; i < src.elseChildren.Count; i++)
            {
                LogicSentenceInstance child = CloneSentenceInstance(src.elseChildren[i]);
                if (child != null) dst.elseChildren.Add(child);
            }
        }

        if (src.bodyChildren != null)
        {
            for (int i = 0; i < src.bodyChildren.Count; i++)
            {
                LogicSentenceInstance child = CloneSentenceInstance(src.bodyChildren[i]);
                if (child != null) dst.bodyChildren.Add(child);
            }
        }

        return dst;
    }

    private LogicSlotValue CloneLogicSlotValue(LogicSlotValue src)
    {
        if (src == null)
            return new LogicSlotValue();

        return new LogicSlotValue
        {
            valueType = src.valueType,
            sourceType = src.sourceType,
            boolValue = src.boolValue,
            intValue = src.intValue,
            floatValue = src.floatValue,
            stringValue = src.stringValue,
            enumValue = src.enumValue,
            variableKey = src.variableKey,
            contextKey = src.contextKey,
            assetReference = src.assetReference,
            sceneObjectId = src.sceneObjectId,
            sceneObjectName = src.sceneObjectName
        };
    }

    private bool TryGetRuntimeNode(
        string nodePath,
        out AIBehaviorPackage.AIRuleTreeNode node,
        out List<AIBehaviorPackage.AIRuleTreeNode> ownerList,
        out int indexInOwner)
    {
        node = null;
        ownerList = null;
        indexInOwner = -1;

        if (selectedPackage == null || string.IsNullOrWhiteSpace(nodePath))
            return false;

        List<AIBehaviorPackage.AIRuleTreeNode> currentList = selectedPackage.ruleTree;
        string[] parts = nodePath.Split('/');
        int cursor = 0;

        while (cursor < parts.Length)
        {
            if (!int.TryParse(parts[cursor], out int index))
                return false;

            if (currentList == null || index < 0 || index >= currentList.Count)
                return false;

            ownerList = currentList;
            indexInOwner = index;
            node = currentList[index];
            cursor++;

            if (cursor >= parts.Length)
                return true;

            if (parts[cursor] != "children")
                return false;

            cursor++;
            currentList = node.children;
        }

        return node != null;
    }

    private void HandleRuleTreeSelectionClick(string nodePath, Event e)
    {
        bool ctrl = e.control || e.command;
        bool shift = e.shift;

        if (shift)
        {
            AddRuleTreeRangeSelection(nodePath);
            return;
        }

        if (ctrl)
        {
            if (selectedRuleNodePaths.Contains(nodePath))
            {
                selectedRuleNodePaths.Remove(nodePath);
                if (selectedRuleNodePath == nodePath)
                    selectedRuleNodePath = selectedRuleNodePaths.LastOrDefault();
            }
            else
            {
                selectedRuleNodePaths.Add(nodePath);
                selectedRuleNodePath = nodePath;
            }

            rangeAnchorRuleNodePath = nodePath;
            ForceRefreshRuleEditorPanel(true);
            return;
        }

        SetSingleRuleTreeSelection(nodePath);
        ForceRefreshRuleEditorPanel(true);
    }

    private void HandleRuleTreeRightClickSelection(string nodePath)
    {
        if (!selectedRuleNodePaths.Contains(nodePath))
            SetSingleRuleTreeSelection(nodePath);
        else
            selectedRuleNodePath = nodePath;

        ForceRefreshRuleEditorPanel(true);
    }

    private void SetSingleRuleTreeSelection(string nodePath)
    {
        selectedRuleNodePaths.Clear();
        if (!string.IsNullOrWhiteSpace(nodePath))
            selectedRuleNodePaths.Add(nodePath);

        selectedRuleNodePath = nodePath;
        rangeAnchorRuleNodePath = nodePath;
        ForceRefreshRuleEditorPanel(true);
    }

    private void ClearRuleTreeSelection()
    {
        selectedRuleNodePaths.Clear();
        selectedRuleNodePath = "";
        rangeAnchorRuleNodePath = "";
        ForceRefreshRuleEditorPanel(true);
    }

    private void AddRuleTreeRangeSelection(string endPath)
    {
        if (string.IsNullOrWhiteSpace(endPath))
            return;

        RebuildRuleTreeFlatCacheIfNeeded();

        List<string> visiblePaths = ruleTreeFlatCache.Select(x => x.path).ToList();

        string startPath = string.IsNullOrWhiteSpace(rangeAnchorRuleNodePath) ? selectedRuleNodePath : rangeAnchorRuleNodePath;
        if (string.IsNullOrWhiteSpace(startPath))
        {
            SetSingleRuleTreeSelection(endPath);
            return;
        }

        int startIndex = visiblePaths.IndexOf(startPath);
        int endIndex = visiblePaths.IndexOf(endPath);

        if (startIndex < 0 || endIndex < 0)
        {
            SetSingleRuleTreeSelection(endPath);
            return;
        }

        selectedRuleNodePaths.Clear();

        int min = Mathf.Min(startIndex, endIndex);
        int max = Mathf.Max(startIndex, endIndex);

        for (int i = min; i <= max; i++)
            selectedRuleNodePaths.Add(visiblePaths[i]);

        selectedRuleNodePath = endPath;
        ForceRefreshRuleEditorPanel(true);
    }

    private List<string> GetOrderedSelectedPathsForDelete()
    {
        return selectedRuleNodePaths
            .OrderByDescending(GetNodePathDepth)
            .ThenByDescending(x => x)
            .ToList();
    }

    private List<string> GetOrderedSelectedPathsForMove()
    {
        return FilterTopLevelSelectedPaths(selectedRuleNodePaths)
            .OrderBy(x => GetFlatIndex(x))
            .ToList();
    }

    private List<string> GetOrderedSelectedPathsForCopyPaste()
    {
        return FilterTopLevelSelectedPaths(selectedRuleNodePaths)
            .OrderBy(x => GetFlatIndex(x))
            .ToList();
    }

    private List<string> FilterTopLevelSelectedPaths(IEnumerable<string> input)
    {
        List<string> list = input.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList();
        List<string> result = new List<string>();

        for (int i = 0; i < list.Count; i++)
        {
            bool isChildOfSelected = false;
            for (int j = 0; j < list.Count; j++)
            {
                if (i == j)
                    continue;

                if (list[i].StartsWith(list[j] + "/"))
                {
                    isChildOfSelected = true;
                    break;
                }
            }

            if (!isChildOfSelected)
                result.Add(list[i]);
        }

        return result;
    }

    private int GetFlatIndex(string path)
    {
        for (int i = 0; i < ruleTreeFlatCache.Count; i++)
        {
            if (ruleTreeFlatCache[i].path == path)
                return i;
        }

        return int.MaxValue;
    }

    private int GetNodePathDepth(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;
        return path.Split('/').Length;
    }

    private void RemoveRuntimeNodeByPath(string path)
    {
        if (!TryGetRuntimeNode(path, out _, out List<AIBehaviorPackage.AIRuleTreeNode> ownerList, out int index))
            return;

        if (ownerList == null || index < 0 || index >= ownerList.Count)
            return;

        ownerList.RemoveAt(index);
    }

    private void RemoveRuntimeNodesByPaths(IEnumerable<string> paths)
    {
        List<string> ordered = paths
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .OrderByDescending(GetNodePathDepth)
            .ThenByDescending(x => x)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
            RemoveRuntimeNodeByPath(ordered[i]);
    }

    private SerializedProperty GetSelectedRuleDataProperty()
    {
        if (selectedSO == null || string.IsNullOrWhiteSpace(selectedRuleNodePath))
            return null;

        if (selectedRuleNodePaths.Count != 1)
            return null;

        if (!TryGetRuleTreeNodeProperty(selectedRuleNodePath, out SerializedProperty nodeProp))
            return null;

        SerializedProperty nodeTypeProp = nodeProp.FindPropertyRelative("nodeType");
        bool isRule = nodeTypeProp != null &&
                      nodeTypeProp.enumValueIndex == (int)AIBehaviorPackage.RuleTreeNodeType.Rule;

        if (!isRule)
            return null;

        return nodeProp.FindPropertyRelative("ruleData");
    }

    private bool TryGetRuleTreeNodeProperty(string nodePath, out SerializedProperty nodeProp)
    {
        nodeProp = null;

        if (selectedSO == null || string.IsNullOrWhiteSpace(nodePath))
            return false;

        SerializedProperty currentArray = selectedSO.FindProperty("ruleTree");
        if (currentArray == null)
            return false;

        string[] parts = nodePath.Split('/');
        int cursor = 0;

        while (cursor < parts.Length)
        {
            if (!int.TryParse(parts[cursor], out int index))
                return false;

            if (index < 0 || index >= currentArray.arraySize)
                return false;

            nodeProp = currentArray.GetArrayElementAtIndex(index);
            cursor++;

            if (cursor >= parts.Length)
                return true;

            if (parts[cursor] != "children")
                return false;

            cursor++;
            currentArray = nodeProp.FindPropertyRelative("children");
            if (currentArray == null)
                return false;
        }

        return nodeProp != null;
    }

    private void ClampRuleTreeSelection()
    {
        if (selectedRuleNodePaths.Count > 0)
            selectedRuleNodePaths.RemoveWhere(x => !TryGetRuntimeNode(x, out _, out _, out _));

        if (!string.IsNullOrWhiteSpace(selectedRuleNodePath) &&
            !TryGetRuntimeNode(selectedRuleNodePath, out _, out _, out _))
        {
            selectedRuleNodePath = selectedRuleNodePaths.LastOrDefault() ?? "";
        }

        if (string.IsNullOrWhiteSpace(selectedRuleNodePath) && selectedRuleNodePaths.Count > 0)
            selectedRuleNodePath = selectedRuleNodePaths.Last();
    }

    private bool GetRuntimeNodeEnabled(AIBehaviorPackage.AIRuleTreeNode node)
    {
        if (node == null)
            return false;

        if (node.nodeType == AIBehaviorPackage.RuleTreeNodeType.Folder)
            return node.enabled;

        if (node.ruleData != null)
            return node.ruleData.enabled;

        return node.enabled;
    }

    private bool IsCommentTemplate(LogicSentenceTemplate template)
    {
        if (template == null)
            return false;

        string id = template.templateId ?? "";
        string title = template.displayName ?? "";

        id = id.ToLowerInvariant();
        title = title.ToLowerInvariant();

        return id.Contains("comment") ||
               id.Contains("note") ||
               title.Contains("注释") ||
               title.Contains("comment") ||
               title.Contains("note");
    }

    private LogicSlotAssignment FindRuntimeAssignment(List<LogicSlotAssignment> assignments, string slotId)
    {
        if (assignments == null || string.IsNullOrWhiteSpace(slotId))
            return null;

        for (int i = 0; i < assignments.Count; i++)
        {
            LogicSlotAssignment item = assignments[i];
            if (item != null && item.slotId == slotId)
                return item;
        }

        return null;
    }

    private EntryState EvaluateSentenceListState(List<LogicSentenceInstance> items)
    {
        if (items == null || items.Count == 0)
            return EntryState.Valid;

        bool hasWarning = false;

        for (int i = 0; i < items.Count; i++)
        {
            EntryState state = EvaluateSentenceStateRecursive(items[i]);
            if (state == EntryState.Error)
                return EntryState.Error;

            if (state == EntryState.Warning)
                hasWarning = true;
        }

        return hasWarning ? EntryState.Warning : EntryState.Valid;
    }

    private EntryState EvaluateSentenceStateRecursive(LogicSentenceInstance sentence)
    {
        if (sentence == null)
            return EntryState.Warning;

        LogicSentenceTemplate template = AILogicSentenceTemplateLibrary.GetTemplateById(sentence.templateId);
        if (template == null)
            return EntryState.Error;

        if (!sentence.enabled)
            return EntryState.Valid;

        if (IsCommentTemplate(template))
            return EntryState.Valid;

        if (template.slots != null)
        {
            for (int i = 0; i < template.slots.Count; i++)
            {
                LogicSentenceTemplate.SlotDefinition slotDef = template.slots[i];
                LogicSlotAssignment assignment = FindRuntimeAssignment(sentence.slotAssignments, slotDef.slotId);

                if (assignment == null)
                {
                    if (slotDef.required)
                        return EntryState.Error;
                    continue;
                }

                ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(assignment.value.valueType);
                if (handler == null)
                    return EntryState.Error;

                bool valid = handler.IsValid(slotDef, assignment.value);
                if (!valid)
                    return EntryState.Error;
            }
        }

        if (sentence.conditionChildren != null)
        {
            for (int i = 0; i < sentence.conditionChildren.Count; i++)
            {
                EntryState state = EvaluateSentenceStateRecursive(sentence.conditionChildren[i]);
                if (state == EntryState.Error)
                    return EntryState.Error;
            }
        }

        if (sentence.thenChildren != null)
        {
            for (int i = 0; i < sentence.thenChildren.Count; i++)
            {
                EntryState state = EvaluateSentenceStateRecursive(sentence.thenChildren[i]);
                if (state == EntryState.Error)
                    return EntryState.Error;
            }
        }

        if (sentence.elseChildren != null)
        {
            for (int i = 0; i < sentence.elseChildren.Count; i++)
            {
                EntryState state = EvaluateSentenceStateRecursive(sentence.elseChildren[i]);
                if (state == EntryState.Error)
                    return EntryState.Error;
            }
        }

        if (sentence.bodyChildren != null)
        {
            for (int i = 0; i < sentence.bodyChildren.Count; i++)
            {
                EntryState state = EvaluateSentenceStateRecursive(sentence.bodyChildren[i]);
                if (state == EntryState.Error)
                    return EntryState.Error;
            }
        }

        return EntryState.Valid;
    }

    private EntryState GetRuntimeRuleState(AIBehaviorPackage.AIRuleTreeNode node)
    {
        if (node == null || node.nodeType == AIBehaviorPackage.RuleTreeNodeType.Folder)
            return EntryState.Valid;

        AIBehaviorPackage.AIRule rule = node.ruleData;
        if (rule == null || rule.sentenceRoot == null)
            return EntryState.Warning;

        bool hasEvent =
            rule.sentenceRoot.motiveGroup != null &&
            rule.sentenceRoot.motiveGroup.items != null &&
            rule.sentenceRoot.motiveGroup.items.Count > 0 &&
            rule.sentenceRoot.motiveGroup.items[0] != null &&
            !string.IsNullOrWhiteSpace(rule.sentenceRoot.motiveGroup.items[0].templateId);

        bool hasCondition =
            rule.sentenceRoot.conditionGroup != null &&
            rule.sentenceRoot.conditionGroup.items != null &&
            rule.sentenceRoot.conditionGroup.items.Count > 0;

        bool hasAction =
            rule.sentenceRoot.actionGroup != null &&
            rule.sentenceRoot.actionGroup.items != null &&
            rule.sentenceRoot.actionGroup.items.Count > 0;

        if (!hasEvent || !hasCondition || !hasAction)
            return EntryState.Warning;

        EntryState motiveState = EvaluateSentenceListState(rule.sentenceRoot.motiveGroup.items);
        if (motiveState == EntryState.Error)
            return EntryState.Error;

        EntryState conditionState = EvaluateSentenceListState(rule.sentenceRoot.conditionGroup.items);
        if (conditionState == EntryState.Error)
            return EntryState.Error;

        EntryState actionState = EvaluateSentenceListState(rule.sentenceRoot.actionGroup.items);
        if (actionState == EntryState.Error)
            return EntryState.Error;

        if (motiveState == EntryState.Warning ||
            conditionState == EntryState.Warning ||
            actionState == EntryState.Warning)
            return EntryState.Warning;

        return EntryState.Valid;
    }

    private bool IsSelectedRuleAffectedByPaths(List<string> paths)
    {
        if (string.IsNullOrWhiteSpace(selectedRuleNodePath) || paths == null || paths.Count == 0)
            return false;

        for (int i = 0; i < paths.Count; i++)
        {
            string p = paths[i];
            if (selectedRuleNodePath == p ||
                selectedRuleNodePath.StartsWith(p + "/") ||
                p.StartsWith(selectedRuleNodePath + "/"))
                return true;
        }

        return false;
    }

    private void MarkPackageDirtyAndRefreshSO(bool refreshRightPanel)
    {
        EditorUtility.SetDirty(selectedPackage);

        if (refreshRightPanel)
        {
            if (selectedSO == null || selectedSO.targetObject != selectedPackage)
                selectedSO = new SerializedObject(selectedPackage);
            else
                selectedSO.Update();

            logicPanel.MarkCacheDirty();
            rightPanelDirty = true;
        }

        ruleTreeFlatCacheDirty = true;
        RepaintHost();
    }

    private void RepaintHost()
    {
        ruleTreeFlatCacheDirty = true;
        Context?.Repaint();
        GUI.changed = true;
    }

    private string GetRuntimeNodeTitle(AIBehaviorPackage.AIRuleTreeNode node)
    {
        if (node == null)
            return "";

        if (node.nodeType == AIBehaviorPackage.RuleTreeNodeType.Folder)
            return string.IsNullOrWhiteSpace(node.displayName) ? "新文件夹" : node.displayName;

        if (node.ruleData != null && !string.IsNullOrWhiteSpace(node.ruleData.ruleName))
            return node.ruleData.ruleName;

        return string.IsNullOrWhiteSpace(node.displayName) ? "新规则" : node.displayName;
    }

    private void CreateNewAIPackage(string folderPath)
    {
        EnsureFolderExists(folderPath);

        AIBehaviorPackage asset = ScriptableObject.CreateInstance<AIBehaviorPackage>();
        asset.aiId = GenerateUniqueAIId("new_ai");
        asset.displayName = "新AI行为包";

        string path = AssetDatabase.GenerateUniqueAssetPath(folderPath.TrimEnd('/') + "/AI_NewBehaviorPackage.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Refresh();
        SelectPackage(asset);
        selectedFolderPath = folderPath;
    }

    private void DeletePackage(AIBehaviorPackage pkg)
    {
        if (pkg == null)
            return;

        string path = AssetDatabase.GetAssetPath(pkg);
        if (string.IsNullOrEmpty(path))
            return;

        bool ok = EditorUtility.DisplayDialog(
            "删除 AI 行为包",
            $"确定删除 AI 行为包：\n{pkg.name}\n\n此操作会删除资源文件。",
            "删除",
            "取消"
        );
        if (!ok)
            return;

        if (selectedPackage == pkg)
            selectedPackage = null;

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Refresh();
    }

    private void SelectPackage(AIBehaviorPackage pkg)
    {
        selectedPackage = pkg;
        selectedFolderKey = "";
        selectedSO = null;
        ClearRuleTreeSelection();
        ruleTreeFlatCacheDirty = true;
        rightPanelDirty = true;
        Context.Repaint();
    }

    private void SelectFolder(string folderKey, string folderPath)
    {
        selectedFolderKey = folderKey;
        selectedFolderPath = folderPath;
        selectedPackage = null;
        selectedSO = null;
        ClearRuleTreeSelection();
        rightPanelDirty = true;
        Context.Repaint();
    }

    private string GetCurrentCreateFolder()
    {
        if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            return selectedFolderPath;

        if (selectedPackage != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(selectedPackage);
            string folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(folder))
                return folder;
        }

        return DefaultAICreateFolder;
    }

    private string GenerateUniqueAIId(string baseId)
    {
        string safeBase = SanitizeId(string.IsNullOrWhiteSpace(baseId) ? "ai" : baseId);
        if (string.IsNullOrWhiteSpace(safeBase))
            safeBase = "ai";

        HashSet<string> existing = new HashSet<string>(
            aiPackages.Where(x => x != null && !string.IsNullOrWhiteSpace(x.aiId)).Select(x => x.aiId)
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

    private string SanitizeId(string value)
    {
        string s = value.Trim().ToLower().Replace(" ", "_");
        return new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
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

    private string GetRelativeFolder(string assetPath)
    {
        string folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/") ?? "";
        if (folder.StartsWith(AIRootFolder))
        {
            string relative = folder.Substring(AIRootFolder.Length).TrimStart('/');
            return string.IsNullOrEmpty(relative) ? "未分类" : relative;
        }

        return "未分类";
    }

    private bool IsFolderChainVisible(string folder)
    {
        string[] parts = folder.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
        string current = "";

        for (int i = 0; i < parts.Length; i++)
        {
            current = string.IsNullOrEmpty(current) ? parts[i] : current + "/" + parts[i];
            if (!GetFolderExpanded(current))
                return false;
        }

        return true;
    }

    private bool GetFolderExpanded(string key)
    {
        if (!folderExpanded.TryGetValue(key, out bool expanded))
        {
            expanded = true;
            folderExpanded[key] = true;
        }

        return expanded;
    }

    private void DrawAssetHeader(string title, string typeLabel, string path, string id)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(typeLabel, EditorStyles.miniLabel);
        EditorGUILayout.Space(2f);
        DrawReadonlyRow("资源路径", path);
        if (!string.IsNullOrWhiteSpace(id))
            DrawReadonlyRow("AI ID", id);
        EditorGUILayout.EndVertical();
    }

    private void DrawPingButtons(Object target)
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

    private void DrawUniqueIdWarning(AIBehaviorPackage pkg)
    {
        if (pkg == null || string.IsNullOrWhiteSpace(pkg.aiId))
            return;

        int duplicateCount = aiPackages.Count(x => x != null && x.aiId == pkg.aiId);
        if (duplicateCount > 1)
        {
            EditorGUILayout.HelpBox(
                $"警告：当前 aiId \"{pkg.aiId}\" 与其他 AI 行为包重复。建议保持唯一。",
                MessageType.Error
            );
        }
    }

    private void ShowFolderContextMenu(TreeNode node)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("新建 AI 包"), false, () => CreateNewAIPackage(node.assetFolderPath));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("在 Project 中定位"), false, () =>
        {
            Object folderObj = AssetDatabase.LoadAssetAtPath<Object>(node.assetFolderPath);
            if (folderObj != null)
            {
                Selection.activeObject = folderObj;
                EditorGUIUtility.PingObject(folderObj);
            }
        });
        menu.ShowAsContext();
    }

    private void ShowPackageContextMenu(AIBehaviorPackage pkg)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("删除"), false, () => DeletePackage(pkg));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("在 Project 中定位"), false, () =>
        {
            Selection.activeObject = pkg;
            EditorGUIUtility.PingObject(pkg);
        });
        menu.ShowAsContext();
    }

    private void DrawReadonlyRow(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(value) ? "-" : value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRow(string label, SerializedProperty property, bool multiline = false)
    {
        if (property == null)
        {
            EditorGUILayout.HelpBox($"字段 {label} 不存在。", MessageType.Warning);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));

        EditorGUI.BeginChangeCheck();
        if (multiline && property.propertyType == SerializedPropertyType.String)
            property.stringValue = EditorGUILayout.TextArea(property.stringValue, GUILayout.MinHeight(54f));
        else
            EditorGUILayout.PropertyField(property, GUIContent.none, true);

        if (EditorGUI.EndChangeCheck())
        {
            RecordUndo($"Edit AI {label}");
            selectedSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(selectedPackage);
            ruleTreeFlatCacheDirty = true;
            rightPanelDirty = true;
            logicPanel.MarkCacheDirty();
        }

        EditorGUILayout.EndHorizontal();
    }
}
