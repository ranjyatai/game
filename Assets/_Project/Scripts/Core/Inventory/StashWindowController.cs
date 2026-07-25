using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SkyPrison.Runtime.UI;

/// <summary>
/// 仓库窗口控制器。
/// 继承 SkyPrisonBaseWindowController，负责：
///   - 左侧页签（1/2/3/4）切换，已解锁的可点击，未解锁的显示灰色锁
///   - 页切换时把 InventoryGridView 重新绑定到对应 InventoryRuntime 页
///   - 打开时同时打开背包窗口（双窗并排）
/// </summary>
public class StashWindowController : SkyPrisonBaseWindowController
{
    protected override string WindowId => "stash";

    // 之前是 static readonly 数组、label 全是写死的中文——不管切什么语言这条提示条
    // 都只显示中文，用户截图实测确认过（日/英环境下后面几条还是汉语）。改成跟背包
    // InventoryWindowController.BuildHints() 同一个套路：运行时查 locTable，
    // 尽量复用背包那几个语义相同的 key（ui_hint_move/ui_hint_grab_drop/
    // ui_hint_menu/ui_hint_close 背包已经有对应翻译，直接借用）。
    protected override IReadOnlyList<SkyPrisonWindowHint> BuildHints()
    {
        var loc = GetComponentInChildren<SkyPrisonInventoryTextLocalizer>(true)
               ?? GetComponentInParent<SkyPrisonInventoryTextLocalizer>();
        var table = loc != null ? loc.Table : null;
        string L(string key, string fallback) => table != null ? table.Get(key, fallback) : fallback;

        return new[]
        {
            SkyPrisonWindowHint.Icon("mouse/left",  "拖拽", L("ui_hint_transfer_item", "转移物品")),
            SkyPrisonWindowHint.Icon("mouse/right", "右键", L("ui_hint_operate",       "操作")),
            // 之前把整理/翻页/排序全塞进提示条，背包仓库同时开着时两边拼一起太长——
            // 只留最核心的几个，跟背包那边一起精简，其余功能还在，只是不常驻提示。
            SkyPrisonWindowHint.GamepadIcon("gamepad/up",     "↕",  L("ui_hint_move",        "移动")),
            SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/a", "A",  L("ui_hint_grab_drop",    "抓取/放下")),
            SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/b", "B",  L("ui_hint_menu",         "菜单")),
            SkyPrisonWindowHint.GamepadIcon("gamepad/r3", "R3",  L("ui_hint_switch_window", "切换焦点")),
            SkyPrisonWindowHint.Action(SkyPrisonInputAction.Inventory, L("ui_hint_close", "关闭")),
        };
    }

    // ── Inspector 引用 ────────────────────────────────────────────────────

    [Header("页签")]
    [SerializeField] private Transform pageTabContainer;   // 左侧 Tab 容器

    [Header("格子")]
    [SerializeField] private StashGridView stashGridView;  // 复用或扩展自 InventoryGridView

    [Header("同时打开背包")]
    [SerializeField] private GameObject inventoryWindowPrefab;

    // ── 筛选标签（全部/消耗品/材料/装备/任务/重要物品）：跟背包同一套视觉效果(沿字形辉光)，
    // 数值/颜色现读自背包 InventoryWindowController 组件在真实 prefab 上的序列化值
    // (18/{0.66,0.66,0.68,1})，不是它 C# 字段声明的默认值——之前用的21/纯白从没跟这份
    // prefab 实际用的值核对过，导致仓库未选中的标签颜色比背包更亮/更白。已经换算成
    // Stash 面板自己的"字面值即实际渲染值"单位（背包那边是靠面板 1.3 倍运行时缩放放大
    // 出来的，Stash 走 SkyPrisonFloatingWindowKit 那套，字面值本身已经是最终大小）。
    // tabFontSize 目前没有代码实际读取（字号只在编辑器建 prefab 时烤进 TMP 组件一次），
    // 留着只是保持字段跟真实值同步，避免以后有人照抄这个默认值又抄错。

    [Header("筛选标签")]
    [SerializeField] private TMP_FontAsset tabFont;
    [SerializeField] private float tabFontSize = 23.4f;
    [SerializeField] private Color selectedFaceColor = Color.white;
    [SerializeField] private Color normalColor = new Color(0.66f, 0.66f, 0.68f, 1f);
    [SerializeField] private Color glowColor = new Color(0.42f, 0.92f, 0.68f, 1f);
    [SerializeField, Range(0f, 3f)] private float glowIntensity = 1.5f;
    [SerializeField, Range(4f, 64f)] private float glowPadding = 33.8f;
    [SerializeField] private Color hoverColor = new Color(0.42f, 0.92f, 0.68f, 0.5f);

