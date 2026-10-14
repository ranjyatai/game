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
        // 和背包一样要跨场景常驻，否则换图货币会清零
        if (Application.isPlaying)
        {
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
        }
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

    // ── 存档 ──────────────────────────────────────────────────────────────

    public List<SavedCurrencyEntry> Serialize()
    {
        var list = new List<SavedCurrencyEntry>();
        foreach (var kv in _amounts)
            if (kv.Value > 0) list.Add(new SavedCurrencyEntry { currencyId = kv.Key, amount = kv.Value });
        return list;
    }

    /// <summary>读档 / 新游戏：先把现有货币全部清零（触发变化事件，界面能刷新），再按存档数据写回。</summary>
    public void Deserialize(List<SavedCurrencyEntry> saved)
    {
        foreach (var id in new List<string>(_amounts.Keys)) Set(id, 0);
        _amounts.Clear();
        if (saved == null) return;
        foreach (var s in saved)
            if (s != null && !string.IsNullOrEmpty(s.currencyId)) Set(s.currencyId, s.amount);
    }

    /// <summary>扣款：不足返回 false（不扣）。</summary>
    public bool Spend(string currencyId, long cost)
    {
        if (cost <= 0) return true;
        if (Get(currencyId) < cost) return false;
        Set(currencyId, Get(currencyId) - cost);
        return true;
    }
}
