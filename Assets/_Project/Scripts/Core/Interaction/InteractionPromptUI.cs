using UnityEngine;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 世界交互提示条。视觉规格与 SkyPrisonItemPickupController 的拾取提示条一致：
/// 圆角淡出底板 + 键帽图标 + 文字，ScreenSpaceOverlay + WorldToScreenPoint 定位。
/// 由 SkyPrisonInteractionController 驱动，不直接挂场景。
/// </summary>
public sealed class InteractionPromptUI : MonoBehaviour
{
    private const int IconSize = 68;
    private const int FontSize = 40;
    private const int SortOrder = 1130; // ItemPickup = 1140，稍低一层

    private float _promptHeight = 2.4f;

    private SkyPrisonInputSettings _input;

    private Canvas        _canvas;
    private RectTransform _panel;
    private Image         _interactIcon;
    private Text          _actionToken;
    private Text          _labelText;
    private bool          _fontBound;
    private static Sprite _fadeSprite;

    // 切换目标提示行——跟 SkyPrisonItemPickupController 的"切换目标"行同规格。
    // 之前这里完全没有这一行：NPC/设施是当前选中目标、旁边又有可拾取物品时，
    // 玩家只看得到"按E与XXX对话"，看不到任何"按G切换"的提示，G键切换逻辑本身
    // 在 SkyPrisonInteractionController 里是正常工作的，只是这边UI从没显示过，
    // 玩家会以为G没反应（用户反馈"在NPC身边丢物品，没法按G切换捡起来"）。
    private GameObject _cycleRow;
    private Image      _cycleIcon;
    private Text       _cycleToken;
    private Text       _cycleLabelText;
    private UILocalizationTable _locTable;

    private IInteractable _showing;
    private int _showingCount = -1;
    private int _showingSelectedIndex = -1;

    public void Configure(float promptHeight) => _promptHeight = promptHeight;

    public void Show(IInteractable target, Camera cam, int count, int selectedIndex)
    {
        EnsureUI();

        bool same = target == _showing;
        _showing = target;

        if (!same || !_canvas.gameObject.activeSelf)
        {
            _canvas.gameObject.SetActive(true);
            TryBindFont();
            RefreshIcon();
        }

        // 更新文字
        _labelText.text  = target.InteractLabel;
        float alpha = target.CanInteract ? 1f : 0.38f;
        _labelText.color = new Color(1f, 1f, 1f, alpha);
        if (_interactIcon != null) _interactIcon.color = new Color(1f, 1f, 1f, alpha);
        if (_actionToken  != null) _actionToken.color  = new Color(0.56f, 0.84f, 1f, alpha);

        if (count != _showingCount || selectedIndex != _showingSelectedIndex)
        {
            _showingCount = count;
            _showingSelectedIndex = selectedIndex;
            RefreshCycleRow(count, selectedIndex);
        }

        // 跟随世界坐标
        Vector3 worldPos = target.InteractPosition + Vector3.up * _promptHeight;
        Vector2 screen   = cam != null
            ? cam.WorldToScreenPoint(worldPos)
            : (Vector2)worldPos;

        _panel.position = screen;
    }

    private void RefreshCycleRow(int count, int selectedIndex)
    {
        bool showCycle = count > 1;
        _cycleRow?.SetActive(showCycle);
        if (!showCycle) return;

        SkyPrisonInputSettings input = GetInput();
        SkyPrisonInputPromptIconDatabase iconDb = SkyPrisonQuickItemPromptStrip.RuntimeDatabase
            ?? Resources.Load<SkyPrisonInputPromptIconDatabase>("InputPromptIconDatabase");
        bool preferGamepad = SkyPrisonInputDeviceTracker.Current == SkyPrisonInputDeviceTracker.DeviceFamily.Gamepad;

        bool hasCycleIcon = false;
        if (input != null && iconDb != null &&
            SkyPrisonInputPromptResolver.TryResolveActionIcon(
                input, iconDb, SkyPrisonInputAction.CyclePickup, preferGamepad,
                SkyPrisonInputPromptDeviceStyle.GamepadXbox,
                out Sprite cSprite, out _, out _) && cSprite != null)
        {
            _cycleIcon.sprite = cSprite;
            _cycleIcon.preserveAspect = true;
            _cycleIcon.enabled = true;
            hasCycleIcon = true;
        }
        else
        {
            _cycleIcon.enabled = false;
        }

        _cycleToken.gameObject.SetActive(!hasCycleIcon);
        if (!hasCycleIcon && input != null)
        {
            var b = input.GetBinding(SkyPrisonInputAction.CyclePickup);
            _cycleToken.text = b != null && b.primaryKey != KeyCode.None ? $"[{b.primaryKey}]" : "[G]";
        }

        UILocalizationTable loc = GetLocTable();
        string cycleLabel = loc != null ? loc.Get("ui_pickup_cycle", "切换目标") : "切换目标";
        _cycleLabelText.text = $"{cycleLabel}  {selectedIndex + 1}/{count}";
    }

