using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 全屏选本地图（运行时代码搭建，占位美术）。节点用 0~1 归一化坐标摆在底图上；
/// 未解锁的本不创建节点。选中节点 → 右侧详情 → 出发 → SceneLoader.EnterChapter。
/// requiresSubway 的本先播放地铁转场（目前是黑屏文字占位）。
/// </summary>
public sealed class SkyPrisonRaidMapScreen : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    static SkyPrisonRaidMapScreen inst;

    // 所有界面文字走字典表（UILocalizationTable，Resources 里的同一张表）；表里没有这个 key 就用后面的中文兜底
    static UILocalizationTable locTable; static bool locTableLoaded;
    public static string LocText(string key, string fallback) { return L(key, fallback); }
    static string L(string key, string fallback)
    {
        if (!locTableLoaded) { locTable = Resources.Load<UILocalizationTable>("UILocalizationTable"); locTableLoaded = true; }
        return locTable != null ? locTable.Get(key, fallback) : fallback;
    }

    SkyPrisonWindowHint[] hintsBase, hintsNight;
    // 键鼠和手柄各一套图标：同一条提示带两个 key，提示条按当前实际在用的设备挑；只有一边有 key 的提示在另一种设备下自动隐藏
    static SkyPrisonWindowHint Hint(string kbKey, string kbText, string gpKey, string gpText, string label)
        => new SkyPrisonWindowHint { iconKey = kbKey, gamepadIconKey = gpKey, fallbackText = kbText, label = label };
    void BuildHints()
    {
        var switchRaid = Hint("keyboard/a", "A", "gamepad/xbox/lb", "LB", "");                                   // 两个键并排（A D / LB RB），文字放在第二个后面
        var switchRaid2 = Hint("keyboard/d", "D", "gamepad/xbox/rb", "RB", L("raid_hint_switch_raid", "切换 RAID"));
        var tab = Hint("keyboard/tab", "Tab", "gamepad/xbox/y", "Y", L("raid_hint_switch_daynight", "切换昼夜"));
        var pan = Hint("mouse/left", L("raid_hint_drag", "拖拽"), "gamepad/xbox/l", "LS", L("raid_hint_pan", "平移"));
        var zoom = Hint("mouse/scroll", L("raid_hint_wheel", "滚轮"), "gamepad/xbox/rt", "RT", L("raid_hint_zoom", "缩放"));
        var orbit = Hint("mouse/right", L("raid_hint_rclick", "右键"), "gamepad/xbox/r", "RS", L("raid_hint_orbit", "转动视角"));
        var confirm = Hint(null, "", "gamepad/xbox/a", "A", L("raid_hint_confirm", "选定 / 出发"));   // 键鼠靠点击，不需要这条
        var close = Hint("keyboard/esc", "Esc", "gamepad/xbox/b", "B", L("ui_hint_close", "关闭"));
        hintsBase = new[] { switchRaid, switchRaid2, confirm, pan, zoom, orbit, close };
        hintsNight = new[] { switchRaid, switchRaid2, tab, confirm, pan, zoom, orbit, close };   // 夜版已开放时，切换昼夜紧跟在「切换 RAID」后面
    }

    readonly List<RaidDefinition> shown = new List<RaidDefinition>();
    RaidDefinition selected;
    Text title, desc, goText, outLabel;
    Text raidTag; RectTransform titleLine, outLine;
    RectTransform outGrid; SkyPrison.Runtime.UI.InventoryItemDetailPanel outDetail; Transform screenRoot; GameObject detailCanvasGo;
    float tipHoldUntil;   // 物品详情卡刚收起的这一小段时间内，鼠标底下的据点不要抢选中项
    bool nightSel;
    sealed class VariantToggle { public Image[] lines; public Text txt; public RectTransform rt; public bool hover; public Image icon; public Text newBadge; }
    VariantToggle dayTg, nightTg;
    RawImage impression; Text impressionHint; Material impressionMat, impressionStatic; bool tabHintShown;
    Button goBtn;
    RectTransform mapArea;
    CanvasGroup subwayFade;
    Text subwayText;
    bool departing;
    SkyPrisonRaidMapViewport view;
    // 详情面板只在鼠标停在据点节点（或面板本身）上时显示，离开后稍作停留再淡出
    CanvasGroup panelGroup; bool overNode, overPanel; float hoverGrace;
    SkyPrison.Runtime.UI.SkyPrisonHUDRoot_V1 hudRoot; bool hudWasVisible;   // 全屏地图盖在 HUD 上时，把 HUD（快捷栏 1234 等）整体藏起来
    SkyPrisonRaidDioramaController diorama; RenderTexture dioramaRT, markerRT; Camera markerCam;
    Material blurMat, panelBlurMat;
    RectTransform detailMask, detailPanel; float panelOpen; Image goImg; Image[] btnLines;
    readonly List<SkyPrisonRaidDioramaMarker> markers = new List<SkyPrisonRaidDioramaMarker>();
    readonly List<RaidDefinition> markerRaids = new List<RaidDefinition>();
    RaidDefinition hoverMarkerRaid;
    readonly List<Text> nodeLabels = new List<Text>();
    Vector2 downPos; bool downOnTarget; bool locked;   // locked：已选中锁定（点击 / 键盘切换），面板和全息保持，点空白处解除
    readonly List<RaidDefinition> dioramaRaids = new List<RaidDefinition>();
    readonly List<KeyValuePair<RectTransform, Vector3>> dioramaNodes = new List<KeyValuePair<RectTransform, Vector3>>();
    readonly List<CanvasGroup> nodeFades = new List<CanvasGroup>();
    readonly List<RectTransform> nodeLeaders = new List<RectTransform>();
    static Sprite hexMarker;
    // 悬停反馈：鼠标指到据点（模型或节点）时，在该据点所有网格上追加冷绿色全息扫描线材质
    public static Transform DebugForceHover;
    Material holoMat; float holoFade; Transform hoverSite, litSite; bool overBuilding;
    readonly Dictionary<Renderer, Material[]> origMats = new Dictionary<Renderer, Material[]>();
    static readonly Dictionary<string, string> SiteByRaid = new Dictionary<string, string> { { "old_factory", "Site_OldFactory" }, { "school", "Site_School" }, { "golden_land", "Site_GoldenKingdom" }, { "hospital", "Site_Hospital" }, { "tower", "Tower" } };
    const float NodesAppearAt = 4.8f;   // 升起动画基本完成之后才显示节点

    public static void Open(SkyPrisonRaidDeployPoint point)
    {
        if (IsOpen) return;
        var go = new GameObject("RaidMapScreen");
        inst = go.AddComponent<SkyPrisonRaidMapScreen>();
        inst.Build(point);
        IsOpen = true;
        SkyPrisonWindowManager_V1.ExternalBlock = true;
        FreezeWorld();
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapOpen);
    }

    // 选关界面打开期间把余穹世界冻住（和暂停菜单一样 Time.timeScale=0）：这个界面自己的动画、镜头、淡入淡出全部用
    // unscaledDeltaTime / 实时时间，不受影响。关闭 / 销毁 / 出发时恢复到打开前的值（幂等，多处调用没关系）。
    static bool worldFrozen; static float savedTimeScale = 1f;
    static void FreezeWorld()
    {
        if (worldFrozen) return;
        savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f; worldFrozen = true;
    }
    static void ThawWorld()
    {
        if (!worldFrozen) return;
        Time.timeScale = savedTimeScale; worldFrozen = false;
    }

    // Esc 同一次按键：这里的 Update() 先关掉界面，SkyPrisonPlayerInputRouter（执行顺序更靠后）同一帧再读到
    // Menu=true，看到 IsOpen 已经是 false 就把暂停菜单打开了——表现为"在选关界面按 Esc 弹出暂停菜单"。
    // 记下关闭发生的帧号，路由那边这一帧不再处理 Menu（和 PauseMenuController 防"同帧关了又开"是同一套做法）。
    static int lastCloseFrame = -1;
    public static bool JustClosedThisFrame { get { return lastCloseFrame == Time.frameCount; } }

    public static void Close()
    {
        if (inst != null) Destroy(inst.gameObject);
        if (IsOpen) lastCloseFrame = Time.frameCount;
        ThawWorld();
        inst = null; IsOpen = false;
        SkyPrisonWindowManager_V1.ExternalBlock = false;
    }

    void OnDestroy()
    {
        if (detailCanvasGo != null) Destroy(detailCanvasGo);
        if (inst == this || inst == null) ThawWorld();
        if (inst == this) { if (IsOpen) lastCloseFrame = Time.frameCount; inst = null; IsOpen = false; SkyPrisonWindowManager_V1.ExternalBlock = false; }
        if (hudRoot != null && hudWasVisible)
        {
            if (!departing) hudRoot.SetHUDVisible(true);
            else
            {   // 出发进本：此刻恢复 HUD 会让左下角快捷栏（独立嵌套画布）冒在读条画面上；等目标场景揭幕后再恢复。
                var h = hudRoot; System.Action onReveal = null;
                onReveal = () => { SceneLoader.OnRevealComplete -= onReveal; if (h != null) h.SetHUDVisible(true); };
                SceneLoader.OnRevealComplete += onReveal;
            }
        }
        if (tabHintShown) { SkyPrisonWindowHintBar.PopContext(); tabHintShown = false; }
        SkyPrisonWindowHintBar.SetAlignLeft(false);
        SkyPrisonWindowHintBar.GetOrCreate().Clear();
        if (diorama != null) Destroy(diorama.gameObject);
        if (dioramaRT != null) { dioramaRT.Release(); Destroy(dioramaRT); }
        if (markerRT != null) { markerRT.Release(); Destroy(markerRT); }
    }

    // 字体：用项目里设置好的默认字体（和提示条、背包同一个来源），取不到才退回 Unity 内置字体
    static Font cfgFont;
    static Font F
    {
        get
        {
            var f = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentFont() : null;
            if (f == null)
            {
                // 运行时单例还没起来（或被销毁）时，直接读本地化配置里默认语言的字体，不要退回内置字体
                if (cfgFont == null)
                {
                    var cfg = Resources.Load<LocalizationRuntimeConfig>("SkyPrison/LocalizationRuntimeConfig");
                    var def = cfg != null && cfg.settings != null ? cfg.settings.GetDefaultLanguage() : null;
                    cfgFont = def != null ? def.primaryFont : null;
                }
                f = cfgFont;
            }
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }

    static RectTransform Rect(string n, Transform parent, Color? bg = null)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(parent, false);
        if (bg.HasValue) go.AddComponent<Image>().color = bg.Value;
        return go.GetComponent<RectTransform>();
    }

    static Text Label(string n, Transform parent, int size, TextAnchor a, Color c)
    {
        var r = Rect(n, parent); var t = r.gameObject.AddComponent<Text>();
        t.font = F; t.fontSize = size; t.alignment = a; t.color = c; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    /// <summary>设置顶部影像图：按 2:1 居中裁切；没配图就显示"影像待补"占位。</summary>
    void SetImpression(Sprite s, string placeholder)
    {
        if (impression == null) return;
        if (s == null || s.texture == null)
        {
            // 没有影像 / 未公开：显示和读档界面空槽位一样的无信号雪花屏
            if (impressionStatic == null)
            {
                var sh = Shader.Find("UI/SkyPrison/TVStatic");
                if (sh != null) { impressionStatic = new Material(sh) { hideFlags = HideFlags.HideAndDontSave }; impressionStatic.SetFloat("_UseUnscaled", 1f); }   // 选关界面冻结了世界（timeScale=0），雪花要用不缩放时间才会动
            }
            if (impressionStatic != null)
            {
                impression.texture = null; impression.uvRect = new Rect(0, 0, 1, 1);
                impression.material = impressionStatic; impression.color = Color.white; impression.enabled = true;
                if (impressionHint != null) impressionHint.text = "";
            }
            else
            {
                impression.enabled = false; impression.texture = null;
                if (impressionHint != null) impressionHint.text = placeholder;
            }
            return;
        }
        var tex = s.texture; float ta = tex.width / (float)tex.height; const float target = 2.3f;
        // 取 sprite 在贴图上的矩形，再按 2:1 居中裁切
        var sr = s.textureRect; float tw = tex.width, th = tex.height;
        float rw = sr.width, rh = sr.height, ra = rw / rh;
        float cw = ra > target ? rh * target : rw, ch = ra > target ? rh : rw / target;
        float cx = sr.x + (rw - cw) * 0.5f, cy = sr.y + (rh - ch) * 0.5f;
        if (impressionMat != null) { impression.material = impressionMat; impression.color = new Color(0.92f, 0.92f, 0.94f, 1f); }
        else impression.material = null;
        impression.texture = tex; impression.uvRect = new Rect(cx / tw, cy / th, cw / tw, ch / th);
        impression.enabled = true;
        if (impressionHint != null) impressionHint.text = "";
    }

    VariantToggle MakeToggle(Transform parent, string name, string label, Sprite ico, float x0, float x1, float y0, float y1, System.Action onClick)
    {
        var rt = Rect(name, parent, new Color(0f, 0f, 0f, 0.001f)); Anchor(rt, x0, y0, x1, y1);
        var tg = new VariantToggle { rt = rt };
        tg.lines = new Image[]
        {
            FrameLine(rt, "T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -2), new Vector2(0, 0)),
            FrameLine(rt, "B", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 2)),
            FrameLine(rt, "L", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(2, 0)),
            FrameLine(rt, "R", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-2, 0), new Vector2(0, 0)),
        };
        tg.txt = Label(name + "Text", rt, 26, TextAnchor.MiddleCenter, Color.white); Stretch(tg.txt.rectTransform); tg.txt.text = label;
        tg.txt.rectTransform.offsetMin = new Vector2(34f, 0f);                       // 给左边的图标让出位置
        var icoRt = Rect("Icon", rt); icoRt.anchorMin = icoRt.anchorMax = new Vector2(0f, 0.5f); icoRt.pivot = new Vector2(0f, 0.5f);
        icoRt.sizeDelta = new Vector2(34f, 34f); icoRt.anchoredPosition = new Vector2(14f, 0f);
        tg.icon = icoRt.gameObject.AddComponent<Image>(); tg.icon.sprite = ico; tg.icon.raycastTarget = false;
        var hitA = Rect("Hit", rt, new Color(0f, 0f, 0f, 0.001f));            // 感应区比可见框大一圈
        hitA.anchorMin = Vector2.zero; hitA.anchorMax = Vector2.one; hitA.offsetMin = new Vector2(-10f, -10f); hitA.offsetMax = new Vector2(10f, 10f);
        // 左上角的 NEW 小字：和背包新道具同一个样式（无底、淡红字、呼吸闪），悬停看过后消除
        tg.newBadge = Label(name + "New", rt, 16, TextAnchor.UpperLeft, NewBadgeColor); tg.newBadge.horizontalOverflow = HorizontalWrapMode.Overflow;
        tg.newBadge.rectTransform.anchorMin = tg.newBadge.rectTransform.anchorMax = new Vector2(0f, 1f); tg.newBadge.rectTransform.pivot = new Vector2(0f, 0f);
        tg.newBadge.rectTransform.anchoredPosition = new Vector2(2f, 3f); tg.newBadge.rectTransform.sizeDelta = new Vector2(60f, 20f);
        tg.newBadge.text = L("raid_new", "NEW"); tg.newBadge.gameObject.SetActive(false);
        var btn = rt.gameObject.AddComponent<Button>(); btn.transition = Selectable.Transition.None; btn.onClick.AddListener(() => onClick());
        AddHover(rt.gameObject, () => { overPanel = true; tg.hover = true; if (tg == nightTg) MarkNightSeen(); RefreshVariantUI(); }, () => { overPanel = false; hoverGrace = 0.35f; tg.hover = false; RefreshVariantUI(); });
        return tg;
    }

    // 夜版第一次开放：夜按钮上有 NEW，鼠标悬停过（或已经切到夜）就算看过，之后不再出现。按 RAID 分开记。
    static readonly Color NewBadgeColor = new Color(1f, 0.45f, 0.45f, 1f);   // 淡红，和背包 NEW 一致
    static string NightSeenKey(RaidDefinition r) { return "SkyPrison_RaidNightSeen_" + r.raidId; }
    bool NightIsNew { get { return selected != null && selected.IsNightUnlocked && PlayerPrefs.GetInt(NightSeenKey(selected), 0) == 0; } }
    void MarkNightSeen()
    {
        if (selected == null || PlayerPrefs.GetInt(NightSeenKey(selected), 0) != 0) return;
        PlayerPrefs.SetInt(NightSeenKey(selected), 1); PlayerPrefs.Save();
        if (nightTg != null && nightTg.newBadge != null) nightTg.newBadge.gameObject.SetActive(false);
    }

    void SetNight(bool night)
    {
        if (selected == null) return;
        if (night && !selected.IsNightUnlocked) { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden); return; }            // 夜版没开放：点不动
        bool changed = nightSel != (night && selected.hasNight);
        nightSel = night && selected.hasNight;
        if (nightSel) MarkNightSeen();   // 切到夜版也算看过了
        if (changed) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapDayNight);
        PlayerPrefs.SetInt("SkyPrison_RaidNight_" + selected.raidId, nightSel ? 1 : 0);
        UpdateInfoTexts();
    }


    // ── 数据来源：以地图编辑器为准（名字、介绍、昼夜主要产出）；地图编辑器没配的才退回 RAID 资产自己的字段 ──
    static string RaidName(RaidDefinition r)
    {
        var reg = MapLocalizationRegistry.LoadOrNull();
        return reg != null ? reg.GetDisplayName(r.targetScene, r.Name) : r.Name;
    }

    static string RaidDesc(RaidDefinition r)
    {
        var reg = MapLocalizationRegistry.LoadOrNull();
        return reg != null ? reg.GetDescription(r.targetScene, "") : "";   // 只认地图编辑器里写的介绍，不再回退到 RAID 资源里的旧占位文字
    }

    static List<ItemDefinition> RaidOutputs(RaidDefinition r, bool night)
    {
        var reg = MapLocalizationRegistry.LoadOrNull();
        var e = reg != null ? reg.FindByScene(r.targetScene) : null;
        if (e == null) return null;
        return night && e.raidNightOutputs != null && e.raidNightOutputs.Count > 0 ? e.raidNightOutputs : e.raidDayOutputs;
    }

    void UpdateInfoTexts()
    {
        var r = selected;
        if (r == null) { title.text = ""; raidTag.text = ""; desc.text = ""; SetImpression(null, ""); RebuildOutputs(null, false); RefreshVariantUI(); return; }
        bool night = nightSel && r.IsNightUnlocked;
        title.text = RaidName(r); raidTag.text = RaidNo(r);
        SetImpression(!Visited(r, night) ? null : (night && r.nightThumbnail != null ? r.nightThumbnail : r.thumbnail),   // 没去过这个版本 = 未公开，显示雪花屏
             L("raid_image_pending", "影像待补"));
        string d = RaidDesc(r);
        if (night) { var nd = r.NightDesc; if (!string.IsNullOrEmpty(nd)) d += "\n\n" + nd; }
        desc.text = d;
        goText.text = (r.requiresSubway ? L("raid_depart_subway", "乘地铁出发") : L("raid_depart", "出发"));
        goBtn.interactable = !string.IsNullOrEmpty(r.targetScene) && r.CanEnter(night);
        RebuildOutputs(r, night);
        RefreshVariantUI();
    }

    /// <summary>主要物资产出：一排图标（昼夜各一组，从地图编辑器读），鼠标放上去显示物品详情，复用背包的物品详情面板。</summary>
    void RebuildOutputs(RaidDefinition r, bool night)
    {
        if (outGrid == null) return;
        if (outDetail != null) outDetail.Hide();
        for (int i = outGrid.childCount - 1; i >= 0; i--) Destroy(outGrid.GetChild(i).gameObject);
        var items = r != null ? RaidOutputs(r, night) : null;
        bool any = items != null && items.Count > 0;
        outLabel.gameObject.SetActive(any); outGrid.gameObject.SetActive(any); LayoutOutLine();
        if (!any) return;
        const float Cell = 78f, Gap = 10f; const int PerRow = 5;
        for (int i = 0; i < items.Count; i++)
        {
            var def = items[i]; if (def == null) continue;
            var cell = Rect("Out" + i, outGrid, new Color(0f, 0f, 0f, 0.35f));
            cell.anchorMin = cell.anchorMax = new Vector2(0f, 1f); cell.pivot = new Vector2(0f, 1f);
            cell.sizeDelta = new Vector2(Cell, Cell);
            cell.anchoredPosition = new Vector2((i % PerRow) * (Cell + Gap), -(i / PerRow) * (Cell + Gap));
            var frame = new Image[]
            {
                FrameLine(cell, "T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -1.5f), new Vector2(0, 0)),
                FrameLine(cell, "B", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1.5f)),
                FrameLine(cell, "L", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1.5f, 0)),
                FrameLine(cell, "R", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-1.5f, 0), new Vector2(0, 0)),
            };
            foreach (var f in frame) f.color = new Color(1f, 1f, 1f, 0.35f);
            var icon = Rect("Icon", cell); icon.anchorMin = Vector2.zero; icon.anchorMax = Vector2.one; icon.offsetMin = new Vector2(7f, 7f); icon.offsetMax = new Vector2(-7f, -7f);
            bool known = SkyPrisonItemDiscovery.IsDiscovered(def);          // 没持有过的物品：图标换成「?」，不显示详情
            var iim = icon.gameObject.AddComponent<Image>(); iim.sprite = def.icon; iim.preserveAspect = true; iim.raycastTarget = false; iim.enabled = known && def.icon != null;
            if (!known)
            {
                var q = Label("Unknown", cell, 46, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.55f)); Stretch(q.rectTransform); q.text = "?";
                foreach (var f in frame) f.color = new Color(1f, 1f, 1f, 0.22f);
            }
            cell.GetComponent<Image>().raycastTarget = true;
            var capturedDef = def; var capturedFrame = frame; var capturedKnown = known;
            AddHover(cell.gameObject,
                () => { overPanel = true; foreach (var f in capturedFrame) f.color = new Color(0.35f, 1f, 0.7f, 1f); if (outDetail != null && capturedKnown) outDetail.Show(capturedDef, null, detailPanel); },
                () => { overPanel = false; hoverGrace = 0.35f; tipHoldUntil = Time.unscaledTime + 0.7f; foreach (var f in capturedFrame) f.color = new Color(1f, 1f, 1f, capturedKnown ? 0.35f : 0.22f); if (outDetail != null) outDetail.Hide(); });
        }
    }

    void RefreshVariantUI()
    {
        if (dayTg == null || nightTg == null) return;
        var r = selected;
        bool has = r != null && r.IsNightUnlocked;           // 夜版没开放（或没有夜版）就整排昼 / 夜都不显示，只剩一个版本没得选
        dayTg.rt.gameObject.SetActive(has); nightTg.rt.gameObject.SetActive(has);
        // 整排不显示时，下面的「主要产出」整体上移补位，不留空档
        float shift = has ? 0f : 0.065f;
        if (outLabel != null) { outLabel.rectTransform.anchorMin = new Vector2(0.08f, 0.548f + shift); outLabel.rectTransform.anchorMax = new Vector2(0.92f, 0.58f + shift); }
        if (outGrid != null) { outGrid.anchorMin = new Vector2(0.08f, 0.385f + shift); outGrid.anchorMax = new Vector2(0.92f, 0.54f + shift); }
        LayoutOutLine();
        if (!has) return;
        bool night = nightSel;
        PaintToggle(dayTg, !night, true);
        PaintToggle(nightTg, night, true);
        if (nightTg.newBadge != null) nightTg.newBadge.gameObject.SetActive(NightIsNew);
    }

    static void PaintToggle(VariantToggle tg, bool selectedNow, bool available)
    {
        Color c = !available ? new Color(1f, 1f, 1f, 0.28f)
                : selectedNow ? new Color(0.35f, 1f, 0.7f, 1f)
                : new Color(1f, 1f, 1f, tg.hover ? 1f : 0.6f);
        foreach (var l in tg.lines) l.color = c;
        tg.txt.color = c;
        if (tg.icon != null) tg.icon.color = c;
    }

    // ── 昼夜图标：运行时画的描边小图标（64×64，白色，带抗锯齿），颜色由状态决定 ──
    static Sprite sunSprite, moonSprite;

    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a; float h = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * h)).magnitude;
    }

    static Sprite MakeIcon(System.Func<Vector2, float> sdf)      // sdf：到笔画中心线的距离，减掉半个笔画宽 → <0 在笔画内
    {
        const int N = 64; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = sdf(new Vector2(x + 0.5f - N * 0.5f, y + 0.5f - N * 0.5f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d)));          // 1 像素抗锯齿
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 64f);
    }

    static Sprite SunSprite()
    {
        if (sunSprite != null) return sunSprite;
        sunSprite = MakeIcon(p =>
        {
            const float half = 2.4f;                                       // 半个笔画宽
            float d = Mathf.Abs(p.magnitude - 11f) - half;                  // 中间的圆环
            for (int k = 0; k < 8; k++)                                     // 8 根放射线
            {
                float a = k * Mathf.PI / 4f; var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                d = Mathf.Min(d, SegDist(p, dir * 17.5f, dir * 26f) - half);
            }
            return d;
        });
        return sunSprite;
    }

    static Sprite MoonSprite()
    {
        if (moonSprite != null) return moonSprite;
        moonSprite = MakeIcon(p =>
        {
            const float half = 2.4f;
            float outer = (p - new Vector2(-3f, 0f)).magnitude - 22f;       // 大圆
            float inner = 18f - (p - new Vector2(8f, 7f)).magnitude;         // 被挖掉的小圆
            float shape = Mathf.Max(outer, inner);                           // 月牙的有符号距离（内部为负）
            return Mathf.Abs(shape) - half;                                  // 只画轮廓
        });
        return moonSprite;
    }

    const float PanelW = 0.30f, PanelExtra = 0.06f, PanelFull = PanelW + PanelExtra;   // 内容区占屏宽 30%；底板再向左多出 6%，渐隐放在多出来的部分
    /// <summary>让底板子物体向左比内容区多出 PanelExtra（相对内容区宽度）。</summary>
    static void ExtendLeft(RectTransform r)
    {
        r.anchorMin = new Vector2(-PanelExtra / PanelW, 0f); r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
    }

    /// <summary>鼠标是否在详情窗口的范围内（按几何判断，不依赖悬停事件——事件在子控件之间切换时会有空档）。</summary>
    bool PointerInPanel()
    {
        return panelOpen > 0.05f && detailMask != null && RectTransformUtility.RectangleContainsScreenPoint(detailMask, Input.mousePosition, null);
    }

    static Sprite leftFadeSprite;
    /// <summary>64×2 的水平渐变：左边 12% 从透明升到不透明，其余全不透明。压暗层用它，和毛玻璃底的左边缘淡出对齐。</summary>
    static Sprite LeftFadeSprite()
    {
        if (leftFadeSprite != null) return leftFadeSprite;
        const int W = 128; var tex = new Texture2D(W, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int x = 0; x < W; x++)
        {
            float f = Mathf.Clamp01(((x + 0.5f) / W) / 0.19f); float a = f * f * (3f - 2f * f);
            for (int y = 0; y < 2; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        leftFadeSprite = Sprite.Create(tex, new Rect(0, 0, W, 2), new Vector2(0.5f, 0.5f), 100f);
        return leftFadeSprite;
    }

    static RectTransform ThinLine(Transform parent, string name)
    {
        var r = Rect(name, parent, new Color(1f, 1f, 1f, 0.3f));
        r.GetComponent<Image>().raycastTarget = false;
        return r;
    }

    static string RaidNo(RaidDefinition r) { return "RAID-" + (r.order > 0 ? r.order : 1).ToString("00"); }

    /// <summary>「主要产出」文字右边的细线：从文字右端往后空一小段，一直拉到窗口右边；产出整体上移时跟着走。</summary>
    void LayoutOutLine()
    {
        if (outLine == null || outLabel == null) return;
        float y = (outLabel.rectTransform.anchorMin.y + outLabel.rectTransform.anchorMax.y) * 0.5f - 0.006f;   // 文字靠下，线略低于中心
        outLine.anchorMin = new Vector2(0.08f, y); outLine.anchorMax = new Vector2(0.92f, y);
        float textW = outLabel.preferredWidth;
        outLine.offsetMin = new Vector2(textW + 16f, -0.5f); outLine.offsetMax = new Vector2(0f, 0.5f);
        outLine.gameObject.SetActive(outLabel.gameObject.activeSelf);
    }

    static Image FrameLine(Transform parent, string n, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var r = Rect("Line" + n, parent, new Color(1f, 1f, 1f, 0.85f));
        r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = oMin; r.offsetMax = oMax;
        var im = r.GetComponent<Image>(); im.raycastTarget = false; return im;
    }

    // ── 长按出发：按住鼠标左键 / 手柄 A，沿方框的边从上边正中顺时针绕一整圈，绕回上边正中才真正出发；松手匀速回弹到 0 ──
    const float HoldSeconds = 1.0f, ReleaseSeconds = 0.3f;
    static readonly Color HoldColor = new Color(0.35f, 1f, 0.7f, 1f);
    bool mouseHold, gpHold; float holdProgress; bool holdFired;
    RectTransform goRect; RectTransform[] holdSegs; Text holdHint;

    void BuildHoldButton(RectTransform btnRect, RectTransform panel)
    {
        goRect = btnRect;
        var trig = btnRect.GetComponent<EventTrigger>() ?? btnRect.gameObject.AddComponent<EventTrigger>();
        var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown }; down.callback.AddListener(e => { var pe = e as PointerEventData; if (pe == null || pe.button == PointerEventData.InputButton.Left) mouseHold = true; }); trig.triggers.Add(down);
        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp }; up.callback.AddListener(_ => mouseHold = false); trig.triggers.Add(up);
        // 五段冷绿线，顺时针一圈：右半上边 → 右边（向下）→ 整条下边（向左）→ 左边（向上）→ 左半上边（回到上边正中），依次接力往外长
        holdSegs = new RectTransform[5];
        var specs = new[] {   // anchor, pivot
            new[]{0.5f,1f, 0f,1f}, new[]{1f,1f, 1f,1f}, new[]{1f,0f, 1f,0f},
            new[]{0f,0f, 0f,0f}, new[]{0f,1f, 0f,1f} };
        for (int i = 0; i < 5; i++)
        {
            var r = Rect("HoldSeg" + i, btnRect, HoldColor); r.GetComponent<Image>().raycastTarget = false;
            r.anchorMin = r.anchorMax = new Vector2(specs[i][0], specs[i][1]); r.pivot = new Vector2(specs[i][2], specs[i][3]);
            r.anchoredPosition = Vector2.zero; r.sizeDelta = Vector2.zero; holdSegs[i] = r;
        }
        // 「长按出发」小提示：按钮正下方一行小字
        holdHint = Label("HoldHint", panel, 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.4f));
        Anchor(holdHint.rectTransform, 0.2f, 0.016f, 0.8f, 0.05f); holdHint.text = L("raid_hold_hint", "长按出发");
    }

    void HoldUpdate()
    {
        if (goRect == null || holdSegs == null) return;
        bool can = !departing && goBtn != null && goBtn.interactable && panelOpen > 0.5f && selected != null;
        bool holding = can && (mouseHold || gpHold);
        float dt = Time.unscaledDeltaTime;
        if (holding) holdProgress = Mathf.Min(1f, holdProgress + dt / HoldSeconds);
        else { holdProgress = Mathf.Max(0f, holdProgress - dt / ReleaseSeconds); holdFired = false; }
        if (holdProgress >= 1f && !holdFired && can) { holdFired = true; mouseHold = false; gpHold = false; Depart(); }
        ApplyHoldVisual();
    }

    void ApplyHoldVisual()
    {
        float w = goRect.rect.width, h = goRect.rect.height, half = w * 0.5f, thick = 3f;
        float dist = holdProgress * (2f * w + 2f * h);   // 周长，从上边正中出发顺时针
        float[] lens =
        {
            Mathf.Clamp(dist, 0f, half),
            Mathf.Clamp(dist - half, 0f, h),
            Mathf.Clamp(dist - half - h, 0f, w),
            Mathf.Clamp(dist - half - h - w, 0f, h),
            Mathf.Clamp(dist - half - h - w - h, 0f, half)
        };
        for (int i = 0; i < 5; i++)
        {
            bool isSide = (i == 1 || i == 3);
            holdSegs[i].sizeDelta = isSide ? new Vector2(thick, lens[i]) : new Vector2(lens[i], thick);
            holdSegs[i].gameObject.SetActive(lens[i] > 0.01f);
        }
        if (goText != null && holdProgress > 0f) goText.color = Color.Lerp(Color.white, HoldColor, holdProgress);
        if (holdHint != null) holdHint.color = new Color(1f, 1f, 1f, holdProgress > 0f ? 0.15f : 0.4f);
    }

    // 出发框是长按的「槽」：没填充的部分是暗灰（悬停时稍亮一点），冷绿只留给「已经按出来的进度」，所以悬停不再把线和字变绿。
    static readonly Color BtnTrack = new Color(0.34f, 0.36f, 0.40f, 1f), BtnTrackHot = new Color(0.55f, 0.58f, 0.63f, 1f);
    void SetBtnHot(bool hot)
    {
        var line = hot ? BtnTrackHot : BtnTrack;
        if (btnLines != null) foreach (var l in btnLines) l.color = line;
        if (goText != null && holdProgress <= 0f) goText.color = Color.white;
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    static void Anchor(RectTransform r, float x0, float y0, float x1, float y1)
    { r.anchorMin = new Vector2(x0, y0); r.anchorMax = new Vector2(x1, y1); r.offsetMin = r.offsetMax = Vector2.zero; }

    void Build(SkyPrisonRaidDeployPoint point)
    {
        hudRoot = FindFirstObjectByType<SkyPrison.Runtime.UI.SkyPrisonHUDRoot_V1>();
        if (hudRoot != null) { hudWasVisible = hudRoot.HudVisible; hudRoot.SetHUDVisible(false); }
        var cv = gameObject.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 5000;
        var sc = gameObject.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var root = Rect("Root", transform, new Color(0.03f, 0.05f, 0.08f, 0.97f)); Stretch(root);

        // 地图区：整座城市底图（Resources/UI/Raid/RaidCityMap），按原比例铺满屏幕；节点用归一化坐标放在底图上。
        // 没有底图时退回纯色占位方块。换图只要替换这张 png（保持 16:9 最好）。
        var cityMap = Resources.Load<Sprite>("UI/Raid/RaidCityMap");
        var dioPrefab = Resources.Load<GameObject>("UI/Raid/RaidDiorama");
        if (dioPrefab != null) cityMap = cityMap != null ? cityMap : null;
        if (cityMap != null || dioPrefab != null)
        {
            var vp = Rect("Viewport", root, new Color(0f, 0f, 0f, 0f)); Stretch(vp);          // 全屏、接收滚轮和拖动
            mapArea = Rect("MapFrame", vp);
            mapArea.anchorMin = mapArea.anchorMax = mapArea.pivot = new Vector2(0.5f, 0.5f);
            var fit = mapArea.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; fit.aspectRatio = cityMap != null ? cityMap.rect.width / cityMap.rect.height : 16f / 9f;
            if (dioPrefab != null)
            {
                // 3D 沙盘渲染到 RT，铺在地图框里；沙盘放在世界远处，只被它自己的相机拍到
                dioramaRT = new RenderTexture(1920, 1080, 24, RenderTextureFormat.DefaultHDR) { name = "RaidDioramaRT", antiAliasing = 1, useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear };
                var dgo = Instantiate(dioPrefab, new Vector3(0f, -3000f, 0f), Quaternion.identity); dgo.name = "RaidDiorama";
                diorama = dgo.GetComponent<SkyPrisonRaidDioramaController>();
                diorama.cam.targetTexture = dioramaRT; diorama.cam.aspect = 16f / 9f; diorama.cam.depth = -100f;
                var raw = Rect("DioramaImage", mapArea); Stretch(raw);
                var ri = raw.gameObject.AddComponent<RawImage>(); ri.texture = dioramaRT; ri.raycastTarget = false;
                var blurShader = Resources.Load<Shader>("Shaders/DioramaEdgeBlur");          // 边缘景深：中心清晰、四周用 mip 糊开
                if (blurShader != null) { blurMat = new Material(blurShader); ri.material = blurMat; }
                // 标记和沙盘用同一台相机、同一张 RT 渲染（不再单独一台相机，避免两者有任何相对偏移）；边缘模糊在标记所在区域自动关掉，保持清晰
            }
            else
            {
                var mi = Rect("MapImage", mapArea, Color.white); Stretch(mi);
                var mimg = mi.GetComponent<Image>(); mimg.sprite = cityMap; mimg.raycastTarget = false;
            }
            if (diorama != null) vp.gameObject.AddComponent<SkyPrisonRaidDioramaInput>().target = diorama;   // 3D 沙盘：相机真缩放
            else { view = vp.gameObject.AddComponent<SkyPrisonRaidMapViewport>(); view.viewport = vp; view.content = mapArea; }
        }
        else
        {
            mapArea = Rect("MapArea", root, new Color(0.07f, 0.1f, 0.14f, 1f));
            Anchor(mapArea, 0.04f, 0.08f, 0.68f, 0.92f);
        }
        if (diorama != null) AddStageVignette(root);   // 舞台暗角：屏幕边缘压黑，视线集中到中间的聚光圈
        if (diorama == null)   // 3D 沙盘不要左上角的标题；2D 兜底版保留
        {
            var tl = Label("MapTitle", root, 34, TextAnchor.UpperLeft, new Color(0.6f, 0.9f, 1f));
            tl.text = L("raid_map_title", "RAID  ·  出发地图"); Anchor(tl.rectTransform, 0.04f, 0.93f, 0.6f, 0.99f);
        }

        // 右侧详情窗口：贴右边、整条高度，从右往左展开。
        // 做法：遮罩容器（RectMask2D）的宽度从 0 长到 30%，里面的内页固定大小、贴在容器右边——内页不缩放，像从屏幕右侧拉出来。
        var mask = Rect("DetailMask", root); mask.gameObject.AddComponent<RectMask2D>(); Anchor(mask, 1f - PanelFull, 0f, 1f, 1f); detailMask = mask;
        var panel = Rect("Detail", mask, new Color(0.02f, 0.02f, 0.03f, 0.45f)); detailPanel = panel; panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);   // 本体只负责内容和悬停检测，底色画在下面向左多出一截的 Dim / Frost 上
        panel.anchorMin = new Vector2(1f, 0f); panel.anchorMax = new Vector2(1f, 1f); panel.pivot = new Vector2(1f, 0.5f); panel.anchoredPosition = Vector2.zero;
        if (dioramaRT != null && blurMat != null)   // 毛玻璃底：把沙盘那张 RT 对应屏幕右侧 30% 的区域取出来，黑白 + 重度模糊 + 压暗，透出后面的沙盘
        {
            panelBlurMat = new Material(blurMat.shader);
            panelBlurMat.SetFloat("_ClearRadius", 0f); panelBlurMat.SetFloat("_FeatherRadius", 0.05f); panelBlurMat.SetFloat("_MaxLod", 4.2f);
            panelBlurMat.SetFloat("_Gray", 1f); panelBlurMat.SetFloat("_Blur9", 1f); panelBlurMat.SetFloat("_MarkerOn", 0f);
            panelBlurMat.SetFloat("_FadeX0", 1f - PanelFull); panelBlurMat.SetFloat("_FadeW", PanelFull * 0.19f);   // 底板整体向左多出 PanelExtra，渐隐落在多出的那一截上（再往里一点点）
            var bgr = Rect("Frost", panel); ExtendLeft(bgr); bgr.SetAsFirstSibling();
            var bri = bgr.gameObject.AddComponent<RawImage>(); bri.texture = dioramaRT; bri.uvRect = new Rect(1f - PanelFull, 0f, PanelFull, 1f);
            bri.material = panelBlurMat; bri.color = new Color(0.34f, 0.34f, 0.37f, 1f); bri.raycastTarget = false;
        }
        var dim = Rect("Dim", panel, new Color(0.02f, 0.02f, 0.03f, 0.45f)); ExtendLeft(dim); dim.SetSiblingIndex(1);   // 压暗层，左边 22% 渐隐，和毛玻璃底对齐
        var dimImg = dim.GetComponent<Image>(); dimImg.sprite = LeftFadeSprite(); dimImg.raycastTarget = false;
        panelGroup = panel.gameObject.AddComponent<CanvasGroup>(); panelGroup.alpha = 0f; panelGroup.blocksRaycasts = false;
        AddHover(panel.gameObject, () => overPanel = true, () => { overPanel = false; hoverGrace = 0.35f; });
        // 顶部影像图（旧工厂印象图）：2:1 居中裁切、黑白处理，外面一圈细白框；没配图时显示占位
        var imgFrame = Rect("Impression", panel, new Color(0f, 0f, 0f, 0.35f)); Anchor(imgFrame, 0.08f, 0.775f, 0.92f, 0.97f);
        var imgRaw = Rect("Img", imgFrame); Stretch(imgRaw);
        impression = imgRaw.gameObject.AddComponent<RawImage>(); impression.raycastTarget = false; impression.enabled = false;
        if (blurMat != null)
        {
            impressionMat = new Material(blurMat.shader);   // 复用模糊着色器当"黑白滤镜"：清晰半径给大 = 不模糊，灰度拉满
            impressionMat.SetFloat("_ClearRadius", 9f); impressionMat.SetFloat("_FeatherRadius", 0.05f); impressionMat.SetFloat("_MaxLod", 0f);
            impressionMat.SetFloat("_Gray", 1f); impressionMat.SetFloat("_Blur9", 0f); impressionMat.SetFloat("_MarkerOn", 0f);
            impression.material = impressionMat; impression.color = new Color(0.92f, 0.92f, 0.94f, 1f);
        }
        impressionHint = Label("ImgHint", imgFrame, 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.35f)); Stretch(impressionHint.rectTransform);
        FrameLine(imgFrame, "T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -1), new Vector2(0, 0)).color = new Color(1f, 1f, 1f, 0.5f);
        FrameLine(imgFrame, "B", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1)).color = new Color(1f, 1f, 1f, 0.5f);
        FrameLine(imgFrame, "L", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0)).color = new Color(1f, 1f, 1f, 0.5f);
        FrameLine(imgFrame, "R", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-1, 0), new Vector2(0, 0)).color = new Color(1f, 1f, 1f, 0.5f);
        raidTag = Label("RaidTag", panel, 22, TextAnchor.LowerLeft, new Color(0.55f, 0.92f, 0.78f, 0.7f)); Anchor(raidTag.rectTransform, 0.08f, 0.73f, 0.92f, 0.76f);   // 旧工厂上面的冷绿小字 RAID-01
        title = Label("Title", panel, 54, TextAnchor.LowerLeft, Color.white); Anchor(title.rectTransform, 0.08f, 0.668f, 0.92f, 0.742f);
        titleLine = ThinLine(panel, "TitleLine");                                         // 标题下面一条极细的横线
        Anchor(titleLine, 0.08f, 0.664f, 0.92f, 0.664f); titleLine.offsetMin = new Vector2(0f, -0.5f); titleLine.offsetMax = new Vector2(0f, 0.5f);
        screenRoot = root;
        outLabel = Label("OutLabel", panel, 20, TextAnchor.LowerLeft, new Color(1f, 1f, 1f, 0.55f)); Anchor(outLabel.rectTransform, 0.08f, 0.548f, 0.92f, 0.58f); outLabel.text = L("raid_main_output", "主要产出");
        outLine = ThinLine(panel, "OutLine");                                              // 「主要产出」右边的极细横线
        outGrid = Rect("OutGrid", panel); Anchor(outGrid, 0.08f, 0.385f, 0.92f, 0.54f);
        // 物品详情卡：直接用背包那一套（同一个 InventoryItemDetailPanel 组件）。它的尺寸是按 3840×2160 的画布设计的，
        // 选关界面自己的画布参考分辨率是 1920×1080，放进去会整体大一倍——所以给它单独开一个和背包一致的根画布
        // （3840×2160、同样的缩放方式），这样大小、字号、位置计算都和背包里鼠标悬停时一模一样，以后背包那边改了这里自动同步。
        detailCanvasGo = new GameObject("OutputDetailCanvas", typeof(RectTransform));
        var dcv = detailCanvasGo.AddComponent<Canvas>(); dcv.renderMode = RenderMode.ScreenSpaceOverlay; dcv.sortingOrder = 5001;
        var dsc = detailCanvasGo.AddComponent<CanvasScaler>();
        dsc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; dsc.referenceResolution = new Vector2(3840f, 2160f); dsc.matchWidthOrHeight = 0.5f;
        if (dioramaRT != null && blurMat != null)
        {   // 详情卡的背景要和右侧窗口一样是「黑白 + 高斯模糊」的磨砂。卡片会在自己所在画布里找一个叫 "Blur" 的 RawImage 来借贴图 / 材质 / 颜色，
            // 并自己按卡片所在的屏幕位置设 uvRect（整张全屏 RT 按屏幕比例取）。背包那边这个节点放的是背包自己的磨砂；
            // 这里放沙盘的 RT + 同一支黑白模糊着色器，卡片就会磨砂沙盘。这个节点自己不显示，只是给卡片取参数。
            var cardBlurMat = new Material(blurMat.shader);
            cardBlurMat.SetFloat("_ClearRadius", 0f); cardBlurMat.SetFloat("_FeatherRadius", 0.05f); cardBlurMat.SetFloat("_MaxLod", 4.2f);
            cardBlurMat.SetFloat("_Gray", 1f); cardBlurMat.SetFloat("_Blur9", 1f); cardBlurMat.SetFloat("_MarkerOn", 0f); cardBlurMat.SetFloat("_FadeW", 0f);
            var blurHolder = new GameObject("Blur", typeof(RectTransform)); blurHolder.transform.SetParent(detailCanvasGo.transform, false);
            var blurRaw = blurHolder.AddComponent<RawImage>(); blurRaw.texture = dioramaRT; blurRaw.material = cardBlurMat;
            blurRaw.color = new Color(0.28f, 0.28f, 0.31f, 1f); blurRaw.raycastTarget = false; blurRaw.enabled = false;
        }
        var outDetailGo = new GameObject("OutputDetail", typeof(RectTransform)); outDetailGo.transform.SetParent(detailCanvasGo.transform, false);
        outDetail = outDetailGo.AddComponent<SkyPrison.Runtime.UI.InventoryItemDetailPanel>();
        desc = Label("Desc", panel, 26, TextAnchor.UpperLeft, new Color(0.78f, 0.8f, 0.83f)); Anchor(desc.rectTransform, 0.08f, 0.16f, 0.92f, 0.335f);   // 和上面的产出图标拉开一段距离
        BuildRuler(root);   // 挂在整屏根上而不是右侧窗口里：窗口收起时刻度也一直在
        // 昼 / 夜 切换：两个并排的空心小框；选中的是冷绿，没选的是白，夜版没开放就灰掉
        dayTg = MakeToggle(panel, "DayTg", L("raid_day", "昼"), SunSprite(), 0.08f, 0.46f, 0.585f, 0.645f, () => SetNight(false));
        nightTg = MakeToggle(panel, "NightTg", L("raid_night", "夜"), MoonSprite(), 0.54f, 0.92f, 0.585f, 0.645f, () => SetNight(true));
        // 出发按钮：空心的矩形框（四条细线），里面是空的；悬停才把线和字变成冷绿（和标记变绿同一个反馈）
        var btnRect = Rect("Go", panel, new Color(0f, 0f, 0f, 0.001f)); Anchor(btnRect, 0.20f, 0.05f, 0.80f, 0.108f);   // 水平居中   // 近乎全透明，只为了接收点击 / 悬停
        goImg = btnRect.GetComponent<Image>();
        btnLines = new Image[]
        {
            FrameLine(btnRect, "T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -2), new Vector2(0, 0)),
            FrameLine(btnRect, "B", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 2)),
            FrameLine(btnRect, "L", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(2, 0)),
            FrameLine(btnRect, "R", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-2, 0), new Vector2(0, 0)),
        };
        var hitArea = Rect("Hit", btnRect, new Color(0f, 0f, 0f, 0.001f));          // 感应区：四周各外扩一圈，指到框附近就算
        hitArea.anchorMin = Vector2.zero; hitArea.anchorMax = Vector2.one; hitArea.offsetMin = new Vector2(-18f, -16f); hitArea.offsetMax = new Vector2(18f, 16f);
        goBtn = btnRect.gameObject.AddComponent<Button>(); goBtn.transition = Selectable.Transition.None; /* 出发改成长按：见 HoldUpdate，不再用点击 */
        goText = Label("GoText", btnRect, 30, TextAnchor.MiddleCenter, Color.white); Stretch(goText.rectTransform);
        AddHover(btnRect.gameObject,
            () => { overPanel = true; SetBtnHot(true); },
            () => { overPanel = false; hoverGrace = 0.35f; SetBtnHot(false); mouseHold = false; });   // 按住时手滑出了框 = 取消
        BuildHoldButton(btnRect, panel);
        SetBtnHot(false);   // 初始就是暗灰的槽

        BuildHints();
        SkyPrisonWindowHintBar.GetOrCreate().Show(hintsBase);
        SkyPrisonWindowHintBar.SetAlignLeft(true);   // 按键提示靠左：右边有详情窗口，靠左不会被挡   // 按键提示：和背包同一条提示条（屏幕底部居中）

        if (view != null)   // 2D 底图的缩放按钮（3D 沙盘靠滚轮 / 拖动，不再放按钮）
        {
            string[] lab = { "＋", "－", "⌂" };
            System.Action[] act = diorama != null
                ? new System.Action[] { () => diorama.ZoomBy(1f / 1.5f), () => diorama.ZoomBy(1.5f), () => diorama.ResetView() }
                : new System.Action[] { () => view.ZoomButton(1.5f), () => view.ZoomButton(1f / 1.5f), () => view.ResetView() };
            for (int i = 0; i < 3; i++)
            {
                var b = Rect("Zoom" + i, root, new Color(0.15f, 0.2f, 0.28f, 0.92f)); Anchor(b, 0.03f, 0.22f - i * 0.075f, 0.065f, 0.285f - i * 0.075f);
                var act0 = act[i]; b.gameObject.AddComponent<Button>().onClick.AddListener(() => act0());
                var tx = Label("T", b, 34, TextAnchor.MiddleCenter, Color.white); tx.text = lab[i]; Stretch(tx.rectTransform);
            }
        }

        var fade = Rect("Subway", root, Color.black); Stretch(fade);
        subwayFade = fade.gameObject.AddComponent<CanvasGroup>(); subwayFade.alpha = 0; subwayFade.blocksRaycasts = false;
        subwayText = Label("ST", fade, 44, TextAnchor.MiddleCenter, Color.white); Stretch(subwayText.rectTransform);

        var list = new List<RaidDefinition>(point.raids); list.Sort((a, b) => a.order.CompareTo(b.order));
        foreach (var r in list)
        {
            if (r == null || !r.IsUnlocked) continue;   // 未解锁 = 完全不显示
            shown.Add(r);
            if (diorama != null)
            {   // 3D 沙盘里已有对应的 3D 标记（真正在模型上方）→ 不再建 UI 节点
                SkyPrisonRaidDioramaMarker mk = null;
                SkyPrisonRaidDioramaMarker template = null;
                foreach (var cand in diorama.GetComponentsInChildren<SkyPrisonRaidDioramaMarker>(true)) { if (template == null) template = cand; if (cand.raidId == r.raidId) mk = cand; }
                if (mk == null && template != null && SiteFor(r) != null)
                {   // 沙盘预制体里只摆了旧工厂的标记：其他 RAID 照它复制一份（同样的六边形 / 引线 / 衬底 / NEW 标），再绑到各自的据点上
                    mk = Instantiate(template, template.transform.parent);
                    mk.name = "Marker_" + r.raidId; mk.raidId = r.raidId;
                }
                if (mk != null) { var st0 = SiteFor(r); if (st0 != null) mk.AttachTo(st0); mk.SetText(TacticalLabelRich(r)); mk.SetNew(PlayerPrefs.GetInt("SkyPrison_RaidSeen_" + r.raidId, 0) == 0); mk.SetAlpha(0f); markers.Add(mk); markerRaids.Add(r); continue; }
            }
            var n = Rect("Node_" + r.raidId, mapArea, new Color(0.2f, 0.8f, 1f, 1f));
            n.anchorMin = n.anchorMax = r.mapPosition; n.sizeDelta = new Vector2(46, 46);
            if (diorama != null) { var mi2 = n.GetComponent<Image>(); mi2.sprite = HexMarkerSprite(); mi2.color = new Color(0.86f, 0.95f, 1f, 1f); n.sizeDelta = new Vector2(72, 72); }
            var captured = r;
            n.gameObject.AddComponent<Button>().onClick.AddListener(() => { Select(captured); FocusRaid(captured); locked = true; });
            var hit = Rect("Hit", n); hit.anchorMin = hit.anchorMax = new Vector2(0.5f, 0.5f); hit.sizeDelta = new Vector2(140f, 140f);   // 比可见方块大一圈的悬停区
            var hitImg = hit.gameObject.AddComponent<Image>(); hitImg.color = new Color(0f, 0f, 0f, 0f); hitImg.raycastTarget = true;
            AddHover(n.gameObject, () => { Select(captured); overNode = true; }, () => { overNode = false; hoverGrace = 0.35f; });
            AddHover(hit.gameObject, () => { Select(captured); overNode = true; }, () => { overNode = false; hoverGrace = 0.35f; });
            if (view != null) view.Register(n);
            if (diorama != null)
            {
                dioramaRaids.Add(r);
                dioramaNodes.Add(new KeyValuePair<RectTransform, Vector3>(n, new Vector3(-80f + r.mapPosition.x * 160f, 10f, -45f + r.mapPosition.y * 90f)));
                var lead = Rect("Leader", n, new Color(0.86f, 0.95f, 1f, 0.55f)); lead.pivot = new Vector2(0.5f, 1f); lead.anchorMin = lead.anchorMax = new Vector2(0.5f, 0.5f);
                lead.GetComponent<Image>().raycastTarget = false; lead.anchoredPosition = new Vector2(0, -34f); lead.sizeDelta = new Vector2(2f, 40f); nodeLeaders.Add(lead);
                var cg = n.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0f; cg.blocksRaycasts = false; nodeFades.Add(cg);
            }
            var nl = Label("L", n, 26, TextAnchor.UpperCenter, Color.white);
            nl.rectTransform.anchorMin = nl.rectTransform.anchorMax = new Vector2(0.5f, diorama != null ? 1f : 0f);
            nl.rectTransform.sizeDelta = new Vector2(300, 40);
            nl.rectTransform.anchoredPosition = diorama != null ? new Vector2(0, 30) : new Vector2(0, -2);
            if (diorama != null) { nl.alignment = TextAnchor.LowerCenter; nl.fontSize = 28; }
            nl.text = diorama != null ? TacticalLabel(r) : r.Name;
            if (diorama != null) { nl.fontSize = 20; nl.lineSpacing = 1.1f; nl.color = Color.white; nl.raycastTarget = true; nodeLabels.Add(nl);
                AddHover(nl.gameObject, () => { Select(captured); overNode = true; }, () => { overNode = false; hoverGrace = 0.35f; }); nl.rectTransform.sizeDelta = new Vector2(360, 60); }
        }
        Select(shown.Count > 0 ? shown[0] : null);
    }

    /// <summary>标准 smoothstep(edge0, edge1, x)。（Mathf.SmoothStep 的参数是 from/to/t，不是这个，之前误用过。）</summary>
    bool TryMapRay(out Ray ray)
    {
        ray = default;
        if (mapArea == null || diorama == null || diorama.cam == null) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapArea, Input.mousePosition, null, out var lp)) return false;
        var rc = mapArea.rect; var nrm = new Vector2((lp.x - rc.xMin) / rc.width, (lp.y - rc.yMin) / rc.height);
        if (nrm.x < 0f || nrm.x > 1f || nrm.y < 0f || nrm.y > 1f) return false;
        ray = diorama.cam.ViewportPointToRay(new Vector3(nrm.x, nrm.y, 0f)); return true;
    }

    void FocusRaid(RaidDefinition r)
    {
        if (diorama == null || r == null) return;
        var site = SiteFor(r);
        Vector3 wp = site != null ? SiteBounds(site).center : diorama.ToWorld(new Vector3(-80f + r.mapPosition.x * 160f, 0f, -45f + r.mapPosition.y * 90f));
        // 只把镜头移到该 RAID，不改缩放；右边 30% 被详情窗口占了，所以把据点放在左边 70% 可见区域的中央（画面 x≈0.35），标记和文字不会被窗口盖住
        var cam = diorama.cam;
        float dist = Vector3.Distance(cam.transform.position, wp);
        float halfW = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * (16f / 9f);
        Vector3 right = cam.transform.right; right.y = 0f; right.Normalize();
        diorama.FocusOn(wp + right * (halfW * 0.30f), -1f);
        lockRaid = r; var gt = diorama.GoalTargetLocal; lockXZ = new Vector2(gt.x, gt.z);   // 刻度中央方块的「锁定」判定：镜头停在这个点才算对准了这个本（镜头目标被刻意偏向左边，不在据点正中）
        if (site != null) diorama.RiseNow(site);   // 镜头一切过去，这个据点立刻开始升起（不再等镜头够近）
    }

    /// <summary>战术感的坐标标注：RAID 编号 + 经纬度 + 栅格（由 mapPosition 推出，Tokyo 神田一带）。</summary>
    /// <summary>沙盘里 3D 标记用的带层次版本（TMP 富文本）：RAID 编号最大、颜色跟随选中状态（白→冷绿）；网格号小一号偏灰；经纬度更小、纯白。</summary>
    static string TacticalLabelRich(RaidDefinition r)
    {
        double lat = 35.6950 + (r.mapPosition.y - 0.5) * 0.07, lon = 139.7700 + (r.mapPosition.x - 0.5) * 0.14;
        string Dms(double v, char pos, char neg) { char c = v >= 0 ? pos : neg; v = System.Math.Abs(v); int d = (int)v; double m = (v - d) * 60; int mi = (int)m; int sec = (int)((m - mi) * 60); return d + "°" + mi.ToString("00") + "′" + sec.ToString("00") + "″" + c; }
        int gx = Mathf.Clamp((int)(r.mapPosition.x * 26f), 0, 25), gy = Mathf.Clamp((int)((1f - r.mapPosition.y) * 12f) + 1, 1, 12);
        return "<size=125%>RAID-" + (r.order > 0 ? r.order : 1).ToString("00") + "</size>   <size=74%><color=#C9D1D8>" + (char)('A' + gx) + gy.ToString("00") + "</color></size>\n"
             + "<size=80%><color=#FFFFFF>" + Dms(lat, 'N', 'S') + "   " + Dms(lon, 'E', 'W') + "</color></size>";
    }

    static string TacticalLabel(RaidDefinition r)
    {
        double lat = 35.6950 + (r.mapPosition.y - 0.5) * 0.07, lon = 139.7700 + (r.mapPosition.x - 0.5) * 0.14;
        string Dms(double v, char pos, char neg) { char c = v >= 0 ? pos : neg; v = System.Math.Abs(v); int d = (int)v; double m = (v - d) * 60; int mi = (int)m; int sec = (int)((m - mi) * 60); return d + "°" + mi.ToString("00") + "′" + sec.ToString("00") + "″" + c; }
        int gx = Mathf.Clamp((int)(r.mapPosition.x * 26f), 0, 25), gy = Mathf.Clamp((int)((1f - r.mapPosition.y) * 12f) + 1, 1, 12);
        return "RAID-" + (r.order > 0 ? r.order : 1).ToString("00") + "  ·  " + (char)('A' + gx) + gy.ToString("00") + "\n" + Dms(lat, 'N', 'S') + "  " + Dms(lon, 'E', 'W');
    }

    static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

    /// <summary>屏幕空间的径向暗角（盖在沙盘上、UI 之下）：中心透明，往边缘逐渐压到近黑。</summary>
    static void AddStageVignette(RectTransform root)
    {
        const int W = 192, H = 108;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float nx = (x + 0.5f) / W * 2f - 1f, ny = (y + 0.5f) / H * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx * 0.8f + ny * ny * 1.2f);                       // 略偏横向椭圆
                float a = Smooth(0.85f, 1.45f, r) * 0.45f;
                tex.SetPixel(x, y, new Color(0.01f, 0.012f, 0.02f, a));
            }
        tex.Apply();
        var go = new GameObject("StageVignette", typeof(RectTransform)); go.transform.SetParent(root, false);
        Stretch(go.GetComponent<RectTransform>());
        var ri = go.AddComponent<RawImage>(); ri.texture = tex; ri.raycastTarget = false;
    }

    // ── 悬停全息反馈 ───────────────────────────────────────────────────
    Transform SiteFor(RaidDefinition r)
    {
        if (r == null || diorama == null || !SiteByRaid.TryGetValue(r.raidId, out var nm)) return null;
        return diorama.transform.Find(nm);
    }

    static Bounds SiteBounds(Transform site)
    {
        Bounds b = default; bool first = true;
        foreach (var r in LitRenderers(site))
        {
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    void UpdateHologram()
    {
        hoverRaid = null;
        if (holoMat == null)
        {
            var sh = Resources.Load<Shader>("Shaders/DioramaHologram"); if (sh == null) return;
            holoMat = new Material(sh);
        }
        Transform target = null; bool linked = false;
        if (overNode && selected != null) { target = SiteFor(selected); linked = target != null; }
        else if (!overPanel && !NavHold() && !TipHold() && mapArea != null && diorama.cam != null)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(mapArea, Input.mousePosition, null, out var lp))
            {
                var rc = mapArea.rect;
                var nrm = new Vector2((lp.x - rc.xMin) / rc.width, (lp.y - rc.yMin) / rc.height);
                if (nrm.x > 0f && nrm.x < 1f && nrm.y > 0f && nrm.y < 1f)
                {
                    var ray = diorama.cam.ViewportPointToRay(new Vector3(nrm.x, nrm.y, 0f));
                    float best = float.MaxValue;
                    foreach (Transform ch in diorama.transform)
                    {
                        if ((!ch.name.StartsWith("Site_") && ch.name != "Tower") || !ch.gameObject.activeInHierarchy) continue;
                        float dist;
                        if (diorama.RiseProgress(ch) < 0.5f)
                        {   // 还没升起（缩放≈0）：整体包围盒外扩一圈，给个够大的命中体积
                            var bb = SiteBounds(ch); bb.Expand(2f);
                            if (!bb.IntersectRay(ray, out dist)) continue;
                        }
                        else
                        {   // 已经升起：逐个网格的包围盒各自判断，不用整个据点的大盒子——大盒子在建筑旁边的空地上方也会命中
                            dist = float.MaxValue;
                            foreach (var rr in LitRenderers(ch)) { var rb = rr.bounds; rb.Expand(0.6f); if (rb.IntersectRay(ray, out float rd) && rd < dist) dist = rd; }
                            if (dist == float.MaxValue) continue;
                        }
                        if (dist < best) { best = dist; target = ch; }
                    }
                    if (target == null)   // 兜底：射线没点到模型，就找屏幕上离鼠标最近（约 0.09 屏高以内）的 RAID 据点，不要求精确点到
                    {
                        float bd = 0f;   // 不再用「离鼠标最近的据点」兜底：只有真的点到建筑模型才算
                        foreach (var r in shown)
                        {
                            var st = SiteFor(r); if (st == null) continue;
                            var vp = diorama.cam.WorldToViewportPoint(st.position + Vector3.up * 2f);
                            if (vp.z <= 0f) continue;
                            float d = Vector2.Distance(new Vector2(vp.x * 16f / 9f, vp.y), new Vector2(nrm.x * 16f / 9f, nrm.y));
                            if (d < bd) { bd = d; target = st; }
                        }
                    }
                    if (target != null)
                        foreach (var r in shown) if (SiteFor(r) == target) { hoverRaid = r; if (!locked) { if (selected != r) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapScroll); Select(r); } linked = true; diorama.RiseNow(target); }   // 已经聚焦（locked，比如用 A/D 切过去）时，鼠标悬停不改选中项，只在点击时才切   // 指到就开始升起
                    if (!linked) target = null;   // 没开放的据点（没有对应 RAID）不出绿色全息
                }
            }
        }
        if (target == null && locked && selected != null) target = SiteFor(selected);
        if (DebugForceHover != null) target = DebugForceHover;   // 调试：不动鼠标也能验证全息效果
        overBuilding = linked && !overNode;
        if (target != null && target != litSite) { RestoreLit(); ApplyLit(target); }
        holoFade = Mathf.MoveTowards(holoFade, target != null ? 1f : 0f, Time.unscaledDeltaTime * 7f);
        if (target == null && holoFade <= 0.001f) RestoreLit();
        holoMat.SetFloat("_Fade", holoFade);
    }

    /// <summary>悬停时要盖全息层的所有网格：据点自己的（不含地面圆环），塔则连同外面转动的机械环一起。</summary>
    static List<MeshRenderer> LitRenderers(Transform site)
    {
        var list = new List<MeshRenderer>();
        foreach (var r in site.GetComponentsInChildren<MeshRenderer>()) if (r.name != "NodeRing") list.Add(r);
        if (site.name == "Tower")
        {
            var rings = site.parent != null ? site.parent.Find("TowerRings") : null;
            if (rings != null) list.AddRange(rings.GetComponentsInChildren<MeshRenderer>());
        }
        return list;
    }

    void ApplyLit(Transform site)
    {
        litSite = site;
        foreach (var r in LitRenderers(site))
        {
            var orig = r.sharedMaterials; origMats[r] = orig;
            var arr = new Material[orig.Length + 1]; System.Array.Copy(orig, arr, orig.Length); arr[orig.Length] = holoMat;
            r.sharedMaterials = arr;
        }
    }

    void RestoreLit()
    {
        foreach (var kv in origMats) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
        origMats.Clear(); litSite = null;
    }

    /// <summary>六边形线框 + 中心实心小六边形的标记贴图（白色，靠 Image.color 染色）。</summary>
    static Sprite HexMarkerSprite()
    {
        if (hexMarker != null) return hexMarker;
        const int N = 128; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float px = (x + 0.5f) / N * 2f - 1f, py = (y + 0.5f) / N * 2f - 1f;
                float qx = Mathf.Abs(px), qy = Mathf.Abs(py);
                float d = Mathf.Max(qx * 0.8660254f + qy * 0.5f, qy) / 0.92f;                       // 六边形距离，边缘 = 1
                float ring = 1f - Smooth(0f, 0.06f, Mathf.Abs(d - 0.9f) - 0.04f);          // 外框
                float core = 1f - Smooth(0f, 0.05f, d - 0.26f);                              // 中心点
                float glow = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.16f) * 0.22f;               // 外框的一点柔光
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Max(ring, core) + glow)));
            }
        tex.Apply();
        hexMarker = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return hexMarker;
    }

    static void AddHover(GameObject go, System.Action enter, System.Action exit)
    {
        var trig = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
        var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter }; e.callback.AddListener(_ => enter()); trig.triggers.Add(e);
        var x = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit }; x.callback.AddListener(_ => exit()); trig.triggers.Add(x);
    }

    static string VisitedKey(RaidDefinition r, bool night) { return "SkyPrison_RaidVisited_" + r.raidId + (night ? "_N" : "_D"); }
    static bool Visited(RaidDefinition r, bool night) { return PlayerPrefs.GetInt(VisitedKey(r, night), 0) == 1; }

    // 用键盘 / 手柄切 RAID 之后，鼠标没动就不再按"鼠标正指着哪个据点"去改选中项——
    // 镜头飞过去的途中，不同据点会从静止的鼠标底下划过，之前每划过一个就把选中项抢走（面板是学校、镜头却在别处）。
    // 物品详情卡弹出 / 刚收起时，鼠标往往正停在它下面的沙盘上：详情卡本身不挡射线，鼠标底下划过的据点（比如学校）
    // 会被当成悬停选中。详情卡开着、或刚收起的 0.7 秒内，不按鼠标位置改选中项。
    bool TipHold() { return (outDetail != null && outDetail.IsOpen) || Time.unscaledTime < tipHoldUntil; }

    RaidDefinition hoverRaid;   // 鼠标当前指着的 RAID（每帧重算；已聚焦时不直接改 selected，点击才生效）
    bool navHold; Vector2 navMousePos;
    bool NavHold()
    {
        if (navHold && ((Vector2)Input.mousePosition - navMousePos).sqrMagnitude > 25f) navHold = false;   // 鼠标动了（超过 5 像素）才恢复悬停选择
        return navHold;
    }

    void StepRaid(int step)
    {
        if (step == 0 || shown.Count == 0) return;
        navHold = true; navMousePos = Input.mousePosition;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapScroll);
        int cur = shown.IndexOf(selected); int next = cur < 0 ? (step > 0 ? 0 : shown.Count - 1) : (cur + step + shown.Count) % shown.Count;
        Select(shown[next]); FocusRaid(shown[next]); locked = true;
    }

    // ── 手柄：LB/RB 或十字键左右切 RAID，Y 切昼夜，A 选定/出发，B 关闭；左摇杆平移、右摇杆转视角、RT/LT 缩放 ──
    const float GpPanPixelsPerSec = 900f, GpOrbitPixelsPerSec = 320f, GpZoomPerSec = 1.4f, GpDead = 0.2f;
    static Vector2 Dead(Vector2 v) { return v.magnitude < GpDead ? Vector2.zero : v; }
    void GamepadUpdate()
    {
        var gp = UnityEngine.InputSystem.Gamepad.current;
        if (gp == null) return;
        if (gp.buttonEast.wasPressedThisFrame) { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapClose); Close(); return; }
        if (diorama != null && shown.Count > 0)
        {
            int step = (gp.leftShoulder.wasPressedThisFrame || gp.dpad.left.wasPressedThisFrame) ? -1
                     : (gp.rightShoulder.wasPressedThisFrame || gp.dpad.right.wasPressedThisFrame) ? 1 : 0;
            StepRaid(step);
        }
        if (gp.buttonNorth.wasPressedThisFrame && panelOpen > 0.5f && selected != null && selected.IsNightUnlocked) SetNight(!nightSel);
        if (gp.buttonSouth.wasPressedThisFrame)
        {
            if (selected == null && shown.Count > 0) StepRaid(1);
            else if (selected != null && panelOpen > 0.5f && goBtn != null && goBtn.interactable) { /* 出发改成长按 A：见 HoldUpdate */ }
            else if (selected != null) { FocusRaid(selected); locked = true; }
        }
        gpHold = gp.buttonSouth.isPressed && goBtn != null && goBtn.interactable && panelOpen > 0.5f && selected != null;
        if (diorama == null) return;
        float dt = Time.unscaledDeltaTime;
        var ls = Dead(gp.leftStick.ReadValue()); var rs = Dead(gp.rightStick.ReadValue());
        if (ls != Vector2.zero) diorama.PanByPixels(-ls * GpPanPixelsPerSec * dt);   // 推哪边就往哪边看（和"拖拽"方向相反）
        if (rs != Vector2.zero) diorama.Orbit(rs * GpOrbitPixelsPerSec * dt);
        float z = gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue();
        if (Mathf.Abs(z) > 0.05f) diorama.ZoomBy(Mathf.Exp(-z * GpZoomPerSec * dt));
    }

    void Select(RaidDefinition r)
    {
        selected = r;
        nightSel = r != null && r.hasNight && r.IsNightUnlocked && PlayerPrefs.GetInt("SkyPrison_RaidNight_" + r.raidId, 0) == 1;
        UpdateInfoTexts();
    }

    // ── 右侧竖向刻度（罗盘条的竖版）：贴窗口最右边的一条密集刻度尺，长短不一；尺子随选中的 RAID 上下滚动，
    //    正中央一个冷绿数字框读出当前刻度值（RAID 在地图上的纬向位置 0~100），其他已开放的 RAID 是尺上的蓝色小点 ──
    const int RulerHalf = 60; const float RulerPPU = 12f;   // 每格 12 像素（参考分辨率），两个方向各 60 格，足够铺出屏幕上下沿之外
    static readonly Color RulerGreen = new Color(0.35f, 1f, 0.7f, 1f), RulerBlue = new Color(0.45f, 0.65f, 1f, 1f);
    RectTransform rulerBox; RectTransform[] rulerTickRt; Image[] rulerTick; float rulerCur = -1f;

    const float RulerBoxSize = 20f;
    RaidDefinition lockRaid; Vector2 lockXZ;
    readonly Dictionary<string, Vector3> rulerCenters = new Dictionary<string, Vector3>();
    bool rulerInit; Image[] rulerFrame; readonly Dictionary<string, float> rulerVals = new Dictionary<string, float>();

    // 刻度值 = 沙盘本地 Z 换算到 0~100（和 RAID 的 mapPosition.y 同一个尺度：z = -45 + y*90）
    static float ZToRuler(float localZ) { return (localZ + 45f) / 90f * 100f; }
    /// <summary>某个 RAID 在刻度上的位置：优先用沙盘上据点的实际位置，没有据点就退回 mapPosition 推算。只算一次。</summary>
    float RulerValue(RaidDefinition r)
    {
        if (rulerVals.TryGetValue(r.raidId, out var v)) return v;
        var site = SiteFor(r);
        v = site != null && diorama != null ? ZToRuler(diorama.transform.InverseTransformPoint(SiteBounds(site).center).z) : r.mapPosition.y * 100f;
        rulerVals[r.raidId] = v; return v;
    }
    static float Hash01(int v) { float s = Mathf.Sin(v * 12.9898f + 78.233f) * 43758.5453f; return s - Mathf.Floor(s); }

    void BuildRuler(Transform parent)
    {
        var box = Rect("Ruler", parent); Anchor(box, 0.979f, -0.1f, 0.997f, 1.1f); rulerBox = box;   // 上下都比屏幕多出一截：刻度直接伸出屏幕外，不在中间收尾
        int n = RulerHalf * 2 + 1;
        rulerTick = new Image[n]; rulerTickRt = new RectTransform[n];
        for (int i = 0; i < n; i++)
        {
            var t = Rect("T" + i, box, new Color(1f, 1f, 1f, 0.3f)); t.GetComponent<Image>().raycastTarget = false; rulerTick[i] = t.GetComponent<Image>(); rulerTickRt[i] = t;
            t.anchorMin = t.anchorMax = new Vector2(1f, 0.5f); t.pivot = new Vector2(1f, 0.5f);
        }
        // 正中的当前标记：镂空的小方块，框线 2 像素；框里没有底色，框内的刻度在更新里直接不画，所以是真镂空
        var rb = Rect("Readout", box); rb.anchorMin = rb.anchorMax = new Vector2(1f, 0.5f); rb.pivot = new Vector2(1f, 0.5f); rb.anchoredPosition = Vector2.zero; rb.sizeDelta = new Vector2(RulerBoxSize, RulerBoxSize);
        rulerFrame = new[] {
            FrameLine(rb, "T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -2), new Vector2(0, 0)),
            FrameLine(rb, "B", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 2)),
            FrameLine(rb, "L", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(2, 0)),
            FrameLine(rb, "R", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-2, 0), new Vector2(0, 0)) };
    }

    float rulerLastCur = float.NaN; bool rulerLastLocked; int[] rulerSlotVal, rulerStamp; float[] rulerBaseA; bool[] rulerGlowed; int rulerFrameNo;

    void RulerUpdate()
    {
        if (rulerBox == null || rulerTick == null || rulerFrame == null || diorama == null) return;
        // 刻度跟着沙盘镜头走：拖动平移、A/D 飞到某个 RAID 时都是连续滚动（镜头自己已经做了平滑，这里只轻轻跟一下去抖）
        float camVal = ZToRuler(diorama.TargetLocal.z);
        rulerCur = !rulerInit ? camVal : Mathf.Lerp(rulerCur, camVal, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 30f)); rulerInit = true;
        // 锁定：选中了某个 RAID，并且镜头已经停在飞向它的那个点附近，中央方块才变冷绿；巡视中是灰色
        var ct = diorama.TargetLocal;
        bool locked = selected != null && selected == lockRaid && (new Vector2(ct.x, ct.z) - lockXZ).magnitude < 1.2f;   // 拖开一点就解除锁定
        // 对线：任何一个已开放 RAID 的据点滚到和中央方块同一条水平线上（屏幕纵向位置对齐），方块和附近刻度也变冷绿——不用选中，拖动沙盘经过时就亮
        if (!locked && diorama.cam != null)
            foreach (var rr in shown)
            {
                if (rr == null) continue;
                if (!rulerCenters.TryGetValue(rr.raidId, out var lc))
                {
                    var st = SiteFor(rr); if (st == null) continue;
                    lc = diorama.transform.InverseTransformPoint(SiteBounds(st).center); rulerCenters[rr.raidId] = lc;
                }
                var vp = diorama.cam.WorldToViewportPoint(diorama.transform.TransformPoint(lc));
                if (vp.z > 0f && Mathf.Abs(vp.y - 0.5f) < 14f / 1080f) { locked = true; break; }
            }
        // 性能：刻度没动、锁定状态也没变就整个跳过——静止时不碰任何一根线，UI 不会被反复标脏重建
        if (!float.IsNaN(rulerLastCur) && Mathf.Abs(rulerCur - rulerLastCur) < 0.002f && locked == rulerLastLocked) return;
        if (locked != rulerLastLocked || float.IsNaN(rulerLastCur))
        {
            Color frameCol = locked ? new Color(RulerGreen.r, RulerGreen.g, RulerGreen.b, 0.9f) : new Color(0.6f, 0.64f, 0.68f, 0.7f);
            foreach (var fi in rulerFrame) fi.color = frameCol;
        }
        rulerLastCur = rulerCur; rulerLastLocked = locked;
        // 只画到屏幕上下沿稍外一点（不是整个多出来的容器），少画一二十根看不见的线
        float halfH = ((RectTransform)rulerBox.parent).rect.height * 0.5f + 24f; if (halfH < 100f) halfH = 560f;
        int n = rulerTick.Length;
        if (rulerSlotVal == null) { rulerSlotVal = new int[n]; rulerStamp = new int[n]; rulerBaseA = new float[n]; rulerGlowed = new bool[n]; for (int i = 0; i < n; i++) rulerSlotVal[i] = int.MinValue; }
        rulerFrameNo++;
        int baseV = Mathf.FloorToInt(rulerCur);
        for (int v = baseV - RulerHalf; v <= baseV + RulerHalf; v++)
        {
            float y = (v - rulerCur) * RulerPPU;
            if (Mathf.Abs(y) > halfH || Mathf.Abs(y) <= RulerBoxSize * 0.5f + 1f) continue;   // 超出屏幕沿 / 落在中央镂空方块里的不画
            int s = ((v % n) + n) % n;   // 每个刻度值固定占用同一根线：长度、粗细、底色只在它换了新的值时设一次
            var img = rulerTick[s]; rulerStamp[s] = rulerFrameNo;
            if (rulerSlotVal[s] != v)
            {
                bool ten = v % 10 == 0, five = !ten && v % 5 == 0;
                float h = Hash01(v);
                float len = ten ? 11f : five ? 9f : 7f + Mathf.Floor(h * 2f);   // 长短差别不大：长线只比短线略长一点、略粗一点
                rulerTickRt[s].sizeDelta = new Vector2(len, ten ? 2f : 1f);
                rulerBaseA[s] = ten ? 0.5f : five ? 0.38f : 0.28f + h * 0.08f;
                img.color = new Color(1f, 1f, 1f, rulerBaseA[s]);
                rulerSlotVal[s] = v;
            }
            rulerTickRt[s].anchoredPosition = new Vector2(-1f, y);
            if (!img.enabled) img.enabled = true;
            if (Mathf.Abs(y) < 240f || rulerGlowed[s])   // 只有靠近中央的才有冷绿点亮；离开光晕范围后恢复一次底色
            {
                float a = rulerBaseA[s];
                float g = Mathf.Exp(-(y * y) / (2f * 60f * 60f));   // 当前标记附近点亮成冷绿
                img.color = Color.Lerp(new Color(1f, 1f, 1f, a), locked ? new Color(RulerGreen.r, RulerGreen.g, RulerGreen.b, Mathf.Min(1f, a + 0.4f)) : new Color(1f, 1f, 1f, Mathf.Min(1f, a + 0.2f)), g);
                rulerGlowed[s] = Mathf.Abs(y) < 240f;
            }
        }
        for (int i = 0; i < n; i++) if (rulerStamp[i] != rulerFrameNo && rulerTick[i].enabled) rulerTick[i].enabled = false;   // 这一帧没用到的线（滚出范围的）关掉，只在状态真变了才写
    }

    void Update()
    {
        HoldUpdate();
        RulerUpdate();
        if (impressionStatic != null) impressionStatic.SetFloat("_UnscaledTime", Time.unscaledTime);
        if (nightTg != null && nightTg.newBadge != null && nightTg.newBadge.gameObject.activeSelf)   // NEW 呼吸闪，参数和背包一致
        { var c = NewBadgeColor; c.a = Mathf.Lerp(0.35f, 1f, (Mathf.Sin(Time.unscaledTime * 3.2f) + 1f) * 0.5f); nightTg.newBadge.color = c; }
        if (!departing && Input.GetKeyDown(KeyCode.Escape)) { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapClose); Close(); }
        if (!departing && panelOpen > 0.5f && selected != null && selected.IsNightUnlocked && Input.GetKeyDown(KeyCode.Tab)) SetNight(!nightSel);
        if (diorama != null && !departing && shown.Count > 0)
        {
            int step = (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.LeftArrow)) ? -1
                     : (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.RightArrow)) ? 1 : 0;
            StepRaid(step);
        }
        if (!departing) GamepadUpdate();
        if (blurMat != null && diorama != null && diorama.cam != null && markers.Count > 0)
        {   // 标记所在的屏幕区域（六边形 + 引线 + 文字）不做边缘模糊
            // 用标记各部件（六边形 / 引线 / 水平线 / 文字）实际包围盒投影出的屏幕矩形，文字和长线都不会被模糊
            Vector2 lo = new Vector2(9f, 9f), hi = new Vector2(-9f, -9f); bool ok = true;
            int mIdx = Mathf.Max(0, markerRaids.IndexOf(selected));   // 选中 / 悬停的那个标记不做边缘模糊
            foreach (var rr in markers[mIdx].GetComponentsInChildren<Renderer>())
            {
                var b = rr.bounds; var c = b.center; var e = b.extents;
                for (int i = 0; i < 8; i++)
                {
                    var v = diorama.cam.WorldToViewportPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                    if (v.z <= 0f) { ok = false; continue; }
                    lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v);
                }
            }
            blurMat.SetVector("_MarkerRect", new Vector4(lo.x - 0.01f, lo.y - 0.015f, hi.x + 0.01f, hi.y + 0.015f)); blurMat.SetFloat("_MarkerOn", ok && hi.x > lo.x ? 1f : 0f);
        }
        if (markerCam != null && diorama != null && diorama.cam != null) { markerCam.fieldOfView = diorama.cam.fieldOfView; markerCam.nearClipPlane = diorama.cam.nearClipPlane; markerCam.farClipPlane = diorama.cam.farClipPlane; markerCam.aspect = 16f / 9f; }
        if (diorama != null && markers.Count > 0)
        {
            RaidDefinition hit = null;
            if (false && !overPanel && !NavHold() && TryMapRay(out var mray) && Physics.Raycast(mray, out var rh, 800f))   // 标记的线和字不再响应鼠标，只有建筑模型可以点
            {
                var mk = rh.collider.GetComponentInParent<SkyPrisonRaidDioramaMarker>();
                if (mk != null) { int mi = markers.IndexOf(mk); if (mi >= 0) hit = markerRaids[mi]; }
            }
            if (hit != null) { if (hit != hoverMarkerRaid) Select(hit); overNode = true; }
            else if (hoverMarkerRaid != null) { overNode = false; hoverGrace = 0.35f; }
            hoverMarkerRaid = hit;
        }
        if (diorama != null) UpdateHologram();
        if (panelGroup != null)
        {
            if (!overNode && !overPanel && hoverGrace > 0f) hoverGrace -= Time.unscaledDeltaTime;
            bool want = locked || (overPanel && panelGroup.alpha > 0.5f);   // 只有点击聚焦到 RAID 模型 / 标记后才显示详情，悬停不弹
            panelOpen = Mathf.MoveTowards(panelOpen, want ? 1f : 0f, Time.unscaledDeltaTime / (want ? 0.45f : 0.3f));
            bool wantTabHint = panelOpen > 0.5f && selected != null && selected.IsNightUnlocked && !departing;
            if (wantTabHint != tabHintShown)   // 用提示条自带的"上下文提示"：窗口出现才多一项，窗口收起就还原
            {
                tabHintShown = wantTabHint;
                if (wantTabHint) SkyPrisonWindowHintBar.PushContext(hintsNight); else SkyPrisonWindowHintBar.PopContext();
            }
            float e = 1f - Mathf.Pow(1f - panelOpen, 3f);                         // 先快后缓
            panelGroup.alpha = Mathf.Clamp01(panelOpen * 3f);                      // 内容随展开淡入
            panelGroup.blocksRaycasts = panelOpen > 0.5f;
            float full = ((RectTransform)detailMask.parent).rect.width * 0.30f;   // 内页的最终宽度（固定，不随展开缩放）
            detailMask.anchorMin = new Vector2(1f - PanelFull * e, 0f);
            detailPanel.sizeDelta = new Vector2(full, 0f);
        }
        if (diorama != null)
        {
            for (int i = 0; i < dioramaNodes.Count; i++)
            {
                var node = dioramaNodes[i].Key; const float pulse = 1f;   // 标记和文字不再缩放 / 呼吸
                Vector3 wp = dioramaNodes[i].Value;
                var site = SiteFor(dioramaRaids[i]);
                if (site != null) { var b = SiteBounds(site); wp = diorama.transform.InverseTransformPoint(new Vector3(b.center.x, b.max.y + 1.6f, b.center.z)); }
                var vpTop = diorama.cam.WorldToViewportPoint(diorama.ToWorld(wp));
                node.anchorMin = node.anchorMax = new Vector2(vpTop.x, vpTop.y); node.anchoredPosition = Vector2.zero; node.localScale = Vector3.one * pulse;
                var g0 = wp; var vpGround = diorama.cam.WorldToViewportPoint(diorama.ToWorld(new Vector3(g0.x, 0.5f, g0.z)));
                float h = Mathf.Max(0f, (vpTop.y - vpGround.y) * mapArea.rect.height - 34f);
                nodeLeaders[i].sizeDelta = new Vector2(2f, h / pulse);
            }
            int hot = (overNode || overBuilding || locked) ? dioramaRaids.IndexOf(selected) : -1;
            for (int i = 0; i < nodeLabels.Count; i++)
                nodeLabels[i].color = Color.Lerp(nodeLabels[i].color, i == hot ? new Color(0.35f, 1f, 0.7f, 1f) : Color.white, Time.unscaledDeltaTime * 14f);
            // 点在据点模型上（按下与抬起位置基本一致，不是拖动）→ 镜头移到该 RAID 中央
            if (Input.GetMouseButtonDown(0)) { downPos = Input.mousePosition; downOnTarget = overBuilding || overNode; if (!overNode && !overBuilding && !overPanel && !PointerInPanel()) locked = false; }   // 点在详情窗口范围内（含空白处）不取消聚焦
            if (Input.GetMouseButtonUp(0) && downOnTarget && (overBuilding || overNode) && (hoverRaid ?? selected) != null && ((Vector2)Input.mousePosition - downPos).sqrMagnitude < 36f) { var cr = hoverRaid ?? selected; if (cr != selected) Select(cr); FocusRaid(cr); locked = true; SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapSelect); }
            { int hm = (overNode || overBuilding || locked) ? markerRaids.IndexOf(selected) : -1;
              float am = Mathf.Clamp01((diorama.Clock - NodesAppearAt) / 0.8f);
              for (int i = 0; i < markers.Count; i++) { var st = SiteFor(markerRaids[i]); float rp = st != null ? Mathf.Clamp01((diorama.RiseProgress(st) - 0.6f) / 0.4f) : 1f; markers[i].SetHot(i == hm); markers[i].SetAlpha(am * rp);
                if (i == hm && markerRaids[i] != null) { var seenKey = "SkyPrison_RaidSeen_" + markerRaids[i].raidId; if (PlayerPrefs.GetInt(seenKey, 0) == 0) { PlayerPrefs.SetInt(seenKey, 1); markers[i].SetNew(false); } } } }
            float a = Mathf.Clamp01((diorama.Clock - NodesAppearAt) / 0.8f);
            foreach (var cg in nodeFades) { cg.alpha = a; cg.blocksRaycasts = a > 0.99f; }
        }
    }

    void Depart()
    {
        if (selected == null || departing) return;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapDepart);
        departing = true;
        PlayerPrefs.SetInt(VisitedKey(selected, nightSel && selected.IsNightUnlocked), 1); PlayerPrefs.Save();   // 记下去过：之后影像图才会公开
        StartCoroutine(DepartRoutine(selected));
    }

    IEnumerator DepartRoutine(RaidDefinition r)
    {
        if (r.requiresSubway)
        {
            subwayFade.blocksRaycasts = true;
            subwayText.text = L("raid_subway_to", "地铁 · 前往 ") + r.Name;
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 0.6f) { subwayFade.alpha = t; yield return null; }
            subwayFade.alpha = 1;
            yield return new WaitForSecondsRealtime(1.8f);   // 占位：以后换成地铁演出
        }
        else yield return SkyPrisonDepartWarp.Play();   // 不乘地铁：四周凹陷吸入中心再黑屏；乘地铁已经是黑屏了，不再叠
        string scene = r.targetScene; Vector3 pos = r.arrivalPosition;
        SkyPrisonRaidRunContext.Set(r.raidId, nightSel && r.IsNightUnlocked, scene, r.order, r.CoordinateText());
        Close();
        // 进本要建章节会话（AFP、撤离结算都靠它），不能用本内切图的 SwitchMap。
        SceneLoader.EnterChapter(r.raidId, scene, pos);
    }
}
