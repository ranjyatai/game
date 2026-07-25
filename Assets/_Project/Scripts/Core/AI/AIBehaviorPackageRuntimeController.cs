using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// AI 行为包运行器。
/// 当前支持：
/// 1）初始化事件；
/// 2）每 N 秒事件；
/// 3）当前目标 / 行为类型 / 看见玩家 / 距离比较；
/// 4）移动到坐标、随机徘徊、远离目标、停止移动；
/// 5）移动方式 Walk / Run -> UnitMovementController.runHeld；
/// 6）移动卡住保护：撞墙 / 被障碍物阻挡时自动停止当前移动指令。
///
/// 注意：这一版仍然保持“轻量运行时解释器”。“看见”优先走 SkyPrisonVisionManager 的单位视野判定，找不到视野系统时才回退为距离判定。
/// </summary>
[DefaultExecutionOrder(9300)]
public class AIBehaviorPackageRuntimeController : MonoBehaviour
{
    [Header("AI Package")]
    [SerializeField] private AIBehaviorPackage behaviorPackage;
    [SerializeField] private bool runInitializeOnEnable = true;
    [SerializeField] private bool rerunInitializeWhenPackageChanged = true;

    [Header("Movement")]
    [SerializeField] private UnitMovementController movementController;
    [SerializeField] private UnitActionController   actionController;
    [SerializeField] private UnitActionModuleRuntime actionModule;
    private UnitDeathController _deathController;
    private bool _targetWasDeadLike;
    private Transform _deadLikeTarget;
    private Vector2 _smoothedMoveInput;
    // 目标濒死后禁止回出生点的冷却截止时间（防止清空 target 后立刻触发出生点返回）
    private float _spawnReturnBlockedUntil = -1f;
    [SerializeField] private float arriveDistance = 0.18f;
    [SerializeField] private bool keepCurrentY = true;
    [SerializeField] private bool snapToTargetOnArrive = false;

    [Header("Movement / Stuck Protection")]
    [Tooltip("开启后，AI 有移动目标但一段时间几乎没有位移时，会判定为被障碍物卡住，并自动停止当前移动指令。")]
    [SerializeField] private bool stopMoveWhenStuck = true;
    [SerializeField] private float stuckCheckInterval = 0.35f;
    [SerializeField] private float stuckMinMoveDistance = 0.04f;
    [SerializeField] private float stuckStopDelay = 0.9f;
    [SerializeField] private bool clearMoveTargetWhenStuck = true;

    [Header("Perception / Target")]
    [SerializeField] private Transform currentTarget;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float defaultVisionRange = 8f;
    [SerializeField] private bool autoFindPlayerByTag = true;

    [Header("AI State")]
    [SerializeField] private string behaviorType = "Wander";

    [Header("性能")]
    [Tooltip("AI 逻辑（规则求值）的最小执行间隔（秒）。移动平滑仍然每帧更新。0 = 每帧执行（不限速）。" +
             "之前默认 0.05s（约3帧）——玩家贴近敌人时，是否追击/换目标这类判断最多要等3帧才反应，" +
             "看起来像排序穿帮，其实是这个节流造成的。调到 0.02s 降低感知延迟，仍保留一点节流省性能。")]
    [SerializeField] private float aiLogicInterval = 0.02f;
    private float _nextAILogicTime = -1f;

    [Header("分离避让")]
    [Tooltip("与同阵营 AI 单位之间的分离半径（米）。0 = 关闭分离。")]
    [SerializeField] private float separationRadius = 1.2f;
    [Tooltip("分离力相对于移动方向的权重。1 = 与目标方向等权。")]
    [SerializeField] private float separationWeight = 1.5f;
    [Tooltip("攻击范围相对于技能 Hitbox 的扩展倍率。1 = 精确 Hitbox 边界，1.5 = 扩大 50%。")]
    [SerializeField] private float attackRangeMultiplier = 1.5f;
    private static readonly List<AIBehaviorPackageRuntimeController> _allAI = new List<AIBehaviorPackageRuntimeController>();

    // 分离避让扫描节流：这段是对 _allAI 全体做 O(n) 距离检查，之前每帧无条件执行，
    // 巡逻单位密集的区域会变成每帧 O(n²)，直接反映成帧率骤降。分离方向不需要逐帧
    // 重算也看不出来（角色本身还是每帧在移动），改成跟 aiLogicInterval 同频重算一次，
    // 中间帧复用上一次结果。
    private const float SeparationRecalcInterval = 0.1f;
    private float _nextSeparationRecalcTime = -1f;
    private Vector2 _cachedSeparation;

