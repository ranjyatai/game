using System.Collections;
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 战术研修窗口「FIELD TRAINING」：在 REXIA 那边交互打开(对话选项「接受战术研修」)，用研修点点科技树。
/// 外观走统一悬浮窗套件(磨砂 / 角标 / 标题栏 / 关闭图标 / 从中间一条线展开)，跟 AFP 窗口「ADAPTATION SYSTEM」同一家族：
///   · 左边：横向科技树(根在左，往右展开)，节点是装备槽同款的框(四角小方块 + 左右细竖线)；已学冷绿、满级淡紫、锁定变暗；
///     主前置是灰线，次要前置(多个前置才能解锁)是偏青的线；内容超出就拖动 / 滚轮平移，选中节点自动居中；
///   · 右边：详情——名字、等级、效果说明、前置条件逐条(满足冷绿 / 不满足淡红)、每一级的效果、按住 1 秒学习；
///   · 顶部：标签页(探索者课程 / 战略家课程，Q/E 或手柄 LB/RB 切换) + 研修点。
/// 世界不停，窗口开着时角色定住、攻击 / 闪避被挡；进入战斗窗口自动关闭。读写的是 TechTreeRuntime(两张图共用一个点数池)。
/// </summary>
public sealed class SkyPrisonTrainingWindow : MonoBehaviour
{
    static SkyPrisonTrainingWindow _instance;
    public static bool IsOpen => _instance != null;
    static int _lastCloseFrame = -1;
    public static bool JustClosedThisFrame => Time.frameCount == _lastCloseFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() { _instance = null; _lastCloseFrame = -1; }

    public static void Show()
    {
        if (_instance != null) return;
        var go = new GameObject("[TacticalTrainingWindow]");
        _instance = go.AddComponent<SkyPrisonTrainingWindow>();
        _instance.StartCoroutine(_instance.BuildRoutine());
    }

    public static void Hide() { if (_instance != null) _instance.Close(); }

    // ── 配色 / 尺寸 ───────────────────────────────────────────
    const int SortingOrder = 31900;
    const float MarginX = 140f, MarginTop = 80f, MarginBottom = 250f;      // 底部留出按键提示条的位置
    const float CardW = 600f, CardH = 150f, ColStep = 760f, RowStep = 190f, Margin = 80f, DetailW = 900f;
    const float MinZoom = 0.2f, MaxZoom = 1.8f, GlowPad = 28f, IconS = 124f, ChromaOffset = 3.5f;
    const float HoldSeconds = 1f;
    static readonly Color White = Color.white;
    static readonly Color Faint = new Color(1f, 1f, 1f, 0.55f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.28f);
    static readonly Color ColdGreen = new Color(0.42f, 0.92f, 0.68f, 1f);
    static readonly Color Purple = new Color(0.79f, 0.66f, 1f, 1f);
    static readonly Color Warn = new Color(1f, 0.45f, 0.45f, 1f);
    static readonly Color LineLocked = new Color(0.27f, 0.27f, 0.28f, 1f);
    static readonly Color LineOpen = new Color(0.27f, 0.27f, 0.28f, 1f);      // 没学的路线统一暗灰(跟 LineLocked 一样)
    static readonly Color LineLearned = new Color(0.36f, 0.78f, 0.58f, 1f);

    // ── 数据结构 ──────────────────────────────────────────────
    enum NodeState { Locked, Available, Learned, Maxed }

    sealed class NodeView
    {
        public Color dotsBase, outlineBase; public float chromaK = -1f; public RawImage dots; public Image glow; public Image box; public Image[] outline; public Image iconFrame, iconDot, iconImg; public List<Image> iconFx = new List<Image>();
        public TechTreeNodeData data; public int idx, parent = -1, depth; public float row;
        public List<int> kids = new List<int>(), extraParents = new List<int>();
        public RectTransform rt; public Image bg, bar; public TMP_Text name, tag; public List<TMP_Text> nameFx = new List<TMP_Text>(); public List<RectTransform> outlineSets = new List<RectTransform>(); public Vector2 pos;
        public NodeState state;
    }

    sealed class EdgeView { public NodeView from; public List<NodeView> kids = new List<NodeView>(); public Image outSeg, outGlow; public List<Image> trunkSegs = new List<Image>(), trunkGlows = new List<Image>(); public List<Vector2> trunkRange = new List<Vector2>(); public List<Image> kidSegs = new List<Image>(), kidGlows = new List<Image>(); }

    sealed class GraphView
    {
        public TechTreeGraphAsset graph; public string tabName;
        public List<NodeView> nodes = new List<NodeView>(); public List<EdgeView> edges = new List<EdgeView>();
        public RectTransform content, glowLayer; public Vector2 size; public float zoom;
    }

    readonly List<GraphView> _views = new List<GraphView>();
    int _tab; NodeView _sel;
    TMP_FontAsset _font;
    SkyPrisonInputSettings _input;
    System.Func<string, string, string> _L;
    RectTransform _boxRt, _viewport; SkyPrisonBlurUVTracker _blurTracker;
    readonly Vector3[] _corners = new Vector3[4];
    float _zoom = 1f; Vector2 _pan;
    CanvasGroup _boxGroup;
    sealed class GlowRef { public Image img; public float baseA; }
    readonly List<GlowRef> _glows = new List<GlowRef>();
    bool _built, _savedExternalBlock;
    TMP_Text _pointsText, _pointsLabel;
    readonly List<TMP_Text> _tabTexts = new List<TMP_Text>(); readonly List<Image> _tabLines = new List<Image>();
    // 详情
    readonly List<Image> _dIconSprite = new List<Image>(), _dIconDiamond = new List<Image>();
    TMP_Text _dName, _dLevel, _dBody, _dLevels, _dReq, _dState, _holdLabel;
    RectTransform _holdRt, _holdFill; Image[] _holdOutline; Image _holdBg;
    static readonly Color HoldInk = new Color(0.02f, 0.05f, 0.055f, 1f);      // 「挖空」字：跟窗口底色一致
    float _holdProgress; bool _needRelease, _mouseHold;
    float _breathStart = -99f; NodeView _breathNode;

    // ── 生命周期 ──────────────────────────────────────────────
    IEnumerator BuildRoutine()
    {
        DontDestroyOnLoad(gameObject);
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
        _savedExternalBlock = SkyPrisonWindowManager_V1.ExternalBlock;
        SkyPrisonWindowManager_V1.ExternalBlock = true;
        HideCombatHud();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        _font = SkyPrisonFloatingWindowKit.LoadTMPFont("ZhouFangRiMingTi-2 SDF");
        _input = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");
        var table = Resources.Load<UILocalizationTable>("UILocalizationTable");
        _L = (k, f) => table != null ? table.Get(k, f) : f;
        yield return null;

        BuildUI();
        if (TechTreeRuntime.Instance != null)
        {
            TechTreeRuntime.OnNodeLevelUp += OnLevelUp;
            TechTreeRuntime.OnPointsChanged += OnPoints;
        }
        Canvas.ForceUpdateCanvases();
        yield return null;
        SwitchTab(0, false);
        ShowHints();
        yield return StartCoroutine(FoldRoutine(0f, 1f));
        _built = true;
        yield return StartCoroutine(FlickerRoutine());
    }

    void OnLevelUp(string id, int lv) { Refresh(); }
    void OnPoints(int p) { Refresh(); }

    void ShowHints()
    {
        var hints = new[]
        {
            new SkyPrisonWindowHint { iconKey = "keyboard/w", gamepadIconKey = "gamepad/up", fallbackText = "W", label = _L("training_hint_select", "选择") },
            new SkyPrisonWindowHint { iconKey = "keyboard/q", gamepadIconKey = "gamepad/xbox/lb", fallbackText = "Q/E", label = _L("training_hint_tab", "切换课程") },
            new SkyPrisonWindowHint { iconKey = "mouse/scroll", gamepadIconKey = "gamepad/xbox/r3", fallbackText = "滚轮", label = _L("training_hint_zoom", "缩放") },
            new SkyPrisonWindowHint { iconKey = "keyboard/enter", gamepadIconKey = "gamepad/xbox/a", fallbackText = "Enter", label = _L("training_hint_learn", "按住研修") },
            new SkyPrisonWindowHint { iconKey = "keyboard/esc", gamepadIconKey = "gamepad/xbox/b", fallbackText = "Esc", label = _L("ui_afp_hint_close", "关闭") },
        };
        SkyPrisonWindowHintBar.GetOrCreate().Show(hints);
    }

    // ── 构建 ──────────────────────────────────────────────────
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
        _boxRt.anchorMin = Vector2.zero; _boxRt.anchorMax = Vector2.one;
        _boxRt.pivot = new Vector2(0.5f, 0.5f);
        _boxRt.offsetMin = new Vector2(MarginX, MarginBottom); _boxRt.offsetMax = new Vector2(-MarginX, -MarginTop);
        _boxRt.localScale = new Vector3(1f, 0f, 1f);
        _boxGroup = boxGo.AddComponent<CanvasGroup>();

