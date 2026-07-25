using System;
using UnityEngine;

/// <summary>
/// Sky Prison formal runtime resource component for unit Load / LP.
///
/// Responsibilities:
/// - Own current load runtime value.
/// - Read max / recovery / cost values from UnitDefinition.parameterValues.
/// - Provide safe spend checks for dodge and sprint.
///
/// It does NOT read keyboard input and does NOT move the unit.
/// ActionController / MovementController ask this component before executing actions.
/// </summary>
[DefaultExecutionOrder(9050)]
public class UnitLoadRuntime : MonoBehaviour
{
    [Header("Version")]
    [SerializeField] private string scriptVersion = "V5 - 2026-05-26 - reads existing BattleDefinitionsWorkspace LP keys";

    [Header("Definition Defaults")]
    [SerializeField] private float fallbackMaxLoad = 100f;
    [SerializeField] private float fallbackLoadRecoverRate = 20f;
    [SerializeField] private float fallbackLoadRecoverDelay = 0.65f;
    [Tooltip("负荷值被耗尽到 0 时使用的特殊恢复延迟。用于避免体力归零后按住奔跑产生一抽一抽的反复切换。")]
    [SerializeField] private float fallbackExhaustedLoadRecoverDelay = 4.5f;
    [SerializeField] private float fallbackDodgeLoadCost = 25f;
    [SerializeField] private float fallbackSprintLoadCostPerSecond = 15f;
    [Tooltip("负荷耗尽后，至少恢复到多少点才允许再次开始奔跑。0 表示只要大于 0 就可以。建议保留 3-8，避免刚恢复一点点又立刻被 Shift 抽干。")]
    [SerializeField] private float fallbackSprintResumeLoadAfterExhausted = 5f;

    [Header("Runtime")]
    [SerializeField] private float maxLoad = 100f;
    [SerializeField] private float currentLoad = 100f;
    [SerializeField] private float loadRecoverRate = 20f;
    [SerializeField] private float loadRecoverDelay = 0.65f;
    [SerializeField] private float exhaustedLoadRecoverDelay = 4.5f;
    [SerializeField] private float dodgeLoadCost = 25f;
    [SerializeField] private float sprintLoadCostPerSecond = 15f;
    [SerializeField] private float sprintResumeLoadAfterExhausted = 5f;
    [SerializeField] private bool recoverAutomatically = true;
    [SerializeField] private bool resetToMaxOnFirstDefinitionApply = true;

    [Header("Debug")]
    [SerializeField] private float recoverDelayTimer = 0f;
    [SerializeField] private bool exhaustedSprintLock = false;
    [SerializeField] private string lastSpendReason = "";
    [SerializeField] private bool lastSpendSucceeded = false;

    private bool hasAppliedDefinitionOnce = false;

    public string Version => scriptVersion;
    public float MaxLoad => Mathf.Max(0f, maxLoad);
    public float CurrentLoad => Mathf.Clamp(currentLoad, 0f, MaxLoad);
    public float LoadRecoverRate => Mathf.Max(0f, loadRecoverRate);
    public float LoadRecoverDelay => Mathf.Max(0f, loadRecoverDelay);
    public float ExhaustedLoadRecoverDelay => Mathf.Max(LoadRecoverDelay, exhaustedLoadRecoverDelay);
    public float DodgeLoadCost => Mathf.Max(0f, dodgeLoadCost);
    public float SprintLoadCostPerSecond => Mathf.Max(0f, sprintLoadCostPerSecond);
    public float SprintResumeLoadAfterExhausted => Mathf.Clamp(sprintResumeLoadAfterExhausted, 0f, MaxLoad);
    public float NormalizedLoad => MaxLoad <= 0.0001f ? 0f : Mathf.Clamp01(CurrentLoad / MaxLoad);
    public bool IsRecoveringBlocked => recoverDelayTimer > 0f;
    public bool IsExhaustedSprintLocked => exhaustedSprintLock;
    public bool IsLoadExhaustedPenaltyActive => exhaustedSprintLock;
    public bool CanUseLoadConsumingAction => !exhaustedSprintLock;
    public bool HasAnyLoad => CurrentLoad > 0.0001f;
    public bool CanDodge => CanSpendLoadAction(DodgeLoadCost);

