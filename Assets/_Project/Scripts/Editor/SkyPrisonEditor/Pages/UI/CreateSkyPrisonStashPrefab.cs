#if UNITY_EDITOR
using System.Collections.Generic;
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 创建 / 恢复 PF_SkyPrisonStash prefab（仓库窗口）。
    ///
    /// 2026-07-21 五次修订：前四轮反复对不上尺寸，根本原因找到了——项目里早就有一个
    /// 专门为了防止"每个窗口各自抄一遍背包的数字，抄着抄着就分家"这件事而建的共享工具
    /// <see cref="SkyPrisonFloatingWindowKit"/>（连类文档都是这么写的），之前我完全没用
    /// 它，自己从零手搭了一遍角标/标题栏/关闭按钮/模糊背景，每次手抄背包的数字都可能
    /// 抄错或者漏看一个环节（这次还额外挖出背包身上有个 SkyPrisonInventoryPanelScaler
    /// 脚本，运行时把面板整体再放大1.3倍——这个缩放只在Play模式才生效，读静态prefab
    /// 资产完全看不出来，之前几轮"现读背包数值"都被这个坑漏掉了）。
    ///
    /// 现在窗口的通用外壳(黑白模糊背景/四角白色角标/标题栏+拖动/关闭按钮/正文字号)
    /// 全部直接调用 SkyPrisonFloatingWindowKit 的共享方法，不再自己重新实现一遍——
    /// 这些方法内部已经把 1.3 倍这件事处理掉了(StandardScaleMultiplier)，跟背包
    /// 和角色信息面板这些其它窗口保证100%一致，不会再单独跑偏。
    ///
    /// 格子/页签/排序整理条是背包/kit都没有覆盖到的"仓库专属"部分：格子尺寸/间距/
    /// 颜色/排序整理按钮宽度现读自背包真实节点，再乘上同一个 StandardScaleMultiplier
    /// 换算成实际渲染大小；页签(1/2/3/4)缩小到原尺寸1/3、底部排序条布局是仓库独立
    /// 设计的，不涉及跟背包的对照问题。
    /// </summary>
    public static class CreateSkyPrisonStashPrefab
    {
        public const string PrefabPath    = "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonStash.prefab";
        public const string ResMirrorPath = "Assets/Resources/UI/Window/PF_SkyPrisonStash.prefab";
        private const string InventoryPrefabPath = "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab";

        private const int Columns   = StashRuntime.DefaultColumns; // 5
        private const int CellCount = StashRuntime.SlotsPerPage;   // 100

        // 本窗口独有、不需要跟背包对照的布局常量（不乘 StandardScaleMultiplier——
        // 这些数字本来就是直接对着4K画布定的，跟背包的"设计值需要换算成实际渲染值"
        // 是两回事）。
        // 页签改成"贴左边框+横向拉长"之后，原本单独留白的 LeftMargin(40) 直接并进
        // TabColumnW 里（47+40=87），面板总宽度算式不用跟着变——页签左边直接是面板
        // 左边缘，右边界位置跟以前一样，只是页签本身占的横向空间变宽了。
        private const float LeftMargin    = 0f;
        private const float TabColumnW    = 130f; // 原47 + 并入的LeftMargin(40) + 用户两次要求"再大一些"累加
        private const float TabHeight     = 80f;  // 原53，同样两次"再大一些"累加
        private const float TabGap        = 24f;  // 原7，用户两次要求"间距大一点"累加
        private const float TabTopInset   = 20f;  // 页签整体比网格区顶部再往下挪一点，用户要求
        private const float GapTabToGrid  = 12f;  // 原20，为页签变宽腾一点空间，一起收紧
        private const string LockIconAssetPath = "Assets/_Project/UIUX/Window/Styles/Default/Sprites/UIWindow_Default_Lock.png";
        private const float LockIconSize = 48f; // 跟着页签一起放大一点，原40
        // 右边距不再单独定义——网格区/整理条右边直接贴面板边缘，"离右边框多远"完全靠
        // 背包现读的 viewportInset/contentMargin/TidyButtonRightInset 这几个真实值决定，
        // 不能再额外叠加一层仓库自己发明的右边距(之前就是这样才对不上背包)。
        private const float BottomBarGapToGrid    = 20f;
        private const float FilterBarHeight       = 48f; // 现读自背包 FilterBar，乘 mul 换算
        private const float FilterBarGapToContent = 20f; // 排序行下方到页签/网格区之间的间隔
        private const int   MinVisibleRows        = 6;   // 用户要求：面板高度至少要能看见6行格子

        // 整理条——之前这两个值(90/30)是我自己瞎定的"仓库专属"数字，跟背包完全没对照，
        // 导致整理按钮框的高宽比例跟背包对不上(明显偏高/偏方)。现读自背包真实的底部
        // 容器：容器高度52(不是90)、贴着面板底边零边距(不是留30空白)，TidyButton 自己
        // 在容器内上下各收进7(sizeDelta.y=-14)，不是撑满整个容器高度。
        private const float TidyBarHeight        = 52f;
        private const float TidyBarMarginBottom  = 0f;
        private const float TidyButtonHeightInset = 14f;
        // 容器本身贴着面板左右边缘零边距(不是留 LeftMargin/RightMargin 空白)，
        // TidyButton 自己在容器内右边收进8——两层叠加才是背包真实的"离右下角多远"。
        private const float TidyButtonRightInset = 8f;

        // 排序行(排序标签+下拉框+升降序按钮)——现读自背包同一行元素的真实相对位置，
        // 背包这几个元素紧贴在筛选标签栏正下方(零间隙)、且是从左边开始固定宽度摆放，
        // 不是拉伸铺满整行(行右边大片留白，背包截图能直接看到)。之前误以为背包这几个
        // 元素在底部、且下拉框是拉伸到剩余宽度的，两个假设都错了，这次照抄真实数值。
        private const float SortRowHeight          = 40f;
        private const float SortLabelOffsetX       = 12f;
        private const float SortLabelWidth         = 40f;
        private const float SortDropdownOffsetX    = 54f;
        private const float SortDropdownWidth      = 140f;
        private const float SortOrderButtonOffsetX = 202f;
        private const float SortLabelFontSize      = 19f;   // 现读自背包 SortLabel 真实 m_FontSize
        private const float SortOrderArrowFontSize = 21f;   // 现读自背包 SortOrderButton 箭头真实 m_FontSize（之前瞎写的36）
        private static readonly Color SortOrderArrowColor = new Color(0.8f, 0.8f, 0.82f, 1f); // 现读自背包真实颜色
        private const float TidyLabelFontSize      = 20f;   // 现读自背包 TidyButton 真实 m_FontSize（之前瞎写的32）
        // 筛选标签——之前用的21/纯白都是没核对过的猜测值，现读自背包 InventoryWindowController
        // 这个组件在真实 prefab 上的序列化值（18/{0.66,0.66,0.68,1}），不是它 C# 字段声明
        // 里写的默认值（那两个默认值从来没跟这份 prefab 实际用的值对齐过）。
        private const float FilterTabFontSize = 18f;
        private static readonly Color FilterTabNormalColor = new Color(0.66f, 0.66f, 0.68f, 1f);

        // 背包所有旧版 Text（排序标签/整理按钮/升降序箭头）用的都是这个字体资产，不是
        // Unity 内置默认字体——之前用内置字体，字体本身就跟背包不一样。
        private const string LegacyFontAssetPath = "Assets/_Project/UIUX/Fonts/ZhouFangRiMingTi-2.otf";

        // 本地化：仓库要复用背包同一套 SkyPrisonInventoryTextLocalizer + UILocalizationTable，
        // 筛选标签/排序/整理这些文案背包已经有对应 key，直接复用；标题"仓库"背包没有对应
        // key，需要现建一个。
        private const string LocalizationSettingsPath = "Assets/_Project/Data/ProjectSettings/LocalizationProjectSettings.asset";
        private const string LocalizationTablePath     = "Assets/_Project/Data/Resources/UILocalizationTable.asset";
        private const string StashTitleLocKey = "ui_stash_title";

        // ── 从背包 prefab 现读的真实数值（都是背包的"设计值"，还没乘1.3倍）─────────
        private struct InventoryReference
        {
            public float CellSize;
            public float CellGap;
            public Color CellColor;
            public float ViewportSideInset;
            public float ContentSideMargin;
            public float SortOrderButtonWidth;
            public float TidyButtonWidth;
            public bool  Valid;
        }

        private static InventoryReference ReadInventoryReference()
        {
            var result = new InventoryReference();
            var inv = AssetDatabase.LoadAssetAtPath<GameObject>(InventoryPrefabPath);
            if (inv == null)
            {
                Debug.LogError("[Stash] 找不到背包 prefab，没法现读真实尺寸。");
                return result;
            }

            Transform panel = FindDeep(inv.transform, "InventoryPanel");
            Transform slot0 = FindDeep(inv.transform, "Slot_00");
            Transform slot1 = FindDeep(inv.transform, "Slot_01");
            Transform sortOrderButton = FindDeep(inv.transform, "SortOrderButton");
            Transform tidyButton = FindDeep(inv.transform, "TidyButton");

            if (panel == null || slot0 == null || slot1 == null || sortOrderButton == null || tidyButton == null)
            {
                Debug.LogError("[Stash] 背包 prefab 里少了某个预期节点(InventoryPanel/Slot_00/" +
                    "Slot_01/SortOrderButton/TidyButton)，背包结构可能改过，现读逻辑需要跟着更新对应的节点名。");
                return result;
            }

            var panelRT = (RectTransform)panel;

            var slot0RT = (RectTransform)slot0;
            var slot1RT = (RectTransform)slot1;
            result.CellSize = slot0RT.sizeDelta.x;
            result.CellGap = slot1RT.anchoredPosition.x - slot0RT.anchoredPosition.x - result.CellSize;
            Image slot0Img = slot0.GetComponent<Image>();
            if (slot0Img != null) result.CellColor = slot0Img.color;

            // Viewport 是 GridContent 的父节点，直接挂在 InventoryPanel 下、左右对称内缩。
            Transform gridContent = slot0.parent;
            Transform viewport = gridContent.parent;
            var viewportRT = (RectTransform)viewport;
            var contentRT = (RectTransform)gridContent;
            // Unity 的 offsetMin = anchoredPosition - sizeDelta*pivot；这个节点是对称内缩的
            // 拉伸矩形(anchoredPosition.x=0, sizeDelta.x=-16, pivot.x=0.5)，算出来
            // offsetMin.x = 0-(-16*0.5) = +8，这本身就是"内缩了多少"，不需要再取负号。
            result.ViewportSideInset = viewportRT.offsetMin.x;
            float viewportWidth = panelRT.sizeDelta.x + viewportRT.sizeDelta.x; // sizeDelta是负的内缩总量
            result.ContentSideMargin = (viewportWidth - contentRT.sizeDelta.x) * 0.5f;

            result.SortOrderButtonWidth = ((RectTransform)sortOrderButton).sizeDelta.x;
            result.TidyButtonWidth = ((RectTransform)tidyButton).sizeDelta.x;

            result.Valid = true;
            return result;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        // 背包所有旧版 Text（排序标签/整理按钮/升降序箭头）用的都是这份字体资产——之前
        // 用 Unity 内置默认字体，字体本身就跟背包对不上，不只是字号/颜色的问题。
        private static Font LoadLegacyFont() => AssetDatabase.LoadAssetAtPath<Font>(LegacyFontAssetPath);

        // "仓库"这个标题背包没有对应的本地化 key（背包自己是"背包"/ui_inventory_title），
        // 现建一个 key，跟背包其它文案共用同一张 UILocalizationTable，而不是自己另起
        // 一张表——这样以后要出多语言，改这一张表就够了。
        private static void EnsureStashTitleLocKey(UILocalizationTable table)
        {
            if (table == null) return;
            var entry = table.EnsureEntry(StashTitleLocKey, new List<string> { "zh-CN", "ja", "en" });
            SetLangText(entry, "zh-CN", "仓库");
            SetLangText(entry, "ja", "倉庫");
            SetLangText(entry, "en", "Stash");
            EditorUtility.SetDirty(table);
        }

        // 提示条"转移物品"/"操作"/"切换焦点"这几条之前是写死的中文，没查表——用户
        // 截图实测出日/英环境下这几条还是汉语。跟标题同一个套路补上对应翻译。
        private static void EnsureHintLocKeys(UILocalizationTable table)
        {
            if (table == null) return;

            var transfer = table.EnsureEntry("ui_hint_transfer_item", new List<string> { "zh-CN", "ja", "en" });
            SetLangText(transfer, "zh-CN", "转移物品");
            SetLangText(transfer, "ja", "アイテム移動");
            SetLangText(transfer, "en", "Transfer Item");

            var operate = table.EnsureEntry("ui_hint_operate", new List<string> { "zh-CN", "ja", "en" });
            SetLangText(operate, "zh-CN", "操作");
            SetLangText(operate, "ja", "操作");
            SetLangText(operate, "en", "Actions");

            var switchWin = table.EnsureEntry("ui_hint_switch_window", new List<string> { "zh-CN", "ja", "en" });
            SetLangText(switchWin, "zh-CN", "切换焦点");
            SetLangText(switchWin, "ja", "フォーカス切替");
            SetLangText(switchWin, "en", "Switch Focus");

            EditorUtility.SetDirty(table);
        }

        private static void SetLangText(UILocalizationEntry entry, string languageCode, string text)
        {
            foreach (var t in entry.texts)
            {
                if (t.languageCode != languageCode) continue;
                if (string.IsNullOrEmpty(t.text)) t.text = text; // 只填空的，不覆盖已有翻译
                return;
            }
        }

        [MenuItem("Tools/Sky Prison/UI/Create Stash Window")]
        public static void Create()
        {
            InventoryReference refData = ReadInventoryReference();
            if (!refData.Valid)
            {
                Debug.LogError("[Stash] 现读背包真实尺寸失败，已取消生成，避免用错误/占位数值建出跟背包对不上的窗口。");
                return;
            }

            var root = new GameObject("PF_SkyPrisonStash");
            var rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero; rootRT.anchorMax = Vector2.one;
            rootRT.sizeDelta = Vector2.zero; rootRT.anchoredPosition = Vector2.zero;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1100;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.matchWidthOrHeight  = 0.5f;

            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            root.AddComponent<SkyPrisonUIGlobalStyleSettings_V1>();

            var meta = root.AddComponent<SkyPrisonUIPrefabMetadata_V1>();
            meta.uiId              = "stash";
            meta.displayName       = "仓库";
            meta.kind              = SkyPrisonUIPrefabKindV1.Window;
            meta.defaultVisible    = false;
            meta.sortingOrder      = 1100;
            meta.blocksRaycasts    = true;
            meta.lockGameplayInput = false; // 跟背包一致：不挡移动/奔跑/跳跃，只挡攻击/闪避
            meta.showMouseCursor   = true;
            meta.inputModeWhenOpen = SkyPrisonUIInputModeV1.Gameplay;
            meta.closeOnEscape     = true;
            meta.referenceResolution = new Vector2(3840f, 2160f);

            TMP_FontAsset font = SkyPrisonFloatingWindowKit.LoadTMPFont("ZhouFangRiMingTi-2 SDF");
            Font legacyFont = LoadLegacyFont();
            CopyStyleFromInventory(root.GetComponent<SkyPrisonUIGlobalStyleSettings_V1>(), font);

            var localizationSettings = AssetDatabase.LoadAssetAtPath<LocalizationProjectSettings>(LocalizationSettingsPath);
            var localizationTable    = AssetDatabase.LoadAssetAtPath<UILocalizationTable>(LocalizationTablePath);
            if (localizationTable != null) { EnsureStashTitleLocKey(localizationTable); EnsureHintLocKeys(localizationTable); }
            else Debug.LogWarning("[Stash] 找不到 UILocalizationTable，仓库文字不会接入字典表，会保留硬编码中文兜底。");

            BuiltPanelRefs refs = BuildContents(rootRT, refData, font, legacyFont, localizationSettings, localizationTable);

            var controller = root.AddComponent<StashWindowController>();
            WireController(controller, refs);

            EnsureFolder("Assets/_Project/Prefabs/UI/Window");
            EnsureFolder("Assets/Resources/UI/Window");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            if (ok)
            {
                if (AssetDatabase.CopyAsset(PrefabPath, ResMirrorPath))
                    AssetDatabase.ImportAsset(ResMirrorPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[Stash] Prefab 已创建：{PrefabPath}");
            }
            else
            {
                Debug.LogError("[Stash] Prefab 保存失败！");
            }

            Object.DestroyImmediate(root);
        }

        // ── 主结构 ────────────────────────────────────────────────────────────

        private struct BuiltPanelRefs
        {
            public Button        CloseButton;
            public Transform     TabContainer;
            public StashGridView GridView;
            public TMP_FontAsset TabFont;
        }

        private static BuiltPanelRefs BuildContents(RectTransform root, InventoryReference r, TMP_FontAsset font,
            Font legacyFont, LocalizationProjectSettings localizationSettings, UILocalizationTable localizationTable)
        {
            float mul = SkyPrisonFloatingWindowKit.StandardScaleMultiplier;

            // 格子/留白全部现读自背包的"设计值"，乘上跟背包完全相同的倍率才是实际渲染
            // 大小——背包身上挂着 SkyPrisonInventoryPanelScaler，运行时把整个面板放大
            // 1.3倍，这个缩放只在Play模式生效，静态读prefab资产读不出来，必须手动补乘。
            float cellSize   = r.CellSize * mul;
            float cellGap    = r.CellGap * mul;
            float viewportInset = r.ViewportSideInset * mul;
            float contentMargin = r.ContentSideMargin * mul;
            float orderBtnW  = r.SortOrderButtonWidth * mul;
            float tidyBtnW   = r.TidyButtonWidth * mul;
            float filterBarHeight = FilterBarHeight * mul;
            float sortRowHeight = SortRowHeight * mul;

            float gridContentWidth = Columns * cellSize + (Columns - 1) * cellGap;
            // gridRegionWidth 本身左右两端已经各自包含 (contentMargin+viewportInset)，
            // 这就是"最右侧格子到网格区右边缘"该有的距离，跟背包一致——不能再在
            // 外面额外加一层 RightMargin，那样会让最右格子比背包多出一大截空白
            // (之前正是多套了这一层，导致仓库最右格子明显比背包离右边框更远)。
            float gridRegionWidth  = gridContentWidth + (contentMargin + viewportInset) * 2f;
            float panelWidth = LeftMargin + TabColumnW + GapTabToGrid + gridRegionWidth;

            // 面板高度：仓库比背包更高，独立按"至少能看见 MinVisibleRows 行格子"这个
            // 需求反推，不再照抄背包的高度（用户明确要求仓库比背包高）。
            // 标题栏→筛选标签栏→排序行 三者依次零间隙贴着叠放，跟背包真实的堆叠方式
            // 一致(背包这三者的偏移量算出来正好首尾相接，没有额外间隔)；排序行下方到
            // 页签/网格区之间才留 FilterBarGapToContent 这个仓库自己的呼吸间隙。
            float topOffsetForGrid = SkyPrisonFloatingWindowKit.TitleBarHeight
                + filterBarHeight + sortRowHeight + FilterBarGapToContent;
            float tidyBarHeight = TidyBarHeight * mul;
            float bottomReserved = TidyBarMarginBottom * mul + tidyBarHeight + BottomBarGapToGrid;
            float requiredGridViewportHeight = MinVisibleRows * cellSize + (MinVisibleRows - 1) * cellGap + viewportInset * 2f;
            float panelHeight = topOffsetForGrid + requiredGridViewportHeight + bottomReserved;

            // 面板：跟背包对称摆在屏幕左侧。页签这几轮"再大一些"陆续让 panelWidth
            // 多涨了43px（TabColumnW从87加到130），右边界因此往右顶到了背包身上——
            // 离左屏幕边缘的留白从48收紧到16，找回一部分空间，不用去动背包的位置。
            var panel = CreateChildRect(root, "StashPanel",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(16f, 0f), new Vector2(panelWidth, panelHeight));

            // 1. 黑白高斯模糊背景 —— 直接用共享 Kit，跟背包/角色面板同一套实现，不再自己搭一遍。
            SkyPrisonFloatingWindowKit.BuildBlurBackground(null, panel, out _);

            var panelHit = panel.gameObject.AddComponent<Image>();
            panelHit.color = new Color(0f, 0f, 0f, 0f);
            panelHit.raycastTarget = true;

            // 2. 四角白色 L 形角标 —— 现读背包真实角标(长20/粗2，Kit 自带的默认值
            // 39/3.9 是 Kit 自己的"新标准"，跟背包手搭的这份预制体本来就没对齐，Kit
            // 类文档里也承认这一点)，用带参数的重载传真实值，才能跟背包视觉一致。
            // 用户曾要求改成跟角色信息面板一样大（角色信息面板用不带参数的默认值
            // 39/3.9），但直接搜背包prefab里4个角标节点的真实序列化值，精确确认
            // 就是 {x:20,y:2}，角色信息面板视觉上"看起来差不多大"是它自己另外还有
            // 别的缩放，不能当真实数值抄——跟背包对齐只能用20/2，改回来。
            SkyPrisonFloatingWindowKit.AddCornerBrackets(panel, Color.white, 20f * mul, 2f * mul);

            // 3. 标题栏（含拖动 + 标题文字，共享 Kit 标准规格：字号/位置/高度已经内置1.3倍）
            SkyPrisonFloatingWindowKit.BuildTitleBar(panel, "仓库", font, out TMP_Text titleLabel);

            // 4. 关闭按钮 —— 共享 Kit 标准规格，锚在面板右上角。
            SkyPrisonFloatingWindowKit.BuildCloseButton(panel, null, font);
            Button closeButton = panel.Find("CloseButton").GetComponent<Button>();

            // 5. 筛选标签栏（全部/消耗品/材料/装备/任务/重要）——标题栏正下方、贯穿整个
            // 面板宽度，跟背包 FilterBar 的位置规则一致(紧贴标题栏，边到边不留左右边距)。
            var filterBarRT = CreateChildRect(panel, "FilterBar",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -SkyPrisonFloatingWindowKit.TitleBarHeight), new Vector2(0f, filterBarHeight));
            filterBarRT.gameObject.AddComponent<Image>().color = Color.clear;
            List<TextMeshProUGUI> filterLabels = BuildFilterTabs(filterBarRT, panelWidth, font);

            // 6. 排序行(排序标签+下拉框+升降序按钮)——紧贴筛选标签栏正下方，零间隙，
            // 元素相对位置/宽度都是从背包同一行现读的真实设计值（不是拉伸铺满整行）。
            float sortRowTop = SkyPrisonFloatingWindowKit.TitleBarHeight + filterBarHeight;
            var sortRowRT = CreateChildRect(panel, "SortRow",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -sortRowTop), new Vector2(0f, sortRowHeight));
            sortRowRT.gameObject.AddComponent<Image>().color = Color.clear;
            (Text sortLabelText, Text orderLabelText) = BuildSortRow(sortRowRT, orderBtnW, font, legacyFont);

            // 7. 左侧页签容器（4页，缩小到原尺寸1/3，本窗口独有）——底边跟网格区一样，
            // 停在底部排序条上方，不能只留固定值，否则页签栏底部会伸进排序条范围里重叠。
            var tabContainer = CreateChildRect(panel, "PageTabContainer",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(LeftMargin, -topOffsetForGrid), new Vector2(TabColumnW, panelHeight - topOffsetForGrid - bottomReserved));
            BuildPageTabs(tabContainer, font);

            // 8. 网格区域（ScrollRect + 100格），底部给整理条留位置
            var gridScrollRT = CreateChildRect(panel, "GridScrollArea",
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            gridScrollRT.offsetMin = new Vector2(LeftMargin + TabColumnW + GapTabToGrid, bottomReserved);
            // 右边贴到面板边缘(不再额外减 RightMargin)——viewport/content 自己的内缩
            // (viewportInset+contentMargin)已经是背包真实的"离右边框多远"，这里再减
            // 一层等于叠加了两次边距。
            gridScrollRT.offsetMax = new Vector2(0f, -topOffsetForGrid);

            RectTransform gridContent = BuildScrollingGrid(gridScrollRT, cellSize, cellGap, viewportInset, r.CellColor, font);

            // 100格只有6行能同时看到，剩下14行只能靠鼠标滚轮/拖拽滚出来，之前完全没有
            // 可见的滚动条提示——用户根本看不出来还能往下滚。补上项目统一的胶囊形滚动条
            // (SkyPrisonUIScrollbar，设置窗口/存档选择器同一套)，贴在网格区右边。
            // 宽度直接照抄设置窗口那份已验证长得对的参数(8f，不乘mul)——之前乘了mul，
            // 圆头看起来跟直线没区别，先跟已知正确的参照对齐排除这个变量。
            // 用户反馈可点击/可拖拽范围太窄、位置还有点偏——8宽在仓库这种整体放大过的
            // UI尺度下，实际点击命中区域太小了；rightMargin之前是瞎猜的viewportInset
            // 比例值，没对齐视觉边缘。换成更粗一点的宽度 + 固定的小边距。
            var stashScrollRect = gridScrollRT.GetComponent<ScrollRect>();
            SkyPrisonUIScrollbar.AttachVertical(stashScrollRect, gridScrollRT, Color.white,
                rightMargin: 6f, topMargin: 0f, bottomMargin: 0f, width: 16f);

            var stashGridView = gridScrollRT.gameObject.AddComponent<StashGridView>();
            AssignPrivateField(stashGridView, "gridContent", gridContent);
            AssignPrivateField(stashGridView, "columns", Columns);
            AssignPrivateField(stashGridView, "fallbackCap", CellCount);

            // 9. 底部整理条（TidyButton，命名跟 SkyPrisonInventorySortControls.Setup()
            //    里 FindDeep 找的名字一致，才能被那套已有组件自动接线）——背包真实的
            //    整理按钮就是单独在底部，跟排序行(第6步)是两个分开的行，不在一起。
            Text tidyLabelText = BuildTidyBar(panel, tidyBtnW, legacyFont);

            // 10. 本地化——挂 SkyPrisonInventoryTextLocalizer，复用背包同一张
            // UILocalizationTable。筛选标签/排序/整理这几个 key 背包已经有，直接复用；
            // 标题用新建的 ui_stash_title。升降序箭头("↑"/"↓")不是可译文字，只登记
            // 字体（fontOnlyTexts），不登记 locKey。
            var localizer = panel.gameObject.AddComponent<SkyPrisonInventoryTextLocalizer>();
            localizer.SetSources(localizationSettings, localizationTable);
            if (titleLabel != null) localizer.RegisterTMPTextNode(titleLabel, StashTitleLocKey);
            for (int i = 0; i < filterLabels.Count; i++)
                localizer.RegisterTMPTextNode(filterLabels[i], FilterTabDefs[i].locKey);
            localizer.RegisterTextNode(sortLabelText, "ui_sort_label");
            localizer.RegisterTextNode(tidyLabelText, "ui_tidy_button");
            localizer.RegisterFontOnlyText(orderLabelText);

            return new BuiltPanelRefs
            {
                CloseButton  = closeButton,
                TabContainer = tabContainer,
                GridView     = stashGridView,
                TabFont      = font,
            };
        }

        private static void WireController(StashWindowController controller, BuiltPanelRefs refs)
        {
            var so = new SerializedObject(controller);
            so.FindProperty("closeButton").objectReferenceValue = refs.CloseButton;
            so.FindProperty("pageTabContainer").objectReferenceValue = refs.TabContainer;
            so.FindProperty("stashGridView").objectReferenceValue = refs.GridView;
            so.FindProperty("tabFont").objectReferenceValue = refs.TabFont;

            var invPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(InventoryPrefabPath);
            if (invPrefab != null)
                so.FindProperty("inventoryWindowPrefab").objectReferenceValue = invPrefab;
            else
                Debug.LogWarning("[Stash] 找不到背包 prefab，没能自动绑定「同时打开背包」引用，需要手动在 Inspector 里拖。");

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── 页签（4页，缩小到1/3尺寸，未解锁用半透明遮罩表示，本窗口独有）──────────

        private static void BuildPageTabs(RectTransform container, TMP_FontAsset font)
        {
            for (int i = 0; i < StashRuntime.MaxPages; i++)
            {
                float y = -TabTopInset - (TabHeight + TabGap) * i;
                var tab = CreateChildRect(container, $"Tab_{i + 1}",
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(0f, TabHeight));

                tab.gameObject.AddComponent<Image>().color = Color.clear;
                tab.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
                // "コ"字形边框——只画上/左/下三条边，右边(挨着网格区那一侧)不画，
                // 视觉上像页签跟右边的格子区是连在一起的，用户明确要求的形状。
                AddKoShapeOutline(tab, Color.white, 2f);

                var label = AddTMP(tab, "Label", (i + 1).ToString(), 32,
                    TextAlignmentOptions.Center, new Color(0.88f, 0.88f, 0.90f, 1f), FontStyles.Bold, font);
                StretchFull(label.rectTransform);

                // 锁定遮罩——半透明深色覆盖整个页签，RefreshTabStates() 按解锁状态开关
                // (StashWindowController 靠 tab.Find("LockIcon") 精确找这个节点名，
                // 不能改)。真正的锁头图案作为它的子节点叠加在上面，跟着一起显隐。
                var lockIcon = CreateChildRect(tab, "LockIcon",
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                var lockImg = lockIcon.gameObject.AddComponent<Image>();
                lockImg.color = new Color(0f, 0f, 0f, 0.6f);
                lockImg.raycastTarget = false;

                Sprite lockSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LockIconAssetPath);
                if (lockSprite != null)
                {
                    var lockSpriteRt = CreateChildRect(lockIcon, "LockIconSprite",
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        Vector2.zero, new Vector2(LockIconSize, LockIconSize));
                    var lockSpriteImg = lockSpriteRt.gameObject.AddComponent<Image>();
                    lockSpriteImg.sprite = lockSprite;
                    lockSpriteImg.preserveAspect = true;
                    lockSpriteImg.raycastTarget = false;
                }
            }
        }

        // "コ"字形边框：上/右/下三条边，左边（紧贴面板左边框/页签自己这一侧）留空——
        // 之前画反了，把该留空的左边画了边框、该保留的右边（紧贴网格区那一侧）删掉了。
        private static void AddKoShapeOutline(RectTransform rt, Color c, float px)
        {
            SkyPrisonFloatingWindowKit.AddLineRT(rt, "OT", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, px), c);
            SkyPrisonFloatingWindowKit.AddLineRT(rt, "OB", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, px), c);
            SkyPrisonFloatingWindowKit.AddLineRT(rt, "OR", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(px, 0f), c);
        }

        // ── 筛选标签栏（全部/消耗品/材料/装备/任务/重要物品）──────────────────────
        // 字号/颜色现读自背包 InventoryWindowController 组件在真实 prefab 上的序列化值
        // (18/{0.66,0.66,0.68,1})，不是它 C# 字段声明的默认值——之前用的21/纯白从没
        // 跟这份 prefab 实际用的值核对过。6 个等宽 Tab 边到边铺满整个面板宽度，跟背包
        // FilterBar 的规则一样(144宽 x 6 = 864 正好等于背包面板宽度)。点击/辉光/
        // hover 接线在运行时由 StashWindowController.SetupFilterTabs 完成，这里只搭骨架。
        // 标签文字用 locKey 对应的 fallback（跟背包 UILocalizationTable 里的实际值一致，
        // 不是从节点名猜的——"重要物品"之前就因为猜节点名简写成"重要"，跟背包对不上）；
        // BuildContents 会再把这些 TMP 组件注册进 SkyPrisonInventoryTextLocalizer，
        // 换语言时才会真的查表更新，不是写死中文。
        private static readonly (string locKey, string fallback)[] FilterTabDefs =
        {
            ("ui_filter_all",        "全部"),
            ("ui_filter_consumable", "消耗品"),
            ("ui_filter_material",   "材料"),
            ("ui_filter_equipment",  "装备"),
            ("ui_filter_quest",      "任务"),
            ("ui_filter_keyitem",    "重要物品"),
        };

        private static List<TextMeshProUGUI> BuildFilterTabs(RectTransform filterBar, float panelWidth, TMP_FontAsset font)
        {
            float mul = SkyPrisonFloatingWindowKit.StandardScaleMultiplier;
            float tabWidth = panelWidth / FilterTabDefs.Length;
            float tabFontSize = FilterTabFontSize * mul;
            var labels = new List<TextMeshProUGUI>(FilterTabDefs.Length);

            for (int i = 0; i < FilterTabDefs.Length; i++)
            {
                var tab = CreateChildRect(filterBar, $"FilterTab_{FilterTabDefs[i].locKey}",
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                    new Vector2(tabWidth * i, 0f), new Vector2(tabWidth, 0f));

                var clickImg = tab.gameObject.AddComponent<Image>();
                clickImg.color = new Color(1f, 1f, 1f, 0f);
                clickImg.raycastTarget = true;

                var label = AddTMP(tab, "Label", FilterTabDefs[i].fallback, tabFontSize,
                    TextAlignmentOptions.Center, FilterTabNormalColor, FontStyles.Normal, font);
                label.overflowMode = TextOverflowModes.Overflow;
                StretchFull(label.rectTransform);
                labels.Add(label);
            }

            return labels;
        }

        // ── 100格网格（ScrollRect + Viewport + Mask + Content）──────────────────

        private static RectTransform BuildScrollingGrid(RectTransform area, float cellSize, float cellGap,
            float viewportInset, Color cellColor, TMP_FontAsset font)
        {
            var scrollRect = area.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical   = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateChildRect(area, "Viewport",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            viewport.offsetMin = new Vector2(viewportInset, viewportInset);
            viewport.offsetMax = new Vector2(-viewportInset, -viewportInset);
            viewport.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = CreateChildRect(viewport, "GridContent",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 0f));

            scrollRect.viewport = viewport;
            scrollRect.content  = content;

            int rows = Mathf.CeilToInt(CellCount / (float)Columns);
            float contentWidth  = Columns * cellSize + (Columns - 1) * cellGap;
            float contentHeight = rows * (cellSize + cellGap) - cellGap;
            content.sizeDelta = new Vector2(contentWidth, contentHeight);

            for (int i = 0; i < CellCount; i++)
            {
                int col = i % Columns;
                int row = i / Columns;
                Vector2 pos = new Vector2(
                    col * (cellSize + cellGap),
                    -row * (cellSize + cellGap));
                BuildSlotCell(content, i, pos, cellSize, cellColor, font, IconInsetRaw * SkyPrisonFloatingWindowKit.StandardScaleMultiplier);
            }

            return content;
        }

        private const float IconInsetRaw = 28f; // 背包真实值：图标比格子边长小28(两侧各收14)

        private static void BuildSlotCell(Transform parent, int index, Vector2 anchoredPos,
            float cellSize, Color cellColor, TMP_FontAsset font, float iconInset)
        {
            // 格子锚点(0,1)——Unity 会把这个锚点自动解析到 Content 自己矩形的左上角
            // (Content.rect.xMin，本身就等于 -contentWidth/2，因为 Content pivot=(0.5,1))，
            // 不需要再手动减一次 contentWidth*0.5。anchoredPos 直接用 col/row 算出来的值
            // 即可，Unity的锚点解析已经处理好了居中这件事。
            var cell = CreateChildRect(parent, $"Slot_{index}",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                anchoredPos,
                new Vector2(cellSize, cellSize));

            var bg = cell.gameObject.AddComponent<Image>();
            bg.color = cellColor;

            // 图标尺寸——之前用 cellSize*0.72 是没核对过的猜测比例，背包真实的图标是
            // "格子边长 - 28(两侧各收14)"这个固定内缩值，不是按比例缩放，28 已经在
            // 外面乘过 mul 换算成实际渲染值传进来（见 iconInset 参数）。
            float iconSize = Mathf.Max(0f, cellSize - iconInset);
            var iconRT = CreateChildRect(cell, "Icon",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(iconSize, iconSize));
            var icon = iconRT.gameObject.AddComponent<Image>();
            icon.color = Color.white;
            icon.enabled = false;
            icon.raycastTarget = false;

            var countRT = CreateChildRect(cell, "Count",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-4f, 3f), new Vector2(cellSize * 0.7f, cellSize * 0.2f));
            var count = countRT.gameObject.AddComponent<TextMeshProUGUI>();
            count.alignment = TextAlignmentOptions.BottomRight;
            count.fontSize = cellSize * 0.13f;
            count.raycastTarget = false;
            if (font != null) count.font = font;

            var newBadge = CreateChildRect(cell, "NewBadge",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(3f, -3f), new Vector2(cellSize * 0.3f, cellSize * 0.15f));
            var badgeBg = newBadge.gameObject.AddComponent<Image>();
            badgeBg.color = new Color(0.75f, 0.16f, 0.16f, 1f);
            var badgeLabelGO = new GameObject("Label");
            var badgeLabelRT = badgeLabelGO.AddComponent<RectTransform>();
            badgeLabelRT.SetParent(newBadge, false);
            StretchFull(badgeLabelRT);
            var badgeLabel = badgeLabelGO.AddComponent<Text>();
            badgeLabel.text = "NEW";
            badgeLabel.alignment = TextAnchor.MiddleCenter;
            badgeLabel.fontSize = Mathf.RoundToInt(cellSize * 0.075f);
            badgeLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            badgeLabel.raycastTarget = false;
            newBadge.gameObject.SetActive(false);

            var slotView = cell.gameObject.AddComponent<InventorySlotView>();
            slotView.Init(icon, count, newBadge.gameObject, bg, index);

            // 挂 InventorySlotInteractor——不是为了让仓库格子自己也能被拖拽(那个还没做，
            // 拖动仓库自己的格子目前是空操作)，而是为了让背包侧 SkyPrisonInventoryInteraction
            // 的 FindCellUnder(全局 raycast 找 InventorySlotInteractor)能识别到"这是一个
            // 仓库格子"，从背包拖物品放到这里才会走转移逻辑，而不是被当成"拖出窗口"丢弃。
            cell.gameObject.AddComponent<InventorySlotInteractor>();
        }

        // ── 排序行（排序标签 + 下拉框 + 升降序按钮）──────────────────────────────
        // 节点命名严格对应 SkyPrisonInventorySortControls.Setup() 里 FindDeep 要找的
        // 名字("SortDropdown"/"SortOrderButton")，该组件运行时自愈接线，这里只需要把
        // 外观和名字搭对，不用重新实现下拉逻辑。
        //
        // 之前误以为这一行在底部、且下拉框要拉伸铺满剩余宽度——这两点已经修正。另外
        // 之前还错误地认定这几个元素背景全透明没有边框，把描边整个删掉了——重新逐个
        // 检查这几个节点的子物体后发现：它们各自都有 4 条 1px 白色细线子物体(上下左右)
        // 拼成边框，只是父节点自己的 Image 是透明的，边框是另外叠加的子物体，不是父
        // 节点本身的背景——之前只看了父节点的颜色，漏看了这层子物体，误判成"背包没有
        // 边框"。现在用 Kit 的 AddOutline 补回来，粗细用背包真实的 1px(乘 mul 换算)。
        private static (Text sortLabel, Text orderLabel) BuildSortRow(RectTransform row, float orderBtnW, TMP_FontAsset font, Font legacyFont)
        {
            float mul = SkyPrisonFloatingWindowKit.StandardScaleMultiplier;
            float borderPx = 1f * mul;

            float labelOffset = SortLabelOffsetX * mul;
            float labelWidth  = SortLabelWidth * mul;
            var sortLabelRT = CreateChildRect(row, "SortLabel",
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(labelOffset, 0f), new Vector2(labelWidth, 0f));
            var sortLabel = sortLabelRT.gameObject.AddComponent<Text>();
            sortLabel.text = "排序";
            sortLabel.alignment = TextAnchor.MiddleLeft;
            sortLabel.fontSize = Mathf.RoundToInt(SortLabelFontSize * mul);
            sortLabel.color = Color.white;
            sortLabel.font = legacyFont;
            sortLabel.raycastTarget = false;

            float ddOffset = SortDropdownOffsetX * mul;
            float ddWidth  = SortDropdownWidth * mul;
            var ddRT = CreateChildRect(row, "SortDropdown",
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(ddOffset, 0f), new Vector2(ddWidth, 0f));
            var ddImg = ddRT.gameObject.AddComponent<Image>();
            ddImg.color = new Color(0f, 0f, 0f, 0f);
            SkyPrisonFloatingWindowKit.AddOutline(ddRT, Color.white, borderPx);

            float orderOffset = SortOrderButtonOffsetX * mul;
            var orderRT = CreateChildRect(row, "SortOrderButton",
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(orderOffset, 0f), new Vector2(orderBtnW, 0f));
            var orderImg = orderRT.gameObject.AddComponent<Image>();
            orderImg.color = new Color(0f, 0f, 0f, 0f);
            SkyPrisonFloatingWindowKit.AddOutline(orderRT, Color.white, borderPx);
            var orderLabelGO = new GameObject("Label");
            var orderLabelRT = orderLabelGO.AddComponent<RectTransform>();
            orderLabelRT.SetParent(orderRT, false);
            StretchFull(orderLabelRT);
            var orderLabel = orderLabelGO.AddComponent<Text>();
            orderLabel.text = "↑";
            orderLabel.alignment = TextAnchor.MiddleCenter;
            orderLabel.fontSize = Mathf.RoundToInt(SortOrderArrowFontSize * mul);
            orderLabel.color = SortOrderArrowColor;
            orderLabel.font = legacyFont;
            orderLabel.raycastTarget = false;

            return (sortLabel, orderLabel);
        }

        // ── 底部整理条（本窗口独有布局，按钮宽度现读自背包并换算实际大小）──────────
        // 节点命名对应 SkyPrisonInventorySortControls.Setup() 里 FindDeep 要找的
        // "TidyButton"。背包真实的整理按钮就是单独在底部，跟排序行是分开的两行。
        private static Text BuildTidyBar(RectTransform panel, float tidyBtnW, Font legacyFont)
        {
            float mul = SkyPrisonFloatingWindowKit.StandardScaleMultiplier;
            float barHeight = TidyBarHeight * mul;
            float barMarginBottom = TidyBarMarginBottom * mul;
            float heightInset = TidyButtonHeightInset * mul;

            // 容器贴着面板左右边缘零边距，全宽——背包真实的这个容器就是这样(不留
            // LeftMargin/RightMargin空白)，"离右下角多远"完全由 TidyButton 自己
            // 右边收进多少决定，不是靠容器边距撑出来的。
            var bar = CreateChildRect(panel, "TidyBar",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, barMarginBottom), new Vector2(0f, barHeight));
            bar.gameObject.AddComponent<Image>().color = Color.clear;

            // TidyButton 右边收进 TidyButtonRightInset(8，背包真实值)，上下各收进
            // heightInset/2——背包真实数值是容器52高、按钮收进14(sizeDelta.y=-14)、
            // 右边收进8(anchoredPosition.x=-8)，不是通栏铺满、也不是贴死右边缘。
            var tidyRT = CreateChildRect(bar, "TidyButton",
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-TidyButtonRightInset * mul, 0f), new Vector2(tidyBtnW, -heightInset));
            var tidyImg = tidyRT.gameObject.AddComponent<Image>();
            tidyImg.color = new Color(0f, 0f, 0f, 0f);
            // 边框系数不能照抄背包的 1（1*mul=1.3 canvas单位）——在非4K显示器上还要再乘一层
            // CanvasScaler的运行时缩放，细到亚像素后抗锯齿会让某几个角落的线段直接看不见，
            // 表现成"整理框缺了一角"。项目里"要细但要看得见"的线统一用 2.0 起步的下限
            // （见 ui_thin_line_min_width 这条约定），这里保底到至少2。
            // 之前为了防止亚像素消失把这里提到2倍，代价是比背包真实的1倍粗——用户直接
            // 反馈"比背包粗"，对齐优先，改回跟背包一致的1倍。
            SkyPrisonFloatingWindowKit.AddOutline(tidyRT, Color.white, 1f * mul);

            // 背包真实的整理按钮文字是旧版 Text，不是 TMP——字号32/彩色也是之前没核对
            // 过的猜测值，背包真实值是20号、纯白色。
            var tidyLabelGo = new GameObject("Label", typeof(RectTransform));
            tidyLabelGo.transform.SetParent(tidyRT, false);
            StretchFull((RectTransform)tidyLabelGo.transform);
            var tidyLabel = tidyLabelGo.AddComponent<Text>();
            tidyLabel.text = "整理";
            tidyLabel.alignment = TextAnchor.MiddleCenter;
            tidyLabel.fontSize = Mathf.RoundToInt(TidyLabelFontSize * mul);
            tidyLabel.color = Color.white;
            tidyLabel.font = legacyFont;
            tidyLabel.raycastTarget = false;

            return tidyLabel;
        }

        // ── TMP 工具 ──────────────────────────────────────────────────────────

        private static TextMeshProUGUI AddTMP(RectTransform parent, string name,
            string text, float fontSize, TextAlignmentOptions align, Color color, FontStyles style, TMP_FontAsset font)
        {
            var rt = CreateChildRect(parent, name,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fontSize;
            tmp.alignment = align; tmp.color = color;
            tmp.fontStyle = style; tmp.raycastTarget = false;
            if (font != null) tmp.font = font;
            return tmp;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        }

        private static void CopyStyleFromInventory(SkyPrisonUIGlobalStyleSettings_V1 dest, TMP_FontAsset font)
        {
            if (dest == null || font == null) return;
            dest.defaultTextFont   = font;
            dest.defaultNumberFont = font;
            dest.defaultTextColor  = Color.white;
        }

        // ── 通用工具 ──────────────────────────────────────────────────────────

        private static RectTransform CreateChildRect(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin        = anchorMin;
            rt.anchorMax        = anchorMax;
            rt.pivot            = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = sizeDelta;
            return rt;
        }

        private static void AssignPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field == null)
            {
                Debug.LogError($"[Stash] 找不到字段 {target.GetType().Name}.{fieldName}，检查字段名是否改过。");
                return;
            }
            field.SetValue(target, value);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
            string folder = System.IO.Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
#endif
