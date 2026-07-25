using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 仓库面板级交互控制器（挂在 StashPanel 上，由 StashWindowController 运行时自愈接线）。
    /// 负责：拖拽（仓库格子内部换位/合并、拖到背包送回背包）、右键快速拆分、左键操作菜单
    /// （拆分/丢弃，仓库存放的物品不支持"使用/装备/指定快捷物品"这些背包专属操作，
    /// 所以菜单行数比背包 SkyPrisonInventoryInteraction 少很多）、拆分/丢弃的数量弹窗。
    ///
    /// 数量弹窗用简单的 -/+ 步进器，不是背包那套带动画的拖拽滑条——仓库这边追求"能用"，
    /// 视觉复杂度没必要跟背包完全一致；菜单/弹窗都是 StashPanel 的子物体（不是单独
    /// Overlay Canvas），会随窗口本身的 CanvasGroup 淡出一起消失，不用像背包那样
    /// 特地处理"窗口关闭动画播放期间弹层还留在原地"的问题。
    ///
    /// 数据操作跟背包一样全部走 InventoryRuntime（MergeSlots/MoveSlot/SplitSlot/
    /// DiscardSlot/TransferSlotTo）；改完数据后 OnInventoryChanged → StashGridView
    /// 自动刷新显示。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StashInventoryInteraction : MonoBehaviour, IInventorySlotController
    {
        // 当前生效的 InventoryRuntime 会随页签切换而变，用委托解析而不是缓存——
        // 跟 SkyPrisonInventorySortControls.SetInventorySourceOverride 同一个思路。
        private System.Func<InventoryRuntime> _inventoryResolver;
        public void SetInventorySource(System.Func<InventoryRuntime> resolver) => _inventoryResolver = resolver;
        private InventoryRuntime Inv => _inventoryResolver != null ? _inventoryResolver() : null;

        public void SetHoveredSlot(int slotIndex) { /* 仓库暂时没有悬浮详情/装备对比预览，占位实现 */ }

        // ── 拖拽幽灵 ──────────────────────────────────────────────────────
        private Canvas _ghostCanvas;
        private Image _ghost;
        private RectTransform _ghostRt;
        private int _srcIndex = -1;
        private bool _dragging;

        public void BeginDrag(int srcIndex, PointerEventData e)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || srcIndex < 0 || srcIndex >= inv.Slots.Count) return;

            InventoryItemEntry entry = inv.Slots[srcIndex];
            if (entry == null || entry.definition == null) return;

            _dragging = true;
            _srcIndex = srcIndex;

            EnsureGhost();
            _ghost.sprite = entry.definition.icon;
            _ghost.enabled = entry.definition.icon != null;
            _ghostCanvas.gameObject.SetActive(true);
            UpdateDrag(e);

            FindSlotView(srcIndex)?.SetEmpty();
            SkyPrisonItemMaterialSoundTable.PlayPickup(entry.definition.general.soundMaterial);
        }

        public void UpdateDrag(PointerEventData e)
        {
            if (!_dragging || _ghostRt == null) return;
            _ghostRt.position = e.position;
        }

        public void EndDrag(int srcIndex, PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (_ghostCanvas != null) _ghostCanvas.gameObject.SetActive(false);

            InventoryRuntime inv = Inv;
            if (inv == null) { RefreshGrid(); return; }

            InventorySlotInteractor target = InventorySlotInteractor.FindUnder(e);
            InventoryGridView backpackTarget = target != null ? target.GetComponentInParent<InventoryGridView>() : null;
            StashGridView stashTarget = target != null ? target.GetComponentInParent<StashGridView>() : null;

            if (backpackTarget != null)
            {
                InventoryRuntime backpackInv = InventoryRuntimeBootstrap.Instance != null
                    ? InventoryRuntimeBootstrap.Instance.Inventory : null;
                if (backpackInv != null)
                    HandleTransfer(inv, srcIndex, backpackInv, target.SlotIndex);
            }
            else if (stashTarget != null && target.SlotIndex != srcIndex)
            {
                HandleDropWithin(inv, srcIndex, target.SlotIndex);
            }
            // 落在别处（窗口外/没精确命中格子）：仓库还没有丢弃弹窗，宁可原地回弹
            // 也不能误删数据，所以这里不做任何事，交给下面的 RefreshGrid 还原显示。

            RefreshGrid();
        }

        private StashGridView _grid;

        private InventorySlotView FindSlotView(int index)
        {
            if (_grid == null) _grid = GetComponentInChildren<StashGridView>(true);
            var views = GetComponentsInChildren<InventorySlotView>(true);
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null && views[i].SlotIndex == index) return views[i];
            return null;
        }

        private void RefreshGrid()
        {
            if (_grid == null) _grid = GetComponentInChildren<StashGridView>(true);
            _grid?.Refresh();
        }

        // 仓库内部换位/合并：跟背包 HandleDrop 同一套规则(同种未满合并/否则交换)。
        private void HandleDropWithin(InventoryRuntime inv, int src, int dst)
        {
            var slots = inv.Slots;
            if (src < 0 || src >= slots.Count || dst < 0 || dst >= slots.Count) return;

            InventoryItemEntry s = slots[src];
            InventoryItemEntry d = slots[dst];
            if (s == null) return;

            if (d != null && s.definition == d.definition && s.definition.maxStackCount > 1 && !d.IsStackFull)
                inv.MergeSlots(src, dst);
            else
                inv.MoveSlot(src, dst);

            SkyPrisonItemMaterialSoundTable.PlayDrop(s.definition.general.soundMaterial);
        }

        // 拖到背包格子 → 跨 InventoryRuntime 转移回背包(TransferSlotTo 双向通用)。
        private void HandleTransfer(InventoryRuntime srcInv, int srcIndex, InventoryRuntime dstInv, int dstIndex)
        {
            InventoryItemEntry s = srcInv.Slots[srcIndex];
            if (s == null) return;

            if (srcInv.TransferSlotTo(srcIndex, dstInv, dstIndex))
                SkyPrisonItemMaterialSoundTable.PlayDrop(s.definition.general.soundMaterial);
        }

        // ── 右键快速拆分（拆一个到第一个空格）────────────────────────────────
        public void QuickSplitOne(int index)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || index < 0 || index >= inv.Slots.Count) return;
            InventoryItemEntry entry = inv.Slots[index];
            if (entry?.definition == null || entry.definition.maxStackCount <= 1 || entry.count <= 1
                || inv.UsedSlots >= inv.Capacity)
            {
                SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Forbidden);
                return;
            }
            inv.SplitSlot(index, 1);
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            SkyPrisonItemMaterialSoundTable.PlayPickup(entry.definition.general.soundMaterial);
        }

        // ── 左键操作菜单（拆分/丢弃）────────────────────────────────────────
        // 仓库存放中的物品不支持"使用/装备/指定快捷物品"这些跟"正在携带"相关的操作，
        // 所以菜单最多只有拆分/丢弃两行，比背包简单很多。
        private GameObject _menuRoot;
        private RectTransform _menuBox;
        private readonly List<Button> _menuRowButtons = new List<Button>();
        private readonly List<Image> _menuRowImages = new List<Image>();
        private readonly List<System.Action> _menuRowActions = new List<System.Action>();
        private int _menuSelectedRow = -1;

        private static readonly Color MenuRowNormalColor = new Color(1f, 1f, 1f, 0f);
        private static readonly Color MenuRowSelectedColor = new Color(0.42f, 0.92f, 0.68f, 0.35f); // 冷绿，跟手柄格子高亮同色系

        private const float MenuRowW = 180f;
        private const float MenuRowH = 46f;

        public bool IsContextMenuOpen => _menuRoot != null && _menuRoot.activeSelf;

        public void ShowContextMenu(int index, PointerEventData e)
        {
            if (!BuildContextMenuRows(index)) return;

            _menuBox.sizeDelta = new Vector2(MenuRowW, MenuRowH * _menuRowButtons.Count);
            PositionAtScreenPoint(_menuBox, e.position, e.pressEventCamera);
            OpenMenuCommon();
        }

        // 手柄用：不需要 PointerEventData，直接给一个屏幕锚点（焦点格附近）。
        public void ShowContextMenuAt(int index, Vector2 screenPos)
        {
            if (!BuildContextMenuRows(index)) return;

            _menuBox.sizeDelta = new Vector2(MenuRowW, MenuRowH * _menuRowButtons.Count);
            PositionAtScreenPoint(_menuBox, screenPos, null); // Overlay画布用null相机
            OpenMenuCommon();
        }

        private bool BuildContextMenuRows(int index)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || index < 0 || index >= inv.Slots.Count) return false;
            InventoryItemEntry entry = inv.Slots[index];
            if (entry?.definition == null) return false;

            bool canSplit = entry.definition.maxStackCount > 1 && entry.count > 1 && inv.UsedSlots < inv.Capacity;
            bool canDiscard = entry.CanDiscard;
            if (!canSplit && !canDiscard) return false;

            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
            EnsureMenu();
            ClearMenuRows();

            int capturedIndex = index;
            if (canSplit) AddMenuRow("拆分", () => RequestSplit(capturedIndex));
            if (canDiscard) AddMenuRow("丢弃", () => RequestDiscard(capturedIndex));
            return true;
        }

        private void OpenMenuCommon()
        {
            // 遮罩先置顶、菜单再置顶——菜单必须是最终最上层的兄弟节点，否则遮罩会
            // 盖住菜单本身，连菜单自己的按钮都点不到。
            OpenBlocker();
            _menuRoot.SetActive(true);
            _menuRoot.transform.SetAsLastSibling();

            // 手柄导航默认选中第一行，跟格子焦点高亮同一个视觉语言（冷绿描边/填色）。
            _menuSelectedRow = _menuRowButtons.Count > 0 ? 0 : -1;
            RefreshMenuHighlight();
        }

        // ── 手柄：操作菜单导航 ──────────────────────────────────────────────
        public void MenuNavigate(int delta)
        {
            if (!IsContextMenuOpen || _menuRowButtons.Count == 0 || delta == 0) return;
            _menuSelectedRow = ((_menuSelectedRow + delta) % _menuRowButtons.Count + _menuRowButtons.Count) % _menuRowButtons.Count;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Switch);
            RefreshMenuHighlight();
        }

        public void MenuConfirm()
        {
            if (!IsContextMenuOpen) return;
            if (_menuSelectedRow < 0 || _menuSelectedRow >= _menuRowActions.Count) return;
            System.Action action = _menuRowActions[_menuSelectedRow];
            CloseMenu();
            action?.Invoke();
        }

        public void MenuCancel()
        {
            if (!IsContextMenuOpen) return;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
            CloseMenu();
        }

        private void RefreshMenuHighlight()
        {
            for (int i = 0; i < _menuRowImages.Count; i++)
                if (_menuRowImages[i] != null)
                    _menuRowImages[i].color = i == _menuSelectedRow ? MenuRowSelectedColor : MenuRowNormalColor;
        }

        private void EnsureMenu()
        {
            if (_menuRoot != null) return;

            _menuRoot = new GameObject("StashContextMenu", typeof(RectTransform));
            _menuRoot.transform.SetParent(transform, false);
            _menuBox = (RectTransform)_menuRoot.transform;
            _menuBox.anchorMin = _menuBox.anchorMax = new Vector2(0f, 1f);
            _menuBox.pivot = new Vector2(0f, 1f);

            var bg = _menuRoot.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.09f, 0.10f, 0.96f);
            SkyPrisonFloatingWindowKit.AddOutline(_menuBox, Color.white, 2f);

            // 点菜单外面关闭——全屏透明遮罩插在菜单前一个兄弟节点，菜单自己每次显示时
            // SetAsLastSibling 保证盖在遮罩之上，两者点击互不打架。
            var blockerGo = new GameObject("MenuBlocker", typeof(RectTransform));
            blockerGo.transform.SetParent(transform, false);
            var blockerRT = (RectTransform)blockerGo.transform;
            blockerRT.anchorMin = Vector2.zero; blockerRT.anchorMax = Vector2.one;
            blockerRT.offsetMin = Vector2.zero; blockerRT.offsetMax = Vector2.zero;
            var blockerImg = blockerGo.AddComponent<Image>();
            blockerImg.color = new Color(0f, 0f, 0f, 0f);
            var blockerBtn = blockerGo.AddComponent<Button>();
            blockerBtn.transition = Selectable.Transition.None;
            blockerBtn.onClick.AddListener(CloseMenu);
            blockerGo.SetActive(false);
            _menuBlocker = blockerGo;

            _menuRoot.SetActive(false);
        }

        private GameObject _menuBlocker;

        private void ClearMenuRows()
        {
            for (int i = 0; i < _menuRowButtons.Count; i++)
                if (_menuRowButtons[i] != null) Destroy(_menuRowButtons[i].gameObject);
            _menuRowButtons.Clear();
            _menuRowImages.Clear();
            _menuRowActions.Clear();
            _menuSelectedRow = -1;
        }

        private void AddMenuRow(string label, System.Action onClick)
        {
            int row = _menuRowButtons.Count;
            var rowGo = new GameObject($"Row_{label}", typeof(RectTransform));
            rowGo.transform.SetParent(_menuBox, false);
            var rowRT = (RectTransform)rowGo.transform;
            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(1f, 1f);
            rowRT.pivot = new Vector2(0.5f, 1f);
            rowRT.anchoredPosition = new Vector2(0f, -row * MenuRowH);
            rowRT.sizeDelta = new Vector2(0f, MenuRowH);

            var rowImg = rowGo.AddComponent<Image>();
            rowImg.color = new Color(1f, 1f, 1f, 0f);
            var rowBtn = rowGo.AddComponent<Button>();
            rowBtn.transition = Selectable.Transition.None;
            rowBtn.onClick.AddListener(() => { onClick(); CloseMenu(); });
            SkyPrisonUIButtonFeedback.Attach(rowGo);

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(rowRT, false);
            var textRT = (RectTransform)textGo.transform;
            textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero; textRT.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 28;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            _menuRowButtons.Add(rowBtn);
            _menuRowImages.Add(rowImg);
            _menuRowActions.Add(onClick);
        }

        // 菜单/弹窗共用同一个关闭入口——遮罩点击、取消按钮、确认后都调这个，
        // 两者是互斥的(同一时间只会有一个在显示)，一起清掉不会有副作用。
        private void CloseMenu()
        {
            if (_menuRoot != null) _menuRoot.SetActive(false);
            if (_popupRoot != null) _popupRoot.SetActive(false);
            if (_menuBlocker != null) _menuBlocker.SetActive(false);
        }

        // 菜单/弹窗显示时才需要遮罩接管点击——跟菜单一起开合。
        private void OpenBlocker() { if (_menuBlocker != null) { _menuBlocker.SetActive(true); _menuBlocker.transform.SetAsLastSibling(); } }

        // 把点击的屏幕坐标换算成 StashPanel 本地坐标，定位弹出的菜单/弹窗。
        private void PositionAtScreenPoint(RectTransform box, Vector2 screenPos, Camera cam)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    (RectTransform)transform, screenPos, cam, out Vector2 local))
                box.anchoredPosition = local;
        }

        // ── 数量弹窗（拆分/丢弃共用，简单的 -/+ 步进器）─────────────────────
        private GameObject _popupRoot;
        private RectTransform _popupBox;
        private Text _popupTitleText, _popupNameText, _popupAmountText;
        private int _popupMin, _popupMax, _popupValue;
        private System.Action<int> _popupConfirm;

        public void RequestSplit(int index)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || index < 0 || index >= inv.Slots.Count) return;

            InventoryItemEntry entry = inv.Slots[index];
            if (entry?.definition == null) return;
            if (entry.definition.maxStackCount <= 1 || entry.count <= 1) return;
            if (inv.UsedSlots >= inv.Capacity) return;

            int max = entry.count - 1;
            ShowAmountPopup("拆分", entry.definition.GetLocalizedDisplayName(), max, Mathf.Max(1, max / 2),
                amt => inv.SplitSlot(index, amt));
        }

        public void RequestDiscard(int index)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || index < 0 || index >= inv.Slots.Count) return;

            InventoryItemEntry entry = inv.Slots[index];
            if (entry?.definition == null) return;
            if (!entry.CanDiscard) return;

            ShowAmountPopup("丢弃", entry.definition.GetLocalizedDisplayName(), entry.count, entry.count,
                amt =>
                {
                    ItemDefinition def = entry.definition;
                    inv.DiscardSlot(index, amt);
                    Vector3 dropPos = GetPlayerDropPosition();
                    var dropped = LootDropWorldObject.SpawnDrop(def, amt, dropPos);
                    if (dropped != null)
                    {
                        GameObject unit = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
                        Vector3 tossOrigin = unit != null
                            ? unit.transform.position + Vector3.up * 0.9f
                            : dropPos + Vector3.up * 0.9f;
                        LootDropTossEffect.Apply(dropped.gameObject, tossOrigin, dropped.transform.position);
                    }
                    SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.DropToGround);
                });
        }

        private static Vector3 GetPlayerDropPosition()
        {
            GameObject unit = SkyPrisonPlayerAuthority.CurrentPlayerUnit?.gameObject;
            if (unit == null) return Vector3.zero;
            Vector3 forward = unit.transform.forward;
            forward.y = 0f;
            return unit.transform.position + forward.normalized * 0.8f;
        }

        private void ShowAmountPopup(string title, string itemName, int max, int initial, System.Action<int> onConfirm)
        {
            EnsurePopup();
            _popupMin = 1;
            _popupMax = Mathf.Max(1, max);
            _popupValue = Mathf.Clamp(initial, _popupMin, _popupMax);
            _popupConfirm = onConfirm;
            _popupTitleText.text = title;
            _popupNameText.text = itemName;
            RefreshPopupAmount();

            // 遮罩先置顶、弹窗再置顶——顺序理由同 ShowContextMenu。
            OpenBlocker();
            _popupRoot.SetActive(true);
            _popupRoot.transform.SetAsLastSibling();
        }

        private void EnsurePopup()
        {
            if (_popupRoot != null) return;

            _popupRoot = new GameObject("StashAmountPopup", typeof(RectTransform));
            _popupRoot.transform.SetParent(transform, false);
            _popupBox = (RectTransform)_popupRoot.transform;
            _popupBox.anchorMin = _popupBox.anchorMax = new Vector2(0.5f, 0.5f);
            _popupBox.pivot = new Vector2(0.5f, 0.5f);
            _popupBox.anchoredPosition = Vector2.zero;
            _popupBox.sizeDelta = new Vector2(320f, 200f);

            var bg = _popupRoot.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.09f, 0.10f, 0.96f);
            SkyPrisonFloatingWindowKit.AddOutline(_popupBox, Color.white, 2f);
            SkyPrisonFloatingWindowKit.AddCornerBrackets(_popupBox);

            _popupTitleText = AddPopupText(_popupBox, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(0f, 32f), 26, FontStyle.Bold);

            _popupNameText = AddPopupText(_popupBox, "ItemName", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(0f, 28f), 22, FontStyle.Normal);

            // -/+ 步进器 + 数量文字，一排居中
            var minusBtn = AddStepButton(_popupBox, "-", new Vector2(0.5f, 0.5f), new Vector2(-90f, -8f), () => AdjustPopupAmount(-1));
            _popupAmountText = AddPopupText(_popupBox, "Amount", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(120f, 44f), 30, FontStyle.Bold);
            var plusBtn = AddStepButton(_popupBox, "+", new Vector2(0.5f, 0.5f), new Vector2(90f, -8f), () => AdjustPopupAmount(1));

            // 确认/取消——左右并排，锚在底边中点两侧
            AddPopupButton(_popupBox, "确认", new Vector2(0.5f, 0f), new Vector2(-72f, 32f), ConfirmPopup);
            AddPopupButton(_popupBox, "取消", new Vector2(0.5f, 0f), new Vector2(72f, 32f), CloseMenu);

            _popupRoot.SetActive(false);
        }

        private Text AddPopupText(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, int fontSize, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
            var text = go.AddComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;
            return text;
        }

        private Button AddStepButton(RectTransform parent, string label, Vector2 pivot, Vector2 anchoredPos, System.Action onClick)
        {
            var go = new GameObject($"Step_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = pivot; rt.anchorMax = pivot; rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(44f, 44f);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            SkyPrisonFloatingWindowKit.AddOutline(rt, Color.white, 2f);
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
            SkyPrisonUIButtonFeedback.Attach(go);

            AddPopupText(rt, "Label", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 30, FontStyle.Bold).text = label;
            return btn;
        }

        // 固定锚点+尺寸的按钮(锚在 popup 底边中点，anchoredPos 从那个点算偏移)——
        // 跟 AddStepButton 同一套简单写法，不用拉伸锚点绕弯子。
        private Button AddPopupButton(RectTransform parent, string label, Vector2 pivot, Vector2 anchoredPos, System.Action onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = pivot; rt.anchorMax = pivot; rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(120f, 44f);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            SkyPrisonFloatingWindowKit.AddOutline(rt, Color.white, 2f);
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
            SkyPrisonUIButtonFeedback.Attach(go);

            AddPopupText(rt, "Label", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 24, FontStyle.Normal).text = label;
            return btn;
        }

        private void AdjustPopupAmount(int delta)
        {
            _popupValue = Mathf.Clamp(_popupValue + delta, _popupMin, _popupMax);
            RefreshPopupAmount();
        }

        private void RefreshPopupAmount()
        {
            if (_popupAmountText != null) _popupAmountText.text = _popupValue.ToString();
        }

        private void ConfirmPopup()
        {
            System.Action<int> confirm = _popupConfirm;
            int amount = _popupValue;
            CloseMenu();
            confirm?.Invoke(amount);
        }

        // ── 手柄：数量弹窗 ──────────────────────────────────────────────────
        public bool IsAmountPopupOpen => _popupRoot != null && _popupRoot.activeSelf;
        public void AmountPopupStep(int delta) => AdjustPopupAmount(delta);
        public void AmountPopupConfirm()
        {
            if (!IsAmountPopupOpen) return;
            ConfirmPopup();
        }
        public void AmountPopupCancel()
        {
            if (!IsAmountPopupOpen) return;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Cancel);
            CloseMenu();
        }

        private void EnsureGhost()
        {
            if (_ghostCanvas != null) return;

            var go = new GameObject("DragGhost", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _ghostCanvas = go.AddComponent<Canvas>();
            _ghostCanvas.overrideSorting = true;
            _ghostCanvas.sortingOrder = 1200; // 高于仓库(1100)及其磨砂层
            go.AddComponent<GraphicRaycaster>();

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            _ghostRt = (RectTransform)iconGo.transform;
            _ghostRt.sizeDelta = new Vector2(120f, 120f);
            _ghost = iconGo.AddComponent<Image>();
            _ghost.raycastTarget = false;
            _ghost.preserveAspect = true;
            var c = _ghost.color; c.a = 0.85f; _ghost.color = c;

            go.SetActive(false);
        }
    }
}
