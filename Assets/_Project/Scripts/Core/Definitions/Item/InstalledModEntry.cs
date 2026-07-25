using System;
using System.Collections.Generic;

/// <summary>
/// 已安装在武器某个槽位上的组件实例。存在武器的 InventoryItemEntry 里。
/// </summary>
[Serializable]
public class InstalledModEntry
{
    /// <summary>对应武器上的哪个槽（ModSlotDefinition.slotKey）。</summary>
    public string slotKey;

    /// <summary>组件物品的 itemKey。</summary>
    public string modItemKey;

    /// <summary>该组件是否已鉴定。</summary>
    public bool isIdentified;

    /// <summary>已投掷的词条实例（鉴定前同样存在，只是 UI 不显示数值）。</summary>
    public List<RolledModBonus> bonuses = new List<RolledModBonus>();
}
