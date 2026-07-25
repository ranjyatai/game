using System.Collections;
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 全局自动保存提示：右上角一个转圈图标（尾部渐变淡出）+ 文字，跟随
/// SaveManager.OnSaveStarted/OnSaved/OnSaveFailed 三个事件走"保存中 → 完成/失败 → 淡出"。
/// 自举单例，DontDestroyOnLoad，不需要挂在任何场景/预制体上。
/// </summary>
public sealed class AutoSaveIndicatorUI : MonoBehaviour
{
    private const float SpinDegreesPerSecond = 260f;
    private const float HoldSecondsOnDone    = 0.9f;
    private const float HoldSecondsOnFail    = 1.8f;
    private const float FadeSeconds          = 0.35f;
    // SaveManager.Save() 是同步的，OnSaveStarted → OnSaved 之间只隔几行代码，同一帧就跑完，
    // 圆环转不了一帧就被藏起来。这里强制圆环至少转够这么久，玩家才能真正看到"转圈"这个效果。
    private const float MinSpinSeconds       = 0.6f;

    private static readonly Color ColdGreen = new Color(0.42f, 0.92f, 0.68f, 1f);
    private static readonly Color WarnRed   = new Color(0.85f, 0.40f, 0.38f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<AutoSaveIndicatorUI>() != null) return;
        var go = new GameObject("[AutoSaveIndicator]") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<AutoSaveIndicatorUI>();
    }

    private CanvasGroup _group;
    private RectTransform _spinnerRt;
    private TMP_Text _label;
    private Coroutine _routine;
    private UILocalizationTable _locTable;

    private string L(string key, string fallback) =>
        _locTable != null ? _locTable.Get(key, fallback) : fallback;

    private void Awake()
    {
        _locTable = Resources.Load<UILocalizationTable>("UILocalizationTable");
        Build();
        SaveManager.OnSaveStarted += HandleSaveStarted;
        SaveManager.OnSaved       += HandleSaved;
        SaveManager.OnSaveFailed  += HandleSaveFailed;
        LocalizationRuntime.OnLanguageChanged += HandleLanguageChanged;
    }

    private void OnDestroy()
    {
        SaveManager.OnSaveStarted -= HandleSaveStarted;
        SaveManager.OnSaved       -= HandleSaved;
        SaveManager.OnSaveFailed  -= HandleSaveFailed;
        LocalizationRuntime.OnLanguageChanged -= HandleLanguageChanged;
    }

    private void Update()
    {
        if (_spinnerRt != null && _spinnerRt.gameObject.activeInHierarchy)
            _spinnerRt.Rotate(0f, 0f, -SpinDegreesPerSecond * Time.unscaledDeltaTime);
    }

    private void HandleLanguageChanged(string code) => BindFont();

    private float _savingStartTime;

    private void HandleSaveStarted()
    {
        if (_routine != null) StopCoroutine(_routine);
        _label.text = L("ui_autosave_saving", "自动保存中，请勿退出游戏...");
        _label.color = Color.white;
        _group.alpha = 1f;
        _spinnerRt.gameObject.SetActive(true);
        _savingStartTime = Time.realtimeSinceStartup;
    }

    private void HandleSaved()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FinishRoutine(L("ui_autosave_done", "自动保存完毕"), Color.white, HoldSecondsOnDone));
    }

    private void HandleSaveFailed(string reason)
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FinishRoutine(L("ui_autosave_failed", "自动保存失败"), WarnRed, HoldSecondsOnFail));
    }

    private IEnumerator FinishRoutine(string text, Color color, float hold)
    {
        // 保证圆环至少真实转了 MinSpinSeconds 秒，不会因为存档太快而一帧闪过
        float elapsed = Time.realtimeSinceStartup - _savingStartTime;
        if (elapsed < MinSpinSeconds)
            yield return new WaitForSecondsRealtime(MinSpinSeconds - elapsed);

        _spinnerRt.gameObject.SetActive(false);
        _label.text = text;
        _label.color = color;

        yield return new WaitForSecondsRealtime(hold);

        float t = 0f;
        while (t < FadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Lerp(1f, 0f, t / FadeSeconds);
            yield return null;
        }
        _group.alpha = 0f;
        _routine = null;
    }

    // ── 构建 ──────────────────────────────────────────────────────────────

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32765; // 压过按键提示条（32760），是全局最高优先级的常驻提示之一
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(3840f, 2160f);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable   = false;

        var rootRt = (RectTransform)transform;

        var rowGo = new GameObject("Row", typeof(RectTransform));
        rowGo.transform.SetParent(rootRt, false);
        var rowRt = (RectTransform)rowGo.transform;
        rowRt.anchorMin = rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot     = new Vector2(1f, 1f);
        rowRt.anchoredPosition = new Vector2(-56f, -56f);
        // 宽度按最长语言（日文/英文那句提示比中文长不少）留够，配合下面 label 的自动缩字号，
        // 不会因为切成日/英文就把字挤出屏幕或叠到转圈图标上。
        rowRt.sizeDelta        = new Vector2(920f, 72f);

        var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.spacing        = 20f;
        layout.childControlWidth  = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth  = false;
        layout.childForceExpandHeight = false;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(rowGo.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.sizeDelta = new Vector2(820f, 56f);
        _label = labelGo.AddComponent<TextMeshProUGUI>();
        _label.text        = "";
        _label.fontSize    = 34f;
        _label.alignment   = TextAlignmentOptions.MidlineRight;
        _label.raycastTarget = false;
        // 中/日/英三语长度差很多，固定字号在长文本语言下会被挤出这条盒子——开自动缩字号，
        // 短文本（中文）保持大字号，长文本（日/英）自动缩小到能放下为止。
        _label.enableAutoSizing = true;
        _label.fontSizeMin = 22f;
        _label.fontSizeMax = 34f;
        _label.overflowMode = TextOverflowModes.Truncate;

        var spinnerGo = new GameObject("Spinner", typeof(RectTransform));
        spinnerGo.transform.SetParent(rowGo.transform, false);
        _spinnerRt = (RectTransform)spinnerGo.transform;
        _spinnerRt.sizeDelta = new Vector2(56f, 56f);
        var spinnerImg = spinnerGo.AddComponent<Image>();
        spinnerImg.sprite = BuildSpinnerSprite();
        spinnerImg.color  = ColdGreen;
        spinnerImg.raycastTarget = false;

        BindFont();
        _group.alpha = 0f;
        _spinnerRt.gameObject.SetActive(false);
    }

    // 转圈图标：环形贴图，留一段缺口（约90°）当"C"字缺角，缺口以外的圆弧
    // 沿角度做柔和的拖尾渐变（尾部淡出、头部最亮），配合每帧旋转就是参考图那种
    // 经典 loading spinner 效果，不依赖任何美术资源。
    private static Sprite BuildSpinnerSprite()
    {
        const int size = 128;
        const float outerR = 0.46f;
        const float innerR = 0.30f;
        const float gapDegrees = 90f;               // 缺口张角
        const float arcDegrees = 360f - gapDegrees;  // 实际有像素的弧长
        const float tailMinAlpha = 0.06f;            // 尾部（弧起点）最低透明度，不完全消失
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name       = "AutoSaveSpinnerRing"
        };

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size - 0.5f;
                float ny = (y + 0.5f) / size - 0.5f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float a = 0f;

                if (r >= innerR && r <= outerR)
                {
                    float angleDeg = Mathf.Atan2(ny, nx) * Mathf.Rad2Deg; // -180..180
                    if (angleDeg < 0f) angleDeg += 360f;                  // 0..360

                    if (angleDeg <= arcDegrees)
                    {
                        // 沿弧长从尾部（0°）到头部（arcDegrees）渐亮
                        float t = angleDeg / arcDegrees;
                        a = Mathf.Lerp(tailMinAlpha, 1f, t);

                        // 弧两端各羽化一点角度，避免缺口边缘出现锯齿硬边
                        const float edgeFeatherDeg = 6f;
                        float distFromStart = angleDeg;
                        float distFromEnd   = arcDegrees - angleDeg;
                        float edgeFeather = Mathf.Clamp01(Mathf.Min(distFromStart, distFromEnd) / edgeFeatherDeg);
                        a *= edgeFeather;

                        // 内外边缘也留一点羽化
                        float edge = Mathf.Min(r - innerR, outerR - r);
                        float radialFeather = Mathf.Clamp01(edge / 0.03f);
                        a *= radialFeather;
                    }
                }

                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // 字体走字典表当前语言（中/日/英各自的本地化字体），不是固定用一款中文字体。
    private void BindFont()
    {
        TMP_FontAsset font = LocalizationRuntime.Instance != null
            ? LocalizationRuntime.Instance.GetCurrentTMPFont()
            : null;

        if (font == null)
        {
            var style = FindObjectOfType<SkyPrisonUIGlobalStyleSettings_V1>();
            font = style?.defaultTextFont;
        }
#if UNITY_EDITOR
        if (font == null)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("ZhouFangRiMingTi-2 SDF t:TMP_FontAsset");
            if (guids.Length > 0)
                font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
        }
#endif
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/ZhouFangRiMingTi-2 SDF");
        if (font == null) return;
        _label.font = font;
    }
}