    private UILocalizationTable GetLocTable()
    {
        if (_locTable == null)
            _locTable = Resources.Load<UILocalizationTable>("UILocalizationTable")
                     ?? Resources.Load<UILocalizationTable>("Settings/UILocalizationTable");
        return _locTable;
    }

    // 提示条的画布挂在 DontDestroyOnLoad 上，不会随场景卸载；而驱动它的交互控制器随场景销毁时没人再调 Hide，
    // 切场景（比如从余穹出发进 RAID）后提示条会残留在屏幕上不消失。控制器销毁时把画布一并清掉。
    private void OnDestroy()
    {
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    public void Hide()
    {
        _showing = null;
        _showingCount = -1;
        _showingSelectedIndex = -1;
        if (_canvas != null) _canvas.gameObject.SetActive(false);
    }

    // ── UI 构建（跟拾取提示条同规格：圆角淡出底板 + 键帽 + 文字）──────────────────

    private void EnsureUI()
    {
        if (_canvas != null) return;

        var go = new GameObject("[InteractionPrompt]", typeof(RectTransform));
        DontDestroyOnLoad(go);

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortOrder;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        var outerGo = new GameObject("Outer", typeof(RectTransform));
        outerGo.transform.SetParent(go.transform, false);
        _panel       = (RectTransform)outerGo.transform;
        _panel.pivot = new Vector2(0.5f, 0f);

        var outerBg = outerGo.AddComponent<Image>();
        outerBg.sprite = BuildFadeSprite();
        outerBg.type   = Image.Type.Simple;
        outerBg.color  = new Color(0.06f, 0.07f, 0.09f, 0.55f);
        outerBg.raycastTarget = false;

        var outerVl = outerGo.AddComponent<VerticalLayoutGroup>();
        outerVl.childAlignment        = TextAnchor.MiddleLeft;
        outerVl.padding               = new RectOffset(40, 40, 12, 12);
        outerVl.childControlWidth     = true;
        outerVl.childControlHeight    = true;
        outerVl.childForceExpandWidth = true;
        outerVl.childForceExpandHeight = false;

        var outerFit = outerGo.AddComponent<ContentSizeFitter>();
        outerFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        outerFit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(outerGo.transform, false);
        var rowHl = row.AddComponent<HorizontalLayoutGroup>();
        rowHl.childAlignment         = TextAnchor.MiddleLeft;
        rowHl.spacing                = 16f;
        rowHl.childControlWidth      = true;
        rowHl.childControlHeight     = true;
        rowHl.childForceExpandWidth  = false;
        rowHl.childForceExpandHeight = false;
        var rowFit = row.AddComponent<ContentSizeFitter>();
        rowFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        rowFit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // 键帽图标
        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(row.transform, false);
        _interactIcon = iconGo.AddComponent<Image>();
        _interactIcon.raycastTarget = false;
        var iconLe = iconGo.AddComponent<LayoutElement>();
        iconLe.preferredWidth  = IconSize;
        iconLe.preferredHeight = IconSize;

        // 没有图标资源时的兜底文字键帽，比如 "[E]"
        var tokenGo = new GameObject("Token", typeof(RectTransform));
        tokenGo.transform.SetParent(row.transform, false);
        _actionToken = tokenGo.AddComponent<Text>();
        _actionToken.fontSize  = FontSize;
        _actionToken.color     = new Color(0.56f, 0.84f, 1f, 1f);
        _actionToken.alignment = TextAnchor.MiddleLeft;
        _actionToken.horizontalOverflow = HorizontalWrapMode.Overflow;
        _actionToken.raycastTarget = false;
        _actionToken.gameObject.SetActive(false);

        // 动作文字（比如"与NPC对话"）
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(row.transform, false);
        _labelText = labelGo.AddComponent<Text>();
        _labelText.fontSize  = FontSize;
        _labelText.color     = Color.white;
        _labelText.alignment = TextAnchor.MiddleLeft;
        _labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _labelText.raycastTarget = false;

        // ── 行2：[切换键帽] [切换文字]——跟 SkyPrisonItemPickupController 的
        // 切换行同规格，附近有多个可交互对象时显示"切换目标 [G] 1/2"。
        var row2 = new GameObject("Row2", typeof(RectTransform));
        row2.transform.SetParent(outerGo.transform, false);
        _cycleRow = row2;
        var row2Hl = row2.AddComponent<HorizontalLayoutGroup>();
        row2Hl.childAlignment         = TextAnchor.MiddleLeft;
        row2Hl.spacing                = 16f;
        row2Hl.childControlWidth      = true;
        row2Hl.childControlHeight     = true;
        row2Hl.childForceExpandWidth  = false;
        row2Hl.childForceExpandHeight = false;
        var row2Fit = row2.AddComponent<ContentSizeFitter>();
        row2Fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        row2Fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var cIconGo = new GameObject("CycleIcon", typeof(RectTransform));
        cIconGo.transform.SetParent(row2.transform, false);
        _cycleIcon = cIconGo.AddComponent<Image>();
        _cycleIcon.raycastTarget = false;
        var cIconLe = cIconGo.AddComponent<LayoutElement>();
        cIconLe.preferredWidth  = IconSize;
        cIconLe.preferredHeight = IconSize;

        var cTokenGo = new GameObject("CycleToken", typeof(RectTransform));
        cTokenGo.transform.SetParent(row2.transform, false);
        _cycleToken = cTokenGo.AddComponent<Text>();
        _cycleToken.fontSize  = FontSize;
        _cycleToken.color     = new Color(0.56f, 0.84f, 1f, 1f);
        _cycleToken.alignment = TextAnchor.MiddleLeft;
        _cycleToken.horizontalOverflow = HorizontalWrapMode.Overflow;
        _cycleToken.raycastTarget = false;
        _cycleToken.gameObject.SetActive(false);

        var cLabelGo = new GameObject("CycleLabel", typeof(RectTransform));
        cLabelGo.transform.SetParent(row2.transform, false);
        _cycleLabelText = cLabelGo.AddComponent<Text>();
        _cycleLabelText.fontSize  = FontSize;
        _cycleLabelText.color     = new Color(0.75f, 0.78f, 0.82f, 1f);
        _cycleLabelText.alignment = TextAnchor.MiddleLeft;
        _cycleLabelText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _cycleLabelText.raycastTarget = false;

        row2.SetActive(false);
        _canvas.gameObject.SetActive(false);
    }

    private void TryBindFont()
    {
        if (_fontBound) return;
        var loc = LocalizationRuntime.Instance;
        if (loc == null) return;
        Font font = loc.GetCurrentFont();
        if (font == null) return;
        if (_labelText   != null) _labelText.font   = font;
        if (_actionToken != null) _actionToken.font = font;
        if (_cycleToken  != null) _cycleToken.font  = font;
        if (_cycleLabelText != null) _cycleLabelText.font = font;
        _fontBound = true;
    }

    private void RefreshIcon()
    {
        if (_interactIcon == null) return;

        SkyPrisonInputSettings input = GetInput();
        SkyPrisonInputPromptIconDatabase iconDb = SkyPrisonQuickItemPromptStrip.RuntimeDatabase
            ?? Resources.Load<SkyPrisonInputPromptIconDatabase>("InputPromptIconDatabase");

        bool preferGamepad = SkyPrisonInputDeviceTracker.Current == SkyPrisonInputDeviceTracker.DeviceFamily.Gamepad;
        bool hasIcon = false;
        if (input != null && iconDb != null &&
            SkyPrisonInputPromptResolver.TryResolveActionIcon(
                input, iconDb, SkyPrisonInputAction.Interact, preferGamepad,
                SkyPrisonInputPromptDeviceStyle.GamepadXbox,
                out Sprite sprite, out _, out _) && sprite != null)
        {
            _interactIcon.sprite = sprite;
            _interactIcon.preserveAspect = true;
            _interactIcon.enabled = true;
            hasIcon = true;
        }
        else
        {
            _interactIcon.enabled = false;
        }

        _actionToken.gameObject.SetActive(!hasIcon);
        if (!hasIcon && input != null)
        {
            var b = input.GetBinding(SkyPrisonInputAction.Interact);
            _actionToken.text = b != null && b.primaryKey != KeyCode.None ? $"[{b.primaryKey}]" : "[?]";
        }
    }

    private SkyPrisonInputSettings GetInput()
    {
        if (_input == null)
        {
            _input = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings")
                  ?? Resources.Load<SkyPrisonInputSettings>("Settings/SkyPrisonInputSettings");
            _input?.EnsureDefaults();
        }
        return _input;
    }

    private static Sprite BuildFadeSprite()
    {
        if (_fadeSprite != null) return _fadeSprite;
        const int w = 256, h = 8;
        const float edge = 0.22f;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags  = HideFlags.HideAndDontSave
        };
        for (int x = 0; x < w; x++)
        {
            float u = x / (float)(w - 1);
            float a = u < edge ? u / edge : (u > 1f - edge ? (1f - u) / edge : 1f);
            a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(a));
            var c = new Color(1f, 1f, 1f, a);
            for (int y = 0; y < h; y++) tex.SetPixel(x, y, c);
        }
        tex.Apply();
        _fadeSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        _fadeSprite.hideFlags = HideFlags.HideAndDontSave;
        return _fadeSprite;
    }
}