    [Header("Runtime Debug")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool hasMoveTarget = false;
    [SerializeField] private Vector3 currentMoveTarget;
    [SerializeField] private bool currentMoveRunHeld = false;
    [SerializeField] private string currentMoveModeDebug = "";
    [SerializeField] private bool lastMoveFailedByStuck = false;
    [SerializeField] private string lastExecutedSentence = "";

    private AIBehaviorPackage initializedPackage;
    private bool initializedOnce;
    private Vector3 stuckLastCheckPosition;
    private float stuckLastCheckTime;
    private float stuckAccumulatedTime;
    private readonly Dictionary<string, float> ruleNextRunTimes = new Dictionary<string, float>();

    // 预排序规则缓存，避免每帧 new List + Sort
    private readonly List<AIBehaviorPackage.AIRule> _sortedRulesCache = new List<AIBehaviorPackage.AIRule>();
    private AIBehaviorPackage _cachedSortedPackage;
    private int _cachedSortedRuleCount = -1;
    // rules/{i}、tree/{i}/{j} 这类 key 字符串之前每次 tick 都用字符串插值现拼一遍——
    // 每个AI单位、每条规则/每个树节点、每次逻辑判定都在拼字符串，是持续贯穿全程的
    // GC分配源（跟"哪怕站着不动GC也一直很高"这个症状完全对得上，因为AI逻辑判定
    // 不看玩家是否在动）。这两份缓存按节点/规则对象本身的引用做key，只在第一次
    // 遇到某个节点/规则时拼一次字符串，之后一直复用同一个string实例。
    private readonly Dictionary<AIBehaviorPackage.AIRuleTreeNode, string> _nodeKeyCache = new Dictionary<AIBehaviorPackage.AIRuleTreeNode, string>();
    private readonly List<string> _sortedRuleKeysCache = new List<string>();

    public AIBehaviorPackage BehaviorPackage => behaviorPackage;
    public Transform CurrentTarget => currentTarget;
    public string BehaviorType => behaviorType;
    public bool HasMoveTarget => hasMoveTarget;
    public Vector3 CurrentMoveTarget => currentMoveTarget;
    public bool LastMoveFailedByStuck => lastMoveFailedByStuck;

    private void Reset()
    {
        AutoSetup();
    }

    private Vector3 _spawnPosition;

    private void Awake()
    {
        _spawnPosition = transform.position;
        AutoSetup();
    }

    private void OnEnable()
    {
        AutoSetup();
        _allAI.Add(this);

        // 随机相位偏移，防止所有 AI 同帧评估规则导致行为同化
        _nextAILogicTime = Time.time + UnityEngine.Random.Range(0f, aiLogicInterval * 10f);
        // 分离扫描同理错开相位，避免所有巡逻单位在同一帧一起触发 O(n) 重算
        _nextSeparationRecalcTime = Time.time + UnityEngine.Random.Range(0f, SeparationRecalcInterval);

        if (runInitializeOnEnable)
            RunInitializeIfNeeded(force: true);
    }

    private void OnDisable()
    {
        _allAI.Remove(this);
    }

    private void Update()
    {
        if (_deathController == null)
            _deathController = GetComponentInParent<UnitDeathController>(true)
                            ?? GetComponent<UnitDeathController>();

        if (_deathController != null && _deathController.IsDeadLike)
        {
            movementController?.ClearMoveInput();
            return;
        }

        // 当前目标死亡/复活处理
        if (currentTarget != null)
        {
            UnitDeathController targetDeath = currentTarget.GetComponentInParent<UnitDeathController>(true)
                                           ?? currentTarget.GetComponent<UnitDeathController>();
            bool isDeadNow = targetDeath != null && targetDeath.IsDeadLike;
            if (isDeadNow && !_targetWasDeadLike)
            {
                // 目标进入濒死：清空 target + 停止移动 + 启动冷却（冷却期内禁止回出生点）
                _deadLikeTarget = currentTarget;
                currentTarget = null;
                _targetWasDeadLike = true;
                // 每个单位随机错开 0~4 秒再出发，避免所有敌人同时涌向各自出生点互相堵死
                _spawnReturnBlockedUntil = Time.time + UnityEngine.Random.Range(0f, 4f);
                FinishMoveTarget();
                GetComponent<UnitPerceptionRuntime>()?.ClearTransientState();
            }
        }

        // 持续监听濒死目标是否复活
        if (_targetWasDeadLike && _deadLikeTarget != null)
        {
            UnitDeathController targetDeath = _deadLikeTarget.GetComponentInParent<UnitDeathController>(true)
                                           ?? _deadLikeTarget.GetComponent<UnitDeathController>();
            if (targetDeath == null || !targetDeath.IsDeadLike)
            {
                // 目标复活：解除冷却，重置濒死标记，等待感知重新建立目标
                _deadLikeTarget = null;
                _targetWasDeadLike = false;
                _spawnReturnBlockedUntil = -1f;
            }
        }

        // AI 逻辑频率限制：规则求值不必每帧执行
        bool runLogic = aiLogicInterval <= 0f || Time.time >= _nextAILogicTime;
        if (runLogic)
        {
            _nextAILogicTime = Time.time + aiLogicInterval;
            EnsureExternalMovementOwnership();

            if (rerunInitializeWhenPackageChanged && initializedPackage != behaviorPackage)
                RunInitializeIfNeeded(force: true);

            // 攻击结束后恢复正常朝向驱动
            if (movementController != null && actionController != null && !actionController.IsAttacking)
                movementController.ClearFacingOverride();

            ExecuteTickRules();
        }

        // 移动目标追踪保持每帧，确保移动平滑
        TickMoveTarget();
    }

    public void AutoSetup()
    {
        if (movementController == null)
            movementController = GetComponent<UnitMovementController>();

        if (movementController == null)
            movementController = GetComponentInParent<UnitMovementController>();

        if (movementController == null)
            movementController = GetComponentInChildren<UnitMovementController>(true);

        if (actionController == null)
            actionController = GetComponent<UnitActionController>()
                            ?? GetComponentInParent<UnitActionController>()
                            ?? GetComponentInChildren<UnitActionController>(true);

        if (actionModule == null)
            actionModule = GetComponent<UnitActionModuleRuntime>()
                        ?? GetComponentInParent<UnitActionModuleRuntime>()
                        ?? GetComponentInChildren<UnitActionModuleRuntime>(true);

        EnsureExternalMovementOwnership();
    }

    private void EnsureExternalMovementOwnership()
    {
        if (movementController == null)
            movementController = GetComponent<UnitMovementController>();

        if (movementController == null)
            movementController = GetComponentInParent<UnitMovementController>();

        if (movementController == null)
            movementController = GetComponentInChildren<UnitMovementController>(true);

        if (movementController == null)
            return;

        if (movementController.InputMode != UnitMovementController.MovementInputMode.External)
            movementController.SetInputMode(UnitMovementController.MovementInputMode.External);
    }

    public void SetBehaviorPackage(AIBehaviorPackage package)
    {
        if (behaviorPackage == package)
            return;

        behaviorPackage = package;
        initializedPackage = null;
        initializedOnce = false;
        ruleNextRunTimes.Clear();
        ClearCurrentAction();

        if (isActiveAndEnabled && runInitializeOnEnable)
            RunInitializeIfNeeded(force: true);
    }

    public void SetCurrentTarget(Transform target)
    {
        currentTarget = target;
    }

    public void ClearCurrentTarget()
    {
        currentTarget = null;
    }

    public void SetBehaviorType(string nextBehaviorType)
    {
        behaviorType = NormalizeBehaviorType(nextBehaviorType);
    }

    [ContextMenu("Run Initialize Now")]
    public void RunInitializeNow()
    {
        RunInitializeIfNeeded(force: true);
    }

    public void ClearCurrentAction()
    {
        hasMoveTarget = false;
        currentMoveRunHeld = false;
        ResetStuckTracking(clearFailureFlag: false);

        if (movementController != null)
            movementController.ClearMoveInput();
    }

    private void RunInitializeIfNeeded(bool force)
    {
        if (behaviorPackage == null)
            return;

        if (!force && initializedOnce && initializedPackage == behaviorPackage)
            return;

        initializedPackage = behaviorPackage;
        initializedOnce = true;
        ruleNextRunTimes.Clear();

        if (movementController == null)
            AutoSetup();

        ExecuteInitializeRules(behaviorPackage);
    }

    private void ExecuteInitializeRules(AIBehaviorPackage package)
    {
        bool executedAny = false;

        if (package.ruleTree != null && package.ruleTree.Count > 0)
        {
            for (int i = 0; i < package.ruleTree.Count; i++)
                executedAny |= ExecuteInitializeNode(package.ruleTree[i]);
        }

        if (!executedAny && package.rules != null)
        {
            List<AIBehaviorPackage.AIRule> sorted = new List<AIBehaviorPackage.AIRule>(package.rules);
            sorted.Sort((a, b) => b.priority.CompareTo(a.priority));

            for (int i = 0; i < sorted.Count; i++)
            {
                if (ExecuteInitializeRule(sorted[i]))
                    executedAny = true;
            }
        }

        if (debugLogs)
            Debug.Log($"[AIBehaviorPackageRuntimeController] {name}: initialize package={package.name}, executed={executedAny}", this);
    }

    private bool ExecuteInitializeNode(AIBehaviorPackage.AIRuleTreeNode node)
    {
        if (node == null || !node.enabled)
            return false;

        bool executedAny = false;

        if (node.nodeType == AIBehaviorPackage.RuleTreeNodeType.Rule)
        {
            executedAny |= ExecuteInitializeRule(node.ruleData);
        }
        else if (node.children != null)
        {
            for (int i = 0; i < node.children.Count; i++)
                executedAny |= ExecuteInitializeNode(node.children[i]);
        }

        return executedAny;
    }

    private bool ExecuteInitializeRule(AIBehaviorPackage.AIRule rule)
    {
        if (rule == null || !rule.enabled)
            return false;

        if (!HasInitializeEvent(rule))
            return false;

        if (!EvaluateRuleConditions(rule))
            return false;

        return ExecuteActionGroup(rule);
    }

    private void ExecuteTickRules()
    {
        if (behaviorPackage == null)
            return;

        if (behaviorPackage.ruleTree != null && behaviorPackage.ruleTree.Count > 0)
        {
            for (int i = 0; i < behaviorPackage.ruleTree.Count; i++)
            {
                AIBehaviorPackage.AIRuleTreeNode node = behaviorPackage.ruleTree[i];
                if (node == null) continue;
                if (!_nodeKeyCache.TryGetValue(node, out string key))
                {
                    key = $"tree/{i}";
                    _nodeKeyCache[node] = key;
                }
                ExecuteTickNode(node, key);
            }
            return;
        }

        if (behaviorPackage.rules == null)
            return;

        // 规则列表和包不变时复用排序缓存，避免每帧 new List + Sort
        if (_cachedSortedPackage != behaviorPackage || _cachedSortedRuleCount != behaviorPackage.rules.Count)
        {
            _cachedSortedPackage = behaviorPackage;
            _cachedSortedRuleCount = behaviorPackage.rules.Count;
            _sortedRulesCache.Clear();
            _sortedRulesCache.AddRange(behaviorPackage.rules);
            _sortedRulesCache.Sort((a, b) => b.priority.CompareTo(a.priority));

            _sortedRuleKeysCache.Clear();
            for (int i = 0; i < _sortedRulesCache.Count; i++)
                _sortedRuleKeysCache.Add($"rules/{i}");
        }

        for (int i = 0; i < _sortedRulesCache.Count; i++)
            ExecuteTickRule(_sortedRulesCache[i], _sortedRuleKeysCache[i]);
    }

    private void ExecuteTickNode(AIBehaviorPackage.AIRuleTreeNode node, string key)
    {
        if (node == null || !node.enabled)
            return;

        if (node.nodeType == AIBehaviorPackage.RuleTreeNodeType.Rule)
        {
            ExecuteTickRule(node.ruleData, key);
            return;
        }

        if (node.children == null)
            return;

        for (int i = 0; i < node.children.Count; i++)
        {
            AIBehaviorPackage.AIRuleTreeNode child = node.children[i];
            if (child == null) continue;
            if (!_nodeKeyCache.TryGetValue(child, out string childKey))
            {
                childKey = $"{key}/{i}";
                _nodeKeyCache[child] = childKey;
            }
            ExecuteTickNode(child, childKey);
        }
    }

    private bool ExecuteTickRule(AIBehaviorPackage.AIRule rule, string key)
    {
        if (rule == null || !rule.enabled)
            return false;

        if (!TryGetEverySecondsInterval(rule, out float interval))
            return false;

        interval = Mathf.Max(0.02f, interval);

        if (!ruleNextRunTimes.TryGetValue(key, out float nextRunTime))
        {
            nextRunTime = Time.time;
            ruleNextRunTimes[key] = nextRunTime;
        }

        if (Time.time < nextRunTime)
            return false;

        ruleNextRunTimes[key] = Time.time + interval;

        if (!EvaluateRuleConditions(rule))
            return false;

        return ExecuteActionGroup(rule);
    }

    private bool HasInitializeEvent(AIBehaviorPackage.AIRule rule)
    {
        IList motiveItems = GetSentenceItems(rule?.sentenceRoot?.motiveGroup);

        if (motiveItems == null || motiveItems.Count == 0)
            return true;

        for (int i = 0; i < motiveItems.Count; i++)
        {
            string templateId = GetTemplateId(motiveItems[i]);
            if (templateId == "event_initialize")
                return true;
        }

        return false;
    }

    private bool TryGetEverySecondsInterval(AIBehaviorPackage.AIRule rule, out float interval)
    {
        interval = 0f;
        IList motiveItems = GetSentenceItems(rule?.sentenceRoot?.motiveGroup);

        if (motiveItems == null || motiveItems.Count == 0)
            return false;

        for (int i = 0; i < motiveItems.Count; i++)
        {
            object sentence = motiveItems[i];
            if (GetTemplateId(sentence) != "event_every_seconds")
                continue;

            interval = GetFloatSlot(sentence, "seconds", 1f);
            return true;
        }

        return false;
    }

    private bool EvaluateRuleConditions(AIBehaviorPackage.AIRule rule)
    {
        IList conditionItems = GetSentenceItems(rule?.sentenceRoot?.conditionGroup);
        if (conditionItems == null || conditionItems.Count == 0)
            return true;

        bool requireAll = rule.requireAllConditions;
        bool anyTrue = false;

        for (int i = 0; i < conditionItems.Count; i++)
        {
            bool result = EvaluateConditionSentence(conditionItems[i]);

            if (requireAll && !result)
                return false;

            if (result)
                anyTrue = true;
        }

        return requireAll || anyTrue;
    }

    private bool EvaluateConditionSentence(object sentence)
    {
        string templateId = GetTemplateId(sentence);

        if (string.IsNullOrWhiteSpace(templateId))
            return true;

        switch (templateId)
        {
            case "cond_unit_no_current_target":
            {
                bool expected = GetBoolSlot(sentence, "expected", true);
                return (currentTarget == null) == expected;
            }

            case "cond_unit_current_target_exists":
            {
                bool expected = GetBoolSlot(sentence, "expected", true);
                return (currentTarget != null) == expected;
            }

            case "cond_unit_can_see_unit":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                Transform target = ResolveUnitSlot(sentence, "target", currentTarget != null ? currentTarget : GetPlayerTransform());
                bool expected = GetBoolSlot(sentence, "expected", true);
                return CanSeeTransform(subject, target, defaultVisionRange) == expected;
            }

            case "cond_unit_can_see_player":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                return CanSeeTransform(subject, GetPlayerTransform(), defaultVisionRange) == expected;
            }

            case "cond_unit_can_see_current_target":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                return CanSeeTransform(subject, currentTarget, defaultVisionRange) == expected;
            }


