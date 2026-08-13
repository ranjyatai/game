#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 天空囚笼编辑器 — 交互包编辑器页面。编辑 NPCDialogueDefinition："NPC怎么交互"的
/// 编排数据——名字、句子库引用、按条件分支的对话变体(条件+开场白+选项)。
///
/// 条件编辑复用触发器同一套模板选择器(AILogicSentenceTemplatePickerWindow +
/// AISlotValuePickerWindow)，不是另起一套——跟 NPCDialogueDefinition 本身"条件语言
/// 跟触发器共用"的设计一致。
/// </summary>
public class SkyPrisonInteractionPackagePage : SkyPrisonEditorPageBase
{
    public override string TabName => "交互包编辑器";

    private LocalizationProjectSettings _settings;
    private readonly List<NPCDialogueDefinition> _packages = new List<NPCDialogueDefinition>();
    private NPCDialogueDefinition _current;

    private Vector2 _leftScroll;
    private Vector2 _rightScroll;
    private Vector2 _detailsScroll;

    private NPCDialogueDefinition _renamingPackage;
    private string _renameBuffer = "";
    private const string RenameControlName = "InteractionPackageRenameField";

    private readonly Dictionary<int, bool> _variantFoldouts = new Dictionary<int, bool>();

    // 树形画布选中态——每次只有一个节点(开场白或某个选项)在展开详情，
    // 点哪个节点，下面的详情区就编辑哪个。
    private NPCDialogueDefinition.DialogueVariant _selectedVariant;
    private DialogueOption _selectedOption;

    // 多层选项下钻路径——每个变体各自一份，不能用单一共享字段：一个变体展开
    // 就会调一次 DrawVariantTree，如果同时展开了多个变体，单一共享字段会在
    // 同一次OnGUI里被"处理下一个变体"的那次调用误判成"变体切换了"而清空，
    // 导致刚下钻进去的路径立刻在同一帧末尾被冲掉——这正是"选项点不了/详情
    // 面板不刷新"的真正原因，跟事件处理本身无关。
    private readonly Dictionary<NPCDialogueDefinition.DialogueVariant, List<DialogueOption>> _drillPaths =
        new Dictionary<NPCDialogueDefinition.DialogueVariant, List<DialogueOption>>();

    private List<DialogueOption> GetDrillPath(NPCDialogueDefinition.DialogueVariant variant)
    {
        if (variant == null) return new List<DialogueOption>();
        if (!_drillPaths.TryGetValue(variant, out var path))
        {
            path = new List<DialogueOption>();
            _drillPaths[variant] = path;
        }
        return path;
    }

    // 选项框拖拽排序——按住拖动横向移动位置，"添加选项"占位框永远留在最后，
    // 不参与排序、也不会被拖到它后面去。
    private DialogueOption _dragOption;
    private NPCDialogueDefinition.DialogueVariant _dragVariant;
    private Vector2 _dragStartPos;
    private bool _dragActive;
    private int _dragInsertIndex = -1;

    // 固定的哨兵值，标记"树形拖拽系统正占用输入"——不走 GUIUtility.GetControlID
    // (那个要求每帧调用次数/顺序完全一致，跟这里手动摆Rect的画法对不上)，用一个
    // 不会跟真实控件ID撞上的固定数字就够。之前没有正确抢占 hotControl，导致
    // MouseUp 有时候被别的控件"截胡"，选项框点了没反应、拖拽也拖不动。
    private const int TreeDragHotControlId = 918273645;

    private static readonly Color HeaderBg          = new Color(0.16f, 0.16f, 0.18f, 1f);
    private static readonly Color RowEvenBg         = new Color(0.13f, 0.13f, 0.15f, 1f);
    private static readonly Color RowOddBg          = new Color(0.15f, 0.15f, 0.17f, 1f);
    private static readonly Color AccentColor       = new Color(0.40f, 0.70f, 0.95f, 1f);
    private static readonly Color SelectedFillColor = new Color(0.14f, 0.22f, 0.30f, 1f);

    private const string DefaultFolder = "Assets/_Project/Data/Dialogue";

    public SkyPrisonInteractionPackagePage(SkyPrisonEditorContext context) : base(context) { }

    public override void OnEnable() => ReloadAssets();

    public override void Refresh() => ReloadAssets();

