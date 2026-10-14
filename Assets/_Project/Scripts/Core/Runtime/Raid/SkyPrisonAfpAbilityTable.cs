using System;
using UnityEngine;

/// <summary>
/// AFP 里程碑能力的「按档数据表」：每个能力有若干档(每 5 级一档)，每一档都有自己的一句话效果、自己的科技树节点。
/// 购买窗口(能力块的「已达成 + 下一档预告」)和角色面板第二页(档位阶梯)都读这张表，所以以后某一档想换成「新词条」而不是「数值变大」，
/// 只需要改这一档的效果文字和对应的实现，UI 不用动。
/// 科技树节点：多档能力每档一个，ID = 前缀_档位(例 seeker_afp_heat_shield_1 ~ _4)，高档位要求前面的档位节点都已点亮；单档能力(生命保险)用前缀本身。
/// </summary>
public static class SkyPrisonAfpAbilityTable
{
    public sealed class AbilityDef
    {
        public string id;                 // field_heat / field_shock / insurance / burst / mastery
        public string upgradeKey;         // 对应的 AFP 升级项 key(决定等级从哪读)
        public string nodePrefix;         // 科技树节点前缀
        public int maxTier = 4, levelsPerTier = 5, icon;
        public Func<string> name;                 // 能力名(不含档位)
        public Func<int, string> shortEffect;     // 第 t 档的一句话效果(已本地化)
        public bool MultiTier => maxTier > 1;
    }

    static string L(string key, string fallback) => DoorText.Get(key, fallback);

    public static readonly AbilityDef[] All =
    {
        new AbilityDef { id = "field_heat", upgradeKey = "heat", nodePrefix = "seeker_afp_heat_shield", icon = SkyPrisonAfpIcons.Shield,
            name = () => string.Format(L("ui_afp_ab_field_fmt", "特效力场：{0}"), L("ui_afp_dmg_heat", "灼热")),
            shortEffect = t => string.Format(L("ui_afp_ab_field_eff_fmt", "豁免一次{0}异常 · {1}秒恢复"), L("ui_afp_dmg_heat", "灼热"), Mathf.RoundToInt(SkyPrisonAfpAbsorbShield.RechargeSecondsForTier(t))) },
        new AbilityDef { id = "field_shock", upgradeKey = "shock", nodePrefix = "seeker_afp_shock_shield", icon = SkyPrisonAfpIcons.Shield,
            name = () => string.Format(L("ui_afp_ab_field_fmt", "特效力场：{0}"), L("ui_afp_dmg_shock", "电磁")),
            shortEffect = t => string.Format(L("ui_afp_ab_field_eff_fmt", "豁免一次{0}异常 · {1}秒恢复"), L("ui_afp_dmg_shock", "电磁"), Mathf.RoundToInt(SkyPrisonAfpAbsorbShield.RechargeSecondsForTier(t))) },
        new AbilityDef { id = "field_corrosion", upgradeKey = "corrosion", nodePrefix = "seeker_afp_corrosion_shield", icon = SkyPrisonAfpIcons.Shield,
            name = () => string.Format(L("ui_afp_ab_field_fmt", "特效力场：{0}"), L("ui_afp_dmg_corrosion", "腐蚀")),
            shortEffect = t => string.Format(L("ui_afp_ab_field_eff_fmt", "豁免一次{0}异常 · {1}秒恢复"), L("ui_afp_dmg_corrosion", "腐蚀"), Mathf.RoundToInt(SkyPrisonAfpAbsorbShield.RechargeSecondsForTier(t))) },
        new AbilityDef { id = "field_freeze", upgradeKey = "freeze", nodePrefix = "seeker_afp_freeze_shield", icon = SkyPrisonAfpIcons.Shield,
            name = () => string.Format(L("ui_afp_ab_field_fmt", "特效力场：{0}"), L("ui_afp_dmg_freeze", "冻结")),
            shortEffect = t => string.Format(L("ui_afp_ab_field_eff_fmt", "豁免一次{0}异常 · {1}秒恢复"), L("ui_afp_dmg_freeze", "冻结"), Mathf.RoundToInt(SkyPrisonAfpAbsorbShield.RechargeSecondsForTier(t))) },
        new AbilityDef { id = "insurance", upgradeKey = "hp", nodePrefix = "seeker_afp_life_insurance", maxTier = 1, levelsPerTier = 5, icon = SkyPrisonAfpIcons.Insurance,
            name = () => L("ui_afp_ab_insurance", "生命保险"),
            shortEffect = t => L("ui_afp_ab_exempt", "死亡豁免") },
        new AbilityDef { id = "burst", upgradeKey = "attrAtk", nodePrefix = "seeker_afp_attr_burst", icon = SkyPrisonAfpIcons.Bolt,
            name = () => L("ui_afp_ab_burst", "异常追伤"),
            shortEffect = t => string.Format(L("ui_afp_ab_burst_short_fmt", "追伤 攻击力×{0} · 异常增伤 +{1}%"), SkyPrisonAfpAnomalyBurst.MultiplierForTier(t).ToString("0.0"), Mathf.RoundToInt(SkyPrisonAfpAnomalyBurst.VulnerabilityPerTier * 100f * t)) },
        new AbilityDef { id = "usurp", upgradeKey = "", nodePrefix = "seeker_usurper", maxTier = 1, icon = SkyPrisonAfpIcons.Drop,
            name = () => L("ui_afp_ab_usurp", "篡夺者"),
            shortEffect = t => L("ui_afp_ab_usurp_short", "攻击回复红色血量") },
        new AbilityDef { id = "cycle", upgradeKey = "physAtk", nodePrefix = "seeker_afp_cycle", icon = SkyPrisonAfpIcons.Cycle,
            name = () => L("ui_afp_ab_cycle", "回转"),
            shortEffect = t => string.Format(L("ui_afp_ab_cycle_short_fmt", "击杀回复 {0}% LP"), Mathf.RoundToInt(SkyPrisonAfpPhysicalMastery.KillLpFraction[Mathf.Clamp(t, 1, 4) - 1] * 100f)) },
        new AbilityDef { id = "lethal", upgradeKey = "physAtk", nodePrefix = "seeker_afp_lethal", icon = SkyPrisonAfpIcons.Bullet,
            name = () => L("ui_afp_ab_lethal", "致命子弹"),
            shortEffect = t => string.Format(L("ui_afp_ab_lethal_short_fmt_v2", "每第 {0} 发 伤害 +{1}%"), SkyPrisonAfpPhysicalMastery.DeadlyEvery,
                Mathf.RoundToInt(SkyPrisonAfpPhysicalMastery.DeadlyBonus[Mathf.Clamp(t, 1, 4) - 1] * 100f)) },
        // 弱点特效 / 暴击特效满级(Lv3)的质变：单档，节点就是词条节点本身
        new AbilityDef { id = "armorbreak", upgradeKey = SkyPrisonAfpWeakCrit.WeakKey, nodePrefix = SkyPrisonAfpWeakCrit.WeakNodeId, maxTier = 1, levelsPerTier = SkyPrisonAfpWeakCrit.MaxLevel, icon = SkyPrisonAfpIcons.Bolt,
            name = () => L("ui_afp_ab_armorbreak", "破甲冲击"),
            shortEffect = t => L("ui_afp_ab_armorbreak_short", "弱点暴击 失衡×3 · 无视霸体") },
        new AbilityDef { id = "critrecover", upgradeKey = SkyPrisonAfpWeakCrit.CritKey, nodePrefix = SkyPrisonAfpWeakCrit.CritNodeId, maxTier = 1, levelsPerTier = SkyPrisonAfpWeakCrit.MaxLevel, icon = SkyPrisonAfpIcons.Bullet,
            name = () => L("ui_afp_ab_critrecover", "会心回收"),
            shortEffect = t => L("ui_afp_ab_critrecover_short", "枪暴击 40% 退弹 · 剑暴击回 4% LP") },
    };

