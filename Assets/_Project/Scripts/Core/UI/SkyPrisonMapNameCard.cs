using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 进图地名牌：每次进入一张新地图（读条揭幕后 / 区域快速切换后 / 直接进场景），屏幕左上淡入一块名字牌，停一会儿再淡出。
/// 名字取地图编辑器里这张图当前语言的名字（MapLocalizationRegistry），改名自动跟着变；
/// 在 RAID 里副标显示「RAID-01 · 昼 / 夜」，主城等没有副标。
/// 主菜单、读条场景这类不在地图注册表里的场景不出。
/// 位置：左上（顶部居中归 BOSS 血条、屏幕居中归任务演出）。
/// </summary>
public sealed class SkyPrisonMapNameCard : MonoBehaviour
{
    // ── 可调参数 ──
    const float DelayAfterLoad   = 0.45f;   // 读条揭幕动画大约这么久，等它播完再出
    const float FadeIn           = 0.12f;   // 牌子本身只做一个很短的淡入，名字靠打字机逐字出现
    const float CharInterval     = 0.075f;  // 名字每个字出来的间隔（秒）
    const float SubCharInterval  = 0.035f;  // 副标每个字的间隔
    const float Hold             = 1.80f;   // 打完字之后停留
    const float FadeOut          = 0.60f;
    const float LeftMargin       = 150f;    // 画布 3840×2160 下的像素
    const float TopMargin        = 210f;    // 留出左上 HUD 状态条的位置
    const float LineToTitleRatio = 3.5f;    // 横线长度 = 标题宽度的几倍（3~4 倍比较紧凑）
    const float MinLineWidth     = 520f;
    const float MaxLineWidth     = 1500f;
    const float LineThickness    = 2f;      // 横线粗细（画布 4K 单位）
    const float BurstAberration  = 11f;     // 字打完那一瞬间色差错开的像素
    const float RestAberration   = 3f;      // 之后停留时的色差
    const float AberrationSettle = 0.35f;   // 从爆开收到停留值用的时间
    const float CrossInset       = 18f;     // 「+」往左收多少（横线端点到竖线的距离）
    const float CrossSize        = 30f;     // 横线右端「+」的大小
    const int   NameFontSize     = 112;
    const int   SubFontSize      = 40;
    const int   CoordFontSize    = 30;
    const float CoordRightPad    = 12f;   // 经纬度右端相对「+」竖线再往右多伸一点（正值向右）

    static readonly Color ColdGreen = new Color(0.55f, 1f, 0.82f, 1f);   // 冷绿，偏白一点（比上一版淡）   // 冷绿；配合加算混合，亮处会发光

    static Material additiveMat;
    /// <summary>UI 加算材质（Resources/Shaders/UIAdditive）；找不到 shader 时退回默认材质，文字照常显示只是不发光。</summary>
    static Material AdditiveMaterial()
    {
        if (additiveMat != null) return additiveMat;
        var sh = Resources.Load<Shader>("Shaders/UIAdditive");
        if (sh != null) additiveMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        return additiveMat;
    }

