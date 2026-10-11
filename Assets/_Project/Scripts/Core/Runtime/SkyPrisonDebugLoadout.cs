#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 仅编辑器的测试快捷键（打包后不存在）：
///   F4 = 一键装备：大剑(军用重铲)装主武器并切到它、军铳装副武器且弹匣填满、背包补 300 发子弹；
///   F2 = 在玩家前方放 1 只颂赞者 + 2 只归巢者（先清掉上一批 TEST_CH_*）；
///   F3 = 在玩家前方放两只织面者（8 / 11 米、深度错开 ±1.8，开局同时在画面内、同时看到玩家；先清掉上一批 TEST_FW_*）。
/// 运行时自己常驻，不用挂任何场景对象。已有的 F6~F10 被夜视/遮挡调试/状态调试占着，所以用 F3、F4。
/// </summary>
public class SkyPrisonDebugLoadout : MonoBehaviour
{
    private const string SwordPath = "Assets/_Project/Data/Items/ID_NewItem 1.asset";
    private const string GunPath = "Assets/_Project/Data/Items/ID_NewItem 1_Copy.asset";
    private const string AmmoPath = "Assets/_Project/Data/Items/ID_compressed_9mmBullet.asset";
    private const string WeaverPrefabPath = "Assets/_Project/Prefabs/Units/Enemy/Mob/PF_Unit_Enemy_FaceWeaver.prefab";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (!Application.isPlaying) return;
        if (FindObjectOfType<SkyPrisonDebugLoadout>() != null) return;
        var go = new GameObject("[SkyPrisonDebugLoadout]");
        DontDestroyOnLoad(go);
        go.AddComponent<SkyPrisonDebugLoadout>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F4)) EquipLoadout();
        if (Input.GetKeyDown(KeyCode.F3)) SpawnWeavers();
        if (Input.GetKeyDown(KeyCode.F2)) SpawnChanterGroup();
    }

    private static void EquipLoadout()
    {
        var inv = InventoryRuntimeBootstrap.Instance != null ? InventoryRuntimeBootstrap.Instance.Inventory : null;
        var eq = EquipmentRuntime.Instance;
        if (inv == null || eq == null) { Debug.LogWarning("[DebugLoadout] 背包/装备系统还没就绪，稍后再按 F4"); return; }

        var sword = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SwordPath);
        var gun = AssetDatabase.LoadAssetAtPath<ItemDefinition>(GunPath);
        var ammo = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AmmoPath);
        if (sword == null || gun == null || ammo == null) { Debug.LogWarning("[DebugLoadout] 找不到测试用武器/子弹资产"); return; }

        EquipIfMissing(inv, eq, sword);
        EquipIfMissing(inv, eq, gun);
        inv.AddItem(ammo, 300);

        // 当前生效切到大剑
        if (eq.GetEquipped(EquipmentSlotType.Weapon)?.definition == sword && eq.ActiveWeaponSlot != EquipmentSlotType.Weapon) eq.CycleActiveWeapon();
        else if (eq.GetEquipped(EquipmentSlotType.WeaponSecondary)?.definition == sword && eq.ActiveWeaponSlot != EquipmentSlotType.WeaponSecondary) eq.CycleActiveWeapon();

        foreach (var slot in new[] { EquipmentSlotType.Weapon, EquipmentSlotType.WeaponSecondary })
        {
            var w = eq.GetEquipped(slot);
            if (w != null && w.definition == gun) w.currentMagazineAmmo = gun.equipment.magazineSize;
        }
        UnitActionModuleRuntime.RaiseWeaponAmmoChanged();
        Debug.Log("[DebugLoadout] 已装备：大剑 + 军铳，弹匣满，备用子弹 " + inv.GetAmmoCount(gun.equipment.ammoCaliber));
    }

    private static void EquipIfMissing(InventoryRuntime inv, EquipmentRuntime eq, ItemDefinition def)
    {
        if (eq.GetEquipped(EquipmentSlotType.Weapon)?.definition == def || eq.GetEquipped(EquipmentSlotType.WeaponSecondary)?.definition == def) return;
        inv.AddItem(def, 1);
        InventoryItemEntry entry = null;
        foreach (var s in inv.Slots) if (s != null && s.definition == def && s.count > 0) { entry = s; break; }
        if (entry != null) eq.TryEquipFromInventory(inv, entry);
    }

    private static void SpawnWeavers()
    {
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) { Debug.LogWarning("[DebugLoadout] 玩家还没就绪"); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaverPrefabPath);
        if (prefab == null) { Debug.LogWarning("[DebugLoadout] 找不到织面者预制体"); return; }

        foreach (var go in FindObjectsOfType<Transform>())
            if (go != null && go.parent == null && go.name.StartsWith("TEST_FW_")) Destroy(go.gameObject);

        Vector3 pp = player.transform.position;
        var a = Instantiate(prefab, new Vector3(pp.x + 8f, 0f, pp.z + 1.8f), Quaternion.identity); a.name = "TEST_FW_A";
        var b = Instantiate(prefab, new Vector3(pp.x + 11f, 0f, pp.z - 1.8f), Quaternion.identity); b.name = "TEST_FW_B";
        Debug.Log("[DebugLoadout] 已在前方 8 / 11 米放两只织面者（都在画面内、都在唤醒范围内，开局同时看到玩家）");
    }

    private const string ChanterPrefabPath = "Assets/_Project/Prefabs/Units/Enemy/Mob/PF_Unit_Enemy_Chanter.prefab";
    private const string HomerPrefabPath = "Assets/_Project/Prefabs/Units/Enemy/Mob/PF_Unit_Enemy_Mob_01.prefab";

    // F2：前方放 1 只颂赞者(14 米，躲后排) + 2 只归巢者(9 米，深度错开)，看回血/狂暴
    private static void SpawnChanterGroup()
    {
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) { Debug.LogWarning("[DebugLoadout] 玩家还没就绪"); return; }
        var chanter = AssetDatabase.LoadAssetAtPath<GameObject>(ChanterPrefabPath);
        var homer = AssetDatabase.LoadAssetAtPath<GameObject>(HomerPrefabPath);
        if (chanter == null || homer == null) { Debug.LogWarning("[DebugLoadout] 找不到颂赞者/归巢者预制体"); return; }

        foreach (var go in FindObjectsOfType<Transform>())
            if (go != null && go.parent == null && go.name.StartsWith("TEST_CH_")) Destroy(go.gameObject);

        Vector3 pp = player.transform.position;
        Instantiate(homer, new Vector3(pp.x + 9f, 0f, pp.z + 1.5f), Quaternion.identity).name = "TEST_CH_Homer_A";
        Instantiate(homer, new Vector3(pp.x + 9f, 0f, pp.z - 1.5f), Quaternion.identity).name = "TEST_CH_Homer_B";
        Instantiate(chanter, new Vector3(pp.x + 14f, 0f, pp.z), Quaternion.identity).name = "TEST_CH_Chanter";
        Debug.Log("[DebugLoadout] 已放 1 只颂赞者(14 米) + 2 只归巢者(9 米)");
    }
}
#endif
