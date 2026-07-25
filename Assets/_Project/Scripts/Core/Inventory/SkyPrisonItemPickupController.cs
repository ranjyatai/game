using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 世界掉落物拾取控制器。
    /// 提示条样式与 SkyPrisonWindowHintBar 保持一致：
    ///   ScreenSpaceOverlay + WorldToScreenPoint 定位，
    ///   HorizontalLayoutGroup + ContentSizeFitter 自动布局，
    ///   Image 键帽图标 (34px) + Text/TMP 文字 (20px)，
    ///   物品名用品级颜色，LV9 用 RainbowTextEffect，字体取全局样式设置。
    /// </summary>
    [DefaultExecutionOrder(9000)]
    [DisallowMultipleComponent]
    public sealed class SkyPrisonItemPickupController : MonoBehaviour
    {
        private const int   IconSize   = 68;
        private const int   FontSize   = 40;
        private const int   SortOrder  = 1140;

        [SerializeField] private float pickupRadius = 2.4f;
        [SerializeField] private float promptHeight = 2.2f;

        private SkyPrisonInputSettings           _input;
        private SkyPrisonWindowManager_V1        _windows;
        private SkyPrisonInputPromptIconDatabase _iconDb;
        private UILocalizationTable              _locTable;

        private readonly List<LootDropWorldObject> _inRange = new();
        private int                 _selectedIdx;
        private LootDropWorldObject _currentTarget;

        // ── 提示 UI ───────────────────────────────────────────────────────────
        private Canvas          _canvas;
        private RectTransform   _panel;
        private Image           _interactIcon;
        private Text            _actionToken;
        private Text            _prefixTxt;       // "拾取" 前缀（legacy Text）
        private TMP_Text        _itemNameTxt;     // 物品名 + 数量（TMP，支持 RainbowTextEffect）
        private RainbowTextEffect _rainbow;
        private GameObject      _cycleRow;
        private Image           _cycleIcon;
        private Text            _cycleToken;
        private Text            _cycleLabelTxt;
        private static Sprite   _fadeSprite;
        private bool            _tmpFontBound;

        private LootDropWorldObject _promptFor;
        private int                 _promptCount;

        // Sort 比较器缓存：避免每帧 lambda 闭包分配
        private Vector3 _sortRefPos;
        private System.Comparison<LootDropWorldObject> _sortComparison;

        // Camera 缓存
        private Camera _cam;

        // ── 生命周期 ──────────────────────────────────────────────────────────

        private void Awake()
        {
            _sortComparison = (a, b) =>
                (a.transform.position - _sortRefPos).sqrMagnitude.CompareTo(
                (b.transform.position - _sortRefPos).sqrMagnitude);
        }

        private void OnEnable()
        {
            SkyPrisonInputDeviceTracker.OnDeviceFamilyChanged += OnDeviceChanged;
        }

        private void OnDisable()
        {
            SkyPrisonInputDeviceTracker.OnDeviceFamilyChanged -= OnDeviceChanged;
        }

        private void OnDeviceChanged(SkyPrisonInputDeviceTracker.DeviceFamily _,
                                     SkyPrisonInputDeviceTracker.DeviceFamily __)
        {
            _promptFor = null;
        }

        private void Update()
        {
            GameObject playerGo = SkyPrisonPlayerAuthority.CurrentPlayerUnit != null
                ? SkyPrisonPlayerAuthority.CurrentPlayerUnit.gameObject : null;

            if (playerGo == null) { ClearSelection(); HidePrompt(); return; }
            // HasAnyWindowOpen() 只认走 windowManager.Open() 注册过的窗口，设置窗口/暂停菜单
            // 这类纯手写全屏窗口只设 ExternalBlock 静态标记，漏了这条会导致那些窗口开着的
            // 时候按 E 照样能拾取东西——跟 SkyPrisonInteractionController 同一个 bug。
            if ((Windows() != null && _windows.HasAnyWindowOpen()) || SkyPrisonWindowManager_V1.AnyBlockingWindowOpen)
                { ClearSelection(); HidePrompt(); return; }

            RefreshInRange(playerGo.transform.position);

            if (_inRange.Count == 0) { ClearSelection(); HidePrompt(); return; }

            _selectedIdx = Mathf.Clamp(_selectedIdx, 0, _inRange.Count - 1);
            LootDropWorldObject target = _inRange[_selectedIdx];

            if (target != _currentTarget)
            {
                _currentTarget?.GetComponent<LootDropVisual>()?.SetSelected(false);
                _currentTarget = target;
                _currentTarget.GetComponent<LootDropVisual>()?.SetSelected(true);
            }

            ShowPrompt(target, _inRange.Count);

            SkyPrisonInputSettings input = GetInput();
            if (input == null) return;

            if (input.GetActionDown(SkyPrisonInputAction.CyclePickup) && _inRange.Count > 1)
            {
                _selectedIdx = (_selectedIdx + 1) % _inRange.Count;
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            }

            if (input.GetActionDown(SkyPrisonInputAction.Interact))
                TryPickup(target);
        }

        // ── 范围检测 ──────────────────────────────────────────────────────────

        private void RefreshInRange(Vector3 pos)
        {
            _inRange.Clear();
            float r2 = pickupRadius * pickupRadius;
            var list = LootDropWorldObject.Active;
            for (int i = 0; i < list.Count; i++)
            {
                LootDropWorldObject l = list[i];
                if (l == null || l.Item == null) continue;
                if ((l.transform.position - pos).sqrMagnitude <= r2)
                    _inRange.Add(l);
            }
            _sortRefPos = pos;
            _inRange.Sort(_sortComparison);

            if (_currentTarget != null && !_inRange.Contains(_currentTarget))
            {
                _currentTarget.GetComponent<LootDropVisual>()?.SetSelected(false);
                _currentTarget = null;
                _selectedIdx   = 0;
            }
        }

        // ── 拾取逻辑 ──────────────────────────────────────────────────────────

        private void TryPickup(LootDropWorldObject loot)
        {
            InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
            if (inv == null || loot == null || loot.Item == null) return;

            int leftover = inv.AddItem(loot.Item, loot.Count);
            int accepted = loot.Count - leftover;

            if (accepted <= 0)
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                return;
            }

            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Pickup);

            if (leftover <= 0)
            {
                loot.GetComponent<LootDropVisual>()?.SetSelected(false);
                _currentTarget = null;
                Destroy(loot.gameObject);
            }
            else
            {
                loot.SetRemaining(leftover);
            }
        }

        private void ClearSelection()
        {
            _currentTarget?.GetComponent<LootDropVisual>()?.SetSelected(false);
            _currentTarget = null;
            _selectedIdx   = 0;
            _inRange.Clear();
        }

        // ── 提示 UI 驱动 ──────────────────────────────────────────────────────

        private void ShowPrompt(LootDropWorldObject loot, int count)
        {
            EnsureUI();

            bool changed = _promptFor != loot || _promptCount != count;
            if (changed)
            {
                _promptFor   = loot;
                _promptCount = count;
                RebuildPrompt(loot, count);
            }

            if (_cam == null) _cam = Camera.main;
            if (_cam != null)
            {
                Vector3 wp = loot.transform.position + Vector3.up * promptHeight;
                Vector3 sp = _cam.WorldToScreenPoint(wp);
                if (sp.z > 0f)
                {
                    _panel.position = sp;
                    _canvas.gameObject.SetActive(true);
                }
                else
                {
                    _canvas.gameObject.SetActive(false);
                }
            }
        }

        private void RebuildPrompt(LootDropWorldObject loot, int count)
        {
            UILocalizationTable loc = GetLocTable();
            string pickupLabel = loc != null ? loc.Get("ui_pickup_action", "拾取") : "拾取";
            string cycleLabel  = loc != null ? loc.Get("ui_pickup_cycle",  "切换目标") : "切换目标";

            string itemName = loot.Item != null
                ? (LocalizationRuntime.Instance != null
                    ? LocalizationRuntime.Instance.GetText(loot.Item.localizedNames, loot.Item.displayName)
                    : loot.Item.displayName)
                : "物品";

            string countStr = loot.Count > 1 ? $"  ×{loot.Count}" : "";

            // 键帽图标
            bool preferGamepad = SkyPrisonInputDeviceTracker.Current ==
                                 SkyPrisonInputDeviceTracker.DeviceFamily.Gamepad;
            SkyPrisonInputPromptIconDatabase db = GetIconDb();
            SkyPrisonInputSettings input = GetInput();
            bool hasIcon = false;
            if (db != null && input != null &&
                SkyPrisonInputPromptResolver.TryResolveActionIcon(
                    input, db, SkyPrisonInputAction.Interact, preferGamepad,
                    SkyPrisonInputPromptDeviceStyle.GamepadXbox,
                    out Sprite sprite, out _, out _) && sprite != null)
            {
                _interactIcon.sprite = sprite;
                _interactIcon.preserveAspect = true;
                _interactIcon.enabled = true;
                hasIcon = true;
            }
            else
            {
                _interactIcon.enabled = false;
            }
            _actionToken.gameObject.SetActive(!hasIcon);
            if (!hasIcon && input != null)
            {
                var b = input.GetBinding(SkyPrisonInputAction.Interact);
                _actionToken.text = b != null && b.primaryKey != KeyCode.None
                    ? $"[{b.primaryKey}]" : "[?]";
            }

            // 背包满判断
            InventoryRuntime inv = InventoryRuntimeBootstrap.Instance?.Inventory;
            bool canPickup = inv == null || loot.Item == null ||
                             inv.SimulateAdd(loot.Item, loot.Count) < loot.Count;

            float alpha = canPickup ? 1f : 0.35f;

            // 前缀
            _prefixTxt.text  = pickupLabel;
            _prefixTxt.color = new Color(0.90f, 0.92f, 0.96f, alpha);
            if (_interactIcon != null) _interactIcon.color = new Color(1f, 1f, 1f, alpha);
            if (_actionToken  != null) _actionToken.color  = new Color(0.56f, 0.84f, 1f, alpha);

            // 字体延迟绑定，对齐 ItemPickupToastUI 方案
            if (!_tmpFontBound)
            {
                TMP_FontAsset f = ResolveFont();
                if (f != null) { _itemNameTxt.font = f; _tmpFontBound = true; }
            }

            // 物品名 + 品级颜色（LV9 = RainbowTextEffect，其余 rich text）
            int lv = loot.Item?.itemLevel ?? 1;
            _itemNameTxt.color = new Color(1f, 1f, 1f, alpha);

            if (lv == 9)
            {
                _rainbow.enabled = true;
                // 仅物品名字符着彩虹，×N 数量用 LV1 灰
                var grayOutside = new Color32(180, 178, 169, (byte)(alpha * 255));
                _rainbow.SetVisibleRange(0, itemName.Length - 1, grayOutside);
                _itemNameTxt.text = itemName + countStr;
            }
            else
            {
                _rainbow.enabled = false;
                string hex = QualityHex(lv);
                _itemNameTxt.text = $"<color=#{hex}>{itemName}</color>{countStr}";
            }

            // 切换行
            bool showCycle   = count > 1;
            bool hasCycleIcon = false;
            if (showCycle && db != null && input != null &&
                SkyPrisonInputPromptResolver.TryResolveActionIcon(
                    input, db, SkyPrisonInputAction.CyclePickup, preferGamepad,
                    SkyPrisonInputPromptDeviceStyle.GamepadXbox,
                    out Sprite cSprite, out _, out _) && cSprite != null)
            {
                _cycleIcon.sprite = cSprite;
                _cycleIcon.preserveAspect = true;
                _cycleIcon.enabled = true;
                hasCycleIcon = true;
            }
            else
            {
                _cycleIcon.enabled = false;
            }
            _cycleToken.gameObject.SetActive(showCycle && !hasCycleIcon);
            if (showCycle && !hasCycleIcon && input != null)
            {
                var b = input.GetBinding(SkyPrisonInputAction.CyclePickup);
                _cycleToken.text = b != null && b.primaryKey != KeyCode.None
                    ? $"[{b.primaryKey}]" : "[G]";
            }
            string idxStr = count > 1 ? $"  {_selectedIdx + 1}/{count}" : "";
            _cycleLabelTxt.text = $"{cycleLabel}{idxStr}";
            _cycleRow?.SetActive(showCycle);
        }

        private void HidePrompt()
        {
            _promptFor   = null;
            _promptCount = 0;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        // ── 构建 UI ───────────────────────────────────────────────────────────

        private void EnsureUI()
        {
            if (_canvas != null) return;

            Font          legacyFont = LocalizationRuntime.Instance?.GetCurrentFont();
            TMP_FontAsset tmpFont    = Object.FindObjectOfType<SkyPrisonUIGlobalStyleSettings_V1>()?.defaultTextFont;

            var cGo = new GameObject("[ItemPickupPrompt]") { hideFlags = HideFlags.HideAndDontSave };
            _canvas = cGo.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortOrder;
            var scaler = cGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            cGo.AddComponent<GraphicRaycaster>();

            var outerGo = new GameObject("Outer", typeof(RectTransform));
            outerGo.transform.SetParent(cGo.transform, false);
            _panel       = (RectTransform)outerGo.transform;
            _panel.pivot = new Vector2(0.5f, 0f);

            var outerBg = outerGo.AddComponent<Image>();
            outerBg.sprite = BuildFadeSprite();
            outerBg.type   = Image.Type.Simple;
            outerBg.color  = new Color(0.06f, 0.07f, 0.09f, 0.55f);
            outerBg.raycastTarget = false;

            var outerVl = outerGo.AddComponent<VerticalLayoutGroup>();
            outerVl.childAlignment       = TextAnchor.MiddleLeft;
            outerVl.spacing              = 8f;
            outerVl.padding              = new RectOffset(40, 40, 12, 12);
            outerVl.childControlWidth    = true;
            outerVl.childControlHeight   = true;
            outerVl.childForceExpandWidth  = true;
            outerVl.childForceExpandHeight = false;

            var outerFit = outerGo.AddComponent<ContentSizeFitter>();
            outerFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            outerFit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // ── 行1：[键帽] [拾取前缀] [物品名TMP] ───────────────────────
            var row1 = MakeHRow("Row1", outerGo.transform, 16f);

            var iconGo = new GameObject("InteractIcon", typeof(RectTransform));
            iconGo.transform.SetParent(row1.transform, false);
            _interactIcon = iconGo.AddComponent<Image>();
            _interactIcon.raycastTarget = false;
            var iconLe = iconGo.AddComponent<LayoutElement>();
            iconLe.preferredWidth  = IconSize;
            iconLe.preferredHeight = IconSize;

            _actionToken = MakeLegacyText("Token", row1.transform, legacyFont);
            _actionToken.color = new Color(0.56f, 0.84f, 1f, 1f);

            _prefixTxt = MakeLegacyText("Prefix", row1.transform, legacyFont);
            _prefixTxt.color = new Color(0.90f, 0.92f, 0.96f, 1f);

            // 物品名：TMP + RainbowTextEffect
            var nameGo = new GameObject("ItemName", typeof(RectTransform));
            nameGo.transform.SetParent(row1.transform, false);
            _itemNameTxt = nameGo.AddComponent<TextMeshProUGUI>();
            _itemNameTxt.fontSize    = FontSize;
            _itemNameTxt.color       = Color.white;
            _itemNameTxt.alignment   = TextAlignmentOptions.MidlineLeft;
            _itemNameTxt.raycastTarget = false;
            _itemNameTxt.overflowMode  = TextOverflowModes.Overflow;
            if (tmpFont != null) _itemNameTxt.font = tmpFont;
            _rainbow = nameGo.AddComponent<RainbowTextEffect>();
            _rainbow.enabled = false;

            // ── 行2：[切换键帽] [切换文字] ──────────────────────────────
            var row2Go = MakeHRow("Row2", outerGo.transform, 12f);
            _cycleRow = row2Go;

            var cIconGo = new GameObject("CycleIcon", typeof(RectTransform));
            cIconGo.transform.SetParent(row2Go.transform, false);
            _cycleIcon = cIconGo.AddComponent<Image>();
            _cycleIcon.raycastTarget = false;
            var cIconLe = cIconGo.AddComponent<LayoutElement>();
            cIconLe.preferredWidth  = IconSize;
            cIconLe.preferredHeight = IconSize;

            _cycleToken = MakeLegacyText("CycleToken", row2Go.transform, legacyFont);
            _cycleToken.color = new Color(0.56f, 0.84f, 1f, 1f);

            _cycleLabelTxt = MakeLegacyText("CycleLabel", row2Go.transform, legacyFont);
            _cycleLabelTxt.color = new Color(0.75f, 0.78f, 0.82f, 1f);

            row2Go.SetActive(false);
            cGo.SetActive(false);
        }

        private static GameObject MakeHRow(string name, Transform parent, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment         = TextAnchor.MiddleLeft;
            hl.spacing                = spacing;
            hl.childControlWidth      = true;
            hl.childControlHeight     = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }

        private static Text MakeLegacyText(string name, Transform parent, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            if (font != null) t.font = font;
            t.fontSize           = FontSize;
            t.alignment          = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow   = VerticalWrapMode.Overflow;
            t.supportRichText    = true;
            t.raycastTarget      = false;
            return t;
        }

        // ── 品级颜色（与 InventoryItemDetailPanel.QualityHex 完全一致）────────

        private static TMP_FontAsset ResolveFont()
        {
            var style = Object.FindObjectOfType<SkyPrisonUIGlobalStyleSettings_V1>();
            if (style?.defaultTextFont != null) return style.defaultTextFont;
#if UNITY_EDITOR
            const string name = "ZhouFangRiMingTi-2 SDF";
            string[] guids = UnityEditor.AssetDatabase.FindAssets(name + " t:TMP_FontAsset");
            if (guids.Length > 0)
                return UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
#endif
            return Resources.Load<TMP_FontAsset>("Fonts & Materials/ZhouFangRiMingTi-2 SDF");
        }

        private static string QualityHex(int lv) => lv switch
        {
            1 => "B4B2A9",
            2 => "97C459",
            3 => "ED93B1",
            4 => "85B7EB",
            5 => "AFA9EC",
            6 => "1D9E75",
            7 => "D85A30",
            8 => "E24B4A",
            9 => "AFA9EC",
            _ => "B4B2A9"
        };

        private static Sprite BuildFadeSprite()
        {
            if (_fadeSprite != null) return _fadeSprite;
            const int w = 256, h = 8;
            const float edge = 0.22f;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags  = HideFlags.HideAndDontSave
            };
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                float a = u < edge ? u / edge : (u > 1f - edge ? (1f - u) / edge : 1f);
                a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(a));
                var c = new Color(1f, 1f, 1f, a);
                for (int y = 0; y < h; y++) tex.SetPixel(x, y, c);
            }
            tex.Apply();
            _fadeSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            _fadeSprite.hideFlags = HideFlags.HideAndDontSave;
            return _fadeSprite;
        }

        // ── 资源解析 ──────────────────────────────────────────────────────────

        private SkyPrisonInputSettings GetInput()
        {
            if (_input == null)
            {
                _input = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings")
                      ?? Resources.Load<SkyPrisonInputSettings>("Settings/SkyPrisonInputSettings");
                _input?.EnsureDefaults();
            }
            return _input;
        }

        private SkyPrisonWindowManager_V1 Windows()
        {
            if (_windows == null) _windows = FindObjectOfType<SkyPrisonWindowManager_V1>();
            return _windows;
        }

        private SkyPrisonInputPromptIconDatabase GetIconDb()
        {
            if (_iconDb == null)
            {
                // 资产实际文件名是 InputPromptIconDatabase（没有 SkyPrison 前缀），下面
                // 这行的 Resources.Load 名字一直没对上，这个兜底其实从来没生效过。
                _iconDb = SkyPrisonQuickItemPromptStrip.RuntimeDatabase
                       ?? Resources.Load<SkyPrisonInputPromptIconDatabase>("InputPromptIconDatabase");
            }
            return _iconDb;
        }

        private UILocalizationTable GetLocTable()
        {
            if (_locTable == null)
                _locTable = Resources.Load<UILocalizationTable>("UILocalizationTable")
                         ?? Resources.Load<UILocalizationTable>("Settings/UILocalizationTable");
            return _locTable;
        }

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
        }
    }
}
