using System;
using UnityEngine;

/// <summary>
/// 组件词条的可能范围定义（存在 ItemModExtension 里，作为词条池）。
/// 掉落时从池中随机抽取 bonusCount 条，在 minValue~maxValue 之间投掷数值，
/// 结果存入 InventoryItemEntry.rolledBonuses，鉴定前对玩家隐藏。
/// </summary>
[Serializable]
public class PossibleModBonus
{
    /// <summary>属性键，与 TechTreeRewardData.key 同规范（如 "AttackPower"、"CritRate"）。</summary>
    public string statKey;

    /// <summary>UI 显示名（如 "攻击力"、"暴击率"）。</summary>
    public string displayName;

    [Min(0)] public float minValue;
    [Min(0)] public float maxValue;

    /// <summary>是否为百分比（显示时加 %）。</summary>
    public bool isPercent;

    /// <summary>抽取权重，越高越容易被选中。</summary>
    [Min(0.01f)] public float weight = 1f;
}
