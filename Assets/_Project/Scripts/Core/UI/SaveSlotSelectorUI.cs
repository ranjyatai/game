using System;
using System.Collections;
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 存档槽位选择器（程序化弹窗，遵循项目 UI 规范：白色描边 / 透明按钮 + ButtonFeedback）。
/// 贴边全屏窗口：背后无内容可露出，故不使用磨砂/角标（那套是给悬浮式小窗准备的）。
/// 用于"继续游戏"和"新游戏"两种模式：
///   - Continue 模式：只列出非空槽位，点击即加载
///   - NewGame  模式：列出全部槽位，默认光标在第一个空槽；
///                   选中非空槽位会先弹覆盖确认（覆盖确认用悬浮小窗，带角标）
/// </summary>
public class SaveSlotSelectorUI : MonoBehaviour
{
    public enum Mode { Continue, NewGame }

    // ── 单例（程序化创建，不放 Prefab）──────────────────────────────────
    private static SaveSlotSelectorUI _instance;

    /// <summary>存档窗口是否正打开。底下的主菜单/其他导航逻辑要检查这个，
    /// 弹窗打开期间自己的 W/S/方向键导航必须让路，否则会出现"背后菜单在无声移动光标，
    /// 却听到切换音效"这种诡异现象——按键其实被两边同时吃了。</summary>
    public static bool IsOpen { get; private set; }

    // 每次进入 Play Mode 都重置静态状态，防止 Editor 关闭 Domain Reload 时残留
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStaticState()
    {
        _instance = null;
        IsOpen = false;
    }

    public static void Show(Mode mode, Action<int> onConfirm, Action onCancel = null)
    {
        if (_instance != null) return;

        var go = new GameObject("[SaveSlotSelector]");
        DontDestroyOnLoad(go);
        var ui = go.AddComponent<SaveSlotSelectorUI>();
        ui._mode      = mode;
        ui._onConfirm = onConfirm;
        ui._onCancel  = onCancel;
        ui.StartCoroutine(ui.BuildRoutine());
        _instance = ui;
        IsOpen = true;
    }

