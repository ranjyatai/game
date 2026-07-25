using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 背包排序控件接线（运行时自愈，挂在背包窗口根节点）：
    ///  - SortDropdown：点击弹出排序项列表（获取时间/等级/分类/重量/价值/名称），选中即更新当前排序字段。
    ///  - SortOrderButton：点击在升序(↑)/降序(↓)间切换。
    ///  - TidyButton(整理)：点击时才真正排序——合并相同物品到满组 + 按当前字段/方向排序 + 紧凑空格。
    ///
    /// 弹层走独立高层级 Canvas(1102)，盖过色收差快照层(1101)，避免被静止快照挡住、延迟出现。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonInventorySortControls : MonoBehaviour
    {
        // 之前这几个排序选项的文字是直接硬编码在这份 Option 数组里，从没接过
        // UILocalizationTable，不管切什么语言这个下拉框永远显示中文。改成存
        // locKey + 中文兜底，实际显示时走 L() 查表解析。
        private readonly struct Option
        {
            public readonly InventorySortField field;
            public readonly string locKey;
            public readonly string fallback;
            public Option(InventorySortField f, string key, string fb) { field = f; locKey = key; fallback = fb; }
        }

        private static readonly Option[] Options =
        {
            new Option(InventorySortField.AcquireTime, "ui_sort_time",     "获取时间"),
            new Option(InventorySortField.Level,       "ui_sort_level",    "等级"),
            new Option(InventorySortField.Category,    "ui_sort_category", "分类"),
            new Option(InventorySortField.Weight,      "ui_sort_weight",   "重量"),
            new Option(InventorySortField.Value,       "ui_sort_value",    "价值"),
            new Option(InventorySortField.Name,        "ui_sort_name",     "名称"),
        };

        private UILocalizationTable _locTable;
        private bool _locResolved;

        private string L(string key, string fallback)
        {
            if (!_locResolved)
            {
                var loc = GetComponentInParent<SkyPrisonInventoryTextLocalizer>()
                       ?? GetComponentInChildren<SkyPrisonInventoryTextLocalizer>(true);
                _locTable = loc != null ? loc.Table : null;
                _locResolved = true;
            }
            return _locTable != null ? _locTable.Get(key, fallback) : fallback;
        }

        private static readonly Color HoverGreen = new Color(0.42f, 0.92f, 0.68f, 0.85f);
        private static readonly Color RowNormal  = Color.white;

        private InventoryRuntime _inv;
        private InventorySortField _field = InventorySortField.AcquireTime;
        private bool _ascending = true;

        // 2026-07-21：默认服务玩家背包——那份 InventoryRuntime 一局内固定不变，缓存一次
        // 够用。仓库不一样，切页签当前生效的 InventoryRuntime 会变，需要每次都重新问，
        // 不能缓存。仓库窗口调 SetInventorySourceOverride 传一个"取当前页"的委托，覆盖
        // 默认的"取玩家背包"行为；传 null 恢复默认。
        private System.Func<InventoryRuntime> _inventorySourceOverride;

        /// <summary>供仓库等"当前生效数据源会变化"的窗口调用。resolver 每次 ResolveInventory
        /// 都会重新调用，不会被缓存结果卡住——仓库切页签后排序/整理要立刻作用在新的当前页
        /// 上，不能沿用切页签之前缓存的旧引用。</summary>
        public void SetInventorySourceOverride(System.Func<InventoryRuntime> resolver)
        {
            _inventorySourceOverride = resolver;
            _inv = null;
        }

        private RectTransform _dropdownRect;
        private Text _dropdownLabel;
        private Text _orderArrow;
        private Font _font;
        private GameObject _popup;
        private CanvasGroup _popupCg;
        private Coroutine _unfold;
        private RectTransform _list;
        private float _listWidth, _headerH, _fullH;
        private bool _closing;
        private bool _wired;
        private CanvasGroup _rootCg;

        private void OnEnable()
        {
            Setup();
            LocalizationRuntime.OnLanguageChanged += OnLanguageChanged;
        }

        private void OnDisable()
        {
            LocalizationRuntime.OnLanguageChanged -= OnLanguageChanged;
            DestroyPopupImmediate();
        }

        private void OnLanguageChanged(string _)
        {
            DestroyPopupImmediate(); // 弹层里的选项文字是打开那一刻现造的，直接关掉，不用另外刷新
            UpdateDropdownLabel();
        }

        private void Update()
        {
            if (_popup == null) return;

            // CanvasGroup 由 WindowManager 在 Instantiate 之后动态挂上，OnEnable 时还不存在，懒加载。
            if (_rootCg == null) _rootCg = GetComponent<CanvasGroup>();
            if (_rootCg == null) return;

            // popup 的 CanvasGroup alpha 逐帧镜像父窗口 alpha，完全同步淡出，不靠阈值判断。
            float a = _rootCg.alpha;
            _popupCg.alpha = a;
            _popupCg.blocksRaycasts = a > 0.5f;
            if (a < 0.01f) DestroyPopupImmediate();
        }

        private void Setup()
        {
            if (_wired) { ResolveInventory(); return; }

            Transform sortDropdown = FindDeep(transform, "SortDropdown");
            Transform sortOrderBtn = FindDeep(transform, "SortOrderButton");
            Transform tidyBtn      = FindDeep(transform, "TidyButton");
            if (sortDropdown == null || tidyBtn == null)
                return;

            _dropdownRect = (RectTransform)sortDropdown;
            _font = FindUsableFont();

            // 下拉框：加文字标签 + 点击弹列表
            _dropdownLabel = EnsureLabel(sortDropdown, "Label", 18);
            Button ddBtn = sortDropdown.GetComponent<Button>() ?? sortDropdown.gameObject.AddComponent<Button>();
            ddBtn.transition = Selectable.Transition.None;
            ddBtn.onClick.RemoveAllListeners();
            ddBtn.onClick.AddListener(TogglePopup);
            SkyPrisonUIButtonFeedback.Attach(sortDropdown.gameObject);

            // 升/降序按钮
            if (sortOrderBtn != null)
            {
                Transform arrow = sortOrderBtn.Find("Label");
                _orderArrow = arrow != null ? arrow.GetComponent<Text>() : null;
                Button ordBtn = sortOrderBtn.GetComponent<Button>() ?? sortOrderBtn.gameObject.AddComponent<Button>();
                ordBtn.transition = Selectable.Transition.None;
                ordBtn.onClick.RemoveAllListeners();
                ordBtn.onClick.AddListener(ToggleOrder);
                SkyPrisonUIButtonFeedback.Attach(sortOrderBtn.gameObject);
            }

            // 整理按钮
            Button tBtn = tidyBtn.GetComponent<Button>() ?? tidyBtn.gameObject.AddComponent<Button>();
            tBtn.transition = Selectable.Transition.None;
            tBtn.onClick.RemoveAllListeners();
            tBtn.onClick.AddListener(Tidy);
            SkyPrisonUIButtonFeedback.Attach(tidyBtn.gameObject); // 连框带文字一起染绿

            ResolveInventory();
            UpdateDropdownLabel();
            UpdateOrderArrow();
            _wired = true;
        }

        private void ResolveInventory()
        {
            if (_inventorySourceOverride != null)
            {
                _inv = _inventorySourceOverride();
                return;
            }

            if (_inv != null) return;
            _inv = InventoryRuntimeBootstrap.Instance != null
                ? InventoryRuntimeBootstrap.Instance.Inventory
                : FindObjectOfType<InventoryRuntime>();
        }

        private void ToggleOrder()
        {
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            _ascending = !_ascending;
            UpdateOrderArrow();
        }

        private void SelectField(InventorySortField f)
        {
            SkyPrisonSystemSEPlayer.Play(f == _field ? SkyPrisonSystemSEType.Confirm : SkyPrisonSystemSEType.Switch);
            _field = f;
            UpdateDropdownLabel();
            ClosePopup();
        }

        private void Tidy()
        {
            ResolveInventory();
            _inv?.TidyUp(_field, _ascending);
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Tidy);
        }

        // ── 手柄入口（供 SkyPrisonInventoryGamepad 调用）──────────────────────

        public void GamepadTidy() => Tidy();

        public void GamepadToggleOrder()
        {
            ToggleOrder();
        }

        public void GamepadCycleSortField()
        {
            // 循环到下一个排序字段
            int cur = 0;
            for (int i = 0; i < Options.Length; i++)
                if (Options[i].field == _field) { cur = i; break; }
            int next = (cur + 1) % Options.Length;
            _field = Options[next].field;
            UpdateDropdownLabel();
        }

        private void UpdateOrderArrow()
        {
            if (_orderArrow != null) _orderArrow.text = _ascending ? "↑" : "↓";
        }

        private void UpdateDropdownLabel()
        {
            if (_dropdownLabel != null) _dropdownLabel.text = LabelFor(_field);
        }

        private string LabelFor(InventorySortField f)
        {
            for (int i = 0; i < Options.Length; i++)
                if (Options[i].field == f) return L(Options[i].locKey, Options[i].fallback);
            return f.ToString();
        }

        // ── 弹出列表 ──────────────────────────────────────────────────────────

        private void TogglePopup()
        {
            if (_closing) return;
            if (_popup != null) { ClosePopup(); return; }
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
            BuildPopup();
        }

        // 动画收拢（高度从满高回到顶格）再销毁，和展开对称、有连续感。
        private void ClosePopup()
        {
            if (_popup == null || _closing) return;
            if (_unfold != null) { StopCoroutine(_unfold); _unfold = null; }
            _closing = true;
            StartCoroutine(CollapseAndDestroy());
        }

        private System.Collections.IEnumerator CollapseAndDestroy()
        {
            const float dur = 0.12f;
            float from = _list != null ? _list.sizeDelta.y : _fullH;
            float t = 0f;
            while (t < dur && _list != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                _list.sizeDelta = new Vector2(_listWidth, Mathf.Lerp(from, 0f, k));
                yield return null;
            }
            KillPopup();
            _closing = false;
        }

        /// <summary>窗口开始播关闭动画的那一刻就调用——弹层是单独建在最外层Canvas上的，
        /// 不是窗口的子物体，窗口自己的关闭压缩动画完全影响不到它，如果等窗口动画播完
        /// 真正销毁那一刻（OnDisable）才清掉，弹层会在窗口收缩动画播放期间一直悬在
        /// 原地不跟着关，看起来像"背包关了，弹层没关"。SkyPrisonWindowManager_V1.Close()
        /// 在开始关闭动画之前调这个，弹层立刻消失，不用等动画播完。</summary>
        public void ForceCloseDropdown() => DestroyPopupImmediate();

        // 窗口关闭等：立刻销毁，不做动画。
        private void DestroyPopupImmediate()
        {
            if (_unfold != null) { StopCoroutine(_unfold); _unfold = null; }
            StopAllCoroutines();
            _closing = false;
            KillPopup();
        }

        // 立即禁用 Canvas（当帧停止渲染）再 Destroy（帧末 GC），避免 Destroy 延迟导致多渲染一帧留下残影。
        private void KillPopup()
        {
            if (_popup == null) return;
            var c = _popup.GetComponent<Canvas>();
            if (c != null) c.enabled = false;
            Destroy(_popup);
            _popup = null;
            _list = null;
            _popupCg = null;
        }

        private void BuildPopup()
        {
            if (_dropdownRect == null) return;

            // 下拉框的屏幕矩形（适配 Overlay/Camera 两种窗口画布）。
            Canvas ddCanvas = _dropdownRect.GetComponentInParent<Canvas>();
            Camera cam = (ddCanvas != null && ddCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? (ddCanvas.worldCamera != null ? ddCanvas.worldCamera : Camera.main)
                : null;

            var corners = new Vector3[4];
            _dropdownRect.GetWorldCorners(corners); // 0=左下 1=左上 2=右上 3=右下
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 tl = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            Vector2 brc = RectTransformUtility.WorldToScreenPoint(cam, corners[3]);
            float width = Mathf.Max(60f, Mathf.Abs(brc.x - bl.x));
            float boxH  = Mathf.Abs(tl.y - bl.y);
            float rowH  = Mathf.Max(24f, boxH > 1f ? boxH * 0.95f : 34f);
            int fontSize = Mathf.Max(12, Mathf.RoundToInt(rowH * 0.48f));

            // 独立 Overlay Canvas：Overlay 永远盖过 Camera 画布，确保盖过色收差快照(1101)、即时可点。
            var go = new GameObject("[InvSort] Popup", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1102;
            go.AddComponent<GraphicRaycaster>();
            // CanvasGroup：用于 Update 里逐帧镜像父窗口 alpha，确保随窗口淡出完全同步。
            _popupCg = go.AddComponent<CanvasGroup>();
            _popupCg.alpha = _rootCg != null ? _rootCg.alpha : 1f;

            // 全屏透明遮罩：点列表外侧即关闭。
            var blockerGo = new GameObject("Blocker", typeof(RectTransform));
            blockerGo.transform.SetParent(go.transform, false);
            RectTransform blocker = (RectTransform)blockerGo.transform;
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one;
            blocker.offsetMin = Vector2.zero; blocker.offsetMax = Vector2.zero;
            var blockerImg = blockerGo.AddComponent<Image>();
            blockerImg.color = new Color(0f, 0f, 0f, 0f);
            blockerImg.raycastTarget = true;
            var blockerBtn = blockerGo.AddComponent<Button>();
            blockerBtn.transition = Selectable.Transition.None;
            blockerBtn.onClick.AddListener(ClosePopup);

            // 盒子：顶边对齐下拉框「顶边」→ 看起来就是下拉框本身向下展开（连四周描边一起延伸）。
            var listGo = new GameObject("List", typeof(RectTransform));
            listGo.transform.SetParent(go.transform, false);
            RectTransform list = (RectTransform)listGo.transform;

            float headerH = boxH > 1f ? boxH : rowH;     // 顶部一格 = 原下拉框（显示当前项，盖住原框只显示一次）
            int optionCount = Options.Length - 1;          // 当前项不重复，只列其余
            float fullH = headerH + rowH * optionCount;
            list.pivot = new Vector2(0f, 1f);
            list.position = new Vector3(tl.x, tl.y, 0f);   // 下拉框顶边左角
            list.sizeDelta = new Vector2(width, headerH);  // 从「只有当前项一格」开始，向下展开

            // 内容层（带裁剪，随盒子高度逐行显现）；描边也放在内容层内，随裁剪一起消失，不扫线。
            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(list, false);
            RectTransform content = (RectTransform)contentGo.transform;
            content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            contentGo.AddComponent<RectMask2D>();

            // 背景：不画暗色填充块，而是「沿用面板磨砂背景」遮住下面的格子——满高固定、由裁剪逐行显现。
            BuildPopupBackdrop(content, tl, width, fullH);

            // 顶部一格：当前项（绿色高亮，点它收起）
            BuildPopupRow(content, "Header", LabelFor(_field), 0f, headerH, fontSize, HoverGreen, _field, true);

            // 其余项：从 headerH 往下排
            int idx = 0;
            for (int i = 0; i < Options.Length; i++)
            {
                if (Options[i].field == _field) continue;
                BuildPopupRow(content, "Opt_" + Options[i].field, L(Options[i].locKey, Options[i].fallback),
                    headerH + rowH * idx, rowH, fontSize, RowNormal, Options[i].field, false);
                idx++;
            }

            // 描边放在 content 内（RectMask2D 层），随内容一起被裁剪，收起时不会出现扫过去的边框线。
            AddPopupBorder(content);

            _popup = go;
            _list = list; _listWidth = width; _headerH = headerH; _fullH = fullH;
            if (_unfold != null) StopCoroutine(_unfold);
            _unfold = StartCoroutine(UnfoldList(list, width, headerH, fullH));
        }

        // 弹层背景：不用暗色块，而是复用面板那张磨砂 RawImage（同纹理/材质/亮度），
        // 并按弹层屏幕矩形采样对应区域 → 看起来就是面板磨砂的延续，且把下面的格子遮住。
        private void BuildPopupBackdrop(RectTransform content, Vector2 screenTopLeft, float width, float fullH)
        {
            var backdropGo = new GameObject("Backdrop", typeof(RectTransform));
            backdropGo.transform.SetParent(content, false);
            RectTransform backdrop = (RectTransform)backdropGo.transform;
            backdrop.anchorMin = new Vector2(0f, 1f);
            backdrop.anchorMax = new Vector2(1f, 1f);
            backdrop.pivot = new Vector2(0.5f, 1f);
            backdrop.anchoredPosition = Vector2.zero;
            backdrop.sizeDelta = new Vector2(0f, fullH);

            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);

            RawImage panelBlur = FindPanelBlurImage();
            if (panelBlur != null && panelBlur.texture != null)
            {
                var blurGo = new GameObject("Blur", typeof(RectTransform));
                blurGo.transform.SetParent(backdrop, false);
                RectTransform brt = (RectTransform)blurGo.transform;
                brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
                brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
                var raw = blurGo.AddComponent<RawImage>();
                raw.texture = panelBlur.texture;
                raw.material = panelBlur.material;
                // 强制 alpha=1，确保磨砂层完全遮住背后的背包格子
                raw.color = new Color(panelBlur.color.r, panelBlur.color.g, panelBlur.color.b, 1f);
                raw.uvRect = new Rect(screenTopLeft.x / sw, (screenTopLeft.y - fullH) / sh, width / sw, fullH / sh);
                raw.raycastTarget = false;
            }
            else
            {
                // 无磨砂纹理时用深色兜底，仍然遮住格子
                var fallbackGo = new GameObject("FallbackBg", typeof(RectTransform));
                fallbackGo.transform.SetParent(backdrop, false);
                RectTransform frt = (RectTransform)fallbackGo.transform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
                var fallback = fallbackGo.AddComponent<Image>();
                fallback.color = new Color(0.08f, 0.09f, 0.10f, 1f);
                fallback.raycastTarget = false;
            }

            // 极淡冷调叠层：与面板气质一致但不遮住磨砂质感；raycastTarget=true 吸收弹层内空白点击
            var tintGo = new GameObject("Tint", typeof(RectTransform));
            tintGo.transform.SetParent(backdrop, false);
            RectTransform trt = (RectTransform)tintGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var tint = tintGo.AddComponent<Image>();
            tint.color = new Color(0.06f, 0.08f, 0.10f, 0.18f); // 极淡冷调，不黑
            tint.raycastTarget = true;

            // 底部描边：固定在 Backdrop 底部（fullH 处），随 RectMask2D 裁剪消失，不会向上扫。
            var borderB = new GameObject("Border_B", typeof(RectTransform));
            borderB.transform.SetParent(backdropGo.transform, false);
            RectTransform bBrt = (RectTransform)borderB.transform;
            bBrt.anchorMin = new Vector2(0f, 0f); bBrt.anchorMax = new Vector2(1f, 0f);
            bBrt.pivot = new Vector2(0.5f, 0f);
            bBrt.anchoredPosition = Vector2.zero;
            bBrt.sizeDelta = new Vector2(0f, 2f);
            borderB.AddComponent<Image>().color = Color.white;
        }

        private RawImage FindPanelBlurImage()
        {
            // 背包手搭的磨砂节点叫"BlurBackground"，但 SkyPrisonFloatingWindowKit.
            // BuildBlurBackground()（仓库/其它代码搭建的窗口都走这个）建出来的节点叫
            // "Blur"，两个名字不一样。这里之前只认"BlurBackground"，仓库这边永远查
            // 不到，直接落到深色兜底填充——跟 InventoryItemDetailPanel 之前踩过的是
            // 同一个坑，这里一直没同步修。两个名字都要试。
            Transform t = FindDeep(transform, "BlurBackground") ?? FindDeep(transform, "Blur");
            return t != null ? t.GetComponent<RawImage>() : null;
        }

        private void BuildPopupRow(RectTransform parent, string name, string text, float y, float h,
            int fontSize, Color color, InventorySortField field, bool isHeader)
        {
            var rowGo = new GameObject(name, typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            RectTransform row = (RectTransform)rowGo.transform;
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, -y);
            row.sizeDelta = new Vector2(0f, h);

            var rowImg = rowGo.AddComponent<Image>();
            rowImg.color = new Color(1f, 1f, 1f, 0f);
            rowImg.raycastTarget = true;
            var rowBtn = rowGo.AddComponent<Button>();
            rowBtn.transition = Selectable.Transition.None;
            if (isHeader)
                rowBtn.onClick.AddListener(ClosePopup);
            else
            {
                InventorySortField f = field;
                rowBtn.onClick.AddListener(() => SelectField(f));
            }

            Text label = EnsureLabel(row, "Text", fontSize);
            label.text = text;
            label.color = color;

            if (!isHeader)
            {
                var hover = rowGo.AddComponent<SkyPrisonSortRowHover>();
                hover.Configure(label, color, HoverGreen);
            }
        }

        private static void AddPopupBorder(RectTransform box)
        {
            AddEdge(box, "Border_T", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 2f));
            AddEdge(box, "Border_L", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(2f, 0f));
            AddEdge(box, "Border_R", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(2f, 0f));
            // Border_B 不放在这里（会跟着 content 底边向上扫），改放在 Backdrop 内固定位置
        }

        private static void AddEdge(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;
        }

        // 从「当前项一格」向下展开到满高，配合内容层 RectMask2D 逐行显现，描边随盒子伸缩。
        private System.Collections.IEnumerator UnfoldList(RectTransform list, float width, float fromH, float toH)
        {
            const float dur = 0.14f;
            float t = 0f;
            while (t < dur && list != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                list.sizeDelta = new Vector2(width, Mathf.Lerp(fromH, toH, k));
                yield return null;
            }
            if (list != null) list.sizeDelta = new Vector2(width, toH);
            _unfold = null;
        }

        // ── 工具 ──────────────────────────────────────────────────────────────

        private Text EnsureLabel(Transform parent, string childName, int fontSize)
        {
            Transform existing = parent.Find(childName);
            Text text = existing != null ? existing.GetComponent<Text>() : null;
            if (text == null)
            {
                var go = new GameObject(childName, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                text = go.AddComponent<Text>();
            }
            if (_font != null) text.font = _font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        // 复用窗口里已有 Text 的字体（旧版 Text 需要 Font 才能渲染中文）。
        private Font FindUsableFont()
        {
            Text[] texts = GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
                if (texts[i] != null && texts[i].font != null)
                    return texts[i].font;
            return null;
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
    }

    /// <summary>排序项悬停高亮：hover 时文字变冷绿，移开恢复。当前选中项常驻冷绿。</summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonSortRowHover : MonoBehaviour,
        UnityEngine.EventSystems.IPointerEnterHandler,
        UnityEngine.EventSystems.IPointerExitHandler
    {
        private Text _label;
        private Color _normal;
        private Color _hover;

        public void Configure(Text label, Color normal, Color hover)
        {
            _label = label; _normal = normal; _hover = hover;
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e)
        {
            if (_label != null) _label.color = _hover;
        }

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e)
        {
            if (_label != null) _label.color = _normal;
        }
    }
}
