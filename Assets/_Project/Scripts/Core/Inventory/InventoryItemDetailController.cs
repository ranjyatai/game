using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 物品详情面板控制器：
    ///  - 鼠标：悬停格子 0.3s 后显示详情面板，移出立即隐藏。
    ///  - 手柄：Y 键（JoystickButton3）切换详情面板显隐；切换焦点时若面板打开则更新内容。
    /// 由 SkyPrisonInventoryInteraction.Awake() 自动挂载。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryItemDetailController : MonoBehaviour
    {
        // Back/View(Xbox)=Button6 / Share(PS4 原生)=Button8，由 gamepad 组件动态提供——
        // 背包用 SkyPrisonInventoryGamepad，仓库用 StashInventoryGamepad，两边都要试一下。
        private KeyCode BtnDetail => GetComponent<SkyPrisonInventoryGamepad>()?.BtnBack
                                  ?? GetComponent<StashInventoryGamepad>()?.BtnBack
                                  ?? KeyCode.JoystickButton6;
        private const float   HoverDelay = 0.3f;

        private SkyPrisonInventoryInteraction _interaction;
        private InventoryItemDetailPanel      _panel;
        private CanvasGroup                   _inventoryCg; // 背包面板的 CanvasGroup，监测关闭动画

        // 鼠标 hover 状态
        private int   _hoverSlot  = -1;
        private float _hoverTimer = 0f;
        private RectTransform _hoverCellRect; // 悬停中的具体格子——详情面板贴在它旁边，不是贴在整个面板边缘

        // 手柄详情开关（独立于鼠标 hover）
        private bool _gamepadDetailOpen;
        private int  _lastGamepadFocus = -1;

        // 2026-07-23：仓库也要复用这个详情面板控制器（挂在 StashPanel 上），但仓库当前
        // 生效的 InventoryRuntime 是"当前选中页"，不是玩家背包——用可选的委托覆盖默认
        // 解析，跟 SkyPrisonInventorySortControls.SetInventorySourceOverride 同一个思路。
        // 传 null（默认）保持原来的背包行为不变。
        private System.Func<InventoryRuntime> _inventoryOverride;
        public void SetInventorySource(System.Func<InventoryRuntime> resolver) => _inventoryOverride = resolver;

        private InventoryRuntime Inv =>
            _inventoryOverride != null ? _inventoryOverride() : InventoryRuntimeBootstrap.Instance?.Inventory;

        private void Awake()
        {
            _interaction = GetComponent<SkyPrisonInventoryInteraction>();

            // 在背包面板的父节点下创建面板 GameObject，避免被背包 RectMask2D 裁剪
            var panelGo = new GameObject("ItemDetailPanel", typeof(RectTransform));
            panelGo.transform.SetParent(transform.parent, false);
            var rt = (RectTransform)panelGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            _panel = panelGo.AddComponent<InventoryItemDetailPanel>();
        }

        private void OnDisable()
        {
            _panel?.Hide();
            _hoverSlot  = -1;
            _hoverTimer = 0f;
            _gamepadDetailOpen = false;
        }

        private void OnDestroy()
        {
            if (_panel != null) Destroy(_panel.gameObject);
        }

        // ── 鼠标 hover 入口（由 InventorySlotInteractor 调用）────────────

        public void OnSlotHoverEnter(int slotIndex, RectTransform cellRect)
        {
            _hoverSlot  = slotIndex;
            _hoverTimer = 0f;
            _hoverCellRect = cellRect;
        }

        public void OnSlotHoverExit(int slotIndex)
        {
            if (_hoverSlot != slotIndex) return;
            _hoverSlot  = -1;
            _hoverTimer = 0f;
            if (!_gamepadDetailOpen) _panel?.Hide();
        }

        // ── Update ────────────────────────────────────────────────────────

        private void Update()
        {
            // 背包面板关闭动画期间（alpha 或 scale 不为 1）立刻隐藏详情面板，防止乱跑
            if (_inventoryCg == null) _inventoryCg = GetComponent<CanvasGroup>();
            bool inventoryClosing = (_inventoryCg != null && _inventoryCg.alpha < 0.99f)
                                 || transform.localScale.x < 0.99f
                                 || transform.localScale.y < 0.99f;
            if (inventoryClosing)
            {
                _panel?.Hide();
                _hoverSlot = -1;
                _hoverTimer = 0f;
                _gamepadDetailOpen = false;
                return;
            }

            // 背包/仓库双开时，L3把手柄焦点切到另一边之后，这一边的光标高亮框会自己
            // 隐藏，但手柄详情面板之前没跟着关——用户反馈"光标不聚焦在右侧时候详情
            // 还留着"。这里查一下这一边的手柄组件是不是还是当前活跃焦点，不是就强制关掉。
            if (_gamepadDetailOpen)
            {
                var gamepad = GetComponent<SkyPrisonInventoryGamepad>();
                var stashGamepad = GetComponent<StashInventoryGamepad>();
                bool focusActive = gamepad != null ? gamepad.IsFocusActive
                                  : stashGamepad != null ? stashGamepad.IsFocusActive : true;
                if (!focusActive)
                {
                    _gamepadDetailOpen = false;
                    if (_hoverSlot < 0) _panel?.Hide();
                }
            }

            HandleMouseHover();
            HandleGamepadDetail();
        }

        private void HandleMouseHover()
        {
            if (_hoverSlot < 0) return;

            _hoverTimer += Time.unscaledDeltaTime;
            if (_hoverTimer < HoverDelay) return;

            // 延迟到达：显示详情
            ShowForSlot(_hoverSlot);
        }

        private void HandleGamepadDetail()
        {
            if (!Input.GetKeyDown(BtnDetail)) return;

            var gamepad = GetComponent<SkyPrisonInventoryGamepad>();
            var stashGamepad = GetComponent<StashInventoryGamepad>();

            // 背包/仓库双开时，Share这个按键在两边的 InventoryItemDetailController 上
            // 都存在——之前这里没检查"我这边现在是不是活跃焦点"，导致按Share哪怕焦点
            // 在另一边，这一边照样会弹详情。现在只有活跃那一边才响应。
            bool focusActive = gamepad != null ? gamepad.IsFocusActive
                              : stashGamepad != null ? stashGamepad.IsFocusActive : true;
            if (!focusActive) return;

            int focus = gamepad != null ? gamepad.FocusIndex : (stashGamepad != null ? stashGamepad.FocusIndex : -1);

            if (_gamepadDetailOpen)
            {
                _gamepadDetailOpen = false;
                if (_hoverSlot < 0) _panel?.Hide(); // 若鼠标没在 hover 才真正关闭
            }
            else
            {
                _gamepadDetailOpen = true;
                ShowForSlot(focus);
            }
        }

        // 手柄焦点变化时，若面板已打开则更新内容
        public void OnGamepadFocusChanged(int newFocus)
        {
            if (!_gamepadDetailOpen || newFocus == _lastGamepadFocus) return;
            _lastGamepadFocus = newFocus;
            ShowForSlot(newFocus);
        }

        // ── 内部：显示指定格子的详情 ─────────────────────────────────────

        private void ShowForSlot(int slotIndex)
        {
            InventoryRuntime inv = Inv;
            if (inv == null || slotIndex < 0 || slotIndex >= inv.Slots.Count) return;

            InventoryItemEntry entry = inv.Slots[slotIndex];
            if (entry?.definition == null) { _panel?.Hide(); return; }

            // 鼠标 hover 触发时用当时缓存的那个格子的 RectTransform；手柄焦点触发没有
            // 现成的悬停格子，按 slotIndex 现查一次。两种情况都找不到才退回整个面板
            // （不应该发生，纯兜底）。
            RectTransform anchor = (_hoverSlot == slotIndex) ? _hoverCellRect : null;
            if (anchor == null) anchor = FindSlotRect(slotIndex);
            if (anchor == null) anchor = (RectTransform)transform;

            _panel?.Show(entry.definition, entry, anchor);
        }

        private RectTransform FindSlotRect(int slotIndex)
        {
            var views = GetComponentsInChildren<InventorySlotView>(true);
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null && views[i].SlotIndex == slotIndex)
                    return (RectTransform)views[i].transform;
            return null;
        }
    }
}
