/// <summary>
/// 古物街鉴定服务：收费 → 设 isIdentified = true → 词条对外可见、属性生效。
/// 无状态静态类。
/// </summary>
public static class IdentificationRuntime
{
    public enum IdentifyResult
    {
        Success,
        AlreadyIdentified,
        InsufficientFunds,
        InvalidItem,
    }

    /// <summary>计算鉴定费用（读 ItemModExtension.identificationCost）。</summary>
    public static int GetCost(InventoryItemEntry entry)
    {
        if (entry?.definition?.mod == null) return 0;
        return entry.definition.mod.identificationCost;
    }

    /// <summary>鉴定单件物品：扣款 + 设 isIdentified = true。</summary>
    public static IdentifyResult Identify(InventoryItemEntry entry, string currencyId)
    {
        if (entry?.definition?.mod == null) return IdentifyResult.InvalidItem;
        if (entry.isIdentified)             return IdentifyResult.AlreadyIdentified;

        int cost = GetCost(entry);
        if (cost > 0 && CurrencyRuntime.Instance != null)
        {
            if (!CurrencyRuntime.Instance.Spend(currencyId, cost))
                return IdentifyResult.InsufficientFunds;
        }

        entry.isIdentified = true;

        // 鉴定后同步到装备属性（如果这件组件已安装在当前装备武器上）
        SyncToEquippedWeapon(entry);

        return IdentifyResult.Success;
    }

    /// <summary>批量鉴定背包内所有未鉴定组件，返回实际鉴定数量。</summary>
    public static int IdentifyAll(InventoryRuntime inventory, string currencyId)
    {
        if (inventory == null) return 0;
        int count = 0;
        foreach (var slot in inventory.Slots)
        {
            if (slot == null || slot.isIdentified) continue;
            if (slot.definition?.mod == null) continue;
            if (Identify(slot, currencyId) == IdentifyResult.Success) count++;
        }
        return count;
    }

    // 如果该组件已安装在已装备的武器/防具上，把槽位里的 isIdentified 同步为 true，再触发属性重算
    private static void SyncToEquippedWeapon(InventoryItemEntry modItem)
    {
        var eq = EquipmentRuntime.Instance;
        if (eq == null) return;

        bool anyChanged = false;
        foreach (EquipmentSlotType slotType in System.Enum.GetValues(typeof(EquipmentSlotType)))
        {
            var equipped = eq.GetEquipped(slotType);
            if (equipped == null) continue;
            foreach (var mod in equipped.installedMods)
            {
                if (mod.modItemKey != modItem.definition.itemKey || mod.isIdentified) continue;
                mod.isIdentified = true;
                anyChanged = true;
            }
        }

        // 只要有变动就触发 EquipmentStatApplier 重算：re-equip 当前武器即可（Applier 同时扫全槽）
        if (anyChanged)
        {
            var weapon = eq.GetWeapon();
            if (weapon != null)
            {
                eq.Unequip(EquipmentSlotType.Weapon);
                eq.Equip(weapon);
            }
        }
    }
}