    /// <summary>
    /// Sprint is blocked when load has been exhausted to 0. The lock releases only after
    /// the exhausted delay has elapsed and current load has recovered to the resume threshold.
    /// This prevents the run/walk twitch caused by holding sprint at 0 load.
    /// </summary>
    public bool CanSprint
    {
        get
        {
            if (SprintLoadCostPerSecond <= 0f)
                return true;

            if (exhaustedSprintLock)
                return CurrentLoad + 0.0001f >= SprintResumeLoadAfterExhausted;

            return HasAnyLoad;
        }
    }

    private void Awake()
    {
        ClampRuntimeValues();
    }

    private void OnEnable()
    {
        ClampRuntimeValues();
    }

    private void Update()
    {
        TickRecover(Time.deltaTime);
    }

    public void ApplyDefinition(UnitDefinition definition)
    {
        float oldMax = Mathf.Max(0f, maxLoad);
        float oldCurrent = Mathf.Clamp(currentLoad, 0f, oldMax);
        float oldRatio = oldMax > 0.0001f ? oldCurrent / oldMax : 1f;

        // 正式路线：
        // 这些 key 已经由 SkyPrisonBattleDefinitionsWorkspace 管理。
        // UnitLoadRuntime 只读取 UnitDefinition.parameterValues，不再要求 BattleParameterModels/Database 追加新字段。
        maxLoad = ReadParameter(definition, fallbackMaxLoad, "maxLp", "maxLoad", "maximumLoad", "最大负荷值");

        float explicitCurrent = ReadParameter(definition, float.NaN, "lp", "load", "currentLp", "currentLoad", "负荷值LP", "负荷值");

        // 回复：优先走 属性定义页的 base + rate 体系。
        // 如果旧单位还没有 lpRecoveryBase，只填了 lpRecoveryRate，则把 lpRecoveryRate 暂作每秒回复值兼容，避免突然变成 20% * fallback。
        float lpRecoveryBase;
        if (TryReadParameter(definition, out lpRecoveryBase, "lpRecoveryBase", "loadRecoveryBase", "负荷值回复基础值"))
        {
            float lpRecoveryRate = ReadParameter(definition, 100f, "lpRecoveryRate", "loadRecoveryRate", "loadRecoverRate", "负荷值回复率");
            loadRecoverRate = Mathf.Max(0f, lpRecoveryBase * NormalizePercentOrMultiplier(lpRecoveryRate, 100f));
        }
        else
        {
            loadRecoverRate = ReadParameter(definition, fallbackLoadRecoverRate, "lpRecoveryRate", "loadRecoveryRate", "loadRecoverRate", "负荷值回复率");
        }

        // 恢复延迟：优先 base + rate。旧字段 lpRecoveryDelay 只作为兼容 fallback。
        float lpRecoverDelayBase;
        if (TryReadParameter(definition, out lpRecoverDelayBase, "lpRecoveryDelayBase", "loadRecoveryDelayBase", "负荷值恢复延迟基础值", "恢复延迟基础值"))
        {
            float lpRecoverDelayRate = ReadParameter(definition, 100f, "lpRecoveryDelayRate", "loadRecoveryDelayRate", "负荷值恢复延迟倍率", "恢复延迟倍率");
            loadRecoverDelay = Mathf.Max(0f, lpRecoverDelayBase * NormalizePercentOrMultiplier(lpRecoverDelayRate, 100f));
        }
        else
        {
            loadRecoverDelay = ReadParameter(definition, fallbackLoadRecoverDelay, "lpRecoveryDelay", "lpRecoverDelay", "loadRecoverDelay", "loadRecoveryDelay", "负荷值恢复延迟", "恢复延迟时间");
        }

        // 消耗：正式属性定义体系是 Rate，优先按 maxLp 百分比计算。
        // 旧测试字段 dodgeLpCost / sprintLpCost 只在没有 Rate 时兜底。
        float dodgeRate;
        if (TryReadParameter(definition, out dodgeRate, "dodgeLpCostRate", "dodgeLoadCostRate", "闪避消耗率", "闪避负荷消耗率"))
            dodgeLoadCost = Mathf.Max(0f, maxLoad * NormalizePercentOrMultiplier(dodgeRate, 100f));
        else
            dodgeLoadCost = ReadParameter(definition, fallbackDodgeLoadCost, "dodgeLpCost", "dodgeLoadCost", "dodgeCost", "闪避消耗", "闪避负荷消耗");

        float sprintRate;
        if (TryReadParameter(definition, out sprintRate, "sprintLpCostRate", "sprintLoadCostRate", "冲刺消耗率", "冲刺负荷消耗率"))
            sprintLoadCostPerSecond = Mathf.Max(0f, maxLoad * NormalizePercentOrMultiplier(sprintRate, 100f));
        else
            sprintLoadCostPerSecond = ReadParameter(definition, fallbackSprintLoadCostPerSecond, "sprintLpCost", "sprintLoadCost", "sprintCost", "冲刺消耗", "冲刺负荷消耗");

        // 负荷耗尽是惩罚状态。当前属性定义页暂时没有正式 key，所以这里只读兼容 key；
        // 没填时沿用 runtime fallback 4.5 秒，避免归零后按住 Shift 一抽一抽。
        exhaustedLoadRecoverDelay = ReadParameter(
            definition,
            fallbackExhaustedLoadRecoverDelay,
            "exhaustedLpRecoverDelay",
            "exhaustedLpRecoveryDelay",
            "emptyLpRecoverDelay",
            "exhaustedLoadRecoverDelay",
            "emptyLoadRecoverDelay",
            "负荷耗尽恢复延迟",
            "负荷值耗尽恢复延迟",
            "空负荷恢复延迟");

        sprintResumeLoadAfterExhausted = ReadParameter(
            definition,
            fallbackSprintResumeLoadAfterExhausted,
            "sprintResumeLpAfterExhausted",
            "sprintResumeLoadAfterExhausted",
            "sprintResumeLoad",
            "负荷耗尽后奔跑恢复阈值",
            "负荷值耗尽后奔跑恢复阈值",
            "奔跑恢复负荷阈值");

        maxLoad = Mathf.Max(0f, maxLoad);
        loadRecoverRate = Mathf.Max(0f, loadRecoverRate);
        loadRecoverDelay = Mathf.Max(0f, loadRecoverDelay);
        exhaustedLoadRecoverDelay = Mathf.Max(loadRecoverDelay, exhaustedLoadRecoverDelay);
        dodgeLoadCost = Mathf.Max(0f, dodgeLoadCost);
        sprintLoadCostPerSecond = Mathf.Max(0f, sprintLoadCostPerSecond);
        sprintResumeLoadAfterExhausted = Mathf.Clamp(sprintResumeLoadAfterExhausted, 0f, maxLoad);

        // 在单位定义里，当前值 lp 留 0 通常表示未填写。
        // 首次应用时按 maxLp 填满，避免出生即负荷耗尽。
        if (!float.IsNaN(explicitCurrent) && explicitCurrent > 0f)
            currentLoad = explicitCurrent;
        else if (!hasAppliedDefinitionOnce && resetToMaxOnFirstDefinitionApply)
            currentLoad = maxLoad;
        else if (oldMax > 0.0001f && maxLoad > 0.0001f)
            currentLoad = maxLoad * Mathf.Clamp01(oldRatio);
        else
            currentLoad = Mathf.Min(currentLoad, maxLoad);

        ClampRuntimeValues();
        hasAppliedDefinitionOnce = true;
    }

