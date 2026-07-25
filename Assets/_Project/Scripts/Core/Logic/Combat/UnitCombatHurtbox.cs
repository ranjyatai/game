using Spine.Unity;
using UnityEngine;

/// <summary>
/// 单位受击框（3D 碰撞体版本）。
/// 伤害计算由 UnitActionModuleRuntime.HandleHit 统一处理；
/// Hurtbox 只负责受击动画 + 硬直，不直接调 ApplyDamage。
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class UnitCombatHurtbox : MonoBehaviour
{
    [SerializeField] private UnitHealthController healthController;
    [SerializeField] private UnitActionController actionController;
    [SerializeField] private SkeletonAnimation    skeletonAnimation;

    [Tooltip("受击硬直时长（秒）。0 = 不打断动作。技能的 stunDuration 优先，此值作兜底。")]
    [SerializeField] private float  hitStunDuration  = 0.4f;
    [Tooltip("Spine 受击动画名称（Track 1 播放）。")]
    [SerializeField] private string hurtAnimationKey = "Hurt";

    // 防止同一帧/同一次挥击重复触发动画
    private float _lastHitTime = -999f;
    private const float HIT_COOLDOWN = 0.1f;

    public UnitHealthController HealthController => healthController;

    private void Awake()
    {
        var col       = GetComponent<Collider>();
        col.isTrigger = true;

        if (healthController == null)
            healthController = GetComponentInParent<UnitHealthController>(true);

        if (actionController == null)
            actionController = GetComponentInParent<UnitActionController>(true);

        if (skeletonAnimation == null)
            skeletonAnimation = GetComponentInParent<SkeletonAnimation>(true)
                             ?? GetComponentInChildren<SkeletonAnimation>(true);
    }

    public void SetHealthController(UnitHealthController hc) => healthController = hc;

    private void OnTriggerEnter(Collider other)
    {
        UnitCombatHitbox hitbox = other.GetComponent<UnitCombatHitbox>()
                               ?? other.GetComponentInParent<UnitCombatHitbox>(true);

        if (hitbox == null || !hitbox.IsActive) return;
        if (hitbox.Owner != null && transform.IsChildOf(hitbox.Owner.transform)) return;

        // 冷却：防止骨骼移动导致物理引擎重复触发 enter
        if (Time.time - _lastHitTime < HIT_COOLDOWN) return;
        if (healthController != null && healthController.IsDead) return;
        // 濒死弹窗期间不再受击
        UnitDeathController deathCtrl = healthController != null
            ? healthController.GetComponent<UnitDeathController>()
           ?? healthController.GetComponentInParent<UnitDeathController>(true)
            : null;
        if (deathCtrl != null && deathCtrl.IsDeadLike) return;
        // 闪避无敌帧：闪避状态中不受击
        if (actionController != null && actionController.IsDodging) return;
        _lastHitTime = Time.time;

        // 硬直和击退统一由 HandleHit 处理，Hurtbox 只负责受击动画
        if (skeletonAnimation != null && !string.IsNullOrEmpty(hurtAnimationKey))
        {
            var entry = skeletonAnimation.AnimationState.SetAnimation(1, hurtAnimationKey, false);
            if (entry != null) entry.MixDuration = 0.05f;
            skeletonAnimation.AnimationState.AddEmptyAnimation(1, 0.15f, 0f);
        }

        string victim   = healthController != null ? healthController.gameObject.name : transform.root.name;
        string attacker = hitbox.Owner != null ? hitbox.Owner.name : hitbox.transform.root.name;
        Debug.Log($"[Hurtbox] 受击={victim}  攻击={attacker}  HP剩余={healthController?.CurrentHealth}", this);
    }
}
