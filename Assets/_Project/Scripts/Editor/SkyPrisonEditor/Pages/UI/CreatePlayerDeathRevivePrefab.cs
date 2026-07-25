#if UNITY_EDITOR
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 创建 / 恢复 PF_PlayerDeathRevive prefab（弹窗类）。
    /// 风格与背包窗口完全一致：
    ///   - 面板内 B&W 高斯模糊（SkyPrisonInventoryBlurBackground + UVTracker 挂 panel）
    ///   - 暗色半透叠加 (0.03, 0.04, 0.05, 0.84)
    ///   - 四角白色 L 形角标 len=20 thickness=2（同背包 AddCornerBrackets）
    ///   - 按钮白色 1px 细线外框（ButtonFeedback 自动处理 hover → 冷绿）
    /// </summary>
    public static class CreatePlayerDeathRevivePrefab
    {
        public const string PrefabPath    = "Assets/_Project/Prefabs/UI/Window/PF_PlayerDeathRevive.prefab";
        public const string ResMirrorPath = "Assets/Resources/UI/Window/PF_PlayerDeathRevive.prefab";

        // ── 供 UI 工作台「恢复默认」调用 ─────────────────────────────────────
        public static void RebuildContents(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.GetChild(i).gameObject);
            BuildContents(root.GetComponent<RectTransform>() ?? root.gameObject.AddComponent<RectTransform>());
        }

        // ── 菜单入口（首次创建） ──────────────────────────────────────────────
        [MenuItem("Tools/Sky Prison/UI/Create Player Death Revive Popup")]
        public static void Create()
        {
            var root = new GameObject("PF_PlayerDeathRevive");
            var rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero; rootRT.anchorMax = Vector2.one;
            rootRT.sizeDelta = Vector2.zero; rootRT.anchoredPosition = Vector2.zero;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1200;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.matchWidthOrHeight   = 0.5f;

            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            root.AddComponent<SkyPrisonUIGlobalStyleSettings_V1>(); // 字体/样式来源，同背包

            var meta = root.AddComponent<SkyPrisonUIPrefabMetadata_V1>();
            meta.uiId              = "player_death_revive";
            meta.displayName       = "死亡复活弹窗";
            meta.kind              = SkyPrisonUIPrefabKindV1.Popup;
            meta.blocksRaycasts    = true;
            meta.lockGameplayInput = true;
            meta.showMouseCursor   = true;
            meta.inputModeWhenOpen = SkyPrisonUIInputModeV1.UI;

            // 从背包 prefab 的全局样式复制字体到本 prefab，确保中文字体一致
            CopyStyleFromInventory(root.GetComponent<SkyPrisonUIGlobalStyleSettings_V1>());

            BuildContents(rootRT);

            EnsureFolder("Assets/_Project/Prefabs/UI/Window");
            EnsureFolder("Assets/Resources/UI/Window");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            if (ok)
            {
                if (AssetDatabase.CopyAsset(PrefabPath, ResMirrorPath))
                    AssetDatabase.ImportAsset(ResMirrorPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[DeathRevive] Prefab 已创建：{PrefabPath}");
            }
            else
                Debug.LogError("[DeathRevive] Prefab 保存失败！");

            Object.DestroyImmediate(root);
        }

        // ── 主结构（Create 与 RebuildContents 共用） ─────────────────────────

        private static void BuildContents(RectTransform root)
        {
            // ── 窗口面板（560 × 400，居中）────────────────────────────────────
            var panel = CreateChildRect(root, "WindowPanel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(780f, 620f));

            // 1. 模糊背景层（填满面板）
            var blurBgRT = CreateChildRect(panel, "BlurBackground",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var blurRaw = blurBgRT.gameObject.AddComponent<RawImage>();
            blurRaw.color         = new Color(0.05f, 0.06f, 0.08f, 1f); // 编辑器预览占位色
            blurRaw.raycastTarget = false;

            // 模糊组件挂在 panel 上，与背包完全一致
            var blur = panel.gameObject.AddComponent<SkyPrisonInventoryBlurBackground>();
            blur.SetReferences(blurRaw, panel);
            var uvt = panel.gameObject.AddComponent<SkyPrisonBlurUVTracker>();
            uvt.Bind(blurRaw, panel);

            // 2. 半透暗色叠加（与背包 0.03, 0.04, 0.05, 0.84 一致）
            var panelBg = panel.gameObject.AddComponent<Image>();
            panelBg.color         = new Color(0.03f, 0.04f, 0.05f, 0.84f);
            panelBg.raycastTarget = true;

            // 3. 四角白色 L 形角标（len=20px, thickness=2px，与背包 AddCornerBrackets 完全一致）
            AddCornerBrackets(panel, Color.white, 20f, 2f);

            // 4. 标题区 + 标题文字
            var titleArea = CreateChildRect(panel, "TitleArea",
                new Vector2(0f, 0.82f), new Vector2(1f, 0.97f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            titleArea.gameObject.AddComponent<Image>().color = Color.clear;
            var titleTxt = AddTMP(titleArea, "Title", "生命维持警告", 34,
                                  TextAlignmentOptions.Center,
                                  new Color(0.75f, 0.42f, 0.42f, 1f), FontStyles.Bold);
            StretchFull(titleTxt.rectTransform);

            // 标题外框（工作台可见；运行时由 FitTitleFrame 精确适配文字宽度）
            var frameRT = CreateChildRect(titleArea, "TitleFrame",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280f, 52f));
            frameRT.SetSiblingIndex(0); // 文字层下方
            frameRT.gameObject.AddComponent<Image>().color = Color.clear;
            AddBorderLines(frameRT, new Color(0.75f, 0.42f, 0.42f, 0.85f), 3.5f);

            // 5. 标题下分割线（白色极淡）
            AddLine(panel, "TitleSep",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, -63f), new Vector2(-40f, 1f),
                new Color(1f, 1f, 1f, 0.08f));

            // 6. 道具选择器区（含箭头 + 视口 + 占位卡片）
            var selArea = CreateChildRect(panel, "ItemSelectorArea",
                new Vector2(0f, 0.28f), new Vector2(1f, 0.78f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            selArea.gameObject.AddComponent<Image>().color = Color.clear;
            BuildSelectorContent(selArea);

            // 7. 警告红字（稍靠下）
            var warnTxt = AddTMP(panel, "Warning", "死亡将会造成背包所持物品的损失", 15,
                                 TextAlignmentOptions.Center,
                                 new Color(0.75f, 0.42f, 0.42f, 0.90f), FontStyles.Normal);
            SetAnchors(warnTxt.rectTransform, 0.05f, 0.19f, 0.95f, 0.25f);

            // 8. 按钮
            MakeButton(panel, "BtnRevive",
                new Vector2(0.06f, 0.05f), new Vector2(0.46f, 0.17f),
                "使用该道具", new Color(0.88f, 0.88f, 0.90f, 1f));

            MakeButton(panel, "BtnDie",
                new Vector2(0.54f, 0.05f), new Vector2(0.94f, 0.17f),
                "返回据点", new Color(0.75f, 0.42f, 0.42f, 1f));
        }

        // ── 角标（完全复刻背包 AddCornerBrackets）────────────────────────────

        private static void AddCornerBrackets(RectTransform target, Color color,
            float len = 20f, float thickness = 2f)
        {
            var corners = new[]
            {
                new Vector2(0f, 1f), // 左上
                new Vector2(1f, 1f), // 右上
                new Vector2(0f, 0f), // 左下
                new Vector2(1f, 0f), // 右下
            };
            foreach (var a in corners)
            {
                // 水平臂
                AddLine(target, "Br_H", a, a, a, Vector2.zero, new Vector2(len, thickness), color);
                // 垂直臂
                AddLine(target, "Br_V", a, a, a, Vector2.zero, new Vector2(thickness, len), color);
            }
        }

        // ── 按钮：透明底 + 白色 1px 外框 + ButtonFeedback ────────────────────

        private static void MakeButton(RectTransform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, string label, Color textColor)
        {
            var rt = CreateChildRect(parent, name,
                anchorMin, anchorMax, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            rt.gameObject.AddComponent<Image>().color = Color.clear;
            rt.gameObject.AddComponent<Button>();

            // 白色 1px 外框（ButtonFeedback 挂上后 hover 整体变冷绿）
            AddOutline(rt, Color.white, 3f);

            // 标签
            var lblRT = CreateChildRect(rt, "Label",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var txt = lblRT.gameObject.AddComponent<TextMeshProUGUI>();
            txt.text          = label;
            txt.fontSize      = 18;
            txt.alignment     = TextAlignmentOptions.Center;
            txt.color         = textColor;
            txt.raycastTarget = false;

            SkyPrisonUIButtonFeedback.Attach(rt.gameObject);
        }

        // ── 线条工具（同背包 AddLine / AddOutline） ───────────────────────────

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
            AddLine(target, "Outline_T", new Vector2(0f,1f), new Vector2(1f,1f), new Vector2(0.5f,1f), Vector2.zero, new Vector2(0f, thickness), color);
            AddLine(target, "Outline_B", new Vector2(0f,0f), new Vector2(1f,0f), new Vector2(0.5f,0f), Vector2.zero, new Vector2(0f, thickness), color);
            AddLine(target, "Outline_L", new Vector2(0f,0f), new Vector2(0f,1f), new Vector2(0f,0.5f), Vector2.zero, new Vector2(thickness, 0f), color);
            AddLine(target, "Outline_R", new Vector2(1f,0f), new Vector2(1f,1f), new Vector2(1f,0.5f), Vector2.zero, new Vector2(thickness, 0f), color);
        }

        // ── 选择器内容 ────────────────────────────────────────────────────────

        private static void BuildSelectorContent(RectTransform area)
        {
            var coldGreen = new Color(0.42f, 0.92f, 0.68f, 0.85f);

            // 左箭头（无边框，细高条，占区域左 8% × 70% 高）
            var btnL = CreateChildRect(area, "ArrowL",
                new Vector2(0f, 0.15f), new Vector2(0.08f, 0.85f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            btnL.gameObject.AddComponent<Image>().color = Color.clear;
            btnL.gameObject.AddComponent<Button>();
            SkyPrisonUIButtonFeedback.Attach(btnL.gameObject);
            var ltxt = AddTMP(btnL, "G", "<", 60, TextAlignmentOptions.Center,
                              new Color(.72f,.72f,.76f,1f), FontStyles.Normal);
            StretchFull(ltxt.rectTransform);

            // 右箭头
            var btnR = CreateChildRect(area, "ArrowR",
                new Vector2(0.92f, 0.15f), new Vector2(1f, 0.85f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            btnR.gameObject.AddComponent<Image>().color = Color.clear;
            btnR.gameObject.AddComponent<Button>();
            SkyPrisonUIButtonFeedback.Attach(btnR.gameObject);
            var rtxt = AddTMP(btnR, "G", ">", 60, TextAlignmentOptions.Center,
                              new Color(.72f,.72f,.76f,1f), FontStyles.Normal);
            StretchFull(rtxt.rectTransform);

            // 视口（Mask）
            var vp = CreateChildRect(area, "Viewport",
                new Vector2(0.10f, 0f), new Vector2(0.90f, 1f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            vp.gameObject.AddComponent<Image>().color = Color.white;
            var mask = vp.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            // 卡片容器
            var cc = CreateChildRect(vp, "CardContainer",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            // 卡片
            var card = CreateChildRect(cc, "Card",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            // 图标（居中，无深色背景框）
            var iconRT = CreateChildRect(card, "Icon",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(120f, 120f));
            iconRT.gameObject.AddComponent<Image>().color = new Color(1f,1f,1f,0f); // 占位

            // 冷绿选框
            var sf = CreateChildRect(iconRT, "SelFrame",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            sf.gameObject.AddComponent<Image>().color = Color.clear;
            AddBorderLines(sf, coldGreen, 1.5f);

            // 数量：叠在图标右下角内（参考物品窗口 ×N 位置）
            // pivot=(1,0) anchor 底右角，anchoredPos.y=0 底边与图标底边齐平
            var cntRT = CreateChildRect(iconRT, "Count",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-4f, 0f), new Vector2(72f, 30f));
            var cntTxt = cntRT.gameObject.AddComponent<TextMeshProUGUI>();
            cntTxt.text = "<size=65%>×</size>1"; cntTxt.fontSize = 26;
            cntTxt.alignment = TextAlignmentOptions.BottomRight;
            cntTxt.color = new Color(.88f,.88f,.90f,1f); cntTxt.raycastTarget = false;
            cntTxt.enableWordWrapping = false;
            cntTxt.richText = true;
            cntTxt.overflowMode = TextOverflowModes.Overflow;
            var font = GetFont(); if (font != null) cntTxt.font = font;

            // 道具名（图标下方）
            var nameTxt = AddTMP(card, "Name", "临时补救模组", 22, TextAlignmentOptions.Center,
                                 new Color(.9f,.9f,.92f,1f), FontStyles.Normal);
            SetAnchors(nameTxt.rectTransform, .05f, .16f, .95f, .38f);

            // GCD
            var gcdTxt = AddTMP(card, "GCD", "", 17, TextAlignmentOptions.Center,
                                new Color(.88f,.58f,.18f,1f), FontStyles.Normal);
            SetAnchors(gcdTxt.rectTransform, .05f, .00f, .95f, .12f);
        }

        // 四边等宽边框线
        private static void AddBorderLines(RectTransform parent, Color c, float px)
        {
            AddLine(parent, "SF_T", new Vector2(0f,1f), new Vector2(1f,1f), new Vector2(.5f,1f), Vector2.zero, new Vector2(0f,px), c);
            AddLine(parent, "SF_B", new Vector2(0f,0f), new Vector2(1f,0f), new Vector2(.5f,0f), Vector2.zero, new Vector2(0f,px), c);
            AddLine(parent, "SF_L", new Vector2(0f,0f), new Vector2(0f,1f), new Vector2(0f,.5f), Vector2.zero, new Vector2(px,0f), c);
            AddLine(parent, "SF_R", new Vector2(1f,0f), new Vector2(1f,1f), new Vector2(1f,.5f), Vector2.zero, new Vector2(px,0f), c);
        }

        // ── TMP 工具（带字体赋值，确保 Editor 预览可见）────────────────────────

        // 缓存字体，避免每次都读磁盘
        private static TMPro.TMP_FontAsset s_cachedFont;
        private static TMPro.TMP_FontAsset GetFont()
        {
            if (s_cachedFont != null) return s_cachedFont;
            const string fontPath = "Assets/_Project/UIUX/Fonts/TMP/ZhouFangRiMingTi-2 SDF.asset";
            s_cachedFont = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(fontPath);
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
            string text, float fontSize, TextAlignmentOptions align,
            Color color, FontStyles style)
        {
            var rt = CreateChildRect(parent, name,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
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

        private static void CopyStyleFromInventory(SkyPrisonUIGlobalStyleSettings_V1 dest)
        {
            if (dest == null) return;

            // 直接按路径加载游戏中文字体（背包同款）
            const string fontPath = "Assets/_Project/UIUX/Fonts/TMP/ZhouFangRiMingTi-2 SDF.asset";
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(fontPath);

            // 兜底：从背包 prefab 读
            if (font == null)
            {
                const string invPath = "Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab";
                var invGO = AssetDatabase.LoadAssetAtPath<GameObject>(invPath);
                var src   = invGO != null ? invGO.GetComponent<SkyPrisonUIGlobalStyleSettings_V1>() : null;
                if (src != null) font = src.defaultTextFont;
            }

            if (font != null)
            {
                dest.defaultTextFont   = font;
                dest.defaultNumberFont = font;
                dest.defaultTextColor  = Color.white;
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