    private static float NormalizePercentOrMultiplier(float value, float fallbackPercent)
    {
        if (float.IsNaN(value))
            value = fallbackPercent;

        value = Mathf.Max(0f, value);

        // 编辑器里百分比字段通常以 100 表示 100%。
        // 如果以后某处直接传 0~1 倍率，也兼容。
        return value > 1f ? value * 0.01f : value;
    }

    public void ApplyLoadDefinition(
        float newMaxLoad,
        float newLoadRecoverRate,
        float newLoadRecoverDelay,
        float newDodgeLoadCost,
        float newSprintLoadCostPerSecond,
        bool resetCurrentToMax = false)
    {
        ApplyLoadDefinition(
            newMaxLoad,
            newLoadRecoverRate,
            newLoadRecoverDelay,
            Mathf.Max(newLoadRecoverDelay, fallbackExhaustedLoadRecoverDelay),
            newDodgeLoadCost,
            newSprintLoadCostPerSecond,
            fallbackSprintResumeLoadAfterExhausted,
            resetCurrentToMax);
    }

    public void ApplyLoadDefinition(
        float newMaxLoad,
        float newLoadRecoverRate,
        float newLoadRecoverDelay,
        float newExhaustedLoadRecoverDelay,
        float newDodgeLoadCost,
        float newSprintLoadCostPerSecond,
        float newSprintResumeLoadAfterExhausted,
        bool resetCurrentToMax = false)
    {
        float oldMax = Mathf.Max(0f, maxLoad);
        float oldRatio = oldMax > 0.0001f ? Mathf.Clamp01(currentLoad / oldMax) : 1f;

        maxLoad = Mathf.Max(0f, newMaxLoad);
        loadRecoverRate = Mathf.Max(0f, newLoadRecoverRate);
        loadRecoverDelay = Mathf.Max(0f, newLoadRecoverDelay);
        exhaustedLoadRecoverDelay = Mathf.Max(loadRecoverDelay, newExhaustedLoadRecoverDelay);
        dodgeLoadCost = Mathf.Max(0f, newDodgeLoadCost);
        sprintLoadCostPerSecond = Mathf.Max(0f, newSprintLoadCostPerSecond);
        sprintResumeLoadAfterExhausted = Mathf.Clamp(newSprintResumeLoadAfterExhausted, 0f, maxLoad);

        currentLoad = resetCurrentToMax ? maxLoad : maxLoad * oldRatio;
        ClampRuntimeValues();
        hasAppliedDefinitionOnce = true;
    }

