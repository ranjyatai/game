using UnityEngine;

/// <summary>
/// 负重等级控制器。挂在玩家 GameObject 上（UnitDefinitionRuntimeApplier 自动添加）。
///
/// 负重分级：
///   轻量 Light   ：装备总重 ≤ 轻载上限   → LP消耗倍率 ×0.8，移速无惩罚
///   正常 Normal  ：轻载上限 < 重量 ≤ 中载上限 → ×1.0
///   超重 Heavy   ：中载上限 < 重量 ≤ 重载上限 → ×1.3，移速 ×0.85
///   过载 Overload：重量 > 重载上限          → ×1.8，移速 ×0.70，无法奔跑
///
/// 阈值由 UnitDefinition 参数驱动（weightLight/weightHeavy/weightOverload），
/// 未配置时使用内置默认值。
/// </summary>
[DisallowMultipleComponent]
public sealed class EquipmentWeightController : MonoBehaviour
{
    public enum WeightTier { Light, Normal, Heavy, Overload }

    // 阈值（石英，游戏里的重量单位，和背包重量条同一个单位），可由 UnitBattleStatRuntime 覆盖
    [SerializeField] private float lightThreshold    = 10f;
    [SerializeField] private float heavyThreshold    = 25f;
    [SerializeField] private float overloadThreshold = 40f;

    public WeightTier CurrentTier { get; private set; } = WeightTier.Normal;
    public float      TotalEquipWeight { get; private set; }

    private UnitLoadRuntime _loadRuntime;

    private void Awake()
    {
        _loadRuntime = GetComponent<UnitLoadRuntime>();
    }

    private void OnEnable()
    {
        EquipmentRuntime.OnEquipped   += HandleEquipmentChanged;
        EquipmentRuntime.OnUnequipped += HandleEquipmentChanged;

        // 存档读档/场景加载时，玩家可能已经带着装备出场（不是本次运行时 Equip() 触发的），
        // 这里不初始算一遍的话 TotalEquipWeight 会一直停在 0，直到玩家在这局里手动
        // 卸装/装备一次东西才会第一次被算进去——这正是"装备的武器防具没算进负重"的根因。
        Rebuild();
    }

    private void OnDisable()
    {
        EquipmentRuntime.OnEquipped   -= HandleEquipmentChanged;
        EquipmentRuntime.OnUnequipped -= HandleEquipmentChanged;
    }

    private void HandleEquipmentChanged(EquipmentSlotType _, InventoryItemEntry __)
        => Rebuild();

    public void Rebuild()
    {
        var eq = EquipmentRuntime.Instance;
        TotalEquipWeight = 0f;
        if (eq != null)
        {
            // 主/副武器槽都要算——之前只读 GetWeapon()（武器一槽），武器装到副武器槽
            // 时完全没被计入负重，等于这把武器凭空没有重量。
            var weapon = eq.GetWeapon();
            if (weapon?.definition != null)
                TotalEquipWeight += weapon.definition.weightOrQuartz;
            var weaponSecondary = eq.GetWeaponSecondary();
            if (weaponSecondary?.definition != null)
                TotalEquipWeight += weaponSecondary.definition.weightOrQuartz;
            foreach (var armor in eq.GetAllArmor())
                if (armor?.definition != null)
                    TotalEquipWeight += armor.definition.weightOrQuartz;
        }

        var prev = CurrentTier;
        if      (TotalEquipWeight <= lightThreshold)    CurrentTier = WeightTier.Light;
        else if (TotalEquipWeight <= heavyThreshold)    CurrentTier = WeightTier.Normal;
        else if (TotalEquipWeight <= overloadThreshold) CurrentTier = WeightTier.Heavy;
        else                                            CurrentTier = WeightTier.Overload;

        if (CurrentTier != prev) ApplyTierEffects();
    }

    private void ApplyTierEffects()
    {
        if (_loadRuntime == null) return;

        // LP 消耗倍率通过 ApplyLoadDefinition 的 multiplier 接口覆盖
        // 移速/奔跑限制通过 StatusController 或 UnitMovementController 扩展
        // 此处只记录状态，由 HUD / 移动控制器主动查询 CurrentTier
        Debug.Log($"[EquipmentWeightController] 负重等级变更 → {CurrentTier}，总重 {TotalEquipWeight:F1}kg");
    }

    /// <summary>获取当前负重等级对应的 LP 消耗倍率。</summary>
    public float GetLpCostMultiplier() => CurrentTier switch
    {
        WeightTier.Light    => 0.8f,
        WeightTier.Normal   => 1.0f,
        WeightTier.Heavy    => 1.3f,
        WeightTier.Overload => 1.8f,
        _                   => 1.0f,
    };

    /// <summary>获取当前负重等级对应的移速倍率。</summary>
    public float GetMoveSpeedMultiplier() => CurrentTier switch
    {
        WeightTier.Light    => 1.0f,
        WeightTier.Normal   => 1.0f,
        WeightTier.Heavy    => 0.85f,
        WeightTier.Overload => 0.70f,
        _                   => 1.0f,
    };

    /// <summary>
    /// 重装兵器线的额外硬直 / 击退减免（0~1，科技树节点写入）。0 = 没有。
    /// 档位自带的基础减免：超重 -15%，过载 -30%；科技树再叠加（相乘），上限 90%。
    /// 只看装备总重（石英），不看背包——塞满背包不会触发。移动惩罚仍由 UnitMovementController 的背负系统负责，这里不重复处理。
    /// </summary>
    public float TechStaggerResist { get; set; }

    // 战略家树「重装兵器 I~IV」：节点 id = heavy_arms_1..4，等级读存档。I/II/III 额外 -15/-30/-45/-60%。不会减到零：硬直和击退至少保留 25%。
    public const string HeavyArmsNodePrefix = "strategist_heavy_arms_";
    private static readonly float[] HeavyArmsResist = { 0f, 0.15f, 0.30f, 0.45f, 0.60f };

    private int HeavyArmsTier()
    {
        int t = 0;
        for (int i = 1; i <= 4; i++)
            if (TechTreeRuntime.SavedLevel(HeavyArmsNodePrefix + i) >= 1) t = i;
        return t;
    }

    private float TierBaseStagger => CurrentTier == WeightTier.Overload ? 0.70f : CurrentTier == WeightTier.Heavy ? 0.85f : 1f;
    private bool AtHeavyOrAbove => CurrentTier == WeightTier.Heavy || CurrentTier == WeightTier.Overload;

    private float TechResistNow()
    {
        float r = TechStaggerResist;
        if (AtHeavyOrAbove)                      // 科技树加成只在重装档以上生效
            r = Mathf.Max(r, HeavyArmsResist[HeavyArmsTier()]);
        return Mathf.Clamp01(r);
    }

    private const float StaggerFloor = 0.25f;   // 再重的装备也不会完全免疫：硬直 / 击退至少保留 25%

    /// <summary>硬直时间倍率。</summary>
    public float StunMultiplier => Mathf.Clamp(TierBaseStagger * (1f - TechResistNow()), StaggerFloor, 1f);

    /// <summary>击退力倍率。</summary>
    public float KnockbackMultiplier => Mathf.Clamp(TierBaseStagger * (1f - TechResistNow()), StaggerFloor, 1f);

    /// <summary>过载时禁止奔跑。</summary>
    public bool CanSprint => CurrentTier != WeightTier.Overload;

    /// <summary>由编辑器或科技树调整阈值后调用。</summary>
    public void SetThresholds(float light, float heavy, float overload)
    {
        lightThreshold    = light;
        heavyThreshold    = heavy;
        overloadThreshold = overload;
        Rebuild();
    }
}
