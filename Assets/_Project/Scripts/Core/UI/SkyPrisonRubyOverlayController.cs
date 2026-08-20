using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 注音的真正定位在这里做——不是靠 &lt;space&gt;/&lt;cspace&gt; 标签"算出应该多宽"，
/// 而是等本体文字（纯净文本，没有任何标记）真正渲染完之后，从
/// TMP_TextInfo.characterInfo[] 里读每个字符实际生成出来的坐标，拿真实结果去摆
/// 独立的注音小标签。宽度、位置准不准直接取决于 TMP 自己排的版，多行、自动换行、
/// 字体替换这些都不用额外处理——真实字符坐标本来就已经把这些都算进去了。
///
/// 项目里显示名字/介绍这类文本目前都是 TextMeshProUGUI（Canvas UI），先只支持
/// 这个类型；不是这个类型的 TMP_Text 直接跳过，不报错也不生效。
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public sealed class SkyPrisonRubyOverlayController : MonoBehaviour
{
    [Tooltip("注音字号相对本体字号的比例，日文注音常见比例。")]
    [SerializeField] private float rubySizeRatio = 0.5f;

    [Tooltip("注音底边和本体顶边之间再留的间隙，单位跟字号一样（TMP 的本地/画布单位）。")]
    [SerializeField] private float gapAboveBase = 1f;

    // 2026-08-19：默认不强制——只有单位头顶 HUD 名字那种小字号场景才需要这个保底
    // （字号缩得多，材质本来的描边会跟着细到看不清）；对话框这类本来字号就不小的
    // 地方，材质自己的描边已经够用，强行加粗反而变成不该有的加粗效果。由
    // SkyPrisonRubyTextInstaller 识别到是头顶名字时调 SetOutlineCorrection 单独打开。
    [Tooltip("注音字号缩小之后，描边（如果本体材质有描边）会跟着按比例变细——保底强制给这个宽度（0~1，TMP SDF 描边参数的范围）。0 = 不强制，用材质本来的描边。")]
    [SerializeField] private float minOutlineWidth = 0f;

    public void SetOutlineCorrection(float minWidth) => minOutlineWidth = minWidth;

    private TextMeshProUGUI _tmp;
    private SkyPrisonRubyTextPreprocessor _preprocessor;
    private readonly List<TextMeshProUGUI> _labelPool = new List<TextMeshProUGUI>();
    private readonly List<Material> _labelMaterialCache = new List<Material>();
    private readonly List<Material> _labelMaterialSource = new List<Material>();
    private string _lastText;

    // 注音是摆在本体这一行正上方的独立标签，TMP 排多行文字时并不知道这一行多占了
    // 这块高度——不额外加行距的话，换行之后带注音的那一行会直接怼上（甚至压进）
    // 上一行的底部。只在检测到有注音时临时加，没有注音时还原，不覆盖设计师专门
    // 调过的行距。
    private const float ExtraLineSpacingForRuby = 30f;
    private float _originalLineSpacing;
    private bool _hasOriginalLineSpacing;
    private bool _lastHadRuby;

    private void Awake()
    {
        _tmp = GetComponent<TextMeshProUGUI>();
    }

    public void Bind(SkyPrisonRubyTextPreprocessor preprocessor)
    {
        _preprocessor = preprocessor;
    }

    private void LateUpdate()
    {
        if (_tmp == null || _preprocessor == null)
            return;

        // text 拿到的是"设置时的原始字符串"（含 {A|B} 标记），不是预处理之后的纯净
        // 文本——用它来判断"这次赋值内容变没变"最直接，不用额外自己维护一份缓存
        // 去对比。
        if (_tmp.text == _lastText)
            return;
        _lastText = _tmp.text;

        RefreshOverlays();
    }

    private void RefreshOverlays()
    {
        // 2026-08-19：必须先强制生成网格，再读 LastGroups——LastGroups 只在 TMP 真正
        // 跑一遍预处理器时才会刷新，而这个时机默认是本帧稍后的 Canvas 重建阶段，比
        // 这个 LateUpdate 晚。如果先读 LastGroups 再判断要不要强制更新，读到的是上一次
        // 文本的旧结果——常见后果是刚好判成"没有注音"直接 return，而 _lastText 已经
        // 更新过，下一帧文本没变化直接跳过整个方法，注音标签永远不会补出来（哪怕
        // TMP 自己稍后已经用新文本正确跑过一次预处理）。这就是 DialogueSubtitleHUD
        // 的名字反复不出注音、但头顶 HUD 名字（只赋值一次，且赋值前已被强制过）没事
        // 的真正原因。
        _tmp.ForceMeshUpdate();

        List<SkyPrisonRubyTextProcessor.RubyGroup> groups = _preprocessor.LastGroups;
        bool hasRuby = groups != null && groups.Count > 0;
        ApplyLineSpacingForRuby(hasRuby);

        if (!hasRuby)
        {
            HideAllLabels(0);
            return;
        }

        TMP_TextInfo info = _tmp.textInfo;

        int used = 0;
        foreach (SkyPrisonRubyTextProcessor.RubyGroup group in groups)
        {
            if (group.BaseLength <= 0)
                continue;
            if (group.BaseStartIndex < 0 || group.BaseStartIndex + group.BaseLength > info.characterCount)
                continue;

            TMP_CharacterInfo first = info.characterInfo[group.BaseStartIndex];
            TMP_CharacterInfo last = info.characterInfo[group.BaseStartIndex + group.BaseLength - 1];
            if (!first.isVisible && !last.isVisible)
                continue;

            // 本体这几个字符各自的 topLeft/bottomRight 都是真实生成出来的字形坐标
            // （本地空间，相对这个 TMP_Text 自己的 RectTransform）——取最左字符的
            // 左边、最右字符的右边、两者里更高的顶边，就是这一小段文字的真实包围盒，
            // 不是靠字号乘字符数估出来的。
            float left = Mathf.Min(first.bottomLeft.x, first.topLeft.x);
            float right = Mathf.Max(last.bottomRight.x, last.topRight.x);
            float top = Mathf.Max(first.topLeft.y, last.topRight.y);
            float baseWidth = Mathf.Max(0.0001f, right - left);
            float centerX = (left + right) * 0.5f;

            // 间隙至少留字号的一小部分——固定像素值在字号差异很大的地方（比如
            // 头顶小字 vs 对话框大字）要么显小要么显大，按当前字号留一个比例下限，
            // 确保注音的底边不会贴到/压进本体文字里。
            float gap = Mathf.Max(gapAboveBase, _tmp.fontSize * 0.08f);

            PositionRubyLabel(used++, group.RubyText, centerX, top + gap, baseWidth);
        }

        HideAllLabels(used);
    }

    private void PositionRubyLabel(int index, string rubyText, float centerX, float topY, float baseWidth)
    {
        TextMeshProUGUI label = GetOrCreateLabel(index);
        label.gameObject.SetActive(true);

        label.font = _tmp.font;
        ApplyOutlineCorrectedMaterial(label, index);
        label.color = _tmp.color;
        label.fontSize = _tmp.fontSize * rubySizeRatio;
        label.fontStyle = _tmp.fontStyle;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        label.characterSpacing = 0f;
        label.text = rubyText;

        RectTransform rt = label.rectTransform;
        // characterInfo 里的坐标是相对"父物体（本体文字）自己 Pivot 原点"算的本地
        // 坐标——子物体的锚点必须设成跟父物体的 Pivot 完全一样，anchoredPosition
        // 才会落在同一个参考系里，直接拿 characterInfo 算出来的数字当偏移量用。
        // 之前固定写死 (0.5,0) 相当于"父物体 Rect 左下角为中心"，跟父物体真实
        // Pivot（多数 UI 文本是 (0.5,0.5) 或别的值）对不上，偏移量就全错了。
        Vector2 parentPivot = _tmp.rectTransform.pivot;
        rt.anchorMin = parentPivot;
        rt.anchorMax = parentPivot;
        // 标签自己的 Pivot 用底边中心——这样 anchoredPosition.y 直接对应"注音底边"
        // 的位置，只要 topY 传进来的是"本体顶边 + 间隙"，注音的底边天然就不会跟
        // 本体文字重叠，不用额外再判断。
        rt.pivot = new Vector2(0.5f, 0f);

        int rubyCharCount = rubyText.Length;
        float finalWidth = baseWidth;

        if (rubyCharCount > 1)
        {
            // characterSpacing 这个属性实际用的单位，跟 GetPreferredValues() 返回的
            // 宽度是不是同一套换算基准，我没有把握——上一版直接拿两者相除硬算，
            // 结果撑不够宽。改成不猜换算系数，实测标定：先量 cspace=0 时的真实宽度，
            // 再随便给一个测试值量一次变化多少，用这两个真实数据点反推"每单位
            // cspace 实际让宽度变化多少"，最后一次到位——全程只信 ForceMeshUpdate
            // 之后 characterInfo 里的真实渲染结果，不信任何理论换算。
            float w0 = MeasureLabelWidth(label);

            const float probeSpacing = 50f;
            label.characterSpacing = probeSpacing;
            float w1 = MeasureLabelWidth(label);

            float widthPerSpacingUnit = (w1 - w0) / probeSpacing;
            if (Mathf.Abs(widthPerSpacingUnit) > 0.0001f)
            {
                float needed = baseWidth - w0;
                float cspace = needed / widthPerSpacingUnit;
                label.characterSpacing = cspace;
                finalWidth = MeasureLabelWidth(label);
            }
            else
            {
                // 探测值都撑不出任何变化（比如字体不支持字距），放弃拉伸，退回居中。
                label.characterSpacing = 0f;
                finalWidth = w0;
            }
        }
        else
        {
            label.characterSpacing = 0f;
            finalWidth = MeasureLabelWidth(label);
        }

        // 用最终真实测出来的宽度定容器和位置——万一撑不满/撑过头（比如单字注音，
        // 或者极端情况下拉伸没能完全达标），容器仍然按注音自己的真实宽度居中对齐
        // 到本体正上方，不会因为用了"理论上该有的" baseWidth 而跟实际渲染错位。
        rt.sizeDelta = new Vector2(Mathf.Max(finalWidth, baseWidth), label.fontSize * 1.2f);
        rt.anchoredPosition = new Vector2(centerX, topY);
    }

    /// <summary>注音字号是本体的一半，如果直接共用本体的材质实例，描边宽度这类参数
    /// 是按字形本地空间定义的，缩小显示字号会让描边跟着等比例变细，字号越小越容易
    /// 细到肉眼看不清（本体上明明有黑边，注音上却像是没描边）。这里给每个 label 各
    /// 克隆一份材质实例（只在源材质变了的时候才重新克隆，不是每帧都 new），按缩小
    /// 比例反向补偿描边宽度，并且强制给一个下限，保证不会细到看不见。</summary>
    private void ApplyOutlineCorrectedMaterial(TextMeshProUGUI label, int index)
    {
        Material source = _tmp.fontMaterial;

        // minOutlineWidth<=0 = 不强制，直接共用本体材质，跟材质本来缩小之后的样子
        // 一致——只有明确调用过 SetOutlineCorrection 打开保底的场景（目前是单位头顶
        // HUD 名字）才需要克隆材质、反向补偿描边宽度。
        if (minOutlineWidth <= 0f)
        {
            label.fontMaterial = source;
            return;
        }

        while (_labelMaterialCache.Count <= index)
        {
            _labelMaterialCache.Add(null);
            _labelMaterialSource.Add(null);
        }

        if (_labelMaterialCache[index] == null || _labelMaterialSource[index] != source)
        {
            Material clone = new Material(source) { hideFlags = HideFlags.DontSave };
            if (clone.HasProperty("_OutlineWidth"))
            {
                float baseOutline = source.HasProperty("_OutlineWidth") ? source.GetFloat("_OutlineWidth") : 0f;
                // 缩小到 rubySizeRatio 倍显示，要维持同样的视觉描边粗细，参数要反向
                // 放大差不多同一个倍数；再跟保底值取较大者，防止本体本来就没描边、
                // 或者算出来的补偿值仍然太细。
                float compensated = rubySizeRatio > 0.001f ? baseOutline / rubySizeRatio : baseOutline;
                clone.SetFloat("_OutlineWidth", Mathf.Clamp01(Mathf.Max(compensated, minOutlineWidth)));
            }
            if (clone.HasProperty("_OutlineColor"))
            {
                Color oc = clone.GetColor("_OutlineColor");
                if (oc.a <= 0.01f)
                    clone.SetColor("_OutlineColor", Color.black);
            }

            if (_labelMaterialCache[index] != null)
                Object.DestroyImmediate(_labelMaterialCache[index]);
            _labelMaterialCache[index] = clone;
            _labelMaterialSource[index] = source;
        }

        label.fontMaterial = _labelMaterialCache[index];
    }

    /// <summary>强制立刻生成网格，从真实字符坐标量这个 label 当前文字内容的渲染宽度——
    /// 不用 GetPreferredValues() 预测，直接读渲染结果，跟本体宽度的测量方式完全一致，
    /// 两边保证是同一套坐标/单位系统，不存在换算偏差的可能。</summary>
    private static float MeasureLabelWidth(TextMeshProUGUI label)
    {
        label.ForceMeshUpdate();
        TMP_TextInfo info = label.textInfo;
        if (info.characterCount <= 0)
            return 0f;

        TMP_CharacterInfo first = info.characterInfo[0];
        TMP_CharacterInfo last = info.characterInfo[info.characterCount - 1];
        float left = Mathf.Min(first.bottomLeft.x, first.topLeft.x);
        float right = Mathf.Max(last.bottomRight.x, last.topRight.x);
        return Mathf.Max(0f, right - left);
    }

    private void ApplyLineSpacingForRuby(bool hasRuby)
    {
        if (hasRuby == _lastHadRuby)
            return;
        _lastHadRuby = hasRuby;

        if (hasRuby)
        {
            if (!_hasOriginalLineSpacing)
            {
                _originalLineSpacing = _tmp.lineSpacing;
                _hasOriginalLineSpacing = true;
            }
            _tmp.lineSpacing = _originalLineSpacing + ExtraLineSpacingForRuby;
        }
        else if (_hasOriginalLineSpacing)
        {
            _tmp.lineSpacing = _originalLineSpacing;
            _hasOriginalLineSpacing = false;
        }
    }

    private TextMeshProUGUI GetOrCreateLabel(int index)
    {
        if (index < _labelPool.Count)
            return _labelPool[index];

        GameObject go = new GameObject("RubyLabel_" + index, typeof(RectTransform));
        go.transform.SetParent(_tmp.rectTransform, false);
        go.hideFlags = HideFlags.DontSave;

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        _labelPool.Add(label);
        return label;
    }

    private void HideAllLabels(int fromIndex)
    {
        for (int i = fromIndex; i < _labelPool.Count; i++)
            if (_labelPool[i] != null)
                _labelPool[i].gameObject.SetActive(false);
    }
}
