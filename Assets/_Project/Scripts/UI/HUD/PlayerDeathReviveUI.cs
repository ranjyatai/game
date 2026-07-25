using System.Collections;
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家死亡决策弹窗。
/// - 展开/收起动画与背包窗口相同（横线展开 / 收缩成横线）
/// - 选中道具有冷绿色选框
/// - 左右切换带滑动轮播动画
/// - 底部提示条复用 SkyPrisonWindowHintBar
/// </summary>
[DisallowMultipleComponent]
public class PlayerDeathReviveUI : MonoBehaviour
{
    // ── 静态入口 ───────────────────────────────────────────────────────────────

    private static PlayerDeathReviveUI _current;

    public static void Show(PlayerDeathDecisionController controller,
                            InventoryItemEntry _, bool __)
    {
        Debug.Log("[PlayerDeathReviveUI] Show() called");
        if (_current != null) Destroy(_current.gameObject);

        // 强制关闭所有已打开的窗口
        try
        {
            var wm = UnityEngine.Object.FindObjectOfType<SkyPrisonWindowManager_V1>();
            if (wm != null)
            {
                var keys = new List<string>(wm.OpenedWindowKeys);
                foreach (var k in keys) wm.Close(k);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[PlayerDeathReviveUI] CloseAll failed: " + e.Message);
        }

        var pfab = Resources.Load<GameObject>("UI/Window/PF_PlayerDeathRevive");
        Debug.Log($"[PlayerDeathReviveUI] prefab load = {(pfab != null ? pfab.name : "NULL")}");

        GameObject root = pfab != null ? Instantiate(pfab) : BuildFallbackRoot();
        root.name = "PlayerDeathReviveUI";
        DontDestroyOnLoad(root);

        PlayerAimFacingController.Instance?.PushWindowCursor();
        Cursor.visible   = true;
        Cursor.lockState = CursorLockMode.None;

        _current = root.AddComponent<PlayerDeathReviveUI>();
        Debug.Log("[PlayerDeathReviveUI] component added, calling Init");
        try { _current.Init(controller); }
        catch (System.Exception e) { Debug.LogError("[PlayerDeathReviveUI] Init FAILED: " + e); }
    }

    public static void Hide()
    {
        if (_current == null) return;
        Cursor.visible   = false;
        Cursor.lockState = CursorLockMode.Locked;
        PlayerAimFacingController.Instance?.PopWindowCursor();
        _current.StartCoroutine(_current.CompressOutAndDestroy());
        _current = null;
    }


    // ── 内部状态 ───────────────────────────────────────────────────────────────

    private PlayerDeathDecisionController                _ctrl;
    private List<(InventoryItemEntry entry, bool onGCD)> _items;
    private int   _sel;
    private bool  _closing;
    private bool  _inputReady; // 防止弹窗出现瞬间吃到玩家之前按下的按键
    private float _tick;

    private CanvasGroup  _cg;
    private RectTransform _panelRT;   // 实际可视面板（WindowPanel）

    // 选择器节点
    private RectTransform  _cardViewport;
    private Image          _icon;
    private Image          _iconGCDOverlay;  // 叠在图标上的灰色遮罩，GCD 时显示
    private Image          _selFrame;
    private TextMeshProUGUI _itemName, _itemCount, _gcdText, _indexHint;
    private Button          _btnL, _btnR, _btnRevive;
    private CanvasGroup     _btnLCG, _btnRCG;
    private TextMeshProUGUI _btnReviveLbl;
    private CanvasGroup     _btnReviveCG;

    // 字体（从 prefab 根节点的 SkyPrisonUIGlobalStyleSettings_V1 读取）
    private TMP_FontAsset _tmpFont;

    // 濒死边缘渐变遮罩
    private CanvasGroup _vignetteGroup;
    private GameObject  _vignetteRoot;   // 独立 GO，不 parent 到弹窗下

    // 本地化字典表（运行时从 Resources 加载）
    private UILocalizationTable _locTable;

    // 标题文字 & 自适应外框
    private TextMeshProUGUI _titleTmp;
    private RectTransform   _titleFrameRT;

private static readonly Color ColdGreen  = new Color(0.42f, 0.92f, 0.68f, 0.85f);
    private static readonly Color ArrowGray  = new Color(0.35f, 0.35f, 0.37f, 1f);
    private static readonly Color ArrowWhite = new Color(0.72f, 0.72f, 0.76f, 1f);
    private const float SlideWidth = 240f;

    // 操作提示条（键鼠 + 手柄各自独立一套，由 HintBar 根据设备选择显示；不混用 Action 类型以避免手柄模式过滤）
    private static readonly SkyPrisonWindowHint[] _hints = new[]
    {
        SkyPrisonWindowHint.Icon("keyboard/a",            "A / D", "切换道具"),
        SkyPrisonWindowHint.GamepadIcon("gamepad/left",   "←",    "切换道具"),
        SkyPrisonWindowHint.GamepadIcon("gamepad/right",  "→",    "切换道具"),
        SkyPrisonWindowHint.Icon("keyboard/e",            "E",     "使用道具"),
        SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/a", "A",    "使用道具"),
        SkyPrisonWindowHint.Icon("keyboard/esc",          "Esc",  "返回据点"),
        SkyPrisonWindowHint.GamepadIcon("gamepad/xbox/b", "B",    "返回据点"),
    };

    // ── 初始化 ────────────────────────────────────────────────────────────────

    private void Init(PlayerDeathDecisionController ctrl)
    {
        _ctrl  = ctrl;
        _items = ctrl.FindAllReviveItems();
        _sel   = 0;
        _locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        for (int i = 0; i < _items.Count; i++)
            if (!_items[i].onGCD) { _sel = i; break; }

        _cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        _cg.alpha = 1f; // 立即可见；scale 动画做视觉效果
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.NearDeath);

        var cv = GetComponent<Canvas>();
        if (cv != null) cv.sortingOrder = 1200;

        // 优先读 prefab 根节点的全局样式字体
        var style = GetComponent<SkyPrisonUIGlobalStyleSettings_V1>();
        _tmpFont = style != null ? style.defaultTextFont : null;

        // 兜底：从场景中已加载的 TMP 组件找支持中文的字体
        if (_tmpFont == null)
        {
            foreach (var t in FindObjectsOfType<TMPro.TextMeshProUGUI>())
            {
                if (t.font != null && t.font.name.Contains("ZhouFang"))
                { _tmpFont = t.font; break; }
            }
        }
        // 最终兜底：TMP 全局默认（可能无中文，但至少不会崩）
        if (_tmpFont == null) _tmpFont = TMPro.TMP_Settings.defaultFontAsset;

        var panelTr = transform.Find("WindowPanel");
        _panelRT = panelTr != null ? panelTr.GetComponent<RectTransform>() : null;

        // prefab 没有 WindowPanel（Resources 未同步）时，动态创建面板
        if (_panelRT == null)
        {
            Debug.Log("[PlayerDeathReviveUI] WindowPanel not found, building fallback panel");
            _panelRT = BuildPanelFallback();
        }
        else
        {
            Debug.Log("[PlayerDeathReviveUI] WindowPanel found in prefab");
        }

        PopulatePanel(_panelRT);

        RefreshSel();
        BuildVignette();
        StartCoroutine(ExpandIn());
        StartCoroutine(EnableInputAfterFrames(2));
        ResolveHintBar()?.Show(_hints);

        // 注册到全局提示条
        var hintBar = FindObjectOfType<SkyPrisonWindowHintBar>();
        hintBar?.Show(_hints);
    }

