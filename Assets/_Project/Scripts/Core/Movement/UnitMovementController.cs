using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
[DefaultExecutionOrder(10000)]
[RequireComponent(typeof(Rigidbody))]
public class UnitMovementController : MonoBehaviour
{
    public enum MovementInputMode
    {
        PlayerInput,
        External,
        Disabled
    }

    public enum MovementApplyMode
    {
        LegacyFlatMovement,
        ExternalTerrainMotor
    }

    public enum UnitGroundFollowMode
    {
        LockY,
        RaycastGround,
        PhysicsGravity
    }

    public enum BurdenLevel
    {
        Light,
        Medium,
        Heavy,
        Overweight
    }

    public enum JumpRuntimeState
    {
        None,
        Start,
        Air,
        Land
    }

    public enum DodgeRuntimeState
    {
        None,
        Forward,
        Back
    }

    [System.Serializable]
    public struct BurdenRuntimeData
    {
        public BurdenLevel level;
        public float walkPenalty;
        public float runPenalty;
        public float sharpnessMultiplier;
        public bool canRun;
        public bool canDodge;
        public bool canJump;
        public float jumpHeightMultiplier;
    }

    [Header("Input")]
    [Tooltip("PlayerInput：直接读取键盘输入。External：由玩家输入适配器或 AI 写入移动意图。Disabled：不读取输入。")]
    [SerializeField] private MovementInputMode inputMode = MovementInputMode.PlayerInput;
    [Tooltip("开发期输入设置。留空时会自动读取 Assets/_Project/Data/Settings/SkyPrisonInputSettings.asset。")]
    [SerializeField] private SkyPrisonInputSettings inputSettings;
    [SerializeField] private bool useSkyPrisonInputSettings = true;
    [SerializeField] private bool externalRunHeld = false;
    [SerializeField] private bool externalSneakHeld = false;

    [Header("Formal Action Runtime")]
    [SerializeField] private UnitLoadRuntime loadRuntime;
    [SerializeField] private bool autoFindLoadRuntime = true;
    [SerializeField] private UnitActionModuleRuntime actionModuleRuntime;
    [SerializeField] private bool autoFindActionModuleRuntime = true;

    [Header("Movement Apply Mode")]
    [Tooltip("LegacyFlatMovement：使用本脚本原有平面移动。ExternalTerrainMotor：本脚本只读取输入/状态/动画，不再写入 Transform/Rigidbody，实际位移交给 TerrainGroundMotorV5。")]
    [SerializeField] private MovementApplyMode movementApplyMode = MovementApplyMode.ExternalTerrainMotor;
    [SerializeField] private TerrainGroundMotorV5 externalTerrainMotor;
    [Tooltip("开启后，如果同物体上存在 TerrainGroundMotorV5，会自动切到 ExternalTerrainMotor，避免旧平面移动和 Terrain Motor 抢 Transform。") ]
    [SerializeField] private bool autoSwitchToExternalTerrainMotorWhenAvailable = true;
    [SerializeField] private bool autoFindExternalTerrainMotor = true;
    [Tooltip("ExternalTerrainMotor 模式下如果当前单位没有 TerrainGroundMotorV5，自动退回本脚本的 RaycastGround 平面移动，避免动画在跑但 Transform 不移动。正式链路稳定后建议关闭。") ]
    [SerializeField] private bool fallbackToLegacyFlatMovementWhenExternalTerrainMotorMissing = true;
    [Tooltip("ExternalTerrainMotor 模式下强制接管 TerrainGroundMotor 输入权，关闭其自身 Unity Input / Sprint Key，避免两套移动输入互相摩擦。") ]
    [SerializeField] private bool enforceExternalTerrainMotorInputOwnership = true;
    [Tooltip("调试显示：当前 TerrainGroundMotor 主权绑定状态。") ]
    [SerializeField] private string externalTerrainMotorOwnershipRuntime = "None";

    [Header("Action Input Interface")]
    [Tooltip("只作为开发期兜底输入。正式键位建议由输入适配器调用 RequestJump / RequestDodge / SetExternalMoveInput。")]
    [SerializeField] private bool enablePlayerFallbackActionKeys = true;
    [SerializeField] private KeyCode fallbackJumpKey = KeyCode.Space;
    [SerializeField] private KeyCode fallbackDodgeKey = KeyCode.LeftAlt;

    [Header("Action Runtime")]
    [Tooltip("跳跃起跳段。后续真实空中/落地判定接入后，会从 Start 切到 Air，再由 NotifyJumpLand 落地。")]
    [SerializeField] private string jumpStartAnimationKey = "Jump_Start";
    [SerializeField] private string jumpAirAnimationKey = "Jump_Air";
    [SerializeField] private string jumpLandAnimationKey = "Jump_Land";
    [SerializeField] private float jumpStartLockSeconds = 0.12f;
    [Tooltip("起跳动画最短保留时间。用于保证 Jump_1 里的 Spine Event（例如 Jumpstep_up）有足够时间触发。不会降低原本 jumpStartLockSeconds，只会作为下限。")]
    [SerializeField] private float jumpStartEventHoldSeconds = 0.22f;
    [SerializeField] private float jumpAirMinSeconds = 0.16f;
    [SerializeField] private float jumpLandLockSeconds = 0.16f;
    [Tooltip("落地动画最短保留时间。用于保证 Jump_2 里的 Spine Event（例如 Jumpstep_down）有足够时间触发。不会降低原本 jumpLandLockSeconds，只会作为下限。")]
    [SerializeField] private float jumpLandEventHoldSeconds = 0.22f;
    [Tooltip("当前先用计时自动落地。等接入真实地面检测/空中高度后，可关闭并由 NotifyJumpLand 触发落地段。开启 3D 跳跃物理时，会优先用高度/重力落地。")]
    [SerializeField] private bool autoLandJumpByTimer = true;
    [Tooltip("跳跃输入缓冲。移动/奔跑时 TerrainMotor 的落地状态可能有 1~数帧波动，按键不应该因为这一帧未通过 RequestJump 就被直接吃掉。")]
    [SerializeField] private float jumpInputBufferSeconds = 0.16f;

    [Header("Jump Launch Sync")]
    [Tooltip("开启后，跳跃按键先进入 Jump Start 动画，延迟一点点后才真正触发 3D 跳跃冲量，避免双脚还在起跳准备动作时角色已经离地。")]
    [SerializeField] private bool delayJumpPhysicsUntilLaunchWindow = true;
    [Tooltip("从 Jump Start 开始到真正起跳冲量之间的延迟。建议对齐 Spine 里的 Jumpstep_up 事件帧，一般 0.06~0.14 秒。")]
    [SerializeField] private float jumpLaunchPhysicsDelaySeconds = 0.10f;
    [Tooltip("如果延迟到点时 TerrainGroundMotorV5 拒绝起跳，是否取消这次跳跃状态，避免角色卡在 Jump Start/Air。")]
    [SerializeField] private bool cancelJumpIfDelayedLaunchRejected = true;
    [SerializeField] private bool currentJumpPhysicsLaunchPendingRuntime = false;
    [SerializeField] private float currentJumpPhysicsLaunchDelayRemainingRuntime = 0f;
    [SerializeField] private string lastJumpPhysicsLaunchRuntime = "None";

    [Header("Jump 3D Physics")]
    [Tooltip("开启后，跳跃会推动单位根节点在世界 Y 轴上抛物线跳起，并按 Jump Gravity 下落。移动脚本仍保持 Kinematic 控制，避免 Rigidbody 与横版移动互相抢控制。")]
    [SerializeField] private bool driveJumpBy3DPhysics = true;
    [Tooltip("单位定义写入的跳跃高度，单位：Unity 世界单位。")]
    [SerializeField] private float jumpHeight = 1.4f;
    [Tooltip("脚本控制的跳跃重力。数值越大，起落越快。")]
    [SerializeField] private float jumpGravity = 18f;
    [Tooltip("空中水平操控倍率。1 表示空中和地面一样灵敏，0 表示空中不能改变水平速度。")]
    [Range(0f, 1f)]
    [SerializeField] private float airControlMultiplier = 0.85f;

    [Header("Jump Burden Correction")]
    [Tooltip("轻负重跳跃高度倍率。一般保持 1。")]
    [SerializeField] private float lightBurdenJumpHeightMultiplier = 1.0f;
    [Tooltip("中负重跳跃高度倍率。负重越高会在轻/中/重区间内继续插值。")]
    [SerializeField] private float mediumBurdenJumpHeightMultiplier = 0.88f;
    [Tooltip("重负重跳跃高度倍率。超过重负重阈值后进入超重，不能跳跃。")]
    [SerializeField] private float heavyBurdenJumpHeightMultiplier = 0.62f;
    [Tooltip("超重是否禁止跳跃。建议开启，和超重不能跑步保持一致。")]
    [SerializeField] private bool overweightDisablesJump = true;

    [Header("Ground Shadow During Jump")]
    [Tooltip("脚底投影节点。留空时会自动查找名为 ShadowRoot 的子节点。跳跃时该节点会被压回地面，而不是跟着单位根节点一起升高。")]
    [SerializeField] private Transform groundShadowRoot;
    [SerializeField] private bool keepGroundShadowOnGroundDuringJump = true;
    [Tooltip("跳得越高，地面投影越小。只缩放 X/Z，不改变 Y。")]
    [SerializeField] private bool scaleGroundShadowByJumpHeight = true;
    [Range(0.1f, 1f)]
    [SerializeField] private float groundShadowScaleAtMaxJumpHeight = 0.72f;
    [Tooltip("用于计算投影缩放的最大高度。0 表示使用当前 Jump Height。")]
    [SerializeField] private float groundShadowScaleReferenceHeight = 0f;

    [Tooltip("闪避向前 / 向后动画。没有拆分资源时会回退到 Dodge Animation Key。")]
    [SerializeField] private string dodgeForwardAnimationKey = "dodge_forward";
    [SerializeField] private string dodgeBackAnimationKey = "dodge_back";
    [SerializeField] private float dodgeLockSeconds = 0.5f;
    [Tooltip("闪避起手瞬间、动画真实时长还没算出来之前用的极短兜底锁定值——真正生效的锁定" +
             "时长由 ExtendDodgeLockDuration 按动画真实时长决定（只会延长不会缩短），这个值" +
             "只是给那次延长发生之前的极短空档兜底，不能跟 dodgeLockSeconds 混用：后者还要" +
             "拿去算闪避位移速度/加速度（距离÷dodgeLockSeconds），调小了会让闪避速度算出" +
             "离谱的数值。之前直接用 dodgeLockSeconds(0.5s) 当初始锁定，如果闪避动画本身" +
             "更短（比如0.6s/1.3倍速≈0.46s），会多锁住约40ms——动画已经放完了却还不能操作，" +
             "是「闪避后卡手」的来源之一。")]
    [SerializeField] private float dodgeInputUnlockFallbackSeconds = 0.05f;
    [SerializeField] private bool driveDodgeMovement = true;
    [SerializeField] private float dodgeSpeed = 7.5f;

    [Header("Dodge Distance / External Terrain Motor")]
    [Tooltip("ExternalTerrainMotor 模式下，闪避按目标距离反推速度，而不是只把 sprintSpeed 当成闪避速度。这样不会因为闪避锁定时间短、TerrainMotor 加速慢而显得位移过近。") ]
    [SerializeField] private bool useDodgeDistanceForExternalTerrainMotor = true;
    [Tooltip("向前/侧向闪避希望达到的水平距离，单位：Unity 世界单位。") ]
    [SerializeField] private float dodgeDistance = 2.2f;
    [Tooltip("后撤闪避希望达到的水平距离。通常略短于前闪。") ]
    [SerializeField] private float dodgeBackDistance = 1.65f;
    [Tooltip("闪避期间给 TerrainGroundMotor 的速度倍率。正常保持 1。") ]
    [SerializeField] private float externalTerrainDodgeSpeedMultiplier = 1.0f;
    [Tooltip("闪避期间临时提高 TerrainGroundMotor.acceleration，避免 0.22 秒闪避还没加速到目标速度就结束，看起来像被取消。") ]
    [SerializeField] private bool boostExternalTerrainMotorAccelerationDuringDodge = true;
    [Tooltip("闪避期间 TerrainGroundMotor.acceleration 至少提升到 dodgeSpeed / dodgeLockSeconds * 该倍率。") ]
    [SerializeField] private float externalTerrainDodgeAccelerationMultiplier = 1.35f;

    [Header("Animation")]
    [Tooltip("是否由移动状态自动驱动待机 / 行走 / 奔跑动画。")]
    [SerializeField] private bool driveMovementAnimation = true;
    [Tooltip("正式动作结构：开启后，UnitMovementController 不再直接播放 Spine/Animator 动画；动画由 UnitActionController + SpineAnimationDriver_Current 统一驱动。")]
    [SerializeField] private bool actionControllerOwnsAnimation = true;
    [Tooltip("3D 通道使用 Animator；没有 Animator 时，会尝试通过反射调用 Spine SkeletonAnimation。")]
    [SerializeField] private Animator targetAnimator;
    [Tooltip("Spine SkeletonAnimation 组件引用。没有直接类型依赖，避免项目未导入 Spine 时编译失败。")]
    [SerializeField] private Component spineAnimationComponent;
    [Tooltip("AI/换装/外描边结构里可能存在多个 SkeletonAnimation。开启后，移动动画会同步写入当前单位子树下所有 Spine 动画组件，避免自动引用到错误代理导致实际角色仍停在 move。")]
    [SerializeField] private bool driveAllChildSpineAnimations = true;
    [Tooltip("在 LateUpdate 末尾再次校验实际 Spine 动画。用于防止其它脚本/初始化流程把 run_heavy 覆盖回 move。")]
    [SerializeField] private bool enforceMovementAnimationLateUpdate = false;
    [SerializeField] private string idleAnimationKey = "idle";
    [SerializeField] private string walkAnimationKey = "walk";
    [SerializeField] private string runAnimationKey = "run";
    [SerializeField] private string sneakAnimationKey = "sneak";
    [SerializeField] private string jumpAnimationKey = "jump";
    [SerializeField] private string attackAnimationKey = "attack";
    [SerializeField] private string hitAnimationKey = "hit";
    [SerializeField] private string dodgeAnimationKey = "dodge";
    [SerializeField] private string deathAnimationKey = "die";
    [SerializeField] private float movementAnimationFade = 0.08f;
    [SerializeField] private float oneShotAnimationLockSeconds = 0.18f;

    [Header("Move")]
    [SerializeField] private UnitMovementType movementType = UnitMovementType.Walk;
    [SerializeField] private float walkSpeed = 3.6f;
    [SerializeField] private float sneakSpeed = 0.3f;
    [SerializeField] private float runSpeed = 5.2f;
    [SerializeField] private float velocitySharpness = 45f;
    [SerializeField] private float minWalkSpeed = 1.2f;

    [Header("Movement Feel")]
    [Range(0.8f, 1f)]
    [SerializeField] private float sustainedMoveSpeedMultiplier = 0.93f;
    [SerializeField] private float sustainedMoveDelay = 0.12f;

    [Header("Collision Solve")]
    [SerializeField] private CapsuleCollider movementCapsule;
    [SerializeField] private LayerMask blockingLayers = ~0;
    [SerializeField] private float shellOffset = 0.02f;
    [SerializeField] private float wallSlideFactor = 1f;
    [Tooltip("水平移动/穿透修正时忽略 BaseGroundBlock 自己的地面碰撞体。地面高度由 GroundQuery/Raycast 管，不能让地面 Collider 参与横向阻挡，否则角色贴 ShapeMask/地图边缘时会被夹住，只能靠跳跃脱离。")]
    [SerializeField] private bool ignoreBaseGroundBlockCollidersInBlockingSolve = true;
    [Tooltip("TerrainCollider 是地面高度来源，不应该参与水平 CapsuleCast / 穿透修正；否则角色会被地面当成墙挡住，表现为原地跑。")]
    [SerializeField] private bool ignoreTerrainCollidersInBlockingSolve = true;
    [Tooltip("可推道具(SkyPrisonPushablePropRuntime，如锥桶)走推开物理，不应被当成墙硬挡；否则胶囊蹭它的 Convex Mesh 边时会被勾住、和推开物理互相较劲。")]
    [SerializeField] private bool ignorePushablePropCollidersInBlockingSolve = true;
    [SerializeField] private bool drawDebug = false;
    [Tooltip("移动后检查自身实体胶囊是否已经挤入其它阻挡碰撞体，并把单位推出。用于解决持续顶住其它单位后突破的问题。")]
    [SerializeField] private bool solvePenetrationAfterMove = true;
    [SerializeField] private int penetrationSolveIterations = 2;
    [SerializeField] private float maxPenetrationCorrectionPerFrame = 0.35f;
    [Header("Collision Solve / Soft Anti-Bounce")]
    [Tooltip("商业版柔性退穿透：把 ComputePenetration 的推出量当作纠偏，不当作移动速度，避免撞墙时镜头/敌人抖动。")]
    [SerializeField] private bool useSoftPenetrationCorrection = true;
    [Tooltip("每帧退穿透的软上限。旧值 0.35 对角色来说太像被墙弹出去；建议 0.04~0.10。")]
    [SerializeField] private float softMaxPenetrationCorrectionPerFrame = 0.07f;
    [Tooltip("退穿透强度。1=按物理算满；0.35 左右会明显减少撞墙弹力。")]
    [Range(0.05f, 1f)]
    [SerializeField] private float penetrationCorrectionStrength = 0.35f;
    [Tooltip("ComputePenetration 额外推出安全边。旧逻辑用 shellOffset 会过推；这里默认只给极小安全边。")]
    [SerializeField] private float penetrationExtraPush = 0.002f;
    [Tooltip("多个碰撞体同时重叠时，把总推出量平均，避免角落/墙边被多块碰撞体叠加弹飞。")]
    [SerializeField] private bool averageMultiplePenetrationCorrections = true;
    [Tooltip("退穿透只改位置，不回写 currentVelocity。否则摄像机会把退穿透当成角色速度，造成撞墙晃动。")]
    [SerializeField] private bool ignorePenetrationCorrectionWhenUpdatingVelocity = true;

    [Header("Burden Runtime")]
    [Tooltip("测试模式下，直接使用下方测试负重和测试上限。")]
    [SerializeField] private bool useTestBurden = false;

    [Tooltip("当前总负重。正式运行时由背包/装备/道具总和写入。")]
    [SerializeField] private float currentBurden = 0f;

    [Tooltip("当前负重上限。正式运行时可由角色属性/科技树/装备被动等写入。")]
    [SerializeField] private float maxBurden = 120f;

    [Header("Test Values")]
    [SerializeField] private float testCurrentBurden = 0f;
    [SerializeField] private float testMaxBurden = 120f;

    [Header("Burden Thresholds (%)")]
    [Range(0f, 1f)]
    [SerializeField] private float lightUpperRatio = 0.30f;

    [Range(0f, 1f)]
    [SerializeField] private float mediumUpperRatio = 0.60f;

    [Range(0f, 1f)]
    [SerializeField] private float heavyUpperRatio = 0.85f;