    private readonly List<TextMeshProUGUI> _filterTabLabels = new List<TextMeshProUGUI>();
    private SkyPrisonTabGlowRenderer _glowRenderer;
    private int _selectedFilterTab = -1;

    public InventoryFilterTab CurrentFilter { get; private set; } = InventoryFilterTab.All;

    /// <summary>供 SkyPrisonInventoryTabHover 查询：选中项不被 hover 着色覆盖。</summary>
    public bool IsTabSelected(int index) => _selectedFilterTab == index;

    // ── 内部状态 ──────────────────────────────────────────────────────────

    private readonly List<Button>    _tabButtons = new List<Button>();
    private readonly List<Image>     _tabLockIcons = new List<Image>();
    private int _currentPage = 0;
    public int CurrentPage => _currentPage; // 供 StashInventoryGamepad 解析当前页的 InventoryRuntime

    private SkyPrisonWindowManager_V1 _manager;

    // ── 生命周期 ──────────────────────────────────────────────────────────

    // 2026-07-23：之前这几步是顺序直接调用的——只要前面任何一步抛异常，Awake() 就会被
    // 整个中断，后面的步骤全部不会执行。实测出现过 CanvasGroup 相关的异常在 SelectPage
    // 里抛出，导致 OpenInventoryAlongside() 从未执行——表现为"仓库开出来了，但背包死活
    // 不跟着开"，看起来像是背包联动坏了，其实是前面某一步半路炸了。每一步互相独立、
    // 互不依赖对方是否成功，所以拆开各自 try/catch，一步失败不该连累其它步骤都不跑。
    protected override void OnWindowOpen()
    {
        RunStep(BuildPageTabs, nameof(BuildPageTabs));
        RunStep(() => SelectPage(0), nameof(SelectPage));
        RunStep(SetupFilterTabs, nameof(SetupFilterTabs));
        RunStep(OpenInventoryAlongside, nameof(OpenInventoryAlongside));
        RunStep(SetupSortControls, nameof(SetupSortControls));
        RunStep(SetupInteraction, nameof(SetupInteraction));
    }

    // ── 拖拽交互 ──────────────────────────────────────────────────────────
    // 挂在 StashPanel 上(不是这个根节点)——StashInventoryInteraction 自己的
    // transform 要能直接当成仓库面板的 RectTransform 用(落点判定)，跟背包
    // SkyPrisonInventoryInteraction 挂在 InventoryPanel 上是同一个道理。
    private void SetupInteraction()
    {
        Transform panel = transform.Find("StashPanel");
        if (panel == null) return;

        var interaction = panel.GetComponent<SkyPrison.Runtime.UI.StashInventoryInteraction>()
                       ?? panel.gameObject.AddComponent<SkyPrison.Runtime.UI.StashInventoryInteraction>();
        interaction.SetInventorySource(() => StashRuntime.Instance?.GetPage(_currentPage));

        // 物品详情面板——鼠标悬停格子看描述，跟背包复用同一个控制器(InventoryItemDetailController)，
        // 只是把它解析"当前生效背包"的默认逻辑换成"当前选中的仓库页"。
        var detail = panel.GetComponent<SkyPrison.Runtime.UI.InventoryItemDetailController>()
                  ?? panel.gameObject.AddComponent<SkyPrison.Runtime.UI.InventoryItemDetailController>();
        detail.SetInventorySource(() => StashRuntime.Instance?.GetPage(_currentPage));

        // 手柄交互——跟 StashInventoryInteraction 同一个物体上，自愈式接线。
        if (panel.GetComponent<SkyPrison.Runtime.UI.StashInventoryGamepad>() == null)
            panel.gameObject.AddComponent<SkyPrison.Runtime.UI.StashInventoryGamepad>();
    }

    private void RunStep(System.Action step, string stepName)
    {
        try { step(); }
        catch (System.Exception e)
        {
            Debug.LogError($"[Stash] OnWindowOpen 的 {stepName} 步骤抛出异常，已跳过，" +
                $"其它步骤不受影响：{e}", this);
        }
    }

    // 背包<->仓库关闭联动现在统一在 SkyPrisonWindowManager_V1.Close() 里做（那是唯一的
    // 公共出口，不管从关闭按钮/B键/ESC哪条路径关，联动都会生效），这里不需要重复处理。
    protected override void OnWindowClose() { }