    private void OnEnable()
    {
        LocalizationRuntime.OnLanguageChanged += OnLanguageChanged;
    }

    private void OnDisable()
    {
        LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(string _)
    {
        if (_titleTmp != null)
        {
            _titleTmp.text = L("ui_death_title", "生命维持警告");
            StartCoroutine(FitTitleFrame());
        }
    }

    private void OnDestroy()
    {
        LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
        if (_vignetteRoot != null) Destroy(_vignetteRoot);
        var hintBar = FindObjectOfType<SkyPrisonWindowHintBar>();
        hintBar?.Clear();
    }

    // ── 展开动画（从中心横线展开，与背包相反） ────────────────────────────────

    private IEnumerator EnableInputAfterFrames(int frames)
    {
        for (int i = 0; i < frames; i++) yield return null;
        _inputReady = true;
    }

    private IEnumerator ExpandIn()
    {
        RectTransform rt = _panelRT != null ? _panelRT : GetComponent<RectTransform>();
        SetPivotKeepPosition(rt, new Vector2(0.5f, 0.5f));
        Vector3 baseScale = rt.localScale;

        float xDur = 0.12f;
        float yDur = 0.20f;

        // 先从点横向展开成一条线
        rt.localScale = new Vector3(0f, 0f, baseScale.z);
        float t = 0f;
        while (t < xDur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / xDur);
            k = k * k * (3f - 2f * k); // smoothstep
            rt.localScale = new Vector3(baseScale.x * k, 0f, baseScale.z);
            yield return null;
        }

        // 再从横线展开成完整面板
        t = 0f;
        while (t < yDur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / yDur);
            k = k * k * (3f - 2f * k);
            rt.localScale = new Vector3(baseScale.x, baseScale.y * k, baseScale.z);
            yield return null;
        }
        rt.localScale = baseScale;
    }

    // ── 收起动画（压缩成横线，与背包一致） ───────────────────────────────────


    private IEnumerator CompressOutAndDestroy()
    {
        _closing = true;
        RectTransform rt = _panelRT != null ? _panelRT : GetComponent<RectTransform>();
        SetPivotKeepPosition(rt, new Vector2(0.5f, 0.5f));

        // 冻结 UV tracker 避免缩放时模糊图抖
        var uvt = rt != null ? rt.GetComponent<SkyPrisonBlurUVTracker>() : null;
        if (uvt != null) uvt.Frozen = true;

        Vector3 baseScale = rt.localScale;
        float yDur = 0.18f;
        float xDur = 0.10f;

        float ti = 0f;
        while (ti < yDur)
        {
            ti += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(ti / yDur);
            k = k * k;
            if (rt) rt.localScale = new Vector3(baseScale.x, baseScale.y * k, baseScale.z);
            yield return null;
        }

        ti = 0f;
        while (ti < xDur)
        {
            ti += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(ti / xDur);
            if (rt) rt.localScale = new Vector3(baseScale.x * k, 0f, baseScale.z);
            yield return null;
        }

        // 无论复活还是放弃死亡，强制通知 glitch 效果淡出（OnRespawned 在放弃路径下不会触发）
        if (_ctrl != null)
        {
            var glitch = _ctrl.GetComponent<PlayerDeathGlitchEffect>();
            if (glitch != null) glitch.FadeOut();
        }

        // 渐变遮罩淡出后销毁独立 GO
        if (_vignetteGroup != null)
        {
            float vt = 0f, vDur = 0.3f;
            while (vt < vDur)
            {
                vt += Time.unscaledDeltaTime;
                _vignetteGroup.alpha = 1f - Mathf.Clamp01(vt / vDur);
                yield return null;
            }
        }
        if (_vignetteRoot != null) Destroy(_vignetteRoot);
        ResolveHintBar()?.Show(null);
        Destroy(gameObject);
    }

    // ── 动态填充面板 ──────────────────────────────────────────────────────────

    private void PopulatePanel(RectTransform panel)
    {
        // ── 找 prefab 中已烘焙的节点，只更新数据／事件 ──

        // 标题：prefab 已有 Title TMP，只需确保字体
        var titleTmp = panel.Find("TitleArea/Title")?.GetComponent<TextMeshProUGUI>();
        if (titleTmp != null)
        {
            if (_tmpFont != null) titleTmp.font = _tmpFont;
            titleTmp.text = L("ui_death_title", "生命维持警告");
            _titleTmp = titleTmp;

            // 在 TitleArea 下创建自适应外框（如已存在则复用）
            var titleArea = titleTmp.transform.parent;
            if (titleArea != null)
            {
                var existingFrame = titleArea.Find("TitleFrame");
                if (existingFrame == null)
                {
                    var frameGO = new GameObject("TitleFrame");
                    _titleFrameRT = frameGO.AddComponent<RectTransform>();
                    _titleFrameRT.SetParent(titleArea, false);
                    _titleFrameRT.SetSiblingIndex(0); // 在文字下方渲染
                    _titleFrameRT.anchorMin = _titleFrameRT.anchorMax = new Vector2(0.5f, 0.5f);
                    _titleFrameRT.pivot     = new Vector2(0.5f, 0.5f);
                    var img = frameGO.AddComponent<Image>();
                    img.color = Color.clear;
                    img.raycastTarget = false;
                    AddOutline(_titleFrameRT, new Color(0.75f, 0.42f, 0.42f, 0.85f), 3.5f);
                }
                else
                {
                    _titleFrameRT = existingFrame.GetComponent<RectTransform>();
                }
                StartCoroutine(FitTitleFrame());
            }
        }

        // 道具选择器节点
        var selArea = panel.Find("ItemSelectorArea");
        if (selArea != null)
        {
            // 箭头按钮
            var arrowL = selArea.Find("ArrowL");
            var arrowR = selArea.Find("ArrowR");
            if (arrowL != null)
            {
                _btnL  = arrowL.GetComponent<Button>(); _btnL?.onClick.AddListener(OnPrev);
                _btnLCG = arrowL.GetComponent<CanvasGroup>(); if (_btnLCG == null) _btnLCG = arrowL.gameObject.AddComponent<CanvasGroup>();
            }
            if (arrowR != null)
            {
                _btnR  = arrowR.GetComponent<Button>(); _btnR?.onClick.AddListener(OnNext);
                _btnRCG = arrowR.GetComponent<CanvasGroup>(); if (_btnRCG == null) _btnRCG = arrowR.gameObject.AddComponent<CanvasGroup>();
            }

            // 轮播容器
            var cc = selArea.Find("Viewport/CardContainer");
            _cardViewport = cc != null ? cc.GetComponent<RectTransform>() : null;

            // 卡片内各节点
            var card = cc != null ? cc.Find("Card") : null;
            if (card != null)
            {
                var iconGO = card.Find("Icon");
                if (iconGO != null)
                {
                    _icon = iconGO.GetComponent<Image>();
                    if (_icon != null)
                    {
                        _icon.preserveAspect = true;
                        _icon.raycastTarget  = false;
                        _icon.color          = Color.white; // prefab 可能序列化为 clear，强制还原
                    }

                    // GCD 灰色遮罩层（叠在图标上，不动 _icon.material 避免 sprite UV 丢失）
                    var overlayGO = new GameObject("GCDOverlay");
                    var overlayRT = overlayGO.AddComponent<RectTransform>();
                    overlayRT.SetParent(iconGO, false);
                    Anchor(overlayRT, 0f, 0f, 1f, 1f);
                    _iconGCDOverlay = overlayGO.AddComponent<Image>();
                    _iconGCDOverlay.color = new Color(0.08f, 0.08f, 0.08f, 0.72f);
                    _iconGCDOverlay.raycastTarget = false;
                    _iconGCDOverlay.gameObject.SetActive(false);

                    // 冷绿选框的子线条颜色在 prefab 里已设置，只需保留引用
                    var sfGO = iconGO.Find("SelFrame");
                    if (sfGO != null) _selFrame = sfGO.GetComponent<Image>();
                }

                _itemName  = card.Find("Name")?.GetComponent<TextMeshProUGUI>();
                // Count 在 Icon 子节点内（叠在图标右下角）
                _itemCount = card.Find("Icon/Count")?.GetComponent<TextMeshProUGUI>()
                          ?? card.Find("Count")?.GetComponent<TextMeshProUGUI>();
                _gcdText   = card.Find("GCD")?.GetComponent<TextMeshProUGUI>();

                // 确保字体
                ApplyFont(_itemName); ApplyFont(_itemCount); ApplyFont(_gcdText);
                if (_gcdText != null) _gcdText.gameObject.SetActive(false);
            }

            if (_items.Count == 0)
                BuildNoItem(selArea.GetComponent<RectTransform>());
        }

        // 警告红字：prefab 已有，只需确保字体
        var warnTmp = panel.Find("Warning")?.GetComponent<TextMeshProUGUI>();
        if (warnTmp != null)
        {
            if (_tmpFont != null) warnTmp.font = _tmpFont;
            warnTmp.text = L("ui_death_warning", "死亡将会造成背包所持物品的损失");
        }

        // 按钮事件
        var btnReviveGO = panel.Find("BtnRevive");
        var btnDieGO    = panel.Find("BtnDie");

        if (btnReviveGO != null)
        {
            _btnRevive    = btnReviveGO.GetComponent<Button>();
            _btnReviveLbl = btnReviveGO.GetComponentInChildren<TextMeshProUGUI>();
            _btnReviveCG  = btnReviveGO.GetComponent<CanvasGroup>();
            if (_btnReviveCG == null) _btnReviveCG = btnReviveGO.gameObject.AddComponent<CanvasGroup>();
            _btnReviveCG.interactable   = true;
            _btnReviveCG.blocksRaycasts = true;
            if (_btnRevive != null) _btnRevive.onClick.AddListener(OnRevive);
            if (_btnReviveLbl != null) _btnReviveLbl.text = L("ui_death_use_item", "使用该道具");
            ApplyFont(_btnReviveLbl);
            if (_items.Count == 0 && _btnRevive != null)
                _btnRevive.gameObject.SetActive(false);
        }
        if (btnDieGO != null)
        {
            var bd    = btnDieGO.GetComponent<Button>();
            var bdLbl = btnDieGO.GetComponentInChildren<TextMeshProUGUI>();
            if (bd != null)    bd.onClick.AddListener(OnDie);
            if (bdLbl != null) bdLbl.text = L("ui_death_return_base", "返回据点");
            ApplyFont(bdLbl);
        }
    }

    private void ApplyFont(TextMeshProUGUI tmp)
    {
        if (tmp != null && _tmpFont != null) tmp.font = _tmpFont;
    }

    // 冷绿选框：四条 1.5px Image 贴在图标外圈
    private static Image AddSelFrame(RectTransform frame)
    {
        var rt = MakeRect("SelFrame", frame);
        // 比 Frame 略大（各方向超出 3px）
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.sizeDelta        = Vector2.zero; // 与图标等大，不外扩
        rt.anchoredPosition = Vector2.zero;
        rt.SetAsLastSibling();

        // 四边等宽 1.5px 正方形边框
        const float b = 1.5f;
        AddOutlineLine(rt, "SF_T", new Vector2(0f,1f), new Vector2(1f,1f), new Vector2(.5f,1f), new Vector2(0f,b));
        AddOutlineLine(rt, "SF_B", new Vector2(0f,0f), new Vector2(1f,0f), new Vector2(.5f,0f), new Vector2(0f,b));
        AddOutlineLine(rt, "SF_L", new Vector2(0f,0f), new Vector2(0f,1f), new Vector2(0f,.5f), new Vector2(b,0f));
        AddOutlineLine(rt, "SF_R", new Vector2(1f,0f), new Vector2(1f,1f), new Vector2(1f,.5f), new Vector2(b,0f));

        // 返回根 Image 作为可见性句柄（本身透明，子线条有颜色）
        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.clear; img.raycastTarget = false;
        return img;
    }

    private static void AddOutlineLine(RectTransform parent, string name,
        Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 sizeDelta)
    {
        var rt = MakeRect(name, parent);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = sizeDelta;
        rt.gameObject.AddComponent<Image>().color = ColdGreen;
    }

    private void BuildNoItem(RectTransform area)
    {
        var t = AddTMP("NoItem", area, 18, TextAlignmentOptions.Center, new Color(.46f,.46f,.50f,1f));
        t.text = "背包中没有复活道具";
        Anchor(t.rectTransform, 0f, 0f, 1f, 1f);
    }

    // ── 刷新选中道具 ──────────────────────────────────────────────────────────

    private void RefreshSel()
    {
        if (_items.Count == 0) return;
        var (e, gcd) = _items[_sel];
        var def = e.definition;

        if (_icon != null)
        {
            _icon.sprite  = def.icon;
            _icon.enabled = def.icon != null;
            _icon.color = Color.white;
            Debug.Log($"[DeathUI] sprite={(def.icon!=null?def.icon.name:"NULL")} enabled={_icon.enabled} go={_icon.gameObject.name} childCount={_icon.transform.childCount}");
            if (_iconGCDOverlay != null) _iconGCDOverlay.gameObject.SetActive(gcd);
        }
        if (_itemName != null)
        {
            string hex  = ItemQualityHex(def.itemLevel);
            string name = string.IsNullOrEmpty(def.displayName) ? def.itemKey : def.displayName;
            _itemName.text = $"<color=#{hex}>{name}</color>";
        }
        if (_itemCount != null) _itemCount.text = $"<size=65%>×</size>{e.count}";
        if (_gcdText   != null) _gcdText.gameObject.SetActive(gcd);
        // 冷绿选框：GCD 时隐藏（已无法使用）；可用时显示
        if (_selFrame  != null) _selFrame.gameObject.SetActive(!gcd);

        if (_btnRevive != null)
        {
            // 保持 interactable=true 让点击事件能触发，OnRevive 内部判断 GCD 播 Forbidden
            _btnRevive.interactable = true;
            if (_btnReviveLbl != null)
                _btnReviveLbl.text = gcd
                    ? $"{L("ui_death_use_item","使用该道具")}（{L("ui_death_on_cooldown","冷却中")}）"
                    : L("ui_death_use_item", "使用该道具");
        }
        // GCD 时按钮整体变灰（CanvasGroup alpha 降低），可用时恢复
        if (_btnReviveCG != null)
            _btnReviveCG.alpha = gcd ? 0.38f : 1f;

        bool multi = _items.Count > 1;
        // 箭头始终显示：多物品时可交互（白色），单物品时灰色且禁用
        SetArrowState(_btnL, _btnLCG, multi);
        SetArrowState(_btnR, _btnRCG, multi);
        if (_indexHint != null) _indexHint.text = multi ? $"{_sel + 1} / {_items.Count}" : "";
    }

    // ── 轮播切换动画 ──────────────────────────────────────────────────────────

    private Coroutine _slideCoroutine;

    private void SlideTo(int newSel, bool toLeft)
    {
        if (_cardViewport == null) { _sel = newSel; RefreshSel(); return; }
        if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
        _slideCoroutine = StartCoroutine(SlideAnim(newSel, toLeft));
    }

    private IEnumerator SlideAnim(int newSel, bool slideLeft)
    {
        // 旧卡片滑出
        float sign   = slideLeft ? -1f : 1f;
        float dur    = 0.16f;
        float t      = 0f;
        Vector2 from = Vector2.zero;
        Vector2 to   = new Vector2(sign * SlideWidth, 0f);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            k = k * k; // ease-in
            if (_cardViewport) _cardViewport.anchoredPosition = Vector2.Lerp(from, to, k);
            yield return null;
        }

        // 更新数据
        _sel = newSel;
        RefreshSel();

        // 新卡片从另一侧滑入
        if (_cardViewport) _cardViewport.anchoredPosition = new Vector2(-sign * SlideWidth, 0f);
        t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            k = 1f - (1f - k) * (1f - k); // ease-out
            if (_cardViewport) _cardViewport.anchoredPosition = Vector2.Lerp(
                new Vector2(-sign * SlideWidth, 0f), Vector2.zero, k);
            yield return null;
        }
        if (_cardViewport) _cardViewport.anchoredPosition = Vector2.zero;
    }

    // ── 事件 ──────────────────────────────────────────────────────────────────

    // ── 濒死边缘渐变遮罩 ──────────────────────────────────────────────────────

    private void BuildVignette()
    {
        _vignetteRoot = new GameObject("DeathVignette");
        DontDestroyOnLoad(_vignetteRoot);
        var canvas = _vignetteRoot.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        // 低于 HUD Canvas（500），只覆盖游戏世界，不压任何 UI
        canvas.sortingOrder = 10;
        _vignetteRoot.AddComponent<UnityEngine.UI.CanvasScaler>();
        _vignetteGroup = _vignetteRoot.AddComponent<CanvasGroup>();
        _vignetteGroup.blocksRaycasts = false;
        _vignetteGroup.interactable   = false;
        _vignetteGroup.alpha          = 0f;

        var topRT  = MakeVignetteBar(_vignetteRoot.transform, top: true);
        var botRT  = MakeVignetteBar(_vignetteRoot.transform, top: false);
        var pulse  = MakePulseOverlay(_vignetteRoot.transform);

        StartCoroutine(ExpandVignette(topRT, botRT));
        StartCoroutine(PulseLoop(pulse));
    }

    // 从屏幕边缘向内蔓延展开
    private IEnumerator ExpandVignette(RectTransform topRT, RectTransform botRT)
    {
        float dur = 0.6f, t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / dur);
            _vignetteGroup.alpha = k;
            // 顶部：从 anchorMin.y=1（零高）向下展开到 0.72
            topRT.anchorMin = new Vector2(0f, Mathf.Lerp(1f, 0.72f, k));
            topRT.anchorMax = new Vector2(1f, 1f);
            // 底部：从 anchorMax.y=0（零高）向上展开到 0.28
            botRT.anchorMin = new Vector2(0f, 0f);
            botRT.anchorMax = new Vector2(1f, Mathf.Lerp(0f, 0.28f, k));
            yield return null;
        }
        _vignetteGroup.alpha = 1f;
    }

    private static Image MakePulseOverlay(Transform parent)
    {
        var go = new GameObject("PulseOverlay");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color         = new Color(0.55f, 0.04f, 0.04f, 0f); // 深红，alpha 由协程驱动
        img.raycastTarget = false;
        return img;
    }

    // 深红脉冲：每 2.2 秒一次，用 sin 曲线做呼吸感，alpha 在 0 ~ 0.18 之间
    private IEnumerator PulseLoop(Image overlay)
    {
        const float period  = 2.2f;
        const float maxAlpha = 0.18f;
        float phase = 0f;
        while (overlay != null)
        {
            phase += Time.unscaledDeltaTime / period * (Mathf.PI * 2f);
            // sin 在 -1~1，映射到 0~1，再乘 vignette 整体 alpha 同步淡入淡出
            float t = (Mathf.Sin(phase - Mathf.PI * 0.5f) + 1f) * 0.5f;
            float vigAlpha = _vignetteGroup != null ? _vignetteGroup.alpha : 1f;
            var c = overlay.color;
            c.a = t * maxAlpha * vigAlpha;
            overlay.color = c;
            yield return null;
        }
    }

    private static RectTransform MakeVignetteBar(Transform parent, bool top)
    {
        var tex = new Texture2D(1, 8, TextureFormat.RGBA32, false);
        tex.wrapMode    = TextureWrapMode.Clamp;
        tex.filterMode  = FilterMode.Bilinear;
        var pixels = new Color[8];
        for (int i = 0; i < 8; i++)
        {
            // 顶部条：i大=纹理顶=屏幕边=黑；底部条：i小=纹理底=屏幕边=黑
            float a = top ? (i / 7f) : (1f - i / 7f);
            pixels[i] = new Color(0f, 0f, 0f, a * 0.82f);
        }
        tex.SetPixels(pixels);
        tex.Apply();

        var go = new GameObject(top ? "VigTop" : "VigBot");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var raw = go.AddComponent<RawImage>();
        raw.texture = tex;
        raw.raycastTarget = false;

        return rt;
    }

    // 等一帧 layout 稳定后，按文字 preferredWidth 调整外框尺寸
    private IEnumerator FitTitleFrame()
    {
        yield return null; // 等 TMP 完成 mesh 更新
        if (_titleTmp == null || _titleFrameRT == null) yield break;
        _titleTmp.ForceMeshUpdate();
        float padX = 28f; // 左右各 14px 余量
        float padY = 10f; // 上下各 5px 余量
        _titleFrameRT.sizeDelta = new Vector2(
            _titleTmp.preferredWidth  + padX,
            _titleTmp.preferredHeight + padY);
        // 对齐到文字锚点中心
        _titleFrameRT.anchoredPosition = Vector2.zero;
    }

    // 与 InventoryItemDetailPanel.QualityHex 保持同一套品级颜色
    private static string ItemQualityHex(int lv) => lv switch
    {
        1 => "B4B2A9",
        2 => "97C459",
        3 => "ED93B1",
        4 => "85B7EB",
        5 => "AFA9EC",
        6 => "1D9E75",
        7 => "D85A30",
        8 => "E24B4A",
        9 => "AFA9EC",
        _ => "B4B2A9"
    };

    private string L(string key, string fallback) =>
        _locTable != null ? _locTable.Get(key, fallback) : fallback;

    private static SkyPrisonWindowHintBar ResolveHintBar()
    {
        // WindowManager 挂着 HintBar；找不到时自己创建一个（与 WindowManager 同路径）
        var wm = FindObjectOfType<SkyPrisonWindowManager_V1>();
        if (wm != null)
        {
            var bar = wm.GetComponent<SkyPrisonWindowHintBar>()
                   ?? wm.gameObject.AddComponent<SkyPrisonWindowHintBar>();
            return bar;
        }
        return null;
    }

    private void SetArrowState(Button btn, CanvasGroup cg, bool active)
    {
        if (btn == null) return;
        // 保持 interactable=true，让点击事件照常触发 Forbidden SE
        var fb = btn.GetComponent<SkyPrisonUIButtonFeedback>();
        if (fb != null) fb.enabled = active;
        if (cg != null) cg.alpha = active ? 1f : 0.35f;
    }

    private void OnPrev()
    {
        if (_items.Count <= 1) { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden); return; }
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        SlideTo((_sel - 1 + _items.Count) % _items.Count, false);
    }

    private void OnNext()
    {
        if (_items.Count <= 1) { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden); return; }
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        SlideTo((_sel + 1) % _items.Count, true);
    }

    private void OnRevive()
    {
        if (_closing || _items.Count == 0) return;
        if (_items[_sel].onGCD) { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden); return; }
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Revive);
        _closing = true;
        _ctrl?.ConfirmUseReviveItem(_items[_sel].entry);
        StartCoroutine(CompressOutAndDestroy());
    }

    private void OnDie()
    {
        if (_closing) return;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
        _closing = true;
        _ctrl?.DeclineReviveAndDie();
        StartCoroutine(CompressOutAndDestroy());
    }

    // ── Update：GCD 倒计时 + 键盘快捷键 ─────────────────────────────────────

    private void Update()
    {
        // 键盘 + 手柄快捷键
        if (!_closing && _inputReady)
        {
            bool left  = Input.GetKeyDown(KeyCode.LeftArrow)  || Input.GetKeyDown(KeyCode.A)
                      || Input.GetKeyDown(KeyCode.JoystickButton14); // DPad Left (PS) / D-Left (Xbox)
            bool right = Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)
                      || Input.GetKeyDown(KeyCode.JoystickButton15); // DPad Right
            bool confirm = Input.GetKeyDown(KeyCode.E)
                        || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                        || Input.GetKeyDown(KeyCode.JoystickButton0); // A(Xbox) / Cross(PS)
            bool cancel  = Input.GetKeyDown(KeyCode.Escape)
                        || Input.GetKeyDown(KeyCode.JoystickButton1); // B(Xbox) / Circle(PS)

            if (left)    OnPrev();
            if (right)   OnNext();
            if (confirm) OnRevive();
            if (cancel)  OnDie();
        }

        // GCD 倒计时刷新
        _tick -= Time.unscaledDeltaTime;
        if (_tick > 0f || _items == null || _items.Count == 0) return;
        _tick = 0.2f;

        // GCD 期间不刷新道具状态（死亡状态下 GCD 不会恢复，防止玩家等待后再用）
        // 只更新 GCD 倒计时文字显示

        if (_gcdText != null && _gcdText.gameObject.activeSelf && _sel < _items.Count)
        {
            float r = PlayerDeathDecisionController.GetReviveGCDRemaining();
            _gcdText.text = r > 0f ? $"冷却剩余 {r:F1} 秒" : "已可使用";
        }
    }

    // ── 工具 ──────────────────────────────────────────────────────────────────

    // 动态构建面板（当 prefab 里没有 WindowPanel 节点时使用）
    private RectTransform BuildPanelFallback()
    {
        var panelGO = new GameObject("WindowPanel");
        var panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.SetParent(transform, false);

        // 居中固定尺寸 780×620
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot     = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(780f, 620f);

        // 深色半透明背景
        var bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.04f, 0.05f, 0.92f);

        // 白色角标（L 形）
        AddCornerBrackets(panelRT, Color.white, 20f, 2f);

        // 标题区
        var titleGO = new GameObject("TitleArea");
        var titleRT = titleGO.AddComponent<RectTransform>();
        titleRT.SetParent(panelRT, false);
        Anchor(titleRT, 0f, 0.82f, 1f, 1f);
        titleGO.AddComponent<Image>().color = Color.clear;

        // 选择区（名字与 prefab 一致，PopulatePanel 会找到它）
        var selGO = new GameObject("ItemSelectorArea");
        var selRT = selGO.AddComponent<RectTransform>();
        selRT.SetParent(panelRT, false);
        Anchor(selRT, 0.05f, 0.33f, 0.95f, 0.82f);

        // 箭头
        BuildFallbackArrow(selRT, "ArrowL", true);
        BuildFallbackArrow(selRT, "ArrowR", false);

        // 视口→卡片容器→卡片
        var vpGO = new GameObject("Viewport"); var vpRT = vpGO.AddComponent<RectTransform>(); vpRT.SetParent(selRT, false);
        Anchor(vpRT, 0.10f, 0f, 0.90f, 1f);
        vpGO.AddComponent<Image>().color = Color.white;
        var vmask = vpGO.AddComponent<Mask>(); vmask.showMaskGraphic = false;
        var ccGO = new GameObject("CardContainer"); var ccRT = ccGO.AddComponent<RectTransform>(); ccRT.SetParent(vpRT, false); Anchor(ccRT, 0f, 0f, 1f, 1f);
        var cGO = new GameObject("Card"); var cRT = cGO.AddComponent<RectTransform>(); cRT.SetParent(ccRT, false); Anchor(cRT, 0f, 0f, 1f, 1f);
        var iGO = new GameObject("Icon"); var iRT = iGO.AddComponent<RectTransform>(); iRT.SetParent(cRT, false);
        iRT.anchorMin = iRT.anchorMax = new Vector2(0.5f,0.5f); iRT.pivot = new Vector2(0.5f,0.5f);
        iRT.sizeDelta = new Vector2(120f,120f); iRT.anchoredPosition = new Vector2(0f,20f);
        iGO.AddComponent<Image>().color = Color.white;
        var sfGO2 = new GameObject("SelFrame"); var sfRT = sfGO2.AddComponent<RectTransform>(); sfRT.SetParent(iRT, false); Anchor(sfRT, 0f,0f,1f,1f); sfGO2.AddComponent<Image>().color = Color.clear;
        // 四边冷绿线
        AddOutlineLine(sfRT, "SF_T", new Vector2(0f,1f), new Vector2(1f,1f), new Vector2(.5f,1f), new Vector2(0f,1.5f));
        AddOutlineLine(sfRT, "SF_B", new Vector2(0f,0f), new Vector2(1f,0f), new Vector2(.5f,0f), new Vector2(0f,1.5f));
        AddOutlineLine(sfRT, "SF_L", new Vector2(0f,0f), new Vector2(0f,1f), new Vector2(0f,.5f), new Vector2(1.5f,0f));
        AddOutlineLine(sfRT, "SF_R", new Vector2(1f,0f), new Vector2(1f,1f), new Vector2(1f,.5f), new Vector2(1.5f,0f));
        MakeStaticTMP(cRT, "Name", "—", 22, new Color(.9f,.9f,.92f,1f), .05f,.16f,.95f,.38f);
        MakeStaticTMP(cRT, "GCD",  "",  17, new Color(.88f,.58f,.18f,1f), .05f,.00f,.95f,.12f);
        // Count 叠在 Icon 右下角，底部齐平
        var cntFallbackRT = MakeRect("Count", iRT);
        cntFallbackRT.anchorMin = cntFallbackRT.anchorMax = new Vector2(1f, 0f);
        cntFallbackRT.pivot = new Vector2(1f, 0f);
        cntFallbackRT.anchoredPosition = new Vector2(-4f, 0f);
        cntFallbackRT.sizeDelta = new Vector2(96f, 44f);
        var cntFallbackTmp = cntFallbackRT.gameObject.AddComponent<TextMeshProUGUI>();
        cntFallbackTmp.text = ""; cntFallbackTmp.fontSize = 36;
        cntFallbackTmp.alignment = TextAlignmentOptions.BottomRight;
        cntFallbackTmp.color = new Color(.88f,.88f,.90f,1f); cntFallbackTmp.raycastTarget = false;
        cntFallbackTmp.enableWordWrapping = false;
        cntFallbackTmp.overflowMode = TextOverflowModes.Overflow;

        // 按钮：使用该道具
        MakeButtonRT(panelRT, "BtnRevive",
            new Vector2(0.06f, 0.05f), new Vector2(0.46f, 0.17f),
            "使用该道具", new Color(0.88f, 0.88f, 0.90f, 1f));

        // 按钮：返回据点
        MakeButtonRT(panelRT, "BtnDie",
            new Vector2(0.54f, 0.05f), new Vector2(0.94f, 0.17f),
            "返回据点", new Color(0.75f, 0.42f, 0.42f, 1f));

        return panelRT;
    }

    private static void MakeButtonRT(RectTransform panel, string name,
        Vector2 aMin, Vector2 aMax, string label, Color textColor)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(panel, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;

        go.AddComponent<Image>().color = Color.clear;
        go.AddComponent<Button>();
        AddOutline(rt, Color.white, 3f);
        SkyPrisonUIButtonFeedback.Attach(go);

        var lbl = new GameObject("Label");
        var lblRT = lbl.AddComponent<RectTransform>();
        lblRT.SetParent(rt, false);
        Anchor(lblRT, 0f, 0f, 1f, 1f);
        var tmp = lbl.AddComponent<TextMeshProUGUI>();
        tmp.text = label; tmp.fontSize = 36;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = textColor; tmp.raycastTarget = false;
    }

    private static void AddOutline(RectTransform rt, Color c, float px)
    {
        AddLineRT(rt, "OT", new Vector2(0f,1f), new Vector2(1f,1f), new Vector2(.5f,1f), Vector2.zero, new Vector2(0f,px), c);
        AddLineRT(rt, "OB", new Vector2(0f,0f), new Vector2(1f,0f), new Vector2(.5f,0f), Vector2.zero, new Vector2(0f,px), c);
        AddLineRT(rt, "OL", new Vector2(0f,0f), new Vector2(0f,1f), new Vector2(0f,.5f), Vector2.zero, new Vector2(px,0f), c);
        AddLineRT(rt, "OR", new Vector2(1f,0f), new Vector2(1f,1f), new Vector2(1f,.5f), Vector2.zero, new Vector2(px,0f), c);
    }

    private static void AddCornerBrackets(RectTransform panel, Color c, float len, float thick)
    {
        Vector2[] corners = { Vector2.zero, new Vector2(1,0), new Vector2(0,1), Vector2.one };
        foreach (var corner in corners)
        {
            // H 线
            var hRT = MakeRect($"CB_H_{corner}", panel);
            hRT.anchorMin = hRT.anchorMax = corner; hRT.pivot = corner;
            hRT.sizeDelta = new Vector2(len, thick); hRT.anchoredPosition = Vector2.zero;
            hRT.gameObject.AddComponent<Image>().color = c;
            // V 线
            var vRT = MakeRect($"CB_V_{corner}", panel);
            vRT.anchorMin = vRT.anchorMax = corner; vRT.pivot = corner;
            vRT.sizeDelta = new Vector2(thick, len); vRT.anchoredPosition = Vector2.zero;
            vRT.gameObject.AddComponent<Image>().color = c;
        }
    }

    private static void BuildFallbackArrow(RectTransform parent, string name, bool left)
    {
        var rt = MakeRect(name, parent);
        if (left) { rt.anchorMin = new Vector2(0f,0.05f); rt.anchorMax = new Vector2(0.12f,0.95f); }
        else       { rt.anchorMin = new Vector2(0.88f,0.05f); rt.anchorMax = new Vector2(1f,0.95f); }
        rt.sizeDelta = rt.anchoredPosition = Vector2.zero;
        rt.gameObject.AddComponent<Image>().color = Color.clear;
        rt.gameObject.AddComponent<Button>();
        AddLineRT(rt,"T",new Vector2(0f,.9f),new Vector2(1f,1f),new Vector2(.5f,1f),Vector2.zero,new Vector2(0f,1f),Color.white);
        AddLineRT(rt,"B",new Vector2(0f,0f),new Vector2(1f,0f),new Vector2(.5f,0f),Vector2.zero,new Vector2(0f,1f),Color.white);
        AddLineRT(rt,"L",new Vector2(0f,0f),new Vector2(0f,1f),new Vector2(0f,.5f),Vector2.zero,new Vector2(1f,0f),Color.white);
        AddLineRT(rt,"R",new Vector2(1f,0f),new Vector2(1f,1f),new Vector2(1f,.5f),Vector2.zero,new Vector2(1f,0f),Color.white);
        SkyPrisonUIButtonFeedback.Attach(rt.gameObject);
        // 靜態上下文直接建 TMP，不依賴實例字體
        var glyphRT = MakeRect("G", rt);
        Anchor(glyphRT, 0f, 0f, 1f, 1f);
        var glyph = glyphRT.gameObject.AddComponent<TextMeshProUGUI>();
        glyph.text = left ? "<" : ">"; glyph.fontSize = 40;
        glyph.alignment = TextAlignmentOptions.Center;
        glyph.color = new Color(.8f,.8f,.84f,1f); glyph.raycastTarget = false;
    }

    private static TextMeshProUGUI MakeStaticTMP(RectTransform p, string n, string text,
        float sz, Color c, float x0, float y0, float x1, float y1)
    {
        var rt = MakeRect(n, p);
        Anchor(rt, x0, y0, x1, y1);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = sz; tmp.color = c;
        tmp.alignment = TextAlignmentOptions.Center; tmp.raycastTarget = false;
        return tmp;
    }

    private static GameObject BuildFallbackRoot()
    {
        var go = new GameObject("PlayerDeathReviveUI_Fallback");
        go.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();
        go.AddComponent<CanvasGroup>();
        return go;
    }

    private static RectTransform MakeRect(string n, Transform p)
    {
        var go = new GameObject(n);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(p, false);
        return rt;
    }

    // 创建 TMP 文字节点，字体优先用实例上缓存的 _tmpFont
    private TextMeshProUGUI AddTMP(string n, Transform p, float sz,
                                   TextAlignmentOptions align, Color c)
    {
        var rt  = MakeRect(n, p);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.fontSize      = sz;
        tmp.alignment     = align;
        tmp.color         = c;
        tmp.raycastTarget = false;
        if (_tmpFont != null)
        {
            tmp.font = _tmpFont;
            tmp.fontSharedMaterial = _tmpFont.material;
        }
        return tmp;
    }

    private Button Arrow(string n, Transform p, string glyph, bool left)
    {
        var rt = MakeRect(n, p);
        // 左箭头占 0%~12%，右箭头占 88%~100%（在父区域内完整显示）
        if (left) { rt.anchorMin = new Vector2(0f, 0.15f); rt.anchorMax = new Vector2(0.08f, 0.85f); }
        else       { rt.anchorMin = new Vector2(0.92f, 0.15f); rt.anchorMax = new Vector2(1f, 0.85f); }
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;

        rt.gameObject.AddComponent<Image>().color = Color.clear;
        var btn = rt.gameObject.AddComponent<Button>();
        var bc  = ColorBlock.defaultColorBlock;
        bc.normalColor = bc.highlightedColor = bc.pressedColor = Color.clear;
        bc.colorMultiplier = 1f;
        btn.colors = bc;

        // 无边框，直接大字符
        SkyPrisonUIButtonFeedback.Attach(rt.gameObject);

        var t = AddTMP("G", rt, 60, TextAlignmentOptions.Center, new Color(.72f,.72f,.76f,1f));
        t.text = glyph;
        Anchor(t.rectTransform, 0f, 0f, 1f, 1f);
        return btn;
    }

    private static void AddLineRT(RectTransform parent, string name,
        Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
    {
        var rt = MakeRect(name, parent);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        rt.gameObject.AddComponent<Image>().color = c;
        rt.GetComponent<Image>().raycastTarget = false;
    }

    private static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
    {
        rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
        rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
    }

    private static void SetPivotKeepPosition(RectTransform rt, Vector2 pivot)
    {
        if (rt == null) return;
        Vector2 size   = rt.rect.size;
        Vector2 dPivot = pivot - rt.pivot;
        Vector2 dPos   = new Vector2(dPivot.x * size.x * rt.localScale.x,
                                     dPivot.y * size.y * rt.localScale.y);
        rt.pivot            = pivot;
        rt.anchoredPosition += dPos;
    }
}
