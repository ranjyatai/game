using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 加载界面 UI。挂在 DontDestroyOnLoad 的 Canvas 上，由 LoadingScreenBootstrap 自动创建。
/// </summary>
public class LoadingScreenUI : MonoBehaviour
{
    // ── 配色 ─────────────────────────────────────────────────────────────
    private static readonly Color ColdGreen  = new Color(0.52f, 0.90f, 0.68f, 1f);
    private static readonly Color WhiteDim   = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color WhiteMid   = new Color(1f, 1f, 1f, 0.30f);
    private static readonly Color WhiteFaint = new Color(1f, 1f, 1f, 0.05f);

    private static readonly string[] FallbackTips =
    {
        "死亡后背包内所有物品将永久丢失，请谨慎行事。",
        "探索地图时注意视野死角，敌人可能潜伏在阴影中。",
        "基地仓库的物品不会因死亡丢失，出发前记得整理。",
        "全息掉落物的颜色代表稀有度，金色表示极品装备。",
        "被击倒前请确认附近有安全撤退路线。",
    };

    private LoadingScreenSettings _loadingSettings;

    private static readonly string[] StatusLines =
    {
        "INITIALIZING SECTOR DATA...",
        "LOADING ASSET BUNDLE [{0}/12]",
        "CALIBRATING ENVIRONMENT MESH...",
        "SYNCING WORLD STATE...",
        "VALIDATING RUNTIME CATALOG...",
        "STREAMING AUDIO PACKAGES...",
        "PREPARING SPAWN POINTS...",
        "ESTABLISHING PATROL ROUTES...",
    };

    // ── UI 组件 ──────────────────────────────────────────────────────────
    private Canvas       _canvas;
    private CanvasGroup  _group;
    private CanvasGroup  _contentGroup;  // 所有 content 元素的父级；按 E 后瞬间 alpha=0
    private Image        _barFill;
    private TMP_Text     _tipText;
    private TMP_Text     _statusLine;
    private CanvasGroup  _confirmGroup;
    private Image        _confirmIcon;
    private TMP_Text     _confirmLabel;

    private SkyPrisonInputSettings           _inputSettings;
    private SkyPrisonInputPromptIconDatabase _iconDb;

    private bool _isReady;
    private int  _statusIndex;
    private int  _fakeBundleIdx;

    // 进度条距底部距离（要高于角标，角标 margin+arm = 18+28 = 46px）
    private const float BarBottomY = 112f;
    private const float BarHeight  = 4f;

    // ── 生命周期 ──────────────────────────────────────────────────────────

    // 用静态字段做单例去重，而不是 FindObjectOfType。
    // FindObjectOfType 在两个实例几乎同帧 Awake 时不可靠：
    // 双方都可能"找到自己"而不是对方（取决于场景对象遍历顺序），导致谁都没有自毁，
    // 最终出现两份 LoadingScreenUI 各自跑自己的揭幕淡出动画，互相打架造成画面闪烁/回暗。
    // 静态字段是确定性的：不管谁先 Awake，后来者一定能看到前者已经登记的引用。
    private static LoadingScreenUI _instance;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        _loadingSettings = Resources.Load<LoadingScreenSettings>("LoadingScreenSettings");

        Build();
        SetVisible(false);

        SceneLoader.OnLoadStart    += HandleLoadStart;
        SceneLoader.OnLoadProgress += HandleLoadProgress;
        SceneLoader.OnLoadReady    += HandleLoadReady;
        SceneLoader.OnLoadFadeOut  += HandleLoadFadeOut;
        SceneLoader.OnLoadComplete += HandleLoadComplete;

        _inputSettings = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");
        LoadIconDatabase();

        SkyPrisonInputDeviceTracker.OnDeviceFamilyChanged += OnDeviceChanged;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;

