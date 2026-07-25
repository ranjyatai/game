using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonUnitDefinitionAssetListPanel
{
    private class TreeNode
    {
        public string displayName;
        public string fullKey;
        public string assetFolderPath;
        public int depth;
        public bool isFolder;
        public UnitDefinition unit;
        public Rect lastRect;
    }

    private enum UnitClipboardMode
    {
        None,
        Copy,
        Cut
    }

    private const float LeftTitleRowHeight = 22f;
    private const float LeftToolbarRowHeight = 24f;
    private const float LeftSearchRowHeight = 22f;
    private const float LeftRowGap = 6f;
    private const float LeftContainerPadding = 8f;
    private const float LeftListRowHeight = 24f;

    private readonly SkyPrisonUnitDefinitionPage page;
    private readonly Dictionary<string, bool> folderExpanded = new Dictionary<string, bool>();
    private readonly List<TreeNode> visibleNodes = new List<TreeNode>();

    private string search = "";
    private string selectedFolderPath = SkyPrisonUnitDefinitionPage.DefaultUnitCreateFolder;
    private string selectedFolderKey = "";
    private Vector2 listScroll;

    private UnitDefinition clipboardUnitDefinition;
    private UnitClipboardMode clipboardMode = UnitClipboardMode.None;

    public SkyPrisonUnitDefinitionAssetListPanel(SkyPrisonUnitDefinitionPage page)
    {
        this.page = page;
    }

    public void OnRefresh()
    {
        string selectedPath = page.SelectedUnitDefinition != null ? AssetDatabase.GetAssetPath(page.SelectedUnitDefinition) : "";
        if (!string.IsNullOrEmpty(selectedPath))
        {
            string folder = Path.GetDirectoryName(selectedPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(folder))
                selectedFolderPath = folder;
        }
    }

    public void Draw()
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

        DrawLeftLabelRow(titleRect, "单位定义列表");
        DrawToolbarRow(toolbarRect);
        DrawSearchRow(searchRect);

        BuildTree();
        DrawTreeContainer(containerRect);
    }

    public void HandlePostGUI()
    {
        HandleDragAndDrop();
    }

    public void CopyUnit(UnitDefinition unit)
    {
        clipboardUnitDefinition = unit;
        clipboardMode = UnitClipboardMode.Copy;
    }

    public void CutUnit(UnitDefinition unit)
    {
        clipboardUnitDefinition = unit;
        clipboardMode = UnitClipboardMode.Cut;
    }

    public bool TryPasteClipboardToCurrentFolder()
    {
        if (clipboardUnitDefinition == null)
            return false;

        PasteClipboardUnitToFolder(GetCurrentCreateFolder());
        return true;
    }

    private void DrawLeftLabelRow(Rect rect, string label)
    {
        GUI.Label(rect, label, EditorStyles.boldLabel);
    }

    private void DrawToolbarRow(Rect rect)
    {
        const float buttonSize = 20f;
        const float gap = 4f;

        float y = rect.y + (rect.height - buttonSize) * 0.5f;
        float right = rect.xMax;

        Rect refreshRect = new Rect(right - buttonSize, y, buttonSize, buttonSize);
        Rect minusRect = new Rect(refreshRect.x - gap - buttonSize, y, buttonSize, buttonSize);
        Rect plusRect = new Rect(minusRect.x - gap - buttonSize, y, buttonSize, buttonSize);

        if (DrawToolButton(plusRect, "+", "新建单位定义"))
            CreateNewUnitDefinition(GetCurrentCreateFolder());

        using (new EditorGUI.DisabledScope(page.SelectedUnitDefinition == null))
        {
            if (DrawToolButton(minusRect, "-", "删除当前单位定义"))
                DeleteUnitDefinition(page.SelectedUnitDefinition);
        }

        if (DrawToolButton(refreshRect, "↻", "刷新"))
            page.Refresh();
    }

    private void DrawSearchRow(Rect rect)
    {
        string newSearch = EditorGUI.TextField(rect, search);
        if (newSearch != search)
        {
            search = newSearch;
            listScroll = Vector2.zero;
        }
    }

    private void DrawTreeContainer(Rect rect)
    {
        EditorGUI.DrawRect(rect, page.LeftTopBg);
        page.DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.06f));

        Rect viewRect = new Rect(
            rect.x + LeftContainerPadding,
            rect.y + LeftContainerPadding,
            rect.width - LeftContainerPadding * 2f,
            rect.height - LeftContainerPadding * 2f);

        float contentHeight = Mathf.Max(viewRect.height, visibleNodes.Count * LeftListRowHeight);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        listScroll = GUI.BeginScrollView(viewRect, listScroll, contentRect, false, true);

        for (int i = 0; i < visibleNodes.Count; i++)
        {
            TreeNode node = visibleNodes[i];
            Rect rowRect = new Rect(0f, i * LeftListRowHeight, contentRect.width, LeftListRowHeight);
            node.lastRect = rowRect;

            if (node.isFolder)
                DrawFolderNode(rowRect, node);
            else
                DrawUnitNode(rowRect, node);
        }

        if (visibleNodes.Count == 0)
            GUI.Label(new Rect(4f, 2f, contentRect.width - 8f, 22f), "没有匹配的单位定义", EditorStyles.miniLabel);

        GUI.EndScrollView();
    }

    private void BuildTree()
    {
        visibleNodes.Clear();
        Dictionary<string, List<UnitDefinition>> folderToUnits = new Dictionary<string, List<UnitDefinition>>();

        foreach (UnitDefinition unit in page.UnitDefinitions)
        {
            if (unit == null)
                continue;

            string compareName = string.IsNullOrWhiteSpace(unit.displayName) ? unit.name : unit.displayName;
            string compareId = string.IsNullOrWhiteSpace(unit.unitId) ? "" : unit.unitId;

            if (!string.IsNullOrWhiteSpace(search))
            {
                string keyword = search.Trim().ToLowerInvariant();
                bool match =
                    compareName.ToLowerInvariant().Contains(keyword) ||
                    compareId.ToLowerInvariant().Contains(keyword) ||
                    unit.name.ToLowerInvariant().Contains(keyword);

                if (!match)
                    continue;
            }

            string path = AssetDatabase.GetAssetPath(unit).Replace("\\", "/");
            string relativeFolder = GetRelativeFolder(path);

            if (!folderToUnits.ContainsKey(relativeFolder))
                folderToUnits.Add(relativeFolder, new List<UnitDefinition>());

            folderToUnits[relativeFolder].Add(unit);
        }

        List<string> allFolders = folderToUnits.Keys
            .OrderBy(GetFolderSortPriority)
            .ThenBy(GetFolderPrimaryName)
            .ThenBy(x => x)
            .ToList();

        HashSet<string> addedFolders = new HashSet<string>();

        foreach (string folder in allFolders)
        {
            string[] parts = folder.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
            string current = "";
            string currentAssetPath = SkyPrisonUnitDefinitionPage.UnitRootFolder;

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

            foreach (UnitDefinition unit in folderToUnits[folder]
                         .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
                         .ThenBy(x => x.name))
            {
                visibleNodes.Add(new TreeNode
                {
                    displayName = string.IsNullOrWhiteSpace(unit.displayName) ? unit.name : unit.displayName,
                    fullKey = folder + "/" + unit.name,
                    assetFolderPath = SkyPrisonUnitDefinitionPage.UnitRootFolder.TrimEnd('/') + "/" + folder,
                    depth = parts.Length,
                    isFolder = false,
                    unit = unit
                });
            }
        }
    }

    private int GetFolderSortPriority(string folder)
    {
        string top = GetFolderPrimaryName(folder);

        if (top == "Standard")
            return 0;

        if (top == "Custom")
            return 1;

        return 10;
    }

    private string GetFolderPrimaryName(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return "";

        string[] parts = folder.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : folder;
    }

    private void DrawFolderNode(Rect rowRect, TreeNode node)
    {
        bool isSelected = selectedFolderKey == node.fullKey && page.SelectedUnitDefinition == null;
        bool hover = rowRect.Contains(Event.current.mousePosition);

        if (isSelected)
        {
            EditorGUI.DrawRect(rowRect, page.SelectedFolderBlue);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), page.AccentBlue);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.04f));
        }

        float indent = node.depth * 14f;
        Rect foldoutRect = new Rect(rowRect.x + indent + 4f, rowRect.y, 18f, rowRect.height);
        Rect labelRect = new Rect(rowRect.x + indent + 20f, rowRect.y, rowRect.width - indent - 20f, rowRect.height);

        bool expanded = GetFolderExpanded(node.fullKey);
        bool newExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, false);
        if (newExpanded != expanded)
            folderExpanded[node.fullKey] = newExpanded;

        GUIStyle style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = isSelected ? Color.white : new Color(0.88f, 0.88f, 0.90f, 1f) }
        };
        GUI.Label(labelRect, node.displayName, style);

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

    private void DrawUnitNode(Rect rowRect, TreeNode node)
    {
        bool isSelected = page.SelectedUnitDefinition == node.unit;
        bool hover = rowRect.Contains(Event.current.mousePosition);

        if (isSelected)
        {
            EditorGUI.DrawRect(rowRect, page.SelectedRowBlue);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), page.AccentBlue);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.05f));
        }

        float indent = node.depth * 14f + 18f;
        Rect iconRect = new Rect(rowRect.x + indent, rowRect.y + 2f, 20f, 20f);
        Rect labelRect = new Rect(rowRect.x + indent + 24f, rowRect.y, rowRect.width - indent - 24f, rowRect.height);

        if (node.unit != null && node.unit.icon != null)
            GUI.DrawTexture(iconRect, node.unit.icon.texture, ScaleMode.ScaleToFit);

        GUIStyle style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = isSelected ? Color.white : new Color(0.90f, 0.90f, 0.92f, 1f) }
        };
        GUI.Label(labelRect, node.displayName, style);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
        {
            if (e.button == 0)
            {
                page.SelectUnit(node.unit);
                selectedFolderPath = node.assetFolderPath;
                selectedFolderKey = "";
                e.Use();
            }
            else if (e.button == 1)
            {
                page.SelectUnit(node.unit);
                selectedFolderPath = node.assetFolderPath;
                selectedFolderKey = "";
                ShowUnitContextMenu(node);
                e.Use();
            }
        }

        if (e.type == EventType.MouseDrag && rowRect.Contains(e.mousePosition) && node.unit != null)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new Object[] { node.unit };
            DragAndDrop.SetGenericData("SkyPrisonDraggedUnitDefinition", node.unit);
            DragAndDrop.StartDrag(node.unit.name);
            e.Use();
        }
    }

    private void ShowFolderContextMenu(TreeNode node)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("新建单位定义"), false, () => CreateNewUnitDefinition(node.assetFolderPath));

        if (clipboardUnitDefinition != null)
            menu.AddItem(new GUIContent("粘贴"), false, () => PasteClipboardUnitToFolder(node.assetFolderPath));
        else
            menu.AddDisabledItem(new GUIContent("粘贴"));

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

    private void ShowUnitContextMenu(TreeNode node)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("复制"), false, () => CopyUnit(node.unit));
        menu.AddItem(new GUIContent("剪切"), false, () => CutUnit(node.unit));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("删除"), false, () => DeleteUnitDefinition(node.unit));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("在 Project 中定位"), false, () =>
        {
            Selection.activeObject = node.unit;
            EditorGUIUtility.PingObject(node.unit);
        });
        menu.ShowAsContext();
    }

    private void HandleDragAndDrop()
    {
        Event e = Event.current;
        if (e == null)
            return;

        UnitDefinition draggedUnit = DragAndDrop.GetGenericData("SkyPrisonDraggedUnitDefinition") as UnitDefinition;
        if (draggedUnit == null)
            return;

        TreeNode hoveredFolder = visibleNodes.FirstOrDefault(n => n.isFolder && n.lastRect.Contains(e.mousePosition));
        if (hoveredFolder == null)
            return;

        if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                MoveUnitDefinitionToFolder(draggedUnit, hoveredFolder.assetFolderPath);
                DragAndDrop.SetGenericData("SkyPrisonDraggedUnitDefinition", null);
            }

            e.Use();
        }
    }

    private void SelectFolder(string folderKey, string folderPath)
    {
        page.ClearSelectedUnitAndSO();
        selectedFolderKey = folderKey;
        selectedFolderPath = folderPath;
    }

    private void CreateNewUnitDefinition(string folderPath)
    {
        page.EnsureFolderExists(folderPath);

        UnitDefinition asset = ScriptableObject.CreateInstance<UnitDefinition>();
        asset.displayName = "新单位";
        asset.unitId = page.GenerateUniqueUnitId("new_unit");

        string path = AssetDatabase.GenerateUniqueAssetPath(folderPath.TrimEnd('/') + "/UD_NewUnit.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        page.Refresh();
        page.SelectUnit(asset);
        selectedFolderPath = folderPath;
        selectedFolderKey = "";
    }

    public bool DeleteUnitDefinition(UnitDefinition unit)
    {
        if (unit == null)
            return false;

        string path = AssetDatabase.GetAssetPath(unit);
        if (string.IsNullOrEmpty(path))
            return false;

        bool ok = EditorUtility.DisplayDialog(
            "删除单位定义",
            $"确定删除单位定义：\n{unit.name}\n\n此操作会删除资源文件。",
            "删除",
            "取消"
        );
        if (!ok)
            return false;

        if (page.SelectedUnitDefinition == unit)
            page.ClearSelectedUnitAndSO();

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        page.Refresh();
        return true;
    }

    private void MoveUnitDefinitionToFolder(UnitDefinition unit, string targetFolderPath)
    {
        if (unit == null || string.IsNullOrEmpty(targetFolderPath))
            return;

        string sourcePath = AssetDatabase.GetAssetPath(unit);
        if (string.IsNullOrEmpty(sourcePath))
            return;

        page.EnsureFolderExists(targetFolderPath);

        string sourceFolder = Path.GetDirectoryName(sourcePath)?.Replace("\\", "/") ?? "";
        if (sourceFolder == targetFolderPath)
            return;

        string fileName = Path.GetFileName(sourcePath);
        string targetPath = AssetDatabase.GenerateUniqueAssetPath(targetFolderPath.TrimEnd('/') + "/" + fileName);
        string error = AssetDatabase.MoveAsset(sourcePath, targetPath);

        if (!string.IsNullOrEmpty(error))
        {
            EditorUtility.DisplayDialog("移动失败", error, "确定");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        page.Refresh();
        UnitDefinition moved = AssetDatabase.LoadAssetAtPath<UnitDefinition>(targetPath);
        page.SelectUnit(moved);
        selectedFolderPath = targetFolderPath;
        selectedFolderKey = "";
    }

    private void PasteClipboardUnitToFolder(string folderPath)
    {
        if (clipboardUnitDefinition == null)
            return;

        if (clipboardMode == UnitClipboardMode.Copy)
            PasteCopiedUnitToFolder(folderPath);
        else if (clipboardMode == UnitClipboardMode.Cut)
        {
            MoveUnitDefinitionToFolder(clipboardUnitDefinition, folderPath);
            clipboardUnitDefinition = null;
            clipboardMode = UnitClipboardMode.None;
        }
    }

    private void PasteCopiedUnitToFolder(string folderPath)
    {
        if (clipboardUnitDefinition == null)
            return;

        page.EnsureFolderExists(folderPath);

        UnitDefinition clone = Object.Instantiate(clipboardUnitDefinition);
        clone.name = clipboardUnitDefinition.name + "_Copy";
        clone.unitId = page.GenerateUniqueUnitId(
            string.IsNullOrWhiteSpace(clipboardUnitDefinition.unitId)
                ? "unit_copy"
                : clipboardUnitDefinition.unitId + "_copy"
        );

        string path = AssetDatabase.GenerateUniqueAssetPath(
            folderPath.TrimEnd('/') + "/" + clipboardUnitDefinition.name + "_Copy.asset");

        AssetDatabase.CreateAsset(clone, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        page.Refresh();
        page.SelectUnit(clone);
        selectedFolderPath = folderPath;
        selectedFolderKey = "";
    }

    private string GetCurrentCreateFolder()
    {
        if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            return selectedFolderPath;

        if (page.SelectedUnitDefinition != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(page.SelectedUnitDefinition);
            string folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(folder))
                return folder;
        }

        return SkyPrisonUnitDefinitionPage.DefaultUnitCreateFolder;
    }

    private string GetRelativeFolder(string assetPath)
    {
        string folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/") ?? "";

        if (folder.StartsWith(SkyPrisonUnitDefinitionPage.UnitRootFolder))
        {
            string relative = folder.Substring(SkyPrisonUnitDefinitionPage.UnitRootFolder.Length).TrimStart('/');
            return string.IsNullOrEmpty(relative) ? "未分类" : relative;
        }

        const string projectRoot = "Assets/_Project/";
        if (folder.StartsWith(projectRoot))
        {
            string relative = folder.Substring(projectRoot.Length).TrimStart('/');
            return string.IsNullOrEmpty(relative) ? "未分类" : relative;
        }

        string fileName = Path.GetFileName(folder);
        return string.IsNullOrWhiteSpace(fileName) ? "未分类" : fileName;
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

    private bool DrawToolButton(Rect rect, string text, string tooltip)
    {
        Event e = Event.current;
        bool hover = rect.Contains(e.mousePosition);
        bool clicked = e.type == EventType.MouseDown && e.button == 0 && hover;

        Color bg = hover ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.04f);
        EditorGUI.DrawRect(rect, bg);
        page.DrawThinBorder(rect, new Color(1f, 1f, 1f, hover ? 0.12f : 0.05f));

        GUI.Label(rect, new GUIContent(text, tooltip), page.GetCenteredToolbarTextStyle());

        if (clicked)
        {
            e.Use();
            GUI.changed = true;
            return true;
        }

        return false;
    }
}
