using System.Collections.Generic;

/// <summary>
/// 「这个存档有没有持有过这件物品」的记录。背包每次获得物品时登记（InventoryRuntime.AddItem），随存档保存。
/// 旧存档没有这份记录：读档后第一次用到时，把当前背包 / 仓库 / 已装备里已有的物品也算作持有过。
/// </summary>
public static class SkyPrisonItemDiscovery
{
    static HashSet<string> set;
    static PlayerSaveData bound;

    static bool Ensure()
    {
        var p = SaveManager.Player;
        if (p == null) return false;
        if (bound == p && set != null) return true;
        bound = p;
        if (p.discoveredItemKeys == null) p.discoveredItemKeys = new List<string>();
        set = new HashSet<string>(p.discoveredItemKeys);
        // 旧存档兜底：现在手上已经有的，一定持有过
        void Add(SavedItemEntry e) { if (e != null && !string.IsNullOrEmpty(e.itemKey) && set.Add(e.itemKey)) p.discoveredItemKeys.Add(e.itemKey); }
        if (p.backpack != null) foreach (var e in p.backpack) Add(e);
        if (p.stashPages != null) foreach (var pg in p.stashPages) if (pg != null && pg.items != null) foreach (var e in pg.items) Add(e);
        if (p.equippedSlots != null) foreach (var s in p.equippedSlots) if (s != null) Add(s.item);
        return true;
    }

    public static void MarkDiscovered(ItemDefinition def)
    {
        if (def == null || string.IsNullOrEmpty(def.itemKey) || !Ensure()) return;
        if (set.Add(def.itemKey)) bound.discoveredItemKeys.Add(def.itemKey);
    }

    /// <summary>持有过 = true。没有存档数据（比如直接从地图场景进 Play）按"没持有过"算。</summary>
    public static bool IsDiscovered(ItemDefinition def)
    {
        if (def == null || string.IsNullOrEmpty(def.itemKey) || !Ensure()) return false;
        return set.Contains(def.itemKey);
    }
}
