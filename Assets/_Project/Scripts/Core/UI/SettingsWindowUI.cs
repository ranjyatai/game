using System.Collections;
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 游戏设置窗口——框架先行：Tab 切换 / 窗口样式 / 关闭按钮先搭好，
/// 四个 Tab（显示/音频/操作/玩法）具体内容后续再填（数据层 SettingsPanel /
/// SettingsDisplayPanel 等已经是实的，只是还没有 UI 控件去绑）。
/// 主菜单和暂停菜单的"游戏设置"按钮共用这一个窗口。
/// 视觉规范跟 SaveSlotSelectorUI / PauseMenuController 一致：磨砂背景 + 冷绿细边框 +
/// 透明按钮 + ButtonFeedback；不用暂停菜单那套双色调终端特效（那是暂停专属基调）。
/// </summary>
public class SettingsWindowUI : MonoBehaviour
{
    private static SettingsWindowUI _instance;
    public static bool IsOpen => _instance != null;

    // 暂停菜单的 HideAllGameCanvases() 每帧扫描所有 ScreenSpaceOverlay Canvas 并把它们
    // alpha 清零/禁止交互（用来藏住玩法 HUD）——但设置窗口是独立于暂停菜单挂出来的
    // GameObject，之前没有排除掉，导致从暂停菜单点"游戏设置"后自己的画面被暂停菜单的
    // 扫描逻辑当成"要藏起来的玩法 Canvas"藏掉了（表现为"闪一下又切回暂停菜单，按继续
    // 游戏关掉暂停后才看到设置"），同时 blocksRaycasts/interactable 被强制关掉导致点不了
    // 任何按钮、关不掉窗口。暴露根 Transform 供暂停菜单排除。
    public static Transform ActiveRoot => _instance != null ? _instance.transform : null;

    // 同一次 Esc 按键：这个窗口自己的 Update() 先把自己关掉，调用方（暂停菜单）如果
    // 在同一帧稍后才检查 IsOpen，会读到已经是 false，误以为这次 Esc 没被任何人处理，
    // 接着把自己也关掉——两层窗口被同一次按键一起关掉。记录关闭帧号，调用方额外查
    // 这个标记，跟 PauseMenuController.Show() 用的是同一个思路。
    private static int _lastCloseFrame = -1;
    public static bool JustClosedThisFrame => Time.frameCount == _lastCloseFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStaticState()
    {
        _instance = null;
        _lastCloseFrame = -1;
    }

    public static void Show()
    {
        if (_instance != null) return;

        var go = new GameObject("[SettingsWindow]");
        var ui = go.AddComponent<SettingsWindowUI>();
        _instance = ui;
        ui.StartCoroutine(ui.BuildRoutine());
    }

    public static void Hide()
    {
        if (_instance != null) _instance.Close();
    }

    // ── 视觉常量（跟 SaveSlotSelectorUI 一致）───────────────────────────────
    private static readonly Color ColdGreen    = new Color(0.42f, 0.92f, 0.68f, 1f);
    private static readonly Color TextLight    = new Color(0.88f, 0.88f, 0.90f, 1f);
    private static readonly Color TextFaint    = new Color(0.55f, 0.62f, 0.65f, 1f);
    private static readonly Color PanelOverlay = new Color(0.03f, 0.04f, 0.05f, 0.84f); // 跟 SaveSlotSelectorUI 对齐

    // 7 类：显示/画面拆开（显示=系统层，画面=渲染质量），键鼠/手柄拆开（各自可调项
    // 完全不同），语言单独一类——跟 SaveSlotSelectorUI 同款商业游戏设置分类，讨论定的框架。
    // 具体 key/文案现在唯一定义在 SkyPrisonSettingsTabDefinitions，跟编辑器"界面设置"
    // 工具的书签页签共用同一份，不在这里重复维护。
    private readonly string[] _tabKeys = SkyPrisonSettingsTabDefinitions.Keys;
    private readonly string[] _tabFallback = SkyPrisonSettingsTabDefinitions.FallbackLabels;
    private SettingsSidebarIconSettings _sidebarIcons;

    private TMP_FontAsset _font;
    private RenderTexture _capturedBlurRT;
    private bool _savedExternalBlock;
    private float _savedTimeScale;
    private GameObject _brightnessDialogRoot;
    private Slider _brightnessDialogSlider;
    private float _brightnessDialogInitialValue;
    private RectTransform _rootRt;
    private SkyPrisonInputSettings _inputSettings;
    private SkyPrisonInputPromptIconDatabase _iconDb;

    private readonly List<Button> _tabButtons = new();
    private readonly List<Image> _tabRowBgs = new();
    private readonly List<GameObject> _tabAccents = new();
    private readonly List<MenuButtonHoverFX> _tabFx = new();
    private readonly List<GameObject> _tabContents = new();
    private int _activeTab;
    private Material _desaturateMaterial;

    // ── 无鼠标/手柄导航：侧栏用 Up/Down 切 Tab（已有），内容区这一套让每一行都能不用
    // 鼠标操作——Up/Down 移动行光标，Left/Right 调值（滑块/循环切换），Interact 触发
    // （开关/链接按钮/进入亮度弹窗）。每个 Tab 切换时这份列表会清空重建。
    private class RowNavEntry
    {
        public GameObject cursor;
        public System.Action<int> onHorizontal; // 传 -1/+1
        public System.Action onConfirm;
        // 光标/焦点离开这一行时触发——目前只有"左右箭头循环选项"那几行（分辨率/窗口
        // 模式/画质/语言/帧率）会用，用来把切换途中的"待生效值"真正应用下去，见
        // BuildValueCycleRow 里的 Commit()。
        public System.Action onBlur;
    }

    // 所有"循环选项"行的待提交动作，不管当前在哪个 Tab 都注册在这里——关闭整个设置
    // 窗口时统一兜底提交一遍，防止玩家切完值直接关窗口、光标还没来得及移开导致的漏提交。
    private readonly List<System.Action> _allCycleCommits = new();
    private enum FocusArea { Sidebar, Content }
    private FocusArea _focus = FocusArea.Sidebar;
    // 每个 Tab 一份独立的行列表（内容在打开设置窗口时就把 7 个 Tab 全部建好了，
    // 不是切换时才建，所以每行必须按 Tab 分开存，不能塞进同一个列表）。
    private readonly List<List<RowNavEntry>> _rowNavPerTab = new();
    private List<RowNavEntry> _buildingNavList;
    private List<RowNavEntry> _rowNav = new();
    private int _rowCursor;

    private IEnumerator BuildRoutine()
    {
        DontDestroyOnLoad(gameObject);
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);

        // 从主菜单直接打开（不经过暂停菜单，比如安全区里）时游戏没有被冻结，
        // 点击会穿透到角色攻击等操作——跟暂停菜单一样标记 ExternalBlock。
        // 如果已经是从暂停菜单打开（ExternalBlock 已经是 true），关闭时保留原值，
        // 别把暂停菜单自己设的挡位提前关掉。
        _savedExternalBlock = SkyPrisonWindowManager_V1.ExternalBlock;
        SkyPrisonWindowManager_V1.ExternalBlock = true;

