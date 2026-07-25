using System.Collections.Generic;
using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 背包网格绑定器：挂在背包面板上，把 InventoryRuntime（数据）映射到一排
    /// InventorySlotView（视图）。订阅 OnInventoryChanged，内容变化时整体刷新。
    ///
    /// 容量模型（已定）：PF 里建满最大格，当前容量内可用、容量外隐藏（留扩充悬念）。
    /// GridContent 高度收到正好容纳当前容量行数，滚动滑不出不存在的格子。
    /// 没解析到 InventoryRuntime 时（玩家未生成等）按 fallbackCapacity 布局，保证窗口外观正确。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryGridView : MonoBehaviour
    {
        [SerializeField] private Transform gridContent;
        [Tooltip("没解析到 InventoryRuntime 时按此容量布局（应与背包初始容量一致）。")]
        [SerializeField] private int fallbackCapacity = 20;

        private readonly List<InventorySlotView> _cells = new List<InventorySlotView>();
        private InventoryRuntime _inv;
        private InventoryFilterTab _filter = InventoryFilterTab.All;
        private EquipmentSlotType? _forcedEquipSlot;
        private int? _forcedQuickSlotIndex;
        private bool _resolved;
        private bool _subscribed;

        /// <summary>编辑器构建 PF 时调用，注入格子容器。</summary>
        public void SetGridContent(Transform content) => gridContent = content;

        private void OnEnable()
        {
            ResolveCells();
            TryBindInventory();
            Refresh();
            // 快捷栏角标要能感知"绑定关系变了"（不是背包数据变了）——比如刚把某个物品
            // 指定为快捷物品2，这个格子要立刻冒出"2"角标，不用等背包数据发生变化。
            QuickSlotRuntime.OnSlotChanged += HandleQuickSlotChanged;
        }

        private void OnDisable()
        {
            if (_inv != null && _subscribed) { _inv.OnInventoryChanged -= Refresh; _subscribed = false; }
            QuickSlotRuntime.OnSlotChanged -= HandleQuickSlotChanged;
        }

        private void HandleQuickSlotChanged(int index, ItemDefinition definition) => Refresh();

        // 解析 InventoryRuntime 并订阅；允许晚绑定（玩家可能比窗口晚就位）。
        private void TryBindInventory()
        {
            if (_inv == null)
            {
                _inv = InventoryRuntimeBootstrap.Instance != null
                    ? InventoryRuntimeBootstrap.Instance.Inventory
                    : FindObjectOfType<InventoryRuntime>();
            }
            if (_inv != null && !_subscribed)
            {
                _inv.OnInventoryChanged += Refresh;
                _subscribed = true;
            }
        }

        private void ResolveCells()
        {
            _cells.Clear();
            Transform c = gridContent != null ? gridContent : transform;
            _cells.AddRange(c.GetComponentsInChildren<InventorySlotView>(true));
            _cells.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
            _resolved = true;
        }

        /// <summary>
        /// 整体刷新：容量外的格子直接隐藏（不显示，留扩充悬念），容量内按打包顺序绑定。
        /// 同时把 GridContent 高度收到正好容纳当前容量的行数，使滚动滑不出空白。
        /// </summary>
        public void Refresh()
        {
            if (!_resolved) ResolveCells();
            TryBindInventory();

            // 没有数据层也要按兜底容量布局（隐藏多余格 + 收缩内容），保证窗口外观正确。
            IReadOnlyList<InventoryItemEntry> slots = _inv != null ? _inv.Slots : null;
            int cap = _inv != null ? _inv.Capacity : fallbackCapacity;
            float contentBottom = 0f;

            for (int i = 0; i < _cells.Count; i++)
            {
                InventorySlotView cell = _cells[i];
                if (cell == null) continue;

                bool active = i < cap;
                if (cell.gameObject.activeSelf != active)
                    cell.gameObject.SetActive(active);

                if (!active) continue;

                if (slots != null && i < slots.Count) cell.Bind(slots[i]);
                else                                  cell.SetEmpty();

                // 累计内容区需要的高度（格子顶部锚定，anchoredPosition.y 向下为负）
                var rt = (RectTransform)cell.transform;
                float bottom = -rt.anchoredPosition.y + rt.sizeDelta.y;
                if (bottom > contentBottom) contentBottom = bottom;
            }

            if (gridContent is RectTransform crt && contentBottom > 0f)
                crt.sizeDelta = new Vector2(crt.sizeDelta.x, contentBottom);

            ApplyFilter(_filter);
        }

        /// <summary>由 InventoryWindowController 在切换筛选 Tab 时调用：不匹配的物品调暗。
        /// 强制装备槽过滤（见 SetForcedEquipSlotFilter）优先于普通 Tab 过滤生效。</summary>
        public void ApplyFilter(InventoryFilterTab filter)
        {
            _filter = filter;
            if (_inv == null) return;

            var matching = new HashSet<int>(_forcedQuickSlotIndex.HasValue
                ? _inv.GetIndicesForQuickSlot()
                : _forcedEquipSlot.HasValue
                    ? _inv.GetIndicesForEquipSlot(_forcedEquipSlot.Value)
                    : _inv.GetFilteredIndices(filter));
            for (int i = 0; i < _cells.Count; i++)
            {
                InventorySlotView cell = _cells[i];
                if (cell == null || cell.IsEmpty) continue;
                cell.SetDimmed(!matching.Contains(i));
            }
        }

        /// <summary>从角色面板点装备槽呼出背包时调用：强制只亮起能装进该槽的物品，
        /// 普通 FilterTab 分类（消耗品/材料/装备...）暂时不生效。传 null 解除强制过滤，
        /// 恢复正常 Tab 过滤。</summary>
        public void SetForcedEquipSlotFilter(EquipmentSlotType? slot)
        {
            _forcedEquipSlot = slot;
            _forcedQuickSlotIndex = null;
            ApplyFilter(_filter);
        }

        /// <summary>从角色面板点快捷物品行呼出背包时调用：强制只亮起能塞进快捷槽的
        /// 消耗品（非复活道具）。传具体槽位序号（0~3），传 null 解除；跟
        /// SetForcedEquipSlotFilter 互斥，开一个会关掉另一个。</summary>
        public void SetForcedQuickSlotFilter(int? quickSlotIndex)
        {
            _forcedQuickSlotIndex = quickSlotIndex;
            if (quickSlotIndex.HasValue) _forcedEquipSlot = null;
            ApplyFilter(_filter);
        }

        public int? ForcedQuickSlotIndex => _forcedQuickSlotIndex;
        public EquipmentSlotType? ForcedEquipSlot => _forcedEquipSlot;
    }
}
