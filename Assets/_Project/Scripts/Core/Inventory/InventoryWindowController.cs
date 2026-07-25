using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SkyPrison.Runtime.UI;

/// <summary>
/// 挂在 PF_SkyPrisonInventory 根节点上：
///  - 关闭按钮 / ESC
///  - 筛选标签栏：每个标签可点击聚焦，选中项为「白字 + 沿字形蓝色辉光」，
///    并按分类把不匹配的道具图标调暗(不是隐藏)。
/// WindowManager 通过 Open() 实例化时自动注入 managerRef。
/// </summary>
public class InventoryWindowController : MonoBehaviour, ISkyPrisonWindowHintProvider
{
    // 底部操作提示条的内容（拖拽换位/合并、右键拆分、拖出丢弃、关闭）。
    // 之前是 static readonly 数组，label 是写死的中文字符串——static 字段初始化时
    // 根本没有 locTable 可查（也没有实例上下文能调 L()），不管切什么语言这条提示条
    // 都只会显示中文。
    //
    // GetWindowHints() 是 SkyPrisonWindowManager_V1 每帧调用的——如果每次都重新
    // GetComponentInChildren + 13 次查表 + new 数组，相当于每秒 60 次做这些开销，
    // 足够造成的 GC/帧间抖动会让 SkyPrisonInventoryChromatic 那套「帧间对比面板
    // 矩形」的运动检测偶发误判，表现为窗口快照层和实时磨砂层来回切换的"忽明忽暗"。
    // 所以缓存结果，只在语言真的变化时才重建，其余帧直接返回缓存，跟以前 static
    // 数组一样零开销。
    private SkyPrisonWindowHint[] _cachedHints;

    private void RebuildHintsCache() => _cachedHints = BuildHints();

    private SkyPrisonWindowHint[] BuildHints()
    {
        var loc = GetComponentInChildren<SkyPrisonInventoryTextLocalizer>(true)
               ?? GetComponentInParent<SkyPrisonInventoryTextLocalizer>();
        var table = loc != null ? loc.Table : null;
        string L(string key, string fallback) => table != null ? table.Get(key, fallback) : fallback;

        return new[]
        {
            // 键鼠（手柄模式自动隐藏）
            SkyPrisonWindowHint.Icon("mouse/left",  "拖拽", L("ui_hint_move_merge", "换位/合并")),
            SkyPrisonWindowHint.Icon("mouse/right", "右键", L("ui_hint_split",       "拆分")),
            SkyPrisonWindowHint.Icon("mouse/left",  "拖出", L("ui_hint_discard",     "丢弃")),
            // 手柄（键鼠模式自动隐藏）——之前把整理/详情/切分类/切排序全塞进提示条，
            // 背包仓库同时开着时两边提示条拼一起长得夸张，用户反馈"选关键的"。这里
            // 只留最核心的几个，其余功能仍然生效，只是不在提示条里常驻刷存在感。
            SkyPrisonWindowHint.GamepadIcon("gamepad/up",     "↕",  L("ui_hint_move",        "移动")),
            SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/a", "A",  L("ui_hint_grab_drop",    "抓取/放下")),
            SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/b", "B",  L("ui_hint_menu",         "菜单")),
            // 背包仓库同时打开时用来切换手柄光标操作哪一边——只有另一个窗口也开着才
            // 真正有意义，但提示条不区分这个条件，常驻显示。
            SkyPrisonWindowHint.GamepadIcon("gamepad/r3", "R3", L("ui_hint_switch_window", "切换焦点")),
            // 关闭：动作型，键鼠出 B/Tab、手柄出 △ 键帽（自适应）
            SkyPrisonWindowHint.Action(SkyPrisonInputAction.Inventory, L("ui_hint_close", "关闭")),
        };
    }

    public System.Collections.Generic.IReadOnlyList<SkyPrisonWindowHint> GetWindowHints()
    {
        if (_cachedHints == null) RebuildHintsCache();
        return _cachedHints;
    }

    [SerializeField] private Button closeButton;

