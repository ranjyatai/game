using UnityEngine;

/// <summary>
/// AFP 里程碑效果：「异常追伤」。
/// 「属性攻击强化」点到 5 级，且在 Seeker 科技树里点亮了对应节点后生效：玩家的攻击让敌人的某种属性异常累计满值、
/// 异常状态真正赋予的那一刻，额外打出一笔同属性的追伤。每 5 级一档(最多 4 档)，追伤 = 玩家当前攻击力 × 倍率(I~IV：3.0 / 4.5 / 6.0 / 8.0)；另外处于异常状态中的敌人受到玩家的伤害每档 +5%(异常增伤)。
/// 追伤只吃目标对应属性的抗性(属性伤害本来就不走防御)，不进暴击，且本身不再累计异常，避免自循环。
/// 双属性武器：两种属性各自累计、各自触发，各打一笔。
/// 由 UnitAnomalyController.TryTriggerFullAnomaly 在异常赋予成功(没被特效力场豁免)后调用 OnAnomalyTriggered。
/// 一局内有效：AFP 等级清零后档位变 0。
/// </summary>
public static class SkyPrisonAfpAnomalyBurst
{
    public const string NodeId = "seeker_afp_attr_burst";
    public const int LevelsPerTier = 5, MaxTier = 4;
    public static readonly float[] TierMultiplier = { 3.0f, 4.5f, 6.0f, 8.0f };
    public const float VulnerabilityPerTier = 0.05f;      // 异常增伤：处于异常中的敌人，受到玩家的伤害每档 +5%

    static SkyPrisonAfpUpgrades.Def AttrDef()
    {
        for (int i = 0; i < SkyPrisonAfpUpgrades.All.Length; i++)
            if (SkyPrisonAfpUpgrades.All[i].key == "attrAtk") return SkyPrisonAfpUpgrades.All[i];
        return null;
    }

    /// <summary>当前档位(0 = 没有)：科技树节点已点亮 && 属性攻击强化每满 5 级一档。</summary>
    public static int Tier
    {
        get
        {
            var a = SkyPrisonAfpAbilityTable.Find("burst");
            return a == null ? 0 : SkyPrisonAfpAbilityTable.CurrentTier(a);
        }
    }

    public static float MultiplierForTier(int tier) => tier <= 0 ? 0f : TierMultiplier[Mathf.Min(tier, MaxTier) - 1];

    /// <summary>某个属性攻击强化等级离下一档还差几级(已最高档返回 -1)。</summary>
    public static int LevelsToNextTier(int level)
    {
        int tier = level / LevelsPerTier;
        return tier >= MaxTier ? -1 : (tier + 1) * LevelsPerTier - level;
    }

    static readonly string[] AnomalyStatusIds = { "status_burn", "status_electric", "status_corruption", "status_iced" };

    /// <summary>攻击者是不是玩家自己(AFP 强化只对玩家生效)。</summary>
    public static bool IsPlayerAttacker(UnitBattleStatRuntime attacker)
    {
        if (attacker == null) return false;
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        return player != null && (attacker.transform == player.transform || attacker.transform.IsChildOf(player.transform) || player.transform.IsChildOf(attacker.transform));
    }

    /// <summary>异常增伤：目标身上有任何一种异常状态(灼烧/触电/腐蚀/冻结)时，玩家对它的伤害 ×(1 + 档位×5%)。</summary>
    public static float VulnerabilityMultiplier(UnitBattleStatRuntime attacker, UnitHealthController target)
    {
        int tier = Tier;
        if (tier <= 0 || target == null || !IsPlayerAttacker(attacker)) return 1f;
        var sc = target.GetComponent<UnitStatusController>() ?? target.GetComponentInParent<UnitStatusController>() ?? target.GetComponentInChildren<UnitStatusController>(true);
        if (sc == null) return 1f;
        for (int i = 0; i < AnomalyStatusIds.Length; i++)
            if (sc.HasStatus(AnomalyStatusIds[i])) return 1f + VulnerabilityPerTier * tier;
        return 1f;
    }

    /// <summary>异常赋予成功时调用。source = 造成这次累计的攻击者；只有玩家造成的才追伤。</summary>
    public static void OnAnomalyTriggered(string anomalyKey, GameObject source, UnitAnomalyController target)
    {
        int tier = Tier;
        if (tier <= 0 || target == null || source == null || string.IsNullOrEmpty(anomalyKey)) return;

        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) return;
        var st = source.transform;
        if (!(st == player.transform || st.IsChildOf(player.transform) || player.transform.IsChildOf(st))) return;

        var attackerStats = player.GetComponentInChildren<UnitBattleStatRuntime>(true);
        if (attackerStats == null) return;

        var health = target.GetComponentInChildren<UnitHealthController>(true) ?? target.GetComponentInParent<UnitHealthController>();
        if (health == null || health.IsDead) return;
        var targetStats = target.GetComponentInChildren<UnitBattleStatRuntime>(true) ?? target.GetComponentInParent<UnitBattleStatRuntime>();

        float dmg = attackerStats.GetFinalValue("atk", 10f) * MultiplierForTier(tier);
        dmg *= UnitActionModuleRuntime.ResolveResistMultiplier(targetStats, anomalyKey + "Resist");
        dmg *= SkyPrisonAfpUpgrades.AttackMultiplier(attackerStats, false);       // 属性攻击强化本身的百分比也算上
        if (dmg <= 0f) return;

        var bridge = health.GetComponent<UnitOverheadHealthBridge>() ?? health.GetComponentInChildren<UnitOverheadHealthBridge>(true);
        bridge?.ShowDamageNumberByKey(anomalyKey, dmg, false, false, false);
        health.ApplyDamage(dmg);          // 不走异常累计：追伤不会再触发异常
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.AbilityBurst);
    }
}
