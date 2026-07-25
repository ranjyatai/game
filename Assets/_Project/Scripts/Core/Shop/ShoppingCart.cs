using System;
using System.Collections.Generic;

/// <summary>
/// 单次购物会话的购物车。由 ShopWindowController 持有，结账后清空。
/// </summary>
public class ShoppingCart
{
    public class CartLine
    {
        public ShopItemEntry shopEntry;
        public int           quantity;
        public string        currencyId;
        public int           unitPrice;
        public int           lineTotal => unitPrice * quantity;
    }

    private readonly List<CartLine> _lines = new List<CartLine>();
    private readonly string _defaultCurrency;

    public IReadOnlyList<CartLine> Lines => _lines;

    public event Action OnCartChanged;

    public ShoppingCart(string defaultCurrency)
    {
        _defaultCurrency = defaultCurrency;
    }

    // ── 操作 ──────────────────────────────────────────────────────────────

    public void AddOrIncrement(ShopItemEntry entry, int qty = 1)
    {
        if (entry?.item == null || qty <= 0) return;

        string cid   = entry.ResolveCurrencyId(_defaultCurrency);
        int unitPrice = entry.ResolvePrice(cid);

        // 库存校验：已选 + 新增 ≤ 剩余库存
        int alreadyInCart = GetQuantity(entry);
        int maxCanAdd = entry.stock < 0
            ? int.MaxValue
            : Math.Max(0, entry.remainingStock - alreadyInCart);
        qty = Math.Min(qty, maxCanAdd);
        if (qty <= 0) return;

        var line = FindLine(entry);
        if (line != null)
        {
            line.quantity += qty;
        }
        else
        {
            _lines.Add(new CartLine
            {
                shopEntry  = entry,
                quantity   = qty,
                currencyId = cid,
                unitPrice  = unitPrice,
            });
        }
        OnCartChanged?.Invoke();
    }

    public void SetQuantity(ShopItemEntry entry, int qty)
    {
        if (entry == null) return;
        var line = FindLine(entry);
        if (qty <= 0)
        {
            if (line != null) { _lines.Remove(line); OnCartChanged?.Invoke(); }
            return;
        }
        // 库存上限
        int cap = entry.stock < 0 ? int.MaxValue : entry.remainingStock;
        qty = Math.Min(qty, cap);
        if (line == null)
            AddOrIncrement(entry, qty);
        else
        {
            line.quantity = qty;
            OnCartChanged?.Invoke();
        }
    }

    public void Remove(ShopItemEntry entry)
    {
        var line = FindLine(entry);
        if (line == null) return;
        _lines.Remove(line);
        OnCartChanged?.Invoke();
    }

    public void Clear()
    {
        if (_lines.Count == 0) return;
        _lines.Clear();
        OnCartChanged?.Invoke();
    }

    // ── 查询 ──────────────────────────────────────────────────────────────

    public int GetQuantity(ShopItemEntry entry) => FindLine(entry)?.quantity ?? 0;

    public bool IsEmpty => _lines.Count == 0;

    /// <summary>按货币 ID 返回各货币的合计金额。</summary>
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

    /// <summary>检查玩家是否有足够余额结账。</summary>
    public bool CanAfford(CurrencyRuntime currency)
    {
        if (currency == null) return false;
        foreach (var kv in GetTotals())
            if (currency.Get(kv.Key) < kv.Value) return false;
        return true;
    }

    // ── 结账 ──────────────────────────────────────────────────────────────

    public enum CheckoutResult { Success, InsufficientFunds, InventoryFull }

    /// <summary>
    /// 结账：扣款 → 发货到背包 → 扣库存 → 清空购物车。
    /// </summary>
    public CheckoutResult Checkout(CurrencyRuntime currency, InventoryRuntime inventory)
    {
        if (!CanAfford(currency)) return CheckoutResult.InsufficientFunds;

        // 预检：背包容量
        foreach (var l in _lines)
        {
            int leftover = inventory.SimulateAdd(l.shopEntry.item, l.quantity);
            if (leftover > 0) return CheckoutResult.InventoryFull;
        }

        // 实际扣款
        foreach (var kv in GetTotals())
            currency.Spend(kv.Key, kv.Value);

        // 发货 + 扣库存
        foreach (var l in _lines)
        {
            inventory.AddItem(l.shopEntry.item, l.quantity);
            if (l.shopEntry.stock >= 0)
                l.shopEntry.remainingStock = Math.Max(0, l.shopEntry.remainingStock - l.quantity);
        }

        Clear();
        return CheckoutResult.Success;
    }

    private CartLine FindLine(ShopItemEntry entry)
    {
        foreach (var l in _lines)
            if (l.shopEntry == entry) return l;
        return null;
    }
}
