using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 第一版：阵营视野汇总管理器。
/// 目标：
/// 1. 收集场景里所有带 UnitDefinitionRuntimeBinder 的单位
/// 2. 汇总“玩家阵营”共享视野
/// 3. 提供统一查询接口，供战争迷雾 / 单位显隐 / 小地图后续复用
///
/// 当前第一版阵营规则：
/// 玩家阵营：Player / Ally / NeutralPassive / Creature
/// 敌对阵营：Enemy / Elite / Boss / NeutralHostile
/// </summary>
[DefaultExecutionOrder(9100)]
public class SkyPrisonVisionManager : MonoBehaviour
{
    public static SkyPrisonVisionManager Instance { get; private set; }

    public enum RuntimeFaction
    {
        Unknown = 0,
        PlayerFaction = 1,
        HostileFaction = 2,
    }

    [Header("Scan")]
    [SerializeField] private bool autoScanOnEnable = true;
    [SerializeField] private bool autoRescanInterval = true;
    [SerializeField] private float rescanInterval = 1.0f;

    [Header("Soft Query")]
    [Tooltip("视觉层边缘柔化宽度。Shader 的 Soft Edge Width 应与这里保持接近。")]
    [SerializeField] private float visualSoftEdgeWidth = 3.5f;

    [Header("Unit Visibility")]
    [Tooltip("开启后，单位显隐会把迷雾渐变边缘也视为可见区；只有进入完全暗淡区才隐藏。")]
    [SerializeField] private bool keepUnitsVisibleInsideSoftEdge = true;
    [Tooltip("单位可见度低于该值时隐藏。0.01 表示几乎完全进入暗雾才隐藏。")]
    [SerializeField, Range(0f, 1f)] private float unitHideVisibilityThreshold = 0.01f;
    [Tooltip("开启后，单位显隐使用与 FogMaskRenderer 接近的判定：半径 / 扇形 / 柔边，不再额外做 Terrain VisionBlocker 射线遮挡。用于避免地面视野已展开，但单位仍被旧射线判定隐藏。")]
    [SerializeField] private bool unitVisibilityUseFogMaskLikeQuery = true;
    [Tooltip("开启后，单位显隐不会只采样 Binder Root，而会采样单位视觉中心 / Root / 脚底附近，任一点进入视野就显示。")]
    [SerializeField] private bool unitVisibilityUseMultiSample = true;
    [Tooltip("单位显隐额外容错距离。用于让单位半个身位进入视野时就恢复显示。")]
    [SerializeField, Min(0f)] private float unitVisibilityExtraPadding = 0.75f;

    [Header("Terrain Vision Blockers")]
    [SerializeField] private bool enableTerrainVisionBlockers = true;
    [Tooltip("只让 VisionBlockerRoot / Vision_Blocker_Box 这套视野遮挡节点挡视线，避免 MeshCollider / BackTrigger / FrontTrigger 越权。")]
    [SerializeField] private bool requireExplicitVisionBlockerNode = true;
    [Tooltip("兼容旧实例：当没有命中 VisionBlockerRoot 时，允许 blockVision=true 的地形装饰物普通碰撞体挡视线。正式结构稳定后建议关闭。")]
    [SerializeField] private bool allowLegacyTerrainColliderVisionBlock = false;
    [SerializeField] private QueryTriggerInteraction terrainVisionBlockerTriggerInteraction = QueryTriggerInteraction.Collide;
    [SerializeField] private float terrainVisionBlockerStartPadding = 0.05f;
    [Tooltip("开启后，在 Scene 里画出视线检测线：绿色=无遮挡，红色=被 VisionBlockerRoot 挡住。仅用于调试。")]
    [SerializeField] private bool debugDrawVisionBlockerRays = false;

    [Header("Perception Runtime Sync")]
    [Tooltip("开启后，VisionManager 会把玩家可见强度写入 UnitPerceptionRuntime。")]
    [SerializeField] private bool enablePerceptionRuntimeVisionSync = true;
    [Tooltip("感知同步间隔。0 表示每帧同步。")]
    [SerializeField] private float perceptionRuntimeVisionSyncInterval = 0.15f;
    [Tooltip("距离在半径多少比例内视为满视觉强度。默认 0.35 = 半径 35% 内满强度。")]
    [SerializeField, Range(0.01f, 1f)] private float fullAwarenessDistanceRatio = 0.35f;
    [Tooltip("扇形角度在半角多少比例内视为满视觉强度。默认 0.50 = 中央 50% 满强度。")]
    [SerializeField, Range(0.01f, 1f)] private float fullAwarenessAngleRatio = 0.50f;
    [Tooltip("开启时只同步玩家目标；关闭时同步所有可被感知的 Character，并按 CharacterIdentity 写入玩家/敌人/精英/BOSS/友军/中立/生物分类。")]
    [SerializeField] private bool perceptionSyncPlayerTargetsOnly = false;
    [Tooltip("开启后，Player/Ally 阵营中所有 shareVisionToFaction=true 的单位共享视野，用于感知同步。")]
    [SerializeField] private bool sharePlayerFactionVisionForPerception = true;
    [Tooltip("开启后，Enemy/Elite/Boss 阵营中所有 shareVisionToFaction=true 的单位共享视野，用于感知同步。")]
    [SerializeField] private bool shareHostileFactionVisionForPerception = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private static readonly RaycastHit[] s_VisionRaycastBuffer = new RaycastHit[32];

