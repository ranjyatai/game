using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家装备槽运行时。单例，DontDestroyOnLoad。
/// 持有各槽位当前装备的 InventoryItemEntry，供战斗/耐久系统读取。
/// 装备/卸下由背包 UI 调用 Equip / Unequip。
/// </summary>
public class EquipmentRuntime : MonoBehaviour
{
    public static EquipmentRuntime Instance { get; private set; }

    // ── 事件 ──────────────────────────────────────────────────────────────
    public static event Action<EquipmentSlotType, InventoryItemEntry> OnEquipped;
    public static event Action<EquipmentSlotType, InventoryItemEntry> OnUnequipped;
    /// <summary>当前生效（正在战斗里用的）武器槽切换了——鼠标滚轮切武器、或者当前生效槽
    /// 被卸下导致自动切到另一把时触发。</summary>
    public static event Action<EquipmentSlotType> OnActiveWeaponChanged;

    // ── 装备槽 ────────────────────────────────────────────────────────────
    private readonly Dictionary<EquipmentSlotType, InventoryItemEntry> _slots
        = new Dictionary<EquipmentSlotType, InventoryItemEntry>();

    /// <summary>两把武器里，哪一把是当前真正在战斗里生效的——两个槽都能装备物品，
    /// 但战斗模组同一时间只认一把，滚轮切换的就是这个。</summary>
    public EquipmentSlotType ActiveWeaponSlot { get; private set; } = EquipmentSlotType.Weapon;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // EquipmentRuntime is explicitly the PLAYER's equipment runtime (see class doc), but it's a
    // DontDestroyOnLoad singleton so it never sits under the player's own GameObject hierarchy -
    // GetComponentInParent/Children on this transform can't reach it. FindObjectOfType<T>() with
    // no scoping used to grab whichever unit's UnitActionModuleRuntime Unity happened to return
    // first, including enemies, silently equipping weapons onto the wrong unit. Resolve by the
    // "Player" tag instead (same pattern already used in CharacterPresenceFeature.cs) and cache
    // it - the player unit doesn't get recreated mid-scene.
    private UnitActionModuleRuntime _playerActionModule;

    private UnitActionModuleRuntime ResolvePlayerActionModule()
    {
        if (_playerActionModule != null)
            return _playerActionModule;

        GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
            _playerActionModule = playerGO.GetComponentInChildren<UnitActionModuleRuntime>(true);

        return _playerActionModule;
    }

    // ── 公开接口 ──────────────────────────────────────────────────────────

    /// <summary>装备一个物品到对应槽位（槽位取自 entry.definition.equipment.slot 固定值）。
    /// 若槽位已有物品则先卸下。</summary>
    public void Equip(InventoryItemEntry entry)
    {
        if (entry?.definition?.equipment == null) return;
        Equip(entry, entry.definition.equipment.slot);
    }

    /// <summary>装备到指定槽位——给 TryEquipFromInventory 的 targetSlotOverride 用，
    /// 不能沿用上面那个只认 entry.definition.equipment.slot 固定值的重载，否则武器
    /// 永远只会装进配置表写死的那个槽，没法把同一款武器分别塞进主/副武器槽。</summary>
    public void Equip(InventoryItemEntry entry, EquipmentSlotType slot)
    {
        if (entry?.definition?.equipment == null) return;

        bool isWeaponSlot = slot == EquipmentSlotType.Weapon || slot == EquipmentSlotType.WeaponSecondary;

        // 之前这里有一段"当前生效槽是空的就自动把新装的这把提升成生效"的逻辑——
        // 本意是解决"只装了一把武器但没生效"的问题，但矫枉过正：玩家哪怕是主动
        // 切成空手在用（比如就想赤手空拳），只要往另一个槽随手塞了个东西，武器就
        // 会被强行切到手上，跟界面（HUD还显示着空手）对不上。装备到某个槽永远不
        // 应该自己改变"当前生效的是哪把"，那只能由玩家手动切换（滚轮/按键，见
        // CycleActiveWeapon）决定。

        if (_slots.TryGetValue(slot, out var old) && old != null)
            Unequip(slot);

        _slots[slot] = entry;
        OnEquipped?.Invoke(slot, entry);

        // 只有装到"当前生效"的那把武器槽才通知战斗模组——两把武器都能装备物品，
        // 但同一时刻只有 ActiveWeaponSlot 指向的那把真正参与战斗。装非生效槽（比如
        // 刚换上副武器但目前用的还是主武器）不应该打断当前正在用的武器。
        if (isWeaponSlot && slot == ActiveWeaponSlot)
        {
            var module = ResolvePlayerActionModule();
            module?.EquipWeapon(entry.definition.equipment);
        }
    }

