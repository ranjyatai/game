using System.Collections;
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 玩家任务日志——按 L 键打开。窗口开关/拖拽/关闭按钮/角标/磨砂背景全部走
/// SkyPrisonFloatingWindowKit(跟角色面板/背包同一套规格)，内容部分(清单勾选风格)
/// 是这个面板专属设计，不跟其它窗口共用。
///
/// 数据直接读 QuestRuntime.GetActiveQuests()，不新建一套任务状态缓存——目标文字/
/// 进度解析也复用 QuestObjectiveTextResolver(跟编辑器任务页预览同一份逻辑)，保证
/// 玩家在游戏里看到的文字和策划在编辑器里预览的文字永远一致。
/// </summary>
public class QuestLogController : MonoBehaviour
{
    private const int SortingOrder = 31850;

    private static QuestLogController _instance;
    public static bool IsOpen => _instance != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStaticState()
    {
        _instance = null;
    }

    public static void Show()
    {
        if (_instance != null) return;
        var go = new GameObject("[QuestLog]");
        var ui = go.AddComponent<QuestLogController>();
        _instance = ui;
        ui.Build();
    }

    public static void Hide()
    {
        if (_instance != null) _instance.Close();
    }

    public static void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    // ── 布局常量(跟角色面板同一套倍率基准) ───────────────────────────────────
    private const float M = SkyPrisonFloatingWindowKit.StandardScaleMultiplier;
    private const float BoxWidth  = 2000f * M;
    private const float BoxHeight = 1150f * M;
    private const float Padding   = 32f * M;
    private const float QuestGap  = 28f * M;
    private const float TitleFontSize = SkyPrisonFloatingWindowKit.PrimaryFontSize;
    private const float ObjectiveFontSize = SkyPrisonFloatingWindowKit.DescriptionFontSize;
    private const float TabBarHeight = ObjectiveFontSize * 2f;

    private enum QuestLogTab { InProgress, Completed }
    private QuestLogTab _currentTab = QuestLogTab.InProgress;

    private TMP_Text _tabInProgressText;
    private TMP_Text _tabCompletedText;

    // 每隔这么久重新拉一次任务列表——目标进度(击杀计数/物品数量)不是事件驱动的，
    // 用轮询保证面板开着的时候数字会自己跟着涨，不用玩家关了再开才刷新。
    private const float RefreshInterval = 0.5f;

    // 富文本染色用的十六进制——跟 SkyPrisonUIPalette.ColdGreen (0.42,0.92,0.68) 同一个
    // 颜色，只是 TMP 的 <color=#RRGGBB> 标签需要十六进制字符串，不能直接传 Color。
    private const string ColdGreenHex = "6BEBAD";

    private RectTransform _boxRt;
    private RectTransform _listContent;
    private TMP_FontAsset _font;
    private UILocalizationTable _locTable;
    private SkyPrisonBlurUVTracker _blurUvTracker;

    /// <summary>展开态卡片里"当前目标"/分割线/区域-委托人要对齐到节点链第一个节点的
    /// 实际位置——量这个位置必须等RebuildQuestList末尾那次"强制跑完一整轮布局"
    /// (见那边的注释)跑完之后才准，卡片构建过程中量到的全是Unity RectTransform
    /// 还没被任何布局系统碰过的默认值(100x100)。所以构建时只记下要对齐的对象，
    /// 真正测量+赋值挪到那次统一刷新之后做，按每张卡片记一条。</summary>
    private readonly List<NodeAlignmentTarget> _pendingNodeAlignments = new List<NodeAlignmentTarget>();

    private struct NodeAlignmentTarget
    {
        public RectTransform headerRt;
        public RectTransform nodeChainRt;
        public VerticalLayoutGroup currentLayout;
        public HorizontalLayoutGroup dividerLayout;
        public HorizontalLayoutGroup infoRowLayout;
    }

    // ── 悬浮查询卡("贾维斯"式)：鼠标停在讨伐/收集目标里的敌人/物品名字上，瞬间弹出
    // 图标+名字+简讯，不是完整窗口——没有角标/磨砂背景/开窗动画，跟随鼠标出现/消失。
    private readonly List<(TMP_Text text, QuestObjectiveTextResolver.AutoInfo info, QuestObjectiveTextResolver.Progress progress)> _linkableTexts = new();
    private RectTransform _hoverCardRt;
    private Image _hoverCardIcon;
    private TMP_Text _hoverCardTitle;
    private TMP_Text _hoverCardSub;
    private TMP_Text _hoverCardDesc;
    private SkyPrisonBlurUVTracker _hoverCardBlurTracker;
    private bool _hoverCardVisible;
    private Coroutine _hoverCardAnimCoroutine;
    private string _hoverCardLastName;
    private string _hoverCardLastDesc;
    private const float HoverCardIconSize = 84f * M;
    private const float HoverCardWidth = 640f * M;

    // ── 放弃任务确认框：按Delete键对当前展开的任务发起放弃。跟接受任务确认框
    // (NPCDialogueWindowController)同一套结构——半透明背景自己就是raycast挡板，
    // box是它的子物体，按钮天然叠在背景之上，不会有"兄弟节点sibling顺序万一
    // 排反了盖住按钮点不了"这种风险。窗口标配：黑白高斯模糊+白色角标+透明底白框
    // 按钮，危险操作只在标题/按钮文字颜色上体现，不改边框/角标颜色。
    private GameObject _abandonConfirmRootGo; // 背景根，Show/Hide切它的active
    private RectTransform _abandonConfirmRt; // box，参与开关缩放动画
    private SkyPrisonBlurUVTracker _abandonConfirmBlurTracker;
    private TMP_Text _abandonConfirmMessageText;
    private bool _abandonConfirmVisible;
    private Coroutine _abandonConfirmAnimCoroutine;
    private QuestDefinition _pendingAbandonQuest;
    private const float AbandonConfirmWidth = 720f * M;

    private bool _savedExternalBlock;
    private bool _hudHideRequestCounted;
    private bool _hudShouldBeHidden;
    private SkyPrisonWindowManager_V1 _windowManagerForHud;

    private float _refreshTimer;

    // 展开/收起(看详情) 跟 追踪中任务/追踪 以前是绑在一起的同一个动作(点卡片=两个
    // 一起切)，用户明确要求拆开成两件独立的事——展开只是"我想看这条任务的详情"，
    // 不该顺带把它设成追踪中任务；追踪改用专门的快捷键(T)，只在这条任务当前展开时
    // 才对它生效。这个字段记的是"UI上现在展开看详情的是哪一条"，跟
    // QuestRuntime.trackedQuestId(存档里的追踪中任务)是两个完全独立的状态。
    private string _expandedQuestId = "";

    private string L(string key, string fallback) =>
        _locTable != null ? _locTable.Get(key, fallback) : fallback;

    private void Update()
    {
        SkyPrisonFloatingWindowKit.DriveHintBarAvoidance(_boxRt, _cornersBuffer);

        _refreshTimer -= Time.unscaledDeltaTime;
        if (_refreshTimer <= 0f)
        {
            _refreshTimer = RefreshInterval;
            RebuildQuestList();
        }

        // 放弃任务确认框开着的时候，底下任务列表的悬浮查询卡不该跟着鼠标继续弹出——
        // 确认框是模态操作，背后的内容应该整个被"锁住"，FindIntersectingLink只看
        // 屏幕坐标不管上面盖没盖东西，不主动跳过的话悬浮卡会穿模态框一直显示。
        if (_abandonConfirmVisible)
            HideHoverCard();
        else
            UpdateHoverCard();

        UpdateAbandonQuestHotkey();
        UpdateTrackQuestHotkey();
        UpdateCardFocusVisuals();
    }

    /// <summary>键盘/手柄光标(_cardNav)停在哪张卡片上，就把那张卡片的背景调亮一点，
    /// 离开的卡片恢复原来的基础透明度——DrawHighlight已经关掉(见Build()那边的注释)，
    /// 这是它的替代视觉，只在真的换焦点的那一帧才动，不用每帧都重设所有卡片颜色。</summary>
    private void UpdateCardFocusVisuals()
    {
        if (_cardNav == null || _cardFocusEntries.Count == 0) return;

        // 纯鼠标玩家没碰过键盘/手柄的话，_focus也会停在一个默认值(比如0)，
        // CurrentFocusedButton本身不区分"这是真的导航过来的"还是"只是没人管的
        // 默认值"——不查IsGamepadMode的话，纯鼠标玩家会看到第一张卡片背景莫名
        // 其妙比其它卡片亮一点。
        Button focused = _cardNav.IsGamepadMode ? _cardNav.CurrentFocusedButton : null;
        if (focused == _lastCardFocusButton) return;
        _lastCardFocusButton = focused;

        const float FocusAlphaBoost = 0.05f;
        foreach (var entry in _cardFocusEntries)
        {
            if (entry.bg == null) continue;
            bool isFocused = entry.button == focused;
            Color c = entry.bg.color;
            c.a = isFocused ? entry.baseAlpha + FocusAlphaBoost : entry.baseAlpha;
            entry.bg.color = c;
        }
    }

    // 按Delete键放弃"当前展开看详情的这条任务"——跟T键(追踪)统一用同一个目标
    // 概念(_expandedQuestId)，不再对着"追踪中任务"(GetTrackedQuest)。展开与追踪已经
    // 拆成两件独立的事，放弃任务作为窗口里第三个操作，也应该跟着展开态走，
    // 不然玩家会遇到"光标明明停在A任务上，按Delete却弹出放弃B任务(追踪中任务)的
    // 确认框"这种目标不一致的体验。只允许对"进行中"的任务生效——已完成的任务
    // 没有"放弃"这个操作。
    private void UpdateAbandonQuestHotkey()
    {
        if (_abandonConfirmVisible) return;
        if (!Input.GetKeyDown(KeyCode.Delete)) return;
        if (string.IsNullOrEmpty(_expandedQuestId)) return;

        QuestRuntime runtime = QuestRuntime.Instance;
        if (runtime == null || !runtime.IsActive(_expandedQuestId)) return;

        QuestDefinition expanded = runtime.GetQuestById(_expandedQuestId);
        if (expanded == null) return;

        ShowAbandonConfirmPopup(expanded);
    }

    // 按T键切换"当前展开看详情的这条任务"的追踪中任务/追踪状态——展开与追踪已经
    // 拆成两件独立的事(点卡片只负责展开/收起，不再顺带切追踪)，这个快捷键是
    // 追踪唯一的入口。没有展开任何一条时按了直接no-op，不报错也不提示，跟
    // 放弃任务确认框开着时不重复触发是同一个"安静地什么都不做"的处理方式。
    // 只允许对"进行中"的任务生效——已完成的任务追踪没有意义(没有HUD可指向的
    // 目标)，且QuestRuntime.CompleteQuest/AbandonQuest本来就会在任务不再进行中
    // 时自动摘掉trackedQuestId，这里主动挡一道，不让玩家对着已完成任务重新
    // 设置一个马上又会被摘掉的追踪状态。
    private void UpdateTrackQuestHotkey()
    {
        if (_abandonConfirmVisible) return;
        if (!Input.GetKeyDown(KeyCode.T)) return;
        if (string.IsNullOrEmpty(_expandedQuestId)) return;

        QuestRuntime runtime = QuestRuntime.Instance;
        if (runtime == null || !runtime.IsActive(_expandedQuestId)) return;

        QuestDefinition expanded = runtime.GetQuestById(_expandedQuestId);
        if (expanded == null) return;

        bool alreadyTracked = runtime.GetTrackedQuestId() == _expandedQuestId;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        runtime.SetTrackedQuest(alreadyTracked ? null : expanded);
        RebuildQuestList();
    }

    // 悬停检测连续"没找到"多久了才真的算移开——每0.5秒任务列表整个重建一次，
    // 日志证实重建那一帧FindIntersectingLink会偶尔检测不到链接(哪怕鼠标纹丝不动)，
    // 之前"检测不到立刻HideHoverCard"导致卡片跟着这个刷新节奏一直收起展开
    // (用户反馈"它始终在收起展开收起展开")。给一点点容错时间，扛过这种单帧抖动，
    // 鼠标真的移开这么久肯定早就超过这个阈值了，不影响正常的"移开即消失"体验。
    private const float HoverMissGrace = 0.15f;
    private float _hoverMissTimer;

    // 每帧检测鼠标是否停在某个目标名字的TMP链接标签上——瞬时查询，不需要
    // 停留延迟(那是给悬浮提示用的，"贾维斯"式查询要的是立刻响应)。
    private void UpdateHoverCard()
    {
        for (int i = 0; i < _linkableTexts.Count; i++)
        {
            var (text, info, progress) = _linkableTexts[i];
            if (text == null || !text.gameObject.activeInHierarchy) continue;

            int linkIndex = TMP_TextUtilities.FindIntersectingLink(text, Input.mousePosition, null);
            if (linkIndex < 0) continue;

            _hoverMissTimer = 0f;
            ShowHoverCard(info, progress, Input.mousePosition);
            return;
        }

        _hoverMissTimer += Time.unscaledDeltaTime;
        if (_hoverMissTimer >= HoverMissGrace)
            HideHoverCard();
    }

    // 小浮窗——图标+名字算H1(TitleFontSize)，进度/简介算H2(ObjectiveFontSize，项目里
    // 已建立的最小字号档位)。
    //
    // 排版不用嵌套VerticalLayoutGroup+ContentSizeFitter那套——背包的物品详情面板
    // (InventoryItemDetailPanel)早就趟过这条路然后放弃了：那套嵌套自动布局在"文字
    // 内容频繁变化+同时还有缩放动画"的场景下会互相干扰算不出稳定结果，表现为高度
    // 一直变、卡片跳动。物品详情面板改成了"每个元素手动摆固定位置，只有简介这一个
    // 真正需要动态高度的元素单独测量"，这里直接照抄同一套做法：每个元素都是
    // _hoverCardRt的直接子物体、锚点用左上角定点锚(不是拉伸锚)、位置/尺寸都用
    // 显式的anchoredPosition+sizeDelta摆好，唯独Desc这个元素为了支持变长文字换行，
    // TMP组件直接挂在它自己身上(不是嵌套在子物体里)配合ContentSizeFitter，量出来的
    // 实际高度手动决定下面还有什么、以及整张卡片总高度——见ShowHoverCard里的
    // RecomputeHoverCardHeight。
    private const float HoverCardPadX = 16f * M;
    private const float HoverCardPadY = 12f * M;
    private const float HoverCardRowGap = 6f * M;
    private const float HoverCardHeadIconGap = 12f * M;
    private const float HoverCardSubHeight = ObjectiveFontSize * 1.3f;

    private void EnsureHoverCard()
    {
        if (_hoverCardRt != null) return;

        var cardGo = new GameObject("HoverCard", typeof(RectTransform));
        cardGo.transform.SetParent(transform, false); // 挂在Canvas根下，不跟着任务列表滚动
        _hoverCardRt = (RectTransform)cardGo.transform;
        _hoverCardRt.pivot = new Vector2(0f, 1f);
        _hoverCardRt.sizeDelta = new Vector2(HoverCardWidth, HoverCardPadY * 2f + HoverCardIconSize);

        // 背景跟主窗口同一套磨砂黑白模糊(BuildBlurBackground)，不是纯色深色底——
        // 用户明确说"不要加深色，只需要黑白+高斯模糊"，挡住下面场景内容是可以接受的，
        // 只是视觉风格要跟其它窗口一致。这里没有任何LayoutGroup控制_hoverCardRt的
        // 子物体，"Blur"这个子物体自己就是(0,0)-(1,1)满铺锚点，不会被排版系统挤扁，
        // 不需要额外的ignoreLayout处理。
        SkyPrisonFloatingWindowKit.BuildBlurBackground(this, _hoverCardRt, out _hoverCardBlurTracker);

        // 冷绿是强调色，留给"选中/追踪中任务"这类真正需要突出的场景——这张悬浮卡
        // 只是个独立浮窗，边界用普通白色细描边就够了，跟卡片没有被追踪时的
        // 普通边框(BuildCardOutline里isTracked=false那个分支)同一个道理。
        SkyPrisonFloatingWindowKit.AddOutline(_hoverCardRt, new Color(1f, 1f, 1f, 0.25f), 1.5f);

        float rowWidth = HoverCardWidth - HoverCardPadX * 2f;

        var iconRt = SkyPrisonFloatingWindowKit.MakeRect("Icon", _hoverCardRt, Vector2.zero, Vector2.zero);
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 1f);
        iconRt.pivot = new Vector2(0f, 1f);
        iconRt.sizeDelta = new Vector2(HoverCardIconSize, HoverCardIconSize);
        iconRt.anchoredPosition = new Vector2(HoverCardPadX, -HoverCardPadY);
        _hoverCardIcon = iconRt.gameObject.AddComponent<Image>();
        _hoverCardIcon.preserveAspect = true;
        _hoverCardIcon.raycastTarget = false;

