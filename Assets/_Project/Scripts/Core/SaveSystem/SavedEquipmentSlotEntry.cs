using System;

/// <summary>
/// 单个装备槽的存档快照——记录这个槽位当前穿着的是哪件物品。
/// 跟背包(SavedItemEntry列表)是分开存的，因为装备到某个槽位时该实例会从背包移除
/// (EquipmentRuntime.TryEquipFromInventory)，不再是背包数据的一部分。
/// </summary>
[Serializable]
public class SavedEquipmentSlotEntry
{
    public EquipmentSlotType slot;
    public SavedItemEntry item;

    public SavedEquipmentSlotEntry() { }

    public SavedEquipmentSlotEntry(EquipmentSlotType slot, InventoryItemEntry entry)
    {
        this.slot = slot;
        item = new SavedItemEntry(entry);
    }
}
