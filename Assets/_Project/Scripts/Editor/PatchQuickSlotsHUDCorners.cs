using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SkyPrison.Runtime.UI;

// PF_SkyPrisonBattleHUD 是烤好的 prefab（不是运行时程序化生成的），Unity 开着的时候
// 不能在外部直接改 .prefab 的 YAML（容易跟编辑器内存状态冲突、有损坏风险）——所以这里
// 走 Unity 自己的 PrefabUtility API，在编辑器里点一下菜单来改，安全。
// 项目里这个 prefab 有两份（Prefabs/UI/HUD 和 Resources/UI/HUD，内容已经不完全一样），
// 两份都会各自打上同样的补丁，保持视觉一致。
public static class PatchQuickSlotsHUDCorners
{
    private static readonly string[] PrefabPaths =
    {
        "Assets/_Project/Prefabs/UI/HUD/PF_SkyPrisonBattleHUD.prefab",
        "Assets/_Project/Resources/UI/HUD/PF_SkyPrisonBattleHUD.prefab",
    };

    // 完全去掉这层填充色带来的暗度——alpha=0，槽位背景不再叠加任何灰/黑色调，图标
    // 在正常（不被压暗）的底子上显示。
    private static readonly Color LighterFillColor = new Color(0.5f, 0.5f, 0.5f, 0f);
    private const float CornerSize = 5f;
    private static readonly Color CornerColor = new Color(1f, 1f, 1f, 0.55f); // 白色，降低存在感
    private const float EdgeLineWidth = 1.5f;
    private const float EdgeLineMargin = CornerSize + 4f; // 上下各让开这么多，不碰到角标记
    private static readonly Color EdgeLineColor = new Color(1f, 1f, 1f, 0.25f);

    // 两份材质都放在 Resources 底下——QuickSlotUseController 是运行时脚本，冷却切灰/切回彩色
    // 靠它在运行时 Resources.Load 这两个材质来"换引用"（见该脚本注释），不能用 AssetDatabase
    // （Editor-only，打包后没有）。
    private const string HologramMaterialPath = "Assets/_Project/Resources/UI/HUD/M_QuickSlotHologramIcon.mat";
    private const string HologramGrayscaleMaterialPath = "Assets/_Project/Resources/UI/HUD/M_QuickSlotHologramIconGrayscale.mat";
    public const string HologramMaterialResourcesPath = "UI/HUD/M_QuickSlotHologramIcon";
    public const string HologramGrayscaleMaterialResourcesPath = "UI/HUD/M_QuickSlotHologramIconGrayscale";
    private const string HologramShaderName = "UI/SkyPrison/HologramIcon";

    [MenuItem("SkyPrison/HUD/Patch QuickSlots Corners + Lighter Fill")]
    public static void Patch()
    {
        Material hologramMaterial = GetOrCreateHologramMaterial(HologramMaterialPath, "M_QuickSlotHologramIcon", desaturate: false);
        // 返回值本身在这里用不到——调这个方法是为了保证 Resources 底下这份灰度材质资产
        // 存在/参数是最新的，QuickSlotUseController 运行时自己 Resources.Load 出来用。
        GetOrCreateHologramMaterial(HologramGrayscaleMaterialPath, "M_QuickSlotHologramIconGrayscale", desaturate: true);
        TMP_FontAsset countdownFont = FindExistingHudFont(PrefabPaths);
        int patchedPrefabs = 0;
        foreach (string path in PrefabPaths)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                Debug.LogWarning($"[PatchQuickSlotsHUDCorners] 没找到 prefab：{path}");
                continue;
            }

            Transform quickSlotsArea = FindDeepChild(root.transform, "QuickSlotsArea");
            if (quickSlotsArea == null)
                Debug.LogWarning($"[PatchQuickSlotsHUDCorners] {path} 里没找到 QuickSlotsArea");

            var iconRefs = new Image[4];
            var cooldownFillRefs = new Image[4];

