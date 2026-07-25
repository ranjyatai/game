using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 项目统一的滚动条样式：细窄的胶囊形轨道（上下圆头）+ 同形状的手柄，平时灰白色暗淡，
/// 点击拖动时手柄变成实心白色（Scrollbar 自带的 Selectable 状态过渡，不用额外脚本）。
/// 任何需要滚动列表的窗口（设置/背包/存档选择等）都应该用这个，不要各自再写一份——
/// 第一次是在设置窗口的按键绑定列表里写的，之后但凡要加滚动条就直接调这个。
/// 用法：自己的 ScrollRect 搭好 viewport/content 之后，调用 AttachVertical 把滚动条
/// 贴在同一个父节点的右边，边距参数跟自己的 ScrollArea 保持一致就行。
/// </summary>
public static class SkyPrisonUIScrollbar
{
    /// <summary>
    /// parent：滚动条挂载的父节点（通常就是装 ScrollArea 的那个 box）。
    /// rightMargin/topMargin/bottomMargin：滚动条轨道离 parent 三边的距离，
    /// 跟自己的 ScrollArea 的 offsetMin/Max 对齐，滚动条才会跟列表纵向范围一致。
    /// width：轨道/手柄宽度，同时也决定圆头半径（= width/2），细一点比较好看。
    /// </summary>
    public static Scrollbar AttachVertical(
        ScrollRect scrollRect,
        RectTransform parent,
        Color accentColor,
        float rightMargin = 48f,
        float topMargin = 0f,
        float bottomMargin = 0f,
        float width = 8f,
        ScrollRect.ScrollbarVisibility visibility = ScrollRect.ScrollbarVisibility.Permanent)
    {
        // Sliced 九宫格的边框实际显示尺寸 = 贴图边框像素 × Canvas.referencePixelsPerUnit ÷
        // Sprite.pixelsPerUnit，两次换算漏一个都会让圆头要么被压扁成尖角、要么整个吃掉
        // 剩余显示区域变成一根针——GetCapsuleSprite 里按这条公式反推出正确的 pixelsPerUnit。
        float canvasRefPPU = parent.GetComponentInParent<Canvas>()?.referencePixelsPerUnit ?? 100f;
        var capsule = GetCapsuleSprite(width, canvasRefPPU);

        var trackGo = new GameObject("Scrollbar", typeof(RectTransform));
        var trackRt = (RectTransform)trackGo.transform;
        trackRt.SetParent(parent, false);
        trackRt.anchorMin = new Vector2(1f, 0f);
        trackRt.anchorMax = new Vector2(1f, 1f);
        trackRt.offsetMin = new Vector2(-rightMargin - width, bottomMargin);
        trackRt.offsetMax = new Vector2(-rightMargin, -topMargin);
        var trackImg = trackGo.AddComponent<Image>();
        trackImg.sprite = capsule;
        trackImg.type   = Image.Type.Sliced;
        trackImg.color  = new Color(1f, 1f, 1f, 0.10f); // 轨道本身常驻，但很淡，不抢视线

        var handleGo = new GameObject("Handle", typeof(RectTransform));
        var handleRt = (RectTransform)handleGo.transform;
        handleRt.SetParent(trackRt, false);
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = Vector2.one;
        handleRt.offsetMin = handleRt.offsetMax = Vector2.zero;
        var handleImg = handleGo.AddComponent<Image>();
        handleImg.sprite = capsule;
        handleImg.type   = Image.Type.Sliced;

        var scrollbar = trackGo.AddComponent<Scrollbar>();
        scrollbar.direction     = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect    = handleRt;
        scrollbar.targetGraphic = handleImg;
        scrollbar.transition    = Selectable.Transition.ColorTint;
        // 关掉 Unity 自带的自动方向导航——这是个 Selectable，手柄摇杆/D-pad 默认会让
        // Unity 自己在相邻 Selectable 间切换选中态，跟调用方自己手写的光标系统抢输入，
        // 表现为"光标明明到头不动了，但手柄还是会触发一次多余的移动音效"。
        var scrollbarNav = scrollbar.navigation;
        scrollbarNav.mode = Navigation.Mode.None;
        scrollbar.navigation = scrollbarNav;
        // 平时灰白色、半透明，悬停稍微亮一点，按下拖动时变成实心白色——不用强调色，
        // 强调色（冷绿）只用在真正需要突出的地方，滚动条平时应该低调。
        scrollbar.colors = new ColorBlock
        {
            normalColor      = new Color(0.82f, 0.84f, 0.86f, 0.45f),
            highlightedColor = new Color(0.9f, 0.91f, 0.93f, 0.7f),
            pressedColor     = Color.white,
            selectedColor    = new Color(0.9f, 0.91f, 0.93f, 0.7f),
            disabledColor    = new Color(1f, 1f, 1f, 0.15f),
            colorMultiplier  = 1f,
            fadeDuration     = 0.1f,
        };

        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = visibility;

        return scrollbar;
    }

    // 胶囊形贴图：中段是直边，上下两端各是半圆——配合 Image.Type.Sliced 的九宫格拉伸，
    // 不管滚动条多长，圆头始终保持半径不变，不会被拉伸/压扁成椭圆或尖角。
    // 贴图本身固定 32px 宽，pixelsPerUnit 按目标显示宽度 + Canvas 的 referencePixelsPerUnit
    // 一起反推，让贴图里 16 texture px 的半径换算出来正好是 width/2 个 canvas 单位——
    // 按“显示宽度+canvas单位换算率”这对组合分别缓存。
    private static readonly Dictionary<(float width, float canvasRefPPU), Sprite> _capsuleSpriteCache = new();

