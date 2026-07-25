using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonItemPoolPage : SkyPrisonEditorPageBase
{
    private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>
    {
        { "基础信息", true },
        { "物品掉落池", true }
    };

    private readonly Dictionary<int, HashSet<int>> selectedEntryIndicesByPool = new Dictionary<int, HashSet<int>>();
    private readonly Dictionary<int, int> activeEntryIndexByPool = new Dictionary<int, int>();
    private readonly Dictionary<int, int> anchorEntryIndexByPool = new Dictionary<int, int>();
    private readonly Dictionary<int, Vector2> entryGridScrollByPool = new Dictionary<int, Vector2>();

    private string search = "";
    private List<DropProfile> dropProfiles = new List<DropProfile>();
    private DropProfile selectedDropProfile;
    private SerializedObject selectedDropSO;

    private int focusedPoolIndex = -1;
    private bool focusInRightEntryGrid = false;
    private Vector2 leftListScroll;

    private const string DefaultDropCreateFolder = "Assets/_Project/Data/Database/Loot/DropProfiles";

    private const float EntryCardWidth = 108f;
    private const float EntryCardHeight = 106f;
    private const float EntryCardIconSize = 56f;
    private const float EntryCardSpacing = 8f;

    private const float EntryContainerHeight = 252f;
    private const float EntryContainerPadding = 8f;
    private const float EntryContainerScrollbarReserve = 18f;

    private const float LeftTitleRowHeight = 22f;
    private const float LeftToolbarRowHeight = 24f;
    private const float LeftSearchRowHeight = 22f;
    private const float LeftRowGap = 6f;
    private const float LeftContainerPadding = 8f;
    private const float LeftListRowHeight = 24f;

    private readonly Color leftTopBg = new Color(0.13f, 0.13f, 0.14f, 1f);
    private readonly Color accentGreen = new Color(0.42f, 0.82f, 0.52f, 1f);
    private readonly Color selectedRowGreen = new Color(0.30f, 0.62f, 0.34f, 0.28f);

    private static readonly Color EntryCardBg = new Color(1f, 1f, 1f, 0.045f);
    private static readonly Color EntryCardSelected = new Color(0.28f, 0.62f, 0.36f, 0.22f);
    private static readonly Color EntryCardActiveOutline = new Color(0.42f, 0.82f, 0.52f, 0.95f);
    private static readonly Color EntryCardDisabled = new Color(1f, 1f, 1f, 0.45f);
    private static readonly Color EntryCardError = new Color(1f, 0.42f, 0.42f, 1f);

    private static readonly Color AddCardBg = new Color(1f, 1f, 1f, 0.025f);
    private static readonly Color AddCardBorder = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color AddCardPlus = new Color(0.84f, 0.84f, 0.84f, 1f);
    private static readonly Color AddCardText = new Color(0.66f, 0.66f, 0.66f, 1f);
    private static readonly Color AddCardHover = new Color(0.28f, 0.62f, 0.36f, 0.16f);

    private Texture2D dropPoolHeaderIcon;
    private bool dropPoolHeaderIconLoaded = false;

    private const string EditorIconFolder = "Assets/_Project/Icon/Editor/";
    private const string EditorIconPrefix = "SkyPrisonEditor_";
    private const float DropPoolHeaderIconSize = 25.2f;

    public SkyPrisonItemPoolPage(SkyPrisonEditorContext context) : base(context) { }

    public override string TabName => "物品池";

    public override void OnEnable()
    {
        Refresh();
    }

    public override void Refresh()
    {
        string selectedPath = selectedDropProfile != null ? AssetDatabase.GetAssetPath(selectedDropProfile) : "";

        string[] guids = AssetDatabase.FindAssets("t:DropProfile");
        dropProfiles = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<DropProfile>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(x => x != null)
            .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName)
            .ThenBy(x => x.name)
            .ToList();

        if (!string.IsNullOrEmpty(selectedPath))
        {
            DropProfile matched = dropProfiles.FirstOrDefault(x => AssetDatabase.GetAssetPath(x) == selectedPath);
            if (matched != null)
                selectedDropProfile = matched;
        }

        if (selectedDropProfile == null && dropProfiles.Count > 0)
            selectedDropProfile = dropProfiles[0];
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

        DrawLeftLabelRow(titleRect, "物品掉落池列表");
        DrawLeftToolbarRow(toolbarRect);
        DrawLeftSearchRow(searchRect);
        DrawLeftListContainer(containerRect);
    }

    public override void OnGUIRight()
    {
        if (selectedDropProfile == null)
        {
            EditorGUILayout.HelpBox("请先在左侧选择一个物品掉落池。", MessageType.Info);
            return;
        }

        if (selectedDropSO == null || selectedDropSO.targetObject != selectedDropProfile)
            selectedDropSO = new SerializedObject(selectedDropProfile);

        selectedDropSO.Update();

        HandlePageShortcuts();

        DrawWorkspaceHeader();

        EditorGUILayout.Space(6f);

        DrawFoldoutSection("基础信息", DrawDropBasicInfo);
        DrawFoldoutSection("物品掉落池", DrawDropPools);

        selectedDropSO.ApplyModifiedProperties();

        if (GUI.changed)
            EditorUtility.SetDirty(selectedDropProfile);
    }

    private void DrawWorkspaceHeader()
    {
        EditorGUILayout.BeginVertical("box");

        string title = string.IsNullOrWhiteSpace(selectedDropProfile.displayName)
            ? selectedDropProfile.name
            : selectedDropProfile.displayName;

        EditorGUILayout.LabelField("物品池工作台", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);

        EditorGUILayout.Space(6f);

        DrawReadonlyRow("资源路径", AssetDatabase.GetAssetPath(selectedDropProfile));
        if (!string.IsNullOrWhiteSpace(selectedDropProfile.profileId))
            DrawReadonlyRow("ID", selectedDropProfile.profileId);

        SerializedProperty displayNameProp = selectedDropSO.FindProperty("displayName");
        if (displayNameProp != null)
            DrawRow("显示名称", displayNameProp);

        EditorGUILayout.Space(4f);
        DrawPingButtons(selectedDropProfile);

        EditorGUILayout.EndVertical();
    }

    private void DrawLeftLabelRow(Rect rect, string label)
    {
        GUI.Label(rect, label, EditorStyles.boldLabel);
    }

    private void DrawLeftToolbarRow(Rect rect)
    {
        const float buttonSize = 20f;
        const float gap = 4f;

        float y = rect.y + (rect.height - buttonSize) * 0.5f;
        float right = rect.xMax;

        Rect refreshRect = new Rect(right - buttonSize, y, buttonSize, buttonSize);
        Rect minusRect = new Rect(refreshRect.x - gap - buttonSize, y, buttonSize, buttonSize);
        Rect plusRect = new Rect(minusRect.x - gap - buttonSize, y, buttonSize, buttonSize);

        if (DrawToolButton(plusRect, "+", "新建掉落池"))
            CreateNewDropProfile();

        using (new EditorGUI.DisabledScope(selectedDropProfile == null))
        {
            if (DrawToolButton(minusRect, "-", "删除当前掉落池"))
                DeleteSelectedDropProfile();
        }

        if (DrawToolButton(refreshRect, "↻", "刷新"))
            Refresh();
    }

    private void DrawLeftSearchRow(Rect rect)
    {
        string newSearch = EditorGUI.TextField(rect, search);
        if (newSearch != search)
        {
            search = newSearch;
            leftListScroll = Vector2.zero;
        }
    }

    private void DrawLeftListContainer(Rect rect)
    {
        EditorGUI.DrawRect(rect, leftTopBg);
        DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.06f));

        Rect viewRect = new Rect(
            rect.x + LeftContainerPadding,
            rect.y + LeftContainerPadding,
            rect.width - LeftContainerPadding * 2f,
            rect.height - LeftContainerPadding * 2f);

        List<DropProfile> filtered = GetFilteredProfiles();

        float contentHeight = Mathf.Max(viewRect.height, filtered.Count * LeftListRowHeight);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, viewRect.width - 14f), contentHeight);

        leftListScroll = GUI.BeginScrollView(viewRect, leftListScroll, contentRect, false, true);

        for (int i = 0; i < filtered.Count; i++)
        {
            DropProfile profile = filtered[i];
            Rect rowRect = new Rect(0f, i * LeftListRowHeight, contentRect.width, LeftListRowHeight);

            bool isSelected = selectedDropProfile == profile;
            bool hover = rowRect.Contains(Event.current.mousePosition);

            if (isSelected)
            {
                EditorGUI.DrawRect(rowRect, selectedRowGreen);
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 4f, rowRect.height), accentGreen);
            }
            else if (hover)
            {
                EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.05f));
            }

            string label = string.IsNullOrWhiteSpace(profile.displayName) ? profile.name : profile.displayName;

            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 6, 0, 0),
                normal = { textColor = isSelected ? Color.white : new Color(0.88f, 0.88f, 0.90f, 1f) }
            };

            if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
            {
                selectedDropProfile = profile;
                selectedDropSO = null;
                focusInRightEntryGrid = false;
                focusedPoolIndex = -1;
            }

            GUI.Label(rowRect, label, style);
        }

        if (filtered.Count == 0)
            GUI.Label(new Rect(4f, 2f, contentRect.width - 8f, 22f), "没有匹配的掉落池", EditorStyles.miniLabel);

        GUI.EndScrollView();
    }

    private List<DropProfile> GetFilteredProfiles()
    {
        IEnumerable<DropProfile> filtered = dropProfiles;
        if (!string.IsNullOrWhiteSpace(search))
        {
            string keyword = search.Trim().ToLowerInvariant();
            filtered = filtered.Where(x =>
                x != null &&
                (
                    (!string.IsNullOrEmpty(x.displayName) && x.displayName.ToLowerInvariant().Contains(keyword)) ||
                    (!string.IsNullOrEmpty(x.profileId) && x.profileId.ToLowerInvariant().Contains(keyword)) ||
                    x.name.ToLowerInvariant().Contains(keyword)
                ));
        }

        return filtered.ToList();
    }

    private bool DrawToolButton(Rect rect, string text, string tooltip)
    {
        Event e = Event.current;
        bool hover = rect.Contains(e.mousePosition);
        bool clicked = e.type == EventType.MouseDown && e.button == 0 && hover;

        Color bg = hover ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.04f);
        EditorGUI.DrawRect(rect, bg);
        DrawThinBorder(rect, new Color(1f, 1f, 1f, hover ? 0.12f : 0.05f));

        GUI.Label(rect, new GUIContent(text, tooltip), GetCenteredToolbarTextStyle());

        if (clicked)
        {
            e.Use();
            GUI.changed = true;
            return true;
        }

        return false;
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

    private void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private void HandlePageShortcuts()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown)
            return;

        if (!focusInRightEntryGrid || focusedPoolIndex < 0)
            return;

        SerializedProperty pools = selectedDropSO.FindProperty("pools");
        if (focusedPoolIndex >= pools.arraySize)
            return;

        SerializedProperty pool = pools.GetArrayElementAtIndex(focusedPoolIndex);
        SerializedProperty entries = pool.FindPropertyRelative("entries");
        if (entries == null)
            return;

        int entryCount = entries.arraySize;
        int active = GetActiveEntryIndex(focusedPoolIndex);

        if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            if (DeleteSelectedEntries(focusedPoolIndex, entries))
                e.Use();
            return;
        }

        if (e.keyCode == KeyCode.Tab)
        {
            if (entryCount <= 0)
                return;

            int next = active < 0 ? 0 : active + (e.shift ? -1 : 1);
            next = Mathf.Clamp(next, 0, entryCount - 1);
            SelectSingleEntry(focusedPoolIndex, next);
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow)
        {
            if (entryCount <= 0)
                return;

            int next = active < 0 ? 0 : active + (e.keyCode == KeyCode.RightArrow ? 1 : -1);
            next = Mathf.Clamp(next, 0, entryCount - 1);

            if (e.shift)
                ExpandSelectionTo(focusedPoolIndex, next);
            else
                SelectSingleEntry(focusedPoolIndex, next);

            e.Use();
            return;
        }

        if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && active >= 0 && active < entryCount)
        {
            DropPoolMode mode = (DropPoolMode)pool.FindPropertyRelative("poolMode").enumValueIndex;
            OpenEditEntryWindow(focusedPoolIndex, active, mode);
            e.Use();
        }
    }

    private void DrawPingButtons(UnityEngine.Object target)
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

    private void DrawFoldoutSection(string title, Action drawer)
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

    private void DrawDropBasicInfo()
    {
        DrawRow("掉落表 ID", selectedDropSO.FindProperty("profileId"));
        DrawRow("显示名称", selectedDropSO.FindProperty("displayName"));
        DrawRow("备注", selectedDropSO.FindProperty("note"), true);
    }

    private HashSet<int> GetSelectionSet(int poolIndex)
    {
        if (!selectedEntryIndicesByPool.TryGetValue(poolIndex, out HashSet<int> set))
        {
            set = new HashSet<int>();
            selectedEntryIndicesByPool[poolIndex] = set;
        }
        return set;
    }

    private int GetActiveEntryIndex(int poolIndex)
    {
        return activeEntryIndexByPool.TryGetValue(poolIndex, out int value) ? value : -1;
    }

    private void SetActiveEntryIndex(int poolIndex, int value)
    {
        activeEntryIndexByPool[poolIndex] = value;
    }

    private int GetAnchorEntryIndex(int poolIndex)
    {
        return anchorEntryIndexByPool.TryGetValue(poolIndex, out int value) ? value : -1;
    }

    private void SetAnchorEntryIndex(int poolIndex, int value)
    {
        anchorEntryIndexByPool[poolIndex] = value;
    }

    private void SelectSingleEntry(int poolIndex, int entryIndex)
    {
        HashSet<int> set = GetSelectionSet(poolIndex);
        set.Clear();
        if (entryIndex >= 0)
            set.Add(entryIndex);

        SetActiveEntryIndex(poolIndex, entryIndex);
        SetAnchorEntryIndex(poolIndex, entryIndex);
        focusedPoolIndex = poolIndex;
        focusInRightEntryGrid = true;
        GUI.changed = true;
    }

    private void ToggleSingleEntry(int poolIndex, int entryIndex)
    {
        HashSet<int> set = GetSelectionSet(poolIndex);
        if (set.Contains(entryIndex))
            set.Remove(entryIndex);
        else
            set.Add(entryIndex);

        SetActiveEntryIndex(poolIndex, entryIndex);
        SetAnchorEntryIndex(poolIndex, entryIndex);
        focusedPoolIndex = poolIndex;
        focusInRightEntryGrid = true;
        GUI.changed = true;
    }

    private void ExpandSelectionTo(int poolIndex, int targetIndex)
    {
        int anchor = GetAnchorEntryIndex(poolIndex);
        if (anchor < 0)
            anchor = targetIndex;

        HashSet<int> set = GetSelectionSet(poolIndex);
        set.Clear();

        int min = Mathf.Min(anchor, targetIndex);
        int max = Mathf.Max(anchor, targetIndex);
        for (int i = min; i <= max; i++)
            set.Add(i);

        SetActiveEntryIndex(poolIndex, targetIndex);
        SetAnchorEntryIndex(poolIndex, anchor);
        focusedPoolIndex = poolIndex;
        focusInRightEntryGrid = true;
        GUI.changed = true;
    }

    private bool DeleteSelectedEntries(int poolIndex, SerializedProperty entries)
    {
        HashSet<int> set = GetSelectionSet(poolIndex);
        if (set.Count == 0)
            return false;

        List<int> indices = set.OrderByDescending(x => x).ToList();
        foreach (int idx in indices)
        {
            if (idx >= 0 && idx < entries.arraySize)
                entries.DeleteArrayElementAtIndex(idx);
        }

        selectedDropSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(selectedDropProfile);

        set.Clear();

        int remaining = entries.arraySize;
        if (remaining > 0)
        {
            int next = Mathf.Clamp(indices.Min(), 0, remaining - 1);
            set.Add(next);
            SetActiveEntryIndex(poolIndex, next);
            SetAnchorEntryIndex(poolIndex, next);
        }
        else
        {
            SetActiveEntryIndex(poolIndex, -1);
            SetAnchorEntryIndex(poolIndex, -1);
        }

        GUI.changed = true;
        return true;
    }

    private Vector2 GetEntryGridScroll(int poolIndex)
    {
        return entryGridScrollByPool.TryGetValue(poolIndex, out Vector2 value) ? value : Vector2.zero;
    }

    private void SetEntryGridScroll(int poolIndex, Vector2 value)
    {
        entryGridScrollByPool[poolIndex] = value;
    }

    private string GetEntryDisplayName(SerializedProperty entry)
    {
        UnityEngine.Object itemObj = GetEntryItemObject(entry);
        return GetItemDisplayName(itemObj);
    }

    private UnityEngine.Object GetEntryItemObject(SerializedProperty entry)
    {
        SerializedProperty itemProp = entry.FindPropertyRelative("itemDefinition");
        return itemProp != null ? itemProp.objectReferenceValue : null;
    }

    private string GetItemDisplayName(UnityEngine.Object itemObj)
    {
        if (itemObj == null)
            return "未绑定物品";

        try
        {
            SerializedObject itemSO = new SerializedObject(itemObj);
            SerializedProperty displayNameProp = itemSO.FindProperty("displayName");
            if (displayNameProp != null && !string.IsNullOrWhiteSpace(displayNameProp.stringValue))
                return displayNameProp.stringValue;
        }
        catch
        {
        }

        return itemObj.name;
    }

    private Texture2D GetEntryIconTexture(SerializedProperty entry)
    {
        return GetItemIconTexture(GetEntryItemObject(entry));
    }

    private Texture2D GetItemIconTexture(UnityEngine.Object itemObj)
    {
        if (itemObj == null)
            return null;

        try
        {
            SerializedObject itemSO = new SerializedObject(itemObj);
            SerializedProperty iconProp = itemSO.FindProperty("icon");
            if (iconProp != null && iconProp.objectReferenceValue != null)
            {
                if (iconProp.objectReferenceValue is Sprite sprite && sprite != null)
                    return sprite.texture;

                if (iconProp.objectReferenceValue is Texture2D tex && tex != null)
                    return tex;
            }
        }
        catch
        {
        }

        return AssetPreview.GetAssetPreview(itemObj) ?? AssetPreview.GetMiniThumbnail(itemObj);
    }

    private bool IsEntryEnabled(SerializedProperty entry)
    {
        SerializedProperty enabledProp = entry.FindPropertyRelative("enabled");
        return enabledProp == null || enabledProp.boolValue;
    }

    private bool IsEntryMissingItem(SerializedProperty entry)
    {
        return GetEntryItemObject(entry) == null;
    }

    private string GetEntryRateText(SerializedProperty entry, DropPoolMode poolMode)
    {
        if (poolMode == DropPoolMode.IndependentRolls)
        {
            SerializedProperty chanceProp = entry.FindPropertyRelative("dropChance");
            return chanceProp != null ? $"{chanceProp.floatValue:0.##}%" : "-";
        }

        SerializedProperty weightProp = entry.FindPropertyRelative("weight");
        return weightProp != null ? $"权重 {weightProp.floatValue:0.##}" : "-";
    }

    private string GetEntryCountText(SerializedProperty entry)
    {
        SerializedProperty minProp = entry.FindPropertyRelative("minCount");
        SerializedProperty maxProp = entry.FindPropertyRelative("maxCount");

        int min = minProp != null ? minProp.intValue : 0;
        int max = maxProp != null ? maxProp.intValue : 0;

        return min == max ? min.ToString() : $"{min}~{max}";
    }

    private void EnsureHeaderIconsLoaded()
    {
        if (dropPoolHeaderIconLoaded)
            return;

        dropPoolHeaderIcon = LoadEditorIcon(14);
        dropPoolHeaderIconLoaded = true;
    }

    private Texture2D LoadEditorIcon(int number)
    {
        string num = number.ToString("00");
        string basePath = EditorIconFolder + EditorIconPrefix + num;

        string[] paths =
        {
            basePath + ".png",
            basePath + ".tga",
            basePath + ".jpg",
            basePath + ".jpeg",
            basePath + ".psd"
        };

        for (int i = 0; i < paths.Length; i++)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
            if (tex != null)
                return tex;
        }

        return null;
    }

    private void DrawDropPoolHeaderRow(string title)
    {
        EnsureHeaderIconsLoaded();

        Rect rowRect = EditorGUILayout.GetControlRect(false, 28f);
        float x = rowRect.x;

        if (dropPoolHeaderIcon != null)
        {
            Rect iconRect = new Rect(
                x,
                rowRect.y + (rowRect.height - DropPoolHeaderIconSize) * 0.5f,
                DropPoolHeaderIconSize,
                DropPoolHeaderIconSize
            );
            GUI.DrawTexture(iconRect, dropPoolHeaderIcon, ScaleMode.ScaleToFit, true);
            x = iconRect.xMax + 6f;
        }

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        EditorGUI.LabelField(new Rect(x, rowRect.y + 4f, rowRect.width - (x - rowRect.x), 20f), title, titleStyle);
    }

    private void DrawEntryCard(Rect entryRect, SerializedProperty entry, DropPoolMode poolMode, bool selected, bool active)
    {
        bool enabled = IsEntryEnabled(entry);
        bool missingItem = IsEntryMissingItem(entry);

        EditorGUI.DrawRect(entryRect, EntryCardBg);

        if (selected)
            EditorGUI.DrawRect(entryRect, EntryCardSelected);

        DrawCardBorder(entryRect, active ? EntryCardActiveOutline : new Color(1f, 1f, 1f, 0.08f));

        Color oldColor = GUI.color;

        if (!enabled)
            GUI.color = EntryCardDisabled;
        else if (missingItem)
            GUI.color = EntryCardError;

        Texture2D icon = GetEntryIconTexture(entry);
        Rect iconRect = new Rect(
            entryRect.x + (entryRect.width - EntryCardIconSize) * 0.5f,
            entryRect.y + 5f,
            EntryCardIconSize,
            EntryCardIconSize
        );

        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(1f, 1f, 1f, 0.08f));

        string name = GetEntryDisplayName(entry);
        GUI.Label(new Rect(entryRect.x + 6f, entryRect.y + 64f, entryRect.width - 12f, 18f), new GUIContent(name, name), EditorStyles.miniBoldLabel);

        GUI.Label(new Rect(entryRect.x + 6f, entryRect.yMax - 18f, 44f, 16f), GetEntryCountText(entry), EditorStyles.miniLabel);
        GUI.Label(new Rect(entryRect.xMax - 44f, entryRect.yMax - 18f, 38f, 16f), GetEntryRateText(entry, poolMode), EditorStyles.miniLabel);

        if (missingItem && enabled)
            EditorGUI.DrawRect(new Rect(entryRect.xMax - 10f, entryRect.y + 6f, 6f, 6f), EntryCardError);

        GUI.color = oldColor;
    }

    private void DrawAddEntryCard(Rect rect)
    {
        bool hover = rect.Contains(Event.current.mousePosition);

        EditorGUI.DrawRect(rect, AddCardBg);
        if (hover)
            EditorGUI.DrawRect(rect, AddCardHover);

        DrawCardBorder(rect, AddCardBorder);

        Color oldColor = GUI.color;

        GUI.color = AddCardPlus;
        GUIStyle plusStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 34,
            alignment = TextAnchor.MiddleCenter
        };
        GUI.Label(new Rect(rect.x, rect.y + 10f, rect.width, 36f), "+", plusStyle);

        GUI.color = AddCardText;
        GUIStyle textStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter
        };
        GUI.Label(new Rect(rect.x + 4f, rect.y + 66f, rect.width - 8f, 18f), "添加物品", textStyle);

        GUI.color = oldColor;
    }

    private void DrawCardBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private void OpenAddEntryWindow(int poolIndex, DropPoolMode poolMode)
    {
        SkyPrisonItemPoolEntryEditorWindow.OpenCreate(
            poolMode,
            model => ValidateDuplicateOnSubmit(poolIndex, model, -1),
            model =>
            {
                SerializedProperty pools = selectedDropSO.FindProperty("pools");
                if (poolIndex < 0 || poolIndex >= pools.arraySize)
                    return;

                SerializedProperty pool = pools.GetArrayElementAtIndex(poolIndex);
                SerializedProperty entries = pool.FindPropertyRelative("entries");
                if (entries == null)
                    return;

                int newIndex = entries.arraySize;
                entries.arraySize++;
                SerializedProperty entry = entries.GetArrayElementAtIndex(newIndex);

                ApplyModelToEntry(entry, model);

                selectedDropSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(selectedDropProfile);

                SelectSingleEntry(poolIndex, newIndex);
            }
        );
    }

    private void OpenEditEntryWindow(int poolIndex, int entryIndex, DropPoolMode poolMode)
    {
        SerializedProperty pools = selectedDropSO.FindProperty("pools");
        if (poolIndex < 0 || poolIndex >= pools.arraySize)
            return;

        SerializedProperty pool = pools.GetArrayElementAtIndex(poolIndex);
        SerializedProperty entries = pool.FindPropertyRelative("entries");
        if (entries == null || entryIndex < 0 || entryIndex >= entries.arraySize)
            return;

        SerializedProperty entry = entries.GetArrayElementAtIndex(entryIndex);
        SkyPrisonItemPoolEntryEditorWindow.EntryEditModel model = BuildModelFromEntry(entry);

        SkyPrisonItemPoolEntryEditorWindow.OpenEdit(
            model,
            poolMode,
            updated => ValidateDuplicateOnSubmit(poolIndex, updated, entryIndex),
            updated =>
            {
                SerializedProperty latestPools = selectedDropSO.FindProperty("pools");
                if (poolIndex < 0 || poolIndex >= latestPools.arraySize)
                    return;

                SerializedProperty latestPool = latestPools.GetArrayElementAtIndex(poolIndex);
                SerializedProperty latestEntries = latestPool.FindPropertyRelative("entries");
                if (latestEntries == null || entryIndex < 0 || entryIndex >= latestEntries.arraySize)
                    return;

                SerializedProperty latestEntry = latestEntries.GetArrayElementAtIndex(entryIndex);
                ApplyModelToEntry(latestEntry, updated);

                selectedDropSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(selectedDropProfile);

                SelectSingleEntry(poolIndex, entryIndex);
            }
        );
    }

    private string ValidateDuplicateOnSubmit(int poolIndex, SkyPrisonItemPoolEntryEditorWindow.EntryEditModel model, int editingIndex)
    {
        if (model == null || model.itemDefinition == null)
            return "";

        SerializedProperty pools = selectedDropSO.FindProperty("pools");
        if (poolIndex < 0 || poolIndex >= pools.arraySize)
            return "";

        SerializedProperty pool = pools.GetArrayElementAtIndex(poolIndex);
        SerializedProperty entries = pool.FindPropertyRelative("entries");
        if (entries == null)
            return "";

        for (int i = 0; i < entries.arraySize; i++)
        {
            if (i == editingIndex)
                continue;

            SerializedProperty itemProp = entries.GetArrayElementAtIndex(i).FindPropertyRelative("itemDefinition");
            if (itemProp != null && itemProp.objectReferenceValue == model.itemDefinition)
                return "该物品已存在于当前掉落池中。";
        }

        return "";
    }

    private SkyPrisonItemPoolEntryEditorWindow.EntryEditModel BuildModelFromEntry(SerializedProperty entry)
    {
        return new SkyPrisonItemPoolEntryEditorWindow.EntryEditModel
        {
            enabled = entry.FindPropertyRelative("enabled")?.boolValue ?? true,
            itemDefinition = entry.FindPropertyRelative("itemDefinition")?.objectReferenceValue,
            dropChance = entry.FindPropertyRelative("dropChance")?.floatValue ?? 0f,
            weight = entry.FindPropertyRelative("weight")?.floatValue ?? 1f,
            minCount = entry.FindPropertyRelative("minCount")?.intValue ?? 1,
            maxCount = entry.FindPropertyRelative("maxCount")?.intValue ?? 1,
            note = entry.FindPropertyRelative("note")?.stringValue ?? ""
        };
    }

    private void ApplyModelToEntry(SerializedProperty entry, SkyPrisonItemPoolEntryEditorWindow.EntryEditModel model)
    {
        SerializedProperty enabledProp = entry.FindPropertyRelative("enabled");
        SerializedProperty itemProp = entry.FindPropertyRelative("itemDefinition");
        SerializedProperty chanceProp = entry.FindPropertyRelative("dropChance");
        SerializedProperty weightProp = entry.FindPropertyRelative("weight");
        SerializedProperty minProp = entry.FindPropertyRelative("minCount");
        SerializedProperty maxProp = entry.FindPropertyRelative("maxCount");
        SerializedProperty noteProp = entry.FindPropertyRelative("note");

        if (enabledProp != null) enabledProp.boolValue = model.enabled;
        if (itemProp != null) itemProp.objectReferenceValue = model.itemDefinition;
        if (chanceProp != null) chanceProp.floatValue = model.dropChance;
        if (weightProp != null) weightProp.floatValue = model.weight;
        if (minProp != null) minProp.intValue = Mathf.Max(0, model.minCount);
        if (maxProp != null) maxProp.intValue = Mathf.Max(model.minCount, model.maxCount);
        if (noteProp != null) noteProp.stringValue = model.note ?? "";
    }

    private void DrawEntryGridContainer(SerializedProperty entries, int poolIndex, DropPoolMode poolMode)
    {
        Rect outerRect = EditorGUILayout.GetControlRect(false, EntryContainerHeight, GUILayout.ExpandWidth(true));
        GUI.Box(outerRect, GUIContent.none);

        Event clickEvent = Event.current;
        if (clickEvent.type == EventType.MouseDown && outerRect.Contains(clickEvent.mousePosition))
        {
            focusInRightEntryGrid = true;
            focusedPoolIndex = poolIndex;
        }

        Rect viewRect = new Rect(outerRect.x + 1f, outerRect.y + 1f, outerRect.width - 2f, outerRect.height - 2f);

        int totalCardCount = entries.arraySize + 1;

        float availableWidth = Mathf.Max(
            EntryCardWidth,
            viewRect.width - EntryContainerPadding * 2f - EntryContainerScrollbarReserve
        );

        int columnCount = Mathf.Max(1, Mathf.FloorToInt((availableWidth + EntryCardSpacing) / (EntryCardWidth + EntryCardSpacing)));

        float rowWidth = columnCount * EntryCardWidth + Mathf.Max(0, columnCount - 1) * EntryCardSpacing;
        int rowCount = Mathf.CeilToInt(totalCardCount / (float)columnCount);

        float contentHeight = EntryContainerPadding * 2f +
                              rowCount * EntryCardHeight +
                              Mathf.Max(0, rowCount - 1) * EntryCardSpacing;

        Rect contentRect = new Rect(
            0f,
            0f,
            Mathf.Max(viewRect.width - 16f, rowWidth + EntryContainerPadding * 2f),
            Mathf.Max(viewRect.height, contentHeight)
        );

        Vector2 scroll = GetEntryGridScroll(poolIndex);
        scroll = GUI.BeginScrollView(viewRect, scroll, contentRect);

        float startX = EntryContainerPadding;
        float startY = EntryContainerPadding;

        HashSet<int> selectedSet = GetSelectionSet(poolIndex);
        int activeIndex = GetActiveEntryIndex(poolIndex);

        for (int i = 0; i < totalCardCount; i++)
        {
            int row = i / columnCount;
            int col = i % columnCount;

            Rect cardRect = new Rect(
                startX + col * (EntryCardWidth + EntryCardSpacing),
                startY + row * (EntryCardHeight + EntryCardSpacing),
                EntryCardWidth,
                EntryCardHeight
            );

            bool isAddCard = i == entries.arraySize;

            if (isAddCard)
            {
                DrawAddEntryCard(cardRect);

                Event e = Event.current;
                if (e.type == EventType.MouseDown && cardRect.Contains(e.mousePosition) && e.button == 0)
                {
                    focusInRightEntryGrid = true;
                    focusedPoolIndex = poolIndex;
                    OpenAddEntryWindow(poolIndex, poolMode);
                    e.Use();
                }
            }
            else
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                DrawEntryCard(cardRect, entry, poolMode, selectedSet.Contains(i), activeIndex == i);

                Event e = Event.current;
                if (e.type == EventType.MouseDown && cardRect.Contains(e.mousePosition))
                {
                    focusInRightEntryGrid = true;
                    focusedPoolIndex = poolIndex;

                    if (e.button == 0)
                    {
                        if (e.shift)
                            ExpandSelectionTo(poolIndex, i);
                        else if (e.control || e.command)
                            ToggleSingleEntry(poolIndex, i);
                        else
                            SelectSingleEntry(poolIndex, i);

                        if (e.clickCount >= 2)
                            OpenEditEntryWindow(poolIndex, i, poolMode);

                        e.Use();
                    }
                    else if (e.button == 1)
                    {
                        if (!selectedSet.Contains(i))
                            SelectSingleEntry(poolIndex, i);

                        ShowEntryContextMenu(entries, poolIndex, i, poolMode);
                        e.Use();
                    }
                }
            }
        }

        GUI.EndScrollView();
        SetEntryGridScroll(poolIndex, scroll);
    }

    private void ShowEntryContextMenu(SerializedProperty entries, int poolIndex, int entryIndex, DropPoolMode poolMode)
    {
        GenericMenu menu = new GenericMenu();

        HashSet<int> set = GetSelectionSet(poolIndex);
        bool multi = set.Count > 1;

        if (!multi)
        {
            menu.AddItem(new GUIContent("编辑"), false, () =>
            {
                OpenEditEntryWindow(poolIndex, entryIndex, poolMode);
            });
            menu.AddSeparator("");
        }

        menu.AddItem(new GUIContent(multi ? "批量删除" : "删除"), false, () =>
        {
            DeleteSelectedEntries(poolIndex, entries);
        });

        menu.ShowAsContext();
    }

    private void DrawDropPools()
    {
        SerializedProperty pools = selectedDropSO.FindProperty("pools");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("添加掉落池"))
        {
            int newIdx = pools.arraySize;
            pools.arraySize++;
            SerializedProperty newPool = pools.GetArrayElementAtIndex(newIdx);
            SerializedProperty enabledProp = newPool.FindPropertyRelative("enabled");
            SerializedProperty nameProp = newPool.FindPropertyRelative("poolName");
            if (enabledProp != null) enabledProp.boolValue = true;
            if (nameProp != null) nameProp.stringValue = "新掉落池";
        }

        if (GUILayout.Button("清空掉落池"))
        {
            if (EditorUtility.DisplayDialog("清空掉落池", "确定清空所有掉落池吗？", "确定", "取消"))
                pools.ClearArray();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);

        for (int i = 0; i < pools.arraySize; i++)
        {
            SerializedProperty pool = pools.GetArrayElementAtIndex(i);

            SerializedProperty enabled = pool.FindPropertyRelative("enabled");
            SerializedProperty poolName = pool.FindPropertyRelative("poolName");
            SerializedProperty poolNote = pool.FindPropertyRelative("poolNote");
            SerializedProperty poolMode = pool.FindPropertyRelative("poolMode");
            SerializedProperty pickCount = pool.FindPropertyRelative("pickCount");
            SerializedProperty entries = pool.FindPropertyRelative("entries");

            EditorGUILayout.BeginVertical("box");

            bool isEnabled = enabled != null && enabled.boolValue;
            string poolLabel = isEnabled ? $"掉落池 {i + 1}" : $"掉落池 {i + 1}  ⚠ 已禁用";
            DrawDropPoolHeaderRow(poolLabel);

            DrawRow("启用", enabled);
            DrawRow("池名称", poolName);
            DrawRow("池备注", poolNote, true);
            DrawRow("池模式", poolMode);

            DropPoolMode mode = (DropPoolMode)poolMode.enumValueIndex;
            if (mode == DropPoolMode.WeightedPick)
                DrawRow("抽取次数", pickCount);

            EditorGUILayout.Space(8f);
            DrawEntryGridContainer(entries, i, mode);

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("删除此池", GUILayout.Height(24f)))
            {
                pools.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndVertical();
                break;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }
    }

    private void CreateNewDropProfile()
    {
        EnsureFolderExists(DefaultDropCreateFolder);

        DropProfile asset = ScriptableObject.CreateInstance<DropProfile>();
        asset.displayName = "新掉落池";
        asset.profileId = "new_drop_profile";

        string path = AssetDatabase.GenerateUniqueAssetPath(DefaultDropCreateFolder + "/DP_NewDropProfile.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Refresh();
        selectedDropProfile = asset;
        selectedDropSO = null;
    }

    private void DeleteSelectedDropProfile()
    {
        if (selectedDropProfile == null)
            return;

        string path = AssetDatabase.GetAssetPath(selectedDropProfile);
        if (string.IsNullOrEmpty(path))
            return;

        bool ok = EditorUtility.DisplayDialog(
            "删除物品掉落池",
            "确定删除当前物品掉落池吗？",
            "删除",
            "取消"
        );

        if (!ok)
            return;

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        selectedDropProfile = null;
        selectedDropSO = null;
        focusInRightEntryGrid = false;
        focusedPoolIndex = -1;

        Refresh();
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

    private void DrawReadonlyRow(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(value) ? "-" : value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawRow(string label, SerializedProperty property, bool multiline = false)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));

        if (property == null)
            EditorGUILayout.LabelField("字段不存在");
        else if (multiline && property.propertyType == SerializedPropertyType.String)
            property.stringValue = EditorGUILayout.TextArea(property.stringValue, GUILayout.MinHeight(54f));
        else
            EditorGUILayout.PropertyField(property, GUIContent.none, true);

        EditorGUILayout.EndHorizontal();
    }
}