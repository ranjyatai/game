using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonMapAssetListPanel
{
    private class TreeNode
    {
        public string displayName;
        public string fullKey;
        public string assetFolderPath;
        public int depth;
        public bool isFolder;
        public MapDefinition map;
        public Rect lastRect;
    }

    private enum ClipboardMode
    {
        None,
        Copy,
        Cut,
    }

    private const float LeftTitleRowHeight = 22f;
    private const float LeftToolbarRowHeight = 24f;
    private const float LeftSearchRowHeight = 22f;
    private const float LeftRowGap = 6f;
    private const float LeftContainerPadding = 8f;
    private const float LeftListRowHeight = 24f;

    private readonly SkyPrisonMapEditorPage page;
    private readonly Dictionary<string, bool> folderExpanded = new Dictionary<string, bool>();
    private readonly List<TreeNode> visibleNodes = new List<TreeNode>();

    private string search = "";
    private string selectedFolderPath = SkyPrisonMapEditorPage.DefaultMapCreateFolder;
    private string selectedFolderKey = "";
    private Vector2 listScroll;

    private MapDefinition pendingFocusMap;
    private string pendingFocusMapAssetPath = "";
    private string pendingFocusPackageFolder = "";

    private MapDefinition clipboardMap;
    private ClipboardMode clipboardMode = ClipboardMode.None;

    private string renamingFolderKey = "";
    private string renamingFolderPath = "";
    private MapDefinition renamingMap;
    private string renameFolderBuffer = "";
    private bool renameFocusPending = false;

    private static readonly Color FoldoutArrowGray = new Color(0.50f, 0.50f, 0.52f, 1f);

    private const float MapDragStartThreshold = 7f;
    private MapDefinition pendingDragMap;
    private string pendingDragPackageFolder = "";
    private Vector2 pendingDragStartMouse;

    public SkyPrisonMapAssetListPanel(SkyPrisonMapEditorPage page)
    {
        this.page = page;
    }

    public void OnRefresh()
    {
        string selectedPath = page.SelectedMap != null ? AssetDatabase.GetAssetPath(page.SelectedMap) : "";
        if (!string.IsNullOrEmpty(selectedPath))
        {
            string folder = Path.GetDirectoryName(selectedPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(folder))
                selectedFolderPath = GetParentFolderPath(folder);
        }
    }

    public void Draw()
    {
        Rect fullRect = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        float y = fullRect.y;
        Rect titleRect = new Rect(fullRect.x, y, fullRect.width, LeftTitleRowHeight);
        y += LeftTitleRowHeight + LeftRowGap;

        Rect toolbarRect = new Rect(fullRect.x, y, fullRect.width, LeftToolbarRowHeight);
        y += LeftToolbarRowHeight + LeftRowGap;

        Rect searchRect = new Rect(fullRect.x, y, fullRect.width, LeftSearchRowHeight);
        y += LeftSearchRowHeight + LeftRowGap;

        Rect containerRect = new Rect(fullRect.x, y, fullRect.width, Mathf.Max(40f, fullRect.yMax - y));

        GUI.Label(titleRect, "地图列表", EditorStyles.boldLabel);
        DrawToolbarRow(toolbarRect);
        DrawSearchRow(searchRect);
        BuildTree();
        DrawTreeContainer(containerRect);
    }

    public void HandlePostGUI()
    {
        // 拖拽需要使用左侧树 ScrollView 内部坐标，具体处理放在 DrawTreeContainer 内。
    }


    public void FocusMap(MapDefinition map, bool expandParents)
    {
        if (map == null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(map);
        if (string.IsNullOrWhiteSpace(assetPath) && map == pendingFocusMap)
            assetPath = pendingFocusMapAssetPath;

        FocusMapByPath(map, assetPath, expandParents);
    }

    public void FocusCreatedMap(MapDefinition map, string assetPath, bool expandParents)
    {
        FocusMapByPath(map, assetPath, expandParents);
    }

    private void FocusMapByPath(MapDefinition map, string assetPath, bool expandParents)
    {
        if (map == null)
            return;

        assetPath = string.IsNullOrWhiteSpace(assetPath) ? AssetDatabase.GetAssetPath(map) : assetPath;
        assetPath = string.IsNullOrWhiteSpace(assetPath) ? "" : assetPath.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        string packageFolder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(packageFolder))
            return;

        pendingFocusMap = map;
        pendingFocusMapAssetPath = assetPath;
        pendingFocusPackageFolder = packageFolder;

        string relativePackage = GetRelativeFolder(packageFolder);
        string parentRelative = GetParentRelativeFolder(relativePackage);

        if (expandParents)
        {
            string[] parts = parentRelative.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
            string accum = "";
            for (int i = 0; i < parts.Length; i++)
            {
                accum = string.IsNullOrEmpty(accum) ? parts[i] : accum + "/" + parts[i];
                folderExpanded["CAT>" + accum] = true;
            }
        }

        selectedFolderPath = GetParentFolderPath(packageFolder);
        selectedFolderKey = "CAT>" + parentRelative;

        BuildTree();
        int index = visibleNodes.FindIndex(n => !n.isFolder && (n.map == map || n.assetFolderPath == packageFolder));
        if (index >= 0)
            listScroll.y = Mathf.Max(0f, index * LeftListRowHeight - LeftListRowHeight * 2f);

        Selection.activeObject = map;
        EditorGUIUtility.PingObject(map);
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

        if (DrawToolButton(plusRect, "+", "在当前文件夹创建地图"))
            page.CreateNewMap(GetCurrentCreateFolder());

        using (new EditorGUI.DisabledScope(page.SelectedMap == null))
        {
            if (DrawToolButton(minusRect, "-", "删除当前地图"))
                DeleteMapPackage(page.SelectedMap);
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

        Rect viewRect = new Rect(rect.x + LeftContainerPadding, rect.y + LeftContainerPadding, rect.width - LeftContainerPadding * 2f, rect.height - LeftContainerPadding * 2f);
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
                DrawMapNode(rowRect, node);
        }

        HandleMapDragStartInsideTree();
        HandleMapDragAndDropInsideTree(contentRect);

        if (visibleNodes.Count == 0)
            GUI.Label(new Rect(4f, 2f, contentRect.width - 8f, 22f), "没有匹配的地图", EditorStyles.miniLabel);

        GUI.EndScrollView();
    }

    private void BuildTree()
    {
        visibleNodes.Clear();

        // 左侧树不能只依赖 page.Maps 的缓存。
        // 新建地图后的第一帧里，文件夹可能已经出现，但缓存还没把 MD_xxx.asset 纳入。
        // 所以这里合并 page.Maps、pendingFocusMap，以及 AssetDatabase 当前能查到的 MapDefinition。
        List<MapDefinition> sourceMaps = new List<MapDefinition>();
        if (page.Maps != null)
            sourceMaps.AddRange(page.Maps.Where(m => m != null));

        if (pendingFocusMap != null && !sourceMaps.Contains(pendingFocusMap))
            sourceMaps.Add(pendingFocusMap);

        string[] mapGuids = AssetDatabase.FindAssets("t:MapDefinition", new[] { SkyPrisonMapEditorPage.MapDefinitionRootFolder });
        foreach (string guid in mapGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MapDefinition map = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
            if (map != null && !sourceMaps.Contains(map))
                sourceMaps.Add(map);
        }

        var packageInfos = sourceMaps
            .Where(m => m != null)
            .Select(m =>
            {
                string assetPath = AssetDatabase.GetAssetPath(m);
                if (string.IsNullOrWhiteSpace(assetPath) && m == pendingFocusMap)
                    assetPath = pendingFocusMapAssetPath;

                assetPath = string.IsNullOrWhiteSpace(assetPath) ? "" : assetPath.Replace("\\", "/");
                string packageFolder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/") ?? "";
                string relativePackageFolder = GetRelativeFolder(packageFolder);
                return new PackageInfo
                {
                    map = m,
                    packageFolder = packageFolder,
                    relativePackageFolder = relativePackageFolder,
                    categoryRelative = GetParentRelativeFolder(relativePackageFolder),
                    packageName = GetLastPathPart(relativePackageFolder)
                };
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.packageFolder))
            .Where(x => !string.IsNullOrWhiteSpace(x.relativePackageFolder))
            .Where(x => !IsHiddenRootFolder(x.relativePackageFolder))
            .GroupBy(x => x.packageFolder)
            .Select(g => g.First())
            .ToList();

        if (pendingFocusMap != null && !string.IsNullOrWhiteSpace(pendingFocusPackageFolder))
        {
            string packageFolder = pendingFocusPackageFolder.Replace("\\", "/");
            string relativePackageFolder = GetRelativeFolder(packageFolder);
            if (!string.IsNullOrWhiteSpace(relativePackageFolder)
                && !IsHiddenRootFolder(relativePackageFolder)
                && !packageInfos.Any(x => x.packageFolder == packageFolder))
            {
                packageInfos.Add(new PackageInfo
                {
                    map = pendingFocusMap,
                    packageFolder = packageFolder,
                    relativePackageFolder = relativePackageFolder,
                    categoryRelative = GetParentRelativeFolder(relativePackageFolder),
                    packageName = GetLastPathPart(relativePackageFolder)
                });
            }
        }

        HashSet<string> packageFolders = new HashSet<string>(packageInfos.Select(x => x.packageFolder));
        HashSet<string> categoryFolders = new HashSet<string>();

        foreach (string category in GetAllCategoryFolders(packageFolders))
            categoryFolders.Add(category);

        foreach (var info in packageInfos)
            EnsureCategoryParents(info.categoryRelative, categoryFolders);

        List<string> orderedCategories = categoryFolders
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderBy(GetFolderRootSortWeight)
            .ThenBy(x => x)
            .ToList();

        List<string> rootCategories = orderedCategories
            .Where(x => string.IsNullOrWhiteSpace(GetParentRelativeFolder(x)))
            .ToList();

        foreach (string rootCategory in rootCategories)
            AddCategoryRecursive(rootCategory, orderedCategories, packageInfos);
    }

    private class PackageInfo
    {
        public MapDefinition map;
        public string packageFolder;
        public string relativePackageFolder;
        public string categoryRelative;
        public string packageName;
    }

    private void AddCategoryRecursive(string category, List<string> allCategories, List<PackageInfo> packageInfos)
    {
        string[] parts = category.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
        int depth = parts.Length - 1;

        visibleNodes.Add(new TreeNode
        {
            displayName = parts[parts.Length - 1],
            fullKey = "CAT>" + category,
            assetFolderPath = ToAbsoluteFolder(category),
            depth = depth,
            isFolder = true
        });

        if (!IsFolderExpanded("CAT>" + category))
            return;

        foreach (string childCategory in allCategories.Where(x => GetParentRelativeFolder(x) == category).OrderBy(GetFolderRootSortWeight).ThenBy(x => x))
            AddCategoryRecursive(childCategory, allCategories, packageInfos);

        foreach (PackageInfo info in packageInfos
                     .Where(x => x.categoryRelative == category)
                     .OrderBy(x => x.packageName))
        {
            if (!MatchesSearch(info))
                continue;

            visibleNodes.Add(new TreeNode
            {
                displayName = info.packageName,
                fullKey = "PKG>" + info.packageFolder,
                assetFolderPath = info.packageFolder,
                depth = depth + 1,
                isFolder = false,
                map = info.map
            });
        }
    }

    private bool MatchesSearch(PackageInfo info)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        string keyword = search.Trim().ToLowerInvariant();
        string display = string.IsNullOrWhiteSpace(info.map.displayName) ? info.packageName : info.map.displayName;
        string key = string.IsNullOrWhiteSpace(info.map.mapKey) ? "" : info.map.mapKey;

        return display.ToLowerInvariant().Contains(keyword)
            || key.ToLowerInvariant().Contains(keyword)
            || info.packageName.ToLowerInvariant().Contains(keyword)
            || info.map.name.ToLowerInvariant().Contains(keyword);
    }

    private List<string> GetAllCategoryFolders(HashSet<string> packageFolders)
    {
        List<string> result = new List<string>();
        string root = SkyPrisonMapEditorPage.MapDefinitionRootFolder;
        if (!AssetDatabase.IsValidFolder(root))
            return result;

        CollectCategoryFolders(root, "", packageFolders, result);
        return result;
    }

    private void CollectCategoryFolders(string absoluteFolder, string relativeFolder, HashSet<string> packageFolders, List<string> output)
    {
        foreach (string rawSub in AssetDatabase.GetSubFolders(absoluteFolder))
        {
            string sub = rawSub.Replace("\\", "/");
            // 只要这个文件夹本身包含 MapDefinition，它就是地图包，不应该再作为普通分类文件夹显示。
            if (packageFolders.Contains(sub) || FolderContainsMapDefinitionDirect(sub))
                continue;

            bool insidePackage = packageFolders.Any(pkg => sub.StartsWith(pkg + "/"));
            if (insidePackage)
                continue;

            string name = Path.GetFileName(sub);
            string rel = string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;

            if (IsHiddenRootFolder(rel))
                continue;

            output.Add(rel);
            CollectCategoryFolders(sub, rel, packageFolders, output);
        }
    }

    private bool FolderContainsMapDefinitionDirect(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !AssetDatabase.IsValidFolder(folder))
            return false;

        folder = folder.Replace("\\", "/").TrimEnd('/');

        string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { folder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            if (parent == folder)
                return true;
        }

        // AssetDatabase 在新建地图后的当前帧可能还没把 MD_xxx.asset 纳入 FindAssets。
        // 这里直接扫磁盘做一次兜底，避免左侧树把地图包文件夹误判为普通分类文件夹。
        string absoluteFolder = ToAbsoluteSystemPath(folder);
        if (Directory.Exists(absoluteFolder))
        {
            string[] files = Directory.GetFiles(absoluteFolder, "*.asset", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileNameWithoutExtension(files[i]);
                if (!string.IsNullOrWhiteSpace(fileName) && fileName.StartsWith("MD_"))
                    return true;
            }
        }

        return false;
    }

    private void DrawFolderNode(Rect rowRect, TreeNode node)
    {
        bool isSelected = selectedFolderKey == node.fullKey && page.SelectedMap == null;
        bool hover = rowRect.Contains(Event.current.mousePosition);

        if (isSelected)
        {
            EditorGUI.DrawRect(rowRect, page.SelectedFolderGreen);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), page.AccentGreen);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.04f));
        }

        float indent = node.depth * 14f;
        Rect foldoutRect = new Rect(rowRect.x + indent + 4f, rowRect.y + 1f, 12f, rowRect.height - 2f);
        Rect labelRect = new Rect(rowRect.x + indent + 18f, rowRect.y, rowRect.width - indent - 18f, rowRect.height);

        bool expanded = IsFolderExpanded(node.fullKey);

        GUIStyle arrowStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 9,
            normal = { textColor = FoldoutArrowGray },
            hover = { textColor = FoldoutArrowGray },
            active = { textColor = FoldoutArrowGray },
            focused = { textColor = FoldoutArrowGray }
        };
        GUI.Label(foldoutRect, expanded ? "▼" : "▶", arrowStyle);

        Event e = Event.current;

        if (renamingFolderKey == node.fullKey)
        {
            GUI.SetNextControlName("MapFolderRenameField");
            string newValue = EditorGUI.TextField(labelRect, renameFolderBuffer ?? "");
            if (newValue != renameFolderBuffer)
                renameFolderBuffer = newValue;

            if (renameFocusPending)
            {
                EditorGUI.FocusTextInControl("MapFolderRenameField");
                renameFocusPending = false;
            }

            if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "MapFolderRenameField")
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    CommitFolderRename();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    CancelFolderRename();
                    e.Use();
                }
            }
        }
        else
        {
            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = isSelected ? Color.white : new Color(0.88f, 0.88f, 0.90f, 1f) }
            };
            GUI.Label(labelRect, node.displayName, style);
        }

        if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
        {
            if (e.button == 0)
            {
                if (foldoutRect.Contains(e.mousePosition))
                {
                    folderExpanded[node.fullKey] = !expanded;
                    SelectFolder(node.fullKey, node.assetFolderPath);
                    e.Use();
                    return;
                }

                if (labelRect.Contains(e.mousePosition) && e.clickCount >= 2)
                {
                    BeginFolderRename(node);
                    e.Use();
                    return;
                }

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

        if (renamingFolderKey == node.fullKey && e.type == EventType.MouseDown && !labelRect.Contains(e.mousePosition) && !foldoutRect.Contains(e.mousePosition))
            CommitFolderRename();
    }

    private void DrawMapNode(Rect rect, TreeNode node)
    {
        bool selected = page.SelectedMap == node.map;
        bool hover = rect.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(rect, page.SelectedRowGreen);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), page.AccentGreen);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.05f));
        }

        float indent = node.depth * 14f;
        Rect iconRect = new Rect(rect.x + indent + 4f, rect.y, 14f, rect.height);
        Rect labelRect = new Rect(rect.x + indent + 20f, rect.y, rect.width - indent - 20f, rect.height);

        GUIStyle iconStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            normal = { textColor = selected ? Color.white : page.AccentGreen }
        };
        GUI.Label(iconRect, "◆", iconStyle);

        Event e = Event.current;

        if (renamingFolderKey == node.fullKey)
        {
            GUI.SetNextControlName("MapPackageRenameField");
            string newValue = EditorGUI.TextField(labelRect, renameFolderBuffer ?? "");
            if (newValue != renameFolderBuffer)
                renameFolderBuffer = newValue;

            if (renameFocusPending)
            {
                EditorGUI.FocusTextInControl("MapPackageRenameField");
                renameFocusPending = false;
            }

            if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "MapPackageRenameField")
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    CommitFolderRename();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    CancelFolderRename();
                    e.Use();
                }
            }
        }
        else
        {
            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = selected ? Color.white : new Color(0.90f, 0.90f, 0.92f, 1f) }
            };
            GUI.Label(labelRect, node.displayName, style);
        }

        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
        {
            if (e.button == 0)
            {
                page.SelectMap(node.map);
                selectedFolderPath = GetParentFolderPath(node.assetFolderPath);
                selectedFolderKey = "CAT>" + GetRelativeFolder(selectedFolderPath);

                if (e.clickCount >= 2 && labelRect.Contains(e.mousePosition))
                {
                    ClearPendingMapDrag();
                    BeginFolderRename(node);
                    e.Use();
                    return;
                }

                pendingDragMap = node.map;
                pendingDragPackageFolder = node.assetFolderPath;
                pendingDragStartMouse = e.mousePosition;
                e.Use();
            }
            else if (e.button == 1)
            {
                page.SelectMap(node.map);
                selectedFolderPath = GetParentFolderPath(node.assetFolderPath);
                selectedFolderKey = "CAT>" + GetRelativeFolder(selectedFolderPath);
                ShowMapContextMenu(node);
                e.Use();
            }
        }
    }

    private void ShowFolderContextMenu(TreeNode node)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("新建地图"), false, () => page.CreateNewMap(node.assetFolderPath));
        menu.AddItem(new GUIContent("新建文件夹"), false, () => CreateSubFolder(node.assetFolderPath));
        menu.AddItem(new GUIContent("重命名"), false, () => BeginFolderRename(node));

        if (clipboardMap != null)
            menu.AddItem(new GUIContent("粘贴"), false, () => PasteClipboardMapToFolder(node.assetFolderPath));
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

    private void ShowMapContextMenu(TreeNode node)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("打开地图 Scene"), false, () => SkyPrisonMapEditorUtility.OpenMapScene(node.map));
        menu.AddItem(new GUIContent("重命名地图包"), false, () => BeginFolderRename(node));
        menu.AddItem(new GUIContent("在 Project 中定位地图包"), false, () =>
        {
            string assetPath = AssetDatabase.GetAssetPath(node.map);
            string folderPath = string.IsNullOrWhiteSpace(assetPath) ? node.assetFolderPath : Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            Object folderObj = !string.IsNullOrWhiteSpace(folderPath) ? AssetDatabase.LoadAssetAtPath<Object>(folderPath) : null;
            Selection.activeObject = folderObj != null ? folderObj : node.map;
            EditorGUIUtility.PingObject(Selection.activeObject);
        });
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("复制"), false, () => CopyMap(node.map));
        menu.AddItem(new GUIContent("剪切"), false, () => CutMap(node.map));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("删除"), false, () => DeleteMapPackage(node.map));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("定位 MapDefinition Asset"), false, () =>
        {
            Selection.activeObject = node.map;
            EditorGUIUtility.PingObject(node.map);
        });
        menu.ShowAsContext();
    }

    private void HandleMapDragStartInsideTree()
    {
        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseUp || e.type == EventType.Ignore)
        {
            ClearPendingMapDrag();
            return;
        }

        if (e.type != EventType.MouseDrag || pendingDragMap == null)
            return;

        if ((e.mousePosition - pendingDragStartMouse).magnitude < MapDragStartThreshold)
            return;

        DragAndDrop.PrepareStartDrag();
        DragAndDrop.objectReferences = new Object[] { pendingDragMap };
        DragAndDrop.SetGenericData("SkyPrisonDraggedMapDefinition", pendingDragMap);
        DragAndDrop.SetGenericData("SkyPrisonDraggedMapPackageFolder", pendingDragPackageFolder);
        DragAndDrop.StartDrag("移动地图包: " + GetMapNodeDisplayName(pendingDragMap));
        ClearPendingMapDrag();
        e.Use();
    }

    private void ClearPendingMapDrag()
    {
        pendingDragMap = null;
        pendingDragPackageFolder = "";
        pendingDragStartMouse = Vector2.zero;
    }

    private class MapDropTarget
    {
        public string targetFolder;
        public string targetLabel;
        public bool valid;
        public Rect highlightRect;
        public bool highlightFolder;
        public float lineY;
        public string invalidReason;
    }

    private void HandleMapDragAndDropInsideTree(Rect contentRect)
    {
        Event e = Event.current;
        if (e == null)
            return;

        MapDefinition draggedMap = DragAndDrop.GetGenericData("SkyPrisonDraggedMapDefinition") as MapDefinition;
        if (draggedMap == null)
            return;

        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform && e.type != EventType.Repaint)
            return;

        MapDropTarget target = GetMapDropTarget(draggedMap, e.mousePosition, contentRect);
        if (target != null)
            DrawMapDropIndicator(target);

        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
            return;

        DragAndDrop.visualMode = target != null && target.valid
            ? DragAndDropVisualMode.Move
            : DragAndDropVisualMode.Rejected;

        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();

            if (target != null && target.valid)
            {
                string mapName = GetMapNodeDisplayName(draggedMap);
                bool ok = EditorUtility.DisplayDialog(
                    "移动地图包",
                    "确定将地图包「" + mapName + "」移动到：\n" + target.targetLabel + "？\n\n会移动整个地图包文件夹，包括 MapDefinition 和 .unity Scene。",
                    "移动",
                    "取消");

                if (ok)
                {
                    if (SkyPrisonMapEditorUtility.MoveMapPackageToCategoryFolder(draggedMap, target.targetFolder, out MapDefinition movedMap))
                    {
                        page.Refresh();
                        if (movedMap != null)
                        {
                            page.SelectMap(movedMap);
                            FocusMap(movedMap, true);
                        }
                    }
                }
            }

            DragAndDrop.SetGenericData("SkyPrisonDraggedMapDefinition", null);
            DragAndDrop.SetGenericData("SkyPrisonDraggedMapPackageFolder", null);
        }

        e.Use();
    }

    private MapDropTarget GetMapDropTarget(MapDefinition draggedMap, Vector2 mousePosition, Rect contentRect)
    {
        if (draggedMap == null || !contentRect.Contains(mousePosition))
            return null;

        TreeNode hovered = visibleNodes.FirstOrDefault(n => n.lastRect.Contains(mousePosition));
        if (hovered == null)
            return null;

        string targetFolder = "";
        bool highlightFolder = false;
        float lineY = -1f;
        Rect highlightRect = hovered.lastRect;

        if (hovered.isFolder)
        {
            targetFolder = hovered.assetFolderPath;
            highlightFolder = true;
        }
        else
        {
            targetFolder = GetParentFolderPath(hovered.assetFolderPath);
            lineY = mousePosition.y < hovered.lastRect.center.y ? hovered.lastRect.y : hovered.lastRect.yMax;
        }

        MapDropTarget target = new MapDropTarget
        {
            targetFolder = targetFolder,
            targetLabel = GetReadableFolderLabel(targetFolder),
            highlightFolder = highlightFolder,
            highlightRect = highlightRect,
            lineY = lineY,
            valid = true,
        };

        ValidateMapDropTarget(draggedMap, target);
        return target;
    }

    private void ValidateMapDropTarget(MapDefinition draggedMap, MapDropTarget target)
    {
        if (target == null)
            return;

        if (draggedMap == null)
        {
            target.valid = false;
            target.invalidReason = "没有拖拽地图。";
            return;
        }

        string targetFolder = (target.targetFolder ?? "").Replace("\\", "/").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(targetFolder) || !AssetDatabase.IsValidFolder(targetFolder))
        {
            target.valid = false;
            target.invalidReason = "目标文件夹不存在。";
            return;
        }

        string relativeTarget = GetRelativeFolder(targetFolder);
        if (IsHiddenRootFolder(relativeTarget))
        {
            target.valid = false;
            target.invalidReason = "不能移动到模板目录。";
            return;
        }

        if (FolderContainsMapDefinitionDirect(targetFolder))
        {
            target.valid = false;
            target.invalidReason = "不能移动到另一个地图包内部。";
            return;
        }

        string sourceAssetPath = AssetDatabase.GetAssetPath(draggedMap).Replace("\\", "/");
        string sourcePackageFolder = string.IsNullOrWhiteSpace(sourceAssetPath) ? "" : Path.GetDirectoryName(sourceAssetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(sourcePackageFolder))
        {
            target.valid = false;
            target.invalidReason = "源地图包路径无效。";
            return;
        }

        string sourceParent = GetParentFolderPath(sourcePackageFolder);
        if (targetFolder == sourceParent)
        {
            target.valid = false;
            target.invalidReason = "已经在该文件夹下。";
            return;
        }

        if (targetFolder == sourcePackageFolder || targetFolder.StartsWith(sourcePackageFolder + "/"))
        {
            target.valid = false;
            target.invalidReason = "不能移动到自己的内部。";
            return;
        }

        string targetPackageFolder = targetFolder + "/" + Path.GetFileName(sourcePackageFolder);
        if (AssetDatabase.IsValidFolder(targetPackageFolder) || Directory.Exists(targetPackageFolder))
        {
            target.valid = false;
            target.invalidReason = "目标目录已有同名地图包。";
        }
    }

    private void DrawMapDropIndicator(MapDropTarget target)
    {
        if (target == null)
            return;

        Color color = target.valid ? page.AccentGreen : new Color(1f, 0.30f, 0.25f, 0.90f);

        if (target.highlightFolder)
        {
            Color bg = target.valid ? new Color(page.AccentGreen.r, page.AccentGreen.g, page.AccentGreen.b, 0.18f) : new Color(1f, 0.18f, 0.12f, 0.16f);
            EditorGUI.DrawRect(target.highlightRect, bg);
            EditorGUI.DrawRect(new Rect(target.highlightRect.x, target.highlightRect.yMax - 2f, target.highlightRect.width, 2f), color);
        }
        else if (target.lineY >= 0f)
        {
            EditorGUI.DrawRect(new Rect(4f, target.lineY - 1f, Mathf.Max(20f, target.highlightRect.width - 8f), 2f), color);
        }

        if (!target.valid && !string.IsNullOrWhiteSpace(target.invalidReason))
        {
            Rect labelRect = new Rect(8f, Mathf.Max(0f, target.highlightRect.y - 20f), target.highlightRect.width - 16f, 18f);
            GUI.Label(labelRect, target.invalidReason, EditorStyles.miniLabel);
        }
    }

    private string GetReadableFolderLabel(string folderPath)
    {
        string rel = GetRelativeFolder(folderPath);
        return string.IsNullOrWhiteSpace(rel) ? SkyPrisonMapEditorPage.MapDefinitionRootFolder : rel;
    }

    private string GetMapNodeDisplayName(MapDefinition map)
    {
        if (map == null)
            return "-";

        string assetPath = AssetDatabase.GetAssetPath(map);
        string folder = string.IsNullOrWhiteSpace(assetPath) ? "" : Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        if (!string.IsNullOrWhiteSpace(folder))
            return Path.GetFileName(folder);

        return string.IsNullOrWhiteSpace(map.fileName) ? map.name : map.fileName;
    }

    private void SelectFolder(string folderKey, string folderPath)
    {
        page.ClearSelectedMapAndSO();
        selectedFolderKey = folderKey;
        selectedFolderPath = folderPath;
    }

    private void BeginFolderRename(TreeNode node)
    {
        renamingFolderKey = node.fullKey;
        renamingFolderPath = node.assetFolderPath;
        renamingMap = node.map;
        renameFolderBuffer = node.displayName;
        renameFocusPending = true;
        GUI.changed = true;
    }

    private void CommitFolderRename()
    {
        if (string.IsNullOrWhiteSpace(renamingFolderPath))
        {
            CancelFolderRename();
            return;
        }

        string newName = (renameFolderBuffer ?? "").Trim();
        string oldName = Path.GetFileName(renamingFolderPath);
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName)
        {
            CancelFolderRename();
            return;
        }

        if (renamingMap != null)
        {
            MapDefinition map = renamingMap;
            bool ok = SkyPrisonMapEditorUtility.RenameMapPackage(map, newName);
            CancelFolderRename();
            page.Refresh();
            if (ok && map != null)
            {
                page.SelectMap(map);
                FocusMap(map, true);
            }
            return;
        }

        string parent = Path.GetDirectoryName(renamingFolderPath)?.Replace("\\", "/");
        string targetPath = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + newName);

        string error = AssetDatabase.MoveAsset(renamingFolderPath, targetPath);
        if (!string.IsNullOrWhiteSpace(error))
            Debug.LogError("[SkyPrisonMapAssetListPanel] Rename folder failed: " + error);

        CancelFolderRename();
        page.Refresh();
    }

    private void CancelFolderRename()
    {
        renamingFolderKey = "";
        renamingFolderPath = "";
        renameFolderBuffer = "";
        renamingMap = null;
        renameFocusPending = false;
        GUI.changed = true;
    }

    private void CopyMap(MapDefinition map)
    {
        clipboardMap = map;
        clipboardMode = ClipboardMode.Copy;
    }

    private void CutMap(MapDefinition map)
    {
        clipboardMap = map;
        clipboardMode = ClipboardMode.Cut;
    }

    private void PasteClipboardMapToFolder(string folderPath)
    {
        if (clipboardMap == null || string.IsNullOrWhiteSpace(folderPath))
            return;

        if (clipboardMode == ClipboardMode.Copy)
            SkyPrisonMapEditorUtility.DuplicateMapPackageToCategoryFolder(clipboardMap, folderPath);
        else if (clipboardMode == ClipboardMode.Cut)
            SkyPrisonMapEditorUtility.MoveMapPackageToCategoryFolder(clipboardMap, folderPath);

        if (clipboardMode == ClipboardMode.Cut)
        {
            clipboardMap = null;
            clipboardMode = ClipboardMode.None;
        }

        page.Refresh();
    }

    private void CreateSubFolder(string parentFolder)
    {
        string unique = AssetDatabase.GenerateUniqueAssetPath(parentFolder.TrimEnd('/') + "/NewFolder");
        string parent = Path.GetDirectoryName(unique)?.Replace("\\", "/");
        string folderName = Path.GetFileName(unique);
        if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(folderName))
        {
            AssetDatabase.CreateFolder(parent, folderName);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            page.Refresh();

            selectedFolderKey = "CAT>" + GetRelativeFolder(unique);
            selectedFolderPath = unique.Replace("\\", "/");
        }
    }

    private void MoveMapToFolder(MapDefinition map, string folderPath)
    {
        if (map == null || string.IsNullOrWhiteSpace(folderPath))
            return;

        SkyPrisonMapEditorUtility.MoveMapPackageToCategoryFolder(map, folderPath);
        page.Refresh();
    }

    public bool DeleteMapPackage(MapDefinition map)
    {
        if (map == null)
            return false;

        bool ok = EditorUtility.DisplayDialog("删除地图", "确定删除当前地图吗？\n会删除该地图所在文件夹及其内容。", "删除", "取消");
        if (!ok)
            return false;

        SkyPrisonMapEditorUtility.DeleteMapPackage(map);
        page.ClearSelectedMapAndSO();
        page.Refresh();
        return true;
    }

    private string GetCurrentCreateFolder()
    {
        if (!string.IsNullOrWhiteSpace(selectedFolderPath) && AssetDatabase.IsValidFolder(selectedFolderPath))
            return selectedFolderPath;

        if (page.SelectedMap != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(page.SelectedMap);
            string folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            string categoryFolder = GetParentFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(categoryFolder) && AssetDatabase.IsValidFolder(categoryFolder))
                return categoryFolder;
        }

        return SkyPrisonMapEditorPage.DefaultMapCreateFolder;
    }

    private string GetRelativeFolder(string absoluteFolderPath)
    {
        string folder = absoluteFolderPath.Replace("\\", "/");
        if (folder.StartsWith(SkyPrisonMapEditorPage.MapDefinitionRootFolder))
        {
            string rel = folder.Substring(SkyPrisonMapEditorPage.MapDefinitionRootFolder.Length).Trim('/');
            return rel;
        }
        return folder;
    }

    private string GetParentRelativeFolder(string relativeFolder)
    {
        if (string.IsNullOrWhiteSpace(relativeFolder))
            return "";

        int idx = relativeFolder.LastIndexOf('/');
        return idx >= 0 ? relativeFolder.Substring(0, idx) : "";
    }

    private string GetParentFolderPath(string absoluteFolderPath)
    {
        return Path.GetDirectoryName(absoluteFolderPath)?.Replace("\\", "/") ?? SkyPrisonMapEditorPage.DefaultMapCreateFolder;
    }

    private string ToAbsoluteFolder(string relative)
    {
        relative = relative.Trim('/');
        return string.IsNullOrEmpty(relative)
            ? SkyPrisonMapEditorPage.MapDefinitionRootFolder
            : SkyPrisonMapEditorPage.MapDefinitionRootFolder + "/" + relative;
    }

    private string ToAbsoluteSystemPath(string assetPath)
    {
        assetPath = string.IsNullOrWhiteSpace(assetPath) ? "" : assetPath.Replace("\\", "/");
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(projectRoot))
            return assetPath;

        return Path.Combine(projectRoot, assetPath).Replace("\\", "/");
    }

    private string GetLastPathPart(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return "";
        string[] parts = relativePath.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[parts.Length - 1] : relativePath;
    }

    private void EnsureCategoryParents(string categoryRelative, HashSet<string> categoryFolders)
    {
        if (string.IsNullOrWhiteSpace(categoryRelative))
            return;

        string[] parts = categoryRelative.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
        string accum = "";
        for (int i = 0; i < parts.Length; i++)
        {
            accum = string.IsNullOrEmpty(accum) ? parts[i] : accum + "/" + parts[i];
            categoryFolders.Add(accum);
        }
    }

    private bool IsHiddenRootFolder(string relativeFolder)
    {
        string top = GetFolderPrimaryName(relativeFolder);
        return top == "_Templates";
    }

    private int GetFolderRootSortWeight(string folder)
    {
        string top = GetFolderPrimaryName(folder);
        if (top == "Final") return 0;
        if (top == "Hub") return 1;
        if (top == "Raid") return 2;
        if (top == "Survival") return 3;
        return 10;
    }

    private string GetFolderPrimaryName(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return "";
        string[] parts = folder.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : folder;
    }

    private bool IsFolderExpanded(string key)
    {
        if (!folderExpanded.TryGetValue(key, out bool expanded))
        {
            string rawKey = key.StartsWith("CAT>") ? key.Substring(4) : key;
            bool defaultExpanded = !string.IsNullOrWhiteSpace(rawKey) && rawKey.IndexOf('/') < 0;
            folderExpanded[key] = defaultExpanded;
            return defaultExpanded;
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
        GUI.Label(rect, new GUIContent(text, tooltip), new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11 });

        if (clicked)
        {
            e.Use();
            GUI.changed = true;
            return true;
        }

        return false;
    }
}