    [Header("Ground / 3D Height Follow")]
    [Tooltip("LockY：旧版锁定初始高度。RaycastGround：用真实 3D 地面高度。PhysicsGravity：交给刚体重力/物理。")]
    [SerializeField] private UnitGroundFollowMode groundFollowMode = UnitGroundFollowMode.RaycastGround;
    [SerializeField] private bool lockYToStartPosition = false; // Terrain Motor 兼容默认值：不要锁死 Y；旧版 LockY 需要时可手动打开
    [SerializeField] private float lockedY = 0f;
    [Tooltip("只勾选真正的地面层。不要包含 Unit/Character/Player/Enemy/Trigger/Occluder 层。")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float groundRayStartHeight = 3f;
    [SerializeField] private float groundRayDistance = 8f;
    [SerializeField] private float groundOffset = 0f;
    [SerializeField] private float maxGroundSlopeAngle = 55f;
    [SerializeField] private bool rejectTooSteepSlope = true;
    [SerializeField] private bool keepHeightWhenNoGround = true;

    [Header("Ground Query Service")]
    [Tooltip("优先使用统一地面查询服务。用于读取 GroundShapeMask / SurfaceMaterialMap / 模型 GroundSurfaceMarker。")]
    [SerializeField] private bool useGroundQueryService = true;
    [SerializeField] private GroundQueryService groundQueryService;
    [Tooltip("没有找到 GroundQueryService 时自动在 WorldRoot 下创建一个。")]
    [SerializeField] private bool autoCreateGroundQueryService = true;

    [Header("Map Boundary Constraint")]
    [Tooltip("限制单位根节点不能移出 BaseGroundBlock / MapBounds 的 XZ 数据边界。注意：这只是地图边界约束，不等于 ShapeMask 无地面坠落规则。")]
    [SerializeField] private bool constrainToGroundBlockBounds = true;
    [SerializeField] private BaseGroundBlock boundaryGroundBlock;
    [SerializeField] private bool autoFindBoundaryGroundBlock = true;
    [SerializeField] private float boundaryPadding = 0.05f;
    [Tooltip("边界约束是否把移动胶囊半径也算进去。开启后不会让单位半个身体越出地图边界，能避免被边界来回夹住。")]
    [SerializeField] private bool accountCapsuleRadiusInBoundary = true;
    [Tooltip("边界内缩安全边。用于避免刚好贴边时被碰撞解算推出又被拉回。建议 0.02~0.08。")]
    [SerializeField] private float boundarySkinWidth = 0.03f;
    [SerializeField] private bool stopVelocityWhenBoundaryClamped = true;



    [Header("Ground Shape Constraint")]
    [Tooltip("限制单位不能从 GroundShapeMask 有地面区域直接走进无地面区域。若已经被推出到无地面区域，则允许输入把它带回有地面区域，避免卡死在边界。")]
    [SerializeField] private bool constrainToExistingGroundShape = true;
    [Tooltip("单位已经处在无地面区域时，允许继续移动以便回到有地面区域。后续接坠落死亡后，可按状态关闭。")]
    [SerializeField] private bool allowEscapeWhenAlreadyOutsideGround = true;
    [Tooltip("目标点落在无地面区域时，尝试只保留 X 或 Z 方向的切线移动，避免贴着地面边缘时被完全锁死。")]
    [SerializeField] private bool slideAlongGroundShapeEdge = true;

    [Header("Physics Runtime")]
    [SerializeField] private bool runtimeUseGravity = false;
    [SerializeField] private bool runtimeIsKinematic = false;
    [SerializeField] private RigidbodyInterpolation runtimeInterpolation = RigidbodyInterpolation.Interpolate;
    [SerializeField] private CollisionDetectionMode runtimeCollisionDetection = CollisionDetectionMode.ContinuousDynamic;
    [SerializeField]
    private RigidbodyConstraints runtimeConstraints =
        RigidbodyConstraints.FreezePositionY |
        RigidbodyConstraints.FreezeRotationX |
        RigidbodyConstraints.FreezeRotationY |
        RigidbodyConstraints.FreezeRotationZ;

    [Header("Debug (Runtime)")]
    [SerializeField] private BurdenLevel currentBurdenLevel = BurdenLevel.Light;
    [SerializeField] private bool canRunNow = true;
    [SerializeField] private bool canDodgeNow = true;
    [SerializeField] private bool canJumpNow = true;
    [SerializeField] private bool canMoveNow = true;
    [SerializeField] private JumpRuntimeState currentJumpState = JumpRuntimeState.None;
    [SerializeField] private DodgeRuntimeState currentDodgeState = DodgeRuntimeState.None;
    [SerializeField] private Vector2 currentDodgeDirection = Vector2.zero;
    [SerializeField] private float currentActionLockedUntil = 0f;
    [SerializeField] private float currentEffectiveWalkSpeed = 0f;
    [SerializeField] private float currentEffectiveSneakSpeed = 0f;
    [SerializeField] private float currentEffectiveRunSpeed = 0f;
    [SerializeField] private bool currentIsGrounded = true;
    [SerializeField] private float currentGroundYRuntime = 0f;
    [SerializeField] private bool currentGroundHasGround = true;
    [SerializeField] private GroundSurfaceType currentGroundSurfaceTypeRuntime = GroundSurfaceType.Default;
    [SerializeField] private GroundSurfaceMaterialDefinition currentGroundSurfaceMaterialRuntime;
    [SerializeField] private bool currentGroundIsFallDeathArea = false;
    [SerializeField] private string currentGroundSourceRuntime = "-";
    [SerializeField] private bool currentWasClampedByMapBoundary = false;
    [SerializeField] private string currentMapBoundarySourceRuntime = "-";
    [SerializeField] private float currentBaseJumpHeightRuntime = 0f;
    [SerializeField] private float currentEffectiveJumpHeightRuntime = 0f;
    [SerializeField] private float currentJumpHeightMultiplierRuntime = 1f;
    [SerializeField] private float currentJumpHeightRuntime = 0f;
    [SerializeField] private float currentJumpVerticalVelocityRuntime = 0f;
    [SerializeField] private bool currentWantsRun = false;
    [SerializeField] private bool currentWantsSneak = false;
    [SerializeField] private float currentChosenMoveSpeed = 0f;
    [SerializeField] private string currentAnimationKeyRuntime = "";
    [SerializeField] private string currentActualSpineAnimationRuntime = "";
    [SerializeField] private string lastAnimationRequest = "";
    [SerializeField] private bool lastAnimationPlaySucceeded = false;
    [SerializeField] private float currentEffectiveSharpness = 0f;
    [SerializeField] private Vector2 currentInput = Vector2.zero;
    [SerializeField] private Vector3 currentVelocity = Vector3.zero;
    [SerializeField] private float currentBurdenPercent01 = 0f;
    [SerializeField] private float currentBurdenPercent100 = 0f;
    [SerializeField] private float currentBurdenValueRuntime = 0f;
    [SerializeField] private float maxBurdenValueRuntime = 0f;

    private Rigidbody rb;
    private Vector2 input;
    private float sameDirectionTimer = 0f;
    private Vector2 lastNonZeroInput = Vector2.zero;
    private bool jumpQueued = false;
    private float jumpQueuedUntil = -999f;
    private bool dodgeQueued = false;
    private DodgeRuntimeState queuedDodgeState = DodgeRuntimeState.Forward;
    private Vector2 queuedDodgeDirection = Vector2.zero;
    // 特殊调用方(比如攻击取消后撤步)可以传一个比1小的倍率，让这一次闪避比正常闪避
    // 短——ResolveExternalTerrainDodgeSpeed 算出来的速度是"至少要多快才能覆盖配置
    // 距离"的下限，光调 dodgeDistance/dodgeBackDistance 通常压不下去(dodgeSpeed这个
    // 基础地板值本身就比它们换算出来的速度高)，必须在最终速度上再乘一次才有效。
    // 1=正常闪避（默认），队列/激活两份是因为闪避请求先入队、下一帧才真正开始。
    private float queuedDodgeSpeedScale = 1f;
    private float activeDodgeSpeedScale = 1f;
    private JumpRuntimeState jumpState = JumpRuntimeState.None;
    private DodgeRuntimeState dodgeState = DodgeRuntimeState.None;
    private bool jumpPhysicsLaunchPending = false;
    private float jumpPhysicsLaunchAt = -999f;
    private bool jumpPhysicsLaunched = false;
    private Vector2 dodgeDirection = Vector2.zero;
    private float dodgeStartTime = -999f;

    // 蓄力攻击释放时的冲刺——跟闪避是完全独立的一套状态，不复用dodgeState，因为闪避
    // 本身是一个专门的UnitActionState(带无敌判定/动画切换)，蓄力冲刺发生在Attack状态
    // 内部，还要保留攻击判定(hit_start/hit_end)正常生效，两者不能混。
    private bool chargeDashActive = false;
    private Vector2 chargeDashDirection = Vector2.zero;
    private float chargeDashUntil = 0f;
    private float chargeDashSpeed = 0f;

    private float actionStateUntil = 0f;
    private float jumpVerticalVelocity = 0f;
    private float jumpHeightOffset = 0f;
    // 空中攻击悬停用——冻结期间跳跃的重力/下落物理完全不推进，高度定在冻结那一刻的
    // 值上，直到解除才继续算。
    private bool jumpVerticalPhysicsFrozen = false;
    private float jumpStartedAt = -999f;
    private float sampledGroundY = 0f;
    private bool sampledGroundValid = false;
    private bool isGrounded = true;
    private bool runtimePhysicsInitialized = false;
    private bool groundShadowDefaultsCached = false;
    private Vector3 groundShadowOriginalLocalPosition = Vector3.zero;
    private Vector3 groundShadowOriginalLocalScale = Vector3.one;

    // Ground shadow must use the authored ground contact as its vertical anchor.
    // In ExternalTerrainMotor mode the unit root itself moves upward during jump, so relying on
    // jumpHeightOffset is not authoritative. Cache the visible shadow world Y while grounded, then
    // keep the shadow at that world Y while the terrain motor is airborne/jumping.
    private bool groundShadowWorldAnchorCached = false;
    private float groundShadowGroundWorldY = 0f;
    private float groundShadowGroundRootWorldY = 0f;
    private Vector3 cachedCapsuleCenterFromRoot = Vector3.zero;
    private string currentAnimationKey = "";
    private float oneShotAnimationLockedUntil = 0f;
    private readonly Collider[] penetrationBuffer = new Collider[32];
    private readonly RaycastHit[] movementCastBuffer = new RaycastHit[32];
    private readonly RaycastHit[] groundHitBuffer = new RaycastHit[24];
    private readonly List<Component> spineAnimationTargets = new List<Component>(8);

    private TerrainGroundMotorV5 cachedExternalDodgeMotor;
    private bool cachedExternalDodgeMotorAccelerationValid = false;
    private float cachedExternalDodgeMotorAcceleration = 40f;

    // V2 performance cache: movement animation must not scan/reflect every Update/LateUpdate.
    private bool spineAnimationTargetsInitialized = false;
    private bool spineAnimationTargetsDirty = true;
    private readonly Dictionary<System.Type, bool> spineComponentTypeCache = new Dictionary<System.Type, bool>();
    private readonly Dictionary<System.Type, MethodInfo> spineSetAnimationMethodCache = new Dictionary<System.Type, MethodInfo>();
    private readonly object[] spineSetAnimationArgs = new object[3];

    public float CurrentBurden => useTestBurden ? testCurrentBurden : currentBurden;
    public float MaxBurden => Mathf.Max(1f, useTestBurden ? testMaxBurden : maxBurden);
    public float BurdenPercent01 => Mathf.Clamp01(CurrentBurden / MaxBurden);
    public float BurdenPercent100 => BurdenPercent01 * 100f;
    public float EffectiveJumpHeight => Mathf.Max(0.05f, jumpHeight) * Mathf.Max(0f, EvaluateBurden(CurrentBurden, MaxBurden).jumpHeightMultiplier);

    public BurdenLevel CurrentBurdenLevel => currentBurdenLevel;
    public bool CanRun => canRunNow;
    public bool CanDodge => canDodgeNow;
    public bool CanJump => canJumpNow;
    public bool CanMove => canMoveNow;
    public Vector2 MoveInput => input;
    /// <summary>
    /// 用于视觉朝向判断的移动方向。优先使用当前输入；如果输入被清空但 Rigidbody 仍有本帧速度，则用速度兜底。
    /// X 对应世界 X，Y 对应世界 Z。
    /// </summary>
    private Vector2 _facingOverride;
    private bool    _hasFacingOverride;

    public void SetFacingOverride(Vector2 dir) { _facingOverride = dir; _hasFacingOverride = true; }
    public void ClearFacingOverride()          { _hasFacingOverride = false; }

    public Vector2 FacingInput
    {
        get
        {
            if (_hasFacingOverride && _facingOverride.sqrMagnitude > 0.0001f)
                return _facingOverride;

            if (input.sqrMagnitude > 0.0001f)
                return input;

            Vector2 velocityInput = new Vector2(currentVelocity.x, currentVelocity.z);
            if (velocityInput.sqrMagnitude > 0.0001f)
                return velocityInput.normalized;

            return Vector2.zero;
        }
    }
    public Vector3 CurrentVelocity => currentVelocity;
    // 给动画层用的"目标"速度(经负重惩罚调整后的稳定值)，跟CurrentVelocity不一样——
    // CurrentVelocity是TerrainGroundMotor按acceleration逐帧加速逼近的瞬时值，起步/
    // 停下的一瞬间会先经过接近0的低值，拿瞬时值去缩放动画播放速度会在每次起步/急停时
    // 让腿部动画跟着抽一下(几乎定格再恢复)，反而比不做速度匹配更容易看出"打滑"。
    // 只给潜行动画速度匹配用，走/跑动画不需要这个(见SpineAnimationDriver_Current字段注释)。
    public float CurrentEffectiveSneakSpeed => currentEffectiveSneakSpeed;
    public UnitMovementType MovementType => movementType;
    public MovementInputMode InputMode => inputMode;
    public MovementApplyMode ApplyMode => movementApplyMode;
    public bool IsUsingExternalTerrainMotor => movementApplyMode == MovementApplyMode.ExternalTerrainMotor;
    public bool IsRunHeld => inputMode == MovementInputMode.PlayerInput ? GetPlayerActionHeld(SkyPrisonInputAction.Sprint, KeyCode.LeftShift) : externalRunHeld;
    public bool IsSneakHeld => inputMode == MovementInputMode.PlayerInput ? GetPlayerActionHeld(SkyPrisonInputAction.Sneak, KeyCode.LeftControl) : externalSneakHeld;
    public bool IsJumping => jumpState != JumpRuntimeState.None;
    public bool IsDodging => dodgeState != DodgeRuntimeState.None;
    public bool IsActionLocked => IsJumping || IsDodging || Time.time < oneShotAnimationLockedUntil;
    public bool ShouldPlayMoveAnimation => movementType != UnitMovementType.Immobile && input.sqrMagnitude > 0.0001f;
    public Rigidbody CachedRigidbody => rb;
    public string CurrentAnimationKey => currentAnimationKey;
    public bool ActionControllerOwnsAnimation => actionControllerOwnsAnimation;
    public JumpRuntimeState CurrentJumpRuntimeState => jumpState;
    public DodgeRuntimeState CurrentDodgeRuntimeState => dodgeState;
    /// <summary>当前闪避（或跳跃等其它一次性动作）的世界方向，闪避接突刺这类"沿用当前
    /// 动作方向"的后续技能需要用它。闪避结束后会被清零，只在对应状态期间读才有意义。</summary>
    public Vector2 CurrentDodgeDirection => currentDodgeDirection;
    /// <summary>当前一次性动作（跳跃/闪避等）状态锁定到什么时间点结束，单位是 Time.time
    /// 的绝对时间戳。闪避接突刺这类"只能在动作快结束的那一小段窗口里衔接"的判断需要
    /// 用 (CurrentActionLockedUntil - Time.time) 算出"还剩多久"。</summary>
    public float CurrentActionLockedUntil => currentActionLockedUntil;
    /// <summary>当前闪避已经播放了多久（秒）——从 TryStartDodge 那一帧算起。闪避接突刺
    /// 的"动画播到第N帧才能打断"窗口用这个而不是"距离结束还有多久"来判断。</summary>
    public float CurrentDodgeElapsedSeconds => dodgeState != DodgeRuntimeState.None ? Time.time - dodgeStartTime : 0f;
    /// <summary>闪避锁定/位移持续的秒数（兜底默认值）。真正的锁定时长由
    /// ExtendDodgeLockDuration 按闪避动画片段的真实时长动态延长，这个只在还没
    /// 拿到真实时长之前，或者取不到动画数据时兜底使用。</summary>
    public float DodgeLockSeconds => Mathf.Max(0.01f, dodgeLockSeconds);

    /// <summary>
    /// 由 SpineAnimationDriver_Current 在真正开始播放闪避动画时调用，把闪避锁定/位移
    /// 窗口延长到跟动画片段真实时长一致——用户明确要求"闪避动画必须完整播完"，不能
    /// 靠加速播放去凑固定的 dodgeLockSeconds，也不能让状态先到期把还在播的动画切掉。
    /// 只在当前确实还处于闪避状态时才延长（防止动画事件延迟到达时误伤下一次状态）。
    /// </summary>
    public void ExtendDodgeLockDuration(float animationDurationSeconds)
    {
        if (dodgeState == DodgeRuntimeState.None) return;
        if (animationDurationSeconds <= 0f) return;

        float target = Time.time + animationDurationSeconds;
        if (target <= actionStateUntil) return; // 已经够长，不用缩短已经生效的窗口

        actionStateUntil = target;
        oneShotAnimationLockedUntil = actionStateUntil;
        currentActionLockedUntil = actionStateUntil;
    }
    public UnitGroundFollowMode GroundFollowMode => groundFollowMode;
    public bool CurrentGroundHasGround => currentGroundHasGround;
    public float CurrentGroundY => currentGroundYRuntime;
    public GroundSurfaceType CurrentGroundSurfaceType => currentGroundSurfaceTypeRuntime;
    public GroundSurfaceMaterialDefinition CurrentGroundSurfaceMaterial => currentGroundSurfaceMaterialRuntime;
    public bool CurrentGroundIsFallDeathArea => currentGroundIsFallDeathArea;
    public string CurrentGroundSource => currentGroundSourceRuntime;
    public bool CurrentWasClampedByMapBoundary => currentWasClampedByMapBoundary;
    public string CurrentMapBoundarySource => currentMapBoundarySourceRuntime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        AutoFindAnimationTargets();
        AutoFindGroundShadowRoot();
        AutoFindGroundQueryService();
        AutoFindBoundaryGroundBlock();
        CacheGroundShadowDefaults();

        if (movementCapsule == null)
            movementCapsule = FindMovementCapsule();

        if (rb != null)
            lockedY = rb.position.y;
        else
            lockedY = transform.position.y;

        CacheCapsuleOffsetFromRoot();
        EnsureTerrainGroundRuntimeDefaults();
        AutoSwitchToExternalTerrainMotorIfAvailable();
    }

    private void OnEnable()
    {
        EnsureRuntimePhysicsApplied();
    }

    private void Start()
    {
        EnsureTerrainGroundRuntimeDefaults();
        AutoSwitchToExternalTerrainMotorIfAvailable();
        EnsureRuntimePhysicsApplied();
        AutoFindExternalTerrainMotor();
    }

    private void Update()
    {
        ReadInput();
        ReadPlayerFallbackActionInput();
        ConsumeQueuedActionInput();
        ProcessDelayedJumpPhysicsLaunchRuntime();
        UpdateTimedActionState();
        UpdateDirectionHoldState();
        RefreshBurdenDebugView();
        UpdateMovementAnimation();
    }

    private void LateUpdate()
    {
        if (enforceMovementAnimationLateUpdate)
            UpdateMovementAnimation();

        UpdateGroundShadowRuntime();
    }

