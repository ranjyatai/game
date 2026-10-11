#if UNITY_EDITOR
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 创建工房"一键修复"确认弹窗 PF_WorkshopRepairPopup。
    /// 风格跟 PF_PlayerDeathRevive 同一套(模糊背景+四角白色L形角标+细线按钮外框)，
    /// 只是内容换成"是否消耗 [图标]数字 进行修理？"+确认/取消。
    /// </summary>
    public static class CreateWorkshopRepairPopupPrefab
    {
        public const string PrefabPath    = "Assets/_Project/Prefabs/UI/Window/PF_WorkshopRepairPopup.prefab";
        public const string ResMirrorPath = "Assets/Resources/UI/Window/PF_WorkshopRepairPopup.prefab";

        [MenuItem("天空囚笼/UI/Create Workshop Repair Popup", false, 174)]
        public static void Create()
        {
            var root = new GameObject("PF_WorkshopRepairPopup");
            var rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero; rootRT.anchorMax = Vector2.one;
            rootRT.sizeDelta = Vector2.zero; rootRT.anchoredPosition = Vector2.zero;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1200;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.matchWidthOrHeight  = 0.5f;

            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            root.AddComponent<SkyPrisonUIGlobalStyleSettings_V1>();

            var meta = root.AddComponent<SkyPrisonUIPrefabMetadata_V1>();
            meta.uiId              = "workshop_repair";
            meta.displayName       = "工房修理确认";
            meta.kind              = SkyPrisonUIPrefabKindV1.Popup;
            meta.blocksRaycasts    = true;
            meta.lockGameplayInput = true;
            meta.showMouseCursor   = true;
            meta.inputModeWhenOpen = SkyPrisonUIInputModeV1.UI;

            CopyStyleFromInventory(root.GetComponent<SkyPrisonUIGlobalStyleSettings_V1>());

            // ── 窗口面板（1248 × 780，居中）用户明确要求再宽1.3倍(960→1248)，高度
            // 不变。
            var panel = CreateChildRect(rootRT, "WindowPanel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1248f, 780f));

            var blurBgRT = CreateChildRect(panel, "BlurBackground",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var blurRaw = blurBgRT.gameObject.AddComponent<RawImage>();
            blurRaw.color = new Color(0.05f, 0.06f, 0.08f, 1f);
            blurRaw.raycastTarget = false;

            var blur = panel.gameObject.AddComponent<SkyPrisonInventoryBlurBackground>();
            blur.SetReferences(blurRaw, panel);
            var uvt = panel.gameObject.AddComponent<SkyPrisonBlurUVTracker>();
            uvt.Bind(blurRaw, panel);

            var panelBg = panel.gameObject.AddComponent<Image>();
            panelBg.color = new Color(0.03f, 0.04f, 0.05f, 0.84f);
            panelBg.raycastTarget = true;

            SkyPrisonFloatingWindowKit.AddCornerBrackets(panel); // Kit 默认值(39/3.9)，跟角色信息面板一致

            // 标题栏+关闭按钮——用户明确要求标题左对齐、关闭按钮用项目统一的×图标，
            // 不要各窗口各画一套。直接复用商店/背包同款的 FloatingWindowKit 助手，
            // 而不是手搭一个居中文字+纯文本"×"。
            var font = SkyPrisonFloatingWindowKit.LoadTMPFont("ZhouFangRiMingTi-2 SDF");
            SkyPrisonFloatingWindowKit.BuildTitleBar(panel, "装备修理", font, out var titleTmp);
            SkyPrisonFloatingWindowKit.BuildCloseButton(panel, null, font);
            var closeBtn = panel.Find("CloseButton")?.GetComponent<Button>();

            float contentTop = SkyPrisonFloatingWindowKit.TitleBarHeight;

            // 提示文字——单行，字号放大一档。所有区块统一用"从标题栏往下按像素叠放"
            // 的写法(不跟百分比锚点混用)，摆位可预测、不重叠。
            const float msgTop = 20f, msgH = 44f;
            var messageRt = CreateChildRect(panel, "Message",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(contentTop + msgTop)), new Vector2(-80f, msgH));
            var messageTxt = AddTMPComponent(messageRt, "是否消耗以下费用，修理身上所有装备？", 26,
                TextAlignmentOptions.Center, new Color(0.82f, 0.84f, 0.86f, 1f), FontStyles.Normal);

            // 身上没有需要修的装备时的提示（跟Message互斥显隐，同一块区域）
            var emptyRt = CreateChildRect(panel, "EmptyText",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(contentTop + msgTop)), new Vector2(-80f, msgH));
            var emptyTxt = AddTMPComponent(emptyRt, "身上没有需要修理的装备", 26,
                TextAlignmentOptions.Center, new Color(0.55f, 0.58f, 0.6f, 1f), FontStyles.Normal);
            emptyTxt.gameObject.SetActive(false);

            // 装备列表——用户明确要求做成表格：图标/名字/耐久/消耗代币/个别修理按钮，
            // 可能不止一件需要修，用滚动列表装，跟商店货架/购物车同一套ScrollRect
            // 做法(Viewport+Mask+VerticalLayoutGroup+ContentSizeFitter)。
            const float listTop = 90f, listH = 340f;
            var listAreaRt = CreateChildRect(panel, "ListArea",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(contentTop + listTop)), new Vector2(-48f, listH));
            Transform listContent = BuildScrollArea(listAreaRt);
            GameObject itemRowPrefabAsset = BuildAndSaveRepairItemRowPrefab();

            // 费用摘要——"所需"/"所持"两行，标签左对齐、图标+数字贴右对齐，跟商店
            // 结账摘要(ShopWindowController/CreateSkyPrisonShopPrefab.MakeSummaryRow)
            // 同一个视觉语言。
            var tokenDef = AssetDatabase.LoadAssetAtPath<CurrencyDefinition>("Assets/_Project/Data/Definitions/Standard/Currencies/CD_Token.asset");

            // rowW 加宽——同样是给9位数货币留够显示空间(见下面ValueGroup的注释)。
            const float rowW = 420f, rowH = 58f, rowGap = 2f;
            const float summaryTop = listTop + listH + 20f;
            var summaryRt = CreateChildRect(panel, "Summary",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(contentTop + summaryTop)), new Vector2(rowW, rowH * 2f + rowGap));

            (TextMeshProUGUI value, Image icon, TextMeshProUGUI labelTmp) MakeCostRow(string name, string label, float yFromTop, Color labelColor)
            {
                var rowRt = CreateChildRect(summaryRt, name,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -yFromTop), new Vector2(0f, rowH));

                var labelRt = CreateChildRect(rowRt, "Label",
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    Vector2.zero, new Vector2(100f, rowH));
                var labelTxt = AddTMPComponent(labelRt, label, 30, TextAlignmentOptions.Left, labelColor, FontStyles.Normal);

                // ValueGroup加宽到320——用户明确要求给货币数字留够9位数(带千分位
                // 逗号约11个字符)的显示空间，不用按long类型19位的理论上限留，但也
                // 不能只按现在看到的几位数留死。
                var groupRt = CreateChildRect(rowRt, "ValueGroup",
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    Vector2.zero, new Vector2(320f, rowH));

                var iconRt = CreateChildRect(groupRt, "Icon",
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    Vector2.zero, new Vector2(48f, 48f));
                var iconImg = iconRt.gameObject.AddComponent<Image>();
                iconImg.preserveAspect = true;
                if (tokenDef != null) iconImg.sprite = tokenDef.icon;

                // 数字用固定的"图标右边留80px起排"，不再靠sizeDelta+anchoredPosition
                // 混着算(之前那种写法在stretch锚点下算出来的实际可用宽度经常比预期
                // 窄，数字被挤到贴着图标)——直接用offsetMin/offsetMax，跟商店那边
                // 已经验证过没问题的写法完全一致。
                var valueRt = CreateChildRect(groupRt, "Value", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                valueRt.offsetMin = new Vector2(80f, 0f);
                valueRt.offsetMax = Vector2.zero;
                var valueTxt = AddTMPComponent(valueRt, "0", 34, TextAlignmentOptions.Right, labelColor, FontStyles.Bold);

                return (valueTxt, iconImg, labelTxt);
            }

            var (costValueTxt, costIconImg, costLabelTxt) = MakeCostRow("CostRow", "所需", 0f, new Color(0.42f, 0.92f, 0.68f, 1f));
            var (walletValueTxt, _, walletLabelTxt) = MakeCostRow("WalletRow", "所持", rowH + rowGap, new Color(0.82f, 0.84f, 0.86f, 1f));

            // 一键修理/取消——用跟上面所有区块一致的像素叠放坐标系摆位，不再跟按钮
            // 惯用的百分比锚点混用(混用是之前"所需/所持"跟提示文字重叠那次事故的
            // 根因，这次直接统一写法，不留同样的坑)。
            const float btnTop = summaryTop + rowH * 2f + rowGap + 20f, btnH = 70f, btnW = 444f;
            var confirmBtnRt = CreateChildRect(panel, "ConfirmButton",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-24f, -(contentTop + btnTop)), new Vector2(btnW, btnH));
            BuildButtonContents(confirmBtnRt, "一键修理", new Color(0.42f, 0.92f, 0.68f, 1f));

            var cancelBtnRt = CreateChildRect(panel, "CancelButton",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -(contentTop + btnTop)), new Vector2(btnW, btnH));
            BuildButtonContents(cancelBtnRt, "取消", new Color(0.88f, 0.88f, 0.90f, 1f));

            // ── 组件挂载 ────────────────────────────────────────────────────
            var controller = root.AddComponent<WorkshopRepairPopupController>();
            var so = new SerializedObject(controller);
            so.FindProperty("closeButton").objectReferenceValue = closeBtn;
            so.FindProperty("titleText").objectReferenceValue = ConvertToLegacyIfNeeded(titleTmp);
            so.FindProperty("costLabel").objectReferenceValue = ConvertToLegacyIfNeeded(costLabelTxt);
            so.FindProperty("walletLabel").objectReferenceValue = ConvertToLegacyIfNeeded(walletLabelTxt);
            so.FindProperty("messageText").objectReferenceValue = ConvertToLegacyIfNeeded(messageTxt);
            so.FindProperty("emptyText").objectReferenceValue = ConvertToLegacyIfNeeded(emptyTxt);
            so.FindProperty("currencyIcon").objectReferenceValue = costIconImg;
            so.FindProperty("costText").objectReferenceValue = ConvertToLegacyIfNeeded(costValueTxt);
            so.FindProperty("walletText").objectReferenceValue = ConvertToLegacyIfNeeded(walletValueTxt);
            so.FindProperty("summaryRoot").objectReferenceValue = summaryRt.gameObject;
            so.FindProperty("itemListContent").objectReferenceValue = listContent;
            so.FindProperty("itemRowPrefab").objectReferenceValue = itemRowPrefabAsset;
            var confirmBtnTf = panel.Find("ConfirmButton");
            var cancelBtnTf  = panel.Find("CancelButton");
            so.FindProperty("confirmButton").objectReferenceValue = confirmBtnTf.GetComponent<Button>();
            so.FindProperty("confirmLabel").objectReferenceValue = ConvertToLegacyIfNeeded(confirmBtnTf.Find("Label").GetComponent<TextMeshProUGUI>());
            so.FindProperty("cancelButton").objectReferenceValue = cancelBtnTf.GetComponent<Button>();
            so.FindProperty("cancelLabel").objectReferenceValue = ConvertToLegacyIfNeeded(cancelBtnTf.Find("Label").GetComponent<TextMeshProUGUI>());
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolder("Assets/_Project/Prefabs/UI/Window");
            EnsureFolder("Assets/Resources/UI/Window");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            if (ok)
            {
                if (AssetDatabase.CopyAsset(PrefabPath, ResMirrorPath))
                    AssetDatabase.ImportAsset(ResMirrorPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[WorkshopRepair] Prefab 已创建：{PrefabPath}");
            }
            else
                Debug.LogError("[WorkshopRepair] Prefab 保存失败！");

            Object.DestroyImmediate(root);
        }

        // messageText/emptyText/costText/confirmLabel/cancelLabel 字段在
        // WorkshopRepairPopupController 里声明成了 UnityEngine.UI.Text(legacy)，
        // 跟商店/其它窗口的字段类型保持一致，方便复用 SetChildText 这类小工具——
        // 但这里的可视文字实际是用 TextMeshProUGUI 搭的(跟随PlayerDeathRevive这套
        // 风格)，所以给这几个物体补一个legacy Text组件当"数据接口"，两个组件叠在
        // 同一个物体上，TMP负责显示，legacy Text只用来给控制器改text属性——
        // 简化起见改成直接换成legacy Text组件本身，不叠两份。
        private static Text ConvertToLegacyIfNeeded(TMP_Text tmp)
        {
            if (tmp == null) return null;
            var go = tmp.gameObject;
            string text = tmp.text;
            float fontSize = tmp.fontSize;
            Color color = tmp.color;
            TextAlignmentOptions align = tmp.alignment;
            FontStyles style = tmp.fontStyle;
            Object.DestroyImmediate(tmp);

            var legacyText = go.AddComponent<Text>();
            legacyText.font = LoadLegacyFont();
            legacyText.fontSize = Mathf.RoundToInt(fontSize);
            legacyText.color = color;
            legacyText.text = text;
            legacyText.fontStyle = style == FontStyles.Bold ? FontStyle.Bold : FontStyle.Normal;
            // BuildTitleBar()用的是MidlineLeft(不是Left)——TMP的"基线对齐"和"中线对齐"
            // 是两组不同的枚举值，之前这里只判断了Left/Right，MidlineLeft落到默认分支
            // 被错误转换成了居中，标题白白多绕一圈变成"看起来像是我自己手滑给它居中了"。
            legacyText.alignment = align switch
            {
                TextAlignmentOptions.Left or TextAlignmentOptions.MidlineLeft or TextAlignmentOptions.BottomLeft or TextAlignmentOptions.TopLeft => TextAnchor.MiddleLeft,
                TextAlignmentOptions.Right or TextAlignmentOptions.MidlineRight or TextAlignmentOptions.BottomRight or TextAlignmentOptions.TopRight => TextAnchor.MiddleRight,
                _ => TextAnchor.MiddleCenter,
            };
            legacyText.horizontalOverflow = HorizontalWrapMode.Overflow;
            legacyText.raycastTarget = false;
            return legacyText;
        }

        private static Font _cachedLegacyFont;
        private static Font LoadLegacyFont()
        {
            if (_cachedLegacyFont == null)
                _cachedLegacyFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/UIUX/Fonts/ZhouFangRiMingTi-2.otf");
            return _cachedLegacyFont;
        }

        // ── 角标/线条/按钮 ───────────────────────────────────────────────────

        private static void BuildButtonContents(RectTransform rt, string label, Color textColor)
        {
            rt.gameObject.AddComponent<Image>().color = Color.clear;
            rt.gameObject.AddComponent<Button>();
            AddOutline(rt, Color.white, 3f);

            var lblRT = CreateChildRect(rt, "Label", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var txt = lblRT.gameObject.AddComponent<TextMeshProUGUI>();
            txt.text = label; txt.fontSize = 34;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = textColor; txt.raycastTarget = false;
            var font = GetFont(); if (font != null) txt.font = font;

            SkyPrisonUIButtonFeedback.Attach(rt.gameObject);
        }

        // ── 装备列表滚动区 + 行模板 ────────────────────────────────────────────

        private static Transform BuildScrollArea(RectTransform area)
        {
            var scrollRect = area.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateChildRect(area, "Viewport", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            viewport.gameObject.AddComponent<Image>().color = Color.white;

            var content = CreateChildRect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false; vlg.childForceExpandHeight = false;
            vlg.spacing = 6f;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport;
            scrollRect.content = content;

            SkyPrisonUIScrollbar.AttachVertical(scrollRect, area, new Color(1f, 1f, 1f, 0.5f),
                rightMargin: 2f, bottomMargin: 0f, width: 4f,
                visibility: ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport);

            return content;
        }

        // 装备行——图标/名字/耐久/消耗代币(图标+数字)/个别修理按钮，字段名字得跟
        // WorkshopRepairPopupController.BindItemRow() 查找的路径完全对上。
        private static GameObject BuildAndSaveRepairItemRowPrefab()
        {
            const float rowH = 92f;
            var go = new GameObject("WorkshopRepairItemRow", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(0f, rowH);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = rowH; le.flexibleWidth = 1f;

            // 背景留白，实际的亮暗交替填充色由 WorkshopRepairPopupController.
            // RefreshItemList() 按行序号(lineIndex%2)设置——用户明确要求跟商店购物车
            // 行同一套"一深一浅交替"视觉，不能是固定同一个颜色。
            var bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.04f);

            var iconRt = CreateChildRect(rt, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(14f, 0f), new Vector2(72f, 72f));
            var iconImg = iconRt.gameObject.AddComponent<Image>();
            iconImg.preserveAspect = true;

            // 行内文字全用legacy Text直接搭(不是TMP+转换)——这份prefab是运行时
            // Instantiate的模板，WorkshopRepairPopupController.BindItemRow()按名字
            // 查子物体后直接GetComponent<Text>()赋值，模板上就得是真正的Text组件，
            // 不是"挂了TMP、还没转换"的半成品。
            var nameRt = CreateChildRect(rt, "ItemName", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(100f, 0f), new Vector2(260f, rowH));
            var nameTxt = nameRt.gameObject.AddComponent<Text>();
            nameTxt.font = LoadLegacyFont(); nameTxt.fontSize = 28; nameTxt.color = Color.white;
            nameTxt.alignment = TextAnchor.MiddleLeft; nameTxt.horizontalOverflow = HorizontalWrapMode.Overflow; nameTxt.raycastTarget = false;

            // 耐久归零(报废)时用淡红色——用户明确要求，跟商店"余额不足"那个红色是
            // 同一个色值(InsufficientFundsColor)，颜色语义在项目里保持一致，不用
            // 另配一个新红色。具体判断/上色在 WorkshopRepairPopupController.
            // BindItemRow() 里做，这里只建好文字组件本身。
            var durRt = CreateChildRect(rt, "Durability", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(380f, 0f), new Vector2(160f, rowH));
            var durTxt = durRt.gameObject.AddComponent<Text>();
            durTxt.font = LoadLegacyFont(); durTxt.fontSize = 26; durTxt.color = new Color(0.75f, 0.78f, 0.8f, 1f);
            durTxt.alignment = TextAnchor.MiddleLeft; durTxt.horizontalOverflow = HorizontalWrapMode.Overflow; durTxt.raycastTarget = false;

            // 用户明确要求"怕货币位数多顶到图标"——按9位数(带千分位逗号大概11个
            // 字符，用户确认9位数已经是"开修改器都花不完"的量，不用按long类型的
            // 理论上限19位留)算够用的宽度，图标到数字之间、以及整个数字区的可用
            // 宽度都留大一些，跟RepairButton之间也留够间距不会被挤到。
            var totalGroupRt = CreateChildRect(rt, "TotalGroup", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-190f, 0f), new Vector2(300f, rowH));
            var costIconRt = CreateChildRect(totalGroupRt, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(40f, 40f));
            var costIconImg = costIconRt.gameObject.AddComponent<Image>();
            costIconImg.preserveAspect = true;
            var costTextRt = CreateChildRect(totalGroupRt, "CostText", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            costTextRt.offsetMin = new Vector2(70f, 0f);
            costTextRt.offsetMax = Vector2.zero;
            var costTxt = costTextRt.gameObject.AddComponent<Text>();
            costTxt.font = LoadLegacyFont(); costTxt.fontSize = 28; costTxt.color = new Color(0.42f, 0.92f, 0.68f, 1f);
            costTxt.fontStyle = FontStyle.Bold; costTxt.alignment = TextAnchor.MiddleRight;
            costTxt.horizontalOverflow = HorizontalWrapMode.Overflow; costTxt.raycastTarget = false;

            const float repairBtnW = 130f, repairBtnH = 64f;
            var repairBtnRt = CreateChildRect(rt, "RepairButton", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-14f, 0f), new Vector2(repairBtnW, repairBtnH));
            repairBtnRt.gameObject.AddComponent<Image>().color = Color.clear;
            repairBtnRt.gameObject.AddComponent<Button>();
            AddOutline(repairBtnRt, Color.white, 2f);
            var repairLabelRt = CreateChildRect(repairBtnRt, "Label", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var repairLabelTxt = repairLabelRt.gameObject.AddComponent<Text>();
            repairLabelTxt.font = LoadLegacyFont(); repairLabelTxt.fontSize = 26; repairLabelTxt.color = new Color(0.42f, 0.92f, 0.68f, 1f);
            repairLabelTxt.text = "修理"; repairLabelTxt.alignment = TextAnchor.MiddleCenter; repairLabelTxt.raycastTarget = false;
            SkyPrisonUIButtonFeedback.Attach(repairBtnRt.gameObject);

            const string path = "Assets/_Project/Prefabs/UI/Window/PF_WorkshopRepairItemRow.prefab";
            EnsureFolder("Assets/_Project/Prefabs/UI/Window");
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static Image AddLine(RectTransform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta, Color color)
        {
            var rt = CreateChildRect(parent, name, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color; img.raycastTarget = false;
            return img;
        }

        private static void AddOutline(RectTransform target, Color color, float thickness = 1f)
        {
            AddLine(target, "Outline_T", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, thickness), color);
            AddLine(target, "Outline_B", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, thickness), color);
            AddLine(target, "Outline_L", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(thickness, 0f), color);
            AddLine(target, "Outline_R", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(thickness, 0f), color);
        }

        // ── TMP 工具 ────────────────────────────────────────────────────────

        private static TMP_FontAsset s_cachedFont;
        private static TMP_FontAsset GetFont()
        {
            if (s_cachedFont != null) return s_cachedFont;
            const string fontPath = "Assets/_Project/UIUX/Fonts/TMP/ZhouFangRiMingTi-2 SDF.asset";
            s_cachedFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (s_cachedFont == null)
            {
                const string invPath = "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab";
                var inv = AssetDatabase.LoadAssetAtPath<GameObject>(invPath);
                var src = inv != null ? inv.GetComponent<SkyPrisonUIGlobalStyleSettings_V1>() : null;
                if (src != null) s_cachedFont = src.defaultTextFont;
            }
            return s_cachedFont;
        }

        private static TextMeshProUGUI AddTMP(RectTransform parent, string name,
            string text, float fontSize, TextAlignmentOptions align, Color color, FontStyles style)
        {
            var rt = CreateChildRect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return AddTMPComponent(rt, text, fontSize, align, color, style);
        }

        private static TextMeshProUGUI AddTMPRaw(RectTransform rt, string text, float fontSize,
            TextAlignmentOptions align, Color color, FontStyles style)
            => AddTMPComponent(rt, text, fontSize, align, color, style);

        private static TextMeshProUGUI AddTMPComponent(RectTransform rt, string text, float fontSize,
            TextAlignmentOptions align, Color color, FontStyles style)
        {
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = fontSize;
            tmp.alignment = align; tmp.color = color;
            tmp.fontStyle = style; tmp.raycastTarget = false;
            var font = GetFont();
            if (font != null) tmp.font = font;
            return tmp;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        }

        private static void SetAnchors(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        }

        private static RectTransform CreateChildRect(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
            return rt;
        }

        private static void CopyStyleFromInventory(SkyPrisonUIGlobalStyleSettings_V1 dest)
        {
            if (dest == null) return;
            const string fontPath = "Assets/_Project/UIUX/Fonts/TMP/ZhouFangRiMingTi-2 SDF.asset";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (font == null)
            {
                const string invPath = "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab";
                var invGO = AssetDatabase.LoadAssetAtPath<GameObject>(invPath);
                var src = invGO != null ? invGO.GetComponent<SkyPrisonUIGlobalStyleSettings_V1>() : null;
                if (src != null) font = src.defaultTextFont;
            }
            if (font != null)
            {
                dest.defaultTextFont = font;
                dest.defaultNumberFont = font;
                dest.defaultTextColor = Color.white;
            }
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
