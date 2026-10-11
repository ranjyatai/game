using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BOSS「震碎者」(原型第一步)：
///   · 四足慢走：四条腿按 左前→右后→右前→左后 的顺序轮流抬起、前迈、落下，同一时刻只动一条腿；
///   · 每一脚落地：以落脚点为圆心扩出一圈环形冲击波(速度 waveSpeed、最大半径 waveMaxRadius)，
///     冲击波前沿扫过地面的玩家会受伤——<b>跳起来(或闪避)就能躲过</b>，所以这是个"节奏 + 跳跃"的 BOSS；
///   · 落脚本身砸中脚下的玩家：大伤害 + 硬直 + 击退；
///   · 四条腿都有伤害判定：腿落地站定时，玩家蹭到腿也会受伤；
///   · 外观先用占位几何体(身体 + 四根腿)，Spine/正式模型之后再换。
/// 伤害全走 SkyPrisonAIDash.ApplyFlatDamage(固定整数伤害，和冲刺词条同一口径：防御/抗性/盾/闪避无敌/异常累积)。
/// 之后：腿血量/可破坏、场内随机弹幕、阶段。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonShattererAI : MonoBehaviour
{
    [Header("唤醒")]
    [SerializeField] private float wakeRangeX = 24f;
    [SerializeField] private float wakeRangeZ = 14f;

    [Header("步态")]
    [Tooltip("两步之间的间隔(秒)。每一步都会出一圈冲击波，间隔就是冲击波的节奏。")]
    [SerializeField] private float stepInterval = 2.6f;
    [SerializeField] private float liftSeconds = 0.85f;
    [SerializeField] private float slamSeconds = 0.12f;
    [SerializeField] private float liftHeight = 1.8f;
    [Tooltip("每一步朝玩家方向多迈出多远(米)。")]
    [SerializeField] private float stepReach = 1.4f;
    [SerializeField] private float legSpreadX = 1.7f;
    [SerializeField] private float legSpreadZ = 1.1f;
    [Tooltip("身体慢走的速度，0~1 的移动输入强度(再乘单位自己的步行速度)。")]
    [SerializeField, Range(0f, 1f)] private float walkInput = 0.3f;

    [Header("环形冲击波")]
    [SerializeField] private float waveMaxRadius = 55f;
    [SerializeField] private float waveSpeed = 5f;
    [Tooltip("冲击波前沿往里多深(米)算命中带——玩家在这条带里且在地面才会被打。")]
    [SerializeField] private float waveBand = 1.6f;
    [SerializeField] private int waveDamage = 30;
    [SerializeField] private float waveStun = 0.3f;
    [SerializeField] private float waveKnockback = 5f;
    [Tooltip("玩家脚底离地面高过这个高度(米)就算跳起来了，冲击波打不到。")]
    [SerializeField] private float airSafeHeight = 0.35f;

    [Header("冲击波外观：垂直半透明光幕(向上渐隐)")]
    [SerializeField] private float waveWallHeight = 1.3f;
    [SerializeField] private float leapWaveWallHeight = 2.2f;
    [Range(0f, 1f), SerializeField] private float waveWallAlpha = 0.5f;

    [Header("落脚砸击")]
    [SerializeField] private float stompRadius = 1.8f;
    [SerializeField] private int stompDamage = 45;
    [SerializeField] private float stompStun = 0.5f;
    [SerializeField] private float stompKnockback = 6f;

    [Header("腿接触伤害(腿落地站定时)")]
    [SerializeField] private float contactRadius = 1.4f;
    [SerializeField] private int contactDamage = 15;
    [SerializeField] private float contactInterval = 0.7f;

    [Header("招式调度")]
    [Tooltip("连续走几步之后放一次大招(三点震荡波 / 跳砸二选一)。")]
    [SerializeField] private int stepsBeforeAttack = 6;
    [SerializeField] private float attackRecoverySeconds = 1.4f;
    [Range(0f, 1f), Tooltip("放大招时选「连踏」的概率，其余是「高跳砸地」。")]
    [SerializeField] private float quakeChance = 0.55f;

    [Header("高射炮弹幕（从核心连发几轮，向上射出后坠落在玩家周围）")]
    [Range(0f, 1f), Tooltip("放大招时选「高射炮」的概率；其余按「连踏 / 高跳砸地」再分。")]
    [SerializeField] private float barrageChance = 0.34f;
    [SerializeField] private int mortarWaves = 3;
    [SerializeField] private int mortarShellsPerWave = 5;
    [Tooltip("一轮和下一轮之间的间隔(秒)。")]
    [SerializeField] private float mortarWaveGap = 1.1f;
    [Tooltip("同一轮里相邻两发的发射间隔(秒)。")]
    [SerializeField] private float mortarShellGap = 0.12f;
    [Tooltip("炮弹从发射到落地的总时间(秒)。落点预告从发射时就画在地上。")]
    [SerializeField] private float mortarFlightSeconds = 1.6f;
    [SerializeField] private float mortarRiseHeight = 22f;
    [Tooltip("落点离玩家的最近/最远距离(米)；每轮里有一发直接落在玩家脚下附近。")]
    [SerializeField] private Vector2 mortarScatter = new Vector2(1.5f, 8f);
    [SerializeField] private float mortarRadius = 2.3f;
    [SerializeField] private int mortarDamage = 35;
    [SerializeField] private float mortarStun = 0.3f;
    [SerializeField] private float mortarKnockback = 4f;

    [Header("连踏（三只脚连着踩，几圈波同时存在）")]
    [SerializeField] private int flurryCount = 3;
    [Tooltip("连踏里相邻两脚落地的间隔(秒)。越短，场上同时存在的波越多。")]
    [SerializeField] private float flurryGap = 0.4f;
    [SerializeField] private float flurryLiftSeconds = 0.45f;
    [Tooltip("连踏时每脚比平时多往前探出多远，让三个落点分开。")]
    [SerializeField] private float flurryExtraReach = 2.2f;

    [Header("高跳砸地")]
    [SerializeField] private float leapCrouchSeconds = 0.5f;
    [SerializeField] private float leapTrackSeconds = 0.7f;
    [SerializeField] private float leapLockSeconds = 0.55f;
    [SerializeField] private float leapAirSeconds = 1.0f;
    [SerializeField] private float leapHeight = 11f;
    [SerializeField] private float leapImpactRadius = 4.2f;
    [SerializeField] private int leapImpactDamage = 90;
    [SerializeField] private float leapWaveMaxRadius = 70f;
    [SerializeField] private float leapWaveSpeed = 6f;
    [SerializeField] private int leapWaveDamage = 55;
    [SerializeField] private float leapRecoverySeconds = 1.6f;

    [Header("腿与身体的拴系（腿不能离身体太远）")]
    [Tooltip("站定的腿离它「应在位置」(身体+腿站位)超过这个距离(米)，就优先让这条腿迈步追上来。")]
    [SerializeField] private float legTether = 3.2f;
    [Tooltip("硬上限：腿被身体拖着最远离开应在位置多少米(超过就被拽住)。")]
    [SerializeField] private float legMaxStretch = 5f;
    [Tooltip("全断倒地：身体从站立高度砸到地面的时间(秒)。")]
    [SerializeField] private float collapseSeconds = 0.7f;

    [Header("腿血量（腿的受击判定就是模型本身；断了不修复）")]
    [Range(0.02f, 0.5f), Tooltip("每条腿的血量 = BOSS 最大血量 × 这个比例。")]
    [SerializeField] private float legHpFraction = 0.07f;
    [Range(0f, 1f), Tooltip("打在腿上的伤害，有多少比例同时传给 BOSS 本体(其余全部被腿吃掉)。")]
    [SerializeField] private float legBodyShare = 0.3f;
    [Range(0f, 0.6f), Tooltip("每断一条腿，剩下的招式伤害加成。")]
    [SerializeField] private float brokenDamageBonus = 0.2f;

    [Header("伤害类型")]
    [SerializeField] private string damageTypeKey = "impact";

    [Header("落脚扬尘（留空则不出）")]
    [SerializeField] private GameObject[] stompDustPrefabs;
    [SerializeField] private float stompDustScale = 4f;
    [SerializeField] private float stompDustLifetime = 3f;

    [Header("占位外观")]
    [SerializeField] private bool showPlaceholderBody = true;
    [SerializeField] private bool showWaveRing = true;

    private class Leg
    {
        public Transform visual;
        public Vector2 offset;     // 相对身体的站位(以"朝右"为基准，朝左时 X 取反)
        public Vector3 foot;       // 脚当前世界位置(含抬起高度)
        public Vector3 anchor;     // 落地站定的世界位置
        public bool planted = true;
        public float nextContactTime;
        public SkyPrisonShattererLeg part;
        public bool broken;
    }

    private readonly List<Leg> _legs = new List<Leg>();
    private readonly int[] _order = { 0, 3, 1, 2 };        // 左前 → 右后 → 右前 → 左后
    private int _orderIdx;
    private Transform _bodyVisual;
    private readonly List<GameObject> _temp = new List<GameObject>();

    private UnitMovementController _movement;
    private UnitActionController _action;
    private UnitHealthController _health;
    private UnitDeathController _death;
    private SkyPrisonEnemyLeash _leash;
    private bool _refsReady, _filled, _awake, _stepping, _hurt;
    private float _nextStepTime, _face = 1f;
    private Vector3 _prevPos; private float _speedEma;
    private Vector3 _bodyLift;
    private int _brokenCount;
    private bool _collapsed; private float _collapseDrop;
    private bool _attacking; private int _stepsSinceAttack; private float _bodyDrop;
    private SkyPrisonUnitRuntimeIdentity _playerId;
    private UnitHealthController _playerHealth;
    private UnitMovementController _playerMove;
    private static Material _lineMat, _bodyMat;
    private static Mesh _wallMesh;

    private bool Dead => _death != null && _death.IsDeadLike;
    private bool AllBroken => _legs.Count > 0 && _brokenCount >= _legs.Count;
    private float DamageBoost => 1f + brokenDamageBonus * _brokenCount;     // 越断越凶
    public float LegMaxHp => (_health != null ? _health.MaxHealth : 1000f) * legHpFraction;
    public float LegBodyShare => legBodyShare;

    // 腿断了(永久)：停在原地变成断桩，扬一团尘；之后不再迈步/不再出波/没有接触伤害
    public void OnLegBroken(int index)
    {
        if (index < 0 || index >= _legs.Count) return;
        var l = _legs[index];
        if (l.broken) return;
        l.broken = true;
        _brokenCount++;
        l.foot = new Vector3(l.foot.x, transform.position.y, l.foot.z);
        l.anchor = l.foot;
        l.planted = true;
        SpawnDust(l.foot);
    }

    // 倒地：身体从站立高度加速砸到地上(中心离地 0.7m = 方块半高)，砸地一圈冲击波 + 扬尘
    private IEnumerator CollapseRoutine()
    {
        _attacking = true;
        float t = 0f;
        float total = 3.3f - 0.7f;
        while (t < collapseSeconds)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / collapseSeconds);
            _collapseDrop = total * u * u;           // 加速下坠
            yield return null;
        }
        _collapseDrop = total;
        _bodyDrop = 0f; _bodyLift = Vector3.zero;
        Vector3 g = transform.position;
        SpawnDust(g); SpawnDust(g + new Vector3(2f, 0f, 0f)); SpawnDust(g - new Vector3(2f, 0f, 0f));
        if (_playerId != null && PlayerPlanarDist(g) <= stompRadius * 2f && PlayerHeightAboveGround() < 1.2f)
            HitPlayer(g, stompDamage, stompStun, stompKnockback);
        StartCoroutine(WaveRoutine(g, waveMaxRadius * 0.5f, waveSpeed * 1.6f, waveDamage, waveStun, waveKnockback));
        _nextStepTime = Time.time + 2.5f;
        _attacking = false;
    }

    // 腿「应在的位置」：身体 + 这条腿的站位(随朝向翻转)
    private Vector3 HomeOf(Leg l)
    {
        Vector3 b = transform.position;
        return new Vector3(b.x + l.offset.x * _face, transform.position.y, b.z + l.offset.y);
    }

    // 下一条要迈的腿：按顺序跳过断了的；全断了返回 null
    private Leg NextLeg()
    {
        for (int k = 0; k < _order.Length; k++)
        {
            var l = _legs[_order[_orderIdx % _order.Length]];
            _orderIdx++;
            if (!l.broken) return l;
        }
        return null;
    }

    private void Awake() { EnsureRefs(); }
    private void Start() { EnsureRefs(); BuildPlaceholder(); }

    private void EnsureRefs()
    {
        if (_refsReady) return;
        _movement = GetComponentInChildren<UnitMovementController>(true);
        if (_movement == null) return;
        _action = GetComponentInChildren<UnitActionController>(true);
        _health = GetComponentInChildren<UnitHealthController>(true);
        _death = GetComponentInChildren<UnitDeathController>(true);
        _leash = GetComponent<SkyPrisonEnemyLeash>();
        _movement.ConfigureAsExternalExecutor();
        if (_health != null) _health.OnDamaged += (h, a) => { _awake = true; _hurt = true; };
        _refsReady = true;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _temp.Count; i++) if (_temp[i] != null) Destroy(_temp[i]);
        for (int i = 0; i < _legs.Count; i++) if (_legs[i].visual != null) Destroy(_legs[i].visual.gameObject);
        if (_bodyVisual != null) Destroy(_bodyVisual.gameObject);
    }

    // ── 占位外观：身体一个方块 + 四根圆柱腿 ──────────────────────────
    private void BuildPlaceholder()
    {
        if (_legs.Count > 0) return;
        Vector3 basePos = transform.position;
        // 站位：左前、右前、左后、右后(索引 0,1,2,3)；朝右基准，Z 正负分左右
        Vector2[] offs = { new Vector2(legSpreadX, legSpreadZ), new Vector2(legSpreadX, -legSpreadZ), new Vector2(-legSpreadX, legSpreadZ), new Vector2(-legSpreadX, -legSpreadZ) };
        for (int i = 0; i < 4; i++)
        {
            var leg = new Leg { offset = offs[i] };
            leg.anchor = basePos + new Vector3(offs[i].x, 0f, offs[i].y);
            leg.foot = leg.anchor;
            if (showPlaceholderBody)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                g.name = "ShattererLeg" + i;
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(transform, true);                          // 挂在 BOSS 根下：子弹/近战判定顺着父级找到 BOSS 的血
                g.transform.localScale = new Vector3(1.35f, 1.6f, 1.35f);       // 粗腿：直径 1.35m，圆柱高 2*1.6 = 3.2m
                ApplyVisual(g);
                leg.visual = g.transform;
                // 受击判定 = 模型形状本身(胶囊，和圆柱同尺寸)，触发器：不会物理顶开玩家，只负责被命中
                var hit = new GameObject("LegHit" + i);
                hit.transform.SetParent(g.transform, false);
                int hl = LayerMask.NameToLayer("Character2D"); if (hl >= 0) hit.layer = hl;
                var cc = hit.AddComponent<CapsuleCollider>();
                cc.isTrigger = true; cc.direction = 1; cc.radius = 0.5f; cc.height = 2f;
                var part = hit.AddComponent<SkyPrisonShattererLeg>();
                part.Init(this, i, g.GetComponent<Renderer>(), cc);
                leg.part = part;
            }
            _legs.Add(leg);
        }
        if (showPlaceholderBody)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = "ShattererBody";
            Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(transform, true);
            b.transform.localScale = new Vector3(3.8f, 1.4f, 2.6f);
            ApplyVisual(b);
            _bodyVisual = b.transform;
            var bh = new GameObject("BodyHit");
            bh.transform.SetParent(b.transform, false);
            int bl = LayerMask.NameToLayer("Character2D"); if (bl >= 0) bh.layer = bl;
            // 身体受击判定：方块悬在 3.3m 高空，平地的射击/近战够不到——改成「从地面一直立到方块顶」的立柱(覆盖身体正下方)。
            // 父级(方块)缩放 y=1.4：世界高度 4.0m = 本地 2.857，中心在地面到顶的中点 = 本地 -0.929
            var bc = bh.AddComponent<BoxCollider>(); bc.isTrigger = true;
            bc.size = new Vector3(1f, 4.0f / 1.4f, 1f);
            bc.center = new Vector3(0f, -1.3f / 1.4f, 0f);
        }
        // 旧的受击小球(来自 Spine 骨架占位的头/身/脚锚点)关掉，只认模型形状
        foreach (var sc in GetComponentsInChildren<SphereCollider>(true))
            if (sc != null && sc.name.EndsWith("Anchor")) sc.enabled = false;
        {
        }
    }

    private static void ApplyVisual(GameObject g)
    {
        int layer = LayerMask.NameToLayer("World3D");
        if (layer >= 0) g.layer = layer;
        var r = g.GetComponent<Renderer>();
        if (_bodyMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _bodyMat = new Material(sh);
            _bodyMat.color = new Color(0.16f, 0.16f, 0.19f);
        }
        r.sharedMaterial = _bodyMat;
    }

    // ── 主循环 ───────────────────────────────────────────────────
    private void Update()
    {
        EnsureRefs();
        if (_movement == null) return;
        if (_legs.Count == 0) BuildPlaceholder();

        // UD 的 maxHp 在 Awake 之后才应用：出生时当前血量还停在预制体默认值，补满一次
        if (!_filled && _health != null && _health.MaxHealth > 101f) { _health.SetCurrentHealth(_health.MaxHealth); _filled = true; }

        if (Dead) { SubmitMove(Vector2.zero); return; }
        if (_leash != null && _leash.IsReturning) { SubmitMove(Vector2.zero); return; }

        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) { SubmitMove(Vector2.zero); return; }
        CachePlayer(player);

        Vector3 delta = player.transform.position - transform.position;
        if (!_awake)
        {
            if (Mathf.Abs(delta.x) <= wakeRangeX && Mathf.Abs(delta.z) <= wakeRangeZ) _awake = true;
            else { SubmitMove(Vector2.zero); return; }
        }

        if (_attacking) { SubmitMove(Vector2.zero); UpdateLegContact(); return; }
        if (AllBroken)
        {
            // 四条腿全断：身体砸到地上趴着不能动，只剩核心还能放高射炮
            if (!_collapsed) { _collapsed = true; StartCoroutine(CollapseRoutine()); }
            SubmitMove(Vector2.zero);
            if (Time.time >= _nextStepTime) StartCoroutine(AttackRoutine());
            return;
        }

        // 实测身体速度(平滑)：四条腿轮一圈要 4*stepInterval，每条腿这一步要追上这段时间身体走出去的距离，
        // 否则腿越落越远(身体走得比腿快)。前迈距离 = max(固定值, 速度 × 一圈时间 × 0.8)
        float inst = Time.deltaTime > 0.0001f ? (transform.position - _prevPos).magnitude / Time.deltaTime : 0f;
        _speedEma = Mathf.Lerp(_speedEma, inst, 0.05f);
        _prevPos = transform.position;

        if (Mathf.Abs(delta.x) > 1.5f) _face = delta.x >= 0f ? 1f : -1f;
        _movement.SetFacingOverride(new Vector2(_face, 0f));

        // 腿跟着身体：断腿(断桩)始终贴在身体上；站定的腿被拽到不超过 legMaxStretch；记录最落后的那条
        Leg lagging = null; float maxLag = 0f;
        for (int i = 0; i < _legs.Count; i++)
        {
            var l = _legs[i];
            Vector3 home = HomeOf(l);
            if (l.broken) { l.anchor = home; l.foot = home; continue; }
            if (!l.planted) continue;
            Vector3 off = l.anchor - home; off.y = 0f;
            float lag = off.magnitude;
            if (lag > legMaxStretch) { l.anchor = home + off / lag * legMaxStretch; l.foot = l.anchor; lag = legMaxStretch; }
            if (lag > maxLag) { maxLag = lag; lagging = l; }
        }

        // 慢走：朝玩家的 X/Z 方向挪，够近了就站着踏脚；最落后的腿越远走得越慢(身体被腿拖着)，到上限就停下等腿
        Vector2 move = new Vector2(delta.x, delta.z);
        if (move.magnitude < 3.5f) move = Vector2.zero;
        else move = move.normalized * walkInput * Mathf.Clamp01(1f - (maxLag - legTether * 0.5f) / (legMaxStretch - legTether * 0.5f));
        SubmitMove(move);

        // 腿站定后的接触伤害
        UpdateLegContact();

        // 轮流迈步
        if (!_stepping && Time.time >= _nextStepTime)
        {
            if (_stepsSinceAttack >= stepsBeforeAttack) StartCoroutine(AttackRoutine());
            else
            {
                // 有腿落后太多就先迈它(拴系)，否则按固定顺序轮流
                Leg next = (lagging != null && maxLag > legTether) ? lagging : NextLeg();
                if (next != null) StartCoroutine(StepRoutine(next));
            }
        }
    }

    private void LateUpdate()
    {
        // 占位外观：用的是几何体，把 Spine 网格关掉(它是别的怪的骨架占位)，只保留阴影
        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
            if (mr != null && mr.enabled && mr.gameObject.name.StartsWith("Spine")) mr.enabled = false;

        if (!showPlaceholderBody || _legs.Count == 0) return;
        Vector3 bp = transform.position;
        // 断桩始终贴在身体上(不论身体在走还是已经倒地)
        for (int i = 0; i < _legs.Count; i++)
            if (_legs[i].broken) { var hm = HomeOf(_legs[i]); _legs[i].anchor = hm; _legs[i].foot = hm; }
        // 倒地后不再叠加炮击后坐力，免得身体压进地里
        float drop = (_collapseDrop > 0f ? 0f : _bodyDrop) + _collapseDrop;
        if (_bodyVisual != null) _bodyVisual.position = bp + Vector3.up * (3.3f - drop) + _bodyLift;
        for (int i = 0; i < _legs.Count; i++)
        {
            var l = _legs[i];
            if (l.visual == null) continue;
            if (l.broken)
            {
                // 断桩：只剩半米高的残根
                l.visual.localScale = new Vector3(1.35f, 0.3f, 1.35f);
                l.visual.position = l.foot + Vector3.up * 0.3f;
            }
            else l.visual.position = l.foot + Vector3.up * 1.6f;
        }
    }

    // ── 迈一步：抬腿(前摇，地上有预告) → 前迈悬停 → 猛踏 → 冲击波 ─────────
    private IEnumerator StepRoutine(Leg leg, float lift = -1f, float extraReach = 0f, bool manage = true)
    {
        if (lift <= 0f) lift = liftSeconds;
        if (manage) _stepping = true;
        float groundY = transform.position.y;
        Vector3 from = leg.anchor;
        Vector3 to = TargetFor(leg, groundY, extraReach);
        leg.planted = false;

        var marker = showWaveRing ? CreateRing(to, stompRadius, 0.5f) : null;

        float t = 0f;
        while (t < lift)
        {
            if (Dead) { if (marker != null) Destroy(marker.gameObject); if (manage) _stepping = false; yield break; }
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / lift);
            float s = u * u * (3f - 2f * u);
            Vector3 p = Vector3.Lerp(from, to, s);
            p.y = groundY + Mathf.Sin(Mathf.Min(1f, u * 1.4f) * Mathf.PI * 0.5f) * liftHeight;
            leg.foot = p;
            yield return null;
        }

        t = 0f;
        while (t < slamSeconds)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / slamSeconds);
            Vector3 p = to;
            p.y = groundY + (1f - u * u) * liftHeight;
            leg.foot = p;
            yield return null;
        }
        leg.foot = new Vector3(to.x, groundY, to.z);
        leg.anchor = leg.foot;
        leg.planted = true;
        if (marker != null) Destroy(marker.gameObject);

        Stomp(leg.foot);
        if (manage)
        {
            _stepsSinceAttack++;
            _nextStepTime = Time.time + stepInterval * Mathf.Max(0.5f, 1f - 0.1f * _brokenCount);
            _stepping = false;
        }
    }

    private Vector3 TargetFor(Leg leg, float groundY, float extraReach = 0f)
    {
        Vector3 b = transform.position;
        float reach = Mathf.Clamp(Mathf.Max(stepReach, _speedEma * stepInterval * 4f * 0.8f), stepReach, 6f) + extraReach;
        return new Vector3(b.x + leg.offset.x * _face + _face * reach, groundY, b.z + leg.offset.y);
    }

    // ── 落脚：砸击 + 扬尘 + 冲击波 ──────────────────────────────
    private void Stomp(Vector3 pos)
    {
        SpawnDust(pos);
        var p = _playerId;
        if (p != null && PlayerPlanarDist(pos) <= stompRadius && PlayerHeightAboveGround() < 1.2f)
            HitPlayer(pos, stompDamage, stompStun, stompKnockback);
        StartCoroutine(WaveRoutine(pos, waveMaxRadius, waveSpeed, waveDamage, waveStun, waveKnockback));
    }

    private IEnumerator WaveRoutine(Vector3 center, float maxRadius, float speed, int damage, float stun, float knockback)
    {
        LineRenderer ring = showWaveRing ? CreateRing(center, 0.2f, 1f) : null;
        MeshRenderer wall = showWaveRing ? CreateWall() : null;
        float wallH = maxRadius > 30f ? leapWaveWallHeight : waveWallHeight;
        float r = 0f;
        bool hit = false;
        while (r < maxRadius)
        {
            if (Dead) break;
            r += speed * Time.deltaTime;
            if (ring != null) { SetRing(ring, center, r); SetRingAlpha(ring, 0.9f * (1f - r / maxRadius)); }
            SetWall(wall, center, r, wallH, waveWallAlpha * (1f - r / maxRadius));
            if (!hit && _playerId != null)
            {
                float d = PlayerPlanarDist(center);
                if (d <= r && d >= r - waveBand && PlayerHeightAboveGround() <= airSafeHeight)
                {
                    hit = true;                     // 同一圈波只打一次
                    HitPlayer(center, damage, stun, knockback);
                }
            }
            yield return null;
        }
        if (ring != null) Destroy(ring.gameObject);
        if (wall != null) Destroy(wall.gameObject);
    }

    // ── 大招：三点震荡波 / 高跳砸地 ─────────────────────────────────
    private IEnumerator AttackRoutine()
    {
        _attacking = true;
        _stepping = false;
        yield return null;
        if (Dead) { _attacking = false; yield break; }
        float r = Random.value;
        if (AllBroken || r < barrageChance) yield return StartCoroutine(MortarBarrage());
        else if (Random.value < quakeChance) yield return StartCoroutine(TripleQuake());
        else yield return StartCoroutine(LeapSlam());
        _bodyDrop = 0f; _bodyLift = Vector3.zero;
        _stepsSinceAttack = 0;
        _nextStepTime = Time.time + attackRecoverySeconds + (AllBroken ? 4f : 0f);
        _attacking = false;
    }

    // 连踏：三只脚连着正常迈步踩下，间隔很短，所以几圈冲击波会同时在场上扩散(玩家要连续判断每一圈的跳跃时机)。
    // 每一脚都是正常抬腿→前迈→踩下，落点预告和普通步一样提前画在地上。
    private IEnumerator TripleQuake()
    {
        int n = Mathf.Clamp(flurryCount, 1, 4);
        int pending = n;
        for (int i = 0; i < n; i++)
        {
            if (Dead) break;
            var leg = NextLeg();
            if (leg == null) break;
            // 每只脚的落点分开：给不同脚加不同的前探距离
            StartCoroutine(FlurryStep(leg, flurryExtraReach * (0.4f + 0.6f * i / Mathf.Max(1, n - 1)), () => pending--));
            yield return new WaitForSeconds(flurryGap);
        }
        while (pending > 0 && !Dead) yield return null;
        yield return new WaitForSeconds(0.3f);
    }

    private IEnumerator FlurryStep(Leg leg, float extra, System.Action done)
    {
        yield return StartCoroutine(StepRoutine(leg, flurryLiftSeconds, extra, false));
        done?.Invoke();
    }

    // 高射炮：从核心(身体中心)连发几轮炮弹，每发先笔直射向高空(飞出画面)，再从高空坠落到地上提前标好的落点。
    // 每轮的落点围着玩家当前位置撒开，其中一发落在玩家脚下附近逼他挪动；落点预告从发射那一刻就画在地上。
    private IEnumerator MortarBarrage()
    {
        int pendingShells = 0;
        for (int w = 0; w < mortarWaves; w++)
        {
            if (Dead) break;
            var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            float groundY = transform.position.y;
            Vector3 pc = player != null ? player.transform.position : transform.position + new Vector3(_face * 6f, 0f, 0f);
            for (int i = 0; i < mortarShellsPerWave; i++)
            {
                Vector2 off = i == 0 ? Random.insideUnitCircle * 1.2f : Random.insideUnitCircle.normalized * Random.Range(mortarScatter.x, mortarScatter.y);
                var target = new Vector3(pc.x + off.x, groundY, pc.z + off.y * 0.6f);   // 进深方向压缩一点，场地是横向的
                pendingShells++;
                StartCoroutine(MortarShell(target, () => pendingShells--));
                _bodyDrop = 0.5f;                                                      // 每发后坐力：身体一沉
                yield return new WaitForSeconds(mortarShellGap);
                _bodyDrop = 0f;
            }
            yield return new WaitForSeconds(mortarWaveGap);
        }
        while (pendingShells > 0 && !Dead) yield return null;
        yield return new WaitForSeconds(0.4f);
    }

    private IEnumerator MortarShell(Vector3 target, System.Action done)
    {
        Vector3 core = (_bodyVisual != null ? _bodyVisual.position : transform.position + Vector3.up * 3.3f);
        var marker = showWaveRing ? CreateRing(target, mortarRadius, 0.6f) : null;
        var inner = showWaveRing ? CreateRing(target, mortarRadius * 0.3f, 0.8f) : null;

        var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shell.name = "ShattererMortarShell";
        Destroy(shell.GetComponent<Collider>());
        int layer = LayerMask.NameToLayer("World3D");
        if (layer >= 0) shell.layer = layer;
        shell.transform.localScale = Vector3.one * 0.7f;
        var mat = new Material(LineMat()); mat.color = new Color(1f, 0.6f, 0.6f, 1f);
        shell.GetComponent<Renderer>().sharedMaterial = mat;
        var trail = shell.AddComponent<TrailRenderer>();
        trail.time = 0.35f; trail.startWidth = 0.45f; trail.endWidth = 0f;
        trail.material = new Material(LineMat());
        trail.startColor = new Color(1f, 0.6f, 0.6f, 0.9f); trail.endColor = new Color(1f, 0.6f, 0.6f, 0f);
        _temp.Add(shell);

        float up = Mathf.Clamp(mortarFlightSeconds * 0.4f, 0.3f, 1.2f);          // 上升段
        float down = mortarFlightSeconds - up;                                     // 下落段
        Vector3 apex = new Vector3(Mathf.Lerp(core.x, target.x, 0.2f), core.y + mortarRiseHeight, Mathf.Lerp(core.z, target.z, 0.2f));
        Vector3 dropFrom = new Vector3(target.x, core.y + mortarRiseHeight, target.z);

        float t = 0f;
        while (t < up)
        {
            if (Dead) break;
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / up);
            shell.transform.position = Vector3.Lerp(core, apex, u * u);            // 越飞越快
            yield return null;
        }
        t = 0f;
        shell.transform.position = dropFrom;
        trail.Clear();
        while (t < down)
        {
            if (Dead) break;
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / down);
            shell.transform.position = Vector3.Lerp(dropFrom, target, u * u);      // 加速坠落
            float pulse = 0.45f + 0.5f * Mathf.Abs(Mathf.Sin(t * (8f + 14f * u)));
            if (marker != null) SetRingAlpha(marker, pulse);
            if (inner != null) { SetRing(inner, target, Mathf.Lerp(mortarRadius * 0.3f, mortarRadius, u)); SetRingAlpha(inner, pulse); }
            yield return null;
        }

        // 命中：范围伤害 + 扬尘 + 一圈短促的冲击环
        Destroy(shell);
        if (marker != null) Destroy(marker.gameObject);
        if (inner != null) Destroy(inner.gameObject);
        if (!Dead)
        {
            SpawnDust(target);
            if (_playerId != null && PlayerPlanarDist(target) <= mortarRadius && PlayerHeightAboveGround() < 1.2f)
                HitPlayer(target, mortarDamage, mortarStun, mortarKnockback);
            var burst = showWaveRing ? CreateRing(target, 0.3f, 0.9f) : null;
            float bt = 0f;
            while (burst != null && bt < 0.35f)
            {
                bt += Time.deltaTime;
                float u = bt / 0.35f;
                SetRing(burst, target, Mathf.Lerp(0.3f, mortarRadius * 1.3f, u));
                SetRingAlpha(burst, 0.9f * (1f - u));
                yield return null;
            }
            if (burst != null) Destroy(burst.gameObject);
        }
        done?.Invoke();
    }

    // 蹲下 → 落点锁定(先跟着玩家，最后一小段锁死不动并变亮) → 高高跳起 → 砸在落点：落点范围重击 + 巨大环形冲击波
    private IEnumerator LeapSlam()
    {
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (player == null) yield break;
        float groundY = transform.position.y;
        Vector3 target = new Vector3(player.transform.position.x, groundY, player.transform.position.z);

        var ring = CreateRing(target, leapImpactRadius, 0.7f);
        var cross = CreateRing(target, leapImpactRadius * 0.45f, 0.9f);
        var footMarks = new LineRenderer[_legs.Count];
        for (int i = 0; i < footMarks.Length; i++) footMarks[i] = CreateRing(target, 0.9f, 0.9f);

        // 蹲伏
        float t = 0f;
        while (t < leapCrouchSeconds)
        {
            if (Dead) break;
            t += Time.deltaTime;
            _bodyDrop = Mathf.Sin(Mathf.Clamp01(t / leapCrouchSeconds) * Mathf.PI * 0.5f) * 1.1f;
            yield return null;
        }

        // 跟踪(落点跟着玩家挪)
        t = 0f;
        while (t < leapTrackSeconds)
        {
            if (Dead) break;
            t += Time.deltaTime;
            if (player != null) target = new Vector3(player.transform.position.x, groundY, player.transform.position.z);
            if (ring != null) { SetRing(ring, target, leapImpactRadius); SetRingAlpha(ring, 0.55f + 0.3f * Mathf.Sin(t * 12f)); }
            if (cross != null) { SetRing(cross, target, leapImpactRadius * 0.45f); SetRingAlpha(cross, 0.7f); }
            UpdateFootMarks(footMarks, target, groundY, 0.7f);
            yield return null;
        }

        // 锁定：落点不再变，圈变亮、快闪
        t = 0f;
        while (t < leapLockSeconds)
        {
            if (Dead) break;
            t += Time.deltaTime;
            float u = t / leapLockSeconds;
            if (ring != null) SetRingAlpha(ring, 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(t * (18f + 20f * u))));
            if (cross != null) { SetRing(cross, target, Mathf.Lerp(leapImpactRadius * 0.45f, leapImpactRadius, u)); SetRingAlpha(cross, 1f); }
            UpdateFootMarks(footMarks, target, groundY, 1f);
            yield return null;
        }

        // 起跳：水平朝落点冲过去，视觉上高高跃起(抛物线)，四腿收起
        Vector3 from = transform.position;
        Vector2 dir = new Vector2(target.x - from.x, target.z - from.z);
        float dist = dir.magnitude;
        if (dist > 0.05f && _movement != null) _movement.StartChargeDash(dir.normalized, dist, leapAirSeconds);
        for (int i = 0; i < _legs.Count; i++) if (!_legs[i].broken) _legs[i].planted = false;
        t = 0f;
        while (t < leapAirSeconds)
        {
            if (Dead) break;
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / leapAirSeconds);
            float h = leapHeight * 4f * u * (1f - u);
            _bodyDrop = Mathf.Lerp(1.1f, 0f, Mathf.Clamp01(u * 4f));
            _bodyLift = Vector3.up * h;
            Vector3 bp = transform.position;
            for (int i = 0; i < _legs.Count; i++)
            {
                var l = _legs[i];
                if (l.broken) continue;
                l.foot = new Vector3(bp.x + l.offset.x * _face * 0.6f, groundY + h * 0.9f + 0.4f, bp.z + l.offset.y * 0.6f);
            }
            yield return null;
        }

        // 落地：四腿钉在身体四周，落点范围重击 + 巨大冲击波 + 大片扬尘
        if (_movement != null) _movement.CancelChargeDash();
        _bodyLift = Vector3.zero; _bodyDrop = 0f;
        Vector3 land = transform.position; land.y = groundY;
        for (int i = 0; i < _legs.Count; i++)
        {
            var l = _legs[i];
            if (l.broken) continue;
            l.anchor = new Vector3(land.x + l.offset.x * _face, groundY, land.z + l.offset.y);
            l.foot = l.anchor; l.planted = true;
        }
        if (ring != null) Destroy(ring.gameObject);
        if (cross != null) Destroy(cross.gameObject);
        for (int i = 0; i < footMarks.Length; i++) if (footMarks[i] != null) Destroy(footMarks[i].gameObject);
        if (!Dead)
        {
            for (int i = 0; i < _legs.Count; i++) if (!_legs[i].broken) SpawnDust(_legs[i].anchor);
            SpawnDust(land);
            if (_playerId != null && PlayerPlanarDist(land) <= leapImpactRadius && PlayerHeightAboveGround() < 1.2f)
                HitPlayer(land, leapImpactDamage, 0.8f, 9f);
            // 巨大冲击波由四只脚各踩一圈叠成：从四个脚的位置同时扩出(命中判定同一波只打一次，不会四倍伤害)
            StartCoroutine(MultiWaveRoutine(UnbrokenAnchors(), leapWaveMaxRadius, leapWaveSpeed, leapWaveDamage, 0.6f, 8f));
        }
        yield return new WaitForSeconds(leapRecoverySeconds);   // 落地硬直：这是最大的输出窗口
    }

    private Vector3[] UnbrokenAnchors()
    {
        var list = new List<Vector3>();
        for (int i = 0; i < _legs.Count; i++) if (!_legs[i].broken) list.Add(_legs[i].anchor);
        if (list.Count == 0) list.Add(transform.position);
        return list.ToArray();
    }

    // 落点预告里四只脚各自的小圈：落点中心 + 腿的站位偏移
    private void UpdateFootMarks(LineRenderer[] marks, Vector3 center, float groundY, float alpha)
    {
        for (int i = 0; i < marks.Length && i < _legs.Count; i++)
        {
            if (marks[i] == null) continue;
            var o = _legs[i].offset;
            SetRing(marks[i], new Vector3(center.x + o.x * _face, groundY, center.z + o.y), 0.9f);
            SetRingAlpha(marks[i], alpha);
        }
    }

    // 多个脚点同时各扩一圈同样的环形波；玩家只会被「最先扫到」的那一圈打一次
    private IEnumerator MultiWaveRoutine(Vector3[] centers, float maxRadius, float speed, int damage, float stun, float knockback)
    {
        var rings = new LineRenderer[centers.Length];
        var walls = new MeshRenderer[centers.Length];
        for (int i = 0; i < centers.Length; i++) { rings[i] = showWaveRing ? CreateRing(centers[i], 0.2f, 1f) : null; walls[i] = showWaveRing ? CreateWall() : null; }
        float r = 0f;
        bool hit = false;
        while (r < maxRadius)
        {
            if (Dead) break;
            r += speed * Time.deltaTime;
            for (int i = 0; i < centers.Length; i++)
            {
                if (rings[i] != null) { SetRing(rings[i], centers[i], r); SetRingAlpha(rings[i], 0.9f * (1f - r / maxRadius)); }
                SetWall(walls[i], centers[i], r, leapWaveWallHeight, waveWallAlpha * 0.6f * (1f - r / maxRadius));   // 四个脚点叠在一起，单块淡一点
            }
            if (!hit && _playerId != null && PlayerHeightAboveGround() <= airSafeHeight)
            {
                for (int i = 0; i < centers.Length; i++)
                {
                    float d = PlayerPlanarDist(centers[i]);
                    if (d <= r && d >= r - waveBand) { hit = true; HitPlayer(centers[i], damage, stun, knockback); break; }
                }
            }
            yield return null;
        }
        for (int i = 0; i < rings.Length; i++) { if (rings[i] != null) Destroy(rings[i].gameObject); if (walls[i] != null) Destroy(walls[i].gameObject); }
    }

    // ── 腿的接触伤害 ────────────────────────────────────────────
    private void UpdateLegContact()
    {
        if (_playerId == null) return;
        for (int i = 0; i < _legs.Count; i++)
        {
            var l = _legs[i];
            if (l.broken || !l.planted || Time.time < l.nextContactTime) continue;
            if (PlayerPlanarDist(l.foot) > contactRadius || PlayerHeightAboveGround() > 2.5f) continue;
            l.nextContactTime = Time.time + contactInterval;
            HitPlayer(l.foot, contactDamage, 0.15f, 3f);
        }
    }

    // ── 工具 ────────────────────────────────────────────────────
    private void CachePlayer(SkyPrisonUnitRuntimeIdentity p)
    {
        if (p == _playerId && _playerHealth != null) return;
        _playerId = p;
        _playerHealth = p.GetComponentInChildren<UnitHealthController>(true);
        _playerMove = p.GetComponentInChildren<UnitMovementController>(true);
    }

    private float PlayerPlanarDist(Vector3 c)
    {
        Vector3 pp = _playerId.transform.position;
        float dx = pp.x - c.x, dz = pp.z - c.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // 玩家脚底离"当前地面"的高度：跳起来 / 站在高处都算
    private float PlayerHeightAboveGround()
    {
        if (_playerId == null) return 0f;
        float y = _playerId.transform.position.y;
        float ground = _playerMove != null ? _playerMove.CurrentGroundY : transform.position.y;
        float h = y - ground;
        if (_playerMove != null && _playerMove.IsJumping) h = Mathf.Max(h, airSafeHeight + 0.01f);
        return h;
    }

    private void HitPlayer(Vector3 from, int damage, float stun, float knockback)
    {
        if (_playerHealth == null || _playerHealth.IsDead) return;
        var act = _playerHealth.GetComponent<UnitActionController>() ?? _playerHealth.GetComponentInParent<UnitActionController>();
        bool dodging = act != null && act.IsDodging;
        SkyPrisonAIDash.ApplyFlatDamage(gameObject, _playerHealth, Mathf.RoundToInt(damage * DamageBoost), damageTypeKey);
        if (dodging || _playerHealth.IsDead) return;
        if (stun > 0f && act != null) act.ForceHitStun(stun);
        if (knockback > 0f && _playerMove != null)
        {
            float dir = Mathf.Sign(_playerId.transform.position.x - from.x);
            if (dir == 0f) dir = 1f;
            _playerMove.ApplyKnockback(new Vector3(dir * knockback, 0f, 0f));
        }
    }

    private void SubmitMove(Vector2 move)
    {
        if (_action != null) _action.SubmitMoveIntent(move, false, false);
        else if (_movement != null) _movement.SetExternalMoveInput(move, false);
    }

    private void SpawnDust(Vector3 pos)
    {
        if (stompDustPrefabs == null || stompDustPrefabs.Length == 0) return;
        var vfx = FootstepVFXManager.Instance;
        if (vfx == null) return;
        int sorting = Mathf.RoundToInt(pos.z * 100f);
        for (int i = 0; i < stompDustPrefabs.Length; i++)
        {
            if (stompDustPrefabs[i] == null) continue;
            vfx.SpawnDust(new[] { stompDustPrefabs[i] }, pos, Quaternion.identity, stompDustScale, sorting, stompDustLifetime);
        }
    }

    private static Material LineMat()
    {
        if (_lineMat == null) _lineMat = new Material(Shader.Find("Sprites/Default"));
        return _lineMat;
    }

    private LineRenderer CreateRing(Vector3 center, float radius, float alpha)
    {
        var go = new GameObject("ShattererRing");
        int layer = LayerMask.NameToLayer("World3D");
        if (layer >= 0) go.layer = layer;
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = 48;
        lr.startWidth = lr.endWidth = 0.22f;
        lr.alignment = LineAlignment.View;
        lr.material = new Material(LineMat());
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        SetRing(lr, center, radius);
        SetRingAlpha(lr, alpha);
        _temp.Add(go);
        return lr;
    }

    // 单位圆柱面(半径 1、高 1)：底边 alpha=1、顶边 alpha=0 的顶点色，向上透明渐变。Sprites/Default 双面、吃顶点色
    private static Mesh GetWallMesh()
    {
        if (_wallMesh != null) return _wallMesh;
        const int seg = 72;
        var verts = new Vector3[(seg + 1) * 2];
        var cols = new Color[verts.Length];
        var tris = new int[seg * 6];
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts[i * 2] = dir;                           // 底
            verts[i * 2 + 1] = dir + Vector3.up;          // 顶
            cols[i * 2] = new Color(1f, 1f, 1f, 1f);
            cols[i * 2 + 1] = new Color(1f, 1f, 1f, 0f);
        }
        for (int i = 0; i < seg; i++)
        {
            int b = i * 2, t = 0;
            tris[t = i * 6] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
            tris[t + 3] = b + 1; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
        }
        _wallMesh = new Mesh { name = "ShattererWaveWall", hideFlags = HideFlags.HideAndDontSave };
        _wallMesh.vertices = verts; _wallMesh.colors = cols; _wallMesh.triangles = tris;
        _wallMesh.bounds = new Bounds(Vector3.up * 0.5f, new Vector3(2f, 1f, 2f));
        _wallMesh.UploadMeshData(true);
        return _wallMesh;
    }

    private MeshRenderer CreateWall()
    {
        var go = new GameObject("ShattererWaveWall");
        int layer = LayerMask.NameToLayer("World3D");
        if (layer >= 0) go.layer = layer;
        go.AddComponent<MeshFilter>().sharedMesh = GetWallMesh();
        var mr = go.AddComponent<MeshRenderer>();
        mr.material = new Material(LineMat());
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        _temp.Add(go);
        return mr;
    }

    private static void SetWall(MeshRenderer wall, Vector3 center, float radius, float height, float alpha)
    {
        if (wall == null) return;
        var tr = wall.transform;
        tr.position = center + Vector3.up * 0.03f;
        tr.localScale = new Vector3(Mathf.Max(0.01f, radius), height, Mathf.Max(0.01f, radius));
        wall.material.color = new Color(1f, 0.55f, 0.55f, Mathf.Clamp01(alpha));
    }

    private static void SetRing(LineRenderer lr, Vector3 center, float radius)
    {
        int n = lr.positionCount;
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0.06f, Mathf.Sin(a) * radius));
        }
    }

    private static void SetRingAlpha(LineRenderer lr, float alpha)
    {
        var c = new Color(1f, 0.55f, 0.55f, Mathf.Clamp01(alpha));
        lr.startColor = lr.endColor = c;
        if (lr.material != null) lr.material.color = c;
    }
}