    private void FixedUpdate()
    {
        EnsureRuntimePhysicsApplied();

        if (movementApplyMode == MovementApplyMode.ExternalTerrainMotor)
        {
            if (externalTerrainMotor == null)
                AutoFindExternalTerrainMotor();

            if (externalTerrainMotor == null && fallbackToLegacyFlatMovementWhenExternalTerrainMotorMissing)
            {
                movementApplyMode = MovementApplyMode.LegacyFlatMovement;
                runtimePhysicsInitialized = false;
                EnsureRuntimePhysicsApplied();
            }
            else
            {
                UpdateExternalTerrainMotorResponsive();
                return;
            }
        }

        UpdateJumpPhysicsRuntime();
        UpdateMovementResponsive();
    }

    public void SetMovementApplyMode(MovementApplyMode mode)
    {
        movementApplyMode = mode;
        runtimePhysicsInitialized = false;
        EnsureRuntimePhysicsApplied();
    }

    public void AssignExternalTerrainMotor(TerrainGroundMotorV5 motor)
    {
        externalTerrainMotor = motor;
        EnsureExternalTerrainMotorOwnership();
    }

    public void SetInputMode(MovementInputMode mode)
    {
        inputMode = mode;
        if (inputMode != MovementInputMode.External)
        {
            externalRunHeld = false;
            externalSneakHeld = false;
        }

        if (inputMode == MovementInputMode.Disabled)
            SetMoveInput(Vector2.zero, false, false);
    }

    public void SetPlayerFallbackActionKeysEnabled(bool enabled)
    {
        enablePlayerFallbackActionKeys = enabled;
    }

    public void ConfigureAsExternalExecutor(bool disableFallbackActionKeys = true)
    {
        SetInputMode(MovementInputMode.External);
        if (disableFallbackActionKeys)
            SetPlayerFallbackActionKeysEnabled(false);
        SetActionControllerOwnsAnimation(true);
    }

    public void SetActionControllerOwnsAnimation(bool value)
    {
        actionControllerOwnsAnimation = value;
        if (actionControllerOwnsAnimation)
            currentAnimationKey = string.Empty;
    }

    public void AssignLoadRuntime(UnitLoadRuntime runtime)
    {
        loadRuntime = runtime;
    }

    public UnitLoadRuntime ResolveLoadRuntime()
    {
        if (loadRuntime == null && autoFindLoadRuntime)
            loadRuntime = GetComponent<UnitLoadRuntime>() ?? GetComponentInParent<UnitLoadRuntime>() ?? GetComponentInChildren<UnitLoadRuntime>(true);
        return loadRuntime;
    }

    public UnitActionModuleRuntime ResolveActionModuleRuntime()
    {
        if (actionModuleRuntime == null && autoFindActionModuleRuntime)
            actionModuleRuntime = GetComponent<UnitActionModuleRuntime>() ?? GetComponentInParent<UnitActionModuleRuntime>() ?? GetComponentInChildren<UnitActionModuleRuntime>(true);
        return actionModuleRuntime;
    }

    public void SetMoveInput(Vector2 moveInput, bool runHeld = false)
    {
        SetMoveInput(moveInput, runHeld, false);
    }

    public void SetMoveInput(Vector2 moveInput, bool runHeld, bool sneakHeld)
    {
        if (moveInput.sqrMagnitude > 1f)
            moveInput.Normalize();

        input = moveInput;
        currentInput = input;
        externalRunHeld = runHeld;
        externalSneakHeld = sneakHeld;
    }

    public void SetExternalMoveInput(Vector2 moveInput, bool runHeld = false)
    {
        SetExternalMoveInput(moveInput, runHeld, false);
    }

    public void SetExternalMoveInput(Vector2 moveInput, bool runHeld, bool sneakHeld)
    {
        if (inputMode != MovementInputMode.External)
            inputMode = MovementInputMode.External;

        SetMoveInput(moveInput, runHeld, sneakHeld);
    }

    public void SetExternalMoveInput(Vector2 moveInput, UnitMovementType externalMovementType)
    {
        bool wantsRun = IsRunLikeMovementType(externalMovementType);
        bool wantsSneak = IsSneakLikeMovementType(externalMovementType);
        SetExternalMoveInput(moveInput, wantsRun, wantsSneak);
    }

    private bool IsRunLikeMovementType(UnitMovementType type)
    {
        string value = type.ToString();
        return value.IndexOf("run", System.StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("sprint", System.StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dash", System.StringComparison.OrdinalIgnoreCase) >= 0
            || value.Contains("奔跑")
            || value.Contains("跑步")
            || value.Contains("冲刺")
            || value.Contains("疾跑");
    }

    private bool IsSneakLikeMovementType(UnitMovementType type)
    {
        string value = type.ToString();
        return value.IndexOf("sneak", System.StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("stealth", System.StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("crouch", System.StringComparison.OrdinalIgnoreCase) >= 0
            || value.Contains("潜行")
            || value.Contains("慢走");
    }

    /// <summary>
    /// 外部输入适配器 / AI 专用：本帧请求跳跃。
    /// 不把动作锁死在键盘上，键盘只是开发期 fallback。
    /// </summary>
    public void RequestJump()
    {
        jumpQueued = true;
        jumpQueuedUntil = Time.time + Mathf.Max(0.01f, jumpInputBufferSeconds);
    }

    /// <summary>
    /// 外部输入适配器 / AI 专用：本帧请求闪避。
    /// forward=true 表示当前朝向/输入方向闪避，forward=false 表示反向后撤闪避。
    /// </summary>
    public void RequestDodge(bool forward = true)
    {
        Vector2 baseDir = input.sqrMagnitude > 0.0001f ? input.normalized : FacingInput;
        if (baseDir.sqrMagnitude <= 0.0001f)
            baseDir = Vector2.right;

        RequestDodge(forward ? baseDir : -baseDir, forward ? DodgeRuntimeState.Forward : DodgeRuntimeState.Back);
    }

    public void RequestDodge(Vector2 worldXZDirection, DodgeRuntimeState dodgeKind = DodgeRuntimeState.Forward, float speedScale = 1f)
    {
        if (worldXZDirection.sqrMagnitude > 1f)
            worldXZDirection.Normalize();

        if (worldXZDirection.sqrMagnitude <= 0.0001f)
            worldXZDirection = Vector2.right;

        dodgeQueued = true;
        queuedDodgeState = dodgeKind == DodgeRuntimeState.None ? DodgeRuntimeState.Forward : dodgeKind;
        queuedDodgeDirection = worldXZDirection;
        queuedDodgeSpeedScale = Mathf.Max(0.01f, speedScale);
    }

    /// <summary>
    /// 蓄力攻击松开释放的瞬间调用——角色朝给定世界XZ方向冲一段距离，同时保留当前
    /// Attack状态和攻击判定(hit_start/hit_end事件照常触发)，跟闪避那套完全独立
    /// （不设置dodgeState，不会被当成闪避处理）。durationSeconds决定冲刺跑多久，
    /// distance/durationSeconds算出冲刺速度。
    /// </summary>
    public void StartChargeDash(Vector2 worldXZDirection, float distance, float durationSeconds)
    {
        if (worldXZDirection.sqrMagnitude > 1f)
            worldXZDirection.Normalize();
        if (worldXZDirection.sqrMagnitude <= 0.0001f)
            worldXZDirection = Vector2.right;

        durationSeconds = Mathf.Max(0.05f, durationSeconds);
        chargeDashDirection = worldXZDirection;
        chargeDashSpeed = Mathf.Max(0f, distance) / durationSeconds;
        chargeDashUntil = Time.time + durationSeconds;
        chargeDashActive = true;

        // 起手立即snap到冲刺速度，跳过TerrainGroundMotor的加速插值——跟闪避同样的
        // 道理，不然冲刺时长本来就短，还没加速到目标速度冲刺就已经结束了，位移会
        // 明显不够。
        Vector3 dashVelocity = new Vector3(worldXZDirection.x, 0f, worldXZDirection.y) * chargeDashSpeed;
        if (externalTerrainMotor != null)
            externalTerrainMotor.SnapHorizontalVelocity(dashVelocity);
        // 2026-07-21：只 Snap motor 自己的速度不够——FacingInput 的兜底逻辑读的是这个
        // 类缓存的 currentVelocity 字段，不是 motor 的速度（跟 ImmediateStop 踩过的
        // 同一个坑）。闪避接突刺场景下 CancelDodge() 会把 currentDodgeDirection 清零，
        // 紧接着这里如果不同步更新 currentVelocity，SpineAnimationDriver_Current 那边
        // 朝向判定会读到冲刺开始前的旧方向，导致角色朝向跟实际冲刺方向不一致（看起来
        // 像是"反方向突刺"）。
        currentVelocity = dashVelocity;
    }

    /// <summary>
    /// 后续接真实跳跃高度/地面检测时，由落地检测调用。
    /// 当前 autoLandJumpByTimer 开启时不必手动调用。
    /// </summary>
    public void NotifyJumpLand()
    {
        if (jumpState == JumpRuntimeState.None || jumpState == JumpRuntimeState.Land)
            return;

        EnterJumpLandState();
    }

    public void CancelActionState()
    {
        RestoreExternalTerrainMotorDodgeTuningIfNeeded(externalTerrainMotor);
        jumpQueued = false;
        jumpQueuedUntil = -999f;
        dodgeQueued = false;
        jumpState = JumpRuntimeState.None;
        dodgeState = DodgeRuntimeState.None;
        dodgeDirection = Vector2.zero;
        actionStateUntil = 0f;
        oneShotAnimationLockedUntil = 0f;
        ResetJumpPhysicsRuntime();
        ClearDelayedJumpPhysicsLaunchRuntime();
        currentJumpState = JumpRuntimeState.None;
        currentDodgeState = DodgeRuntimeState.None;
        currentDodgeDirection = Vector2.zero;
        currentActionLockedUntil = 0f;
    }

    public void ClearMoveInput()
    {
        SetMoveInput(Vector2.zero, false);
    }

    /// <summary>立即停步：清输入并强制 motor 水平速度归零，用于受击硬直等场合。
    /// 2026-07-21：之前只清了 externalTerrainMotor 自己的物理速度，没有同步清掉
    /// UnitMovementController 这边缓存的 currentVelocity——FacingInput 的兜底逻辑
    /// (没有输入时按 currentVelocity 判断朝向)读的是这个缓存字段，不是 motor 自己的
    /// 速度，导致调这个方法之后 FacingInput 仍然能读到"归零前"的残留方向，且因为
    /// FacingInput 会对速度向量做 .normalized，哪怕只剩一点点残留速度也会被放大成
    /// 满强度的方向信号，触发朝向误判。现在两边一起清，才是真正的"水平速度归零"。</summary>
    public void ImmediateStop()
    {
        SetMoveInput(Vector2.zero, false);
        currentVelocity = Vector3.zero;
        if (externalTerrainMotor != null)
            externalTerrainMotor.ResetHorizontalVelocity();
        else
            Debug.LogWarning($"[ImmediateStop] {name}: externalTerrainMotor is null, 无法清零速度", this);
    }

    /// <summary>施加一次性击退冲量（世界空间 X 轴方向，m/s）。</summary>
    public void ApplyKnockback(Vector3 worldImpulse)
    {
        if (externalTerrainMotor != null)
        {
            Debug.Log($"[Knockback] ApplyKnockback→Motor impulse={worldImpulse} on {name}", this);
            externalTerrainMotor.AddKnockbackImpulse(worldImpulse);
        }
        else
        {
            Debug.LogWarning($"[Knockback] ApplyKnockback: externalTerrainMotor is null on {name}", this);
        }
    }

    /// <summary>空中攻击悬停用——进入/解除悬停模式。不是完全定住不动(那样像时间停止，
    /// 违和感很重)，而是按缩小过的重力(gravityScale)让角色像"鸟拍一下翅膀"那样先
    /// 顶高(liftAmount)、再缓慢自然地下坠，跳过贴地/落地判定，保证不会在演出(hit_end
    /// 之前)结束前就先落地；解除后恢复正常重力/贴地判定。只在jumpState是Start/Air时
    /// 才有意义，落地/没在跳跃时调用没有任何效果。
    /// 2026-07-21：movementApplyMode==ExternalTerrainMotor（这个项目的实际运行模式）
    /// 下，跳跃物理真正由 externalTerrainMotor(TerrainGroundMotorV5) 驱动，
    /// UnitMovementController 自己这一份 jumpVerticalVelocity/UpdateJumpPhysicsRuntime
    /// 是死路径（同 movement-active-path-terrainmotor 记忆）——冻结要同时转发给
    /// externalTerrainMotor 才会真正生效，只冻自己这份在实机上看不出任何效果。</summary>
    public void SetJumpVerticalPhysicsFrozen(bool frozen, float liftAmount = 0f, float gravityScale = 0.15f)
    {
        jumpVerticalPhysicsFrozen = frozen;
        if (frozen)
        {
            jumpVerticalVelocity = 0f;
            jumpHeightOffset += liftAmount; // 死路径也保持一致，万一某天真的走到 LegacyFlatMovement 兜底
        }

        if (externalTerrainMotor != null)
            externalTerrainMotor.SetVerticalPhysicsFrozen(frozen, liftAmount, gravityScale);
    }

    public void ApplyMovementDefinition(
        UnitMovementType newMovementType,
        float newWalkSpeed,
        float newSneakSpeed,
        float newRunSpeed,
        float newMovementInertia,
        float newMinWalkSpeed,
        float newSustainedMoveSpeedMultiplier,
        float newSustainedMoveDelay)
    {
        movementType = newMovementType;
        walkSpeed = Mathf.Max(0f, newWalkSpeed);
        sneakSpeed = Mathf.Max(0f, newSneakSpeed);
        runSpeed = Mathf.Max(0f, newRunSpeed);
        velocitySharpness = Mathf.Max(0.01f, newMovementInertia);
        minWalkSpeed = Mathf.Max(0f, newMinWalkSpeed);
        sustainedMoveSpeedMultiplier = Mathf.Clamp(newSustainedMoveSpeedMultiplier, 0.5f, 1f);
        sustainedMoveDelay = Mathf.Max(0f, newSustainedMoveDelay);

        if (movementType == UnitMovementType.Immobile)
            StopImmediately();
    }

    public void ApplyJumpDefinition(float newJumpHeight)
    {
        jumpHeight = Mathf.Max(0.05f, newJumpHeight);
    }

    public void ApplyJumpDefinition(
        float newJumpHeight,
        float newLightBurdenJumpHeightMultiplier,
        float newMediumBurdenJumpHeightMultiplier,
        float newHeavyBurdenJumpHeightMultiplier,
        bool newOverweightDisablesJump)
    {
        jumpHeight = Mathf.Max(0.05f, newJumpHeight);
        lightBurdenJumpHeightMultiplier = Mathf.Max(0f, newLightBurdenJumpHeightMultiplier);
        mediumBurdenJumpHeightMultiplier = Mathf.Max(0f, newMediumBurdenJumpHeightMultiplier);
        heavyBurdenJumpHeightMultiplier = Mathf.Max(0f, newHeavyBurdenJumpHeightMultiplier);
        overweightDisablesJump = newOverweightDisablesJump;
    }

    public void ApplyJumpDefinition(float newJumpHeight, float newJumpGravity, bool use3DJumpPhysics = true, float newAirControlMultiplier = 0.85f)
    {
        jumpHeight = Mathf.Max(0.05f, newJumpHeight);
        jumpGravity = Mathf.Max(0.01f, newJumpGravity);
        driveJumpBy3DPhysics = use3DJumpPhysics;
        airControlMultiplier = Mathf.Clamp01(newAirControlMultiplier);
    }

    public void ApplyAnimationDefinition(UnitAnimationKeySet animationKeys)
    {
        if (animationKeys == null)
            return;

        driveMovementAnimation = animationKeys.driveMovementAnimation;
        idleAnimationKey = animationKeys.idleKey;
        walkAnimationKey = animationKeys.walkKey;
        runAnimationKey = animationKeys.runKey;
        sneakAnimationKey = GetStringMember(animationKeys, "sneakKey", animationKeys.sneakKey);

        // 兼容旧 UnitDefinition：旧字段 jumpKey / dodgeKey 继续作为兜底；
        // 新字段存在时，优先使用起跳/滞空/落地、前闪/后闪的拆分 Key。
        jumpAnimationKey = GetStringMember(animationKeys, "jumpKey", animationKeys.jumpKey);
        jumpStartAnimationKey = GetStringMember(animationKeys, "jumpStartKey", string.IsNullOrWhiteSpace(jumpAnimationKey) ? jumpStartAnimationKey : jumpAnimationKey);
        jumpAirAnimationKey = GetStringMember(animationKeys, "jumpAirKey", string.IsNullOrWhiteSpace(jumpAnimationKey) ? jumpAirAnimationKey : jumpAnimationKey);
        jumpLandAnimationKey = GetStringMember(animationKeys, "jumpLandKey", string.IsNullOrWhiteSpace(jumpAnimationKey) ? jumpLandAnimationKey : jumpAnimationKey);

        attackAnimationKey = animationKeys.attackKey;
        hitAnimationKey = animationKeys.hitKey;

        dodgeAnimationKey = GetStringMember(animationKeys, "dodgeKey", animationKeys.dodgeKey);
        dodgeForwardAnimationKey = GetStringMember(animationKeys, "dodgeForwardKey", string.IsNullOrWhiteSpace(dodgeAnimationKey) ? dodgeForwardAnimationKey : dodgeAnimationKey);
        dodgeBackAnimationKey = GetStringMember(animationKeys, "dodgeBackKey", string.IsNullOrWhiteSpace(dodgeAnimationKey) ? dodgeBackAnimationKey : dodgeAnimationKey);

        deathAnimationKey = animationKeys.deathKey;
        movementAnimationFade = Mathf.Max(0f, animationKeys.movementAnimationFade);
        oneShotAnimationLockSeconds = Mathf.Max(0f, animationKeys.oneShotLockSeconds);

        spineAnimationTargetsDirty = true;
        AutoFindAnimationTargets();
        currentAnimationKey = "";
        if (!actionControllerOwnsAnimation)
            UpdateMovementAnimation(force: true);
    }

    public void PlayActionAnimation(UnitActionAnimationSlot slot, bool loop = false)
    {
        string key = GetAnimationKey(slot);
        if (string.IsNullOrWhiteSpace(key))
            return;

        oneShotAnimationLockedUntil = Time.time + oneShotAnimationLockSeconds;
        PlayAnimationKey(key, loop, force: true);
    }

    public void PlayJumpAnimation() => PlayActionAnimation(UnitActionAnimationSlot.Jump, false);
    public void PlayAttackAnimation() => PlayActionAnimation(UnitActionAnimationSlot.Attack, false);
    public void PlayHitAnimation() => PlayActionAnimation(UnitActionAnimationSlot.Hit, false);
    public void PlayDodgeAnimation() => PlayActionAnimation(UnitActionAnimationSlot.Dodge, false);
    public void PlayDeathAnimation() => PlayActionAnimation(UnitActionAnimationSlot.Death, false);

    private static string GetStringMember(object source, string memberName, string fallback)
    {
        if (source == null || string.IsNullOrWhiteSpace(memberName))
            return fallback;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        System.Type type = source.GetType();

        FieldInfo field = type.GetField(memberName, flags);
        if (field != null && field.FieldType == typeof(string))
        {
            string value = field.GetValue(source) as string;
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.PropertyType == typeof(string) && property.CanRead)
        {
            string value = property.GetValue(source, null) as string;
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        return fallback;
    }

    public void ApplyPhysicsDefinition(
        bool useGravity,
        bool isKinematic,
        RigidbodyInterpolation interpolation,
        CollisionDetectionMode collisionDetectionMode,
        RigidbodyConstraints constraints)
    {
        runtimeUseGravity = useGravity;
        runtimeIsKinematic = isKinematic;
        runtimeInterpolation = interpolation;
        runtimeCollisionDetection = collisionDetectionMode;
        runtimeConstraints = constraints;

        ApplyRuntimePhysicsToRigidbody();
    }

    public void AssignMovementCapsule(CapsuleCollider capsule)
    {
        movementCapsule = capsule;
        CacheCapsuleOffsetFromRoot();
    }

    public void ForceRefreshRuntimePhysics()
    {
        ApplyRuntimePhysicsToRigidbody();
    }

    private void EnsureRuntimePhysicsApplied()
    {
        if (runtimePhysicsInitialized)
            return;

        ApplyRuntimePhysicsToRigidbody();
    }

    private void ApplyRuntimePhysicsToRigidbody()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (rb == null)
            return;

        RigidbodyConstraints resolvedConstraints = runtimeConstraints;

        if (movementApplyMode == MovementApplyMode.ExternalTerrainMotor)
        {
            resolvedConstraints &= ~RigidbodyConstraints.FreezePositionY;
            rb.useGravity = false;
            rb.isKinematic = true;
        }
        else
        {
            if (groundFollowMode == UnitGroundFollowMode.RaycastGround || groundFollowMode == UnitGroundFollowMode.PhysicsGravity)
                resolvedConstraints &= ~RigidbodyConstraints.FreezePositionY;

            rb.useGravity = groundFollowMode == UnitGroundFollowMode.PhysicsGravity ? runtimeUseGravity : false;
            rb.isKinematic = runtimeIsKinematic;
        }

        rb.interpolation = runtimeInterpolation;
        rb.collisionDetectionMode = runtimeCollisionDetection;
        rb.constraints = resolvedConstraints;

        runtimePhysicsInitialized = true;
    }

    private void EnsureTerrainGroundRuntimeDefaults()
    {
        int world3DLayer = LayerMask.NameToLayer("World3D");
        if (world3DLayer >= 0)
            groundLayers |= 1 << world3DLayer;

        // 可推道具(锥桶/路障)走推开物理，绝不该被横向解算当墙硬挡，否则蹭它凸包边会被勾住。
        // 直接把 PushableProp 层从阻挡掩码里剔除，比逐碰撞体查组件更稳。
        int pushableLayer = LayerMask.NameToLayer("PushableProp");
        if (pushableLayer >= 0)
            blockingLayers &= ~(1 << pushableLayer);
    }

    private void AutoFindGroundQueryService()
    {
        if (!useGroundQueryService || groundQueryService != null)
            return;

        groundQueryService = GroundQueryService.Active != null
            ? GroundQueryService.Active
            : FindObjectOfType<GroundQueryService>();

        if (groundQueryService == null && autoCreateGroundQueryService)
            groundQueryService = GroundQueryService.FindOrCreateInScene();
    }

    public void AssignGroundQueryService(GroundQueryService service)
    {
        groundQueryService = service;
    }

    public void AssignBoundaryGroundBlock(BaseGroundBlock block)
    {
        boundaryGroundBlock = block;
    }

    private void AutoFindBoundaryGroundBlock()
    {
        if (!constrainToGroundBlockBounds || boundaryGroundBlock != null || !autoFindBoundaryGroundBlock)
            return;

        BaseGroundBlock[] blocks = FindObjectsOfType<BaseGroundBlock>();
        if (blocks == null || blocks.Length == 0)
            return;

        Vector3 position = rb != null ? rb.position : transform.position;
        boundaryGroundBlock = FindBestBoundaryBlock(blocks, position);
    }

    private BaseGroundBlock FindBestBoundaryBlock(BaseGroundBlock[] blocks, Vector3 position)
    {
        BaseGroundBlock best = null;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < blocks.Length; i++)
        {
            BaseGroundBlock block = blocks[i];
            if (block == null)
                continue;

            Bounds bounds = block.WorldBounds;
            if (bounds.size.x <= 0.001f || bounds.size.z <= 0.001f)
                continue;

            if (ContainsXZ(bounds, position))
                return block;

            float score = SqrDistanceToBoundsXZ(bounds, position);
            if (score < bestScore)
            {
                bestScore = score;
                best = block;
            }
        }

        return best;
    }

    private bool ContainsXZ(Bounds bounds, Vector3 position)
    {
        return position.x >= bounds.min.x && position.x <= bounds.max.x &&
               position.z >= bounds.min.z && position.z <= bounds.max.z;
    }

    private float SqrDistanceToBoundsXZ(Bounds bounds, Vector3 position)
    {
        float x = Mathf.Max(bounds.min.x - position.x, 0f, position.x - bounds.max.x);
        float z = Mathf.Max(bounds.min.z - position.z, 0f, position.z - bounds.max.z);
        return x * x + z * z;
    }

    private void AutoFindAnimationTargets()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponentInChildren<Animator>(true);

        RefreshSpineAnimationTargets();
    }

    private void AutoFindGroundShadowRoot()
    {
        if (groundShadowRoot != null)
            return;

        Transform direct = transform.Find("ShadowRoot");
        if (direct != null)
        {
            groundShadowRoot = direct;
            return;
        }

        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (candidate == null || candidate == transform)
                continue;

            if (candidate.name == "ShadowRoot")
            {
                groundShadowRoot = candidate;
                return;
            }
        }
    }

    private void CacheGroundShadowDefaults()
    {
        if (groundShadowRoot == null)
            return;

        groundShadowOriginalLocalPosition = groundShadowRoot.localPosition;
        groundShadowOriginalLocalScale = groundShadowRoot.localScale;
        groundShadowDefaultsCached = true;
    }

    private void UpdateGroundShadowRuntime()
    {
        if (!keepGroundShadowOnGroundDuringJump && !scaleGroundShadowByJumpHeight)
            return;

        AutoFindGroundShadowRoot();
        if (groundShadowRoot == null)
            return;

        if (!groundShadowDefaultsCached)
            CacheGroundShadowDefaults();

        bool externalJumping = movementApplyMode == MovementApplyMode.ExternalTerrainMotor
                               && driveJumpBy3DPhysics
                               && externalTerrainMotor != null
                               && externalTerrainMotor.IsJumping;

        bool internalJumping = driveJumpBy3DPhysics
                               && movementApplyMode != MovementApplyMode.ExternalTerrainMotor
                               && (jumpState == JumpRuntimeState.Start || jumpState == JumpRuntimeState.Air);

        bool shouldLockToGround = externalJumping || internalJumping;

        // Refresh the ground anchor only while the unit is not jumping.
        // This records the authored shadow contact point in the same runtime chain that the player sees.
        if (!shouldLockToGround || !groundShadowWorldAnchorCached)
        {
            groundShadowGroundWorldY = groundShadowRoot.position.y;
            groundShadowGroundRootWorldY = transform.position.y;
            groundShadowWorldAnchorCached = true;
        }

        float jumpOffset = 0f;
        if (externalJumping)
        {
            // Authoritative in the current route: TerrainGroundMotorV5 raises the unit root.
            jumpOffset = Mathf.Max(0f, transform.position.y - groundShadowGroundRootWorldY);
        }
        else if (internalJumping)
        {
            // Legacy route: this script owns vertical jump offset.
            jumpOffset = Mathf.Max(0f, jumpHeightOffset);
        }

        if (keepGroundShadowOnGroundDuringJump)
        {
            if (externalJumping)
            {
                Vector3 worldPosition = groundShadowRoot.position;
                worldPosition.y = groundShadowGroundWorldY;
                groundShadowRoot.position = worldPosition;
            }
            else
            {
                Vector3 localPosition = groundShadowOriginalLocalPosition;
                localPosition.y -= jumpOffset;
                groundShadowRoot.localPosition = localPosition;
            }
        }

        if (scaleGroundShadowByJumpHeight)
        {
            float referenceHeight = groundShadowScaleReferenceHeight > 0.001f
                ? groundShadowScaleReferenceHeight
                : Mathf.Max(0.001f, jumpHeight);
            float t = Mathf.Clamp01(jumpOffset / referenceHeight);
            float scale = Mathf.Lerp(1f, groundShadowScaleAtMaxJumpHeight, t);

            groundShadowRoot.localScale = new Vector3(
                groundShadowOriginalLocalScale.x * scale,
                groundShadowOriginalLocalScale.y,
                groundShadowOriginalLocalScale.z * scale);
        }
        else if (groundShadowRoot.localScale != groundShadowOriginalLocalScale)
        {
            groundShadowRoot.localScale = groundShadowOriginalLocalScale;
        }
    }

    private void RefreshSpineAnimationTargets(bool force = false)
    {
        if (!force && spineAnimationTargetsInitialized && !spineAnimationTargetsDirty)
            return;

        spineAnimationTargets.Clear();

        Component[] components = GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
                continue;

            if (!IsSpineSkeletonAnimationComponent(component))
                continue;

            if (!spineAnimationTargets.Contains(component))
                spineAnimationTargets.Add(component);
        }

        if (spineAnimationComponent != null && IsSpineSkeletonAnimationComponent(spineAnimationComponent))
        {
            if (!spineAnimationTargets.Contains(spineAnimationComponent))
                spineAnimationTargets.Insert(0, spineAnimationComponent);
        }

        // 优先选择真正的 Spine 显示节点，而不是 OutlineProxy / HighlightProxy 等代理节点。
        if (spineAnimationTargets.Count > 0)
        {
            Component best = spineAnimationTargets[0];
            for (int i = 0; i < spineAnimationTargets.Count; i++)
            {
                Component candidate = spineAnimationTargets[i];
                if (candidate == null)
                    continue;

                string path = GetTransformPath(candidate.transform);
                string lower = path.ToLowerInvariant();
                bool looksLikeVisualSpine = lower.Contains("spine gameobject") || lower.Contains("spineroot") || lower.Contains("spine root");
                bool looksLikeProxy = lower.Contains("outlineproxy") || lower.Contains("highlightproxy") || lower.Contains("proxy");

                if (looksLikeVisualSpine && !looksLikeProxy)
                {
                    best = candidate;
                    break;
                }
            }

            spineAnimationComponent = best;
        }

        spineAnimationTargetsInitialized = true;
        spineAnimationTargetsDirty = false;
    }

