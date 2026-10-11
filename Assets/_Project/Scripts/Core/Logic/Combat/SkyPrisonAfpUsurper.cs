using UnityEngine;

/// <summary>
/// 探索者科技树能力「篡夺者」(节点 seeker_usurper)——AFP 窗口里的独立升级项(key = usurp)，最高 10 级：
///   · 购买条件：追加生命 ≥ 10 级 且 物理攻击强化 ≥ 10 级(按已购买的等级算)，并且科技树里点亮了这个节点；节点没点亮整行不出现。
///   · 玩家受到的伤害，有一部分变成血条上的红色部分(随时间缓慢消退，由 UnitHealthController 的 regainPool 管)：1 级 30% → 10 级 60%。
///   · 玩家攻击敌人造成的伤害，按转化率变成生命回复，只能回复红色部分：近战 1 级 20% → 10 级 80%，远程是近战的 40%。
///   · 以后装备可以给「额外等级」(BonusLevels)：科技树点亮的前提下，进局就自带这么多级，和 AFP 买的叠加，上限 10。
/// 只对玩家自己生效；可破坏物(箱子)不算「敌人」。
/// </summary>
public static class SkyPrisonAfpUsurper
{
    public const string NodeId = "seeker_usurper";
    public const string UpgradeKey = "usurp";
    public const int MaxLevel = 10, NeedHpLevel = 10, NeedPhysLevel = 10;
    public const float RangedFactor = 0.40f;        // 远程转化率 = 近战 × 这个

    /// <summary>装备给的额外等级(以后接入)。只在科技树点亮时才算。</summary>
    public static int BonusLevels;

    public static bool NodeOn => SkyPrisonAfpAbsorbShield.NodeUnlocked(NodeId);

    static SkyPrisonAfpUpgrades.Def Def()
    {
        for (int i = 0; i < SkyPrisonAfpUpgrades.All.Length; i++)
            if (SkyPrisonAfpUpgrades.All[i].key == UpgradeKey) return SkyPrisonAfpUpgrades.All[i];
        return null;
    }

    /// <summary>AFP 里买到的等级 + 装备额外等级，上限 10；节点没点亮 = 0。</summary>
    public static int EffectiveLevel
    {
        get
        {
            if (!NodeOn) return 0;
            var d = Def();
            int lv = (d != null ? SkyPrisonAfpUpgrades.Level(d) : 0) + Mathf.Max(0, BonusLevels);
            return Mathf.Min(MaxLevel, lv);
        }
    }

    public static bool Active => EffectiveLevel > 0;

    static float T(int level) => Mathf.Clamp01((level - 1) / (float)(MaxLevel - 1));
    /// <summary>受到的伤害里变成红色部分的比例：1 级 30% → 10 级 60%。</summary>
    public static float PoolRatioAt(int level) => level <= 0 ? 0f : Mathf.Lerp(0.30f, 0.60f, T(level));
    /// <summary>近战：造成伤害转为回复的比例：1 级 20% → 10 级 80%。</summary>
    public static float MeleeAt(int level) => level <= 0 ? 0f : Mathf.Lerp(0.20f, 0.80f, T(level));
    public static float RangedAt(int level) => MeleeAt(level) * RangedFactor;

    /// <summary>UnitHealthController.ApplyDamage 里调用：这个单位挨打时，伤害里有多少比例变成红色部分。不是玩家 / 没生效返回 0。</summary>
    public static float PoolRatioFor(UnitHealthController health)
    {
        if (health == null) return 0f;
        int lv = EffectiveLevel;
        if (lv <= 0) return 0f;
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) return 0f;
        var t = health.transform;
        return (t == player.transform || t.IsChildOf(player.transform) || player.transform.IsChildOf(t)) ? PoolRatioAt(lv) : 0f;
    }

    /// <summary>伤害公式里、玩家这一击对 target 结算完之后调用。dealt = 这一击实际造成的伤害(物理 + 属性)。</summary>
    public static void OnDealDamage(UnitBattleStatRuntime attacker, ItemEquipmentExtension weapon, UnitHealthController target, float dealt)
    {
        int lv = EffectiveLevel;
        if (dealt <= 0f || lv <= 0 || attacker == null || target == null || !SkyPrisonAfpAnomalyBurst.IsPlayerAttacker(attacker)) return;
        if (target.GetComponentInParent<SkyPrisonBreakableCrateEffect>() != null) return;
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null || target.transform == player.transform || target.transform.IsChildOf(player.transform)) return;

        var health = player.GetComponentInChildren<UnitHealthController>(true);
        if (health == null) return;
        bool ranged = weapon != null && weapon.usesAmmo;
        float got = health.ApplyRegain(dealt * (ranged ? RangedAt(lv) : MeleeAt(lv)));
        if (got > 0.01f && Time.unscaledTime - _lastSe > 1.5f)
        {
            _lastSe = Time.unscaledTime;
            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.AbilityUsurper);
        }
    }

    static float _lastSe = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { BonusLevels = 0; _driverObj = null; }

    // ── HUD 状态「篡夺者」：生效时挂在玩家身上，层数 = 当前等级；等级清零 / 节点未点亮时撤掉 ──
    // 状态定义是 Resources/AFP/Status/afp_usurper(statusId = status_usurp)，图标是 255×256 的官方状态图标尺寸。
    const string StatusAssetPath = "AFP/Status/afp_usurper";
    static GameObject _driverObj;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void InstallDriver()
    {
        if (_driverObj != null) return;
        _driverObj = new GameObject("[AfpUsurperDriver]") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(_driverObj);
        _driverObj.AddComponent<Driver>();
    }

    sealed class Driver : MonoBehaviour
    {
        StatusDefinition _def;
        int _shown;
        float _next;
        UnitStatusController _sc;

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.3f;
            var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            if (player == null) { _sc = null; _shown = 0; return; }
            if (_sc == null) _sc = player.GetComponentInChildren<UnitStatusController>(true);
            if (_sc == null) return;
            if (_def == null) _def = Resources.Load<StatusDefinition>(StatusAssetPath);
            if (_def == null) return;

            int lv = EffectiveLevel;
            if (lv <= 0)
            {
                if (_sc.HasStatus(_def.statusId)) _sc.RemoveStatus(_def.statusId);
                _shown = 0;
                return;
            }
            if (!_sc.HasStatus(_def.statusId) || lv != _shown)
            {
                _sc.ApplyStatus(_def, player.gameObject, lv);
                _shown = lv;
            }
        }
    }
}
