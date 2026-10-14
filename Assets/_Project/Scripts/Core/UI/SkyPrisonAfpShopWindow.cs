using System.Collections;
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// AFP 购买窗口「ADAPTATION SYSTEM」：在 AFP 购买区里按 E 打开。
/// 外观完全走项目统一的悬浮窗套件 SkyPrisonFloatingWindowKit（跟背包 / 角色面板同一套）：
///   · 实时磨砂背景(辅助相机对窗口正后方的世界做高斯模糊 + 去色，亮度同背包)，所以底下的东西有「倒影」，颜色深浅跟其它窗口一致；
///   · 四角角标、标题栏(可拖动)、右上角关闭图标；
///   · 打开 / 关闭都是「从中间一条横线展开 / 压成中间一条横线」(scaleY)。
/// 内容：标题栏右侧是 AFP 滚动计数器；中间七项抗性升级(每行：名字+当前效果 / 20 格等级 / 价格按钮)，
/// 项目多了超出时右侧出现统一样式的滚动条。价格是全局价：买任何一项之后所有项一起涨。
/// 世界不停(不冻结时间)，但窗口开着时角色定住、攻击/闪避被挡(W/S 选行不能同时走路)；
/// 离开购买区、进入战斗、按背包/角色面板/任务日志/地图键，窗口都会自动关闭。
/// </summary>
public sealed class SkyPrisonAfpShopWindow : MonoBehaviour
{
    static SkyPrisonAfpShopWindow _instance;
    public static bool IsOpen => _instance != null;
    static int _lastCloseFrame = -1;
    public static bool JustClosedThisFrame => Time.frameCount == _lastCloseFrame;
    public static Transform ActiveRoot => _instance != null ? _instance.transform : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() { _instance = null; _lastCloseFrame = -1; }

    public static void Show(SkyPrisonAfpPurchaseZone zone = null)
    {
        if (_instance != null) return;
        var go = new GameObject("[AfpShopWindow]");
        _instance = go.AddComponent<SkyPrisonAfpShopWindow>();
        _instance._zone = zone;
        _instance.StartCoroutine(_instance.BuildRoutine());
    }

    public static void Hide() { if (_instance != null) _instance.Close(); }

    // ── 配色/尺寸 ─────────────────────────────────────────────
    const int SortingOrder = 31900;                       // 跟角色面板/背包同级：压过 HUD，不抢暂停菜单/设置
    const float PanelW = 2400f, PanelH = 1500f;      // 2000 太窄：能力块和 −/+ 胶囊挤在一起，加宽到 2400
    static readonly Color White = Color.white;
    static readonly Color Faint = new Color(1f, 1f, 1f, 0.55f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.28f);
    static readonly Color ColdGreen = new Color(0.42f, 0.92f, 0.68f, 1f);

    // 里程碑能力块：一个升级项下面可以挂 1~2 个(物理攻击强化 → 回转 + 致命子弹)，两个时上下叠成紧凑版
    class Chip
    {
        public RectTransform root;
        public Image icon; public TMP_Text name, a, b, outT;
        public ChipAnim anim; public int shownTier = -1; public bool compact;
    }

    class Row
    {
        public SkyPrisonAfpUpgrades.Def def;
        public Image bg, selectBar, ring;
        public TMP_Text name, effect, valSign, newSign, valInt, valFrac, newInt, newFrac, pendingText, minusLabel, plusLabel;
        public Image previewArrow;
        public PopFx pop;
        public RectTransform newGroup;
        public Chip[] chips = new Chip[0];
        public int lastPend;
        public RectTransform rt;
    }

    readonly List<Row> _rows = new List<Row>();
    int _cursor;
    bool _built, _savedExternalBlock, _scrollToCursor;
    TMP_FontAsset _font;
    SkyPrisonInputSettings _input;
    SkyPrisonAfpPurchaseZone _zone;
    RectTransform _boxRt;
    SkyPrisonBlurUVTracker _blurTracker;
    readonly Vector3[] _corners = new Vector3[4];
    ScrollRect _scroll; RectTransform _scrollView, _scrollContent;
    TMP_Text _totalText, _totalCost, _confirmText, _clearText;
    RectTransform _totalClip;
    float _totalW, _mqStart;
    string _mqText = "";
    Image[] _confirmOutline, _clearOutline;
    BarBtnFx _confirmFx, _clearFx;
    float _prevDpadX, _prevAxisX;
    System.Func<string, string, string> _L;

    // AFP 滚动计数器(跟 HUD 右下角那个同一套做法)：七位，每位两层数字上下滑动
    const int Digits = 7;
    const float DigitFont = 56f, CellW = 38f, CellH = 70f, BaselineY = 14f, LabelW = 100f;
    const float BaselineShift = BaselineY - CellH * 0.5f;
    static readonly string[] DigitStr = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
    static readonly Color ZeroColor = new Color(0.52f, 0.54f, 0.57f, 0.9f);
    static readonly Color SpendColor = new Color(1f, 0.45f, 0.45f, 1f);
    static readonly Color GainColor = new Color(0.35f, 1f, 0.7f, 1f);
    TMP_Text[] _da, _db; RectTransform[] _daRt, _dbRt; int[] _dla, _dlb;
    float _dTarget, _dShown, _gainPulse, _spendPulse; bool _dSnap = true, _dDirty = true;

    IEnumerator BuildRoutine()
    {
        DontDestroyOnLoad(gameObject);
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);

        // 世界不停，但要挡住玩法输入(攻击/闪避/交互)；移动由 SkyPrisonPlayerInputRouter 单独定住
        _savedExternalBlock = SkyPrisonWindowManager_V1.ExternalBlock;
        SkyPrisonWindowManager_V1.ExternalBlock = true;
        HideCombatHud();                       // 窗口开着时下面的战斗 HUD(HP/LP 条、快捷栏)暂时淡出，跟背包/角色面板一致
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        _font = SkyPrisonFloatingWindowKit.LoadTMPFont("ZhouFangRiMingTi-2 SDF");
        _input = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");
        var table = Resources.Load<UILocalizationTable>("UILocalizationTable");
        _L = (k, f) => table != null ? table.Get(k, f) : f;
        yield return null;

