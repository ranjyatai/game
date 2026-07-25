using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonItemDefinitionAssetListPanel
{
    private class TreeNode
    {
        public string displayName;
        public string fullKey;
        public string assetFolderPath;
        public int depth;
        public bool isFolder;
        public ItemDefinition item;
        public Rect lastRect;
    }

    private enum ItemClipboardMode
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

    private readonly SkyPrisonItemDefinitionPage page;
    private readonly Dictionary<string, bool> folderExpanded = new Dictionary<string, bool>();
    private readonly List<TreeNode> visibleNodes = new List<TreeNode>();

    private string search = "";
    private string selectedFolderPath = SkyPrisonItemDefinitionPage.DefaultItemCreateFolder;
    private string selectedFolderKey = "";
    private Vector2 listScroll;

    private ItemDefinition clipboardItemDefinition;
    private ItemClipboardMode clipboardMode = ItemClipboardMode.None;

    public SkyPrisonItemDefinitionAssetListPanel(SkyPrisonItemDefinitionPage page)
    {
        this.page = page;
    }

    public void OnRefresh()
    {
        string selectedPath = page.SelectedItemDefinition != null ? AssetDatabase.GetAssetPath(page.SelectedItemDefinition) : "";
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

        DrawLeftLabelRow(titleRect, "物品定义列表");
        DrawToolbarRow(toolbarRect);
        DrawSearchRow(searchRect);

        BuildTree();
        DrawTreeContainer(containerRect);
    }

    public void HandlePostGUI()
    {
        HandleDragAndDrop();
    }

    public void CopyItem(ItemDefinition item)
    {
        clipboardItemDefinition = item;
        clipboardMode = ItemClipboardMode.Copy;
    }

    public void CutItem(ItemDefinition item)
    {
        clipboardItemDefinition = item;
        clipboardMode = ItemClipboardMode.Cut;
    }

    public bool TryPasteClipboardToCurrentFolder()
    {
        if (clipboardItemDefinition == null)
            return false;

        PasteClipboardItemToFolder(GetCurrentCreateFolder());
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

        if (DrawToolButton(plusRect, "+", "新建物品定义"))
            CreateNewItemDefinition(GetCurrentCreateFolder());

        using (new EditorGUI.DisabledScope(page.SelectedItemDefinition == null))
        {
            if (DrawToolButton(minusRect, "-", "删除当前物品定义"))
                DeleteItemDefinition(page.SelectedItemDefinition);
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
                DrawItemNode(rowRect, node);
        }

        if (visibleNodes.Count == 0)
            GUI.Label(new Rect(4f, 2f, contentRect.width - 8f, 22f), "没有匹配的物品定义", EditorStyles.miniLabel);

        GUI.EndScrollView();
    }

    private void BuildTree()
    {
        visibleNodes.Clear();
        Dictionary<string, List<ItemDefinition>> folderToItems = new Dictionary<string, List<ItemDefinition>>();
        // 每个复合分组key（物理文件夹 + "/" + 自动分类虚拟目录）对应的物理层级数——
        // 建树时用来区分"哪几段是真实磁盘文件夹（新建/拖拽要用）"和"哪几段是纯展示用的
        // 虚拟分类（武器/防具/消耗品……），虚拟段不生成新的磁盘文件夹，新建物品定义时
        // 仍然落在物理文件夹里，不会把物品实际挪来挪去。
        Dictionary<string, int> physicalDepthByFolder = new Dictionary<string, int>();

        foreach (ItemDefinition item in page.ItemDefinitions)
        {
            if (item == null)
                continue;

            string compareName = string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName;
            string compareKey = string.IsNullOrWhiteSpace(item.itemKey) ? "" : item.itemKey;

            if (!string.IsNullOrWhiteSpace(search))
            {
                string keyword = search.Trim().ToLowerInvariant();
                bool match =
                    compareName.ToLowerInvariant().Contains(keyword) ||
                    compareKey.ToLowerInvariant().Contains(keyword) ||
                    item.name.ToLowerInvariant().Contains(keyword);

                if (!match)
                    continue;
            }

            string path = AssetDatabase.GetAssetPath(item).Replace("\\", "/");
            string physicalFolder = GetRelativeFolder(path);
            string categoryLabel = GetCategoryLabel(item);
            string relativeFolder = physicalFolder + "/" + categoryLabel;

            if (!folderToItems.ContainsKey(relativeFolder))
            {
                folderToItems.Add(relativeFolder, new List<ItemDefinition>());
                physicalDepthByFolder[relativeFolder] =
                    physicalFolder.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
            }

            folderToItems[relativeFolder].Add(item);
        }

        List<string> allFolders = folderToItems.Keys
            .OrderBy(GetFolderSortPriority)
            .ThenBy(GetFolderPrimaryName)
            .ThenBy(x => x)
            .ToList();

        HashSet<string> addedFolders = new HashSet<string>();

        foreach (string folder in allFolders)
        {
            string[] parts = folder.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
            int physicalDepth = physicalDepthByFolder[folder];
            string current = "";
            string currentAssetPath = SkyPrisonItemDefinitionPage.ItemRootFolder;

            for (int i = 0; i < parts.Length; i++)
            {
                current = string.IsNullOrEmpty(current) ? parts[i] : current + "/" + parts[i];

                // 虚拟分类段（i >= physicalDepth）不往下延伸磁盘路径，"新建物品定义"落在
                // 它上一级真实文件夹里，不会因为点了"武器"这个虚拟节点就在磁盘上真造一个
                // "武器"文件夹出来。
                if (i < physicalDepth)
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

            string leafAssetFolderPath = currentAssetPath;
            foreach (ItemDefinition item in folderToItems[folder]
                         .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
                         .ThenBy(x => x.name))
            {
                visibleNodes.Add(new TreeNode
                {
                    displayName = string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName,
                    fullKey = folder + "/" + item.name,
                    assetFolderPath = leafAssetFolderPath,
                    depth = parts.Length,
                    isFolder = false,
                    item = item
                });
            }
        }
    }

    // 自动分类：不依赖物品实际存放的磁盘文件夹，直接按数据字段判断——装备类按
    // 装备槽位分"武器"/"防具"，非装备按物品分类分"消耗品"/"材料"等，跟磁盘位置解耦，
    // 物品不用真的搬文件夹也能在列表里按类型分组浏览。
    private static string GetCategoryLabel(ItemDefinition item)
    {
        if (item.majorCategory == ItemMajorCategory.Equipment && item.equipment != null)
        {
            switch (item.equipment.slot)
            {
                case EquipmentSlotType.Weapon:
                case EquipmentSlotType.WeaponSecondary:
                    return "武器";
                case EquipmentSlotType.Head:
                case EquipmentSlotType.UpperBody:
                case EquipmentSlotType.LowerBody:
                case EquipmentSlotType.Hands:
                case EquipmentSlotType.Shoes:
                    return "防具";
                default:
                    return "装备";
            }
        }

        switch (item.category)
        {
            case ItemCategory.Consumable: return "消耗品";
            case ItemCategory.Material:   return "材料";
            case ItemCategory.Quest:      return "任务道具";
            case ItemCategory.Currency:   return "凭证 / 货币";
            case ItemCategory.Special:    return "特殊";
            default:                      return "其它";
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
        bool isSelected = selectedFolderKey == node.fullKey && page.SelectedItemDefinition == null;
        bool hover = rowRect.Contains(Event.current.mousePosition);

        if (isSelected)
        {
            EditorGUI.DrawRect(rowRect, page.SelectedFolderYellow);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), page.AccentYellow);
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

    private void DrawItemNode(Rect rowRect, TreeNode node)
    {
        bool isSelected = page.SelectedItemDefinition == node.item;
        bool hover = rowRect.Contains(Event.current.mousePosition);

        if (isSelected)
        {
            EditorGUI.DrawRect(rowRect, page.SelectedRowYellow);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), page.AccentYellow);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.05f));
        }

        float indent = node.depth * 14f + 18f;
        Rect iconRect = new Rect(rowRect.x + indent, rowRect.y + 2f, 20f, 20f);
        Rect labelRect = new Rect(rowRect.x + indent + 24f, rowRect.y, rowRect.width - indent - 24f, rowRect.height);

        if (node.item != null && node.item.icon != null)
            GUI.DrawTexture(iconRect, node.item.icon.texture, ScaleMode.ScaleToFit);

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
                page.SelectItem(node.item);
                selectedFolderPath = node.assetFolderPath;
                selectedFolderKey = "";
                e.Use();
            }
            else if (e.button == 1)
            {
                page.SelectItem(node.item);
                selectedFolderPath = node.assetFolderPath;
                selectedFolderKey = "";
                ShowItemContextMenu(node);
                e.Use();
            }
        }

        if (e.type == EventType.MouseDrag && rowRect.Contains(e.mousePosition) && node.item != null)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new Object[] { node.item };
            DragAndDrop.SetGenericData("SkyPrisonDraggedItemDefinition", node.item);
            DragAndDrop.StartDrag(node.item.name);
            e.Use();
        }
    }

    private void ShowFolderContextMenu(TreeNode node)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("新建物品定义"), false, () => CreateNewItemDefinition(node.assetFolderPath));

        if (clipboardItemDefinition != null)
            menu.AddItem(new GUIContent("粘贴"), false, () => PasteClipboardItemToFolder(node.assetFolderPath));
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

    private void ShowItemContextMenu(TreeNode node)
    {
        const string standardItems = "Assets/_Project/Data/Definitions/Standard/Items";
        const string customItems   = "Assets/_Project/Data/Definitions/Custom/Items";

        string currentPath = AssetDatabase.GetAssetPath(node.item);
        bool inStandard = currentPath.Contains("/Standard/");
        bool inCustom   = currentPath.Contains("/Custom/");

        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("复制"), false, () => CopyItem(node.item));
        menu.AddItem(new GUIContent("剪切"), false, () => CutItem(node.item));
        menu.AddSeparator("");

        if (inCustom)
            menu.AddItem(new GUIContent("移动到 Standard/Items"), false,
                () => MoveItemDefinitionToFolder(node.item, standardItems));
        else
            menu.AddDisabledItem(new GUIContent("移动到 Standard/Items"));

        if (inStandard)
            menu.AddItem(new GUIContent("移动到 Custom/Items"), false,
                () => MoveItemDefinitionToFolder(node.item, customItems));
        else
            menu.AddDisabledItem(new GUIContent("移动到 Custom/Items"));

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("删除"), false, () => DeleteItemDefinition(node.item));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("在 Project 中定位"), false, () =>
        {
            Selection.activeObject = node.item;
            EditorGUIUtility.PingObject(node.item);
        });
        menu.ShowAsContext();
    }

    private void HandleDragAndDrop()
    {
        Event e = Event.current;
        if (e == null)
            return;

        ItemDefinition draggedItem = DragAndDrop.GetGenericData("SkyPrisonDraggedItemDefinition") as ItemDefinition;
        if (draggedItem == null)
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
                MoveItemDefinitionToFolder(draggedItem, hoveredFolder.assetFolderPath);
                DragAndDrop.SetGenericData("SkyPrisonDraggedItemDefinition", null);
            }

            e.Use();
        }
    }

    private void SelectFolder(string folderKey, string folderPath)
    {
        page.ClearSelectedItemAndSO();
        selectedFolderKey = folderKey;
        selectedFolderPath = folderPath;
    }

    private void CreateNewItemDefinition(string folderPath)
    {
        page.EnsureFolderExists(folderPath);

        ItemDefinition asset = ScriptableObject.CreateInstance<ItemDefinition>();
        asset.displayName = "新道具";
        asset.itemKey = page.GenerateUniqueItemKey("new_item");
        asset.nameKey = $"item_name_{asset.itemKey}";
        asset.descKey = $"item_desc_{asset.itemKey}";
        asset.iconKey = $"icon_{asset.itemKey}";
        asset.itemId = page.GenerateUniqueItemId(10000);

        string path = AssetDatabase.GenerateUniqueAssetPath(folderPath.TrimEnd('/') + "/ID_NewItem.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        page.Refresh();
        page.SelectItem(asset);
        selectedFolderPath = folderPath;
        selectedFolderKey = "";
    }

    public bool DeleteItemDefinition(ItemDefinition item)
    {
        if (item == null)
            return false;

        string path = AssetDatabase.GetAssetPath(item);
        if (string.IsNullOrEmpty(path))
            return false;

        bool ok = EditorUtility.DisplayDialog(
            "删除物品定义",
            $"确定删除物品定义：\n{item.name}\n\n此操作会删除资源文件。",
            "删除",
            "取消"
        );
        if (!ok)
            return false;

        if (page.SelectedItemDefinition == item)
            page.ClearSelectedItemAndSO();

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        page.Refresh();
        return true;
    }

    private void MoveItemDefinitionToFolder(ItemDefinition item, string targetFolderPath)
    {
        if (item == null || string.IsNullOrEmpty(targetFolderPath))
            return;

        string sourcePath = AssetDatabase.GetAssetPath(item);
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
        ItemDefinition moved = AssetDatabase.LoadAssetAtPath<ItemDefinition>(targetPath);
        page.SelectItem(moved);
        selectedFolderPath = targetFolderPath;
        selectedFolderKey = "";
    }

    private void PasteClipboardItemToFolder(string folderPath)
    {
        if (clipboardItemDefinition == null)
            return;

        if (clipboardMode == ItemClipboardMode.Copy)
            PasteCopiedItemToFolder(folderPath);
        else if (clipboardMode == ItemClipboardMode.Cut)
        {
            MoveItemDefinitionToFolder(clipboardItemDefinition, folderPath);
            clipboardItemDefinition = null;
            clipboardMode = ItemClipboardMode.None;
        }
    }

    private void PasteCopiedItemToFolder(string folderPath)
    {
        if (clipboardItemDefinition == null)
            return;

        page.EnsureFolderExists(folderPath);

        ItemDefinition clone = Object.Instantiate(clipboardItemDefinition);
        clone.name = clipboardItemDefinition.name + "_Copy";
        clone.itemKey = page.GenerateUniqueItemKey(
            string.IsNullOrWhiteSpace(clipboardItemDefinition.itemKey)
                ? "item_copy"
                : clipboardItemDefinition.itemKey + "_copy"
        );
        clone.nameKey = $"item_name_{clone.itemKey}";
        clone.descKey = $"item_desc_{clone.itemKey}";
        clone.iconKey = $"icon_{clone.itemKey}";
        clone.itemId = page.GenerateUniqueItemId(
            clipboardItemDefinition.itemId > 0 ? clipboardItemDefinition.itemId + 1 : 10000
        );

        string path = AssetDatabase.GenerateUniqueAssetPath(
            folderPath.TrimEnd('/') + "/" + clipboardItemDefinition.name + "_Copy.asset");

        AssetDatabase.CreateAsset(clone, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        page.Refresh();
        page.SelectItem(clone);
        selectedFolderPath = folderPath;
        selectedFolderKey = "";
    }

    private string GetCurrentCreateFolder()
    {
        if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            return selectedFolderPath;

        if (page.SelectedItemDefinition != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(page.SelectedItemDefinition);
            string folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(folder))
                return folder;
        }

        return SkyPrisonItemDefinitionPage.DefaultItemCreateFolder;
    }

    private string GetRelativeFolder(string assetPath)
    {
        string folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/") ?? "";

        if (folder.StartsWith(SkyPrisonItemDefinitionPage.ItemRootFolder))
        {
            string relative = folder.Substring(SkyPrisonItemDefinitionPage.ItemRootFolder.Length).TrimStart('/');
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
