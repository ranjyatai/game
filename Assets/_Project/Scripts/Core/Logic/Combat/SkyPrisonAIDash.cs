using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 词条"冲刺"的执行体：沿一条直线冲出去，对沿途碰到的敌对单位各造成一次固定整数伤害。
/// 冲刺方向三种来源（由词条决定）：
///   · 到对象位置：直接冲向某个点/单位的当前位置，到那里为止（可限制最大距离）；
///   · 朝对象方向：只取"朝向某个单位/点位"的方向，冲固定距离（可以冲过头）；
///   · 按角度：世界水平面上的角度，0°=朝 +X(右)，90°=朝 +Z(深处)，逆时针为正。
/// 位移走 UnitMovementController.StartChargeDash（不是闪避，没有无敌）；
/// 伤害是固定值：物理类型(slash/strike/impact)吃防御和对应抗性，属性类型只吃对应属性抗性，
/// 并带同等数值的异常累计（跟技能命中的口径一致）；目标正在闪避则无敌躲过；
/// 目标身上有 IIncomingHitModifier（守卫者的盾）时同样会被它拦截/减伤。
/// 冲击：击退力沿 X 轴把目标推开、硬直让目标一段时间无法行动（都是 0 则只有伤害）。
/// 穿透(pierce)：开=一路冲过去，沿途每个敌人各打一次；关=撞到第一个敌人就当场停下，只打那一个。
/// 冲刺中再次触发会被忽略（避免 AI 每帧重复触发）。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonAIDash : MonoBehaviour
{
    public struct Request
    {
        public Vector2 direction;        // 世界 XZ 平面上的单位方向
        public float distance;
        public float duration;
        public int damage;
        public string damageType;        // slash/strike/impact/heat/shock/corrosion/freeze
        public float hitRadius;
        public float knockback;          // 击退力（沿 X 轴把目标推开，0=不击退，数值口径同技能的 knockbackForce）
        public float stun;               // 硬直秒数（目标期间无法行动，0=无）。项目里单位没有独立的"倒地"状态，长硬直就是击倒的近似
        public bool pierce;              // true=穿透：一路冲过去、沿途每个敌人各打一次；false=撞到第一个就停
    }

    [SerializeField] private float defaultHitRadius = 1.1f;

    private UnitMovementController _movement;
    private bool _dashing;
    private bool _configured;

    public bool IsDashing => _dashing;
    public float DefaultHitRadius => defaultHitRadius;

    private void Awake()
    {
        _movement = GetComponentInChildren<UnitMovementController>(true);
    }

    /// <summary>开始冲刺。正在冲刺、或没有移动控制器时返回 false。</summary>
    public bool Begin(Request req)
    {
        if (_dashing || _movement == null) return false;
        if (!_configured) { _movement.ConfigureAsExternalExecutor(false); _configured = true; }   // 冲刺位移要走外部驱动模式；AI 包驱动的单位本来就是，这里只是兜底
        if (req.direction.sqrMagnitude < 0.0001f || req.distance <= 0.01f) return false;
        StartCoroutine(Run(req));
        return true;
    }

    private IEnumerator Run(Request req)
    {
        _dashing = true;
        Vector2 dir = req.direction.normalized;
        float duration = Mathf.Max(0.05f, req.duration);
        float radius = req.hitRadius > 0.01f ? req.hitRadius : defaultHitRadius;

        if (Mathf.Abs(dir.x) > 0.05f) _movement.SetFacingOverride(new Vector2(Mathf.Sign(dir.x), 0f));
        _movement.StartChargeDash(dir, req.distance, duration);

        // 候选目标冲刺开始时筛一次：存活、与自己敌对
        var candidates = new List<UnitHealthController>();
        var all = FindObjectsOfType<UnitHealthController>();
        for (int i = 0; i < all.Length; i++)
        {
            var h = all[i];
            if (h == null || h.IsDead) continue;
            var go = h.gameObject;
            if (go.transform == transform || go.transform.IsChildOf(transform) || transform.IsChildOf(go.transform)) continue;
            if (!UnitCombatHitbox.AreHostile(gameObject, go)) continue;
            candidates.Add(h);
        }

        var hit = new HashSet<UnitHealthController>();
        float t = 0f;
        bool stopped = false;
        while (t < duration)
        {
            t += Time.deltaTime;
            Vector3 p = transform.position;
            for (int i = 0; i < candidates.Count; i++)
            {
                var h = candidates[i];
                if (h == null || h.IsDead || hit.Contains(h)) continue;
                Vector3 tp = h.transform.position;
                float dx = tp.x - p.x, dz = tp.z - p.z;
                if (dx * dx + dz * dz > radius * radius) continue;
                hit.Add(h);
                ApplyFlatDamage(gameObject, h, req.damage, req.damageType);
                ApplyImpact(h, req.knockback, req.stun);
                if (!req.pierce) { stopped = true; break; }
            }
            if (stopped) { _movement.CancelChargeDash(); break; }
            yield return null;
        }
        _dashing = false;
    }

    private void ApplyImpact(UnitHealthController target, float knockback, float stun)
    {
        if (target == null || target.IsDead) return;
        if (knockback <= 0f && stun <= 0f) return;
        var action = target.GetComponent<UnitActionController>() ?? target.GetComponentInParent<UnitActionController>();
        if (action != null && action.IsDodging) return;     // 闪避中无敌，击退/硬直也不吃

        if (stun > 0f) action?.ForceHitStun(stun);
        if (knockback > 0f)
        {
            var mv = target.GetComponent<UnitMovementController>() ?? target.GetComponentInParent<UnitMovementController>();
            if (mv != null)
            {
                float dirX = Mathf.Sign(target.transform.position.x - transform.position.x);
                if (dirX == 0f) dirX = 1f;
                mv.ApplyKnockback(new Vector3(dirX * knockback, 0f, 0f));
            }
        }
    }

    // ── 固定伤害 ───────────────────────────────────────────

    public static bool IsPhysicalType(string key) => key == "slash" || key == "strike" || key == "impact";

    public static void ApplyFlatDamage(GameObject source, UnitHealthController target, int amount, string typeKey)
    {
        if (target == null || target.IsDead || amount <= 0) return;
        if (string.IsNullOrWhiteSpace(typeKey)) typeKey = "strike";

        // 闪避中无敌
        var action = target.GetComponent<UnitActionController>() ?? target.GetComponentInParent<UnitActionController>();
        if (action != null && action.IsDodging) return;

        var stats = target.GetComponent<UnitBattleStatRuntime>() ?? target.GetComponentInParent<UnitBattleStatRuntime>();
        bool physical = IsPhysicalType(typeKey);
        float dmg = amount;
        if (physical) dmg *= UnitActionModuleRuntime.ResolveDefenseMultiplier(stats);
        dmg *= UnitActionModuleRuntime.ResolveResistMultiplier(stats, typeKey + "Resist");

        IncomingHitModifiers.Apply(target, physical, typeKey, source != null ? source.transform.position.x : 0f, ref dmg);
        if (dmg <= 0f) return;

        var bridge = target.GetComponent<UnitOverheadHealthBridge>() ?? target.GetComponentInChildren<UnitOverheadHealthBridge>(true);
        bridge?.ShowDamageNumberByKey(typeKey, dmg, false, false, false);
        target.ApplyDamage(dmg);

        if (!physical && !target.IsDead)
        {
            GameObject root = target.gameObject;
            var binder = target.GetComponent<UnitDefinitionRuntimeBinder>() ?? target.GetComponentInParent<UnitDefinitionRuntimeBinder>(true);
            if (binder != null) root = binder.gameObject;
            UnitAnomalyController.EnsureOnRoot(root)?.ApplyAccumulation(typeKey, dmg, source);
        }
    }
}