    // ── 排序/整理 ─────────────────────────────────────────────────────────
    // 复用背包同一套排序控件组件(SkyPrisonInventorySortControls)，运行时自愈——跟
    // InventoryWindowController.Awake() 的自动接线套路一样。唯一的区别是仓库的
    // "当前生效数据"会随页签切换而变，必须用 SetInventorySourceOverride 传一个每次都
    // 重新取当前页的委托，不能像背包那样只解析一次就缓存住。
    private void SetupSortControls()
    {
        var sort = GetComponent<SkyPrison.Runtime.UI.SkyPrisonInventorySortControls>()
                ?? gameObject.AddComponent<SkyPrison.Runtime.UI.SkyPrisonInventorySortControls>();
        sort.SetInventorySourceOverride(() => StashRuntime.Instance?.GetPage(_currentPage));
    }

    // ── 筛选标签（全部/消耗品/材料/装备/任务/重要）────────────────────────────
    // 跟 InventoryWindowController.SetupFilterTabs 同一套逻辑（辉光渲染器/hover 组件都是
    // 直接复用的通用组件），只是最终把过滤结果转发给 stashGridView.ApplyFilter 而不是
    // InventoryGridView，且不需要背包那边"强制装备槽/快捷槽过滤"的特殊分支——仓库不会
    // 被角色面板呼出，不存在那两种场景。

    private void SetupFilterTabs()
    {
        Transform filterBar = FindDeep(transform, "FilterBar");
        var tabs = new List<Transform>();
        if (filterBar != null)
            foreach (Transform child in filterBar)
                if (child.name.StartsWith("FilterTab"))
                    tabs.Add(child);

        if (tabFont == null || filterBar == null || tabs.Count == 0) return;

        // 挂在 StashPanel 上（FilterBar 的直接父节点），不能挂在 gameObject(prefab 根节点)
        // 上——StashInventoryTabGlowRenderer 用"自己所在节点"当坐标换算的稳定祖先，
        // 真正被拖拽移动的是 StashPanel 本身，不是根节点；挂在根节点上时，换算出来的
        // 位置只在生成那一刻正确，之后拖动窗口这条线就不会跟着动了。
        Transform stashPanel = filterBar.parent;
        _glowRenderer = stashPanel.GetComponent<SkyPrisonTabGlowRenderer>()
                     ?? stashPanel.gameObject.AddComponent<SkyPrisonTabGlowRenderer>();
        _glowRenderer.Configure(tabFont, glowColor, glowIntensity, glowPadding);

        _filterTabLabels.Clear();
        for (int i = 0; i < tabs.Count; i++)
        {
            Transform tab = tabs[i];
            TextMeshProUGUI tmp = tab.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp == null) continue;
            _filterTabLabels.Add(tmp);

            Button btn = tab.GetComponent<Button>() ?? tab.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            int index = i; // 闭包捕获
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => SelectFilterTab(index));

