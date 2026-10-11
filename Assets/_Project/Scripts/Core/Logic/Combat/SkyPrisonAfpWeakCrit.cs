using UnityEngine;

/// <summary>
/// AFP 词条「弱点特效」「暴击特效」(用户 10-12 定)，各自需要探索者研修节点点亮才出现在 AFP 购买区：
///   · 弱点特效(3 级，600/2400/7000)：这一击的伤害类型是目标的弱点(抗性为负)时，暴击率 +15/30/50%，失衡累积(低级怪是击退与硬直) ×1.2/1.35/1.5；
///       Lv3 质变「破甲冲击」：弱点暴击的失衡累积再 ×3，而且失衡条满时无视霸体直接打断(不再用固定冷却，靠失衡条的免疫期和阈值递增限制)。
///   · 暴击特效(3 级，800/3000/8000)：暴击伤害 +20/40/70 个百分点(150% → 170/190/220%)；
///       Lv3 质变「会心回收」：枪械暴击 40% 概率退回这发子弹；大剑暴击回复最大 LP 的 4%。
/// 玩家基础暴击率是 0，所以两条是一对：弱点特效给暴击率，暴击特效放大暴击。只对玩家自己生效，一局内有效。
/// </summary>
public static class SkyPrisonAfpWeakCrit
{
    public const string WeakKey = "weakExploit", CritKey = "critBoost";
    public const string WeakNodeId = "seeker_afp_weak", CritNodeId = "seeker_afp_crit";

    public static readonly float[] WeakCritRate = { 0f, 15f, 30f, 50f };          // 百分比
    public static readonly float[] WeakImpactMult = { 1f, 1.2f, 1.35f, 1.5f };
    public static readonly float[] CritDamageBonus = { 0f, 20f, 40f, 70f };       // 暴击伤害百分点
    public const int MaxLevel = 3;
    public const float WeakCritStaggerMultiplier = 3f;
    public const float GunRefundChance = 0.4f;
    public const float SwordCritLpFraction = 0.04f;

    public static int WeakLevel => Mathf.Clamp(SkyPrisonAfpUpgrades.LevelByKey(WeakKey), 0, MaxLevel);
    public static int CritLevel => Mathf.Clamp(SkyPrisonAfpUpgrades.LevelByKey(CritKey), 0, MaxLevel);

    /// <summary>这一击打没打中弱点：物理类型或任意一条有伤害的属性，在目标身上的抗性为负。</summary>
    public static bool IsWeakHit(UnitBattleStatRuntime target, SkillDefinition skill)
    {
        if (target == null || skill == null) return false;
        if (skill.damageMultiplier > 0f && target.GetFinalValue(UnitActionModuleRuntime.PhysicalResistKey(skill.damageTypeKey), 0f) < 0f) return true;
        if (skill.attributeHits != null)
            foreach (var h in skill.attributeHits)
                if (h != null && h.attributeDamageMultiplier > 0f && !string.IsNullOrWhiteSpace(h.attributeKey)
                    && target.GetFinalValue(h.attributeKey + "Resist", 0f) < 0f) return true;
        return false;
    }

    public static bool IsWeakKey(UnitBattleStatRuntime target, string resistKey) => target != null && target.GetFinalValue(resistKey, 0f) < 0f;

    static bool IsPlayer(UnitBattleStatRuntime attacker) => attacker != null && SkyPrisonAfpAnomalyBurst.IsPlayerAttacker(attacker);

    /// <summary>额外暴击率(0~1)。</summary>
    public static float BonusCritRate(UnitBattleStatRuntime attacker, bool weakHit)
        => weakHit && IsPlayer(attacker) ? WeakCritRate[WeakLevel] / 100f : 0f;

    /// <summary>额外暴击伤害倍率(0.2 = +20 个百分点)。</summary>
    public static float BonusCritDamage(UnitBattleStatRuntime attacker)
        => IsPlayer(attacker) ? CritDamageBonus[CritLevel] / 100f : 0f;

    /// <summary>打弱点时的冲击倍率：中高级怪乘在失衡累积上，低级怪乘在击退 / 硬直上。</summary>
    public static float ImpactMultiplier(UnitBattleStatRuntime attacker, bool weakHit)
        => weakHit && IsPlayer(attacker) ? WeakImpactMult[WeakLevel] : 1f;

    /// <summary>Lv3 破甲冲击是否生效(这一击是弱点暴击、且弱点特效满级)。</summary>
    public static bool ArmorBreak(UnitBattleStatRuntime attacker, bool weakHit, bool positiveCrit)
        => weakHit && positiveCrit && WeakLevel >= MaxLevel && IsPlayer(attacker);

    /// <summary>Lv3 会心回收：玩家打出暴击后调用。</summary>
    public static void OnCrit(UnitBattleStatRuntime attacker, ItemEquipmentExtension weapon, InventoryItemEntry weaponEntry)
    {
        if (CritLevel < MaxLevel || !IsPlayer(attacker) || weapon == null) return;
        if (weapon.usesAmmo)
        {
            if (weaponEntry == null || Random.value >= GunRefundChance) return;
            weaponEntry.currentMagazineAmmo += 1;          // 退回刚打出去的这一发
            UnitActionModuleRuntime.RaiseWeaponAmmoChanged();
        }
        else if (weapon.weaponCategory == WeaponCategoryType.Sword)
        {
            var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            var load = player != null ? player.GetComponentInChildren<UnitLoadRuntime>(true) : null;
            if (load == null || load.MaxLoad <= 0f) return;
            load.SetCurrentLoad(load.CurrentLoad + load.MaxLoad * SwordCritLpFraction);
        }
    }

    public static void ResetSession() { }
}
