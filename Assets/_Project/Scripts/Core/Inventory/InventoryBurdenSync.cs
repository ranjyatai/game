using UnityEngine;

/// <summary>
/// 挂在 InventoryRuntimeBootstrap 上，把"背包里的重量 + 身上已装备的重量"合计同步到
/// 当前玩家的 UnitMovementController，保持 HUD 负重比（LD）与实际总重量一致。
///
/// 装备中的武器/防具会从背包格子里移除（见 EquipmentRuntime.TryEquipFromInventory
/// 的 RemoveExactEntry 说明），之前这里只读 InventoryRuntime.TotalWeight（背包格子
/// 里躺着的东西），装备到身上的这部分重量完全没算进负重——不管东西是在背包里还是
/// 已经穿/拿在身上，都应该算进玩家的负重，不能因为换了个"存放位置"就凭空消失。
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryBurdenSync : MonoBehaviour
{
    private InventoryRuntime _inventory;
    private UnitMovementController _movement;
    private EquipmentWeightController _equipWeight;

    private void OnEnable()
    {
        _inventory = InventoryRuntimeBootstrap.Instance?.Inventory;
        if (_inventory != null)
            _inventory.OnInventoryChanged += OnInventoryChanged;

        EquipmentRuntime.OnEquipped   += OnEquipmentChanged;
        EquipmentRuntime.OnUnequipped += OnEquipmentChanged;
        SkyPrisonPlayerAuthority.CurrentPlayerChangedGlobal += OnPlayerChanged;

        // 初始同步一次
        RebindMovement();
        Push();
    }

    private void OnDisable()
    {
        if (_inventory != null)
            _inventory.OnInventoryChanged -= OnInventoryChanged;

        EquipmentRuntime.OnEquipped   -= OnEquipmentChanged;
        EquipmentRuntime.OnUnequipped -= OnEquipmentChanged;
        SkyPrisonPlayerAuthority.CurrentPlayerChangedGlobal -= OnPlayerChanged;
    }

    private void OnEquipmentChanged(EquipmentSlotType slot, InventoryItemEntry entry) => Push();

    private void OnPlayerChanged(
        SkyPrisonUnitRuntimeIdentity oldPlayer,
        SkyPrisonUnitRuntimeIdentity newPlayer)
    {
        RebindMovement();
        Push();
    }

    private void RebindMovement()
    {
        var auth = Object.FindObjectOfType<SkyPrisonPlayerAuthority>();
        var player = auth?.CurrentPlayer;
        _movement = player != null
            ? player.GetComponentInChildren<UnitMovementController>(true)
            : null;
        _equipWeight = player != null
            ? player.GetComponentInChildren<EquipmentWeightController>(true)
            : null;

        // 兜底：若 Authority 还没绑定玩家，直接从场景找
        if (_movement == null)
            _movement = Object.FindObjectOfType<UnitMovementController>();
        if (_equipWeight == null)
            _equipWeight = Object.FindObjectOfType<EquipmentWeightController>();
    }

    private void OnInventoryChanged() => Push();

    private void Update()
    {
        // 如果还没找到移动控制器（玩家晚于 Bootstrap 出现），持续尝试绑定
        if (_movement == null)
        {
            RebindMovement();
            if (_movement != null) Push();
        }
    }

    private void Push()
    {
        if (_inventory == null) return;
        if (_movement == null) return;

        // EquipmentWeightController 自己也订阅了同一个装备变化事件、也会 Rebuild 一遍
        // TotalEquipWeight，但两边订阅顺序不确定——如果这边的 Push() 先于对方的 Rebuild()
        // 执行，读到的就是上一次装备状态的旧值。这里主动先 Rebuild 一次再读，不依赖
        // 谁先订阅谁后订阅。
        _equipWeight?.Rebuild();
        float equippedWeight = _equipWeight != null ? _equipWeight.TotalEquipWeight : 0f;
        _movement.SetBurdenValues(_inventory.TotalWeight + equippedWeight, _inventory.MaxWeightCapacity);
    }
}