    static SkyPrisonMapNameCard current;
    static string lastShownScene = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneLoader.OnLoadComplete -= OnFullLoadComplete; SceneLoader.OnLoadComplete += OnFullLoadComplete;
        SceneLoader.OnQuickSwitchComplete -= OnQuickSwitchComplete; SceneLoader.OnQuickSwitchComplete += OnQuickSwitchComplete;
        // 第一个场景（编辑器里直接从某张地图 Play、或没走读条的进入）：也出一次
        if (!SceneLoader.IsLoading) TryShow(SceneManager.GetActiveScene().name, 0.6f);
    }

    static void OnFullLoadComplete() { TryShow(SceneManager.GetActiveScene().name, DelayAfterLoad); }
    static void OnQuickSwitchComplete() { TryShow(SceneManager.GetActiveScene().name, 0.1f); }

    static void TryShow(string sceneName, float delay)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        var reg = MapLocalizationRegistry.LoadOrNull();
        if (reg == null || reg.FindByScene(sceneName) == null) return;       // 不在地图注册表里（主菜单等）：不出
        if (current != null) Destroy(current.gameObject);
        var go = new GameObject("[MapNameCard]");
        current = go.AddComponent<SkyPrisonMapNameCard>();
        current.StartCoroutine(current.Run(sceneName, delay));
    }

    static Font cachedFont;
    static Font PickFont()
    {
        if (cachedFont != null) return cachedFont;
        Font f = LocalizationRuntime.Instance != null ? LocalizationRuntime.Instance.GetCurrentFont() : null;
        if (f == null)
        {
            var cfg = Resources.Load<LocalizationRuntimeConfig>("SkyPrison/LocalizationRuntimeConfig");
            var def = cfg != null && cfg.settings != null ? cfg.settings.GetDefaultLanguage() : null;
            f = def != null ? def.primaryFont : null;
        }
        if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return f;
    }

    IEnumerator Run(string sceneName, float delay)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < delay) yield return null;
        // 揭幕时如果又在读条（连续切图），这次作废
        if (SceneLoader.IsLoading) { Destroy(gameObject); yield break; }

        var reg = MapLocalizationRegistry.LoadOrNull();
        string name = reg != null ? reg.GetDisplayName(sceneName, sceneName) : sceneName;
        string sub = BuildSubtitle(sceneName);

        // ── 画布（3840×2160 参考，和背包 / 提示条同一套）──
        var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(3840f, 2160f); scaler.matchWidthOrHeight = 0.5f;

        var group = new GameObject("Card", typeof(RectTransform), typeof(CanvasGroup)); group.transform.SetParent(transform, false);
        var rt = (RectTransform)group.transform; rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(1600f, 260f);
        var cg = group.GetComponent<CanvasGroup>(); cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;

        Font font = PickFont();
        var nameText = MakeText("Name", rt, "", NameFontSize, ColdGreen, font);
        nameText.rectTransform.anchorMin = new Vector2(0f, 1f); nameText.rectTransform.anchorMax = new Vector2(1f, 1f); nameText.rectTransform.pivot = new Vector2(0f, 1f);
        nameText.rectTransform.anchoredPosition = Vector2.zero; nameText.rectTransform.sizeDelta = new Vector2(0f, NameFontSize * 1.35f);

        // 色差：红 / 蓝两层同样的字（加算），左右错开几像素，主字在最上面。
        // 打字过程中不出现，等名字全部显示完那一刻才「炸」出来：先错得很开，再很快收到一个小偏移停住。
        Text ghostR = MakeText("NameR", rt, "", NameFontSize, new Color(1f, 0.12f, 0.2f, 0.5f), font);
        Text ghostB = MakeText("NameB", rt, "", NameFontSize, new Color(0.1f, 0.5f, 1f, 0.5f), font);
        foreach (var g in new[] { ghostR, ghostB })
        {
            g.rectTransform.anchorMin = nameText.rectTransform.anchorMin; g.rectTransform.anchorMax = nameText.rectTransform.anchorMax; g.rectTransform.pivot = nameText.rectTransform.pivot;
            g.rectTransform.sizeDelta = nameText.rectTransform.sizeDelta; g.rectTransform.anchoredPosition = Vector2.zero;
        }
        nameText.transform.SetAsLastSibling();   // 主字压在两层色差上面
        // 主字用普通混合（不加算）：加算的话主字和下面的红、蓝两层在字身里叠加，会把冷绿冲成白色；
        // 普通混合下主字把字身盖成纯冷绿，红蓝只在边缘露出来，才是色差该有的样子。红蓝两层和线、十字仍然是加算。
        nameText.material = null;

        float lineY = -(NameFontSize * 1.35f) - 6f;
        var line = new GameObject("Line", typeof(RectTransform), typeof(Image)); line.transform.SetParent(rt, false);
        var lrt = (RectTransform)line.transform; lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f); lrt.pivot = new Vector2(0f, 1f);
        lrt.anchoredPosition = new Vector2(0f, lineY); // 线长 = 标题实际渲染宽度 × LineToTitleRatio（量真实宽度，所以两个字的「余穹」和更长的「旧工厂」比例一致）；再夹一个上下限
        nameText.text = name; float titleW = nameText.preferredWidth; nameText.text = "";
        float lineW = Mathf.Clamp(titleW * LineToTitleRatio, MinLineWidth, MaxLineWidth); lrt.sizeDelta = new Vector2(0f, LineThickness);
        line.GetComponent<Image>().color = new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, 0.8f); line.GetComponent<Image>().raycastTarget = false; line.GetComponent<Image>().material = AdditiveMaterial();

        // 横线右端的「+」十字：两小段垂直交叉的冷绿线，跟着横线的右端走
        var crossH = new GameObject("CrossH", typeof(RectTransform), typeof(Image)); crossH.transform.SetParent(rt, false);
        var crossV = new GameObject("CrossV", typeof(RectTransform), typeof(Image)); crossV.transform.SetParent(rt, false);
        foreach (var cgo in new[] { crossH, crossV })
        {
            var cr = (RectTransform)cgo.transform; cr.anchorMin = cr.anchorMax = new Vector2(0f, 1f); cr.pivot = new Vector2(0.5f, 0.5f);
            var ci = cgo.GetComponent<Image>(); ci.color = new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, 0.95f); ci.raycastTarget = false; ci.material = AdditiveMaterial();
            cgo.SetActive(false);
        }
        ((RectTransform)crossH.transform).sizeDelta = new Vector2(CrossSize, LineThickness);
        ((RectTransform)crossV.transform).sizeDelta = new Vector2(LineThickness, CrossSize);
        Text subText = null;
        if (!string.IsNullOrEmpty(sub))
        {
            subText = MakeText("Sub", rt, "", SubFontSize, new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, 0.8f), font);
            subText.rectTransform.anchorMin = new Vector2(0f, 1f); subText.rectTransform.anchorMax = new Vector2(1f, 1f); subText.rectTransform.pivot = new Vector2(0f, 1f);
            subText.rectTransform.anchoredPosition = new Vector2(0f, lineY - 18f); subText.rectTransform.sizeDelta = new Vector2(0f, SubFontSize * 1.4f);
        }

        // 经纬度：副标行右端（对齐横线右端的「+」），冷绿小字（加算，和副标一致），副标打完后淡入，不打字、不色差
        string coord = BuildCoordinates(sceneName);
        Text coordText = null;
        Color coordColor = new Color(ColdGreen.r, ColdGreen.g, ColdGreen.b, 0.8f);   // 和副标同色同透明度（加算冷绿）
        if (!string.IsNullOrEmpty(coord))
        {
            // 打字机逐字出现，所以用左对齐 + 先量好整行宽度定左端，保证打完时右端正好贴着「+」（右对齐的话打字时整行会往左滑）
            coordText = MakeText("Coord", rt, coord, CoordFontSize, coordColor, font);
            float coordW = coordText.preferredWidth; coordText.text = "";
            coordText.alignment = TextAnchor.UpperLeft;
            coordText.rectTransform.anchorMin = coordText.rectTransform.anchorMax = new Vector2(0f, 1f); coordText.rectTransform.pivot = new Vector2(0f, 1f);
            coordText.rectTransform.anchoredPosition = new Vector2(lineW - CrossInset + CoordRightPad - coordW, lineY - 18f - (SubFontSize - CoordFontSize) * 0.5f);
            coordText.rectTransform.sizeDelta = new Vector2(coordW + 40f, CoordFontSize * 1.4f);
        }

        // ── 打字机：牌子先快速淡入，名字一个字一个字出来（没有光标「_」），名字打完横线展开，再打副标 → 停留 → 淡出 ──
        // 全程用实时时间（读条后游戏时间可能刚恢复）。
        rt.anchoredPosition = new Vector2(LeftMargin, -TopMargin);
        float t = 0f;
        while (t < FadeIn) { t += Time.unscaledDeltaTime; cg.alpha = Mathf.Clamp01(t / FadeIn); yield return null; }
        cg.alpha = 1f;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapNameCardShow);
        for (int i = 1; i <= name.Length; i++)
        {
            nameText.text = name.Substring(0, i);
            OnTypeTick();
            yield return WaitRealtime(CharInterval);
        }
        // 字全显示完：色差延迟出现（一小段爆开再收回）
        ghostR.text = name; ghostB.text = name;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapNameCardAberration);
        t = 0f;
        while (t < AberrationSettle)
        {
            t += Time.unscaledDeltaTime; float k = Mathf.Clamp01(t / AberrationSettle);
            float off = Mathf.Lerp(BurstAberration, RestAberration, 1f - (1f - k) * (1f - k));
            SetAberration(ghostR, ghostB, off);
            yield return null;
        }
        SetAberration(ghostR, ghostB, RestAberration);
        t = 0f;
        crossH.SetActive(true); crossV.SetActive(true);
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapNameCardLine);
        while (t < 0.25f) { t += Time.unscaledDeltaTime; float k = Mathf.Clamp01(t / 0.25f); float cw = lineW * (k * (2f - k)); lrt.sizeDelta = new Vector2(cw, LineThickness); PlaceCross(crossH, crossV, lrt, cw, lineY); yield return null; }
        lrt.sizeDelta = new Vector2(lineW, LineThickness); PlaceCross(crossH, crossV, lrt, lineW, lineY);
        if (subText != null)
            for (int i = 1; i <= sub.Length; i++) { subText.text = sub.Substring(0, i); OnTypeTick(); yield return WaitRealtime(SubCharInterval); }
        if (coordText != null)
            for (int i = 1; i <= coord.Length; i++) { coordText.text = coord.Substring(0, i); OnTypeTick(); yield return WaitRealtime(SubCharInterval); }
        t = 0f; while (t < Hold) { t += Time.unscaledDeltaTime; yield return null; }
        t = 0f;
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapNameCardFadeOut);
        while (t < FadeOut) { t += Time.unscaledDeltaTime; float k = 1f - Mathf.Clamp01(t / FadeOut); cg.alpha = k * k; yield return null; }
        lastShownScene = sceneName;
        if (current == this) current = null;
        Destroy(gameObject);
    }

    static void PlaceCross(GameObject h, GameObject v, RectTransform line, float width, float lineY)
    {
        var p = new Vector2(width - CrossInset, lineY - LineThickness * 0.5f);   // 「+」整体往左靠一点：竖线落在横线端点左边，横线比竖线多伸出一小截
        ((RectTransform)h.transform).anchoredPosition = p; ((RectTransform)v.transform).anchoredPosition = p;
    }

    static void SetAberration(Text r, Text b, float offset)
    {
        r.rectTransform.anchoredPosition = new Vector2(-offset, 0f);
        b.rectTransform.anchoredPosition = new Vector2(offset, 0f);
    }

    /// <summary>每打出一个字调一次。音效以后接在这里（现在不出声）。</summary>
    static void OnTypeTick() { SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.MapNameCardType); }

    static IEnumerator WaitRealtime(float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
    }

    /// <summary>经纬度：地图编辑器里填了就用填的；没填且是本次 RAID，用选关标记同一个公式；否则不显示。</summary>
    static string BuildCoordinates(string sceneName)
    {
        var reg = MapLocalizationRegistry.LoadOrNull(); var e = reg != null ? reg.FindByScene(sceneName) : null;
        if (e != null && !string.IsNullOrWhiteSpace(e.coordinates)) return e.coordinates.Trim();
        if (!string.IsNullOrEmpty(SkyPrisonRaidRunContext.RaidId) && SkyPrisonRaidRunContext.Scene == sceneName) return SkyPrisonRaidRunContext.Coordinates;
        return "";
    }

    /// <summary>副标：在 RAID 里显示「RAID-01 · 昼」，其他地图没有。</summary>
    static string BuildSubtitle(string sceneName)
    {
        if (string.IsNullOrEmpty(SkyPrisonRaidRunContext.RaidId) || SkyPrisonRaidRunContext.Scene != sceneName)
        {   // 不是 RAID：显示地图分类（HUB / FINAL …）
            var reg = MapLocalizationRegistry.LoadOrNull(); var e = reg != null ? reg.FindByScene(sceneName) : null;
            return e != null && !string.IsNullOrEmpty(e.category) ? e.category.ToUpperInvariant() : "";
        }
        string no = "RAID-" + (SkyPrisonRaidRunContext.Order > 0 ? SkyPrisonRaidRunContext.Order : 1).ToString("00");
        string dn = DoorText.Get(SkyPrisonRaidRunContext.Night ? "raid_night" : "raid_day", SkyPrisonRaidRunContext.Night ? "夜" : "昼");
        return no + "  ·  " + dn;
    }

    static Text MakeText(string name, Transform parent, string content, int size, Color color, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.text = content; t.color = color; t.alignment = TextAnchor.UpperLeft; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        var am = AdditiveMaterial(); if (am != null) t.material = am;   // 加算混合：冷绿发光
        return t;
    }
}
