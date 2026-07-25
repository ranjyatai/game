using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonItemPickerPopup : EditorWindow
{
    private const float WindowWidth = 920f;
    private const float WindowHeight = 760f;

    private const string WindowTitle = "选择物品";
    private const string PrefPosX = "SkyPrison_ItemPicker_PosX";
    private const string PrefPosY = "SkyPrison_ItemPicker_PosY";

    private const string DefaultSearchFolder = "Assets/_Project";
    private const float CardWidth = 96f;
    private const float CardHeight = 122f;
    private const float CardPadding = 10f;

    private static Action<UnityEngine.Object> onConfirm;
    private static UnityEngine.Object currentSelected;
    private static string[] includeTypeNames;

    private readonly List<UnityEngine.Object> items = new List<UnityEngine.Object>();
    private readonly List<string> discoveredTypeNames = new List<string>();

    private Vector2 scroll;
    private string search = "";
    private UnityEngine.Object selected;

    private enum SortMode
    {
        NameAsc = 0,
        NameDesc = 1,
        TypeAsc = 2
    }

    private SortMode sortMode = SortMode.NameAsc;
    private int typeFilterIndex = 0;

    public static void Open(
        UnityEngine.Object current,
        Action<UnityEngine.Object> confirmCallback,
        params string[] acceptedTypeNames)
    {
        onConfirm = confirmCallback;
        currentSelected = current;
        includeTypeNames = acceptedTypeNames != null && acceptedTypeNames.Length > 0
            ? acceptedTypeNames
            : new[] { "Item", "ItemDefinition", "ItemData", "SkyPrisonItemDefinition" };

        SkyPrisonItemPickerPopup window = GetWindow<SkyPrisonItemPickerPopup>(true, WindowTitle, true);
        window.minSize = new Vector2(WindowWidth, WindowHeight);
        window.maxSize = new Vector2(WindowWidth, WindowHeight);
        window.position = window.GetRememberedCenteredRect(WindowWidth, WindowHeight);
        window.ShowUtility();
        window.Focus();
    }

    private Rect GetRememberedCenteredRect(float width, float height)
    {
        float x = EditorPrefs.GetFloat(PrefPosX, -99999f);
        float y = EditorPrefs.GetFloat(PrefPosY, -99999f);

        if (x > -99990f && y > -99990f)
            return new Rect(x, y, width, height);

        Rect main = EditorGUIUtility.GetMainWindowPosition();
        return new Rect(
            main.x + (main.width - width) * 0.5f,
            main.y + (main.height - height) * 0.5f,
            width,
            height
        );
    }

    private void OnEnable()
    {
        selected = currentSelected;
        RefreshItems();
    }

    private void OnDisable()
    {
        EditorPrefs.SetFloat(PrefPosX, position.x);
        EditorPrefs.SetFloat(PrefPosY, position.y);
    }

    private void OnGUI()
    {
        HandleKeyboard();

        EditorGUILayout.BeginVertical();
        DrawHeader();
        DrawGridArea();
        DrawFooter();
        EditorGUILayout.EndVertical();
    }

    private void HandleKeyboard()
    {
        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            Close();
            e.Use();
        }
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("物品选择", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        search = EditorGUILayout.TextField("搜索", search);

        GUILayout.Space(8f);

        string[] typeOptions = BuildTypeOptions();
        typeFilterIndex = EditorGUILayout.Popup(typeFilterIndex, typeOptions, GUILayout.Width(130f));

        sortMode = (SortMode)EditorGUILayout.EnumPopup(sortMode, GUILayout.Width(120f));

        if (GUILayout.Button("刷新", GUILayout.Width(56f)))
            RefreshItems();

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private string[] BuildTypeOptions()
    {
        List<string> options = new List<string> { "全部类型" };
        options.AddRange(discoveredTypeNames);
        return options.ToArray();
    }

    private void DrawGridArea()
    {
        Rect outer = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUI.Box(outer, GUIContent.none);

        Rect inner = new Rect(outer.x + 8f, outer.y + 8f, outer.width - 16f, outer.height - 16f);

        List<UnityEngine.Object> filtered = GetFilteredItems();

        if (filtered.Count == 0)
        {
            GUILayout.BeginArea(inner);
            GUILayout.FlexibleSpace();
            EditorGUILayout.HelpBox("没有找到可选物品。", MessageType.Info);
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
            return;
        }

        int columns = Mathf.Max(1, Mathf.FloorToInt((inner.width + CardPadding) / (CardWidth + CardPadding)));
        int rows = Mathf.CeilToInt(filtered.Count / (float)columns);

        float contentWidth = columns * (CardWidth + CardPadding);
        float contentHeight = rows * (CardHeight + CardPadding);

        Rect viewRect = inner;
        Rect contentRect = new Rect(0f, 0f, contentWidth, contentHeight);

        scroll = GUI.BeginScrollView(viewRect, scroll, contentRect);

        for (int i = 0; i < filtered.Count; i++)
        {
            int row = i / columns;
            int col = i % columns;

            Rect cardRect = new Rect(
                col * (CardWidth + CardPadding),
                row * (CardHeight + CardPadding),
                CardWidth,
                CardHeight
            );

            DrawItemCard(cardRect, filtered[i]);
        }

        GUI.EndScrollView();
    }

    private static readonly Color SelectedCardBg     = new Color(0.78f, 0.95f, 0.20f, 0.18f);
    private static readonly Color SelectedCardBorder  = new Color(0.78f, 0.95f, 0.20f, 1f);
    private static readonly Color NormalCardBg        = new Color(1f, 1f, 1f, 0.03f);
    private static readonly Color NormalCardBorder    = new Color(1f, 1f, 1f, 0.08f);

    private void DrawItemCard(Rect rect, UnityEngine.Object obj)
    {
        bool isSelected = selected == obj;

        EditorGUI.DrawRect(rect, isSelected ? SelectedCardBg : NormalCardBg);
        DrawBorder(rect, isSelected ? SelectedCardBorder : NormalCardBorder);

        Texture2D icon = GetObjectIcon(obj);
        Rect iconRect = new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, 58f);

        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(iconRect, new Color(1f, 1f, 1f, 0.06f));

        string name = obj != null ? obj.name : "未命名";
        GUIStyle nameStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.UpperCenter,
            wordWrap = true
        };
        GUI.Label(new Rect(rect.x + 6f, rect.y + 72f, rect.width - 12f, 34f), name, nameStyle);

        string typeName = obj != null ? obj.GetType().Name : "-";
        GUI.Label(new Rect(rect.x + 6f, rect.yMax - 18f, rect.width - 12f, 14f), typeName, EditorStyles.centeredGreyMiniLabel);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && e.button == 0)
        {
            selected = obj;
            Repaint();

            if (e.clickCount == 2)
                ConfirmAndClose();

            e.Use();
        }
    }

    private void DrawFooter()
    {
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(selected != null ? $"当前选择：{selected.name}" : "当前选择：无", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("取消", GUILayout.Width(100f), GUILayout.Height(28f)))
            Close();

        using (new EditorGUI.DisabledScope(selected == null))
        {
            if (GUILayout.Button("确定", GUILayout.Width(100f), GUILayout.Height(28f)))
                ConfirmAndClose();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void ConfirmAndClose()
    {
        onConfirm?.Invoke(selected);
        Close();
    }

    private void RefreshItems()
    {
        items.Clear();
        discoveredTypeNames.Clear();

        string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { DefaultSearchFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj == null)
                continue;

            string typeName = obj.GetType().Name;
            if (includeTypeNames.Any(t => typeName == t))
            {
                items.Add(obj);
                if (!discoveredTypeNames.Contains(typeName))
                    discoveredTypeNames.Add(typeName);
            }
        }

        discoveredTypeNames.Sort(StringComparer.OrdinalIgnoreCase);
        items.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
    }

    private List<UnityEngine.Object> GetFilteredItems()
    {
        IEnumerable<UnityEngine.Object> query = items;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string keyword = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x != null &&
                (
                    x.name.ToLowerInvariant().Contains(keyword) ||
                    x.GetType().Name.ToLowerInvariant().Contains(keyword)
                ));
        }

        if (typeFilterIndex > 0)
        {
            string typeName = BuildTypeOptions()[typeFilterIndex];
            query = query.Where(x => x != null && x.GetType().Name == typeName);
        }

        switch (sortMode)
        {
            case SortMode.NameDesc:
                query = query.OrderByDescending(x => x.name);
                break;
            case SortMode.TypeAsc:
                query = query.OrderBy(x => x.GetType().Name).ThenBy(x => x.name);
                break;
            case SortMode.NameAsc:
            default:
                query = query.OrderBy(x => x.name);
                break;
        }

        return query.ToList();
    }

    private Texture2D GetObjectIcon(UnityEngine.Object obj)
    {
        if (obj == null)
            return null;

        // 优先使用 ItemDefinition 上配置的 icon Sprite
        if (obj is ItemDefinition itemDef && itemDef.icon != null && itemDef.icon.texture != null)
            return itemDef.icon.texture;

        Texture2D preview = AssetPreview.GetAssetPreview(obj);
        if (preview != null)
            return preview;

        GUIContent iconContent = EditorGUIUtility.ObjectContent(obj, obj.GetType());
        return iconContent?.image as Texture2D;
    }

    private void DrawBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }
}
