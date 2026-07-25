using UnityEngine;

[DisallowMultipleComponent]
public class TerrainGroundMotorV5 : MonoBehaviour
{
    public enum MotorState
    {
        Grounded,
        Sliding,
        Falling
    }

    [Header("Input")]
    public bool readUnityInput = true;
    public bool enableSprintKey = true;

    [Header("Movement")]
    public float moveSpeed = 5.5f;
    public float sprintSpeed = 8f;
    public float acceleration = 200f;
    public float deceleration = 200f;
    public bool projectGroundMovementOnSlope = true;

    [Header("Slope Blocking")]
    [Tooltip("防止角色把水平输入直接送上过陡的 Terrain 斜面。可站立判断仍由 Max Walkable Slope 决定。")]
    public bool blockUphillOnTooSteepSlope = true;
    [Tooltip("超过这个坡度时，视为接近墙面。角色不能继续向坡上爬，只允许横向或向下滑。")]
    public float hardBlockSlope = 65f;
    [Tooltip("下一步 Terrain 高度比当前脚下高度高出多少时，才认为是在向陡坡上爬。")]
    public float steepUphillBlockHeight = 0.06f;
    [Tooltip("被陡坡阻挡时是否清掉水平速度，避免角色贴墙抖动。")]
    public bool clearVelocityWhenBlockedBySlope = true;

    [Header("Body Collider Binding")]
    [Tooltip("角色移动身体 Collider。留空时会优先找本节点 Collider，其次找 CollisionRoot 子节点，再找单位子树内第一个非 Trigger Collider。脚本本身不再强制要求挂在 Collider 同节点。")]
    public Collider bodyColliderOverride;
    [Tooltip("优先查找的碰撞节点名称。你的单位结构里通常是 CollisionRoot。")]
    public string collisionRootName = "CollisionRoot";
    [Tooltip("本节点没有 Collider 时，自动从子节点查找角色身体 Collider。")]
    public bool autoFindBodyColliderInChildren = true;
    [Tooltip("子节点 Collider 查找是否包含未激活节点。编辑器搭结构时建议开启。")]
    public bool includeInactiveBodyCollider = true;
    [Tooltip("优先使用 CollisionRoot 下的 Collider，避免误拿 VisualRoot / Trigger / 代理体 Collider。")]
    public bool preferCollisionRootCollider = true;
    [Tooltip("自动查找身体 Collider 时跳过 Trigger。")]
    public bool ignoreTriggerBodyColliders = true;
    [Tooltip("找不到身体 Collider 时输出警告。找不到时脚底可继续用 FootProbe，但水平防钩不会生效。")]
    public bool warnWhenBodyColliderMissing = true;

    [Header("Ground Probe")]
    public LayerMask groundMask = ~0;
    public Transform footProbe;
    public float probeHeight = 2.0f;
    public float probeDistance = 10f;
    public float footHeightOffset = 0.02f;
    public float groundedSnapDistance = 0.35f;
    public float stepDownSnapDistance = 0.75f;
    public float landingTolerance = 0.08f;
    public float landingExtraDistance = 0.75f;
    public float belowGroundRecoverDistance = 2.0f;
    public float maxWalkableSlope = 45f;
    public bool preferTerrainSampleHeight = true;

    [Header("Gravity")]
    public float gravity = 28f;
    public float maxFallSpeed = 32f;

    [Header("Jump")]
    public bool enableJump = true;
    public float jumpHeight = 1.7f;
    [Range(0f, 1f)] public float airControlMultiplier = 0.75f;
    public bool allowJumpFromSliding = true;
    [Tooltip("起跳后短时间内不因为脚下仍接近地面而立刻重新贴地，但仍会阻止穿入 Terrain。")]
    public float jumpGroundIgnoreTime = 0.12f;


    [Header("Sliding")]
    public bool enableSliding = true;
    public float steepContactDistance = 0.9f;
    public float slideAcceleration = 20f;
    public float slideDeceleration = 16f;
    public float maxSlideSpeed = 9f;
    public float slideSnapDistance = 1.25f;
    public bool allowInputWhileSliding = true;
    [Range(0f, 1f)] public float inputControlWhileSliding = 0.35f;

    [Header("Obstacle Edge Anti Hook")]
    [Tooltip("启用身体水平投射。角色碰到箱子、墙角、物体边缘时，会沿墙面滑开，而不是把位移硬顶进边角。")]
    public bool enableHorizontalBodyCollision = true;
    [Tooltip("身体水平阻挡层。建议只包含实体物理层，例如 World3D / DecorationBody，不要包含 Trigger / 遮挡代理体。")]
    public LayerMask bodyBlockMask = ~0;
    [Tooltip("可推道具(SkyPrisonPushablePropRuntime，如锥桶/路障)走推开物理，不作为身体硬阻挡；否则胶囊蹭它的凸包边会被勾住，还和推开物理互相较劲。")]
    public bool ignorePushablePropsInBodyBlock = true;
    [Tooltip("身体投射提前停下的安全距离，避免刚好贴到角上后被下一帧反复判定。")]
    public float bodyCastSkin = 0.06f;
    [Tooltip("投射体缩小量。商业观感优先时保持 0，不用牺牲角色身体碰撞正确性来换防钩。只有特殊模型极端卡角时才小幅调到 0.005~0.015。")]
    public float bodyCastShrink = 0.0f;
    [Range(1, 4)]
    [Tooltip("滑墙迭代次数。2 通常足够处理箱子角；过高没有必要。")]
    public int horizontalSlideIterations = 2;
    [Tooltip("身体投射命中法线 Y 大于该值时，视为地面/斜坡，不作为水平墙体处理。")]
    [Range(0f, 1f)] public float maxBodyBlockNormalY = 0.35f;
    
    [Header("Body Clearance / Collision Accuracy")]
    [Tooltip("保持角色和模型之间的商业视觉余量。开启后只放大移动扫掠体，不缩小真实身体碰撞，也不会用于退穿透。用于避免角色视觉上贴墙过近/嵌入箱体边缘。")]
    public bool preserveBodyVisualClearance = true;
    [Tooltip("移动扫掠体额外水平余量。0.02~0.05 通常足够；太大会出现隔空撞墙。")]
    public float bodySweepExtraClearance = 0.025f;

    [Header("Ground / Wall Role Separation")]
    [Tooltip("Body Block Mask 内的箱子/墙/集装箱只作为水平阻挡，不再被脚底 Ground Probe 当成可站立地面。")]
    public bool rejectBodyBlockCollidersAsGround = true;
    [Tooltip("Unit Separation Mask 内的单位 Collider 不能被脚底当成地面。")]
    public bool rejectUnitCollidersAsGround = true;
    [Tooltip("非 Terrain 的物理地面命中，如果比脚底高出过多，则不允许 Snap 上去，避免被箱体边缘/墙面台阶吸上去。")]
    public bool limitPhysicsGroundStepUp = true;
    [Tooltip("允许非 Terrain 物理地面把脚底向上吸附的最大高度。0.08~0.14 比较适合 2.5D 行走。")]
    public float maxPhysicsGroundStepUpHeight = 0.12f;

    [Tooltip("防止物体边缘、盒子侧面、Mesh 边角被脚底 Raycast 当成陡坡地面，从而进入 Sliding 或清空速度。")]
    public bool ignoreSteepPhysicsGroundAsGround = true;
    [Tooltip("Physics Ground 命中超过这个坡度时，不再作为脚底地面。Terrain 高度场不受这个限制。")]
    public float maxPhysicsGroundSlopeAsGround = 58f;


    [Header("Micro Hook Ignore")]
    [Tooltip("用竖向胶囊体扫描代替盒体扫描。不会修改实际 Collider，只让移动判定像圆角身体一样滑过瓦楞、角柱、小斜面。")]
    public bool useRoundedBodySweep = true;
    [Range(0.4f, 1.0f)]
    [Tooltip("圆角扫描半径相对角色 X/Z 半径的比例。商业观感优先时保持 1，保证角色不会明显嵌入墙体；防钩主要交给 Substep / 清速度 / Stuck Rescue。")]
    public float roundedSweepRadiusScale = 1.0f;
    [Tooltip("圆角扫描最小半径，防止角色 Collider 很薄时扫描体退化。")]
    public float minRoundedSweepRadius = 0.12f;
    [Tooltip("忽略 Cast 起点已经重叠造成的 0 距离命中，避免被小凸起锁死。")]
    public bool ignoreInitialOverlappedBodyHits = true;
    [Tooltip("进入两个不同墙面法线组成的内角时，停止继续往角里投影，并轻微向角外推出。")]
    public bool enableCornerDeadlockBreak = true;
    [Range(-0.2f, 0.8f)]
    [Tooltip("连续两次墙面法线点乘低于该值时，视为内角。0.25 约等于夹角大于 75 度。")]
    public float cornerNormalDotThreshold = 0.25f;
    [Tooltip("内角脱困时向角外轻推的距离。只影响 XZ，不改 Y。")]
    public float cornerPushOutDistance = 0.05f;
    [Tooltip("内角锁死时清掉水平速度，避免下一帧继续把角色送回钩子里。")]
    public bool clearVelocityWhenCornerLocked = true;

