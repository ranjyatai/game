using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class RuntimeAnomalyEntry
{
    public string anomalyKey = "";
    public float currentAccumulation = 0f;
    public float threshold = 100f;
    public float decayDelayRemaining = 0f;
}

[DisallowMultipleComponent]
public class UnitAnomalyController : MonoBehaviour
{
    [SerializeField] private UnitDefinition ownerDefinition;
    [SerializeField] private BattleParameterDatabase battleParameterDatabase;
    [SerializeField] private UnitStatusController statusController;
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private List<RuntimeAnomalyEntry> runtimeEntries = new List<RuntimeAnomalyEntry>();

    public UnitDefinition OwnerDefinition => ownerDefinition;
    public BattleParameterDatabase BattleParameterDatabase => battleParameterDatabase;
    public IReadOnlyList<RuntimeAnomalyEntry> RuntimeEntries => runtimeEntries;

    public static UnitAnomalyController EnsureOnRoot(GameObject unitRoot)
    {
        if (unitRoot == null)
            return null;

        UnitAnomalyController controller = unitRoot.GetComponent<UnitAnomalyController>();
        if (controller == null)
            controller = unitRoot.AddComponent<UnitAnomalyController>();

        controller.EnsureInternalState();
        return controller;
    }

    private void Awake()
    {
        EnsureInternalState();
        AutoSetup();
    }

    private void Update()
    {
        if (!Application.isPlaying)
            return;

        Tick(Time.deltaTime);
    }

    private void Reset()
    {
        AutoSetup();
    }

    public void EnsureInitialized(UnitDefinition definition)
    {
        ownerDefinition = definition;
        AutoSetup();
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (statusController == null)
            statusController = GetComponent<UnitStatusController>();

        if (ownerDefinition != null)
            battleParameterDatabase = ResolveBattleParameterDatabase(ownerDefinition);
    }

    public float ApplyAccumulation(string anomalyKey, float inputAmount, GameObject sourceUnit = null)
    {
        if (string.IsNullOrWhiteSpace(anomalyKey) || inputAmount <= 0f)
            return 0f;

        EnsureInternalState();
        AutoSetup();

        BattleParameterDatabase database = battleParameterDatabase;
        if (database == null)
            return 0f;

        ElementAnomalyDefinition anomalyDefinition = database.FindElementAnomaly(anomalyKey);
        if (anomalyDefinition == null)
            return 0f;

        float categoryMultiplier = database.ResolveCategoryAnomalyReceiveMultiplier(ownerDefinition);
        float unitMultiplier = ownerDefinition != null ? ownerDefinition.ClampedAnomalyReceiveMultiplier : 1f;
        float rate = Mathf.Max(0f, anomalyDefinition.defaultAnomalyAccumulationRate);
        float finalAmount = inputAmount * rate * categoryMultiplier * unitMultiplier;

        if (finalAmount <= 0f)
            return 0f;

        RuntimeAnomalyEntry entry = GetOrCreateEntry(anomalyKey);
        entry.threshold = ResolveThreshold(anomalyDefinition);
        entry.currentAccumulation = Mathf.Clamp(entry.currentAccumulation + finalAmount, 0f, entry.threshold);
        entry.decayDelayRemaining = Mathf.Max(0f, database.anomalyDecayDelay);

        if (debugLogs)
        {
            Debug.Log($"[UnitAnomalyController] {name} anomaly={anomalyKey} input={inputAmount} rate={rate} category={categoryMultiplier} unit={unitMultiplier} final={finalAmount} current={entry.currentAccumulation}/{entry.threshold}", this);
        }

        TryTriggerFullAnomaly(anomalyDefinition, sourceUnit, entry);
        return finalAmount;
    }

    public void Tick(float deltaTime)
    {
        if (runtimeEntries == null || runtimeEntries.Count == 0)
            return;

        // 归零 / 无效条目必须无条件清理。
        // 不能放在 anomalyDecayPerSecond 判断之后，否则衰减速度为 0 时，
        // 已经触发完成并归零的异常条目会残留在 runtimeEntries 中。
        CleanupInvalidOrEmptyEntries();

        if (deltaTime <= 0f || runtimeEntries.Count == 0)
            return;

        BattleParameterDatabase database = battleParameterDatabase;
        if (database == null)
            return;

        float decaySpeed = Mathf.Max(0f, database.anomalyDecayPerSecond);
        if (decaySpeed <= 0f)
            return;

        for (int i = runtimeEntries.Count - 1; i >= 0; i--)
        {
            RuntimeAnomalyEntry entry = runtimeEntries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.anomalyKey))
            {
                runtimeEntries.RemoveAt(i);
                continue;
            }