    /// <summary>卸下指定槽位的装备。</summary>
    public void Unequip(EquipmentSlotType slot)
    {
        if (!_slots.TryGetValue(slot, out var entry) || entry == null) return;
        _slots[slot] = null;
        OnUnequipped?.Invoke(slot, entry);

        if (slot == EquipmentSlotType.Weapon || slot == EquipmentSlotType.WeaponSecondary)
        {
            if (slot == ActiveWeaponSlot)
            {
                // 卸下的正好是当前生效那把——如果另一把有装备，自动切过去接着用；
                // 都没有了才真正回到空手。
                var otherSlot = slot == EquipmentSlotType.Weapon ? EquipmentSlotType.WeaponSecondary : EquipmentSlotType.Weapon;
                var otherEntry = GetEquipped(otherSlot);
                var module = ResolvePlayerActionModule();
                if (otherEntry != null)
                {
                    ActiveWeaponSlot = otherSlot;
                    OnActiveWeaponChanged?.Invoke(ActiveWeaponSlot);
                    module?.EquipWeapon(otherEntry.definition.equipment);
                }
                else
                {
                    module?.UnequipWeapon();
                }
            }
        }
    }

    /// <summary>鼠标滚轮切武器——在主/副武器槽之间切换"当前生效"的那一把。目标槽没
    /// 装备也允许切过去，代表"切到空手"（近战武器只有一把、或者干脆没武器的时候，
    /// 玩家也应该能滚轮切回徒手状态，不是非得两个槽都有武器才能切）。</summary>
    public void CycleActiveWeapon()
    {
        var targetSlot = ActiveWeaponSlot == EquipmentSlotType.Weapon
            ? EquipmentSlotType.WeaponSecondary
            : EquipmentSlotType.Weapon;

        var targetEntry = GetEquipped(targetSlot);

        ActiveWeaponSlot = targetSlot;
        OnActiveWeaponChanged?.Invoke(ActiveWeaponSlot);

        var module = ResolvePlayerActionModule();
        if (targetEntry != null)
            module?.EquipWeapon(targetEntry.definition.equipment);
        else
            module?.UnequipWeapon(); // 目标槽是空的——切到徒手
    }

    /// <summary>清空所有装备槽——只在新游戏/读档前用来重置状态，不走 Unequip() 的
    /// "自动切换到另一把武器"联动（清空阶段没有"另一把"这个概念）。存在的意义：
    /// EquipmentRuntime 是 DontDestroyOnLoad 单例，读档/新游戏本身不会自动清掉上一局
    /// 残留在内存里的装备数据，不显式清一次的话，读档还原的装备会跟上一局的叠在一起。</summary>
    public void ClearAllSlotsForNewSession()
    {
        foreach (EquipmentSlotType slot in Enum.GetValues(typeof(EquipmentSlotType)))
        {
            if (_slots.TryGetValue(slot, out var entry) && entry != null)
            {
                _slots[slot] = null;
                OnUnequipped?.Invoke(slot, entry);
            }
        }

        ActiveWeaponSlot = EquipmentSlotType.Weapon;
        ResolvePlayerActionModule()?.UnequipWeapon();
    }

    /// <summary>读档还原专用——直接设置生效武器槽，不做自动切换/通知战斗模组那套联动
    /// （模组通知交给随后对这个槽位调用的 Equip() 负责，见 InventorySaveConnector）。</summary>
    public void SetActiveWeaponSlotForLoad(EquipmentSlotType slot)
    {
        if (slot == EquipmentSlotType.Weapon || slot == EquipmentSlotType.WeaponSecondary)
            ActiveWeaponSlot = slot;
    }

    /// <summary>获取指定槽位当前装备的 entry，未装备返回 null。</summary>
    public InventoryItemEntry GetEquipped(EquipmentSlotType slot)
    {
        _slots.TryGetValue(slot, out var entry);
        return entry;
    }

    /// <summary>获取当前装备的武器 entry，未装备返回 null。</summary>
    public InventoryItemEntry GetWeapon()
        => GetEquipped(EquipmentSlotType.Weapon);

    /// <summary>获取所有护甲槽的 entry（排除武器和副武器）。</summary>
    public IEnumerable<InventoryItemEntry> GetAllArmor()
    {
        foreach (var kv in _slots)
        {
            if (kv.Key != EquipmentSlotType.Weapon && kv.Key != EquipmentSlotType.WeaponSecondary && kv.Value != null)
                yield return kv.Value;
        }
    }