            var hover = tab.GetComponent<SkyPrisonInventoryTabHover>()
                     ?? tab.gameObject.AddComponent<SkyPrisonInventoryTabHover>();
            hover.Configure(IsTabSelected, index, tmp, normalColor, hoverColor);
        }

        SelectFilterTab(0);
    }

    public void SelectFilterTab(int index)
    {
        if (index < 0 || index >= _filterTabLabels.Count) return;
        if (index != _selectedFilterTab) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        _selectedFilterTab = index;

        for (int i = 0; i < _filterTabLabels.Count; i++)
        {
            TextMeshProUGUI tmp = _filterTabLabels[i];
            if (tmp == null) continue;
            tmp.color = (i == index) ? selectedFaceColor : normalColor;
            tmp.fontSharedMaterial = tabFont.material;
        }

        if (_glowRenderer != null)
            _glowRenderer.ShowFor(_filterTabLabels[index]);

        CurrentFilter = (InventoryFilterTab)Mathf.Clamp(index, 0, 5);
        stashGridView?.ApplyFilter(CurrentFilter);
    }

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

    // ── 页签构建 ──────────────────────────────────────────────────────────

    private void BuildPageTabs()
    {
        if (pageTabContainer == null) return;

        _tabButtons.Clear();
        _tabLockIcons.Clear();

        for (int i = 0; i < pageTabContainer.childCount; i++)
        {
            Transform tab = pageTabContainer.GetChild(i);
            Button btn = tab.GetComponent<Button>() ?? tab.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            SkyPrisonUIButtonFeedback.Attach(tab.gameObject);

            int pageIndex = i; // 闭包捕获
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => SelectPage(pageIndex));

            _tabButtons.Add(btn);

            // 锁图标（子节点名为 "LockIcon"，未解锁时显示）
            Transform lockTf = tab.Find("LockIcon");
            _tabLockIcons.Add(lockTf != null ? lockTf.GetComponent<Image>() : null);
        }

        RefreshTabStates();
    }

    private void RefreshTabStates()
    {
        int unlocked = StashRuntime.Instance?.UnlockedPages ?? 1;

        for (int i = 0; i < _tabButtons.Count; i++)
        {
            bool isUnlocked = i < unlocked;
            // 之前这里把未解锁页签的按钮设成 interactable=false——Unity的Button在
            // interactable=false时根本不会触发onClick，点击事件压根传不到SelectPage()
            // 里，那句"未解锁播放Forbidden提示音"的代码因此从来没被执行过，点上去
            // 要么没声音要么被别的东西接住播了别的音效。按钮必须一直保持可点，
            // 靠SelectPage()内部自己判断解锁状态来决定放行还是播拒绝音效，锁头图标
            // 只做纯视觉提示，不能真的挡掉点击。
            _tabButtons[i].interactable = true;

            if (_tabLockIcons.Count > i && _tabLockIcons[i] != null)
                _tabLockIcons[i].gameObject.SetActive(!isUnlocked);

            // 未解锁时数字和锁头图标叠在一起很挤/难看——锁着的页只显示锁头，
            // 数字直接隐藏；解锁了再把数字放回来。
            Transform label = _tabButtons[i].transform.Find("Label");
            if (label != null) label.gameObject.SetActive(isUnlocked);
        }
    }

    // ── 页切换 ────────────────────────────────────────────────────────────

    public void SelectPage(int pageIndex)
    {
        if (StashRuntime.Instance == null) return;
        if (!StashRuntime.Instance.IsPageUnlocked(pageIndex))
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            return;
        }

        if (pageIndex != _currentPage)
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);

        _currentPage = pageIndex;

        // 把格子视图绑定到对应页的 InventoryRuntime
        InventoryRuntime page = StashRuntime.Instance.GetPage(pageIndex);
        stashGridView?.BindInventory(page);

        // 高亮当前页签
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            if (_tabButtons[i] == null) continue;
            // 简单用 alpha 区分选中/未选中，视觉细节由 prefab 的 Tab 样式决定。
            // 用 TryGetComponent 而不是 GetComponent<T>() ?? AddComponent<T>()——后者
            // 在 Unity Object 上用 ?? 有已知的"假空"陷阱(??是原始引用比较，不走 Unity
            // 重载过的 == )，TryGetComponent 是官方推荐的、不会有这个陷阱的安全写法。
            if (!_tabButtons[i].gameObject.TryGetComponent(out CanvasGroup cg))
                cg = _tabButtons[i].gameObject.AddComponent<CanvasGroup>();
            cg.alpha = (i == _currentPage) ? 1f : 0.55f;
        }
    }

    // 手柄翻页
    public void PreviousPage() => SelectPage(Mathf.Max(0, _currentPage - 1));
    public void NextPage()     => SelectPage(Mathf.Min(StashRuntime.MaxPages - 1, _currentPage + 1));

    // L1/R1 翻页的输入轮询搬到 StashInventoryGamepad 里去了（跟格子导航/A/B/X/
    // L2/R2/筛选切换统一由一个组件管，不再各管各的）——这里如果还留着一份 Update
    // 读同样的按键，会跟新组件同一帧各触发一次 PreviousPage/NextPage，一次按键
    // 翻两页。

    // ── 打开背包 ──────────────────────────────────────────────────────────

    private void OpenInventoryAlongside()
    {
        if (inventoryWindowPrefab == null)
        {
            Debug.LogWarning("[Stash] inventoryWindowPrefab 没有绑定，背包不会跟着仓库一起打开——" +
                "去 Inspector 里的 StashWindowController 组件「同时打开背包」栏检查一下这个引用是否为空，" +
                "为空的话重新跑一次 Tools/Sky Prison/UI/Create Stash Window 应该会自动补上。", this);
            return;
        }
        if (_manager == null) _manager = FindObjectOfType<SkyPrisonWindowManager_V1>();
        if (_manager == null) return;

        if (!_manager.IsOpen("inventory"))
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
            _manager.Open(inventoryWindowPrefab);
        }
    }
}