    [Header("筛选标签")]
    [Tooltip("中文 TMP 字体(动态图集)。为空则标签辉光/聚焦不生效。")]
    [SerializeField] private TMP_FontAsset tabFont;
    [SerializeField] private int   selectedTabIndex  = 0;
    [SerializeField] private float tabFontSize       = 21f;
    [SerializeField] private Color selectedFaceColor = Color.white;
    [SerializeField] private Color normalColor       = Color.white;
    [SerializeField] private Color glowColor         = new Color(0.42f, 0.92f, 0.68f, 1f); // 默认=系统冷绿，见 SkyPrisonUIPalette.ColdGreen
    [Tooltip("辉光强度。越大越亮越浓。")]
    [SerializeField, Range(0f, 3f)] private float glowIntensity = 1.5f;
    [Tooltip("辉光向外渗开的像素余量。越大光晕铺得越远越柔。")]
    [SerializeField, Range(4f, 64f)] private float glowPadding = 26f;

    private SkyPrisonWindowManager_V1 _manager;
    private readonly List<TextMeshProUGUI> _tabLabels = new List<TextMeshProUGUI>();
    private SkyPrisonTabGlowRenderer _glowRenderer;
    private InventoryGridView _gridView;
    private int _selected = -1;

    [Tooltip("未选中标签 hover 时的文字色（半透明冷绿，作为可点击预览）。")]
    [SerializeField] private Color hoverColor = new Color(0.42f, 0.92f, 0.68f, 0.5f);

    public InventoryFilterTab CurrentFilter { get; private set; } = InventoryFilterTab.All;

    public void Init(Button btn) => closeButton = btn;

    /// <summary>供 hover 组件查询某标签是否为当前选中项（选中项不被 hover 着色覆盖）。</summary>
    public bool IsTabSelected(int index) => _selected == index;

    /// <summary>编辑器构建 PF 时调用，强制刷新选中标签辉光色（系统配色，覆盖旧序列化值）。</summary>
    public void SetGlowColor(Color c) => glowColor = c;

    private void Awake()
    {
        if (_manager == null)
            _manager = FindObjectOfType<SkyPrisonWindowManager_V1>();

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(CloseWindow);
            SkyPrison.Runtime.UI.SkyPrisonUIButtonFeedback.Attach(closeButton.gameObject);
        }

        SetupFilterTabs();

