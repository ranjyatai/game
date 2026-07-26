#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SkyPrison.Runtime.UI;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 商店窗口 v1——按用户草图搭：左货架/右详情两栏，居中不可拖拽，标题栏购物车按钮
    /// 切换到结账列表视图（不是货架/结账同时铺开）。第一版，先能跑起来给用户看，
    /// 细节（NPC立绘、货架行hover显隐动画的打磨）留到定稿后再收。
    /// </summary>
    public static class CreateSkyPrisonShopPrefab
    {
        public const string PrefabPath    = "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonShop.prefab";
        public const string ResMirrorPath = "Assets/Resources/UI/Window/PF_SkyPrisonShop.prefab";
        private const string ShelfRowPrefabPath = "Assets/_Project/Prefabs/UI/Window/PF_ShopShelfRow.prefab";
        private const string CartRowPrefabPath  = "Assets/_Project/Prefabs/UI/Window/PF_ShopCartRow.prefab";
        private const string SellRowPrefabPath  = "Assets/_Project/Prefabs/UI/Window/PF_ShopSellRow.prefab";
        private const string DemoShopAssetPath  = "Assets/_Project/Data/Definitions/Custom/Shop/SD_DemoShop.asset";

        // 上一版 2700宽+偏移420 直接把窗口顶到实际测试屏幕外面去了（用户截图只剩详情区
        // 一条窄边）——偏移量相对窗口宽度太激进，回调到更保守的组合，同时把分割线
        // （ShelfW）往右挪，给货架区留更多空间做下面的两列卡片网格。
        private const float PanelW = 2700f, PanelH = 1550f; // 用户反馈整体还要再大一点，继续放大
        private const float ShelfW = 1650f; // 用户反馈货架区要更宽，继续加宽
        private const float PanelOffsetX = -340f;
        // 参考用户发的截图（竖版商店卡片，图标占满上半、价格用高亮色块压底部），
        // 改成竖版卡片+6列网格（用户反馈卡片可以小一半，列数翻倍让卡片宽度差不多减半）。
        private const float ShelfGridSpacing = 16f, ShelfGridPadding = 8f, ShelfScrollbarReserve = 20f;
        private const int   ShelfColumns = 5; // 用户反馈卡片可以再大一点，一行5个
        private static readonly float ShelfCardW =
            (ShelfW - ShelfGridPadding * 2f - ShelfGridSpacing * (ShelfColumns - 1) - ShelfScrollbarReserve) / ShelfColumns;
        private const float ShelfCardH = 480f; // 数量胶囊加高后跟名字区打架，加高卡片留出间距

        [MenuItem("Tools/Sky Prison/UI/Create Shop Window")]
        public static void Create()
        {
            ShopDefinition demoShop = EnsureDemoShopAsset(); // 先建好，等下直接烤进prefab字段，F6不用再运行时现set

            TMP_FontAsset font = SkyPrisonFloatingWindowKit.LoadTMPFont("ZhouFangRiMingTi-2 SDF");
            float mul = SkyPrisonFloatingWindowKit.StandardScaleMultiplier;

            var root = new GameObject("PF_SkyPrisonShop");
            var rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero; rootRT.anchorMax = Vector2.one;
            rootRT.sizeDelta = Vector2.zero; rootRT.anchoredPosition = Vector2.zero;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1100;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();

            var meta = root.AddComponent<SkyPrisonUIPrefabMetadata_V1>();
            meta.uiId = "shop";
            meta.displayName = "商店";
            meta.kind = SkyPrisonUIPrefabKindV1.Window;
            meta.defaultVisible = false;
            meta.sortingOrder = 1100;
            meta.blocksRaycasts = true;
            // 商店是真正的模态窗口（不像背包/仓库那种允许开着接着走路的），用户明确
            // 要求"打开商店窗口时候窗口外的操作请冻结"，包括角色移动——跟
            // PlayerDeathRevive同一套（lockGameplayInput=true + inputModeWhenOpen=UI），
            // 不是背包/仓库那套"只挡攻击/闪避、不挡移动"的方案。
            meta.lockGameplayInput = true;
            meta.showMouseCursor = true;
            meta.inputModeWhenOpen = SkyPrisonUIInputModeV1.UI;
            meta.closeOnEscape = true;
            meta.referenceResolution = new Vector2(3840f, 2160f);

            // 全屏阻挡层——先临时去掉排查关闭按钮/价格按钮点不动的问题（用户反馈加了
            // 阻挡层之后这两个按钮就失灵了，时间点刚好对上，先移除验证是不是这个原因，
            // 排查清楚后再决定怎么把"冻结背景"这个需求用别的方式做回来）。

            // 居中、不可拖拽——固定锚在屏幕正中心，不挂 SkyPrisonUIWindowDragHandle。
            var panel = MakeRect("ShopPanel", rootRT, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelW * mul / 1.3f, PanelH * mul / 1.3f); // 面板尺寸本身就按4K基准写，不需要再乘mul
            panel.sizeDelta = new Vector2(PanelW, PanelH);
            panel.anchoredPosition = new Vector2(PanelOffsetX, 0f);

            SkyPrisonFloatingWindowKit.BuildBlurBackground(null, panel, out _);
            var panelHit = panel.gameObject.AddComponent<Image>();
            panelHit.color = new Color(0f, 0f, 0f, 0f);
            panelHit.raycastTarget = true;
            SkyPrisonFloatingWindowKit.AddCornerBrackets(panel, Color.white, 20f * mul, 2f * mul);

            // 标题栏——不用 Kit.BuildTitleBar（那个自带拖拽），手搭一个不可拖拽的版本。
            var titleBar = MakeRect("TitleBar", panel, new Vector2(0f, 1f), new Vector2(1f, 1f));
            titleBar.pivot = new Vector2(0.5f, 1f);
            titleBar.sizeDelta = new Vector2(0f, SkyPrisonFloatingWindowKit.TitleBarHeight);
            var titleLabelRt = MakeRect("Title", titleBar, Vector2.zero, Vector2.one);
            titleLabelRt.offsetMin = new Vector2(32f, 0f);
            titleLabelRt.offsetMax = new Vector2(-260f, 0f);
            var titleLabel = SkyPrisonFloatingWindowKit.MakeText(titleLabelRt, "Label", "商店",
                SkyPrisonFloatingWindowKit.TitleFontSize, FontStyles.Bold, font);
            titleLabel.alignment = TextAlignmentOptions.MidlineLeft;
            titleLabel.raycastTarget = false;
            var titleLabelLegacyText = ConvertToLegacyIfNeeded(titleLabel); // 兼容 ShopWindowController.titleLabel:Text 字段

            SkyPrisonFloatingWindowKit.BuildCloseButton(panel, null, font); // onClose 由 SkyPrisonBaseWindowController 自动接管 closeButton 字段

            // 标题栏原来的购物车图标按钮去掉了（用户明确要求）——右下角新增的"结账"
            // 大按钮已经能做同一件事，数量角标也跟着挪到那个按钮上（下面创建）。

            // ── 购买/出售 标签页——左上角，标题栏正下方（用户明确要求：标签式，选中
            // 有绿色下划线，切换要有滑动过程不能瞬间跳过去）。
            const float tabBarH = 72f, tabW = 176f, tabGap = 4f, tabUnderlineH = 4f * 1f;
            var tabBarRt = MakeRect("TabBar", panel, new Vector2(0f, 1f), new Vector2(1f, 1f));
            tabBarRt.pivot = new Vector2(0.5f, 1f);
            tabBarRt.sizeDelta = new Vector2(0f, tabBarH);
            tabBarRt.anchoredPosition = new Vector2(0f, -SkyPrisonFloatingWindowKit.TitleBarHeight);

            Button MakeTabButton(string name, string label, float x, out Text labelOut)
            {
                var tabRt = MakeRect(name, tabBarRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
                tabRt.pivot = new Vector2(0f, 0.5f);
                tabRt.sizeDelta = new Vector2(tabW, tabBarH - 8f);
                tabRt.anchoredPosition = new Vector2(x, 0f);
                tabRt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
                var btn = tabRt.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                NoAutoNav(btn);
                var labelRt = MakeRect("Label", tabRt, Vector2.zero, Vector2.one);
                labelOut = labelRt.gameObject.AddComponent<Text>();
                labelOut.text = label; labelOut.font = LoadLegacyFont(); labelOut.fontSize = 32;
                labelOut.alignment = TextAnchor.MiddleCenter; labelOut.raycastTarget = false;
                return btn;
            }

            var buyTabBtn  = MakeTabButton("BuyTab", "购买", 24f, out Text buyTabLabelText);
            var sellTabBtn = MakeTabButton("SellTab", "出售", 24f + tabW + tabGap, out Text sellTabLabelText);
            buyTabLabelText.color = SkyPrisonUIPalette.ColdGreen; // 初始就是"购买"标签选中态

            var tabUnderlineRt = MakeRect("TabUnderline", tabBarRt, new Vector2(0f, 0f), new Vector2(0f, 0f));
            tabUnderlineRt.pivot = new Vector2(0f, 0f);
            tabUnderlineRt.sizeDelta = new Vector2(tabW, tabUnderlineH);
            tabUnderlineRt.anchoredPosition = new Vector2(24f, 6f);
            var tabUnderlineImg = tabUnderlineRt.gameObject.AddComponent<Image>();
            tabUnderlineImg.color = SkyPrisonUIPalette.ColdGreen;
            tabUnderlineImg.raycastTarget = false;

            float contentTop = SkyPrisonFloatingWindowKit.TitleBarHeight + tabBarH;

            // ── 购物区(左货架+右详情) ────────────────────────────────────────
            var shoppingRoot = MakeRect("ShoppingViewRoot", panel, Vector2.zero, Vector2.one);
            shoppingRoot.offsetMin = new Vector2(0f, 0f);
            shoppingRoot.offsetMax = new Vector2(0f, -contentTop);

            var shelfArea = MakeRect("ShelfArea", shoppingRoot, new Vector2(0f, 0f), new Vector2(0f, 1f));
            shelfArea.pivot = new Vector2(0f, 0.5f);
            shelfArea.sizeDelta = new Vector2(ShelfW, 0f);
            shelfArea.anchoredPosition = new Vector2(24f, 0f);
            Transform shelfContent = BuildScrollArea(shelfArea, out ScrollRect shelfScroll, useGrid: true);

            const float checkoutEntryH = 104f; // 用户反馈要再高一点，配合结账文字加大——提前到这里声明，下面分割线要用

            // 货架/详情分割竖线——之前是"借用"货架滚动条本身当分割线，滚动条改成
            // 不需要滚动时自动隐藏(AutoHideAndExpandViewport)之后，货架内容不足一页
            // 时分割线跟着一起消失了(用户反馈"竖线去哪里了")。改成单独一条常驻显示
            // 的细线，不再依赖滚动条；上下两头各加一个小方块端点(用户明确要求)。
            // 用户反馈线下端拖太长，超出窗口范围了——之前上下对称各留20，底部明显
            // 不够。改成不对称留白：顶部20，底部留够比"结账"入口按钮预留区(24+
            // checkoutEntryH+16)更靠上一点，线的下端点就不会拖到按钮那一层去。
            const float dividerX = ShelfW + 36f;
            const float dividerTopMargin = 20f, dividerBottomMargin = 24f + checkoutEntryH + 40f;
            var dividerLineRt = MakeRect("ShelfDetailDivider", shoppingRoot, new Vector2(0f, 0f), new Vector2(0f, 1f));
            dividerLineRt.offsetMin = new Vector2(dividerX - 1f, dividerBottomMargin);
            dividerLineRt.offsetMax = new Vector2(dividerX + 1f, -dividerTopMargin);
            dividerLineRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.35f);

            var dividerCapTopRt = MakeRect("CapTop", shoppingRoot, new Vector2(0f, 1f), new Vector2(0f, 1f));
            dividerCapTopRt.pivot = new Vector2(0.5f, 1f);
            dividerCapTopRt.sizeDelta = new Vector2(10f, 10f);
            dividerCapTopRt.anchoredPosition = new Vector2(dividerX, -dividerTopMargin);
            dividerCapTopRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.5f);

            var dividerCapBottomRt = MakeRect("CapBottom", shoppingRoot, new Vector2(0f, 0f), new Vector2(0f, 0f));
            dividerCapBottomRt.pivot = new Vector2(0.5f, 0f);
            dividerCapBottomRt.sizeDelta = new Vector2(10f, 10f);
            dividerCapBottomRt.anchoredPosition = new Vector2(dividerX, dividerBottomMargin);
            dividerCapBottomRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.5f);

            // 详情区底部让出一块给"结账"大按钮，再往上多让一块给钱包总览条（用户
            // 明确要求"结账上方展示玩家现在所有货币的所持金"，不止一种代币）。
            const float walletBarH = 64f, walletBarGap = 12f, walletTitleReserve = 36f + 6f;
            var detailArea = MakeRect("DetailArea", shoppingRoot, new Vector2(0f, 0f), new Vector2(1f, 1f));
            detailArea.offsetMin = new Vector2(ShelfW + 48f, 24f + checkoutEntryH + 16f + walletBarH + walletBarGap + walletTitleReserve);
            detailArea.offsetMax = new Vector2(-24f, 0f);
            var detailRefs = BuildDetailArea(detailArea, font);

            // "结账"按钮改成在右侧模块（详情区那一整块横向范围）里左右居中，而不是
            // 死贴在窗口最右边（用户明确要求）。右侧模块横向范围跟DetailArea用同一个
            // 起止：从 ShelfW+48 到面板右边距24。
            float rightModuleLeft = ShelfW + 48f;
            float rightModuleRight = PanelW - 24f;
            float rightModuleCenterX = (rightModuleLeft + rightModuleRight) * 0.5f;

            var checkoutEntryRt = MakeRect("GoToCheckoutButton", shoppingRoot, new Vector2(0f, 0f), new Vector2(0f, 0f));
            checkoutEntryRt.pivot = new Vector2(0.5f, 0f);
            checkoutEntryRt.sizeDelta = new Vector2(rightModuleRight - rightModuleLeft, checkoutEntryH);
            checkoutEntryRt.anchoredPosition = new Vector2(rightModuleCenterX, 24f);
            var checkoutEntryImg = checkoutEntryRt.gameObject.AddComponent<Image>();
            checkoutEntryImg.color = new Color(0f, 0f, 0f, 0f);
            SkyPrisonFloatingWindowKit.AddOutline(checkoutEntryRt, SkyPrisonUIPalette.ColdGreen, 2f * mul);
            var checkoutEntryBtn = checkoutEntryRt.gameObject.AddComponent<Button>();
            checkoutEntryBtn.transition = Selectable.Transition.None;
            NoAutoNav(checkoutEntryBtn);
            var checkoutEntryFeedback = SkyPrisonUIButtonFeedback.Attach(checkoutEntryRt.gameObject);

            // 购物车图标——用户之前讨论过、已经导入好的 UIWindow_Default_Cart.png，
            // 放在"结账"文字左边。图标固定贴左边距，文字区从图标右边开始（整体还是
            // 居中对齐，视觉上就是"图标+文字"一起偏向按钮左侧，不是文字单独完全居中）。
            const float checkoutIconSize = 56f;
            Sprite cartIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UIUX/Window/Styles/Default/Sprites/UIWindow_Default_Cart.png");
            if (cartIconSprite != null)
            {
                var cartIconRt = MakeRect("CartIcon", checkoutEntryRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
                cartIconRt.pivot = new Vector2(0f, 0.5f);
                cartIconRt.sizeDelta = new Vector2(checkoutIconSize * mul, checkoutIconSize * mul);
                cartIconRt.anchoredPosition = new Vector2(32f * mul, 0f);
                var cartIconImg = cartIconRt.gameObject.AddComponent<Image>();
                cartIconImg.sprite = cartIconSprite; cartIconImg.preserveAspect = true; cartIconImg.raycastTarget = false;
                cartIconImg.color = SkyPrisonUIPalette.ColdGreen;
            }

            var checkoutEntryLabel = MakeRect("Label", checkoutEntryRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            checkoutEntryLabel.text = "结账"; checkoutEntryLabel.font = LoadLegacyFont(); checkoutEntryLabel.fontSize = 40; // 用户反馈文字再大一点
            checkoutEntryLabel.alignment = TextAnchor.MiddleCenter; checkoutEntryLabel.color = SkyPrisonUIPalette.ColdGreen;
            checkoutEntryLabel.raycastTarget = false;

            // 购物车数量角标——原来挂在标题栏购物车图标上，图标去掉了之后挪到这个
            // "结账"按钮的右上角。改成正圆形，字号加大；悬停按钮时不跟着一起变半透明
            // （用户明确要求），下面单独用Configure()把角标排除在按钮的悬停染色目标外。
            // 圆形角标之前用 Image.Type.Sliced + 半径=贴图边长一半(64,32) 画圆——border
            // 32+32刚好等于贴图整个宽度，等于没有可拉伸的中间区域，Sliced在这种退化
            // 情况下渲染出来的是带直边的圆角方块，不是真正的圆（用户反馈"感觉不圆"）。
            // 圆形本身宽高比恒为1:1、不会被拉伸变形，根本不需要9-slice，改回
            // Image.Type.Simple 直接整张贴图缩放，圆形结构不会失真。数字也再放大一档。
            var cartCountRt = MakeRect("Count", checkoutEntryRt, new Vector2(1f, 1f), new Vector2(1f, 1f));
            cartCountRt.pivot = new Vector2(1f, 1f);
            cartCountRt.sizeDelta = new Vector2(64f * mul, 64f * mul);
            cartCountRt.anchoredPosition = new Vector2(10f * mul, 10f * mul);
            var cartCountBg = cartCountRt.gameObject.AddComponent<Image>();
            cartCountBg.sprite = LoadOrCreatePersistedRoundedRectSprite(64, 32); // 半径=贴图边长一半，出来就是正圆
            cartCountBg.type = Image.Type.Simple;
            cartCountBg.color = SkyPrisonUIPalette.ColdGreen;
            // 字号稍微收一档；白色实心字改成"挖空"风格——颜色跟卡片/面板背景同色，
            // 读起来像是绿色圆底上挖了个洞露出底色数字，跟价格色块的挖空数字是同一
            // 套手法（用户明确要求）。
            var cartCountText = SkyPrisonFloatingWindowKit.MakeText(cartCountRt, "Label", "",
                36f * mul, FontStyles.Bold, font);
            cartCountText.color = new Color(0.13f, 0.14f, 0.15f, 1f);
            cartCountText.alignment = TextAlignmentOptions.Center;
            cartCountText.raycastTarget = false;
            var cartCountLegacyText = ConvertToLegacyIfNeeded(cartCountText); // 仅为兼容 ShopWindowController.cartCountText:Text 字段

            // SkyPrisonUIButtonFeedback默认会递归收集按钮下所有子图形一起悬停变色，
            // 角标本来就有自己固定的强调色，不该跟着按钮一起被染成半透明——这里手动
            // 排除角标（Count）底下的图形，只保留描边线条+"结账"文字两块。
            var countGraphics = new HashSet<Graphic>(cartCountRt.GetComponentsInChildren<Graphic>(true));
            var feedbackTargets = new List<Graphic>();
            foreach (Graphic g in checkoutEntryRt.GetComponentsInChildren<Graphic>(true))
                if (!countGraphics.Contains(g)) feedbackTargets.Add(g);
            checkoutEntryFeedback.Configure(feedbackTargets.ToArray());

            // ── 钱包总览条——"结账"按钮正上方，列出项目里所有货币各自当前持有量 ──
            // 之前用 HorizontalLayoutGroup 摆各个货币槽位，编辑器生成阶段布局系统根本
            // 没跑过一次(Unity的Layout重算是运行时/编辑器重绘时才触发，PrefabUtility.
            // SaveAsPrefabAsset 存下来的是生成那一刻还没被布局系统摆过的原始坐标，
            // 存进prefab的位置全部叠在一起没展开)——改回跟这个文件里其它地方一样的
            // 手动摆放，不依赖任何自动布局系统在保存前跑一遍。
            // 用户明确要求：条上方加"所持货币"标题；每个槽位内部图标贴左、数字贴右
            // （不是图标紧挨着数字），槽位横向平分整条宽度。
            const float walletTitleH = 36f;
            var walletTitleRt = MakeRect("WalletTitle", shoppingRoot, new Vector2(0f, 0f), new Vector2(0f, 0f));
            walletTitleRt.pivot = new Vector2(0.5f, 0f);
            walletTitleRt.sizeDelta = new Vector2(rightModuleRight - rightModuleLeft, walletTitleH);
            walletTitleRt.anchoredPosition = new Vector2(rightModuleCenterX, 24f + checkoutEntryH + walletBarGap + walletBarH);
            var walletTitleText = walletTitleRt.gameObject.AddComponent<Text>();
            walletTitleText.text = "所持货币"; walletTitleText.font = LoadLegacyFont(); walletTitleText.fontSize = 26;
            walletTitleText.color = new Color(0.7f, 0.72f, 0.74f, 1f);
            walletTitleText.alignment = TextAnchor.MiddleLeft; walletTitleText.raycastTarget = false;

            var walletBarRt = MakeRect("WalletBar", shoppingRoot, new Vector2(0f, 0f), new Vector2(0f, 0f));
            walletBarRt.pivot = new Vector2(0.5f, 0f);
            walletBarRt.sizeDelta = new Vector2(rightModuleRight - rightModuleLeft, walletBarH);
            walletBarRt.anchoredPosition = new Vector2(rightModuleCenterX, 24f + checkoutEntryH + walletBarGap);

            string[] currencyGuids = AssetDatabase.FindAssets("t:CurrencyDefinition");
            var currencyDefs = new List<CurrencyDefinition>();
            foreach (string guid in currencyGuids)
            {
                var def = AssetDatabase.LoadAssetAtPath<CurrencyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null) currencyDefs.Add(def);
            }
            currencyDefs.Sort((a, b) => string.Compare(a.currencyId, b.currencyId, System.StringComparison.OrdinalIgnoreCase));

            var walletSlots = new List<ShopWalletBar.Slot>();
            if (currencyDefs.Count > 0)
            {
                const float slotGap = 40f;
                float totalWidth = rightModuleRight - rightModuleLeft;
                float slotWidth = (totalWidth - slotGap * (currencyDefs.Count - 1)) / currencyDefs.Count;

                for (int i = 0; i < currencyDefs.Count; i++)
                {
                    var def = currencyDefs[i];
                    var slotRt = MakeRect($"Slot_{def.currencyId}", walletBarRt, new Vector2(0f, 0f), new Vector2(0f, 1f));
                    slotRt.pivot = new Vector2(0f, 0.5f);
                    slotRt.sizeDelta = new Vector2(slotWidth, 0f);
                    slotRt.anchoredPosition = new Vector2(i * (slotWidth + slotGap), 0f);

                    var slotIconRt = MakeRect("Icon", slotRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
                    slotIconRt.pivot = new Vector2(0f, 0.5f);
                    slotIconRt.sizeDelta = new Vector2(48f, 48f);
                    slotIconRt.anchoredPosition = Vector2.zero;
                    var slotIconImg = slotIconRt.gameObject.AddComponent<Image>();
                    slotIconImg.sprite = def.icon; slotIconImg.preserveAspect = true;

                    // 数字贴槽位最右边，跟图标之间空出中间那段（用户明确要求"图标左对齐，
                    // 数字右对齐"，不是紧挨着）。
                    var slotTextRt = MakeRect("Amount", slotRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                    slotTextRt.pivot = new Vector2(1f, 0.5f);
                    slotTextRt.sizeDelta = new Vector2(slotWidth - 64f, 48f);
                    slotTextRt.anchoredPosition = Vector2.zero;
                    var slotText = slotTextRt.gameObject.AddComponent<Text>();
                    slotText.font = LoadLegacyFont(); slotText.fontSize = 32; slotText.color = SkyPrisonUIPalette.ColdGreen;
                    slotText.alignment = TextAnchor.MiddleRight; slotText.raycastTarget = false;

                    walletSlots.Add(new ShopWalletBar.Slot { currency = def, amountText = slotText });
                }
            }

            var walletBar = walletBarRt.gameObject.AddComponent<ShopWalletBar>();
            var walletBarSo = new SerializedObject(walletBar);
            var walletSlotsProp = walletBarSo.FindProperty("slots");
            walletSlotsProp.arraySize = walletSlots.Count;
            for (int i = 0; i < walletSlots.Count; i++)
            {
                var elem = walletSlotsProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("currency").objectReferenceValue = walletSlots[i].currency;
                elem.FindPropertyRelative("amountText").objectReferenceValue = walletSlots[i].amountText;
            }
            walletBarSo.ApplyModifiedPropertiesWithoutUndo();

            // ── 结账区(购物车列表，初始隐藏) ────────────────────────────────
            var checkoutRoot = MakeRect("CheckoutViewRoot", panel, Vector2.zero, Vector2.one);
            checkoutRoot.offsetMin = new Vector2(24f, 24f);
            checkoutRoot.offsetMax = new Vector2(-24f, -contentTop);
            checkoutRoot.gameObject.SetActive(false);
            // 用户要求结账界面的"结账"/"返回购物"按钮跟购物界面那个大"结账"入口按钮
            // 一样大——直接把同一组尺寸(右侧模块整宽×104高)传进去，两个按钮左右并排
            // 摆在结账界面这个更宽的区域里（结账界面用的是整个面板宽度，摆得下）。
            var checkoutRefs = BuildCheckoutArea(checkoutRoot, font, rightModuleRight - rightModuleLeft, checkoutEntryH);

            // ── 出售区（把背包物品卖给商店，初始隐藏）──────────────────────────
            var sellRoot = MakeRect("SellViewRoot", panel, Vector2.zero, Vector2.one);
            sellRoot.offsetMin = new Vector2(24f, 24f);
            sellRoot.offsetMax = new Vector2(-24f, -contentTop);
            sellRoot.gameObject.SetActive(false);

            // 底部让出一块给"去结账"入口按钮——跟购物区右下角"结账"按钮同一套尺寸/
            // 交互逻辑，用户明确要求出售也要走"选数量→加入清单→结账页确认"两段式，
            // 不能一点就直接卖掉，所以出售列表本身不再是终点，得有路走到确认页。
            var sellScrollArea = MakeRect("SellScrollArea", sellRoot, Vector2.zero, Vector2.one);
            sellScrollArea.offsetMin = new Vector2(0f, checkoutEntryH + 16f);
            sellScrollArea.offsetMax = Vector2.zero;
            Transform sellContent = BuildScrollArea(sellScrollArea, out _, useGrid: false, showScrollbar: true);

            var sellEmptyRt = MakeRect("SellEmptyText", sellScrollArea, Vector2.zero, Vector2.one);
            var sellEmptyText = sellEmptyRt.gameObject.AddComponent<Text>();
            sellEmptyText.text = "没有能卖的东西"; sellEmptyText.font = LoadLegacyFont(); sellEmptyText.fontSize = 32;
            sellEmptyText.color = new Color(0.55f, 0.58f, 0.6f, 1f);
            sellEmptyText.alignment = TextAnchor.MiddleCenter;
            sellEmptyText.raycastTarget = false;
            sellEmptyText.gameObject.SetActive(false);

            // 出售区"去结账"入口按钮——完全照抄购物区 GoToCheckoutButton 那一份
            // (同尺寸/同风格/同一个 ToggleCheckoutView() 处理函数)，右上角数量角标
            // 换成出售清单自己的计数(sellCartCountText)。
            // 用户反馈"结账"按钮拉满整行太长了，而且要跟购物区那个按钮同一个位置——
            // 之前直接照抄了 checkoutEntryRt 的 anchoredPosition，忽略了 sellRoot 和
            // shoppingRoot 的 offsetMin 其实不一样：shoppingRoot 左/下边距是(0,0)(紧贴
            // 面板边缘)，sellRoot 是(24,24)(整体内缩24)。两边局部坐标系原点错开了24，
            // 直接搬同一个 anchoredPosition 就会带出一个24px的偏移。这里减掉这个差值，
            // 让两个按钮换算到面板坐标系下落在完全同一个位置。
            var sellCheckoutEntryRt = MakeRect("SellGoToCheckoutButton", sellRoot, new Vector2(0f, 0f), new Vector2(0f, 0f));
            sellCheckoutEntryRt.pivot = new Vector2(0.5f, 0f);
            sellCheckoutEntryRt.sizeDelta = new Vector2(rightModuleRight - rightModuleLeft, checkoutEntryH);
            sellCheckoutEntryRt.anchoredPosition = new Vector2(rightModuleCenterX - 24f, 0f);
            var sellCheckoutEntryImg = sellCheckoutEntryRt.gameObject.AddComponent<Image>();
            sellCheckoutEntryImg.color = new Color(0f, 0f, 0f, 0f);
            SkyPrisonFloatingWindowKit.AddOutline(sellCheckoutEntryRt, SkyPrisonUIPalette.ColdGreen, 2f * mul);
            var sellCheckoutEntryBtn = sellCheckoutEntryRt.gameObject.AddComponent<Button>();
            sellCheckoutEntryBtn.transition = Selectable.Transition.None;
            NoAutoNav(sellCheckoutEntryBtn);
            var sellCheckoutEntryFeedback = SkyPrisonUIButtonFeedback.Attach(sellCheckoutEntryRt.gameObject);

            if (cartIconSprite != null)
            {
                var sellCartIconRt = MakeRect("CartIcon", sellCheckoutEntryRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
                sellCartIconRt.pivot = new Vector2(0f, 0.5f);
                sellCartIconRt.sizeDelta = new Vector2(checkoutIconSize * mul, checkoutIconSize * mul);
                sellCartIconRt.anchoredPosition = new Vector2(32f * mul, 0f);
                var sellCartIconImg = sellCartIconRt.gameObject.AddComponent<Image>();
                sellCartIconImg.sprite = cartIconSprite; sellCartIconImg.preserveAspect = true; sellCartIconImg.raycastTarget = false;
                sellCartIconImg.color = SkyPrisonUIPalette.ColdGreen;
            }

            var sellCheckoutEntryLabel = MakeRect("Label", sellCheckoutEntryRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            sellCheckoutEntryLabel.text = "结账"; sellCheckoutEntryLabel.font = LoadLegacyFont(); sellCheckoutEntryLabel.fontSize = 40;
            sellCheckoutEntryLabel.alignment = TextAnchor.MiddleCenter; sellCheckoutEntryLabel.color = SkyPrisonUIPalette.ColdGreen;
            sellCheckoutEntryLabel.raycastTarget = false;

            var sellCartCountRt = MakeRect("Count", sellCheckoutEntryRt, new Vector2(1f, 1f), new Vector2(1f, 1f));
            sellCartCountRt.pivot = new Vector2(1f, 1f);
            sellCartCountRt.sizeDelta = new Vector2(64f * mul, 64f * mul);
            sellCartCountRt.anchoredPosition = new Vector2(10f * mul, 10f * mul);
            var sellCartCountBg = sellCartCountRt.gameObject.AddComponent<Image>();
            sellCartCountBg.sprite = LoadOrCreatePersistedRoundedRectSprite(64, 32);
            sellCartCountBg.type = Image.Type.Simple;
            sellCartCountBg.color = SkyPrisonUIPalette.ColdGreen;
            var sellCartCountText = MakeRect("Label", sellCartCountRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            sellCartCountText.font = LoadLegacyFont(); sellCartCountText.fontSize = 30; sellCartCountText.fontStyle = FontStyle.Bold;
            sellCartCountText.color = new Color(0.13f, 0.14f, 0.15f, 1f);
            sellCartCountText.alignment = TextAnchor.MiddleCenter;
            sellCartCountText.raycastTarget = false;

            var sellCountGraphics = new HashSet<Graphic>(sellCartCountRt.GetComponentsInChildren<Graphic>(true));
            var sellFeedbackTargets = new List<Graphic>();
            foreach (Graphic g in sellCheckoutEntryRt.GetComponentsInChildren<Graphic>(true))
                if (!sellCountGraphics.Contains(g)) sellFeedbackTargets.Add(g);
            sellCheckoutEntryFeedback.Configure(sellFeedbackTargets.ToArray());

            // ── 行模板 prefab（Instantiate用）──────────────────────────────
            EnsureFolder("Assets/_Project/Prefabs/UI/Window");
            GameObject shelfRowPrefab = BuildAndSaveShelfRowPrefab(font);
            GameObject cartRowPrefab  = BuildAndSaveCartRowPrefab(font);
            GameObject sellRowPrefab  = BuildAndSaveSellRowPrefab(font);

            var controller = root.AddComponent<ShopWindowController>();
            var so = new SerializedObject(controller);
            so.FindProperty("shopDefinition").objectReferenceValue = demoShop; // 演示数据直接烤进prefab，F6开出来就有货
            var tokenDefForShop = AssetDatabase.LoadAssetAtPath<CurrencyDefinition>("Assets/_Project/Data/Definitions/Standard/Currencies/CD_Token.asset");
            so.FindProperty("defaultCurrencyIcon").objectReferenceValue = tokenDefForShop != null ? tokenDefForShop.icon : null;
            so.FindProperty("closeButton").objectReferenceValue = panel.Find("CloseButton")?.GetComponent<Button>();
            so.FindProperty("titleLabel").objectReferenceValue = titleLabelLegacyText;
            so.FindProperty("shelfContent").objectReferenceValue = shelfContent;
            so.FindProperty("shelfRowPrefab").objectReferenceValue = shelfRowPrefab;
            so.FindProperty("normalIconMaterial").objectReferenceValue = LoadOrCreatePersistedIconFadeMaterial();
            so.FindProperty("grayscaleIconMaterial").objectReferenceValue = LoadOrCreatePersistedIconGrayscaleFadeMaterial();
            so.FindProperty("detailIcon").objectReferenceValue = detailRefs.icon;
            so.FindProperty("detailName").objectReferenceValue = detailRefs.name;
            so.FindProperty("detailTag").objectReferenceValue = detailRefs.tag;
            so.FindProperty("detailDesc").objectReferenceValue = detailRefs.desc;
            so.FindProperty("cartContent").objectReferenceValue = checkoutRefs.cartContent;
            so.FindProperty("cartRowPrefab").objectReferenceValue = cartRowPrefab;
            so.FindProperty("cartCostText").objectReferenceValue = checkoutRefs.costText;
            so.FindProperty("cartWalletText").objectReferenceValue = checkoutRefs.walletText;
            so.FindProperty("cartAfterText").objectReferenceValue = checkoutRefs.afterText;
            so.FindProperty("checkoutButton").objectReferenceValue = checkoutRefs.checkoutButton;
            so.FindProperty("checkoutLabel").objectReferenceValue = checkoutRefs.checkoutLabel;
            so.FindProperty("shoppingViewRoot").objectReferenceValue = shoppingRoot.gameObject;
            so.FindProperty("checkoutViewRoot").objectReferenceValue = checkoutRoot.gameObject;
            so.FindProperty("goToCheckoutButton").objectReferenceValue = checkoutEntryBtn;
            so.FindProperty("goToCheckoutLabel").objectReferenceValue = checkoutEntryLabel;
            so.FindProperty("cartCountText").objectReferenceValue = cartCountLegacyText;
            so.FindProperty("backToShopButton").objectReferenceValue = checkoutRefs.backToShopButton;
            so.FindProperty("backToShopLabel").objectReferenceValue = checkoutRefs.backToShopLabel;
            so.FindProperty("costLabel").objectReferenceValue = checkoutRefs.costLabel;
            so.FindProperty("walletLabel").objectReferenceValue = checkoutRefs.walletLabel;
            so.FindProperty("afterLabel").objectReferenceValue = checkoutRefs.afterLabel;
            so.FindProperty("buyTabButton").objectReferenceValue = buyTabBtn;
            so.FindProperty("buyTabLabel").objectReferenceValue = buyTabLabelText;
            so.FindProperty("sellTabButton").objectReferenceValue = sellTabBtn;
            so.FindProperty("sellTabLabel").objectReferenceValue = sellTabLabelText;
            so.FindProperty("tabUnderline").objectReferenceValue = tabUnderlineRt;
            so.FindProperty("sellViewRoot").objectReferenceValue = sellRoot.gameObject;
            so.FindProperty("sellContent").objectReferenceValue = sellContent;
            so.FindProperty("sellRowPrefab").objectReferenceValue = sellRowPrefab;
            so.FindProperty("sellEmptyText").objectReferenceValue = sellEmptyText;
            so.FindProperty("sellGoToCheckoutButton").objectReferenceValue = sellCheckoutEntryBtn;
            so.FindProperty("sellGoToCheckoutLabel").objectReferenceValue = sellCheckoutEntryLabel;
            so.FindProperty("sellCartCountText").objectReferenceValue = sellCartCountText;
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolder("Assets/Resources/UI/Window");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            if (ok)
            {
                if (AssetDatabase.CopyAsset(PrefabPath, ResMirrorPath))
                    AssetDatabase.ImportAsset(ResMirrorPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[Shop] Prefab 已创建：{PrefabPath}");
            }
            else
            {
                Debug.LogError("[Shop] SaveAsPrefabAsset 失败。");
            }

            Object.DestroyImmediate(root);
        }

        // ── 详情区 ──────────────────────────────────────────────────────────
        private struct DetailRefs
        {
            public Image icon; public Text name; public Text tag; public Text desc;
        }

        private static DetailRefs BuildDetailArea(RectTransform area, TMP_FontAsset font)
        {
            var r = new DetailRefs();

            // 用户反馈详情区图标/文字都太小——图标放大到300，名字/描述字号也跟着加大。
            var iconRt = MakeRect("Icon", area, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.sizeDelta = new Vector2(300f, 300f);
            iconRt.anchoredPosition = new Vector2(0f, -16f);
            r.icon = iconRt.gameObject.AddComponent<Image>();
            r.icon.preserveAspect = true;

            var nameRt = MakeRect("Name", area, new Vector2(0f, 1f), new Vector2(1f, 1f));
            nameRt.pivot = new Vector2(0.5f, 1f);
            nameRt.sizeDelta = new Vector2(0f, 56f);
            nameRt.anchoredPosition = new Vector2(0f, -336f);
            r.name = nameRt.gameObject.AddComponent<Text>();
            r.name.font = LoadLegacyFont(); r.name.fontSize = 40; r.name.alignment = TextAnchor.MiddleCenter; r.name.color = Color.white;

            // 品级(Lv.N，按品质上色) + 物品类型（消耗品/材料/武器等）——用户明确要求
            // 加上，跟背包那边 InventoryItemDetailPanel.BuildTagLine 是同一套文案格式，
            // 支持富文本颜色标签，Text.richText 默认就是开的不用额外设置。
            // "分类"和"Lv.N"这两行之间用户反馈挤在一起想再留点距离——这一行本身
            // 宽度不够就会自动换成两行，之前框高只留了44(单行高度)，两行文字被压
            // 得紧贴在一起还会被框裁掉。加高框、开verticalOverflow、调大lineSpacing
            // 行间倍率，两行之间才有真正呼吸的空间。
            var tagRt = MakeRect("Tag", area, new Vector2(0f, 1f), new Vector2(1f, 1f));
            tagRt.pivot = new Vector2(0.5f, 1f);
            tagRt.sizeDelta = new Vector2(0f, 90f);
            tagRt.anchoredPosition = new Vector2(0f, -396f);
            r.tag = tagRt.gameObject.AddComponent<Text>();
            r.tag.font = LoadLegacyFont(); r.tag.fontSize = 30; r.tag.alignment = TextAnchor.MiddleCenter;
            r.tag.color = new Color(0.82f, 0.84f, 0.86f, 1f);
            r.tag.verticalOverflow = VerticalWrapMode.Overflow;
            r.tag.lineSpacing = 1.6f;

            // 库存/货币/单价/数量步进/加入购物车——用户明确要求这些信息本来就该属于左侧
            // 物品卡自己（已经搬过去了），详情区只保留大图+名字+描述，不再重复摆一份。
            // 之前这里锚点只钉在顶部（没有垂直拉伸），offsetMin/offsetMax算出来的高度是
            // 负数，描述文字虽然有真实数据但框本身是坏的，显示不出来——改成上下都拉伸
            // 的锚点，offsetMin/offsetMax才是"底边距/顶边距"的正常含义。
            var descRt = MakeRect("Description", area, Vector2.zero, Vector2.one);
            descRt.offsetMin = new Vector2(0f, 24f);
            descRt.offsetMax = new Vector2(0f, -498f); // Tag加高46(90-44)，往下让出对应空间
            r.desc = descRt.gameObject.AddComponent<Text>();
            r.desc.font = LoadLegacyFont(); r.desc.fontSize = 28; r.desc.color = new Color(0.82f, 0.84f, 0.86f, 1f);
            r.desc.alignment = TextAnchor.UpperLeft; r.desc.horizontalOverflow = HorizontalWrapMode.Wrap; r.desc.verticalOverflow = VerticalWrapMode.Overflow;

            return r;
        }

        // ── 结账区 ──────────────────────────────────────────────────────────
        private struct CheckoutRefs
        {
            public Transform cartContent; public Text costText; public Text walletText; public Text afterText;
            public Text costLabel; public Text walletLabel; public Text afterLabel; // "所需"/"所持"/"结账后" 静态标签，支持语言切换需要运行时能改
            public Button checkoutButton;
            public Text checkoutLabel; public Button backToShopButton; public Text backToShopLabel;
        }

        // buttonWidth/buttonHeight：结账/返回购物两个按钮的尺寸，用户明确要求跟购物界面
        // 右下角那个大"结账"入口按钮一样大——由调用方把 checkoutEntryH 那组尺寸传进来，
        // 不在这里另起一套。
        private static CheckoutRefs BuildCheckoutArea(RectTransform area, TMP_FontAsset font, float buttonWidth, float buttonHeight)
        {
            var r = new CheckoutRefs();

            const float summaryH = 240f; // 所需/所持/结账后 三行，字号加大后需要更高
            float bottomReserve = 24f + buttonHeight + 16f + summaryH;

            var scrollArea = MakeRect("CartScrollArea", area, new Vector2(0f, 0f), new Vector2(1f, 1f));
            scrollArea.offsetMin = new Vector2(0f, bottomReserve);
            scrollArea.offsetMax = new Vector2(0f, 0f);
            // 之前 showScrollbar:false 是为了藏掉常驻显示的那条竖线，现在滚动条本身
            // 改成自动显隐了(超一页才出现)，没必要再整个不装，两个列表统一行为。
            r.cartContent = BuildScrollArea(scrollArea, out _, useGrid: false, showScrollbar: true);

            // 所需/所持/结账后——每行左边是"所需"这种标签(左对齐)，右边是货币图标+数字
            // (右对齐)，整块贴在结账区右侧（用户明确要求标签左对齐、数字前带货币图标、
            // 字号加大、颜色统一用冷绿，"结账后"上面加一条分隔线跟前两行区分开）。
            float rowH = 64f, rowGap = 10f, dividerGap = 20f;
            var summaryRt = MakeRect("PaymentSummary", area, new Vector2(0f, 0f), new Vector2(1f, 0f));
            summaryRt.pivot = new Vector2(0.5f, 0f);
            summaryRt.sizeDelta = new Vector2(0f, summaryH);
            summaryRt.anchoredPosition = new Vector2(0f, 24f + buttonHeight + 16f);

            var tokenDefForRows = AssetDatabase.LoadAssetAtPath<CurrencyDefinition>("Assets/_Project/Data/Definitions/Standard/Currencies/CD_Token.asset");
            Sprite currencyIcon = tokenDefForRows != null ? tokenDefForRows.icon : null;

            (Text label, Text value) MakeSummaryRow(string name, string label, float yFromTop)
            {
                var rowRt = MakeRect(name, summaryRt, new Vector2(0f, 1f), new Vector2(1f, 1f));
                rowRt.pivot = new Vector2(0.5f, 1f);
                rowRt.sizeDelta = new Vector2(0f, rowH);
                rowRt.anchoredPosition = new Vector2(0f, -yFromTop);

                var labelRt = MakeRect("Label", rowRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
                labelRt.pivot = new Vector2(0f, 0.5f);
                labelRt.sizeDelta = new Vector2(200f, rowH);
                labelRt.anchoredPosition = Vector2.zero;
                var labelText = labelRt.gameObject.AddComponent<Text>();
                labelText.text = label; labelText.font = LoadLegacyFont(); labelText.fontSize = 40;
                labelText.color = SkyPrisonUIPalette.ColdGreen;
                labelText.alignment = TextAnchor.MiddleLeft; labelText.raycastTarget = false;

                var groupRt = MakeRect("ValueGroup", rowRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                groupRt.pivot = new Vector2(1f, 0.5f);
                groupRt.sizeDelta = new Vector2(260f, rowH);
                groupRt.anchoredPosition = Vector2.zero;

                var iconRt = MakeRect("Icon", groupRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
                iconRt.pivot = new Vector2(0f, 0.5f);
                iconRt.sizeDelta = new Vector2(44f, 44f);
                iconRt.anchoredPosition = Vector2.zero;
                var iconImg = iconRt.gameObject.AddComponent<Image>();
                iconImg.sprite = currencyIcon; iconImg.preserveAspect = true;

                var valueRt = MakeRect("Value", groupRt, Vector2.zero, Vector2.one);
                valueRt.offsetMin = new Vector2(56f, 0f);
                valueRt.offsetMax = Vector2.zero;
                var valueText = valueRt.gameObject.AddComponent<Text>();
                valueText.font = LoadLegacyFont(); valueText.fontSize = 40; valueText.color = SkyPrisonUIPalette.ColdGreen;
                valueText.alignment = TextAnchor.MiddleRight; valueText.raycastTarget = false;
                return (labelText, valueText);
            }
            // 用户明确要求"所持"在上"所需"在下（所需那行数字带负号，读起来像"从所持里
            // 扣掉这些"，顺序也要符合这个直觉）。
            (r.walletLabel, r.walletText) = MakeSummaryRow("WalletRow", "所持", 0f);
            (r.costLabel, r.costText)     = MakeSummaryRow("CostRow", "所需", rowH + rowGap);

            // 分隔线——压在"结账后"这一行上方，跟前两行区分开（用户明确要求）。
            var dividerRt = MakeRect("Divider", summaryRt, new Vector2(0f, 1f), new Vector2(1f, 1f));
            dividerRt.pivot = new Vector2(0.5f, 1f);
            dividerRt.sizeDelta = new Vector2(0f, 2f * SkyPrisonFloatingWindowKit.StandardScaleMultiplier);
            dividerRt.anchoredPosition = new Vector2(0f, -((rowH + rowGap) * 2f - dividerGap * 0.5f));
            var dividerImg = dividerRt.gameObject.AddComponent<Image>();
            dividerImg.color = new Color(SkyPrisonUIPalette.ColdGreen.r, SkyPrisonUIPalette.ColdGreen.g, SkyPrisonUIPalette.ColdGreen.b, 0.45f);

            (r.afterLabel, r.afterText) = MakeSummaryRow("AfterRow", "结账后", (rowH + rowGap) * 2f + dividerGap);
            r.afterText.fontStyle = FontStyle.Bold;

            // 返回购物——结账界面左下角，用户明确要求要有路走回购物区，不能只能靠
            // 结完账/关窗口才能离开这个视图。跟标题栏购物车按钮/右下角"结账"按钮
            // 一样，都是调用同一个 ToggleCheckoutView() 来回切换。
            var backToShopRt = MakeRect("BackToShopButton", area, new Vector2(0f, 0f), new Vector2(0f, 0f));
            backToShopRt.pivot = new Vector2(0f, 0f);
            backToShopRt.sizeDelta = new Vector2(buttonWidth, buttonHeight);
            backToShopRt.anchoredPosition = new Vector2(0f, 24f);
            var backToShopImg = backToShopRt.gameObject.AddComponent<Image>();
            backToShopImg.color = new Color(0f, 0f, 0f, 0f);
            SkyPrisonFloatingWindowKit.AddOutline(backToShopRt, Color.white, 2f);
            r.backToShopButton = backToShopRt.gameObject.AddComponent<Button>();
            NoAutoNav(r.backToShopButton);
            SkyPrisonUIButtonFeedback.Attach(backToShopRt.gameObject);
            var backToShopLabelRt = MakeRect("Label", backToShopRt, Vector2.zero, Vector2.one);
            r.backToShopLabel = backToShopLabelRt.gameObject.AddComponent<Text>();
            r.backToShopLabel.text = "返回购物"; r.backToShopLabel.font = LoadLegacyFont(); r.backToShopLabel.fontSize = 40;
            r.backToShopLabel.alignment = TextAnchor.MiddleCenter; r.backToShopLabel.color = Color.white; r.backToShopLabel.raycastTarget = false;

            var checkoutBtnRt = MakeRect("CheckoutButton", area, new Vector2(1f, 0f), new Vector2(1f, 0f));
            checkoutBtnRt.pivot = new Vector2(1f, 0f);
            checkoutBtnRt.sizeDelta = new Vector2(buttonWidth, buttonHeight);
            checkoutBtnRt.anchoredPosition = new Vector2(0f, 24f);
            var checkoutBtnImg = checkoutBtnRt.gameObject.AddComponent<Image>();
            checkoutBtnImg.color = new Color(0f, 0f, 0f, 0f);
            SkyPrisonFloatingWindowKit.AddOutline(checkoutBtnRt, SkyPrisonUIPalette.ColdGreen, 2f);
            r.checkoutButton = checkoutBtnRt.gameObject.AddComponent<Button>();
            NoAutoNav(r.checkoutButton);
            SkyPrisonUIButtonFeedback.Attach(checkoutBtnRt.gameObject);
            var checkoutLabelRt = MakeRect("Label", checkoutBtnRt, Vector2.zero, Vector2.one);
            r.checkoutLabel = checkoutLabelRt.gameObject.AddComponent<Text>();
            r.checkoutLabel.text = "结账"; r.checkoutLabel.font = LoadLegacyFont(); r.checkoutLabel.fontSize = 40;
            r.checkoutLabel.alignment = TextAnchor.MiddleCenter; r.checkoutLabel.color = SkyPrisonUIPalette.ColdGreen; r.checkoutLabel.raycastTarget = false;

            return r;
        }

        // ── 通用 ScrollRect ───────────────────────────────────────────────
        // useGrid=true 时是"左右左右"两列卡片网格（用户要求的货架样式），false 时是
        // 结账区那种单列表单行。
        private static Transform BuildScrollArea(RectTransform area, out ScrollRect scrollRect, bool useGrid = false, bool showScrollbar = true)
        {
            scrollRect = area.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = MakeRect("Viewport", area, Vector2.zero, Vector2.one);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            viewport.gameObject.AddComponent<Image>().color = Color.white;

            var content = MakeRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f));
            content.pivot = new Vector2(0.5f, 1f);

            if (useGrid)
            {
                var glg = content.gameObject.AddComponent<GridLayoutGroup>();
                glg.cellSize = new Vector2(ShelfCardW, ShelfCardH);
                glg.spacing = new Vector2(ShelfGridSpacing, ShelfGridSpacing);
                int pad = Mathf.RoundToInt(ShelfGridPadding);
                glg.padding = new RectOffset(pad, pad, pad, pad);
                glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                glg.constraintCount = ShelfColumns; // 左右左右两列
                var gridFitter = content.gameObject.AddComponent<ContentSizeFitter>();
                gridFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            else
            {
                var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
                // childForceExpandWidth 单独设true根本不生效——Unity只有在
                // childControlWidth=true时才会真的把子物体宽度改写成撑满容器，
                // 之前一直没设这个，导致结账购物车行的sizeDelta.x停留在创建时的0，
                // 右侧锚点(pivot=1)的数量步进器/合计文字全部无声无息地跑没了。
                vlg.childControlWidth = true;
                vlg.childForceExpandHeight = false;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.spacing = 8f;
                vlg.padding = new RectOffset(4, 4, 4, 4);
                var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            scrollRect.viewport = viewport;
            scrollRect.content = content;

            // 货架的滚动条紧贴在货架区右边缘，正好卡在货架/详情分割处，之前用纯白8px宽
            // 太显眼，看起来像一条粗分割线——调细调暗，只在真正需要滚动时才明显。
            // 之前顶部/底部都是0留白，货架区顶部紧贴标题栏看着还好，底部直接顶到
            // 窗口下边缘——用户明确要求底部也留一份跟标准面板内容边距(24)一致的
            // 空间，不能只有顶部好看。
            // 用户明确要求"超过一页才显示滚动条，不足一页就不显示"——AutoHide
            // AndExpandViewport 是 Unity 内置的这个行为(内容够放的时候连滚动条占的
            // 那几像素宽度都会让viewport扩展回去补上，不是只是隐藏但还占位)。
            // 结账购物车列表之前是"showScrollbar=false"整个不装滚动条(治标：把常驻
            // 显示的那条线藏掉了)，现在改成同一套自动显隐，两边行为统一。
            if (showScrollbar)
            {
                SkyPrisonUIScrollbar.AttachVertical(scrollRect, area, new Color(1f, 1f, 1f, 0.5f),
                    rightMargin: 4f, bottomMargin: 24f, width: 4f,
                    visibility: ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport);
            }

            return content;
        }

        // ── 行模板：货架行/购物车行，各自存成独立小prefab（Instantiate用）───────
        private static GameObject BuildAndSaveShelfRowPrefab(TMP_FontAsset font)
        {
            // 竖版卡片（参照用户发的截图：图标占满上半，名字/库存在下面，价格用
            // 高亮圆角色块压在卡片底部），卡片本身也带圆角。
            var go = new GameObject("ShopShelfRow", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(ShelfCardW, ShelfCardH);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = ShelfCardW; le.preferredHeight = ShelfCardH;

            Sprite roundedSprite = LoadOrCreatePersistedRoundedRectSprite(64, 14);
            var bg = go.AddComponent<Image>();
            bg.sprite = roundedSprite; bg.type = Image.Type.Sliced;
            bg.color = new Color(1f, 1f, 1f, 0.06f);
            go.AddComponent<CanvasGroup>(); // 之前"缺货置灰"代码找的就是这个组件，一直没挂，那段代码其实从没生效过
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            NoAutoNav(btn);
            // 不挂 SkyPrisonUIButtonFeedback——它会递归收集卡片下所有子图形（图标/文字/
            // 价格色块全部算进去）悬停时整个染绿，用户反馈这不是想要的效果（只想要
            // 描边高亮，图2那种），描边已经用 HoverOutline 单独做了。

            const float margin = 6f;
            float iconSize = ShelfCardW - margin * 2f; // 正方形，横向撑满卡片
            var iconRt = MakeRect("Icon", rt, new Vector2(0f, 1f), new Vector2(0f, 1f));
            iconRt.pivot = new Vector2(0f, 1f);
            iconRt.sizeDelta = new Vector2(iconSize, iconSize);
            iconRt.anchoredPosition = new Vector2(margin, -margin);
            var icon = iconRt.gameObject.AddComponent<Image>();
            icon.name = "Icon";

            // 图标底部渐隐——用户明确指出"是图标自己淡出变透明，不是叠一层深色遮罩"
            // （之前那版叠深色渐变贴图，看着像贴了块脏色块）。改成给图标本身的Image
            // 换一个按UV.y线性衰减alpha的shader，图标自己的像素在底部真的透明掉。
            var iconFadeMat = LoadOrCreatePersistedIconFadeMaterial();
            if (iconFadeMat != null) icon.material = iconFadeMat;

            float belowIconY = -(margin + iconSize); // 图标下边缘（从卡片顶部算的负值）

            // 库存数量挪到图标右上角当角标（用户明确要求），白色字体，压在图标上面。
            var stockRt = MakeRect("StockText", rt, new Vector2(1f, 1f), new Vector2(1f, 1f));
            stockRt.pivot = new Vector2(1f, 1f);
            stockRt.sizeDelta = new Vector2(iconSize * 0.6f, 30f);
            stockRt.anchoredPosition = new Vector2(-margin - 6f, -margin - 6f);
            var stockText = stockRt.gameObject.AddComponent<Text>();
            stockText.font = LoadLegacyFont(); stockText.fontSize = 24; stockText.color = Color.white;
            stockText.alignment = TextAnchor.UpperRight;
            stockText.verticalOverflow = VerticalWrapMode.Overflow;
            var stockShadow = stockRt.gameObject.AddComponent<Shadow>();
            stockShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            stockShadow.effectDistance = new Vector2(1f, -1f); // 压在图标上，加个投影别被图标花纹吃掉

            // 名字居中——图标底部现在是图标自己淡出，往上挪一截让名字"浮"在淡出区域
            // 里面（用户明确要求：图标自己淡出后名字可以再上移、再放大）。
            // 之前框高只按单行(56)留，日文/英文名字变长换行后靠verticalOverflow溢出
            // 到框外，正好撞上下面的数量步进胶囊——框加高到能装两行，整体再往上提一点
            // (nameOverlapIcon 40→64)腾出空间，两行文字就不会再往下溢出到步进器上。
            const float nameH = 88f, nameOverlapIcon = 64f;
            var nameRt = MakeRect("ItemName", rt, new Vector2(0f, 1f), new Vector2(1f, 1f));
            nameRt.pivot = new Vector2(0f, 1f);
            nameRt.offsetMax = new Vector2(-margin, belowIconY + nameOverlapIcon);
            nameRt.offsetMin = new Vector2(margin, belowIconY + nameOverlapIcon - nameH);
            var nameText = nameRt.gameObject.AddComponent<Text>();
            nameText.font = LoadLegacyFont(); nameText.fontSize = 36; nameText.color = Color.white;
            nameText.alignment = TextAnchor.UpperCenter; // 用户要求名字居中
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            nameText.verticalOverflow = VerticalWrapMode.Overflow;

            // 价格——高亮色块压在卡片底部，深灰色。数字整个在色块里居中显示（不再是
            // 跟图标绑一起的小组居中），货币图标单独钉在色块左边（用户明确要求这个
            // 布局），数字字号加大，色块也加高。
            const float bottomMargin = 6f, qtyRowH = 58f, rowGap = 18f, priceBarH = 64f, priceBarSideInset = 4f;
            const float priceBarBottom = bottomMargin;
            const float qtyRowBottom = priceBarBottom + priceBarH + rowGap;

            // 价格色块改用白色填充+"挖空"数字（用户明确要求）：色块圆角比卡片本身
            // 小一号（单独一个更小半径的圆角贴图），数字颜色跟卡片背景深色调一致，
            // 造成"白底上挖了个洞露出底色数字"的错觉。
            Sprite priceBarSprite = LoadOrCreatePersistedRoundedRectSprite(64, 8);
            var priceBarRt = MakeRect("PriceRow", rt, new Vector2(0f, 0f), new Vector2(1f, 0f));
            priceBarRt.pivot = new Vector2(0.5f, 0f);
            priceBarRt.offsetMin = new Vector2(priceBarSideInset, priceBarBottom);
            priceBarRt.offsetMax = new Vector2(-priceBarSideInset, priceBarBottom + priceBarH);
            var priceBarImg = priceBarRt.gameObject.AddComponent<Image>();
            priceBarImg.sprite = priceBarSprite; priceBarImg.type = Image.Type.Sliced;
            priceBarImg.color = new Color(0.82f, 0.82f, 0.83f, 1f); // 淡灰色填充（用户反馈试试这个）
            var priceBarBtn = priceBarRt.gameObject.AddComponent<Button>(); // 用户要求点价格直接加入购物车
            priceBarBtn.transition = Selectable.Transition.None;
            NoAutoNav(priceBarBtn);
            // 挪位置治标不治本——用户直接指出应该是"裁剪"：给价格色块本身挂 Mask，
            // 用它自己那张圆角矩形贴图的 alpha 形状当裁剪范围，角标三角形贴死在
            // 右上角也没关系，超出圆角轮廓的部分会被这个 Mask 自动裁掉。
            // showMaskGraphic=true 让色块自己正常显示（Mask 默认会隐藏自己的图形）。
            var priceBarMask = priceBarRt.gameObject.AddComponent<Mask>();
            priceBarMask.showMaskGraphic = true;
            // 之前干脆不挂 SkyPrisonUIButtonFeedback——hover变绿是不需要了，但用户
            // 反馈这样点了完全没有"激活"的交互感，太生硬。重新挂上，但关掉hover染色
            // (enableHoverTint=false)，只保留点击那一下的冷绿闪光反馈；同时关掉这个
            // 组件自带的确认音效(playClickSE=false)，音效交给DoAddToCart自己按
            // 加购成功/库存不足的实际结果决定播哪个，不然会双重播放。
            var priceBarFeedback = priceBarRt.gameObject.AddComponent<SkyPrisonUIButtonFeedback>();
            var priceBarFeedbackSo = new SerializedObject(priceBarFeedback);
            priceBarFeedbackSo.FindProperty("enableHoverTint").boolValue = false;
            priceBarFeedbackSo.FindProperty("playClickSE").boolValue = false;
            priceBarFeedbackSo.ApplyModifiedPropertiesWithoutUndo();
            priceBarFeedback.Configure(priceBarImg);

            Sprite priceCartIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UIUX/Window/Styles/Default/Sprites/UIWindow_Default_Cart.png");
            if (priceCartIconSprite != null)
            {
                const float badgeSize = 56f;
                // 贴死在色块右上角正角就行——超出圆角轮廓的部分交给上面新加的
                // Mask(priceBarRt) 自动裁掉，不用再靠挪位置去"躲"圆角。
                var addBadgeRt = MakeRect("AddToCartBadge", priceBarRt, new Vector2(1f, 1f), new Vector2(1f, 1f));
                addBadgeRt.pivot = new Vector2(1f, 1f);
                addBadgeRt.sizeDelta = new Vector2(badgeSize, badgeSize);
                addBadgeRt.anchoredPosition = Vector2.zero;
                addBadgeRt.gameObject.SetActive(false); // 默认隐藏，悬停才显示
                var addBadgeBg = addBadgeRt.gameObject.AddComponent<Image>();
                addBadgeBg.sprite = LoadOrCreatePersistedCornerTriangleSprite(64); // 三角折角丝带，不是圆
                addBadgeBg.type = Image.Type.Simple;
                addBadgeBg.color = SkyPrisonUIPalette.ColdGreen;
                addBadgeBg.raycastTarget = false;

                // 图标往三角形色块的重心(右上角那一侧)偏，不能摆在色块几何中心——
                // 色块本身只有右上半个正方形有颜色，摆在正中心一半会飘在透明区域上。
                // 用户反馈图标可以再大一些，从20加到30。
                var addBadgeIconRt = MakeRect("Icon", addBadgeRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                addBadgeIconRt.pivot = new Vector2(0.5f, 0.5f);
                addBadgeIconRt.sizeDelta = new Vector2(30f, 30f);
                addBadgeIconRt.anchoredPosition = new Vector2(badgeSize * 0.14f, badgeSize * 0.14f);
                var addBadgeIcon = addBadgeIconRt.gameObject.AddComponent<Image>();
                addBadgeIcon.sprite = priceCartIconSprite; addBadgeIcon.preserveAspect = true;
                addBadgeIcon.color = new Color(0.08f, 0.1f, 0.09f, 1f); // 深色图标压在绿底上，跟其它绿底深字一致走深色对比
                addBadgeIcon.raycastTarget = false;
            }

            var priceIconRt = MakeRect("PriceIcon", priceBarRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            priceIconRt.pivot = new Vector2(0f, 0.5f);
            priceIconRt.sizeDelta = new Vector2(52f, 52f);
            priceIconRt.anchoredPosition = new Vector2(18f, 0f);
            var priceIcon = priceIconRt.gameObject.AddComponent<Image>();
            priceIcon.preserveAspect = true;

            var priceRt = MakeRect("ItemPrice", priceBarRt, Vector2.zero, Vector2.one);
            priceRt.offsetMin = Vector2.zero; priceRt.offsetMax = Vector2.zero;
            var priceText = priceRt.gameObject.AddComponent<Text>();
            priceText.font = LoadLegacyFont(); priceText.fontSize = 34;
            priceText.color = new Color(0.13f, 0.14f, 0.15f, 1f); // 挖空效果——跟卡片深色背景同色
            priceText.alignment = TextAnchor.MiddleCenter; priceText.fontStyle = FontStyle.Bold; // 数字在色块里居中
            priceText.horizontalOverflow = HorizontalWrapMode.Overflow; // 数字位数多也不会被截断
            priceText.verticalOverflow = VerticalWrapMode.Overflow;

            // 数量步进——只在鼠标聚焦这张卡片时才显示（ShopShelfRowHover 控制显隐），
            // 放在价格色块上面。"加购"独立按钮已经去掉了——点价格色块本身就会加入
            // 购物车，这个按钮是多余的（用户明确要求删掉）。
            var quickAddRt = MakeRect("QuickAddButton", rt, new Vector2(0f, 0f), new Vector2(1f, 0f));
            quickAddRt.pivot = new Vector2(0.5f, 0f);
            quickAddRt.sizeDelta = new Vector2(0f, qtyRowH);
            quickAddRt.anchoredPosition = new Vector2(0f, qtyRowBottom);

            // 数量选择器胶囊——用户反馈还要再大一圈，这次整体加大；中间的数字改成
            // 真正可输入的数字框（InputField），不再只能靠点"-"/"+"一格格调，可以
            // 直接打字输入目标数量。
            const float capsuleW = 200f;
            Sprite ringSprite = LoadOrCreatePersistedRingSprite(64, (int)(qtyRowH / 2f), 3);

            var capsuleRt = MakeRect("QtyCapsule", quickAddRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            capsuleRt.pivot = new Vector2(0.5f, 0.5f);
            capsuleRt.sizeDelta = new Vector2(capsuleW, qtyRowH);
            capsuleRt.anchoredPosition = Vector2.zero;

            var capsuleOuterRt = MakeRect("Ring", capsuleRt, Vector2.zero, Vector2.one);
            var capsuleOuterImg = capsuleOuterRt.gameObject.AddComponent<Image>();
            capsuleOuterImg.sprite = ringSprite; capsuleOuterImg.type = Image.Type.Sliced;
            capsuleOuterImg.color = Color.white;
            capsuleOuterImg.raycastTarget = false;

            var qaMinusRt = MakeRect("QAMinus", capsuleRt, new Vector2(0f, 0f), new Vector2(0.3f, 1f));
            qaMinusRt.offsetMin = Vector2.zero; qaMinusRt.offsetMax = Vector2.zero;
            qaMinusRt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var qaMinusBtn = qaMinusRt.gameObject.AddComponent<Button>();
            NoAutoNav(qaMinusBtn);
            SkyPrisonUIButtonFeedback.Attach(qaMinusRt.gameObject); // 之前这版漏挂了，"-"点了没反馈，用户反馈"不像按钮"
            var qaMinusLabel = MakeRect("Label", qaMinusRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            qaMinusLabel.text = "-"; qaMinusLabel.font = LoadLegacyFont(); qaMinusLabel.fontSize = 34;
            qaMinusLabel.alignment = TextAnchor.MiddleCenter; qaMinusLabel.color = Color.white; qaMinusLabel.raycastTarget = false;

            var qaQtyRt = MakeRect("QAQty", capsuleRt, new Vector2(0.3f, 0f), new Vector2(0.7f, 1f));
            qaQtyRt.offsetMin = new Vector2(2f, 4f); qaQtyRt.offsetMax = new Vector2(-2f, -4f);
            var qaQtyBg = qaQtyRt.gameObject.AddComponent<Image>();
            qaQtyBg.color = new Color(1f, 1f, 1f, 0f); // 没聚焦时透明，聚焦时由ShopQtyInputFocusHighlight亮出来
            var qaQtyInput = qaQtyRt.gameObject.AddComponent<InputField>();
            qaQtyInput.contentType = InputField.ContentType.IntegerNumber;
            qaQtyInput.characterLimit = 4;
            qaQtyInput.lineType = InputField.LineType.SingleLine;
            NoAutoNav(qaQtyInput);

            var qaQtyTextRt = MakeRect("Text", qaQtyRt, Vector2.zero, Vector2.one);
            qaQtyTextRt.offsetMin = new Vector2(4f, 0f); qaQtyTextRt.offsetMax = new Vector2(-4f, 0f);
            var qaQtyText = qaQtyTextRt.gameObject.AddComponent<Text>();
            qaQtyText.text = "1"; qaQtyText.font = LoadLegacyFont(); qaQtyText.fontSize = 26;
            qaQtyText.alignment = TextAnchor.MiddleCenter; qaQtyText.color = Color.white; qaQtyText.fontStyle = FontStyle.Bold;
            qaQtyText.supportRichText = false;
            qaQtyInput.textComponent = qaQtyText;
            qaQtyInput.text = "1"; // InputField自己的text才是真正的数据源，只改子Text组件的话
                                    // 会在InputField初始化时被自己内部的空字符串覆盖掉，导致显示成空白
                                    // （用户反馈"至少是1"，其实是这个默认值没能正确显示出来）。
            var qaQtyFocus = qaQtyRt.gameObject.AddComponent<ShopQtyInputFocusHighlight>();
            var qaQtyFocusSo = new SerializedObject(qaQtyFocus);
            qaQtyFocusSo.FindProperty("background").objectReferenceValue = qaQtyBg;
            qaQtyFocusSo.ApplyModifiedPropertiesWithoutUndo();

            var qaPlusRt = MakeRect("QAPlus", capsuleRt, new Vector2(0.7f, 0f), new Vector2(1f, 1f));
            qaPlusRt.offsetMin = Vector2.zero; qaPlusRt.offsetMax = Vector2.zero;
            qaPlusRt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var qaPlusBtn = qaPlusRt.gameObject.AddComponent<Button>();
            NoAutoNav(qaPlusBtn);
            SkyPrisonUIButtonFeedback.Attach(qaPlusRt.gameObject); // 同上，"+"也补上反馈
            var qaPlusLabel = MakeRect("Label", qaPlusRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            qaPlusLabel.text = "+"; qaPlusLabel.font = LoadLegacyFont(); qaPlusLabel.fontSize = 34;
            qaPlusLabel.alignment = TextAnchor.MiddleCenter; qaPlusLabel.color = Color.white; qaPlusLabel.raycastTarget = false;

            quickAddRt.gameObject.SetActive(false); // 默认隐藏，鼠标聚焦时由 ShopShelfRowHover 显示

            // 鼠标放上去卡片描绿框（跟光标高亮同色，用户明确要求），默认隐藏。
            var hoverOutlineRt = MakeRect("HoverOutline", rt, Vector2.zero, Vector2.one);
            hoverOutlineRt.offsetMin = Vector2.zero; hoverOutlineRt.offsetMax = Vector2.zero;
            SkyPrisonFloatingWindowKit.AddOutline(hoverOutlineRt, SkyPrisonUIPalette.ColdGreen, 2f);
            hoverOutlineRt.gameObject.SetActive(false);

            go.AddComponent<ShopShelfRowHover>();

            // 售罄——居中盖在卡片上的文字（用户明确要求），默认隐藏，售罄时
            // ShopWindowController 会一起把图标切成灰度材质+卡片整体调暗。
            var soldOutRt = MakeRect("SoldOutText", rt, Vector2.zero, Vector2.one);
            soldOutRt.offsetMin = Vector2.zero; soldOutRt.offsetMax = Vector2.zero;
            var soldOutText = soldOutRt.gameObject.AddComponent<Text>();
            soldOutText.text = "售罄"; soldOutText.font = LoadLegacyFont(); soldOutText.fontSize = 44;
            soldOutText.fontStyle = FontStyle.Bold;
            soldOutText.color = Color.white;
            soldOutText.alignment = TextAnchor.MiddleCenter;
            soldOutText.raycastTarget = false;
            var soldOutShadow = soldOutRt.gameObject.AddComponent<Shadow>();
            soldOutShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            soldOutShadow.effectDistance = new Vector2(2f, -2f);
            // 卡片整体调暗是靠卡片根节点的CanvasGroup.alpha=0.4实现的，"售罄"文字是
            // 它的子物体，本来也会被一起拖到40%透明度，跟后面被压暗的名字文字糊在
            // 一起分不清——用户明确要求"售罄"文字本身保持纯白清晰。给它自己单独挂
            // 一个CanvasGroup并勾ignoreParentGroups，脱离父级CanvasGroup的透明度影响。
            var soldOutOwnCg = soldOutRt.gameObject.AddComponent<CanvasGroup>();
            soldOutOwnCg.alpha = 1f;
            soldOutOwnCg.ignoreParentGroups = true;
            soldOutRt.gameObject.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(go, ShelfRowPrefabPath);
            Object.DestroyImmediate(go);
            return AssetDatabase.LoadAssetAtPath<GameObject>(ShelfRowPrefabPath);
        }

        private static GameObject BuildAndSaveCartRowPrefab(TMP_FontAsset font)
        {
            // 用户反馈：字太小(至少要跟"所需/所持"那组40号字一样大)，左边缺图标，
            // 数量要能直接用"- N +"调（减到0直接从列表移除，不用再点单独的×）。
            // 整行加高、字号全面放大，给图标+步进器腾出空间。
            const float rowH = 116f; // 图标/名字放大后行高跟着加高，不然图标顶到行边缘
            var go = new GameObject("ShopCartRow", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(0f, rowH);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = rowH; le.flexibleWidth = 1f;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.04f);

            // 整行本身也是一个Button——纯粹给手柄导航当目标用(用户明确要求"焦点该
            // 落在整条上，不该非得精确停在-/+上")，鼠标点整行目前没有对应动作，
            // onClick留空即可，不影响鼠标直接点"-"/"+"/输入框这些子控件。
            var rowBtn = go.AddComponent<Button>();
            rowBtn.transition = Selectable.Transition.None;
            NoAutoNav(rowBtn);

            // 图标/名字用户反馈还能再放大1.3~1.4倍——72→100(约1.39x)，40号字→54号(1.35x)。
            var iconRt = MakeRect("Icon", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.sizeDelta = new Vector2(100f, 100f);
            iconRt.anchoredPosition = new Vector2(12f, 0f);
            var iconImg = iconRt.gameObject.AddComponent<Image>();
            iconImg.preserveAspect = true;

            var nameRt = MakeRect("CartItemName", rt, new Vector2(0f, 0f), new Vector2(0f, 1f));
            nameRt.pivot = new Vector2(0f, 0.5f);
            nameRt.sizeDelta = new Vector2(420f, 0f);
            nameRt.anchoredPosition = new Vector2(128f, 0f);
            var nameText = nameRt.gameObject.AddComponent<Text>();
            nameText.font = LoadLegacyFont(); nameText.fontSize = 54; nameText.color = Color.white;
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow; nameText.verticalOverflow = VerticalWrapMode.Overflow;

            // 数量步进——"- N +" 靠右摆一组，跟货架卡片的QtyCapsule同一套视觉语言。
            // 减到0直接从购物车里移除这一行（用户明确要求），不用另外找×按钮。
            // 之前这组跟右边的合计(CartItemTotal)锚点距离没算够，两块重叠在了一起，
            // "+"和数字被合计文字盖住看不见——现在两块之间留足间距，不再重叠。
            const float capsuleW = 220f, capsuleH = 64f;
            var qtyGroupRt = MakeRect("QtyGroup", rt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            qtyGroupRt.pivot = new Vector2(1f, 0.5f);
            qtyGroupRt.sizeDelta = new Vector2(capsuleW, capsuleH);
            // 用户反馈数量往左挪一些，给右边价格数字留更多空间（数字位数一多就不够放）。
            qtyGroupRt.anchoredPosition = new Vector2(-340f, 0f);
            Sprite ringSprite = LoadOrCreatePersistedRingSprite(64, (int)(capsuleH / 2f), 3);
            var ringImg = MakeRect("Ring", qtyGroupRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            ringImg.sprite = ringSprite; ringImg.type = Image.Type.Sliced; ringImg.color = Color.white; ringImg.raycastTarget = false;

            var qtyMinusRt = MakeRect("QtyMinus", qtyGroupRt, new Vector2(0f, 0f), new Vector2(0.3f, 1f));
            qtyMinusRt.offsetMin = Vector2.zero; qtyMinusRt.offsetMax = Vector2.zero;
            qtyMinusRt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var qtyMinusBtn = qtyMinusRt.gameObject.AddComponent<Button>();
            NoAutoNav(qtyMinusBtn);
            SkyPrisonUIButtonFeedback.Attach(qtyMinusRt.gameObject);
            var qtyMinusLabel = MakeRect("Label", qtyMinusRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            qtyMinusLabel.text = "-"; qtyMinusLabel.font = LoadLegacyFont(); qtyMinusLabel.fontSize = 34;
            qtyMinusLabel.alignment = TextAnchor.MiddleCenter; qtyMinusLabel.color = Color.white; qtyMinusLabel.raycastTarget = false;

            // 之前是纯Text，用户反馈"数字看不见，也没法输入"——数字看不见是因为
            // ShopWindowController那边SetChildText查的时候漏写了"QtyGroup/"这层路径
            // 前缀(Transform.Find不带斜杠只查直接子物体)，值其实从来没被设进来过；
            // "没法输入"是真的功能缺失，改成InputField，跟货架卡片QAQty同一套做法。
            var qtyRt = MakeRect("CartItemQty", qtyGroupRt, new Vector2(0.3f, 0f), new Vector2(0.7f, 1f));
            qtyRt.offsetMin = Vector2.zero; qtyRt.offsetMax = Vector2.zero;
            var qtyBg = qtyRt.gameObject.AddComponent<Image>();
            qtyBg.color = new Color(0f, 0f, 0f, 0f);
            var qtyInput = qtyRt.gameObject.AddComponent<InputField>();
            qtyInput.contentType = InputField.ContentType.IntegerNumber;
            qtyInput.characterLimit = 4;
            qtyInput.lineType = InputField.LineType.SingleLine;
            NoAutoNav(qtyInput);

            var qtyTextRt = MakeRect("Text", qtyRt, Vector2.zero, Vector2.one);
            qtyTextRt.offsetMin = new Vector2(4f, 0f); qtyTextRt.offsetMax = new Vector2(-4f, 0f);
            var qtyText = qtyTextRt.gameObject.AddComponent<Text>();
            qtyText.text = "1"; qtyText.font = LoadLegacyFont(); qtyText.fontSize = 34; qtyText.color = Color.white;
            qtyText.alignment = TextAnchor.MiddleCenter; qtyText.fontStyle = FontStyle.Bold;
            qtyInput.textComponent = qtyText;
            qtyInput.text = "1"; // InputField自己的text才是真正数据源，只设子Text会被内部初始化覆盖成空白

            var qtyPlusRt = MakeRect("QtyPlus", qtyGroupRt, new Vector2(0.7f, 0f), new Vector2(1f, 1f));
            qtyPlusRt.offsetMin = Vector2.zero; qtyPlusRt.offsetMax = Vector2.zero;
            qtyPlusRt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var qtyPlusBtn = qtyPlusRt.gameObject.AddComponent<Button>();
            NoAutoNav(qtyPlusBtn);
            SkyPrisonUIButtonFeedback.Attach(qtyPlusRt.gameObject);
            var qtyPlusLabel = MakeRect("Label", qtyPlusRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            qtyPlusLabel.text = "+"; qtyPlusLabel.font = LoadLegacyFont(); qtyPlusLabel.fontSize = 34;
            qtyPlusLabel.alignment = TextAnchor.MiddleCenter; qtyPlusLabel.color = Color.white; qtyPlusLabel.raycastTarget = false;

            // 合计——用户明确要求货币用图标表示，不要显示"token"这种货币ID文字。
            // 跟货架价格条的图标+数字是同一套做法。
            var totalGroupRt = MakeRect("TotalGroup", rt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            totalGroupRt.pivot = new Vector2(1f, 0.5f);
            totalGroupRt.sizeDelta = new Vector2(260f, 56f); // 数量往左挪出的空间顺便给价格数字留够
            totalGroupRt.anchoredPosition = new Vector2(-24f, 0f);

            var totalIconRt = MakeRect("TotalIcon", totalGroupRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            totalIconRt.pivot = new Vector2(0f, 0.5f);
            totalIconRt.sizeDelta = new Vector2(44f, 44f);
            totalIconRt.anchoredPosition = Vector2.zero;
            var totalIconImg = totalIconRt.gameObject.AddComponent<Image>();
            totalIconImg.preserveAspect = true;

            var totalRt = MakeRect("CartItemTotal", totalGroupRt, Vector2.zero, Vector2.one);
            totalRt.offsetMin = new Vector2(56f, 0f); totalRt.offsetMax = Vector2.zero;
            var totalText = totalRt.gameObject.AddComponent<Text>();
            totalText.font = LoadLegacyFont(); totalText.fontSize = 38; totalText.color = SkyPrisonUIPalette.ColdGreen;
            totalText.alignment = TextAnchor.MiddleRight;
            totalText.horizontalOverflow = HorizontalWrapMode.Overflow;

            PrefabUtility.SaveAsPrefabAsset(go, CartRowPrefabPath);
            Object.DestroyImmediate(go);
            return AssetDatabase.LoadAssetAtPath<GameObject>(CartRowPrefabPath);
        }

        private static GameObject BuildAndSaveSellRowPrefab(TMP_FontAsset font)
        {
            // 出售行——用户明确要求"不能一点就把一组全卖掉，要跟购买一样的UI逻辑"：
            // 加一个"- N +"数量步进(跟购物车行QtyGroup同一套胶囊控件)，选好数量后
            // 点"加入"只是加进出售清单，真正的扣除要到结账页点"确认出售"才执行，
            // 跟购买(货架选数量→加购物车→结账确认)完全对称。
            const float rowH = 116f;
            var go = new GameObject("ShopSellRow", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(0f, rowH);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = rowH; le.flexibleWidth = 1f;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.04f);

            // 整行本身也是一个Button——手柄导航目标，A键=加入(跟子物体SellButton
            // 同一个动作，见ShopWindowController.BindSellRow)。用户明确要求"焦点该
            // 落在整条上，不该非得精确停在-/+/加入这几个小控件上"。
            var rowBtn = go.AddComponent<Button>();
            rowBtn.transition = Selectable.Transition.None;
            NoAutoNav(rowBtn);

            var iconRt = MakeRect("Icon", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.sizeDelta = new Vector2(100f, 100f);
            iconRt.anchoredPosition = new Vector2(12f, 0f);
            var iconImg = iconRt.gameObject.AddComponent<Image>();
            iconImg.preserveAspect = true;

            var nameRt = MakeRect("SellItemName", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            nameRt.pivot = new Vector2(0f, 0.5f);
            nameRt.sizeDelta = new Vector2(300f, 44f);
            nameRt.anchoredPosition = new Vector2(128f, 16f);
            var nameText = nameRt.gameObject.AddComponent<Text>();
            nameText.font = LoadLegacyFont(); nameText.fontSize = 44; nameText.color = Color.white;
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow; nameText.verticalOverflow = VerticalWrapMode.Overflow;

            // "拥有×N"——小字副标题，压在名字下面，纯提示信息，不参与交互。
            var qtyRt = MakeRect("SellItemQty", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            qtyRt.pivot = new Vector2(0f, 0.5f);
            qtyRt.sizeDelta = new Vector2(300f, 32f);
            qtyRt.anchoredPosition = new Vector2(128f, -26f);
            var qtyText = qtyRt.gameObject.AddComponent<Text>();
            qtyText.font = LoadLegacyFont(); qtyText.fontSize = 26; qtyText.color = new Color(0.65f, 0.67f, 0.69f, 1f);
            qtyText.alignment = TextAnchor.MiddleLeft;
            qtyText.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 数量步进——跟购物车行QtyGroup同一套"环形胶囊 + - / 输入框 / +"做法，
            // 默认选中数量=1，上限=玩家实际持有数量(运行时按 entry.count 夹范围)。
            const float capsuleW = 220f, capsuleH = 64f;
            var qtyGroupRt = MakeRect("QtyGroup", rt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            qtyGroupRt.pivot = new Vector2(1f, 0.5f);
            qtyGroupRt.sizeDelta = new Vector2(capsuleW, capsuleH);
            // 用户反馈数量胶囊跟合计价格块之间挤在一起重叠了——三块(数量/合计/加入按钮)
            // 都是pivot=1从右边缘算距离，之前的x值算出来右边两块之间只留了负间距，
            // 现在每块之间都留足24px真实间隙，不再靠"看起来数字对得上"去猜。
            qtyGroupRt.anchoredPosition = new Vector2(-432f, 0f);
            Sprite ringSprite = LoadOrCreatePersistedRingSprite(64, (int)(capsuleH / 2f), 3);
            var ringImg = MakeRect("Ring", qtyGroupRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            ringImg.sprite = ringSprite; ringImg.type = Image.Type.Sliced; ringImg.color = Color.white; ringImg.raycastTarget = false;

            var qtyMinusRt = MakeRect("QtyMinus", qtyGroupRt, new Vector2(0f, 0f), new Vector2(0.3f, 1f));
            qtyMinusRt.offsetMin = Vector2.zero; qtyMinusRt.offsetMax = Vector2.zero;
            qtyMinusRt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var qtyMinusBtn = qtyMinusRt.gameObject.AddComponent<Button>();
            NoAutoNav(qtyMinusBtn);
            SkyPrisonUIButtonFeedback.Attach(qtyMinusRt.gameObject);
            var qtyMinusLabel = MakeRect("Label", qtyMinusRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            qtyMinusLabel.text = "-"; qtyMinusLabel.font = LoadLegacyFont(); qtyMinusLabel.fontSize = 34;
            qtyMinusLabel.alignment = TextAnchor.MiddleCenter; qtyMinusLabel.color = Color.white; qtyMinusLabel.raycastTarget = false;

            var qtyInputRt = MakeRect("SellQty", qtyGroupRt, new Vector2(0.3f, 0f), new Vector2(0.7f, 1f));
            qtyInputRt.offsetMin = Vector2.zero; qtyInputRt.offsetMax = Vector2.zero;
            var qtyBg = qtyInputRt.gameObject.AddComponent<Image>();
            qtyBg.color = new Color(0f, 0f, 0f, 0f);
            var qtyInput = qtyInputRt.gameObject.AddComponent<InputField>();
            qtyInput.contentType = InputField.ContentType.IntegerNumber;
            qtyInput.characterLimit = 4;
            qtyInput.lineType = InputField.LineType.SingleLine;
            NoAutoNav(qtyInput);

            var qtyInputTextRt = MakeRect("Text", qtyInputRt, Vector2.zero, Vector2.one);
            qtyInputTextRt.offsetMin = new Vector2(4f, 0f); qtyInputTextRt.offsetMax = new Vector2(-4f, 0f);
            var qtyInputText = qtyInputTextRt.gameObject.AddComponent<Text>();
            qtyInputText.text = "1"; qtyInputText.font = LoadLegacyFont(); qtyInputText.fontSize = 34; qtyInputText.color = Color.white;
            qtyInputText.alignment = TextAnchor.MiddleCenter; qtyInputText.fontStyle = FontStyle.Bold;
            qtyInput.textComponent = qtyInputText;
            qtyInput.text = "1";

            var qtyPlusRt = MakeRect("QtyPlus", qtyGroupRt, new Vector2(0.7f, 0f), new Vector2(1f, 1f));
            qtyPlusRt.offsetMin = Vector2.zero; qtyPlusRt.offsetMax = Vector2.zero;
            qtyPlusRt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var qtyPlusBtn = qtyPlusRt.gameObject.AddComponent<Button>();
            NoAutoNav(qtyPlusBtn);
            SkyPrisonUIButtonFeedback.Attach(qtyPlusRt.gameObject);
            var qtyPlusLabel = MakeRect("Label", qtyPlusRt, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            qtyPlusLabel.text = "+"; qtyPlusLabel.font = LoadLegacyFont(); qtyPlusLabel.fontSize = 34;
            qtyPlusLabel.alignment = TextAnchor.MiddleCenter; qtyPlusLabel.color = Color.white; qtyPlusLabel.raycastTarget = false;

            // 合计价格——货币图标+数字，绿色加号(+N)表示卖出获得，随选中数量实时乘算。
            var totalGroupRt = MakeRect("TotalGroup", rt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            totalGroupRt.pivot = new Vector2(1f, 0.5f);
            totalGroupRt.sizeDelta = new Vector2(220f, 56f);
            totalGroupRt.anchoredPosition = new Vector2(-188f, 0f);

            var totalIconRt = MakeRect("SellIcon", totalGroupRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            totalIconRt.pivot = new Vector2(0f, 0.5f);
            totalIconRt.sizeDelta = new Vector2(44f, 44f);
            totalIconRt.anchoredPosition = Vector2.zero;
            var totalIconImg = totalIconRt.gameObject.AddComponent<Image>();
            totalIconImg.preserveAspect = true;

            var totalRt = MakeRect("SellItemPrice", totalGroupRt, Vector2.zero, Vector2.one);
            totalRt.offsetMin = new Vector2(56f, 0f); totalRt.offsetMax = Vector2.zero;
            var totalText = totalRt.gameObject.AddComponent<Text>();
            totalText.font = LoadLegacyFont(); totalText.fontSize = 36; totalText.color = SkyPrisonUIPalette.ColdGreen;
            totalText.alignment = TextAnchor.MiddleRight;
            totalText.horizontalOverflow = HorizontalWrapMode.Overflow;

            const float sellBtnW = 140f, sellBtnH = 76f;
            var sellBtnRt = MakeRect("SellButton", rt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            sellBtnRt.pivot = new Vector2(1f, 0.5f);
            sellBtnRt.sizeDelta = new Vector2(sellBtnW, sellBtnH);
            sellBtnRt.anchoredPosition = new Vector2(-24f, 0f);
            // 用户明确要求"加入"按钮改成线框样式、不要圆角——跟结账区"返回购物"/
            // "结账"按钮同一套做法：透明背景 + AddOutline() 描边(直角，不是圆角贴图)。
            var sellBtnImg = sellBtnRt.gameObject.AddComponent<Image>();
            sellBtnImg.color = new Color(0f, 0f, 0f, 0f);
            SkyPrisonFloatingWindowKit.AddOutline(sellBtnRt, SkyPrisonUIPalette.ColdGreen, 2f);
            var sellBtn = sellBtnRt.gameObject.AddComponent<Button>();
            NoAutoNav(sellBtn);
            SkyPrisonUIButtonFeedback.Attach(sellBtnRt.gameObject);
            var sellBtnLabelRt = MakeRect("Label", sellBtnRt, Vector2.zero, Vector2.one);
            sellBtnLabelRt.offsetMin = Vector2.zero; sellBtnLabelRt.offsetMax = Vector2.zero;
            var sellBtnLabel = sellBtnLabelRt.gameObject.AddComponent<Text>();
            sellBtnLabel.text = "加入"; sellBtnLabel.font = LoadLegacyFont(); sellBtnLabel.fontSize = 36;
            sellBtnLabel.alignment = TextAnchor.MiddleCenter; sellBtnLabel.color = SkyPrisonUIPalette.ColdGreen; sellBtnLabel.raycastTarget = false;

            PrefabUtility.SaveAsPrefabAsset(go, SellRowPrefabPath);
            Object.DestroyImmediate(go);
            return AssetDatabase.LoadAssetAtPath<GameObject>(SellRowPrefabPath);
        }

        // ── 圆角矩形 Sprite（持久化到磁盘）────────────────────────────────────
        // SkyPrisonRoundedRectSprite.Create() 生成的是纯内存 Sprite——这套跟本session
        // 之前踩过的滚动条胶囊贴图一模一样的坑：PrefabUtility.SaveAsPrefabAsset 没法
        // 序列化一个没有GUID/资产文件的Sprite引用，存进prefab里会静默变成{fileID:0}。
        // 这里存成磁盘资产，跟贴图文件一起复用。
        private const string GeneratedSpriteDir = "Assets/_Project/UIUX/Generated";

        private static Sprite LoadOrCreatePersistedRoundedRectSprite(int texSize, int radius)
        {
            string fileName = $"RoundedRect_s{texSize}_r{radius}.asset";
            string path = $"{GeneratedSpriteDir}/{fileName}";

            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(GeneratedSpriteDir))
            {
                const string parent = "Assets/_Project/UIUX";
                if (!AssetDatabase.IsValidFolder(parent)) return null; // 不该发生
                AssetDatabase.CreateFolder(parent, "Generated");
            }

            Sprite memSprite = SkyPrisonRoundedRectSprite.Create(texSize, radius);
            var tex = memSprite.texture;
            tex.name = fileName;
            var persistedSprite = Sprite.Create(tex, memSprite.rect, new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, memSprite.border);

            AssetDatabase.CreateAsset(tex, path);
            AssetDatabase.AddObjectToAsset(persistedSprite, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // 加入购物车角标——用户画图澄清：不是圆形，是贴在卡片右上角的三角形"折角"
        // 丝带（经典电商角标样式），而且只在鼠标悬停卡片时才出现，不是常驻。三角形
        // 直接生成一张贴图：右上角(x,y都偏大)那半个正方形填色，其余透明。
        private static Sprite LoadOrCreatePersistedCornerTriangleSprite(int texSize)
        {
            string fileName = $"CornerTriangle_s{texSize}.asset";
            string path = $"{GeneratedSpriteDir}/{fileName}";

            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(GeneratedSpriteDir))
            {
                const string parent = "Assets/_Project/UIUX";
                if (!AssetDatabase.IsValidFolder(parent)) return null;
                AssetDatabase.CreateFolder(parent, "Generated");
            }

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = fileName;

            var pixels = new Color[texSize * texSize];
            for (int y = 0; y < texSize; y++)
                for (int x = 0; x < texSize; x++)
                    pixels[y * texSize + x] = (x + y >= texSize) ? Color.white : Color.clear;
            tex.SetPixels(pixels);
            tex.Apply();

            var persistedSprite = Sprite.Create(tex, new Rect(0, 0, texSize, texSize), new Vector2(0.5f, 0.5f), 100f);

            AssetDatabase.CreateAsset(tex, path);
            AssetDatabase.AddObjectToAsset(persistedSprite, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // 之前用"两层圆角矩形叠色块伪造挖空"做数量胶囊的描边，结果背景色对不准，
        // 看起来还是像实心填充——用户点名指出这不是描边。这次直接生成一张真正中间
        // alpha=0的圆角环形贴图，描边是贴图本身透出来的，不再靠拼颜色伪装。
        private static Sprite LoadOrCreatePersistedRingSprite(int texSize, int radius, int thickness)
        {
            string fileName = $"RoundedRingRect_s{texSize}_r{radius}_t{thickness}.asset";
            string path = $"{GeneratedSpriteDir}/{fileName}";

            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(GeneratedSpriteDir))
            {
                const string parent = "Assets/_Project/UIUX";
                if (!AssetDatabase.IsValidFolder(parent)) return null;
                AssetDatabase.CreateFolder(parent, "Generated");
            }

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = fileName;

            var pixels = new Color[texSize * texSize];
            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    bool inOuter = IsInsideRoundedRectLocal(x, y, texSize, texSize, radius);
                    bool inInner = IsInsideRoundedRectLocal(x, y, texSize, texSize, radius, thickness);
                    pixels[y * texSize + x] = (inOuter && !inInner) ? Color.white : Color.clear;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            var border = new Vector4(radius, radius, radius, radius);
            var persistedSprite = Sprite.Create(tex, new Rect(0, 0, texSize, texSize), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);

            AssetDatabase.CreateAsset(tex, path);
            AssetDatabase.AddObjectToAsset(persistedSprite, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static bool IsInsideRoundedRectLocal(int px, int py, int w, int h, int r, int inset = 0)
        {
            int left = inset, right = w - 1 - inset;
            int bottom = inset, top = h - 1 - inset;
            if (px < left || px > right || py < bottom || py > top) return false;

            int rr = Mathf.Max(0, r - inset);
            int x0 = left + rr, x1 = right - rr;
            int y0 = bottom + rr, y1 = top - rr;
            if (px >= x0 && px <= x1) return true;
            if (py >= y0 && py <= y1) return true;

            int cx = (px < x0) ? x0 : x1;
            int cy = (py < y0) ? y0 : y1;
            float dx = px - cx, dy = py - cy;
            return dx * dx + dy * dy <= (float)rr * rr;
        }

        // 图标底部渐隐——图标自己的像素透明度按UV.y衰减（用户明确要求"图标自己淡出"，
        // 不是叠一层深色遮罩），用 SkyPrisonUIVerticalFadeIcon.shader 配的材质。
        private const string IconFadeMaterialPath = "Assets/_Project/UIUX/Generated/Mat_ShopIconFade.mat";

        private static Material LoadOrCreatePersistedIconFadeMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(IconFadeMaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("SkyPrison/UI/VerticalFadeIcon");
            if (shader == null)
            {
                Debug.LogWarning("[Shop] 找不到 SkyPrison/UI/VerticalFadeIcon shader，图标底部渐隐这次跳过。");
                return null;
            }

            if (!AssetDatabase.IsValidFolder(GeneratedSpriteDir))
            {
                const string parent = "Assets/_Project/UIUX";
                if (!AssetDatabase.IsValidFolder(parent)) return null;
                AssetDatabase.CreateFolder(parent, "Generated");
            }

            var mat = new Material(shader);
            mat.SetFloat("_FadeStart", 0.55f); // 图标上55%完全不透明
            mat.SetFloat("_FadeEnd", 0f);       // 图标底边完全透明

            AssetDatabase.CreateAsset(mat, IconFadeMaterialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(IconFadeMaterialPath);

            return AssetDatabase.LoadAssetAtPath<Material>(IconFadeMaterialPath);
        }

        // 售罄货架卡片图标要变黑白（用户明确要求）——跟正常图标共用同一个渐隐 shader，
        // 只是 _Saturation 烤成0。材质是共享资产，不能在运行时直接改一份共享材质的
        // 属性（会连带影响所有还在用同一份材质的其他卡片），所以单独存一份持久化的
        // 灰度版本，两种状态各自引用不同材质实例，互不干扰。
        private const string IconGrayscaleFadeMaterialPath = "Assets/_Project/UIUX/Generated/Mat_ShopIconGrayscaleFade.mat";

        private static Material LoadOrCreatePersistedIconGrayscaleFadeMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(IconGrayscaleFadeMaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("SkyPrison/UI/VerticalFadeIcon");
            if (shader == null) return null;

            if (!AssetDatabase.IsValidFolder(GeneratedSpriteDir))
            {
                const string parent = "Assets/_Project/UIUX";
                if (!AssetDatabase.IsValidFolder(parent)) return null;
                AssetDatabase.CreateFolder(parent, "Generated");
            }

            var mat = new Material(shader);
            mat.SetFloat("_FadeStart", 0.55f);
            mat.SetFloat("_FadeEnd", 0f);
            mat.SetFloat("_Saturation", 0f); // 纯黑白

            AssetDatabase.CreateAsset(mat, IconGrayscaleFadeMaterialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(IconGrayscaleFadeMaterialPath);

            return AssetDatabase.LoadAssetAtPath<Material>(IconGrayscaleFadeMaterialPath);
        }

        // ── 演示数据：随便挑几个已有物品建一个能跑的商店 ──────────────────────
        private static ShopDefinition EnsureDemoShopAsset()
        {
            EnsureFolder("Assets/_Project/Data/Definitions/Custom/Shop");
            var existing = AssetDatabase.LoadAssetAtPath<ShopDefinition>(DemoShopAssetPath);
            if (existing != null)
            {
                // 之前几版生成的演示数据把priceOverride写死成了50，把之前留下的这个旧值
                // 清掉，让它按ResolvePrice()的默认逻辑去读物品自己的真实价格数据。
                bool dirty = false;
                foreach (var entry in existing.items)
                {
                    if (entry.priceOverride == 50) { entry.priceOverride = 0; dirty = true; }
                }
                if (dirty) EditorUtility.SetDirty(existing);
                return existing;
            }

            var shop = ScriptableObject.CreateInstance<ShopDefinition>();
            shop.shopId = "demo_shop";
            shop.displayName = "补给站(演示)";
            shop.defaultCurrencyId = "token";
            shop.refreshStockOnChapterStart = true;

            string[] demoItems =
            {
                "Assets/_Project/Data/Definitions/Standard/Items/ID_first_aid_medicine.asset",
                "Assets/_Project/Data/Definitions/Standard/Items/ID_canned_food.asset",
                "Assets/_Project/Data/Definitions/Standard/Items/ID_compressed_biscuit.asset",
                "Assets/_Project/Data/Definitions/Standard/Items/ID_cooling_agent.asset",
                "Assets/_Project/Data/Definitions/Standard/Items/ID_dry_battery.asset",
            };
            foreach (string path in demoItems)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item == null) continue;
                // priceOverride留0——ShopItemEntry.ResolvePrice()本来就会在没有override时
                // 自动去读物品自己定义的currencyPrices（真实数据），不用在这里瞎编一个50。
                shop.items.Add(new ShopItemEntry { item = item, stock = 10, priceOverride = 0 });
            }

            AssetDatabase.CreateAsset(shop, DemoShopAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Shop] 演示商店数据已创建：{DemoShopAssetPath}（{shop.items.Count}件商品，F6可以直接测）。");
            return shop;
        }

        // ── 小工具 ──────────────────────────────────────────────────────────
        private static RectTransform MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            string name = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static Font _legacyFont;
        private static Font LoadLegacyFont()
        {
            if (_legacyFont == null)
                _legacyFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/UIUX/Fonts/ZhouFangRiMingTi-2.otf");
            return _legacyFont;
        }

        private static Text ConvertToLegacyIfNeeded(TMP_Text tmp)
        {
            // cartCountText/titleLabel 字段类型是旧版 Text，这里直接用旧版 Text 重建一份。
            // 之前这里把 fontSize/color 写死成 18号黑字，完全无视调用方之前设置的大小和
            // 颜色——角标数字明明改到44*mul号白字了，转换这一步又给强行压回18号黑字，
            // "放大过了怎么还是那么小"就是这个原因。改成读转换前 TMP 组件的实际值。
            var go = tmp.gameObject;
            float size = tmp.fontSize;
            Color color = tmp.color;
            bool bold = (tmp.fontStyle & FontStyles.Bold) != 0;
            // 对齐方式也被硬编码成MiddleCenter吞掉过——标题栏文字之前显式设了
            // MidlineLeft(跟其它窗口标题一样靠左)，转换这一步直接无视，商店标题
            // 变成了居中显示。按TMP的水平对齐语义分左/右/居中三档映射回旧版Text。
            TextAnchor alignment = MapTmpAlignmentToLegacy(tmp.alignment);
            Object.DestroyImmediate(tmp);
            var text = go.AddComponent<Text>();
            text.font = LoadLegacyFont();
            text.fontSize = Mathf.Max(1, Mathf.RoundToInt(size));
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.color = color;
            text.alignment = alignment;
            return text;
        }

        private static TextAnchor MapTmpAlignmentToLegacy(TextAlignmentOptions tmpAlign)
        {
            string s = tmpAlign.ToString();
            if (s.Contains("Left")) return TextAnchor.MiddleLeft;
            if (s.Contains("Right")) return TextAnchor.MiddleRight;
            return TextAnchor.MiddleCenter;
        }

        // 用户反馈"WASD移动货架光标会导致鼠标点击失灵"——根源是所有 Button/InputField
        // 默认都是 Navigation.Mode.Automatic，Unity 自带的 StandaloneInputModule 每帧
        // 也在读 Horizontal/Vertical 这两个轴(跟 SkyPrisonListGamepadNav 读的是同一对
        // 轴，WASD默认就绑在这两个轴上)，会按自动导航把EventSystem的选中对象挪到
        // 邻近的Selectable上——挪到InputField(比如数量输入框QAQty)时，旧版InputField.
        // OnSelect()会无条件调用ActivateInputField()进入编辑态，把键盘/选中焦点吃住，
        // 后续鼠标点别的卡片就没反应了。这个项目里方向导航全部由 SkyPrisonListGamepadNav
        // 手写实现，Unity自带这套自动导航从头到尾都不需要，统一关掉。
        private static void NoAutoNav(Selectable s)
        {
            if (s == null) return;
            var nav = s.navigation;
            nav.mode = Navigation.Mode.None;
            s.navigation = nav;
        }
    }
}
#endif
