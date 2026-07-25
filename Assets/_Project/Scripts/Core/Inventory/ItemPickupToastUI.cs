using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 右侧拾取提示栏。使用独立 ScreenSpaceOverlay Canvas，避免主 UI Canvas 渲染冲突。
/// 由 SkyPrisonRuntimeUIDriver 自动 AddComponent 生成，不需要手动挂载。
/// </summary>
public class ItemPickupToastUI : MonoBehaviour
{
    [Header("布局")]
    [SerializeField] private float entryHeight   = 73f;
    [SerializeField] private float entrySpacing  = 8f;
    [SerializeField] private float rightMargin   = 0f;
    [SerializeField] private float bottomMargin  = 380f;

    [Header("动画")]
    [SerializeField] private float slideInDuration = 0.18f;
    [SerializeField] private float holdDuration    = 2.4f;
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float stackShiftSpeed = 10f;

    [Header("外观")]
    [SerializeField] private float entryWidth    = 392f;
    [SerializeField] private Color bgDark        = new Color(0.02f, 0.02f, 0.02f, 0.88f);
    [SerializeField] private Color bgTransparent = new Color(0.02f, 0.02f, 0.02f, 0f);
    [SerializeField] private Color countColor    = new Color(0.92f, 0.92f, 0.90f, 0.75f);
    [SerializeField] private float nameFontSize  = 31f;
    [SerializeField] private float countFontSize = 27f;

    private Canvas        _canvas;
    private RectTransform _container;
    private Texture2D     _gradientTex;
    private TMP_FontAsset _textFont;
    private TMP_FontAsset _numberFont;
    private bool          _fontBound;
    private float         _rainbowHue;

    private class ToastEntry
    {
        public ItemDefinition  def;
        public int             totalCount;
        public GameObject      root;
        public CanvasGroup     cg;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI countText;
        public Image           iconImg;
        public float           targetY;
        public Coroutine       lifetimeRoutine;
        public bool            isRainbow;
    }

    private readonly List<ToastEntry> _entries = new List<ToastEntry>();

    // ── 生命周期 ──────────────────────────────────────────────────────────

    private void Awake()
    {
        BuildGradientTex();
        EnsureCanvas();
    }