        BuildHologramBackdrop(_boxRt);
        SkyPrisonFloatingWindowKit.AddCornerBrackets(_boxRt);
        var titleBar = SkyPrisonFloatingWindowKit.BuildTitleBar(_boxRt, _L("training_title", "FIELD TRAINING"), _font, out _);
        var drag = titleBar.GetComponent<SkyPrisonUIWindowDragHandle>(); if (drag != null) Destroy(drag);      // 战术研修窗口固定居中，不可拖动
        SkyPrisonFloatingWindowKit.BuildCloseButton(_boxRt, Close, _font);

        float top = SkyPrisonFloatingWindowKit.TitleBarHeight;
        BuildPoints(_boxRt);
        BuildTabs(_boxRt, top);

        // 树的视口：标签下面、详情左边。ScrollRect 同时支持拖动平移和滚轮
        _viewport = MakeRect("Viewport", _boxRt, Vector2.zero, Vector2.one);
        _viewport.offsetMin = new Vector2(50f, 150f);      // 底部给左下角的研修点留位置
        _viewport.offsetMax = new Vector2(-(DetailW + 90f), -(top + 130f));
        _viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = _viewport.gameObject.AddComponent<Image>(); vpImg.color = Color.clear;      // 空白处也能拖
        _viewport.gameObject.AddComponent<PanZoomHandler>().owner = this;

        // 左上角一小片淡出的方格背景，跟 AFP 条目框呼应
        var gridRt = MakeRect("GridDecor", _viewport, new Vector2(0f, 1f), new Vector2(0f, 1f));
        gridRt.pivot = new Vector2(0f, 1f); gridRt.anchoredPosition = Vector2.zero; gridRt.sizeDelta = new Vector2(1100f, 700f);
        gridRt.gameObject.AddComponent<CanvasRenderer>();
        var grid = gridRt.gameObject.AddComponent<GridFadeGraphic>(); grid.raycastTarget = false;

        var detail = MakeRect("Detail", _boxRt, new Vector2(1f, 0f), new Vector2(1f, 1f));
        detail.pivot = new Vector2(1f, 0.5f);
        detail.offsetMin = new Vector2(-(DetailW + 50f), 50f); detail.offsetMax = new Vector2(-50f, -(top + 40f));
        BuildDetail(detail, DetailW);