    // ── 配色（与项目 UI 设计系统一致）───────────────────────────────────
    private static readonly Color ColdGreen     = new Color(0.42f, 0.92f, 0.68f, 1f);
    private static readonly Color TextLight     = new Color(0.88f, 0.88f, 0.90f, 1f);
    private static readonly Color TextFaint     = new Color(0.55f, 0.62f, 0.65f, 1f);
    private static readonly Color TextDisabled  = new Color(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Color DangerText    = new Color(0.75f, 0.42f, 0.42f, 1f);
    private static readonly Color PanelOverlay  = new Color(0.03f, 0.04f, 0.05f, 0.84f);
    private static readonly Color SepLine       = new Color(1f, 1f, 1f, 0.08f);

    // ── 运行时 ────────────────────────────────────────────────────────────
    private Mode          _mode;
    private Action<int>   _onConfirm;
    private Action        _onCancel;

    private GameObject    _root;
    private GameObject    _confirmOverlay;
    private TMP_FontAsset _font;
    private TMP_FontAsset _numberFont; // 编号徽标专用字体（东亚重工风格）
    private RenderTexture _capturedBlurRT;
    private SkyPrisonInputPromptIconDatabase _iconDb;
    private UILocalizationTable _locTable;

    private string L(string key, string fallback) =>
        _locTable != null ? _locTable.Get(key, fallback) : fallback;

    private readonly List<SaveSlotMeta>  _metas = new List<SaveSlotMeta>();

    private const float RowHeight  = 320f;
    private const float RowSpacing = 32f;

    // ── 手柄/键盘光标导航 ─────────────────────────────────────────────────
    private class RowRef
    {
        public RectTransform rt;
        public Image[] outline;
        public SaveSlotMeta meta;
        public bool disabled;
        public MenuButtonHoverFX chromaticFx; // 空存档没有——只有真实存档条目才做色收差效果
    }
    private readonly List<RowRef> _rows = new List<RowRef>();
    private int _cursorIndex = -1;
    private SkyPrisonInputSettings _inputSettings;
    private ScrollRect _scrollRect;
    private RectTransform _viewportRt;
    private RectTransform _contentRt;
    private RawImage _previewImage;
    private Texture2D _previewTexture; // 上一张快照贴图，切换/关闭时要 Destroy 掉，避免每次移动光标都泄漏一张
    private Material _glitchMaterial; // 有快照时用：扫描线 + 偶尔的 RGB 错位故障
    private Material _staticMaterial; // 空存档时用：动态雪花屏（无信号）
    private float _navCooldown;
    private const float NavDelay  = 0.20f;
    private const float NavRepeat = 0.12f;

    // MoveUp/MoveDown 在 SkyPrisonInputSettings.asset 里 gamepadKey 是 None（键盘专用），
    // GetActionDown/GetAction 对手柄输入永远返回 false——跟 PauseMenuController 同款
    // bug，这里也照它已经验证过能用的方案直接读原始轴当兜底。
    private const float NavAxisThreshold = 0.6f;
    private float _prevNavAxisY, _prevNavDpadY;

    private static float SafeAxis(string name)
    {
        try { return Input.GetAxisRaw(name); }
        catch { return 0f; }
    }

    // ── 构建 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 先等当前帧渲染完成、截取真实屏幕画面（不管背后是 Live2D/AE/Canvas UI，
    /// 只要屏幕上显示了就一定截得到），做降采样模糊 + 黑白化，再铺满全屏当窗口背景。
    /// 比"克隆 Main Camera 再实时拍摄"的磨砂方案更可靠：不受 cullingMask 限制，
    /// 也不受"Screen Space Overlay 不会被辅助相机捕获"的限制。
    /// </summary>
    private IEnumerator BuildRoutine()
    {
        yield return new WaitForEndOfFrame();
        _capturedBlurRT = CaptureAndBlurScreen();

        // 字体：与项目其他程序化 UI 一致的加载方式
        var style = FindObjectOfType<SkyPrisonUIGlobalStyleSettings_V1>();
        _font = style?.defaultTextFont;
        if (_font == null) _font = LoadTMPFont("ZhouFangRiMingTi-2 SDF");

        // 编号徽标专用字体：项目里选中的"东亚重工"风格字体（文件名因编码问题显示乱码，
        // 用 GUID 精确定位，不靠文件名字符串）。
        _numberFont = LoadFontByGuid("ea35a3e4c89493f44bb2e5cbe046505c") ?? _font;

        // 按键提示图标数据库：跟 LoadingScreenUI 完全一样的加载方式。
        // 不走 SkyPrisonWindowHintBar，因为那套依赖只有游戏内 HUD 运行时才会
        // 填充的 SkyPrisonQuickItemPromptStrip.RuntimeDatabase（private set，
        // 主菜单场景里没有任何东西会去设置它，永远是 null，只能退化成看不清的文字）。
        LoadIconDatabase();

        // 手柄/键盘输入设置：同 MainMenuController 的加载方式。
        _inputSettings = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");

        // 中日英字典表：资产之前放在 Data/Localization/ 下，不在任何 Resources 文件夹里，
        // 这里的 Resources.Load 一直是拿 null——已经把资产移到 Data/Resources/ 下修好了。
        _locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");

        // 读取所有槽位元数据（只在打开时读一次，语言切换重建 UI 不用重新读盘）
        for (int i = 0; i < GamePaths.SaveSlotCount; i++)
            _metas.Add(SaveManager.ReadSlotMeta(i));

        LocalizationRuntime.OnLanguageChanged += OnLanguageChanged;
        RebuildUI();
    }

    // 之前语言切换这个窗口不会跟着刷新，得关掉重开才看得到新语言。整个 UI 内容都在
    // 一个可丢弃的 Canvas 子物体（_root）下，语言变化时直接整个销毁重建。
    private void OnLanguageChanged(string _) => RebuildUI();

    private void RebuildUI()
    {
        if (_root != null) Destroy(_root);
        _rows.Clear();

        // Canvas
        _root = new GameObject("Canvas");
        _root.transform.SetParent(transform, false);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9950;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.matchWidthOrHeight  = 0.5f;
        _root.AddComponent<GraphicRaycaster>();

        // ── 贴边全屏窗口：背景 = 模糊黑白截图 + 半透黑叠加 ───────────────────
        var panel = MakeRect("WindowPanel", _root.transform, Vector2.zero, Vector2.one);

        if (_capturedBlurRT != null)
        {
            var blurImg = panel.gameObject.AddComponent<RawImage>();
            blurImg.texture      = _capturedBlurRT;
            blurImg.raycastTarget = false;
            // 这条 uvRect 翻转是配合旧版 CaptureScreenshotIntoRenderTexture 加的，换成
            // CaptureScreenshotAsTexture 之后不再需要（CaptureAndBlurScreen 里已经在
            // Blit 阶段翻过一次了，这里如果再翻等于翻两次抵消，纯属误留，删掉。
            var desatShader = Shader.Find("UI/SkyPrison/Desaturate");
            if (desatShader != null)
            {
                var mat = new Material(desatShader) { hideFlags = HideFlags.HideAndDontSave };
                mat.SetFloat("_Saturation", 0f);
                blurImg.material = mat;
            }
        }

        var overlay = MakeRect("DarkOverlay", panel, Vector2.zero, Vector2.one);
        overlay.gameObject.AddComponent<Image>().color = PanelOverlay;

        // 标题（靠左）
        string title = _mode == Mode.Continue
            ? L("ui_saveslot_title_continue", "选择存档")
            : L("ui_saveslot_title_new", "选择存档位");
        var titleTmp = AddTMP(panel, "Title", title, 60, TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Bold);
        Anchor(titleTmp.rectTransform, 0f, 0.90f, 1f, 0.97f);
        titleTmp.rectTransform.offsetMin = new Vector2(96f, 0f);
        titleTmp.rectTransform.offsetMax = new Vector2(-96f, 0f);

        // 标题下分割线（水平方向拉伸满宽，之前锚点写反成了垂直拉伸）
        AddLine(panel, "TitleSep",
            new Vector2(0f, 0.90f), new Vector2(1f, 0.90f), new Vector2(0.5f, 0.90f),
            Vector2.zero, new Vector2(-192f, 1f), SepLine);

        int defaultSlot = FindDefaultSlot();

        // ── 右侧大预览区：显示当前光标所在存档的快照 ─────────────────────
        // 只有一份，不是每行都放缩略图。存档时同步保存的截图（GamePaths.SaveScreenshot）
        // 在光标移动/选择时动态加载显示；没有截图就保持空白，不画占位边框。
        var previewRt = MakeRect("SelectedPreview", panel, Vector2.zero, Vector2.zero);
        Anchor(previewRt, 0.63f, 0.50f, 0.97f, 0.87f);
        _previewImage = previewRt.gameObject.AddComponent<RawImage>();
        _previewImage.raycastTarget = false;
        _previewImage.enabled = false;

        // 电子屏幕质感：常驻横向扫描线 + 偶尔（低频率）触发的小型 RGB 错位故障
        var glitchShader = Shader.Find("UI/SkyPrison/ScreenGlitch");
        if (glitchShader != null)
            _glitchMaterial = new Material(glitchShader) { hideFlags = HideFlags.HideAndDontSave };

        // 空存档预览：动态雪花屏（无信号感），光标停在 NO DATA 槽位时切换到这个材质
        var staticShader = Shader.Find("UI/SkyPrison/TVStatic");
        if (staticShader != null)
            _staticMaterial = new Material(staticShader) { hideFlags = HideFlags.HideAndDontSave };

        _previewImage.material = _glitchMaterial;

        // ── 可滚动的存档列表（ScrollRect，鼠标滚轮/拖拽都能滚）────────────
        var scrollArea = MakeRect("ScrollArea", panel, Vector2.zero, Vector2.zero);
        Anchor(scrollArea, 0.03f, 0.16f, 0.60f, 0.87f);

        var viewport = MakeRect("Viewport", scrollArea, Vector2.zero, Vector2.one);
        // 标准 Unity ScrollView 写法：Mask 需要的 Graphic 用不透明白色，
        // 靠 Mask.showMaskGraphic=false 隐藏渲染，而不是自己拿极低 alpha 蒙混过去
        // （之前用 alpha=0.001 很可能就是那个诡异灰框的来源）。
        viewport.gameObject.AddComponent<Image>().color = Color.white;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        _viewportRt = viewport;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport, false);
        var contentRt = content.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot     = new Vector2(0.5f, 1f);
        _contentRt = contentRt;

        _scrollRect = scrollArea.gameObject.AddComponent<ScrollRect>();
        _scrollRect.viewport        = viewport;
        _scrollRect.content         = contentRt;
        _scrollRect.horizontal      = false;
        _scrollRect.vertical        = true;
        _scrollRect.movementType    = ScrollRect.MovementType.Clamped;
        _scrollRect.scrollSensitivity = 24f;

        // 所有槽位都显示（包括空槽位，显示 NO DATA），不再过滤
        for (int i = 0; i < _metas.Count; i++)
        {
            float y0 = -(i * (RowHeight + RowSpacing));
            BuildSlotRow(contentRt, _metas[i], y0);
        }

        float totalHeight = _metas.Count > 0 ? _metas.Count * RowHeight + (_metas.Count - 1) * RowSpacing : 0f;
        contentRt.sizeDelta = new Vector2(0f, totalHeight);

        // 右上角 × 关闭按钮：直接用实际的关闭图标 PNG（按 GUID 精确加载，
        // 不经过 GlobalStyleSettings/Prefab 查找那层间接引用）。
        BuildCloseButton(panel);

        // 底部按键提示：走全项目共用的 SkyPrisonWindowHintBar，图标解析/设备切换
        // 都是它自己管，这边不用再单独订阅设备切换事件了。
        BuildHintBar(panel);

        // 光标默认停在推荐槽位（默认目标找不到就落在第一个可选行）
        _cursorIndex = defaultSlot >= 0 ? defaultSlot : FirstSelectableRow();
        RefreshCursorVisuals();

        TryBindFonts(_root.transform);
    }

