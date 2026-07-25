using UnityEngine;

/// <summary>
/// Independent pushable prop runtime for physics tests.
/// Does not reference terrain decoration systems.
///
/// Key behavior:
/// - Starts kinematic so props do not jump on spawn.
/// - Receives pushes from SkyPrisonUnitPhysicsProbe via ApplyPush.
/// - Can slide with physics.
/// - Can enter a controlled pivot-tip knockdown.
/// - During pivot-tip, clamps visual bottom against GroundPhysics so the prop neither sinks underground nor gets launched upward by penetration resolution.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class SkyPrisonPushablePropRuntime : MonoBehaviour
{
    public enum KnockdownMotionMode
    {
        PhysicsTorque = 0,
        PivotTipKinematic = 1,
    }

    public enum FlattenAxis
    {
        X = 0,
        Y = 1,
        Z = 2,
    }

    [Header("Collision Response Flags")]
    public bool receiveVolumeCollision = true;
    public bool receiveAttackImpulse = true;
    public bool receiveExplosionImpulse = true;
    public bool receiveScriptedImpulse = true;

    public bool ReceiveVolumeCollision => receiveVolumeCollision;
    public bool ReceiveAttackImpulse => receiveAttackImpulse;
    public bool ReceiveExplosionImpulse => receiveExplosionImpulse;
    public bool ReceiveScriptedImpulse => receiveScriptedImpulse;

    [Header("Startup")]
    public bool stayKinematicUntilPushed = true;
    public bool useGravityAfterActivated = false;
    public bool returnToKinematicWhenStable = true;

    [Header("Movement Plane")]
    [Tooltip("默认开启：把 Y 当作高度轴，只在 XZ 地面平面推动/滚动。关闭后可用 depthAxis 指定要压扁的轴。")]
    public bool useHorizontalGroundPlane = true;
    [Tooltip("当 useHorizontalGroundPlane 关闭时，用这个轴作为深度/高度轴，从推动方向中剔除。横版 X/Y 平面可设为 Z。俯视/2.5D 地面 X/Z 平面设为 Y。")]
    public FlattenAxis depthAxis = FlattenAxis.Y;

    [Header("Physics")]
    public float mass = 0.35f;
    public float linearDamping = 6f;
    public float angularDamping = 9f;
    public float maxPlanarSpeed = 3.5f;

    [Header("Push")]
    public float externalPushMultiplier = 2.2f;
    public float minimumImpulse = 0.02f;
    public bool applyForceAtTop = true;
    public bool useColliderTopPoint = true;
    [Range(0f, 1.5f)] public float colliderTopRatio = 0.85f;
    public float topForceHeight = 0.9f;
    public float topForceMultiplier = 2.0f;

    [Header("Pure Real Physics Mode")]
    [Tooltip("开启后，玩家探针第一次碰到物体时只负责激活动态刚体；后续推倒、滚动、滑动都交给角色实体碰撞 + Rigidbody + 地面碰撞。")]
    public bool pureRealPhysicsFromFirstPush = true;
    [Tooltip("开启后，ApplyPush 不再给 AddForce / AddForceAtPosition。探针只唤醒刚体，避免脚本外力干扰真实物理。")]
    public bool scriptedPushOnlyActivatesRealPhysics = true;

    [Header("Paper Doll Push")]
    [Tooltip("开启后，玩家探针只触发表演式推倒，不让道具和角色进入真实刚体角力。")]
    public bool paperDollPushMode = false;
    [Tooltip("开启后，只要被探针推到就直接进入 PivotTipKinematic 推倒，不先做滑动受力。")]
    public bool paperDollTriggerKnockdownImmediately = false;
    [Tooltip("开启后，纸娃娃模式下不会执行 AddForce / AddForceAtPosition。")]
    public bool paperDollSuppressPhysicsForce = false;
    [Tooltip("开启后，纸娃娃推倒结束后保持 Kinematic，避免倒地后继续弹跳或反推玩家。后续再次被探针推到时，会临时释放成轻量刚体继续滑动。")]
    public bool paperDollKeepKinematicAfterTip = true;
    [Tooltip("开启后，物体已经倒下后仍然可以被玩家探针继续推动。")]
    public bool paperDollAllowPushAfterKnockdown = true;
    [Tooltip("物体倒下后再次被推时的滑动倍率。建议低于 1，避免倒地物体像活物一样反冲。")]
    [Range(0.05f, 2f)] public float paperDollPostKnockdownPushMultiplier = 0.85f;
    [Tooltip("倒地后被再次推动时，是否使用真实刚体受力，而不是直接改速度。开启后，上/下/左/右都会按平面方向继续滚动或滑动。")]
    public bool paperDollPostKnockdownUseRealPhysics = true;
    [Tooltip("倒地后被推时是否用偏心受力点制造滚动。关闭则只给中心推力。")]
    public bool paperDollPostKnockdownForceAtEdge = true;
    [Tooltip("倒地后被推时额外追加的滚动扭矩倍率。")]
    [Range(0f, 4f)] public float paperDollPostKnockdownTorqueMultiplier = 1.2f;
    [Tooltip("倒地后被推动时临时使用的线性阻尼，太大就像粘住地面，太小会滑太远。")]
    public float paperDollPostKnockdownLinearDamping = 2.0f;
    [Tooltip("倒地后被推动时临时使用的角阻尼，太大就不会滚。")]
    public float paperDollPostKnockdownAngularDamping = 1.2f;
    [Tooltip("倒地后被再次推动时，是否使用重力。一般保持开启，让道具贴地滑动。")]
    public bool paperDollUseGravityWhenPushedAfterKnockdown = true;
    [Tooltip("倒地后被推动时的最低有效冲量。避免 Probe 速度很低时，真实刚体受力小到看不出来。")]
    public float paperDollPostKnockdownMinimumImpulse = 0.65f;
    [Tooltip("倒地后被推动时的最低平面速度补偿。不是替代物理，而是保证 AddForce 后玩家能立刻看到道具被带动。")]
    public float paperDollPostKnockdownMinimumPlanarSpeed = 1.15f;
    [Tooltip("倒地后被推时是否立刻做地面保护。一般关闭，避免 GroundPhysics 层配置错误时把刚体重新钉住。")]
    public bool paperDollApplyGroundProtectionAfterPostPush = false;

    [Header("Real Physics Authority After Knockdown")]
    [Tooltip("开启后，倒地后的可推动物体主要交给 Unity Rigidbody / Collider / PhysicsMaterial 处理。脚本只负责初始推倒和异常防护，不再持续磁吸、姿态纠正或自动切回 Kinematic。")]
    public bool realPhysicsAuthorityAfterKnockdown = true;
    [Tooltip("PivotTip 表演结束后，即使 paperDollKeepKinematicAfterTip 开着，也释放成真实动态刚体。")]
    public bool realPhysicsReleaseAfterPivotTip = true;
    [Tooltip("真实物理模式下，稳定后不自动切回 Kinematic，让 Unity 自己 Sleep。")]
    public bool realPhysicsKeepDynamicWhenStable = true;
    [Tooltip("真实物理模式下跳过 GroundProtection / GroundMagnet 一类每帧贴地修正，避免和物理解算抢控制权。")]
    public bool realPhysicsSkipGroundProtection = true;
    [Tooltip("真实物理模式下跳过 maxPlanarSpeed 平面限速，避免把真实滚动削成悬浮滑块。")]
    public bool realPhysicsSkipPlanarSpeedLimit = true;
    [Tooltip("真实物理模式下允许 PushableProp 和 UnitBody / Character2D 发生实体碰撞，这样玩家顶住它时才能真实挡停。")]
    public bool realPhysicsAllowUnitBodyCollision = true;
    public bool realPhysicsUseGravity = true;
    public bool realPhysicsUseOffCenterImpulse = true;
    [Tooltip("真实物理模式下默认不额外加脚本扭矩。滚动主要交给偏心受力和碰撞形状。")]
    public bool realPhysicsUseExtraScriptTorque = false;
    [Range(0f, 4f)] public float realPhysicsExtraTorqueMultiplier = 0f;
    public float realPhysicsLinearDamping = 0.45f;
    public float realPhysicsAngularDamping = 0.35f;
    public float realPhysicsMaxAngularVelocity = 14f;
    public float realPhysicsMaxDepenetrationVelocity = 3f;
    [Tooltip("真实物理模式下不再给最低平面速度补偿。否则会像脚本推着走，不像真实碰撞。")]
    public bool realPhysicsDisableMinimumPlanarVelocity = true;
    [Tooltip("真实物理模式下强制保证 PushableProp × GroundPhysics 没有被 IgnoreLayerCollision 屏蔽。真正的地面接触靠这一层碰撞矩阵。")]
    public bool realPhysicsEnsureGroundCollision = true;
    [Tooltip("真实物理模式下使用连续碰撞，降低锥桶/路障被快速顶动时穿过地面的概率。")]
    public CollisionDetectionMode realPhysicsCollisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    [Tooltip("真实物理模式下提高求解迭代，让小物体和地面接触更稳定。")]
    public int realPhysicsSolverIterations = 12;
    public int realPhysicsSolverVelocityIterations = 4;

    [Header("Real Physics Emergency Sink Rescue")]
    [Tooltip("只在物体已经明显沉入地面时才救援。普通接触完全交给物理引擎，避免脚本每帧垫高导致升天。")]
    public bool realPhysicsEmergencySinkRescue = false;
    [Tooltip("Collider 底部低于地面超过这个深度才救援。数值越大，越不干预正常接触。")]
    public float realPhysicsEmergencySinkDepth = 0.35f;
    [Tooltip("单次救援最多上抬距离。只用于严重穿地，不参与正常贴地。")]
    public float realPhysicsEmergencyMaxLift = 0.18f;
    [Tooltip("救援后保留的离地皮肤厚度。")]
    public float realPhysicsEmergencyGroundSkin = 0.02f;
    [Tooltip("两次严重穿地救援之间的最短间隔。")]
    public float realPhysicsEmergencyCooldown = 0.5f;

    [Header("Ground Contact Debug")]
    public bool debugLogGroundCollisionContacts = false;
    public int debugGroundContactCount;
    public Vector3 debugLastGroundContactPoint;
    public Vector3 debugLastGroundContactNormal;
    public bool debugPushableGroundLayerIgnored;

    [Header("No Step Guard")]
    public bool ignoreUnitBodyAndCharacterCollision = true;
    public string unitBodyLayerName = "UnitBody";
    public string characterLayerName = "Character2D";
    public string pushableLayerName = "PushableProp";

    [Header("Knockdown")]
    public bool enableKnockdown = true;
    public KnockdownMotionMode knockdownMotionMode = KnockdownMotionMode.PhysicsTorque;
    public float knockdownImpulseThreshold = 0.2f;
    public float accumulatedKnockdownThreshold = 0.5f;
    public float accumulatedKnockdownDecayPerSecond = 2.5f;
    public float sustainedPushTimeThreshold = 0.05f;
    public bool oneKnockdownPerActivation = true;

    [Header("Physics Torque Knockdown")]
    public bool useGravityAfterKnockdown = true;
    public float knockdownTorqueImpulse = 8f;
    public float knockdownUpwardImpulse = 0f;
    public bool forceAngularVelocityOnKnockdown = true;
    public float forcedKnockdownAngularVelocity = 10f;
    public float forcedKnockdownDriveDuration = 0.35f;
    public bool lowerAngularDampingDuringKnockdown = true;
    public float knockdownAngularDamping = 0.25f;

    [Header("Pivot Tip Knockdown")]
    public bool enablePivotTipKnockdown = true;
    public bool keepKinematicDuringPivotTip = true;
    public bool useRendererBoundsForPivot = false;
    public bool raycastPivotToGround = true;
    public string groundLayerName = "World3D";
    [Range(0.1f, 1.5f)] public float pivotForwardEdgeRatio = 1f;
    public float pivotGroundSkin = 0.01f;
    public float pivotTipDegreesPerSecond = 220f;
    public float pivotTipMaxAngle = 82f;
    public bool invertPivotRotationDirection = false;
    public bool releaseRigidbodyAfterPivotTip = false;
    public bool useGravityAfterPivotRelease = true;
    public float pivotReleaseDownImpulse = 0.05f;
    public float pivotReleaseForwardImpulse = 0f;

    [Header("Ground Protection During Pivot")]
    public bool protectAgainstGroundPenetration = true;
    public bool preventLargeHoverDuringPivot = true;
    public float maxAllowedVisualHover = 0.03f;
    public float maxSnapDownPerStep = 0.08f;
    public float groundRayStartHeight = 0.75f;
    public float groundRayDistance = 3f;
    public float groundRayExtraTopHeight = 2.0f;
    public bool protectAfterPivotRelease = false;
    public bool useLastKnownGroundWhenRayMisses = false;
    public bool useFallbackGroundPlaneWhenRayMisses = false;
    public float fallbackGroundY = 0f;
    public bool zeroDownwardVelocityWhenSnappedUp = true;
    public bool zeroUpwardVelocityOnRelease = true;
    public bool syncTransformsBeforeRelease = true;

    [Header("Auto Sleep")]
    public float stableSpeed = 0.06f;
    public float stableAngularSpeed = 0.08f;
    public float stableTimeBeforeKinematic = 1.0f;

    [Header("Debug")]
    public bool drawDebugGizmos = true;
    public bool drawLivePreviewPivotWhenIdle = true;
    public Vector3 debugLastImpulse;
    public Vector3 debugLastPushDirection;
    public Vector3 debugLastPivotWorld;
    public bool debugIsActivated;
    public bool debugIsKnockedDown;
    public bool debugIsPivotTipping;
    public bool debugRealPhysicsAuthorityActive;
    public bool debugRigidbodyUseGravity;
    public bool debugRigidbodyIsKinematic;
    public RigidbodyConstraints debugRigidbodyConstraints;
    public float debugAccumulatedKnockdown;
    public float debugSustainedPushTime;

    private Rigidbody rb;
    private Renderer[] cachedRenderers;
    private Collider[] cachedColliders;

    private bool activated;
    private bool knockedDown;
    private bool pivotTipping;
    private bool hasKnockedDownOnce;

    private float accumulatedKnockdown;
    private float sustainedPushTime;
    private float stableTimer;
    private float forcedAngularTimer;

    private Vector3 lastPushDirection = Vector3.right;
    private Vector3 pivotWorld;
    private Vector3 pivotLocal;
    private Vector3 pivotAxisWorld;
    private float pivotTipAngle;
    private float pivotStartRootY;

    private int groundMask;
    private float lastEmergencySinkRescueTime = -999f;
    private bool hasLastKnownGroundY;
    private float lastKnownGroundY;

    private Vector3 RigidbodyLinearVelocity
    {
        get
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }
        set
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }
    }

    private void SetRigidbodyDamping(float linear, float angular)
    {
        if (rb == null)
            return;

#if UNITY_6000_0_OR_NEWER
        rb.linearDamping = Mathf.Max(0f, linear);
        rb.angularDamping = Mathf.Max(0f, angular);
#else
        rb.drag = Mathf.Max(0f, linear);
        rb.angularDrag = Mathf.Max(0f, angular);
#endif
    }

    private void SetRigidbodyAngularDamping(float angular)
    {
        if (rb == null)
            return;

#if UNITY_6000_0_OR_NEWER
        rb.angularDamping = Mathf.Max(0f, angular);
#else
        rb.angularDrag = Mathf.Max(0f, angular);
#endif
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        cachedColliders = GetComponentsInChildren<Collider>(true);
        RebuildGroundMask();

        ApplyRigidbodyDefaults();
        ApplyNoStepGuard();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
            return;

        if (rb == null)
            rb = GetComponent<Rigidbody>();
        RebuildGroundMask();
        ApplyNoStepGuard();
    }

    private void FixedUpdate()
    {
        debugIsActivated = activated;
        debugIsKnockedDown = knockedDown;
        debugIsPivotTipping = pivotTipping;
        debugRealPhysicsAuthorityActive = IsRealPhysicsAuthorityActive();
        if (rb != null)
        {
            debugRigidbodyUseGravity = rb.useGravity;
            debugRigidbodyIsKinematic = rb.isKinematic;
            debugRigidbodyConstraints = rb.constraints;
        }
        debugAccumulatedKnockdown = accumulatedKnockdown;
        debugSustainedPushTime = sustainedPushTime;

        if (pivotTipping)
        {
            TickPivotTip();
            return;
        }

        if (forcedAngularTimer > 0f)
        {
            forcedAngularTimer -= Time.fixedDeltaTime;
            if (forceAngularVelocityOnKnockdown && knockedDown)
            {
                Vector3 av = rb.angularVelocity;
                if (pivotAxisWorld.sqrMagnitude > 0.0001f)
                    av += pivotAxisWorld.normalized * (forcedKnockdownAngularVelocity * Time.fixedDeltaTime);
                rb.angularVelocity = Vector3.ClampMagnitude(av, Mathf.Abs(forcedKnockdownAngularVelocity));
            }
        }

        if (accumulatedKnockdown > 0f)
            accumulatedKnockdown = Mathf.Max(0f, accumulatedKnockdown - accumulatedKnockdownDecayPerSecond * Time.fixedDeltaTime);

        if (!IsRealPhysicsAuthorityActive() || !realPhysicsSkipPlanarSpeedLimit)
            LimitPlanarSpeed();

        if (protectAfterPivotRelease && (activated || knockedDown) && (!IsRealPhysicsAuthorityActive() || !realPhysicsSkipGroundProtection))
            ApplyGroundProtectionDuringPivot(forceFullCorrection: false);

        // TerrainCollider / Rigidbody 负责正常地面承托。
        // Emergency Sink Rescue 已从自动循环中下线，避免脚本向上修正参与真实物理接触导致跳动。

        TryReturnToKinematicWhenStable();
    }


    public bool CanReceiveUnitProbePush()
    {
        if (!receiveVolumeCollision)
            return false;

        if (pureRealPhysicsFromFirstPush)
            return true;

        if (pivotTipping)
            return false;

        if (knockedDown && paperDollPushMode)
            return paperDollAllowPushAfterKnockdown;

        if (knockedDown && realPhysicsAuthorityAfterKnockdown)
            return !scriptedPushOnlyActivatesRealPhysics;

        return true;
    }

    public void ApplyPush(Vector3 pushDirection, float impulse, Vector3 sourceWorldPosition)
    {
        Vector3 dir = FlattenDirection(pushDirection);
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = FlattenDirection(transform.position - sourceWorldPosition);
            if (dir.sqrMagnitude < 0.0001f)
                dir = lastPushDirection.sqrMagnitude > 0.0001f ? lastPushDirection : Vector3.right;
        }
        dir.Normalize();
        lastPushDirection = dir;
        debugLastPushDirection = dir;

        float finalImpulse = Mathf.Max(minimumImpulse, impulse) * externalPushMultiplier;
        debugLastImpulse = dir * finalImpulse;

        if (pureRealPhysicsFromFirstPush)
        {
            ActivatePureRealPhysicsBody();
            if (!scriptedPushOnlyActivatesRealPhysics && rb != null && !rb.isKinematic)
                rb.AddForce(dir * finalImpulse, ForceMode.Impulse);
            return;
        }

        if (paperDollPushMode)
        {
            // Paper Doll mode has two phases:
            // 1. Standing prop: probe triggers a controlled pivot-tip performance.
            // 2. Fallen prop: probe may release it as a light sliding body so it can still be pushed around.
            if (knockedDown && !pivotTipping)
            {
                if (paperDollAllowPushAfterKnockdown)
                    ApplyPaperDollPostKnockdownPush(dir, finalImpulse);
                return;
            }

            if (CanStartPaperDollKnockdown())
            {
                if (paperDollTriggerKnockdownImmediately || UpdateKnockdownState(finalImpulse))
                {
                    BeginKnockdown(dir, sourceWorldPosition, finalImpulse);
                    return;
                }
            }

            if (paperDollSuppressPhysicsForce)
                return;
        }

        EnsureActivated();

        bool shouldKnockdown = UpdateKnockdownState(finalImpulse);
        if (shouldKnockdown)
        {
            BeginKnockdown(dir, sourceWorldPosition, finalImpulse);
            return;
        }

        if (!pivotTipping && !rb.isKinematic)
        {
            Vector3 forcePoint = GetForcePoint(dir, sourceWorldPosition);
            if (applyForceAtTop)
            {
                rb.AddForceAtPosition(dir * finalImpulse * topForceMultiplier, forcePoint, ForceMode.Impulse);
            }
            else
            {
                rb.AddForce(dir * finalImpulse, ForceMode.Impulse);
            }
        }
    }

    // Backward-compatible aliases in case an older probe calls Push.

    // Compatibility overload: older probe scripts used to pass `this` as the source.
    // Keep this so mixed script versions still compile, then resolve the source to a world position.
    public void ApplyPush(Vector3 pushDirection, float impulse, Component sourceComponent)
    {
        Vector3 sourcePosition;
        if (sourceComponent != null)
            sourcePosition = sourceComponent.transform.position;
        else
            sourcePosition = transform.position - SafePlanarDirection(pushDirection) * 0.5f;

        ApplyPush(pushDirection, impulse, sourcePosition);
    }

    public void Push(Vector3 pushDirection, float impulse)
    {
        ApplyPush(pushDirection, impulse, transform.position - pushDirection.normalized);
    }

    public void Push(Vector3 pushDirection, float impulse, Component sourceComponent)
    {
        ApplyPush(pushDirection, impulse, sourceComponent);
    }

    public void Push(Vector3 pushDirection, float impulse, Vector3 sourceWorldPosition)
    {
        ApplyPush(pushDirection, impulse, sourceWorldPosition);
    }

    private void ActivatePureRealPhysicsBody()
    {
        if (rb == null)
            return;

        activated = true;
        knockedDown = true;
        hasKnockedDownOnce = true;
        pivotTipping = false;
        stableTimer = 0f;
        accumulatedKnockdown = 0f;
        sustainedPushTime = 0f;

        if (syncTransformsBeforeRelease)
            Physics.SyncTransforms();

        rb.isKinematic = false;
        ConfigureRealPhysicsAuthorityBody();
        rb.WakeUp();
    }

    private void ApplyPaperDollPostKnockdownPush(Vector3 dir, float finalImpulse)
    {
        if (rb == null)
            return;

        activated = true;
        stableTimer = 0f;

        if (rb.isKinematic)
        {
            if (syncTransformsBeforeRelease)
                Physics.SyncTransforms();

            rb.isKinematic = false;
        }

        rb.detectCollisions = true;
        rb.WakeUp();

        if (realPhysicsAuthorityAfterKnockdown)
        {
            ConfigureRealPhysicsAuthorityBody();
        }
        else
        {
            rb.useGravity = paperDollUseGravityWhenPushedAfterKnockdown;
            rb.mass = Mathf.Max(0.001f, mass);

            // Fallen props should behave like real light physics again.
            // Use lower damping than the standing anti-jitter values, otherwise it feels nailed to the ground.
            SetRigidbodyDamping(paperDollPostKnockdownLinearDamping, paperDollPostKnockdownAngularDamping);

            Vector3 v = RigidbodyLinearVelocity;
            if (zeroUpwardVelocityOnRelease && v.y > 0f)
                v.y = 0f;
            RigidbodyLinearVelocity = v;
        }

        float push = Mathf.Max(paperDollPostKnockdownMinimumImpulse, Mathf.Max(minimumImpulse, finalImpulse) * Mathf.Max(0f, paperDollPostKnockdownPushMultiplier));

        if (paperDollPostKnockdownUseRealPhysics)
        {
            bool useOffCenter = realPhysicsAuthorityAfterKnockdown
                ? realPhysicsUseOffCenterImpulse
                : paperDollPostKnockdownForceAtEdge;

            if (useOffCenter)
            {
                Vector3 forcePoint = GetFallenPushForcePoint(dir);
                rb.AddForceAtPosition(dir.normalized * push, forcePoint, ForceMode.Impulse);
            }
            else
            {
                rb.AddForce(dir.normalized * push, ForceMode.Impulse);
            }

            bool useExtraTorque = realPhysicsAuthorityAfterKnockdown
                ? realPhysicsUseExtraScriptTorque
                : paperDollPostKnockdownTorqueMultiplier > 0f;

            float torqueMultiplier = realPhysicsAuthorityAfterKnockdown
                ? realPhysicsExtraTorqueMultiplier
                : paperDollPostKnockdownTorqueMultiplier;

            Vector3 rollAxis = Vector3.Cross(Vector3.up, dir.normalized);
            if (useExtraTorque && rollAxis.sqrMagnitude > 0.0001f && torqueMultiplier > 0f)
                rb.AddTorque(rollAxis.normalized * push * torqueMultiplier, ForceMode.Impulse);

            if (!realPhysicsAuthorityAfterKnockdown || !realPhysicsDisableMinimumPlanarVelocity)
                ApplyMinimumPlanarVelocity(dir.normalized, paperDollPostKnockdownMinimumPlanarSpeed);
        }
        else
        {
            // Legacy soft slide mode. Kept as an emergency switch, but real physics is the default now.
            RigidbodyLinearVelocity = RigidbodyLinearVelocity + dir.normalized * push;
        }

        rb.WakeUp();
        if (paperDollApplyGroundProtectionAfterPostPush)
            ApplyGroundProtectionDuringPivot(forceFullCorrection: false);
        LimitPlanarSpeed();
    }

    private Vector3 GetFallenPushForcePoint(Vector3 dir)
    {
        Bounds b = GetReferenceBounds();
        Vector3 planarDir = FlattenDirection(dir);
        if (planarDir.sqrMagnitude < 0.0001f)
            planarDir = lastPushDirection.sqrMagnitude > 0.0001f ? lastPushDirection : Vector3.right;
        planarDir.Normalize();

        // Push on the side facing the source and slightly above the visual bottom.
        // This creates a believable roll without making the prop fight the player.
        float extent = ProjectedExtent(b.extents, planarDir);
        Vector3 p = b.center - planarDir * extent * 0.75f;
        p.y = Mathf.Lerp(b.min.y, b.center.y, 0.35f);
        return p;
    }

    private bool IsRealPhysicsAuthorityActive()
    {
        return realPhysicsAuthorityAfterKnockdown && knockedDown && rb != null && !pivotTipping && !rb.isKinematic;
    }

    private void ConfigureRealPhysicsAuthorityBody()
    {
        if (rb == null)
            return;

        rb.mass = Mathf.Max(0.001f, mass);
        rb.useGravity = realPhysicsUseGravity;
        rb.detectCollisions = true;
        rb.constraints = RigidbodyConstraints.None;
        rb.collisionDetectionMode = realPhysicsCollisionDetectionMode;
        rb.solverIterations = Mathf.Max(1, realPhysicsSolverIterations);
        rb.solverVelocityIterations = Mathf.Max(1, realPhysicsSolverVelocityIterations);
        rb.maxAngularVelocity = Mathf.Max(0.1f, realPhysicsMaxAngularVelocity);
        rb.maxDepenetrationVelocity = Mathf.Max(0.01f, realPhysicsMaxDepenetrationVelocity);
        SetRigidbodyDamping(realPhysicsLinearDamping, realPhysicsAngularDamping);
        ApplyNoStepGuard();
        rb.WakeUp();
    }

    private void ApplyRigidbodyDefaults()
    {
        if (rb == null)
            return;

        rb.mass = Mathf.Max(0.001f, mass);
        SetRigidbodyDamping(linearDamping, angularDamping);

        if (stayKinematicUntilPushed)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        else
        {
            rb.isKinematic = false;
            rb.useGravity = useGravityAfterActivated;
            activated = true;
        }
    }

    private void ApplyNoStepGuard()
    {
        int pushable = LayerMask.NameToLayer(pushableLayerName);
        int unitBody = LayerMask.NameToLayer(unitBodyLayerName);
        int character = LayerMask.NameToLayer(characterLayerName);

        bool ignore = ignoreUnitBodyAndCharacterCollision;
        if (realPhysicsAuthorityAfterKnockdown && realPhysicsAllowUnitBodyCollision)
            ignore = false;

        if (pushable >= 0 && unitBody >= 0)
            Physics.IgnoreLayerCollision(pushable, unitBody, ignore);
        if (pushable >= 0 && character >= 0)
            Physics.IgnoreLayerCollision(pushable, character, ignore);

        int ground = LayerMask.NameToLayer(groundLayerName);
        if (realPhysicsAuthorityAfterKnockdown && realPhysicsEnsureGroundCollision && pushable >= 0 && ground >= 0)
            Physics.IgnoreLayerCollision(pushable, ground, false);

        int world3D = LayerMask.NameToLayer("World3D");
        if (realPhysicsAuthorityAfterKnockdown && realPhysicsEnsureGroundCollision && pushable >= 0 && world3D >= 0)
            Physics.IgnoreLayerCollision(pushable, world3D, false);

        int legacyGround = LayerMask.NameToLayer("GroundPhysics");
        if (realPhysicsAuthorityAfterKnockdown && realPhysicsEnsureGroundCollision && pushable >= 0 && legacyGround >= 0)
            Physics.IgnoreLayerCollision(pushable, legacyGround, false);

        debugPushableGroundLayerIgnored = pushable >= 0 && ground >= 0 && Physics.GetIgnoreLayerCollision(pushable, ground);
    }

    private void EnsureActivated()
    {
        if (activated && !rb.isKinematic)
            return;

        activated = true;
        rb.isKinematic = false;
        rb.useGravity = useGravityAfterActivated;
        rb.WakeUp();
    }

    private bool CanStartPaperDollKnockdown()
    {
        if (!enableKnockdown || !enablePivotTipKnockdown)
            return false;
        if (knockedDown || pivotTipping)
            return false;
        if (oneKnockdownPerActivation && hasKnockedDownOnce)
            return false;
        return knockdownMotionMode == KnockdownMotionMode.PivotTipKinematic;
    }

    private bool UpdateKnockdownState(float finalImpulse)
    {
        if (!enableKnockdown || knockedDown || pivotTipping)
            return false;
        if (oneKnockdownPerActivation && hasKnockedDownOnce)
            return false;

        accumulatedKnockdown += finalImpulse;
        sustainedPushTime += Time.fixedDeltaTime;

        return finalImpulse >= knockdownImpulseThreshold
            || accumulatedKnockdown >= accumulatedKnockdownThreshold
            || sustainedPushTime >= sustainedPushTimeThreshold;
    }

    private void BeginKnockdown(Vector3 dir, Vector3 sourceWorldPosition, float finalImpulse)
    {
        knockedDown = true;
        hasKnockedDownOnce = true;
        sustainedPushTime = 0f;
        accumulatedKnockdown = 0f;

        if (knockdownMotionMode == KnockdownMotionMode.PivotTipKinematic && enablePivotTipKnockdown)
        {
            BeginPivotTip(dir);
            return;
        }

        rb.isKinematic = false;
        if (realPhysicsAuthorityAfterKnockdown)
        {
            ConfigureRealPhysicsAuthorityBody();
        }
        else
        {
            rb.useGravity = useGravityAfterKnockdown;
            if (lowerAngularDampingDuringKnockdown)
                SetRigidbodyAngularDamping(knockdownAngularDamping);
        }

        pivotAxisWorld = Vector3.Cross(Vector3.up, dir).normalized;
        if (invertPivotRotationDirection)
            pivotAxisWorld = -pivotAxisWorld;

        Vector3 forcePoint = GetForcePoint(dir, sourceWorldPosition);
        rb.AddForceAtPosition(dir * finalImpulse * topForceMultiplier, forcePoint, ForceMode.Impulse);
        if (knockdownTorqueImpulse > 0f && pivotAxisWorld.sqrMagnitude > 0.0001f)
            rb.AddTorque(pivotAxisWorld * knockdownTorqueImpulse, ForceMode.Impulse);
        if (knockdownUpwardImpulse > 0f)
            rb.AddForce(Vector3.up * knockdownUpwardImpulse, ForceMode.Impulse);

        forcedAngularTimer = forcedKnockdownDriveDuration;
    }

    private void BeginPivotTip(Vector3 dir)
    {
        pivotTipping = true;
        pivotTipAngle = 0f;
        pivotStartRootY = transform.position.y;

        RigidbodyLinearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = false;
        if (keepKinematicDuringPivotTip)
            rb.isKinematic = true;

        CalculatePivot(dir, out pivotWorld, out pivotAxisWorld);
        if (invertPivotRotationDirection)
            pivotAxisWorld = -pivotAxisWorld;

        pivotLocal = transform.InverseTransformPoint(pivotWorld);
        debugLastPivotWorld = pivotWorld;
    }

    private void TickPivotTip()
    {
        float step = Mathf.Abs(pivotTipDegreesPerSecond) * Time.fixedDeltaTime;
        if (step <= 0f)
            step = 1f;

        step = Mathf.Min(step, Mathf.Max(0f, pivotTipMaxAngle - pivotTipAngle));
        if (step <= 0f)
        {
            FinishPivotTip();
            return;
        }

        // Rotate around a fixed local pivot and re-anchor it to the original world pivot.
        Quaternion delta = Quaternion.AngleAxis(step, pivotAxisWorld.normalized);
        transform.rotation = delta * transform.rotation;
        Vector3 pivotAfter = transform.TransformPoint(pivotLocal);
        transform.position += pivotWorld - pivotAfter;

        // Protect both sides of the ground contact: do not sink and do not hover too much.
        ApplyGroundProtectionDuringPivot();

        pivotTipAngle += step;
        if (pivotTipAngle >= pivotTipMaxAngle - 0.001f)
            FinishPivotTip();
    }

    private void FinishPivotTip()
    {
        ApplyGroundProtectionDuringPivot(forceFullCorrection: true);
        pivotTipping = false;

        bool releaseAsRealPhysics = realPhysicsAuthorityAfterKnockdown && realPhysicsReleaseAfterPivotTip;

        if (!releaseAsRealPhysics && (!releaseRigidbodyAfterPivotTip || (paperDollPushMode && paperDollKeepKinematicAfterTip)))
        {
            RigidbodyLinearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
            return;
        }

        if (syncTransformsBeforeRelease)
            Physics.SyncTransforms();

        rb.isKinematic = false;
        if (releaseAsRealPhysics)
            ConfigureRealPhysicsAuthorityBody();
        else
            rb.useGravity = useGravityAfterPivotRelease;

        Vector3 lv = RigidbodyLinearVelocity;
        if (!releaseAsRealPhysics && zeroUpwardVelocityOnRelease && lv.y > 0f)
            lv.y = 0f;
        if (pivotReleaseForwardImpulse != 0f)
            lv += lastPushDirection.normalized * pivotReleaseForwardImpulse;
        if (pivotReleaseDownImpulse > 0f)
            lv += Vector3.down * pivotReleaseDownImpulse;
        RigidbodyLinearVelocity = lv;
        rb.WakeUp();
    }

    private void CalculatePivot(Vector3 dir, out Vector3 pivot, out Vector3 axis)
    {
        Bounds b = GetReferenceBounds();
        Vector3 planarDir = FlattenDirection(dir).normalized;
        if (planarDir.sqrMagnitude < 0.0001f)
            planarDir = Vector3.right;

        float extent = ProjectedExtent(b.extents, planarDir) * pivotForwardEdgeRatio;
        pivot = b.center - planarDir * extent;
        pivot.y = b.min.y + pivotGroundSkin;

        if (raycastPivotToGround && TryFindGroundYAt(pivot, b, out float groundY))
            pivot.y = groundY + pivotGroundSkin;

        axis = Vector3.Cross(Vector3.up, planarDir).normalized;
        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.forward;
    }

    private Vector3 GetForcePoint(Vector3 dir, Vector3 sourceWorldPosition)
    {
        Bounds b = GetReferenceBounds();
        if (applyForceAtTop && useColliderTopPoint)
        {
            Vector3 p = b.center - dir.normalized * ProjectedExtent(b.extents, dir.normalized) * 0.5f;
            p.y = Mathf.Lerp(b.center.y, b.max.y, Mathf.Clamp01(colliderTopRatio));
            return p;
        }

        return rb.worldCenterOfMass + Vector3.up * topForceHeight;
    }

    private Bounds GetReferenceBounds()
    {
        if (useRendererBoundsForPivot && TryGetRendererBounds(out Bounds rbounds))
            return rbounds;
        if (TryGetColliderBounds(out Bounds cbounds))
            return cbounds;
        return new Bounds(transform.position, Vector3.one);
    }

    private bool TryGetRendererBounds(out Bounds bounds)
    {
        bool has = false;
        bounds = new Bounds(transform.position, Vector3.zero);
        if (cachedRenderers == null || cachedRenderers.Length == 0)
            cachedRenderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer r in cachedRenderers)
        {
            if (r == null || !r.enabled)
                continue;
            if (!has)
            {
                bounds = r.bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }
        return has;
    }

    private bool TryGetColliderBounds(out Bounds bounds)
    {
        bool has = false;
        bounds = new Bounds(transform.position, Vector3.zero);
        if (cachedColliders == null || cachedColliders.Length == 0)
            cachedColliders = GetComponentsInChildren<Collider>(true);

        foreach (Collider c in cachedColliders)
        {
            if (c == null || !c.enabled || c.isTrigger)
                continue;
            if (!has)
            {
                bounds = c.bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(c.bounds);
            }
        }
        return has;
    }

    private void RebuildGroundMask()
    {
        int mask = 0;

        int named = LayerMask.NameToLayer(groundLayerName);
        if (named >= 0)
            mask |= 1 << named;

        int world3D = LayerMask.NameToLayer("World3D");
        if (world3D >= 0)
            mask |= 1 << world3D;

        int legacyGround = LayerMask.NameToLayer("GroundPhysics");
        if (legacyGround >= 0)
            mask |= 1 << legacyGround;

        groundMask = mask;
    }

    private bool IsGroundCollider(Collider col)
    {
        if (col == null)
            return false;
        if (col is TerrainCollider)
            return true;
        return (groundMask & (1 << col.gameObject.layer)) != 0;
    }

    private void ApplyEmergencySinkRescueIfNeeded()
    {
        if (!realPhysicsEmergencySinkRescue || !IsRealPhysicsAuthorityActive() || rb == null || rb.isKinematic)
            return;
        if (Time.time - lastEmergencySinkRescueTime < Mathf.Max(0.02f, realPhysicsEmergencyCooldown))
            return;
        if (!TryGetColliderBounds(out Bounds b))
            return;
        if (!TryFindGroundYForBounds(b, out float groundY))
            return;

        float severeLimit = groundY - Mathf.Max(0.01f, realPhysicsEmergencySinkDepth);
        if (b.min.y >= severeLimit)
            return;

        float targetBottom = groundY + Mathf.Max(0f, realPhysicsEmergencyGroundSkin);
        float lift = Mathf.Clamp(targetBottom - b.min.y, 0f, Mathf.Max(0.01f, realPhysicsEmergencyMaxLift));
        if (lift <= 0.0001f)
            return;

        rb.position += Vector3.up * lift;
        Vector3 v = RigidbodyLinearVelocity;
        if (v.y < 0f)
            RigidbodyLinearVelocity = new Vector3(v.x, 0f, v.z);
        Physics.SyncTransforms();
        rb.WakeUp();
        lastEmergencySinkRescueTime = Time.time;
    }

    private void ApplyGroundProtectionDuringPivot(bool forceFullCorrection = false)
    {
        if (!protectAgainstGroundPenetration && !preventLargeHoverDuringPivot)
            return;
        if (groundMask == 0 && !useFallbackGroundPlaneWhenRayMisses && !hasLastKnownGroundY)
            return;

        Bounds b = GetReferenceBounds();
        if (!TryFindGroundYForBounds(b, out float groundY))
            return;

        float targetBottom = groundY + pivotGroundSkin;
        float visualBottom = b.min.y;
        float delta = 0f;

        if (protectAgainstGroundPenetration && visualBottom < targetBottom)
        {
            // Raise only enough to stop underground penetration.
            delta = targetBottom - visualBottom;
        }
        else if (preventLargeHoverDuringPivot && visualBottom > targetBottom + maxAllowedVisualHover)
        {
            // Allow small hover tolerance, then gently snap down. Never over-snap in one step unless force requested.
            float hover = visualBottom - (targetBottom + maxAllowedVisualHover);
            delta = -Mathf.Min(hover, forceFullCorrection ? hover : maxSnapDownPerStep);
        }

        if (Mathf.Abs(delta) > 0.0001f)
        {
            transform.position += Vector3.up * delta;

            if (zeroDownwardVelocityWhenSnappedUp && delta > 0f && rb != null && !rb.isKinematic)
            {
                Vector3 v = RigidbodyLinearVelocity;
                if (v.y < 0f)
                    RigidbodyLinearVelocity = new Vector3(v.x, 0f, v.z);
            }
        }
    }

    private bool TryFindGroundYAt(Vector3 nearPoint, Bounds b, out float groundY)
    {
        float topY = Mathf.Max(b.max.y, nearPoint.y) + groundRayExtraTopHeight;
        Vector3 origin = new Vector3(nearPoint.x, topY, nearPoint.z);
        float dist = Mathf.Max(0.1f, topY - b.min.y + groundRayDistance);

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, dist, groundMask, QueryTriggerInteraction.Ignore))
        {
            CacheGroundY(hit.point.y);
            groundY = hit.point.y;
            return true;
        }

        return TryFindGroundYForBounds(b, out groundY);
    }

    private bool TryFindGroundYForBounds(Bounds b, out float groundY)
    {
        groundY = float.NegativeInfinity;
        bool hitAny = false;

        Vector3 c = b.center;
        Vector3 e = b.extents;
        Vector3[] samples =
        {
            new Vector3(c.x, 0f, c.z),
            new Vector3(c.x - e.x, 0f, c.z - e.z),
            new Vector3(c.x - e.x, 0f, c.z + e.z),
            new Vector3(c.x + e.x, 0f, c.z - e.z),
            new Vector3(c.x + e.x, 0f, c.z + e.z),
            new Vector3(c.x - e.x, 0f, c.z),
            new Vector3(c.x + e.x, 0f, c.z),
            new Vector3(c.x, 0f, c.z - e.z),
            new Vector3(c.x, 0f, c.z + e.z),
        };

        float originY = b.max.y + groundRayExtraTopHeight;
        float dist = Mathf.Max(0.1f, originY - b.min.y + groundRayDistance);

        foreach (Vector3 p in samples)
        {
            Vector3 origin = new Vector3(p.x, originY, p.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, dist, groundMask, QueryTriggerInteraction.Ignore))
            {
                if (!hitAny || hit.point.y > groundY)
                    groundY = hit.point.y;
                hitAny = true;
            }
        }

        if (hitAny)
        {
            CacheGroundY(groundY);
            return true;
        }

        if (useLastKnownGroundWhenRayMisses && hasLastKnownGroundY)
        {
            groundY = lastKnownGroundY;
            return true;
        }

        if (useFallbackGroundPlaneWhenRayMisses)
        {
            groundY = fallbackGroundY;
            return true;
        }

        return false;
    }

    private void CacheGroundY(float y)
    {
        lastKnownGroundY = y;
        hasLastKnownGroundY = true;
    }

    private float ProjectedExtent(Vector3 extents, Vector3 dir)
    {
        dir = new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z));
        return extents.x * dir.x + extents.y * dir.y + extents.z * dir.z;
    }

    private Vector3 FlattenDirection(Vector3 v)
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
            case FlattenAxis.Z: v.z = 0f; break;
        }
        return v;
    }

    private Vector3 SafePlanarDirection(Vector3 v)
    {
        v = FlattenDirection(v);
        if (v.sqrMagnitude < 0.0001f)
            return Vector3.right;
        return v.normalized;
    }

    private void LimitPlanarSpeed()
    {
        if (IsRealPhysicsAuthorityActive() && realPhysicsSkipPlanarSpeedLimit)
            return;

        if (rb == null || rb.isKinematic || maxPlanarSpeed <= 0f)
            return;

        Vector3 v = RigidbodyLinearVelocity;
        Vector3 planar = FlattenDirection(v);
        if (planar.magnitude > maxPlanarSpeed)
        {
            Vector3 clamped = planar.normalized * maxPlanarSpeed;
            RigidbodyLinearVelocity = v + (clamped - planar);
        }
    }

    private void ApplyMinimumPlanarVelocity(Vector3 dir, float minSpeed)
    {
        if (rb == null || rb.isKinematic || minSpeed <= 0f)
            return;

        dir = FlattenDirection(dir);
        if (dir.sqrMagnitude < 0.0001f)
            return;
        dir.Normalize();

        Vector3 v = RigidbodyLinearVelocity;
        Vector3 planar = FlattenDirection(v);
        float along = Vector3.Dot(planar, dir);
        if (along < minSpeed)
            RigidbodyLinearVelocity = v + dir * (minSpeed - along);
    }

    private void TryReturnToKinematicWhenStable()
    {
        if (!returnToKinematicWhenStable || !activated || rb == null || rb.isKinematic || pivotTipping)
            return;
        if (IsRealPhysicsAuthorityActive() && realPhysicsKeepDynamicWhenStable)
            return;

        bool stable = RigidbodyLinearVelocity.magnitude <= stableSpeed && rb.angularVelocity.magnitude <= stableAngularSpeed;
        if (stable)
            stableTimer += Time.fixedDeltaTime;
        else
            stableTimer = 0f;

        if (stableTimer >= stableTimeBeforeKinematic)
        {
            ApplyGroundProtectionDuringPivot(forceFullCorrection: true);
            RigidbodyLinearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.Sleep();
            rb.isKinematic = true;
            rb.useGravity = false;
            activated = false;
            stableTimer = 0f;
            SetRigidbodyAngularDamping(angularDamping);
        }
    }


    private void OnCollisionStay(Collision collision)
    {
        if (collision == null || collision.collider == null)
            return;
        if (!IsGroundCollider(collision.collider))
            return;

        debugGroundContactCount = collision.contactCount;
        if (collision.contactCount > 0)
        {
            ContactPoint c = collision.GetContact(0);
            debugLastGroundContactPoint = c.point;
            debugLastGroundContactNormal = c.normal;
            if (debugLogGroundCollisionContacts)
                Debug.Log($"[SkyPrisonPushablePropRuntime] Ground contact {name}: point={c.point}, normal={c.normal}, contacts={collision.contactCount}", this);
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision == null || collision.collider == null)
            return;
        if (!IsGroundCollider(collision.collider))
            return;

        debugGroundContactCount = 0;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
            return;

        Gizmos.color = Color.yellow;
        Vector3 p = debugLastPivotWorld;
        if (!Application.isPlaying && drawLivePreviewPivotWhenIdle)
        {
            Vector3 dir = transform.right;
            CalculatePreviewPivot(dir, out p);
        }
        else if (Application.isPlaying && drawLivePreviewPivotWhenIdle && !pivotTipping)
        {
            CalculatePivot(lastPushDirection.sqrMagnitude > 0.0001f ? lastPushDirection : transform.right, out p, out _);
        }

        if (p != Vector3.zero)
        {
            Gizmos.DrawSphere(p, 0.06f);
            Gizmos.DrawLine(p, p + Vector3.up * 0.4f);
        }

        if (Application.isPlaying)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position + lastPushDirection.normalized * 0.8f);
        }
    }

    private void CalculatePreviewPivot(Vector3 dir, out Vector3 p)
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (cachedRenderers == null)
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
        if (cachedColliders == null)
            cachedColliders = GetComponentsInChildren<Collider>(true);
        RebuildGroundMask();

        CalculatePivot(FlattenDirection(dir).sqrMagnitude > 0.0001f ? FlattenDirection(dir) : Vector3.right, out p, out _);
    }
}