        // 分隔线
        AddLine(_boxRt, "Divider", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-(DetailW + 70f), 0f), new Vector2(1.6f, -(top + 160f)), new Color(1f, 1f, 1f, 0.14f));

        foreach (var g in TechTreeRuntime.Instance != null ? TechTreeRuntime.Instance.Graphs : (IReadOnlyList<TechTreeGraphAsset>)GameAssetLoader.TechTrees)
            if (g != null) _views.Add(BuildGraph(g, _views.Count));
        BuildTabLabels();
        foreach (var t in rootRt.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (_font != null) t.font = _font;
        if (_holdLabel != null && _holdLabel.font != null)
        {
            var wm = new Material(_holdLabel.font.material);
            wm.SetFloat("_ColorMask", 0f); wm.SetFloat("_StencilComp", 8f); wm.SetFloat("_Stencil", 1f); wm.SetFloat("_StencilOp", 2f);
            wm.SetFloat("_StencilWriteMask", 255f); wm.SetFloat("_StencilReadMask", 255f);
            wm.EnableKeyword("UNITY_UI_ALPHACLIP");
            _holdLabel.fontSharedMaterial = wm;
        }
        foreach (var gv in _views)
            foreach (var nv in gv.nodes)
                foreach (var nt in nv.nameFx) { var m = TmpAdditiveFor(nt); if (m != null) nt.fontSharedMaterial = m; }
    }

    void BuildPoints(RectTransform box)
    {
        // 左下角：标签在左，数字紧挨着它
        var host = MakeRect("Points", box, new Vector2(0f, 0f), new Vector2(0f, 0f));
        host.pivot = new Vector2(0f, 0f); host.sizeDelta = new Vector2(620f, 90f);
        host.anchoredPosition = new Vector2(60f, 36f);
        _pointsLabel = AddText(host, "Label", _L("training_points", "研修点"), 36, TextAlignmentOptions.MidlineLeft, Faint, FontStyles.Normal);
        _pointsLabel.rectTransform.anchorMin = new Vector2(0f, 0f); _pointsLabel.rectTransform.anchorMax = new Vector2(0.4f, 1f);
        _pointsLabel.raycastTarget = false;
        _pointsText = AddText(host, "Value", "0", 78, TextAlignmentOptions.MidlineLeft, White, FontStyles.Normal);
        _pointsText.rectTransform.anchorMin = new Vector2(0.4f, 0f); _pointsText.rectTransform.anchorMax = new Vector2(1f, 1f);
        _pointsText.raycastTarget = false;
    }

    void BuildTabs(RectTransform box, float top)
    {
        // 标签文字在 BuildTabLabels 里按实际有几张图生成
        var host = MakeRect("Tabs", box, new Vector2(0f, 1f), new Vector2(0f, 1f));
        host.pivot = new Vector2(0f, 1f); host.anchoredPosition = new Vector2(60f, -(top + 24f)); host.sizeDelta = new Vector2(1400f, 80f);
        host.gameObject.name = "Tabs";
        _tabHost = host;
        AddLine(box, "TabsLine", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(50f, -(top + 110f)), new Vector2(-(50f + DetailW + 90f), 1.4f), new Color(1f, 1f, 1f, 0.2f));
    }
    RectTransform _tabHost;

    void BuildTabLabels()
    {
        for (int i = 0; i < _views.Count; i++)
        {
            int captured = i;
            var rt = MakeRect("Tab" + i, _tabHost, new Vector2(0f, 0f), new Vector2(0f, 1f));
            rt.pivot = new Vector2(0f, 0.5f); rt.sizeDelta = new Vector2(420f, 0f); rt.anchoredPosition = new Vector2(i * 440f, 0f);
            var hit = rt.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            var btn = rt.gameObject.AddComponent<Button>(); btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => SwitchTab(captured, true));
            var t = AddText(rt, "Label", _views[i].tabName, 46, TextAlignmentOptions.MidlineLeft, Faint, FontStyles.Bold);
            t.raycastTarget = false; _tabTexts.Add(t);
            var line = AddLine(rt, "Line", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(220f, 4f), ColdGreen);
            _tabLines.Add(line);
        }
    }

    string TabNameFor(TechTreeGraphAsset g, int index)
    {
        string id = (g.graphId ?? "") + "|" + g.name;
        if (id.ToLowerInvariant().Contains("seeker")) return _L("training_tab_seeker", "探索者");
        if (id.ToLowerInvariant().Contains("strategist")) return _L("training_tab_strategist", "战略家");
        return string.IsNullOrEmpty(g.displayName) ? g.name : g.displayName;
    }

    // ── 树：布局 + 节点 + 连线 ────────────────────────────────
    GraphView BuildGraph(TechTreeGraphAsset g, int index)
    {
        var v = new GraphView { graph = g, tabName = TabNameFor(g, index) };
        int n = g.nodes.Count;
        for (int i = 0; i < n; i++) v.nodes.Add(new NodeView { data = g.nodes[i], idx = i });
        var idToIdx = new Dictionary<string, int>();
        for (int i = 0; i < n; i++) if (!string.IsNullOrEmpty(g.nodes[i].nodeId)) idToIdx[g.nodes[i].nodeId] = i;
        for (int i = 0; i < n; i++)
        {
            var nv = v.nodes[i];
            int p = nv.data.primaryParentIndex;
            if (p >= 0 && p < n && p != i) { nv.parent = p; v.nodes[p].kids.Add(i); }
            foreach (var req in nv.data.secondaryRequirements)
                if (!string.IsNullOrEmpty(req.targetNodeId) && idToIdx.TryGetValue(req.targetNodeId, out int s) && s != i && !nv.extraParents.Contains(s)) nv.extraParents.Add(s);
        }
        // 深度：主前置 + 次要前置里最深的那个再 +1
        for (int pass = 0; pass <= n; pass++)
        {
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                var nv = v.nodes[i]; int d = 0;
                if (nv.parent >= 0) d = Mathf.Max(d, v.nodes[nv.parent].depth + 1);
                foreach (int s in nv.extraParents) d = Mathf.Max(d, v.nodes[s].depth + 1);
                if (d != nv.depth) { nv.depth = d; changed = true; }
            }
            if (!changed) break;
        }
        // 行：主前置树上的整洁树布局(叶子占连续行，父节点在孩子正中)
        float nextRow = 0f;
        for (int i = 0; i < n; i++) if (v.nodes[i].parent < 0) AssignRow(v, i, ref nextRow, 0);
        for (int i = 0; i < n; i++) if (v.nodes[i].rt == null && v.nodes[i].row == 0f && v.nodes[i].parent >= 0 && !IsReached(v, i)) { /* 孤立的坏数据：放到末尾 */ v.nodes[i].row = nextRow++; }
        float maxDepth = 0f, maxRow = 0f;
        foreach (var nv in v.nodes) { maxDepth = Mathf.Max(maxDepth, nv.depth); maxRow = Mathf.Max(maxRow, nv.row); }
        v.size = new Vector2(Margin * 2f + maxDepth * ColStep + CardW, Margin * 2f + maxRow * RowStep + CardH);

        var content = MakeRect("Content" + index, _viewport, new Vector2(0f, 1f), new Vector2(0f, 1f));
        content.pivot = new Vector2(0f, 1f); content.anchoredPosition = Vector2.zero; content.sizeDelta = v.size;
        v.content = content;
        content.gameObject.SetActive(false);

        // 先建发光层(最底)，再画线，最后画节点
        v.glowLayer = MakeRect("Glow", content, Vector2.zero, Vector2.one);
        v.glowLayer.offsetMin = v.glowLayer.offsetMax = Vector2.zero;
        foreach (var nv in v.nodes)
        {
            nv.pos = new Vector2(Margin + nv.depth * ColStep + CardW * 0.5f, -(Margin + nv.row * RowStep + CardH * 0.5f));
        }
        foreach (var nv in v.nodes)
        {
            var gr = MakeRect("NG", v.glowLayer, new Vector2(0f, 1f), new Vector2(0f, 1f));
            gr.pivot = new Vector2(0.5f, 0.5f); gr.anchoredPosition = nv.pos; gr.sizeDelta = new Vector2(CardW + GlowPad * 2f, CardH + GlowPad * 2f);
            nv.glow = SoftImage(gr.gameObject); nv.glow.enabled = false;
        }
        foreach (var nv in v.nodes) if (nv.kids.Count > 0) v.edges.Add(MakeEdge(content, nv, v));
        foreach (var nv in v.nodes) BuildNode(content, nv);
        return v;
    }

    static bool IsReached(GraphView v, int i)
    {
        for (int guard = 0; guard < v.nodes.Count + 2 && i >= 0; guard++) { if (v.nodes[i].parent < 0) return true; i = v.nodes[i].parent; }
        return false;
    }

    static void AssignRow(GraphView v, int i, ref float nextRow, int guard)
    {
        var nv = v.nodes[i];
        if (guard > v.nodes.Count) { nv.row = nextRow++; return; }
        if (nv.kids.Count == 0) { nv.row = nextRow; nextRow += 1f; return; }
        foreach (int k in nv.kids) AssignRow(v, k, ref nextRow, guard + 1);
        nv.row = (v.nodes[nv.kids[0]].row + v.nodes[nv.kids[nv.kids.Count - 1]].row) * 0.5f;
    }

    // 一个父节点一组线：出口横线 + 竖干 + 每个孩子一条横线，三类线互不重叠(重叠处半透明会叠亮，看起来透明度不一致)
    EdgeView MakeEdge(RectTransform content, NodeView from, GraphView v)
    {
        const float T = 4f;
        var e = new EdgeView { from = from };
        float x0 = from.pos.x + CardW * 0.5f, y0 = from.pos.y, mid = x0 + (ColStep - CardW) * 0.5f;
        float ymin = y0, ymax = y0;
        foreach (int k in from.kids) { var kn = v.nodes[k]; e.kids.Add(kn); ymin = Mathf.Min(ymin, kn.pos.y); ymax = Mathf.Max(ymax, kn.pos.y); }
        // 发光先建(在发光层里)，线本体后建
        e.outGlow = SegGlow(v.glowLayer, x0, mid, y0, y0);
        // 竖干按各孩子的高度切成一段一段：只有「父节点到已点亮孩子」那几段才亮
        var ys = new List<float> { y0 };
        foreach (var kn in e.kids) if (!ys.Exists(yy => Mathf.Abs(yy - kn.pos.y) < 0.5f)) ys.Add(kn.pos.y);
        ys.Sort();
        if (ys.Count == 1) e.trunkRange.Add(new Vector2(ys[0], ys[0]));
        for (int i = 0; i + 1 < ys.Count; i++) e.trunkRange.Add(new Vector2(ys[i], ys[i + 1]));
        foreach (var r in e.trunkRange) e.trunkGlows.Add(SegGlow(v.glowLayer, mid, mid, r.x, r.y));
        foreach (var kn in e.kids) e.kidGlows.Add(SegGlow(v.glowLayer, mid, kn.pos.x - CardW * 0.5f, kn.pos.y, kn.pos.y));
        e.outSeg = SegRect(content, x0, mid - T * 0.5f, y0 - T * 0.5f, y0 + T * 0.5f);
        for (int i = 0; i < e.trunkRange.Count; i++)
        {
            float ya = e.trunkRange[i].x - (i == 0 ? T * 0.5f : 0f), yb = e.trunkRange[i].y + (i == e.trunkRange.Count - 1 ? T * 0.5f : 0f);
            e.trunkSegs.Add(SegRect(content, mid - T * 0.5f, mid + T * 0.5f, ya, yb));
        }
        foreach (var kn in e.kids)
            e.kidSegs.Add(SegRect(content, mid + T * 0.5f, kn.pos.x - CardW * 0.5f, kn.pos.y - T * 0.5f, kn.pos.y + T * 0.5f));
        return e;
    }

    static Image SegGlow(RectTransform layer, float xa, float xb, float ya, float yb)
    {
        var rt = MakeRect("LG", layer, new Vector2(0f, 1f), new Vector2(0f, 1f));
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2((xa + xb) * 0.5f, (ya + yb) * 0.5f);
        rt.sizeDelta = new Vector2(Mathf.Abs(xb - xa) + GlowPad * 2f, Mathf.Abs(yb - ya) + GlowPad * 2f);
        var img = SoftImage(rt.gameObject); img.enabled = false;
        return img;
    }

    // 柔边发光图：九宫格拉伸，边缘渐隐
    static readonly Color[] ChannelMask = { new Color(1f, 0f, 0f, 1f), new Color(0f, 1f, 0f, 1f), new Color(0f, 0f, 1f, 1f) };

    static Material _tmpAddMat; static Material _tmpAddSrc;
    // 文字用的加算材质：拿字体自己的材质复制一份，换成加算版 TMP shader(属性同名，图集贴图自动沿用)
    static Material TmpAdditiveFor(TMP_Text t)
    {
        var holder = Resources.Load<Material>("UI/M_TMPAdditive");
        if (holder == null || t.font == null) return null;
        if (_tmpAddMat == null || _tmpAddSrc != t.font.material)
        {
            _tmpAddSrc = t.font.material;
            _tmpAddMat = new Material(_tmpAddSrc) { shader = holder.shader };
        }
        return _tmpAddMat;
    }

    static Color Mask(int channel, Color c) => new Color(ChannelMask[channel].r * c.r, ChannelMask[channel].g * c.g, ChannelMask[channel].b * c.b, c.a);

    static Material _glowMat;
    static Material _addMat;
    static Material AdditiveMaterial()
    {
        if (_addMat == null) _addMat = Resources.Load<Material>("UI/M_UIAdditive");
        return _addMat;
    }

    const float DotPitch = 8f;
    static Texture2D _dotTex;
    static Texture2D DotTexture()
    {
        if (_dotTex != null) return _dotTex;
        const int N = 8;
        _dotTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                bool dot = x >= 2 && x <= 5 && y >= 2 && y <= 5 && !((x == 2 || x == 5) && (y == 2 || y == 5));      // 4×4 去四角的小圆点
                _dotTex.SetPixel(x, y, new Color(1f, 1f, 1f, dot ? 1f : 0f));
            }
        _dotTex.Apply();
        return _dotTex;
    }

    static Sprite _softSprite;
    static Sprite SoftSprite()
    {
        if (_softSprite != null) return _softSprite;
        const int N = 32; const int B = 12;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Min(Mathf.Min(x, N - 1 - x), Mathf.Min(y, N - 1 - y));
                float a = Mathf.Clamp01(d / B); a = a * a * (3f - 2f * a);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        _softSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 43f, 0, SpriteMeshType.FullRect, new Vector4(B, B, B, B));      // 43 ppu：边框 12px 在 UI 里 ≈ 28 单位
        return _softSprite;
    }

    static Image SoftImage(GameObject go)
    {
        var img = go.AddComponent<Image>(); img.sprite = SoftSprite(); img.type = Image.Type.Sliced; img.raycastTarget = false;
        if (_glowMat == null) _glowMat = Resources.Load<Material>("UI/M_UIGlowMax");
        if (_glowMat != null) img.material = _glowMat;      // BlendOp Max：重叠处不叠加变亮
        return img;
    }

    static Image SegRect(RectTransform parent, float xa, float xb, float ya, float yb)
    {
        var rt = MakeRect("Seg", parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2((xa + xb) * 0.5f, (ya + yb) * 0.5f);
        rt.sizeDelta = new Vector2(Mathf.Max(1f, xb - xa), Mathf.Max(1f, yb - ya));
        var img = rt.gameObject.AddComponent<Image>(); img.raycastTarget = false;
        return img;
    }

    void BuildNode(RectTransform content, NodeView nv)
    {
        var rt = MakeRect("N_" + nv.data.nodeId, content, new Vector2(0f, 1f), new Vector2(0f, 1f));
        rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(CardW, CardH); rt.anchoredPosition = nv.pos;
        nv.rt = rt;
        nv.bg = rt.gameObject.AddComponent<Image>(); nv.bg.color = new Color(1f, 1f, 1f, 0.04f);
        var btn = rt.gameObject.AddComponent<Button>(); btn.transition = Selectable.Transition.None;
        var captured = nv;
        btn.onClick.AddListener(() => { if (_sel != captured) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); Select(captured); });

        // 普通方框(不是装备槽样式)：细边框，颜色随状态变
        var outlineAll = new List<Image>();
        var addM = AdditiveMaterial();
        for (int c = 0; c < 3; c++)
        {
            var set = MakeRect("OutlineCh" + c, rt, Vector2.zero, Vector2.one);
            set.offsetMin = Vector2.zero; set.offsetMax = Vector2.zero; nv.outlineSets.Add(set);
            foreach (var im in AddOutline(set, ChannelMask[c], 2.5f)) { if (addM != null) im.material = addM; outlineAll.Add(im); }
        }
        nv.outline = outlineAll.ToArray();

        // 显示器点阵感：整个方框铺一层 LED 点阵(重复贴图，一个 quad)，颜色随状态变
        var dr = MakeRect("Dots", rt, Vector2.zero, Vector2.one); dr.offsetMin = dr.offsetMax = Vector2.zero;
        nv.dots = dr.gameObject.AddComponent<RawImage>(); nv.dots.texture = DotTexture(); nv.dots.raycastTarget = false;
        nv.dots.uvRect = new Rect(0f, 0f, CardW / DotPitch, CardH / DotPitch);

        // 左边图标框(更大)；图标做真正的色差：R/G/B 三张单通道副本加算叠加，左右错开——重叠处还原成原色，边缘露出红/蓝边
        var fr = MakeRect("Icon", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        fr.pivot = new Vector2(0f, 0.5f); fr.anchoredPosition = new Vector2(18f, 0f); fr.sizeDelta = new Vector2(IconS, IconS);
        nv.iconFrame = fr.gameObject.AddComponent<Image>(); nv.iconFrame.color = new Color(0f, 0f, 0f, 0.45f); nv.iconFrame.raycastTarget = false;
        Color[] ch = { new Color(1f, 0f, 0f, 1f), new Color(0f, 1f, 0f, 1f), new Color(0f, 0f, 1f, 1f) };
        float[] chOff = { -ChromaOffset, 0f, ChromaOffset };
        var addMat = AdditiveMaterial();
        for (int c = 0; c < 3; c++)
        {
            Image im;
            if (nv.data.icon != null)
            {
                var cr = MakeRect("Ch" + c, fr, Vector2.zero, Vector2.one); cr.offsetMin = new Vector2(10f + chOff[c], 10f); cr.offsetMax = new Vector2(-10f + chOff[c], -10f);
                im = cr.gameObject.AddComponent<Image>(); im.sprite = nv.data.icon; im.preserveAspect = true;
            }
            else
            {
                // 还没配图标：菱形占位，同样做通道分离
                var dot = MakeRect("Dot" + c, fr, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)); dot.sizeDelta = new Vector2(40f, 40f); dot.localRotation = Quaternion.Euler(0f, 0f, 45f);
                dot.anchoredPosition = new Vector2(chOff[c], 0f);
                im = dot.gameObject.AddComponent<Image>();
            }
            im.raycastTarget = false; im.color = ch[c]; if (addMat != null) im.material = addMat;
            nv.iconFx.Add(im);
        }

        nv.bar = AddLine(rt, "SelBar", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(8f, 0f), ColdGreen);
        nv.bar.enabled = false;

        for (int c = 0; c < 3; c++)
        {
            var nt = AddText(rt, "Name" + c, nv.data.nodeName, 34, TextAlignmentOptions.TopLeft, ChannelMask[c], FontStyles.Bold);
            Anchor(nt.rectTransform, 0f, 0f, 1f, 1f);
            nt.rectTransform.offsetMin = new Vector2(IconS + 40f, 44f); nt.rectTransform.offsetMax = new Vector2(-16f, -14f);
            nt.raycastTarget = false; nt.enableWordWrapping = true; nt.overflowMode = TextOverflowModes.Ellipsis;
            nv.nameFx.Add(nt);
        }
        nv.name = nv.nameFx[1];
        nv.tag = AddText(rt, "Tag", "", 28, TextAlignmentOptions.BottomLeft, Faint, FontStyles.Normal);
        Anchor(nv.tag.rectTransform, 0f, 0f, 1f, 1f); nv.tag.rectTransform.offsetMin = new Vector2(IconS + 40f, 12f); nv.tag.rectTransform.offsetMax = new Vector2(-16f, -80f);
        nv.tag.raycastTarget = false;
    }

    // ── 详情栏 ────────────────────────────────────────────────
    void BuildDetail(RectTransform d, float w)
    {
        _dName = AddText(d, "Name", "", 68, TextAlignmentOptions.Top, White, FontStyles.Bold);
        Place(_dName.rectTransform, 0f, -290f, w, 90f); _dName.raycastTarget = false; _dName.overflowMode = TextOverflowModes.Ellipsis;
        _dLevel = AddText(d, "Level", "", 36, TextAlignmentOptions.Top, Faint, FontStyles.Normal);
        Place(_dLevel.rectTransform, 0f, -386f, w, 50f); _dLevel.raycastTarget = false;
        // 详情里的大图标：跟节点同样的 R/G/B 通道分离
        var dfr = MakeRect("DIcon", d, new Vector2(0f, 1f), new Vector2(0f, 1f));
        dfr.anchorMin = dfr.anchorMax = new Vector2(0.5f, 1f); dfr.pivot = new Vector2(0.5f, 1f); dfr.anchoredPosition = Vector2.zero; dfr.sizeDelta = new Vector2(260f, 260f);
        var dbg = dfr.gameObject.AddComponent<Image>(); dbg.color = new Color(0f, 0f, 0f, 0.45f); dbg.raycastTarget = false;
        var dAdd = AdditiveMaterial();
        for (int c = 0; c < 3; c++)
        {
            float off = ChromaOffset * 1.2f * (c - 1);
            var sr = MakeRect("S" + c, dfr, Vector2.zero, Vector2.one); sr.offsetMin = new Vector2(26f + off, 26f); sr.offsetMax = new Vector2(-26f + off, -26f);
            var si = sr.gameObject.AddComponent<Image>(); si.preserveAspect = true; si.raycastTarget = false; si.color = ChannelMask[c]; if (dAdd != null) si.material = dAdd;
            var dr = MakeRect("D" + c, dfr, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)); dr.sizeDelta = new Vector2(100f, 100f); dr.localRotation = Quaternion.Euler(0f, 0f, 45f); dr.anchoredPosition = new Vector2(off, 0f);
            var di = dr.gameObject.AddComponent<Image>(); di.raycastTarget = false; di.color = new Color(ChannelMask[c].r, ChannelMask[c].g, ChannelMask[c].b, 0.55f); if (dAdd != null) di.material = dAdd;
            _dIconSprite.Add(si); _dIconDiamond.Add(di);
        }
        AddLine(d, "NameLine", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -446f), new Vector2(0f, 1.4f), new Color(1f, 1f, 1f, 0.25f));

        _dBody = AddText(d, "Body", "", 36, TextAlignmentOptions.TopLeft, new Color(1f, 1f, 1f, 0.9f), FontStyles.Normal);
        Place(_dBody.rectTransform, 0f, -470f, w, 300f); _dBody.enableWordWrapping = true; _dBody.raycastTarget = false;
        _dReq = AddText(d, "Req", "", 34, TextAlignmentOptions.TopLeft, White, FontStyles.Normal);
        Place(_dReq.rectTransform, 0f, -790f, w, 190f); _dReq.enableWordWrapping = true; _dReq.raycastTarget = false;
        _dLevels = AddText(d, "Levels", "", 34, TextAlignmentOptions.TopLeft, White, FontStyles.Normal);
        Place(_dLevels.rectTransform, 0f, -990f, w, 330f); _dLevels.enableWordWrapping = true; _dLevels.raycastTarget = false;
        _dState = AddText(d, "State", "", 38, TextAlignmentOptions.BottomLeft, White, FontStyles.Bold);
        _dState.rectTransform.anchorMin = new Vector2(0f, 0f); _dState.rectTransform.anchorMax = new Vector2(1f, 0f);
        _dState.rectTransform.pivot = new Vector2(0f, 0f); _dState.rectTransform.anchoredPosition = new Vector2(0f, 150f); _dState.rectTransform.sizeDelta = new Vector2(0f, 80f);
        _dState.raycastTarget = false; _dState.enableWordWrapping = true;

        // 「按住研修」按钮：白色细框，按住时冷绿色从左往右填满，1 秒完成
        _holdRt = MakeRect("Hold", d, new Vector2(0f, 0f), new Vector2(1f, 0f));
        _holdRt.pivot = new Vector2(0.5f, 0f); _holdRt.anchoredPosition = new Vector2(0f, 20f); _holdRt.sizeDelta = new Vector2(0f, 110f);
        var hit = _holdRt.gameObject.AddComponent<Image>(); hit.color = Color.clear;      // 只负责接收鼠标
        // 真·挖空：先画文字(只写模板缓冲，不出颜色)，再画白底和绿色进度——它们只在「模板≠1」处绘制，字的位置就被抠掉，露出底下的窗口
        _holdLabel = AddText(_holdRt, "Label", _L("training_hold", "按住研修"), 78, TextAlignmentOptions.Center, Color.white, FontStyles.Bold);
        _holdLabel.raycastTarget = false;
        var readMat = new Material(Shader.Find("UI/Default"));
        readMat.SetFloat("_StencilComp", 6f); readMat.SetFloat("_Stencil", 1f); readMat.SetFloat("_StencilOp", 0f);
        readMat.SetFloat("_StencilReadMask", 255f); readMat.SetFloat("_StencilWriteMask", 0f);
        var bgRt = MakeRect("BgFill", _holdRt, Vector2.zero, Vector2.one); bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        _holdBg = bgRt.gameObject.AddComponent<Image>(); _holdBg.color = new Color(1f, 1f, 1f, 0.95f); _holdBg.raycastTarget = false; _holdBg.material = readMat;
        var fill = MakeRect("Fill", _holdRt, new Vector2(0f, 0f), new Vector2(0f, 1f));
        fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero; fill.pivot = new Vector2(0f, 0.5f);
        _holdFill = fill; var fi = fill.gameObject.AddComponent<Image>(); fi.color = ColdGreen; fi.raycastTarget = false; fi.material = readMat;
        _holdOutline = new Image[0];
        var trig = _holdRt.gameObject.AddComponent<EventTrigger>();
        var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown }; down.callback.AddListener(_ => _mouseHold = true); trig.triggers.Add(down);
        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp }; up.callback.AddListener(_ => _mouseHold = false); trig.triggers.Add(up);
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit }; exit.callback.AddListener(_ => _mouseHold = false); trig.triggers.Add(exit);
    }

    // ── 标签页 / 选择 ─────────────────────────────────────────
    void SwitchTab(int index, bool sound)
    {
        if (_views.Count == 0) { Refresh(); return; }
        index = (index % _views.Count + _views.Count) % _views.Count;
        if (sound && index != _tab) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        for (int i = 0; i < _views.Count; i++) _views[i].content.gameObject.SetActive(i == index);
        _tab = index;
        _zoom = MaxZoom; _zoomTarget = MaxZoom;      // 每次打开、每次切标签都从最大缩放开始，再自己滚轮缩小
        _pan = Vector2.zero; ApplyPan(true);
        for (int i = 0; i < _tabTexts.Count; i++) { _tabTexts[i].color = i == index ? White : Faint; _tabLines[i].enabled = i == index; }
        // 默认选中：第一个「可以学」的节点，没有就根节点
        var gv = _views[index];
        NodeView pick = null;
        foreach (var nv in gv.nodes) { ComputeState(nv); if (nv.state == NodeState.Available) { pick = nv; break; } }
        if (pick == null && gv.nodes.Count > 0) pick = gv.nodes[0];
        _holdProgress = 0f;
        Select(pick, true);
    }

    void Select(NodeView nv, bool snap = false)
    {
        _sel = nv; _holdProgress = 0f; _needRelease = true;
        Refresh();
        if (nv != null) CenterOn(nv, snap);
    }

    Vector2 _scrollTarget; bool _hasScrollTarget;

    float FitZoom(GraphView gv)
    {
        float vw = Mathf.Max(1f, _viewport.rect.width), vh = Mathf.Max(1f, _viewport.rect.height);
        return Mathf.Clamp(Mathf.Min(vw / gv.size.x, vh / gv.size.y), MinZoom, 1f);
    }

    Vector2 ClampPan(Vector2 pan)
    {
        var gv = _views[_tab];
        float vw = _viewport.rect.width, vh = _viewport.rect.height;
        float sx = gv.size.x * _zoom, sy = gv.size.y * _zoom;
        pan.x = sx <= vw ? (vw - sx) * 0.5f : Mathf.Clamp(pan.x, vw - sx, 0f);
        pan.y = sy <= vh ? -(vh - sy) * 0.5f : Mathf.Clamp(pan.y, 0f, sy - vh);
        return pan;
    }

    void ApplyPan(bool clampNow)
    {
        if (_views.Count == 0) return;
        if (clampNow) _pan = ClampPan(_pan);
        var c = _views[_tab].content;
        c.localScale = new Vector3(_zoom, _zoom, 1f);
        c.anchoredPosition = _pan;
    }

    void CenterOn(NodeView nv, bool snap)
    {
        _scrollTarget = ClampPan(new Vector2(_viewport.rect.width * 0.5f - nv.pos.x * _zoom, -_viewport.rect.height * 0.5f - nv.pos.y * _zoom));
        _hasScrollTarget = true;
        if (snap) { _pan = _scrollTarget; _hasScrollTarget = false; ApplyPan(false); }
    }

    // 鼠标位置为中心缩放(pointerScreen 为空时以视口中心)
    // 缩放是「目标缩放 + 每帧平滑追赶」：滚轮/按键只改目标，画面连续变化，不是一格一格跳
    float _zoomTarget = MaxZoom; Vector2? _zoomPointer;

    void ZoomBy(float factor, Vector2? pointerScreen)
    {
        if (_views.Count == 0) return;
        _zoomTarget = Mathf.Clamp(_zoomTarget * factor, FitZoom(_views[_tab]) * 0.999f, MaxZoom);
        _zoomPointer = pointerScreen;
    }

    void TickZoom()
    {
        if (_views.Count == 0 || Mathf.Abs(_zoom - _zoomTarget) < 0.0005f) return;
        float nz = Mathf.Lerp(_zoom, _zoomTarget, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
        if (Mathf.Abs(nz - _zoomTarget) < 0.0005f) nz = _zoomTarget;
        ApplyZoom(nz, _zoomPointer);
    }

    // 以指针位置(没有就视口中心)为中心，把缩放设成 nz
    void ApplyZoom(float nz, Vector2? pointerScreen)
    {
        if (Mathf.Approximately(nz, _zoom)) return;
        Vector2 ptr = new Vector2(_viewport.rect.width * 0.5f, -_viewport.rect.height * 0.5f);
        if (pointerScreen.HasValue && RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, pointerScreen.Value, null, out var local))
            ptr = new Vector2(local.x + _viewport.rect.width * 0.5f, local.y - _viewport.rect.height * 0.5f);
        _pan = ptr - (ptr - _pan) * (nz / _zoom);
        _zoom = nz; _views[_tab].zoom = nz; _hasScrollTarget = false;
        ApplyPan(true);
    }

    sealed class PanZoomHandler : MonoBehaviour, IDragHandler, IScrollHandler
    {
        public SkyPrisonTrainingWindow owner;
        public void OnDrag(PointerEventData e)
        {
            if (owner == null || owner._views.Count == 0) return;
            float k = owner._viewport.rect.width / Mathf.Max(1f, ((RectTransform)owner._viewport).rect.width);
            owner._hasScrollTarget = false;
            // 屏幕像素 → 画布单位
            var canvas = owner.GetComponent<Canvas>();
            float sf = canvas != null ? canvas.scaleFactor : 1f;
            owner._pan += e.delta / Mathf.Max(0.0001f, sf) * k;
            owner.ApplyPan(true);
        }
        public void OnScroll(PointerEventData e)
        {
            if (owner == null) return;
            owner.ZoomBy(Mathf.Pow(1.12f, e.scrollDelta.y), e.position);
        }
    }

    // ── 状态 / 刷新 ───────────────────────────────────────────
    static int LevelOf(string id) => TechTreeRuntime.Instance != null ? TechTreeRuntime.Instance.GetNodeLevel(id) : 0;

    bool PrereqMet(NodeView nv, out List<string> lines)
    {
        lines = new List<string>(); bool ok = true;
        var gv = _views[_tab];
        if (nv.parent >= 0)
        {
            var p = gv.nodes[nv.parent]; bool m = LevelOf(p.data.nodeId) >= 1; ok &= m;
            lines.Add(Req(m, p.data.nodeName));
        }
        foreach (var req in nv.data.secondaryRequirements)
        {
            int have = LevelOf(req.targetNodeId); bool m = have >= req.requiredLevel; ok &= m;
            var rn = TechTreeRuntime.Instance != null ? TechTreeRuntime.Instance.FindNode(req.targetNodeId) : null;
            lines.Add(Req(m, (rn != null ? rn.nodeName : req.targetNodeId) + (req.requiredLevel > 1 ? " Lv." + req.requiredLevel : "")));
        }
        if (nv.data.externalCondition != TechTreeExternalCondition.None)
        {
            bool m = TechTreeRuntime.IsExternalConditionMet(nv.data.externalCondition); ok &= m;
            lines.Add(Req(m, TechTreeRuntime.ExternalConditionText(nv.data.externalCondition)));
        }
        return ok;
    }

    static string Req(bool met, string text) => met ? "<color=#7CFFC0FF>" + text + "</color>" : "<color=#FF7373FF>" + text + "</color>";

    void ComputeState(NodeView nv)
    {
        int lv = LevelOf(nv.data.nodeId);
        if (lv >= nv.data.maxLevel) nv.state = NodeState.Maxed;
        else if (lv > 0) nv.state = NodeState.Learned;
        else nv.state = PrereqMet(nv, out _) ? NodeState.Available : NodeState.Locked;
    }

    void Refresh()
    {
        if (_views.Count == 0) return;
        if (_pointsText != null) _pointsText.text = (TechTreeRuntime.Instance != null ? TechTreeRuntime.Instance.AvailablePoints : 0).ToString();
        var gv = _views[_tab];
        _glows.Clear();
        foreach (var nv in gv.nodes)
        {
            ComputeState(nv);
            int lv = LevelOf(nv.data.nodeId);
            bool sel = nv == _sel;
            Color nameC = nv.state == NodeState.Locked ? new Color(1f, 1f, 1f, 0.5f) : nv.state == NodeState.Maxed ? Purple : White;
            for (int c = 0; c < 3; c++) nv.nameFx[c].color = Mask(c, nameC);
            nv.bg.color = sel ? new Color(0.22f, 0.31f, 0.27f, 1f)
                          : nv.state == NodeState.Learned ? new Color(0.16f, 0.24f, 0.21f, 1f)
                          : nv.state == NodeState.Maxed ? new Color(0.22f, 0.19f, 0.30f, 1f)
                          : nv.state == NodeState.Available ? new Color(0.21f, 0.21f, 0.22f, 1f)
                          : new Color(0.14f, 0.14f, 0.15f, 1f);
            Color oc = sel ? ColdGreen : nv.state == NodeState.Maxed ? new Color(Purple.r, Purple.g, Purple.b, 0.7f)
                       : nv.state == NodeState.Learned ? new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, 0.55f)
                       : nv.state == NodeState.Available ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.16f);
            nv.outlineBase = oc; for (int oi = 0; oi < nv.outline.Length; oi++) nv.outline[oi].color = Mask(oi / 4, oc);
            float ia = nv.state == NodeState.Locked ? 0.3f : 1f;
            float fxA = (nv.data.icon != null ? 1f : 0.55f) * ia;
            foreach (var fxi in nv.iconFx) { var fc = fxi.color; fc.a = fxA; fxi.color = fc; }
            Color dc = sel ? ColdGreen : nv.state == NodeState.Learned ? ColdGreen : nv.state == NodeState.Maxed ? Purple : White;
            float da = sel ? 0.26f : nv.state == NodeState.Learned ? 0.2f : nv.state == NodeState.Maxed ? 0.22f : 0f;      // 没点亮、没选中的节点不画点阵(保持和别的窗口一样平静)
            SetNodeChroma(nv, (sel || nv.state == NodeState.Learned || nv.state == NodeState.Maxed) ? 1f : 0f);
            nv.dotsBase = new Color(dc.r, dc.g, dc.b, da); nv.dots.color = nv.dotsBase;
            bool lit = nv.state == NodeState.Learned || nv.state == NodeState.Maxed;
            nv.glow.enabled = lit;
            if (lit) { var gc = nv.state == NodeState.Maxed ? Purple : ColdGreen; nv.glow.color = new Color(gc.r, gc.g, gc.b, 1f); _glows.Add(new GlowRef { img = nv.glow, baseA = nv.state == NodeState.Maxed ? 0.8f : 0.7f }); }
            nv.bar.enabled = sel; nv.bar.color = nv.state == NodeState.Maxed ? Purple : ColdGreen;
            nv.tag.text = nv.state == NodeState.Maxed ? "MAX" : nv.data.maxLevel > 1 || lv > 0 ? "Lv." + lv + "/" + nv.data.maxLevel : "";
            nv.tag.color = nv.state == NodeState.Maxed ? Purple : lv > 0 ? ColdGreen : Dim;
        }
        foreach (var e in gv.edges)
        {
            // 线全部用不透明纯色(预先混好深色底)，所以任何位置亮度都一致
            int fromLv = LevelOf(e.from.data.nodeId);
            bool anyLearned = false;
            float y0e = e.from.pos.y;
            var litTrunk = new bool[e.trunkSegs.Count];
            for (int k = 0; k < e.kids.Count; k++)
            {
                bool kl = LevelOf(e.kids[k].data.nodeId) > 0; anyLearned |= kl;
                e.kidSegs[k].color = kl ? LineLearned : LineLocked;
                SetLineGlow(e.kidGlows[k], kl);
                if (!kl) continue;
                float lo = Mathf.Min(y0e, e.kids[k].pos.y), hi = Mathf.Max(y0e, e.kids[k].pos.y);
                for (int i = 0; i < litTrunk.Length; i++)
                    if (e.trunkRange[i].x >= lo - 0.5f && e.trunkRange[i].y <= hi + 0.5f) litTrunk[i] = true;
            }
            for (int i = 0; i < litTrunk.Length; i++)
            {
                e.trunkSegs[i].color = litTrunk[i] ? LineLearned : LineLocked;
                SetLineGlow(e.trunkGlows[i], litTrunk[i]);
            }
            SetLineGlow(e.outGlow, anyLearned && fromLv > 0);
            e.outSeg.color = anyLearned && fromLv > 0 ? LineLearned : LineLocked;
        }
        RefreshDetail();
    }

    // 色差强度：k=0 时 R/G/B 三份完全重合(加算回原色，看起来就是普通文字/图标/边框)，k=1 时按 ChromaOffset 错开
    void SetNodeChroma(NodeView nv, float k)
    {
        if (Mathf.Approximately(nv.chromaK, k)) return;
        nv.chromaK = k;
        for (int c = 0; c < 3; c++)
        {
            float tx = (c - 1) * k;
            var rt = nv.nameFx[c].rectTransform;
            rt.offsetMin = new Vector2(IconS + 40f + ChromaOffset * 0.7f * tx, 44f); rt.offsetMax = new Vector2(-16f + ChromaOffset * 0.7f * tx, -14f);
            nv.outlineSets[c].offsetMin = new Vector2(ChromaOffset * 0.6f * tx, 0f); nv.outlineSets[c].offsetMax = new Vector2(ChromaOffset * 0.6f * tx, 0f);
            if (c < nv.iconFx.Count)
            {
                var ir = nv.iconFx[c].rectTransform;
                if (nv.data.icon != null) { ir.offsetMin = new Vector2(10f + ChromaOffset * tx, 10f); ir.offsetMax = new Vector2(-10f + ChromaOffset * tx, -10f); }
                else ir.anchoredPosition = new Vector2(ChromaOffset * tx, 0f);
            }
        }
    }

    void SetLineGlow(Image g, bool on)
    {
        g.enabled = on;
        if (!on) return;
        g.color = new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, 1f);
        _glows.Add(new GlowRef { img = g, baseA = 0.5f });
    }

    // 荧光呼吸：已点亮的节点和路线一起缓缓明暗
    void PulseGlows()
    {
        float w = 0.72f + 0.28f * Mathf.Sin(Time.unscaledTime * 1.7f);
        for (int i = 0; i < _glows.Count; i++)
        {
            var g = _glows[i]; if (g.img == null) continue;
            if (_flickNode != null && (g.img == _flickNode.glow || (_flickEdge != null && g.img == _flickEdge.kidGlows[_flickKid]))) continue;
            var c = g.img.color; c.a = g.baseA * w; g.img.color = c;
        }
    }

    void RefreshDetail()
    {
        var nv = _sel;
        if (nv == null) return;
        var d = nv.data;
        int lv = LevelOf(d.nodeId);
        _dName.text = d.nodeName;
        for (int c = 0; c < 3; c++)
        {
            bool has = d.icon != null;
            _dIconSprite[c].gameObject.SetActive(has); _dIconDiamond[c].gameObject.SetActive(!has);
            if (has) _dIconSprite[c].sprite = d.icon;
            float da = nv.state == NodeState.Locked ? 0.35f : 1f;
            var sc = _dIconSprite[c].color; sc.a = da; _dIconSprite[c].color = sc;
            var dc = _dIconDiamond[c].color; dc.a = 0.55f * da; _dIconDiamond[c].color = dc;
        }
        _dName.color = nv.state == NodeState.Maxed ? Purple : White;
        _dLevel.text = "Lv." + lv + " / " + d.maxLevel;

        string body = !string.IsNullOrEmpty(d.designerNote) ? d.designerNote : d.description;
        body = (body ?? "").Replace("效果说明：\n", "").Replace("效果说明：", "");
        // 备注里手写的「前置：…」那一行由下面的前置条件逐条显示，这里去掉
        var kept = new List<string>();
        foreach (var line in body.Split('\n')) if (!line.TrimStart().StartsWith("前置：") && !line.TrimStart().StartsWith("前置:")) kept.Add(line);
        _dBody.text = string.Join("\n", kept).Trim();

        PrereqMet(nv, out var reqLines);
        _dReq.text = reqLines.Count > 0 ? "<alpha=#99>" + _L("training_prereq", "前置条件") + "<alpha=#FF>\n" + string.Join("\n", reqLines) : "";

        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= d.maxLevel; i++)
        {
            TechTreeLevelData ld = null; foreach (var l in d.levels) if (l.level == i) { ld = l; break; }
            int cost = 0; if (ld != null) foreach (var c in ld.costs) if (c.item == null) cost += c.amount;
            string note = d.maxLevel > 1 && ld != null && !string.IsNullOrEmpty(ld.note) ? ld.note : "";
            bool done = i <= lv;
            string left = (done ? "<color=#7CFFC0FF>●</color> " : "<alpha=#77>○ ") + (d.maxLevel > 1 ? "Lv." + i + "  " : "") + note;
            string right = done ? "" : "   " + cost + " " + _L("training_points", "研修点");
            sb.Append(left).Append(right).Append(done ? "" : "<alpha=#FF>").Append('\n');
        }
        _dLevels.text = sb.ToString().TrimEnd('\n');

        string reason = ""; int needCost = 0;
        bool can = TechTreeRuntime.Instance != null && TechTreeRuntime.Instance.CheckLevelUp(d.nodeId, out reason, out needCost);
        if (lv >= d.maxLevel) { _dState.text = "<color=#C9A8FFFF>" + _L("training_done", "研修完成") + "</color>"; }
        else if (can) _dState.text = string.Format(_L("training_cost_fmt", "下一级需要 {0} 研修点"), needCost);
        else _dState.text = "<color=#FF9A9AFF>" + reason + "</color>";
        _learnable = can;
        float a = can ? 1f : 0.3f;
        _holdBg.color = new Color(1f, 1f, 1f, can ? 0.95f : 0.22f);      // 能学：白色实心；学不了：淡灰
        _holdLabel.color = Color.white;      // 只写模板，颜色不显示；alpha 必须 >0
    }
    bool _learnable;

    // ── 输入 ──────────────────────────────────────────────────
    float _prevDpadX, _prevDpadY, _prevAxisX, _prevAxisY;
    Vector2Int _navDir; float _navNext;
    const float NavRepeatDelay = 0.35f, NavRepeatInterval = 0.12f;
    static float Axis(string n) { try { return Input.GetAxisRaw(n); } catch { return 0f; } }

    void Update()
    {
        if (!_built) return;
        SkyPrisonFloatingWindowKit.DriveHintBarAvoidance(_boxRt, _corners);
        if (PlayerCombatStateRuntime.IsInCombat) { Close(); return; }
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null) EventSystem.current.SetSelectedGameObject(null);

        // 选中节点自动居中(平滑)
        if (_hasScrollTarget && _views.Count > 0)
        {
            _pan = Vector2.Lerp(_pan, _scrollTarget, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            if ((_pan - _scrollTarget).sqrMagnitude < 0.5f) { _pan = _scrollTarget; _hasScrollTarget = false; }
            ApplyPan(false);
        }
        TickZoom();
        // 键盘缩放：- / +
        if (Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus)) ZoomBy(1f - 1.2f * Time.unscaledDeltaTime, null);
        if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus)) ZoomBy(1f + 1.2f * Time.unscaledDeltaTime, null);
        // 手柄缩放：L3(左摇杆按下)缩小、R3(右摇杆按下)放大
        if (Input.GetKey(KeyCode.JoystickButton8)) ZoomBy(1f - 1.2f * Time.unscaledDeltaTime, null);
        if (Input.GetKey(KeyCode.JoystickButton9)) ZoomBy(1f + 1.2f * Time.unscaledDeltaTime, null);

        // 方向：WASD / 方向键 / 手柄十字键 / 左摇杆。按住连发
        float dx = Axis("DPadHorizontal"), dy = Axis("DPadVertical"), ax = Axis("Horizontal"), ay = Axis("Vertical");
        int hx = 0, hy = 0;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) || dx < -0.6f || ax < -0.6f) hx = -1;
        else if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) || dx > 0.6f || ax > 0.6f) hx = 1;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) || dy > 0.6f || ay > 0.6f) hy = 1;
        else if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || dy < -0.6f || ay < -0.6f) hy = -1;
        var dir = new Vector2Int(hx, hy);
        bool fire = false;
        if (dir == Vector2Int.zero) _navDir = dir;
        else if (dir != _navDir) { _navDir = dir; _navNext = Time.unscaledTime + NavRepeatDelay; fire = true; }
        else if (Time.unscaledTime >= _navNext) { _navNext = Time.unscaledTime + NavRepeatInterval; fire = true; }
        if (fire) MoveSelection(dir);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 测试键：F2 加 50 研修点(只在编辑器 / 开发版里有)
        if (Input.GetKeyDown(KeyCode.F2) && SaveManager.Player != null && SaveManager.Player.techTree != null)
        {
            SaveManager.Player.techTree.availablePoints += 50;
            SaveManager.Save();
            Refresh();
        }
