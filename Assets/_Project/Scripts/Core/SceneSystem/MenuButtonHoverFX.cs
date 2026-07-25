using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 按钮/条目 hover 色收差动效。
/// 进入：主文字淡入冷绿，红/蓝偏移层从 0 淡入并持续 glitch 抖动。
/// 离开：偏移层淡出消失，主文字恢复白色。
/// 支持多组文字同时生效（比如存档行需要编号/名字/时间戳整行一起有效果），
/// 用 Init() 传入第一组，再用 AddLayer() 追加其余组即可，全部共用同一套时间轴。
/// </summary>
public class MenuButtonHoverFX : MonoBehaviour
{
    private class Layer
    {
        public TMP_Text main, r, b;
        public RectTransform rtR, rtB;
        // 红/蓝层原始 offset。之前直接把 offset 写死成 ±偏移量，这在"整个按钮铺满、
        // offset 本来就是 0"的主菜单按钮上没问题，但用在 AnchorPixelLeft 定位（offset.x
        // 本来就不是 0，要给左边数字区域让位）的文字上，会把原有位置直接抹掉，红蓝层
        // 整个跑偏。改成在原始 offset 基础上叠加偏移量，而不是覆盖掉。
        public Vector2 baseMinR, baseMaxR, baseMinB, baseMaxB;
    }
    private readonly List<Layer> _layers = new();

    // ── 参数 ─────────────────────────────────────────────────────────────
    private Button _btn;
    private static readonly Color ColdGreen  = new Color(0.52f, 0.90f, 0.68f, 1f);
    private static readonly Color RedLayer   = new Color(1f,  0.22f, 0.22f, 1f);
    private static readonly Color BlueLayer  = new Color(0.22f, 0.88f, 0.78f, 1f);

    private const float FadeInDur   = 0.30f;
    private const float FadeOutDur  = 0.22f;
    private const float AlphaMax    = 0.55f;   // 峰值明显可见
    private const float BaseOffset  = 2.0f;    // 偏移增强
    private const float GlitchAmp   = 0.25f;   // 几乎感觉不到的微颤
    private const float GlitchFreq  = 0.35f;   // 非常慢的主频
    private const float GlitchFreq2 = 0.85f;   // 慢谐波

    private Coroutine _current;

    // ─────────────────────────────────────────────────────────────────────

    public void Init(Button btn, TMP_Text main,
        TMP_Text r, RectTransform rtR,
        TMP_Text b, RectTransform rtB)
    {
        _btn = btn;
        _layers.Clear();
        AddLayer(main, r, rtR, b, rtB);
    }

    /// <summary>追加一组要一起做色收差的文字（比如存档行的编号、状态时间戳），
    /// 跟第一组共用同一套淡入/glitch/淡出时间轴，视觉上整行同步生效。</summary>
    public void AddLayer(TMP_Text main, TMP_Text r, RectTransform rtR, TMP_Text b, RectTransform rtB)
    {
        r.gameObject.SetActive(false);
        b.gameObject.SetActive(false);
        _layers.Add(new Layer
        {
            main = main, r = r, b = b, rtR = rtR, rtB = rtB,
            baseMinR = rtR.offsetMin, baseMaxR = rtR.offsetMax,
            baseMinB = rtB.offsetMin, baseMaxB = rtB.offsetMax,
        });
    }

    public void OnEnter()
    {
        if (!_btn.interactable) return;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
        Restart(HoverLoop());
    }

    // 手柄切换光标时调用：激活 FX 但不播 SE（SE 由调用方统一播）
    public void OnEnterSilent()
    {
        if (!_btn.interactable) return;
        Restart(HoverLoop());
    }

    public void OnExit()
    {
        Restart(FadeOut());
    }

    private void Restart(IEnumerator routine)
    {
        if (_current != null) StopCoroutine(_current);
        _current = StartCoroutine(routine);
    }

    // 淡入 → 持续 glitch 抖动
    private IEnumerator HoverLoop()
    {
        foreach (var l in _layers) { l.r.gameObject.SetActive(true); l.b.gameObject.SetActive(true); }

        float t = 0f;
        // 阶段 1：淡入
        while (t < FadeInDur)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / FadeInDur);
            foreach (var l in _layers) l.main.color = Color.Lerp(Color.white, ColdGreen, p);
            ApplyLayers(p * AlphaMax, 0f);
            yield return null;
        }
        foreach (var l in _layers) l.main.color = ColdGreen;

        // 阶段 2：持续 glitch 浮动
        float glitchT = 0f;
        while (true)
        {
            glitchT += Time.unscaledDeltaTime;
            // 两个正弦叠加 → 非周期浮动
            float s1 = Mathf.Sin(glitchT * GlitchFreq  * Mathf.PI * 2f);
            float s2 = Mathf.Sin(glitchT * GlitchFreq2 * Mathf.PI * 2f) * 0.4f;
            float glitch = (s1 + s2) * GlitchAmp;
            // alpha 也随 glitch 轻微呼吸
            float a = AlphaMax * (0.75f + 0.25f * Mathf.Abs(s1));
            ApplyLayers(a, glitch);
            yield return null;
        }
    }

    // 淡出
    private IEnumerator FadeOut()
    {
        var startMain = new Color[_layers.Count];
        for (int i = 0; i < _layers.Count; i++) startMain[i] = _layers[i].main.color;
        Color targetMain = _btn.interactable ? Color.white : new Color(1f, 1f, 1f, 0.3f);
        float startA = GetLayerAlpha();
        float t = 0f;
        while (t < FadeOutDur)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / FadeOutDur);
            for (int i = 0; i < _layers.Count; i++)
                _layers[i].main.color = Color.Lerp(startMain[i], targetMain, p);
            ApplyLayers(Mathf.Lerp(startA, 0f, p), 0f);
            yield return null;
        }
        foreach (var l in _layers)
        {
            l.main.color = targetMain;
            l.r.transform.localScale = Vector3.one;
            l.b.transform.localScale = Vector3.one;
            l.r.gameObject.SetActive(false);
            l.b.gameObject.SetActive(false);
        }
    }

    private void ApplyLayers(float alpha, float extraOffset)
    {
        float off = BaseOffset + extraOffset;
        // 改成一般的"平行"色收差：红/蓝层只做纯横向平移，不再做放射缩放。
        // 放射缩放是绕 RectTransform 的 pivot 转的，遇到又宽又空、文字贴边对齐的框
        // （存档行的名字/时间戳）时，偏移量会因为离 pivot 远近不同而被放大到夸张的
        // 程度，改 pivot 也只能缓解、治标不治本；平移完全不吃 pivot/框大小，
        // 不管套在什么形状的文字框上偏移量都一样、可控。
        var cr = RedLayer;  cr.a = alpha;
        var cb = BlueLayer; cb.a = alpha;

        foreach (var l in _layers)
        {
            l.r.color = cr;
            l.b.color = cb;

            // 叠加小横向偏移强化左右感——在原始 offset 基础上叠加，不能直接覆盖掉
            l.rtR.offsetMin = l.baseMinR + new Vector2(-off, 0f);
            l.rtR.offsetMax = l.baseMaxR + new Vector2(-off, 0f);
            l.rtB.offsetMin = l.baseMinB + new Vector2(off, 0f);
            l.rtB.offsetMax = l.baseMaxB + new Vector2(off, 0f);
        }
    }

    private float GetLayerAlpha()
    {
        if (_layers.Count == 0) return 0f;
        var l = _layers[0];
        return l.r.gameObject.activeSelf ? l.r.color.a : 0f;
    }
}
