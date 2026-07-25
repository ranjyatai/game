using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SkyPrisonSkillAssetListPanel
{
    private class ListNode
    {
        public string         displayName;
        public string         fullKey;
        public string         assetFolderPath;
        public int            depth;
        public bool           isFolder;
        public SkillDefinition skill;
        public Rect           lastRect;
    }

    private enum ClipMode { None, Copy, Cut }

    private const float TitleH    = 22f;
    private const float ToolbarH  = 24f;
    private const float SearchH   = 22f;
    private const float RowGap    = 6f;
    private const float Pad       = 8f;
    private const float RowH      = 24f;

    private readonly SkyPrisonSkillPage     page;
    private readonly Dictionary<string,bool> folderExpanded = new Dictionary<string, bool>();
    private readonly List<ListNode>          visibleNodes   = new List<ListNode>();

    private string     search             = "";
    private string     selectedFolderPath = SkyPrisonSkillPage.DefaultSkillCreateFolder;
    private string     selectedFolderKey  = "";
    private Vector2    listScroll;
    private SkillDefinition clipboard;
    private ClipMode   clipMode = ClipMode.None;

    public SkyPrisonSkillAssetListPanel(SkyPrisonSkillPage page) { this.page = page; }

    public void OnRefresh()
    {
        if (page.SelectedSkill == null) return;
        string path   = AssetDatabase.GetAssetPath(page.SelectedSkill);
        string folder = Path.GetDirectoryName(path)?.Replace("\\", "/");
        if (!string.IsNullOrWhiteSpace(folder)) selectedFolderPath = folder;
    }

    public void Draw()
    {
        Rect full = GUILayoutUtility.GetRect(0, 100000, 0, 100000,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        float y = full.y;
        Rect titleRect   = new Rect(full.x, y, full.width, TitleH);   y += TitleH + RowGap;
        Rect toolbarRect = new Rect(full.x, y, full.width, ToolbarH); y += ToolbarH + RowGap;
        Rect searchRect  = new Rect(full.x, y, full.width, SearchH);  y += SearchH + RowGap;
        Rect listRect    = new Rect(full.x, y, full.width, Mathf.Max(40f, full.yMax - y));

        GUI.Label(titleRect, "技能定义列表", EditorStyles.boldLabel);
        DrawToolbar(toolbarRect);
        DrawSearch(searchRect);
        BuildNodes();
        DrawList(listRect);
    }

    public void HandlePostGUI() => HandleDragDrop();

    public void CopySkill(SkillDefinition s) { clipboard = s; clipMode = ClipMode.Copy; }
    public void CutSkill(SkillDefinition  s) { clipboard = s; clipMode = ClipMode.Cut;  }

    public bool TryPaste()
    {
        if (clipboard == null) return false;
        PasteToFolder(GetCreateFolder());
        return true;
    }

    // ── 工具栏 ────────────────────────────────────────────────────────────

    private void DrawToolbar(Rect rect)
    {
        const float sz = 20f, gap = 4f;
        float cy = rect.y + (rect.height - sz) * 0.5f;
        float right = rect.xMax;

        Rect refreshR = new Rect(right - sz, cy, sz, sz);
        Rect minusR   = new Rect(refreshR.x - gap - sz, cy, sz, sz);
        Rect plusR    = new Rect(minusR.x   - gap - sz, cy, sz, sz);

        if (DrawBtn(plusR, "+", "新建技能"))
            CreateSkill(GetCreateFolder());

        using (new EditorGUI.DisabledScope(page.SelectedSkill == null))
        {
            if (DrawBtn(minusR, "-", "删除技能"))
                DeleteSkill(page.SelectedSkill);
        }

        if (DrawBtn(refreshR, "↻", "刷新"))
            page.Refresh();
    }

    private void DrawSearch(Rect rect)
    {
        string next = EditorGUI.TextField(rect, search);
        if (next != search) { search = next; listScroll = Vector2.zero; }
    }

    // ── 列表 ─────────────────────────────────────────────────────────────

    private void DrawList(Rect rect)
    {
        EditorGUI.DrawRect(rect, page.LeftTopBg);
        page.DrawThinBorder(rect, new Color(1f, 1f, 1f, 0.06f));

        Rect view    = new Rect(rect.x + Pad, rect.y + Pad, rect.width - Pad * 2f, rect.height - Pad * 2f);
        float contentH = Mathf.Max(view.height, visibleNodes.Count * RowH);
        Rect content = new Rect(0, 0, Mathf.Max(10f, view.width - 14f), contentH);

        listScroll = GUI.BeginScrollView(view, listScroll, content, false, true);

        for (int i = 0; i < visibleNodes.Count; i++)
        {
            ListNode n = visibleNodes[i];
            Rect row = new Rect(0, i * RowH, content.width, RowH);
            n.lastRect = row;
            if (n.isFolder) DrawFolderRow(row, n);
            else            DrawSkillRow(row, n);
        }

        if (visibleNodes.Count == 0)
            GUI.Label(new Rect(4, 2, content.width - 8, 22), "没有匹配的技能定义", EditorStyles.miniLabel);

        GUI.EndScrollView();
    }

    private void DrawFolderRow(Rect row, ListNode n)
    {
        bool selected = selectedFolderKey == n.fullKey && page.SelectedSkill == null;
        bool hover    = row.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(row, new Color(0.55f, 0.52f, 0.12f, 0.20f));
            EditorGUI.DrawRect(new Rect(row.x, row.y, 3f, row.height), page.AccentMustard);
        }
        else if (hover)
            EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.04f));

        float indent = n.depth * 14f;
        Rect foldRect  = new Rect(row.x + indent + 4f, row.y, 18f, row.height);
        Rect labelRect = new Rect(row.x + indent + 20f, row.y, row.width - indent - 20f, row.height);

        bool exp    = GetExpanded(n.fullKey);
        bool newExp = EditorGUI.Foldout(foldRect, exp, GUIContent.none, false);
        if (newExp != exp) folderExpanded[n.fullKey] = newExp;

        var style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = selected ? Color.white : new Color(0.88f, 0.88f, 0.90f) }
        };
        GUI.Label(labelRect, n.displayName, style);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
        {
            if (e.button == 0) { SelectFolder(n); e.Use(); }
            else if (e.button == 1) { SelectFolder(n); ShowFolderMenu(n); e.Use(); }
        }
    }

    private void DrawSkillRow(Rect row, ListNode n)
    {
        bool selected = page.SelectedSkill == n.skill;
        bool hover    = row.Contains(Event.current.mousePosition);

        if (selected)
        {
            EditorGUI.DrawRect(row, page.SelectedRowMustard);
            EditorGUI.DrawRect(new Rect(row.x, row.y, 3f, row.height), page.AccentMustard);
        }
        else if (hover)
            EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.05f));

        float indent = n.depth * 14f + 18f;
        Rect labelRect = new Rect(row.x + indent, row.y, row.width - indent, row.height);

        var style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = selected ? Color.white : new Color(0.90f, 0.90f, 0.92f) }
        };

        string tag = n.skill != null ? $" [{CategoryTag(n.skill.category)}]" : "";
        GUI.Label(labelRect, n.displayName + tag, style);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
        {
            if (e.button == 0)
            { page.SelectSkill(n.skill); selectedFolderPath = n.assetFolderPath; selectedFolderKey = ""; e.Use(); }
            else if (e.button == 1)
            { page.SelectSkill(n.skill); selectedFolderPath = n.assetFolderPath; selectedFolderKey = ""; ShowSkillMenu(n); e.Use(); }
        }

        if (e.type == EventType.MouseDrag && row.Contains(e.mousePosition) && n.skill != null)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new Object[] { n.skill };
            DragAndDrop.SetGenericData("SkyPrisonDraggedSkill", n.skill);
            DragAndDrop.StartDrag(n.skill.name);
            e.Use();
        }
    }

    // ── 树构建 ────────────────────────────────────────────────────────────

    private void BuildNodes()
    {
        visibleNodes.Clear();
        var folderMap = new Dictionary<string, List<SkillDefinition>>();

        foreach (SkillDefinition s in page.SkillDefinitions)
        {
            if (s == null) continue;
            if (!string.IsNullOrWhiteSpace(search))
            {
                string kw = search.Trim().ToLowerInvariant();
                bool match = s.displayName.ToLowerInvariant().Contains(kw)
                          || s.skillKey.ToLowerInvariant().Contains(kw)
                          || s.name.ToLowerInvariant().Contains(kw);
                if (!match) continue;
            }

            string path   = AssetDatabase.GetAssetPath(s);
            string folder = GetRelativeFolder(path);
            if (!folderMap.ContainsKey(folder)) folderMap[folder] = new List<SkillDefinition>();
            folderMap[folder].Add(s);
        }

        var added = new HashSet<string>();
        foreach (string folder in folderMap.Keys.OrderBy(f => f))
        {
            string[] parts    = folder.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
            string current    = "";
            string assetBase  = SkyPrisonSkillPage.SkillRootFolder;

            for (int i = 0; i < parts.Length; i++)
            {
                current   = string.IsNullOrEmpty(current) ? parts[i] : current + "/" + parts[i];
                assetBase = assetBase.TrimEnd('/') + "/" + parts[i];

                if (!added.Contains(current))
                {
                    added.Add(current);
                    visibleNodes.Add(new ListNode
                    {
                        displayName     = parts[i],
                        fullKey         = current,
                        assetFolderPath = assetBase,
                        depth           = i,
                        isFolder        = true,
                    });
                }
            }

            if (!IsFolderVisible(folder)) continue;

            foreach (SkillDefinition s in folderMap[folder]
                         .OrderBy(x => string.IsNullOrWhiteSpace(x.displayName) ? x.name : x.displayName))
            {
                visibleNodes.Add(new ListNode
                {
                    displayName     = string.IsNullOrWhiteSpace(s.displayName) ? s.name : s.displayName,
                    fullKey         = folder + "/" + s.name,
                    assetFolderPath = SkyPrisonSkillPage.SkillRootFolder.TrimEnd('/') + "/" + folder,
                    depth           = parts.Length,
                    isFolder        = false,
                    skill           = s,
                });
            }
        }
    }

    // ── 右键菜单 ─────────────────────────────────────────────────────────

    private void ShowFolderMenu(ListNode n)
    {
        GenericMenu m = new GenericMenu();
        m.AddItem(new GUIContent("新建技能"), false, () => CreateSkill(n.assetFolderPath));
        if (clipboard != null)
            m.AddItem(new GUIContent("粘贴"), false, () => PasteToFolder(n.assetFolderPath));
        else
            m.AddDisabledItem(new GUIContent("粘贴"));
        m.AddSeparator("");
        m.AddItem(new GUIContent("在 Project 中定位"), false, () =>
        {
            var obj = AssetDatabase.LoadAssetAtPath<Object>(n.assetFolderPath);
            if (obj != null) { Selection.activeObject = obj; EditorGUIUtility.PingObject(obj); }
        });
        m.ShowAsContext();
    }

    private void ShowSkillMenu(ListNode n)
    {
        const string std = "Assets/_Project/Data/Definitions/Standard/Skills";
        const string cus = "Assets/_Project/Data/Definitions/Custom/Skills";

        string curPath = AssetDatabase.GetAssetPath(n.skill);
        GenericMenu m  = new GenericMenu();
        m.AddItem(new GUIContent("复制"), false, () => CopySkill(n.skill));
        m.AddItem(new GUIContent("剪切"), false, () => CutSkill(n.skill));
        m.AddSeparator("");
        if (curPath.Contains("/Custom/"))
            m.AddItem(new GUIContent("移动到 Standard/Skills"), false, () => MoveToFolder(n.skill, std));
        else
            m.AddDisabledItem(new GUIContent("移动到 Standard/Skills"));
        if (curPath.Contains("/Standard/"))
            m.AddItem(new GUIContent("移动到 Custom/Skills"), false, () => MoveToFolder(n.skill, cus));
        else
            m.AddDisabledItem(new GUIContent("移动到 Custom/Skills"));
        m.AddSeparator("");
        m.AddItem(new GUIContent("删除"), false, () => DeleteSkill(n.skill));
        m.AddSeparator("");
        m.AddItem(new GUIContent("在 Project 中定位"), false, () =>
        { Selection.activeObject = n.skill; EditorGUIUtility.PingObject(n.skill); });
        m.ShowAsContext();
    }

    // ── CRUD ─────────────────────────────────────────────────────────────

    private void CreateSkill(string folderPath)
    {
        page.EnsureFolderExists(folderPath);
        SkillDefinition asset = ScriptableObject.CreateInstance<SkillDefinition>();
        asset.skillKey    = page.GenerateUniqueSkillKey("new_skill");
        asset.displayName = "新技能";

        string path = AssetDatabase.GenerateUniqueAssetPath(folderPath.TrimEnd('/') + "/SK_NewSkill.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        page.Refresh();
        page.SelectSkill(asset);
        selectedFolderPath = folderPath;
        selectedFolderKey  = "";
    }

    public bool DeleteSkill(SkillDefinition skill)
    {
        if (skill == null) return false;
        string path = AssetDatabase.GetAssetPath(skill);
        if (string.IsNullOrEmpty(path)) return false;

        bool ok = EditorUtility.DisplayDialog("删除技能",
            $"确定删除技能定义：\n{skill.name}\n\n此操作会删除资源文件。", "删除", "取消");
        if (!ok) return false;

        if (page.SelectedSkill == skill) page.ClearSkillSelection();
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        page.Refresh();
        return true;
    }

    private void MoveToFolder(SkillDefinition skill, string targetFolder)
    {
        if (skill == null) return;
        string src = AssetDatabase.GetAssetPath(skill);
        if (string.IsNullOrEmpty(src)) return;

        string srcFolder = Path.GetDirectoryName(src)?.Replace("\\", "/") ?? "";
        if (srcFolder == targetFolder) return;

        page.EnsureFolderExists(targetFolder);
        string dst = AssetDatabase.GenerateUniqueAssetPath(targetFolder.TrimEnd('/') + "/" + Path.GetFileName(src));
        string err = AssetDatabase.MoveAsset(src, dst);
        if (!string.IsNullOrEmpty(err)) { EditorUtility.DisplayDialog("移动失败", err, "确定"); return; }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        page.Refresh();
        page.SelectSkill(AssetDatabase.LoadAssetAtPath<SkillDefinition>(dst));
        selectedFolderPath = targetFolder;
        selectedFolderKey  = "";
    }

    private void PasteToFolder(string folderPath)
    {
        if (clipboard == null) return;
        if (clipMode == ClipMode.Copy)  PasteCopy(folderPath);
        else if (clipMode == ClipMode.Cut) { MoveToFolder(clipboard, folderPath); clipboard = null; clipMode = ClipMode.None; }
    }

    private void PasteCopy(string folderPath)
    {
        if (clipboard == null) return;
        page.EnsureFolderExists(folderPath);
        SkillDefinition clone = Object.Instantiate(clipboard);
        clone.name     = clipboard.name + "_Copy";
        clone.skillKey = page.GenerateUniqueSkillKey(
            string.IsNullOrWhiteSpace(clipboard.skillKey) ? "skill_copy" : clipboard.skillKey + "_copy");

        string path = AssetDatabase.GenerateUniqueAssetPath(folderPath.TrimEnd('/') + "/" + clipboard.name + "_Copy.asset");
        AssetDatabase.CreateAsset(clone, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        page.Refresh();
        page.SelectSkill(clone);
        selectedFolderPath = folderPath;
        selectedFolderKey  = "";
    }

    private void HandleDragDrop()
    {
        Event e = Event.current;
        if (e == null) return;
        SkillDefinition dragged = DragAndDrop.GetGenericData("SkyPrisonDraggedSkill") as SkillDefinition;
        if (dragged == null) return;
        ListNode hover = visibleNodes.FirstOrDefault(n => n.isFolder && n.lastRect.Contains(e.mousePosition));
        if (hover == null) return;
        if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                MoveToFolder(dragged, hover.assetFolderPath);
                DragAndDrop.SetGenericData("SkyPrisonDraggedSkill", null);
            }
            e.Use();
        }
    }

    // ── 工具 ─────────────────────────────────────────────────────────────

    private void SelectFolder(ListNode n) { page.ClearSkillSelection(); selectedFolderKey = n.fullKey; selectedFolderPath = n.assetFolderPath; }

    private string GetCreateFolder()
    {
        if (!string.IsNullOrWhiteSpace(selectedFolderPath)) return selectedFolderPath;
        if (page.SelectedSkill != null)
        {
            string f = Path.GetDirectoryName(AssetDatabase.GetAssetPath(page.SelectedSkill))?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(f)) return f;
        }
        return SkyPrisonSkillPage.DefaultSkillCreateFolder;
    }

    private string GetRelativeFolder(string assetPath)
    {
        string folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/") ?? "";
        string root   = SkyPrisonSkillPage.SkillRootFolder;
        return folder.StartsWith(root) ? folder.Substring(root.Length).TrimStart('/') : "未分类";
    }

    private bool IsFolderVisible(string folder)
    {
        string[] parts = folder.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
        string cur = "";
        foreach (string p in parts)
        {
            cur = string.IsNullOrEmpty(cur) ? p : cur + "/" + p;
            if (!GetExpanded(cur)) return false;
        }
        return true;
    }

    private bool GetExpanded(string key)
    {
        if (!folderExpanded.TryGetValue(key, out bool v)) { v = true; folderExpanded[key] = true; }
        return v;
    }

    private bool DrawBtn(Rect r, string text, string tip)
    {
        Event e    = Event.current;
        bool  hov  = r.Contains(e.mousePosition);
        EditorGUI.DrawRect(r, hov ? new Color(1,1,1,0.10f) : new Color(1,1,1,0.04f));
        page.DrawThinBorder(r, new Color(1,1,1, hov ? 0.12f : 0.05f));
        GUI.Label(r, new GUIContent(text, tip), page.CenteredToolbarStyle());
        if (e.type == EventType.MouseDown && e.button == 0 && hov) { e.Use(); GUI.changed = true; return true; }
        return false;
    }

    private string CategoryTag(SkillCategory cat) => cat switch
    {
        SkillCategory.LightAttack => "轻",
        SkillCategory.HeavyAttack => "重",
        SkillCategory.Dash        => "冲",
        SkillCategory.Special     => "特",
        SkillCategory.Passive     => "被动",
        _                         => "?",
    };
}