    // 预分配感知同步用的次要目标缓冲，避免每帧 new 数组
    private const int kMaxSecondaryStatic = 16;
    private readonly UnitDefinitionRuntimeBinder[] _secBinders   = new UnitDefinitionRuntimeBinder[kMaxSecondaryStatic];
    private readonly float[]                        _secAwareness = new float[kMaxSecondaryStatic];
    private readonly Vector3[]                      _secPositions = new Vector3[kMaxSecondaryStatic];

    private readonly List<VisionUnitEntry> visionUnits = new List<VisionUnitEntry>();
    private readonly List<VisionSourceData> playerFactionSources = new List<VisionSourceData>();
    private readonly List<VisionSourceData> hostileFactionSources = new List<VisionSourceData>();
    private float nextRescanTime = -1f;
    private float nextPerceptionRuntimeVisionSyncTime = -1f;
    private int lastRuntimeRefreshFrame = -1;

    public IReadOnlyList<VisionSourceData> PlayerFactionSources => playerFactionSources;
    public float VisualSoftEdgeWidth => Mathf.Max(0f, visualSoftEdgeWidth);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (autoScanOnEnable)
            RebuildCache();
    }

    private void OnEnable()
    {
        if (Instance == null)
            Instance = this;

        if (autoScanOnEnable)
            RebuildCache();
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        // 事件驱动注册后，周期性全场扫描只作为低频兜底（默认每30秒一次）
        if (autoRescanInterval && rescanInterval > 0f && Time.time >= nextRescanTime)
        {
            RebuildCache();
            nextRescanTime = Time.time + Mathf.Max(rescanInterval, 30f);
        }
        else
        {
            RefreshRuntimeVisionSources();
        }

        TrySyncPerceptionRuntimeVisionStates();
    }

    [ContextMenu("Rebuild Cache")]
    public void RebuildCache()
    {
        visionUnits.Clear();
        playerFactionSources.Clear();
        hostileFactionSources.Clear();

        UnitDefinitionRuntimeBinder[] binders = FindObjectsByType<UnitDefinitionRuntimeBinder>(FindObjectsSortMode.None);
        for (int i = 0; i < binders.Length; i++)
        {
            UnitDefinitionRuntimeBinder binder = binders[i];
            if (binder == null || binder.UnitDefinitionAsset == null)
                continue;

            UnitDefinition ud = binder.UnitDefinitionAsset;
            if (ud == null || ud.defineType != UnitDefineType.Character)
                continue;

            VisionUnitEntry entry = BuildEntry(binder, ud);
            visionUnits.Add(entry);

            if (!entry.isVisionEmitter || !entry.shareVisionToFaction)
                continue;

            if (entry.faction == RuntimeFaction.PlayerFaction)
                playerFactionSources.Add(entry.sourceData);
            else if (entry.faction == RuntimeFaction.HostileFaction)
                hostileFactionSources.Add(entry.sourceData);
        }


        lastRuntimeRefreshFrame = Time.frameCount;
        nextRescanTime = Time.time + Mathf.Max(0.1f, rescanInterval);
    }

    public bool IsWorldPositionVisibleToPlayerFaction(Vector3 worldPosition)
    {
        RefreshRuntimeVisionSources();
        if (playerFactionSources.Count == 0)
            return false;

        for (int i = 0; i < playerFactionSources.Count; i++)
        {
            if (IsWorldPositionVisibleBySource(playerFactionSources[i], worldPosition))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 仅供视觉层：返回 0~1，可用于边缘柔化。
    /// 1 = 完全可见；0 = 完全迷雾。
    /// </summary>
    public float EvaluatePlayerFactionVisibility01(Vector3 worldPosition)
    {
        RefreshRuntimeVisionSources();
        if (playerFactionSources.Count == 0)
            return 0f;

        float best = 0f;
        float soft = Mathf.Max(0.01f, visualSoftEdgeWidth);

        for (int i = 0; i < playerFactionSources.Count; i++)
        {
            float v = EvaluateSourceVisibility01(playerFactionSources[i], worldPosition, soft);
            if (v > best)
                best = v;

            if (best >= 1f)
                return 1f;
        }

        return Mathf.Clamp01(best);
    }

    public bool ShouldHideUnitFromPlayer(UnitDefinitionRuntimeBinder binder)
    {
        if (binder == null || binder.UnitDefinitionAsset == null)
            return false;

        UnitDefinition ud = binder.UnitDefinitionAsset;
        if (ud.ignoreFogOfWar)
            return false;

        RuntimeFaction faction = ResolveFaction(ud);
        if (faction == RuntimeFaction.PlayerFaction)
            return false;

        float visibility = EvaluatePlayerFactionUnitVisibility01(binder);
        return visibility <= unitHideVisibilityThreshold;
    }

    public bool ShouldShowUnitOnMinimap(UnitDefinitionRuntimeBinder binder)
    {
        if (binder == null || binder.UnitDefinitionAsset == null)
            return false;

        UnitDefinition ud = binder.UnitDefinitionAsset;
        if (ud.ignoreFogOfWar)
            return true;

        RuntimeFaction faction = ResolveFaction(ud);
        if (faction == RuntimeFaction.PlayerFaction)
            return true;

        return EvaluatePlayerFactionUnitVisibility01(binder) > unitHideVisibilityThreshold;
    }

    /// <summary>
    /// 单位显隐专用查询。
    /// 和战争迷雾地面显示保持一致：优先按 FogMaskRenderer 的半径 / 扇形 / 柔边逻辑判断，
    /// 不再让单位显隐额外被 Terrain VisionBlocker 射线遮挡，避免“地面已经亮了，单位仍像在迷雾里”的断链。
    /// </summary>
    private float EvaluatePlayerFactionUnitVisibility01(UnitDefinitionRuntimeBinder binder)
    {
        if (binder == null)
            return 0f;

        if (!unitVisibilityUseMultiSample)
            return EvaluatePlayerFactionUnitVisibilityAtPoint01(ResolveVisionTargetPointWorld(binder.transform));

        float best = 0f;

        // 1) 视觉中心：最接近玩家肉眼看到的单位位置。
        best = Mathf.Max(best, EvaluatePlayerFactionUnitVisibilityAtPoint01(ResolveVisionTargetPointWorld(binder.transform)));
        if (best >= 1f)
            return 1f;

        // 2) Binder Root：兼容原逻辑。
        best = Mathf.Max(best, EvaluatePlayerFactionUnitVisibilityAtPoint01(binder.transform.position));
        if (best >= 1f)
            return 1f;

        // 3) 脚底附近：避免单位只有脚先进入视野时仍被隐藏。
        Vector3 footPoint = binder.transform.position + Vector3.up * 0.25f;
        best = Mathf.Max(best, EvaluatePlayerFactionUnitVisibilityAtPoint01(footPoint));

        return Mathf.Clamp01(best);
    }

    private float EvaluatePlayerFactionUnitVisibilityAtPoint01(Vector3 worldPosition)
    {
        RefreshRuntimeVisionSources();
        if (playerFactionSources.Count == 0)
            return 0f;

        float best = 0f;
        float soft = Mathf.Max(0.01f, visualSoftEdgeWidth + unitVisibilityExtraPadding);

        for (int i = 0; i < playerFactionSources.Count; i++)
        {
            VisionSourceData source = playerFactionSources[i];

            float v = unitVisibilityUseFogMaskLikeQuery
                ? EvaluateSourceVisibility01NoTerrainBlock(source, worldPosition, soft)
                : EvaluateSourceVisibility01(source, worldPosition, soft);

            if (v > best)
                best = v;

            if (best >= 1f)
                return 1f;
        }

        return Mathf.Clamp01(best);
    }

    /// <summary>
    /// AI / 触发器用：判断观察单位是否能通过自身视野看见目标单位。
    /// 这里复用战争迷雾同一套视野定义：圆形 / 扇形 / 朝向 / 半径。
    /// </summary>
    public bool CanUnitSeeUnit(UnitDefinitionRuntimeBinder viewer, UnitDefinitionRuntimeBinder target)
    {
        if (viewer == null || target == null)
            return false;

        if (viewer.UnitDefinitionAsset == null || target.UnitDefinitionAsset == null)
            return false;

        UnitDefinition viewerDefinition = viewer.UnitDefinitionAsset;
        if (viewerDefinition.defineType != UnitDefineType.Character)
            return false;

        if (!viewerDefinition.enableVision && !viewerDefinition.ignoreFogOfWar)
            return false;

        VisionUnitEntry viewerEntry = BuildEntry(viewer, viewerDefinition);
        if (!viewerEntry.isVisionEmitter && !viewerEntry.sourceData.ignoreFogOfWar)
            return false;

        Vector3 targetPoint = ResolveVisionTargetPointWorld(target.transform);
        return IsWorldPositionVisibleBySource(viewerEntry.sourceData, targetPoint);
    }

    /// <summary>
    /// AI / 触发器用：Transform 入口。会自动向父级查找 UnitDefinitionRuntimeBinder。
    /// </summary>
    public bool CanTransformSeeTransform(Transform viewer, Transform target)
    {
        if (viewer == null || target == null)
            return false;

        UnitDefinitionRuntimeBinder viewerBinder = viewer.GetComponentInParent<UnitDefinitionRuntimeBinder>();
        UnitDefinitionRuntimeBinder targetBinder = target.GetComponentInParent<UnitDefinitionRuntimeBinder>();

        if (viewerBinder == null || targetBinder == null)
            return false;

        return CanUnitSeeUnit(viewerBinder, targetBinder);
    }

    /// <summary>
    /// AI 感知层用：返回观察单位对目标单位的视觉强度 0~1。
    /// 0 = 没有视觉感知；0.25/0.5/0.8 默认可分别进入怀疑/警戒/发现。
    /// </summary>
    public float EvaluateUnitSeeUnitAwareness01(UnitDefinitionRuntimeBinder viewer, UnitDefinitionRuntimeBinder target)
    {
        if (viewer == null || target == null)
            return 0f;

        if (viewer.UnitDefinitionAsset == null || target.UnitDefinitionAsset == null)
            return 0f;

        UnitDefinition viewerDefinition = viewer.UnitDefinitionAsset;
        if (viewerDefinition.defineType != UnitDefineType.Character)
            return 0f;

        if (!viewerDefinition.enableVision && !viewerDefinition.ignoreFogOfWar)
            return 0f;

        VisionUnitEntry viewerEntry = BuildEntry(viewer, viewerDefinition);
        if (!viewerEntry.isVisionEmitter && !viewerEntry.sourceData.ignoreFogOfWar)
            return 0f;

        Vector3 targetPoint = ResolveVisionTargetPointWorld(target.transform);
        return EvaluateSourceSightAwareness01(viewerEntry.sourceData, targetPoint);
    }

    /// <summary>
    /// 手动立即把当前视觉结果写入所有单位的 UnitPerceptionRuntime。
    /// V3 默认同步所有可被感知的 Character 目标，并按 CharacterIdentity 记录目标分类。
    /// </summary>
    [ContextMenu("Sync Perception Runtime Vision States Now")]
    public void SyncPerceptionRuntimeVisionStatesNow()
    {
        RefreshRuntimeVisionSources();
        SyncPerceptionRuntimeVisionStatesInternal();
    }

    public void RegisterOrRefresh(UnitDefinitionRuntimeBinder binder)
    {
        RegisterUnit(binder);
    }

    public void RegisterUnit(UnitDefinitionRuntimeBinder binder)
    {
        if (binder == null || binder.UnitDefinitionAsset == null)
            return;

        UnitDefinition ud = binder.UnitDefinitionAsset;
        if (ud.defineType != UnitDefineType.Character)
            return;

        // 若已存在则先移除旧条目
        for (int i = visionUnits.Count - 1; i >= 0; i--)
        {
            if (visionUnits[i].binder == binder)
            {
                visionUnits.RemoveAt(i);
                break;
            }
        }

        VisionUnitEntry entry = BuildEntry(binder, ud);
        visionUnits.Add(entry);

        // 下一帧 RefreshRuntimeVisionSources 会重建 playerFactionSources，这里强制重刷
        lastRuntimeRefreshFrame = -1;

    }

    public void UnregisterUnit(UnitDefinitionRuntimeBinder binder)
    {
        if (binder == null)
            return;

        for (int i = visionUnits.Count - 1; i >= 0; i--)
        {
            if (visionUnits[i].binder == binder)
            {
                visionUnits.RemoveAt(i);
                break;
            }
        }

        lastRuntimeRefreshFrame = -1;

    }

    /// <summary>
    /// 供 UnitFootstepAudioEmitter 等使用：遍历所有已注册单位的感知组件，避免 FindObjectsOfType 全场扫描。
    /// </summary>
    public void IteratePerceptionListeners(System.Action<UnitPerceptionRuntime> callback)
    {
        if (callback == null)
            return;

        for (int i = 0; i < visionUnits.Count; i++)
        {
            UnitDefinitionRuntimeBinder b = visionUnits[i].binder;
            if (b == null)
                continue;

            UnitPerceptionRuntime p = visionUnits[i].perceptionRuntime;
            if (p != null && p.isActiveAndEnabled)
                callback(p);
        }
    }

    private void TrySyncPerceptionRuntimeVisionStates()
    {
        if (!enablePerceptionRuntimeVisionSync)
            return;

        float interval = Mathf.Max(0f, perceptionRuntimeVisionSyncInterval);
        if (interval > 0f && Time.time < nextPerceptionRuntimeVisionSyncTime)
            return;

        nextPerceptionRuntimeVisionSyncTime = Time.time + interval;
        SyncPerceptionRuntimeVisionStatesInternal();
    }

    private void SyncPerceptionRuntimeVisionStatesInternal()
    {
        if (visionUnits.Count == 0)
        {
            return;
        }

        for (int i = 0; i < visionUnits.Count; i++)
        {
            VisionUnitEntry viewerEntry = visionUnits[i];
            UnitDefinitionRuntimeBinder viewer = viewerEntry.binder;
            if (viewer == null || viewer.UnitDefinitionAsset == null)
                continue;

            UnitPerceptionRuntime perception = viewerEntry.perceptionRuntime;
            // 首次注册时 UnitPerceptionRuntime 可能还未挂上，延迟补查
            if (perception == null)
            {
                perception = viewer.GetComponent<UnitPerceptionRuntime>()
                    ?? viewer.GetComponentInChildren<UnitPerceptionRuntime>(true);
                if (perception != null)
                {
                    viewerEntry.perceptionRuntime = perception;
                    viewerEntry.canBePerceived = perception.CanBePerceived;
                    visionUnits[i] = viewerEntry;
                }
            }
            if (perception == null || !perception.enabled || !perception.CanUseVision)
            {
                continue;
            }

            // 自身无视野，且阵营也没有共享视野源时，直接报告 0 感知
            bool hasFactionSources =
                (viewerEntry.faction == RuntimeFaction.PlayerFaction && sharePlayerFactionVisionForPerception && playerFactionSources.Count > 0) ||
                (viewerEntry.faction == RuntimeFaction.HostileFaction && shareHostileFactionVisionForPerception && hostileFactionSources.Count > 0);

            if (!viewerEntry.isVisionEmitter && !viewerEntry.sourceData.ignoreFogOfWar && !hasFactionSources)
            {
                perception.BeginVisionFrame();
                perception.ReportVisionAwareness(null, viewer.transform.position, 0f, false);
                continue;
            }

            // ── 多目标同步：遍历所有目标，找出每个目标的感知强度 ──
            // 视野共享：若阵营开启了 shareXxxFactionVisionForPerception，
            //           使用整个阵营的合并 sources 来评估 awareness（任一队友能看到 = 全队能感知）
            // primaryTarget = 整体感知强度最高的目标（主要时间累加器基于此）
            // 其余感知强度 > 0 的目标通过 ReportAdditionalVisionContact 注册到对应类别
            List<VisionSourceData> factionSources = null;
            if (viewerEntry.faction == RuntimeFaction.PlayerFaction && sharePlayerFactionVisionForPerception && playerFactionSources.Count > 0)
                factionSources = playerFactionSources;
            else if (viewerEntry.faction == RuntimeFaction.HostileFaction && shareHostileFactionVisionForPerception && hostileFactionSources.Count > 0)
                factionSources = hostileFactionSources;

            float bestAwareness = 0f;
            UnitDefinitionRuntimeBinder bestTarget = null;
            Vector3 bestTargetPosition = viewer.transform.position;

            int secondaryCount = 0;
            // 使用实例级预分配缓冲，避免每帧 new 数组产生 GC
            UnitDefinitionRuntimeBinder[] secondaryBinders   = _secBinders;
            float[]                        secondaryAwareness = _secAwareness;
            Vector3[]                      secondaryPositions = _secPositions;

            for (int j = 0; j < visionUnits.Count; j++)
            {
                VisionUnitEntry targetEntry = visionUnits[j];
                UnitDefinitionRuntimeBinder target = targetEntry.binder;
                if (target == null || target == viewer || target.UnitDefinitionAsset == null)
                    continue;

                // 同阵营单位不互相感知（只感知对立/中立方）
                if (targetEntry.faction == viewerEntry.faction)
                    continue;

                if (perceptionSyncPlayerTargetsOnly && !IsPlayerIdentity(target.UnitDefinitionAsset))
                    continue;

                // 目标的 perceptionRuntime 也可能延迟挂上，顺手补查
                if (targetEntry.perceptionRuntime == null)
                {
                    UnitPerceptionRuntime tp = target.GetComponent<UnitPerceptionRuntime>()
                        ?? target.GetComponentInChildren<UnitPerceptionRuntime>(true);
                    if (tp != null)
                    {
                        targetEntry.perceptionRuntime = tp;
                        targetEntry.canBePerceived = tp.CanBePerceived;
                        visionUnits[j] = targetEntry;
                    }
                }

                // canBePerceived 可在运行时改变（如濒死状态），每帧实时读取
                if (targetEntry.perceptionRuntime != null)
                {
                    targetEntry.canBePerceived = targetEntry.perceptionRuntime.CanBePerceived;
                    visionUnits[j] = targetEntry;
                }
                if (!targetEntry.canBePerceived)
                    continue;

                Vector3 targetPoint = targetEntry.previewGizmo != null
                    ? targetEntry.previewGizmo.GetResolvedVisionOriginWorld()
                    : target.transform.position + targetEntry.targetLocalOffset;

                // 有阵营共享视野时，取整个阵营能看到该目标的最高 awareness；否则只用自己的视野
                float awareness = factionSources != null
                    ? EvaluateFactionSightAwareness01(factionSources, targetPoint)
                    : EvaluateSourceSightAwareness01(viewerEntry.sourceData, targetPoint);
                if (awareness <= 0f)
                    continue;

                if (awareness > bestAwareness)
                {
                    // 旧 best 降级为次要
                    if (bestTarget != null && secondaryCount < kMaxSecondaryStatic)
                    {
                        secondaryBinders[secondaryCount] = bestTarget;
                        secondaryAwareness[secondaryCount] = bestAwareness;
                        secondaryPositions[secondaryCount] = bestTargetPosition;
                        secondaryCount++;
                    }
                    bestAwareness = awareness;
                    bestTarget = target;
                    bestTargetPosition = targetPoint;
                }
                else if (secondaryCount < kMaxSecondaryStatic)
                {
                    secondaryBinders[secondaryCount] = target;
                    secondaryAwareness[secondaryCount] = awareness;
                    secondaryPositions[secondaryCount] = targetPoint;
                    secondaryCount++;
                }
            }

            // 清除上一帧即时接触标志，本帧重新积累
            perception.BeginVisionFrame();

            // 主目标：驱动时间累加器和感知等级
            bool bestTargetIsPlayer = bestTarget != null && IsPlayerIdentity(bestTarget.UnitDefinitionAsset);
            perception.ReportVisionAwareness(bestTarget, bestTargetPosition, bestAwareness, bestTargetIsPlayer);

            // 次要目标：补充其他类别的 canSee*/hasDetected*
            for (int s = 0; s < secondaryCount; s++)
            {
                UnitDefinitionRuntimeBinder secTarget = secondaryBinders[s];
                if (secTarget == null || secTarget == bestTarget)
                    continue;
                perception.ReportAdditionalVisionContact(secTarget, secondaryPositions[s], secondaryAwareness[s]);
            }

        }
    }

    private static bool IsPlayerIdentity(UnitDefinition definition)
    {
        return definition != null && definition.characterIdentity == CharacterIdentity.Player;
    }

    private void RefreshRuntimeVisionSources()
    {
        if (lastRuntimeRefreshFrame == Time.frameCount)
            return;

        lastRuntimeRefreshFrame = Time.frameCount;
        playerFactionSources.Clear();
        hostileFactionSources.Clear();

        for (int i = 0; i < visionUnits.Count; i++)
        {
            VisionUnitEntry entry = visionUnits[i];
            if (entry.binder == null || entry.binder.UnitDefinitionAsset == null)
                continue;

            // 轻量更新：只刷新世界坐标和朝向，不重建 entry，无 GC 分配
            RefreshEntryWorldPositions(ref entry);
            visionUnits[i] = entry;


            if (!entry.isVisionEmitter || !entry.shareVisionToFaction)
                continue;

            if (entry.faction == RuntimeFaction.PlayerFaction)
                playerFactionSources.Add(entry.sourceData);
            else if (entry.faction == RuntimeFaction.HostileFaction)
                hostileFactionSources.Add(entry.sourceData);
        }
    }

    private VisionUnitEntry BuildEntry(UnitDefinitionRuntimeBinder binder, UnitDefinition ud)
    {
        RuntimeFaction faction = ResolveFaction(ud);
        bool useCircle = ud.characterIdentity == CharacterIdentity.Player || ud.visionShape == UnitVisionShape.Circle;
        bool useFacing = !useCircle && ud.useFacingDirection;

        Vector3 origin = ResolveVisionOriginWorld(binder.transform);
        Vector3 forward = ResolveVisionForwardWorld(binder.transform, useFacing);

        VisionSourceData source = new VisionSourceData
        {
            binder = binder,
            definition = ud,
            faction = faction,
            originWorld = origin,
            forwardWorld = forward,
            radius = Mathf.Max(0f, ud.visionRadius),
            angle = useCircle ? 360f : Mathf.Clamp(ud.visionAngle, 1f, 360f),
            useCircle = useCircle,
            useFacingDirection = useFacing,
            ignoreFogOfWar = ud.ignoreFogOfWar,
            shareVisionToFaction = ud.shareVisionToFaction,
        };

        VisionUnitEntry entry = new VisionUnitEntry
        {
            binder = binder,
            definition = ud,
            faction = faction,
            isVisionEmitter = ud.enableVision,
            shareVisionToFaction = ud.shareVisionToFaction,
            sourceData = source,
        };

        ComputeEntryOffsets(ref entry);
        return entry;
    }

    // 计算并缓存视野原点/目标点的本地偏移，只在 RebuildCache 时调用一次
    private static void ComputeEntryOffsets(ref VisionUnitEntry entry)
    {
        Transform root = entry.binder.transform;
        entry.previewGizmo = root.GetComponent<UnitVisionPreviewGizmo>();

        if (entry.previewGizmo != null)
        {
            entry.originLocalOffset = Vector3.zero;
            entry.targetLocalOffset = Vector3.zero;
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 rootPos = root.position;
            float cx = bounds.center.x - rootPos.x;
            float cz = bounds.center.z - rootPos.z;
            float baseY = bounds.min.y - rootPos.y;
            float height = bounds.size.y;

            entry.originLocalOffset = new Vector3(cx, baseY + height * 0.68f, cz);
            entry.targetLocalOffset = new Vector3(cx, baseY + height * 0.50f, cz);
        }
        else
        {
            entry.originLocalOffset = new Vector3(0f, 1.2f, 0f);
            entry.targetLocalOffset = new Vector3(0f, 0.8f, 0f);
        }

        // 缓存移动控制器，用于在 RefreshEntryWorldPositions 里读取真实 XZ 朝向
        entry.movementController = root.GetComponent<UnitMovementController>()
            ?? root.GetComponentInParent<UnitMovementController>()
            ?? root.GetComponentInChildren<UnitMovementController>(true);

        // 缓存感知组件引用，避免热路径 GetComponent
        entry.perceptionRuntime = root.GetComponent<UnitPerceptionRuntime>();
        entry.canBePerceived = entry.perceptionRuntime != null
            ? entry.perceptionRuntime.CanBePerceived
            : true; // 没有组件的 Character 默认可被感知（向后兼容）
    }

    // 每帧轻量刷新：只更新世界坐标和朝向，无任何 GC 分配
    private static void RefreshEntryWorldPositions(ref VisionUnitEntry entry)
    {
        Transform root = entry.binder.transform;
        VisionSourceData src = entry.sourceData;

        if (entry.previewGizmo != null)
        {
            src.originWorld = entry.previewGizmo.GetResolvedVisionOriginWorld();
            src.forwardWorld = entry.previewGizmo.GetResolvedForward();
        }
        else
        {
            src.originWorld = root.position + entry.originLocalOffset;

            if (src.useFacingDirection)
            {
                // 优先用 UnitMovementController.FacingInput（已合并 XZ 实际朝向 + override）
                if (entry.movementController != null)
                {
                    Vector2 facing = entry.movementController.FacingInput;
                    if (facing.sqrMagnitude > 0.0001f)
                    {
                        // FacingInput.x = X 轴，FacingInput.y = Z 轴（XZ 水平面）
                        src.forwardWorld = new Vector3(facing.x, 0f, facing.y).normalized;
                    }
                    else
                    {
                        bool facingRight = Mathf.Abs(root.localScale.x) > 0.0001f
                            ? root.localScale.x > 0f
                            : root.lossyScale.x >= 0f;
                        src.forwardWorld = facingRight ? Vector3.right : Vector3.left;
                    }
                }
                else
                {
                    bool facingRight = Mathf.Abs(root.localScale.x) > 0.0001f
                        ? root.localScale.x > 0f
                        : root.lossyScale.x >= 0f;
                    src.forwardWorld = facingRight ? Vector3.right : Vector3.left;
                }
            }
            else
            {
                src.forwardWorld = Vector3.right;
            }
        }

        entry.sourceData = src;
    }

    private RuntimeFaction ResolveFaction(UnitDefinition ud)
    {
        switch (ud.characterIdentity)
        {
            case CharacterIdentity.Player:
            case CharacterIdentity.Ally:
            case CharacterIdentity.NeutralPassive:
            case CharacterIdentity.Creature:
                return RuntimeFaction.PlayerFaction;

            case CharacterIdentity.Enemy:
            case CharacterIdentity.Elite:
            case CharacterIdentity.Boss:
            case CharacterIdentity.NeutralHostile:
                return RuntimeFaction.HostileFaction;

            default:
                return RuntimeFaction.Unknown;
        }
    }

    private Vector3 ResolveVisionOriginWorld(Transform root)
    {
        UnitVisionPreviewGizmo preview = root.GetComponent<UnitVisionPreviewGizmo>();
        if (preview != null)
            return preview.GetResolvedVisionOriginWorld();

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.68f, bounds.center.z);
        }

        return root.position + Vector3.up * 1.2f;
    }

    private Vector3 ResolveVisionTargetPointWorld(Transform root)
    {
        if (root == null)
            return Vector3.zero;

        UnitVisionPreviewGizmo preview = root.GetComponent<UnitVisionPreviewGizmo>();
        if (preview != null)
            return preview.GetResolvedVisionOriginWorld();

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.50f, bounds.center.z);
        }

        return root.position + Vector3.up * 0.8f;
    }

    private Vector3 ResolveVisionForwardWorld(Transform root, bool useFacing)
    {
        UnitVisionPreviewGizmo preview = root.GetComponent<UnitVisionPreviewGizmo>();
        if (preview != null)
            return preview.GetResolvedForward();

        if (!useFacing)
            return Vector3.right;

        bool facingRight = true;
        if (Mathf.Abs(root.localScale.x) > 0.0001f)
            facingRight = root.localScale.x > 0f;
        else if (Mathf.Abs(root.lossyScale.x) > 0.0001f)
            facingRight = root.lossyScale.x > 0f;

        return facingRight ? Vector3.right : Vector3.left;
    }

    /// <summary>从阵营所有视野源中取最高 awareness，实现视野共享。</summary>
    private float EvaluateFactionSightAwareness01(List<VisionSourceData> sources, Vector3 worldPosition)
    {
        float best = 0f;
        for (int k = 0; k < sources.Count; k++)
        {
            float v = EvaluateSourceSightAwareness01(sources[k], worldPosition);
            if (v > best)
            {
                best = v;
                if (best >= 1f)
                    break;
            }
        }
        return best;
    }

    private float EvaluateSourceSightAwareness01(VisionSourceData source, Vector3 worldPosition)
    {
        if (source.ignoreFogOfWar)
            return 1f;

        Vector3 delta = worldPosition - source.originWorld;
        delta.y = 0f;

        float distance = delta.magnitude;
        if (source.radius <= 0f || distance > source.radius)
            return 0f;

        float distanceFull = Mathf.Clamp01(fullAwarenessDistanceRatio) * source.radius;
        distanceFull = Mathf.Clamp(distanceFull, 0.01f, Mathf.Max(0.01f, source.radius));
        float distanceAwareness = 1f - Mathf.InverseLerp(distanceFull, source.radius, distance);
        distanceAwareness = Mathf.Clamp01(distanceAwareness);

        float angleAwareness = 1f;
        if (!source.useCircle && source.useFacingDirection && distance > 0.0001f)
        {
            float half = source.angle * 0.5f;
            float angleToTarget = Vector3.Angle(source.forwardWorld, delta.normalized);
            if (angleToTarget > half)
                return 0f;

            float angleFull = Mathf.Clamp01(fullAwarenessAngleRatio) * half;
            angleFull = Mathf.Clamp(angleFull, 0.01f, Mathf.Max(0.01f, half));
            angleAwareness = 1f - Mathf.InverseLerp(angleFull, half, angleToTarget);
            angleAwareness = Mathf.Clamp01(angleAwareness);
        }

        float awareness = Mathf.Min(distanceAwareness, angleAwareness);
        if (awareness <= 0f)
            return 0f;

        if (IsTerrainVisionBlocked(source, worldPosition))
            return 0f;

        return Mathf.Clamp01(awareness);
    }

    private bool IsWorldPositionVisibleBySource(VisionSourceData source, Vector3 worldPosition)
    {
        if (source.ignoreFogOfWar)
            return true;

        Vector3 delta = worldPosition - source.originWorld;
        delta.y = 0f;

        float distance = delta.magnitude;
        if (distance > source.radius)
            return false;

        bool shapeVisible = true;
        if (!source.useCircle && source.useFacingDirection)
        {
            if (distance > 0.0001f)
            {
                float angleToTarget = Vector3.Angle(source.forwardWorld, delta.normalized);
                shapeVisible = angleToTarget <= source.angle * 0.5f;
            }
        }

        if (!shapeVisible)
            return false;

        return !IsTerrainVisionBlocked(source, worldPosition);
    }

    private float EvaluateSourceVisibility01NoTerrainBlock(VisionSourceData source, Vector3 worldPosition, float softWidth)
    {
        if (source.ignoreFogOfWar)
            return 1f;

        Vector3 delta = worldPosition - source.originWorld;
        delta.y = 0f;

        float distance = delta.magnitude;
        if (distance > source.radius + softWidth)
            return 0f;

        float radiusVisible = 1f - Mathf.InverseLerp(source.radius, source.radius + softWidth, distance);
        radiusVisible = Mathf.Clamp01(radiusVisible);

        float shapeVisible = radiusVisible;
        if (!source.useCircle && source.useFacingDirection)
        {
            if (distance > 0.0001f)
            {
                float half = source.angle * 0.5f;
                float angleToTarget = Vector3.Angle(source.forwardWorld, delta.normalized);
                float angleVisible = 1f - Mathf.InverseLerp(half, half + 8f, angleToTarget);
                angleVisible = Mathf.Clamp01(angleVisible);
                shapeVisible = Mathf.Min(radiusVisible, angleVisible);
            }
        }

        return Mathf.Clamp01(shapeVisible);
    }

    private float EvaluateSourceVisibility01(VisionSourceData source, Vector3 worldPosition, float softWidth)
    {
        if (source.ignoreFogOfWar)
            return 1f;

        Vector3 delta = worldPosition - source.originWorld;
        delta.y = 0f;

        float distance = delta.magnitude;
        if (distance > source.radius + softWidth)
            return 0f;

        float radiusVisible = 1f - Mathf.InverseLerp(source.radius, source.radius + softWidth, distance);
        radiusVisible = Mathf.Clamp01(radiusVisible);

        float shapeVisible = radiusVisible;
        if (!source.useCircle && source.useFacingDirection)
        {
            if (distance > 0.0001f)
            {
                float half = source.angle * 0.5f;
                float angleToTarget = Vector3.Angle(source.forwardWorld, delta.normalized);
                float angleVisible = 1f - Mathf.InverseLerp(half, half + 8f, angleToTarget);
                angleVisible = Mathf.Clamp01(angleVisible);
                shapeVisible = Mathf.Min(radiusVisible, angleVisible);
            }
        }

        if (shapeVisible <= 0f)
            return 0f;

        if (IsTerrainVisionBlocked(source, worldPosition))
            return 0f;

        return shapeVisible;
    }

    private bool IsTerrainVisionBlocked(VisionSourceData source, Vector3 worldPosition)
    {
        if (!enableTerrainVisionBlockers)
            return false;

        Vector3 from = source.originWorld;
        Vector3 to = worldPosition;
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance <= 0.001f)
            return false;

        Vector3 direction = delta / distance;
        float startPadding = Mathf.Clamp(terrainVisionBlockerStartPadding, 0f, Mathf.Max(0f, distance - 0.001f));
        Vector3 rayOrigin = from + direction * startPadding;
        float rayDistance = Mathf.Max(0f, distance - startPadding);

        int hitCount = Physics.RaycastNonAlloc(rayOrigin, direction, s_VisionRaycastBuffer, rayDistance, ~0, terrainVisionBlockerTriggerInteraction);
        if (hitCount == 0)
        {
            DrawVisionBlockerDebugLine(from, to, false);
            return false;
        }

        TerrainDecorationRuntimeBinder legacyCandidate = null;
        Collider legacyCandidateCollider = null;

        for (int i = 0; i < hitCount; i++)
        {
            Collider c = s_VisionRaycastBuffer[i].collider;
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
                continue;

            TerrainDecorationRuntimeBinder terrainBinder = c.GetComponentInParent<TerrainDecorationRuntimeBinder>();
            if (terrainBinder == null || terrainBinder.definition == null)
                continue;

            if (!terrainBinder.definition.blockVision)
                continue;

            if (IsExplicitVisionBlockerCollider(c.transform))
            {
                DrawVisionBlockerDebugLine(from, to, true);
                return true;
            }

            if (legacyCandidate == null)
            {
                legacyCandidate = terrainBinder;
                legacyCandidateCollider = c;
            }
        }

        bool allowLegacy = !requireExplicitVisionBlockerNode || allowLegacyTerrainColliderVisionBlock;
        if (allowLegacy && legacyCandidate != null)
        {
            DrawVisionBlockerDebugLine(from, to, true);
            return true;
        }

        DrawVisionBlockerDebugLine(from, to, false);
        return false;
    }

    private static bool IsExplicitVisionBlockerCollider(Transform hitTransform)
    {
        Transform t = hitTransform;
        while (t != null)
        {
            string n = t.name;
            if (n == "Vision_Blocker_Box" || n == "VisionBlockerRoot")
                return true;

            t = t.parent;
        }

        return false;
    }

    private void DrawVisionBlockerDebugLine(Vector3 from, Vector3 to, bool blocked)
    {
        if (!debugDrawVisionBlockerRays)
            return;

        Debug.DrawLine(from, to, blocked ? Color.red : Color.green, 0.08f, false);
    }

    [System.Serializable]
    public struct VisionSourceData
    {
        public UnitDefinitionRuntimeBinder binder;
        public UnitDefinition definition;
        public RuntimeFaction faction;
        public Vector3 originWorld;
        public Vector3 forwardWorld;
        public float radius;
        public float angle;
        public bool useCircle;
        public bool useFacingDirection;
        public bool ignoreFogOfWar;
        public bool shareVisionToFaction;
    }

    private struct VisionUnitEntry
    {
        public UnitDefinitionRuntimeBinder binder;
        public UnitDefinition definition;
        public RuntimeFaction faction;
        public bool isVisionEmitter;
        public bool shareVisionToFaction;
        public VisionSourceData sourceData;

        // 缓存字段，RebuildCache 时计算一次，热路径复用
        public UnitVisionPreviewGizmo previewGizmo;
        public UnitMovementController movementController; // 用于读取 XZ 朝向
        public Vector3 originLocalOffset;      // 视野发射点相对 root.position 的偏移
        public Vector3 targetLocalOffset;      // 被感知点相对 root.position 的偏移
        public UnitPerceptionRuntime perceptionRuntime; // 可为 null
        public bool canBePerceived;            // 缓存 CanBePerceived，无组件时默认 true
    }
}