#endif

        // Q/E 或 LB/RB：切课程
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.JoystickButton4)) SwitchTab(_tab - 1, true);
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.JoystickButton5)) SwitchTab(_tab + 1, true);

        // 按住研修：Enter / 空格 / 手柄 A / 鼠标按住按钮。松开才能再学下一级(防止一直按着连学)
        bool held = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton0) || _mouseHold;
        if (!held) { _needRelease = false; _holdProgress = Mathf.MoveTowards(_holdProgress, 0f, Time.unscaledDeltaTime * 4f); }
        else if (!_needRelease)
        {
            if (_learnable) _holdProgress += Time.unscaledDeltaTime / HoldSeconds;
            else { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden); _needRelease = true; _holdProgress = 0f; }      // 学不了：响一声，要松开再按
        }
        if (_holdProgress >= 1f) LearnSelected();
        if (_holdFill != null) _holdFill.anchorMax = new Vector2(Mathf.Clamp01(_holdProgress), 1f);
        TickFlicker();
        PulseGlows();

        bool esc = _input != null ? _input.GetActionDown(SkyPrisonInputAction.Menu) : Input.GetKeyDown(KeyCode.Escape);
        if (esc || Input.GetKeyDown(KeyCode.JoystickButton1)) Close();
    }

    void LearnSelected()
    {
        _holdProgress = 0f; _needRelease = true;
        if (_sel == null || TechTreeRuntime.Instance == null) return;
        if (TechTreeRuntime.Instance.TryLevelUp(_sel.data.nodeId, out _))
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.AfpDeploy);
            _flickNode = _sel; _flickStart = Time.unscaledTime; _flickEdge = null; _flickKid = -1;
            foreach (var e in _views[_tab].edges) { int ki = e.kids.IndexOf(_sel); if (ki >= 0) { _flickEdge = e; _flickKid = ki; break; } }
        }
        else SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
        Refresh();
    }

    // 刚学会的节点：冷绿呼吸几下
    NodeView _flickNode; float _flickStart; EdgeView _flickEdge; int _flickKid;

    // 日光灯式点亮：亮-灭-半亮-亮-灭-微亮-亮，最后稳定。路线先闪，0.2 秒后节点再闪(像电流通过去)
    static float Flick(float t)
    {
        if (t < 0f) return 0f;
        if (t < 0.06f) return 0f;
        if (t < 0.14f) return 1f;
        if (t < 0.22f) return 0.12f;
        if (t < 0.30f) return 0.9f;
        if (t < 0.36f) return 0f;
        if (t < 0.44f) return 0.65f;
        if (t < 0.50f) return 0.2f;
        return 1f;
    }

    void TickFlicker()
    {
        if (_flickNode == null) return;
        float t = Time.unscaledTime - _flickStart;
        if (t >= 0.85f) { _flickNode = null; _flickEdge = null; Refresh(); return; }
        float ln = Flick(t - 0.2f), le = Flick(t);
        var nv = _flickNode;
        Color lit = nv == _sel ? new Color(0.22f, 0.31f, 0.27f, 1f) : new Color(0.16f, 0.24f, 0.21f, 1f);
        nv.bg.color = Color.Lerp(new Color(0.14f, 0.14f, 0.15f, 1f), lit, ln);
        var dcol = nv.dotsBase; dcol.a *= 0.25f + 0.75f * ln; nv.dots.color = dcol;
        for (int oi = 0; oi < nv.outline.Length; oi++) { var oc = nv.outlineBase; oc.a *= 0.3f + 0.7f * ln; nv.outline[oi].color = Mask(oi / 4, oc); }
        var gc = ColdGreen; nv.glow.enabled = true; nv.glow.color = new Color(gc.r, gc.g, gc.b, 0.7f * ln);
        if (_flickEdge != null)
        {
            _flickEdge.kidSegs[_flickKid].color = Color.Lerp(LineOpen, LineLearned, le);
            var eg = _flickEdge.kidGlows[_flickKid]; eg.enabled = true; eg.color = new Color(gc.r, gc.g, gc.b, 0.5f * le);
        }
    }

    void TickBreath()
    {
        if (_breathNode == null) return;
        float t = Time.unscaledTime - _breathStart;
        if (t >= 2.6f) { _breathNode = null; Refresh(); return; }
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.6f, 2.6f, t));
        float wave = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / 0.9f - Mathf.PI * 0.5f);
        _breathNode.bg.color = Color.Lerp(new Color(0.16f, 0.24f, 0.21f, 1f), new Color(0.26f, 0.50f, 0.38f, 1f), wave * fade);
    }

    // 方向选择：沿按的方向找最近的节点(沿方向的距离 + 2 倍的偏离距离，越小越优先)
    void MoveSelection(Vector2Int dir)
    {
        if (_sel == null || _views.Count == 0) return;
        var gv = _views[_tab];
        NodeView best = null; float bestScore = float.MaxValue;
        foreach (var nv in gv.nodes)
        {
            if (nv == _sel) continue;
            Vector2 d = nv.pos - _sel.pos;           // 内容坐标本身就是 y 向上为正(下面的节点 y 更小)，不要再翻转
            float along = dir.x != 0 ? d.x * dir.x : d.y * dir.y;
            float perp = dir.x != 0 ? Mathf.Abs(d.y) : Mathf.Abs(d.x);
            if (dir.x != 0 && dir.y != 0) { along = d.x * dir.x + d.y * dir.y; perp = Mathf.Abs(d.x * dir.y - d.y * dir.x); }
            if (along <= 10f) continue;
            float score = along + perp * 2f;
            if (score < bestScore) { bestScore = score; best = nv; }
        }
        if (best != null) { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); Select(best); }
    }

    // ── 开 / 关 ───────────────────────────────────────────────
    IEnumerator FoldRoutine(float from, float to)
    {
        if (_boxRt == null) yield break;
        float dur = SkyPrisonFloatingWindowKit.OpenCloseAnimDuration;
        if (to < from && _blurTracker != null) _blurTracker.Frozen = true;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            if (_boxRt == null) yield break;
            float p = Mathf.Clamp01(t / dur);
            float k = to > from ? 1f - (1f - p) * (1f - p) : 1f - p * p;
            _boxRt.localScale = new Vector3(1f, Mathf.Max(0.002f, k), 1f);
            yield return null;
        }
        if (_boxRt != null) _boxRt.localScale = new Vector3(1f, Mathf.Max(0.002f, to), 1f);
    }

    IEnumerator FlickerRoutine()
    {
        float[] a = { 0.55f, 1f, 0.7f, 1f, 0.85f, 1f };
        foreach (float v in a)
        {
            if (_boxGroup == null) yield break;
            _boxGroup.alpha = v;
            yield return new WaitForSecondsRealtime(0.035f);
        }
        if (_boxGroup != null) _boxGroup.alpha = 1f;
    }

    void BuildHologramBackdrop(RectTransform box)
    {
        // 外缘辉光(盒子外面一圈淡青)
        var halo = MakeRect("Halo", box, Vector2.zero, Vector2.one); halo.offsetMin = new Vector2(-28f, -28f); halo.offsetMax = new Vector2(28f, 28f);
        var hi = SoftImage(halo.gameObject); hi.color = new Color(0.45f, 0.92f, 0.8f, 0.14f);
        // 半透明深色底：背后的世界隐约可见
        var back = MakeRect("Back", box, Vector2.zero, Vector2.one);
        var bi = back.gameObject.AddComponent<Image>(); bi.color = new Color(0.015f, 0.04f, 0.045f, 0.82f); bi.raycastTarget = true;
        // 扫描线
        var scan = MakeRect("Scan", box, Vector2.zero, Vector2.one);
        scan.gameObject.AddComponent<CanvasRenderer>();
        var sg = scan.gameObject.AddComponent<ScanlineGraphic>(); sg.raycastTarget = false;
        // 细边框
        AddOutline(box, new Color(0.55f, 0.95f, 0.85f, 0.35f), 2f);
    }

    sealed class ScanlineGraphic : MaskableGraphic
    {
        const float Pitch = 7f, Thick = 1.6f;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            Color32 c = new Color(0.6f, 1f, 0.9f, 0.025f);
            for (float y = 0f; y < r.height; y += Pitch)
            {
                int i = vh.currentVertCount;
                vh.AddVert(new Vector3(r.xMin, r.yMax - y), c, Vector2.zero);
                vh.AddVert(new Vector3(r.xMax, r.yMax - y), c, Vector2.zero);
                vh.AddVert(new Vector3(r.xMax, r.yMax - y - Thick), c, Vector2.zero);
                vh.AddVert(new Vector3(r.xMin, r.yMax - y - Thick), c, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i + 2, i + 3, i);
            }
        }
    }

    void Close()
    {
        if (_instance != this) return;
        TechTreeRuntime.OnNodeLevelUp -= OnLevelUp;
        TechTreeRuntime.OnPointsChanged -= OnPoints;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);
        SkyPrisonWindowHintBar.GetOrCreate().Clear();
        SkyPrisonWindowHintBar.SetEdgeTop(false);
        SkyPrisonWindowManager_V1.ExternalBlock = _savedExternalBlock;
        RestoreCombatHud();
        _instance = null;
        _lastCloseFrame = Time.frameCount;
        _built = false;
        StartCoroutine(CloseRoutine());
    }

    IEnumerator CloseRoutine()
    {
        yield return StartCoroutine(FoldRoutine(1f, 0f));
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        TechTreeRuntime.OnNodeLevelUp -= OnLevelUp;
        TechTreeRuntime.OnPointsChanged -= OnPoints;
        if (_instance == this) { SkyPrisonWindowManager_V1.ExternalBlock = _savedExternalBlock; _instance = null; }
        if (_hudHideRequestCounted)
        {
            SkyPrisonWindowManager_V1.ExternalHudHideRequests = Mathf.Max(0, SkyPrisonWindowManager_V1.ExternalHudHideRequests - 1);
            _hudHideRequestCounted = false;
            if (_windowManagerForHud != null) _windowManagerForHud.RefreshHudVisibility();
            if (_quickItemFadeTarget != null) _quickItemFadeTarget.alpha = 1f;
        }
    }

    // ── 战斗 HUD 的隐藏 / 恢复：跟 AFP 窗口、角色面板同一套(计数式) ───────────────
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

    // ── 左上角淡出方格(跟 AFP 条目框里的小网格同一款，尺寸大一些) ─────────────────
    sealed class GridFadeGraphic : MaskableGraphic
    {
        const float Cell = 64f, Thick = 2f, Seg = 32f, MaxAlpha = 0.12f;

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
            float t = Mathf.Clamp01((x / w + y / h) * 0.5f * 1.25f);
            return MaxAlpha * Mathf.Pow(1f - t, 1.8f);
        }

        void Quad(VertexHelper vh, Rect r, float x0, float y0, float x1, float y1, float w, float h)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(r.xMin + x0, r.yMax - y0), V(x0, y0, w, h), Vector2.zero);
            vh.AddVert(new Vector3(r.xMin + x1, r.yMax - y0), V(x1, y0, w, h), Vector2.zero);
            vh.AddVert(new Vector3(r.xMin + x1, r.yMax - y1), V(x1, y1, w, h), Vector2.zero);
            vh.AddVert(new Vector3(r.xMin + x0, r.yMax - y1), V(x0, y1, w, h), Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        Color32 V(float x, float y, float w, float h) { return new Color(1f, 1f, 1f, AlphaAt(x, y, w, h)); }
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

    // 左上角为原点、向右向下的定位(详情栏里的文字块用)
    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
    }
}
