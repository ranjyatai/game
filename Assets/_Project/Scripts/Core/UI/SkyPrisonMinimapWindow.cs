using System.Collections;
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 战术地图窗口(默认 M 键，输入系统里的「地图」动作)——悬浮窗套件外观(实时磨砂 + 角标 + 标题栏)：
///   · 固定是 3D 全息视图：场景里真实的模型重画成白色半透明的 X 光全息，右键拖动旋转、中键拖动平移、滚轮缩放(没有 2D / 3D 切换：Tab 是背包的副键位)；
///     2D 俯视图只是兜底(显卡不支持 GPU 实例化 / 没有高度数据时才用)，玩家切不到；
///   · 只显示走过的区域；区域标注(SkyPrisonMapZone)始终显示；撤离点 / AFP 购买区始终显示，箱子只在走过的区域里显示；
///   · 最多 4 个标记(全白，圆 / 方 / 三角 / 菱形 + 立体数字 1–4 + 竖直光柱)：中键点一下在光标处放置(按 1–4 编号依次占用空槽)；
///     光标靠近已有标记时吸附到它上面(高亮)，这时中键点一下是撤销它；中键拖动是平移；1–4 / 点右栏可以指定下一个放哪个槽，Backspace 清除当前槽；
///   · 世界不停、角色不定住、战斗中不能打开；开着时攻击 / 闪避 / 快捷物品被挡；战斗 HUD 淡出；
///   · 没点亮「战术地图解放」(strategist_root)时不能打开。
/// 数据见 SkyPrisonMinimap。3D 视图用一台只渲染「舞台」图层(31)的临时相机，把 World3D 层的模型按网格种类 GPU 实例化重画到 RT 里再贴进窗口，
/// 区域 / 地标 / 标记都用这台相机投影到窗口坐标，所以随镜头转动位置不会错。
/// </summary>
public sealed class SkyPrisonMinimapWindow : MonoBehaviour
{
    static SkyPrisonMinimapWindow _instance;
    public static bool IsOpen => _instance != null;
    static int _lastCloseFrame = -1;
    public static bool JustClosedThisFrame => Time.frameCount == _lastCloseFrame;

    public const string UnlockNodeId = "strategist_root";
    public static bool Unlocked => SkyPrisonAfpAbsorbShield.NodeUnlocked(UnlockNodeId);

    const int SortingOrder = 31800;           // 在 HUD 之上、战术研修 / 购买窗口(31900)之下
    const int StageLayer = 31;                // 3D 舞台专用图层：游戏里所有相机的 cullingMask 都不含它
    static readonly Vector3 StageOrigin = new Vector3(0f, -4000f, 0f);
    static readonly Color ColdGreen = new Color(0.42f, 0.92f, 0.68f, 1f);
    static readonly Color Faint = new Color(1f, 1f, 1f, 0.55f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() { _instance = null; _lastCloseFrame = -1; }

    public static void Toggle() { if (_instance != null) Hide(); else Show(); }
    public static void Hide() { if (_instance != null) _instance.Close(); }

    public static bool Show()
    {
        if (_instance != null) return true;
        var mm = SkyPrisonMinimap.Instance;
        if (!Unlocked || mm == null || !mm.InRaid || !mm.HasMap || PlayerCombatStateRuntime.IsInCombat || !mm.EnsureTerrain())      // 只在 RAID 里能用
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
            return false;
        }
        var go = new GameObject("[MinimapWindow]");
        _instance = go.AddComponent<SkyPrisonMinimapWindow>();
        _instance.Build(mm);
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
        _instance.StartCoroutine(_instance.FoldRoutine(0f, 1f, () => { if (_instance != null) _instance._built = true; }));
        return true;
    }

    // ── 状态 ─────────────────────────────────────────────────
    SkyPrisonMinimap _mm;
    SkyPrisonInputSettings _input;
    RectTransform _boxRt; SkyPrisonBlurUVTracker _blurTracker; TMPro.TMP_FontAsset _font;
    RectTransform _map, _zoneLayer, _landLayer, _markLayer;
    float _mw, _mh, _px, _canvasScale;
    float _areaX, _areaY, _areaW, _areaH;
    RawImage _terrain; Material _terrainMat2D;
    Image _playerIcon, _playerRing;
    readonly Image[] _markIcon = new Image[SkyPrisonMinimap.MarkerSlots];
    readonly Image[] _rowBg = new Image[SkyPrisonMinimap.MarkerSlots];
    readonly TMP_Text[] _rowName = new TMP_Text[SkyPrisonMinimap.MarkerSlots];
    readonly Image[][] _rowCorners = new Image[SkyPrisonMinimap.MarkerSlots][];
    TMP_Text _cursorText, _infoPos, _infoExplored, _infoTime;
    RectTransform _compass, _compassNeedle; TMP_Text _compassN; float _compassD, _u;
    RectTransform[] _legendRows;                                          // 地图右上角图例的各行(撤离点 / AFP 强化 / 箱子 / 玩家)
    readonly bool[] _rowSet = new bool[SkyPrisonMinimap.MarkerSlots];      // 槽位文字当前显示的状态(变了才重设字体，避免每帧重排)
    readonly bool[] _rowSelected = new bool[SkyPrisonMinimap.MarkerSlots];
    System.Func<string, string, string> _L;
    int _slot;
    bool _built, _savedExternalBlock;
    float _nextLandmarks;

    // 3D 舞台
    bool _has3D, _mode3D;
    GameObject _stageRoot; Camera _cam; RenderTexture _rt; Material _holoMat, _premultMat;
    readonly GameObject[] _stageBeam = new GameObject[SkyPrisonMinimap.MarkerSlots];     // 3D 舞台里每个标记的光柱
    float _yaw, _pitch = 58f, _dist; Vector3 _target;
    bool _ortho; float _savedPitch = 58f;     // Tab：切换「正交俯视」(正对下方、无透视)和默认的透视斜视角；回到透视时恢复原来的俯仰角
    bool _rmbDown, _mmbDown; float _rmbMoved, _mmbMoved; Vector2 _lastMouse;
    int _snap = -1;                    // 光标吸附到的标记(-1 = 没有)
    bool _slotManual;                  // 玩家手动指定了槽位(放下后恢复自动：最小的空槽)
    Image _snapRing, _cross;          // _cross：手柄准星(LS 移动，位置是 _gpCursor)
    Vector2 _gpCursor; bool _gpCursorInit;

    // 叠加元素
    sealed class ZoneView { public SkyPrisonMapZone zone; public PolyGraphic fill, line; public TMP_Text label; }
    readonly List<ZoneView> _zones = new List<ZoneView>();
    PolyGraphic _boundsFrame;
    sealed class Landmark { public Image icon; public TMP_Text label; public Vector3 world; public bool on; public Transform follow; public bool hadFollow; public bool enemy; }
    Image _mapBack;                       // 3D 模式下地图区后面垫的一块黑底(全息要在黑底上才有黑白分明的反差)
    RectTransform _mapFrame;              // 地图区的边框线(画在遮罩外面)
    // 标记的颜色(玩家在候选色里自己选)：地图上的图钉 / 右栏槽位里的图钉 / 槽位右端的色块 / 3D 舞台里的光柱材质
    readonly TMP_Text[] _markNum = new TMP_Text[SkyPrisonMinimap.MarkerSlots];
    readonly Image[] _rowPin = new Image[SkyPrisonMinimap.MarkerSlots];
    readonly TMP_Text[] _rowPinNum = new TMP_Text[SkyPrisonMinimap.MarkerSlots];
    readonly Image[] _rowSwatch = new Image[SkyPrisonMinimap.MarkerSlots];
    readonly Material[][] _stageMats = new Material[SkyPrisonMinimap.MarkerSlots][];
    readonly List<Landmark> _landPool = new List<Landmark>();
    int _landUsed;

