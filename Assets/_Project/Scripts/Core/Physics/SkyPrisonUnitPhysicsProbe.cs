using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Isolated push probe for units. Attach this to a child object on the unit.
/// The child object should be on UnitPhysicsProbe layer and should have a Trigger collider.
/// It finds PushableProp objects by overlap and pushes SkyPrisonPushablePropRuntime.
///
/// V2 - 2026-05-22 - Cached / throttled runtime probe
/// - Caches Rigidbody lookup.
/// - Caches Collider -> SkyPrisonPushablePropRuntime lookup.
/// - Avoids per-FixedUpdate debug string churn.
/// - Deduplicates pushable targets when one prop has multiple colliders.
/// - Throttles empty scans when the unit is not moving and has no held push direction.
/// </summary>
[DisallowMultipleComponent]
public sealed class SkyPrisonUnitPhysicsProbe : MonoBehaviour
{
    public enum FlattenAxis
    {
        X = 0,
        Y = 1,
        Z = 2,
    }

    [Header("Version")]
    [SerializeField] private string scriptVersion = "V2 - 2026-05-22 - cached throttled physics probe";

    [Header("References")]
    [Tooltip("Usually the player/unit root transform. If empty, transform.root is used.")]
    public Transform velocitySource;

    [Header("Probe")]
    public LayerMask pushableLayerMask;
    public bool includeTriggerColliders = true;
    public float probeRadius = 0.75f;
    public Vector3 localProbeOffset = new Vector3(0.55f, 0f, 0f);
    [Tooltip("默认开启：把 Y 当作高度轴，只在 XZ 地面平面计算推动方向。这样往地图上/下推也会得到 Z 方向推动。")]
    public bool useHorizontalGroundPlane = true;
    [Tooltip("当 useHorizontalGroundPlane 关闭时，用这个轴作为要压扁的深度轴。横版 X/Y 平面可设为 Z。俯视/2.5D 地面 X/Z 平面设为 Y。")]
    public FlattenAxis depthAxis = FlattenAxis.Y;

    [Header("Push")]
    public float minimumSourceSpeed = 0.02f;
    public float impulsePerSecond = 32f;
    public float maxImpulsePerFixedStep = 3.5f;
    public float fallbackImpulseWhenOverlapping = 0.18f;
    [Tooltip("同一个可推动道具被推过一次之后，这么多秒内不再重复推——闪避这类瞬间高速位移，" +
             "如果角色跟道具还保持物理重叠，之前每个FixedUpdate都会用接近上限的冲量再推一次，" +
             "闪避这几帧累积下来总冲量远超正常走路顶一下，会把道具直接顶穿进旁边的墙体/集装箱。" +
             "加这个冷却之后，同一次接触只按最开始那一下的冲量算，不会因为闪避帧数多就被反复叠加。")]
    public float pushCooldownSeconds = 0.12f;
    [Tooltip("默认开启：推动方向优先使用玩家实际移动方向。即使玩家被物体挡住速度变低，也会短时间沿最后一次有效移动方向继续推。")]
    public bool usePlayerMotionDirection = true;
    [Tooltip("玩家速度太低时，继续沿最后一次有效移动方向推的保留时间。这样按住上/下/左/右顶着物体时，方向不会突然变成玩家到物体的连线。")]
    public float cachedDirectionHoldSeconds = 0.35f;
    [Tooltip("应急兜底：只有完全没有玩家移动方向缓存时，才按 玩家 -> 被推物体 的连线方向推。默认关闭，避免贴着侧面往上推时物体却往侧边跑。")]
    public bool useSourceToPropDirectionWhenNoMotion = false;

    [Header("Performance")]
    [Tooltip("没有移动方向、没有命中时，隔几次 FixedUpdate 才做一次空扫描。正在移动/有缓存方向/上次有命中时仍每帧检测。")]
    [SerializeField] private int idleEmptyScanInterval = 3;

    [Tooltip("缓存 Collider 到 PushableRuntime 的查找结果，避免 FixedUpdate 中反复 GetComponentInParent。")]
    [SerializeField] private bool cachePushableLookup = true;

    [Tooltip("同一个可推动物有多个 Collider 命中时，每个 FixedUpdate 只推一次。")]
    [SerializeField] private bool deduplicatePushablesPerStep = true;

    [Tooltip("运行时默认关闭字符串调试字段写入，避免 FixedUpdate 热路径持续写 hit.name / pushable.name。")]
    [SerializeField] private bool updateDebugNamesAtRuntime = false;

    [Header("Debug / Read Only")]
    [SerializeField] private Vector3 debugSourceVelocity;
    [SerializeField] private Vector3 debugPushDirection;
    [SerializeField] private int debugHitCount;
    [SerializeField] private string debugLastHitName = "-";
    [SerializeField] private string debugLastPushableName = "-";
    [SerializeField] private float debugLastImpulse;
    [SerializeField] private float debugLastPushTime;
    [SerializeField] private int debugLookupCacheCount;
    [SerializeField] private int debugSkippedIdleScans;

