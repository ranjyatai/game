using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 背包与存档系统的对接桥梁。单例，DontDestroyOnLoad。
/// 监听 SaveManager 事件，在合适时机把背包/仓库数据同步进出 PlayerSaveData。
///
/// 需要：
///   - InventoryRuntimeBootstrap.Instance.Inventory  （随身背包）
///   - StashRuntime（仓库，待做时接入）
///   - ItemRegistry asset 通过 Inspector 绑定
/// </summary>
public class InventorySaveConnector : MonoBehaviour
{
    public static InventorySaveConnector Instance { get; private set; }

    [Tooltip("ItemRegistry asset，留空则自动从 Resources/ItemRegistry 加载")]
    [SerializeField] private ItemRegistry itemRegistry;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (itemRegistry == null)
            itemRegistry = GameAssetLoader.ItemRegistry;

        if (itemRegistry == null)
            Debug.LogError("[InventorySaveConnector] 找不到 ItemRegistry。请执行菜单 Sky Prison → 重建 Asset Manifest。");
    }

    private void OnEnable()
    {
        SaveManager.OnSaveLoaded += HandleSaveLoaded;
        SaveManager.OnNewGame   += HandleNewGame;
        SaveManager.OnSaved     += HandleSaved;
    }

    private void OnDisable()
    {
        SaveManager.OnSaveLoaded -= HandleSaveLoaded;
        SaveManager.OnNewGame    -= HandleNewGame;
        SaveManager.OnSaved      -= HandleSaved;
    }

    // ── 读档：存档→背包 ───────────────────────────────────────────────────

    private void HandleSaveLoaded()
    {
        if (itemRegistry == null)
        {
            Debug.LogError("[InventorySaveConnector] ItemRegistry 未绑定，无法还原背包。");
            return;
        }

        var inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv != null)
            inv.Deserialize(SaveManager.Player?.backpack, itemRegistry);

        StashRuntime.Instance?.Deserialize(SaveManager.Player?.stashPages, itemRegistry);

        QuickSlotRuntime.Instance?.Deserialize(SaveManager.Player?.quickSlots, itemRegistry);

        DeserializeEquipment(SaveManager.Player?.equippedSlots, SaveManager.Player?.activeWeaponSlot ?? EquipmentSlotType.Weapon);
    }

    private void HandleNewGame()
    {
        var inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv != null)
            inv.Deserialize(null, itemRegistry);

        StashRuntime.Instance?.Deserialize(null, itemRegistry);

        QuickSlotRuntime.Instance?.Deserialize(null, itemRegistry);

        EquipmentRuntime.Instance?.ClearAllSlotsForNewSession();
    }

    private void HandleSaved()
    {
        if (SaveManager.Player == null) return;

        var inv = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (inv != null)
            SaveManager.Player.backpack = inv.Serialize();

        if (StashRuntime.Instance != null)
            SaveManager.Player.stashPages = StashRuntime.Instance.Serialize(itemRegistry);

        if (QuickSlotRuntime.Instance != null)
            SaveManager.Player.quickSlots = QuickSlotRuntime.Instance.Serialize();

        SaveManager.Player.equippedSlots = SerializeEquipment();
        SaveManager.Player.activeWeaponSlot = EquipmentRuntime.Instance?.ActiveWeaponSlot ?? EquipmentSlotType.Weapon;
    }

    // ── 装备槽：EquipmentRuntime <-> 存档 ──────────────────────────────────
    // EquipmentRuntime 自己的槽位数据(_slots)之前完全没接进存档系统——装备到某个槽位时
    // 物品实例已经从背包移除(见EquipmentRuntime.TryEquipFromInventory)，不属于背包
    // 数据的一部分，只靠上面 inv.Deserialize 那条路径救不回来，需要单独存/取。

    private List<SavedEquipmentSlotEntry> SerializeEquipment()
    {
        var result = new List<SavedEquipmentSlotEntry>();
        var eq = EquipmentRuntime.Instance;
        if (eq == null) return result;

        foreach (EquipmentSlotType slot in Enum.GetValues(typeof(EquipmentSlotType)))
        {
            InventoryItemEntry entry = eq.GetEquipped(slot);
            if (entry?.definition == null) continue;
            result.Add(new SavedEquipmentSlotEntry(slot, entry));
        }
        return result;
    }

    private void DeserializeEquipment(List<SavedEquipmentSlotEntry> saved, EquipmentSlotType activeWeaponSlot)
    {
        var eq = EquipmentRuntime.Instance;
        if (eq == null) return;

        // 先清空——EquipmentRuntime是DontDestroyOnLoad单例，读档前必须先扔掉上一局/
        // 上一次读档残留在内存里的装备，否则会跟这次刚还原的状态叠在一起。
        eq.ClearAllSlotsForNewSession();
        eq.SetActiveWeaponSlotForLoad(activeWeaponSlot);

        if (saved == null || itemRegistry == null) return;

        foreach (var savedSlot in saved)
        {
            if (savedSlot?.item == null) continue;
            InventoryItemEntry entry = savedSlot.item.ToInventoryItemEntry(itemRegistry);
            if (entry == null) continue;
            eq.Equip(entry, savedSlot.slot);
        }
    }

    // ── 手动触发（供其他系统主动调用）────────────────────────────────────

    /// <summary>立即把当前背包写进 PlayerSaveData 并落盘。</summary>
    public static void FlushAndSave()
    {
        Instance?.HandleSaved();
        SaveManager.Save();
    }
}
