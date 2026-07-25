using UnityEngine;
using Spine.Unity;

/// <summary>
/// 装备近战武器时的视觉+判定——武器现在直接画在角色自己的Spine骨架里（美术直接在
/// Axia_base.spine项目里把这把武器所有图层建成一个具名Skin，比如
/// "weapon_sword_heavySpade"），不再是独立的Spine文件/预制体，也不用另外实例化
/// 一个GameObject、算缩放/角度/图层——角色自己的渲染管线（排序、遮挡、图层）本来
/// 就适用于这份Mesh里的每一个三角形，不用额外处理。
///
/// 视觉：真正的皮肤切换/合并交给 AppearanceRuntime（本来就在管发型/内衣皮肤的合并
/// 叠加，见其类注释）——这里只是在武器装备/卸下/切换主副手时调 SetWeaponSkin，
/// 不能自己直接调 Skeleton.SetSkin：那样会把 AppearanceRuntime 刚设好的发型/内衣
/// 皮肤覆盖掉，两边各写一份必然打架。
///
/// 判定：武器的判定形状（Boundingbox附件）现在也在角色自己骨架里同一个插槽下，
/// 不用再找"另一个骨架文件"——装备时把 UnitCombatHitbox 的判定来源从角色徒手
/// （arm_L_hand2/arm_R_hand2）切到武器判定插槽（ItemEquipmentExtension.
/// weaponJudgmentSlotName），都是同一个 BoneFollower 追的同一具角色骨架，
/// 只是 BoundingBoxFollower 读的插槽名字不同。
///
/// 只处理"当前生效"的那把武器槽（跟 EquipmentRuntime.ActiveWeaponSlot 一致）——装到
/// 非生效槽（比如已经在用主武器，又往副武器槽塞了东西）不应该动当前生效的皮肤/判定。
/// </summary>
[DisallowMultipleComponent]
public class WeaponVisualRuntime : MonoBehaviour
{
    [SerializeField] private UnitActionModuleRuntime actionModule;
    [SerializeField] private bool debugLogs = true; // 调试阶段先默认开——这个组件是代码动态AddComponent加的，没有Prefab能提前勾选这个字段

    // 角色自己徒手的判定来源——只在第一次真正需要切换武器判定时捕获一次，之后
    // 卸武器都切回这一份，不用每次重新去 hitbox 身上找。
    private BoundingBoxFollower _handBoundingBoxSource;
    private bool _handSourceCaptured;

    // 当前武器判定专属的 BoneFollower+BoundingBoxFollower 挂载物——独立GameObject
    // （见 ApplyJudgment 内注释：不能挂骨架根节点，也不能挂 hitbox 自己身上），
    // 换武器/卸武器时整个销毁重建。
    private GameObject _weaponBBSourceGo;
    private BoundingBoxFollower _weaponBoundingBoxFollower;

    private void Awake()
    {
        if (actionModule == null) actionModule = GetComponent<UnitActionModuleRuntime>();
    }

    private void OnEnable()
    {
        EquipmentRuntime.OnEquipped   += HandleEquipped;
        EquipmentRuntime.OnUnequipped += HandleUnequipped;
        EquipmentRuntime.OnActiveWeaponChanged += HandleActiveWeaponChanged;
    }

    private void OnDisable()
    {
        EquipmentRuntime.OnEquipped   -= HandleEquipped;
        EquipmentRuntime.OnUnequipped -= HandleUnequipped;
        EquipmentRuntime.OnActiveWeaponChanged -= HandleActiveWeaponChanged;
    }

    private void HandleEquipped(EquipmentSlotType slot, InventoryItemEntry entry)
    {
        if (!IsActiveWeaponSlot(slot)) return;
        ApplyWeapon(entry?.definition?.equipment, entry?.dyeColors);
    }

    private void HandleUnequipped(EquipmentSlotType slot, InventoryItemEntry entry)
    {
        if (!IsActiveWeaponSlot(slot)) return;
        RevertWeapon();
    }

    // 滚轮切换主/副武器——生效槽变了，皮肤/判定也要跟着换成那把武器的。
    private void HandleActiveWeaponChanged(EquipmentSlotType newActiveSlot)
    {
        if (!BelongsToEquipmentSingleton()) return;
        var entry = EquipmentRuntime.Instance?.GetEquipped(newActiveSlot);
        if (entry != null) ApplyWeapon(entry.definition?.equipment, entry.dyeColors);
        else RevertWeapon();
    }

