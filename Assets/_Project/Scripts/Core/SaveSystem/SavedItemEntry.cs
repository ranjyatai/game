using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 背包/仓库里一格物品的序列化快照。
/// 用 itemKey 字符串引用物品定义，避免直接持有 ScriptableObject 引用。
/// </summary>
[Serializable]
public class SavedItemEntry
{
    public string itemKey;
    public int    count;

    /// <summary>当前耐久度。-1 = 不参与耐久系统。</summary>
    public int durability = -1;

    /// <summary>武器组件：是否已鉴定。</summary>
    public bool isIdentified = true;

    /// <summary>武器组件：已投掷的词条实例。</summary>
    public List<RolledModBonus> rolledBonuses = new List<RolledModBonus>();

    /// <summary>武器：各槽位安装的组件。</summary>
    public List<InstalledModEntry> installedMods = new List<InstalledModEntry>();

    /// <summary>装备：3个染色区域当前颜色。</summary>
    public Color[] dyeColors;

    /// <summary>装备：幻化外观来源的itemKey，空="没有幻化，显示本来的样子"。</summary>
    public string transmogSourceItemKey = "";

    public SavedItemEntry() { }

    public SavedItemEntry(InventoryItemEntry entry)
    {
        itemKey      = entry.definition?.itemKey ?? "";
        count        = entry.count;
        durability   = entry.currentDurability;
        isIdentified = entry.isIdentified;
        rolledBonuses  = entry.rolledBonuses  ?? new List<RolledModBonus>();
        installedMods  = entry.installedMods  ?? new List<InstalledModEntry>();
        dyeColors      = entry.dyeColors;
        transmogSourceItemKey = entry.transmogSourceItemKey ?? "";
    }

    /// <summary>反向重建——从这份快照还原出一个真正的 InventoryItemEntry。用于装备槽
    /// 存档还原（EquipmentRuntime 的槽位不是背包，物品被装备时已经从背包移除了，不能
    /// 靠 InventoryRuntime.Deserialize 那条路一起还原，需要单独重建）。找不到对应
    /// ItemDefinition（itemKey为空/registry查不到）时返回 null。</summary>
    public InventoryItemEntry ToInventoryItemEntry(ItemRegistry registry)
    {
        if (registry == null || string.IsNullOrEmpty(itemKey)) return null;
        if (!registry.TryFind(itemKey, out var def)) return null;

        var result = new InventoryItemEntry(def, Math.Max(1, count));
        if (durability >= 0) result.currentDurability = durability;
        if (rolledBonuses?.Count > 0) result.rolledBonuses = rolledBonuses;
        if (installedMods?.Count > 0) result.installedMods = installedMods;
        if (dyeColors?.Length == 3) result.dyeColors = dyeColors;
        if (!string.IsNullOrEmpty(transmogSourceItemKey)) result.transmogSourceItemKey = transmogSourceItemKey;
        result.isIdentified = isIdentified;
        return result;
    }
}
