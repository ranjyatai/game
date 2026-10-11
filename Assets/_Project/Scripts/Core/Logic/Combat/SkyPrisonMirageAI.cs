using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 怪人(Mirage)的伏击 AI(用户 10-12 定)：配合光学迷彩 SkyPrisonOpticalCamo，打了就跑、换个位置再来。
///   · 潜行接近：迷彩中靠近；每一轮按 flankChance 决定绕到玩家背后，其余从正面摸过去——有绕背倾向，但不总是绕。
///   · 突袭：进入攻击距离后不现形，只在前摇期间出现一阵空气扭曲(用户 10-12：攻击不现形，只有受伤才现形)，然后打一击；从背后打中 ×backstabMultiplier，出手前在迷彩中再 ×ambushMultiplier。
///   · 谨慎：玩家正对着它且很近、玩家在攻击 / 蓄力、它刚被打中现形、短时间掉血较多 → 面朝玩家后退，重新隐身。
///   · 迷彩换位：后退之后按概率接一次迷彩高速冲刺，绕到侧面 / 背后(不是瞬移：一路跑过去，有扭曲残影和脚步声，玩家能跟)。
///   · 一直找不到背后的机会，过 frontalPatience 秒就从正面硬上，不会永远绕圈。
///   · 失衡(SkyPrisonStagger 调 Stagger)时打断当前行为并硬直，跑不掉——集火窗口。
/// 移动走 UnitActionController.SubmitMoveIntent；单位定义里 aiBehaviorPackage 必须为空，否则会抢移动指令。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonMirageAI : MonoBehaviour, IStaggerable
{
    [Header("招式")]
    [SerializeField] private SkillDefinition strikeSkill;
    [Tooltip("前摇(秒)：期间空气扭曲加强(不现形)，这是玩家唯一的出招提示。")]
    [SerializeField] private float strikeWindup = 0.45f;
    [SerializeField] private float strikeRecovery = 0.5f;
    [SerializeField] private float strikeRangeX = 1.6f;
    [SerializeField] private float strikeRangeZ = 0.9f;
    [SerializeField] private float strikeHitRadius = 1.7f;
    [Tooltip("从背后打中玩家的伤害倍率。")]
    [SerializeField] private float backstabMultiplier = 1.5f;
    [Tooltip("出手前处于迷彩中的伤害倍率(可与背刺叠加)。")]
    [SerializeField] private float ambushMultiplier = 1.3f;
    [Tooltip("背刺 / 迷彩偷袭时电磁异常累积的倍率(用户 10-12：攻击附带电磁，偷袭一次基本直接触发电磁异常 = 10 秒 LP 不回复)。")]
    [SerializeField] private float ambushBuildupMultiplier = 2.5f;

    [Header("接近")]
    [Tooltip("每一轮去绕背的概率。")]
    [SerializeField, Range(0f, 1f)] private float flankChance = 0.55f;
    [Tooltip("绕到玩家背后多远(米)。")]
    [SerializeField] private float flankDistance = 2.2f;
    [Tooltip("一直找不到机会，过这么久(秒)就从正面硬上。")]
    [SerializeField] private float frontalPatience = 4f;
    [Tooltip("玩家进入这个距离(米)才开始行动。")]
    [SerializeField] private float awareRange = 16f;

    [Header("谨慎 / 撤退")]
    [Tooltip("玩家正对着它、且在这个距离(米)内，就算'没机会'。")]
    [SerializeField] private float exposedRange = 4f;
    [SerializeField] private float retreatSecondsMin = 1.0f;
    [SerializeField] private float retreatSecondsMax = 1.5f;
    [Tooltip("1.5 秒内掉血超过最大生命的这个比例就撤退。")]
    [SerializeField] private float retreatDamageRatio = 0.08f;
    [Tooltip("撤退后接一次迷彩高速换位的概率。")]
    [SerializeField, Range(0f, 1f)] private float repositionChance = 0.4f;
    [Tooltip("换位冲刺的移速加成(moveSpeedRate 百分点，100 = 2 倍)。")]
    [SerializeField] private float repositionSpeedBonus = 100f;
    [SerializeField] private float repositionSeconds = 1.1f;

    private enum Mode { Idle, Approach, Strike, Retreat, Reposition }
    private Mode _mode = Mode.Idle;

    private UnitMovementController _movement;
    private UnitActionController _action;
    private UnitActionModuleRuntime _module;
    private UnitHealthController _health;
    private UnitDeathController _death;
    private UnitBattleStatRuntime _stats;
    private SkyPrisonOpticalCamo _camo;
    private bool _refsReady;

    private bool _flankThisRound;
    private float _roundStart;
    private float _modeUntil;
    private Vector2 _repositionTarget;
    private float _playerFacing = 1f;
    private Collider _playerCollider;
    private readonly List<float> _hurtTimes = new List<float>();
    private readonly List<float> _hurtAmounts = new List<float>();
    private Coroutine _routine;
    private SkillDefinition _skillBackstab, _skillAmbush, _skillBoth;

    // ── IStaggerable ──
    public bool SuperArmored => false;
    public bool Airborne => false;
    public void Stagger(float seconds)
    {
        StopRoutine();
        SetRepositionBoost(false);
        Move(Vector2.zero, false);
        if (_action != null) _action.ForceHitStun(seconds);
        if (_camo != null) _camo.Reveal(seconds + 0.4f);
        _mode = Mode.Retreat; _modeUntil = Time.time + seconds + Random.Range(retreatSecondsMin, retreatSecondsMax);
    }

    private void EnsureRefs()
    {
        if (_refsReady) return;
        _movement = GetComponentInChildren<UnitMovementController>(true);
        if (_movement == null) return;
        _action = GetComponentInChildren<UnitActionController>(true);
        _module = GetComponentInChildren<UnitActionModuleRuntime>(true);
        _health = GetComponentInChildren<UnitHealthController>(true);
        _death = GetComponentInChildren<UnitDeathController>(true);
        _stats = GetComponentInChildren<UnitBattleStatRuntime>(true);
        _camo = GetComponentInChildren<SkyPrisonOpticalCamo>(true);
        if (_health != null) _health.OnDamaged += OnDamaged;
        _movement.ConfigureAsExternalExecutor(false);
        _refsReady = true;
    }

    private void Awake() { EnsureRefs(); }
    private void Start() { EnsureRefs(); }

    private void OnDestroy()
    {
        if (_health != null) _health.OnDamaged -= OnDamaged;
        foreach (var s in new[] { _skillBackstab, _skillAmbush, _skillBoth }) if (s != null) Destroy(s);
    }

    private void OnDamaged(UnitHealthController h, float amount)
    {
        _hurtTimes.Add(Time.time); _hurtAmounts.Add(amount);
    }

    private bool Dead => _death != null && _death.IsDeadLike;

    private void Update()
    {
        EnsureRefs();
        if (!_refsReady) return;
        if (Dead) { StopRoutine(); SetRepositionBoost(false); return; }
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) { Move(Vector2.zero, false); return; }
        if (_action != null && _action.CurrentState == UnitActionController.UnitActionState.HitStun) { Move(Vector2.zero, false); return; }

        TrackPlayerFacing(player);
        Vector3 pp = player.transform.position, me = transform.position;
        float dx = pp.x - me.x, dz = pp.z - me.z;
        float dist = Mathf.Sqrt(dx * dx + dz * dz);

        switch (_mode)
        {
            case Mode.Idle:
                Move(Vector2.zero, false);
                if (dist <= awareRange) BeginRound();
                break;

            case Mode.Approach:
                if (ShouldRetreat(player, dx, dist)) { BeginRetreat(); break; }
                TickApproach(player, dx, dz);
                break;

            case Mode.Strike:
                break;      // 由协程推进

            case Mode.Retreat:
                // 面朝玩家后退，迷彩自己慢慢恢复(SkyPrisonOpticalCamo 现形计时结束后淡回)
                Face(Mathf.Sign(dx));
                Move(new Vector2(-Mathf.Sign(dx), Mathf.Abs(dz) < 1f ? (dz >= 0f ? -0.4f : 0.4f) : 0f), false);
                if (Time.time >= _modeUntil)
                {
                    if (Random.value < repositionChance) BeginReposition(player);
                    else BeginRound();
                }
                break;

            case Mode.Reposition:
                TickReposition();
                break;
        }
    }

    // ── 接近 ──
    private void BeginRound()
    {
        _mode = Mode.Approach;
        _roundStart = Time.time;
        _flankThisRound = Random.value < flankChance;
    }

    private void TickApproach(SkyPrisonUnitRuntimeIdentity player, float dx, float dz)
    {
        Vector3 pp = player.transform.position, me = transform.position;
        bool patienceOut = Time.time - _roundStart > frontalPatience;
        bool behind = IsBehindPlayer(me, pp);

        // 在攻击距离内：背后、正面但玩家没盯着、或者耐心用完 → 出手
        if (Mathf.Abs(dx) <= strikeRangeX && Mathf.Abs(dz) <= strikeRangeZ && (behind || !_flankThisRound || patienceOut))
        {
            _routine = StartCoroutine(StrikeRoutine(player));
            return;
        }

        Vector2 target;
        if (_flankThisRound && !patienceOut)
        {
            // 绕背：目标点在玩家背后 flankDistance，深度先错开一点再切回，避免直接穿过玩家正面
            float backX = pp.x - _playerFacing * flankDistance;
            bool needSwing = Mathf.Sign(me.x - pp.x) == _playerFacing && Mathf.Abs(me.x - pp.x) < flankDistance + 1.5f;
            float zOff = needSwing ? (me.z >= pp.z ? 1.6f : -1.6f) : 0f;
            target = new Vector2(backX, pp.z + zOff);
        }
        else
        {
            target = new Vector2(pp.x - Mathf.Sign(dx) * (strikeRangeX * 0.7f), pp.z);
        }
        Vector2 d = new Vector2(target.x - me.x, target.y - me.z);
        Vector2 cmd = new Vector2(Mathf.Abs(d.x) > 0.3f ? Mathf.Sign(d.x) : 0f, Mathf.Abs(d.y) > 0.3f ? Mathf.Sign(d.y) : 0f);
        ClearFace();
        Move(cmd, d.magnitude > 4f);
    }

    // ── 谨慎 ──
    private bool ShouldRetreat(SkyPrisonUnitRuntimeIdentity player, float dx, float dist)
    {
        // 短时间掉血较多
        float recent = 0f;
        for (int i = _hurtTimes.Count - 1; i >= 0; i--)
        {
            if (Time.time - _hurtTimes[i] > 1.5f) { _hurtTimes.RemoveAt(i); _hurtAmounts.RemoveAt(i); continue; }
            recent += _hurtAmounts[i];
        }
        if (_health != null && recent >= _health.MaxHealth * retreatDamageRatio) return true;

        bool playerFacesMe = Mathf.Sign(-dx) == _playerFacing;      // 怪人在玩家面朝的那一侧
        if (!playerFacesMe || dist > exposedRange) return false;
        // 玩家正对着它且很近：玩家在攻击 / 蓄力，或者自己已经现形 → 没机会
        var pa = player.GetComponentInChildren<UnitActionController>(true);
        var pm = player.GetComponentInChildren<UnitActionModuleRuntime>(true);
        bool playerThreat = (pa != null && pa.IsAttacking) || (pm != null && pm.IsChargeHeld);
        bool exposed = _camo != null && !_camo.Camouflaged;
        return playerThreat || exposed;
    }

    private void BeginRetreat()
    {
        _mode = Mode.Retreat;
        _modeUntil = Time.time + Random.Range(retreatSecondsMin, retreatSecondsMax);
    }

    // ── 迷彩换位：高速绕到侧面 / 背后 ──
    private void BeginReposition(SkyPrisonUnitRuntimeIdentity player)
    {
        Vector3 pp = player.transform.position;
        float side = Random.value < 0.6f ? -_playerFacing : _playerFacing;          // 六成去背后
        float zSide = Random.value < 0.5f ? -1f : 1f;
        _repositionTarget = new Vector2(pp.x + side * Random.Range(3f, 5f), pp.z + zSide * Random.Range(0.5f, 2f));
        _mode = Mode.Reposition;
        _modeUntil = Time.time + repositionSeconds;
        SetRepositionBoost(true);
    }

    private void TickReposition()
    {
        Vector3 me = transform.position;
        Vector2 d = new Vector2(_repositionTarget.x - me.x, _repositionTarget.y - me.z);
        if (d.magnitude < 0.6f || Time.time >= _modeUntil) { SetRepositionBoost(false); BeginRound(); return; }
        ClearFace();
        Move(new Vector2(Mathf.Abs(d.x) > 0.3f ? Mathf.Sign(d.x) : 0f, Mathf.Abs(d.y) > 0.3f ? Mathf.Sign(d.y) : 0f), true);
    }

    private void SetRepositionBoost(bool on)
    {
        if (_stats == null) return;
        if (on) _stats.SetExternalModifiers("MirageReposition", new List<StatModifier> { new StatModifier("moveSpeedRate", repositionSpeedBonus, StatModifierOp.Add) });
        else _stats.ClearExternalModifiers("MirageReposition");
    }

    // ── 突袭 ──
    private IEnumerator StrikeRoutine(SkyPrisonUnitRuntimeIdentity player)
    {
        _mode = Mode.Strike;
        Move(Vector2.zero, false);
        float dirX = Mathf.Sign(player.transform.position.x - transform.position.x); if (dirX == 0f) dirX = 1f;
        Face(dirX);
        bool wasCamo = _camo != null && _camo.Camouflaged;
        if (_camo != null) _camo.Ripple(strikeWindup + 0.1f, 0.7f);      // 出招不现形(用户 10-12)，前摇只有一阵空气扭曲作为提示

        yield return new WaitForSeconds(strikeWindup);
        if (Dead) yield break;

        bool behind = IsBehindPlayer(transform.position, player.transform.position);
        var skill = PickSkill(behind, wasCamo);
        Vector3 center = transform.position + new Vector3(dirX * strikeRangeX * 0.6f, 0f, 0f);
        TryHitPlayer(player, skill, center);

        yield return new WaitForSeconds(strikeRecovery);
        _routine = null;
        BeginRetreat();          // 打完就退，重新隐身
    }

    private SkillDefinition PickSkill(bool backstab, bool ambush)
    {
        if (strikeSkill == null) return null;
        if (!backstab && !ambush) return strikeSkill;
        if (backstab && ambush) return _skillBoth != null ? _skillBoth : (_skillBoth = Clone(backstabMultiplier * ambushMultiplier));
        if (backstab) return _skillBackstab != null ? _skillBackstab : (_skillBackstab = Clone(backstabMultiplier));
        return _skillAmbush != null ? _skillAmbush : (_skillAmbush = Clone(ambushMultiplier));
    }

    // 背刺 / 偷袭用的技能副本：物理伤害乘倍率，属性(电磁)只放大异常累积，不放大属性伤害
    private SkillDefinition Clone(float mult)
    {
        var s = Instantiate(strikeSkill);
        s.damageMultiplier = strikeSkill.damageMultiplier * mult;
        if (s.attributeHits != null)
        {
            var hits = new List<SkillAttributeHit>();
            foreach (var h in s.attributeHits)
            {
                if (h == null) continue;
                hits.Add(new SkillAttributeHit { attributeKey = h.attributeKey, attributeDamageMultiplier = h.attributeDamageMultiplier, anomalyBuildupMultiplier = h.anomalyBuildupMultiplier * ambushBuildupMultiplier });
            }
            s.attributeHits = hits;
        }
        return s;
    }

    private void TryHitPlayer(SkyPrisonUnitRuntimeIdentity player, SkillDefinition skill, Vector3 center)
    {
        if (player == null || skill == null || _module == null) return;
        Vector3 p = player.transform.position;
        float ddx = p.x - center.x, ddz = p.z - center.z;
        if (ddx * ddx + ddz * ddz > strikeHitRadius * strikeHitRadius) return;
        if (Mathf.Abs(p.y - transform.position.y) > 2f) return;
        if (_playerCollider == null) _playerCollider = player.GetComponentInChildren<Collider>();
        if (_playerCollider != null) _module.ResolveSkillHit(skill, _playerCollider);
    }

    // ── 工具 ──
    private bool IsBehindPlayer(Vector3 me, Vector3 pp) => Mathf.Sign(me.x - pp.x) == -_playerFacing && Mathf.Abs(me.x - pp.x) > 0.2f;

    private void TrackPlayerFacing(SkyPrisonUnitRuntimeIdentity player)
    {
        var pmv = player.GetComponentInChildren<UnitMovementController>(true);
        if (pmv == null) return;
        float fx = pmv.FacingInput.x;
        if (Mathf.Abs(fx) > 0.1f) _playerFacing = Mathf.Sign(fx);      // 停下时保留上一次的朝向
    }

    private void Move(Vector2 move, bool run)
    {
        if (_action != null) _action.SubmitMoveIntent(move, run, false);
        else if (_movement != null) _movement.SetExternalMoveInput(move, run);
    }

    private void Face(float dirX) { if (_movement != null && dirX != 0f) _movement.SetFacingOverride(new Vector2(dirX, 0f)); }
    private void ClearFace() { if (_movement != null) _movement.ClearFacingOverride(); }

    private void StopRoutine()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
    }
}