    private int FirstSelectableRow()
    {
        for (int i = 0; i < _rows.Count; i++)
            if (!_rows[i].disabled) return i;
        return 0;
    }

    /// <summary>
    /// 跟 LoadingScreenUI.LoadIconDatabase 完全一样的加载方式：
    /// 优先用游戏内已经填充好的 RuntimeDatabase（如果碰巧存在），
    /// 否则在编辑器里直接按已知资产路径加载数据库资产本身。
    /// </summary>
    private void LoadIconDatabase()
    {
        _iconDb = SkyPrisonQuickItemPromptStrip.RuntimeDatabase
               ?? Resources.Load<SkyPrisonInputPromptIconDatabase>("InputPromptIconDatabase");
    }

    private void BuildCloseButton(RectTransform panel)
    {
        // 关闭图标 PNG 的 GUID（UIWindow_Default_Close.png），按 GUID 精确加载，
        // 不经过 GlobalStyleSettings 组件查找——那个组件只挂在游戏内窗口 Prefab 上，
        // 主菜单场景里根本没有实例，间接引用必然拿不到。
        Sprite closeIcon = LoadSpriteByGuid("1a860d9de75042546ba9c69ed9e23434");

        var btnRt = MakeRect("CloseButton", panel, Vector2.zero, Vector2.zero);
        btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 1f);
        btnRt.pivot     = new Vector2(1f, 1f);
        btnRt.sizeDelta = new Vector2(80f, 80f);
        btnRt.anchoredPosition = new Vector2(-32f, -32f);