    private bool IsActiveWeaponSlot(EquipmentSlotType slot)
    {
        if (slot != EquipmentSlotType.Weapon && slot != EquipmentSlotType.WeaponSecondary) return false;
        if (!BelongsToEquipmentSingleton()) return false;
        return EquipmentRuntime.Instance != null && slot == EquipmentRuntime.Instance.ActiveWeaponSlot;
    }

    // EquipmentRuntime.OnEquipped/OnUnequipped 是静态事件，全场景只有一份
    // EquipmentRuntime.Instance（代表玩家一个人的装备）——但 WeaponVisualRuntime 是
    // 挂在所有Character类型单位身上的（敌人也有），大家都订阅了同一份静态事件。
    // 改用项目里现成的"当前玩家是谁"权威判断（SkyPrisonPlayerAuthority.
    // CurrentPlayerGameObject），直接问"我是不是玩家控制的那个单位"。
    private bool BelongsToEquipmentSingleton()
    {
        GameObject playerGo = SkyPrisonPlayerAuthority.Instance != null
            ? SkyPrisonPlayerAuthority.Instance.CurrentPlayerGameObject
            : null;
        if (playerGo == null) return false;

        return transform.IsChildOf(playerGo.transform) || transform == playerGo.transform;
    }

    private void ApplyWeapon(ItemEquipmentExtension ext, Color[] dyeColors)
    {
        if (!BelongsToEquipmentSingleton()) return;

        string skinName = ext != null && !string.IsNullOrEmpty(ext.weaponSkinName) ? ext.weaponSkinName : null;
        AppearanceRuntime.Instance?.SetWeaponSkin(skinName);
        // 染色是这件装备实例自己的（工坊改过色就跟配置表默认色不一样了），不是从
        // ext上读——ext只决定"皮肤"和"判定插槽"这些同一把武器所有实例共享的部分。
        AppearanceRuntime.Instance?.SetWeaponDye(dyeColors);

        ApplyJudgment(ext);
    }

    private void RevertWeapon()
    {
        if (BelongsToEquipmentSingleton())
        {
            AppearanceRuntime.Instance?.SetWeaponSkin(null);
            AppearanceRuntime.Instance?.SetWeaponDye(null);
        }

        RevertJudgment();
    }