    public bool CanSpend(float amount)
    {
        amount = Mathf.Max(0f, amount);
        if (amount <= 0f)
            return true;

        return CurrentLoad > 0.0001f;
    }

    /// <summary>
    /// Formal load-consuming action gate.
    /// When load has reached 0, the unit enters an exhausted penalty state.
    /// During this state, consuming actions such as sprint / dodge / future weapon actions are forbidden,
    /// even if a tiny amount of load has started to recover.
    /// </summary>
    public bool CanSpendLoadAction(float amount)
    {
        amount = Mathf.Max(0f, amount);
        if (amount <= 0f)
            return true;

        if (exhaustedSprintLock)
            return false;

        // LP > 0 时始终允许再用一次（哪怕剩余量不够完整消耗）
        return CurrentLoad > 0.0001f;
    }

    public bool TrySpend(float amount, string reason = "Spend")
    {
        return TrySpendLoadAction(amount, reason);
    }

    public bool TrySpendLoadAction(float amount, string reason = "Spend")
    {
        amount = Mathf.Max(0f, amount);
        lastSpendReason = reason;

        if (!CanSpendLoadAction(amount))
        {
            lastSpendSucceeded = false;
            return false;
        }

        currentLoad = Mathf.Clamp(currentLoad - amount, 0f, MaxLoad);
        NotifyLoadUsed(reason, CurrentLoad <= 0.0001f);
        lastSpendSucceeded = true;
        return true;
    }

    public bool TrySpendDodge()
    {
        return TrySpendLoadAction(DodgeLoadCost, "Dodge");
    }

    /// <summary>
    /// Sprint is continuous. It is allowed while the unit has load and is not in exhausted sprint lock,
    /// then drains up to the available amount. If cost is 0, sprint is free.
    /// </summary>
    public bool TrySpendSprint(float deltaTime)
    {
        float costPerSecond = SprintLoadCostPerSecond;
        if (costPerSecond <= 0f)
            return true;

        if (!CanSprint)
        {
            lastSpendReason = "Sprint";
            lastSpendSucceeded = false;
            return false;
        }

        float amount = Mathf.Max(0f, costPerSecond * Mathf.Max(0f, deltaTime));
        currentLoad = Mathf.Clamp(currentLoad - Mathf.Min(amount, CurrentLoad), 0f, MaxLoad);
        NotifyLoadUsed("Sprint", CurrentLoad <= 0.0001f);
        lastSpendSucceeded = true;
        return true;
    }

    public void NotifyLoadUsed(string reason = "Used")
    {
        NotifyLoadUsed(reason, CurrentLoad <= 0.0001f);
    }

