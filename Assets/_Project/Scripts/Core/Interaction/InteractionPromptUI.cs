using UnityEngine;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

/// <summary>
/// 世界交互提示条。与 ItemPickupController 的提示规格一致：
/// ScreenSpaceOverlay Canvas + WorldToScreenPoint 定位。
/// 由 SkyPrisonInteractionController 驱动，不直接挂场景。
/// </summary>
public sealed class InteractionPromptUI : MonoBehaviour
{
    private float _promptHeight = 2.4f;

    private Canvas        _canvas;
    private RectTransform _panel;
    private Image         _interactIcon;
    private Text          _labelText;
    private bool          _fontBound;

    private IInteractable _showing;

    public void Configure(float promptHeight) => _promptHeight = promptHeight;

    public void Show(IInteractable target, Camera cam)
    {
        EnsureUI();

        bool same = target == _showing;
        _showing = target;

        if (!same || !_canvas.gameObject.activeSelf)
        {
            _canvas.gameObject.SetActive(true);
            TryBindFont();
        }

        // 更新文字
        _labelText.text  = target.InteractLabel;
        _labelText.color = target.CanInteract
            ? Color.white
            : new Color(1f, 1f, 1f, 0.38f);

        // 跟随世界坐标
        Vector3 worldPos = target.InteractPosition + Vector3.up * _promptHeight;
        Vector2 screen   = cam != null
            ? cam.WorldToScreenPoint(worldPos)
            : (Vector2)worldPos;

        _panel.position = screen;
    }

    public void Hide()
    {
        _showing = null;
        if (_canvas != null) _canvas.gameObject.SetActive(false);
    }

    // ── UI 构建 ───────────────────────────────────────────────────────────

    private void EnsureUI()
    {
        if (_canvas != null) return;

        var go = new GameObject("[InteractionPrompt]", typeof(RectTransform));
        DontDestroyOnLoad(go);

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1130; // ItemPickup = 1140，稍低一层

        var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // 横排面板：键帽 + 文字
        var panelGo = new GameObject("Panel", typeof(RectTransform));
        panelGo.transform.SetParent(go.transform, false);
        _panel = (RectTransform)panelGo.transform;
        _panel.sizeDelta = Vector2.zero;
        _panel.pivot     = new Vector2(0.5f, 0f);

        var layout = panelGo.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment        = TextAnchor.MiddleCenter;
        layout.spacing               = 12f;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        panelGo.AddComponent<ContentSizeFitter>().horizontalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        // 键帽图标占位（尺寸与 ItemPickup 一致）
        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(_panel, false);
        _interactIcon = iconGo.AddComponent<Image>();
        _interactIcon.color = Color.white;
        var iconLe = iconGo.AddComponent<LayoutElement>();
        iconLe.preferredWidth  = 68f;
        iconLe.preferredHeight = 68f;
        TryBindInteractIcon();

        // 动作文字
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(_panel, false);
        _labelText = labelGo.AddComponent<Text>();
        _labelText.fontSize  = 40;
        _labelText.color     = Color.white;
        _labelText.alignment = TextAnchor.MiddleLeft;
        _labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _labelText.raycastTarget = false;

        _canvas.gameObject.SetActive(false);
    }

    private void TryBindFont()
    {
        if (_fontBound || _labelText == null) return;
        var loc = LocalizationRuntime.Instance;
        if (loc == null) return;
        Font font = loc.GetCurrentFont();
        if (font == null) return;
        _labelText.font = font;
        _fontBound = true;
    }

    private void TryBindInteractIcon()
    {
        if (_interactIcon == null) return;
        var input = FindObjectOfType<SkyPrisonInputSettings>();
        var iconDb = FindObjectOfType<SkyPrisonInputPromptIconDatabase>();
        if (input == null || iconDb == null) return;

        // 取 Interact 动作当前绑定的按键，再转成图标
        var binding = input.GetBinding(SkyPrisonInputAction.Interact);
        if (binding == null) return;
        bool isGamepad = SkyPrisonInputDeviceTracker.Current == SkyPrisonInputDeviceTracker.DeviceFamily.Gamepad;
        var style = isGamepad
            ? SkyPrisonInputPromptDeviceStyle.GamepadXbox
            : SkyPrisonInputPromptDeviceStyle.KeyboardMouse;
        if (iconDb.TryGetSpriteForBinding(binding, style, isGamepad, out Sprite icon, out _, out _))
            _interactIcon.sprite = icon;
    }
}
