using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 颂赞者(支援类 Memento)：自己不攻击、很脆，躲在同伴后面，持续「牵线」——
/// 被连上的同伴 Memento **大幅减伤(85%) + 狂暴**(攻击↑、防御↓、移速↑、行动速度↑)，不回血。
/// 设计目的：逼玩家先处理颂赞者——连着线的怪几乎打不动，颂赞者一死所有连线立刻断开。
///   · 连线：每 0.5 秒挑 linkRadius 内最近的 maxLinks 只同伴连上；已连的超过 breakRadius 才断(回差，不会在边界闪)；
///     breakOnWalls 打开时墙挡住也会断(默认关——不给玩家隔墙卡断线的取巧)。
///   · 减伤：被连的单位挂 SkyPrisonChanterLinkWard(IIncomingHitModifier)，多只颂赞者连同一只不叠加，取最大值。
///   · 狂暴：连线期间每 0.5 秒刷新 status_berserk(持续 berserkLingerSeconds)，断线后再残留这么久才消失。
///   · 不连：灰炉(固定遭遇)、守卫者(盾本身就有 90% 物理减伤，再叠就打不动)、其它颂赞者。
///   · 站位：在同伴集团背对玩家的一侧找个点站定；集团挪动超过 repositionDistance 才重新找点(不跟着玩家每一步平移)。
///     换边有冷却(sideSwitchCooldown)，玩家包抄能抓到窗口；没有同伴时就地站着，不跟玩家保持距离。
///     玩家贴近 panicRadius 时做一次短后撤(朝移动方向跑开 hopSeconds 秒)，有冷却，跳完就停下，不会一直逃。
/// 移动走 UnitActionController.SubmitMoveIntent（跟守卫者/织面者同一条通道）；单位定义里 aiBehaviorPackage 必须为空。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonChanterSupport : MonoBehaviour, IStaggerable
{
    [Header("牵线")]
    [Tooltip("开始连线的半径(米，水平)。")]
    [SerializeField] private float linkRadius = 16f;
    [Tooltip("已连上的同伴超过这个距离(米)才断线。比 linkRadius 大，形成回差。")]
    [SerializeField] private float breakRadius = 20f;
    [Tooltip("同时最多连几只。")]
    [SerializeField] private int maxLinks = 3;
    [Tooltip("墙挡住时是否断线。关=隔墙也连着(不给玩家卡墙断线)。")]
    [SerializeField] private bool breakOnWalls = false;
    [Tooltip("玩家进入这个距离(米)才开始连线。")]
    [SerializeField] private float awareRadius = 24f;

    [Tooltip("站定后吟唱多久(秒)连线才生效。吟唱和维持连线期间都不能移动。")]
    [SerializeField] private float castSeconds = 1.2f;

    [Header("效果")]
    [Tooltip("被连单位受到伤害的减免比例(0.85 = 只吃 15%)。")]
    [Range(0f, 0.95f)]
    [SerializeField] private float damageReduction = 0.85f;
    [Tooltip("狂暴状态(status_berserk)。")]
    [SerializeField] private StatusDefinition berserkStatus;
    [Tooltip("断线后狂暴再残留多久(秒)。")]
    [SerializeField] private float berserkLingerSeconds = 2f;
    [Tooltip("狂暴时 Spine 整体播放速度倍率(行动速度)。")]
    [SerializeField] private float berserkActionSpeed = 1.25f;

    [Header("站位")]
    [SerializeField] private float allySearchRadius = 18f;
    [Tooltip("站在集团背对玩家那一侧多远(米)。")]
    [SerializeField] private float backOffset = 4f;
    [Tooltip("集团中心挪动超过这个距离(米)才重新找站位点。")]
    [SerializeField] private float repositionDistance = 3f;
    [Tooltip("换到集团另一侧之后，至少隔这么久(秒)才会再换边。")]
    [SerializeField] private float sideSwitchCooldown = 6f;
    [SerializeField] private float arriveTolerance = 1.2f;
    [SerializeField] private float runWhenFartherThan = 5f;

    [Header("后撤")]
    [Tooltip("玩家贴近到这个距离(米)就后撤一次。")]
    [SerializeField] private float panicRadius = 4.5f;
    [SerializeField] private float hopSeconds = 0.55f;
    [SerializeField] private float hopCooldown = 5f;

    [Header("表现（占位）")]
    [SerializeField] private Color linkColor = new Color(1f, 0.78f, 0.32f, 0.85f);
    [SerializeField] private float linkWidth = 0.09f;

    private UnitMovementController _movement;
    private UnitActionController _action;
    private UnitDeathController _death;
    private bool _refsReady;

    private readonly List<Transform> _allies = new List<Transform>();
    private readonly List<Transform> _links = new List<Transform>();
    private readonly List<LineRenderer> _linkLines = new List<LineRenderer>();
    private Material _fxMat;
    private float _nextScan;
    private bool _aware;

    private Vector3 _anchor;
    private bool _hasAnchor;
    private Vector3 _standPoint;
    private Vector3 _standAnchor;
    private bool _hasStandPoint;
    private float _side;
    private float _nextSideSwitch;

    private bool _moving, _running;
    private float _nextDecide;
    private Vector2 _cmd;
    private float _hopUntil = -1f;
    private float _nextHop;
    private Vector2 _hopDir;

    // 施放：站定吟唱 castSeconds 秒后连线生效；连线期间站着不动(维持施放)，一移动就断开
    private bool _channelActive;
    private float _castEnd = -1f;
    private float _nextCastTry;
    private readonly List<Transform> _castTargets = new List<Transform>();

    public int LinkCount => _links.Count;
    public float DamageReduction => damageReduction;

    private void EnsureRefs()
    {
        if (_refsReady) return;
        _movement = GetComponentInChildren<UnitMovementController>(true);
        if (_movement == null) return;            // 摆在场景里的单位：子组件运行时才创建，下一帧再试
        _action = GetComponentInChildren<UnitActionController>(true);
        _death = GetComponentInChildren<UnitDeathController>(true);
        _movement.ConfigureAsExternalExecutor(false);
        _refsReady = true;
    }

    private void Awake() { EnsureRefs(); }
    private void Start() { EnsureRefs(); }

    private void OnDisable() { ClearLinks(); }

    private void OnDestroy()
    {
        ClearLinks();
        if (_fxMat != null) Destroy(_fxMat);
    }

    private void Update()
    {
        EnsureRefs();
        if (!_refsReady) return;
        if (_death != null && _death.IsDeadLike) { BreakChannel(); return; }   // 一死所有连线立刻断

        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) return;
        Vector3 pp = player.transform.position;
        float distToPlayer = FlatDist(pp, transform.position);
        if (!_aware && distToPlayer <= awareRadius) _aware = true;

        if (Time.time >= _nextScan)
        {
            _nextScan = Time.time + 0.5f;
            ScanAllies();
            if (_channelActive) { UpdateLinks(); RefreshLinkEffects(); }
        }

        TickMovement(pp, distToPlayer);
        TickLinkFx();
    }

    // ── 同伴 / 连线 ─────────────────────────────────────

    private void ScanAllies()
    {
        _allies.Clear();
        Vector3 sum = Vector3.zero;
        var all = FindObjectsByType<UnitHealthController>(FindObjectsSortMode.None);
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        for (int i = 0; i < all.Length; i++)
        {
            var h = all[i];
            if (h == null || h.IsDead) continue;
            var binder = h.GetComponentInParent<UnitDefinitionRuntimeBinder>(true);
            Transform root = binder != null ? binder.transform : h.transform;
            if (root == transform || root.IsChildOf(transform) || transform.IsChildOf(root)) continue;
            if (player != null && root == player.transform) continue;
            if (!UnitCombatHitbox.AreAllied(gameObject, root.gameObject)) continue;         // 只算同阵营的活物(不含箱子/中立 NPC)
            if (root.GetComponentInChildren<SkyPrisonGrayFurnace>(true) != null) continue;
            if (FlatDist(root.position, transform.position) > allySearchRadius) continue;
            _allies.Add(root);
            sum += root.position;
        }
        _hasAnchor = _allies.Count > 0;
        if (_hasAnchor) _anchor = sum / _allies.Count;
    }

    private bool CanLink(Transform t)
    {
        if (t == null || !t.gameObject.activeInHierarchy) return false;
        var h = t.GetComponentInChildren<UnitHealthController>(true);
        if (h == null || h.IsDead) return false;
        if (t.GetComponentInChildren<SkyPrisonChanterSupport>(true) != null) return false;
        if (t.GetComponentInChildren<SkyPrisonGuardianShield>(true) != null) return false;
        if (t.GetComponentInChildren<SkyPrisonGrayFurnace>(true) != null) return false;
        return true;
    }

    private bool WallBetween(Transform t)
    {
        if (!breakOnWalls || SkyPrisonVisionManager.Instance == null) return false;
        Vector3 a = transform.position + Vector3.up * 0.04f;
        Vector3 d = t.position - transform.position; d.y = 0f;
        float len = d.magnitude;
        if (len < 0.1f) return false;
        return SkyPrisonVisionManager.Instance.TryGetVisionBlockDistance(a, d / len, len, out _);
    }

    private void UpdateLinks()
    {
        // 先去掉失效 / 超出断线半径的
        for (int i = _links.Count - 1; i >= 0; i--)
        {
            var t = _links[i];
            if (!CanLink(t) || FlatDist(t.position, transform.position) > breakRadius || WallBetween(t)) Unlink(i);
        }
        // 再按距离补满
        while (_links.Count < maxLinks)
        {
            Transform best = null; float bestD = linkRadius;
            for (int i = 0; i < _allies.Count; i++)
            {
                var t = _allies[i];
                if (_links.Contains(t) || !CanLink(t)) continue;
                float d = FlatDist(t.position, transform.position);
                if (d <= bestD && !WallBetween(t)) { bestD = d; best = t; }
            }
            if (best == null) break;
            _links.Add(best);
            SkyPrisonChanterLinkWard.EnsureOn(best).AddLinker(this);
        }
    }

    private void RefreshLinkEffects()
    {
        for (int i = 0; i < _links.Count; i++)
        {
            var t = _links[i];
            if (t == null || berserkStatus == null) continue;
            var st = t.GetComponentInChildren<UnitStatusController>(true);
            if (st == null) continue;
            st.ApplyStatus(berserkStatus, gameObject, 1, berserkLingerSeconds + 0.5f);   // 连线中每 0.5 秒刷新
            SkyPrisonBerserkActionSpeed.EnsureOn(t.gameObject, berserkActionSpeed);
        }
    }

    private void Unlink(int i)
    {
        var t = _links[i];
        if (t != null)
        {
            var ward = SkyPrisonChanterLinkWard.Find(t);
            if (ward != null) ward.RemoveLinker(this);
        }
        _links.RemoveAt(i);
    }

    private void ClearLinks()
    {
        for (int i = _links.Count - 1; i >= 0; i--) Unlink(i);
        for (int i = 0; i < _linkLines.Count; i++) if (_linkLines[i] != null) _linkLines[i].enabled = false;
    }

    // ── 站位 / 后撤 ─────────────────────────────────────

    private void TickMovement(Vector3 pp, float distToPlayer)
    {
        Vector3 self = transform.position;

        // 后撤：玩家贴近且冷却好了 → 朝远离玩家的方向短跑一下，面朝移动方向；跑完就停
        if (Time.time < _hopUntil)
        {
            _movement.ClearFacingOverride();
            SubmitMove(_hopDir, true); _moving = true;
            return;
        }
        if (distToPlayer < panicRadius && Time.time >= _nextHop)
        {
            float away = Mathf.Sign(self.x - pp.x); if (away == 0f) away = 1f;
            float zAway = Mathf.Abs(self.z - pp.z) > 0.5f ? Mathf.Sign(self.z - pp.z) : 0f;
            _hopDir = new Vector2(away, zAway);
            _hopUntil = Time.time + hopSeconds;
            _nextHop = Time.time + hopCooldown;
            _hasStandPoint = false;
            BreakChannel();                  // 一动就断线
            return;
        }

        // 维持施放：还有连线就站着不动；连线全断了才去重新找位置
        if (_channelActive)
        {
            if (_links.Count > 0) { Stop(); FacePlayer(pp); return; }
            BreakChannel();
            _hasStandPoint = false;
        }

        // 吟唱中：站定，吟唱完连线生效
        if (_castEnd > 0f)
        {
            Stop(); FacePlayer(pp);
            if (Time.time >= _castEnd)
            {
                _castEnd = -1f;
                _channelActive = true;
                UpdateLinks();
                RefreshLinkEffects();
                if (_links.Count == 0) { _channelActive = false; _nextCastTry = Time.time + 1f; }
            }
            return;
        }

        // 站位点：集团背对玩家一侧；集团挪动超过 repositionDistance 才重算，不跟着玩家每一步平移
        if (_hasAnchor)
        {
            float rel = pp.x - _anchor.x;
            float newSide = _side == 0f ? (rel >= 0f ? 1f : -1f) : (Mathf.Sign(rel) != _side && Mathf.Abs(rel) > 3f ? Mathf.Sign(rel) : _side);
            // 换边有冷却：玩家绕过集团包抄时，它不会立刻跟着躲到另一边，留出窗口
            if (newSide != _side && _side != 0f && Time.time < _nextSideSwitch) newSide = _side;
            if (!_hasStandPoint || newSide != _side || FlatDist(_anchor, _standAnchor) > repositionDistance)
            {
                if (newSide != _side) _nextSideSwitch = Time.time + sideSwitchCooldown;
                _side = newSide;
                _standAnchor = _anchor;
                _standPoint = new Vector3(_anchor.x - _side * backOffset, self.y, _anchor.z);
                _hasStandPoint = true;
            }
        }
        else if (!_hasStandPoint)
        {
            // 没有同伴：就地站着，不跟玩家保持距离(否则像幽灵一样飘着走)
            _standPoint = self;
            _hasStandPoint = true;
        }

        float dx = _standPoint.x - self.x, dz = _standPoint.z - self.z;
        float enter = arriveTolerance * 1.6f, leave = arriveTolerance * 0.5f;
        float thr = _moving ? leave : enter;
        if (Mathf.Abs(dx) <= thr && Mathf.Abs(dz) <= thr)
        {
            Stop();
            FacePlayer(pp);           // 站定时面朝玩家
            if (_aware && Time.time >= _nextCastTry) TryStartCast();
            return;
        }

        if (!_moving || Time.time >= _nextDecide)
        {
            _nextDecide = Time.time + 0.25f;
            _cmd = new Vector2(Mathf.Abs(dx) > leave ? Mathf.Sign(dx) : 0f, Mathf.Abs(dz) > leave ? Mathf.Sign(dz) : 0f);
        }
        float farDist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
        _running = _running ? farDist > runWhenFartherThan * 0.6f : farDist > runWhenFartherThan;
        _movement.ClearFacingOverride();     // 走动时面朝移动方向，不倒着走
        SubmitMove(_cmd, _running);
        _moving = true;
    }

    private void TryStartCast()
    {
        _castTargets.Clear();
        // 吟唱表现用：预览会连上的目标(最近的 maxLinks 只)
        var tmp = new List<Transform>();
        for (int i = 0; i < _allies.Count; i++)
        {
            var t = _allies[i];
            if (CanLink(t) && FlatDist(t.position, transform.position) <= linkRadius && !WallBetween(t)) tmp.Add(t);
        }
        if (tmp.Count == 0) { _nextCastTry = Time.time + 1f; return; }
        Vector3 me = transform.position;
        tmp.Sort((a, b) => FlatDist(a.position, me).CompareTo(FlatDist(b.position, me)));
        for (int i = 0; i < tmp.Count && i < maxLinks; i++) _castTargets.Add(tmp[i]);
        _castEnd = Time.time + castSeconds;
    }

    public bool SuperArmored => false;
    public bool Airborne => false;

    /// <summary>失衡：吟唱 / 连线断开、硬直，硬直结束后再过 1 秒才能重新吟唱。</summary>
    public void Stagger(float seconds)
    {
        BreakChannel();
        Stop();
        var action = GetComponentInChildren<UnitActionController>(true);
        if (action != null) action.ForceHitStun(seconds);
        _nextCastTry = Time.time + seconds + 1f;
    }

    private void BreakChannel()
    {
        ClearLinks();
        _channelActive = false;
        _castEnd = -1f;
        _castTargets.Clear();
    }

    private void FacePlayer(Vector3 pp)
    {
        float toX = pp.x - transform.position.x;
        if (Mathf.Abs(toX) > 1f) _movement.SetFacingOverride(new Vector2(Mathf.Sign(toX), 0f));
    }

    private void Stop()
    {
        if (!_moving) return;
        SubmitMove(Vector2.zero, false);
        _moving = false; _running = false; _cmd = Vector2.zero;
    }

    private void SubmitMove(Vector2 move, bool run)
    {
        if (_action != null) _action.SubmitMoveIntent(move, run, false);
        else if (_movement != null) _movement.SetExternalMoveInput(move, run);
    }

    // ── 占位表现：到每个被连单位的金色连线(轻微下垂 + 脉动) ──

    private void TickLinkFx()
    {
        // 吟唱中显示预览连线(从颂赞者往目标慢慢伸出去)，生效后显示真正的连线
        bool casting = _castEnd > 0f;
        var shown = casting ? _castTargets : _links;
        float grow = casting ? 1f - Mathf.Clamp01((_castEnd - Time.time) / Mathf.Max(0.01f, castSeconds)) : 1f;
        while (_linkLines.Count < shown.Count)
        {
            var go = new GameObject("ChanterLink");
            go.transform.SetParent(transform, false);
            int layer = LayerMask.NameToLayer("World3D");
            if (layer >= 0) go.layer = layer;
            var lr = go.AddComponent<LineRenderer>();
            if (_fxMat == null) _fxMat = new Material(Shader.Find("Sprites/Default"));
            lr.sharedMaterial = _fxMat;
            lr.useWorldSpace = true; lr.alignment = LineAlignment.View;
            lr.startWidth = linkWidth * 1.4f; lr.endWidth = linkWidth;
            lr.positionCount = 12;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            _linkLines.Add(lr);
        }
        Vector3 a = transform.position + Vector3.up * 1.4f;
        for (int i = 0; i < _linkLines.Count; i++)
        {
            var lr = _linkLines[i];
            bool on = i < shown.Count && shown[i] != null;
            lr.enabled = on;
            if (!on) continue;
            Vector3 b = Vector3.Lerp(a, shown[i].position + Vector3.up * 1.2f, grow);
            Color c = linkColor; c.a *= casting ? 0.45f : 0.6f + 0.4f * Mathf.Sin(Time.time * 6f + i * 1.7f);
            lr.startColor = lr.endColor = c;
            float sag = Mathf.Min(1.2f, FlatDist(a, b) * 0.06f);
            for (int k = 0; k < lr.positionCount; k++)
            {
                float u = k / (float)(lr.positionCount - 1);
                lr.SetPosition(k, Vector3.Lerp(a, b, u) + Vector3.down * (sag * 4f * u * (1f - u)));
            }
        }
    }

    private static float FlatDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
