using System.Collections.Generic;
using Effekseer;
using UnityEngine;

/// <summary>
/// 剑气弹幕抛射物——运行时用 Spawn() 整个用代码生成(不需要预先做Prefab)，沿固定
/// 方向匀速飞行，飞行中用球形触发器检测碰撞。命中判定/伤害结算完全复用近战攻击
/// 那一套(UnitActionModuleRuntime.ResolveSkillHit)，友伤/阵营/死亡/闪避无敌帧检查
/// 直接照抄 UnitCombatHitbox.OnTriggerEnter 的规则，保持两条命中路径行为一致。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonSwordQiProjectile : MonoBehaviour
{
    private GameObject _owner;
    private UnitActionModuleRuntime _ownerActionModule;
    private SkillDefinition _skill;
    private Vector3 _direction;
    private float _speed;
    private float _hitRadius;
    private bool _destroyOnHit;
    private float _dieAt;
    private EffekseerHandle _vfxHandle;
    private readonly HashSet<Collider> _hitAlready = new HashSet<Collider>();
    // 复用的 SphereCast 结果缓冲区，避免每帧 new 一个数组。飞行路径上同时挂着的目标
    // 数量不会很多，8 个足够，真超了也只是漏检最远的几个，不会报错。
    private static readonly RaycastHit[] SweepHitsBuffer = new RaycastHit[8];

    /// <summary>生成一发抛射物。owner=发射者根物体(友伤排除/阵营判断用)，
    /// ownerActionModule=命中时回调伤害结算的那个组件，direction 已经是归一化的世界
    /// 方向（不用再传角度，调用方算好）。</summary>
    public static SkyPrisonSwordQiProjectile Spawn(
        GameObject owner,
        UnitActionModuleRuntime ownerActionModule,
        SkillDefinition skill,
        Vector3 spawnPosition,
        Vector3 direction)
    {
        if (skill == null || skill.projectile == null) return null;

        var go = new GameObject($"SwordQiProjectile_{skill.skillKey}");
        go.transform.position = spawnPosition;

        var projectile = go.AddComponent<SkyPrisonSwordQiProjectile>();
        projectile.Initialize(owner, ownerActionModule, skill, direction);
        return projectile;
    }

    private void Initialize(GameObject owner, UnitActionModuleRuntime ownerActionModule, SkillDefinition skill, Vector3 direction)
    {
        _owner = owner;
        _ownerActionModule = ownerActionModule;
        _skill = skill;
        _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;

        SkillProjectileData data = skill.projectile;
        _speed = Mathf.Max(0.01f, data.speed);
        _hitRadius = Mathf.Max(0.01f, data.hitRadius);
        _destroyOnHit = data.destroyOnHit;
        _dieAt = Time.time + Mathf.Max(0.05f, data.maxLifetimeSeconds);

        // 纯触发器检测，用kinematic Rigidbody保证OnTriggerEnter稳定触发（不依赖对方
        // 那边是否有Rigidbody），移动全靠Update()里手动改transform.position，不吃物理力。
        // 2026-07-21：光靠触发器逐帧重叠判定，速度快/帧率低时可能一帧内直接穿过目标
        // (两帧之间没有任何一帧真正跟目标重叠过，OnTriggerEnter完全不会触发)。Update()
        // 里额外做一次 SphereCast 扫掠这一帧实际走过的路径作为保险，两条检测路径共用
        // 同一份 TryProcessHit 命中处理逻辑，不会重复结算(_hitAlready去重)。
        var col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = _hitRadius;

        var rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        if (data.projectileVfx != null)
        {
            _vfxHandle = EffekseerSystem.PlayEffect(data.projectileVfx, transform.position);

            // 朝向：按实际飞行方向的角度摆一次——具体0度是不是等于"朝右"这个特效自己
            // 的默认朝向没法在这里确认(得在Effekseer Studio里实测，见
            // project_charge_dash_vfx_calibration教训)，先用这个公式打底，角度看着不
            // 对的话把 rotationOffsetDegrees 这个常量改一下就行，不用改公式本身。
            const float rotationOffsetDegrees = 0f;
            float angleDeg = Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg + rotationOffsetDegrees;
            _vfxHandle.SetRotation(Quaternion.Euler(0f, 0f, angleDeg));

            // 镜像：跟挥剑特效/蓄力突刺特效同一套约定——世界+X方向(面右/facing==-1)时
            // 对Y轴取负做真正的几何翻转(不是单纯转个角度)，因为特效里的装饰粒子造型
            // 往往是不对称的手性图形，光转角度转不出镜像版本。弹幕这里"朝哪边飞"就是
            // 判断依据，跟角色facing的判断条件是同一个方向(世界+X)。
            Vector3 scale = data.projectileVfxScale;
            if (_direction.x > 0f)
                scale = new Vector3(scale.x, -scale.y, scale.z);
            _vfxHandle.SetScale(scale);

            int layer = LayerMask.NameToLayer("World3D");
            _vfxHandle.layer = layer < 0 ? 0 : layer;
        }
    }

    private void Update()
    {
        if (Time.time >= _dieAt)
        {
            DestroySelf();
            return;
        }

        Vector3 previousPosition = transform.position;
        float step = _speed * Time.deltaTime;
        Vector3 nextPosition = previousPosition + _direction * step;

        // 先扫掠这一帧实际要走的这一小段路径，命中的话直接把位置摁到命中点——不会
        // "跑到命中点前面去"，位置始终贴着弹幕实体本身，不会离得很远。扫掠只覆盖这
        // 一帧的移动距离(step)，找到的命中点必然就在这一帧的起点和终点之间。
        // 显式传 QueryTriggerInteraction.Collide——UnitCombatHurtbox 的碰撞体是
        // isTrigger=true(见UnitCombatHurtbox.Awake)，不显式指定的话会跟着项目的
        // Physics设置(Queries Hit Triggers)走，万一那边是关的，这次扫掠会直接跳过
        // 所有受击框，等于白扫。
        int hitCount = Physics.SphereCastNonAlloc(
            previousPosition, _hitRadius, _direction, SweepHitsBuffer, step,
            Physics.AllLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hitCount; i++)
        {
            Collider candidate = SweepHitsBuffer[i].collider;
            if (candidate == null) continue;

            if (TryProcessHit(candidate, SweepHitsBuffer[i].point))
            {
                // Destroy() 是延迟到这一帧结束才真正销毁的，这里还能正常访问 this——
                // 用 _destroyOnHit 直接判断要不要提前结束，不用等着看组件是不是"已经
                // 没了"（那个检测在同一帧内根本还没生效）。
                if (_destroyOnHit) return;
                break; // 命中后没销毁(可穿透)，继续正常飞行；只在这一帧内处理一次命中。
            }
        }

        transform.position = nextPosition;
        if (_vfxHandle.exists)
            _vfxHandle.SetLocation(transform.position);
    }

    private void OnTriggerEnter(Collider other)
    {
        // 保底：正常情况下 Update() 里的 SphereCast 扫掠应该已经先一步抓到命中，这里
        // 命中点直接用弹幕当前位置(transform.position)，不会跟扫掠版本对不上太多。
        if (other == null) return;
        TryProcessHit(other, transform.position);
    }

    /// <summary>命中判定+伤害结算共用逻辑，SphereCast 扫掠和 OnTriggerEnter 触发器
    /// 两条检测路径都调这个方法，靠 _hitAlready 去重，不会同一个目标结算两次。
    /// hitPoint 只用来生成命中特效的位置，不影响伤害结算本身。返回 true 表示这次
    /// 命中被处理了（不代表弹幕一定被销毁——destroyOnHit=false 时可以继续飞）。</summary>
    private bool TryProcessHit(Collider other, Vector3 hitPoint)
    {
        if (_owner != null && other.transform.IsChildOf(_owner.transform)) return false;
        if (_hitAlready.Contains(other)) return false;

        UnitCombatHurtbox hurtbox =
            other.GetComponent<UnitCombatHurtbox>()
         ?? other.GetComponentInParent<UnitCombatHurtbox>(true);

        UnitHealthController health = hurtbox != null
            ? hurtbox.HealthController
            : other.GetComponent<UnitHealthController>()
           ?? other.GetComponentInParent<UnitHealthController>(true);

        if (health == null) return false;
        if (!UnitCombatHitbox.AreHostile(_owner, health.gameObject)) return false;

        UnitDeathController targetDeath =
            health.GetComponent<UnitDeathController>()
         ?? health.GetComponentInParent<UnitDeathController>(true);
        if (targetDeath != null && targetDeath.IsDeadLike) return false;

        UnitActionController targetAction =
            health.GetComponent<UnitActionController>()
         ?? health.GetComponentInParent<UnitActionController>(true);
        if (targetAction != null && targetAction.IsDodging) return false;

        _hitAlready.Add(other);
        _ownerActionModule?.ResolveSkillHit(_skill, other);

        if (_skill.projectile.impactVfx != null)
        {
            EffekseerHandle impact = EffekseerSystem.PlayEffect(_skill.projectile.impactVfx, hitPoint);
            int layer = LayerMask.NameToLayer("World3D");
            impact.layer = layer < 0 ? 0 : layer;
        }

        if (_destroyOnHit)
            DestroySelf();

        return true;
    }

    private void DestroySelf()
    {
        if (_vfxHandle.exists)
            _vfxHandle.Stop();
        Destroy(gameObject);
    }
}
