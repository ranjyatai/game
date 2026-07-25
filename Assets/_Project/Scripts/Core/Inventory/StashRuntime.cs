using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 基地仓库运行时。单例，DontDestroyOnLoad。
/// 管理多页仓库，每页是一个独立的 InventoryRuntime 实例。
/// 页数由仓库设施等级决定（LV1=1页，LV2=2页……）。
/// </summary>
public class StashRuntime : MonoBehaviour
{
    public static StashRuntime Instance { get; private set; }

    public const int SlotsPerPage  = 100;
    public const int MaxPages      = 4;
    public const int DefaultColumns = 5;

    /// <summary>当前解锁的页数（由设施等级驱动）。</summary>
    public int UnlockedPages { get; private set; } = 1;

    private readonly List<InventoryRuntime> _pages = new List<InventoryRuntime>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 预建 MaxPages 个 InventoryRuntime，每页挂在独立子 GameObject 上
        for (int i = 0; i < MaxPages; i++)
        {
            var child = new GameObject($"StashPage_{i}");
            child.transform.SetParent(transform);
            var inv = child.AddComponent<InventoryRuntime>();
            inv.SetInitialCapacity(SlotsPerPage);
            _pages.Add(inv);
        }

        BaseManager.OnFacilityUpgraded += HandleFacilityUpgraded;
    }

    private void OnDestroy()
    {
        BaseManager.OnFacilityUpgraded -= HandleFacilityUpgraded;
    }

    // ── 页访问 ────────────────────────────────────────────────────────────

    /// <summary>获取指定页的 InventoryRuntime（页码 0 起）。</summary>
    public InventoryRuntime GetPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pages.Count) return null;
        return _pages[pageIndex];
    }

    public bool IsPageUnlocked(int pageIndex) => pageIndex < UnlockedPages;

    // ── 设施升级解锁新页 ──────────────────────────────────────────────────

    private void HandleFacilityUpgraded(FacilityType type, int newLevel)
    {
        // 仓库每升一级解锁一页（LV1=1页，LV2=2页……）
        if (type != FacilityType.Workshop) return;
        UnlockedPages = Mathf.Clamp(newLevel, 1, MaxPages);
    }

    // ── 存档序列化 ────────────────────────────────────────────────────────

    public List<StashPageSaveData> Serialize(ItemRegistry registry)
    {
        var result = new List<StashPageSaveData>();
        for (int i = 0; i < _pages.Count; i++)
        {
            result.Add(new StashPageSaveData
            {
                pageIndex = i,
                items     = _pages[i].Serialize(),
            });
        }
        return result;
    }

    public void Deserialize(List<StashPageSaveData> saved, ItemRegistry registry)
    {
        // 先清空所有页
        foreach (var page in _pages)
            page.Deserialize(null, registry);

        if (saved == null) return;

        foreach (var pageData in saved)
        {
            if (pageData.pageIndex < 0 || pageData.pageIndex >= _pages.Count) continue;
            _pages[pageData.pageIndex].Deserialize(pageData.items, registry);
        }
    }
}
