using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 监听 EquipmentRuntime 的装备/卸装事件，
/// 把当前所有装备（基础属性 + 已安装组件词条）汇总为 StatModifier 列表，
/// 注入玩家的 UnitBattleStatRuntime。
/// 挂在玩家 GameObject 上，或由 UnitDefinitionRuntimeApplier 自动添加。
/// </summary>
[DisallowMultipleComponent]
public sealed class EquipmentStatApplier : MonoBehaviour
{
    private const string SourceTag = "equipment";

    private UnitBattleStatRuntime _statRuntime;

    private void Awake()
    {
        _statRuntime = GetComponent<UnitBattleStatRuntime>();
    }

    private void OnEnable()
    {
        EquipmentRuntime.OnEquipped   += HandleEquipmentChanged;
        EquipmentRuntime.OnUnequipped += HandleEquipmentChanged;
        // 滚轮切换主/副武器时，即使两个槽的装备都没变，属性加成也要跟着切换到
        // "现在这把手上真的在用的武器"上——之前没订阅这个事件，切换武器后属性面板
        // 完全不会刷新，还停在上一把武器的加成上。
        EquipmentRuntime.OnActiveWeaponChanged += HandleActiveWeaponChanged;
        // 装备耐久磨到0（报废）那一刻要立刻失去属性加成，不能等下次装备/卸装/切换
        // 武器才刷新——不订阅这个事件的话，武器在战斗中被打到报废，加成会一直留着
        // 直到玩家碰巧做了别的装备操作才会消失。
        DurabilitySystem.OnDurabilityChanged += HandleDurabilityChanged;
    }

    private void OnDisable()
    {
        EquipmentRuntime.OnEquipped   -= HandleEquipmentChanged;
        EquipmentRuntime.OnUnequipped -= HandleEquipmentChanged;
        EquipmentRuntime.OnActiveWeaponChanged -= HandleActiveWeaponChanged;
        DurabilitySystem.OnDurabilityChanged -= HandleDurabilityChanged;
    }

    private void HandleEquipmentChanged(EquipmentSlotType _, InventoryItemEntry __)
        => Rebuild();

    private void HandleActiveWeaponChanged(EquipmentSlotType _)
        => Rebuild();

    private void HandleDurabilityChanged(InventoryItemEntry entry, int oldDurability, int newDurability)
        => Rebuild();

    private void Rebuild()
    {
        // Awake() 里第一次取的时候，UnitBattleStatRuntime 有可能还没被
        // UnitStatusRuntimeBootstrap（另一个脚本）挂上去——Unity 不保证同一个
        // GameObject 上多个脚本 Awake() 的先后顺序，这个组件本身又是
        // UnitDefinitionRuntimeApplier 运行时动态 AddComponent 出来的，AddComponent
        // 会立刻同步触发 Awake()，时机比脚本执行顺序设置更难保证。之前只在 Awake()
        // 里取一次、取到 null 就永远是 null，导致装备属性加成静默不生效（面板一直
        // 停在基础值），这里改成取到 null 时随时可以重新尝试拿一次。
        if (_statRuntime == null)
            _statRuntime = GetComponent<UnitBattleStatRuntime>();
        if (_statRuntime == null) return;
        var eq = EquipmentRuntime.Instance;
        if (eq == null) return;

        var mods = new List<StatModifier>();

        // 武器——之前这里硬编码读 eq.GetWeapon()（永远只认武器一槽），武器装到
        // 副武器槽、或者滚轮切成生效的是副武器时，属性加成完全不会生效。武器属性
        // 加成应该只算"当前手上真的在用的那把"（ActiveWeaponSlot 指向的槽），
        // 不管它physically在武器一还是武器二，也不是两把一起算（同一时刻只有一把
        // 真正在用）。
        var weapon = eq.GetEquipped(eq.ActiveWeaponSlot);
        if (weapon?.definition?.equipment != null)
            CollectEquipmentMods(weapon, mods);

        // 防具
        foreach (var armor in eq.GetAllArmor())
            if (armor?.definition?.equipment != null)
                CollectEquipmentMods(armor, mods);

        _statRuntime.SetExternalModifiers(SourceTag, mods);
    }

    // internal（不是private）——CharacterPanelController 的装备对比预览要复用这套"收集
    // 一件装备的StatModifier"逻辑，不能另外写一份口径可能对不上的近似算法。
    internal static void CollectEquipmentMods(InventoryItemEntry entry, List<StatModifier> mods)
    {
        // 耐久磨到0（报废）的装备失去全部属性加成和组件词条——一件破损到用不了的
        // 武器/防具不该还在暗中提供满额的属性加成，这也是耐久系统本身存在的意义
        // （不然耐久就只是个纯数字展示，没有实际影响）。
        if (DurabilitySystem.IsDestroyed(entry))
            return;

        var eq = entry.definition.equipment;

        // 装备基础属性加成——跟角色的 parameterValues 共用同一份核心属性字典
        // （BattleParameterDatabase.coreAttributes），key 对得上运行时 StatModifier
        // 认的 statKey，不用为装备另外维护一套属性名。
        if (eq.statBonuses != null)
        {
            foreach (var bonus in eq.statBonuses)
                if (bonus != null && !string.IsNullOrEmpty(bonus.parameterKey) && bonus.value != 0f)
                    mods.Add(new StatModifier(bonus.parameterKey, bonus.value));
        }

        // 已安装的组件词条
        foreach (var mod in entry.installedMods)
        {
            if (!mod.isIdentified) continue; // 未鉴定的组件词条不生效
            foreach (var bonus in mod.bonuses)
                if (!string.IsNullOrEmpty(bonus.statKey))
                    mods.Add(new StatModifier(bonus.statKey, bonus.value));
        }
    }
}