    /// <summary>四边形：填充或描边(顶点是它自己 RectTransform 的本地坐标)。区域 / 地图边框用，每帧按镜头重新投影。</summary>
    sealed class PolyGraphic : MaskableGraphic
    {
        readonly Vector2[] _pts = new Vector2[4];
        public bool fill; public float thick = 2f;
        public void SetPoints(Vector2 a, Vector2 b, Vector2 c, Vector2 d) { _pts[0] = a; _pts[1] = b; _pts[2] = c; _pts[3] = d; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (fill)
            {
                for (int i = 0; i < 4; i++) vh.AddVert(_pts[i], color, Vector2.zero);
                vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);
                return;
            }
            // 描边做成一圈斜接(miter)的环带：每个顶点算出内外两个点，相邻顶点之间一块四边形，四块互不重叠
            // (分别画四条各自带宽度的线，拐角处会重叠，半透明时那一小块就更亮)。
            var inner = new Vector2[4]; var outer = new Vector2[4];
            float half = thick * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = _pts[i], prev = _pts[(i + 3) % 4], next = _pts[(i + 1) % 4];
                Vector2 e1 = (p - prev), e2 = (next - p);
                Vector2 n1 = e1.sqrMagnitude > 1e-6f ? new Vector2(-e1.y, e1.x).normalized : Vector2.zero;
                Vector2 n2 = e2.sqrMagnitude > 1e-6f ? new Vector2(-e2.y, e2.x).normalized : n1;
                if (n1 == Vector2.zero) n1 = n2;
                Vector2 m = (n1 + n2); m = m.sqrMagnitude > 1e-6f ? m.normalized : n1;
                float k = Mathf.Max(0.35f, Vector2.Dot(m, n1));                  // 斜接长度 = 半宽 / cos，夹一下免得很尖的角伸太长
                Vector2 off = m * (half / k);
                inner[i] = p - off; outer[i] = p + off;
            }
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4, kk = vh.currentVertCount;
                vh.AddVert(inner[i], color, Vector2.zero); vh.AddVert(outer[i], color, Vector2.zero);
                vh.AddVert(outer[j], color, Vector2.zero); vh.AddVert(inner[j], color, Vector2.zero);
                vh.AddTriangle(kk, kk + 1, kk + 2); vh.AddTriangle(kk, kk + 2, kk + 3);
            }
        }
    }

    // ── 搭建 ─────────────────────────────────────────────────
    void Build(SkyPrisonMinimap mm)
    {
        _mm = mm;
        _input = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");
        var locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        _L = (k, f) => locTable != null ? locTable.Get(k, f) : f;          // 中 / 日 / 英：字典里没有这个 key 时用默认文字
        _savedExternalBlock = SkyPrisonWindowManager_V1.ExternalBlock;
        SkyPrisonWindowManager_V1.ExternalBlock = true;
        HideCombatHud();                       // 跟其它悬浮窗一致：窗口开着时战斗 HUD(HP/LP 条、快捷栏)淡出

        // 画布按 3840×2160 缩放(和购买窗口 / 角色面板一致)；下面所有尺寸都用「画布单位」：H = 画布高度，px = H/1080
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        var root = (RectTransform)transform;
        _canvasScale = Mathf.Sqrt(Screen.width / 3840f * (Screen.height / 2160f));      // matchWidthOrHeight=0.5 时的缩放系数
        float W = Screen.width / _canvasScale, H = Screen.height / _canvasScale;
        _px = H / 1080f; float px = _px;
        _font = SkyPrisonFloatingWindowKit.LoadTMPFont("ZhouFangRiMingTi-2 SDF");

        // 窗口更大、右栏更窄：地图是主角
        float PW = W * 0.9f, PH = H * 0.86f, m = 28f * px, titleH = SkyPrisonFloatingWindowKit.TitleBarHeight;
        float headerH = titleH + 52f * px, footerH = 50f * px, rc = PW * 0.16f;
        var boxGo = new GameObject("Box", typeof(RectTransform));
        boxGo.transform.SetParent(root, false);
        _boxRt = (RectTransform)boxGo.transform;
        Center(_boxRt, Vector2.zero, new Vector2(PW, PH));
        _boxRt.localScale = new Vector3(1f, 0f, 1f);                  // 先收成中间一条横线，打开时从线展开
        // 磨砂背景(实时高斯模糊，黑白) + 角标 + 标题栏 + 右上角关闭图标：全部走统一套件，和购买窗口 / 角色面板同款
        SkyPrisonFloatingWindowKit.BuildBlurBackground(this, _boxRt, out _blurTracker);
        var panel = boxGo.AddComponent<Image>(); panel.color = new Color(0f, 0f, 0f, 0.001f); panel.raycastTarget = true;      // 挡住下面的点击(几乎全透明)
        SkyPrisonFloatingWindowKit.AddCornerBrackets(_boxRt);
        SkyPrisonFloatingWindowKit.BuildTitleBar(_boxRt, _L("minimap_title", "战术地图"), _font, out _);
        SkyPrisonFloatingWindowKit.BuildCloseButton(_boxRt, Close, _font);

        // 标题下面一行：地图名(和进图时的地名牌同一个来源：地图本地化表里的显示名，没有就退回场景名)
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var reg = MapLocalizationRegistry.LoadOrNull();
        string mapName = reg != null ? reg.GetDisplayName(sceneName, sceneName) : sceneName;
        var sub = Txt(panel.rectTransform, mapName, Mathf.RoundToInt(22f * px), Faint, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        TopLeft(sub.rectTransform, m, titleH + 12f * px, 1400f * px, 32f * px);

        // 地图区
        _areaX = m; _areaY = headerH + m * 0.5f; _areaW = PW - rc - m * 3f; _areaH = PH - headerH - footerH - m;
        _mapBack = Img(panel.rectTransform, "MapBack", new Color(0f, 0f, 0f, 1f)); _mapBack.raycastTarget = false;      // 必须建在 _map 之前：它在 _map 后面
        _map = R("Map", panel.rectTransform);
        _terrain = _map.gameObject.AddComponent<RawImage>(); _terrain.raycastTarget = true;
        var shader2D = Resources.Load<Shader>("UI/SkyPrisonMinimapTerrain");
        if (shader2D == null) shader2D = Shader.Find("SkyPrison/UI/MinimapTerrain");
        if (shader2D != null) { _terrainMat2D = new Material(shader2D); _terrainMat2D.SetTexture("_ExploredTex", _mm.Explored); }
        // 矩形遮罩：地图区里所有叠加元素(区域 / 地标 / NPC 名字 / 玩家 / 标记 / 图例)只显示在框内，镜头怎么转怎么缩放都不会跑出框外
        _map.gameObject.AddComponent<RectMask2D>();
        // 边框线单独画在遮罩外面(原来是外扩 3px 画在地图区里面的，加了遮罩会被一起裁掉)；建在 _map 之后，所以盖在它上面
        _mapFrame = R("MapFrame", panel.rectTransform);
        Border(_mapFrame, 2f * px, new Color(1f, 1f, 1f, 0.35f), 3f * px);

        _zoneLayer = Stretch(R("Zones", _map)); _landLayer = Stretch(R("Landmarks", _map)); _markLayer = Stretch(R("Markers", _map));
        _boundsFrame = NewPoly(_zoneLayer, "Bounds", new Color(1f, 1f, 1f, 0.35f), false, 2f * px);
        BuildZones();

        // 玩家：白色小菱形 + 呼吸的圈；4 个标记
        _playerRing = Img(_markLayer, "PlayerRing", new Color(1f, 1f, 1f, 0.6f)); _playerRing.sprite = SkyPrisonMinimapIcons.Disk(); _playerRing.raycastTarget = false;
        _playerIcon = Img(_markLayer, "Player", Color.white); _playerIcon.sprite = SkyPrisonMinimapIcons.Diamond(); _playerIcon.raycastTarget = false;
        for (int i = 0; i < SkyPrisonMinimap.MarkerSlots; i++)
        {
            _markIcon[i] = Img(_markLayer, "Mark" + i, Color.white); _markIcon[i].raycastTarget = false;
            _markIcon[i].rectTransform.pivot = new Vector2(0.5f, 0f);          // 图钉的尖角是锚点：尖角对准标记的位置
            SkyPrisonMinimapIcons.StyleMarker(_markIcon[i], i);
            _markNum[i] = SkyPrisonMinimapIcons.AddMarkerNumber(_markIcon[i].rectTransform, i + 1, Mathf.RoundToInt(15f * px));      // 四个标记同一种图钉，靠编号(和玩家选的颜色)区分
            _markIcon[i].gameObject.SetActive(false);
        }

        _snapRing = Img(_markLayer, "SnapRing", new Color(1f, 1f, 1f, 0.9f)); _snapRing.sprite = SkyPrisonMinimapIcons.Ring(); _snapRing.raycastTarget = false;
        _snapRing.gameObject.SetActive(false);
        // 手柄准星：手柄没有光标，LS 移动这个准星，放置 / 撤销标记以它为准(推到地图区边缘继续推 = 平移地图)
        _cross = Img(_markLayer, "Crosshair", new Color(1f, 1f, 1f, 0.85f)); _cross.sprite = SkyPrisonMinimapIcons.Ring(); _cross.raycastTarget = false;
        _cross.rectTransform.anchorMin = _cross.rectTransform.anchorMax = _cross.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        _cross.rectTransform.anchoredPosition = Vector2.zero; _cross.rectTransform.sizeDelta = Vector2.one * 30f * _px;
        _cross.gameObject.SetActive(false);

        // 右栏：顶部「战术信息」(罗盘 + 探索度 + 当前位置)，中间区域图例，右下角标记槽(底边和地图框底边对齐)。
        // 尺寸单位 u = 角色面板装备区那套单位(M*W)换算到当前画布：行高 50u、行距 5u、角上小方块 6u、标题 24u……
        float u = 1.82f * H / 2160f;
        float rx = PW - rc - m;
        float markerBlockH = 24f * u + 4f * 50f * u + 3f * 5f * u;
        float markerTop = _areaY + _areaH + 3f * px - markerBlockH;
        BuildGroupHeader(panel.rectTransform, rx, markerTop, rc, _L("minimap_markers", "标记"), u);
        float ry = markerTop + 24f * u;
        for (int i = 0; i < SkyPrisonMinimap.MarkerSlots; i++)
        {
            BuildMarkerRow(panel.rectTransform, i, rx, ry, rc, u);
            ry += 55f * u;
        }
        BuildInfoBlock(panel.rectTransform, rx, headerH + m * 0.6f, rc, u, out float infoBottom);
        BuildLegend(panel.rectTransform, rx, infoBottom + 18f * u, rc);
        BuildMapLegend(u);

        _cursorText = Txt(panel.rectTransform, "", Mathf.RoundToInt(22f * px), Color.white, TextAlignmentOptions.BottomRight, FontStyles.Normal);
        _cursorText.rectTransform.anchorMin = _cursorText.rectTransform.anchorMax = new Vector2(1f, 0f); _cursorText.rectTransform.pivot = new Vector2(1f, 0f);
        _cursorText.rectTransform.anchoredPosition = new Vector2(-m, 12f * px); _cursorText.rectTransform.sizeDelta = new Vector2(PW * 0.4f, 32f * px);

        _has3D = _mm.EnsureHeightField() && Build3DStage();
        SetMode(_has3D);
        ShowHints();
        RefreshLandmarks();
    }

    // ── 右栏的几块 ────────────────────────────────────────────
    // 分组标题：粗体、70% 透明度的白字，下面一条细线(和角色面板装备区的组标题一样)
    void BuildGroupHeader(RectTransform parent, float x, float y, float w, string label, float u)
    {
        var header = R("Header", parent);
        TopLeft(header, x, y, w, 24f * u);
        var t = Txt(header, label, 12.9f * u, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Left, FontStyles.Bold);
        Stretch(t.rectTransform);
        var line = Img(header, "Line", new Color(1f, 1f, 1f, 0.25f)); line.raycastTarget = false;
        line.rectTransform.anchorMin = new Vector2(0f, 0f); line.rectTransform.anchorMax = new Vector2(1f, 0f); line.rectTransform.pivot = new Vector2(0f, 0f);
        line.rectTransform.anchoredPosition = Vector2.zero; line.rectTransform.sizeDelta = new Vector2(0f, 1.4f * u);
    }

    // 标记槽：角色面板「快捷物品」槽的样式——很淡的行底、四个角各一个小方块、左右各一条很细的竖线；
    // 左边是图钉图标(同一种图标 + 编号)，右边上面一行小字「标记 N」，下面一行大字：没放置时是装饰字体的「NO DATA」(40% 透明度，固定英文)，放了就是距离
    void BuildMarkerRow(RectTransform parent, int i, float x, float y, float w, float u)
    {
        int slot = i;
        var row = Img(parent, "Slot" + i, new Color(1f, 1f, 1f, 0.05f));
        TopLeft(row.rectTransform, x, y, w, 50f * u);
        var btn = row.gameObject.AddComponent<Button>(); btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => { _slot = slot; _slotManual = true; SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); });
        _rowBg[i] = row;

        var corners = new Image[4]; var anchors = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        for (int k = 0; k < 4; k++)
        {
            var c = Img(row.rectTransform, "Corner", new Color(1f, 1f, 1f, 0.4f)); c.raycastTarget = false;
            c.rectTransform.anchorMin = c.rectTransform.anchorMax = anchors[k]; c.rectTransform.pivot = anchors[k];
            c.rectTransform.anchoredPosition = Vector2.zero; c.rectTransform.sizeDelta = new Vector2(6f * u, 6f * u);
            corners[k] = c;
        }
        _rowCorners[i] = corners;
        for (int side = 0; side < 2; side++)
        {
            var e = Img(row.rectTransform, side == 0 ? "EdgeL" : "EdgeR", new Color(1f, 1f, 1f, 0.18f)); e.raycastTarget = false;
            e.rectTransform.anchorMin = new Vector2(side, 0f); e.rectTransform.anchorMax = new Vector2(side, 1f); e.rectTransform.pivot = new Vector2(side, 0.5f);
            e.rectTransform.anchoredPosition = Vector2.zero; e.rectTransform.sizeDelta = new Vector2(0.9f * u, 0f);
        }

        float iconW = 32f * u, iconH = iconW * SkyPrisonMinimapIcons.PinAspect;
        var icon = Img(row.rectTransform, "Icon", Color.white); icon.raycastTarget = false;
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f); icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        icon.rectTransform.anchoredPosition = new Vector2(12f * u + iconW * 0.5f, 0f); icon.rectTransform.sizeDelta = new Vector2(iconW, iconH);
        SkyPrisonMinimapIcons.StyleMarker(icon, i);
        _rowPin[i] = icon;
        _rowPinNum[i] = SkyPrisonMinimapIcons.AddMarkerNumber(icon.rectTransform, i + 1, 15f * u);

        // 右端的小色块：当前这个槽选的颜色，点一下切到下一种候选色(白 → 琥珀 → 红 → 青 → 绿 → 白……)
        var sw = Img(row.rectTransform, "Swatch", Color.white);
        sw.rectTransform.anchorMin = sw.rectTransform.anchorMax = new Vector2(1f, 0.5f); sw.rectTransform.pivot = new Vector2(1f, 0.5f);
        sw.rectTransform.anchoredPosition = new Vector2(-14f * u, 0f); sw.rectTransform.sizeDelta = new Vector2(16f * u, 16f * u);
        Border(sw.rectTransform, 1.4f * u, new Color(1f, 1f, 1f, 0.55f), 3f * u);
        var swBtn = sw.gameObject.AddComponent<Button>(); swBtn.transition = Selectable.Transition.None;
        swBtn.onClick.AddListener(() => { _mm.CycleMarkerColor(slot); SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); });
        _rowSwatch[i] = sw;

        float tx = 12f * u + iconW + 12f * u;
        var label = Txt(row.rectTransform, _L("minimap_marker_n", "标记 {0}").Replace("{0}", (i + 1).ToString()), 10.9f * u, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.BottomLeft, FontStyles.Normal);
        label.rectTransform.anchorMin = new Vector2(0f, 0.5f); label.rectTransform.anchorMax = new Vector2(1f, 1f); label.rectTransform.offsetMin = new Vector2(tx, 0f); label.rectTransform.offsetMax = new Vector2(-38f * u, 0f);
        var name = Txt(row.rectTransform, "NO DATA", 20f * u, new Color(1f, 1f, 1f, 0.4f), TextAlignmentOptions.TopLeft, FontStyles.Bold, true);
        name.rectTransform.anchorMin = new Vector2(0f, 0f); name.rectTransform.anchorMax = new Vector2(1f, 0.5f); name.rectTransform.offsetMin = new Vector2(tx, 0f); name.rectTransform.offsetMax = new Vector2(-38f * u, 0f);
        _rowName[i] = name;
    }

    // 地图右上角的图例：这张图上会出现哪几类标注(撤离点 / AFP 强化 / 箱子 / 玩家)。图标和地图上画的是同一套。
    void BuildMapLegend(float u)
    {
        var items = new (Sprite sprite, Color color, float size, string text)[]
        {
            (SkyPrisonMinimapIcons.Diamond(), new Color(0.4f, 1f, 0.7f, 1f), 14f, _L("minimap_legend_extract", "撤离点")),
            (SkyPrisonMinimapIcons.Disk(), ColdGreen, 12f, _L("minimap_legend_afp", "AFP强化")),
            (null, new Color(1f, 0.78f, 0.3f, 0.9f), 8f, _L("minimap_legend_crate", "箱子")),
            (SkyPrisonMinimapIcons.Ring(), new Color(0.88f, 0.96f, 1f, 0.95f), 12f, _L("minimap_legend_npc", "NPC")),
            (SkyPrisonMinimapIcons.Disk(), new Color(1f, 0.28f, 0.25f, 0.95f), 10f, _L("minimap_legend_enemy", "敌人")),
            (SkyPrisonMinimapIcons.Diamond(), QuestGold, 13f, _L("minimap_legend_quest", "任务目标")),
            (SkyPrisonMinimapIcons.Diamond(), Color.white, 11f, _L("minimap_legend_you", "玩家")),
        };
        var box = R("MapLegend", _map);
        box.anchorMin = box.anchorMax = new Vector2(1f, 1f); box.pivot = new Vector2(1f, 1f);
        float rowH = 22f * u, boxW = 190f * u;
        box.anchoredPosition = new Vector2(-14f * u, -12f * u); box.sizeDelta = new Vector2(boxW, rowH * items.Length);
        _legendRows = new RectTransform[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            var row = R("Row", box);
            _legendRows[i] = row;
            TopLeft(row, 0f, i * rowH, boxW, rowH);
            var ic = Img(row, "Icon", items[i].color); ic.raycastTarget = false; ic.sprite = items[i].sprite;
            ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(1f, 0.5f); ic.rectTransform.pivot = new Vector2(1f, 0.5f);
            ic.rectTransform.anchoredPosition = new Vector2(-(8f * u + (14f - items[i].size) * 0.5f * u), 0f); ic.rectTransform.sizeDelta = new Vector2(items[i].size * u, items[i].size * u);
            var t = Txt(row, items[i].text, 11.5f * u, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Right, FontStyles.Normal);
            t.rectTransform.anchorMin = new Vector2(0f, 0f); t.rectTransform.anchorMax = new Vector2(1f, 1f);
            t.rectTransform.offsetMin = Vector2.zero; t.rectTransform.offsetMax = new Vector2(-(8f * u + 14f * u + 8f * u), 0f);
        }
    }

    // 战术信息：罗盘(跟着镜头转，N 指北；地图能旋转，所以必须有) + 探索度 + 当前位置
    void BuildInfoBlock(RectTransform parent, float x, float y, float w, float u, out float bottom)
    {
        _u = u;
        BuildGroupHeader(parent, x, y, w, _L("minimap_info", "战术信息"), u);
        y += 24f * u + 16f * u;

        _compassD = 70f * u;
        _compass = R("Compass", parent);
        TopLeft(_compass, x + 14f * u, y + 10f * u, _compassD, _compassD);
        var ring = Img(_compass, "Ring", new Color(1f, 1f, 1f, 0.55f)); ring.sprite = SkyPrisonMinimapIcons.Ring(); ring.raycastTarget = false; Stretch(ring.rectTransform);
        var tick = Img(_compass, "Dot", new Color(1f, 1f, 1f, 0.5f)); tick.sprite = SkyPrisonMinimapIcons.Disk(); tick.raycastTarget = false;
        Center(tick.rectTransform, Vector2.zero, new Vector2(4f * u, 4f * u));
        var needle = Img(_compass, "Needle", Color.white); needle.sprite = SkyPrisonMinimapIcons.Triangle(); needle.raycastTarget = false;
        needle.rectTransform.anchorMin = needle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); needle.rectTransform.pivot = new Vector2(0.5f, 0f);
        needle.rectTransform.anchoredPosition = Vector2.zero; needle.rectTransform.sizeDelta = new Vector2(_compassD * 0.2f, _compassD * 0.42f);
        _compassNeedle = needle.rectTransform;
        _compassN = Txt(_compass, "N", 12f * u, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        Center(_compassN.rectTransform, Vector2.zero, new Vector2(24f * u, 20f * u));

        float x2 = x + 14f * u + _compassD + 24f * u, w2 = w - (x2 - x);
        var l1 = Txt(parent, _L("minimap_explored", "探索度"), 10.9f * u, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.BottomLeft, FontStyles.Normal);
        TopLeft(l1.rectTransform, x2, y, w2, 16f * u);
        _infoExplored = Txt(parent, "0%", 20f * u, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        TopLeft(_infoExplored.rectTransform, x2, y + 18f * u, w2, 26f * u);
        var l2 = Txt(parent, _L("minimap_position", "当前位置"), 10.9f * u, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.BottomLeft, FontStyles.Normal);
        TopLeft(l2.rectTransform, x2, y + 50f * u, w2, 16f * u);
        _infoPos = Txt(parent, "(---, ---)", 14f * u, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        TopLeft(_infoPos.rectTransform, x2, y + 68f * u, w2, 22f * u);
        var l3 = Txt(parent, _L("minimap_raid_time", "入局时长"), 10.9f * u, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.BottomLeft, FontStyles.Normal);
        TopLeft(l3.rectTransform, x2, y + 98f * u, w2, 16f * u);
        _infoTime = Txt(parent, "00:00:00", 30f * u, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        _infoTime.enableWordWrapping = false; _infoTime.overflowMode = TextOverflowModes.Overflow;
        TopLeft(_infoTime.rectTransform, x2, y + 116f * u, w2, 40f * u);
        bottom = y + 160f * u;
    }

    void ShowHints()
    {
        var hints = new List<SkyPrisonWindowHint>
        {
            SkyPrisonWindowHint.Icon("mouse/scroll", _L("minimap_hint_middle", "中键"),
                _has3D ? _L("minimap_hint_place_pan", "放置 / 撤销标记 (拖动平移)") : _L("minimap_hint_place", "放置 / 撤销标记")),
        };
        if (_has3D)
        {
            hints.Add(SkyPrisonWindowHint.Icon("mouse/right", _L("minimap_hint_right", "右键"), _L("minimap_hint_rotate", "拖动旋转")));
            hints.Add(SkyPrisonWindowHint.Icon("mouse/scroll", _L("minimap_hint_wheel", "滚轮"), _L("minimap_hint_zoom", "缩放")));
            hints.Add(SkyPrisonWindowHint.Icon("keyboard/tab", "Tab", _L("minimap_hint_ortho", "正交俯视 / 透视")));
        }
        hints.Add(SkyPrisonWindowHint.Action(SkyPrisonInputAction.Map, _L("ui_hint_close", "关闭")));
        // 手柄(只在手柄模式显示)：A 在准星处放置 / 撤销，LB RB 换槽位，LS 平移，RS 转动，RT 缩放，Y 切正交，B 关闭
        hints.Add(SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/l", "LS", _L("minimap_hint_cursor", "移动准星 (推到边缘平移)")));
        hints.Add(SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/a", "A", _L("minimap_hint_place", "放置 / 撤销标记")));
        hints.Add(SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/lb", "LB", ""));
        hints.Add(SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/rb", "RB", _L("minimap_hint_slot", "切换标记槽位")));
        hints.Add(SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/r3", "R3", _L("minimap_hint_color", "标记换色")));
        if (_has3D)
        {
            hints.Add(SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/r", "RS", _L("raid_hint_orbit", "转动视角")));
            hints.Add(SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/rt", "RT", _L("raid_hint_zoom", "缩放")));
            hints.Add(SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/y", "Y", _L("minimap_hint_ortho", "正交俯视 / 透视")));
        }
        hints.Add(SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/b", "B", ""));                                               // 两个键并排(B / View)，文字放在第二个后面
        hints.Add(SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/view", "View", _L("ui_hint_close", "关闭")));      // View(PS4 触摸板)也是地图键：再按一次关闭
        SkyPrisonWindowHintBar.GetOrCreate().Show(hints);
    }

    // ── 3D 舞台 ──────────────────────────────────────────────
    bool Build3DStage()
    {
        var shader = Resources.Load<Shader>("UI/SkyPrisonMinimapHologram");
        if (shader == null) shader = Shader.Find("SkyPrison/MinimapHologram");
        if (shader == null || !_mm.HasHeightField || !SystemInfo.supportsInstancing) return false;

        float range = Mathf.Max(1f, _mm.HfMax - _mm.HfMin);
        float S = SkyPrisonMinimap.VisualHeightScale;      // 舞台里的高度是夸张后的
        var size = _mm.MapBounds.size;
        _holoMat = new Material(shader) { enableInstancing = true };
        _holoMat.SetTexture("_MainTex", _mm.Terrain); _holoMat.SetTexture("_ExploredTex", _mm.Explored);
        _holoMat.SetFloat("_HMax", range * S);
        _holoMat.SetFloat("_FloorH", Mathf.Max(1.5f, 1.5f * S));                                       // 离地 1.5 米以内算「地面」：只在这里画地面细网格
        _holoMat.SetFloat("_GridStep", Mathf.Max(1f, Mathf.Round(Mathf.Max(size.x, size.z) / 200f)));   // 细网格约每 2 米一格(404 米宽的图)，每 10 格一条主线
        _holoMat.SetVector("_StageOrigin", new Vector4(StageOrigin.x, StageOrigin.y, StageOrigin.z, 0f));
        _holoMat.SetVector("_MapSize", new Vector4(size.x, size.z, 0f, 0f));
        _premultMat = new Material(Resources.Load<Shader>("UI/SkyPrisonPremultiplied") ?? Shader.Find("SkyPrison/UI/Premultiplied"));

        // 世界 → 舞台：先把地图中心挪到原点、地面压到 y=0，再把高度放大 S 倍，最后搬到舞台原点
        var c = _mm.MapBounds.center;
        _toStage = Matrix4x4.TRS(StageOrigin, Quaternion.identity, new Vector3(1f, S, 1f)) * Matrix4x4.Translate(new Vector3(-c.x, -_mm.HfMin, -c.z));

        _stageRoot = new GameObject("[MinimapStage]") { layer = StageLayer };      // 只是相机的父物体：几何不是场景物体，每帧用 Graphics.RenderMeshInstanced 画
        _stageRoot.transform.position = StageOrigin;
        GatherStageGeometry();

        var camGo = new GameObject("Cam"); camGo.transform.SetParent(_stageRoot.transform, false);
        _cam = camGo.AddComponent<Camera>();
        _cam.enabled = false; _cam.fieldOfView = 35f; _cam.nearClipPlane = 0.5f;
        _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _cam.cullingMask = 1 << StageLayer; _cam.allowHDR = false; _cam.allowMSAA = true;
        var data = _cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None; data.renderShadows = false;

        _target = new Vector3(0f, range * 0.25f, 0f);
        _yaw = 0f; _pitch = 58f;
        _dist = Mathf.Max(size.x / 1.6f, size.z) * 1.15f;
        return true;
    }

    float MinDist => Mathf.Max(_mm.MapBounds.size.x, _mm.MapBounds.size.z) * 0.12f;
    float MaxDist => Mathf.Max(_mm.MapBounds.size.x, _mm.MapBounds.size.z) * 2.4f;

    void UpdateStageCamera()
    {
        Quaternion rot = Quaternion.Euler(_ortho ? 90f : _pitch, _yaw, 0f);
        _cam.orthographic = _ortho;
        if (_ortho) _cam.orthographicSize = _dist * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);      // 画面高度和透视视角在同样距离下大致相当，切换时视野不会突变
        _cam.transform.position = StageOrigin + _target + rot * new Vector3(0f, 0f, -_dist);
        _cam.transform.rotation = rot;
        _cam.farClipPlane = _dist * 4f + Mathf.Max(_mm.MapBounds.size.x, _mm.MapBounds.size.z);
        DrawStageGeometry();
        _cam.Render();
    }

    // ── 场景里真实的模型 → 全息 ──────────────────────────────
    // 把 World3D 层所有网格渲染器的(网格, 子网格, 矩阵)按「网格种类」分组，用 GPU 实例化重画进舞台相机：
    // 这张图 760 个渲染器只有 70 种网格，所以每帧只有几十次绘制；也不用读网格数据(有的网格没开读写)。
    sealed class MeshBatch { public Mesh mesh; public int sub; public readonly List<Matrix4x4> m = new List<Matrix4x4>(); public Matrix4x4[] arr = new Matrix4x4[0]; }
    readonly Dictionary<long, MeshBatch> _batches = new Dictionary<long, MeshBatch>();
    float _nextGather;
    Matrix4x4 _toStage;

    bool AnyExplored(Bounds b)
    {
        return _mm.IsExplored(b.center) || _mm.IsExplored(new Vector3(b.min.x, b.center.y, b.min.z)) || _mm.IsExplored(new Vector3(b.max.x, b.center.y, b.min.z))
            || _mm.IsExplored(new Vector3(b.min.x, b.center.y, b.max.z)) || _mm.IsExplored(new Vector3(b.max.x, b.center.y, b.max.z));
    }

    bool _diagLogged;

    // 诊断：第一次收集时把「场景里到底有什么、收集到了多少」写进日志([MinimapDiag])，排查「地面 / 模型没显示」用
    void LogStageDiag(int w3dTotal, int noMesh, int culledSmall, int included, Vector3 playerPos, bool hasPlayer)
    {
        var sb = new System.Text.StringBuilder("[MinimapDiag] scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
            + " World3D渲染器=" + w3dTotal + " 无网格=" + noMesh + " 被探索裁掉(小件)=" + culledSmall + " 实际收集实例=" + included + " 批次=" + _batches.Count
            + " 探索=" + _mm.ExploredFraction.ToString("P2") + " 地图中心=" + _mm.MapBounds.center.ToString("F1") + " 大小=" + _mm.MapBounds.size.ToString("F1")
            + " HfMin=" + _mm.HfMin.ToString("F2") + " HfMax=" + _mm.HfMax.ToString("F2") + " Terrain组件=" + FindObjectsByType<Terrain>(FindObjectsSortMode.None).Length
            + " 玩家=" + (hasPlayer ? playerPos.ToString("F1") : "null") + "\n   玩家周围40米内各层的网格渲染器数量：");
        var near = new SortedDictionary<string, int>();
        foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (mr == null || !hasPlayer || (mr.bounds.center - playerPos).sqrMagnitude > 40f * 40f) continue;
            string ln = LayerMask.LayerToName(mr.gameObject.layer); if (string.IsNullOrEmpty(ln)) ln = "#" + mr.gameObject.layer;
            near.TryGetValue(ln, out int n); near[ln] = n + 1;
        }
        foreach (var kv in near) sb.Append(kv.Key + "=" + kv.Value + " ");
        int sprites = 0; foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)) if (hasPlayer && (sr.bounds.center - playerPos).sqrMagnitude < 40f * 40f) sprites++;
        sb.Append("\n   玩家周围 SpriteRenderer=" + sprites + " BaseGroundBlock=" + FindObjectsByType<BaseGroundBlock>(FindObjectsSortMode.None).Length);
        // 舞台层(31)上除了我们自己的光柱以外还有谁：舞台相机会把它们也拍进去(地图里「很低的地方出现暗灰影子」就是查这个)
        sb.Append("\n   舞台层上的渲染器：");
        int others = 0;
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r == null || r.gameObject.layer != StageLayer || r.transform.root.name.StartsWith("StageBeam")) continue;
            if (++others > 15) break;
            sb.Append("\n     " + r.GetType().Name + " " + r.name + " 根=" + r.transform.root.name + " 激活=" + r.gameObject.activeInHierarchy + " 位置=" + r.bounds.center.ToString("F0") + " 大小=" + r.bounds.size.ToString("F0"));
        }
        if (others == 0) sb.Append("无");
        Debug.Log(sb.ToString());
    }

    void GatherStageGeometry()
    {
        foreach (var kv in _batches) kv.Value.m.Clear();
        int world3D = LayerMask.NameToLayer("World3D");
        int w3dTotal = 0, noMesh = 0, culledSmall = 0, included = 0;
        foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy || mr.gameObject.layer != world3D) continue;
            w3dTotal++;
            var mf = mr.GetComponent<MeshFilter>(); var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) { noMesh++; continue; }
            Bounds rb = mr.bounds;
            if (rb.size.x < 40f && rb.size.z < 40f && !AnyExplored(rb)) { culledSmall++; continue; }        // 小件只画靠近走过区域的(着色器里还有精确的遮罩)；大件(地面等)总是画
            included++;
            Matrix4x4 m = _toStage * mr.transform.localToWorldMatrix;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                long key = (long)mesh.GetInstanceID() * 16L + s;
                if (!_batches.TryGetValue(key, out MeshBatch b)) { b = new MeshBatch { mesh = mesh, sub = s }; _batches[key] = b; }
                b.m.Add(m);
            }
        }
        // Unity Terrain(旧工厂这类地图的地面)：没有 MeshRenderer，上面的收集拿不到，地图里地面一片空。
        // 把高度图转成一张网格(缓存，每个 TerrainData 只转一次)，和模型一样画进舞台；只显示走过的区域由着色器的探索遮罩负责。
        foreach (var t in Terrain.activeTerrains)
        {
            if (t == null || !t.isActiveAndEnabled || t.terrainData == null) continue;
            Mesh tm = TerrainMesh(t.terrainData);
            if (tm == null) continue;
            included++;
            long key = (long)tm.GetInstanceID() * 16L;
            if (!_batches.TryGetValue(key, out MeshBatch tb)) { tb = new MeshBatch { mesh = tm, sub = 0 }; _batches[key] = tb; }
            tb.m.Add(_toStage * Matrix4x4.Translate(t.transform.position));
        }
        foreach (var kv in _batches) kv.Value.arr = kv.Value.m.ToArray();
        if (!_diagLogged)
        {
            _diagLogged = true;
            var unit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            LogStageDiag(w3dTotal, noMesh, culledSmall, included, unit != null ? unit.transform.position : Vector3.zero, unit != null);
        }
    }

    // Terrain 高度图 → 网格(最多 128×128 个顶点，对小地图足够)。按 TerrainData 缓存；窗口销毁时一起销毁。
    readonly Dictionary<TerrainData, Mesh> _terrainMeshes = new Dictionary<TerrainData, Mesh>();
    Mesh TerrainMesh(TerrainData td)
    {
        if (_terrainMeshes.TryGetValue(td, out Mesh cached) && cached != null) return cached;
        int hr = td.heightmapResolution;
        int n = Mathf.Clamp(hr, 2, 128);
        float[,] h = td.GetHeights(0, 0, hr, hr);
        Vector3 size = td.size;
        var verts = new Vector3[n * n];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float fx = x / (float)(n - 1), fz = z / (float)(n - 1);
                float y = h[Mathf.RoundToInt(fz * (hr - 1)), Mathf.RoundToInt(fx * (hr - 1))];      // GetHeights 是 [行=z, 列=x]
                verts[z * n + x] = new Vector3(fx * size.x, y * size.y, fz * size.z);
            }
        var tris = new int[(n - 1) * (n - 1) * 6];
        int ti = 0;
        for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                int i0 = z * n + x, i1 = i0 + 1, i2 = i0 + n, i3 = i2 + 1;
                tris[ti++] = i0; tris[ti++] = i2; tris[ti++] = i1;
                tris[ti++] = i1; tris[ti++] = i2; tris[ti++] = i3;
            }
        var m = new Mesh { name = "MinimapTerrain_" + td.name };
        if (verts.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = verts; m.triangles = tris;
        m.RecalculateNormals(); m.RecalculateBounds();
        _terrainMeshes[td] = m;
        return m;
    }

    void DrawStageGeometry()
    {
        if (_holoMat == null || _cam == null) return;
        var rp = new RenderParams(_holoMat)
        {
            layer = StageLayer, camera = _cam, receiveShadows = false,
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            worldBounds = new Bounds(StageOrigin, Vector3.one * 20000f),
        };
        foreach (var kv in _batches)
        {
            var b = kv.Value; int n = b.arr.Length;
            if (b.mesh == null) continue;
            for (int start = 0; start < n; start += 500)                 // 一批最多 500 个实例(常量缓冲区大小限制)
                Graphics.RenderMeshInstanced(rp, b.mesh, b.sub, b.arr, Mathf.Min(500, n - start), start);
        }
    }

    void ReleaseRT() { if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; } }

    // 窗口坐标：_map 的中心为原点(pivot 居中)
    void LayoutMap()
    {
        float aspect = _mm.MapBounds.size.x / Mathf.Max(0.01f, _mm.MapBounds.size.z);
        if (_mode3D) { _mw = _areaW; _mh = _areaH; }
        else { _mw = Mathf.Min(_areaW, _areaH * aspect); _mh = _mw / aspect; }
        _map.anchorMin = _map.anchorMax = new Vector2(0f, 1f); _map.pivot = new Vector2(0.5f, 0.5f);
        _map.anchoredPosition = new Vector2(_areaX + _areaW * 0.5f, -(_areaY + _areaH * 0.5f));
        _map.sizeDelta = new Vector2(_mw, _mh);
        var back = _mapBack.rectTransform;           // 黑底和地图区同位置同大小
        back.anchorMin = back.anchorMax = new Vector2(0f, 1f); back.pivot = new Vector2(0.5f, 0.5f);
        back.anchoredPosition = _map.anchoredPosition; back.sizeDelta = _map.sizeDelta;
        var frame = _mapFrame;                       // 边框和地图区同位置同大小
        frame.anchorMin = frame.anchorMax = new Vector2(0f, 1f); frame.pivot = new Vector2(0.5f, 0.5f);
        frame.anchoredPosition = _map.anchoredPosition; frame.sizeDelta = _map.sizeDelta;
    }

    void SetMode(bool mode3D)
    {
        _mode3D = mode3D && _has3D;
        LayoutMap();
        if (_mode3D)
        {
            ReleaseRT();
            int w = Mathf.Clamp(Mathf.RoundToInt(_mw * _canvasScale), 256, 2560), h = Mathf.Clamp(Mathf.RoundToInt(_mh * _canvasScale), 256, 1600);
            _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "MinimapStageRT", antiAliasing = 4, wrapMode = TextureWrapMode.Clamp };
            _rt.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt;     // Unity 6 的 RenderGraph 看这个字段(见 ScreenSpaceOutlineRTManager)
            _rt.Create();
            _cam.targetTexture = _rt;
            _terrain.texture = _rt; _terrain.material = _premultMat; _terrain.color = Color.white;      // RT 里是预乘透明，要用预乘混合显示，边缘才柔和
        }
        else
        {
            _terrain.texture = _mm.Terrain; _terrain.color = Color.white;
            if (_terrainMat2D != null) _terrain.material = _terrainMat2D;
        }
        if (_boundsFrame != null) _boundsFrame.gameObject.SetActive(_mode3D);
        _mapBack.enabled = _mode3D;                  // 2D 俯视图自带不透明底色，不需要
    }

    // ── 坐标换算(2D = 平面映射；3D = 舞台相机投影) ─────────────────
    Vector3 StagePoint(Vector3 w)
    {
        var c = _mm.MapBounds.center;
        return StageOrigin + new Vector3(w.x - c.x, (w.y - _mm.HfMin) * SkyPrisonMinimap.VisualHeightScale, w.z - c.z);      // 垂直夸张，和网格一致
    }

    bool ToMap(Vector3 w, out Vector2 local)
    {
        if (!_mode3D)
        {
            Vector2 uv = _mm.WorldToUv(w);
            local = new Vector2((uv.x - 0.5f) * _mw, (uv.y - 0.5f) * _mh);
            return true;
        }
        Vector3 vp = _cam.WorldToViewportPoint(StagePoint(w));
        local = new Vector2((vp.x - 0.5f) * _mw, (vp.y - 0.5f) * _mh);
        return vp.z > 0.01f;
    }

    /// <summary>窗口里的光标位置 → 世界坐标：2D 直接映射到玩家所在高度；3D 沿相机射线在高度图上步进找交点。</summary>
    bool MouseWorld(Vector2 lp, float fallbackY, out Vector3 world)
    {
        Vector2 vp = new Vector2(lp.x / _mw + 0.5f, lp.y / _mh + 0.5f);
        if (!_mode3D) { world = _mm.UvToWorld(vp, fallbackY); return true; }

        Ray r = _cam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
        var c = _mm.MapBounds.center;
        float S = SkyPrisonMinimap.VisualHeightScale;      // 舞台里高度被放大了 S 倍：把射线换算回真实世界坐标(仿射变换，直线还是直线)
        Vector3 o = new Vector3(r.origin.x - StageOrigin.x + c.x, _mm.HfMin + (r.origin.y - StageOrigin.y) / S, r.origin.z - StageOrigin.z + c.z);
        Vector3 d = new Vector3(r.direction.x, r.direction.y / S, r.direction.z);
        var b = _mm.MapBounds;
        float maxT = _dist * 4f + Mathf.Max(b.size.x, b.size.z), step = Mathf.Max(0.6f, _dist * 0.004f);
        Vector3 prev = o;
        for (float t = 0f; t < maxT; t += step)
        {
            Vector3 p = o + d * t;
            if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z)
            {
                float h = _mm.HeightAt(p.x, p.z);
                if (p.y <= h) { world = new Vector3(p.x, h, p.z); return true; }
            }
            prev = p;
        }
        // 没打到地形：退回到穿过地图中心高度的水平面
        if (Mathf.Abs(d.y) > 1e-4f)
        {
            float t = (_mm.HfMin + (_mm.HfMax - _mm.HfMin) * 0.3f - o.y) / d.y;
            if (t > 0f)
            {
                // 射线没碰到地形：只有落点在整张地图范围内才算有效(以前是夹到地图边缘，点在地图外的黑底上也会在边缘放下标记)
                Vector3 p = o + d * t; world = p;
                return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;
            }
        }
        world = prev; return false;
    }

    // ── 叠加元素 ─────────────────────────────────────────────
    void BuildZones()
    {
        foreach (var z in SkyPrisonMapZone.All)
        {
            if (z == null || !z.isActiveAndEnabled) continue;
            var zv = new ZoneView { zone = z };
            zv.fill = NewPoly(_zoneLayer, "ZoneFill", new Color(z.color.r, z.color.g, z.color.b, 0.08f + 0.3f * z.intensity), true, 0f);
            zv.line = NewPoly(_zoneLayer, "ZoneLine", new Color(z.color.r, z.color.g, z.color.b, 0.85f), false, 2f * _px);
            string hz = HazardLabel(z);
            string text = ZoneLabel(z) + (string.IsNullOrEmpty(hz) ? "" : "\n" + hz);
            zv.label = Txt(_zoneLayer, text, Mathf.RoundToInt(20f * _px), new Color(z.color.r, z.color.g, z.color.b, 0.95f), TextAlignmentOptions.Center, FontStyles.Bold);
            zv.label.rectTransform.sizeDelta = new Vector2(400f * _px, 80f * _px);
            _zones.Add(zv);
        }
    }

    PolyGraphic NewPoly(RectTransform parent, string name, Color c, bool fill, float thick)
    {
        var rt = Stretch(R(name, parent));
        if (rt.GetComponent<CanvasRenderer>() == null) rt.gameObject.AddComponent<CanvasRenderer>();      // 自定义 Graphic 不会自动补这个组件；缺了既不画，RectMask2D 裁切时还会每帧报错
        var g = rt.gameObject.AddComponent<PolyGraphic>();
        g.color = c; g.fill = fill; g.thick = thick; g.raycastTarget = false;
        return g;
    }

    // 区域名：地图里填的是字典 key 就按语言显示译文，不是 key 就原样显示
    string ZoneLabel(SkyPrisonMapZone z) => string.IsNullOrEmpty(z.label) ? "" : _L(z.label, z.label);

    // 环境危害：自定义说明同样可以是字典 key；预设的四种复用 AFP 里已有的三语译文(灼热 / 电磁 / 腐蚀 / 冻结)
    string HazardLabel(SkyPrisonMapZone z)
    {
        if (!string.IsNullOrEmpty(z.hazardNote)) return _L(z.hazardNote, z.hazardNote);
        switch (z.hazard)
        {
            case SkyPrisonMapZone.Hazard.Heat: return _L("ui_afp_dmg_heat", "灼热");
            case SkyPrisonMapZone.Hazard.Shock: return _L("ui_afp_dmg_shock", "电磁");
            case SkyPrisonMapZone.Hazard.Corrosion: return _L("ui_afp_dmg_corrosion", "腐蚀");
            case SkyPrisonMapZone.Hazard.Freeze: return _L("ui_afp_dmg_freeze", "冻结");
            default: return "";
        }
    }

    void BuildLegend(RectTransform panel, float x, float y, float w)
    {
        var seen = new HashSet<string>(); var items = new List<SkyPrisonMapZone>();
        foreach (var z in SkyPrisonMapZone.All)
        {
            if (z == null || !z.isActiveAndEnabled) continue;
            if (seen.Add(z.label + "|" + ColorUtility.ToHtmlStringRGB(z.color))) items.Add(z);
        }
        if (items.Count == 0) return;
        BuildGroupHeader(panel, x, y, w, _L("minimap_zones", "区域"), _u);
        y += 24f * _u + 10f * _u;
        foreach (var z in items)
        {
            var sw = Img(panel, "Swatch", new Color(z.color.r, z.color.g, z.color.b, 0.35f + 0.5f * z.intensity));
            TopLeft(sw.rectTransform, x, y + 4f * _px, 22f * _px, 22f * _px); sw.raycastTarget = false;
            string hzl = HazardLabel(z);
            var t = Txt(panel, ZoneLabel(z) + (string.IsNullOrEmpty(hzl) ? "" : "  · " + hzl), Mathf.RoundToInt(20f * _px), Color.white, TextAlignmentOptions.TopLeft, FontStyles.Normal);
            TopLeft(t.rectTransform, x + 32f * _px, y, w - 32f * _px, 28f * _px); t.raycastTarget = false;
            y += 32f * _px;
        }
    }

    // 地标只在「本局发现过」之后才显示：位置在走过的区域里(探索记录整局保留，走过就算发现)才画，没走到过的一律不画——撤离点 / AFP 强化 / 箱子都一样。
    // 每 0.5 秒重新收集一次(箱子会被打碎，探索范围也在变)，位置每帧按镜头重算；图例只列本局已经发现的种类。
    void RefreshLandmarks()
    {
        _landUsed = 0;
        bool hasExtract = false, hasAfp = false, hasCrate = false;
        foreach (var e in FindObjectsByType<ExtractionTerminal>(FindObjectsSortMode.None))
            if (Discovered(e.transform.position, 3f)) { hasExtract = true; Land(e.transform.position, SkyPrisonMinimapIcons.Diamond(), new Color(0.4f, 1f, 0.7f, 1f), 22f, _L("minimap_legend_extract", "撤离点")); }
        foreach (var z in FindObjectsByType<SkyPrisonAfpPurchaseZone>(FindObjectsSortMode.None))
            if (Discovered(z.transform.position, Mathf.Max(z.size.x, z.size.y) * 0.5f)) { hasAfp = true; Land(z.transform.position, SkyPrisonMinimapIcons.Disk(), ColdGreen, 20f, _L("minimap_legend_afp", "AFP强化")); }
        foreach (var c in FindObjectsByType<SkyPrisonBreakableCrateEffect>(FindObjectsSortMode.None))
            if (_mm.IsExplored(c.transform.position)) { hasCrate = true; Land(c.transform.position, null, new Color(1f, 0.78f, 0.3f, 0.9f), 10f, ""); }
        // NPC(友军 / 中立无敌意的单位，不含玩家)：和地标同一条规则——位置在走过的区域里才显示；旁边写名字(走哪跟哪)，名字跟语言走
        bool hasNpc = false;
        foreach (var b in FindObjectsByType<UnitDefinitionRuntimeBinder>(FindObjectsSortMode.None))
        {
            var d = b.UnitDefinitionAsset;
            if (d == null || (d.characterIdentity != CharacterIdentity.Ally && d.characterIdentity != CharacterIdentity.NeutralPassive)) continue;
            if (!b.gameObject.activeInHierarchy || !_mm.IsExplored(b.transform.position)) continue;
            string nm = d.GetLocalizedDisplayName();
            hasNpc = true;
            Land(b.transform.position, SkyPrisonMinimapIcons.Ring(), new Color(0.88f, 0.96f, 1f, 0.95f), 15f, nm, b.transform);
        }
        // 敌人(敌人 / 精英 / BOSS / 中立敌意)：红点，只在此刻位于玩家视野内时显示(PositionLandmarks 每帧判断)；这里每 0.5 秒收集一次还活着、激活着的
        bool hasEnemy = false;
        foreach (var b in FindObjectsByType<UnitDefinitionRuntimeBinder>(FindObjectsSortMode.None))
        {
            var d = b.UnitDefinitionAsset;
            if (d == null) continue;
            var id = d.characterIdentity;
            if (id != CharacterIdentity.Enemy && id != CharacterIdentity.Elite && id != CharacterIdentity.Boss && id != CharacterIdentity.NeutralHostile) continue;
            if (!b.gameObject.activeInHierarchy) continue;
            var hp = b.GetComponentInParent<UnitHealthController>();
            if (hp != null && hp.IsDead) continue;
            hasEnemy = true;
            Land(b.transform.position, SkyPrisonMinimapIcons.Disk(), new Color(1f, 0.28f, 0.25f, 0.95f), id == CharacterIdentity.Boss ? 16f : 10f, "", b.transform, true);
        }
        // 任务目标：进行中任务当前目标指向的 NPC / 地点 / 区域。和上面不同，不要求走到过——接了任务就直接标出来
        SkyPrisonQuestMapTargets.Collect(_questTargets);
        foreach (var qt in _questTargets) Land(qt.position, SkyPrisonMinimapIcons.Diamond(), QuestGold, 18f, qt.label, qt.follow);
        bool hasQuest = _questTargets.Count > 0;
        for (int i = _landUsed; i < _landPool.Count; i++) { _landPool[i].on = false; _landPool[i].icon.gameObject.SetActive(false); }
        RefreshMapLegend(hasExtract, hasAfp, hasCrate, hasNpc, hasEnemy, hasQuest);
        PositionLandmarks();                 // 当帧就摆到位
    }

    /// <summary>这个位置(及它周围 radius 米内)本局走到过吗——大一点的东西(AFP 购买区)只要走到过它的一部分就算发现。</summary>
    bool Discovered(Vector3 p, float radius)
    {
        return _mm.IsExplored(p) || _mm.IsExplored(p + new Vector3(radius, 0f, 0f)) || _mm.IsExplored(p - new Vector3(radius, 0f, 0f))
            || _mm.IsExplored(p + new Vector3(0f, 0f, radius)) || _mm.IsExplored(p - new Vector3(0f, 0f, radius));
    }

    // 图例：只列本局已经发现的种类(玩家始终有)，可见的几行从上往下紧凑排列
    static readonly Color QuestGold = new Color(1f, 0.82f, 0.25f, 1f);
    readonly List<SkyPrisonQuestMapTargets.Target> _questTargets = new List<SkyPrisonQuestMapTargets.Target>();

    void RefreshMapLegend(bool extract, bool afp, bool crate, bool npc, bool enemy, bool quest)
    {
        if (_legendRows == null) return;
        bool[] has = { extract, afp, crate, npc, enemy, quest, true };
        float rowH = 22f * _u; int slot = 0;
        for (int i = 0; i < _legendRows.Length; i++)
        {
            _legendRows[i].gameObject.SetActive(has[i]);
            if (!has[i]) continue;
            TopLeft(_legendRows[i], 0f, slot * rowH, 190f * _u, rowH); slot++;
        }
    }

    void Land(Vector3 world, Sprite sprite, Color color, float size, string label, Transform follow = null, bool enemy = false)
    {
        Landmark l;
        if (_landUsed < _landPool.Count) l = _landPool[_landUsed];
        else
        {
            l = new Landmark { icon = Img(_landLayer, "Land", Color.white) };
            l.icon.raycastTarget = false;
            l.icon.rectTransform.anchorMin = l.icon.rectTransform.anchorMax = l.icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            l.label = Txt(l.icon.rectTransform, "", Mathf.RoundToInt(18f * _px), Color.white, TextAlignmentOptions.Top, FontStyles.Bold);
            l.label.outlineColor = new Color32(0, 0, 0, 255); l.label.outlineWidth = 0.3f;      // 黑色描边：全息模型是亮白的，浅色名字压在上面看不清
            l.label.rectTransform.anchorMin = l.label.rectTransform.anchorMax = new Vector2(0.5f, 0f); l.label.rectTransform.pivot = new Vector2(0.5f, 1f);
            l.label.rectTransform.anchoredPosition = new Vector2(0f, -2f); l.label.rectTransform.sizeDelta = new Vector2(120f * _px, 26f * _px); l.label.raycastTarget = false;
            _landPool.Add(l);
        }
        _landUsed++;
        l.world = world; l.on = true; l.follow = follow; l.hadFollow = follow != null; l.enemy = enemy;      // follow：会走动的东西(NPC / 敌人)每帧取它当前的位置
        // 只更新大小，不碰位置：位置由 PositionLandmarks 负责(以前这里把位置重置到窗口中心，每 0.5 秒重新收集一次就让图标闪到中心再弹回来)
        l.icon.rectTransform.sizeDelta = new Vector2(size * _px, size * _px);
        l.icon.sprite = sprite; l.icon.color = color;
        l.label.text = label; l.label.color = color;
    }

    static string FormatDuration(float seconds)
    {
        int t = Mathf.Max(0, Mathf.FloorToInt(seconds));
        int h = t / 3600, m = (t % 3600) / 60, s = t % 60;
        return h.ToString("00") + ":" + m.ToString("00") + ":" + s.ToString("00");
    }

    // 把所有地标 / NPC 按当前镜头投影到窗口坐标。每帧跑一次；重新收集之后也立刻跑一次，免得新收集的图标隔一帧才摆到位
    void PositionLandmarks()
    {
        var b = _mm.MapBounds;
        foreach (var l in _landPool)
        {
            if (!l.on) continue;
            if (l.hadFollow && l.follow == null) { l.icon.gameObject.SetActive(false); continue; }      // 被跟随的 NPC 已经不在了
            Vector3 w = l.world;
            if (l.follow != null) w = l.follow.position;                                                 // NPC / 敌人：取当前位置
            else if (_mode3D) w.y = Mathf.Max(w.y, _mm.HeightAt(w.x, w.z));
            // 敌人只在「此刻在玩家阵营视野里」才标出来(每帧判断，走出视野立刻消失，不留记忆)
            if (l.enemy)
            {
                var vm = SkyPrisonVisionManager.Instance;
                if (vm == null || !vm.IsWorldPositionVisibleToPlayerFaction(w)) { l.icon.gameObject.SetActive(false); continue; }
            }
            bool ok = ToMap(w, out Vector2 lp) && w.x >= b.min.x && w.x <= b.max.x && w.z >= b.min.z && w.z <= b.max.z;
            l.icon.gameObject.SetActive(ok);
            if (ok) l.icon.rectTransform.anchoredPosition = lp;
        }
    }

    // 每帧：按当前镜头把区域 / 地标 / 玩家 / 标记投影到窗口坐标
    void UpdateOverlays(Vector3 playerPos, bool hasPlayer)
    {
        var b = _mm.MapBounds;
        foreach (var zv in _zones)
        {
            if (zv.zone == null) continue;
            var z = zv.zone; Quaternion rot = Quaternion.Euler(0f, z.transform.eulerAngles.y, 0f);
            Vector3 c = z.transform.position; float hx = z.size.x * 0.5f, hz = z.size.y * 0.5f;
            Vector2[] p = new Vector2[4]; bool ok = true;
            Vector3[] corner = { new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 w = c + rot * corner[i]; w.y = _mode3D ? _mm.HeightAt(w.x, w.z) + 0.2f : c.y;
                ok &= ToMap(w, out p[i]);
            }
            zv.fill.gameObject.SetActive(ok); zv.line.gameObject.SetActive(ok); zv.label.gameObject.SetActive(ok);
            if (!ok) continue;
            zv.fill.SetPoints(p[0], p[1], p[2], p[3]); zv.line.SetPoints(p[0], p[1], p[2], p[3]);
            zv.label.rectTransform.anchoredPosition = (p[0] + p[1] + p[2] + p[3]) * 0.25f;
        }
        if (_mode3D)       // 地图范围的外框：给 3D 视图一个空间参照
        {
            float y = _mm.HfMin; Vector2 a, bb, cc, d;
            bool ok = ToMap(new Vector3(b.min.x, y, b.min.z), out a) & ToMap(new Vector3(b.max.x, y, b.min.z), out bb) & ToMap(new Vector3(b.max.x, y, b.max.z), out cc) & ToMap(new Vector3(b.min.x, y, b.max.z), out d);
            _boundsFrame.gameObject.SetActive(ok); if (ok) _boundsFrame.SetPoints(a, bb, cc, d);
        }

        PositionLandmarks();

        if (hasPlayer && ToMap(playerPos, out Vector2 pp))
        {
            _playerIcon.rectTransform.anchoredPosition = pp; _playerIcon.rectTransform.sizeDelta = new Vector2(16f * _px, 16f * _px);
            float k = Mathf.Repeat(Time.unscaledTime * 0.8f, 1f);
            _playerRing.rectTransform.anchoredPosition = pp; _playerRing.rectTransform.sizeDelta = Vector2.one * (16f + 40f * k) * _px;
            _playerRing.color = new Color(1f, 1f, 1f, (1f - k) * 0.6f);
        }
        for (int i = 0; i < SkyPrisonMinimap.MarkerSlots; i++)
        {
            bool set = _mm.MarkerSet[i];
            // 3D 视图里每个标记在舞台上升起一条光柱 + 地面光环(和游戏世界里的光柱是同一套)
            if (set && _mode3D)
            {
                if (_stageBeam[i] == null)
                {
                    _stageMats[i] = new[] { new Material(SkyPrisonMinimapBeam.StageBeamMaterial), new Material(SkyPrisonMinimapBeam.StageRingMaterial), new Material(SkyPrisonMinimapBeam.StageDigitMaterial) };
                    _stageBeam[i] = SkyPrisonMinimapBeam.Create("StageBeam" + i, StageLayer, _stageMats[i][0], _stageMats[i][1], _stageMats[i][2], i + 1);
                }
                _stageBeam[i].SetActive(true);
                _stageBeam[i].transform.position = StagePoint(_mm.MarkerPos[i]);
                float big = Mathf.Max(b.size.x, b.size.z);
                SkyPrisonMinimapBeam.SetShape(_stageBeam[i], big * 0.008f, big * 0.26f);
                SkyPrisonMinimapBeam.Spin(_stageBeam[i], Time.time, i);   // 立体数字绕竖直轴旋转
            }
            else if (_stageBeam[i] != null) _stageBeam[i].SetActive(false);

            // 玩家选的颜色：地图上的图钉、右栏槽位里的图钉(没放置时淡一点)、右端色块、3D 舞台里的光柱
            Color mc = _mm.MarkerColor(i);
            _markIcon[i].color = mc; if (_markNum[i] != null) _markNum[i].color = mc;
            Color rc2 = set ? mc : new Color(mc.r, mc.g, mc.b, 0.45f);
            _rowPin[i].color = rc2; if (_rowPinNum[i] != null) _rowPinNum[i].color = rc2;
            _rowSwatch[i].color = mc;
            Color sc = (_snap == i && set) ? new Color(1f, 0.5f, 0.5f, mc.a) : mc;      // 光标吸附到它(中键=撤销)时，3D 里的光柱 / 地面圆环变淡红
            if (_stageMats[i] != null) foreach (var sm in _stageMats[i]) if (sm != null) sm.SetColor("_Color", sc);

            Vector2 mp = Vector2.zero; bool vis = set && ToMap(_mm.MarkerPos[i], out mp);
            _markIcon[i].gameObject.SetActive(vis);
            if (vis) { _markIcon[i].rectTransform.anchoredPosition = mp; _markIcon[i].rectTransform.sizeDelta = new Vector2(24f * _px, 24f * _px * SkyPrisonMinimapIcons.PinAspect); }      // 标记小一点，地图才大
            // 槽位文字：没放置 = 装饰字体的 NO DATA(40% 透明度)；放了 = 游戏字体的距离。状态变了才换字体(换字体会重排)
            if (set)
            {
                Vector3 dv = _mm.MarkerPos[i] - playerPos; dv.y = 0f;
                if (!_rowSet[i]) { _rowSet[i] = true; _rowName[i].font = SkyPrisonMinimapIcons.GameFont != null ? SkyPrisonMinimapIcons.GameFont : _rowName[i].font; _rowName[i].color = Color.white; }
                _rowName[i].text = dv.magnitude.ToString("0.0") + " m";      // 距离精确到小数点后 1 位
            }
            else if (_rowSet[i] || _rowName[i].text != "NO DATA")
            {
                _rowSet[i] = false;
                if (SkyPrisonMinimapIcons.DecoFont != null) _rowName[i].font = SkyPrisonMinimapIcons.DecoFont;
                _rowName[i].text = "NO DATA"; _rowName[i].color = new Color(1f, 1f, 1f, 0.4f);
            }
            bool selected = i == _slot;
            if (_rowSelected[i] != selected)         // 当前要放的槽：四个角的小方块变冷绿
            {
                _rowSelected[i] = selected;
                foreach (var c in _rowCorners[i]) c.color = selected ? new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, 0.95f) : new Color(1f, 1f, 1f, 0.4f);
            }
        }

        // 战术信息：罗盘跟着镜头转(3D 才有朝向；2D 固定北在上)、探索度、玩家当前位置
        float north = _mode3D ? _yaw : 0f;
        _compassNeedle.localRotation = Quaternion.Euler(0f, 0f, north);
        float na = north * Mathf.Deg2Rad;
        _compassN.rectTransform.anchoredPosition = new Vector2(-Mathf.Sin(na), Mathf.Cos(na)) * (_compassD * 0.5f + 11f * _u);      // 北在屏幕上的方向 = 向上逆时针转 yaw
        _infoExplored.text = Mathf.RoundToInt(_mm.ExploredFraction * 100f) + "%";
        _infoTime.text = FormatDuration(_mm.RaidElapsed);                      // 入局时长(进入这个关卡多久了)
        _infoPos.text = hasPlayer ? "(" + playerPos.x.ToString("0.0") + ", " + playerPos.z.ToString("0.0") + ")" : "(---, ---)";

        // 吸附高亮：光标靠近某个标记时，在它外面套一圈(这时中键是撤销)
        bool snapShow = _snap >= 0 && _mm.MarkerSet[_snap] && ToMap(_mm.MarkerPos[_snap], out Vector2 sp);
        _snapRing.gameObject.SetActive(snapShow);
        if (snapShow)
        {
            sp = ToMapSafe(_mm.MarkerPos[_snap]);
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 8f);
            _snapRing.rectTransform.anchoredPosition = sp + new Vector2(0f, 24f * _px * SkyPrisonMinimapIcons.PinAspect * 0.46f);      // 套在图钉的六边形上
            _snapRing.rectTransform.sizeDelta = Vector2.one * 46f * _px * pulse;
        }
    }

    Vector2 ToMapSafe(Vector3 w) { ToMap(w, out Vector2 l); return l; }

    // ── 每帧 ─────────────────────────────────────────────────
    void Update()
    {
        if (!_built) return;           // 展开 / 收起动画期间不处理输入
        if (_mm == null || !_mm.HasMap) { Close(); return; }

        bool esc = _input != null ? _input.GetActionDown(SkyPrisonInputAction.Menu) : Input.GetKeyDown(KeyCode.Escape);
        if (esc) { Close(); return; }
        // 手柄(Input System，见 SkyPrisonMapGamepad)：B 关闭(地图键 = PS4 触摸板 / Xbox View 由输入路由负责开关)；
        // 当前实际在用手柄时，光标换成地图正中央的准星
        var gp = UnityEngine.InputSystem.Gamepad.current;
        bool gpActive = gp != null && SkyPrisonInputDeviceTracker.Current == SkyPrisonInputDeviceTracker.DeviceFamily.Gamepad;
        if (gp != null && gp.buttonEast.wasPressedThisFrame) { Close(); return; }
        if (_mode3D && (Input.GetKeyDown(KeyCode.Tab) || (gp != null && gp.buttonNorth.wasPressedThisFrame)))        // Tab / 手柄 Y：正交俯视 ⇄ 透视(Tab 已经不是背包键了)
        {
            _ortho = !_ortho;
            if (_ortho) { _savedPitch = _pitch; } else _pitch = _savedPitch;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        }

        // 槽位：默认永远指向「最小的空槽」(按 1–4 编号依次占用)；手动按 1–4 / 点右栏可以指定一个槽(放下后又回到自动)
        for (int i = 0; i < SkyPrisonMinimap.MarkerSlots; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) { if (_slot != i) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); _slot = i; _slotManual = true; }
        if (gp != null && (gp.leftShoulder.wasPressedThisFrame || gp.rightShoulder.wasPressedThisFrame))      // 手柄 LB / RB：上一个 / 下一个槽位
        {
            int n = SkyPrisonMinimap.MarkerSlots;
            _slot = (_slot + (gp.rightShoulder.wasPressedThisFrame ? 1 : n - 1)) % n; _slotManual = true;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        }
        if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete) || (gp != null && gp.buttonWest.wasPressedThisFrame)) { _mm.ClearMarker(_slot); _slotManual = false; }      // 手柄 X：清除当前槽
        if (gp != null && gp.rightStickButton.wasPressedThisFrame) { _mm.CycleMarkerColor(_slot); SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch); }      // 手柄 R3(按下右摇杆)：当前槽换下一种颜色(和点右栏色块一样)
        if (!_slotManual) { int free = FirstFreeSlot(); if (free >= 0) _slot = free; }

        var unit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        bool hasPlayer = unit != null; Vector3 pp = hasPlayer ? unit.transform.position : Vector3.zero;

        Vector2 lp; Vector2 mouse = Input.mousePosition;
        bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(_map, mouse, null, out lp) && _map.rect.Contains(lp);
        if (!gpActive) _gpCursorInit = false;
        if (gp != null)
        {
            // 手柄：LS 移动准星(准星就是「光标」)，推到地图区边缘时继续推 = 平移地图；RS 转动、LT / RT 缩放(速度按镜头距离走，近了慢、远了快)；方向和鼠标一致
            float dt = Time.unscaledDeltaTime;
            Vector2 ls = SkyPrisonMapGamepad.Stick(gp.leftStick.ReadValue()), rs = SkyPrisonMapGamepad.Stick(gp.rightStick.ReadValue());
            Rect mr = _map.rect;
            if (!_gpCursorInit) { _gpCursor = mr.center; _gpCursorInit = true; }
            Vector2 edgePush = Vector2.zero;
            if (ls != Vector2.zero)
            {
                float margin = Mathf.Min(mr.width, mr.height) * 0.06f;
                _gpCursor += ls * (mr.height * 0.85f * dt);
                if (ls.x > 0f && _gpCursor.x >= mr.xMax - margin) edgePush.x = ls.x;
                if (ls.x < 0f && _gpCursor.x <= mr.xMin + margin) edgePush.x = ls.x;
                if (ls.y > 0f && _gpCursor.y >= mr.yMax - margin) edgePush.y = ls.y;
                if (ls.y < 0f && _gpCursor.y <= mr.yMin + margin) edgePush.y = ls.y;
                _gpCursor = new Vector2(Mathf.Clamp(_gpCursor.x, mr.xMin + margin, mr.xMax - margin), Mathf.Clamp(_gpCursor.y, mr.yMin + margin, mr.yMax - margin));
            }
            if (gpActive) { lp = _gpCursor; inside = true; _cross.rectTransform.anchoredPosition = _gpCursor - mr.center; }
            if (_mode3D && edgePush != Vector2.zero)
            {
                Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
                _target += ((yawRot * Vector3.right) * edgePush.x + (yawRot * Vector3.forward) * edgePush.y) * (_dist * 0.5f * dt);
                var sz = _mm.MapBounds.size; _target.x = Mathf.Clamp(_target.x, -sz.x * 0.5f, sz.x * 0.5f); _target.z = Mathf.Clamp(_target.z, -sz.z * 0.5f, sz.z * 0.5f);
            }
            if (_mode3D) {
            if (rs != Vector2.zero) { _yaw += rs.x * 110f * dt; if (!_ortho) _pitch = Mathf.Clamp(_pitch - rs.y * 70f * dt, 12f, 88f); }
            float zoom = gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue();
            if (Mathf.Abs(zoom) > 0.05f) _dist = Mathf.Clamp(_dist * Mathf.Exp(-zoom * 1.3f * dt), MinDist, MaxDist);
            }
        }

        // 3D 镜头：右键拖动旋转、中键拖动平移、滚轮缩放。中键「点一下」(没怎么动)是放置 / 撤销标记，拖动才是平移
        if (Input.GetMouseButtonDown(2)) { _mmbDown = inside; _mmbMoved = 0f; }
        if (_mmbDown && Input.GetMouseButton(2))
        {
            Vector2 dlt = mouse - _lastMouse; _mmbMoved += dlt.magnitude;
            if (_mode3D && _mmbMoved > 6f)
            {
                float k = _dist * 0.0016f / Mathf.Max(0.5f, _canvasScale);
                Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
                _target -= (yawRot * Vector3.right) * dlt.x * k + (yawRot * Vector3.forward) * dlt.y * k;
                var sz = _mm.MapBounds.size; _target.x = Mathf.Clamp(_target.x, -sz.x * 0.5f, sz.x * 0.5f); _target.z = Mathf.Clamp(_target.z, -sz.z * 0.5f, sz.z * 0.5f);
            }
        }
        if (_mode3D)
        {
            if (Input.GetMouseButtonDown(1)) { _rmbDown = inside; _rmbMoved = 0f; }
            if (_rmbDown && Input.GetMouseButton(1))
            {
                Vector2 dlt = mouse - _lastMouse; _rmbMoved += dlt.magnitude;
                if (_rmbMoved > 6f) { _yaw += dlt.x * 0.25f; if (!_ortho) _pitch = Mathf.Clamp(_pitch - dlt.y * 0.25f, 12f, 88f); }      // 正交俯视时俯仰角锁死(正对下方)，只能转方向
            }
            if (inside && Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f) _dist = Mathf.Clamp(_dist * (1f - Input.mouseScrollDelta.y * 0.1f), MinDist, MaxDist);
        }
        if (Input.GetMouseButtonUp(1)) _rmbDown = false;
        _lastMouse = mouse;

        if (_mode3D)
        {
            if (Time.unscaledTime >= _nextGather) { _nextGather = Time.unscaledTime + 2f; GatherStageGeometry(); }      // 走过的区域在变，隔一会儿重新收集一次要画的模型
            UpdateStageCamera();
        }

        // 吸附：光标靠近某个已放的标记时，吸附到它上面(高亮)，这时中键是撤销它；否则中键在光标处放一个新标记
        _snap = inside ? NearestMarker(lp) : -1;
        bool mmbClick = Input.GetMouseButtonUp(2) && _mmbDown && _mmbMoved <= 6f;
        if (gp != null && gp.buttonSouth.wasPressedThisFrame) mmbClick = true;      // 手柄 A：在准星处放置 / 撤销标记
        if (Input.GetMouseButtonUp(2)) _mmbDown = false;
        if (inside && MouseWorld(lp, pp.y, out Vector3 world))
        {
            Vector3 shown = _snap >= 0 ? _mm.MarkerPos[_snap] : world;
            _cursorText.color = _snap >= 0 ? new Color(1f, 0.55f, 0.55f, 1f) : Color.white;      // 吸附到已有标记(中键=撤销)时底部坐标变淡红
            _cursorText.text = (_snap >= 0 ? _L("minimap_marker_n", "标记 {0}").Replace("{0}", (_snap + 1).ToString()) + "   " : "") + "(" + shown.x.ToString("0.0") + ", " + shown.z.ToString("0.0") + ")";      // 二元坐标 (x, z)
            if (mmbClick)
            {
                if (_snap >= 0) { _mm.ClearMarker(_snap); SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel); _slotManual = false; }
                else if (_slotManual || FirstFreeSlot() >= 0)
                {
                    // 接地：竖直位置用游戏统一的地面查询(角色能站的地面)，不用点到的墙顶 / 高度图最高点
                    world.y = _mm.GroundY(world.x, world.z, pp.y);
                    _mm.PlaceMarker(_slot, world); SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm); _slotManual = false;
                }
                else SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);      // 4 个都放满了：先靠近一个撤销
            }
        }
        else
        {
            _cursorText.text = "";
            if (mmbClick && inside) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);      // 点在地图范围外：不能标记
        }

        UpdateOverlays(pp, hasPlayer);
        if (Time.unscaledTime >= _nextLandmarks) { _nextLandmarks = Time.unscaledTime + 0.5f; RefreshLandmarks(); }
    }

    int FirstFreeSlot()
    {
        for (int i = 0; i < SkyPrisonMinimap.MarkerSlots; i++) if (!_mm.MarkerSet[i]) return i;
        return -1;
    }

    /// <summary>光标附近(吸附半径内)最近的已放标记；没有返回 -1。</summary>
    int NearestMarker(Vector2 lp)
    {
        int best = -1; float bestD = 46f * _px;
        for (int i = 0; i < SkyPrisonMinimap.MarkerSlots; i++)
        {
            if (!_mm.MarkerSet[i] || !ToMap(_mm.MarkerPos[i], out Vector2 mp)) continue;
            float d = Vector2.Distance(lp, mp);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    // ── 关闭 ─────────────────────────────────────────────────
    void Close()
    {
        if (_instance != this) return;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Close);
        SkyPrisonWindowHintBar.GetOrCreate().Clear();
        SkyPrisonWindowHintBar.SetEdgeTop(false);
        SkyPrisonWindowManager_V1.ExternalBlock = _savedExternalBlock;
        RestoreCombatHud();
        _instance = null; _lastCloseFrame = Time.frameCount;
        _built = false;
        StartCoroutine(FoldRoutine(1f, 0f, () => Destroy(gameObject)));      // 收成中间一条横线再销毁
    }

    // 开 / 关动画：和购买窗口一致，从中间一条横线展开，关闭时压回去(关闭期间冻结磨砂采样区，免得它跟着缩放变形)
    IEnumerator FoldRoutine(float from, float to, System.Action done)
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
        done?.Invoke();
    }

    void OnDestroy()
    {
        foreach (var tm in _terrainMeshes.Values) if (tm != null) Destroy(tm);      // Terrain 转出来的网格是运行时建的，自己销毁
        _terrainMeshes.Clear();
        if (_instance == this) { SkyPrisonWindowManager_V1.ExternalBlock = _savedExternalBlock; _instance = null; }      // 场景切换等外力销毁：把挡输入还回去
        // HUD 隐藏请求是计数器，必须还回去(正常 Close 已经还过，这里只兜底外力销毁)
        if (_hudHideRequestCounted)
        {
            SkyPrisonWindowManager_V1.ExternalHudHideRequests = Mathf.Max(0, SkyPrisonWindowManager_V1.ExternalHudHideRequests - 1);
            _hudHideRequestCounted = false;
            if (_windowManagerForHud != null) _windowManagerForHud.RefreshHudVisibility();
            if (_quickItemFadeTarget != null) _quickItemFadeTarget.alpha = 1f;
        }
        ReleaseRT();
        for (int i = 0; i < _stageBeam.Length; i++) if (_stageBeam[i] != null) Destroy(_stageBeam[i]);
        for (int i = 0; i < _stageMats.Length; i++) if (_stageMats[i] != null) foreach (var sm in _stageMats[i]) if (sm != null) Destroy(sm);
        if (_stageRoot != null) Destroy(_stageRoot);
        if (_holoMat != null) Destroy(_holoMat);
        if (_premultMat != null) Destroy(_premultMat);
        if (_terrainMat2D != null) Destroy(_terrainMat2D);
    }

    // ── 战斗 HUD 的隐藏 / 恢复：做法和购买窗口、角色面板一致 ─────────────────
    // HP/LP 条等走 windowManager 统一的 ExternalHudHideRequests 计数器(多个窗口互不打架)；快捷栏提示条是独立的根 Canvas，自己淡出淡入。
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
        if (_windowManagerForHud == null) _windowManagerForHud = FindFirstObjectByType<SkyPrisonWindowManager_V1>();
        if (_windowManagerForHud != null)
        {
            if (!_hudHideRequestCounted) { SkyPrisonWindowManager_V1.ExternalHudHideRequests++; _hudHideRequestCounted = true; }
            _windowManagerForHud.RefreshHudVisibility();
        }
        if (_quickItemFadeTarget == null)
        {
            var strip = FindFirstObjectByType<SkyPrisonQuickItemPromptStrip>();
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

    // ── 小工具 ────────────────────────────────────────────────
    static RectTransform R(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static RectTransform Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; return rt; }

    static void TopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
    }

    static void Center(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    static Image Img(Transform parent, string name, Color c)
    {
        var rt = R(name, parent);
        var img = rt.gameObject.AddComponent<Image>(); img.color = c;
        return img;
    }

    /// <summary>游戏统一的 UI 字体(各悬浮窗用的那个)，不是系统字体。decorative = true 用「东亚重工」装饰字体(占位字 NO DATA 之类)。</summary>
    TMP_Text Txt(Transform parent, string text, float size, Color color, TextAlignmentOptions align, FontStyles style, bool decorative = false)
    {
        var rt = R("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var f = decorative ? SkyPrisonMinimapIcons.DecoFont : SkyPrisonMinimapIcons.GameFont;
        if (f != null) t.font = f;
        t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.fontStyle = style;
        t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow; t.raycastTarget = false;
        return t;
    }

    /// <summary>沿一个矩形外缘画四条细线(outset：往外扩多少)。</summary>
    static void Border(RectTransform rt, float thick, Color c, float outset = 0f)
    {
        var holder = Stretch(R("Border", rt)); holder.offsetMin = new Vector2(-outset, -outset); holder.offsetMax = new Vector2(outset, outset);
        // 四条线互不重叠：上下两条通长，左右两条夹在它们中间。(半透明的线在拐角处如果重叠，叠加后那一小块会更亮。)
        Line(holder, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thick), Vector2.zero, c);
        Line(holder, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, thick), c);
        Line(holder, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, thick), new Vector2(thick, -thick), c);
        Line(holder, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thick, thick), new Vector2(0f, -thick), c);
    }

    static void Line(RectTransform parent, Vector2 amin, Vector2 amax, Vector2 omin, Vector2 omax, Color c)
    {
        var img = Img(parent, "L", c); img.raycastTarget = false;
        var rt = img.rectTransform; rt.anchorMin = amin; rt.anchorMax = amax; rt.offsetMin = omin; rt.offsetMax = omax;
    }
}
