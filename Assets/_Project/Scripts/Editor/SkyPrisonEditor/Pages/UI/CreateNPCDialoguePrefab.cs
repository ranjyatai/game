#if UNITY_EDITOR
using SkyPrison.Runtime.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Editor.UI
{
    /// <summary>
    /// 创建通用NPC对话窗口 PF_NPCDialogue——所有NPC共用同一个prefab，
    /// NPCDialogueWindowController.Begin() 在打开后现场喂具体是哪个NPC/说什么。
    /// 视觉规格：固定右下角、透明背景、左侧极细竖线，标题(NPC名字，冷绿，左对齐)
    /// 在上，选项列表(左对齐，随数量自适应高度)在下，没有模糊底板/角标/关闭按钮
    /// 那套弹窗外壳——台词改走 DialogueSubtitleHUD(血条上方字幕)，这个面板只管
    /// 名字+选项。
    /// </summary>
    public static class CreateNPCDialoguePrefab
    {
        public const string PrefabPath    = "Assets/_Project/Prefabs/UI/Window/PF_NPCDialogue.prefab";
        public const string ResMirrorPath = "Assets/Resources/UI/Window/PF_NPCDialogue.prefab";
        private const float PanelWidth = 620f;
        // 面板固定高度(够放标题+大约6-7条选项)，不再跟着选项数量用ContentSizeFitter
        // 自动变高——选项少就底下空着，选项数量变化再也不会带着标题一起挪位置。
        private const float PanelHeight = 560f;

        [MenuItem("天空囚笼/UI/Create NPC Dialogue Window", false, 172)]
        public static void Create()
        {
            var root = new GameObject("PF_NPCDialogue");
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
            meta.uiId              = "npc_dialogue";
            meta.displayName       = "NPC对话";
            meta.kind              = SkyPrisonUIPrefabKindV1.Popup;
            meta.blocksRaycasts    = true;
            meta.lockGameplayInput = true;
            meta.showMouseCursor   = true;
            meta.inputModeWhenOpen = SkyPrisonUIInputModeV1.UI;

            CopyStyleFromInventory(root.GetComponent<SkyPrisonUIGlobalStyleSettings_V1>());

            // ── 窗口面板（固定在屏幕右侧偏下，透明背景，随内容自适应高度）────────────
            // 参考风格：只有名字标题+选项列表，没有模糊底板/边角标/关闭按钮那套弹窗外壳，
            // 台词改用 DialogueSubtitleHUD(血条上方字幕)，这个面板只负责名字+选项。
            //
            // 锚点钉在右上(1,1)，面板本身是固定尺寸(宽PanelWidth×高PanelHeight)——
            // 之前用ContentSizeFitter自适应高度，选项数量一变面板高度跟着变，就算
            // 锚点在顶上、pivot全程也没保持住，标题还是会跟着上下跳。改成固定尺寸后
            // 标题的位置完全不受选项数量影响，选项少就下面空着，选项多也不会撑爆
            // (PanelHeight按最多6-7条选项留了余量)。
            var panel = CreateChildRect(rootRT, "WindowPanel",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-480f, -1338f), new Vector2(PanelWidth, PanelHeight));

            var panelVl = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            panelVl.padding = new RectOffset(16, 24, 16, 20);
            panelVl.spacing = 14f;
            panelVl.childAlignment = TextAnchor.UpperLeft;
            panelVl.childControlWidth = true; panelVl.childForceExpandWidth = true;
            panelVl.childControlHeight = true; panelVl.childForceExpandHeight = false;

            // 标题行——NPC自己的名字，比选项更靠左上（不跟竖线/选项共用缩进），
            // 背景是左侧干净、向右暗淡淡出的半透明黑条状底色(渐变Sprite由
            // NPCDialogueWindowController在运行时生成再填进来，编辑器脚本这里现生成
            // 的Texture存不进prefab资产，只能留空等运行时接上)。试过黑白+高斯模糊，
            // 观感不明显不值当那份开销，改回最简单的纯色。
            var headerRowGo = new GameObject("HeaderRow", typeof(RectTransform));
            headerRowGo.transform.SetParent(panel, false);
            headerRowGo.AddComponent<LayoutElement>().preferredHeight = 68f;

            var headerBgRt = CreateChildRect((RectTransform)headerRowGo.transform, "HeaderBackground",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var headerBgImg = headerBgRt.gameObject.AddComponent<Image>();
            headerBgImg.raycastTarget = false;

            var headerTextRt = CreateChildRect((RectTransform)headerRowGo.transform, "HeaderText",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            headerTextRt.offsetMin = new Vector2(14f, 0f);
            var headerTmp = AddTMPComponent(headerTextRt, "NPC", 48,
                TextAlignmentOptions.MidlineLeft, SkyPrisonUIPalette.ColdGreen, FontStyles.Normal);

            // 句子文本——现在只给"任务列表"浏览态用，正常对话台词走 DialogueSubtitleHUD，
            // 默认不占空间，SetActive(false) 由 NPCDialogueWindowController 在进任务列表时打开。
            var sentenceGo = new GameObject("SentenceText", typeof(RectTransform));
            sentenceGo.transform.SetParent(panel, false);
            sentenceGo.AddComponent<LayoutElement>().preferredHeight = 100f;
            var sentenceTmp = AddTMPComponent((RectTransform)sentenceGo.transform, "", 28,
                TextAlignmentOptions.TopLeft, new Color(0.88f, 0.88f, 0.90f, 1f), FontStyles.Normal);
            sentenceTmp.enableWordWrapping = true;
            sentenceGo.SetActive(false);

            // 选项列表——数量不固定，运行时由 NPCDialogueWindowController 现场搭按钮；
            // 高度跟着选项数量自适应，不用滚动条(参考图里的列表本来就短)。竖线+聚焦
            // 方块都挂在这个容器下(不是标题所在的panel)，所以只跟选项区一样高，
            // 且比标题缩进更靠右——名字不跟选项共用同一条竖线缩进。
            var optionsAreaGo = new GameObject("OptionsArea", typeof(RectTransform));
            optionsAreaGo.transform.SetParent(panel, false);
            var optionsAreaRt = (RectTransform)optionsAreaGo.transform;
            var optionsVl = optionsAreaGo.AddComponent<VerticalLayoutGroup>();
            optionsVl.padding = new RectOffset(44, 0, 4, 4);
            optionsVl.spacing = 4f;
            optionsVl.childAlignment = TextAnchor.UpperLeft;
            optionsVl.childControlWidth = true; optionsVl.childForceExpandWidth = true;
            optionsVl.childControlHeight = false; optionsVl.childForceExpandHeight = false;
            var optionsFit = optionsAreaGo.AddComponent<ContentSizeFitter>();
            optionsFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            optionsFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 左侧一条极细竖线，只贴着选项区这段高度（不含标题），随选项数量自适应
            // 高度一起拉伸，不用逐帧代码。
            var accentLineRt = CreateChildRect(optionsAreaRt, "LeftAccentLine",
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(20f, 0f), new Vector2(2f, 0f));
            accentLineRt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var accentLineImg = accentLineRt.gameObject.AddComponent<Image>();
            accentLineImg.color = new Color(1f, 1f, 1f, 0.35f);
            accentLineImg.raycastTarget = false;

            // ■ 聚焦标记——跟着当前聚焦选项的位置上下滑动，就落在竖线的位置上，
            // 忽略布局自己摆放。颜色用白色(不用冷绿——聚焦已经靠选项文字变绿+放大
            // 来表示，方块跟文字一起变绿反而重复)。
            var markerRt = CreateChildRect(optionsAreaRt, "SelectionMarker",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 1f),
                new Vector2(21f, -6f), new Vector2(18f, 18f));
            markerRt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var markerImg = markerRt.gameObject.AddComponent<Image>();
            markerImg.color = SkyPrisonUIPalette.White;
            markerImg.raycastTarget = false;

            // ── 组件挂载 ────────────────────────────────────────────────────
            var controller = root.AddComponent<NPCDialogueWindowController>();
            var so = new SerializedObject(controller);
            so.FindProperty("headerText").objectReferenceValue = ConvertToLegacyIfNeeded(headerTmp);
            so.FindProperty("headerBackground").objectReferenceValue = headerBgImg;
            so.FindProperty("sentenceText").objectReferenceValue = ConvertToLegacyIfNeeded(sentenceTmp);
            so.FindProperty("optionsContainer").objectReferenceValue = optionsAreaRt;
            so.FindProperty("selectionMarker").objectReferenceValue = markerRt;
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
                Debug.Log($"[NPCDialogue] Prefab 已创建：{PrefabPath}");
            }
            else
                Debug.LogError("[NPCDialogue] Prefab 保存失败！");

            Object.DestroyImmediate(root);
        }

        // ── TMP→legacy Text 转换(跟工房修理弹窗生成器同一份写法)────────────────
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
            legacyText.alignment = align switch
            {
                TextAlignmentOptions.Left or TextAlignmentOptions.MidlineLeft or TextAlignmentOptions.BottomLeft or TextAlignmentOptions.TopLeft => TextAnchor.UpperLeft,
                TextAlignmentOptions.Right or TextAlignmentOptions.MidlineRight or TextAlignmentOptions.BottomRight or TextAlignmentOptions.TopRight => TextAnchor.MiddleRight,
                _ => TextAnchor.MiddleCenter,
            };
            legacyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            legacyText.verticalOverflow = VerticalWrapMode.Overflow;
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

        // ── TMP 工具 ────────────────────────────────────────────────────────
        private static TMP_FontAsset s_cachedFont;
        private static TMP_FontAsset GetFont()
        {
            if (s_cachedFont != null) return s_cachedFont;
            const string fontPath = "Assets/_Project/UIUX/Fonts/TMP/ZhouFangRiMingTi-2 SDF.asset";
            s_cachedFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            return s_cachedFont;
        }

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
