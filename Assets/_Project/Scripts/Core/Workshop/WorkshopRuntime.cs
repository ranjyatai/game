using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 工房服务逻辑：修复耐久 + 安装/卸载改装组件。
/// 无状态单例，所有方法都是纯操作，结果即时生效于 InventoryItemEntry。
/// </summary>
public static class WorkshopRuntime
{
    // ── 耐久修复 ──────────────────────────────────────────────────────────

    public enum RepairResult
    {
        Success,
        AlreadyFull,
        InsufficientFunds,
        InvalidItem,
    }

    /// <summary>计算修复费用（按缺损量线性计费）。</summary>
    public static int GetRepairCost(InventoryItemEntry weapon, string currencyId, int costPerPoint = 5)
    {
        if (weapon?.definition?.equipment == null) return 0;
        int missing = weapon.definition.equipment.maxDurability - weapon.currentDurability;
        return Mathf.Max(0, missing * costPerPoint);
    }

    /// <summary>修复目标物品的耐久度到满值，扣除对应货币。</summary>
    public static RepairResult Repair(InventoryItemEntry weapon, string currencyId, int costPerPoint = 5)
    {
        if (weapon?.definition?.equipment == null) return RepairResult.InvalidItem;

        int max = weapon.definition.equipment.maxDurability;
        if (max <= 0) return RepairResult.InvalidItem; // 不参与耐久系统

        if (weapon.currentDurability >= max) return RepairResult.AlreadyFull;

        int cost = GetRepairCost(weapon, currencyId, costPerPoint);
        if (cost > 0 && CurrencyRuntime.Instance != null)
        {
            if (!CurrencyRuntime.Instance.Spend(currencyId, cost))
                return RepairResult.InsufficientFunds;
        }

        weapon.currentDurability = max;
        return RepairResult.Success;
    }

    // ── 组件安装 ──────────────────────────────────────────────────────────

    public enum InstallResult
    {
        Success,
        SlotNotFound,
        SlotOccupied,
        IncompatibleType,
        ModItemInvalid,
    }

    /// <summary>
    /// 把 modItem 安装到 weapon 的 slotKey 槽位。
    /// modItem 必须从背包消耗掉，由调用方（WorkshopWindowController）负责调用 inventory.RemoveItem。
    /// </summary>
    public static InstallResult Install(
        InventoryItemEntry weapon,
        string slotKey,
        InventoryItemEntry modItem)
    {
        if (weapon?.definition?.equipment == null) return InstallResult.SlotNotFound;
        if (modItem?.definition?.mod == null)      return InstallResult.ModItemInvalid;

        // 找槽位定义
        ModSlotDefinition slotDef = FindSlot(weapon, slotKey);
        if (slotDef == null) return InstallResult.SlotNotFound;

        // 已有组件
        if (weapon.HasModInSlot(slotKey)) return InstallResult.SlotOccupied;

        // 类型兼容检查
        if (!modItem.definition.mod.IsCompatibleWith(slotDef.slotTypeKey))
            return InstallResult.IncompatibleType;

        var entry = new InstalledModEntry
        {
            slotKey    = slotKey,
            modItemKey = modItem.definition.itemKey,
            isIdentified = modItem.isIdentified,
            bonuses    = new List<RolledModBonus>(modItem.rolledBonuses),
        };
        weapon.installedMods.Add(entry);
        return InstallResult.Success;
    }

    // ── 组件卸载 ──────────────────────────────────────────────────────────

    public enum RemoveResult
    {
        Success,
        SlotEmpty,
        InventoryFull,
    }

    /// <summary>
    /// 从 weapon 的 slotKey 槽卸载组件，还回 inventory。
    /// </summary>
    public static RemoveResult Remove(
        InventoryItemEntry weapon,
        string slotKey,
        InventoryRuntime inventory,
        ItemRegistry registry)
    {
        var mod = weapon.GetInstalledMod(slotKey);
        if (mod == null) return RemoveResult.SlotEmpty;

        // 查找组件物品定义，还回背包
        if (registry != null && registry.TryFind(mod.modItemKey, out var modDef))
        {
            int leftover = inventory.SimulateAdd(modDef, 1);
            if (leftover > 0) return RemoveResult.InventoryFull;
            inventory.AddItem(modDef, 1);
        }

        weapon.installedMods.Remove(mod);
        return RemoveResult.Success;
    }

    // ── 工具 ─────────────────────────────────────────────────────────────

    public static ModSlotDefinition FindSlot(InventoryItemEntry weapon, string slotKey)
    {
        if (weapon?.definition?.equipment == null) return null;
        foreach (var s in weapon.definition.equipment.modSlots)
            if (s.slotKey == slotKey) return s;
        return null;
    }

    /// <summary>返回当前武器空置的槽位列表。</summary>
    public static List<ModSlotDefinition> GetEmptySlots(InventoryItemEntry weapon)
    {
        var result = new List<ModSlotDefinition>();
        if (weapon?.definition?.equipment == null) return result;
        foreach (var s in weapon.definition.equipment.modSlots)
            if (!weapon.HasModInSlot(s.slotKey)) result.Add(s);
        return result;
    }
}
