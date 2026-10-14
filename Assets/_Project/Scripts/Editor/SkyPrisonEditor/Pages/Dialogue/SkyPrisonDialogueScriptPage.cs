#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 天空囚笼编辑器 — 台本编辑器页面。
/// 左栏：项目里全部 DialogueSentenceLibrary 资产，选中/新建。
/// 右栏：选中句子库的句子条目——编号/章节/备注 + 各启用语言译文，
/// 表格样式跟字典表页面一致，方便策划两边切换时体验一致。
/// </summary>
public class SkyPrisonDialogueScriptPage : SkyPrisonEditorPageBase
{
    public override string TabName => "台本编辑器";

    // ── 资产引用 ────────────────────────────────────────────────────────
    private LocalizationProjectSettings _settings;
    private DialogueChapterSettings _chapterSettings;
    private readonly List<DialogueSentenceLibrary> _libraries = new List<DialogueSentenceLibrary>();
    private DialogueSentenceLibrary _current;

    // ── 左栏状态 ────────────────────────────────────────────────────────
    private Vector2 _leftScroll;
    private readonly Dictionary<string, bool> _groupFoldouts = new Dictionary<string, bool>();

    // ── 右栏状态 ────────────────────────────────────────────────────────
    private Vector2 _rightScroll;
    private string _searchId = "";
    private string _chapterFilter = "";
    private readonly Dictionary<int, bool> _foldouts = new Dictionary<int, bool>();
    private int _addEntryChapterIndex = 0;
    private DialogueSentenceLibrary _addEntryChapterInitializedFor;

    // 句子库列表行内重命名——双击进入，跟触发器页面同一套交互(Enter提交/Esc取消/
    // 点别处提交)。
    private DialogueSentenceLibrary _renamingLibrary;
    private string _renameBuffer = "";
    private const string RenameControlName = "DialogueLibraryRenameField";

    private static readonly Color DuplicateWarnColor = new Color(0.85f, 0.35f, 0.30f, 1f);

    // ── 样式（光标/选中强调色：蓝紫色）───────────────────────────────────
    private static readonly Color HeaderBg         = new Color(0.16f, 0.16f, 0.18f, 1f);
    private static readonly Color RowEvenBg        = new Color(0.13f, 0.13f, 0.15f, 1f);
    private static readonly Color RowOddBg         = new Color(0.15f, 0.15f, 0.17f, 1f);
    private static readonly Color AccentColor      = new Color(0.56f, 0.42f, 0.95f, 1f);
    private static readonly Color SelectedFillColor = new Color(0.22f, 0.18f, 0.32f, 1f);

    private const string DefaultFolder = "Assets/_Project/Data/Dialogue";

    public SkyPrisonDialogueScriptPage(SkyPrisonEditorContext context) : base(context) { }

    public override void OnEnable() => ReloadAssets();

    public override void Refresh() => ReloadAssets();

