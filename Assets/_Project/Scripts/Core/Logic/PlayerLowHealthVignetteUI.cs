using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 残血红色屏边呼吸效果。血量低于阈值时浮现，比濒死 GlitchEffect 弱。
/// 挂在玩家身上，自动创建独立 Canvas 叠层。
/// </summary>
[DisallowMultipleComponent]
public class PlayerLowHealthVignetteUI : MonoBehaviour
{
    [Header("触发条件")]
    [SerializeField] private float lowHealthThreshold = 0.35f;   // HP% 低于此值开始显示

    [Header("强度")]
    [SerializeField] private float maxAlpha      = 0.38f;        // 低血最大透明度（濒死约 0.72）
    [SerializeField] private float pulseSpeed    = 0.9f;         // 呼吸频率（Hz）
    [SerializeField] private float pulseDepth    = 0.35f;        // 呼吸幅度（占 maxAlpha 比例）

    [Header("过渡")]
    [SerializeField] private float fadeSpeed     = 2.5f;         // 淡入淡出速度

    private UnitHealthController _health;
    private Canvas               _canvas;
    private Image                _vignette;
    private Texture2D            _vignetteTex;
    private float                _currentAlpha;

    // ── 生命周期 ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _health = GetComponent<UnitHealthController>()
               ?? GetComponentInParent<UnitHealthController>()
               ?? GetComponentInChildren<UnitHealthController>();

        BuildVignetteCanvas();
    }

    private void OnDestroy()
    {
        if (_canvas  != null) Destroy(_canvas.gameObject);
        if (_vignetteTex != null) Destroy(_vignetteTex);
    }

    private void Update()
    {
        if (_vignette == null || _health == null) return;

        float hpRatio  = _health.MaxHealth > 0f ? _health.CurrentHealth / _health.MaxHealth : 1f;
        bool  isLow    = hpRatio < lowHealthThreshold && _health.CurrentHealth > 0f;

        // 血量越低效果越强（threshold→0 线性）
        float intensity = isLow ? Mathf.Clamp01(1f - hpRatio / lowHealthThreshold) : 0f;

        // 呼吸 sine（0.65~1.0 范围，避免完全消失）
        float breath = 1f - pulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f));
        float target = isLow ? maxAlpha * intensity * breath : 0f;

        _currentAlpha = Mathf.MoveTowards(_currentAlpha, target, fadeSpeed * Time.unscaledDeltaTime);

        var c = _vignette.color;
        c.a = _currentAlpha;
        _vignette.color = c;
    }

    // ── Canvas 构建 ───────────────────────────────────────────────────────

    private void BuildVignetteCanvas()
    {
        var cGo = new GameObject("[LowHealthVignette]") { hideFlags = HideFlags.HideAndDontSave };
        _canvas = cGo.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 49;   // 低于 Toast(50)，不覆盖重要 UI

        var imgGo = new GameObject("Vignette", typeof(RectTransform));
        imgGo.transform.SetParent(cGo.transform, false);

        var rt = imgGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _vignette = imgGo.AddComponent<Image>();
        _vignette.sprite       = BuildVignetteSprite();
        _vignette.color        = new Color(1f, 0f, 0f, 0f);
        _vignette.raycastTarget = false;
    }

    private Sprite BuildVignetteSprite()
    {
        const int size = 128;
        _vignetteTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name       = "LowHealthVignette"
        };

        var pixels = new Color[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - half) / half;
            float dy = (y - half) / half;
            // 从边缘向中心衰减，中心透明
            float dist  = Mathf.Sqrt(dx * dx + dy * dy);
            float alpha = Mathf.Clamp01((dist - 0.45f) / 0.55f);
            alpha = alpha * alpha;   // 平方让过渡更柔
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }

        _vignetteTex.SetPixels(pixels);
        _vignetteTex.Apply();

        return Sprite.Create(_vignetteTex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            size);
    }
}
