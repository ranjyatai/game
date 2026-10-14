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

    // 破损档位加价——用户明确要求"耐久度为0显然需要高昂费用"，不能是纯线性
    // (缺10点花50、缺50点花250这种"每点单价恒定"的算法感觉不出"越接近报废越
    // 麻烦"这个直觉)。复用 DurabilitySystem 的档位划分(60%/30%/10%/1%阈值)，只是
    // 那边原本没人调用；Destroyed(耐久=0)档倍率明确调得比其它档更高，拉开差距。
    private const float StateMultiplierDamaged   = 1.5f;
    private const float StateMultiplierBroken    = 2.0f;
    private const float StateMultiplierDestroyed = 4.0f; // 耐久归零单独拉高，不是简单跟Broken一个梯度

    // 装了改装组件的武器维护成本更高——用户明确要求"和武器有没有强化、装了部件
    // 等有关"，等级本身故意不算(等级已经体现在装备本身的稀有度/售价上，修理费
    // 再跟着等级涨容易变成"买得起修不起"，是否要那种资源压力设计留给以后单独决定)。
    private const float ModMultiplierPerSlot = 0.15f; // 每装一个组件+15%维护成本

    /// <summary>
    /// 计算修复费用：基础(缺损量×单价) × 破损档位倍率(越接近报废越贵) × 改装组件
    /// 加成(装的组件越多维护成本越高)。
    /// </summary>
    public static int GetRepairCost(InventoryItemEntry weapon, string currencyId, int costPerPoint = 5)
    {
        if (weapon?.definition?.equipment == null) return 0;
        int missing = weapon.definition.equipment.maxDurability - weapon.currentDurability;
        if (missing <= 0) return 0;

        float stateMultiplier = DurabilitySystem.GetState(weapon) switch
        {
            DurabilitySystem.DurabilityState.Destroyed => StateMultiplierDestroyed,
            DurabilitySystem.DurabilityState.Broken    => StateMultiplierBroken,
            DurabilitySystem.DurabilityState.Damaged   => StateMultiplierDamaged,
            _                                           => 1f,
        };

        int modCount = weapon.installedMods?.Count ?? 0;
        float modMultiplier = 1f + modCount * ModMultiplierPerSlot;

        return Mathf.Max(1, Mathf.CeilToInt(missing * costPerPoint * stateMultiplier * modMultiplier));
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

    /// <summary>
    /// 汇总玩家身上所有已装备、且参与耐久系统的装备（武器双槽+全部防具）——公开给
    /// 修理弹窗的表格UI枚举展示用，不止内部算总价/一键修复自己用。
    /// </summary>
    public static IEnumerable<InventoryItemEntry> GetAllEquippedRepairable()
    {
        var eq = EquipmentRuntime.Instance;
        if (eq == null) yield break;

        var weapon = eq.GetWeapon();
        if (weapon?.definition?.equipment != null && weapon.definition.equipment.maxDurability > 0)
            yield return weapon;

        var weaponSecondary = eq.GetWeaponSecondary();
        if (weaponSecondary?.definition?.equipment != null && weaponSecondary.definition.equipment.maxDurability > 0)
            yield return weaponSecondary;

        foreach (var armor in eq.GetAllArmor())
            if (armor?.definition?.equipment != null && armor.definition.equipment.maxDurability > 0)
                yield return armor;
    }

    /// <summary>身上所有装备(武器+防具)一次性修满的总费用——按各自缺损量线性累加。</summary>
    public static int GetRepairAllCost(string currencyId, int costPerPoint = 5)
    {
        int total = 0;
        foreach (var item in GetAllEquippedRepairable())
            total += GetRepairCost(item, currencyId, costPerPoint);
        return total;
    }

    /// <summary>
    /// 一键修复身上所有装备到满耐久，一次性扣总费用（不是逐件单独扣款）——避免扣到一半
    /// 钱不够，前面几件修了后面几件没修这种"部分修复"的糊涂状态。
    /// </summary>
    public static RepairResult RepairAll(string currencyId, int costPerPoint = 5)
    {
        var needRepair = new List<InventoryItemEntry>();
        int total = 0;
        foreach (var item in GetAllEquippedRepairable())
        {
            int cost = GetRepairCost(item, currencyId, costPerPoint);
            if (cost <= 0) continue;
            needRepair.Add(item);
            total += cost;
        }

        if (needRepair.Count == 0) return RepairResult.AlreadyFull;

        if (total > 0)
        {
            if (CurrencyRuntime.Instance == null) return RepairResult.InsufficientFunds;
            if (!CurrencyRuntime.Instance.Spend(currencyId, total))
                return RepairResult.InsufficientFunds;
        }

        foreach (var item in needRepair)
            item.currentDurability = item.definition.equipment.maxDurability;

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