    private void ApplyJudgment(ItemEquipmentExtension ext)
    {
        RevertJudgment(); // 先清掉上一把的判定挂件，换武器/卸武器都先经过这里

        if (ext == null || string.IsNullOrEmpty(ext.weaponJudgmentSlotName)) return;
        if (actionModule == null || actionModule.Hitbox == null)
        {
            if (debugLogs) Debug.LogWarning("[WeaponVisualRuntime] 找不到 UnitActionModuleRuntime/Hitbox，武器判定切不了。", this);
            return;
        }

        UnitCombatHitbox hitbox = actionModule.Hitbox;

        if (!_handSourceCaptured)
        {
            _handBoundingBoxSource = hitbox.GetComponent<BoundingBoxFollower>();
            // 只有真的抓到了才算"捕获过"——UnitActionModuleRuntime.Awake跑得比真正绑
            // 手部骨骼的 CombatHitbox_Hand 塞进来早时，这里会先抓到null。之前不管抓没
            // 抓到都标记为true，导致这次没抓到就永远没机会了，后面卸武器/空手攻击时
            // RevertJudgment 也没有真正的手部来源可切，判定框只能停留在上一把武器的
            // 挂件上。现在null的话不标记，下次真正装备武器时还会再试一次。
            if (_handBoundingBoxSource != null)
                _handSourceCaptured = true;
        }

        // hitbox.GetComponent<BoneFollower>() 有时候拿不到——UnitActionModuleRuntime
        // 的 Awake/ResolveReferences 跑得比 UnitDefinitionRuntimeApplier 把"真正绑手骨骼
        // 的 CombatHitbox_Hand"塞进来更早，会先现造一个没有 BoneFollower 的通用胶囊体
        // 占了 hitbox 这个引用；不追这个时序竞争，直接在同一个 hitRoot 底下再搜一遍。
        BoneFollower hitboxBoneFollower = hitbox.GetComponent<BoneFollower>();
        if (hitboxBoneFollower == null && hitbox.transform.parent != null)
            hitboxBoneFollower = hitbox.transform.parent.GetComponentInChildren<BoneFollower>(true);

        if (hitboxBoneFollower == null || hitboxBoneFollower.skeletonRenderer == null)
        {
            if (debugLogs) Debug.LogWarning("[WeaponVisualRuntime] 找不到角色骨架的BoneFollower，武器判定切不了。", this);
            return;
        }

        SkeletonRenderer skeletonRenderer = hitboxBoneFollower.skeletonRenderer;
        Spine.Slot slot = skeletonRenderer.Skeleton?.FindSlot(ext.weaponJudgmentSlotName);
        if (slot == null)
        {
            Debug.LogWarning($"[WeaponVisualRuntime] 角色骨架里找不到判定插槽「{ext.weaponJudgmentSlotName}」，判定沿用徒手。", this);
            return;
        }

        // 不能直接挂在骨架根节点（skeletonRenderer.gameObject）上——Spine 算出来的
        // boundingbox顶点是相对"这个插槽绑定的那根骨骼"的局部坐标，不是相对骨架根
        // 节点，挂在根节点上形状会出现在骨架原点附近而不是武器握持的手上。也不能挂在
        // hitbox自己身上（同物体已有3D Rigidbody，跟BoundingBoxFollower内部生成的
        // PolygonCollider2D冲突，会被Unity直接拒绝创建）。新建一个独立的BoneFollower
        // 物体，逐帧跟着这把武器判定插槽绑定的那根骨骼走（位置+旋转都跟，否则挥砍/
        // 转身时形状朝向会错）。
        string boneName = slot.Data.BoneData?.Name;
        if (string.IsNullOrEmpty(boneName))
        {
            Debug.LogWarning($"[WeaponVisualRuntime] 判定插槽「{ext.weaponJudgmentSlotName}」没有绑定骨骼，判定沿用徒手。", this);
            return;
        }

        _weaponBBSourceGo = new GameObject("WeaponJudgmentBBSource");
        _weaponBBSourceGo.transform.SetParent(hitbox.transform.parent ?? hitbox.transform, false);

        var weaponBoneFollower = _weaponBBSourceGo.AddComponent<BoneFollower>();
        weaponBoneFollower.skeletonRenderer   = skeletonRenderer;
        weaponBoneFollower.boneName           = boneName;
        weaponBoneFollower.followBoneRotation = true;
        weaponBoneFollower.followZPosition    = false;

        _weaponBoundingBoxFollower = _weaponBBSourceGo.AddComponent<BoundingBoxFollower>();
        _weaponBoundingBoxFollower.skeletonRenderer = skeletonRenderer;
        _weaponBoundingBoxFollower.slotName = ext.weaponJudgmentSlotName;
        _weaponBoundingBoxFollower.isTrigger = true;
        _weaponBoundingBoxFollower.Initialize();

        hitbox.SetBoundingBoxSource(_weaponBoundingBoxFollower);

        if (debugLogs)
            Debug.Log($"[WeaponVisualRuntime] 判定切到武器插槽「{ext.weaponJudgmentSlotName}」。", this);
    }

    private void RevertJudgment()
    {
        if (_weaponBBSourceGo != null)
        {
            Destroy(_weaponBBSourceGo);
            _weaponBBSourceGo = null;
            _weaponBoundingBoxFollower = null;
        }

        // 之前这里加了 _handBoundingBoxSource != null 才切换的判断，本意是"没抓到手部
        // 来源就别乱切"，但副作用是：如果没抓到（见上面ApplyJudgment的时序竞争注释），
        // hitbox._boundingBoxSource 会一直停留在刚被 Destroy 掉的武器判定挂件上——
        // Update() 里 "_boundingBoxSource != null" 这个检查对已销毁对象会正确判定为
        // false，不会报错，但表现上判定框会静默回退到写死数值的兜底胶囊体，位置很可能
        // 还留在武器判定挂件销毁前的最后位置附近，看起来就像"空手攻击判定还在武器上"。
        // 现在不管有没有抓到手部来源都无条件调用，null 也要传进去，保证武器挂件的引用
        // 一定会被清掉，不会有残留。
        if (actionModule != null && actionModule.Hitbox != null)
            actionModule.Hitbox.SetBoundingBoxSource(_handBoundingBoxSource);
    }
}