    private bool IsSpineSkeletonAnimationComponent(Component component)
    {
        if (component == null)
            return false;

        System.Type type = component.GetType();
        if (spineComponentTypeCache.TryGetValue(type, out bool cached))
            return cached;

        string typeName = type.Name;
        string fullName = type.FullName ?? string.Empty;
        bool result = typeName == "SkeletonAnimation" || fullName == "Spine.Unity.SkeletonAnimation"
            || typeName == "SkeletonMecanim" || fullName == "Spine.Unity.SkeletonMecanim"
            || typeName == "SkeletonGraphic" || fullName == "Spine.Unity.SkeletonGraphic";

        spineComponentTypeCache[type] = result;
        return result;
    }

    private string GetTransformPath(Transform target)
    {
        if (target == null)
            return string.Empty;

        string path = target.name;
        Transform current = target.parent;
        while (current != null && current != transform.parent)
        {
            path = current.name + "/" + path;
            if (current == transform)
                break;
            current = current.parent;
        }
        return path;
    }

    private CapsuleCollider FindMovementCapsule()
    {
        Transform collisionRoot = transform.Find("CollisionRoot");
        if (collisionRoot != null)
        {
            CapsuleCollider cap = collisionRoot.GetComponent<CapsuleCollider>();
            if (cap != null)
                return cap;
        }

        return GetComponentInChildren<CapsuleCollider>(true);
    }

    private void CacheCapsuleOffsetFromRoot()
    {
        if (movementCapsule == null)
        {
            cachedCapsuleCenterFromRoot = Vector3.zero;
            return;
        }

        Vector3 worldCenter = movementCapsule.transform.TransformPoint(movementCapsule.center);
        cachedCapsuleCenterFromRoot = transform.InverseTransformPoint(worldCenter);
    }


    private void ReadInput()
    {
        if (movementType == UnitMovementType.Immobile || inputMode == MovementInputMode.Disabled)
        {
            input = Vector2.zero;
            currentInput = Vector2.zero;
            externalRunHeld = false;
            externalSneakHeld = false;
            return;
        }

        if (inputMode == MovementInputMode.PlayerInput)
        {
            if (useSkyPrisonInputSettings)
                input = GetInputSettings().GetMoveVector();
            else
            {
                float x = Input.GetAxisRaw("Horizontal");
                float z = Input.GetAxisRaw("Vertical");
                input = new Vector2(x, z);
            }
        }

        if (input.sqrMagnitude > 1f)
            input.Normalize();

        currentInput = input;
    }

    private SkyPrisonInputSettings GetInputSettings()
    {
        if (inputSettings != null)
        {
            inputSettings.EnsureDefaults();
            return inputSettings;
        }

        inputSettings = Resources.Load<SkyPrisonInputSettings>("SkyPrisonInputSettings");

#if UNITY_EDITOR
        if (inputSettings == null)
            inputSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<SkyPrisonInputSettings>(SkyPrisonInputSettings.DefaultAssetPath);
#endif

        if (inputSettings == null)
        {
            inputSettings = ScriptableObject.CreateInstance<SkyPrisonInputSettings>();
            inputSettings.EnsureDefaults();
        }

        return inputSettings;
    }

    private bool GetPlayerActionHeld(SkyPrisonInputAction action, KeyCode fallbackKey)
    {
        if (useSkyPrisonInputSettings)
            return GetInputSettings().GetAction(action);

        return Input.GetKey(fallbackKey);
    }

    private void ReadPlayerFallbackActionInput()
    {
        if (!enablePlayerFallbackActionKeys)
            return;

        if (inputMode != MovementInputMode.PlayerInput)
            return;

        if (Input.GetKeyDown(fallbackJumpKey))
            RequestJump();

        if (Input.GetKeyDown(fallbackDodgeKey))
        {
            bool back = input.y < -0.15f;
            RequestDodge(!back);
        }
    }

    private void ConsumeQueuedActionInput()
    {
        if (movementType == UnitMovementType.Immobile || inputMode == MovementInputMode.Disabled)
        {
            jumpQueued = false;
            jumpQueuedUntil = -999f;
            dodgeQueued = false;
            return;
        }

        if (dodgeQueued)
        {
            dodgeQueued = false;
            activeDodgeSpeedScale = queuedDodgeSpeedScale;
            queuedDodgeSpeedScale = 1f;
            TryStartDodge(queuedDodgeDirection, queuedDodgeState);
        }

        if (jumpQueued)
        {
            if (Time.time > jumpQueuedUntil)
            {
                jumpQueued = false;
                jumpQueuedUntil = -999f;
            }
            else if (TryStartJump())
            {
                jumpQueued = false;
                jumpQueuedUntil = -999f;
            }
        }
    }

    private void BeginJumpPhysicsRuntime()
    {
        jumpPhysicsLaunched = true;
        jumpPhysicsLaunchPending = false;
        currentJumpPhysicsLaunchPendingRuntime = false;
        currentJumpPhysicsLaunchDelayRemainingRuntime = 0f;
        lastJumpPhysicsLaunchRuntime = "Launched: internal jump physics";
        jumpStartedAt = Time.time;

        if (!driveJumpBy3DPhysics)
            return;

        float height = Mathf.Max(0.05f, EffectiveJumpHeight);
        float gravity = Mathf.Max(0.01f, jumpGravity);
        jumpHeightOffset = 0f;
        jumpVerticalVelocity = Mathf.Sqrt(2f * gravity * height);
        isGrounded = false;
        currentIsGrounded = false;
        currentJumpHeightRuntime = jumpHeightOffset;
        currentJumpVerticalVelocityRuntime = jumpVerticalVelocity;
    }

    private void ResetJumpPhysicsRuntime()
    {
        jumpVerticalVelocity = 0f;
        jumpHeightOffset = 0f;
        jumpStartedAt = -999f;
        jumpPhysicsLaunched = false;
        jumpPhysicsLaunchPending = false;
        jumpPhysicsLaunchAt = -999f;
        currentJumpPhysicsLaunchPendingRuntime = false;
        currentJumpPhysicsLaunchDelayRemainingRuntime = 0f;
        lastJumpPhysicsLaunchRuntime = "Reset";
        isGrounded = true;
        currentIsGrounded = true;
        currentJumpHeightRuntime = 0f;
        currentJumpVerticalVelocityRuntime = 0f;
    }

    private void UpdateJumpPhysicsRuntime()
    {
        if (!driveJumpBy3DPhysics)
            return;

        if (jumpState != JumpRuntimeState.Start && jumpState != JumpRuntimeState.Air)
            return;

        if (ShouldDelayJumpPhysicsLaunch() && !jumpPhysicsLaunched)
            return;

        if (jumpVerticalPhysicsFrozen)
        {
            // 悬停期间高度定住不变，不吃重力——currentJumpVerticalVelocityRuntime摁到0
            // 是给动画/其它读者看的"当前没有下落速度"，不是jumpVerticalVelocity本身
            // （那个已经在SetJumpVerticalPhysicsFrozen(true)时清空过了）。
            currentJumpHeightRuntime = Mathf.Max(0f, jumpHeightOffset);
            currentJumpVerticalVelocityRuntime = 0f;
            currentIsGrounded = false;
            return;
        }

        float gravity = Mathf.Max(0.01f, jumpGravity);
        float dt = Time.fixedDeltaTime;

        jumpVerticalVelocity -= gravity * dt;
        jumpHeightOffset += jumpVerticalVelocity * dt;

        if (jumpHeightOffset <= 0f && jumpVerticalVelocity <= 0f)
        {
            float minAirEndTime = jumpStartedAt + Mathf.Max(0.01f, jumpStartLockSeconds) + Mathf.Max(0.01f, jumpAirMinSeconds);
            if (Time.time >= minAirEndTime)
            {
                jumpHeightOffset = 0f;
                jumpVerticalVelocity = 0f;

                if (jumpState != JumpRuntimeState.Land)
                    EnterJumpLandState();
            }
        }

        currentJumpHeightRuntime = Mathf.Max(0f, jumpHeightOffset);
        currentJumpVerticalVelocityRuntime = jumpVerticalVelocity;
        currentIsGrounded = jumpState == JumpRuntimeState.None || jumpState == JumpRuntimeState.Land;
    }

    private bool ShouldDelayJumpPhysicsLaunch()
    {
        return driveJumpBy3DPhysics
            && delayJumpPhysicsUntilLaunchWindow
            && jumpLaunchPhysicsDelaySeconds > 0.001f;
    }

    private void ScheduleDelayedJumpPhysicsLaunchRuntime()
    {
        jumpPhysicsLaunched = false;
        jumpPhysicsLaunchPending = true;
        jumpPhysicsLaunchAt = Time.time + Mathf.Max(0f, jumpLaunchPhysicsDelaySeconds);
        jumpStartedAt = Time.time;
        jumpHeightOffset = 0f;
        jumpVerticalVelocity = 0f;
        isGrounded = true;
        currentIsGrounded = true;
        currentJumpHeightRuntime = 0f;
        currentJumpVerticalVelocityRuntime = 0f;
        currentJumpPhysicsLaunchPendingRuntime = true;
        currentJumpPhysicsLaunchDelayRemainingRuntime = Mathf.Max(0f, jumpPhysicsLaunchAt - Time.time);
#if UNITY_EDITOR
        lastJumpPhysicsLaunchRuntime = $"Pending: launch in {currentJumpPhysicsLaunchDelayRemainingRuntime:0.###}s";
#endif
    }