    private readonly Collider[] overlapBuffer = new Collider[16];
    private readonly Dictionary<Collider, SkyPrisonPushablePropRuntime> pushableByCollider = new Dictionary<Collider, SkyPrisonPushablePropRuntime>(32);
    private readonly List<SkyPrisonPushablePropRuntime> pushedThisStep = new List<SkyPrisonPushablePropRuntime>(8);
    private readonly Dictionary<SkyPrisonPushablePropRuntime, float> lastPushTimeByPushable = new Dictionary<SkyPrisonPushablePropRuntime, float>(8);

    private Transform cachedSource;
    private Rigidbody cachedSourceRb;
    private Vector3 lastSourcePosition;
    private bool hasLastSourcePosition;
    private Vector3 lastValidPlanarPushDirection;
    private float lastValidPlanarPushTime = -999f;
    private int idleScanCursor;
    private bool hadHitLastFixedStep;

    private void Reset()
    {
        velocitySource = transform.root;
        includeTriggerColliders = true;
        probeRadius = 0.75f;
        localProbeOffset = new Vector3(0.55f, 0f, 0f);
        useHorizontalGroundPlane = true;
        depthAxis = FlattenAxis.Y;
        minimumSourceSpeed = 0.02f;
        impulsePerSecond = 32f;
        maxImpulsePerFixedStep = 3.5f;
        fallbackImpulseWhenOverlapping = 0.18f;
        pushCooldownSeconds = 0.12f;
        usePlayerMotionDirection = true;
        cachedDirectionHoldSeconds = 0.35f;
        useSourceToPropDirectionWhenNoMotion = false;
        idleEmptyScanInterval = 3;
        cachePushableLookup = true;
        deduplicatePushablesPerStep = true;
        updateDebugNamesAtRuntime = false;

        int pushableLayer = LayerMask.NameToLayer("PushableProp");
        if (pushableLayer >= 0)
            pushableLayerMask = 1 << pushableLayer;

        Collider col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    private void Awake()
    {
        ResolveSourceIfNeeded(true);

        if (pushableLayerMask.value == 0)
        {
            int pushableLayer = LayerMask.NameToLayer("PushableProp");
            if (pushableLayer >= 0)
                pushableLayerMask = 1 << pushableLayer;
        }

        if (cachedSource != null)
        {
            lastSourcePosition = cachedSource.position;
            hasLastSourcePosition = true;
        }
    }

    private void OnDisable()
    {
        pushedThisStep.Clear();
        hadHitLastFixedStep = false;
    }

    private void FixedUpdate()
    {
        Transform source = ResolveSourceIfNeeded(false);
        if (source == null)
            return;

        Vector3 sourceVelocity = ResolveSourceVelocity(source);
        lastSourcePosition = source.position;
        hasLastSourcePosition = true;

        Vector3 planarVelocity = Flatten(sourceVelocity);
        float speed = planarVelocity.magnitude;
        float minSpeedSqr = minimumSourceSpeed * minimumSourceSpeed;

        bool hasFreshMotionDirection = usePlayerMotionDirection && planarVelocity.sqrMagnitude >= minSpeedSqr;
        if (hasFreshMotionDirection)
        {
            lastValidPlanarPushDirection = planarVelocity.normalized;
            lastValidPlanarPushTime = Time.time;
        }

        bool hasHeldDirection =
            usePlayerMotionDirection &&
            lastValidPlanarPushDirection.sqrMagnitude > 0.0001f &&
            Time.time - lastValidPlanarPushTime <= Mathf.Max(0f, cachedDirectionHoldSeconds);

        debugSourceVelocity = sourceVelocity;
        debugHitCount = 0;
        debugLastImpulse = 0f;

        // If the unit is idle, has no recent push direction, and did not hit anything last step,
        // do not run a physics overlap every single FixedUpdate.
        int interval = Mathf.Max(1, idleEmptyScanInterval);
        if (!hadHitLastFixedStep && !hasFreshMotionDirection && !hasHeldDirection && interval > 1)
        {
            idleScanCursor++;
            if (idleScanCursor % interval != 0)
            {
                debugSkippedIdleScans++;
                return;
            }
        }

        idleScanCursor = 0;

        Vector3 center = transform.TransformPoint(localProbeOffset);
        int hitCount = Physics.OverlapSphereNonAlloc(
            center,
            Mathf.Max(0.01f, probeRadius),
            overlapBuffer,
            pushableLayerMask,
            includeTriggerColliders ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);

        debugHitCount = hitCount;
        hadHitLastFixedStep = hitCount > 0;

        if (hitCount <= 0)
            return;

        float impulse = speed >= minimumSourceSpeed
            ? Mathf.Min(maxImpulsePerFixedStep, speed * impulsePerSecond * Time.fixedDeltaTime)
            : fallbackImpulseWhenOverlapping;

        if (deduplicatePushablesPerStep)
            pushedThisStep.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapBuffer[i];
            if (hit == null)
                continue;

            SkyPrisonPushablePropRuntime pushable = ResolvePushable(hit);
            if (pushable == null)
                continue;

            if (deduplicatePushablesPerStep)
            {
                bool alreadyPushed = false;
                for (int p = 0; p < pushedThisStep.Count; p++)
                {
                    if (pushedThisStep[p] == pushable)
                    {
                        alreadyPushed = true;
                        break;
                    }
                }

                if (alreadyPushed)
                    continue;

                pushedThisStep.Add(pushable);
            }

            Vector3 pushDirection = ResolvePushDirection(hit, pushable, source, center, planarVelocity, hasFreshMotionDirection, hasHeldDirection);
            if (pushDirection.sqrMagnitude < 0.0001f)
                continue;

            pushDirection.Normalize();
            debugPushDirection = pushDirection;

            if (!pushable.CanReceiveUnitProbePush())
                continue;

            if (pushCooldownSeconds > 0f
                && lastPushTimeByPushable.TryGetValue(pushable, out float lastPushTime)
                && Time.time - lastPushTime < pushCooldownSeconds)
                continue;

            if (updateDebugNamesAtRuntime || !Application.isPlaying)
            {
                debugLastHitName = hit.name;
                debugLastPushableName = pushable.name;
            }

            debugLastImpulse = impulse;
            debugLastPushTime = Time.time;
            lastPushTimeByPushable[pushable] = Time.time;

            pushable.ApplyPush(pushDirection, impulse, source.position);
        }

        debugLookupCacheCount = pushableByCollider.Count;
    }

