using System.Collections;
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 悬浮窗互相不能重叠的登记表——背包和角色信息面板都用各自不同的拖拽脚本
/// （InventoryDragHandler / SkyPrisonUIWindowDragHandle），两边坐标系（各自独立的
/// Canvas）也不一样，没法直接互相引用对方的 RectTransform 做位置换算。用屏幕空间
/// （ScreenSpaceOverlay 的 GetWorldCorners 就是屏幕像素）做公共比较基准，谁在拖动
/// 只要查一下"除了自己之外，注册在案的其它窗口现在的屏幕矩形"，拖动结果会跟这些
/// 矩形重叠就直接拒绝这次移动（停在重叠前最后一次合法位置），不做更复杂的"贴边滑动"。
/// </summary>
public static class SkyPrisonWindowOverlapGuard
{
    private static readonly List<RectTransform> _openWindows = new List<RectTransform>();

    public static void Register(RectTransform rt)
    {
        if (rt != null && !_openWindows.Contains(rt))
            _openWindows.Add(rt);
    }

    public static void Unregister(RectTransform rt) => _openWindows.Remove(rt);

    /// <summary>self 如果按 selfWorldCorners 这个屏幕矩形摆放，会不会跟其它已注册窗口重叠。</summary>
    public static bool WouldOverlapOthers(RectTransform self, Rect selfScreenRect)
    {
        for (int i = 0; i < _openWindows.Count; i++)
        {
            RectTransform other = _openWindows[i];
            if (other == null || other == self) continue;

            var corners = new Vector3[4];
            other.GetWorldCorners(corners);
            var otherRect = new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
            if (selfScreenRect.Overlaps(otherRect))
                return true;
        }
        return false;
    }

    /// <summary>让窗口的四条边都不超出所在 Canvas 的范围——拖拽到屏幕外面的通用夹取逻辑，
    /// 跟 InventoryDragHandler.ClampToCanvas 是同一套算法，抽到这里两边共用，不用各写一份。
    /// 适用于任意 anchor / pivot 组合。</summary>
    public static Vector2 ClampToCanvas(RectTransform target, Vector2 desiredAnchoredPosition, RectTransform canvasRect)
    {
        if (canvasRect == null) return desiredAnchoredPosition;

        Rect canvas = canvasRect.rect; // canvas 本地空间矩形（原点在 canvas pivot）
        Vector2 half = canvas.size * 0.5f;

        Vector2 panelSize = new Vector2(
            target.rect.width  * target.localScale.x,
            target.rect.height * target.localScale.y
        );

        Vector2 anchorRef = new Vector2(
            Mathf.Lerp(-half.x, half.x, (target.anchorMin.x + target.anchorMax.x) * 0.5f),
            Mathf.Lerp(-half.y, half.y, (target.anchorMin.y + target.anchorMax.y) * 0.5f)
        );

        Vector2 pivot = desiredAnchoredPosition + anchorRef;

        float toLeft   = target.pivot.x * panelSize.x;
        float toRight  = (1f - target.pivot.x) * panelSize.x;
        float toBottom = target.pivot.y * panelSize.y;
        float toTop    = (1f - target.pivot.y) * panelSize.y;

        float clampedX = Mathf.Clamp(pivot.x, canvas.xMin + toLeft,   canvas.xMax - toRight);
        float clampedY = Mathf.Clamp(pivot.y, canvas.yMin + toBottom, canvas.yMax - toTop);

        return new Vector2(clampedX, clampedY) - anchorRef;
    }

    // 贴脸但严格意义上两个矩形还没真正相交时 Rect.Overlaps 会判定"没重叠"，视觉上却已经
    // 紧贴在一起了——加一圈缓冲区，提前一点判定"够近了"。
    private const float HintOverlapMargin = 60f;

    /// <summary>统一判断"现在有没有任何一个已注册窗口盖住底部提示条"，然后统一决定提示条
    /// 该在顶部还是底部——之前角色信息面板和背包各自查各自的窗口、各自调 SetEdgeTop，
    /// 两边互不知道对方的存在，谁在这一帧后调用就覆盖掉谁的判断，导致提示条卡在错误的
    /// 位置死活不切换。现在只认这一个入口，查的是"所有注册窗口"而不是"调用者自己那个"，
    /// 不管从哪个窗口的 Update 里调用，结果都一样，不会再互相打架。</summary>
    // 用户明确要求：以后所有按键提示条都不用刻意躲避窗口了，固定在底部就好，不用
    // 判断窗口有没有盖住它就跳到顶部——这个方法保留（很多地方在调，改签名要动一堆
    // 调用点），但内部改成什么都不做，永远保持在底部。
    public static void RefreshHintBarEdgeForAllWindows()
    {
        SkyPrisonWindowHintBar.SetEdgeTop(false);
    }
}

