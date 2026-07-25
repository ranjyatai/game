using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地形装饰物前景遮挡触发器：Camera Area Sweep 版。
///
/// 核心规则：
/// 1. BackTrigger 不再决定前后关系，只作为脚本挂点 / 旧结构兼容。
/// 2. 候选单位由当前遮挡物 VisualRoot 的 Renderer Bounds 外扩主动扫描得到。
/// 3. 单位身体、武器、衣服、头发、挂件作为一个整体 Renderer 点云提供屏幕位置。
/// 4. 普通判定仍以 UnitBody 逻辑深度保证稳定；极限边缘进入会额外使用角色真实可见部件的 Main Camera 像素射线深度。
/// 5. 从 Camera 到 logicalSample 做射线深度测试：如果在到达单位逻辑点之前，先命中当前 VisualRoot 下的真实 Collider，则该点被当前遮挡物挡住。
/// 6. 任意有效点被挡住，即开启 FrontOccluderRoot 并通知 IOcclusionStateReceiver。
///
/// 这个版本保留旧 Builder 需要的 ConfigureFootprint/public 字段，但最终判定不再依赖 BackTrigger / footprint 前缘线。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkyPrisonTerrainDecorationFrontOccluderTrigger : MonoBehaviour
{
    [Header("Version / Sanity Check")]
    [SerializeField]
    private string scriptVersion = "V59 - 2026-06-10 - reject self/proxy candidates and require real unit root";

    [Header("Target")]
    [Tooltip("需要被开关的前景遮挡代理根节点，通常是 RuleRoot/FrontOccluderRoot。")]
    public GameObject frontOccluderRoot;

    [Tooltip("为空时，自动从当前装饰物实例祖先下寻找 FrontOccluderRoot。")]
    public bool autoFindFrontOccluderRoot = true;

    [Tooltip("允许进入候选扫描的单位层。推荐只包含 UnitBody。")]
    public LayerMask targetLayers = ~0;

    [Tooltip("V59：候选扫描时强制排除当前装饰物自身的 VisualRoot / CollisionRoot / FrontOccluderRoot / Trigger 节点，避免前景代理体被误当成单位候选。")]
    public bool rejectSelfAndProxyCandidates = true;

    [Tooltip("V59：候选 Collider 必须能解析到真正的单位根或 IOcclusionStateReceiver。关闭后会退回旧版 parent/transform fallback，不推荐。")]
    public bool requireRealUnitCandidateRoot = true;

    [Tooltip("进入遮挡状态时，通知单位身上的 IOcclusionStateReceiver。")]
    public bool notifyOcclusionStateReceivers = true;

    [Header("Camera Area Sweep")]
    [Tooltip("用于前后深度测试的摄像机。为空时使用 Camera.main。")]
    public Camera sourceCamera;

    [Tooltip("启用后，使用 Camera Area Sweep 作为唯一遮挡成立逻辑。")]
    public bool useCameraAreaSweep = true;

    [Header("Commercial Occlusion Priority")]
    [Tooltip("商品级遮挡优先级：上下高度关系 > 前后深度关系 > 主相机像素交集。像素交集只允许补边缘，不允许推翻高度/前后大关系。")]
    public bool useHeightDepthPixelPriority = true;

    [Tooltip("启用后，普通侧向遮挡必须先通过上下高度关系：当前物体的实体高度范围必须和单位视觉高度范围存在重叠。头顶遮挡仍由 Overhead Height Occlusion 优先处理。")]
    public bool useSideOcclusionHeightGate = true;

    [Tooltip("侧向高度关系判定使用单位 Renderer Bounds。关闭时只使用 UnitBody / 逻辑锚点高度。")]
    public bool useUnitRendererBoundsForSideHeightGate = true;

    [Tooltip("侧向高度关系判定的上下外扩。用于避免脚尖/箱边 1 像素高度误差导致遮挡抖动。")]
    public float sideOcclusionHeightPadding = 0.08f;

    [Tooltip("当前物体和单位视觉高度范围至少要有这么多重叠，才允许进入普通前后深度遮挡。")]
    public float minSideOcclusionHeightOverlap = 0.02f;

    [Tooltip("Raycast 检测层。应包含当前装饰物 VisualRoot 下真实模型 / MeshCollider / BoxCollider 所在层，例如 World3D。")]
    public LayerMask raycastLayers = ~0;

    [Tooltip("射线检测 Trigger 的方式。推荐 Ignore，避免 BackTrigger / FrontTrigger 污染深度。")]
    public QueryTriggerInteraction raycastTriggerInteraction = QueryTriggerInteraction.Ignore;

    [Tooltip("射线到达单位逻辑点前的安全余量。")]
    public float depthEpsilon = 0.015f;

    [Tooltip("任意真实采样点被当前遮挡物挡住即可打开局部遮挡。默认 1，适合长武器尖端。")]
    public int minBlockedSampleCount = 1;

    [Header("Occluder Content")]
    [Tooltip("当前遮挡物的真实视觉/碰撞内容根。推荐拖当前实例 / VisualRoot。")]
    public Transform occluderContentRoot;

    [Tooltip("为空时自动寻找最近的 VisualRoot。")]
    public bool autoFindNearestVisualRoot = true;

    [Tooltip("当前遮挡物自己的碰撞体根。推荐使用当前实例 / CollisionRoot。为空时会自动寻找最近的 CollisionRoot。")]
    public Transform occluderColliderRoot;

    [Tooltip("为空时自动寻找最近的 CollisionRoot，并只使用这个根下面的非 Trigger Collider 做精确自判定。")]
    public bool autoFindNearestCollisionRoot = true;

    [Header("Self Occluder Test")]
    [Tooltip("当前遮挡物只检测自己 VisualRoot 下的非 Trigger Collider，不让其他物体抢第一命中。")]
    public bool useSelfColliderRayDepth = true;

    [Tooltip("已废弃：Renderer Bounds 不再参与最终遮挡判定。保留字段仅为兼容旧 Inspector 序列化。")]
    public bool useSelfRendererBoundsRayDepth = false;

    [Tooltip("已废弃：Renderer Bounds Fallback 已退出最终判定。保留字段仅为兼容旧 Inspector 序列化。")]
    public float selfRendererBoundsPadding = 0.0f;

    [Tooltip("优先使用当前 VisualRoot 下真实 Mesh 三角面做自己遮挡判定。比 Renderer Bounds 精确得多。Mesh 不可读时会自动跳过并回退 Bounds。")]
    public bool useSelfVisualMeshTriangleRayDepth = true;

    [Tooltip("每个遮挡物 Renderer 最多检测的三角面数量。0 表示不限制。数值越大越准但越耗 CPU。")]
    public int maxSelfVisualMeshTrianglesPerRenderer = 20000;

    [Tooltip("Mesh 三角面检测命中后是否立刻停止当前采样点检测。开启性能更好；关闭会找最近命中点。")]
    public bool stopAtFirstSelfMeshHit = false;

    [Tooltip("只把 OtherHit 作为调试信息，不作为遮挡失败依据。")]
    public bool debugGlobalOtherHitOnly = true;

    [Header("Candidate Wake Scan")]
    [Tooltip("主动扫描附近 UnitBody 候选，不要求单位进入 BackTrigger。")]
    public bool activeWakeScan = true;

    [Tooltip("候选扫描范围优先使用当前遮挡物 VisualRoot 下真实 Renderer Bounds。")]
    public bool useVisualRootBoundsForWakeScan = true;

    [Tooltip("VisualRoot Bounds 作为候选扫描范围时的额外外扩。只影响是否纳入计算，不影响最终前后关系。")]
    public Vector3 visualRootWakeExtraPadding = new Vector3(3f, 2f, 3f);

    [Tooltip("主动扫描最小间隔。0 表示每帧。")]
    public float wakeScanInterval = 0.03f;

    [Tooltip("测试用：直接扫描场景内所有目标层 Collider。量产不建议一直开启。")]
    public bool scanAllTargetLayerCollidersForDebug = false;

    [Tooltip("全局扫描间隔。")]
    public float globalCandidateScanInterval = 0.10f;

    [Tooltip("如果 VisualRoot Bounds 不可用，是否允许使用当前 Trigger Bounds 作为候选 fallback。默认关闭。")]
    public bool allowBackTriggerWakeFallback = false;

    [Tooltip("Trigger Bounds fallback 外扩。")]
    public Vector3 wakeScanExtraPadding = new Vector3(2f, 1f, 2f);

    [Tooltip("主动扫描缓存大小。")]
    public int wakeScanBufferSize = 128;

    [Header("Unit Sampling")]
    [Tooltip("优先使用可读 Mesh 顶点采样。不可读时自动回退 Renderer Bounds。")]
    public bool preferUnitMeshVertices = true;

    [Tooltip("每个 Renderer 最多采样的 Mesh 顶点数量。")]
    public int maxUnitMeshSamplesPerRenderer = 96;

    [Tooltip("Mesh 不可读或无 Mesh 时，回退到 Renderer Bounds 采样。")]
    public bool fallbackToRendererBoundsSamples = true;

    [Tooltip("无论是否使用 Mesh 顶点采样，都额外加入 Renderer Bounds 的中心、六向端点和 8 角点。用于稳定捕捉视觉外轮廓：武器尖端、头发、衣服边缘、舞台投影等。只贡献视觉宽度/高度，深度仍会统一投回 UnitBody。")]
    public bool alwaysAddRendererBoundsFootprintSamples = true;

    [Tooltip("Mesh 顶点采样以外，最少保证每个 Renderer 都贡献 Bounds 外轮廓。开启后不再依赖 Mesh 顶点是否刚好采到尖端。")]
    public bool forceRendererBoundsAsVisualFootprint = true;

    [Tooltip("是否包含 inactive 的单位 Renderer。通常关闭。")]
    public bool includeInactiveUnitRenderers = false;

    [Tooltip("根据名称排除不应参与遮挡采样的 Renderer。")]
    public bool useNameExclusion = true;

    [Tooltip("即使命中排除关键词，只要路径包含这些词，就作为视觉占位外轮廓保留。用于 StageProjection / GroundShadow / UnitShadow / FootShadow / 投影等。它们只参与视觉外轮廓，不参与深度。")]
    public string[] forceIncludeVisualFootprintRendererNameKeywords = new string[]
    {
        "StageProjection", "Stage Projection", "GroundShadow", "Ground_Shadow", "UnitShadow", "FootShadow", "Projection", "ShadowPlane", "ContactShadow", "投影", "影子", "舞台投影"
    };

    [Tooltip("材质 / Shader 白名单。用于保证舞台投影即使名字没有 Shadow / Projection 关键词，也会作为角色视觉占位外轮廓参与遮挡开关判定。")]
    public string[] forceIncludeVisualFootprintShaderKeywords = new string[]
    {
        "Custom/FrontOccluder/Shadows/Projector", "FrontOccluder/Shadows", "StageShadow", "ContactShadow"
    };

    [Header("Projection / Shadow Footprint Occlusion")]
    [Tooltip("开启后，舞台投影 / 接地影子的屏幕占位会参与投影相关诊断。默认不允许它单独开启角色本体遮挡，避免背后物体因为投影而压住角色。")]
    public bool useProjectionFootprintAsOcclusionParticipant = false;

    [Tooltip("投影参与遮挡开关时使用投影自身的世界位置 / 屏幕像素，而不是强行投回 UnitBody 唯一深度。这样投影可以在边缘处提前正式开启遮挡关系。")]
    public bool projectionFootprintUsesOwnWorldDepth = true;

    [Tooltip("投影占位采样点被当前物体挡住多少个才算命中。默认 1，仅用于调试或显式允许投影开启本体遮挡时。")]
    public int projectionFootprintMinBlockedSampleCount = 1;

    [Tooltip("危险开关：允许投影占位单独开启角色本体遮挡。默认关闭。商品级稳定规则下，投影裁切应由 ShadowOccluderMask 像素级处理，不应反向驱动角色本体被遮挡。")]
    public bool projectionFootprintMayOpenCharacterOcclusion = false;

    [Tooltip("如果允许投影开启角色本体遮挡，是否仍要求 UnitBody 逻辑锚点已经处于当前物体的相机后方体积内。默认开启，防止背后物体越权。")]
    public bool projectionFootprintRequiresBodyBehindVolume = true;

    [Tooltip("绘制投影占位参与遮挡开关的调试线。")]
    public bool debugDrawProjectionFootprintOcclusion = false;

    [Tooltip("排除名称关键词。注意 Shadow/Projection 可被上面的视觉外轮廓白名单救回。")]
    public string[] excludedUnitRendererNameKeywords = new string[]
    {
        "Outline", "Proxy", "Occluder", "Trigger", "Collider", "Collision",
        "Overhead", "Health", "UI", "HP", "Bar", "Gizmo", "Debug"
    };

    [Header("Logical Depth Anchor")]
    [Tooltip("视觉采样点只取屏幕位置，真实深度投回单位逻辑深度平面。")]
    public bool useUnitLogicalDepthPlane = true;

    [Tooltip("角色只有一个真实前后深度：所有武器/衣服/头发/身体采样点只贡献左右宽度/画面位置，前后深度统一使用 UnitBody / UnitPhysicsProbe 的逻辑锚点。")]
    public bool useVisualWidthWithUnifiedUnitDepth = true;

    [Tooltip("统一深度采样点的高度是否使用视觉采样点自身高度。开启后武器/头发等仍能贡献垂直位置；关闭则高度也使用 UnitBody。")]
    public bool useVisualSampleHeightForUnifiedDepth = true;

    [Tooltip("旧开关，仅作为兼容保留。当前推荐关闭：不要让 Billboard 视觉采样点自己的世界深度决定前后关系。")]
    public bool useVisualSampleWorldPositionForSensitiveSweep = false;

    [Tooltip("旧开关，仅作为兼容保留。当前推荐关闭：视觉采样点可以贡献左右宽度，但不应独自用自己的 Billboard 深度完成后方确认。")]
    public bool allowVisualSampleInsideBehindVolumeToConfirm = false;

    [Tooltip("逻辑深度锚点优先找 UnitBodyCollider / UnitBody / UnitPhysicsProbe / BodyCollider 等。")]
    public bool preferLogicalBodyCollider = true;

    [Tooltip("没有找到明确身体锚点时，退回 UnitRoot.position。")]
    public bool fallbackToUnitRootPosition = true;

    [Header("Camera Facing Behind Volume")]
    [Tooltip("启用后，视觉采样点命中当前物体只是第一步；UnitBody 逻辑锚点还必须进入当前物体由相机可见最外侧凸点生成的后方体积，才允许打开遮挡代理。")]
    public bool requireUnitBodyInsideCameraFacingBehindVolume = true;

    [Tooltip("后方体积左右边界外扩。只影响 UnitBody 站位确认，不影响视觉采样敏感度。")]
    public float behindVolumeSideMargin = 0.08f;

    [Tooltip("UnitBody 必须越过当前相机侧前沿线这么多深度，才算真正进入物体后方。")]
    public float behindVolumeFrontMargin = 0.08f;

    [Tooltip("后方体积高度上下外扩。")]
    public float behindVolumeHeightPadding = 0.20f;

    [Tooltip("是否使用遮挡物 VisualRoot 高度范围约束 UnitBody。")]
    public bool useBehindVolumeHeightCheck = true;

    [Tooltip("绘制相机朝向后方体积的调试线。")]
    public bool debugDrawBehindVolume = false;

    [Header("Overhead Height Occlusion")]
    [Tooltip("启用后，如果当前遮挡物位于 UnitBody / 角色头顶上方，并且 UnitBody 的水平站位落入当前遮挡物水平覆盖范围，则直接打开遮挡代理。用于桥、上层平台、叠放高物体等从头顶压住角色的情况。")]
    public bool useOverheadHeightOcclusion = true;

    [Tooltip("优先使用单位 Renderer Bounds 的顶部作为角色头顶高度；关闭时只使用 UnitBody 逻辑锚点高度。")]
    public bool useUnitRendererTopForOverhead = true;

    [Tooltip("当前物体底面至少要高于角色头顶这么多，才认为是头顶物体。负值可以让刚贴近头顶的物体也触发。")]
    public float overheadBottomAboveUnitTopMargin = -0.02f;

    [Tooltip("头顶水平覆盖判定的 XZ 外扩。数值越大，头顶遮挡越容易触发。")]
    public float overheadHorizontalPadding = 0.10f;

    [Tooltip("启用后，头顶遮挡不再只要求世界 XZ 正下方，而是使用 GameplayCamera 视角下的投影重叠判断。适合从镜头看已经被上层物体盖住、但世界 XZ 并非正下方的情况。")]
    public bool useCameraProjectedOverheadOcclusion = true;

    [Tooltip("头顶摄像机投影重叠的屏幕/Viewport 外扩。0.01 约等于屏幕宽高的 1%。")]
    public float overheadCameraProjectionPadding = 0.015f;

    [Tooltip("摄像机投影头顶遮挡的候选扫描会额外全局检查 TargetLayers Collider，并用屏幕投影先筛掉无关单位。开启后可覆盖‘镜头上重叠但 XZ 不在正下方’的情况。")]
    public bool useCameraProjectedOverheadCandidateScan = true;

    [Tooltip("摄像机投影头顶判断失败时，是否继续使用旧的 XZ 正下方头顶判定作为 fallback。")]
    public bool allowWorldXZOverheadFallback = true;

    [Tooltip("头顶遮挡专用候选扫描。普通 VisualRoot Bounds 扫描会受高度影响，头顶物体在角色上方时可能扫不到 UnitBody；开启后用遮挡物 XZ 覆盖范围向下扫描 UnitBody。")]
    public bool useOverheadCandidateWakeScan = true;

    [Tooltip("头顶候选扫描向下扩展距离。需要覆盖角色所在地面高度。")]
    public float overheadCandidateScanDownDistance = 30f;

    [Tooltip("头顶候选扫描向上扩展距离。通常保持较小即可。")]
    public float overheadCandidateScanUpDistance = 2f;

    [Tooltip("绘制头顶高度遮挡的水平覆盖范围与高度参考线。")]
    public bool debugDrawOverheadOcclusion = false;

    [Header("Dynamic Occluder Refresh")]
    [Tooltip("启用后，可推动 / 会倒下 / Rigidbody 驱动的遮挡物会在 Transform 或 Rigidbody 状态变化时低频刷新当前 Collider / Renderer 缓存与 Behind Volume 支撑点。静态物体仍然走缓存。")]
    public bool useDynamicOccluderRefresh = true;

    [Tooltip("移动中的动态遮挡物刷新间隔。0 表示每帧刷新；建议 0.05~0.10，避免推箱子/倒下时卡顿。")]
    public float dynamicRefreshWhileMovingInterval = 0.08f;

    [Tooltip("位置变化超过该阈值后，标记遮挡物几何为 Dirty。")]
    public float dynamicPositionDirtyThreshold = 0.01f;

    [Tooltip("旋转变化超过该角度后，标记遮挡物几何为 Dirty。")]
    public float dynamicRotationDirtyThreshold = 0.5f;

    [Tooltip("缩放变化超过该阈值后，标记遮挡物几何为 Dirty。")]
    public float dynamicScaleDirtyThreshold = 0.001f;

    [Tooltip("如果遮挡物带 Rigidbody，刚从运动变为 Sleep 时强制刷新一次，锁定最终倒下/停止姿态。")]
    public bool refreshOnceWhenRigidbodySleeps = true;

    [Tooltip("动态刷新时是否清空候选。默认关闭，避免移动物体刷新瞬间造成遮挡闪烁；候选仍会在下一次扫描中更新。")]
    public bool clearCandidatesOnDynamicRefresh = false;


    [Header("Motion Continuity / Run Stability")]
    [Tooltip("启用后，高速移动单位会用上一帧 UnitBody 逻辑锚点到当前锚点的运动扫掠进行遮挡补偿。不是暴力刷新，而是让遮挡判定具备时间连续性。")]
    public bool useMotionSweptOcclusion = true;

    [Tooltip("UnitBody 逻辑锚点单帧位移超过该值时，启用 swept anchor 检查。数值过小会让走路也参与；过大则奔跑边界仍可能漏判。")]
    public float motionSweepMinDistance = 0.08f;

    [Tooltip("运动扫掠时同时检查上一帧与中点。开启后奔跑穿过边缘更稳定。")]
    public bool useMotionSweepMidpoint = true;

    [Tooltip("遮挡从 true 变为 false 时保留的宽限帧数。用于防止奔跑贴边时因为一帧采样失败而闪烁。0 表示立即释放。")]
    public int releaseHysteresisFrames = 2;

    [Header("Commercial Pixel Exit Guard")]
    [Tooltip("商业级退出保护：进入遮挡保持敏感；退出遮挡必须更保守，避免单位视觉边缘还在物体背后时提前跳到物体前面。")]
    public bool useCommercialPixelExitGuard = true;

    [Tooltip("普通判定已经为 false 后，额外保持遮挡的帧数。建议 4~8。")]
    public int commercialExitGuardFrames = 6;

    [Tooltip("退出前如果当前激活的 OutlineProxy_*_Occluded 在摄像机画面上仍与当前遮挡物投影重叠，则继续保持遮挡。")]
    public bool keepWhileActiveOccludedProxyOverlapsOccluder = true;

    [Tooltip("摄像机投影重叠退出保护的 Viewport 外扩。")]
    public float commercialExitProjectionPadding = 0.008f;

    [Tooltip("如果单位的 SpinePartOcclusionSampler 仍报告任意部位被遮挡，则继续保持遮挡。")]
    public bool keepWhileSpinePartSamplerBlocked = true;

    [Tooltip("绘制商业级退出保护的调试连线。")]
    public bool debugDrawCommercialExitGuard = false;

    [Header("Commercial One Pixel Enter Guard")]
    [Tooltip("商业级进入保护：普通采样没有命中时，如果单位视觉轮廓与当前遮挡物在主相机画面中已经有哪怕极小重叠，并且该重叠像素从主相机看确实先命中当前遮挡物，则立即开启遮挡关系。用于修复极限边缘 1 像素遮挡时开关抽搐。")]
    public bool useOnePixelEnterGuard = true;

    [Tooltip("进入保护使用的 Viewport 外扩。数值越大越敏感。0.003~0.008 通常足够；过大会造成提前遮挡。")]
    public float onePixelEnterProjectionPadding = 0.004f;

    [Tooltip("Viewport 重叠至少达到多少屏幕像素才尝试进入保护。1 表示真正的一像素级别。")]
    public float onePixelEnterMinOverlapPixels = 1.0f;

    [Tooltip("进入保护命中后，至少保持若干帧，避免站在物体边缘时一帧有、一帧无地抽搐。")]
    public int onePixelEnterHoldFrames = 4;

    [Tooltip("进入保护会在重叠区域采样中心和四角。开启后可捕捉更细边缘；关闭则只采样中心，性能略好但不够稳。")]
    public bool onePixelEnterSampleOverlapCorners = true;

    [Tooltip("投影 / GroundShadow / ContactShadow 只允许参与投影裁切，不允许作为角色本体遮挡采样。开启后可避免投影把角色反向压到背后物体后面。")]
    public bool excludeProjectionRenderersFromCharacterOcclusionSamples = true;

    [Tooltip("V53：像素级进入不再只看 UnitBody / 脚底逻辑锚点。开启后，武器、头发、衣服、身体等真实可见 Renderer 采样点会以 Main Camera 射线顺序参与边缘进入判定。")]
    public bool useVisualPartPixelDepthGate = true;

    [Tooltip("V53：像素级进入使用可见部件自己的世界深度做 Main Camera 射线比较。关闭时仍会投回 UnitBody 统一深度。")]
    public bool visualPartPixelDepthUsesActualWorldDepth = true;

    [Tooltip("V53：视觉部件像素级进入至少需要多少个可见部件采样点被当前遮挡物挡住。1 表示武器尖端 / 头发边缘一像素也能进入。")]
    public int visualPartPixelDepthMinBlockedSampleCount = 1;

    [Tooltip("V53：允许视觉部件像素级进入作为 Front Body Hard Veto 的例外。它不是纯像素覆盖，而是 Main Camera 射线确认当前遮挡物在该角色部件前方。")]
    public bool visualPartPixelDepthCanCancelFrontBodyVeto = true;

    [Tooltip("V54：把 Main Camera 里的 Renderer 像素重叠正式写入深度成立后的遮挡判定。开启后，武器/头发/身体等可见部件的屏幕交集会逐 Renderer 采样，而不是继续只依赖 UnitBody / 脚底逻辑锚点。")]
    public bool useVisualRendererPixelOverlapDepthGate = true;

    [Tooltip("V54：Renderer 像素交集至少需要多少个采样点被当前遮挡物挡住。1 表示武器尖端/外轮廓一像素成立即可进入遮挡。")]
    public int visualRendererPixelOverlapMinBlockedSampleCount = 1;

    [Tooltip("V54：Renderer 像素交集使用的 Viewport 外扩。它只在高度和前后深度关系成立后生效。")]
    public float visualRendererPixelOverlapPadding = 0.0035f;

    [Tooltip("V54：Renderer 像素交集采样中心和四角。开启后更敏感，适合武器尖端与箱体边缘的商品级判定。")]
    public bool visualRendererPixelOverlapSampleCorners = true;

    [Tooltip("V55：当前后深度关系已经成立后，不再继续用 UnitBody / 脚底 / 采样点循环决定是否遮挡，而是完全交给 Main Camera 下角色真实 Renderer 与当前遮挡物的像素交集决定。")]
    public bool pixelIntersectionFinalGateAfterDepthEstablished = true;

    [Tooltip("V58：深度成立以后，优先使用密集 viewport 像素网格裁判。它不再只采样重叠矩形的中心/四角，而是在 Main Camera 下扫描角色 Renderer 与当前物体投影重叠区域，命中任意有效像素即可成立。")]
    public bool useDepthEstablishedDenseViewportPixelGate = true;

    [Tooltip("V58：密集像素裁判每个重叠区域每轴采样数量。9=81点；11=121点。数值越大越像素级，但性能更高。")]
    public int depthEstablishedDensePixelGrid = 9;

    [Tooltip("V58：每帧每个物体最多测试多少个密集像素点，避免极大 Renderer 导致开销过高。")]
    public int depthEstablishedDensePixelMaxSamples = 96;

    [Tooltip("V58：密集像素裁判的 Viewport 外扩。通常和 Renderer Pixel Overlap Padding 接近。")]
    public float depthEstablishedDensePixelPadding = 0.0035f;

    [Tooltip("V55：深度成立后的最终像素裁判优先使用 Renderer 屏幕矩形交集 + Main Camera 射线确认。")]
    public bool depthEstablishedGateUseRendererPixelOverlap = true;

    [Tooltip("V56：当前后深度已经由 UnitBody / 逻辑锚点确认成立后，最终像素裁判不再对每个武器/头发/身体采样点重复做前后深度门槛。否则又会退回脚底/局部深度二次否决。真正的裁判只剩 Main Camera 像素交集 + 射线是否先命中当前物体。")]
    public bool finalPixelGateSkipsPerSampleDepthGateAfterDepthEstablished = true;

    [Tooltip("V55：Renderer 像素裁判没命中时，是否允许可见部件采样点作为像素裁判兜底。")]
    public bool depthEstablishedGateUseVisualPartPointFallback = true;

    [Tooltip("V55：前两者没命中时，是否允许旧的一像素重叠作为像素裁判兜底。注意它只在前后深度已经成立后使用。")]
    public bool depthEstablishedGateUseOnePixelOverlapFallback = true;

    [Tooltip("绘制一像素进入保护的调试线。")]
    public bool debugDrawOnePixelEnterGuard = false;

    [Header("Commercial Edge Release Latch")]
    [Tooltip("商业级边缘释放锁：一旦当前物体已经遮挡角色，只要主相机画面中角色视觉轮廓仍和当前物体有极小重叠，就不允许立刻退出遮挡。用于修复站在箱体边缘时遮挡开关一帧进入、一帧退出的抽搐。")]
    public bool useEdgeOverlapReleaseLatch = true;

    [Tooltip("释放锁命中后保持的帧数。建议 6~10。它只在已经处于遮挡状态后生效，不会主动制造新遮挡。")]
    public int edgeOverlapReleaseLatchFrames = 8;

    [Tooltip("边缘释放锁的 Viewport 外扩。通常要略大于一像素进入保护，避免退出比进入更敏感。")]
    public float edgeOverlapReleaseProjectionPadding = 0.006f;

    [Tooltip("仍视为边缘重叠的最小屏幕像素。1 表示一像素级保持。")]
    public float edgeOverlapReleaseMinOverlapPixels = 1.0f;

    [Tooltip("绘制边缘释放锁调试线。")]
    public bool debugDrawEdgeOverlapReleaseLatch = false;

    [Header("Commercial Front-Side Release Override")]
    [Tooltip("商业级正面释放：如果 UnitBody / 逻辑锚点从主相机射线看已经明确位于当前遮挡物前方，则立即释放遮挡，并且不允许一像素边缘保护继续把它拉回遮挡。用于修复角色来到正面仍被边缘迟滞锁住的问题。")]
    public bool useFrontSideReleaseOverride = true;

    [Tooltip("正面释放需要连续确认的帧数。1 表示一旦明确在前面就立刻释放；调到 2~3 可减少极限边缘抖动。")]
    public int frontSideReleaseConfirmFrames = 1;

    [Tooltip("启用后，正面释放成立时会清空 Motion / Commercial / Edge / OnePixel 的迟滞计数，避免旧状态把角色继续锁在遮挡里。")]
    public bool clearOcclusionHysteresisWhenFrontSide = true;

    [Tooltip("启用后，如果 UnitBody 明确在当前遮挡物前方，一像素进入保护也不能单独开启遮挡。这样可以避免角色站在正面时，仅因武器/屏幕边缘重叠而被拉进遮挡状态。")]
    public bool frontSideBlocksOnePixelEnterGuard = true;

    [Tooltip("V49：前后关系优先于像素交集。默认关闭覆盖粗正面判断：只有当前后关系已经允许遮挡时，一像素进入保护才可补足边缘敏感度。")]
    public bool pixelEnterCanOverrideCoarseFrontSideBlock = false;

    [Tooltip("V49：一像素进入保护必须服从 Camera Facing Behind Volume。像素交集只能作为边缘补强，不能越过前后关系把正面的角色拉回遮挡。")]
    public bool onePixelEnterRequiresBehindVolume = true;

    [Tooltip("V52：一像素进入保护只使用“深度是否在物体后方”的门槛，不再要求采样点必须落在粗 BehindVolume 的左右支撑范围内。规则顺序仍然是高度 > 前后深度 > 像素交集；这里把左右边缘交给像素交集处理，避免极限边缘因为粗支撑范围漏判而木讷。")]
    public bool onePixelEnterUsesDepthOnlyBehindGate = true;

    [Tooltip("正面释放使用主相机射线到 UnitBody 逻辑锚点，若到达锚点前没有命中当前遮挡物，则认为当前遮挡物没有处在角色前方。")]
    public bool useRayToLogicalAnchorForFrontSideRelease = true;

    [Tooltip("V51：严格优先级下的身体正面硬否决。只要 UnitBody / 逻辑锚点明确在当前遮挡物正面，当前物体不能遮挡角色；该否决高于像素交集、运动扫掠、边缘迟滞和一像素保持。")]
    public bool useFrontBodyHardVeto = true;

    [Tooltip("V58：禁止在进入高度/深度/像素最终裁判之前使用 UnitBody 正面硬否决。正面硬否决只能在深度未成立，或像素最终裁判没有接管时生效。")]
    public bool disableEarlyFrontBodyVetoBeforeDepthPixelGate = true;

    [Tooltip("启用后，头顶高度遮挡成立时不使用正面硬否决。用于桥、上层平台、叠放高物体等确实从上方压住角色的情况。")]
    public bool frontBodyHardVetoDoesNotCancelOverheadOcclusion = true;

    [Tooltip("正面硬否决成立时，是否立即清空所有遮挡迟滞计数。建议开启，否则旧的一像素保持/边缘释放锁可能继续把角色锁在遮挡里。")]
    public bool clearHysteresisOnFrontBodyHardVeto = true;

    [Tooltip("绘制正面释放/正面硬否决调试线。绿色表示正面清空，红色表示仍被当前物体挡住。")]
    public bool debugDrawFrontSideReleaseOverride = false;

    [Header("Runtime Performance / Throttle")]
    [Tooltip("启用后，静态遮挡物在没有候选单位时进入轻量睡眠：不重评估、不重复 ApplyVisibility，只按较低频率扫描候选。不会关闭已有遮挡功能。")]
    public bool useIdleSleepOptimization = true;

    [Tooltip("运行中没有候选单位时的主动扫描间隔。默认 0.12 秒，避免大量静态装饰物每 0.03 秒扫一次。进入候选后仍使用 wakeScanInterval。")]
    public float idleWakeScanInterval = 0.12f;

    [Tooltip("运行时头顶投影全局候选扫描的最小间隔。该扫描会 FindObjectsByType<Collider>，必须节流。0 表示不节流。")]
    public float cameraProjectedOverheadGlobalScanInterval = 0.20f;

    [Tooltip("默认关闭 Transform.hasChanged 作为动态刷新依据。移动/倒下物体仍会通过位置/旋转/缩放阈值和 Rigidbody sleep 状态刷新，避免静态父子层级把 hasChanged 卡成常亮。")]
    public bool useTransformHasChangedForDynamicRefresh = false;

    [Tooltip("默认不在 Play 模式持续生成路径/命中名称等调试字符串。debugLogs 或 debugDrawSamples 开启时会自动恢复。")]
    public bool suppressRuntimeDebugStrings = true;

    [Tooltip("默认不每帧 ResolveRoots。只有引用缺失时才重新查找，避免每个遮挡物递归搜索层级。")]
    public bool resolveRootsEveryLateUpdate = false;



    [Header("Startup Occlusion Material Sync")]
    [Tooltip("开局时单位的 OcclusionMaterialReceiver / RuntimeReceiver 可能晚于遮挡触发器绑定。启用后会在前几帧强制重扫候选、刷新 Receiver 列表，并把当前遮挡状态重新通知一次。")]
    public bool useStartupOcclusionMaterialSync = true;

    [Tooltip("OnEnable 后强制同步遮挡材质的帧数。用于覆盖 UnitDefinitionRuntimeApplier / UnitOcclusionMaterialReceiver 晚绑定。")]
    public int startupOcclusionSyncFrames = 10;

    [Tooltip("OnEnable 后强制同步遮挡材质的最长时间。帧数和时间任一未结束都会继续同步。")]
    public float startupOcclusionSyncDuration = 0.50f;

    [Tooltip("启动同步期间，即使遮挡状态没有变化，也重新向 Receiver 发送当前状态。用于修复开局已处于遮挡但材质没有刷新。")]
    public bool forceNotifyReceiversDuringStartupSync = true;

    [Header("Legacy Builder Compatibility / Public Settings Kept For Editor Tools")]
    [Tooltip("兼容旧 Builder/Inspector 字段。本 Camera Area Sweep 版本不使用 footprint 决定前后。")]
    public bool useFootprintPrecision = false;
    public float horizontalTolerance = 0.08f;
    public float frontTolerance = 0.08f;
    public float verticalTolerance = 0.08f;
    public bool enableOverheadOcclusion = true;
    public float overheadHeightMargin = 0.02f;
    public float overheadHorizontalExpansion = 0.75f;
    public float overheadBackDepthLimit = 0f;

    [SerializeField] private Vector3 footprintOrigin;
    [SerializeField] private float footprintMinY = float.NegativeInfinity;
    [SerializeField] private float footprintMaxY = float.PositiveInfinity;
    [SerializeField] private Vector3 footprintRightAxis = Vector3.right;
    [SerializeField] private Vector3 footprintDepthAxis = Vector3.forward;
    [SerializeField] private Vector2[] footprintPolygon;

    [Header("Debug")]
    public bool debugLogs = false;
    public bool debugDrawSamples = false;
    public float debugDrawDuration = 0.03f;
    public Color debugBlockedColor = Color.red;
    public Color debugOtherHitColor = Color.yellow;
    public Color debugClearColor = Color.cyan;
    public Color debugLogicalPointColor = Color.magenta;

    [SerializeField] private int debugCandidateCount;
    [SerializeField] private int debugSampleCount;
    [SerializeField] private int debugRayHitCount;
    [SerializeField] private int debugCurrentHitCount;
    [SerializeField] private int debugOtherHitCount;
    [SerializeField] private int debugClearCount;
    [SerializeField] private bool debugLastShouldOcclude;
    [SerializeField] private string debugLastRejectReason;
    [SerializeField] private string debugLastCandidateName;
    [SerializeField] private string debugLastResolvedUnitRootName;
    [SerializeField] private int debugLastResolvedRendererCount;
    [SerializeField] private string debugLogicalAnchorName;
    [SerializeField] private string debugLogicalAnchorSource;
    [SerializeField] private Vector3 debugLogicalAnchorWorldPosition;
    [SerializeField] private bool debugLogicalAnchorInsideBehindVolume;
    [SerializeField] private float debugBehindVolumeAnchorSide;
    [SerializeField] private float debugBehindVolumeMinSide;
    [SerializeField] private float debugBehindVolumeMaxSide;
    [SerializeField] private float debugBehindVolumeAnchorDepth;
    [SerializeField] private float debugBehindVolumeFrontDepthAtAnchor;
    [SerializeField] private float debugBehindVolumeGateDepth;
    [SerializeField] private float debugBehindVolumeMinY;
    [SerializeField] private float debugBehindVolumeMaxY;
    [SerializeField] private string debugBehindVolumeRejectReason;
    [SerializeField] private Vector3 debugBehindVolumeLeftSupportWorldPosition;
    [SerializeField] private Vector3 debugBehindVolumeRightSupportWorldPosition;
    [SerializeField] private bool debugOverheadOcclusionHit;
    [SerializeField] private string debugOverheadRejectReason;
    [SerializeField] private float debugOverheadOccluderBottomY;
    [SerializeField] private float debugOverheadUnitTopY;
    [SerializeField] private Vector3 debugOverheadAnchorWorldPosition;
    [SerializeField] private Bounds debugOverheadHorizontalBounds;
    [SerializeField] private Vector4 debugOverheadOccluderViewportRect;
    [SerializeField] private Vector4 debugOverheadUnitViewportRect;
    [SerializeField] private string debugOverheadProjectionMode;
    [SerializeField] private int debugDynamicRefreshCount;
    [SerializeField] private bool debugDynamicOccluderMoving;
    [SerializeField] private string debugDynamicRefreshReason;
    [SerializeField] private int debugLastReceiverCount;
    [SerializeField] private string debugStartupSyncReason;
    [SerializeField] private string debugCurrentOccluderRootName;
    [SerializeField] private string debugCurrentOccluderRootPath;
    [SerializeField] private int debugCurrentOccluderColliderCount;
    [SerializeField] private int debugCurrentOccluderRendererCount;
    [SerializeField] private string debugCurrentOccluderColliderRootName;
    [SerializeField] private string debugCurrentOccluderColliderRootPath;
    [SerializeField] private string debugLastOtherHitName;
    [SerializeField] private string debugLastOtherHitPath;
    [SerializeField] private string debugLastOtherHitRootName;
    [SerializeField] private string debugLastOtherHitLayer;
    [SerializeField] private float debugLastOtherHitDistance;
    [SerializeField] private string debugLastCurrentHitName;
    [SerializeField] private string debugLastCurrentHitPath;
    [SerializeField] private float debugLastCurrentHitDistance;
    [SerializeField] private string debugLastCurrentHitType;
    [SerializeField] private string debugMotionSweepReason;
    [SerializeField] private float debugMotionSweepDistance;
    [SerializeField] private int debugMotionReleaseGraceRemaining;

    private readonly Collider[] wakeScanBufferDefault = new Collider[128];
    private Collider[] wakeScanBufferRuntime;

    private readonly Dictionary<Transform, CandidateState> candidateStates = new Dictionary<Transform, CandidateState>();
    private readonly List<Transform> candidateSnapshot = new List<Transform>(32);
    private readonly HashSet<Collider> currentOccluderColliders = new HashSet<Collider>();
    private readonly List<Renderer> currentOccluderRenderers = new List<Renderer>(32);
    private readonly List<Renderer> tempRenderers = new List<Renderer>(32);
    private readonly List<Vector3> tempSamples = new List<Vector3>(256);
    private readonly List<Vector3> tempOverheadSamples = new List<Vector3>(256);
    private readonly List<Vector3> tempProjectionFootprintSamples = new List<Vector3>(128);
    private readonly RaycastHit[] raycastHits = new RaycastHit[64];

    // TryRaycastRendererMeshTriangles 原来每条射线都重新调用 mesh.vertices/mesh.triangles——
    // 这两个访问器每次都会把整份顶点/三角面数组从 native 内存拷贝一份到托管内存。同一个候选
    // 单位一次评估会有普通采样/密集像素网格/渲染体像素重叠三套逻辑各自打几十上百条射线，
    // 全部命中同一批遮挡物渲染体，等于同一份网格数据被反复复制几百次。改成只在
    // RebuildCurrentOccluderColliders 时（遮挡物内容变化才会调用）缓存一次，射线测试直接查表。
    private sealed class MeshRayCache
    {
        public Mesh mesh;
        public Vector3[] vertices;
        public int[] triangles;
        public Transform meshTransform;
    }

    private readonly Dictionary<Renderer, MeshRayCache> occluderMeshRayCache = new Dictionary<Renderer, MeshRayCache>();

    // ResolveUnitRoot 沿着物体层级一路 GetComponents<MonoBehaviour>() 找 IOcclusionStateReceiver，
    // ResolveReceivers 再对整个单位做一次 GetComponentsInChildren<MonoBehaviour>()——玩家跑进
    // 一片新区域时，附近好几个装饰物会在同一帧里第一次发现玩家/敌人，各自都要走一遍这两个
    // 方法，之前每次调用都新分配一个数组，是"跑图瞬间GC猛增"的一个来源。复用这两个缓冲区，
    // 改用 List 版本的 NonAlloc 重载。
    private readonly List<MonoBehaviour> scratchAncestorBehaviours = new List<MonoBehaviour>(16);
    private readonly List<MonoBehaviour> scratchChildBehaviours = new List<MonoBehaviour>(32);

    private float nextWakeScanTime;
    private float nextGlobalScanTime;
    private float nextDynamicRefreshTime;
    private bool hasLastDynamicPose;
    private Vector3 lastDynamicPosition;
    private Quaternion lastDynamicRotation;
    private Vector3 lastDynamicScale;
    private Rigidbody cachedDynamicRigidbody;
    private bool wasDynamicRigidbodyMoving;
    private int startupSyncFramesRemaining;
    private float startupSyncEndTime;
    private float nextCameraProjectedOverheadGlobalScanTime;
    private bool lastFrontOccluderVisibility;
    private readonly Dictionary<Transform, string> transformPathCache = new Dictionary<Transform, string>(256);

    private sealed class CandidateState
    {
        public Transform unitRoot;
        public Collider sourceCollider;
        public readonly List<MonoBehaviour> receivers = new List<MonoBehaviour>();
        public bool occluding;
        public bool hasLastLogicalAnchor;
        public Vector3 lastLogicalAnchor;
        public int releaseGraceRemaining;
        public int commercialExitGuardRemaining;
        public int onePixelEnterHoldRemaining;
        public int edgeOverlapReleaseLatchRemaining;
        public int frontSideReleaseConfirmRemaining;
    }

    public void ConfigureFootprint(Vector3 origin, Vector3 rightAxis, Vector3 depthAxis, Vector2[] polygon)
    {
        ConfigureFootprint(origin, rightAxis, depthAxis, polygon, float.NegativeInfinity, float.PositiveInfinity);
    }

    public void ConfigureFootprint(Vector3 origin, Vector3 rightAxis, Vector3 depthAxis, Vector2[] polygon, float minY, float maxY)
    {
        footprintOrigin = origin;
        footprintRightAxis = rightAxis.sqrMagnitude > 0.0001f ? rightAxis.normalized : Vector3.right;
        footprintDepthAxis = depthAxis.sqrMagnitude > 0.0001f ? depthAxis.normalized : Vector3.forward;
        footprintPolygon = polygon != null ? (Vector2[])polygon.Clone() : null;

        if (float.IsNaN(minY) || float.IsNaN(maxY) || minY > maxY)
        {
            footprintMinY = float.NegativeInfinity;
            footprintMaxY = float.PositiveInfinity;
        }
        else
        {
            footprintMinY = minY;
            footprintMaxY = maxY;
        }
    }

    private void Awake()
    {
        EnsureRuntimeBuffer();
        ResolveRoots();
        RebuildCurrentOccluderColliders();
        RefreshDynamicOccluderGeometry("Awake", true);
        ApplyVisibility();
    }

    private void OnEnable()
    {
        EnsureRuntimeBuffer();
        ResolveRoots();
        RebuildCurrentOccluderColliders();
        RefreshDynamicOccluderGeometry("OnEnable", true);
        BeginStartupOcclusionMaterialSync();
        ScanCandidatesIfNeeded(true);
        ReevaluateCandidates(IsStartupOcclusionMaterialSyncActive());
        ForceNotifyCurrentOcclusionStates("OnEnable initial sync");
        ApplyVisibility();
    }

    private void LateUpdate()
    {
        if (ShouldResolveRootsInLateUpdate())
            ResolveRoots();

        HandleDynamicOccluderRefresh(false);

        bool startupSyncActive = IsStartupOcclusionMaterialSyncActive();
        ScanCandidatesIfNeeded(startupSyncActive);

        // 关键优化：大量静态遮挡物在没有候选单位时不需要每帧重评估。
        // 仍然按 idleWakeScanInterval 扫描候选；一旦扫到候选，会立即进入完整重评估链。
        bool hasCandidates = candidateStates.Count > 0;
        if (hasCandidates || startupSyncActive || !useIdleSleepOptimization)
            ReevaluateCandidates(startupSyncActive);
        else
            debugCandidateCount = 0;

        if (startupSyncActive && forceNotifyReceiversDuringStartupSync)
            ForceNotifyCurrentOcclusionStates("startup receiver/material sync");

        TickStartupOcclusionMaterialSync();

        if (hasCandidates || lastFrontOccluderVisibility || startupSyncActive || !useIdleSleepOptimization)
            ApplyVisibility();
    }

    private void OnDisable()
    {
        NotifyAllReceivers(false);
        candidateStates.Clear();
        transformPathCache.Clear();
        lastFrontOccluderVisibility = false;

        if (frontOccluderRoot != null)
            frontOccluderRoot.SetActive(false);
    }

    private void OnValidate()
    {
        if (maxUnitMeshSamplesPerRenderer < 0)
            maxUnitMeshSamplesPerRenderer = 0;
        if (minBlockedSampleCount < 1)
            minBlockedSampleCount = 1;
        if (wakeScanBufferSize < 16)
            wakeScanBufferSize = 16;
        if (maxSelfVisualMeshTrianglesPerRenderer < 0)
            maxSelfVisualMeshTrianglesPerRenderer = 0;
        if (behindVolumeSideMargin < 0f)
            behindVolumeSideMargin = 0f;
        if (behindVolumeFrontMargin < 0f)
            behindVolumeFrontMargin = 0f;
        if (behindVolumeHeightPadding < 0f)
            behindVolumeHeightPadding = 0f;
        if (dynamicRefreshWhileMovingInterval < 0f)
            dynamicRefreshWhileMovingInterval = 0f;
        if (dynamicPositionDirtyThreshold < 0f)
            dynamicPositionDirtyThreshold = 0f;
        if (dynamicRotationDirtyThreshold < 0f)
            dynamicRotationDirtyThreshold = 0f;
        if (dynamicScaleDirtyThreshold < 0f)
            dynamicScaleDirtyThreshold = 0f;
        if (startupOcclusionSyncFrames < 0)
            startupOcclusionSyncFrames = 0;
        if (startupOcclusionSyncDuration < 0f)
            startupOcclusionSyncDuration = 0f;
        if (idleWakeScanInterval < 0f)
            idleWakeScanInterval = 0f;
        if (projectionFootprintMinBlockedSampleCount < 1)
            projectionFootprintMinBlockedSampleCount = 1;
        if (visualPartPixelDepthMinBlockedSampleCount < 1)
            visualPartPixelDepthMinBlockedSampleCount = 1;

        if (sideOcclusionHeightPadding < 0f)
            sideOcclusionHeightPadding = 0f;
        if (minSideOcclusionHeightOverlap < 0f)
            minSideOcclusionHeightOverlap = 0f;
        if (cameraProjectedOverheadGlobalScanInterval < 0f)
            cameraProjectedOverheadGlobalScanInterval = 0f;
    }

    public void ResetRuntimeState()
    {
        NotifyAllReceivers(false);
        candidateStates.Clear();
        ApplyVisibility();
    }


    private void BeginStartupOcclusionMaterialSync()
    {
        if (!useStartupOcclusionMaterialSync)
        {
            startupSyncFramesRemaining = 0;
            startupSyncEndTime = 0f;
            debugStartupSyncReason = "disabled";
            return;
        }

        startupSyncFramesRemaining = Mathf.Max(0, startupOcclusionSyncFrames);
        startupSyncEndTime = Time.time + Mathf.Max(0f, startupOcclusionSyncDuration);
        debugStartupSyncReason = "started";
    }

    private bool IsStartupOcclusionMaterialSyncActive()
    {
        if (!useStartupOcclusionMaterialSync)
            return false;

        return startupSyncFramesRemaining > 0 || Time.time <= startupSyncEndTime;
    }

    private void TickStartupOcclusionMaterialSync()
    {
        if (startupSyncFramesRemaining > 0)
            startupSyncFramesRemaining--;

        if (!IsStartupOcclusionMaterialSyncActive() && debugStartupSyncReason == "startup receiver/material sync")
            debugStartupSyncReason = "completed";
    }

    private void ForceNotifyCurrentOcclusionStates(string reason)
    {
        if (!notifyOcclusionStateReceivers)
            return;

        foreach (KeyValuePair<Transform, CandidateState> pair in candidateStates)
        {
            CandidateState state = pair.Value;
            if (state == null || state.unitRoot == null)
                continue;

            RefreshReceivers(state, true);
            NotifyCandidateReceivers(state, state.occluding);
        }

        debugStartupSyncReason = reason;
    }

    private void EnsureRuntimeBuffer()
    {
        if (wakeScanBufferRuntime == null || wakeScanBufferRuntime.Length != wakeScanBufferSize)
            wakeScanBufferRuntime = new Collider[Mathf.Max(16, wakeScanBufferSize)];
    }

    private void ResolveRoots()
    {
        if (sourceCamera == null)
            sourceCamera = Camera.main;

        if (autoFindFrontOccluderRoot && frontOccluderRoot == null)
        {
            Transform found = FindChildInAncestors(transform, "FrontOccluderRoot");
            if (found != null)
                frontOccluderRoot = found.gameObject;
        }

        if (autoFindNearestVisualRoot && occluderContentRoot == null)
            occluderContentRoot = FindChildInAncestors(transform, "VisualRoot");

        if (autoFindNearestCollisionRoot && occluderColliderRoot == null)
            occluderColliderRoot = FindChildInAncestors(transform, "CollisionRoot");
    }

    private bool ShouldResolveRootsInLateUpdate()
    {
        if (resolveRootsEveryLateUpdate)
            return true;

        if (sourceCamera == null)
            return true;
        if (autoFindFrontOccluderRoot && frontOccluderRoot == null)
            return true;
        if (autoFindNearestVisualRoot && occluderContentRoot == null)
            return true;
        if (autoFindNearestCollisionRoot && occluderColliderRoot == null)
            return true;

        return false;
    }

    private bool ShouldWriteRuntimeDebugStrings()
    {
        if (!Application.isPlaying)
            return true;
        if (!suppressRuntimeDebugStrings)
            return true;
        return debugLogs || debugDrawSamples;
    }

    private bool ShouldDrawRuntimeDebug()
    {
        if (!Application.isPlaying)
            return true;
        return debugLogs || debugDrawSamples;
    }

    private Transform FindChildInAncestors(Transform start, string childName)
    {
        Transform t = start;
        while (t != null)
        {
            Transform found = FindChildRecursive(t, childName);
            if (found != null)
                return found;
            t = t.parent;
        }
        return null;
    }

    private Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private void HandleDynamicOccluderRefresh(bool force)
    {
        if (!useDynamicOccluderRefresh && !force)
            return;

        Transform tracked = GetDynamicTrackingTransform();
        if (tracked == null)
            return;

        if (cachedDynamicRigidbody == null)
            cachedDynamicRigidbody = FindRigidbodyForDynamicRefresh(tracked);

        bool rbMoving = cachedDynamicRigidbody != null && !cachedDynamicRigidbody.IsSleeping();
        bool rbJustSlept = cachedDynamicRigidbody != null && wasDynamicRigidbodyMoving && !rbMoving;
        bool poseDirty = IsDynamicPoseDirty(tracked);

        debugDynamicOccluderMoving = rbMoving || poseDirty;

        if (!force && !poseDirty && !(refreshOnceWhenRigidbodySleeps && rbJustSlept))
        {
            wasDynamicRigidbodyMoving = rbMoving;
            return;
        }

        if (!force && rbMoving && dynamicRefreshWhileMovingInterval > 0f && Time.time < nextDynamicRefreshTime)
        {
            wasDynamicRigidbodyMoving = rbMoving;
            return;
        }

        string reason;
        if (force)
            reason = "Force";
        else if (refreshOnceWhenRigidbodySleeps && rbJustSlept)
            reason = "Rigidbody slept final refresh";
        else if (rbMoving)
            reason = "Rigidbody moving dirty refresh";
        else
            reason = "Transform dirty refresh";

        RefreshDynamicOccluderGeometry(reason, true);
        wasDynamicRigidbodyMoving = rbMoving;
    }

    private Transform GetDynamicTrackingTransform()
    {
        if (occluderColliderRoot != null)
            return occluderColliderRoot;
        if (occluderContentRoot != null)
            return occluderContentRoot;
        return transform;
    }

    private Rigidbody FindRigidbodyForDynamicRefresh(Transform tracked)
    {
        if (tracked == null)
            return null;

        Rigidbody rb = tracked.GetComponentInParent<Rigidbody>();
        if (rb != null)
            return rb;

        return tracked.GetComponentInChildren<Rigidbody>(true);
    }

    private bool IsDynamicPoseDirty(Transform tracked)
    {
        if (tracked == null)
            return false;

        if (!hasLastDynamicPose)
            return true;

        float positionDelta = Vector3.Distance(tracked.position, lastDynamicPosition);
        float rotationDelta = Quaternion.Angle(tracked.rotation, lastDynamicRotation);
        Vector3 scaleDelta = tracked.lossyScale - lastDynamicScale;
        float scaleAmount = Mathf.Max(Mathf.Abs(scaleDelta.x), Mathf.Abs(scaleDelta.y), Mathf.Abs(scaleDelta.z));

        return positionDelta > dynamicPositionDirtyThreshold
            || rotationDelta > dynamicRotationDirtyThreshold
            || scaleAmount > dynamicScaleDirtyThreshold
            || (useTransformHasChangedForDynamicRefresh && tracked.hasChanged);
    }

    private void CaptureDynamicPose(Transform tracked)
    {
        if (tracked == null)
            return;

        lastDynamicPosition = tracked.position;
        lastDynamicRotation = tracked.rotation;
        lastDynamicScale = tracked.lossyScale;
        tracked.hasChanged = false;
        hasLastDynamicPose = true;
    }

    private void RefreshDynamicOccluderGeometry(string reason, bool resetScanTimers)
    {
        Transform tracked = GetDynamicTrackingTransform();

        RebuildCurrentOccluderColliders();
        CaptureDynamicPose(tracked);

        if (resetScanTimers)
        {
            nextWakeScanTime = 0f;
            nextGlobalScanTime = 0f;
        }

        if (clearCandidatesOnDynamicRefresh)
            candidateStates.Clear();

        nextDynamicRefreshTime = Time.time + Mathf.Max(0f, dynamicRefreshWhileMovingInterval);
        debugDynamicRefreshCount++;
        debugDynamicRefreshReason = reason;
    }

    private void RebuildCurrentOccluderColliders()
    {
        currentOccluderColliders.Clear();
        currentOccluderRenderers.Clear();
        occluderMeshRayCache.Clear();

        if (occluderContentRoot == null && occluderColliderRoot == null)
            return;

        Transform colliderSearchRoot = occluderColliderRoot != null ? occluderColliderRoot : occluderContentRoot;
        if (colliderSearchRoot != null)
        {
            Collider[] cols = colliderSearchRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null || c.isTrigger)
                    continue;
                currentOccluderColliders.Add(c);
            }
        }

        if (occluderContentRoot == null)
            return;

        Renderer[] renderers = occluderContentRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;
            currentOccluderRenderers.Add(r);

            if (useSelfVisualMeshTriangleRayDepth)
                BuildMeshRayCacheForRenderer(r);
        }
    }

    private void BuildMeshRayCacheForRenderer(Renderer renderer)
    {
        if (renderer == null)
            return;

        Mesh mesh = null;
        Transform meshTransform = renderer.transform;

        SkinnedMeshRenderer smr = renderer as SkinnedMeshRenderer;
        if (smr != null)
        {
            mesh = smr.sharedMesh;
            meshTransform = smr.transform;
        }
        else
        {
            MeshFilter mf = renderer.GetComponent<MeshFilter>();
            if (mf != null)
            {
                mesh = mf.sharedMesh;
                meshTransform = mf.transform;
            }
        }

        if (mesh == null || !mesh.isReadable || mesh.vertexCount <= 0)
            return;

        Vector3[] vertices;
        int[] triangles;
        try
        {
            vertices = mesh.vertices;
            triangles = mesh.triangles;
        }
        catch
        {
            return;
        }

        if (vertices == null || triangles == null || triangles.Length < 3)
            return;

        occluderMeshRayCache[renderer] = new MeshRayCache
        {
            mesh = mesh,
            vertices = vertices,
            triangles = triangles,
            meshTransform = meshTransform,
        };
    }

    private void ScanCandidatesIfNeeded(bool force)
    {
        if (!activeWakeScan && !scanAllTargetLayerCollidersForDebug)
            return;

        float effectiveWakeScanInterval = GetEffectiveWakeScanInterval();
        if (!force && effectiveWakeScanInterval > 0f && Time.time < nextWakeScanTime)
            return;

        nextWakeScanTime = Time.time + Mathf.Max(0f, effectiveWakeScanInterval);

        PruneInvalidCandidates();

        if (scanAllTargetLayerCollidersForDebug)
        {
            if (force || globalCandidateScanInterval <= 0f || Time.time >= nextGlobalScanTime)
            {
                nextGlobalScanTime = Time.time + Mathf.Max(0f, globalCandidateScanInterval);
                ScanAllTargetLayerColliders();
            }
        }
        else
        {
            ScanCandidatesByWakeBounds();
            if (useOverheadHeightOcclusion && useOverheadCandidateWakeScan)
                ScanCandidatesByOverheadWakeBounds();
        }
    }

    private float GetEffectiveWakeScanInterval()
    {
        if (!Application.isPlaying || !useIdleSleepOptimization)
            return wakeScanInterval;

        return candidateStates.Count <= 0 ? Mathf.Max(wakeScanInterval, idleWakeScanInterval) : wakeScanInterval;
    }

    private void ScanAllTargetLayerColliders()
    {
#if UNITY_2023_1_OR_NEWER
        Collider[] all = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        Collider[] all = Object.FindObjectsOfType<Collider>();
#endif
        for (int i = 0; i < all.Length; i++)
        {
            Collider c = all[i];
            if (IsValidTarget(c))
                AddOrRefreshCandidate(c);
        }
    }

    private void ScanCandidatesByWakeBounds()
    {
        if (!TryGetWakeBounds(out Bounds bounds))
            return;

        Collider[] buffer = wakeScanBufferRuntime != null ? wakeScanBufferRuntime : wakeScanBufferDefault;
        int count = Physics.OverlapBoxNonAlloc(
            bounds.center,
            bounds.extents,
            buffer,
            Quaternion.identity,
            targetLayers,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            Collider c = buffer[i];
            if (IsValidTarget(c))
                AddOrRefreshCandidate(c);
            buffer[i] = null;
        }
    }

    private void ScanCandidatesByOverheadWakeBounds()
    {
        if (!TryGetCurrentOccluderSolidBounds(out Bounds solidBounds))
            return;

        if (useCameraProjectedOverheadOcclusion && useCameraProjectedOverheadCandidateScan)
            ScanCandidatesByCameraProjectedOverhead(solidBounds);

        // 头顶候选扫描不能使用遮挡物自身 Bounds 的 Y 范围。
        // 上层箱子/平台位于角色头上时，UnitBody 往往在它的 Bounds 下方，普通 3D OverlapBox 会扫不到。
        Bounds overheadBounds = solidBounds;
        overheadBounds.Expand(new Vector3(overheadHorizontalPadding * 2f, 0f, overheadHorizontalPadding * 2f));

        float top = solidBounds.max.y + Mathf.Max(0f, overheadCandidateScanUpDistance);
        float bottom = solidBounds.min.y - Mathf.Max(0f, overheadCandidateScanDownDistance);
        overheadBounds.center = new Vector3(overheadBounds.center.x, (top + bottom) * 0.5f, overheadBounds.center.z);
        overheadBounds.extents = new Vector3(overheadBounds.extents.x, Mathf.Max(0.01f, (top - bottom) * 0.5f), overheadBounds.extents.z);

        Collider[] buffer = wakeScanBufferRuntime != null ? wakeScanBufferRuntime : wakeScanBufferDefault;
        int count = Physics.OverlapBoxNonAlloc(
            overheadBounds.center,
            overheadBounds.extents,
            buffer,
            Quaternion.identity,
            targetLayers,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            Collider c = buffer[i];
            if (IsValidTarget(c))
                AddOrRefreshCandidate(c);
            buffer[i] = null;
        }
    }

    private bool TryGetWakeBounds(out Bounds bounds)
    {
        if (useVisualRootBoundsForWakeScan && TryGetRendererBounds(occluderContentRoot, true, out bounds))
        {
            bounds.Expand(visualRootWakeExtraPadding * 2f);
            return true;
        }

        if (allowBackTriggerWakeFallback)
        {
            Collider c = GetComponent<Collider>();
            if (c != null)
            {
                bounds = c.bounds;
                bounds.Expand(wakeScanExtraPadding * 2f);
                return true;
            }
        }

        bounds = default;
        return false;
    }

    private bool TryGetRendererBounds(Transform root, bool includeInactive, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive);
        bool has = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
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

    private bool IsValidTarget(Collider c)
    {
        if (c == null || !c.enabled)
            return false;

        int bit = 1 << c.gameObject.layer;
        if ((targetLayers.value & bit) == 0)
            return false;

        // V59：主动扫描范围可能覆盖当前装饰物自身的 Trigger / Collision / FrontOccluderProxy。
        // 这些 Collider 绝对不能进入 candidateStates，否则会出现 FrontOccluderRoot 被解析成“单位”，
        // debugCandidateCount 虚高、debugLastResolvedUnitRootName=FrontOccluderRoot、代理体开关被自引用污染。
        if (rejectSelfAndProxyCandidates && IsColliderOwnedByCurrentOccluder(c))
            return false;

        return true;
    }

    private bool IsColliderOwnedByCurrentOccluder(Collider c)
    {
        if (c == null)
            return false;

        Transform t = c.transform;
        if (t == null)
            return false;

        if (frontOccluderRoot != null && t.IsChildOf(frontOccluderRoot.transform))
            return true;

        if (occluderContentRoot != null && t.IsChildOf(occluderContentRoot))
            return true;

        if (occluderColliderRoot != null && t.IsChildOf(occluderColliderRoot))
            return true;

        // BackTrigger 脚本自身以及 RuleRoot 下的 FrontTrigger / BackTrigger 也不应成为单位候选。
        if (t == transform || t.IsChildOf(transform))
            return true;

        Transform ruleRoot = transform.parent;
        if (ruleRoot != null)
        {
            string n = t.name;
            if (!string.IsNullOrEmpty(n) &&
                (n.Contains("BackTrigger") || n.Contains("FrontTrigger") || n.Contains("FrontOccluder") || n.Contains("OccluderProxy")))
            {
                if (t.IsChildOf(ruleRoot))
                    return true;
            }
        }

        return false;
    }

    private void AddOrRefreshCandidate(Collider sourceCollider)
    {
        if (sourceCollider == null)
            return;

        Transform unitRoot = ResolveUnitRoot(sourceCollider);
        if (unitRoot == null)
            return;

        if (!candidateStates.TryGetValue(unitRoot, out CandidateState state))
        {
            state = new CandidateState();
            state.unitRoot = unitRoot;
            candidateStates.Add(unitRoot, state);
            ResolveReceivers(unitRoot, state.receivers);
        }

        state.sourceCollider = sourceCollider;
    }

    private Transform ResolveUnitRoot(Collider candidate)
    {
        if (candidate == null)
            return null;

        Transform t = candidate.transform;
        Transform bestNamedRoot = null;
        Transform receiverRoot = null;

        while (t != null)
        {
            if (bestNamedRoot == null && IsLikelyUnitRootName(t.name))
                bestNamedRoot = t;

            t.GetComponents(scratchAncestorBehaviours);
            for (int i = 0; i < scratchAncestorBehaviours.Count; i++)
            {
                MonoBehaviour mb = scratchAncestorBehaviours[i];
                if (mb is IOcclusionStateReceiver)
                {
                    receiverRoot = t;
                    break;
                }
            }

            if (receiverRoot != null)
                break;

            t = t.parent;
        }

        // V59：优先使用真正的 IOcclusionStateReceiver 根。它代表单位确实能接收遮挡状态。
        if (receiverRoot != null)
            return receiverRoot;

        // 其次才使用明确像单位根的命名节点。
        if (bestNamedRoot != null)
            return bestNamedRoot;

        // 旧版会 fallback 到 candidate.transform.parent / candidate.transform。
        // 这会把 FrontOccluderRoot、CollisionRoot、VisualRoot 等当前装饰物自身节点误当成单位候选，
        // 是本次“代理体不亮 / debugLastResolvedUnitRootName=FrontOccluderRoot”的主要污染源。
        if (requireRealUnitCandidateRoot)
            return null;

        if (candidate.transform.parent != null)
            return candidate.transform.parent;

        return candidate.transform;
    }

    private bool IsLikelyUnitRootName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        return name.Contains("UnitRoot")
            || name.Contains("PlayerRoot")
            || name.Contains("EnemyRoot")
            || name.Contains("AllyRoot")
            || name.Contains("PF_Unit")
            || name.Contains("UnitRuntime");
    }

    private void ResolveReceivers(Transform unitRoot, List<MonoBehaviour> output)
    {
        output.Clear();
        if (!notifyOcclusionStateReceivers || unitRoot == null)
            return;

        unitRoot.GetComponentsInChildren(true, scratchChildBehaviours);
        for (int i = 0; i < scratchChildBehaviours.Count; i++)
        {
            MonoBehaviour mb = scratchChildBehaviours[i];
            if (mb is IOcclusionStateReceiver)
                output.Add(mb);
        }
    }


    private bool RefreshReceivers(CandidateState state, bool force)
    {
        if (state == null || state.unitRoot == null || !notifyOcclusionStateReceivers)
            return false;

        if (!force && state.receivers.Count > 0)
            return false;

        int oldCount = state.receivers.Count;
        ResolveReceivers(state.unitRoot, state.receivers);
        debugLastReceiverCount = state.receivers.Count;
        return oldCount != state.receivers.Count;
    }

    private void PruneInvalidCandidates()
    {
        candidateSnapshot.Clear();
        foreach (KeyValuePair<Transform, CandidateState> pair in candidateStates)
        {
            if (pair.Key == null)
                candidateSnapshot.Add(pair.Key);
        }

        for (int i = 0; i < candidateSnapshot.Count; i++)
            candidateStates.Remove(candidateSnapshot[i]);
    }

    private void ReevaluateCandidates(bool forceRefreshReceivers)
    {
        debugCandidateCount = candidateStates.Count;
        debugSampleCount = 0;
        debugRayHitCount = 0;
        debugCurrentHitCount = 0;
        debugOtherHitCount = 0;
        debugClearCount = 0;
        debugLastShouldOcclude = false;
        debugLogicalAnchorInsideBehindVolume = false;
        debugBehindVolumeAnchorSide = 0f;
        debugBehindVolumeMinSide = 0f;
        debugBehindVolumeMaxSide = 0f;
        debugBehindVolumeAnchorDepth = 0f;
        debugBehindVolumeFrontDepthAtAnchor = 0f;
        debugBehindVolumeGateDepth = 0f;
        debugBehindVolumeMinY = 0f;
        debugBehindVolumeMaxY = 0f;
        debugBehindVolumeRejectReason = "";
        debugBehindVolumeLeftSupportWorldPosition = Vector3.zero;
        debugBehindVolumeRightSupportWorldPosition = Vector3.zero;
        debugOverheadOcclusionHit = false;
        debugOverheadRejectReason = "";
        debugOverheadOccluderBottomY = 0f;
        debugOverheadUnitTopY = 0f;
        debugOverheadAnchorWorldPosition = Vector3.zero;
        debugOverheadHorizontalBounds = default;
        debugOverheadOccluderViewportRect = Vector4.zero;
        debugOverheadUnitViewportRect = Vector4.zero;
        debugOverheadProjectionMode = "";
        debugLastRejectReason = "";
        debugMotionSweepReason = "";
        debugMotionSweepDistance = 0f;
        debugMotionReleaseGraceRemaining = 0;
        debugLastOtherHitName = "";
        debugLastOtherHitPath = "";
        debugLastOtherHitRootName = "";
        debugLastOtherHitLayer = "";
        debugLastOtherHitDistance = 0f;
        debugLastCurrentHitName = "";
        debugLastCurrentHitPath = "";
        debugLastCurrentHitDistance = 0f;
        if (ShouldWriteRuntimeDebugStrings())
        {
            debugCurrentOccluderRootName = occluderContentRoot != null ? occluderContentRoot.name : "NULL";
            debugCurrentOccluderRootPath = occluderContentRoot != null ? GetTransformPath(occluderContentRoot) : "NULL";
            debugCurrentOccluderColliderRootName = occluderColliderRoot != null ? occluderColliderRoot.name : "NULL";
            debugCurrentOccluderColliderRootPath = occluderColliderRoot != null ? GetTransformPath(occluderColliderRoot) : "NULL";
        }
        debugCurrentOccluderColliderCount = currentOccluderColliders.Count;
        debugCurrentOccluderRendererCount = currentOccluderRenderers.Count;

        if (candidateStates.Count == 0)
        {
            ApplyVisibility();
            return;
        }

        if (sourceCamera == null)
        {
            debugLastRejectReason = "No source camera.";
            SetAllCandidatesOccluding(false);
            return;
        }

        if (occluderContentRoot == null)
        {
            debugLastRejectReason = "No occluderContentRoot / VisualRoot.";
            SetAllCandidatesOccluding(false);
            return;
        }

        if (currentOccluderColliders.Count == 0 && currentOccluderRenderers.Count == 0)
            RebuildCurrentOccluderColliders();

        candidateSnapshot.Clear();
        foreach (KeyValuePair<Transform, CandidateState> pair in candidateStates)
        {
            if (pair.Key != null)
                candidateSnapshot.Add(pair.Key);
        }

        for (int i = 0; i < candidateSnapshot.Count; i++)
        {
            Transform unitRoot = candidateSnapshot[i];
            if (!candidateStates.TryGetValue(unitRoot, out CandidateState state))
                continue;

            bool receiverListChanged = RefreshReceivers(state, forceRefreshReceivers || state.receivers.Count == 0);
            bool should = ShouldOccludeByCameraAreaSweep(state);
            SetCandidateOccluding(state, should, receiverListChanged);
        }
    }

    private void SetAllCandidatesOccluding(bool value)
    {
        foreach (KeyValuePair<Transform, CandidateState> pair in candidateStates)
            SetCandidateOccluding(pair.Value, value);
    }

    private bool ShouldOccludeByCameraAreaSweep(CandidateState state)
    {
        if (state == null || state.unitRoot == null)
            return false;

        debugLastCandidateName = state.sourceCollider != null ? state.sourceCollider.name : "NULL";
        debugLastResolvedUnitRootName = state.unitRoot.name;
        debugMotionSweepReason = "";
        debugMotionSweepDistance = 0f;
        debugMotionReleaseGraceRemaining = state.releaseGraceRemaining;

        Vector3 logicalAnchor;
        string anchorName;
        string anchorSource;
        if (!TryGetLogicalAnchor(state, out logicalAnchor, out anchorName, out anchorSource))
        {
            debugLastRejectReason = "No logical anchor.";
            return ApplyMotionReleaseHysteresis(state, false, Vector3.zero, false);
        }

        debugLogicalAnchorName = anchorName;
        debugLogicalAnchorSource = anchorSource;
        debugLogicalAnchorWorldPosition = logicalAnchor;

        // V58：这里不能再提前用 UnitBody / logical anchor 做正面硬否决。
        // 旧 V51 在进入 EvaluateOcclusionAtLogicalAnchor 之前就 return false，导致后面的
        // “高度成立 -> 深度成立 -> Main Camera 像素交集最终裁判”完全没有机会执行。
        // 正面硬否决仍保留，但必须放在 EvaluateOcclusionAtLogicalAnchor 内部，等深度门槛判断后再决定。
        if (!disableEarlyFrontBodyVetoBeforeDepthPixelGate && ShouldApplyFrontBodyHardVeto(state, logicalAnchor))
        {
            if (clearHysteresisOnFrontBodyHardVeto || clearOcclusionHysteresisWhenFrontSide)
                ClearOcclusionHysteresisCountersForce(state);

            state.releaseGraceRemaining = 0;
            state.commercialExitGuardRemaining = 0;
            state.edgeOverlapReleaseLatchRemaining = 0;
            state.onePixelEnterHoldRemaining = 0;
            state.frontSideReleaseConfirmRemaining = 0;
            debugLastShouldOcclude = false;
            debugMotionReleaseGraceRemaining = 0;
            debugLastRejectReason = "V58 legacy early front-body veto: disabled by default because it bypasses depth-established pixel gate.";
            RememberLogicalAnchor(state, logicalAnchor, true);
            return false;
        }

        bool shouldOcclude = EvaluateOcclusionAtLogicalAnchor(state, logicalAnchor);

        if (!shouldOcclude && useMotionSweptOcclusion && state.hasLastLogicalAnchor)
        {
            float moved = Vector3.Distance(state.lastLogicalAnchor, logicalAnchor);
            debugMotionSweepDistance = moved;

            if (moved >= Mathf.Max(0f, motionSweepMinDistance))
            {
                if (useMotionSweepMidpoint)
                {
                    Vector3 mid = (state.lastLogicalAnchor + logicalAnchor) * 0.5f;
                    if (EvaluateOcclusionAtLogicalAnchor(state, mid))
                    {
                        shouldOcclude = true;
                        debugMotionSweepReason = "Accepted by motion swept midpoint anchor.";
                        debugLastRejectReason = debugMotionSweepReason;
                    }
                }

                if (!shouldOcclude && EvaluateOcclusionAtLogicalAnchor(state, state.lastLogicalAnchor))
                {
                    shouldOcclude = true;
                    debugMotionSweepReason = "Accepted by previous logical anchor.";
                    debugLastRejectReason = debugMotionSweepReason;
                }

                if (!shouldOcclude && string.IsNullOrEmpty(debugMotionSweepReason))
                    debugMotionSweepReason = "Motion sweep checked but no swept anchor was occluded.";
            }
        }

        return ApplyMotionReleaseHysteresis(state, shouldOcclude, logicalAnchor, true);
    }

    private bool ApplyMotionReleaseHysteresis(CandidateState state, bool shouldOcclude, Vector3 logicalAnchor, bool hasLogicalAnchor)
    {
        if (state == null)
            return shouldOcclude;

        if (shouldOcclude)
        {
            state.releaseGraceRemaining = Mathf.Max(0, releaseHysteresisFrames);
            state.commercialExitGuardRemaining = Mathf.Max(0, commercialExitGuardFrames);
            state.frontSideReleaseConfirmRemaining = 0;
            debugMotionReleaseGraceRemaining = Mathf.Max(state.releaseGraceRemaining, state.commercialExitGuardRemaining);
            RememberLogicalAnchor(state, logicalAnchor, hasLogicalAnchor);
            debugLastShouldOcclude = true;
            return true;
        }

        // V47：正面释放优先级必须高于所有退出迟滞。
        // 如果 UnitBody 逻辑锚点已经从主相机射线看处于当前遮挡物前方，迟滞不能继续把角色锁在遮挡里。
        if (state.occluding && useFrontSideReleaseOverride && hasLogicalAnchor && IsLogicalAnchorClearlyInFrontOfCurrentOccluder(logicalAnchor, state))
        {
            state.frontSideReleaseConfirmRemaining++;
            if (state.frontSideReleaseConfirmRemaining >= Mathf.Max(1, frontSideReleaseConfirmFrames))
            {
                ClearOcclusionHysteresisCounters(state);
                debugLastShouldOcclude = false;
                debugMotionReleaseGraceRemaining = 0;
                debugLastRejectReason = "Front-side release override: logical anchor is clearly in front of current occluder.";
                RememberLogicalAnchor(state, logicalAnchor, hasLogicalAnchor);
                return false;
            }

            debugLastShouldOcclude = true;
            debugMotionReleaseGraceRemaining = Mathf.Max(debugMotionReleaseGraceRemaining, Mathf.Max(1, frontSideReleaseConfirmFrames - state.frontSideReleaseConfirmRemaining));
            debugLastRejectReason = "Front-side release override: waiting confirm frames.";
            RememberLogicalAnchor(state, logicalAnchor, hasLogicalAnchor);
            return true;
        }
        else if (state != null)
        {
            state.frontSideReleaseConfirmRemaining = 0;
        }

        // V50：高度关系高于退出迟滞。
        // 如果当前物体和单位在上下高度上已经不可能形成侧向遮挡，边缘像素重叠不能继续锁住遮挡。
        if (state.occluding && useHeightDepthPixelPriority && useSideOcclusionHeightGate && hasLogicalAnchor && !IsSideOcclusionHeightRelationValid(state, logicalAnchor))
        {
            ClearOcclusionHysteresisCounters(state);
            debugLastShouldOcclude = false;
            debugMotionReleaseGraceRemaining = 0;
            debugLastRejectReason = "Height-depth-pixel priority: release because height relation is no longer valid.";
            RememberLogicalAnchor(state, logicalAnchor, hasLogicalAnchor);
            return false;
        }

        // V46：边缘释放锁。
        // V45 已经做到“进入遮挡很敏感”，但极限边缘还可能因为下一帧普通采样/BehindVolume 漏判而立刻释放。
        // 这里不主动制造新遮挡，只在 state.occluding 已经为 true 时生效：
        // 只要主相机画面上角色视觉轮廓仍和当前遮挡物有一像素级重叠，就继续保持几帧，
        // 直到连续离开边缘后再允许退出。
        if (state.occluding && useEdgeOverlapReleaseLatch)
        {
            if (ShouldKeepOcclusionByEdgeOverlapReleaseLatch(state))
            {
                state.edgeOverlapReleaseLatchRemaining = Mathf.Max(state.edgeOverlapReleaseLatchRemaining, Mathf.Max(0, edgeOverlapReleaseLatchFrames));
                debugMotionReleaseGraceRemaining = Mathf.Max(debugMotionReleaseGraceRemaining, state.edgeOverlapReleaseLatchRemaining);
                debugLastShouldOcclude = true;
                debugLastRejectReason = "Edge overlap release latch: unit visual silhouette still overlaps current occluder in main camera.";
                RememberLogicalAnchor(state, logicalAnchor, hasLogicalAnchor);
                return true;
            }

            if (state.edgeOverlapReleaseLatchRemaining > 0)
            {
                state.edgeOverlapReleaseLatchRemaining--;
                debugMotionReleaseGraceRemaining = Mathf.Max(debugMotionReleaseGraceRemaining, state.edgeOverlapReleaseLatchRemaining);
                debugLastShouldOcclude = true;
                debugLastRejectReason = "Edge overlap release latch: wait stable all-clear frames before release.";
                RememberLogicalAnchor(state, logicalAnchor, hasLogicalAnchor);
                return true;
            }
        }

        if (state.occluding && useCommercialPixelExitGuard)
        {
            if (ShouldKeepOcclusionForCommercialExit(state))
            {
                state.commercialExitGuardRemaining = Mathf.Max(state.commercialExitGuardRemaining, Mathf.Max(0, commercialExitGuardFrames));
                debugMotionReleaseGraceRemaining = state.commercialExitGuardRemaining;
                debugLastShouldOcclude = true;
                RememberLogicalAnchor(state, logicalAnchor, hasLogicalAnchor);
                return true;
            }

            if (state.commercialExitGuardRemaining > 0)
            {
                state.commercialExitGuardRemaining--;
                debugMotionReleaseGraceRemaining = state.commercialExitGuardRemaining;
                debugLastShouldOcclude = true;
                debugLastRejectReason = "Commercial exit guard: wait all-clear frames before release.";
                RememberLogicalAnchor(state, logicalAnchor, hasLogicalAnchor);
                return true;
            }
        }

        // 高速奔跑贴边时，几何判定可能有一帧漏采样。
        // 只在“上一状态已经遮挡”时保留极短释放宽限，不主动制造新遮挡。
        if (state.occluding && state.releaseGraceRemaining > 0)
        {
            state.releaseGraceRemaining--;
            debugMotionReleaseGraceRemaining = state.releaseGraceRemaining;
            debugLastShouldOcclude = true;
            debugLastRejectReason = "Release hysteresis: keep occlusion for motion continuity.";
            RememberLogicalAnchor(state, logicalAnchor, hasLogicalAnchor);
            return true;
        }

        state.releaseGraceRemaining = 0;
        state.commercialExitGuardRemaining = 0;
        debugMotionReleaseGraceRemaining = 0;
        RememberLogicalAnchor(state, logicalAnchor, hasLogicalAnchor);
        return false;
    }


    private void ClearOcclusionHysteresisCounters(CandidateState state)
    {
        if (state == null || !clearOcclusionHysteresisWhenFrontSide)
            return;

        state.releaseGraceRemaining = 0;
        state.commercialExitGuardRemaining = 0;
        state.onePixelEnterHoldRemaining = 0;
        state.edgeOverlapReleaseLatchRemaining = 0;
        state.frontSideReleaseConfirmRemaining = 0;
    }

    private void ClearOcclusionHysteresisCountersForce(CandidateState state)
    {
        if (state == null)
            return;

        state.releaseGraceRemaining = 0;
        state.commercialExitGuardRemaining = 0;
        state.onePixelEnterHoldRemaining = 0;
        state.edgeOverlapReleaseLatchRemaining = 0;
        state.frontSideReleaseConfirmRemaining = 0;
    }

    private bool IsLogicalAnchorClearlyInFrontOfCurrentOccluder(Vector3 logicalAnchor, CandidateState state)
    {
        if (!useRayToLogicalAnchorForFrontSideRelease)
            return false;

        if (sourceCamera == null)
            return false;

        Vector3 cameraPosition = sourceCamera.transform.position;
        Vector3 toAnchor = logicalAnchor - cameraPosition;
        float distance = toAnchor.magnitude;
        if (distance <= 0.0001f)
            return false;

        Ray ray = new Ray(cameraPosition, toAnchor / distance);
        bool blockedBeforeAnchor = IsRayBlockedByCurrentOccluder(
            ray,
            Mathf.Max(0f, distance - Mathf.Max(0f, depthEpsilon)),
            state != null ? state.unitRoot : null);

        if (debugDrawFrontSideReleaseOverride && ShouldDrawRuntimeDebug())
            Debug.DrawLine(cameraPosition, logicalAnchor, blockedBeforeAnchor ? Color.red : Color.green, debugDrawDuration);

        return !blockedBeforeAnchor;
    }

    private bool ShouldKeepOcclusionForCommercialExit(CandidateState state)
    {
        if (state == null || state.unitRoot == null)
            return false;

        if (keepWhileSpinePartSamplerBlocked)
        {
            SpinePartOcclusionSampler sampler = state.unitRoot.GetComponentInChildren<SpinePartOcclusionSampler>(true);
            if (sampler != null && sampler.AnyOccluded)
            {
                debugLastRejectReason = "Commercial exit guard: SpinePartOcclusionSampler still blocked.";
                return true;
            }
        }

        if (keepWhileActiveOccludedProxyOverlapsOccluder && IsActiveOccludedProxyStillProjectedOverCurrentOccluder(state))
        {
            debugLastRejectReason = "Commercial exit guard: active OccludedProxy still overlaps current occluder projection.";
            return true;
        }

        return false;
    }

    private bool ShouldApplyFrontBodyHardVeto(CandidateState state, Vector3 logicalAnchor)
    {
        if (!useHeightDepthPixelPriority || !useFrontBodyHardVeto)
            return false;

        if (state == null || sourceCamera == null)
            return false;

        if (frontBodyHardVetoDoesNotCancelOverheadOcclusion && useOverheadHeightOcclusion && IsCurrentOccluderOverheadOfUnit(state, logicalAnchor))
            return false;

        return IsLogicalAnchorClearlyInFrontOfCurrentOccluder(logicalAnchor, state);
    }

    private bool IsActiveOccludedProxyStillProjectedOverCurrentOccluder(CandidateState state)
    {
        if (sourceCamera == null || state == null || state.unitRoot == null)
            return false;

        if (!TryGetCurrentOccluderVisualBounds(out Bounds occluderBounds))
            return false;

        if (!TryGetActiveOccludedProxyRendererBounds(state.unitRoot, out Bounds proxyBounds))
            return false;

        if (!TryBuildViewportRectFromBounds(occluderBounds, commercialExitProjectionPadding, out Rect occluderRect))
            return false;

        if (!TryBuildViewportRectFromBounds(proxyBounds, commercialExitProjectionPadding, out Rect proxyRect))
            return false;

        bool overlap = RectsOverlap(occluderRect, proxyRect);

        if (debugDrawCommercialExitGuard && ShouldDrawRuntimeDebug())
            Debug.DrawLine(occluderBounds.center, proxyBounds.center, overlap ? Color.yellow : Color.gray, debugDrawDuration);

        return overlap;
    }

    private bool TryGetCurrentOccluderVisualBounds(out Bounds bounds)
    {
        bounds = default;
        bool has = false;

        if (currentOccluderRenderers.Count == 0 && occluderContentRoot != null)
            RebuildCurrentOccluderColliders();

        for (int i = 0; i < currentOccluderRenderers.Count; i++)
        {
            Renderer r = currentOccluderRenderers[i];
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

        if (has)
            return true;

        return TryGetRendererBounds(occluderContentRoot, true, out bounds);
    }

    private bool TryGetActiveOccludedProxyRendererBounds(Transform unitRoot, out Bounds bounds)
    {
        bounds = default;
        if (unitRoot == null)
            return false;

        Renderer[] renderers = unitRoot.GetComponentsInChildren<Renderer>(true);
        bool has = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;

            string n = r.name;
            string path = GetTransformPath(r.transform);
            if ((string.IsNullOrEmpty(n) || !n.Contains("_Occluded")) &&
                (string.IsNullOrEmpty(path) || !path.Contains("_Occluded")))
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

    private void RememberLogicalAnchor(CandidateState state, Vector3 logicalAnchor, bool hasLogicalAnchor)
    {
        if (state == null || !hasLogicalAnchor)
            return;

        state.lastLogicalAnchor = logicalAnchor;
        state.hasLastLogicalAnchor = true;
    }

    private bool EvaluateOcclusionAtLogicalAnchor(CandidateState state, Vector3 logicalAnchor)
    {
        if (sourceCamera == null)
        {
            debugLastRejectReason = "No source camera.";
            return false;
        }

        if (useOverheadHeightOcclusion && IsCurrentOccluderOverheadOfUnit(state, logicalAnchor))
        {
            debugLastShouldOcclude = true;
            debugLastRejectReason = "Overhead height occlusion.";
            return true;
        }

        // V50/V55：商品级遮挡优先级第一层——上下高度关系。
        // 头顶物体已在上面优先处理；普通侧向遮挡必须先确认当前物体高度范围
        // 与单位视觉高度范围有实际重叠。否则后面的前后深度和像素交集都不能单独开启遮挡。
        if (useHeightDepthPixelPriority && useSideOcclusionHeightGate && !IsSideOcclusionHeightRelationValid(state, logicalAnchor))
        {
            debugLastRejectReason = "Height-depth-pixel priority: side occlusion rejected by height relation gate.";
            return false;
        }

        bool logicalAnchorInsideBehindVolume = true;
        bool depthEstablishedForPixelGate = true;
        if (requireUnitBodyInsideCameraFacingBehindVolume)
        {
            // V55：这里拆成两个概念：
            // 1. logicalAnchorInsideBehindVolume：旧的完整 BehindVolume，包含左右粗范围。
            // 2. depthEstablishedForPixelGate：只表示前后深度已经成立。
            // 一旦 depthEstablishedForPixelGate 成立，左右边缘不再继续交给脚底/UnitBody 决定，而交给 Main Camera 像素交集决定。
            logicalAnchorInsideBehindVolume = IsLogicalAnchorInsideCameraFacingBehindVolume(logicalAnchor);
            depthEstablishedForPixelGate = onePixelEnterUsesDepthOnlyBehindGate
                ? IsLogicalAnchorBehindCameraFacingDepthPlane(logicalAnchor)
                : logicalAnchorInsideBehindVolume;

            if (!depthEstablishedForPixelGate)
            {
                // 前后深度没成立时，像素交集不能越权。
                if (ShouldApplyFrontBodyHardVeto(state, logicalAnchor))
                {
                    ClearFrontBodyVetoHysteresisIfNeeded(state);
                    debugLastShouldOcclude = false;
                    debugLastRejectReason = "V55 strict priority: depth relation rejected before pixel intersection.";
                    return false;
                }

                if (!logicalAnchorInsideBehindVolume && !allowVisualSampleInsideBehindVolumeToConfirm && !useVisualWidthWithUnifiedUnitDepth)
                {
                    debugLastRejectReason = string.IsNullOrEmpty(debugBehindVolumeRejectReason)
                        ? "Depth relation is not established."
                        : debugBehindVolumeRejectReason;
                    return false;
                }
            }
        }
        else
        {
            debugLogicalAnchorInsideBehindVolume = true;
            depthEstablishedForPixelGate = true;
        }

        // V55：当前后深度成立后，脚底/UnitBody 不再和像素交集抢裁判权。
        // 这时遮挡是否成立完全交给 Main Camera 下真实角色 Renderer 与当前物体的像素交集决定。
        if (pixelIntersectionFinalGateAfterDepthEstablished && depthEstablishedForPixelGate)
        {
            if (EvaluateDepthEstablishedPixelIntersectionGate(state, logicalAnchor))
            {
                state.onePixelEnterHoldRemaining = Mathf.Max(state.onePixelEnterHoldRemaining, Mathf.Max(0, onePixelEnterHoldFrames));
                state.frontSideReleaseConfirmRemaining = 0;
                debugLastShouldOcclude = true;
                debugLastRejectReason = "V56 depth satisfied: final decision accepted by Main Camera pixel intersection only.";
                return true;
            }

            debugLastShouldOcclude = false;
            debugLastRejectReason = "V56 depth satisfied: pixel intersection final gate rejected occlusion.";
            return false;
        }

        // V51：只有当前后深度未进入 V55 像素最终裁判时，才允许身体正面硬否决提前退出。
        if (ShouldApplyFrontBodyHardVeto(state, logicalAnchor))
        {
            ClearFrontBodyVetoHysteresisIfNeeded(state);
            debugLastShouldOcclude = false;
            debugLastRejectReason = "V55 front/body hard veto before depth-established pixel gate.";
            return false;
        }

        // V44：投影占位不能默认反向驱动角色本体遮挡。
        // 投影裁切已经由 ShadowOccluderMask / UnitGroundShadowFollowerTerrain 像素级处理；
        // 如果让投影在这里单独打开 frontOccluderRoot，会导致背后物体因为投影命中而把角色本体压住。
        if (useProjectionFootprintAsOcclusionParticipant &&
            projectionFootprintMayOpenCharacterOcclusion &&
            (!projectionFootprintRequiresBodyBehindVolume || logicalAnchorInsideBehindVolume) &&
            EvaluateProjectionFootprintOcclusion(state))
        {
            debugLastShouldOcclude = true;
            debugLastRejectReason = "Projection footprint character occlusion participant.";
            return true;
        }

        CollectUnitSamples(state.unitRoot, tempSamples, out int rendererCount);
        debugLastResolvedRendererCount = rendererCount;
        debugSampleCount += tempSamples.Count;

        if (tempSamples.Count == 0)
        {
            debugLastRejectReason = "No unit samples.";
            return false;
        }

        Plane logicalPlane = new Plane(sourceCamera.transform.forward, logicalAnchor);
        int blockedCount = 0;

        for (int i = 0; i < tempSamples.Count; i++)
        {
            Vector3 visualSample = tempSamples[i];
            Vector3 sweepTarget = visualSample;
            Vector3 logicalSample = visualSample;

            if (useVisualWidthWithUnifiedUnitDepth)
            {
                // 角色只允许有一个真实前后深度：UnitBody / UnitPhysicsProbe / UnitBodyCollider。
                // 武器、衣服、头发、身体采样点只贡献左右宽度与可选高度。
                sweepTarget = BuildUnifiedDepthVisualWidthPoint(visualSample, logicalAnchor);
                logicalSample = sweepTarget;
            }
            else if (useUnitLogicalDepthPlane)
            {
                Vector3 screen = sourceCamera.WorldToScreenPoint(visualSample);
                if (screen.z <= 0f)
                {
                    debugClearCount++;
                    continue;
                }

                Ray screenRay = sourceCamera.ScreenPointToRay(screen);
                if (!logicalPlane.Raycast(screenRay, out float enter))
                {
                    debugClearCount++;
                    continue;
                }

                logicalSample = screenRay.GetPoint(enter);
                if (!useVisualSampleWorldPositionForSensitiveSweep)
                    sweepTarget = logicalSample;
            }

            bool insideBehindVolume = true;
            if (requireUnitBodyInsideCameraFacingBehindVolume)
            {
                if (useVisualWidthWithUnifiedUnitDepth)
                {
                    // 用“视觉横向宽度 + UnitBody唯一深度”的点检查后方体积。
                    // 这样武器能扩大左右触发范围，但不能产生自己的前后深度。
                    insideBehindVolume = IsLogicalAnchorInsideCameraFacingBehindVolume(sweepTarget);
                }
                else if (!logicalAnchorInsideBehindVolume && allowVisualSampleInsideBehindVolumeToConfirm)
                {
                    insideBehindVolume = IsLogicalAnchorInsideCameraFacingBehindVolume(visualSample);
                }
                else
                {
                    insideBehindVolume = logicalAnchorInsideBehindVolume;
                }

                if (!insideBehindVolume)
                {
                    debugClearCount++;
                    if (ShouldDrawRuntimeDebug() && debugDrawSamples)
                    {
                        Debug.DrawLine(sourceCamera.transform.position, sweepTarget, debugClearColor, debugDrawDuration);
                        Debug.DrawLine(visualSample, sweepTarget, debugLogicalPointColor, debugDrawDuration);
                    }
                    continue;
                }
            }

            Ray ray = new Ray(sourceCamera.transform.position, (sweepTarget - sourceCamera.transform.position).normalized);
            float sampleDistance = Vector3.Distance(sourceCamera.transform.position, sweepTarget);
            bool blocked = IsRayBlockedByCurrentOccluder(ray, sampleDistance, state.unitRoot);

            if (ShouldDrawRuntimeDebug() && debugDrawSamples)
            {
                Color color = blocked ? debugBlockedColor : debugClearColor;
                Debug.DrawLine(sourceCamera.transform.position, sweepTarget, color, debugDrawDuration);
                Debug.DrawLine(visualSample, sweepTarget, debugLogicalPointColor, debugDrawDuration);
            }

            if (blocked)
            {
                blockedCount++;
                if (blockedCount >= Mathf.Max(1, minBlockedSampleCount))
                {
                    debugLastShouldOcclude = true;
                    return true;
                }
            }
        }

        // V54：如果高度关系已经成立，且某个角色可见 Renderer 的屏幕像素与当前遮挡物发生重叠，
        // 并且从 Main Camera 射线看当前遮挡物确实在该角色部件之前，则正式开启遮挡。
        // 这一步把“深度成立后的像素交集”写入判定链，不再只看 UnitBody / 脚底站位。
        if (ShouldEnterOcclusionByVisualRendererPixelOverlapDepth(state, logicalAnchor, "ordinary renderer-pixel edge"))
        {
            state.onePixelEnterHoldRemaining = Mathf.Max(state.onePixelEnterHoldRemaining, Mathf.Max(0, onePixelEnterHoldFrames));
            state.frontSideReleaseConfirmRemaining = 0;
            debugLastShouldOcclude = true;
            debugLastRejectReason = "V54 visual renderer pixel-depth gate: visible character renderer pixel is blocked by current occluder.";
            return true;
        }

        if (ShouldEnterOcclusionByVisualPartPixelDepth(state, logicalAnchor, "ordinary point edge"))
        {
            state.onePixelEnterHoldRemaining = Mathf.Max(state.onePixelEnterHoldRemaining, Mathf.Max(0, onePixelEnterHoldFrames));
            state.frontSideReleaseConfirmRemaining = 0;
            debugLastShouldOcclude = true;
            debugLastRejectReason = "V53 visual part pixel-depth gate: weapon/body/hair visible sample is blocked by current occluder.";
            return true;
        }

        if (useOnePixelEnterGuard)
        {
            bool coarseFrontSide = frontSideBlocksOnePixelEnterGuard && IsLogicalAnchorClearlyInFrontOfCurrentOccluder(logicalAnchor, state);
            bool pixelEnter = ShouldEnterOcclusionByOnePixelOverlap(state, logicalAnchor, logicalPlane);

            // V50：上下高度关系 > 前后深度关系 > 像素交集。
            // 像素级进入只负责补足边缘敏感度；它不能越过高度门槛，
            // 也不能在 UnitBody / 后方体积已经明确处于正面时单独把角色拉进遮挡。
            if (pixelEnter && (!coarseFrontSide || pixelEnterCanOverrideCoarseFrontSideBlock))
            {
                state.onePixelEnterHoldRemaining = Mathf.Max(state.onePixelEnterHoldRemaining, Mathf.Max(0, onePixelEnterHoldFrames));
                state.frontSideReleaseConfirmRemaining = 0;
                debugLastShouldOcclude = true;
                debugLastRejectReason = coarseFrontSide
                    ? "One pixel enter guard: pixel hit accepted by explicit override."
                    : "One pixel enter guard: visual overlap pixel is blocked by current occluder and front/back relation allows it.";
                return true;
            }

            if (coarseFrontSide && !pixelEnter)
                debugLastRejectReason = "One pixel enter guard blocked: logical anchor is clearly in front and no pixel ray is blocked.";
        }

        if (state.onePixelEnterHoldRemaining > 0)
        {
            state.onePixelEnterHoldRemaining--;
            debugLastShouldOcclude = true;
            debugLastRejectReason = "One pixel enter guard: hold frames after edge hit.";
            return true;
        }

        debugLastRejectReason = blockedCount <= 0 ? "No sample blocked by current VisualRoot." : "Blocked count below threshold.";
        return false;
    }


    private bool EvaluateDepthEstablishedPixelIntersectionGate(CandidateState state, Vector3 logicalAnchor)
    {
        if (state == null || state.unitRoot == null || sourceCamera == null)
            return false;

        bool accepted = false;

        if (useDepthEstablishedDenseViewportPixelGate &&
            ShouldEnterOcclusionByDenseViewportPixelGateAfterDepthEstablished(state, logicalAnchor))
        {
            accepted = true;
        }
        else if (depthEstablishedGateUseRendererPixelOverlap &&
            ShouldEnterOcclusionByVisualRendererPixelOverlapDepth(state, logicalAnchor, "V58 depth-established renderer pixel fallback", finalPixelGateSkipsPerSampleDepthGateAfterDepthEstablished))
        {
            accepted = true;
        }
        else if (depthEstablishedGateUseVisualPartPointFallback &&
            ShouldEnterOcclusionByVisualPartPixelDepth(state, logicalAnchor, "V56 depth-established visual part point fallback", finalPixelGateSkipsPerSampleDepthGateAfterDepthEstablished))
        {
            accepted = true;
        }
        else if (depthEstablishedGateUseOnePixelOverlapFallback && useOnePixelEnterGuard)
        {
            Plane logicalPlane = new Plane(sourceCamera.transform.forward, logicalAnchor);
            accepted = ShouldEnterOcclusionByOnePixelOverlap(state, logicalAnchor, logicalPlane);
        }

        return accepted;
    }


    private bool ShouldEnterOcclusionByDenseViewportPixelGateAfterDepthEstablished(CandidateState state, Vector3 logicalAnchor)
    {
        if (state == null || state.unitRoot == null || sourceCamera == null)
            return false;

        if (!TryGetCurrentOccluderVisualBounds(out Bounds occluderBounds))
            return false;

        float padding = Mathf.Max(0f, depthEstablishedDensePixelPadding);
        if (!TryBuildViewportRectFromBounds(occluderBounds, padding, out Rect occluderRect))
            return false;

        Renderer[] renderers = state.unitRoot.GetComponentsInChildren<Renderer>(includeInactiveUnitRenderers);
        if (renderers == null || renderers.Length == 0)
            return false;

        int grid = Mathf.Clamp(depthEstablishedDensePixelGrid, 2, 25);
        int maxSamples = Mathf.Max(1, depthEstablishedDensePixelMaxSamples);
        int tested = 0;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            if (excludeProjectionRenderersFromCharacterOcclusionSamples && IsProjectionOrShadowFootprintRenderer(r))
                continue;

            if (IsExcludedUnitRenderer(r))
                continue;

            Bounds rb = r.bounds;

            // 高度仍然是最高资格门槛。这里用 Renderer 自己的高度，避免脚底代表武器/头发。
            if (useHeightDepthPixelPriority && useSideOcclusionHeightGate && !IsRendererHeightOverlappingCurrentOccluderSideRange(rb))
                continue;

            if (!TryBuildViewportRectFromBounds(rb, padding, out Rect rendererRect))
                continue;

            if (!TryGetViewportIntersection(occluderRect, rendererRect, out Rect overlapRect))
                continue;

            float minPixels = Mathf.Max(0f, onePixelEnterMinOverlapPixels);
            if (sourceCamera.pixelWidth > 0 && sourceCamera.pixelHeight > 0 && minPixels > 0f)
            {
                float overlapPixelsX = overlapRect.width * sourceCamera.pixelWidth;
                float overlapPixelsY = overlapRect.height * sourceCamera.pixelHeight;
                if (overlapPixelsX < minPixels && overlapPixelsY < minPixels)
                    continue;
            }

            // 深度已经由外层确认。这里不再用 UnitBody / 脚底或单点局部深度二次否决，
            // 只用 Renderer 自己的摄像机深度平面把 viewport 像素投回角色可见部件附近。
            Plane rendererDepthPlane = new Plane(sourceCamera.transform.forward, rb.center);

            for (int y = 0; y < grid; y++)
            {
                float fy = grid <= 1 ? 0.5f : (y + 0.5f) / grid;
                float vpY = Mathf.Lerp(overlapRect.yMin, overlapRect.yMax, fy);

                for (int x = 0; x < grid; x++)
                {
                    float fx = grid <= 1 ? 0.5f : (x + 0.5f) / grid;
                    float vpX = Mathf.Lerp(overlapRect.xMin, overlapRect.xMax, fx);
                    Ray ray = sourceCamera.ViewportPointToRay(new Vector3(vpX, vpY, 0f));

                    if (!rendererDepthPlane.Raycast(ray, out float enter))
                        continue;

                    if (enter <= 0.0001f)
                        continue;

                    Vector3 target = ray.GetPoint(enter);
                    float distance = Vector3.Distance(sourceCamera.transform.position, target);
                    if (distance <= 0.0001f)
                        continue;

                    tested++;
                    bool blocked = IsRayBlockedByCurrentOccluder(ray, distance, state.unitRoot);

                    if (debugDrawOnePixelEnterGuard && ShouldDrawRuntimeDebug())
                        Debug.DrawLine(sourceCamera.transform.position, target, blocked ? Color.green : Color.gray, debugDrawDuration);

                    if (blocked)
                    {
                        debugLastRejectReason = "V58 dense viewport pixel gate accepted occlusion after depth established.";
                        return true;
                    }

                    if (tested >= maxSamples)
                        return false;
                }
            }
        }

        return false;
    }

    private void ClearFrontBodyVetoHysteresisIfNeeded(CandidateState state)
    {
        if (state == null || !clearHysteresisOnFrontBodyHardVeto)
            return;

        state.onePixelEnterHoldRemaining = 0;
        state.edgeOverlapReleaseLatchRemaining = 0;
        state.commercialExitGuardRemaining = 0;
        state.releaseGraceRemaining = 0;
        state.frontSideReleaseConfirmRemaining = Mathf.Max(state.frontSideReleaseConfirmRemaining, Mathf.Max(1, frontSideReleaseConfirmFrames));
    }


    private bool EvaluateProjectionFootprintOcclusion(CandidateState state)
    {
        if (state == null || state.unitRoot == null || sourceCamera == null)
            return false;

        CollectProjectionFootprintSamples(state.unitRoot, tempProjectionFootprintSamples);
        if (tempProjectionFootprintSamples.Count == 0)
            return false;

        int blockedCount = 0;
        int required = Mathf.Max(1, projectionFootprintMinBlockedSampleCount);

        for (int i = 0; i < tempProjectionFootprintSamples.Count; i++)
        {
            Vector3 sample = tempProjectionFootprintSamples[i];
            Vector3 target = projectionFootprintUsesOwnWorldDepth ? sample : BuildUnifiedDepthVisualWidthPoint(sample, state.hasLastLogicalAnchor ? state.lastLogicalAnchor : state.unitRoot.position);

            Vector3 screen = sourceCamera.WorldToScreenPoint(target);
            if (screen.z <= 0f)
                continue;

            Ray ray = new Ray(sourceCamera.transform.position, (target - sourceCamera.transform.position).normalized);
            float sampleDistance = Vector3.Distance(sourceCamera.transform.position, target);
            bool blocked = IsRayBlockedByCurrentOccluder(ray, sampleDistance, state.unitRoot);

            if (debugDrawProjectionFootprintOcclusion && ShouldDrawRuntimeDebug())
            {
                Debug.DrawLine(sourceCamera.transform.position, target, blocked ? Color.magenta : Color.gray, debugDrawDuration);
                Debug.DrawLine(sample, target, Color.cyan, debugDrawDuration);
            }

            if (blocked)
            {
                blockedCount++;
                if (blockedCount >= required)
                    return true;
            }
        }

        return false;
    }

    private void CollectProjectionFootprintSamples(Transform unitRoot, List<Vector3> samples)
    {
        samples.Clear();
        if (unitRoot == null)
            return;

        Renderer[] renderers = unitRoot.GetComponentsInChildren<Renderer>(includeInactiveUnitRenderers);
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            if (!IsForcedVisualFootprintRenderer(r))
                continue;

            AddBoundsSamples(r.bounds, samples);
        }
    }



    private bool ShouldKeepOcclusionByEdgeOverlapReleaseLatch(CandidateState state)
    {
        if (state == null || state.unitRoot == null || sourceCamera == null)
            return false;

        if (!TryGetCurrentOccluderVisualBounds(out Bounds occluderBounds))
            return false;

        if (!TryGetUnitCharacterOcclusionRendererBounds(state.unitRoot, out Bounds unitBounds))
            return false;

        float padding = Mathf.Max(0f, edgeOverlapReleaseProjectionPadding);
        if (!TryBuildViewportRectFromBounds(occluderBounds, padding, out Rect occluderRect))
            return false;

        if (!TryBuildViewportRectFromBounds(unitBounds, padding, out Rect unitRect))
            return false;

        if (!TryGetViewportIntersection(occluderRect, unitRect, out Rect overlapRect))
            return false;

        float minPixels = Mathf.Max(0f, edgeOverlapReleaseMinOverlapPixels);
        if (sourceCamera.pixelWidth > 0 && sourceCamera.pixelHeight > 0 && minPixels > 0f)
        {
            float overlapPixelsX = overlapRect.width * sourceCamera.pixelWidth;
            float overlapPixelsY = overlapRect.height * sourceCamera.pixelHeight;
            if (overlapPixelsX < minPixels && overlapPixelsY < minPixels)
                return false;
        }

        if (debugDrawEdgeOverlapReleaseLatch && ShouldDrawRuntimeDebug())
        {
            Ray ray = sourceCamera.ViewportPointToRay(new Vector3(overlapRect.center.x, overlapRect.center.y, 0f));
            Debug.DrawRay(ray.origin, ray.direction * 8f, Color.yellow, debugDrawDuration);
        }

        return true;
    }

    private bool ShouldEnterOcclusionByVisualRendererPixelOverlapDepth(CandidateState state, Vector3 logicalAnchor, string context)
    {
        return ShouldEnterOcclusionByVisualRendererPixelOverlapDepth(state, logicalAnchor, context, false);
    }

    private bool ShouldEnterOcclusionByVisualRendererPixelOverlapDepth(CandidateState state, Vector3 logicalAnchor, string context, bool skipPerSampleDepthGate)
    {
        if (!useVisualRendererPixelOverlapDepthGate)
            return false;

        if (state == null || state.unitRoot == null || sourceCamera == null)
            return false;

        if (!TryGetCurrentOccluderVisualBounds(out Bounds occluderBounds))
            return false;

        float padding = Mathf.Max(0f, visualRendererPixelOverlapPadding);
        if (!TryBuildViewportRectFromBounds(occluderBounds, padding, out Rect occluderRect))
            return false;

        Renderer[] renderers = state.unitRoot.GetComponentsInChildren<Renderer>(includeInactiveUnitRenderers);
        if (renderers == null || renderers.Length == 0)
            return false;

        int blockedCount = 0;
        int required = Mathf.Max(1, visualRendererPixelOverlapMinBlockedSampleCount);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            if (excludeProjectionRenderersFromCharacterOcclusionSamples && IsProjectionOrShadowFootprintRenderer(r))
                continue;

            if (IsExcludedUnitRenderer(r))
                continue;

            Bounds rb = r.bounds;

            // 高度关系是第一优先级。Renderer 自身没有和当前物体高度范围发生关系时，不能靠屏幕交集硬开遮挡。
            if (useHeightDepthPixelPriority && useSideOcclusionHeightGate && !IsRendererHeightOverlappingCurrentOccluderSideRange(rb))
                continue;

            if (!TryBuildViewportRectFromBounds(rb, padding, out Rect rendererRect))
                continue;

            if (!TryGetViewportIntersection(occluderRect, rendererRect, out Rect overlapRect))
                continue;

            float minPixels = Mathf.Max(0f, onePixelEnterMinOverlapPixels);
            if (sourceCamera.pixelWidth > 0 && sourceCamera.pixelHeight > 0 && minPixels > 0f)
            {
                float overlapPixelsX = overlapRect.width * sourceCamera.pixelWidth;
                float overlapPixelsY = overlapRect.height * sourceCamera.pixelHeight;
                if (overlapPixelsX < minPixels && overlapPixelsY < minPixels)
                    continue;
            }

            tempViewportOverlapSamples.Clear();
            tempViewportOverlapSamples.Add(overlapRect.center);
            if (visualRendererPixelOverlapSampleCorners)
            {
                tempViewportOverlapSamples.Add(new Vector2(overlapRect.xMin, overlapRect.yMin));
                tempViewportOverlapSamples.Add(new Vector2(overlapRect.xMin, overlapRect.yMax));
                tempViewportOverlapSamples.Add(new Vector2(overlapRect.xMax, overlapRect.yMin));
                tempViewportOverlapSamples.Add(new Vector2(overlapRect.xMax, overlapRect.yMax));
            }

            // 用该 Renderer 自身的深度平面，而不是 UnitBody / 脚底逻辑平面。
            // 这样武器尖端、头发、衣服边缘的屏幕像素能用自己的 Main Camera 深度参与判定。
            Plane rendererDepthPlane = new Plane(sourceCamera.transform.forward, rb.center);

            for (int s = 0; s < tempViewportOverlapSamples.Count; s++)
            {
                Vector2 vp = tempViewportOverlapSamples[s];
                Ray ray = sourceCamera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));

                if (!rendererDepthPlane.Raycast(ray, out float enter))
                    continue;

                if (enter <= 0.0001f)
                    continue;

                Vector3 target = ray.GetPoint(enter);

                // V56：如果外层已经确认“后方深度关系成立”，这里不能再用每个 Renderer / 武器点的局部深度
                // 做第二次否决。否则最终像素裁判又会退回脚底/局部深度抢裁判权。
                // 非最终门控路径仍保留这层深度检查，避免像素交集越权。
                if (!skipPerSampleDepthGate && requireUnitBodyInsideCameraFacingBehindVolume)
                {
                    bool depthAllows = onePixelEnterUsesDepthOnlyBehindGate
                        ? IsLogicalAnchorBehindCameraFacingDepthPlane(target)
                        : IsLogicalAnchorInsideCameraFacingBehindVolume(target);

                    if (!depthAllows)
                        continue;
                }

                float distance = Vector3.Distance(sourceCamera.transform.position, target);
                if (distance <= 0.0001f)
                    continue;

                bool blocked = IsRayBlockedByCurrentOccluder(ray, distance, state.unitRoot);
                if (debugDrawOnePixelEnterGuard && ShouldDrawRuntimeDebug())
                    Debug.DrawLine(sourceCamera.transform.position, target, blocked ? Color.green : Color.gray, debugDrawDuration);

                if (!blocked)
                    continue;

                blockedCount++;
                if (blockedCount >= required)
                    return true;
            }
        }

        return false;
    }

    private bool IsRendererHeightOverlappingCurrentOccluderSideRange(Bounds rendererBounds)
    {
        if (!TryGetCurrentOccluderVisualBounds(out Bounds occluderBounds))
            return true;

        float padding = Mathf.Max(0f, sideOcclusionHeightPadding);
        float minA = rendererBounds.min.y;
        float maxA = rendererBounds.max.y;
        float minB = occluderBounds.min.y - padding;
        float maxB = occluderBounds.max.y + padding;
        float overlap = Mathf.Min(maxA, maxB) - Mathf.Max(minA, minB);
        return overlap >= Mathf.Max(0f, minSideOcclusionHeightOverlap);
    }

    private bool ShouldEnterOcclusionByVisualPartPixelDepth(CandidateState state, Vector3 logicalAnchor, string context)
    {
        return ShouldEnterOcclusionByVisualPartPixelDepth(state, logicalAnchor, context, false);
    }

    private bool ShouldEnterOcclusionByVisualPartPixelDepth(CandidateState state, Vector3 logicalAnchor, string context, bool skipPerSampleDepthGate)
    {
        if (!useVisualPartPixelDepthGate)
            return false;

        if (state == null || state.unitRoot == null || sourceCamera == null)
            return false;

        CollectUnitSamples(state.unitRoot, tempSamples, out int rendererCount);
        if (tempSamples.Count == 0)
            return false;

        int blockedCount = 0;
        int required = Mathf.Max(1, visualPartPixelDepthMinBlockedSampleCount);

        for (int i = 0; i < tempSamples.Count; i++)
        {
            Vector3 visualSample = tempSamples[i];
            Vector3 target = visualPartPixelDepthUsesActualWorldDepth
                ? visualSample
                : BuildUnifiedDepthVisualWidthPoint(visualSample, logicalAnchor);

            Vector3 screen = sourceCamera.WorldToScreenPoint(target);
            if (screen.z <= 0f)
                continue;

            // 第一优先级仍然是高度关系：视觉部件自己的高度必须和当前遮挡物有关系。
            // 这里不再用脚底 / UnitBody 高度去代表武器或头发的像素边缘。
            if (useHeightDepthPixelPriority && useSideOcclusionHeightGate)
            {
                if (!IsVisualPartHeightInsideCurrentOccluderSideRange(target))
                    continue;
            }

            // V56：最终像素裁判路径里，外层已经确认后方深度成立；这里不再让局部采样点重复做深度否决。
            if (!skipPerSampleDepthGate && requireUnitBodyInsideCameraFacingBehindVolume)
            {
                bool depthAllows = onePixelEnterUsesDepthOnlyBehindGate
                    ? IsLogicalAnchorBehindCameraFacingDepthPlane(target)
                    : IsLogicalAnchorInsideCameraFacingBehindVolume(target);

                if (!depthAllows)
                    continue;
            }

            Ray ray = new Ray(sourceCamera.transform.position, (target - sourceCamera.transform.position).normalized);
            float distance = Vector3.Distance(sourceCamera.transform.position, target);
            if (distance <= 0.0001f)
                continue;

            bool blocked = IsRayBlockedByCurrentOccluder(ray, distance, state.unitRoot);
            if (debugDrawOnePixelEnterGuard && ShouldDrawRuntimeDebug())
                Debug.DrawLine(sourceCamera.transform.position, target, blocked ? Color.green : Color.gray, debugDrawDuration);

            if (!blocked)
                continue;

            blockedCount++;
            if (blockedCount >= required)
                return true;
        }

        return false;
    }

    private bool IsVisualPartHeightInsideCurrentOccluderSideRange(Vector3 visualPartWorldPoint)
    {
        if (!TryGetCurrentOccluderVisualBounds(out Bounds occluderBounds))
            return true;

        float padding = Mathf.Max(0f, sideOcclusionHeightPadding);
        float y = visualPartWorldPoint.y;
        return y >= occluderBounds.min.y - padding && y <= occluderBounds.max.y + padding;
    }

    private bool ShouldEnterOcclusionByOnePixelOverlap(CandidateState state, Vector3 logicalAnchor, Plane logicalPlane)
    {
        if (state == null || state.unitRoot == null || sourceCamera == null)
            return false;

        if (!TryGetCurrentOccluderVisualBounds(out Bounds occluderBounds))
            return false;

        if (!TryGetUnitCharacterOcclusionRendererBounds(state.unitRoot, out Bounds unitBounds))
            return false;

        float padding = Mathf.Max(0f, onePixelEnterProjectionPadding);
        if (!TryBuildViewportRectFromBounds(occluderBounds, padding, out Rect occluderRect))
            return false;

        if (!TryBuildViewportRectFromBounds(unitBounds, padding, out Rect unitRect))
            return false;

        if (!TryGetViewportIntersection(occluderRect, unitRect, out Rect overlapRect))
            return false;

        float minPixels = Mathf.Max(0f, onePixelEnterMinOverlapPixels);
        if (sourceCamera.pixelWidth > 0 && sourceCamera.pixelHeight > 0 && minPixels > 0f)
        {
            float overlapPixelsX = overlapRect.width * sourceCamera.pixelWidth;
            float overlapPixelsY = overlapRect.height * sourceCamera.pixelHeight;
            if (overlapPixelsX < minPixels && overlapPixelsY < minPixels)
                return false;
        }

        tempViewportOverlapSamples.Clear();
        tempViewportOverlapSamples.Add(overlapRect.center);
        if (onePixelEnterSampleOverlapCorners)
        {
            tempViewportOverlapSamples.Add(new Vector2(overlapRect.xMin, overlapRect.yMin));
            tempViewportOverlapSamples.Add(new Vector2(overlapRect.xMin, overlapRect.yMax));
            tempViewportOverlapSamples.Add(new Vector2(overlapRect.xMax, overlapRect.yMin));
            tempViewportOverlapSamples.Add(new Vector2(overlapRect.xMax, overlapRect.yMax));
        }

        for (int i = 0; i < tempViewportOverlapSamples.Count; i++)
        {
            Vector2 vp = tempViewportOverlapSamples[i];
            Ray ray = sourceCamera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
            if (!logicalPlane.Raycast(ray, out float enter))
                continue;

            Vector3 target = ray.GetPoint(enter);

            if (onePixelEnterRequiresBehindVolume && requireUnitBodyInsideCameraFacingBehindVolume)
            {
                bool depthAllowsPixelEnter = onePixelEnterUsesDepthOnlyBehindGate
                    ? IsLogicalAnchorBehindCameraFacingDepthPlane(target)
                    : IsLogicalAnchorInsideCameraFacingBehindVolume(target);

                if (!depthAllowsPixelEnter)
                    continue;
            }

            float distance = Vector3.Distance(sourceCamera.transform.position, target);
            if (distance <= 0.0001f)
                continue;

            if (IsRayBlockedByCurrentOccluder(ray, distance, state.unitRoot))
            {
                if (debugDrawOnePixelEnterGuard && ShouldDrawRuntimeDebug())
                    Debug.DrawLine(sourceCamera.transform.position, target, Color.green, debugDrawDuration);
                return true;
            }

            if (debugDrawOnePixelEnterGuard && ShouldDrawRuntimeDebug())
                Debug.DrawLine(sourceCamera.transform.position, target, Color.gray, debugDrawDuration);
        }

        return false;
    }

    private readonly List<Vector2> tempViewportOverlapSamples = new List<Vector2>(5);

    private bool TryGetViewportIntersection(Rect a, Rect b, out Rect intersection)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMax = Mathf.Min(a.yMax, b.yMax);

        if (xMax < xMin || yMax < yMin)
        {
            intersection = default;
            return false;
        }

        intersection = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }

    private bool TryGetUnitCharacterOcclusionRendererBounds(Transform unitRoot, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        if (unitRoot == null)
            return false;

        Renderer[] renderers = unitRoot.GetComponentsInChildren<Renderer>(includeInactiveUnitRenderers);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            if (excludeProjectionRenderersFromCharacterOcclusionSamples && IsProjectionOrShadowFootprintRenderer(r))
                continue;

            if (IsExcludedUnitRenderer(r))
                continue;

            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return hasBounds;
    }

    private Vector3 BuildUnifiedDepthVisualWidthPoint(Vector3 visualSample, Vector3 logicalAnchor)
    {
        if (sourceCamera == null)
            return logicalAnchor;

        Vector3 viewDir = sourceCamera.transform.forward;
        viewDir.y = 0f;
        if (viewDir.sqrMagnitude < 0.0001f)
            viewDir = footprintDepthAxis.sqrMagnitude > 0.0001f ? footprintDepthAxis : Vector3.forward;
        viewDir.Normalize();

        Vector3 sideAxis = sourceCamera.transform.right;
        sideAxis.y = 0f;
        if (sideAxis.sqrMagnitude < 0.0001f)
            sideAxis = Vector3.Cross(Vector3.up, viewDir);
        if (sideAxis.sqrMagnitude < 0.0001f)
            sideAxis = Vector3.right;
        sideAxis.Normalize();

        float side = Vector3.Dot(visualSample, sideAxis);
        float depth = Vector3.Dot(logicalAnchor, viewDir);
        float height = useVisualSampleHeightForUnifiedDepth ? visualSample.y : logicalAnchor.y;

        // side 来自视觉轮廓，depth 来自唯一 UnitBody 站位。
        // 这避免同一个 Billboard 角色因为武器/身体顶点不同而出现多个前后深度。
        return sideAxis * side + viewDir * depth + Vector3.up * height;
    }



    private bool IsSideOcclusionHeightRelationValid(CandidateState state, Vector3 logicalAnchor)
    {
        if (!useSideOcclusionHeightGate)
            return true;

        if (!TryGetCurrentOccluderSolidBounds(out Bounds occluderBounds))
            return true;

        float unitMinY = logicalAnchor.y;
        float unitMaxY = logicalAnchor.y;

        if (state != null && state.unitRoot != null && useUnitRendererBoundsForSideHeightGate && TryGetUnitCharacterOcclusionRendererBounds(state.unitRoot, out Bounds unitBounds))
        {
            unitMinY = unitBounds.min.y;
            unitMaxY = unitBounds.max.y;
        }
        else if (state != null && state.unitRoot != null && TryGetUnitRendererBounds(state.unitRoot, out Bounds fallbackBounds))
        {
            unitMinY = fallbackBounds.min.y;
            unitMaxY = fallbackBounds.max.y;
        }

        float padding = Mathf.Max(0f, sideOcclusionHeightPadding);
        float minOverlap = Mathf.Max(0f, minSideOcclusionHeightOverlap);

        float aMin = occluderBounds.min.y - padding;
        float aMax = occluderBounds.max.y + padding;
        float bMin = unitMinY - padding;
        float bMax = unitMaxY + padding;

        float overlap = Mathf.Min(aMax, bMax) - Mathf.Max(aMin, bMin);
        bool valid = overlap >= minOverlap;

        if (!valid)
        {
            debugLastRejectReason = $"Height relation rejected: occluderY=[{occluderBounds.min.y:0.###},{occluderBounds.max.y:0.###}], unitY=[{unitMinY:0.###},{unitMaxY:0.###}], overlap={overlap:0.###}.";
        }

        return valid;
    }


    private bool IsCurrentOccluderOverheadOfUnit(CandidateState state, Vector3 logicalAnchor)
    {
        debugOverheadOcclusionHit = false;
        debugOverheadRejectReason = "";
        debugOverheadProjectionMode = "";
        debugOverheadAnchorWorldPosition = logicalAnchor;

        if (!enableOverheadOcclusion)
        {
            debugOverheadRejectReason = "Overhead rejected: legacy enableOverheadOcclusion is off.";
            return false;
        }

        if (state == null || state.unitRoot == null)
        {
            debugOverheadRejectReason = "Overhead rejected: no unit root.";
            return false;
        }

        if (!TryGetCurrentOccluderSolidBounds(out Bounds occluderBounds))
        {
            debugOverheadRejectReason = "Overhead rejected: no occluder bounds.";
            return false;
        }

        float unitTopY = logicalAnchor.y;
        if (useUnitRendererTopForOverhead && TryGetUnitRendererBounds(state.unitRoot, out Bounds unitBounds))
            unitTopY = Mathf.Max(unitTopY, unitBounds.max.y);

        debugOverheadOccluderBottomY = occluderBounds.min.y;
        debugOverheadUnitTopY = unitTopY;

        // 头顶遮挡只处理“物体底面高于角色头顶/核心高度”的情况。
        // 地面上的箱子、墙体这类侧向遮挡不应该被这个规则直接打开。
        if (occluderBounds.min.y < unitTopY + overheadBottomAboveUnitTopMargin)
        {
            debugOverheadRejectReason = $"Overhead rejected: occluder bottom {occluderBounds.min.y:0.###} < unit top {unitTopY:0.###} + margin {overheadBottomAboveUnitTopMargin:0.###}.";
            return false;
        }

        // 新规则：视觉镜头判定。
        // 不是只看世界 XZ 正下方，而是看当前 GameplayCamera 视角下，头顶物体投影是否压住单位整体视觉区域。
        if (useCameraProjectedOverheadOcclusion)
        {
            if (TryIsCameraProjectedOverheadOfUnit(state, logicalAnchor, occluderBounds, out string projectionRejectReason))
            {
                debugOverheadOcclusionHit = true;
                debugOverheadProjectionMode = "Camera Projection";
                debugOverheadRejectReason = "Overhead accepted by camera projection.";
                return true;
            }

            if (!allowWorldXZOverheadFallback)
            {
                debugOverheadRejectReason = projectionRejectReason;
                return false;
            }
        }

        // 旧规则 fallback：真实世界正下方 XZ 重合。
        Bounds horizontal = occluderBounds;
        horizontal.Expand(new Vector3(overheadHorizontalPadding * 2f, 0f, overheadHorizontalPadding * 2f));
        debugOverheadHorizontalBounds = horizontal;

        bool insideXZ = logicalAnchor.x >= horizontal.min.x && logicalAnchor.x <= horizontal.max.x
            && logicalAnchor.z >= horizontal.min.z && logicalAnchor.z <= horizontal.max.z;

        if (!insideXZ)
        {
            string xzReason = $"Overhead rejected: anchor XZ outside overhead bounds. anchor=({logicalAnchor.x:0.###},{logicalAnchor.z:0.###}) boundsX=[{horizontal.min.x:0.###},{horizontal.max.x:0.###}] boundsZ=[{horizontal.min.z:0.###},{horizontal.max.z:0.###}].";
            debugOverheadRejectReason = useCameraProjectedOverheadOcclusion
                ? $"Camera projection rejected and XZ fallback rejected. {xzReason}"
                : xzReason;
            if (ShouldDrawRuntimeDebug() && debugDrawOverheadOcclusion)
                DrawOverheadBoundsDebug(horizontal, logicalAnchor, false);
            return false;
        }

        debugOverheadOcclusionHit = true;
        debugOverheadProjectionMode = "World XZ Fallback";
        debugOverheadRejectReason = "Overhead accepted by XZ fallback.";

        if (ShouldDrawRuntimeDebug() && debugDrawOverheadOcclusion)
            DrawOverheadBoundsDebug(horizontal, logicalAnchor, true);

        return true;
    }

    private bool TryIsCameraProjectedOverheadOfUnit(CandidateState state, Vector3 logicalAnchor, Bounds occluderBounds, out string rejectReason)
    {
        rejectReason = "";

        if (sourceCamera == null)
        {
            rejectReason = "Overhead camera projection rejected: no source camera.";
            return false;
        }

        if (!TryBuildViewportRectFromBounds(occluderBounds, overheadCameraProjectionPadding, out Rect occluderRect))
        {
            rejectReason = "Overhead camera projection rejected: cannot build occluder viewport rect.";
            return false;
        }

        if (!TryBuildUnitOverheadViewportRect(state, logicalAnchor, overheadCameraProjectionPadding, out Rect unitRect))
        {
            rejectReason = "Overhead camera projection rejected: cannot build unit viewport rect.";
            return false;
        }

        debugOverheadOccluderViewportRect = RectToVector4(occluderRect);
        debugOverheadUnitViewportRect = RectToVector4(unitRect);

        bool overlap = RectsOverlap(occluderRect, unitRect);
        if (ShouldDrawRuntimeDebug() && debugDrawOverheadOcclusion)
            DrawOverheadViewportRectsDebug(occluderRect, unitRect, occluderBounds, overlap);

        if (!overlap)
        {
            rejectReason = $"Overhead camera projection rejected: no viewport overlap. occluder={RectToString(occluderRect)} unit={RectToString(unitRect)}.";
            return false;
        }

        return true;
    }

    private void ScanCandidatesByCameraProjectedOverhead(Bounds solidBounds)
    {
        if (sourceCamera == null)
            return;

        if (Application.isPlaying && cameraProjectedOverheadGlobalScanInterval > 0f)
        {
            if (Time.time < nextCameraProjectedOverheadGlobalScanTime)
                return;
            nextCameraProjectedOverheadGlobalScanTime = Time.time + cameraProjectedOverheadGlobalScanInterval;
        }

        if (!TryBuildViewportRectFromBounds(solidBounds, overheadCameraProjectionPadding, out Rect occluderRect))
            return;

#if UNITY_2023_1_OR_NEWER
        Collider[] all = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        Collider[] all = Object.FindObjectsOfType<Collider>();
#endif
        for (int i = 0; i < all.Length; i++)
        {
            Collider c = all[i];
            if (c == null || !IsValidTarget(c))
                continue;

            Bounds b = c.bounds;
            // 只扫描在当前头顶物体下方可能被压住的单位。这里不要求 XZ 正下方，只要求高度层次合理。
            if (solidBounds.min.y < b.max.y + overheadBottomAboveUnitTopMargin)
                continue;

            if (!TryBuildViewportRectFromBounds(b, overheadCameraProjectionPadding, out Rect candidateRect))
                continue;

            if (RectsOverlap(occluderRect, candidateRect))
                AddOrRefreshCandidate(c);
        }
    }

    private bool TryBuildUnitOverheadViewportRect(CandidateState state, Vector3 logicalAnchor, float padding, out Rect rect)
    {
        rect = default;

        if (state == null || state.unitRoot == null)
            return false;

        tempOverheadSamples.Clear();
        CollectUnitSamples(state.unitRoot, tempOverheadSamples, out _);

        if (tempOverheadSamples.Count > 0 && TryBuildViewportRectFromPoints(tempOverheadSamples, padding, out rect))
            return true;

        if (TryGetUnitRendererBounds(state.unitRoot, out Bounds unitBounds) && TryBuildViewportRectFromBounds(unitBounds, padding, out rect))
            return true;

        return TryBuildViewportRectFromPoints(logicalAnchor, padding, out rect);
    }

    private bool TryBuildViewportRectFromBounds(Bounds b, float padding, out Rect rect)
    {
        List<Vector3> points = tempViewportPointBuffer;
        points.Clear();

        Vector3 c = b.center;
        Vector3 e = b.extents;
        for (int xi = -1; xi <= 1; xi += 2)
        for (int yi = -1; yi <= 1; yi += 2)
        for (int zi = -1; zi <= 1; zi += 2)
            points.Add(c + new Vector3(e.x * xi, e.y * yi, e.z * zi));

        return TryBuildViewportRectFromPoints(points, padding, out rect);
    }

    private bool TryBuildViewportRectFromPoints(Vector3 point, float padding, out Rect rect)
    {
        tempViewportPointBuffer.Clear();
        tempViewportPointBuffer.Add(point);
        return TryBuildViewportRectFromPoints(tempViewportPointBuffer, padding, out rect);
    }

    private readonly List<Vector3> tempViewportPointBuffer = new List<Vector3>(16);

    private bool TryBuildViewportRectFromPoints(List<Vector3> points, float padding, out Rect rect)
    {
        rect = default;
        if (sourceCamera == null || points == null || points.Count == 0)
            return false;

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;
        bool has = false;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 vp = sourceCamera.WorldToViewportPoint(points[i]);
            if (vp.z <= 0f)
                continue;

            minX = Mathf.Min(minX, vp.x);
            minY = Mathf.Min(minY, vp.y);
            maxX = Mathf.Max(maxX, vp.x);
            maxY = Mathf.Max(maxY, vp.y);
            has = true;
        }

        if (!has)
            return false;

        minX -= padding;
        minY -= padding;
        maxX += padding;
        maxY += padding;

        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private bool RectsOverlap(Rect a, Rect b)
    {
        return a.xMin <= b.xMax && a.xMax >= b.xMin && a.yMin <= b.yMax && a.yMax >= b.yMin;
    }

    private Vector4 RectToVector4(Rect r)
    {
        return new Vector4(r.xMin, r.yMin, r.xMax, r.yMax);
    }

    private string RectToString(Rect r)
    {
        return $"[{r.xMin:0.###},{r.yMin:0.###}]~[{r.xMax:0.###},{r.yMax:0.###}]";
    }

    private void DrawOverheadViewportRectsDebug(Rect occluderRect, Rect unitRect, Bounds occluderBounds, bool accepted)
    {
        if (sourceCamera == null)
            return;

        float distance = Mathf.Max(0.5f, Vector3.Distance(sourceCamera.transform.position, occluderBounds.center));
        DrawViewportRect(occluderRect, distance, accepted ? Color.red : Color.cyan);
        DrawViewportRect(unitRect, distance * 0.98f, Color.magenta);
    }

    private void DrawViewportRect(Rect r, float distance, Color color)
    {
        Vector3 p0 = sourceCamera.ViewportToWorldPoint(new Vector3(r.xMin, r.yMin, distance));
        Vector3 p1 = sourceCamera.ViewportToWorldPoint(new Vector3(r.xMax, r.yMin, distance));
        Vector3 p2 = sourceCamera.ViewportToWorldPoint(new Vector3(r.xMax, r.yMax, distance));
        Vector3 p3 = sourceCamera.ViewportToWorldPoint(new Vector3(r.xMin, r.yMax, distance));
        Debug.DrawLine(p0, p1, color, debugDrawDuration);
        Debug.DrawLine(p1, p2, color, debugDrawDuration);
        Debug.DrawLine(p2, p3, color, debugDrawDuration);
        Debug.DrawLine(p3, p0, color, debugDrawDuration);
    }

    private bool TryGetCurrentOccluderSolidBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        if (currentOccluderColliders.Count == 0 && currentOccluderRenderers.Count == 0)
            RebuildCurrentOccluderColliders();

        foreach (Collider c in currentOccluderColliders)
        {
            if (c == null || !c.enabled || c.isTrigger)
                continue;

            if (!hasBounds)
            {
                bounds = c.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(c.bounds);
            }
        }

        if (hasBounds)
            return true;

        for (int i = 0; i < currentOccluderRenderers.Count; i++)
        {
            Renderer r = currentOccluderRenderers[i];
            if (r == null || !r.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return hasBounds;
    }

    private bool TryGetUnitRendererBounds(Transform unitRoot, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        if (unitRoot == null)
            return false;

        Renderer[] renderers = unitRoot.GetComponentsInChildren<Renderer>(includeInactiveUnitRenderers);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            if (IsExcludedUnitRenderer(r))
                continue;

            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return hasBounds;
    }

    private void DrawOverheadBoundsDebug(Bounds b, Vector3 anchor, bool accepted)
    {
        Color c = accepted ? Color.red : Color.cyan;
        float y = b.min.y;
        Vector3 p0 = new Vector3(b.min.x, y, b.min.z);
        Vector3 p1 = new Vector3(b.max.x, y, b.min.z);
        Vector3 p2 = new Vector3(b.max.x, y, b.max.z);
        Vector3 p3 = new Vector3(b.min.x, y, b.max.z);
        Debug.DrawLine(p0, p1, c, debugDrawDuration);
        Debug.DrawLine(p1, p2, c, debugDrawDuration);
        Debug.DrawLine(p2, p3, c, debugDrawDuration);
        Debug.DrawLine(p3, p0, c, debugDrawDuration);
        Debug.DrawLine(anchor, new Vector3(anchor.x, y, anchor.z), c, debugDrawDuration);
    }

    private bool IsLogicalAnchorBehindCameraFacingDepthPlane(Vector3 logicalAnchor)
    {
        if (sourceCamera == null)
            return false;

        if (currentOccluderRenderers.Count == 0)
            RebuildCurrentOccluderColliders();

        if (currentOccluderRenderers.Count == 0)
            return false;

        Vector3 viewDir = sourceCamera.transform.forward;
        viewDir.y = 0f;
        if (viewDir.sqrMagnitude < 0.0001f)
            viewDir = footprintDepthAxis.sqrMagnitude > 0.0001f ? footprintDepthAxis : Vector3.forward;
        viewDir.Normalize();

        Vector3 sideAxis = sourceCamera.transform.right;
        sideAxis.y = 0f;
        if (sideAxis.sqrMagnitude < 0.0001f)
            sideAxis = Vector3.Cross(Vector3.up, viewDir);
        if (sideAxis.sqrMagnitude < 0.0001f)
            sideAxis = Vector3.right;
        sideAxis.Normalize();

        Bounds totalBounds = default;
        bool hasBounds = false;

        if (currentOccluderColliders.Count == 0)
            RebuildCurrentOccluderColliders();

        foreach (Collider c in currentOccluderColliders)
        {
            if (c == null || !c.enabled || c.isTrigger)
                continue;

            if (!hasBounds)
            {
                totalBounds = c.bounds;
                hasBounds = true;
            }
            else
            {
                totalBounds.Encapsulate(c.bounds);
            }
        }

        if (!hasBounds)
        {
            for (int i = 0; i < currentOccluderRenderers.Count; i++)
            {
                Renderer r = currentOccluderRenderers[i];
                if (r == null || !r.enabled)
                    continue;

                if (!hasBounds)
                {
                    totalBounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    totalBounds.Encapsulate(r.bounds);
                }
            }
        }

        if (!hasBounds)
            return false;

        if (useBehindVolumeHeightCheck)
        {
            float minY = totalBounds.min.y - behindVolumeHeightPadding;
            float maxY = totalBounds.max.y + behindVolumeHeightPadding;
            if (logicalAnchor.y < minY || logicalAnchor.y > maxY)
                return false;
        }

        Vector3 origin = totalBounds.center;
        List<Vector2> pts = new List<Vector2>(Mathf.Max(32, currentOccluderColliders.Count * 16 + currentOccluderRenderers.Count * 8));
        List<BehindVolumeProjectedPoint> supportPoints = new List<BehindVolumeProjectedPoint>(pts.Capacity);

        foreach (Collider c in currentOccluderColliders)
        {
            if (c == null || !c.enabled || c.isTrigger)
                continue;

            AddColliderProjectedSupportPoints(c, origin, sideAxis, viewDir, pts, supportPoints);
        }

        if (supportPoints.Count < 2)
        {
            for (int i = 0; i < currentOccluderRenderers.Count; i++)
            {
                Renderer r = currentOccluderRenderers[i];
                if (r == null || !r.enabled)
                    continue;

                AddRendererProjectedSupportCorners(r, origin, sideAxis, viewDir, pts, supportPoints);
            }
        }

        if (pts.Count < 3 || supportPoints.Count < 2)
            return false;

        if (!TryGetCameraSideSupportEdge(supportPoints, out BehindVolumeProjectedPoint leftSupport, out BehindVolumeProjectedPoint rightSupport))
            return false;

        Vector3 delta = logicalAnchor - origin;
        float anchorSide = Vector3.Dot(delta, sideAxis);
        float anchorDepth = Vector3.Dot(delta, viewDir);

        // V52：这里刻意不做 side 范围否决。
        // 一像素进入阶段已经有 Main Camera 画面重叠作为横向证据；
        // depth gate 只负责回答“这个像素采样点是否在当前物体前缘之后”。
        float minSide = Mathf.Min(leftSupport.projected.x, rightSupport.projected.x);
        float maxSide = Mathf.Max(leftSupport.projected.x, rightSupport.projected.x);
        float clampedSide = Mathf.Clamp(anchorSide, minSide, maxSide);
        float supportLineDepth = GetDepthOnSupportEdge(leftSupport.projected, rightSupport.projected, clampedSide);
        float gateDepth = supportLineDepth + behindVolumeFrontMargin;

        return anchorDepth > gateDepth;
    }

    private bool IsLogicalAnchorInsideCameraFacingBehindVolume(Vector3 logicalAnchor)
    {
        debugLogicalAnchorInsideBehindVolume = false;
        debugBehindVolumeRejectReason = "";

        if (sourceCamera == null)
        {
            debugBehindVolumeRejectReason = "BehindVolume rejected: no source camera.";
            return false;
        }

        if (currentOccluderRenderers.Count == 0)
            RebuildCurrentOccluderColliders();

        if (currentOccluderRenderers.Count == 0)
        {
            debugBehindVolumeRejectReason = "BehindVolume rejected: no occluder renderers.";
            return false;
        }

        Vector3 viewDir = sourceCamera.transform.forward;
        viewDir.y = 0f;
        if (viewDir.sqrMagnitude < 0.0001f)
            viewDir = footprintDepthAxis.sqrMagnitude > 0.0001f ? footprintDepthAxis : Vector3.forward;
        viewDir.Normalize();

        Vector3 sideAxis = sourceCamera.transform.right;
        sideAxis.y = 0f;
        if (sideAxis.sqrMagnitude < 0.0001f)
            sideAxis = Vector3.Cross(Vector3.up, viewDir);
        if (sideAxis.sqrMagnitude < 0.0001f)
            sideAxis = Vector3.right;
        sideAxis.Normalize();

        // Behind Volume 的几何基准优先使用当前实例 CollisionRoot 下的 Collider。
        // 这里不要再依赖 VisualRoot 的 Renderer.bounds：
        // 集装箱这类旋转长物体，用世界 AABB / 可见零件取凸点很容易把 Z/depth 取歪。
        // MeshCollider / BoxCollider 已经是当前物体的真实遮挡几何，更适合作为“左右凸点连线”的来源。
        Bounds totalBounds = default;
        bool hasBounds = false;

        if (currentOccluderColliders.Count == 0)
            RebuildCurrentOccluderColliders();

        foreach (Collider c in currentOccluderColliders)
        {
            if (c == null || !c.enabled || c.isTrigger)
                continue;

            if (!hasBounds)
            {
                totalBounds = c.bounds;
                hasBounds = true;
            }
            else
            {
                totalBounds.Encapsulate(c.bounds);
            }
        }

        if (!hasBounds)
        {
            for (int i = 0; i < currentOccluderRenderers.Count; i++)
            {
                Renderer r = currentOccluderRenderers[i];
                if (r == null || !r.enabled)
                    continue;

                if (!hasBounds)
                {
                    totalBounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    totalBounds.Encapsulate(r.bounds);
                }
            }
        }

        if (!hasBounds)
        {
            debugBehindVolumeRejectReason = "BehindVolume rejected: no valid occluder collider/renderer bounds.";
            return false;
        }

        Vector3 origin = totalBounds.center;
        debugBehindVolumeMinY = totalBounds.min.y - behindVolumeHeightPadding;
        debugBehindVolumeMaxY = totalBounds.max.y + behindVolumeHeightPadding;

        if (useBehindVolumeHeightCheck)
        {
            if (logicalAnchor.y < debugBehindVolumeMinY || logicalAnchor.y > debugBehindVolumeMaxY)
            {
                debugBehindVolumeRejectReason = $"BehindVolume rejected: anchor height {logicalAnchor.y:0.###} outside [{debugBehindVolumeMinY:0.###}, {debugBehindVolumeMaxY:0.###}].";
                return false;
            }
        }

        List<Vector2> pts = new List<Vector2>(Mathf.Max(32, currentOccluderColliders.Count * 16 + currentOccluderRenderers.Count * 8));
        List<BehindVolumeProjectedPoint> supportPoints = new List<BehindVolumeProjectedPoint>(pts.Capacity);

        // 优先从当前物体自己的 Collider 取支撑点。MeshCollider / BoxCollider 的几何比 Renderer.bounds 更贴近实际遮挡体。
        foreach (Collider c in currentOccluderColliders)
        {
            if (c == null || !c.enabled || c.isTrigger)
                continue;

            AddColliderProjectedSupportPoints(c, origin, sideAxis, viewDir, pts, supportPoints);
        }

        // 如果没有 Collider 支撑点，再退回 VisualRoot Renderer Mesh 顶点 / localBounds。
        if (supportPoints.Count < 2)
        {
            for (int i = 0; i < currentOccluderRenderers.Count; i++)
            {
                Renderer r = currentOccluderRenderers[i];
                if (r == null || !r.enabled)
                    continue;

                AddRendererProjectedSupportCorners(r, origin, sideAxis, viewDir, pts, supportPoints);
            }
        }

        if (pts.Count < 3 || supportPoints.Count < 2)
        {
            debugBehindVolumeRejectReason = "BehindVolume rejected: not enough projected points.";
            return false;
        }

        List<Vector2> hull = BuildConvexHull2D(pts);
        if (hull.Count < 3)
        {
            debugBehindVolumeRejectReason = "BehindVolume rejected: invalid hull.";
            return false;
        }

        Vector3 delta = logicalAnchor - origin;
        float anchorSide = Vector3.Dot(delta, sideAxis);
        float anchorDepth = Vector3.Dot(delta, viewDir);
        debugBehindVolumeAnchorSide = anchorSide;
        debugBehindVolumeAnchorDepth = anchorDepth;

        // 关键修正：后方体积入口线不再取“最近前缘 / local front depth”。
        // 它使用当前镜头 side 轴上的左右最外侧凸点，并在这两个支撑点之间连线。
        // 这条线对应用户蓝线：相机视角下物体投影轮廓的左右外凸点连线；
        // Behind Volume 从这条支撑线沿 viewDir 往镜头里侧延伸。
        if (!TryGetCameraSideSupportEdge(supportPoints, out BehindVolumeProjectedPoint leftSupport, out BehindVolumeProjectedPoint rightSupport))
        {
            debugBehindVolumeRejectReason = "BehindVolume rejected: cannot build support edge.";
            return false;
        }

        debugBehindVolumeLeftSupportWorldPosition = leftSupport.world;
        debugBehindVolumeRightSupportWorldPosition = rightSupport.world;

        float minSide = Mathf.Min(leftSupport.projected.x, rightSupport.projected.x);
        float maxSide = Mathf.Max(leftSupport.projected.x, rightSupport.projected.x);
        debugBehindVolumeMinSide = minSide;
        debugBehindVolumeMaxSide = maxSide;

        if (anchorSide < minSide - behindVolumeSideMargin || anchorSide > maxSide + behindVolumeSideMargin)
        {
            debugBehindVolumeRejectReason = $"BehindVolume rejected: anchor side {anchorSide:0.###} outside support edge [{minSide:0.###}, {maxSide:0.###}].";
            if (ShouldDrawRuntimeDebug() && debugDrawBehindVolume)
                DrawBehindVolumeSupportEdgeDebug(origin, sideAxis, viewDir, leftSupport, rightSupport, anchorSide, 0f, false);
            return false;
        }

        float clampedSide = Mathf.Clamp(anchorSide, minSide, maxSide);
        float supportLineDepth = GetDepthOnSupportEdge(leftSupport.projected, rightSupport.projected, clampedSide);

        debugBehindVolumeFrontDepthAtAnchor = supportLineDepth;
        debugBehindVolumeGateDepth = supportLineDepth + behindVolumeFrontMargin;

        bool inside = anchorDepth > debugBehindVolumeGateDepth;
        debugLogicalAnchorInsideBehindVolume = inside;

        if (ShouldDrawRuntimeDebug() && debugDrawBehindVolume)
            DrawBehindVolumeSupportEdgeDebug(origin, sideAxis, viewDir, leftSupport, rightSupport, anchorSide, debugBehindVolumeGateDepth, inside);

        if (!inside)
        {
            debugBehindVolumeRejectReason = $"BehindVolume rejected: anchor depth {anchorDepth:0.###} <= support gate {debugBehindVolumeGateDepth:0.###}.";
            return false;
        }

        return true;
    }


    private struct BehindVolumeProjectedPoint
    {
        public Vector3 world;
        public Vector2 projected;

        public BehindVolumeProjectedPoint(Vector3 world, Vector2 projected)
        {
            this.world = world;
            this.projected = projected;
        }
    }

    private bool TryGetCameraSideSupportEdge(List<BehindVolumeProjectedPoint> points, out BehindVolumeProjectedPoint leftSupport, out BehindVolumeProjectedPoint rightSupport)
    {
        leftSupport = default;
        rightSupport = default;

        if (points == null || points.Count < 2)
            return false;

        const float eps = 0.0001f;
        float minSide = float.PositiveInfinity;
        float maxSide = float.NegativeInfinity;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 p = points[i].projected;
            if (p.x < minSide)
                minSide = p.x;
            if (p.x > maxSide)
                maxSide = p.x;
        }

        if (float.IsNaN(minSide) || float.IsInfinity(minSide) || float.IsNaN(maxSide) || float.IsInfinity(maxSide) || Mathf.Abs(maxSide - minSide) < eps)
            return false;

        bool hasLeft = false;
        bool hasRight = false;
        float leftDepth = float.PositiveInfinity;
        float rightDepth = float.PositiveInfinity;

        // 关键：左右支撑点必须保留“自己的完整世界坐标”。
        // 旧版只保存 side/depth，再用统一坐标重建，容易把 Z/前后位置画错。
        // 这里直接记录产生 minSide/maxSide 的真实点，入口线就是这两个真实点的连线。
        for (int i = 0; i < points.Count; i++)
        {
            BehindVolumeProjectedPoint candidate = points[i];
            Vector2 p = candidate.projected;

            if (Mathf.Abs(p.x - minSide) <= eps)
            {
                // 同一侧如果是一条边，取更靠近摄像机的一端作为当前可见支撑点。
                if (!hasLeft || p.y < leftDepth)
                {
                    leftSupport = candidate;
                    leftDepth = p.y;
                    hasLeft = true;
                }
            }

            if (Mathf.Abs(p.x - maxSide) <= eps)
            {
                if (!hasRight || p.y < rightDepth)
                {
                    rightSupport = candidate;
                    rightDepth = p.y;
                    hasRight = true;
                }
            }
        }

        return hasLeft && hasRight;
    }

    private float GetDepthOnSupportEdge(Vector2 leftSupport, Vector2 rightSupport, float side)
    {
        const float eps = 0.0001f;
        if (Mathf.Abs(rightSupport.x - leftSupport.x) < eps)
            return Mathf.Min(leftSupport.y, rightSupport.y);

        float t = Mathf.InverseLerp(leftSupport.x, rightSupport.x, side);
        return Mathf.Lerp(leftSupport.y, rightSupport.y, t);
    }

    private void DrawBehindVolumeSupportEdgeDebug(Vector3 origin, Vector3 sideAxis, Vector3 depthAxis, BehindVolumeProjectedPoint leftSupport, BehindVolumeProjectedPoint rightSupport, float anchorSide, float gateDepth, bool inside)
    {
        float y = debugLogicalAnchorWorldPosition != Vector3.zero ? debugLogicalAnchorWorldPosition.y : origin.y;

        // 入口线直接使用左右凸点的真实世界 XZ，不再用 side + 统一 depth 重建。
        // 只把 Y 调到逻辑锚点高度，便于在 Scene 中观察横向/前后位置。
        Vector3 left = leftSupport.world;
        Vector3 right = rightSupport.world;
        left.y = y;
        right.y = y;

        // 支撑凸点连线：这条线应该对齐用户所说的“蓝线”。
        Color supportColor = inside ? Color.red : Color.green;
        Debug.DrawLine(left, right, supportColor, debugDrawDuration);

        // 简单画出向镜头里侧延伸的方向，帮助判断体积是否往正确方向扫。
        float drawDepth = Mathf.Max(1.5f, totalDebugBehindVolumeDepthHint());
        Vector3 leftFar = left + depthAxis * drawDepth;
        Vector3 rightFar = right + depthAxis * drawDepth;
        Debug.DrawLine(left, leftFar, supportColor, debugDrawDuration);
        Debug.DrawLine(right, rightFar, supportColor, debugDrawDuration);
        Debug.DrawLine(leftFar, rightFar, supportColor, debugDrawDuration);

        if (gateDepth != 0f)
        {
            Vector3 anchorLine = origin + sideAxis * anchorSide + depthAxis * gateDepth;
            anchorLine.y = y;
            Debug.DrawLine(anchorLine - sideAxis * 0.15f, anchorLine + sideAxis * 0.15f, Color.magenta, debugDrawDuration);
        }
    }

    private float totalDebugBehindVolumeDepthHint()
    {
        Bounds b;
        if (TryGetCurrentOccluderBounds(out b))
            return Mathf.Max(1.5f, b.size.x + b.size.z);
        return 3.0f;
    }

    private bool TryGetCurrentOccluderBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        for (int i = 0; i < currentOccluderRenderers.Count; i++)
        {
            Renderer r = currentOccluderRenderers[i];
            if (r == null || !r.enabled)
                continue;
            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }
        return hasBounds;
    }


    private void AddColliderProjectedSupportPoints(Collider c, Vector3 origin, Vector3 sideAxis, Vector3 depthAxis, List<Vector2> pts, List<BehindVolumeProjectedPoint> supportPoints)
    {
        if (c == null)
            return;

        MeshCollider meshCollider = c as MeshCollider;
        if (meshCollider != null && meshCollider.sharedMesh != null)
        {
            Mesh mesh = meshCollider.sharedMesh;
            Matrix4x4 localToWorld = meshCollider.transform.localToWorldMatrix;

            if (mesh.isReadable)
            {
                AddMeshVertexProjectedPoints(mesh, localToWorld, origin, sideAxis, depthAxis, pts, supportPoints);
                return;
            }

            // Mesh 不可读时，退回 sharedMesh local bounds。依然比 Collider.bounds 世界 AABB 更能保留旋转关系。
            AddLocalBoundsProjectedCorners(mesh.bounds, localToWorld, origin, sideAxis, depthAxis, pts, supportPoints);
            return;
        }

        BoxCollider box = c as BoxCollider;
        if (box != null)
        {
            Bounds localBounds = new Bounds(box.center, box.size);
            AddLocalBoundsProjectedCorners(localBounds, box.transform.localToWorldMatrix, origin, sideAxis, depthAxis, pts, supportPoints);
            return;
        }

        // 其他 Collider 暂时用自身 bounds 兜底。Sphere/Capsule 对本项目集装箱不是主路径。
        AddBoundsProjectedCorners(c.bounds, origin, sideAxis, depthAxis, pts, supportPoints);
    }

    private bool AddRendererProjectedSupportCorners(Renderer r, Vector3 origin, Vector3 sideAxis, Vector3 depthAxis, List<Vector2> pts, List<BehindVolumeProjectedPoint> supportPoints)
    {
        if (r == null)
            return false;

        // 关键修正：左右凸点必须来自 Mesh 顶点自己的 world position。
        // 不再只用 Renderer.bounds，也不只用 localBounds 8 角点。
        // 旋转、缩放、父子层级都交给 localToWorldMatrix 处理；
        // 找到 side 轴最左/最右顶点时，保留该顶点完整 XYZ，因此 Z/depth 不会被重建丢失。
        Mesh mesh = null;
        Matrix4x4 localToWorld = r.transform.localToWorldMatrix;

        SkinnedMeshRenderer skinned = r as SkinnedMeshRenderer;
        if (skinned != null && skinned.sharedMesh != null)
        {
            mesh = skinned.sharedMesh;
            localToWorld = skinned.transform.localToWorldMatrix;
        }
        else
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                mesh = mf.sharedMesh;
                localToWorld = mf.transform.localToWorldMatrix;
            }
        }

        if (mesh != null)
        {
            if (mesh.isReadable)
            {
                AddMeshVertexProjectedPoints(mesh, localToWorld, origin, sideAxis, depthAxis, pts, supportPoints);
                return true;
            }

            // Mesh 不可读时，只能退回 localBounds 角点。这里仍比 Renderer.bounds 世界 AABB 干净，
            // 因为 localBounds 会经过 localToWorld，至少保留模型旋转后的前后关系。
            AddLocalBoundsProjectedCorners(mesh.bounds, localToWorld, origin, sideAxis, depthAxis, pts, supportPoints);
            return true;
        }

        // 没有 Mesh 时才退回 Renderer.bounds。这个 fallback 只为兼容特殊 Renderer。
        AddBoundsProjectedCorners(r.bounds, origin, sideAxis, depthAxis, pts, supportPoints);
        return false;
    }

    private void AddMeshVertexProjectedPoints(Mesh mesh, Matrix4x4 localToWorld, Vector3 origin, Vector3 sideAxis, Vector3 depthAxis, List<Vector2> pts, List<BehindVolumeProjectedPoint> supportPoints)
    {
        if (mesh == null)
            return;

        Vector3[] vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0)
            return;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = localToWorld.MultiplyPoint3x4(vertices[i]);
            Vector3 d = p - origin;
            Vector2 projected = new Vector2(Vector3.Dot(d, sideAxis), Vector3.Dot(d, depthAxis));
            pts.Add(projected);
            supportPoints.Add(new BehindVolumeProjectedPoint(p, projected));
        }
    }

    private void AddLocalBoundsProjectedCorners(Bounds localBounds, Matrix4x4 localToWorld, Vector3 origin, Vector3 sideAxis, Vector3 depthAxis, List<Vector2> pts, List<BehindVolumeProjectedPoint> supportPoints)
    {
        Vector3 c = localBounds.center;
        Vector3 e = localBounds.extents;
        for (int xi = -1; xi <= 1; xi += 2)
        for (int yi = -1; yi <= 1; yi += 2)
        for (int zi = -1; zi <= 1; zi += 2)
        {
            Vector3 local = c + new Vector3(e.x * xi, e.y * yi, e.z * zi);
            Vector3 p = localToWorld.MultiplyPoint3x4(local);
            Vector3 d = p - origin;
            Vector2 projected = new Vector2(Vector3.Dot(d, sideAxis), Vector3.Dot(d, depthAxis));
            pts.Add(projected);
            supportPoints.Add(new BehindVolumeProjectedPoint(p, projected));
        }
    }

    private void AddBoundsProjectedCorners(Bounds b, Vector3 origin, Vector3 sideAxis, Vector3 depthAxis, List<Vector2> pts, List<BehindVolumeProjectedPoint> supportPoints)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;
        for (int xi = -1; xi <= 1; xi += 2)
        for (int yi = -1; yi <= 1; yi += 2)
        for (int zi = -1; zi <= 1; zi += 2)
        {
            Vector3 p = c + new Vector3(e.x * xi, e.y * yi, e.z * zi);
            Vector3 d = p - origin;
            Vector2 projected = new Vector2(Vector3.Dot(d, sideAxis), Vector3.Dot(d, depthAxis));
            pts.Add(projected);
            supportPoints.Add(new BehindVolumeProjectedPoint(p, projected));
        }
    }

    private List<Vector2> BuildConvexHull2D(List<Vector2> points)
    {
        List<Vector2> sorted = new List<Vector2>(points);
        sorted.Sort((a, b) =>
        {
            int x = a.x.CompareTo(b.x);
            return x != 0 ? x : a.y.CompareTo(b.y);
        });

        List<Vector2> hull = new List<Vector2>();
        for (int i = 0; i < sorted.Count; i++)
        {
            Vector2 p = sorted[i];
            while (hull.Count >= 2 && Cross2D(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        int lowerCount = hull.Count;
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            Vector2 p = sorted[i];
            while (hull.Count > lowerCount && Cross2D(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 1]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        if (hull.Count > 1)
            hull.RemoveAt(hull.Count - 1);

        return hull;
    }

    private float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private bool TryGetFrontDepthAtSide(List<Vector2> hull, float side, out float frontDepth)
    {
        frontDepth = float.PositiveInfinity;
        bool found = false;
        const float eps = 0.0001f;

        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 a = hull[i];
            Vector2 b = hull[(i + 1) % hull.Count];
            float minX = Mathf.Min(a.x, b.x) - eps;
            float maxX = Mathf.Max(a.x, b.x) + eps;
            if (side < minX || side > maxX)
                continue;

            float depth;
            if (Mathf.Abs(a.x - b.x) < eps)
            {
                depth = Mathf.Min(a.y, b.y);
            }
            else
            {
                float t = Mathf.InverseLerp(a.x, b.x, side);
                depth = Mathf.Lerp(a.y, b.y, t);
            }

            if (depth < frontDepth)
            {
                frontDepth = depth;
                found = true;
            }
        }

        return found;
    }

    private void DrawBehindVolumeDebug(Vector3 origin, Vector3 sideAxis, Vector3 depthAxis, float minSide, float maxSide, float frontDepthAtAnchor, float anchorSide, bool inside)
    {
        float y = debugLogicalAnchorWorldPosition != Vector3.zero ? debugLogicalAnchorWorldPosition.y : origin.y;
        Vector3 left = origin + sideAxis * minSide + depthAxis * frontDepthAtAnchor;
        Vector3 right = origin + sideAxis * maxSide + depthAxis * frontDepthAtAnchor;
        left.y = y;
        right.y = y;
        Color lineColor = inside ? Color.red : Color.green;
        Debug.DrawLine(left, right, lineColor, debugDrawDuration);

        Vector3 anchorLine = origin + sideAxis * anchorSide + depthAxis * debugBehindVolumeGateDepth;
        anchorLine.y = y;
        Debug.DrawLine(anchorLine - sideAxis * 0.15f, anchorLine + sideAxis * 0.15f, Color.magenta, debugDrawDuration);
    }

    private bool TryGetLogicalAnchor(CandidateState state, out Vector3 anchor, out string anchorName, out string anchorSource)
    {
        anchor = Vector3.zero;
        anchorName = "";
        anchorSource = "";

        if (state == null || state.unitRoot == null)
            return false;

        if (preferLogicalBodyCollider)
        {
            Collider[] cols = state.unitRoot.GetComponentsInChildren<Collider>(true);
            Collider best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null)
                    continue;

                int score = GetLogicalAnchorScore(c.name);
                if (score > bestScore)
                {
                    best = c;
                    bestScore = score;
                }
            }

            if (best != null && bestScore > 0)
            {
                anchor = best.bounds.center;
                anchorName = best.name;
                anchorSource = "Preferred logical collider";
                return true;
            }
        }

        if (fallbackToUnitRootPosition)
        {
            anchor = state.unitRoot.position;
            anchorName = state.unitRoot.name;
            anchorSource = "UnitRoot.position fallback";
            return true;
        }

        if (state.sourceCollider != null)
        {
            anchor = state.sourceCollider.bounds.center;
            anchorName = state.sourceCollider.name;
            anchorSource = "Source collider fallback";
            return true;
        }

        return false;
    }

    private int GetLogicalAnchorScore(string n)
    {
        if (string.IsNullOrEmpty(n))
            return -999;

        if (ContainsAny(n, "CollisionRoot", "Main_Collision_MeshRoot", "MeshRoot", "OcclusionRoot", "FrontOccluder", "BackTrigger", "FrontTrigger", "Outline", "Shadow"))
            return -999;

        if (ContainsAny(n, "UnitBodyCollider")) return 1000;
        if (ContainsAny(n, "UnitBody")) return 950;
        if (ContainsAny(n, "UnitPhysicsProbe")) return 900;
        if (ContainsAny(n, "BodyCollider")) return 850;
        if (ContainsAny(n, "CharacterBodyCollider")) return 840;
        if (ContainsAny(n, "PhysicsProbe")) return 800;
        if (ContainsAny(n, "HeadAnchor")) return 50;

        return 0;
    }

    private bool ContainsAny(string source, params string[] keys)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            if (!string.IsNullOrEmpty(keys[i]) && source.Contains(keys[i]))
                return true;
        }
        return false;
    }

    private bool IsRayBlockedByCurrentOccluder(Ray ray, float sampleDistance, Transform unitRoot)
    {
        float maxDistance = Mathf.Max(0f, sampleDistance - Mathf.Max(0.0001f, depthEpsilon));
        if (maxDistance <= 0f)
            return false;

        // 核心改动：当前遮挡物自己只检测自己。
        // 不再用“世界 Raycast 第一命中是谁”决定成败，避免地面、树、其他箱子抢走结果。
        bool selfHit = TryRaycastCurrentOccluderOnly(ray, maxDistance, out float selfDistance, out string selfName, out string selfPath);
        if (selfHit)
        {
            debugRayHitCount++;
            debugCurrentHitCount++;
            debugLastCurrentHitName = selfName;
            debugLastCurrentHitPath = selfPath;
            debugLastCurrentHitDistance = selfDistance;

            if (ShouldDrawRuntimeDebug() && debugDrawSamples)
                Debug.DrawLine(ray.origin, ray.origin + ray.direction * selfDistance, debugBlockedColor, debugDrawDuration);

            return true;
        }

        // OtherHit 现在只用于排查，不再作为当前遮挡物失败依据。
        // 也就是说：绿箱算绿箱的，红箱算红箱的，地面/树/叉车不会抢当前箱子的答案。
        if (debugGlobalOtherHitOnly)
            CaptureNearestGlobalOtherHitForDebug(ray, maxDistance, unitRoot);
        else
            debugClearCount++;

        return false;
    }

    private bool TryRaycastCurrentOccluderOnly(Ray ray, float maxDistance, out float bestDistance, out string hitName, out string hitPath)
    {
        bestDistance = float.PositiveInfinity;
        hitName = "";
        hitPath = "";
        debugLastCurrentHitType = "";
        bool hitAny = false;

        if (occluderContentRoot == null)
            return false;

        // 1) 最优先：当前 VisualRoot 下真实 Mesh 三角面。
        // 这一步解决 Renderer Bounds 太粗，导致角色在物体前面也被误判的问题。
        if (useSelfVisualMeshTriangleRayDepth)
        {
            if (currentOccluderRenderers.Count == 0)
                RebuildCurrentOccluderColliders();

            for (int i = 0; i < currentOccluderRenderers.Count; i++)
            {
                Renderer r = currentOccluderRenderers[i];
                if (r == null || !r.enabled)
                    continue;

                if (!TryRaycastRendererMeshTriangles(r, ray, maxDistance, out float distance))
                    continue;

                if (distance >= 0f && distance < bestDistance)
                {
                    bestDistance = distance;
                    hitName = r.name;
                    hitPath = GetTransformPath(r.transform);
                    debugLastCurrentHitType = "Visual Mesh Triangle";
                    hitAny = true;

                    if (stopAtFirstSelfMeshHit)
                        return true;
                }
            }
        }

        // 2) 其次：当前实例 CollisionRoot / VisualRoot 下 Collider。适合使用已贴合模型的 MeshCollider / BoxCollider。
        if (useSelfColliderRayDepth)
        {
            if (currentOccluderColliders.Count == 0)
                RebuildCurrentOccluderColliders();

            foreach (Collider c in currentOccluderColliders)
            {
                if (c == null || !c.enabled || c.isTrigger)
                    continue;

                if (!c.Raycast(ray, out RaycastHit hit, maxDistance))
                    continue;

                if (hit.distance >= 0f && hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    hitName = c.name;
                    hitPath = GetTransformPath(c.transform);
                    debugLastCurrentHitType = "Self Collider";
                    hitAny = true;
                }
            }
        }

        // Renderer Bounds Fallback 已退出最终遮挡判定。
        // Bounds 只能用于候选扫描 / Debug 可视化，不能再让 LastShouldOcclude = true。
        // 这样可以避免旋转后的 AABB 空白区域、模型外侧虚胖范围造成前方误遮。

        if (!hitAny)
        {
            bestDistance = 0f;
            return false;
        }

        return true;
    }

    private bool TryRaycastRendererMeshTriangles(Renderer renderer, Ray worldRay, float maxDistance, out float bestDistance)
    {
        bestDistance = float.PositiveInfinity;

        if (renderer == null)
            return false;

        if (!occluderMeshRayCache.TryGetValue(renderer, out MeshRayCache cache))
        {
            // 缓存缺失（例如 useSelfVisualMeshTriangleRayDepth 是在 RebuildCurrentOccluderColliders
            // 之后才被打开的）——退回直接读取一次，并顺手把结果补进缓存，避免后续射线继续miss。
            BuildMeshRayCacheForRenderer(renderer);
            if (!occluderMeshRayCache.TryGetValue(renderer, out cache))
                return false;
        }

        Vector3[] vertices = cache.vertices;
        int[] triangles = cache.triangles;
        Transform meshTransform = cache.meshTransform;

        if (vertices == null || triangles == null || triangles.Length < 3 || meshTransform == null)
            return false;

        // 粗筛：射线连渲染体的世界 Bounds 都不相交，就没必要跑下面最多几万个三角面的逐面测试。
        // 密集像素网格/渲染体像素重叠/普通采样这三套逻辑各自独立发射线，命中同一批遮挡物
        // 渲染体，很多射线其实压根没扎中这个渲染体的包围盒——这一步是纯粹的性能剪枝，
        // 不改变命中判定结果（Bounds 一定完整包住实际网格，剪掉的射线原本也不可能命中三角面）。
        if (!renderer.bounds.IntersectRay(worldRay, out float boundsEntryDistance) || boundsEntryDistance > maxDistance)
            return false;

        int triCount = triangles.Length / 3;
        int maxTris = maxSelfVisualMeshTrianglesPerRenderer <= 0 ? triCount : Mathf.Max(1, maxSelfVisualMeshTrianglesPerRenderer);
        int step = Mathf.Max(1, triCount / maxTris);

        Matrix4x4 localToWorld = meshTransform.localToWorldMatrix;
        bool hitAny = false;

        for (int tri = 0; tri < triCount; tri += step)
        {
            int ti = tri * 3;
            int i0 = triangles[ti];
            int i1 = triangles[ti + 1];
            int i2 = triangles[ti + 2];

            if (i0 < 0 || i0 >= vertices.Length || i1 < 0 || i1 >= vertices.Length || i2 < 0 || i2 >= vertices.Length)
                continue;

            Vector3 v0 = localToWorld.MultiplyPoint3x4(vertices[i0]);
            Vector3 v1 = localToWorld.MultiplyPoint3x4(vertices[i1]);
            Vector3 v2 = localToWorld.MultiplyPoint3x4(vertices[i2]);

            if (!RayIntersectsTriangleDoubleSided(worldRay, v0, v1, v2, out float distance))
                continue;

            if (distance < 0f || distance > maxDistance)
                continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                hitAny = true;

                if (stopAtFirstSelfMeshHit)
                    return true;
            }
        }

        if (!hitAny)
        {
            bestDistance = 0f;
            return false;
        }

        return true;
    }

    private bool RayIntersectsTriangleDoubleSided(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
    {
        distance = 0f;

        const float epsilon = 0.0000001f;
        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;
        Vector3 pvec = Vector3.Cross(ray.direction, edge2);
        float det = Vector3.Dot(edge1, pvec);

        if (det > -epsilon && det < epsilon)
            return false;

        float invDet = 1f / det;
        Vector3 tvec = ray.origin - v0;
        float u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < 0f || u > 1f)
            return false;

        Vector3 qvec = Vector3.Cross(tvec, edge1);
        float v = Vector3.Dot(ray.direction, qvec) * invDet;
        if (v < 0f || u + v > 1f)
            return false;

        distance = Vector3.Dot(edge2, qvec) * invDet;
        return distance >= 0f;
    }

    private void CaptureNearestGlobalOtherHitForDebug(Ray ray, float maxDistance, Transform unitRoot)
    {
        int hitCount = Physics.RaycastNonAlloc(ray, raycastHits, maxDistance, raycastLayers, raycastTriggerInteraction);
        if (hitCount <= 0)
        {
            debugClearCount++;
            return;
        }

        debugRayHitCount += hitCount;

        int nearestIndex = -1;
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = raycastHits[i];
            Collider c = hit.collider;
            if (c == null)
                continue;

            if (unitRoot != null && (c.transform == unitRoot || c.transform.IsChildOf(unitRoot)))
                continue;

            // 当前遮挡物已经由 TryRaycastCurrentOccluderOnly 自己判断过；这里仅记录其他物体。
            if (IsCurrentOccluderHit(c))
                continue;

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                nearestIndex = i;
            }
        }

        if (nearestIndex < 0)
        {
            debugClearCount++;
            return;
        }

        RaycastHit nearest = raycastHits[nearestIndex];
        debugOtherHitCount++;
        if (nearest.collider != null)
        {
            debugLastOtherHitName = nearest.collider.name;
            debugLastOtherHitPath = GetTransformPath(nearest.collider.transform);
            debugLastOtherHitRootName = nearest.collider.transform.root != null ? nearest.collider.transform.root.name : "NULL";
            debugLastOtherHitLayer = LayerMask.LayerToName(nearest.collider.gameObject.layer);
            debugLastOtherHitDistance = nearest.distance;
        }

        if (ShouldWriteRuntimeDebugStrings() && debugLogs)
            Debug.Log($"[OcclusionTrigger][OtherHit-DebugOnly] currentRoot={debugCurrentOccluderRootName} hit={debugLastOtherHitPath} layer={debugLastOtherHitLayer} distance={debugLastOtherHitDistance:0.###}", nearest.collider);

        if (ShouldDrawRuntimeDebug() && debugDrawSamples)
            Debug.DrawLine(ray.origin, nearest.point, debugOtherHitColor, debugDrawDuration);
    }

    private bool IsCurrentOccluderHit(Collider c)
    {
        if (c == null || occluderContentRoot == null)
            return false;

        if (currentOccluderColliders.Contains(c))
            return true;

        Transform t = c.transform;
        return t == occluderContentRoot || t.IsChildOf(occluderContentRoot);
    }

    private void CollectUnitSamples(Transform unitRoot, List<Vector3> samples, out int rendererCount)
    {
        samples.Clear();
        rendererCount = 0;

        if (unitRoot == null)
            return;

        Renderer[] renderers = unitRoot.GetComponentsInChildren<Renderer>(includeInactiveUnitRenderers);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            if (excludeProjectionRenderersFromCharacterOcclusionSamples && IsProjectionOrShadowFootprintRenderer(r))
                continue;

            bool forcedVisualFootprint = IsForcedVisualFootprintRenderer(r);
            if (!forcedVisualFootprint && IsExcludedUnitRenderer(r))
                continue;

            rendererCount++;
            int before = samples.Count;

            // 视觉外轮廓规则：
            // - Mesh 顶点可以提供细节。
            // - Bounds 外轮廓必须稳定加入，避免武器尖端 / 衣服边缘 / 舞台投影因为采样步长而漏掉。
            // - 这些点只贡献屏幕宽度/高度，后续 BuildUnifiedDepthVisualWidthPoint 会统一投回 UnitBody 深度。
            if (alwaysAddRendererBoundsFootprintSamples || forceRendererBoundsAsVisualFootprint || forcedVisualFootprint)
                AddBoundsSamples(r.bounds, samples);

            if (!forcedVisualFootprint && preferUnitMeshVertices)
                TryAddMeshSamples(r, samples, maxUnitMeshSamplesPerRenderer);

            if (samples.Count == before && fallbackToRendererBoundsSamples)
                AddBoundsSamples(r.bounds, samples);
        }
    }


    private bool IsProjectionOrShadowFootprintRenderer(Renderer r)
    {
        if (r == null)
            return false;

        string path = GetTransformPath(r.transform);
        if (!string.IsNullOrEmpty(path))
        {
            string[] keys = { "StageProjection", "Stage Projection", "GroundShadow", "Ground_Shadow", "UnitShadow", "FootShadow", "Projection", "ShadowPlane", "ContactShadow", "投影", "影子", "舞台投影" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (!string.IsNullOrEmpty(key) && path.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }

        Material[] mats = r.sharedMaterials;
        if (mats != null)
        {
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null)
                    continue;

                string matName = mat.name;
                string shaderName = mat.shader != null ? mat.shader.name : string.Empty;
                if ((!string.IsNullOrEmpty(matName) &&
                    (matName.IndexOf("Shadow", System.StringComparison.OrdinalIgnoreCase) >= 0 || matName.IndexOf("Projection", System.StringComparison.OrdinalIgnoreCase) >= 0 || matName.IndexOf("投影", System.StringComparison.OrdinalIgnoreCase) >= 0)) ||
                    (!string.IsNullOrEmpty(shaderName) &&
                    (shaderName.IndexOf("FrontOccluder/Shadows", System.StringComparison.OrdinalIgnoreCase) >= 0 || shaderName.IndexOf("StageShadow", System.StringComparison.OrdinalIgnoreCase) >= 0 || shaderName.IndexOf("ContactShadow", System.StringComparison.OrdinalIgnoreCase) >= 0)))
                    return true;
            }
        }

        return false;
    }

    private bool IsForcedVisualFootprintRenderer(Renderer r)
    {
        if (r == null)
            return false;

        string path = GetTransformPath(r.transform);
        if (forceIncludeVisualFootprintRendererNameKeywords != null)
        {
            for (int i = 0; i < forceIncludeVisualFootprintRendererNameKeywords.Length; i++)
            {
                string key = forceIncludeVisualFootprintRendererNameKeywords[i];
                if (!string.IsNullOrEmpty(key) && path.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }

        if (forceIncludeVisualFootprintShaderKeywords != null)
        {
            Material[] mats = r.sharedMaterials;
            if (mats != null)
            {
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null)
                        continue;

                    string matName = mat.name;
                    string shaderName = mat.shader != null ? mat.shader.name : string.Empty;
                    for (int k = 0; k < forceIncludeVisualFootprintShaderKeywords.Length; k++)
                    {
                        string key = forceIncludeVisualFootprintShaderKeywords[k];
                        if (string.IsNullOrEmpty(key))
                            continue;

                        if ((!string.IsNullOrEmpty(matName) && matName.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrEmpty(shaderName) && shaderName.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    private bool IsExcludedUnitRenderer(Renderer r)
    {
        if (!useNameExclusion || r == null)
            return false;

        // 白名单优先：舞台投影 / 接地影子属于视觉占位外轮廓，不能因为 Shadow / Projection 关键词被误排除。
        if (IsForcedVisualFootprintRenderer(r))
            return false;

        string path = GetTransformPath(r.transform);
        for (int i = 0; i < excludedUnitRendererNameKeywords.Length; i++)
        {
            string key = excludedUnitRendererNameKeywords[i];
            if (!string.IsNullOrEmpty(key) && path.Contains(key))
                return true;
        }

        return false;
    }

    private string GetTransformPath(Transform t)
    {
        if (t == null)
            return "";

        if (Application.isPlaying && transformPathCache.TryGetValue(t, out string cached))
            return cached;

        string s = t.name;
        Transform p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 32)
        {
            s = p.name + "/" + s;
            p = p.parent;
        }

        if (Application.isPlaying)
            transformPathCache[t] = s;

        return s;
    }

    private void TryAddMeshSamples(Renderer r, List<Vector3> samples, int maxSamples)
    {
        if (r == null || maxSamples <= 0)
            return;

        Mesh mesh = null;
        Transform meshTransform = r.transform;

        SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
        if (smr != null)
        {
            mesh = smr.sharedMesh;
            meshTransform = smr.transform;
        }
        else
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf != null)
            {
                mesh = mf.sharedMesh;
                meshTransform = mf.transform;
            }
        }

        if (mesh == null || !mesh.isReadable || mesh.vertexCount <= 0)
            return;

        Vector3[] vertices;
        try
        {
            vertices = mesh.vertices;
        }
        catch
        {
            return;
        }

        int step = Mathf.Max(1, vertices.Length / Mathf.Max(1, maxSamples));
        int added = 0;
        for (int i = 0; i < vertices.Length && added < maxSamples; i += step)
        {
            samples.Add(meshTransform.TransformPoint(vertices[i]));
            added++;
        }
    }

    private void AddBoundsSamples(Bounds b, List<Vector3> samples)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;

        samples.Add(c);
        samples.Add(c + new Vector3(e.x, 0, 0));
        samples.Add(c - new Vector3(e.x, 0, 0));
        samples.Add(c + new Vector3(0, e.y, 0));
        samples.Add(c - new Vector3(0, e.y, 0));
        samples.Add(c + new Vector3(0, 0, e.z));
        samples.Add(c - new Vector3(0, 0, e.z));

        samples.Add(c + new Vector3(e.x, e.y, e.z));
        samples.Add(c + new Vector3(e.x, e.y, -e.z));
        samples.Add(c + new Vector3(e.x, -e.y, e.z));
        samples.Add(c + new Vector3(e.x, -e.y, -e.z));
        samples.Add(c + new Vector3(-e.x, e.y, e.z));
        samples.Add(c + new Vector3(-e.x, e.y, -e.z));
        samples.Add(c + new Vector3(-e.x, -e.y, e.z));
        samples.Add(c + new Vector3(-e.x, -e.y, -e.z));
    }

    private void SetCandidateOccluding(CandidateState state, bool shouldOcclude)
    {
        SetCandidateOccluding(state, shouldOcclude, false);
    }

    private void SetCandidateOccluding(CandidateState state, bool shouldOcclude, bool forceNotifyBecauseReceiversChanged)
    {
        if (state == null)
            return;

        bool stateChanged = state.occluding != shouldOcclude;
        if (!stateChanged && !forceNotifyBecauseReceiversChanged)
            return;

        state.occluding = shouldOcclude;

        if (!notifyOcclusionStateReceivers)
            return;

        NotifyCandidateReceivers(state, shouldOcclude);
    }

    private void NotifyCandidateReceivers(CandidateState state, bool shouldOcclude)
    {
        if (state == null)
            return;

        for (int i = 0; i < state.receivers.Count; i++)
        {
            MonoBehaviour mb = state.receivers[i];
            if (mb == null)
                continue;

            IOcclusionStateReceiver receiver = mb as IOcclusionStateReceiver;
            if (receiver != null)
                receiver.SetOccludedBy(this, shouldOcclude);
        }
    }

    private void NotifyAllReceivers(bool isOccluded)
    {
        foreach (KeyValuePair<Transform, CandidateState> pair in candidateStates)
        {
            CandidateState state = pair.Value;
            if (state == null)
                continue;

            for (int i = 0; i < state.receivers.Count; i++)
            {
                MonoBehaviour mb = state.receivers[i];
                if (mb == null)
                    continue;

                IOcclusionStateReceiver receiver = mb as IOcclusionStateReceiver;
                if (receiver != null)
                    receiver.SetOccludedBy(this, isOccluded);
            }

            state.occluding = isOccluded;
        }
    }

    private void ApplyVisibility()
    {
        bool any = false;
        foreach (KeyValuePair<Transform, CandidateState> pair in candidateStates)
        {
            CandidateState state = pair.Value;
            if (state != null && state.occluding)
            {
                any = true;
                break;
            }
        }

        lastFrontOccluderVisibility = any;
        if (frontOccluderRoot != null && frontOccluderRoot.activeSelf != any)
            frontOccluderRoot.SetActive(any);
    }
}