    private Transform ResolveSourceIfNeeded(bool force)
    {
        Transform source = velocitySource != null ? velocitySource : transform.root;
        if (!force && cachedSource == source)
            return cachedSource;

        cachedSource = source;
        cachedSourceRb = cachedSource != null ? cachedSource.GetComponentInParent<Rigidbody>() : null;

        if (cachedSource != null && !hasLastSourcePosition)
        {
            lastSourcePosition = cachedSource.position;
            hasLastSourcePosition = true;
        }

        return cachedSource;
    }

    private Vector3 ResolveSourceVelocity(Transform source)
    {
        if (cachedSourceRb != null && !cachedSourceRb.isKinematic)
        {
#if UNITY_6000_0_OR_NEWER
            return cachedSourceRb.linearVelocity;
#else
            return cachedSourceRb.velocity;
#endif
        }

        if (hasLastSourcePosition)
            return (source.position - lastSourcePosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);

        return Vector3.zero;
    }

    private SkyPrisonPushablePropRuntime ResolvePushable(Collider hit)
    {
        if (hit == null)
            return null;

        if (cachePushableLookup && pushableByCollider.TryGetValue(hit, out SkyPrisonPushablePropRuntime cached))
            return cached;

        SkyPrisonPushablePropRuntime pushable = hit.GetComponentInParent<SkyPrisonPushablePropRuntime>();
        if (pushable == null)
            pushable = hit.GetComponent<SkyPrisonPushablePropRuntime>();

        if (cachePushableLookup)
            pushableByCollider[hit] = pushable;

        return pushable;
    }

    private Vector3 ResolvePushDirection(
        Collider hit,
        SkyPrisonPushablePropRuntime pushable,
        Transform source,
        Vector3 probeCenter,
        Vector3 planarVelocity,
        bool hasFreshMotionDirection,
        bool hasHeldDirection)
    {
        Vector3 pushDirection = Vector3.zero;

        if (usePlayerMotionDirection && hasFreshMotionDirection)
        {
            pushDirection = planarVelocity;
        }
        else if (usePlayerMotionDirection && hasHeldDirection)
        {
            pushDirection = lastValidPlanarPushDirection;
        }
        else if (useSourceToPropDirectionWhenNoMotion)
        {
            Vector3 propCenter = hit.bounds.center;
            Collider propCollider = pushable.GetComponentInChildren<Collider>();
            if (propCollider != null)
                propCenter = propCollider.bounds.center;

            pushDirection = Flatten(propCenter - source.position);
        }

        if (pushDirection.sqrMagnitude < 0.0001f)
            pushDirection = Flatten(hit.bounds.center - probeCenter);

        if (pushDirection.sqrMagnitude < 0.0001f)
            pushDirection = Flatten(transform.right);

        return pushDirection;
    }

    private Vector3 Flatten(Vector3 v)
    {
        if (useHorizontalGroundPlane)
        {
            v.y = 0f;
            return v;
        }

        switch (depthAxis)
        {
            case FlattenAxis.X: v.x = 0f; break;
            case FlattenAxis.Y: v.y = 0f; break;
            default: v.z = 0f; break;
        }
        return v;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(transform.TransformPoint(localProbeOffset), Mathf.Max(0.01f, probeRadius));
    }
}