    // ════════════════════════════════════════════════════════════════════
    // 左栏 — 句子库列表
    // ════════════════════════════════════════════════════════════════════
    public override void OnGUILeft()
    {
        if (_settings == null) ReloadAssets();

        _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
        DrawSectionHeader("句子库");

        if (GUILayout.Button("＋ 新建句子库", GUILayout.Height(24f)))
            CreateLibraryAsset();

        GUILayout.Space(4f);

        // 按 chapterGroup 分层显示——一个台本包对应一段章节剧情，分组之后左栏能
        // 顺着章节结构浏览，不是一坨扁平列表混在一起。
        var groups = new Dictionary<string, List<DialogueSentenceLibrary>>();
        var groupOrder = new List<string>();

        // 分组的 key 是章节的固定ID(或"__none__")，不是显示名字——改章节名字不会让
        // 这里的分组散架，显示的时候才临时把ID转换成当前的显示名字。
        const string NoGroupKey = "__none__";

        foreach (var lib in _libraries)
        {
            if (lib == null) continue;
            string groupKey = string.IsNullOrWhiteSpace(lib.chapterGroup) ? NoGroupKey : lib.chapterGroup;
            if (!groups.TryGetValue(groupKey, out var list))
            {
                list = new List<DialogueSentenceLibrary>();
                groups[groupKey] = list;
                groupOrder.Add(groupKey);
            }
            list.Add(lib);
        }

        // 按章节列表本身的顺序排(章节1排在章节2前面)，不是按名字字母序——名字字母序
        // 对"第10章"排在"第2章"前面这种事完全没有意义。
        groupOrder.Sort((a, b) =>
        {
            if (a == NoGroupKey) return 1;
            if (b == NoGroupKey) return -1;
            int ai = _chapterSettings != null ? _chapterSettings.IndexOfId(a) : -1;
            int bi = _chapterSettings != null ? _chapterSettings.IndexOfId(b) : -1;
            if (ai < 0) ai = int.MaxValue;
            if (bi < 0) bi = int.MaxValue;
            return ai.CompareTo(bi);
        });

        foreach (string groupKey in groupOrder)
        {
            string groupLabel = groupKey == NoGroupKey
                ? "未分组"
                : (_chapterSettings != null ? _chapterSettings.GetDisplayName(groupKey) : groupKey);

            if (!_groupFoldouts.ContainsKey(groupKey)) _groupFoldouts[groupKey] = true;
            _groupFoldouts[groupKey] = EditorGUILayout.Foldout(_groupFoldouts[groupKey], groupLabel, true);
            if (!_groupFoldouts[groupKey]) continue;

            foreach (var lib in groups[groupKey])
                DrawLibraryRow(lib, 14f);
        }

        if (_libraries.Count == 0)
            EditorGUILayout.HelpBox("项目里还没有句子库，点上面按钮新建一个。", MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void DrawLibraryRow(DialogueSentenceLibrary lib, float indent)
    {
        bool selected = lib == _current;
        Rect rowRect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));

        if (selected)
        {
            EditorGUI.DrawRect(rowRect, SelectedFillColor);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 3f, rowRect.height), AccentColor);
        }
        else if (rowRect.Contains(Event.current.mousePosition))
        {
            EditorGUI.DrawRect(rowRect, RowOddBg);
        }

        Rect labelRect = new Rect(rowRect.x + 8f + indent, rowRect.y, rowRect.width - 8f - indent, rowRect.height);

        if (_renamingLibrary == lib)
        {
            GUI.SetNextControlName(RenameControlName);
            _renameBuffer = EditorGUI.TextField(labelRect, _renameBuffer);

            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                CommitLibraryRename();
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _renamingLibrary = null;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && !labelRect.Contains(e.mousePosition))
            {
                CommitLibraryRename();
            }

            return;
        }

        GUI.Label(labelRect, lib.name, EditorStyles.label);

        if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
        {
            if (Event.current.button == 1)
            {
                ShowLibraryContextMenu(lib);
                Event.current.Use();
            }
            else if (Event.current.clickCount == 2)
            {
                StartLibraryRename(lib);
                Event.current.Use();
            }
            else
            {
                _current = lib;
                GUI.FocusControl(null);
                Event.current.Use();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // 右栏 — 句子条目编辑
    // ════════════════════════════════════════════════════════════════════
    public override void OnGUIRight()
    {
        EditorGUILayout.BeginVertical("box");
        DrawSectionHeader("台本句子");

        EditorGUILayout.HelpBox("文本里写 {player} 会自动替换成当前玩家角色的名字，不用手打角色名。", MessageType.None);

        EditorGUILayout.BeginHorizontal();
        _searchId = EditorGUILayout.TextField("搜索编号", _searchId, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("×", GUILayout.Width(22f))) _searchId = "";
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        _chapterFilter = EditorGUILayout.TextField("章节筛选", _chapterFilter, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("×", GUILayout.Width(22f))) _chapterFilter = "";
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = _current != null;

        // 编号是"章节-序号"自动生成的正式规则(声优台本那种一句一号、不能重复的体验)，
        // 新增前先选好这句挂在哪个章节下，序号自动接着这个章节已有的最大序号往后排。
        // 章节列表本身是全项目共用的一份(Tools/Sky Prison/章节设置)，不是每个句子库
        // 各自维护。
        bool hasChapters = _chapterSettings != null && _chapterSettings.entries != null && _chapterSettings.entries.Count > 0;
        if (hasChapters)
        {
            // 切换到一个句子库时，"新增句子"默认章节自动带出这个包左栏归的那个章节
            // (不用每次手选)；后续这一轮里手动改了下拉框，就一直沿用手改的值，不会
            // 每帧被重新覆盖回去。
            if (_addEntryChapterInitializedFor != _current)
            {
                int defaultIdx = string.IsNullOrEmpty(_current.chapterGroup)
                    ? 0
                    : Mathf.Max(0, _chapterSettings.IndexOfId(_current.chapterGroup));
                _addEntryChapterIndex = defaultIdx;
                _addEntryChapterInitializedFor = _current;
            }

            _addEntryChapterIndex = Mathf.Clamp(_addEntryChapterIndex, 0, _chapterSettings.entries.Count - 1);
            _addEntryChapterIndex = EditorGUILayout.Popup(_addEntryChapterIndex, BuildChapterDisplayNames(), GUILayout.Width(80f));
        }

        if (GUILayout.Button("＋ 新增句子"))
            AddEntry();
        if (GUILayout.Button("在 Inspector 中打开"))
            Selection.activeObject = _current;
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        if (_current == null)
        {
            EditorGUILayout.HelpBox("请在左侧选择或新建一个句子库。", MessageType.Info);
            return;
        }

        DrawLibraryGroupField();

        if (GUILayout.Button("打开章节设置窗口（新增/命名/排序章节）"))
            ChapterSettingsToolWindow.Open();
        GUILayout.Space(4f);

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

        _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

        bool dirty = false;
        int rowIndex = 0;
        DialogueSentenceLibrary.SentenceEntry toRemove = null;
        HashSet<string> duplicateIds = FindDuplicateIds(_current.entries);

        foreach (var entry in _current.entries)
        {
            if (entry == null) continue;
            if (!string.IsNullOrEmpty(_searchId) &&
                !entry.sentenceId.Contains(_searchId, System.StringComparison.OrdinalIgnoreCase))
                continue;
            string entryChapterName = _chapterSettings != null ? _chapterSettings.GetDisplayName(entry.chapter) : entry.chapter;
            if (!string.IsNullOrEmpty(_chapterFilter) &&
                (string.IsNullOrEmpty(entryChapterName) || entryChapterName.IndexOf(_chapterFilter, System.StringComparison.OrdinalIgnoreCase) < 0))
                continue;

            Rect rowRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(rowRect, rowIndex % 2 == 0 ? RowEvenBg : RowOddBg);
            rowIndex++;

            int foldoutKey = entry.GetHashCode();
            if (!_foldouts.ContainsKey(foldoutKey)) _foldouts[foldoutKey] = false;

            bool isDuplicate = !string.IsNullOrEmpty(entry.sentenceId) && duplicateIds.Contains(entry.sentenceId);

            EditorGUILayout.BeginHorizontal();
            string headerLabel = string.IsNullOrEmpty(entry.sentenceId) ? "(未命名)" : entry.sentenceId;
            if (!string.IsNullOrEmpty(entryChapterName)) headerLabel += $"  [{entryChapterName}]";
            if (isDuplicate) headerLabel += "  ⚠编号重复";

            if (isDuplicate)
            {
                GUIStyle warnFoldoutStyle = new GUIStyle(EditorStyles.foldoutHeader);
                warnFoldoutStyle.normal.textColor = DuplicateWarnColor;
                warnFoldoutStyle.onNormal.textColor = DuplicateWarnColor;
                warnFoldoutStyle.focused.textColor = DuplicateWarnColor;
                _foldouts[foldoutKey] = EditorGUILayout.Foldout(_foldouts[foldoutKey], headerLabel, true, warnFoldoutStyle);
            }
            else
            {
                _foldouts[foldoutKey] = EditorGUILayout.Foldout(_foldouts[foldoutKey], headerLabel, true, EditorStyles.foldoutHeader);
            }

            if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(48f)))
                toRemove = entry;
            EditorGUILayout.EndHorizontal();

            // 收起时也把主语言文本露出来——写台本的人不用逐条展开就能顺着读完这一页
            // 的剧情走向。
            if (!_foldouts[foldoutKey])
            {
                string mainText = FindEntryText(entry, GetDefaultLanguageCode());
                if (!string.IsNullOrEmpty(mainText))
                {
                    string speakerName = ResolveSpeakerPreviewName(entry);
                    string preview = string.IsNullOrEmpty(speakerName) ? mainText : $"{speakerName}：{mainText}";

                    GUIStyle previewStyle = new GUIStyle(EditorStyles.label)
                    {
                        wordWrap = false,
                        normal = { textColor = new Color(0.72f, 0.72f, 0.72f) }
                    };
                    Rect previewRect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                    previewRect.x += 24f;
                    previewRect.width -= 24f;
                    GUI.Label(previewRect, preview, previewStyle);
                }
            }

            if (_foldouts[foldoutKey])
            {
                EditorGUI.indentLevel++;

                if (hasChapters)
                {
                    // 正式编号规则："章节-序号"自动生成，不允许手动改编号——
                    // 像声优拿到的台本一样，编号是固定的引用坐标，改编号只能通过
                    // 换章节(下面的下拉)来彻底重新生成，不能手改成任意字符串。
                    EditorGUILayout.LabelField("编号", entry.sentenceId);

                    int chapterIdx = _chapterSettings.IndexOfId(entry.chapter);
                    int newChapterIdx = EditorGUILayout.Popup("章节", chapterIdx, BuildChapterDisplayNames());
                    if (newChapterIdx >= 0 && newChapterIdx != chapterIdx)
                    {
                        string newChapterId = _chapterSettings.entries[newChapterIdx].id;
                        entry.chapter = newChapterId;
                        entry.sentenceId = GenerateNextSentenceId(newChapterId, entry);
                        dirty = true;
                    }
                }
                else
                {
                    string newId = EditorGUILayout.TextField("编号", entry.sentenceId);
                    if (newId != entry.sentenceId) { entry.sentenceId = newId; dirty = true; }

                    string newChapter = EditorGUILayout.TextField("章节", entry.chapter);
                    if (newChapter != entry.chapter) { entry.chapter = newChapter; dirty = true; }
                }

                string newNote = EditorGUILayout.TextField("备注", entry.note);
                if (newNote != entry.note) { entry.note = newNote; dirty = true; }

                // 说话人从单位列表里选，不手打名字——手打的名字跟单位定义脱节，
                // 换角色/改名都对不上；"匿名"用于还没在剧情里揭示身份的角色。
                EditorGUILayout.BeginHorizontal();
                bool newAnonymous = EditorGUILayout.ToggleLeft("匿名(???)", entry.speakerAnonymous, GUILayout.Width(100f));
                if (newAnonymous != entry.speakerAnonymous) { entry.speakerAnonymous = newAnonymous; dirty = true; }

                GUI.enabled = !entry.speakerAnonymous;
                GUILayout.Label("说话人", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
                string speakerLabel = entry.speakerUnit != null ? UnitPickerPopupContent.GetLabel(entry.speakerUnit) : "(未选择)";
                Rect speakerBtnRect = GUILayoutUtility.GetRect(new GUIContent(speakerLabel), EditorStyles.objectField, GUILayout.ExpandWidth(true));
                if (GUI.Button(speakerBtnRect, speakerLabel, EditorStyles.objectField))
                {
                    var capturedEntry = entry;
                    var capturedLibrary = _current;
                    PopupWindow.Show(speakerBtnRect, new UnitPickerPopupContent(picked =>
                    {
                        capturedEntry.speakerUnit = picked;
                        EditorUtility.SetDirty(capturedLibrary);
                        AssetDatabase.SaveAssets();
                    }));
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                // 化名/绰号——留空则用 speakerUnit 的真名，填了就按语言显示这句专属的化名
                // (比如揭示真名前"蓝头发的人"，之后的句子留空回真名)。
                if (!entry.speakerAnonymous)
                {
                    EnsureLanguageSlots(entry.speakerAliasOverride, langs);
                    foreach (var lang in langs)
                    {
                        foreach (var t in entry.speakerAliasOverride)
                        {
                            if (t.languageCode != lang.languageCode) continue;

                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Label($"化名（{lang.displayName}）", EditorStyles.miniLabel, GUILayout.Width(90f));
                            string newAlias = EditorGUILayout.TextField(t.text);
                            if (newAlias != t.text) { t.text = newAlias; dirty = true; }
                            EditorGUILayout.EndHorizontal();
                            break;
                        }
                    }
                }

                EnsureEntryHasAllLanguages(entry, langs);
                EnsureEntryHasAllVoiceSlots(entry, langs);

                foreach (var lang in langs)
                {
                    string label = lang.isDefault ? $"{lang.displayName} ★" : lang.displayName;
                    foreach (var t in entry.texts)
                    {
                        if (t.languageCode != lang.languageCode) continue;

                        EditorGUILayout.BeginHorizontal();
                        GUIStyle labelStyle = new GUIStyle(EditorStyles.label);
                        if (lang.isDefault) labelStyle.normal.textColor = AccentColor;
                        GUILayout.Label(label, labelStyle, GUILayout.Width(90f));
                        string newText = EditorGUILayout.TextArea(t.text, GUILayout.Height(40f));
                        if (newText != t.text) { t.text = newText; dirty = true; }
                        EditorGUILayout.EndHorizontal();
                        break;
                    }

                    foreach (var v in entry.voices)
                    {
                        if (v.languageCode != lang.languageCode) continue;

                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label(label + " 语音", EditorStyles.miniLabel, GUILayout.Width(90f));
                        AudioClip newClip = (AudioClip)EditorGUILayout.ObjectField(v.clip, typeof(AudioClip), false);
                        if (newClip != v.clip) { v.clip = newClip; dirty = true; }
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                }

                EditorGUI.indentLevel--;
                GUILayout.Space(2f);
            }

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();

        if (toRemove != null)
        {
            _current.entries.Remove(toRemove);
            dirty = true;
        }

        if (dirty)
        {
            EditorUtility.SetDirty(_current);
            AssetDatabase.SaveAssets();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // 工具方法
    // ════════════════════════════════════════════════════════════════════

    private string GetDefaultLanguageCode()
    {
        if (_settings == null) return null;
        var def = _settings.GetDefaultLanguage();
        return def != null ? def.languageCode : null;
    }

    private string ResolveSpeakerPreviewName(DialogueSentenceLibrary.SentenceEntry entry)
    {
        if (entry.speakerAnonymous) return "???";

        string code = GetDefaultLanguageCode();

        string alias = FindEntryText(entry.speakerAliasOverride, code);
        if (!string.IsNullOrEmpty(alias)) return alias;

        if (entry.speakerUnit == null) return null;

        string localized = FindLocalizedUnitName(entry.speakerUnit, code);
        if (!string.IsNullOrEmpty(localized)) return localized;
        return entry.speakerUnit.displayName;
    }

    private static void EnsureLanguageSlots(List<LocalizedTextEntry> texts,
        List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        foreach (var lang in langs)
        {
            bool found = false;
            foreach (var t in texts)
                if (t.languageCode == lang.languageCode) { found = true; break; }
            if (!found)
                texts.Add(new LocalizedTextEntry { languageCode = lang.languageCode, text = "" });
        }
    }

    private static string FindLocalizedUnitName(UnitDefinition unit, string languageCode)
    {
        if (unit == null || unit.localizedNames == null || string.IsNullOrEmpty(languageCode)) return null;
        foreach (var t in unit.localizedNames)
            if (t != null && t.languageCode == languageCode) return t.text;
        return null;
    }

    private static string FindEntryText(DialogueSentenceLibrary.SentenceEntry entry, string languageCode)
        => FindEntryText(entry?.texts, languageCode);

    private static string FindEntryText(List<LocalizedTextEntry> texts, string languageCode)
    {
        if (texts == null || string.IsNullOrEmpty(languageCode)) return null;
        foreach (var t in texts)
            if (t != null && t.languageCode == languageCode) return t.text;
        return null;
    }

    private void StartLibraryRename(DialogueSentenceLibrary lib)
    {
        _renamingLibrary = lib;
        _renameBuffer = lib.name;
        EditorGUIUtility.editingTextField = true;
        EditorApplication.delayCall += () => EditorGUI.FocusTextInControl(RenameControlName);
    }

    private void CommitLibraryRename()
    {
        if (_renamingLibrary == null) return;

        string newName = _renameBuffer.Trim();
        if (!string.IsNullOrEmpty(newName) && newName != _renamingLibrary.name)
        {
            string path = AssetDatabase.GetAssetPath(_renamingLibrary);
            AssetDatabase.RenameAsset(path, newName);
            AssetDatabase.SaveAssets();
        }

        _renamingLibrary = null;
    }

    private void ShowLibraryContextMenu(DialogueSentenceLibrary lib)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("重命名"), false, () => StartLibraryRename(lib));
        menu.AddItem(new GUIContent("复制"), false, () => DuplicateLibrary(lib));
        menu.AddItem(new GUIContent("在 Inspector 中打开"), false, () => Selection.activeObject = lib);
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("删除"), false, () => DeleteLibrary(lib));
        menu.ShowAsContext();
    }

    private void DuplicateLibrary(DialogueSentenceLibrary lib)
    {
        string path = AssetDatabase.GetAssetPath(lib);
        string newPath = AssetDatabase.GenerateUniqueAssetPath(path);
        if (!AssetDatabase.CopyAsset(path, newPath)) return;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var copy = AssetDatabase.LoadAssetAtPath<DialogueSentenceLibrary>(newPath);
        ReloadAssets();
        if (copy != null) _current = copy;
    }

    private void DeleteLibrary(DialogueSentenceLibrary lib)
    {
        if (!EditorUtility.DisplayDialog("删除句子库", $"确定要删除「{lib.name}」吗？这个操作不可撤销。", "删除", "取消"))
            return;

        string path = AssetDatabase.GetAssetPath(lib);
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();

        if (_current == lib) _current = null;
        if (_renamingLibrary == lib) _renamingLibrary = null;

        ReloadAssets();
    }

    private void DrawLibraryGroupField()
    {
        if (_current == null) return;

        EditorGUILayout.BeginHorizontal();

        bool hasChapters = _chapterSettings != null && _chapterSettings.entries != null && _chapterSettings.entries.Count > 0;
        if (hasChapters)
        {
            // 左栏分组直接用全局章节列表当分类——台本包归到哪个章节下，就是左栏的
            // 大分类，存的是章节的固定ID，不是自由填的字符串，改章节名字不会散架。
            string[] displayNames = BuildChapterDisplayNames();
            string[] options = new string[displayNames.Length + 1];
            options[0] = "(未分组)";
            System.Array.Copy(displayNames, 0, options, 1, displayNames.Length);

            int currentIdx = string.IsNullOrEmpty(_current.chapterGroup) ? 0 : _chapterSettings.IndexOfId(_current.chapterGroup) + 1;
            if (currentIdx < 0) currentIdx = 0;

            int newIdx = EditorGUILayout.Popup("左栏分组", currentIdx, options);
            if (newIdx != currentIdx)
            {
                _current.chapterGroup = newIdx == 0 ? "" : _chapterSettings.entries[newIdx - 1].id;
                EditorUtility.SetDirty(_current);
                AssetDatabase.SaveAssets();
            }
        }
        else
        {
            string newGroup = EditorGUILayout.TextField("左栏分组", _current.chapterGroup);
            if (newGroup != _current.chapterGroup)
            {
                _current.chapterGroup = newGroup;
                EditorUtility.SetDirty(_current);
                AssetDatabase.SaveAssets();
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void AddEntry()
    {
        if (_current == null) return;

        var entry = new DialogueSentenceLibrary.SentenceEntry();

        if (_chapterSettings != null && _chapterSettings.entries != null && _chapterSettings.entries.Count > 0)
        {
            int idx = Mathf.Clamp(_addEntryChapterIndex, 0, _chapterSettings.entries.Count - 1);
            string chapterId = _chapterSettings.entries[idx].id;
            entry.chapter = chapterId;
            entry.sentenceId = GenerateNextSentenceId(chapterId, null);
        }

        _current.entries.Add(entry);
        EditorUtility.SetDirty(_current);
        AssetDatabase.SaveAssets();
    }

    /// <summary>"章节-序号"正式编号规则：在指定章节下找当前已用到的最大序号，往后接一个。
    /// excludeEntry 传当前正在改章节的条目本身，避免它自己旧编号(马上要被替换掉)干扰
    /// "已用到的最大序号"的统计。
    ///
    /// 序号统计按章节的固定ID匹配(entry.chapter==chapterId)，不按编号文本里的章节前缀
    /// 字符串匹配——章节改名后，老句子的编号前缀还停留在改名前的旧文字(编号本来就是
    /// 生成那一刻的快照，不会跟着改名回溯更新)，如果按前缀文本匹配就会找不到同章节的
    /// 老句子，序号从1重新开始，跟老编号撞车。按ID匹配就没有这个问题。</summary>
    private string GenerateNextSentenceId(string chapterId, DialogueSentenceLibrary.SentenceEntry excludeEntry)
    {
        string chapterName = _chapterSettings != null ? _chapterSettings.GetDisplayName(chapterId) : chapterId;
        int maxSeq = 0;

        foreach (var e in _current.entries)
        {
            if (e == null || e == excludeEntry || string.IsNullOrEmpty(e.sentenceId)) continue;
            if (e.chapter != chapterId) continue;

            int lastDash = e.sentenceId.LastIndexOf('-');
            if (lastDash < 0 || lastDash == e.sentenceId.Length - 1) continue;

            string suffix = e.sentenceId.Substring(lastDash + 1);
            if (int.TryParse(suffix, out int seq) && seq > maxSeq)
                maxSeq = seq;
        }

        return $"{chapterName}-{(maxSeq + 1):000}";
    }

    private string[] BuildChapterDisplayNames()
    {
        if (_chapterSettings == null || _chapterSettings.entries == null) return System.Array.Empty<string>();

        var result = new string[_chapterSettings.entries.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = _chapterSettings.entries[i] != null ? _chapterSettings.entries[i].displayName : "";
        return result;
    }

    private static HashSet<string> FindDuplicateIds(List<DialogueSentenceLibrary.SentenceEntry> entries)
    {
        var seen = new HashSet<string>();
        var duplicates = new HashSet<string>();

        foreach (var e in entries)
        {
            if (e == null || string.IsNullOrEmpty(e.sentenceId)) continue;
            if (!seen.Add(e.sentenceId))
                duplicates.Add(e.sentenceId);
        }

        return duplicates;
    }

    private void CreateLibraryAsset()
    {
        EnsureFolder(DefaultFolder);

        string path = AssetDatabase.GenerateUniqueAssetPath(DefaultFolder + "/DialogueSentenceLibrary.asset");
        var lib = ScriptableObject.CreateInstance<DialogueSentenceLibrary>();
        AssetDatabase.CreateAsset(lib, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        _libraries.Add(lib);
        _current = lib;
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
        _chapterSettings = DialogueChapterSettingsUtility.GetOrCreateSettings();

        _libraries.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:DialogueSentenceLibrary"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var lib = AssetDatabase.LoadAssetAtPath<DialogueSentenceLibrary>(path);
            if (lib != null) _libraries.Add(lib);
        }

        if (_current == null && _libraries.Count > 0)
            _current = _libraries[0];
    }

    private void EnsureEntryHasAllLanguages(DialogueSentenceLibrary.SentenceEntry entry,
        List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        foreach (var lang in langs)
        {
            bool found = false;
            foreach (var t in entry.texts)
                if (t.languageCode == lang.languageCode) { found = true; break; }
            if (!found)
                entry.texts.Add(new LocalizedTextEntry { languageCode = lang.languageCode, text = "" });
        }
    }

    private void EnsureEntryHasAllVoiceSlots(DialogueSentenceLibrary.SentenceEntry entry,
        List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        foreach (var lang in langs)
        {
            bool found = false;
            foreach (var v in entry.voices)
                if (v.languageCode == lang.languageCode) { found = true; break; }
            if (!found)
                entry.voices.Add(new LocalizedVoiceEntry { languageCode = lang.languageCode, clip = null });
        }
    }

    private List<LocalizationProjectSettings.LanguageEntry> GetEnabledLanguages()
    {
        var result = new List<LocalizationProjectSettings.LanguageEntry>();
        if (_settings == null) return result;

        foreach (var l in _settings.languages)
            if (l != null && l.enabled && l.isDefault) result.Add(l);
        foreach (var l in _settings.languages)
            if (l != null && l.enabled && !l.isDefault) result.Add(l);

        return result;
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
