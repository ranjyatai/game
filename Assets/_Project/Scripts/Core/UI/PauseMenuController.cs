using System.Collections;
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 关卡内 ESC 暂停菜单：继续游戏 / 设置 / 返回主菜单。
/// 与背包等悬浮窗口不同——暂停菜单真的会冻结游戏时间（Time.timeScale=0），
/// 打开期间背包/移动等玩法系统一律不可操作。
/// 视觉风格照搬 SaveSlotSelectorUI：磨砂背景 + 冷绿细边框 + 透明按钮。
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    private static PauseMenuController _instance;
    public static bool IsOpen { get; private set; }

    // Esc 同一次按键：PauseMenuController.Update()（默认执行顺序）先跑，关掉菜单、
    // IsOpen 已经变 false；SkyPrisonPlayerInputRouter.Update()（执行顺序 8500，排在后面）
    // 这一帧还读得到同一次 GetActionDown(Menu)=true，看 IsOpen 已经是 false 就又把菜单
    // 重新打开——同一帧内"关了又开"，表现为按 Esc 根本关不掉、一直在响应打开。
    // 记录关闭发生的帧号，Show() 里拒绝在同一帧内重新打开，从源头掐掉这条竞态。
    private static int _lastCloseFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStaticState()
    {
        IsOpen = false;
        _instance = null;
        _lastCloseFrame = -1;
        SkyPrisonWindowManager_V1.ExternalBlock = false;
    }

    public static void Toggle()
    {
        if (IsOpen) Resume();
        else Show();
    }

    public static void Show()
    {
        if (IsOpen || Time.frameCount == _lastCloseFrame) return;
        IsOpen = true;

        var go = new GameObject("[PauseMenu]");
        var ui = go.AddComponent<PauseMenuController>();
        _instance = ui;
        ui.StartCoroutine(ui.BuildRoutine());
    }

    public static void Resume()
    {
        if (!IsOpen || _instance == null) return;
        _instance.BeginClose();
    }

    // ── 视觉常量（与 SaveSlotSelectorUI 一致）───────────────────────────────
    private static readonly Color ColdGreen    = new Color(0.42f, 0.92f, 0.68f, 1f);
    private static readonly Color TextLight    = new Color(0.88f, 0.88f, 0.90f, 1f);
    private static readonly Color TextFaint    = new Color(0.55f, 0.62f, 0.65f, 1f);
    private static readonly Color PanelOverlay = new Color(0.03f, 0.04f, 0.05f, 0.32f);

    private TMP_FontAsset _font;
    private RenderTexture _capturedBlurRT;

    private readonly List<Button> _buttons = new();
    private readonly List<Image[]> _buttonOutlines = new();
    private int _cursorIndex;
    private float _navCooldown;
    private const float NavDelay  = 0.28f;
    private const float NavRepeat = 0.14f;

    // MoveUp/MoveDown 在 SkyPrisonInputSettings.asset 里 gamepadKey 是 None（这两个
    // action 本来就是给键盘用的），GetActionDown/GetAction 对手柄输入永远返回 false——
    // 这个菜单的手柄光标移动之前完全靠这两个 action，所以手柄插了也一直不会动。
    // 照 SettingsWindowUI 已经验证过能用的方案，直接读原始 D-pad/摇杆轴当兜底，
    // 不改共享的输入资源。这个项目的 D-pad 走 "DPadVertical" 虚拟轴，不是按钮。
    private const float NavAxisThreshold = 0.6f;
    private float _prevNavAxisY, _prevNavDpadY;

    private static float SafeAxis(string name)
    {
        try { return Input.GetAxisRaw(name); }
        catch { return 0f; }
    }

    private SkyPrisonInputSettings _inputSettings;
    private GameObject _returnConfirmOverlay;
    private RectTransform _panelRt; // 主菜单三个按钮所在的面板，弹二次确认框时整体隐藏，避免文字透出来
    private Material _desaturateMaterial; // 弹窗背景用：复用同一张模糊截屏，转灰度
    private SkyPrisonInputPromptIconDatabase _iconDb;
    private TMP_Text _titleText;
    private TMP_Text _titleCursorText;

    private float _savedTimeScale = 1f;
    private bool _savedCursorVisible;
    private CursorLockMode _savedCursorLockState;

    // 暂停时隐藏的所有玩法 canvas（血条/快捷栏/状态图标等各自独立的系统）。
    // 不逐个找具体系统，而是通用遮盖所有根 ScreenSpaceOverlay Canvas——
    // 跟 SceneLoader.HideGameCanvases 的兜底思路一致，以后新增的悬浮系统不用再补。
    private readonly List<(CanvasGroup cg, float origAlpha, bool origInteractable, bool origBlocksRaycasts)> _hiddenGameCanvases = new();

    // 战斗 HUD / 状态图标条这些各自独立系统——CanvasGroup.alpha 这条路子屡次被它们自己
    // 的逻辑（背包开关的淡入淡出协程、战斗可见度自动淡出、状态图标自己的刷新等）抢着
    // 写同一个字段，快速开关背包/刚进图立刻按 Esc 时经常抢不过。直接 SetActive(false)
    // 整个根节点是唯一打不过的办法——被禁用的 GameObject 不会被渲染、Update/协程也
    // 不会跑，没有谁能在这期间"抢"回可见性。
    private readonly List<(GameObject go, bool wasActive)> _forceDisabledRoots = new();

    private void ForceDisable(GameObject go)
    {
        if (go == null) return;
        _forceDisabledRoots.Add((go, go.activeSelf));
        go.SetActive(false);
    }

    private IEnumerator BuildRoutine()
    {
        DontDestroyOnLoad(gameObject);

        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);

        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        // 暂停菜单是纯代码搭的，没走 SkyPrisonWindowManager_V1.Open()/Close() 那条 prefab
        // 流程，不设这个标记的话，攻击/闪避/跳跃等判断读到的 AnyBlockingWindowOpen 还是
        // false——之前就出现过"点继续游戏，角色同时打出一次轻攻击"的输入穿透。
        SkyPrisonWindowManager_V1.ExternalBlock = true;

        // 游戏内平时是锁定/隐藏鼠标的（角色朝向/瞄准用），暂停菜单打开时必须解锁+显示，
        // 不然玩家看不到、也点不到按钮——之前漏了这一步，"继续游戏"点了没反应。
        _savedCursorVisible   = Cursor.visible;
        _savedCursorLockState = Cursor.lockState;
        Cursor.visible   = true;
        Cursor.lockState = CursorLockMode.None;
        PlayerAimFacingController.Instance?.PushWindowCursor();

        HideAllGameCanvases();

        var uiDriver = FindObjectOfType<SkyPrisonRuntimeUIDriver>();
        ForceDisable(uiDriver != null ? uiDriver.HudInstance : null);

        // 左下角状态图标条是自己独立的一套 DontDestroyOnLoad Canvas（不挂在战斗 HUD
        // 根节点下面），刚进图立刻按 Esc 时经常还没被上面的通用扫描追上，单独按名字
        // 找出来一起强制禁用。
        ForceDisable(GameObject.Find("[PlayerHUDStatusBar]"));

        // ScreenCapture 系列 API 必须在这一帧渲染完成之后调用才能稳定拿到完整画面，
        // 之前紧跟着 HideAllGameCanvases() 同帧调用，读到的是渲染中途/上一帧的画面，
        // 在某些 Game 视图缩放（比如这次报的 4K UHD + 0.54x）下会整体截不到、只剩淡淡叠加。
        yield return new WaitForEndOfFrame();

        _capturedBlurRT = CaptureAndBlurScreen();
        EnsureDesaturateMaterial();
        _font = LoadTMPFont("ZhouFangRiMingTi-2 SDF");
        _inputSettings = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");
        LoadIconDatabase();

        yield return null;

        LocalizationRuntime.OnLanguageChanged += OnLanguageChanged;
        RebuildUI();
    }

    // 之前语言切换这个菜单不会跟着刷新，得关掉重开才看得到新语言。整个 UI 内容都在
    // 一个可丢弃的 Canvas 子物体下，语言变化时直接整个销毁重建，比追踪每个 Text
    // 组件简单可靠——这个菜单本来就是"打开时全量构建一次"的模式，重建开销可忽略。
    private GameObject _canvasGo;

    private void OnLanguageChanged(string _) => RebuildUI();

    private void RebuildUI()
    {
        if (_canvasGo != null) Destroy(_canvasGo);
        _buttons.Clear();
        _buttonOutlines.Clear();

        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        string L(string key, string fallback) => locTable != null ? locTable.Get(key, fallback) : fallback;

        var canvasGo = new GameObject("Canvas");
        _canvasGo = canvasGo;
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        // Canvas.sortingOrder 内部按 16 位有符号整数存（范围 -32768~32767），超过 32767
        // 会直接绕成负数（比如 33000 会变成 -32536）——之前用的 33000 已经溢出了，只是因为
        // 暂停菜单会把其他画布强制隐藏（alpha=0/SetActive(false)）没人跟它抢排序才没暴露。
        // 换成压过快捷栏提示条（32000）但仍在合法范围内的数值。
        canvas.sortingOrder = 32100;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(3840f, 2160f);
        canvasGo.AddComponent<GraphicRaycaster>();

        var rootRt = canvasGo.GetComponent<RectTransform>();

        // 背景：捕获到的模糊截屏，走灰度+冷绿双色调（"系统暂停/时间冻结"的终端质感，
        // 不用纯黑白——跟标题的 PAUSE / SYSTEM HOLD 终端文案、项目里到处在用的冷绿
        // 强调色是同一套视觉语言）+ 暗色叠加。
        if (_capturedBlurRT != null)
        {
            var bgRt = MakeRect("BlurBg", rootRt, Vector2.zero, Vector2.one);
            var bgImg = bgRt.gameObject.AddComponent<RawImage>();
            bgImg.texture = _capturedBlurRT;
            if (_desaturateMaterial != null) bgImg.material = _desaturateMaterial;
            bgImg.raycastTarget = false;
        }
        var overlayRt = MakeRect("Overlay", rootRt, Vector2.zero, Vector2.one);
        overlayRt.gameObject.AddComponent<Image>().color = PanelOverlay;

        // 面板：只做布局容器，不画框也不填充底色，直接露出后面的模糊背景
        var panelRt = MakeRect("Panel", rootRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        panelRt.sizeDelta = new Vector2(840f, 680f);
        _panelRt = panelRt;

        // 面板本身先透明——先让背景的径向扭曲从夸张值收敛到稳定值演一遍"系统校准"的
        // 过渡效果，扭曲稳定下来之后菜单面板（标题+按钮）再淡入，不是开场直接糊一脸。
        var panelCg = panelRt.gameObject.AddComponent<CanvasGroup>();
        panelCg.alpha = 0f;

        var titleRt = MakeRect("Title", panelRt, new Vector2(0f, 1f), new Vector2(1f, 1f));
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -56f);
        titleRt.sizeDelta = new Vector2(0f, 80f);

        // 主文案和闪烁光标拆成两个独立对象，用 HorizontalLayoutGroup 横排在一起——
        // 之前不管是换字符还是切同一个字符的 alpha，只要还在同一段字符串里，
        // TMP 居中对齐时都可能因为测量/富文本解析的细微差异重新计算整体宽度，
        // 导致前面的文字跟着挪位置。拆成两个对象后，主文案的宽度是完全独立、
        // 不受光标可见性影响的常量，光标那个对象自己隐现，前面文字物理上不可能被牵动。
        var titleGroupGo = new GameObject("TitleGroup", typeof(RectTransform));
        titleGroupGo.transform.SetParent(titleRt, false);
        var titleGroupRt = (RectTransform)titleGroupGo.transform;
        titleGroupRt.anchorMin = titleGroupRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleGroupRt.pivot = new Vector2(0.5f, 0.5f);
        var titleLayout = titleGroupGo.AddComponent<HorizontalLayoutGroup>();
        titleLayout.childAlignment = TextAnchor.MiddleCenter;
        titleLayout.spacing = 0f;
        titleLayout.childControlWidth = true;
        titleLayout.childControlHeight = true;
        titleLayout.childForceExpandWidth = false;
        titleLayout.childForceExpandHeight = false;
        var titleFitter = titleGroupGo.AddComponent<ContentSizeFitter>();
        titleFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        titleFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // "PAUSE / SYSTEM HOLD" 是刻意做成系统终端风格的固定文案，跟 "NO DATA"、
        // "STREAMING AUDIO PACKAGES..." 那类系统提示一样，不随语言切换翻译——
        // 中/日/英玩家看到的都是同一行英文系统字符，这是这套"终端感"视觉语言本身
        // 的一部分。
        _titleText = AddTMP(titleGroupRt, "Label", "PAUSE / SYSTEM HOLD", 48f,
            TextAlignmentOptions.Center, ColdGreen, FontStyles.Bold);
        _titleCursorText = AddTMP(titleGroupRt, "Cursor", "_", 48f,
            TextAlignmentOptions.Center, ColdGreen, FontStyles.Bold);
        StartCoroutine(BlinkTitleCursor());

        // 标题下方一条细横线 + 两侧各一个小方块——终端系统标题栏常见的装饰写法
        // （参考"MAIN SYSTEM / COMBAT MODE ACTIVE"那类 HUD 标题）。横线比文字本身
        // 宽一截，不是紧贴着文字宽度。
        BuildTitleUnderline(titleRt);

        BuildMenuButton(panelRt, 0, L("ui_pause_resume", "继续游戏"), Resume);
        BuildMenuButton(panelRt, 1, L("ui_menu_settings", "游戏设置"), SettingsWindowUI.Show);
        // 安全区域（基地）没有"章节会话"（activeSession 为空）——返回主菜单不会丢失
        // 任何进度，跟"继续游戏"一样无风险，不需要弹二次确认；只有真的在关卡里
        // （activeSession 非空）才需要警告"战斗进度会丢失"。
        bool inChapter = SaveManager.Player?.activeSession != null;
        BuildMenuButton(panelRt, 2, L("ui_pause_return_menu", "返回主菜单"),
            inChapter ? () => ShowReturnMenuConfirm(rootRt, L) : OnReturnToMainMenu);

        BuildHintBar(rootRt, L);

        TryBindFonts(rootRt);
        RefreshCursorVisuals();

        StartCoroutine(PauseIntroTransition(panelCg));
    }

    private const float DistortionSteadyStrength = 0.075f;
    private const float DistortionIntroDuration  = 0.5f;
    private const float PanelFadeInDuration      = 0.25f;

    // 开场先是没有任何扭曲的"实景"（真实截屏原样），扭曲从 0 逐渐长到目标强度——
    // 感觉是"真实画面正在被转换成系统屏幕"，不是反过来从夸张扭曲收敛回正常。
    // 扭曲长到位之后菜单面板才淡入，两段是先后关系，不是同时进行。全程用不缩放
    // 时间，暂停期间 Time.timeScale=0 不会卡住这段动画。
    private IEnumerator PauseIntroTransition(CanvasGroup panelCg)
    {
        float t = 0f;
        while (t < DistortionIntroDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / DistortionIntroDuration);
            float k = 1f - (1f - p) * (1f - p); // ease-out：一开始长得快，后段慢慢稳定下来
            if (_desaturateMaterial != null)
                _desaturateMaterial.SetFloat("_DistortionStrength", Mathf.Lerp(0f, DistortionSteadyStrength, k));
            yield return null;
        }
        if (_desaturateMaterial != null)
            _desaturateMaterial.SetFloat("_DistortionStrength", DistortionSteadyStrength);

        float ft = 0f;
        while (ft < PanelFadeInDuration)
        {
            ft += Time.unscaledDeltaTime;
            if (panelCg == null) yield break;
            panelCg.alpha = Mathf.Clamp01(ft / PanelFadeInDuration);
            yield return null;
        }
        if (panelCg != null) panelCg.alpha = 1f;
    }

    /// <summary>灰度+冷绿双色调材质，暂停背景和返回确认框背景共用同一份，保证观感统一。</summary>
    private void EnsureDesaturateMaterial()
    {
        if (_desaturateMaterial != null) return;
        var desatShader = Shader.Find("UI/SkyPrison/Desaturate");
        if (desatShader == null) return;

        _desaturateMaterial = new Material(desatShader) { hideFlags = HideFlags.HideAndDontSave };
        _desaturateMaterial.SetColor("_DuotoneColor", ColdGreen);
        _desaturateMaterial.SetFloat("_DuotoneStrength", 0.6f);
        _desaturateMaterial.SetFloat("_ScanlineCount", 240f);
        _desaturateMaterial.SetFloat("_ScanlineStrength", 0.25f);
        // 持续存在、幅度很小的整屏左右抖动——不是偶尔冒一下的故障块。
        _desaturateMaterial.SetFloat("_JitterStrength", 0.0025f);
        _desaturateMaterial.SetFloat("_JitterSpeed", 6f);
        // 径向扭曲："系统屏幕化"效果——四边中点夹得最紧。这里先设成 0（完全是未经
        // 处理的实景截屏），PauseIntroTransition 会在打开的瞬间把它从 0 长到
        // DistortionSteadyStrength，做出"真实画面被转换成系统屏幕"的开场演出。
        _desaturateMaterial.SetFloat("_DistortionStrength", 0f);
        _desaturateMaterial.SetFloat("_DistortionPulseSpeed", 0.5f);
    }

    /// <summary>
    /// 跟 LoadingScreenUI / SaveSlotSelectorUI 完全一样的加载方式：优先用游戏内已经
    /// 填充好的 RuntimeDatabase，否则编辑器里直接按已知资产路径加载数据库资产本身。
    /// </summary>
    private void LoadIconDatabase()
    {
        _iconDb = SkyPrisonQuickItemPromptStrip.RuntimeDatabase
               ?? Resources.Load<SkyPrisonInputPromptIconDatabase>("InputPromptIconDatabase");
    }

    private void BuildMenuButton(RectTransform panel, int index, string label, System.Action onClick)
    {
        const float top = 192f;
        const float step = 112f;
        var rt = MakeRect($"Btn_{index}", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(600f, 88f);
        rt.anchoredPosition = new Vector2(0f, -(top + step * index));

        rt.gameObject.AddComponent<Image>().color = Color.clear;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() =>
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
            onClick?.Invoke();
        });
        SkyPrisonUIButtonFeedback.Attach(rt.gameObject);

        // 鼠标悬停时把光标同步过来，跟键盘/手柄共用同一套选中态，
        // 不会出现"鼠标悬停一个按钮变绿、键盘光标停在另一个按钮"的双重指示。
        int capturedIndex = index;
        var trigger = rt.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var pointerEnter = new UnityEngine.EventSystems.EventTrigger.Entry
        {
            eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter
        };
        pointerEnter.callback.AddListener(_ =>
        {
            if (_cursorIndex == capturedIndex) return;
            _cursorIndex = capturedIndex;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            RefreshCursorVisuals();
        });
        trigger.triggers.Add(pointerEnter);

        var outline = AddOutline(rt, new Color(1f, 1f, 1f, 0.35f), 3f);
        AddTMP(rt, "Label", label, 36f, TextAlignmentOptions.Center, TextLight, FontStyles.Normal)
            .raycastTarget = false;

        _buttons.Add(btn);
        _buttonOutlines.Add(outline);
    }

    // ── 底部按键提示条：用全项目共用的 SkyPrisonWindowHintBar（跟背包/设置窗口一样），
    // 不再自己重写一遍图标解析+设备切换逻辑。返回确认弹窗自己那套提示走 PushContext/
    // PopContext（共用组件本来就是为了这种"临时切到子上下文提示、关掉再切回来"设计的），
    // 不用再手动隐藏/显示 _mainHintBar 了。
    private void BuildHintBar(RectTransform root, System.Func<string, string, string> L)
    {
        // 不能用 SkyPrisonWindowHint.Action(MoveUp/Interact/Menu, ...)：这几个 action 在
        // SkyPrisonInputSettings.asset 里 gamepadKey 都是 None（专门给键盘用的），这个
        // 菜单的手柄导航走的是原始轴/JoystickButton（见 HandleCursorNavigation），根本不
        // 经过这几个 action。用 Action() 解析在手柄模式下永远找不到手柄图标，整条提示
        // 会被直接隐藏——手柄插着时这条提示条等于完全消失。跟 SettingsWindowUI 一样
        // 改成手动配对键鼠图标+手柄图标。"继续游戏"这个词跟上面第一个菜单项重复、
        // 容易被误会成第 4 个独立操作，改成"关闭菜单"更准确（Esc/B 本来就是直接关闭
        // 暂停菜单、回到游戏，跟点开"继续游戏"那一项效果一样，只是走的是快捷键）。
        var hints = new[]
        {
            new SkyPrisonWindowHint { iconKey = "keyboard/w", gamepadIconKey = "gamepad/up", fallbackText = "W", label = L("ui_saveslot_move_hint", "移动") },
            new SkyPrisonWindowHint { iconKey = "keyboard/e", gamepadIconKey = "gamepad/xbox/a", fallbackText = "E", label = L("ui_saveslot_click_token", "确定") },
            new SkyPrisonWindowHint { iconKey = "keyboard/esc", gamepadIconKey = "gamepad/xbox/b", fallbackText = "Esc", label = L("ui_pause_close", "关闭菜单") },
        };
        SkyPrisonWindowHintBar.GetOrCreate().Show(hints);
    }

    /// <summary>
    /// 返回主菜单前必须二次确认——搜打撤游戏关卡内没有手动存档，回主菜单等于放弃本次，
    /// 战斗进度会直接丢失，跟"继续游戏/设置"那种可以随便点的选项不是一个量级的操作。
    /// </summary>
    private void ShowReturnMenuConfirm(RectTransform root, System.Func<string, string, string> L)
    {
        if (_returnConfirmOverlay != null) return;

        // 主菜单三个按钮整体隐藏——弹窗背景本身是半透明模糊，光靠透明度盖是盖不干净的，
        // 之前就出现过按钮文字从确认框边缘透出来的问题，干脆藏起来，等确认框关闭再显示。
        if (_panelRt != null) _panelRt.gameObject.SetActive(false);


        var overlay = MakeRect("ReturnConfirm", root, Vector2.zero, Vector2.one);
        overlay.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
        overlay.gameObject.AddComponent<Button>().onClick.AddListener(() => { }); // 挡住点击穿透到后面菜单
        _returnConfirmOverlay = overlay.gameObject;

        // 视觉照搬濒死复活弹窗（PlayerDeathReviveUI）：深色面板 + 白色 L 形角标，
        // 危险色只用在文字上（标题/确认按钮），不整框染红——跟死亡弹窗的警示级别区分开。
        var box = MakeRect("Box", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        box.sizeDelta = new Vector2(960f, 440f);

        // 背景复用同一张已经截好的模糊图，转成黑白（跟其他窗口统一的磨砂规范），
        // 上面再叠一层半透明黑，保证文字可读、也保证盖住的地方真的什么都看不见。
        if (_capturedBlurRT != null)
        {
            var blurImg = box.gameObject.AddComponent<RawImage>();
            blurImg.texture = _capturedBlurRT;

            // 截图是整屏的，直接铺满这个小盒子会把整张图挤扁——按盒子在屏幕上的实际
            // 占比裁出对应那一小块 UV，看起来才像"这块正好是背景那一小片"，而不是
            // 缩小变形的整屏缩略图。
            float wFrac = box.rect.width  / root.rect.width;
            float hFrac = box.rect.height / root.rect.height;
            blurImg.uvRect = new Rect(0.5f - wFrac * 0.5f, 0.5f - hFrac * 0.5f, wFrac, hFrac);

            EnsureDesaturateMaterial();
            if (_desaturateMaterial != null) blurImg.material = _desaturateMaterial;
        }
        else
        {
            box.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.05f, 0.92f);
        }
        var boxTint = MakeRect("Tint", box, Vector2.zero, Vector2.one);
        boxTint.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        AddCornerBrackets(box, Color.white, 40f, 4f);
        Color danger = new Color(0.75f, 0.42f, 0.42f, 1f);

        var titleRt = MakeRect("Title", box, new Vector2(0f, 1f), new Vector2(1f, 1f));
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -56f);
        titleRt.sizeDelta = new Vector2(0f, 64f);
        AddTMP(titleRt, "Label", L("ui_pause_return_confirm_title", "返回主菜单？"), 40f,
            TextAlignmentOptions.Center, danger, FontStyles.Bold);

        var bodyRt = MakeRect("Body", box, new Vector2(0.08f, 0.42f), new Vector2(0.92f, 0.72f));
        var bodyTmp = AddTMP(bodyRt, "Label", L("ui_pause_return_confirm_body", "本次战斗进度将会丢失，确定要返回吗？"), 30f,
            TextAlignmentOptions.Center, Color.white, FontStyles.Normal);
        bodyTmp.enableWordWrapping = true;

        BuildConfirmDialogButton(box, new Vector2(0.08f, 0.14f), new Vector2(0.48f, 0.34f),
            L("ui_pause_return_confirm_yes", "确认返回"), danger, () =>
            {
                CloseReturnMenuConfirm();
                OnReturnToMainMenu();
            });
        BuildConfirmDialogButton(box, new Vector2(0.52f, 0.14f), new Vector2(0.92f, 0.34f),
            L("ui_discard_cancel", "取消"), TextLight, CloseReturnMenuConfirm);

        // 按键提示：这个弹窗单独一套（确定=确认返回，Esc=取消），走共用提示条的
        // PushContext——关闭弹窗时 PopContext 会自动切回主提示条，不用手动管隐藏/显示。
        SkyPrisonWindowHintBar.PushContext(new[]
        {
            SkyPrisonWindowHint.Action(SkyPrisonInputAction.Interact, L("ui_saveslot_click_token", "确定")),
            SkyPrisonWindowHint.Action(SkyPrisonInputAction.Menu, L("ui_discard_cancel", "取消")),
        });

        TryBindFonts(overlay);
    }

    private void CloseReturnMenuConfirm()
    {
        if (_returnConfirmOverlay == null) return;
        Destroy(_returnConfirmOverlay);
        _returnConfirmOverlay = null;
        if (_panelRt != null) _panelRt.gameObject.SetActive(true);
        SkyPrisonWindowHintBar.PopContext();
    }

    private void BuildConfirmDialogButton(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
        string label, Color textColor, System.Action onClick)
    {
        var rt = MakeRect("Btn", parent, anchorMin, anchorMax);
        rt.gameObject.AddComponent<Image>().color = Color.clear;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() =>
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
            onClick?.Invoke();
        });
        SkyPrisonUIButtonFeedback.Attach(rt.gameObject);
        AddOutline(rt, new Color(1f, 1f, 1f, 0.35f), 3f);
        AddTMP(rt, "Label", label, 32f, TextAlignmentOptions.Center, textColor, FontStyles.Normal)
            .raycastTarget = false;
    }

    private void OnReturnToMainMenu()
    {
        Time.timeScale = _savedTimeScale;
        IsOpen = false;
        _lastCloseFrame = Time.frameCount;
        _instance = null;
        SkyPrisonWindowManager_V1.ExternalBlock = false;
        RestoreCursor();
        SkyPrisonWindowHintBar.GetOrCreate().Clear();
        SceneLoader.LoadMainMenu();
        Destroy(gameObject);
    }

    private bool _closing;

    // 关闭时把开场那套演出倒过来播一遍：面板先淡出，然后背景畸变从稳定值收回到0，
    // 感觉像"系统屏幕变回真实画面"，再真正执行原来的清理/销毁。全程不缩放时间，
    // 这时候游戏还是暂停状态（Time.timeScale 还没恢复），不会被跳过。
    private void BeginClose()
    {
        if (_closing) return;
        _closing = true;
        StartCoroutine(CloseOutroThenClose());
    }

    private IEnumerator CloseOutroThenClose()
    {
        var panelCg = _panelRt != null ? _panelRt.GetComponent<CanvasGroup>() : null;

        // 底部按键提示条要在这里就清掉，不能拖到协程最后的 Close() 里——面板本身只需要
        // PanelFadeInDuration(0.25秒) 就淡出消失了，但后面还有两段共1秒的画面畸变收回
        // 动画（退出转场效果）。之前 Clear() 只在协程最后调用，等于面板早就看不见了，
        // 提示条却还在空荡荡的游戏画面上多停留将近1秒才消失——表现就是"没打开窗口时
        // 按键提示还在"。提示条要跟面板本身的淡出同步消失，不用等整套转场动画播完。
        SkyPrisonWindowHintBar.GetOrCreate().Clear();

        // 淡出动画一开始播，交互就要立刻冻结——之前这里只管淡透明度，
        // interactable/blocksRaycasts 一直是 true，导致选项明明已经在视觉上消失，
        // 光标/手柄导航却还能在这段淡出期间正常切换/点击。同时清掉 EventSystem 当前
        // 选中项，防止手柄导航高亮停在一个正在消失的按钮上。
        if (panelCg != null)
        {
            panelCg.interactable   = false;
            panelCg.blocksRaycasts = false;
        }
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem != null) eventSystem.SetSelectedGameObject(null);

        float ft = 0f;
        while (ft < PanelFadeInDuration)
        {
            ft += Time.unscaledDeltaTime;
            if (panelCg != null) panelCg.alpha = 1f - Mathf.Clamp01(ft / PanelFadeInDuration);
            yield return null;
        }
        if (panelCg != null) panelCg.alpha = 0f;

        // 先淡出"绿色+屏幕感"（双色调/扫描线），回到接近原始画面的颜色，再收畸变——
        // 两件事分先后，不是同时糊在一起收尾。
        float gt = 0f;
        while (gt < DistortionIntroDuration)
        {
            gt += Time.unscaledDeltaTime;
            float gp = Mathf.Clamp01(gt / DistortionIntroDuration);
            if (_desaturateMaterial != null)
            {
                _desaturateMaterial.SetFloat("_DuotoneStrength", Mathf.Lerp(0.6f, 0f, gp));
                _desaturateMaterial.SetFloat("_ScanlineStrength", Mathf.Lerp(0.25f, 0f, gp));
            }
            yield return null;
        }
        if (_desaturateMaterial != null)
        {
            _desaturateMaterial.SetFloat("_DuotoneStrength", 0f);
            _desaturateMaterial.SetFloat("_ScanlineStrength", 0f);
        }

        float t = 0f;
        while (t < DistortionIntroDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / DistortionIntroDuration);
            float k = p * p; // ease-in：一开始收得慢，后段收得快
            if (_desaturateMaterial != null)
                _desaturateMaterial.SetFloat("_DistortionStrength", Mathf.Lerp(DistortionSteadyStrength, 0f, k));
            yield return null;
        }
        if (_desaturateMaterial != null) _desaturateMaterial.SetFloat("_DistortionStrength", 0f);

        Close();
    }

    private void Close()
    {
        Time.timeScale = _savedTimeScale;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);
        IsOpen = false;
        _lastCloseFrame = Time.frameCount;
        _instance = null;
        SkyPrisonWindowManager_V1.ExternalBlock = false;
        RestoreCursor();
        SkyPrisonWindowHintBar.GetOrCreate().Clear();
        // 之前这里只恢复了 alpha，HideAllGameCanvases() 里还顺手把 blocksRaycasts/
        // interactable 也摁成了false——恢复只管透明度，这两个开关永远停在false，
        // 视觉上窗口好好的，但GraphicRaycaster再也不往里面发指针事件，鼠标点击
        // 从此失灵。商店这类跟暂停菜单同为ScreenSpaceOverlay根Canvas的悬浮窗口，
        // 只要暂停菜单开过一次就会中招。三个字段全部恢复。
        foreach (var entry in _hiddenGameCanvases)
        {
            if (entry.cg == null) continue;
            entry.cg.alpha = entry.origAlpha;
            entry.cg.interactable = entry.origInteractable;
            entry.cg.blocksRaycasts = entry.origBlocksRaycasts;
        }
        foreach (var entry in _forceDisabledRoots)
            if (entry.go != null) entry.go.SetActive(entry.wasActive);
        if (_capturedBlurRT != null) { _capturedBlurRT.Release(); Destroy(_capturedBlurRT); }
        if (_desaturateMaterial != null) { Destroy(_desaturateMaterial); _desaturateMaterial = null; }
        Destroy(gameObject);
    }

    private void RestoreCursor()
    {
        Cursor.visible   = _savedCursorVisible;
        Cursor.lockState = _savedCursorLockState;
        PlayerAimFacingController.Instance?.PopWindowCursor();
    }

    // 已经处理过的 CanvasGroup，防止重复加进 _hiddenGameCanvases（那样恢复时同一个
    // 会被处理两次，第二次会把 alpha 错误地设成"已经是0"那次记录的原值）。
    private readonly HashSet<CanvasGroup> _hiddenGameCanvasSet = new();

    /// <summary>
    /// 遮盖所有玩法 Canvas（血条/快捷栏/状态图标等），不管它们各自属于哪个独立系统。
    /// 之前只挑"根" Canvas（isRootCanvas），但像快捷栏按键提示条那种用
    /// overrideSorting=true 的嵌套 Canvas（挂在别的 Canvas 底下、自己不是 root）会被
    /// 这条过滤直接跳过，完全没被摸到——只指望它的根祖先的 CanvasGroup 生效，但根祖先
    /// 可能是另一套完全不相关的系统，压根没被这次扫描找到或没被正确影响到，实测下来
    /// 就是这个原因导致某些 HUD 元素死活收不干净。改成不管是不是 root，只要是
    /// ScreenSpaceOverlay 就直接在它自己身上加 CanvasGroup 摁 alpha=0，嵌套的、
    /// override sorting 的都能覆盖到，双保险。
    /// 另外持续每帧扫描（不是只在打开那一刻扫一次），抓住暂停期间任何延迟创建/
    /// 重新创建的画布（比如背包窗口在暂停中被重新打开又关闭）；已处理过的用 HashSet
    /// 跳过，不会重复处理，开销可以忽略。
    /// </summary>
    private void HideAllGameCanvases()
    {
        foreach (var canvas in FindObjectsOfType<Canvas>(includeInactive: true))
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            if (canvas.transform.IsChildOf(transform)) continue; // 别把自己也遮了

            // 设置窗口是独立挂出来的 GameObject，不是暂停菜单的子物体——不排除的话会被
            // 当成玩法 Canvas 藏掉（闪回暂停菜单、按钮点不了、Esc 关不掉）。
            var settingsRoot = SettingsWindowUI.ActiveRoot;
            if (settingsRoot != null && canvas.transform.IsChildOf(settingsRoot)) continue;

            // 自定义鼠标图标也是独立的常驻 ScreenSpaceOverlay Canvas，不排除的话会被
            // 这条通用遮盖逻辑一起摁 alpha=0 藏起来——暂停/设置界面看不到鼠标图标
            // 就是这个原因，鼠标理应任何界面下都要显示。
            if (canvas == SkyPrison.Runtime.UI.SkyPrisonCustomCursor.CursorCanvas) continue;

            var cg = canvas.GetComponent<CanvasGroup>();
            if (cg == null) cg = canvas.gameObject.AddComponent<CanvasGroup>();
            if (!_hiddenGameCanvasSet.Add(cg)) continue; // 已经处理过

            _hiddenGameCanvases.Add((cg, cg.alpha, cg.interactable, cg.blocksRaycasts));
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            cg.interactable = false;
        }
    }

    private void Update()
    {
        // 持续扫描，抓住暂停期间任何延迟创建/重新创建的玩法 Canvas（比如背包窗口
        // 在暂停中被重新打开又关闭）。已处理过的会被 HashSet 跳过，开销可以忽略。
        HideAllGameCanvases();

        // Unity 内置 _Time 跟着 Time.timeScale 走，暂停时 timeScale=0 会让 shader 里所有
        // 用 _Time 驱动的动画（比如背景的局部抖动块）当场冻结。这里每帧手动把不缩放的
        // 时间传给材质，shader 那边改用 _UnscaledTime 而不是内置 _Time。
        if (_desaturateMaterial != null)
            _desaturateMaterial.SetFloat("_UnscaledTime", Time.unscaledTime);

        // 淡出动画开始后（BeginClose 已经把 _closing 置true），这套上下选择/确认/取消
        // 全是自己手动读键盘手柄输入的，完全不经过 CanvasGroup.interactable——之前只把
        // CanvasGroup 关掉压根拦不住这里，导致面板已经在淡出消失，玩家却还能切换选项、
        // 还能听到切换音效。直接在这里挡住，淡出期间不再处理任何这套自定义导航输入。
        if (_closing)
            return;

        bool escape = _inputSettings != null
            ? _inputSettings.GetActionDown(SkyPrisonInputAction.Menu)
            : Input.GetKeyDown(KeyCode.Escape);
        bool gamepadB = Input.GetKeyDown(KeyCode.JoystickButton1);

        // 返回主菜单二次确认弹窗打开时，Esc/B 等同于"取消"，E/确定等同于"确认返回"，
        // 不直接退出暂停菜单、也不走下面主菜单三个按钮的光标导航（面板已经隐藏了）。
        if (_returnConfirmOverlay != null)
        {
            if (escape || gamepadB) { CloseReturnMenuConfirm(); return; }

            bool confirmKey = _inputSettings != null
                ? _inputSettings.GetActionDown(SkyPrisonInputAction.Interact)
                : Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.JoystickButton0);
            if (confirmKey)
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
                CloseReturnMenuConfirm();
                OnReturnToMainMenu();
            }
            return;
        }

        // 设置窗口打开时，它自己的 Update() 会处理 Esc 把自己关掉（它是独立窗口，
        // sortingOrder 比暂停菜单高）——这里让路，不要在同一次按键里把暂停菜单也关了。
        // 还要额外查"这一帧刚关闭"，否则设置窗口自己先关、这里紧接着读到 IsOpen=false，
        // 会把同一次 Esc 误当成"没人处理"又把暂停菜单也关掉。
        if (SettingsWindowUI.IsOpen || SettingsWindowUI.JustClosedThisFrame) return;

        if (escape || gamepadB) { Resume(); return; }

        HandleCursorNavigation();
    }

    private void HandleCursorNavigation()
    {
        if (_buttons.Count == 0) return;

        bool confirm = _inputSettings != null
            ? _inputSettings.GetActionDown(SkyPrisonInputAction.Interact)
            : Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.JoystickButton0);
        if (confirm)
        {
            _buttons[_cursorIndex].onClick.Invoke();
            return;
        }

        float axisY = SafeAxis("Vertical");
        float dpadY = SafeAxis("DPadVertical");
        // 边缘检测：轴值从中立区跨过阈值那一帧才算"按下一次"，摇杆/D-pad 推住不放
        // 不会每帧连续触发（跟 held 分支的持续重复分开处理）。
        bool axisUpEdge   = (axisY >  NavAxisThreshold && _prevNavAxisY <=  NavAxisThreshold) || (dpadY >  NavAxisThreshold && _prevNavDpadY <=  NavAxisThreshold);
        bool axisDownEdge = (axisY < -NavAxisThreshold && _prevNavAxisY >= -NavAxisThreshold) || (dpadY < -NavAxisThreshold && _prevNavDpadY >= -NavAxisThreshold);
        _prevNavAxisY = axisY;
        _prevNavDpadY = dpadY;

        bool up   = (_inputSettings != null && _inputSettings.GetActionDown(SkyPrisonInputAction.MoveUp))   || Input.GetKeyDown(KeyCode.UpArrow) || axisUpEdge;
        bool down = (_inputSettings != null && _inputSettings.GetActionDown(SkyPrisonInputAction.MoveDown)) || Input.GetKeyDown(KeyCode.DownArrow) || axisDownEdge;
        bool upHeld   = (_inputSettings != null && _inputSettings.GetAction(SkyPrisonInputAction.MoveUp))   || Input.GetKey(KeyCode.UpArrow) || axisY > NavAxisThreshold || dpadY > NavAxisThreshold;
        bool downHeld = (_inputSettings != null && _inputSettings.GetAction(SkyPrisonInputAction.MoveDown)) || Input.GetKey(KeyCode.DownArrow) || axisY < -NavAxisThreshold || dpadY < -NavAxisThreshold;

        if (up || down)
        {
            MoveCursor(up ? -1 : 1);
            _navCooldown = NavDelay;
        }
        else if (upHeld || downHeld)
        {
            _navCooldown -= Time.unscaledDeltaTime;
            if (_navCooldown <= 0f)
            {
                MoveCursor(upHeld ? -1 : 1);
                _navCooldown = NavRepeat;
            }
        }
        else
        {
            _navCooldown = 0f;
        }
    }

    /// <summary>标题结尾的 "_" 像系统等待输入的光标一样闪烁——固定 0.5s 间隔硬切换
    /// （不做淡入淡出），这样才有"数字信号跳变"的生硬感，而不是柔和的呼吸灯效果。
    /// 光标是独立于主文案的另一个 TMP 对象（见 BuildRoutine 里的 TitleGroup），
    /// 这里只切它自己的 alpha，不改主文案的文字内容/宽度，前面的字不可能被牵动。</summary>
    private IEnumerator BlinkTitleCursor()
    {
        const float blinkInterval = 0.5f;
        bool cursorOn = true;
        while (true)
        {
            if (_titleCursorText != null)
            {
                Color c = _titleCursorText.color;
                c.a = cursorOn ? 1f : 0f;
                _titleCursorText.color = c;
            }
            cursorOn = !cursorOn;
            yield return new WaitForSecondsRealtime(blinkInterval);
        }
    }

    private void MoveCursor(int dir)
    {
        _cursorIndex = (_cursorIndex + dir + _buttons.Count) % _buttons.Count;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        RefreshCursorVisuals();
    }

    private void RefreshCursorVisuals()
    {
        for (int i = 0; i < _buttonOutlines.Count; i++)
        {
            Color c = (i == _cursorIndex) ? ColdGreen : new Color(1f, 1f, 1f, 0.35f);
            foreach (var img in _buttonOutlines[i]) img.color = c;
        }
    }

    private void OnDestroy()
    {
        LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
        if (_capturedBlurRT != null) { _capturedBlurRT.Release(); Destroy(_capturedBlurRT); }
        if (_desaturateMaterial != null) { Destroy(_desaturateMaterial); _desaturateMaterial = null; }

        // 兜底：这个物体不是 DontDestroyOnLoad，如果是被 Close()/CloseOutroThenClose()
        // 之外的路径销毁的（比如地图切换时被当普通场景物体一起清掉），下面这些全局
        // 静态状态之前完全没人重置——IsOpen 永远卡 true、ExternalBlock 永远卡 true、
        // 鼠标锁定状态也恢复不了，Esc 键从此彻底失灵（路由器看 IsOpen==true 不再
        // 触发 Show()，Resume() 看 _instance 已销毁直接返回），底部按键提示条永远
        // 卡在画面上清不掉。只在这个实例仍是当前活跃实例时才复位，避免旧实例的
        // OnDestroy 把刚打开的新实例状态误清掉。
        if (_instance == this)
        {
            Time.timeScale = _savedTimeScale;
            IsOpen = false;
            _lastCloseFrame = Time.frameCount;
            _instance = null;
            SkyPrisonWindowManager_V1.ExternalBlock = false;
            RestoreCursor();
            SkyPrisonWindowHintBar.GetOrCreate().Clear();
        }
    }

    // ── UI 工具（与 SaveSlotSelectorUI 一致的规范实现）──────────────────────

    private static RenderTexture CaptureAndBlurScreen()
    {
        // 用 CaptureScreenshotAsTexture 而不是手动按 Screen.width/height 建 RT 再
        // CaptureScreenshotIntoRenderTexture——编辑器高 DPI 缩放下后者会出现实际
        // 截到的画面比目标 RT 小一圈、贴在左下角、其余留黑的错位问题。
        // CaptureScreenshotAsTexture 返回的纹理尺寸就是真实截图尺寸，不会对不上。
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

        // 降到很小（真正糊开、不再是可辨认的缩小图），但放大回全屏时逐级放大——
        // 每一步放大不超过 2 倍，跟降采样时经过的尺寸链一一对应走回去。
        // 之前是"降一路小尺寸，最后一步直接怼到全屏"，几十像素单步放大几百倍，
        // 插值再怎么调都会显出网格状痕迹、或者干脆看起来就是"缩小糊了再放大"；
        // 逐级放大（类似 Kawase/dual-filter 模糊金字塔）才会呈现真正柔和的扩散感。
        const int MinBlurEdge = 40; // 数值越大降采样停得越早，糊得越轻——嫌糊得太狠就调大这个
        const int MaxBlurSteps = 10; // 安全上限，防止极端比例死循环
        var downSizes = new List<Vector2Int> { new Vector2Int(baseW, baseH) };
        RenderTexture src = baseRT;
        int curW = baseW, curH = baseH;
        for (int i = 0; i < MaxBlurSteps && curW > MinBlurEdge && curH > MinBlurEdge; i++)
        {
            curW = Mathf.Max(MinBlurEdge, curW / 2);
            curH = Mathf.Max(MinBlurEdge, curH / 2);
            var down = RenderTexture.GetTemporary(curW, curH, 0, RenderTextureFormat.DefaultHDR);
            down.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, down);
            temps.Add(down);
            downSizes.Add(new Vector2Int(curW, curH));
            src = down;
        }

        // 逐级放大：按降采样时经过的尺寸链，从小到大一步步走回去，一路走到 downSizes[0]
        // （也就是 baseW/baseH，之前的降采样基准图）。最后从 baseW/baseH 放大到真实全屏
        // 尺寸交给下面创建 result 时的那一次 Blit 来做——通常也就 2~4 倍，足够柔和。
        for (int i = downSizes.Count - 2; i >= 0; i--)
        {
            Vector2Int size = downSizes[i];
            var up = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.DefaultHDR);
            up.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, up);
            temps.Add(up);
            src = up;
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

    private const float TitleUnderlineWidth  = 1100f;
    private const float TitleUnderlineY      = -34f; // 相对 titleRt 竖直中心往下，落在标题文字底部附近
    private const float TitleMarkerGap       = 18f;  // 方块跟横线两端的间距
    private const float TitleMarkerSize      = 10f;

    private static void BuildTitleUnderline(RectTransform titleRt)
    {
        var lineRt = MakeRect("Underline", titleRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        lineRt.pivot = new Vector2(0.5f, 0.5f);
        lineRt.anchoredPosition = new Vector2(0f, TitleUnderlineY);
        lineRt.sizeDelta = new Vector2(TitleUnderlineWidth, 2f);
        var lineImg = lineRt.gameObject.AddComponent<Image>();
        lineImg.color = ColdGreen;
        lineImg.raycastTarget = false;

        float markerX = TitleUnderlineWidth * 0.5f + TitleMarkerGap;
        BuildTitleMarker(titleRt, -markerX);
        BuildTitleMarker(titleRt, markerX);
    }

    private static void BuildTitleMarker(RectTransform titleRt, float x)
    {
        var markerRt = MakeRect("Marker", titleRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        markerRt.pivot = new Vector2(0.5f, 0.5f);
        markerRt.anchoredPosition = new Vector2(x, TitleUnderlineY);
        markerRt.sizeDelta = new Vector2(TitleMarkerSize, TitleMarkerSize);
        var markerImg = markerRt.gameObject.AddComponent<Image>();
        markerImg.color = ColdGreen;
        markerImg.raycastTarget = false;
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

    /// <summary>白色 L 形四角标——照搬 PlayerDeathReviveUI 的警示窗规范，只在弹窗类小窗上用。</summary>
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

    private void TryBindFonts(Transform root)
    {
        if (_font == null) return;
        foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            t.font = _font;
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