        float titleX = HoverCardPadX + HoverCardIconSize + HoverCardHeadIconGap;
        var titleRt = SkyPrisonFloatingWindowKit.MakeRect("Title", _hoverCardRt, Vector2.zero, Vector2.zero);
        titleRt.anchorMin = titleRt.anchorMax = new Vector2(0f, 1f);
        titleRt.pivot = new Vector2(0f, 1f);
        titleRt.sizeDelta = new Vector2(rowWidth - HoverCardIconSize - HoverCardHeadIconGap, HoverCardIconSize);
        titleRt.anchoredPosition = new Vector2(titleX, -HoverCardPadY);
        _hoverCardTitle = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        _hoverCardTitle.fontSize = TitleFontSize;
        _hoverCardTitle.fontStyle = FontStyles.Bold;
        _hoverCardTitle.alignment = TextAlignmentOptions.MidlineLeft;
        _hoverCardTitle.color = Color.white;
        _hoverCardTitle.enableWordWrapping = false;
        _hoverCardTitle.overflowMode = TextOverflowModes.Overflow;
        _hoverCardTitle.raycastTarget = false;
        if (_font != null) _hoverCardTitle.font = _font;

        var subRt = SkyPrisonFloatingWindowKit.MakeRect("Sub", _hoverCardRt, Vector2.zero, Vector2.zero);
        subRt.anchorMin = subRt.anchorMax = new Vector2(0f, 1f);
        subRt.pivot = new Vector2(0f, 1f);
        subRt.sizeDelta = new Vector2(rowWidth, HoverCardSubHeight);
        subRt.anchoredPosition = new Vector2(HoverCardPadX, -(HoverCardPadY + HoverCardIconSize + HoverCardRowGap));
        _hoverCardSub = subRt.gameObject.AddComponent<TextMeshProUGUI>();
        _hoverCardSub.fontSize = ObjectiveFontSize;
        _hoverCardSub.alignment = TextAlignmentOptions.TopLeft;
        _hoverCardSub.color = new Color(SkyPrisonUIPalette.ColdGreen.r, SkyPrisonUIPalette.ColdGreen.g, SkyPrisonUIPalette.ColdGreen.b, 1f);
        _hoverCardSub.enableWordWrapping = false;
        _hoverCardSub.overflowMode = TextOverflowModes.Overflow;
        _hoverCardSub.raycastTarget = false;
        if (_font != null) _hoverCardSub.font = _font;

