#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 天空囚笼编辑器 — 任务编辑器页面。编辑 QuestDefinition：标识/显示文字、归属NPC、
/// 解锁条件、目标列表、完成时执行动作。之前只能靠原始 Inspector 编辑，跟触发器/
/// 对话不一样体验很割裂——条件/动作复用同一套 AILogicSentenceTemplatePickerWindow，
/// 跟 SkyPrisonInteractionPackagePage 是同一套"直接改ScriptableObject字段+
/// EditorUtility.SetDirty"的简单模式，不是 SerializedObject/SerializedProperty
/// 那一套(触发器页面用的)——QuestDefinition 的字段都是扁平List，没有触发器规则
/// 那种motive/condition/action嵌套结构，用不着那么重的方案。
/// </summary>
public class SkyPrisonQuestPage : SkyPrisonEditorPageBase
{
    private const string DefaultFolder = "Assets/_Project/Data/Quests";

    private LocalizationProjectSettings _settings;
    private readonly List<QuestDefinition> _quests = new List<QuestDefinition>();
    private QuestDefinition _current;
    private string _search = "";

    // 系列任务用文件夹分组——相对 DefaultFolder 的路径，""=根目录。真实用
    // AssetDatabase 文件夹(不是编辑器里假装的分组)，这样在 Project 窗口里拖动
    // 整理也是同一份数据，不会出现两边看到的结构对不上的情况。
    private string _currentFolder = "";
    private bool _creatingFolder = false;
    private string _newFolderNameBuffer = "";
    private const string NewFolderControlName = "QuestPageNewFolderField";

    private Vector2 _leftScroll;
    private Vector2 _rightScroll;

    private QuestDefinition _renamingQuest;
    private string _renameBuffer = "";
    private const string RenameControlName = "QuestPageRenameField";

    private readonly Dictionary<QuestObjective, bool> _objectiveFoldouts = new Dictionary<QuestObjective, bool>();

    private static readonly Color HeaderBg = new Color(0.16f, 0.16f, 0.18f, 1f);
    private static readonly Color RowEvenBg = new Color(0.13f, 0.13f, 0.15f, 1f);
    private static readonly Color RowOddBg = new Color(0.15f, 0.15f, 0.17f, 1f);
    private static readonly Color AccentColor = new Color(0.85f, 0.65f, 0.25f, 1f);
    private static readonly Color SelectedFillColor = new Color(0.28f, 0.22f, 0.10f, 1f);
    private static readonly Color LeftPanelBg = new Color(0.18f, 0.18f, 0.19f, 1f);
    private static readonly Color ListContainerBg = new Color(0.11f, 0.11f, 0.14f, 1f);

    private string CurrentFolderFullPath => string.IsNullOrEmpty(_currentFolder) ? DefaultFolder : DefaultFolder + "/" + _currentFolder;

    public SkyPrisonQuestPage(SkyPrisonEditorContext context) : base(context) { }

    public override string TabName => "任务编辑器";

    public override void OnEnable() => ReloadAssets();
    public override void Refresh() => ReloadAssets();

    public override bool TrySelectObject(UnityEngine.Object obj)
    {
        if (obj is not QuestDefinition quest)
            return false;

        ReloadAssets();
        _current = _quests.FirstOrDefault(q => q == quest) ?? quest;
        return true;
    }

    // ════════════════════════════════════════════════════════════════════
    // 左栏 — 任务列表
    // ════════════════════════════════════════════════════════════════════
    // 手动Rect+GUI.BeginScrollView(不用EditorGUILayout.BeginScrollView)——那个
    // 不给固定高度会一直跟着内容长高，永远不会真正出现滚轮。跟状态列表(状态页
    // 面SkyPrisonStatusPage)、变量表同一套画法，用户明确要求列表统一成这个
    // 深色底、能顶到底部的样式。
    public override void OnGUILeft()
    {
        if (_settings == null) ReloadAssets();

        Rect fullRect = GUILayoutUtility.GetRect(
            0f, 100000f, 0f, 100000f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(fullRect, LeftPanelBg);

        Rect inner = new Rect(fullRect.x + 8f, fullRect.y + 8f, fullRect.width - 16f, fullRect.height - 16f);
        float y = inner.y;

        Rect titleRect = new Rect(inner.x, y, inner.width, 20f);
        y += 24f;

        Rect toolbarRect = new Rect(inner.x, y, inner.width, 22f);
        y += 28f;

        bool showBreadcrumb = !string.IsNullOrEmpty(_currentFolder);
        Rect breadcrumbRect = default;
        if (showBreadcrumb)
        {
            breadcrumbRect = new Rect(inner.x, y, inner.width, 20f);
            y += 24f;
        }

        Rect searchRect = new Rect(inner.x, y, inner.width, 20f);
        y += 28f;

        Rect listOuterRect = new Rect(inner.x, y, inner.width, Mathf.Max(80f, inner.yMax - y));
        Rect listViewRect = new Rect(listOuterRect.x + 6f, listOuterRect.y + 6f, listOuterRect.width - 12f, listOuterRect.height - 12f);

        GUI.Label(titleRect, "任务列表", EditorStyles.boldLabel);
        DrawLeftToolbar(toolbarRect);
        if (showBreadcrumb)
            DrawBreadcrumb(breadcrumbRect);
        _search = EditorGUI.TextField(searchRect, _search ?? "");

        EditorGUI.DrawRect(listOuterRect, ListContainerBg);
        DrawThinBorder(listOuterRect, new Color(1f, 1f, 1f, 0.08f));

        // 搜索时把所有文件夹一起摊平搜(不管当前在哪层)，不搜索时只显示当前文件夹
        // 下的子文件夹+任务——跟大多数文件浏览器的搜索行为一致。
        bool searching = !string.IsNullOrWhiteSpace(_search);
        List<string> subFolders = searching ? new List<string>() : GetSubFolderNames(CurrentFolderFullPath);
        List<QuestDefinition> quests = GetVisibleQuests(searching);

        const float rowHeight = 24f;
        int totalRows = (_creatingFolder ? 1 : 0) + subFolders.Count + quests.Count;
        float contentHeight = Mathf.Max(listViewRect.height, totalRows * rowHeight + 4f);
        Rect contentRect = new Rect(0f, 0f, Mathf.Max(10f, listViewRect.width - 14f), contentHeight);

        Vector2 localMouse = Event.current.mousePosition + _leftScroll - new Vector2(listViewRect.x, listViewRect.y);
        _leftScroll = GUI.BeginScrollView(listViewRect, _leftScroll, contentRect, false, true);

        float rowY = 0f;

        if (_creatingFolder)
        {
            DrawNewFolderRow(new Rect(0f, rowY, contentRect.width, rowHeight - 2f));
            rowY += rowHeight;
        }

        foreach (string folderName in subFolders)
        {
            DrawFolderRow(new Rect(0f, rowY, contentRect.width, rowHeight - 2f), folderName, localMouse);
            rowY += rowHeight;
        }

        foreach (QuestDefinition quest in quests)
        {
            DrawQuestRow(new Rect(0f, rowY, contentRect.width, rowHeight - 2f), quest, localMouse);
            rowY += rowHeight;
        }

        if (!_creatingFolder && subFolders.Count == 0 && quests.Count == 0)
            GUI.Label(new Rect(8f, 6f, contentRect.width - 16f, 20f), "这里还没有任务，点上面按钮新建一个。", EditorStyles.miniLabel);

        GUI.EndScrollView();
    }

    private void DrawLeftToolbar(Rect rect)
    {
        const float gap = 4f;
        float w = (rect.width - gap * 2f) / 3f;

        Rect newQuestRect = new Rect(rect.x, rect.y, w, rect.height);
        Rect newFolderRect = new Rect(newQuestRect.xMax + gap, rect.y, w, rect.height);
        Rect refreshRect = new Rect(newFolderRect.xMax + gap, rect.y, w, rect.height);

        if (GUI.Button(newQuestRect, "新建任务"))
            CreateQuestAsset();
        if (GUI.Button(newFolderRect, "新建文件夹"))
            BeginCreateFolder();
        if (GUI.Button(refreshRect, "刷新"))
            ReloadAssets();
    }

    private void DrawBreadcrumb(Rect rect)
    {
        Rect backRect = new Rect(rect.x, rect.y, 60f, rect.height);
        Rect pathRect = new Rect(backRect.xMax + 6f, rect.y, rect.width - backRect.width - 6f, rect.height);

        if (GUI.Button(backRect, "◀ 上级"))
            NavigateUp();

        GUI.Label(pathRect, "当前：" + _currentFolder, EditorStyles.miniLabel);
    }

    private void NavigateUp()
    {
        int idx = _currentFolder.LastIndexOf('/');
        _currentFolder = idx >= 0 ? _currentFolder.Substring(0, idx) : "";
        GUI.FocusControl(null);
    }

    private List<string> GetSubFolderNames(string parentFullPath)
    {
        var result = new List<string>();
        if (!AssetDatabase.IsValidFolder(parentFullPath))
            return result;

        foreach (string full in AssetDatabase.GetSubFolders(parentFullPath))
            result.Add(Path.GetFileName(full));

        result.Sort(System.StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private List<QuestDefinition> GetVisibleQuests(bool searching)
    {
        IEnumerable<QuestDefinition> source = _quests.Where(q => q != null);

        if (searching)
        {
            string key = _search.Trim().ToLower();
            source = source.Where(q => GetQuestListLabel(q).ToLower().Contains(key) || (q.questId ?? "").ToLower().Contains(key));
        }
        else
        {
            string folder = CurrentFolderFullPath;
            source = source.Where(q => GetContainingFolder(q) == folder);
        }

        return source.OrderBy(q => GetQuestListLabel(q), System.StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetContainingFolder(QuestDefinition quest)
    {
        string path = AssetDatabase.GetAssetPath(quest);
        if (string.IsNullOrEmpty(path))
            return DefaultFolder;

        string dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
        return string.IsNullOrEmpty(dir) ? DefaultFolder : dir;
    }

    // 列表优先显示任务标题(策划实际填的名字)，填了才好认——填之前退回资产文件名
    // (也就是questId，比如"quest_001")当兜底，不会显示空白。跟双击改名编辑的
    // 还是文件名/questId本身，这两者是两件事：标题给玩家看，ID是内部引用用的。
    private string GetQuestListLabel(QuestDefinition quest)
    {
        if (quest == null) return "-";
        string title = GetPreferredLocalizedText(quest.title);
        string label = string.IsNullOrWhiteSpace(title) ? quest.name : title;
        return label + (quest.category == QuestCategory.Mainline ? "（主线）" : "");
    }

    private string GetPreferredLocalizedText(List<LocalizedTextEntry> texts)
    {
        if (texts == null) return "";

        if (_settings != null)
        {
            foreach (LocalizationProjectSettings.LanguageEntry lang in _settings.languages)
            {
                if (lang == null || !lang.isDefault) continue;
                foreach (LocalizedTextEntry t in texts)
                    if (t.languageCode == lang.languageCode && !string.IsNullOrWhiteSpace(t.text))
                        return t.text;
            }
        }

        foreach (LocalizedTextEntry t in texts)
            if (!string.IsNullOrWhiteSpace(t.text))
                return t.text;

        return "";
    }

    private void DrawFolderRow(Rect rect, string folderName, Vector2 localMouse)
    {
        bool hover = rect.Contains(localMouse);
        if (hover)
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.05f));

        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            _currentFolder = string.IsNullOrEmpty(_currentFolder) ? folderName : _currentFolder + "/" + folderName;
            GUI.FocusControl(null);
        }

        Texture folderIcon = EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_Folder Icon" : "Folder Icon").image;
        const float iconSize = 16f;
        Rect iconRect = new Rect(rect.x + 8f, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize);
        if (folderIcon != null)
            GUI.DrawTexture(iconRect, folderIcon, ScaleMode.ScaleToFit, true);

        Rect labelRect = new Rect(iconRect.xMax + 6f, rect.y, rect.width - (iconRect.xMax + 10f), rect.height);
        GUIStyle style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
        GUI.Label(labelRect, folderName, style);

        if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && rect.Contains(Event.current.mousePosition))
        {
            ShowFolderContextMenu(folderName);
            Event.current.Use();
        }
    }

    private void ShowFolderContextMenu(string folderName)
    {
        string fullPath = CurrentFolderFullPath + "/" + folderName;
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("删除文件夹（含里面所有任务）"), false, () =>
        {
            bool ok = EditorUtility.DisplayDialog("删除文件夹",
                $"确定删除文件夹 \"{folderName}\" 以及里面的所有任务吗？此操作不可撤销。", "删除", "取消");
            if (!ok) return;

            AssetDatabase.DeleteAsset(fullPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ReloadAssets();
        });
        menu.ShowAsContext();
    }

    private void DrawNewFolderRow(Rect rect)
    {
        GUI.SetNextControlName(NewFolderControlName);
        _newFolderNameBuffer = EditorGUI.TextField(rect, _newFolderNameBuffer);

        Event e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
        {
            CommitCreateFolder();
            e.Use();
        }
        else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            _creatingFolder = false;
            e.Use();
        }
        else if (e.type == EventType.MouseDown && !rect.Contains(e.mousePosition))
        {
            CommitCreateFolder();
        }
    }