        SceneLoader.OnLoadStart    -= HandleLoadStart;
        SceneLoader.OnLoadProgress -= HandleLoadProgress;
        SceneLoader.OnLoadReady    -= HandleLoadReady;
        SceneLoader.OnLoadFadeOut  -= HandleLoadFadeOut;
        SceneLoader.OnLoadComplete -= HandleLoadComplete;
        SkyPrisonInputDeviceTracker.OnDeviceFamilyChanged -= OnDeviceChanged;
    }

    // 每次进入 Play Mode 都重置静态单例引用，防止 Editor 关闭 Domain Reload 时残留野指针
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStaticInstance()
    {
        _instance = null;
    }

    private void Update()
    {
        if (!_isReady) return;

        // Interact 这个 action 在 SkyPrisonInputSettings.asset 里 gamepadKey 是 None
        // （键盘专用），单靠 GetActionDown(Interact) 手柄 A 键按下去在这个画面完全没
        // 反应——不只是图标没切，是真的按了没用。跟其它窗口一样额外兜一个原始
        // JoystickButton0。
        bool confirm = (_inputSettings != null && _inputSettings.GetActionDown(SkyPrisonInputAction.Interact))
            || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)
            || Input.GetKeyDown(KeyCode.E)      || Input.GetMouseButtonDown(0)
            || Input.GetKeyDown(KeyCode.JoystickButton0);

        if (confirm)
        {
            _isReady = false;
            if (_confirmGroup != null) _confirmGroup.alpha = 0f;
            SceneLoader.ConfirmEnter();
        }
    }

    // ── 事件 ─────────────────────────────────────────────────────────────

    private void HandleLoadStart()
    {
        if (_tipText != null)
        {
            var    loc      = Object.FindObjectOfType<LocalizationRuntime>();
            string langCode = loc != null ? loc.CurrentCode : "zh-CN";
            string tip = _loadingSettings != null && _loadingSettings.tips != null && _loadingSettings.tips.Count > 0
                ? _loadingSettings.GetRandomTip(langCode)
                : FallbackTips[Random.Range(0, FallbackTips.Length)];
            _tipText.text = tip;
        }
        if (_confirmGroup != null) _confirmGroup.alpha = 0f;
        _isReady       = false;
        _statusIndex   = 0;
        _fakeBundleIdx = 1;
        SetProgress(0f);

        // 重置 content 层（供重复加载复用）
        if (_contentGroup != null) _contentGroup.alpha = 1f;

        // 从黑屏淡入，而非直接弹出
        StopAllCoroutines();
        if (_canvas != null) _canvas.enabled = true; // 上一次 reveal 可能把 Canvas 禁用了，这里重新打开
        _group.alpha          = 0f;
        _group.blocksRaycasts = true;
        _group.interactable   = true;
        StartCoroutine(FadeGroupRoutine(0f, 1f, 0.35f));
        StartCoroutine(StatusLineCycle());
    }

    private void HandleLoadProgress(float t) => SetProgress(t);

    private void HandleLoadReady()
    {
        SetProgress(1f);
        _isReady = true;
        if (_iconDb == null) LoadIconDatabase();
        RefreshConfirmPrompt();
        StartCoroutine(ConfirmBlink());
    }

    /// <summary>
    /// 玩家按 E：瞬间清空 ContentLayer（tip/bar/角标/状态文字全消），
    /// 只留纯黑底继续遮住场景激活帧，等 HandleLoadComplete 做最终揭幕。
    /// </summary>
    private void HandleLoadFadeOut()
    {
        _isReady = false;
        StopAllCoroutines();
        if (_contentGroup != null) _contentGroup.alpha = 0f;
        _group.alpha = 1f;   // 纯黑底保持不透明
    }

    /// <summary>
    /// BootCoordinator Phase4 完成后触发：纯黑底 0.2s 淡出，干净露出游戏世界。
    /// Content 已经是 alpha=0，不会有任何 UI 残留。
    /// </summary>
    private void HandleLoadComplete()
    {
        _isReady = false;
        StopAllCoroutines();
        StartCoroutine(RevealRoutine());
    }

    // ── 过渡协程 ──────────────────────────────────────────────────────────

    private IEnumerator FadeGroupRoutine(float from, float to, float duration)
    {
        if (_group == null) yield break;
        _group.alpha = from;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.SmoothStep(from, to, t / duration);
            yield return null;
        }
        _group.alpha = to;
    }

    private IEnumerator RevealRoutine()
    {
        if (_group == null) yield break;
        yield return new WaitForEndOfFrame();

        var gameCanvases = SceneLoader.GameCanvases;

        // try/finally 保证：无论中途是否被 StopAllCoroutines/异常打断，
        // 最终一定会把 loading canvas 彻底隐藏 + 游戏 canvas 恢复原始 alpha。
        try
        {
            // 标记揭幕动画独占这些 CanvasGroup 的 alpha 写入权，
            // 期间任何"自己也在驱动同一个alpha"的系统（如战斗HUD可见度自动淡入淡出）都会让路。
            SceneLoader.SetRevealing(true);
            _group.alpha = 1f;

            const float duration = 0.25f;
            float t = 0f;
            while (t < duration)
            {
                // 限制单帧最大步进：避免某一帧耗时突增时动画跟着跳一大截。
                t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
                float ratio = Mathf.Clamp01(t / duration);

                // 黑底和游戏UI错开时间轴：黑底在前70%时间就基本淡完，UI从20%才开始淡入，
                // 两者仍在同一时刻(ratio=1)完成，避免中间同时叠加两层半透明黑色的"回暗"感。
                float bgRatio = Mathf.Clamp01(ratio / 0.7f);
                float uiRatio = Mathf.Clamp01((ratio - 0.2f) / 0.8f);
                _group.alpha = Mathf.SmoothStep(1f, 0f, bgRatio);
                if (gameCanvases != null)
                    foreach (var (cg, origAlpha) in gameCanvases)
                        if (cg != null) cg.alpha = Mathf.SmoothStep(0f, origAlpha, uiRatio);

                yield return null;
            }
        }
        finally
        {
            if (gameCanvases != null)
                foreach (var (cg, origAlpha) in gameCanvases)
                    if (cg != null) cg.alpha = origAlpha;

            SetVisible(false);
            SceneLoader.SetRevealing(false); // 交还 alpha 控制权，各系统自己的实时逻辑从这里接手
            SceneLoader.NotifyRevealComplete(); // 画面真正露出来了，这之后才能拍存档快照/触发自动保存
        }
    }

    private void OnDeviceChanged(SkyPrisonInputDeviceTracker.DeviceFamily _,
                                 SkyPrisonInputDeviceTracker.DeviceFamily __)
    {
        if (_isReady) RefreshConfirmPrompt();
    }

    // ── 状态 ─────────────────────────────────────────────────────────────

    private void SetVisible(bool v)
    {
        if (_group == null) return;
        _group.alpha          = v ? 1f : 0f;
        _group.blocksRaycasts = v;
        _group.interactable   = v;
        // 硬开关：alpha 只是"看起来透明"，Canvas.enabled=false 才是真正不渲染，
        // 即便 alpha 因为某种 bug 卡住，也不会永久挡住游戏世界。
        if (_canvas != null) _canvas.enabled = v;
    }

    private void SetProgress(float t)
    {
        if (_barFill != null) _barFill.fillAmount = Mathf.Clamp01(t);
    }

    private void RefreshConfirmPrompt()
    {
        if (_confirmIcon == null || _confirmLabel == null) return;

        bool preferGamepad = SkyPrisonInputDeviceTracker.Current ==
                             SkyPrisonInputDeviceTracker.DeviceFamily.Gamepad;

        Sprite sprite = null;
        bool gotSprite;
        if (preferGamepad)
        {
            // Interact 这个 action 的 gamepadKey 在数据里就是 None（键盘专用），
            // TryResolveActionIcon 内部一查到 None 就直接回退回键盘图标，不管当前
            // 实际用的是不是手柄——这里的真实确认键跟别的窗口一样是硬编码的
            // JoystickButton0（A 键），不走这个 action 的手柄绑定，图标也直接照
            // 这个物理键查，不经过 Interact。
            gotSprite = _iconDb != null &&
                        _iconDb.TryGetSpriteForKeyCode(KeyCode.JoystickButton0,
                            SkyPrisonInputPromptDeviceStyle.GamepadXbox, out sprite, out _);
        }
        else
        {
            gotSprite = _inputSettings != null && _iconDb != null &&
                        SkyPrisonInputPromptResolver.TryResolveActionIcon(
                            _inputSettings, _iconDb,
                            SkyPrisonInputAction.Interact,
                            false,
                            SkyPrisonInputPromptDeviceStyle.GamepadXbox,
                            out sprite, out _, out _);
        }

        string continueWord = GetContinueWord();
        if (gotSprite)
        {
            _confirmIcon.sprite  = sprite;
            _confirmIcon.enabled = true;
            _confirmLabel.text   = $"  {continueWord}";
        }
        else
        {
            _confirmIcon.enabled = false;
            _confirmLabel.text   = $"[ {GetFallbackKeyLabel()} ]  {continueWord}";
        }
    }

    private string GetContinueWord()
    {
        var table = Resources.Load<UILocalizationTable>("UILocalizationTable");
        if (table != null)
        {
            string result = table.Get("ui_loading_continue", "");
            if (!string.IsNullOrEmpty(result)) return result;
        }
        // fallback：按语言 code 硬编码兜底，表里没配置时保证显示正确
        var loc = Object.FindObjectOfType<LocalizationRuntime>();
        string lang = loc != null ? loc.CurrentCode : "zh-CN";
        if (lang.StartsWith("en")) return "Continue";
        if (lang.StartsWith("ja")) return "続ける";
        if (lang.StartsWith("ko")) return "계속";
        return "继续";
    }

    private string GetFallbackKeyLabel()
    {
        bool isGamepad = SkyPrisonInputDeviceTracker.Current ==
                         SkyPrisonInputDeviceTracker.DeviceFamily.Gamepad;
        // 真实确认键在手柄模式下是硬编码的 JoystickButton0（A），不是 Interact 这个
        // action 的 gamepadKey（那个字段本来就是 None，键盘专用）。
        if (isGamepad) return "A";
        if (_inputSettings == null) return "E";
        var binding = _inputSettings.GetBinding(SkyPrisonInputAction.Interact);
        if (binding == null || binding.primaryKey == KeyCode.None) return "E";
        return binding.primaryKey.ToString();
    }

    // ── 协程 ─────────────────────────────────────────────────────────────

    private IEnumerator StatusLineCycle()
    {
        while (_group != null && _group.alpha > 0f)
        {
            if (_statusLine != null)
            {
                string raw  = StatusLines[_statusIndex % StatusLines.Length];
                string line = raw.Contains("{0}") ? string.Format(raw, _fakeBundleIdx) : raw;
                _statusLine.text = "> " + line;
                _statusIndex++;
                _fakeBundleIdx = Mathf.Min(_fakeBundleIdx + 1, 12);
            }
            yield return new WaitForSecondsRealtime(0.55f);
        }
    }

    private IEnumerator ConfirmBlink()
    {
        if (_confirmGroup == null) yield break;
        while (_isReady)
        {
            _confirmGroup.alpha = Mathf.Sin(Time.unscaledTime * 2.0f) * 0.3f + 0.7f;
            yield return null;
        }
        if (_confirmGroup != null) _confirmGroup.alpha = 0f;
    }

    // ── 构建 UI ───────────────────────────────────────────────────────────

    private void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 33000; // 高于所有游戏 UI（HiddenOutline=32700, QuickItemPrompt=32000）
        var canvas = _canvas;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.matchWidthOrHeight  = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _group = gameObject.AddComponent<CanvasGroup>();

        var root = (RectTransform)transform;

        // ── 纯黑背景：始终留在 root，按 E 后继续遮住场景激活 ──────────────
        MakeFullRect("BG", root).gameObject.AddComponent<Image>().color =
            new Color(0.04f, 0.04f, 0.05f, 1f);

        // ── ContentLayer：所有可见内容放这里，按 E 后 alpha=0 瞬间清空 ──────
        var contentRt = MakeFullRect("ContentLayer", root);
        _contentGroup = contentRt.gameObject.AddComponent<CanvasGroup>();
        _contentGroup.alpha = 1f;

        // 背景底图（Texture2D，直接拖 PNG）
        if (_loadingSettings != null && _loadingSettings.backgroundTexture != null)
        {
            var bgRt  = MakeFullRect("BGTexture", contentRt);
            var bgImg = bgRt.gameObject.AddComponent<RawImage>();
            bgImg.texture = _loadingSettings.backgroundTexture;
        }

        // 角标图层
        if (_loadingSettings != null && _loadingSettings.cornerOverlayTexture != null)
        {
            var tex  = _loadingSettings.cornerOverlayTexture;
            float uw = 1f / 3f;
            float uh = 1f / 3f;
            var   cell = new Vector2(tex.width * uw, tex.height * uh);
            BuildCornerQuadrant("CO_TL", contentRt, tex, new Vector2(0,1),   new Rect(0f,    2f*uh, uw, uh), cell);
            BuildCornerQuadrant("CO_TR", contentRt, tex, new Vector2(1,1),   new Rect(2f*uw, 2f*uh, uw, uh), cell);
            BuildCornerQuadrant("CO_BL", contentRt, tex, new Vector2(0,0),   new Rect(0f,    0f,    uw, uh), cell);
            BuildCornerQuadrant("CO_BR", contentRt, tex, new Vector2(1,0),   new Rect(2f*uw, 0f,    uw, uh), cell);
            BuildCornerQuadrant("CO_TC", contentRt, tex, new Vector2(0.5f,1),new Rect(uw,    2f*uh, uw, uh), cell);
            BuildCornerQuadrant("CO_BC", contentRt, tex, new Vector2(0.5f,0),new Rect(uw,    0f,    uw, uh), cell);
        }

        // 右上角区域水印
        var zoneRt = MakeAnchoredRect("ZoneWatermark", contentRt,
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-96f, -80f), new Vector2(1040f, 160f));
        var zoneTxt       = zoneRt.gameObject.AddComponent<TextMeshProUGUI>();
        zoneTxt.text      = "SECTOR // UNDEFINED";
        zoneTxt.alignment = TextAlignmentOptions.Right;
        zoneTxt.fontSize  = 84f;
        zoneTxt.color     = WhiteFaint;
        zoneTxt.fontStyle = FontStyles.Bold;

        // 左下：系统状态行
        var statusRt = MakeAnchoredRect("StatusLine", contentRt,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(56f, BarBottomY + BarHeight + 80f), new Vector2(1400f, 56f));
        _statusLine           = statusRt.gameObject.AddComponent<TextMeshProUGUI>();
        _statusLine.text      = "> INITIALIZING...";
        _statusLine.alignment = TextAlignmentOptions.Left;
        _statusLine.fontSize  = 40f;
        _statusLine.color     = WhiteMid;

        // 左下：提示文字
        var tipRt = MakeAnchoredRect("Tip", contentRt,
            new Vector2(0f, 0f), new Vector2(0.6f, 0f),
            new Vector2(56f, BarBottomY + BarHeight + 140f), new Vector2(0f, 88f));
        _tipText                    = tipRt.gameObject.AddComponent<TextMeshProUGUI>();
        _tipText.text               = "";
        _tipText.alignment          = TextAlignmentOptions.Left;
        _tipText.fontSize           = 40f;
        _tipText.color              = new Color(1f, 1f, 1f, 0.5f);
        _tipText.enableWordWrapping = true;
        _tipText.overflowMode       = TextOverflowModes.Ellipsis;

        // 右下：确认提示
        BuildConfirmPrompt(contentRt);

        // 进度条（冷绿，贴底稍离边）
        var track = MakeFullWidthBar("BarTrack", contentRt, BarBottomY, BarHeight);
        track.gameObject.AddComponent<Image>().color = WhiteDim;
        var fillRt      = MakeFullRect("BarFill", track);
        _barFill            = fillRt.gameObject.AddComponent<Image>();
        _barFill.color      = ColdGreen;
        _barFill.type       = Image.Type.Filled;
        _barFill.fillMethod = Image.FillMethod.Horizontal;
        _barFill.fillAmount = 0f;

        BindFont(_statusLine, _tipText, _confirmLabel, zoneTxt);
    }

    // ── 确认提示（图标 + 文字）───────────────────────────────────────────

    private void BuildConfirmPrompt(RectTransform root)
    {
        var go            = new GameObject("ConfirmPrompt");
        go.transform.SetParent(root, false);
        _confirmGroup             = go.AddComponent<CanvasGroup>();
        _confirmGroup.alpha       = 0f;
        _confirmGroup.blocksRaycasts = false;

        var rt             = go.AddComponent<RectTransform>();
        rt.anchorMin       = new Vector2(1f, 0f);
        rt.anchorMax       = new Vector2(1f, 0f);
        rt.pivot           = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-56f, BarBottomY + BarHeight + 24f);
        rt.sizeDelta       = new Vector2(760f, 88f);

        var layout                    = go.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment         = TextAnchor.MiddleRight;
        layout.spacing                = 12f;
        layout.childForceExpandWidth  = false;
        layout.childForceExpandHeight = false;

        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.Unconstrained;

        var iconGo        = new GameObject("Icon");
        iconGo.transform.SetParent(go.transform, false);
        iconGo.AddComponent<RectTransform>().sizeDelta = new Vector2(68f, 68f);
        _confirmIcon          = iconGo.AddComponent<Image>();
        _confirmIcon.color    = Color.white;
        _confirmIcon.enabled  = false;
        var iconLE            = iconGo.AddComponent<LayoutElement>();
        iconLE.preferredWidth  = 68f;
        iconLE.preferredHeight = 68f;

        var labelGo       = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, false);
        _confirmLabel             = labelGo.AddComponent<TextMeshProUGUI>();
        _confirmLabel.text        = $"[ E ]  {GetContinueWord()}";
        _confirmLabel.alignment   = TextAlignmentOptions.Right;
        _confirmLabel.fontSize    = 40f;
        _confirmLabel.color       = ColdGreen;
        _confirmLabel.characterSpacing = 1.5f;
        var labelLE               = labelGo.AddComponent<LayoutElement>();
        labelLE.preferredHeight   = 68f;
    }

    // ── 数据库加载 ────────────────────────────────────────────────────────

    private void LoadIconDatabase()
    {
        if (SkyPrisonQuickItemPromptStrip.RuntimeDatabase != null)
        {
            _iconDb = SkyPrisonQuickItemPromptStrip.RuntimeDatabase;
            return;
        }
        // 之前这里的兜底只在 UNITY_EDITOR 下用 AssetDatabase 加载——打包后这段代码
        // 整个不存在，读条画面又几乎总是在玩法 HUD 还没建出来之前就先跑（游戏刚启动/
        // 切场景），RuntimeDatabase 这时候基本是 null，图标兜底等于形同虚设，打包后
        // 一直只能看到文字。改成跟 SkyPrisonWindowHintBar/SkyPrisonItemPickupController
        // 一样从 Resources 加载，编辑器和打包后都能生效。
        _iconDb = Resources.Load<SkyPrisonInputPromptIconDatabase>("InputPromptIconDatabase");
    }

    // ── 角标图层象限 ──────────────────────────────────────────────────────
    // anchor = pivot = 屏幕角归一化坐标，anchoredPosition = (0,0) 贴住角，
    // sizeDelta = 图片尺寸的一半（固定 canvas 单位，不随分辨率缩放变大）。

    private static void BuildCornerQuadrant(string name, Transform parent,
        Texture2D tex, Vector2 corner, Rect uv, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = corner;
        rt.anchorMax        = corner;
        rt.pivot            = corner;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = size;
        var img    = go.AddComponent<RawImage>();
        img.texture = tex;
        img.uvRect  = uv;
    }

    // ── 布局工具 ──────────────────────────────────────────────────────────

    private static RectTransform MakeFullRect(string n, Transform parent)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static RectTransform MakeFullWidthBar(string n, Transform parent, float bottomY, float h)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 0f);
        rt.anchorMax        = new Vector2(1f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, bottomY);
        rt.sizeDelta        = new Vector2(0f, h);
        return rt;
    }

    private static RectTransform MakeAnchoredRect(string n, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = new Vector2(0f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        return rt;
    }

    // ── 字体绑定 ──────────────────────────────────────────────────────────

    private static void BindFont(params TMP_Text[] targets)
    {
        TMP_FontAsset font = null;
#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("ZhouFangRiMingTi-2 SDF t:TMP_FontAsset");
        if (guids.Length > 0)
            font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
#endif
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/ZhouFangRiMingTi-2 SDF");
        if (font == null) return;
        foreach (var t in targets)
            if (t != null) t.font = font;
    }
}