    // ════════════════════════════════════════════════════════════════════
    // 左栏 — 交互包列表
    // ════════════════════════════════════════════════════════════════════
    public override void OnGUILeft()
    {
        if (_settings == null) ReloadAssets();

        _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
        DrawSectionHeader("交互包");

        if (GUILayout.Button("＋ 新建交互包", GUILayout.Height(24f)))
            CreatePackageAsset();

        GUILayout.Space(4f);

        foreach (var pkg in _packages)
            DrawPackageRow(pkg);

        if (_packages.Count == 0)
            EditorGUILayout.HelpBox("项目里还没有交互包，点上面按钮新建一个。", MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    private void DrawPackageRow(NPCDialogueDefinition pkg)
    {
        if (pkg == null) return;

        bool selected = pkg == _current;
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

        Rect labelRect = new Rect(rowRect.x + 8f, rowRect.y, rowRect.width - 8f, rowRect.height);

        if (_renamingPackage == pkg)
        {
            GUI.SetNextControlName(RenameControlName);
            _renameBuffer = EditorGUI.TextField(labelRect, _renameBuffer);

            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                CommitPackageRename();
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _renamingPackage = null;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && !labelRect.Contains(e.mousePosition))
            {
                CommitPackageRename();
            }
            return;
        }

        GUI.Label(labelRect, pkg.name, EditorStyles.label);

        if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
        {
            if (Event.current.button == 1)
            {
                ShowPackageContextMenu(pkg);
                Event.current.Use();
            }
            else if (Event.current.clickCount == 2)
            {
                StartPackageRename(pkg);
                Event.current.Use();
            }
            else
            {
                _current = pkg;
                GUI.FocusControl(null);
                Event.current.Use();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // 右栏 — 交互包内容编辑
    // ════════════════════════════════════════════════════════════════════
    public override void OnGUIRight()
    {
        if (_current == null)
        {
            EditorGUILayout.HelpBox("请在左侧选择或新建一个交互包。", MessageType.Info);
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

        // 上半：句子库/名字/变体树列表，自己单独滚动，固定高度不会被下面详情区顶跑。
        _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.Height(420f));

        EditorGUILayout.BeginVertical("box");
        DrawSectionHeader("基本信息");

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("句子库", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
        string libLabel = _current.sentenceLibrary != null ? _current.sentenceLibrary.name : "（未选择）";
        if (GUILayout.Button(libLabel, EditorStyles.objectField))
        {
            SkyPrisonItemPickerPopup.Open(
                _current.sentenceLibrary,
                picked =>
                {
                    _current.sentenceLibrary = picked as DialogueSentenceLibrary;
                    EditorUtility.SetDirty(_current);
                    AssetDatabase.SaveAssets();
                },
                nameof(DialogueSentenceLibrary));
        }
        EditorGUILayout.EndHorizontal();

        EnsureLanguageSlots(_current.npcName, langs);
        foreach (var lang in langs)
        {
            foreach (var t in _current.npcName)
            {
                if (t.languageCode != lang.languageCode) continue;

                EditorGUILayout.BeginHorizontal();
                string label = lang.isDefault ? $"名字（{lang.displayName}）★" : $"名字（{lang.displayName}）";
                GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
                string newText = EditorGUILayout.TextField(t.text);
                if (newText != t.text) { t.text = newText; dirty = true; }
                EditorGUILayout.EndHorizontal();
                break;
            }
        }
        EditorGUILayout.EndVertical();

        GUILayout.Space(6f);

        EditorGUILayout.BeginVertical("box");
        DrawSectionHeader("待机闲聊（玩家停在选项界面太久没操作，随机念一句；留空=不启用）");
        dirty |= DrawIdleChatter();
        EditorGUILayout.EndVertical();

        GUILayout.Space(6f);

        EditorGUILayout.BeginVertical("box");
        DrawSectionHeader("对话变体（按顺序判定，放一个无条件变体在最后当默认兜底）");

        if (GUILayout.Button("＋ 新增变体"))
        {
            _current.variants.Add(new NPCDialogueDefinition.DialogueVariant());
            dirty = true;
        }

        NPCDialogueDefinition.DialogueVariant variantToRemove = null;
        for (int vi = 0; vi < _current.variants.Count; vi++)
        {
            var variant = _current.variants[vi];
            if (variant == null) continue;

            int variantKey = variant.GetHashCode();
            if (!_variantFoldouts.ContainsKey(variantKey)) _variantFoldouts[variantKey] = true;

            Rect rowRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(rowRect, vi % 2 == 0 ? RowEvenBg : RowOddBg);

            EditorGUILayout.BeginHorizontal();
            string header = $"变体 {vi + 1}" + (variant.conditions.Count == 0 ? "（无条件/默认兜底）" : $"（{variant.conditions.Count}个条件）");
            _variantFoldouts[variantKey] = EditorGUILayout.Foldout(_variantFoldouts[variantKey], header, true, EditorStyles.foldoutHeader);
            if (GUILayout.Button("删除变体", EditorStyles.miniButton, GUILayout.Width(64f)))
                variantToRemove = variant;
            EditorGUILayout.EndHorizontal();

            if (_variantFoldouts[variantKey])
            {
                EditorGUI.indentLevel++;
                dirty |= DrawVariantConditions(variant);
                GUILayout.Space(6f);
                dirty |= DrawVariantTree(variant);
                EditorGUI.indentLevel--;
                GUILayout.Space(4f);
            }

            EditorGUILayout.EndVertical();
        }

        if (variantToRemove != null)
        {
            _current.variants.Remove(variantToRemove);
            if (_selectedVariant == variantToRemove) { _selectedVariant = null; _selectedOption = null; }
            _drillPaths.Remove(variantToRemove);
            dirty = true;
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();

        // 用刚画完的这块区域实际落在窗口里的Y坐标来算剩余高度，不是拍脑袋减一个
        // 常数——之前"windowHeight减560"这种估算法很容易还是卡在最小值附近，
        // 跟没改一样。GetLastRect()给的是这一路画下来、经过滚动区域折叠后的
        // 真实占用坐标，用它反推剩余空间准得多。
        Rect topAreaRect = GUILayoutUtility.GetLastRect();

        // 下半：选中节点的详情——固定在树列表下方，不随树列表滚动跑掉，
        // 切换选中的节点/变体也不会挪位置，方便持续对照着改。
        dirty |= DrawSelectedNodeDetailsPanel(langs, topAreaRect);

        if (dirty)
        {
            EditorUtility.SetDirty(_current);
            AssetDatabase.SaveAssets();
        }
    }

    private bool DrawSelectedNodeDetailsPanel(List<LocalizationProjectSettings.LanguageEntry> langs, Rect topAreaRect)
    {
        bool dirty = false;

        GUILayout.Space(6f);

        // 用上面那块区域实际到达的Y坐标反推剩余高度——这个坐标是相对页面内容区域
        // (BeginArea内)的本地坐标，跟窗口整体高度加一个"页面上方chrome占用"的常量
        // (工具栏+Tab+抽屉按钮等，大致120px)换算，比直接对窗口高度做盲目减法准。
        const float ChromeAboveContentArea = 120f;
        float windowHeight = Context.Window != null ? Context.Window.position.height : 700f;
        float availableBelowTopArea = windowHeight - ChromeAboveContentArea - topAreaRect.yMax;
        float detailsHeight = Mathf.Max(260f, availableBelowTopArea - 12f);

        EditorGUILayout.BeginVertical("box", GUILayout.Height(detailsHeight));
        DrawSectionHeader("节点详情");

        bool variantStillExists = _selectedVariant != null && _current.variants.Contains(_selectedVariant);
        if (!variantStillExists)
        {
            EditorGUILayout.HelpBox("在上面的树里点一个节点（开场白或选项），这里显示它的详细设置。", MessageType.Info);
            EditorGUILayout.EndVertical();
            return dirty;
        }

        _detailsScroll = EditorGUILayout.BeginScrollView(_detailsScroll);

        if (_selectedOption == null)
        {
            EditorGUILayout.LabelField("开场白详情", EditorStyles.miniBoldLabel);
            dirty |= DrawVariantGreeting(_selectedVariant, langs);
        }
        else if (ContainsOptionRecursive(_selectedVariant.options, _selectedOption))
        {
            EditorGUILayout.LabelField("选项详情", EditorStyles.miniBoldLabel);
            dirty |= DrawOptionDetails(_selectedOption, langs);
        }
        else
        {
            _selectedOption = null;
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        return dirty;
    }

    // ── 条件 ────────────────────────────────────────────────────────────────

    private bool DrawVariantConditions(NPCDialogueDefinition.DialogueVariant variant)
    {
        EditorGUILayout.LabelField("变体条件（全部成立才选中这个变体）", EditorStyles.miniBoldLabel);
        return DrawConditionList(variant.conditions);
    }

    /// <summary>通用条件列表编辑——变体条件、选项显示条件共用这一个，都是同一种
    /// List&lt;LogicSentenceInstance&gt;，编辑方式没有理由分两套。</summary>
    private bool DrawConditionList(List<LogicSentenceInstance> conditions)
    {
        bool dirty = false;

        LogicSentenceInstance toRemove = null;
        for (int i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(DescribeCondition(condition));

            var captured = condition;
            if (GUILayout.Button("编辑…", GUILayout.Width(60f)))
            {
                AILogicSentenceTemplatePickerWindow.OpenForEdit(
                    LogicSentenceCategory.Condition, LogicTemplateContext.Trigger, captured,
                    edited =>
                    {
                        int idx = conditions.IndexOf(captured);
                        if (idx >= 0) conditions[idx] = edited;
                        EditorUtility.SetDirty(_current);
                        AssetDatabase.SaveAssets();
                    });
            }
            if (GUILayout.Button("删除", GUILayout.Width(50f)))
                toRemove = condition;
            EditorGUILayout.EndHorizontal();
        }

        if (toRemove != null)
        {
            conditions.Remove(toRemove);
            dirty = true;
        }

        if (GUILayout.Button("＋ 添加条件"))
        {
            AILogicSentenceTemplatePickerWindow.Open(
                LogicSentenceCategory.Condition, LogicTemplateContext.Trigger,
                created =>
                {
                    conditions.Add(created);
                    EditorUtility.SetDirty(_current);
                    AssetDatabase.SaveAssets();
                });
        }

        return dirty;
    }

    /// <summary>选中的节点可能嵌套在任意深度的子选项里，不能只查顶层 options——
    /// 之前只查顶层，导致选中一个子层选项后，下一次绘制详情面板时判定"找不到"
    /// 又把 _selectedOption 清空，看起来像点了没反应。</summary>
    private static bool ContainsOptionRecursive(List<DialogueOption> options, DialogueOption target)
    {
        if (options == null || target == null) return false;
        foreach (var o in options)
        {
            if (o == target) return true;
            if (ContainsOptionRecursive(o.subOptions, target)) return true;
        }
        return false;
    }

    private static string DescribeCondition(LogicSentenceInstance condition)
    {
        if (condition == null || string.IsNullOrEmpty(condition.templateId)) return "<未设置>";
        var template = AILogicSentenceTemplateLibrary.GetTemplateById(condition.templateId);
        return template != null ? template.displayName : condition.templateId;
    }

    // ── 开场白 ──────────────────────────────────────────────────────────────

    private bool DrawVariantGreeting(NPCDialogueDefinition.DialogueVariant variant, List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        bool dirty = false;
        DialogueSentenceLibrary lib = _current.sentenceLibrary;

        EditorGUILayout.LabelField("开场白（配多句=随机抽一句开始对话；配一句=固定）", EditorStyles.miniBoldLabel);

        if (lib != null && lib.entries.Count > 0)
        {
            int removeIdx = -1;
            for (int gi = 0; gi < variant.greetingSentenceIds.Count; gi++)
            {
                string currentId = variant.greetingSentenceIds[gi];
                var currentEntry = lib.entries.Find(e => e != null && e.sentenceId == currentId);
                string btnLabel = currentEntry != null ? BuildSentenceOptionLabel(currentEntry) : "（未选择）";

                EditorGUILayout.BeginHorizontal();
                Rect btnRect = GUILayoutUtility.GetRect(new GUIContent(btnLabel), EditorStyles.objectField, GUILayout.ExpandWidth(true));
                if (GUI.Button(btnRect, btnLabel, EditorStyles.objectField))
                {
                    int capturedIndex = gi;
                    PopupWindow.Show(btnRect, new SentencePickerPopupContent(lib.entries, BuildSentenceOptionLabel, picked =>
                    {
                        variant.greetingSentenceIds[capturedIndex] = picked;
                        EditorUtility.SetDirty(_current);
                        AssetDatabase.SaveAssets();
                    }));
                }
                if (GUILayout.Button("×", GUILayout.Width(24f)))
                    removeIdx = gi;
                EditorGUILayout.EndHorizontal();
            }

            if (removeIdx >= 0)
            {
                variant.greetingSentenceIds.RemoveAt(removeIdx);
                dirty = true;
            }

            if (GUILayout.Button("＋ 添加一句开场白"))
            {
                variant.greetingSentenceIds.Add(lib.entries[0].sentenceId);
                dirty = true;
            }
        }
        else
        {
            EditorGUILayout.HelpBox("上面先选一个句子库，才能挑开场白句子。", MessageType.None);
        }

        return dirty;
    }

    // ── 待机闲聊：跟开场白同一套"句子挑选列表"写法，只是挂在交互包顶层，
    // 不属于任何一个变体——不管当前激活的是哪个变体，闲聊池都是同一份。────────

    private bool DrawIdleChatter()
    {
        bool dirty = false;
        DialogueSentenceLibrary lib = _current.sentenceLibrary;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("停留多久触发(秒)", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
        float newDelay = EditorGUILayout.FloatField(_current.idleChatterDelaySeconds);
        if (!Mathf.Approximately(newDelay, _current.idleChatterDelaySeconds))
        {
            _current.idleChatterDelaySeconds = Mathf.Max(1f, newDelay);
            dirty = true;
        }
        EditorGUILayout.EndHorizontal();

        if (lib != null && lib.entries.Count > 0)
        {
            int removeIdx = -1;
            for (int ii = 0; ii < _current.idleChatterSentenceIds.Count; ii++)
            {
                string currentId = _current.idleChatterSentenceIds[ii];
                var currentEntry = lib.entries.Find(e => e != null && e.sentenceId == currentId);
                string btnLabel = currentEntry != null ? BuildSentenceOptionLabel(currentEntry) : "（未选择）";

                EditorGUILayout.BeginHorizontal();
                Rect btnRect = GUILayoutUtility.GetRect(new GUIContent(btnLabel), EditorStyles.objectField, GUILayout.ExpandWidth(true));
                if (GUI.Button(btnRect, btnLabel, EditorStyles.objectField))
                {
                    int capturedIndex = ii;
                    PopupWindow.Show(btnRect, new SentencePickerPopupContent(lib.entries, BuildSentenceOptionLabel, picked =>
                    {
                        _current.idleChatterSentenceIds[capturedIndex] = picked;
                        EditorUtility.SetDirty(_current);
                        AssetDatabase.SaveAssets();
                    }));
                }
                if (GUILayout.Button("×", GUILayout.Width(24f)))
                    removeIdx = ii;
                EditorGUILayout.EndHorizontal();
            }

            if (removeIdx >= 0)
            {
                _current.idleChatterSentenceIds.RemoveAt(removeIdx);
                dirty = true;
            }

            if (GUILayout.Button("＋ 添加一句闲聊"))
            {
                _current.idleChatterSentenceIds.Add(lib.entries[0].sentenceId);
                dirty = true;
            }
        }
        else
        {
            EditorGUILayout.HelpBox("上面先选一个句子库，才能挑闲聊句子。", MessageType.None);
        }

        return dirty;
    }

    // ── 树形画布：开场白在顶端，选项作为下方并列分支，连线表示归属关系 ─────────

    private const float TreeBoxWidth = 168f;
    private const float TreeBoxHeight = 56f;
    private const float TreeHGap = 14f;
    private const float TreeVGap = 40f;

    private bool DrawVariantTree(NPCDialogueDefinition.DialogueVariant variant)
    {
        bool dirty = false;

        List<DialogueOption> drillPath = GetDrillPath(variant);

        List<DialogueOption> currentOptions = drillPath.Count > 0
            ? drillPath[drillPath.Count - 1].subOptions
            : variant.options;

        // 面包屑——下钻了几层、能不能返回上一层。
        if (drillPath.Count > 0)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("◀ 返回上一层", GUILayout.Width(110f)))
            {
                drillPath.RemoveAt(drillPath.Count - 1);
                GUI.FocusControl(null);
            }
            string path = "开场白";
            foreach (var step in drillPath)
                path += " ▶ " + Truncate(ResolveOptionPreviewLabel(step), 10);
            GUILayout.Label(path, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        int childCount = currentOptions.Count + 1; // +1 = "添加选项"占位框
        float totalWidth = childCount * TreeBoxWidth + (childCount - 1) * TreeHGap;

        Rect canvasRect = GUILayoutUtility.GetRect(
            Mathf.Max(totalWidth, 200f), TreeBoxHeight * 2f + TreeVGap + 12f, GUILayout.ExpandWidth(true));

        float centerX = canvasRect.x + canvasRect.width * 0.5f;
        float startX = centerX - totalWidth * 0.5f;

        Rect parentRect = new Rect(centerX - TreeBoxWidth * 0.5f, canvasRect.y, TreeBoxWidth, TreeBoxHeight);
        float childY = canvasRect.y + TreeBoxHeight + TreeVGap;

        // 先画连线，框后画盖住线头，视觉上线是"长出来"的，不是穿过框中心。
        // 横平竖直的直角连线，跟科技树画布同一套(DrawOrthogonalVerticalEdge)。
        Handles.BeginGUI();
        Handles.color = new Color(1f, 1f, 1f, 0.35f);
        for (int i = 0; i < childCount; i++)
        {
            Rect childRect = new Rect(startX + i * (TreeBoxWidth + TreeHGap), childY, TreeBoxWidth, TreeBoxHeight);
            DrawOrthogonalVerticalEdge(parentRect, childRect);
        }
        Handles.EndGUI();

        // 顶部节点——没下钻时是开场白，下钻了就是当前这一层的"父选项"本身
        // (显示它自己的预览文字，点它=选中它，详情区照常能编辑它)。
        if (drillPath.Count == 0)
        {
            bool greetingSelected = _selectedVariant == variant && _selectedOption == null;
            if (DrawTreeNodeBox(parentRect, "【开场白】\n" + Truncate(ResolveGreetingPreview(variant), 12), greetingSelected))
            {
                _selectedVariant = variant;
                _selectedOption = null;
                GUI.FocusControl(null); // 切换选中节点时清掉输入焦点，不然下面详情区的文本框
                                         // 还锁着上一个节点没提交的编辑缓冲，看起来像"没刷新"。
            }
        }
        else
        {
            var parentOption = drillPath[drillPath.Count - 1];
            bool parentSelected = _selectedVariant == variant && _selectedOption == parentOption;
            if (DrawTreeNodeBox(parentRect, Truncate(ResolveOptionPreviewLabel(parentOption), 12), parentSelected))
            {
                _selectedVariant = variant;
                _selectedOption = parentOption;
                GUI.FocusControl(null);
            }
        }

        // 选项节点
        DialogueOption toRemove = null;
        bool draggingInThisVariant = _dragActive && _dragVariant == variant;

        for (int oi = 0; oi < currentOptions.Count; oi++)
        {
            var option = currentOptions[oi];
            Rect childRect = new Rect(startX + oi * (TreeBoxWidth + TreeHGap), childY, TreeBoxWidth, TreeBoxHeight);
            bool selected = _selectedVariant == variant && _selectedOption == option;

            // 拖拽中的插入位置指示——在目标槽位左边画一条竖线。
            if (draggingInThisVariant && _dragInsertIndex == oi)
                EditorGUI.DrawRect(new Rect(childRect.x - TreeHGap * 0.5f - 1f, childRect.y, 2f, childRect.height), AccentColor);

            bool isDragSource = _dragActive && _dragOption == option;
            if (isDragSource) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            DrawTreeNodeBoxVisual(childRect, Truncate(ResolveOptionPreviewLabel(option), 12), selected);
            if (isDragSource) GUI.color = Color.white;

            // 手动接管点击/拖拽——不能用 GUI.Button，它内部会在 MouseUp 时 Use() 掉
            // 事件，导致后面自己写的"拖拽结束"判断永远收不到那个 MouseUp。
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && childRect.Contains(Event.current.mousePosition))
            {
                _dragOption = option;
                _dragVariant = variant;
                _dragStartPos = Event.current.mousePosition;
                _dragActive = false;
                GUIUtility.hotControl = TreeDragHotControlId;
                Event.current.Use();
            }

            if (Event.current.type == EventType.ContextClick && childRect.Contains(Event.current.mousePosition))
            {
                var captured = option;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("删除选项"), false, () => { toRemove = captured; });
                menu.ShowAsContext();
                Event.current.Use();
            }
        }

        // "＋添加选项"占位框——永远在最后，不参与拖拽排序，也不是合法的插入目标。
        Rect addRect = new Rect(startX + currentOptions.Count * (TreeBoxWidth + TreeHGap), childY, TreeBoxWidth, TreeBoxHeight);
        if (draggingInThisVariant && _dragInsertIndex >= currentOptions.Count)
            EditorGUI.DrawRect(new Rect(addRect.x - TreeHGap * 0.5f - 1f, addRect.y, 2f, addRect.height), AccentColor);

        if (GUI.Button(addRect, "＋ 添加选项"))
        {
            var newOption = new DialogueOption();
            currentOptions.Add(newOption);
            _selectedVariant = variant;
            _selectedOption = newOption;
            GUI.FocusControl(null);
            dirty = true;
        }

        // 拖拽移动中/松开——用鼠标横坐标相对起始位置换算成第几个槽位，不依赖某一个
        // 具体框的Rect(拖动过程中鼠标可能已经飞出所有框的范围)。整段自己接管事件，
        // 不借助 GUI.Button，MouseUp 到这里时事件还没被谁提前消费掉。
        if (_dragOption != null && _dragVariant == variant)
        {
            Event e = Event.current;

            if (GUIUtility.hotControl != TreeDragHotControlId)
            {
                // hotControl 意外被别的控件抢走了(不应该发生，但防御性放弃这次拖拽/
                // 点击状态，避免卡在"按着不放"的状态里)。
                _dragOption = null;
                _dragVariant = null;
                _dragActive = false;
                _dragInsertIndex = -1;
            }
            else if (e.type == EventType.MouseDrag)
            {
                if (!_dragActive && (e.mousePosition - _dragStartPos).magnitude > 6f)
                    _dragActive = true;

                if (_dragActive)
                {
                    float relativeX = e.mousePosition.x - startX + (TreeBoxWidth + TreeHGap) * 0.5f;
                    int insertIndex = Mathf.Clamp(Mathf.FloorToInt(relativeX / (TreeBoxWidth + TreeHGap)), 0, currentOptions.Count);
                    _dragInsertIndex = insertIndex;
                    Context.Repaint();
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                if (_dragActive && _dragInsertIndex >= 0)
                {
                    int fromIndex = currentOptions.IndexOf(_dragOption);
                    int insertAt = Mathf.Clamp(_dragInsertIndex, 0, currentOptions.Count);
                    if (fromIndex >= 0 && fromIndex != insertAt && fromIndex != insertAt - 1)
                    {
                        var moved = _dragOption;
                        currentOptions.RemoveAt(fromIndex);
                        if (insertAt > fromIndex) insertAt--;
                        currentOptions.Insert(Mathf.Clamp(insertAt, 0, currentOptions.Count), moved);
                        dirty = true;
                    }
                }
                else
                {
                    // 没超过拖拽阈值——当成普通点击，选中这个节点。
                    _selectedVariant = variant;
                    _selectedOption = _dragOption;
                    GUI.FocusControl(null);
                }

                _dragOption = null;
                _dragVariant = null;
                _dragActive = false;
                _dragInsertIndex = -1;
                GUIUtility.hotControl = 0;
                e.Use();
            }
        }

        if (toRemove != null)
        {
            currentOptions.Remove(toRemove);
            if (drillPath.Contains(toRemove)) drillPath.Clear(); // 删掉的正好是路径上的节点，路径失效，退回顶层
            if (_selectedOption == toRemove) _selectedOption = null;
            dirty = true;
        }

        return dirty;
    }

    /// <summary>右角连线：从父框底边中点竖直向下，到父子中点高度再横向平移，
    /// 最后竖直接入子框顶边中点——横平竖直，跟科技树画布同一套画法。</summary>
    private static void DrawOrthogonalVerticalEdge(Rect parentRect, Rect childRect)
    {
        Vector3 start = new Vector3(parentRect.center.x, parentRect.yMax, 0f);
        Vector3 end = new Vector3(childRect.center.x, childRect.yMin, 0f);
        float midY = (start.y + end.y) * 0.5f;

        Handles.DrawLine(start, new Vector3(start.x, midY, 0f));
        Handles.DrawLine(new Vector3(start.x, midY, 0f), new Vector3(end.x, midY, 0f));
        Handles.DrawLine(new Vector3(end.x, midY, 0f), end);
    }

    /// <summary>画一个树节点框，返回这一帧是否被点击。文字和点击各画各的——
    /// 点击层用空样式的Button盖在上面接输入，不会覆盖掉下面已经画好的文字。</summary>
    private bool DrawTreeNodeBox(Rect rect, string label, bool selected)
    {
        DrawTreeNodeBoxVisual(rect, label, selected);
        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    /// <summary>只画外观，不接管点击——选项框需要自己手动处理MouseDown/Drag/Up
    /// 来实现拖拽排序，GUI.Button 内部会在MouseUp时消费(Use)事件，导致后面
    /// 自己写的拖拽结束判断永远收不到那个MouseUp，所以选项框不能用它。</summary>
    private void DrawTreeNodeBoxVisual(Rect rect, string label, bool selected)
    {
        EditorGUI.DrawRect(rect, selected ? SelectedFillColor : RowEvenBg);
        DrawBoxOutline(rect, selected ? AccentColor : new Color(1f, 1f, 1f, 0.25f));
        if (selected)
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 3f), AccentColor);

        GUIStyle style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontSize = 11
        };
        GUI.Label(rect, label, style);
    }

    private static void DrawBoxOutline(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "…";
    }

    private string ResolveGreetingPreview(NPCDialogueDefinition.DialogueVariant variant)
    {
        DialogueSentenceLibrary lib = _current.sentenceLibrary;
        if (variant.greetingSentenceIds == null || variant.greetingSentenceIds.Count == 0) return "(未设置)";

        if (variant.greetingSentenceIds.Count > 1)
            return $"({variant.greetingSentenceIds.Count}选1随机)";

        string sentenceId = variant.greetingSentenceIds[0];
        if (lib == null || string.IsNullOrEmpty(sentenceId)) return "(未设置)";
        if (!lib.TryGet(sentenceId, out var entry)) return "(未设置)";

        string speaker = ResolveSpeakerPreview(entry);
        string text = FindText(entry.texts, GetDefaultLanguageCode());
        if (string.IsNullOrEmpty(text)) return "(空)";
        return string.IsNullOrEmpty(speaker) ? text : $"{speaker}：{text}";
    }

    /// <summary>把一条句子库条目拼成"编号 | 说话人：文本"这种可读的下拉选项文字——
    /// 挑句子的地方(开场白/跳转句子)都用这个，不再是光秃秃的编号。</summary>
    private string BuildSentenceOptionLabel(DialogueSentenceLibrary.SentenceEntry entry)
    {
        string id = string.IsNullOrEmpty(entry.sentenceId) ? "(未命名)" : entry.sentenceId;
        string speaker = ResolveSpeakerPreview(entry);
        string text = FindText(entry.texts, GetDefaultLanguageCode());
        string body = string.IsNullOrEmpty(speaker) ? text : $"{speaker}：{text}";
        return string.IsNullOrEmpty(body) ? id : $"{id}  |  {Truncate(body, 22)}";
    }

    private string ResolveSpeakerPreview(DialogueSentenceLibrary.SentenceEntry entry)
    {
        if (entry.speakerAnonymous) return "???";

        string code = GetDefaultLanguageCode();

        string alias = FindText(entry.speakerAliasOverride, code);
        if (!string.IsNullOrEmpty(alias)) return alias;

        if (entry.speakerUnit == null) return null;

        string localized = FindLocalizedUnitName(entry.speakerUnit, code);
        return !string.IsNullOrEmpty(localized) ? localized : entry.speakerUnit.displayName;
    }

    private static string FindLocalizedUnitName(UnitDefinition unit, string languageCode)
    {
        if (unit == null || unit.localizedNames == null || string.IsNullOrEmpty(languageCode)) return null;
        foreach (var t in unit.localizedNames)
            if (t != null && t.languageCode == languageCode) return t.text;
        return null;
    }

    private bool DrawOptionDetails(DialogueOption option, List<LocalizationProjectSettings.LanguageEntry> langs)
    {
        bool dirty = false;

        EditorGUILayout.LabelField("选项文本", EditorStyles.miniBoldLabel);
        EnsureLanguageSlots(option.label, langs);
        foreach (var lang in langs)
        {
            foreach (var t in option.label)
            {
                if (t.languageCode != lang.languageCode) continue;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"文本（{lang.displayName}）", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
                string newText = EditorGUILayout.TextField(t.text);
                if (newText != t.text) { t.text = newText; dirty = true; }
                EditorGUILayout.EndHorizontal();
                break;
            }
        }

        DrawSectionDivider();
        EditorGUILayout.LabelField("动作", EditorStyles.miniBoldLabel);

        var newActionType = (DialogueOptionActionType)EditorGUILayout.EnumPopup("动作", option.actionType);
        if (newActionType != option.actionType) { option.actionType = newActionType; dirty = true; }

        switch (option.actionType)
        {
            case DialogueOptionActionType.ShowSentence:
            {
                dirty |= DrawOptionTargetSentence(option);
                bool newClose = EditorGUILayout.ToggleLeft("这句讲完后关闭对话(告别语用)", option.closeAfterShow);
                if (newClose != option.closeAfterShow) { option.closeAfterShow = newClose; dirty = true; }
                break;
            }

            case DialogueOptionActionType.SetQuestFlag:
            {
                dirty |= DrawOptionTargetSentence(option);
                string newFlag = EditorGUILayout.TextField("任务标记", option.questFlag);
                if (newFlag != option.questFlag) { option.questFlag = newFlag; dirty = true; }
                break;
            }

            case DialogueOptionActionType.AcceptQuest:
            {
                LocalizationProjectSettings questLocSettings = LocalizationSettingsUtility.GetOrCreateSettings();
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("要接取的任务(可选)", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
                string questLabel = option.questToAccept != null
                    ? QuestPickerPopupContent.GetLabel(option.questToAccept, questLocSettings)
                    : "（留空——按归属NPC自动判断）";
                Rect questBtnRect = GUILayoutUtility.GetRect(new GUIContent(questLabel), EditorStyles.objectField, GUILayout.ExpandWidth(true));
                if (GUI.Button(questBtnRect, questLabel, EditorStyles.objectField))
                {
                    var capturedOption = option;
                    var capturedPackage = _current;
                    PopupWindow.Show(questBtnRect, new QuestPickerPopupContent(picked =>
                    {
                        capturedOption.questToAccept = picked;
                        EditorUtility.SetDirty(capturedPackage);
                        AssetDatabase.SaveAssets();
                    }));
                }
                if (option.questToAccept != null && GUILayout.Button("清空", GUILayout.Width(50f)))
                {
                    option.questToAccept = null;
                    EditorUtility.SetDirty(_current);
                    AssetDatabase.SaveAssets();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox(
                    "点这个选项永远先弹出一个子列表，列表里的任务选中才真正接取，不会有" +
                    "任务名字/接取动作直接暴露在这个选项按钮本身上。留空(推荐)：列表里" +
                    "自动列出\"归属NPC=当前这个NPC\"名下全部满足条件(未接取/完成+解锁条件" +
                    "成立)的可接任务——归属NPC本身就是唯一标志，不用在这里重复指定，也" +
                    "不需要手动建子选项。只有需要单独再放一个只对应某一条任务的独立入口" +
                    "时，才手动选一个具体任务在这里强制指定(列表里就只会显示这一条)。" +
                    "这个选项本身的按钮文字用上面\"标题\"栏手打的静态文案(比如\"有事情" +
                    "要谈...\")，不会被任务名字覆盖。",
                    MessageType.None);
                break;
            }

            case DialogueOptionActionType.OpenWindow:
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("窗口", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
                string windowLabel = option.targetWindowPrefab != null
                    ? WindowPrefabPickerPopupContent.GetLabel(option.targetWindowPrefab)
                    : "（未选择）";
                Rect windowBtnRect = GUILayoutUtility.GetRect(new GUIContent(windowLabel), EditorStyles.objectField, GUILayout.ExpandWidth(true));
                if (GUI.Button(windowBtnRect, windowLabel, EditorStyles.objectField))
                {
                    var capturedOption = option;
                    var capturedPackage = _current;
                    PopupWindow.Show(windowBtnRect, new WindowPrefabPickerPopupContent(picked =>
                    {
                        capturedOption.targetWindowPrefab = picked;
                        EditorUtility.SetDirty(capturedPackage);
                        AssetDatabase.SaveAssets();
                    }));
                }
                EditorGUILayout.EndHorizontal();

                // 窗口具体打开哪份数据——同样是"商店"窗口，供给站/古董店/工坊商店是
                // 不同的ShopDefinition，必须能在这里指定，不能靠窗口prefab自己写死。
                // 按选中的窗口prefab动态决定这个字段该过滤成哪个具体类型(比如商店
                // 窗口只给选ShopDefinition)，不然默认Object选择器什么资产都往里塞，
                // 根本找不到商店在哪。以后新窗口类型在 GetExpectedPayloadType 里加
                // 一行映射即可，不用每种窗口各加一个专属字段。
                Type payloadType = GetExpectedPayloadType(option.targetWindowPrefab);
                bool payloadRequired = payloadType != typeof(UnityEngine.Object);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(
                    payloadRequired ? $"具体{payloadType.Name}（必选）" : "窗口数据",
                    GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
                string payloadLabel = option.targetWindowPayload != null
                    ? GenericAssetPickerPopupContent.GetLabel(option.targetWindowPayload)
                    : "（未选择）";
                Rect payloadBtnRect = GUILayoutUtility.GetRect(new GUIContent(payloadLabel), EditorStyles.objectField, GUILayout.ExpandWidth(true));
                GUI.enabled = payloadRequired; // 没选窗口/窗口不需要具体数据时，这里没有意义可选的东西
                if (GUI.Button(payloadBtnRect, payloadLabel, EditorStyles.objectField))
                {
                    var capturedOption = option;
                    var capturedPackage = _current;
                    PopupWindow.Show(payloadBtnRect, new GenericAssetPickerPopupContent(payloadType, picked =>
                    {
                        capturedOption.targetWindowPayload = picked;
                        EditorUtility.SetDirty(capturedPackage);
                        AssetDatabase.SaveAssets();
                    }));
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                if (payloadRequired && option.targetWindowPayload == null)
                {
                    EditorGUILayout.HelpBox(
                        $"这个窗口必须指定具体打开哪个 {payloadType.Name}(比如供给站/古董店/工坊商店不是同一份数据)——" +
                        "不选的话运行时会用prefab自己原本绑定的那份，很可能不是你想要的那个。",
                        MessageType.Warning);
                }
                else if (!payloadRequired)
                {
                    EditorGUILayout.HelpBox("这个窗口不需要额外指定数据，留空即可。", MessageType.None);
                }

                bool newReturn = EditorGUILayout.ToggleLeft("关闭子窗口后弹回对话", option.returnToDialogueAfterWindowClose);
                if (newReturn != option.returnToDialogueAfterWindowClose) { option.returnToDialogueAfterWindowClose = newReturn; dirty = true; }
                break;
            }

            case DialogueOptionActionType.ShowQuestList:
                EditorGUILayout.HelpBox(
                    "运行时会按这个交互包挂载的NPC，动态列出该NPC名下进行中的任务(QuestDefinition.giverNpc)，不需要额外配置目标。",
                    MessageType.None);
                break;
        }

        DrawSectionDivider();
        EditorGUILayout.LabelField("子选项（多层分支——点这个选项后换成显示这一套，不是回到当前这层原来的选项）", EditorStyles.miniBoldLabel);

        int subCount = option.subOptions != null ? option.subOptions.Count : 0;
        List<DialogueOption> ownerDrillPath = GetDrillPath(_selectedVariant);
        bool alreadyHere = ownerDrillPath.Count > 0 && ownerDrillPath[ownerDrillPath.Count - 1] == option;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(subCount > 0 ? $"已配 {subCount} 个子选项" : "还没有子选项", GUILayout.ExpandWidth(true));

        if (alreadyHere)
        {
            EditorGUILayout.LabelField("（当前正显示这一层）", GUILayout.Width(160f));
        }
        else if (GUILayout.Button(subCount > 0 ? "进入子选项 ▶" : "＋ 新建子选项并进入", GUILayout.Width(160f)))
        {
            if (subCount == 0)
            {
                option.subOptions.Add(new DialogueOption());
                dirty = true;
            }
            ownerDrillPath.Add(option);
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();

        DrawSectionDivider();
        EditorGUILayout.LabelField("显示条件（全部成立才显示这个选项，留空=永远显示）", EditorStyles.miniBoldLabel);
        dirty |= DrawConditionList(option.visibilityConditions);

        return dirty;
    }

    private static void DrawSectionDivider()
    {
        GUILayout.Space(6f);
        Rect r = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.12f));
        GUILayout.Space(6f);
    }

    /// <summary>跳转句子（配多个=随机回应，配一个=固定）。</summary>
    private bool DrawOptionTargetSentence(DialogueOption option)
    {
        bool dirty = false;
        DialogueSentenceLibrary lib = _current.sentenceLibrary;

        EditorGUILayout.LabelField("跳转句子（配多句=随机回应；配一句=固定）", EditorStyles.miniBoldLabel);

        if (lib != null && lib.entries.Count > 0)
        {
            int removeIdx = -1;
            for (int ti = 0; ti < option.targetSentenceIds.Count; ti++)
            {
                string currentId = option.targetSentenceIds[ti];
                var currentEntry = lib.entries.Find(e => e != null && e.sentenceId == currentId);
                string btnLabel = currentEntry != null ? BuildSentenceOptionLabel(currentEntry) : "（未选择）";

                EditorGUILayout.BeginHorizontal();
                Rect btnRect = GUILayoutUtility.GetRect(new GUIContent(btnLabel), EditorStyles.objectField, GUILayout.ExpandWidth(true));
                if (GUI.Button(btnRect, btnLabel, EditorStyles.objectField))
                {
                    int capturedIndex = ti;
                    PopupWindow.Show(btnRect, new SentencePickerPopupContent(lib.entries, BuildSentenceOptionLabel, picked =>
                    {
                        option.targetSentenceIds[capturedIndex] = picked;
                        EditorUtility.SetDirty(_current);
                        AssetDatabase.SaveAssets();
                    }));
                }
                if (GUILayout.Button("×", GUILayout.Width(24f)))
                    removeIdx = ti;
                EditorGUILayout.EndHorizontal();
            }

            if (removeIdx >= 0)
            {
                option.targetSentenceIds.RemoveAt(removeIdx);
                dirty = true;
            }

            if (GUILayout.Button("＋ 添加一句"))
            {
                option.targetSentenceIds.Add(lib.entries[0].sentenceId);
                dirty = true;
            }
        }
        else
        {
            EditorGUILayout.HelpBox("上面先选一个句子库，才能挑跳转句子。", MessageType.None);
        }

        return dirty;
    }

    /// <summary>按窗口prefab上挂的controller类型，判断这个窗口"必须指定哪个具体数据"——
    /// 商店窗口必须知道是供给站还是古董店，不是可选项。以后新的窗口类型
    /// (比如设施升级窗口)在这里加一行映射即可，不用改 DialogueOption 的数据结构。</summary>
    private static Type GetExpectedPayloadType(GameObject windowPrefab)
    {
        if (windowPrefab == null) return typeof(UnityEngine.Object);

        if (windowPrefab.GetComponent<ShopWindowController>() != null)
            return typeof(ShopDefinition);

        return typeof(UnityEngine.Object);
    }

    private string ResolveOptionPreviewLabel(DialogueOption option)
    {
        string code = GetDefaultLanguageCode();
        string text = FindText(option.label, code);
        if (string.IsNullOrEmpty(text)) text = "(未命名选项)";
        return $"{text}  [{option.actionType}]";
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

    private static string FindText(List<LocalizedTextEntry> texts, string languageCode)
    {
        if (texts == null || string.IsNullOrEmpty(languageCode)) return null;
        foreach (var t in texts)
            if (t != null && t.languageCode == languageCode) return t.text;
        return null;
    }

    private static void EnsureLanguageSlots(List<LocalizedTextEntry> texts, List<LocalizationProjectSettings.LanguageEntry> langs)
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

    private void StartPackageRename(NPCDialogueDefinition pkg)
    {
        _renamingPackage = pkg;
        _renameBuffer = pkg.name;
        EditorGUIUtility.editingTextField = true;
        EditorApplication.delayCall += () => EditorGUI.FocusTextInControl(RenameControlName);
    }

    private void CommitPackageRename()
    {
        if (_renamingPackage == null) return;

        string newName = _renameBuffer.Trim();
        if (!string.IsNullOrEmpty(newName) && newName != _renamingPackage.name)
        {
            string path = AssetDatabase.GetAssetPath(_renamingPackage);
            AssetDatabase.RenameAsset(path, newName);
            AssetDatabase.SaveAssets();
        }

        _renamingPackage = null;
    }

    private void ShowPackageContextMenu(NPCDialogueDefinition pkg)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("重命名"), false, () => StartPackageRename(pkg));
        menu.AddItem(new GUIContent("复制"), false, () => DuplicatePackage(pkg));
        menu.AddItem(new GUIContent("在 Inspector 中打开"), false, () => Selection.activeObject = pkg);
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("删除"), false, () => DeletePackage(pkg));
        menu.ShowAsContext();
    }

    private void DuplicatePackage(NPCDialogueDefinition pkg)
    {
        string path = AssetDatabase.GetAssetPath(pkg);
        string newPath = AssetDatabase.GenerateUniqueAssetPath(path);
        if (!AssetDatabase.CopyAsset(path, newPath)) return;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var copy = AssetDatabase.LoadAssetAtPath<NPCDialogueDefinition>(newPath);
        ReloadAssets();
        if (copy != null) _current = copy;
    }

    private void DeletePackage(NPCDialogueDefinition pkg)
    {
        if (!EditorUtility.DisplayDialog("删除交互包", $"确定要删除「{pkg.name}」吗？这个操作不可撤销。", "删除", "取消"))
            return;

        string path = AssetDatabase.GetAssetPath(pkg);
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();

        if (_current == pkg) _current = null;
        if (_renamingPackage == pkg) _renamingPackage = null;

        ReloadAssets();
    }

    private void CreatePackageAsset()
    {
        EnsureFolder(DefaultFolder);

        string path = AssetDatabase.GenerateUniqueAssetPath(DefaultFolder + "/NPCDialogueDefinition.asset");
        var pkg = ScriptableObject.CreateInstance<NPCDialogueDefinition>();
        AssetDatabase.CreateAsset(pkg, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        _packages.Add(pkg);
        _current = pkg;
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

        _packages.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:NPCDialogueDefinition"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var pkg = AssetDatabase.LoadAssetAtPath<NPCDialogueDefinition>(path);
            if (pkg != null) _packages.Add(pkg);
        }

        if (_current == null && _packages.Count > 0)
            _current = _packages[0];
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