        btnRt.gameObject.AddComponent<Image>().color = Color.clear;
        var btn = btnRt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(OnCancel);
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
            // 兜底：真的找不到图标资产时才退回描边框+文字符号
            AddOutline(btnRt, Color.white, 3f);
            var label = AddTMP(btnRt, "Label", "×", 44, TextAlignmentOptions.Center, TextLight, FontStyles.Normal);
            label.raycastTarget = false;
        }
    }

    /// <summary>一个提示 chip 的可动态刷新部分：图标(们)、兜底文字、是不是"确认"chip（需要按
    /// Interact 实际绑定解析，不是固定 key）。</summary>
    // 用全项目共用的 SkyPrisonWindowHintBar（跟背包/设置/暂停窗口一样），不再自己
    // 重写一遍图标解析+设备切换逻辑。
    private void BuildHintBar(RectTransform panel)
    {
        // 不能用 SkyPrisonWindowHint.Action(MoveUp/Interact/Menu, ...)：这几个 action 的
        // gamepadKey 都是 None（键盘专用），这个窗口的手柄导航走的是原始轴/JoystickButton
        // （见 HandleCursorNavigation），不经过这几个 action，用 Action() 解析在手柄模式
        // 下永远找不到手柄图标、整条提示会被隐藏。改成手动配对键鼠图标+手柄图标。
        var hints = new[]
        {
            // 鼠标点击这个动作本身没有对应的手柄按键，手柄模式下自动隐藏（跟背包等
            // 窗口同样的约定），不需要额外处理。
            new SkyPrisonWindowHint { iconKey = "mouse/left", fallbackText = "点击", label = L("ui_saveslot_select_hint", "选择存档") },
            new SkyPrisonWindowHint { iconKey = "keyboard/w", gamepadIconKey = "gamepad/up", fallbackText = "W", label = L("ui_saveslot_move_hint", "移动") },
            new SkyPrisonWindowHint { iconKey = "keyboard/e", gamepadIconKey = "gamepad/xbox/a", fallbackText = "E", label = L("ui_saveslot_click_token", "点击") },
            new SkyPrisonWindowHint { iconKey = "keyboard/esc", gamepadIconKey = "gamepad/xbox/b", fallbackText = "Esc", label = L("ui_saveslot_return", "返回") },
            new SkyPrisonWindowHint { iconKey = "keyboard/delete", gamepadIconKey = "gamepad/xbox/x", fallbackText = "Del", label = L("ui_saveslot_delete_hint", "删除存档") },
        };
        SkyPrisonWindowHintBar.GetOrCreate().Show(hints);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
        {
            OnCancel();
            return;
        }

        HandleCursorNavigation();
    }

    private void HandleCursorNavigation()
    {
        if (_rows.Count == 0) return;

        bool confirm = _inputSettings != null
            ? _inputSettings.GetActionDown(SkyPrisonInputAction.Interact)
            : Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.JoystickButton0);
        if (confirm && _cursorIndex >= 0 && _cursorIndex < _rows.Count)
        {
            var row = _rows[_cursorIndex];
            if (row.disabled)
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden); // 空存档不能读取，光标可以停在这但确认要拒绝
            else
                OnSlotClicked(row.meta);
            return;
        }

        // 删除存档：键盘 Delete / 手柄 X（跟提示条 keyboard/delete + gamepad/xbox/x 对应），
        // 光标停在空槽位上时没有意义，直接忽略。
        bool deleteKey = Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.JoystickButton2);
        if (deleteKey && _cursorIndex >= 0 && _cursorIndex < _rows.Count)
        {
            var row = _rows[_cursorIndex];
            if (!row.disabled && row.meta != null && !row.meta.isEmpty)
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
                ShowDeleteConfirm(row.meta);
            }
            return;
        }

        float axisY = SafeAxis("Vertical");
        float dpadY = SafeAxis("DPadVertical");
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

    private void MoveCursor(int dir)
    {
        if (_rows.Count == 0) return;
        // 空存档（disabled）现在也允许光标停留——只是确认时会被拒绝（见 HandleCursorNavigation），
        // 不再在导航时跳过，这样玩家可以把光标停在空槽位上查看/以后做"雪花屏"预览效果。
        int next = (_cursorIndex + dir + _rows.Count) % _rows.Count;
        if (next == _cursorIndex) return;

        _cursorIndex = next;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        RefreshCursorVisuals();
    }

    private void RefreshCursorVisuals()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            bool isCursor = i == _cursorIndex;
            // disabled 行光标停留时用高亮白（"在这但不能用"），不用冷绿——冷绿在这套规范里
            // 专门代表"可执行的选中态"，空存档确认会被拒绝，不该给出"可以确认"的视觉暗示。
            Color c = row.disabled
                ? (isCursor ? new Color(1f, 1f, 1f, 0.6f) : new Color(1f, 1f, 1f, 0.15f))
                : (isCursor ? ColdGreen : Color.white);
            float thickness = isCursor ? 1.5f : 1f;

            // 跟主菜单按钮一样的色收差 hover 效果，只在有真实存档的行上出现
            // （空槽位跟主菜单里"不可用按钮"一个道理，不该有这个反馈）。
            if (row.chromaticFx != null)
            {
                // 用 OnEnterSilent——SE 已经由 MoveCursor/点击/悬停各自的入口统一播过了，
                // 这里再调用 OnEnter() 会跟着重复播一次 Switch 音效。
                if (isCursor && !row.disabled) row.chromaticFx.OnEnterSilent();
                else row.chromaticFx.OnExit();
            }
            foreach (var line in row.outline)
            {
                if (line == null) continue;
                line.color = c;
                var lrt = line.rectTransform;
                // 沿用原厚度方向（横线改高度、竖线改宽度）
                if (Mathf.Approximately(lrt.sizeDelta.x, 0f))
                    lrt.sizeDelta = new Vector2(lrt.sizeDelta.x, thickness);
                else
                    lrt.sizeDelta = new Vector2(thickness, lrt.sizeDelta.y);
            }
        }

        ScrollToCursor();
        RefreshPreview();
    }

    /// <summary>把光标所在行完整滚动到可视范围内（上下都要露出整行，不能只掉出一半）。</summary>
    private void ScrollToCursor()
    {
        if (_contentRt == null || _viewportRt == null) return;
        if (_cursorIndex < 0 || _cursorIndex >= _rows.Count) return;

        float rowTop    = _cursorIndex * (RowHeight + RowSpacing);
        float rowBottom = rowTop + RowHeight;
        float viewH     = _viewportRt.rect.height;
        float curY      = -_contentRt.anchoredPosition.y; // content 已经滚动掉的高度

        float newY = curY;
        if (rowTop < curY) newY = rowTop;
        else if (rowBottom > curY + viewH) newY = rowBottom - viewH;

        float maxScroll = Mathf.Max(0f, _contentRt.sizeDelta.y - viewH);
        newY = Mathf.Clamp(newY, 0f, maxScroll);

        // 直接改 anchoredPosition 会跟 ScrollRect 自己在 LateUpdate 里做的边界钳制/惯性
        // 打架——它内部认的是 verticalNormalizedPosition，不是我们手算的像素值，两边脱节
        // 就会出现"算是对的，但显示时被拉回去一点，选中行只露出一半"的现象。
        // 改用官方 API 走它自己的换算，才不会跟内部状态冲突。
        if (_scrollRect != null)
        {
            _scrollRect.StopMovement();
            float normalized = maxScroll > 0f ? 1f - Mathf.Clamp01(newY / maxScroll) : 1f;
            _scrollRect.verticalNormalizedPosition = normalized;
        }
        else
        {
            _contentRt.anchoredPosition = new Vector2(_contentRt.anchoredPosition.x, -newY);
        }
    }

    /// <summary>加载并显示光标所在存档槽位的快照（GamePaths.SaveScreenshot）；
    /// 空存档显示动态雪花屏（无信号），既没有快照也不是空存档（异常情况）才隐藏。</summary>
    private void RefreshPreview()
    {
        if (_previewImage == null) return;

        // 先清掉上一张，避免每次切换光标都新建一张 Texture2D 却不释放
        if (_previewTexture != null) { Destroy(_previewTexture); _previewTexture = null; }

        if (_cursorIndex < 0 || _cursorIndex >= _rows.Count) { _previewImage.enabled = false; return; }

        var meta = _rows[_cursorIndex].meta;
        if (meta.isEmpty)
        {
            if (_staticMaterial == null) { _previewImage.enabled = false; return; }
            // 雪花屏是纯程序化噪点，不读贴图内容，随便给一张内置白图当占位即可。
            _previewImage.texture  = Texture2D.whiteTexture;
            _previewImage.material = _staticMaterial;
            _previewImage.enabled  = true;
            return;
        }

        _previewTexture = LoadSaveScreenshot(meta.slot);
        if (_previewTexture == null) { _previewImage.enabled = false; return; }

        _previewImage.texture  = _previewTexture;
        _previewImage.material = _glitchMaterial;
        _previewImage.enabled  = true;
    }

    private static Texture2D LoadSaveScreenshot(int slot)
    {
        string path = GamePaths.SaveScreenshot(slot);
        if (!System.IO.File.Exists(path)) return null;

        try
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            if (!tex.LoadImage(bytes)) { Destroy(tex); return null; }
            return tex;
        }
        catch { return null; }
    }

    private void BuildSlotRow(RectTransform parent, SaveSlotMeta meta, float y0)
    {
        var row = new GameObject($"Slot_{meta.slot}");
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin        = new Vector2(0f, 1f);
        rowRt.anchorMax        = new Vector2(1f, 1f);
        rowRt.pivot            = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, y0);
        rowRt.sizeDelta        = new Vector2(0f, RowHeight);

        row.AddComponent<Image>().color = Color.clear;
        var btn = row.AddComponent<Button>();

        // 空槽位在 Continue 模式下不能读取（没有存档数据）；NewGame 模式下空槽位正常
        // 可点（点击即在这个槽新建游戏）。按钮本身保持 interactable=true——光标/鼠标都
        // 可以停在空槽位上，只是确认时会播放"禁止"音效，而不是把整行禁用到点不了。
        bool disabledForContinue = meta.isEmpty && _mode == Mode.Continue;
        btn.interactable = true;

        // 初始一律白色描边，光标高亮由 RefreshCursorVisuals 统一控制
        // （冷绿只在"当前光标所在行"出现，符合项目"冷绿只在选中/交互态出现"的规则）。
        Color rowOutline = disabledForContinue ? new Color(1f, 1f, 1f, 0.15f) : Color.white;
        var outlineImgs = AddOutline(rowRt, rowOutline, 3f);
        if (!disabledForContinue) SkyPrisonUIButtonFeedback.Attach(row);

        var rowRef = new RowRef { rt = rowRt, outline = outlineImgs, meta = meta, disabled = disabledForContinue };
        _rows.Add(rowRef);

        const float badgeSize = RowHeight - 48f; // 272

        // 编号：不要方框，就是一个铺满这块区域的巨大数字。
        var badgeRt = MakeRect("NumberBadge", rowRt, Vector2.zero, Vector2.zero);
        badgeRt.anchorMin = badgeRt.anchorMax = new Vector2(0f, 0.5f);
        badgeRt.pivot     = new Vector2(0f, 0.5f);
        badgeRt.sizeDelta = new Vector2(badgeSize, badgeSize);
        badgeRt.anchoredPosition = new Vector2(24f, 0f);
        var badgeNum = AddTMP(badgeRt, "Num", meta.slot.ToString("00"), 168,
            TextAlignmentOptions.Center, meta.isEmpty ? TextDisabled : new Color(1f, 1f, 1f, 0.55f), FontStyles.Bold);
        badgeNum.raycastTarget = false;
        if (_numberFont != null) badgeNum.font = _numberFont;

        // 快照现在只在右侧大预览区显示一份（当前选中的存档），每行不再单独放缩略图。
        float textLeft = 24f + badgeSize + 48f;

        if (meta.isEmpty)
        {
            var noData = AddTMP(rowRt, "NoData", L("ui_saveslot_no_data", "NO DATA"), 44,
                TextAlignmentOptions.MidlineLeft, TextDisabled, FontStyles.Bold);
            AnchorPixelLeft(noData.rectTransform, textLeft, 0.1f, 0.9f);
            noData.raycastTarget = false;
        }
        else
        {
            var label = AddTMP(rowRt, "Label", SlotLabel(meta), 44,
                TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Normal);
            AnchorPixelLeft(label.rectTransform, textLeft, 0.5f, 0.92f);
            label.raycastTarget = false;

            var status = AddTMP(rowRt, "Status", StatusLine(meta), 30,
                TextAlignmentOptions.MidlineLeft, TextFaint, FontStyles.Normal);
            AnchorPixelLeft(status.rectTransform, textLeft, 0.10f, 0.5f);
            status.raycastTarget = false;

            // 色收差偏移层（跟主菜单 MakeMenuButton 一模一样的做法，同一个 MenuButtonHoverFX
            // 组件驱动）——整行都要有效果，编号/名字/时间戳三组文字都各配一对红蓝副本，
            // 共用同一个 FX 组件的时间轴，光标/鼠标停在这一行时一起生效。只有真实存档
            // 条目才有，空槽位没有。
            var fx = row.AddComponent<MenuButtonHoverFX>();

            var labelR = AddTMP(rowRt, "Label_R", SlotLabel(meta), 44,
                TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Normal);
            AnchorPixelLeft(labelR.rectTransform, textLeft, 0.5f, 0.92f);
            labelR.raycastTarget = false;

            var labelB = AddTMP(rowRt, "Label_B", SlotLabel(meta), 44,
                TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Normal);
            AnchorPixelLeft(labelB.rectTransform, textLeft, 0.5f, 0.92f);
            labelB.raycastTarget = false;

            fx.Init(btn, label, labelR, (RectTransform)labelR.transform, labelB, (RectTransform)labelB.transform);

            var badgeR = AddTMP(badgeRt, "Num_R", badgeNum.text, 168,
                TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            badgeR.raycastTarget = false;
            if (_numberFont != null) badgeR.font = _numberFont;

            var badgeB = AddTMP(badgeRt, "Num_B", badgeNum.text, 168,
                TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
            badgeB.raycastTarget = false;
            if (_numberFont != null) badgeB.font = _numberFont;

            fx.AddLayer(badgeNum, badgeR, (RectTransform)badgeR.transform, badgeB, (RectTransform)badgeB.transform);

            var statusR = AddTMP(rowRt, "Status_R", StatusLine(meta), 30,
                TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Normal);
            AnchorPixelLeft(statusR.rectTransform, textLeft, 0.10f, 0.5f);
            statusR.raycastTarget = false;

            var statusB = AddTMP(rowRt, "Status_B", StatusLine(meta), 30,
                TextAlignmentOptions.MidlineLeft, Color.white, FontStyles.Normal);
            AnchorPixelLeft(statusB.rectTransform, textLeft, 0.10f, 0.5f);
            statusB.raycastTarget = false;

            fx.AddLayer(status, statusR, (RectTransform)statusR.transform, statusB, (RectTransform)statusB.transform);

            rowRef.chromaticFx = fx;

            // 删除存档：只有非空槽位才需要，放右下角，纯文字小字（自己的透明 Image 会
            // 挡住底下整行那个大 Button 的射线检测，点删除不会连带触发"选中这个槽位"）。
            var deleteRt = MakeRect("DeleteButton", rowRt, new Vector2(1f, 0f), new Vector2(1f, 0f));
            deleteRt.pivot = new Vector2(1f, 0f);
            deleteRt.sizeDelta = new Vector2(200f, 48f);
            deleteRt.anchoredPosition = new Vector2(-16f, 12f);
            deleteRt.gameObject.AddComponent<Image>().color = Color.clear; // 只用来接住点击，不显示任何底框
            var deleteBtn = deleteRt.gameObject.AddComponent<Button>();
            var deleteLabel = AddTMP(deleteRt, "DeleteLabel", L("ui_saveslot_delete", "删除存档"), 28,
                TextAlignmentOptions.MidlineRight, Color.white, FontStyles.Normal);
            deleteLabel.raycastTarget = false;
            SkyPrisonUIButtonFeedback.Attach(deleteRt.gameObject);
            var capturedForDelete = meta;
            deleteBtn.onClick.AddListener(() =>
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
                ShowDeleteConfirm(capturedForDelete);
            });
        }

        var captured = meta;
        int rowIndex = meta.slot;

        // 鼠标悬停时把光标同步过来，跟键盘/手柄共用同一套选中态（含空槽位，
        // 只是空槽位描边是灰白色而不是冷绿——同一套规则，之前只有点击才会同步）。
        var trigger = row.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var pointerEnter = new UnityEngine.EventSystems.EventTrigger.Entry
        {
            eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter
        };
        pointerEnter.callback.AddListener(_ =>
        {
            if (_cursorIndex == rowIndex) return;
            _cursorIndex = rowIndex;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            RefreshCursorVisuals();
        });
        trigger.triggers.Add(pointerEnter);

        btn.onClick.AddListener(() =>
        {
            _cursorIndex = rowIndex;
            RefreshCursorVisuals();
            if (disabledForContinue)
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            else
                OnSlotClicked(captured);
        });
    }

    /// <summary>本地化版的状态行，替代 SaveSlotMeta.StatusLine——
    /// 位置名称走 MapLocalizationRegistry 解析成地图编辑器里配置的多语言名称，
    /// 而不是直接显示原始的 mapId/chapterId 字符串。</summary>
    private string StatusLine(SaveSlotMeta meta)
    {
        if (meta.isEmpty) return L("ui_saveslot_no_data", "NO DATA");

        string loc;
        if (string.IsNullOrEmpty(meta.chapterId))
        {
            loc = "基地"; // TODO: 待"基地"本身也有多语言名称需求时再接字典表
        }
        else
        {
            string sceneKey = !string.IsNullOrEmpty(meta.mapId) ? meta.mapId : meta.chapterId;
            var registry = MapLocalizationRegistry.LoadOrNull();
            loc = registry != null ? registry.GetDisplayName(sceneKey, meta.chapterId) : meta.chapterId;
        }

        return $"{meta.lastSaved}  |  {loc}  |  {meta.playTimeText}";
    }

    /// <summary>本地化版的槽位名称，替代 SaveSlotMeta.SlotLabel（那个是硬编码中文，
    /// 存档系统本身不接触本地化表，翻译放在 UI 层这里做）。5 个槽位地位完全平等，
    /// 不再有"0 号是自动存档"这种特殊标签（怪物猎人式"一个角色一份存档"设计）。</summary>
    private string SlotLabel(SaveSlotMeta meta) =>
        string.Format(L("ui_saveslot_slot_format", "存档 {0}"), meta.slot);

    private int FindDefaultSlot()
    {
        if (_mode == Mode.Continue)
        {
            // 5 个槽位地位平等，默认选"最近一次存档时间最新"的那个，不再硬优先 0 号。
            int best = -1;
            System.DateTime bestTime = System.DateTime.MinValue;
            for (int i = 0; i < _metas.Count; i++)
            {
                if (_metas[i].isEmpty) continue;
                if (!System.DateTime.TryParse(_metas[i].lastSaved, out System.DateTime t))
                    t = System.DateTime.MinValue;
                if (best < 0 || t > bestTime)
                {
                    best = i;
                    bestTime = t;
                }
            }
            return best;
        }
        else
        {
            for (int i = 0; i < _metas.Count; i++) // 5 个槽位都能选，不再跳过 0 号
                if (_metas[i].isEmpty) return i;
            return 0; // 全满就默认槽0（需覆盖确认）
        }
    }

    // ── 交互 ──────────────────────────────────────────────────────────────

    private void OnSlotClicked(SaveSlotMeta meta)
    {
        if (_mode == Mode.Continue)
        {
            Confirm(meta.slot);
            return;
        }

        if (!meta.isEmpty)
            ShowOverwriteConfirm(meta);
        else
            Confirm(meta.slot);
    }

    /// <summary>覆盖确认用悬浮小窗（不是全屏），这里才需要磨砂式的白色角标做视觉区隔。</summary>
    private void ShowOverwriteConfirm(SaveSlotMeta meta)
    {
        if (_confirmOverlay != null) return;

        _confirmOverlay = new GameObject("OverwriteConfirm");
        _confirmOverlay.transform.SetParent(_root.transform, false);

        var dim = MakeRect("Dim", _confirmOverlay.transform, Vector2.zero, Vector2.one);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(() => { });

        var box = MakeRect("Box", _confirmOverlay.transform, Vector2.zero, Vector2.zero);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot     = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(1040f, 520f);

        box.gameObject.AddComponent<Image>().color = PanelOverlay;
        AddCornerBrackets(box, Color.white, 40f, 4f);

        string overwriteTitle = string.Format(
            L("ui_saveslot_overwrite_title_format", "覆盖 {0}？"), SlotLabel(meta));
        var titleTmp = AddTMP(box, "Title", overwriteTitle, 40,
            TextAlignmentOptions.Center, DangerText, FontStyles.Bold);
        Anchor(titleTmp.rectTransform, 0f, 0.68f, 1f, 0.88f);

        var bodyTmp = AddTMP(box, "Body", StatusLine(meta), 28,
            TextAlignmentOptions.Center, TextFaint, FontStyles.Normal);
        Anchor(bodyTmp.rectTransform, 0.06f, 0.42f, 0.94f, 0.66f);

        MakeButton(box, "Confirm", new Vector2(0.08f, 0.10f), new Vector2(0.48f, 0.30f),
            L("ui_saveslot_overwrite_confirm", "覆盖并开始"), DangerText,
            () => { Destroy(_confirmOverlay); _confirmOverlay = null; Confirm(meta.slot); });

        MakeButton(box, "Cancel", new Vector2(0.52f, 0.10f), new Vector2(0.92f, 0.30f),
            L("ui_saveslot_return", "返回"), TextLight,
            () => { Destroy(_confirmOverlay); _confirmOverlay = null; });

        TryBindFonts(_confirmOverlay.transform);
    }

    /// <summary>删除存档确认——跟覆盖确认同一套悬浮小窗样式，删除不可撤销，按钮走危险色。</summary>
    private void ShowDeleteConfirm(SaveSlotMeta meta)
    {
        if (_confirmOverlay != null) return;

        _confirmOverlay = new GameObject("DeleteConfirm");
        _confirmOverlay.transform.SetParent(_root.transform, false);

        var dim = MakeRect("Dim", _confirmOverlay.transform, Vector2.zero, Vector2.one);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(() => { });

        var box = MakeRect("Box", _confirmOverlay.transform, Vector2.zero, Vector2.zero);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot     = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(1040f, 520f);

        box.gameObject.AddComponent<Image>().color = PanelOverlay;
        AddCornerBrackets(box, Color.white, 40f, 4f);

        string deleteTitle = string.Format(
            L("ui_saveslot_delete_title_format", "删除 {0}？"), SlotLabel(meta));
        var titleTmp = AddTMP(box, "Title", deleteTitle, 40,
            TextAlignmentOptions.Center, DangerText, FontStyles.Bold);
        Anchor(titleTmp.rectTransform, 0f, 0.68f, 1f, 0.88f);

        var bodyTmp = AddTMP(box, "Body",
            L("ui_saveslot_delete_body", "删除后无法恢复，确定要删除这份存档吗？"), 28,
            TextAlignmentOptions.Center, TextFaint, FontStyles.Normal);
        Anchor(bodyTmp.rectTransform, 0.06f, 0.42f, 0.94f, 0.66f);

        MakeButton(box, "Confirm", new Vector2(0.08f, 0.10f), new Vector2(0.48f, 0.30f),
            L("ui_saveslot_delete_confirm", "确认删除"), DangerText,
            () =>
            {
                Destroy(_confirmOverlay);
                _confirmOverlay = null;
                SaveManager.DeleteSave(meta.slot);
                // _metas 是打开窗口那一刻缓存的快照，RebuildUI() 只会重新摆一遍 UI，
                // 不会自己重新读盘——删除之后必须先把这一格的元数据换成"空"，
                // 不然界面还会显示删除前的旧数据。
                if (meta.slot >= 0 && meta.slot < _metas.Count)
                    _metas[meta.slot] = SaveManager.ReadSlotMeta(meta.slot);
                RebuildUI();
            });

        MakeButton(box, "Cancel", new Vector2(0.52f, 0.10f), new Vector2(0.92f, 0.30f),
            L("ui_saveslot_return", "返回"), TextLight,
            () => { Destroy(_confirmOverlay); _confirmOverlay = null; });

        TryBindFonts(_confirmOverlay.transform);
    }

    private void Confirm(int slot)
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
        var cb = _onConfirm;
        Close();
        cb?.Invoke(slot);
    }

    private void OnCancel()
    {
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
        var cb = _onCancel;
        Close();
        cb?.Invoke();
    }

    private void Close()
    {
        _instance = null;
        IsOpen = false;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
        SkyPrisonWindowHintBar.GetOrCreate().Clear();

        if (_capturedBlurRT != null)
        {
            _capturedBlurRT.Release();
            Destroy(_capturedBlurRT);
            _capturedBlurRT = null;
        }

        if (_previewTexture != null)
        {
            Destroy(_previewTexture);
            _previewTexture = null;
        }

        if (_glitchMaterial != null) { Destroy(_glitchMaterial); _glitchMaterial = null; }
        if (_staticMaterial != null) { Destroy(_staticMaterial); _staticMaterial = null; }
    }

    /// <summary>
    /// 截取当前屏幕画面，做多级降采样再升采样制造模糊（双线性过滤天然带模糊效果，
    /// 不需要额外的高斯 shader）。返回的 RenderTexture 由调用方负责在 OnDestroy 里释放。
    ///
    /// 模糊强度基准与 SkyPrisonInventoryBlurBackground 保持一致：
    /// 先把源图缩小到长边 960（SrcLongEdge），再从这个基准继续降采样，
    /// 而不是直接从全屏分辨率往下降——否则同样的降采样级数在不同分辨率下模糊强度不一样。
    /// </summary>
    private static RenderTexture CaptureAndBlurScreen()
    {
        // 用 CaptureScreenshotAsTexture，不要手动按 Screen.width/height 建 RT 再用
        // CaptureScreenshotIntoRenderTexture——高 DPI 缩放下两者尺寸对不上，画面会
        // 挤到左下角、其余留黑（项目标准写法，见 PauseMenuController/SettingsWindowUI）。
        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        int w = Mathf.Max(4, shot.width);
        int h = Mathf.Max(4, shot.height);

        var full = new RenderTexture(w, h, 0, RenderTextureFormat.DefaultHDR) { hideFlags = HideFlags.HideAndDontSave };
        full.Create();
        // 这里之前用的是 CaptureScreenshotIntoRenderTexture（方向正确），换成
        // CaptureScreenshotAsTexture + Blit 之后画面上下翻了过来，翻回来纠正。
        // 注意：PauseMenuController/SettingsWindowUI 从一开始就用的是这套新方法，
        // 本来就是正的，不需要也不能加这个翻转（加了反而会把它们弄反）。
        Graphics.Blit(shot, full, new Vector2(1f, -1f), new Vector2(0f, 1f));
        Destroy(shot);

        // 第一步：缩小到与背包磨砂一致的基准长边（960），模糊强度换算基准统一
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

        // 第二步：从基准分辨率继续降采样（与背包磨砂 blurHalfSteps 默认值 6 一致）
        const int blurHalfSteps = 6;
        RenderTexture src = baseRT;
        int curW = baseW, curH = baseH;
        for (int i = 0; i < blurHalfSteps; i++)
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

    // ── UI 工具（与 PlayerDeathReviveUI / 背包窗口一致的规范实现）─────────

    private static RectTransform MakeRect(string name, Transform parent, Vector2 amin, Vector2 amax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = amin; rt.anchorMax = amax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
    {
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// <summary>左边缘用像素偏移（给缩略图让位），右边缘、上下用比例。</summary>
    private static void AnchorPixelLeft(RectTransform rt, float leftPixels, float y0, float y1)
    {
        rt.anchorMin = new Vector2(0f, y0);
        rt.anchorMax = new Vector2(1f, y1);
        rt.offsetMin = new Vector2(leftPixels, 0f);
        rt.offsetMax = new Vector2(-32f, 0f);
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

    /// <summary>透明底 + 1px 白色外框 + ButtonFeedback（项目按钮规范）。</summary>
    private static void MakeButton(RectTransform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, string label, Color textColor, Action onClick)
    {
        var rt = MakeRect(name, parent, anchorMin, anchorMax);
        rt.gameObject.AddComponent<Image>().color = Color.clear;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick?.Invoke());

        AddOutline(rt, Color.white, 3f);
        SkyPrisonUIButtonFeedback.Attach(rt.gameObject);

        var lbl = AddTMP(rt, "Label", label, 36, TextAlignmentOptions.Center, textColor, FontStyles.Normal);
        lbl.raycastTarget = false;
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

    private static void AddLine(RectTransform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
        => AddLineRT(parent, name, anchorMin, anchorMax, pivot, pos, size, c);

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
        {
            // "Num" 编号徽标（含色收差用的 Num_R/Num_B 副本）用专门的东亚重工字体，
            // 不能被这里统一覆盖掉——之前只排除了 "Num"，Num_R/Num_B 被换成了通用字体，
            // 字形对不上主数字，看起来就是位置和字体都不对。
            if (t.gameObject.name == "Num" || t.gameObject.name == "Num_R" || t.gameObject.name == "Num_B") continue;
            t.font = _font;
        }
    }

    /// <summary>
    /// 按 GUID 精确加载字体资产，绕开文件名乱码问题（这个字体文件名因编码问题
    /// 在文件系统里显示为乱码字符，没法用文件名字符串安全定位）。
    /// 只在 Editor 里能这样查；打包后需要保证该字体被打进 Resources 或直接引用，
    /// 这里退回 null 由调用方兜底到默认字体。
    /// </summary>
    private static TMP_FontAsset LoadFontByGuid(string guid)
    {
#if UNITY_EDITOR
        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(path))
            return UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
#endif
        return null;
    }

    /// <summary>按 GUID 精确加载 Sprite 资产（编辑器专用，打包后需保证走 Resources 镜像）。</summary>
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
        // 同 SettingsWindowUI 那份——Build 里没有 AssetDatabase，之前这里缺 Resources
        // 兜底，关闭按钮在 Build 里一直拿不到真实图标。
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
