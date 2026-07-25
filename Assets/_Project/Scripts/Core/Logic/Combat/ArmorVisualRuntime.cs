using UnityEngine;

/// <summary>
/// 装备防具（头/上装/下装/手/鞋）时的视觉——跟 WeaponVisualRuntime 是同一套机制
/// （见其类注释），区别是防具没有判定/Hitbox要切，只有皮肤+染色；而且同一时间最多
/// 5件防具可以同时装备，不像武器只有一把"生效"，所以每个槽位独立记录、独立应用，
/// 互不覆盖（见 AppearanceRuntime._armorSkinKeys / ArmorDyeTags 上的说明）。
///
/// 真正的皮肤切换/合并交给 AppearanceRuntime——这里只在防具装备/卸下时调
/// SetArmorSkin/SetArmorDye，不直接操作 Skeleton.SetSkin。
/// </summary>
[DisallowMultipleComponent]
public class ArmorVisualRuntime : MonoBehaviour
{
    private static readonly EquipmentSlotType[] ArmorSlots =
    {
        EquipmentSlotType.Head,
        EquipmentSlotType.UpperBody,
        EquipmentSlotType.LowerBody,
        EquipmentSlotType.Hands,
        EquipmentSlotType.Shoes,
    };

    private void OnEnable()
    {
        EquipmentRuntime.OnEquipped   += HandleEquipped;
        EquipmentRuntime.OnUnequipped += HandleUnequipped;
    }

    private void OnDisable()
    {
        EquipmentRuntime.OnEquipped   -= HandleEquipped;
        EquipmentRuntime.OnUnequipped -= HandleUnequipped;
    }

    private void HandleEquipped(EquipmentSlotType slot, InventoryItemEntry entry)
    {
        if (!IsArmorSlot(slot) || !BelongsToEquipmentSingleton()) return;

        string skinName = entry?.definition?.equipment?.armorSkinName;
        skinName = !string.IsNullOrEmpty(skinName) ? skinName : null;
        AppearanceRuntime.Instance?.SetArmorSkin(slot, skinName);
        // 染色是这件装备实例自己的（工坊改过色就跟配置表默认色不一样了），不是从
        // definition上读——definition只决定"皮肤"这个所有实例共享的部分。
        AppearanceRuntime.Instance?.SetArmorDye(slot, entry?.dyeColors);
    }

    private void HandleUnequipped(EquipmentSlotType slot, InventoryItemEntry entry)
    {
        if (!IsArmorSlot(slot) || !BelongsToEquipmentSingleton()) return;

        AppearanceRuntime.Instance?.SetArmorSkin(slot, null);
        AppearanceRuntime.Instance?.SetArmorDye(slot, null);
    }

    private static bool IsArmorSlot(EquipmentSlotType slot)
    {
        for (int i = 0; i < ArmorSlots.Length; i++)
        {
            if (ArmorSlots[i] == slot) return true;
        }
        return false;
    }

    // EquipmentRuntime.OnEquipped/OnUnequipped 是静态事件，全场景只有一份
    // EquipmentRuntime.Instance（代表玩家一个人的装备）——但 ArmorVisualRuntime 是
    // 挂在所有Character类型单位身上的（敌人也有），大家都订阅了同一份静态事件。
    // 用项目里现成的"当前玩家是谁"权威判断，直接问"我是不是玩家控制的那个单位"
    // （跟 WeaponVisualRuntime.BelongsToEquipmentSingleton 同一个判断方式）。
    private bool BelongsToEquipmentSingleton()
    {
        GameObject playerGo = SkyPrisonPlayerAuthority.Instance != null
            ? SkyPrisonPlayerAuthority.Instance.CurrentPlayerGameObject
            : null;
        if (playerGo == null) return false;

        return transform.IsChildOf(playerGo.transform) || transform == playerGo.transform;
    }
}