    private void ClearDelayedJumpPhysicsLaunchRuntime()
    {
        jumpPhysicsLaunchPending = false;
        jumpPhysicsLaunchAt = -999f;
        currentJumpPhysicsLaunchPendingRuntime = false;
        currentJumpPhysicsLaunchDelayRemainingRuntime = 0f;
    }

    private void ProcessDelayedJumpPhysicsLaunchRuntime()
    {
        if (!jumpPhysicsLaunchPending)
            return;

        if (jumpState != JumpRuntimeState.Start && jumpState != JumpRuntimeState.Air)
        {
            ClearDelayedJumpPhysicsLaunchRuntime();
            return;
        }

        currentJumpPhysicsLaunchPendingRuntime = true;
        currentJumpPhysicsLaunchDelayRemainingRuntime = Mathf.Max(0f, jumpPhysicsLaunchAt - Time.time);

        if (Time.time < jumpPhysicsLaunchAt)
        {
#if UNITY_EDITOR
            lastJumpPhysicsLaunchRuntime = $"Pending: launch in {currentJumpPhysicsLaunchDelayRemainingRuntime:0.###}s";
#endif
            return;
        }

        bool accepted = true;

        if (movementApplyMode == MovementApplyMode.ExternalTerrainMotor && driveJumpBy3DPhysics)
        {
            if (externalTerrainMotor == null)
                AutoFindExternalTerrainMotor();

            accepted = externalTerrainMotor != null && externalTerrainMotor.RequestJump(EffectiveJumpHeight);

            if (accepted)
            {
                jumpPhysicsLaunched = true;
                jumpPhysicsLaunchPending = false;
                currentJumpPhysicsLaunchPendingRuntime = false;
                currentJumpPhysicsLaunchDelayRemainingRuntime = 0f;
                jumpStartedAt = Time.time;
                isGrounded = false;
                currentIsGrounded = false;
                lastJumpPhysicsLaunchRuntime = "Launched: TerrainGroundMotorV5 accepted delayed RequestJump";
            }
        }
        else
        {
            BeginJumpPhysicsRuntime();
            accepted = true;
        }

        if (!accepted)
        {
            lastJumpPhysicsLaunchRuntime = "Rejected: delayed jump launch was refused by TerrainGroundMotorV5";
            ClearDelayedJumpPhysicsLaunchRuntime();

            if (cancelJumpIfDelayedLaunchRejected)
                CancelActionState();
        }
    }

    /// <summary>
    /// 可选：如果以后想让 Spine 事件 Jumpstep_up 精确触发离地，可以在事件转发脚本里调用这个方法。
    /// 当前没有接事件时，会按 jumpLaunchPhysicsDelaySeconds 自动触发。
    /// </summary>
    public void NotifyJumpLaunchImpulse()
    {
        if (!jumpPhysicsLaunchPending)
            return;

        jumpPhysicsLaunchAt = Time.time;
        ProcessDelayedJumpPhysicsLaunchRuntime();
    }

    private bool TryStartJump()
    {
        BurdenRuntimeData burden = EvaluateBurden(CurrentBurden, MaxBurden);
        canJumpNow = burden.canJump;
        currentBaseJumpHeightRuntime = Mathf.Max(0.05f, jumpHeight);
        currentJumpHeightMultiplierRuntime = Mathf.Max(0f, burden.jumpHeightMultiplier);
        currentEffectiveJumpHeightRuntime = canJumpNow ? EffectiveJumpHeight : 0f;

        if (jumpState != JumpRuntimeState.None || dodgeState != DodgeRuntimeState.None || !canJumpNow)
            return false;

        bool delayPhysicsLaunch = ShouldDelayJumpPhysicsLaunch();

        if (movementApplyMode == MovementApplyMode.ExternalTerrainMotor && driveJumpBy3DPhysics)
        {
            if (externalTerrainMotor == null)
                AutoFindExternalTerrainMotor();

            if (externalTerrainMotor == null)
                return false;

            if (!delayPhysicsLaunch && !externalTerrainMotor.RequestJump(EffectiveJumpHeight))
                return false;
        }

        jumpState = JumpRuntimeState.Start;
        float startHoldSeconds = Mathf.Max(0.01f, jumpStartLockSeconds, jumpStartEventHoldSeconds);
        actionStateUntil = Time.time + startHoldSeconds;
        oneShotAnimationLockedUntil = actionStateUntil;
        if (delayPhysicsLaunch)
            ScheduleDelayedJumpPhysicsLaunchRuntime();
        else
            BeginJumpPhysicsRuntime();

        currentJumpState = jumpState;
        currentActionLockedUntil = actionStateUntil;

        string key = ResolveJumpStartKey();
        if (!actionControllerOwnsAnimation)
            PlayAnimationKey(key, false, force: true);
        return true;
    }

    private bool TryStartDodge(Vector2 direction, DodgeRuntimeState kind)
    {
        if (jumpState != JumpRuntimeState.None || dodgeState != DodgeRuntimeState.None || !canDodgeNow)
            return false;

        if (direction.sqrMagnitude > 1f)
            direction.Normalize();
        if (direction.sqrMagnitude <= 0.0001f)
            direction = FacingInput.sqrMagnitude > 0.0001f ? FacingInput.normalized : Vector2.right;

        dodgeState = kind == DodgeRuntimeState.None ? DodgeRuntimeState.Forward : kind;
        dodgeDirection = direction;
        dodgeStartTime = Time.time;
        // 这里用极短的兜底值，不用 dodgeLockSeconds——后者要留给位移速度/加速度计算用，
        // 真正的锁定时长几乎立刻会被 ExtendDodgeLockDuration 按动画真实时长纠正。
        actionStateUntil = Time.time + Mathf.Max(0.01f, dodgeInputUnlockFallbackSeconds);
        oneShotAnimationLockedUntil = actionStateUntil;
        currentDodgeState = dodgeState;
        currentDodgeDirection = dodgeDirection;
        currentActionLockedUntil = actionStateUntil;

        // 闪避起手立即把 Motor 水平速度 snap 到闪避方向，跳过加速插值避免滑步感
        if (externalTerrainMotor != null && driveDodgeMovement)
        {
            float dodgeBaseSpd = ResolveExternalTerrainDodgeSpeed(currentEffectiveRunSpeed > 0.01f ? currentEffectiveRunSpeed : runSpeed);
            externalTerrainMotor.SnapHorizontalVelocity(new Vector3(direction.x, 0f, direction.y) * dodgeBaseSpd);
        }

        string key = ResolveDodgeKey(dodgeState);
        if (!actionControllerOwnsAnimation)
            PlayAnimationKey(key, false, force: true);
        return true;
    }

    /// <summary>立即结束闪避，不等 actionStateUntil 到时间——闪避接突刺打断闪避衔接
    /// 攻击时用。跟 UpdateTimedActionState 里闪避自然结束那段逻辑一致，只是不做
    /// "恢复到行走速度"的收尾 snap，因为调用方紧接着会自己调 StartChargeDash 把速度
    /// 设成突刺速度，这里再 snap 一次纯属多余。</summary>
    public void CancelDodge()
    {
        if (dodgeState == DodgeRuntimeState.None)
            return;

        RestoreExternalTerrainMotorDodgeTuningIfNeeded(externalTerrainMotor);
        dodgeState = DodgeRuntimeState.None;
        dodgeDirection = Vector2.zero;
        currentDodgeState = DodgeRuntimeState.None;
        currentDodgeDirection = Vector2.zero;
        currentActionLockedUntil = 0f;
        actionStateUntil = 0f;
        activeDodgeSpeedScale = 1f;
    }

    private void UpdateTimedActionState()
    {
        if (jumpState == JumpRuntimeState.Start && Time.time >= actionStateUntil)
        {
            jumpState = JumpRuntimeState.Air;
            actionStateUntil = Time.time + Mathf.Max(0.01f, jumpAirMinSeconds);
            oneShotAnimationLockedUntil = actionStateUntil;
            currentJumpState = jumpState;
            currentActionLockedUntil = actionStateUntil;
            if (!actionControllerOwnsAnimation)
                PlayAnimationKey(ResolveJumpAirKey(), true, force: true);
        }
        else if (jumpState == JumpRuntimeState.Air && !driveJumpBy3DPhysics && autoLandJumpByTimer && Time.time >= actionStateUntil)
        {
            EnterJumpLandState();
        }
        else if (jumpState == JumpRuntimeState.Land && Time.time >= actionStateUntil)
        {
            jumpState = JumpRuntimeState.None;
            currentJumpState = JumpRuntimeState.None;
            currentActionLockedUntil = 0f;
            UpdateMovementAnimation(force: true);
        }

        if (dodgeState != DodgeRuntimeState.None && Time.time >= actionStateUntil)
        {
            RestoreExternalTerrainMotorDodgeTuningIfNeeded(externalTerrainMotor);
            dodgeState = DodgeRuntimeState.None;
            dodgeDirection = Vector2.zero;
            currentDodgeState = DodgeRuntimeState.None;
            currentDodgeDirection = Vector2.zero;
            currentActionLockedUntil = 0f;
            activeDodgeSpeedScale = 1f;

            // 闪避结束时立即清除残留速度，防止惯性脚滑。
            // 有输入方向则 snap 到正常行走速度，无输入则清零。
            if (externalTerrainMotor != null)
            {
                float snapSpeed = input.sqrMagnitude > 0.0001f ? currentEffectiveWalkSpeed : 0f;
                Vector3 snapVel = snapSpeed > 0f ? new Vector3(input.x, 0f, input.y).normalized * snapSpeed : Vector3.zero;
                externalTerrainMotor.SnapHorizontalVelocity(snapVel);
            }

            UpdateMovementAnimation(force: true);
        }
    }

    private void EnterJumpLandState()
    {
        jumpHeightOffset = 0f;
        jumpVerticalVelocity = 0f;
        isGrounded = true;
        currentIsGrounded = true;
        currentJumpHeightRuntime = 0f;
        currentJumpVerticalVelocityRuntime = 0f;

        jumpState = JumpRuntimeState.Land;
        float landHoldSeconds = Mathf.Max(0.01f, jumpLandLockSeconds, jumpLandEventHoldSeconds);
        actionStateUntil = Time.time + landHoldSeconds;
        oneShotAnimationLockedUntil = actionStateUntil;
        currentJumpState = jumpState;
        currentActionLockedUntil = actionStateUntil;
        if (!actionControllerOwnsAnimation)
            PlayAnimationKey(ResolveJumpLandKey(), false, force: true);
    }

    private string ResolveJumpStartKey()
    {
        if (!string.IsNullOrWhiteSpace(jumpStartAnimationKey)) return jumpStartAnimationKey;
        return string.IsNullOrWhiteSpace(jumpAnimationKey) ? "Jump_Start" : jumpAnimationKey;
    }

    private string ResolveJumpAirKey()
    {
        if (!string.IsNullOrWhiteSpace(jumpAirAnimationKey)) return jumpAirAnimationKey;
        return string.IsNullOrWhiteSpace(jumpAnimationKey) ? "Jump_Air" : jumpAnimationKey;
    }

    private string ResolveJumpLandKey()
    {
        if (!string.IsNullOrWhiteSpace(jumpLandAnimationKey)) return jumpLandAnimationKey;
        return string.IsNullOrWhiteSpace(jumpAnimationKey) ? "Jump_Land" : jumpAnimationKey;
    }

    private string ResolveDodgeKey(DodgeRuntimeState kind)
    {
        if (kind == DodgeRuntimeState.Back && !string.IsNullOrWhiteSpace(dodgeBackAnimationKey))
            return dodgeBackAnimationKey;

        if (kind == DodgeRuntimeState.Forward && !string.IsNullOrWhiteSpace(dodgeForwardAnimationKey))
            return dodgeForwardAnimationKey;

        return string.IsNullOrWhiteSpace(dodgeAnimationKey) ? "dodge" : dodgeAnimationKey;
    }

    private void UpdateDirectionHoldState()
    {
        if (movementType == UnitMovementType.Immobile)
        {
            sameDirectionTimer = 0f;
            lastNonZeroInput = Vector2.zero;
            return;
        }

        if (input.sqrMagnitude < 0.0001f)
        {
            sameDirectionTimer = 0f;
            lastNonZeroInput = Vector2.zero;
            return;
        }

        if (lastNonZeroInput == Vector2.zero)
        {
            lastNonZeroInput = input;
            sameDirectionTimer = 0f;
            return;
        }

        float sameDir = Vector2.Dot(lastNonZeroInput.normalized, input.normalized);

        if (sameDir > 0.98f)
            sameDirectionTimer += Time.deltaTime;
        else
        {
            sameDirectionTimer = 0f;
            lastNonZeroInput = input;
        }
    }

    private void AutoFindExternalTerrainMotor()
    {
        if (!autoFindExternalTerrainMotor || externalTerrainMotor != null)
            return;

        externalTerrainMotor = GetComponent<TerrainGroundMotorV5>();
        EnsureExternalTerrainMotorOwnership();
    }

    private void AutoSwitchToExternalTerrainMotorIfAvailable()
    {
        if (!autoSwitchToExternalTerrainMotorWhenAvailable)
            return;

        AutoFindExternalTerrainMotor();

        if (externalTerrainMotor == null)
            return;

        movementApplyMode = MovementApplyMode.ExternalTerrainMotor;
        lockYToStartPosition = false;
        EnsureExternalTerrainMotorOwnership();
    }

    private void EnsureExternalTerrainMotorOwnership()
    {
        if (!enforceExternalTerrainMotorInputOwnership)
        {
            externalTerrainMotorOwnershipRuntime = "Disabled";
            return;
        }

        if (movementApplyMode != MovementApplyMode.ExternalTerrainMotor)
        {
            externalTerrainMotorOwnershipRuntime = "Inactive: " + movementApplyMode;
            return;
        }

        if (externalTerrainMotor == null)
        {
            externalTerrainMotorOwnershipRuntime = "Missing";
            return;
        }

        externalTerrainMotor.ClaimExternalInputOwner(this, "UnitMovementController.ExternalTerrainMotor");
        externalTerrainMotorOwnershipRuntime = externalTerrainMotor.ExternalInputOwnerRuntime;
    }

    private void OnDisable()
    {
        RestoreExternalTerrainMotorDodgeTuningIfNeeded(externalTerrainMotor);
        if (externalTerrainMotor != null)
            externalTerrainMotor.ReleaseExternalInputOwner(this);
    }

    private float ResolveExternalTerrainDodgeSpeed(float effectiveRun)
    {
        float lockSeconds = Mathf.Max(0.01f, dodgeLockSeconds);
        float targetDistance = dodgeState == DodgeRuntimeState.Back ? dodgeBackDistance : dodgeDistance;
        float distanceSpeed = useDodgeDistanceForExternalTerrainMotor ? Mathf.Max(0f, targetDistance) / lockSeconds : 0f;
        float resolved = Mathf.Max(effectiveRun, dodgeSpeed, distanceSpeed);
        return Mathf.Max(0f, resolved * Mathf.Max(0.01f, externalTerrainDodgeSpeedMultiplier) * Mathf.Max(0.01f, activeDodgeSpeedScale));
    }

    private void ApplyExternalTerrainMotorDodgeTuning(TerrainGroundMotorV5 motor, bool dodging, float dodgeMoveSpeed)
    {
        if (motor == null)
            return;

        if (!boostExternalTerrainMotorAccelerationDuringDodge)
        {
            RestoreExternalTerrainMotorDodgeTuningIfNeeded(motor);
            return;
        }

        if (!dodging)
        {
            RestoreExternalTerrainMotorDodgeTuningIfNeeded(motor);
            return;
        }

        if (cachedExternalDodgeMotor != motor || !cachedExternalDodgeMotorAccelerationValid)
        {
            RestoreExternalTerrainMotorDodgeTuningIfNeeded(cachedExternalDodgeMotor);
            cachedExternalDodgeMotor = motor;
            cachedExternalDodgeMotorAcceleration = motor.acceleration;
            cachedExternalDodgeMotorAccelerationValid = true;
        }

        float requiredAcceleration = Mathf.Max(0.01f, dodgeMoveSpeed) / Mathf.Max(0.01f, dodgeLockSeconds);
        requiredAcceleration *= Mathf.Max(0.01f, externalTerrainDodgeAccelerationMultiplier);
        motor.acceleration = Mathf.Max(cachedExternalDodgeMotorAcceleration, requiredAcceleration);
    }

    private void RestoreExternalTerrainMotorDodgeTuningIfNeeded(TerrainGroundMotorV5 motor)
    {
        if (!cachedExternalDodgeMotorAccelerationValid)
            return;

        TerrainGroundMotorV5 target = motor != null ? motor : cachedExternalDodgeMotor;
        if (target != null && target == cachedExternalDodgeMotor)
            target.acceleration = cachedExternalDodgeMotorAcceleration;

        cachedExternalDodgeMotor = null;
        cachedExternalDodgeMotorAccelerationValid = false;
    }