            case "cond_unit_can_see_perception_target":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                string targetKind = GetEnumSlot(sentence, "targetKind", "Player");
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return EvaluatePerceptionTargetFlag(perception, targetKind, PerceptionTargetFlagMode.CanSee, currentTarget) == expected;
            }

            case "cond_unit_can_hear_perception_target":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                string targetKind = GetEnumSlot(sentence, "targetKind", "Player");
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return EvaluatePerceptionTargetFlag(perception, targetKind, PerceptionTargetFlagMode.CanHear, currentTarget) == expected;
            }

            case "cond_unit_has_detected_perception_target":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                string targetKind = GetEnumSlot(sentence, "targetKind", "Player");
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return EvaluatePerceptionTargetFlag(perception, targetKind, PerceptionTargetFlagMode.Detected, currentTarget) == expected;
            }

            case "cond_unit_is_suspicious":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.IsSuspicious) == expected;
            }

            case "cond_unit_is_alert":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.IsAlert) == expected;
            }

            case "cond_unit_has_detected_player":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.HasDetectedPlayer) == expected;
            }

            case "cond_unit_can_hear_player":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.CanHearPlayer) == expected;
            }

            case "cond_unit_can_hear_hostile_unit":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.CanHearHostileUnit) == expected;
            }

            case "cond_unit_can_hear_friendly_unit":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.CanHearFriendlyUnit) == expected;
            }

            case "cond_unit_can_hear_neutral_unit":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.CanHearNeutralUnit) == expected;
            }

            case "cond_unit_can_hear_creature_unit":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.CanHearCreatureUnit) == expected;
            }

            case "cond_unit_has_last_heard_position":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.HasLastHeardPosition) == expected;
            }

            case "cond_unit_has_last_known_position":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.HasLastKnownPosition) == expected;
            }

            case "cond_unit_can_see_hostile_unit":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.CanSeeHostileUnit) == expected;
            }

            case "cond_unit_has_detected_hostile_unit":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                bool detected = false;
                if (perception != null && perception.HasDetectedHostileUnit)
                {
                    // 若已知敌对目标处于濒死/死亡状态，视为未发现
                    UnitDefinitionRuntimeBinder hostile = perception.LastHostileTarget;
                    if (hostile != null)
                    {
                        UnitDeathController hd = hostile.GetComponent<UnitDeathController>()
                                              ?? hostile.GetComponentInParent<UnitDeathController>(true);
                        detected = (hd == null || !hd.IsDeadLike);
                    }
                    else
                    {
                        detected = true;
                    }
                }
                return detected == expected;
            }

            case "cond_unit_can_see_friendly_unit":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.CanSeeFriendlyUnit) == expected;
            }

            case "cond_unit_can_see_neutral_unit":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.CanSeeNeutralUnit) == expected;
            }

            case "cond_unit_can_see_creature_unit":
            {
                Transform subject = ResolveUnitSlot(sentence, "subject", transform);
                bool expected = GetBoolSlot(sentence, "expected", true);
                UnitPerceptionRuntime perception = GetPerceptionRuntime(subject);
                return (perception != null && perception.CanSeeCreatureUnit) == expected;
            }

            case "cond_unit_to_current_target_distance_compare":
            {
                if (currentTarget == null)
                    return false;

                float distance = GetFlatDistance(transform.position, currentTarget.position);
                LogicComparisonOperator op = GetComparisonSlot(sentence, "op", LogicComparisonOperator.Greater);
                float rhs = GetFloatSlot(sentence, "distance", 0f);
                return CompareFloat(distance, op, rhs);
            }

            case "cond_unit_behavior_type_is":
            {
                string expected = NormalizeBehaviorType(GetEnumSlot(sentence, "behaviorType", "Wander"));
                string current = NormalizeBehaviorType(behaviorType);
                return string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);
            }

            case "cond_unit_move_failed_by_stuck":
            {
                bool expected = GetBoolSlot(sentence, "expected", true);
                return lastMoveFailedByStuck == expected;
            }

            case "cond_unit_has_move_target":
            {
                bool expected = GetBoolSlot(sentence, "expected", true);
                return hasMoveTarget == expected;
            }

            case "cond_unit_to_unit_distance_compare":
            {
                Transform a = ResolveUnitSlot(sentence, "unitA", transform);
                Transform b = ResolveUnitSlot(sentence, "unitB", currentTarget != null ? currentTarget : GetPlayerTransform());

                if (a == null || b == null)
                    return false;

                float distance = GetFlatDistance(a.position, b.position);
                LogicComparisonOperator op = GetComparisonSlot(sentence, "op", LogicComparisonOperator.Greater);
                float rhs = GetFloatSlot(sentence, "distance", 0f);
                return CompareFloat(distance, op, rhs);
            }

            case "cond_unit_is_attacking":
            {
                bool expected = GetBoolSlot(sentence, "expected", true);
                bool isAttacking = actionController != null && actionController.IsAttacking;
                return isAttacking == expected;
            }

            case "cond_unit_can_attack":
            {
                // 当前不在攻击/硬直/死亡状态
                bool expected = GetBoolSlot(sentence, "expected", true);
                bool canAttack = actionController != null && actionController.CanEnterAttackPublic();
                return canAttack == expected;
            }

            case "cond_ready_to_attack_target":
            {
                // 复合条件：有当前目标 + 目标存活 + 进入有效攻击范围 + 可以发起攻击
                if (currentTarget == null) return false;
                // 目标已进入濒死/死亡状态，禁止攻击（_targetWasDeadLike 是快速路径，无需查组件）
                if (_targetWasDeadLike) return false;
                UnitDeathController targetDeath = currentTarget.GetComponentInParent<UnitDeathController>(true)
                                               ?? currentTarget.GetComponent<UnitDeathController>();
                if (targetDeath != null && targetDeath.IsDeadLike) return false;
                // 检测 Hitbox 球与目标 Hurtbox 是否真正有交集
                if (!IsHitboxReachingTarget(currentTarget)) return false;
                // 仍在主动移动中：等停下来再攻击，避免追击途中挥空
                if (hasMoveTarget) return false;
                if (actionController == null || !actionController.CanEnterAttackPublic()) return false;
                return true;
            }

            default:
                if (debugLogs)
                    Debug.Log($"[AIBehaviorPackageRuntimeController] {name}: condition not supported yet: {templateId}. Fallback true.", this);
                return true;
        }
    }


    private enum PerceptionTargetFlagMode
    {
        CanSee = 0,
        CanHear = 1,
        Detected = 2
    }

    private static string NormalizePerceptionTargetKind(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Player";

        string key = raw.Trim();

        switch (key)
        {
            case "玩家":
            case "Player":
            case "player":
                return "Player";

            case "当前目标":
            case "CurrentTarget":
            case "currentTarget":
            case "current_target":
                return "CurrentTarget";

            case "敌对单位":
            case "敌人":
            case "敌方":
            case "HostileUnit":
            case "EnemyUnit":
            case "Enemy":
                return "HostileUnit";

            case "友军":
            case "友方":
            case "FriendlyUnit":
            case "AllyUnit":
            case "Ally":
                return "FriendlyUnit";

            case "中立单位":
            case "中立":
            case "NeutralUnit":
            case "Neutral":
                return "NeutralUnit";

            case "生物":
            case "CreatureUnit":
            case "Creature":
                return "CreatureUnit";
        }

        return key;
    }

    private static bool EvaluatePerceptionTargetFlag(UnitPerceptionRuntime perception, string targetKind, PerceptionTargetFlagMode mode, Transform currentTarget)
    {
        if (perception == null)
            return false;

        string key = NormalizePerceptionTargetKind(targetKind);

        switch (key)
        {
            case "Player":
                if (mode == PerceptionTargetFlagMode.CanSee)
                    return perception.CanSeePlayer;
                if (mode == PerceptionTargetFlagMode.CanHear)
                    return perception.CanHearPlayer;
                return perception.HasDetectedPlayer;

            case "CurrentTarget":
            {
                UnitDefinitionRuntimeBinder targetBinder = currentTarget != null ? currentTarget.GetComponentInParent<UnitDefinitionRuntimeBinder>() : null;
                if (targetBinder == null)
                    return false;

                if (mode == PerceptionTargetFlagMode.CanSee)
                    return perception.LastVisionTarget == targetBinder;
                if (mode == PerceptionTargetFlagMode.CanHear)
                    return perception.LastHearingTarget == targetBinder;
                return perception.LastDetectedTarget == targetBinder;
            }

            case "HostileUnit":
            case "EnemyUnit":
                // 玩家走独立通道，从敌方 AI 视角玩家也是敌对目标
                if (mode == PerceptionTargetFlagMode.CanSee)
                    return perception.CanSeeHostileUnit || perception.CanSeePlayer;
                if (mode == PerceptionTargetFlagMode.CanHear)
                    return perception.CanHearHostileUnit || perception.CanHearPlayer;
                return perception.HasDetectedHostileUnit || perception.HasDetectedPlayer;

            case "FriendlyUnit":
            case "AllyUnit":
                if (mode == PerceptionTargetFlagMode.CanSee)
                    return perception.CanSeeFriendlyUnit;
                if (mode == PerceptionTargetFlagMode.CanHear)
                    return perception.CanHearFriendlyUnit;
                return perception.HasDetectedFriendlyUnit;

            case "NeutralUnit":
                if (mode == PerceptionTargetFlagMode.CanSee)
                    return perception.CanSeeNeutralUnit;
                if (mode == PerceptionTargetFlagMode.CanHear)
                    return perception.CanHearNeutralUnit;
                return perception.HasDetectedNeutralUnit;

            case "CreatureUnit":
                if (mode == PerceptionTargetFlagMode.CanSee)
                    return perception.CanSeeCreatureUnit;
                if (mode == PerceptionTargetFlagMode.CanHear)
                    return perception.CanHearCreatureUnit;
                return perception.HasDetectedCreatureUnit;
        }

        return false;
    }

    private bool ExecuteActionGroup(AIBehaviorPackage.AIRule rule)
    {
        IList actionItems = GetSentenceItems(rule?.sentenceRoot?.actionGroup);
        if (actionItems == null || actionItems.Count == 0)
            return false;

        bool executedAny = false;
        for (int i = 0; i < actionItems.Count; i++)
            executedAny |= ExecuteActionSentence(actionItems[i]);

        return executedAny;
    }

    private bool ExecuteActionSentence(object sentence)
    {
        string templateId = GetTemplateId(sentence);
        lastExecutedSentence = templateId;

        switch (templateId)
        {
            case "act_move_to_xy":
                return ExecuteMoveToXY(sentence, GetMoveRunHeld(sentence, "moveMode", false));

            case "act_move_unit_to_xy_with_move_mode":
                return ExecuteMoveToXY(sentence, GetMoveRunHeld(sentence, "moveMode", false));

            case "act_move_to_target":
                return ExecuteMoveToTarget(sentence, GetMoveRunHeld(sentence, "moveMode", false));

            case "act_move_unit_to_target_with_move_mode":
                return ExecuteMoveToTarget(sentence, GetMoveRunHeld(sentence, "moveMode", false));

            case "act_move_unit_random_in_radius":
                return ExecuteRandomWander(sentence);

            case "act_move_unit_to_last_heard_position":
                return ExecuteMoveToPerceptionPosition(sentence, PerceptionPositionKind.LastHeard, GetMoveRunHeld(sentence, "moveMode", false));

            case "act_move_unit_to_last_known_position":
                return ExecuteMoveToPerceptionPosition(sentence, PerceptionPositionKind.LastKnown, GetMoveRunHeld(sentence, "moveMode", false));

            case "act_move_unit_to_last_perception_position":
                return ExecuteMoveToPerceptionPosition(sentence, PerceptionPositionKind.LastPerception, GetMoveRunHeld(sentence, "moveMode", false));

            case "act_move_unit_away_from_target":
                return ExecuteMoveAwayFromTarget(sentence);

            case "act_move_unit_to_spawn_position":
            {
                // 目标刚濒死后的冷却期内：原地待机，不回出生点
                if (Time.time < _spawnReturnBlockedUntil) return false;
                string moveMode = GetEnumSlot(sentence, "moveMode", "行走");
                bool run = IsRunMoveMode(moveMode);
                SetMoveTarget(_spawnPosition, run);
                return true;
            }

            case "act_stop_unit_move":
                ClearCurrentAction();
                return true;

            case "act_clear_move_failure":
                lastMoveFailedByStuck = false;
                return true;

            case "act_set_current_target_to_player":
            {
                Transform pt = GetPlayerTransform();
                if (pt != null)
                {
                    UnitDeathController pd = pt.GetComponent<UnitDeathController>()
                                          ?? pt.GetComponentInParent<UnitDeathController>(true);
                    // pd == null 时也视为不可攻击（玩家对象存在但无死亡控制器，保守处理）
                    if (pd == null || pd.IsDeadLike) return false;
                }
                currentTarget = pt;
                return currentTarget != null;
            }

            case "act_set_current_target_to_last_detected_target":
                return ExecuteSetCurrentTargetFromPerception(sentence, PerceptionTargetKind.LastDetected);

            case "act_set_current_target_to_last_perceived_target":
                return ExecuteSetCurrentTargetFromPerception(sentence, PerceptionTargetKind.LastPerceived);

            case "act_set_current_target_to_last_hearing_target":
                return ExecuteSetCurrentTargetFromPerception(sentence, PerceptionTargetKind.LastHearing);

            case "act_set_current_target_to_last_hostile_target":
                return ExecuteSetCurrentTargetFromPerception(sentence, PerceptionTargetKind.LastHostile);

            case "act_set_current_target":
            {
                string kind = GetEnumSlot(sentence, "targetKind", "LastDetected");
                switch (kind)
                {
                    case "Player":
                    {
                        Transform pt = GetPlayerTransform();
                        if (pt != null) { UnitDeathController pd = pt.GetComponent<UnitDeathController>() ?? pt.GetComponentInParent<UnitDeathController>(true); if (pd != null && pd.IsDeadLike) return false; }
                        currentTarget = pt; return currentTarget != null;
                    }
                    case "LastDetected":  return ExecuteSetCurrentTargetFromPerception(sentence, PerceptionTargetKind.LastDetected);
                    case "LastPerceived": return ExecuteSetCurrentTargetFromPerception(sentence, PerceptionTargetKind.LastPerceived);
                    case "LastHearing":   return ExecuteSetCurrentTargetFromPerception(sentence, PerceptionTargetKind.LastHearing);
                    case "LastHostile":   return ExecuteSetCurrentTargetFromPerception(sentence, PerceptionTargetKind.LastHostile);
                    default:              return ExecuteSetCurrentTargetFromPerception(sentence, PerceptionTargetKind.LastDetected);
                }
            }

            case "act_clear_current_target":
                currentTarget = null;
                return true;

            case "act_basic_attack":
                return ExecuteBasicAttack(sentence);

            case "act_heavy_attack":
                return ExecuteHeavyAttack(sentence);

            case "act_set_behavior_type":
                behaviorType = NormalizeBehaviorType(GetEnumSlot(sentence, "behaviorType", behaviorType));
                return true;

            case "act_wait_seconds":
                return true;

            case "act_switch_ai_package":
                return ExecuteSwitchAIPackage(sentence);

            default:
                if (debugLogs)
                    Debug.Log($"[AIBehaviorPackageRuntimeController] {name}: action not supported yet: {templateId}", this);
                return false;
        }
    }

    private enum PerceptionPositionKind
    {
        LastHeard,
        LastKnown,
        LastPerception
    }

    private enum PerceptionTargetKind
    {
        LastDetected,
        LastPerceived,
        LastHearing,
        LastHostile
    }

    private bool ExecuteMoveToPerceptionPosition(object sentence, PerceptionPositionKind kind, bool runHeld)
    {
        Transform subject = ResolveUnitSlot(sentence, "subject", transform);
        UnitPerceptionRuntime perception = GetPerceptionRuntime(subject != null ? subject : transform);
        if (perception == null)
            return false;

        Vector3 target;
        switch (kind)
        {
            case PerceptionPositionKind.LastHeard:
                if (!perception.HasLastHeardPosition)
                    return false;
                target = perception.LastHeardPosition;
                break;

            case PerceptionPositionKind.LastKnown:
                if (!perception.HasLastKnownPosition)
                    return false;
                target = perception.LastKnownPosition;
                break;

            case PerceptionPositionKind.LastPerception:
                if (perception.HasLastHeardPosition && perception.LastHeardTime >= perception.LastSeenTime)
                    target = perception.LastHeardPosition;
                else if (perception.HasLastKnownPosition)
                    target = perception.LastKnownPosition;
                else if (perception.HasLastHeardPosition)
                    target = perception.LastHeardPosition;
                else
                    return false;
                break;

            default:
                return false;
        }

        if (keepCurrentY)
            target.y = transform.position.y;

        SetMoveTarget(target, runHeld);

        if (debugLogs)
            Debug.Log($"[AIBehaviorPackageRuntimeController] {name}: MoveToPerceptionPosition kind={kind}, target={target}, run={runHeld}", this);

        return true;
    }

    private bool ExecuteSetCurrentTargetFromPerception(object sentence, PerceptionTargetKind kind)
    {
        Transform subject = ResolveUnitSlot(sentence, "subject", transform);
        UnitPerceptionRuntime perception = GetPerceptionRuntime(subject != null ? subject : transform);
        if (perception == null)
            return false;

        UnitDefinitionRuntimeBinder targetBinder = null;
        switch (kind)
        {
            case PerceptionTargetKind.LastDetected:
                targetBinder = perception.LastDetectedTarget;
                break;
            case PerceptionTargetKind.LastPerceived:
                targetBinder = perception.LastPerceivedTarget != null ? perception.LastPerceivedTarget : perception.LastVisionTarget;
                break;
            case PerceptionTargetKind.LastHearing:
                targetBinder = perception.LastHearingTarget != null ? perception.LastHearingTarget : perception.LastHeardSource;
                break;
            case PerceptionTargetKind.LastHostile:
                targetBinder = perception.LastHostileTarget;
                break;
        }

        if (targetBinder != null)
        {
            UnitDeathController td = targetBinder.GetComponent<UnitDeathController>()
                                  ?? targetBinder.GetComponentInParent<UnitDeathController>(true);
            if (td != null && td.IsDeadLike)
                targetBinder = null;
        }

        currentTarget = targetBinder != null ? targetBinder.transform : null;

        if (debugLogs)
            Debug.Log($"[AIBehaviorPackageRuntimeController] {name}: SetCurrentTargetFromPerception kind={kind}, target={(currentTarget != null ? currentTarget.name : "null")}", this);

        return currentTarget != null;
    }

    private bool ExecuteMoveToXY(object sentence, bool runHeld)
    {
        float x = GetFloatSlot(sentence, "x", transform.position.x);
        float z = GetFloatSlot(sentence, "y", transform.position.z); // 模板里的 y 槽位在 2.5D 地图中代表 Z。
        float y = keepCurrentY ? transform.position.y : 0f;

        SetMoveTarget(new Vector3(x, y, z), runHeld);

        if (debugLogs)
            Debug.Log($"[AIBehaviorPackageRuntimeController] {name}: MoveToXY X={x}, Z={z}, run={runHeld}", this);

        return true;
    }

    private bool ExecuteMoveToTarget(object sentence, bool runHeld)
    {
        Transform target = ResolveUnitSlot(sentence, "target", currentTarget != null ? currentTarget : GetPlayerTransform());
        if (target == null)
            return false;

        Vector3 p = target.position;
        SetMoveTarget(new Vector3(p.x, keepCurrentY ? transform.position.y : p.y, p.z), runHeld);
        return true;
    }

    private bool ExecuteRandomWander(object sentence)
    {
        float radius = Mathf.Max(0.1f, GetFloatSlot(sentence, "radius", 5f));
        bool runHeld = GetMoveRunHeld(sentence, "moveMode", false);

        Vector2 random = UnityEngine.Random.insideUnitCircle;
        if (random.sqrMagnitude < 0.0001f)
            random = Vector2.right;

        Vector3 target = transform.position + new Vector3(random.x, 0f, random.y) * radius;
        if (keepCurrentY)
            target.y = transform.position.y;

        SetMoveTarget(target, runHeld);
        return true;
    }

    private bool ExecuteMoveAwayFromTarget(object sentence)
    {
        Transform target = ResolveUnitSlot(sentence, "target", currentTarget != null ? currentTarget : GetPlayerTransform());
        if (target == null)
            return false;

        float distance = Mathf.Max(0.1f, GetFloatSlot(sentence, "distance", 8f));
        bool runHeld = GetMoveRunHeld(sentence, "moveMode", true);

        Vector3 away = transform.position - target.position;
        away.y = 0f;

        if (away.sqrMagnitude < 0.0001f)
            away = -transform.forward;

        Vector3 moveTarget = transform.position + away.normalized * distance;
        if (keepCurrentY)
            moveTarget.y = transform.position.y;

        SetMoveTarget(moveTarget, runHeld);
        return true;
    }

    private bool ExecuteBasicAttack(object sentence)
    {
        if (actionModule == null)
        {
            Debug.LogWarning($"[AI Attack] {name}: actionModule is null", this);
            return false;
        }

        FaceCurrentTarget();
        ClearCurrentAction();

        SkillDefinition skill = actionModule.RequestLightAttack();
        if (skill == null)
            Debug.LogWarning($"[AI Attack] {name}: RequestLightAttack returned null. module={actionModule.CurrentModule}, canAttack={actionController?.CanEnterAttackPublic()}", this);
        return skill != null;
    }

    private bool ExecuteHeavyAttack(object sentence)
    {
        if (actionModule == null)
            return false;

        FaceCurrentTarget();
        ClearCurrentAction();

        SkillDefinition skill = actionModule.RequestHeavyAttack();
        return skill != null;
    }

    // 让 AI 单位面朝当前目标（通过 SetFacingOverride 注入朝向，SpineAnimationDriver 读取后翻转 Spine Scale）
    private void FaceCurrentTarget()
    {
        Transform facingTarget = currentTarget;
        if (facingTarget == null || movementController == null)
            return;

        float dx = facingTarget.position.x - transform.position.x;
        if (Mathf.Abs(dx) < 0.01f)
            return;

        movementController.SetFacingOverride(new Vector2(dx > 0f ? 1f : -1f, 0f));
    }

    private bool ExecuteSwitchAIPackage(object sentence)
    {
        object assignment = FindSlotAssignment(sentence, "package");
        object value = GetMemberValue(assignment, "value");
        UnityEngine.Object asset = GetMemberValue(value, "assetReference") as UnityEngine.Object;
        AIBehaviorPackage nextPackage = asset as AIBehaviorPackage;

        if (nextPackage == null)
            return false;

        SetBehaviorPackage(nextPackage);
        return true;
    }

    private void SetMoveTarget(Vector3 target, bool runHeld)
    {
        bool wasMoving = hasMoveTarget;

        currentMoveTarget = target;
        currentMoveRunHeld = runHeld;
        hasMoveTarget = true;

        if (!wasMoving)
            ResetStuckTracking(clearFailureFlag: true);

        if (movementController == null)
            AutoSetup();

        // 关键：AI 包的“移动方式”必须落到 UnitMovementController 的外部输入口。
        // 不要只存成 AI 自己的状态，否则移动脚本仍可能保持 Walk。
        if (movementController != null)
            movementController.SetExternalMoveInput(Vector2.zero, runHeld, false);
    }

    private void TickMoveTarget()
    {
        if (!hasMoveTarget)
            return;

        if (movementController == null)
            AutoSetup();

        if (movementController == null)
            return;

        // HitStun / Attack / Dead 期间停止输入，让 ImmediateStop 生效
        if (actionController != null && actionController.IsHardLocked)
        {
            movementController.SetExternalMoveInput(Vector2.zero, false, false);
            return;
        }

        // 追击活体目标时实时跟随其 Transform，不依赖 0.15s 一次的快照坐标
        if (currentTarget != null)
        {
            UnitDeathController td = currentTarget.GetComponentInParent<UnitDeathController>(true)
                                  ?? currentTarget.GetComponent<UnitDeathController>();
            if (td == null || !td.IsDeadLike)
            {
                currentMoveTarget = currentTarget.position;

                // Hitbox 已能碰到目标 Hurtbox：停止移动，让攻击规则接管
                if (IsHitboxReachingTarget(currentTarget))
                {
                    FinishMoveTarget();
                    return;
                }
            }
        }

        Vector3 current = transform.position;
        Vector2 delta = new Vector2(currentMoveTarget.x - current.x, currentMoveTarget.z - current.z);
        float distance = delta.magnitude;

        if (distance <= arriveDistance)
        {
            FinishMoveTarget();
            return;
        }

        if (TickStuckProtection(current, distance))
            return;

        Vector2 input = delta / Mathf.Max(distance, 0.0001f);

        // 分离避让：只在非追击状态（徘徊/巡逻）时生效，追击时不干扰路径。
        // 对全体 AI 的 O(n) 扫描按 SeparationRecalcInterval 限频，不是每帧都重算——
        // 巡逻单位密集时这段之前是每帧 O(n²)，直接导致那片区域帧率骤降。
        if (separationRadius > 0f && currentTarget == null && hasMoveTarget)
        {
            if (Time.time >= _nextSeparationRecalcTime)
            {
                _nextSeparationRecalcTime = Time.time + SeparationRecalcInterval;

                Vector2 sep = Vector2.zero;
                Vector3 myPos = transform.position;
                for (int s = 0; s < _allAI.Count; s++)
                {
                    AIBehaviorPackageRuntimeController other = _allAI[s];
                    if (other == null || other == this) continue;
                    if (UnitCombatHitbox.AreHostile(gameObject, other.gameObject)) continue;
                    Vector3 diff = myPos - other.transform.position;
                    float dist2D = Mathf.Sqrt(diff.x * diff.x + diff.z * diff.z);
                    if (dist2D < 0.001f || dist2D >= separationRadius) continue;
                    float strength = 1f - (dist2D / separationRadius);
                    sep += new Vector2(diff.x, diff.z).normalized * strength;
                }
                _cachedSeparation = sep;
            }

            if (_cachedSeparation.sqrMagnitude > 0f)
            {
                // 无目标时（回出生点/巡逻）用更强的分离力，把密集叠堆的单位推散
                float effectiveSepWeight = separationWeight * 2.5f;
                input = (input + _cachedSeparation * effectiveSepWeight).normalized;
            }
        }

        movementController.SetExternalMoveInput(input, currentMoveRunHeld, false);
    }

    private bool TickStuckProtection(Vector3 currentPosition, float distanceToMoveTarget)
    {
        if (!stopMoveWhenStuck)
            return false;

        if (!hasMoveTarget)
            return false;

        if (distanceToMoveTarget <= Mathf.Max(arriveDistance * 1.5f, arriveDistance + 0.02f))
            return false;

        float interval = Mathf.Max(0.05f, stuckCheckInterval);

        if (stuckLastCheckTime <= 0f)
        {
            stuckLastCheckTime = Time.time;
            stuckLastCheckPosition = currentPosition;
            return false;
        }

        float elapsed = Time.time - stuckLastCheckTime;
        if (elapsed < interval)
            return false;

        float moved = GetFlatDistance(currentPosition, stuckLastCheckPosition);
        float minMoveDistance = Mathf.Max(0.001f, stuckMinMoveDistance);

        if (moved < minMoveDistance)
        {
            stuckAccumulatedTime += elapsed;

            // 无追击目标时（回出生点 / 巡逻）容忍时间延长到 4 倍，避免单位互相挤压时误判卡住
            float effectiveStopDelay = currentTarget == null ? stuckStopDelay * 4f : stuckStopDelay;
            if (stuckAccumulatedTime >= Mathf.Max(interval, effectiveStopDelay))
            {
                FailMoveTargetByStuck();
                return true;
            }
        }
        else
        {
            stuckAccumulatedTime = 0f;
        }

        stuckLastCheckTime = Time.time;
        stuckLastCheckPosition = currentPosition;
        return false;
    }

    private void FailMoveTargetByStuck()
    {
        lastMoveFailedByStuck = true;

        if (clearMoveTargetWhenStuck)
            hasMoveTarget = false;

        currentMoveRunHeld = false;
        ResetStuckTracking(clearFailureFlag: false);

        if (movementController != null)
            movementController.StopImmediately();

        if (debugLogs)
            Debug.Log($"[AIBehaviorPackageRuntimeController] {name}: move target failed by stuck / obstacle. target={currentMoveTarget}", this);
    }

    // 玩家濒死时由 PlayerDeathDecisionController 统一调用，清除所有敌人的感知记忆
    public void ClearPerceptionOnPlayerDeath()
    {
        GetComponent<UnitPerceptionRuntime>()?.ClearTransientState();
    }

    private void FinishMoveTarget()
    {
        hasMoveTarget = false;
        _smoothedMoveInput = Vector2.zero;
        ResetStuckTracking(clearFailureFlag: true);

        if (movementController != null)
            movementController.StopImmediately();

        if (snapToTargetOnArrive)
        {
            Vector3 p = transform.position;
            transform.position = new Vector3(currentMoveTarget.x, p.y, currentMoveTarget.z);
        }
    }

    private void ResetStuckTracking(bool clearFailureFlag)
    {
        stuckLastCheckPosition = transform.position;
        stuckLastCheckTime = Time.time;
        stuckAccumulatedTime = 0f;

        if (clearFailureFlag)
            lastMoveFailedByStuck = false;
    }


    private static UnitPerceptionRuntime GetPerceptionRuntime(Transform unit)
    {
        if (unit == null)
            return null;

        UnitPerceptionRuntime runtime = unit.GetComponent<UnitPerceptionRuntime>();
        if (runtime != null)
            return runtime;

        runtime = unit.GetComponentInParent<UnitPerceptionRuntime>();
        if (runtime != null)
            return runtime;

        return unit.GetComponentInChildren<UnitPerceptionRuntime>(true);
    }

    private bool CanSeeTransform(Transform target, float range)
    {
        return CanSeeTransform(transform, target, range);
    }

    private bool CanSeeTransform(Transform viewer, Transform target, float range)
    {
        if (viewer == null || target == null)
            return false;

        SkyPrisonVisionManager manager = SkyPrisonVisionManager.Instance;
        if (manager != null)
        {
            bool hasViewerBinder = viewer.GetComponentInParent<UnitDefinitionRuntimeBinder>() != null;
            bool hasTargetBinder = target.GetComponentInParent<UnitDefinitionRuntimeBinder>() != null;

            if (hasViewerBinder && hasTargetBinder)
                return manager.CanTransformSeeTransform(viewer, target);
        }

        return GetFlatDistance(viewer.position, target.position) <= Mathf.Max(0f, range);
    }

    private Transform GetPlayerTransform()
    {
        if (!autoFindPlayerByTag || string.IsNullOrWhiteSpace(playerTag))
            return null;

        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        return player != null ? player.transform : null;
    }

    // 检测下一次轻攻击的 Hitbox 是否在 XZ 平面上能覆盖到目标 Hurtbox（2.5D 专用，容忍小的 Y 差）
    // 判断攻击臂展是否覆盖到目标（XZ 平面，仅用攻击者自身臂展，不叠加目标体积）
    //
    // 之前完全不检查 Y 轴——只要水平距离够近就放行攻击，敌人从高处（比如摔落、站在
    // 明显更高的台子上）够不着玩家的情况下依然会一直挥空。这里加一层高度差上限，
    // 超过这个范围直接判定"够不着"，不影响正常地面小起伏（原本设计就是给这种场景
    // 用的，容忍量按角色身高给一个合理值）。
    private const float MaxAttackHeightDifference = 2.2f;

    // 实际命中判定用的是挂在手骨骼上、跟着骨骼实时移动的小胶囊体（半径写死 0.55，
    // 见 UnitDefinitionRuntimeApplier.EnsureSpineBoundingBoxHitbox），跟这里 skill.hitbox
    // 里配置的 offset/radius（很多技能这块只是没人调过的占位值，比如空手挥拳是 offset=0/
    // radius=1）完全是两套数字——用后者算出来的"够不够得着"，经常和手骨骼实际扫到的
    // 范围对不上，导致角色站着不动、贴身站在敌人面前，AI 却因为这个占位数字判断"还没
    // 到位"或者反过来"提前出手"，出现连续挥空。
    // 改成用双方 UnitDefinition.collisionRadius（身体物理碰撞半径，已经在用、真实反映
    // 体型）算出"贴到物理接触距离"，跟 skill 那个估算取较大值兜底——保证至少贴到真实
    // 贴脸距离才会放行攻击，不会被一个没调过的占位数字卡住，攻击更容易覆盖住动画里
    // 挥拳的实际轨迹。
    private const float MeleeContactMargin = 0.15f;

    private float ResolveCollisionRadius(Transform t)
    {
        UnitDefinitionRuntimeBinder binder = t.GetComponentInParent<UnitDefinitionRuntimeBinder>();
        if (binder != null && binder.UnitDefinitionAsset != null && binder.UnitDefinitionAsset.overrideCollisionShape)
            return Mathf.Max(0.1f, binder.UnitDefinitionAsset.collisionRadius);
        return 0.5f;
    }

    private bool IsHitboxReachingTarget(Transform target)
    {
        if (target == null || actionModule == null) return false;

        if (Mathf.Abs(transform.position.y - target.position.y) > MaxAttackHeightDifference)
            return false;

        SkillDefinition skill = actionModule.GetNextLightAttackSkill();
        float skillReach = skill != null && skill.hitbox != null
            ? Mathf.Abs(skill.hitbox.offset.x) + skill.hitbox.radius
            : 1.5f;

        float contactReach = ResolveCollisionRadius(transform) + ResolveCollisionRadius(target) + MeleeContactMargin;
        float reach = Mathf.Max(skillReach, contactReach);

        return GetFlatDistance(transform.position, target.position) <= reach;
    }

    private static float GetFlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static bool CompareFloat(float left, LogicComparisonOperator op, float right)
    {
        switch (op)
        {
            case LogicComparisonOperator.Greater: return left > right;
            case LogicComparisonOperator.Less: return left < right;
            case LogicComparisonOperator.Equal: return Mathf.Approximately(left, right);
            case LogicComparisonOperator.NotEqual: return !Mathf.Approximately(left, right);
            case LogicComparisonOperator.GreaterOrEqual: return left > right || Mathf.Approximately(left, right);
            case LogicComparisonOperator.LessOrEqual: return left < right || Mathf.Approximately(left, right);
            case LogicComparisonOperator.StrictEqual: return Mathf.Approximately(left, right);
            default: return false;
        }
    }

    // 下面几个 Get*Slot 系列函数是 AI 判定树每次 tick（默认 50Hz/单位）读条件/槽位值的热路径。
    // 反射版本除了 Type.GetField/GetProperty 查找贵之外，更大的开销是 field.GetValue() 对
    // bool/float/int/enum 这些值类型的返回值必然装箱（object 装箱是堆分配）——这是 F10 报告里
    // "堆峰值很小但GC次数极高"的真实成因，光缓存 MemberInfo（见 GetMemberValue）治标不治本。
    // sentence/value/assignment 在这套系统里运行时永远是 LogicSentenceInstance/LogicSlotValue/
    // LogicSlotAssignment（TriggerPackageRuntime.cs 已经用完全强类型的方式读同一份数据，证明
    // 这个假设成立）——这里加一条强类型快速路径，直接读字段，不经过反射、不装箱；原来的反射版本
    // 保留在下面当兜底（理论上永远走不到，纯粹图安全，行为不变）。
    private static IList GetSentenceItems(object group)
    {
        if (group == null)
            return null;

        if (group is AIBehaviorPackage.ConditionSentenceGroup cGroup) return cGroup.items;
        if (group is AIBehaviorPackage.MotiveSentenceGroup mGroup) return mGroup.items;
        if (group is AIBehaviorPackage.ActionSentenceGroup aGroup) return aGroup.items;

        object items = GetMemberValue(group, "items");
        return items as IList;
    }

    private static string GetTemplateId(object sentence)
    {
        if (sentence == null)
            return "";

        if (sentence is LogicSentenceInstance typedSentence)
            return typedSentence.templateId ?? "";

        object value = GetMemberValue(sentence, "templateId");
        return value as string ?? "";
    }

    private static bool GetBoolSlot(object sentence, string slotId, bool fallback)
    {
        object value = GetSlotValue(sentence, slotId);
        if (value == null)
            return fallback;

        if (value is LogicSlotValue typedValue)
            return typedValue.boolValue;

        object boolValue = GetMemberValue(value, "boolValue");
        if (boolValue is bool b)
            return b;

        object stringValue = GetMemberValue(value, "stringValue");
        if (stringValue is string s && bool.TryParse(s, out bool parsed))
            return parsed;

        return fallback;
    }

    private static float GetFloatSlot(object sentence, string slotId, float fallback)
    {
        object value = GetSlotValue(sentence, slotId);
        if (value == null)
            return fallback;

        if (value is LogicSlotValue typedValue)
            return typedValue.EvaluateFloat();

        // 随机范围：sourceType == RandomRange 时取 [floatValue, floatValueMax]
        object sourceType = GetMemberValue(value, "sourceType");
        bool isRandom = sourceType is LogicValueSourceType lst && lst == LogicValueSourceType.RandomRange;
        if (isRandom)
        {
            object minObj = GetMemberValue(value, "floatValue");
            object maxObj = GetMemberValue(value, "floatValueMax");
            float min = minObj is float fMin ? fMin : 0f;
            float max = maxObj is float fMax ? fMax : min;
            return UnityEngine.Random.Range(Mathf.Min(min, max), Mathf.Max(min, max));
        }

        object floatValue = GetMemberValue(value, "floatValue");
        if (floatValue is float f)
            return f;

        object intValue = GetMemberValue(value, "intValue");
        if (intValue is int i)
            return i;

        object stringValue = GetMemberValue(value, "stringValue");
        if (stringValue is string s && float.TryParse(s, out float parsed))
            return parsed;

        return fallback;
    }

    private static string NormalizeBehaviorType(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Wander";

        string s = raw.Trim();

        switch (s)
        {
            case "待机":
            case "Idle":
            case "idle":
                return "Idle";

            case "怀疑":
            case "Suspicious":
            case "suspicious":
                return "Suspicious";

            case "警戒":
            case "Alert":
            case "alert":
                return "Alert";

            case "发现":
            case "Detected":
            case "detected":
                return "Detected";

            case "徘徊":
            case "Wander":
            case "wander":
                return "Wander";

            case "逃跑":
            case "Flee":
            case "flee":
                return "Flee";

            case "停止":
            case "Stop":
            case "stop":
                return "Stop";

            case "追击":
            case "Chase":
            case "chase":
                return "Chase";

            case "攻击":
            case "Attack":
            case "attack":
                return "Attack";

            case "返回":
            case "Return":
            case "return":
                return "Return";
        }

        return s;
    }

    private static string GetEnumSlot(object sentence, string slotId, string fallback)
    {
        object value = GetSlotValue(sentence, slotId);
        if (value == null)
            return fallback;

        if (value is LogicSlotValue typedValue)
        {
            if (!string.IsNullOrWhiteSpace(typedValue.enumValue))
                return typedValue.enumValue;
            if (!string.IsNullOrWhiteSpace(typedValue.stringValue))
                return typedValue.stringValue;
            return fallback;
        }

        object enumValue = GetMemberValue(value, "enumValue");
        if (enumValue is string e && !string.IsNullOrWhiteSpace(e))
            return e;

        object stringValue = GetMemberValue(value, "stringValue");
        if (stringValue is string s && !string.IsNullOrWhiteSpace(s))
            return s;

        return fallback;
    }

    private static LogicComparisonOperator GetComparisonSlot(object sentence, string slotId, LogicComparisonOperator fallback)
    {
        object value = GetSlotValue(sentence, slotId);
        if (value == null)
            return fallback;

        if (value is LogicSlotValue typedValue)
        {
            if (typedValue.enumValue != null && Enum.TryParse(typedValue.enumValue, out LogicComparisonOperator typedParsedEnum))
                return typedParsedEnum;
            if (Enum.IsDefined(typeof(LogicComparisonOperator), typedValue.intValue))
                return (LogicComparisonOperator)typedValue.intValue;
            if (typedValue.stringValue != null && Enum.TryParse(typedValue.stringValue, out LogicComparisonOperator typedParsedString))
                return typedParsedString;
            return fallback;
        }

        object enumValue = GetMemberValue(value, "enumValue");
        if (enumValue is string enumText && Enum.TryParse(enumText, out LogicComparisonOperator parsedEnum))
            return parsedEnum;

        object intValue = GetMemberValue(value, "intValue");
        if (intValue is int i && Enum.IsDefined(typeof(LogicComparisonOperator), i))
            return (LogicComparisonOperator)i;

        object stringValue = GetMemberValue(value, "stringValue");
        if (stringValue is string s && Enum.TryParse(s, out LogicComparisonOperator parsedString))
            return parsedString;

        return fallback;
    }

    private bool GetMoveRunHeld(object sentence, string slotId, bool fallback)
    {
        // AI 编辑器历史上可能出现过 moveMode / movementMode / speedMode 等不同槽名。
        // 这里不要只认一个 slotId，否则“AI 包里显示是奔跑”，运行时却会按 Walk。
        string raw;
        bool result;

        result = TryReadMoveRunHeldFromSlot(sentence, slotId, out raw);
        if (result)
        {
            currentMoveModeDebug = raw;
            return true;
        }

        // 指定槽位存在且明确是 Walk / Sneak 时，要压住 fallback。
        if (TryReadMoveModeTextFromSlot(sentence, slotId, out raw) && IsExplicitNonRunMoveMode(raw))
        {
            currentMoveModeDebug = raw;
            return false;
        }

        string[] candidateSlots =
        {
            "moveMode", "movementMode", "moveType", "movementType",
            "speedMode", "speedType", "gait", "runMode",
            "runHeld", "isRun", "shouldRun", "useRun",
            "移动方式", "移动模式", "速度模式"
        };

        for (int i = 0; i < candidateSlots.Length; i++)
        {
            string candidate = candidateSlots[i];
            if (string.Equals(candidate, slotId, StringComparison.Ordinal))
                continue;

            if (TryReadMoveRunHeldFromSlot(sentence, candidate, out raw))
            {
                currentMoveModeDebug = raw;
                return true;
            }

            if (TryReadMoveModeTextFromSlot(sentence, candidate, out raw) && IsExplicitNonRunMoveMode(raw))
            {
                currentMoveModeDebug = raw;
                return false;
            }
        }

        // 最后一层保险：扫描全部槽位的值。只看值，不用 slotId 猜，避免 random 这种槽名误判为 run。
        if (TryScanAllSlotsForRunMode(sentence, out raw))
        {
            currentMoveModeDebug = raw;
            return true;
        }

        currentMoveModeDebug = fallback ? "fallback:Run" : "fallback:Walk";
        return fallback;
    }

    private static bool TryReadMoveRunHeldFromSlot(object sentence, string slotId, out string raw)
    {
        raw = "";

        object value = GetSlotValue(sentence, slotId);
        if (value == null)
            return false;

        if (value is LogicSlotValue typedValue)
        {
            raw = $"{slotId}:{typedValue.boolValue}";
            return typedValue.boolValue;
        }

        object boolValue = GetMemberValue(value, "boolValue");
        if (boolValue is bool b)
        {
            raw = $"{slotId}:{b}";
            return b;
        }

        raw = ReadSlotValueText(value);
        return IsRunMoveMode(raw);
    }

    private static bool TryReadMoveModeTextFromSlot(object sentence, string slotId, out string raw)
    {
        raw = "";

        object value = GetSlotValue(sentence, slotId);
        if (value == null)
            return false;

        raw = ReadSlotValueText(value);
        return !string.IsNullOrWhiteSpace(raw);
    }

    private static bool TryScanAllSlotsForRunMode(object sentence, out string raw)
    {
        raw = "";

        if (sentence == null)
            return false;

        if (sentence is LogicSentenceInstance typedSentence)
        {
            List<LogicSlotAssignment> typedAssignments = typedSentence.slotAssignments;
            if (typedAssignments == null)
                return false;

            for (int i = 0; i < typedAssignments.Count; i++)
            {
                LogicSlotAssignment typedAssignment = typedAssignments[i];
                if (typedAssignment == null)
                    continue;

                string typedText = ReadSlotValueText(typedAssignment.value);
                if (IsRunMoveMode(typedText))
                {
                    raw = $"{typedAssignment.slotId ?? "slot"}:{typedText}";
                    return true;
                }
            }

            return false;
        }

        object assignmentsObj = GetMemberValue(sentence, "slotAssignments");
        IList assignments = assignmentsObj as IList;
        if (assignments == null)
            return false;

        for (int i = 0; i < assignments.Count; i++)
        {
            object assignment = assignments[i];
            object value = GetMemberValue(assignment, "value");
            string text = ReadSlotValueText(value);

            if (IsRunMoveMode(text))
            {
                string slot = GetMemberValue(assignment, "slotId") as string ?? "slot";
                raw = $"{slot}:{text}";
                return true;
            }
        }

        return false;
    }

    private static string ReadSlotValueText(object value)
    {
        if (value == null)
            return "";

        if (value is LogicSlotValue typedValue)
        {
            if (!string.IsNullOrWhiteSpace(typedValue.enumValue))
                return typedValue.enumValue;
            if (!string.IsNullOrWhiteSpace(typedValue.stringValue))
                return typedValue.stringValue;
            return typedValue.intValue.ToString();
        }

        object enumValue = GetMemberValue(value, "enumValue");
        if (enumValue is string e && !string.IsNullOrWhiteSpace(e))
            return e;

        object stringValue = GetMemberValue(value, "stringValue");
        if (stringValue is string s && !string.IsNullOrWhiteSpace(s))
            return s;

        object displayName = GetMemberValue(value, "displayName");
        if (displayName is string d && !string.IsNullOrWhiteSpace(d))
            return d;

        object name = GetMemberValue(value, "name");
        if (name is string n && !string.IsNullOrWhiteSpace(n))
            return n;

        object intValue = GetMemberValue(value, "intValue");
        if (intValue is int i)
            return i.ToString();

        object boolValue = GetMemberValue(value, "boolValue");
        if (boolValue is bool b)
            return b.ToString();

        return "";
    }

    private static bool IsRunMoveMode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string s = raw.Trim();

        return s.Equals("Run", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Sprint", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Dash", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Fast", StringComparison.OrdinalIgnoreCase)
            || s.Equals("跑步", StringComparison.OrdinalIgnoreCase)
            || s.Equals("奔跑", StringComparison.OrdinalIgnoreCase)
            || s.Equals("冲刺", StringComparison.OrdinalIgnoreCase)
            || s.Equals("疾跑", StringComparison.OrdinalIgnoreCase)
            || s.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("sprint", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("dash", StringComparison.OrdinalIgnoreCase) >= 0
            || s.Contains("奔跑")
            || s.Contains("跑步")
            || s.Contains("冲刺")
            || s.Contains("疾跑");
    }

    private static bool IsExplicitNonRunMoveMode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string s = raw.Trim();

        return s.Equals("Walk", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Move", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Sneak", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Crouch", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Slow", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Idle", StringComparison.OrdinalIgnoreCase)
            || s.Equals("False", StringComparison.OrdinalIgnoreCase)
            || s.Equals("行走", StringComparison.OrdinalIgnoreCase)
            || s.Equals("移动", StringComparison.OrdinalIgnoreCase)
            || s.Equals("潜行", StringComparison.OrdinalIgnoreCase)
            || s.Equals("慢走", StringComparison.OrdinalIgnoreCase);
    }

    private Transform ResolveUnitSlot(object sentence, string slotId, Transform fallback)
    {
        object value = GetSlotValue(sentence, slotId);
        if (value == null)
            return fallback;

        if (value is LogicSlotValue typedValue)
        {
            if (typedValue.assetReference is GameObject typedGo)
                return typedGo.transform;
            if (typedValue.assetReference is Component typedComponent)
                return typedComponent.transform;

            Transform typedContextResolved = ResolveContextUnit(typedValue.contextKey);
            if (typedContextResolved != null)
                return typedContextResolved;

            Transform typedSceneResolved = ResolveSceneObject(typedValue.sceneObjectId);
            if (typedSceneResolved != null)
                return typedSceneResolved;

            Transform typedStringResolved = ResolveContextUnit(typedValue.stringValue);
            if (typedStringResolved != null)
                return typedStringResolved;

            return fallback;
        }

        UnityEngine.Object assetReference = GetMemberValue(value, "assetReference") as UnityEngine.Object;
        if (assetReference is GameObject go)
            return go.transform;

        if (assetReference is Component component)
            return component.transform;

        string contextKey = GetMemberValue(value, "contextKey") as string;
        Transform contextResolved = ResolveContextUnit(contextKey);
        if (contextResolved != null)
            return contextResolved;

        string sceneObjectId = GetMemberValue(value, "sceneObjectId") as string;
        Transform sceneResolved = ResolveSceneObject(sceneObjectId);
        if (sceneResolved != null)
            return sceneResolved;

        string stringValue = GetMemberValue(value, "stringValue") as string;
        Transform stringResolved = ResolveContextUnit(stringValue);
        if (stringResolved != null)
            return stringResolved;

        return fallback;
    }

    private Transform ResolveContextUnit(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        switch (key.Trim())
        {
            case "Self":
            case "self":
            case "ThisUnit":
            case "this_unit":
            case "该单位":
                return transform;

            case "Player":
            case "player":
            case "玩家":
                return GetPlayerTransform();

            case "CurrentTarget":
            case "currentTarget":
            case "current_target":
            case "当前目标":
                return currentTarget;
        }

        return ResolveByNameCached(key);
    }

    // GameObject.Find 是全场景名字扫描，之前这两处（ResolveContextUnit / ResolveSceneObject）
    // 每次逻辑句求值都重新扫一遍，没有任何缓存。正式游戏近百只怪、场景对象规模上去之后，
    // 这种"每次AI判定都全场景找名字"的代价会随场景规模线性变差。行为包里引用的场景物体
    // 名字在一局里基本是固定的，缓存下来复用；缓存到的对象被销毁了（cached != null 检查
    // 不通过）就重新找一次，不会缓存出脏引用。
    private static readonly Dictionary<string, Transform> _sceneObjectByNameCache = new Dictionary<string, Transform>();

    private static Transform ResolveByNameCached(string name)
    {
        if (_sceneObjectByNameCache.TryGetValue(name, out Transform cached) && cached != null)
            return cached;

        GameObject byName = GameObject.Find(name);
        Transform result = byName != null ? byName.transform : null;
        _sceneObjectByNameCache[name] = result;
        return result;
    }

    private static Transform ResolveSceneObject(string sceneObjectId)
    {
        if (string.IsNullOrWhiteSpace(sceneObjectId))
            return null;

        return ResolveByNameCached(sceneObjectId);
    }

    private static object GetSlotValue(object sentence, string slotId)
    {
        object assignment = FindSlotAssignment(sentence, slotId);
        if (assignment == null)
            return null;

        if (assignment is LogicSlotAssignment typedAssignment)
            return typedAssignment.value;

        return GetMemberValue(assignment, "value");
    }

    private static object FindSlotAssignment(object sentence, string slotId)
    {
        if (sentence == null)
            return null;

        // LogicSentenceInstance.GetAssignment 是同一份数据本来就有的强类型方法，直接复用，
        // 不用反射再扫一遍 slotAssignments。
        if (sentence is LogicSentenceInstance typedSentence)
            return typedSentence.GetAssignment(slotId);

        object assignmentsObj = GetMemberValue(sentence, "slotAssignments");
        IList assignments = assignmentsObj as IList;
        if (assignments == null)
            return null;

        for (int i = 0; i < assignments.Count; i++)
        {
            object assignment = assignments[i];
            string currentSlotId = GetMemberValue(assignment, "slotId") as string;
            if (currentSlotId == slotId)
                return assignment;
        }

        return null;
    }

    // GetMemberValue 被 AI 判定树的几乎每一次条件/槽位读取调用（每单位每次 tick 可能几十次，
    // tick 间隔默认 0.02s = 50Hz），Type.GetField/GetProperty 是最重的反射调用之一，没加
    // DeclaredOnly 还要沿整条继承链查——这是 F10 报告里"每秒53次GC"的头号嫌疑。这里不改动
    // 任何返回值/调用方逻辑（照样是反射取值、照样装箱返回 object），只是把"查哪个成员"这一步
    // 按 (类型, 成员名) 缓存下来，避免每次调用都重新扫一遍类型元数据。这套 (类型,成员名) 组合
    // 数量很小（就这几个 Logic* 类型的固定字段名），缓存几次 tick 内就会填满，之后几乎零反射
    // 查找开销。
    private static readonly Dictionary<(Type, string), MemberInfo> memberLookupCache = new Dictionary<(Type, string), MemberInfo>();

    private static object GetMemberValue(object target, string name)
    {
        if (target == null || string.IsNullOrWhiteSpace(name))
            return null;

        Type type = target.GetType();
        (Type, string) key = (type, name);

        if (!memberLookupCache.TryGetValue(key, out MemberInfo member))
        {
            member = (MemberInfo)type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     ?? type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            memberLookupCache[key] = member;
        }

        if (member is FieldInfo field)
            return field.GetValue(target);

        if (member is PropertyInfo property && property.CanRead)
            return property.GetValue(target, null);

        return null;
    }
}
