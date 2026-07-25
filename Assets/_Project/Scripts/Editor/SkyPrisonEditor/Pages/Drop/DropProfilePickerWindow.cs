using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class DropProfilePickerWindow : EditorWindow
{
    private const float W = 860f;
    private const float H = 580f;
    private const float CardW = 240f;
    private const float CardH = 200f;
    private const float CardPad = 10f;

    private static Action<DropProfile> onConfirm;
    private static DropProfile current;

    private List<DropProfile> all = new List<DropProfile>();
    private DropProfile selected;
    private string search = "";
    private Vector2 scroll;

    public static void Open(DropProfile currentProfile, Action<DropProfile> callback)
    {
        onConfirm = callback;
        current = currentProfile;
        var w = GetWindow<DropProfilePickerWindow>(true, "选择掉落配置", true);
        w.minSize = w.maxSize = new Vector2(W, H);
        w.position = CenteredRect(W, H);
        w.ShowUtility();
        w.Focus();
    }

    private static Rect CenteredRect(float w, float h)
    {
        Rect main = EditorGUIUtility.GetMainWindowPosition();
        return new Rect(main.x + (main.width - w) * 0.5f, main.y + (main.height - h) * 0.5f, w, h);
    }

    private void OnEnable()
    {
        selected = current;
        Refresh();
    }

    private void Refresh()
    {
        all.Clear();
        string[] guids = AssetDatabase.FindAssets("t:DropProfile", new[] { "Assets/_Project" });
        foreach (string g in guids)
        {
            DropProfile dp = AssetDatabase.LoadAssetAtPath<DropProfile>(AssetDatabase.GUIDToAssetPath(g));
            if (dp != null) all.Add(dp);
        }
        all.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
    }

    private void OnGUI()
    {
        // 顶部搜索栏
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("搜索", GUILayout.Width(36f));
        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(40f))) Refresh();
        EditorGUILayout.EndHorizontal();

        // 卡片区域
        Rect area = GUILayoutUtility.GetRect(0, W, 0, H - 60f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUI.Box(area, GUIContent.none);

        List<DropProfile> filtered = Filter();
        int cols = Mathf.Max(1, Mathf.FloorToInt((area.width - CardPad) / (CardW + CardPad)));
        int rows = Mathf.CeilToInt(filtered.Count / (float)cols);
        float contentH = rows * (CardH + CardPad) + CardPad;

        Rect view = new Rect(area.x + 4f, area.y + 4f, area.width - 8f, area.height - 8f);
        Rect content = new Rect(0, 0, view.width, contentH);
        scroll = GUI.BeginScrollView(view, scroll, content);

        for (int i = 0; i < filtered.Count; i++)
        {
            int row = i / cols, col = i % cols;
            Rect card = new Rect(col * (CardW + CardPad) + CardPad, row * (CardH + CardPad) + CardPad, CardW, CardH);
            DrawCard(card, filtered[i]);
        }
        GUI.EndScrollView();

        // 底部确认栏
        EditorGUILayout.BeginHorizontal("box");
        GUILayout.Label(selected != null
            ? $"已选：{(!string.IsNullOrWhiteSpace(selected.displayName) ? selected.displayName : selected.name)}"
            : "未选择", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("取消", GUILayout.Width(80f), GUILayout.Height(26f))) Close();
        using (new EditorGUI.DisabledScope(selected == null))
            if (GUILayout.Button("确定", GUILayout.Width(80f), GUILayout.Height(26f))) Confirm();
        EditorGUILayout.EndHorizontal();
    }

    private static readonly Color SelBg = new Color(0.78f, 0.95f, 0.20f, 0.18f);
    private static readonly Color SelBorder = new Color(0.78f, 0.95f, 0.20f, 1f);
    private static readonly Color NormBg = new Color(1f, 1f, 1f, 0.03f);
    private static readonly Color NormBorder = new Color(1f, 1f, 1f, 0.08f);

    private void DrawCard(Rect r, DropProfile dp)
    {
        bool sel = selected == dp;
        EditorGUI.DrawRect(r, sel ? SelBg : NormBg);
        DrawBorder(r, sel ? SelBorder : NormBorder);

        float x = r.x + 6f;
        float y = r.y + 6f;
        float w = r.width - 12f;

        // 配置名称
        string title = !string.IsNullOrWhiteSpace(dp.displayName) ? dp.displayName : dp.name;
        GUI.Label(new Rect(x, y, w, 16f), title, new GUIStyle(EditorStyles.miniBoldLabel) { wordWrap = false });
        y += 18f;

        // 每个池
        for (int p = 0; p < dp.pools.Count; p++)
        {
            DropProfile.DropPool pool = dp.pools[p];
            if (pool == null) continue;

            string mode = pool.poolMode == DropPoolMode.IndependentRolls ? "独立" : "权重";
            GUI.Label(new Rect(x, y, w, 13f), $"[{mode}] {pool.poolName}", EditorStyles.miniLabel);
            y += 14f;

            // 每条物品
            for (int e2 = 0; e2 < pool.entries.Count; e2++)
            {
                DropProfile.DropEntry entry = pool.entries[e2];
                if (entry == null || entry.itemDefinition == null) continue;
                if (y + 20f > r.yMax - 6f) { GUI.Label(new Rect(x, y, w, 13f), "…", EditorStyles.miniLabel); goto NextClick; }

                // 图标
                const float iconSize = 18f;
                Texture2D icon = GetIcon(entry.itemDefinition);
                Rect iconRect = new Rect(x, y + 1f, iconSize, iconSize);
                if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
                else EditorGUI.DrawRect(iconRect, new Color(1f, 1f, 1f, 0.08f));

                // 名称 + 掉率
                string itemName = entry.itemDefinition is ItemDefinition id && !string.IsNullOrWhiteSpace(id.displayName)
                    ? id.displayName
                    : entry.itemDefinition.name;
                string rateStr = pool.poolMode == DropPoolMode.IndependentRolls
                    ? $"{entry.dropChance:0.#}%"
                    : $"权{entry.weight:0.#}";
                string countStr = entry.minCount == entry.maxCount ? $"x{entry.minCount}" : $"x{entry.minCount}~{entry.maxCount}";
                string line = $"{itemName}  {rateStr}  {countStr}";
                GUI.Label(new Rect(x + iconSize + 3f, y, w - iconSize - 3f, 20f), line, EditorStyles.miniLabel);
                y += 20f;
            }
        }

        NextClick:
        Event ev = Event.current;
        if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition) && ev.button == 0)
        {
            selected = dp;
            if (ev.clickCount == 2) Confirm();
            Repaint();
            ev.Use();
        }
    }

    private static Texture2D GetIcon(ScriptableObject obj)
    {
        if (obj is ItemDefinition itemDef && itemDef.icon != null && itemDef.icon.texture != null)
            return itemDef.icon.texture;
        Texture2D preview = AssetPreview.GetAssetPreview(obj);
        if (preview != null) return preview;
        return EditorGUIUtility.ObjectContent(obj, obj.GetType())?.image as Texture2D;
    }

    private void Confirm()
    {
        onConfirm?.Invoke(selected);
        Close();
    }

    private List<DropProfile> Filter()
    {
        if (string.IsNullOrWhiteSpace(search)) return all;
        string kw = search.Trim().ToLowerInvariant();
        var result = new List<DropProfile>();
        foreach (var dp in all)
            if (dp.name.ToLowerInvariant().Contains(kw) ||
                (dp.displayName ?? "").ToLowerInvariant().Contains(kw))
                result.Add(dp);
        return result;
    }

    private void DrawBorder(Rect r, Color c)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
    }
}