    [Header("Sprint Corner Anti Hook")]
    [Tooltip("高速/奔跑时把一次水平位移拆成多个小步处理，避免一帧扎进 MeshCollider 角落。")]
    public bool enableSprintCornerAntiHook = true;
    [Tooltip("身体水平解算的最大单步距离。奔跑速度越高越需要小步。0.06~0.10 通常比较稳。商业观感优先时宁可拆小步，也不要缩小身体碰撞。")]
    public float maxBodySweepStepDistance = 0.08f;
    [Tooltip("身体扫掠前后做一次轻量退穿透。V13 已改为只处理真实重叠，不再把 Body Cast Skin 范围内的贴墙状态当成重叠，避免无形力量持续推角色。")]
    public bool depenetrateBodyDuringHorizontalSolve = true;
    [Tooltip("所有 solve 结束后再做一次兜底退穿透，修复跳跃 Y 上升时侧身撞进墙面的情况。不依赖 depenetrateBodyDuringHorizontalSolve 开关。")]
    public bool depenetrateBodyAfterSolve = true;
    [Range(1, 4)]
    [Tooltip("退穿透迭代次数。2 通常足够，过高可能让贴墙手感变硬。")]
    public int bodyDepenetrationIterations = 2;
    [Tooltip("每个小步允许的最大退穿透距离。不要太大，否则会像被墙弹开。")]
    public float maxBodyDepenetrationPerStep = 0.055f;
    [Tooltip("退穿透时额外留出的极小安全边。")]
    public float bodyDepenetrationExtraPush = 0.006f;
    [Tooltip("V13：退穿透重叠检测的极小余量。不要使用 Body Cast Skin 做退穿透半径，否则角色只是贴墙也会被当成重叠并持续推出。")]
    public float bodyDepenetrationOverlapPadding = 0.002f;
    [Tooltip("V13：退穿透只处理真实重叠，不处理 Body Cast Skin 范围内的贴近状态。保持开启，避免贴墙时出现无形推力。")]
    public bool depenetrateOnlyRealOverlap = true;
    [Tooltip("只要身体水平解算确实被实体阻挡，就清掉朝阻挡方向的水平速度，避免奔跑下一帧继续顶进角里。")]
    public bool clearVelocityWhenBodyBlocked = true;
    [Tooltip("V9：圆角扫描模式下，退穿透也使用圆角近似，而不是直接用真实 BoxCollider/MeshCollider 做 ComputePenetration。避免高速冲角时又被角色 Box 角重新咬住。")]
    public bool useRoundedBodyForDepenetration = true;
    [Tooltip("V9：退穿透把角色从角里推出后，本帧后续小步沿当前剩余方向走，不再强行拉回原始目标线，防止反复回钩。")]
    public bool keepDepenetratedPath = true;
    [Tooltip("V9：当 Cast 一开始就贴墙/重叠，仍按阻挡处理，而不是完全忽略 0 距离命中。高速奔跑时建议开启。")]
    public bool treatZeroDistanceBodyHitAsBlock = true;

    [Header("Commercial Stuck Rescue")]
    [Tooltip("商业保底：角色有输入、有期望位移，但实际几乎不动并持续一小段时间时，自动从角落/小钩子里脱困。")]
    public bool enableStuckRescue = true;
    [Tooltip("疑似卡住需要持续的时间。0.12~0.20 比较适合动作游戏。")]
    public float stuckCheckSeconds = 0.16f;
    [Tooltip("输入强度超过该值才参与卡住检测，避免轻推摇杆/键盘抖动触发。")]
    [Range(0f, 1f)] public float stuckMinInputMagnitude = 0.45f;
    [Tooltip("本帧/累计期望移动至少达到该距离，才认为玩家确实想移动。")]
    public float stuckMinExpectedDistance = 0.08f;
    [Tooltip("检测窗口内实际水平移动低于该距离，判定为卡住。")]
    public float stuckMaxActualDistance = 0.015f;
    [Tooltip("脱困时水平推出/回退的距离。不要太大，玩家应感受到被角轻轻弹开，而不是瞬移。")]
    public float stuckRescueDistance = 0.10f;
    [Tooltip("一次脱困后冷却，避免连续弹跳。")]
    public float stuckRescueCooldown = 0.18f;
    [Tooltip("优先沿最近墙面法线推出；没有法线时才回退到 Last Safe Position。")]
    public bool rescuePreferBlockingNormal = true;
    [Tooltip("脱困时清掉水平速度，避免下一帧继续顶回同一个角。")]
    public bool clearVelocityWhenRescued = true;

    [Header("Last Safe Position")]
    [Tooltip("记录最近一次稳定、安全的位置。卡入复杂 MeshCollider 时可回退。")]
    public bool enableLastSafePosition = true;
    [Tooltip("记录安全点的最小间隔。")]
    public float recordSafePositionInterval = 0.05f;
    [Tooltip("安全点必须处于 Grounded 状态。")]
    public bool safePositionMustBeGrounded = true;
    [Tooltip("安全点不能和 BodyBlockMask 内的实体重叠。")]
    public bool safePositionMustNotOverlapBody = true;
    [Tooltip("回退安全点时最多移动的水平距离，防止明显瞬移。")]
    public float lastSafeRewindDistance = 0.12f;

    [Header("Sprint Block Grace")]
    [Tooltip("奔跑撞到角/墙后，短时间按走路速度处理，避免继续满速钻角。")]
    public bool enableSprintBlockGrace = true;
    [Tooltip("撞墙后禁止继续满速 Sprint 解算的时间。")]
    public float sprintBlockGraceSeconds = 0.16f;

    [Header("Unit Soft Separation")]
    [Tooltip("单位之间不再走墙体/箱子硬阻挡逻辑，而是用软分离轻轻推开，避免互相黏住。")]
    public bool enableUnitSoftSeparation = true;
    [Tooltip("单位软分离层。建议放 Character2D / UnitBody。不要放 World3D / DecorationTrigger / 遮挡代理。")]
    public LayerMask unitSeparationMask = 0;
    [Tooltip("如果 Unit Separation Mask 没设置，运行时尝试自动使用名为 Character2D 的 Layer。")]
    public bool autoUseCharacter2DLayerForUnitSeparation = true;
    [Tooltip("即使 Body Block Mask 包含了单位层，也在墙体扫掠/退穿透/卡住检测里跳过单位 Collider。")]
    public bool ignoreUnitCollidersInBodyBlock = true;
    [Tooltip("除了按 Layer 判断外，也把挂有 TerrainGroundMotorV5 的对象视为单位。")]
    public bool identifyUnitByTerrainGroundMotor = true;
    [Tooltip("单位软分离的额外安全距离。越大越不容易贴在一起，但太大会像隔空排斥。")]
    public float unitSeparationPadding = 0.035f;
    [Tooltip("单位软分离强度。只影响分离速度，不会清掉移动速度。")]
    public float unitSeparationStrength = 10f;
    [Tooltip("单位软分离每个 FixedUpdate 最大水平推出距离，避免弹开过猛。")]
    public float unitSeparationMaxPushPerFrame = 0.055f;
    [Tooltip("空中是否也做单位软分离。开启后跳起来也更容易从单位接触中挣脱。")]
    public bool applyUnitSeparationWhileAirborne = true;
    [Tooltip("单位软分离推出后，再用场景硬阻挡重新裁剪一次，防止被另一个单位顶进墙/箱子/集装箱。")]
    public bool resolveBodyBlockAfterUnitSeparation = true;
    [Tooltip("单位软分离如果会把角色推入硬阻挡，是否直接削掉这部分分离位移。正式版建议开启。")]
    public bool clampUnitSeparationAgainstBodyBlock = true;

    private readonly Collider[] bodyOverlapBuffer = new Collider[32];
    // 脚底地面探测每次 FixedUpdate 会调用好几次（当前脚位/坡度预判/落地检测等），
    // 之前用 Physics.RaycastAll 每次都新分配一个数组——单位一多，GC 压力会随时间
    // 越滚越大（这正是"跑一会儿就开始严重掉帧"的元凶之一）。换成 NonAlloc 复用缓冲区。
    private readonly RaycastHit[] groundRaycastBuffer = new RaycastHit[16];
    // 身体水平阻挡扫掠（CapsuleCast/BoxCast）同理，一次 FixedUpdate 里防钩/多步迭代
    // 会调用好几次，之前每次都新分配数组，跟脚底探测是同一类 GC 压力源。
    private readonly RaycastHit[] bodyCastBuffer = new RaycastHit[16];

    [Header("Debug")]
    public bool drawDebugProbe = false;
    public bool drawDebugState = false;

    [Header("External Input Ownership")]
    [Tooltip("调试显示：当前是否由外部系统接管输入。接管后 TerrainGroundMotor 不再读取 Unity Input。")]
    [SerializeField] private bool externalInputOwnedRuntime = false;
    [Tooltip("调试显示：当前外部输入主控名称。通常应为 UnitMovementController。") ]
    [SerializeField] private string externalInputOwnerRuntime = "None";

    public MotorState State => state;
    public bool IsGrounded => state == MotorState.Grounded;
    public bool IsSliding => state == MotorState.Sliding;
    public bool IsFalling => state == MotorState.Falling;
    public bool IsJumping => jumpActive;
    public float VerticalVelocity => verticalVelocity;
    public Vector3 CurrentGroundNormal => currentGroundNormal;
    public float CurrentSlopeAngle => currentSlopeAngle;
    public bool HasGroundAnchor => hasGroundAnchor;
    public Vector3 GroundAnchorPoint => groundAnchorPoint;


    private Rigidbody rb;
    private Collider ownCollider;
    private Vector3 horizontalVelocity;
    private Vector3 slideVelocity;
    private Vector3 knockbackVelocity;         // 击退冲量，独立于控制输入，按帧衰减
    private const float KnockbackDeceleration = 18f; // m/s²，衰减速率
    private float verticalVelocity;
    private bool jumpActive;
    // 空中攻击悬停用——悬停期间 SimulateGroundOrFall 提前return，跳过贴地/落地判定，
    // 但仍然按 hoverGravityScale 缩小过的重力继续缓慢下坠，不是完全定住不动。
    private bool verticalPhysicsFrozen;
    private float hoverGravityScale = 0.15f;
    private Vector2 externalMoveInput;
    private bool externalSprint;
    public bool HasExternalMoveInput => externalMoveInput.sqrMagnitude > 0.001f;
    private MotorState state = MotorState.Falling;
    private Vector3 currentGroundNormal = Vector3.up;
    private float currentSlopeAngle;
    private float jumpGroundIgnoreTimer;
    private bool hasGroundAnchor;
    private Vector3 groundAnchorPoint;
    private Vector3 lastFramePosition;
    private Vector3 lastSafePosition;
    private bool hasLastSafePosition;
    private float safePositionRecordTimer;
    private float stuckTimer;
    private float stuckExpectedDistanceAccum;
    private float stuckActualDistanceAccum;
    private float stuckRescueCooldownTimer;
    private float sprintBlockTimer;
    private Vector3 lastBlockingNormal;
    private bool bodyBlockedThisFrame;



