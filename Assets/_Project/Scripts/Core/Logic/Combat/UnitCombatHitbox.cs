using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 挂在角色 Hitbox 子物体上（3D 碰撞体版本）。
/// 平时 Collider 禁用；Spine hit_start 事件时由 UnitActionModuleRuntime 调 Activate()，
/// hit_end 或技能结束时调 Deactivate()。
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class UnitCombatHitbox : MonoBehaviour
{
    [Header("归属")]
    [Tooltip("此 Hitbox 属于哪个角色的伤害来源，用于排除自身受击")]
    [SerializeField] private GameObject owner;

    [Tooltip("朝向参考根节点（localScale.x > 0 = 面右）。留空自动取 transform.root。仅在找不到 UnitMovementController 时作为兜底。")]
    [SerializeField] private Transform facingRoot;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;

    private Collider      _col;
    private SkillDefinition _activeSkill;
    private readonly HashSet<Collider> _hitThisSwing = new HashSet<Collider>();

    // 视觉翻转翻的是 Spine 骨骼子节点的 localScale（SpineAnimationDriver_Current），
    // 从来不会去动 transform.root 的 localScale——facingRoot 默认指向 transform.root，
    // 导致这里读到的永远是面右（+1），hitbox 的 offset.x 永远不镜像，人物明明转身面向
    // 目标了，攻击判定框却还留在原来那一侧，造成"看着打中了实际打空"。
    // 直接读 SpineAnimationDriver_Current.Facing（唯一朝向真值来源：1=面左，-1=面右），
    // 别自己按 FacingInput/localScale 另猜符号约定——这个项目里 FacingInput.x>0 反而对应
    // facing=-1（面右），两套符号是反的，猜错了会导致镜像方向刚好相反，比完全不镜像更隐蔽。
    // 找不到 SpineAnimationDriver_Current 时才退回 localScale 兜底。
    private SpineAnimationDriver_Current _spineFacing;

    // 手部有美术画的 boundingbox 附件（插槽名约定 "arm_R_hand2"/"arm_L_hand2"）时优先用
    // 这个——附件形状跟着挥拳动画实时形变/移动，比下面固定半径的胶囊体准得多。
    // 找不到附件（角色还没画）时才退回 offset/radius 那套写死数值的兜底逻辑。
    private Spine.Unity.BoundingBoxFollower _boundingBoxSource;

    public SkillDefinition ActiveSkill => _activeSkill;
    public bool            IsActive    => _col != null && _col.enabled;
    public GameObject      Owner       => owner;

    public event System.Action<UnitCombatHitbox, Collider> OnHit;

    public void SetFacingRoot(Transform root) => facingRoot = root;
    public void SetOwner(GameObject go)       => owner      = go;
    public void SetBoundingBoxSource(Spine.Unity.BoundingBoxFollower follower) => _boundingBoxSource = follower;

    private void Awake()
    {
        // Hitbox 是纯 Trigger，不需要 Rigidbody。
        // 若 Prefab 上误挂了 Rigidbody，BoneFollower 与物理插值会导致碰撞体位置偏移，运行时自动移除。
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);

        _col = GetComponent<Collider>();
        _col.isTrigger = true;
        _col.enabled   = false;

        if (owner == null)      owner      = transform.root.gameObject;
        if (facingRoot == null) facingRoot = transform.root;

        _spineFacing = GetComponentInParent<SpineAnimationDriver_Current>();
    }

    public void Activate(SkillDefinition skill)
    {
        _activeSkill = skill;
        _hitThisSwing.Clear();

        if (skill != null)
            ApplyHitboxShape(skill.hitbox);

        _col.enabled = true;
        if (debugLogs) Debug.Log($"[Hitbox:{name}] Activated → {skill?.skillKey}", this);
    }

    private void Update()
    {
        // boundingbox 附件跟着挥拳动画每帧形变/移动，写死数值那套 ApplyHitboxShape 只在
        // Activate() 那一瞬间算一次就够了（技能配置不会中途变），但这个必须逐帧刷新，
        // 否则手在动、判定框却停在挥拳开始那一帧的位置不动，等于没接上。
        if (_col != null && _col.enabled && _boundingBoxSource != null)
            SyncFromBoundingBox();
    }

    private void SyncFromBoundingBox()
    {
        PolygonCollider2D poly = _boundingBoxSource.CurrentCollider;
        if (poly == null) return;

        Bounds worldBounds = poly.bounds;
        Vector3 localCenter = transform.InverseTransformPoint(worldBounds.center);
        float lossyScale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
        float localRadius = Mathf.Max(worldBounds.extents.x, worldBounds.extents.y) / lossyScale;
        localRadius = Mathf.Max(0.15f, localRadius);

        if (_col is CapsuleCollider cap)
        {
            cap.center = new Vector3(localCenter.x, localCenter.y, 0f);
            cap.radius = localRadius;
            if (cap.direction == 2)
                cap.height = Mathf.Max(cap.height, localRadius * 2f + 0.01f);
        }
        else if (_col is SphereCollider sc)
        {
            sc.center = new Vector3(localCenter.x, localCenter.y, 0f);
            sc.radius = localRadius;
        }
        else if (_col is BoxCollider bc)
        {
            bc.center = new Vector3(localCenter.x, localCenter.y, 0f);
            bc.size   = new Vector3(localRadius * 2f, localRadius * 2f, bc.size.z);
        }
    }

    private void ApplyHitboxShape(SkillHitboxData data)
    {
        if (data == null) return;

        // 有 boundingbox 附件的话，形状/位置交给 SyncFromBoundingBox() 逐帧刷新，
        // 这里只需要先按 offset/radius 兜底摆一次位置，避免第一帧还没跑到 Update
        // 之前判定框停在上一次的旧数值上。
        float facingSign;
        if (_spineFacing != null)
            facingSign = _spineFacing.Facing == 1 ? -1f : 1f; // Facing: 1=面左(需镜像)，-1=面右(原样)
        else
            facingSign = (facingRoot != null && facingRoot.localScale.x < 0f) ? -1f : 1f;
        Vector3 offset   = new Vector3(data.offset.x * facingSign, data.offset.y, 0f);
        float r          = data.shape == SkillHitboxShape.Circle
                           ? data.radius
                           : Mathf.Max(data.size.x, data.size.y) * 0.5f;

        if (_col is CapsuleCollider cap)
        {
            cap.center = offset;
            cap.radius = r;
            if (cap.direction == 2 && data.zDepth > 0f)
                cap.height = Mathf.Max(data.zDepth, r * 2f + 0.01f);
        }
        else if (_col is SphereCollider sc)
        {
            sc.center = offset;
            sc.radius = r;
        }
        else if (_col is BoxCollider bc)
        {
            bc.center = offset;
            bc.size   = new Vector3(r * 2f, r * 2f, bc.size.z);
        }
    }

    public void Deactivate()
    {
        _col.enabled = false;
        _activeSkill = null;
        _hitThisSwing.Clear();
        if (debugLogs) Debug.Log($"[Hitbox:{name}] Deactivated", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_col.enabled) return;
        if (other == null) return;
        if (owner != null && other.transform.IsChildOf(owner.transform)) return;
        if (_hitThisSwing.Contains(other)) return;

        // 攻击者自己已经死亡/濒死：这一下打出去的时候人已经死了，不该再算命中——
        // 之前只检查了"目标"死没死（防止鞭尸），没检查"发起攻击的这一方"死没死。
        // 敌人被打死那一刻，如果它自己的攻击 Hitbox 恰好还在生效窗口内（动画/协程还没
        // 来得及被打断），这一下依然会正常判定命中，表现为"敌人死亡瞬间，最后一击
        // 像亡语一样还是砸到了玩家身上"，即使死亡动画已经开始播放。
        UnitDeathController ownerDeath = owner != null
            ? (owner.GetComponent<UnitDeathController>() ?? owner.GetComponentInParent<UnitDeathController>(true))
            : null;
        if (ownerDeath != null && ownerDeath.IsDeadLike) return;

        UnitCombatHurtbox hurtbox =
            other.GetComponent<UnitCombatHurtbox>()
         ?? other.GetComponentInParent<UnitCombatHurtbox>(true);

        UnitHealthController health = hurtbox != null
            ? hurtbox.HealthController
            : other.GetComponent<UnitHealthController>()
           ?? other.GetComponentInParent<UnitHealthController>(true);

        if (health == null) return;

        // 阵营检查：只有敌对阵营才互相伤害
        if (!AreHostile(owner, health.gameObject)) return;

        // 目标濒死/死亡：不受击
        UnitDeathController targetDeath =
            health.GetComponent<UnitDeathController>()
         ?? health.GetComponentInParent<UnitDeathController>(true);
        if (targetDeath != null && targetDeath.IsDeadLike) return;

        // 目标闪避无敌帧：闪避中不受击
        UnitActionController targetAction =
            health.GetComponent<UnitActionController>()
         ?? health.GetComponentInParent<UnitActionController>(true);
        if (targetAction != null && targetAction.IsDodging) return;

        _hitThisSwing.Add(other);
        OnHit?.Invoke(this, other);

        // 玩家武器命中时磨损
        if (GetIdentity(owner) == CharacterIdentity.Player && EquipmentRuntime.Instance != null)
        {
            var weaponEntry = EquipmentRuntime.Instance.GetWeapon();
            if (weaponEntry != null)
                DurabilitySystem.Wear(weaponEntry);
        }
    }

    // ── 阵营判断 ─────────────────────────────────────────────────
    // 玩家阵营：Player、Ally
    // 敌对阵营：Enemy、Elite、Boss、NeutralHostile、Creature
    // NeutralPassive 不主动攻击任何人
    public static bool AreHostile(GameObject a, GameObject b)
    {
        if (a == null || b == null) return true; // 保守：找不到归属视为可命中

        CharacterIdentity idA = GetIdentity(a);
        CharacterIdentity idB = GetIdentity(b);

        // 相同 identity 不互打
        if (idA == idB) return false;

        bool aIsPlayerSide = idA == CharacterIdentity.Player || idA == CharacterIdentity.Ally;
        bool bIsPlayerSide = idB == CharacterIdentity.Player || idB == CharacterIdentity.Ally;

        bool aIsEnemySide  = idA == CharacterIdentity.Enemy || idA == CharacterIdentity.Elite ||
                             idA == CharacterIdentity.Boss  || idA == CharacterIdentity.NeutralHostile ||
                             idA == CharacterIdentity.Creature;
        bool bIsEnemySide  = idB == CharacterIdentity.Enemy || idB == CharacterIdentity.Elite ||
                             idB == CharacterIdentity.Boss  || idB == CharacterIdentity.NeutralHostile ||
                             idB == CharacterIdentity.Creature;

        // 玩家侧 vs 敌对侧 才算敌对
        return (aIsPlayerSide && bIsEnemySide) || (aIsEnemySide && bIsPlayerSide);
    }

    private static CharacterIdentity GetIdentity(GameObject go)
    {
        SkyPrisonUnitRuntimeIdentity rid =
            go.GetComponent<SkyPrisonUnitRuntimeIdentity>()
         ?? go.GetComponentInParent<SkyPrisonUnitRuntimeIdentity>(true)
         ?? go.GetComponentInChildren<SkyPrisonUnitRuntimeIdentity>(true);
        if (rid != null) return rid.RuntimeIdentity;

        // 兜底：读 UnitDefinition 上的静态 characterIdentity
        UnitDefinitionRuntimeBinder binder =
            go.GetComponent<UnitDefinitionRuntimeBinder>()
         ?? go.GetComponentInParent<UnitDefinitionRuntimeBinder>(true)
         ?? go.GetComponentInChildren<UnitDefinitionRuntimeBinder>(true);
        return binder?.UnitDefinitionAsset?.characterIdentity ?? CharacterIdentity.Enemy;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (_col == null) _col = GetComponent<Collider>();
        if (_col == null) return;

        Gizmos.color = _col.enabled
            ? new Color(1f, 0.2f, 0.2f, 0.6f)
            : new Color(0.8f, 0.8f, 0.2f, 0.15f);

        if (_col is CapsuleCollider cap)
            Gizmos.DrawWireSphere(transform.TransformPoint(cap.center), cap.radius);
        else if (_col is SphereCollider sc)
            Gizmos.DrawWireSphere(transform.TransformPoint(sc.center), sc.radius);
        else if (_col is BoxCollider bc)
        {
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(bc.center), transform.rotation, transform.lossyScale);
            Gizmos.DrawWireCube(Vector3.zero, bc.size);
            Gizmos.matrix = old;
        }
    }
#endif
}