    private void NotifyLoadUsed(string reason, bool exhausted)
    {
        lastSpendReason = reason;

        if (exhausted)
        {
            currentLoad = 0f;
            exhaustedSprintLock = true;
            recoverDelayTimer = ExhaustedLoadRecoverDelay;
            return;
        }

        recoverDelayTimer = LoadRecoverDelay;
    }

    public void TickRecover(float deltaTime)
    {
        if (!recoverAutomatically)
            return;

        if (MaxLoad <= 0f)
        {
            currentLoad = 0f;
            recoverDelayTimer = 0f;
            exhaustedSprintLock = false;
            return;
        }

        if (recoverDelayTimer > 0f)
        {
            recoverDelayTimer = Mathf.Max(0f, recoverDelayTimer - Mathf.Max(0f, deltaTime));
            return;
        }

        if (LoadRecoverRate <= 0f)
            return;

        currentLoad = Mathf.Clamp(currentLoad + LoadRecoverRate * Mathf.Max(0f, deltaTime), 0f, MaxLoad);

        if (exhaustedSprintLock && CurrentLoad + 0.0001f >= SprintResumeLoadAfterExhausted)
            exhaustedSprintLock = false;
    }

    public void SetCurrentLoad(float value)
    {
        currentLoad = Mathf.Clamp(value, 0f, MaxLoad);

        if (currentLoad <= 0.0001f)
            exhaustedSprintLock = true;
        else if (currentLoad + 0.0001f >= SprintResumeLoadAfterExhausted)
            exhaustedSprintLock = false;
    }

    public void RestoreToFull()
    {
        currentLoad = MaxLoad;
        recoverDelayTimer = 0f;
        exhaustedSprintLock = false;
    }

    public void ClearLoad()
    {
        currentLoad = 0f;
        recoverDelayTimer = ExhaustedLoadRecoverDelay;
        exhaustedSprintLock = true;
    }

    private void ClampRuntimeValues()
    {
        maxLoad = Mathf.Max(0f, maxLoad);
        currentLoad = Mathf.Clamp(currentLoad, 0f, maxLoad);
        loadRecoverRate = Mathf.Max(0f, loadRecoverRate);
        loadRecoverDelay = Mathf.Max(0f, loadRecoverDelay);
        exhaustedLoadRecoverDelay = Mathf.Max(loadRecoverDelay, exhaustedLoadRecoverDelay);
        dodgeLoadCost = Mathf.Max(0f, dodgeLoadCost);
        sprintLoadCostPerSecond = Mathf.Max(0f, sprintLoadCostPerSecond);
        sprintResumeLoadAfterExhausted = Mathf.Clamp(sprintResumeLoadAfterExhausted, 0f, maxLoad);
        recoverDelayTimer = Mathf.Max(0f, recoverDelayTimer);

        if (currentLoad <= 0.0001f && hasAppliedDefinitionOnce)
            exhaustedSprintLock = true;
        else if (currentLoad + 0.0001f >= SprintResumeLoadAfterExhausted)
            exhaustedSprintLock = false;
    }

    private static bool TryReadParameter(UnitDefinition definition, out float result, params string[] keys)
    {
        result = 0f;

        if (definition == null || definition.parameterValues == null || keys == null)
            return false;

        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            if (string.IsNullOrWhiteSpace(key))
                continue;

            for (int j = 0; j < definition.parameterValues.Count; j++)
            {
                UnitParameterValue value = definition.parameterValues[j];
                if (value == null)
                    continue;

                if (string.Equals(value.parameterKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    result = value.value;
                    return true;
                }
            }
        }

        return false;
    }

    private static float ReadParameter(UnitDefinition definition, float fallback, params string[] keys)
    {
        if (definition == null || definition.parameterValues == null || keys == null)
            return fallback;

        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            if (string.IsNullOrWhiteSpace(key))
                continue;

            for (int j = 0; j < definition.parameterValues.Count; j++)
            {
                UnitParameterValue value = definition.parameterValues[j];
                if (value == null)
                    continue;

                if (string.Equals(value.parameterKey, key, StringComparison.OrdinalIgnoreCase))
                    return value.value;
            }
        }

        return fallback;
    }
}
