using UnityEngine;
using SkyPrison.Runtime.UI;

/// <summary>
/// 仓库专用 GridView。
/// 继承 InventoryGridView 的逻辑，增加 BindInventory() 供页签切换时外部调用。
/// </summary>
[DisallowMultipleComponent]
public sealed class StashGridView : MonoBehaviour
{
    [SerializeField] private Transform gridContent;
    [SerializeField] private int       columns      = 5;
    [SerializeField] private int       fallbackCap  = StashRuntime.SlotsPerPage;

    private InventoryRuntime              _inv;
    private bool                          _subscribed;

    /// <summary>供 SkyPrisonInventoryInteraction 判断"拖拽目标是不是仓库格子"时解析
    /// 当前应该把物品送进哪个 InventoryRuntime——跟随页签切换动态变化，不缓存。</summary>
    public InventoryRuntime BoundInventory => _inv;
    private InventoryFilterTab            _filter = InventoryFilterTab.All;
    private System.Collections.Generic.List<InventorySlotView> _cells
        = new System.Collections.Generic.List<InventorySlotView>();

    private void OnEnable()
    {
        ResolveCells();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    /// <summary>页签切换时调用，重新绑定到目标页的 InventoryRuntime。</summary>
    public void BindInventory(InventoryRuntime inv)
    {
        Unsubscribe();
        _inv = inv;
        if (_inv != null)
        {
            _inv.OnInventoryChanged += Refresh;
            _subscribed = true;
        }
        Refresh();
    }

    private void Unsubscribe()
    {
        if (_inv != null && _subscribed)
        {
            _inv.OnInventoryChanged -= Refresh;
            _subscribed = false;
        }
    }

    private void ResolveCells()
    {
        _cells.Clear();
        Transform c = gridContent != null ? gridContent : transform;
        _cells.AddRange(c.GetComponentsInChildren<InventorySlotView>(true));
        _cells.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));
    }

    public void Refresh()
    {
        var slots = _inv?.Slots;
        int cap   = _inv != null ? _inv.Capacity : fallbackCap;
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
            else                                   cell.SetEmpty();

            var rt = (RectTransform)cell.transform;
            float bottom = -rt.anchoredPosition.y + rt.sizeDelta.y;
            if (bottom > contentBottom) contentBottom = bottom;
        }

        if (gridContent is RectTransform crt && contentBottom > 0f)
            crt.sizeDelta = new Vector2(crt.sizeDelta.x, contentBottom);

        ApplyFilter(_filter);
    }

    /// <summary>由 StashWindowController 的筛选标签调用：跟背包 InventoryGridView.ApplyFilter
    /// 同一套逻辑，不匹配当前分类的物品调暗。切页签后 BindInventory→Refresh 会自动用
    /// 当前记住的筛选状态重新应用一遍，不用外部再调一次。</summary>
    public void ApplyFilter(InventoryFilterTab filter)
    {
        _filter = filter;
        if (_inv == null) return;

        var matching = new System.Collections.Generic.HashSet<int>(_inv.GetFilteredIndices(filter));
        for (int i = 0; i < _cells.Count; i++)
        {
            InventorySlotView cell = _cells[i];
            if (cell == null || cell.IsEmpty) continue;
            cell.SetDimmed(!matching.Contains(i));
        }
    }
}