    private void OnEnable()
    {
        var inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv != null) inv.OnItemGained += HandleItemGained;
    }

    private void OnDisable()
    {
        var inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv != null) inv.OnItemGained -= HandleItemGained;
    }

    private void OnDestroy()
    {
        if (_gradientTex != null) Destroy(_gradientTex);
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    // ── 独立 Canvas ───────────────────────────────────────────────────────

    private void EnsureCanvas()
    {
        if (_canvas != null) return;

        var cGo = new GameObject("[ItemPickupToastCanvas]") { hideFlags = HideFlags.HideAndDontSave };
        _canvas = cGo.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 50;
        var scaler = cGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        cGo.AddComponent<GraphicRaycaster>();

        // 独立根 Canvas，不挂在 uiRootCanvas 下，必须单独注册，
        // 否则读条揭幕前会以全 alpha 渲染出来（拾取提示会"抢跑"）。
        var canvasGroup = cGo.AddComponent<CanvasGroup>();
        SceneLoader.RegisterGameCanvasForReveal(canvasGroup);

        // 右下角容器，条目向上堆叠
        var conGo = new GameObject("Container", typeof(RectTransform));
        conGo.transform.SetParent(cGo.transform, false);
        _container = conGo.GetComponent<RectTransform>();
        _container.anchorMin        = new Vector2(1f, 0f);
        _container.anchorMax        = new Vector2(1f, 0f);
        _container.pivot            = new Vector2(1f, 0f);
        _container.anchoredPosition = new Vector2(-rightMargin, bottomMargin);
        _container.sizeDelta        = new Vector2(entryWidth, 0f);
    }

    // ── 字体延迟绑定（仿 SkyPrisonItemPickupController）──────────────────

    private void TryBindFont()
    {
        if (_fontBound) return;
        var style = Object.FindObjectOfType<SkyPrisonUIGlobalStyleSettings_V1>();
        if (style == null) return;

        _textFont   = style.defaultTextFont;
        _numberFont = style.defaultNumberFont != null ? style.defaultNumberFont : _textFont;

        if (_textFont == null)
            _textFont = LoadFont("ZhouFangRiMingTi-2 SDF");
        if (_numberFont == null)
            _numberFont = _textFont;

        _fontBound = (_textFont != null);

        // 补刷已创建的条目
        if (_fontBound)
        {
            foreach (var e in _entries)
            {
                if (e.nameText  != null && _textFont   != null) e.nameText.font  = _textFont;
                if (e.countText != null && _numberFont != null) e.countText.font = _numberFont;
            }
        }
    }

    private static TMP_FontAsset LoadFont(string assetName)
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

    // ── Update ────────────────────────────────────────────────────────────

    private void Update()
    {
        TryBindFont();

        _rainbowHue = (_rainbowHue + Time.unscaledDeltaTime * 0.4f) % 1f;

        foreach (var e in _entries)
        {
            if (e.root == null) continue;

            if (e.isRainbow && e.nameText != null)
                e.nameText.color = Color.HSVToRGB(_rainbowHue, 0.75f, 1f);

            var rt = e.root.GetComponent<RectTransform>();
            float cur = rt.anchoredPosition.y;
            if (!Mathf.Approximately(cur, e.targetY))
            {
                float speed = stackShiftSpeed * Mathf.Abs(e.targetY - cur) + 200f;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x,
                    Mathf.MoveTowards(cur, e.targetY, speed * Time.unscaledDeltaTime));
            }
        }
    }

    // ── 渐变贴图 ──────────────────────────────────────────────────────────

    private void BuildGradientTex()
    {
        const int w = 64;
        _gradientTex = new Texture2D(w, 1, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name       = "ItemToastGradient"
        };
        var pixels = new Color[w];
        for (int i = 0; i < w; i++)
        {
            float t     = i / (float)(w - 1);
            float alpha = Mathf.Lerp(bgTransparent.a, bgDark.a, t * t);
            pixels[i]   = new Color(bgDark.r, bgDark.g, bgDark.b, alpha);
        }
        _gradientTex.SetPixels(pixels);
        _gradientTex.Apply();
    }

    // ── 拾取事件 ──────────────────────────────────────────────────────────

    private void HandleItemGained(ItemDefinition def, int amount)
    {
        if (def == null || amount <= 0) return;

        foreach (var ex in _entries)
        {
            if (ex.def == def && ex.root != null)
            {
                ex.totalCount += amount;
                ex.countText.text = $"+{ex.totalCount}";
                if (ex.lifetimeRoutine != null) StopCoroutine(ex.lifetimeRoutine);
                ex.lifetimeRoutine = StartCoroutine(EntryLifetime(ex));
                return;
            }
        }

        var entry = CreateEntry(def, amount);
        _entries.Add(entry);
        RecalcTargetPositions();
        entry.lifetimeRoutine = StartCoroutine(EntryLifetime(entry));
        StartCoroutine(SlideIn(entry));
    }

    // ── 条目创建 ──────────────────────────────────────────────────────────

    private ToastEntry CreateEntry(ItemDefinition def, int amount)
    {
        EnsureCanvas();

        var root = new GameObject("Toast_" + def.itemKey, typeof(RectTransform));
        root.transform.SetParent(_container, false);

        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 0f);
        rt.anchorMax        = new Vector2(1f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.sizeDelta        = new Vector2(0f, entryHeight);
        rt.anchoredPosition = new Vector2(0f, -(entryHeight));

        var cg = root.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        // 渐变背景（宽度跟父容器拉伸）
        var bg = root.AddComponent<Image>();
        bg.sprite = Sprite.Create(_gradientTex,
            new Rect(0, 0, _gradientTex.width, _gradientTex.height),
            new Vector2(0.5f, 0.5f));
        bg.color = Color.white;
        bg.type  = Image.Type.Simple;
        bg.raycastTarget = false;

        // 图标（右侧）
        float iconSize  = entryHeight * 0.62f;
        float iconRight = -8f;

        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(root.transform, false);
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin        = new Vector2(1f, 0.5f);
        iconRt.anchorMax        = new Vector2(1f, 0.5f);
        iconRt.pivot            = new Vector2(1f, 0.5f);
        iconRt.anchoredPosition = new Vector2(iconRight, 0f);
        iconRt.sizeDelta        = new Vector2(iconSize, iconSize);
        var iconImg = iconGo.AddComponent<Image>();
        if (def.icon != null) { iconImg.sprite = def.icon; iconImg.preserveAspect = true; }
        else iconImg.enabled = false;
        iconImg.raycastTarget = false;

        float textLeft  = 10f;
        float textRight = -(iconSize - iconRight + 4f);

        // 数量（上半）
        var countGo = new GameObject("Count", typeof(RectTransform));
        countGo.transform.SetParent(root.transform, false);
        var countRt = countGo.GetComponent<RectTransform>();
        countRt.anchorMin        = new Vector2(0f, 0.5f);
        countRt.anchorMax        = new Vector2(1f, 1f);
        countRt.pivot            = new Vector2(0f, 0.5f);
        countRt.offsetMin        = new Vector2(textLeft,  0f);
        countRt.offsetMax        = new Vector2(textRight, 0f);
        var countText = countGo.AddComponent<TextMeshProUGUI>();
        countText.text          = $"+{amount}";
        countText.fontSize      = countFontSize;
        countText.color         = countColor;
        countText.alignment     = TextAlignmentOptions.MidlineLeft;
        countText.overflowMode  = TextOverflowModes.Overflow;
        countText.raycastTarget = false;
        if (_textFont != null) countText.font = _numberFont;

        // 物品名（下半）
        var nameGo = new GameObject("Name", typeof(RectTransform));
        nameGo.transform.SetParent(root.transform, false);
        var nameRt = nameGo.GetComponent<RectTransform>();
        nameRt.anchorMin        = new Vector2(0f, 0f);
        nameRt.anchorMax        = new Vector2(1f, 0.5f);
        nameRt.pivot            = new Vector2(0f, 0.5f);
        nameRt.offsetMin        = new Vector2(textLeft,  0f);
        nameRt.offsetMax        = new Vector2(textRight, 0f);
        var nameText = nameGo.AddComponent<TextMeshProUGUI>();
        nameText.text           = string.IsNullOrWhiteSpace(def.displayName) ? def.itemKey : def.GetLocalizedDisplayName();
        nameText.fontSize       = nameFontSize;
        nameText.alignment      = TextAlignmentOptions.MidlineLeft;
        nameText.overflowMode   = TextOverflowModes.Overflow;
        nameText.raycastTarget  = false;
        if (_textFont != null) nameText.font = _textFont;

        bool isRainbow = def.itemLevel >= 9;
        nameText.color = isRainbow ? Color.white : LootDropModelLibrary.GetLevelColor(def.itemLevel);

        return new ToastEntry
        {
            def        = def,
            totalCount = amount,
            root       = root,
            cg         = cg,
            nameText   = nameText,
            countText  = countText,
            iconImg    = iconImg,
            targetY    = 0f,
            isRainbow  = isRainbow,
        };
    }

    // ── 动画协程 ──────────────────────────────────────────────────────────

    private IEnumerator SlideIn(ToastEntry e)
    {
        float elapsed = 0f;
        var rt = e.root.GetComponent<RectTransform>();
        while (elapsed < slideInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / slideInDuration), 2f);
            e.cg.alpha = t;
            rt.anchoredPosition = new Vector2(Mathf.Lerp(entryWidth * 0.15f, 0f, t),
                rt.anchoredPosition.y);
            yield return null;
        }
        e.cg.alpha = 1f;
        rt.anchoredPosition = new Vector2(0f, rt.anchoredPosition.y);
    }

    private IEnumerator EntryLifetime(ToastEntry e)
    {
        yield return new WaitForSecondsRealtime(holdDuration);

        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (e.cg != null) e.cg.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration);
            yield return null;
        }

        RemoveEntry(e);
    }

    private void RemoveEntry(ToastEntry e)
    {
        _entries.Remove(e);
        if (e.root != null) Destroy(e.root);
        RecalcTargetPositions();
    }

    private void RecalcTargetPositions()
    {
        float step = entryHeight + entrySpacing;
        for (int i = 0; i < _entries.Count; i++)
            _entries[i].targetY = i * step;
    }
}