        // 简介文——用户明确要求"至少要有名字/头像/介绍文"，而且要能完整显示、字号
        // 不能低于ObjectiveFontSize这个项目里已建立的最小字号档位。TMP组件直接挂在
        // descRt自己身上(不是靠MakeText另建一个子物体承载文字)，ContentSizeFitter
        // 才能读到同一个物体上的这份ILayoutElement，量出正确的换行后高度——这是跟
        // 物品详情面板那个_descText同款的接法。宽度用sizeDelta.x固定死(点锚点，
        // 不是拉伸锚，不会有sizeDelta跟拉伸锚互相打架的问题)，只让高度跟着内容变。
        var descRt = SkyPrisonFloatingWindowKit.MakeRect("Desc", _hoverCardRt, Vector2.zero, Vector2.zero);
        descRt.anchorMin = descRt.anchorMax = new Vector2(0f, 1f);
        descRt.pivot = new Vector2(0f, 1f);
        descRt.sizeDelta = new Vector2(rowWidth, 0f);
        _hoverCardDesc = descRt.gameObject.AddComponent<TextMeshProUGUI>();
        _hoverCardDesc.fontSize = ObjectiveFontSize * 1.3f; // 比最小字号档位大一档，跟InfoRow那批"字号发虚"调整同一个道理
        _hoverCardDesc.alignment = TextAlignmentOptions.TopLeft;
        _hoverCardDesc.color = new Color(1f, 1f, 1f, 0.75f);
        _hoverCardDesc.enableWordWrapping = true;
        _hoverCardDesc.overflowMode = TextOverflowModes.Overflow;
        _hoverCardDesc.raycastTarget = false;
        if (_font != null) _hoverCardDesc.font = _font;
        var descFitter = descRt.gameObject.AddComponent<ContentSizeFitter>();
        descFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        descFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        cardGo.SetActive(false);
    }

    /// <summary>Desc要么隐藏、要么按实际换行结果量出高度——量完之后才知道卡片
    /// 总高度该给多少，直接写回_hoverCardRt.sizeDelta，不依赖任何自动布局系统
    /// 帮忙"顺便"算出这个数字。</summary>
    private void RecomputeHoverCardHeight(bool hasDesc)
    {
        float contentBottom = HoverCardPadY + HoverCardIconSize + HoverCardRowGap + HoverCardSubHeight;
        float totalHeight;
        if (hasDesc)
        {
            float descY = contentBottom + HoverCardRowGap;
            var descRt = (RectTransform)_hoverCardDesc.transform;
            descRt.anchoredPosition = new Vector2(HoverCardPadX, -descY);
            LayoutRebuilder.ForceRebuildLayoutImmediate(descRt);
            float descHeight = descRt.rect.height;
            totalHeight = descY + descHeight + HoverCardPadY;
        }
        else
        {
            totalHeight = contentBottom + HoverCardPadY;
        }

        _hoverCardRt.sizeDelta = new Vector2(HoverCardWidth, totalHeight);
    }

    private void ShowHoverCard(QuestObjectiveTextResolver.AutoInfo info, QuestObjectiveTextResolver.Progress progress, Vector3 screenPos)
    {
        EnsureHoverCard();

        // 只在真正"从没显示到显示"这个切换瞬间播开卡动画——同一次悬停里每帧都会
        // 调这个方法(刷新内容位置)，不能每帧都重新播一次动画，那样会卡在动画
        // 开头出不来。已经在显示中途换悬停目标(比如划过另一个名字)也不重播，
        // 只有真正从隐藏变显示才折叠打开。
        if (!_hoverCardVisible)
        {
            _hoverCardVisible = true;
            _hoverCardRt.gameObject.SetActive(true);
            _hoverCardRt.localScale = new Vector3(1f, 0f, 1f);
            if (_hoverCardAnimCoroutine != null) StopCoroutine(_hoverCardAnimCoroutine);
            _hoverCardAnimCoroutine = StartCoroutine(HoverCardOpenAnimation());
        }

        _hoverCardIcon.sprite = info.Icon;
        _hoverCardIcon.enabled = info.Icon != null;
        _hoverCardTitle.text = info.Name;
        _hoverCardSub.text = progress.Resolved
            ? $"{L("questlog_hover_progress", "进度")} {Mathf.Min(progress.Current, progress.Target)}/{progress.Target}"
            : "";

        bool hasDesc = !string.IsNullOrWhiteSpace(info.Description);
        _hoverCardDesc.gameObject.SetActive(hasDesc);
        if (hasDesc)
            _hoverCardDesc.text = info.Description.Trim();

        // 只有名字/介绍文真的换了(换悬停目标)才需要重新量一次高度——进度数字
        // 每帧都可能变但不影响换行行数，没必要跟着每帧都重算。悬停同一个目标不动
        // 的时候这里是纯粹的空转，不会再有高度算不稳定、卡片跳动的问题——高度
        // 完全由RecomputeHoverCardHeight手动决定，不依赖任何自动布局系统。
        if (info.Name != _hoverCardLastName || info.Description != _hoverCardLastDesc)
        {
            _hoverCardLastName = info.Name;
            _hoverCardLastDesc = info.Description;
            RecomputeHoverCardHeight(hasDesc);
        }

        // 卡片贴着鼠标右下方浮动，不挡住鼠标正指着的那个字。
        _hoverCardRt.position = screenPos + new Vector3(24f * M, -24f * M, 0f);
    }

    private void HideHoverCard()
    {
        if (!_hoverCardVisible) return; // 已经是隐藏状态，不用每帧重复关一次
        _hoverCardVisible = false;
        if (_hoverCardRt == null) return;
        if (_hoverCardAnimCoroutine != null) StopCoroutine(_hoverCardAnimCoroutine);
        _hoverCardAnimCoroutine = StartCoroutine(HoverCardCloseAnimation());
    }

    // 跟QuestLogController自己开窗/关窗那套(OpenBoxAnimation/CloseBoxAnimation)同一个
    // 折叠动画公式，pivot=(0,1)左上角锚点，缩放Y轴从鼠标那个点往下"展开"/收回去，
    // 不是从窗口中心放大缩小。
    private IEnumerator HoverCardOpenAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_hoverCardRt == null) yield break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - (1f - p) * (1f - p);
            _hoverCardRt.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_hoverCardRt != null) _hoverCardRt.localScale = Vector3.one;
    }

    private IEnumerator HoverCardCloseAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_hoverCardRt == null) yield break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - p * p;
            _hoverCardRt.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_hoverCardRt != null)
        {
            _hoverCardRt.gameObject.SetActive(false);
            _hoverCardRt.localScale = Vector3.one;
        }
    }

    // ── 放弃任务确认框 ────────────────────────────────────────────────────
    // 完整照抄PauseMenuController.ShowReturnMenuConfirm("返回主菜单？"二次确认框)
    // 那一套结构——同样是"关卡内会丢失进度的破坏性操作，需要二次确认"，项目里
    // 已经有一份验证过能正常显示/点击的实现，不用另起一套。关键是标题/正文/按钮
    // 全部用锚点百分比(anchorMin/anchorMax)分区摆放，不是靠LayoutGroup自动堆叠
    // 算高度——LayoutGroup那版每一块的preferredHeight是我自己拍的，跟实际文字
    // 需要的高度对不上，导致标题/正文/按钮挤在一起互相重叠。

    private void EnsureAbandonConfirmPopup()
    {
        if (_abandonConfirmRt != null) return;

        // 根节点自己就是半透明背景+raycast挡板，box是它的子物体(不是平级兄弟)——
        // 按钮永远叠在背景之上，不存在"兄弟节点sibling顺序万一排反了"这种风险。
        // 不响应点击关闭：放弃任务是破坏性操作，不该允许"点哪都能关"这种容易
        // 误触的退出方式，只能靠下面明确的"取消"按钮关掉。
        RectTransform rootRt = SkyPrisonFloatingWindowKit.MakeRect("AbandonConfirmPopup", _boxRt, Vector2.zero, Vector2.one);
        _abandonConfirmRootGo = rootRt.gameObject;
        var backdrop = rootRt.gameObject.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.35f);
        backdrop.raycastTarget = true;
        rootRt.gameObject.AddComponent<Button>().onClick.AddListener(() => { }); // 纯粹挡住点击穿透到后面任务列表

        RectTransform box = SkyPrisonFloatingWindowKit.MakeRect("Box", rootRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        box.sizeDelta = new Vector2(960f * M, 440f * M);
        _abandonConfirmRt = box;

        // 窗口标配：黑白高斯模糊背景(悬浮窗口用SkyPrisonFloatingWindowKit.BuildBlurBackground
        // 这一套，不是PauseMenuController暂停菜单那种整屏截图+金字塔模糊——两种模糊
        // 场景不同，ui-design-system里写得很清楚)，不叠暗色tint矩形；白色角标；
        // 危险操作只体现在标题/确认按钮的文字颜色上，边框/角标不跟着变色。
        SkyPrisonFloatingWindowKit.BuildBlurBackground(this, box, out _abandonConfirmBlurTracker);
        // 用不带参数的重载，跟这个窗口自己的_boxRt、以及项目里其它所有窗口共用同一套
        // 角标尺寸常量(CornerBracketLength/Thickness=30/3*M)——之前直接照抄
        // PauseMenuController里40f/4f这两个原始像素值，那是它自己不走M缩放系统的
        // 独立换算，套到这里反而比其它窗口的角标明显大一圈。
        SkyPrisonFloatingWindowKit.AddCornerBrackets(box);

        Color danger = new Color(0.75f, 0.42f, 0.42f, 1f);

        var titleRt = SkyPrisonFloatingWindowKit.MakeRect("Title", box, new Vector2(0f, 1f), new Vector2(1f, 1f));
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -56f * M);
        titleRt.sizeDelta = new Vector2(0f, 64f * M);
        TMP_Text titleText = SkyPrisonFloatingWindowKit.MakeText(titleRt, "Label",
            L("questlog_abandon_confirm_title", "放弃任务？"), TitleFontSize, FontStyles.Bold, _font);
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = danger;
        titleText.raycastTarget = false;

        var bodyRt = SkyPrisonFloatingWindowKit.MakeRect("Body", box, new Vector2(0.08f, 0.42f), new Vector2(0.92f, 0.72f));
        _abandonConfirmMessageText = SkyPrisonFloatingWindowKit.MakeText(bodyRt, "Label", "", ObjectiveFontSize * 1.2f, FontStyles.Normal, _font);
        _abandonConfirmMessageText.alignment = TextAlignmentOptions.Center;
        _abandonConfirmMessageText.color = Color.white;
        _abandonConfirmMessageText.enableWordWrapping = true;
        _abandonConfirmMessageText.raycastTarget = false;
        if (_font != null) _abandonConfirmMessageText.font = _font;

        BuildAbandonConfirmButton(box, new Vector2(0.08f, 0.14f), new Vector2(0.48f, 0.34f),
            L("questlog_abandon_confirm_confirm", "确认放弃"), danger, () =>
            {
                QuestDefinition quest = _pendingAbandonQuest;
                HideAbandonConfirmPopup();
                if (quest != null)
                {
                    QuestRuntime.Instance?.AbandonQuest(quest);
                    RebuildQuestList();
                }
            });
        BuildAbandonConfirmButton(box, new Vector2(0.52f, 0.14f), new Vector2(0.92f, 0.34f),
            L("questlog_abandon_confirm_cancel", "取消"), new Color(0.88f, 0.88f, 0.90f), HideAbandonConfirmPopup);

        rootRt.gameObject.SetActive(false);
    }

    /// <summary>窗口标配按钮：透明底+白色描边+ButtonFeedback，锚点百分比区间摆放
    /// (不是LayoutGroup自动分配)，跟PauseMenuController.BuildConfirmDialogButton
    /// 完全同一套接法。</summary>
    private void BuildAbandonConfirmButton(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
        string label, Color textColor, System.Action onClick)
    {
        var rt = SkyPrisonFloatingWindowKit.MakeRect("Btn_" + label, parent, anchorMin, anchorMax);
        rt.gameObject.AddComponent<Image>().color = Color.clear;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() =>
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
            onClick?.Invoke();
        });
        SkyPrisonUIButtonFeedback.Attach(rt.gameObject);
        SkyPrisonFloatingWindowKit.AddOutline(rt, new Color(1f, 1f, 1f, 0.35f), 3f * M);
        TMP_Text t = SkyPrisonFloatingWindowKit.MakeText(rt, "Label", label, ObjectiveFontSize * 1.3f, FontStyles.Normal, _font);
        t.alignment = TextAlignmentOptions.Center;
        t.color = textColor;
        t.raycastTarget = false;
    }

    private void ShowAbandonConfirmPopup(QuestDefinition quest)
    {
        EnsureAbandonConfirmPopup();
        _pendingAbandonQuest = quest;
        // 任务名用冷绿色标出来，不用书名号《》——项目是"白+冷绿"两色系统，冷绿本来就是
        // 强调色，标重点信息不需要另外的标点符号约定。
        _abandonConfirmMessageText.text = string.Format(
            L("questlog_abandon_confirm_message", "确定要放弃{0}吗？放弃后可以重新接取，但当前进度会清空。"),
            $"<color=#{ColdGreenHex}>{quest.GetLocalizedTitle(quest.questId)}</color>");

        _abandonConfirmVisible = true;
        _abandonConfirmRootGo.SetActive(true);
        _abandonConfirmRt.localScale = new Vector3(1f, 0f, 1f);
        if (_abandonConfirmAnimCoroutine != null) StopCoroutine(_abandonConfirmAnimCoroutine);
        _abandonConfirmAnimCoroutine = StartCoroutine(AbandonConfirmOpenAnimation());
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
    }

    private void HideAbandonConfirmPopup()
    {
        if (!_abandonConfirmVisible) return;
        _abandonConfirmVisible = false;
        if (_abandonConfirmAnimCoroutine != null) StopCoroutine(_abandonConfirmAnimCoroutine);
        _abandonConfirmAnimCoroutine = StartCoroutine(AbandonConfirmCloseAnimation());
    }

    private IEnumerator AbandonConfirmOpenAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_abandonConfirmRt == null) yield break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - (1f - p) * (1f - p);
            _abandonConfirmRt.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_abandonConfirmRt != null) _abandonConfirmRt.localScale = Vector3.one;
    }

    private IEnumerator AbandonConfirmCloseAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_abandonConfirmRt == null) yield break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - p * p;
            _abandonConfirmRt.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_abandonConfirmRt != null) _abandonConfirmRt.localScale = Vector3.one;
        if (_abandonConfirmRootGo != null) _abandonConfirmRootGo.SetActive(false);
    }

    private readonly Vector3[] _cornersBuffer = new Vector3[4];

    // 键鼠手柄统一的"光标"——这个窗口以前只能用鼠标点卡片，用户明确要求键盘/手柄
    // 也要能选中/展开任务。复用背包/商店那一批窗口共用的SkyPrisonListGamepadNav
    // (方向键/WASD/摇杆移动光标+A键/回车确认)，不用自己另起一套。DrawHighlight
    // 留默认true——这个窗口目前没有自己的聚焦视觉，直接用它自带的冷绿细框表示
    // "光标停在哪张卡片"，跟BuildCardOutline画的"这条是追踪中任务"粗边框是两回事，
    // 不会互相覆盖语义(一个说光标在哪，一个说追踪的是哪条)。
    private SkyPrisonListGamepadNav _cardNav;
    // 每张卡片自己的背景Image+它没被光标聚焦时该有的基础透明度——每次
    // RebuildQuestList都是整个销毁重建，这份列表也要跟着清空重新收集，不然会拿着
    // 已经被Destroy的旧Image去比对，浪费之外还可能报错。
    private readonly List<(Button button, Image bg, float baseAlpha)> _cardFocusEntries = new();
    private Button _lastCardFocusButton;

    private void Build()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);

        _font = SkyPrisonFloatingWindowKit.LoadTMPFont("ZhouFangRiMingTi-2 SDF");
        _locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        _cardNav = gameObject.AddComponent<SkyPrisonListGamepadNav>();
        // 追踪中的任务卡本来就有一圈冷绿色边框(BuildCardOutline)——如果它同时又是
        // 键盘/手柄光标停留的那张卡，_cardNav自带的四边高亮框(也是冷绿色)会跟这圈
        // 边框叠在一起，两条线粗细/位置对不齐，看起来像边框下面多出一条线。关掉
        // 自带高亮框，改用UpdateCardFocusVisuals()自己的背景色调深方案区分"光标在
        // 这"，跟"是不是追踪中"(边框颜色)完全独立，不会再互相打架。
        _cardNav.DrawHighlight = false;

        _savedExternalBlock = SkyPrisonWindowManager_V1.ExternalBlock;
        SkyPrisonWindowManager_V1.ExternalBlock = true;
        HideCombatHud();

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var boxGo = new GameObject("Box", typeof(RectTransform));
        boxGo.transform.SetParent(transform, false);
        _boxRt = (RectTransform)boxGo.transform;
        _boxRt.anchorMin = _boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        _boxRt.pivot = new Vector2(0.5f, 0.5f);
        _boxRt.anchoredPosition = Vector2.zero;
        _boxRt.sizeDelta = new Vector2(BoxWidth, BoxHeight);

        SkyPrisonFloatingWindowKit.BuildBlurBackground(this, _boxRt, out _blurUvTracker);
        SkyPrisonFloatingWindowKit.AddCornerBrackets(_boxRt);
        SkyPrisonFloatingWindowKit.BuildTitleBar(_boxRt, L("questlog_title", "任务日志"), _font, out _);
        SkyPrisonFloatingWindowKit.BuildCloseButton(_boxRt, Hide, _font);

        BuildTabBar();
        BuildScrollArea();
        RebuildQuestList();

        _boxRt.localScale = new Vector3(1f, 0f, 1f);
        StartCoroutine(OpenBoxAnimation());

        RefreshHints();
    }

    /// <summary>T这个键的提示文字要跟着"当前展开的任务现在是不是已经追踪"动态换——
    /// 没追踪显示"设为追踪中任务"，已经追踪显示"取消追踪中任务"，不然玩家会对着已经
    /// 追踪的任务再按一次T，看提示还以为是"设为"，容易以为按了没反应。RebuildQuestList
    /// 每次状态变化(展开/收起、追踪切换、每0.5秒的轮询)都会调这个方法，保持同步。</summary>
    private void RefreshHints()
    {
        bool expandedIsTracked = !string.IsNullOrEmpty(_expandedQuestId)
            && QuestRuntime.Instance != null
            && QuestRuntime.Instance.GetTrackedQuestId() == _expandedQuestId;

        string trackHintLabel = expandedIsTracked
            ? L("questlog_hint_untrack", "取消追踪中任务")
            : L("questlog_hint_track", "设为追踪中任务");

        SkyPrisonWindowHintBar.GetOrCreate().Show(new[]
        {
            SkyPrisonWindowHint.Action(SkyPrisonInputAction.QuestLog, L("ui_hint_close", "关闭")),
            // Delete没有对应的SkyPrisonInputAction(不是走键位重绑定系统的正式游戏动作，
            // 只是这个窗口内部硬编码的一个快捷键)，用字面Icon而不是Action——找不到
            // "keyboard/delete"这个图标的话会自动退回文字token"Del"，不会崩。
            SkyPrisonWindowHint.Icon("keyboard/delete", "Del", L("questlog_hint_abandon", "放弃任务")),
            // T同样是这个窗口内部硬编码的快捷键，不是正式的可重绑定输入动作——
            // 只在某条任务展开看详情时才对它生效，没展开任何任务时按了不会有反应。
            SkyPrisonWindowHint.Icon("keyboard/t", "T", trackHintLabel),
        });
    }

    // 分页签——"进行中/已完成"两个状态各自的结构(主线/支线分组+数量)一直可见，
    // 不用靠有没有数据来决定要不要显示这套骨架("界面里应该有结构，而不是真的什么
    // 也没有")。"失败"状态后端目前完全没有对应的数据(PlayerSaveData 只有
    // activeQuestIds/completedQuestIds)，属于要另外设计的新机制，先不做。
    private void BuildTabBar()
    {
        var tabBarRt = SkyPrisonFloatingWindowKit.MakeRect("TabBar", _boxRt, Vector2.zero, Vector2.one);
        tabBarRt.pivot = new Vector2(0.5f, 1f);
        tabBarRt.anchorMin = new Vector2(0f, 1f);
        tabBarRt.anchorMax = new Vector2(1f, 1f);
        tabBarRt.offsetMin = new Vector2(Padding, 0f);
        tabBarRt.offsetMax = new Vector2(-Padding, 0f);
        tabBarRt.sizeDelta = new Vector2(0f, TabBarHeight);
        tabBarRt.anchoredPosition = new Vector2(0f, -(SkyPrisonFloatingWindowKit.TitleBarHeight + 8f));

        var layout = tabBarRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 56f * M;
        // childControlWidth 必须是true——之前是false，配合按钮本身anchorMin=anchorMax=zero
        // (零面积矩形)，LayoutElement.preferredWidth 完全不会被采用，导致按钮实际尺寸是
        // 0x0：文字容器0宽度让TMP每个字都换行(看起来"竖着")，点击区域也是0x0点不到。
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        // childForceExpandWidth 的Unity默认值是true，之前没显式设过——两个Tab按钮
        // 会被拉开撑满整条Tab栏的宽度，"已完成"跑到了离"进行中"很远的地方，不是
        // 紧挨着的两个Tab。显式关掉，两个按钮只按spacing挨在一起，贴在左侧。
        layout.childForceExpandWidth = false;
        // Tab栏这里之前只吃了窗口最外层的Padding，而下面每张任务卡片自己内部还
        // 额外加了一圈20*M的padding(卡片标题"主线任务"实际左边距是Padding+20*M)，
        // 两者对不齐，"进行中"看起来比卡片内容更贴边——补上同样的左边距对齐。
        layout.padding = new RectOffset((int)(20f * M), 0, 0, 0);

        _tabInProgressText = BuildTabButton(tabBarRt, L("questlog_tab_inprogress", "进行中"), QuestLogTab.InProgress);
        BuildTabDivider(tabBarRt);
        _tabCompletedText = BuildTabButton(tabBarRt, L("questlog_tab_completed", "已完成"), QuestLogTab.Completed);
        RefreshTabColors();

        // Tab栏和下面的任务列表之间加一条横向分隔线，跟列表内容留出明确的距离，
        // 不是两块内容紧挨在一起。
        var hDividerRt = SkyPrisonFloatingWindowKit.MakeRect("TabBarDivider", _boxRt,
            new Vector2(0f, 1f), new Vector2(1f, 1f));
        hDividerRt.pivot = new Vector2(0.5f, 1f);
        hDividerRt.offsetMin = new Vector2(Padding, 0f);
        hDividerRt.offsetMax = new Vector2(-Padding, 0f);
        hDividerRt.sizeDelta = new Vector2(0f, 2f);
        hDividerRt.anchoredPosition = new Vector2(0f, -(SkyPrisonFloatingWindowKit.TitleBarHeight + TabBarHeight + 20f));
        var hDividerImg = hDividerRt.gameObject.AddComponent<Image>();
        hDividerImg.color = new Color(1f, 1f, 1f, 0.14f);
        hDividerImg.raycastTarget = false;
    }

    /// <summary>两个Tab之间的细灰色竖线分隔，纯装饰，不响应点击。</summary>
    private void BuildTabDivider(RectTransform parent)
    {
        var rt = SkyPrisonFloatingWindowKit.MakeRect("Divider", parent, Vector2.zero, Vector2.zero);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 2f * M;
        le.preferredHeight = TabBarHeight * 0.5f;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.18f);
        img.raycastTarget = false;
    }

    private TMP_Text BuildTabButton(RectTransform parent, string label, QuestLogTab tab)
    {
        var btnRt = SkyPrisonFloatingWindowKit.MakeRect(tab.ToString(), parent, Vector2.zero, Vector2.zero);
        var le = btnRt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = label.Length * TitleFontSize * 0.9f + 24f * M;
        le.preferredHeight = TabBarHeight;

        btnRt.gameObject.AddComponent<Image>().color = Color.clear;
        var btn = btnRt.gameObject.AddComponent<Button>();
        var nav = btn.navigation;
        nav.mode = Navigation.Mode.None;
        btn.navigation = nav;
        btn.onClick.AddListener(() =>
        {
            if (_currentTab == tab) return;
            _currentTab = tab;
            RefreshTabColors();
            RebuildQuestList();
        });

        TMP_Text text = SkyPrisonFloatingWindowKit.MakeText(btnRt, "Label", label, TitleFontSize, FontStyles.Bold, _font);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        // MakeText默认开着自动换行，"进行中"/"已完成"这种短标签本来不该换行，
        // 结果按钮宽度稍微一紧就被拆成两行("进行"/"中")——显式关掉。
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private void RefreshTabColors()
    {
        if (_tabInProgressText != null)
            _tabInProgressText.color = _currentTab == QuestLogTab.InProgress ? SkyPrisonUIPalette.ColdGreen : new Color(1f, 1f, 1f, 0.5f);
        if (_tabCompletedText != null)
            _tabCompletedText.color = _currentTab == QuestLogTab.Completed ? SkyPrisonUIPalette.ColdGreen : new Color(1f, 1f, 1f, 0.5f);
    }

    private void BuildScrollArea()
    {
        var scrollGo = new GameObject("Scroll", typeof(RectTransform));
        scrollGo.transform.SetParent(_boxRt, false);
        var scrollRt = (RectTransform)scrollGo.transform;
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(Padding, Padding);
        // 24f→44f：Tab栏下面新加了一条分隔线(在20f处)，分隔线和下面任务列表之间
        // 还要再留一点距离，不是紧贴着。
        scrollRt.offsetMax = new Vector2(-Padding, -(SkyPrisonFloatingWindowKit.TitleBarHeight + TabBarHeight + 44f));

        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollGo.AddComponent<RectMask2D>();
        // 之前在这里给RectMask2D加了padding，以为能把裁切边界往外推——查证之后
        // 这个字段的语义其实是让裁切范围整体往里收缩(收得更严格)，不是往外扩，
        // 方向反了，撤掉。真正的裁字根因跟裁切边界完全无关，是下面卡片文字漏设
        // enableWordWrapping=false导致的换行，不是这一层的问题。
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        // Elastic——拖到头之后有一段"拖不动、松手回弹"的软范围，跟背包窗口同一个
        // 手感，不是硬边界(Clamped)那种一撞就停死。
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.elasticity = 0.12f;
        scrollRect.scrollSensitivity = 24f;

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(scrollRt, false);
        _listContent = (RectTransform)contentGo.transform;
        _listContent.anchorMin = new Vector2(0f, 1f);
        _listContent.anchorMax = new Vector2(1f, 1f);
        _listContent.pivot = new Vector2(0.5f, 1f);
        _listContent.anchoredPosition = Vector2.zero;
        // 真正的裁字根因：用 new GameObject(...) 直接建的RectTransform，默认残留
        // sizeDelta=(100,100)——X轴锚点是拉伸的(0→1)，这100单位没清零的话会按
        // pivot对称摊到左右各50，导致_listContent(以及靠它撑宽度的每一张卡片)
        // 比RectMask2D的裁切区域整整宽100，左右各越界50。这个文件里其它矩形都是
        // 走 SkyPrisonFloatingWindowKit.MakeRect 创建、每次都会把offsetMin/Max清零，
        // 唯独这个_listContent是当年直接手写 new GameObject 建的，漏了这一步。
        // 用几何日志实测坐标验证过：card宽度2616.80，裁切区域宽度2516.80，
        // 差值正好是100。
        _listContent.sizeDelta = new Vector2(0f, _listContent.sizeDelta.y);

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.spacing = QuestGap;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = _listContent;
        scrollRect.viewport = scrollRt;

        // 任务多到超出可视范围时右侧出现滚动条——项目统一样式(SkyPrisonUIScrollbar)，
        // 边距要跟上面ScrollArea自己的offsetMin/Max对齐，滚动条纵向范围才会跟列表
        // 完全一致。rightMargin比Padding多留4px，避免贴在跟RectMask2D裁切边界完全
        // 重合的位置(之前占位框边框显示不全就是栽在这个坑上)。
        SkyPrisonUIScrollbar.AttachVertical(scrollRect, _boxRt, SkyPrisonUIPalette.ColdGreen,
            rightMargin: Padding + 4f,
            topMargin: SkyPrisonFloatingWindowKit.TitleBarHeight + TabBarHeight + 24f,
            bottomMargin: Padding,
            visibility: ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport);
    }

    private void RebuildQuestList()
    {
        if (_listContent == null) return;

        _pendingNodeAlignments.Clear();

        for (int i = _listContent.childCount - 1; i >= 0; i--)
            Destroy(_listContent.GetChild(i).gameObject);
        _linkableTexts.Clear();
        // 整批卡片马上要被销毁重建，之前收集的(button,bg)配对全部指向已经Destroy的
        // 旧物体，必须先清空，不然UpdateCardFocusVisuals会拿着悬空引用去比对。
        _cardFocusEntries.Clear();
        _lastCardFocusButton = null;
        // 不在这里主动HideHoverCard——每0.5秒都会跑到这里，鼠标其实还稳稳停在同一个
        // 名字上的话，这里关一下、UpdateHoverCard紧接着在同一帧又因为检测到悬停重新
        // 打开，表现成悬浮卡跟着刷新周期一直"关闭再重开"的小动画抽搐。交给下面
        // Update()里紧跟着的UpdateHoverCard()自己判断：真悬停着就什么都不用做，
        // 真的移开了才需要关。

        List<QuestDefinition> quests = QuestRuntime.Instance == null
            ? new List<QuestDefinition>()
            : _currentTab == QuestLogTab.InProgress
                ? QuestRuntime.Instance.GetActiveQuests()
                : QuestRuntime.Instance.GetCompletedQuests();

        // 追踪中任务(追踪中的那一条)单独拎出来放最上面的"追踪中任务"分类，不再跟主线/
        // 支线的普通列表混在一起——用户明确要求"多加一个分类，保持置顶"，不是靠卡片
        // 右上角一个小角标(那个角标之前跟节点链最后一个节点的文字挤在同一个角落，
        // 会互相盖住)。只在"进行中"分页生效——已完成的任务不可能是追踪状态
        // (UpdateTrackQuestHotkey/放弃任务时都会摘掉trackedQuestId)。
        string trackedId = _currentTab == QuestLogTab.InProgress && QuestRuntime.Instance != null
            ? QuestRuntime.Instance.GetTrackedQuestId()
            : "";
        QuestDefinition trackedQuest = string.IsNullOrEmpty(trackedId) ? null : quests.Find(q => q.questId == trackedId);

        List<QuestDefinition> mainlineQuests = quests.FindAll(q => q.category == QuestCategory.Mainline && q != trackedQuest);
        List<QuestDefinition> sideQuests = quests.FindAll(q => q.category != QuestCategory.Mainline && q != trackedQuest);

        List<CharacterArcDefinition> arcs = _currentTab == QuestLogTab.InProgress
            ? CharacterArcRuntime.GetAll()
            : new List<CharacterArcDefinition>();

        // NO REQUEST只在整个分页彻底没有任何内容(不管追踪中/主线/支线/人物记录)时显示一次；
        // 单个分类是0条就直接跳过整块(不画标题也不画占位框)，不再"哪怕0条也要摆个
        // 骨架"——这是玩家自己要求改的，跟之前的设计决定正好相反。
        bool completelyEmpty = trackedQuest == null && mainlineQuests.Count == 0 && sideQuests.Count == 0 && arcs.Count == 0;
        if (completelyEmpty)
        {
            BuildEmptyPlaceholder(null);
        }
        else
        {
            if (trackedQuest != null)
            {
                BuildSectionHeader(L("questlog_section_primary", "追踪中任务"), 1);
                BuildQuestCard(trackedQuest, big: trackedQuest.category == QuestCategory.Mainline);
            }

            if (mainlineQuests.Count > 0)
            {
                BuildSectionHeader(L("questlog_section_mainline", "主线任务"), mainlineQuests.Count);
                foreach (QuestDefinition quest in mainlineQuests)
                    BuildQuestCard(quest, big: true);
            }

            if (sideQuests.Count > 0)
            {
                BuildSectionHeader(L("questlog_section_side", "支线任务"), sideQuests.Count);
                foreach (QuestDefinition quest in sideQuests)
                    BuildQuestCard(quest, big: false);
            }

            // 人物记录是跟任务平行的系统，不受"进行中/已完成"这两个任务分页控制——
            // 只在"进行中"分页额外展示，避免跟"已完成"分页里同一批人物记录重复出现
            // 却没有实际区别(人物记录没有active/completed两态，两个分页会长得一样)。
            if (_currentTab == QuestLogTab.InProgress && arcs.Count > 0)
            {
                BuildSectionHeader(L("questlog_section_characterarc", "人物记录"), arcs.Count);
                foreach (CharacterArcDefinition arc in arcs)
                    BuildCharacterArcCard(arc);
            }
        }

        // 每0.5秒整个销毁重建一次列表——TMP文字+LayoutGroup刚创建出来那一帧，Unity
        // 的自动布局还没跑完一轮，几何体是按"初始默认宽度"生成的(容器还没被
        // VerticalLayoutGroup/ContentSizeFitter撑到正确尺寸就已经排版过一次文字)，
        // 表现成文字被截断只剩最后几个字——这一帧的错误几何体会一直保持到下一次
        // 重建(每次都在同一个错误状态里重新开始)，不会自己变好。这里强制立刻跑完
        // 一整轮布局，保证文字在"正确宽度"下重新排版，不用等下一帧才生效(下一帧
        // 之前已经又被销毁重建过一轮了，永远追不上)。
        LayoutRebuilder.ForceRebuildLayoutImmediate(_listContent);

        // "当前目标"/分割线/区域-委托人对齐节点链第一个节点位置——必须放在上面这次
        // 强制刷新之后量，理由跟上面那段注释是同一个坑。这里再补一次刷新，让量出来的
        // padding实际生效反映到文字位置上(改padding之后VerticalLayoutGroup要重新
        // 摆一次子物体才会动)。
        foreach (NodeAlignmentTarget target in _pendingNodeAlignments)
        {
            if (target.nodeChainRt == null || target.nodeChainRt.childCount == 0 || target.headerRt == null)
                continue;
            var firstCol = target.nodeChainRt.GetChild(0) as RectTransform;
            if (firstCol == null) continue;

            firstCol.GetWorldCorners(_cornersBuffer);
            Vector3 localInHeader = target.headerRt.InverseTransformPoint(_cornersBuffer[0]);
            int offset = (int)Mathf.Max(0f, localInHeader.x - target.headerRt.rect.xMin);

            if (target.currentLayout != null)
                target.currentLayout.padding = new RectOffset(offset, 0, 0, 0);
            if (target.dividerLayout != null)
                target.dividerLayout.padding = new RectOffset(offset, 0, 0, 0);
            if (target.infoRowLayout != null)
            {
                RectOffset p = target.infoRowLayout.padding;
                target.infoRowLayout.padding = new RectOffset(offset, p.right, p.top, p.bottom);
            }
        }
        _pendingNodeAlignments.Clear();
        if (_listContent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_listContent);

        // 每次整个列表销毁重建之后，卡片按钮实例全部换了一批新的，必须重新喂一次——
        // 人物记录卡(BuildCharacterArcCard)没挂Button，天然不会混进来，不用额外过滤。
        if (_cardNav != null && _listContent != null)
            _cardNav.SetTargets(_listContent.GetComponentsInChildren<Button>(false));

        RefreshHints();
    }

    // 排查记录：几何日志证实headerRt/listContent/viewport宽度全部远大于文字实际
    // 需要的宽度(preferredWidth仅165，容器有2616+)，排除了"宽度不够被裁"这个方向；
    // 字符串本身也确认一字不少。剩下唯一没排除的变量是<color>富文本标签的解析——
    // 改成完全不用富文本标签，拆成"标签文字"+"数量文字"两个独立TMP组件，颜色直接
    // 用Color属性设置，不再靠字符串里嵌tag，从根源上去掉这个变量。
    private void BuildSectionHeader(string label, int count)
    {
        var headerRt = SkyPrisonFloatingWindowKit.MakeRect("SectionHeader", _listContent, Vector2.zero, Vector2.one);
        headerRt.gameObject.AddComponent<LayoutElement>().preferredHeight = ObjectiveFontSize * 1.3f;

        var layout = headerRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 12f * M;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        // 排查："宽度足够"+"去掉富文本标签"两轮修复都没用，剩下最可疑的方向是
        // 字体资产本身缺"主/线/支/人/物"这几个字的字形(TMP找不到字形会直接把那个
        // 字符从渲染网格里丢掉，不占位置，表现就是"文字被砍掉一截"而不是显示豆腐块)。
        // 直接查HasCharacter，不再猜。
        if (_font != null)
        {
            Debug.Log($"[QuestLogController] 字体字形排查：font={_font.name}, " +
                      $"主={_font.HasCharacter('主')}, 线={_font.HasCharacter('线')}, " +
                      $"支={_font.HasCharacter('支')}, 人={_font.HasCharacter('人')}, " +
                      $"物={_font.HasCharacter('物')}, 任={_font.HasCharacter('任')}, " +
                      $"务={_font.HasCharacter('务')}, 记={_font.HasCharacter('记')}, " +
                      $"录={_font.HasCharacter('录')}");
        }
        else
        {
            Debug.Log("[QuestLogController] 字体字形排查：_font为null");
        }

        TMP_Text labelText = SkyPrisonFloatingWindowKit.MakeText(headerRt, "Label", label, ObjectiveFontSize, FontStyles.Bold, _font);
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.color = SkyPrisonUIPalette.ColdGreen;
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TextOverflowModes.Overflow;
        labelText.gameObject.AddComponent<LayoutElement>().preferredWidth = 240f * M;

        TMP_Text countText = SkyPrisonFloatingWindowKit.MakeText(headerRt, "Count", $"({count})", ObjectiveFontSize, FontStyles.Bold, _font);
        countText.alignment = TextAlignmentOptions.MidlineLeft;
        countText.color = Color.white;
        countText.enableWordWrapping = false;
        countText.overflowMode = TextOverflowModes.Overflow;
        countText.gameObject.AddComponent<LayoutElement>().preferredWidth = 80f * M;
    }

    private const float EmptyPlaceholderHeight = 150f * M;

    /// <summary>某个分组(或整个分页)当前没有内容时的占位框——灰底+"+"号点阵填充+
    /// 居中"NO REQUEST"，不是留白，而是明确告诉玩家"这里本来就没有内容"，跟战术
    /// 终端"无信号"画面是同一个思路。categoryLabel非空时把分类名字("主线任务"这种)
    /// 一起写在框左上角，调用方这种情况下就不用再单独画一条SectionHeader了；
    /// categoryLabel传null用于"整个分页彻底没有任何内容"，只显示一个大大的
    /// NO REQUEST，不重复摆三个同样的占位框。</summary>
    private void BuildEmptyPlaceholder(string categoryLabel)
    {
        // 左右安全边距现在统一交给_listContent自己的VerticalLayoutGroup.padding
        // 负责(跟任务卡片cardGo同一个机制)，这里不再额外收一层比例——之前boxRt
        // 自己还要再往里缩2%，跟卡片只收一次的量对不上，占位框看起来比卡片窄一圈。
        // 统一成"只由_listContent的padding收一次"，占位框和卡片的实际宽度才会一致。
        var rt = SkyPrisonFloatingWindowKit.MakeRect("EmptyPlaceholder", _listContent, Vector2.zero, Vector2.one);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = EmptyPlaceholderHeight;

        var boxRt = SkyPrisonFloatingWindowKit.MakeRect("Box", rt, Vector2.zero, Vector2.one);
        boxRt.offsetMin = Vector2.zero;
        boxRt.offsetMax = Vector2.zero;

        var bg = boxRt.gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.035f);

        var gridRt = SkyPrisonFloatingWindowKit.MakeRect("Grid", boxRt, Vector2.zero, Vector2.one);
        var gridImg = gridRt.gameObject.AddComponent<Image>();
        gridImg.sprite = GetEmptyPlaceholderGridSprite();
        gridImg.type = Image.Type.Tiled;
        gridImg.color = new Color(1f, 1f, 1f, 0.12f);
        gridImg.raycastTarget = false;

        // 确认过是"占位框比窗口宽，左右边框被顶到窗口外"——改比例缩进(boxRt)修好后，
        // 边框颜色改回细线，跟"NO REQUEST"文字用同一个颜色(白色+0.45透明度)，不用
        // 冷绿也不用亮粉，风格上跟文字统一。
        SkyPrisonFloatingWindowKit.AddOutline(boxRt, new Color(1f, 1f, 1f, 0.45f), 3f);

        if (!string.IsNullOrEmpty(categoryLabel))
        {
            var catRt = SkyPrisonFloatingWindowKit.MakeRect("CategoryLabel", boxRt, Vector2.zero, Vector2.one);
            catRt.offsetMin = new Vector2(20f * M, 0f);
            catRt.offsetMax = new Vector2(-20f * M, -12f * M);
            TMP_Text catText = SkyPrisonFloatingWindowKit.MakeText(catRt, "Label", categoryLabel, ObjectiveFontSize, FontStyles.Bold, _font);
            catText.alignment = TextAlignmentOptions.TopLeft;
            catText.color = SkyPrisonUIPalette.ColdGreen;
            catText.enableWordWrapping = false;
            catText.overflowMode = TextOverflowModes.Overflow;
            catText.raycastTarget = false;
        }

        // "东亚重工"风格字体——项目里那份文件名乱码的字体资产，跟物品详情面板的
        // "DATA"装饰字用的是同一个查找方式(按稳定存在的关键字"ع"找，不靠打不出来的
        // 乱码文件名匹配)。Build里AssetDatabase不可用，查不到就退回_font，不会崩。
        TMP_FontAsset noRequestFont = LoadFontByKeyword("ع") ?? _font;
        TMP_Text label = SkyPrisonFloatingWindowKit.MakeText(boxRt, "NoRequest", "NO REQUEST", TitleFontSize * 1.43f, FontStyles.Bold, noRequestFont);
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 1f, 1f, 0.45f);
        label.characterSpacing = 6f;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
    }

    private static Sprite _emptyPlaceholderGridSprite;

    /// <summary>断开的细十字点阵——不是连续网格线(试过一版，格子会连成一整张网，
    /// 不是想要的效果)，是每个格子中心一个孤立的十字，1px细线、比最早那版"+"号
    /// 稍微细长一点，格子之间留空隙，互不相连。</summary>
    private static Sprite GetEmptyPlaceholderGridSprite()
    {
        if (_emptyPlaceholderGridSprite != null) return _emptyPlaceholderGridSprite;

        const int size = 22; // 格子间距——之前16太密，加大到22留更多空隙
        const int center = size / 2;
        const int armReach = 3; // 十字每条臂伸出去的长度，比最早那版(1)长一点
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color clear = new Color(1f, 1f, 1f, 0f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool onCross = (x == center && Mathf.Abs(y - center) <= armReach)
                            || (y == center && Mathf.Abs(x - center) <= armReach);
                tex.SetPixel(x, y, onCross ? Color.white : clear);
            }
        }
        tex.Apply();

        // pixelsPerUnit传size*2.2——数值越大，平铺出来的每个十字在屏幕上占的实际
        // 尺寸越小，线条视觉上也跟着变细(纹理本身还是1px线，缩小之后自然更细)。
        // 之前直接传size，十字在4K参考分辨率下被放得太大，线又粗又占地方。
        _emptyPlaceholderGridSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size * 2.2f);
        _emptyPlaceholderGridSprite.hideFlags = HideFlags.HideAndDontSave;
        return _emptyPlaceholderGridSprite;
    }

    private static Sprite _heroTintGradientSprite;

    /// <summary>任务卡片配图从左到右淡出到暗色的渐变遮罩——左边保留图片本身细节，
    /// 右边(标题/节点链文字所在区域)过渡到接近卡片底色的暗色，保证文字在任何图片
    /// 内容上都可读。用1像素高、64像素宽的横向渐变贴图，Bilinear过滤+Simple拉伸
    /// 铺满整个遮罩区域，不是逐像素画贴图那种重活。</summary>
    private static Sprite GetHeroTintGradientSprite()
    {
        if (_heroTintGradientSprite != null) return _heroTintGradientSprite;

        const int width = 64;
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color dark = new Color(0.02f, 0.03f, 0.03f);
        for (int x = 0; x < width; x++)
        {
            float t = x / (float)(width - 1);
            float alpha = Mathf.Lerp(0.05f, 0.92f, t);
            tex.SetPixel(x, 0, new Color(dark.r, dark.g, dark.b, alpha));
        }
        tex.Apply();

        _heroTintGradientSprite = Sprite.Create(tex, new Rect(0f, 0f, width, 1f), new Vector2(0.5f, 0.5f));
        _heroTintGradientSprite.hideFlags = HideFlags.HideAndDontSave;
        return _heroTintGradientSprite;
    }

    /// <summary>按文件名里稳定存在的关键字模糊查找TMP字体资产——跟
    /// InventoryItemDetailPanel.LoadFontByKeyword同一个用途/同一个查找方式，那份是
    /// private嵌套类型里的，这里的窗口是独立类，没法直接复用，照抄一份。只能在
    /// Editor下工作(AssetDatabase)，Build里直接返回null，调用方必须自己准备兜底。</summary>
    private static TMP_FontAsset LoadFontByKeyword(string keyword)
    {
#if UNITY_EDITOR
        string dir = "Assets/_Project/UIUX/Fonts/TMP/";
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { dir }))
        {
            string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (p.Contains(keyword))
                return UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(p);
        }