    private static Sprite GetCapsuleSprite(float displayWidth, float canvasRefPPU)
    {
        var cacheKey = (displayWidth, canvasRefPPU);
        if (_capsuleSpriteCache.TryGetValue(cacheKey, out var cached) && cached != null)
            return cached;

#if UNITY_EDITOR
        // 之前这里生成的 Sprite/Texture2D 只存在于内存里，从没存过盘。设置窗口这类
        // "运行时Play模式才现搭UI"的调用方没事——反正每次进Play都会重新生成一份，
        // 内存对象够用。但仓库这类"编辑器脚本烤成 .prefab 资产文件"的调用方就会出
        // 大问题：Unity 保存 .prefab 时，Sprite 引用如果指向一个没有磁盘资产/GUID
        // 的内存对象，序列化出来就是空引用(fileID: 0)——这正是仓库滚动条圆头完全
        // 不显示、看起来像一条直线的真正原因（Image.sprite 从一开始就没真的存上，
        // 不是圆角计算错了）。改成在编辑器下把纹理/精灵真正存成一份磁盘资产（按参数
        // 缓存文件名，重复调用直接复用同一份，不会越攒越多），存过盘的 Sprite 才有
        // 真实 GUID，prefab 序列化才保得住引用。
        Sprite persisted = LoadOrCreatePersistedCapsuleSprite(displayWidth, canvasRefPPU);
        if (persisted != null)
        {
            _capsuleSpriteCache[cacheKey] = persisted;
            return persisted;
        }
#endif

        BuildCapsuleTextureAndSprite(displayWidth, canvasRefPPU, out _, out var builtSprite);
        _capsuleSpriteCache[cacheKey] = builtSprite;
        return builtSprite;
    }

    private const int CapsuleTexW = 32;
    private const int CapsuleCapH = 16; // 半圆端的高度 = 半径（贴图像素）
    private const int CapsuleMidH = 32; // 中段直边高度，够长即可，反正会被压缩/拉伸

    private static void BuildCapsuleTextureAndSprite(float displayWidth, float canvasRefPPU, out Texture2D tex, out Sprite sprite)
    {
        const int w = CapsuleTexW;
        const int capH = CapsuleCapH;
        const int midH = CapsuleMidH;
        const int h = capH * 2 + midH;
        const float radius = w * 0.5f;

        tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            name       = "SkyPrisonScrollbarCapsule"
        };

        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float nx = x + 0.5f - radius;
                float a;

                if (y < capH)
                {
                    float ny = y + 0.5f - capH; // 顶部半圆圆心在 (radius, capH)
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);
                    a = Mathf.Clamp01((radius - dist) / 1.5f + 0.5f);
                }
                else if (y >= h - capH)
                {
                    float ny = y + 0.5f - (h - capH); // 底部半圆圆心
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);
                    a = Mathf.Clamp01((radius - dist) / 1.5f + 0.5f);
                }
                else
                {
                    a = Mathf.Clamp01((radius - Mathf.Abs(nx)) / 1.5f + 0.5f); // 中段直边也做一点边缘羽化
                }

                pixels[y * w + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        // Unity 对 Sliced Sprite 的边框换算： 显示边框(canvas单位) = 贴图边框(px) ×
        // referencePixelsPerUnit ÷ sprite.pixelsPerUnit。反推 pixelsPerUnit，让显示边框
        // 正好等于 displayWidth 的一半（半径），圆头才会是真圆，不多不少刚好撑满宽度。
        float desiredRadius = Mathf.Max(displayWidth * 0.5f, 0.01f);
        float ppu = capH * canvasRefPPU / desiredRadius;
        sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu,
            0, SpriteMeshType.FullRect, new Vector4(0f, capH, 0f, capH));
    }

#if UNITY_EDITOR
    private const string PersistedCapsuleDir = "Assets/_Project/UIUX/Generated";

    private static Sprite LoadOrCreatePersistedCapsuleSprite(float displayWidth, float canvasRefPPU)
    {
        // 之前把小数点全部替换成下划线时连".asset"这个必须的扩展名也被换掉了
        // （变成"..._asset"这种没有真实扩展名的文件），Unity认不出这是什么类型的
        // 资产，CreateAsset存进去的东西形同虚设，Sprite引用一直是空的——只替换
        // 数字部分里的小数点，最后再拼接干净的".asset"。
        string widthPart = displayWidth.ToString("0.###").Replace(".", "_");
        string ppuPart   = canvasRefPPU.ToString("0.###").Replace(".", "_");
        string fileName = $"ScrollbarCapsule_w{widthPart}_ppu{ppuPart}.asset";
        string path = $"{PersistedCapsuleDir}/{fileName}";

        var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        if (!UnityEditor.AssetDatabase.IsValidFolder(PersistedCapsuleDir))
        {
            string parent = "Assets/_Project/UIUX";
            if (!UnityEditor.AssetDatabase.IsValidFolder(parent))
                return null; // 不该发生——UIUX目录本来就存在，保底别崩
            UnityEditor.AssetDatabase.CreateFolder(parent, "Generated");
        }

        BuildCapsuleTextureAndSprite(displayWidth, canvasRefPPU, out Texture2D tex, out Sprite sprite);
        UnityEditor.AssetDatabase.CreateAsset(tex, path);
        UnityEditor.AssetDatabase.AddObjectToAsset(sprite, path);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.ImportAsset(path);

        return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
#endif
}