    private void BeginCreateFolder()
    {
        _creatingFolder = true;
        _newFolderNameBuffer = "新文件夹";
        EditorGUIUtility.editingTextField = true;
        EditorApplication.delayCall += () => EditorGUI.FocusTextInControl(NewFolderControlName);
    }

    private void CommitCreateFolder()
    {
        string folderName = (_newFolderNameBuffer ?? "").Trim();
        _creatingFolder = false;
        if (string.IsNullOrEmpty(folderName))
            return;

        EnsureFolder(CurrentFolderFullPath);
        if (!AssetDatabase.IsValidFolder(CurrentFolderFullPath + "/" + folderName))
            AssetDatabase.CreateFolder(CurrentFolderFullPath, folderName);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void DrawThinBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private void DrawQuestRow(Rect rowRect, QuestDefinition quest, Vector2 localMouse)
    {
        if (quest == null) return;

        bool selected = quest == _current;
        bool hover = rowRect.Contains(localMouse);

        if (selected)
        {
            EditorGUI.DrawRect(rowRect, SelectedFillColor);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 3f, rowRect.height), AccentColor);
        }
        else if (hover)
        {
            EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.05f));
        }

        Rect labelRect = new Rect(rowRect.x + 8f, rowRect.y, rowRect.width - 12f, rowRect.height);

        if (_renamingQuest == quest)
        {
            GUI.SetNextControlName(RenameControlName);
            _renameBuffer = EditorGUI.TextField(labelRect, _renameBuffer);

            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                CommitQuestRename();
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _renamingQuest = null;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && !labelRect.Contains(e.mousePosition))
            {
                CommitQuestRename();
            }
            return;
        }

        GUIStyle style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = selected ? Color.white : new Color(0.90f, 0.90f, 0.92f, 1f) }
        };
        GUI.Label(labelRect, GetQuestListLabel(quest), style);

        if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
        {
            if (Event.current.button == 1)
            {
                ShowQuestContextMenu(quest);
                Event.current.Use();
            }
            else if (Event.current.clickCount == 2)
            {
                StartQuestRename(quest);
                Event.current.Use();
            }
            else
            {
                _current = quest;
                GUI.FocusControl(null);
                Event.current.Use();
            }
        }
    }

    private void ShowQuestContextMenu(QuestDefinition quest)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("重命名"), false, () => StartQuestRename(quest));
        menu.AddItem(new GUIContent("复制"), false, () => DuplicateQuest(quest));
        menu.AddItem(new GUIContent("在 Inspector 中打开"), false, () => Selection.activeObject = quest);
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("删除"), false, () => DeleteQuest(quest));
        menu.ShowAsContext();
    }

    private void StartQuestRename(QuestDefinition quest)
    {
        _renamingQuest = quest;
        _renameBuffer = quest.name;
        EditorGUIUtility.editingTextField = true;
        EditorApplication.delayCall += () => EditorGUI.FocusTextInControl(RenameControlName);
    }

    private void CommitQuestRename()
    {
        if (_renamingQuest == null) return;

        string newName = _renameBuffer.Trim();
        if (!string.IsNullOrEmpty(newName) && newName != _renamingQuest.name)
        {
            string path = AssetDatabase.GetAssetPath(_renamingQuest);
            AssetDatabase.RenameAsset(path, newName);
            AssetDatabase.SaveAssets();
        }

        _renamingQuest = null;
    }

    private void DuplicateQuest(QuestDefinition quest)
    {
        string path = AssetDatabase.GetAssetPath(quest);
        string newPath = AssetDatabase.GenerateUniqueAssetPath(path);
        if (!AssetDatabase.CopyAsset(path, newPath)) return;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var copy = AssetDatabase.LoadAssetAtPath<QuestDefinition>(newPath);
        ReloadAssets();
        if (copy != null) _current = copy;
    }

    private void DeleteQuest(QuestDefinition quest)
    {
        bool ok = EditorUtility.DisplayDialog("删除任务", $"确定删除任务 {quest.name} 吗？", "删除", "取消");
        if (!ok) return;

        string path = AssetDatabase.GetAssetPath(quest);
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (_current == quest) _current = null;
        ReloadAssets();
    }

    private void CreateQuestAsset()
    {
        EnsureFolder(CurrentFolderFullPath);

        string questId = GenerateNextQuestId();
        string path = AssetDatabase.GenerateUniqueAssetPath(CurrentFolderFullPath + "/" + questId + ".asset");
        var quest = ScriptableObject.CreateInstance<QuestDefinition>();
        quest.questId = questId;
        AssetDatabase.CreateAsset(quest, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        _quests.Add(quest);
        _current = quest;
    }

    // 新建任务时自动按 quest_001/quest_002... 顺序编号——questId 同时也是完成后
    // 设置的任务标记名，让策划一个个手打编号容易撞车/漏号，扫描现有任务里已经
    // 用了这个前缀的最大编号，自动接着往下排。跟老规矩一样，建完之后这个字段
    // 仍然是普通文本框，随时可以手动改成更有意义的名字（比如 main_02_xxx）。
    private string GenerateNextQuestId()
    {
        const string prefix = "quest_";
        int maxNum = 0;
        foreach (QuestDefinition q in _quests)
        {
            if (q == null || string.IsNullOrEmpty(q.questId) || !q.questId.StartsWith(prefix))
                continue;
            if (int.TryParse(q.questId.Substring(prefix.Length), out int n))
                maxNum = Mathf.Max(maxNum, n);
        }
        return prefix + (maxNum + 1).ToString("000");
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    private void ReloadAssets()
    {
        _settings = LocalizationSettingsUtility.GetOrCreateSettings();

        // 当前所在的文件夹如果被删掉了(比如刚在别处删了这个文件夹)，退回根目录，
        // 不然列表会一直卡在一个不存在的路径上，显示"这里还没有任务"看着像bug。
        if (!string.IsNullOrEmpty(_currentFolder) && !AssetDatabase.IsValidFolder(CurrentFolderFullPath))
            _currentFolder = "";

        string selectedPath = _current != null ? AssetDatabase.GetAssetPath(_current) : "";
        _quests.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:QuestDefinition"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);
            if (quest != null) _quests.Add(quest);
        }

        if (!string.IsNullOrEmpty(selectedPath))
            _current = _quests.FirstOrDefault(q => AssetDatabase.GetAssetPath(q) == selectedPath);

        if (_current == null && _quests.Count > 0)
            _current = _quests[0];
    }

    // ════════════════════════════════════════════════════════════════════
    // 右栏 — 任务详情编辑
    // ════════════════════════════════════════════════════════════════════
    public override void OnGUIRight()
    {
        if (_current == null)
        {
            EditorGUILayout.HelpBox("请在左侧选择或新建一个任务。", MessageType.Info);
            return;
        }

        if (_settings == null)
        {
            EditorGUILayout.HelpBox("请先在字典表页面配置语言。", MessageType.Warning);
            return;
        }

        List<LocalizationProjectSettings.LanguageEntry> langs = GetEnabledLanguages();
        if (langs.Count == 0)
        {
            EditorGUILayout.HelpBox("没有启用的语言，请先在字典表页面添加语言。", MessageType.Warning);
            return;
        }

        bool dirty = false;

        _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

        EditorGUILayout.BeginVertical("box");
        DrawSectionHeader("基本信息");
        dirty |= DrawBasicInfo(langs);
        EditorGUILayout.EndVertical();

        GUILayout.Space(6f);

        EditorGUILayout.BeginVertical("box");
        DrawSectionHeader("解锁条件（全部成立时任务自动开始，不需要玩家手动接取；留空=一开始就解锁）");
        dirty |= DrawSentenceList(_current.unlockConditions, LogicSentenceCategory.Condition, "添加解锁条件");
        EditorGUILayout.EndVertical();

        GUILayout.Space(6f);

        EditorGUILayout.BeginVertical("box");
        DrawSectionHeader("任务目标（全部必做目标达成时任务完成；可选目标不参与完成判定）");
        dirty |= DrawObjectives(langs);
        EditorGUILayout.EndVertical();

        GUILayout.Space(6f);

        EditorGUILayout.BeginVertical("box");
        DrawSectionHeader("任务报酬（完成任务时自动发放——底层就是\"完成时执行\"里的\"增加货币\"/\"增加物品到背包\"动作，这里只是更直观的展示，不是另一份数据）");
        dirty |= DrawQuestRewards();
        EditorGUILayout.EndVertical();

        GUILayout.Space(6f);

        EditorGUILayout.BeginVertical("box");
        DrawSectionHeader("完成时执行（任务完成那一刻执行一次，比如 设置任务标记 推进剧情/解锁其他内容）");
        dirty |= DrawSentenceList(_current.onCompleteActions, LogicSentenceCategory.Action, "添加完成动作",
            s => s != null && (s.templateId == "act_add_currency" || s.templateId == "act_add_item_to_bag"));
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();

        if (dirty)
        {
            EditorUtility.SetDirty(_current);
            AssetDatabase.SaveAssets();
        }
    }

    private bool DrawBasicInfo(List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        bool dirty = false;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("任务ID", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
        string newId = EditorGUILayout.TextField(_current.questId);
        if (newId != _current.questId) { _current.questId = newId; dirty = true; }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("任务ID也是完成后设置的任务标记(quest flag)名——对话/触发器条件可以直接判定这个ID。", MessageType.None);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("任务分类", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
        var newCategory = (QuestCategory)EditorGUILayout.EnumPopup(_current.category);
        if (newCategory != _current.category) { _current.category = newCategory; dirty = true; }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("归属NPC", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
        string npcLabel = _current.giverNpc != null ? UnitPickerPopupContent.GetLabel(_current.giverNpc) : "（不归属任何NPC）";
        Rect npcBtnRect = GUILayoutUtility.GetRect(new GUIContent(npcLabel), EditorStyles.objectField, GUILayout.ExpandWidth(true));
        if (GUI.Button(npcBtnRect, npcLabel, EditorStyles.objectField))
        {
            // 选的是地图上具体放置的哪一个实例，不是笼统的UnitDefinition资产——用户
            // 明确要求改成直接在场景里点，而不是从一份名字列表里选(列表依赖每个NPC
            // 提前手动挂 SkyPrisonSceneUnitMarker，选的时候也没法确认"点到的是不是
            // 我想要的那一个"，点选直观得多)。复用AI行为树编辑器已有的场景拾取协调器
            // (AIScenePickCoordinator)，不用另起一套SceneView GUI。giverNpc本身
            // (UnitDefinition)还是照样赋值，任务列表匹配逻辑不用跟着改；场景名/路径/
            // GUID是额外多记的"在哪"信息，留给以后引导玩家去找NPC的功能用。
            bool started = QuestGiverNpcScenePickLauncher.Begin(result =>
            {
                if (result == null) return;
                _current.giverNpc = result.unitDefinition;
                _current.giverNpcSceneName = result.sceneName;
                _current.giverNpcScenePath = result.scenePath;
                _current.giverNpcSceneUnitGuid = result.sceneUnitGuid;
                EditorUtility.SetDirty(_current);
                AssetDatabase.SaveAssets();

                EditorApplication.delayCall += () =>
                {
                    var w = EditorWindow.GetWindow<SkyPrisonEditorWindow>();
                    if (w != null) { w.Focus(); w.Repaint(); }
                };
            });

            if (!started)
                Debug.LogWarning("[SkyPrisonQuestPage] 场景拾取未能启动——可能已经有其它拾取在进行中，请先完成或按ESC取消那一个。");
        }
        if (_current.giverNpc != null && GUILayout.Button("清空", GUILayout.Width(50f)))
        {
            _current.giverNpc = null;
            _current.giverNpcSceneName = "";
            _current.giverNpcScenePath = "";
            _current.giverNpcSceneUnitGuid = "";
            EditorUtility.SetDirty(_current);
            AssetDatabase.SaveAssets();
        }
        EditorGUILayout.EndHorizontal();

        if (_current.giverNpc != null)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth);
            string locationLabel = string.IsNullOrWhiteSpace(_current.giverNpcSceneUnitGuid)
                ? "未记录具体放置位置——这是老数据或直接选的资产，建议重新点上面按钮，在对应地图场景里选一次具体实例"
                : $"位于地图：{_current.giverNpcSceneName}";
            EditorGUILayout.LabelField(locationLabel, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.HelpBox(
            "有归属NPC=这个任务不会自动开始，必须在该NPC的对话选项里配一条\"接受任务\"" +
            "动作(actionType=AcceptQuest)，玩家选中才会真正进入进行中(解锁条件退化成" +
            "\"允许接取的前提\")。不归属任何NPC=保持原来的行为(解锁条件一旦成立就自动开始)。",
            MessageType.None);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("背景插画", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
        Sprite newHero = (Sprite)EditorGUILayout.ObjectField(_current.heroImage, typeof(Sprite), false);
        if (newHero != _current.heroImage) { _current.heroImage = newHero; dirty = true; }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("任务日志大卡片(目前只有主线任务用大卡片)背景——纯展示用，留空显示占位色块，不影响任务功能，随时可以后补。", MessageType.None);

        dirty |= DrawLocalizedTextFields("区域", _current.regionDisplayName, langs);
        EditorGUILayout.HelpBox("任务日志显示的地点名字，纯展示用，不参与任何判定。留空则不显示这一行。之前是单语言文本字段，" +
            "切日文/英文还是显示中文——现在跟标题/描述一样是多语言字段。", MessageType.None);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("危险等级", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
        int newHazard = EditorGUILayout.IntSlider(_current.hazardRank, 1, 10);
        if (newHazard != _current.hazardRank) { _current.hazardRank = newHazard; dirty = true; }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox("HAZARD RANK，1~10，纯展示用，不影响判定/掉落/难度。本篇大致1~5，DLC/高难内容6~8。", MessageType.None);

        GUILayout.Space(4f);
        dirty |= DrawLocalizedTextFields("标题", _current.title, langs);
        GUILayout.Space(2f);
        dirty |= DrawLocalizedTextFields("描述", _current.description, langs, true);

        return dirty;
    }

    // 清单勾选风格——用户明确要求编辑时的观感要接近玩家将来看任务面板的样子：
    // 勾选框+大字标题(取目标描述的默认语言文字，不是内部Key)是常驻可见的"概览行"，
    // 具体的Key/可选开关/多语言文本/完成条件默认收起来，点"编辑详情"才展开，
    // 不会让一堆开发向字段糊在预览行上抢视线。勾选框本身不表示真实完成状态——
    // 编辑器这里看的是任务定义，不是某个存档的进度，纯粹是"预览将来打勾会长
    // 什么样"，可选目标用更淡的边框颜色跟必做目标区分。
    private bool DrawObjectives(List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        bool dirty = false;

        Rect containerRect = EditorGUILayout.BeginVertical();
        EditorGUI.DrawRect(containerRect, ListContainerBg);
        DrawThinBorder(containerRect, new Color(1f, 1f, 1f, 0.08f));
        GUILayout.Space(4f);

        if (_current.objectives.Count == 0)
        {
            EditorGUILayout.LabelField("（暂无目标）", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(2f);
        }

        QuestObjective toRemove = null;
        for (int i = 0; i < _current.objectives.Count; i++)
        {
            QuestObjective objective = _current.objectives[i];
            if (objective == null) continue;

            if (!_objectiveFoldouts.ContainsKey(objective)) _objectiveFoldouts[objective] = false;
            bool expanded = _objectiveFoldouts[objective];

            Rect rowRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(rowRect, i % 2 == 0 ? RowEvenBg : RowOddBg);
            GUILayout.Space(3f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(6f);

            Rect checkboxRect = GUILayoutUtility.GetRect(16f, 16f, GUILayout.Width(16f), GUILayout.Height(16f));
            DrawObjectiveCheckbox(checkboxRect, objective.isOptional);
            GUILayout.Space(6f);

            string previewTitle = ResolveObjectiveTitle(objective, _current.giverNpc);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            GUILayout.Label(previewTitle, titleStyle, GUILayout.ExpandWidth(false));

            if (objective.isOptional)
                GUILayout.Label("(可选)", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));

            GUILayout.FlexibleSpace();

            _objectiveFoldouts[objective] = GUILayout.Toggle(expanded, "编辑详情", EditorStyles.miniButton, GUILayout.Width(70f));
            if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(44f)))
                toRemove = objective;
            GUILayout.Space(6f);
            EditorGUILayout.EndHorizontal();

            if (_objectiveFoldouts[objective])
            {
                GUILayout.Space(4f);
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("可选目标", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
                bool newOptional = EditorGUILayout.Toggle(objective.isOptional);
                if (newOptional != objective.isOptional) { objective.isOptional = newOptional; dirty = true; }
                EditorGUILayout.EndHorizontal();

                // 一个目标节点可以同时要求好几件事("杀死A和B才算完成"/"缴纳A和B")——
                // 每条结构化条件都给自己的友好编辑行，不再是"只有唯一一条时才给友好
                // 界面、多了就整块消失退化成手搓句型槽位"。
                LogicSentenceInstance singleAuto = objective.completionConditions.Count == 1
                    ? objective.completionConditions[0] : null;
                AutoObjectiveKind singleKind = ClassifyAutoObjective(singleAuto);

                bool multi = objective.completionConditions.Count > 1;

                if (!multi)
                {
                    // 只有一条条件(或还没有条件)——维持原来的样子。文案字段仍然叫
                    // "模板"、仍然可以写{0}/{1}占位符：单条件时运行时能解析出唯一的
                    // 主资产来填。这里绝不能因为ClassifyAutoObjective没认出某种句型
                    // (比如"签到型对话"用的是cond_counter_at_least)就改口叫"概括标题"
                    // 并警告不许写占位符——那种目标恰恰需要{0}，由发布者NPC来填。
                    if (singleKind != AutoObjectiveKind.None)
                    {
                        dirty |= DrawAutoObjectiveDetail(objective, singleAuto, singleKind);

                        GUILayout.Space(4f);
                        EditorGUILayout.LabelField(
                            singleKind switch
                            {
                                AutoObjectiveKind.Talk => "文案模板——{0}=名字是自动填的，只需要编辑周围的字(比如翻译成日语/英语)",
                                AutoObjectiveKind.Deliver => "文案模板——{0}=物品名、{1}=数量、{2}=交付对象名字都是自动填的，只需要编辑周围的字(比如翻译成日语/英语)",
                                _ => "文案模板——{0}=名字、{1}=数量都是自动填的，只需要编辑周围的字(比如翻译成日语/英语)"
                            },
                            EditorStyles.wordWrappedMiniLabel);
                        dirty |= DrawLocalizedTextFields("模板", objective.description, langs);
                    }
                    else
                    {
                        dirty |= DrawLocalizedTextFields("目标描述(玩家会看到的文字)", objective.description, langs, true);

                        GUILayout.Space(4f);
                        EditorGUILayout.LabelField("完成条件（全部成立才算这个目标完成——同一目标下多条条件是\"且\"的关系，没有先后）", EditorStyles.miniBoldLabel);
                        dirty |= DrawSentenceList(objective.completionConditions, LogicSentenceCategory.Condition, "添加完成条件");
                    }
                }
                else
                {
                    // 多条件节点没有唯一主资产，{0}/{1}占位符没东西可填，所以这里要的是
                    // 一句"概括标题"而不是模板。留空也没关系——运行时
                    // QuestObjectiveTextResolver 会把每条条件拼成"讨伐A ×2、收集B ×3"。
                    EditorGUILayout.LabelField(
                        "概括标题（可留空——留空时游戏内自动按每条条件拼成\"讨伐A ×2、收集B ×3\"显示。多条件下没有主资产，这里写{0}/{1}占位符是填不上的）",
                        EditorStyles.wordWrappedMiniLabel);
                    dirty |= DrawLocalizedTextFields("概括标题", objective.description, langs, true);

                    GUILayout.Space(4f);
                    EditorGUILayout.LabelField("完成条件（全部成立才算这个目标完成——同一目标下多条条件是\"且\"的关系，没有先后）", EditorStyles.miniBoldLabel);

                    int condToRemove = -1;
                    for (int ci = 0; ci < objective.completionConditions.Count; ci++)
                    {
                        LogicSentenceInstance cond = objective.completionConditions[ci];
                        AutoObjectiveKind k = ClassifyAutoObjective(cond);
                        if (k == AutoObjectiveKind.None) continue; // 非结构化的交给下面的通用句型列表

                        // 每条包一层容器(底色+细边框)，视觉上一眼能看出"这是绑在一起的
                        // 一条"，跟DrawSentenceList的容器是同一套做法。默认折叠，标题栏
                        // 直接把这条要求写全("交付 压缩饼干 ×3 给 琪亚拉")，不展开也能读。
                        Rect condRect = EditorGUILayout.BeginVertical();
                        EditorGUI.DrawRect(condRect, ci % 2 == 0 ? RowEvenBg : RowOddBg);
                        DrawThinBorder(condRect, new Color(1f, 1f, 1f, 0.08f));
                        GUILayout.Space(3f);

                        if (!_conditionFoldouts.TryGetValue(cond, out bool condExpanded))
                            condExpanded = false;

                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(4f);
                        _conditionFoldouts[cond] = EditorGUILayout.Foldout(condExpanded, SummarizeCondition(cond), true);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(44f)))
                            condToRemove = ci;
                        GUILayout.Space(4f);
                        EditorGUILayout.EndHorizontal();

                        if (_conditionFoldouts[cond])
                        {
                            EditorGUI.indentLevel++;
                            dirty |= DrawAutoObjectiveDetail(objective, cond, k);
                            EditorGUI.indentLevel--;
                        }

                        GUILayout.Space(3f);
                        EditorGUILayout.EndVertical();
                        GUILayout.Space(2f);
                    }
                    if (condToRemove >= 0)
                    {
                        _conditionFoldouts.Remove(objective.completionConditions[condToRemove]);
                        objective.completionConditions.RemoveAt(condToRemove);
                        dirty = true;
                    }

                    // 结构化条件上面已经逐条画过了，这里用hideFilter把它们藏掉，
                    // 只留非结构化的那些+"添加"按钮，避免同一条条件显示两遍。
                    dirty |= DrawSentenceList(objective.completionConditions, LogicSentenceCategory.Condition,
                        "添加完成条件（通用句型）",
                        s => ClassifyAutoObjective(s) != AutoObjectiveKind.None);
                }

                // 追加入口——只提供跟这个节点同类的那一种。对话节点就只能再加一个
                // 对话对象、交付节点就只能再加一件交付物品，节点保持同质，不会出现
                // "一个节点里又要杀怪又要交东西"这种读起来费劲的配置。
                GUILayout.Space(4f);
                dirty |= DrawAppendConditionButton(objective);

                EditorGUI.indentLevel--;
                GUILayout.Space(4f);
            }

            GUILayout.Space(3f);
            EditorGUILayout.EndVertical();
        }

        if (toRemove != null)
        {
            _current.objectives.Remove(toRemove);
            _objectiveFoldouts.Remove(toRemove);
            dirty = true;
        }

        GUILayout.Space(2f);

        // 快捷模板——"讨伐"/"收集"是任务目标里最常见的两类，直接选单位/物品+定型文，
        // 不用先手打描述文字、再另外去句型选择器里翻"计数器达到至少"/"拥有物品数量
        // 达到至少"这两条、还要照QuestRuntime里kill_unit_xxx的拼写约定手打counterId
        // (拼错一个字母整条目标就永远判定不了)。选完自动把描述文字和完成条件都填好，
        // 数量默认5，想改就跟改其它条件一样点"编辑详情"里的"编辑…"调数值。
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("＋ 讨伐目标", GUILayout.Height(20f)))
        {
            Rect btnRect = GUILayoutUtility.GetLastRect();
            PopupWindow.Show(btnRect, new UnitPickerPopupContent(picked => QuickAddKillObjective(picked, langs)));
        }
        if (GUILayout.Button("＋ 收集目标", GUILayout.Height(20f)))
        {
            SkyPrisonItemPickerPopup.Open(null, picked => QuickAddCollectObjective(picked as ItemDefinition, langs), nameof(ItemDefinition));
        }
        if (GUILayout.Button("＋ 对话目标", GUILayout.Height(20f)))
        {
            Rect btnRect = GUILayoutUtility.GetLastRect();
            PopupWindow.Show(btnRect, new UnitPickerPopupContent(picked => QuickAddTalkObjective(picked, langs)));
        }
        if (GUILayout.Button("＋ 交付目标", GUILayout.Height(20f)))
        {
            // 交付要选两个引用(物品+收件NPC)，先选物品再链式弹出NPC选择器，两个都
            // 选完才真正创建目标——不能只选了一半就先建一条残缺的目标出来。
            // NPC选择器是PopupWindow.Show(锚点矩形, ...)这种要求当帧GUI矩形的弹窗，
            // 但它是在物品选择器(独立EditorWindow)关闭之后的回调里才弹出，那时候
            // 早就不在这次OnGUI的布局阶段里了，GUILayoutUtility.GetLastRect()会拿到
            // 错误的矩形——必须趁按钮刚点下、还在当前这次布局阶段时就把矩形捕获
            // 下来，留给回调用，不能在回调里现取。
            Rect deliverBtnRect = GUILayoutUtility.GetLastRect();
            SkyPrisonItemPickerPopup.Open(null, pickedItem =>
            {
                var item = pickedItem as ItemDefinition;
                if (item == null) return;
                PopupWindow.Show(deliverBtnRect, new UnitPickerPopupContent(pickedNpc => QuickAddDeliverObjective(item, pickedNpc, langs)));
            }, nameof(ItemDefinition));
        }
        if (GUILayout.Button("＋ 空白目标", GUILayout.Height(20f)))
        {
            _current.objectives.Add(new QuestObjective());
            dirty = true;
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(4f);

        EditorGUILayout.EndVertical();

        return dirty;
    }

    private static AutoObjectiveKind ClassifyAutoObjective(LogicSentenceInstance c)
    {
        if (c == null) return AutoObjectiveKind.None;
        return c.templateId switch
        {
            "cond_unit_kill_count_at_least" => AutoObjectiveKind.Kill,
            "cond_has_item_count_at_least" => AutoObjectiveKind.Collect,
            "cond_has_talked_to_npc" => AutoObjectiveKind.Talk,
            "cond_has_delivered_item_to_npc" => AutoObjectiveKind.Deliver,
            _ => AutoObjectiveKind.None
        };
    }

    private static string AutoObjectiveKindLabel(AutoObjectiveKind kind) => kind switch
    {
        AutoObjectiveKind.Kill => "讨伐",
        AutoObjectiveKind.Collect => "收集",
        AutoObjectiveKind.Talk => "对话",
        AutoObjectiveKind.Deliver => "交付",
        _ => "自定义"
    };

    /// <summary>每条结构化条件的折叠状态。默认折叠——一个节点挂三四条交付物品时，
    /// 全展开会把整页撑得读不下去；折叠标题栏已经把这条要求写全了。</summary>
    private readonly Dictionary<LogicSentenceInstance, bool> _conditionFoldouts = new Dictionary<LogicSentenceInstance, bool>();

    /// <summary>折叠状态下那一行标题——直接把这条条件读成人话。不能用运行时的
    /// QuestObjectiveTextResolver.ResolveConditionLine：那个要读
    /// SaveManager.Player/InventoryRuntime 取当前进度，编辑器里这两个都是null，
    /// 会一路掉到"&lt;未命名条件&gt;"。这里只读资产引用和数值，编辑器下永远可用。</summary>
    private string SummarizeCondition(LogicSentenceInstance c)
    {
        if (c == null) return "（空条件）";

        int count = c.GetAssignment("count")?.value?.EvaluateInt() ?? 0;
        var unit = c.GetAssignment("unit")?.value?.assetReference as UnitDefinition;
        var npc = c.GetAssignment("npc")?.value?.assetReference as UnitDefinition;
        var item = c.GetAssignment("item")?.value?.assetReference as ItemDefinition;

        switch (c.templateId)
        {
            case "cond_unit_kill_count_at_least":
                return $"讨伐  {UnitPickerPopupContent.GetLabel(unit)}  ×{count}";
            case "cond_has_item_count_at_least":
                return $"收集  {GetItemDisplayLabel(item)}  ×{count}";
            case "cond_has_talked_to_npc":
                return $"与  {UnitPickerPopupContent.GetLabel(unit)}  对话";
            case "cond_has_delivered_item_to_npc":
                return $"交付  {GetItemDisplayLabel(item)}  ×{count}  给  {UnitPickerPopupContent.GetLabel(npc)}";
            default:
                return c.templateId;
        }
    }

    /// <summary>这个节点属于哪一类——按第一条能识别出来的结构化条件定。追加按钮只
    /// 提供同类的那一种，节点保持同质。</summary>
    private static AutoObjectiveKind GetObjectiveKind(QuestObjective objective)
    {
        if (objective?.completionConditions == null) return AutoObjectiveKind.None;
        foreach (LogicSentenceInstance c in objective.completionConditions)
        {
            AutoObjectiveKind k = ClassifyAutoObjective(c);
            if (k != AutoObjectiveKind.None) return k;
        }
        return AutoObjectiveKind.None;
    }

    /// <summary>往"已经存在的目标节点"里再追加一条同类的结构化条件——跟底部那排
    /// "＋讨伐目标/＋收集目标"共用同一批Build*Condition构造器，区别只是那排是
    /// 新建一整个目标节点，这个是往当前节点里加一条"且"关系的条件。
    /// 追加时不碰objective.description：概括标题是人工写的(或留空自动拼接)，
    /// 不该被后加的条件覆盖掉。</summary>
    private bool DrawAppendConditionButton(QuestObjective objective)
    {
        AutoObjectiveKind kind = GetObjectiveKind(objective);

        // 签到型对话(cond_counter_at_least)识别不出具体种类，但它语义上就是"对话"，
        // 再加一个对话对象是合理的；完全空白的节点则不给追加入口，先用上面的通用
        // 句型列表或外层的"＋讨伐目标"等建出第一条来。
        if (kind == AutoObjectiveKind.None)
        {
            bool looksLikeCheckin = objective?.completionConditions != null
                && objective.completionConditions.Exists(s => s != null && s.templateId == "cond_counter_at_least");
            if (!looksLikeCheckin) return false;
            kind = AutoObjectiveKind.Talk;
        }

        string label = kind switch
        {
            AutoObjectiveKind.Kill => "＋ 再加一个讨伐对象",
            AutoObjectiveKind.Collect => "＋ 再加一件收集物品",
            AutoObjectiveKind.Talk => "＋ 再加一个对话对象",
            AutoObjectiveKind.Deliver => "＋ 再加一件缴纳物品",
            _ => "＋ 再加一条"
        };

        if (GUILayout.Button(label, GUILayout.Height(20f)))
        {
            Rect r = GUILayoutUtility.GetLastRect();
            switch (kind)
            {
                case AutoObjectiveKind.Kill:
                    PopupWindow.Show(r, new UnitPickerPopupContent(picked => AppendKillCondition(objective, picked)));
                    break;
                case AutoObjectiveKind.Talk:
                    PopupWindow.Show(r, new UnitPickerPopupContent(picked => AppendTalkCondition(objective, picked)));
                    break;
                case AutoObjectiveKind.Collect:
                    SkyPrisonItemPickerPopup.Open(null, picked => AppendCollectCondition(objective, picked as ItemDefinition), nameof(ItemDefinition));
                    break;
                case AutoObjectiveKind.Deliver:
                    // 缴纳只需要再选一件物品——收件人沿用这个节点已有的那条交付条件
                    // 里的NPC，不再让用户重选一遍(同一个节点交给两个不同的人是极少见
                    // 的配置，真要那么配可以用下面的通用句型列表)。这样也顺带绕开了
                    // "物品选择器关闭后GUI矩形已失效、NPC选择器弹不出来"那个坑。
                    UnitDefinition inheritNpc = null;
                    foreach (LogicSentenceInstance ex in objective.completionConditions)
                    {
                        if (ex != null && ex.templateId == "cond_has_delivered_item_to_npc")
                        {
                            inheritNpc = ex.GetAssignment("npc")?.value?.assetReference as UnitDefinition;
                            if (inheritNpc != null) break;
                        }
                    }
                    UnitDefinition capturedNpc = inheritNpc;
                    SkyPrisonItemPickerPopup.Open(null,
                        picked => AppendDeliverCondition(objective, picked as ItemDefinition, capturedNpc),
                        nameof(ItemDefinition));
                    break;
            }
        }

        return false; // 追加动作自己走SetDirty+SaveAssets，不靠返回值触发外层保存
    }

    private void CommitObjectiveConditionAppend()
    {
        EditorUtility.SetDirty(_current);
        AssetDatabase.SaveAssets();

        // 资产/单位选择器的回调是在OnGUI之外跑的，不主动请求重绘的话，界面要等下
        // 一次鼠标移动或点击才会反映出新加的条件——看起来完全就像"点了没反应"。
        // 只在真的追加成功后走一次，不是每帧刷。
        EditorWindow.focusedWindow?.Repaint();
        foreach (EditorWindow w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            w.Repaint();
    }

    private void AppendKillCondition(QuestObjective objective, UnitDefinition unit)
    {
        if (objective == null || unit == null || string.IsNullOrWhiteSpace(unit.unitId))
        {
            if (unit != null && string.IsNullOrWhiteSpace(unit.unitId))
                Debug.LogWarning($"[SkyPrisonQuestPage] 单位 {unit.name} 没有配置 unitId，无法生成击杀计数器，条件未追加。");
            return;
        }
        objective.completionConditions.Add(BuildUnitKillCountCondition(unit, QuickObjectiveDefaultCount));
        CommitObjectiveConditionAppend();
    }

    private void AppendCollectCondition(QuestObjective objective, ItemDefinition item)
    {
        if (objective == null || item == null) return;
        objective.completionConditions.Add(BuildHasItemCountCondition(item, QuickObjectiveDefaultCount));
        CommitObjectiveConditionAppend();
    }

    private void AppendTalkCondition(QuestObjective objective, UnitDefinition npc)
    {
        if (objective == null || npc == null || string.IsNullOrWhiteSpace(npc.unitId))
        {
            if (npc != null && string.IsNullOrWhiteSpace(npc.unitId))
                Debug.LogWarning($"[SkyPrisonQuestPage] 单位 {npc.name} 没有配置 unitId，无法生成对话计数器，条件未追加。");
            return;
        }
        objective.completionConditions.Add(BuildHasTalkedToNpcCondition(npc));
        CommitObjectiveConditionAppend();
    }

    private void AppendDeliverCondition(QuestObjective objective, ItemDefinition item, UnitDefinition npc)
    {
        if (objective == null || item == null) return;
        if (npc == null || string.IsNullOrWhiteSpace(npc.unitId))
        {
            if (npc != null)
                Debug.LogWarning($"[SkyPrisonQuestPage] 单位 {npc.name} 没有配置 unitId，无法生成交付计数器，条件未追加。");
            return;
        }
        objective.completionConditions.Add(BuildHasDeliveredItemToNpcCondition(item, npc, QuickObjectiveDefaultCount));
        CommitObjectiveConditionAppend();
    }

    private const int QuickObjectiveDefaultCount = 5;

    // 描述存的是带 {0}(名字)/{1}(数量) 占位符的模板文本，不是烤死的名字——名字/
    // 数量永远从 completionConditions 里那条结构化条件(真正的资产引用)现读现填，
    // 单位改名/在Project窗口重命名都会立刻反映在这里，不用回来手动改文案。策划
    // 唯一需要编辑的是模板里"讨伐"、"×"这几个字本身(每种语言各自的动词/单位量词)，
    // 这部分本来就需要人工翻译，不该自动生成。
    private void QuickAddKillObjective(UnitDefinition unit, List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        if (unit == null || string.IsNullOrWhiteSpace(unit.unitId))
        {
            if (unit != null)
                Debug.LogWarning($"[SkyPrisonQuestPage] 单位 {unit.name} 没有配置 unitId，无法生成击杀计数器，快捷目标未创建。");
            return;
        }

        var objective = new QuestObjective();
        FillObjectiveTemplateAllLanguages(objective.description, langs, "讨伐 {0} ×{1}", "{0}を討伐 ×{1}", "Defeat {0} x{1}");
        objective.completionConditions.Add(BuildUnitKillCountCondition(unit, QuickObjectiveDefaultCount));

        _current.objectives.Add(objective);
        _objectiveFoldouts[objective] = false;
        EditorUtility.SetDirty(_current);
        AssetDatabase.SaveAssets();
    }

    private void QuickAddCollectObjective(ItemDefinition item, List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        if (item == null) return;

        var objective = new QuestObjective();
        FillObjectiveTemplateAllLanguages(objective.description, langs, "收集 {0} ×{1}", "{0}を収集 ×{1}", "Collect {0} x{1}");
        objective.completionConditions.Add(BuildHasItemCountCondition(item, QuickObjectiveDefaultCount));

        _current.objectives.Add(objective);
        _objectiveFoldouts[objective] = false;
        EditorUtility.SetDirty(_current);
        AssetDatabase.SaveAssets();
    }

    // 对话类目标没有"数量"概念，只有"发生过没有"——模板文本不用{1}占位符，
    // string.Format 对多传的未使用参数没问题，不需要单独一套Format调用。
    private void QuickAddTalkObjective(UnitDefinition npc, List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        if (npc == null || string.IsNullOrWhiteSpace(npc.unitId))
        {
            if (npc != null)
                Debug.LogWarning($"[SkyPrisonQuestPage] 单位 {npc.name} 没有配置 unitId，无法生成对话计数器，快捷目标未创建。");
            return;
        }

        var objective = new QuestObjective();
        FillObjectiveTemplateAllLanguages(objective.description, langs, "与 {0} 对话", "{0}と話す", "Talk to {0}");
        objective.completionConditions.Add(BuildHasTalkedToNpcCondition(npc));

        _current.objectives.Add(objective);
        _objectiveFoldouts[objective] = false;
        EditorUtility.SetDirty(_current);
        AssetDatabase.SaveAssets();
    }

    // 交付类目标模板同时要用到{0}(物品名)/{1}(数量)/{2}(交付对象名字)三个占位符——
    // string.Format对模板里没用到的占位符不会报错，所以哪怕以后手改文案只留{0}也
    // 不会崩，只是不会显示交付对象而已。
    private void QuickAddDeliverObjective(ItemDefinition item, UnitDefinition npc, List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        if (item == null) return;
        if (npc == null || string.IsNullOrWhiteSpace(npc.unitId))
        {
            if (npc != null)
                Debug.LogWarning($"[SkyPrisonQuestPage] 单位 {npc.name} 没有配置 unitId，无法生成交付计数器，快捷目标未创建。");
            return;
        }

        var objective = new QuestObjective();
        FillObjectiveTemplateAllLanguages(objective.description, langs,
            "交付 {0} ×{1} 给 {2}", "{0} を {1} つ {2} に渡す", "Deliver {0} x{1} to {2}");
        objective.completionConditions.Add(BuildHasDeliveredItemToNpcCondition(item, npc, QuickObjectiveDefaultCount));

        _current.objectives.Add(objective);
        _objectiveFoldouts[objective] = false;
        EditorUtility.SetDirty(_current);
        AssetDatabase.SaveAssets();
    }

    // 快捷模板固定就中/日/英三种语言代码，按项目里已经确立的约定(zh-CN/ja-JP/
    // en-US，见 LocalizationProjectSettings 的字段提示)——不认识的语言代码留空，
    // 不强行拿中文占位，免得看起来像"已经翻译好了"。
    private static void FillObjectiveTemplateAllLanguages(
        List<LocalizedTextEntry> texts, List<LocalizationProjectSettings.LanguageEntry> langs,
        string zh, string ja, string en)
    {
        EnsureLanguageSlots(texts, langs);
        foreach (LocalizedTextEntry t in texts)
        {
            string value = t.languageCode switch
            {
                "zh-CN" => zh,
                "ja-JP" => ja,
                "en-US" => en,
                _ => null
            };
            if (value != null) t.text = value;
        }
    }

    // 目标预览行的标题——跟游戏内任务日志共用同一套解析(QuestObjectiveTextResolver，
    // 在运行时程序集，不在Editor文件夹下)，避免编辑器预览和玩家实际看到的文字两边
    // 各写一份、以后改一边忘了改另一边导致预览和游戏内不一致。
    private static string ResolveObjectiveTitle(QuestObjective objective, UnitDefinition giverNpc)
        => QuestObjectiveTextResolver.ResolveTitle(objective, giverNpc);

    private static LogicSentenceInstance BuildUnitKillCountCondition(UnitDefinition unit, int count)
    {
        var instance = new LogicSentenceInstance { templateId = "cond_unit_kill_count_at_least", enabled = true };
        instance.GetOrCreateAssignment("unit").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.UnitAsset,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = unit
        };
        instance.GetOrCreateAssignment("count").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.Int,
            sourceType = LogicValueSourceType.Constant,
            intValue = count
        };
        return instance;
    }

    private static LogicSentenceInstance BuildHasTalkedToNpcCondition(UnitDefinition npc)
    {
        var instance = new LogicSentenceInstance { templateId = "cond_has_talked_to_npc", enabled = true };
        instance.GetOrCreateAssignment("unit").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.UnitAsset,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = npc
        };
        return instance;
    }

    private static LogicSentenceInstance BuildHasItemCountCondition(ItemDefinition item, int count)
    {
        var instance = new LogicSentenceInstance { templateId = "cond_has_item_count_at_least", enabled = true };
        instance.GetOrCreateAssignment("item").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.Item,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = item
        };
        instance.GetOrCreateAssignment("count").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.Int,
            sourceType = LogicValueSourceType.Constant,
            intValue = count
        };
        return instance;
    }

    private static LogicSentenceInstance BuildHasDeliveredItemToNpcCondition(ItemDefinition item, UnitDefinition npc, int count)
    {
        var instance = new LogicSentenceInstance { templateId = "cond_has_delivered_item_to_npc", enabled = true };
        instance.GetOrCreateAssignment("item").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.Item,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = item
        };
        instance.GetOrCreateAssignment("npc").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.UnitAsset,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = npc
        };
        instance.GetOrCreateAssignment("count").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.Int,
            sourceType = LogicValueSourceType.Constant,
            intValue = count
        };
        return instance;
    }

    // 讨伐/收集这两类结构化目标的详情——真正的资产引用+数量都在这里编辑，不走
    // 通用的完成条件列表(那里编辑的是完整句型，容易改出跟"讨伐/收集"预设不匹配
    // 的slotId组合，反而把结构化关系弄断)。"定位"能直接跳到Project窗口里那个
    // 资产，回应"以后要不要点名字看详情"的关切——引用是真的，能跳转就是证明。
    private enum AutoObjectiveKind { None, Kill, Collect, Talk, Deliver }

    // 讨伐/收集/对话 三类结构化目标共用同一套"选择+定位+更换"UI；数量字段只有
    // 讨伐/收集需要——对话是"发生过没有"的判定，没有数量概念，Talk 类型直接跳过
    // 那一段，不会莫名其妙给 cond_has_talked_to_npc 这个模板本来没有的"count"槽位
    // 造一个从没被任何地方读取的孤儿assignment出来。交付比这三类都多一个引用
    // (同时要物品+收件NPC)，走单独的分支，不硬塞进"单slotId"的这套抽象里。
    private bool DrawAutoObjectiveDetail(QuestObjective objective, LogicSentenceInstance condition, AutoObjectiveKind kind)
    {
        if (kind == AutoObjectiveKind.Deliver)
            return DrawDeliverObjectiveDetail(objective, condition);

        bool dirty = false;
        // 讨伐/对话都是"unit"槽位(同样指向UnitDefinition)，只有收集是"item"。
        string slotId = kind == AutoObjectiveKind.Collect ? "item" : "unit";
        UnityEngine.Object current = condition.GetAssignment(slotId)?.value?.assetReference;

        string fieldLabel = kind switch
        {
            AutoObjectiveKind.Kill => "讨伐目标",
            AutoObjectiveKind.Collect => "收集目标",
            AutoObjectiveKind.Talk => "对话目标",
            _ => ""
        };

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(fieldLabel, GUILayout.Width(EditorGUIUtility.labelWidth - 4f));

        string currentLabel = current != null
            ? (kind == AutoObjectiveKind.Collect ? GetItemDisplayLabel(current as ItemDefinition) : UnitPickerPopupContent.GetLabel(current as UnitDefinition))
            : "（未选择——文案里的名字会显示为空）";
        GUILayout.Label(currentLabel, EditorStyles.boldLabel);

        GUILayout.FlexibleSpace();

        using (new EditorGUI.DisabledScope(current == null))
        {
            if (GUILayout.Button("定位", GUILayout.Width(44f)))
                EditorGUIUtility.PingObject(current);
        }

        if (GUILayout.Button("更换…", GUILayout.Width(52f)))
        {
            if (kind == AutoObjectiveKind.Collect)
            {
                SkyPrisonItemPickerPopup.Open(current, picked =>
                {
                    condition.GetOrCreateAssignment("item").value = new LogicSlotValue
                    {
                        valueType = LogicSlotValueType.Item,
                        sourceType = LogicValueSourceType.AssetReference,
                        assetReference = picked
                    };
                    EditorUtility.SetDirty(_current);
                    AssetDatabase.SaveAssets();
                }, nameof(ItemDefinition));
            }
            else
            {
                Rect btnRect = GUILayoutUtility.GetLastRect();
                PopupWindow.Show(btnRect, new UnitPickerPopupContent(picked =>
                {
                    condition.GetOrCreateAssignment("unit").value = new LogicSlotValue
                    {
                        valueType = LogicSlotValueType.UnitAsset,
                        sourceType = LogicValueSourceType.AssetReference,
                        assetReference = picked
                    };
                    EditorUtility.SetDirty(_current);
                    AssetDatabase.SaveAssets();
                }));
            }
        }
        EditorGUILayout.EndHorizontal();

        if (kind != AutoObjectiveKind.Talk)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("数量", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
            LogicSlotAssignment countAssign = condition.GetOrCreateAssignment("count");
            if (countAssign.value == null)
                countAssign.value = new LogicSlotValue { valueType = LogicSlotValueType.Int, sourceType = LogicValueSourceType.Constant };
            int count = countAssign.value.intValue;
            int newCount = EditorGUILayout.IntField(count, GUILayout.Width(70f));
            if (newCount != count) { countAssign.value.intValue = Mathf.Max(0, newCount); dirty = true; }
            EditorGUILayout.EndHorizontal();
        }

        // 对话类目标是唯一一种玩家会真的"点开跟NPC说话"来推进的类型(签到计数器由
        // NPCDialogueWindowController.CheckInWithGiverForQuest驱动)，配一句专属台词
        // 让NPC不是干巴巴地重复任务描述文字。讨伐/收集是在场景里被动完成的，没有
        // 对应的对话时刻可以说这句话，不给这两类加这个字段。
        if (kind == AutoObjectiveKind.Talk)
        {
            GUILayout.Space(4f);
            dirty |= DrawNpcResponseSentenceRow(objective, _current.giverNpc);
        }

        return dirty;
    }

    // 交付比讨伐/收集/对话都多一个引用(物品+收件NPC两个都要选)，两行各自独立的
    // "选择+定位+更换"，数量行复用跟讨伐/收集完全一样的写法。
    private bool DrawDeliverObjectiveDetail(QuestObjective objective, LogicSentenceInstance condition)
    {
        DrawObjectiveReferenceRow(condition, "item", "交付物品", isItem: true);
        DrawObjectiveReferenceRow(condition, "npc", "交付对象", isItem: false);

        bool dirty = false;
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("数量", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
        LogicSlotAssignment countAssign = condition.GetOrCreateAssignment("count");
        if (countAssign.value == null)
            countAssign.value = new LogicSlotValue { valueType = LogicSlotValueType.Int, sourceType = LogicValueSourceType.Constant };
        int count = countAssign.value.intValue;
        int newCount = EditorGUILayout.IntField(count, GUILayout.Width(70f));
        if (newCount != count) { countAssign.value.intValue = Mathf.Max(0, newCount); dirty = true; }
        EditorGUILayout.EndHorizontal();

        // 交付也是玩家点开"有事情找你"、真的跟NPC打交道那一刻确认的(ConfirmDeliveryPopup)，
        // 跟对话目标同一个道理，也配一句专属台词——但台词要从"交付对象"自己的句子库
        // 挑，不是giverNpc的：交付对象不一定等于发任务人(比如A给的任务、要求交给B)，
        // 运行时ConfirmDeliveryPopup是在收件人B自己的对话窗口里播这句台词，台词ID
        // 得来自B的句子库，不然会出现"选的时候好好的，运行时播放却是空"的错配。
        UnitDefinition deliverTo = condition.GetAssignment("npc")?.value?.assetReference as UnitDefinition;
        GUILayout.Space(4f);
        dirty |= DrawNpcResponseSentenceRow(objective, deliverTo);

        return dirty;
    }

    // 更换动作是异步弹窗回调(物品是独立EditorWindow，NPC是PopupWindow.Show)，
    // 不经过这个方法本身的dirty返回值——回调里直接EditorUtility.SetDirty+
    // SaveAssets，跟DrawAutoObjectiveDetail里"更换…"按钮那份是同一个套路。
    private void DrawObjectiveReferenceRow(LogicSentenceInstance condition, string slotId, string label, bool isItem)
    {
        UnityEngine.Object current = condition.GetAssignment(slotId)?.value?.assetReference;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth - 4f));

        string currentLabel = current != null
            ? (isItem ? GetItemDisplayLabel(current as ItemDefinition) : UnitPickerPopupContent.GetLabel(current as UnitDefinition))
            : "（未选择——文案里的名字会显示为空）";
        GUILayout.Label(currentLabel, EditorStyles.boldLabel);

        GUILayout.FlexibleSpace();

        using (new EditorGUI.DisabledScope(current == null))
        {
            if (GUILayout.Button("定位", GUILayout.Width(44f)))
                EditorGUIUtility.PingObject(current);
        }

        if (GUILayout.Button("更换…", GUILayout.Width(52f)))
        {
            if (isItem)
            {
                SkyPrisonItemPickerPopup.Open(current, picked =>
                {
                    condition.GetOrCreateAssignment(slotId).value = new LogicSlotValue
                    {
                        valueType = LogicSlotValueType.Item,
                        sourceType = LogicValueSourceType.AssetReference,
                        assetReference = picked
                    };
                    EditorUtility.SetDirty(_current);
                    AssetDatabase.SaveAssets();
                }, nameof(ItemDefinition));
            }
            else
            {
                Rect btnRect = GUILayoutUtility.GetLastRect();
                PopupWindow.Show(btnRect, new UnitPickerPopupContent(picked =>
                {
                    condition.GetOrCreateAssignment(slotId).value = new LogicSlotValue
                    {
                        valueType = LogicSlotValueType.UnitAsset,
                        sourceType = LogicValueSourceType.AssetReference,
                        assetReference = picked
                    };
                    EditorUtility.SetDirty(_current);
                    AssetDatabase.SaveAssets();
                }));
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>目标的NPC反馈——两种互斥方式，触发包优先：①"演出触发包"，主线级别
    /// 用，手动立刻执行一整套触发器动作(镜头/暂停/连续台词等)，跟场景自动生成、
    /// 按动机+条件驱动的那一套触发器是完全独立的第二条执行路径(见
    /// TriggerPackageRuntime.ExecuteImmediatelyCoroutine)；②简单的对话台词列表，从responseNpc
    /// (对话目标=giverNpc本人；交付目标=交付对象，不一定等于giverNpc)自己的句子库里
    /// 选，配几句就按顺序完整念几句(不是随机抽一句，见DialogueSubtitleHUD.
    /// ShowLineSequence)——顺序真的有意义，所以列表要支持上下移动，不只是加/删。
    /// 配了①，②整个变灰不生效(视觉上提示"这个不会被用到")。</summary>
    private bool DrawNpcResponseSentenceRow(QuestObjective objective, UnitDefinition responseNpc)
    {
        bool dirty = false;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("演出触发包(可选，优先于下面的台词)", GUILayout.Width(EditorGUIUtility.labelWidth + 60f));
        var newPkg = (TriggerPackage)EditorGUILayout.ObjectField(objective.npcResponseTriggerPackage, typeof(TriggerPackage), false);
        if (newPkg != objective.npcResponseTriggerPackage) { objective.npcResponseTriggerPackage = newPkg; dirty = true; }
        EditorGUILayout.EndHorizontal();

        using var disabledScope = new EditorGUI.DisabledScope(objective.npcResponseTriggerPackage != null);

        DialogueSentenceLibrary lib = responseNpc != null && responseNpc.dialogue != null
            ? responseNpc.dialogue.sentenceLibrary
            : null;

        EditorGUILayout.LabelField("对话台词(可选，配多句=按顺序完整念完)", GUILayout.Width(EditorGUIUtility.labelWidth + 220f));

        if (lib == null || lib.entries.Count == 0)
        {
            GUILayout.Label("（对应NPC没配对话/句子库，没法选——留空则用任务描述当反馈）", EditorStyles.wordWrappedMiniLabel);
            return dirty;
        }

        int removeIdx = -1, moveUpIdx = -1, moveDownIdx = -1;
        for (int si = 0; si < objective.npcResponseSentenceIds.Count; si++)
        {
            string currentId = objective.npcResponseSentenceIds[si];
            var currentEntry = lib.entries.Find(e => e != null && e.sentenceId == currentId);
            string btnLabel = currentEntry != null ? BuildQuestSentenceOptionLabel(currentEntry) : "（未选择）";

            EditorGUILayout.BeginHorizontal();
            Rect btnRect = GUILayoutUtility.GetRect(new GUIContent(btnLabel), EditorStyles.objectField, GUILayout.ExpandWidth(true));
            if (GUI.Button(btnRect, btnLabel, EditorStyles.objectField))
            {
                int capturedIndex = si;
                PopupWindow.Show(btnRect, new SentencePickerPopupContent(lib.entries, BuildQuestSentenceOptionLabel, picked =>
                {
                    objective.npcResponseSentenceIds[capturedIndex] = picked;
                    EditorUtility.SetDirty(_current);
                    AssetDatabase.SaveAssets();
                }));
            }
            using (new EditorGUI.DisabledScope(si == 0))
            {
                if (GUILayout.Button("↑", GUILayout.Width(22f))) moveUpIdx = si;
            }
            using (new EditorGUI.DisabledScope(si == objective.npcResponseSentenceIds.Count - 1))
            {
                if (GUILayout.Button("↓", GUILayout.Width(22f))) moveDownIdx = si;
            }
            if (GUILayout.Button("×", GUILayout.Width(24f)))
                removeIdx = si;
            EditorGUILayout.EndHorizontal();
        }

        if (removeIdx >= 0)
        {
            objective.npcResponseSentenceIds.RemoveAt(removeIdx);
            dirty = true;
        }
        else if (moveUpIdx > 0)
        {
            (objective.npcResponseSentenceIds[moveUpIdx - 1], objective.npcResponseSentenceIds[moveUpIdx]) =
                (objective.npcResponseSentenceIds[moveUpIdx], objective.npcResponseSentenceIds[moveUpIdx - 1]);
            dirty = true;
        }
        else if (moveDownIdx >= 0 && moveDownIdx < objective.npcResponseSentenceIds.Count - 1)
        {
            (objective.npcResponseSentenceIds[moveDownIdx + 1], objective.npcResponseSentenceIds[moveDownIdx]) =
                (objective.npcResponseSentenceIds[moveDownIdx], objective.npcResponseSentenceIds[moveDownIdx + 1]);
            dirty = true;
        }

        if (GUILayout.Button("＋ 添加一句台词"))
        {
            objective.npcResponseSentenceIds.Add(lib.entries[0].sentenceId);
            dirty = true;
        }

        return dirty;
    }

    /// <summary>轻量版——不像SkyPrisonInteractionPackagePage.BuildSentenceOptionLabel
    /// 那样处理说话人匿名/多语言当前语言这些细节，这里只是给挑选台词用的辅助显示，
    /// 不追求完全一致，够认出是哪句话即可。</summary>
    private static string BuildQuestSentenceOptionLabel(DialogueSentenceLibrary.SentenceEntry entry)
    {
        string id = string.IsNullOrEmpty(entry.sentenceId) ? "(未命名)" : entry.sentenceId;
        string text = "";
        foreach (var t in entry.texts)
        {
            if (!string.IsNullOrWhiteSpace(t.text)) { text = t.text; break; }
        }
        if (text.Length > 22) text = text.Substring(0, 22) + "…";
        return string.IsNullOrEmpty(text) ? id : $"{id}  |  {text}";
    }

    private static string GetItemDisplayLabel(ItemDefinition item)
    {
        if (item == null) return "";
        return string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName;
    }

    private static void DrawObjectiveCheckbox(Rect rect, bool optional)
    {
        Color borderColor = optional ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.75f);
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), borderColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), borderColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), borderColor);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), borderColor);
    }

    // ── 本地化文本 ─────────────────────────────────────────────────────────

    // 标签放字段上面一行(不是并排)，长标签(比如"目标描述(玩家会看到的文字)")才
    // 有地方自动换行——之前并排布局把标签硬夹在 labelWidth 那点宽度里，长一点
    // 的说明文字直接被裁掉，用户反馈"文字显示不全"。
    private static bool DrawLocalizedTextFields(string label, List<LocalizedTextEntry> texts, List<LocalizationProjectSettings.LanguageEntry> langs, bool multiline = false)
    {
        bool dirty = false;
        EnsureLanguageSlots(texts, langs);

        GUIStyle labelStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };

        foreach (LocalizationProjectSettings.LanguageEntry lang in langs)
        {
            foreach (LocalizedTextEntry t in texts)
            {
                if (t.languageCode != lang.languageCode) continue;

                string fieldLabel = lang.isDefault ? $"{label}（{lang.displayName}）★" : $"{label}（{lang.displayName}）";
                GUILayout.Label(fieldLabel, labelStyle);
                string newText = multiline
                    ? EditorGUILayout.TextArea(t.text, GUILayout.MinHeight(40f))
                    : EditorGUILayout.TextField(t.text);
                if (newText != t.text) { t.text = newText; dirty = true; }
                GUILayout.Space(2f);
                break;
            }
        }

        return dirty;
    }

    private static void EnsureLanguageSlots(List<LocalizedTextEntry> texts, List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        foreach (LocalizationProjectSettings.LanguageEntry lang in langs)
        {
            bool found = false;
            foreach (LocalizedTextEntry t in texts)
                if (t.languageCode == lang.languageCode) { found = true; break; }
            if (!found)
                texts.Add(new LocalizedTextEntry { languageCode = lang.languageCode, text = "" });
        }
    }

    private List<LocalizationProjectSettings.LanguageEntry> GetEnabledLanguages()
    {
        var result = new List<LocalizationProjectSettings.LanguageEntry>();
        if (_settings == null) return result;

        foreach (LocalizationProjectSettings.LanguageEntry l in _settings.languages)
            if (l != null && l.enabled && l.isDefault) result.Add(l);
        foreach (LocalizationProjectSettings.LanguageEntry l in _settings.languages)
            if (l != null && l.enabled && !l.isDefault) result.Add(l);

        return result;
    }

    // ── 条件/动作列表——两者形状相同(List<LogicSentenceInstance>)，用同一个
    // 方法按 category 分流，跟 SkyPrisonInteractionPackagePage.DrawConditionList
    // 是同一套做法，那边只有条件一种用途所以没参数化，这里条件/动作都要用到。

    // ── 任务报酬 ──────────────────────────────────────────────────────────
    // 不新开一份"奖励"数据——本质就是onCompleteActions里的act_add_currency/
    // act_add_item_to_bag这两种动作，这里只是过滤出来，用更直观的"图标+名字+
    // 数量"行展示/编辑，而不是让策划自己去完成动作列表里一条条翻句型。跟物品池
    // 选物品同一个弹窗(SkyPrisonItemPickerPopup)，用户明确要求保持一致。

    private const int QuestRewardDefaultCurrencyAmount = 100;
    private const int QuestRewardDefaultItemCount = 1;

    private bool DrawQuestRewards()
    {
        bool dirty = false;

        Rect containerRect = EditorGUILayout.BeginVertical();
        EditorGUI.DrawRect(containerRect, ListContainerBg);
        DrawThinBorder(containerRect, new Color(1f, 1f, 1f, 0.08f));
        GUILayout.Space(4f);

        List<LogicSentenceInstance> currencyRewards = _current.onCompleteActions
            .Where(s => s != null && s.templateId == "act_add_currency").ToList();
        List<LogicSentenceInstance> itemRewards = _current.onCompleteActions
            .Where(s => s != null && s.templateId == "act_add_item_to_bag").ToList();

        if (currencyRewards.Count == 0 && itemRewards.Count == 0)
        {
            EditorGUILayout.LabelField("（暂无奖励）", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(2f);
        }

        LogicSentenceInstance toRemove = null;
        int rowIndex = 0;

        foreach (LogicSentenceInstance sentence in currencyRewards)
        {
            CurrencyDefinition currency = sentence.GetAssignment("currencyId")?.value?.assetReference as CurrencyDefinition;
            LogicSlotAssignment amountAssign = sentence.GetOrCreateAssignment("amount");
            if (amountAssign.value == null)
                amountAssign.value = new LogicSlotValue { valueType = LogicSlotValueType.Int, sourceType = LogicValueSourceType.Constant };

            Rect rowRect = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(rowRect, rowIndex % 2 == 0 ? RowEvenBg : RowOddBg);
            rowIndex++;
            GUILayout.Space(6f);

            Texture icon = currency != null && currency.icon != null ? currency.icon.texture : null;
            Rect iconRect = GUILayoutUtility.GetRect(20f, 20f, GUILayout.Width(20f), GUILayout.Height(20f));
            if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            GUILayout.Space(4f);

            string currencyLabel = currency != null
                ? (string.IsNullOrWhiteSpace(currency.displayName) ? currency.name : currency.displayName)
                : "（未选择货币）";
            GUILayout.Label(currencyLabel, GUILayout.Width(140f));

            int amount = amountAssign.value.intValue;
            int newAmount = EditorGUILayout.IntField(amount, GUILayout.Width(70f));
            if (newAmount != amount) { amountAssign.value.intValue = Mathf.Max(0, newAmount); dirty = true; }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("删除", GUILayout.Width(50f)))
                toRemove = sentence;
            GUILayout.Space(6f);
            EditorGUILayout.EndHorizontal();
        }

        foreach (LogicSentenceInstance sentence in itemRewards)
        {
            ItemDefinition item = sentence.GetAssignment("itemId")?.value?.assetReference as ItemDefinition;
            LogicSlotAssignment countAssign = sentence.GetOrCreateAssignment("amount");
            if (countAssign.value == null)
                countAssign.value = new LogicSlotValue { valueType = LogicSlotValueType.Int, sourceType = LogicValueSourceType.Constant };

            Rect rowRect = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(rowRect, rowIndex % 2 == 0 ? RowEvenBg : RowOddBg);
            rowIndex++;
            GUILayout.Space(6f);

            Texture icon = item != null && item.icon != null ? item.icon.texture : null;
            Rect iconRect = GUILayoutUtility.GetRect(20f, 20f, GUILayout.Width(20f), GUILayout.Height(20f));
            if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            GUILayout.Space(4f);

            string itemLabel = item != null
                ? (string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName)
                : "（未选择物品）";
            GUILayout.Label(itemLabel, GUILayout.Width(140f));

            int count = countAssign.value.intValue;
            int newCount = EditorGUILayout.IntField(count, GUILayout.Width(70f));
            if (newCount != count) { countAssign.value.intValue = Mathf.Max(0, newCount); dirty = true; }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("删除", GUILayout.Width(50f)))
                toRemove = sentence;
            GUILayout.Space(6f);
            EditorGUILayout.EndHorizontal();
        }

        if (toRemove != null)
        {
            _current.onCompleteActions.Remove(toRemove);
            dirty = true;
        }

        GUILayout.Space(2f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("＋ 添加货币奖励", GUILayout.Height(20f)))
        {
            SkyPrisonItemPickerPopup.Open(null, picked => AddCurrencyReward(picked as CurrencyDefinition), nameof(CurrencyDefinition));
        }
        if (GUILayout.Button("＋ 添加道具奖励", GUILayout.Height(20f)))
        {
            SkyPrisonItemPickerPopup.Open(null, picked => AddItemReward(picked as ItemDefinition), nameof(ItemDefinition));
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(4f);

        EditorGUILayout.EndVertical();

        return dirty;
    }

    private void AddCurrencyReward(CurrencyDefinition currency)
    {
        if (currency == null) return;

        var sentence = new LogicSentenceInstance { templateId = "act_add_currency", enabled = true };
        sentence.GetOrCreateAssignment("currencyId").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.Currency,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = currency
        };
        sentence.GetOrCreateAssignment("amount").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.Int,
            sourceType = LogicValueSourceType.Constant,
            intValue = QuestRewardDefaultCurrencyAmount
        };

        _current.onCompleteActions.Add(sentence);
        EditorUtility.SetDirty(_current);
        AssetDatabase.SaveAssets();
    }

    private void AddItemReward(ItemDefinition item)
    {
        if (item == null) return;

        var sentence = new LogicSentenceInstance { templateId = "act_add_item_to_bag", enabled = true };
        sentence.GetOrCreateAssignment("itemId").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.Item,
            sourceType = LogicValueSourceType.AssetReference,
            assetReference = item
        };
        sentence.GetOrCreateAssignment("amount").value = new LogicSlotValue
        {
            valueType = LogicSlotValueType.Int,
            sourceType = LogicValueSourceType.Constant,
            intValue = QuestRewardDefaultItemCount
        };

        _current.onCompleteActions.Add(sentence);
        EditorUtility.SetDirty(_current);
        AssetDatabase.SaveAssets();
    }

    // 容器包一层深色底+细边框——列表里放的是同一个节点下要"同时满足/同时执行"
    // 的复合条目(比如一个目标下"杀死A怪5只"+"杀死B怪5只"两条完成条件，AND关系、
    // 没有先后顺序)，用户明确要求视觉上要看出这是一组绑在一起的东西，不能是
    // 光秃秃一个"+添加"按钮飘在那，容易被误读成"这里还没有内容"。
    private bool DrawSentenceList(List<LogicSentenceInstance> sentences, LogicSentenceCategory category, string addLabel, Func<LogicSentenceInstance, bool> hideFilter = null)
    {
        bool dirty = false;

        Rect containerRect = EditorGUILayout.BeginVertical();
        EditorGUI.DrawRect(containerRect, ListContainerBg);
        DrawThinBorder(containerRect, new Color(1f, 1f, 1f, 0.08f));
        GUILayout.Space(4f);

        // 过滤掉已经在"任务报酬"友好界面里单独管理的条目(增加货币/增加物品到背包)，
        // 不然同一条奖励会在两个地方各显示一遍——filter只影响这里显示什么，增删
        // 操作还是直接对传进来的完整列表做，不会产生第二份数据。
        List<LogicSentenceInstance> visible = hideFilter == null
            ? sentences
            : sentences.Where(s => !hideFilter(s)).ToList();

        LogicSentenceInstance toRemove = null;
        if (visible.Count == 0)
        {
            EditorGUILayout.LabelField("（暂无——留空表示不设限制/不执行任何动作）", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(2f);
        }
        else
        {
            for (int i = 0; i < visible.Count; i++)
            {
                LogicSentenceInstance sentence = visible[i];

                Rect rowRect = EditorGUILayout.BeginHorizontal();
                EditorGUI.DrawRect(rowRect, i % 2 == 0 ? RowEvenBg : RowOddBg);
                GUILayout.Space(6f);
                EditorGUILayout.LabelField(DescribeSentence(sentence));

                LogicSentenceInstance captured = sentence;
                if (GUILayout.Button("编辑…", GUILayout.Width(60f)))
                {
                    AILogicSentenceTemplatePickerWindow.OpenForEdit(
                        category, LogicTemplateContext.Trigger, captured,
                        edited =>
                        {
                            int idx = sentences.IndexOf(captured);
                            if (idx >= 0) sentences[idx] = edited;
                            EditorUtility.SetDirty(_current);
                            AssetDatabase.SaveAssets();
                        });
                }
                if (GUILayout.Button("删除", GUILayout.Width(50f)))
                    toRemove = sentence;
                GUILayout.Space(6f);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(1f);
            }
        }

        if (toRemove != null)
        {
            sentences.Remove(toRemove);
            dirty = true;
        }

        GUILayout.Space(2f);
        if (GUILayout.Button("＋ " + addLabel, GUILayout.Height(20f)))
        {
            AILogicSentenceTemplatePickerWindow.Open(
                category, LogicTemplateContext.Trigger,
                created =>
                {
                    sentences.Add(created);
                    EditorUtility.SetDirty(_current);
                    AssetDatabase.SaveAssets();
                });
        }
        GUILayout.Space(4f);

        EditorGUILayout.EndVertical();

        return dirty;
    }

    private static string DescribeSentence(LogicSentenceInstance sentence)
    {
        if (sentence == null || string.IsNullOrEmpty(sentence.templateId)) return "<未设置>";
        LogicSentenceTemplate template = AILogicSentenceTemplateLibrary.GetTemplateById(sentence.templateId);
        return template != null ? template.displayName : sentence.templateId;
    }

    private static void DrawSectionHeader(string title)
    {
        Rect r = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, HeaderBg);
        GUI.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 8f, r.height),
                  title, EditorStyles.boldLabel);
        GUILayout.Space(4f);
    }
}
#endif