        // 打开设置窗口那一刻，EventSystem 当前选中的还是背后那个窗口（比如暂停菜单的
        // "继续游戏"按钮）——Unity 的 EventSystem/InputModule 是全局的，手柄"确定"键
        // 映射的 Submit 会直接对着这个仍然被选中的按钮调用 OnSubmit()，跟设置窗口自己
        // 的按键处理完全是两条路、互不知情：结果就是在设置窗口里随便按哪儿的"确定"，
        // 背后那个隐形的"继续游戏"都会被同时点一下，表现成"按确定直接进游戏"。
        // 清空选中对象，让 Unity 自动 Submit 没有目标可打。
        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);

        // 同理冻结场景——嵌套在暂停菜单里时 timeScale 已经是 0（这里等于没操作），
        // 但直接从主菜单（比如安全区）打开时场景本来没冻结，得自己冻一下。
        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        yield return new WaitForEndOfFrame();
        _capturedBlurRT = CaptureAndBlurScreen();
        _font = LoadTMPFont("ZhouFangRiMingTi-2 SDF");
        _inputSettings = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");
        _sidebarIcons = Resources.Load<SettingsSidebarIconSettings>("SettingsSidebarIconSettings");
        LoadIconDatabase();

        yield return null;

        LocalizationRuntime.OnLanguageChanged += OnLanguageChanged;
        RebuildUI(0);
    }

    // 之前语言切换只在窗口构建那一刻查一次表，切了语言这个窗口自己不会跟着刷新，
    // 得关掉重开才看得到新语言。现在整个 UI 内容都在一个可丢弃的 Canvas 子物体下，
    // 语言变化时直接整个销毁重建，比零散地追踪每个 Text 组件简单可靠得多——这个
    // 窗口本来就是"打开时全量构建一次"的模式，重建的开销可以忽略。
    private GameObject _canvasGo;

    private void OnLanguageChanged(string _)
    {
        RebuildUI(_activeTab);
    }

    private void RebuildUI(int tabToRestore)
    {
        if (_canvasGo != null) Destroy(_canvasGo);

        // 这几份列表是靠 BuildSidebar/BuildTabContents 一路 Add 出来的，之前只在打开
        // 窗口时建一次；现在语言变化会重建整个 Canvas，不清空的话每次语言切换都会
        // 在列表里再叠一份，Update() 里的手柄导航会对着已经被销毁的旧引用瞎跑。
        _tabButtons.Clear();
        _tabRowBgs.Clear();
        _tabAccents.Clear();
        _tabFx.Clear();
        _tabContents.Clear();
        _rowNavPerTab.Clear();
        _rowNav = new List<RowNavEntry>();
        _rowCursor = 0;
        _focus = FocusArea.Sidebar;

        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        string L(string key, string fallback) => locTable != null ? locTable.Get(key, fallback) : fallback;

        var canvasGo = new GameObject("Canvas");
        _canvasGo = canvasGo;
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        // Canvas.sortingOrder 内部按 16 位有符号整数存（范围 -32768~32767），超过 32767
        // 会绕成负数（之前用 33500 直接变成 -32036，导致设置窗口排到全场垫底、被主菜单
        // 自己的 Canvas 盖住，完全看不见）。换成压过暂停菜单（32100）但仍在合法范围内的值。
        canvas.sortingOrder = 32200;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(3840f, 2160f);
        canvasGo.AddComponent<GraphicRaycaster>();

        var rootRt = canvasGo.GetComponent<RectTransform>();
        _rootRt = rootRt;

        // 贴边全屏窗口，跟 SaveSlotSelectorUI 一样的规范——不是浮动小卡片。
        var panelRt = MakeRect("WindowPanel", rootRt, Vector2.zero, Vector2.one);

        if (_capturedBlurRT != null)
        {
            var bgImg = panelRt.gameObject.AddComponent<RawImage>();
            bgImg.texture = _capturedBlurRT;
            bgImg.raycastTarget = false;
            // 跟 SaveSlotSelectorUI 一样去色——不去色的话背景截图的彩色噪点会让磨砂
            // 看起来比存档界面糙很多，这才是两边观感不一致的真正原因。
            var desatShader = Shader.Find("UI/SkyPrison/Desaturate");
            if (desatShader != null)
            {
                _desaturateMaterial = new Material(desatShader) { hideFlags = HideFlags.HideAndDontSave };
                _desaturateMaterial.SetFloat("_Saturation", 0f);
                bgImg.material = _desaturateMaterial;
            }
        }
        var overlayRt = MakeRect("DarkOverlay", panelRt, Vector2.zero, Vector2.one);
        overlayRt.gameObject.AddComponent<Image>().color = PanelOverlay;
        overlayRt.gameObject.AddComponent<Button>().onClick.AddListener(() => { }); // 挡住点击穿透

        // 标题（靠左上角），照搬 SaveSlotSelectorUI 的锚点规范
        var titleTmp = AddTMP(panelRt, "Title", L("ui_settings_title", "设置"), 60,
            TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Bold);
        Anchor(titleTmp.rectTransform, 0f, 0.90f, 1f, 0.97f);
        titleTmp.rectTransform.offsetMin = new Vector2(96f, 0f);
        titleTmp.rectTransform.offsetMax = new Vector2(-96f, 0f);

        // 标题下分割线
        AddLine(panelRt, "TitleSep",
            new Vector2(0f, 0.90f), new Vector2(1f, 0.90f), new Vector2(0.5f, 0.90f),
            Vector2.zero, new Vector2(-192f, 1f), new Color(1f, 1f, 1f, 0.15f));

        // 主体内容区：分割线以下、底部提示条以上
        var contentRootRt = MakeRect("ContentRoot", panelRt, Vector2.zero, Vector2.one);
        Anchor(contentRootRt, 0f, 0.08f, 1f, 0.88f);
        contentRootRt.offsetMin = new Vector2(96f, 0f);
        contentRootRt.offsetMax = new Vector2(-96f, 0f);

        BuildCloseButton(panelRt);
        BuildSidebar(contentRootRt, L);
        BuildTabContents(contentRootRt, L);
        BuildHintBar(panelRt, L);

        SwitchTab(Mathf.Clamp(tabToRestore, 0, _tabKeys.Length - 1));
        TryBindFonts(rootRt);
    }

    /// <summary>
    /// 跟 LoadingScreenUI / SaveSlotSelectorUI / PauseMenuController 完全一样的加载方式：
    /// 优先用游戏内已经填充好的 RuntimeDatabase，否则从 Resources 里加载——之前这里
    /// 的兜底只在 UNITY_EDITOR 下用 AssetDatabase 加载，打包后这段代码整个不存在，
    /// 图标兜底形同虚设，跟 LoadingScreenUI.cs 之前踩的是同一个 bug。
    /// </summary>
    private void LoadIconDatabase()
    {
        _iconDb = SkyPrisonQuickItemPromptStrip.RuntimeDatabase
               ?? Resources.Load<SkyPrisonInputPromptIconDatabase>("InputPromptIconDatabase");
    }

    // ── 底部按键提示条：用全项目共用的 SkyPrisonWindowHintBar（跟背包窗口一样），
    // 不再自己重写一遍图标解析+设备切换逻辑——之前这里、暂停菜单、存档选择器各写
    // 各的，图标解析/设备切换的 bug 出一次要三个地方分别修，现在只有一份实现。
    private void BuildHintBar(RectTransform root, System.Func<string, string, string> L)
    {
        // 这里不能用 SkyPrisonWindowHint.Action(MoveUp/Interact/Menu, ...)：这个窗口的
        // 手柄行导航（见 Update() 里的 ReadNavAxisEdges/GamepadConfirm）读的是原始
        // DPad 轴和 JoystickButton0，根本不经过这几个 action 的绑定——而这几个 action
        // 在 SkyPrisonInputSettings.asset 里 gamepadKey 本来就是 None（专门给键盘用的
        // 移动/交互动作），用 Action() 解析永远找不到手柄图标，手柄模式下这些提示会
        // 被直接隐藏。改成手动配对键鼠图标+手柄图标，两边各显示各自实际生效的按键。
        var hints = new[]
        {
            new SkyPrisonWindowHint { iconKey = "keyboard/w", gamepadIconKey = "gamepad/up", fallbackText = "W", label = L("ui_settings_tab_hint", "切换分类") },
            new SkyPrisonWindowHint { iconKey = "keyboard/e", gamepadIconKey = "gamepad/xbox/a", fallbackText = "E", label = L("ui_saveslot_click_token", "确定") },
            new SkyPrisonWindowHint { iconKey = "keyboard/esc", gamepadIconKey = "gamepad/xbox/b", fallbackText = "Esc", label = L("ui_saveslot_return", "返回") },
        };
        SkyPrisonWindowHintBar.GetOrCreate().Show(hints);
    }

    // ── 关闭按钮（跟存档选择器一样的规范：GUID 精确加载真实图标）────────────
    private void BuildCloseButton(RectTransform panel)
    {
        Sprite closeIcon = LoadSpriteByGuid("1a860d9de75042546ba9c69ed9e23434");

        var btnRt = MakeRect("CloseButton", panel, Vector2.zero, Vector2.zero);
        btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 1f);
        btnRt.pivot     = new Vector2(1f, 1f);
        btnRt.sizeDelta = new Vector2(72f, 72f);
        btnRt.anchoredPosition = new Vector2(-28f, -28f);

        btnRt.gameObject.AddComponent<Image>().color = Color.clear;
        var btn = btnRt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(Close);
        SkyPrisonUIButtonFeedback.Attach(btnRt.gameObject);

        if (closeIcon != null)
        {
            var iconRt = MakeRect("Icon", btnRt, Vector2.zero, Vector2.one);
            var icon = iconRt.gameObject.AddComponent<Image>();
            icon.sprite = closeIcon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }
        else
        {
            AddOutline(btnRt, Color.white, 3f);
            AddTMP(btnRt, "Label", "×", 40, TextAlignmentOptions.Center, TextLight, FontStyles.Normal)
                .raycastTarget = false;
        }
    }

    // ── 左侧竖排目录（照参考的主流游戏设置界面：左侧垂直 Tab 列表 + 右侧内容区，
    // 不是顶部横排）。选中行用实心冷绿底 + 左侧一条竖着的强调线，不是单纯描边。──
    private const float SidebarWidth = 480f; // 加了书签图标后日文/英文标签变窄容易换行，整体加宽腾出空间

    private void BuildSidebar(RectTransform contentRoot, System.Func<string, string, string> L)
    {
        // contentRoot 本身已经是"标题分割线以下、底部提示条以上"那一块了，
        // 这里只用来左右分栏，不用再单独留上下边距。
        var sidebarRt = MakeRect("Sidebar", contentRoot, new Vector2(0f, 0f), new Vector2(0f, 1f));
        sidebarRt.offsetMin = new Vector2(0f, 0f);
        sidebarRt.offsetMax = new Vector2(SidebarWidth, 0f);

        var layout = sidebarRt.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 12f;
        layout.padding = new RectOffset(0, 0, 56, 0); // 整体往下挪一点，别贴着分割线
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        for (int i = 0; i < _tabKeys.Length; i++)
        {
            int index = i;
            var rowRt = MakeRect($"Tab_{i}", sidebarRt, Vector2.zero, Vector2.zero);
            var rowLe = rowRt.gameObject.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 88f;

            var bg = rowRt.gameObject.AddComponent<Image>();
            bg.color = Color.clear;
            var btn = rowRt.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
                SwitchTab(index);
            });

            // 选中行才显示的矩形线框（跟暂停菜单按钮同款四边细边框），
            // 换掉之前的左侧竖线强调条。
            var bracketRt = MakeRect("SelectBox", rowRt, Vector2.zero, Vector2.one);
            bracketRt.offsetMin = new Vector2(8f, 4f);
            bracketRt.offsetMax = new Vector2(-8f, -4f);
            AddOutline(bracketRt, ColdGreen, 3f);
            var accent = bracketRt.gameObject;
            accent.SetActive(false);

            // 书签图标（可选）：有配置才腾位置，没配置的分类文字保持原来贴右边缘的位置，
            // 不留空当占位——图标数量/内容在 Tools/界面设置 的"设置界面书签"页签里配。
            // Texture2D 不是 Sprite——用 RawImage 直接吃贴图，不用管 Texture Type/Sprite Mode，
            // 拖个 PNG 进 Tools/界面设置 的书签页签就能用，不用再折腾导入设置。
            Texture2D bookmarkIcon = _sidebarIcons != null ? _sidebarIcons.GetIcon(i) : null;
            const float IconSlot = 96f; // 86 * 1.12，跟着图标一起放大，避免文字被图标压到
            float labelRightInset = 16f;
            if (bookmarkIcon != null)
            {
                var iconRt = MakeRect("Icon", rowRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                iconRt.pivot = new Vector2(1f, 0.5f);
                iconRt.anchoredPosition = new Vector2(-16f, 0f);
                iconRt.sizeDelta = new Vector2(68f, 68f); // 60 * 1.13 ≈ 68
                var iconImg = iconRt.gameObject.AddComponent<RawImage>();
                iconImg.texture = bookmarkIcon;
                iconImg.raycastTarget = false;
                labelRightInset = IconSlot;
            }

            // 选项文字靠右——竖排目录跟右侧内容区之间隔着一条分割线，文字往右靠拢
            // 视觉上更贴近那条线，符合参考图的观感。
            var labelRt = MakeRect("Label", rowRt, Vector2.zero, Vector2.one);
            labelRt.offsetMin = new Vector2(32f, 0f);
            labelRt.offsetMax = new Vector2(-labelRightInset, 0f);
            var label = AddTMP(labelRt, "Text", L(_tabKeys[i], _tabFallback[i]), 56f,
                TextAlignmentOptions.MidlineRight, TextLight, FontStyles.Normal);
            label.raycastTarget = false;

            // 光标表现照搬主菜单选项：同一套 MenuButtonHoverFX 色收差（红/蓝文字偏移层 +
            // glitch 抖动），不是背包/存档那套 ButtonFeedback 绿色填充。
            var labelR = AddTMP(labelRt, "Text_R", L(_tabKeys[i], _tabFallback[i]), 56f,
                TextAlignmentOptions.MidlineRight, Color.white, FontStyles.Normal);
            labelR.raycastTarget = false;
            var labelB = AddTMP(labelRt, "Text_B", L(_tabKeys[i], _tabFallback[i]), 56f,
                TextAlignmentOptions.MidlineRight, Color.white, FontStyles.Normal);
            labelB.raycastTarget = false;
            var fx = rowRt.gameObject.AddComponent<MenuButtonHoverFX>();
            fx.Init(btn, label, labelR, (RectTransform)labelR.transform, labelB, (RectTransform)labelB.transform);

            // 鼠标悬停即同步光标（跟键盘/手柄共用同一套选中态，这个项目里所有窗口
            // 统一的规则），不额外做"悬停预览、点击才确认"那一套。
            var trigger = rowRt.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var pointerEnter = new UnityEngine.EventSystems.EventTrigger.Entry
            {
                eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter
            };
            pointerEnter.callback.AddListener(_ =>
            {
                if (_activeTab == index) return;
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
                SwitchTab(index);
            });
            trigger.triggers.Add(pointerEnter);

            _tabButtons.Add(btn);
            _tabRowBgs.Add(bg);
            _tabAccents.Add(accent);
            _tabFx.Add(fx);
        }

        // 侧栏和内容区之间的竖分隔线
        var divider = MakeRect("Divider", contentRoot, new Vector2(0f, 0f), new Vector2(0f, 1f));
        divider.pivot = new Vector2(0f, 0.5f);
        divider.anchoredPosition = new Vector2(SidebarWidth + 32f, 0f);
        divider.sizeDelta = new Vector2(1f, 0f);
        divider.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
    }

    // ── 右侧内容区（先放占位——数据层是实的，等具体控件设计好再往这里填）────
    private void BuildTabContents(RectTransform contentRoot, System.Func<string, string, string> L)
    {
        var areaRt = MakeRect("ContentArea", contentRoot, Vector2.zero, Vector2.one);
        areaRt.offsetMin = new Vector2(SidebarWidth + 64f, 0f);
        areaRt.offsetMax = new Vector2(0f, 0f);

        for (int i = 0; i < _tabKeys.Length; i++)
        {
            var contentRt = MakeRect($"Content_{i}", areaRt, Vector2.zero, Vector2.one);

            _buildingNavList = new List<RowNavEntry>();
            switch (i)
            {
                case 0: BuildDisplayTabContent(contentRt, L);   break;
                case 1: BuildGraphicsTabContent(contentRt, L);  break;
                case 2: BuildAudioTabContent(contentRt, L);     break;
                case 3: BuildLanguageTabContent(contentRt, L);  break;
                case 4: BuildKeyMouseTabContent(contentRt, L);  break;
                case 5: BuildGamepadTabContent(contentRt, L);   break;
                case 6: BuildGameplayTabContent(contentRt, L);  break;
            }
            _rowNavPerTab.Add(_buildingNavList);
            _buildingNavList = null;

            contentRt.gameObject.SetActive(false);
            _tabContents.Add(contentRt.gameObject);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // 每个 Tab 的内容——骨架先行：把所有能设置的项目都摆成行，具体控件交互
    // （下拉展开、按键捕获 UI 等）留到下一步细化，这一步先保证"有这一行、
    // 显示当前值、能跟数据层读写上"。
    // ══════════════════════════════════════════════════════════════════════

    private void BuildDisplayTabContent(RectTransform contentRt, System.Func<string, string, string> L)
    {
        var s = SaveManager.Settings;
        int row = 0;

        BuildToggleRow(contentRt, row++, L("ui_settings_vsync", "垂直同步"),
            s != null && s.vsync, v =>
            {
                if (s != null) s.vsync = v;
                QualitySettings.vSyncCount = v ? 1 : 0;
            });

        var windowModeLabels = new[] { L("ui_settings_windowmode_windowed", "窗口"),
            L("ui_settings_windowmode_fullscreen", "全屏"),
            L("ui_settings_windowmode_borderless", "无边框全屏") };
        BuildValueCycleRow(contentRt, row++, L("ui_settings_windowmode", "窗口模式"),
            windowModeLabels,
            windowModeLabels[s != null ? Mathf.Clamp(s.windowMode, 0, 2) : 1],
            v =>
            {
                if (s == null) return;
                int mode = System.Array.IndexOf(windowModeLabels, v);
                s.windowMode = mode;
                s.fullscreen = mode != 0;
                // 之前这里先手动赋值一次 Screen.fullScreenMode，紧接着
                // ApplyClampedResolution 内部又调一次 Screen.SetResolution——同一帧对
                // 交换链连下两次模式切换指令，在 D3D12 + flip-model swapchain 这套配置下
                // 容易互相打架，表现为"切了窗口模式但画面没反应"。只留一次真正生效的调用。
                FullScreenMode targetMode = mode switch
                {
                    0 => FullScreenMode.Windowed,
                    2 => FullScreenMode.FullScreenWindow, // 无边框全屏
                    _ => FullScreenMode.ExclusiveFullScreen,
                };
                SaveManager.ApplyClampedResolution(s.resolutionWidth, s.resolutionHeight, targetMode);
            });

        BuildValueCycleRow(contentRt, row++, L("ui_settings_resolution", "分辨率"),
            new[] { "1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160" },
            s != null ? $"{s.resolutionWidth}x{s.resolutionHeight}" : "1920x1080",
            v =>
            {
                if (s == null) return;
                var parts = v.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int rw) && int.TryParse(parts[1], out int rh))
                {
                    s.resolutionWidth = rw; s.resolutionHeight = rh;
                    SaveManager.ApplyClampedResolution(rw, rh, Screen.fullScreenMode);
                }
            });

        // "无限制"之前是硬编码中文，不管切什么语言都不会变；显示当前值那里也有个
        // 独立小 bug——targetFrameRate 是 -1（无限制）时直接 ToString() 会变成
        // 字面的 "-1"，跟下面选项列表里任何一个标签都对不上，行为上等同于"当前值
        // 没有匹配到任何选项"。改成显式用同一个 unlimitedLabel 判断。
        string unlimitedLabel = L("ui_settings_framerate_unlimited", "无限制");
        var framerateOptions = new[] { "30", "60", "120", "144", "240", unlimitedLabel };
        string currentFramerateLabel = s == null ? "60"
            : s.targetFrameRate < 0 ? unlimitedLabel
            : s.targetFrameRate.ToString();
        BuildValueCycleRow(contentRt, row++, L("ui_settings_framerate", "帧率上限"),
            framerateOptions,
            currentFramerateLabel,
            v =>
            {
                int fps = v == unlimitedLabel ? -1 : int.Parse(v);
                if (s != null) s.targetFrameRate = fps;
                Application.targetFrameRate = fps;
            });

        BuildToggleRow(contentRt, row++, L("ui_settings_show_fps", "显示FPS"),
            s != null && s.showFps,
            v => { if (s != null) s.showFps = v; SkyPrisonFPSCounterUI.SetVisible(v); });
    }

    private void BuildGraphicsTabContent(RectTransform contentRt, System.Func<string, string, string> L)
    {
        var s = SaveManager.Settings;

        BuildBrightnessRow(contentRt, 0, L); // 亮度：独立弹窗校准，见 BuildBrightnessRow/OpenBrightnessDialog

        // Unity Project Settings 里实际配置了 6 档 Quality Level（Very Low~Ultra），之前
        // UI 只暴露 4 个标签、直接拿下标 0~3 传给 SetQualityLevel——相当于把 Unity 真正的
        // "Very High"(4)"Ultra"(5) 两档完全浪费掉了，UI 上写着"极致"其实只到 Unity 的
        // "High"档。现在把 6 档全部暴露出来，以后加光追等分档渲染效果，直接按
        // QualitySettings.GetQualityLevel() 的 0~5 分支处理即可，不需要再回来改这个界面。
        var qualityLabels = new[]
        {
            L("ui_quality_verylow", "极低"), L("ui_quality_low", "低"), L("ui_quality_mid", "中"),
            L("ui_quality_high", "高"), L("ui_quality_veryhigh", "极高"), L("ui_quality_ultra", "极致")
        };
        int currentQualityLevel = s != null ? Mathf.Clamp(s.qualityLevel, 0, qualityLabels.Length - 1) : 3;
        BuildValueCycleRow(contentRt, 1, L("ui_settings_quality", "画质预设"),
            qualityLabels,
            qualityLabels[currentQualityLevel],
            v =>
            {
                int level = System.Array.IndexOf(qualityLabels, v);
                if (level < 0) level = qualityLabels.Length - 1;
                if (s != null) s.qualityLevel = level;
                QualitySettings.SetQualityLevel(level, applyExpensiveChanges: true);
            });

        BuildToggleRow(contentRt, 2, L("ui_settings_motion_blur", "动态模糊"),
            s != null && s.motionBlur, v =>
            {
                if (s != null) s.motionBlur = v;
                SkyPrisonBrightnessManager.ApplyMotionBlur(v); // 实时生效
            });

        BuildToggleRow(contentRt, 3, L("ui_settings_chromatic", "色差"),
            s != null && s.chromaticAberration, v =>
            {
                if (s != null) s.chromaticAberration = v;
                SkyPrisonBrightnessManager.ApplyChromaticAberration(v); // 实时生效
            });

        BuildToggleRow(contentRt, 4, L("ui_settings_screenshake", "屏幕震动"),
            s != null && s.screenShake, v =>
            {
                if (s != null) s.screenShake = v;
                SkyPrisonScreenShake.Enabled = v;
            });

        // URP 的抗锯齿挂在 Camera 组件上，不是 Volume 能覆盖的，每个场景独立的 Main
        // Camera 都要重新应用一遍——见 SkyPrisonAntialiasingApplier（订阅 sceneLoaded）。
        var aaLabels = new[] { L("ui_settings_aa_off", "关闭"), "FXAA", "TAA" };
        int currentAA = s != null ? Mathf.Clamp(s.antialiasingMode, 0, 2) : 1;
        BuildValueCycleRow(contentRt, 5, L("ui_settings_antialiasing", "抗锯齿"),
            aaLabels, aaLabels[currentAA], v =>
            {
                int mode = System.Array.IndexOf(aaLabels, v);
                if (mode < 0) mode = 1;
                if (s != null) s.antialiasingMode = mode;
                SkyPrisonAntialiasingApplier.Apply(mode);
            });
    }


    private void BuildAudioTabContent(RectTransform contentRt, System.Func<string, string, string> L)
    {
        var s = SaveManager.Settings;

        // 之前这四条滑块只写 SettingsData 存档字段，从没同步到真正驱动音效播放音量的
        // SkyPrisonAudioGlobalSettings.Instance——拖了跟没拖一样，游戏里实际播放音量
        // 一直是默认值 1。现在拖动时也实时写一份过去，不用等重开游戏才生效。
        BuildSliderRow(contentRt, 0, L("ui_settings_volume_master", "主音量"),
            s?.masterVolume ?? 1f, v =>
            {
                if (s != null) s.masterVolume = v;
                var gs = SkyPrisonAudioGlobalSettings.Instance;
                if (gs != null) gs.masterVolume = v;
            });
        BuildSliderRow(contentRt, 1, L("ui_settings_volume_music", "音乐"),
            s?.musicVolume ?? 1f, v =>
            {
                if (s != null) s.musicVolume = v;
                var gs = SkyPrisonAudioGlobalSettings.Instance;
                if (gs != null) gs.bgmVolume = v;
            });
        BuildSliderRow(contentRt, 2, L("ui_settings_volume_sfx", "音效"),
            s?.sfxVolume ?? 1f, v =>
            {
                if (s != null) s.sfxVolume = v;
                var gs = SkyPrisonAudioGlobalSettings.Instance;
                if (gs != null) gs.seVolume = v;
            });
        BuildSliderRow(contentRt, 3, L("ui_settings_volume_voice", "语音"),
            s?.voiceVolume ?? 1f, v =>
            {
                if (s != null) s.voiceVolume = v;
                var gs = SkyPrisonAudioGlobalSettings.Instance;
                if (gs != null) gs.voiceVolume = v;
            });
    }

    private void BuildLanguageTabContent(RectTransform contentRt, System.Func<string, string, string> L)
    {
        var s = SaveManager.Settings;
        // zh-TW 之前在这个列表里，但 UILocalizationTable.asset 里一条繁体中文词条都
        // 没填——选了这个选项其实每个字都会掉回代码里硬编码的简体中文兜底，是个假
        // 选项。项目本来就没打算做繁体，去掉，只留真正有翻译数据的三种语言。
        var codes  = new[] { "zh-CN", "ja", "en" };
        var labels = new[] { "简体中文", "日本語", "English" };
        // 之前这里只读/写 SettingsData.languageCode，从没调用过 LocalizationRuntime——
        // 选了语言只是存进了存档字段，当前运行时语言完全没变，界面文字也不会跟着切换。
        // 实际生效的语言以 LocalizationRuntime.Instance.CurrentCode 为准，不是这个字段。
        string current = LocalizationRuntime.Instance != null
            ? LocalizationRuntime.Instance.CurrentCode
            : (s != null ? s.languageCode : "zh-CN");
        int idx = System.Array.IndexOf(codes, current);
        string currentLabel = idx >= 0 ? labels[idx] : labels[0];

        BuildValueCycleRow(contentRt, 0, L("ui_settings_language", "语言"), labels, currentLabel, v =>
        {
            int i = System.Array.IndexOf(labels, v);
            if (i < 0) return;
            if (s != null) s.languageCode = codes[i];
            LocalizationRuntime.Instance?.SetLanguage(codes[i]);
        });
    }

    private void BuildKeyMouseTabContent(RectTransform contentRt, System.Func<string, string, string> L)
    {
        var s = SaveManager.Settings;

        BuildSliderRow(contentRt, 0, L("ui_settings_mouse_sensitivity", "鼠标灵敏度"),
            Mathf.InverseLerp(0.1f, 10f, s?.mouseSensitivity ?? 1f),
            v => { if (s != null) s.mouseSensitivity = Mathf.Lerp(0.1f, 10f, v); });

        // Y 轴反转：本作是 2.5D，角色朝向只有左右、没有任何垂直视角/俯仰摄像机系统，
        // 这个开关接不了任何真实逻辑，之前是纯摆设——去掉，不留假开关。

        BuildLinkRow(contentRt, 1, L("ui_settings_keybinds", "按键绑定"),
            L("ui_settings_keybinds_enter", "查看/修改"), () => OpenKeybindDialog(L));
    }

    // ── 按键绑定弹窗：左边功能名，右边主键/副键两个可点击槽位 ────────────────

    // 显示顺序 + 本地化 key + 中文兜底；手柄键(gamepadKey)不在这里改，那是手柄 tab
    // 自己的手柄图列表的事，这里只管键盘/鼠标两个槽位。
    private static readonly (SkyPrisonInputAction action, string locKey, string fallback)[] KeybindRows =
    {
        (SkyPrisonInputAction.MoveUp,      "ui_settings_action_moveup",    "上移动"),
        (SkyPrisonInputAction.MoveDown,    "ui_settings_action_movedown",  "下移动"),
        (SkyPrisonInputAction.MoveLeft,    "ui_settings_action_moveleft",  "左移动"),
        (SkyPrisonInputAction.MoveRight,   "ui_settings_action_moveright", "右移动"),
        (SkyPrisonInputAction.Sprint,      "ui_settings_action_sprint",    "奔跑"),
        (SkyPrisonInputAction.Sneak,       "ui_settings_action_sneak",     "潜行"),
        (SkyPrisonInputAction.Jump,        "ui_settings_action_jump",      "跳跃"),
        (SkyPrisonInputAction.Dodge,       "ui_settings_action_dodge",     "闪避"),
        (SkyPrisonInputAction.Interact,    "ui_settings_action_interact",  "交互/拾取/使用"),
        (SkyPrisonInputAction.LightAttack, "ui_settings_action_light_attack", "轻攻击"),
        (SkyPrisonInputAction.HeavyAttack, "ui_settings_action_heavy_attack", "重攻击"),
        (SkyPrisonInputAction.Skill1,      "ui_settings_action_skill1",    "技能 1"),
        (SkyPrisonInputAction.Skill2,      "ui_settings_action_skill2",    "技能 2"),
        (SkyPrisonInputAction.Skill3,      "ui_settings_action_skill3",    "技能 3"),
        (SkyPrisonInputAction.Reload,      "ui_settings_action_reload",    "换弹"),
        (SkyPrisonInputAction.CyclePickup, "ui_settings_action_cyclepickup", "切换拾取目标"),
        (SkyPrisonInputAction.Inventory,   "ui_settings_action_inventory", "背包"),
        (SkyPrisonInputAction.Map,         "ui_settings_action_map",       "地图"),
        (SkyPrisonInputAction.Menu,        "ui_settings_action_menu",      "菜单"),
        (SkyPrisonInputAction.CharacterPanel, "ui_settings_action_characterpanel", "角色面板"),
        (SkyPrisonInputAction.QuickItem1,  "ui_settings_action_quickitem1", "快捷物品 1"),
        (SkyPrisonInputAction.QuickItem2,  "ui_settings_action_quickitem2", "快捷物品 2"),
        (SkyPrisonInputAction.QuickItem3,  "ui_settings_action_quickitem3", "快捷物品 3"),
        (SkyPrisonInputAction.QuickItem4,  "ui_settings_action_quickitem4", "快捷物品 4"),
    };

    private class KeybindSlotRef
    {
        public Image icon;
        public TMP_Text label;
        public GameObject cursorHighlight; // 手柄/键盘光标停在这个槽位时显示的冷绿描边
    }

    private class KeybindRowRef
    {
        public SkyPrisonInputBinding binding;
        public Button primaryBtn, secondaryBtn;
        public KeybindSlotRef primarySlot, secondarySlot;
        public RectTransform rowRt;
    }

    private GameObject _keybindDialogRoot;
    private readonly List<KeybindRowRef> _keybindRowRefs = new();
    private SkyPrisonInputBinding _keybindCapturingBinding;
    private bool _keybindCapturingIsSecondary;
    private KeybindSlotRef _keybindCapturingSlot;

    // 光标位置（行索引 + 主/副列），配合 cursorHighlight 显示当前选中槽位。
    private int _keybindCursorRow;
    private bool _keybindCursorIsSecondary;
    private ScrollRect _keybindScrollRect;
    private RectTransform _keybindViewportRt;

    // 开窗那一刻给每个 binding 的 primary/secondary 拍个快照——"返回（不保存）"要能
    // 完整撤销这次窗口里做过的所有修改（包括"恢复默认按键"），不是靠 Undo 历史，
    // 就是简单地把这份快照原样写回去。"保存并返回"才会真正落盘 + 保留改动。
    private readonly Dictionary<SkyPrisonInputBinding, (KeyCode primary, KeyCode secondary)> _keybindSnapshot = new();

    // ── 手柄按键绑定弹窗：跟键鼠那份结构一样，但只有一列（手柄键），且捕获只认
    // JoystickButton 家族，不认键盘/鼠标——两边职责分开，不要混。────────────────
    private class GamepadKeybindRowRef
    {
        public SkyPrisonInputBinding binding;
        public KeybindSlotRef slot;
        public RectTransform rowRt;
    }

    private GameObject _gamepadKeybindDialogRoot;
    private readonly List<GamepadKeybindRowRef> _gamepadKeybindRowRefs = new();
    private SkyPrisonInputBinding _gamepadCapturingBinding;
    private KeybindSlotRef _gamepadCapturingSlot;
    private int _gamepadCursorRow;
    private ScrollRect _gamepadKeybindScrollRect;
    private RectTransform _gamepadKeybindViewportRt;
    private readonly Dictionary<SkyPrisonInputBinding, KeyCode> _gamepadKeybindSnapshot = new();

    private void OpenKeybindDialog(System.Func<string, string, string> L)
    {
        if (_keybindDialogRoot != null) return;
        if (_rootRt == null) return;
        if (_inputSettings == null) return;

        var dialogRoot = new GameObject("KeybindDialog");
        dialogRoot.transform.SetParent(_rootRt, false);
        var dialogRootRt = dialogRoot.AddComponent<RectTransform>();
        dialogRootRt.anchorMin = Vector2.zero;
        dialogRootRt.anchorMax = Vector2.one;
        dialogRootRt.offsetMin = dialogRootRt.offsetMax = Vector2.zero;
        _keybindDialogRoot = dialogRoot;
        _keybindRowRefs.Clear();

        // 开窗那一刻就清空 EventSystem 的选中对象——不然点"查看/修改"链接行开这个弹窗
        // 那次点击本身会让那一行留在"选中"状态，手柄摇杆/D-pad 会拿它当自动导航起点，
        // 在它跟弹窗外面的东西之间切换选中态，声音听着像是弹窗自己发出来的。
        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);

        _keybindSnapshot.Clear();
        if (_inputSettings.bindings != null)
            foreach (var b in _inputSettings.bindings)
                if (b != null) _keybindSnapshot[b] = (b.primaryKey, b.secondaryKey);

        var dim = MakeRect("Dim", dialogRootRt, Vector2.zero, Vector2.one);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(() => { });

        const float BoxWidth  = 1760f;
        const float BoxHeight = 1760f;
        var boxRt = MakeRect("Box", dialogRootRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        boxRt.pivot = new Vector2(0.5f, 0.5f);
        boxRt.sizeDelta = new Vector2(BoxWidth, BoxHeight);
        boxRt.anchoredPosition = Vector2.zero;

        if (_capturedBlurRT != null)
        {
            var blurImg = boxRt.gameObject.AddComponent<RawImage>();
            blurImg.texture = _capturedBlurRT;
            float wFrac = BoxWidth  / _rootRt.rect.width;
            float hFrac = BoxHeight / _rootRt.rect.height;
            blurImg.uvRect = new Rect(0.5f - wFrac * 0.5f, 0.5f - hFrac * 0.5f, wFrac, hFrac);
            if (_desaturateMaterial != null) blurImg.material = _desaturateMaterial;
        }
        else
        {
            boxRt.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.05f, 0.92f);
        }
        var boxTint = MakeRect("Tint", boxRt, Vector2.zero, Vector2.one);
        boxTint.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        AddCornerBrackets(boxRt, Color.white, 40f, 4f);

        var titleTmp = AddTMP(boxRt, "Title", L("ui_settings_keybinds", "按键绑定"), 44f,
            TextAlignmentOptions.TopLeft, TextLight, FontStyles.Bold);
        var titleRt = titleTmp.rectTransform;
        titleRt.anchorMin = titleRt.anchorMax = new Vector2(0f, 1f);
        titleRt.pivot = new Vector2(0f, 1f);
        titleRt.sizeDelta = new Vector2(700f, 56f);
        titleRt.anchoredPosition = new Vector2(48f, -40f);

        // 列标题：功能 / 主键 / 副键
        const float ColMain  = 0.56f;  // 功能名占左边这一段
        const float ColPri   = 0.68f;  // 主键槽中心
        const float ColSec   = 0.86f;  // 副键槽中心

        // 右边留给滚动条的宽度 + 跟列表之间的间距，滚动区域要相应收窄，两者不会叠在一起。
        // 这两个常量提到前面来，是因为列标题跟下面的可滚动行必须用同一套左右边距——
        // 之前标题条是贴 boxRt 整个宽度（0~1），行是贴收窄过的 ScrollArea 宽度（0~1），
        // 两边的 0~1 对应的实际物理宽度不一样，同样的 ColPri/ColSec 分数换算出来的
        // 像素位置自然对不上，标题就跟下面的键位槽错开了。
        const float ScrollbarWidth = 8f;
        const float ScrollbarGap   = 20f;

        var headerRt = MakeRect("Header", boxRt, new Vector2(0f, 1f), new Vector2(1f, 1f));
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.sizeDelta = new Vector2(0f, 48f);
        headerRt.anchoredPosition = new Vector2(0f, -112f);
        headerRt.offsetMin = new Vector2(48f, headerRt.offsetMin.y);
        headerRt.offsetMax = new Vector2(-48f - ScrollbarWidth - ScrollbarGap, headerRt.offsetMax.y);

        var priHeader = AddTMP(headerRt, "PriHeader", L("ui_settings_keybind_primary", "主键"), 26f,
            TextAlignmentOptions.Center, TextFaint, FontStyles.Normal);
        priHeader.rectTransform.anchorMin = new Vector2(ColPri - 0.08f, 0f);
        priHeader.rectTransform.anchorMax = new Vector2(ColPri + 0.08f, 1f);
        priHeader.rectTransform.offsetMin = priHeader.rectTransform.offsetMax = Vector2.zero;

        var secHeader = AddTMP(headerRt, "SecHeader", L("ui_settings_keybind_secondary", "副键"), 26f,
            TextAlignmentOptions.Center, TextFaint, FontStyles.Normal);
        secHeader.rectTransform.anchorMin = new Vector2(ColSec - 0.08f, 0f);
        secHeader.rectTransform.anchorMax = new Vector2(ColSec + 0.08f, 1f);
        secHeader.rectTransform.offsetMin = secHeader.rectTransform.offsetMax = Vector2.zero;

        // 可滚动的功能列表
        var scrollArea = MakeRect("ScrollArea", boxRt, Vector2.zero, Vector2.one);
        scrollArea.offsetMin = new Vector2(48f, 160f);
        scrollArea.offsetMax = new Vector2(-48f - ScrollbarWidth - ScrollbarGap, -176f);

        var viewport = MakeRect("Viewport", scrollArea, Vector2.zero, Vector2.one);
        viewport.gameObject.AddComponent<Image>().color = Color.white;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport, false);
        var contentRt2 = content.AddComponent<RectTransform>();
        contentRt2.anchorMin = new Vector2(0f, 1f);
        contentRt2.anchorMax = new Vector2(1f, 1f);
        contentRt2.pivot     = new Vector2(0.5f, 1f);

        const float KeybindRowH = 84f;
        contentRt2.sizeDelta = new Vector2(0f, KeybindRows.Length * KeybindRowH);

        var scrollRect = scrollArea.gameObject.AddComponent<ScrollRect>();
        scrollRect.viewport     = viewport;
        scrollRect.content      = contentRt2;
        scrollRect.horizontal   = false;
        scrollRect.vertical     = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 24f;
        _keybindScrollRect = scrollRect;
        _keybindViewportRt = viewport;

        // 滚动条：项目统一样式，见 SkyPrisonUIScrollbar——以后别的地方要滚动条直接调
        // 这个，不要再复制一遍。边距要跟上面 ScrollArea 的 offsetMin/Max 对齐。
        SkyPrisonUIScrollbar.AttachVertical(scrollRect, boxRt, ColdGreen,
            rightMargin: 48f, topMargin: 176f, bottomMargin: 160f, width: ScrollbarWidth);

        for (int i = 0; i < KeybindRows.Length; i++)
        {
            var (action, locKey, fallback) = KeybindRows[i];
            var binding = _inputSettings.GetBinding(action);
            if (binding == null) continue;

            var rowRt = MakeRect($"Row_{i}", contentRt2, new Vector2(0f, 1f), new Vector2(1f, 1f));
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.sizeDelta = new Vector2(0f, KeybindRowH);
            rowRt.anchoredPosition = new Vector2(0f, -i * KeybindRowH);

            if (i % 2 == 0)
                rowRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);

            var nameTmp = AddTMP(rowRt, "Name", L(locKey, fallback), 30f,
                TextAlignmentOptions.MidlineLeft, TextLight, FontStyles.Normal);
            nameTmp.rectTransform.anchorMin = new Vector2(0f, 0f);
            nameTmp.rectTransform.anchorMax = new Vector2(ColMain, 1f);
            nameTmp.rectTransform.offsetMin = new Vector2(24f, 0f);
            nameTmp.rectTransform.offsetMax = Vector2.zero;

            var rowRef = new KeybindRowRef { binding = binding, rowRt = rowRt };
            int rowIndex = _keybindRowRefs.Count; // 加入 _keybindRowRefs 后的下标，光标导航按这个索引

            BuildKeybindSlot(rowRt, ColPri, binding, isSecondary: false, rowRef, rowIndex, L);
            BuildKeybindSlot(rowRt, ColSec, binding, isSecondary: true, rowRef, rowIndex, L);

            _keybindRowRefs.Add(rowRef);
        }

        _keybindCursorRow = 0;
        _keybindCursorIsSecondary = false;
        var firstSlot = GetKeybindSlot(0, false);
        if (firstSlot?.cursorHighlight != null) firstSlot.cursorHighlight.SetActive(true);

        // 底部三个按钮：恢复默认 / 返回（不保存）/ 保存并返回。
        const float FootBtnWidth  = 340f;
        const float FootBtnHeight = 88f;
        const float FootBtnGap    = 32f;

        var resetBtnRt = MakeRect("ResetDefaults", boxRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        resetBtnRt.pivot = new Vector2(0.5f, 0f);
        resetBtnRt.sizeDelta = new Vector2(FootBtnWidth, FootBtnHeight);
        resetBtnRt.anchoredPosition = new Vector2(-(FootBtnWidth + FootBtnGap), 48f);
        resetBtnRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        AddOutline(resetBtnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var resetBtn = resetBtnRt.gameObject.AddComponent<Button>();
        { var nav = resetBtn.navigation; nav.mode = Navigation.Mode.None; resetBtn.navigation = nav; }
        var resetLabel = AddTMP(resetBtnRt, "Text", L("ui_settings_keybind_reset", "恢复默认按键"), 28f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        resetLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(resetBtnRt.gameObject);
        resetBtn.onClick.AddListener(() =>
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            _inputSettings.ApplyV5DefaultKeyboardScheme();
            RefreshAllKeybindSlotVisuals();
        });

        var cancelBtnRt = MakeRect("CancelClose", boxRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        cancelBtnRt.pivot = new Vector2(0.5f, 0f);
        cancelBtnRt.sizeDelta = new Vector2(FootBtnWidth, FootBtnHeight);
        cancelBtnRt.anchoredPosition = new Vector2(0f, 48f);
        cancelBtnRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        AddOutline(cancelBtnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var cancelBtn = cancelBtnRt.gameObject.AddComponent<Button>();
        { var nav = cancelBtn.navigation; nav.mode = Navigation.Mode.None; cancelBtn.navigation = nav; }
        var cancelLabel = AddTMP(cancelBtnRt, "Text", L("ui_settings_keybind_cancel", "返回（不保存）"), 28f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        cancelLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(cancelBtnRt.gameObject);
        cancelBtn.onClick.AddListener(() => CloseKeybindDialog(save: false));

        var saveBtnRt = MakeRect("SaveClose", boxRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        saveBtnRt.pivot = new Vector2(0.5f, 0f);
        saveBtnRt.sizeDelta = new Vector2(FootBtnWidth, FootBtnHeight);
        saveBtnRt.anchoredPosition = new Vector2(FootBtnWidth + FootBtnGap, 48f);
        saveBtnRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        AddOutline(saveBtnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var saveBtn = saveBtnRt.gameObject.AddComponent<Button>();
        { var nav = saveBtn.navigation; nav.mode = Navigation.Mode.None; saveBtn.navigation = nav; }
        var saveLabel = AddTMP(saveBtnRt, "Text", L("ui_settings_keybind_save", "保存并返回"), 28f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        saveLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(saveBtnRt.gameObject);
        saveBtn.onClick.AddListener(() => CloseKeybindDialog(save: true));

        TryBindFonts(dialogRootRt);

        // 底部提示条这时候还显示着外面设置窗口那套"W 切换分类/E 确定"——W 在这个弹窗
        // 里毫无意义，E 更是会被"按任意键"的捕获逻辑直接当成新键位——必须换成这个
        // 弹窗自己的提示，关闭时再换回来。
        var keybindHints = new[]
        {
            new SkyPrisonWindowHint { iconKey = "keyboard/w", gamepadIconKey = "gamepad/up", fallbackText = "W", label = L("ui_settings_keybind_hint_move", "移动光标") },
            new SkyPrisonWindowHint { iconKey = "mouse/left", gamepadIconKey = "gamepad/xbox/a", fallbackText = "点击", label = L("ui_settings_keybind_hint_select", "选择键位重新绑定") },
            new SkyPrisonWindowHint { iconKey = "keyboard/esc", gamepadIconKey = "gamepad/xbox/b", fallbackText = "Esc", label = L("ui_settings_keybind_hint_cancel", "取消捕获 / 返回") },
        };
        SkyPrisonWindowHintBar.GetOrCreate().Show(keybindHints);
    }

    private void RefreshAllKeybindSlotVisuals()
    {
        foreach (var row in _keybindRowRefs)
        {
            if (row.binding == null) continue;
            if (row.primarySlot   != null) ApplyKeybindSlotVisual(row.primarySlot,   row.binding.primaryKey);
            if (row.secondarySlot != null) ApplyKeybindSlotVisual(row.secondarySlot, row.binding.secondaryKey);
        }
    }

    private KeybindSlotRef GetKeybindSlot(int row, bool isSecondary)
    {
        if (row < 0 || row >= _keybindRowRefs.Count) return null;
        var rowRef = _keybindRowRefs[row];
        return isSecondary ? rowRef.secondarySlot : rowRef.primarySlot;
    }

    /// <summary>把光标高亮从旧位置挪到新位置，并把新行滚动到可见范围内。
    /// 返回是否真的移动了——已经在最上/最下一行时再按上/下会被 Clamp 钳住，
    /// 光标位置没变就不该播"移动"音效，调用方靠这个返回值判断要不要播。</summary>
    private bool MoveKeybindCursor(int newRow, bool newIsSecondary)
    {
        newRow = Mathf.Clamp(newRow, 0, _keybindRowRefs.Count - 1);
        if (newRow == _keybindCursorRow && newIsSecondary == _keybindCursorIsSecondary)
            return false;

        var oldSlot = GetKeybindSlot(_keybindCursorRow, _keybindCursorIsSecondary);
        if (oldSlot?.cursorHighlight != null) oldSlot.cursorHighlight.SetActive(false);

        _keybindCursorRow         = newRow;
        _keybindCursorIsSecondary = newIsSecondary;

        var newSlot = GetKeybindSlot(newRow, newIsSecondary);
        if (newSlot?.cursorHighlight != null) newSlot.cursorHighlight.SetActive(true);

        ScrollKeybindRowIntoView(newRow);
        return true;
    }

    private void ScrollKeybindRowIntoView(int row)
    {
        if (_keybindScrollRect == null || _keybindViewportRt == null) return;
        if (row < 0 || row >= _keybindRowRefs.Count) return;
        var rowRt = _keybindRowRefs[row].rowRt;
        if (rowRt == null) return;

        float viewH    = _keybindViewportRt.rect.height;
        float rowTop    = -rowRt.anchoredPosition.y;               // 行顶部离内容顶端的距离
        float rowBottom = rowTop + rowRt.rect.height;              // 行底部离内容顶端的距离
        float curY      = -_keybindScrollRect.content.anchoredPosition.y; // 当前已经滚动掉的高度

        float newY = curY;
        if (rowTop < curY) newY = rowTop;
        else if (rowBottom > curY + viewH) newY = rowBottom - viewH;

        float maxScroll = Mathf.Max(0f, _keybindScrollRect.content.sizeDelta.y - viewH);
        newY = Mathf.Clamp(newY, 0f, maxScroll);

        // 直接改 content.anchoredPosition 会跟 ScrollRect 自己在 LateUpdate 里做的边界
        // 钳制/惯性打架（它内部认的是 verticalNormalizedPosition，不是手算的像素值）——
        // 跟 SaveSlotSelectorUI.ScrollToCursor 踩过的是同一个坑，这里改用同一套官方 API。
        _keybindScrollRect.StopMovement();
        float normalized = maxScroll > 0f ? 1f - Mathf.Clamp01(newY / maxScroll) : 1f;
        _keybindScrollRect.verticalNormalizedPosition = normalized;
    }

    private void BuildKeybindSlot(RectTransform rowRt, float colCenter, SkyPrisonInputBinding binding,
        bool isSecondary, KeybindRowRef rowRef, int rowIndex, System.Func<string, string, string> L)
    {
        var slotRt = MakeRect(isSecondary ? "Secondary" : "Primary", rowRt,
            new Vector2(colCenter - 0.08f, 0.12f), new Vector2(colCenter + 0.08f, 0.88f));
        slotRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
        AddOutline(slotRt, new Color(1f, 1f, 1f, 0.4f), 2f);
        var btn = slotRt.gameObject.AddComponent<Button>();
        // Unity 的 Button 默认 Navigation.Mode=Automatic，会自己响应手柄摇杆/D-pad 在
        // 相邻 Selectable 之间移动选中态（跟这个弹窗自己手写的光标系统是两套并行的
        // 输入响应）——摇杆推到底之后哪怕我这边已经把光标钳住不动，Unity 自己的自动
        // 导航还是会触发一次"选中态切换"，播放出多余的移动音效。整个弹窗内所有
        // Selectable 都要关掉自动导航，只留这一套手写的光标逻辑说了算。
        var btnNav = btn.navigation; btnNav.mode = Navigation.Mode.None; btn.navigation = btnNav;

        var iconRt = MakeRect("Icon", slotRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(56f, 56f);
        var icon = iconRt.gameObject.AddComponent<Image>();
        icon.raycastTarget = false;
        icon.preserveAspect = true;

        var label = AddTMP(slotRt, "Text", "", 26f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        label.raycastTarget = false;

        // 光标只有一套：键盘/手柄导航和鼠标悬停共用同一个高亮，不是各画各的——鼠标移
        // 上来就等于把键盘/手柄光标同步过来（跟 SaveSlotSelectorUI 同一个约定），
        // 所以这里不用 SkyPrisonUIButtonFeedback 的独立悬停变色，否则会出现"两套光标"。
        var cursorRt = MakeRect("Cursor", slotRt, Vector2.zero, Vector2.one);
        AddOutline(cursorRt, ColdGreen, 4f);
        cursorRt.gameObject.SetActive(false);

        var slotRef = new KeybindSlotRef { icon = icon, label = label, cursorHighlight = cursorRt.gameObject };
        ApplyKeybindSlotVisual(slotRef, isSecondary ? binding.secondaryKey : binding.primaryKey);

        if (isSecondary) { rowRef.secondaryBtn = btn; rowRef.secondarySlot = slotRef; }
        else             { rowRef.primaryBtn   = btn; rowRef.primarySlot   = slotRef; }

        var trigger = slotRt.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var pointerEnter = new UnityEngine.EventSystems.EventTrigger.Entry
        {
            eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter
        };
        pointerEnter.callback.AddListener(_ => MoveKeybindCursor(rowIndex, isSecondary));
        trigger.triggers.Add(pointerEnter);

        btn.onClick.AddListener(() => BeginKeybindCapture(binding, isSecondary, slotRef, L));
    }

    private void BeginKeybindCapture(SkyPrisonInputBinding binding, bool isSecondary, KeybindSlotRef slotRef,
        System.Func<string, string, string> L)
    {
        // 一次只能捕获一个槽位，重新选别的槽位等于取消上一个捕获（把它的图标/文字改回原值）。
        if (_keybindCapturingSlot != null)
            RefreshKeybindSlotVisual(_keybindCapturingBinding, _keybindCapturingIsSecondary);

        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        _keybindCapturingBinding     = binding;
        _keybindCapturingIsSecondary = isSecondary;
        _keybindCapturingSlot        = slotRef;
        slotRef.icon.enabled = false;
        slotRef.label.text   = L("ui_settings_keybind_press_any", "请按任意键…");
        slotRef.label.color  = ColdGreen;
    }

    // 优先显示对应的键帽图标（跟按键提示条同一套 _iconDb），找不到图标才退回文字——
    // 比如 KeyCode.None（"—"）或者美术资源里确实没画的键（比如冒号/句号）。
    private void ApplyKeybindSlotVisual(KeybindSlotRef slot, KeyCode key)
    {
        Sprite sprite = null;
        if (key != KeyCode.None && _iconDb != null)
            _iconDb.TryGetSpriteForKeyCode(key, SkyPrisonInputPromptDeviceStyle.KeyboardMouse, out sprite, out _);

        if (sprite != null)
        {
            slot.icon.enabled = true;
            slot.icon.sprite  = sprite;
            slot.label.text   = "";
        }
        else
        {
            slot.icon.enabled = false;
            slot.label.text   = KeyDisplayName(key);
            slot.label.color  = TextLight;
        }
    }

    private void RefreshKeybindSlotVisual(SkyPrisonInputBinding binding, bool isSecondary)
    {
        foreach (var row in _keybindRowRefs)
        {
            if (row.binding != binding) continue;
            var slot = isSecondary ? row.secondarySlot : row.primarySlot;
            if (slot == null) return;
            ApplyKeybindSlotVisual(slot, isSecondary ? binding.secondaryKey : binding.primaryKey);
            return;
        }
    }

    // 捕获态下每帧轮询按键——Update() 检测到 _keybindCapturingSlot != null 时调用。
    private void PollKeybindCapture()
    {
        // 手柄 B 跟键盘 Escape 一样，只取消这次捕获，不用手柄按键真的绑定进去
        // （这个弹窗只管键鼠两个槽位，手柄键有自己的 tab）。
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
        {
            RefreshKeybindSlotVisual(_keybindCapturingBinding, _keybindCapturingIsSecondary);
            _keybindCapturingBinding = null;
            _keybindCapturingSlot    = null;
            return;
        }

        foreach (KeyCode kc in KeybindCaptureCandidates)
        {
            if (kc == KeyCode.Escape) continue;
            if (!Input.GetKeyDown(kc)) continue;

            // 不阻止重复按键：这个键如果被别的功能占用，把那边的槽位清空（设为 None），
            // 当前这个槽位照常拿到这个键——两边都要重新画一遍，因为清掉的那个槽位可能
            // 在别的行。
            bool hadConflict = ClearKeybindConflicts(kc, _keybindCapturingBinding);

            if (_keybindCapturingIsSecondary) _keybindCapturingBinding.secondaryKey = kc;
            else                              _keybindCapturingBinding.primaryKey   = kc;

            _inputSettings.RebuildLookup();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(_inputSettings);
#endif
            SkyPrisonSystemSEPlayer.Play(hadConflict ? SkyPrisonSystemSEType.Switch : SkyPrisonSystemSEType.Confirm);
            RefreshAllKeybindSlotVisuals();
            _keybindCapturingBinding = null;
            _keybindCapturingSlot    = null;
            return;
        }
    }

    /// <summary>把除 exclude 外，任何主/副键槽用着 kc 的功能清空那个槽位（设 None）。
    /// 返回是否真的清过东西——只用来决定提示音，不影响赋值本身。</summary>
    private bool ClearKeybindConflicts(KeyCode kc, SkyPrisonInputBinding exclude)
    {
        if (kc == KeyCode.None) return false;
        if (_inputSettings?.bindings == null) return false;
        bool cleared = false;
        foreach (var b in _inputSettings.bindings)
        {
            if (b == null || b == exclude) continue;
            if (b.primaryKey == kc)   { b.primaryKey   = KeyCode.None; cleared = true; }
            if (b.secondaryKey == kc) { b.secondaryKey = KeyCode.None; cleared = true; }
        }
        return cleared;
    }

    // 只轮询键盘+鼠标按键（不含手柄），这个弹窗只管键鼠两个槽位。
    private static readonly KeyCode[] KeybindCaptureCandidates = BuildKeybindCaptureCandidates();

    private static KeyCode[] BuildKeybindCaptureCandidates()
    {
        var list = new List<KeyCode>();
        foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
        {
            string n = kc.ToString();
            if (n.StartsWith("Joystick")) continue;
            if (n.StartsWith("Mouse") && kc != KeyCode.Mouse0 && kc != KeyCode.Mouse1 && kc != KeyCode.Mouse2) continue;
            list.Add(kc);
        }
        return list.ToArray();
    }

    private static string KeyDisplayName(KeyCode key)
    {
        if (key == KeyCode.None) return "—";
        switch (key)
        {
            case KeyCode.Mouse0: return "鼠标左键";
            case KeyCode.Mouse1: return "鼠标右键";
            case KeyCode.Mouse2: return "鼠标中键";
            case KeyCode.Alpha0: return "0";
            case KeyCode.Alpha1: return "1";
            case KeyCode.Alpha2: return "2";
            case KeyCode.Alpha3: return "3";
            case KeyCode.Alpha4: return "4";
            case KeyCode.Alpha5: return "5";
            case KeyCode.Alpha6: return "6";
            case KeyCode.Alpha7: return "7";
            case KeyCode.Alpha8: return "8";
            case KeyCode.Alpha9: return "9";
            default: return key.ToString();
        }
    }

    /// <summary>save=false（返回/Esc/手柄B）：把这次窗口打开期间的所有改动——包括点过的
    /// "恢复默认按键"——原样撤销，回到开窗那一刻的快照。save=true（保存并返回）：保留
    /// 改动并落盘（落盘限制见类注释：Build 里这行只在编辑器下真正生效）。</summary>
    private void CloseKeybindDialog(bool save)
    {
        if (_keybindDialogRoot == null) return;
        SkyPrisonSystemSEPlayer.Play(save ? SkyPrisonSystemSEType.Confirm : SkyPrisonSystemSEType.Close);

        if (!save)
        {
            foreach (var kv in _keybindSnapshot)
            {
                kv.Key.primaryKey   = kv.Value.primary;
                kv.Key.secondaryKey = kv.Value.secondary;
            }
        }
        _inputSettings.RebuildLookup();
#if UNITY_EDITOR
        if (save && _inputSettings != null) UnityEditor.AssetDatabase.SaveAssets();
#endif

        _keybindCapturingBinding = null;
        _keybindCapturingSlot    = null;
        _keybindRowRefs.Clear();
        _keybindSnapshot.Clear();
        Destroy(_keybindDialogRoot);
        _keybindDialogRoot = null;

        // 换回外层设置窗口自己的提示条（W 切换分类/E 确定/Esc 返回），不然弹窗关掉后
        // 底部还停留在按键绑定弹窗那套提示上。
        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        string L(string key, string fallback) => locTable != null ? locTable.Get(key, fallback) : fallback;
        BuildHintBar(_rootRt, L);
    }

    // ── 手柄按键绑定弹窗：跟键鼠那份几乎同一套做法，只有一列（手柄键），
    // 捕获只认 JoystickButton 家族。────────────────────────────────────────

    private void OpenGamepadKeybindDialog(System.Func<string, string, string> L)
    {
        if (_gamepadKeybindDialogRoot != null) return;
        if (_rootRt == null) return;
        if (_inputSettings == null) return;

        var dialogRoot = new GameObject("GamepadKeybindDialog");
        dialogRoot.transform.SetParent(_rootRt, false);
        var dialogRootRt = dialogRoot.AddComponent<RectTransform>();
        dialogRootRt.anchorMin = Vector2.zero;
        dialogRootRt.anchorMax = Vector2.one;
        dialogRootRt.offsetMin = dialogRootRt.offsetMax = Vector2.zero;
        _gamepadKeybindDialogRoot = dialogRoot;
        _gamepadKeybindRowRefs.Clear();

        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);

        _gamepadKeybindSnapshot.Clear();
        if (_inputSettings.bindings != null)
            foreach (var b in _inputSettings.bindings)
                if (b != null) _gamepadKeybindSnapshot[b] = b.gamepadKey;

        var dim = MakeRect("Dim", dialogRootRt, Vector2.zero, Vector2.one);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(() => { });

        const float BoxWidth  = 1200f;
        const float BoxHeight = 1760f;
        var boxRt = MakeRect("Box", dialogRootRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        boxRt.pivot = new Vector2(0.5f, 0.5f);
        boxRt.sizeDelta = new Vector2(BoxWidth, BoxHeight);
        boxRt.anchoredPosition = Vector2.zero;

        if (_capturedBlurRT != null)
        {
            var blurImg = boxRt.gameObject.AddComponent<RawImage>();
            blurImg.texture = _capturedBlurRT;
            float wFrac = BoxWidth  / _rootRt.rect.width;
            float hFrac = BoxHeight / _rootRt.rect.height;
            blurImg.uvRect = new Rect(0.5f - wFrac * 0.5f, 0.5f - hFrac * 0.5f, wFrac, hFrac);
            if (_desaturateMaterial != null) blurImg.material = _desaturateMaterial;
        }
        else
        {
            boxRt.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.05f, 0.92f);
        }
        var boxTint = MakeRect("Tint", boxRt, Vector2.zero, Vector2.one);
        boxTint.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        AddCornerBrackets(boxRt, Color.white, 40f, 4f);

        var titleTmp = AddTMP(boxRt, "Title", L("ui_settings_gamepad_keybinds", "手柄按键绑定"), 44f,
            TextAlignmentOptions.TopLeft, TextLight, FontStyles.Bold);
        var titleRt = titleTmp.rectTransform;
        titleRt.anchorMin = titleRt.anchorMax = new Vector2(0f, 1f);
        titleRt.pivot = new Vector2(0f, 1f);
        titleRt.sizeDelta = new Vector2(700f, 56f);
        titleRt.anchoredPosition = new Vector2(48f, -40f);

        const float ColMain = 0.6f;   // 功能名占左边这一段
        const float ColKey  = 0.82f;  // 手柄键槽中心
        const float ScrollbarWidth = 8f;
        const float ScrollbarGap   = 20f;

        var headerRt = MakeRect("Header", boxRt, new Vector2(0f, 1f), new Vector2(1f, 1f));
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.sizeDelta = new Vector2(0f, 48f);
        headerRt.anchoredPosition = new Vector2(0f, -112f);
        headerRt.offsetMin = new Vector2(48f, headerRt.offsetMin.y);
        headerRt.offsetMax = new Vector2(-48f - ScrollbarWidth - ScrollbarGap, headerRt.offsetMax.y);

        var keyHeader = AddTMP(headerRt, "KeyHeader", L("ui_settings_keybind_gamepad_key", "手柄键"), 26f,
            TextAlignmentOptions.Center, TextFaint, FontStyles.Normal);
        keyHeader.rectTransform.anchorMin = new Vector2(ColKey - 0.1f, 0f);
        keyHeader.rectTransform.anchorMax = new Vector2(ColKey + 0.1f, 1f);
        keyHeader.rectTransform.offsetMin = keyHeader.rectTransform.offsetMax = Vector2.zero;

        var scrollArea = MakeRect("ScrollArea", boxRt, Vector2.zero, Vector2.one);
        scrollArea.offsetMin = new Vector2(48f, 160f);
        scrollArea.offsetMax = new Vector2(-48f - ScrollbarWidth - ScrollbarGap, -176f);

        var viewport = MakeRect("Viewport", scrollArea, Vector2.zero, Vector2.one);
        viewport.gameObject.AddComponent<Image>().color = Color.white;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport, false);
        var contentRt2 = content.AddComponent<RectTransform>();
        contentRt2.anchorMin = new Vector2(0f, 1f);
        contentRt2.anchorMax = new Vector2(1f, 1f);
        contentRt2.pivot     = new Vector2(0.5f, 1f);

        const float RowH = 84f;
        contentRt2.sizeDelta = new Vector2(0f, KeybindRows.Length * RowH);

        var scrollRect = scrollArea.gameObject.AddComponent<ScrollRect>();
        scrollRect.viewport     = viewport;
        scrollRect.content      = contentRt2;
        scrollRect.horizontal   = false;
        scrollRect.vertical     = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 24f;
        _gamepadKeybindScrollRect = scrollRect;
        _gamepadKeybindViewportRt = viewport;

        SkyPrisonUIScrollbar.AttachVertical(scrollRect, boxRt, ColdGreen,
            rightMargin: 48f, topMargin: 176f, bottomMargin: 160f, width: ScrollbarWidth);

        for (int i = 0; i < KeybindRows.Length; i++)
        {
            var (action, locKey, fallback) = KeybindRows[i];
            var binding = _inputSettings.GetBinding(action);
            if (binding == null) continue;

            var rowRt = MakeRect($"Row_{i}", contentRt2, new Vector2(0f, 1f), new Vector2(1f, 1f));
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.sizeDelta = new Vector2(0f, RowH);
            rowRt.anchoredPosition = new Vector2(0f, -i * RowH);

            if (i % 2 == 0)
                rowRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);

            var nameTmp = AddTMP(rowRt, "Name", L(locKey, fallback), 30f,
                TextAlignmentOptions.MidlineLeft, TextLight, FontStyles.Normal);
            nameTmp.rectTransform.anchorMin = new Vector2(0f, 0f);
            nameTmp.rectTransform.anchorMax = new Vector2(ColMain, 1f);
            nameTmp.rectTransform.offsetMin = new Vector2(24f, 0f);
            nameTmp.rectTransform.offsetMax = Vector2.zero;

            int rowIndex = _gamepadKeybindRowRefs.Count;
            var rowRef = new GamepadKeybindRowRef { binding = binding, rowRt = rowRt };
            BuildGamepadKeybindSlot(rowRt, ColKey, binding, rowRef, rowIndex, L);
            _gamepadKeybindRowRefs.Add(rowRef);
        }

        _gamepadCursorRow = 0;
        var firstSlot = GetGamepadKeybindSlot(0);
        if (firstSlot?.cursorHighlight != null) firstSlot.cursorHighlight.SetActive(true);

        const float FootBtnWidth  = 340f;
        const float FootBtnHeight = 88f;
        const float FootBtnGap    = 32f;

        var resetBtnRt = MakeRect("ResetDefaults", boxRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        resetBtnRt.pivot = new Vector2(0.5f, 0f);
        resetBtnRt.sizeDelta = new Vector2(FootBtnWidth, FootBtnHeight);
        resetBtnRt.anchoredPosition = new Vector2(-(FootBtnWidth + FootBtnGap), 48f);
        resetBtnRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        AddOutline(resetBtnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var resetBtn = resetBtnRt.gameObject.AddComponent<Button>();
        { var nav = resetBtn.navigation; nav.mode = Navigation.Mode.None; resetBtn.navigation = nav; }
        var resetLabel = AddTMP(resetBtnRt, "Text", L("ui_settings_keybind_reset", "恢复默认按键"), 28f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        resetLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(resetBtnRt.gameObject);
        resetBtn.onClick.AddListener(() =>
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            _inputSettings.ApplyV5DefaultKeyboardScheme();
            RefreshAllGamepadKeybindSlotVisuals();
        });

        var cancelBtnRt = MakeRect("CancelClose", boxRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        cancelBtnRt.pivot = new Vector2(0.5f, 0f);
        cancelBtnRt.sizeDelta = new Vector2(FootBtnWidth, FootBtnHeight);
        cancelBtnRt.anchoredPosition = new Vector2(0f, 48f);
        cancelBtnRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        AddOutline(cancelBtnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var cancelBtn = cancelBtnRt.gameObject.AddComponent<Button>();
        { var nav = cancelBtn.navigation; nav.mode = Navigation.Mode.None; cancelBtn.navigation = nav; }
        var cancelLabel = AddTMP(cancelBtnRt, "Text", L("ui_settings_keybind_cancel", "返回（不保存）"), 28f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        cancelLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(cancelBtnRt.gameObject);
        cancelBtn.onClick.AddListener(() => CloseGamepadKeybindDialog(save: false));

        var saveBtnRt = MakeRect("SaveClose", boxRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        saveBtnRt.pivot = new Vector2(0.5f, 0f);
        saveBtnRt.sizeDelta = new Vector2(FootBtnWidth, FootBtnHeight);
        saveBtnRt.anchoredPosition = new Vector2(FootBtnWidth + FootBtnGap, 48f);
        saveBtnRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        AddOutline(saveBtnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var saveBtn = saveBtnRt.gameObject.AddComponent<Button>();
        { var nav = saveBtn.navigation; nav.mode = Navigation.Mode.None; saveBtn.navigation = nav; }
        var saveLabel = AddTMP(saveBtnRt, "Text", L("ui_settings_keybind_save", "保存并返回"), 28f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        saveLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(saveBtnRt.gameObject);
        saveBtn.onClick.AddListener(() => CloseGamepadKeybindDialog(save: true));

        TryBindFonts(dialogRootRt);

        var gamepadHints = new[]
        {
            new SkyPrisonWindowHint { iconKey = "keyboard/w", gamepadIconKey = "gamepad/up", fallbackText = "W", label = L("ui_settings_keybind_hint_move", "移动光标") },
            new SkyPrisonWindowHint { iconKey = "mouse/left", gamepadIconKey = "gamepad/xbox/a", fallbackText = "点击", label = L("ui_settings_keybind_hint_select", "选择键位重新绑定") },
            new SkyPrisonWindowHint { iconKey = "keyboard/esc", gamepadIconKey = "gamepad/xbox/b", fallbackText = "Esc", label = L("ui_settings_keybind_hint_cancel", "取消捕获 / 返回") },
        };
        SkyPrisonWindowHintBar.GetOrCreate().Show(gamepadHints);
    }

    private void BuildGamepadKeybindSlot(RectTransform rowRt, float colCenter, SkyPrisonInputBinding binding,
        GamepadKeybindRowRef rowRef, int rowIndex, System.Func<string, string, string> L)
    {
        var slotRt = MakeRect("GamepadKey", rowRt,
            new Vector2(colCenter - 0.1f, 0.12f), new Vector2(colCenter + 0.1f, 0.88f));
        slotRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
        AddOutline(slotRt, new Color(1f, 1f, 1f, 0.4f), 2f);
        var btn = slotRt.gameObject.AddComponent<Button>();
        { var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav; }

        var iconRt = MakeRect("Icon", slotRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(56f, 56f);
        var icon = iconRt.gameObject.AddComponent<Image>();
        icon.raycastTarget = false;
        icon.preserveAspect = true;

        var label = AddTMP(slotRt, "Text", "", 26f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        label.raycastTarget = false;

        var cursorRt = MakeRect("Cursor", slotRt, Vector2.zero, Vector2.one);
        AddOutline(cursorRt, ColdGreen, 4f);
        cursorRt.gameObject.SetActive(false);

        var slotRef = new KeybindSlotRef { icon = icon, label = label, cursorHighlight = cursorRt.gameObject };
        ApplyGamepadKeybindSlotVisual(slotRef, binding.gamepadKey);
        rowRef.slot = slotRef;

        var trigger = slotRt.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var pointerEnter = new UnityEngine.EventSystems.EventTrigger.Entry
        {
            eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter
        };
        pointerEnter.callback.AddListener(_ => MoveGamepadKeybindCursor(rowIndex));
        trigger.triggers.Add(pointerEnter);

        btn.onClick.AddListener(() => BeginGamepadKeybindCapture(binding, slotRef, L));
    }

    private void BeginGamepadKeybindCapture(SkyPrisonInputBinding binding, KeybindSlotRef slotRef,
        System.Func<string, string, string> L)
    {
        if (_gamepadCapturingSlot != null)
            RefreshGamepadKeybindSlotVisual(_gamepadCapturingBinding);

        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        _gamepadCapturingBinding = binding;
        _gamepadCapturingSlot    = slotRef;
        slotRef.icon.enabled = false;
        slotRef.label.text   = L("ui_settings_keybind_press_any_gamepad", "请按手柄任意键…");
        slotRef.label.color  = ColdGreen;
    }

    // 手柄键沿用同一套 KeyDisplayName/图标解析，不需要另外写。
    private void ApplyGamepadKeybindSlotVisual(KeybindSlotRef slot, KeyCode key)
    {
        Sprite sprite = null;
        if (key != KeyCode.None && _iconDb != null)
            _iconDb.TryGetSpriteForKeyCode(key, SkyPrisonInputPromptDeviceStyle.GamepadXbox, out sprite, out _);

        if (sprite != null)
        {
            slot.icon.enabled = true;
            slot.icon.sprite  = sprite;
            slot.label.text   = "";
        }
        else
        {
            slot.icon.enabled = false;
            slot.label.text   = KeyDisplayName(key);
            slot.label.color  = TextLight;
        }
    }

    private void RefreshGamepadKeybindSlotVisual(SkyPrisonInputBinding binding)
    {
        foreach (var row in _gamepadKeybindRowRefs)
        {
            if (row.binding != binding || row.slot == null) continue;
            ApplyGamepadKeybindSlotVisual(row.slot, binding.gamepadKey);
            return;
        }
    }

    private void RefreshAllGamepadKeybindSlotVisuals()
    {
        foreach (var row in _gamepadKeybindRowRefs)
            if (row.binding != null && row.slot != null)
                ApplyGamepadKeybindSlotVisual(row.slot, row.binding.gamepadKey);
    }

    private KeybindSlotRef GetGamepadKeybindSlot(int row)
    {
        if (row < 0 || row >= _gamepadKeybindRowRefs.Count) return null;
        return _gamepadKeybindRowRefs[row].slot;
    }

    private bool MoveGamepadKeybindCursor(int newRow)
    {
        newRow = Mathf.Clamp(newRow, 0, _gamepadKeybindRowRefs.Count - 1);
        if (newRow == _gamepadCursorRow) return false;

        var oldSlot = GetGamepadKeybindSlot(_gamepadCursorRow);
        if (oldSlot?.cursorHighlight != null) oldSlot.cursorHighlight.SetActive(false);

        _gamepadCursorRow = newRow;

        var newSlot = GetGamepadKeybindSlot(newRow);
        if (newSlot?.cursorHighlight != null) newSlot.cursorHighlight.SetActive(true);

        ScrollGamepadKeybindRowIntoView(newRow);
        return true;
    }

    private void ScrollGamepadKeybindRowIntoView(int row)
    {
        if (_gamepadKeybindScrollRect == null || _gamepadKeybindViewportRt == null) return;
        if (row < 0 || row >= _gamepadKeybindRowRefs.Count) return;
        var rowRt = _gamepadKeybindRowRefs[row].rowRt;
        if (rowRt == null) return;

        float viewH = _gamepadKeybindViewportRt.rect.height;
        float rowTop = -rowRt.anchoredPosition.y;
        float rowBottom = rowTop + rowRt.rect.height;
        float curY = -_gamepadKeybindScrollRect.content.anchoredPosition.y;

        float newY = curY;
        if (rowTop < curY) newY = rowTop;
        else if (rowBottom > curY + viewH) newY = rowBottom - viewH;

        float maxScroll = Mathf.Max(0f, _gamepadKeybindScrollRect.content.sizeDelta.y - viewH);
        newY = Mathf.Clamp(newY, 0f, maxScroll);

        _gamepadKeybindScrollRect.StopMovement();
        float normalized = maxScroll > 0f ? 1f - Mathf.Clamp01(newY / maxScroll) : 1f;
        _gamepadKeybindScrollRect.verticalNormalizedPosition = normalized;
    }

    // 只轮询手柄按键（JoystickButton 家族），键鼠输入在这个弹窗里没有意义。
    private static readonly KeyCode[] GamepadCaptureCandidates = BuildGamepadCaptureCandidates();

    private static KeyCode[] BuildGamepadCaptureCandidates()
    {
        var list = new List<KeyCode>();
        foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
            if (kc.ToString().StartsWith("Joystick") && kc != KeyCode.JoystickButton1 && kc != KeyCode.JoystickButton0)
                list.Add(kc); // B 留给取消捕获，A 留给这个弹窗自己的"选择/确定"
        return list.ToArray();
    }

    private void PollGamepadKeybindCapture()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
        {
            RefreshGamepadKeybindSlotVisual(_gamepadCapturingBinding);
            _gamepadCapturingBinding = null;
            _gamepadCapturingSlot    = null;
            return;
        }

        foreach (KeyCode kc in GamepadCaptureCandidates)
        {
            if (!Input.GetKeyDown(kc)) continue;

            bool hadConflict = ClearGamepadKeybindConflicts(kc, _gamepadCapturingBinding);
            _gamepadCapturingBinding.gamepadKey = kc;

            _inputSettings.RebuildLookup();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(_inputSettings);
#endif
            SkyPrisonSystemSEPlayer.Play(hadConflict ? SkyPrisonSystemSEType.Switch : SkyPrisonSystemSEType.Confirm);
            RefreshAllGamepadKeybindSlotVisuals();
            _gamepadCapturingBinding = null;
            _gamepadCapturingSlot    = null;
            return;
        }
    }

    private bool ClearGamepadKeybindConflicts(KeyCode kc, SkyPrisonInputBinding exclude)
    {
        if (kc == KeyCode.None) return false;
        if (_inputSettings?.bindings == null) return false;
        bool cleared = false;
        foreach (var b in _inputSettings.bindings)
        {
            if (b == null || b == exclude) continue;
            if (b.gamepadKey == kc) { b.gamepadKey = KeyCode.None; cleared = true; }
        }
        return cleared;
    }

    private void CloseGamepadKeybindDialog(bool save)
    {
        if (_gamepadKeybindDialogRoot == null) return;
        SkyPrisonSystemSEPlayer.Play(save ? SkyPrisonSystemSEType.Confirm : SkyPrisonSystemSEType.Close);

        if (!save)
            foreach (var kv in _gamepadKeybindSnapshot)
                kv.Key.gamepadKey = kv.Value;

        _inputSettings.RebuildLookup();
#if UNITY_EDITOR
        if (save && _inputSettings != null) UnityEditor.AssetDatabase.SaveAssets();
#endif

        _gamepadCapturingBinding = null;
        _gamepadCapturingSlot    = null;
        _gamepadKeybindRowRefs.Clear();
        _gamepadKeybindSnapshot.Clear();
        Destroy(_gamepadKeybindDialogRoot);
        _gamepadKeybindDialogRoot = null;

        var locTable2 = Resources.Load<UILocalizationTable>("UILocalizationTable");
        string L2(string key, string fallback) => locTable2 != null ? locTable2.Get(key, fallback) : fallback;
        BuildHintBar(_rootRt, L2);
    }

    // 每个「物理按键」（不是 action！）在手柄图上的 UV 坐标（0~1，原点左下）。
    // 之前按 action 摆坐标、靠手动交换两个值来避免连线交叉，结果坐标和物理按键脱钩——
    // 交换以后标签对应的物理位置反而错了（比如 Jump 绑的是 A 键，线却指向 X 的位置）。
    // 现在改成按 KeyCode（物理按键本身）摆坐标，不管以后 action 怎么重新绑定，连线
    // 永远自动指向这个按键在手柄上的真实物理位置，不会再出现"换标签忘换线"的问题。
    // KeyCode 数值：330=A 331=B 332=X 333=Y 334=LB 335=RB 336=View 337=Menu
    // 338=L3(左摇杆按下) 339=R3(右摇杆按下) 342=方向键上 343=下 344=左 345=右。
    // 坐标是照着这张手柄图目测估的，回 Unity 里对比实际图片位置手动微调。
    private static readonly Dictionary<KeyCode, Vector2> GamepadButtonUV = new()
    {
        { (KeyCode)338, new Vector2(0.28f, 0.62f) },   // L3：左摇杆按下
        { (KeyCode)339, new Vector2(0.62f, 0.49f) },   // R3：右摇杆按下
        { (KeyCode)334, new Vector2(0.02f, 0.83f) },   // LB
        { (KeyCode)335, new Vector2(0.98f, 0.83f) },   // RB
        { (KeyCode)336, new Vector2(0.44f, 0.62f) },   // View（小方块图标）
        { (KeyCode)337, new Vector2(0.57f, 0.62f) },   // Menu（汉堡图标）
        { (KeyCode)330, new Vector2(0.735f, 0.56f) },  // A（面键最下方）
        { (KeyCode)331, new Vector2(0.80f, 0.62f) },   // B（面键最右方）
        { (KeyCode)332, new Vector2(0.66f, 0.62f) },   // X（面键最左方）
        { (KeyCode)333, new Vector2(0.735f, 0.67f) },  // Y（面键最上方）
        { (KeyCode)342, new Vector2(0.385f, 0.58f) },  // 方向键 上
        { (KeyCode)343, new Vector2(0.385f, 0.43f) },  // 方向键 下
        { (KeyCode)344, new Vector2(0.326f, 0.49f) },  // 方向键 左
        { (KeyCode)345, new Vector2(0.444f, 0.49f) },  // 方向键 右
    };

    private void BuildGamepadTabContent(RectTransform contentRt, System.Func<string, string, string> L)
    {
        // 左中右布局：手柄图居中，左右各一列真实按键绑定，每行拉一条细线连到图上
        // 对应的物理按键位置。摇杆死区/震动强度数据层还没有字段，滑块先摆着不接读写。
        const float DiagramSize = 1120f;
        const float ColWidth = 440f;
        const float ColGap = 140f;   // 列跟手柄图之间的水平间距，也是连线导轨错开摆放的可用空间
        const float RowH = 80f;

        var diagramRt = MakeRect("Diagram", contentRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        diagramRt.pivot = new Vector2(0.5f, 1f);
        diagramRt.sizeDelta = new Vector2(DiagramSize, DiagramSize);
        diagramRt.anchoredPosition = new Vector2(0f, -16f);
        var diagramImg = diagramRt.gameObject.AddComponent<RawImage>();
        diagramImg.texture = Resources.Load<Texture2D>("UI/T_Settings_GamepadDiagram");

        // 手柄图中心在 contentRt 局部坐标里的位置（diagramRt 是顶部锚点，中心要再往下移半个尺寸）
        Vector2 diagramCenter = new Vector2(0f, -16f - DiagramSize * 0.5f);

        Dictionary<SkyPrisonInputAction, SkyPrisonInputBinding> bindingByAction = new();
        if (_inputSettings != null)
            foreach (var b in _inputSettings.bindings)
                if (b != null) bindingByAction[b.action] = b;

        // 按「物理按键」分组，不是按「功能」分组——一个物理按键在手柄图上只有一个
        // 位置，之前反过来按功能建行，一旦两个功能绑了同一个物理键就会画出两行、
        // 两条线全指向同一个点，看着像"一堆功能全挤在一个键上"还分不清谁是谁。
        // 现在每个物理键只出现一行，这一行把绑在它上面的所有功能名字合并显示。
        Dictionary<KeyCode, List<SkyPrisonInputBinding>> bindingsByKey = new();
        foreach (var b in bindingByAction.Values)
        {
            if (b.gamepadKey == KeyCode.None) continue;
            if (!GamepadButtonUV.ContainsKey(b.gamepadKey)) continue;
            if (!bindingsByKey.TryGetValue(b.gamepadKey, out var list))
                bindingsByKey[b.gamepadKey] = list = new List<SkyPrisonInputBinding>();
            list.Add(b);
        }

        var leftEntries = new List<(List<SkyPrisonInputBinding> bindings, Vector2 uv)>();
        var rightEntries = new List<(List<SkyPrisonInputBinding> bindings, Vector2 uv)>();
        foreach (var kv in bindingsByKey)
        {
            Vector2 uv = GamepadButtonUV[kv.Key];
            (uv.x < 0.5f ? leftEntries : rightEntries).Add((kv.Value, uv));
        }
        // 按目标按键在图上的高度从上到下排序，配合 BuildGamepadColumn 里"每行错开
        // 一条垂直导轨"的画法，两列内部的线才不会交叉。
        leftEntries.Sort((a, b) => b.uv.y.CompareTo(a.uv.y));
        rightEntries.Sort((a, b) => b.uv.y.CompareTo(a.uv.y));

        // 列表可能比手柄图矮，下面滑块的位置得让两者取更靠下的那个，不能只看列表高度。
        float listTop = 16f + DiagramSize;
        BuildGamepadColumn(contentRt, leftEntries, leftSide: true, ColWidth, ColGap, RowH, DiagramSize, diagramCenter, L, ref listTop);
        BuildGamepadColumn(contentRt, rightEntries, leftSide: false, ColWidth, ColGap, RowH, DiagramSize, diagramCenter, L, ref listTop);

        // 死区/震动放在图和连线列表下面，走标准整行样式
        var s = SaveManager.Settings;
        var slidersRt = MakeRect("Sliders", contentRt, Vector2.zero, Vector2.one);
        slidersRt.offsetMin = new Vector2(0f, 0f);
        slidersRt.offsetMax = new Vector2(0f, -listTop - 16f);

        // 死区实际驱动 SkyPrisonInputSettings.gamepadDeadZone（真正用在移动摇杆读数上的
        // 那个共享 asset 字段，跟 PlayerAimFacingController 里那个从没被读过的同名死字段
        // 是两回事，别搞混）。
        var sharedInputSettings = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");
        BuildSliderRow(slidersRt, 0, L("ui_settings_gamepad_deadzone", "摇杆死区"),
            Mathf.InverseLerp(0f, 0.95f, s?.gamepadDeadzone ?? 0.25f), v =>
            {
                float deadzone = Mathf.Lerp(0f, 0.95f, v);
                if (s != null) s.gamepadDeadzone = deadzone;
                if (sharedInputSettings != null) sharedInputSettings.gamepadDeadZone = deadzone;
            });

        BuildSliderRow(slidersRt, 1, L("ui_settings_gamepad_vibration", "震动强度"),
            s?.vibrationStrength ?? 1f, v =>
            {
                if (s != null) s.vibrationStrength = v;
                SkyPrisonGamepadRumble.Strength = v;
            });

        BuildLinkRow(slidersRt, 2, L("ui_settings_gamepad_keybinds", "手柄按键绑定"),
            L("ui_settings_keybinds_enter", "查看/修改"), () => OpenGamepadKeybindDialog(L));
    }

    // 手柄图例里的功能名要走本地化表，不能直接用 binding.displayName（那是资产里
    // 存的原始中文，不是 loc key）。按 action 映射到真正的 "ui_xxx" key + 中文兜底
    // （兜底文字就是 displayName 原来那份中文，找不到表项时跟以前表现一致）。
    private static readonly Dictionary<SkyPrisonInputAction, string> ActionLabelKeys = new()
    {
        { SkyPrisonInputAction.Sprint,      "ui_settings_action_sprint" },
        { SkyPrisonInputAction.Sneak,       "ui_settings_action_sneak" },
        { SkyPrisonInputAction.Dodge,       "ui_settings_action_dodge" },
        { SkyPrisonInputAction.Jump,        "ui_settings_action_jump" },
        { SkyPrisonInputAction.LightAttack, "ui_settings_action_light_attack" },
        { SkyPrisonInputAction.HeavyAttack, "ui_settings_action_heavy_attack" },
        { SkyPrisonInputAction.Skill1,      "ui_settings_action_skill1" },
        { SkyPrisonInputAction.Skill2,      "ui_settings_action_skill2" },
        { SkyPrisonInputAction.Skill3,      "ui_settings_action_skill3" },
        { SkyPrisonInputAction.Reload,      "ui_settings_action_reload" },
        { SkyPrisonInputAction.Inventory,   "ui_settings_action_inventory" },
        { SkyPrisonInputAction.Map,         "ui_settings_action_map" },
        { SkyPrisonInputAction.Menu,        "ui_settings_action_menu" },
        { SkyPrisonInputAction.CharacterPanel, "ui_settings_action_characterpanel" },
        { SkyPrisonInputAction.QuickItem1,  "ui_settings_action_quickitem1" },
        { SkyPrisonInputAction.QuickItem2,  "ui_settings_action_quickitem2" },
        { SkyPrisonInputAction.QuickItem3,  "ui_settings_action_quickitem3" },
        { SkyPrisonInputAction.QuickItem4,  "ui_settings_action_quickitem4" },
    };

    private static string ActionLabel(SkyPrisonInputBinding binding, System.Func<string, string, string> L)
    {
        if (ActionLabelKeys.TryGetValue(binding.action, out string key))
            return L(key, binding.displayName);
        return binding.displayName;
    }

    // 去掉中文/英文括号里的补充说明，只留括号前的核心名字（"重攻击（长按轻攻击）" → "重攻击"）。
    private static string StripParenthetical(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int i = s.IndexOfAny(new[] { '（', '(' });
        return i < 0 ? s : s.Substring(0, i).TrimEnd();
    }

    // 手动缩字号，不依赖 TMP 的 enableAutoSizing（在这套运行时现造 UI 的场景下已经
    // 确认不可靠，见 InventoryItemDetailPanel.ShrinkNameToFit 同款教训）。从最大字号
    // 开始量宽度，超了就降一档，直到测量结果真正塞得进去为止。
    private static void ShrinkTextToFit(TMP_Text text, float availableWidth, float maxSize, float minSize)
    {
        if (text == null || availableWidth <= 0f) return;

        float size = maxSize;
        text.fontSize = size;
        text.ForceMeshUpdate();

        for (int i = 0; i < 20 && size > minSize; i++)
        {
            float width = text.GetPreferredValues(text.text, 0f, 0f).x;
            if (width <= availableWidth) break;

            size -= 1f;
            if (size < minSize) size = minSize;
            text.fontSize = size;
            text.ForceMeshUpdate();
        }
    }

    private void BuildGamepadColumn(RectTransform contentRt, List<(List<SkyPrisonInputBinding> bindings, Vector2 uv)> entries,
        bool leftSide, float colWidth, float colGap, float rowH, float diagramSize, Vector2 diagramCenter,
        System.Func<string, string, string> L, ref float listTop)
    {
        float colX = (leftSide ? -1f : 1f) * (diagramSize * 0.5f + colGap + colWidth * 0.5f);
        float startY = -16f - diagramSize * 0.25f; // 列表整体大致跟手柄图纵向居中对齐，从图的上四分之一处开始往下排

        for (int i = 0; i < entries.Count; i++)
        {
            var (bindings, uv) = entries[i];
            KeyCode gamepadKey = bindings[0].gamepadKey; // 一组里所有 binding 的 gamepadKey 都相同（按它分的组）
            float rowCenterY = startY - i * rowH;

            var rowRt = MakeRect($"Bind_{gamepadKey}", contentRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            rowRt.pivot = new Vector2(0.5f, 0.5f);
            rowRt.sizeDelta = new Vector2(colWidth, rowH);
            rowRt.anchoredPosition = new Vector2(colX, rowCenterY);

            // 左右两列对称：图标永远贴着靠手柄图那一侧（左列在右边、右列在左边），
            // 文字永远在外侧（离手柄图更远的那一边）。之前两列共用同一套"左文字右图标"
            // 布局，右列因此变成图标贴着手柄图对面、文字反而挤在图标旁边，才叠住了。
            const float Split = 0.62f; // 外侧文字区占比
            float outerMin = leftSide ? 0f : 1f - Split;
            float outerMax = leftSide ? Split : 1f;
            float innerMin = leftSide ? Split : 0f;
            float innerMax = leftSide ? 1f : 1f - Split;

            // 同一个物理键绑了多个功能时，名字用 "/" 连起来一起显示在这一行里，
            // 而不是拆成好几行、好几条线全指向同一个点。括号里的补充说明（比如
            // "长按轻攻击"）在这张紧凑的图例里挤不下，直接去掉，只留功能名本身。
            //
            // 注意：不能直接用 binding.displayName 当 L() 的 key——之前那样写等于
            // L("技能1", "技能1")，查表的 key 本身就是没翻译过的原始中文，表里
            // 根本没有这种 key，永远查不到、只会原样回退中文，不管当前是什么语言。
            // 必须用 ActionLabel() 转成真正的 "ui_xxx" key 再查表。
            string combinedName = bindings.Count == 1
                ? StripParenthetical(ActionLabel(bindings[0], L))
                : string.Join(" / ", bindings.ConvertAll(b => StripParenthetical(ActionLabel(b, L))));

            var nameTmp = AddTMP(rowRt, "Name", combinedName, 38f,
                leftSide ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight,
                TextLight, FontStyles.Normal);
            nameTmp.rectTransform.anchorMin = new Vector2(outerMin, 0f);
            nameTmp.rectTransform.anchorMax = new Vector2(outerMax, 1f);
            nameTmp.rectTransform.offsetMin = nameTmp.rectTransform.offsetMax = Vector2.zero;
            // 名字文本区宽度固定（跟按键图标平分那一行），多功能合并显示时容易超宽换行、
            // 挤成两三行。这里禁掉自动换行，超出的部分直接往外溢出显示成一整行，不裁剪。
            nameTmp.enableWordWrapping = false;
            nameTmp.overflowMode = TextOverflowModes.Overflow;
            // 英文/日文翻译经常比中文长得多（"Light Attack / Heavy Attack" 这种），
            // 一整行塞不下会直接压在图标上面。跟物品详情面板长名字同一套手动缩放方案
            // （TMP 内置 enableAutoSizing 在这类现造 UI 场景下已经确认不可靠）。
            ShrinkTextToFit(nameTmp, (outerMax - outerMin) * colWidth, 38f, 24f);

            // 按键名字直接用图标，不用文字（跟底部提示条那套图标解析共用同一个
            // SkyPrisonInputPromptIconDatabase）；找不到图标才退回文字兜底。
            Sprite keySprite = null;
            if (_iconDb != null)
                _iconDb.TryGetSpriteForKeyCode(gamepadKey, SkyPrisonInputPromptDeviceStyle.GamepadXbox, out keySprite, out _);

            var keySlotRt = MakeRect("KeySlot", rowRt, new Vector2(innerMin, 0f), new Vector2(innerMax, 1f));
            if (keySprite != null)
            {
                // 图标贴着 KeySlot 靠手柄图那一侧的边缘（左列贴右边、右列贴左边）。
                var keyImgRt = MakeRect("KeyIcon", keySlotRt, leftSide ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f),
                    leftSide ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f));
                keyImgRt.pivot = new Vector2(0.5f, 0.5f);
                keyImgRt.sizeDelta = new Vector2(56f, 56f);
                keyImgRt.anchoredPosition = new Vector2(leftSide ? -8f : 8f, 0f);
                var keyImg = keyImgRt.gameObject.AddComponent<Image>();
                keyImg.sprite = keySprite;
                keyImg.preserveAspect = true;
                keyImg.raycastTarget = false;
            }
            else
            {
                var keyTmp = AddTMP(keySlotRt, "Key", gamepadKey.ToString(), 38f,
                    leftSide ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft,
                    ColdGreen, FontStyles.Normal);
                keyTmp.rectTransform.anchorMin = Vector2.zero;
                keyTmp.rectTransform.anchorMax = Vector2.one;
                keyTmp.rectTransform.offsetMin = keyTmp.rectTransform.offsetMax = Vector2.zero;
            }

            // 连线：先横再竖（两段，不再多拐一段横的），从这一行靠手柄图那一侧的边缘
            // 直接拉到目标点的 x，再垂直下去接上目标点的 y。前面已经按目标高度从上到下
            // 排过序，两段式在这个前提下基本不会交叉；端点画一个比线粗的实心圆点标出来。
            float lineStartX = colX + (leftSide ? colWidth * 0.5f : -colWidth * 0.5f);
            Vector2 lineStart = new Vector2(lineStartX, rowCenterY);
            Vector2 lineEnd = diagramCenter + new Vector2((uv.x - 0.5f) * diagramSize, (uv.y - 0.5f) * diagramSize);

            // LB/RB 指定死了：只能是纯横线，不许拐弯，横线长度还要拉到 2.73 倍。
            bool forceStraightExtended = bindings.Exists(b => b.action == SkyPrisonInputAction.Skill1 || b.action == SkyPrisonInputAction.Skill2);
            if (forceStraightExtended)
            {
                float extendedX = lineStart.x + (lineEnd.x - lineStart.x) * 2.73f; // 2.1 × 1.3
                lineEnd = new Vector2(extendedX, lineStart.y);
            }

            // 快捷物品 1（方向键 上）也强制纯横线，不用倍数拉伸——目标 x 已经手动
            // 对齐到快捷物品 4 了，直接锁 y 就行。
            bool forceStraightPlain = bindings.Exists(b => b.action == SkyPrisonInputAction.QuickItem1);
            if (forceStraightPlain)
                lineEnd = new Vector2(lineEnd.x, lineStart.y);

            AddElbowConnector(contentRt, lineStart, lineEnd, new Color(1f, 1f, 1f, 0.3f), 3f);
            AddConnectorDot(contentRt, lineEnd, new Color(1f, 1f, 1f, 0.7f), 10f);
        }

        float colBottom = -(startY - (entries.Count - 1) * rowH) + rowH * 0.5f;
        if (colBottom > listTop) listTop = colBottom;
    }

    // ── 横平竖直的折线连接（用于手柄按键图的连接线，电路图/说明书那种拐角走线，
    // 不是直的斜线）：先横再竖，就两段——从 a 水平走到 b.x，再垂直拉到 b.y，不再
    // 多拐一段横的接进去。
    private static void AddElbowConnector(RectTransform parent, Vector2 a, Vector2 b, Color c, float thickness)
    {
        // 行高度本来就跟目标点差不多高的（比如方向键那几个），没必要为了几像素的
        // 误差硬拐一下，直接拉一条直的横线更干净。
        const float StraightSnap = 16f;
        if (Mathf.Abs(a.y - b.y) < StraightSnap)
        {
            AddAxisSegment(parent, new Vector2(a.x, a.y), new Vector2(b.x, a.y), c, thickness);
            return;
        }

        // 第一段：水平，从 a.x 到 b.x，高度停在 a.y
        AddAxisSegment(parent, new Vector2(a.x, a.y), new Vector2(b.x, a.y), c, thickness);
        // 第二段：垂直，从 a.y 拉到 b.y，停在 b.x
        AddAxisSegment(parent, new Vector2(b.x, a.y), new Vector2(b.x, b.y), c, thickness);
    }

    // 连线终点的小圆点标记，比线本身粗一圈。
    private static void AddConnectorDot(RectTransform parent, Vector2 pos, Color c, float diameter)
    {
        var dotRt = MakeRect("ConnectorDot", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        dotRt.pivot = new Vector2(0.5f, 0.5f);
        dotRt.anchoredPosition = pos;
        dotRt.sizeDelta = new Vector2(diameter, diameter);
        var img = dotRt.gameObject.AddComponent<Image>();
        img.sprite = SkyPrisonRoundedRectSprite.Get(24, 12);
        img.type = Image.Type.Simple;
        img.preserveAspect = true;
        img.color = c;
        img.raycastTarget = false;
    }

    private static void AddAxisSegment(RectTransform parent, Vector2 a, Vector2 b, Color c, float thickness)
    {
        bool horizontal = Mathf.Abs(b.x - a.x) >= Mathf.Abs(b.y - a.y);
        float length = horizontal ? Mathf.Abs(b.x - a.x) : Mathf.Abs(b.y - a.y);
        if (length < 0.5f) return; // 长度接近 0 的段不用画（避免多余的小方块）

        Vector2 center = (a + b) * 0.5f;
        var segRt = MakeRect("ConnectorSeg", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        segRt.pivot = new Vector2(0.5f, 0.5f);
        segRt.anchoredPosition = center;
        segRt.sizeDelta = horizontal ? new Vector2(length, thickness) : new Vector2(thickness, length);
        var img = segRt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
    }

    private void BuildGameplayTabContent(RectTransform contentRt, System.Func<string, string, string> L)
    {
        var s = SaveManager.Settings;

        BuildToggleRow(contentRt, 0, L("ui_settings_damage_numbers", "伤害数字显示"),
            s != null && s.showDamageNumbers, v => { if (s != null) s.showDamageNumbers = v; });

        // 底层字段 harmonyMode 语义不变（true=温和/血腥被压制，BloodVFXManager 就是
        // 按这个语义读的，不能动）。UI 展示改成"流血表现"这个业界通用叫法（跟《艾尔登
        // 法环》日文版一致），但那个词的开关方向是反的——玩家理解 ON＝显示流血，
        // 这里显示值和写回值都取反，不影响底层数据和其它读这个字段的系统。
        BuildToggleRow(contentRt, 1, L("ui_settings_gore_effects", "流血效果"),
            s != null && !s.harmonyMode, v =>
            {
                if (s != null) s.harmonyMode = !v;
                BloodVFXManager.HarmonyMode = !v; // 实时生效，不用等关窗口/重开局才应用
            });

        BuildToggleRow(contentRt, 2, L("ui_settings_autosave", "自动保存"),
            s == null || s.autoSaveEnabled, v => { if (s != null) s.autoSaveEnabled = v; });

        // 商店出售稀有物品前二次确认——用户明确要求可调阈值(Lv5/Lv8/不提示)，默认Lv5以上。
        string[] sellConfirmLabels =
        {
            L("ui_settings_sell_confirm_lv5", "Lv5以上二次确认"),
            L("ui_settings_sell_confirm_lv8", "Lv8以上二次确认"),
            L("ui_settings_sell_confirm_off", "不提示"),
        };
        int[] sellConfirmValues = { 5, 8, -1 };
        int sellConfirmIdx = 0;
        if (s != null)
        {
            int foundIdx = System.Array.IndexOf(sellConfirmValues, s.sellConfirmRarityThreshold);
            sellConfirmIdx = foundIdx >= 0 ? foundIdx : 0;
        }
        BuildValueCycleRow(contentRt, 3, L("ui_settings_sell_confirm", "出售稀有物品二次确认"),
            sellConfirmLabels, sellConfirmLabels[sellConfirmIdx],
            v =>
            {
                int idx = System.Array.IndexOf(sellConfirmLabels, v);
                if (idx < 0) idx = 0;
                if (s != null) s.sellConfirmRarityThreshold = sellConfirmValues[idx];
            });

        BuildLinkRow(contentRt, 4, L("ui_settings_clear_cache", "清除缓存"),
            L("ui_settings_clear_cache_action", "清除"), ShowClearCacheConfirm);
    }

    private GameObject _clearCacheConfirmOverlay;

    /// <summary>清除缓存前先弹窗告知当前占用大小，避免玩家在不知道会删多少东西的
    /// 情况下手滑点掉——跟存档删除确认同一个道理，删除类操作都要有二次确认。</summary>
    private void ShowClearCacheConfirm()
    {
        if (_clearCacheConfirmOverlay != null) return;
        if (_rootRt == null) return;

        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        string L(string key, string fallback) => locTable != null ? locTable.Get(key, fallback) : fallback;

        _clearCacheConfirmOverlay = new GameObject("ClearCacheConfirm");
        _clearCacheConfirmOverlay.transform.SetParent(_rootRt, false);
        var overlayRt = _clearCacheConfirmOverlay.AddComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = overlayRt.offsetMax = Vector2.zero;

        var dim = MakeRect("Dim", overlayRt, Vector2.zero, Vector2.one);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(() => { }); // 挡住点击穿透

        // 跟窗口自己的背景/亮度弹窗同一套做法：复用窗口打开时截好的那张模糊图，按这个
        // 盒子在屏幕上的实际占比裁一小块 UV（不是整张缩小塞进去），再去色——不是简单
        // 铺一块纯黑，这样盒子里看到的画面跟盒子外的模糊背景是同一张图的同一块区域，
        // 边缘对得上，不会有"重复绘制"的违和感。
        const float BoxWidth  = 1040f;
        const float BoxHeight = 480f;
        var box = MakeRect("Box", overlayRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        box.pivot = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(BoxWidth, BoxHeight);
        box.anchoredPosition = Vector2.zero;

        if (_capturedBlurRT != null)
        {
            var blurImg = box.gameObject.AddComponent<RawImage>();
            blurImg.texture = _capturedBlurRT;
            float wFrac = BoxWidth  / _rootRt.rect.width;
            float hFrac = BoxHeight / _rootRt.rect.height;
            blurImg.uvRect = new Rect(0.5f - wFrac * 0.5f, 0.5f - hFrac * 0.5f, wFrac, hFrac);
            if (_desaturateMaterial != null) blurImg.material = _desaturateMaterial;
        }
        else
        {
            box.gameObject.AddComponent<Image>().color = PanelOverlay;
        }
        var boxTint = MakeRect("Tint", box, Vector2.zero, Vector2.one);
        boxTint.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        AddCornerBrackets(box, Color.white, 40f, 4f);

        var titleTmp = AddTMP(box, "Title", L("ui_settings_clear_cache_title", "清除缓存？"), 40,
            TextAlignmentOptions.Center, TextLight, FontStyles.Bold);
        Anchor(titleTmp.rectTransform, 0f, 0.66f, 1f, 0.86f);

        string body = string.Format(
            L("ui_settings_clear_cache_body_format", "当前缓存占用 {0}，清除后素材需要重新生成，确定要清除吗？"),
            GamePaths.GetCacheSizeFormatted());
        var bodyTmp = AddTMP(box, "Body", body, 28,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        Anchor(bodyTmp.rectTransform, 0.06f, 0.40f, 0.94f, 0.64f);

        const float BtnWidth = 360f;
        const float BtnHeight = 96f;
        const float BtnGap = 40f;

        var cancelBtnRt = MakeRect("Cancel", box, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        cancelBtnRt.pivot = new Vector2(0.5f, 0f);
        cancelBtnRt.sizeDelta = new Vector2(BtnWidth, BtnHeight);
        cancelBtnRt.anchoredPosition = new Vector2(-(BtnWidth * 0.5f + BtnGap * 0.5f), 48f);
        cancelBtnRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        AddOutline(cancelBtnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var cancelBtn = cancelBtnRt.gameObject.AddComponent<Button>();
        { var nav = cancelBtn.navigation; nav.mode = Navigation.Mode.None; cancelBtn.navigation = nav; }
        var cancelLabel = AddTMP(cancelBtnRt, "Text", L("ui_saveslot_return", "返回"), 32f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        cancelLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(cancelBtnRt.gameObject);
        cancelBtn.onClick.AddListener(() =>
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);
            Destroy(_clearCacheConfirmOverlay);
            _clearCacheConfirmOverlay = null;
        });

        var confirmBtnRt = MakeRect("Confirm", box, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        confirmBtnRt.pivot = new Vector2(0.5f, 0f);
        confirmBtnRt.sizeDelta = new Vector2(BtnWidth, BtnHeight);
        confirmBtnRt.anchoredPosition = new Vector2(BtnWidth * 0.5f + BtnGap * 0.5f, 48f);
        confirmBtnRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        AddOutline(confirmBtnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var confirmBtn = confirmBtnRt.gameObject.AddComponent<Button>();
        var confirmLabel = AddTMP(confirmBtnRt, "Text", L("ui_settings_clear_cache_action", "清除"), 32f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        confirmLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(confirmBtnRt.gameObject);
        confirmBtn.onClick.AddListener(() =>
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
            GamePaths.ClearCache();
            Destroy(_clearCacheConfirmOverlay);
            _clearCacheConfirmOverlay = null;
        });

        TryBindFonts(_clearCacheConfirmOverlay.transform);
    }

    // ── 通用行控件：开关 ON/OFF，点了就切换，右侧居中显示当前值（跟参考的商业
    // 游戏设置列表一致的交互方式）。
    private void BuildToggleRow(RectTransform contentRt, int rowIndex, string label, bool initial, System.Action<bool> onChanged)
    {
        BuildSettingsRow(contentRt, rowIndex, label, out var rightSlot, out var navEntry);

        var valueRt = MakeRect("Value", rightSlot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        valueRt.pivot = new Vector2(0.5f, 0.5f);
        valueRt.sizeDelta = new Vector2(240f, RowHeight);
        // 之前这里只有子物体的文字（raycastTarget=false）能挡鼠标，Button 自己这个物体上
        // 一个 Graphic 都没有——GraphicRaycaster 压根找不到东西可以命中，点了跟没点一样。
        // 加一个透明但 raycastTarget=true 的背景，Button 自己就能接住点击了。
        valueRt.gameObject.AddComponent<Image>().color = Color.clear;
        var btn = valueRt.gameObject.AddComponent<Button>();
        bool state = initial;
        var valueTmp = AddTMP(valueRt, "Text", state ? "ON" : "OFF", 40f,
            TextAlignmentOptions.Center, state ? Color.white : TextFaint, FontStyles.Normal);
        valueTmp.raycastTarget = false;

        btn.onClick.AddListener(() =>
        {
            state = !state;
            valueTmp.text = state ? "ON" : "OFF";
            valueTmp.color = state ? Color.white : TextFaint;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            onChanged(state);
            // 之前只有关窗口那一刻才存盘，中途崩溃/被强杀就白改了。开关是离散事件，
            // 点一下存一次盘完全没有性能顾虑，直接落盘。
            SaveManager.SaveSettings();
        });

        // 只接 Interact，不接 Left/Right——这样 Left 才能按"没有横向调值就退回侧栏"
        // 的通用规则正常工作，不会被这里的切换吃掉。
        navEntry.onConfirm = () => btn.onClick.Invoke();
    }

    // ── 通用行控件：细轨道滑块直接嵌在行右侧（跟亮度弹窗内的滑块同一套细节样式），
    // 用于 0~1 归一化的连续值（音量/灵敏度这类）。
    private void BuildSliderRow(RectTransform contentRt, int rowIndex, string label, float initial01, System.Action<float> onChanged)
    {
        BuildSettingsRow(contentRt, rowIndex, label, out var rightSlot, out var navEntry);
        const float TrackHeight = 40f;

        const float SliderWidth = 520f;
        const float ValueBoxWidth = 112f;
        const float Gap = 32f;
        // 滑块+数值这一整组在右侧区域里居中摆放，不贴右边缘（跟别的控件行同一套规则）。
        float groupWidth = SliderWidth + Gap + ValueBoxWidth;
        float sliderCenterX = -groupWidth * 0.5f + SliderWidth * 0.5f;
        float valueCenterX  =  groupWidth * 0.5f - ValueBoxWidth * 0.5f;

        var valueTmp = AddTMP(rightSlot, "Value", "", 40f,
            TextAlignmentOptions.MidlineRight, Color.white, FontStyles.Normal);
        var valueRt = valueTmp.rectTransform;
        valueRt.anchorMin = valueRt.anchorMax = new Vector2(0.5f, 0.5f);
        valueRt.pivot = new Vector2(0.5f, 0.5f);
        valueRt.sizeDelta = new Vector2(ValueBoxWidth, RowHeight);
        valueRt.anchoredPosition = new Vector2(valueCenterX, 0f);

        // 滑块给固定宽度，不铺满整个右侧区域——太宽的话细轨道看起来空得很奇怪。
        var sliderRt = MakeRect("Slider", rightSlot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        sliderRt.pivot = new Vector2(0.5f, 0.5f);
        sliderRt.sizeDelta = new Vector2(SliderWidth, TrackHeight);
        sliderRt.anchoredPosition = new Vector2(sliderCenterX, 0f);
        var slider = sliderRt.gameObject.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.transition = Selectable.Transition.None;

        // 轨道细线只有 3px 高，之前没有别的图形覆盖整条轨道高度，鼠标要精准点在这条线上
        // 才能开始拖，稍微偏一点就点不到——加一个铺满整个 TrackHeight 的透明点击区。
        var clickAreaRt = MakeRect("ClickArea", sliderRt, Vector2.zero, Vector2.one);
        clickAreaRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

        var bgRt = MakeRect("Background", sliderRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
        bgRt.pivot = new Vector2(0.5f, 0.5f);
        bgRt.sizeDelta = new Vector2(0f, 6f);
        bgRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);

        var fillAreaRt = MakeRect("FillArea", sliderRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
        fillAreaRt.pivot = new Vector2(0.5f, 0.5f);
        fillAreaRt.sizeDelta = new Vector2(0f, 6f);
        var fillRt = MakeRect("Fill", fillAreaRt, Vector2.zero, Vector2.one);
        fillRt.gameObject.AddComponent<Image>().color = ColdGreen;
        slider.fillRect = fillRt;
        slider.targetGraphic = null;

        var handleAreaRt = MakeRect("HandleArea", sliderRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
        handleAreaRt.pivot = new Vector2(0.5f, 0.5f);
        handleAreaRt.sizeDelta = new Vector2(0f, TrackHeight);
        var handleRt = MakeRect("Handle", handleAreaRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.sizeDelta = new Vector2(28f, 28f);
        var handleImg = handleRt.gameObject.AddComponent<Image>();
        handleImg.sprite = SkyPrisonRoundedRectSprite.Get(24, 12);
        handleImg.type = Image.Type.Simple;
        handleImg.preserveAspect = true;
        handleImg.color = Color.white;
        slider.handleRect = handleRt;

        slider.value = Mathf.Clamp01(initial01);
        valueTmp.text = Mathf.RoundToInt(slider.value * 100f).ToString();
        slider.onValueChanged.AddListener(v =>
        {
            valueTmp.text = Mathf.RoundToInt(v * 100f).ToString();
            onChanged(v);
        });

        // 滑块拖动期间 onValueChanged 每帧都会触发，每次都存盘会在拖动时疯狂写磁盘。
        // 只在松开鼠标那一刻存一次——EventTrigger 挂在整个 sliderRt 上，拖拽/点击/
        // 键盘导航调值（navEntry.onHorizontal 每次调用后也会走到这里松手）都能覆盖到。
        var pointerUpTrigger = sliderRt.gameObject.AddComponent<EventTrigger>();
        var pointerUpEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        pointerUpEntry.callback.AddListener(_ => SaveManager.SaveSettings());
        pointerUpTrigger.triggers.Add(pointerUpEntry);

        // 没鼠标也能拖：Left/Right 每次挪 5%。这是离散的单次按键事件（边缘检测，
        // 不是每帧触发），直接存盘没有拖动时那种连续调用的顾虑。
        navEntry.onHorizontal = dir =>
        {
            slider.value = Mathf.Clamp01(slider.value + dir * 0.05f);
            SaveManager.SaveSettings();
        };
    }

    // ── 通用行控件：左右箭头切换固定选项列表（分辨率/帧率/画质预设/语言这类离散值）。
    //
    // 之前每点一下箭头就立刻真正应用（比如切分辨率/窗口模式），玩家想从选项列表
    // 一头快速切到另一头，中途经过的每个选项都会被当真实目标应用一次——切分辨率
    // 这种本来就有画面闪动风险的操作，被迫经历好几次没必要的真实切换，很烦躁。
    // 现在改成：箭头切换只更新显示文字（手感不变），真正调用 onChanged/存盘延迟到
    // "离开这一行"那一刻才触发一次（鼠标移出这一行 / 键盘或手柄光标移到别的行 /
    // 关闭设置窗口，见 RowNavEntry.onBlur 和 _allCycleCommits）。
    private void BuildValueCycleRow(RectTransform contentRt, int rowIndex, string label, string[] options, string initial, System.Action<string> onChanged)
    {
        RectTransform rowRt = BuildSettingsRow(contentRt, rowIndex, label, out var rightSlot, out var navEntry);
        if (options == null || options.Length == 0) return;

        int idx = System.Array.IndexOf(options, initial);
        if (idx < 0) idx = 0;
        int committedIdx = idx;

        var valueTmp = AddTMP(rightSlot, "Value", options[idx], 40f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        var valueRt = valueTmp.rectTransform;
        valueRt.anchorMin = new Vector2(0.5f, 0f);
        valueRt.anchorMax = new Vector2(0.5f, 1f);
        valueRt.pivot = new Vector2(0.5f, 0.5f);
        valueRt.sizeDelta = new Vector2(320f, 0f);
        valueRt.anchoredPosition = Vector2.zero;

        void Advance(int dir)
        {
            idx = (idx + dir + options.Length) % options.Length;
            valueTmp.text = options[idx];
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            // 不在这里调用 onChanged/存盘——只是预览，真正生效延迟到 Commit()。
        }

        void Commit()
        {
            if (idx == committedIdx) return;
            committedIdx = idx;
            onChanged(options[idx]);
            SaveManager.SaveSettings();
        }

        navEntry.onBlur = Commit;
        _allCycleCommits.Add(Commit);

        // 鼠标路径：没有"选中光标"这个概念，用"鼠标指针移出这一整行"代替"离开"。
        var rowExitTrigger = rowRt.gameObject.AddComponent<EventTrigger>();
        var rowExitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        rowExitEntry.callback.AddListener(_ => Commit());
        rowExitTrigger.triggers.Add(rowExitEntry);

        void SpawnArrow(bool leftSide, int dir)
        {
            var arrowRt = MakeRect(leftSide ? "ArrowL" : "ArrowR", rightSlot,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            arrowRt.pivot = new Vector2(0.5f, 0.5f);
            arrowRt.sizeDelta = new Vector2(64f, RowHeight);
            arrowRt.anchoredPosition = new Vector2(leftSide ? -220f : 220f, 0f);
            // 同上：Button 自己的物体得有个 raycastTarget=true 的 Graphic，不能全指望
            // 子物体文字（那个是 false）。
            arrowRt.gameObject.AddComponent<Image>().color = Color.clear;
            var btn = arrowRt.gameObject.AddComponent<Button>();
            var arrowTmp = AddTMP(arrowRt, "Text", leftSide ? "<" : ">", 40f,
                TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            arrowTmp.raycastTarget = false;
            SkyPrisonUIButtonFeedback.Attach(arrowRt.gameObject);
            btn.onClick.AddListener(() => Advance(dir));
        }

        SpawnArrow(true, -1);
        SpawnArrow(false, 1);

        // 没鼠标也能切：Left/Right 直接走跟箭头按钮一样的 Advance 逻辑。
        navEntry.onHorizontal = Advance;
    }

    // ── 通用行控件：右侧一个跳转/动作按钮（按键绑定列表、清除缓存这类不是简单
    // 开关/数值的项目，点了触发一个动作）。
    private void BuildLinkRow(RectTransform contentRt, int rowIndex, string label, string actionText, System.Action onClick)
    {
        BuildSettingsRow(contentRt, rowIndex, label, out var rightSlot, out var navEntry);

        var btnRt = MakeRect("ActionButton", rightSlot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        btnRt.pivot = new Vector2(0.5f, 0.5f);
        btnRt.sizeDelta = new Vector2(280f, 80f);
        var btnImg = btnRt.gameObject.AddComponent<Image>();
        btnImg.color = new Color(1f, 1f, 1f, 0.02f);
        AddOutline(btnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var btn = btnRt.gameObject.AddComponent<Button>();
        var btnLabel = AddTMP(btnRt, "Text", actionText, 30f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        btnLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(btnRt.gameObject);
        btn.onClick.AddListener(() =>
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            onClick();
        });

        navEntry.onConfirm = () => btn.onClick.Invoke();
    }

    // ── 亮度校准：左边滑块调 _Brightness，右边参考图用同一条伽马曲线实时预览，
    // 调到刚好能看清图案轮廓为准。目前只写 SaveManager.Settings.brightness 做实时预览，
    // 真正作用到全场景渲染的后处理 Volume 还没接（见 SettingsDisplayPanel.ApplyToEngine 的注释）。
    // ── 设置行标准：所有 Tab 内容今后都按这个行高走，不再各自随手写数字。────
    // 参考商业游戏设置列表的比例（label 左对齐 / 控件右对齐 / 行底细分隔线 / 每行
    // 一层很弱的底色，不是靠行间空白分隔）。
    private const float RowHeight = 128f;
    private const float RowLabelSplit = 0.5f; // 左边 label 区、右边控件区的分界比例
    private const float RowSidePadding = 48f; // 左右都别贴边，留出呼吸空间

    private RectTransform BuildSettingsRow(RectTransform parent, int rowIndex, string label, out RectTransform rightSlot)
        => BuildSettingsRow(parent, rowIndex, label, out rightSlot, out _);

    private RectTransform BuildSettingsRow(RectTransform parent, int rowIndex, string label, out RectTransform rightSlot, out RowNavEntry navEntry)
    {
        var rowRt = MakeRect($"Row_{rowIndex}", parent, new Vector2(0f, 1f), new Vector2(1f, 1f));
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.sizeDelta = new Vector2(0f, RowHeight);
        rowRt.anchoredPosition = new Vector2(0f, -rowIndex * RowHeight);

        // 底色改成横向渐变（中间实、两端淡出），不是一整块死板的平色填充。
        var bgImg = rowRt.gameObject.AddComponent<Image>();
        bgImg.sprite = GetRowGradientSprite();
        bgImg.type = Image.Type.Simple;
        bgImg.color = new Color(1f, 1f, 1f, 0.06f);

        AddLine(rowRt, "Divider", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            Vector2.zero, new Vector2(0f, 1f), new Color(1f, 1f, 1f, 0.1f));

        // 左：label 靠左对齐，但离行左边缘留一段 padding，不贴边
        var labelRt = MakeRect("Label", rowRt, Vector2.zero, new Vector2(RowLabelSplit, 1f));
        var labelTmp = AddTMP(labelRt, "Text", label, 48f,
            TextAlignmentOptions.MidlineLeft, TextLight, FontStyles.Normal);
        labelTmp.rectTransform.anchorMin = Vector2.zero;
        labelTmp.rectTransform.anchorMax = Vector2.one;
        labelTmp.rectTransform.offsetMin = new Vector2(RowSidePadding, 0f);
        labelTmp.rectTransform.offsetMax = Vector2.zero;

        // 右：控件区离行右边缘也留同样的 padding；控件本身在这块区域里居中摆放
        // （不是贴着右边缘），由各自的 Build 方法自己决定控件宽度再居中。
        rightSlot = MakeRect("Right", rowRt, new Vector2(RowLabelSplit, 0f), Vector2.one);
        rightSlot.offsetMax = new Vector2(-RowSidePadding, 0f);

        // 无鼠标操作用的行光标——冷绿细边框，默认隐藏，被 Update() 的行导航选中才显示。
        var cursorRt = MakeRect("NavCursor", rowRt, Vector2.zero, Vector2.one);
        AddOutline(cursorRt, ColdGreen, 3f);
        cursorRt.gameObject.SetActive(false);

        navEntry = new RowNavEntry { cursor = cursorRt.gameObject };
        _buildingNavList?.Add(navEntry);
        return rowRt;
    }

    // ── 亮度：普通设置行 + 一个按钮，点了才弹出居中的校准弹窗——不是每个 Tab 打开
    // 就顶一大块图占地方，跟别的设置行（开关/下拉）保持同样的行高节奏。
    private void BuildBrightnessRow(RectTransform contentRt, int rowIndex, System.Func<string, string, string> L)
    {
        BuildSettingsRow(contentRt, rowIndex, L("ui_settings_brightness", "亮度"), out var rightSlot, out var navEntry);

        // 控件在右侧区域里居中摆放，不是贴着行的右边缘。
        var btnRt = MakeRect("EnterButton", rightSlot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        btnRt.pivot = new Vector2(0.5f, 0.5f);
        btnRt.sizeDelta = new Vector2(360f, 88f);
        btnRt.anchoredPosition = Vector2.zero;
        var btnImg = btnRt.gameObject.AddComponent<Image>();
        btnImg.color = new Color(1f, 1f, 1f, 0.02f);
        // 没光标时是白色细线框，SkyPrisonUIButtonFeedback 会在悬停时自动把它连同文字
        // 一起染成冷绿（跟侧栏选中项那套联动逻辑一致），不用在这里手动切颜色。
        AddOutline(btnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var btn = btnRt.gameObject.AddComponent<Button>();
        var btnLabel = AddTMP(btnRt, "Text", L("ui_settings_brightness_enter", "亮度设置"), 32f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        btnLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(btnRt.gameObject);
        btn.onClick.AddListener(() =>
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            OpenBrightnessDialog(L);
        });

        navEntry.onConfirm = () => btn.onClick.Invoke();
    }

    // ── 亮度校准弹窗：居中弹出，图在上、滑块在下紧贴着图（滑块跟图同宽，像是图的
    // 一部分而不是独立一行）。埃尔登法环那套做法：左右两张图共用同一块黑底面板，
    // 左边固定亮度（清楚可见，当参照答案），右边跟着滑块走，调到刚好能隐约看清
    // 图案就是合适的亮度。滑块细轨道：左段冷绿=有效量，右段灰=无效量，白色圆球
    // 手柄比轨道粗一圈（用 preserveAspect 保证是正圆，不被拉伸成椭圆）。目前只写
    // SaveManager.Settings.brightness 做实时预览，真正作用到全场景渲染的后处理
    // Volume 还没接（见 SettingsDisplayPanel.ApplyToEngine 的注释）。确认按钮/Esc
    // 都只是关掉弹窗回到设置主界面，不关设置窗口本身。
    private void OpenBrightnessDialog(System.Func<string, string, string> L)
    {
        if (_brightnessDialogRoot != null) return;
        if (_rootRt == null) return;

        var dialogRoot = new GameObject("BrightnessDialog");
        dialogRoot.transform.SetParent(_rootRt, false);
        var dialogRootRt = dialogRoot.AddComponent<RectTransform>();
        dialogRootRt.anchorMin = Vector2.zero;
        dialogRootRt.anchorMax = Vector2.one;
        dialogRootRt.offsetMin = dialogRootRt.offsetMax = Vector2.zero;
        _brightnessDialogRoot = dialogRoot;

        var dim = MakeRect("Dim", dialogRootRt, Vector2.zero, Vector2.one);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(() => { }); // 挡住点击穿透

        // 跟项目里其它角标窗口（暂停菜单的确认弹窗等）同一套做法：背景复用窗口自己
        // 截好的那张模糊图，按这个盒子在屏幕上的实际占比裁一小块 UV 出来（不是整张
        // 缩小塞进去），去色 + 叠一层半透明黑，四角描白色 L 形角标，不用普通描边框。
        const float BoxWidth  = 1520f;
        const float BoxHeight = 1240f;
        var boxRt = MakeRect("Box", dialogRootRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        boxRt.pivot = new Vector2(0.5f, 0.5f);
        boxRt.sizeDelta = new Vector2(BoxWidth, BoxHeight);
        boxRt.anchoredPosition = Vector2.zero;

        if (_capturedBlurRT != null)
        {
            var blurImg = boxRt.gameObject.AddComponent<RawImage>();
            blurImg.texture = _capturedBlurRT;
            float wFrac = BoxWidth  / _rootRt.rect.width;
            float hFrac = BoxHeight / _rootRt.rect.height;
            blurImg.uvRect = new Rect(0.5f - wFrac * 0.5f, 0.5f - hFrac * 0.5f, wFrac, hFrac);
            if (_desaturateMaterial != null) blurImg.material = _desaturateMaterial;
        }
        else
        {
            boxRt.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.05f, 0.92f);
        }
        var boxTint = MakeRect("Tint", boxRt, Vector2.zero, Vector2.one);
        boxTint.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        AddCornerBrackets(boxRt, Color.white, 40f, 4f);

        var titleTmp = AddTMP(boxRt, "Title", L("ui_settings_brightness", "亮度"), 40f,
            TextAlignmentOptions.TopLeft, TextLight, FontStyles.Bold);
        var titleRt = titleTmp.rectTransform;
        titleRt.anchorMin = titleRt.anchorMax = new Vector2(0f, 1f);
        titleRt.pivot = new Vector2(0f, 1f);
        titleRt.sizeDelta = new Vector2(600f, 56f);
        titleRt.anchoredPosition = new Vector2(48f, -40f);

        var hintTmp = AddTMP(boxRt, "Hint", L("ui_settings_brightness_hint", "调整亮度，直到右侧标志隐约可见。"), 28f,
            TextAlignmentOptions.Center, TextFaint, FontStyles.Normal);
        var hintRt = hintTmp.rectTransform;
        hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 1f);
        hintRt.pivot = new Vector2(0.5f, 1f);
        hintRt.sizeDelta = new Vector2(BoxWidth - 96f, 40f);
        hintRt.anchoredPosition = new Vector2(0f, -104f);

        var innerRt = MakeRect("Inner", boxRt, Vector2.zero, Vector2.one);
        innerRt.offsetMin = new Vector2(48f, 120f);
        innerRt.offsetMax = new Vector2(-48f, -160f);
        BuildBrightnessCalibrator(innerRt, L);

        // 取消要能把这次弹窗里改动的亮度值退回去，所以开窗那一刻先记一下原值。
        float initialBrightness = SaveManager.Settings != null ? SaveManager.Settings.brightness : 1f;
        _brightnessDialogInitialValue = initialBrightness;

        const float BtnWidth = 360f;
        const float BtnHeight = 96f;
        const float BtnGap = 40f;

        var cancelBtnRt = MakeRect("Cancel", boxRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        cancelBtnRt.pivot = new Vector2(0.5f, 0f);
        cancelBtnRt.sizeDelta = new Vector2(BtnWidth, BtnHeight);
        cancelBtnRt.anchoredPosition = new Vector2(-(BtnWidth * 0.5f + BtnGap * 0.5f), 48f);
        cancelBtnRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        AddOutline(cancelBtnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var cancelBtn = cancelBtnRt.gameObject.AddComponent<Button>();
        { var nav = cancelBtn.navigation; nav.mode = Navigation.Mode.None; cancelBtn.navigation = nav; }
        var cancelLabel = AddTMP(cancelBtnRt, "Text", L("ui_settings_brightness_cancel", "取消"), 32f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        cancelLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(cancelBtnRt.gameObject);
        cancelBtn.onClick.AddListener(() =>
        {
            if (SaveManager.Settings != null) SaveManager.Settings.brightness = initialBrightness;
            SkyPrisonBrightnessManager.Apply(initialBrightness);
            CloseBrightnessDialog();
        });

        var confirmBtnRt = MakeRect("Confirm", boxRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        confirmBtnRt.pivot = new Vector2(0.5f, 0f);
        confirmBtnRt.sizeDelta = new Vector2(BtnWidth, BtnHeight);
        confirmBtnRt.anchoredPosition = new Vector2(BtnWidth * 0.5f + BtnGap * 0.5f, 48f);
        confirmBtnRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        // 没光标时是白色细线框，跟其它按钮一样，SkyPrisonUIButtonFeedback 会在悬停时
        // 自动把它染成冷绿，不用常态就绿。
        AddOutline(confirmBtnRt, new Color(1f, 1f, 1f, 0.5f), 3f);
        var confirmBtn = confirmBtnRt.gameObject.AddComponent<Button>();
        var confirmLabel = AddTMP(confirmBtnRt, "Text", L("ui_settings_brightness_confirm", "确认返回"), 32f,
            TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
        confirmLabel.raycastTarget = false;
        SkyPrisonUIButtonFeedback.Attach(confirmBtnRt.gameObject);
        confirmBtn.onClick.AddListener(CloseBrightnessDialog);

        // TryBindFonts(rootRt) 在 BuildRoutine 里只跑一次，那时候这个弹窗还没造出来，
        // 弹窗里的字用的是 TMP 默认字体（没有中文字形）——同一个字符串"亮度"在弹窗
        // 外面显示正常、弹窗里却是方块，就是因为字体压根没绑上，不是缺字形。
        TryBindFonts(dialogRootRt);
    }

    private void CloseBrightnessDialog()
    {
        if (_brightnessDialogRoot == null) return;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);
        // 亮度弹窗内部拖动/按方向键调值是逐帧连续触发的（跟离散的开关/选项不一样），
        // 不适合每次改动都存盘——统一在关弹窗（确认或取消，取消已经把值退回原始
        // 值了）这一刻存一次，覆盖鼠标拖动和无鼠标操作两种路径。
        SaveManager.SaveSettings();
        Destroy(_brightnessDialogRoot);
        _brightnessDialogRoot = null;
    }

    private void BuildBrightnessCalibrator(RectTransform contentRt, System.Func<string, string, string> L)
    {
        const float ImageSize   = 680f;
        const float ImageGap    = 40f;
        const float TrackHeight = 40f;

        // 标题已经由弹窗外壳（OpenBrightnessDialog）画了一遍，这里不用重复画。
        float imagesTop = 0f;
        float totalWidth = ImageSize * 2f + ImageGap;

        var refTex = Resources.Load<Texture2D>("UI/T_Settings_BrightnessRef");
        var brightShader = Shader.Find("UI/SkyPrison/Brightness");

        // 埃尔登法环那套做法：左右两张图共用同一块黑底面板（不是两个各自带边框的
        // 卡片），左边固定亮度（清楚可见，当"正确答案"参照），右边跟着滑块走、默认
        // 很暗，调到刚好能隐约看清图案就是合适的亮度。
        var panelRt = MakeRect("Panel", contentRt, new Vector2(0f, 1f), new Vector2(0f, 1f));
        panelRt.pivot = new Vector2(0f, 1f);
        panelRt.sizeDelta = new Vector2(totalWidth, ImageSize);
        panelRt.anchoredPosition = new Vector2(0f, imagesTop);
        panelRt.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.02f, 1f);

        // 左：固定亮度的参照图，不跟着滑块动
        var refRt = MakeRect("Reference", panelRt, new Vector2(0f, 0f), new Vector2(0f, 0f));
        refRt.pivot = new Vector2(0f, 0f);
        refRt.sizeDelta = new Vector2(ImageSize, ImageSize);
        refRt.anchoredPosition = Vector2.zero;
        var refImg = refRt.gameObject.AddComponent<RawImage>();
        refImg.texture = refTex;
        if (brightShader != null)
        {
            var refMat = new Material(brightShader);
            refMat.SetFloat("_Brightness", 1.6f); // 固定给足亮度，清楚可见的参照
            refImg.material = refMat;
        }

        // 右：实时预览，跟着滑块走，默认压得很暗（接近看不见）
        var previewRt = MakeRect("Preview", panelRt, new Vector2(1f, 0f), new Vector2(1f, 0f));
        previewRt.pivot = new Vector2(1f, 0f);
        previewRt.sizeDelta = new Vector2(ImageSize, ImageSize);
        previewRt.anchoredPosition = Vector2.zero;
        var previewImg = previewRt.gameObject.AddComponent<RawImage>();
        previewImg.texture = refTex;
        Material brightMat = null;
        if (brightShader != null)
        {
            brightMat = new Material(brightShader);
            previewImg.material = brightMat;
        }

        // 滑块紧贴在图下面，跟两张图加起来一样宽——视觉上是图的延伸，不是独立一行
        float sliderTop = imagesTop - ImageSize - 40f;
        var sliderRt = MakeRect("Slider", contentRt, new Vector2(0f, 1f), new Vector2(0f, 1f));
        sliderRt.pivot = new Vector2(0f, 1f);
        sliderRt.sizeDelta = new Vector2(totalWidth - 112f, TrackHeight);
        sliderRt.anchoredPosition = new Vector2(0f, sliderTop);
        var slider = sliderRt.gameObject.AddComponent<Slider>();
        slider.minValue = 0.4f;
        slider.maxValue = 2.5f;
        slider.transition = Selectable.Transition.None;
        _brightnessDialogSlider = slider; // Update() 里 Left/Right 无鼠标操作要用

        // 轨道细线只有 3px 高，之前没有别的图形覆盖整条轨道高度，鼠标要精准点在这条线上
        // 才能开始拖，稍微偏一点就点不到——加一个铺满整个 TrackHeight 的透明点击区。
        var clickAreaRt = MakeRect("ClickArea", sliderRt, Vector2.zero, Vector2.one);
        clickAreaRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

        var bgRt = MakeRect("Background", sliderRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
        bgRt.pivot = new Vector2(0.5f, 0.5f);
        bgRt.sizeDelta = new Vector2(0f, 6f);
        bgRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);

        var fillAreaRt = MakeRect("FillArea", sliderRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
        fillAreaRt.pivot = new Vector2(0.5f, 0.5f);
        fillAreaRt.sizeDelta = new Vector2(0f, 6f);
        var fillRt = MakeRect("Fill", fillAreaRt, Vector2.zero, Vector2.one);
        fillRt.gameObject.AddComponent<Image>().color = ColdGreen;
        slider.fillRect = fillRt;
        slider.targetGraphic = null;

        // 手柄：白色圆球，比轨道粗一圈；容器高度收紧到轨道高度，避免任何非等比拉伸，
        // 再加 preserveAspect 双保险，保证不管容器怎样都不会被拉成椭圆。
        var handleAreaRt = MakeRect("HandleArea", sliderRt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
        handleAreaRt.pivot = new Vector2(0.5f, 0.5f);
        handleAreaRt.sizeDelta = new Vector2(0f, TrackHeight);
        var handleRt = MakeRect("Handle", handleAreaRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.sizeDelta = new Vector2(28f, 28f);
        var handleImg = handleRt.gameObject.AddComponent<Image>();
        handleImg.sprite = SkyPrisonRoundedRectSprite.Get(24, 12);
        handleImg.type = Image.Type.Simple;
        handleImg.preserveAspect = true;
        handleImg.color = Color.white;
        slider.handleRect = handleRt;

        var valueTmp = AddTMP(contentRt, "Value", "1.00", 32f,
            TextAlignmentOptions.MidlineLeft, TextFaint, FontStyles.Normal);
        var valueRt = valueTmp.rectTransform;
        valueRt.anchorMin = valueRt.anchorMax = new Vector2(0f, 1f);
        valueRt.pivot = new Vector2(0f, 1f);
        valueRt.sizeDelta = new Vector2(96f, TrackHeight);
        valueRt.anchoredPosition = new Vector2(totalWidth - 112f + 24f, sliderTop);

        float initial = SaveManager.Settings != null ? SaveManager.Settings.brightness : 1f;
        slider.value = Mathf.Clamp(initial, slider.minValue, slider.maxValue);
        valueTmp.text = slider.value.ToString("F2");
        brightMat?.SetFloat("_Brightness", slider.value);

        slider.onValueChanged.AddListener(v =>
        {
            valueTmp.text = v.ToString("F2");
            brightMat?.SetFloat("_Brightness", v);
            if (SaveManager.Settings != null) SaveManager.Settings.brightness = v;
            SkyPrisonBrightnessManager.Apply(v); // 实时作用到游戏画面，不用等关窗口存盘

        });
    }

    private void SwitchTab(int index)
    {
        _activeTab = index;
        for (int i = 0; i < _tabContents.Count; i++)
            _tabContents[i].SetActive(i == index);

        for (int i = 0; i < _tabRowBgs.Count; i++)
        {
            bool active = i == index;
            _tabAccents[i].SetActive(active);

            // 用 OnEnterSilent——SE 已经由点击/悬停/键盘各自的入口统一播过了，
            // 这里再调用 OnEnter() 会跟着重复播一次 Switch 音效。
            if (active) _tabFx[i].OnEnterSilent();
            else        _tabFx[i].OnExit();
        }

        // 切 Tab 就把行光标收掉、焦点还给侧栏——上一个 Tab 里选中的行跟这个新 Tab
        // 的内容对不上号。
        if (_rowNav != null)
            foreach (var e in _rowNav)
            {
                if (e.cursor != null) e.cursor.SetActive(false);
                e.onBlur?.Invoke(); // 切 Tab 也算"离开这一行"，把待生效值提交掉
            }
        _rowNav = index >= 0 && index < _rowNavPerTab.Count ? _rowNavPerTab[index] : new List<RowNavEntry>();
        _rowCursor = 0;
        _focus = FocusArea.Sidebar;
    }

    private void Close()
    {
        // 兜底：不管玩家是靠鼠标移开、切 Tab 还是直接关窗口，所有"循环选项"行的
        // 待生效值都要在这里保证提交一遍（大部分情况已经在离开那一行时提交过了，
        // 这里重复调用是安全的——Commit() 内部会检查有没有真正变化）。
        for (int i = 0; i < _allCycleCommits.Count; i++)
            _allCycleCommits[i]?.Invoke();

        LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);
        SkyPrisonWindowHintBar.GetOrCreate().Clear(); // 提示条是共用的，关窗口要清掉，不然留着挡在屏幕下面
        // 之前这里从来没存过盘——改的所有值只停在内存里的 SaveManager.Settings，关窗口
        // 就白改了。这些行现在都是直接写 SaveManager.Settings 字段，关窗口时落一次盘。
        SaveManager.SaveSettings();
        SkyPrisonWindowManager_V1.ExternalBlock = _savedExternalBlock;
        Time.timeScale = _savedTimeScale;
        if (_capturedBlurRT != null) { _capturedBlurRT.Release(); Destroy(_capturedBlurRT); }
        _instance = null;
        _lastCloseFrame = Time.frameCount;
        Destroy(gameObject);
    }

    // 手柄方向键在 SkyPrisonInputSettings.asset 里没绑给 MoveUp/Down/Left/Right/Interact
    // （那几个动作的 gamepadKey 全是 None，只支持键盘），所以这几个窗口内手柄导航要用的
    // 键位在这里单独兜底，不改共享的输入资源（避免影响游戏内快捷物品等其它绑定）。
    // 这几个 JoystickButton 数值是照实际手柄按键图那份数据核对过的：12~15=方向键
    // 上/下/左/右，0=A（确认）。
    private const KeyCode GamepadDpadUp    = (KeyCode)342;
    private const KeyCode GamepadDpadDown  = (KeyCode)343;
    private const KeyCode GamepadDpadLeft  = (KeyCode)344;
    private const KeyCode GamepadDpadRight = (KeyCode)345;
    private const KeyCode GamepadConfirm   = KeyCode.JoystickButton0;

    // D-pad 在这个项目的 Input Manager 里是走"DPadVertical"/"DPadHorizontal"这两个
    // 虚拟轴（POV Hat），不是离散的 JoystickButton——上面那几个 JoystickButton 常量
    // 只能兜住极少数把 D-pad 虚拟成按钮的手柄/驱动，大多数情况下根本不会触发。
    // 摇杆同理是走 "Horizontal"/"Vertical"。跟 MainMenuController 用的是同一套边缘
    // 检测写法（轴值从中立区跨过阈值那一帧才算"按下一次"，不然摇杆推住不放会每帧都触发）。
    private const float NavAxisThreshold = 0.6f;
    private float _prevNavAxisX, _prevNavAxisY, _prevNavDpadX, _prevNavDpadY;

    private void ReadNavAxisEdges(out bool axisUp, out bool axisDown, out bool axisLeft, out bool axisRight)
    {
        float axisX = SafeAxis("Horizontal");
        float axisY = SafeAxis("Vertical");
        float dpadX = SafeAxis("DPadHorizontal");
        float dpadY = SafeAxis("DPadVertical");

        axisUp    = (axisY >  NavAxisThreshold && _prevNavAxisY <=  NavAxisThreshold) || (dpadY >  NavAxisThreshold && _prevNavDpadY <=  NavAxisThreshold);
        axisDown  = (axisY < -NavAxisThreshold && _prevNavAxisY >= -NavAxisThreshold) || (dpadY < -NavAxisThreshold && _prevNavDpadY >= -NavAxisThreshold);
        axisLeft  = (axisX < -NavAxisThreshold && _prevNavAxisX >= -NavAxisThreshold) || (dpadX < -NavAxisThreshold && _prevNavDpadX >= -NavAxisThreshold);
        axisRight = (axisX >  NavAxisThreshold && _prevNavAxisX <=  NavAxisThreshold) || (dpadX >  NavAxisThreshold && _prevNavDpadX <=  NavAxisThreshold);

        _prevNavAxisX = axisX; _prevNavAxisY = axisY;
        _prevNavDpadX = dpadX; _prevNavDpadY = dpadY;
    }

    private static float SafeAxis(string name)
    {
        try { return Input.GetAxisRaw(name); }
        catch { return 0f; } // 轴没在 Input Manager 里配置就当 0，别整个报错
    }

    private void Update()
    {
        // BuildRoutine 是协程，隔了好几个 yield 才会真正建出侧栏/Tab；这几帧里
        // Update() 已经在跑了，_tabButtons 还是空列表。这时候如果手柄残留着上一次的
        // 方向输入（比如开设置窗口前刚好按着 D-pad），会走到下面 %_tabButtons.Count
        // 那行，空列表模 0，直接抛 DivideByZeroException 崩游戏。窗口还没建完就先不响应。
        if (_tabButtons.Count == 0) return;

        // 整个设置窗口开着期间每帧都清一次——不止按键绑定弹窗，主界面任何一个 Tab 里
        // 手柄"确定"键都可能踩中这同一个坑：只要 EventSystem 选中对象还残留着背后窗口
        // （暂停菜单"继续游戏"之类）的按钮，Unity 自己的 Submit 就会绕过这个窗口的输入
        // 处理直接点它。见 BuildRoutine 里第一次清空时的详细注释。
        if (UnityEngine.EventSystems.EventSystem.current != null
            && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);

        bool escape = _inputSettings != null
            ? _inputSettings.GetActionDown(SkyPrisonInputAction.Menu)
            : Input.GetKeyDown(KeyCode.Escape);
        bool gamepadB = Input.GetKeyDown(KeyCode.JoystickButton1);

        // 亮度弹窗开着时是另一套输入逻辑——Esc/B 当"取消"用（退回原值再关），Interact
        // 当"确认"用，Left/Right 直接调滑块。不用再切"选中哪个按钮"，两个按钮各自
        // 绑一个固定按键，鼠标手柄键盘都能用。
        if (_brightnessDialogRoot != null)
        {
            if (escape || gamepadB)
            {
                if (SaveManager.Settings != null) SaveManager.Settings.brightness = _brightnessDialogInitialValue;
                SkyPrisonBrightnessManager.Apply(_brightnessDialogInitialValue);
                CloseBrightnessDialog();
                return;
            }

            bool dialogConfirm = (_inputSettings != null && _inputSettings.GetActionDown(SkyPrisonInputAction.Interact))
                || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(GamepadConfirm);
            if (dialogConfirm) { CloseBrightnessDialog(); return; }

            if (_brightnessDialogSlider != null)
            {
                float stickX = SafeAxis("Horizontal") + SafeAxis("DPadHorizontal");
                bool dLeft  = (_inputSettings != null && _inputSettings.GetAction(SkyPrisonInputAction.MoveLeft))
                    || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(GamepadDpadLeft) || stickX < -NavAxisThreshold;
                bool dRight = (_inputSettings != null && _inputSettings.GetAction(SkyPrisonInputAction.MoveRight))
                    || Input.GetKey(KeyCode.RightArrow) || Input.GetKey(GamepadDpadRight) || stickX > NavAxisThreshold;
                if (dLeft)  _brightnessDialogSlider.value -= Time.unscaledDeltaTime * 1.2f;
                if (dRight) _brightnessDialogSlider.value += Time.unscaledDeltaTime * 1.2f;
            }
            return;
        }

        // 按键绑定弹窗开着时：捕获中就轮询按键，没在捕获就是方向键移动光标 + 确认键
        // 开始捕获 + Esc/B 关闭弹窗（不保存）。
        if (_keybindDialogRoot != null)
        {
            // EventSystem 选中对象已经在 Update() 顶部每帧清空过了（整个设置窗口通用），
            // 这里不用重复处理，只留 Navigation.Mode=None（见 BuildKeybindSlot）防止
            // Unity 自动导航在弹窗内部的 Selectable 之间瞎跳。

            if (_keybindCapturingSlot != null)
            {
                PollKeybindCapture();
                return;
            }

            if (escape || gamepadB) { CloseKeybindDialog(save: false); return; }

            ReadNavAxisEdges(out bool kbUp, out bool kbDown, out bool kbLeft, out bool kbRight);
            bool navUp    = Input.GetKeyDown(KeyCode.UpArrow)   || Input.GetKeyDown(GamepadDpadUp)   || kbUp;
            bool navDown  = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(GamepadDpadDown) || kbDown;
            bool navLeft  = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(GamepadDpadLeft) || kbLeft;
            bool navRight = Input.GetKeyDown(KeyCode.RightArrow)|| Input.GetKeyDown(GamepadDpadRight)|| kbRight;

            // 某些手柄的摇杆/D-pad 在同一帧会同时报上下（或左右）两个方向（HAT 噪声/
            // 驱动实现差异）——先上跳一格再立刻下跳回来，光标净移动量是 0 但两次移动
            // 都是"真的移动"（离开原位再回来），所以两次都会通过 MoveKeybindCursor 的
            // 边界检查、各自播一次音效，听起来就像是"卡在边界上还一直响"。跟设置窗口
            // 主内容区导航同一个坑，改成同一帧互斥，只认一个方向。
            if (navUp && navDown)    { navUp = navDown = false; }
            if (navLeft && navRight) { navLeft = navRight = false; }

            if (navUp || navDown)
            {
                if (MoveKeybindCursor(_keybindCursorRow + (navDown ? 1 : -1), _keybindCursorIsSecondary))
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            }
            else if (navLeft || navRight)
            {
                if (MoveKeybindCursor(_keybindCursorRow, navRight))
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            }

            if (Input.GetKeyDown(GamepadConfirm))
            {
                var cursorRowRef  = _keybindCursorRow >= 0 && _keybindCursorRow < _keybindRowRefs.Count
                    ? _keybindRowRefs[_keybindCursorRow] : null;
                var cursorSlotRef = GetKeybindSlot(_keybindCursorRow, _keybindCursorIsSecondary);
                if (cursorRowRef?.binding != null && cursorSlotRef != null)
                {
                    var keybindLocTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
                    string KeybindL(string key, string fallback) => keybindLocTable != null ? keybindLocTable.Get(key, fallback) : fallback;
                    BeginKeybindCapture(cursorRowRef.binding, _keybindCursorIsSecondary, cursorSlotRef, KeybindL);
                }
            }
            return;
        }

        // 手柄按键绑定弹窗：单列，逻辑是键鼠那份的简化版（没有左右切列）。
        if (_gamepadKeybindDialogRoot != null)
        {
            if (_gamepadCapturingSlot != null)
            {
                PollGamepadKeybindCapture();
                return;
            }

            if (escape || gamepadB) { CloseGamepadKeybindDialog(save: false); return; }

            ReadNavAxisEdges(out bool gpUp, out bool gpDown, out bool _, out bool __);
            bool navUp2   = Input.GetKeyDown(KeyCode.UpArrow)   || Input.GetKeyDown(GamepadDpadUp)   || gpUp;
            bool navDown2 = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(GamepadDpadDown) || gpDown;
            if (navUp2 && navDown2) { navUp2 = navDown2 = false; }

            if (navUp2 || navDown2)
            {
                if (MoveGamepadKeybindCursor(_gamepadCursorRow + (navDown2 ? 1 : -1)))
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            }

            if (Input.GetKeyDown(GamepadConfirm))
            {
                var cursorRowRef2 = _gamepadCursorRow >= 0 && _gamepadCursorRow < _gamepadKeybindRowRefs.Count
                    ? _gamepadKeybindRowRefs[_gamepadCursorRow] : null;
                var cursorSlotRef2 = GetGamepadKeybindSlot(_gamepadCursorRow);
                if (cursorRowRef2?.binding != null && cursorSlotRef2 != null)
                {
                    var gpLocTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
                    string GpL(string key, string fallback) => gpLocTable != null ? gpLocTable.Get(key, fallback) : fallback;
                    BeginGamepadKeybindCapture(cursorRowRef2.binding, cursorSlotRef2, GpL);
                }
            }
            return;
        }

        // 清除缓存确认弹窗开着时，跟亮度弹窗一样先吃掉所有导航输入——不然背后的
        // 手柄图列表还能继续响应方向键/切分类，弹窗形同虚设。
        if (_clearCacheConfirmOverlay != null)
        {
            if (escape || gamepadB)
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);
                Destroy(_clearCacheConfirmOverlay);
                _clearCacheConfirmOverlay = null;
            }
            return;
        }

        // 焦点在内容区（行光标）时，返回键先退回侧栏，不直接关掉整个设置窗口——
        // 跟 Left 退回侧栏是同一个"先回到上一级，再退出"的逻辑，只是换了个按键触发。
        if ((escape || gamepadB) && _focus == FocusArea.Content)
        {
            SetRowCursorVisible(_rowCursor, false);
            _focus = FocusArea.Sidebar;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            return;
        }

        if (escape || gamepadB) { Close(); return; }

        ReadNavAxisEdges(out bool axisUp, out bool axisDown, out bool axisLeft, out bool axisRight);

        bool up    = (_inputSettings != null && _inputSettings.GetActionDown(SkyPrisonInputAction.MoveUp))
            || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(GamepadDpadUp) || axisUp;
        bool down  = (_inputSettings != null && _inputSettings.GetActionDown(SkyPrisonInputAction.MoveDown))
            || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(GamepadDpadDown) || axisDown;
        bool left  = (_inputSettings != null && _inputSettings.GetActionDown(SkyPrisonInputAction.MoveLeft))
            || Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(GamepadDpadLeft) || axisLeft;
        bool right = (_inputSettings != null && _inputSettings.GetActionDown(SkyPrisonInputAction.MoveRight))
            || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(GamepadDpadRight) || axisRight;
        bool confirm = (_inputSettings != null && _inputSettings.GetActionDown(SkyPrisonInputAction.Interact))
            || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(GamepadConfirm);

        // 某些手柄的 D-pad 在同一帧会同时报上下（或左右）两个方向的按下事件（HAT 噪声/
        // 驱动实现差异），之前 up/down 各自一个独立 if，两个都触发时会先切一格、马上又
        // 切回来，声音响了但 Tab/光标净移动量是 0——表现为"听到声音、位置却没变"。
        // 改成互斥：同一帧只认一个方向。
        if (up && down)     { up = down = false; }
        if (left && right)  { left = right = false; }

        if (_focus == FocusArea.Sidebar)
        {
            // 竖排目录，用上下切换（不是横排那套左右）——符合参考的主流游戏设置界面操作习惯。
            if (up)   { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); SwitchTab((_activeTab - 1 + _tabButtons.Count) % _tabButtons.Count); }
            if (down) { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); SwitchTab((_activeTab + 1) % _tabButtons.Count); }

            // 不用鼠标也能进内容区操作每一行——Right 或 Interact 都行。
            if ((right || confirm) && _rowNav.Count > 0)
            {
                _focus = FocusArea.Content;
                _rowCursor = Mathf.Clamp(_rowCursor, 0, _rowNav.Count - 1);
                SetRowCursorVisible(_rowCursor, true);
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            }
            return;
        }

        // ── 内容区：Up/Down 移动行光标，Left/Right 调值，Interact 触发。────────
        if (_rowNav.Count == 0) { _focus = FocusArea.Sidebar; return; }

        if (up || down)
        {
            SetRowCursorVisible(_rowCursor, false);
            _rowNav[_rowCursor].onBlur?.Invoke(); // 光标要挪走了，把这一行的待生效值提交掉
            _rowCursor = ((_rowCursor + (down ? 1 : -1)) % _rowNav.Count + _rowNav.Count) % _rowNav.Count;
            SetRowCursorVisible(_rowCursor, true);
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        }

        var current = _rowNav[_rowCursor];

        if (right && current.onHorizontal != null)
        {
            current.onHorizontal(1);
        }
        else if (left)
        {
            if (current.onHorizontal != null) current.onHorizontal(-1);
            else
            {
                // 这一行没有左右可调的值（开关/链接那种），Left 就当"退回侧栏"用。
                SetRowCursorVisible(_rowCursor, false);
                current.onBlur?.Invoke();
                _focus = FocusArea.Sidebar;
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            }
        }

        if (confirm) current.onConfirm?.Invoke();
    }

    private void SetRowCursorVisible(int index, bool visible)
    {
        if (index < 0 || index >= _rowNav.Count) return;
        var cursor = _rowNav[index].cursor;
        if (cursor != null) cursor.SetActive(visible);
    }

    private void OnDestroy()
    {
        LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
        if (_capturedBlurRT != null) { _capturedBlurRT.Release(); Destroy(_capturedBlurRT); }
    }

    // ── UI 工具（与 SaveSlotSelectorUI / PauseMenuController 一致的规范实现）──

    private static RenderTexture CaptureAndBlurScreen()
    {
        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        int w = Mathf.Max(4, shot.width);
        int h = Mathf.Max(4, shot.height);

        var full = new RenderTexture(w, h, 0, RenderTextureFormat.DefaultHDR) { hideFlags = HideFlags.HideAndDontSave };
        full.Create();
        Graphics.Blit(shot, full);
        Destroy(shot);

        const int SrcLongEdge = 960;
        float aspect = (float)w / h;
        int baseW, baseH;
        if (aspect >= 1f) { baseW = SrcLongEdge; baseH = Mathf.Max(4, Mathf.RoundToInt(SrcLongEdge / aspect)); }
        else              { baseH = SrcLongEdge; baseW = Mathf.Max(4, Mathf.RoundToInt(SrcLongEdge * aspect)); }

        var temps = new List<RenderTexture>();
        var baseRT = RenderTexture.GetTemporary(baseW, baseH, 0, RenderTextureFormat.DefaultHDR);
        baseRT.filterMode = FilterMode.Bilinear;
        Graphics.Blit(full, baseRT);
        temps.Add(baseRT);

        // 按"选择存档"那份的糊度来——降到 960 基准后再腰斩 6 次（960→15px），
        // 一步放大回全屏，不走逐级放大链。跟 PauseMenuController 那套"糊得弱一些"
        // 的调法不是同一个目标观感，这里直接照抄 SaveSlotSelectorUI 的参数。
        const int BlurHalfSteps = 6;
        RenderTexture src = baseRT;
        int curW = baseW, curH = baseH;
        for (int i = 0; i < BlurHalfSteps; i++)
        {
            curW = Mathf.Max(4, curW / 2);
            curH = Mathf.Max(4, curH / 2);
            var down = RenderTexture.GetTemporary(curW, curH, 0, RenderTextureFormat.DefaultHDR);
            down.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, down);
            temps.Add(down);
            src = down;
        }

        var result = new RenderTexture(w, h, 0, RenderTextureFormat.DefaultHDR) { hideFlags = HideFlags.HideAndDontSave };
        result.filterMode = FilterMode.Bilinear;
        result.Create();
        Graphics.Blit(src, result);

        foreach (var t in temps) RenderTexture.ReleaseTemporary(t);
        full.Release();
        Destroy(full);

        return result;
    }

    private static RectTransform MakeRect(string name, Transform parent, Vector2 amin, Vector2 amax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = amin; rt.anchorMax = amax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static TMP_Text AddTMP(RectTransform parent, string name, string text, float size,
        TextAlignmentOptions align, Color color, FontStyles style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = color;
        tmp.alignment = align; tmp.fontStyle = style;
        return tmp;
    }

    private static Image[] AddOutline(RectTransform rt, Color c, float px)
    {
        return new[]
        {
            AddLineRT(rt, "OT", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, px), c),
            AddLineRT(rt, "OB", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, px), c),
            AddLineRT(rt, "OL", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(px, 0f), c),
            AddLineRT(rt, "OR", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(px, 0f), c),
        };
    }

    // 设置行底色用的横向渐变 Sprite：中间实、两端淡出（alpha 0→1→0），缓存一份复用。
    private static Sprite _rowGradientSprite;

    private static Sprite GetRowGradientSprite()
    {
        if (_rowGradientSprite != null) return _rowGradientSprite;

        const int w = 128, h = 4;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        var pixels = new Color[w * h];
        for (int x = 0; x < w; x++)
        {
            float t = x / (float)(w - 1);
            // 两端 18% 区间做淡入淡出，中间 64% 保持满alpha
            float fadeIn  = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.18f, t));
            float fadeOut = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.82f, t));
            float alpha = Mathf.Min(fadeIn, fadeOut);
            for (int y = 0; y < h; y++)
                pixels[y * w + x] = new Color(1f, 1f, 1f, alpha);
        }

        tex.SetPixels(pixels);
        tex.Apply();

        _rowGradientSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
            100f, 0u, SpriteMeshType.FullRect, Vector4.zero);
        return _rowGradientSprite;
    }

    private static void AddCornerBrackets(RectTransform panel, Color c, float len, float thick)
    {
        Vector2[] corners = { Vector2.zero, new Vector2(1, 0), new Vector2(0, 1), Vector2.one };
        foreach (var corner in corners)
        {
            var hRT = MakeRect("CB_H", panel, corner, corner);
            hRT.pivot = corner; hRT.sizeDelta = new Vector2(len, thick); hRT.anchoredPosition = Vector2.zero;
            hRT.gameObject.AddComponent<Image>().color = c;

            var vRT = MakeRect("CB_V", panel, corner, corner);
            vRT.pivot = corner; vRT.sizeDelta = new Vector2(thick, len); vRT.anchoredPosition = Vector2.zero;
            vRT.gameObject.AddComponent<Image>().color = c;
        }
    }

    private static Image AddLineRT(RectTransform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
        return img;
    }

    private static void AddLine(RectTransform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
        => AddLineRT(parent, name, anchorMin, anchorMax, pivot, pos, size, c);

    private static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
    {
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private void TryBindFonts(Transform root)
    {
        if (_font == null) return;
        foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            t.font = _font;
    }

    private static Sprite LoadSpriteByGuid(string guid)
    {
#if UNITY_EDITOR
        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(path))
        {
            var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
        }
#endif
        // Build 里没有 AssetDatabase，GUID 精确加载这条路在 Build 里从来没生效过——
        // 之前 #if UNITY_EDITOR 外面直接 return null，缺了 Resources 兜底，关闭按钮
        // 在 Build 里永远拿不到真实图标，只能退回文字兜底（方框+×）。跟这个项目
        // 已经踩过好几次的"图标/字体资源没有 Resources 兜底"是同一类坑。
        if (guid == "1a860d9de75042546ba9c69ed9e23434")
            return Resources.Load<Sprite>("UI/UIWindow_Default_Close");
        return null;
    }

    private static TMP_FontAsset LoadTMPFont(string assetName)
    {
#if UNITY_EDITOR
        string path = $"Assets/_Project/UIUX/Fonts/TMP/{assetName}.asset";
        var fa = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (fa != null) return fa;
        string[] guids = UnityEditor.AssetDatabase.FindAssets(assetName + " t:TMP_FontAsset");
        if (guids.Length > 0)
        {
            fa = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
            if (fa != null) return fa;
        }
#endif
        return Resources.Load<TMP_FontAsset>($"Fonts & Materials/{assetName}");
    }
}