            if (entry.currentAccumulation <= 0f)
            {
                runtimeEntries.RemoveAt(i);
                continue;
            }

            if (entry.decayDelayRemaining > 0f)
            {
                entry.decayDelayRemaining = Mathf.Max(0f, entry.decayDelayRemaining - deltaTime);
                continue;
            }

            entry.currentAccumulation = Mathf.Max(0f, entry.currentAccumulation - decaySpeed * deltaTime);
            if (entry.currentAccumulation <= 0f)
                runtimeEntries.RemoveAt(i);
        }
    }

    public float GetAccumulation01(string anomalyKey)
    {
        RuntimeAnomalyEntry entry = FindEntry(anomalyKey);
        if (entry == null || entry.threshold <= 0f)
            return 0f;

        return Mathf.Clamp01(entry.currentAccumulation / entry.threshold);
    }

    public void ClearAllAccumulations()
    {
        if (runtimeEntries == null)
            return;

        runtimeEntries.Clear();
    }

    private void TryTriggerFullAnomaly(ElementAnomalyDefinition anomalyDefinition, GameObject sourceUnit, RuntimeAnomalyEntry entry)
    {
        if (anomalyDefinition == null || entry == null)
            return;

        if (entry.currentAccumulation + 0.0001f < entry.threshold)
            return;

        if (anomalyDefinition.enableAnomalyStatusOnFull
            && anomalyDefinition.grantedStatusDefinition != null
            && statusController != null)
        {
            statusController.ApplyStatus(anomalyDefinition.grantedStatusDefinition, sourceUnit);
        }

        // 异常满值已经转化为状态后，累计条目本身应立即清掉。
        // 这里不依赖 Tick，也不依赖 anomalyDecayPerSecond，避免 0 值条目残留。
        entry.currentAccumulation = 0f;
        entry.decayDelayRemaining = 0f;

        if (runtimeEntries != null)
            runtimeEntries.Remove(entry);

        if (debugLogs)
            Debug.Log($"[UnitAnomalyController] {name} anomaly triggered -> {anomalyDefinition.key}", this);
    }

    private void CleanupInvalidOrEmptyEntries()
    {
        if (runtimeEntries == null || runtimeEntries.Count == 0)
            return;

        for (int i = runtimeEntries.Count - 1; i >= 0; i--)
        {
            RuntimeAnomalyEntry entry = runtimeEntries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.anomalyKey) || entry.currentAccumulation <= 0f)
                runtimeEntries.RemoveAt(i);
        }
    }

    private float ResolveThreshold(ElementAnomalyDefinition anomalyDefinition)
    {
        if (anomalyDefinition == null)
            return 100f;

        StatusDefinition grantedStatus = anomalyDefinition.grantedStatusDefinition;
        if (grantedStatus != null && grantedStatus.accumulationThreshold > 0f)
            return grantedStatus.accumulationThreshold;

        return 100f;
    }

    private RuntimeAnomalyEntry FindEntry(string anomalyKey)
    {
        if (runtimeEntries == null)
            return null;

        for (int i = 0; i < runtimeEntries.Count; i++)
        {
            RuntimeAnomalyEntry entry = runtimeEntries[i];
            if (entry != null && entry.anomalyKey == anomalyKey)
                return entry;
        }

        return null;
    }

    private RuntimeAnomalyEntry GetOrCreateEntry(string anomalyKey)
    {
        RuntimeAnomalyEntry existing = FindEntry(anomalyKey);
        if (existing != null)
            return existing;

        RuntimeAnomalyEntry created = new RuntimeAnomalyEntry();
        created.anomalyKey = anomalyKey;
        runtimeEntries.Add(created);
        return created;
    }

    private void EnsureInternalState()
    {
        if (runtimeEntries == null)
            runtimeEntries = new List<RuntimeAnomalyEntry>();
    }

    private BattleParameterDatabase ResolveBattleParameterDatabase(UnitDefinition definition)
    {
        if (definition != null && definition.battleParameterDatabase != null)
            return definition.battleParameterDatabase;

        BattleParameterDatabase[] databases = Resources.LoadAll<BattleParameterDatabase>("");
        for (int i = 0; i < databases.Length; i++)
        {
            BattleParameterDatabase db = databases[i];
            if (db != null && db.isRuntimeActive)
                return db;
        }

        return databases.Length > 0 ? databases[0] : null;
    }
}