    /// <summary>获取当前装备的副武器 entry，未装备返回 null。</summary>
    public InventoryItemEntry GetWeaponSecondary()
        => GetEquipped(EquipmentSlotType.WeaponSecondary);

    /// <summary>指定槽位是否已装备。</summary>
    public bool IsEquipped(EquipmentSlotType slot)
        => _slots.TryGetValue(slot, out var e) && e != null;

    // ── 背包 <-> 装备槽 互转（原本各写一份在 SkyPrisonInventoryInteraction 里，
    //    角色面板要做同样的事，抽到这里两边共用一份）─────────────────────────

    /// <summary>
    /// 把背包里第 index 个物品装备到对应槽位；若该槽已有装备会先还回背包，背包满则
    /// 整体失败（不装备、不卸旧的）。返回是否成功。
    /// <paramref name="targetSlotOverride"/>：从角色面板点"副武器"槽呼出背包装备时传入
    /// EquipmentSlotType.WeaponSecondary——武器道具的 equipment.slot 字段是写死在物品
    /// 配置表里的固定值（比如铲子固定是 Weapon），同一款武器数据上根本不存在"主/副"
    /// 的区分，不传这个覆盖参数的话，任何武器不管从哪个槽呼出背包装备，永远只会被
    /// 塞进 equipment.slot 写死的那个槽——想把两把同款武器分别装到主/副武器槽（或者
    /// 把随便哪把武器指定当副武器用）根本做不到。只允许在"武器换武器槽位"之间覆盖
    /// （Weapon⇄WeaponSecondary），护甲槽没有这个"选哪个槽"的场景，不允许覆盖。
    /// </summary>
    public bool TryEquipFromInventory(InventoryRuntime inv, InventoryItemEntry entry, EquipmentSlotType? targetSlotOverride = null)
    {
        if (inv == null || entry?.definition == null || !entry.definition.IsEquipmentItem)
        {
            Debug.LogWarning($"[EquipmentRuntime] TryEquipFromInventory 拒绝：inv={inv != null}, " +
                $"entry.definition={entry?.definition != null}, IsEquipmentItem={entry?.definition?.IsEquipmentItem}");
            return false;
        }

        var definitionSlot = entry.definition.equipment.slot;
        bool definitionIsWeaponSlot = definitionSlot == EquipmentSlotType.Weapon || definitionSlot == EquipmentSlotType.WeaponSecondary;
        bool overrideIsWeaponSlot   = targetSlotOverride == EquipmentSlotType.Weapon || targetSlotOverride == EquipmentSlotType.WeaponSecondary;

        var slot = (targetSlotOverride.HasValue && definitionIsWeaponSlot && overrideIsWeaponSlot)
            ? targetSlotOverride.Value
            : definitionSlot;

        var currentlyEquipped = GetEquipped(slot);
        if (currentlyEquipped != null)
        {
            // 用 AddExactEntry 把这一件原样放回去（保留它自己的耐久/染色/词条），
            // 不能用 AddItem(definition, count)——那个会新建一个全新entry丢光这些
            // 数据，maxStackCount>1 时还会把数量合并进背包里已有的同款堆叠，混进
            // 另一件独立实例里，后面按引用删除时会牵连着一起删掉。
            if (!inv.AddExactEntry(currentlyEquipped))
            {
                Debug.LogWarning($"[EquipmentRuntime] TryEquipFromInventory 拒绝：背包放不下被换下来的 " +
                    $"「{currentlyEquipped.definition.itemKey}」。");
                return false; // 背包满，无法换装
            }
            Unequip(slot);
        }

        Equip(entry, slot);
        // 按具体实例移除，不能用 RemoveItem(definition, count)——那个按种类从列表
        // 末尾找任意一堆来扣，背包里如果还有同款的另一件，很可能被误删，这一件反而
        // 留在背包原位没删掉，变成"同一件东西同时在已装备和背包里"。
        inv.RemoveExactEntry(entry, entry.count);
        Debug.Log($"[EquipmentRuntime] 换装成功：槽位={slot}，装上「{entry.definition.itemKey}」。");
        return true;
    }

    /// <summary>把指定槽位的装备卸下、还回背包；背包满则失败并保持原状。</summary>
    public bool TryUnequipToInventory(InventoryRuntime inv, EquipmentSlotType slot)
    {
        if (inv == null) return false;

        var entry = GetEquipped(slot);
        if (entry == null) return false;

        // 同样必须原样放回去（AddExactEntry），理由见 TryEquipFromInventory 里的说明。
        if (!inv.AddExactEntry(entry))
            return false; // 背包满，无法卸装

        Unequip(slot);
        return true;
    }
}
