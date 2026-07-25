using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonActionModuleListPanel
{
    private enum ClipMode { None, Copy, Cut }

    private const float TitleH   = 22f;
    private const float ToolbarH = 24f;
    private const float SearchH  = 22f;
    private const float RowGap   = 6f;
    private const float Pad      = 8f;
    private const float RowH     = 24f;

    private readonly SkyPrisonSkillPage page;
    private string  search     = "";
    private Vector2 listScroll;
    private string  createFolder = SkyPrisonSkillPage.DefaultModuleCreateFolder;

    private WeaponCombatModule clipboard;
    private ClipMode               clipMode = ClipMode.None;

    public SkyPrisonActionModuleListPanel(SkyPrisonSkillPage page) { this.page = page; }

    public void OnRefresh() { }
    public void HandlePostGUI() { }

    public void CopyModule(WeaponCombatModule m) { clipboard = m; clipMode = ClipMode.Copy; }
    public void CutModule(WeaponCombatModule  m) { clipboard = m; clipMode = ClipMode.Cut;  }

    public bool TryPaste()
    {
        if (clipboard == null) return false;
        PasteToFolder(createFolder);
        return true;
    }

    public void Draw()
    {
        Rect full = GUILayoutUtility.GetRect(0, 100000, 0, 100000,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        float y = full.y;
        Rect titleR   = new Rect(full.x, y, full.width, TitleH);   y += TitleH + RowGap;
        Rect toolbarR = new Rect(full.x, y, full.width, ToolbarH); y += ToolbarH + RowGap;
        Rect searchR  = new Rect(full.x, y, full.width, SearchH);  y += SearchH + RowGap;
        Rect listR    = new Rect(full.x, y, full.width, Mathf.Max(40f, full.yMax - y));

        GUI.Label(titleR, "战斗模组列表", EditorStyles.boldLabel);
        DrawToolbar(toolbarR);
        DrawSearch(searchR);
        DrawList(listR);
    }

    private void DrawToolbar(Rect rect)
    {
        const float sz = 20f, gap = 4f;
        float cy = rect.y + (rect.height - sz) * 0.5f, right = rect.xMax;
        Rect rR = new Rect(right - sz, cy, sz, sz);
        Rect mR = new Rect(rR.x - gap - sz, cy, sz, sz);
        Rect pR = new Rect(mR.x - gap - sz, cy, sz, sz);

        if (DrawBtn(pR, "+", "新建战斗模组")) CreateModule(createFolder);
        using (new EditorGUI.DisabledScope(page.SelectedModule == null))
        { if (DrawBtn(mR, "-", "删除战斗模组")) DeleteModule(page.SelectedModule); }
        if (DrawBtn(rR, "↻", "刷新")) page.Refresh();
    }

    private void DrawSearch(Rect rect)
    {
        string next = EditorGUI.TextField(rect, search);
        if (next != search) { search = next; listScroll = Vector2.zero; }
    }

    private void DrawList(Rect rect)
    {
        EditorGUI.DrawRect(rect, page.LeftTopBg);
        page.DrawThinBorder(rect, new Color(1,1,1,0.06f));

        Rect view    = new Rect(rect.x + Pad, rect.y + Pad, rect.width - Pad * 2, rect.height - Pad * 2);
        var  modules = page.ModuleDefinitions
            .Where(m => m != null && (string.IsNullOrWhiteSpace(search) ||
                m.displayName.ToLowerInvariant().Contains(search.ToLowerInvariant()) ||
                m.moduleKey.ToLowerInvariant().Contains(search.ToLowerInvariant())))
            .ToList();

        float contentH = Mathf.Max(view.height, modules.Count * RowH);
        Rect content   = new Rect(0, 0, Mathf.Max(10f, view.width - 14f), contentH);

        listScroll = GUI.BeginScrollView(view, listScroll, content, false, true);

        for (int i = 0; i < modules.Count; i++)
        {
            WeaponCombatModule m = modules[i];
            Rect row = new Rect(0, i * RowH, content.width, RowH);
            DrawRow(row, m);
        }

        if (modules.Count == 0)
            GUI.Label(new Rect(4, 2, content.width - 8, 22), "没有匹配的战斗模组", EditorStyles.miniLabel);

        GUI.EndScrollView();
    }

    private void DrawRow(Rect row, WeaponCombatModule m)
    {
        bool selected = page.SelectedModule == m;
        bool hover    = row.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(row, page.SelectedRowMustard);
            EditorGUI.DrawRect(new Rect(row.x, row.y, 3f, row.height), page.AccentMustard);
        }
        else if (hover)
            EditorGUI.DrawRect(row, new Color(1,1,1,0.05f));

        var style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = selected ? Color.white : new Color(0.90f,0.90f,0.92f) }
        };
        string label = (string.IsNullOrWhiteSpace(m.displayName) ? m.name : m.displayName)
                     + $"  [{m.moduleKey}]";
        GUI.Label(new Rect(row.x + 8, row.y, row.width - 8, row.height), label, style);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
        {
            if (e.button == 0) { page.SelectModule(m); e.Use(); }
            else if (e.button == 1) { page.SelectModule(m); ShowContextMenu(m); e.Use(); }
        }
    }

    private void ShowContextMenu(WeaponCombatModule m)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("复制"), false, () => CopyModule(m));
        menu.AddItem(new GUIContent("剪切"), false, () => CutModule(m));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("删除"), false, () => DeleteModule(m));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("在 Project 中定位"), false, () =>
        { Selection.activeObject = m; EditorGUIUtility.PingObject(m); });
        menu.ShowAsContext();
    }

    private void CreateModule(string folder)
    {
        page.EnsureFolderExists(folder);
        WeaponCombatModule asset = ScriptableObject.CreateInstance<WeaponCombatModule>();
        asset.moduleKey   = page.GenerateUniqueModuleKey("new_module");
        asset.displayName = "新战斗模组";

        string path = AssetDatabase.GenerateUniqueAssetPath(folder.TrimEnd('/') + "/AM_NewModule.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        page.Refresh();
        page.SelectModule(asset);
        createFolder = folder;
    }

    public bool DeleteModule(WeaponCombatModule m)
    {
        if (m == null) return false;
        string path = AssetDatabase.GetAssetPath(m);
        if (string.IsNullOrEmpty(path)) return false;

        bool ok = EditorUtility.DisplayDialog("删除战斗模组",
            $"确定删除：\n{m.name}\n\n此操作会删除资源文件。", "删除", "取消");
        if (!ok) return false;

        if (page.SelectedModule == m) page.ClearModuleSelection();
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        page.Refresh();
        return true;
    }

    private void PasteToFolder(string folder)
    {
        if (clipboard == null) return;
        if (clipMode == ClipMode.Copy) PasteCopy(folder);
        else
        {
            string src = AssetDatabase.GetAssetPath(clipboard);
            page.EnsureFolderExists(folder);
            string dst = AssetDatabase.GenerateUniqueAssetPath(folder.TrimEnd('/') + "/" + Path.GetFileName(src));
            AssetDatabase.MoveAsset(src, dst);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            page.Refresh();
            page.SelectModule(AssetDatabase.LoadAssetAtPath<WeaponCombatModule>(dst));
            clipboard = null; clipMode = ClipMode.None;
        }
    }

    private void PasteCopy(string folder)
    {
        page.EnsureFolderExists(folder);
        WeaponCombatModule clone = Object.Instantiate(clipboard);
        clone.name      = clipboard.name + "_Copy";
        clone.moduleKey = page.GenerateUniqueModuleKey(clipboard.moduleKey + "_copy");
        string path = AssetDatabase.GenerateUniqueAssetPath(folder.TrimEnd('/') + "/" + clipboard.name + "_Copy.asset");
        AssetDatabase.CreateAsset(clone, path);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        page.Refresh();
        page.SelectModule(clone);
    }

    private bool DrawBtn(Rect r, string text, string tip)
    {
        Event e = Event.current;
        bool  h = r.Contains(e.mousePosition);
        EditorGUI.DrawRect(r, h ? new Color(1,1,1,0.10f) : new Color(1,1,1,0.04f));
        page.DrawThinBorder(r, new Color(1,1,1, h ? 0.12f : 0.05f));
        GUI.Label(r, new GUIContent(text, tip), page.CenteredToolbarStyle());
        if (e.type == EventType.MouseDown && e.button == 0 && h) { e.Use(); GUI.changed = true; return true; }
        return false;
    }
}
