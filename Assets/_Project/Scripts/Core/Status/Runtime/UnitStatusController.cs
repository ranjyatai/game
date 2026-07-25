using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class UnitStatusController : MonoBehaviour
{
    public const int MaxStatusCapacity = 999;
    private const float StatusExpireEpsilon = 0.0001f;

    [SerializeField] private UnitDefinition ownerDefinition;
    [SerializeField] private List<RuntimeStatusEntry> activeStatuses = new List<RuntimeStatusEntry>();
    [SerializeField] private UnitHealthController healthController;
    [SerializeField] private UnitDeathController deathController;
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private bool autoTickInUpdate = true;

    [Header("Runtime Tick Diagnostic - Read Only")]
    [SerializeField] private string runtimeTickDiagnostic = "-";
    [SerializeField] private int runtimeAutoTickCount;
    [SerializeField] private int runtimeExternalTickCount;
    [SerializeField] private bool runtimeAutoTickEnabledSnapshot;

    private readonly Dictionary<string, RuntimeStatusEntry> statusByKey = new Dictionary<string, RuntimeStatusEntry>();
    private readonly List<StatusViewData> visibleStatusCache = new List<StatusViewData>();

    public event Action StatusesChanged;
    public event Action<RuntimeStatusEntry, float> DotDamageApplied;

    public UnitDefinition OwnerDefinition => ownerDefinition;
    public IReadOnlyList<RuntimeStatusEntry> ActiveStatuses => activeStatuses;
    public bool AutoTickInUpdate => autoTickInUpdate;
    public IReadOnlyList<StatusViewData> VisibleStatuses
    {
        get
        {
            ClearVisibleCacheIfNoActiveStatuses(false);
            return visibleStatusCache;
        }
    }

    public static UnitStatusController EnsureOnRoot(GameObject unitRoot)
    {
        if (unitRoot == null)
            return null;

        UnitStatusController controller = unitRoot.GetComponent<UnitStatusController>();
        if (controller == null)
            controller = unitRoot.AddComponent<UnitStatusController>();

        controller.EnsureInternalState();
        return controller;
    }

    private void Reset()
    {
        AutoSetup();
    }

    private void Awake()
    {
        AutoSetup();
        EnsureInternalState();
        RebuildLookup();
        RebuildVisibleCache();
    }

    public void SetAutoTickInUpdate(bool enabled, string reason = "")
    {
        if (autoTickInUpdate == enabled)
            return;

        autoTickInUpdate = enabled;
        runtimeAutoTickEnabledSnapshot = autoTickInUpdate;
        runtimeTickDiagnostic = string.IsNullOrWhiteSpace(reason)
            ? $"AutoTickInUpdate={autoTickInUpdate}"
            : $"AutoTickInUpdate={autoTickInUpdate} | reason={reason}";
    }

    public void TickStatusesExternal(float deltaTime, string reason = "")
    {
        runtimeExternalTickCount++;
        runtimeTickDiagnostic = string.IsNullOrWhiteSpace(reason)
            ? $"External Tick #{runtimeExternalTickCount}"
            : $"External Tick #{runtimeExternalTickCount} | reason={reason}";

        TickStatuses(deltaTime);
    }

    private void Update()
    {
        runtimeAutoTickEnabledSnapshot = autoTickInUpdate;

        if (!Application.isPlaying || !autoTickInUpdate)
            return;

        runtimeAutoTickCount++;
#if UNITY_EDITOR
        // 纯 Inspector 调试字段——每个挂了状态系统的单位、每一帧都在拼这个字符串，
        // 跟"哪怕站着不动GC也一直很高"这个症状完全对得上（这个Update不看玩家是否
        // 在动，只要autoTickInUpdate开着就每帧跑）。Build里没人看这个字段，跳过。
        runtimeTickDiagnostic = $"Auto Update Tick #{runtimeAutoTickCount}";
#endif
        TickStatuses(Time.deltaTime);
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (healthController == null)
            healthController = GetComponent<UnitHealthController>();

        if (deathController == null)
        {
            deathController = GetComponent<UnitDeathController>();
            if (deathController != null)
            {
                deathController.OnDeathStarted -= OnUnitDied;
                deathController.OnDeathStarted += OnUnitDied;
            }
        }
    }

    private void OnDestroy()
    {
        if (deathController != null)
            deathController.OnDeathStarted -= OnUnitDied;
    }

    private void OnUnitDied(UnitDeathController _)
    {
        ClearAllStatuses();
    }

    public void EnsureInitialized(UnitDefinition definition)
    {
        ownerDefinition = definition;
        AutoSetup();
        EnsureInternalState();
        RebuildLookup();
        RebuildVisibleCache();
    }

    public bool HasStatus(string statusKey)
    {
        if (string.IsNullOrWhiteSpace(statusKey))
            return false;

        return statusByKey.ContainsKey(statusKey);
    }

    public RuntimeStatusEntry GetStatus(string statusKey)
    {
        if (string.IsNullOrWhiteSpace(statusKey))
            return null;

        RuntimeStatusEntry entry;
        return statusByKey.TryGetValue(statusKey, out entry) ? entry : null;
    }

    public RuntimeStatusEntry ApplyStatus(StatusDefinition definition, GameObject sourceUnit = null, int requestedStacks = 1, float durationOverride = -1f)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.statusId))
            return null;

        if (deathController != null && !deathController.IsAlive)
            return null;

        EnsureInternalState();

        RuntimeStatusEntry existing;
        if (statusByKey.TryGetValue(definition.statusId, out existing) && existing != null)
        {
            existing.RefreshStacks(requestedStacks);
            existing.RefreshDuration(durationOverride);
            existing.pendingRemove = false;
            RebuildVisibleCache();
            RaiseStatusesChanged();
            return existing;
        }

        if (activeStatuses.Count >= MaxStatusCapacity)
        {
            Debug.LogWarning($"[UnitStatusController] 状态容量已达上限 {MaxStatusCapacity}，无法继续添加：{definition.statusId}", this);
            return null;
        }

        RuntimeStatusEntry entry = new RuntimeStatusEntry();
        entry.Initialize(definition, sourceUnit, Mathf.Max(definition.baseStack, requestedStacks), durationOverride);
        activeStatuses.Add(entry);
        statusByKey[entry.statusKey] = entry;
        RebuildVisibleCache();
        RaiseStatusesChanged();
        return entry;
    }

    public bool RemoveStatus(string statusKey)
    {
        RuntimeStatusEntry entry = GetStatus(statusKey);
        if (entry == null)
            return false;

        bool removed = activeStatuses.Remove(entry);
        if (removed)
        {
            statusByKey.Remove(statusKey);
            RebuildVisibleCache();
            RaiseStatusesChanged();
        }
        return removed;
    }

    public void ClearAllStatuses()
    {
        bool hadRuntimeStatuses = activeStatuses != null && activeStatuses.Count > 0;
        bool hadVisibleCache = visibleStatusCache.Count > 0;

        if (activeStatuses != null)
            activeStatuses.Clear();

        statusByKey.Clear();
        visibleStatusCache.Clear();

        if (hadRuntimeStatuses || hadVisibleCache)
            RaiseStatusesChanged();
    }

    public void TickStatuses(float deltaTime)
    {
        EnsureInternalState();

        if (deltaTime <= 0f)
        {
            ClearVisibleCacheIfNoActiveStatuses(true);
            return;
        }

        if (activeStatuses.Count == 0)
        {
            ClearVisibleCacheIfNoActiveStatuses(true);
            return;
        }

        bool dirty = false;
        // 单纯的持续时间倒数（没有周期触发/DOT的普通限时状态）之前完全不会让dirty=true，
        // 导致 RebuildVisibleCache/RaiseStatusesChanged 全程不跑一次——HUD上的状态图标
        // 进度条只在 StatusesChanged 事件里刷新，等于永远看不到条在变短，直到状态到期
        // 一瞬间才跳变消失，表现就是"进度条一直是满的、像定格了"。有可见限时状态时
        // 必须每帧都当作dirty，进度条才能跟着时间正常缩短。
        bool hasVisibleLimitedDuration = false;

        for (int i = activeStatuses.Count - 1; i >= 0; i--)
        {
            RuntimeStatusEntry entry = activeStatuses[i];
            if (entry == null || !entry.IsValid)
            {
                activeStatuses.RemoveAt(i);
                dirty = true;
                continue;
            }

            entry.ApplyDeltaTime(deltaTime);

            if (entry.HasVisibleLimitedDuration)
                hasVisibleLimitedDuration = true;

            int triggerTickCount;
            if (entry.TryConsumeTriggerTick(out triggerTickCount) && triggerTickCount > 0)
                dirty = true;

            int dotTickCount;
            if (entry.TryConsumeDotTick(out dotTickCount) && dotTickCount > 0)
            {
                ApplyDotTicks(entry, dotTickCount);
                dirty = true;
            }

            if (entry.pendingRemove || entry.IsExpired || ShouldExpireByDuration(entry))
            {
                entry.remainingDuration = 0f;

                statusByKey.Remove(entry.statusKey);
                activeStatuses.RemoveAt(i);
                dirty = true;
            }
        }

        if (!dirty && !hasVisibleLimitedDuration)
            return;

        RebuildLookup();
        RebuildVisibleCache();
        RaiseStatusesChanged();
    }

    public float GetTotalRegainRatio()
    {
        float total = 0f;

        for (int i = 0; i < activeStatuses.Count; i++)
        {
            RuntimeStatusEntry entry = activeStatuses[i];
            if (entry == null || entry.definition == null)
                continue;

            StatusDefinition def = entry.definition;
            if (!def.enableRegain || def.regainRatio <= 0f)
                continue;

            int stack = Mathf.Max(1, entry.stackCount);
            total += def.regainRatio * stack;
        }

        return Mathf.Max(0f, total);
    }

    private void ApplyDotTicks(RuntimeStatusEntry entry, int tickCount)
    {
        if (entry == null || entry.definition == null || tickCount <= 0)
            return;

        StatusDefinition definition = entry.definition;
        if (!definition.enableDot)
            return;

        if (definition.dotTargetResource != StatusDotTargetResource.HP)
        {
            if (debugLogs)
                Debug.Log($"[UnitStatusController] DOT 目标资源 {definition.dotTargetResource} 还未接入，状态：{definition.statusId}", this);
            return;
        }

        UnitHealthController targetHealth = ResolveHealthController();
        if (targetHealth == null)
            return;

        for (int tickIndex = 0; tickIndex < tickCount; tickIndex++)
        {
            float amount = EvaluateDotDamage(definition, entry, targetHealth);
            if (amount <= 0f)
                continue;

            if (!definition.dotCanKill)
            {
                float maxAllowed = Mathf.Max(0f, targetHealth.CurrentHealth - 1f);
                amount = Mathf.Min(amount, maxAllowed);
                if (amount <= 0f)
                    continue;
            }

            // 第一版：DOT 不进入 regain 池
            targetHealth.ApplyDamage(amount, 0f);
            DotDamageApplied?.Invoke(entry, amount);

            if (debugLogs)
                Debug.Log($"[UnitStatusController] DOT -> {definition.statusId} tickDamage={amount} target={targetHealth.name}", this);
        }
    }

    private float EvaluateDotDamage(StatusDefinition definition, RuntimeStatusEntry entry, UnitHealthController targetHealth)
    {
        switch (definition.dotValueMode)
        {
            case StatusDotValueMode.Fixed:
                return ApplyDotStackRule(definition, entry, Mathf.Max(0f, definition.dotBaseValue));

            case StatusDotValueMode.TargetMaxResourcePercent:
                return Mathf.Max(0f, targetHealth.MaxHealth * Mathf.Max(0f, definition.dotPercentValue));

            case StatusDotValueMode.TargetCurrentResourcePercent:
                return Mathf.Max(0f, targetHealth.CurrentHealth * Mathf.Max(0f, definition.dotPercentValue));

            case StatusDotValueMode.OwnerAttributeRatio:
            case StatusDotValueMode.TargetAttributeRatio:
                if (debugLogs)
                    Debug.Log($"[UnitStatusController] DOT 数值模式 {definition.dotValueMode} 尚未接入，先返回 0。状态：{definition.statusId}", this);
                return 0f;

            default:
                return 0f;
        }
    }

    private float ApplyDotStackRule(StatusDefinition definition, RuntimeStatusEntry entry, float baseAmount)
    {
        if (baseAmount <= 0f)
            return 0f;

        int extraStacks = Mathf.Max(0, entry.stackCount - 1);

        switch (definition.dotStackMode)
        {
            case StatusDotStackMode.None:
                return baseAmount;

            case StatusDotStackMode.MultiplyByStack:
                return baseAmount * Mathf.Max(1, entry.stackCount);

            case StatusDotStackMode.LinearAdd:
                return baseAmount + definition.dotStackAddValue * extraStacks;

            default:
                return baseAmount;
        }
    }

    private UnitHealthController ResolveHealthController()
    {
        if (healthController == null)
            healthController = GetComponent<UnitHealthController>();

        return healthController;
    }

    private void OnValidate()
    {
        AutoSetup();
        EnsureInternalState();
        RebuildLookup();
        RebuildVisibleCache();
    }

    private void EnsureInternalState()
    {
        if (activeStatuses == null)
            activeStatuses = new List<RuntimeStatusEntry>();
    }

    private void RebuildLookup()
    {
        statusByKey.Clear();
        for (int i = 0; i < activeStatuses.Count; i++)
        {
            RuntimeStatusEntry entry = activeStatuses[i];
            if (entry == null || !entry.IsValid)
                continue;
            statusByKey[entry.statusKey] = entry;
        }
    }

    private void RebuildVisibleCache()
    {
        visibleStatusCache.Clear();
        for (int i = 0; i < activeStatuses.Count; i++)
        {
            RuntimeStatusEntry entry = activeStatuses[i];
            if (entry == null || entry.definition == null)
                continue;
            if (entry.definition.isHidden || !entry.definition.showInHud)
                continue;
            if (entry.definition.grantMode == StatusGrantMode.PersistentPassive)
                continue;
            if (!entry.HasVisibleLimitedDuration)
                continue;

            float normalizedDuration = Mathf.Clamp01(entry.remainingDuration / entry.displayTotalDuration);

            visibleStatusCache.Add(new StatusViewData
            {
                key = entry.statusKey,
                displayName = entry.definition.displayName,
                icon = entry.definition.icon,
                visible = true,
                stacks = entry.stackCount,
                remainingNormalized = normalizedDuration,
                hideDurationMask = false,
                durationFillMode = StatusDurationFillMode.FollowDisplayDirection
            });
        }
    }

    private bool ShouldExpireByDuration(RuntimeStatusEntry entry)
    {
        if (entry == null || entry.definition == null)
            return true;

        if (entry.definition.durationType != StatusDurationType.Timed)
            return false;

        return entry.remainingDuration <= StatusExpireEpsilon;
    }

    private void ClearVisibleCacheIfNoActiveStatuses(bool notifyWhenChanged)
    {
        bool hasActiveStatuses = activeStatuses != null && activeStatuses.Count > 0;
        if (hasActiveStatuses || visibleStatusCache.Count == 0)
            return;

        visibleStatusCache.Clear();

        if (notifyWhenChanged)
            RaiseStatusesChanged();
    }

    private void RaiseStatusesChanged()
    {
        StatusesChanged?.Invoke();
    }
}
