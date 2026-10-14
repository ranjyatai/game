using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 夜视仪电池图标：手机电池样式（横向、右端小凸起、4 格）。
/// 只在夜视装置已搭载、并且开着 / 没充满 / 冷却中时显示，满电且关着时隐藏。
/// 颜色：正常冷绿；低于 LowCharge 淡红（不闪）；耗尽冷却中灰色、文字提示 CHARGING。
/// 回充时最前面那一格慢慢呼吸。电量逻辑在 <see cref="SkyPrisonNightVisionMode"/>。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonNightVisionBatteryHud : MonoBehaviour
{
    private const int Cells = 4;

    // 屏幕左上角（和 LP 条同一侧），位置偏移可以按实机画面再调。
    private static readonly Vector2 AnchorOffset = new Vector2(12f, -52f);   // FPS 计数在左上角（4K 参考分辨率下 24,-24），电池放它正下方
    private const float CellW = 11f, CellH = 13f, CellGap = 2.5f, Pad = 3f, Border = 1.5f, NubW = 3f, NubH = 7f;

    private static readonly Color Normal = new Color(1f, 1f, 1f, 1f);
    private static readonly Color Low = new Color(1f, 0.68f, 0.68f, 1f);
    private static readonly Color Depleted = new Color(0.72f, 0.74f, 0.76f, 1f);

    private Canvas _canvas;
    private CanvasGroup _group;
    private readonly Image[] _cells = new Image[Cells];
    private readonly Image[] _frame = new Image[5];   // 上 下 左 右 + 凸起
    private TextMeshProUGUI _label;
    private static Material _imageMaterial;

    /// <summary>HUD 色差材质（和 LP / HP 条同一套着色器）：矩形边缘一侧偏红、一侧偏蓝，本体保持白色。</summary>
    private static Material ImageMaterial
    {
        get
        {
            if (_imageMaterial == null)
            {
                Shader sh = Shader.Find("Sky Prison/UI/HUD Chromatic V83");
                if (sh != null)
                {
                    _imageMaterial = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                    _imageMaterial.SetFloat("_ChromaticAmount", 0f);
                    _imageMaterial.SetFloat("_RectEdgeFringeStrength", 0.85f);
                    _imageMaterial.SetFloat("_RectEdgeFringeWidth", 1.2f);
                    _imageMaterial.SetFloat("_ChromaticAngle", 0f);
                }
            }
            return _imageMaterial;
        }
    }

    private static void ApplyChromaticTextMaterial(TextMeshProUGUI t)
    {
        Shader sh = Shader.Find("Sky Prison/UI/HUD TMP Chromatic V83");
        if (sh == null || t.font == null)
            return;
        var m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t.font.atlasTexture);
        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
        t.fontSharedMaterial = m;
    }
    private float _alpha;

    private void Awake()
    {
        var go = new GameObject("[SkyPrisonNightVisionBattery]");
        DontDestroyOnLoad(go);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 41;
        _group = go.AddComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;
        go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        go.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);
        go.GetComponent<CanvasScaler>().matchWidthOrHeight = 0.5f;

        float innerW = Cells * CellW + (Cells - 1) * CellGap;
        float w = innerW + Pad * 2f + Border * 2f;
        float h = CellH + Pad * 2f + Border * 2f;

        var root = new GameObject("Battery", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(go.transform, false);
        root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = AnchorOffset;
        root.sizeDelta = new Vector2(w + NubW, h);

        _frame[0] = Bar(root, 0f, 0f, w, Border, true);                 // 上
        _frame[1] = Bar(root, 0f, h - Border, w, Border, true);         // 下（坐标原点在左上，Y 向下取负）
        _frame[2] = Bar(root, 0f, 0f, Border, h, true);                 // 左
        _frame[3] = Bar(root, w - Border, 0f, Border, h, true);         // 右
        _frame[4] = Bar(root, w, (h - NubH) * 0.5f, NubW, NubH, true);  // 凸起

        for (int i = 0; i < Cells; i++)
            _cells[i] = Bar(root, Border + Pad + i * (CellW + CellGap), Border + Pad, CellW, CellH, true);

        _label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        _label.transform.SetParent(root, false);
        _label.fontSize = 12f;
        _label.fontStyle = FontStyles.Bold;
        _label.characterSpacing = 4f;
        _label.raycastTarget = false;
        _label.enableWordWrapping = false;
        _label.alignment = TextAlignmentOptions.TopLeft;
        RectTransform lr = _label.rectTransform;
        lr.anchorMin = lr.anchorMax = new Vector2(0f, 1f);
        lr.pivot = new Vector2(0f, 1f);
        lr.anchoredPosition = new Vector2(0f, -h - 2f);
        lr.sizeDelta = new Vector2(200f, 18f);
        ApplyChromaticTextMaterial(_label);

        _group.alpha = 0f;
    }

    private static Image Bar(RectTransform parent, float x, float yFromTop, float w, float h, bool raycast)
    {
        var go = new GameObject("p", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -yFromTop);
        rt.sizeDelta = new Vector2(w, h);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        if (ImageMaterial != null)
            img.material = ImageMaterial;
        return img;
    }

    private void OnDestroy()
    {
        if (_canvas != null)
            Destroy(_canvas.gameObject);
    }

    private void LateUpdate()
    {
        bool show = SkyPrisonNightVisionMode.BatteryVisible;
        _alpha = Mathf.MoveTowards(_alpha, show ? 1f : 0f, 5f * Time.unscaledDeltaTime);
        _group.alpha = _alpha;
        if (_alpha <= 0.01f)
            return;

        float charge = SkyPrisonNightVisionMode.Charge;
        bool depleted = SkyPrisonNightVisionMode.Depleted;
        bool charging = !SkyPrisonNightVisionMode.IsActive && charge < 0.999f;
        Color c = depleted ? Depleted : charge < SkyPrisonNightVisionMode.LowCharge ? Low : Normal;

        for (int i = 0; i < _frame.Length; i++)
            _frame[i].color = new Color(c.r, c.g, c.b, 0.9f);

        for (int i = 0; i < Cells; i++)
        {
            float fill = Mathf.Clamp01(charge * Cells - i);      // 这一格填了多少
            float a = fill > 0f ? Mathf.Lerp(0.35f, 0.95f, fill) : 0.08f;
            if (charging && fill > 0f && fill < 1f)
                a *= 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);   // 回充时最前面那一格慢慢呼吸
            _cells[i].color = new Color(c.r, c.g, c.b, a);
        }

        _label.color = new Color(c.r, c.g, c.b, 0.9f);
        _label.text = depleted ? "CHARGING" : charging ? "RECHARGING" : "NV " + Mathf.CeilToInt(charge * 100f) + "%";
    }
}
