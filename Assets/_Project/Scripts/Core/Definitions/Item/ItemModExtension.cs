using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 武器组件（mod）物品的扩展定义。挂在 ItemDefinition 上。
/// 组件本身也是背包里的物品，可以被鉴定、安装到武器槽位、或从工房拆卸。
/// </summary>
[Serializable]
public class ItemModExtension
{
    [Header("槽位兼容性")]
    [Tooltip("可安装的槽位类型键列表（与 ModSlotDefinition.slotTypeKey 对应）。")]
    public string[] compatibleSlotTypeKeys = Array.Empty<string>();

    [Header("鉴定")]
    [Tooltip("未鉴定时显示的名称，例如「未鉴定·剑刃」。")]
    public string unidentifiedDisplayName = "未鉴定组件";

    [Tooltip("鉴定费用（代币）。")]
    [Min(0)] public int identificationCost = 100;

    [Header("词条池")]
    [Tooltip("掉落时从这个池里随机抽取词条并投掷数值。")]
    public List<PossibleModBonus> bonusPool = new List<PossibleModBonus>();

    [Tooltip("每个组件固定拥有几条词条。")]
    [Min(1)] public int bonusCount = 2;

    // ── 便捷查询 ──────────────────────────────────────────────────────────

    public bool IsCompatibleWith(string slotTypeKey)
    {
        foreach (var key in compatibleSlotTypeKeys)
            if (key == slotTypeKey) return true;
        return false;
    }
}