        // 排序控件接线（下拉项 / 升降序 / 整理），运行时自愈，与标签同套路。
        if (GetComponent<SkyPrison.Runtime.UI.SkyPrisonInventorySortControls>() == null)
            gameObject.AddComponent<SkyPrison.Runtime.UI.SkyPrisonInventorySortControls>();
    }

    private void OnEnable()
    {
        LocalizationRuntime.OnLanguageChanged += OnLanguageChanged;
    }

    private void OnDisable()
    {
        LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
    }

    private void Start()
    {
        // Unity 先跑完场景里所有物体的 Awake 才轮到 OnEnable——SetupFilterTabs() 在
        // Awake 里调 SelectTab() 时，标签文字本地化（SkyPrisonInventoryTextLocalizer.
        // OnEnable）根本还没跑，辉光是按翻译前的旧文字烤的图，之后文字变成目标语言，
        // 辉光形状就再也没更新过、包不住文字。Start() 保证这时候所有 OnEnable 都已经
        // 跑完，再重选一次当前标签，让辉光按最终已本地化的文字重新烤一次。
        if (_selected >= 0) SelectTab(_selected);
        RebuildHintsCache();
    }

    private void OnLanguageChanged(string _)
    {
        // 运行时切语言：标签文字已经由 SkyPrisonInventoryTextLocalizer 更新，这里只
        // 需要让辉光跟着重新烤一次，形状才会贴合新语言的文字。
        if (_selected >= 0) SelectTab(_selected);
        RebuildHintsCache();
    }

    private void OnDestroy()
    {
        LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
        if (closeButton != null)
            closeButton.onClick.RemoveListener(CloseWindow);
    }

    public void CloseWindow()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);
        if (_manager == null)
            _manager = FindObjectOfType<SkyPrisonWindowManager_V1>();
        _manager?.Close("inventory");
    }

    // ── 标签栏 ───────────────────────────────────────────────────────────────

    private void SetupFilterTabs()
    {
        Transform filterBar = FindDeep(transform, "FilterBar");
        var tabs = new List<Transform>();
        if (filterBar != null)
            foreach (Transform child in filterBar)
                if (child.name.StartsWith("FilterTab"))
                    tabs.Add(child);

        Debug.Log($"[InvTabs] tabFont={(tabFont != null ? tabFont.name : "NULL")}, " +
                  $"filterBar={(filterBar != null)}, tabs={tabs.Count}", this);

        if (tabFont == null || filterBar == null || tabs.Count == 0) return;

        try { SetupFilterTabsCore(tabs, filterBar); }
        catch (System.Exception e) { Debug.LogError($"[InvTabs] 配置标签出错: {e}", this); }
    }

    private void SetupFilterTabsCore(List<Transform> tabs, Transform filterBar)
    {

        // 2026-07-23：之前这里挂在 gameObject(this) 上——InventoryWindowController 其实
        // 挂在 prefab 根节点上，不是 InventoryPanel 本身；真正被拖拽移动的是
        // InventoryPanel，不是根节点。发光条组件用"自己所在节点"当坐标换算的稳定
        // 参照系，挂在根节点上时这个参照系跟拖拽移动的对象对不上，表现为"拖动背包
        // 窗口之后，选中标签下面那条线不会跟着挪，一直停在生成那一刻的位置"。
        // FilterBar 的直接父节点就是 InventoryPanel，挂在那上面才对。
        Transform inventoryPanel = filterBar.parent;
        _glowRenderer = inventoryPanel.GetComponent<SkyPrisonTabGlowRenderer>()
                        ?? inventoryPanel.gameObject.AddComponent<SkyPrisonTabGlowRenderer>();
        _glowRenderer.Configure(tabFont, glowColor, glowIntensity, glowPadding);

        _tabLabels.Clear();
        for (int i = 0; i < tabs.Count; i++)
        {
            Transform tab = tabs[i];
            TextMeshProUGUI tmp = EnsureTabLabel(tab);
            _tabLabels.Add(tmp);

            // 整块标签可点击：盖一张透明的 raycast Image，点击事件冒泡到本 GO 上的 Button。
            EnsureClickArea(tab);
            Button btn = tab.GetComponent<Button>() ?? tab.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            int index = i; // 闭包捕获
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => SelectTab(index));

            // 悬停预览：未选中项 hover 时文字染半透明冷绿（事件经 ClickArea 冒泡到本标签）。
            var hover = tab.GetComponent<SkyPrison.Runtime.UI.SkyPrisonInventoryTabHover>()
                        ?? tab.gameObject.AddComponent<SkyPrison.Runtime.UI.SkyPrisonInventoryTabHover>();
            hover.Configure(IsTabSelected, index, tmp, normalColor, hoverColor);
        }

        SelectTab(Mathf.Clamp(selectedTabIndex, 0, _tabLabels.Count - 1));
    }

    // 标签文字:无论当前是旧版 Text 还是已烤入的 TMP，都强制重新配置好(自愈),保证可见。
    private TextMeshProUGUI EnsureTabLabel(Transform tab)
    {
        // 读出标签文字(优先旧版 Text，其次已有 TMP，最后从物体名推断)。
        Text legacy = tab.GetComponent<Text>();
        TextMeshProUGUI tmp = tab.GetComponentInChildren<TextMeshProUGUI>(true);
        string label =
            legacy != null && !string.IsNullOrEmpty(legacy.text) ? legacy.text :
            tmp != null && !string.IsNullOrEmpty(tmp.text) ? tmp.text :
            tab.name.Replace("FilterTab_", "");

        if (tmp == null)
        {
            var go = new GameObject("TabLabelTMP", typeof(RectTransform));
            go.transform.SetParent(tab, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
            tmp = go.AddComponent<TextMeshProUGUI>();
        }

        tmp.font          = tabFont;                    // 强制(动态字体)
        tmp.fontSharedMaterial = tabFont.material;      // 先给正确的默认材质，避免坏材质导致不显示
        tmp.text          = label;
        tmp.fontSize      = tabFontSize;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;                      // 点击交给 ClickArea
        tmp.overflowMode  = TextOverflowModes.Overflow;
        tmp.color         = normalColor;
        tmp.enabled       = true;

        // 文字已经放进 TMP，最后再隐藏旧版 Text(确保不会留下空白)。
        if (legacy != null) legacy.enabled = false;
        return tmp;
    }

    private void EnsureClickArea(Transform tab)
    {
        if (tab.Find("ClickArea") != null) return;
        var go = new GameObject("ClickArea", typeof(RectTransform));
        go.transform.SetParent(tab, false);
        go.transform.SetAsFirstSibling();
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0f); // 透明，仅作点击区
        img.raycastTarget = true;
    }

    public void SelectTab(int index)
    {
        if (index < 0 || index >= _tabLabels.Count) return;
        if (index != _selected) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        _selected = index;

        for (int i = 0; i < _tabLabels.Count; i++)
        {
            TextMeshProUGUI tmp = _tabLabels[i];
            if (tmp == null) continue;
            bool sel = i == index;

            // 清晰字层始终用干净的字体材质，只换颜色，保证字面锐利可读。
            tmp.color = sel ? selectedFaceColor : normalColor;
            tmp.fontSharedMaterial = tabFont.material;
        }

        // 辉光只跟着选中项（renderer 内部复用同一张显示层，自动移动到当前标签背后）。
        if (_glowRenderer != null && index >= 0 && index < _tabLabels.Count)
            _glowRenderer.ShowFor(_tabLabels[index]);

        CurrentFilter = (InventoryFilterTab)Mathf.Clamp(index, 0, 5);
        ApplyFilter(CurrentFilter);
    }

    // 调暗逻辑下放到 InventoryGridView（它持有每格 SlotView 引用）。
    private void ApplyFilter(InventoryFilterTab filter)
    {
        if (_gridView == null)
            _gridView = GetComponentInChildren<InventoryGridView>(true);
        _gridView?.ApplyFilter(filter);
    }

    /// <summary>从角色面板点装备槽呼出背包时调用：强制只亮起能装进该槽的物品（武器槽→
    /// 只亮武器，手部槽→只亮手套……），普通 Tab 分类过滤暂时不生效。传 null 解除。</summary>
    public void SetForcedEquipSlotFilter(EquipmentSlotType? slot)
    {
        if (_gridView == null)
            _gridView = GetComponentInChildren<InventoryGridView>(true);
        _gridView?.SetForcedEquipSlotFilter(slot);
    }

    /// <summary>从角色面板点快捷物品行呼出背包时调用：强制只亮起能塞进快捷槽的消耗品，
    /// 传具体槽位序号（0~3）；传 null 解除。</summary>
    public void SetForcedQuickSlotFilter(int? quickSlotIndex)
    {
        if (_gridView == null)
            _gridView = GetComponentInChildren<InventoryGridView>(true);
        _gridView?.SetForcedQuickSlotFilter(quickSlotIndex);
    }

    public int? ForcedQuickSlotIndex => _gridView != null ? _gridView.ForcedQuickSlotIndex : null;
    public EquipmentSlotType? ForcedEquipSlot => _gridView != null ? _gridView.ForcedEquipSlot : null;

    // 装备对比预览用：角色面板轮询这个属性，读当前鼠标悬浮在哪个格子上。
    private SkyPrisonInventoryInteraction _interactionForHover;
    public InventoryItemEntry HoveredEntry
    {
        get
        {
            if (_interactionForHover == null)
                _interactionForHover = GetComponentInChildren<SkyPrisonInventoryInteraction>(true);
            return _interactionForHover != null ? _interactionForHover.HoveredEntry : null;
        }
    }

    // ── 工具 ─────────────────────────────────────────────────────────────────

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
