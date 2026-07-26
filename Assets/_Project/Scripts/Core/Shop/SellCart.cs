using System;
using System.Collections.Generic;

/// <summary>
/// 单次出售会话的"出售清单"——跟 ShoppingCart 是同一套两段式流程(选数量→加入清单→
/// 结账页最终确认)，只是数据源从 ShopItemEntry(货架商品) 换成 InventoryItemEntry(背包物品)。
/// 由 ShopWindowController 持有，确认出售/关窗后清空。
/// </summary>
public class SellCart
{
    public class SellLine
    {
        public InventoryItemEntry entry;
        public int    quantity;
        public string currencyId;
        public int    unitPrice;
        public int    lineTotal => unitPrice * quantity;
    }

    private readonly List<SellLine> _lines = new List<SellLine>();

    public IReadOnlyList<SellLine> Lines => _lines;

    public event Action OnCartChanged;

    // ── 操作 ──────────────────────────────────────────────────────────────

    public void AddOrIncrement(InventoryItemEntry entry, int qty, int unitPrice, string currencyId)
    {
        if (entry == null || entry.IsEmpty || qty <= 0) return;

        int alreadyInCart = GetQuantity(entry);
        int maxCanAdd = Math.Max(0, entry.count - alreadyInCart);
        qty = Math.Min(qty, maxCanAdd);
        if (qty <= 0) return;

        var line = FindLine(entry);
        if (line != null)
        {
            line.quantity += qty;
        }
        else
        {
            _lines.Add(new SellLine
            {
                entry      = entry,
                quantity   = qty,
                currencyId = currencyId,
                unitPrice  = unitPrice,
            });
        }
        OnCartChanged?.Invoke();
    }

    public void SetQuantity(InventoryItemEntry entry, int qty)
    {
        if (entry == null) return;
        var line = FindLine(entry);
        if (qty <= 0)
        {
            if (line != null) { _lines.Remove(line); OnCartChanged?.Invoke(); }
            return;
        }
        qty = Math.Min(qty, entry.count);
        if (line == null) return; // 没配价格信息，SetQuantity只用于调整已有行
        line.quantity = qty;
        OnCartChanged?.Invoke();
    }

    public void Clear()
    {
        if (_lines.Count == 0) return;
        _lines.Clear();
        OnCartChanged?.Invoke();
    }

    // ── 查询 ──────────────────────────────────────────────────────────────

    public int GetQuantity(InventoryItemEntry entry) => FindLine(entry)?.quantity ?? 0;

    public bool IsEmpty => _lines.Count == 0;

    public Dictionary<string, long> GetTotals()
    {
        var totals = new Dictionary<string, long>();
        foreach (var l in _lines)
        {
            if (!totals.ContainsKey(l.currencyId)) totals[l.currencyId] = 0;
            totals[l.currencyId] += l.lineTotal;
        }
        return totals;
    }

    // ── 结账 ──────────────────────────────────────────────────────────────

    public enum CheckoutResult { Success, InventoryError }

    /// <summary>
    /// 确认出售：从背包扣除对应数量 → 发放货币 → 清空清单。
    /// 逐行按数量精确扣除(RemoveExactEntry)，任何一行库存不够就整体失败，不部分执行。
    /// </summary>
    public CheckoutResult Checkout(CurrencyRuntime currency, InventoryRuntime inventory)
    {
        if (inventory == null || currency == null) return CheckoutResult.InventoryError;

        foreach (var l in _lines)
            if (l.entry == null || l.entry.IsEmpty || l.entry.count < l.quantity)
                return CheckoutResult.InventoryError;

        foreach (var l in _lines)
            inventory.RemoveExactEntry(l.entry, l.quantity);

        foreach (var kv in GetTotals())
            currency.Add(kv.Key, kv.Value);

        Clear();
        return CheckoutResult.Success;
    }

    private SellLine FindLine(InventoryItemEntry entry)
    {
        foreach (var l in _lines)
            if (l.entry == entry) return l;
        return null;
    }
}
