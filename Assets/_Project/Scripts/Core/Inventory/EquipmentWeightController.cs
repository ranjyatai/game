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

    // 阈值（kg），可由 UnitBattleStatRuntime 覆盖
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