            bool changed = false;
            for (int i = 1; i <= 4; i++)
            {
                Transform slot = FindDeepChild(root.transform, $"Slot_{i:00}");
                if (slot == null)
                {
                    Debug.LogWarning($"[PatchQuickSlotsHUDCorners] {path} 里没找到 Slot_{i:00}");
                    continue;
                }
                var slotRt = slot as RectTransform;

                // 原来的 "Icon" 留在 Slot_XX 底下，会被色收差RT捕获吃掉亮度（同一个
                // luminance-derived-alpha 问题，见下面 CleanBG_ 的注释）——图标本身也
                // 是亮色内容，被RT捕获之后显得发暗、发闷，跟"全息投影"想要的通透、发亮
                // 效果正好相反。这里直接禁用旧 Icon（不再使用，只留着避免破坏引用），
                // 图标改成在下面新建一个不进RT捕获的 CleanFG_Icon_XX。
                var iconTf = slot.Find("Icon");
                if (iconTf != null)
                {
                    var staleIcon = iconTf.GetComponent<Image>();
                    if (staleIcon != null) staleIcon.enabled = false;
                }

                // 槽位自身的背景 Image 会被色收差 RT 捕获——查过 shader 源码
                // (SkyPrisonHUDModulePostProcess.shader)，它的透明度是从RGB亮度反推的，
                // 不读真实alpha通道（URP17截屏RT背景alpha恒为1，没法用alpha区分背景/
                // 内容，只能用亮度当替代信号）。这意味着任何进了这个RT的内容，无论alpha
                // 设多少，亮色都会读成不透明，没法在色收差捕获范围内做出真正的半透明。
                // 所以把背景整个搬出去，做成一个不进RT捕获、走普通UI透明度混合的兄弟节点
                // （命名 CleanBG_，跟 QuickItemPromptOverlay 同一套排除机制，见
                // SkyPrisonHUDModulePostProcessRenderer.IsQuickPromptObjectName）。
                var slotBg = slot.GetComponent<Image>();
                if (slotBg != null) slotBg.enabled = false;

                // 早期版本把角标记/边框线直接挂在 slot 自己身上——清掉这些遗留物，
                // 免得跟新建在 CleanBG_ 下面的那份重复显示。
                foreach (var staleName in new[] { "Corner_TL", "Corner_TR", "Corner_BL", "Corner_BR", "EdgeLine_L", "EdgeLine_R" })
                {
                    var stale = slot.Find(staleName);
                    if (stale != null) Object.DestroyImmediate(stale.gameObject);
                }

                // 之前跑过一版有缩放换算bug（见下面注释），算出来的尺寸偏大——已存在的
                // CleanBG 直接删掉重建，不能只刷新颜色不刷新尺寸，不然错误的尺寸会一直留着。
                Transform existingBg = quickSlotsArea != null ? quickSlotsArea.Find($"CleanBG_{i:00}") : null;
                if (existingBg != null) Object.DestroyImmediate(existingBg.gameObject);

                var contentParent = slotRt != null ? slotRt.parent as RectTransform : null;
                if (quickSlotsArea != null && slotRt != null && contentParent != null)
                {
                    // 前几版都在用各种API去"测量"跨层级的矩形，一直翻车（0尺寸、0缩放……）。
                    // 直接查了 Slot_XX 的父物体 QuickSlotsContent 的锚点：anchorMin=(0,0)，
                    // anchorMax=(1,1)，anchoredPosition=(0,0)，sizeDelta=(0,0)——也就是
                    // QuickSlotsContent 相对 QuickSlotsArea 是零偏移、四边拉伸贴满的，
                    // 两者本地坐标系完全重合。既然如此就不需要跨层级换算——Slot_XX 自己的
                    // anchorMin/anchorMax/pivot/anchoredPosition/sizeDelta 原样搬到
                    // QuickSlotsArea 下面的新物体上就是对的，纯数值拷贝，不调用任何依赖
                    // 场景激活状态的测量API。
                    var bgGo = new GameObject($"CleanBG_{i:00}", typeof(RectTransform), typeof(Image));
                    var bgRt = (RectTransform)bgGo.transform;
                    bgRt.SetParent(quickSlotsArea, false);
                    bgRt.localScale = Vector3.one;
                    bgRt.anchorMin = slotRt.anchorMin;
                    bgRt.anchorMax = slotRt.anchorMax;
                    bgRt.pivot = slotRt.pivot;
                    bgRt.anchoredPosition = slotRt.anchoredPosition;
                    bgRt.sizeDelta = slotRt.sizeDelta;

                    var bgImg = bgGo.GetComponent<Image>();
                    bgImg.color = LighterFillColor;
                    bgImg.raycastTarget = false;
                    // alpha=0已经等于看不见了，但物体还在意味着还是有一块Image在合批/占位——
                    // 直接把这个填充Image禁用掉，彻底不参与渲染，图标底下就是纯背景，不再
                    // 有任何暗色调子。四个角标记/边框线还是这个物体的子物体，不受影响。
                    bgImg.enabled = false;

                    // 角标记 + 边框线也一起挪到 CleanBG_ 底下（子物体会跟着父物体一起被
                    // 排除出色收差RT捕获，见 IsQuickPromptOverlayOrChild 会往上找祖先）。
                    // 之前把它们留在 slot 自己身上、还在RT捕获范围内，色收差在1.5px的细线
                    // 上几乎把白色整个替换成红/蓝两个偏移通道，纯白线看着就是彩色——挪出来
                    // 之后才是真正不受色收差影响的纯白。
                    AddCorner(bgRt, "Corner_TL", new Vector2(0f, 1f));
                    AddCorner(bgRt, "Corner_TR", new Vector2(1f, 1f));
                    AddCorner(bgRt, "Corner_BL", new Vector2(0f, 0f));
                    AddCorner(bgRt, "Corner_BR", new Vector2(1f, 0f));
                    AddEdgeLine(bgRt, "EdgeLine_L", 0f);
                    AddEdgeLine(bgRt, "EdgeLine_R", 1f);

                    // 图标 + 柔光 + 冷却遮罩都不能留在 Slot_XX 底下（会被RT捕获吃掉亮度），
                    // 但也不能各自建一个独立的 CleanFG_ 顶层物体——渲染器每帧会把所有
                    // CleanFG_ 前缀物体强制"排到最后一层"，如果同一个槽位下有两个独立的
                    // CleanFG_ 物体，它们俩谁真正在最后由处理顺序决定，帧与帧之间不稳定，
                    // 会互相打架导致闪烁。所以这里只建一个 CleanFG_Slot_XX，三层内容
                    // （柔光→图标→冷却遮罩，按绘制顺序）都塞在它自己底下，物体内部子物体
                    // 顺序固定不受RT渲染器排序逻辑影响，不会闪。
                    // 之前几版迭代分别叫过 CleanFG_Icon_XX / CleanFG_Cooldown_XX——名字改了
                    // 但旧物体从没删过，一直烤在 prefab 里。这些遗留物体也是 CleanFG_ 前缀，
                    // 一样会被渲染器强制排到最后一层，跟新建的 CleanFG_Slot_XX 抢位置，
                    // 就是"图层抽搐"的真正原因。这里把所有历史命名都清一遍。
                    foreach (var staleFgName in new[] { $"CleanFG_Slot_{i:00}", $"CleanFG_Icon_{i:00}", $"CleanFG_Cooldown_{i:00}" })
                    {
                        Transform staleFg = quickSlotsArea.Find(staleFgName);
                        if (staleFg != null) Object.DestroyImmediate(staleFg.gameObject);
                    }

                    var fgGo = new GameObject($"CleanFG_Slot_{i:00}", typeof(RectTransform), typeof(RectMask2D));
                    var fgRt = (RectTransform)fgGo.transform;
                    fgRt.SetParent(quickSlotsArea, false);
                    fgRt.localScale = Vector3.one;
                    fgRt.anchorMin = slotRt.anchorMin;
                    fgRt.anchorMax = slotRt.anchorMax;
                    fgRt.pivot = slotRt.pivot;
                    fgRt.anchoredPosition = slotRt.anchoredPosition;
                    fgRt.sizeDelta = slotRt.sizeDelta;
                    // 图标故意比槽位本身更大（撑满、不完整显示），靠这个 RectMask2D 把溢出
                    // 槽位边界的部分裁掉，避免图标视觉上盖到相邻槽位上。
                    fgGo.GetComponent<RectMask2D>().padding = Vector4.zero;

                    iconRefs[i - 1] = AddHologramIcon(fgRt, hologramMaterial);
                    cooldownFillRefs[i - 1] = AddCooldownFill(fgRt);
                    AddCooldownText(fgRt, countdownFont);
                    AddCountText(fgRt, countdownFont);
                }

                changed = true;
            }

