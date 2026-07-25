using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class InventoryItemEntry
{
    public ItemDefinition definition;
    public int count;
    public long acquireTime;

    // 新获得未查看：左上角 NEW 徽标用。鼠标悬停看过后置 false。
    public bool isNew;

    // ── 耐久度（仅装备有效，消耗品 = -1 表示不参与）──────────────────────
    /// <summary>当前耐久度。-1 = 不参与耐久系统。</summary>
    public int currentDurability = -1;

    // ── 弹匣（仅热武器有效）──────────────────────────────────────────────
    /// <summary>当前弹匣内弹药数。-1 = 不参与弹匣系统（近战武器/未勾选usesAmmo的装备）。
    /// 攻击消耗这里的数值，打空后要按换弹键从背包（按口径汇总的备用弹药）补充。</summary>
    public int currentMagazineAmmo = -1;

    // ── 武器组件（仅组件物品有效）────────────────────────────────────────
    /// <summary>
    /// 该组件是否已鉴定。未鉴定时 UI 显示 unidentifiedDisplayName，隐藏 rolledBonuses 数值。
    /// 非组件物品此字段无意义（默认 true 即可）。
    /// </summary>
    public bool isIdentified = true;

    /// <summary>掉落时投掷好的词条实例。鉴定前数值对玩家不可见，但数据已确定。</summary>
    public List<RolledModBonus> rolledBonuses = new List<RolledModBonus>();

    // ── 已装备武器的改装槽（仅武器有效）─────────────────────────────────
    /// <summary>该武器各槽位当前安装的组件。slotKey → InstalledModEntry。</summary>
    public List<InstalledModEntry> installedMods = new List<InstalledModEntry>();

    // ── 染色（仅装备/武器有效）───────────────────────────────────────────
    /// <summary>3个染色区域当前颜色，按顺序对应染色区域1/2/3。掉落时从
    /// ItemEquipmentExtension.defaultDyeColorSchemes 里随机抽一套初始化，工坊改色时
    /// 直接改这里，消耗一个染料改一个区域。</summary>
    public Color[] dyeColors;

    // ── 光学迷彩 / 幻化（仅装备/武器有效）────────────────────────────────
    /// <summary>幻化外观来源的itemKey——不为空时，渲染这件装备用这个itemKey对应
    /// 装备的造型+染色，但属性/耐久/词条/这件装备自己的染色数据都不受影响，
    /// 只是"看着像"另一件装备。null=显示自己本来的样子。</summary>
    public string transmogSourceItemKey;

    public InventoryItemEntry(ItemDefinition def, int amount)
    {
        definition  = def;
        count       = amount;
        acquireTime = DateTime.UtcNow.Ticks;

        // 装备且有耐久度定义时初始化为满耐久
        if (def?.equipment != null && def.equipment.maxDurability > 0)
            currentDurability = def.equipment.maxDurability;

        // 热武器掉落/生成时默认满弹匣
        if (def?.equipment != null && def.equipment.usesAmmo && def.equipment.magazineSize > 0)
            currentMagazineAmmo = def.equipment.magazineSize;

        // 装备掉落时从配色方案列表里随机抽一套初始化；没配方案的话给中性白
        // （乘法叠色下=不改变原图）
        if (def?.equipment != null)
            dyeColors = def.equipment.GetRandomDefaultDyeColors();

        // 组件物品：掉落时自动投掷词条，默认未鉴定
        if (def?.mod != null && def.mod.bonusPool.Count > 0)
        {
            isIdentified = false;
            rolledBonuses = ModRoller.Roll(def.mod);
        }
    }

    public bool IsEmpty    => definition == null || count <= 0;
    public bool IsStackFull => definition != null && count >= definition.maxStackCount;
    public int  StackRoom  => definition != null ? definition.maxStackCount - count : 0;
    public bool CanDiscard => definition != null && definition.canDiscard && !definition.isKeyItem;

    // ── 改装槽便捷查询 ────────────────────────────────────────────────────

    public InstalledModEntry GetInstalledMod(string slotKey)
    {
        foreach (var m in installedMods)
            if (m.slotKey == slotKey) return m;
        return null;
    }

    public bool HasModInSlot(string slotKey) => GetInstalledMod(slotKey) != null;
}
