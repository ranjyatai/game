using UnityEngine;

/// <summary>
/// AFP 里程碑效果：「回转」(剑) 和「致命子弹」(双枪)——「物理攻击强化」每满 5 级一档(最多 4 档)，各自需要科技树节点 seeker_afp_cycle_N / seeker_afp_lethal_N。
///   · 回转(剑专用)：玩家击杀敌人时，回复最大 LP 的一定比例(I~IV：12% / 18% / 25% / 35%)，大血量敌人(上限 ≥ 250，织面者、BOSS 等) ×3。
///       让玩家敢于冲进敌群：杀得越快，LP 越够，越敢继续冲。可破坏物(箱子)和玩家自己不算。
///   · 致命子弹(双枪)：每第 10 发命中的子弹，物理伤害 +100/150/200/300%。
/// 只对玩家自己生效(敌人没有 AFP)；一局内有效。
/// </summary>
public static class SkyPrisonAfpPhysicalMastery
{
    public const string NodeId = "seeker_afp_phys_mastery";
    public const int LevelsPerTier = 5, MaxTier = 4;
    /// <summary>续战：每杀一只敌人回复的 LP，占最大 LP 的比例。</summary>
    public static readonly float[] KillLpFraction = { 0.12f, 0.18f, 0.25f, 0.35f };
    public const float LargeEnemyMaxHp = 250f;        // 血量上限 ≥ 这个数算「大血量敌人」
    public const float LargeEnemyKillMultiplier = 3f; // 击杀大血量敌人回复的 LP 倍数
    /// <summary>致命子弹：每第 N 发命中的子弹额外加伤(不看弹匣里剩几发，所以靠换弹 / 少开枪凑不出来)。</summary>
    public const int DeadlyEvery = 10;
    public static readonly float[] DeadlyBonus = { 1.00f, 1.50f, 2.00f, 3.00f };
    static int _gunHits;
    /// <summary>计数变化(命中 / 清零)，枪械 HUD 的进度小格订阅它刷新。</summary>
    public static event System.Action OnHitsChanged;
    /// <summary>当前这一轮已经命中了几发(0~9)；第 10 发触发致命子弹后回到 0。</summary>
    public static int HitsIntoCycle => _gunHits % DeadlyEvery;
    public static void ResetCounter() { _gunHits = 0; OnHitsChanged?.Invoke(); }

    static SkyPrisonAfpUpgrades.Def PhysDef()
    {
        for (int i = 0; i < SkyPrisonAfpUpgrades.All.Length; i++)
            if (SkyPrisonAfpUpgrades.All[i].key == "physAtk") return SkyPrisonAfpUpgrades.All[i];
        return null;
    }

    /// <summary>回转(剑)的当前档位。</summary>
    public static int CycleTier
    {
        get
        {
            var a = SkyPrisonAfpAbilityTable.Find("cycle");
            return a == null ? 0 : SkyPrisonAfpAbilityTable.CurrentTier(a);
        }
    }

    /// <summary>致命子弹(双枪)的当前档位。</summary>
    public static int LethalTier
    {
        get
        {
            var a = SkyPrisonAfpAbilityTable.Find("lethal");
            return a == null ? 0 : SkyPrisonAfpAbilityTable.CurrentTier(a);
        }
    }

    public static int LevelsToNextTier(int level)
    {
        int tier = level / LevelsPerTier;
        return tier >= MaxTier ? -1 : (tier + 1) * LevelsPerTier - level;
    }

    static bool IsGun(ItemEquipmentExtension ext) => ext != null && ext.usesAmmo;

    /// <summary>伤害公式里、物理伤害算完抗性之后调用：双枪的「致命子弹」。attacker 不是玩家时什么都不做。</summary>
    public static bool ApplyToHit(UnitBattleStatRuntime attacker, ItemEquipmentExtension weapon, InventoryItemEntry weaponEntry, UnitHealthController target, ref float physicalDamage)
    {
        int tier = LethalTier;
        if (tier <= 0 || attacker == null || physicalDamage <= 0f || !SkyPrisonAfpAnomalyBurst.IsPlayerAttacker(attacker)) return false;
        bool triggered = false;
        if (IsGun(weapon))
        {
            _gunHits++;
            if (_gunHits % DeadlyEvery == 0) { physicalDamage *= 1f + DeadlyBonus[tier - 1]; triggered = true; }
            OnHitsChanged?.Invoke();
        }
        return triggered;
    }

    /// <summary>玩家这一击把敌人打死时调用(伤害公式里，打之前活着、打完死了)：「回转」回 LP。</summary>
    public static void OnKill(UnitBattleStatRuntime attacker, UnitHealthController target, ItemEquipmentExtension weapon)
    {
        int tier = CycleTier;
        if (tier <= 0 || attacker == null || target == null || !SkyPrisonAfpAnomalyBurst.IsPlayerAttacker(attacker)) return;
        if (weapon == null || weapon.weaponCategory != WeaponCategoryType.Sword) return;          // 回转：剑专用，枪械击杀不回 LP
        if (target.GetComponentInParent<SkyPrisonBreakableCrateEffect>() != null) return;       // 箱子之类的可破坏物不算
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null || target.transform == player.transform || target.transform.IsChildOf(player.transform)) return;

        var load = player.GetComponentInChildren<UnitLoadRuntime>(true);
        if (load == null || load.MaxLoad <= 0f) return;
        float amount = load.MaxLoad * KillLpFraction[tier - 1] * (target.MaxHealth >= LargeEnemyMaxHp ? LargeEnemyKillMultiplier : 1f);
        load.SetCurrentLoad(load.CurrentLoad + amount);
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.AbilityCycle);
    }
}
