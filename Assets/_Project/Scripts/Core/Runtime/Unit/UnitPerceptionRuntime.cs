using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// V4 - 2026-05-29: Closes hearing loop. Footstep logical noise can update hearing target identity/category memory.
// Common unit perception state layer. It stores perception results only.
// AI behavior decides what to do with Suspicious / Alert / Detected and target category.

public enum UnitPerceptionLevel
{
    None = 0,
    Suspicious = 1,
    Alert = 2,
    Detected = 3
}

public enum UnitPerceptionTargetCategory
{
    Unknown = 0,
    Player = 1,
    Friendly = 2,
    Hostile = 3,
    NeutralPassive = 4,
    NeutralHostile = 5,
    Creature = 6,
}

[DisallowMultipleComponent]
public sealed class UnitPerceptionRuntime : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UnitDefinitionRuntimeBinder runtimeBinder;
    [SerializeField] private UnitDefinition unitDefinition;

    [Header("Perception Capability")]
    [SerializeField] private bool canUseVision = true;
    [SerializeField] private bool canUseHearing = true;
    [SerializeField] private bool canBePerceived = true;

    [Header("Vision")]
    [Tooltip("视觉感知倍率。1.0 为普通视觉。")]
    [SerializeField] private float visionMultiplier = 1f;

    [Tooltip("视觉强度达到该比例时进入怀疑。默认 0.25 = 25%。")]
    [Range(0f, 1f)]
    [SerializeField] private float visionSuspicionThreshold = 0.25f;

    [Tooltip("视觉强度达到该比例并持续指定时间后进入警戒。默认 0.50 = 50%。")]
    [Range(0f, 1f)]
    [SerializeField] private float visionAlertThreshold = 0.5f;

    [Tooltip("视觉强度达到该比例并持续指定时间后真正发现。默认 0.80 = 80%。")]
    [Range(0f, 1f)]
    [SerializeField] private float visionDetectThreshold = 0.8f;

    [Tooltip("视觉强度超过警戒阈值后，需要持续多久才进入警戒。")]
    [SerializeField] private float visionAlertHoldSeconds = 0.15f;

    [Tooltip("视觉强度超过发现阈值后，需要持续多久才真正发现。")]
    [SerializeField] private float visionDetectHoldSeconds = 0.35f;

    [Tooltip("视觉记忆秒数。目标消失后，怀疑/警戒/发现状态保留多久。")]
    [SerializeField] private float visionMemorySeconds = 3f;

    [Header("Hearing")]
    [Tooltip("1.0 = 普通听力；20.0 = 顺风耳；0.0 = 完全不使用听觉。")]
    [SerializeField] private float hearingMultiplier = 1f;
    [SerializeField] private float hearingBaseRange = 10f;
    [SerializeField] private float hearingMaxRange = 30f;
    [SerializeField] private float hearingSuspicionThreshold = 0.005f;
    [SerializeField] private float hearingAlertThreshold = 0.015f;
    [SerializeField] private float hearingDetectThreshold = 0.04f;
    [SerializeField] private float hearingMemorySeconds = 3f;

    [Header("Runtime State")]
    [SerializeField] private UnitPerceptionLevel currentLevel = UnitPerceptionLevel.None;
    [SerializeField] private bool isSuspicious;
    [SerializeField] private bool isAlert;
    [SerializeField] private bool hasDetectedTarget;
    [SerializeField] private bool hasDetectedPlayer;
    [SerializeField] private bool canSeePlayer;
    [SerializeField] private bool canHearPlayer;
    [SerializeField] private bool hasLastKnownPosition;
    [SerializeField] private bool hasLastHeardPosition;
    [SerializeField] private Vector3 lastKnownPosition;
    [SerializeField] private Vector3 lastHeardPosition;
    [SerializeField] private float lastPerceptionTime = -9999f;
    private float _nextDecayCheckTime = 0f;
    [SerializeField] private float lastSeenTime = -9999f;
    [SerializeField] private float lastHeardTime = -9999f;
    [SerializeField] private float lastEffectiveNoise;
    [SerializeField] private float lastSightAwareness01;
    [SerializeField] private float visionAlertAccumulatedSeconds;
    [SerializeField] private float visionDetectAccumulatedSeconds;
    [SerializeField] private UnitDefinitionRuntimeBinder lastPerceivedTarget;
    [SerializeField] private UnitDefinitionRuntimeBinder lastHeardSource;

    [Header("Target Identity Memory")]
    [SerializeField] private UnitDefinitionRuntimeBinder lastVisionTarget;
    [SerializeField] private CharacterIdentity lastVisionTargetIdentity;
    [SerializeField] private UnitPerceptionTargetCategory lastVisionTargetCategory = UnitPerceptionTargetCategory.Unknown;
    [SerializeField] private UnitDefinitionRuntimeBinder lastDetectedTarget;
    [SerializeField] private CharacterIdentity lastDetectedTargetIdentity;
    [SerializeField] private UnitPerceptionTargetCategory lastDetectedTargetCategory = UnitPerceptionTargetCategory.Unknown;
    [SerializeField] private UnitDefinitionRuntimeBinder lastHearingTarget;
    [SerializeField] private CharacterIdentity lastHearingTargetIdentity;
    [SerializeField] private UnitPerceptionTargetCategory lastHearingTargetCategory = UnitPerceptionTargetCategory.Unknown;
    [SerializeField] private UnitDefinitionRuntimeBinder lastHostileTarget;
    [SerializeField] private UnitDefinitionRuntimeBinder lastFriendlyTarget;
    [SerializeField] private UnitDefinitionRuntimeBinder lastNeutralTarget;
    [SerializeField] private bool canSeeHostileUnit;
    [SerializeField] private bool canSeeFriendlyUnit;
    [SerializeField] private bool canSeeNeutralUnit;
    [SerializeField] private bool canSeeCreatureUnit;
    [SerializeField] private bool canHearHostileUnit;
    [SerializeField] private bool canHearFriendlyUnit;
    [SerializeField] private bool canHearNeutralUnit;
    [SerializeField] private bool canHearCreatureUnit;
    [SerializeField] private bool hasDetectedHostileUnit;
    [SerializeField] private bool hasDetectedFriendlyUnit;
    [SerializeField] private bool hasDetectedNeutralUnit;
    [SerializeField] private bool hasDetectedCreatureUnit;

    public UnitDefinitionRuntimeBinder RuntimeBinder => runtimeBinder;
    public UnitDefinition UnitDefinition => unitDefinition;

    public bool CanUseVision => canUseVision;
    public bool CanUseHearing => canUseHearing;
    public bool CanBePerceived => canBePerceived;
    public void SetCanBePerceived(bool value) { canBePerceived = value; }

    public float VisionMultiplier => visionMultiplier;
    public float VisionSuspicionThreshold => visionSuspicionThreshold;
    public float VisionAlertThreshold => visionAlertThreshold;
    public float VisionDetectThreshold => visionDetectThreshold;
    public float VisionAlertHoldSeconds => visionAlertHoldSeconds;
    public float VisionDetectHoldSeconds => visionDetectHoldSeconds;
    public float VisionMemorySeconds => visionMemorySeconds;
    public float LastSightAwareness01 => lastSightAwareness01;
    public float VisionAlertAccumulatedSeconds => visionAlertAccumulatedSeconds;
    public float VisionDetectAccumulatedSeconds => visionDetectAccumulatedSeconds;

    public float HearingMultiplier => hearingMultiplier;
    public float HearingBaseRange => hearingBaseRange;
    public float HearingMaxRange => hearingMaxRange;
    public float HearingSuspicionThreshold => hearingSuspicionThreshold;
    public float HearingAlertThreshold => hearingAlertThreshold;
    public float HearingDetectThreshold => hearingDetectThreshold;
    public float HearingMemorySeconds => hearingMemorySeconds;

    public UnitPerceptionLevel CurrentLevel => currentLevel;
    public bool IsSuspicious => isSuspicious;
    public bool IsAlert => isAlert;
    public bool HasDetectedTarget => hasDetectedTarget;
    public bool HasDetectedPlayer => hasDetectedPlayer || canSeePlayer || canHearPlayer;
    public bool CanSeePlayer => canSeePlayer;
    public bool CanHearPlayer => canHearPlayer;
    public bool HasLastKnownPosition => hasLastKnownPosition;
    public bool HasLastHeardPosition => hasLastHeardPosition;
    public Vector3 LastKnownPosition => lastKnownPosition;
    public Vector3 LastHeardPosition => lastHeardPosition;
    public float LastPerceptionTime => lastPerceptionTime;
    public float LastSeenTime => lastSeenTime;
    public float LastHeardTime => lastHeardTime;
    public float LastEffectiveNoise => lastEffectiveNoise;
    public UnitDefinitionRuntimeBinder LastPerceivedTarget => lastPerceivedTarget;
    public UnitDefinitionRuntimeBinder LastHeardSource => lastHeardSource;

    public UnitDefinitionRuntimeBinder LastVisionTarget => lastVisionTarget;
    public CharacterIdentity LastVisionTargetIdentity => lastVisionTargetIdentity;
    public UnitPerceptionTargetCategory LastVisionTargetCategory => lastVisionTargetCategory;
    public UnitDefinitionRuntimeBinder LastDetectedTarget => lastDetectedTarget;
    public CharacterIdentity LastDetectedTargetIdentity => lastDetectedTargetIdentity;
    public UnitPerceptionTargetCategory LastDetectedTargetCategory => lastDetectedTargetCategory;
    public UnitDefinitionRuntimeBinder LastHearingTarget => lastHearingTarget;
    public CharacterIdentity LastHearingTargetIdentity => lastHearingTargetIdentity;
    public UnitPerceptionTargetCategory LastHearingTargetCategory => lastHearingTargetCategory;
    public UnitDefinitionRuntimeBinder LastHostileTarget => lastHostileTarget;
    public UnitDefinitionRuntimeBinder LastFriendlyTarget => lastFriendlyTarget;
    public UnitDefinitionRuntimeBinder LastNeutralTarget => lastNeutralTarget;
    public bool CanSeeHostileUnit => canSeeHostileUnit;
    public bool CanSeeFriendlyUnit => canSeeFriendlyUnit;
    public bool CanSeeNeutralUnit => canSeeNeutralUnit;
    public bool CanSeeCreatureUnit => canSeeCreatureUnit;
    public bool CanHearHostileUnit => canHearHostileUnit;
    public bool CanHearFriendlyUnit => canHearFriendlyUnit;
    public bool CanHearNeutralUnit => canHearNeutralUnit;
    public bool CanHearCreatureUnit => canHearCreatureUnit;
    public bool HasDetectedHostileUnit => hasDetectedHostileUnit;
    public bool HasDetectedFriendlyUnit => hasDetectedFriendlyUnit;
    public bool HasDetectedNeutralUnit => hasDetectedNeutralUnit;
    public bool HasDetectedCreatureUnit => hasDetectedCreatureUnit;

    // 每帧视觉同步前调用，清空本帧即时视觉接触标志，不影响记忆/持续状态
    private bool visionFrameActive;

    // 全局自注册清单——给 PlayerCombatStateRuntime 这类"要扫一遍所有单位感知状态"
    // 的系统用，不用每次自己 FindObjectsOfType 扫全场景。
    public static readonly List<UnitPerceptionRuntime> ActiveInstances = new List<UnitPerceptionRuntime>();

    private void Awake()
    {
        ResolveReferences();
        BindDeathController();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (!ActiveInstances.Contains(this))
            ActiveInstances.Add(this);
    }

    private void OnDisable()
    {
        UnbindDeathController();
        ActiveInstances.Remove(this);
    }

    private void OnDestroy()
    {
        UnbindDeathController();
    }

    private void Update()
    {
        // 记忆衰减是秒级判断，不需要每帧执行
        if (Time.time >= _nextDecayCheckTime)
        {
            _nextDecayCheckTime = Time.time + 0.1f;
            DecayMemoryIfExpired();
        }
    }

    private void BindDeathController()
    {
        UnitDeathController dc = GetComponent<UnitDeathController>() ?? GetComponentInParent<UnitDeathController>();
        if (dc != null)
            dc.OnDeathStarted += HandleOwnerDeath;
    }

    private void UnbindDeathController()
    {
        UnitDeathController dc = GetComponent<UnitDeathController>() ?? GetComponentInParent<UnitDeathController>();
        if (dc != null)
            dc.OnDeathStarted -= HandleOwnerDeath;
    }

    private void HandleOwnerDeath(UnitDeathController _)
    {
        ClearTransientState();
    }

    public void ResolveReferences()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        if (unitDefinition == null && runtimeBinder != null)
            unitDefinition = runtimeBinder.UnitDefinitionAsset;
    }

    public void ConfigureFromDefinition(
        UnitDefinitionRuntimeBinder binder,
        UnitDefinition definition,
        bool runtimeEnabled,
        bool defaultCanUseVision,
        bool defaultCanUseHearing,
        bool defaultCanBePerceived)
    {
        runtimeBinder = binder != null ? binder : runtimeBinder;
        unitDefinition = definition != null ? definition : unitDefinition;

        canUseVision = ReadBool(unitDefinition, defaultCanUseVision, "canUseVision", "enableVisionPerception", "enableVisualPerception");
        canUseHearing = ReadBool(unitDefinition, defaultCanUseHearing, "canUseHearing", "enableHearingPerception", "enableHearing");
        canBePerceived = ReadBool(unitDefinition, defaultCanBePerceived, "canBePerceived", "canBeDetected", "isPerceivable");

        visionMultiplier = Mathf.Max(0f, ReadFloat(unitDefinition, visionMultiplier, "visionMultiplier", "sightMultiplier", "visualPerceptionMultiplier"));
        visionSuspicionThreshold = Mathf.Clamp01(ReadFloat(unitDefinition, visionSuspicionThreshold, "visionSuspicionThreshold", "sightSuspicionThreshold", "visualSuspicionThreshold"));
        visionAlertThreshold = Mathf.Clamp01(ReadFloat(unitDefinition, visionAlertThreshold, "visionAlertThreshold", "sightAlertThreshold", "visualAlertThreshold"));
        visionDetectThreshold = Mathf.Clamp01(ReadFloat(unitDefinition, visionDetectThreshold, "visionDetectThreshold", "sightDetectThreshold", "visualDetectThreshold", "visionDetectionThreshold"));

        visionAlertThreshold = Mathf.Max(visionSuspicionThreshold, visionAlertThreshold);
        visionDetectThreshold = Mathf.Max(visionAlertThreshold, visionDetectThreshold);

        visionAlertHoldSeconds = Mathf.Max(0f, ReadFloat(unitDefinition, visionAlertHoldSeconds, "visionAlertHoldSeconds", "sightAlertHoldSeconds"));
        visionDetectHoldSeconds = Mathf.Max(0f, ReadFloat(unitDefinition, visionDetectHoldSeconds, "visionDetectHoldSeconds", "sightDetectHoldSeconds"));
        visionMemorySeconds = Mathf.Max(0f, ReadFloat(unitDefinition, visionMemorySeconds, "visionMemorySeconds", "sightMemorySeconds", "perceptionMemorySeconds"));

        hearingMultiplier = Mathf.Max(0f, ReadFloat(unitDefinition, hearingMultiplier, "hearingMultiplier", "hearingSenseMultiplier", "hearingPower"));
        hearingBaseRange = Mathf.Max(0f, ReadFloat(unitDefinition, hearingBaseRange, "hearingBaseRange", "hearingNearRange"));
        hearingMaxRange = Mathf.Max(Mathf.Max(0.01f, hearingBaseRange), ReadFloat(unitDefinition, hearingMaxRange, "hearingMaxRange", "hearingRange"));
        hearingSuspicionThreshold = Mathf.Max(0f, ReadFloat(unitDefinition, hearingSuspicionThreshold, "hearingSuspicionThreshold", "hearingSuspiciousThreshold"));
        hearingAlertThreshold = Mathf.Max(hearingSuspicionThreshold, ReadFloat(unitDefinition, hearingAlertThreshold, "hearingAlertThreshold"));
        hearingDetectThreshold = Mathf.Max(hearingAlertThreshold, ReadFloat(unitDefinition, hearingDetectThreshold, "hearingDetectThreshold", "hearingDetectionThreshold"));
        hearingMemorySeconds = Mathf.Max(0f, ReadFloat(unitDefinition, hearingMemorySeconds, "hearingMemorySeconds", "perceptionMemorySeconds"));

        enabled = runtimeEnabled;

        if (!runtimeEnabled)
            ClearTransientState();
    }

    // Backward compatible hard visual contact setter.
    // Use ReportVisionPlayerAwareness() for layered suspicion / alert / detected vision.
    public void SetVisionPlayerState(bool visible, Vector3 playerPosition, UnitDefinitionRuntimeBinder playerBinder)
    {
        if (!canUseVision)
        {
            canSeePlayer = false;
            return;
        }

        if (visible)
        {
            canSeePlayer = true;
            hasDetectedPlayer = true;
            hasDetectedTarget = true;
            lastSeenTime = Time.time;
            lastPerceptionTime = Time.time;
            lastKnownPosition = playerPosition;
            hasLastKnownPosition = true;
            lastPerceivedTarget = playerBinder;
            lastVisionTarget = playerBinder;
            UpdateTargetIdentityMemory(playerBinder, true);
            Promote(UnitPerceptionLevel.Detected);
        }
        else
        {
            canSeePlayer = false;
            ResetCurrentVisionContactFlags();
            visionAlertAccumulatedSeconds = 0f;
            visionDetectAccumulatedSeconds = 0f;
            RebuildLevelFromFlags();
        }
    }

    public UnitPerceptionLevel ReportVisionPlayerAwareness(float sightAwareness01, Vector3 playerPosition, UnitDefinitionRuntimeBinder playerBinder)
    {
        return ReportVisionAwareness(playerBinder, playerPosition, sightAwareness01, true);
    }

    public UnitPerceptionLevel ReportVisionAwareness(UnitDefinitionRuntimeBinder target, Vector3 targetPosition, float sightAwareness01, bool targetIsPlayer)
    {
        if (!canUseVision || visionMultiplier <= 0f)
        {
            if (targetIsPlayer)
                canSeePlayer = false;

            ResetCurrentVisionContactFlags();
            lastSightAwareness01 = 0f;
            visionAlertAccumulatedSeconds = 0f;
            visionDetectAccumulatedSeconds = 0f;
            RebuildLevelFromFlags();
            return currentLevel;
        }

        float awareness = Mathf.Clamp01(sightAwareness01 * visionMultiplier);
        lastSightAwareness01 = awareness;

        bool hasTarget = target != null && target.UnitDefinitionAsset != null;
        bool isPlayerTarget = targetIsPlayer || IsIdentity(target, CharacterIdentity.Player);

        if (awareness < visionSuspicionThreshold || !hasTarget)
        {
            if (isPlayerTarget)
                canSeePlayer = false;

            ResetCurrentVisionContactFlags();
            visionAlertAccumulatedSeconds = 0f;
            visionDetectAccumulatedSeconds = 0f;
            RebuildLevelFromFlags();
            return currentLevel;
        }

        lastSeenTime = Time.time;
        lastPerceptionTime = Time.time;
        lastKnownPosition = targetPosition;
        hasLastKnownPosition = true;
        lastPerceivedTarget = target;
        lastVisionTarget = target;
        UpdateTargetIdentityMemory(target, false);

        UnitPerceptionLevel visualLevel = UnitPerceptionLevel.Suspicious;

        if (awareness >= visionAlertThreshold)
            visionAlertAccumulatedSeconds += Time.deltaTime;
        else
            visionAlertAccumulatedSeconds = 0f;

        if (awareness >= visionDetectThreshold)
            visionDetectAccumulatedSeconds += Time.deltaTime;
        else
            visionDetectAccumulatedSeconds = 0f;

        if (visionAlertAccumulatedSeconds >= visionAlertHoldSeconds)
            visualLevel = UnitPerceptionLevel.Alert;

        bool detected = visionDetectAccumulatedSeconds >= visionDetectHoldSeconds;
        if (detected)
        {
            visualLevel = UnitPerceptionLevel.Detected;
            hasDetectedTarget = true;
            lastDetectedTarget = target;
            UpdateTargetIdentityMemory(target, true);

            if (isPlayerTarget)
            {
                canSeePlayer = true;
                hasDetectedPlayer = true;
            }
        }
        else
        {
            // Suspicious / Alert: 本单位感知到了目标方向，但尚未完整确认。
            // 只清除本目标类别的即时接触标志，不影响听觉或其他类别视觉标志。
            if (isPlayerTarget)
                canSeePlayer = false;
        }

        Promote(visualLevel);
        return currentLevel;
    }

    /// <summary>
    /// 供 VisionManager 多目标同步：用于主目标之外的次要可见目标。
    /// 不触碰时间累加器，不提升感知等级，只按类别更新 canSee*/hasDetected* 标志。
    /// </summary>
    public void ReportAdditionalVisionContact(UnitDefinitionRuntimeBinder target, Vector3 targetPosition, float sightAwareness01)
    {
        if (!canUseVision || visionMultiplier <= 0f || target == null || target.UnitDefinitionAsset == null)
            return;

        float awareness = Mathf.Clamp01(sightAwareness01 * visionMultiplier);
        if (awareness < visionSuspicionThreshold)
            return;

        bool detected = awareness >= visionDetectThreshold;
        UpdateTargetIdentityMemory(target, detected);

        if (detected)
        {
            lastSeenTime = Time.time;
            lastPerceptionTime = Time.time;
            lastKnownPosition = targetPosition;
            hasLastKnownPosition = true;

            // 仅在比当前已有感知更高时提升等级
            hasDetectedTarget = true;
            Promote(UnitPerceptionLevel.Detected);
        }
        else
        {
            // 至少 Suspicious
            Promote(UnitPerceptionLevel.Suspicious);
        }
    }

    /// <summary>每次视觉同步帧开始时调用，清除上一帧的即时视觉接触标志（不影响记忆）。</summary>
    public void BeginVisionFrame()
    {
        // 清空即时 canSee* 标志，让本帧重新从零积累
        canSeePlayer = false;
        canSeeHostileUnit = false;
        canSeeFriendlyUnit = false;
        canSeeNeutralUnit = false;
        canSeeCreatureUnit = false;
        visionFrameActive = true;
    }

    public UnitPerceptionLevel ReportHeardSound(UnitDefinitionRuntimeBinder source, Vector3 soundPosition, float logicalNoise, bool sourceIsPlayer)
    {
        if (!canUseHearing || hearingMultiplier <= 0f || logicalNoise <= 0f)
            return currentLevel;

        float distance = Vector3.Distance(transform.position, soundPosition);
        if (distance > hearingMaxRange)
            return currentLevel;

        float distanceAttenuation;
        if (distance <= hearingBaseRange)
            distanceAttenuation = 1f;
        else
        {
            float denominator = Mathf.Max(0.01f, hearingMaxRange - hearingBaseRange);
            distanceAttenuation = Mathf.Clamp01(1f - ((distance - hearingBaseRange) / denominator));
        }

        float effectiveNoise = logicalNoise * hearingMultiplier * distanceAttenuation;
        lastEffectiveNoise = effectiveNoise;

        UnitPerceptionLevel heardLevel = UnitPerceptionLevel.None;
        if (effectiveNoise >= hearingDetectThreshold)
            heardLevel = UnitPerceptionLevel.Detected;
        else if (effectiveNoise >= hearingAlertThreshold)
            heardLevel = UnitPerceptionLevel.Alert;
        else if (effectiveNoise >= hearingSuspicionThreshold)
            heardLevel = UnitPerceptionLevel.Suspicious;

        if (heardLevel == UnitPerceptionLevel.None)
            return currentLevel;

        lastHeardTime = Time.time;
        lastPerceptionTime = Time.time;
        lastHeardPosition = soundPosition;
        lastKnownPosition = soundPosition;
        hasLastHeardPosition = true;
        hasLastKnownPosition = true;
        lastHeardSource = source;
        lastPerceivedTarget = source;

        bool isPlayerSource = sourceIsPlayer || IsIdentity(source, CharacterIdentity.Player);
        UpdateHearingTargetMemory(source, heardLevel == UnitPerceptionLevel.Detected, isPlayerSource);

        if (isPlayerSource && heardLevel == UnitPerceptionLevel.Detected)
        {
            canHearPlayer = true;
            hasDetectedPlayer = true;
        }

        if (heardLevel == UnitPerceptionLevel.Detected && source != null)
        {
            hasDetectedTarget = true;
            lastDetectedTarget = source;
            UpdateTargetIdentityMemory(source, true);
        }

        Promote(heardLevel);
        return currentLevel;
    }

    public void ForceSetLevel(UnitPerceptionLevel level, Vector3 position, UnitDefinitionRuntimeBinder target)
    {
        currentLevel = level;
        isSuspicious = level >= UnitPerceptionLevel.Suspicious;
        isAlert = level >= UnitPerceptionLevel.Alert;
        hasDetectedTarget = level >= UnitPerceptionLevel.Detected;
        lastKnownPosition = position;
        hasLastKnownPosition = true;
        lastPerceptionTime = Time.time;
        lastPerceivedTarget = target;
        UpdateTargetIdentityMemory(target, level >= UnitPerceptionLevel.Detected);
    }

    public void ForceSetPlayerDetected(Vector3 position, UnitDefinitionRuntimeBinder playerBinder)
    {
        canSeePlayer = true;
        hasDetectedTarget = true;
        hasDetectedPlayer = true;
        ForceSetLevel(UnitPerceptionLevel.Detected, position, playerBinder);
    }

    public void ClearTransientState()
    {
        currentLevel = UnitPerceptionLevel.None;
        isSuspicious = false;
        isAlert = false;
        hasDetectedTarget = false;
        hasDetectedPlayer = false;
        canSeePlayer = false;
        canHearPlayer = false;
        hasLastKnownPosition = false;
        hasLastHeardPosition = false;
        lastEffectiveNoise = 0f;
        lastSightAwareness01 = 0f;
        visionAlertAccumulatedSeconds = 0f;
        visionDetectAccumulatedSeconds = 0f;
        lastPerceivedTarget = null;
        lastHeardSource = null;
        lastVisionTarget = null;
        lastDetectedTarget = null;
        lastHearingTarget = null;
        lastHostileTarget = null;
        lastFriendlyTarget = null;
        lastNeutralTarget = null;
        lastVisionTargetCategory = UnitPerceptionTargetCategory.Unknown;
        lastDetectedTargetCategory = UnitPerceptionTargetCategory.Unknown;
        lastHearingTargetCategory = UnitPerceptionTargetCategory.Unknown;
        ResetCurrentVisionContactFlags();
        ResetCurrentHearingContactFlags();
        hasDetectedHostileUnit = false;
        hasDetectedFriendlyUnit = false;
        hasDetectedNeutralUnit = false;
        hasDetectedCreatureUnit = false;
    }

    private void Promote(UnitPerceptionLevel level)
    {
        if (level > currentLevel)
            currentLevel = level;

        isSuspicious = currentLevel >= UnitPerceptionLevel.Suspicious;
        isAlert = currentLevel >= UnitPerceptionLevel.Alert;
        hasDetectedTarget = hasDetectedTarget || currentLevel >= UnitPerceptionLevel.Detected;
    }

    private void RebuildLevelFromFlags()
    {
        if (canSeePlayer || canHearPlayer || hasDetectedTarget || hasDetectedPlayer)
            Promote(UnitPerceptionLevel.Detected);
        else if (isAlert)
            currentLevel = UnitPerceptionLevel.Alert;
        else if (isSuspicious)
            currentLevel = UnitPerceptionLevel.Suspicious;
        else
            currentLevel = UnitPerceptionLevel.None;
    }

    private void DecayMemoryIfExpired()
    {
        if (currentLevel == UnitPerceptionLevel.None)
            return;

        float memory = Mathf.Max(visionMemorySeconds, hearingMemorySeconds);
        if (memory <= 0f)
            return;

        if (Time.time - lastPerceptionTime <= memory)
            return;

        ClearTransientState();
    }

    private void ResetCurrentVisionContactFlags()
    {
        canSeePlayer = false;
        canSeeHostileUnit = false;
        canSeeFriendlyUnit = false;
        canSeeNeutralUnit = false;
        canSeeCreatureUnit = false;
    }

    private void ResetCurrentHearingContactFlags()
    {
        canHearPlayer = false;
        canHearHostileUnit = false;
        canHearFriendlyUnit = false;
        canHearNeutralUnit = false;
        canHearCreatureUnit = false;
    }

    private void UpdateHearingTargetMemory(UnitDefinitionRuntimeBinder source, bool markDetected, bool forcePlayerSource)
    {
        if (source == null || source.UnitDefinitionAsset == null)
            return;

        CharacterIdentity identity = forcePlayerSource ? CharacterIdentity.Player : source.UnitDefinitionAsset.characterIdentity;
        CharacterIdentity selfId = runtimeBinder?.UnitDefinitionAsset?.characterIdentity ?? CharacterIdentity.Player;
        UnitPerceptionTargetCategory category = ResolveTargetCategory(identity, selfId);

        lastHearingTarget = source;
        lastHearingTargetIdentity = identity;
        lastHearingTargetCategory = category;

        switch (category)
        {
            case UnitPerceptionTargetCategory.Player:
                lastHostileTarget = source;
                canHearPlayer = true;
                if (markDetected)
                    hasDetectedPlayer = true;
                break;

            case UnitPerceptionTargetCategory.Friendly:
                lastFriendlyTarget = source;
                canHearFriendlyUnit = true;
                if (markDetected)
                    hasDetectedFriendlyUnit = true;
                break;

            case UnitPerceptionTargetCategory.Hostile:
                lastHostileTarget = source;
                canHearHostileUnit = true;
                if (markDetected)
                    hasDetectedHostileUnit = true;
                break;

            case UnitPerceptionTargetCategory.NeutralPassive:
            case UnitPerceptionTargetCategory.NeutralHostile:
                lastNeutralTarget = source;
                canHearNeutralUnit = true;
                if (markDetected)
                {
                    hasDetectedNeutralUnit = true;
                    if (category == UnitPerceptionTargetCategory.NeutralHostile)
                        hasDetectedHostileUnit = true;
                }
                if (category == UnitPerceptionTargetCategory.NeutralHostile)
                    canHearHostileUnit = true;
                break;

            case UnitPerceptionTargetCategory.Creature:
                canHearCreatureUnit = true;
                if (markDetected)
                    hasDetectedCreatureUnit = true;
                break;
        }
    }

    private void UpdateTargetIdentityMemory(UnitDefinitionRuntimeBinder target, bool markDetected)
    {
        if (target == null || target.UnitDefinitionAsset == null)
            return;

        CharacterIdentity identity = target.UnitDefinitionAsset.characterIdentity;
        CharacterIdentity selfId = runtimeBinder?.UnitDefinitionAsset?.characterIdentity ?? CharacterIdentity.Player;
        UnitPerceptionTargetCategory category = ResolveTargetCategory(identity, selfId);

        lastVisionTargetIdentity = identity;
        lastVisionTargetCategory = category;

        if (markDetected)
        {
            lastDetectedTarget = target;
            lastDetectedTargetIdentity = identity;
            lastDetectedTargetCategory = category;
        }

        switch (category)
        {
            case UnitPerceptionTargetCategory.Player:
                lastHostileTarget = target;
                if (markDetected)
                {
                    canSeePlayer = true;
                    hasDetectedPlayer = true;
                }
                break;

            case UnitPerceptionTargetCategory.Friendly:
                lastFriendlyTarget = target;
                if (markDetected)
                {
                    canSeeFriendlyUnit = true;
                    hasDetectedFriendlyUnit = true;
                }
                break;

            case UnitPerceptionTargetCategory.Hostile:
                lastHostileTarget = target;
                if (markDetected)
                {
                    canSeeHostileUnit = true;
                    hasDetectedHostileUnit = true;
                }
                break;

            case UnitPerceptionTargetCategory.NeutralPassive:
            case UnitPerceptionTargetCategory.NeutralHostile:
                lastNeutralTarget = target;
                if (markDetected)
                {
                    canSeeNeutralUnit = true;
                    hasDetectedNeutralUnit = true;
                    if (category == UnitPerceptionTargetCategory.NeutralHostile)
                    {
                        canSeeHostileUnit = true;
                        hasDetectedHostileUnit = true;
                    }
                }
                break;

            case UnitPerceptionTargetCategory.Creature:
                if (markDetected)
                {
                    canSeeCreatureUnit = true;
                    hasDetectedCreatureUnit = true;
                }
                break;
        }
    }

    private static readonly CharacterIdentity[] EnemyIdentities =
    {
        CharacterIdentity.Enemy, CharacterIdentity.Elite, CharacterIdentity.Boss
    };

    private static bool IsEnemyFaction(CharacterIdentity id)
    {
        foreach (var e in EnemyIdentities) if (id == e) return true;
        return false;
    }

    private static UnitPerceptionTargetCategory ResolveTargetCategory(CharacterIdentity targetIdentity, CharacterIdentity selfIdentity = CharacterIdentity.Player)
    {
        // 同为敌方阵营（Enemy/Elite/Boss）→ 视为友方，不互相敌对
        if (IsEnemyFaction(selfIdentity) && IsEnemyFaction(targetIdentity))
            return UnitPerceptionTargetCategory.Friendly;

        switch (targetIdentity)
        {
            case CharacterIdentity.Player:
                return UnitPerceptionTargetCategory.Player;
            case CharacterIdentity.Ally:
                return UnitPerceptionTargetCategory.Friendly;
            case CharacterIdentity.Enemy:
            case CharacterIdentity.Elite:
            case CharacterIdentity.Boss:
                return UnitPerceptionTargetCategory.Hostile;
            case CharacterIdentity.NeutralPassive:
                return UnitPerceptionTargetCategory.NeutralPassive;
            case CharacterIdentity.NeutralHostile:
                return UnitPerceptionTargetCategory.NeutralHostile;
            case CharacterIdentity.Creature:
                return UnitPerceptionTargetCategory.Creature;
            default:
                return UnitPerceptionTargetCategory.Unknown;
        }
    }

    private static bool IsIdentity(UnitDefinitionRuntimeBinder binder, CharacterIdentity identity)
    {
        return binder != null && binder.UnitDefinitionAsset != null && binder.UnitDefinitionAsset.characterIdentity == identity;
    }

    private static bool ReadBool(object source, bool fallback, params string[] names)
    {
        object value;
        return TryReadMember(source, out value, names) && value is bool ? (bool)value : fallback;
    }

    private static float ReadFloat(object source, float fallback, params string[] names)
    {
        object value;
        if (!TryReadMember(source, out value, names) || value == null)
            return fallback;

        if (value is float) return (float)value;
        if (value is int) return (int)value;
        if (value is double) return (float)(double)value;
        if (value is long) return (long)value;
        return fallback;
    }

    private static bool TryReadMember(object source, out object value, params string[] names)
    {
        value = null;
        if (source == null || names == null)
            return false;

        Type type = source.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            if (string.IsNullOrEmpty(name))
                continue;

            FieldInfo field = type.GetField(name, flags);
            if (field != null)
            {
                value = field.GetValue(source);
                return true;
            }

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                value = property.GetValue(source, null);
                return true;
            }
        }

        return false;
    }
}