/// <summary>
/// 悬浮式窗口的统一入口——角标大小、关闭按钮、标题栏、磨砂背景、开关动画时长、
/// 按键提示条避让，这几件事以后新窗口一律从这里取，不允许各自抄一份数字再各改各的。
///
/// 起因：背包窗口（PF_SkyPrisonInventory.prefab，美术手工搭的预制体）跟角色信息面板
/// （CharacterPanelController，代码动态搭的）角标大小、字号都不一样——每个窗口自己抄一遍
/// 数字，抄着抄着就分家了。代码搭的窗口以后全部通过这个类建，保证 100% 一致；预制体那种
/// 没法直接"引用代码"的（背包目前还是这样），后续要统一的话得单独把预制体里的角标/图标
/// 尺寺改成跟这里一样的数字，或者把背包也迁移成代码搭建。
/// </summary>
public static class SkyPrisonFloatingWindowKit
{
    // 背包窗口身上那个 SkyPrisonInventoryPanelScaler 一直卡在 1.3 倍缩放，改代码怎么都
    // 刷不掉（很可能是背包这条链路本身还有别的地方在覆盖，一时半会排不干净）。与其继续
    // 死磕"删掉1.3倍"，不如反过来：把背包这个实际已经跑了很久、玩家已经看惯的 1.3倍
    // 大小，直接当成新的"1.0基准"，所有窗口（包括角色信息面板）都按这个基准来，不再
    // 追求"绝对像素相等于背包原始设计值"，而是"渲染出来的最终大小跟背包一致"。
    // 下面所有标准数值 = 原始设计值 × StandardScaleMultiplier，只改这一个倍率就能让
    // 所有引用这里的窗口一起变。
    public const float StandardScaleMultiplier = 1.3f;

    // ── 标准规格（已经乘过 1.3 倍）──────────────────────────────────────────
    public const float CornerBracketLength    = 30f * StandardScaleMultiplier; // 39
    public const float CornerBracketThickness = 3f  * StandardScaleMultiplier; // 3.9
    public static readonly Color CornerBracketColor = Color.white;

    // 标题栏高度/关闭按钮/字号——原始设计值对标背包窗口(PF_SkyPrisonInventory.prefab)
    // 里量出来的数字（标题栏64、关闭按钮40、正文28），乘上 1.3 倍之后就是背包实际
    // 渲染出来的真实大小。
    public const float TitleBarHeight   = 64f * StandardScaleMultiplier; // 83.2
    public const float TitleFontSize    = 28f * StandardScaleMultiplier; // 36.4
    public const float CloseButtonSize  = 40f * StandardScaleMultiplier; // 52
    public const float CloseButtonMargin = 12f * StandardScaleMultiplier; // 15.6
    public const string CloseIconGuid   = "1a860d9de75042546ba9c69ed9e23434"; // UI/UIWindow_Default_Close

    // ── 正文字号——同一套档位，4K 基准，原始设计值对标背包实测值（tab/格子数字是28，
    // 部分小标签是18），同样乘 1.3 倍。以后哪个窗口要调字号大小，改
    // StandardScaleMultiplier 或者这几个原始值，所有引用这里的窗口一起变，不要在
    // 具体窗口脚本里再写死一个数字。
    public const float PrimaryFontSize     = 28f * StandardScaleMultiplier; // 36.4
    public const float DescriptionFontSize = 18f * StandardScaleMultiplier; // 23.4
    public const float DecorativeFontSize  = 18f * StandardScaleMultiplier; // 23.4

    public const float OpenCloseAnimDuration = 0.18f;

    // 贴脸但严格意义上两个矩形还没真正相交时 Rect.Overlaps 会判定"没重叠"，视觉上却已经
    // 紧贴在一起了——加一圈缓冲区，提前一点判定"够近了"。
    public const float HintOverlapMargin = 60f;

