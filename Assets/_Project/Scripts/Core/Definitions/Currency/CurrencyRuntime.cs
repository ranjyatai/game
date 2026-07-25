using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 货币持有量运行时单例（与 InventoryRuntimeBootstrap 同模式，由 RuntimeSystemsBootstrapper 创建）。
/// 其它系统（触发器/商店/掉落）通过 CurrencyRuntime.Instance 增减货币。
/// </summary>
public class CurrencyRuntime : MonoBehaviour
{
    public static CurrencyRuntime Instance { get; private set; }

    private readonly Dictionary<string, long> _amounts = new Dictionary<string, long>();

    /// <summary>某种货币数量变化时触发。参数：currencyId, delta（正=增加 负=减少）。</summary>
    public event Action<string, long> OnCurrencyChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public long Get(string currencyId)
    {
        if (string.IsNullOrEmpty(currencyId)) return 0;
        return _amounts.TryGetValue(currencyId, out long v) ? v : 0;
    }

    public void Set(string currencyId, long value)
    {
        if (string.IsNullOrEmpty(currencyId)) return;
        long prev = Get(currencyId);
        _amounts[currencyId] = Math.Max(0, value);
        long delta = _amounts[currencyId] - prev;
        OnCurrencyChanged?.Invoke(currencyId, delta);
    }

    public void Add(string currencyId, long delta) => Set(currencyId, Get(currencyId) + delta);

    /// <summary>扣款：不足返回 false（不扣）。</summary>
    public bool Spend(string currencyId, long cost)
    {
        if (cost <= 0) return true;
        if (Get(currencyId) < cost) return false;
        Set(currencyId, Get(currencyId) - cost);
        return true;
    }
}