    private void UpdateExternalTerrainMotorResponsive()
    {
        currentWasClampedByMapBoundary = false;
        currentMapBoundarySourceRuntime = "ExternalTerrainMotor";

        if (externalTerrainMotor == null)
            AutoFindExternalTerrainMotor();

        BurdenRuntimeData burden = EvaluateBurden(CurrentBurden, MaxBurden);
        currentBurdenLevel = burden.level;

        canMoveNow = movementType != UnitMovementType.Immobile && inputMode != MovementInputMode.Disabled;
        canRunNow = burden.canRun;
        canDodgeNow = burden.canDodge;
        canJumpNow = burden.canJump;

        float effectiveWalk = Mathf.Max(minWalkSpeed, walkSpeed - burden.walkPenalty);
        float effectiveSneak = Mathf.Min(sneakSpeed, Mathf.Max(minWalkSpeed * 0.45f, sneakSpeed - burden.walkPenalty * 0.65f));
        float effectiveRun = Mathf.Max(effectiveWalk, runSpeed - burden.runPenalty);

        bool wantsSneak = IsSneakHeld;
        bool wantsRun = !wantsSneak && IsRunHeld && burden.canRun;
        Vector2 motorInput = canMoveNow ? input : Vector2.zero;

        bool drivingDodgeExternally = dodgeState != DodgeRuntimeState.None && driveDodgeMovement;

        // 蓄力冲刺到点了就自动结束——跟闪避不一样，没有单独的状态机tick方法，直接在
        // 这里每帧检查过期时间就够了，冲刺时长本来就短。
        //
        // 结束瞬间必须显式把速度摁到0，不能指望TerrainGroundMotorV5自己的线性减速
        // (deceleration默认200/s²，MoveTowards逐帧线性逼近0)——那套是按正常移动速度
        // (几~十几)校准的，冲刺速度动辄几十，同样的减速率算下来滑行距离 = 速度²/(2×
        // 减速度)，按平方增长：50速度/200减速度 = 滑行6.25米，几乎是冲刺本身距离的
        // 一倍以上。这正是"距离感觉多出一截"、"松手了还在滑"的根因——不是冲刺距离
        // 参数本身有问题，是速度调高之后惯性滑行被放大了，靠调距离/时长两个参数
        // 都解决不了，必须冲刺结束时主动清零速度。
        if (chargeDashActive && Time.time >= chargeDashUntil)
        {
            chargeDashActive = false;
            if (externalTerrainMotor != null)
                externalTerrainMotor.SnapHorizontalVelocity(Vector3.zero);
        }
        bool drivingChargeDashExternally = chargeDashActive;

        float externalDodgeMoveSpeed = ResolveExternalTerrainDodgeSpeed(effectiveRun);

        if (drivingDodgeExternally)
        {
            motorInput = dodgeDirection.sqrMagnitude > 0.0001f ? dodgeDirection.normalized : FacingInput;
            wantsRun = true;
            wantsSneak = false;
        }
        else if (drivingChargeDashExternally)
        {
            motorInput = chargeDashDirection;
            wantsRun = true;
            wantsSneak = false;
        }

        float chosenSpeed = drivingDodgeExternally
            ? externalDodgeMoveSpeed
            : (drivingChargeDashExternally
                ? chargeDashSpeed
                : (wantsSneak ? effectiveSneak : (wantsRun ? effectiveRun : effectiveWalk)));
        Vector3 targetVelocity = new Vector3(motorInput.x, 0f, motorInput.y) * chosenSpeed;
        float effectiveSharpness = Mathf.Max(0.01f, velocitySharpness * burden.sharpnessMultiplier);
        currentVelocity = Vector3.Lerp(
            currentVelocity,
            targetVelocity,
            1f - Mathf.Exp(-effectiveSharpness * Time.fixedDeltaTime));
        // 指数逼近永远到不了精确的 0——玩家松手停下来之后，targetVelocity 已经是 0 了，
        // currentVelocity 却要花好几秒才能衰减到肉眼不可见但数值上依然非零的程度，
        // 这几秒里角色（进而摄像机、进而背包窗口色收差的运动检测）其实一直在极其
        // 轻微地继续挪动，实测过是背包窗口"站定还一直闪烁"的真正根因。目标速度
        // 是 0 且当前速度已经很小时，直接摁到精确的 0，把这条衰减尾巴掐掉。
        if (targetVelocity.sqrMagnitude < 0.0001f && currentVelocity.sqrMagnitude < 0.0004f)
            currentVelocity = Vector3.zero;

        if (externalTerrainMotor != null)
        {
            EnsureExternalTerrainMotorOwnership();
            ApplyExternalTerrainMotorDodgeTuning(externalTerrainMotor, drivingDodgeExternally, externalDodgeMoveSpeed);

            // Dodge must not be cancelled by TerrainGroundMotor sprint-block grace or acceleration ramp.
            // During dodge, both walk and sprint speed are temporarily set to the resolved dodge speed,
            // so even if the motor internally downgrades sprint to walk, the dodge distance remains stable.
            //
            // TerrainGroundMotorV5 只有 moveSpeed/sprintSpeed 两档，SetMoveInput 的 sprint 参数
            // 是唯一的选档开关——潜行时 wantsRun 恒为 false（跟奔跑互斥），所以潜行走的也是
            // moveSpeed 这一档。之前这里无条件填 effectiveWalk，从没把 effectiveSneak 传给真正
            // 驱动位移/碰撞的 TerrainGroundMotorV5（本脚本自己的 currentVelocity 只是给动画用的
            // 模拟值，不是实际位移来源，见 movement-active-path-terrainmotor 记忆）——潜行时
            // 角色物理速度其实一直是走路速度，不管 sneakSpeed 改成多少都感觉不到变化，正是
            // "潜行和走路速度感觉一样"、"sneakSpeed调到0.01都没反应"的根因。
            // 蓄力冲刺(drivingChargeDashExternally)跟闪避一样，moveSpeed/sprintSpeed两档
            // 都填成冲刺速度——wantsRun上面已经强制true，实际走的是sprintSpeed这一档，
            // 但两档都填一致更保险，不用纠结Motor内部具体读哪一个。
            externalTerrainMotor.moveSpeed = drivingDodgeExternally ? externalDodgeMoveSpeed
                : drivingChargeDashExternally ? chargeDashSpeed
                : (wantsSneak ? effectiveSneak : effectiveWalk);
            externalTerrainMotor.sprintSpeed = drivingDodgeExternally ? externalDodgeMoveSpeed
                : drivingChargeDashExternally ? chargeDashSpeed
                : effectiveRun;
            externalTerrainMotor.SetMoveInput(motorInput, wantsRun);

            currentGroundHasGround = externalTerrainMotor.IsGrounded || externalTerrainMotor.IsSliding;
            currentGroundYRuntime = rb != null ? rb.position.y : transform.position.y;
            currentGroundSurfaceTypeRuntime = GroundSurfaceType.Default;
            currentGroundSurfaceMaterialRuntime = null;
            currentGroundIsFallDeathArea = externalTerrainMotor.IsFalling;
#if UNITY_EDITOR
            currentGroundSourceRuntime = externalTerrainMotor.State.ToString();
#endif
            currentIsGrounded = externalTerrainMotor.IsGrounded;

            if ((jumpState == JumpRuntimeState.Start || jumpState == JumpRuntimeState.Air) &&
                externalTerrainMotor.IsGrounded &&
                !externalTerrainMotor.IsJumping)
            {
                float minAirEndTime = jumpStartedAt + Mathf.Max(0.01f, jumpStartLockSeconds) + Mathf.Max(0.01f, jumpAirMinSeconds);
                if (Time.time >= minAirEndTime)
                    EnterJumpLandState();
            }
        }
        else
        {
            RestoreExternalTerrainMotorDodgeTuningIfNeeded(null);
            currentGroundHasGround = false;
            currentGroundIsFallDeathArea = false;
            currentGroundSourceRuntime = "ExternalTerrainMotor:Missing";
            currentIsGrounded = false;
        }

        currentEffectiveWalkSpeed = effectiveWalk;
        currentEffectiveSneakSpeed = effectiveSneak;
        currentEffectiveRunSpeed = effectiveRun;
        currentBaseJumpHeightRuntime = Mathf.Max(0.05f, jumpHeight);
        currentJumpHeightMultiplierRuntime = Mathf.Max(0f, burden.jumpHeightMultiplier);
        currentEffectiveJumpHeightRuntime = canJumpNow ? currentBaseJumpHeightRuntime * currentJumpHeightMultiplierRuntime : 0f;
        currentWantsRun = wantsRun;
        currentWantsSneak = wantsSneak;
        currentChosenMoveSpeed = chosenSpeed;
        currentEffectiveSharpness = effectiveSharpness;
        currentBurdenPercent01 = BurdenPercent01;
        currentBurdenPercent100 = BurdenPercent100;
        currentBurdenValueRuntime = CurrentBurden;
        maxBurdenValueRuntime = MaxBurden;
    }

    private void UpdateMovementResponsive()
    {
        if (rb == null)
            return;

        currentWasClampedByMapBoundary = false;
        currentMapBoundarySourceRuntime = "-";

        if (movementType == UnitMovementType.Immobile)
        {
            canMoveNow = false;
            currentVelocity = Vector3.zero;
            Vector3 stayPosition = rb.position;
            stayPosition = ResolveMapBoundaryPosition(stayPosition);
            stayPosition = ResolveGroundPosition(stayPosition);
            rb.MovePosition(stayPosition);
            currentBurdenLevel = EvaluateBurden(CurrentBurden, MaxBurden).level;
            canRunNow = false;
            canDodgeNow = false;
            canJumpNow = false;
            currentEffectiveWalkSpeed = 0f;
            currentEffectiveSneakSpeed = 0f;
            currentEffectiveRunSpeed = 0f;
            currentBaseJumpHeightRuntime = Mathf.Max(0.05f, jumpHeight);
            currentJumpHeightMultiplierRuntime = 0f;
            currentEffectiveJumpHeightRuntime = 0f;
            currentWantsRun = false;
            currentWantsSneak = false;
            currentChosenMoveSpeed = 0f;
            currentEffectiveSharpness = 0f;
            return;
        }

        canMoveNow = true;

        BurdenRuntimeData burden = EvaluateBurden(CurrentBurden, MaxBurden);

        if (dodgeState != DodgeRuntimeState.None && driveDodgeMovement)
        {
            float dodgeBaseSpeed = Mathf.Max(runSpeed, dodgeSpeed);
            Vector3 dodgeTargetVelocity = new Vector3(dodgeDirection.x, 0f, dodgeDirection.y) * dodgeBaseSpeed;
            currentVelocity = dodgeTargetVelocity;

            Vector3 dodgeDesiredDelta = currentVelocity * Time.fixedDeltaTime;
            dodgeDesiredDelta = ResolveMapBoundaryDelta(rb.position, dodgeDesiredDelta);
            dodgeDesiredDelta = ResolveGroundShapeDelta(rb.position, dodgeDesiredDelta);
            Vector3 dodgeSolvedDelta = ResolveMovementDelta(rb.position, dodgeDesiredDelta);
            Vector3 dodgeNextPosition = rb.position + dodgeSolvedDelta;
            dodgeNextPosition = ResolveMapBoundaryPosition(dodgeNextPosition);
            dodgeNextPosition = ResolveGroundPosition(dodgeNextPosition);
            Vector3 dodgePrePenetrationPosition = dodgeNextPosition;

            if (solvePenetrationAfterMove)
            {
                Vector3 dodgePenetrationResolvedPosition = ResolvePenetrationAtPosition(dodgeNextPosition);
                dodgeNextPosition = ResolveGroundShapePositionAfterPenetration(
                    rb.position,
                    dodgePrePenetrationPosition,
                    dodgePenetrationResolvedPosition);
                dodgeNextPosition = ResolveMapBoundaryPosition(dodgeNextPosition);
                dodgeNextPosition = ResolveGroundPosition(dodgeNextPosition);
            }

            Vector3 dodgeVelocitySamplePosition =
                ignorePenetrationCorrectionWhenUpdatingVelocity ? dodgePrePenetrationPosition : dodgeNextPosition;
            UpdateHorizontalVelocityFromResolvedMove(rb.position, dodgeVelocitySamplePosition);
            rb.MovePosition(dodgeNextPosition);

            currentBurdenLevel = burden.level;
            canRunNow = burden.canRun;
            canDodgeNow = burden.canDodge;
            canJumpNow = burden.canJump;
            currentEffectiveWalkSpeed = Mathf.Max(minWalkSpeed, walkSpeed - burden.walkPenalty);
            currentEffectiveSneakSpeed = Mathf.Min(sneakSpeed, Mathf.Max(minWalkSpeed * 0.45f, sneakSpeed - burden.walkPenalty * 0.65f));
            currentEffectiveRunSpeed = Mathf.Max(currentEffectiveWalkSpeed, runSpeed - burden.runPenalty);
            currentBaseJumpHeightRuntime = Mathf.Max(0.05f, jumpHeight);
            currentJumpHeightMultiplierRuntime = Mathf.Max(0f, burden.jumpHeightMultiplier);
            currentEffectiveJumpHeightRuntime = canJumpNow ? currentBaseJumpHeightRuntime * currentJumpHeightMultiplierRuntime : 0f;
            currentWantsSneak = false;
            currentWantsRun = false;
            currentChosenMoveSpeed = dodgeBaseSpeed;
            currentEffectiveSharpness = velocitySharpness * burden.sharpnessMultiplier;
            currentBurdenPercent01 = BurdenPercent01;
            currentBurdenPercent100 = BurdenPercent100;
            currentBurdenValueRuntime = CurrentBurden;
            maxBurdenValueRuntime = MaxBurden;
            return;
        }

        float effectiveWalk = Mathf.Max(minWalkSpeed, walkSpeed - burden.walkPenalty);
        float effectiveSneak = Mathf.Min(sneakSpeed, Mathf.Max(minWalkSpeed * 0.45f, sneakSpeed - burden.walkPenalty * 0.65f));
        float effectiveRun = Mathf.Max(effectiveWalk, runSpeed - burden.runPenalty);

        bool wantsSneak = IsSneakHeld;
        bool wantsRun = !wantsSneak && IsRunHeld && burden.canRun;

        float baseSpeed = wantsSneak ? effectiveSneak : (wantsRun ? effectiveRun : effectiveWalk);

        // 旧版这个系数会把“持续移动”统一乘 0.93。
        // 结果奔跑 5.2 会被压成 4.836，看起来就不像跑。
        // 现在只压普通行走，不压 AI/玩家奔跑。
        if (!wantsSneak && !wantsRun && sameDirectionTimer >= sustainedMoveDelay)
            baseSpeed *= sustainedMoveSpeedMultiplier;

        currentWantsSneak = wantsSneak;
        currentWantsRun = wantsRun;
        currentChosenMoveSpeed = baseSpeed;

        Vector3 targetVelocity = new Vector3(input.x, 0f, input.y) * baseSpeed;
        float effectiveSharpness = velocitySharpness * burden.sharpnessMultiplier;
        if (driveJumpBy3DPhysics && (jumpState == JumpRuntimeState.Start || jumpState == JumpRuntimeState.Air))
            effectiveSharpness *= Mathf.Clamp01(airControlMultiplier);

        currentVelocity = Vector3.Lerp(
            currentVelocity,
            targetVelocity,
            1f - Mathf.Exp(-effectiveSharpness * Time.fixedDeltaTime)
        );
        // 同上：指数衰减到不了精确 0，停下后还会拖好几秒极小幅度的残留位移，
        // 摁到 0 掐掉这条尾巴（见上面 dodge 分支同款注释）。
        if (targetVelocity.sqrMagnitude < 0.0001f && currentVelocity.sqrMagnitude < 0.0004f)
            currentVelocity = Vector3.zero;

        Vector3 desiredDelta = currentVelocity * Time.fixedDeltaTime;
        desiredDelta = ResolveMapBoundaryDelta(rb.position, desiredDelta);
        desiredDelta = ResolveGroundShapeDelta(rb.position, desiredDelta);
        Vector3 solvedDelta = ResolveMovementDelta(rb.position, desiredDelta);

        Vector3 nextPosition = rb.position + solvedDelta;

        nextPosition = ResolveMapBoundaryPosition(nextPosition);
        nextPosition = ResolveGroundPosition(nextPosition);
        Vector3 prePenetrationPosition = nextPosition;

        if (solvePenetrationAfterMove)
        {
            Vector3 penetrationResolvedPosition = ResolvePenetrationAtPosition(nextPosition);
            nextPosition = ResolveGroundShapePositionAfterPenetration(
                rb.position,
                prePenetrationPosition,
                penetrationResolvedPosition);
            nextPosition = ResolveMapBoundaryPosition(nextPosition);
            nextPosition = ResolveGroundPosition(nextPosition);
        }

        Vector3 velocitySamplePosition =
            ignorePenetrationCorrectionWhenUpdatingVelocity ? prePenetrationPosition : nextPosition;
        UpdateHorizontalVelocityFromResolvedMove(rb.position, velocitySamplePosition);
        rb.MovePosition(nextPosition);

        currentBurdenLevel = burden.level;
        canRunNow = burden.canRun;
        canDodgeNow = burden.canDodge;
        canJumpNow = burden.canJump;
        currentBaseJumpHeightRuntime = Mathf.Max(0.05f, jumpHeight);
        currentJumpHeightMultiplierRuntime = Mathf.Max(0f, burden.jumpHeightMultiplier);
        currentEffectiveJumpHeightRuntime = canJumpNow ? currentBaseJumpHeightRuntime * currentJumpHeightMultiplierRuntime : 0f;
        currentEffectiveWalkSpeed = effectiveWalk;
        currentEffectiveSneakSpeed = effectiveSneak;
        currentEffectiveRunSpeed = effectiveRun;
        currentChosenMoveSpeed = baseSpeed;
        currentEffectiveSharpness = effectiveSharpness;
        currentBurdenPercent01 = BurdenPercent01;
        currentBurdenPercent100 = BurdenPercent100;
        currentBurdenValueRuntime = CurrentBurden;
        maxBurdenValueRuntime = MaxBurden;
    }

    private Vector3 ResolveMovementDelta(Vector3 currentPosition, Vector3 desiredDelta)
    {
        if (movementCapsule == null)
            return desiredDelta;

        float distance = desiredDelta.magnitude;
        if (distance <= 0.0001f)
            return Vector3.zero;

        Vector3 direction = desiredDelta / distance;

        GetCapsuleWorldPoints(currentPosition, out Vector3 p1, out Vector3 p2, out float radius);

        bool hitSomething = TryCapsuleCastBlocking(
            p1,
            p2,
            radius,
            direction,
            distance + shellOffset,
            out RaycastHit hit);

        if (!hitSomething)
        {
            if (drawDebug)
                Debug.DrawRay(currentPosition, desiredDelta, Color.green, 0.05f);
            return desiredDelta;
        }

        float safeDistance = Mathf.Max(0f, hit.distance - shellOffset);
        Vector3 moveToContact = direction * safeDistance;

        Vector3 remaining = desiredDelta - moveToContact;
        // 角色是平面移动：把碰撞法线压到水平面再算贴墙滑动。蹭 MeshCollider 的三角形边/底沿时
        // hit.normal 常带 Y 分量（朝上斜），直接 ProjectOnPlane 会把横向滑动量吃掉，表现为偶发"被勾住"。
        Vector3 slideNormal = HorizontalizeNormal(hit.normal);
        Vector3 slide = Vector3.ProjectOnPlane(remaining, slideNormal) * wallSlideFactor;

        GetCapsuleWorldPoints(currentPosition + moveToContact, out Vector3 slideP1, out Vector3 slideP2, out float slideRadius);

        bool slideBlocked = false;
        RaycastHit slideHit = default;

        if (slide.sqrMagnitude > 0.000001f)
        {
            slideBlocked = TryCapsuleCastBlocking(
                slideP1,
                slideP2,
                slideRadius,
                slide.normalized,
                slide.magnitude + shellOffset,
                out slideHit);
        }

        if (slideBlocked)
        {
            float slideSafeDistance = Mathf.Max(0f, slideHit.distance - shellOffset);
            slide = slide.normalized * slideSafeDistance;
        }

        Vector3 finalDelta = moveToContact + slide;

        if (drawDebug)
        {
            Debug.DrawRay(currentPosition, desiredDelta, Color.red, 0.05f);
            Debug.DrawRay(currentPosition, moveToContact, Color.yellow, 0.05f);
            Debug.DrawRay(currentPosition + moveToContact, slide, Color.cyan, 0.05f);
        }

        return finalDelta;
    }

    // 把碰撞法线压到水平面（XZ）。用于平面移动的贴墙滑动，避免网格边/底沿的斜向法线把横向位移吃掉。
    // 法线几乎竖直（站在物体顶面之类）时退回原法线，避免除零/异常。
    private static Vector3 HorizontalizeNormal(Vector3 normal)
    {
        Vector3 flat = new Vector3(normal.x, 0f, normal.z);
        return flat.sqrMagnitude > 1e-6f ? flat.normalized : normal;
    }

