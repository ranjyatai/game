using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class DamageNumberView : MonoBehaviour
{
    [Header("References")]
    public RectTransform root;
    public CanvasGroup canvasGroup;
    public TextMeshProUGUI label;

    [Header("Normal Hit")]
    public float startScale    = 1.05f;
    public float hitScale      = 1.00f;
    public float finalScale    = 1.00f;
    public float popDuration   = 0.06f;
    public float holdDuration  = 0.20f;
    public float fadeDuration  = 0.18f;

    [Header("DOT / Heal")]
    public float softStartScale   = 1.02f;
    public float softHitScale     = 1.00f;
    public float softFinalScale   = 1.00f;
    public float softPopDuration  = 0.05f;
    public float softHoldDuration = 0.18f;
    public float softFadeDuration = 0.15f;

    [Header("Position")]
    public Vector2 baseAnchoredPosition = Vector2.zero;

    [Header("Style")]
    public TMP_FontAsset fontAsset;
    public float fontSize     = 21f;
    public float outlineWidth = 0.10f;
    public Color outlineColor = new Color(0f, 0f, 0f, 1f);
    public Color healColor    = new Color(0.72f, 0.96f, 0.72f, 1f);
    public float flashBoost   = 0.15f;

    // 暴击演出：不用五颜六色，靠"大小+明暗/反色"区分，跟普通伤害数字保持同一套白色系。
    // 正向暴击：字号放大、闪烁更亮，但绝对不能用绿色——绿色在这套 UI 里是回血的颜色，
    // 暴击用绿色会被玩家看成"这一下在加血"。负向暴击：字号缩小 + 反色（正常是黑边白字，
    // 负会心变成白边黑字），不用红色（红色留给危险/警告语义，跟暴击不是一回事）。
    [Header("Crit")]
    public float critScaleMultiplier         = 1.35f;
    public float critFlashBoost              = 0.35f;
    public float negativeCritScaleMultiplier = 0.75f;

    private float elapsed;
    private bool isPlaying;
    private bool softMode;
    private Vector2 spawnPosition;
    private Color baseColor;
    private float _critScaleMultiplier = 1f;
    private float _flashBoostOverride  = -1f; // <0 = 用默认 flashBoost
    private bool  _useOutlineColorOverride;
    private Color _outlineColorOverride;

    // 描边材质缓存：之前每次 EnsureReferences()（每次 Play() 都会走一遍）都 new 一个新
    // Material 扔给 label.fontMaterial，旧的从没 Destroy() 过——伤害数字弹得越多泄漏越快，
    // 战斗越久越卡。现在缓存复用，只有描边参数或字体真的变了才重新生成。
    private Material _outlineMaterial;
    private Shader _outlineMaterialSourceShader;
    private float _appliedOutlineWidth = float.NaN;
    private Color _appliedOutlineColor = new Color(-1f, -1f, -1f, -1f);

    public bool IsPlaying => isPlaying;

    public void EnsureStructure()
    {
        EnsureReferences();
    }

    private void Awake()
    {
        EnsureReferences();
        ResetImmediate();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    private void Update()
    {
        if (!isPlaying)
            return;

        elapsed += Application.isPlaying ? Time.deltaTime : (1f / 60f);

        float pDur = softMode ? softPopDuration : popDuration;
        float hDur = softMode ? softHoldDuration : holdDuration;
        float fDur = softMode ? softFadeDuration : fadeDuration;

        float s0 = softMode ? softStartScale : startScale;
        float s1 = softMode ? softHitScale : hitScale;
        float s2 = softMode ? softFinalScale : finalScale;

        float t0 = pDur;
        float t1 = t0 + hDur;
        float t2 = t1 + fDur;

        root.anchoredPosition = spawnPosition;

        if (elapsed <= t0)
        {
            float p = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, pDur));
            float eased = EaseOutExpo(p);

            float scale = Mathf.LerpUnclamped(s0, s1, eased) * _critScaleMultiplier;
            root.localScale = new Vector3(scale, scale, 1f);

            canvasGroup.alpha = Mathf.Lerp(0.90f, 1f, p);
            label.color = Color.Lerp(GetFlashColor(baseColor), baseColor, Mathf.Clamp01(p * 1.15f));
            return;
        }

        if (elapsed <= t1)
        {
            float p = Mathf.Clamp01((elapsed - t0) / Mathf.Max(0.0001f, hDur));
            float eased = EaseOutCubic(p);

            float scale = Mathf.Lerp(s1, s2, eased) * _critScaleMultiplier;
            root.localScale = new Vector3(scale, scale, 1f);

            canvasGroup.alpha = 1f;
            label.color = baseColor;
            return;
        }

        if (elapsed <= t2)
        {
            float p = Mathf.Clamp01((elapsed - t1) / Mathf.Max(0.0001f, fDur));
            float eased = EaseInCubic(p);

            // 几乎不平移，只让它在消失时有一点点“退远感”
            Vector2 pos = spawnPosition;
            pos.y += Mathf.Lerp(0f, softMode ? 1.5f : 2.5f, eased);
            root.anchoredPosition = pos;

            float scale = Mathf.Lerp(s2, s2 * 0.97f, eased) * _critScaleMultiplier;
            root.localScale = new Vector3(scale, scale, 1f);

            canvasGroup.alpha = 1f - eased;
            label.color = baseColor;
            return;
        }

        StopAndHide();
    }

    public void Play(float amount, Color color, bool isHeal, Vector2 anchoredPosition, bool preferSoft = false,
        bool isPositiveCrit = false, bool isNegativeCrit = false)
    {
        // 之前这个开关只在 SettingsData/SettingsGameplayPanel 里存了个字段，没有任何
        // 消费方读它，勾了跟没勾一个样。伤害数字永远播，直接在这里拦住。
        if (SaveManager.Settings != null && !SaveManager.Settings.showDamageNumbers)
            return;

        EnsureReferences();

        softMode = preferSoft || isHeal;
        spawnPosition = baseAnchoredPosition + anchoredPosition;

        // 暴击不改变基础色相——正向暴击维持跟普通伤害一样的白色系（只放大+加强闪光），
        // 负向暴击才反色：本来是黑边白字，负会心变成白边黑字，一眼就能跟正常/正向暴击
        // 区分开，也不跟回血的绿色/危险的红色混在一起。
        if (isNegativeCrit && !isHeal)
        {
            baseColor = Color.black;
            _critScaleMultiplier = negativeCritScaleMultiplier;
            _flashBoostOverride = -1f;
            _useOutlineColorOverride = true;
            _outlineColorOverride = Color.white;
        }
        else if (isPositiveCrit && !isHeal)
        {
            baseColor = isHeal ? healColor : color;
            _critScaleMultiplier = critScaleMultiplier;
            _flashBoostOverride = critFlashBoost;
            _useOutlineColorOverride = false;
        }
        else
        {
            baseColor = isHeal ? healColor : color;
            _critScaleMultiplier = 1f;
            _flashBoostOverride = -1f;
            _useOutlineColorOverride = false;
        }

        ApplyOutlineToLabel(); // 上面可能改了描边反色开关，这里立刻按新状态重新应用一次

        elapsed = 0f;
        isPlaying = true;

        label.text = FormatValue(amount, isHeal);
        label.color = GetFlashColor(baseColor);
        root.anchoredPosition = spawnPosition;
        root.localScale = Vector3.one * (softMode ? softStartScale : startScale);
        canvasGroup.alpha = 0.90f;
        gameObject.SetActive(true);
    }

    public void StopAndHide()
    {
        isPlaying = false;
        ResetImmediate();
        gameObject.SetActive(false);
    }

    private void ResetImmediate()
    {
        EnsureReferences();
        root.anchoredPosition = baseAnchoredPosition;
        root.localScale = Vector3.one;
        canvasGroup.alpha = 0f;
    }

    private void EnsureReferences()
    {
        if (root == null)
            root = transform as RectTransform;

        if (root == null)
            root = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        if (label == null)
        {
            Transform child = transform.Find("Label");
            if (child == null)
            {
                GameObject go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                go.transform.SetParent(transform, false);
                child = go.transform;
            }

            label = child.GetComponent<TextMeshProUGUI>();
        }

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(320f, 120f);
        labelRect.anchoredPosition = Vector2.zero;

        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = false;
        if (fontAsset != null)
            label.font = fontAsset;

        label.fontSize  = fontSize;
        label.fontStyle = FontStyles.Normal;
        ApplyOutlineToLabel();
    }

    public void ApplyFontAsset(TMP_FontAsset asset)
    {
        if (asset == null)
            return;

        fontAsset = asset;
        EnsureReferences();

        if (label != null)
        {
            label.font = fontAsset;
            ApplyOutlineToLabel();
        }
    }

    private void ApplyOutlineToLabel()
    {
        if (label == null || label.fontSharedMaterial == null) return;

        // 字体换了（shader 来源变了）才需要真的重建材质；同一个字体、同样的描边参数
        // 直接跳过，不然每次 Play() 都会命中这里。
        bool needNewMaterial = _outlineMaterial == null
            || _outlineMaterialSourceShader != label.fontSharedMaterial.shader;

        if (needNewMaterial)
        {
            if (_outlineMaterial != null)
                DestroyMaterial(_outlineMaterial);

            _outlineMaterial = new Material(label.fontSharedMaterial);
            _outlineMaterialSourceShader = label.fontSharedMaterial.shader;
            _appliedOutlineWidth = float.NaN; // 强制下面重新写入描边参数

            // 光 SetFloat/SetColor 写描边参数没用，shader 里的描边分支必须靠这个关键字
            // 打开才会真的跑起来——之前一直没开，普通伤害数字字本体是白色所以看不出来，
            // 负暴击字本体是黑色，没有描边直接就变成暗背景里近乎隐形的黑字。
            _outlineMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
        }

        // 负会心整体反色：正常黑边白字，负会心变成白边黑字（描边这里用反色，
        // 文字本体的黑色在 Play() 里直接把 baseColor 设成 Color.black）。
        Color effectiveOutlineColor = _useOutlineColorOverride ? _outlineColorOverride : outlineColor;

        if (needNewMaterial || _appliedOutlineWidth != outlineWidth || _appliedOutlineColor != effectiveOutlineColor)
        {
            _outlineMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            _outlineMaterial.SetColor(ShaderUtilities.ID_OutlineColor, effectiveOutlineColor);
            _appliedOutlineWidth = outlineWidth;
            _appliedOutlineColor = effectiveOutlineColor;
        }

        if (label.fontMaterial != _outlineMaterial)
            label.fontMaterial = _outlineMaterial;
    }

    private void DestroyMaterial(Material mat)
    {
        if (mat == null) return;
        if (Application.isPlaying) Destroy(mat);
        else DestroyImmediate(mat);
    }

    private void OnDestroy()
    {
        DestroyMaterial(_outlineMaterial);
        _outlineMaterial = null;
    }

    private string FormatValue(float amount, bool isHeal)
    {
        int rounded = Mathf.RoundToInt(Mathf.Abs(amount));
        if (rounded > 999999)
            rounded = 999999;

        return isHeal ? $"+{rounded}" : rounded.ToString();
    }

    private Color GetFlashColor(Color c)
    {
        float boost = _flashBoostOverride >= 0f ? _flashBoostOverride : flashBoost;
        return new Color(
            Mathf.Clamp01(c.r + boost),
            Mathf.Clamp01(c.g + boost),
            Mathf.Clamp01(c.b + boost),
            c.a);
    }

    private static float EaseOutExpo(float t)
    {
        t = Mathf.Clamp01(t);
        return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    private static float EaseInCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }
}