    // ── 磨砂背景 + 黑白 ───────────────────────────────────────────────────
    // 跟背包窗口同一套组件、同一种接法：RawImage 节点本身就是窗口这么大，不是铺满全屏；
    // SkyPrisonBlurUVTracker 每帧按这个节点在屏幕上的实际位置裁 UV，窗口拖动的时候磨砂
    // 范围会跟着窗口一起走。模糊相机延后一帧启用（同一帧建 GameObject 又立刻测量屏幕分辨率
    // 会量到错误值，这是 PauseMenuController/SettingsWindowUI 都踩过的坑）。
    public static void BuildBlurBackground(MonoBehaviour host, RectTransform box, out SkyPrisonBlurUVTracker tracker)
    {
        var blurRt = MakeRect("Blur", box, Vector2.zero, Vector2.one);
        var blurImg = blurRt.gameObject.AddComponent<RawImage>();
        blurImg.raycastTarget = false;
        // RawImage 在 texture 还没赋值之前用 Unity 内置纯白贴图渲染，表现成"开窗瞬间白屏
        // 闪一下"——必须在这里立刻禁用，不能指望等下面 AddComponent<SkyPrisonInventoryBlurBackground>()
        // 触发的 OnEnable 去禁用它：AddComponent 会同步立刻触发一次 OnEnable，但那时候
        // SetReferences 还没调用、blurImage 字段还是 null，OnEnable 里"blurImage!=null
        // 才禁用"的判断会直接跳过，白屏至少露一帧。SkyPrisonInventoryBlurBackground 自己
        // 的延迟逻辑只负责"贴图真正建好之前不要重新启用"，不负责"创建瞬间就先禁用"。
        blurImg.enabled = false;
        var blurComp = blurRt.gameObject.AddComponent<SkyPrisonInventoryBlurBackground>();
        var uvTracker = blurRt.gameObject.AddComponent<SkyPrisonBlurUVTracker>();
        blurComp.SetReferences(blurImg, blurRt);
        blurComp.Saturation = 0f;
        uvTracker.Bind(blurImg, blurRt);
        tracker = uvTracker;
    }

    // ── 标题栏 + 拖拽 ─────────────────────────────────────────────────────
    public static RectTransform BuildTitleBar(RectTransform box, string title, TMP_FontAsset font, out TMP_Text titleLabel)
    {
        var titleBarRt = MakeRect("TitleBar", box, new Vector2(0f, 1f), new Vector2(1f, 1f));
        titleBarRt.pivot = new Vector2(0.5f, 1f);
        titleBarRt.sizeDelta = new Vector2(0f, TitleBarHeight);
        // 拖拽区不需要任何可见底色——留白色/半透明色会在模糊图上方切出一条看得见的色差
        // 分界线，透明 Image 只用来接收拖拽区域的点击/拖动事件。
        titleBarRt.gameObject.AddComponent<Image>().color = Color.clear;
        var dragHandle = titleBarRt.gameObject.AddComponent<SkyPrisonUIWindowDragHandle>();
        dragHandle.Init(box);

        var titleLabelRt = MakeRect("Title", titleBarRt, Vector2.zero, Vector2.one);
        titleLabelRt.offsetMin = new Vector2(32f, 0f);
        titleLabelRt.offsetMax = new Vector2(-96f, 0f); // 右边让出关闭按钮的位置
        titleLabel = MakeText(titleLabelRt, "Label", title, TitleFontSize, FontStyles.Bold, font);
        titleLabel.alignment = TextAlignmentOptions.MidlineLeft;
        titleLabel.raycastTarget = false;

        return titleBarRt;
    }

    // ── 关闭按钮 ──────────────────────────────────────────────────────────
    public static void BuildCloseButton(RectTransform box, System.Action onClose, TMP_FontAsset font)
    {
        Sprite closeIcon = LoadSpriteByGuid(CloseIconGuid);

        var btnRt = MakeRect("CloseButton", box, Vector2.zero, Vector2.zero);
        btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 1f);
        btnRt.pivot = new Vector2(1f, 1f);
        btnRt.sizeDelta = new Vector2(CloseButtonSize, CloseButtonSize);
        btnRt.anchoredPosition = new Vector2(-CloseButtonMargin, -CloseButtonMargin);

        btnRt.gameObject.AddComponent<Image>().color = Color.clear;
        var btn = btnRt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(() => onClose?.Invoke());
        // WASD/方向键默认会被 Unity 自带的 StandaloneInputModule 当成UI导航轴
        // (Horizontal/Vertical)读取，Automatic导航模式下会把EventSystem选中对象
        // 自动挪到邻近的Selectable上——窗口里但凡有自定义的手柄光标系统(比如
        // SkyPrisonListGamepadNav)，这套自带导航完全是多余且会捣乱的，统一关掉。
        var closeBtnNav = btn.navigation;
        closeBtnNav.mode = Navigation.Mode.None;
        btn.navigation = closeBtnNav;
        SkyPrisonUIButtonFeedback.Attach(btnRt.gameObject);

