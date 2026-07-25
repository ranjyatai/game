using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UnitStatusIconView : MonoBehaviour
{
    public RectTransform root;
    public Image baseIcon;
    public Image grayscaleMask;
    public TextMeshProUGUI stackText;

    // 图标现在统一是白色（不再是彩色/黑白两套图），剩余时间不再靠"彩色 vs 灰度"
    // 区分，改成靠透明度区分：剩余量=不透明白色，消逝量=半透明白色。
    [Tooltip("消逝掉的那部分（底图）用多少透明度的白色显示，剩余部分固定是不透明白色(alpha=1)。")]
    [Range(0f, 1f)]
    public float elapsedAlpha = 0.35f;

    private void OnDisable()
    {
        ResetView();
    }

    public void EnsureStructure()
    {
        if (root == null)
            root = transform as RectTransform;

        if (root == null)
            root = gameObject.GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();

        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);

        baseIcon = EnsureImage("BaseIcon", baseIcon);
        grayscaleMask = EnsureImage("DurationMask", grayscaleMask);
        stackText = EnsureText("StackText", stackText);

        ConfigureBaseIcon();
        ConfigureOverlayIcon();
        ConfigureStackText();
        EnsureVisualOrder();
    }

    public void Apply(StatusViewData data, StatusDisplayDirection fallbackDirection)
    {
        EnsureStructure();

        Sprite sprite = data.icon;
        bool hasSprite = sprite != null;

        baseIcon.sprite = sprite;
        baseIcon.enabled = hasSprite;

        grayscaleMask.sprite = sprite;
        grayscaleMask.enabled = hasSprite;

        RefreshStackText(data.stacks);
        RefreshDurationVisual(data, fallbackDirection);
        EnsureVisualOrder();
    }

    private void RefreshDurationVisual(StatusViewData data, StatusDisplayDirection fallbackDirection)
    {
        if (baseIcon == null || grayscaleMask == null)
            return;

        bool hasSprite = baseIcon.sprite != null;
        if (!hasSprite)
        {
            baseIcon.enabled = false;
            grayscaleMask.enabled = false;
            return;
        }

        float remaining = Mathf.Clamp01(data.remainingNormalized);
        bool useDurationMask = !data.hideDurationMask;

        if (!useDurationMask)
        {
            ApplyFullColorMode();
            return;
        }

        ApplyGrayscaleBaseMode();

        StatusDurationFillMode mode = data.durationFillMode;
        if (mode == StatusDurationFillMode.FollowDisplayDirection)
            mode = ConvertFromDisplayDirection(fallbackDirection);

        if (mode == StatusDurationFillMode.None)
        {
            ApplyFullColorMode();
            return;
        }

        grayscaleMask.enabled = true;
        grayscaleMask.color = Color.white;
        grayscaleMask.material = null;

        ApplyRemainingOverlay(mode, remaining);
    }

    private void ApplyFullColorMode()
    {
        baseIcon.material = null;
        baseIcon.color = Color.white;
        baseIcon.enabled = baseIcon.sprite != null;

        grayscaleMask.material = null;
        grayscaleMask.color = Color.white;
        grayscaleMask.enabled = false;
        grayscaleMask.fillAmount = 0f;
    }

    private void ApplyGrayscaleBaseMode()
    {
        // 底图代表"已经消逝掉"的那部分，纯白图不需要灰度材质（白色去饱和还是白色，
        // 视觉上没有区分度），改成半透明白色——上面盖着的 grayscaleMask 用不透明
        // 白色按 fillAmount 盖出"剩余"部分，两层叠起来就是"剩余不透明、消逝半透明"。
        baseIcon.material = null;
        baseIcon.color = new Color(1f, 1f, 1f, elapsedAlpha);
        baseIcon.enabled = baseIcon.sprite != null;
    }

    private void ApplyRemainingOverlay(StatusDurationFillMode mode, float remaining)
    {
        switch (mode)
        {
            case StatusDurationFillMode.LeftToRight:
                grayscaleMask.type = Image.Type.Filled;
                grayscaleMask.fillMethod = Image.FillMethod.Horizontal;
                grayscaleMask.fillOrigin = 1; // Right
                grayscaleMask.fillClockwise = true;
                grayscaleMask.fillAmount = remaining;
                break;

            case StatusDurationFillMode.RightToLeft:
                grayscaleMask.type = Image.Type.Filled;
                grayscaleMask.fillMethod = Image.FillMethod.Horizontal;
                grayscaleMask.fillOrigin = 0; // Left
                grayscaleMask.fillClockwise = true;
                grayscaleMask.fillAmount = remaining;
                break;

            case StatusDurationFillMode.BottomToTop:
                grayscaleMask.type = Image.Type.Filled;
                grayscaleMask.fillMethod = Image.FillMethod.Vertical;
                grayscaleMask.fillOrigin = 1; // Top
                grayscaleMask.fillClockwise = true;
                grayscaleMask.fillAmount = remaining;
                break;

            case StatusDurationFillMode.TopToBottom:
                grayscaleMask.type = Image.Type.Filled;
                grayscaleMask.fillMethod = Image.FillMethod.Vertical;
                grayscaleMask.fillOrigin = 0; // Bottom
                grayscaleMask.fillClockwise = true;
                grayscaleMask.fillAmount = remaining;
                break;

            case StatusDurationFillMode.RadialClockwise:
                grayscaleMask.type = Image.Type.Filled;
                grayscaleMask.fillMethod = Image.FillMethod.Radial360;
                grayscaleMask.fillOrigin = 2;
                grayscaleMask.fillClockwise = false;
                grayscaleMask.fillAmount = remaining;
                break;

            case StatusDurationFillMode.RadialCounterClockwise:
                grayscaleMask.type = Image.Type.Filled;
                grayscaleMask.fillMethod = Image.FillMethod.Radial360;
                grayscaleMask.fillOrigin = 2;
                grayscaleMask.fillClockwise = true;
                grayscaleMask.fillAmount = remaining;
                break;

            default:
                grayscaleMask.enabled = false;
                grayscaleMask.fillAmount = 0f;
                break;
        }
    }

    public void ResetView()
    {
        if (baseIcon != null)
        {
            baseIcon.sprite = null;
            baseIcon.enabled = false;
            baseIcon.material = null;
            baseIcon.color = Color.white;
            baseIcon.type = Image.Type.Simple;
            baseIcon.fillAmount = 1f;
        }

        if (grayscaleMask != null)
        {
            grayscaleMask.sprite = null;
            grayscaleMask.enabled = false;
            grayscaleMask.material = null;
            grayscaleMask.color = Color.white;
            grayscaleMask.type = Image.Type.Filled;
            grayscaleMask.fillAmount = 0f;
        }

        if (stackText != null)
        {
            stackText.text = string.Empty;
            stackText.gameObject.SetActive(false);
        }
    }

    private void RefreshStackText(int stacks)
    {
        if (stackText == null)
            return;

        if (stacks > 1)
        {
            stackText.gameObject.SetActive(true);
            stackText.text = stacks > 99 ? "99+" : stacks.ToString();
        }
        else
        {
            stackText.gameObject.SetActive(false);
            stackText.text = string.Empty;
        }
    }

    private StatusDurationFillMode ConvertFromDisplayDirection(StatusDisplayDirection dir)
    {
        switch (dir)
        {
            case StatusDisplayDirection.LeftToRight:
                return StatusDurationFillMode.LeftToRight;
            case StatusDisplayDirection.RightToLeft:
                return StatusDurationFillMode.RightToLeft;
            case StatusDisplayDirection.BottomToTop:
                return StatusDurationFillMode.BottomToTop;
            case StatusDisplayDirection.TopToBottom:
                return StatusDurationFillMode.TopToBottom;
            default:
                return StatusDurationFillMode.LeftToRight;
        }
    }

    private Image EnsureImage(string childName, Image current)
    {
        if (current != null)
            return current;

        Transform child = root.Find(childName);
        Image image = null;

        if (child != null)
            image = child.GetComponent<Image>();

        if (image == null)
        {
            GameObject go = child != null
                ? child.gameObject
                : new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            if (child == null)
                go.transform.SetParent(root, false);

            image = go.GetComponent<Image>();
        }

        RectTransform rt = image.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    private TextMeshProUGUI EnsureText(string childName, TextMeshProUGUI current)
    {
        if (current != null)
            return current;

        Transform child = root.Find(childName);
        TextMeshProUGUI text = null;

        if (child != null)
            text = child.GetComponent<TextMeshProUGUI>();

        if (text == null)
        {
            GameObject go = child != null
                ? child.gameObject
                : new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));

            if (child == null)
                go.transform.SetParent(root, false);

            text = go.GetComponent<TextMeshProUGUI>();
        }

        RectTransform rt = text.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        text.alignment = TextAlignmentOptions.BottomRight;
        text.fontSize = 12f;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        text.color = Color.white;
        text.margin = new Vector4(0f, 0f, 1.5f, 0.5f);
        text.overflowMode = TextOverflowModes.Overflow;
        text.outlineWidth = 0.2f;
        text.outlineColor = new Color(0f, 0f, 0f, 1f);

        return text;
    }

    private void ConfigureBaseIcon()
    {
        if (baseIcon == null)
            return;

        baseIcon.type = Image.Type.Simple;
        baseIcon.fillAmount = 1f;
        baseIcon.color = Color.white;
        baseIcon.material = null;
    }

    private void ConfigureOverlayIcon()
    {
        if (grayscaleMask == null)
            return;

        grayscaleMask.type = Image.Type.Filled;
        grayscaleMask.fillMethod = Image.FillMethod.Vertical;
        grayscaleMask.fillOrigin = 0;
        grayscaleMask.fillClockwise = true;
        grayscaleMask.fillAmount = 1f;
        grayscaleMask.color = Color.white;
        grayscaleMask.material = null;
        grayscaleMask.raycastTarget = false;
    }

    private void ConfigureStackText()
    {
        if (stackText == null)
            return;

        stackText.gameObject.SetActive(false);
        stackText.text = string.Empty;
    }

    private void EnsureVisualOrder()
    {
        if (baseIcon != null)
            baseIcon.transform.SetSiblingIndex(0);

        if (grayscaleMask != null)
            grayscaleMask.transform.SetSiblingIndex(1);

        if (stackText != null)
            stackText.transform.SetSiblingIndex(2);
    }
}