            if (changed)
            {
                var view = root.GetComponentInChildren<SkyPrisonPlayerHUDView_V4_StyleDriven>(true);
                if (view != null)
                {
                    Debug.Log($"[PatchQuickSlotsHUDCorners] {path}：iconRefs=[{string.Join(",", System.Array.ConvertAll(iconRefs, x => x != null ? x.name : "null"))}] cooldownFillRefs=[{string.Join(",", System.Array.ConvertAll(cooldownFillRefs, x => x != null ? x.name : "null"))}]");
                    var so = new SerializedObject(view);
                    bool okIcons = ApplyImageArray(so, "quickSlotIcons", iconRefs);
                    bool okFills = ApplyImageArray(so, "quickSlotCooldownFills", cooldownFillRefs);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    if (!okIcons) Debug.LogWarning($"[PatchQuickSlotsHUDCorners] {path}：没找到 quickSlotIcons 属性，SerializedObject.FindProperty 失败。");
                    if (!okFills) Debug.LogWarning($"[PatchQuickSlotsHUDCorners] {path}：没找到 quickSlotCooldownFills 属性，SerializedObject.FindProperty 失败。");
                }
                else
                {
                    Debug.LogWarning($"[PatchQuickSlotsHUDCorners] {path} 里没找到 SkyPrisonPlayerHUDView_V4_StyleDriven，quickSlotIcons/quickSlotCooldownFills 没能自动接线。");
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                patchedPrefabs++;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[PatchQuickSlotsHUDCorners] 完成，patch了 {patchedPrefabs} 份 prefab。");
    }

    private static void AddCorner(Transform parent, string name, Vector2 anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(CornerSize, CornerSize);
        var img = go.GetComponent<Image>();
        img.color = CornerColor;
        img.raycastTarget = false;
    }

    private static void AddEdgeLine(Transform parent, string name, float xAnchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(xAnchor, 0f);
        rt.anchorMax = new Vector2(xAnchor, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.offsetMin = new Vector2(-EdgeLineWidth * 0.5f, EdgeLineMargin);
        rt.offsetMax = new Vector2(EdgeLineWidth * 0.5f, -EdgeLineMargin);
        var img = go.GetComponent<Image>();
        img.color = EdgeLineColor;
        img.raycastTarget = false;
    }

    // 冷却遮罩：铺满整个槽位，从下往上收（Vertical fill）——fillAmount 由
    // QuickSlotUseController 每帧写，1=刚用/满冷却，0=冷却结束（Filled Image
    // fillAmount=0 时本来就不画东西，不用额外处理"隐藏"）。冷白色调试过（配合黑色文字
    // 描边）想读出"冷却中"而不是"故障"，但描边在TMP这套材质上死活不生效——改用暗色
    // 填充，白色数字压在暗底上天然就有对比度，不用靠描边。深色遮罩本身只在真正冷却时
    // 才会出现，不会常驻，不会被误读成"图标坏了"。
    private static readonly Color CooldownFillColor = new Color(0.05f, 0.07f, 0.09f, 0.62f);

    private static Image AddCooldownFill(RectTransform parent)
    {
        var go = new GameObject("CooldownFill", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.color = CooldownFillColor;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Vertical;
        img.fillOrigin = (int)Image.OriginVertical.Bottom;
        img.fillAmount = 0f;
        img.raycastTarget = false;
        img.enabled = false; // 默认关闭——QuickSlotUseController 每帧按是否冷却显式开关，不靠fillAmount=0隐身
        return img;
    }

    // 冷却读秒文字：跟背包菜单"使用（3.2s）"同一套 F1（一位小数）格式，QuickSlotUseController
    // 每帧写 text/enabled。建在 CooldownFill 之后（绘制顺序更靠后，盖在遮罩上面能看清）。
    private static void AddCooldownText(RectTransform parent, TMP_FontAsset font)
    {
        var go = new GameObject("CooldownText", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.text = "";
        text.fontSize = 22f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        text.enabled = false; // 没冷却时不显示，QuickSlotUseController 冷却时打开
        ApplyBlackOutline(text);
        go.transform.SetAsLastSibling();
    }

    // 数量角标：右下角显示"×N"，跟背包格子 InventorySlotView 同一套格式/位置，N 是
    // 这个道具类型在背包里的总数量（不是某一个具体堆叠条目的数量）。
    private static void AddCountText(RectTransform parent, TMP_FontAsset font)
    {
        var go = new GameObject("CountText", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.text = "";
        text.fontSize = 30f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.BottomRight;
        text.color = Color.white;
        text.richText = true; // × 号缩小下沉跟数字对齐要用富文本标签，见 QuickSlotUseController
        text.raycastTarget = false;
        text.margin = new Vector4(0f, 0f, 8f, 4f); // 右边距=离右边缘留点空间，往左挪一点
        text.enabled = false; // 没道具时不显示，QuickSlotRuntime.PushToHud 有道具会打开
        ApplyBlackOutline(text);
        // 显式排到最后一层——CooldownFill 的半透明冷白遮罩要是盖在数字上面，数字会跟着
        // 一起被那层浅色冲淡，看着发白、糊在一起。
        go.transform.SetAsLastSibling();
    }

    // 黑色描边——冷却读秒/数量角标叠在图标（亮度、颜色都不固定）上面，纯白字容易跟浅色
    // 图标糊在一起看不清，加一圈黑边保证任何背景下都能读。
    // 之前用 text.outlineWidth/outlineColor 这两个属性设置没生效——这两个setter在部分
    // TMP版本里改的是共享材质引用而不是稳定生效的实例，行为不可靠。这里改成显式 new 一份
    // 材质实例、直接用shader属性名写（_OutlineWidth/_OutlineColor/_OutlineSoftness），
    // 不依赖 TMP_Text 那两个属性的具体实现。
    private static void ApplyBlackOutline(TextMeshProUGUI text)
    {
        if (text.fontSharedMaterial == null) return;
        var mat = new Material(text.fontSharedMaterial);
        if (!mat.HasProperty("_OutlineWidth"))
        {
            Debug.LogWarning($"[PatchQuickSlotsHUDCorners] 字体材质「{mat.shader.name}」没有 _OutlineWidth 属性（可能是Bitmap字体，不支持描边），{text.name} 描边跳过。");
            return;
        }
        mat.SetFloat("_OutlineWidth", 0.2f);
        mat.SetColor("_OutlineColor", Color.black);
        if (mat.HasProperty("_OutlineSoftness")) mat.SetFloat("_OutlineSoftness", 0.05f);
        text.fontSharedMaterial = mat;
    }

    // 全息投影观感的图标：靠 HologramIcon Shader 本身在图标轮廓内做辉光/扫描线/闪烁
    // （见 SkyPrisonUIHologramIcon.shader），不再需要额外叠一层"柔光"Image——那是纯色
    // 矩形，没有径向渐变，边缘是硬直角，看着像凭空多出来一块方块，反而不像全息。
    // 要求图标"放大到不用完整显示"——尺寸故意比槽位（150x96）本身还大，靠 CleanFG_Slot_XX
    // 上加的 RectMask2D 把溢出部分裁掉，效果是图标撑满整个槽位、边缘可能被裁切，而不是
    // 完整缩小塞进框里。建在 CleanFG_Slot_XX 底下，不进色收差RT捕获，保证颜色是真实、
    // 不发暗的。
    private static readonly Color HologramIconTint = new Color(0.94f, 0.97f, 1f, 1f);
    private const float HologramIconSize = 132f;

    private static Image AddHologramIcon(RectTransform parent, Material hologramMaterial)
    {
        var iconGo = new GameObject("IconImage", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(parent, false);
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.anchorMin = new Vector2(0.5f, 0.5f);
        iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.anchoredPosition = Vector2.zero;
        iconRt.sizeDelta = new Vector2(HologramIconSize, HologramIconSize);
        var iconImg = iconGo.GetComponent<Image>();
        iconImg.color = HologramIconTint;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        iconImg.enabled = false; // 默认没道具时隐藏，QuickSlotRuntime.PushToHud 有道具会打开
        if (hologramMaterial != null) iconImg.material = hologramMaterial;
        return iconImg;
    }

    // 全息效果不再靠"贴一层半透明颜色"假装，改用真正的Shader（UI/SkyPrison/HologramIcon）：
    // 扫描线扫过 + 沿图标轮廓的边缘辉光 + 轻微闪烁，都是逐像素在shader里算的。材质存成
    // 项目里的固定资产（不是每次patch都new一个孤立对象），方便以后在Inspector里调参数。
    // 彩色/灰度两份材质分开存盘（不是同一份改参数），因为四个槽位共用材质引用——冷却时
    // 只想让"正在冷却的那一个"变灰，得靠 QuickSlotUseController 在运行时切换材质引用，
    // 不能靠改共享材质的参数（那样会一次性影响所有槽位）。
    private static Material GetOrCreateHologramMaterial(string path, string assetName, bool desaturate)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find(HologramShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[PatchQuickSlotsHUDCorners] 没找到 Shader「{HologramShaderName}」，图标全息效果会退回默认UI材质。");
                return null;
            }

            mat = new Material(shader) { name = assetName };
            string dir = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
                Debug.LogWarning($"[PatchQuickSlotsHUDCorners] 目录不存在：{dir}，请先手动创建。");
            AssetDatabase.CreateAsset(mat, path);
        }

        // .mat 一旦存过一次盘，shader里改的 Properties 默认值不会反向同步过去（已有值已经
        // 序列化在文件里）——调参数阶段这几个关键值每次patch都强制刷新，跟shader里的
        // 默认值保持一致，不用每次手动去Inspector里重设。
        if (mat.HasProperty("_Brightness")) mat.SetFloat("_Brightness", 1.15f);
        if (mat.HasProperty("_InnerGlowAmount")) mat.SetFloat("_InnerGlowAmount", 0.1f);
        if (mat.HasProperty("_Desaturate")) mat.SetFloat("_Desaturate", desaturate ? 1f : 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // 冷却倒计时文字的字体：不用HUD里那些专门指定的特殊字体（比如槽位编号用的"东亚重工"），
    // 走项目里其它地方统一在用的"字典表"默认字体——SkyPrisonUIGlobalStyleSettings_V1.
    // defaultTextFont，从背包prefab上直接读这个组件（Editor/CreatePlayerDeathRevivePrefab.cs
    // 里就是这个套路：读的是prefab资产上的组件，不是FindObjectOfType，Editor下不依赖场景
    // 是否打开）。
    private const string InventoryPrefabPathForFont = "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab";

    private static TMP_FontAsset FindExistingHudFont(string[] prefabPaths)
    {
        var inv = AssetDatabase.LoadAssetAtPath<GameObject>(InventoryPrefabPathForFont);
        var style = inv != null ? inv.GetComponent<SkyPrison.Runtime.UI.SkyPrisonUIGlobalStyleSettings_V1>() : null;
        if (style != null && style.defaultTextFont != null) return style.defaultTextFont;

        Debug.LogWarning("[PatchQuickSlotsHUDCorners] 没找到默认字体（SkyPrisonUIGlobalStyleSettings_V1.defaultTextFont），冷却倒计时文字会用TMP内置默认字体。");
        return null;
    }

    private static bool ApplyImageArray(SerializedObject so, string fieldName, Image[] values)
    {
        var prop = so.FindProperty(fieldName);
        if (prop == null || !prop.isArray) return false;
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        return true;
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        var queue = new Queue<Transform>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) queue.Enqueue(t.GetChild(i));
        }
        return null;
    }
}