        if (closeIcon != null)
        {
            // 图标本身故意比按钮点击区域(40x40)大一圈——背包那边的关闭图标贴图内部
            // 留白很多(实际可见的×图案比贴图本身小)，如果直接把图标铺满40x40的按钮，
            // 可见图案会比背包那边明显小一圈。这里照抄背包(CloseButton/Icon)实际的
            // 64px 宽、右边对齐的做法，保证两边可见的×图案大小是一致的。
            var iconRt = MakeRect("Icon", btnRt, new Vector2(1f, 0f), new Vector2(1f, 1f));
            iconRt.pivot = new Vector2(1f, 0.5f);
            iconRt.sizeDelta = new Vector2(64f * StandardScaleMultiplier, 0f);
            var icon = iconRt.gameObject.AddComponent<Image>();
            icon.sprite = closeIcon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }
        else
        {
            AddOutline(btnRt, Color.white, 3f);
            MakeText(btnRt, "Label", "×", 40f, FontStyles.Normal, font).raycastTarget = false;
        }
    }

    /// <summary>编辑器里按 GUID 精确找资产，Build 里没有 AssetDatabase，已知的关闭图标 GUID
    /// 直接退到 Resources 路径兜底。</summary>
    public static Sprite LoadSpriteByGuid(string guid)
    {
#if UNITY_EDITOR
        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(path))
        {
            var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
        }
#endif
        if (guid == CloseIconGuid)
            return Resources.Load<Sprite>("UI/UIWindow_Default_Close");
        return null;
    }

    // ── 按键提示条避让 ────────────────────────────────────────────────────
    // 之前这里是"只查调用者自己那个窗口"，角色信息面板和背包各查各的、各调各的
    // SetEdgeTop，互相不知道对方存在，谁后调用就覆盖谁——现在统一转发到
    // SkyPrisonWindowOverlapGuard.RefreshHintBarEdgeForAllWindows()，查的是"所有
    // 已注册窗口"，不管从哪个窗口的 Update 里调用结果都一样，不会再互相打架。
    // 参数保留只是为了不用去改所有调用点，box/cornersBuffer 已经不需要了。
    public static void DriveHintBarAvoidance(RectTransform box, Vector3[] cornersBuffer)
    {
        SkyPrisonWindowOverlapGuard.RefreshHintBarEdgeForAllWindows();
    }

    // ── 角标 ──────────────────────────────────────────────────────────────
    public static void AddCornerBrackets(RectTransform panel) =>
        AddCornerBrackets(panel, CornerBracketColor, CornerBracketLength, CornerBracketThickness);

    public static void AddCornerBrackets(RectTransform panel, Color c, float len, float thick)
    {
        Vector2[] corners = { Vector2.zero, new Vector2(1, 0), new Vector2(0, 1), Vector2.one };
        foreach (var corner in corners)
        {
            var hRT = MakeRect("CB_H", panel, corner, corner);
            hRT.pivot = corner; hRT.sizeDelta = new Vector2(len, thick); hRT.anchoredPosition = Vector2.zero;
            hRT.gameObject.AddComponent<Image>().color = c;

            var vRT = MakeRect("CB_V", panel, corner, corner);
            vRT.pivot = corner; vRT.sizeDelta = new Vector2(thick, len); vRT.anchoredPosition = Vector2.zero;
            vRT.gameObject.AddComponent<Image>().color = c;
        }
    }

    public static void AddOutline(RectTransform rt, Color c, float px)
    {
        AddLineRT(rt, "OT", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, px), c);
        AddLineRT(rt, "OB", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, px), c);
        AddLineRT(rt, "OL", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(px, 0f), c);
        AddLineRT(rt, "OR", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(px, 0f), c);
    }

    public static void AddLineRT(RectTransform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
    }

    // ── 通用几何/文字 helper ──────────────────────────────────────────────
    public static RectTransform MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    public static TMP_Text MakeText(Transform parent, string name, string text, float size, FontStyles style, TMP_FontAsset font)
    {
        var rt = MakeRect(name, parent, Vector2.zero, Vector2.one);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        if (font != null) tmp.font = font;
        return tmp;
    }

    // ── 字体（Build 里动态创建 TMP 文字必须走这套，否则 Build 里显示方块）──────
    public static TMP_FontAsset LoadTMPFont(string assetName)
    {
#if UNITY_EDITOR
        string path = $"Assets/_Project/UIUX/Fonts/TMP/{assetName}.asset";
        var fa = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (fa != null) return fa;
        string[] guids = UnityEditor.AssetDatabase.FindAssets(assetName + " t:TMP_FontAsset");
        if (guids.Length > 0)
        {
            fa = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
            if (fa != null) return fa;
        }
#endif
        return Resources.Load<TMP_FontAsset>($"Fonts & Materials/{assetName}");
    }
}
