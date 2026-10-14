using UnityEngine;

/// <summary>
/// 通用站位组件：让不走 AI 包的怪（咏唱者这类）按"射程带 + 深度对齐"自己调整位置。
/// 现有 AI 词条读不到目标的 X 差/深度差，也算不出位置，所以单独封装。
/// 规则：
///   · X 方向：离玩家比 maxDistanceX 远就靠近；比 minDistanceX 近就后退；带内不动。
///   · 深度(Z)方向：与玩家的深度差超过 depthTolerance 就挪过去，让攻击打得到（子弹只沿 X 飞）。
///   · 只在玩家处于 engageRangeX 内才动；Locked=true（开火/硬直期间）时一律站定。
/// 移动通过 UnitMovementController.SetExternalMoveInput 驱动，速度取单位定义里的行走速度。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonRangedPositioning : MonoBehaviour
{
    [Header("射程带（X 方向）")]
    [SerializeField] private float minDistanceX = 0f;
    [SerializeField] private float maxDistanceX = 18f;

    [Tooltip("玩家贴得比这个距离还近：中止当前蓄势/开火，优先后退(奔跑速度)，退出去再射。")]
    [SerializeField] private float abortDistanceX = 0f;

    [Header("深度对齐（Z 方向）")]
    [Tooltip("与玩家深度差在这个范围内就不再挪。")]
    [SerializeField] private float depthTolerance = 1.0f;
    [Tooltip("开始挪动后，深度差小于这个值才停下。比 depthTolerance 小，形成回差，避免在边缘来回抖、脚步声密集。")]
    [SerializeField] private float depthSettle = 0.3f;
    [Tooltip("X 方向：开始挪动后，进入带内这么多米才停下（回差）。")]
    [SerializeField] private float bandSettleMargin = 1.5f;

    [Header("触发")]
    [Tooltip("玩家在这个水平距离内才会调整位置。")]
    [SerializeField] private float engageRangeX = 26f;

    private UnitMovementController _movement;
    private UnitActionController _action;
    private UnitDeathController _death;
    private bool _wasMoving;
    private bool _playerTooClose;
    private bool _movingZ;
    private bool _movingX;

    /// <summary>玩家是否已经贴到枪口（应优先后退，不该继续开火）。</summary>
    public bool PlayerTooClose => _playerTooClose;

    /// <summary>AI 词条"保持距离带"用：运行时改射程带和深度容差。</summary>
    public void Configure(float minX, float maxX, float depthTol)
    {
        minDistanceX = Mathf.Max(0f, minX);
        maxDistanceX = Mathf.Max(minDistanceX + 0.5f, maxX);
        depthTolerance = Mathf.Max(0.1f, depthTol);
        depthSettle = Mathf.Min(depthSettle, depthTolerance * 0.5f);
        engageRangeX = Mathf.Max(engageRangeX, maxDistanceX + 6f);
    }

    // 目标：默认玩家（咏唱者等原有用法不变）；AI 词条可改成最近敌人/友军/出生点/指定点位/当前目标
    private string _targetKind = SkyPrisonAITargetResolver.Player;
    private Vector2 _targetPoint;
    private System.Func<Transform> _currentTargetGetter;
    private Vector3 _targetSpawn;

    public void SetTarget(string kind, Vector2 point, System.Func<Transform> currentTargetGetter, Vector3 spawnPosition)
    {
        _targetKind = string.IsNullOrEmpty(kind) ? SkyPrisonAITargetResolver.Player : kind;
        _targetPoint = point;
        _currentTargetGetter = currentTargetGetter;
        _targetSpawn = spawnPosition;
    }

    private bool TryGetTargetPosition(out Vector3 pos)
    {
        pos = default;
        if (_targetKind == SkyPrisonAITargetResolver.Player)
        {
            var p = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
            if (p == null) return false;
            pos = p.transform.position;
            return true;
        }
        Transform cur = _currentTargetGetter != null ? _currentTargetGetter() : null;
        return SkyPrisonAITargetResolver.TryResolve(_targetKind, transform, cur, _targetSpawn, _targetPoint, out pos);
    }

    /// <summary>开关：关闭时停下并清朝向覆盖，不再驱动移动。</summary>
    public void SetActive(bool on)
    {
        if (enabled == on) return;
        if (!on)
        {
            Stop();
            if (_movement != null) _movement.ClearFacingOverride();
        }
        enabled = on;
    }

    /// <summary>开火/硬直期间由咏唱者组件置 true，期间原地站定。</summary>
    public bool Locked { get; set; }

    /// <summary>当前位置能不能开火：X 在射程带内、深度基本对齐。</summary>
    public bool IsInFiringPosition(Vector3 deltaToTarget)
    {
        float absDx = Mathf.Abs(deltaToTarget.x);
        return absDx >= minDistanceX && absDx <= maxDistanceX
            && Mathf.Abs(deltaToTarget.z) <= depthTolerance;
    }

    // 摆在场景里的单位：子组件是运行时才创建的，Awake 时多半还没有——引用在用到时补取。
    private bool _refsReady;

    private void EnsureRefs()
    {
        if (_refsReady) return;
        _movement = GetComponentInChildren<UnitMovementController>(true);
        if (_movement == null) return;
        _action = GetComponentInChildren<UnitActionController>(true);
        _death = GetComponentInChildren<UnitDeathController>(true);
        _movement.ConfigureAsExternalExecutor();
        _refsReady = true;
    }

    private void Awake() { EnsureRefs(); }

    private void Start() { EnsureRefs(); }

    private void Update()
    {
        EnsureRefs();
        if (_movement == null) return;

        if (_death != null && _death.IsDeadLike)
        {
            Stop();
            return;
        }

        if (!TryGetTargetPosition(out Vector3 targetPos)) { Stop(); return; }

        Vector3 delta = targetPos - transform.position;
        float absDx = Mathf.Abs(delta.x);
        _playerTooClose = absDx < abortDistanceX && Mathf.Abs(delta.z) < 3f;
        if (absDx > engageRangeX) { _movement.ClearFacingOverride(); Stop(); return; }

        // 始终面朝玩家（开火/硬直期间也转身，只是不挪位）
        if (absDx > 0.3f)
            _movement.SetFacingOverride(new Vector2(Mathf.Sign(delta.x), 0f));

        // 贴脸：无视 Locked（硬直之外由 barrage 中止蓄势），先后退
        if (Locked && !_playerTooClose) { Stop(); return; }

        // X：带外才开始挪；开始后走到带内一段距离才停（回差）
        float moveX = 0f;
        bool outsideBand = absDx > maxDistanceX || absDx < minDistanceX;
        bool insideSettled = absDx <= maxDistanceX - bandSettleMargin && absDx >= minDistanceX + bandSettleMargin;
        if (_movingX ? !insideSettled : outsideBand)
        {
            _movingX = true;
            moveX = absDx > (minDistanceX + maxDistanceX) * 0.5f ? Mathf.Sign(delta.x) : -Mathf.Sign(delta.x);
        }
        else _movingX = false;

        // Z：偏出容差才开始挪；开始后对到很准才停（回差）
        float moveZ = 0f;
        float absDz = Mathf.Abs(delta.z);
        if (_movingZ ? absDz > depthSettle : absDz > depthTolerance)
        {
            _movingZ = true;
            moveZ = Mathf.Sign(delta.z);
        }
        else _movingZ = false;

        if (moveX == 0f && moveZ == 0f) { Stop(); return; }

        SubmitMove(new Vector2(moveX, moveZ), _playerTooClose);
        _wasMoving = true;
    }

    private void Stop()
    {
        if (!_wasMoving) return;
        SubmitMove(Vector2.zero, false);
        _wasMoving = false;
    }

    // 必须走 UnitActionController：它每帧在 PushMovementIntent 里用自己的 moveInput
    // 覆盖移动控制器的输入，直接喂移动控制器会被冲掉。
    private void SubmitMove(Vector2 move, bool run)
    {
        if (_action != null) _action.SubmitMoveIntent(move, run, false);
        else _movement.SetExternalMoveInput(move, run);
    }
}