    public static AbilityDef Find(string id)
    {
        for (int i = 0; i < All.Length; i++) if (All[i].id == id) return All[i];
        return null;
    }

    /// <summary>某个 AFP 升级项上挂的所有能力(例如灼热抗性 → 灼热力场；追加生命 → 生命保险)。</summary>
    public static AbilityDef FindForUpgrade(string upgradeKey)
    {
        for (int i = 0; i < All.Length; i++) if (All[i].upgradeKey == upgradeKey) return All[i];
        return null;
    }

    /// <summary>挂在同一个升级项上的全部能力(物理攻击强化 → 回转 + 致命子弹)，按表里的顺序。</summary>
    public static System.Collections.Generic.List<AbilityDef> FindAllForUpgrade(string upgradeKey)
    {
        var list = new System.Collections.Generic.List<AbilityDef>();
        for (int i = 0; i < All.Length; i++) if (All[i].upgradeKey == upgradeKey) list.Add(All[i]);
        return list;
    }

    public static string NodeId(AbilityDef a, int tier) => a.MultiTier ? a.nodePrefix + "_" + tier : a.nodePrefix;

    /// <summary>第 t 档的科技树节点(含前面所有档)都点亮了吗(测试开关打开时一律算点亮)。</summary>
    public static bool TierUnlocked(AbilityDef a, int tier)
    {
        for (int i = 1; i <= tier; i++)
            if (!SkyPrisonAfpAbsorbShield.NodeUnlocked(NodeId(a, i))) return false;
        return true;
    }

    /// <summary>能力对玩家是否可见(第 1 档节点点亮)。没点亮的能力在窗口里完全不显示。</summary>
    public static bool Visible(AbilityDef a) => SkyPrisonAfpAbsorbShield.NodeUnlocked(NodeId(a, 1));

    /// <summary>在某个升级项等级下达成的档位：等级够 + 每一档的节点都点亮。</summary>
    public static int TierAtLevel(AbilityDef a, int level)
    {
        int t = Mathf.Min(level / a.levelsPerTier, a.maxTier);
        while (t > 0 && !TierUnlocked(a, t)) t--;
        return t;
    }

    /// <summary>当前玩家的这个能力档位(按已买等级，不含预加点)。</summary>
    public static int CurrentTier(AbilityDef a)
    {
        int lv = 0;
        for (int i = 0; i < SkyPrisonAfpUpgrades.All.Length; i++)
            if (SkyPrisonAfpUpgrades.All[i].key == a.upgradeKey) lv = SkyPrisonAfpUpgrades.Level(SkyPrisonAfpUpgrades.All[i]);
        return TierAtLevel(a, lv);
    }

    /// <summary>离下一档还差几级(已满档返回 -1)。</summary>
    public static int LevelsToNext(AbilityDef a, int level)
    {
        int tier = level / a.levelsPerTier;
        return tier >= a.maxTier ? -1 : (tier + 1) * a.levelsPerTier - level;
    }

    public static string TierName(int tier) => SkyPrisonAfpAbsorbShield.TierName(tier);
}