    private bool TryCapsuleCastBlocking(
        Vector3 p1,
        Vector3 p2,
        float radius,
        Vector3 direction,
        float distance,
        out RaycastHit bestHit)
    {
        bestHit = default;

        int count = Physics.CapsuleCastNonAlloc(
            p1,
            p2,
            radius,
            direction,
            movementCastBuffer,
            distance,
            blockingLayers,
            QueryTriggerInteraction.Ignore);

        if (count <= 0)
            return false;

        bool found = false;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = movementCastBuffer[i];
            movementCastBuffer[i] = default;

            Collider col = hit.collider;
            if (ShouldIgnoreBlockingCollider(col))
                continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                bestHit = hit;
                found = true;
            }
        }

        return found;
    }

    private bool ShouldIgnoreBlockingCollider(Collider col)
    {
        if (col == null)
            return true;

        if (IsOwnCollider(col))
            return true;

        if (col.isTrigger)
            return true;

        // BaseGroundBlock 的 Collider 是“可站地面/采样平面”，不是水平阻挡体。
        // 如果让它参与 CapsuleCast / ComputePenetration，角色贴地图边缘或 ShapeMask 边缘时会被地面碰撞体横向推出，
        // 然后又被边界/ShapeMask 拉回，形成只能跳跃脱困的粘边状态。
        if (ignoreBaseGroundBlockCollidersInBlockingSolve && col.GetComponentInParent<BaseGroundBlock>() != null)
            return true;

        if (ignoreTerrainCollidersInBlockingSolve && col is TerrainCollider)
            return true;

        // 可推道具(锥桶等)由推开物理处理，不参与硬阻挡/退穿透；否则蹭它的凸包边会被勾住。
        if (ignorePushablePropCollidersInBlockingSolve && col.GetComponentInParent<SkyPrisonPushablePropRuntime>() != null)
            return true;

        return false;
    }

    private void GetCapsuleWorldPoints(Vector3 rootPosition, out Vector3 p1, out Vector3 p2, out float radius)
    {
        if (movementCapsule == null)
        {
            p1 = rootPosition;
            p2 = rootPosition;
            radius = 0.25f;
            return;
        }

        Transform capTransform = movementCapsule.transform;
        Vector3 lossy = capTransform.lossyScale;

        radius = movementCapsule.radius * Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
        float height = Mathf.Max(movementCapsule.height * Mathf.Abs(lossy.y), radius * 2f);
        float half = Mathf.Max(0f, height * 0.5f - radius);

        Vector3 center = rootPosition + cachedCapsuleCenterFromRoot;
        p1 = center + Vector3.up * half;
        p2 = center - Vector3.up * half;
    }

    private Vector3 ResolvePenetrationAtPosition(Vector3 rootPosition)
    {
        if (movementCapsule == null)
            return rootPosition;

        int iterations = Mathf.Clamp(penetrationSolveIterations, 1, 4);
        Vector3 resolvedPosition = rootPosition;
        float maxCorrection = Mathf.Max(0.005f, maxPenetrationCorrectionPerFrame);
        if (useSoftPenetrationCorrection)
            maxCorrection = Mathf.Min(maxCorrection, Mathf.Max(0.005f, softMaxPenetrationCorrectionPerFrame));

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            Vector3 correction = ComputePenetrationCorrection(resolvedPosition);
            correction.y = 0f;

            if (correction.sqrMagnitude <= 0.000001f)
                break;

            if (useSoftPenetrationCorrection)
                correction *= Mathf.Clamp(penetrationCorrectionStrength, 0.05f, 1f);

            if (correction.magnitude > maxCorrection)
                correction = correction.normalized * maxCorrection;

            resolvedPosition += correction;
        }

        return resolvedPosition;
    }

    private Vector3 ComputePenetrationCorrection(Vector3 rootPosition)
    {
        GetCapsuleWorldPoints(rootPosition, out Vector3 p1, out Vector3 p2, out float radius);

        int count = Physics.OverlapCapsuleNonAlloc(
            p1,
            p2,
            radius + shellOffset,
            penetrationBuffer,
            blockingLayers,
            QueryTriggerInteraction.Ignore);

        if (count <= 0)
            return Vector3.zero;

        Vector3 rootDelta = rootPosition - transform.position;
        Vector3 simulatedCapsulePosition = movementCapsule.transform.position + rootDelta;
        Quaternion simulatedCapsuleRotation = movementCapsule.transform.rotation;
        Vector3 totalCorrection = Vector3.zero;
        int validCorrectionCount = 0;

        for (int i = 0; i < count; i++)
        {
            Collider other = penetrationBuffer[i];
            penetrationBuffer[i] = null;

            if (ShouldIgnoreBlockingCollider(other))
                continue;

            bool overlapped = Physics.ComputePenetration(
                movementCapsule,
                simulatedCapsulePosition,
                simulatedCapsuleRotation,
                other,
                other.transform.position,
                other.transform.rotation,
                out Vector3 direction,
                out float distance);

            if (!overlapped || distance <= 0f)
                continue;

            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.000001f)
                continue;

            float extraPush = useSoftPenetrationCorrection ? Mathf.Max(0f, penetrationExtraPush) : shellOffset;
            totalCorrection += direction.normalized * (distance + extraPush);
            validCorrectionCount++;
        }

        if (useSoftPenetrationCorrection && averageMultiplePenetrationCorrections && validCorrectionCount > 1)
            totalCorrection /= validCorrectionCount;

        if (drawDebug && totalCorrection.sqrMagnitude > 0.000001f)
            Debug.DrawRay(rootPosition, totalCorrection, Color.magenta, 0.05f);

        return totalCorrection;
    }


    private void UpdateHorizontalVelocityFromResolvedMove(Vector3 fromPosition, Vector3 toPosition)
    {
        float dt = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        Vector3 actualDelta = toPosition - fromPosition;
        actualDelta.y = 0f;
        currentVelocity = actualDelta / dt;
    }

    private Vector3 ResolveGroundShapeDelta(Vector3 currentPosition, Vector3 desiredDelta)
    {
        if (!constrainToExistingGroundShape || desiredDelta.sqrMagnitude <= 0.0000001f)
            return desiredDelta;

        if (!useGroundQueryService)
            return desiredDelta;

        if (groundQueryService == null)
            AutoFindGroundQueryService();

        if (groundQueryService == null)
            return desiredDelta;

        bool currentKnown = groundQueryService.TryQueryGround(currentPosition, out GroundQueryResult currentResult);
        bool currentHasGround = currentKnown && currentResult.hasGround;

        Vector3 targetPosition = currentPosition + desiredDelta;
        bool targetKnown = groundQueryService.TryQueryGround(targetPosition, out GroundQueryResult targetResult);
        bool targetHasGround = targetKnown && targetResult.hasGround;

        if (!targetKnown)
            return desiredDelta;

        if (targetHasGround)
            return desiredDelta;

        // 已经在无地面区时，不要把输入锁死；否则被碰撞/边界推出后会回不来。
        if ((!currentKnown || !currentHasGround) && allowEscapeWhenAlreadyOutsideGround)
            return desiredDelta;

        // 先沿完整位移找“最远仍合法的位置”。
        // 旧逻辑直接回退到 X/Z 分量，会在边缘留下明显粘滞：速度还在往外推，输入想往内走时要等速度慢慢反向。
        Vector3 furthestValid = FindClosestValidGroundShapePosition(currentPosition, targetPosition, 18);
        Vector3 furthestDelta = furthestValid - currentPosition;
        furthestDelta.y = 0f;
        if (furthestDelta.sqrMagnitude > 0.0000001f)
            return furthestDelta;

        if (!slideAlongGroundShapeEdge)
            return Vector3.zero;

        Vector3 xOnly = new Vector3(desiredDelta.x, 0f, 0f);
        Vector3 zOnly = new Vector3(0f, 0f, desiredDelta.z);
        bool xValid = xOnly.sqrMagnitude > 0.0000001f && IsGroundShapePositionValid(currentPosition + xOnly);
        bool zValid = zOnly.sqrMagnitude > 0.0000001f && IsGroundShapePositionValid(currentPosition + zOnly);

        if (xValid && zValid)
            return xOnly.sqrMagnitude >= zOnly.sqrMagnitude ? xOnly : zOnly;
        if (xValid)
            return xOnly;
        if (zValid)
            return zOnly;

        // 完全被 ShapeMask 边缘挡住时，清掉本帧外推速度，下一帧输入可以更快反向，不会贴边发粘。
        return Vector3.zero;
    }

    private Vector3 ResolveGroundShapePositionFromPrevious(Vector3 previousPosition, Vector3 proposedPosition)
    {
        if (!constrainToExistingGroundShape)
            return proposedPosition;

        Vector3 delta = proposedPosition - previousPosition;
        if (delta.sqrMagnitude <= 0.0000001f)
            return proposedPosition;

        return previousPosition + ResolveGroundShapeDelta(previousPosition, delta);
    }

    private Vector3 ResolveGroundShapePositionAfterPenetration(
        Vector3 previousPosition,
        Vector3 prePenetrationPosition,
        Vector3 penetrationResolvedPosition)
    {
        if (!constrainToExistingGroundShape)
            return penetrationResolvedPosition;

        bool preValid = IsGroundShapePositionValid(prePenetrationPosition);
        bool resolvedValid = IsGroundShapePositionValid(penetrationResolvedPosition);

        if (resolvedValid)
            return penetrationResolvedPosition;

        // 常见卡边原因：角色本来已经成功往地图内移动，随后穿透修正又把它推出 ShapeMask 边界。
        // 这种情况下不要把整帧移动回滚到 previousPosition，否则角色会贴边进不来；优先保留穿透修正前的合法位置。
        if (preValid)
            return prePenetrationPosition;

        Vector3 closestValid = FindClosestValidGroundShapePosition(previousPosition, penetrationResolvedPosition, 12);
        if (IsGroundShapePositionValid(closestValid))
            return closestValid;

        return ResolveGroundShapePositionFromPrevious(previousPosition, penetrationResolvedPosition);
    }

    private Vector3 FindClosestValidGroundShapePosition(Vector3 fromPosition, Vector3 toPosition, int steps)
    {
        if (!constrainToExistingGroundShape)
            return toPosition;

        steps = Mathf.Clamp(steps, 2, 32);

        // 从目标往回找最近的合法点。这样既不会进入红区，也不会把已经向内移动的位移全部吃掉。
        for (int i = steps; i >= 0; i--)
        {
            float t = i / (float)steps;
            Vector3 candidate = Vector3.Lerp(fromPosition, toPosition, t);
            if (IsGroundShapePositionValid(candidate))
                return candidate;
        }

        return fromPosition;
    }

    private bool IsGroundShapePositionValid(Vector3 position)
    {
        if (!constrainToExistingGroundShape || !useGroundQueryService)
            return true;

        if (groundQueryService == null)
            AutoFindGroundQueryService();

        if (groundQueryService == null)
            return true;

        if (!groundQueryService.TryQueryGround(position, out GroundQueryResult result))
            return true;

        return result.hasGround;
    }

    private Vector3 ResolveMapBoundaryDelta(Vector3 currentPosition, Vector3 desiredDelta)
    {
        if (!constrainToGroundBlockBounds || desiredDelta.sqrMagnitude <= 0.0000001f)
            return desiredDelta;

        Vector3 targetPosition = currentPosition + desiredDelta;
        Vector3 clampedTarget = ResolveMapBoundaryPosition(targetPosition, updateRuntimeState: false);
        return clampedTarget - currentPosition;
    }

    private Vector3 ResolveMapBoundaryPosition(Vector3 position)
    {
        return ResolveMapBoundaryPosition(position, updateRuntimeState: true);
    }

    private Vector3 ResolveMapBoundaryPosition(Vector3 position, bool updateRuntimeState)
    {
        if (!constrainToGroundBlockBounds)
            return position;

        if (boundaryGroundBlock == null)
            AutoFindBoundaryGroundBlock();

        if (boundaryGroundBlock == null)
        {
            if (updateRuntimeState)
                currentMapBoundarySourceRuntime = "No BaseGroundBlock";
            return position;
        }

        Bounds bounds = boundaryGroundBlock.WorldBounds;
        float minX;
        float maxX;
        float minZ;
        float maxZ;
        BuildEffectiveBoundaryRange(bounds, out minX, out maxX, out minZ, out maxZ);

        Vector3 clamped = position;
        clamped.x = Mathf.Clamp(position.x, minX, maxX);
        clamped.z = Mathf.Clamp(position.z, minZ, maxZ);

        bool clampedX = !Mathf.Approximately(clamped.x, position.x);
        bool clampedZ = !Mathf.Approximately(clamped.z, position.z);

        if (updateRuntimeState)
        {
            if (clampedX || clampedZ)
            {
                currentWasClampedByMapBoundary = true;
                currentMapBoundarySourceRuntime = boundaryGroundBlock.name;

                if (stopVelocityWhenBoundaryClamped)
                {
                    // 只清掉“继续往地图外推”的速度。
                    // 如果单位已经被推出边界，但当前速度/输入是在往地图内回，不要把它归零；否则会卡在边界进不来。
                    if (clampedX && IsVelocityPushingOutOfBoundaryAxis(position.x, clamped.x, currentVelocity.x))
                        currentVelocity.x = 0f;
                    if (clampedZ && IsVelocityPushingOutOfBoundaryAxis(position.z, clamped.z, currentVelocity.z))
                        currentVelocity.z = 0f;
                }
            }
            else if (string.IsNullOrWhiteSpace(currentMapBoundarySourceRuntime) || currentMapBoundarySourceRuntime == "-")
            {
                currentMapBoundarySourceRuntime = boundaryGroundBlock.name;
            }
        }

        return clamped;
    }

    private bool IsVelocityPushingOutOfBoundaryAxis(float originalPosition, float clampedPosition, float velocity)
    {
        if (originalPosition < clampedPosition)
            return velocity < 0f;
        if (originalPosition > clampedPosition)
            return velocity > 0f;
        return false;
    }

    private void BuildEffectiveBoundaryRange(Bounds bounds, out float minX, out float maxX, out float minZ, out float maxZ)
    {
        float pad = Mathf.Max(0f, boundaryPadding) + Mathf.Max(0f, boundarySkinWidth);
        float radius = accountCapsuleRadiusInBoundary ? GetBoundaryCapsuleRadius() : 0f;

        Vector3 capsuleOffset = accountCapsuleRadiusInBoundary ? cachedCapsuleCenterFromRoot : Vector3.zero;

        // 约束的是单位根节点，但真正不该越界的是移动胶囊。
        // 胶囊中心 = 根节点 + cachedCapsuleCenterFromRoot，所以根节点范围需要反向扣掉 offset。
        minX = bounds.min.x + pad + radius - capsuleOffset.x;
        maxX = bounds.max.x - pad - radius - capsuleOffset.x;
        minZ = bounds.min.z + pad + radius - capsuleOffset.z;
        maxZ = bounds.max.z - pad - radius - capsuleOffset.z;

        if (minX > maxX)
        {
            float centerX = bounds.center.x - capsuleOffset.x;
            minX = centerX;
            maxX = centerX;
        }

        if (minZ > maxZ)
        {
            float centerZ = bounds.center.z - capsuleOffset.z;
            minZ = centerZ;
            maxZ = centerZ;
        }
    }

    private float GetBoundaryCapsuleRadius()
    {
        if (movementCapsule == null)
            return 0.25f;

        Vector3 lossy = movementCapsule.transform.lossyScale;
        return Mathf.Max(0.01f, movementCapsule.radius * Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z)));
    }

    private Vector3 ResolveGroundPosition(Vector3 position)
    {
        float jumpOffset = driveJumpBy3DPhysics &&
                           (jumpState == JumpRuntimeState.Start || jumpState == JumpRuntimeState.Air)
            ? Mathf.Max(0f, jumpHeightOffset)
            : 0f;

        if (groundFollowMode == UnitGroundFollowMode.LockY || lockYToStartPosition)
        {
            // 兼容旧资源：如果仍然勾着 lockYToStartPosition，则保持旧行为。
            // 但跳跃物理开启时允许在锁定基准高度上叠加跳跃高度，否则角色会被锁死在地面。
            if (groundFollowMode == UnitGroundFollowMode.LockY)
            {
                sampledGroundY = lockedY;
                sampledGroundValid = true;
                position.y = lockedY + jumpOffset;
                currentGroundHasGround = true;
                currentGroundYRuntime = sampledGroundY;
                currentGroundSurfaceTypeRuntime = GroundSurfaceType.Default;
                currentGroundSurfaceMaterialRuntime = null;
                currentGroundIsFallDeathArea = false;
                currentGroundSourceRuntime = "LockY";
                currentJumpHeightRuntime = jumpOffset;
                currentIsGrounded = jumpOffset <= 0.0001f;
                return position;
            }
        }

        if (groundFollowMode == UnitGroundFollowMode.PhysicsGravity)
            return position;

        if (groundFollowMode != UnitGroundFollowMode.RaycastGround)
            return position;

        if (TryResolveGroundByQueryService(position, jumpOffset, out Vector3 queryResolvedPosition, out bool queryHandled))
            return queryResolvedPosition;

        if (queryHandled)
            return queryResolvedPosition;

        if (!TrySampleGround(position, out RaycastHit hit))
        {
            sampledGroundValid = false;
            currentGroundHasGround = false;
            currentGroundYRuntime = rb != null ? rb.position.y : transform.position.y;
            currentGroundSurfaceTypeRuntime = GroundSurfaceType.Default;
            currentGroundSurfaceMaterialRuntime = null;
            currentGroundIsFallDeathArea = true;
            currentGroundSourceRuntime = "PhysicsRaycast:NoGround";
            if (keepHeightWhenNoGround)
                position.y = rb != null ? rb.position.y : transform.position.y;
            return position;
        }

        float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
        if (rejectTooSteepSlope && slopeAngle > maxGroundSlopeAngle)
        {
            // 第一版遇到过陡坡先不爬升，避免被墙面/斜立面抬走。
            sampledGroundValid = false;
            position.y = rb != null ? rb.position.y : transform.position.y;
            return position;
        }

        sampledGroundY = hit.point.y + groundOffset;
        sampledGroundValid = true;
        position.y = sampledGroundY + jumpOffset;
        currentGroundHasGround = true;
        currentGroundYRuntime = sampledGroundY;
        currentGroundSurfaceTypeRuntime = GroundSurfaceType.Default;
        currentGroundSurfaceMaterialRuntime = null;
        currentGroundIsFallDeathArea = false;
        currentGroundSourceRuntime = hit.collider != null ? hit.collider.name : "PhysicsRaycast";
        currentJumpHeightRuntime = jumpOffset;
        currentIsGrounded = jumpOffset <= 0.0001f;
        return position;
    }

    private bool TryResolveGroundByQueryService(Vector3 position, float jumpOffset, out Vector3 resolvedPosition, out bool queryHandled)
    {
        resolvedPosition = position;
        queryHandled = false;

        if (!useGroundQueryService)
            return false;

        if (groundQueryService == null)
            AutoFindGroundQueryService();

        if (groundQueryService == null)
            return false;

        queryHandled = groundQueryService.TryQueryGround(position, out GroundQueryResult result);
        if (!queryHandled)
            return false;

        currentGroundHasGround = result.hasGround;
        currentGroundYRuntime = result.groundY;
        currentGroundSurfaceTypeRuntime = result.surfaceType;
        currentGroundSurfaceMaterialRuntime = result.surfaceMaterial;
        currentGroundIsFallDeathArea = result.isFallDeathArea;
        currentGroundSourceRuntime = string.IsNullOrWhiteSpace(result.sourceName) ? "GroundQueryService" : result.sourceName;

        if (!result.hasGround)
        {
            sampledGroundValid = false;
            if (keepHeightWhenNoGround)
                resolvedPosition.y = rb != null ? rb.position.y : transform.position.y;
            return false;
        }

        sampledGroundY = result.groundY + groundOffset;
        sampledGroundValid = true;
        resolvedPosition.y = sampledGroundY + jumpOffset;
        currentJumpHeightRuntime = jumpOffset;
        currentIsGrounded = jumpOffset <= 0.0001f;
        return true;
    }

    private bool TrySampleGround(Vector3 position, out RaycastHit bestHit)
    {
        bestHit = default;

        Vector3 origin = position + Vector3.up * Mathf.Max(0.01f, groundRayStartHeight);
        float distance = Mathf.Max(0.01f, groundRayStartHeight + groundRayDistance);

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            groundHitBuffer,
            distance,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );

        if (hitCount <= 0)
            return false;

        float bestDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHitBuffer[i];
            Collider col = hit.collider;
            groundHitBuffer[i] = default;

            if (col == null)
                continue;

            if (IsOwnCollider(col))
                continue;

            if (col.isTrigger)
                continue;

            // 过滤几乎垂直的墙面/遮挡立面，避免把墙侧面当成地面。
            float upDot = Vector3.Dot(hit.normal, Vector3.up);
            if (upDot <= 0.15f)
                continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                bestHit = hit;
                found = true;
            }
        }

        return found;
    }

    private bool IsOwnCollider(Collider col)
    {
        if (col == null)
            return false;

        if (movementCapsule != null && col == movementCapsule)
            return true;

        Transform colTransform = col.transform;
        return colTransform == transform || colTransform.IsChildOf(transform);
    }

    private BurdenRuntimeData EvaluateBurden(float burdenValue, float burdenCap)
    {
        burdenCap = Mathf.Max(1f, burdenCap);
        float ratio = Mathf.Clamp01(burdenValue / burdenCap);

        float lightEnd = Mathf.Clamp(lightUpperRatio, 0.01f, 0.95f);
        float mediumEnd = Mathf.Clamp(mediumUpperRatio, lightEnd + 0.01f, 0.98f);
        float heavyEnd = Mathf.Clamp(heavyUpperRatio, mediumEnd + 0.01f, 0.999f);

        BurdenRuntimeData data = new BurdenRuntimeData
        {
            level = BurdenLevel.Light,
            walkPenalty = 0f,
            runPenalty = 0f,
            sharpnessMultiplier = 1f,
            canRun = true,
            canDodge = true,
            canJump = true,
            jumpHeightMultiplier = Mathf.Max(0f, lightBurdenJumpHeightMultiplier)
        };

        if (ratio < lightEnd)
        {
            data.level = BurdenLevel.Light;
            return data;
        }

        if (ratio < mediumEnd)
        {
            float t = Mathf.InverseLerp(lightEnd, mediumEnd, ratio);
            data.level = BurdenLevel.Medium;
            data.walkPenalty = Mathf.Lerp(0f, 0.6f, t);
            data.runPenalty = Mathf.Lerp(0f, 1.0f, t);
            data.sharpnessMultiplier = Mathf.Lerp(1f, 0.9f, t);
            data.jumpHeightMultiplier = Mathf.Lerp(
                Mathf.Max(0f, lightBurdenJumpHeightMultiplier),
                Mathf.Max(0f, mediumBurdenJumpHeightMultiplier),
                t);
            data.canRun = true;
            data.canDodge = true;
            data.canJump = true;
            return data;
        }

        if (ratio < heavyEnd)
        {
            float t = Mathf.InverseLerp(mediumEnd, heavyEnd, ratio);
            data.level = BurdenLevel.Heavy;
            data.walkPenalty = Mathf.Lerp(0.6f, 1.35f, t);
            data.runPenalty = Mathf.Lerp(1.0f, 2.2f, t);
            data.sharpnessMultiplier = Mathf.Lerp(0.82f, 0.65f, t);
            data.jumpHeightMultiplier = Mathf.Lerp(
                Mathf.Max(0f, mediumBurdenJumpHeightMultiplier),
                Mathf.Max(0f, heavyBurdenJumpHeightMultiplier),
                t);
            data.canRun = true;
            data.canDodge = false;
            data.canJump = true;
            return data;
        }

        {
            float t = Mathf.InverseLerp(heavyEnd, 1.0f, ratio);
            data.level = BurdenLevel.Overweight;
            data.walkPenalty = Mathf.Lerp(1.35f, 2.2f, t);
            data.runPenalty = 999f;
            data.sharpnessMultiplier = Mathf.Lerp(0.6f, 0.45f, t);
            data.jumpHeightMultiplier = overweightDisablesJump ? 0f : Mathf.Max(0f, heavyBurdenJumpHeightMultiplier);
            data.canRun = false;
            data.canDodge = false;
            data.canJump = !overweightDisablesJump && data.jumpHeightMultiplier > 0.0001f;
            return data;
        }
    }

    private void UpdateMovementAnimation(bool force = false)
    {
        if (actionControllerOwnsAnimation)
            return;

        if (!driveMovementAnimation)
            return;

        if (dodgeState != DodgeRuntimeState.None)
        {
            PlayAnimationKey(ResolveDodgeKey(dodgeState), false, force);
            return;
        }

        if (jumpState == JumpRuntimeState.Start)
        {
            PlayAnimationKey(ResolveJumpStartKey(), false, force);
            return;
        }

        if (jumpState == JumpRuntimeState.Air)
        {
            PlayAnimationKey(ResolveJumpAirKey(), true, force);
            return;
        }

        if (jumpState == JumpRuntimeState.Land)
        {
            PlayAnimationKey(ResolveJumpLandKey(), false, force);
            return;
        }

        if (!force && Time.time < oneShotAnimationLockedUntil)
            return;

        UnitActionAnimationSlot slot = UnitActionAnimationSlot.Idle;
        string directKey = string.Empty;

        bool hasMoveInput = movementType != UnitMovementType.Immobile && input.sqrMagnitude > 0.0001f;
        if (hasMoveInput)
        {
            bool wantsSneak = IsSneakHeld && !string.IsNullOrWhiteSpace(sneakAnimationKey);
            bool wantsRun = !wantsSneak && IsRunHeld && canRunNow;

            if (wantsSneak)
            {
                directKey = sneakAnimationKey;
            }
            else if (wantsRun)
            {
                slot = UnitActionAnimationSlot.Run;

                // UD 漏填或把 Run Key 覆盖成 Walk Key 时，至少不要让 AI 奔跑永远停在 move。
                // 项目当前 Spine 奔跑动画约定为 run_heavy。
                if (string.IsNullOrWhiteSpace(runAnimationKey) || string.Equals(runAnimationKey, walkAnimationKey, System.StringComparison.Ordinal))
                    directKey = "run_heavy";
            }
            else
            {
                slot = UnitActionAnimationSlot.Walk;
            }
        }

        string key = string.IsNullOrWhiteSpace(directKey) ? GetAnimationKey(slot) : directKey;
        if (string.IsNullOrWhiteSpace(key) && slot == UnitActionAnimationSlot.Run)
            key = "run_heavy";
        if (string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(walkAnimationKey))
            key = walkAnimationKey;

        PlayAnimationKey(key, true, force);
    }

    public string ResolveActionAnimationKey(UnitActionAnimationSlot slot)
    {
        return NormalizeAnimationKeyForProject(GetAnimationKey(slot));
    }

    private string NormalizeAnimationKeyForProject(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        // 当前 Axia Spine 资源的动作名是 Idle / Move / Run，Spine 动画名区分大小写。
        // 旧版移动脚本默认 idle / walk / run，会导致 4.3 升级后请求不到真实动作。
        if (string.Equals(key, "idle", System.StringComparison.OrdinalIgnoreCase))
            return "Idle";
        if (string.Equals(key, "walk", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "move", System.StringComparison.OrdinalIgnoreCase))
            return "Move";
        // Run 动画在 Spine 里被整理进了 "Run" 文件夹分组，真实动画名带前缀是 "Run/Run"
        // （武器覆盖版本同理是 "Run/Run_Sword"，不是 "Run_Sword"）。之前少了这个前缀，
        // Spine 找不到叫 "Run" 的动画，会静默保持在上一个成功播放的动画（走路）上不切换。
        if (string.Equals(key, "run", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "run_heavy", System.StringComparison.OrdinalIgnoreCase))
            return "Run/Run";
        if (string.Equals(key, "sneak", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "stealth", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "crouch", System.StringComparison.OrdinalIgnoreCase))
            return "Sneak";

        return key;
    }

    private string GetAnimationKey(UnitActionAnimationSlot slot)
    {
        switch (slot)
        {
            case UnitActionAnimationSlot.Idle: return ResolveLocomotionKey(idleAnimationKey, ov => ov.idle);
            case UnitActionAnimationSlot.Walk: return ResolveLocomotionKey(walkAnimationKey, ov => ov.walk);
            case UnitActionAnimationSlot.Run: return ResolveLocomotionKey(runAnimationKey, ov => ov.run);
            case UnitActionAnimationSlot.Sneak: return ResolveLocomotionKey(sneakAnimationKey, ov => ov.crouch);
            case UnitActionAnimationSlot.Jump: return string.IsNullOrWhiteSpace(jumpAnimationKey) ? ResolveJumpStartKey() : jumpAnimationKey;
            case UnitActionAnimationSlot.JumpStart: return ResolveJumpStartKey();
            case UnitActionAnimationSlot.JumpAir: return ResolveJumpAirKey();
            case UnitActionAnimationSlot.JumpLand: return ResolveJumpLandKey();
            case UnitActionAnimationSlot.Attack: return attackAnimationKey;
            case UnitActionAnimationSlot.Hit: return hitAnimationKey;
            case UnitActionAnimationSlot.Dodge: return dodgeAnimationKey;
            case UnitActionAnimationSlot.DodgeForward: return ResolveDodgeKey(DodgeRuntimeState.Forward);
            case UnitActionAnimationSlot.DodgeBack: return ResolveDodgeKey(DodgeRuntimeState.Back);
            case UnitActionAnimationSlot.Death: return deathAnimationKey;
            default: return string.Empty;
        }
    }

    // WeaponCombatModule.locomotionOverride lets a weapon (e.g. a greatsword) swap the base
    // locomotion key for its own Spine animation (e.g. "run" -> "Run_Sword") without touching
    // the default keys used when unarmed or holding a weapon with no override set.
    private string ResolveLocomotionKey(string defaultKey, System.Func<LocoAnimOverride, string> selector)
    {
        WeaponCombatModule module = ResolveActionModuleRuntime()?.CurrentModule;
        if (module == null || module.locomotionOverride == null)
            return defaultKey;

        return module.locomotionOverride.Resolve(defaultKey, selector(module.locomotionOverride));
    }

    private void PlayAnimationKey(string key, bool loop, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        key = NormalizeAnimationKeyForProject(key);

        // Hot path: running/walking calls this every Update. If our own requested key is unchanged,
        // do not scan children and do not reflect into Spine just to rediscover the same state.
        if (!force && currentAnimationKey == key)
        {
            currentAnimationKeyRuntime = key;
            return;
        }

        AutoFindAnimationTargets();

        string actualSpineAnimation = force ? GetCurrentSpineAnimationName() : string.Empty;
        currentActualSpineAnimationRuntime = actualSpineAnimation;

        bool hasSpineTargets = spineAnimationTargets.Count > 0 || spineAnimationComponent != null;
        bool spineAlreadyOnKey = hasSpineTargets && !string.IsNullOrEmpty(actualSpineAnimation) && string.Equals(actualSpineAnimation, key, System.StringComparison.Ordinal);

        if (!force && spineAlreadyOnKey)
        {
            currentAnimationKey = key;
            currentAnimationKeyRuntime = key;
            return;
        }

        bool played = false;

        if (hasSpineTargets)
            played = TryPlaySpineAnimation(key, loop);

        if (!played && targetAnimator != null)
        {
            if (movementAnimationFade > 0f)
                targetAnimator.CrossFadeInFixedTime(key, movementAnimationFade);
            else
                targetAnimator.Play(key);

            played = true;
        }

        lastAnimationRequest = key;
        lastAnimationPlaySucceeded = played;

        if (played)
        {
            currentAnimationKey = key;
            currentAnimationKeyRuntime = key;
            currentActualSpineAnimationRuntime = key;
        }
    }

    private bool TryPlaySpineAnimation(string key, bool loop)
    {
        RefreshSpineAnimationTargets();

        bool playedAny = false;

        if (spineAnimationComponent != null)
            playedAny |= TryPlaySpineAnimationOnComponent(spineAnimationComponent, key, loop);

        if (driveAllChildSpineAnimations)
        {
            for (int i = 0; i < spineAnimationTargets.Count; i++)
            {
                Component target = spineAnimationTargets[i];
                if (target == null || target == spineAnimationComponent)
                    continue;

                playedAny |= TryPlaySpineAnimationOnComponent(target, key, loop);
            }
        }

        return playedAny;
    }

    private bool TryPlaySpineAnimationOnComponent(Component target, string key, bool loop)
    {
        if (target == null || string.IsNullOrWhiteSpace(key))
            return false;

        try
        {
            object animationState;
            if (!TryGetSpineAnimationState(target, out animationState) || animationState == null)
                return false;

            MethodInfo setAnimation = FindSpineSetAnimationMethod(animationState.GetType());
            if (setAnimation == null)
                return false;

            spineSetAnimationArgs[0] = 0;
            spineSetAnimationArgs[1] = key;
            spineSetAnimationArgs[2] = loop;
            setAnimation.Invoke(animationState, spineSetAnimationArgs);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetCurrentSpineAnimationName()
    {
        RefreshSpineAnimationTargets();

        string assigned = GetCurrentSpineAnimationName(spineAnimationComponent);
        if (!string.IsNullOrWhiteSpace(assigned))
            return assigned;

        for (int i = 0; i < spineAnimationTargets.Count; i++)
        {
            string name = GetCurrentSpineAnimationName(spineAnimationTargets[i]);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return string.Empty;
    }

    private string GetCurrentSpineAnimationName(Component target)
    {
        if (target == null)
            return string.Empty;

        try
        {
            object animationState;
            if (!TryGetSpineAnimationState(target, out animationState) || animationState == null)
                return string.Empty;

            object trackEntry = TryGetSpineCurrentTrackEntry(animationState, 0);
            if (trackEntry == null)
                return string.Empty;

            object animation = GetMemberValue(trackEntry, "Animation") ?? GetMemberValue(trackEntry, "animation");
            if (animation == null)
                return string.Empty;

            object name = GetMemberValue(animation, "Name") ?? GetMemberValue(animation, "name");
            return name as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool TryGetSpineAnimationState(Component target, out object animationState)
    {
        animationState = null;
        if (target == null)
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        System.Type type = target.GetType();

        animationState = GetMemberValue(type, target, "AnimationState", flags)
                      ?? GetMemberValue(type, target, "State", flags)
                      ?? GetMemberValue(type, target, "state", flags)
                      ?? GetMemberValue(type, target, "animationState", flags);

        return animationState != null;
    }

    private MethodInfo FindSpineSetAnimationMethod(System.Type animationStateType)
    {
        if (animationStateType == null)
            return null;

        if (spineSetAnimationMethodCache.TryGetValue(animationStateType, out MethodInfo cached))
            return cached;

        MethodInfo exact = animationStateType.GetMethod(
            "SetAnimation",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(int), typeof(string), typeof(bool) },
            null);

        if (exact != null)
        {
            spineSetAnimationMethodCache[animationStateType] = exact;
            return exact;
        }

        MethodInfo[] methods = animationStateType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method == null || method.Name != "SetAnimation")
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 3)
                continue;

            if (parameters[0].ParameterType == typeof(int) &&
                parameters[1].ParameterType == typeof(string) &&
                parameters[2].ParameterType == typeof(bool))
            {
                spineSetAnimationMethodCache[animationStateType] = method;
                return method;
            }
        }

        spineSetAnimationMethodCache[animationStateType] = null;
        return null;
    }

    private object TryGetSpineCurrentTrackEntry(object animationState, int trackIndex)
    {
        if (animationState == null)
            return null;

        System.Type stateType = animationState.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        string[] methodNames = { "GetCurrent", "GetCurrentTrack", "GetCurrentTrackEntry" };
        for (int i = 0; i < methodNames.Length; i++)
        {
            MethodInfo method = stateType.GetMethod(
                methodNames[i],
                flags,
                null,
                new[] { typeof(int) },
                null);

            if (method == null)
                continue;

            object value = method.Invoke(animationState, new object[] { trackIndex });
            if (value != null)
                return value;
        }

        object tracks = GetMemberValue(stateType, animationState, "Tracks", flags)
                     ?? GetMemberValue(stateType, animationState, "tracks", flags);

        if (tracks is System.Collections.IList list && trackIndex >= 0 && trackIndex < list.Count)
            return list[trackIndex];

        return null;
    }

    private object GetMemberValue(object source, string memberName)
    {
        if (source == null)
            return null;

        return GetMemberValue(source.GetType(), source, memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private object GetMemberValue(System.Type type, object source, string memberName, BindingFlags flags)
    {
        if (type == null || source == null || string.IsNullOrWhiteSpace(memberName))
            return null;

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.CanRead)
            return property.GetValue(source, null);

        FieldInfo field = type.GetField(memberName, flags);
        if (field != null)
            return field.GetValue(source);

        return null;
    }


    private void RefreshBurdenDebugView()
    {
        BurdenRuntimeData burden = EvaluateBurden(CurrentBurden, MaxBurden);
        currentBurdenLevel = burden.level;
        canRunNow = movementType != UnitMovementType.Immobile && burden.canRun;
        canDodgeNow = movementType != UnitMovementType.Immobile && burden.canDodge;
        canJumpNow = movementType != UnitMovementType.Immobile && burden.canJump;
        canMoveNow = movementType != UnitMovementType.Immobile;
        currentBaseJumpHeightRuntime = Mathf.Max(0.05f, jumpHeight);
        currentJumpHeightMultiplierRuntime = Mathf.Max(0f, burden.jumpHeightMultiplier);
        currentEffectiveJumpHeightRuntime = canJumpNow ? currentBaseJumpHeightRuntime * currentJumpHeightMultiplierRuntime : 0f;
        currentBurdenPercent01 = BurdenPercent01;
        currentBurdenPercent100 = BurdenPercent100;
        currentBurdenValueRuntime = CurrentBurden;
        maxBurdenValueRuntime = MaxBurden;
    }

    public void SetCurrentBurden(float value)
    {
        currentBurden = Mathf.Max(0f, value);
    }

    public void AddBurden(float value)
    {
        SetCurrentBurden(currentBurden + value);
    }

    public void RemoveBurden(float value)
    {
        SetCurrentBurden(currentBurden - value);
    }

    public void SetMaxBurden(float value)
    {
        maxBurden = Mathf.Max(1f, value);
    }

    public void AddMaxBurden(float value)
    {
        SetMaxBurden(maxBurden + value);
    }

    public void SetBurdenValues(float newCurrentBurden, float newMaxBurden)
    {
        currentBurden = Mathf.Max(0f, newCurrentBurden);
        maxBurden = Mathf.Max(1f, newMaxBurden);
    }

    public void StopImmediately()
    {
        input = Vector2.zero;
        currentInput = Vector2.zero;
        externalRunHeld = false;
        sameDirectionTimer = 0f;
        lastNonZeroInput = Vector2.zero;
        currentVelocity = Vector3.zero;
        CancelActionState();
        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
        }
    }


#if UNITY_EDITOR
    private void OnValidate()
    {
        if (movementCapsule == null)
            movementCapsule = FindMovementCapsule();

        if (!Application.isPlaying && rb == null)
            rb = GetComponent<Rigidbody>();

        AutoFindAnimationTargets();
        AutoFindGroundShadowRoot();
        if (!Application.isPlaying)
            CacheGroundShadowDefaults();
        CacheCapsuleOffsetFromRoot();
        AutoSwitchToExternalTerrainMotorIfAvailable();
    }
#endif

}