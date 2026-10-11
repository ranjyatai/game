using UnityEngine;

/// <summary>
/// 单位自己处理"失衡"的方式(织面者击落/打断冲刺、咏唱者打断蓄势、颂赞者断连线)。没实现的单位走通用的 ForceHitStun。
/// </summary>
public interface IStaggerable
{
    /// <summary>完全霸体中(织面者地面冲刺飞行段)：失衡条满了也先不打断，等霸体结束；只有「破甲冲击」能强行打断。</summary>
    bool SuperArmored { get; }
    /// <summary>滞空中(织面者跳扑)：用更低的空中阈值。</summary>
    bool Airborne { get; }
    void Stagger(float seconds);
}

/// <summary>
/// 失衡条(用户 10-12 定，玩家看不见)：中级以上的 Memento 平时不吃每一下的小硬直(轻霸体)，
/// 受到的伤害 × 招式失衡系数累积进失衡条，满了才进入失衡(硬直)。低级怪(归巢者)不挂这个组件，照旧每下小硬直。
///   · 阈值 = 最大生命 × 比例(跟着等级血量自动拉开)；滞空时用空中比例。
///   · 2.5 秒没被打开始回落(每秒回落阈值的 25%)。
///   · 失衡后 3 秒免疫；下一次阈值 ×1.5，10 秒内没再失衡就恢复——同一只不能被连续打倒。
///   · 完全霸体中条满了先挂起，霸体结束立刻失衡；AFP「弱点特效」Lv3 破甲冲击(force) 可以直接打断。
/// 守卫者的盾吃掉的伤害不算进来(累积用的是实际造成的伤害)，所以想让守卫者失衡要绕背或先破盾。
/// 灰炉这类固定装置挂 immune 档：完全不吃失衡和小硬直；Boss(震碎者)的条很长、倒下时间长、不吃破甲冲击(打腿机制照旧并存)。
/// 由伤害结算(UnitActionModuleRuntime.ResolveSkillHit)按单位定义名自动挂上，不用手动配。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonStagger : MonoBehaviour
{
    public float thresholdFraction = 0.3f;
    public float airThresholdFraction = 0.3f;
    public float staggerSeconds = 1.2f;
    [Tooltip("完全免疫：不吃失衡，也不吃每一下的小硬直 / 击退(灰炉这类固定装置)。")]
    public bool immune;
    [Tooltip("不吃「破甲冲击」的强制打断(Boss)：只能老老实实把失衡条打满，而且出招中满了也要等招式放完。")]
    public bool ignoreForce;

    public const float DecayDelay = 2.5f;
    public const float DecayPerSecond = 0.25f;     // 阈值的比例
    public const float ImmuneSeconds = 3f;
    public const float EscalationStep = 1.5f;
    public const float EscalationResetSeconds = 10f;

    private UnitHealthController _health;
    private UnitActionController _action;
    private IStaggerable _receiver;
    private float _accum;
    private float _lastHit = -999f;
    private float _immuneUntil = -999f;
    private float _lastStagger = -999f;
    private float _escalation = 1f;
    private bool _pending;          // 霸体中条满了，等霸体结束

    // ── 按单位定义名套用档位(null = 不挂：低级怪照旧每下小硬直；Boss / 灰炉另有机制) ──
    struct Profile { public float frac, air, secs; public bool immune, noForce; public Profile(float f, float a, float s, bool imm = false, bool nf = false) { frac = f; air = a; secs = s; immune = imm; noForce = nf; } }

    static bool TryProfile(string defName, out Profile p)
    {
        p = default;
        if (string.IsNullOrEmpty(defName)) return false;
        string n = defName.ToLowerInvariant();
        // 比例按"对应装备等级的玩家集火约 2~3 秒打出一次失衡"定(10-12 按装备期望表重分配血量后重调)
        if (n.Contains("shatterer")) { p = new Profile(0.12f, 0.12f, 3.0f, false, true); return true; }   // Boss：条很长(18000 血 × 12% ≈ 2160，LV3 火力约 11 秒)，倒下时间长；不吃破甲冲击
        if (n.Contains("grayfurnace")) { p = new Profile(1f, 1f, 0f, true); return true; }     // 灰炉：固定装置，不吃失衡
        if (n.Contains("mirage")) { p = new Profile(0.45f, 0.45f, 1.0f); return true; }       // 怪人：被打中会现形，失衡后更好集火
        if (n.Contains("chanter")) { p = new Profile(0.45f, 0.45f, 1.0f); return true; }
        if (n.Contains("guardian")) { p = new Profile(0.40f, 0.40f, 1.2f); return true; }
        if (n.Contains("casterdelta")) { p = new Profile(0.30f, 0.30f, 1.5f); return true; }
        if (n.Contains("caster")) { p = new Profile(0.40f, 0.40f, 1.2f); return true; }
        if (n.Contains("faceweaver")) { p = new Profile(0.35f, 0.20f, 1.5f); return true; }
        return false;
    }

    /// <summary>取目标身上的失衡组件；没有就按单位定义名决定要不要挂。返回 null = 这个单位不走失衡条。</summary>
    public static SkyPrisonStagger For(UnitHealthController target)
    {
        if (target == null) return null;
        var binder = target.GetComponentInParent<UnitDefinitionRuntimeBinder>(true);
        Transform root = binder != null ? binder.transform : target.transform;
        var s = root.GetComponent<SkyPrisonStagger>();
        if (s != null) return s;
        var def = binder != null ? binder.UnitDefinitionAsset : null;
        if (def == null || def.characterIdentity == CharacterIdentity.Player) return null;
        if (!TryProfile(def.name, out Profile p)) return null;
        s = root.gameObject.AddComponent<SkyPrisonStagger>();
        s.thresholdFraction = p.frac; s.airThresholdFraction = p.air; s.staggerSeconds = p.secs; s.immune = p.immune; s.ignoreForce = p.noForce;
        return s;
    }

    private void EnsureRefs()
    {
        if (_health == null) _health = GetComponentInChildren<UnitHealthController>(true);
        if (_action == null) _action = GetComponentInChildren<UnitActionController>(true);
        if (_receiver == null) _receiver = GetComponentInChildren<IStaggerable>(true);
    }

    float Threshold
    {
        get
        {
            float max = _health != null ? _health.MaxHealth : 100f;
            float frac = _receiver != null && _receiver.Airborne ? airThresholdFraction : thresholdFraction;
            return Mathf.Max(1f, max * frac * _escalation);
        }
    }

    /// <summary>累积失衡值。force = 破甲冲击：无视霸体、无视免疫期也能打断。返回 true = 这一下打出了失衡。</summary>
    public bool AddStagger(float amount, bool force)
    {
        EnsureRefs();
        if (immune || amount <= 0f || _health == null || _health.IsDead) return false;
        if (ignoreForce) force = false;
        if (Time.time < _immuneUntil && !force) return false;
        _lastHit = Time.time;
        _accum += amount;
        if (_accum < Threshold) return false;
        if (_receiver != null && _receiver.SuperArmored && !force) { _accum = Threshold; _pending = true; return false; }
        DoStagger();
        return true;
    }

    private void DoStagger()
    {
        _accum = 0f;
        _pending = false;
        _immuneUntil = Time.time + ImmuneSeconds;
        _escalation = Time.time - _lastStagger < EscalationResetSeconds ? _escalation * EscalationStep : EscalationStep;
        _lastStagger = Time.time;
        SkyPrisonCombatLog.OnStagger(this);
        if (_receiver != null) _receiver.Stagger(staggerSeconds);
        else if (_action != null) _action.ForceHitStun(staggerSeconds);
    }

    private void Update()
    {
        EnsureRefs();
        if (_pending && (_receiver == null || !_receiver.SuperArmored) && _health != null && !_health.IsDead) DoStagger();
        if (_accum > 0f && Time.time - _lastHit > DecayDelay) _accum = Mathf.Max(0f, _accum - Threshold * DecayPerSecond * Time.deltaTime);
        if (_escalation > 1f && Time.time - _lastStagger > EscalationResetSeconds) _escalation = 1f;
    }
}