    private struct GroundHit
    {
        public bool valid;
        public Vector3 point;
        public Vector3 normal;
        public float slopeAngle;
        public Collider collider;
        public Terrain terrain;
        public bool fromTerrainSample;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        InitializeDefaultUnitSeparationMaskIfNeeded();
        ResolveBodyCollider();

        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.freezeRotation = true;
        }
    }

    private void InitializeDefaultUnitSeparationMaskIfNeeded()
    {
        if (!autoUseCharacter2DLayerForUnitSeparation)
            return;

        if (unitSeparationMask.value != 0)
            return;

        int character2DLayer = LayerMask.NameToLayer("Character2D");
        if (character2DLayer >= 0)
            unitSeparationMask = 1 << character2DLayer;
    }

    private void ResolveBodyCollider()
    {
        ownCollider = null;

        if (bodyColliderOverride != null)
        {
            ownCollider = bodyColliderOverride;
            return;
        }

        Collider selfCollider = GetComponent<Collider>();
        if (IsUsableBodyCollider(selfCollider))
        {
            ownCollider = selfCollider;
            return;
        }

        if (autoFindBodyColliderInChildren)
        {
            Collider collisionRootCollider = null;
            if (preferCollisionRootCollider && !string.IsNullOrWhiteSpace(collisionRootName))
            {
                Transform collisionRoot = FindChildRecursive(transform, collisionRootName);
                if (collisionRoot != null)
                    collisionRootCollider = FindFirstUsableCollider(collisionRoot, includeInactiveBodyCollider);
            }

            if (collisionRootCollider != null)
            {
                ownCollider = collisionRootCollider;
                bodyColliderOverride = ownCollider;
                return;
            }

            Collider childCollider = FindFirstUsableCollider(transform, includeInactiveBodyCollider);
            if (childCollider != null)
            {
                ownCollider = childCollider;
                bodyColliderOverride = ownCollider;
                return;
            }
        }

        if (warnWhenBodyColliderMissing)
        {
            Debug.LogWarning(
                $"[TerrainGroundMotorV5] Body Collider not found on '{name}'. " +
                $"Add a Collider under '{collisionRootName}', or assign Body Collider Override. " +
                "Horizontal body collision / anti-hook will be disabled until a collider is found.",
                this);
        }
    }

    private Collider FindFirstUsableCollider(Transform root, bool includeInactive)
    {
        if (root == null)
            return null;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(includeInactive);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (!IsUsableBodyCollider(c))
                continue;

            if (c.transform == transform)
                continue;

            return c;
        }

        return null;
    }

    private bool IsUsableBodyCollider(Collider c)
    {
        if (c == null)
            return false;

        if (!c.enabled)
            return false;

        if (ignoreTriggerBodyColliders && c.isTrigger)
            return false;

        // TerrainCollider / WheelCollider 不是单位身体碰撞体，自动查找时跳过，避免拿错地面或载具轮子。
        if (c is TerrainCollider || c is WheelCollider)
            return false;

        return true;
    }

    private Transform FindChildRecursive(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        if (root.name == targetName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), targetName);
            if (found != null)
                return found;
        }

        return null;
    }

    private void EnsureBodyColliderResolved()
    {
        if (ownCollider != null && ownCollider.enabled)
            return;

        ResolveBodyCollider();
    }

    public void SetMoveInput(Vector2 moveInput, bool sprint)
    {
        externalMoveInput = Vector2.ClampMagnitude(moveInput, 1f);
        externalSprint = sprint;
    }

    public void ClaimExternalInputOwner(Object owner, string reason = null)
    {
        readUnityInput = false;
        enableSprintKey = false;
        externalInputOwnedRuntime = true;
        externalInputOwnerRuntime = owner != null ? owner.name : "External";
        if (!string.IsNullOrEmpty(reason))
            externalInputOwnerRuntime += " / " + reason;
    }

    public void ReleaseExternalInputOwner(Object owner)
    {
        if (owner != null && externalInputOwnerRuntime.StartsWith(owner.name))
        {
            externalInputOwnedRuntime = false;
            externalInputOwnerRuntime = "None";
            externalMoveInput = Vector2.zero;
            externalSprint = false;
        }
    }

    public bool HasExternalInputOwner => externalInputOwnedRuntime;
    public string ExternalInputOwnerRuntime => externalInputOwnerRuntime;

    public bool RequestJump(float overrideJumpHeight = -1f)
    {
        if (!enableJump)
            return false;

        bool canJumpFromCurrentState = state == MotorState.Grounded || (allowJumpFromSliding && state == MotorState.Sliding);
        if (!canJumpFromCurrentState)
            return false;

        float resolvedHeight = overrideJumpHeight > 0f ? overrideJumpHeight : jumpHeight;
        resolvedHeight = Mathf.Max(0.05f, resolvedHeight);

        verticalVelocity = Mathf.Sqrt(2f * Mathf.Max(0.01f, gravity) * resolvedHeight);
        jumpActive = true;
        jumpGroundIgnoreTimer = Mathf.Max(0f, jumpGroundIgnoreTime);
        state = MotorState.Falling;
        slideVelocity = Vector3.zero;
        return true;
    }

    public void CancelJumpVerticalVelocity()
    {
        if (verticalVelocity > 0f)
            verticalVelocity = 0f;
    }

    /// <summary>空中攻击悬停用——进入/解除"悬停模式"。冻住不是完全定住不动(那样像时间
    /// 停止，违和感很重)，而是用缩小过的重力(gravityScale)让角色继续缓慢、自然地下坠，
    /// 只是跳过了下面的贴地/落地判定(SimulateGroundOrFall提前return)，不会因为这段
    /// 时间沉得比较低就被误判成落地——真正的落地要等悬停解除、恢复正常重力/贴地判定
    /// 之后才可能发生，保证"演出还没结束就先落地"这种情况不会出现。
    /// 这是这套项目里 ExternalTerrainMotor 模式下真正驱动跳跃物理的地方——
    /// UnitMovementController 自己的 jumpVerticalVelocity/UpdateJumpPhysicsRuntime 在这个
    /// 模式下是死路径，要冻在这里才有效。
    /// liftAmount：进入悬停那一刻直接把位置向上抬这么多——一次性瞬间抬高，不是速度
    /// 冲量(冻结那一刻就把速度摁到0，速度冲量会被立刻吃掉感觉不出来)，做出"鸟拍一下
    /// 翅膀"那种干净利落的上升感，不是缓慢加速上去的。0=不额外抬高。
    /// gravityScale：悬停期间重力相对正常值的倍率，0=完全不下坠(旧的"冻住不动"效果，
    /// 已确认违和感太重不建议用)，1=跟正常下落一样快。默认给一个较小值，让角色悬停
    /// 时仍然有一点自然的缓慢下沉感，而不是像被按了暂停键。</summary>
    public void SetVerticalPhysicsFrozen(bool frozen, float liftAmount = 0f, float gravityScale = 0.15f)
    {
        verticalPhysicsFrozen = frozen;
        hoverGravityScale = Mathf.Max(0f, gravityScale);
        if (frozen)
        {
            verticalVelocity = 0f;
            if (liftAmount > 0f)
            {
                Vector3 pos = rb != null ? rb.position : transform.position;
                pos.y += liftAmount;
                MoveTo(pos);
            }
        }
    }

    // 复活/传送后调用：清零垂直速度并强制重置为 Grounded 状态
    // 死亡期间 Motor 以 Falling 状态持续累积重力，不清除会在复活后弹射角色
    public void ResetVerticalVelocity()
    {
        verticalVelocity = 0f;
        state = MotorState.Grounded; // 让下一帧做正常地面检测，而非继续 Falling
    }

    /// <summary>立即清零水平速度（受击硬直等需要瞬间停步的场合）。</summary>
    public void ResetHorizontalVelocity()
    {
        horizontalVelocity = Vector3.zero;
        slideVelocity      = Vector3.zero;
        knockbackVelocity  = Vector3.zero;
        externalMoveInput  = Vector2.zero;
        externalSprint     = false;
    }

    /// <summary>立即将水平速度强制设为指定值，跳过加速插值。闪避起手使用。</summary>
    public void SnapHorizontalVelocity(Vector3 velocity)
    {
        horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
        slideVelocity      = Vector3.zero;
    }

    /// <summary>施加一次性击退冲量。方向为世界空间 X 轴（2.5D 横版仅横向）。</summary>
    public void AddKnockbackImpulse(Vector3 worldImpulse)
    {
        knockbackVelocity = new Vector3(worldImpulse.x, 0f, 0f);
        horizontalVelocity = Vector3.zero; // 清零控制速度，让击退感更明显
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (jumpGroundIgnoreTimer > 0f)
            jumpGroundIgnoreTimer = Mathf.Max(0f, jumpGroundIgnoreTimer - dt);

        if (stuckRescueCooldownTimer > 0f)
            stuckRescueCooldownTimer = Mathf.Max(0f, stuckRescueCooldownTimer - dt);

        if (sprintBlockTimer > 0f)
            sprintBlockTimer = Mathf.Max(0f, sprintBlockTimer - dt);

        EnsureBodyColliderResolved();

        Vector3 position = rb != null ? rb.position : transform.position;
        lastFramePosition = position;
        bodyBlockedThisFrame = false;

        Vector3 footOffset = GetFootOffset();
        Vector3 footBefore = position + footOffset;

        bool allowLocalInput = readUnityInput && !externalInputOwnedRuntime;
        Vector2 moveInput = allowLocalInput ? ReadMoveInput() : externalMoveInput;
        bool sprint = allowLocalInput ? (enableSprintKey && Input.GetKey(KeyCode.LeftShift)) : externalSprint;
        if (enableSprintBlockGrace && sprintBlockTimer > 0f)
            sprint = false;

        TryRecordLastSafePosition(position, dt);

        Vector3 inputVelocity = BuildInputVelocity(moveInput, sprint, dt);
        if (jumpActive && state == MotorState.Falling)
            inputVelocity *= Mathf.Clamp01(airControlMultiplier);

        inputVelocity = FilterInputVelocityBySlope(position, inputVelocity, footOffset, dt);

        // 击退冲量衰减（每帧在输入之外独立叠加）
        if (knockbackVelocity.sqrMagnitude > 0.0001f)
        {
            knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, KnockbackDeceleration * dt);
        }
        else
        {
            knockbackVelocity = Vector3.zero;
        }

        Vector3 candidate = position;

        if (state == MotorState.Sliding)
        {
            candidate = SimulateSliding(position, inputVelocity, footOffset, dt, footBefore);
        }
        else
        {
            candidate += (inputVelocity + knockbackVelocity) * dt;
            candidate = SimulateGroundOrFall(position, candidate, footOffset, dt, footBefore);
        }

        Vector3 beforeHorizontalResolve = candidate;
        if (enableHorizontalBodyCollision)
            candidate = ResolveHorizontalBodyCollision(position, candidate);

        Vector3 expectedHorizontal = new Vector3(beforeHorizontalResolve.x - position.x, 0f, beforeHorizontalResolve.z - position.z);
        Vector3 actualHorizontal = new Vector3(candidate.x - position.x, 0f, candidate.z - position.z);

        candidate = ApplyCommercialStuckRescue(
            position,
            candidate,
            moveInput,
            sprint,
            expectedHorizontal,
            actualHorizontal,
            dt);

        Vector3 beforeUnitSeparation = candidate;
        candidate = ApplyUnitSoftSeparation(candidate, dt);

        // Unit separation is a soft gameplay push, not an authority that may tunnel through world blockers.
        // After units push each other apart, run the pushed segment through the same body-block solver again,
        // so another unit can never shove this unit into walls / props / containers.
        if (resolveBodyBlockAfterUnitSeparation && enableHorizontalBodyCollision)
            candidate = ResolveUnitSeparationBodyBlock(beforeUnitSeparation, candidate);

        // 兜底退穿透：修复跳跃 Y 上升时身体侧面撞进墙壁的情况。
        // 水平 solve 只覆盖 X/Z 位移向量，Y 抬升进墙不在扫掠路径内，此处补一次。
        if (depenetrateBodyAfterSolve && enableHorizontalBodyCollision)
            candidate = DepenetrateBodyHorizontally(candidate);

        MoveTo(candidate);
    }

    private Vector3 ApplyCommercialStuckRescue(
        Vector3 previousPosition,
        Vector3 candidate,
        Vector2 moveInput,
        bool sprint,
        Vector3 expectedHorizontal,
        Vector3 actualHorizontal,
        float dt)
    {
        if (!enableStuckRescue)
        {
            ResetStuckAccumulationIfMoving(actualHorizontal.magnitude);
            return candidate;
        }

        float inputMagnitude = moveInput.magnitude;
        float expectedDistance = expectedHorizontal.magnitude;
        float actualDistance = actualHorizontal.magnitude;

        bool wantsMove = inputMagnitude >= stuckMinInputMagnitude;
        bool expectedEnough = expectedDistance > 0.0005f;
        bool actuallyBlocked = actualDistance <= Mathf.Max(0.0001f, expectedDistance * 0.18f);

        if (wantsMove && expectedEnough && actuallyBlocked)
        {
            stuckTimer += dt;
            stuckExpectedDistanceAccum += expectedDistance;
            stuckActualDistanceAccum += actualDistance;
        }
        else
        {
            ResetStuckAccumulationIfMoving(actualDistance);
            return candidate;
        }

        bool timeReached = stuckTimer >= Mathf.Max(0.02f, stuckCheckSeconds);
        bool distanceReached = stuckExpectedDistanceAccum >= Mathf.Max(0.001f, stuckMinExpectedDistance);
        bool movedTooLittle = stuckActualDistanceAccum <= Mathf.Max(0f, stuckMaxActualDistance);
        bool cooldownReady = stuckRescueCooldownTimer <= 0f;

        if (!timeReached || !distanceReached || !movedTooLittle || !cooldownReady)
            return candidate;

        Vector3 rescued = ResolveRescuePosition(previousPosition, candidate, expectedHorizontal);

        if (depenetrateBodyDuringHorizontalSolve)
            rescued = DepenetrateBodyHorizontally(rescued);

        if (clearVelocityWhenRescued)
        {
            horizontalVelocity = Vector3.zero;
            slideVelocity = Vector3.zero;
        }

        if (enableSprintBlockGrace && sprint)
            sprintBlockTimer = Mathf.Max(sprintBlockTimer, sprintBlockGraceSeconds);

        stuckRescueCooldownTimer = Mathf.Max(0f, stuckRescueCooldown);
        ResetStuckAccumulationHard();

        if (drawDebugProbe)
        {
            Debug.DrawLine(candidate + Vector3.up * 0.15f, rescued + Vector3.up * 0.15f, Color.magenta, 0.25f);
            Debug.DrawRay(rescued + Vector3.up * 0.2f, Vector3.up * 0.75f, Color.magenta, 0.25f);
        }

        return rescued;
    }

    private Vector3 ResolveRescuePosition(Vector3 previousPosition, Vector3 candidate, Vector3 expectedHorizontal)
    {
        Vector3 rescued = candidate;
        Vector3 normal = lastBlockingNormal;
        normal.y = 0f;

        if (rescuePreferBlockingNormal && normal.sqrMagnitude > 0.0001f)
        {
            rescued += normal.normalized * Mathf.Max(0.001f, stuckRescueDistance);
            return new Vector3(rescued.x, candidate.y, rescued.z);
        }

        if (enableLastSafePosition && hasLastSafePosition)
        {
            Vector3 safe = new Vector3(lastSafePosition.x, candidate.y, lastSafePosition.z);
            float maxRewind = Mathf.Max(0.001f, Mathf.Min(lastSafeRewindDistance, stuckRescueDistance));
            rescued = Vector3.MoveTowards(candidate, safe, maxRewind);
            return new Vector3(rescued.x, candidate.y, rescued.z);
        }

        Vector3 fallback = expectedHorizontal;
        fallback.y = 0f;
        if (fallback.sqrMagnitude > 0.0001f)
            rescued -= fallback.normalized * Mathf.Max(0.001f, stuckRescueDistance);

        return new Vector3(rescued.x, candidate.y, rescued.z);
    }

    private void TryRecordLastSafePosition(Vector3 position, float dt)
    {
        if (!enableLastSafePosition)
            return;

        safePositionRecordTimer -= dt;
        if (safePositionRecordTimer > 0f)
            return;

        safePositionRecordTimer = Mathf.Max(0.005f, recordSafePositionInterval);

        if (safePositionMustBeGrounded && state != MotorState.Grounded)
            return;

        if (safePositionMustNotOverlapBody && HasBlockingBodyOverlap(position))
            return;

        lastSafePosition = position;
        hasLastSafePosition = true;
    }

    private bool HasBlockingBodyOverlap(Vector3 bodyPosition)
    {
        if (ownCollider == null)
            return false;

        Bounds b = ownCollider.bounds;
        Vector3 centerOffset = b.center - transform.position;
        Vector3 center = bodyPosition + centerOffset;
        Vector3 extents = b.extents;
        float shrink = Mathf.Max(0f, bodyCastShrink + bodyCastSkin);
        extents.x = Mathf.Max(0.01f, extents.x - shrink);
        extents.z = Mathf.Max(0.01f, extents.z - shrink);
        extents.y = Mathf.Max(0.02f, extents.y - Mathf.Max(0f, bodyCastShrink));

        int count;
        if (useRoundedBodySweep && GetBodySweepCapsule(bodyPosition, out Vector3 pointA, out Vector3 pointB, out float radius, false))
        {
            count = Physics.OverlapCapsuleNonAlloc(
                pointA,
                pointB,
                Mathf.Max(0.01f, radius),
                bodyOverlapBuffer,
                bodyBlockMask,
                QueryTriggerInteraction.Ignore);
        }
        else
        {
            count = Physics.OverlapBoxNonAlloc(
                center,
                extents,
                bodyOverlapBuffer,
                Quaternion.identity,
                bodyBlockMask,
                QueryTriggerInteraction.Ignore);
        }

        for (int i = 0; i < count; i++)
        {
            Collider c = bodyOverlapBuffer[i];
            if (ShouldIgnoreAsBodyBlock(c))
                continue;

            return true;
        }

        return false;
    }

    private void ResetStuckAccumulationIfMoving(float actualDistance)
    {
        if (actualDistance > Mathf.Max(0.001f, stuckMaxActualDistance))
            ResetStuckAccumulationHard();
    }

    private void ResetStuckAccumulationHard()
    {
        stuckTimer = 0f;
        stuckExpectedDistanceAccum = 0f;
        stuckActualDistanceAccum = 0f;
    }

    private Vector3 BuildInputVelocity(Vector2 moveInput, bool sprint, float dt)
    {
        Vector3 desired = new Vector3(moveInput.x, 0f, moveInput.y);
        desired = Vector3.ClampMagnitude(desired, 1f);
        desired *= sprint ? sprintSpeed : moveSpeed;

        float accel = desired.sqrMagnitude > 0.0001f ? acceleration : deceleration;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desired, accel * dt);

        return horizontalVelocity;
    }

    private Vector3 FilterInputVelocityBySlope(Vector3 position, Vector3 inputVelocity, Vector3 footOffset, float dt)
    {
        if (!blockUphillOnTooSteepSlope)
            return inputVelocity;

        Vector3 horizontal = new Vector3(inputVelocity.x, 0f, inputVelocity.z);
        if (horizontal.sqrMagnitude < 0.0001f)
            return inputVelocity;

        Vector3 footNow = position + footOffset;
        Vector3 nextPosition = position + horizontal * dt;
        Vector3 footNext = nextPosition + footOffset;

        GroundHit currentGround;
        GroundHit nextGround;
        bool hasCurrentGround = FindGroundAtFootXZ(footNow, true, out currentGround);
        bool hasNextGround = FindGroundAtFootXZ(footNext, true, out nextGround);

        if (!hasNextGround)
            return inputVelocity;

        float referenceGroundY = hasCurrentGround ? currentGround.point.y : footNow.y;
        float heightDelta = nextGround.point.y - referenceGroundY;
        bool nextTooSteep = nextGround.slopeAngle > maxWalkableSlope;
        bool nextHardBlocked = nextGround.slopeAngle >= hardBlockSlope;

        if (!nextTooSteep)
            return inputVelocity;

        // Terrain 法线的水平反方向就是坡面“往上爬”的方向。
        Vector3 uphill = new Vector3(-nextGround.normal.x, 0f, -nextGround.normal.z);
        if (uphill.sqrMagnitude < 0.0001f)
            return inputVelocity;

        uphill.Normalize();
        float uphillAmount = Vector3.Dot(horizontal, uphill);
        bool movingUpSteepSlope = uphillAmount > 0.0001f || heightDelta > steepUphillBlockHeight;

        if (!movingUpSteepSlope)
            return inputVelocity;

        Vector3 filteredHorizontal = horizontal;

        // 45~HardBlockSlope：移除向坡上爬的分量，允许沿等高线横移。
        // >= HardBlockSlope：更像墙，仍保留横向分量，但绝不允许往上顶。
        if (uphillAmount > 0f)
            filteredHorizontal -= uphill * uphillAmount;

        if (nextHardBlocked && filteredHorizontal.sqrMagnitude < 0.0004f)
            filteredHorizontal = Vector3.zero;

        if (clearVelocityWhenBlockedBySlope && filteredHorizontal.sqrMagnitude < horizontal.sqrMagnitude * 0.25f)
            horizontalVelocity = filteredHorizontal;

        if (drawDebugProbe)
        {
            Debug.DrawRay(nextGround.point + Vector3.up * 0.08f, uphill * 0.8f, Color.blue, Time.fixedDeltaTime);
            Debug.DrawLine(footNow, footNext, nextHardBlocked ? Color.red : Color.yellow, Time.fixedDeltaTime);
        }

        return new Vector3(filteredHorizontal.x, inputVelocity.y, filteredHorizontal.z);
    }

    private Vector3 SimulateGroundOrFall(Vector3 previousPosition, Vector3 candidate, Vector3 footOffset, float dt, Vector3 footBefore)
    {
        // 悬停：跳过下面的贴地/落地判定，但仍按缩小过的重力(hoverGravityScale)继续
        // 缓慢下坠——不是完全定住不动(那样像时间停止，违和感很重)。哪怕沉得比较低也
        // 不会被这个函数判定成落地，真正的落地要等悬停解除、恢复正常重力/贴地判定
        // 之后才可能发生。
        if (verticalPhysicsFrozen)
        {
            if (hoverGravityScale > 0f)
            {
                verticalVelocity = Mathf.Max(verticalVelocity - gravity * hoverGravityScale * dt, -maxFallSpeed);
                candidate.y += verticalVelocity * dt;
            }
            return candidate;
        }

        GroundHit ground;
        Vector3 footAtCandidate = candidate + footOffset;

        if (jumpActive && verticalVelocity > 0f)
        {
            state = MotorState.Falling;
            verticalVelocity = Mathf.Max(verticalVelocity - gravity * dt, -maxFallSpeed);
            candidate.y += verticalVelocity * dt;

            // 跑上坡同时跳跃时，Terrain 高度可能在水平位移后突然高过脚底。
            // 起跳宽限期内不重新贴地，但绝不允许脚底穿进 Terrain 高度场下面。
            GroundHit uphillGround;
            if (FindGroundAtFootXZ(candidate + footOffset, true, true, out uphillGround))
            {
                float footToGround = (candidate + footOffset).y - uphillGround.point.y;
                if (footToGround < -landingTolerance || (uphillGround.fromTerrainSample && footToGround < 0f))
                {
                    verticalVelocity = 0f;
                    if (IsWalkable(uphillGround))
                        return SnapToGround(candidate, footOffset, uphillGround, MotorState.Grounded);

                    if (enableSliding)
                        return EnterOrContinueSliding(candidate, footOffset, uphillGround, dt);
                }
            }

            DrawState(candidate, default, MotorState.Falling);
            return candidate;
        }

        bool hasGround = FindGroundAtFootXZ(footAtCandidate, true, out ground);

        if (hasGround)
        {
            bool walkable = IsWalkable(ground);
            float distanceToGround = footAtCandidate.y - ground.point.y;

            if (walkable && CanSnapToWalkableGround(distanceToGround))
                return SnapToGround(candidate, footOffset, ground, MotorState.Grounded);

            if (!walkable && enableSliding && CanTouchSteepGround(distanceToGround))
                return EnterOrContinueSliding(candidate, footOffset, ground, dt);
        }

        state = MotorState.Falling;
        verticalVelocity = Mathf.Max(verticalVelocity - gravity * dt, -maxFallSpeed);
        candidate.y += verticalVelocity * dt;

        Vector3 footAfter = candidate + footOffset;
        GroundHit landingGround;
        // allowStepUp=true：下落落地时脚底可能已经穿进物体，不过滤"地面高于脚底"的情况
        bool hasLandingGround = FindGroundAtFootXZ(footAfter, true, true, out landingGround);

        if (hasLandingGround && ShouldCatchLanding(footBefore, footAfter, landingGround))
        {
            if (IsWalkable(landingGround))
                return SnapToGround(candidate, footOffset, landingGround, MotorState.Grounded);

            if (enableSliding)
                return EnterOrContinueSliding(candidate, footOffset, landingGround, dt);
        }

        // Terrain 是高度场，没有可进入的地下空间。只要下落后脚底已经低于 Terrain 表面，
        // 就强制接回地表，避免跑坡跳跃时因为水平位移导致采样高度突变而坠入地底。
        if (hasLandingGround && landingGround.fromTerrainSample && footAfter.y < landingGround.point.y - landingTolerance)
        {
            if (IsWalkable(landingGround))
                return SnapToGround(candidate, footOffset, landingGround, MotorState.Grounded);

            if (enableSliding)
                return EnterOrContinueSliding(candidate, footOffset, landingGround, dt);
        }

        DrawState(candidate, hasLandingGround ? landingGround : default, MotorState.Falling);
        return candidate;
    }

    private Vector3 SimulateSliding(Vector3 position, Vector3 inputVelocity, Vector3 footOffset, float dt, Vector3 footBefore)
    {
        Vector3 foot = position + footOffset;
        GroundHit ground;
        bool hasGround = FindGroundAtFootXZ(foot, true, out ground);

        if (!hasGround)
        {
            state = MotorState.Falling;
            slideVelocity = Vector3.MoveTowards(slideVelocity, Vector3.zero, slideDeceleration * dt);
            return SimulateGroundOrFall(position, position + inputVelocity * dt, footOffset, dt, footBefore);
        }

        if (IsWalkable(ground) && Mathf.Abs(foot.y - ground.point.y) <= groundedSnapDistance + stepDownSnapDistance)
        {
            slideVelocity = Vector3.zero;
            return SnapToGround(position + inputVelocity * dt, footOffset, ground, MotorState.Grounded);
        }

        float distanceToGround = foot.y - ground.point.y;
        if (!CanTouchSteepGround(distanceToGround))
        {
            state = MotorState.Falling;
            return SimulateGroundOrFall(position + inputVelocity * dt, position + inputVelocity * dt, footOffset, dt, footBefore);
        }

        Vector3 slideDirection = Vector3.ProjectOnPlane(Vector3.down, ground.normal);
        if (slideDirection.sqrMagnitude < 0.0001f)
            slideDirection = Vector3.zero;
        else
            slideDirection.Normalize();

        Vector3 desiredSlideVelocity = slideDirection * maxSlideSpeed;
        slideVelocity = Vector3.MoveTowards(slideVelocity, desiredSlideVelocity, slideAcceleration * dt);

        Vector3 controlledInput = allowInputWhileSliding ? inputVelocity * inputControlWhileSliding : Vector3.zero;
        Vector3 candidate = position + (slideVelocity + controlledInput) * dt;

        GroundHit groundAfter;
        if (FindGroundAtFootXZ(candidate + footOffset, true, out groundAfter))
        {
            if (IsWalkable(groundAfter))
            {
                slideVelocity = Vector3.zero;
                return SnapToGround(candidate, footOffset, groundAfter, MotorState.Grounded);
            }

            if (CanTouchSteepGround((candidate + footOffset).y - groundAfter.point.y))
                return SnapToGround(candidate, footOffset, groundAfter, MotorState.Sliding);
        }

        state = MotorState.Falling;
        verticalVelocity = Mathf.Min(verticalVelocity, 0f);
        DrawState(candidate, groundAfter, MotorState.Falling);
        return candidate;
    }

    private Vector3 EnterOrContinueSliding(Vector3 candidate, Vector3 footOffset, GroundHit ground, float dt)
    {
        verticalVelocity = 0f;
        state = MotorState.Sliding;
        if (verticalVelocity <= 0f)
            jumpActive = false;
        currentGroundNormal = ground.normal;
        currentSlopeAngle = ground.slopeAngle;
        hasGroundAnchor = ground.valid;
        if (ground.valid)
            groundAnchorPoint = ground.point;

        Vector3 snapped = SnapY(candidate, footOffset, ground);
        DrawState(snapped, ground, MotorState.Sliding);
        return snapped;
    }

    private bool CanSnapToWalkableGround(float distanceToGround)
    {
        if (state == MotorState.Grounded)
            return distanceToGround <= groundedSnapDistance + stepDownSnapDistance && distanceToGround >= -belowGroundRecoverDistance;

        if (state == MotorState.Sliding)
            return distanceToGround <= groundedSnapDistance + stepDownSnapDistance && distanceToGround >= -belowGroundRecoverDistance;

        if (jumpGroundIgnoreTimer > 0f && distanceToGround > -landingTolerance)
            return false;

        return Mathf.Abs(distanceToGround) <= groundedSnapDistance;
    }

    private bool CanTouchSteepGround(float distanceToGround)
    {
        return distanceToGround <= steepContactDistance && distanceToGround >= -belowGroundRecoverDistance;
    }

    private bool ShouldCatchLanding(Vector3 footBefore, Vector3 footAfter, GroundHit ground)
    {
        bool crossed = footBefore.y >= ground.point.y - landingTolerance &&
                       footAfter.y <= ground.point.y + landingExtraDistance;

        bool recoverableBelow = footAfter.y < ground.point.y - landingTolerance &&
                                (ground.fromTerrainSample || ground.point.y - footAfter.y <= belowGroundRecoverDistance);

        bool closeEnough = Mathf.Abs(footAfter.y - ground.point.y) <= landingExtraDistance;
        return crossed || recoverableBelow || closeEnough;
    }

    private Vector3 SnapToGround(Vector3 position, Vector3 footOffset, GroundHit ground, MotorState newState)
    {
        if (newState == MotorState.Grounded)
        {
            verticalVelocity = 0f;
            slideVelocity = Vector3.zero;
            jumpActive = false;
        }

        state = newState;
        currentGroundNormal = ground.normal;
        currentSlopeAngle = ground.slopeAngle;
        hasGroundAnchor = ground.valid;
        if (ground.valid)
            groundAnchorPoint = ground.point;

        Vector3 snapped = SnapY(position, footOffset, ground);
        DrawState(snapped, ground, newState);
        return snapped;
    }

    private Vector3 SnapY(Vector3 position, Vector3 footOffset, GroundHit ground)
    {
        Vector3 foot = position + footOffset;
        position.y += ground.point.y + footHeightOffset - foot.y;
        return position;
    }

    private bool IsWalkable(GroundHit ground)
    {
        return ground.valid && ground.slopeAngle <= maxWalkableSlope;
    }

    private Vector2 ReadMoveInput()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        return Vector2.ClampMagnitude(new Vector2(x, z), 1f);
    }

    private Vector3 GetFootOffset()
    {
        if (footProbe != null)
            return footProbe.position - transform.position;

        if (ownCollider != null)
        {
            Bounds b = ownCollider.bounds;
            Vector3 bottom = new Vector3(transform.position.x, b.min.y, transform.position.z);
            return bottom - transform.position;
        }

        return Vector3.zero;
    }

    private bool FindGroundAtFootXZ(Vector3 footWorld, bool includeSteep, out GroundHit bestGround)
        => FindGroundAtFootXZ(footWorld, includeSteep, false, out bestGround);

    // allowStepUp=true 时跳过 limitPhysicsGroundStepUp 过滤，用于下落落地检测。
    private bool FindGroundAtFootXZ(Vector3 footWorld, bool includeSteep, bool allowStepUp, out GroundHit bestGround)
    {
        bestGround = new GroundHit { valid = false, normal = Vector3.up };
        bool found = false;
        float bestScore = float.PositiveInfinity;

        if (preferTerrainSampleHeight)
            found |= TryFindTerrainGround(footWorld, includeSteep, ref bestGround, ref bestScore);

        found |= TryFindPhysicsGround(footWorld, includeSteep, allowStepUp, ref bestGround, ref bestScore);

        if (!preferTerrainSampleHeight)
            found |= TryFindTerrainGround(footWorld, includeSteep, ref bestGround, ref bestScore);

        return found && bestGround.valid;
    }

    private bool TryFindTerrainGround(Vector3 footWorld, bool includeSteep, ref GroundHit bestGround, ref float bestScore)
    {
        bool found = false;
        Terrain[] terrains = Terrain.activeTerrains;

        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain t = terrains[i];
            if (t == null || t.terrainData == null)
                continue;

            if (!IsLayerInMask(t.gameObject.layer, groundMask))
                continue;

            Vector3 tPos = t.transform.position;
            TerrainData data = t.terrainData;
            Vector3 size = data.size;

            if (footWorld.x < tPos.x || footWorld.x > tPos.x + size.x ||
                footWorld.z < tPos.z || footWorld.z > tPos.z + size.z)
                continue;

            float normalizedX = Mathf.InverseLerp(tPos.x, tPos.x + size.x, footWorld.x);
            float normalizedZ = Mathf.InverseLerp(tPos.z, tPos.z + size.z, footWorld.z);

            float groundY = t.SampleHeight(footWorld) + tPos.y;
            Vector3 normal = data.GetInterpolatedNormal(normalizedX, normalizedZ).normalized;
            float slope = Vector3.Angle(normal, Vector3.up);

            if (!includeSteep && slope > maxWalkableSlope)
                continue;

            float verticalDistance = Mathf.Abs(footWorld.y - groundY);
            float score = verticalDistance;

            if (score < bestScore)
            {
                bestScore = score;
                bestGround = new GroundHit
                {
                    valid = true,
                    point = new Vector3(footWorld.x, groundY, footWorld.z),
                    normal = normal,
                    slopeAngle = slope,
                    terrain = t,
                    collider = t.GetComponent<TerrainCollider>(),
                    fromTerrainSample = true
                };
                found = true;
            }
        }

        return found;
    }

    private bool TryFindPhysicsGround(Vector3 footWorld, bool includeSteep, ref GroundHit bestGround, ref float bestScore)
        => TryFindPhysicsGround(footWorld, includeSteep, false, ref bestGround, ref bestScore);

    private bool TryFindPhysicsGround(Vector3 footWorld, bool includeSteep, bool allowStepUp, ref GroundHit bestGround, ref float bestScore)
    {
        bool found = false;
        Vector3 origin = new Vector3(footWorld.x, footWorld.y + probeHeight, footWorld.z);
        float distance = probeHeight + probeDistance;

        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, groundRaycastBuffer, distance, groundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundRaycastBuffer[i];
            if (hit.collider == null)
                continue;

            if (IsSelfCollider(hit.collider))
                continue;

            if (ShouldRejectAsPhysicsGround(hit.collider, footWorld, hit.point, allowStepUp))
                continue;

            float slope = Vector3.Angle(hit.normal, Vector3.up);
            if (!includeSteep && slope > maxWalkableSlope)
                continue;

            // 关键：盒子边、Mesh 侧面、尖角被脚底向下 Raycast 命中时，法线通常接近墙面。
            // 旧逻辑会把它当成“陡坡地面”，然后进入 Sliding / 坡面阻挡，表现为角色被边角黏住。
            // Terrain 的陡坡仍由 Terrain Sample 处理；这里主要过滤非 Terrain 的物理边角。
            if (ignoreSteepPhysicsGroundAsGround && slope > maxPhysicsGroundSlopeAsGround)
                continue;

            float verticalDistance = Mathf.Abs(footWorld.y - hit.point.y);
            float score = verticalDistance + 0.05f;

            if (score < bestScore)
            {
                bestScore = score;
                bestGround = new GroundHit
                {
                    valid = true,
                    point = hit.point,
                    normal = hit.normal,
                    slopeAngle = slope,
                    collider = hit.collider,
                    terrain = hit.collider.GetComponent<Terrain>(),
                    fromTerrainSample = false
                };
                found = true;
            }
        }

        return found;
    }

    private bool IsSelfCollider(Collider col)
    {
        if (col == null)
            return false;

        if (ownCollider != null && col == ownCollider)
            return true;

        return col.transform == transform || col.transform.IsChildOf(transform);
    }

    private bool ShouldRejectAsPhysicsGround(Collider col, Vector3 footWorld, Vector3 hitPoint, bool allowStepUp = false)
    {
        if (col == null)
            return true;

        if (rejectBodyBlockCollidersAsGround && IsLayerInMask(col.gameObject.layer, bodyBlockMask))
            return true;

        if (rejectUnitCollidersAsGround && IsUnitCollider(col))
            return true;

        if (!allowStepUp && limitPhysicsGroundStepUp && hitPoint.y - footWorld.y > Mathf.Max(0f, maxPhysicsGroundStepUpHeight))
            return true;

        return false;
    }

    private bool ShouldIgnoreAsBodyBlock(Collider col)
    {
        if (col == null)
            return true;

        if (IsSelfCollider(col))
            return true;

        if (col.isTrigger)
            return true;

        if (!IsLayerInMask(col.gameObject.layer, bodyBlockMask))
            return true;

        if (ignoreUnitCollidersInBodyBlock && IsUnitCollider(col))
            return true;

        // 可推道具(锥桶/路障)由推开物理处理，不作为身体硬阻挡；否则蹭它凸包边会被勾住。
        if (ignorePushablePropsInBodyBlock && col.GetComponentInParent<SkyPrisonPushablePropRuntime>() != null)
            return true;

        return false;
    }

    private bool IsUnitCollider(Collider col)
    {
        if (col == null)
            return false;

        if (IsSelfCollider(col))
            return false;

        if (unitSeparationMask.value != 0 && IsLayerInMask(col.gameObject.layer, unitSeparationMask))
            return true;

        if (identifyUnitByTerrainGroundMotor)
        {
            TerrainGroundMotorV5 otherMotor = col.GetComponentInParent<TerrainGroundMotorV5>();
            if (otherMotor != null && otherMotor != this)
                return true;
        }

        return false;
    }

    private bool IsLayerInMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    private Vector3 ResolveHorizontalBodyCollision(Vector3 fromPosition, Vector3 targetPosition)
    {
        Vector3 totalDelta = targetPosition - fromPosition;
        Vector3 horizontalDelta = new Vector3(totalDelta.x, 0f, totalDelta.z);
        if (horizontalDelta.sqrMagnitude < 0.000001f)
            return targetPosition;

        if (!enableSprintCornerAntiHook || maxBodySweepStepDistance <= 0.01f)
            return ResolveHorizontalBodyCollisionStep(fromPosition, targetPosition, horizontalDelta);

        // V9：这里不再用“原始 from -> 原始 target 的等分点”作为每一步目标。
        // V8 在退穿透把角色推出角后，下一小步又会朝原始路径拉回去，高速冲角时等于反复把角色送回钩子。
        // 现在改成：每一步都从当前 resolved 出发，沿本帧原始水平位移方向推进一小段。
        float totalDistance = horizontalDelta.magnitude;
        int stepCount = Mathf.Clamp(Mathf.CeilToInt(totalDistance / Mathf.Max(0.01f, maxBodySweepStepDistance)), 1, 12);
        Vector3 stepHorizontal = horizontalDelta / stepCount;

        Vector3 resolved = fromPosition;
        if (depenetrateBodyDuringHorizontalSolve)
            resolved = DepenetrateBodyHorizontally(resolved);

        for (int step = 0; step < stepCount; step++)
        {
            float y0 = Mathf.Lerp(fromPosition.y, targetPosition.y, (float)step / stepCount);
            float y1 = Mathf.Lerp(fromPosition.y, targetPosition.y, (float)(step + 1) / stepCount);

            Vector3 stepFrom = new Vector3(resolved.x, y0, resolved.z);
            if (depenetrateBodyDuringHorizontalSolve)
                stepFrom = DepenetrateBodyHorizontally(stepFrom);

            Vector3 wantedStep = stepHorizontal;

            // 如果关闭 KeepDepenetratedPath，则保留旧的“朝原始目标线追赶”行为，方便对比。
            if (!keepDepenetratedPath)
            {
                Vector3 oldStyleTarget = new Vector3(
                    fromPosition.x + horizontalDelta.x * ((float)(step + 1) / stepCount),
                    y1,
                    fromPosition.z + horizontalDelta.z * ((float)(step + 1) / stepCount));
                wantedStep = new Vector3(oldStyleTarget.x - stepFrom.x, 0f, oldStyleTarget.z - stepFrom.z);
            }

            Vector3 stepTarget = new Vector3(stepFrom.x + wantedStep.x, y1, stepFrom.z + wantedStep.z);
            Vector3 before = stepFrom;
            Vector3 after = ResolveHorizontalBodyCollisionStep(stepFrom, stepTarget, stepTarget - stepFrom);

            if (depenetrateBodyDuringHorizontalSolve)
                after = DepenetrateBodyHorizontally(after);

            resolved = after;

            Vector3 actualStep = new Vector3(after.x - before.x, 0f, after.z - before.z);
            Vector3 expectedStep = new Vector3(stepTarget.x - before.x, 0f, stepTarget.z - before.z);

            // 本小步被完全挡住时，停止继续把角色送向同一个角，并清掉残余奔跑速度。
            if (expectedStep.sqrMagnitude > 0.0001f && Vector3.Dot(expectedStep.normalized, actualStep) <= 0.0005f)
            {
                if (clearVelocityWhenBodyBlocked)
                {
                    horizontalVelocity = Vector3.zero;
                    slideVelocity = Vector3.zero;
                }
                break;
            }
        }

        return new Vector3(resolved.x, targetPosition.y, resolved.z);
    }

    private Vector3 ResolveHorizontalBodyCollisionStep(Vector3 fromPosition, Vector3 targetPosition, Vector3 stepDelta)
    {
        Vector3 horizontalDelta = new Vector3(stepDelta.x, 0f, stepDelta.z);
        if (horizontalDelta.sqrMagnitude < 0.000001f)
            return targetPosition;

        Vector3 resolved = fromPosition;
        Vector3 remaining = horizontalDelta;
        Vector3 previousWallNormal = Vector3.zero;
        bool blockedThisStep = false;

        int iterations = Mathf.Clamp(horizontalSlideIterations, 1, 4);
        for (int i = 0; i < iterations; i++)
        {
            float distance = remaining.magnitude;
            if (distance <= 0.0001f)
                break;

            Vector3 direction = remaining / distance;
            RaycastHit hit;
            if (!CastBodyForHorizontalBlock(resolved, direction, distance + Mathf.Max(0f, bodyCastSkin), out hit))
            {
                resolved += remaining;
                remaining = Vector3.zero;
                break;
            }

            blockedThisStep = true;

            float safeTravel = Mathf.Max(0f, hit.distance - Mathf.Max(0f, bodyCastSkin));
            safeTravel = Mathf.Min(safeTravel, distance);
            resolved += direction * safeTravel;

            Vector3 wallNormal = hit.normal;
            wallNormal.y = 0f;

            if (wallNormal.sqrMagnitude < 0.0001f)
            {
                remaining = Vector3.zero;
                break;
            }

            wallNormal.Normalize();
            bodyBlockedThisFrame = true;
            lastBlockingNormal = wallNormal;

            if (clearVelocityWhenBodyBlocked)
                RemoveVelocityIntoWall(wallNormal);

            // 连续撞到两个差异很大的墙面法线时，说明角色已经被送进内角。
            // 不再继续把剩余位移投影到第二面墙上，否则会在 A 面 / B 面之间来回咬住。
            if (enableCornerDeadlockBreak && previousWallNormal.sqrMagnitude > 0.0001f)
            {
                float normalDot = Vector3.Dot(previousWallNormal, wallNormal);
                if (normalDot <= cornerNormalDotThreshold)
                {
                    Vector3 cornerOut = previousWallNormal + wallNormal;
                    cornerOut.y = 0f;
                    if (cornerOut.sqrMagnitude > 0.0001f && cornerPushOutDistance > 0f)
                        resolved += cornerOut.normalized * cornerPushOutDistance;

                    remaining = Vector3.zero;
                    if (clearVelocityWhenCornerLocked)
                    {
                        horizontalVelocity = Vector3.zero;
                        slideVelocity = Vector3.zero;
                    }
                    break;
                }
            }

            previousWallNormal = wallNormal;

            Vector3 consumed = direction * safeTravel;
            Vector3 left = remaining - consumed;

            // 沿墙面切线滑走，而不是卡在箱子角上。
            remaining = Vector3.ProjectOnPlane(left, wallNormal);
            remaining.y = 0f;

            if (remaining.sqrMagnitude < 0.00001f)
            {
                remaining = Vector3.zero;
                break;
            }
        }

        if (blockedThisStep && clearVelocityWhenBodyBlocked && previousWallNormal.sqrMagnitude > 0.0001f)
        {
            horizontalVelocity = Vector3.ProjectOnPlane(horizontalVelocity, previousWallNormal);
            horizontalVelocity.y = 0f;
        }

        return new Vector3(resolved.x, targetPosition.y, resolved.z);
    }

    private void RemoveVelocityIntoWall(Vector3 wallNormal)
    {
        wallNormal.y = 0f;
        if (wallNormal.sqrMagnitude < 0.0001f)
            return;

        wallNormal.Normalize();

        float intoWall = Vector3.Dot(horizontalVelocity, -wallNormal);
        if (intoWall > 0f)
            horizontalVelocity += wallNormal * intoWall;

        float slideIntoWall = Vector3.Dot(slideVelocity, -wallNormal);
        if (slideIntoWall > 0f)
            slideVelocity += wallNormal * slideIntoWall;
    }

    private Vector3 DepenetrateBodyHorizontally(Vector3 bodyPosition)
    {
        if (ownCollider == null)
            return bodyPosition;

        Vector3 resolved = bodyPosition;
        int iterations = Mathf.Clamp(bodyDepenetrationIterations, 1, 4);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if (!GetBodySweepCapsule(resolved, out Vector3 pointA, out Vector3 pointB, out float radius, false))
                break;

            float overlapRadius = radius + Mathf.Max(0f, depenetrateOnlyRealOverlap ? bodyDepenetrationOverlapPadding : bodyCastSkin);
            int count = Physics.OverlapCapsuleNonAlloc(
                pointA,
                pointB,
                overlapRadius,
                bodyOverlapBuffer,
                bodyBlockMask,
                QueryTriggerInteraction.Ignore);

            if (count <= 0)
                break;

            Vector3 totalCorrection = Vector3.zero;
            int correctionCount = 0;

            for (int i = 0; i < count; i++)
            {
                Collider other = bodyOverlapBuffer[i];
                bodyOverlapBuffer[i] = null;

                if (ShouldIgnoreAsBodyBlock(other))
                    continue;

                Vector3 correction;
                if (useRoundedBodySweep && useRoundedBodyForDepenetration)
                {
                    if (!TryGetRoundedBodyDepenetrationCorrection(resolved, other, out correction))
                        continue;
                }
                else
                {
                    bool overlapped = Physics.ComputePenetration(
                        ownCollider,
                        ownCollider.transform.position + (resolved - transform.position),
                        ownCollider.transform.rotation,
                        other,
                        other.transform.position,
                        other.transform.rotation,
                        out Vector3 direction,
                        out float distance);

                    if (!overlapped || distance <= 0f)
                        continue;

                    direction.y = 0f;
                    if (direction.sqrMagnitude < 0.0001f)
                        continue;

                    correction = direction.normalized * (distance + Mathf.Max(0f, bodyDepenetrationExtraPush));
                }

                correction.y = 0f;
                if (correction.sqrMagnitude < 0.000001f)
                    continue;

                totalCorrection += correction;
                correctionCount++;
            }

            if (correctionCount <= 0 || totalCorrection.sqrMagnitude < 0.000001f)
                break;

            totalCorrection /= correctionCount;
            float maxCorrection = Mathf.Max(0.001f, maxBodyDepenetrationPerStep);
            if (totalCorrection.magnitude > maxCorrection)
                totalCorrection = totalCorrection.normalized * maxCorrection;

            resolved += totalCorrection;

            Vector3 rescueNormal = totalCorrection;
            rescueNormal.y = 0f;
            if (rescueNormal.sqrMagnitude > 0.0001f)
            {
                bodyBlockedThisFrame = true;
                lastBlockingNormal = rescueNormal.normalized;
            }

            if (clearVelocityWhenBodyBlocked)
            {
                Vector3 n = totalCorrection;
                n.y = 0f;
                if (n.sqrMagnitude > 0.0001f)
                    RemoveVelocityIntoWall(n.normalized);
            }
        }

        return resolved;
    }

    private bool TryGetRoundedBodyDepenetrationCorrection(Vector3 bodyPosition, Collider other, out Vector3 correction)
    {
        correction = Vector3.zero;
        if (other == null)
            return false;

        if (!GetBodySweepCapsule(bodyPosition, out Vector3 pointA, out Vector3 pointB, out float radius, false))
            return false;

        Vector3 center = (pointA + pointB) * 0.5f;
        Vector3 closest = other.ClosestPoint(center);
        Vector3 away = center - closest;
        away.y = 0f;

        float desired = radius + Mathf.Max(0f, depenetrateOnlyRealOverlap ? bodyDepenetrationOverlapPadding : bodyCastSkin) + Mathf.Max(0f, bodyDepenetrationExtraPush);
        float dist = away.magnitude;

        if (dist > 0.0001f)
        {
            float push = desired - dist;
            if (push <= 0f)
                return false;

            correction = away.normalized * push;
            return true;
        }

        // ClosestPoint 在点位于 Collider 内部时可能返回自身，导致没有方向。
        // 这种情况下使用 Collider Bounds 中心给一个水平推出方向。对复杂 MeshCollider 不追求精确，只负责把角色从小钩子里拔出来。
        Vector3 fallback = center - other.bounds.center;
        fallback.y = 0f;
        if (fallback.sqrMagnitude < 0.0001f)
        {
            Vector3 hv = horizontalVelocity;
            hv.y = 0f;
            if (hv.sqrMagnitude > 0.0001f)
                fallback = -hv.normalized;
            else
                fallback = -transform.forward;
        }

        if (fallback.sqrMagnitude < 0.0001f)
            return false;

        correction = fallback.normalized * Mathf.Min(Mathf.Max(0.001f, maxBodyDepenetrationPerStep), desired);
        return true;
    }

    private bool GetBodySweepCapsule(Vector3 bodyPosition, out Vector3 pointA, out Vector3 pointB, out float radius, bool includeVisualClearance = true)
    {
        pointA = bodyPosition;
        pointB = bodyPosition;
        radius = 0.1f;

        if (ownCollider == null)
            return false;

        Bounds b = ownCollider.bounds;
        Vector3 centerOffset = b.center - transform.position;
        Vector3 center = bodyPosition + centerOffset;

        Vector3 extents = b.extents;
        float shrink = Mathf.Max(0f, bodyCastShrink);
        extents.x = Mathf.Max(0.01f, extents.x - shrink);
        extents.z = Mathf.Max(0.01f, extents.z - shrink);
        extents.y = Mathf.Max(0.02f, extents.y - shrink);

        float rawRadius = Mathf.Min(extents.x, extents.z) * Mathf.Clamp(roundedSweepRadiusScale, 0.4f, 1f);
        radius = Mathf.Max(minRoundedSweepRadius, rawRadius);
        if (includeVisualClearance && preserveBodyVisualClearance)
            radius += Mathf.Max(0f, bodySweepExtraClearance);
        radius = Mathf.Min(radius, Mathf.Max(0.01f, extents.y - 0.01f));

        float segmentHalf = Mathf.Max(0f, extents.y - radius);
        pointA = center + Vector3.up * segmentHalf;
        pointB = center - Vector3.up * segmentHalf;
        return true;
    }

    private bool CastBodyForHorizontalBlock(Vector3 bodyPosition, Vector3 direction, float distance, out RaycastHit bestHit)
    {
        bestHit = default;

        if (ownCollider == null)
            return false;

        Bounds b = ownCollider.bounds;
        Vector3 centerOffset = b.center - transform.position;
        Vector3 center = bodyPosition + centerOffset;

        Vector3 extents = b.extents;
        float shrink = Mathf.Max(0f, bodyCastShrink);
        extents.x = Mathf.Max(0.01f, extents.x - shrink);
        extents.z = Mathf.Max(0.01f, extents.z - shrink);
        extents.y = Mathf.Max(0.02f, extents.y - shrink);

        int hitCount;
        if (useRoundedBodySweep)
        {
            float rawRadius = Mathf.Min(extents.x, extents.z) * Mathf.Clamp(roundedSweepRadiusScale, 0.4f, 1f);
            float radius = Mathf.Max(minRoundedSweepRadius, rawRadius);
            if (preserveBodyVisualClearance)
                radius += Mathf.Max(0f, bodySweepExtraClearance);
            radius = Mathf.Min(radius, Mathf.Max(0.01f, extents.y - 0.01f));

            float segmentHalf = Mathf.Max(0f, extents.y - radius);
            Vector3 pointA = center + Vector3.up * segmentHalf;
            Vector3 pointB = center - Vector3.up * segmentHalf;

            // 这一个函数每次 FixedUpdate 会因为高速防钩/多次迭代被调用好几遍，之前用
            // CapsuleCastAll/BoxCastAll 每次都新分配一个数组——单位一多、跑得越快，
            // GC 压力越大。换成 NonAlloc 复用缓冲区（跟脚底地面探测那处是同一个思路）。
            hitCount = Physics.CapsuleCastNonAlloc(
                pointA,
                pointB,
                radius,
                direction,
                bodyCastBuffer,
                distance,
                bodyBlockMask,
                QueryTriggerInteraction.Ignore);

            if (drawDebugProbe)
            {
                Debug.DrawLine(pointA, pointA + direction * Mathf.Min(distance, 1.2f), Color.white, Time.fixedDeltaTime);
                Debug.DrawLine(pointB, pointB + direction * Mathf.Min(distance, 1.2f), Color.white, Time.fixedDeltaTime);
            }
        }
        else
        {
            if (preserveBodyVisualClearance)
            {
                float extra = Mathf.Max(0f, bodySweepExtraClearance);
                extents.x += extra;
                extents.z += extra;
            }

            hitCount = Physics.BoxCastNonAlloc(
                center,
                extents,
                direction,
                bodyCastBuffer,
                Quaternion.identity,
                distance,
                bodyBlockMask,
                QueryTriggerInteraction.Ignore);
        }

        bool found = false;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit h = bodyCastBuffer[i];
            if (ShouldIgnoreAsBodyBlock(h.collider))
                continue;

            if (ignoreInitialOverlappedBodyHits && h.distance <= 0.0001f && !treatZeroDistanceBodyHitAsBlock)
                continue;

            if (!IsBodyBlockingHit(h))
                continue;

            if (h.distance < bestDistance)
            {
                bestDistance = h.distance;
                bestHit = h;
                found = true;
            }
        }

        if (drawDebugProbe && found)
        {
            Debug.DrawRay(bestHit.point, bestHit.normal * 0.65f, Color.red, Time.fixedDeltaTime);
            Debug.DrawLine(center, center + direction * Mathf.Min(distance, 1.2f), Color.white, Time.fixedDeltaTime);
        }

        return found;
    }

    private bool IsBodyBlockingHit(RaycastHit hit)
    {
        if (ShouldIgnoreAsBodyBlock(hit.collider))
            return false;

        // 地面、可行走斜坡、Terrain 表面不应该成为“水平墙体”。
        // 只把接近垂直的侧面 / 物体边缘当作身体阻挡。
        if (hit.normal.y > maxBodyBlockNormalY)
            return false;

        return true;
    }


    private Vector3 ApplyUnitSoftSeparation(Vector3 candidate, float dt)
    {
        if (!enableUnitSoftSeparation || ownCollider == null)
            return candidate;

        // 自身静止时不参与分离——移动中的单位应绕开静止单位，而不是把它推走
        if (!HasExternalMoveInput)
            return candidate;

        if (!applyUnitSeparationWhileAirborne && state == MotorState.Falling)
            return candidate;

        int queryMask = unitSeparationMask.value;
        if (queryMask == 0)
        {
            if (!identifyUnitByTerrainGroundMotor)
                return candidate;

            // 兜底：没有设置单位层时，只在挂有 Motor 的对象中筛选。场景单位数量通常很少，这里只作为防漏保险。
            queryMask = ~0;
        }

        if (!GetBodySweepCapsule(candidate, out Vector3 pointA, out Vector3 pointB, out float radius, false))
            return candidate;

        float queryRadius = radius + Mathf.Max(0f, unitSeparationPadding);
        int count = Physics.OverlapCapsuleNonAlloc(
            pointA,
            pointB,
            queryRadius,
            bodyOverlapBuffer,
            queryMask,
            QueryTriggerInteraction.Ignore);

        if (count <= 0)
            return candidate;

        Vector3 center = (pointA + pointB) * 0.5f;
        Vector3 totalPush = Vector3.zero;
        int pushCount = 0;

        for (int i = 0; i < count; i++)
        {
            Collider other = bodyOverlapBuffer[i];
            bodyOverlapBuffer[i] = null;

            if (other == null || IsSelfCollider(other) || other.isTrigger)
                continue;

            if (!IsUnitCollider(other))
                continue;

            // 静止单位不应被移动单位挤走
            TerrainGroundMotorV5 otherMotor =
                other.GetComponent<TerrainGroundMotorV5>()
             ?? other.GetComponentInParent<TerrainGroundMotorV5>(true)
             ?? (other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<TerrainGroundMotorV5>() : null);
            if (otherMotor != null && !otherMotor.HasExternalMoveInput)
                continue;

            Vector3 closest = other.ClosestPoint(center);
            Vector3 away = center - closest;
            away.y = 0f;

            if (away.sqrMagnitude < 0.0001f)
            {
                Transform otherRoot = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform.root;
                away = candidate - otherRoot.position;
                away.y = 0f;
            }

            if (away.sqrMagnitude < 0.0001f)
            {
                Vector3 hv = horizontalVelocity;
                hv.y = 0f;
                away = hv.sqrMagnitude > 0.0001f ? -hv : -transform.forward;
                away.y = 0f;
            }

            if (away.sqrMagnitude < 0.0001f)
                continue;

            float distance = away.magnitude;
            float desiredDistance = queryRadius;
            float penetration = Mathf.Max(0f, desiredDistance - distance);
            if (penetration <= 0f)
                continue;

            totalPush += away.normalized * penetration;
            pushCount++;
        }

        if (pushCount <= 0 || totalPush.sqrMagnitude < 0.000001f)
            return candidate;

        totalPush /= pushCount;
        totalPush.y = 0f;

        float maxPush = Mathf.Min(
            Mathf.Max(0.001f, unitSeparationMaxPushPerFrame),
            Mathf.Max(0.001f, unitSeparationStrength) * Mathf.Max(0.001f, dt));

        if (totalPush.magnitude > maxPush)
            totalPush = totalPush.normalized * maxPush;

        Vector3 separated = candidate + totalPush;

        if (drawDebugProbe)
            Debug.DrawLine(candidate + Vector3.up * 0.25f, separated + Vector3.up * 0.25f, Color.cyan, Time.fixedDeltaTime);

        return separated;
    }

    private Vector3 ResolveUnitSeparationBodyBlock(Vector3 beforeSeparation, Vector3 separatedCandidate)
    {
        Vector3 delta = separatedCandidate - beforeSeparation;
        delta.y = 0f;

        if (delta.sqrMagnitude < 0.000001f)
            return separatedCandidate;

        Vector3 resolved = ResolveHorizontalBodyCollision(beforeSeparation, separatedCandidate);

        if (!clampUnitSeparationAgainstBodyBlock)
            return resolved;

        Vector3 resolvedDelta = resolved - beforeSeparation;
        resolvedDelta.y = 0f;

        // If the second body-block solve removed most of the separation, keep the safe side instead of
        // letting tiny numerical overlap accumulate into the wall. This is intentionally conservative.
        if (Vector3.Dot(resolvedDelta, delta) <= 0.00001f)
            return beforeSeparation;

        return resolved;
    }

    private void MoveTo(Vector3 targetPosition)
    {
        if (rb != null)
            rb.MovePosition(targetPosition);
        else
            transform.position = targetPosition;
    }

    private void DrawState(Vector3 position, GroundHit ground, MotorState motorState)
    {
        if (drawDebugProbe)
        {
            Vector3 foot = position + GetFootOffset();
            Vector3 origin = new Vector3(foot.x, foot.y + probeHeight, foot.z);
            Color c = Color.red;
            if (motorState == MotorState.Grounded) c = Color.green;
            else if (motorState == MotorState.Sliding) c = Color.yellow;

            Debug.DrawLine(origin, origin + Vector3.down * (probeHeight + probeDistance), c, Time.fixedDeltaTime);
            if (ground.valid)
            {
                Debug.DrawRay(ground.point, ground.normal * 0.6f, Color.cyan, Time.fixedDeltaTime);

                if (motorState == MotorState.Sliding)
                {
                    Vector3 slideDirection = Vector3.ProjectOnPlane(Vector3.down, ground.normal);
                    if (slideDirection.sqrMagnitude > 0.0001f)
                        Debug.DrawRay(ground.point + Vector3.up * 0.1f, slideDirection.normalized * 0.8f, Color.magenta, Time.fixedDeltaTime);
                }
            }
        }

        if (drawDebugState)
        {
            Vector3 markerBase = position + Vector3.up * 2.2f;
            Color stateColor = Color.red;
            if (motorState == MotorState.Grounded) stateColor = Color.green;
            else if (motorState == MotorState.Sliding) stateColor = Color.yellow;
            Debug.DrawLine(markerBase, markerBase + Vector3.up * 0.8f, stateColor, Time.fixedDeltaTime);
        }
    }
}