#endif
        return null;
    }

    private static readonly Color SideQuestBlue = new Color(0.5f, 0.72f, 0.95f, 1f);

    private const float NodeSize = 40f * M * 1.3f;
    // Col的宽度要紧贴图标本身，不能留空白——列一宽，图标居中之后离列边缘就有
    // 一截空白，连线只连到列边缘碰不到图标，看起来像没连上。标签文字宽度是
    // 单独给的(ignoreLayout，不受这个窄列限制)，允许比列宽得多、水平居中显示，
    // 长一点的目标名会自动换成两行，不会裁切。
    private const float NodeColumnWidth = NodeSize + 4f * M;
    // 210(原180)——日/英翻译比中文自然长，同样内容("交付xx给xx"这类)换成日英
    // 经常比中文多绕出一整行，光靠缩字号治标不治本，先把可用宽度本身放宽一点，
    // 再配合下面ShrinkTextToMaxLines兜底，两边一起上。
    private const float NodeLabelWidth = 210f * M;
    private const float NodeLabelFontSize = ObjectiveFontSize * 1.2f;
    private const float NodeLabelMinFontSize = NodeLabelFontSize * 0.7f;
    private const float NodeLabelHeight = NodeLabelFontSize * 2.4f;
    private const float HeroImageHeight = 260f * M;

    // big=主线/支线的视觉规格(标题字号、有没有配图)，跟"展开/收起"是两件独立的事：
    // 展开=当前正在看这条任务详情(点卡片/键盘方向键+回车/手柄光标+A切换)，收起=
    // 紧凑单行(标题+节点链+区域/委托人+箭头)，两个类别的收起态长得一样，只是展开
    // 态主线多一张背景配图。是否为"追踪中任务"是完全独立的第三个状态，用绿色
    // 边框+单独置顶的"追踪中任务"分类(RebuildQuestList)表示，不影响展开/收起本身。
    // 收起/展开态整体都要再高一些(不是文字变大，是行高/间距的呼吸空间变大)——
    // 两个状态的具体倍率不一样，且很多高度常量是标题/节点链/当前目标这些共享
    // 方法里定义的，不想给每个方法都加一个scale参数，所以用这个字段记"当前
    // 该乘哪个倍率"，BuildQuestCard进两个分支前先设好，各处直接乘。
    private float _currentCardHeightScale = 1f;
    private const float CollapsedHeightScale = 1.3f;
    private const float ExpandedHeightScale = 1.4f;

    /// <summary>
    /// 在标题文字前面画任务标记图标。
    ///
    /// 做法是「图标绝对定位到左边 + 文字用 margin 让出同样宽度」，而不是把标题拆成
    /// 一个 HorizontalLayoutGroup。卡片布局已经是好几层嵌套的 Layout Group，再插一层
    /// 会连带影响 ShrinkTextToFit 的可用宽度计算和折叠/展开时的高度，风险远大于收益。
    ///
    /// 图标集缺失或该条目没有标记时什么都不画，文字也不缩进——不留空洞。
    /// </summary>
    private void AttachQuestMarkerIcon(RectTransform titleRt, TMP_Text titleText, QuestMarker marker)
    {
        if (titleRt == null || titleText == null || !marker.HasValue)
            return;

        QuestMarkerIconSet set = QuestMarkerIconSet.Instance;
        Sprite sprite = set != null ? set.GetSprite(marker) : null;
        if (sprite == null)
            return;

        float size = titleText.fontSize;
        float gap = size * 0.35f;

        var iconGo = new GameObject("QuestMarkerIcon", typeof(RectTransform), typeof(Image));
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.SetParent(titleRt, false);
        iconRt.anchorMin = new Vector2(0f, 1f);
        iconRt.anchorMax = new Vector2(0f, 1f);
        iconRt.pivot = new Vector2(0f, 1f);
        iconRt.sizeDelta = new Vector2(size, size);
        // 图标是方的、文字有行高，往下挪一点点才和字面视觉对齐。
        iconRt.anchoredPosition = new Vector2(0f, -size * 0.12f);

        var image = iconGo.GetComponent<Image>();
        image.sprite = sprite;
        image.color = set.GetColor(marker);
        image.raycastTarget = false;
        image.preserveAspect = true;

        Vector4 margin = titleText.margin;
        margin.x += size + gap;
        titleText.margin = margin;
    }

    private void BuildQuestCard(QuestDefinition quest, bool big)
    {
        bool isTracked = quest.questId == QuestRuntime.Instance?.GetTrackedQuestId();
        bool expanded = quest.questId == _expandedQuestId;
        _currentCardHeightScale = expanded ? ExpandedHeightScale : CollapsedHeightScale;

        var cardGo = new GameObject($"Quest_{quest.questId}", typeof(RectTransform));
        cardGo.transform.SetParent(_listContent, false);
        var cardRt = (RectTransform)cardGo.transform;

        var cardLayout = cardGo.AddComponent<VerticalLayoutGroup>();
        cardLayout.childAlignment = TextAnchor.UpperLeft;
        cardLayout.spacing = 10f * M * _currentCardHeightScale;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;
        cardLayout.padding = expanded && big
            ? new RectOffset((int)(20f * M), (int)(20f * M), (int)(16f * M * _currentCardHeightScale), (int)(16f * M * _currentCardHeightScale))
            : new RectOffset((int)(20f * M), (int)(20f * M), (int)(12f * M * _currentCardHeightScale), (int)(12f * M * _currentCardHeightScale));
        cardGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        cardGo.AddComponent<LayoutElement>();

        var cardBg = cardGo.AddComponent<Image>();
        cardBg.color = new Color(1f, 1f, 1f, expanded && big ? 0.04f : 0.02f);

        // 点击区域是整张卡片(不管展开/收起态)，不是只有标题/节点链那一小条——
        // "收起有效位置太少"就是之前只在Header/紧凑行那一小块加Button导致的。
        // CanvasGroup用来做切换时的淡入淡出，不是瞬间切换，让这个操作有"过程感"。
        var cardCanvasGroup = cardGo.AddComponent<CanvasGroup>();
        var cardButton = cardGo.AddComponent<Button>();
        cardButton.transition = Selectable.Transition.None;
        QuestDefinition capturedQuestToggle = quest;
        bool wasExpandedToggle = expanded;
        cardButton.onClick.AddListener(() =>
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            StartCoroutine(ToggleExpandedWithFade(cardCanvasGroup, capturedQuestToggle, wasExpandedToggle));
        });

        // 光标(键盘/手柄)停在这张卡片上时，靠背景色调深来表示"聚焦"，不是靠额外画
        // 一圈边框——边框颜色已经被"是否追踪中"占用了(isTracked=冷绿/白)，再叠一层
        // 独立的高亮边框会跟追踪中的冷绿边框重叠。背景色调深是完全独立的视觉维度，
        // 不管这张卡是不是追踪中都不会跟边框颜色打架。
        _cardFocusEntries.Add((cardButton, cardBg, cardBg.color.a));

        // 追踪中任务(T键切换追踪状态，见UpdateTrackQuestHotkey)用冷绿色边框高亮，
        // 其余卡片用普通白色细边框——参考图里"聚焦"的那张卡片边框明显更显眼，
        // 不是所有卡片边框长得一样。
        // 不用共享的AddOutline——那个方法画完线之后没机会在"创建的同一时刻"就设置
        // ignoreLayout，事后用Find()按名字补丁不够直接。这里自己画四条线，创建时
        // 就带上ignoreLayout，不脱离VerticalLayoutGroup(cardLayout)的控制就会被当成
        // 普通排版子物体重新摆位/压扁。粗细也乘上M(之前AddOutline调用漏乘了)。
        BuildCardOutline(cardRt,
            isTracked ? new Color(SkyPrisonUIPalette.ColdGreen.r, SkyPrisonUIPalette.ColdGreen.g, SkyPrisonUIPalette.ColdGreen.b, 0.8f)
                      : new Color(1f, 1f, 1f, 0.25f),
            (isTracked ? 2.5f : 1.5f) * M);

        if (big)
        {
            // 配图是整张卡片的背景板，收起态也要有(不是只有展开才显示)——铺满
            // cardRt当前的实际尺寸，收起态卡片矮，图片跟着矮，不会跑到边框外面。
            // 不是单独占一条横向色带——文字/节点链应该叠在同一张图上面。用
            // ignoreLayout 让它脱离 VerticalLayoutGroup 的排版流程，直接铺满卡片
            // 最终尺寸(不管 ContentSizeFitter 把高度撑到多少)，再叠一层暗色遮罩
            // 保证文字在任何图片内容上都可读。两者都要排在最前面(sibling index靠前)，
            // 才会画在后面加进来的文字/节点链下方，不会盖住它们。
            var heroRt = SkyPrisonFloatingWindowKit.MakeRect("Hero", cardRt, Vector2.zero, Vector2.one);
            heroRt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            heroRt.SetAsFirstSibling();
            Image heroImg = heroRt.gameObject.AddComponent<Image>();
            if (quest.heroImage != null)
            {
                heroImg.sprite = quest.heroImage;
                heroImg.type = Image.Type.Simple;
                heroImg.preserveAspect = false;
            }
            else
            {
                // 没配美术资源时的占位色块——不是真的空白，玩家能看出"这里将来是一张图"，
                // 不是渲染出错。用户明确说过"没有美术资源可以先留位"。
                heroImg.color = new Color(1f, 1f, 1f, 0.06f);
            }

            // 左边保留图片本身细节，右边(标题/节点链/文字所在区域)过渡到卡片的暗色调——
            // 不是整张图压一个统一的半透明色，那样看起来像蒙了层灰纱，不是"图片融进UI"
            // 的渐隐效果。
            var tintRt = SkyPrisonFloatingWindowKit.MakeRect("HeroTint", cardRt, Vector2.zero, Vector2.one);
            tintRt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            tintRt.SetSiblingIndex(1);
            var tintImg = tintRt.gameObject.AddComponent<Image>();
            tintImg.sprite = GetHeroTintGradientSprite();
            tintImg.type = Image.Type.Simple;
        }

        if (expanded)
        {
            // 展开态：标题(分类+标题)在左，节点链在右，同一行，节点链吃掉标题让出来
            // 的所有剩余空间(flexibleWidth)——不是标题占满剩余空间、节点链挤在角落，
            // 那样节点链之间的连线会很短，跟参考图不一致。主线/支线展开态共用这一份，
            // 差别只在于主线多一张Hero配图。
            var headerRt = SkyPrisonFloatingWindowKit.MakeRect("Header", cardRt, Vector2.zero, Vector2.one);
            var headerLayout = headerRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerLayout.childAlignment = TextAnchor.UpperLeft;
            headerLayout.spacing = 24f * M;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = false;

            BuildTitleBlock(headerRt, quest, big, flexibleWidth: false);
            RectTransform nodeChainRt = BuildNodeChain(headerRt, quest.objectives, quest.questId, quest.giverNpc);

            var alignTarget = new NodeAlignmentTarget { headerRt = headerRt, nodeChainRt = nodeChainRt };

            QuestObjective current = quest.GetCurrentObjective();
            if (current != null)
            {
                var currentRt = SkyPrisonFloatingWindowKit.MakeRect("CurrentObjective", cardRt, Vector2.zero, Vector2.one);
                var currentLayout = currentRt.gameObject.AddComponent<VerticalLayoutGroup>();
                currentLayout.childAlignment = TextAnchor.UpperLeft;
                currentLayout.spacing = 4f * M * _currentCardHeightScale;
                currentLayout.childControlWidth = true;
                currentLayout.childControlHeight = true;
                currentRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                currentRt.gameObject.AddComponent<LayoutElement>();
                alignTarget.currentLayout = currentLayout;

                var labelRt = SkyPrisonFloatingWindowKit.MakeRect("Label", currentRt, Vector2.zero, Vector2.one);
                labelRt.gameObject.AddComponent<LayoutElement>().preferredHeight = ObjectiveFontSize * 1.1f * _currentCardHeightScale;
                TMP_Text labelText = SkyPrisonFloatingWindowKit.MakeText(labelRt, "Text",
                    L("questlog_current_objective", "当前目标"), ObjectiveFontSize, FontStyles.Bold, _font);
                labelText.alignment = TextAlignmentOptions.TopLeft;
                labelText.color = SkyPrisonUIPalette.ColdGreen;
                labelText.enableWordWrapping = false;
                labelText.overflowMode = TextOverflowModes.Overflow;

                BuildObjectiveDetailText(currentRt, current, TitleFontSize, quest.questId, quest.giverNpc);
            }

            alignTarget.dividerLayout = BuildHorizontalDivider(cardRt);
            alignTarget.infoRowLayout = BuildInfoRow(cardRt, quest.GetLocalizedRegionDisplayName(), quest.giverNpc, quest.hazardRank);
            _pendingNodeAlignments.Add(alignTarget);
        }
        else
        {
            // 收起态：紧凑单行(标题+节点链+区域/委托人+箭头)，点这一行展开/收起——
            // 只切换"是否在看这条任务详情"，不影响追踪中任务状态(两者已经拆开成
            // 独立操作，追踪改用专门的T键，见UpdateTrackQuestHotkey)。主线/支线
            // 共用同一份。
            var rowRt = SkyPrisonFloatingWindowKit.MakeRect("CompactRow", cardRt, Vector2.zero, Vector2.one);
            // 收起态卡片的总高度(padding+这一行)要跟NO REQUEST占位框的高度对齐——
            // 直接拿EmptyPlaceholderHeight减掉收起态自己的上下padding，而不是另外
            // 猜一个数字，这样两者的最终高度能精确一致。
            float collapsedVerticalPadding = 2f * 12f * M * _currentCardHeightScale;
            rowRt.gameObject.AddComponent<LayoutElement>().preferredHeight =
                EmptyPlaceholderHeight - collapsedVerticalPadding;
            var rowLayout = rowRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.spacing = 28f * M;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            BuildTitleBlock(rowRt, quest, big, flexibleWidth: false);
            BuildNodeChain(rowRt, quest.objectives, quest.questId, quest.giverNpc, compactLabels: true);
            BuildVerticalDivider(rowRt);
            BuildCompactInfoBlock(rowRt, quest.GetLocalizedRegionDisplayName(), quest.giverNpc, quest.hazardRank);
            BuildExpandChevron(rowRt, false);
        }
    }

    private const float ToggleFadeDuration = 0.12f;

    /// <summary>点卡片切换展开/收起(纯粹是"我想看这条任务详情"，不再顺带切换首要
    /// 任务——两件事之前绑在一起，用户明确要求拆开)——先把当前卡片淡出，再真正
    /// 切换状态触发整个列表重建，重建完再把新生成的卡片淡入。不是瞬间切换，
    /// 让这个操作有"过程感"。旧卡片如果在淡出途中被(每0.5秒一次的)自动刷新
    /// 提前销毁，每帧都做null检查，不会报错，只是少一小段淡出效果。</summary>
    private IEnumerator ToggleExpandedWithFade(CanvasGroup outgoingGroup, QuestDefinition quest, bool wasExpanded)
    {
        float t = 0f;
        while (t < ToggleFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            if (outgoingGroup != null)
                outgoingGroup.alpha = 1f - Mathf.Clamp01(t / ToggleFadeDuration);
            yield return null;
        }

        _expandedQuestId = wasExpanded ? "" : quest.questId;
        RebuildQuestList();

        if (_listContent == null) yield break;
        Transform newCardT = _listContent.Find($"Quest_{quest.questId}");
        CanvasGroup newGroup = newCardT != null ? newCardT.GetComponent<CanvasGroup>() : null;
        if (newGroup == null) yield break;

        newGroup.alpha = 0f;
        float ft = 0f;
        while (ft < ToggleFadeDuration)
        {
            ft += Time.unscaledDeltaTime;
            if (newGroup == null) yield break;
            newGroup.alpha = Mathf.Clamp01(ft / ToggleFadeDuration);
            yield return null;
        }
        newGroup.alpha = 1f;
    }

    /// <summary>分类标记(主线/支线) + 标题——大卡片/支线折叠行共用同一份，不用各写
    /// 一份"两行文字堆叠"的布局。flexibleWidth传false=按文字自然宽度显示(两种卡片
    /// 现在都是这样，节点链才是那个该吃掉剩余空间的元素)。</summary>
    // 标题栏这一块占的固定宽度直接决定了节点链从哪里开始——实际标题文字("余寓
    // 生活支援"这种)远用不到640/420这么宽，收窄之后节点链的起点会明显往左挪，
    // 不是靠调节点链内部的padding这种边角料。
    private const float TitleBlockWidthBig = 280f * M;
    private const float TitleBlockWidthCompact = 180f * M;

    private void BuildTitleBlock(RectTransform parent, QuestDefinition quest, bool big, bool flexibleWidth)
    {
        var titleBlockRt = SkyPrisonFloatingWindowKit.MakeRect("TitleBlock", parent, Vector2.zero, Vector2.zero);
        var titleBlockLe = titleBlockRt.gameObject.AddComponent<LayoutElement>();
        if (flexibleWidth)
        {
            titleBlockLe.flexibleWidth = 1f;
        }
        else
        {
            // 之前让它靠子物体(标题/分类文字)自己的TMP preferredWidth反向撑出宽度——
            // 刚创建出来那一帧TMP文字还没跑完排版，preferredWidth读到的是不可靠的
            // 临时值(经常是0或很小)，表现就是标题被从左边"吃掉"一截，跟这份文件里
            // 之前排查过的"任务日志分区标题截断"是同一类问题(都是靠临时preferredWidth
            // 反推容器宽度)。改成直接给一个固定宽度，不依赖任何一帧才准的动态测量。
            titleBlockLe.preferredWidth = big ? TitleBlockWidthBig : TitleBlockWidthCompact;
        }
        var titleBlockLayout = titleBlockRt.gameObject.AddComponent<VerticalLayoutGroup>();
        titleBlockLayout.childAlignment = TextAnchor.UpperLeft;
        titleBlockLayout.spacing = 4f * M * _currentCardHeightScale;
        titleBlockLayout.childControlWidth = true;
        titleBlockLayout.childControlHeight = true;

        var categoryRt = SkyPrisonFloatingWindowKit.MakeRect("Category", titleBlockRt, Vector2.zero, Vector2.one);
        categoryRt.gameObject.AddComponent<LayoutElement>().preferredHeight = ObjectiveFontSize * 1.1f * _currentCardHeightScale;
        TMP_Text categoryText = SkyPrisonFloatingWindowKit.MakeText(categoryRt, "Label",
            quest.category == QuestCategory.Mainline ? L("questlog_section_mainline", "主线任务") : L("questlog_section_side", "支线任务"),
            ObjectiveFontSize, FontStyles.Bold, _font);
        categoryText.alignment = TextAlignmentOptions.TopLeft;
        categoryText.color = quest.category == QuestCategory.Mainline ? SkyPrisonUIPalette.ColdGreen : SideQuestBlue;
        categoryText.enableWordWrapping = false;
        categoryText.overflowMode = TextOverflowModes.Overflow;

        var titleTextRt = SkyPrisonFloatingWindowKit.MakeRect("Title", titleBlockRt, Vector2.zero, Vector2.one);
        titleTextRt.gameObject.AddComponent<LayoutElement>().preferredHeight = TitleFontSize * 1.4f * _currentCardHeightScale;
        TMP_Text titleText = SkyPrisonFloatingWindowKit.MakeText(titleTextRt, "Label",
            quest.GetLocalizedTitle(quest.questId), big ? TitleFontSize * 1.3f : TitleFontSize, FontStyles.Bold, _font);
        titleText.alignment = TextAlignmentOptions.TopLeft;
        titleText.color = Color.white;
        titleText.enableWordWrapping = false;
        titleText.overflowMode = TextOverflowModes.Overflow;

        AttachQuestMarkerIcon(titleTextRt, titleText, QuestMarkerResolver.ResolveForQuest(quest));

        // 英文比中/日文长得多(同样意思要更多字母)，标题栏是固定宽度(TitleBlockWidthBig/
        // Compact)——不缩字号的话英文标题会直接撞上节点链/紧凑行的其它元素。跟
        // InventoryItemDetailPanel.ShrinkNameToFit同一套做法：从最大字号量宽度，超了
        // 就降一档，直到测量结果不超或者已经降到下限为止，不依赖TMP内置的
        // enableAutoSizing(这份文件其它地方也验证过在这种运行时现造UI场景下不生效)。
        if (!flexibleWidth)
        {
            float availableWidth = big ? TitleBlockWidthBig : TitleBlockWidthCompact;
            float minFontSize = big ? TitleFontSize : TitleFontSize * 0.8f;
            ShrinkTextToFit(titleText, availableWidth, minFontSize);
        }
    }

    /// <summary>手动缩字号直到测量宽度塞进availableWidth，或者已经降到minFontSize
    /// 下限——不依赖TMP的enableAutoSizing(试过在运行时现造UI场景下不生效，见
    /// InventoryItemDetailPanel.ShrinkNameToFit同款注释)。</summary>
    private static void ShrinkTextToFit(TMP_Text text, float availableWidth, float minFontSize)
    {
        float size = text.fontSize;
        text.ForceMeshUpdate();
        for (int i = 0; i < 20 && size > minFontSize; i++)
        {
            float width = text.GetPreferredValues(text.text, 0f, 0f).x;
            if (width <= availableWidth) break;

            size -= 2f;
            if (size < minFontSize) size = minFontSize;
            text.fontSize = size;
            text.ForceMeshUpdate();
        }
    }

    /// <summary>节点链标签用——只有换行会超过maxLines(目前固定2行)才缩字号，宽度本身
    /// 已经通过NodeLabelWidth留够常见情况的余量，缩字号只是给日/英这类比中文自然
    /// 更长的翻译兜底，不是常态触发。跟ShrinkTextToFit同一套"量了超再降一档"的
    /// 手动循环写法(TMP的enableAutoSizing在这种运行时现造UI场景下不生效，这份文件
    /// 别处已经验证过)，只是判断条件从"测量宽度"换成"实际换行行数"。</summary>
    private static void ShrinkTextToMaxLines(TMP_Text text, int maxLines, float minFontSize)
    {
        float size = text.fontSize;
        text.ForceMeshUpdate();
        for (int i = 0; i < 20 && size > minFontSize; i++)
        {
            if (text.textInfo.lineCount <= maxLines) break;

            size -= 1f;
            if (size < minFontSize) size = minFontSize;
            text.fontSize = size;
            text.ForceMeshUpdate();
        }
    }

    /// <summary>卡片自己画一圈边框——四条线创建的同一时刻就带上ignoreLayout，不用
    /// 事后再靠Find()按名字补丁，避免"这一帧还没找到/找错对象"这类不确定性。</summary>
    private void BuildCardOutline(RectTransform cardRt, Color color, float thickness)
    {
        BuildCardOutlineLine(cardRt, "OT", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, thickness), color);
        BuildCardOutlineLine(cardRt, "OB", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, thickness), color);
        BuildCardOutlineLine(cardRt, "OL", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(thickness, 0f), color);
        BuildCardOutlineLine(cardRt, "OR", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(thickness, 0f), color);
    }

    private void BuildCardOutlineLine(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        go.AddComponent<LayoutElement>().ignoreLayout = true;
    }

    private const float DividerThickness = 2f * M;

    /// <summary>返回内层的HorizontalLayoutGroup，调用方(RebuildQuestList最后那一段
    /// 节点对齐处理)量到实际偏移量之后拿它设置padding.left——外层Divider槽位受
    /// cardLayout(childControlWidth=true)控制满宽，直接改它自己的offsetMin没用，
    /// 每次布局都会被重置，只有内层自己的HorizontalLayoutGroup.padding能生效。</summary>
    private HorizontalLayoutGroup BuildHorizontalDivider(RectTransform parent)
    {
        var lineRt = SkyPrisonFloatingWindowKit.MakeRect("Divider", parent, Vector2.zero, Vector2.one);
        lineRt.gameObject.AddComponent<LayoutElement>().preferredHeight = DividerThickness;
        var insetLayout = lineRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        insetLayout.childControlWidth = true;
        insetLayout.childControlHeight = true;
        insetLayout.childForceExpandWidth = true;
        insetLayout.childForceExpandHeight = true;
        var barRt = SkyPrisonFloatingWindowKit.MakeRect("Bar", lineRt, Vector2.zero, Vector2.one);
        barRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
        return insetLayout;
    }

    private void BuildVerticalDivider(RectTransform parent)
    {
        var lineRt = SkyPrisonFloatingWindowKit.MakeRect("Divider", parent, Vector2.zero, Vector2.one);
        lineRt.gameObject.AddComponent<LayoutElement>().preferredWidth = DividerThickness;
        lineRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
    }

    /// <summary>支线折叠行右侧的区域/委托人——跟大卡片底部那条横向InfoRow不是同一份
    /// (那个是独立整行、这个要嵌进折叠行当中间偏右的一栏)，两行文字纵向堆叠，
    /// 按自然宽度显示，不抢节点链的空间。</summary>
    // 加了区域/委托人左边的图标之后，文字可用宽度被图标+间距吃掉一截——之前这个
    // 宽度刚好够放下文字，图标一加进去就把"委托人"这种较长的文字往外挤，显示不全。
    // 加宽这个常量补偿掉图标占用的空间，让文字拿到的可用宽度跟加图标之前一样。
    private const float CompactInfoBlockWidth = 380f * M + 50f * M;

    private void BuildCompactInfoBlock(RectTransform parent, string region, UnitDefinition giver, int hazardRank)
    {
        var blockRt = SkyPrisonFloatingWindowKit.MakeRect("InfoBlock", parent, Vector2.zero, Vector2.zero);
        // 跟TitleBlock同一个坑：不能让这个容器的宽度靠里面Region/Giver这两个TMP文字
        // 刚创建那一帧还不可靠的preferredWidth反向撑出来，会导致文字挤在一个几乎
        // 是0宽度的容器里，表现成文字位置扭曲/挤在一起。直接给固定宽度。
        blockRt.gameObject.AddComponent<LayoutElement>().preferredWidth = CompactInfoBlockWidth;
        var blockLayout = blockRt.gameObject.AddComponent<VerticalLayoutGroup>();
        blockLayout.childAlignment = TextAnchor.MiddleLeft;
        blockLayout.spacing = 2f * M * _currentCardHeightScale;
        blockLayout.childControlWidth = true;
        blockLayout.childControlHeight = true;

        // 字号之前用ObjectiveFontSize(项目里给小号说明文字用的)，跟参考图里
        // 正常可读大小、真正当成一行内容看的分量对不上，看起来发虚发小——
        // 放大一档，跟标题不是同一档但也不是脚注小字。
        const float compactInfoFontSize = ObjectiveFontSize * 1.3f;

        float compactInfoRowHeight = compactInfoFontSize * 1.2f * _currentCardHeightScale;

        if (!string.IsNullOrWhiteSpace(region))
        {
            BuildIconLabelRow(blockRt, GetRegionIconSprite(),
                $"{L("questlog_field_region", "区域")}: {region}", compactInfoFontSize, compactInfoRowHeight);
        }
        if (giver != null)
        {
            // 统一走GetLocalizedDisplayName()，不要直接读displayName原始字段——
            // 之前这里切日文/英文委托人名字还是中文，就是漏了这一层，UnitDefinition
            // 自己的注释里明确点名"好几处直接读displayName"这个坑，这里就是其中一处。
            string giverName = giver.GetLocalizedDisplayName();
            BuildIconLabelRow(blockRt, GetGiverIconSprite(),
                $"{L("questlog_field_giver", "委托人")}: {giverName}", compactInfoFontSize, compactInfoRowHeight);
        }

        BuildHazardRankText(blockRt, hazardRank, compactInfoFontSize, compactInfoFontSize * 1.2f * _currentCardHeightScale);
    }

    /// <summary>纯文字版HAZARD RANK——不是接受任务确认框里那种带边框的徽标(那个固定
    /// 宽度算错过一次，直接把标题挤出框外)，这里就是一行跟区域/委托人同规格的文字，
    /// 颜色按等级从冷绿到暖红插值。</summary>
    private void BuildHazardRankText(RectTransform parent, int hazardRank, float fontSize, float rowHeight,
        float preferredWidth = -1f, bool rightAlign = false)
    {
        hazardRank = Mathf.Clamp(hazardRank, 1, 10);
        string dots = new string('◆', hazardRank) + new string('◇', 10 - hazardRank);
        Color badgeColor = Color.Lerp(SkyPrisonUIPalette.ColdGreen, SkyPrisonUIPalette.WarmRed, (hazardRank - 1) / 9f);

        var t = SkyPrisonFloatingWindowKit.MakeText(parent, "Hazard",
            $"{L("quest_hazard_rank_label", "HAZARD RANK")} {dots}", fontSize, FontStyles.Normal, _font);
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = rowHeight;
        // 横向排列(BuildInfoRow)时必须给固定宽度，跟同一行的区域/委托人一样，不能
        // 靠TMP刚创建那一帧不可靠的preferredWidth反推；纵向堆叠(BuildCompactInfoBlock)
        // 时父级自己会把宽度撑满，不用额外指定，传-1跳过。
        if (preferredWidth > 0f) le.preferredWidth = preferredWidth;
        // rightAlign=true时吃掉同一行里区域/委托人让出来的所有剩余空间，文字本身
        // 也右对齐，贴在整行最右边，不是紧跟在委托人后面。
        if (rightAlign) le.flexibleWidth = 1f;
        t.alignment = rightAlign ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        t.color = badgeColor;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
    }

    /// <summary>折叠行最右边的展开指示箭头——展开时转90度指向下方，跟通用的"这里
    /// 还有更多内容"手势一致，不用额外文字提示。</summary>
    private void BuildExpandChevron(RectTransform parent, bool expanded)
    {
        var chevronRt = SkyPrisonFloatingWindowKit.MakeRect("Chevron", parent, Vector2.zero, Vector2.zero);
        chevronRt.gameObject.AddComponent<LayoutElement>().preferredWidth = ObjectiveFontSize * 1.4f;
        // 图形箭头，不是字符">"——照抄CharacterPanelController.GetPreviewArrowSprite()
        // 那套程序化生成方式(装备属性对比"xxx > xxx"用的同一个箭头形状)，收起态
        // 指向右，展开态转90度指向下方呈v字形。
        var chevronImg = chevronRt.gameObject.AddComponent<Image>();
        chevronImg.sprite = GetChevronArrowSprite();
        chevronImg.color = new Color(1f, 1f, 1f, 0.5f);
        chevronImg.raycastTarget = false;
        chevronImg.preserveAspect = true;
        chevronRt.localEulerAngles = expanded ? new Vector3(0f, 0f, -90f) : Vector3.zero;
    }

    private static Sprite _glowSprite;

    /// <summary>柔和径向渐变光晕——中心亮、往边缘平滑衰减到全透明，垫在已完成
    /// 节点图标下面模拟发光，程序化生成贴图，不是真的屏幕空间Bloom。</summary>
    private static Sprite GetGlowSprite()
    {
        if (_glowSprite != null) return _glowSprite;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        float center = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float t = Mathf.Clamp01(dist / center);
                float alpha = Mathf.Pow(1f - t, 2.2f); // 越靠中心越亮，指数让边缘收得更柔和
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        _glowSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        return _glowSprite;
    }

    private static Sprite _chevronArrowSprite;

    /// <summary>右尖箭头(">"形)——跟CharacterPanelController.GetPreviewArrowSprite()
    /// 同一套生成方式(两条斜线在右侧收拢成一个尖，左侧张开，中间没有竖直的背边)，
    /// 那份是私有静态方法，没法直接复用，照抄一份。</summary>
    private static Sprite GetChevronArrowSprite()
    {
        if (_chevronArrowSprite != null) return _chevronArrowSprite;

        const int size = 64;
        const float strokeHalf = 0.11f;
        const float amp = 0.42f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = y / (float)(size - 1);
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)(size - 1);
                float topLineY = 0.5f + amp * (1f - nx);
                float bottomLineY = 0.5f - amp * (1f - nx);
                bool inside = Mathf.Abs(ny - topLineY) <= strokeHalf || Mathf.Abs(ny - bottomLineY) <= strokeHalf;
                pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        _chevronArrowSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        return _chevronArrowSprite;
    }

    // 区域/委托人左边的小图标——放在Resources/UI下(跟窗口关闭按钮那个图标同一套
    // 加载方式)，不是挂在Inspector序列化字段上，缓存成静态字段避免重复Resources.Load。
    private static Sprite _regionIconSprite;
    private static Sprite _giverIconSprite;

    private static Sprite GetRegionIconSprite()
    {
        if (_regionIconSprite == null) _regionIconSprite = Resources.Load<Sprite>("UI/Icon_QuestRegion");
        return _regionIconSprite;
    }

    private static Sprite GetGiverIconSprite()
    {
        if (_giverIconSprite == null) _giverIconSprite = Resources.Load<Sprite>("UI/Icon_QuestGiver");
        return _giverIconSprite;
    }

    /// <summary>图标+文字这一小组合——BuildInfoRow(横向排列，要给整组一个固定宽度
    /// preferredWidth跟同一行的其它元素对齐)和BuildCompactInfoBlock(纵向堆叠，
    /// 父级VerticalLayoutGroup自己会撑满宽度，不用传)共用同一份。找不到图标(比如
    /// 资源还没放进去)就只显示文字，不会因为拿不到Sprite而整行消失。</summary>
    private void BuildIconLabelRow(RectTransform parent, Sprite icon, string label, float fontSize, float rowHeight, float preferredWidth = -1f)
    {
        var groupRt = SkyPrisonFloatingWindowKit.MakeRect("IconLabel", parent, Vector2.zero, Vector2.zero);
        var groupLe = groupRt.gameObject.AddComponent<LayoutElement>();
        groupLe.preferredHeight = rowHeight;
        if (preferredWidth > 0f) groupLe.preferredWidth = preferredWidth;
        var groupLayout = groupRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        groupLayout.childAlignment = TextAnchor.MiddleLeft;
        groupLayout.spacing = 8f * M;
        groupLayout.childControlWidth = true;
        groupLayout.childControlHeight = true;
        groupLayout.childForceExpandWidth = false;
        // 这份文件反复踩过的坑：childForceExpandHeight不显式设置默认是true——这一行的
        // preferredHeight(rowHeight)比图标本身的方形尺寸(iconSize)高，图标会被强行
        // 撑高变成非正方形的框，配合preserveAspect实际画出来的图会比框小一圈、
        // 图标看起来没填满/显示不全。显式关掉，图标框保持真正的iconSize正方形。
        groupLayout.childForceExpandHeight = false;

        // 先建文字、量出它实际渲染的字形高度(不是行高，行高比字形本身的墨水高度
        // 多留了一截行距空间)——图标应该跟"字看起来多高"对齐，不是跟这一行分到的
        // 排版空间对齐，之前不管是按fontSize硬乘系数、还是按rowHeight打折，本质
        // 都是在猜，猜出来的数字要么太大要么跟文字对不上。ForceMeshUpdate之后
        // textBounds.size.y就是TMP自己算出来的真实字形包围盒高度，不用再猜。
        var textRt = SkyPrisonFloatingWindowKit.MakeRect("Text", groupRt, Vector2.zero, Vector2.zero);
        textRt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        TMP_Text t = SkyPrisonFloatingWindowKit.MakeText(textRt, "Label", label, fontSize, FontStyles.Normal, _font);
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.color = new Color(1f, 1f, 1f, 0.7f);
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        t.ForceMeshUpdate();
        float measuredTextHeight = t.textBounds.size.y;

        if (icon != null)
        {
            // 图标跟文字实际字形高度对齐，再兜底不超过这一行分到的高度(rowHeight)，
            // 双重保险：既不会因为按行高/字号瞎猜导致比文字大一截，也不会因为量出来
            // 的字形高度异常(比如空字符串)撑爆这一行。
            float iconSize = Mathf.Min(measuredTextHeight > 0f ? measuredTextHeight : fontSize, rowHeight * 0.9f);
            var iconRt = SkyPrisonFloatingWindowKit.MakeRect("Icon", groupRt, Vector2.zero, Vector2.zero);
            iconRt.SetSiblingIndex(0); // 图标要排在文字左边，创建顺序是先文字后图标，靠sibling index摆回左边
            var iconLe = iconRt.gameObject.AddComponent<LayoutElement>();
            iconLe.preferredWidth = iconSize;
            iconLe.preferredHeight = iconSize;
            iconLe.flexibleWidth = 0f;
            iconLe.flexibleHeight = 0f;
            var iconImg = iconRt.gameObject.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
        }
    }

    /// <summary>返回rowLayout，调用方(RebuildQuestList最后那一段节点对齐处理)量到
    /// 实际偏移量之后拿它设置padding.left，跟BuildHorizontalDivider同一个道理。</summary>
    private HorizontalLayoutGroup BuildInfoRow(RectTransform parent, string region, UnitDefinition giver, int hazardRank)
    {

        var rowRt = SkyPrisonFloatingWindowKit.MakeRect("InfoRow", parent, Vector2.zero, Vector2.one);
        rowRt.gameObject.AddComponent<LayoutElement>().preferredHeight = ObjectiveFontSize * 1.2f * _currentCardHeightScale;
        var rowLayout = rowRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.spacing = 40f * M;
        // 之前childControlWidth=false，两条文字各自是MakeText默认的满铺(0,0)-(1,1)
        // 锚点，HorizontalLayoutGroup不控制宽度的话谁都不会被收窄，两条文字整个
        // 叠在一起——同一行看起来"扭曲"就是这个原因。改成true，让下面手动给的
        // preferredWidth真正生效，各自收到自己该有的宽度，不再重叠。
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;

        // 字号之前用ObjectiveFontSize(小号说明文字规格)，跟展开态整块内容的分量比
        // 显得发虚——放大一档，跟收起态紧凑行那次的调整同一个道理。
        const float infoRowFontSize = ObjectiveFontSize * 1.3f;

        float infoRowHeight = ObjectiveFontSize * 1.2f * _currentCardHeightScale;

        if (!string.IsNullOrWhiteSpace(region))
        {
            BuildIconLabelRow(rowRt, GetRegionIconSprite(),
                $"{L("questlog_field_region", "区域")}: {region}", infoRowFontSize, infoRowHeight, InfoRowFieldWidth);
        }
        if (giver != null)
        {
            // 统一走GetLocalizedDisplayName()，不要直接读displayName原始字段——
            // 之前这里切日文/英文委托人名字还是中文，就是漏了这一层，UnitDefinition
            // 自己的注释里明确点名"好几处直接读displayName"这个坑，这里就是其中一处。
            string giverName = giver.GetLocalizedDisplayName();
            BuildIconLabelRow(rowRt, GetGiverIconSprite(),
                $"{L("questlog_field_giver", "委托人")}: {giverName}", infoRowFontSize, infoRowHeight, InfoRowFieldWidth);
        }

        // HAZARD RANK靠右对齐——给flexibleWidth吃掉区域/委托人让出来的所有剩余
        // 空间，文字本身也右对齐，贴在这一行的最右边，不是紧跟着委托人后面。
        BuildHazardRankText(rowRt, hazardRank, infoRowFontSize, ObjectiveFontSize * 1.2f * _currentCardHeightScale,
            preferredWidth: -1f, rightAlign: true);

        return rowLayout;
    }

    // 同样的道理——加宽补偿图标+间距占掉的宽度，避免"委托人"这种较长文字被挤到
    // 显示不全。
    private const float InfoRowFieldWidth = 420f * M + 50f * M;

    // 人物记录卡片——跟支线任务同一套紧凑布局，只是配色换成紫色(区分"这是另一套
    // 系统"，不是支线任务的一种)，右侧用状态文字代替危险等级徽标。
    private void BuildCharacterArcCard(CharacterArcDefinition arc)
    {
        var cardGo = new GameObject($"Arc_{arc.arcId}", typeof(RectTransform));
        cardGo.transform.SetParent(_listContent, false);
        var cardRt = (RectTransform)cardGo.transform;

        var cardLayout = cardGo.AddComponent<VerticalLayoutGroup>();
        cardLayout.childAlignment = TextAnchor.UpperLeft;
        cardLayout.spacing = 10f * M;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.padding = new RectOffset((int)(20f * M), (int)(20f * M), (int)(12f * M), (int)(12f * M));
        cardGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        cardGo.AddComponent<LayoutElement>();
        cardGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);

        Color purple = new Color(0.62f, 0.5f, 0.95f, 1f);

        var titleRowRt = SkyPrisonFloatingWindowKit.MakeRect("TitleRow", cardRt, Vector2.zero, Vector2.one);
        titleRowRt.gameObject.AddComponent<LayoutElement>().preferredHeight = TitleFontSize * 1.4f;
        var titleRowLayout = titleRowRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        titleRowLayout.childAlignment = TextAnchor.MiddleLeft;
        titleRowLayout.spacing = 20f * M;
        titleRowLayout.childControlWidth = true;
        titleRowLayout.childControlHeight = true;

        var titleTextRt = SkyPrisonFloatingWindowKit.MakeRect("Title", titleRowRt, Vector2.zero, Vector2.zero);
        titleTextRt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        // 同一个坑：直接读displayName原始字段，切日文/英文名字不跟着变，统一走
        // GetLocalizedDisplayName()。
        string npcName = arc.npc != null ? arc.npc.GetLocalizedDisplayName() + "：" : "";
        TMP_Text titleText = SkyPrisonFloatingWindowKit.MakeText(titleTextRt, "Label",
            npcName + arc.GetLocalizedTitle(arc.arcId), TitleFontSize, FontStyles.Bold, _font);
        titleText.alignment = TextAlignmentOptions.TopLeft;
        titleText.color = purple;

        AttachQuestMarkerIcon(titleTextRt, titleText, QuestMarkerResolver.ResolveForArc(arc));

        bool complete = arc.IsComplete();
        var stateRt = SkyPrisonFloatingWindowKit.MakeRect("State", titleRowRt, Vector2.zero, Vector2.zero);
        var stateLe = stateRt.gameObject.AddComponent<LayoutElement>();
        stateLe.preferredWidth = 6 * ObjectiveFontSize;
        TMP_Text stateText = SkyPrisonFloatingWindowKit.MakeText(stateRt, "Label",
            $"{L("questlog_field_state", "状态")}: {(complete ? L("questlog_state_complete", "已完成") : L("questlog_state_inprogress", "进行中"))}",
            ObjectiveFontSize, FontStyles.Bold, _font);
        stateText.alignment = TextAlignmentOptions.MidlineRight;
        stateText.color = complete ? SkyPrisonUIPalette.ColdGreen : new Color(1f, 1f, 1f, 0.6f);

        BuildNodeChain(cardRt, arc.stages);
    }

    /// <summary>横向节点链进度条——每个步骤一个圆点(已完成=冷绿实心+勾，当前=空心
    /// 高亮环，未到=暗淡空心环)，圆点之间用连线连起来。任务目标/人物记录阶段共用
    /// 同一份绘制逻辑(都是 List&lt;QuestObjective&gt;)，不用为两套系统各写一份。
    /// questIdForBaseline传任务的questId时，判定目标是否完成会套用起点快照上下文
    /// (跟QuestDefinition.AreRequiredObjectivesComplete同一套)，接任务这个动作本身
    /// 不会被误判成"已经推进了目标"；人物记录阶段(arc.stages)不是任务，传null，
    /// 行为不变。之前这里漏了套这层上下文，导致节点链显示的勾选状态跟真正的任务
    /// 完成判定不一致——ScanAll()那边不会秒过，但UI上看起来已经打勾。</summary>
    private RectTransform BuildNodeChain(RectTransform parent, List<QuestObjective> steps, string questIdForBaseline = null, UnitDefinition giverNpc = null, bool compactLabels = false)
    {
        if (steps == null || steps.Count == 0) return null;

        if (questIdForBaseline != null)
            TriggerConditionEvaluator.BeginQuestObjectiveContext(questIdForBaseline);

        int currentIndex;
        try
        {
            currentIndex = steps.Count;
            for (int i = 0; i < steps.Count; i++)
                if (!QuestDefinition.IsObjectiveEffectivelyComplete(questIdForBaseline, i, steps[i])) { currentIndex = i; break; }
        }
        finally
        {
            if (questIdForBaseline != null)
                TriggerConditionEvaluator.EndQuestObjectiveContext();
        }

        var rowRt = SkyPrisonFloatingWindowKit.MakeRect("NodeChain", parent, Vector2.zero, Vector2.one);
        var rowLe = rowRt.gameObject.AddComponent<LayoutElement>();
        rowLe.preferredHeight = (NodeLabelHeight + 10f * M) * _currentCardHeightScale + NodeSize;
        // 节点链是"该吃掉剩余空间"的那个元素——标题/区域信息按自然宽度显示，节点链
        // 撑满标题跟其它元素之间让出来的空间，连线才会跟参考图一样长，不是挤在
        // 角落里一小截。单节点(没有连线可撑)的情况下，这个圆点仍然靠hLayout的
        // UpperLeft对齐+childForceExpandWidth=false保持自身比例、贴着左边，不会被
        // 拉伸——多出来的空间只是空出来，不会强行塞进这一个节点里。
        rowLe.flexibleWidth = 1f;
        var hLayout = rowRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        hLayout.childAlignment = TextAnchor.UpperLeft;
        hLayout.childControlWidth = true;
        hLayout.childControlHeight = true;
        hLayout.childForceExpandWidth = false;
        hLayout.childForceExpandHeight = false;
        hLayout.spacing = 0f;
        // 标签比节点列宽得多、以图标为中心左右展开——最后一个节点的标签会往右探出
        // 节点链自己的可用宽度，容易伸到旁边的区域/委托人信息上，右边留一块安全
        // 边距接住这部分探出。左边不需要留一样多——节点链左侧紧挨着标题栏，本来
        // 就有headerLayout自己的间距挡着，留太多反而把整条链往右推得太多。
        int labelOverhang = (int)((NodeLabelWidth - NodeColumnWidth) / 2f);
        hLayout.padding = new RectOffset(0, labelOverhang, 0, 0);

        for (int i = 0; i < steps.Count; i++)
        {
            bool done = i < currentIndex;
            bool isCurrent = i == currentIndex;

            // Col只负责"图标要连线的那个点"，宽度紧贴图标本身(NodeSize)，不能比
            // 图标宽——列一宽，图标居中之后离列边缘就有一截空白，连线只连到列边缘，
            // 碰不到图标，看起来像没连上。标签文字改成ignoreLayout、宽度单独给一个
            // 比列宽得多的固定值、水平居中在同一个位置，不受这个窄列宽度限制，允许
            // 视觉上盖过相邻的连线区域(反正只是文字，不会跟连线打架)。
            var colRt = SkyPrisonFloatingWindowKit.MakeRect($"Col{i}", rowRt, Vector2.zero, Vector2.zero);
            var colLe = colRt.gameObject.AddComponent<LayoutElement>();
            colLe.preferredWidth = NodeColumnWidth;
            var colLayout = colRt.gameObject.AddComponent<VerticalLayoutGroup>();
            colLayout.childAlignment = TextAnchor.UpperCenter;
            colLayout.spacing = 6f * M;
            colLayout.childControlWidth = true;
            colLayout.childControlHeight = true;
            // 这个文件自己反复踩过的坑：VerticalLayoutGroup的childForceExpandWidth/
            // Height不显式设置的话默认是true——节点圆圈是这个列里唯一还参与排版的
            // 子物体(标签已经ignoreLayout了)，不关掉的话会被强行撑到跟整个列一样宽，
            // 表现就是"圆圈变大了"。
            colLayout.childForceExpandWidth = false;
            colLayout.childForceExpandHeight = false;

            // 标签是ignoreLayout、绝对定位的，colLayout看不到它占的空间——如果不额外
            // 补一个占位块，colLayout会把节点图标直接摆到列的最顶上，跟悬空的标签
            // 文字重叠。这个LabelSpacer参与正常排版、高度跟标签一致，把图标正确
            // 挤到标签下面，不会碰到一起。
            var labelSpacerRt = SkyPrisonFloatingWindowKit.MakeRect("LabelSpacer", colRt, Vector2.zero, Vector2.one);
            labelSpacerRt.gameObject.AddComponent<LayoutElement>().preferredHeight = NodeLabelHeight;

            var labelRt = SkyPrisonFloatingWindowKit.MakeRect("Label", colRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            labelRt.pivot = new Vector2(0.5f, 1f);
            labelRt.sizeDelta = new Vector2(NodeLabelWidth, NodeLabelHeight);
            labelRt.anchoredPosition = Vector2.zero;
            labelRt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            // 收起态(compactLabels)节点一多就会挤成一整行糊在一起——用户明确要求
            // 折叠起来的时候只留"当前要做的那一个"的文字，已完成/还没轮到的节点
            // 只留图标不留字，展开态(compactLabels=false)不受影响、四个节点的文字
            // 都照常显示(那边横向空间本来就更宽裕)。
            string labelContent = (!compactLabels || isCurrent)
                ? QuestObjectiveTextResolver.ResolveTitle(steps[i], giverNpc)
                : "";
            TMP_Text labelText = SkyPrisonFloatingWindowKit.MakeText(labelRt, "Text",
                labelContent, NodeLabelFontSize, FontStyles.Normal, _font);
            labelText.alignment = TextAlignmentOptions.Top;
            labelText.color = done ? new Color(1f, 1f, 1f, 0.55f) : (isCurrent ? Color.white : new Color(1f, 1f, 1f, 0.35f));
            if (!string.IsNullOrEmpty(labelContent))
                ShrinkTextToMaxLines(labelText, 2, NodeLabelMinFontSize);

            var nodeRt = SkyPrisonFloatingWindowKit.MakeRect("Node", colRt, Vector2.zero, Vector2.zero);
            var nodeLe = nodeRt.gameObject.AddComponent<LayoutElement>();
            nodeLe.preferredWidth = NodeSize;
            nodeLe.preferredHeight = NodeSize;

            if (done || isCurrent)
            {
                // 发光——比图标大一圈的柔和径向渐变光晕垫在下面，纯UI Image叠加，
                // 不是真的屏幕空间Bloom(对一个小图标没必要那么重)。已完成/当前
                // 这两个"亮着"的状态都要发光，只有还没到达的暗淡状态不发光。
                // 图标本身不能再直接挂在nodeRt自己身上——一个物体自己的Graphic
                // 组件永远先于它的子物体渲染，挂在nodeRt上会被稍后加进来的子物体
                // Glow盖住。改成图标也做成子物体，创建顺序排在Glow后面，保证盖在
                // 光晕上面。
                var glowRt = SkyPrisonFloatingWindowKit.MakeRect("Glow", nodeRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                glowRt.sizeDelta = new Vector2(NodeSize * 2.4f, NodeSize * 2.4f);
                glowRt.anchoredPosition = Vector2.zero;
                var glowImg = glowRt.gameObject.AddComponent<Image>();
                glowImg.sprite = GetGlowSprite();
                glowImg.color = new Color(SkyPrisonUIPalette.ColdGreen.r, SkyPrisonUIPalette.ColdGreen.g, SkyPrisonUIPalette.ColdGreen.b, 0.5f);
                glowImg.raycastTarget = false;
            }

            if (done)
            {
                var discRt = SkyPrisonFloatingWindowKit.MakeRect("Disc", nodeRt, Vector2.zero, Vector2.one);
                var disc = discRt.gameObject.AddComponent<Image>();
                disc.sprite = GetNodeSprite(false);
                disc.color = SkyPrisonUIPalette.ColdGreen;
                disc.preserveAspect = true;
                var check = SkyPrisonFloatingWindowKit.MakeText(discRt, "Check", "✓", NodeSize * 0.55f, FontStyles.Bold, _font);
                check.color = Color.black;
            }
            else if (isCurrent)
            {
                var ringRt = SkyPrisonFloatingWindowKit.MakeRect("Ring", nodeRt, Vector2.zero, Vector2.one);
                var ring = ringRt.gameObject.AddComponent<Image>();
                ring.sprite = GetNodeSprite(true);
                ring.color = SkyPrisonUIPalette.ColdGreen;
                ring.preserveAspect = true;
                var dotRt = SkyPrisonFloatingWindowKit.MakeRect("Dot", ringRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                dotRt.sizeDelta = new Vector2(NodeSize * 0.4f, NodeSize * 0.4f);
                var dotImg = dotRt.gameObject.AddComponent<Image>();
                dotImg.sprite = GetNodeSprite(false);
                dotImg.color = SkyPrisonUIPalette.ColdGreen;
                dotImg.preserveAspect = true;
            }
            else
            {
                var ring = nodeRt.gameObject.AddComponent<Image>();
                ring.sprite = GetNodeSprite(true);
                ring.color = new Color(1f, 1f, 1f, 0.3f);
                ring.preserveAspect = true;
            }

            // 连线颜色/样式看它连向的"右边那个节点"是什么状态，不是看左边这个节点：
            // 右边已完成=绿色实线，右边是当前节点=白色实线，右边还没到达=虚线。
            if (i < steps.Count - 1)
            {
                bool rightDone = (i + 1) < currentIndex;
                bool rightCurrent = (i + 1) == currentIndex;
                BuildNodeChainLine(rowRt, rightDone, rightCurrent);
            }
        }

        return rowRt;
    }

    private void BuildNodeChainLine(RectTransform parent, bool rightDone, bool rightCurrent)
    {
        var lineRt = SkyPrisonFloatingWindowKit.MakeRect("Line", parent, Vector2.zero, Vector2.zero);
        var lineLe = lineRt.gameObject.AddComponent<LayoutElement>();
        lineLe.flexibleWidth = 1f;
        // 节点一多，标题栏/区域信息这些固定宽度元素占掉大部分空间，连线能分到的
        // "剩余空间"会被压得很短，视觉上跟短横线差不多——给个保底最小宽度，
        // 不管挤成什么样，连线至少有个能看出"这是一条线"的长度。
        lineLe.minWidth = 40f * M;
        // 不再靠"Spacer+BarArea"这套跟节点列(colLayout)平行的排版结构去凑巧对齐——
        // 两边要手动保持完全一样的高度/间距，稍有出入就偏移，这正是之前反复对不齐
        // 的原因。直接算出节点图标中心到列顶部的绝对偏移量(标签占位高度+列间距+
        // 图标半径)，把连线钉在这个精确坐标上，不依赖任何"两个东西必须保持同步"
        // 的隐性假设。
        float nodeCenterOffsetFromTop = NodeLabelHeight + 6f * M + NodeSize * 0.5f;
        var barRt = SkyPrisonFloatingWindowKit.MakeRect("Bar", lineRt, new Vector2(0f, 1f), new Vector2(1f, 1f));
        barRt.pivot = new Vector2(0.5f, 0.5f);
        barRt.anchoredPosition = new Vector2(0f, -nodeCenterOffsetFromTop);
        barRt.sizeDelta = new Vector2(0f, 2f * M);
        Image barImg = barRt.gameObject.AddComponent<Image>();
        // 连线往右边那个节点看：右边已完成=实心冷绿，右边是当前节点=实心白，
        // 右边还没到达=真正的虚线(平铺贴图)，不是靠透明度假装虚线。
        if (rightDone)
        {
            barImg.color = SkyPrisonUIPalette.ColdGreen;
        }
        else if (rightCurrent)
        {
            barImg.color = Color.white;
        }
        else
        {
            barImg.type = Image.Type.Tiled;
            barImg.sprite = GetDashSprite();
            barImg.color = new Color(1f, 1f, 1f, 0.35f);
        }
    }

    private static Sprite _dashSprite;

    /// <summary>虚线用的平铺贴图——一段实心+一段透明，Image.Type.Tiled沿横向重复，
    /// 不是靠低透明度实线假装虚线。</summary>
    private static Sprite GetDashSprite()
    {
        if (_dashSprite != null) return _dashSprite;

        const int w = 16, h = 8;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = x < w * 0.55f ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        tex.SetPixels32(pixels);
        tex.Apply();
        _dashSprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
        _dashSprite.hideFlags = HideFlags.HideAndDontSave;
        return _dashSprite;
    }

    private static Sprite _nodeDiscSprite;
    private static Sprite _nodeRingSprite;

    /// <summary>圆点/圆环贴图，程序生成一次全局复用(不需要单独的美术资源)——实心圆用于
    /// 已完成状态，圆环(挖空中心)用于当前/未到达状态，边缘做了抗锯齿过渡。</summary>
    private static Sprite GetNodeSprite(bool ring)
    {
        if (ring && _nodeRingSprite != null) return _nodeRingSprite;
        if (!ring && _nodeDiscSprite != null) return _nodeDiscSprite;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float outerR = size * 0.46f;
        // 描边粗细 = outerR-innerR，之前0.16*size看起来太粗，收到0.09*size左右。
        float innerR = ring ? size * 0.37f : 0f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center, dy = y - center;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float outerA = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(outerR - 1.5f, outerR + 1.5f, d));
                float innerA = ring ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerR - 1.5f, innerR + 1.5f, d)) : 1f;
                float a = Mathf.Clamp01(outerA) * Mathf.Clamp01(innerA);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        if (ring) _nodeRingSprite = sprite; else _nodeDiscSprite = sprite;
        return sprite;
    }

    /// <summary>"当前目标"大字——跟节点链下面的小标签共用 ResolveTitleWithLink，
    /// 讨伐/收集类目标的名字部分能被贾维斯式悬浮卡查询，进度数字用冷绿强调。</summary>
    private void BuildObjectiveDetailText(RectTransform parent, QuestObjective objective, float fontSize, string questIdForBaseline, UnitDefinition giverNpc = null)
    {
        // 一个目标同时要求好几件事(completionConditions不止一条)时，"当前目标"这里
        // 先显示一行总标题+右侧聚合进度("已满足/总共")，再把每一条单独列成一行——
        // 不能只显示目标本身唯一那一条模板，那套模板是给"整个目标正好对应一种
        // 结构化类型"设计的，多条件目标没有这种唯一模板可用。
        if (objective.completionConditions.Count > 1)
        {
            QuestObjectiveTextResolver.Progress aggregate = QuestObjectiveTextResolver.GetAggregateProgress(objective, questIdForBaseline);
            string header = QuestObjectiveTextResolver.ResolveTitle(objective, giverNpc);
            if (aggregate.Resolved)
                header += $"  <color=#{ColdGreenHex}>({aggregate.Current}/{aggregate.Target})</color>";

            var headerRt = SkyPrisonFloatingWindowKit.MakeRect("Header", parent, Vector2.zero, Vector2.one);
            headerRt.gameObject.AddComponent<LayoutElement>().preferredHeight = fontSize * 1.6f * _currentCardHeightScale;
            TMP_Text headerText = SkyPrisonFloatingWindowKit.MakeText(headerRt, "Label", header, fontSize, FontStyles.Normal, _font);
            headerText.alignment = TextAlignmentOptions.TopLeft;
            headerText.color = Color.white;
            headerText.enableWordWrapping = false;
            headerText.overflowMode = TextOverflowModes.Overflow;

            foreach (LogicSentenceInstance condition in objective.completionConditions)
            {
                QuestObjectiveTextResolver.ConditionLine line =
                    QuestObjectiveTextResolver.ResolveConditionLine(condition, questIdForBaseline, giverNpc);

                var lineRt = SkyPrisonFloatingWindowKit.MakeRect("Line", parent, Vector2.zero, Vector2.one);
                lineRt.gameObject.AddComponent<LayoutElement>().preferredHeight = fontSize * 1.6f * _currentCardHeightScale;

                string lineContent = line.Text;
                if (line.Progress.Resolved)
                    lineContent += $"  <color=#{ColdGreenHex}>({Mathf.Min(line.Progress.Current, line.Progress.Target)}/{line.Progress.Target})</color>";

                TMP_Text lineText = SkyPrisonFloatingWindowKit.MakeText(lineRt, "Label", lineContent, fontSize, FontStyles.Normal, _font);
                lineText.alignment = TextAlignmentOptions.TopLeft;
                lineText.color = Color.white;
                lineText.enableWordWrapping = false;
                lineText.overflowMode = TextOverflowModes.Overflow;

                // 多条件目标之前没接进贾维斯悬浮卡——单条件那边(下面的ResolveTitleWithLink
                // 分支)早就有这个链路，这里补齐，让拆开显示的每一条子条件也能悬停查询。
                if (line.Info.Resolved)
                {
                    // 刚创建这一帧TMP的链接信息(FindIntersectingLink要用的那份)还没算出来——
                    // 每0.5秒整个列表重建一次，鼠标停留不动的情况下，重建那一帧
                    // FindIntersectingLink会找不到链接、悬浮卡瞬间隐藏再下一帧才重新弹出，
                    // 表现成"闪烁"。强制立刻算一次网格，跟这份文件其它地方处理"刚创建
                    // 那一帧不可靠"是同一个套路。
                    lineText.ForceMeshUpdate();
                    _linkableTexts.Add((lineText, line.Info, line.Progress));
                }
            }
            return;
        }

        var textRt = SkyPrisonFloatingWindowKit.MakeRect("Text", parent, Vector2.zero, Vector2.one);
        textRt.gameObject.AddComponent<LayoutElement>().preferredHeight = fontSize * 1.6f * _currentCardHeightScale;

        string title = QuestObjectiveTextResolver.ResolveTitleWithLink(objective, out QuestObjectiveTextResolver.AutoInfo autoInfo, giverNpc);
        QuestObjectiveTextResolver.Progress progress = QuestObjectiveTextResolver.GetProgress(objective, questIdForBaseline);
        if (progress.Resolved)
            title += $"  <color=#{ColdGreenHex}>({Mathf.Min(progress.Current, progress.Target)}/{progress.Target})</color>";

        TMP_Text text = SkyPrisonFloatingWindowKit.MakeText(textRt, "Label", title, fontSize, FontStyles.Normal, _font);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = Color.white;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;

        if (autoInfo.Resolved)
        {
            text.ForceMeshUpdate(); // 理由同上面多条件那段注释——修"闪烁"用的
            _linkableTexts.Add((text, autoInfo, progress));
        }
    }

    // ── HUD 隐藏：跟角色面板同一套(ExternalHudHideRequests计数器 + 重试)，避免
    // 关卡刚加载、HUD实例还没建好时开面板漏掉这次隐藏请求。────────────────────
    private void HideCombatHud()
    {
        _hudShouldBeHidden = true;
        TryResolveAndHideHud();
        if (_windowManagerForHud == null)
            StartCoroutine(RetryResolveHudReferences());
    }

    private void TryResolveAndHideHud()
    {
        if (_windowManagerForHud == null)
            _windowManagerForHud = FindObjectOfType<SkyPrisonWindowManager_V1>();
        if (_windowManagerForHud != null)
        {
            if (!_hudHideRequestCounted)
            {
                SkyPrisonWindowManager_V1.ExternalHudHideRequests++;
                _hudHideRequestCounted = true;
            }
            _windowManagerForHud.RefreshHudVisibility();
        }
    }

    private IEnumerator RetryResolveHudReferences()
    {
        for (int i = 0; i < 60 && _hudShouldBeHidden; i++)
        {
            yield return null;
            TryResolveAndHideHud();
        }
    }

    private void RestoreCombatHud()
    {
        _hudShouldBeHidden = false;
        if (_windowManagerForHud != null && _hudHideRequestCounted)
        {
            SkyPrisonWindowManager_V1.ExternalHudHideRequests =
                Mathf.Max(0, SkyPrisonWindowManager_V1.ExternalHudHideRequests - 1);
            _hudHideRequestCounted = false;
            _windowManagerForHud.RefreshHudVisibility();
        }
    }

    private IEnumerator OpenBoxAnimation()
    {
        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_boxRt == null) yield break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - (1f - p) * (1f - p);
            _boxRt.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        if (_boxRt != null) _boxRt.localScale = Vector3.one;
    }

    private IEnumerator CloseBoxAnimation()
    {
        if (_blurUvTracker != null) _blurUvTracker.Frozen = true;

        float t = 0f;
        while (t < SkyPrisonFloatingWindowKit.OpenCloseAnimDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_boxRt == null) break;
            float p = Mathf.Clamp01(t / SkyPrisonFloatingWindowKit.OpenCloseAnimDuration);
            float k = 1f - p * p;
            _boxRt.localScale = new Vector3(1f, k, 1f);
            yield return null;
        }
        Destroy(gameObject);
    }

    private void Close()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);

        SkyPrisonWindowHintBar.GetOrCreate().Clear();
        SkyPrisonWindowManager_V1.ExternalBlock = _savedExternalBlock;
        RestoreCombatHud();
        _instance = null;
        StartCoroutine(CloseBoxAnimation());
    }
}