        BuildUI();
        _openedInSession = AFPRuntime.Instance != null && AFPRuntime.Instance.IsInSession;
        SkyPrisonAfpUpgrades.OnChanged += Refresh;
        AFPRuntime.OnAFPChanged += OnAfpChanged;
        Refresh();
        ShowHints();
        yield return StartCoroutine(FoldRoutine(0f, 1f));
        _built = true;
    }

    // ── 构建 ─────────────────────────────────────────────────
    void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var rootRt = (RectTransform)transform;

        var boxGo = new GameObject("Box", typeof(RectTransform));
        boxGo.transform.SetParent(rootRt, false);
        _boxRt = (RectTransform)boxGo.transform;
        _boxRt.anchorMin = _boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        _boxRt.pivot = new Vector2(0.5f, 0.5f);
        _boxRt.anchoredPosition = Vector2.zero;
        _boxRt.sizeDelta = new Vector2(PanelW, PanelH);
        _boxRt.localScale = new Vector3(1f, 0f, 1f);                       // 先收成中间一条横线，打开时从线展开

        // 磨砂背景(实时) + 角标 + 标题栏(可拖动) + 右上角关闭图标：全部走统一套件
        SkyPrisonFloatingWindowKit.BuildBlurBackground(this, _boxRt, out _blurTracker);
        SkyPrisonFloatingWindowKit.AddCornerBrackets(_boxRt);
        SkyPrisonFloatingWindowKit.BuildTitleBar(_boxRt, "ADAPTATION SYSTEM", _font, out _);
        SkyPrisonFloatingWindowKit.BuildCloseButton(_boxRt, Close, _font);

        BuildOdometer(_boxRt);

        // 说明一行(标题栏下)
        var sub = AddText(_boxRt, "Sub", _L("ui_afp_shop_sub", "适应性调整仅在本次部署内有效。每完成一级强化，后续所有项目的 AFP 消耗同步上调。离开作业区或检测到交战时，系统自动断开。"), 32, TextAlignmentOptions.MidlineLeft, Faint, FontStyles.Normal);
        var sr = sub.rectTransform;
        sr.anchorMin = new Vector2(0f, 1f); sr.anchorMax = new Vector2(1f, 1f); sr.pivot = new Vector2(0.5f, 1f);
        sr.anchoredPosition = new Vector2(0f, -(SkyPrisonFloatingWindowKit.TitleBarHeight + 6f));
        sr.sizeDelta = new Vector2(-120f, 56f);
        // 日文比中英文长，一行放不下就自动缩小字号(最小 22)，不超出窗口
        sub.enableWordWrapping = false; sub.overflowMode = TextOverflowModes.Overflow;
        sub.enableAutoSizing = true; sub.fontSizeMax = 32; sub.fontSizeMin = 22;

        // 行容器：占标题/说明以下的整块；项目多了超出就出滚动条
        var box = MakeRect("RowsBox", _boxRt, Vector2.zero, Vector2.one);
        box.offsetMin = new Vector2(60f, 200f);                // 底部留给「合计 + 清除 + 强化」栏
        box.offsetMax = new Vector2(-60f, -(SkyPrisonFloatingWindowKit.TitleBarHeight + 76f));

        var scrollRt = MakeRect("ScrollArea", box, Vector2.zero, Vector2.one);
        scrollRt.gameObject.AddComponent<RectMask2D>();
        var scrollBg = scrollRt.gameObject.AddComponent<Image>(); scrollBg.color = Color.clear;   // 让空白处也能拖动/滚轮
        var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 60f;

        var content = MakeRect("Rows", scrollRt, new Vector2(0f, 1f), new Vector2(1f, 1f));
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 14f;
        layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;
        scroll.viewport = scrollRt;
        _scroll = scroll; _scrollView = scrollRt; _scrollContent = content;

        SkyPrisonUIScrollbar.AttachVertical(scroll, box, ColdGreen, rightMargin: 0f, topMargin: 0f, bottomMargin: 0f,
            visibility: ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport);

        var defs = SkyPrisonAfpUpgrades.All;
        for (int i = 0; i < defs.Length; i++) _rows.Add(BuildRow(content, defs[i], i));
        BuildBottomBar(_boxRt);

        foreach (var t in rootRt.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (_font != null) t.font = _font;
        var numFont = Resources.Load<TMP_FontAsset>("UI/HUD/MRT-すくてらむ SDF");
        if (numFont != null && _da != null)
            for (int i = 0; i < Digits; i++) { _da[i].font = numFont; _db[i].font = numFont; }
    }

    Row BuildRow(RectTransform parent, SkyPrisonAfpUpgrades.Def def, int index)
    {
        var r = new Row { def = def };
        var rowRt = MakeRect("Row" + index, parent, Vector2.zero, Vector2.one);
        r.rt = rowRt;
        rowRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 140f;
        r.bg = rowRt.gameObject.AddComponent<Image>();
        r.bg.color = new Color(1f, 1f, 1f, 0.03f);
        int captured = index;
        var rowBtn = rowRt.gameObject.AddComponent<Button>();
        rowBtn.transition = Selectable.Transition.None;
        rowBtn.onClick.AddListener(() => { _cursor = captured; _scrollToCursor = true; Refresh(); });

        // 条目框里的背景装饰：名字那一角一小片正方形网格，从左上角往右下渐变淡出(画在文字下面，不挡点击)
        var gridRt = MakeRect("GridDecor", rowRt, new Vector2(0f, 1f), new Vector2(0f, 1f));
        gridRt.pivot = new Vector2(0f, 1f);
        gridRt.anchoredPosition = Vector2.zero;
        gridRt.sizeDelta = new Vector2(640f, 140f);
        gridRt.gameObject.AddComponent<CanvasRenderer>();     // 自定义 Graphic 不会自动补这个组件，缺了整块都不会画
        var gridG = gridRt.gameObject.AddComponent<GridFadeGraphic>(); gridG.raycastTarget = false;

        // 选中时左侧一条竖线(冷绿)
        var bar = MakeRect("SelectBar", rowRt, new Vector2(0f, 0f), new Vector2(0f, 1f));
        bar.sizeDelta = new Vector2(8f, 0f); bar.pivot = new Vector2(0f, 0.5f);
        r.selectBar = bar.gameObject.AddComponent<Image>(); r.selectBar.color = ColdGreen; r.selectBar.raycastTarget = false;

        // 名字(上半) + 当前效果(下半)，各占固定区域，不重叠
        r.name = AddText(rowRt, "Name", "", 50, TextAlignmentOptions.BottomLeft, White, FontStyles.Bold);
        Anchor(r.name.rectTransform, 0f, 0.46f, 0.36f, 0.97f);
        r.name.rectTransform.offsetMin = new Vector2(48f, 0f); r.name.rectTransform.offsetMax = Vector2.zero;
        r.effect = AddText(rowRt, "Effect", "", 32, TextAlignmentOptions.TopLeft, Faint, FontStyles.Normal);
        Anchor(r.effect.rectTransform, 0f, 0.03f, 0.36f, 0.44f);
        r.effect.rectTransform.offsetMin = new Vector2(48f, 0f); r.effect.rectTransform.offsetMax = Vector2.zero;
        r.name.raycastTarget = false; r.effect.raycastTarget = false;

        // 中间：大号当前数值，按「小数点」对齐——整数部分右对齐到固定的小数点位置，小数点及之后左对齐，
        // 所以位数不同时只有左边多出几个数字，小数部分始终在原位，一眼比大小。有预加点时接「图形箭头 + 新数值(冷绿)」，新数值同样按自己的小数点对齐。
        const float ValueFont = 60f, PointX = 150f, ArrowX = 262f, NewPointLocal = 140f;
        var valArea = MakeRect("ValueArea", rowRt, new Vector2(0.27f, 0.04f), new Vector2(0.54f, 0.96f));
        valArea.offsetMin = valArea.offsetMax = Vector2.zero;
        r.valInt = MakeValueText(valArea, "Int", ValueFont, TextAlignmentOptions.MidlineRight, White, new Vector2(1f, 0.5f), PointX, PointX);
        r.valFrac = MakeValueText(valArea, "Frac", ValueFont, TextAlignmentOptions.MidlineLeft, White, new Vector2(0f, 0.5f), PointX, 220f);
        r.valSign = MakeValueText(valArea, "Sign", ValueFont, TextAlignmentOptions.MidlineLeft, White, new Vector2(0f, 0.5f), 8f, 50f);        // 加号固定在最左一列，各行上下对齐
        var arrowRt = MakeRect("PreviewArrow", valArea, Vector2.zero, Vector2.zero);
        arrowRt.anchorMin = arrowRt.anchorMax = new Vector2(0f, 0.5f); arrowRt.pivot = new Vector2(0f, 0.5f);
        arrowRt.sizeDelta = new Vector2(38f, 32f); arrowRt.anchoredPosition = new Vector2(ArrowX, 0f);
        r.previewArrow = arrowRt.gameObject.AddComponent<Image>();
        r.previewArrow.sprite = GetPreviewArrowSprite(); r.previewArrow.color = ColdGreen; r.previewArrow.raycastTarget = false;
        var newGroup = MakeRect("NewValue", valArea, Vector2.zero, Vector2.zero);
        newGroup.anchorMin = newGroup.anchorMax = new Vector2(0f, 0.5f); newGroup.pivot = new Vector2(0f, 0.5f);
        newGroup.sizeDelta = new Vector2(NewPointLocal + 220f, 100f); newGroup.anchoredPosition = new Vector2(ArrowX + 38f + 14f, 0f);
        r.newInt = MakeValueText(newGroup, "Int", ValueFont, TextAlignmentOptions.MidlineRight, ColdGreen, new Vector2(1f, 0.5f), NewPointLocal, NewPointLocal);
        r.newFrac = MakeValueText(newGroup, "Frac", ValueFont, TextAlignmentOptions.MidlineLeft, ColdGreen, new Vector2(0f, 0.5f), NewPointLocal, 220f);
        r.newSign = MakeValueText(newGroup, "Sign", ValueFont, TextAlignmentOptions.MidlineLeft, ColdGreen, new Vector2(0f, 0.5f), 8f, 50f);
        r.pop = newGroup.gameObject.AddComponent<PopFx>();
        r.previewArrow.gameObject.SetActive(false); newGroup.gameObject.SetActive(false);
        r.newGroup = newGroup;

        // 里程碑能力块：能力名 + 「已达成的这一档」一行 + 「下一档预告」一行。档位提升时，已达成的旧内容向上切走，下一档冒上来变成已达成并绿色闪一下，再往下冒出新的下一档预告
        int chipCount = Mathf.Clamp(SkyPrisonAfpAbilityTable.FindAllForUpgrade(def.key).Count, 0, 2);
        r.chips = new Chip[chipCount];
        for (int ci = 0; ci < chipCount; ci++)
        {
            bool compact = chipCount > 1;
            float y0 = compact ? (ci == 0 ? 0.52f : 0.04f) : 0.08f, y1 = compact ? (ci == 0 ? 0.96f : 0.48f) : 0.92f;
            r.chips[ci] = BuildChip(rowRt, "Ability" + ci, y0, y1, compact);
        }

        // 预加点：商店购买同款的细边胶囊「－  数量  ＋」(只改「待强化」的等级，不花钱；最后在底部统一强化)
        var cap = MakeRect("Capsule", rowRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
        cap.pivot = new Vector2(1f, 0.5f); cap.sizeDelta = new Vector2(360f, 100f); cap.anchoredPosition = new Vector2(-48f, 0f);
        var ringRt = MakeRect("Ring", cap, Vector2.zero, Vector2.one);
        r.ring = ringRt.gameObject.AddComponent<Image>();
        r.ring.sprite = GetCapsuleRing(); r.ring.type = Image.Type.Sliced; r.ring.color = White; r.ring.raycastTarget = false;

        // 左 30%：减    中 40%：数量    右 30%：加(跟商店一样的三段比例)
        r.minusLabel = BuildCapsuleButton(cap, "Minus", "−", 0f, 0.3f, true, () => { _cursor = captured; RemovePending(); });
        r.plusLabel = BuildCapsuleButton(cap, "Plus", "+", 0.7f, 1f, false, () => { _cursor = captured; AddPending(); });
        r.pendingText = AddText(cap, "Pending", "0", 48, TextAlignmentOptions.Center, Dim, FontStyles.Bold);
        var pr = r.pendingText.rectTransform;
        pr.anchorMin = new Vector2(0.3f, 0f); pr.anchorMax = new Vector2(0.7f, 1f); pr.offsetMin = pr.offsetMax = Vector2.zero;
        r.pendingText.raycastTarget = false;
        return r;
    }

    // 胶囊里的一段可点按钮(减/加)：透明点击区 + 绿色高亮(悬停淡绿、点击亮一下)，标签是它的文字
    TMP_Text BuildCapsuleButton(RectTransform capsule, string name, string label, float x0, float x1, bool left, UnityEngine.Events.UnityAction onClick)
    {
        var rt = MakeRect(name, capsule, new Vector2(x0, 0f), new Vector2(x1, 1f));
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.gameObject.AddComponent<Image>().color = Color.clear;
        var b = rt.gameObject.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(onClick);

        var text = AddText(rt, "Label", label, 56, TextAlignmentOptions.Center, White, FontStyles.Normal);
        text.raycastTarget = false;
        var fx = rt.gameObject.AddComponent<GlowFx>(); fx.label = text;
        return text;
    }

    // 绿色高亮：悬停淡淡泛绿，点击瞬间亮一下再衰减(跟 SkyPrisonUIButtonFeedback 同一套冷绿)。
    // 不用现成的 SkyPrisonUIButtonFeedback，因为它启动时记住各图形的「原色」、悬停结束再还原，
    // 而这个窗口的按钮颜色是每帧按状态(能不能点/选没选中)重算的，两边会互相覆盖——这里只动一层独立的高亮图。
    sealed class GlowFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public TMP_Text label;
        public Color baseColor = White; public bool usable;
        bool hover; float flash, mix;
        public void OnPointerEnter(PointerEventData e) { hover = true; }
        public void OnPointerExit(PointerEventData e) { hover = false; }
        public void OnPointerClick(PointerEventData e) { flash = 1f; }
        void Update()
        {
            if (label == null) return;
            float dt = Time.unscaledDeltaTime;
            if (flash > 0f) flash = Mathf.Max(0f, flash - dt / 0.35f);
            float target = usable ? Mathf.Max(hover ? 1f : 0f, flash) : 0f;
            mix = Mathf.MoveTowards(mix, target, dt / 0.1f);
            label.color = Color.Lerp(baseColor, ColdGreen, mix);     // 只有符号自己白→绿，没有任何填充块
        }
    }

    // 胶囊细边环 Sprite(九宫格，左右圆头)：运行时程序生成。贴图 128x64、PPU=64，Canvas 参考 PPU=100，
    // 所以显示高度 = 64*100/64 = 100 单位，圆头半径正好是 50，不会被拉成椭圆。
    static Sprite _ringSprite;
    static Sprite GetCapsuleRing()
    {
        if (_ringSprite != null) return _ringSprite;
        const int R = 32, T = 2, W = 4 * R, H = 2 * R;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, R, W - R);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, R));     // 到胶囊中心线(线段)的距离
                float a = Mathf.Clamp01(Mathf.Min(R - d + 0.5f, d - (R - T) + 0.5f));                  // 抗锯齿的环
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        _ringSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 64f, 0, SpriteMeshType.FullRect, new Vector4(R, 0, R, 0));
        return _ringSprite;
    }

    // 底部栏：合计 + 清除 + 强化
    void BuildBottomBar(RectTransform box)
    {
        var bar = MakeRect("BottomBar", box, new Vector2(0f, 0f), new Vector2(1f, 0f));
        bar.offsetMin = new Vector2(60f, 40f); bar.offsetMax = new Vector2(-60f, 170f);
        AddLine(bar, "TopLine", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(0f, 1f), new Color(1f, 1f, 1f, 0.15f));

        // 合计文字放在一个裁切区里(不压到右边两个按钮)：第一行「本次提升 …」太长就在这一行范围内来回滚动，第二行花费固定
        _totalClip = MakeRect("TotalClip", bar, new Vector2(0f, 0f), new Vector2(1f, 1f));
        _totalClip.offsetMin = new Vector2(48f, 0f); _totalClip.offsetMax = new Vector2(-(392f + 340f + 40f), 0f);
        _totalClip.gameObject.AddComponent<RectMask2D>();
        _totalText = AddText(_totalClip, "Total", "", 40, TextAlignmentOptions.MidlineLeft, White, FontStyles.Normal);
        _totalText.raycastTarget = false; _totalText.enableWordWrapping = false; _totalText.overflowMode = TextOverflowModes.Overflow;
        _totalCost = AddText(_totalClip, "TotalCost", "", 40, TextAlignmentOptions.MidlineLeft, White, FontStyles.Normal);
        _totalCost.raycastTarget = false; _totalCost.enableWordWrapping = false; _totalCost.overflowMode = TextOverflowModes.Ellipsis;
        SetTotalLayout(false);

        // 两个按钮一样大；「强化」长按 1 秒才生效(防误点)，「清除」单击
        _clearFx = MakeBarButton(bar, "Clear", _L("ui_afp_clear", "清除"), 340f, -392f, false, out _clearOutline, out _clearText, ClearPending);
        _confirmFx = MakeBarButton(bar, "Confirm", _L("ui_afp_confirm", "完成部署"), 340f, -28f, true, out _confirmOutline, out _confirmText, Commit);
    }

    // 两行(有预加点)：上面「本次提升」，下面「花费」；没有预加点时只有一行装饰文字，占满整个高度
    void SetTotalLayout(bool two)
    {
        var tr = _totalText.rectTransform;
        tr.anchorMin = new Vector2(0f, two ? 0.5f : 0f); tr.anchorMax = new Vector2(0f, 1f); tr.pivot = new Vector2(0f, 0.5f);
        tr.anchoredPosition = new Vector2(0f, 0f); tr.sizeDelta = new Vector2(Mathf.Max(10f, _totalW), 0f);
        var cr = _totalCost.rectTransform;
        cr.anchorMin = new Vector2(0f, 0f); cr.anchorMax = new Vector2(1f, 0.5f); cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero;
        _totalCost.gameObject.SetActive(two);
    }

    // 第一行超出裁切区时：停一下 → 向左滚到末尾 → 停一下 → 滚回来，循环
    void TickMarquee()
    {
        if (_totalText == null || _totalClip == null) return;
        float cw = _totalClip.rect.width, travel = _totalW - cw;
        float x = 0f;
        if (travel > 1f)
        {
            const float pause = 1.1f, speed = 160f;
            float move = travel / speed, cycle = (pause + move) * 2f;
            float t = (Time.unscaledTime - _mqStart) % cycle;
            if (t < pause) x = 0f;
            else if (t < pause + move) x = -travel * ((t - pause) / move);
            else if (t < pause * 2f + move) x = -travel;
            else x = -travel * (1f - (t - pause * 2f - move) / move);
        }
        var p = _totalText.rectTransform.anchoredPosition;
        if (!Mathf.Approximately(p.x, x)) _totalText.rectTransform.anchoredPosition = new Vector2(x, p.y);
    }

    // 完成部署之后：刚强化的那几行在冷绿色里「呼吸」几下
    readonly System.Collections.Generic.HashSet<string> _breathKeys = new System.Collections.Generic.HashSet<string>();
    float _breathStart = -99f;
    const float BreathSeconds = 3.4f;

    void TickBreath()
    {
        if (_breathKeys.Count == 0) return;
        float t = Time.unscaledTime - _breathStart;
        if (t >= BreathSeconds) { _breathKeys.Clear(); Refresh(); return; }
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BreathSeconds - 1f, BreathSeconds, t));
        float wave = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / 1.1f - Mathf.PI * 0.5f);
        for (int i = 0; i < _rows.Count; i++)
        {
            if (!_breathKeys.Contains(_rows[i].def.key)) continue;
            _rows[i].bg.color = new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, (0.07f + 0.30f * wave) * fade + 0.03f * (1f - fade));
        }
    }

    BarBtnFx MakeBarButton(RectTransform bar, string name, string label, float w, float xFromRight, bool holdable, out Image[] outline, out TMP_Text text, UnityEngine.Events.UnityAction onClick)
    {
        var rt = MakeRect(name, bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
        rt.pivot = new Vector2(1f, 0.5f); rt.sizeDelta = new Vector2(w, 100f); rt.anchoredPosition = new Vector2(xFromRight, 0f);
        rt.gameObject.AddComponent<Image>().color = Color.clear;
        outline = AddOutline(rt, White, 3f);
        text = AddText(rt, "Label", label, 48, TextAlignmentOptions.Center, White, FontStyles.Normal);
        text.raycastTarget = false;
        var fx = rt.gameObject.AddComponent<BarBtnFx>();
        fx.outline = outline; fx.label = text; fx.holdable = holdable; fx.onFire = onClick;
        if (holdable)
        {
            // 长按进度：冷绿线从上边正中顺时针绕一圈(跟出发按钮同一套)
            fx.segs = new RectTransform[5];
            var specs = new[] { new[]{0.5f,1f,0f,1f}, new[]{1f,1f,1f,1f}, new[]{1f,0f,1f,0f}, new[]{0f,0f,0f,0f}, new[]{0f,1f,0f,1f} };
            for (int k = 0; k < 5; k++)
            {
                var sr = MakeRect("HoldSeg" + k, rt, Vector2.zero, Vector2.zero);
                sr.anchorMin = sr.anchorMax = new Vector2(specs[k][0], specs[k][1]); sr.pivot = new Vector2(specs[k][2], specs[k][3]);
                sr.anchoredPosition = Vector2.zero; sr.sizeDelta = Vector2.zero;
                var si = sr.gameObject.AddComponent<Image>(); si.color = ColdGreen; si.raycastTarget = false;
                fx.segs[k] = sr;
            }
        }
        return fx;
    }

    // 底部按钮：没填充，只是「白→绿」(悬停变绿)；长按按钮按住时绿线绕框一圈，满了才触发
    sealed class BarBtnFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public Image[] outline; public TMP_Text label; public RectTransform[] segs;
        public bool holdable, enabledState, keyHold;
        public UnityEngine.Events.UnityAction onFire;
        bool hover, mouseHold, fired; float progress, mix;
        const float HoldSeconds = 1f, ReleaseSeconds = 0.3f;
        public void OnPointerEnter(PointerEventData e) { hover = true; }
        public void OnPointerExit(PointerEventData e) { hover = false; mouseHold = false; }
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            mouseHold = true;
            if (holdable && !enabledState && onFire != null) onFire();   // 灰着按下：只在按下的瞬间给一次「禁止」音效，按住不再重复
        }
        public void OnPointerUp(PointerEventData e) { mouseHold = false; }
        public void OnPointerClick(PointerEventData e) { if (!holdable && onFire != null) onFire(); }
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool pressing = mouseHold || keyHold;
            if (!pressing) fired = false;                                   // 松手后才能再触发一次(一次按住只触发一次)
            bool holding = holdable && enabledState && pressing && !fired;
            if (holding) progress = Mathf.Min(1f, progress + dt / HoldSeconds);
            else if (fired) progress = 0f;                                  // 触发后绿线立刻清空刷新，不是慢慢退回去
            else progress = Mathf.Max(0f, progress - dt / ReleaseSeconds);  // 中途松手才匀速回弹
            if (holdable && progress >= 1f && !fired) { fired = true; progress = 0f; if (onFire != null) onFire(); }
            // 长按按钮跟出发按钮一样是个「槽」：悬停只让框稍微亮一点，绿色只留给按出来的进度(否则悬停就全绿，进度绕框根本看不出来)
            float target = (enabledState && hover && !holdable) ? 1f : 0f;
            mix = Mathf.MoveTowards(mix, target, dt / 0.12f);
            var baseC = enabledState ? (holdable && !hover && progress <= 0f ? new Color(1f, 1f, 1f, 0.6f) : White) : Dim;
            var lineC = Color.Lerp(baseC, ColdGreen, mix);                                   // 框：普通按钮悬停变绿；长按按钮保持白/灰，绿线由进度画
            var textC = Color.Lerp(baseC, ColdGreen, Mathf.Max(mix, progress));              // 字：长按时随进度渐变成绿
            foreach (var o in outline) if (o != null) o.color = lineC;
            if (label != null) label.color = textC;
            if (segs == null || segs.Length < 5) return;
            var rt = (RectTransform)transform;
            float w = rt.rect.width, h = rt.rect.height, half = w * 0.5f, th = 3f;
            float d = progress * (2f * w + 2f * h);
            float[] ls = { Mathf.Clamp(d, 0f, half), Mathf.Clamp(d - half, 0f, h), Mathf.Clamp(d - half - h, 0f, w), Mathf.Clamp(d - half - h - w, 0f, h), Mathf.Clamp(d - half - h - w - h, 0f, half) };
            for (int k = 0; k < 5; k++)
            {
                bool side = (k == 1 || k == 3);
                segs[k].sizeDelta = side ? new Vector2(th, ls[k]) : new Vector2(ls[k], th);
                segs[k].gameObject.SetActive(ls[k] > 0.01f);
            }
        }
    }

    void BuildOdometer(RectTransform box)
    {
        float totalW = LabelW + Digits * CellW;
        float rightInset = SkyPrisonFloatingWindowKit.CloseButtonSize + SkyPrisonFloatingWindowKit.CloseButtonMargin + 60f;
        var host = MakeRect("AfpOdometer", box, new Vector2(1f, 1f), new Vector2(1f, 1f));
        host.pivot = new Vector2(1f, 0.5f);
        host.sizeDelta = new Vector2(totalW, CellH);
        host.anchoredPosition = new Vector2(-rightInset, -SkyPrisonFloatingWindowKit.TitleBarHeight * 0.5f);

        var label = AddText(host, "Label", "AFP", 34, TextAlignmentOptions.BaselineLeft, White, FontStyles.Normal);
        var lr = label.rectTransform;
        lr.anchorMin = lr.anchorMax = new Vector2(0f, 0.5f); lr.pivot = new Vector2(0f, 0.5f);
        lr.sizeDelta = new Vector2(LabelW, 60f); lr.anchoredPosition = new Vector2(0f, BaselineShift);
        label.raycastTarget = false;

        _da = new TMP_Text[Digits]; _db = new TMP_Text[Digits];
        _daRt = new RectTransform[Digits]; _dbRt = new RectTransform[Digits];
        _dla = new int[Digits]; _dlb = new int[Digits];
        for (int i = 0; i < Digits; i++)
        {
            _dla[i] = _dlb[i] = -1;
            var cell = new GameObject("Digit" + i, typeof(RectTransform), typeof(RectMask2D));
            cell.transform.SetParent(host, false);
            var cr = (RectTransform)cell.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0f, 0.5f); cr.pivot = new Vector2(0f, 0.5f);
            cr.anchoredPosition = new Vector2(LabelW + (Digits - 1 - i) * CellW, 0f);   // i=0 是个位，在最右
            cr.sizeDelta = new Vector2(CellW, CellH);
            _da[i] = MakeDigit(cr, "A", out _daRt[i]);
            _db[i] = MakeDigit(cr, "B", out _dbRt[i]);
        }
    }

    TMP_Text MakeDigit(RectTransform parent, string name, out RectTransform rt)
    {
        var t = AddText(parent, name, "0", DigitFont, TextAlignmentOptions.Baseline, White, FontStyles.Normal);
        t.raycastTarget = false;
        rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(CellW, CellH); rt.anchoredPosition = new Vector2(0f, BaselineShift);
        return t;
    }

    void ShowHints()
    {
        var hints = new[]
        {
            new SkyPrisonWindowHint { iconKey = "keyboard/w", gamepadIconKey = "gamepad/up", fallbackText = "W", label = _L("ui_afp_hint_select", "选择") },
            new SkyPrisonWindowHint { iconKey = "keyboard/d", gamepadIconKey = "gamepad/right", fallbackText = "D", label = _L("ui_afp_hint_adjust", "预加点 A−/D+") },
            new SkyPrisonWindowHint { iconKey = "keyboard/e", gamepadIconKey = "gamepad/xbox/a", fallbackText = "E", label = _L("ui_afp_hint_confirm", "完成部署") },
            new SkyPrisonWindowHint { iconKey = "keyboard/esc", gamepadIconKey = "gamepad/xbox/b", fallbackText = "Esc", label = _L("ui_afp_hint_close", "关闭") },
        };
        SkyPrisonWindowHintBar.GetOrCreate().Show(hints);
    }

    // ── 开/关动画：跟背包/角色面板一致——从中间一条横线展开，关闭时压成中间一条横线 ──────────
    IEnumerator FoldRoutine(float from, float to)
    {
        if (_boxRt == null) yield break;
        float dur = SkyPrisonFloatingWindowKit.OpenCloseAnimDuration;
        if (to < from && _blurTracker != null) _blurTracker.Frozen = true;   // 关闭动画期间冻结磨砂采样区，不然它跟着缩放变形
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            if (_boxRt == null) yield break;
            float p = Mathf.Clamp01(t / dur);
            float k = to > from ? 1f - (1f - p) * (1f - p) : 1f - p * p;      // 展开 ease-out(有弹出感)；收起 ease-in(越收越快)
            _boxRt.localScale = new Vector3(1f, Mathf.Max(0.002f, k), 1f);
            yield return null;
        }
        if (_boxRt != null) _boxRt.localScale = new Vector3(1f, Mathf.Max(0.002f, to), 1f);
    }

    // ── 刷新 ─────────────────────────────────────────────────
    void OnAfpChanged(int _) => Refresh();

    void Refresh()
    {
        if (AFPRuntime.Instance != null) SetAfpTarget(AFPRuntime.Instance.CurrentAFP);
        int pendingLevels = SkyPrisonAfpUpgrades.PendingLevels;
        int pendingCost = SkyPrisonAfpUpgrades.PendingCost;
        bool affordable = SkyPrisonAfpUpgrades.PendingAffordable;
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            var d = r.def;
            bool rowVisible = SkyPrisonAfpUpgrades.IsVisible(d);
            if (r.rt.gameObject.activeSelf != rowVisible) r.rt.gameObject.SetActive(rowVisible);
            if (!rowVisible) continue;
            bool rowUnlocked = SkyPrisonAfpUpgrades.IsUnlocked(d);
            int lv = SkyPrisonAfpUpgrades.Level(d);
            int pend = SkyPrisonAfpUpgrades.Pending(d);
            bool sel = i == _cursor;
            r.bg.color = sel ? new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, 0.16f) : new Color(1f, 1f, 1f, 0.03f);
            r.selectBar.enabled = sel;
            r.selectBar.color = ColdGreen;
            // 名字后面接小号等级：已强化几次一眼可见；有预加点显示预加之后的等级(冷绿)，满级写 MAX
            string lvTag;
            if (lv + pend >= d.maxLevel) lvTag = "<color=#C9A8FFFF>MAX</color>";
            else if (pend > 0) lvTag = "<color=#7CFFC0FF>Lv." + (lv + pend) + "</color>";
            else lvTag = "<color=#FFFFFF" + (lv > 0 ? "B3" : "47") + ">Lv." + lv + "</color>";
            r.name.text = _L(d.labelKey, d.labelFallback) + "  <size=60%><b>" + lvTag + "</b></size>";
            r.name.color = rowUnlocked ? White : new Color(1f, 1f, 1f, 0.45f);

            // 中间大号数值：当前值，有预加点接「箭头 + 新值」；新值每次增加弹一下
            string unit = UnitText(d);
            bool fracItem = d.perLevel % 1f != 0f;   // 篡夺者(valueAt)的每级量也带小数(6.7)，一样固定显示一位小数，% 号才和其他百分比行对在同一列
            SplitValue(SkyPrisonAfpUpgrades.ValueAt(d, lv), unit, fracItem, out string curInt, out string curFrac);
            r.valSign.text = curInt.Length > 0 ? curInt.Substring(0, 1) : ""; r.valInt.text = curInt.Length > 0 ? curInt.Substring(1) : ""; r.valFrac.text = curFrac;
            var curColor = (lv > 0 || pend > 0) ? White : Dim;
            r.valInt.color = curColor; r.valFrac.color = curColor; r.valSign.color = curColor;
            bool showPrev = pend > 0;
            r.previewArrow.gameObject.SetActive(showPrev); r.newGroup.gameObject.SetActive(showPrev);
            if (showPrev)
            {
                SplitValue(SkyPrisonAfpUpgrades.ValueAt(d, lv + pend), unit, fracItem, out string nInt, out string nFrac);
                r.newSign.text = nInt.Length > 0 ? nInt.Substring(0, 1) : ""; r.newInt.text = nInt.Length > 0 ? nInt.Substring(1) : ""; r.newFrac.text = nFrac;
                if (pend > r.lastPend) r.pop.Pop();
            }
            r.lastPend = pend;

            // 里程碑能力(含预加点之后会解锁的)
            ApplyAbility(r, d, lv, lv + pend);

            // 左下小字：下一级能多拿多少 + 价格(买不起价格淡红)；满级只写 MAX
            bool maxed = lv + pend >= d.maxLevel;
            if (maxed) r.effect.text = _L("ui_afp_max", "MAX");
            else if (!rowUnlocked) r.effect.text = d.lockedHint != null ? d.lockedHint() : "";       // 条件没到：灰着，写缺什么
            else
            {
                string price = SkyPrisonAfpUpgrades.MarginalPrice(d).ToString();
                if (!SkyPrisonAfpUpgrades.CanAddPending(d)) price = "<color=#FF7373FF>" + price + "</color>";
                float nextDelta = SkyPrisonAfpUpgrades.ValueAt(d, lv + pend + 1) - SkyPrisonAfpUpgrades.ValueAt(d, lv + pend);
                r.effect.text = string.Format(_L("ui_afp_next_fmt", "下一级 +{0}%"), nextDelta.ToString("0.#")).Replace("%", unit) + "　·　" + price + " AFP";
            }
            r.effect.color = Faint;

            r.pendingText.text = pend > 0 ? "+" + pend : "0";
            r.pendingText.color = pend > 0 ? ColdGreen : Dim;
            bool canAdd = SkyPrisonAfpUpgrades.CanAddPending(d);
            SetGlyph(r.minusLabel, pend > 0 ? White : Dim, pend > 0);
            SetGlyph(r.plusLabel, canAdd ? (sel ? ColdGreen : White) : Dim, canAdd);
            r.ring.color = (pend > 0 || canAdd) ? White : Dim;
        }

        if (_totalText != null)
        {
            if (pendingLevels > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < _rows.Count; i++)
                {
                    int pn = SkyPrisonAfpUpgrades.Pending(_rows[i].def);
                    if (pn <= 0) continue;
                    if (sb.Length > 0) sb.Append("　");
                    var pd = _rows[i].def; int pl = SkyPrisonAfpUpgrades.Level(pd);
                    sb.Append(_L(pd.labelKey, pd.labelFallback)).Append(" +").Append((SkyPrisonAfpUpgrades.ValueAt(pd, pl + pn) - SkyPrisonAfpUpgrades.ValueAt(pd, pl)).ToString("0.#")).Append(UnitText(pd));
                }
                string cost = string.Format(_L("ui_afp_cost_fmt", "花费 {0} AFP"), pendingCost);
                if (!affordable) cost = "<color=#FF7373FF>" + cost + "</color>";
                SetMarquee(_L("ui_afp_summary", "本次提升") + "　" + sb.ToString(), true);
                _totalCost.text = cost;
            }
            else SetMarquee("POWERED BY KYOGOKU", false);      // 装饰性系统文案：固定英文、不进字典、不随语言切换(跟 ADAPTATION SYSTEM 同类)
            _totalText.color = pendingLevels > 0 ? White : Dim;
        }
        if (_confirmFx != null) _confirmFx.enabledState = pendingLevels > 0 && affordable;
        if (_clearFx != null) _clearFx.enabledState = pendingLevels > 0;
    }

    void SetMarquee(string text, bool two)
    {
        if (_mqText != text)
        {
            _mqText = text; _totalText.text = text;
            _totalText.ForceMeshUpdate();
            _totalW = _totalText.preferredWidth + 8f;
            _mqStart = Time.unscaledTime;
        }
        SetTotalLayout(two);
    }

    // 数值拆成「整数部分」和「小数点及之后(+单位)」：例 2.5% → "+2" 和 ".5%"；20 发 → "+20" 和 " 发"
    // forceDecimal=true：这一项每级是小数(0.5%)，不管当前值是不是整数都固定显示一位小数(5.0%)，这样 % 号也在同一列对齐
    static void SplitValue(float v, string unit, bool forceDecimal, out string left, out string right)
    {
        string t = v.ToString(forceDecimal ? "0.0" : "0.#", System.Globalization.CultureInfo.InvariantCulture);
        int dot = t.IndexOf('.');
        left = "+" + (dot < 0 ? t : t.Substring(0, dot));
        right = (dot < 0 ? "" : t.Substring(dot)) + unit;
    }

    TMP_Text MakeValueText(RectTransform parent, string name, float size, TextAlignmentOptions align, Color color, Vector2 pivot, float x, float width)
    {
        var t = AddText(parent, name, "", size, align, color, FontStyles.Bold);
        t.raycastTarget = false; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); rt.pivot = pivot;
        rt.anchoredPosition = new Vector2(x, 0f); rt.sizeDelta = new Vector2(width, 100f);
        return t;
    }

    string UnitText(SkyPrisonAfpUpgrades.Def d) => d.unit == "{r}" ? _L("ui_afp_unit_rounds", " 发") : d.unit;

    // ── 里程碑能力展示(数据来自 SkyPrisonAfpAbilityTable：每档自己的效果文字和科技树节点) ─────────────
    TMP_Text MakeChipText(RectTransform ab, string name, float size, TextAlignmentOptions align, FontStyles style, float y0, float y1, float inset = 78f)
    {
        var t = AddText(ab, name, "", size, align, White, style);
        t.raycastTarget = false; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis;
        Anchor(t.rectTransform, 0f, y0, 1f, y1); t.rectTransform.offsetMin = new Vector2(inset, 0f);
        return t;
    }

    Chip BuildChip(RectTransform rowRt, string objName, float y0, float y1, bool compact)
    {
        var c = new Chip { compact = compact };
        var ab = MakeRect(objName, rowRt, new Vector2(0.52f, y0), new Vector2(0.78f, y1));
        ab.offsetMin = ab.offsetMax = Vector2.zero;
        c.root = ab;
        float iconSize = compact ? 44f : 64f, inset = compact ? 58f : 78f;
        var iconRt = MakeRect("Icon", ab, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        iconRt.pivot = new Vector2(0f, 0.5f); iconRt.sizeDelta = new Vector2(iconSize, iconSize); iconRt.anchoredPosition = Vector2.zero;
        c.icon = iconRt.gameObject.AddComponent<Image>(); c.icon.raycastTarget = false;
        if (compact)
        {
            // 紧凑版：名字一行 + 一行说明(已达成的效果；没达成就写下一档预告)
            c.name = MakeChipText(ab, "Name", 24, TextAlignmentOptions.BottomLeft, FontStyles.Bold, 0.50f, 1f, inset);
            c.a = MakeChipText(ab, "EffA", 19, TextAlignmentOptions.TopLeft, FontStyles.Normal, 0.0f, 0.50f, inset);
            c.b = MakeChipText(ab, "EffB", 19, TextAlignmentOptions.TopLeft, FontStyles.Normal, 0.0f, 0.50f, inset);
            c.outT = MakeChipText(ab, "EffOut", 19, TextAlignmentOptions.TopLeft, FontStyles.Normal, 0.0f, 0.50f, inset);
        }
        else
        {
            c.name = MakeChipText(ab, "Name", 28, TextAlignmentOptions.BottomLeft, FontStyles.Bold, 0.60f, 1f, inset);
            c.a = MakeChipText(ab, "EffA", 22, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 0.30f, 0.60f, inset);
            c.b = MakeChipText(ab, "EffB", 20, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 0.0f, 0.30f, inset);
            c.outT = MakeChipText(ab, "EffOut", 22, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, 0.30f, 0.60f, inset);
        }
        c.outT.text = "";
        c.anim = ab.gameObject.AddComponent<ChipAnim>();
        c.anim.a = c.a; c.anim.b = c.b; c.anim.outText = c.outT;
        ab.gameObject.SetActive(false);
        return c;
    }

    void ApplyAbility(Row r, SkyPrisonAfpUpgrades.Def d, int lvNow, int lvPreview)
    {
        var list = SkyPrisonAfpAbilityTable.FindAllForUpgrade(d.key);
        for (int i = 0; i < r.chips.Length && i < list.Count; i++) ApplyChip(r.chips[i], list[i], lvPreview);
    }

    void ApplyChip(Chip c, SkyPrisonAfpAbilityTable.AbilityDef a, int lvPreview)
    {
        bool has = a != null && SkyPrisonAfpAbilityTable.Visible(a);        // 第 1 档科技树节点没点亮：整块不显示
        c.root.gameObject.SetActive(has);
        if (!has) { c.shownTier = -1; return; }

        int tier = SkyPrisonAfpAbilityTable.TierAtLevel(a, lvPreview);
        bool maxed = a.MultiTier && tier >= a.maxTier;                          // 多档能力满档：淡紫
        var purple = SkyPrisonAfpIcons.MaxTierColor;

        string nm = a.name();
        if (tier > 0 && a.MultiTier) nm += " " + SkyPrisonAfpAbilityTable.TierName(tier);
        string lineA = tier > 0 ? a.shortEffect(tier) : "";

        // 下一档预告：下一档的节点没点亮就不显示(不剧透)
        string lineB = "";
        if (tier < a.maxTier && SkyPrisonAfpAbilityTable.TierUnlocked(a, tier + 1))
        {
            int need = SkyPrisonAfpAbilityTable.LevelsToNext(a, lvPreview);
            string eff = a.shortEffect(tier + 1);
            lineB = (a.MultiTier ? SkyPrisonAfpAbilityTable.TierName(tier + 1) + " > " : "") + eff
                  + (need > 0 ? "  " + string.Format(_L("ui_afp_ab_need_short_fmt", "再 {0} 级"), need) : "");
        }
        // 紧凑版只有一行说明：已达成就写效果，否则写下一档预告
        if (c.compact) { if (tier <= 0) { lineA = lineB; } lineB = ""; }

        var iconC = tier <= 0 ? new Color(1f, 1f, 1f, 0.22f) : maxed ? purple : ColdGreen;
        var nameC = tier <= 0 ? new Color(1f, 1f, 1f, 0.45f) : maxed ? purple : White;
        var aC = maxed ? new Color(purple.r, purple.g, purple.b, 0.9f) : new Color(1f, 1f, 1f, tier > 0 ? 0.85f : 0.38f);
        var bC = new Color(1f, 1f, 1f, 0.38f);

        var anim = c.anim;
        if (c.shownTier >= 0 && tier > c.shownTier && anim != null)
            anim.Play(c.a.text, c.a.color);                                    // 档位提升：旧的向上切走，新的冒上来
        c.icon.sprite = GetAbilitySprite(a.icon); c.icon.color = iconC;
        c.name.text = nm; c.name.color = nameC;
        c.a.text = lineA; c.b.text = lineB;
        if (anim != null) { anim.aFinal = aC; anim.bFinal = bC; }
        if (anim == null || !anim.Playing) { c.a.color = aC; c.b.color = bC; }
        c.shownTier = tier;
    }

    // 档位提升的切换动画：旧的「已达成」向上滑出并淡出；新的「已达成」从下面升上来、绿色闪一下再落回正常色；下一档预告稍晚一点升上来
    internal sealed class ChipAnim : MonoBehaviour
    {
        public TMP_Text a, b, outText;
        public Color aFinal = Color.white, bFinal = Color.white;
        float _t = 1f; Color _outColor;
        const float Duration = 0.55f, Rise = 46f;
        public bool Playing => _t < 1f;

        public void Play(string oldAText, Color oldAColor)
        {
            if (outText != null) { outText.text = oldAText; _outColor = oldAColor; }
            _t = 0f;
        }

        static void SetY(TMP_Text t, float y) { if (t != null) { var ap = t.rectTransform.anchoredPosition; t.rectTransform.anchoredPosition = new Vector2(ap.x, y); } }   // 保留横向偏移(文字起点 78)，只动竖直位置
        static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }

        void Update()
        {
            if (_t >= 1f) return;
            _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / Duration);
            float k = Ease(_t), kb = Ease((_t - 0.25f) / 0.75f);
            if (outText != null) { SetY(outText, Mathf.Lerp(0f, Rise, k)); outText.color = new Color(_outColor.r, _outColor.g, _outColor.b, _outColor.a * (1f - k)); }
            var green = new Color(0.42f, 0.92f, 0.68f, 1f);
            if (a != null) { SetY(a, Mathf.Lerp(-Rise, 0f, k)); var c = Color.Lerp(green, aFinal, Mathf.SmoothStep(0.2f, 1f, _t)); c.a = Mathf.Lerp(0f, aFinal.a, k); a.color = c; }
            if (b != null) { SetY(b, Mathf.Lerp(-Rise, 0f, kb)); b.color = new Color(bFinal.r, bFinal.g, bFinal.b, bFinal.a * kb); }
            if (_t >= 1f)
            {
                SetY(a, 0f); SetY(b, 0f); SetY(outText, 0f);
                if (a != null) a.color = aFinal; if (b != null) b.color = bFinal;
                if (outText != null) outText.text = "";
            }
        }
    }

    static Sprite GetAbilitySprite(int kind) => SkyPrisonAfpIcons.GetSprite(kind);

    // 正方形网格：左上角最亮，沿对角线往右下渐变到 0；每条线按小段拆开，让顶点透明度跟着位置变
    sealed class GridFadeGraphic : MaskableGraphic
    {
        const float Cell = 28f, Thick = 1.6f, Seg = 14f, MaxAlpha = 0.20f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float w = r.width, h = r.height;
            for (float x = 0f; x <= w + 0.01f; x += Cell)
                for (float y = 0f; y < h; y += Seg)
                    Quad(vh, r, x - Thick * 0.5f, y, x + Thick * 0.5f, Mathf.Min(h, y + Seg), w, h);
            for (float y = 0f; y <= h + 0.01f; y += Cell)
                for (float x = 0f; x < w; x += Seg)
                    Quad(vh, r, x, y - Thick * 0.5f, Mathf.Min(w, x + Seg), y + Thick * 0.5f, w, h);
        }

        float AlphaAt(float x, float y, float w, float h)
        {
            float t = Mathf.Clamp01((x / w + y / h) * 0.5f * 1.25f);       // 0 = 左上角，1 = 右下
            return MaxAlpha * Mathf.Pow(1f - t, 1.8f);
        }

        void Quad(VertexHelper vh, Rect r, float x0, float y0, float x1, float y1, float w, float h)
        {
            // (x, y) 以左上角为原点向右 / 向下
            int i = vh.currentVertCount;
            Color32 c00 = V(x0, y0, w, h), c10 = V(x1, y0, w, h), c11 = V(x1, y1, w, h), c01 = V(x0, y1, w, h);
            vh.AddVert(new Vector3(r.xMin + x0, r.yMax - y0), c00, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin + x1, r.yMax - y0), c10, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin + x1, r.yMax - y1), c11, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin + x0, r.yMax - y1), c01, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        Color32 V(float x, float y, float w, float h) { return new Color(1f, 1f, 1f, AlphaAt(x, y, w, h)); }
    }

    // 新值「弹一下」：每次预加点增加时放大再回落，给「又强了一截」的手感
    sealed class PopFx : MonoBehaviour
    {
        float t;
        public void Pop() { t = 1f; }
        void Update()
        {
            if (t <= 0f) { if (transform.localScale != Vector3.one) transform.localScale = Vector3.one; return; }
            t = Mathf.Max(0f, t - Time.unscaledDeltaTime / 0.28f);
            transform.localScale = Vector3.one * (1f + 0.28f * t * t);
        }
    }

    // 雪佛龙箭头贴图：跟 CharacterPanelController.GetPreviewArrowSprite 完全同一张(两条斜线在右边收成尖角，没有竖直背边)
    static Sprite _previewArrowSprite;
    static Sprite GetPreviewArrowSprite()
    {
        if (_previewArrowSprite != null) return _previewArrowSprite;
        const int size = 64; const float strokeHalf = 0.11f, amp = 0.42f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = y / (float)(size - 1);
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)(size - 1);
                bool inside = Mathf.Abs(ny - (0.5f + amp * (1f - nx))) <= strokeHalf || Mathf.Abs(ny - (0.5f - amp * (1f - nx))) <= strokeHalf;
                px[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        tex.SetPixels32(px); tex.Apply();
        _previewArrowSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        return _previewArrowSprite;
    }

    static void SetGlyph(TMP_Text t, Color c, bool usable)
    {
        var fx = t.GetComponentInParent<GlowFx>();
        if (fx != null) { fx.baseColor = c; fx.usable = usable; } else t.color = c;
    }

    static void SetButtonColor(Image[] outline, TMP_Text label, Color c)
    {
        if (outline != null) foreach (var o in outline) if (o != null) o.color = c;
        if (label != null) label.color = c;
    }

    // 目标值变了：变小=花费(淡红闪烁)，变大=获得(泛冷绿)；第一次直接对齐，不滚
    void SetAfpTarget(int v)
    {
        if (_dSnap) { _dTarget = v; _dShown = v; _dSnap = false; _dDirty = true; return; }
        if (v == (int)_dTarget) return;
        if (v < _dTarget) _spendPulse = 1f; else _gainPulse = 1f;
        _dTarget = v;
    }

    void UpdateOdometer(float dt)
    {
        bool animating = false;
        float diff = _dTarget - _dShown;
        if (Mathf.Abs(diff) > 0.001f)
        {
            _dShown = Mathf.MoveTowards(_dShown, _dTarget, Mathf.Max(90f, Mathf.Abs(diff) * 3f) * dt);    // 差得越多滚得越快
            animating = true;
        }
        if (_gainPulse > 0f) { _gainPulse = Mathf.Max(0f, _gainPulse - dt / 0.35f); animating = true; }
        if (_spendPulse > 0f) { _spendPulse = Mathf.Max(0f, _spendPulse - dt / 0.7f); animating = true; }
        if (animating || _dDirty) { ApplyDigits(_dShown); _dDirty = false; }
    }

    void ApplyDigits(float v)
    {
        if (_da == null) return;
        v = Mathf.Max(0f, v);
        for (int i = 0; i < Digits; i++)
        {
            float x = v / Mathf.Pow(10f, i);
            float fl = Mathf.Floor(x), fr = x - fl;
            // 个位连续滚；高位只在低位真正进位的「最后 1 个单位」里滚(整数 94 时百位 f=0 静止，99→100 那 1 个单位里百位才 0→1)。
            // 之前用 Pow(frac(v/10^i),6)：v=94 时百位 0.94^6≈0.69，静止也卡在滚了 69% 的位置
            float pw = Mathf.Pow(10f, i);
            float f = i == 0 ? fr : Mathf.Clamp01((v % pw) - (pw - 1f));
            int cur = (int)(fl % 10f), next = (cur + 1) % 10;
            if (_dla[i] != cur) { _da[i].text = DigitStr[cur]; _dla[i] = cur; }
            if (_dlb[i] != next) { _db[i].text = DigitStr[next]; _dlb[i] = next; }
            _daRt[i].anchoredPosition = new Vector2(0f, BaselineShift + f * CellH);
            _dbRt[i].anchoredPosition = new Vector2(0f, BaselineShift + (f - 1f) * CellH);

            Color baseCol = (i == 0 || fl > 0f) ? White : Color.Lerp(ZeroColor, White, f);
            bool significant = i == 0 || fl > 0f || f > 0.5f;
            Color flash = _gainPulse > 0f ? Color.Lerp(White, GainColor, _gainPulse)
                        : _spendPulse > 0f ? Color.Lerp(White, SpendColor, _spendPulse * (Mathf.Sin(Time.unscaledTime * 42f) > 0f ? 1f : 0.15f)) : White;   // 花费：淡红色快速闪烁
            Color ca = significant ? Color.Lerp(baseCol, flash, (_gainPulse > 0f || _spendPulse > 0f) ? 1f : 0f) : baseCol;
            _da[i].color = ca;
            _db[i].color = significant ? ca : ZeroColor;
        }
    }

    void AddPending()
    {
        if (_cursor < 0 || _cursor >= _rows.Count) return;
        var ok = SkyPrisonAfpUpgrades.TryAddPending(_rows[_cursor].def);
        SkyPrisonSystemSEPlayer.Play(ok ? SkyPrisonSystemSEType.AfpAdd : SkyPrisonSystemSEType.AfpInsufficient);
        Refresh();
    }

    void RemovePending()
    {
        if (_cursor < 0 || _cursor >= _rows.Count) return;
        var ok = SkyPrisonAfpUpgrades.TryRemovePending(_rows[_cursor].def);
        SkyPrisonSystemSEPlayer.Play(ok ? SkyPrisonSystemSEType.Switch : SkyPrisonSystemSEType.Forbidden);
        Refresh();
    }

    void ClearPending()
    {
        if (SkyPrisonAfpUpgrades.PendingLevels <= 0) { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden); return; }
        SkyPrisonAfpUpgrades.ClearPending();
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
        Refresh();
    }

    // 统一强化：一次性买下所有预加点
    void Commit()
    {
        var keys = new System.Collections.Generic.List<string>();
        for (int i = 0; i < _rows.Count; i++) if (SkyPrisonAfpUpgrades.Pending(_rows[i].def) > 0) keys.Add(_rows[i].def.key);
        if (SkyPrisonAfpUpgrades.CommitPending())
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.AfpDeploy);          // 用户指定：Achievement Level Up 3
            _breathKeys.Clear(); foreach (var k in keys) _breathKeys.Add(k);
            _breathStart = Time.unscaledTime;                       // 刚强化的行：冷绿呼吸
        }
        else SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
        Refresh();
    }

    // ── 输入 ─────────────────────────────────────────────────
    float _prevDpadY, _prevAxisY;
    int _navDir; float _navNext;
    const float NavRepeatDelay = 0.35f, NavRepeatInterval = 0.07f;      // 按住上/下：首次延迟、之后的连发间隔
    void Update()
    {
        if (_built) UpdateOdometer(Time.unscaledDeltaTime);
        if (!_built) return;
        TickMarquee();
        TickBreath();
        SkyPrisonFloatingWindowKit.DriveHintBarAvoidance(_boxRt, _corners);

        // 世界没停：离开购买区、或进入战斗，窗口自己关(战斗里不该站着买东西)
        if (PlayerCombatStateRuntime.IsInCombat || (_zone != null && !_zone.PlayerInside)) { Close(); return; }
        // 角色死亡 / 这一局的 AFP 会话结束(出本、撤离、死亡结算)：窗口不能挡在结算画面前面
        if (PlayerDead() || (_openedInSession && (AFPRuntime.Instance == null || !AFPRuntime.Instance.IsInSession))) { Close(); return; }
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);

        float dpad = Axis("DPadVertical"), axis = Axis("Vertical");
        int heldDir = 0;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) || dpad > 0.6f || axis > 0.6f) heldDir = 1;
        else if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || dpad < -0.6f || axis < -0.6f) heldDir = -1;
        bool up = false, down = false;
        if (heldDir == 0) _navDir = 0;
        else if (heldDir != _navDir) { _navDir = heldDir; _navNext = Time.unscaledTime + NavRepeatDelay; up = heldDir > 0; down = heldDir < 0; }
        else if (Time.unscaledTime >= _navNext) { _navNext = Time.unscaledTime + NavRepeatInterval; up = heldDir > 0; down = heldDir < 0; }
        _prevDpadY = dpad; _prevAxisY = axis;
        if (up) { int n = _cursor - 1; while (n >= 0 && !SkyPrisonAfpUpgrades.IsVisible(_rows[n].def)) n--; if (n >= 0) { _cursor = n; _scrollToCursor = true; SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); Refresh(); } }
        if (down) { int n = _cursor + 1; while (n < _rows.Count && !SkyPrisonAfpUpgrades.IsVisible(_rows[n].def)) n++; if (n < _rows.Count) { _cursor = n; _scrollToCursor = true; SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); Refresh(); } }
        if (_scrollToCursor) { EnsureCursorVisible(); _scrollToCursor = false; }

        // A/D(←/→、手柄方向键左右)：减/加当前行的预加点
        bool left = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
        bool right = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
        float dpadX = Axis("DPadHorizontal"), axisX = Axis("Horizontal");
        if ((dpadX < -0.6f && _prevDpadX >= -0.6f) || (axisX < -0.6f && _prevAxisX >= -0.6f)) left = true;
        if ((dpadX > 0.6f && _prevDpadX <= 0.6f) || (axisX > 0.6f && _prevAxisX <= 0.6f)) right = true;
        _prevDpadX = dpadX; _prevAxisX = axisX;
        if (left) RemovePending();
        if (right) AddPending();

        // E / Enter / 手柄 A：统一强化(没有预加点时什么都不做，所以开窗时连按 E 不会误买)
        bool confirm = (_input != null && _input.GetActionDown(SkyPrisonInputAction.Interact))
            || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.JoystickButton0);
        // 长按 1 秒才强化(E / Enter / 手柄 A 按住，进度绕按钮框一圈)
        bool confirmHeld = (_input != null && _input.GetAction(SkyPrisonInputAction.Interact))
            || Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.JoystickButton0);
        if (_confirmFx != null) _confirmFx.keyHold = confirmHeld && SkyPrisonAfpUpgrades.PendingLevels > 0;

        // Esc / 手柄 B 关闭(窗口开着时 ExternalBlock=true，路由里的 Esc 分支不会跑，所以自己处理；
        // 路由里额外查了 JustClosedThisFrame，避免同一次 Esc 关了窗口又拉起暂停菜单)
        bool esc = _input != null ? _input.GetActionDown(SkyPrisonInputAction.Menu) : Input.GetKeyDown(KeyCode.Escape);
        if (esc || Input.GetKeyDown(KeyCode.JoystickButton1)) Close();
    }

    // 选中行不在可视范围里就把内容滚到刚好完整露出它(用世界坐标比较行和视口，不依赖内部锚点和内边距)
    readonly Vector3[] _rowCorners = new Vector3[4], _viewCorners = new Vector3[4];
    void EnsureCursorVisible()
    {
        if (_scroll == null || _cursor < 0 || _cursor >= _rows.Count) return;
        Canvas.ForceUpdateCanvases();
        var viewport = _scroll.viewport != null ? _scroll.viewport : _scrollView;
        _rows[_cursor].rt.GetWorldCorners(_rowCorners);
        viewport.GetWorldCorners(_viewCorners);
        float delta = 0f;                                              // >0：行顶部超出视口上沿；<0：行底部低于视口下沿
        if (_rowCorners[1].y > _viewCorners[1].y) delta = _rowCorners[1].y - _viewCorners[1].y;
        else if (_rowCorners[0].y < _viewCorners[0].y) delta = _rowCorners[0].y - _viewCorners[0].y;
        if (Mathf.Abs(delta) < 0.0001f) return;
        float scale = Mathf.Max(0.0001f, Mathf.Abs(_scrollContent.lossyScale.y));
        float maxScroll = Mathf.Max(0f, _scrollContent.rect.height - viewport.rect.height);
        float y = Mathf.Clamp(_scrollContent.anchoredPosition.y - delta / scale, 0f, maxScroll);
        _scroll.velocity = Vector2.zero;
        _scrollContent.anchoredPosition = new Vector2(_scrollContent.anchoredPosition.x, y);
    }

    static float Axis(string n) { try { return Input.GetAxisRaw(n); } catch { return 0f; } }

    bool _openedInSession;
    static bool PlayerDead()
    {
        var p = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (p == null) return false;
        var h = p.GetComponentInChildren<UnitHealthController>(true);
        return h != null && h.IsDead;
    }

    void Close()
    {
        if (_instance != this) return;
        SkyPrisonAfpUpgrades.OnChanged -= Refresh;
        AFPRuntime.OnAFPChanged -= OnAfpChanged;
        SkyPrisonAfpUpgrades.ClearPending();                  // 没点「强化」的预加点一律作废
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);
        SkyPrisonWindowHintBar.GetOrCreate().Clear();
        SkyPrisonWindowHintBar.SetEdgeTop(false);
        SkyPrisonWindowManager_V1.ExternalBlock = _savedExternalBlock;
        RestoreCombatHud();
        _instance = null;
        _lastCloseFrame = Time.frameCount;
        _built = false;                       // 折叠动画期间不再处理输入
        StartCoroutine(CloseRoutine());
    }

    IEnumerator CloseRoutine()
    {
        yield return StartCoroutine(FoldRoutine(1f, 0f));
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        // 被场景切换等外力销毁时也要把挡输入还回去
        if (_instance == this)
        {
            SkyPrisonWindowManager_V1.ExternalBlock = _savedExternalBlock;
            _instance = null;
        }
        // HUD 隐藏请求是计数器，必须还回去(正常 Close 已经还过，这里只兜底外力销毁)
        if (_hudHideRequestCounted)
        {
            SkyPrisonWindowManager_V1.ExternalHudHideRequests = Mathf.Max(0, SkyPrisonWindowManager_V1.ExternalHudHideRequests - 1);
            _hudHideRequestCounted = false;
            if (_windowManagerForHud != null) _windowManagerForHud.RefreshHudVisibility();
            if (_quickItemFadeTarget != null) _quickItemFadeTarget.alpha = 1f;
        }
    }

    // ── 战斗 HUD 的隐藏/恢复：做法跟角色面板(CharacterPanelController)完全一致 ─────────────────
    // HP/LP 条等走 windowManager 统一的 ExternalHudHideRequests 计数器(计数式，多个窗口互不打架)；
    // 快捷栏提示条是独立的根 Canvas，不在 windowManager 管辖内，自己淡出淡入。
    // 关卡刚加载时 HUD 实例可能还没建好，找不到就重试一会儿；计数器整个周期只真正 ++ 一次。
    const float HudFadeDuration = 0.15f;
    SkyPrisonWindowManager_V1 _windowManagerForHud;
    CanvasGroup _quickItemFadeTarget;
    Coroutine _quickItemFadeRoutine;
    bool _hudShouldBeHidden, _hudHideRequestCounted;

    void HideCombatHud()
    {
        _hudShouldBeHidden = true;
        TryResolveAndHideHud();
        if (_windowManagerForHud == null || _quickItemFadeTarget == null) StartCoroutine(RetryResolveHud());
    }

    void TryResolveAndHideHud()
    {
        if (_windowManagerForHud == null) _windowManagerForHud = FindObjectOfType<SkyPrisonWindowManager_V1>();
        if (_windowManagerForHud != null)
        {
            if (!_hudHideRequestCounted) { SkyPrisonWindowManager_V1.ExternalHudHideRequests++; _hudHideRequestCounted = true; }
            _windowManagerForHud.RefreshHudVisibility();
        }
        if (_quickItemFadeTarget == null)
        {
            var strip = FindObjectOfType<SkyPrisonQuickItemPromptStrip>();
            if (strip != null) { _quickItemFadeTarget = strip.GetComponent<CanvasGroup>(); if (_quickItemFadeTarget == null) _quickItemFadeTarget = strip.gameObject.AddComponent<CanvasGroup>(); }
        }
        FadeQuickItemBar(0f);
    }

    IEnumerator RetryResolveHud()
    {
        for (int i = 0; i < 60 && _hudShouldBeHidden; i++) { yield return null; TryResolveAndHideHud(); }
    }

    void RestoreCombatHud()
    {
        _hudShouldBeHidden = false;
        if (_windowManagerForHud != null && _hudHideRequestCounted)
        {
            SkyPrisonWindowManager_V1.ExternalHudHideRequests = Mathf.Max(0, SkyPrisonWindowManager_V1.ExternalHudHideRequests - 1);
            _hudHideRequestCounted = false;
            _windowManagerForHud.RefreshHudVisibility();
        }
        FadeQuickItemBar(1f);
    }

    void FadeQuickItemBar(float target)
    {
        if (_quickItemFadeTarget == null) return;
        if (_quickItemFadeRoutine != null) StopCoroutine(_quickItemFadeRoutine);
        _quickItemFadeRoutine = StartCoroutine(FadeHudRoutine(_quickItemFadeTarget, target));
    }

    IEnumerator FadeHudRoutine(CanvasGroup cg, float target)
    {
        cg.interactable = target > 0.5f;
        cg.blocksRaycasts = target > 0.5f;
        float start = cg.alpha, t = 0f;
        while (t < HudFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            if (cg == null) yield break;
            cg.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t / HudFadeDuration));
            yield return null;
        }
        if (cg != null) cg.alpha = target;
    }

    // ── UI 小工具 ─────────────────────────────────────────────
    static RectTransform MakeRect(string name, Transform parent, Vector2 amin, Vector2 amax) =>
        SkyPrisonFloatingWindowKit.MakeRect(name, parent, amin, amax);

    static TMP_Text AddText(RectTransform parent, string name, string text, float size, TextAlignmentOptions align, Color color, FontStyles style)
    {
        var rt = MakeRect(name, parent, Vector2.zero, Vector2.one);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = align; tmp.fontStyle = style;
        tmp.enableWordWrapping = false;
        return tmp;
    }

    static Image AddLine(RectTransform parent, string name, Vector2 amin, Vector2 amax, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
    {
        var rt = MakeRect(name, parent, amin, amax);
        rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>(); img.color = c; img.raycastTarget = false;
        return img;
    }

    static Image[] AddOutline(RectTransform rt, Color c, float px)
    {
        return new[]
        {
            AddLine(rt, "OT", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, px), c),
            AddLine(rt, "OB", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, px), c),
            AddLine(rt, "OL", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(px, 0f), c),
            AddLine(rt, "OR", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(px, 0f), c),
        };
    }

    static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
    {
        rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
