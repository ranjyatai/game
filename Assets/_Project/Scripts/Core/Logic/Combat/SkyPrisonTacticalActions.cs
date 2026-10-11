using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通用战术动作：给 AI 词条调用的"开始做一件事"类动作，每个动作都有前摇/位移/硬直，做完自己结束。
///   · LeapOver   跃过目标头顶，落到背后，落地范围伤害；
///   · FlankRunBy 贴着另一条深度线奔跑，越过目标一段距离算绕到背后；
///   · ClawCombo  近身连击（几下、间隔、范围、固定伤害、击退、硬直）；
///   · Evade      朝远离某点的方向做一次真闪避（有无敌帧，时长手动指定——怪物骨架没有闪避动画）。
/// 同一时间只做一个动作（IsBusy）；AI 词条要配"单位正在执行动作"条件，防止规则每个间隔重复触发。
/// 伤害是固定整数（走 SkyPrisonAIDash.ApplyFlatDamage，跟冲刺词条同一口径：防御/抗性/盾/闪避无敌/异常累积），
/// 命中范围内所有与自己敌对的存活单位。位移走 StartChargeDash / RequestDodge，不是闪避的动作没有无敌。
/// 摆在场景里的单位：组件引用在用到时补取。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonTacticalActions : MonoBehaviour
{
    [Header("落地扬尘（跃过头顶落地时，可选）")]
    [SerializeField] private GameObject[] landingDustPrefabs;
    [SerializeField] private float landingDustScale = 3.2f;
    [SerializeField] private float landingDustLifetime = 4f;

    private UnitMovementController _movement;
    private UnitActionController _action;
    private UnitDeathController _death;
    private bool _refsReady;
    private bool _busy;
    private Coroutine _running;

    public bool IsBusy => _busy;

    private bool EnsureRefs()
    {
        if (_refsReady) return true;
        _movement = GetComponentInChildren<UnitMovementController>(true);
        if (_movement == null) return false;
        _action = GetComponentInChildren<UnitActionController>(true);
        _death = GetComponentInChildren<UnitDeathController>(true);
        _movement.ConfigureAsExternalExecutor(false);
        _refsReady = true;
        return true;
    }

    private bool Dead => _death != null && _death.IsDeadLike;

    private bool Begin(IEnumerator routine)
    {
        if (_busy || Dead || !EnsureRefs()) return false;
        _busy = true;
        _running = StartCoroutine(Wrap(routine));
        return true;
    }

    private IEnumerator Wrap(IEnumerator inner)
    {
        yield return inner;
        _busy = false;
        _running = null;
    }

    /// <summary>强制中断当前动作（回归、被拉走时用）。</summary>
    public void Cancel()
    {
        if (_running != null) StopCoroutine(_running);
        _running = null;
        _busy = false;
        if (_refsReady) SubmitMove(Vector2.zero, false);
    }

    // ── 跃过头顶 ──────────────────────────────────────────

    public struct LeapParams
    {
        public Vector3 targetPos;
        public float landBehind;      // 落点越过目标多远(米)
        public float flightSeconds;
        public float landRadius;
        public int damage;
        public string damageType;
        public float knockback;
        public float stun;
        public float recoverySeconds;
    }

    public bool LeapOver(LeapParams p) { return Begin(LeapRoutine(p)); }

    private IEnumerator LeapRoutine(LeapParams p)
    {
        SubmitMove(Vector2.zero, false);
        Vector3 self = transform.position;
        float side = Mathf.Sign(p.targetPos.x - self.x);
        if (side == 0f) side = 1f;
        Vector3 land = p.targetPos; land.x += side * p.landBehind; land.y = self.y;
        Vector2 dir = new Vector2(land.x - self.x, land.z - self.z);
        float dist = dir.magnitude;
        if (dist < 0.1f) yield break;

        float groundY = transform.position.y;
        _movement.SetFacingOverride(new Vector2(side, 0f));
        _movement.RequestJump();
        _movement.StartChargeDash(dir.normalized, dist, Mathf.Max(0.1f, p.flightSeconds));
        for (float t = 0f; t < p.flightSeconds; t += Time.deltaTime) { if (Dead) yield break; yield return null; }
        // 等真正落地再结算（跳跃物理的滞空弧线常比计时长，直接结算会比模型落地早）
        for (float g = 0f; (_movement.IsJumping || transform.position.y - groundY > 0.1f) && g < 1.4f; g += Time.deltaTime) { if (Dead) yield break; yield return null; }
        yield return new WaitForSeconds(0.08f);

        // 落地：范围伤害 + 扬尘
        HitAround(transform.position, p.landRadius, p.damage, p.damageType, p.knockback, p.stun);
        SpawnLandingDust(transform.position);
        _movement.SetFacingOverride(new Vector2(-side, 0f));          // 落到背后，转身面向目标
        yield return new WaitForSeconds(Mathf.Max(0f, p.recoverySeconds));
        _movement.ClearFacingOverride();
    }

    // ── 绕到背后（贴线奔跑）────────────────────────────────

    public bool FlankRunBy(System.Func<Vector3?> getTarget, float laneOffset, float passDistance, float maxSeconds, bool run)
    {
        return Begin(FlankRoutine(getTarget, laneOffset, passDistance, maxSeconds, run));
    }

    private IEnumerator FlankRoutine(System.Func<Vector3?> getTarget, float laneOffset, float passDistance, float maxSeconds, bool run)
    {
        Vector3? first = getTarget();
        if (!first.HasValue) yield break;
        float startSide = Mathf.Sign(transform.position.x - first.Value.x);
        if (startSide == 0f) startSide = 1f;
        float lane = Random.value < 0.5f ? -1f : 1f;
        float end = Time.time + Mathf.Max(0.5f, maxSeconds);

        while (Time.time < end && !Dead)
        {
            Vector3? tp = getTarget();
            if (!tp.HasValue) break;
            float relX = transform.position.x - tp.Value.x;
            if (Mathf.Sign(relX) != startSide && Mathf.Abs(relX) >= passDistance) break;       // 已经绕到背后

            float laneZ = tp.Value.z + lane * laneOffset;
            float dz = laneZ - transform.position.z;
            float moveZ = Mathf.Abs(dz) > 0.35f ? Mathf.Sign(dz) : 0f;
            SubmitMove(new Vector2(-startSide, moveZ), run);
            yield return null;
        }
        SubmitMove(Vector2.zero, false);
    }

    // ── 近身连击 ──────────────────────────────────────────

    public struct ClawParams
    {
        public Vector3 targetPos;
        public int hits;
        public float windup, interval, recovery;
        public float reach, radius;
        public int damage;
        public string damageType;
        public float knockback, stun;
    }

    public bool ClawCombo(ClawParams p) { return Begin(ClawRoutine(p)); }

    private IEnumerator ClawRoutine(ClawParams p)
    {
        SubmitMove(Vector2.zero, false);
        float dirX = Mathf.Sign(p.targetPos.x - transform.position.x);
        if (dirX == 0f) dirX = 1f;
        _movement.SetFacingOverride(new Vector2(dirX, 0f));
        int hits = Mathf.Max(1, p.hits);
        for (int i = 0; i < hits; i++)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, p.windup));
            if (Dead) yield break;
            Vector3 c = transform.position + new Vector3(dirX * p.reach, 0f, 0f);
            HitAround(c, p.radius, p.damage, p.damageType, p.knockback, p.stun);
            if (i < hits - 1) yield return new WaitForSeconds(Mathf.Max(0f, p.interval));
        }
        yield return new WaitForSeconds(Mathf.Max(0f, p.recovery));
        _movement.ClearFacingOverride();
    }

    // ── 闪避 ──────────────────────────────────────────────

    public bool Evade(Vector3 awayFrom, float speedScale, float durationSeconds)
    {
        return Begin(EvadeRoutine(awayFrom, speedScale, durationSeconds));
    }

    private IEnumerator EvadeRoutine(Vector3 awayFrom, float speedScale, float durationSeconds)
    {
        SubmitMove(Vector2.zero, false);
        float away = Mathf.Sign(transform.position.x - awayFrom.x);
        if (away == 0f) away = 1f;
        _movement.SetFacingOverride(new Vector2(-away, 0f));          // 面朝对方倒着闪开
        _movement.RequestDodge(new Vector2(away, 0f), UnitMovementController.DodgeRuntimeState.Back, Mathf.Max(0.1f, speedScale));
        float wait = 0f;
        while (!_movement.IsDodging && wait < 0.2f) { wait += Time.deltaTime; yield return null; }
        _movement.ExtendDodgeLockDuration(Mathf.Max(0.1f, durationSeconds));
        float guard = 0f;
        while (_movement.IsDodging && guard < 1.5f) { guard += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(0.1f);
        _movement.ClearFacingOverride();
    }

    // ── 公共 ──────────────────────────────────────────────

    private void HitAround(Vector3 center, float radius, int damage, string type, float knockback, float stun)
    {
        if (damage <= 0 && knockback <= 0f && stun <= 0f) return;
        var all = FindObjectsOfType<UnitHealthController>();
        float r2 = radius * radius;
        for (int i = 0; i < all.Length; i++)
        {
            var h = all[i];
            if (h == null || h.IsDead) continue;
            var go = h.gameObject;
            if (go.transform == transform || go.transform.IsChildOf(transform) || transform.IsChildOf(go.transform)) continue;
            if (!UnitCombatHitbox.AreHostile(gameObject, go)) continue;
            Vector3 tp = h.transform.position;
            float dx = tp.x - center.x, dz = tp.z - center.z;
            if (dx * dx + dz * dz > r2) continue;
            if (Mathf.Abs(tp.y - transform.position.y) > 2f) continue;

            if (damage > 0) SkyPrisonAIDash.ApplyFlatDamage(gameObject, h, damage, type);
            if (h.IsDead) continue;
            var ac = h.GetComponent<UnitActionController>() ?? h.GetComponentInParent<UnitActionController>();
            if (ac != null && ac.IsDodging) continue;
            if (stun > 0f) ac?.ForceHitStun(stun);
            if (knockback > 0f)
            {
                var mv = h.GetComponent<UnitMovementController>() ?? h.GetComponentInParent<UnitMovementController>();
                float dirX = Mathf.Sign(tp.x - transform.position.x); if (dirX == 0f) dirX = 1f;
                mv?.ApplyKnockback(new Vector3(dirX * knockback, 0f, 0f));
            }
        }
    }

    private void SpawnLandingDust(Vector3 pos)
    {
        if (landingDustPrefabs == null || landingDustPrefabs.Length == 0) return;
        var vfx = FootstepVFXManager.Instance;
        if (vfx == null) return;
        int sorting = Mathf.RoundToInt(pos.z * 100f);
        for (int i = 0; i < landingDustPrefabs.Length; i++)
            if (landingDustPrefabs[i] != null)
                vfx.SpawnDust(new[] { landingDustPrefabs[i] }, pos, Quaternion.identity, landingDustScale, sorting, landingDustLifetime);
    }

    private void SubmitMove(Vector2 move, bool run)
    {
        if (_action != null) _action.SubmitMoveIntent(move, run, false);
        else if (_movement != null) _movement.SetExternalMoveInput(move, run);
    }
}
