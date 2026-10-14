using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 守卫者的护卫特质：附近有咏唱者、灰炉或颂赞者（需要被保护的 Memento）时，倾向于待在它们身边当"盾"，
/// 而不是自己去追玩家。
///   · 护卫对象由策划**明确认定**：优先用 designatedProtectees（组件上拖场景里的单位，或运行时 SetProtectee 指定），
///     有指定就只护卫它们（取其中存活且最近的）；一个都没指定才退回"按单位定义名关键字找附近的 Caster/GrayFurnace"兜底；
///   · 护卫站位：站在"护卫对象与玩家之间"、靠玩家那一侧 guardDistance 米处，深度跟护卫对象对齐，面朝玩家；
///     多个守卫者护卫同一个对象时按实例错开深度，不叠在一起；
///   · 护卫期间暂停 AI 包（追击/近战那套会跟这里抢移动指令），解除护卫时再恢复；
///   · 解除护卫(回到普通攻击AI)：玩家贴到近战距离(engageRadius)、或自己最近被打、或护卫对象没了/死了；
///   · 拉扯上限：追击时离护卫对象超过 leashRadius 且玩家不在近身范围，就回去，避免被玩家一路拖走；
///   · 玩家离开后(超过 releaseRadius)重新回到护卫。
/// 移动走 UnitActionController.SubmitMoveIntent（跟织面者/咏唱者同一条通道）。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonGuardianEscort : MonoBehaviour
{
    [Header("护卫对象（明确认定）")]
    [Tooltip("指定要护卫的单位（拖场景里的单位根物体）。有指定就只护卫它们，取存活且最近的；留空才走下面的关键字兜底。")]
    [SerializeField] private Transform[] designatedProtectees;

    [Header("护卫对象（兜底：没指定时按名字找）")]
    [Tooltip("搜索护卫对象的半径(米)。")]
    [SerializeField] private float escortSearchRadius = 14f;
    [Tooltip("要护卫的单位定义名里包含这些关键字之一就算（不区分大小写）。")]
    [SerializeField] private string[] protecteeNameKeywords = { "Caster", "GrayFurnace", "Chanter" };

    [Header("站位")]
    [Tooltip("站在护卫对象朝玩家那一侧多远(米)。")]
    [SerializeField] private float guardDistance = 2.6f;
    [Tooltip("同一个护卫对象有多个守卫者时，按实例错开的深度(米)。")]
    [SerializeField] private float laneSpread = 1.2f;
    [Tooltip("到位容差(米)：护卫对象(咏唱者)自己也会走动调整位置，容差太小守卫者就会一直追着它走、脚步声不停。起步阈值是它的 1.6 倍，停下阈值是 0.5 倍。")]
    [SerializeField] private float arriveTolerance = 1.2f;
    [Tooltip("玩家要越过护卫对象这么远(米)才算换到了另一侧，守卫者才跟着换边，避免玩家在对象旁边晃一下就来回换位。")]
    [SerializeField] private float sideSwitchMargin = 2f;

    [Tooltip("离护卫点超过这个距离(米)就奔跑过去抢到咏唱者前面，近了再切回走路站定。")]
    [SerializeField] private float runWhenFartherThan = 5f;

    [Header("护卫 ↔ 出击")]
    [Tooltip("玩家离守卫者这么近(米)就放开护卫，交给 AI 包近战。")]
    [SerializeField] private float engageRadius = 4.5f;
    [Tooltip("玩家离开守卫者这么远(米)后，才重新回到护卫。（比 engageRadius 大，形成回差）")]
    [SerializeField] private float releaseRadius = 9f;
    [Tooltip("追击时离护卫对象最远能跑多远(米)，超出且玩家不在近身范围就回去。")]
    [SerializeField] private float leashRadius = 11f;
    [Tooltip("被打之后这段时间(秒)内一定出击，不傻站着挨打。")]
    [SerializeField] private float provokedSeconds = 3f;
    [Tooltip("两次切换之间的最短间隔(秒)，防止在边界上来回抖。")]
    [SerializeField] private float minSwitchSeconds = 1.2f;

    private UnitMovementController _movement;
    private UnitActionController _action;
    private UnitHealthController _health;
    private UnitDeathController _death;
    private Behaviour _aiPackage;                 // AIBehaviorPackageRuntimeController
    private bool _refsReady;

    private Transform _protectee;
    private float _nextScan;
    private bool _escorting;
    private float _lastSwitch = -999f;
    private float _lastHurt = -999f;
    private float _laneSeed;
    private bool _moving;
    private bool _running;
    private float _nextDecide;          // 移动方向下一次允许重新决定的时间（防止每帧在 走/停 之间来回翻）
    private Vector2 _cmd;
    private float _side = 0f;           // 当前站在护卫对象的哪一侧（朝向玩家那边），带迟滞

    /// <summary>运行时指定护卫对象（给 AI 词条/触发器用）。传 null 清除指定，回到关键字兜底。</summary>
    public void SetProtectee(Transform protectee)
    {
        designatedProtectees = protectee != null ? new[] { protectee } : null;
        _protectee = protectee;
        _nextScan = 0f;
    }

    public bool HasDesignatedProtectee
    {
        get
        {
            if (designatedProtectees == null) return false;
            for (int i = 0; i < designatedProtectees.Length; i++) if (designatedProtectees[i] != null) return true;
            return false;
        }
    }

    public bool IsEscorting => _escorting;
    public Transform Protectee => _protectee;

    private void EnsureRefs()
    {
        if (_refsReady) return;
        _movement = GetComponentInChildren<UnitMovementController>(true);
        if (_movement == null) return;            // 摆在场景里的单位：子组件运行时才创建，下一帧再试
        _action = GetComponentInChildren<UnitActionController>(true);
        _health = GetComponentInChildren<UnitHealthController>(true);
        _death = GetComponentInChildren<UnitDeathController>(true);
        _aiPackage = GetComponentInChildren<AIBehaviorPackageRuntimeController>(true);
        if (_health != null) _health.OnDamaged += OnDamaged;
        _laneSeed = (GetInstanceID() & 0xFF) / 255f * 2f - 1f;     // -1~1，每个实例固定
        _refsReady = true;
    }

    private void Awake() { EnsureRefs(); }
    private void Start() { EnsureRefs(); }

    private void OnDestroy()
    {
        if (_health != null) _health.OnDamaged -= OnDamaged;
    }

    private void OnDamaged(UnitHealthController h, float amount) { _lastHurt = Time.time; }

    private void Update()
    {
        EnsureRefs();
        if (!_refsReady) return;
        if (_death != null && _death.IsDeadLike) { SetEscorting(false); return; }

        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) { SetEscorting(false); return; }

        if (Time.time >= _nextScan)
        {
            _nextScan = Time.time + 0.5f;
            _protectee = FindProtectee();
        }

        if (_protectee == null) { SetEscorting(false); return; }       // 没有护卫对象：完全交给 AI 包

        Vector3 self = transform.position;
        Vector3 pp = player.transform.position;
        float distToPlayer = Mathf.Sqrt((pp.x - self.x) * (pp.x - self.x) + (pp.z - self.z) * (pp.z - self.z));
        float distToProtectee = Mathf.Sqrt((_protectee.position.x - self.x) * (_protectee.position.x - self.x) + (_protectee.position.z - self.z) * (_protectee.position.z - self.z));
        bool provoked = Time.time - _lastHurt < provokedSeconds;

        // 状态切换（带回差 + 最短间隔）
        if (Time.time - _lastSwitch >= minSwitchSeconds)
        {
            if (_escorting)
            {
                if (distToPlayer <= engageRadius || provoked) SetEscorting(false);
            }
            else
            {
                bool tooFarFromProtectee = distToProtectee > leashRadius && distToPlayer > engageRadius;
                if (!provoked && (distToPlayer >= releaseRadius || tooFarFromProtectee)) SetEscorting(true);
            }
        }

        if (_escorting) TickEscort(pp);
    }

    // ── 护卫站位 ───────────────────────────────────────────

    private void TickEscort(Vector3 playerPos)
    {
        Vector3 prot = _protectee.position;
        float relX = playerPos.x - prot.x;
        if (_side == 0f) _side = relX >= 0f ? 1f : -1f;
        else if (Mathf.Sign(relX) != _side && Mathf.Abs(relX) > sideSwitchMargin) _side = Mathf.Sign(relX);   // 越过一段距离才换边
        float side = _side;
        float tx = prot.x + side * guardDistance;
        float tz = prot.z + _laneSeed * laneSpread;

        Vector3 self = transform.position;
        float dx = tx - self.x, dz = tz - self.z;
        // 面朝玩家（离得近时不翻朝向，避免抖）
        float toPlayerX = playerPos.x - self.x;
        if (Mathf.Abs(toPlayerX) > 1.0f) _movement.SetFacingOverride(new Vector2(Mathf.Sign(toPlayerX), 0f));

        // 迟滞：没在走时要偏出 enter 才起步，在走时要进到 leave 内才停下，两个阈值不同，边界上不会来回抖
        float enter = arriveTolerance * 1.6f;
        float leave = arriveTolerance * 0.5f;
        float thr = _moving ? leave : enter;
        bool far = Mathf.Abs(dx) > thr || Mathf.Abs(dz) > thr;
        if (!far)
        {
            if (_moving) { SubmitMove(Vector2.zero, false); _moving = false; _running = false; _cmd = Vector2.zero; }
            return;
        }

        // 方向每 0.25 秒才重新定一次（走路速度慢，没必要每帧重算）
        if (!_moving || Time.time >= _nextDecide)
        {
            _nextDecide = Time.time + 0.25f;
            _cmd = new Vector2(Mathf.Abs(dx) > leave ? Mathf.Sign(dx) : 0f, Mathf.Abs(dz) > leave ? Mathf.Sign(dz) : 0f);
        }
        // 离护卫点远就跑（带迟滞：跑起来后要进到 0.6 倍距离才切回走）
        float farDist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
        _running = _running ? farDist > runWhenFartherThan * 0.6f : farDist > runWhenFartherThan;
        SubmitMove(_cmd, _running);
        _moving = true;
    }

    private void SetEscorting(bool on)
    {
        if (_escorting == on) return;
        _escorting = on;
        _lastSwitch = Time.time;
        // 护卫期间 AI 包暂停（它的追击/近战会抢移动指令）；出击时恢复
        if (_aiPackage != null) _aiPackage.enabled = !on;
        if (on)
        {
            _movement.ConfigureAsExternalExecutor(false);
        }
        else
        {
            if (_moving) SubmitMove(Vector2.zero, false);
            _moving = false;
            _running = false;
            _movement?.ClearFacingOverride();
        }
    }

    private void SubmitMove(Vector2 move, bool run)
    {
        if (_action != null) _action.SubmitMoveIntent(move, run, false);
        else if (_movement != null) _movement.SetExternalMoveInput(move, run);
    }

    // ── 找护卫对象 ─────────────────────────────────────────

    private Transform FindProtectee()
    {
        // 明确指定的优先：只在指定的里挑存活且最近的，不受搜索半径限制（策划认定的就是它）
        if (HasDesignatedProtectee)
        {
            Transform nearest = null;
            float nearestSqr = float.MaxValue;
            for (int i = 0; i < designatedProtectees.Length; i++)
            {
                var t = designatedProtectees[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                var hc = t.GetComponentInChildren<UnitHealthController>(true);
                if (hc != null && hc.IsDead) continue;
                Vector3 d = t.position - transform.position; d.y = 0f;
                if (d.sqrMagnitude < nearestSqr) { nearestSqr = d.sqrMagnitude; nearest = t; }
            }
            return nearest;
        }

        Transform best = null;
        float bestSqr = escortSearchRadius * escortSearchRadius;
        var all = FindObjectsOfType<UnitHealthController>();
        for (int i = 0; i < all.Length; i++)
        {
            var h = all[i];
            if (h == null || h.IsDead) continue;
            var root = h.gameObject;
            if (root.transform == transform || root.transform.IsChildOf(transform) || transform.IsChildOf(root.transform)) continue;
            if (UnitCombatHitbox.AreHostile(gameObject, root)) continue;           // 只护卫自己人
            var binder = h.GetComponent<UnitDefinitionRuntimeBinder>() ?? h.GetComponentInParent<UnitDefinitionRuntimeBinder>(true);
            string defName = binder != null && binder.UnitDefinitionAsset != null ? binder.UnitDefinitionAsset.name : null;
            if (string.IsNullOrEmpty(defName) || !MatchesProtectee(defName)) continue;
            Transform t = binder != null ? binder.transform : root.transform;
            Vector3 d = t.position - transform.position; d.y = 0f;
            float sqr = d.sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = t; }
        }
        return best;
    }

    private bool MatchesProtectee(string defName)
    {
        if (protecteeNameKeywords == null) return false;
        for (int i = 0; i < protecteeNameKeywords.Length; i++)
        {
            string k = protecteeNameKeywords[i];
            if (!string.IsNullOrEmpty(k) && defName.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }
}
